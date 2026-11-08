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
/// 区域蓝图（布局：捕获 / 放置 / 旋转镜像）的契约测试 —— <b>零引擎依赖</b>（spec 3 节第 2 条）。
///
/// 这里守的是 D23 那条分界与它周身的几处硬东西：<b>捕获只读、放置全有或全无</b>、
/// 文本自包含（一条 Journal 记录能逐字重放，ADR 0007）、0 成员的两种待遇
/// （捕获不算错、放置当场抛）、以及「旋转 / 镜像是纯函数」——
/// 最后一条以前只能靠出图看整块区域转没转对，现在是离线可跑的。
///
/// 断言强度只比检查点快照与逐成员裁决表（状态级），事件流读条数与字段、不比逐字（「断言比快照」）。
/// </summary>
public class ConstructionLayoutTests
{
    private const double CellSize = 1.0;

    // ---- Capture → PlaceLayout 往返（ADR 0007 的那条 Consequences）---------------

    [Fact]
    public void 捕获再放置_蓝图相对位置与方向逐项一致_id不同是预期()
    {
        var source = World();
        source.Commit("keep", Center(new GridCell(0, 0)), GridOrientation.R90, "a");
        source.Commit("peg", Center(new GridCell(4, 0)), 0, "b");
        source.Commit("tower", Center(new GridCell(6, 0)), GridOrientation.M180, "c");

        var text = source.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(7, 1)));
        var target = World();
        target.PlaceLayout(text, Center(new GridCell(0, 4)), 0);

        var original = Shape(source, new GridCell(0, 0));
        var copy = Shape(target, new GridCell(0, 4));
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        text.ShouldBe("v1;capture-c0-r0;keep,0,0,1;peg,4,0,0;tower,6,0,6", "捕获是纯函数：成员按枚举序、偏移相对成员自己那一堆的最小角、方向原样带着" +
            "（tower 当初是 M180 落的，0~3 旋转 / 4~7 先镜像再旋转的编码里 M180 = 6）");
        copy.ShouldBe(original, "蓝图 / 相对位置 / 方向逐项一致");
        target.Ids.ShouldNotBe(source.Ids, "id 不同是预期：号是模块那一处发的");
        target.OccupiedCount.ShouldBe(source.OccupiedCount);
        target.Count.ShouldBe(3);    }

    [Fact]
    public void 捕获_同一区域两次捕获逐字同文()
    {
        var core = World();
        core.Commit("peg", Center(new GridCell(1, 0)), 0, "a");
        var first = core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(2, 1)));
        var again = core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(2, 1)));

        again.ShouldBe(first);
    }

    [Fact]
    public void 捕获_只读_不改世界不发事件()
    {
        var core = World();
        core.Commit("peg", Center(new GridCell(1, 0)), 0, "a");
        var snapshot = core.DebugSnapshotSegment();
        var events = core.Events.Total;
        var occupied = core.OccupiedCount;

        for (var i = 0; i < 3; i++)
        {
            core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(2, 1)));
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.DebugSnapshotSegment().ShouldBe(snapshot);
        core.Events.Total.ShouldBe(events, "捕获是 call 类那一侧（ADR 0007）");
        core.OccupiedCount.ShouldBe(occupied);    }

    [Fact]
    public void 捕获_空区域回0成员文本_那不算错()
    {
        var core = World();

        var text = core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(2, 1)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        text.ShouldBe("v1;capture-c0-r0;", "末尾那个分号就是「没有成员」");
        Layout.Parse(text).Members.ShouldBeEmpty("0 成员是合法数据（ADR 0007）");
        Layout.Parse(text).Serialize().ShouldBe(text, "往返原样");    }

    [Fact]
    public void 放置0成员布局当场抛_作者错误那一档()
    {
        var core = World();
        var text = Layout.Empty("nothing").Serialize();
        var snapshot = core.DebugSnapshotSegment();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<InvalidOperationException>(
            () => core.PlaceLayout(text, Center(new GridCell(0, 0)), 0),
            "捕获空区域是对的，放置它不是（D7、ADR 0007）");
        core.DebugSnapshotSegment().ShouldBe(snapshot, "抛之前没写任何一列");
        core.Events.Total.ShouldBe(0);    }

    [Fact]
    public void 捕获_跨出区域的建筑照原样带出整份占地()
    {
        var core = World();
        core.Commit("tower", Center(new GridCell(1, 0)), 0, "t1"); // 占 (1,0)(2,0)(1,1)(2,1)
        var text = core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(1, 0)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Layout.Parse(text).Members.Count.ShouldBe(1, "判据是锚点格在区域里；截一半的占地放下去是一座缺着角的建筑");
        var target = World();
        target.PlaceLayout(text, Center(new GridCell(0, 0)), 0);
        target.Find("b1")!.Cells.Count.ShouldBe(4, "整份占地都在");    }

    // ---- 放置：全有或全无（D23）--------------------------------------------------

    [Fact]
    public void 布局成功_成员逐个Placed与LayoutPlaced并存()
    {
        var core = World();
        core.Commit("peg", Center(new GridCell(0, 0)), 0, "a");
        core.Commit("peg", Center(new GridCell(1, 0)), 0, "b");
        var text = core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(1, 0)));
        core.Events.ClearKept();

        core.PlaceLayout(text, Center(new GridCell(5, 5)), 0);

        var wires = core.Events.All.Select(e => e.Wire).ToList();
        var summary = core.Events.Where<LayoutPlaced>().Single();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        // 按 wire 过滤看 D26 那条并存：工期 0 的 Completed 是 02 定的同步连发（D16），
        // 本来就在流里，与布局这一族无关 —— 逐字比整条流的话，改动 D16 就会把这个用例带红。
        wires.Where(w => w != "completed").ShouldBe(new[] { "placed", "placed", "layout_placed" }, Case.Sensitive, "成员逐个 Placed 在前，汇总在后（D26 的并存那一档）");
        wires.Count(w => w == "completed").ShouldBe(2, "两座 peg 都是工期 0：Placed 之后当场连发 Completed，顺序契约 Placed 先（D16）");
        summary.Members.ShouldBe("b1,b2", "汇总带成功名单：上面两座是显式给的号 a / b，自动发号那一份从 b1 起走，" +
            "撞名才往后挪（发号那一处的规则）");
        summary.LayoutName.ShouldBe("capture-c0-r0");
        summary.Subject.ShouldBe("capture-c0-r0", "事件 Subject 是布局名");
        summary.Detail.ShouldBe("capture-c0-r0 members=b1,b2");
        core.Count.ShouldBe(4);    }

    [Fact]
    public void 布局整体被拒_一条LayoutRejected_成员零Placed_一寸不动()
    {
        var core = World();
        var text = new Layout("t", new[]
        {
            new LayoutMember("peg", new GridCell(0, 0)),
            new LayoutMember("peg", new GridCell(4, 0)),
        }).Serialize();
        core.Commit("peg", Center(new GridCell(4, 0)), 0, "blocker");
        core.Events.ClearKept();
        var snapshot = core.DebugSnapshotSegment();
        var occupied = core.OccupiedCount;

        var result = core.PlaceLayout(text, Center(new GridCell(0, 0)), 0);

        var rejected = core.Events.Where<LayoutRejected>().Single();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.PlacedCount.ShouldBe(0, "全有或全无：一个判不过就一个都不落");
        core.Count.ShouldBe(1, "那 0 格也放不下去（与 blocker 压着）");
        core.Events.Count<LayoutRejected>().ShouldBe(1);
        core.Events.Count<BuildingPlaced>().ShouldBe(0, "成员零事件（D26）");
        core.Events.Count<BatchPlaced>().ShouldBe(0, "布局不是批量手势，不发那条汇总");
        rejected.Detail.ShouldBe("failures=4.5,0.5:peg:Occupied", "失败成员 + 原因，逐项认得出是哪座（D26：只列判不过的；上面那 0 格判成了，" +
            "只是跟着整批没落 —— 全有或全无，报成失败反而是谎报）");
        core.DebugSnapshotSegment().ShouldBe(snapshot, "拒绝一寸不动（D23）");
        core.OccupiedCount.ShouldBe(occupied);    }

    [Fact]
    public void 布局_成员之间互相压着也整体拒绝_不抛也不落()
    {
        // 两个成员落在同一格：轮到第二个的时候第一个还没落，规则的占用索引看不见它 ——
        // 那一层比对只有核心做得了（D38）。不比对的话两个都判成，落座那一步当场抛，
        // 而抛出来时前面那个已经落下去了，正好违反「一寸不动」。
        var core = World();
        var text = new Layout("twin", new[]
        {
            new LayoutMember("peg", new GridCell(0, 0)),
            new LayoutMember("peg", new GridCell(0, 0)),
        }).Serialize();
        var snapshot = core.DebugSnapshotSegment();

        var result = core.PlaceLayout(text, Center(new GridCell(2, 2)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.PlacedCount.ShouldBe(0);
        result.Failures.Count.ShouldBe(1);
        result.Failures.Single().Reason.ShouldBe(PlacementReason.Occupied);
        result.Members[0].Ok.ShouldBeTrue("第一个判成了（表里看得见谁过了）");
        core.Count.ShouldBe(0);
        core.DebugSnapshotSegment().ShouldBe(snapshot);
        core.Events.Count<LayoutRejected>().ShouldBe(1);    }

    [Fact]
    public void 布局_成员蓝图不在册_逐成员UnknownBlueprint_整体拒绝()
    {
        var core = World();
        var text = new Layout("ghosty", new[]
        {
            new LayoutMember("peg", new GridCell(0, 0)),
            new LayoutMember("nope", new GridCell(1, 0)),
        }).Serialize();

        var result = core.PlaceLayout(text, Center(new GridCell(0, 0)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        result.PlacedCount.ShouldBe(0);
        result.Members[0].Ok.ShouldBeTrue();
        result.Failures.Single().Reason.ShouldBe(PlacementReason.UnknownBlueprint);
        core.Events.Where<LayoutRejected>().Single().Detail.ShouldContain("1.5,0.5:nope:UnknownBlueprint");
        core.Count.ShouldBe(0);    }

    [Fact]
    public void 布局_只读查询与落建同一条流水线_逐成员表逐字相同()
    {
        var text = new Layout("t", new[]
        {
            new LayoutMember("peg", new GridCell(0, 0)),
            new LayoutMember("peg", new GridCell(1, 0)),
        }).Serialize();

        var query = World();
        var before = query.CheckLayout(text, Center(new GridCell(0, 0)), 0);
        var events = query.Events.Total;
        var snapshot = query.DebugSnapshotSegment();

        var commit = World();
        var landed = commit.PlaceLayout(text, Center(new GridCell(0, 0)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        BatchText.Table(landed.Members).ShouldBe(LayoutText.Table(before.Members!), "预览 = 结果（D25、D29）");
        before.Ok.ShouldBeTrue();
        query.Events.Total.ShouldBe(events);
        query.DebugSnapshotSegment().ShouldBe(snapshot);
        query.OccupiedCount.ShouldBe(0);    }

    [Fact]
    public void 布局放置_落座完自动重算悬停_幽灵从红变绿()
    {
        var core = World();
        var text = "v1;t;peg,0,0,0;peg,1,0,0";
        core.Hover.Push(new GestureSpec("", Center(new GridCell(2, 2)), 0, text, GestureShape.Layout));
        core.Hover.Last!.Ok.ShouldBeTrue();

        core.PlaceLayout(text, Center(new GridCell(2, 2)), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Hover.Last!.Ok.ShouldBeFalse("占用变了要重算");
        core.Hover.Last.Members!.Count(m => !m.Ok).ShouldBe(2);    }

    // ---- 旋转与镜像：静态纯函数（D23、ADR 0007）----------------------------------

    [Fact]
    public void 布局文本_八个方向对不对称成员逐一可区分()
    {
        var layout = new Layout("bent", new[]
        {
            new LayoutMember("peg", new GridCell(0, 0)),
            new LayoutMember("peg", new GridCell(1, 0)),
            new LayoutMember("peg", new GridCell(1, 1)),
        });

        var seen = new List<string>();
        for (var i = 0; i < GridOrientation.Count; i++)
        {
            seen.Add(GridOrientation.TransformLayout(layout, i).Serialize());
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        seen.Distinct().Count().ShouldBe(GridOrientation.Count, "八个方向里有撞在一起的，D4 变换不成立 —— " + string.Join(" | ", seen));
        GridOrientation.TransformLayout(layout, GridOrientation.R0).Serialize().ShouldBe(layout.Serialize(), "rot0 是恒等（走同一份代码，不是特判）");    }

    [Fact]
    public void 布局变换_是纯函数_同输入同输出无副作用()
    {
        var core = World();
        core.Commit("peg", Center(new GridCell(0, 0)), 0, "a");
        var text = core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(1, 1)));
        var snapshot = core.DebugSnapshotSegment();
        var events = core.Events.Total;
        var layout = Layout.Parse(text);

        var first = GridOrientation.TransformLayout(layout, GridOrientation.M90).Serialize();
        var again = GridOrientation.TransformLayout(Layout.Parse(text), GridOrientation.M90).Serialize();
        var after = GridOrientation.TransformLayout(layout, GridOrientation.M90).Serialize();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        again.ShouldBe(first);
        after.ShouldBe(first, "拿同一份数据再转一次还是那个结果（没就地改）");
        layout.Serialize().ShouldBe(text, "输入那一份没被动过（数据进数据出）");
        core.DebugSnapshotSegment().ShouldBe(snapshot, "变换不碰世界");
        core.Events.Total.ShouldBe(events);    }

    [Fact]
    public void 布局变换_成员自身方向跟着复合_镜像两次回到原方向()
    {
        var layout = new Layout("d", new[]
        {
            new LayoutMember("peg", new GridCell(0, 0), GridOrientation.R90),
        });

        var mirrored = GridOrientation.TransformLayout(layout, GridOrientation.M0);
        var back = GridOrientation.TransformLayout(mirrored, GridOrientation.M0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        // 成员原方向 R90，布局整体方向 M0（先镜像再转 0°）：复合出来的是 M270，不是 R270 ——
        // 镜像不是旋转，0~3 只装得下纯旋转，整个八值编码里没有「纯镜像」那一档（D37）。
        mirrored.Members[0].Orientation.ShouldBe(GridOrientation.M270, "成员新方向 = 整体变换 ∘ 成员原方向（复合律，D37）");
        back.Members[0].Orientation.ShouldBe(GridOrientation.R90, "镜像是对合的：镜像两次回到原方向（Compose(M0, M0) = R0，与下面那条群律同源）");
        back.Members[0].Offset.ShouldBe(layout.Members[0].Offset);    }

    [Fact]
    public void 方向复合_是D4的群_恒等与结合律都成立()
    {
        // 镜像是与旋转不可交换的（S·R = R⁻¹·S），复合法则一写错就只在某些组合上露馅；
        // 8³ = 512 个三元组全过一遍，那道防线就跑不掉了。
        for (var inner = 0; inner < GridOrientation.Count; inner++)
        {
            for (var outer = 0; outer < GridOrientation.Count; outer++)
            {
                GridOrientation.Compose(inner, outer).ShouldBeInRange(0, GridOrientation.Count - 1, $"{GridOrientation.Label(inner)} ∘ {GridOrientation.Label(outer)} 越界了");
                for (var third = 0; third < GridOrientation.Count; third++)
                {
                    GridOrientation.Compose(GridOrientation.Compose(inner, outer), third).ShouldBe(GridOrientation.Compose(inner, GridOrientation.Compose(outer, third)), $"结合律在 {GridOrientation.Label(inner)}/{GridOrientation.Label(outer)}/" +
                        $"{GridOrientation.Label(third)} 上破了");
                }
            }
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        for (var i = 0; i < GridOrientation.Count; i++)
        {
            GridOrientation.Compose(GridOrientation.R0, i).ShouldBe(i, "左边恒等");
            GridOrientation.Compose(i, GridOrientation.R0).ShouldBe(i, "右边恒等");
            GridOrientation.Compose(GridOrientation.M0, GridOrientation.M0).ShouldBe(GridOrientation.R0, "镜像两次是恒等");
            GridOrientation.Compose(GridOrientation.R90, GridOrientation.R90).ShouldBe(GridOrientation.R180, "转两次 90° 就是 180°");
        }

        Should.Throw<ArgumentOutOfRangeException>(
            () => GridOrientation.Compose(0, GridOrientation.Count), "越界当场抛（D7）");    }

    [Fact]
    public void 布局整体方向_偏移与成员方向一起转()
    {
        var core = World();
        var text = new Layout("t", new[]
        {
            new LayoutMember("peg", new GridCell(0, 0), GridOrientation.R0),
            new LayoutMember("peg", new GridCell(1, 0), GridOrientation.R0),
        }).Serialize();

        core.PlaceLayout(text, Center(new GridCell(2, 2)), GridOrientation.R90);

        // 盒是 2×1：转 90° 后 (0,0) → (0,0)、(1,0) → (0,1)，盒变 1×2。
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Find("b1")!.Anchor.ShouldBe(new GridCell(2, 2));
        core.Find("b2")!.Anchor.ShouldBe(new GridCell(2, 3));
        core.Find("b1")!.Orientation.ShouldBe(GridOrientation.R90, "成员自身方向跟着整体方向复合（D23）");    }

    // ---- 文本本身（自包含、格式、字段规矩）--------------------------------------

    [Fact]
    public void 布局文本_序列化反序列化往返逐字相同()
    {
        // 名字带中文（布局名要进 Capture 那一栏与事件 Subject，编码问题当场就露），
        // 但不许带 ; , | —— 那三个是分隔符、全表一份没有转义档，构造时就该炸
        // （另有一条用例专测那三个字符）。
        var layout = new Layout("北门塔一二", new[]
        {
            new LayoutMember("keep", new GridCell(0, 0), GridOrientation.M180),
            new LayoutMember("peg", new GridCell(2, 1), GridOrientation.M0),
        });

        var text = layout.Serialize();
        var parsed = Layout.Parse(text);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        parsed.Serialize().ShouldBe(text);
        // 逐项比，不比整份 Layout 的 record 相等：成员表那一列是 List，
        // record 相等比到它就成比引用了 —— 两份内容一样的布局也会判不相等。
        parsed.Name.ShouldBe(layout.Name, "名字逐字带回");
        parsed.Members.Count.ShouldBe(layout.Members.Count);
        for (var i = 0; i < layout.Members.Count; i++)
        {
            parsed.Members[i].ShouldBe(layout.Members[i], $"第 {i + 1} 个成员逐项相同（蓝图 / 偏移 / 方向）");
        }

        text.ShouldNotContain("|", Case.Sensitive, "竖线是动词参数的分隔符，布局文本不许带");
        text.ShouldNotContain("\n", Case.Sensitive, "一行文本：Journal 记录是一行");    }

    [Fact]
    public void 布局文本_读不懂当场抛_不猜()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<FormatException>(() => Layout.Parse(""), "空文本");
        Should.Throw<FormatException>(() => Layout.Parse("v2;t;peg,0,0,0"), "格式版本不对");
        Should.Throw<FormatException>(() => Layout.Parse("v1;;peg,0,0,0"), "名字空");
        Should.Throw<FormatException>(() => Layout.Parse("v1;t;peg,0,0"), "字段数不对");
        Should.Throw<FormatException>(() => Layout.Parse("v1;t;peg,a,0,0"), "偏移读不出");
        Should.Throw<FormatException>(() => Layout.Parse("v1;t;peg,0.5,0,0"), "偏移不是整数格");
        Should.Throw<FormatException>(() => Layout.Parse("v1;t;;peg,0,0,0"), "中间空段");    }

    [Fact]
    public void 布局文本_字段里混进分隔符当场抛_在构造那一侧就炸()
    {
        // 竖线那一条是 02 留下的已知限制的直系后代：切分就是竖线、全表一份、没有转义档，
        // 布局文本要原样进 Journal 当一整格参数（ADR 0007）。判在构造时，炸在写蓝图表那一刻，
        // 而不是录完一局重放时。
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<ArgumentException>(() => new LayoutMember("wa|ll", new GridCell(0, 0)));
        Should.Throw<ArgumentException>(() => new LayoutMember("a,b", new GridCell(0, 0)));
        Should.Throw<ArgumentException>(() => new Layout("a;b", Array.Empty<LayoutMember>()));
        Should.Throw<ArgumentException>(() => new LayoutMember("a\tb", new GridCell(0, 0)));    }

    // ---- 自由放置那一侧 -----------------------------------------------------------

    [Fact]
    public void 自由放置_布局_不转照旧可放_要转当场抛()
    {
        var core = Free();
        core.Commit("small", new Vector2(0f, 0f), 0, "a");
        core.Commit("small", new Vector2(3f, 0f), 0, "b");
        var text = core.CaptureLayout(new Vector2(0f, 0f), new Vector2(3f, 0f));

        var laid = core.PlaceLayout(text, new Vector2(0f, 9f), 0);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        laid.PlacedCount.ShouldBe(2, "不转（0°）照旧可用");
        core.Find("b1")!.Position.ShouldBe(new Vector2(0f, 9f));
        core.Find("b2")!.Position.ShouldBe(new Vector2(3f, 9f), "成员偏移是格点上的整数（BatchStep）");
        Should.Throw<NotSupportedException>(
            () => core.PlaceLayout(text, new Vector2(0f, 18f), 15),
            "自由侧还不接布局整体方向：抛「这一侧不接」而不是回一个假的失败读数（口径 2）");    }

    // ---- 确定性 -----------------------------------------------------------------

    [Fact]
    public void 同种子两遍逐字全等_布局这一族也逐字稳()
    {
        static string Run()
        {
            var core = World();
            core.Commit("keep", Center(new GridCell(0, 0)), GridOrientation.M90, "a");
            core.Commit("peg", Center(new GridCell(4, 0)), 0, "b");
            var text = core.CaptureLayout(Center(new GridCell(0, 0)), Center(new GridCell(5, 1)));
            var placed = core.PlaceLayout(text, Center(new GridCell(0, 4)), 0);
            var rejected = core.PlaceLayout(text, Center(new GridCell(0, 4)), 0);
            return core.DebugSnapshotSegment()
                + core.Events.TailText(99)
                + BatchText.LayoutLine("capture-c0-r0", placed)
                + BatchText.LayoutLine("capture-c0-r0", rejected);
        }

        Run().ShouldBe(Run());
    }

    // ---- 工具 -------------------------------------------------------------------

    /// <summary>
    /// 「相对锚点的 (蓝图, 格偏移, 方向)」表 —— 布局往返比的是这一份，不是绝对坐标。
    /// 捕获的锚点是区域最小角那一格，放置的锚点由调用方给，还原时要给回同一个。
    /// </summary>
    private static List<(string Blueprint, GridCell Offset, int Orientation)> Shape(
        ConstructionCore core, GridCell anchor)
        => core.Ids.Select(id =>
        {
            var b = core.Find(id)!;
            return (b.BlueprintId, b.Anchor!.Value - anchor, b.Orientation);
        }).ToList();

    private static Vector2 Center(GridCell cell) => GridPlacementRule.CenterOf(cell, CellSize);

    private static ConstructionCore World()
        => new(new GridPlacementRule(CellSize, -4, -4, 12, 12), new[]
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

    private static ConstructionCore Free()
        => new(new FreePlacementRule(batchStep: 3), new[]
        {
            new Blueprint("small", 100, new RadiusFootprint(1)),
        });
}
