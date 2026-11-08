#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Godot;

namespace DemoFactory.GugaFramework.DebugBridge;

/// <summary>
/// DebugBridge 宿主（GugaFramework 通用件）— 游戏进程内的 localhost TCP 调试通道。
///
/// 定位（2026-09-23 四阶段调试基建计划 Phase 1，已落地）：
/// - 把 eval 从「默认通道」降级为「兜底通道」：操控台/重放/快照/动态求值全部经
///   本桥直达，不再依赖 MCP game_eval（分类器拦截 / 窗口节流 / GDScript 子集三层死法）。
/// - 结构上与 ReplayRunner 同形：通用层只管**传输与分发**，零游戏知识；
///   词表与快照口径由原型侧注入（Inject/Snapshot/AdvanceTo 三委托 + 动词处理器）。
/// - **默认关**：未配置端口就不启动监听，零开销；仅 127.0.0.1，开发期不出货。
///
/// 协议（JSON 行，一发请求一行 → 一发响应一行）：
///   请求：{"id":1,"verb":"snapshot","args":{...}}
///   响应：{"id":1,"ok":true,"data":...}     （data 为 JSON 值；失败时 data=错误文本）
///
/// 主线程纪律（Godot API 只能在主线程摸）：后台线程只做「收一行」，执行一律
/// CallDeferred 泵回主线程；响应在主线程写回（Socket 写无 Godot API，安全）。
///
/// 单客户端（红线「不做多客户端」）：同一时刻只服务一个连接；新连接进来时旧连接
/// 先被替换（CLI 一次动词一次短连接，天然串行）。
///
/// Journal 纪律（计划红线）：写操作必须走 inject 动词（词表进 Journal），读操作随意；
/// call/eval 动词由原型侧在接线时约束（本层只提供反射/求值机制，不懂语义）。
/// </summary>
public partial class DebugBridgeHost : Node
{
    /// <summary>端口环境变量名（headless 自启：agent 设此变量拉游戏即可接 bridge）。</summary>
    public const string PortEnvVar = "DEBUG_BRIDGE_PORT";

    /// <summary>命令行端口参数名（--debug-bridge-port=7777，OS.GetCmdlineUserArgs 侧）。</summary>
    private const string PortArg = "--debug-bridge-port";

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    /// <summary>动词注册表：verb → 处理器（args JSON → 响应 data JSON）。</summary>
    private readonly Dictionary<string, Func<JsonElement, object?>> _verbs = new();

    /// <summary>call/eval 可达对象表（通用层只认 object，零游戏知识）。</summary>
    private readonly Dictionary<string, object> _targets = new();

    /// <summary>eval 求值器（原型侧注入的委托 — 见 DebugBridgeEval；通用层零 Roslyn 知识）。</summary>
    private Func<string, object?>? _evaluator;

    private long _nextId;
    private int _busy;

    /// <summary>本桥实际监听的端口；0 = 没开（未配置端口，或监听失败）。ping 回读用。</summary>
    private int _port;

    /// <summary>宿主 <c>_Ready</c> 时刻（unix 秒）—— ping 回读用：同机几局并存时要分得清连的是哪一局。</summary>
    private long _startedUnix;

    public override void _Ready()
    {
        _startedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int? port = ResolvePort();
        if (port is null)
        {
            GD.Print("[DebugBridge] 未配置端口（DEBUG_BRIDGE_PORT / --debug-bridge-port），通道关闭。");
            return;
        }
        if (Start((int)port)) return;

        // 端口已被占 = 明确误操作：端口即寻址，CLI 连上的必然是先起的那一局。
        // 留一局没有桥的进程下去，它会吃掉 CPU、抢画面，而读数、注入、重放全作用在旧世界上
        // （「日志里没有」≠「没发生」同族）。所以当场退出：撞号从「静默跑偏」变成硬失败。
        // 没配端口的局（出图、review / skill）走上面那条 return，不受影响。
        Callable.From(() => GetTree()?.Quit(2)).CallDeferred();
    }

