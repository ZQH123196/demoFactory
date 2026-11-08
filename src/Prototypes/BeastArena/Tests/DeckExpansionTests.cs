#nullable enable
using System.Linq;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// ticket 27「二阶甲板扩容」：一阶 24×24 格（48 米），二阶 28×28 格（56 米），一格 2 米。
/// 扩容在进阶完成那一刻生效，永久；寻路网格、蓝图放置边界、落点序列与最终重试还原同步。
///
/// <para><b>既有建筑与布局不搬迁的那条规则</b>（spec「二阶进阶」表「甲板」那一行的落地口径）：
/// 建造格的世界落点只由「格号 × 格长」推出，与边界无关 —— 外扩只是把界外的格变成界内，
/// 任何一格的格号与落点一寸不动。寻路格号 → 空间坐标的平移恒按最大档（14 格 = 28 米）算，
/// 扩容不改两张网格的相对位置，只改外圈那一环的通过性与建造边界。</para>
/// </summary>
public class DeckExpansionTests
{
    private const int Seed = 7;

    /// <summary>走到「仪式到点、位阶 +1」之后的世界。</summary>
    private static ArenaWorld AdvancedToWorld(ArenaWorld world)
    {
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.InRitual.ShouldBeTrue("仪式开起来了（一游戏小时）。");
        world.Advance(ArenaTime.StepsPerGameHour + 1);
        world.Tier.ShouldBe(BeastTier.Tier2, "仪式走完，位阶 +1。");
        return world;
    }

    /// <summary>扩容新增那一角上的建造格（一阶界外、二阶界内）。</summary>
    private static readonly GridCell RingCell = new(12, 12);

    // ---- 一阶尺寸 ----

    [Fact]
    public void 一阶甲板_二十四格四十八米_寻路网格按最大档建且外圈带阻挡()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.DeckCells.ShouldBe(ArenaTuning.DeckCellsTier1, "一阶 24 格。");
        world.DeckSpanMeters.ShouldBe(48.0, "一格 2 米：边长 48 米。");
        world.DeckCellBounds.ShouldBe((-12, -12, 11, 11), "建造边界是一阶那一档。");

