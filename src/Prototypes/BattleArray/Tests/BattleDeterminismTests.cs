#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 确定性哨兵 —— 对照测试可信的前提（照无限领主 DeterminismSentinelTests 那条线）。
///
/// 守住三条边界：
/// 1. 同种子跑两遍：状态快照与事件流**逐字相等** ⇒ 逻辑层没有隐藏随机源、没有字典遍历序依赖；
/// 2. 逐步 vs 大块快进：推进的分块方式（= 调试时的速度档）不改变世界演化；
/// 3. 暂停冻结：冻结段空转、恢复后续跑，与「从没暂停过」逐字一致。
///
/// 外加一条本原型特有的：**敌我同族镜像必须打成同归于尽** —— 它是「没有先后手偏袒」的证明，
/// 对照测试把一切差异归给摆位，前提就是这套机制本身不偏袒任何一方（M8 起两侧要配成同一族才谈得上对称）。
/// </summary>
public class BattleDeterminismTests
{
    private const int Seed = 1001;
    private const int FinalTick = 3000;

    [Fact]
    public void 同种子跑两遍_状态与事件流逐字相等()
    {
        var (cpsA, eventsA) = Run(Seed, chunkTicks: 250, pauseFreeze: true);
        var (cpsB, eventsB) = Run(Seed, chunkTicks: 250, pauseFreeze: true);

        AssertSame(cpsA, cpsB, "状态快照（同种子两遍）");
        AssertSame(eventsA, eventsB, "事件流（同种子两遍）");
    }

    [Fact]
    public void 逐步推进与大块快进_结果逐字相等()
    {
        var (cpsStep, eventsStep) = Run(Seed, chunkTicks: 1, pauseFreeze: false);
        var (cpsBulk, eventsBulk) = Run(Seed, chunkTicks: 250, pauseFreeze: false);

        AssertSame(cpsStep, cpsBulk, "状态快照（一步一步 vs 一发跳 250 步）");
        AssertSame(eventsStep, eventsBulk, "事件流（一步一步 vs 一发跳 250 步）");
    }

    [Fact]
    public void 暂停冻结不改写世界_恢复后与从没暂停过逐字相等()
    {
        var (cpsPlain, eventsPlain) = Run(Seed, chunkTicks: 250, pauseFreeze: false);
        var (cpsFrozen, eventsFrozen) = Run(Seed, chunkTicks: 250, pauseFreeze: true);

        AssertSame(cpsPlain, WithoutFreezeProbe(cpsFrozen), "状态快照（没暂停 vs 暂停过）");
        AssertSame(eventsPlain, eventsFrozen, "事件流（没暂停 vs 暂停过）");
    }

    [Fact]
    public void 敌我同族镜像同摆法_必须同归于尽_机制本身不偏袒任何一方()
    {
        // 三种摆法各自跟自己打：全部必须判「同尽」。
        // 这是对照测试的归因地基 —— 敌方固定用常规线，若机制本身偏袒某一方，
        // 那三档之间的差就不是摆位的差，而是偏袒的差。任何一档出现胜负，本测试先红。
        //
        // M8 起的口径改动（只改「怎么造对称」，没改「对称证明什么」）：两族各带一条种族规则之后，
        // 两侧不同族本来就该分胜负（那条由 <c>UnitCardDataTests.异族镜像同摆法必分胜负</c> 专门钉），
        // 所以这里把两侧配成同一族 —— 对照跑本身也钉在同一族上（<see cref="PlacementComparison.ComparisonRace"/>）。
        foreach (var formation in Formations.ComparisonSet)
        {
            var mirrored = PlacementComparison.Controlled(formation);
            using var world = new BattleWorld(
                Seed, mirrored, mirrored, jitter: false);

            world.AdvanceUntilEnd().ShouldBeTrue($"{formation.Key} 满编对自己该在 120 秒上限内打完");
            world.Outcome.ShouldBe(BattleOutcome.Draw, $"{formation.Key} 同族镜像对战出现胜负 ⇒ 这套规则有先后手偏袒，对照测试的归因不成立");

            // 逐兵对称：我方第 n 号与敌方第 n 号的剩余血量、离中线深度必须完全相同（X 同、Y 反号）
            var ours = world.Units.Where(u => u.Side == Side.Player).ToList();
            var theirs = world.Units.Where(u => u.Side == Side.Enemy).ToList();
            theirs.Count.ShouldBe(ours.Count);
            for (var i = 0; i < ours.Count; i++)
            {
                theirs[i].Health.ShouldBe(ours[i].Health, $"{formation.Key}: {ours[i].Id}/{theirs[i].Id} 血量不对称");
                MathF.Abs(theirs[i].Position.Y + ours[i].Position.Y).ShouldBeLessThan(1e-4f, $"{formation.Key}: Y 没镜像");
                MathF.Abs(theirs[i].Position.X - ours[i].Position.X).ShouldBeLessThan(1e-4f, $"{formation.Key}: X 不该差");
            }
        }
    }

