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
/// 批量手势（画线 / 画矩形 / 批量拆除）与它那两条纯查询的契约测试 —— <b>零引擎依赖</b>
/// （spec 3 节第 2 条）。
///
/// 这里守的是几条「看起来对、其实会分叉」的地方：枚举顺序（重放端要靠它复现，D24）、
/// 边判边落（先判后落会让两个锚点压着的格双双判成，落座那一步当场抛）、
/// 部分成功与全有或全无的边界（前者逐格报原因、后者一寸不动）、汇总事件同形状（D26：
/// 线与矩形之间不许有可选位）、以及「预览 = 结果」这条一致性契约（D25、D29）——
/// 最后一向以前只能靠出图对比红格，现在是一发 <c>dotnet test</c>（「测试与实测分工」）。
///
/// 断言强度只比检查点快照与逐格裁决表（状态级），事件流读条数与字段、不比逐字（「断言比快照」）。
/// </summary>
public class ConstructionBatchTests
{
    private const double CellSize = 1.0;

    // ---- 枚举：顺序是单一真值（D24）----------------------------------------------

    [Fact]
    public void 画线_逐格顺序就是从起点走到终点_两跑逐字全等()
    {
        var core = Batch();
        // 摆在 Bresenham 真正走到的第三步那一格 (3,2)：摆偏一格就测不到「逐格原因」那条路了。
        core.Commit("peg", Center(new GridCell(3, 2)), 0, "blocker");

        var first = Table(core.CheckLine("peg", Center(new GridCell(0, 0)), Center(new GridCell(4, 2)), 0));
        var again = Table(core.CheckLine("peg", Center(new GridCell(0, 0)), Center(new GridCell(4, 2)), 0));

        var walked = BatchShapes.Line(new GridCell(0, 0), new GridCell(4, 2))
            .Select(c => ConstructionNumbers.Point(Center(c)));
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        again.ShouldBe(first, "同手势两跑逐格裁决逐字全等（枚举顺序是确定性）");
        first.Split(';').Select(s => s.Split(':')[0]).ShouldBe(walked, "成员表逐格就是 BatchShapes.Line 那一条走法（D24 的单一真值）");
        first.ShouldContain("3.5,2.5:no:Occupied", Case.Sensitive, "中间那一格压着邻居");    }

    [Fact]
    public void 画线_两端都含且每格只走一次()
    {
        var cells = BatchShapes.Line(new GridCell(0, 0), new GridCell(4, 2));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        cells.Count.ShouldBe(5, "5×2 的斜线走 5 步，不是先横后竖的 9 步");
        cells.First().ShouldBe(new GridCell(0, 0));
        cells.Last().ShouldBe(new GridCell(4, 2));
        cells.Distinct().Count().ShouldBe(cells.Count, "Bresenham 每格只走一次");
        cells.ShouldBe(BatchShapes.Line(new GridCell(0, 0), new GridCell(4, 2)), "纯函数：同输入同输出");    }

    [Fact]
    public void 画线_反向也合法_方向是这一发手势的语义()
    {
        var forward = BatchShapes.Line(new GridCell(0, 0), new GridCell(2, 0));
        var backward = BatchShapes.Line(new GridCell(2, 0), new GridCell(0, 0));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        forward.Select(c => c.Col).ShouldBe(new[] { 0, 1, 2 });
        backward.Select(c => c.Col).ShouldBe(new[] { 2, 1, 0 }, false, "画线的两端不等价：不归一，序就是手势说的那个方向");    }

    [Fact]
    public void 画矩形_两角自己按minmax归一_先行后列()
    {
        var cells = BatchShapes.Rect(new GridCell(2, 1), new GridCell(0, 0));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        cells.ShouldBe(new[]
        {
            new GridCell(0, 0), new GridCell(1, 0), new GridCell(2, 0),
            new GridCell(0, 1), new GridCell(1, 1), new GridCell(2, 1),
        }, false, "矩形的两角等价：谁大谁小都是同一片（与画线不同）");    }

