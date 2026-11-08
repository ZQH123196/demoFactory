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
/// 编辑器动词（Move / Rotate / BuildingAt）的契约测试 —— <b>零引擎依赖</b>（spec 3 节第 2 条）。
///
/// 这里守的是几条「看起来对、其实会分叉」的地方：排除自身有没有真排除（不排除的话网格侧一格都
/// 动不了、自由侧连原地都回不去）、失败那一发是不是真的一寸不动（位置、占用、载荷、进度、事件流）、
/// 旋转是不是原地（锚点漂一格的话 2×2 恒成功那条推论就废了）、编辑会不会把建造进度与血量顺手
/// 写坏。这些在引擎里只能靠出图对比出来，现在是一发 <c>dotnet test</c>（「测试与实测分工」）。
///
/// 断言强度只比检查点快照（状态级），事件流读条数与字段、不比逐字（「断言比快照」）。
/// </summary>
public class ConstructionEditorTests
{
    private const double CellSize = 1.0;

    // ---- Move：原子与排除自身（D20）---------------------------------------------

    [Fact]
    public void 移动失败一寸不动_位置占用载荷进度事件流全不变()
    {
        var payload = new Dictionary<string, int> { ["lv"] = 3 };
        var core = Editor(40);
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1", null, payload);
        core.Commit("peg", new Vector2(5.2f, 5.2f), 0, "peg1");
        // 悬停压在 peg1 那一格上：这一发没成，悬停的重算次数也不该动 ——
        // 「一寸不动」连读数口一起算，占用没变就没有重算的理由。
        core.Hover.Push(new GestureSpec("peg", new Vector2(5.2f, 5.2f)));
        var before = core.DebugSnapshotSegment();
        var events = core.Events.Total;
        var recomputes = core.Hover.Recomputes;
        var occupied = core.OccupiedCount;
        var rest = core.Find("keep1")!;
        var keep = (rest.Position, rest.Anchor, ConstructionNumbers.Cells(rest.Cells), rest.Health,
            rest.RemainingTicks, rest.Progress);

        // 压到 peg1 那一格上：格被占 → 读数 Occupied
        var r = core.Move("keep1", new Vector2(5.2f, 5.2f));

        var after = core.Find("keep1")!;
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeFalse();
        r.Reason.ShouldBe(PlacementReason.Occupied);
        r.BuildingId.ShouldBe("keep1", "拒绝也要报出是哪一座，原型据此不扣款（D6）");
        (after.Position, after.Anchor, ConstructionNumbers.Cells(after.Cells), after.Health,
            after.RemainingTicks, after.Progress).ShouldBe(keep, "位置 / 锚点 / 占地 / 血量 / 进度一个数都没许动");
        after.Payload.ShouldBeSameAs(payload, "载荷原样挂着");
        core.DebugSnapshotSegment().ShouldBe(before, "检查点快照逐字不变");
        core.OccupiedCount.ShouldBe(occupied, "占用没动");
        core.Events.Total.ShouldBe(events, "失败连事件都不发");
        core.Hover.Recomputes.ShouldBe(recomputes);
        core.BuildingIdAt(0.5f, 0.5f).ShouldBe("keep1", "两座都还在原处");
        core.BuildingIdAt(5.5f, 5.5f).ShouldBe("peg1");    }

    [Fact]
    public void 移动成功_腾旧占新且只发一条Moved()
    {
        var core = Editor();
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");
        core.Commit("peg", new Vector2(5.2f, 5.2f), 0, "peg1");

        var r = core.Move("keep1", new Vector2(4.2f, 4.2f));

        var rest = core.Find("keep1")!;
        var moved = core.Events.Where<BuildingMoved>().ToList();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeTrue();
        r.Reason.ShouldBe(PlacementReason.None);
        rest.Anchor.ShouldBe(new GridCell(4, 4));
        rest.Position.ShouldBe(new Vector2(4.5f, 4.5f), "坐标仍由锚点格心推导（D9）");
        core.BuildingIdAt(0.5f, 0.5f).ShouldBe(string.Empty, "旧格腾干净了");
        core.Check("keep", new Vector2(0.2f, 0.2f)).Ok.ShouldBeTrue("腾出来的那几格能再放");
        core.BuildingIdAt(4.5f, 4.5f).ShouldBe("keep1", "新格占上了");
        core.BuildingIdAt(5.5f, 4.5f).ShouldBe("keep1", "占地的每一格都是它的");
        core.BuildingIdAt(5.5f, 5.5f).ShouldBe("peg1", "没碰到邻居");
        core.OccupiedCount.ShouldBe(4, "腾旧占新之后总数不变：3 格 + 1 格");
        moved.Count.ShouldBe(1, "一条移动一条事件");
        moved[0].Subject.ShouldBe("keep1");
        moved[0].From.ShouldBe(new Vector2(0.5f, 0.5f));
        moved[0].To.ShouldBe(new Vector2(4.5f, 4.5f));    }

