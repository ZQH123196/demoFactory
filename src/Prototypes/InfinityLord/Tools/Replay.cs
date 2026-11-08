// 离线重放取证 CLI（计划 Phase 2）— 重放脚本 json 的离线执行与取证报告。
// 与 Debug.cs（bridge 实机通道）同一项目，但**不连 bridge**：重放/二分是纯离线
// 毫秒级（二分每轮新建世界），与真机排障通道正交。引用面与测试项目同一模式：
// GugaFramework/Replay 通用层（纯 C#）+ Logic/*.cs（零 Godot 类型）。
// （Tests 与主 csproj 的既有惯例：源码链接，不引用主 csproj。）

#nullable enable
using System;
using System.IO;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.InfinityLord.Logic;

namespace DemoFactory.Tools.InfinityLord;

public static class Replay
{
    /// <summary>
    /// replay InfinityLord &lt;script.json&gt; [--report] [--bisect]
    /// 缺省 = 正常重放（逐字断言，等价 dotnet test 重放测试）；--report = 取证报告；
    /// --bisect = 一键二分（StartTick==0 脚本）。返回码：0 无分歧 / 1 有分歧或错误。
    /// </summary>
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Usage();
            return 2;
        }
        var file = args[2];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"脚本不存在：{file}");
            return 1;
        }
        var script = ReplayScriptJson.Read(File.ReadAllText(file));
        var report = Array.Exists(args, a => a == "--report");
        var bisect = Array.Exists(args, a => a == "--bisect");

        // --report：取证报告（首个分歧即停，产出结构化材料）
        if (report)
        {
            var r = DivergenceReport.ReplayWithReport(script, HooksFactoryFor(script));
            if (r == null)
            {
                Console.WriteLine($"重放无分歧：{file}");
                return 0;
            }
            Console.WriteLine(r);
            if (r.ExpectedSnapshot != null && r.ActualSnapshot != null)
            {
                Console.WriteLine("--- 两侧快照 ---");
                Console.WriteLine($"录制：{r.ExpectedSnapshot}");
                Console.WriteLine($"重放：{r.ActualSnapshot}");
            }
            return 1;
        }

        // --bisect：对半裁剪二分，定位第一个分叉命令
        if (bisect)
        {
            var b = DivergenceReport.Bisect(script, () => HooksFactoryFor(script));
            Console.WriteLine(b.Summary);
            // 未定位到分叉命令 = 前提不成立（脚本已修复/无可二分内容）——不是失败
            return b.DivergenceFound && b.FirstDivergingEntry == null ? 1 : 0;
        }

        // 缺省：正常重放（逐字断言——不等即抛，等价 dotnet test 重放测试）
        ReplayPlain(script);
        Console.WriteLine($"重放通过：{file}");
        return 0;
    }

    /// <summary>正常重放（逐字断言）— 用报告模式发现分歧后按断言语义抛出。</summary>
    private static void ReplayPlain(ReplayRunner.ReplayScript script)
    {
        var r = DivergenceReport.ReplayWithReport(script, HooksFactoryFor(script));
        if (r != null)
            throw new InvalidOperationException(
                "重放分歧（逐字断言失败）\n" + r);
    }

    /// <summary>
    /// 原型侧三钩子工厂 —— 词表与推进器全在 <see cref="ReplaySetup"/>（无限领主逻辑层那一份），
    /// CLI 是消费者之一，不造第二套。每轮二分都新建世界，所以工厂每轮调一次。
    /// </summary>
    private static ReplayRunner.Hooks HooksFactoryFor(ReplayRunner.ReplayScript script)
        => ReplaySetup.Hooks(new LogicWorld(randomSeed: script.Seed));

    private static void Usage()
    {
        Console.Error.WriteLine(
            """
            用法（项目根执行；纯离线，不连游戏进程）：
              dotnet run --project src/Tools -- replay InfinityLord <script.json>            # 逐字重放（缺省）
              dotnet run --project src/Tools -- replay InfinityLord <script.json> --report   # 取证报告（首个分歧 + 字段级 diff + 前置 Journal）
              dotnet run --project src/Tools -- replay InfinityLord <script.json> --bisect   # 一键二分（log₂ 步点名第一个分叉命令；仅 StartTick==0 脚本）
            """);
    }
}
