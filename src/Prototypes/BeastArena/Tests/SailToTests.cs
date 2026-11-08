#nullable enable
using System;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 手动航行那一发（<c>SailTo|x|z</c>，两个世界米）：改目标点、到点即停、
/// 改道取消进港、四道闸的拒绝，以及手动开出圆盘那条链。
///
/// <para><b>三处不是写路径的口子，出处写在这里</b>：进阶仪式那一格走
/// <see cref="ArenaWorld.AdvanceTier"/>（要先挖到门槛那一层，见 <c>BeastTierTests</c> 的写法）；
/// 停摆期那一格走 <see cref="ArenaWorld.ReportBreach"/>（攻破那一格，词表里没有「打一下镇政府」这一发）；
/// 其余每一发命令都走 <see cref="ArenaSetup.Inject"/>。</para>
///
/// <para><b>出界那一格挑的是北边</b>：大地图那张寻路网格从 −58 米起 516 × 516 格
/// （覆盖世界 −58..458 米），圆盘跨的是世界 −56..456 米 —— 四个方向都出得去，
/// 出界目标的判定见 <c>BigMapGridTests</c> 与 <c>BeastVoyageTests</c> 的四方向用例。</para>
/// </summary>
public class SailToTests
{
    private const int Seed = 7;

    /// <summary>推进到手动那一段走完（停泊态）或步数用尽。</summary>
    private static void SailUntilHolding(ArenaWorld world, int cap = 400)
    {
        for (var i = 0; i < cap && world.Voyage.Phase == VoyagePhase.Sailing; i++)
        {
            world.Advance(10);
        }
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 手动航行令改目标点_走到那一点就停()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        var target = new NavPoint(120.0, 220.0);

        // Act
        ArenaSetup.Inject(world, "SailTo", "120|220", out _, out var why).ShouldBeTrue(why);

        // Assert
        world.Voyage.ManualTarget.ShouldBe(target, "手动目标点记下来了。");
        world.Voyage.TargetCityId.ShouldBeNull("手动航行不指向任何城。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "兽在朝那一点走。");
        world.Voyage.DebugSegment().ShouldContain("manual=120.0,220.0", Case.Sensitive,
            "读数那一列带着手动目标点。");

        SailUntilHolding(world);

        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "到了就停（与进港那一格同一个停法）。");
        world.Voyage.ManualTarget.ShouldBeNull("到了之后手动目标交还。");
        NavArrive.Reached(world.Voyage.Position, target, ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"应当停在 {target} 附近，实际停在 {world.Voyage.Position}");
        world.Voyage.DebugSegment().ShouldContain("manual=-", Case.Sensitive,
            "读数那一列在没有手动目标时是「-」。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 停靠期发手动航行令_回先拔锚()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        // Act
        ArenaSetup.Inject(world, "SailTo", "120|220", out _, out var why).ShouldBeFalse();

        // Assert
        why.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive,
            "停靠期回 InvalidPhase。");
        why.ShouldContain("先拔锚", Case.Sensitive, "提示是「先拔锚」。");
        world.Voyage.ManualTarget.ShouldBeNull("被拒的那一发不记目标点。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 防守中发手动航行令_回打完了再走()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out var pullWhy).ShouldBeTrue(pullWhy);
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence, "拔锚就开防守。");

        // Act
        ArenaSetup.Inject(world, "SailTo", "120|220", out _, out var why).ShouldBeFalse();

        // Assert
        why.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive,
            "防守中回 InvalidPhase。");
        why.ShouldContain("打完了再走", Case.Sensitive, "提示是「打完了再走」。");
        world.Voyage.ManualTarget.ShouldBeNull("被拒的那一发不记目标点。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 仪式中发手动航行令_回仪式中()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var tierWhy).ShouldBeTrue(tierWhy);
        world.InRitual.ShouldBeTrue("仪式开起来了。");

        // Act
        ArenaSetup.Inject(world, "SailTo", "120|220", out _, out var why).ShouldBeFalse();

        // Assert
        why.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive,
            "仪式中回 InvalidPhase。");
        why.ShouldContain("仪式中", Case.Sensitive, "提示是「仪式中」。");
        world.Voyage.ManualTarget.ShouldBeNull("被拒的那一发不记目标点。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 城破当格解救_手动航行令不再停在等解救()
    {
        // Arrange：攻破那一格当格解救，ticket 06 起没有停摆期。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.ReportBreach();
        world.InStandstill.ShouldBeFalse("城破当格解救，不再进停摆期。");

        // Act：停摆那一档已经不成立，手动航行令发得出去。
        ArenaSetup.Inject(world, "SailTo", "120|220", out _, out var why).ShouldBeTrue(why);

        // Assert
        world.Voyage.ManualTarget.ShouldBe(new DemoFactory.GugaFramework.Pathfinding.NavPoint(120.0, 220.0),
            "被受理的那一发记下目标点。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 手动改道_自动航行的城目标清掉_不再进港()
    {
        // Arrange：先让「停靠」那一发开着自动航行（兽朝富城开）。
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.Inject(world, "Dock", BigMapGrid.RichCity.Id, out _, out var dockWhy).ShouldBeTrue(dockWhy);
        world.Voyage.TargetCityId.ShouldBe(BigMapGrid.RichCity.Id, "自动航行的目标城记下来了。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "兽在朝富城开。");

        // Act：玩家右键改道。
        var target = new NavPoint(120.0, 220.0);
        ArenaSetup.Inject(world, "SailTo", "120|220", out _, out var why).ShouldBeTrue(why);

        // Assert
        world.Voyage.TargetCityId.ShouldBeNull("改道即取消进港：目标城清掉了。");
        world.Voyage.ManualTarget.ShouldBe(target, "手动目标点记下来了。");

        SailUntilHolding(world);

        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "到手动目标就停。");
        world.IsDocked.ShouldBeFalse("不进港：目标城那一格已经空了。");
        world.DockedCityId.ShouldBeEmpty("停靠城 id 也没记。");
        NavArrive.Reached(world.Voyage.Position, target, ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"应当停在手动目标附近，实际停在 {world.Voyage.Position}");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 手动那一段不脱队觅食_玩家在掌舵()
    {
        // Arrange：饱食度压到脱队门槛以下，手动目标就摆在生态对象旁边（觅食半径 30 米之内）。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(20.0);
        var eco = BigMapGrid.EcoNodeSpecs[0];
        var target = new NavPoint(eco.Position.X + 2.0, eco.Position.Z + 2.0);
        target.DistanceTo(eco.Position).ShouldBeLessThan(ArenaTuning.FoodSenseMeters,
            "手动目标就摆在生态对象旁边。");

        // Act
        ArenaSetup.Inject(world, "SailTo",
            $"{target.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}|"
            + $"{target.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            out _, out var why).ShouldBeTrue(why);

        // 一路盯着离生态对象最近的那一段：自动航行那一档走到这里会脱队。
        // 预算按开局位置算：从贫城西边的野地到这一点约 120 米，饿着走（0.7 倍速）要 4300 步。
        var closest = double.MaxValue;
        for (var i = 0; i < 700 && world.Voyage.Phase == VoyagePhase.Sailing; i++)
        {
            world.Advance(10);
            closest = Math.Min(closest, world.Voyage.Position.DistanceTo(eco.Position));
        }

        // Assert
        closest.ShouldBeLessThan(ArenaTuning.FoodSenseMeters,
            "那一趟路从生态对象旁边过 —— 自动航行那一档走到这里会脱队觅食。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "兽一直朝手动目标走，到了就停。");
        world.Ecology.Find(eco.Id)!.AvailableAt(world.Tick).ShouldBeTrue(
            "手动那一段不吃东西（不脱队觅食）。");
        NavArrive.Reached(world.Voyage.Position, target, ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"应当停在手动目标附近，实际停在 {world.Voyage.Position}");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 参数不是数字_回InvalidArgs_不兜0()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = world.Voyage.Position;

        // Act
        ArenaSetup.Inject(world, "SailTo", "abc|220", out _, out var why).ShouldBeFalse();

        // Assert
        why.ShouldStartWith($"rejected: {RejectReason.InvalidArgs}", Case.Sensitive,
            "解析不出数字回 InvalidArgs。");
        world.Voyage.ManualTarget.ShouldBeNull("不兜 0：目标点没记下来。");
        world.Voyage.Position.ShouldBe(before, "兽没动。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "没下航行令。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 规范化参数是那一个串_不变文化()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();

        // Act
        ArenaSetup.Inject(world, "SailTo", "210.5|300.25", out var canonical, out _).ShouldBeTrue();

        // Assert
        canonical.ShouldBe("210.5|300.25", "规范化后是 x|z 那一串（不变文化）。");

        // 指数写法也归到同一串：同一逻辑事实在账上只有一种 token。
        ArenaSetup.Inject(world, "SailTo", "1e2|210.5", out var exponent, out _).ShouldBeTrue();
        exponent.ShouldBe("100|210.5", "指数写法被排成同一个 token。");

        // 小数点写成逗号那一档按非法参数拒，不按本地化读（不变文化）。
        ArenaSetup.Inject(world, "SailTo", "210,5|300,25", out _, out var commaWhy).ShouldBeFalse();
        commaWhy.ShouldStartWith($"rejected: {RejectReason.InvalidArgs}", Case.Sensitive,
            "小数点是点不是逗号。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 手动开出圆盘_魂石扣完送回_还能再开出去一次()
    {
        // Arrange：目标点在圆盘北边（世界 0..512 米的寻路网格之内）。
        var world = ArenaWorldBuilder.New(Seed).Build();

        // Act（第一趟）
        ArenaSetup.Inject(world, "SailTo", "90|440", out _, out var why).ShouldBeTrue(why);

        var wentOut = false;
        for (var i = 0; i < 200 && !wentOut; i++)
        {
            world.Advance(100);
            wentOut = BigMapGrid.IsOutOfBounds(world.Voyage.Position);
        }

        wentOut.ShouldBeTrue($"应当走到圆盘外，实际停在 {world.Voyage.Position}");
        world.Voyage.SoulStones.ShouldBeLessThan(ArenaTuning.StartingSoulStones,
            "出界期间魂石照扣。");

        for (var i = 0; i < 400 && world.Voyage.SoulStones > 0.0; i++)
        {
            world.Advance(50);
        }

        world.Voyage.SoulStones.ShouldBe(0.0, "魂石扣到零。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "扣到零就送回圆盘内侧，回到停泊态。");
        world.Voyage.ManualTarget.ShouldBeNull("送回之后不再朝任何去处走。");
        BigMapGrid.IsInsideEdge(world.Voyage.Position).ShouldBeTrue(
            $"送回点落在圆盘内侧，实际在 {world.Voyage.Position}");

        // Act（第二趟）：送回不是终态，玩家照样能再开出去一次。
        ArenaSetup.Inject(world, "SailTo", "90|440", out _, out var againWhy).ShouldBeTrue(againWhy);
        world.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "送回之后照样能再开船。");

        var wentOutAgain = false;
        for (var i = 0; i < 400 && !wentOutAgain; i++)
        {
            // 一步一步盯着：魂石已经是零，出了界下一个整游戏分钟就送回 ——
            // 一次推一百步的话，出去与送回都落在同一格里，盯不到「在界外」那一段。
            world.Advance(1);
            wentOutAgain = BigMapGrid.IsOutOfBounds(world.Voyage.Position);
        }

        wentOutAgain.ShouldBeTrue($"第二趟也应当走到圆盘外，实际停在 {world.Voyage.Position}");

        // 魂石已经是零：出了界就送回（送回那一格判的是「在界外」，与第一趟同一条链）。
        for (var i = 0; i < 400 && BigMapGrid.IsOutOfBounds(world.Voyage.Position); i++)
        {
            world.Advance(10);
        }

        BigMapGrid.IsInsideEdge(world.Voyage.Position).ShouldBeTrue(
            $"第二趟也送回内侧，实际在 {world.Voyage.Position}");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "送回之后回到停泊态。");
    }
}
