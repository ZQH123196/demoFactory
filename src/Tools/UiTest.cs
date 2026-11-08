// UiTest.cs —— uitest 通道：起一局（默认无头），按 JSON 脚本点控件、发命令、断言读数。
//
// 为什么要有这条通道：纯 C# 逻辑用 dotnet test 断言，界面这一层没有第二个可断言的口 ——
// 「玩家点得动吗」只能在真进程里验。脚本化之后，一条 UI 验收就是一条命令 + 一个退出码。
//
// 口径：
// · 这一侧只走调试桥（TCP JSON 行协议），不碰 Godot 类型 —— 所以它能编进 src/Tools。
//   引擎侧那两件（改根视口、按名字或文字找控件并真的按下）在各原型的 Scripts/Ui/UiTestDriver.cs。
// · 无头不出图：无头没有渲染器，screenshot 回 shot=empty-frame。要图另起一次窗口模式。
// · eval 只摆前置条件（伤害、名声、负债、账本余量）；被验的动词仍走 click 或 inject。
// · 用完即停：跑完（哪怕失败或抛异常）必停进程，并复认端口无监听。
// · 头一个失败就停：后面的步骤多半依赖前面的步骤，继续跑只会报一串假红。
// · 步数不是时长：wait 推的是逻辑步，不等墙钟（→ 「时间基准」）。
// · sleep 等墙钟（性能采样要真实时间）；read 把一段 eval 的回复打进控制台，只读不写。
// · set 给界面上的数量框填值（控件名=值），动词仍由随后那一按进入。

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DemoFactory.Tools;

public static class UiTest
{
    /// <summary>默认端口（与 Game / Debug 一致）。</summary>
    private const int DefaultPort = 7777;

    /// <summary>等桥应答的默认秒数（mono 冷启动要装配，首启可能十几秒）。</summary>
    private const int DefaultWaitSeconds = 30;

    /// <summary>根视口的目标宽（像素）。与引擎侧 <c>UiTestDriver</c> 的常量同值（两边各写一份字面量）。</summary>
    private const int TargetViewportWidth = 1920;

    /// <summary>根视口的目标高（像素）。</summary>
    private const int TargetViewportHeight = 1080;

