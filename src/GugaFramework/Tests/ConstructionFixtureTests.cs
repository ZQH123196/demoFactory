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
/// 场地建筑登记（<c>RegisterFixture</c>）的契约测试 —— 全部离线跑通，零引擎依赖。
///
/// <para><b>这批测试存在的理由是它把一条真 bug 从「只能出图看」变成了离线命题</b>：
/// 场地建筑（关卡初始摆位那一批）不在册内时，裁决流看不见它们，原型就得在自己的落建入口里补一段
/// 距离判定 —— 于是同一个点上「幽灵绿、点下去被拒」（D29 的预览一致性在这条路径上不成立）。
/// 让它们进册之后，预览与落建共用同一条 <c>Check</c>，同一个点的两处裁决永远对得上，
/// 而这件事现在是几发 <c>dotnet test</c>，不必起引擎、不必出图。</para>
///
/// <para>自由侧那几组坐标与半径照当年那侧的种子布局抄（半径 = <c>MinPlaceDistance/2</c> = 6，
/// 两处间距 8、11.31 各自小于 12）—— 那正是当初逼出这一格的那个世界的形状，
/// 数字照抄而不是另编，是为了让「这一格为什么必须存在」在测试里也一眼看得出来。</para>
/// </summary>
public class ConstructionFixtureTests
{
    /// <summary>全部蓝图同半径：6 = MinPlaceDistance/2（D12 的等价式那一半，两座要的间距 = 半径和 = 12）。</summary>
    private const double Radius = 6.0;

    private const double CellSize = 1.0;

    // ---- 自由规则（真实消费者的那一侧）-------------------------------------------

    [Fact]
    public void 场地建筑进册且占位_后来玩家建筑贴上去回TooClose()
    {
        var core = FreeCore();
        core.RegisterFixture("Palace", new Vector2(-8, 0), "palace");

        // 半径和 12：(2,0) 离宫殿 10 ⇒ 挡住它的是场地建筑，不是别的什么。
        var v = core.Commit("Tavern", new Vector2(2, 0));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        v.Ok.ShouldBeFalse("模块看得见场地建筑：贴上去就被判过近");
        v.Reason.ShouldBe(PlacementReason.TooClose);
        core.Find("palace").ShouldNotBeNull("场地建筑进册：快照与 Find 都看得见它");
        core.Ids.ShouldBe(new[] { "palace" });
        core.Count.ShouldBe(1);
        core.OccupiedCount.ShouldBe(1, "占着地：占位数与在册数同增");    }

    [Fact]
    public void 场地建筑压着别人也照样登记成功_禁区由规则自己算()
    {
        var core = FreeCore();

        // 宫殿(-8,0) 与 行会(0,0) 相距 8 < 12：同一对建筑在模块眼里本来就「过近」。
        // 场地建筑不是玩家手势，没有「换个点」这个选项 —— 所以这一格不许因压着别人而回读数。
        var palace = core.RegisterFixture("Palace", new Vector2(-8, 0), "palace");
        var guild = core.RegisterFixture("GuildHall", new Vector2(0, 0), "guild");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        palace.Ok.ShouldBeTrue();
        guild.Ok.ShouldBeTrue("场地建筑登记不判间距：关卡摆位要的就是这个");
        core.Count.ShouldBe(2);
        core.Check("Tavern", new Vector2(-4, 0)).Reason.ShouldBe(PlacementReason.TooClose, "窄缝也挡：两座场地建筑之间那条缝照样是禁区");    }

    [Fact]
    public void 场地建筑压着玩家的建筑也照样登记成功_但反过来占住了玩家的地()
    {
        var core = FreeCore();
        core.RegisterFixture("Tavern", new Vector2(0, 0), "seedTavern");

        var later = core.RegisterFixture("Tavern", new Vector2(4, 0), "seedTavern2");
        var player = core.Check("Market", new Vector2(4, 0));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        later.Ok.ShouldBeTrue();
        player.Reason.ShouldBe(PlacementReason.TooClose, "「不判自己」不等于「不占地方」：后登记的那座照样挡住后来的玩家建筑");    }