        // 寻路网格恒按二阶最大档 28×28 建：扩容不换网格，只解外圈阻挡。
        world.DeckGrid.CellsX.ShouldBe(ArenaTuning.DeckCellsTier2);
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(new GridCell(11, 11)))
            .ShouldBeFalse("一阶最外一格是通的。");
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(RingCell))
            .ShouldBeTrue("扩容新增那一格此刻还是墙。");

        // 派生读数同一来源：边长 = 格数 × 格长。
        ArenaTuning.DeckSpanMeters(ArenaTuning.DeckCellsTier1).ShouldBe(
            world.DeckSpanMeters, "读数只从格数推，不留第二份。");
    }

    // ---- 二阶切换 ----

    [Fact]
    public void 进阶完成那一刻_甲板扩到二十八格五十六米_永久生效()
    {
        var world = AdvancedToWorld(ArenaWorldBuilder.New(Seed).Build());

        world.DeckCells.ShouldBe(ArenaTuning.DeckCellsTier2, "扩容在仪式到点那一刻生效。");
        world.DeckSpanMeters.ShouldBe(56.0, "28 格 × 2 米 = 56 米。");
        world.DeckCellBounds.ShouldBe((-14, -14, 13, 13), "建造边界随格数外扩。");
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(RingCell))
            .ShouldBeFalse("外圈那一环的阻挡解开了。");

        // 永久：仪式之后再推多久都不会缩回去。
        world.Advance(ArenaTime.StepsPerGameHour * 4);
        world.DeckCells.ShouldBe(ArenaTuning.DeckCellsTier2, "扩容永久。");
    }

    [Fact]
    public void 扩容不动既有布局_格号与世界落点一寸不变()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var hallBefore = world.Construction.Find(ArenaWorld.TownHallId)!;
        var anchorBefore = hallBefore.Anchor!.Value;
        var posBefore = hallBefore.Position;

        AdvancedToWorld(world);

        var hallAfter = world.Construction.Find(ArenaWorld.TownHallId)!;
        hallAfter.Anchor!.Value.ShouldBe(anchorBefore, "锚点格号不搬迁。");
        hallAfter.Position.ShouldBe(posBefore, "世界落点只由格号 × 格长推出，与边界无关。");
    }

    // ---- 切换边界：同一格进阶前后可否放置 ----

    [Fact]
    public void 同一格_进阶前按界外拒_进阶后放得成()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 一阶：锚点 (12,12) 的 2×2 粮仓压的是界外格 → OutOfBounds。
        var refused = world.Deck.PlaceBuilding("Granary", RingCell.Col, RingCell.Row);
        refused.Ok.ShouldBeFalse("一阶放不进扩容格。");
        refused.Reason.ShouldBe(PlacementReason.OutOfBounds, "拒绝原因是边界，不是占用。");

        AdvancedToWorld(world);

        var placed = world.Deck.PlaceBuilding("Granary", RingCell.Col, RingCell.Row);
        placed.Ok.ShouldBeTrue(placed.Reason.ToString());

        // 同一格再问一次边界判口：活动边界跟着位阶走。
        world.IsDeckCellInBounds(RingCell).ShouldBeTrue();
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(RingCell)).ShouldBeFalse();
    }

    // ---- 寻路在扩容边界上可走 ----

    [Fact]
    public void 寻路_扩容前边界格是墙_扩容后走得进去()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var target = new NavPoint(
            (RingCell.Col + 0.5) * ArenaTuning.DeckCellMeters,
            (RingCell.Row + 0.5) * ArenaTuning.DeckCellMeters);

        // 一阶：那格是墙，「最近可通行格」退回界内。
        world.NearestFreeDeckCell(target.X, target.Z)
            .ShouldNotBe(target, "一阶走不进扩容格。");

        AdvancedToWorld(world);

        // 二阶：同一发判口直接落在那一格上。
        world.NearestFreeDeckCell(target.X, target.Z).ShouldBe(target, "扩容后那一格通了。");

        // 真走一遍：从落点序列的第一格发到扩容角，走到才算「可走」。用敌人档：它不领日常走位的令。
        var walker = world.Roster.Add("deck_walker", UnitRole.Enemy);
        var want = DeckGeometry.Nav(target.X, target.Z);
        world.DeckStepper.Issue(walker.Id,
            NavCommand.GoTo(want, ArenaTuning.DailyArriveToleranceMeters));
        world.Advance(6_000);

        var at = world.DeckStepper.PositionOf(walker.Id);
        (System.Math.Abs(at.X - want.X) <= 1.0 && System.Math.Abs(at.Z - want.Z) <= 1.0)
            .ShouldBeTrue($"扩容边界上要真走得进去：停在 {at.X}, {at.Z}");
    }

    // ---- 快照往返两档尺寸 ----

    [Fact]
    public void 快照往返_一阶快照还原后仍是二十四格()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var snap = FinalSnapshot.Take(world);

        world.Advance(ArenaTime.StepsPerGameHour);
        snap.Restore(world);

        world.DeckCells.ShouldBe(ArenaTuning.DeckCellsTier1, "一阶的快照还原后尺寸不变。");
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(RingCell))
            .ShouldBeTrue("还原不顺手解阻挡。");
    }

    [Fact]
    public void 快照往返_二阶快照还原后仍是二十八格_阻挡与边界同档()
    {
        var world = AdvancedToWorld(ArenaWorldBuilder.New(Seed).Build());
        var snap = FinalSnapshot.Take(world);

        // 战后随便折腾：再推时间、让敌人踩进扩容格，再还原。
        world.Advance(ArenaTime.StepsPerGameHour);

        snap.Restore(world);

        world.DeckCells.ShouldBe(ArenaTuning.DeckCellsTier2, "最终重试还原后尺寸正确。");
        world.DeckSpanMeters.ShouldBe(56.0);
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(RingCell))
            .ShouldBeFalse("还原不缩边界、不重新砌墙：通过性留在战前那一档。");
        world.IsDeckCellInBounds(RingCell).ShouldBeTrue("建造边界留在战前那一档。");
        SegmentOf(world.DebugSnapshot(), "BEAST").ShouldContain("tier=2", Case.Sensitive,
            "位阶读数没被还原抹掉。");
    }

    private static string SegmentOf(string snapshot, string head)
        => snapshot.Split(" | ").First(s => s.Trim().StartsWith(head, System.StringComparison.Ordinal));

    // ---- 位阶档位表 ----

    [Fact]
    public void 档位表_一阶二十四二阶二十八_三阶没有甲板档位当场抛()
    {
        ArenaTuning.DeckCellsFor(BeastTier.Tier1).ShouldBe(24);
        ArenaTuning.DeckCellsFor(BeastTier.Tier2).ShouldBe(28);
        Should.Throw<System.NotSupportedException>(
            () => ArenaTuning.DeckCellsFor(BeastTier.Tier3), "三阶不在切片内，档位没定过。");
    }
}