    /// <summary>数字比较形态：<c>key&lt;op&gt;数</c>（op ∈ &gt; &gt;= &lt; &lt;= ==）。</summary>
    private static readonly Regex NumericAssert = new(
        @"^(?<key>[A-Za-z_][A-Za-z0-9_]*)(?<op>>=|<=|==|>|<)(?<num>[-+]?\d+(?:\.\d+)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>key=值</c> 字段（快照与读数里的读数位都是这个形状）。</summary>
    private static readonly Regex FieldPattern = new(
        @"(?:^|[\s|])(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?<value>[^\s|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>一步脚本（一个动作键 + 可选的 label）。</summary>
    private sealed class Step
    {
        public string Action { get; init; } = "";
        public string Value { get; init; } = "";
        public long Count { get; init; }
        public double Seconds { get; init; }
        public string Label { get; init; } = "";
    }

    /// <summary>一发桥请求的结果（<c>Text</c> 是数据，<c>Error</c> 是失败理由）。</summary>
    private sealed record BridgeReply(bool Ok, string Text, string Error);

    public static int Run(string[] args)
    {
        var scriptPath = "";
        var port = DefaultPort;
        var wait = DefaultWaitSeconds;
        var godotFlag = "";
        var scene = "";
        var root = Directory.GetCurrentDirectory();
        var window = false;

        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--window")
            {
                // 窗口模式：真渲染器在跑，坐标与出图那一侧与无头不同。窗口由 Game.Launch 挪到屏外。
                window = true;
            }
            else if (a == "--port" || a.StartsWith("--port=", StringComparison.Ordinal))
            {
                var raw = SwitchValue(args, ref i, "--port");
                if (!TryPort(raw, out var pv))
                {
                    Console.Error.WriteLine("--port 要一个正整数。");
                    return 2;
                }
                port = pv;
            }
            else if (a == "--scene" || a.StartsWith("--scene=", StringComparison.Ordinal))
            {
                scene = SwitchValue(args, ref i, "--scene") ?? "";
                if (scene.Length == 0)
                {
                    Console.Error.WriteLine("--scene 要一个 res:// 路径。");
                    return 2;
                }
            }
            else if (a.StartsWith("--godot=", StringComparison.Ordinal))
            {
                godotFlag = a["--godot=".Length..];
            }
            else if (a.StartsWith("--root=", StringComparison.Ordinal))
            {
                root = a["--root=".Length..];
            }
            else if (a.StartsWith("--wait=", StringComparison.Ordinal))
            {
                if (!int.TryParse(a["--wait=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wv) || wv <= 0)
                {
                    Console.Error.WriteLine("--wait 要一个正整数（秒）。");
                    return 2;
                }
                wait = wv;
            }
            else if (!a.StartsWith("-", StringComparison.Ordinal))
            {
                scriptPath = a;
            }
            else
            {
                Console.Error.WriteLine($"不认识的开关：{a}");
                return 2;
            }
        }

        if (scriptPath.Length == 0)
        {
            Console.Error.WriteLine("uitest 要一个脚本路径。\n"
                + "  dotnet run --project src/Tools -- uitest src/Prototypes/<原型>/Tests/Scripts/<脚本>.json --port 7801");
            return 2;
        }
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"脚本不存在：{scriptPath}");
            return 2;
        }
        if (!File.Exists(Path.Combine(root, "project.godot")))
        {
            Console.Error.WriteLine($"这不是项目根（{root} 下没有 project.godot）。用 --root=<路径> 指项目根。");
            return 2;
        }

        List<Step> steps;
        try
        {
            steps = ParseSteps(File.ReadAllText(scriptPath));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"脚本读不出来：{e.Message}");
            return 2;
        }

        if (scene.Length == 0)
        {
            scene = DefaultSceneFor(scriptPath, root) ?? "";
            if (scene.Length == 0)
            {
                Console.Error.WriteLine("定不出要起哪个场景。\n"
                    + "  脚本不在 src/Prototypes/<原型>/ 底下时，要显式给 --scene=res://…\n"
                    + "  （默认口径：脚本所在原型的 Scenes/Main.tscn）");
                return 2;
            }
        }

        var started = false;
        var failed = false;
        try
        {
            Console.WriteLine($"uitest：脚本 {scriptPath}（{steps.Count} 步）｜场景 {scene}｜端口 {port}｜"
                + (window ? "窗口（窗口挪到屏外；可出图）" : "无头（无头不出图）"));
            var outcome = Game.Launch(new Game.StartOptions
            {
                Root = root,
                Scene = scene,
                Port = port,
                Window = window,
                WaitSeconds = wait,
                GodotFlag = godotFlag,
                GodotHint = "（uitest 只起引擎可执行文件，不走编辑器；本机路径见《验证操作手册》「前提条件」）",
                Extra = Array.Empty<string>(),
            }, Console.WriteLine);

            if (!outcome.Ok)
            {
                Console.Error.WriteLine(outcome.Error);
                return 1;
            }
            started = true;

            // 视口：不设的话坐标全错（无头默认 64×64，--resolution 透传无效）。
            var viewport = Send(port, "ui_viewport", new Dictionary<string, object?>
            {
                ["width"] = TargetViewportWidth,
                ["height"] = TargetViewportHeight,
            });
            if (!viewport.Ok)
            {
                Console.Error.WriteLine($"根视口没设成：{viewport.Error}");
                failed = true;
            }
            else if (!viewport.Text.Contains("match=true", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"根视口没设成 {TargetViewportWidth}×{TargetViewportHeight}（引擎回读：{viewport.Text}）—— "
                    + "视口没生效，后面的控件坐标全是错的，整轮判红。\n"
                    + "（无头默认根视口就是 64×64，--resolution 不透传，必须显式设 Root.Size）");
                failed = true;
            }
            else
            {
                Console.WriteLine($"根视口：{viewport.Text}");
            }

            if (!failed) failed = !Execute(port, steps);
        }
        finally
        {
            if (started)
            {
                Console.WriteLine("收尾：停进程。");
                var stopCode = Game.Stop(root, new[] { "--port", port.ToString(CultureInfo.InvariantCulture) });
                if (stopCode != 0)
                {
                    Console.Error.WriteLine($"停局没干净（game stop 退 {stopCode}）—— 端口 {port} 可能还有人占着。");
                    failed = true;
                }
            }
        }

        return failed ? 1 : 0;
    }

    // === 逐步执行 ===

    /// <summary>按顺序跑脚本里的每一步；头一个失败就停（后面的步骤多半依赖前面的）。</summary>
    private static bool Execute(int port, IReadOnlyList<Step> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var number = i + 1;
            var expected = step.Value;
            bool ok;
            string actual;
            switch (step.Action)
            {
                case "click":
                    ok = DoClick(port, step.Value, out actual);
                    break;
                case "set":
                    ok = DoSet(port, step.Value, out actual);
                    break;
                case "inject":
                    ok = DoInject(port, step.Value, out actual);
                    break;
                case "eval":
                    ok = DoEval(port, step.Value, out actual);
                    break;
                case "wait":
                    expected = $"推进 {step.Count} 个逻辑步";
                    ok = DoWait(port, step.Count, out actual);
                    break;
                case "assert":
                    ok = DoAssert(port, step.Value, out actual);
                    break;
                case "assertUi":
                    ok = DoAssertUi(port, step.Value, out actual);
                    break;
                case "sleep":
                    expected = $"等 {step.Seconds.ToString("0.###", CultureInfo.InvariantCulture)} 个现实秒";
                    ok = DoSleep(step.Seconds, out actual);
                    break;
                case "read":
                    ok = DoRead(port, step.Value, out actual);
                    break;
                default:
                    ok = false;
                    actual = $"不认识的动作「{step.Action}」。";
                    break;
            }

            if (!ok)
            {
                ReportFailure(number, step, expected, actual);
                return false;
            }
            Console.WriteLine($"[第 {number} 步] {step.Action} {Describe(step)} —— 过了");
        }

        Console.WriteLine($"{steps.Count} 步全过。");
        return true;
    }

