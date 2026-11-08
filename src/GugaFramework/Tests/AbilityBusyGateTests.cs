#nullable enable
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using Shouldly;
using Xunit;

namespace DemoFactory.Tests;

/// <summary>
/// 层里 <c>Busy</c> 那一档的钉子 —— <b>前摇里再按一次</b>。
///
/// <para><b>为什么单独一条</b>：有一个宿主那四条普攻全是零前摇（<c>PreSteps = 0</c>），
/// 而零前摇那一档在按下那一步就过 commit（<c>AbilityStepper.TryActivate</c> 里那一发），
/// 于是那一侧的「同一步再按一次」读到的是 <c>Cooldown</c> 而不是 <c>Busy</c> ——
/// <b>那边够不到这一档</b>。前摇非零的技能在别处还有的是（本文件用的清单注册壳就是），
/// 所以这一档由公共层测试线钉住：换原型、换技能表都不会把它悄悄改成排队或静默吞掉。</para>
///
/// <para><b>用哪一条技能</b>：<see cref="StageChecklistShell.Skill"/>.<see cref="StageChecklistShell.Skill.Windup"/>，
/// 前摇 <see cref="StageChecklistShell.WindupPreSteps"/> 步（3 步，远大于 1，同一步里按第二下时它一定还在放）。
/// 这一条还带消耗与冷却，所以「第二下没扣资源、没起第二次前摇」也一起读得出来。</para>
///
/// <para><b>断的是三件事</b>：① 理由有名字（<c>Busy</c>，不是一个光秃秃的 false）；
/// ② 不是排队（<c>QueuedInput</c> 必须还是 false —— 排队那一档只在冷却中收）；
/// ③ 没有重复结算（同一步里资源只扣一次、前摇剩余步没被重置）。</para>
/// </summary>
public sealed class AbilityBusyCheckTests
{
    /// <summary>出手那一个在射程里按一下：过了激活检查、进前摇。</summary>
    private static SkillStage StageWithWindupStarted()
    {
        var stage = StageChecklistShell.NewStage();
        stage.Stepper.TryActivate(StageChecklistShell.CasterId, StageChecklistShell.Skill.Windup,
            StageChecklistShell.TargetId).ShouldBeTrue("排布前提：这一发在射程里，激活检查该放行");
        return stage;
    }

    [Fact]
    public void 前摇里同一步再按一次_理由是有名字的Busy()
    {
        var stage = StageWithWindupStarted();
        var body = Body(stage);
        body.Phase.ShouldBe(AbilityPhase.Pre, "排布前提：第一发正在前摇里（还没过 commit）");

        var again = stage.Stepper.TryActivate(StageChecklistShell.CasterId, StageChecklistShell.Skill.Windup,
            StageChecklistShell.TargetId, out var reason);

        again.ShouldBeFalse("前摇里这一发不该放出去");
        reason.ShouldBe(AbilityRejectReason.Busy, "「还在前摇里」要有名字：不是排队、不是静默吞掉");
        body.RejectedReason.ShouldBe(AbilityRejectReason.Busy, "理由也要落在身上那一格（快照与调试读它）");

        // 拒绝要留一行（「事件发射前置」：拒绝不许静默）
        var rejects = new List<AbilityRejected>();
        foreach (var line in stage.Stepper.World.Events.Tail(50))
        {
            if (line.Payload is AbilityRejected rejected) rejects.Add(rejected);
        }

        rejects.Count(r => r.Reason == AbilityRejectReason.Busy)
            .ShouldBe(1, "被拒那一发在行为流里读得出，且只留一行（理由是有名字的 Busy）");
        rejects.Single(r => r.Reason == AbilityRejectReason.Busy).AbilityId
            .ShouldBe(StageChecklistShell.Skill.Windup, "留的那一行是这条技能");
    }

    [Fact]
    public void 前摇里再按一次_不进排队口也不重复结算()
    {
        var stage = StageWithWindupStarted();
        var body = Body(stage);
        var focusAfterFirst = Focus(stage);

        stage.Stepper.TryActivate(StageChecklistShell.CasterId, StageChecklistShell.Skill.Windup,
            StageChecklistShell.TargetId).ShouldBeFalse();

        body.QueuedInput.ShouldBeFalse("排队那一档只在冷却中收 —— 前摇里按的不许被记成「排一个」");
        body.StepsRemaining.ShouldBe(StageChecklistShell.WindupPreSteps,
            "前摇剩余步一个都没动（按下即登记、下一步的 S3 才递减；第二下没重启也没推进它）");
        Focus(stage).ShouldBe(focusAfterFirst, "消耗只扣了一次：第二下没过激活检查，一个字节都不该动");
    }

    /// <summary>出手那一个身上那一条技能的状态。</summary>
    private static AbilityOnBody Body(SkillStage stage)
        => stage.Find(StageChecklistShell.CasterId)!.Abilities[StageChecklistShell.Skill.Windup];

    /// <summary>出手那一个这一格还剩多少资源（消耗那一列）。</summary>
    private static double Focus(SkillStage stage)
        => stage.Find(StageChecklistShell.CasterId)!.Current[StageChecklistShell.Attr.Focus];
}