    [Fact]
    public void 悬停与落建对同一个点同源_幽灵与点击永远对得上()
    {
        // 这一条就是那个 bug 的钉子：场地建筑不在册内时，原型落建入口自己判的那一段与裁决流说的不是一回事
        // （真引擎读数：coreReason=None 而 reason='过近：与种子建筑间距 < 12'）。
        // 进册之后，同一个候选点两处裁决必须逐字一致 —— 贴种子、种子之间那条窄缝、空地各来一组。
        var core = SeededFreeCore();
        var points = new[]
        {
            new Vector2(5, 0),      // 贴着行会(0,0)
            new Vector2(-5, 0),     // 贴着宫殿(-8,0)
            new Vector2(-4, 0),     // 宫殿↔行会那条 8 米的窄缝正中间
            new Vector2(-14, 12),   // 坊市(-18,8)↔药店(-10,16) 那条 11.31 米的缝
            new Vector2(120, 90),   // 空地
        };

        var verdicts = new List<PlacementVerdict>();
        foreach (var point in points)
        {
            core.Hover.Push(new GestureSpec("Tavern", point));
            var hover = core.Hover.Last!;
            var commit = core.Commit("Tavern", point);
            verdicts.Add(commit);

            // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
            // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
            // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

            hover.Ok.ShouldBe(commit.Ok, $"{point}：悬停与落建的成败必须一致（D29）");
            hover.Reason.ShouldBe(commit.Reason, $"{point}：拒绝原因也要一致");
            hover.Position.ShouldBe(commit.SnappedWorld, $"{point}：落点是同一个数");
            hover.Circle!.Radius.ShouldBe(commit.Circle!.Radius);        }

        verdicts.Count(v => !v.Ok).ShouldBe(4, "四组点被挡、空地那一组放行 —— 挡住它们的只能是模块自己算出来的禁区");
    }

    [Fact]
    public void 场地建筑登记完_当前手势当场重算_不用再推一次()
    {
        var core = SeededFreeCore();
        core.Hover.Push(new GestureSpec("Tavern", new Vector2(120, 90)));
        var green = core.Hover.Last!;
        var pushed = 0;
        core.Hover.Changed += _ => pushed++;

        core.RegisterFixture("GuildWarrior", new Vector2(120, 90), "extraGuild");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        green.Ok.ShouldBeTrue();
        core.Hover.Last!.Ok.ShouldBeFalse("占用变了就重算：幽灵自己从绿变红（D27）");
        pushed.ShouldBe(1);    }

    [Fact]
    public void 场地建筑不是刚建成的_进度落在完工且只发一条registered()
    {
        var core = FreeCore();

        core.RegisterFixture("Palace", new Vector2(-8, 0), "palace");

        var row = core.Find("palace")!;
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        row.State.ShouldBe(BuildState.Completed, "它不是一次施工，是关卡摆位：进度直接落在完工（D16 两档不变）");
        row.RemainingTicks.ShouldBe(0);
        core.Events.Count<BuildingRegistered>().ShouldBe(1);
        core.Events.Count<BuildingPlaced>().ShouldBe(0, "不是一次放置手势：不该有一条 placed");
        core.Events.Count<BuildingCompleted>().ShouldBe(0, "「建成」这一发对它不存在 —— 发出去只会逼订阅者各写一个例外分支");    }

    [Fact]
    public void 场地建筑的那一行进快照段_载荷一个字都不进()
    {
        var core = FreeCore();

        core.RegisterFixture("Palace", new Vector2(-8.004f, 0.004f), "palace", payload: "Hero/1/0");

        var segment = core.DebugSnapshotSegment();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        segment.ShouldContain("palace Palace");
        segment.ShouldContain("pos=-8,0", Case.Sensitive, "落点是规则吸过的那一个：-8.004/0.004 折到 0.01 那一档（判的点与落的点是同一个数）");
        segment.ShouldContain("occupied=1");
        segment.ShouldNotContain("Hero/1/0", Case.Sensitive, "载荷是原型的私产，模块存而不读（D31、D32）");    }

