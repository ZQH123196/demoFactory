#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using Shouldly;
using Xunit;

namespace DemoFactory.Tests;

/// <summary>
/// 场子的<b>结构化事件出口</b>：typed 尾窗、按 payload 类型筛选、游标增量取数（各一条用例）。
///
/// 判的是出口递出来的那几样——<see cref="AbilityEvent"/> 的步号、主体、形状字段，
/// 以及游标那一格读数——不是事件流逐字相等（「断言比快照」）。
/// 尾窗与筛选两格都跟 <c>EventTailLines</c> 逐字对过一遍：两个出口看的是同一扇窗，
/// 出口是加法、不是改写。
///
/// 每一次出手发五条事件（本仓那份示范注册壳：清冷却 1 条、按下 1 条、落地 3 条），
/// 落地的 <c>DamageDealt</c> 在第 k+3 步（<see cref="SkillStageSample.StrikePreSteps"/> 步前摇）。
/// </summary>
public sealed class SkillStageEventExitTests
{
    private static SkillStage SampleStage(int seed = 1)
        => new(SkillStageSample.Spec(), seed);

    /// <summary>
    /// 递一份<b>环形容量压到 5 条</b>的注册口（其余各列与示范注册壳同形）。
    /// 容量是宿主定的观测窗口，公共层不给缺省——所以这里只能整份重写那份配置。
    /// </summary>
    private static SkillStageSpec SpecWithRingCapacity(int capacity)
    {
        var spec = SkillStageSample.Spec();
        return spec with
        {
            Roster = spec.Roster with
            {
                Config = new AbilityLayerConfig
                {
                    HealthAttribute = SkillStageSample.Attr.Vitality,
                    DamageTakenAttribute = SkillStageSample.Attr.IncomingScale,
                    CooldownAttributePrefix = SkillStageSample.Attr.CooldownPrefix,
                    RootedTag = SkillStageSample.Tag.Held,
                    DamageBlockTag = "training.warded",
                    DamageTakenEvent = SkillStageSample.Trigger.DamageTaken,
                    EffectAppliedEvent = SkillStageSample.Trigger.EffectApplied,
                    EventCapacity = capacity,
                },
            },
        };
    }

    /// <summary>
    /// 放一次完整出手（清冷却 → 按下 → 走完前摇）。
    /// 每次落地的步号由上一次放完时的步号决定，调用方靠它算期望值。
    /// </summary>
    private static void CastOnce(SkillStage stage)
    {
        stage.Inject(AbilityVerbs.ClearCooldown,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}");
        stage.Inject(AbilityVerbs.Cast,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}");
        stage.Advance(SkillStageSample.StrikePreSteps);
    }

    /// <summary>
    /// <b>E1 尾窗递 typed 事件、步号带得出来</b>。
    /// 两个出口对同一扇窗：条数相同、逐条念出的那一行也相同——
    /// 所以新出口是<b>加法</b>，既有的 <c>EventTailLines</c> 一个字没改（「约束唯一出处」 出口那一格）。
    /// </summary>
    [Fact]
    public void E1_尾窗递typed事件_步号主体与形状字段都带得出来()
    {
        var stage = SampleStage();
        CastOnce(stage);

        var typed = stage.EventsTail(64);
        typed.Select(e => e.Line).ShouldBe(stage.EventTailLines(64), Case.Sensitive,
            "同一扇窗的两扇门念出的行逐字相同 —— 新出口是加法，不是改写");

        // 按下那一刻就在第 0 步（前摇还没走完；1 步 = 0.1 秒，「时间基准」）。
        var pressed = typed.Single(e => e.Payload is AbilityActivatedAt);
        pressed.Tick.ShouldBe(0L, "按下那一刻发的那一条落在第 0 步");
        pressed.Subject.ShouldBe(SkillStageSample.CasterId, "主体是出手那一个");
        var activated = (AbilityActivatedAt)pressed.Payload;
        activated.AbilityId.ShouldBe(SkillStageSample.Skill.Strike, "形状字段带得出来：放的是哪一条技能");
        activated.Target.ShouldBe(SkillStageSample.DummyId, "形状字段带得出来：打的是哪一位");

        // 前摇 3 步 ⇒ 第 3 步走完 commit 并落地；伤害按出手强度 25 算（注册进来的那份纯函数）。
        var landed = typed.Single(e => e.Payload is DamageDealt);
        landed.Tick.ShouldBe(SkillStageSample.StrikePreSteps, "前摇 3 步，落地落在第 3 步");
        landed.Subject.ShouldBe(SkillStageSample.DummyId, "事件行的主体是挨打那一个（出手方在形状的 Source 里）");
        var damage = (DamageDealt)landed.Payload;
        damage.Final.ShouldBe(25.0, 0.001, "伤害按出手强度 25 算（纯函数那份注册进来的）");
        damage.Source.ShouldBe(SkillStageSample.CasterId, "形状字段带得出来：出手方是谁");
        damage.Via.ShouldBe(SkillStageSample.Fx.StrikeHit, "形状字段带得出来：经哪一条效果打过来的");
    }