    /// <summary>点一个控件：引擎侧先按名字精确匹配，匹配不上才回退按唯一可见文字。</summary>
    private static bool DoClick(int port, string name, out string actual)
    {
        var reply = Send(port, "ui_click", new Dictionary<string, object?> { ["name"] = name });
        if (!reply.Ok)
        {
            actual = reply.Error;
            return false;
        }
        actual = reply.Text;
        return true;
    }

    /// <summary>发一条写路径命令（走调试桥 inject；被拒就算这一步没过，拒绝理由原样打出来）。</summary>
    private static bool DoInject(int port, string command, out string actual)
    {
        var bar = command.IndexOf('|');
        var cmd = bar < 0 ? command : command[..bar];
        var args = bar < 0 ? "" : command[(bar + 1)..];
        var reply = Send(port, "inject", new Dictionary<string, object?>
        {
            ["cmd"] = cmd,
            ["args"] = args,
        });
        if (!reply.Ok)
        {
            actual = reply.Error;
            return false;
        }
        actual = reply.Text;
        return reply.Text == "ok";
    }

    /// <summary>
    /// 跑一段只摆前置条件的 C#（走调试桥 eval）。
    ///
    /// <para><b>只准摆条件</b>：被验的动词仍走 <c>click</c> 或 <c>inject</c>。eval 里改世界会绕开
    /// Journal（「写路径进 Journal」），所以它造的只是「这一发之前本来就该成立的状态」——
    /// 伤害、名声、负债、账本余量。</para>
    /// </summary>
    private static bool DoEval(int port, string code, out string actual)
    {
        var reply = Send(port, "eval", new Dictionary<string, object?> { ["code"] = code });
        if (!reply.Ok)
        {
            actual = reply.Error;
            return false;
        }
        actual = reply.Text;
        return true;
    }

    /// <summary>给一个数量框填值（<c>控件名=值</c>）；动词仍由随后那一按进入。</summary>
    private static bool DoSet(int port, string spec, out string actual)
    {
        var eq = spec.IndexOf('=');
        if (eq <= 0 || eq == spec.Length - 1)
        {
            actual = $"set 要「控件名=值」，收到「{spec}」。";
            return false;
        }

        var reply = Send(port, "ui_set", new Dictionary<string, object?>
        {
            ["name"] = spec[..eq],
            ["value"] = spec[(eq + 1)..],
        });
        if (!reply.Ok)
        {
            actual = reply.Error;
            return false;
        }
        actual = reply.Text;
        return true;
    }