    [Fact]
    public void 画矩形_从哪一角起拖成员表都一样()
    {
        var core = Batch();

        var normal = Table(core.CheckRect("peg", Center(new GridCell(0, 0)), Center(new GridCell(2, 1)), 0));
        var dragged = Table(core.CheckRect("peg", Center(new GridCell(2, 1)), Center(new GridCell(0, 0)), 0));

        dragged.ShouldBe(normal);
    }

    // ---- 部分成功（D24）----------------------------------------------------------

    [Fact]
    public void 画线_能放几格放几格_逐格原因与计数都对()
    {
        var core = Batch();
        core.Commit("peg", Center(new GridCell(2, 0)), 0, "blocker");

        var result = core.PlaceLine("peg", Center(new GridCell(0, 0)), Center(new GridCell(4, 0)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.Members.Count.ShouldBe(5);
        result.PlacedCount.ShouldBe(4);
        result.FailedCount.ShouldBe(1);
        result.Ok.ShouldBeTrue("4/5 也是打成了（逐格成败在成员表里，D24）");
        result.Failures.Single().Reason.ShouldBe(PlacementReason.Occupied);
        result.Failures.Single().Cell.ShouldBe(new GridCell(2, 0));
        result.PlacedIds.ShouldBe(new[] { "b1", "b2", "b3", "b4" }, Case.Sensitive, "成功名单按枚举序，号由模块那一处发（D34）");
        core.Count.ShouldBe(5, "4 座加上先摆的那座挡路的");    }

    [Fact]
    public void 画线_边判边落_后一格看得见前一格刚占下的地()
    {
        var core = Batch();

        // L 形（3 格）逐格摆：第二个锚点压着第一个的 (1,0) —— 它读数 Occupied；
        // 第三个锚点只压着第二个（已被拒），不压第一个，于是它照样落。
        // 「一个成员失败会让后面几格一起失败」那种实现会在这里露馅。
        var result = core.PlaceLine("keep", Center(new GridCell(0, 0)), Center(new GridCell(2, 0)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.PlacedCount.ShouldBe(2);
        result.FailedCount.ShouldBe(1);
        result.Failures.Single().Reason.ShouldBe(PlacementReason.Occupied);
        result.Failures.Single().Cell.ShouldBe(new GridCell(1, 0));
        result.Placed.Select(p => p.Cell).ShouldBe(new[] { new GridCell(0, 0), new GridCell(2, 0) });
        core.Count.ShouldBe(2);
        core.OccupiedCount.ShouldBe(6, "两座 L 形各 3 格");    }

    [Fact]
    public void 成功成员逐个发Placed_失败格一条Rejected都不发()
    {
        var core = Batch();
        core.Commit("peg", Center(new GridCell(2, 0)), 0, "blocker");

        core.PlaceLine("peg", Center(new GridCell(0, 0)), Center(new GridCell(4, 0)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Events.Count<BuildingPlaced>().ShouldBe(5, "4 个成功成员各一条 + 先摆的那 1 条（D26：现存订阅者零新接线）");
        core.Events.Count<PlacementRejected>().ShouldBe(0, "失败格不逐个发 Rejected：裁决流与汇总两处都有了（D26）");
        core.Events.Count<BatchPlaced>().ShouldBe(1, "一个手势一条汇总，也是一条 Journal 记录的对应面（D24）");    }

    [Fact]
    public void 汇总事件同形状_线与矩形之间没有可选位()
    {
        var line = Batch();
        line.Commit("peg", Center(new GridCell(2, 0)), 0, "blocker");
        line.PlaceLine("peg", Center(new GridCell(0, 0)), Center(new GridCell(4, 0)), 0);

        var rect = Batch();
        rect.PlaceRect("peg", Center(new GridCell(0, 0)), Center(new GridCell(1, 0)), 0);

        var lineEvent = line.Events.Where<BatchPlaced>().Single();
        var rectEvent = rect.Events.Where<BatchPlaced>().Single();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        // 逐字钉死：两个形状的明细行结构完全一样，只有形状名与那几行值不同。
        // 有一格「矩形才有」的可选位，表现层就得为两个形状各写一套解析（D26）。
        lineEvent.Detail.ShouldBe("Line placed=b1,b2,b3,b4 failed=2.5,0.5:Occupied");
        rectEvent.Detail.ShouldBe("Rect placed=b1,b2 failed=-");
        lineEvent.GetType().ShouldBe(rectEvent.GetType());
        lineEvent.Shape.ShouldBe(GestureShape.Line);
        rectEvent.Shape.ShouldBe(GestureShape.Rect);
        lineEvent.Subject.ShouldBe("peg", "Subject 是这次手势的蓝图");    }

    [Fact]
    public void 画矩形_部分成功与线同款()
    {
        var core = Batch();
        core.Commit("tower", Center(new GridCell(1, 1)), 0, "blocker");

        // 3x3 里，blocker 占 (1,1)(2,1)(1,2)(2,2)：四个格各压着两格。
        var result = core.PlaceRect("peg", Center(new GridCell(0, 0)), Center(new GridCell(2, 2)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.Members.Count.ShouldBe(9, "3×3 九格");
        result.PlacedCount.ShouldBe(5);
        result.FailedCount.ShouldBe(4);
        result.Failures.Select(f => f.Cell).ShouldBe(new[]
        {
            new GridCell(1, 1), new GridCell(2, 1), new GridCell(1, 2), new GridCell(2, 2),
        }, false, "先行后列：失败格也是按那个次序列出来的");
        result.Reason.ShouldBe(PlacementReason.Occupied, "没打成才给原因：这里打成了，reason=None");    }

    // ---- 纯查询与「预览 = 结果」（D25、D29）--------------------------------------

    [Fact]
    public void 画矩形预览的逐格表与落建的逐格表逐字相同()
    {
        // 两个世界，同一份初始状态：一个只查，一个照着放。两张表必须逐字一样。
        var query = Batch();
        var before = query.CheckRect("peg", Center(new GridCell(0, 0)), Center(new GridCell(2, 1)), 0);
        var events = query.Events.Total;
        var snapshot = query.DebugSnapshotSegment();

        var commit = Batch();
        var landed = commit.PlaceRect("peg", Center(new GridCell(0, 0)), Center(new GridCell(2, 1)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        BatchText.Table(landed.Members).ShouldBe(LayoutText.Table(before.Members!), "预览 = 结果：红格与落建报的那一串是同一句话（D25、D29）");
        landed.PlacedCount.ShouldBe(6);
        query.Events.Total.ShouldBe(events, "只读查询不发事件");
        query.DebugSnapshotSegment().ShouldBe(snapshot, "只读查询不改状态");
        query.OccupiedCount.ShouldBe(0, "只读查询不占格");    }

    [Fact]
    public void 画矩形与画线都是纯查询_重复调用没有副作用()
    {
        var core = Batch();

        for (var i = 0; i < 3; i++)
        {
            core.CheckLine("peg", Center(new GridCell(0, 0)), Center(new GridCell(3, 0)), 0);
            core.CheckRect("peg", Center(new GridCell(0, 0)), Center(new GridCell(1, 1)), 0);
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Events.Total.ShouldBe(0, "call 类不入录、不发事件（「写路径进 Journal」）");
        core.Count.ShouldBe(0);
        core.OccupiedCount.ShouldBe(0);
        core.DebugSnapshotSegment().ShouldContain("buildings=0");    }

    [Fact]
    public void 未知蓝图_每个成员同一个原因_一条汇总_一寸不动()
    {
        var core = Batch();
        var snapshot = core.DebugSnapshotSegment();

        var result = core.PlaceLine("ghost", Center(new GridCell(0, 0)), Center(new GridCell(2, 0)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.Members.Count.ShouldBe(3, "逐格表照样是全的，一行一个原因");
        result.Failures.Select(f => f.Reason).ShouldAllBe(_e => _e == PlacementReason.UnknownBlueprint);
        result.PlacedCount.ShouldBe(0);
        result.Ok.ShouldBeFalse("一个都没打成就没打成");
        result.Reason.ShouldBe(PlacementReason.UnknownBlueprint, "失败原因逐字进 inject 的 out reason（D34）");
        core.Events.Count<BatchPlaced>().ShouldBe(1);
        core.DebugSnapshotSegment().ShouldBe(snapshot);    }

    [Fact]
    public void 出界逐格报OutOfBounds_不是整条手势失败()
    {
        var core = Batch(3); // 窄边界：列 0..3

        var result = core.PlaceLine("peg", Center(new GridCell(1, 0)), Center(new GridCell(6, 0)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.PlacedCount.ShouldBe(3, "边界内那三格照落");
        result.Failures.Count.ShouldBe(3);
        result.Failures.Select(f => f.Reason).ShouldAllBe(_e => _e == PlacementReason.OutOfBounds);    }

    [Fact]
    public void 负坐标那侧照样枚举得到_两跑逐字全等()
    {
        var core = Batch();

        var first = Table(core.CheckLine("peg", Center(new GridCell(-3, 0)), Center(new GridCell(-1, 0)), 0));
        var again = Table(core.CheckLine("peg", Center(new GridCell(-3, 0)), Center(new GridCell(-1, 0)), 0));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        again.ShouldBe(first);
        first.Split(';').Length.ShouldBe(3);
        first.ShouldStartWith("-2.5,0.5:ok", Case.Sensitive, "floor 不是截断（D9）");    }

    // ---- 批量拆除（D24）----------------------------------------------------------

    [Fact]
    public void 批量拆除_逐成员发Removed去向拆除_不加汇总事件()
    {
        var core = Batch();
        core.Commit("peg", Center(new GridCell(0, 0)), 0, "p1");
        core.Commit("peg", Center(new GridCell(1, 0)), 0, "p2");
        core.Commit("tower", Center(new GridCell(1, 1)), 0, "t1");
        core.Events.ClearKept();

        var result = core.RemoveRect(Center(new GridCell(0, 0)), Center(new GridCell(2, 1)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.RemovedIds.ShouldBe(new[] { "p1", "p2", "t1" }, Case.Sensitive, "按枚举序首次命中的次序（D24 的顺序单一真值）");
        result.CellsVisited.ShouldBe(6, "3×2 六格");
        result.CellsEmpty.ShouldBe(2, "六格里空着两格");
        result.Ok.ShouldBeTrue("一格没人不算失败");
        core.Count.ShouldBe(0, "t1 占四格，只拆了一次");
        core.OccupiedCount.ShouldBe(0);
        core.Events.Count<BuildingRemoved>().ShouldBe(3, "逐成员一条");
        core.Events.Where<BuildingRemoved>().Select(e => e.Cause).ShouldAllBe(_e => _e == RemovalCause.Dismantle);
        core.Events.Count<BatchPlaced>().ShouldBe(0, "拆除不加汇总事件（D24）");
        core.Events.Count<LayoutPlaced>().ShouldBe(0);    }

    [Fact]
    public void 批量拆除_拆完那一块能再放进去()
    {
        var core = Batch();
        core.Commit("tower", Center(new GridCell(0, 0)), 0, "t1");
        core.Commit("peg", Center(new GridCell(5, 5)), 0, "keep");

        core.RemoveRect(Center(new GridCell(0, 0)), Center(new GridCell(1, 1)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.BuildingIdAt(0.5f, 0.5f).ShouldBe(string.Empty, "旧格腾干净了");
        core.BuildingIdAt(5.5f, 5.5f).ShouldBe("keep", "区域外的没动");
        core.PlaceRect("peg", Center(new GridCell(0, 0)), Center(new GridCell(1, 1)), 0).PlacedCount.ShouldBe(4);    }

    [Fact]
    public void 批量拆除_空区域回打成了但什么都没拆()
    {
        var core = Batch();

        var result = core.RemoveRect(Center(new GridCell(0, 0)), Center(new GridCell(1, 1)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.Ok.ShouldBeTrue("空区域不是错（逐格失败的理由在这一层没有）");
        result.RemovedCount.ShouldBe(0);
        result.CellsVisited.ShouldBe(4);
        result.CellsEmpty.ShouldBe(4);
        core.Events.Total.ShouldBe(0, "没拆就没事件（与 D23 的「拒绝一寸不动」同款）");    }

    // ---- 悬停：四个形状都推得进去（D25、D27）-----------------------------------

    [Fact]
    public void 推画线手势_推出一条带成员表的裁决()
    {
        var core = Batch();
        core.Commit("peg", Center(new GridCell(2, 0)), 0, "blocker");
        var pushed = new List<HoverVerdict>();
        core.Hover.Changed += v => pushed.Add(v!);

        core.Hover.Push(new GestureSpec("peg", Center(new GridCell(0, 0)), 0, null, GestureShape.Line,
            LineEnd: Center(new GridCell(4, 0))));

        var hover = pushed.Single();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hover.Shape.ShouldBe(GestureShape.Line, "表现层靠它分「画一格」与「画一片」");
        hover.Members.Count.ShouldBe(5);
        hover.Ok.ShouldBeFalse("有一个成员压着邻居，整体就不是全绿");
        hover.Reason.ShouldBe(PlacementReason.Occupied, "取枚举序里第一个失败成员");
        LayoutText.Table(hover.Members!).ShouldContain("2.5,0.5:no:Occupied", Case.Sensitive, "红格从成员表里读，表现层不许自己再算一遍（D29）");
        hover.Line.ShouldContain("members=5");    }

    [Fact]
    public void 推画矩形手势_红绿跟着占用重算()
    {
        var core = Batch();
        core.Hover.Push(new GestureSpec("peg", Center(new GridCell(0, 0)), 0, null, GestureShape.Rect,
            RectMax: Center(new GridCell(1, 1))));
        core.Hover.Last!.Ok.ShouldBeTrue("空地全绿");

        core.Commit("peg", Center(new GridCell(1, 1)), 0, "blocker");

        var hover = core.Hover.Last!;
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hover.Ok.ShouldBeFalse("占用变了要重算，幽灵该从绿变红");
        hover.Members!.Count(m => !m.Ok).ShouldBe(1);    }

    [Fact]
    public void 同一片格换形状_裁决变了要重推()
    {
        var core = Batch();
        core.Hover.Push(new GestureSpec("peg", Center(new GridCell(0, 0)), 0, null, GestureShape.Line,
            LineEnd: Center(new GridCell(1, 0))));
        var pushed = 0;
        core.Hover.Changed += _ => pushed++;

        // 同一处、同样的成员，只是从「画线」切到「画矩形」：形状进了去重的判据，
        // 否则表现层切工具时幽灵不重画（看着是同一片红绿，枚举的格其实不同）。
        core.Hover.Push(new GestureSpec("peg", Center(new GridCell(0, 0)), 0, null, GestureShape.Rect,
            RectMax: Center(new GridCell(1, 0))));

        pushed.ShouldBe(1);
    }

    [Fact]
    public void 推缺件的手势当场抛_不静默折成只放一个()
    {
        var core = Batch();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<InvalidOperationException>(() => core.Hover.Push(
            new GestureSpec("peg", Center(new GridCell(0, 0)), 0, null, GestureShape.Line)),
            "画线缺终点");
        Should.Throw<InvalidOperationException>(() => core.Hover.Push(
            new GestureSpec("peg", Center(new GridCell(0, 0)), 0, null, GestureShape.Rect)),
            "画矩形缺 max 那一角");
        Should.Throw<InvalidOperationException>(() => core.Hover.Push(
            new GestureSpec("", Center(new GridCell(0, 0)), 0, null, GestureShape.Layout)),
            "布局缺文本");
        core.Hover.Current.ShouldBeNull("抛之前没存进当前手势：存了就是一颗雷");
        // 布局形状用不到蓝图 id（成员在文本里），空 id 不该在这里挡一次。
        core.Hover.Push(new GestureSpec("", Center(new GridCell(0, 0)), 0, "v1;t;peg,0,0,0;peg,1,0,0",
            GestureShape.Layout));
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Hover.Last!.Shape.ShouldBe(GestureShape.Layout);
        core.Hover.Last.Members.Count.ShouldBe(2, "布局的逐成员表也走这一个口");
        core.Hover.Last.Ok.ShouldBeTrue();    }

    // ---- 自由放置那一侧 -----------------------------------------------------------

    [Fact]
    public void 自由放置_画线走那套格点_间距够就一格一格落下()
    {
        // 半径 1、画线步长 3：间距 3 ≥ 半径和 2，一排都放得下。
        var core = Free(3);

        var result = core.PlaceLine("small", new Vector2(0f, 0f), new Vector2(6f, 0f), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.PlacedCount.ShouldBe(3);
        result.Placed.Select(m => m.Candidate).ShouldBe(new[] { new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(6f, 0f) }, false, "格点间距是这条规则的构造参数（BatchStep）");
        core.Events.Count<BatchPlaced>().ShouldBe(1);    }

    [Fact]
    public void 自由放置_格点太密逐格报TooClose_不是整条失败()
    {
        // 半径 1、步长 1：第一个落成之后，隔一格那个正好等于半径和（贴上是放得下的 ——
        // 边缘到边缘的判定是 < 而不是 ≤，D12），再往后那两个都太近。
        var core = Free(1);

        var result = core.PlaceLine("small", new Vector2(0f, 0f), new Vector2(3f, 0f), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.PlacedCount.ShouldBe(2);
        result.FailedCount.ShouldBe(2);
        result.Failures.Select(f => f.Candidate).ShouldBe(new[] { new Vector2(1f, 0f), new Vector2(3f, 0f) }, false, "失败的是压着已经落成那两座的格");
        result.Failures.Select(f => f.Reason).ShouldAllBe(_e => _e == PlacementReason.TooClose);    }

    // ---- 确定性 -----------------------------------------------------------------

    [Fact]
    public void 同种子两遍逐字全等_批量这一族也逐字稳()
    {
        static string Run()
        {
            var core = Batch();
            core.Commit("peg", Center(new GridCell(6, 0)), 0, "blocker");
            var line = core.PlaceLine("peg", Center(new GridCell(0, 0)), Center(new GridCell(5, 2)), 0);
            var rect = core.PlaceRect("peg", Center(new GridCell(0, 4)), Center(new GridCell(2, 5)), 0);
            var removed = core.RemoveRect(Center(new GridCell(0, 0)), Center(new GridCell(2, 2)));
            return core.DebugSnapshotSegment()
                + core.Events.TailText(99)
                + BatchText.Line(line)
                + BatchText.Line(rect)
                + BatchText.Line(removed);
        }

        Run().ShouldBe(Run());
    }

    // ---- 工具 -------------------------------------------------------------------

    /// <summary>逐成员裁决表那一行（查询预览与落建读的是同一份渲染，「断言比快照」 的状态级读数）。</summary>
    private static string Table(PlacementVerdict verdict) => LayoutText.Table(verdict.Members!);

    private static Vector2 Center(GridCell cell) => GridPlacementRule.CenterOf(cell, CellSize);

    private static ConstructionCore Batch(int maxCol = 12, IConstructionHost? host = null)
        => new(new GridPlacementRule(CellSize, -4, -4, maxCol, 12, host), new[]
        {
            new Blueprint("peg", 20, CellFootprint.FromCells(new[] { new GridCell(0, 0) })),
            new Blueprint("keep", 100, CellFootprint.FromCells(new[]
            {
                new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
            })),
            new Blueprint("tower", 100, CellFootprint.FromCells(new[]
            {
                new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1), new GridCell(1, 1),
            })),
        });

    private static ConstructionCore Free(double batchStep)
        => new(new FreePlacementRule(batchStep: batchStep), new[]
        {
            new Blueprint("small", 100, new RadiusFootprint(1)),
        });
}
