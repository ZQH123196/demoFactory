#nullable enable
using System.Collections.Generic;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 攻破与停摆那一件：攻破步号、停摆到期、待记的债、战损与被俘名单、解救。
///
/// <para><b>这一份钉的是「能脱离世界单独活」</b>：不造世界，直接 <see cref="SiegeState"/>
/// 构造、喂 tick 与一次战损估值。破坏性后果（毁建筑、掳人、扣货、收场、写负债账）
/// 留在世界里，由既有的 <c>BattleTests</c> / <c>BreachRecoveryTests</c> 继续钉着。</para>
/// </summary>
public class SiegeStateTests
{
    [Fact]
    public void 单独构造_未攻破_不在停摆_没有战损与被俘()
    {
        var siege = new SiegeState();

        siege.Breached.ShouldBeFalse("开局没被攻破。");
        siege.BreachTick.ShouldBe(-1, "没攻破过，步号是 -1。");
        siege.PendingDebt.ShouldBe(0.0, 1e-9, "没有待记的债。");
        siege.StandstillUntilTick.ShouldBe(0, "不在停摆期。");
        siege.InStandstillAt(0).ShouldBeFalse("没有停摆期。");
        siege.RescueDueAt(0).ShouldBeFalse("没攻破就谈不上解救。");
        siege.WarLossBuildings.ShouldBeEmpty("没有战损。");
        siege.TakenPeople.ShouldBeEmpty("没有被俘。");
        siege.RescueFactionId.ShouldBe(string.Empty, "还没选定解救势力。");
    }

    [Fact]
    public void 债额按一点五倍向上折_不足下界取下界_且是整数金币()
    {
        SiegeState.DebtFrom(2560.0).ShouldBe(3840.0, 1e-9, "2560 × 1.5 = 3840。");
        SiegeState.DebtFrom(100.0).ShouldBe(ArenaTuning.DebtFloor, 1e-9, "150 低于下界，取 500。");
        SiegeState.DebtFrom(0.0).ShouldBe(ArenaTuning.DebtFloor, 1e-9, "没战损也保底 500。");
        SiegeState.DebtFrom(123.4).ShouldBe(500.0, 1e-9, "折完 185.1，仍取下界。");
        SiegeState.DebtFrom(1001.0).ShouldBe(1502.0, 1e-9, "1001 × 1.5 取整 = 1502。");
    }

    [Fact]
    public void 攻破记下步号与三样名单_当格就该解救_不再排停摆期()
    {
        var siege = new SiegeState();

        var accepted = siege.BeginBreach(
            tick: 500,
            rescueFactionId: ArenaWorld.PoorFactionId,
            estimatedLoss: 2560.0,
            warLossBuildings: [ArenaWorld.ArenaId, ArenaWorld.RestaurantId, "training_ground"],
            takenPeople: ["w1", "w2"]);

        accepted.ShouldBeTrue("第一次攻破受理。");
        siege.Breached.ShouldBeTrue("攻破即置位。");
        siege.BreachTick.ShouldBe(500, "记下攻破那一刻的步号。");
        siege.StandstillUntilTick.ShouldBe(0, "ticket 06 起不再排三个游戏日的停摆。");
        siege.InStandstillAt(500).ShouldBeFalse("攻破那一刻不在停摆期里。");
        siege.RescueDueAt(500).ShouldBeTrue("城卫队当格就该到场解救。");
        siege.PendingDebt.ShouldBe(3840.0, 1e-9, "战损估值 2560 折 1.5 倍。");
        siege.RescueFactionId.ShouldBe(ArenaWorld.PoorFactionId, "记下该还给谁。");
        siege.WarLossBuildings.ShouldBe(
            [ArenaWorld.ArenaId, ArenaWorld.RestaurantId, "training_ground"], Case.Sensitive);
        siege.TakenPeople.ShouldBe(["w1", "w2"], Case.Sensitive);
    }