    /// <summary>推进 n 个逻辑步（不等墙钟）：读当前步号 → advance_to 到目标步号 → 复认到位。</summary>
    private static bool DoWait(int port, long steps, out string actual)
    {
        var snapshot = Send(port, "snapshot_debug", new Dictionary<string, object?>());
        if (!snapshot.Ok)
        {
            actual = snapshot.Error;
            return false;
        }
        if (!TryField(snapshot.Text, "tick", out var tickText)
            || !long.TryParse(tickText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
        {
            actual = "快照里读不到 tick= —— 不知道现在在哪一步。";
            return false;
        }

        var target = tick + steps;
        var advance = Send(port, "advance_to", new Dictionary<string, object?> { ["tick"] = target });
        if (!advance.Ok)
        {
            actual = advance.Error;
            return false;
        }
        if (!TryField(advance.Text, "tick", out var nowText)
            || !long.TryParse(nowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var now))
        {
            actual = $"advance_to 回读不到 tick：{advance.Text}";
            return false;
        }
        if (now < target)
        {
            actual = $"推进没到位：目标 tick={target}，实际 tick={now}（世界暂停或被冻住了？）。";
            return false;
        }
        actual = $"tick={tick} → tick={now}";
        return true;
    }

    /// <summary>
    /// 等 n 个现实秒，逻辑照跑。
    /// 性能采样的时间基准是墙钟，`wait` 那种一口气推进不算游玩帧，测不出帧率。
    /// </summary>
    private static bool DoSleep(double seconds, out string actual)
    {
        if (!double.IsFinite(seconds) || seconds < 0.0)
        {
            actual = $"sleep 要非负秒数，收到 {seconds}。";
            return false;
        }
        System.Threading.Thread.Sleep((int)Math.Round(seconds * 1000.0));
        actual = $"等了 {seconds.ToString("0.###", CultureInfo.InvariantCulture)} 秒";
        return true;
    }

    /// <summary>只读的一发 eval：把回复原样打进控制台（性能读数、检查点读数走它）。</summary>
    private static bool DoRead(int port, string code, out string actual)
    {
        if (!DoEval(port, code, out actual))
        {
            return false;
        }
        Console.WriteLine($"  读数：{actual}");
        return true;
    }

    /// <summary>对快照与读数的合读做断言（子串匹配 / 数字比较，支持 &amp;&amp; 组合）。</summary>
    private static bool DoAssert(int port, string expression, out string actual)
    {
        var snapshot = Send(port, "snapshot_debug", new Dictionary<string, object?>());
        if (!snapshot.Ok)
        {
            actual = snapshot.Error;
            return false;
        }
        var readout = Send(port, "readout", new Dictionary<string, object?>());
        if (!readout.Ok)
        {
            actual = readout.Error;
            return false;
        }
        return Evaluate(expression, snapshot.Text, readout.Text, out actual);
    }

    /// <summary>对 <c>ui_rects</c> 首行做断言（视口与布局那一层的事）。</summary>
    private static bool DoAssertUi(int port, string expression, out string actual)
    {
        var rects = Send(port, "ui_rects", new Dictionary<string, object?>());
        if (!rects.Ok)
        {
            actual = rects.Error;
            return false;
        }
        var first = rects.Text.Split('\n')[0].TrimEnd('\r');
        return EvaluateSubstrings(expression, first, out actual);
    }

    // === 判据 ===

    /// <summary>
    /// 判一个断言表达式 —— 支持 <c>&amp;&amp;</c> 组合；每段两种形态：
    /// ① 子串匹配（对快照与读数的合读）；② <c>key&lt;op&gt;数</c> 的数字比较
    /// （key 是快照里的 <c>key=</c> 字段，key 不在就判红，不崩）。
    /// </summary>
    private static bool Evaluate(string expression, string snapshot, string readout, out string actual)
    {
        var parts = expression.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            actual = "断言表达式是空的。";
            return false;
        }

        foreach (var part in parts)
        {
            if (EvaluateOne(part, snapshot, readout, out var why)) continue;
            actual = why;
            return false;
        }
        actual = "";
        return true;
    }

    private static bool EvaluateOne(string part, string snapshot, string readout, out string why)
    {
        why = "";
        var numeric = NumericAssert.Match(part);
        if (!numeric.Success)
        {
            var haystack = snapshot + "\n" + readout;
            if (haystack.Contains(part, StringComparison.Ordinal)) return true;
            why = $"读数里没有「{part}」（读数开头：{Excerpt(haystack)}）";
            return false;
        }

        var key = numeric.Groups["key"].Value;
        var op = numeric.Groups["op"].Value;
        var numberText = numeric.Groups["num"].Value;
        if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
        {
            why = $"「{part}」里的数「{numberText}」解析不了";
            return false;
        }
        if (!TryField(snapshot, key, out var raw))
        {
            why = $"{part}：快照里没有 {key}= 字段（快照开头：{Excerpt(snapshot)}）";
            return false;
        }
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            why = $"{part}：{key}= 的值「{raw}」不是数字";
            return false;
        }
        if (!Compare(value, op, expected))
        {
            why = $"{part}：{key}={raw}，不满足 {op}{numberText}";
            return false;
        }
        return true;
    }

