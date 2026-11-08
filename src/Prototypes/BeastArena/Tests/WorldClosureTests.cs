#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Tests;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 连续三轮停靠闭合 —— <c>design.md</c> 12.1 的头一条命题，也是整台核心循环验证机的正题。
///
/// <para><b>跑的是什么</b>：从贫城出发，在贫城与富城之间往返跑满三轮，每一轮都是
/// 「停靠 → 空艇成交 → 游客进镇消费 → 办赛 → 拔锚限时防守 → 轮结评价 → 航行（途中脱队觅食）→ 下一城」，
/// 每个检查点上 <c>IsConserved()</c> 都为真。</para>
///
/// <para><b>走的是玩家那条写路径</b>：每一发命令都过 <see cref="ReplayAssembly.Inject"/>（也就是
/// <c>ArenaSetup.Inject</c>，与调试桥、菜单、重放端共用同一口），本文件不碰任何内部字段去改状态。
/// 每个检查点按「先快照后命令」的次序录：<see cref="ReplayAssembly.Mark"/> 先落快照、
/// <see cref="ReplayAssembly.Inject"/> 再记命令 —— 两条共用一条序号时间线，同一步内的先后因此被钉死，
/// 末尾按 <c>(步号, 序号)</c> 稳定排序还原的那一格就在这里。</para>
///
/// <para><b>一处不是写路径的口子，出处写在这里</b>：脱队觅食用
/// <see cref="BeastVoyage.SetSatiation"/> 摆前置条件（饱食度 &lt; 30）——
/// 没有任何动词能把饱食度调低，从 80 自然衰减到 30 要 50 个游戏小时（5 万步）。
/// 那一口是 <c>BeastVoyage</c> 上专为「衰减太慢、测试等不起」留的调试直写口。
/// 建造空船停靠区那一步走的是 <c>PlaceBuilding</c>，是真写路径。</para>
/// </summary>
public class WorldClosureTests
{
    private const int Seed = 7;

    /// <summary>停靠期停留步数（1800 步 = 3 个游戏小时）——游客进镇消费那一段。</summary>
    private const int DwellSteps = 1800;

    /// <summary>一个航段的步数上限：两城 240 米 ÷ 0.4 米每现实秒 = 10 游戏小时 = 6000 步，留一半余量。</summary>
    private const int LegStepCap = 12000;

    /// <summary>一场仗的步数上限：办赛 1.5 游戏小时 900 步、防守 2 游戏小时 1200 步，留余量。</summary>
    private const int FightStepCap = 2000;

    /// <summary>脱队觅食那一档把饱食度摆到的数（低于 30 才考虑脱队）。</summary>
    private const double HungrySatiation = 20.0;

    /// <summary>三轮停靠的城序：贫 → 富 → 贫 → 富（三轮停靠，四段航程，最后多出第 4 项是终点）。</summary>
    private static readonly string[] Legs = ["poor_city", "rich_city", "poor_city", "rich_city"];

    /// <summary>跑几轮停靠。</summary>
    private const int RoundCount = 3;

