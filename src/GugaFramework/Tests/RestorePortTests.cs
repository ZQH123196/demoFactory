#nullable enable
using System;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 时钟与寻路步进器的恢复口 —— 存档那一半要靠它们把世界推回存档那一刻。
///
/// <para>这两个口是用户 2026-10-07 确认许可开出来的（寻路层扩展要走用户明确许可）。
/// 判据钉的是「读档前后那一份读数逐字相同」：步号、待触发事件表、登记表顺序。</para>
/// </summary>
public class RestorePortTests
{
    // === 时钟 ===

    [Fact]
    public void 步号能恢复到给定值()
    {
        var clock = new GameClock();
        clock.Advance(5);
        clock.Tick.ShouldBe(5);

        clock.RestoreTick(4000);
        clock.Tick.ShouldBe(4000, "读档把步号拨回存档那一刻，之后时间照常走 Advance");
    }

    [Fact]
    public void 步号不许倒拨()
    {
        // 倒退步号会让已触发的定时事件永远不再触发，而那种世界跑起来不报错、
        // 只是「某批结算从此不再发生」——判红时几乎无从下手，所以这一格当场抛。
        var clock = new GameClock();
        clock.Advance(10);
        Should.Throw<ArgumentOutOfRangeException>(() => clock.RestoreTick(3))
            .Message.ShouldContain("只能往前拨", Case.Sensitive);
    }

    [Fact]
    public void 全量还原的拨回口允许倒退但负数当场抛()
    {
        // 只给「全量快照原样还原」那一档：重试是同一局回到过去，步号必须能退。
        var clock = new GameClock();
        clock.RestoreTick(7200);

        clock.RewindTick(100);
        clock.Tick.ShouldBe(100, "拨回口就是用来倒退的");
        clock.Advance(1).ShouldBeTrue();
        clock.Tick.ShouldBe(101, "拨回之后时间照常从那一格往前走");

        Should.Throw<ArgumentOutOfRangeException>(() => clock.RewindTick(-1));
    }

    [Fact]
    public void 拨回口不放开读档口的只许往前()
    {
        // 两条语义各走各的口：给重试开的倒退口不许顺手把读档的守卫放开。
        var clock = new GameClock();
        clock.RestoreTick(7200);
        clock.RewindTick(100);

        Should.Throw<ArgumentOutOfRangeException>(() => clock.RestoreTick(99))
            .Message.ShouldContain("只能往前拨", Case.Sensitive);
        clock.Tick.ShouldBe(100, "抛了就不许改读数");
    }

    [Fact]
    public void 待触发事件表带名字和剩余步数()
    {
        var clock = new GameClock();
        clock.Schedule("经济周期", 10, () => { });
        clock.Schedule("波次", 40, () => { });
        clock.Advance(4);

        var pending = clock.Pending();
        pending.Count.ShouldBe(2);
        pending[0].Id.ShouldBe("经济周期");
        pending[0].RemainingTicks.ShouldBe(6);
        pending[1].Id.ShouldBe("波次");
        pending[1].RemainingTicks.ShouldBe(36);
    }

    [Fact]
    public void 待触发事件表按调度次序念()
    {
        // 同一步内先排的先触发 —— 读数与还原都要按这个次序，少一条就是静默分叉。
        var clock = new GameClock();
        clock.Schedule("先", 10, () => { });
        clock.Schedule("后", 10, () => { });
        clock.Schedule("更早", 5, () => { });

        clock.Pending().Select(e => e.Id).ShouldBe(new[] { "更早", "先", "后" });
    }

    [Fact]
    public void 取消掉的不进表()
    {
        var clock = new GameClock();
        clock.Schedule("留着", 5, () => { });
        clock.Schedule("撤掉", 5, () => { }).Dispose();
        clock.Pending().Select(e => e.Id).ShouldBe(new[] { "留着" });
    }

    [Fact]
    public void 待触发事件表重建之后逐字相同()
    {
        // 这一格是「不恢复的后果」的判据：不重建的话，脉搏回来了，但这批结算永远不发生，
        // 而且不报错。读档那一发就是按这张表重排一遍。
        var before = new GameClock();
        before.Schedule("经济周期", 30, () => { });
        before.Schedule("波次", 90, () => { });
        before.Schedule("BOSS 降临", 200, () => { });
        before.Advance(12);

        var after = new GameClock();
        after.RestoreTick(before.Tick);
        foreach (var e in before.Pending()) after.Schedule(e.Id, e.RemainingTicks, () => { });

        after.Tick.ShouldBe(before.Tick);
        after.Pending().ShouldBe(before.Pending(), "按那张表重排一遍，读数必须逐字相同");
    }

