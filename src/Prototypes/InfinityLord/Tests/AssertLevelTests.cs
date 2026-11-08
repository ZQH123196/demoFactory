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
/// 断言分层测试（计划 Phase 4.1）—「快照逐字」从唯一口径降为末档的机器证明。
///
/// 被测件：ReplayRunner 的 AssertLevel 档位（通用层纯 C#，零游戏知识）：
/// - L3 逐字（默认）：存量脚本零迁移——任何字符差都红；
/// - L2 语义：结构必须一致（步号漂移/实体生死/段数/段序漂移红），
///   键集与同键值漂移（加字段/调数值）容忍不红；
/// - 命令 ok 翻转：行为分歧，任何档位都红（不走快照断言路径）。
/// L1（守恒不变量）是测试资产层（EconomyConservationTests）的既有断言，不进脚本头部。
/// </summary>
public class AssertLevelTests
{
    // ================= L2 语义 diff（通用层纯函数） =================

    [Fact]
    public void 语义diff_同键值漂移_容忍()
    {
        // 调数值类良性改动：treasury 149→150（L3 会红，L2 容忍）
        var expected = "tick=300 treasury=149 | U mob_1 hp=30/30";
        var actual = "tick=300 treasury=150 | U mob_1 hp=30/30";
        ReplayRunner.SemanticDiff(expected, actual).ShouldBeEmpty();
    }

    [Fact]
    public void 语义diff_键集漂移_容忍()
    {
        // 加字段类良性改动：实体段新增 morale=75（L3 会红，L2 容忍——键集变化不是结构变化）
        var expected = "tick=300 treasury=100 | U mob_1 hp=30/30 gold=0";
        var actual = "tick=300 treasury=100 | U mob_1 hp=30/30 gold=0 morale=75";
        ReplayRunner.SemanticDiff(expected, actual).ShouldBeEmpty();
    }

    [Fact]
    public void 语义diff_实体死亡_结构分歧()
    {
        // 实体整段消失（死亡/未出生）= 身份层变化——L2 必须红（语义分歧不是数值漂移）
        // 段数含头部段：3（头部+两实体）→ 2（头部+一实体）
        var expected = "tick=300 treasury=100 | U mob_1 hp=30/30 | U mob_2 hp=30/30";
        var actual = "tick=300 treasury=100 | U mob_1 hp=30/30";
        var diffs = ReplayRunner.SemanticDiff(expected, actual);
        diffs.Count.ShouldBe(1);
        diffs[0].ShouldContain("实体段数");
        diffs[0].ShouldContain("录制 3 ≠ 重放 2");
    }

    [Fact]
    public void 语义diff_实体出生_结构分歧()
    {
        var expected = "tick=300 treasury=100 | U mob_1 hp=30/30";
        var actual = "tick=300 treasury=100 | U mob_1 hp=30/30 | U mob_2 hp=30/30";
        ReplayRunner.SemanticDiff(expected, actual).ShouldNotBeEmpty();
    }

    [Fact]
    public void 语义diff_tick漂移_结构分歧()
    {
        var expected = "tick=300 treasury=100 | U mob_1 hp=30/30";
        var actual = "tick=299 treasury=100 | U mob_1 hp=30/30";
        var diffs = ReplayRunner.SemanticDiff(expected, actual);
        diffs.ShouldNotBeEmpty();
        diffs[0].ShouldContain("头部步号漂移");
    }

    [Fact]
    public void 语义diff_实体Id漂移_结构分歧()
    {
        // 段序/Id 漂移：录制 mob_1|mob_2 ↔ 重放 mob_2|mob_1（排序契约破坏或 Id 规则变化）
        var expected = "tick=300 treasury=100 | U mob_1 hp=30/30 | U mob_2 hp=30/30";
        var actual = "tick=300 treasury=100 | U mob_2 hp=30/30 | U mob_1 hp=30/30";
        var diffs = ReplayRunner.SemanticDiff(expected, actual);
        diffs.ShouldNotBeEmpty();
    }

    // ================= 端到端：档位流经 json 往返 =================

    [Fact]
    public void 档位_json往返_恒写键自文档化()
    {
        var script = new ReplayRunner.ReplayScript(42, [], [])
        {
            AssertLevel = ReplayRunner.AssertLevel.Semantic,
        };
        var loaded = ReplayScriptJson.Read(ReplayScriptJson.Write(script));
        loaded.AssertLevel.ShouldBe(ReplayRunner.AssertLevel.Semantic);
    }

    [Fact]
    public void 档位_旧json缺键_默认L3()
    {
        const string legacy = "{\"seed\":42,\"entries\":[],\"checkpoints\":[]}";
        var loaded = ReplayScriptJson.Read(legacy);
        loaded.AssertLevel.ShouldBe(ReplayRunner.AssertLevel.Literal);
    }

