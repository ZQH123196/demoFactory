#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 建造模块两条放置规则的契约测试 —— <b>全部离线跑通，零引擎依赖</b>（spec 3 节第 2 条）。
///
/// 这里的每一条都对着一种「看起来对、其实会分叉」的地方：吸附那一侧是 floor 还是截断、
/// 边界判在几格之前、拒绝之后册与占用有没有动、间距是中心距还是边缘距、量化落在哪一档。
/// 这些以前只能在引擎里点一遍看出来，现在是一发 <c>dotnet test</c>。
/// </summary>
public class ConstructionPlacementTests
{
    private const double CellSize = 1.0;

    // ---- 网格 -------------------------------------------------------------------

    [Fact]
    public void 网格吸附_正负坐标都按floor落格_不是截断()
    {
        // 截断（向零取整）会让 0.9 落进 0 格、-0.1 落进 0 格 —— 负坐标那侧整片世界都会错位。
        GridPlacementRule.CellAt(new Vector2(0.9f, 0.9f), CellSize).ShouldBe(new GridCell(0, 0));
        GridPlacementRule.CellAt(new Vector2(1.0f, 1.0f), CellSize).ShouldBe(new GridCell(1, 1));
        GridPlacementRule.CellAt(new Vector2(-0.1f, -0.1f), CellSize).ShouldBe(new GridCell(-1, -1));
        GridPlacementRule.CellAt(new Vector2(-1.0f, -1.0f), CellSize).ShouldBe(new GridCell(-1, -1));
        GridPlacementRule.CellAt(new Vector2(-0.001f, 2.999f), CellSize).ShouldBe(new GridCell(-1, 2));
    }

    [Fact]
    public void 网格落点由锚点格心推导_不存第二份真值()
    {
        var core = GridCore();

        var v = core.Commit("keep", new Vector2(3.2f, 4.9f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        v.Ok.ShouldBeTrue();
        v.Anchor.ShouldBe(new GridCell(3, 4));
        v.SnappedWorld.ShouldBe(new Vector2(3.5f, 4.5f));
        core.Find("b1")!.Position.ShouldBe(new Vector2(3.5f, 4.5f), "坐标是锚点格心推出来的，推一遍就该是同一个数");    }

    [Fact]
    public void 网格负坐标那侧照落格_落点也是负的()
    {
        var core = GridCore();

        var v = core.Commit("keep", new Vector2(-1.7f, -0.3f));

        v.Anchor.ShouldBe(new GridCell(-2, -1));
        v.SnappedWorld.ShouldBe(new Vector2(-1.5f, -0.5f));
    }

    [Fact]
    public void 占地碰撞_第二座压到第一座回Occupied()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(0.1f, 0.1f));

        // L 形占地 (0,0)(1,0)(0,1)：锚点落 (1,0) 就压住了第一座的 (1,0)。
        var v = core.Commit("keep", new Vector2(1.4f, 0.2f));

        v.Ok.ShouldBeFalse();
        v.Reason.ShouldBe(PlacementReason.Occupied);
    }

    [Fact]
    public void D4八值对不对称占地逐个可区分()
    {
        // L 形四格（3 高 2 宽），没有任何对称 —— 八个值里任何两个撞在一起就是变换写错了。
        var footprint = CellFootprint.FromCells(new[]
        {
            new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1), new GridCell(0, 2),
        });

        var shapes = new List<string>();
        for (var i = 0; i < GridOrientation.Count; i++)
        {
            shapes.Add(Cells(GridOrientation.Transform(footprint, i).Offsets));
        }