    // === 寻路步进器 ===

    [Fact]
    public void 寻路步号能恢复到给定值()
    {
        var (stepper, clock, _) = StepperWithAgents();
        clock.Advance(1);
        stepper.StepIndex.ShouldBe(1);

        stepper.RestoreStepIndex(4000);
        stepper.StepIndex.ShouldBe(4000);
    }

    [Fact]
    public void 寻路步号不许倒拨()
    {
        var (stepper, clock, _) = StepperWithAgents();
        clock.Advance(1);
        Should.Throw<ArgumentOutOfRangeException>(() => stepper.RestoreStepIndex(0))
            .Message.ShouldContain("只能往前拨", Case.Sensitive);
    }

    [Fact]
    public void 登记表按登记次序念出来()
    {
        var (stepper, _, _) = StepperWithAgents();

        // 登记次序 = 步进次序。读档按这个次序重建 Register，次序错了就是重放分叉。
        stepper.AgentIds.ShouldBe(new[] { "u1", "u2" });
    }

    [Fact]
    public void 位置与朝向按登记次序读得出来()
    {
        var (stepper, _, _) = StepperWithAgents();
        var rows = stepper.AgentIds
            .Select(id =>
            {
                var p = stepper.PositionOf(id);
                return $"{id}={p.X:F0},{p.Z:F0}@{stepper.SolutionOf(id).FacingDegrees:F0}";
            })
            .ToList();

        // 位置属权威档：要存。只存 NavCommand 是重算不出位置的——重算要的是上一份解的输入。
        rows.ShouldBe(new[] { "u1=1,2@0", "u2=3,4@0" });
    }

    [Fact]
    public void 恢复步号不改变位置与朝向()
    {
        var (stepper, _, _) = StepperWithAgents();
        var before = stepper.PositionOf("u1");
        stepper.RestoreStepIndex(4000);
        stepper.PositionOf("u1").ShouldBe(before, "恢复口只拨步号，不许顺手改任何移动状态");
    }

    [Fact]
    public void 注销单位之后登记表与索引一起收拢()
    {
        // 注销只给「全量快照原样还原」用：战斗中新召的人要连登记一起作废。
        // 索引表与登记表是两份真值，收拢时错一格，后面的人就查不到或串位。
        var (stepper, _, _) = StepperWithAgents();
        stepper.Register(Spec("u3", 5.0, 6.0));

        stepper.Unregister("u1").ShouldBeTrue();
        stepper.HasAgent("u1").ShouldBeFalse("注销后查询口当场变假");
        stepper.AgentIds.ShouldBe(new[] { "u2", "u3" }, "剩下的人保持原次序");
        stepper.PositionOf("u3").ShouldBe(new NavPoint(5.0, 6.0), "索引位移正确才查得到原来那一条");

        // 再注销原来排在后面的人：它已被挪到 0 号位，索引没跟着挪就会摘错人。
        stepper.Unregister("u2").ShouldBeTrue();
        stepper.AgentIds.ShouldBe(new[] { "u3" });

        stepper.Unregister("u9").ShouldBeFalse("本来就没登记 = false，不是错误");
        stepper.AgentIds.ShouldBe(new[] { "u3" }, "没注销成功不许动登记表");
    }

    // === 样本 ===

    private static (NavStepper Stepper, GameClock Clock, NavGrid Grid) StepperWithAgents()
    {
        var spec = new NavGridSpec
        {
            Space = new SpaceId("恢复口"),
            CellMeters = 1.0,
            CellsX = 8,
            CellsZ = 8,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = Array.Empty<NavCell>(),
        };
        var grid = new NavGrid(spec);
        var solver = new NavSolver(
            new NavSpace(spec.Space, grid),
            new NavSolveBudget { PerRequestExpansions = 4000 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);
        var clock = new GameClock();
        var stepper = new NavStepper(
            solver,
            new NavMoveTuning { StepSeconds = 0.1, LookAheadMeters = 2.0, StepSolveExpansions = 20000 },
            clock);
        stepper.Register(Spec("u1", 1.0, 2.0));
        stepper.Register(Spec("u2", 3.0, 4.0));
        return (stepper, clock, grid);
    }

    private static NavAgentSpec Spec(string id, double x, double z) => new()
    {
        Id = id,
        RadiusMeters = 0.4,
        MoveSpeedMetersPerSecond = 3.0,
        Start = new NavPoint(x, z),
        FacingDegrees = 0.0,
    };
}