    // ================= 真世界端到端：同剧本 L3 红 / L2 绿 =================

    /// <summary>录一条小剧本（真实世界快照），返回录制端脚本。</summary>
    private static ReplayRunner.ReplayScript RecordScript(int seed, long finalTick)
    {
        var world = new LogicWorld(randomSeed: seed);
        using var w = ReplaySetup.Recorder(world, seed);

        ReplaySetup.AdvanceTo(world, 100);
        w.Mark("t100");
        w.Inject("PlaceBountyFlag", ReplaySetup.Args("lair1", 20));
        ReplaySetup.AdvanceTo(world, 250);
        w.Mark("t250");
        ReplaySetup.AdvanceTo(world, finalTick);
        w.Mark("t-final");

        return new ReplayRunner.ReplayScript(seed, w.Journal, w.Checkpoints);
    }

    /// <summary>
    /// 良性改动的机器证明：把录制检查点的期望快照做「调数值 + 加字段」两种良性漂移
    /// 后，L3 重放红、L2 重放绿——档位语义在真实快照上成立。
    /// </summary>
    [Fact]
    public void 真实快照_良性漂移_L3红_L2绿()
    {
        var script = RecordScript(seed: 42, finalTick: 400);

        // 良性漂移 #1：调数值（treasury +1）——L3 必红
        var drifted = script with
        {
            Checkpoints = script.Checkpoints
                .Select(c => c with { Snapshot = c.Snapshot.Replace("treasury=", "treasury=9") })
                .ToList(),
        };
        ReplayFails(drifted, ReplayRunner.AssertLevel.Literal).ShouldBeTrue("L3 逐字：调数值也必须红");
        ReplayFails(drifted, ReplayRunner.AssertLevel.Semantic).ShouldBeFalse("L2 语义：同键值漂移应容忍");

        // 良性漂移 #2：加字段（实体段内补一个键）——L3 必红、L2 容忍
        var withField = script with
        {
            Checkpoints = script.Checkpoints
                .Select(c => c with { Snapshot = c.Snapshot.Replace(" | U ", " | U morale=75 ") })
                .ToList(),
        };
        ReplayFails(withField, ReplayRunner.AssertLevel.Literal).ShouldBeTrue("L3 逐字：加字段也必须红");
        // 注意：加字段会破坏段首身份（U morale=75 ≠ U mob_1）——语义层容忍的是
        // 「键集漂移」而非「段首身份漂移」，此构造在段首注入 token 必红；见下条纯段内注键。
        ReplayFails(withField, ReplayRunner.AssertLevel.Semantic).ShouldBeTrue("段首注入 token = 身份漂移，L2 也红（身份不在容忍范围）");

        // 良性漂移 #3（真正的键集漂移）：同段尾部补键——L2 容忍
        var tailField = script with
        {
            Checkpoints = script.Checkpoints
                .Select(c => c with { Snapshot = c.Snapshot.Replace(" hp=", " hp=注=值 hp=") })
                .ToList(),
        };
        ReplayFails(tailField, ReplayRunner.AssertLevel.Semantic).ShouldBeFalse("L2 语义：段内补键（值域 token 对位错位）仍应容忍——键集漂移不是结构漂移");
    }

    /// <summary>
    /// 行为分歧的机器证明：命令 ok 翻转（录制 ok=true / 重放 ok=false）在 L2 下同样红
    /// ——断言分层只作用于快照断言，行为断言任何档位都是分歧。
    /// </summary>
    [Fact]
    public void 命令ok翻转_L2同样红()
    {
        var script = new ReplayRunner.ReplayScript(42,
            new List<ReplayRunner.JournalEntry>
            {
                new(100, "PlaceBountyFlag", "ghost|20", true, 0), // ghost 旗重放端必拒 → ok 翻转
            },
            new List<ReplayRunner.Checkpoint>())
        {
            AssertLevel = ReplayRunner.AssertLevel.Semantic,
        };
        ReplayFails(script, ReplayRunner.AssertLevel.Semantic).ShouldBeTrue("ok 翻转是行为分歧——任何档位都红");
    }

    /// <summary>执行一次重放，返回「是否出现断言失败」（收集 assertEqual 消息而非抛出）。</summary>
    private static bool ReplayFails(ReplayRunner.ReplayScript script, ReplayRunner.AssertLevel level)
    {
        var failed = false;
        using var world = new LogicWorld(randomSeed: script.Seed);
        ReplayRunner.Replay(script with { AssertLevel = level }, ReplaySetup.Hooks(world),
            msg => { if (msg != null) failed = true; });
        return failed;
    }
}
