#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 重放取证器测试（计划 Phase 2）— DivergenceReport 三件事的机器证明。
///
/// 三个被测件（src/GugaFramework/Replay/DivergenceReport.cs，通用层纯 C#）：
/// 1. 结构化快照 diff：字段级点名（替代"首处差异字符下标"——drill A 卡点①）；
/// 2. 报告模式 ReplayWithReport：断言失败不抛即返，产出分歧点 + 前置 Journal；
/// 3. 一键二分 Bisect：log₂ 步定位第一个分叉命令。
///
/// **哨兵红线测试**（计划 2.2 节验收②）：报告模式是旁路——本体 ReplayRunner.Replay
/// 零改动，正常重放路径的逐字断言语义不动。本文件用「人为注入分歧」演示取证器
/// 一发点名（计划 2.2 节验收①），并用哨兵脚本证明「无分歧 → 报告为 null」。
/// </summary>
public class DivergenceReportTests
{
    // ================= 结构化 diff =================

    [Fact]
    public void 结构化diff_点名差异字段与两侧值()
    {
        // 同构快照：treasury 差一笔（drill A 的真实形态：150 vs 149）
        var expected = "tick=300 treasury=150 net=12 outcome=Ongoing | B palace Palace Hero lv=1 hp=400 pos=(0,0)";
        var actual = "tick=300 treasury=149 net=12 outcome=Ongoing | B palace Palace Hero lv=1 hp=400 pos=(0,0)";
        var diffs = DivergenceReport.DiffSegments(expected, actual);

        diffs.Count.ShouldBe(1);
        diffs[0].SegmentHeader.ShouldStartWith("tick=300");
        diffs[0].Tokens.Count.ShouldBe(1);
        diffs[0].Tokens[0].Expected.ShouldBe("treasury=150");
        diffs[0].Tokens[0].Actual.ShouldBe("treasury=149");
    }

    [Fact]
    public void 结构化diff_多实体多字段_逐字段列出()
    {
        var expected = "tick=100 treasury=100 | U mob_1 \"地精\" hp=30/30 gold=0 | U hero_1 \"游侠\" hp=40/40 gold=5";
        var actual = "tick=100 treasury=100 net=0 outcome=Ongoing | U mob_1 \"地精\" hp=28/30 gold=0 | U hero_1 \"游侠\" hp=40/40 gold=7";
        var diffs = DivergenceReport.DiffSegments(expected, actual);

        // 段0：头部多 net= 键（token 数不齐 → 缺失侧报缺）；段1：hp 差；段2：gold 差
        diffs.Count.ShouldBe(3);
        diffs[1].SegmentHeader.ShouldContain("mob_1");
        diffs[1].Tokens.Single().Expected.ShouldBe("hp=30/30");
        diffs[1].Tokens.Single().Actual.ShouldBe("hp=28/30");
        diffs[2].Tokens.Single().Expected.ShouldBe("gold=5");
        diffs[2].Tokens.Single().Actual.ShouldBe("gold=7");
    }

    [Fact]
    public void 结构化diff_段缺失_报缺失侧()
    {
        var expected = "tick=10 treasury=0 | U mob_1 hp=30/30";
        var actual = "tick=10 treasury=10";
        var diffs = DivergenceReport.DiffSegments(expected, actual);

        // 两处：头部段 treasury 差一笔 + 实体段整段缺失
        diffs.Count.ShouldBe(2);
        diffs[1].Tokens.Single().Expected.ShouldBe("U mob_1 hp=30/30");
        diffs[1].Tokens.Single().Actual.ShouldBe("(重放侧缺失)");
    }

    [Fact]
    public void 结构化diff_完全相等_零差异()
    {
        var s = "tick=10 treasury=10 | U mob_1 hp=30/30";
        DivergenceReport.DiffSegments(s, s).ShouldBeEmpty();
    }

    // ================= 报告模式（真世界） =================