    [Fact]
    public void 连续三轮停靠闭合_全程守恒()
    {
        var clock = Stopwatch.StartNew();
        using var run = new ClosureRun(Seed);

        // 开局先立一件空船停靠区（那一件开局留空，spec 说由局内 PlaceBuilding 造）。
        // 有了它，每航段到访数是 1..3 艘 —— 每一轮都必定有一扇成交窗口可开；
        // 没有它时贫城实测掷出过 0，那一趟就没有窗口。
        run.Check("r0-空船停靠区");
        run.Command("PlaceBuilding", "AirshipDock|-11|-11")
            .ShouldBeTrue("空船停靠区落座：它把每航段的空艇到访数从 0..2 抬到 1..3。");
        run.World.HasBlueprint("AirshipDock").ShouldBeTrue();

        var detouredLegs = 0;
        for (var round = 0; round < RoundCount; round++)
        {
            var index = round + 1;
            var here = Legs[round];
            var next = Legs[round + 1];
            var legBefore = run.AllLegPhases.Count;

            // ① 停靠：先抓快照，再发 Dock 令，再走到位。
            run.Check($"r{index}-发停靠令前");
            run.Command("Dock", here).ShouldBeTrue($"第 {index} 轮下 Dock {here}。");
            run.SailTo(here);
            run.World.IsDocked.ShouldBeTrue($"第 {index} 轮停到了 {here}。");
            run.World.DockedCityId.ShouldBe(here, "停稳的城 id。");
            run.World.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "停稳之后巨兽是停泊态。");

            // ② 空艇成交：窗口在停稳那一刻开，30 个游戏分钟。
            run.Check($"r{index}-停稳");
            run.Command("TradeAirship", "30|1|0").ShouldBeTrue($"第 {index} 轮在空艇窗口里成交一次。");
            run.Trades = run.CountVerb("TradeAirship");

            // ③ 游客进镇消费：街上有人、金币账动过（游客在饭店酒店花钱）。
            var coinBeforeDwell = run.World.Town.Coin;
            run.World.Advance(DwellSteps);
            run.World.Visitors.InTownCount.ShouldBeGreaterThan(0, $"第 {index} 轮停靠期街上该有游客。");
            run.World.Town.Coin.ShouldBeGreaterThan(coinBeforeDwell,
                $"第 {index} 轮游客消费要有进账 —— 街上一堆人却一文不进，钱就只出不进。");
            run.Check($"r{index}-游客消费");

            // ④ 办赛（停靠期开赛，打满 1.5 游戏小时；门票按在镇观众结算）。
            var coinBeforeMatch = run.World.Town.Coin;
            var (sideA, sideB) = run.Teams();
            run.Check($"r{index}-发办赛令前");
            run.Command("StartMatch", $"{sideA}|{sideB}").ShouldBeTrue($"第 {index} 轮开赛。");
            run.World.Battle.Occasion.ShouldBe(BattleOccasion.Match);
            run.FightToTheEnd();
            run.MatchesPlayed = run.CountVerb("StartMatch");
            run.World.Battle.Occasion.ShouldBe(BattleOccasion.None);
            run.World.Town.Coin.ShouldBeGreaterThan(coinBeforeMatch,
                $"第 {index} 轮办赛要有门票进账（在镇观众 × 基础消费 × 镇名声那一档）。");
            run.Check($"r{index}-办赛完");

            // ⑤ 拔锚限时防守（轮结评价就在这一发里算，之后清空在镇游客并开 2 游戏小时防守）。
            var moodAtPull = run.World.Visitors.AverageMood;
            var reputationBefore = run.World.Visitors.TownReputation;
            var inTownAtPull = run.World.Visitors.InTownCount;
            inTownAtPull.ShouldBeGreaterThan(0,
                $"第 {index} 轮拔锚那一刻街上该还有人 —— 一趟停靠的中后段没人，"
                + "说明游客池那份预算放得太快（见 ArenaTuning.VisitorEnterPerGameMinute 那一条）。");
            run.Check($"r{index}-发拔锚令前");
            run.Command("PullAnchor", "").ShouldBeTrue($"第 {index} 轮拔锚。");
            TestLog.Ledger($"第 {index} 轮 {here}：拔锚前在镇 {inTownAtPull} 人、平均情绪 {moodAtPull:0.###}、"
                + $"镇名声 {reputationBefore:0.##} → 拔锚后 {run.World.Visitors.TownReputation:0.##}");
            run.World.Battle.Occasion.ShouldBe(BattleOccasion.Defence);
            run.World.DockedCityId.ShouldBeEmpty("拔锚即离城。");
            run.World.Visitors.InTownCount.ShouldBe(0, "拔锚清空在镇游客。");
            run.World.Visitors.TownReputation.ShouldBe(
                reputationBefore + (moodAtPull >= 0.0
                    ? ArenaTuning.ReviewReputationDelta
                    : -ArenaTuning.ReviewReputationDelta),
                1e-9,
                "轮结评价在拔锚那一发里按在镇游客平均情绪加减了镇名声。");
            run.FightToTheEnd();
            run.DefencesSurvived = run.CountVerb("PullAnchor");
            run.World.Battle.Occasion.ShouldBe(BattleOccasion.None);
            run.Check($"r{index}-防守完");

            // ⑥ 航行：途中脱队觅食，吃掉沿途生态对象，再到下一座城。
            run.World.Voyage.SetSatiation(HungrySatiation);
            run.Check($"r{index}-发行航令前");
            run.Command("Dock", next).ShouldBeTrue($"第 {index} 轮朝 {next} 去。");
            run.World.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "下了航行令就是航行态。");
            run.SailTo(next);

