#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

// 一个世界装多条走位流水线（工单 08）—— 按 SpaceId 登记，第几条都能走位，
// 地形接线默认从建造模块查「这栋现在占哪几格」，于是 Move 一发就把阻挡从旧格迁到新格。
//
// 场地：8×8 格、格长 1.0 米，建造侧是 0..5 的 2×2 格占地 —— 2×2 挪动时新占地与旧占地
// 压着的格子有重叠又不全同，断言「旧格解封、新格落阻挡」才不是 0 == 0。
// 三个不在这一族里的读数（StepperOf / GridOf / Detach 的计数）也在这块场子上读。

/// <summary>
/// 多空间装配与地形移动联动 —— 登记按空间分家、旧属性仍指第一条、
/// 查询口回本尊、挪建筑迁阻挡、退订计数按空间数累加。
/// </summary>
public class WorldAssemblyMultiSpaceTests
{
    private const string DeckSpace = "multi-space-deck";
    private const string SeaSpace = "multi-space-sea";
    private const double CellMeters = 1.0;
    private const int Cells = 8;

    // ---- 登记与旧属性 -----------------------------------------------------------

    [Fact]
    public void 两个空间各建一条_推一步两条步进器各走一步()
    {
        var f = new Fixture();
        var deck = f.DeckStepper;
        var sea = f.BuildSecondSpace();

        deck.StepIndex.ShouldBe(0L, "前置断言：还没推过步");
        sea.StepIndex.ShouldBe(0L, "前置断言：第二条刚建好");

        f.Assembly.Advance(1).ShouldBeTrue("层齐了、时间没冻着，这一步就该推得动");

        deck.StepIndex.ShouldBe(1L, "第一条跟着时钟走：两处订的是同一个脉搏");
        sea.StepIndex.ShouldBe(1L,
            "第二条不是摆设：它在构造那一刻就订上了同一个时钟的脉搏，装配推进一次它就收到一次");
    }

    [Fact]
    public void 旧那两个属性仍指第一条_与查询口分家()
    {
        var f = new Fixture();
        var sea = f.BuildSecondSpace();

        f.Assembly.Stepper.ShouldBeSameAs(f.DeckStepper, "旧属性给第一条走位流水线：地形接线那一侧读它");
        f.Assembly.Grid.ShouldBeSameAs(f.DeckGrid, "旧属性给第一条那张网格");
        f.Assembly.StepperOf(new SpaceId(DeckSpace)).ShouldBeSameAs(f.DeckStepper);
        f.Assembly.GridOf(new SpaceId(SeaSpace)).ShouldBeSameAs(f.SeaGrid, "第二个空间查得到自己那张网格");
        f.Assembly.Stepper.ShouldNotBeSameAs(sea, "旧属性不会跟着最后一次注册走 —— 挪一下就换人，调用点无从察觉");
    }

    [Fact]
    public void 同一空间建第二条当场抛()
    {
        var f = new Fixture();

        var ex = Should.Throw<InvalidOperationException>(
            () => f.Assembly.BuildNavPipeline(NewSpec(NewGrid(DeckSpace))));
        ex.Message.ShouldContain(DeckSpace, Case.Sensitive,
            "消息要带空间名：agent 看到的是「哪个空间撞了」，不是「有地方撞了」");
        ex.Message.ShouldContain("同一个空间", Case.Sensitive,
            "消息要说的是同一个空间撞了 —— 一个世界可以有几条流水线，只是一个空间一条");
    }

    [Fact]
    public void 推过步之后再建流水线当场抛_消息带当前步号()
    {
        var f = new Fixture();
        f.Assembly.Advance(2).ShouldBeTrue();

        var ex = Should.Throw<InvalidOperationException>(
            () => f.Assembly.BuildNavPipeline(NewSpec(NewGrid(SeaSpace))));
        ex.Message.ShouldContain("晚", Case.Sensitive);
        ex.Message.ShouldContain("第 2 步", Case.Sensitive,
            "消息要带当前步号：只说「晚了」的话，agent 还得回去数自己推了几步");
    }

    [Fact]
    public void 查询一个没登记过的空间当场抛_消息列出已登记的那几个()
    {
        var f = new Fixture();
        f.BuildSecondSpace();

        Should.Throw<KeyNotFoundException>(() => f.Assembly.StepperOf(new SpaceId("没建过的空间")));
        var ex = Should.Throw<KeyNotFoundException>(() => f.Assembly.GridOf(new SpaceId("没建过的空间")));
        ex.Message.ShouldContain(DeckSpace, Case.Sensitive, "消息要列已登记的空间：问错名的时候这一句直接给出处");
        ex.Message.ShouldContain(SeaSpace, Case.Sensitive);
    }

    // ---- 地形接线与挪建筑 -------------------------------------------------------

    [Fact]
    public void 挪一栋建筑_旧格解封新格落阻挡()
    {
        var f = new Fixture();
        f.Assembly.Advance(1).ShouldBeTrue();

        var anchor = new GridCell(1, 1);
        var id = f.CommitBlock(GridPlacementRule.CenterOf(anchor, CellMeters));
        f.Assembly.Advance(1).ShouldBeTrue();

        f.BlockedAt(anchor).ShouldBeTrue("落座那一发要落成阻挡：挪建筑要验的是迁移，前置不成立就是 0 == 0");

        var moved = GridPlacementRule.CenterOf(new GridCell(4, 4), CellMeters);
        f.Construction.Move(id, moved).Ok.ShouldBeTrue("挪到空地上：占格检查只查邻居");
        f.Assembly.Advance(1).ShouldBeTrue();

        for (var dx = 0; dx < 2; dx++)
        {
            for (var dz = 0; dz < 2; dz++)
            {
                f.BlockedAt(new GridCell(anchor.Col + dx, anchor.Row + dz)).ShouldBeFalse(
                    "旧格要解封：不解封的话旧位置留一堵撤不掉的墙");
                f.BlockedAt(new GridCell(4 + dx, 4 + dz)).ShouldBeTrue(
                    "新格要落阻挡：BuildingMoved 只带建筑 id，新占地得从建造模块那一本册上查");
            }
        }

        f.Assembly.Terrain!.Missed.ShouldBe(0L,
            "查法递进去了就不该有落不下的：这一列不为 0 的症状与「没砌墙」完全一样");
    }

