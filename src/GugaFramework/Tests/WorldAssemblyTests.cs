#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using DemoFactory.GugaFramework.Base;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 世界装配的契约测试 —— 五步的正向推进、七条守卫的次序、退订那一发的读数。
///
/// <para><b>这批测试要变便宜的那一步是「装配错在哪一步」</b>：那三段构造与那三件每步次序
/// 收编前在三处宿主里各抄一份，抄错一处没有任何断言会红 —— 要等世界演化结果不对才反查得到。
/// 这里把每条守卫（缺层、接晚、接第二次、接在没有出口的地方）与那件每步次序各钉成一条离线命题。</para>
///
/// <para><b>场地是最小的一块</b>：8×8 格、格长 1.0 米、阻挡表空表、一张半径 0.5 米的圆形占地蓝图。
/// 网格够小是为了「推进三步」那种用例跑起来是毫秒级；半径非零是为了地形接线真的有印子可落
/// —— 半径 0 会让「地形落地了没有」这一条断言退化成 <c>0 == 0</c>。</para>
/// </summary>
public class WorldAssemblyTests
{
    private const string SpaceName = "assembly-test";

    private const double CellMeters = 1.0;

    private const int Cells = 8;

    private const double ProbeRadius = 0.5;

    // ---- 装配正向与推进（6 条）--------------------------------------------------

    [Fact]
    public void 五步走齐之后推进一步_步号恰好加一()
    {
        var f = new Fixture();

        f.Assembly.Advance(1).ShouldBeTrue("层齐了、时间没冻着，这一步就该推得动");
        f.Clock.Tick.ShouldBe(1, "推一步就是加一步：不多推也不少推");
    }

    [Fact]
    public void 推进三步与三次推一步逐字相等()
    {
        // WorldAssembly 上没有快照口可调，所以比三个可读读数：步号、在册建筑数、落了几枚地形印子。
        var one = new Fixture();
        var three = new Fixture();
        one.Place(new Vector2(3, 3));
        three.Place(new Vector2(3, 3));

        for (var i = 0; i < 3; i++)
        {
            one.Assembly.Advance(1).ShouldBeTrue();
        }

        three.Assembly.Advance(3).ShouldBeTrue();

        three.Clock.Tick.ShouldBe(one.Clock.Tick);
        three.Construction.Count.ShouldBe(one.Construction.Count);
        three.Assembly.Terrain!.StampedCount.ShouldBe(one.Assembly.Terrain!.StampedCount);
    }

    [Fact]
    public void 步数为零回假且什么都不做()
    {
        var f = new Fixture();
        f.Place(new Vector2(3, 3));

        f.Assembly.Advance(0).ShouldBeFalse("推零步不是推了一步");
        f.Clock.Tick.ShouldBe(0);
        f.Assembly.Terrain!.StampedCount.ShouldBe(0, "回假就一步都不走：连地形都不读");
    }

    [Fact]
    public void 暂停中推进回假_步号不动地形也不落地()
    {
        var f = new Fixture();
        f.Place(new Vector2(3, 3));
        f.Clock.Pause();

        f.Assembly.Advance(1).ShouldBeFalse("时钟冻着，这一发推不动");
        f.Clock.Tick.ShouldBe(0, "时间冻结对两层是同一件事");
        f.Assembly.Terrain!.StampedCount.ShouldBe(0, "回假就一步都不走：连地形都不读");
    }

    [Fact]
    public void 每步收尾回调的次数恰好等于步数()
    {
        var f = new Fixture();
        var calls = 0;

        f.Assembly.Advance(3, () => calls++).ShouldBeTrue();

        calls.ShouldBe(3, "收尾是每步一次，不是每发一次");
    }

    [Fact]
    public void 推到目标步号停在那儿_暂停中推它步号不动()
    {
        var f = new Fixture();

        f.Assembly.AdvanceTo(5, chunkTicks: 2);
        f.Clock.Tick.ShouldBe(5, "分块推进停在目标步号上，不越");

        f.Clock.Pause();
        var frozen = f.Clock.Tick;
        f.Assembly.AdvanceTo(50);
        f.Clock.Tick.ShouldBe(frozen, "暂停中推不动：靠前后两次读数断，不靠超时判死循环");
    }

    // ---- 守卫（8 条）------------------------------------------------------------