    public override void _ExitTree()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* 已停即目标达成 */ }
        try { _client?.Close(); } catch { /* 已关即目标达成 */ }
    }

    /// <summary>解析端口：环境变量优先，命令行参数兜底。未配置返回 null（默认关）。</summary>
    private int? ResolvePort()
    {
        var env = OS.GetEnvironment(PortEnvVar);
        if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var p) && p > 0) return p;

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(PortArg + "=", StringComparison.Ordinal)
                && int.TryParse(arg[(PortArg.Length + 1)..], out var ap) && ap > 0)
                return ap;
        }
        return null;
    }

    /// <summary>原型侧接线口：注册词表动词 / call 可达对象 / eval 求值器。必须在 _Ready 前配好。</summary>
    public void Register(string verb, Func<JsonElement, object?> handler) => _verbs[verb] = handler;

    /// <summary>call 动词的可达对象（如 "main" → Main 节点）。public 方法/属性经反射触达。</summary>
    public void Expose(string name, object target) => _targets[name] = target;

    /// <summary>eval 求值器（原型侧注入 — Roslyn 求值细节封在 DebugBridgeEval）。</summary>
    public void SetEvaluator(Func<string, object?> evaluator) => _evaluator = evaluator;

    private bool Start(int port)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            _listener.Start();
        }
        catch (Exception e)
        {
            GD.PushError($"[DebugBridge] 端口 {port} 监听失败：{e.Message}（通道不可用，本局将退出）");
            _port = 0;
            return false;
        }
        _port = port;
        var thread = new System.Threading.Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "DebugBridge.Accept",
        };
        thread.Start();
        GD.Print($"[DebugBridge] 通道开启 127.0.0.1:{port}（单客户端，JSON 行协议）"
            + $"  pid={OS.GetProcessId()} 起局={_startedUnix}");
        return true;
    }

    /// <summary>后台线程：逐连接串行服务。每收一行 → CallDeferred 泵回主线程执行。</summary>
    private void AcceptLoop()
    {
        var token = _cts!.Token;
        var reader = new StreamReader(Stream.Null, Encoding.UTF8);
        while (!token.IsCancellationRequested)
        {
            TcpClient? accepted = null;
            try
            {
                accepted = _listener!.AcceptTcpClient();
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break; // 关停期的正常异常
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested) break;
                GD.PushWarning($"[DebugBridge] accept 失败：{e.Message}");
                continue;
            }

            // 单客户端：换连接时关旧的（旧连接的读线程随流关闭自然退出）
            _client?.Close();
            _client = accepted;
            var stream = _client.GetStream();
            _stream = stream;

            try
            {
                var lines = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while (!token.IsCancellationRequested && (line = lines.ReadLine()) is not null)
                {
                    if (line.Length == 0) continue;
                    PumpToMain(line);
                }
            }
            catch (Exception)
            {
                // 客户端断开是常态（CLI 短连接），静默换下一轮 accept
            }
            if (_client == accepted) _stream = null;
        }
    }

    /// <summary>把一行请求交给主线程执行（CallDeferred — Godot 消息队列，线程安全）。</summary>
    private void PumpToMain(string line)
    {
        var id = System.Threading.Interlocked.Increment(ref _nextId);
        CallDeferred(MethodName.ExecuteDeferred, id, line);
    }

    /// <summary>主线程：解析请求 → 分发动词 → 写回响应（写回无引擎 API，线程安全）。</summary>
    private void ExecuteDeferred(long id, string line)
    {
        (object? data, bool ok) result;
        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var verb = root.TryGetProperty("verb", out var v) ? v.GetString() ?? "" : "";
            var args = root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object
                ? a
                : JsonSerializer.SerializeToElement(new { });

            if (!_verbs.TryGetValue(verb, out var handler))
            {
                result = (new Dictionary<string, object?>
                {
                    ["error"] = $"unknown verb: {verb}",
                    ["verbs"] = string.Join(",", _verbs.Keys),
                }, false);
            }
            else
            {
                result = (handler(args), true);
            }
        }
        catch (Exception e)
        {
            result = (new Dictionary<string, object?>
            {
                ["error"] = e.Message,
                ["type"] = e.GetType().Name,
            }, false);
        }

        // 覆盖 CallDeferred 带来的自增 id：协议 id 以请求里的为准（客户端配对用）
        long responseId = id;
        try
        {
            var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out var idEl)
                && idEl.TryGetInt64(out var requested)) responseId = requested;
        }
        catch { /* 解析失败走自增 id，响应同样 ok=false 已带错误信息 */ }

        WriteResponse(responseId, result.data, result.ok);
    }

    /// <summary>写回一行响应（NetworkStream 无引擎 API，主线程调用即可）。</summary>
    private void WriteResponse(long id, object? data, bool ok)
    {
        var stream = _stream;
        if (stream is null || !stream.CanWrite) return;
        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["ok"] = ok,
                ["data"] = data,
            });
            var bytes = Encoding.UTF8.GetBytes(payload + "\n");
            stream.Write(bytes);
            stream.Flush();
        }
        catch (Exception)
        {
            // 客户端已断开 — 下一轮 accept 清场，无需上抛
        }
    }

    // === call / eval 的通用实现（机制在通用层，语义由原型侧词表与红线约束） ===

    /// <summary>
    /// call：反射调已注册对象（Expose）的 public 方法/属性。args: {"target":"main",
    /// "method":"SetSpeed","args":[20]}。参数按方法签名做 JSON→CLR 简单转换
    /// （string/int/long/bool/double/float）；返回值 JSON 化（基元直通，其余 ToString）。
    /// </summary>
    public object? CallVerb(JsonElement args)
    {
        var target = args.Str("target", required: true);
        var method = args.Str("method", required: true);
        if (!_targets.TryGetValue(target, out var obj))
            throw new ArgumentException($"未注册对象：{target}（已注册：{string.Join(",", _targets.Keys)}）");

        var jsonArgs = args.TryGetProperty("args", out var a)
            ? a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().ToArray()
                : new[] { a } // 单参数标量兼容（CLI --args 20 序列化为标量）
            : Array.Empty<JsonElement>();

        var flags = BindingFlags.Public | BindingFlags.Instance;
        var prop = obj.GetType().GetProperty(method, flags);
        if (prop is { GetMethod: not null })
            return Jsonify(prop.GetValue(obj));

        var candidates = obj.GetType().GetMethods(flags)
            .Where(m => !m.IsSpecialName && m.Name == method)
            .Where(m => m.GetParameters().Length == jsonArgs.Length)
            .ToList();
        if (candidates.Count == 0)
            throw new MissingMethodException(obj.GetType().Name, method);

        var converted = new object?[jsonArgs.Length];
        var chosen = candidates.FirstOrDefault(m => TryConvertAll(m, jsonArgs, converted))
            ?? candidates[0];
        TryConvertAll(chosen, jsonArgs, converted);
        return Jsonify(chosen.Invoke(obj, converted));
    }

    private static bool TryConvertAll(MethodInfo m, JsonElement[] jsonArgs, object?[] target)
    {
        var ps = m.GetParameters();
        for (var i = 0; i < ps.Length; i++)
        {
            if (!TryConvert(jsonArgs[i], ps[i].ParameterType, out var value)) return false;
            target[i] = value;
        }
        return true;
    }

    /// <summary>JSON 值 → CLR 基元（call 反射参数的简单转换面）。</summary>
    private static bool TryConvert(JsonElement value, Type type, out object? result)
    {
        try
        {
            if (type == typeof(string)) { result = value.ToString(); return true; }
            if (type == typeof(int)) { result = value.GetInt32(); return true; }
            if (type == typeof(long)) { result = value.GetInt64(); return true; }
            if (type == typeof(double)) { result = value.GetDouble(); return true; }
            if (type == typeof(float)) { result = value.GetSingle(); return true; }
            if (type == typeof(bool)) { result = value.GetBoolean(); return true; }
            result = null;
            return false;
        }
        catch (FormatException)
        {
            result = null;
            return false;
        }
    }

    /// <summary>CLR 值 → JSON 可序列化形态：基元与向量直通，集合逐元素投影，其余 ToString 兜底。</summary>
    private static object? Jsonify(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b,
        int or long or short or byte or uint or ulong or ushort or sbyte => value,
        double or float or decimal => value,
        System.Numerics.Vector2 v => new { X = v.X, Y = v.Y },
        Godot.Vector2 v => new { X = v.X, Y = v.Y },
        Godot.Vector3 v => new { X = v.X, Y = v.Y, Z = v.Z },
        // 集合层投影（2026-09-28 接上）：LINQ 查询产物此前一路走到 ToString，读回来是
        // "System.Collections.Generic.List`1[System.Int32]" 这种类型名 —— 等于没读出来。
        // 逐元素回本方法，所以数组里的向量照样拿到 {X,Y}；字符串在上一臂就被截住了，不会当集合摊开。
        System.Collections.IEnumerable batch => ProjectElements(batch),
        _ => value.ToString(),
    };

    /// <summary>集合逐元素走同一个投影（元素是复合对象时落 ToString 兜底）。</summary>
    private static List<object?> ProjectElements(System.Collections.IEnumerable source)
    {
        var items = new List<object?>();
        foreach (var item in source) items.Add(Jsonify(item));
        return items;
    }

    /// <summary>
    /// eval：进程内求值任意 C# 表达式（Roslyn CSharpScript，由原型侧注入求值器）。
    /// 主线程纪律同上：求值器在主线程执行；返回值 JSON 化。
    /// </summary>
    public object? EvalVerb(JsonElement args)
    {
        if (_evaluator is null)
            throw new InvalidOperationException("eval 求值器未注入（原型侧接线缺失）");
        var code = args.Str("code", required: true);
        return Jsonify(_evaluator(code));
    }

    /// <summary>
    /// ping — 探针（手册惯例：先 ping 后操作，失败即停）。
    /// 除 <c>pong</c> 外回读**身份**：pid / 端口 / 场景 / 起局时刻。
    /// 同机几局并存时这是唯一能分辨「我连的是不是刚起的那一局」的读数——
    /// 端口即寻址，但换端口躲残留的旧做法会让 CLI 静默连上先起的那一局。
    /// 注意 <c>tick</c> 是已处理的请求数（历史字段），**不是世界步数**，别当时间读。
    /// </summary>
    public object? PingVerb(JsonElement args) => new
    {
        pong = true,
        tick = _nextId,
        pid = OS.GetProcessId(),
        port = _port,
        scene = GetTree()?.CurrentScene?.SceneFilePath ?? "",
        started = _startedUnix,
    };

    /// <summary>verbs — 词表自报（冷启动发现口）。</summary>
    public object? VerbsVerb(JsonElement args) => _verbs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <summary>call/eval 的对象表自报。</summary>
    public object? TargetsVerb(JsonElement args) => _targets.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
}
