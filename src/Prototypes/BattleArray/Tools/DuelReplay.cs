// 排兵布阵 H3 的离线重放取证（D4 那一格）。
//
// 与无限领主那份 src/Prototypes/InfinityLord/Tools/Replay.cs 同一形状，但有两处刻意不同：
// 1. 词表与快照<b>不复制</b>，直接吃 Scripts/Logic/DuelRiftSetup.cs 那一份装配 ——
//    那边同时被离线测试与引擎里的桥用着，抄一份就成了第二套真值（AGENTS 「桥只转发」 讲的正是这事）。
// 2. 多一条 --dump：把实跑的逐格指纹打到标准输出，级 2（引擎里的活局）也打同一格式，
//    两边直接 diff —— 这就是 D8「级 2 与级 1 的快照序列逐字相等」的命令形状。
//
// 注册面：本文件按 csproj 通配 Prototypes\*\Tools\*.cs 源码链接进 DemoFactory.Tools，
// 由 BattleArrayTools.RunVerb 路由过来，公共层（src/Tools/）不需要为新原型改文件。

#nullable enable
using System;
using System.IO;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BattleArray.Logic;

namespace DemoFactory.Tools.BattleArray;

public static class DuelReplay
{
    /// <summary>
    /// replay BattleArray &lt;script.json&gt; [--report] [--bisect] [--dump]
    /// 缺省 = 逐字重放（等价 dotnet test 那条）；--report = 首个分歧的结构化取证；
    /// --bisect = 一键二分定位第一条分叉命令；--dump = 打逐格指纹（与级 2 那份同格式，直接 diff）。
    /// 返回码：0 无分歧 / 1 有分歧或读不到脚本 / 2 用法错。
    /// </summary>
    public static int Run(string[] argv)
    {
        if (argv.Length < 3)
        {
            Usage();
            return 2;
        }

        var file = argv[2];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"脚本不存在：{file}");
            return 1;
        }

        var script = ReplayScriptJson.Read(File.ReadAllText(file));
        var flags = argv.Length > 3 ? argv[3] : "";

        if (flags == "--report")
        {
            var report = DivergenceReport.ReplayWithReport(script, HooksFor(script));
            if (report is null)
            {
                Console.WriteLine($"重放无分歧：{file}");
                return 0;
            }
            Console.WriteLine(report);
            return 1;
        }

        if (flags == "--bisect")
        {
            var bisect = DivergenceReport.Bisect(script, () => HooksFor(script));
            Console.WriteLine(bisect.Summary);
            return bisect.DivergenceFound && bisect.FirstDivergingEntry is null ? 1 : 0;
        }

        var session = new LiveReplaySession(script, HooksFor(script));
        session.RunAll();

        if (flags == "--dump")
        {
            Console.WriteLine(session.DumpSequence());
            Console.Error.WriteLine($"检查点 {session.Checkpoints.Count} 格，分歧 {session.Divergences.Count} 处");
            return session.HasDivergence ? 1 : 0;
        }

        if (session.HasDivergence)
        {
            Console.WriteLine("重放分歧（逐字断言失败）：");
            Console.WriteLine(string.Join("\n", session.Divergences));
            return 1;
        }

        Console.WriteLine($"重放通过：{file}（条目 {script.Entries.Count} 命令 / " +
                          $"{script.Checkpoints.Count} 检查点，全跑完）");
        return 0;
    }

    /// <summary>
    /// 三钩子工厂 —— 每轮二分都重新装一局（种子从脚本头部透传），
    /// 与离线测试、引擎活局吃的是同一段 <see cref="DuelRiftSetup"/>。
    /// </summary>
    private static ReplayRunner.Hooks HooksFor(ReplayRunner.ReplayScript script)
        => DuelRiftSetup.Hooks(DuelRiftSetup.NewWorld(seed: script.Seed));

    private static void Usage()
    {
        Console.Error.WriteLine(
            """
            用法（项目根执行；纯离线，不连游戏进程）：
              dotnet run --project src/Tools -- replay BattleArray <script.json>            # 逐字重放（缺省）
              dotnet run --project src/Tools -- replay BattleArray <script.json> --report   # 取证报告（首个分歧 + 两侧快照）
              dotnet run --project src/Tools -- replay BattleArray <script.json> --bisect   # 一键二分（定位第一条分叉命令）
              dotnet run --project src/Tools -- replay BattleArray <script.json> --dump     # 打逐格指纹（与级 2 那份 diff，D8）
            """);
    }
}