    /// <summary>录制一条小剧本（测试内录制端 — 与 ReplayScriptTests.RecordScript 同构）。</summary>
    private static ReplayRunner.ReplayScript RecordScript(int seed, long finalTick, bool injectBug = false)
    {
        var world = new LogicWorld(randomSeed: seed);
        using var w = ReplaySetup.Recorder(world, seed);

        ReplaySetup.AdvanceTo(world, 100);
        w.Mark("t100");
        w.Inject("PlaceBountyFlag", ReplaySetup.Args("lair1", 20));
        ReplaySetup.AdvanceTo(world, 250);
        w.Mark("t250");
        w.Inject("DebugSetTreasury", ReplaySetup.Args(600));
        w.Inject("PlaceBuilding", ReplaySetup.Args(BuildingKind.Tavern, new System.Numerics.Vector2(40, -40)));
        ReplaySetup.AdvanceTo(world, 400);
        w.Mark("t400");
        ReplaySetup.AdvanceTo(world, finalTick);
        w.Mark("t-final");
        if (injectBug)
        {
            // 人为注入分歧：改写最后检查点的期望快照（模拟「录制世界 ≠ 重放世界」）
            var last = w.Checkpoints[^1];
            w.Checkpoints[^1] = last with { Snapshot = last.Snapshot.Replace("treasury=", "treasury=X") };
        }

        return new ReplayRunner.ReplayScript(seed, w.Journal, w.Checkpoints);
    }

    [Fact]
    public void 报告模式_无分歧返回null()
    {
        var script = RecordScript(seed: 42, finalTick: 500);
        using var world = new LogicWorld(randomSeed: script.Seed);
        var r = DivergenceReport.ReplayWithReport(script, Hooks(world));
        r.ShouldBeNull();
    }

    [Fact]
    public void 报告模式_人为注入分歧_一发点名检查点与字段()
    {
        var script = RecordScript(seed: 42, finalTick: 500, injectBug: true);
        using var world = new LogicWorld(randomSeed: script.Seed);
        var r = DivergenceReport.ReplayWithReport(script, Hooks(world));

        r.ShouldNotBeNull();
        r!.DivergencePoint.ShouldContain("检查点「t-final」");
        r.Diffs.ShouldNotBeEmpty();
        r.PriorEntries.Count.ShouldBeGreaterThan(0);
        // 字段级点名：至少一条 TokenDiff 的两侧都非空且不同
        r.Diffs.Any(d => d.Tokens.Any(t => t.Expected != t.Actual)).ShouldBeTrue();
    }

    // ================= 一键二分 =================

    [Fact]
    public void 二分_ok翻转_一发精确点名分叉命令()
    {
        // ok 翻转构造：entries[1] 记的 ok=true，但重放世界里 ghost 旗不存在 → ok=false。
        // 二分：k=1（只有挂赏）→ 无分歧；k=2（含翻转命令）→ 命令返回不一致 → 分歧。
        // 收敛点 = 下标 1，Kind == "command-ok"（log₂ 精确点名——黑盒能力的兑现形态）。
        var script = new ReplayRunner.ReplayScript(42,
            new List<ReplayRunner.JournalEntry>
            {
                new(100, "PlaceBountyFlag", "lair1|20", true, 0),
                new(150, "RaiseBountyFlag", "ghost|50", true, 1), // 录制端 ok=true 是"毒"——重放端必 false
            },
            new List<ReplayRunner.Checkpoint>());

        var b = DivergenceReport.Bisect(script, () => HooksOf(42));
        b.DivergenceFound.ShouldBeTrue(b.Summary);
        b.Kind.ShouldBe("command-ok", b.Summary);
        b.Index.ShouldBe(1, b.Summary);
        b.FirstDivergingEntry!.Cmd.ShouldBe("RaiseBountyFlag", b.Summary);
        b.Rounds.ShouldBeLessThanOrEqualTo(4);
    }

    [Fact]
    public void 二分_状态类分歧_收缩到检查点前命令窗口()
    {
        // 状态类分歧（诚实边界演示）：检查点期望取**净世界**（无毒）真实快照，
        // entries 含毒命令（DebugSetTreasury）。毒效果只在整本重放（k=3）时与净期望相撞；
        // 裁 1/2 条时检查点在裁剪点之后（不断言）→ 不分歧。单调收敛 k*=3，
        // 定性 = checkpoint：黑盒无法精确到单条命令，但嫌疑窗口把毒命令圈在内。
        var poisonWorld = new LogicWorld(randomSeed: 42);
        var cleanWorld = new LogicWorld(randomSeed: 42);
        using var wPoison = ReplaySetup.Recorder(poisonWorld, 42);
        using var wClean = ReplaySetup.Recorder(cleanWorld, 42);
        foreach (var (w, world) in new[] { (wPoison, poisonWorld), (wClean, cleanWorld) })
        {
            ReplaySetup.AdvanceTo(world, 100);
            w.Inject("PlaceBountyFlag", ReplaySetup.Args("lair1", 20));
            ReplaySetup.AdvanceTo(world, 150);
            if (w == wPoison) w.Inject("DebugSetTreasury", ReplaySetup.Args(600)); // 毒命令（只有毒世界执行）
            ReplaySetup.AdvanceTo(world, 250);
            w.Inject("PlaceBuilding", ReplaySetup.Args(BuildingKind.Tavern, new System.Numerics.Vector2(40, -40)));
            ReplaySetup.AdvanceTo(world, 300);
            w.Mark("t300");
        }

        // 脚本 entries 取毒世界 Journal（3 条），检查点期望换成净世界快照
        var entries = wPoison.Journal.ToList();
        var cleanCheckpoints = wClean.Checkpoints.ToList();
        var script = new ReplayRunner.ReplayScript(42, entries, cleanCheckpoints);

        var b = DivergenceReport.Bisect(script, () => HooksOf(42));
        b.DivergenceFound.ShouldBeTrue(b.Summary);
        b.Kind.ShouldBe("checkpoint", b.Summary);
        b.FirstDivergingEntry.ShouldBeNull("状态类分歧黑盒无法精确到单条命令");
        b.Window.Any(e => e.Cmd == "DebugSetTreasury").ShouldBeTrue($"嫌疑窗口应含毒命令：{b.Summary}");
        b.CheckpointLabel.ShouldContain("t300");
    }