    /// <summary>
    /// <b>E2 按 payload 类型筛选</b>：两次出手 ⇒ 两条 <see cref="DamageDealt"/>，步号带得出来。
    /// 单参那个筛选形状只给 payload 不给步号，表现层就没法答「这一下落在第几步」——
    /// 所以对外这一格给的是整个事件。
    /// </summary>
    [Fact]
    public void E2_按payload类型筛选_该类型的全给且步号带得出()
    {
        var stage = SampleStage();
        CastOnce(stage);     // 第 0 步按下，第 3 步落地
        CastOnce(stage);     // 第 3 步按下，第 6 步落地

        var damages = stage.EventsOf<DamageDealt>();
        damages.Count.ShouldBe(2, "放了两次 ⇒ 两次出手都落地了");
        damages.Select(e => e.Tick).ShouldBe(new long[] { 3, 6 },
            "步号带得出来 —— 这一格递的是整个事件，不是只给形状的那一个");
        damages.Select(e => e.Subject).ShouldBe(new[] { SkillStageSample.DummyId, SkillStageSample.DummyId },
            Case.Sensitive, "主体也带得出来");
        damages.Select(e => e.Line).ShouldBe(
            stage.EventsTail(64).Where(e => e.Payload is DamageDealt).Select(e => e.Line),
            Case.Sensitive, "筛出来的与从尾窗里挑出来的逐字相同（同一扇窗的两扇门）");

        // 谓词那一格：拿得到整个事件，所以能按步号收紧名单。
        var firstOnly = stage.EventsOf<DamageDealt>((e, hit) => e.Tick <= 3);
        firstOnly.Select(e => e.Tick).ShouldBe(new long[] { 3L },
            "谓词按步号收紧 ⇒ 只剩第一次出手那一条");

        // 别的类型那一格是空的：筛选不是「都给你」，是「只给这一类」。
        stage.EventsOf<ShapeHitLanded>().ShouldBeEmpty("示范注册壳不发扇形那一发，那一类一条都没有");
    }

    /// <summary>
    /// <b>E3 游标增量取数</b>：同一筛选在两步之间只给新增的那些，上一条不重给一遍。
    /// 不走游标那一格（<c>EventsOf</c>）每一步都拿全量——表现层正是靠这一格知道「这一帧有没有新东西」。
    /// </summary>
    [Fact]
    public void E3_同一筛选两步之间只给新增的那些()
    {
        var stage = SampleStage();
        stage.EventCursorOf<DamageDealt>().ShouldBe(0L, "还没读过这一类 ⇒ 游标停在 0");

        var idle = stage.EventsSinceCursor<DamageDealt>();
        idle.Events.ShouldBeEmpty("还没放技能 ⇒ 没有新事件");
        idle.DroppedSinceCursor.ShouldBe(0L, "什么也没挤掉");

        CastOnce(stage);
        var first = stage.EventsSinceCursor<DamageDealt>();
        first.Events.Select(e => e.Tick).ShouldBe(new long[] { 3L },
            "两发之间只多了一次出手的那一条");
        first.DroppedSinceCursor.ShouldBe(0L, "环形容量 2000 条 ⇒ 这一段一条也没挤掉");

        CastOnce(stage);
        var second = stage.EventsSinceCursor<DamageDealt>();
        second.Events.Select(e => e.Tick).ShouldBe(new long[] { 6L },
            "只给新增的那一条；上一步读过的第 3 步那条不重给一遍");

        // 读数口：游标读到哪里念得出来。
        stage.EventCursorOf<DamageDealt>().ShouldBe(second.TotalNow,
            "读完游标推到当下（换一局时它归零，所以这一格念得出「读到哪了」）");
        stage.EventCursorOf<DamageDealt>().ShouldBeGreaterThan(0L, "确实推过，不是停在 0");
    }

