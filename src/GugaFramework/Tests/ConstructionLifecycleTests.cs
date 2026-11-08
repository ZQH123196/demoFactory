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
/// 建造过程与生命周期的契约测试（工期 / 状态 / 暂停 / 损毁 / 修复 / 载荷）—— <b>零引擎依赖</b>。
///
/// 这里守的是几条「看起来对、其实会分叉」的地方：工期 0 与工期 n 有没有走同一条流水线、施工中
/// 被打空会不会还补发一条 Completed、暂停期间倒数有没有照走、满血修复是不是回 ok=true、
/// 不透明载荷会不会漏进快照段把逐字基线搅掉。这些以前只能开引擎点一遍看表现，
/// 现在是一发 <c>dotnet test</c>（「测试与实测分工」：逻辑行为用断言，不拿 MCP 验）。
/// </summary>
public class ConstructionLifecycleTests
{
    // ---- 工期与状态机（D15、D16、D17）-------------------------------------------

    [Fact]
    public void 工期0_同一次Commit里连发Placed再Completed_步号是同一个()
    {
        var core = Core(0, () => 4);

        core.Commit("keep", new Vector2(0.2f, 0.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Events.All.Select(e => e.Wire).ShouldBe(new[] { "placed", "completed" }, Case.Sensitive, "顺序是契约：Placed 先（D16）");
        core.Events.All.Select(e => e.Tick).ShouldBe(new long[] { 4, 4 }, "同一次调用里连发，两条带的都是那一步");
        core.Find("b1")!.State.ShouldBe(BuildState.Completed);
        core.Find("b1")!.RemainingTicks.ShouldBe(0);    }

    [Fact]
    public void 工期n_第n步才发Completed_早一步都不发()
    {
        var now = 0L;
        var core = Core(5, () => now);
        core.Commit("keep", new Vector2(0.2f, 0.2f));
        var completedBefore = core.Events.Where<BuildingCompleted>().ToList();

        now = 1;
        core.Advance(4);
        // 读数取当场那几个数，不留 Building 的引用：册里那行是同一个对象，
        // 留着引用到下一步再断言就变成「断言了推进之后的状态」，测不出任何东西。
        var stateAfterFour = core.Find("b1")!.State;
        var leftAfterFour = core.Find("b1")!.RemainingTicks;

        now = 2;
        core.Advance(1);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        stateAfterFour.ShouldBe(BuildState.Building);
        leftAfterFour.ShouldBe(1);
        completedBefore.ShouldBeEmpty("第 5 步没到就不许发");
        core.Find("b1")!.State.ShouldBe(BuildState.Completed);
        core.Events.Where<BuildingCompleted>().Single().Tick.ShouldBe(2);    }

    [Fact]
    public void 工期0与工期n同一条流水线_一次推n步与逐次推n步逐字同结果()
    {
        // 「无特判」不能靠注释保证：把两种推法跑一遍，状态与事件流必须逐字相同。
        static string Run(bool oneShot)
        {
            var core = Core(5, () => 0);
            core.Commit("keep", new Vector2(0.2f, 0.2f));
            core.Commit("keep", new Vector2(4.2f, 0.2f));
            if (oneShot)
            {
                core.Advance(5);
            }
            else
            {
                for (var i = 0; i < 5; i++)
                {
                    core.Advance(1);
                }
            }

            return core.DebugSnapshotSegment() + core.Events.TailText(99);
        }

        Run(oneShot: true).ShouldBe(Run(oneShot: false));
    }

    [Fact]
    public void 推过头的那一步不产生负剩步_事件只发一条()
    {
        var core = Core(3, () => 0);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.Advance(99);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Find("b1")!.RemainingTicks.ShouldBe(0, "剩步钉在 0，不留负数");
        core.Events.Count<BuildingCompleted>().ShouldBe(1);    }

    [Fact]
    public void 倒退的步数当场抛_模块没有倒着走的时间()
    {
        var core = Core(3);

        Should.Throw<ArgumentOutOfRangeException>(() => core.Advance(-1));
    }

    [Fact]
    public void 逐次覆盖工期_不给就用蓝图自带那份()
    {
        var core = Core(5);
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, null, 0);
        core.Commit("keep", new Vector2(4.2f, 0.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Find("b1")!.State.ShouldBe(BuildState.Completed, "覆盖成 0 = 落座即完工");
        core.Find("b2")!.State.ShouldBe(BuildState.Building);
        core.Find("b2")!.RemainingTicks.ShouldBe(5, "没给就照蓝图那份");    }

    [Fact]
    public void 工期覆盖负数当场抛_不折成0()
    {
        var core = Core(5);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            core.Commit("keep", new Vector2(0.2f, 0.2f), 0, null, -3));
    }

    // ---- 去向（D16、D17）--------------------------------------------------------

    [Fact]
    public void 施工中打空血_发Removed去向摧毁_Completed永不发_同格立刻能再放()
    {
        var core = Core(10);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        var hit = core.Damage("b1", 100);
        core.Advance(20);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hit.Ok.ShouldBeTrue();
        core.Count.ShouldBe(0, "摧毁不是状态：不在册了（D16）");
        core.OccupiedCount.ShouldBe(0, "归零立腾占用");
        core.Events.Where<BuildingCompleted>().ShouldBeEmpty("施工中打空则 Completed 永不发");
        core.Events.Where<BuildingRemoved>().Single().Cause.ShouldBe(RemovalCause.Destroyed);
        core.Commit("keep", new Vector2(0.2f, 0.2f)).Ok.ShouldBeTrue("腾干净了才放得进");    }

    [Fact]
    public void 打空那一发先Damaged后Removed_致死的那一下要看得见()
    {
        var core = Core(10);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.Damage("b1", 100);

        core.Events.All.Select(e => e.Wire).ShouldBe(new[] { "placed", "damaged", "removed" });
    }

    [Fact]
    public void 三种去向都进同一条Removed_拆与取消都不补发Completed()
    {
        var core = Core(10);
        core.Commit("keep", new Vector2(0.2f, 0.2f));
        core.Commit("keep", new Vector2(4.2f, 0.2f));

        core.Remove("b1", RemovalCause.Dismantle);
        core.Remove("b2", RemovalCause.Cancel);
        core.Advance(30);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Events.Where<BuildingRemoved>().Select(e => e.Cause).ShouldBe(new[] { RemovalCause.Dismantle, RemovalCause.Cancel });
        core.Events.Where<BuildingCompleted>().ShouldBeEmpty();
        core.OccupiedCount.ShouldBe(0);    }

    // ---- 暂停（D18）-------------------------------------------------------------

    [Fact]
    public void 暂停跳过倒数_暂停期间推n步剩余步不动_恢复接着数()
    {
        var core = Core(10);
        core.Commit("keep", new Vector2(0.2f, 0.2f));
        core.Advance(2);

        core.PauseBuild("b1");
        core.Advance(5);
        var leftWhilePaused = core.Find("b1")!.RemainingTicks;
        var stateWhilePaused = core.Find("b1")!.State;

        core.ResumeBuild("b1");
        core.Advance(3);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        leftWhilePaused.ShouldBe(8, "暂停期间倒数一步都不走（D18）");
        stateWhilePaused.ShouldBe(BuildState.Building);
        core.Find("b1")!.RemainingTicks.ShouldBe(5, "恢复后从暂停处接着数");
        core.Events.Where<BuildPaused>().Single().RemainingTicks.ShouldBe(8);
        core.Events.Where<BuildResumed>().Single().RemainingTicks.ShouldBe(8);    }

    [Fact]
    public void 暂停已完工建筑回InvalidState_动词层回读数不抛_录的是ok为false()
    {
        var sink = Sink(Core(0));
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.2|0.2");

        var r = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Pause, "b1");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeFalse("拒绝是读数不是异常（D7）");
        r.Text.ShouldContain("reason=InvalidState");
        r.Text.ShouldContain("ok=0", Case.Sensitive, "这个 ok 会被录进 Journal，重放要比它（「写路径全录」）");
        sink.Core.Events.Where<BuildPaused>().ShouldBeEmpty();    }

    [Fact]
    public void 已经在暂停中的再暂停_与没在暂停的再恢复_都回InvalidState()
    {
        // 没发生的事不许回成事：回 ok=true 就是骗原型「暂停生效了」，而实际什么也没变。
        var core = Core(10);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        var first = core.PauseBuild("b1");
        var twice = core.PauseBuild("b1");
        var resumed = core.ResumeBuild("b1");
        var twiceResumed = core.ResumeBuild("b1");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        first.Ok.ShouldBeTrue();
        twice.Reason.ShouldBe(PlacementReason.InvalidState);
        resumed.Ok.ShouldBeTrue();
        twiceResumed.Reason.ShouldBe(PlacementReason.InvalidState);    }

    // ---- 血量（D2、D19）---------------------------------------------------------

    [Fact]
    public void 修复钳到上限_超出的那截不生效_事件记的是真修上去的那截()
    {
        var core = Core(0);
        core.Commit("keep", new Vector2(0.2f, 0.2f));
        core.Damage("b1", 30);

        core.Repair("b1", 100);

        var repaired = core.Events.Where<BuildingRepaired>().Single();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Find("b1")!.Health.ShouldBe(100, "补到上限就停，不会冲过上限");
        repaired.Amount.ShouldBe(30, "钳过之后真加上的是缺的那 30，不是发过来的 100");
        repaired.Remaining.ShouldBe(100);    }

    [Fact]
    public void 满血修复回InvalidState_这是原型的不扣款信号()
    {
        var core = Core(0);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        var r = core.Repair("b1", 10);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeFalse("为一口没修上的血付钱是 bug（D6）");
        r.Reason.ShouldBe(PlacementReason.InvalidState);
        core.Events.Where<BuildingRepaired>().ShouldBeEmpty();    }

    [Fact]
    public void 未知id扣血与修复都回UnknownId_动词层回读数不抛_录的是ok为false()
    {
        var sink = Sink(Core(0));
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.2|0.2");

        var dmg = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Damage, "ghost|5");
        var fix = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Repair, "ghost|5");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        dmg.Text.ShouldContain("reason=UnknownId");
        dmg.Text.ShouldContain("ok=0");
        fix.Text.ShouldContain("reason=UnknownId");
        fix.Ok.ShouldBeFalse();    }

    [Fact]
    public void 扣血与补血的数值非正当场抛_零和负数这一发什么都没发生()
    {
        var core = Core(0);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<ArgumentOutOfRangeException>(() => core.Damage("b1", 0));
        Should.Throw<ArgumentOutOfRangeException>(() => core.Damage("b1", -1));
        Should.Throw<ArgumentOutOfRangeException>(() => core.Repair("b1", 0));    }

    // ---- 血量绝对值写口（SetHealth，D19 的补充决定）------------------------------

    [Fact]
    public void 设绝对血量把上限一并抬到max_抬过上限的数之后Repair钳的是新上限()
    {
        // 盖：那道语义坑。只抬当前血量不抬上限的话，最后这一发 Repair 会把 400 反向扣回 100
        // —— 那不是修血是扣血，而且回 ok=true（「修上了」），读数上完全看不出来。
        var core = Core(100);
        core.Commit("keep", new Vector2(0.2f, 0.2f));
        core.Damage("b1", 30); // 70/100

        var set = core.SetHealth("b1", 700);
        core.Damage("b1", 300); // 400/700 —— 缺一口，Repair 那一发才真的发生
        var repaired = core.Repair("b1", 10_000);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        set.Ok.ShouldBeTrue();
        core.Find("b1")!.MaxHealth.ShouldBe(700, "上限被合法抬高");
        repaired.Ok.ShouldBeTrue("没修满不算失败，钳到上限是这一发的语义");
        core.Find("b1")!.Health.ShouldBe(700, "Repair 钳到的是抬高后的上限 700，400 没被反向扣回 100");    }

    [Fact]
    public void 设绝对血量只抬上限不降_换一座才降上限()
    {
        // 盖：给一个比现上限小的数不压回上限 —— 这一条是「抬血线」不是「改规格」，
        // 而载荷与血量上限的更换口径是「换一座」（D31）。
        var core = Core(100);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.SetHealth("b1", 700);
        core.SetHealth("b1", 40);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        core.Find("b1")!.Health.ShouldBe(40, "当前血量照落定");
        core.Find("b1")!.MaxHealth.ShouldBe(700, "上限只抬不降");    }

    [Fact]
    public void 满血设绝对值照样回ok_它不是经济动作不继承满血修复那一档()
    {
        // 盖：与 Repair 的满血 InvalidState 刻意不同款。那一档是给原型的「不扣款」信号（D6），
        // 而这一条是绝对值写入：值已经是那个数的话这一发照样打成了。回 false 等于让同一条命令
        // 在重放端因为「上一步已经抬过了」而翻成另一种结果（「断言三层」：命令 ok 翻转任何档位都红）。
        var core = Core(100);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        var r = core.SetHealth("b1", 100);
        var evt = core.Events.Where<BuildingHealthSet>().Single();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeTrue("满血也成");
        evt.Health.ShouldBe(100);
        evt.MaxHealth.ShouldBe(100);
        evt.Detail.ShouldBe("hp=100/100", "明细里带上限：只有这一条会改上限，订阅者要判断「还能不能再修」得读到它");    }

    [Fact]
    public void 设绝对血量的作者错误与未知id_非正数抛未知id回读数()
    {
        // 盖：两类失败的分界（D7）。血量 0 与「归零」是同一件事而归零要走出册那条链
        // （Damage：Removed 事件 + 腾占用），从这里兜 0 等于给它造第二个出口。
        var sink = Sink(Core(100));
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.2|0.2");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<ArgumentOutOfRangeException>(() => ConstructionVerbs.Dispatch(sink,
            ConstructionVerbs.SetHealthVerb, "b1|0"));
        Should.Throw<ArgumentOutOfRangeException>(() => ConstructionVerbs.Dispatch(sink,
            ConstructionVerbs.SetHealthVerb, "b1|-5"));

        var ghost = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.SetHealthVerb, "没有这座|50");
        ghost.Text.ShouldContain("reason=UnknownId");
        ghost.Text.ShouldContain("ok=0", Case.Sensitive, "录进账的那个 false，重放要比它");    }

    [Fact]
    public void 设绝对血量是写路径_进录的名单里有它而建筑id缺了当场抛()
    {
        // 盖：「写路径全录」。它和 RegisterFixture 一样常在排布里被调到，但**不在两端各跑一次的 SeedWorld 里**
        // —— 剧本里的那一步只在录制端发生一次，漏录就是漏掉一次写。
        var sink = Sink(Core(100));
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.2|0.2");
        var outcome = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.SetHealthVerb, "b1|700");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ConstructionVerbs.Names.ShouldContain(ConstructionVerbs.SetHealthVerb);
        ConstructionVerbs.InjectNames.ShouldContain(ConstructionVerbs.SetHealthVerb);
        outcome.Ok.ShouldBeTrue(outcome.Text);
        sink.Core!.Find("b1")!.Health.ShouldBe(700);

        Should.Throw<FormatException>(
            () => ConstructionVerbs.Dispatch(sink, ConstructionVerbs.SetHealthVerb, "|700"),
            "建筑 id 是必填：兜一个空串等于让这一发落不到任何一座上（「桥只转发」）");    }

    [Fact]
    public void 设绝对血量与补血解析成不同的一格_同一种数在两条动词里意思相反()
    {
        // 盖：给 Repair 的数是「加多少」，给这一条的是「落成多少」。合成一格的话动词表里
        // 就得靠动词名去猜那一段该加还是该设，而 Journal 回放时读的人只有 args 那一串。
        var call = ConstructionVerbs.Parse(ConstructionVerbs.SetHealthVerb, "b1|700");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        call.Health.ShouldBe(700);
        call.Amount.ShouldBeNull("不是增量那一格");
        ConstructionVerbs.Parse(ConstructionVerbs.Repair, "b1|700").Amount.ShouldBe(700);
        ConstructionVerbs.Parse(ConstructionVerbs.Repair, "b1|700").Health.ShouldBeNull();    }

    // ---- 不透明载荷（D31）--------------------------------------------------------

    [Fact]
    public void 载荷原样存取_模块存而不读()
    {
        var payload = new TestPayload("两头 12 兵");
        var core = Core(0);

        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, null, null, payload);

        core.Find("b1")!.Payload.ShouldBeSameAs(payload, "原样归还：模块不解析、不包装、不复制");
    }

    [Fact]
    public void 载荷经动词表原样进出_编解码是原型的内政()
    {
        var sink = Sink(Core(0));

        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.2|0.2|0|-|0|两头 12 兵");

        var payload = sink.Core.Find("b1")!.Payload as TestPayload;
        payload?.Text.ShouldBe("两头 12 兵");
    }

    [Fact]
    public void 载荷不给就是空_工期那一格空串与横杠都算没给()
    {
        var call = ConstructionVerbs.Parse(ConstructionVerbs.Place, "keep|0.2|0.2|0|-|0|-");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        call.BuildDurationTicks.ShouldBe(0);
        call.PayloadText.ShouldBeNull();
        ConstructionVerbs.Parse(ConstructionVerbs.Place, "keep|0.2|0.2").PayloadText.ShouldBeNull();    }

    [Fact]
    public void 模块快照段只印模块那几列_不印载荷()
    {
        var core = Core(3);
        core.Commit("keep", new Vector2(0.2f, 0.2f), 0, null, null, new TestPayload("两头 12 兵"));

        var segment = core.DebugSnapshotSegment();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        segment.ShouldNotContain("两头", Case.Sensitive, "载荷是原型的列，原型自己印（D31、D32）");
        segment.ShouldNotContain("payload");    }

    // ---- 快照段（D32）与动词表（D34）---------------------------------------------

    [Fact]
    public void 快照段每行带状态与剩余步与暂停标志与方向()
    {
        var core = Core(3);
        core.Commit("keep", new Vector2(0.2f, 0.2f), GridOrientation.M90);
        core.Advance(1);

        core.DebugSnapshotSegment().ShouldContain("b1 keep anchor=0,0 hp=100/100 state=building left=2 paused=0 dir=5", Case.Sensitive, "方向印的是裁决给的那一个整数（D37：模块只透传，语义归放置规则）");

        core.PauseBuild("b1");
        core.DebugSnapshotSegment().ShouldContain("b1 keep anchor=0,0 hp=100/100 state=building left=2 paused=1 dir=5", Case.Sensitive, "暂停标志要能一眼查出来：查「为什么没完工」不用开引擎");
    }

    [Fact]
    public void 工期0那一行的状态列是completed()
    {
        var core = Core(0);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.DebugSnapshotSegment().ShouldContain("state=completed left=0 paused=0");
    }

    [Fact]
    public void 四条新动词都在inject名单里_写路径全录()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ConstructionVerbs.InjectNames.ShouldContain(ConstructionVerbs.Damage);
        ConstructionVerbs.InjectNames.ShouldContain(ConstructionVerbs.Repair);
        ConstructionVerbs.InjectNames.ShouldContain(ConstructionVerbs.Pause);
        ConstructionVerbs.InjectNames.ShouldContain(ConstructionVerbs.Resume);    }

    [Fact]
    public void 移除动词缺省去向是拆除_竖线留着写空串也一样()
    {
        var sink = Sink(Core(0));
        ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.2|0.2|0|b1");

        var r = ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Remove, "b1|");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        r.Ok.ShouldBeTrue();
        sink.Core.Events.Where<BuildingRemoved>().Single().Cause.ShouldBe(RemovalCause.Dismantle);    }

    [Fact]
    public void 暂停与补血的必填参数缺了当场抛_不兜0()
    {
        var sink = Sink(Core(0));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<FormatException>(() => ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Pause, ""));
        Should.Throw<FormatException>(() => ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Damage, "b1"));
        Should.Throw<FormatException>(() => ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|1|2|0|-|3.5"));    }

    // ---- 确定性 -----------------------------------------------------------------

    [Fact]
    public void 同动词序列重跑逐字全等()
    {
        static string Run()
        {
            var now = 0L;
            var sink = Sink(Core(20, () => now));
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|0.2|0.2|0|-|10|两头 12 兵");
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Place, "keep|4.2|0.2|0|b2");
            now = 1;
            // 推时间是原型在自己 Advance 固定点做的事，不是玩家的一发命令，所以它不在动词表里
            // （D17：进度由世界时钟推，Journal 录的是动词）。
            sink.Core.Advance(1);
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Damage, "b1|30");
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Pause, "b1");
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Repair, "b1|1000");
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Resume, "b1");
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Repair, "b1|1000");
            ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Damage, "b2|1000");
            return sink.Core.DebugSnapshotSegment() + sink.Core.Events.TailText(99);
        }

        Run().ShouldBe(Run());
    }

    // ---- 工具 -------------------------------------------------------------------

    private static CellFootprint Footprint() => CellFootprint.FromCells(new[]
    {
        new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
    });

    private static ConstructionCore Core(long buildDurationTicks, Func<long>? tick = null)
        => new(new GridPlacementRule(1, -2, -2, 9, 9),
            new[] { new Blueprint("keep", 100, Footprint(), buildDurationTicks) },
            tick);

    private static ThinSink Sink(ConstructionCore core) => new(core);

    /// <summary>原型侧那个不透明数据对象的样子（模块只当它是个 object）。</summary>
    private sealed record TestPayload(string Text);

    /// <summary>
    /// 薄适配器 —— 原型侧该写的那一份：声明接哪几条动词，每个动词一次调用，一个动词名都不写
    /// （D34 的分工）。载荷的编解码就发生在这儿：动词表递的是原文，模块收的是对象（D31）。
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

        public ConstructionCore Core { get; }

        public string HostLabel => "建造过程契约测试宿主";

        public IReadOnlySet<string> SupportedVerbs => _drop;

        public WriteResult PlaceBuilding(string blueprintId, double x, double y, int orientation,
            string? buildingId, long? buildDurationTicks, string? payloadText)
        {
            var v = Core.Commit(blueprintId, new Vector2((float)x, (float)y), orientation, buildingId,
                buildDurationTicks, payloadText is null ? null : new TestPayload(payloadText));
            return v.Ok
                ? WriteResult.Done(Core.BuildingIdAt(x, y))
                : WriteResult.Fail(v.Reason);
        }

        public WriteResult RemoveBuilding(string buildingId, RemovalCause cause)
            => Core.Remove(buildingId, cause);

        public WriteResult MoveBuilding(string buildingId, double x, double y, int? orientation)
            => Core.Move(buildingId, new Vector2((float)x, (float)y), orientation);

        public WriteResult RotateBuilding(string buildingId, int orientation)
            => Core.Rotate(buildingId, orientation);

        public WriteResult DamageBuilding(string buildingId, double amount) => Core.Damage(buildingId, amount);

        public WriteResult RepairBuilding(string buildingId, double amount) => Core.Repair(buildingId, amount);

        public WriteResult SetHealthBuilding(string buildingId, double hp) => Core.SetHealth(buildingId, hp);

        public WriteResult PauseBuild(string buildingId) => Core.PauseBuild(buildingId);

        public WriteResult ResumeBuild(string buildingId) => Core.ResumeBuild(buildingId);

        // 场地建筑登记：同样是薄适配器搬参数 + 调一次那一个模块入口，判定在模块里。
        public WriteResult RegisterFixture(string blueprintId, double x, double y, int orientation,
            string buildingId, string? payloadText)
            => Core.RegisterFixture(blueprintId, new Vector2((float)x, (float)y), buildingId, orientation);

        // 批量这一族：只搬参数、只调一次。枚举、逐成员表、事件都在模块里（D24、D26）。
        public BatchResult PlaceLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => Core.PlaceLine(blueprintId, new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1),
                orientation);

        public BatchResult PlaceRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => Core.PlaceRect(blueprintId, new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1),
                orientation);

        public BatchResult PlaceLayout(string layoutText, double x, double y, int orientation)
            => Core.PlaceLayout(layoutText, new Vector2((float)x, (float)y), orientation);

        public RectRemovalResult RemoveRect(double x0, double y0, double x1, double y1)
            => Core.RemoveRect(new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1));

        public string CheckLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => ConstructionVerbs.VerdictLine(Core.CheckLine(blueprintId, new Vector2((float)x0, (float)y0),
                new Vector2((float)x1, (float)y1), orientation));

        public string CheckRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
            => ConstructionVerbs.VerdictLine(Core.CheckRect(blueprintId, new Vector2((float)x0, (float)y0),
                new Vector2((float)x1, (float)y1), orientation));

        public string CheckLayout(string layoutText, double x, double y, int orientation)
            => ConstructionVerbs.VerdictLine(Core.CheckLayout(layoutText, new Vector2((float)x, (float)y),
                orientation));

        public string CaptureLayout(double x0, double y0, double x1, double y1)
            => Core.CaptureLayout(new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1));

        public string BuildingAt(double x, double y) => Core.BuildingIdAt(x, y);

        public string CheckPlacement(string blueprintId, double x, double y, int orientation)
            => ConstructionVerbs.VerdictLine(Core.Check(blueprintId, new Vector2((float)x, (float)y),
                orientation));

        public string GizmoParams() => ConstructionNumbers.Gizmo(Core.GizmoParams);
    }
}