    [Fact]
    public void 二分_前缀零条也分歧_报分叉不来自命令()
    {
        // 检查点期望被改成毒值（treasury= → treasury=X），前缀 0 条命令即分歧
        // → 分叉不来自任何命令（种子/推进本身的确定性被破坏的形态）。
        var world = new LogicWorld(randomSeed: 42);
        using var w = ReplaySetup.Recorder(world, 42);

        ReplaySetup.AdvanceTo(world, 100);
        w.Mark("t100");
        // 毒化检查点期望（模拟录制世界 ≠ 重放世界，且与命令无关）
        var cp = w.Checkpoints[^1];
        w.Checkpoints[^1] = cp with { Snapshot = cp.Snapshot.Replace("treasury=", "treasury=X") };
        ReplaySetup.AdvanceTo(world, 200);
        w.Inject("PlaceBountyFlag", ReplaySetup.Args("lair1", 20)); // 一条无关命令（在分歧检查点之后）

        var script = new ReplayRunner.ReplayScript(42, w.Journal, w.Checkpoints);
        var b = DivergenceReport.Bisect(script, () => HooksOf(42));
        b.DivergenceFound.ShouldBeTrue(b.Summary);
        b.FirstDivergingEntry.ShouldBeNull(b.Summary);
        b.Summary.ShouldContain("不来自任何命令");
    }

    [Fact]
    public void 二分_无分歧脚本_报前提不成立()
    {
        var script = RecordScript(seed: 42, finalTick: 300);
        var b = DivergenceReport.Bisect(script, () => HooksOf(script.Seed));
        b.FirstDivergingEntry.ShouldBeNull();
        b.Summary.ShouldContain("前提不成立");
    }

    [Fact]
    public void 二分_起点非零脚本_明确拒绝()
    {
        var script = new ReplayRunner.ReplayScript(42,
            new List<ReplayRunner.JournalEntry>
                { new(100, "PlaceBountyFlag", "lair1|20", true, 0) },
            new List<ReplayRunner.Checkpoint>(), StartTick: 959, StartSnapshot: "tick=959 …");
        Should.Throw<ArgumentException>(() =>
            DivergenceReport.Bisect(script, () => HooksOf(42)));
    }

    // ================= 哨兵红线：本体零改动 =================

    [Fact]
    public void 本体重放_无分歧逐字复现_语义未动()
    {
        // 与报告模式同脚本、同钩子——本体 Replay 仍逐字断言（不改其语义的机器证明）
        var script = RecordScript(seed: 42, finalTick: 500);
        using var world = new LogicWorld(randomSeed: script.Seed);
        var seen = new List<string?>();
        ReplayRunner.Replay(script, Hooks(world), msg =>
        {
            if (msg != null) seen.Add(msg);
        });
        seen.ShouldBeEmpty("本体重放应零断言失败——报告模式是旁路，不得改变本体语义");
    }

    // ================= 共用接线 =================

    /// <summary>三钩子工厂 — 唯一一份在 <see cref="ReplaySetup"/>（本原型 Scripts/Logic/）。</summary>
    private static ReplayRunner.Hooks Hooks(LogicWorld world) => ReplaySetup.Hooks(world);

    private static ReplayRunner.Hooks HooksOf(int seed)
    {
        var world = new LogicWorld(randomSeed: seed);
        return Hooks(world); // 测试内一次性使用，世界由 GC 兜底（LogicWorld 是纯 C#，无引擎资源）
    }

}