    [Fact]
    public void 目标区域与自身占地重叠时可移_排除自身是对的()
    {
        var core = Editor();
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");

        // L 形 (0,0)(1,0)(0,1) 的锚点挪到 (1,0)：新占地 (1,0)(2,0)(1,1) 与旧占地压着 (1,0)。
        // 那一格占着的正是它自己 —— 不排除自身，这一次移动永远动不了（D20）。
        var r = core.Move("keep1", new Vector2(1.2f, 0.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeTrue();
        core.Find("keep1")!.Anchor.ShouldBe(new GridCell(1, 0));
        core.BuildingIdAt(1.5f, 0.5f).ShouldBe("keep1", "压着的那格还是它的");
        core.BuildingIdAt(2.5f, 0.5f).ShouldBe("keep1", "新占的那格也是它的");
        core.BuildingIdAt(0.5f, 0.5f).ShouldBe(string.Empty, "让出来的那格腾了");
        core.OccupiedCount.ShouldBe(3, "还是 3 格，没多占也没少占");    }

    [Fact]
    public void 移动不存在的id回读数UnknownId_不抛()
    {
        var core = Editor();

        var r = core.Move("ghost", new Vector2(1.2f, 1.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeFalse();
        r.Reason.ShouldBe(PlacementReason.UnknownId);
        r.BuildingId.ShouldBe("ghost");
        core.Events.Total.ShouldBe(0, "册上没有的东西发不出事件");    }

    // ---- Rotate：原地变换占地后重新 Check（D21）---------------------------------

    [Fact]
    public void 旋转撞邻居拒绝且一寸不动()
    {
        var core = Editor();
        core.Commit("blade", new Vector2(0.2f, 0.2f), 0, "blade1");
        // 找一格「转 90° 之后才占、现在空着」的格，把邻居摆在它正中间
        var target = GridOrientation.Transform(Blade, GridOrientation.R90).At(new GridCell(0, 0))
            .First(c => core.BuildingIdAt(Center(c).X, Center(c).Y) == string.Empty);
        core.Commit("peg", Center(target), 0, "peg1");
        var before = core.DebugSnapshotSegment();
        var events = core.Events.Total;

        var r = core.Rotate("blade1", GridOrientation.R90);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeFalse();
        r.Reason.ShouldBe(PlacementReason.Occupied);
        core.Find("blade1")!.Orientation.ShouldBe(0, "方向没动");
        core.DebugSnapshotSegment().ShouldBe(before, "一寸不动");
        core.Events.Total.ShouldBe(events, "拒绝连事件都不发");
        core.Events.Count<BuildingRotated>().ShouldBe(0);    }

    [Fact]
    public void 旋转出界拒绝且一寸不动()
    {
        // 窄边界：blade 占 2×3，锚点 (1,2) 正好卡在列上界 2。转 90° 盒宽高互换，要占 (3,2) 就出界了 ——
        // 出界判定与占格、间距走的是同一条流水线，没有为编辑动词单开一档。
        var core = new ConstructionCore(new GridPlacementRule(CellSize, 0, 0, 2, 9), new[]
        {
            new Blueprint("blade", 100, Blade),
        });
        core.Commit("blade", new Vector2(1.2f, 2.2f), 0, "blade1");
        var before = core.DebugSnapshotSegment();
        var events = core.Events.Total;

        var outOfBounds = core.Rotate("blade1", GridOrientation.R90);
        var afterReject = core.DebugSnapshotSegment();
        var eventsAfterReject = core.Events.Total;
        var inside = core.Rotate("blade1", GridOrientation.R180);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        outOfBounds.Ok.ShouldBeFalse();
        outOfBounds.Reason.ShouldBe(PlacementReason.OutOfBounds);
        afterReject.ShouldBe(before, "一寸不动");
        eventsAfterReject.ShouldBe(events, "被拒的那一发没发事件");
        inside.Ok.ShouldBeTrue("180° 的盒宽高不变，落回原处那几格");
        core.Events.Count<BuildingRotated>().ShouldBe(1, "只发打成的那一条");    }

    [Fact]
    public void D4_2x2原地旋转恒成功_四邻全占着也一样()
    {
        var core = Editor();
        core.Commit("tower", new Vector2(0.2f, 0.2f), 0, "tower1");
        // 四个邻格全摆上邻居：任何「转出这四格」的实现都会在这里撞上
        core.Commit("peg", Center(new GridCell(2, 0)), 0, "east");
        core.Commit("peg", Center(new GridCell(-1, 0)), 0, "west");
        core.Commit("peg", Center(new GridCell(0, 2)), 0, "north");
        core.Commit("peg", Center(new GridCell(0, -1)), 0, "south");

        var oks = new List<bool>();
        var cells = new List<string>();
        for (var i = 0; i < GridOrientation.Count; i++)
        {
            oks.Add(core.Rotate("tower1", i).Ok);
            cells.Add(ConstructionNumbers.Cells(core.Find("tower1")!.Cells));
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        oks.ShouldAllBe(_e => _e == true, "2×2 绕盒心转回来是同一组格，八个值都打不成的话说明有特判或变换绕错了点（D21）");
        cells.Distinct().Count().ShouldBe(1, "八个方向落在同一组格上：" + cells[0]);
        core.Find("tower1")!.Anchor.ShouldBe(new GridCell(0, 0), "锚点不漂");
        core.Find("tower1")!.Position.ShouldBe(new Vector2(0.5f, 0.5f));
        core.Events.Count<BuildingRotated>().ShouldBe(8);    }

    [Fact]
    public void 旋转原地_锚点与坐标都不动_只有方向与占地跟着转()
    {
        var core = Editor();
        core.Commit("blade", new Vector2(3.2f, 4.9f), 0, "blade1");
        var rest = core.Find("blade1")!;
        var before = (rest.Anchor, rest.Position);
        var cellsBefore = ConstructionNumbers.Cells(rest.Cells);

        core.Rotate("blade1", GridOrientation.M180);

        var after = core.Find("blade1")!;
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        (after.Anchor, after.Position).ShouldBe(before, "候选点就是它自己那一点，规则再吸一次回到同一个锚点格（D9、D21）");
        after.Orientation.ShouldBe(GridOrientation.M180);
        ConstructionNumbers.Cells(after.Cells).ShouldNotBe(cellsBefore, "占地转了");    }

    [Fact]
    public void L形八值矩阵_编辑器侧旋转逐一可区分()
    {
        var core = Editor();
        core.Commit("blade", new Vector2(0.2f, 0.2f), 0, "blade1");

        var seen = new List<string>();
        for (var i = 0; i < GridOrientation.Count; i++)
        {
            var r = core.Rotate("blade1", i);
            var b = core.Find("blade1")!;
            seen.Add(ConstructionNumbers.Cells(b.Cells));
            r.Ok.ShouldBeTrue($"{GridOrientation.Label(i)}：空地上转不该有拒绝");
            b.Orientation.ShouldBe(i);
            ConstructionNumbers.Cells(b.Cells).ShouldBe(ConstructionNumbers.Cells(
                    GridOrientation.Transform(Blade, i).At(new GridCell(0, 0))), $"{GridOrientation.Label(i)}：编辑器侧落下的格与规则那份变换不是同一份（D29 单一真值）");
        }

        seen.Distinct().Count().ShouldBe(GridOrientation.Count, "八个方向里有撞在一起的，D4 变换不成立 —— " + string.Join(" | ", seen));
    }

    [Fact]
    public void 网格方向越界当场抛_不折回0()
    {
        var core = Editor();
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");
        var before = core.DebugSnapshotSegment();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<ArgumentOutOfRangeException>(() => core.Rotate("keep1", GridOrientation.Count),
            "D4 八值之外的方向是作者错误（D7），抛出来比折回 rot0 好查");
        core.DebugSnapshotSegment().ShouldBe(before, "抛之前没写任何一列");    }

    // ---- 载荷、血量、建造进度：编辑不许顺手写坏（D20）---------------------------

    [Fact]
    public void 移动与旋转保留id载荷血量与建造进度()
    {
        var payload = new Dictionary<string, int> { ["lv"] = 3 };
        var core = Editor(40);
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1", null, payload);
        core.Damage("keep1", 30);
        core.Advance(10);
        core.PauseBuild("keep1");
        var rest = core.Find("keep1")!;
        var before = (rest.Id, rest.Health, rest.RemainingTicks, rest.Paused, rest.State);

        var moved = core.Move("keep1", new Vector2(4.2f, 4.2f));
        var rotated = core.Rotate("keep1", GridOrientation.M180);
        var now = core.Find("keep1")!;
        var after = (now.Id, now.Health, now.RemainingTicks, now.Paused, now.State);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        moved.Ok.ShouldBeTrue();
        rotated.Ok.ShouldBeTrue();
        after.ShouldBe(before, "编辑只写位置、锚点、方向、占地那四列");
        now.Payload.ShouldBeSameAs(payload, "载荷是同一个对象，不复制也不丢（D31）");
        after.Health.ShouldBe(70, "扣过的血没回（算术 double，「double 与不变文化」）");
        after.RemainingTicks.ShouldBe(30, "建造进度不因挪位置而重算（D17）");    }

    [Fact]
    public void 在建可移_挪完照样数到头()
    {
        var core = Editor(40);
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");
        core.Advance(10);

        core.Move("keep1", new Vector2(4.2f, 4.2f));
        core.Advance(30);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Find("keep1")!.Anchor.ShouldBe(new GridCell(4, 4));
        core.Find("keep1")!.State.ShouldBe(BuildState.Completed, "挪了地方照样数到头");
        core.Find("keep1")!.RemainingTicks.ShouldBe(0);
        core.Events.Count<BuildingCompleted>().ShouldBe(1);    }

    [Fact]
    public void 暂停中可移_倒数照旧停着()
    {
        var core = Editor(40);
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");
        core.Advance(10);
        core.PauseBuild("keep1");

        var moved = core.Move("keep1", new Vector2(4.2f, 4.2f));
        core.Advance(10);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        moved.Ok.ShouldBeTrue();
        core.Find("keep1")!.Paused.ShouldBeTrue("暂停标志跟着建筑走，挪位置不改它（D18）");
        core.Find("keep1")!.RemainingTicks.ShouldBe(30, "暂停期间倒数不走");
        core.Events.Count<BuildingCompleted>().ShouldBe(0);    }

    [Fact]
    public void 占用变了悬停重算_挪开之后幽灵从红变绿()
    {
        var core = Editor();
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");
        core.Hover.Push(new GestureSpec("peg", new Vector2(0.2f, 0.2f)));
        core.Hover.Last!.Ok.ShouldBeFalse("候选压在 keep1 身上");

        core.Move("keep1", new Vector2(6.2f, 6.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Hover.Last!.Ok.ShouldBeTrue("占用变了要重算，幽灵该从红变绿");
        core.Hover.Last!.Position.ShouldBe(new Vector2(0.5f, 0.5f));    }

    // ---- BuildingAt：拾取走逻辑真值（D22）---------------------------------------

    [Fact]
    public void BuildingAt_命中回id未命中回空_重复调用没有副作用()
    {
        var core = Editor();
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");
        var before = core.DebugSnapshotSegment();
        var events = core.Events.Total;
        var occupied = core.OccupiedCount;

        var hit = new List<string>();
        var miss = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            hit.Add(core.BuildingIdAt(0.5f, 0.5f));
            hit.Add(core.BuildingIdAt(1.5f, 0.5f));
            hit.Add(core.BuildingIdAt(0.5f, 1.5f));
            miss.Add(core.BuildingIdAt(8.5f, 8.5f));
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hit.ShouldAllBe(_e => _e == "keep1", "占着的三格都回同一座");
        miss.ShouldAllBe(_e => _e == string.Empty, "没占着回空串：读数是字符串，少一层判");
        core.DebugSnapshotSegment().ShouldBe(before, "重复读不改状态");
        core.Events.Total.ShouldBe(events, "只读查询不发事件");
        core.OccupiedCount.ShouldBe(occupied);    }

    [Fact]
    public void BuildingAt_跟着占用走_挪走之后那一格回空()
    {
        var core = Editor();
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, "keep1");

        core.Move("keep1", new Vector2(4.2f, 4.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.BuildingIdAt(0.5f, 0.5f).ShouldBe(string.Empty);
        core.BuildingIdAt(4.5f, 4.5f).ShouldBe("keep1");    }

    // ---- 自由放置那一侧 ---------------------------------------------------------

    [Fact]
    public void 自由放置_挪回自己那一处照样成_排除自身对圆形占地一样成立()
    {
        var core = FreeEditor();
        core.Commit("small", new Vector2(10f, 10f), 0, "small1");
        core.Commit("small", new Vector2(30f, 10f), 0, "small2");

        // 半径和 2：挪回原处时唯一「太近」的那个就是它自己。不排除就动不了（D20）。
        var r = core.Move("small1", new Vector2(10f, 10f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeTrue();
        core.OccupiedCount.ShouldBe(2, "腾旧占新之后还是两座");
        core.Find("small1")!.Position.ShouldBe(new Vector2(10f, 10f));
        core.Find("small1")!.Cells.ShouldBeEmpty("自由侧没有格");
        core.Events.Where<BuildingMoved>().Single().To.ShouldBe(new Vector2(10f, 10f));    }

    [Fact]
    public void 自由放置_旋转原地且角度量化到整数度_撞邻居回读数TooClose()
    {
        var core = FreeEditor();
        core.Commit("small", new Vector2(10f, 10f), 0, "small1");
        core.Commit("small", new Vector2(13f, 10f), 0, "small2");

        var rotated = core.Rotate("small1", 30);
        var tooClose = core.Move("small1", new Vector2(12f, 10f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        rotated.Ok.ShouldBeTrue();
        core.Find("small1")!.Orientation.ShouldBe(30, "30 已在 15° 的档上（D13）");
        core.Find("small1")!.Position.ShouldBe(new Vector2(10f, 10f), "原地");
        core.Events.Where<BuildingRotated>().Single().Cells.ShouldBeEmpty("自由侧没有格");
        tooClose.Ok.ShouldBeFalse();
        tooClose.Reason.ShouldBe(PlacementReason.TooClose, "1 < 半径和 2，撞的是别人那座");    }

    // ---- 确定性 -----------------------------------------------------------------

    [Fact]
    public void 同种子两遍逐字全等_编辑动词也逐字稳()
    {
        static string Run()
        {
            var core = Editor();
            core.Commit("keep", new Vector2(0.2f, 0.2f), GridOrientation.R90, "keep1");
            core.Commit("peg", new Vector2(6.2f, 6.2f), 0, "peg1");
            core.Move("keep1", new Vector2(3.2f, 3.2f));
            core.Rotate("keep1", GridOrientation.M180);
            core.Move("keep1", new Vector2(8.2f, 1.2f), GridOrientation.R270);
            core.Move("keep1", new Vector2(1.2f, 1.2f), GridOrientation.R90);
            core.Move("keep1", new Vector2(6.2f, 6.2f));
            return core.DebugSnapshotSegment() + core.Events.TailText(99);
        }

        Run().ShouldBe(Run());
    }

    // ---- 工具 -------------------------------------------------------------------

    /// <summary>blade = L 形，3 高 2 宽：八个方向落在八组不同的格上（D11 的编辑器侧验证要用它）。</summary>
    private static CellFootprint Blade => CellFootprint.FromCells(new[]
    {
        new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1), new GridCell(0, 2),
    });

    private static Vector2 Center(GridCell cell) => GridPlacementRule.CenterOf(cell, CellSize);

    private static ConstructionCore Editor(long buildDurationTicks = 0, IConstructionHost? host = null)
        => new(new GridPlacementRule(CellSize, -4, -4, 12, 12, host), new[]
        {
            new Blueprint("keep", 100, CellFootprint.FromCells(new[]
            {
                new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
            }), buildDurationTicks),
            new Blueprint("peg", 20, CellFootprint.FromCells(new[] { new GridCell(0, 0) })),
            new Blueprint("tower", 100, CellFootprint.FromCells(new[]
            {
                new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1), new GridCell(1, 1),
            })),
            new Blueprint("blade", 100, Blade),
        });

    private static ConstructionCore FreeEditor()
        => new(new FreePlacementRule(), new[]
        {
            new Blueprint("small", 100, new RadiusFootprint(1)),
            new Blueprint("big", 100, new RadiusFootprint(3)),
        });
}