    [Fact]
    public void 换种子真的换一场仗_否则对照测试的20局只是同一局()
    {
        // 抖动开着才有意义：20 粒固定种子必须给出**不止一种**结果，
        // 否则「各跑 20 局」是假的样本量，平均数与单局无异。
        var durations = new HashSet<long>();
        var deathPairs = new HashSet<string>();
        foreach (var seed in PlacementComparison.Seeds)
        {
            var match = PlacementComparison.RunMatch(seed, Formations.ShieldWall, PlacementComparison.EnemyFormation);
            durations.Add(match.DurationTicks);
            deathPairs.Add($"{match.DurationTicks}:{match.OurDeaths}:{match.EnemyDeaths}");
        }

        durations.Count.ShouldBeGreaterThanOrEqualTo(5, $"20 粒种子只跑出 {durations.Count} 种时长 —— 样本量是假的");
        deathPairs.Count.ShouldBeGreaterThanOrEqualTo(5);
    }

    /// <summary>
    /// 默认对阵那一局（运行时进游戏看到的就是它）的<b>锚点数字</b> —— 离线必须算得出同一个结果。
    ///
    /// 为什么单独立一条：M5/M6 的复现是把运行时的判定行与离线报告「逐字对一眼」，靠人记。
    /// M8 之后种族规则进了数值、对照跑又钉在同族上 ⇒ 运行时那一局（人族 vs 兽人）与对照跑
    /// <b>不再是同一局</b>，再拿旧锚点比对就会得出「运行时与离线漂了」的假告警。
    /// 这条把锚点写进测试：表现层那条链（棋盘 → ToFormation → 世界）吃的数值与逻辑层同源，
    /// 不同源就红在这里，不用等到人去对日志。
    /// </summary>
    [Fact]
    public void 默认对阵那一局_离线算得出运行时念的那一行()
    {
        var board = new DeploymentBoard();                       // 布阵期默认阵（常规线满 15 格）
        var match = PlacementComparison.RunMatch(1001, board.ToFormation(), PlacementComparison.EnemyFormation);

        match.Outcome.ShouldBe(BattleOutcome.PlayerDefeat, "运行时判定行是「第 27.5 秒 PlayerDefeat 我方阵亡 15 敌方阵亡 12」，离线这里变了 ⇒ 两处不同源了");
        match.DurationSeconds.ShouldBe(27.5);
        match.OurDeaths.ShouldBe(15);
        match.EnemyDeaths.ShouldBe(12);
        match.FormationKey.ShouldBe(DeploymentBoard.CustomKey);
    }

    // === 哨兵剧本 ===

    /// <summary>
    /// 一次完整运行：满编对满编、每 250 步（或 1 步）推一次，每个采样点记一条全精度快照，
    /// 事件流按发生顺序逐行入账；中段可插一段 50 步冻结。
    /// </summary>
    private static (List<string> Checkpoints, List<string> Events) Run(int seed, int chunkTicks, bool pauseFreeze)
    {
        using var world = new BattleWorld(seed, Formations.ArchersMidline, PlacementComparison.EnemyFormation);
        var events = new List<string>();
        world.Attacked += row => events.Add($"atk {row}");
        world.Died += row => events.Add($"die {row}");
        world.BattleEnded += row => events.Add($"end {row}");

        var checkpoints = new List<string>();
        var target = 250L;
        while (world.Tick < FinalTick)
        {
            if (pauseFreeze && world.Tick == 1000)
            {
                world.SetPaused(true);
                for (var i = 0; i < 50; i++)
                {
                    world.Advance(1).ShouldBeFalse("暂停中 Advance 必须空转");
                }
                world.Tick.ShouldBe(1000, "暂停中步数不该动");
                checkpoints.Add($"t1000|freeze-probe|{Capture(world)}");
                world.SetPaused(false);
            }

            var remaining = Math.Min(chunkTicks, target - world.Tick);
            if (remaining > 0) world.Advance((int)remaining);
            if (world.Tick >= target)
            {
                checkpoints.Add($"t{target}| |{Capture(world)}");
                target += 250;
            }
        }
        return (checkpoints, events);
    }

    private static List<string> WithoutFreezeProbe(List<string> checkpoints)
        => checkpoints.Where(cp => !cp.Contains("freeze-probe")).ToList();

    private static void AssertSame(List<string> expected, List<string> actual, string what)
    {
        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            var a = i < expected.Count ? expected[i] : "<越界>";
            var b = i < actual.Count ? actual[i] : "<越界>";
            b.ShouldBe(a, $"{what} 第 {i} 项开始分歧（此前 {Math.Min(i, expected.Count)} 项逐字一致）\n" +
                $"  先头差异：{FirstDiff(a, b)}");
        }
    }

    private static string FirstDiff(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < length && a[i] == b[i]) i++;
        var from = Math.Max(0, i - 40);
        return $"…{a[Math.Max(0, i - 40)..Math.Min(a.Length, i + 40)]} ≠ …{b[from..Math.Min(b.Length, i + 40)]}";
    }

    /// <summary>全量状态一行：快照本身已按出场序逐行输出，这里拼成一项比对单元。</summary>
    private static string Capture(BattleWorld world)
    {
        var sb = new StringBuilder();
        sb.Append(world.DebugSnapshot().Replace('\n', '|'));
        return sb.ToString();
    }
}