    /// <summary>
    /// <b>E4 环形挤掉的那一段读得出来</b>：环形容量压到 5 条（正好一次出手的条数），
    /// 游标之后再放两次出手 ⇒ 那十条里环形只留得住后五条，前五条读不出来。
    /// <b>这一格是本用例的要点</b>：不报的话，画面上只看到「这一段只多了一条出手」，
    /// 而真相是这一段新发了十条、其中九条读不出来——「日志里没有」≠「没发生」。
    /// </summary>
    [Fact]
    public void E4_中间被环形挤掉的_游标那一格读得出来()
    {
        var stage = new SkillStage(SpecWithRingCapacity(5));
        CastOnce(stage);                 // 五条事件，环形刚好装下

        var before = stage.EventsSinceCursor<DamageDealt>();
        before.Events.Select(e => e.Tick).ShouldBe(new long[] { 3L },
            "容量还够时那一条读得出来");
        before.DroppedSinceCursor.ShouldBe(0L, "第一次读完环形还没挤掉东西");

        CastOnce(stage);                 // 第 6~10 条：环形只剩 5 个位置，这五条全被挤掉
        CastOnce(stage);                 // 第 11~15 条：留在环形里的就是这一次的五条
        var after = stage.EventsSinceCursor<DamageDealt>();

        after.DroppedSinceCursor.ShouldBe(5L,
            "游标停在第 5 条之后发过 10 条，环形只留得住 5 条 ⇒ 中间那 5 条读不到了，这一格念得出");
        after.Events.Select(e => e.Tick).ShouldBe(new long[] { 9L },
            "给的是还留得住的那一条；被挤掉的第 6 步那次出手给不出来");

        // 与既有读数那一格对得上：两条不同的读法，数字必须一致（顺带钉住读数格式没被改）。
        stage.Readout().ShouldBe("events total=15 kept=5 dropped=10 capacity=5",
            "读数那一格逐字不变，且与游标那一格说的是同一件事");
        stage.DebugSnapshot().Split('\n').Last().ShouldBe(stage.Readout(),
            "快照末行还是读数那一行，快照没被这一改带动");

        // 挤掉之后游标照样推到当下，下一步起只给真正新增的。
        stage.EventCursorOf<DamageDealt>().ShouldBe(15L, "游标推到当下累计 15 条");
        stage.EventsSinceCursor<DamageDealt>().Events.ShouldBeEmpty("再读一次没有新的（不重演被挤掉的那几条）");
    }

    /// <summary>
    /// <b>E5 换局归零</b>：<see cref="SkillStage.ResetEventCursor"/> 把游标推回 0，
    /// 下一次读从头再给一遍——换局重挂的表现层靠这一发清干净自己那份「读过的位置」。
    /// </summary>
    [Fact]
    public void E5_换局归零_下一次读从头再给一遍()
    {
        var stage = SampleStage();
        CastOnce(stage);
        stage.EventsSinceCursor<DamageDealt>().Events.Count.ShouldBe(1, "先读一遍，游标推进");
        stage.EventCursorOf<DamageDealt>().ShouldBeGreaterThan(0L, "游标确实推过");
        stage.EventsSinceCursor<DamageDealt>().Events.ShouldBeEmpty("读干净了，没有新的");

        stage.ResetEventCursor();
        stage.EventCursorOf<DamageDealt>().ShouldBe(0L, "归零那一发念得出来");

        var replayed = stage.EventsSinceCursor<DamageDealt>();
        replayed.Events.Select(e => e.Tick).ShouldBe(new long[] { 3L },
            "归零后从头再给一遍 —— 换局重挂的那一份靠这一发清干净");
        replayed.DroppedSinceCursor.ShouldBe(0L, "从头读，环形里那几条都还在，没挤掉");

        // 别的类型各记各的游标：清一份不动另一份。
        stage.EventsSinceCursor<AbilityActivatedAt>().Events.Select(e => e.Tick).ShouldBe(new long[] { 0L },
            "另一个类型还没读过 ⇒ 它那份游标还是 0，头一次读就给全");
        stage.ResetEventCursor();
        stage.EventsSinceCursor<DamageDealt>().DroppedSinceCursor.ShouldBe(0L,
            "从头读不报挤掉：环形里留得住的那些都还在");
    }

    /// <summary>
    /// <b>E6 筛选的两种形状与尾窗同源</b>：三层递出口互相对齐——
    /// 不给谓词给这一类的全部、给谓词收紧、尾窗给最近 n 条，三者都是同一扇窗。
    /// </summary>
    [Fact]
    public void E6_三层出口同源_筛选的两种形状与尾窗对得上()
    {
        var stage = SampleStage();
        CastOnce(stage);
        CastOnce(stage);

        var window = stage.EventsTail(64);
        var typed = stage.EventsOf<DamageDealt>();
        typed.Select(e => e.Line).ShouldBe(window.Where(e => e.Payload is DamageDealt).Select(e => e.Line),
            Case.Sensitive, "筛选与尾窗同源：同一条事件两边念出的行逐字相同");

        // 尾窗取最近 3 条：里面该有的就是最后一次出手那三条。
        var last = stage.EventsTail(3);
        last.Select(e => e.Tick).ShouldBe(new[] { 6L, 6L, 6L },
            "最近 3 条全在第 6 步（最后一次出手的落地那三条）");
        last.Select(e => e.Payload).ShouldBe(
            new EventPayload[] { new DamageDealt(25.0, SkillStageSample.CasterId, SkillStageSample.Fx.StrikeHit),
                new EffectAppliedInstantly(SkillStageSample.Fx.StrikeHit),
                new AbilityCommitted(SkillStageSample.Skill.Strike) },
            "落地的次序：伤害 → 效果即时生效 → commit（层里那三发的次序）");
    }
}