    [Fact]
    public void 地形接线接在建造模块之前当场抛()
    {
        var clock = new GameClock();
        var assembly = new WorldAssembly(clock);
        assembly.BuildNavPipeline(NewSpec(NewGrid()));

        var ex = Should.Throw<InvalidOperationException>(
            () => assembly.AttachTerrain(MapCell));
        ex.Message.ShouldContain("地形接线接在建造模块的事件出口上");
    }

    [Fact]
    public void 走位流水线建第二次当场抛()
    {
        var assembly = new WorldAssembly(new GameClock());
        assembly.BuildNavPipeline(NewSpec(NewGrid()));

        var ex = Should.Throw<InvalidOperationException>(() => assembly.BuildNavPipeline(NewSpec(NewGrid())));
        ex.Message.ShouldContain("一个世界只有一条走位流水线");
    }

    [Fact]
    public void 建造模块建第二次当场抛()
    {
        var assembly = new WorldAssembly(new GameClock());
        assembly.BuildConstruction(new FreePlacementRule(), Blueprints());

        var ex = Should.Throw<InvalidOperationException>(
            () => assembly.BuildConstruction(new FreePlacementRule(), Blueprints()));
        ex.Message.ShouldContain("两个模块就是两本册");
    }

    [Fact]
    public void 地形接线接第二次当场抛()
    {
        var f = new Fixture();

        var ex = Should.Throw<InvalidOperationException>(() => f.Assembly.AttachTerrain(MapCell));
        ex.Message.ShouldContain("已经接过一次");
    }

    [Fact]
    public void 推进之后再接地形当场抛_消息带晚与当前步号()
    {
        var f = new Fixture();
        f.Assembly.Advance(2).ShouldBeTrue();

        var ex = Should.Throw<InvalidOperationException>(() => f.Assembly.AttachTerrain(MapCell));
        ex.Message.ShouldContain("晚");
        ex.Message.ShouldContain(
            "第 2 步", Case.Sensitive,
            "消息要带当前步号：只说「晚了」的话，agent 还得回去数自己推了几步");
    }