            var legPhases = run.AllLegPhases.Skip(legBefore).ToList();
            if (legPhases.Contains(VoyagePhase.DetouringToFood))
            {
                detouredLegs++;
                legPhases.ShouldContain(VoyagePhase.Eating, "到生态对象旁原地进食 5 个游戏分钟。");
                legPhases.ShouldContain(VoyagePhase.ReturningToWaypoint, "吃完先回航线原断点。");
                legPhases.IndexOf(VoyagePhase.DetouringToFood).ShouldBeLessThan(
                    legPhases.IndexOf(VoyagePhase.ReturningToWaypoint), "脱队在返航之前。");
            }
            else
            {
                legPhases.ShouldNotContain(VoyagePhase.DetouringToFood,
                    "这一趟 30 米内确实没有可吞对象，不该凭空脱队。");
            }

            run.World.IsDocked.ShouldBeTrue($"第 {index} 轮末尾停到了 {next}。");
            run.World.DockedCityId.ShouldBe(next, "下一轮停稳的城 id。");
        }

        var elapsed = clock.ElapsedMilliseconds;
        TestLog.Ledger($"连续三轮停靠闭合：{elapsed} ms，{run.World.Tick} 步"
            + $"（{ArenaTime.GameMinutesAt(run.World.Tick)} 个游戏分钟），"
            + $"命令 {run.Commands.Count} 发、检查点 {run.Labels.Count} 个、最终城 {run.World.DockedCityId}。");

        // ---- 全程收口 ----
        run.World.Town.IsConserved().ShouldBeTrue("三轮跑完仍然要平：" + run.World.Town.ConservedReport());
        run.World.DockedCityId.ShouldBe("rich_city", "三轮走完停在富城上。");
        run.Trades.ShouldBe(RoundCount, "每一轮都在空艇窗口里成交一次。");
        run.MatchesPlayed.ShouldBe(RoundCount, "三轮各打一场办赛。");
        run.DefencesSurvived.ShouldBe(RoundCount, "三轮各扛过一场拔锚防守。");
        detouredLegs.ShouldBeGreaterThan(0,
            "四段航程里饿着走的那几趟应当在沿途吃掉至少一件生态对象。");
        run.World.Ecology.Nodes.Count(n => !n.AvailableAt(run.World.Tick)).ShouldBeGreaterThan(0,
            "生态对象确实被吃掉过（吃完的那一件在再生窗口内不可吞）。");
        run.World.TownHallBreached.ShouldBeFalse("三轮打完不该有镇政府被攻破 —— 输的路径另有那一组用例。");

        // 「写路径全录」：发过的每一发都在账上，账上没有多出来的一发。
        run.Recorder.Journal.Count.ShouldBe(run.Commands.Count,
            "发了几发就要录几发 —— 漏录一次重放端就分叉一次。");
        for (var i = 0; i < run.Commands.Count; i++)
        {
            run.Recorder.Journal[i].Cmd.ShouldBe(run.Commands[i].Verb, $"第 {i} 发的动词。");
            run.Recorder.Journal[i].Tick.ShouldBe(run.Commands[i].Tick, $"第 {i} 发落在第几步。");
        }

        // 「先快照后命令」：配成对的那些要同一步，且检查点序号排在命令之前。
        run.Pairs.ShouldNotBeEmpty("这条断言要有配对才验得到 —— 一个同 tick 的快照加命令都没有就是空话。");
        foreach (var pair in run.Pairs)
        {
            pair.Checkpoint.Tick.ShouldBe(pair.Command.Tick,
                $"快照「{pair.Checkpoint.Label}」与命令 {pair.Command.Cmd} 落在同一步上。");
            pair.Checkpoint.Seq.ShouldBeLessThan(pair.Command.Seq,
                $"快照「{pair.Checkpoint.Label}」要排在同一步内那条命令之前（序号即发生顺序）。");
        }
    }

    /// <summary>跑一轮停靠。命令走 <see cref="ReplayAssembly.Inject"/>，检查点先于命令。</summary>
    private sealed class ClosureRun : IDisposable
    {
        private ReplayRunner.Checkpoint? _pendingMark;

        public ClosureRun(int seed)
        {
            World = ArenaWorldBuilder.New(seed).Build();
            Recorder = ArenaSetup.Recorder(World);
            Recorder.BeginSession();
        }

        public ArenaWorld World { get; }

        public ReplayAssembly Recorder { get; }

        /// <summary>发过的每一发（命令 + 落点步号）。</summary>
        public List<(string Verb, string Args, bool Ok, long Tick)> Commands { get; } = [];

        public List<string> Labels { get; } = [];

        /// <summary>同一步内的「快照在前、命令在后」配对。</summary>
        public List<(ReplayRunner.Checkpoint Checkpoint, ReplayRunner.JournalEntry Command)> Pairs { get; } = [];

        /// <summary>每一步采到的航行阶段（航段边界按 <see cref="SailTo"/> 内的清空重采，全量另存一份）。</summary>
        public List<VoyagePhase> AllLegPhases { get; } = [];

        public int Trades { get; set; }

        public int MatchesPlayed { get; set; }

        public int DefencesSurvived { get; set; }

        public void Dispose() => Recorder.Dispose();

        /// <summary>抓一个检查点（快照先落），并在落点立刻断一次守恒。</summary>
        public void Check(string label)
        {
            Recorder.Mark(label);
            Labels.Add(label);
            _pendingMark = Recorder.Checkpoints[^1];
            Conserved(label);
        }

        /// <summary>守恒断言（八条等式那一口的唯一出口）。</summary>
        public void Conserved(string label)
        {
            World.Town.IsConserved().ShouldBeTrue(
                $"检查点「{label}」@tick={World.Tick} 守恒破了：{World.Town.ConservedReport()}");
        }

        /// <summary>发一发命令（走写路径唯一入口，进 Journal）。</summary>
        public bool Command(string verb, string args)
        {
            var ok = Recorder.Inject(verb, args);
            Commands.Add((verb, args, ok, World.Tick));
            if (_pendingMark is { } mark)
            {
                Pairs.Add((mark, Recorder.Journal[^1]));
                _pendingMark = null;
            }

            return ok;
        }

        /// <summary>账上某个动词发过几发（按命令清单数，不看 Journal）。</summary>
        public int CountVerb(string verb)
        {
            var n = 0;
            foreach (var c in Commands)
            {
                if (c.Verb == verb) n++;
            }

            return n;
        }

        /// <summary>推进到停靠在 <paramref name="cityId"/> 那一刻（上限 <see cref="LegStepCap"/> 步）。</summary>
        public void SailTo(string cityId)
        {
            for (var i = 0; i < LegStepCap; i++)
            {
                World.Advance(1);
                AllLegPhases.Add(World.Voyage.Phase);
                if (World.IsDocked && World.DockedCityId == cityId) return;
            }

            throw new InvalidOperationException(
                $"{LegStepCap} 步之内没停到 {cityId}：阶段 {World.Voyage.Phase}、"
                + $"目标 {World.Voyage.TargetCityId}、位置 {World.Voyage.Position}");
        }

        /// <summary>推进到这一场仗打完（场合回到 <see cref="BattleOccasion.None"/>）。</summary>
        public void FightToTheEnd()
        {
            for (var i = 0; i < FightStepCap; i++)
            {
                World.Advance(1);
                if (World.Battle.Occasion == BattleOccasion.None) return;
            }

            throw new InvalidOperationException(
                $"{FightStepCap} 步之内这一场没打完：场合 {World.Battle.Occasion}。");
        }

        /// <summary>从在册活物里现挑还能打的：四名角斗士打四名常驻自由人。</summary>
        public (string SideA, string SideB) Teams()
        {
            var sideA = Live(UnitRole.Gladiator, 4);
            var sideB = Live(UnitRole.FreePerson, 4);
            if (sideB.Count == 0) sideB = Live(UnitRole.Worker, 4);
            sideA.ShouldNotBeEmpty("办赛甲队得有能打的人。");
            sideB.ShouldNotBeEmpty("办赛乙队得有能打的人。");
            return (string.Join(',', sideA), string.Join(',', sideB));
        }

        private List<string> Live(UnitRole role, int take)
        {
            var ids = new List<string>(take);
            foreach (var u in World.Roster.Units)
            {
                if (ids.Count >= take) break;
                if (u.Role != role || !u.CanFight) continue;
                ids.Add(u.Id);
            }

            return ids;
        }
    }
}