    [Fact]
    public void 再攻破一次被拒_第一次的读数一格不动()
    {
        var siege = new SiegeState();
        siege.BeginBreach(500, ArenaWorld.PoorFactionId, 2560.0, ["arena"], ["w1"]);

        var accepted = siege.BeginBreach(900, ArenaWorld.RichFactionId, 9999.0, ["church"], ["w2"]);

        accepted.ShouldBeFalse("已经攻破过就不再受理。");
        siege.BreachTick.ShouldBe(500, "步号不覆盖。");
        siege.PendingDebt.ShouldBe(3840.0, 1e-9, "债额不覆盖。");
        siege.RescueFactionId.ShouldBe(ArenaWorld.PoorFactionId, "势力不覆盖。");
        siege.WarLossBuildings.ShouldBe(["arena"], Case.Sensitive);
        siege.TakenPeople.ShouldBe(["w1"], Case.Sensitive);
    }

    [Fact]
    public void 攻破那一格就该解救_之后不再重复()
    {
        var siege = new SiegeState();
        siege.BeginBreach(100, ArenaWorld.PoorFactionId, 2560.0, ["arena"], ["w1"]);

        siege.RescueDueAt(100).ShouldBeTrue("攻破当格就该解救。");
        siege.RescueDueAt(101).ShouldBeTrue("晚一格读到仍然该解救（世界当格就会调它）。");

        siege.Rescue();

        siege.RescueDueAt(101).ShouldBeFalse("解除之后不再重复解救。");
    }

    [Fact]
    public void 解救清掉攻破与停摆_把待记的债与势力交还并清零_名单留着备查()
    {
        var siege = new SiegeState();
        siege.BeginBreach(100, ArenaWorld.PoorFactionId, 2560.0, ["arena"], ["w1"]);

        var (debt, faction) = siege.Rescue();

        debt.ShouldBe(3840.0, 1e-9, "待记的债交还给世界去记账。");
        faction.ShouldBe(ArenaWorld.PoorFactionId, "记给哪一家一并交还。");
        siege.Breached.ShouldBeFalse("攻破解除。");
        siege.StandstillUntilTick.ShouldBe(0, "停摆期结束。");
        siege.PendingDebt.ShouldBe(0.0, 1e-9, "待记的债清零。");
        siege.RescueDueAt(100 + ArenaTime.StepsPerGameDay).ShouldBeFalse("解除之后不再重复解救。");
        siege.WarLossBuildings.ShouldBe(["arena"], Case.Sensitive, "战损名单留着备查。");
        siege.TakenPeople.ShouldBe(["w1"], Case.Sensitive, "被俘名单留着备查。");
    }

    [Fact]
    public void 没攻破时解救是空单_状态仍是干净的()
    {
        var siege = new SiegeState();

        var (debt, faction) = siege.Rescue();

        debt.ShouldBe(0.0, 1e-9, "没有待记的债。");
        faction.ShouldBe(string.Empty, "没有势力。");
        siege.Breached.ShouldBeFalse();
        siege.StandstillUntilTick.ShouldBe(0);
    }

    [Fact]
    public void 最近势力_量到停靠点_同距取先登记的那一家_没有债主回空串()
    {
        var at = new NavPoint(10.0, 0.0);
        var towns = new (string FactionId, NavPoint StopPoint)[]
        {
            (ArenaWorld.PoorFactionId, new NavPoint(0.0, 0.0)),
            (ArenaWorld.RichFactionId, new NavPoint(100.0, 0.0)),
        };

        SiegeState.NearestFaction(at, towns).ShouldBe(ArenaWorld.PoorFactionId, "10 米离贫城更近。");

        // 正中间：两家一样远 —— 按登记次序取先登记那一家的（严格小于才换人）。
        var middle = new NavPoint(50.0, 0.0);
        SiegeState.NearestFaction(middle, towns).ShouldBe(
            ArenaWorld.PoorFactionId, "同距取先登记的那一家。");

        SiegeState.NearestFaction(at, []).ShouldBe(string.Empty, "一家债主都没有就回空串。");
    }
}
