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
/// 建造模块动词表的契约测试 —— <b>一份表、两处问</b>（D34，口径照 AbilityVerbs）。
///
/// 这里守的是三条分界：作者错误当场抛、状态性拒绝回读数、参数次序与缺省值只有一处。
/// 前两条分界一旦混了，重放端就会多出一类「录进去的东西重放不了」；第三条一旦漂，
/// 录制包装、桥、CLI 三边就会各接一格。
/// </summary>
public class ConstructionVerbsTests
{
    [Fact]
    public void 未知动词当场抛_不在重放里静默少打一笔()
    {
        var sink = CoreSink();

        Should.Throw<InvalidOperationException>(() => ConstructionVerbs.Dispatch(sink, "RaiseBuilding", "keep|1|2"));
    }

    [Fact]
    public void 这一侧不接的动词当场抛_不拿false表达没这一格()
    {
        var sink = new ThinSink(new ConstructionCore(
            new GridPlacementRule(1, -2, -2, 9, 9), Blueprints()), drop: ConstructionVerbs.Remove);

        Should.Throw<NotSupportedException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Remove, "b1"));
    }

    [Fact]
    public void 必填参数缺了当场抛_不兜0()
    {
        var sink = CoreSink();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "|1|2"), "缺蓝图 id");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep||2"), "缺 x");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1"), "缺 y");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Remove, ""), "缺建筑 id");    }

    [Fact]
    public void 数值解析不出当场抛()
    {
        var sink = CoreSink();

        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|left|2"));
    }

    [Fact]
    public void 去向读不懂当场抛_枚举是封闭的()
    {
        var sink = CoreSink();

        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Remove, "b1|incinerate"));
    }

    [Fact]
    public void 缺省段_方向与建筑id与去向都按表里的缺省走()
    {
        var call = ConstructionVerbs.Parse(ConstructionVerbs.Place, "keep|1.5|2.5");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        call.Orientation.ShouldBe(0);
        call.BuildingId.ShouldBeNull();
        ConstructionVerbs.Parse(ConstructionVerbs.Place, "keep|1.5|2.5|0|-").BuildingId.ShouldBeNull("竖线留着但写 - 也算「没给」");
        ConstructionVerbs.Parse(ConstructionVerbs.Remove, "b1|-").Cause.ShouldBe(RemovalCause.Dismantle);
        ConstructionVerbs.Parse(ConstructionVerbs.Remove, "b1|cancel").Cause.ShouldBe(RemovalCause.Cancel);    }

    [Fact]
    public void 注入类回读数_不抛_重放要比它()
    {
        var sink = CoreSink();

        var ok = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5");
        var clash = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|2.5|2.5|0");
        var unknown = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "ghost|1.5|2.5");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ok.Ok.ShouldBeTrue();
        ok.Text.ShouldContain("id=b1");
        clash.Ok.ShouldBeFalse();
        clash.Text.ShouldContain("reason=Occupied");
        unknown.Text.ShouldContain("reason=UnknownBlueprint");    }

    [Fact]
    public void 拆除走inject_腾占用后同格再放得进()
    {
        var sink = CoreSink();
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5");

        var removed = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Remove, "b1|cancel");
        var again = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        removed.Ok.ShouldBeTrue();
        again.Ok.ShouldBeTrue();
        sink.Core!.Events.Where<BuildingRemoved>().Single().Cause.ShouldBe(RemovalCause.Cancel);    }

    [Fact]
    public void call类只读_BuildingAt命中回id未命中回空()
    {
        var sink = CoreSink();
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5|0|keep1");

        var hit = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.BuildingAtVerb, "1.5|2.5");
        var miss = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.BuildingAtVerb, "8|8");
        var before = sink.Core!.DebugSnapshotSegment();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hit.Text.ShouldBe("keep1");
        miss.Text.ShouldBe(string.Empty);
        sink.Core.DebugSnapshotSegment().ShouldBe(before, "call 类没有副作用");    }

    [Fact]
    public void MoveBuilding_方向可省_省掉就是保持当前方向_不是转成rot0()
    {
        var sink = CoreSink();
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5|2|keep1");

        var kept = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Move, "keep1|5.5|5.5");
        // 读数当场取：册里那一行是同一个对象，下一句挪完之后读到的就是挪之后的状态
        var afterKept = (sink.Core!.Find("keep1")!.Orientation, sink.Core.Find("keep1")!.Anchor);
        var turned = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Move, "keep1|7.5|7.5|0");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ConstructionVerbs.Parse(ConstructionVerbs.Move, "keep1|1|1").Direction.ShouldBeNull("方向那一格可省：省略与 - 都算「保持」");
        ConstructionVerbs.Parse(ConstructionVerbs.Move, "keep1|1|1|3").Direction.ShouldBe(3);
        kept.Ok.ShouldBeTrue();
        afterKept.Item1.ShouldBe(2, "没给方向就不许动方向");
        afterKept.Item2.ShouldBe(new GridCell(5, 5), "位置照给，只是方向不动");
        turned.Ok.ShouldBeTrue();
        sink.Core!.Find("keep1")!.Orientation.ShouldBe(0, "给了 0 就是「转成 rot0」");
        sink.Core.Find("keep1")!.Anchor.ShouldBe(new GridCell(7, 7));    }

    [Fact]
    public void Move与Rotate的必填段缺了当场抛_不兜默认值()
    {
        var sink = CoreSink();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Move, "|1|1"), "缺建筑 id");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Move, "b1|1"), "缺 y");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Rotate, "b1"), "Rotate 的方向就是这一条动词的全部");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Rotate, "b1|turn-left"), "方向读不懂");    }

    [Fact]
    public void Move与Rotate是状态性拒绝_回读数不抛()
    {
        var sink = CoreSink();
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5|0|keep1");
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|5.5|5.5|0|keep2");

        var clash = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Move, "keep1|5.5|5.5");
        var gone = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Move, "ghost|5.5|5.5");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        clash.Ok.ShouldBeFalse();
        clash.Text.ShouldContain("reason=Occupied");
        gone.Ok.ShouldBeFalse();
        gone.Text.ShouldContain("reason=UnknownId");    }

    [Fact]
    public void Move与Rotate是写路径_进录的名单里有它们而BuildingAt不进()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ConstructionVerbs.InjectNames.ShouldContain(ConstructionVerbs.Move);
        ConstructionVerbs.InjectNames.ShouldContain(ConstructionVerbs.Rotate);
        ConstructionVerbs.InjectNames.ShouldNotContain(ConstructionVerbs.BuildingAtVerb, "只读查询不进 Journal（D22、「写路径全录」）：漏录与多录都会让重放分叉");    }

    [Fact]
    public void call类_BuildingAt重复调用没有副作用()
    {
        var sink = CoreSink();
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5|0|keep1");
        var before = sink.Core!.DebugSnapshotSegment();
        var events = sink.Core.Events.Total;

        var hits = new List<string>();
        var misses = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            hits.Add(ConstructionVerbs.Dispatch(sink, ConstructionVerbs.BuildingAtVerb, "1.5|2.5").Text);
            misses.Add(ConstructionVerbs.Dispatch(sink, ConstructionVerbs.BuildingAtVerb, "8|8").Text);
        }

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hits.ShouldAllBe(_e => _e == "keep1", "拾取走逻辑真值：同一处反复读是同一个 id");
        misses.ShouldAllBe(_e => _e == string.Empty);
        sink.Core.DebugSnapshotSegment().ShouldBe(before, "重复读不改状态");
        sink.Core.Events.Total.ShouldBe(events, "只读查询不发事件");    }

    [Fact]
    public void call类_Check回裁决那一行的读数()
    {
        var sink = CoreSink();
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1.5|2.5");

        var check = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Check, "keep|2.5|2.5");

        check.Text.ShouldContain("no reason=Occupied");
        check.Text.ShouldContain("cells=");
    }

    [Fact]
    public void call类_GizmoParams回的是模块那份只读查询()
    {
        var sink = CoreSink();

        var gizmo = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Gizmo, "");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        gizmo.Text.ShouldContain("cellSize=1");
        gizmo.Text.ShouldContain("maxCol=9");    }

    [Fact]
    public void 动词名全表与inject名单都是这份给的_不各自抄一份()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ConstructionVerbs.Names.ShouldBe(new[]
        {
            "PlaceBuilding", "RemoveBuilding", "MoveBuilding", "RotateBuilding", "DamageBuilding",
            "RepairBuilding", "SetHealthBuilding", "PauseBuild", "ResumeBuild", "RegisterFixture",
            "BuildingAt", "CheckPlacement", "GizmoParams",
            "PlaceLine", "PlaceRect", "PlaceLayout", "RemoveRect",
            "CheckLine", "CheckRect", "CheckLayout", "CaptureLayout",
        });
        ConstructionVerbs.InjectNames.ShouldBe(new[]
            {
                "PlaceBuilding", "RemoveBuilding", "MoveBuilding", "RotateBuilding", "DamageBuilding",
                "RepairBuilding", "SetHealthBuilding", "PauseBuild", "ResumeBuild",
                "PlaceLine", "PlaceRect", "PlaceLayout", "RemoveRect",
            }, Case.Sensitive, "call 类不进 Journal（「写路径全录」、「写路径进 Journal」），inject 名单要能一句话说清");    }

    [Fact]
    public void 场地建筑登记是写路径但不进录_理由钉在InjectNames上()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ConstructionVerbs.Names.ShouldContain(_e => Equals(_e, ConstructionVerbs.RegisterFixtureVerb), "它得能被剧本与直达口调到（D34：一份表、两处问）");
        ConstructionVerbs.InjectNames.ShouldNotContain(ConstructionVerbs.RegisterFixtureVerb, "录制端与重放端都跑同一个 SeedWorld，录一遍会在重放端重复登记当场抛重名");    }

    [Fact]
    public void 场地建筑登记_建筑id缺了当场抛_不兜自动发号()
    {
        var sink = CoreSink();

        Should.Throw<FormatException>(
            () => ConstructionVerbs.Dispatch(sink, ConstructionVerbs.RegisterFixtureVerb, "keep|0.1|0.1|0|"),
            "场地建筑的 id 是关卡摆位的一部分，兜一个自动号等于让它换个名字进册（「桥只转发」）");
    }

    [Fact]
    public void 场地建筑登记_从动词表走一遍_册与占用都落在那一格()
    {
        var sink = CoreSink();

        var outcome = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.RegisterFixtureVerb,
            "keep|0.1|0.1|0|ruin|-|-");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        outcome.Ok.ShouldBeTrue(outcome.Text);
        sink.Core!.Find("ruin").ShouldNotBeNull();
        sink.Core.Count.ShouldBe(1);
        sink.Core.Check("keep", new Vector2(1.1f, 0.1f)).Reason.ShouldBe(PlacementReason.Occupied, "场地建筑占着位：后来的玩家建筑被模块正常挡住");    }

    [Fact]
    public void 批量四条写路径全录_四条只读查询一条都不录()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        foreach (var verb in new[]
        {
            ConstructionVerbs.PlaceLineVerb, ConstructionVerbs.PlaceRectVerb,
            ConstructionVerbs.PlaceLayoutVerb, ConstructionVerbs.RemoveRectVerb,
        })
        {
            ConstructionVerbs.InjectNames.ShouldContain(_e => Equals(_e, verb), $"{verb} 是写路径（「写路径全录」、D24）");
        }

        foreach (var verb in new[]
        {
            ConstructionVerbs.CheckLineVerb, ConstructionVerbs.CheckRectVerb,
            ConstructionVerbs.CheckLayoutVerb, ConstructionVerbs.CaptureLayoutVerb,
        })
        {
            ConstructionVerbs.InjectNames.ShouldNotContain(verb, $"{verb} 是只读查询，不进 Journal（D25、ADR 0007）");
        }    }

    [Fact]
    public void 批量动词的必填段缺了当场抛_不兜0()
    {
        var sink = CoreSink();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.PlaceLineVerb, "|1|2|3|4"),
            "缺蓝图 id");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.PlaceLineVerb, "keep|1|2|3"),
            "缺终点 x");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.PlaceRectVerb, "keep|1|2|3"),
            "缺 maxx");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.RemoveRectVerb, "1|2|3"),
            "缺 maxy");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.PlaceLayoutVerb, "|1|2"),
            "缺布局文本");
        Should.Throw<FormatException>(() =>
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.CaptureLayoutVerb, "1|2|three|4"),
            "格坐标读不出");    }

    [Fact]
    public void 批量动词的参数次序与缺省值都照这份表()
    {
        var line = ConstructionVerbs.Parse(ConstructionVerbs.PlaceLineVerb, "keep|1|2|5|6");
        var rect = ConstructionVerbs.Parse(ConstructionVerbs.PlaceRectVerb, "keep|1|2|3|4|2");
        var layout = ConstructionVerbs.Parse(ConstructionVerbs.PlaceLayoutVerb, "v1;x;|3|4|3");
        var rectCall = ConstructionVerbs.Parse(ConstructionVerbs.RemoveRectVerb, "0|0|4|4");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        (line.BlueprintId, line.X, line.Y, line.X2, line.Y2, line.Orientation).ShouldBe(("keep", 1d, 2d, 5d, 6d, 0), "方向缺省 0");
        (rect.X, rect.Y, rect.X2, rect.Y2, rect.Orientation).ShouldBe((1d, 2d, 3d, 4d, 2));
        (layout.LayoutText, layout.X, layout.Y, layout.Orientation).ShouldBe(("v1;x;", 3d, 4d, 3));
        (rectCall.X, rectCall.Y, rectCall.X2, rectCall.Y2).ShouldBe((0d, 0d, 4d, 4d), "矩形那一族四格走同一条坐标形状");    }

    [Fact]
    public void 画线走inject_读数带成功计数与逐格表()
    {
        var sink = CoreSink();
        // 用 1x1 的图钉占住中间那一格：L 形自己就会压着邻居，一次只测「撞别人」这一种失败
        // 的话，枚举次序错一格也照样全绿。
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "peg|2.5|0.5|0|blocker");

        var line = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.PlaceLineVerb, "peg|0|0|4|0");
        var again = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.CheckLineVerb, "peg|0|0|4|0");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        line.Ok.ShouldBeTrue("4/5 也算打成了（逐格成败在 table= 里，D24）");
        line.Text.ShouldContain("placed=4");
        line.Text.ShouldContain("failed=1");
        line.Text.ShouldContain("2.5,0.5:no:Occupied", Case.Sensitive, "失败格带原因");
        again.Text.ShouldContain("members=5", Case.Sensitive, "只读查询回逐成员表");
        again.Text.ShouldContain("no:Occupied");    }

    [Fact]
    public void PlaceLayout自包含_一条记录就能重放()
    {
        var sink = CoreSink();
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.5|0.5|0|a");
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|2.5|0.5|0|b");
        var text = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.CaptureLayoutVerb, "0|0|3|1").Text;

        var placed = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.PlaceLayoutVerb, $"{text}|6|3|0");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        text.ShouldStartWith("v1;", Case.Sensitive, "捕获回的是自包含文本（ADR 0007）");
        placed.Ok.ShouldBeTrue();
        placed.Text.ShouldContain("placed=2");
        // 原型显式起名 a / b，所以批量发出来的头一个是 b1 —— 号段是模块那一处的，D34。
        sink.Core!.Find("b1")!.Anchor.ShouldBe(new GridCell(6, 3), "第一条记录说锚点在 (6,3)，两座都得落在那儿");
        sink.Core.Find("b2")!.Anchor.ShouldBe(new GridCell(8, 3));
        sink.Core.OccupiedCount.ShouldBe(12, "4 座 × 3 格");    }

    [Fact]
    public void 布局文本里不许有竖线_分隔符进字段当场抛()
    {
        // 竖线是动词参数的分隔符、全表一份、没有转义档。布局文本要原样进 Journal 当一整格参数，
        // 所以这一条限制判在布局构造那一侧（LayoutText），而不是等录完一局重放时才发现解析不出来。
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<ArgumentException>(() => new LayoutMember("wa|ll", new GridCell(0, 0)));
        Should.Throw<ArgumentException>(() => new LayoutMember("a;b", new GridCell(0, 0)));
        Should.Throw<ArgumentException>(() => new Layout("a,b", Array.Empty<LayoutMember>()));
        Should.Throw<ArgumentException>(() => new Layout("a\nb", Array.Empty<LayoutMember>()));    }

    [Fact]
    public void 解析是纯函数_不发东西不读世界()
    {
        var a = ConstructionVerbs.Parse(ConstructionVerbs.Place, "keep|1.5|2.5|3|shop");
        var b = ConstructionVerbs.Parse(ConstructionVerbs.Place, "keep|1.5|2.5|3|shop");

        a.ShouldBe(b);
    }

    private static IEnumerable<Blueprint> Blueprints()
    {
        yield return new Blueprint("keep", 100, CellFootprint.FromCells(new[]
        {
            new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
        }));
        // 1x1 的图钉：批量那几条要测「一排里撞了中间一个」，占地宽的蓝图自己就压着邻居，
        // 测出来的是另一回事。
        yield return new Blueprint("peg", 20, CellFootprint.FromCells(new[] { new GridCell(0, 0) }));
    }

    private static ThinSink CoreSink() => new(new ConstructionCore(new GridPlacementRule(1, -2, -2, 9, 9), Blueprints()));

    /// <summary>
    /// 薄适配器 —— 原型侧该写的那一份：声明接哪几条动词，每个动词一次调用，一个动词名都不写
    /// （D34 的分工）。
    /// </summary>
    private sealed class ThinSink : IConstructionVerbSink
    {
        private readonly IReadOnlySet<string> _drop;

        public ThinSink(ConstructionCore core, string? drop = null)
        {
            Core = core;
            _drop = drop is null
                ? new HashSet<string>(ConstructionVerbs.Names, StringComparer.Ordinal)
                : new HashSet<string>(ConstructionVerbs.Names.Where(n => n != drop), StringComparer.Ordinal);
        }

        public ConstructionCore? Core { get; }

        public string HostLabel => "契约测试宿主";

        public IReadOnlySet<string> SupportedVerbs => _drop;

        public WriteResult PlaceBuilding(string blueprintId, double x, double y, int orientation,
            string? buildingId, long? buildDurationTicks, string? payloadText)
        {
            // 薄适配器该做的就这些：把一段参数搬到模块那一次调用上，判定归模块。
            // 新建筑的 id 走拾取那条读数（显示即真相），不在这一侧另记一份。
            var v = Core!.Commit(blueprintId, new Vector2((float)x, (float)y), orientation, buildingId,
                buildDurationTicks);
            return v.Ok
                ? WriteResult.Done(Core.BuildingIdAt(x, y))
                : WriteResult.Fail(v.Reason);
        }

        public WriteResult RemoveBuilding(string buildingId, RemovalCause cause)
            => Core!.Remove(buildingId, cause);

        public WriteResult MoveBuilding(string buildingId, double x, double y, int? orientation)
            => Core!.Move(buildingId, new Vector2((float)x, (float)y), orientation);

        public WriteResult RotateBuilding(string buildingId, int orientation)
            => Core!.Rotate(buildingId, orientation);

        public WriteResult DamageBuilding(string buildingId, double amount) => Core!.Damage(buildingId, amount);

        public WriteResult RepairBuilding(string buildingId, double amount) => Core!.Repair(buildingId, amount);

        public WriteResult SetHealthBuilding(string buildingId, double hp) => Core!.SetHealth(buildingId, hp);

        public WriteResult PauseBuild(string buildingId) => Core!.PauseBuild(buildingId);

        public WriteResult ResumeBuild(string buildingId) => Core!.ResumeBuild(buildingId);

        // 场地建筑登记：同样只搬参数、只调一次那一个模块入口（D34 的薄适配器就长这样）。
        public WriteResult RegisterFixture(string blueprintId, double x, double y, int orientation,
            string buildingId, string? payloadText)
            => Core!.RegisterFixture(blueprintId, new Vector2((float)x, (float)y), buildingId, orientation);

        // 批量这一族：薄适配器只搬参数、只调一次。判定、枚举、逐成员表全在模块里
        // —— 适配器里出现第二个枚举就等于 D24 的「顺序单一真值」当场破掉。
        public BatchResult PlaceLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => Core!.PlaceLine(blueprintId, new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1),
                orientation);

        public BatchResult PlaceRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => Core!.PlaceRect(blueprintId, new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1),
                orientation);

        public BatchResult PlaceLayout(string layoutText, double x, double y, int orientation)
            => Core!.PlaceLayout(layoutText, new Vector2((float)x, (float)y), orientation);

        public RectRemovalResult RemoveRect(double x0, double y0, double x1, double y1)
            => Core!.RemoveRect(new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1));

        public string CheckLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => ConstructionVerbs.VerdictLine(Core!.CheckLine(blueprintId, new Vector2((float)x0, (float)y0),
                new Vector2((float)x1, (float)y1), orientation));

        public string CheckRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => ConstructionVerbs.VerdictLine(Core!.CheckRect(blueprintId, new Vector2((float)x0, (float)y0),
                new Vector2((float)x1, (float)y1), orientation));

        public string CheckLayout(string layoutText, double x, double y, int orientation)
            => ConstructionVerbs.VerdictLine(Core!.CheckLayout(layoutText, new Vector2((float)x, (float)y),
                orientation));

        public string CaptureLayout(double x0, double y0, double x1, double y1)
            => Core!.CaptureLayout(new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1));

        public string BuildingAt(double x, double y) => Core!.BuildingIdAt(x, y);

        public string CheckPlacement(string blueprintId, double x, double y, int orientation)
            => ConstructionVerbs.VerdictLine(Core!.Check(blueprintId, new Vector2((float)x, (float)y),
                orientation));

        public string GizmoParams() => ConstructionNumbers.Gizmo(Core!.GizmoParams);
    }
}
