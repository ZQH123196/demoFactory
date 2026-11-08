#nullable enable
using System.Collections.Generic;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 一局流程那一件：段推进、目标与下一站、双成长完成、最终防守资格、四个里程碑步号。
///
/// <para><b>这一份钉的是「能脱离世界单独活」</b>：不造世界，直接 <see cref="RunFlow"/>
/// 构造、喂步号与「停靠 / 拔锚 / 防守收场」三个事实。世界那一侧的接线
/// （开局位置、<c>StartRun</c> 动词、防守收场报到流程）由 <c>RunFlowWorldTests</c> 钉着。</para>
/// </summary>
public class RunFlowTests
{
    private const long SomeTick = 120;

    private static RunFlow NewFlow(out BeastGrowth growth)
    {
        growth = new BeastGrowth();
        return new RunFlow(growth);
    }

    [Fact]
    public void 单独构造_未开始_目标是按下开始游戏()
    {
        var flow = NewFlow(out _);

        flow.Stage.ShouldBe(RunStage.NotStarted, "开局未开始。");
        flow.HomeCityId.ShouldBe(string.Empty, "还没有归属城。");
        flow.NextCityId.ShouldBe(BigMapGrid.PoorCity.Id, "第一站是贫城。");
        flow.Goal.ShouldNotBeNullOrWhiteSpace("未开始也有目标文案。");
        flow.DoubleGrowthComplete.ShouldBeFalse("一阶、未解锁、没切过。");
        flow.FinalDefenceReady.ShouldBeFalse("不在富城段，谈不上终局资格。");
        flow.GluttonUnlockedTick.ShouldBe(-1, "还没解锁暴食态。");
        flow.FirstSwitchTick.ShouldBe(-1, "还没手动切过。");
        flow.Tier2Tick.ShouldBe(-1, "二阶里程碑没报过。");
        flow.FinalVictoryTick.ShouldBe(-1, "最终胜利没报过。");
    }

    [Fact]
    public void 开始游戏_只从末开始推进贫城段_目标与下一站跟着换()
    {
        var flow = NewFlow(out _);
        var stages = new List<RunStage>();
        var goals = new List<string>();
        flow.PhaseEntered += stages.Add;
        flow.GoalChanged += goals.Add;

        var accepted = flow.Start();

        accepted.ShouldBeTrue("未开始那一档发得出去。");
        flow.Stage.ShouldBe(RunStage.ToPoor, "开始游戏只推到开局航程，不直接算进港。");
        flow.HomeCityId.ShouldBe(BigMapGrid.PoorCity.Id, "这一段的归属城是贫城。");
        flow.NextCityId.ShouldBe(BigMapGrid.PoorCity.Id, "下一站还是贫城：玩家要自己停进去。");

        stages.Count.ShouldBe(1, "段进入事件在状态变更之后发一次。");
        stages[0].ShouldBe(RunStage.ToPoor, "只推进到开局航程。");
        goals.Count.ShouldBe(1, "目标随段换了，也只发一次。");
        goals[0].ShouldBe(flow.Goal, "事件参数就是改完之后的目标文案。");
    }

    [Fact]
    public void 再按一次开始游戏_被拒_段与事件都不动()
    {
        var flow = NewFlow(out _);
        flow.Start();

        var fired = 0;
        flow.PhaseEntered += _ => fired++;

        flow.Start().ShouldBeFalse("已经在局里了。");
        flow.Stage.ShouldBe(RunStage.ToPoor, "段不倒退也不重来。");
        fired.ShouldBe(0, "被拒的那一发不发事件。");
    }

    [Fact]
    public void 贫城到沙城到富城_按站推进_只有该到的那座城才推段()
    {
        var flow = NewFlow(out _);
        flow.Start();

        // 开局航程里顺路停富城与野外锚地都不算「到了贫城」。
        flow.NoteDocked(BigMapGrid.WildAnchor.Id).ShouldBeFalse("野外锚地不是下一站。");
        flow.NoteDocked(BigMapGrid.RichCity.Id).ShouldBeFalse("富城不是这一段的下一站。");
        flow.Stage.ShouldBe(RunStage.ToPoor, "顺路停靠不改段。");

        flow.NoteDocked(BigMapGrid.PoorCity.Id).ShouldBeTrue("到了贫城，进贫城段。");
        flow.Stage.ShouldBe(RunStage.Poor, "贫城段。");
        flow.HomeCityId.ShouldBe(BigMapGrid.PoorCity.Id, "归属城是贫城。");
        flow.NextCityId.ShouldBe(BigMapGrid.DesertCity.Id, "下一站是沙城。");

        // 贫城赢了才进第一段航行；航行中回旧城不改段。
        flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.ToSand, "贫城防守胜利 → 第一段航行。");
        flow.NextCityId.ShouldBe(BigMapGrid.DesertCity.Id, "下一站是沙城。");

        flow.NoteDocked(BigMapGrid.PoorCity.Id).ShouldBeFalse("回旧城不改段。");
        flow.Stage.ShouldBe(RunStage.ToSand, "仍在第一段航行。");

