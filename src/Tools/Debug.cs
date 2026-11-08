// DebugBridge CLI — 真机调试动词的统一入口（计划 Phase 1）。
// 一发动词一次短连接：connect → 发请求行 → 读响应行 → 断开。
// 连不上就明确报错 + 提示「游戏未运行或未开端口」，绝不猜（计划 1.1 节）。

#nullable enable
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DemoFactory.Tools;

public static class Debug
{
    /// <summary>默认端口（与 Main 接线的环境变量默认一致 — 换端口用 --port）。</summary>
    private const int DefaultPort = 7777;

    public static int Run(string[] args)
    {
        // debug [原型名] <verb> [args] — 原型名可选（端口即唯一寻址；带不带都兼容）
        // 原型名查 IPrototypeTools 注册表，公共层不留名单（怎么算合格见 IPrototypeTools.cs 头注）。
        // Find 是「只留字母数字 + 小写」规范化后的**全等**比对，不做模糊匹配，
        // 所以 record_start（→ recordstart）这类动词撞不上任何原型目录名。
        var idx = 1;
        if (args.Length > 2 && Prototypes.Find(args[1]) != null)
            idx = 2; // 跳过原型名
        if (args.Length < idx + 1)
        {
            Usage();
            return 2;
        }

        var verb = args[idx];
        var port = DefaultPort;
        for (var i = idx + 1; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length
                && int.TryParse(args[i + 1], out var p) && p > 0)
            {
                port = p;
                args = args.Where((_, idx) => idx != i && idx != i + 1).ToArray();
                break;
            }
        }

        var argsJson = BuildArgsJson(args.Length > idx + 1 ? args[(idx + 1)..] : Array.Empty<string>());
        var request = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["verb"] = verb,
            ["args"] = argsJson,
        });

        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(System.Net.IPAddress.Loopback, port);
            if (!connect.Wait(TimeSpan.FromSeconds(2)))
            {
                Console.Error.WriteLine($"连不上 127.0.0.1:{port}（2s 超时）— 游戏未运行或未开端口"
                    + $"（启动游戏前设环境变量 DEBUG_BRIDGE_PORT={port}，或 --debug-bridge-port={port}）");
                return 1;
            }
            using var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(request + "\n");
            stream.Write(bytes, 0, bytes.Length);

            var reader = new StreamReader(stream, Encoding.UTF8);
            var response = reader.ReadLine();
            if (response is null)
            {
                Console.Error.WriteLine("空响应（连接被对端关闭）");
                return 1;
            }
            Console.WriteLine(Pretty(response));
            return response.Contains("\"ok\":true", StringComparison.Ordinal) ? 0 : 1;
        }
        catch (SocketException)
        {
            Console.Error.WriteLine($"连不上 127.0.0.1:{port} — 游戏未运行或未开端口"
                + $"（启动游戏前设环境变量 DEBUG_BRIDGE_PORT={port}，或 --debug-bridge-port={port}）");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    /// <summary>剩余参数 → args JSON：--key value 或 key=value 形式；裸值进 args 数组。</summary>
    private static JsonElement BuildArgsJson(string[] rest)
    {
        if (rest.Length == 0) return JsonSerializer.SerializeToElement(new { });

        var dict = new Dictionary<string, object?>();
        var list = new List<object?>();
        for (var i = 0; i < rest.Length; i++)
        {
            var arg = rest[i];
            if (arg.StartsWith("--", StringComparison.Ordinal) && i + 1 < rest.Length)
            {
                var key = arg[2..];
                dict[key] = Value(key, rest[i + 1]);
                i++;
            }
            else if (arg.Contains('=', StringComparison.Ordinal))
            {
                var eq = arg.IndexOf('=', StringComparison.Ordinal);
                var key = arg[..eq];
                dict[key] = Value(key, arg[(eq + 1)..]);
            }
            else
            {
                list.Add(Coerce(arg));
            }
        }
        if (list.Count > 0) dict["args"] = list;
        return JsonSerializer.SerializeToElement(dict);
    }

    /// <summary>
    /// <c>--args</c> 的值<b>原样当字符串</b>，不过 <see cref="Coerce"/>。
    ///
    /// <para>理由：<c>--args</c> 传的是 Journal 那一记的 token 串（<c>ReplayArgs.Split</c> 按它切段），
    /// 不是标量。而 <see cref="BridgeArgs.Str"/> 只收字符串，收到 JSON 数字会当场抛
    /// <c>InvalidOperationException</c>。</para>
    ///
    /// <para>这条不是洁癖：仓里原有的动词 args 都带地名或分隔符（<c>lair1|25</c>），恰好绕过了
    /// <see cref="Coerce"/> 的数字分支；存档那两条的 args 是<b>一个纯槽号</b>（<c>1</c>），
    /// 于是它成了第一个踩中的动词 —— 实测 2026-10-07 真机 <c>inject WriteSave</c> 直接抛上面那句。</para>
    ///
    /// <para>其余键照旧走 <see cref="Coerce"/>：<c>--tick</c> / <c>--multiplier</c> 那些要的就是数字。</para>
    /// </summary>
    private static object? Value(string key, string raw) => key == "args" ? raw : Coerce(raw);

    /// <summary>字符串 → JSON 值：数字/bool 尝试解析，其余保持字符串。</summary>
    private static object? Coerce(string raw) => raw switch
    {
        "true" => true,
        "false" => false,
        _ when int.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var i) => i,
        _ when long.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var l) => l,
        _ when double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => raw,
    };

    /// <summary>响应美化：两空格缩进（可读性优先，机器读字段用 jq/grep 也无碍）。</summary>
    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
        }
        catch
        {
            return json;
        }
    }

    private static void Usage()
    {
        Console.Error.WriteLine(
            """
            用法（项目根执行；游戏需已启动且开了桥端口 DEBUG_BRIDGE_PORT）：
              dotnet run --project src/Tools -- debug ping
              dotnet run --project src/Tools -- debug verbs
              dotnet run --project src/Tools -- debug snapshot
              dotnet run --project src/Tools -- debug speed --multiplier 20
              dotnet run --project src/Tools -- debug pause
              dotnet run --project src/Tools -- debug resume
              dotnet run --project src/Tools -- debug step
              dotnet run --project src/Tools -- debug advance --tick 600
              dotnet run --project src/Tools -- debug inject --cmd PlaceBountyFlag --args "lair1|25"
              dotnet run --project src/Tools -- debug mark --label t300
              dotnet run --project src/Tools -- debug record_start
              dotnet run --project src/Tools -- debug record_stop --name replay-v2-xxx
              dotnet run --project src/Tools -- debug call --target main --method DebugEndgame
              dotnet run --project src/Tools -- debug eval --code "Main.DebugEndgame()"
              dotnet run --project src/Tools -- debug logs --count 50

              通用：[--port 7777]（默认 7777；游戏侧设 DEBUG_BRIDGE_PORT 环境变量）
            """);
    }
}