    [Fact]
    public void 作者错误当场抛_三样都不兜默认值()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<InvalidOperationException>(
            () => FreeCore().RegisterFixture("Palace", new Vector2(-8, 0), ""),
            "场地建筑的 id 是关卡摆位的一部分，没有自动发号那一档可兜（「桥只转发」）");
        Should.Throw<InvalidOperationException>(
            () => FreeCore().RegisterFixture("BossLair", new Vector2(0, 0), "boss"),
            "蓝图不在册：没有蓝图就没有半径可占，兜 0 等于这块地谁也判不过近");
        var core = FreeCore();
        core.RegisterFixture("Palace", new Vector2(-8, 0), "palace");
        Should.Throw<InvalidOperationException>(
            () => core.RegisterFixture("Palace", new Vector2(0, 0), "palace"),
            "重名 = 两份摆位");
    }

    [Fact]
    public void 移除场地建筑_那圈地当场放开()
    {
        var core = FreeCore();
        core.RegisterFixture("Palace", new Vector2(-8, 0), "palace");

        var removed = core.Remove("palace", RemovalCause.Destroyed);
        var after = core.Check("Tavern", new Vector2(-8, 0));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        removed.Ok.ShouldBeTrue();
        after.Ok.ShouldBeTrue("腾了占用就放开：场地建筑与玩家建筑走同一条出册链");
        core.Count.ShouldBe(0);
        core.OccupiedCount.ShouldBe(0);    }

    [Fact]
    public void 同种子两跑逐字全等()
    {
        static string Run()
        {
            var core = SeededFreeCore(() => 7);
            core.Commit("Tavern", new Vector2(120, 90));
            core.Damage("palace", 100);
            return core.DebugSnapshotSegment() + core.Events.TailText(99);
        }

        Run().ShouldBe(Run());
    }

    // ---- 网格规则（契约侧：压格那一档的语义与自由侧不同，理由钉在实现上）-----------

    [Fact]
    public void 网格侧_场地建筑登记后玩家建筑压着那一格回Occupied()
    {
        var core = GridCore();
        core.RegisterFixture("keep", new Vector2(0.1f, 0.1f), "ruin");

        var v = core.Commit("keep", new Vector2(1.1f, 0.1f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        v.Reason.ShouldBe(PlacementReason.Occupied);
        core.Find("ruin")!.Anchor.ShouldBe(new GridCell(0, 0), "登记的锚点格就是占地（D9）");
        core.OccupiedCount.ShouldBe(3, "L 形占地占三格");    }

    [Fact]
    public void 网格侧_场地建筑压着已占的格照登记成功_但那一格不夺_留给原占用者()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(0.1f, 0.1f));

        // 后来者夺格是能写的，但拆掉场地建筑之后那几格就没人认领了 ——
        // 册里那座会变成「在册却一格都不占」，那一格占着谁与册上有什么当场给两个答案。
        var fixture = core.RegisterFixture("keep", new Vector2(0.1f, 0.1f), "ruin");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        fixture.Ok.ShouldBeTrue("场地建筑登记不因压着别人而回读数：它不是玩家手势");
        core.BuildingIdAt(0.1f, 0.1f).ShouldBe("b1", "一格一主：已占的格原地留给原占用者");
        core.Remove("ruin", RemovalCause.Dismantle);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.BuildingIdAt(0.1f, 0.1f).ShouldBe("b1");
        core.Find("b1").ShouldNotBeNull();    }

    [Fact]
    public void 网格侧_禁区会缩回去这件事写在测试里_拆掉原占用者之后那格就空出来了()
    {
        var core = GridCore();
        core.Commit("keep", new Vector2(0.1f, 0.1f));
        core.RegisterFixture("keep", new Vector2(3.1f, 0.1f), "ruin");

        core.Remove("b1", RemovalCause.Dismantle);

        core.Check("keep", new Vector2(0.1f, 0.1f)).Ok.ShouldBeTrue("场地建筑没占住那一格（原占用者占着先），所以拆掉之后禁区缩回去 —— 自由规则那侧没有这条限制");
    }

    [Fact]
    public void 网格侧_场地建筑摆出界也照样登记_落点由锚点格心推导()
    {
        var core = GridCore(min: -2, max: 4);

        var fixture = core.RegisterFixture("keep", new Vector2(-2.4f, 4.2f), "ruin");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        fixture.Ok.ShouldBeTrue("场地建筑摆在哪就是哪：不查边界");
        core.Find("ruin")!.Anchor.ShouldBe(new GridCell(-3, 4), "出界那一格照样落格");
        core.Find("ruin")!.Position.ShouldBe(new Vector2(-2.5f, 4.5f), "落点仍是锚点格心推导出来的那个数（D9）");    }

    // ---- 工具 -------------------------------------------------------------------

    /// <summary>当年那份种子布局（自由侧，全部半径 6）。它自身就有两对挨得比 12 近。</summary>
    private static ConstructionCore SeededFreeCore(Func<long>? tick = null)
    {
        var core = FreeCore(tick);
        core.RegisterFixture("Palace", new Vector2(-8, 0), "palace");
        core.RegisterFixture("GuildHall", new Vector2(0, 0), "guild");
        core.RegisterFixture("Market", new Vector2(-18, 8), "market");
        core.RegisterFixture("Tavern", new Vector2(-18, -8), "tavern");
        core.RegisterFixture("Apothecary", new Vector2(-10, 16), "apothecary");
        core.RegisterFixture("MonsterLair", new Vector2(60, 20), "lair1");
        core.RegisterFixture("MonsterLair", new Vector2(65, -25), "lair2");
        return core;
    }

    private static ConstructionCore FreeCore(Func<long>? tick = null)
        => new(new FreePlacementRule(positionStep: 0.01), Blueprints(), tick);

    private static ConstructionCore GridCore(IConstructionHost? host = null, int min = -2, int max = 9)
        => new(new GridPlacementRule(CellSize, min, min, max, max, host), new[]
        {
            new Blueprint("keep", 100, CellFootprint.FromCells(new[]
            {
                new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
            })),
        });

    private static IEnumerable<Blueprint> Blueprints()
    {
        foreach (var id in new[]
        {
            "Palace", "GuildHall", "GuildWarrior", "Market", "Tavern", "Apothecary", "MonsterLair",
        })
        {
            yield return new Blueprint(id, 100, new RadiusFootprint(Radius));
        }
    }
}
