// Game.cs — 一局游戏的进程面：起（start）／看（list）／停（stop）。
//
// 为什么要有它（2026-10-06 实测）：起局原来全靠每轮手敲一条 Start-Process，
// 机器上没有「现在有几局」的读数，于是「端口按轮次换 7777→7778」实际是在**躲残留**——
// 换端口就是换寻址，先起的那一局还在跑，可没人看得见它。两局并存时最坏的一种是撞号：
// 桥监听失败只报一行 PushError 就「游戏照常运行」，而 CLI 连上端口应答的仍是先起的那一局，
// 于是读数、注入、重放、断言全作用在旧世界上，看着全对、验的是上一局。
//
// 三条口径：
// ① 起局前必探端口。端口上有人应答就拒绝起第二局，并把那局的身份与 PID 打出来。
// ② 身份从 ping 回读（pid / 端口 / 场景 / 起局时刻），一眼能确认「我连的是刚起的那一局」。
// ③ 账落 .cache/Runtime/instances.json（产物根经 Artifacts.Dir 取，不进版本库），进程死了账自动作废。
//
// 不动合法的并行用法：不同端口各跑一局仍然允许（多 worktree 并行就靠这条），
// 拦的只是「同一个端口上有两局」——那不是并行，那是误连。
//
// 窗口模式把窗口挪到 (-3000,-3000) 屏外（真机验证惯例）；引擎日志落到 --log-file，
// 不重定向 stdio（父进程退出后不留半截管道）。

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DemoFactory.Tools;

public static class Game
{
    /// <summary>默认端口（与 Debug.Run 的默认一致）。</summary>
    private const int DefaultPort = 7777;

    /// <summary>起局后等桥应答的默认秒数（mono 冷启动要装配，首启可能十几秒）。</summary>
    private const int DefaultWaitSeconds = 30;

    /// <summary>账里的一局（写进 .cache/Runtime/instances.json；进程死了就在下一次 list/start 时作废）。</summary>
    private sealed record RunRecord
    {
        /// <summary>游戏本体 pid（桥回读的那个；桥没应答时是起进程拿到的那个）。</summary>
        public int Pid { get; init; }

        /// <summary><c>_console.exe</c> 启动器 pid（它是本体的父进程；停局时两个都收）。</summary>
        public int LauncherPid { get; init; }

        public int Port { get; init; }
        public string Scene { get; init; } = "";
        public long Started { get; init; }
        public string Log { get; init; } = "";
        public string Exe { get; init; } = "";
    }

    /// <summary>
    /// 起局参数 —— <c>game start</c> 与 <c>uitest</c> 各自填一份（参数不同，起的实现是同一份）。
    /// </summary>
    public sealed class StartOptions
    {
        /// <summary>项目根（下面要有 project.godot）。</summary>
        public string Root { get; init; } = "";

        /// <summary>场景（res:// 路径）。</summary>
        public string Scene { get; init; } = "";

        /// <summary>调试桥端口（端口即寻址）。</summary>
        public int Port { get; init; } = DefaultPort;

        /// <summary>开窗口（默认无头）。</summary>
        public bool Window { get; init; }

        /// <summary>等桥应答的秒数。</summary>
        public int WaitSeconds { get; init; } = DefaultWaitSeconds;

        /// <summary>引擎可执行文件的显式路径（空串 = 走三级找法）。</summary>
        public string GodotFlag { get; init; } = "";

        /// <summary>找不到引擎时给的那句尾注（各调用点的上下文不同，各自带一份）。</summary>
        public string GodotHint { get; init; } = "";

        /// <summary>原样转交引擎的额外参数。</summary>
        public IReadOnlyList<string> Extra { get; init; } = Array.Empty<string>();
    }

    /// <summary>起局的结果（成功时 <see cref="Ok"/> 为真，失败时 <see cref="Error"/> 是判红理由）。</summary>
    public sealed class StartOutcome
    {
        public bool Ok { get; init; }
        public string Error { get; init; } = "";

        /// <summary>游戏本体 pid（桥回读的那个）。</summary>
        public int Pid { get; init; }

        /// <summary>启动器 pid（<c>_console.exe</c>）。</summary>
        public int LauncherPid { get; init; }

        public int Port { get; init; }
        public string Scene { get; init; } = "";

        /// <summary>引擎日志落点。</summary>
        public string Log { get; init; } = "";