    [Fact]
    public void 显式递cellsOf_查法说了算不走建造模块()
    {
        var f = new Fixture(cellsOf: _ => [new GridCell(0, 0)]);
        f.Assembly.Advance(1).ShouldBeTrue();

        var id = f.CommitBlock(GridPlacementRule.CenterOf(new GridCell(1, 1), CellMeters));
        f.Assembly.Advance(1).ShouldBeTrue();

        f.Construction.Move(id, GridPlacementRule.CenterOf(new GridCell(4, 4), CellMeters)).Ok.ShouldBeTrue();
        f.Assembly.Advance(1).ShouldBeTrue();

        f.BlockedAt(new GridCell(0, 0)).ShouldBeTrue("显式递进来的查法答的是 (0,0)：它说了算");
        f.BlockedAt(new GridCell(4, 4)).ShouldBeFalse("建造模块上新落位那几格不是答案：显式查法优先于缺省绑定");
    }

    // ---- 退订 -------------------------------------------------------------------

    [Fact]
    public void 交还订阅的读数按空间数累加_第二次再调回零()
    {
        var f = new Fixture();
        f.BuildSecondSpace();

        f.Assembly.Detach().ShouldBe(3,
            "两个空间各一条时钟脉搏 + 地形接线那一处建造事件出口");
        f.Assembly.Detach().ShouldBe(0, "已经交还过的不再交还一遍");
    }

    [Fact]
    public void 交还之后两条步进器都不再走()
    {
        var f = new Fixture();
        var sea = f.BuildSecondSpace();
        f.Assembly.Detach();

        f.Clock.Advance(5);

        f.DeckStepper.StepIndex.ShouldBe(0L, "交还之后第一条不再收脉搏");
        sea.StepIndex.ShouldBe(0L, "交还按空间逐条算：漏掉第二条的话它还在偷偷走");
    }

    // ---- 场地 -------------------------------------------------------------------

    /// <summary>一个装好三条层的装配：第一个空间走位 + 建造模块 + 地形接线。</summary>
    private sealed class Fixture
    {
        public Fixture(Func<string, IReadOnlyList<GridCell>>? cellsOf = null)
        {
            Clock = new GameClock();
            Assembly = new WorldAssembly(Clock);
            DeckGrid = NewGrid(DeckSpace);
            DeckStepper = Assembly.BuildNavPipeline(NewSpec(DeckGrid));
            SeaGrid = NewGrid(SeaSpace);
            Assembly.BuildConstruction(new GridPlacementRule(CellMeters, 0, 0, 5, 5), Blueprints());
            Assembly.AttachTerrain(MapCell, navCellsOf: null, cellsOf: cellsOf);
        }

        public GameClock Clock { get; }

        public WorldAssembly Assembly { get; }

        public NavGrid DeckGrid { get; }

        public NavGrid SeaGrid { get; }

        public NavStepper DeckStepper { get; }

        public ConstructionCore Construction => Assembly.Construction!;

        /// <summary>在第二个空间上再登记一条走位流水线。</summary>
        public NavStepper BuildSecondSpace() => Assembly.BuildNavPipeline(NewSpec(SeaGrid));

        /// <summary>读第一张网格上那一格封没封（建造格 → 寻路格就是同号那一换算）。</summary>
        public bool BlockedAt(GridCell cell) => DeckGrid.IsBlocked(MapCell(cell));

        /// <summary>落一座 2×2 的建筑，回它的 id。</summary>
        public string CommitBlock(Vector2 at)
        {
            var verdict = Construction.Commit("block", at, 0);
            verdict.Ok.ShouldBeTrue("2×2 占地在空地上放得下");
            return Construction.Ids[^1];
        }
    }

    private static NavGrid NewGrid(string space) => new(new NavGridSpec
    {
        Space = new SpaceId(space),
        CellMeters = CellMeters,
        CellsX = Cells,
        CellsZ = Cells,
        OriginX = 0.0,
        OriginZ = 0.0,
        BlockedCells = [],
    });

    private static NavPipelineSpec NewSpec(NavGrid grid) => new()
    {
        Space = grid.Space,
        Grid = grid,
        Budget = new NavSolveBudget { PerRequestExpansions = 512 },
        Recompute = new NavRecomputeTuning { GoalMoveToleranceMeters = 0.25, NotArrivedStepCap = 4 },
        CacheCapacity = 64,
        RecordCapacity = 256,
        TrackedRequestCapacity = 128,
        Move = new NavMoveTuning { StepSeconds = 0.1, LookAheadMeters = 1.0, StepSolveExpansions = 256 },
        Gate = null,
        Separation = null,
        Events = null,
    };

    private static Blueprint[] Blueprints() =>
    [
        new Blueprint("block", 100.0, CellFootprint.FromCells(new[]
        {
            new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1), new GridCell(1, 1),
        })),
    ];

    private static NavCell MapCell(GridCell cell) => new(cell.Col, cell.Row);
}
