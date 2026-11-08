#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 悬停裁决流的契约测试（D27~D29、ADR 0006）。
///
/// 这几条以前只能靠 MCP 出图看「幽灵是不是红的」——表现层事项、离线测不了。
/// 现在推一次手势就能断言事件，「两处真值」这个病就落在一个 <c>dotnet test</c> 上了。
/// 三条硬契约在这里各盖一条：悬停不进 Journal（call 类那一侧）、不进检查点快照、
/// Commit 不消费悬停值（显式给参）。
/// </summary>
public class ConstructionHoverTests
{
    [Fact]
    public void 推手势推出一条裁决_落点与格名单都读得出来()
    {
        var core = GridCore();
        var pushed = new List<HoverVerdict>();
        core.Hover.Changed += v => pushed.Add(v!);

        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));

        pushed.Count.ShouldBe(1);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        pushed[0].Ok.ShouldBeTrue();
        pushed[0].Position.ShouldBe(new Vector2(3.5f, 4.5f));
        pushed[0].Cells.ShouldBe(new[]
        {
            new GridCell(3, 4), new GridCell(4, 4), new GridCell(3, 5),
        });
        pushed[0].Circle.ShouldBeNull("网格侧给的是格名单，不是圆");    }

    [Fact]
    public void 悬停走的是Check那条流水线_拒绝原因与落建那发逐字同源()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(0.1f, 0.1f));
        core.Hover.Push(new GestureSpec("keep", new Vector2(1.4f, 0.2f)));
        var hover = core.Hover.Last!;

        var commit = core.Commit("keep", new Vector2(1.4f, 0.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hover.Ok.ShouldBeFalse("这一处已经压着第一座了");
        hover.Reason.ShouldBe(commit.Reason);
        hover.Position.ShouldBe(commit.SnappedWorld);
        hover.Cells.ShouldBe(commit.Cells, "悬停与落建是同一条 Check 的两路输出：预览一致性契约（D29）");    }

    [Fact]
    public void 同一手势再推零计算零事件()
    {
        var core = GridCore();
        var pushed = 0;
        core.Hover.Changed += _ => pushed++;
        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));
        var recomputes = core.Hover.Recomputes;

        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        pushed.ShouldBe(1);
        core.Hover.Recomputes.ShouldBe(recomputes, "手势描述没变就连校验都不该跑");    }

    [Fact]
    public void 吸附后同落点再推零事件_鼠标在格内微动不刷事件()
    {
        var core = GridCore();
        var pushed = 0;
        core.Hover.Changed += _ => pushed++;
        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));

        core.Hover.Push(new GestureSpec("keep", new Vector2(3.4f, 4.1f)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        pushed.ShouldBe(1, "两个候选点吸到同一格：裁决没变，就不该有第二条事件");
        core.Hover.Recomputes.ShouldBe(2, "手势描述变了，校验要跑，只是结果没变");    }

    [Fact]
    public void 换到别处推_推出一条新的()
    {
        var core = GridCore();
        var pushed = 0;
        core.Hover.Changed += _ => pushed++;

        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));
        core.Hover.Push(new GestureSpec("keep", new Vector2(6.2f, 4.9f)));

        pushed.ShouldBe(2);
    }

    [Fact]
    public void 提交后自动重算_幽灵从绿变红()
    {
        var core = GridCore();
        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));
        var pushed = 0;
        core.Hover.Changed += _ => pushed++;

        core.Commit("keep", new Vector2(3.2f, 4.9f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        pushed.ShouldBe(1, "占用变了就得重算：那一格刚被自己占了");
        core.Hover.Last!.Ok.ShouldBeFalse();
        core.Hover.Last.Reason.ShouldBe(PlacementReason.Occupied);    }

    [Fact]
    public void 移除后自动重算_幽灵从红变绿()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(3.2f, 4.9f));
        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));
        core.Hover.Last!.Ok.ShouldBeFalse("先确认它红着");

        core.Remove("b1");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Hover.Last!.Ok.ShouldBeTrue();
        core.Hover.Last.Reason.ShouldBe(PlacementReason.None);    }

    [Fact]
    public void 拒绝的那一提交不刷悬停事件_占用没变裁决就不会变()
    {
        var core = GridCore();
        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));
        var pushed = 0;
        core.Hover.Changed += _ => pushed++;

        core.Commit("keep", new Vector2(30f, 30f));

        pushed.ShouldBe(0);
    }

    [Fact]
    public void 推未知蓝图回UnknownBlueprint_不抛_照推红幽灵()
    {
        var core = GridCore();
        core.Hover.Push(new GestureSpec("ghost", new Vector2(3.2f, 4.9f)));

        core.Hover.Last!.Reason.ShouldBe(PlacementReason.UnknownBlueprint);
    }

    [Fact]
    public void 推非单件手势当场抛_不静默折成只放第一个()
    {
        var core = GridCore();

        Should.Throw<InvalidOperationException>(() => core.Hover.Push(
            new GestureSpec("keep", new Vector2(1f, 1f), 0, null, GestureShape.Line)));
    }

    [Fact]
    public void 取消防幽灵推一条null出去_表现层据此收幽灵()
    {
        var core = GridCore();
        var pushed = 0;
        HoverVerdict? last = null;
        core.Hover.Changed += v =>
        {
            pushed++;
            last = v;
        };
        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));

        core.Hover.Clear();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        pushed.ShouldBe(2);
        last.ShouldBeNull();
        core.Hover.Current.ShouldBeNull();    }

    [Fact]
    public void 悬停态一个字都不进快照函数()
    {
        var core = GridCore();
        var before = core.DebugSnapshotSegment();
        var beforeReplay = core.ReplaySnapshotSegment();

        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));
        core.Hover.Push(new GestureSpec("keep", new Vector2(7.7f, 1.1f)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.DebugSnapshotSegment().ShouldBe(before, "推一百次悬停也不该让检查点快照多出一个字（D28）");
        core.ReplaySnapshotSegment().ShouldBe(beforeReplay);    }

    [Fact]
    public void 悬停裁决不进事件流()
    {
        var core = GridCore();
        core.Hover.Push(new GestureSpec("keep", new Vector2(3.2f, 4.9f)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Events.Total.ShouldBe(0, "悬停是 call 类那一侧，不进事件流");
        core.Hover.Line.ShouldContain("hover ok keep");    }

    [Fact]
    public void Commit显式给参不消费悬停值_重放自包含()
    {
        var core = GridCore();
        core.Hover.Push(new GestureSpec("keep", new Vector2(9.2f, 9.2f)));

        // 悬停说这里能放，但落建那发自己说了另一处 —— 落的必须是那一处。
        var v = core.Commit("keep", new Vector2(1.2f, 1.2f));

        v.SnappedWorld.ShouldBe(new Vector2(1.5f, 1.5f));
    }

    [Fact]
    public void 步号取的是同一个时钟读数()
    {
        var now = 5L;
        var core = new ConstructionCore(
            new FreePlacementRule(),
            new[] { new Blueprint("small", 100, new RadiusFootprint(2)) },
            () => now);

        core.Hover.Push(new GestureSpec("small", new Vector2(40.004f, 0f), 22));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Hover.Last!.Tick.ShouldBe(5);
        core.Hover.Last.Position.ShouldBe(new Vector2(40f, 0f));
        core.Hover.Last.Orientation.ShouldBe(15);
        core.Hover.Last.Circle!.Radius.ShouldBe(2);    }

    private static ConstructionCore GridCore(IConstructionHost? host = null)
        => new(new GridPlacementRule(1, -2, -2, 9, 9, host), new[]
        {
            new Blueprint("keep", 100, CellFootprint.FromCells(new[]
            {
                new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
            })),
        });
}