        shapes.Distinct().Count().ShouldBe(GridOrientation.Count, "八个方向落在同样的格上：变换不是 D4 —— " + string.Join(" | ", shapes));
    }

    [Fact]
    public void D4_2x2原地旋转是同一组格()
    {
        // D21 的推论（不是特判）：2×2 绕盒心转 90° 与 180° 都还是那四格，所以恒成功。
        var footprint = CellFootprint.FromCells(new[]
        {
            new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1), new GridCell(1, 1),
        });
        var rest = Cells(footprint.Offsets);

        for (var i = 0; i < GridOrientation.Count; i++)
        {
            Cells(GridOrientation.Transform(footprint, i).Offsets).ShouldBe(rest, $"{GridOrientation.Label(i)} 把 2×2 转出界了 —— 变换是绕锚点做的，不是绕盒心");
        }
    }

    [Fact]
    public void D4方向落在占地上_裁决给的那组格跟着转()
    {
        var core = GridCore();

        var v = core.Commit("keep", new Vector2(0.1f, 0.1f), GridOrientation.M180);

        v.Cells.ShouldBe(new[]
        {
            new GridCell(0, 0), new GridCell(0, 1), new GridCell(1, 1),
        });
    }

    [Fact]
    public void 方向越界当场抛_不折回0()
    {
        var core = GridCore();

        Should.Throw<ArgumentOutOfRangeException>(() => core.Commit("keep", new Vector2(0.1f, 0.1f), 8));
    }

    [Fact]
    public void 出界回OutOfBounds_一寸不动()
    {
        var core = GridCore();

        var v = core.Commit("keep", new Vector2(20.4f, 0.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        v.Reason.ShouldBe(PlacementReason.OutOfBounds);
        v.Cells.Count.ShouldBeGreaterThan(0, "拒绝也要给形状：幽灵要画红的那个形状");
        core.Count.ShouldBe(0);
        core.OccupiedCount.ShouldBe(0);    }

    [Fact]
    public void 边界在负坐标那侧也判()
    {
        var core = GridCore(min: -2, max: 9);

        // floor 之后：-1.5 落 -2 格（在界内），-2.5 落 -3 格（出界）。
        core.Commit("keep", new Vector2(-1.5f, 0.2f)).Reason.ShouldBe(PlacementReason.None);
        core.Commit("keep", new Vector2(-2.5f, 0.2f)).Reason.ShouldBe(PlacementReason.OutOfBounds);
    }

    [Fact]
    public void 宿主谓词回NotBuildable()
    {
        var host = new TestHost(site => site.Cell != new GridCell(0, 1));
        var core = GridCore(host);

        var v = core.Commit("keep", new Vector2(0.1f, 0.1f));

        v.Reason.ShouldBe(PlacementReason.NotBuildable);
    }

    [Fact]
    public void 不递宿主等于全可建()
    {
        var core = GridCore();

        core.Commit("keep", new Vector2(0.1f, 0.1f)).Ok.ShouldBeTrue();
    }

    [Fact]
    public void 网格gizmo参数只读查询_格长与边界是同一组数()
    {
        var core = GridCore(min: -2, max: 9);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.GizmoParams["cellSize"].ShouldBe(CellSize);
        core.GizmoParams["maxCol"].ShouldBe(9.0);
        core.GizmoParams["minRow"].ShouldBe(-2.0);    }

    // ---- 边界外扩（扩容口） --------------------------------------------------------

    [Fact]
    public void 边界外扩_外扩可放新格_内缩当场抛_gizmo跟着走()
    {
        var rule = new GridPlacementRule(CellSize, -2, -2, 1, 1);

        // 一格蓝图贴着旧上界外那一格：界外时被边界拒。
        var bp = new Blueprint("h", 100.0, CellFootprint.FromCells([new GridCell(0, 0)]), 1L);
        var outside = new Vector2(2.5f, 2.5f);
        rule.Check(new PlacementRequest(bp, outside, 0)).Ok.ShouldBeFalse();

        rule.ExpandBounds(-3, -3, 3, 3);
        rule.Check(new PlacementRequest(bp, outside, 0)).Ok.ShouldBeTrue("新格进了界。");
        rule.BoundsText.ShouldBe("col=-3..3,row=-3..3");
        rule.GizmoParams["maxCol"].ShouldBe(3.0, "画出来的网格跟着逻辑判定走（D29）。");

        // 已在册的占用一寸不动：边界外扩不改占用表。
        var inside = new Vector2(-1.5f, -1.5f);
        var placed = rule.Check(new PlacementRequest(bp, inside, 0));
        placed.Ok.ShouldBeTrue();
        rule.Occupy("b1", placed);
        rule.ExpandBounds(-4, -4, 4, 4);
        rule.BuildingIdAt(inside).ShouldBe("b1", "外扩之后旧占用照在。");

        // 内缩当场抛：缩边会让在册建筑落在界外。
        Should.Throw<ArgumentException>(() => rule.ExpandBounds(-4, -4, 3, 3));

        // 同档再叫一次是空动作（还原路径与事件路径可能都落到这里）。
        rule.ExpandBounds(-4, -4, 4, 4);
        rule.BoundsText.ShouldBe("col=-4..4,row=-4..4");
    }

    // ---- 自由 -------------------------------------------------------------------

    [Fact]
    public void 自由_边缘到边缘间距按半径和判()
    {
        var core = FreeCore();

        core.Commit("big", new Vector2(0f, 0f));
        var near = core.Commit("small", new Vector2(4.9f, 0f));
        var edge = core.Commit("small", new Vector2(5.0f, 0f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        near.Reason.ShouldBe(PlacementReason.TooClose, "4.9 < 半径和 5：边缘还搭着");
        edge.Ok.ShouldBeTrue("5.0 = 半径和：够（现状中心距判定也是 < 才拦）");    }

    [Fact]
    public void 自由_半径都取MinPlaceDistance一半时与现状中心距判定逐字等价()
    {
        // 迁移前提（D12）：现状判的是中心距 < 12，全部半径 6 时「半径和」就是同一个数。
        const double minPlaceDistance = 12;
        var core = new ConstructionCore(
            new FreePlacementRule(),
            new[]
            {
                new Blueprint("market", 600, new RadiusFootprint(minPlaceDistance / 2)),
                new Blueprint("tavern", 400, new RadiusFootprint(minPlaceDistance / 2)),
            });

        core.Commit("market", new Vector2(0f, 0f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Commit("tavern", new Vector2(11.9f, 0f)).Reason.ShouldBe(PlacementReason.TooClose);
        core.Commit("tavern", new Vector2(12f, 0f)).Reason.ShouldBe(PlacementReason.None);    }

    [Fact]
    public void 自由_位置量化往返_推40_004落40()
    {
        var core = FreeCore();

        var v = core.Commit("small", new Vector2(40.004f, -12.377f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        v.SnappedWorld.ShouldBe(new Vector2(40f, -12.38f));
        ConstructionNumbers.Format(v.SnappedWorld.X).ShouldBe("40");    }

    [Fact]
    public void 自由_量化用的是中点取远离零_同一格里两侧折到同一档()
    {
        // 用默认的四舍六入五成双，0.005 会折到 0.00，而 0.004 折到 0.00 —— 两边不一致，
        // 幽灵会在半步上跳。
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        FreePlacementRule.Quantize(0.005, 0.01).ShouldBe(0.01);
        FreePlacementRule.Quantize(0.004, 0.01).ShouldBe(0.0);
        FreePlacementRule.Quantize(-0.005, 0.01).ShouldBe(-0.01);    }

    [Fact]
    public void 自由_角度量化到15度_整数度进裁决()
    {
        var core = FreeCore();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Commit("small", new Vector2(0f, 0f), 22).Orientation.ShouldBe(15);
        core.Commit("small", new Vector2(0f, 0f), 23).Orientation.ShouldBe(30);
        core.Commit("small", new Vector2(0f, 0f), 7).Orientation.ShouldBe(0);
        core.Commit("small", new Vector2(0f, 0f), 8).Orientation.ShouldBe(15);    }

    [Fact]
    public void 自由_宿主问的是量化之后那个点()
    {
        var asked = new List<Vector2>();
        var host = new TestHost(site =>
        {
            asked.Add(site.Point);
            return true;
        });
        var core = FreeCore(host);

        core.Commit("small", new Vector2(40.004f, 10.006f));

        asked.ShouldBe(new[] { new Vector2(40f, 10.01f) }, false, "问的点与落的点必须是同一个数，否则画出来的地方与判的地方能差半档");
    }

    [Fact]
    public void 自由_宿主说不可建回NotBuildable()
    {
        var core = FreeCore(new TestHost(_ => false));

        core.Commit("small", new Vector2(0f, 0f)).Reason.ShouldBe(PlacementReason.NotBuildable);
    }

    [Fact]
    public void 自由gizmo参数只读查询_两个步长都在()
    {
        var core = FreeCore();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.GizmoParams["positionStep"].ShouldBe(FreePlacementRule.DefaultPositionStep);
        core.GizmoParams["angleStepDegrees"].ShouldBe((double)FreePlacementRule.DefaultAngleStepDegrees);    }

    [Fact]
    public void 自由_半径零的建筑在自己那一点上被拾取()
    {
        var core = new ConstructionCore(
            new FreePlacementRule(),
            new[] { new Blueprint("post", 10, new RadiusFootprint(0)) });
        core.Commit("post", new Vector2(3f, 4f));

        core.BuildingIdAt(3, 4).ShouldBe("b1");
        core.BuildingIdAt(3.5, 4).ShouldBe(string.Empty);
    }

    // ---- 装配与拒绝路径 ---------------------------------------------------------

    [Fact]
    public void 占地形状与规则不匹配当场抛_不静默折成零格建筑()
    {
        var circle = new Blueprint("round", 100, new RadiusFootprint(3));
        var cells = new Blueprint("blocky", 100, CellFootprint.FromCells(new[] { new GridCell(0, 0) }));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<InvalidOperationException>(() =>
            new ConstructionCore(new GridPlacementRule(1, -2, -2, 9, 9), new[] { circle }));
        Should.Throw<InvalidOperationException>(() =>
            new ConstructionCore(new FreePlacementRule(), new[] { cells }));    }

    [Fact]
    public void 空占地表当场抛_零格建筑放下去没有任何一格被占()
    {
        Should.Throw<ArgumentException>(() => CellFootprint.FromCells(Array.Empty<GridCell>()));
    }

    [Fact]
    public void 蓝图重名当场抛_蓝图表是唯一的蓝图真值()
    {
        var one = new Blueprint("keep", 100, new RadiusFootprint(1));
        var two = new Blueprint("keep", 200, new RadiusFootprint(2));

        Should.Throw<InvalidOperationException>(() =>
            new ConstructionCore(new FreePlacementRule(), new[] { one, two }));
    }

    [Fact]
    public void 未知蓝图回UnknownBlueprint_不抛()
    {
        var core = FreeCore();

        var v = core.Commit("ghost", new Vector2(0f, 0f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        v.Reason.ShouldBe(PlacementReason.UnknownBlueprint);
        core.Count.ShouldBe(0);
        core.Events.Count<PlacementRejected>().ShouldBe(1, "拒绝也必须发事件：表现层的小动作词表才有钩子可订阅");    }

    [Fact]
    public void 拒绝时册与占用一寸不动_并发一条带原因的Rejected()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(0.1f, 0.1f));
        var before = core.DebugSnapshotSegment();

        var v = core.Commit("keep", new Vector2(1.4f, 0.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        v.Ok.ShouldBeFalse();
        core.DebugSnapshotSegment().ShouldBe(before, "拒绝一寸不动：快照逐字没变");
        core.OccupiedCount.ShouldBe(3);
        core.Events.Count<PlacementRejected>().ShouldBe(1);
        core.Events.Where<PlacementRejected>().Single().Reason.ShouldBe(PlacementReason.Occupied);    }

    [Fact]
    public void 移除腾占用并发Removed带去向_同格再放得进()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(0.1f, 0.1f));
        core.Commit("keep", new Vector2(4.1f, 4.1f));

        var r = core.Remove("b1", RemovalCause.Dismantle);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeTrue();
        core.Count.ShouldBe(1);
        core.OccupiedCount.ShouldBe(3, "腾掉的是那三格，另一座的三格还在册上");
        core.Events.Where<BuildingRemoved>().Single().Cause.ShouldBe(RemovalCause.Dismantle);
        core.Commit("keep", new Vector2(0.1f, 0.1f)).Ok.ShouldBeTrue("腾干净了才放得进");
        core.OccupiedCount.ShouldBe(6);    }

    [Fact]
    public void 移除未知id回读数不抛()
    {
        var core = GridCore();

        var r = core.Remove("nope");

        r.Ok.ShouldBeFalse();
        r.Reason.ShouldBe(PlacementReason.UnknownId);
    }

    [Fact]
    public void 提交自动编号_显式给id也发事件()
    {
        var core = FreeCore();

        core.Commit("small", new Vector2(0f, 0f));
        core.Commit("small", new Vector2(100f, 0f), 0, "tavern");

        core.Ids.ShouldBe(new[] { "b1", "tavern" });
        core.Events.Count<BuildingPlaced>().ShouldBe(2);
    }

    [Fact]
    public void 显式重名当场抛()
    {
        var core = FreeCore();
        core.Commit("small", new Vector2(0f, 0f));

        Should.Throw<InvalidOperationException>(() =>
            core.Commit("small", new Vector2(100f, 0f), 0, "b1"));
    }

    [Fact]
    public void 事件带步号_步号取的是构造时递进来的时钟读数()
    {
        var now = 0L;
        var core = new ConstructionCore(
            new FreePlacementRule(),
            new[] { new Blueprint("small", 100, new RadiusFootprint(1)) },
            () => now);

        core.Commit("small", new Vector2(0f, 0f));
        now = 7;
        core.Commit("small", new Vector2(50f, 0f));

        core.Events.Where<BuildingPlaced>().Select(e => e.Tick).ShouldBe(new long[] { 0, 7 });
    }

    [Fact]
    public void 事件流满了丢最旧的_读数带dropped()
    {
        // 工期给 5：这一条问的是环形丢最旧，每座只发一条 placed，事件条数与放的座数对得上。
        // 工期 0 会让每座连发 placed + completed（ticket 02 的 Placed → Completed 事件对）。
        var core = new ConstructionCore(
            new FreePlacementRule(),
            new[] { new Blueprint("small", 100, new RadiusFootprint(1), buildDurationTicks: 5) },
            eventCapacity: 2);
        for (var i = 0; i < 4; i++)
        {
            core.Commit("small", new Vector2(i * 10f, 0f));
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Events.Dropped.ShouldBe(2);
        core.Events.KeptCount.ShouldBe(2);
        core.Events.TailText(1).ShouldContain("dropped=2", Case.Sensitive, "「日志里没有」≠「没发生」：读数必须带着丢了多少");    }

    // ---- 快照段与确定性 ---------------------------------------------------------

    [Fact]
    public void 快照段_网格那侧印锚点自由那侧印坐标()
    {
        var grid = GridCore();
        grid.Commit("keep", new Vector2(3.2f, 4.9f));
        var free = FreeCore();
        free.Commit("small", new Vector2(40f, 30f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        // 列序照 D32：id、蓝图、锚点或坐标、HP、状态、剩余步、暂停标志、方向。
        grid.DebugSnapshotSegment().ShouldContain("b1 keep anchor=3,4 hp=100/100 state=completed left=0 paused=0 dir=0");
        grid.DebugSnapshotSegment().ShouldStartWith("construction kind=Cells buildings=1 occupied=3");
        free.DebugSnapshotSegment().ShouldContain("b1 small pos=40,30 hp=100/100 state=completed left=0 paused=0 dir=0");
        free.DebugSnapshotSegment().ShouldContain("bounds=unbounded");    }

    [Fact]
    public void 快照段_调试段与重放段是同一份格式化()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(1.2f, 1.2f));

        core.ReplaySnapshotSegment().ShouldBe(core.DebugSnapshotSegment());
    }

    [Fact]
    public void 蓝图带模型引用_模块存而不读_快照段不印这一列()
    {
        var core = new ConstructionCore(new FreePlacementRule(), new[]
        {
            new Blueprint("keep", 100, new RadiusFootprint(1), modelPath: "res://Models/Keep.glb"),
        });

        core.Commit("keep", new Vector2(1f, 1f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Blueprints["keep"].ModelPath.ShouldBe("res://Models/Keep.glb", "模型路径按 res:// 引用不按 uid（「按 res:// 引用」），表现层从蓝图表取");
        core.DebugSnapshotSegment().ShouldNotContain("res://", Case.Sensitive, "快照段只印模块自己的列：原型那一列与资产引用归原型（D31、D32）");    }

    [Fact]
    public void 同种子两遍逐字全等()
    {
        static string Run()
        {
            var core = new ConstructionCore(
                new GridPlacementRule(1, -2, -2, 9, 9),
                new[]
                {
                    new Blueprint("keep", 100, CellFootprint.FromCells(new[]
                    {
                        new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
                    })),
                },
                () => 3);
            core.Commit("keep", new Vector2(0.2f, 0.3f), GridOrientation.M90);
            core.Commit("keep", new Vector2(1.1f, 1.1f), GridOrientation.M90);
            core.Commit("keep", new Vector2(7.9f, -1.9f), 3);
            core.Remove("b2", RemovalCause.Cancel);
            return core.DebugSnapshotSegment() + core.Events.TailText(99);
        }

        Run().ShouldBe(Run());
    }

    // ---- 工具 -------------------------------------------------------------------

    private static ConstructionCore GridCore(IConstructionHost? host = null, int min = -2, int max = 9)
        => new(new GridPlacementRule(CellSize, min, min, max, max, host), new[]
        {
            new Blueprint("keep", 100, CellFootprint.FromCells(new[]
            {
                new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
            })),
        });

    private static ConstructionCore FreeCore(IConstructionHost? host = null)
        => new(new FreePlacementRule(host: host), new[]
        {
            new Blueprint("big", 100, new RadiusFootprint(3)),
            new Blueprint("small", 100, new RadiusFootprint(2)),
        });

    private static string Cells(IEnumerable<GridCell> cells)
    {
        var list = cells.ToList();
        list.Sort(static (a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));
        return string.Join(" ", list);
    }

    private sealed class TestHost : IConstructionHost
    {
        private readonly Func<BuildSite, bool> _answer;

        public TestHost(Func<BuildSite, bool> answer) => _answer = answer;

        public bool IsBuildable(BuildSite site) => _answer(site);
    }
}