        flow.NoteDocked(BigMapGrid.DesertCity.Id).ShouldBeTrue("到了沙城才进沙城段。");
        flow.Stage.ShouldBe(RunStage.Sand, "沙城段。");
        flow.HomeCityId.ShouldBe(BigMapGrid.DesertCity.Id, "归属城是沙城。");
        flow.NextCityId.ShouldBe(BigMapGrid.RichCity.Id, "下一站是富城。");

        flow.NoteAnchorPulled(BigMapGrid.DesertCity.Id);
        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.ToRich, "沙城防守胜利 → 第二段航行。");

        flow.NoteDocked(BigMapGrid.RichCity.Id).ShouldBeTrue("到了富城才进富城段。");
        flow.Stage.ShouldBe(RunStage.Rich, "富城段。");
        flow.NextCityId.ShouldBe(string.Empty, "富城是终点，没有下一站。");
    }

    [Fact]
    public void 城破不算防守胜利_段停在原地_补救之后再拔一次赢了才推()
    {
        var flow = NewFlow(out _);
        flow.Start();
        flow.NoteDocked(BigMapGrid.PoorCity.Id);

        flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        flow.NoteDefenceSettled(won: false);
        flow.Stage.ShouldBe(RunStage.Poor, "城破那一场不算赢，段停在贫城。");

        // 补救之后再停靠一次（同一座城）、再拔一次锚，赢了才推。
        flow.NoteDocked(BigMapGrid.PoorCity.Id).ShouldBeFalse("同一段再停一次不改段。");
        flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.ToSand, "补打的那一场赢了才进第一段航行。");
    }

    [Fact]
    public void 偏离路线_停靠拔锚防守都不改段_回到主线照常推进()
    {
        var flow = NewFlow(out _);
        flow.Start();
        flow.NoteDocked(BigMapGrid.PoorCity.Id);
        flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.ToSand, "先到第一段航行这一档。");

        // 航行中绕去富城：停靠、拔锚、防守赢了，都不改段。
        flow.NoteDocked(BigMapGrid.RichCity.Id).ShouldBeFalse("富城不是这一段的下一站。");
        flow.NoteAnchorPulled(BigMapGrid.RichCity.Id);
        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.ToSand, "在富城打赢防守也推不动这一段。");

        // 去野外锚地停靠再走，同样不改段；回到主线下一站照常推进。
        flow.NoteDocked(BigMapGrid.WildAnchor.Id).ShouldBeFalse("野外锚地不改段。");
        flow.NoteDocked(BigMapGrid.DesertCity.Id).ShouldBeTrue("回到主线，沙城照常推进。");
        flow.Stage.ShouldBe(RunStage.Sand, "沙城段。");
    }

    [Fact]
    public void 富城拔锚_双成长未完成只打普通防守_收场之后段留在富城()
    {
        var flow = NewFlow(out var growth);
        ReachRich(flow, growth);

        flow.FinalDefenceReady.ShouldBeFalse("双成长没完成，不够格。");
        flow.NoteAnchorPulled(BigMapGrid.RichCity.Id);

        flow.Stage.ShouldBe(RunStage.NormalDefence, "只打普通防守。");

        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.Rich, "普通防守收场回富城段。");
        flow.HomeCityId.ShouldBe(BigMapGrid.RichCity.Id, "归属城还是富城。");
    }

    [Fact]
    public void 双成长完成才够格_富城拔锚进最终防守_赢了进结算_继续经营回自由()
    {
        var flow = NewFlow(out var growth);
        ReachRich(flow, growth);
        CompleteDoubleGrowth(flow, growth, tick: 900);

        flow.DoubleGrowthComplete.ShouldBeTrue("二阶 + 暴食解锁 + 手动切过一次。");
        flow.FinalDefenceReady.ShouldBeTrue("在富城且双成长完成。");

        var settlements = 0;
        var freePlays = 0;
        flow.SettlementOpened += () => settlements++;
        flow.FreePlayEntered += () => freePlays++;

        flow.NoteAnchorPulled(BigMapGrid.RichCity.Id);
        flow.Stage.ShouldBe(RunStage.FinalDefence, "双成长完成，富城拔锚进最终防守。");

        // 失败重试：段留在最终防守（重试本身不在这一件里）。
        flow.NoteDefenceSettled(won: false);
        flow.Stage.ShouldBe(RunStage.FinalDefence, "最终防守失败留在原地等重试。");
        settlements.ShouldBe(0, "没赢不开结算。");

        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.Settlement, "最终防守胜利进结算。");
        settlements.ShouldBe(1, "结算打开只发一次。");

        flow.ContinueBusiness().ShouldBeTrue("结算屏上「继续经营」那一发。");
        flow.Stage.ShouldBe(RunStage.FreePlay, "回到自由经营。");
        freePlays.ShouldBe(1, "继续经营发一次事件。");

        // 自由经营里富城再拔一次锚：照状态图仍走普通防守。
        flow.NoteAnchorPulled(BigMapGrid.RichCity.Id);
        flow.Stage.ShouldBe(RunStage.NormalDefence, "继续经营之后富城再拔锚走普通防守。");
        flow.ContinueBusiness().ShouldBeFalse("不在结算那一档，继续经营发不出去。");
    }

    [Fact]
    public void 富城拔锚之后双成长才补完_这一场仍是普通防守_下一次才够格()
    {
        var flow = NewFlow(out var growth);
        ReachRich(flow, growth);

        flow.NoteAnchorPulled(BigMapGrid.RichCity.Id);
        flow.Stage.ShouldBe(RunStage.NormalDefence, "拔锚那一刻就判过资格了。");

        CompleteDoubleGrowth(flow, growth, tick: 1200);
        flow.Stage.ShouldBe(RunStage.NormalDefence, "打着的那一场不因中途补完而改档。");

        flow.NoteDefenceSettled(won: true);
        flow.Stage.ShouldBe(RunStage.Rich, "回到富城段。");

        flow.NoteAnchorPulled(BigMapGrid.RichCity.Id);
        flow.Stage.ShouldBe(RunStage.FinalDefence, "这一次双成长已完成，进最终防守。");
    }

    [Fact]
    public void 双成长三项各缺一项都不算完成()
    {
        var growth = new BeastGrowth();
        var flow = new RunFlow(growth);

        flow.DoubleGrowthComplete.ShouldBeFalse("三项全缺。");

        // 只解锁暴食态：还在一阶，不算。
        growth.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("喂食凑够阈值就解锁。");
        flow.NoteGluttonUnlocked(tick: 5);
        flow.DoubleGrowthComplete.ShouldBeFalse("还在一阶，不算。");

        // 二阶 + 暴食解锁 + 没手动切过。
        growth.BeginRitual(0, 1);
        growth.SettleRitual(1);
        growth.Tier.ShouldBe(BeastTier.Tier2, "仪式走完到了二阶。");
        flow.DoubleGrowthComplete.ShouldBeFalse("还没手动切过暴食态。");

        flow.NoteFormSwitched(BeastForm.Glutton, tick: 20);
        flow.DoubleGrowthComplete.ShouldBeTrue("三项齐了。");

        // 荒漠态不要求解锁，当前形态也不要求停在暴食态。
        growth.SwitchForm(BeastForm.Original);
        flow.DoubleGrowthComplete.ShouldBeTrue("切回原始态不影响完成判定。");
    }

    [Fact]
    public void 四个里程碑各记一次步号_重复上报不改读数也不重复发事件()
    {
        var flow = NewFlow(out _);
        var seen = new List<(RunMilestone Kind, long Tick)>();
        flow.MilestoneReached += (kind, tick) => seen.Add((kind, tick));

        flow.NoteGluttonUnlocked(300);
        flow.NoteGluttonUnlocked(999);
        flow.NoteFormSwitched(BeastForm.Desert, 310);
        flow.NoteFormSwitched(BeastForm.Glutton, 320);
        flow.NoteFormSwitched(BeastForm.Glutton, 999);
        flow.NoteTier2(400);
        flow.NoteFinalVictory(500);

        flow.GluttonUnlockedTick.ShouldBe(300, "解锁只记第一次。");
        flow.FirstSwitchTick.ShouldBe(320, "切到荒漠态不算，第一次切暴食才记。");
        flow.Tier2Tick.ShouldBe(400, "二阶留口照记。");
        flow.FinalVictoryTick.ShouldBe(500, "最终胜利留口照记。");

        seen.Count.ShouldBe(4, "四个里程碑各发一次。");
        seen[0].ShouldBe((RunMilestone.GluttonUnlocked, 300L), "第一次上报就发事件。");
        seen[3].ShouldBe((RunMilestone.FinalVictory, 500L), "最终胜利那一发。");
    }

    /// <summary>推到富城段：开始 → 贫城 → 第一段 → 沙城 → 第二段 → 富城。</summary>
    private static void ReachRich(RunFlow flow, BeastGrowth growth)
    {
        flow.Start();
        flow.NoteDocked(BigMapGrid.PoorCity.Id);
        flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        flow.NoteDefenceSettled(won: true);
        flow.NoteDocked(BigMapGrid.DesertCity.Id);
        flow.NoteAnchorPulled(BigMapGrid.DesertCity.Id);
        flow.NoteDefenceSettled(won: true);
        flow.NoteDocked(BigMapGrid.RichCity.Id);

        flow.Stage.ShouldBe(RunStage.Rich, "起点推到富城段。");
        growth.ShouldNotBeNull();
    }

    /// <summary>把三项摆齐：二阶仪式走完、暴食态解锁、手动切一次暴食态。</summary>
    private static void CompleteDoubleGrowth(RunFlow flow, BeastGrowth growth, long tick)
    {
        growth.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        growth.CheckUnlocks();
        flow.NoteGluttonUnlocked(tick);

        growth.BeginRitual(tick, 1);
        growth.SettleRitual(tick + 1);

        growth.SwitchForm(BeastForm.Glutton);
        flow.NoteFormSwitched(BeastForm.Glutton, tick + 2);
    }
}