    /// <summary>纯子串形态（<c>assertUi</c> 用：首行是 <c>viewport=(W, H)</c> 那一行，不是读数字段）。</summary>
    private static bool EvaluateSubstrings(string expression, string haystack, out string actual)
    {
        var parts = expression.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            actual = "断言表达式是空的。";
            return false;
        }
        foreach (var part in parts)
        {
            if (haystack.Contains(part, StringComparison.Ordinal)) continue;
            actual = $"首行里没有「{part}」（首行：{Excerpt(haystack)}）";
            return false;
        }
        actual = "";
        return true;
    }

    private static bool Compare(double actual, string op, double expected) => op switch
    {
        ">" => actual > expected,
        ">=" => actual >= expected,
        "<" => actual < expected,
        "<=" => actual <= expected,
        "==" => Math.Abs(actual - expected) < 1e-9,
        _ => false,
    };

    /// <summary>在文本里找第一个 <c>key=值</c> 字段。</summary>
    private static bool TryField(string text, string key, out string value)
    {
        foreach (Match m in FieldPattern.Matches(text))
        {
            if (!string.Equals(m.Groups["key"].Value, key, StringComparison.Ordinal)) continue;
            value = m.Groups["value"].Value;
            return true;
        }
        value = "";
        return false;
    }

    // === 桥 ===

    /// <summary>一发调试桥请求（短连接：连上、发一行、读一行、断开）。</summary>
    private static BridgeReply Send(int port, string verb, Dictionary<string, object?> args)
    {
        var request = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["verb"] = verb,
            ["args"] = args,
        });
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(IPAddress.Loopback, port).Wait(TimeSpan.FromSeconds(5)))
                return new BridgeReply(false, "", $"连不上 127.0.0.1:{port}（5 秒超时）—— 游戏没起或端口不对");
            using var stream = client.GetStream();
            stream.ReadTimeout = 60_000;
            var bytes = Encoding.UTF8.GetBytes(request + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            var response = new StreamReader(stream, Encoding.UTF8).ReadLine();
            if (response is null) return new BridgeReply(false, "", "空响应（连接被对端关闭）");
            return Parse(response);
        }
        catch (Exception e)
        {
            return new BridgeReply(false, "", e.Message);
        }
    }

    private static BridgeReply Parse(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var text = "";
            var error = "";
            if (root.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.String) text = data.GetString() ?? "";
                else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("error", out var err))
                    error = err.GetString() ?? "";
                else text = data.ToString();
            }
            return new BridgeReply(ok, text, error);
        }
        catch (Exception e)
        {
            return new BridgeReply(false, "", $"响应读不出来：{e.Message}（{Excerpt(response)}）");
        }
    }

    // === 脚本与参数 ===

    /// <summary>脚本 → 步骤表（一个 JSON 数组，每项一个动作键 + 可选 label）。</summary>
    private static List<Step> ParseSteps(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("脚本要是一个 JSON 数组（每项一个步骤）。");

        var steps = new List<Step>();
        var index = 0;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"第 {index} 步不是对象。");

            var label = item.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
                ? l.GetString() ?? ""
                : "";

            string? action = null;
            var value = "";
            long count = 0;
            double seconds = 0.0;
            foreach (var prop in item.EnumerateObject())
            {
                if (prop.Name == "label") continue;
                if (action is not null)
                    throw new InvalidOperationException(
                        $"第 {index} 步有多个动作键（{action} 与 {prop.Name}）—— 一步只准一个动作。");

                action = prop.Name;
                switch (prop.Name)
                {
                    case "click":
                    case "inject":
                    case "eval":
                    case "read":
                    case "set":
                    case "assert":
                    case "assertUi":
                        value = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? ""
                            : prop.Value.ToString();
                        break;
                    case "wait":
                        count = prop.Value.ValueKind == JsonValueKind.Number
                            ? prop.Value.GetInt64()
                            : long.Parse(prop.Value.GetString() ?? "0", CultureInfo.InvariantCulture);
                        break;
                    case "sleep":
                        seconds = prop.Value.ValueKind == JsonValueKind.Number
                            ? prop.Value.GetDouble()
                            : double.Parse(prop.Value.GetString() ?? "0", CultureInfo.InvariantCulture);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"第 {index} 步的动作键「{prop.Name}」不认识（可用：click / set / inject / eval / read / wait / sleep / assert / assertUi）。");
                }
            }

            if (action is null)
                throw new InvalidOperationException($"第 {index} 步没有动作键。");
            steps.Add(new Step { Action = action, Value = value, Count = count, Seconds = seconds, Label = label });
        }
        return steps;
    }

    /// <summary>
    /// 默认场景：脚本路径里的原型目录 → <c>res://src/Prototypes/&lt;原型&gt;/Scenes/Main.tscn</c>。
    ///
    /// <para>为什么从脚本路径推：公共层不许出现原型名，而「这一轮要验哪个原型」
    /// 脚本自己知道 —— 脚本就放在那个原型的 <c>Tests/Scripts/</c> 底下。</para>
    /// </summary>
    private static string? DefaultSceneFor(string scriptPath, string root)
    {
        var parts = Path.GetFullPath(scriptPath).Replace('\\', '/').Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!string.Equals(parts[i], "Prototypes", StringComparison.Ordinal)) continue;
            var dir = parts[i + 1];
            if (dir.Length == 0 || dir.StartsWith("_", StringComparison.Ordinal)) continue;
            var relative = Path.Combine("src", "Prototypes", dir, "Scenes", "Main.tscn");
            if (!File.Exists(Path.Combine(root, relative))) continue;
            return "res://" + relative.Replace('\\', '/');
        }
        return null;
    }

    /// <summary>取开关的值：<c>--k=v</c> 那一支，或 <c>--k v</c> 那一支（后者吃掉下一个参数）。</summary>
    private static string? SwitchValue(string[] args, ref int i, string name)
    {
        var a = args[i];
        if (a.StartsWith(name + "=", StringComparison.Ordinal)) return a[(name.Length + 1)..];
        if (a == name)
        {
            if (i + 1 >= args.Length) return null;
            i++;
            return args[i];
        }
        return null;
    }

    private static bool TryPort(string? raw, out int port)
    {
        port = 0;
        return raw is not null
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
            && port > 0;
    }

    // === 打印 ===

    private static void ReportFailure(int number, Step step, string expected, string actual)
    {
        Console.Error.WriteLine($"第 {number} 步失败{(step.Label.Length > 0 ? $"（{step.Label}）" : "")}：");
        Console.Error.WriteLine($"  动作：{step.Action}");
        Console.Error.WriteLine($"  期望：{expected}");
        Console.Error.WriteLine($"  实际：{actual}");
    }

    private static string Describe(Step step) => step.Action switch
    {
        "wait" => $"推进 {step.Count} 个逻辑步",
        "sleep" => $"等 {step.Seconds.ToString("0.###", CultureInfo.InvariantCulture)} 个现实秒",
        "read" => $"读数 {step.Value}",
        _ => step.Value,
    };

    /// <summary>截一段出来看（读数很长，判红时只带开头那一截）。</summary>
    private static string Excerpt(string text, int limit = 200)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ');
        return flat.Length <= limit ? flat : flat[..limit] + "…";
    }
}