    [Fact]
    public void 分块步数为零当场抛()
    {
        var f = new Fixture();

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => f.Assembly.AdvanceTo(10, chunkTicks: 0));
        ex.Message.ShouldContain("分块步数必须 >= 1");
    }

    [Fact]
    public void 差地形接线那一层就推进_消息说出缺哪一层()
    {
        var clock = new GameClock();
        var assembly = new WorldAssembly(clock);
        assembly.BuildNavPipeline(NewSpec(NewGrid()));
        assembly.BuildConstruction(new FreePlacementRule(), Blueprints());

        var ex = Should.Throw<InvalidOperationException>(() => assembly.Advance(1));
        ex.Message.ShouldContain("缺：地形接线");

        // 守卫排在暂停判定之前：暂停中调 Advance 也得当场报出缺层，
        // 否则这条真 bug 会被一句「暂停中，推不动」盖住。
        clock.Pause();
        var paused = Should.Throw<InvalidOperationException>(() => assembly.Advance(1));
        paused.Message.ShouldContain("缺：地形接线");
    }

    [Fact]
    public void 一层都没建就推进_消息同时说三层()
    {
        var clock = new GameClock();
        var assembly = new WorldAssembly(clock);

        var ex = Should.Throw<InvalidOperationException>(() => assembly.Advance(1));
        ex.Message.ShouldContain("缺：走位流水线、建造模块、地形接线");

        clock.Pause();
        var paused = Should.Throw<InvalidOperationException>(() => assembly.Advance(1));
        paused.Message.ShouldContain("缺：走位流水线、建造模块、地形接线");
    }

    // ---- 退订（3 条）-----------------------------------------------------------

    [Fact]
    public void 交还两处订阅_第二次再调回零()
    {
        var f = new Fixture();

        f.Assembly.Detach().ShouldBe(2, "交还的是两处订阅：一条时钟脉搏与建造事件出口那一处");
        f.Assembly.Detach().ShouldBe(0, "已经交还过的不再交还一遍");
    }

    [Fact]
    public void 交还之后地形不再落地()
    {
        var f = new Fixture();
        f.Place(new Vector2(3, 3));
        f.Assembly.Advance(1).ShouldBeTrue();

        var stamped = f.Assembly.Terrain!.StampedCount;
        stamped.ShouldBeGreaterThan(0L, "前置断言：半径 0.5 米的圆在 1.0 米的格上要落印子 —— 这一条不先断，后面就成 0 == 0");

        f.Assembly.Detach();
        f.Place(new Vector2(5, 5));
        f.Assembly.Advance(1).ShouldBeTrue();

        f.Assembly.Terrain.StampedCount.ShouldBe(stamped, "交还之后不再读那个事件出口：差的那几枚就是没落成地形的");
    }

    [Fact]
    public void 基类订阅表_释放后归零且脉冲不再触发()
    {
        var world = new ProbeWorld();
        var pulses = 0;
        var detaches = 0;
        world.Subscribe(_ => pulses++);
        world.Track(() => detaches++);

        world.SubscriptionCount.ShouldBe(2);
        world.IsDisposed.ShouldBeFalse();
        world.Clock.Advance(1);
        pulses.ShouldBe(1);

        world.Dispose();

        world.IsDisposed.ShouldBeTrue();
        world.SubscriptionCount.ShouldBe(0, "释放之后订阅表里一条都不剩");
        detaches.ShouldBe(1, "登记的那一条退订各触发一次");
        world.Clock.Advance(50);
        pulses.ShouldBe(1, "退订之后脉搏不再触发");

        Should.NotThrow(world.Dispose, "释放是幂等的：重复调直接返回，不重复交还");
    }

    // ---- 档与场地 ---------------------------------------------------------------

    private static NavGrid NewGrid() => new(new NavGridSpec
    {
        Space = new SpaceId(SpaceName),
        CellMeters = CellMeters,
        CellsX = Cells,
        CellsZ = Cells,
        OriginX = 0.0,
        OriginZ = 0.0,
        BlockedCells = [],
    });

    private static NavPipelineSpec NewSpec(NavGrid grid) => new()
    {
        Space = new SpaceId(SpaceName),
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

    private static Blueprint[] Blueprints() => [new Blueprint("probe", 100.0, new RadiusFootprint(ProbeRadius))];

    private static NavCell MapCell(GridCell cell) => new(cell.Col, cell.Row);

    /// <summary>
    /// 圆 → 格的光栅化：中心落在哪一格的那一圈里，离圆心最近的点不超过半径就收进来。
    /// 这一份归宿主（地形接线那层不认识圆形占地）。
    /// </summary>
    private static IReadOnlyList<NavCell> CellsAt(Vector2 at, double radius)
    {
        var cx = (int)Math.Floor(at.X);
        var cz = (int)Math.Floor(at.Y);
        var cells = new List<NavCell>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var x = cx + dx;
                var z = cz + dz;
                var nearX = Math.Clamp((double)at.X, x, x + CellMeters);
                var nearZ = Math.Clamp((double)at.Y, z, z + CellMeters);
                var gapX = (double)at.X - nearX;
                var gapZ = (double)at.Y - nearZ;
                if (Math.Sqrt((gapX * gapX) + (gapZ * gapZ)) <= radius)
                {
                    cells.Add(new NavCell(x, z));
                }
            }
        }

        return cells;
    }

    /// <summary>五步走齐的一套装配，外加宿主递的那个「这栋占哪几格」的查法。</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            Clock = new GameClock();
            Assembly = new WorldAssembly(Clock);
            var grid = NewGrid();
            Assembly.BuildNavPipeline(NewSpec(grid));
            Assembly.BuildConstruction(new FreePlacementRule(), Blueprints());
            Assembly.AttachTerrain(
                MapCell,
                id => NavCells.TryGetValue(id, out var found) ? found : Array.Empty<NavCell>());
        }

        public GameClock Clock { get; }

        public WorldAssembly Assembly { get; }

        public ConstructionCore Construction => Assembly.Construction!;

        public Dictionary<string, IReadOnlyList<NavCell>> NavCells { get; } = new(StringComparer.Ordinal);

        /// <summary>落一座建筑，并把它的寻路格名单登记进宿主那张表。</summary>
        public void Place(Vector2 at)
        {
            var verdict = Construction.Commit("probe", at, 0);
            verdict.Ok.ShouldBeTrue($"probe 半径 {ProbeRadius.ToString(CultureInfo.InvariantCulture)} 米，在这一格上放得下");
            NavCells[Construction.Ids[^1]] = CellsAt(at, ProbeRadius);
        }
    }

    /// <summary>
    /// 基类的替身：那两个订阅口是 protected，测试类够不着，所以在这里公开两个转发口子。
    /// </summary>
    private sealed class ProbeWorld : GameLogicBase
    {
        public void Subscribe(Action<long> handler) => SubscribePulse(handler);

        public void Track(Action detacher) => TrackDetach(detacher);
    }
}
