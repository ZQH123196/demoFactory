#nullable enable
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 流程那一件接进世界之后的四个接口：开局位置、<c>StartRun</c> 动词、
/// 停靠 / 拔锚 / 防守收场报到流程、检查点快照的那几格。
///
/// <para>段推进的规则本身由 <see cref="RunFlowTests"/> 钉着；这一份只钉「世界把它报进去了没有」。</para>
/// </summary>
public class RunFlowWorldTests
{
    [Fact]
    public void 开局在贫城西边的野地_未停靠_流程未开始()
    {
        var world = ArenaWorldBuilder.New().Build();

        world.Flow.Stage.ShouldBe(RunStage.NotStarted, "标题屏上还没按下开始游戏。");
        world.Voyage.Position.ShouldBe(BigMapGrid.RunStart, "开局停在野地那一格，不是停靠点。");
        world.IsDocked.ShouldBeFalse("开局未停靠：玩家自己按停靠才进港。");

        BigMapGrid.RunStart.DistanceTo(BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1)).ShouldBeGreaterThan(
            ArenaTuning.ArriveToleranceMeters, "野地要离停靠点一段，否则开局就算停稳了。");
        world.DebugSnapshot().ShouldContain("flow=notstarted", Case.Sensitive, "快照读得出「未开始」。");
    }

    [Fact]
    public void 开始游戏那一发只推流程_兽不动也不自动进港()
    {
        var world = ArenaWorldBuilder.New().Build();
        var before = world.Voyage.Position;

        ArenaSetup.Inject(world, "StartRun", string.Empty).ShouldBeTrue("开始游戏发得出去。");

        world.Flow.Stage.ShouldBe(RunStage.ToPoor, "推到开局航程。");
        world.Voyage.Position.ShouldBe(before, "开始游戏不下航行令。");
        world.IsDocked.ShouldBeFalse("开始游戏不停靠。");

        ArenaSetup.Inject(world, "StartRun", string.Empty, out _, out var why)
            .ShouldBeFalse("同一局只开始一次。");
        why.ShouldStartWith("rejected: InvalidPhase", Case.Sensitive, "重复开始归「阶段不对」。");
    }

    [Fact]
    public void 停靠贫城进贫城段_拔锚防守收场就进第一段航行()
    {
        var world = ArenaWorldBuilder.New().Build();
        ArenaSetup.Inject(world, "StartRun", string.Empty).ShouldBeTrue("先开局。");

        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Flow.Stage.ShouldBe(RunStage.Poor, "到了贫城才进贫城段。");

        ArenaSetup.Inject(world, "PullAnchor", string.Empty).ShouldBeTrue("拔锚发得出去。");
        world.Flow.Stage.ShouldBe(RunStage.Poor, "拔锚本身不推段，等收场。");

        // 防守 2 游戏小时，多推一段余量；这一场的收场由 BattleSim 报到流程。
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * 3));

        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "防守已经收场。");
        world.TownHallBreached.ShouldBeFalse("这一场镇政府没被攻破。");
        world.Flow.Stage.ShouldBe(RunStage.ToSand, "防守胜利 → 第一段航行。");
        world.Flow.NextCityId.ShouldBe(BigMapGrid.DesertCity.Id, "下一站是沙城。");
    }

    [Fact]
    public void 抵达沙城才进沙城段_防守胜利再进第二段航行()
    {
        var world = ArenaWorldBuilder.New().Build();
        ReachSand(world);

        world.Flow.Stage.ShouldBe(RunStage.Sand, "到了沙城才进沙城段。");

        ArenaSetup.Inject(world, "PullAnchor", string.Empty).ShouldBeTrue("沙城拔锚发得出去。");
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * 3));

        world.TownHallBreached.ShouldBeFalse("这一场镇政府没被攻破。");
        world.Flow.Stage.ShouldBe(RunStage.ToRich, "沙城防守胜利 → 第二段航行。");
    }

    [Fact]
    public void 流程不拦动词_未开始也能停靠航行与拔锚()
    {
        var world = ArenaWorldBuilder.New().Build();
        world.Flow.Stage.ShouldBe(RunStage.NotStarted, "一句开始游戏都没按。");

        // 开局直接绕过贫城去富城：动词照常放行，段不改。
        ArenaSetup.Inject(world, "Dock", BigMapGrid.RichCity.Id).ShouldBeTrue("流程未开始也拦不住停靠。");
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * 12));
        world.IsDocked.ShouldBeTrue("真的停到富城了。");
        world.Flow.Stage.ShouldBe(RunStage.NotStarted, "没按开始游戏，段一动不动。");

        ArenaSetup.Inject(world, "PullAnchor", string.Empty).ShouldBeTrue("流程没开始也拦不住拔锚。");
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * 3));
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "防守收场了。");
        ArenaSetup.Inject(world, "SailTo", "210.5|300.25").ShouldBeTrue("手动航行也不看段。");
        world.Flow.Stage.ShouldBe(RunStage.NotStarted, "三发动词都发了，段还是未开始。");
    }

    [Fact]
    public void 检查点快照读得出段_归属城_下一站与四个里程碑步号()
    {
        var world = ArenaWorldBuilder.New().Build();
        ArenaSetup.Inject(world, "StartRun", string.Empty).ShouldBeTrue("先开局。");
        world.DockedAt(BigMapGrid.PoorCity.Id);

        world.Flow.NoteGluttonUnlocked(120);
        world.Flow.NoteFormSwitched(BeastForm.Glutton, 240);
        world.Flow.NoteTier2(360);
        world.Flow.NoteFinalVictory(480);

        var snapshot = world.DebugSnapshot();
        snapshot.ShouldContain("flow=poor", Case.Sensitive, "段那一格。");
        snapshot.ShouldContain("home=" + BigMapGrid.PoorCity.Id, Case.Sensitive, "归属城那一格。");
        snapshot.ShouldContain("next=" + BigMapGrid.DesertCity.Id, Case.Sensitive, "下一站那一格。");
        snapshot.ShouldContain("ms=glutton:120,switch:240,tier2:360,final:480", Case.Sensitive,
            "四个里程碑步号各一格。");

        // 没报过的那几档写 -1（键在、值在，读者一眼看得出「查过、确实没有」）。
        var fresh = ArenaWorldBuilder.New().Build();
        fresh.DebugSnapshot().ShouldContain("ms=glutton:-1,switch:-1,tier2:-1,final:-1", Case.Sensitive,
            "开局四个里程碑都没报过。");
        fresh.DebugSnapshot().ShouldContain("home=-", Case.Sensitive, "没归属城写短横。");
    }

    /// <summary>推到沙城段：开局 → 停靠贫城 → 拔锚防守 → 停靠沙城。</summary>
    private static void ReachSand(ArenaWorld world)
    {
        ArenaSetup.Inject(world, "StartRun", string.Empty).ShouldBeTrue("先开局。");
        world.DockedAt(BigMapGrid.PoorCity.Id);
        ArenaSetup.Inject(world, "PullAnchor", string.Empty).ShouldBeTrue("贫城拔锚。");
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * 3));
        world.Flow.Stage.ShouldBe(RunStage.ToSand, "起点推到第一段航行。");

        world.DockedAt(BigMapGrid.DesertCity.Id);
        world.Flow.Stage.ShouldBe(RunStage.Sand, "起点到沙城段。");
    }
}