        /// <summary>桥回读的身份（pid / 端口 / 场景 / 起局时刻）。</summary>
        public string Identity { get; init; } = "";
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) return Usage();
        var root = Flag(args, "--root=")?.Value ?? Directory.GetCurrentDirectory();
        return args[1] switch
        {
            "list" => List(root),
            "start" => Start(root, args[2..]),
            "stop" => Stop(root, args[2..]),
            _ => Usage(),
        };
    }

    // === start ===

    private static int Start(string root, string[] args)
    {
        string? scene = null;
        var godotFlag = Flag(args, "--godot=")?.Value ?? "";
        var port = IntFlag(args, "--port", DefaultPort);
        var wait = IntFlag(args, "--wait", DefaultWaitSeconds);
        var window = args.Contains("--window");
        var extra = Passthrough(args);

        foreach (var a in args)
        {
            if (a.StartsWith("-")) continue;
            scene = a;
            break; // 场景只认第一个非开关参数
        }

        if (scene is null)
        {
            Console.Error.WriteLine("缺场景参数。例：\n"
                + "  game start res://src/Prototypes/<原型>/Scenes/Main.tscn --port 7777");
            return 2;
        }
        if (!scene.StartsWith("res://", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"场景要写成 res:// 路径，收到的是「{scene}」。\n"
                + "（project.godot 的 run/main_scene 常常不是你这一轮要跑的那一个 —— 场景必须显式给）");
            return 2;
        }
        if (!File.Exists(Path.Combine(root, "project.godot")))
        {
            Console.Error.WriteLine($"这不是项目根（{root} 下没有 project.godot）。用 --root=<路径> 指项目根。");
            return 2;
        }

        var outcome = Launch(new StartOptions
        {
            Root = root,
            Scene = scene,
            Port = port,
            Window = window,
            WaitSeconds = wait,
            GodotFlag = godotFlag,
            GodotHint = "（game start 只起引擎可执行文件，不走编辑器；本机路径见《验证操作手册》「前提条件」）",
            Extra = extra,
        }, Console.WriteLine);

        if (!outcome.Ok)
        {
            Console.Error.WriteLine(outcome.Error);
            return 1;
        }

        Console.WriteLine($"桥已应答（这就是刚起的那一局）：{outcome.Identity}");
        Console.WriteLine($"下一步：debug ping --port {port} ／ debug snapshot_debug --port {port}"
            + $" ／ 收尾 game stop --port {port}");
        return 0;
    }

    /// <summary>
    /// 起一局并等桥应答 —— <c>game start</c> 与 <c>uitest</c> 共用这一份实现。
    ///
    /// <para><b>为什么共用</b>：起局那一套有五处细节（起前探端口、记账、等桥应答、
    /// 复认本体 pid、失败时的日志落点）。抄第二份必然先漂一份，而漂的那一份是
    /// 「我连的是哪一局」—— 错了读数、注入、断言全作用在旧世界上，看着全对、验的是上一局。</para>
    ///
    /// <para><paramref name="announce"/> 是「进程起来了」那一刻要打的那两行
    /// （各调用点自己决定打不打、怎么打；<c>game start</c> 的原文照旧）。</para>
    /// </summary>
    public static StartOutcome Launch(StartOptions options, Action<string>? announce)
    {
        var root = options.Root;
        var scene = options.Scene;
        var port = options.Port;

        // ① 起局前探活：端口上有人应答就拒绝起第二局，并把那局的身份打出来。
        var occupied = Probe(port);
        if (occupied is not null)
        {
            return new StartOutcome
            {
                Ok = false,
                Error = $"端口 {port} 上已经有一局在应答 —— 不起第二局。\n"
                    + $"  那局是：{Identity(occupied)}\n"
                    + $"  要接着验它就用 `debug <verb> --port {port}`；要换一局先停它：`game stop --port {port}`"
                    + $"（或 `game list` 看全部在跑的实例）。",
            };
        }

        var exe = GodotBin.Resolve(options.GodotFlag, options.GodotHint);
        var runtimeDir = Artifacts.Ensure(root, "Runtime");
        var log = Path.Combine(runtimeDir, $"run-{port}.log");

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(root);
        if (options.Window)
        {
            psi.ArgumentList.Add("--position");
            psi.ArgumentList.Add("-3000,-3000");
        }
        else psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add(scene);
        psi.ArgumentList.Add("--log-file");
        psi.ArgumentList.Add(log);
        psi.ArgumentList.Add("--");                    // 用户参数分隔符
        psi.ArgumentList.Add($"--debug-bridge-port={port}");
        foreach (var e in options.Extra) psi.ArgumentList.Add(e);
        psi.Environment["DEBUG_BRIDGE_PORT"] = port.ToString(CultureInfo.InvariantCulture);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Godot 进程没起来：" + exe);
        var launcherPid = proc.Id;      // _console.exe 只是启动器；本体 pid 要等桥回读
        var record = new RunRecord
        {
            Pid = launcherPid,
            LauncherPid = launcherPid,
            Port = port,
            Scene = scene,
            Started = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Log = log,
            Exe = exe,
        };
        Save(root, Live(root).Append(record).ToList());

        var mode = options.Window ? "窗口模式，窗口挪到屏外" : "无头";
        announce?.Invoke($"起了 启动器 pid={proc.Id}（{mode}）场景={scene} 端口={port}");
        announce?.Invoke($"引擎日志：{log}");

        // ② 等桥应答：应答即证明「我连的就是刚起的那一局」，不等就只能靠猜。
        var deadline = Stopwatch.StartNew();
        Dictionary<string, JsonElement>? id = null;
        while (deadline.Elapsed.TotalSeconds < options.WaitSeconds)
        {
            id = Probe(port);
            if (id is not null) break;
            if (proc.HasExited) break;
            Thread.Sleep(300);
        }

        if (id is null)
        {
            return new StartOutcome
            {
                Ok = false,
                Error = proc.HasExited
                    ? $"进程已经退了（退出码 {proc.ExitCode}）—— 端口 {port} 上没有桥。日志：{log}"
                    : $"等了 {options.WaitSeconds} 秒，端口 {port} 上还没有桥应答。日志：{log}\n"
                        + "（没配端口的局本就不开通道；但这里配了，详见日志里的 [DebugBridge] 那几行）",
            };
        }

        // 本体 pid 是桥回读那个（_console.exe 会另起一个子进程跑游戏，别把启动器当本体记账）
        var gamePid = Long(id, "pid") > 0 ? (int)Long(id, "pid") : launcherPid;
        record = record with { Pid = gamePid, LauncherPid = launcherPid };
        Save(root, Load(root).Select(r => r.Started == record.Started && r.Port == port ? record : r).ToList());

        return new StartOutcome
        {
            Ok = true,
            Pid = gamePid,
            LauncherPid = launcherPid,
            Port = port,
            Scene = scene,
            Log = log,
            Identity = Identity(id),
        };
    }

    // === list ===

    private static int List(string root)
    {
        var ledger = Live(root);
        var ports = ListeningPorts();
        // 账内的本体 pid 与启动器 pid 都要算「已知」，否则同一局会在账外那栏再列一遍
        var known = new HashSet<int>(ledger.SelectMany(r => new[] { r.Pid, r.LauncherPid }));
        var strays = StrayInstances()
            .Where(s => !known.Contains(s.Pid) && (s.LauncherPid == 0 || !known.Contains(s.LauncherPid)))
            .ToList();

        if (ledger.Count > 0)
        {
            Console.WriteLine("经 game start 起的实例（账在 .cache/Runtime/instances.json）：");
            foreach (var r in ledger)
            {
                var id = Probe(r.Port);
                Console.WriteLine($"  本体 pid={r.Pid} 启动器 pid={r.LauncherPid} 端口={r.Port} 场景={r.Scene}"
                    + $" 起局={Stamp(r.Started)} 桥={(id is null ? "无应答" : "应答")} 内存={MemMb(r.Pid)}MB");
                if (id is not null) Console.WriteLine($"      桥上那局：{Identity(id)}");
            }
        }

        // 账外：编辑器 F5 起的、手敲 Start-Process 起的 —— 它们照样吃 CPU 抢画面，照样列出来。
        if (strays.Count > 0)
        {
            Console.WriteLine("账外 Godot 进程（没经 game start 起的：编辑器 F5 或手敲命令）：");
            foreach (var s in strays)
            {
                var owned = ports.Where(kv => kv.Value == s.Pid).Select(kv => kv.Key).OrderBy(x => x).ToList();
                var launcher = s.LauncherPid > 0 ? $" 启动器 pid={s.LauncherPid}" : "";
                Console.WriteLine($"  pid={s.Pid} 场景={(s.Scene == "" ? "读不到" : s.Scene)} 起局={Stamp(StartOf(s.Pid))}"
                    + $" 内存={MemMb(s.Pid)}MB{(owned.Count > 0 ? $" 监听端口={string.Join(",", owned)}" : " 监听端口=无")}{launcher}");
            }
        }

        var total = ledger.Count + strays.Count;
        if (total == 0)
        {
            Console.WriteLine("没有游戏实例在跑。");
            return 0;
        }
        Console.WriteLine($"共 {total} 个实例（_console.exe 是启动器、跟着本体算一局，不另计）。");
        return 0;
    }

    // === stop ===

    /// <summary>
    /// 停一局 —— <c>game stop</c> 与 <c>uitest</c> 共用这一份实现
    /// （收尾只此一条路：杀整棵树 + 复认端口无监听）。
    /// </summary>
    public static int Stop(string root, string[] args)
    {
        var all = args.Contains("all");
        var pid = IntFlag(args, "--pid", -1);
        var port = IntFlag(args, "--port", -1);
        var ledger = Live(root);

        var targets = new List<RunRecord>();
        if (pid > 0) targets.Add(ledger.FirstOrDefault(r => r.Pid == pid) ?? new RunRecord { Pid = pid, Port = 0 });
        else if (port > 0)
        {
            targets.Add(ledger.FirstOrDefault(r => r.Port == port)
                ?? new RunRecord { Port = port, Pid = ListeningPorts().GetValueOrDefault(port) });
        }
        else if (all) targets.AddRange(ledger);
        else
        {
            Console.Error.WriteLine("stop 要指明停哪一局：`game stop --port 7777` ／ `game stop --pid 1234` ／ `game stop all`");
            return 2;
        }

        var killed = 0;
        foreach (var r in targets)
        {
            foreach (var target in new[] { r.Pid, r.LauncherPid }.Where(p => p > 0).Distinct())
            {
                if (!Alive(target))
                {
                    Console.WriteLine($"pid={target} 已经不在了。");
                    continue;
                }
                try
                {
                    // 整棵树一起杀：console.exe 只是启动器，杀它游戏本体就跟着走（反过来先杀本体也行，
                    // 启动器会跟着子进程一起退；两个都收是为了不留下孤儿）。
                    Process.GetProcessById(target).Kill(entireProcessTree: true);
                    killed++;
                    Console.WriteLine($"已停 pid={target} 端口={r.Port} 场景={r.Scene}");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"停 pid={target} 失败：{e.Message}");
                    return 1;
                }
            }
        }

        // 复确认：端口上不该再有监听（手册「用完即停」那一步的等价物）。
        Thread.Sleep(800);
        Save(root, ledger.Where(r => Alive(r.Pid) && !targets.Any(t => t.Pid == r.Pid)).ToList());
        var ports = ListeningPorts();
        foreach (var r in targets)
        {
            if (r.Port <= 0) continue;
            var still = ports.TryGetValue(r.Port, out var owner);
            Console.WriteLine(still
                ? $"复认：端口 {r.Port} 仍在监听（属主 pid={owner}）—— 还有别的东西占着，不是这一局。"
                : $"复认：端口 {r.Port} 已无监听。");
        }
        Console.WriteLine($"收了 {killed} 个进程（本体 + 启动器）。");
        return 0;
    }

    // === 探针与读数 ===

    /// <summary>探一个端口上有没有桥在应答；有则回身份字段，没有回 null（连不上就是没有，不猜）。</summary>
    private static Dictionary<string, JsonElement>? Probe(int port)
    {
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(IPAddress.Loopback, port).Wait(TimeSpan.FromSeconds(2))) return null;
            using var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            var req = Encoding.UTF8.GetBytes("{\"id\":1,\"verb\":\"ping\",\"args\":{}}\n");
            stream.Write(req, 0, req.Length);
            var response = new StreamReader(stream, Encoding.UTF8).ReadLine();
            if (response is null) return null;
            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object) return null;
            var map = new Dictionary<string, JsonElement>();
            foreach (var p in data.EnumerateObject()) map[p.Name] = p.Value.Clone();
            return map;
        }
        catch (Exception)
        {
            return null; // 连不上 / 协议不合 = 没人应答
        }
    }

    private static string Identity(Dictionary<string, JsonElement> id)
        => $"pid={Text(id, "pid")} 端口={Text(id, "port")} 场景={Text(id, "scene")} 起局={Stamp(Long(id, "started"))}";

    private static string Text(Dictionary<string, JsonElement> id, string key)
        => id.TryGetValue(key, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "?";

    private static long Long(Dictionary<string, JsonElement> id, string key)
        => id.TryGetValue(key, out var v) && v.TryGetInt64(out var l) ? l : 0;

    private static string Stamp(long unix)
        => unix <= 0 ? "?"
            : DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static long StartOf(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return new DateTimeOffset(p.StartTime).ToUnixTimeSeconds(); }
        catch (Exception) { return 0; }
    }

    private static string MemMb(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return (p.WorkingSet64 / 1048576.0).ToString("0", CultureInfo.InvariantCulture); }
        catch (Exception) { return "?"; }
    }

    /// <summary>本仓 Godot 进程（按引擎 exe 名前缀取；godot-ai 那种同名残留不算在内）。</summary>
    private static List<int> GodotProcesses()
        => Process.GetProcesses()
            .Where(p => p.ProcessName.StartsWith("Godot_v", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Id)
            .ToList();

    /// <summary>账外的一局：pid / 场景（命令行里那个 res://…tscn）/ 启动器 pid。</summary>
    private sealed record Stray(int Pid, string Scene, int LauncherPid);

    /// <summary>
    /// 账外实例：走 CIM 读命令行与父子关系（<c>_console.exe</c> 只是启动器、它的子进程才是本体，
    /// 两进程是一局；命令行里那个 <c>res://…tscn</c> 就是场景）。
    /// 读不到 CIM 就退回「进程名 + netstat」，场景那栏写读不到 —— 不假装有。
    /// </summary>
    private static List<Stray> StrayInstances()
    {
        var pids = GodotProcesses();
        var result = new List<Stray>();
        var byPid = new Dictionary<int, (int Parent, string Cmd)>();
        try
        {
            foreach (var p in OsProcesses(pids)) byPid[p.Pid] = (p.Parent, p.Cmd);
        }
        catch (Exception)
        {
            return pids.Select(p => new Stray(p, "", 0)).ToList();
        }

        // 是别人父进程的 = 启动器，不另计一局
        var parents = new HashSet<int>(byPid.Values.Select(v => v.Parent));
        foreach (var pid in pids)
        {
            if (!byPid.TryGetValue(pid, out var info)) continue;
            if (parents.Contains(pid)) continue;
            var launcher = byPid.TryGetValue(info.Parent, out var lp) ? info.Parent : 0;
            result.Add(new Stray(pid, SceneOf(info.Cmd), launcher));
        }
        return result;
    }

    private static string SceneOf(string cmd)
    {
        foreach (var token in cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim('"');
            if (t.StartsWith("res://", StringComparison.Ordinal) && t.EndsWith(".tscn", StringComparison.Ordinal)) return t;
        }
        return "";
    }

    private static List<(int Pid, int Parent, string Cmd)> OsProcesses(List<int> pids)
    {
        if (pids.Count == 0) return new List<(int, int, string)>();
        // WQL 的 OR 必须带括号，不带就报「无效查询」
        var filter = "(" + string.Join(") OR (", pids.Select(p => "ProcessId=" + p)) + ")";
        // 2>$null 让 pwsh 自己吞掉 WMI 的报错：只留一条 stdout 管道。
        // 两条管道一起读会互锁（stderr 填满 4KB 缓冲时子进程阻塞写 stderr，
        // 而我在阻塞读 stdout 等 EOF —— 症状是 `game list` 无限挂住）。
        var script = "Get-CimInstance Win32_Process -Filter \"" + filter + "\""
            + " | Select-Object ProcessId,ParentProcessId,CommandLine | ConvertTo-Json -Compress 2>$null";
        foreach (var shell in new[] { "pwsh", "powershell" })
        {
            try
            {
                var psi = new ProcessStartInfo(shell)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(script);
                using var p = Process.Start(psi);
                if (p is null) continue;
                var output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(20_000)) continue;
                if (output.Trim().Length == 0) continue;
                var parsed = JsonSerializer.Deserialize<List<CimRow>>(output) ?? new List<CimRow>();
                return parsed.Select(r => (r.ProcessId, r.ParentProcessId, r.CommandLine ?? "")).ToList();
            }
            catch (Exception)
            {
                // 换下一个 shell 名（WMI 服务没起时 pwsh 会直接报错）
            }
        }
        throw new InvalidOperationException("读不到进程命令行");
    }

    private sealed class CimRow
    {
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public string? CommandLine { get; set; }
    }

    /// <summary>127.0.0.1 上 7xxx 段监听端口 → 属主 PID。走 netstat（口径与手册一致）。</summary>
    private static Dictionary<int, int> ListeningPorts()
    {
        var map = new Dictionary<int, int>();
        try
        {
            var psi = new ProcessStartInfo("netstat") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-ano");
            using var p = Process.Start(psi);
            if (p is null) return map;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            foreach (var line in output.Split('\n'))
            {
                var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 4) continue;
                if (!cols[0].StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                if (!cols[1].StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase)) continue;
                // 监听态的对外地址恒为 0.0.0.0:0；不认状态词（Windows 上可能本地化）
                if (cols[2] != "0.0.0.0:0") continue;
                if (!int.TryParse(cols[1].AsSpan(cols[1].LastIndexOf(':') + 1), out var port)) continue;
                if (port < 7000 || port > 7999) continue;
                if (int.TryParse(cols[^1], out var pid)) map[port] = pid;
            }
        }
        catch (Exception)
        {
            // netstat 不可用：list 少一栏监听，不假装有
        }
        return map;
    }

    // === 账 ===

    private static string LedgerPath(string root) => Artifacts.Dir(root, "Runtime", "instances.json");

    private static List<RunRecord> Load(string root)
    {
        try
        {
            var path = LedgerPath(root);
            if (!File.Exists(path)) return new List<RunRecord>();
            return JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(path)) ?? new List<RunRecord>();
        }
        catch (Exception)
        {
            return new List<RunRecord>();   // 账坏了不该挡住排障：当作空账，下一次 start 覆盖
        }
    }

    /// <summary>读账并剔除已经死掉的进程（死账留着只会骗人）；顺带按桥上身份对账本体 pid。</summary>
    private static List<RunRecord> Live(string root)
    {
        var all = Load(root);
        var live = all.Where(r => Alive(r.Pid)).ToList();
        var reconciled = false;
        for (var i = 0; i < live.Count; i++)
        {
            var r = live[i];
            if (r.Port <= 0 || r.LauncherPid == r.Pid) continue;
            var id = Probe(r.Port);
            if (id is null) continue;
            var pid = Long(id, "pid");
            if (pid <= 0 || pid == r.Pid) continue;
            // 账里记的是启动器时把它挪到 LauncherPid、本体换成桥上那个
            live[i] = r with { Pid = (int)pid, LauncherPid = r.Pid };
            reconciled = true;
        }
        if (live.Count != all.Count || reconciled) Save(root, live);
        return live;
    }

    private static void Save(string root, List<RunRecord> records)
    {
        Artifacts.Ensure(root, "Runtime");
        File.WriteAllText(LedgerPath(root), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }

    // === 参数 ===

    private static (string Key, string Value)? Flag(string[] args, string key)
    {
        foreach (var a in args)
        {
            if (a.StartsWith(key, StringComparison.Ordinal))
                return (key, a[(key.Length)..]);
        }
        return null;
    }

    /// <summary>
    /// 整数开关，两种形状都收：<c>--port 7781</c>（空格，与 <c>debug</c> 那一支同口径）与
    /// <c>--port=7781</c>。只认等号那一支是这轮实测踩到的：命令看着生效、实际取了缺省值。
    /// </summary>
    private static int IntFlag(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == name && i + 1 < args.Length
                && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
            if (a.StartsWith(name + "=", StringComparison.Ordinal)
                && int.TryParse(a[(name.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var e))
                return e;
        }
        return fallback;
    }

    /// <summary>`--` 之后的原样转交引擎（出图参数之类：--write-movie / --resolution / --quit-after）。</summary>
    private static List<string> Passthrough(string[] args)
    {
        var i = Array.IndexOf(args, "--");
        return i < 0 ? new List<string>() : args[(i + 1)..].ToList();
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            """
            用法（项目根执行；跑一局游戏的进程面 —— 起／看／停）:
              game list                              # 这台机器上现在有几局、分别是谁
              game start <res://场景> [--port n] [--window] [--wait=秒] [--godot=<exe>] [-- <引擎参数>]
                                                    # 起一局：起前必探端口，有人应答就拒绝起第二局
              game stop --port n | --pid n | all    # 停一局（整棵树一起杀，完了复认端口无监听）

            默认无头；--window 开窗口并把窗口挪到屏外。
            合法并行不变：不同端口各跑一局仍然允许（多 worktree 就是这么并的）；
            被拦的只是「同一个端口上有两局」——那不是并行，那是误连。
            """);
        return 2;
    }
}
