#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Replay;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 单挑场 H2 的数值台账 —— 「16 条清单」P2 那行的完成口径：<b>每条技能一份可手算的台账</b>，
/// 外加已删的设计档那四笔落点各指着一条断言（编号写在每句 <c>Assert</c> 的注释里）。
///
/// 台账口径：默认场子是「英雄 100 血 20 攻 4 速、木桩 100 血 10 攻、隔 2 米、抖动关」；
/// 平A 每 20 步（2 秒）一下 10 血。所有周期都取 0.5 秒的整数倍（「16 条清单」那条纪律），
/// 所以每一格都能拿纸算出来——红了要能一口说出差在哪一步、差在哪一笔。
///
/// 编号分两段：A31~A54 是第一批八条 + 四笔落点 + 两条通用性的断言（2026-09-27/28 两轮），
/// <b>A55~A63 是第二批这六条</b>（2026-09-28 本轮：<c>tank.guard</c> / <c>warrior.execute</c> /
/// <c>warrior.bleed</c> / <c>archer.rain</c> / <c>archer.mark</c> / <c>rider.joust</c>）；
/// 第二批剩下的两格（<c>tank.slam</c> 的正前 120°、<c>rider.charge</c> 的位移）按用户确认的口径落 H3，
/// 台账在 <see cref="AbilityH3DuelTests"/> 那一侧。
/// </summary>
public class AbilityH2LedgerTests
{
    private static readonly string Hero = SkillDuelRift.HeroId;
    private static readonly string Dummy = SkillDuelRift.DummyId;

    private static SkillTestWorld.Tuning Static => new()
    {
        Enemy = SkillTestWorld.EnemyStance.Static,
    };

    private static SkillTestWorld.Tuning Sparring(long interval = 20) => new()
    {
        Enemy = SkillTestWorld.EnemyStance.BasicAttack,
        BasicAttackIntervalTicks = interval,
    };

    // === 落点 1｜issue 08 抵挡 + 落点 2｜N-15 周期回充（断言 A31 / A32 / A33）===

    /// <summary>
    /// <b>A31（钉 issue 08 那笔落点）</b>：S0 扣血落地<b>前</b>那第三处激活检查问——
    /// 挂着 <c>status.block_ready</c> 的那一发伤害为 0、并且层数减 1。
    /// 台账：平A 第 20 步那一发被吃掉（血不动、blocked=1、charges 1→0），第 40 步没有层了照扣 10。
    /// </summary>
    [Fact]
    public void A31_抵挡吃掉这次伤害_并且层数减一()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroPassives: new[] { SkillDuelRift.Fx.SteadyCharges });
        world.Stacks(Hero, SkillDuelRift.Fx.SteadyCharges).ShouldBe(1, "开局一层抵挡");
        world.Snapshot().ShouldContain("charges=1", Case.Sensitive, "快照念得出 charges=（状态可查）");

        world.Advance(20);
        world.Current(Hero, SkillDuelRift.Attr.Health).ShouldBe(100, "第 20 步那一发被吃掉");
        world.Counter(Hero, "damage_blocked").ShouldBe(1, "吃掉几次读得出来");
        world.Stacks(Hero, SkillDuelRift.Fx.SteadyCharges).ShouldBe(0, "消耗的就是那一层");
        world.Snapshot().ShouldContain("charges=0", Case.Sensitive, "0 层也念得出（打光了、等在回充）");

        world.Advance(20);   // 第 40 步：没有层可消耗 → 这一发照扣
        world.Current(Hero, SkillDuelRift.Attr.Health).ShouldBe(90, "第二发打穿");
        world.Counter(Hero, "damage_taken_total").ShouldBe(10, "累计承伤只数真打进来的");
        world.Counter(Hero, "damage_blocked").ShouldBe(1, "第 40 步没挡住，计数不动");
    }

    /// <summary>
    /// <b>A32（钉 N-15 那笔落点）</b>：周期把层数加回去、封顶 1、快照念得出 <c>charges=</c>。
    /// 台账（平A 每 20 步 / 回充每 60 步）：挡 20 → 穿 40 → <b>回充与平A 撞在同一步的 60 被挡住</b>
    /// → 穿 80 → 穿 100 → 回充在 120 又挡住。段序（S3 的回充在 S1/S2 之后、场子的平A 在整步之后）
    /// 就在这里现形：60 那一步是「先回满、再挨打」。
    /// </summary>
    [Fact]
    public void A32_周期把层数加回去_封顶一_同一步先回满再挨打()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroPassives: new[] { SkillDuelRift.Fx.SteadyCharges });

        world.Advance(40);
        world.Stacks(Hero, SkillDuelRift.Fx.SteadyCharges).ShouldBe(0, "20 挡掉、40 打穿后仍是 0 层");

        world.Advance(20);   // 到第 60 步：S3 回充 0→1，紧接着这一发平A 被挡
        world.Counter(Hero, "charge_recovered").ShouldBe(1, "回充真发生了，且记在历史账上");
        world.Current(Hero, SkillDuelRift.Attr.Health).ShouldBe(90, "60 那一发又被挡住");
        world.Counter(Hero, "damage_blocked").ShouldBe(2);

        world.Advance(40);   // 80、100 两发：没有层 → 照扣
        world.Current(Hero, SkillDuelRift.Attr.Health).ShouldBe(70, "80/100 两发打穿");
        world.Stacks(Hero, SkillDuelRift.Fx.SteadyCharges).ShouldBe(0);

        world.Advance(20);   // 到第 120 步：回充第二次 → 挡住
        world.Counter(Hero, "charge_recovered").ShouldBe(2);
        world.Stacks(Hero, SkillDuelRift.Fx.SteadyCharges).ShouldBe(0, "挡住后又回到 0 层");
        world.Current(Hero, SkillDuelRift.Attr.Health).ShouldBe(70, "封顶 1 层：多的那一发才扣得动");
        world.Counter(Hero, "charge_recharge_capped").ShouldBe(0, "全程没有「满层还硬回」的那一跳——封顶这一格是守住的");
    }

    /// <summary>
    /// <b>A33（抵挡与承伤倍率的先后）</b>：抵挡排在倍率<b>之前</b>问——被挡掉的那一发不乘倍率、
    /// 也不触发受击事件（怒意那条被动跟着不动）。
    /// </summary>
    [Fact]
    public void A33_挡住的那一发不乘倍率也不发受击事件()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroPassives: new[]
        {
            SkillDuelRift.Fx.SteadyCharges, SkillDuelRift.Fx.RageOnHit,
        });

        world.Advance(20);
        world.Current(Hero, SkillDuelRift.Attr.Rage).ShouldBe(0, "挡住 → 没有受击事件，怒不加");
        world.Counter(Hero, "damage_blocked").ShouldBe(1);

        world.Advance(20);   // 第 40 步打穿：伤害落地 + 下一步触发加怒
        world.Advance(1);
        world.Current(Hero, SkillDuelRift.Attr.Rage).ShouldBe(8, "打穿的那一发才走事件口");
        world.Current(Hero, SkillDuelRift.Attr.Health).ShouldBe(90);
    }

    // === 落点 3｜N-16 排队释放（断言 A34 / A35 / A36）===

    /// <summary>
    /// <b>A34（钉 N-16 那笔落点）</b>：冷却中按下不丢，快照念得出 <c>q=1</c>；
    /// 冷却转好的<b>同一步</b>由 S4 的同一个入口补放，读完清零。
    /// 台账：第 5 步按下 → 第 8 步落 20 血、冷却到第 28 步；第 10 步再按 → 拒（Cooldown）+ 排队；
    /// 第 28 步补放 → 前摇 3 步 → 第 31 步落第二刀。
    /// </summary>
    [Fact]
    public void A34_冷却中排队_转好同一步补放并清零()
    {
        var world = SkillDuelRift.Stage(Sparring(),
            heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            dummyUnkillable: true);

        world.Advance(5);
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}").ShouldBeTrue();
        world.Advance(2);
        world.Counter(Dummy, "damage_taken_total").ShouldBe(0, "前摇没走完不该结算（第 8 步才落）");
        world.Advance(1);   // 到第 8 步：过 commit 落那一刀，冷却倒到第 28 步
        world.Counter(Dummy, "damage_taken_total").ShouldBe(20, "满血那一刀 = 20");
        world.Ability(Hero, SkillDuelRift.Skill.Rift).Phase.ShouldBe(AbilityPhase.Cooling);

        world.Advance(2);   // 第 10 步：冷却中再按
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}").ShouldBeFalse();
        world.Ability(Hero, SkillDuelRift.Skill.Rift).QueuedInput.ShouldBeTrue("排上了（上限 1）");
        world.Snapshot().ShouldContain("q=1", Case.Sensitive, "排队这件事进快照（D2 状态可查）");

        world.Advance(18);   // 到第 28 步：S1 放回 Ready，S4 读队补放
        QueueDrainCount(world).ShouldBe(1, "形状 queue_drain（QueuedCastTakenAt / QueuedCastTaken）：转好那一步补放一次，没转好之前一直留着");
        world.Ability(Hero, SkillDuelRift.Skill.Rift).QueuedInput.ShouldBeFalse("读一次就清零");

        world.Advance(3);   // 第 31 步落第二刀：此刻英雄 90 血（第 20 步挨过一发）
        world.Counter(Dummy, "damage_taken_total").ShouldBe(43, "第二刀 = 20 ×(1 + 10/100 × 1.5) = 23 ⇒ 累计 43");
    }

    /// <summary>
    /// <b>A35（排队只排一个）</b>：连按三次只补放一次——队列上限 1，不许攒出第二条路。
    /// </summary>
    [Fact]
    public void A35_队列上限一_连按三次只补一刀()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            dummyUnkillable: true);
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
        world.Advance(8);
        for (var i = 0; i < 3; i++)
        {
            world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}").ShouldBeFalse();
        }

        world.Advance(20);   // 到第 28 步补放，再往后是补放后的冷却
        QueueDrainCount(world).ShouldBe(1, "形状 queue_drain：只补放一次");
        world.Advance(3);
        world.Counter(Dummy, "damage_taken_total").ShouldBe(43, "累计还是两刀");
    }

    /// <summary>
    /// <b>A36（补放又被拒也认栽）</b>：排队补放走的是同一个激活检查——目标走远了就是
    /// <c>NotInRange</c>，不留着每步重试（否则「玩家按的」与「补放的」会漂成两套）。
    /// </summary>
    [Fact]
    public void A36_补放又被拒_不留队列每步重试()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroAbilities: new[] { SkillDuelRift.Skill.Rift });
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
        world.Advance(10);
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");   // 排上
        world.Inject("Move", $"{Dummy}|10");   // 目标走出 3 米射程

        world.Advance(18);   // 第 28 步补放 → NotInRange
        world.Ability(Hero, SkillDuelRift.Skill.Rift).QueuedInput.ShouldBeFalse();
        world.Ability(Hero, SkillDuelRift.Skill.Rift).RejectedReason.ShouldBe(AbilityRejectReason.NotInRange, "为什么没放要有名字");
        world.Advance(5);
        QueueDrainCount(world).ShouldBe(1, "形状 queue_drain：补放只发生一次，不每步重试");
    }

    // === 落点 4｜N-13 光环按距离（断言 A37 / A38）+ N-14 扇形（A39）===

    /// <summary>
    /// <b>A37（钉 N-13 那笔落点）</b>：距离判定在 S5 那段数据算里，跨边界由场子发既有的
    /// <c>ApplyEffect</c> / <c>RemoveEffect</c>——公共层没有「距离条件」那一列，S2 全程不问活世界。
    /// 台账：3 米内敌攻击 200×0.85 = 170；走到 4.5 米 → 光环撤 → 回到 200。差的就是那一个乘数。
    /// </summary>
    [Fact]
    public void A37_光环按边界重应用_台账只差一个乘数()
    {
        var world = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 3 },
            dummyAttack: 200, aura: true);

        // 第一发走 S4 入口（按下主动技），之后每一次边界变化走场子发的那两条动词——两份写入落到同一条实例上
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Dread}|{Dummy}").ShouldBeTrue();
        world.Advance(1);
        world.Current(Dummy, SkillDuelRift.Attr.Attack).ShouldBe(170, "3 米 → 在 4 米界内");
        world.Stacks(Dummy, SkillDuelRift.Fx.DreadAura).ShouldBe(1, "按下与规则重应用指的是同一条效果实例（封顶 1 层，攒不出第二份真值）");
        world.Count<EffectStackCapped>().ShouldBe(1, "形状 stack_capped（EffectStackCapped）：第二次施加被层数上限挡回去——这一格计数就是「没有两套真值」的证据");
        world.HasEvent<SkillTestWorld.AuraEffectApplied>().ShouldBeTrue("形状 aura_applied（AuraEffectApplied）：重应用这一步留痕（Journal 里就是 ApplyEffect）");

        world.Inject("Move", $"{Dummy}|1.5");   // 走到 4.5 米
        world.Advance(1);
        world.Current(Dummy, SkillDuelRift.Attr.Attack).ShouldBe(200, "出界 → 同一条效果被撤");
        world.HasEvent<SkillTestWorld.AuraEffectRemoved>().ShouldBeTrue("形状 aura_removed（AuraEffectRemoved）：出界那一发也留痕");
        world.Snapshot().ShouldNotContain(FxName(SkillDuelRift.Fx.DreadAura), Case.Sensitive, "撤干净了，不留半个真值");

        world.Inject("Move", $"{Dummy}|-2");    // 再走回 2.5 米 → 又应用上
        world.Advance(1);
        world.Current(Dummy, SkillDuelRift.Attr.Attack).ShouldBe(170, "来回越界两次都重算得出来——这才叫「按距离反复重新应用」那一格");
    }

    /// <summary>
    /// <b>A38（界上那一点归界内）</b>：起始距离 4.0 米正好压在半径上 → 光环生效。
    /// 舍入只在界上这一次（3 节约束 2），所以「差一步就越界」两侧都读得出数。
    /// </summary>
    [Fact]
    public void A38_压在边界上算界内()
    {
        var onBoundary = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 4 },
            dummyAttack: 200, aura: true);
        onBoundary.Advance(1);
        onBoundary.Current(Dummy, SkillDuelRift.Attr.Attack).ShouldBe(170, "4.0 米 = 界内");

        var justOut = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 4.1 },
            dummyAttack: 200, aura: true);
        justOut.Advance(1);
        justOut.Current(Dummy, SkillDuelRift.Attr.Attack).ShouldBe(200, "4.1 米 = 界外");
    }

    /// <summary>
    /// <b>A39（钉 N-14 那笔落点）</b>：扇形与射程同一格、一份实现（<c>InsideSector</c>）。
    /// 直线几何上角度是退化的，所以 <c>tank.slam</c> 的「距离 ≤ 3 米且在正前 120° 内」在这里
    /// 拿纯函数的界内界外两侧钉住：距离两侧各一个点、角度两侧各一个点。
    /// </summary>
    [Fact]
    public void A39_扇形命中界内界外两侧各一个点()
    {
        // 正前（+X）、半径 3 米、全角 120°（半角 60°）
        AbilityStepper.InsideSector(0, 0, 2, 0, 1, 0, 3, 120).ShouldBeTrue("正前 2 米：中");
        AbilityStepper.InsideSector(0, 0, 3.2, 0, 1, 0, 3, 120).ShouldBeFalse("3.2 米：出界");
        AbilityStepper.InsideSector(0, 0, 0, 3.2, 1, 0, 3, 120).ShouldBeFalse("侧后方 3.2 米：出界");

        // 角度两侧：30° 与 80°（半角 60° ⇒ 30° 在内、80° 在外）
        const double r = 2;
        AbilityStepper.InsideSector(0, 0, r * Math.Cos(D2R(30)), r * Math.Sin(D2R(30)),
            1, 0, 3, 120).ShouldBeTrue("偏 30° 还在正前 120° 里");
        AbilityStepper.InsideSector(0, 0, r * Math.Cos(D2R(80)), r * Math.Sin(D2R(80)),
            1, 0, 3, 120).ShouldBeFalse("偏 80° 出扇形");

        // 不限角度（cone=0）就是个圆；站在原地那一点算命中（不静默判负）
        AbilityStepper.InsideSector(0, 0, 0, -2, 1, 0, 3, 0).ShouldBeTrue("cone=0 ⇒ 只看距离");
        AbilityStepper.InsideSector(0, 0, 0, 0, 1, 0, 3, 120).ShouldBeTrue("同一点 ⇒ 算在扇形里");
        AbilityStepper.InsideSector(0, 0, 2, 0, 0, 0, 3, 120).ShouldBeTrue("没有朝向 ⇒ 这一发不看角度");
    }

    private static double D2R(double degrees) => degrees * Math.PI / 180.0;

    private static string FxName(string effectId) => "f;" + effectId;

    // === 其余六条的台账（A40~A45）===

    /// <summary>
    /// <b>A40（warrior.rage，N-2 事件触发的单挑场版）</b>：受击 → 登记成待决结算 → 下一步才加怒。
    /// 台账：20/40 两发平A → 第 21 步怒 8、第 41 步怒 16；上限 100 是属性的界，不是触发器里的 if。
    /// </summary>
    [Fact]
    public void A40_受击加怒_登记成待决结算_下一步才动()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroPassives: new[] { SkillDuelRift.Fx.RageOnHit });

        world.Advance(20);
        world.Current(Hero, SkillDuelRift.Attr.Rage).ShouldBe(0, "本步怒气未动（因果不倒）");
        world.Advance(1);
        world.Current(Hero, SkillDuelRift.Attr.Rage).ShouldBe(8, "第 21 步 S0 结算 +8");
        world.Advance(20);   // 第 41 步
        world.Current(Hero, SkillDuelRift.Attr.Rage).ShouldBe(16);

        world.Inject("SetBase", $"{Hero}|{SkillDuelRift.Attr.Rage}|96");
        world.Advance(20);   // 第 61 步：96+8 → 界抬回 100
        world.Current(Hero, SkillDuelRift.Attr.Rage).ShouldBe(100, "资源上限靠界守住");
        world.Sets.Units[Hero].Base[SkillDuelRift.Attr.Rage].ShouldBe(104, "基础值确实过界了——念出来的是派生值（显示即真相）");
    }

    /// <summary>
    /// <b>A41（warrior.rift 本体，commit 点与 Calc 链）</b>：前摇 3 步 → 第 8 步才落伤害；
    /// 伤害按<b>施法者</b> commit 时抄下的已损失生命放大（残血更狠）。
    /// </summary>
    [Fact]
    public void A41_突斩前摇三步_残血那一刀更狠()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            dummyUnkillable: true);

        world.Advance(5);
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
        world.Advance(2);
        world.Counter(Dummy, "damage_taken_total").ShouldBe(0, "前摇没走完就不该结算");
        world.Advance(1);   // 第 8 步过 commit
        world.Counter(Dummy, "damage_taken_total").ShouldBe(20, "第 8 步落 20");

        world.Advance(12);   // 第 20 步挨一发平A → 英雄 90 血
        world.Current(Hero, SkillDuelRift.Attr.Health).ShouldBe(90);
        world.Inject("ClearCooldown", $"{Hero}|{SkillDuelRift.Skill.Rift}");
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
        world.Advance(3);    // 第 23 步落第二刀（残血放大 1.15 倍 = 23）
        world.Counter(Dummy, "damage_taken_total").ShouldBe(43, "20 + 23：残血那一刀比满血那一刀狠");
    }

    /// <summary>
    /// <b>A42（archer.pierce，延迟结算 + 曲线 + 三项 Calc 链）</b>：
    /// 6 米外那一发按落地当步的距离折曲线（×0.8）——攻击 20 时 20×0.8 = 16；
    /// 攻击压到 8 时被链上那道地板抬到 12，再折曲线 12×0.8 = 9.6 → 10。
    /// <b>这一条同时钉着 N-12（多项 Calc 链的 working 传递）</b>：顺序错了就读不出那个地板数。
    /// </summary>
    [Fact]
    public void A42_穿刺箭按落地距离折曲线_链上地板抬得起来()
    {
        var far = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 6 },
            heroAbilities: new[] { SkillDuelRift.Skill.Pierce });
        far.Advance(5);
        far.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Pierce}|{Dummy}");
        far.Advance(7);
        far.Counter(Dummy, "damage_taken_total").ShouldBe(0, "第 13 步才该落地（前摇 2 + 飞 6）");
        far.Advance(1);
        far.Counter(Dummy, "damage_taken_total").ShouldBe(16, "20 × 0.8 = 16");

        var weak = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 6 },
            heroAttack: 8, heroAbilities: new[] { SkillDuelRift.Skill.Pierce });
        weak.Advance(5);
        weak.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Pierce}|{Dummy}");
        weak.Advance(8);
        weak.Counter(Dummy, "damage_taken_total").ShouldBe(10, "8 → 地板 12 → ×0.8 = 9.6 → 四舍五入 10（链与曲线各折一次）");
    }

    /// <summary>
    /// <b>A43（archer.pierce 的配套用例：目标按 Id 在落地那一步现解）</b>：箭在路上时目标被打死，
    /// 落地作废，并念得出为什么作废（<c>void_count</c> + <c>settle_void</c> 一行）。
    /// </summary>
    [Fact]
    public void A43_目标中途阵亡_落地作废且读得出为什么()
    {
        var world = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 6 },
            heroAbilities: new[] { SkillDuelRift.Skill.Pierce });
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Pierce}|{Dummy}");
        world.Advance(4);
        world.Sets.Units[Hero].Pendings.Count.ShouldBe(1, "箭在路上（快照念得出 p; 行）");

        world.Inject("SetBase", $"{Dummy}|{SkillDuelRift.Attr.Health}|0");
        world.Advance(5);
        world.Counter(Hero, "void_count").ShouldBe(1, "作废要有计数");
        world.Counter(Dummy, "damage_taken_total").ShouldBe(0, "作废的这一发不算承伤");
        world.HasEvent<EffectMissed>().ShouldBeTrue("形状 settle_void（EffectMissed）：作废发得出来");
    }

    /// <summary>
    /// <b>A44（archer.evasive，Override 与界的先后）</b>：×1.8 把 4.0 的移速推到 7.2，
    /// 界在最后一步套上 → 读数 6.0（已删的设计档）。
    /// </summary>
    [Fact]
    public void A44_加速被界抬回_读数六米每秒()
    {
        var world = SkillDuelRift.Stage(Static);
        world.Current(Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(4, "没挂效果时是基础值");

        world.Inject("ApplyEffect", $"{Hero}|{SkillDuelRift.Fx.EasiveSpeed}");
        world.Current(Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(6.0, "4 × 1.8 = 7.2 → 上限 6.0 守住（界归属性、不归求值器）");
        world.Snapshot().ShouldContain("1.8", Case.Sensitive, "那条乘数本身念得出来（没被界抹掉）");
    }

    /// <summary>
    /// <b>A45（rider.trample，激活检查的第二处问）</b>：命中施加定身 → <c>BlockedTags</c> 只管入口、
    /// 定身挡住的是 S5 那条腿；到期后同一个激活检查放行。
    /// </summary>
    [Fact]
    public void A45_定身挡住位移_到期自己放行()
    {
        var world = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Trample });
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Trample}|{Dummy}");
        world.Advance(3);   // 前摇 2 步 → 第 3 步过 commit 落定身（20 步）
        world.Sets.Units[Dummy].TagCounts.GetValueOrDefault(SkillDuelRift.Tag.Rooted).ShouldBe(1);

        world.Inject("Move", $"{Dummy}|1").ShouldBeFalse("定身挂着就走不动");
        world.XOf(Dummy).ShouldBe(2, "位置一动不动（站位归场子自己那份）");
        world.HasEvent<MoveBlocked>().ShouldBeTrue("为什么没走要留一条（形状 MoveBlocked）");

        world.Advance(20);   // 到第 23 步：S1 撤掉定身
        world.Inject("Move", $"{Dummy}|1").ShouldBeTrue("撤了就走得动");
        world.XOf(Dummy).ShouldBe(3);
    }

    /// <summary>
    /// <b>A46（rider.mounted，N-5 的 modifier 自带条件）</b>：不满足 = 跳过、<b>不是撤销</b>——
    /// 下马后读数回到 4.0，而那条 modifier 记录还挂着，再上马立刻又生效。
    /// </summary>
    [Fact]
    public void A46_条件不满足是跳过不是撤销()
    {
        var world = SkillDuelRift.Stage(Static, heroPassives: new[] { SkillDuelRift.Fx.MountedBonus });
        ModifierCount(world, Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(1, "效果已经挂上了，只是不生效");
        world.Current(Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(4, "没骑乘 → ×1.25 不生效");

        world.Inject("ApplyEffect", $"{Hero}|{SkillDuelRift.Fx.MountedTag}");
        world.Current(Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(5, "骑上 → 同一条立刻生效");

        world.Inject("RemoveEffect", $"{Hero}|{SkillDuelRift.Fx.MountedTag}|1");
        world.Current(Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(4, "下马 → 跳过");
        ModifierCount(world, Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(1, "记录还在（不是撤销）");
    }

    private static int ModifierCount(SkillTestWorld world, string unitId, string attribute)
        => world.Sets.Units[unitId].Modifiers.Count(m => m.Target == attribute);

    // === 可调参数与确定性（D1 / D3 在 H2 这一层）===

    /// <summary>
    /// <b>A47（D1 时间可控 + 确定性）</b>：同一份剧本两跑，逐格重放快照序列<b>逐字</b>全等；
    /// 不推进时读数一个字都不动（暂停冻逻辑）。
    /// </summary>
    [Fact]
    public void A47_同种子两跑逐字全等_不推进读数不动()
    {
        var first = Script(1);
        var second = Script(1);
        second.ShouldBe(first, "两跑必须逐字相同，否则 A3/A2 破了");
        first.Length.ShouldBe(25, "确实跑了 25 格");

        var world = SkillDuelRift.Stage(Sparring(), heroPassives: new[] { SkillDuelRift.Fx.SteadyCharges });
        world.Advance(7);
        var paused = world.ReplaySnapshot();
        for (var i = 0; i < 3; i++)
        {
            world.ReplaySnapshot().ShouldBe(paused, "读快照本身不许推进世界");
        }
    }

    /// <summary>
    /// <b>A48（我方档位那两档同一条路，「桥只转发」 的 H2 版）</b>：
    /// 「自动释放」与「在每一步之后手动按下」跑出的逐格快照<b>完全相同</b>——
    /// 因为两者都是同一个 <c>TryActivate</c>，差别只在谁在什么时候问这一发。
    /// </summary>
    [Fact]
    public void A48_自动档与手按档_逐格快照相同()
    {
        var auto = new List<string>();
        var autoWorld = SkillDuelRift.Stage(new SkillTestWorld.Tuning
        {
            OursAutoCast = true,
            StartDistanceMeters = 2,
        }, heroAbilities: new[] { SkillDuelRift.Skill.Rift }, dummyUnkillable: true);
        for (var i = 0; i < 25; i++)
        {
            autoWorld.Advance(1);
            auto.Add(Without档位Readout(autoWorld.ReplaySnapshot()));
        }

        var manual = new List<string>();
        var manualWorld = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            dummyUnkillable: true);
        for (var i = 0; i < 25; i++)
        {
            manualWorld.Advance(1);
            manualWorld.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
            manual.Add(Without档位Readout(manualWorld.ReplaySnapshot()));
        }

        manual.ShouldBe(auto, "同一个入口的两条路必须逐格同——差在哪一格：\n" + FirstFrameDiff(auto, manual));
    }

    /// <summary>
    /// 比对时把「我方档位」那一格抹掉：两档本来就 differ 在<b>谁来按这一发</b>，
    /// 要证的是「按下去之后发生的事逐字相同」，不是开局参数相同。
    /// </summary>
    private static string Without档位Readout(string snapshot)
        => snapshot.Replace("ours_auto=1", "ours_auto=x").Replace("ours_auto=0", "ours_auto=x");

    /// <summary>
    /// <b>A49（敌血两档分开「打了多少」与「死没死」）</b>：打不死档累计承伤照读、血回到上限；
    /// 定值档同一笔就把它打死。两档的 <c>took=</c> 那一列必须相同，才是「两件事分开测」。
    /// </summary>
    [Fact]
    public void A49_打不死与定值两档_累计承伤同一本账()
    {
        var dummy = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Rift });
        var effigy = SkillDuelRift.Stage(new SkillTestWorld.Tuning
        {
            EnemyUnkillable = true,
            StartDistanceMeters = 2,
        }, heroAbilities: new[] { SkillDuelRift.Skill.Rift });

        dummy.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
        effigy.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
        dummy.Advance(8);
        effigy.Advance(8);

        dummy.Counter(Dummy, "damage_taken_total").ShouldBe(20);
        effigy.Counter(Dummy, "damage_taken_total").ShouldBe(20, "同一笔账");
        effigy.Current(Dummy, SkillDuelRift.Attr.Health).ShouldBe(100, "打不死 → 回到上限");
        dummy.Current(Dummy, SkillDuelRift.Attr.Health).ShouldBe(80, "定值 → 掉得下去");

        // 再来两刀：定值档第 3 刀就死（20×5=100），打不死档照打
        for (var i = 0; i < 5; i++)
        {
            dummy.Inject("ClearCooldown", $"{Hero}|{SkillDuelRift.Skill.Rift}");
            dummy.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
            effigy.Inject("ClearCooldown", $"{Hero}|{SkillDuelRift.Skill.Rift}");
            effigy.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
            dummy.Advance(4);
            effigy.Advance(4);
        }

        dummy.IsAlive(Dummy).ShouldBeFalse("定值档被打死了");
        effigy.IsAlive(Dummy).ShouldBeTrue("打不死档还在");
        effigy.Counter(Dummy, "damage_taken_total").ShouldBeGreaterThan(100, "它照样把挨过的账留着（DPS 那一格靠这个读数）");
    }

    private static string[] Script(int seed)
    {
        var world = SkillDuelRift.Stage(Sparring(), seed: seed,
            heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            heroPassives: new[] { SkillDuelRift.Fx.SteadyCharges }, dummyUnkillable: true);
        var frames = new List<string>();
        for (var step = 1; step <= 25; step++)
        {
            if (step == 5)
            {
                world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
            }

            if (step == 10)
            {
                world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
            }

            world.Advance(1);
            frames.Add($"tick={step} | " + world.ReplaySnapshot().Replace('\n', ' '));
        }

        return frames.ToArray();
    }

    /// <summary>按形状数事件（层与场子两本账一起数，归并在 <see cref="SkillTestWorld"/> 里做）。</summary>
    private static int CountEvents<T>(SkillTestWorld world, Func<T, bool>? keep = null) where T : EventPayload
        => world.Count(keep);

    /// <summary>queue_drain 这一族有两条形状（打向目标 / 打向自己），数的时候都要算。</summary>
    private static int QueueDrainCount(SkillTestWorld world)
        => world.Count<QueuedCastTakenAt>() + world.Count<QueuedCastTaken>();

    // === 通用性这条 G2 与 D6/D7 在 H2 这一层（A50~A52）===

    /// <summary>
    /// <b>A50（G2 这条的机器口径）</b>：单挑场本体（<c>SkillTestWorld.cs</c>）里搜不到任何技能名、
    /// tag 字面量、属性名——它们全在注册壳 <c>SkillDuelRift.cs</c> 里。
    /// 加一条技能不改场子这件事，从此不靠人眼审。
    /// </summary>
    [Fact]
    public void A50_场子源码里搜不到技能名_tag_与属性名()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SkillTestWorld.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));
        string[] banned =
        {
            "warrior.", "tank.", "archer.", "rider.", "fx.", "status.", "cooldown.",
            "\"hp\"", "\"attack\"", "\"move_speed\"", "\"damage_taken\"", "\"rage\"",
            "warrior.rift", "Fx.", "Skill.",
        };

        foreach (var needle in banned)
        {
            source.ShouldNotContain(needle, Case.Sensitive, $"场子不该认识 {needle}（它该由注册壳递进来）");
        }
    }

    /// <summary>
    /// <b>A51（D6 状态可回跳，在单挑场上跑）</b>：层的快照 → 重建 → 再快照，逐字相等。
    /// 派生值（<c>x;</c> 当前值 / <c>t;</c> tag 计数）不进重建，靠 S2 现算——
    /// 所以这一发顺带把「快照里没混进委托或活对象」（A2）与 S2 的确定性一起钉住。
    /// </summary>
    [Fact]
    public void A51_单挑场上的快照往返_逐字相等()
    {
        var world = SkillDuelRift.Stage(Sparring(),
            heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            heroPassives: new[] { SkillDuelRift.Fx.SteadyCharges }, dummyUnkillable: true);
        world.Advance(5);
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}");
        world.Advance(21);   // 越过第一刀、挨过平A、进过队列、也回充过一层
        var first = world.Snapshot();
        first.ShouldContain("charges=", Case.Sensitive, "这一格里回充那一栏真的念得出来（不然是空跑）");

        var rebuilt = world.Stepper.Branch(first);
        rebuilt.TakeSnapshot().ShouldBe(first, "差在哪一行直接 diff：\n" + FirstLineDiff(first, rebuilt.TakeSnapshot()));
    }

    /// <summary>
    /// <b>A52（D7 分支对照，在单挑场上跑）</b>：同一检查点分两叉，一支按突斩、一支不按——
    /// 起跳是 O(1)（不重跑前五步），两叉的差就是那一笔（20 血）。
    /// 一处边界写在这儿：分支出来的步进器仍绑着<b>父场子</b>那个 <c>IAbilityHost</c>（读活性能读到父局），
    /// 所以本发比的是分支自己那份账（<c>World.Units[...]</c>）；要让分支各绑一个宿主，得给
    /// <c>AbilityStepper.Restore</c> 换成宿主工厂——这笔待确认项记进了本轮的记录。
    /// </summary>
    [Fact]
    public void A52_从检查点分叉_两叉只差那一笔()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            dummyUnkillable: true);
        world.Advance(5);
        var checkpoint = world.Snapshot();

        var casted = world.Stepper.Branch(checkpoint);
        casted.TryActivate(Hero, SkillDuelRift.Skill.Rift, Dummy);
        for (var i = 0; i < 4; i++) casted.AdvanceStep();

        var idle = world.Stepper.Branch(checkpoint);
        for (var i = 0; i < 4; i++) idle.AdvanceStep();

        Total(casted.World, Dummy).ShouldBe(20, "按下的那一叉：第 8 步落 20");
        Total(idle.World, Dummy).ShouldBe(0, "不按的那一叉：一分没掉");
    }

    /// <summary>
    /// <b>A53（N-19 那笔的哨兵用例）</b>：层的一行并进重放段格式时整行的分号都换成空格，
    /// 于是技能行在段格式里散成「<c>a</c> + 名字 + 一串列」三个以上的 token，尾下列全属 L2 的容忍项。
    /// 四腿：<b>⓪</b>形状本身（技能行 token 数 &gt; 2 —— 旧写法只换第一个分号时整行的列挤在第二个
    /// token 里，这一腿当场红，就是 2026-09-27 实测的六格全红）；<b>①</b>给剧本里每条技能行按层的写法
    /// 补一列（<c>;extra=1</c>，正是「下次给层改一列」的形状），L2 语义档必须 0 分歧；
    /// <b>②</b>同一份改过的剧本在 L3 逐字档照红（容忍不等于什么都看不见）；
    /// <b>③</b>把某条技能行的名字（段首第二个 token）换掉，L2 必须报「身份漂移」。
    /// </summary>
    [Fact]
    public void A53_层的行加尾列L2不红_L3与改身份照红()
    {
        var at20 = StageForReplay();
        at20.Advance(20);
        var at40 = StageForReplay();
        at40.Advance(40);
        var recorded = at20.ReplaySnapshot();
        var rows = SkillRows(recorded);
        rows.ShouldNotBeEmpty("段格式里读得出技能行（a 段），不然是空跑");

        // ⓪ 形状：层的一行并进段格式后必须散成一串 token，而不是「类型 + 整行」两个 token
        rows[0].Split(' ').Length.ShouldBeGreaterThan(2, $"技能行在段格式里得散成三个以上 token（挤成两个就是 N-19 那个坑），实到：{rows[0]}");

        var appended = AppendColumn(recorded, "a");
        appended.Length.ShouldBe(recorded.Length + rows.Count * ";extra=1".Length, "尾列真的补到了每一条技能行上（一处没补就是选择段写错了）");

        // ① L2：加尾列属容忍项
        var session = ReplayWith(At(20, appended), ReplayRunner.AssertLevel.Semantic);
        session.Divergences.ShouldBeEmpty("给层加一列是良性改动，L2 不许红：\n" + string.Join("\n", session.Divergences));

        // ② L3：同一份改过的剧本逐字档必须红（红在长度上，不是红在没跑到）
        var literal = ReplayWith(At(20, appended), ReplayRunner.AssertLevel.Literal);
        literal.Divergences.Count.ShouldBe(1, "L3 逐字档该红的还是红");

        // ③ L2：改段首身份必须照红——容忍不等于放过结构变化
        var renamed = RenameFirstSkillRow(recorded);
        var drifted = ReplayWith(At(20, renamed), ReplayRunner.AssertLevel.Semantic);
        drifted.Divergences.Count.ShouldBe(1, "换掉技能名是结构变化，L2 认");
        drifted.Divergences[0].ShouldContain("身份漂移", Case.Sensitive, "报的是身份漂移，不是别的东西：\n" + drifted.Divergences[0]);

        // 顺带钉住「同种子两跑到第 40 步」这条改列前后都不漂（①②③用的都是真演化，不是手搓字符串）
        ReplayWith(At(40, AppendColumn(at40.ReplaySnapshot(), "a")),
            ReplayRunner.AssertLevel.Semantic).Divergences.ShouldBeEmpty();
    }

    /// <summary>
    /// <b>A54（G1 那条通用性在落点 (b) 下的机器口径）</b>：
    /// 16 条检查清单那份文件（<c>AbilityChecklistTests.cs</c>）里搜不到排兵布阵的任何类型名——
    /// 清单认得的是 GugaFramework 的层，不认得某个原型的世界。
    /// 本条与 A50 同形：A50 管「场子不认得技能」，本条管「清单不认得原型」。
    /// </summary>
    /// <remarks>
    /// 落点 (b) 是用户确认的（单挑场那页 12 节 待确认项 1）；2026-09-28 再定 P3 不新建 csproj。
    /// 那份文件原名 <c>AbilityCoverageBatteryTests.cs</c>（「覆盖电池」），同日按「禁自造术语」 改成现名——
    /// 「电池」不是行业词也不是本项目的词，第一次读不出它做什么。
    /// <b>2026-10-01（ticket 05）那份文件搬进公共层 <c>src/GugaFramework/Tests/</c></b>（技能试验场要跑全 16 条，
    /// 而场子不隶属任何原型、清单不能住在原型目录里），所以只有 <c>path</c> 换了几段——判据一个字没动。
    /// </remarks>
    [Fact]
    public void A54_检查清单里搜不到排兵布阵的类型名()
    {
        // 清单那份本体在公共层：src/GugaFramework/Tests/。从本测试的输出目录往上数六段到 src。
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
            "GugaFramework", "Tests", "AbilityChecklistTests.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));
        source.ShouldContain("class AbilityChecklistTests", Case.Sensitive, "读到的是清单那份文件（路径拼错了这条会空跑）");
        string[] banned =
        {
            "BattleWorld", "TroopCard", "UnitCard", "UnitCards", "DuelRiftSetup", "BattleMechanics",
        };

        foreach (var needle in banned)
        {
            source.ShouldNotContain(needle, Case.Sensitive, $"清单不该认得 {needle}（它只该认得 GugaFramework 的层，原型由注册壳递进来）");
        }
    }

    // === 第三批（2026-09-28 P3）：词表收成一份之后钉通用性的两条（A68 / A69）===

    /// <summary>
    /// <b>A68（剧本词表只有一份）</b>：P3 收的第一块。在此之前动词名与参数次序<b>抄了两份</b>——
    /// H2 的 <c>SkillTestWorld.Inject</c> 十条、H3 的 <c>DuelRiftSetup.Inject</c> 八条，
    /// 于是 <c>CastAbility</c> 的第 4 段位移目的地在一侧接得住、在另一侧被<b>静默丢掉</b>
    /// （issue 15 第 1 条点名的那一格）。现在名字、次序、缺省值、什么时候该抛全在
    /// <see cref="AbilityVerbs"/> 那一份表里，两份文件只是适配器。
    /// 三条腿：<b>①②</b> 两份适配器源码里搜不到九个动词名的<b>带引号字面量</b>
    /// （引用常量不算抄，写字面量才算第二份表），也搜不到 <c>case "</c>——按命令分发的 switch
    /// 只准有公共层那一个；<b>③</b> 先断言两份文件真读到了、且各自真调了那一份表
    /// （防路径拼错成空跑，与 A54 同一手法）；<b>④</b> 名单与解析器对得上：
    /// <see cref="AbilityVerbs.Names"/> 那九个名字各能解析出自己那一串参数，名单外的一条当场抛。
    /// </summary>
    [Fact]
    public void A68_词表只有一份_两份适配器里搜不到动词名字面量()
    {
        var adapters = new[] { "SkillTestWorld.cs", "../Scripts/Logic/DuelRiftSetup.cs" };
        foreach (var file in adapters)
        {
            var source = ReadSourceFile(file);
            source.ShouldContain("class ", Case.Sensitive, $"{file} 读到的是源码本体（路径拼错了这条会空跑）");
            source.ShouldContain("AbilityVerbs.Dispatch", Case.Sensitive, $"{file} 真调了公共层那一份表（读错文件或那一发被删了，这条会空跑）");

            foreach (var verb in AbilityVerbs.Names)
            {
                source.ShouldNotContain($"\"{verb}\"", Case.Sensitive,
                    $"{file} 里不该出现动词名 {verb} 的字面量——名单与参数次序只准有一份");
            }

            source.ShouldNotContain("case \"", Case.Sensitive, $"{file} 里不该再有一个按命令分发的 switch（那就是第二份词表）");
        }

        // ④ 名单与解析器对得上：九个名字各发一发**它自己那个形状**的参数都不抛，名单外的一条当场抛
        var samples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AbilityVerbs.Cast] = "u1|a1|t1|1>3.5",
            [AbilityVerbs.Cancel] = "u1|a1",
            [AbilityVerbs.ApplyEffect] = "t1|e1|20|u1",
            [AbilityVerbs.RemoveEffect] = "t1|e1|1",
            [AbilityVerbs.SetAttribute] = "t1|attack|2|Mult|status.mounted",
            [AbilityVerbs.SetBase] = "t1|hp|55",
            [AbilityVerbs.ClearCooldown] = "u1|a1",
            [AbilityVerbs.SetStepsRemaining] = "u1|a1|2",
            [AbilityVerbs.Move] = "u1|1.5",
        };

        foreach (var (verb, args) in samples)
        {
            Should.NotThrow(() => AbilityVerbs.Parse(verb, args), $"{verb} 在名单里却解析不了自己那一串参数（Names 与 Parse 漂成两份了）");
            AbilityVerbs.Parse(verb, args).Cmd.ShouldBe(verb);
        }

        samples.Keys.Count.ShouldBe(AbilityVerbs.Names.Length, "这份样本名单与公共层那份动词名单不等长——加了一条动词却没在这儿补样本");
        Should.Throw<InvalidOperationException>(() => AbilityVerbs.Parse("NotAVerb", "u1"), "名单外的动词当场抛，不在重放里静默少一笔");
    }

    /// <summary>
    /// <b>A69（给了目的地那一格，不许在一侧被静默丢掉）</b>：词表统一之后，
    /// 「这一侧接不接位移目的地」变成宿主<b>声明</b>的能力位，而不是参数被丢掉。
    /// 三条腿：<b>①</b> <see cref="AbilityVerbs.Parse"/> 对第 4 段交出 3.5，给 <c>-</c> 或省略交出 null
    /// （纯函数、不起世界，所以这一格到底是几能单独判）；<b>②</b> 必填参数缺了<b>当场抛</b>、不兜 0
    /// （兜 0 = 静默摆到第 0 排，「桥只转发」 那句）；<b>③</b> H2 这一侧声明不接（单挑场里位置是输入常量、
    /// 不是技能的输出，那页 3 节与 12 节 待确认项 5 确认过），所以带目的地的按下<b>当场抛</b>，
    /// 而不带目的地的同一条命令照旧放行——「注入的路径」与「玩家的路径」不许在这格长成两回事。
    /// H3 那一侧接、且写得出读得回，那半本台账在 <see cref="AbilityH3SlamChargeTests"/>（A66）。
    /// </summary>
    [Fact]
    public void A69_位移目的地那一格_解析得出来也不许被静默丢掉()
    {
        // ① 第 4 段：给了就是「从>到」那一对，"-" 与省略都算没给
        AbilityVerbs.Parse(AbilityVerbs.Cast, "h01|warrior.rift|d01|1>3.5").Move.ShouldBe(((double)1, (double)3.5), "起点与目的地读不出来 = 表把那一格丢了");
        AbilityVerbs.Parse(AbilityVerbs.Cast, "h01|warrior.rift|d01|-").Move.ShouldBeNull();
        AbilityVerbs.Parse(AbilityVerbs.Cast, "h01|warrior.rift|d01").Move.ShouldBeNull();
        AbilityVerbs.Parse(AbilityVerbs.Cast, "h01|warrior.rift|d01|-").TargetId.ShouldBe("d01", "第 3 段是目标、第 4 段才是位移，次序不许漂");
        Should.Throw<FormatException>(() => AbilityVerbs.Parse(AbilityVerbs.Cast, "h01|warrior.rift|d01|3.5"), "只给一个数 = 位移缺了起点：层里不再有坐标，算不出起点，不许拿 0 兜");

        // ② 必填参数缺了当场抛，不兜 0
        Should.Throw<FormatException>(() => AbilityVerbs.Parse(AbilityVerbs.SetBase, "h01|hp"), "数值那一格没给：兜 0 就是静默把属性拧成 0");
        Should.Throw<FormatException>(() => AbilityVerbs.Parse(AbilityVerbs.Cast, "|warrior.rift"), "单位 Id 没给也一样抛");

        // ③ H2 声明不接位移：给了就抛，不给照旧
        var world = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Rift });
        world.Inject(AbilityVerbs.Cast, $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}").ShouldBeTrue("不带位移的那一发照旧放得出去");
        Should.Throw<NotSupportedException>(() => world.Inject(AbilityVerbs.Cast, $"{Hero}|{SkillDuelRift.Skill.Rift}|{Dummy}|0>3.5"), "H2 不接位移却收到了一格：必须当场抛，不许演成「按下了但没走」");
    }

    /// <summary>
    /// <b>A70（断言只有一份、壳只是壳）</b>：P3「一份清单三处跑」的机器口径。G1 说「换宿主不改断言」，
    /// 光看清单本体不认得原型（A54）只证了一半——另一半是<b>三个注册壳里都没有断言</b>：
    /// 壳一旦长出 <c>Assert</c>，就说明某一格的断言被抄进了宿主侧，从此改一处要改三份。
    /// 三条腿，对 <c>AbilityChecklistH1Host.cs</c> / <c>AbilityChecklistH2Host.cs</c> / <c>AbilityChecklistH3Host.cs</c>
    /// 各跑一遍：<b>①</b> 文件读到了（含 <c>class Checklist</c>，防路径拼错成空跑）；
    /// <b>②</b> 里面搜不到 <c>Assert.</c>；<b>③</b> 也搜不到动词名的字面量（与 A68 同一口径，壳只准引用常量）。
    /// </summary>
    [Fact]
    public void A70_三个注册壳里都没有断言也没有动词字面量()
    {
        var shells = new[]
        {
            "AbilityChecklistH1Host.cs", "AbilityChecklistH2Host.cs", "AbilityChecklistH3Host.cs",
        };

        foreach (var file in shells)
        {
            var source = ReadSourceFile(file);
            source.ShouldContain("class Checklist", Case.Sensitive, $"{file} 读到的是注册壳本体（读错文件或改名了，这条会空跑）");
            source.ShouldNotContain("Assert.", Case.Sensitive, $"{file} 里长出了断言——断言只能有清单本体那一份，壳只递名字与数字");
            source.ShouldNotContain("Test]", Case.Sensitive, $"{file} 里长出了测试方法（同上：一处断言只准有一份）");

            foreach (var verb in AbilityVerbs.Names)
            {
                source.ShouldNotContain($"\"{verb}\"", Case.Sensitive,
                    $"{file} 里写了动词名 {verb} 的字面量——名单只准有公共层那一份（A68 同口径）");
            }
        }
    }

    /// <summary>
    /// <b>A83（G2 的机器口径，扫全目录）</b>：公共层技能目录 <c>src/GugaFramework/Abilities/</c> 的
    /// <b>每一份</b> 源文件里都搜不到那五个词表字面量——血量属性名、承伤属性名、冷却前缀、定身 tag、抵挡 tag。
    ///
    /// 为什么扫的是全目录而不是一个文件：A50 扫的是场子那一份（单挑场认不认识技能名），
    /// 这一条扫的是公共层自己（公共层认不认识原型的词表）。两件事，扫的面也不一样。
    /// 那五列以前带缺省，缺省就写在这一个文件里——扫全目录是为了以后谁在别的文件里补一份默认也照样红。
    ///
    /// <b>触发事件名那一格也归宿主了</b>（ticket 10）：<c>"damage_taken"</c> 与 <c>"effect_applied"</c>
    /// 曾经以字面量硬编在 <c>ConsumeTriggers</c> 的三个调用点上，现已收成配置里那两列
    /// <c>DamageTakenEvent</c> / <c>EffectAppliedEvent</c>（<c>required</c>，无缺省）。
    /// 所以本条现在判的不再只是「五列不再有缺省」，而是<b>整份公共层技能目录里一次都不许出现这两个词</b>——
    /// 下面第二个循环把原先那个「按行排除 ConsumeTriggers 调用」的例外撤掉了，例外一撤，这一格才算真钉住。
    /// 派生计数名（<c>damage_taken_total</c>、<c>damage_dealt</c> 等）不是词表，仍按行排除。
    /// </summary>
    [Fact]
    public void A83_公共层技能目录里搜不到那五个词表字面量()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "GugaFramework", "Abilities"));
        Directory.Exists(dir).ShouldBeTrue($"读不到 {dir}（路径拼错了这条就是空跑）");

        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        files.Length.ShouldBeGreaterThan(0, $"{dir} 下一份 .cs 都没读到（空跑）");

        // 那一列的缺省已经删掉，所以这里只判「五列不再以字面量形式出现在公共层」。
        string[] banned = { "\"hp\"", "\"cooldown.\"", "\"status.rooted\"", "\"status.block_ready\"" };
        foreach (var needle in banned)
        {
            foreach (var file in files)
            {
                File.ReadAllText(file).ShouldNotContain(needle, Case.Sensitive, $"{Path.GetFileName(file)} 里出现了 {needle} —— 词表归原型，公共层一个业务字面量都不许有");
            }
        }

        // 承伤属性名那一列 + 事件触发的两个订阅键（ticket 10 收进配置的那两列）：
        // 三者都断言「整份文件里一次都不许出现」。派生计数名不是词表，按行排除。
        var stepper = File.ReadAllText(Path.Combine(dir, "AbilityStepper.cs"));
        foreach (var line in stepper.Split('\n'))
        {
            if (line.Contains("AddCounter("))
            {
                continue;   // 派生计数名（damage_taken_total / damage_dealt …），不是词表那一列
            }

            line.ShouldNotContain("\"damage_taken\"", Case.Sensitive,
                $"AbilityStepper.cs 里 {line.Trim()} 出现了 \"damage_taken\" —— 承伤属性名与承伤事件名都归原型给，层里不留字面量");
            line.ShouldNotContain("\"effect_applied\"", Case.Sensitive,
                $"AbilityStepper.cs 里 {line.Trim()} 出现了 \"effect_applied\" —— 效果落地事件名归原型给，层里不留字面量");
        }

        // 整份目录逐个文件再扫一遍这两个订阅键：上面那个循环只看 AbilityStepper.cs 那一份，
        // 而「以后谁在别的文件里补一份」正是全目录口径要挡的。
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var line in text.Split('\n'))
            {
                if (line.Contains("AddCounter("))
                {
                    continue;   // 派生计数名，与词表无关
                }

                line.ShouldNotContain("\"damage_taken\"", Case.Sensitive,
                    $"{Path.GetFileName(file)} 里 {line.Trim()} 出现了 \"damage_taken\" —— 词表与事件订阅键都归原型给");
                line.ShouldNotContain("\"effect_applied\"", Case.Sensitive,
                    $"{Path.GetFileName(file)} 里 {line.Trim()} 出现了 \"effect_applied\" —— 事件订阅键归原型给");
            }
        }
    }

    /// <summary>
    /// <b>A84（七列必填的反向用例）</b>：接层时少给一列，当场抛，不兜默认值。
    /// 走的是外部行为：步进器构造不出来，抛什么类型、消息里点名哪一列。
    /// 少给一列是<b>编译期</b>红的（七列都 <c>required</c>），所以运行时这条只钉得住「整份没递」那一种；
    /// 「少给一列编译不过」由 A83 那条全目录口径的编译事实本身兜着。
    /// </summary>
    [Fact]
    public void A84_接层时少给一列当场抛_不兜默认值()
    {
        var world = new AbilityWorld();
        world.AddUnit("u1", new[] { ("hp", 100.0) });
        var tables = new AbilityTables();
        var complete = new AbilityLayerConfig
        {
            HealthAttribute = "hp",
            DamageTakenAttribute = "damage_taken",
            CooldownAttributePrefix = "cooldown.",
            RootedTag = "status.rooted",
            DamageBlockTag = "status.block_ready",
            DamageTakenEvent = "damage_taken",
            EffectAppliedEvent = "effect_applied",
            EventCapacity = 2000,
        };

        // 基准：七列齐的时候接得起来（不然下面那条抛就没有对照）
        Should.NotThrow(
            () => new AbilityStepper(world, new BareHost(), new CalcRegistry(), tables, complete),
            "七列齐的接层不该抛");

        // 反向：整份配置没递 —— 层不替宿主造一份带缺省词的
        var nullConfig = Should.Throw<ArgumentNullException>(() =>
            new AbilityStepper(world, new BareHost(), new CalcRegistry(), tables, null!));
        nullConfig!.Message.ShouldContain("AbilityLayerConfig", Case.Sensitive, "抛的消息要点名是哪一份配置没给");
    }

    /// <summary>A84 用的空宿主：只答得回「这个兵在不在」，别的都不问（构造那几步不问活世界）。</summary>
    private sealed class BareHost : IAbilityHost
    {
        public AbilitySet? Find(string unitId) => null;

        public bool IsAlive(string unitId) => true;

        /// <summary>空宿主那一册里一位都没有（<see cref="Find"/> 恒 null、<see cref="UnitIds"/> 空）——
        /// 这一格照实答「不在」，让激活检查给一个有名字的拒绝理由，而不是拿一个查不到的 Id 往下算。</summary>
        public bool Exists(string entityId) => false;

        public IReadOnlyList<string> UnitIds => Array.Empty<string>();

        public double NextRandom() => 0.5;
    }

    private static string ReadSourceFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", fileName);
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"读不到源码 {fileName}（路径拼错了这条就是空跑）", full);
        }

        return File.ReadAllText(full);
    }

    // === 第二批（2026-09-28）：H2 这六条的台账（A55~A63）===
    // 六条全用既有列摆出来，一格都不新加（已删的设计档那句「第二批不再动列」在这里兑现）：
    // 谓词走 Calc 注册口、落点上限走距离曲线、tag 进计算读 CalcInput.TagCount、冷却倍率走 attribute 口。

    /// <summary>
    /// <b>A55（tank.guard 的「同一次施加两笔 modifier」）</b>：一次提交挂两笔，两笔都归同一条效果、
    /// 排序键（Seq）递增，快照念得出持续与授予的 tag。
    /// 台账：第 1 步过 commit → 承伤 <c>Override 0.5</c> 与移速 <c>Mult 0.7</c> 同时上身（4 → 2.8），
    /// 效果行 <c>f;fx.guard_wall;1;51;-</c> 到期步 = commit 那一步 + 50。
    /// </summary>
    [Fact]
    public void A55_同一次施加挂两笔modifier_排序键递增且都归这条效果()
    {
        var world = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Guard });
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Guard}|-").ShouldBeTrue();
        world.Advance(1);   // 前摇 0 步也要下一步的 S3 才过 commit

        var mine = world.Sets.Units[Hero].Modifiers.Where(m => m.SourceEffectId == SkillDuelRift.Fx.GuardWall)
            .OrderBy(m => m.Seq).ToList();
        mine.Count.ShouldBe(2, "两笔都在同一格挂上");
        mine[1].Seq.ShouldBeGreaterThan(mine[0].Seq, "排序键递增（同段两笔靠它定序）");
        world.Current(Hero, SkillDuelRift.Attr.DamageTaken).ShouldBe(0.5);
        world.Current(Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(2.8, "4 × 0.7 = 2.8");
        world.Snapshot().ShouldContain($"f;{SkillDuelRift.Fx.GuardWall};1;51;-");
        world.Snapshot().ShouldContain("t;state.guard;2", Case.Sensitive, "tag 计数 2 = 两处授予叠在一起（效果那条 + 技能「在放」那 8 步那条），S2 汇总得出来");
    }

    /// <summary>
    /// <b>A56（承伤方向的 Override 与属性界的先后）</b>：已删的设计档那条台账——
    /// 第一笔 Override 把承伤顶成 0.5（在界 [0.2, 1] 内，原样出），
    /// 第二笔来源把它顶到 0.1 时<b>界才抬回 0.2</b>；两笔谁先谁后按 <c>(AppliedAtTick, Seq)</c> 取最后一条 Override。
    /// 台账（木桩每 20 步平A 一下、原始 10 血）：第 20 步挨 5、第 40 步挨 2、第 51 步盾墙到期、
    /// 第 60 步仍挨 2（第二条 Override 是自己带的一笔，不随盾墙走）——累计 9。
    /// </summary>
    [Fact]
    public void A56_承伤倍率先聚合再过属性界_盾墙到期后另一笔照在()
    {
        var world = SkillDuelRift.Stage(Sparring(), heroAbilities: new[] { SkillDuelRift.Skill.Guard },
            dummyUnkillable: true);
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Guard}|-");
        world.Advance(20);
        world.Counter(Hero, "damage_taken_total").ShouldBe(5, "第 20 步 10 × 0.5 = 5");

        world.Inject("SetAttribute", $"{Hero}|{SkillDuelRift.Attr.DamageTaken}|0.1|Override");
        world.Current(Hero, SkillDuelRift.Attr.DamageTaken).ShouldBe(0.2, "Override 取最后一条 = 0.1 → 界抬回 0.2（Override 顶不开 clamp）");
        world.Advance(20);
        world.Counter(Hero, "damage_taken_total").ShouldBe(7, "第 40 步再挨 2");

        world.Advance(20);   // 第 60 步：盾墙第 51 步就到期了
        world.Current(Hero, SkillDuelRift.Attr.DamageTaken).ShouldBe(0.2, "那笔自己带的还在");
        world.Current(Hero, SkillDuelRift.Attr.MoveSpeed).ShouldBe(4, "盾墙那笔 Mult 随效果撤掉 → 移速回到基础值");
        world.Counter(Hero, "damage_taken_total").ShouldBe(9, "5 + 2 + 2");
    }

    /// <summary>
    /// <b>A57（入口激活检查 BlockedTags）</b>：挂着 <c>state.silence</c> 时这条技能<b>不接受</b>——
    /// 拒绝原因进快照、进行为流（不是静默 false）；探针效果撤掉后同一条命令立刻放得出。
    /// </summary>
    [Fact]
    public void A57_沉默挡住入口_念得出为什么被挡()
    {
        var world = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Guard });
        world.Inject("ApplyEffect", $"{Hero}|{SkillDuelRift.Fx.SilenceProbe}");

        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Guard}|-").ShouldBeFalse();
        world.Ability(Hero, SkillDuelRift.Skill.Guard).RejectedReason.ShouldBe(AbilityRejectReason.BlockedByTag);
        world.LastEvent<AbilityRejected>(Hero).ShouldNotBeNull("形状 reject（AbilityRejected）：被挡要发得出来");
        world.LastEvent<AbilityRejected>(Hero)!.Reason.ShouldBe(AbilityRejectReason.BlockedByTag, "字段里读得出为什么没放（旧文本的 reason=BlockedByTag）");
        world.Snapshot().ShouldContain("BlockedByTag", Case.Sensitive, "显示即真相：这一格读得出被谁挡");

        world.Inject("RemoveEffect", $"{Hero}|{SkillDuelRift.Fx.SilenceProbe}|0");
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Guard}|-").ShouldBeTrue("激活检查只在入口，探针撤了就走得通");
    }

    /// <summary>
    /// <b>A58（warrior.execute：Cost 激活检查 + 血线谓词 + 按目标已损失生命）</b>：
    /// 怒气 30 是<b>入口真拒绝</b>（<c>NoCost</c>），血线 0.2 是<b>Calc 谓词</b>（不成立时链的产物 0 → 这一发不打）。
    /// 台账：怒气 0 → 拒；补到 40 打一发 → 扣 30 剩 10，木桩满血时谓词归零（承伤 0、Cost 不返还）；
    /// 木桩 15/100 血时 30 + (100-15) = 115。
    /// </summary>
    [Fact]
    public void A58_怒气激活检查拒在入口_血线谓词归零在落地()
    {
        var world = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Execute },
            dummyUnkillable: true);

        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Execute}|{Dummy}").ShouldBeFalse();
        world.Ability(Hero, SkillDuelRift.Skill.Execute).RejectedReason.ShouldBe(AbilityRejectReason.NoCost, "怒气不够 = 入口就拒");

        world.Inject("SetBase", $"{Hero}|{SkillDuelRift.Attr.Rage}|40");
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Execute}|{Dummy}");
        world.Advance(1);
        world.Current(Hero, SkillDuelRift.Attr.Rage).ShouldBe(10, "过 commit 扣 30");
        world.Counter(Dummy, "damage_taken_total").ShouldBe(0, "满血不越线 → 谓词归零");
        world.Current(Dummy, SkillDuelRift.Attr.Health).ShouldBe(100);

        world.Inject("SetBase", $"{Dummy}|{SkillDuelRift.Attr.Health}|15");
        world.Inject("SetBase", $"{Hero}|{SkillDuelRift.Attr.Rage}|40");
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Execute}|{Dummy}");
        world.Advance(1);
        world.Counter(Dummy, "damage_taken_total").ShouldBe(115, "30 + 已损失 85 = 115（残血那一发按落地当步的血量算）");
    }

    /// <summary>
    /// <b>A59（warrior.bleed：堆叠三列 + period 频率 + Duration ≠ period 的正证）</b>：
    /// 台账（按下与过 commit 隔一步、每 5 步一跳、底伤 4、每层 +2、封顶 3 层、时长不重置）：
    /// 第 1 步过 commit → 第 6 步那一跳 4；第 7 步叠到 2 层 → 第 11 步那一跳 6；第 12 步叠到 3 层 →
    /// 第 16 步起每跳 8；第 17 步再按一次 = <c>stack_capped</c>。到期步始终是第 61 步（叠层不许把它推后），
    /// 共 11 跳、累计 82；第 61 步 S1 先把效果撤掉，所以没有第十二跳——这一条就是 <b>Duration ≠ period</b>
    /// 的正证：60 步的持续里跑了 11 个 5 步的周期，两者不是同一个数、也不是同一个口。
    /// </summary>
    [Fact]
    public void A59_流血叠层不重置时长_封顶后每跳按层数加值()
    {
        var world = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Bleed },
            dummyUnkillable: true);
        void Cast() => world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Bleed}|{Dummy}");
        int Stacks() => world.Stacks(Dummy, SkillDuelRift.Fx.BleedWound);
        double Total() => world.Counter(Dummy, "damage_taken_total");
        long? Expires() => world.Sets.Units[Dummy].Effects
            .Single(e => e.EffectId == SkillDuelRift.Fx.BleedWound).ExpiresAtTick;

        Cast();            // 第 0 步按下 → 第 1 步过 commit
        world.Advance(6);
        Total().ShouldBe(4, "第 6 步那一跳按 1 层 = 4");
        Stacks().ShouldBe(1);
        Expires().ShouldBe(61, "60 步的时长从 commit 那一步（第 1 步）起算");

        Cast();            // 第 6 步之后按下 → 第 7 步过 commit
        world.Advance(1);
        Stacks().ShouldBe(2, "叠层");
        Expires().ShouldBe(61, "「叠层不重置时长」那一档策略（AddStackKeepDuration）");
        world.Advance(4);   // 第 11 步那一跳按 2 层 = 6
        Total().ShouldBe(10);

        Cast();            // 第 11 步之后按下 → 第 12 步过 commit 叠到 3 层
        world.Advance(5);   // 第 16 步那一跳按 3 层 = 8
        Stacks().ShouldBe(3);
        Total().ShouldBe(18);

        Cast();            // 第 16 步之后按下 → 第 17 步过 commit：已经封顶
        world.Advance(1);
        world.HasEvent<EffectStackCapped>().ShouldBeTrue("封顶之后再来一次要念得出被封顶（形状 EffectStackCapped）");
        Stacks().ShouldBe(3, "封顶 = 层数不动，也不额外打一跳");

        world.Advance(44);   // 第 61 步：S1 先撤效果，所以这一步没有第十二跳
        CountEvents<PeriodTicked>(world).ShouldBe(11, "形状 period（PeriodTicked）：6/11/16/21/…/56 = 11 跳");
        Total().ShouldBe(82, "4 + 6 + 8 × 9 = 82");
        Stacks().ShouldBe(-1, "到期就是没了");
    }

    /// <summary>
    /// <b>A60（流血那一跳与回充那一跳不打架）</b>：验收口径点名的那条边界——两者共用 S3 那一个周期入口，
    /// 分岔的条件是 <see cref="EffectDef.IsChargeCarrier"/>（Infinite + 带周期 + 带 StackKey +
    /// <b>不带 modifier</b>）。流血带 modifier 所以照旧走「一跳一次结算」，稳固那一跳只把层数加回去。
    /// 台账（同一场子里推到第 61 步）：木桩挨 11 跳 × 4 = 44（第 61 步流血到期、S1 先撤效果）；
    /// 英雄第 20 步那发被抵挡吃掉、第 40 步挨 10、第 60 步「先回满再挨打」又把那层吃掉，
    /// 累计 10 血、抵挡计数 2、回充计数 1。同一份快照里两本账分开念：
    /// 英雄那行念 <c>charges=0</c>（第 60 步刚回满、同一步就被平A 吃掉 = A32 那一格的样子），
    /// 木桩那行念层数与到期步、<b>不冒充 charges=</b>；
    /// 计数一栏 <c>period=11 / recharge=1</c> 就是「共用一个入口、两条不同的那一跳」的读数。
    /// </summary>
    [Fact]
    public void A60_流血那一跳与回充那一跳共用入口但两条分开判()
    {
        var tables = SkillDuelRift.Tables();
        tables.Effect(SkillDuelRift.Fx.SteadyCharges).IsChargeCarrier.ShouldBeTrue("稳固 = 只授予 tag 的常驻周期效果（回充那一档）");
        tables.Effect(SkillDuelRift.Fx.BleedWound).IsChargeCarrier.ShouldBeFalse("流血带 modifier，所以它那一跳是结算不是回充——F4 / N6 守的就是这条线");

        var world = SkillDuelRift.Stage(Sparring(), heroAbilities: new[] { SkillDuelRift.Skill.Bleed },
            heroPassives: new[] { SkillDuelRift.Fx.SteadyCharges }, dummyUnkillable: true);
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Bleed}|{Dummy}");
        world.Advance(56);

        // 第 56 步：流血还在身上（第 61 步才到期），这一格读得出「两本账分开念」
        var bleeding = world.Snapshot();
        bleeding.Split('\n').Single(l => l.StartsWith($"f;{SkillDuelRift.Fx.BleedWound};",
                StringComparison.Ordinal)).ShouldNotContain("charges=", Case.Sensitive, "流血那一行念的是层数与到期步，不冒充充能");
        bleeding.ShouldContain("charges=0", Case.Sensitive, "第 56 步那一层抵挡还没回（回充周期是 60 步）");

        world.Advance(5);   // 第 61 步：S1 先把流血撤掉（所以没有第十二跳），回充已在第 60 步发生过
        world.Counter(Dummy, "damage_taken_total").ShouldBe(44, "11 跳 × 4");
        world.Stacks(Dummy, SkillDuelRift.Fx.BleedWound).ShouldBe(-1, "到期就是没了");
        world.Counter(Hero, "damage_taken_total").ShouldBe(10, "第 40 步那一发落地，第 20 与第 60 步都被抵挡吃掉");
        world.Counter(Hero, "damage_blocked").ShouldBe(2);
        world.Counter(Hero, "charge_recovered").ShouldBe(1, "回充那一跳加的是层数，不是伤害");
        CountEvents<PeriodTicked>(world).ShouldBe(11, "形状 period（PeriodTicked）：流血那一跳走了结算那条路");
        CountEvents<ChargeRefilled>(world).ShouldBe(1, "形状 recharge（ChargeRefilled）：回充那一跳走了加层那条路，同一格不重样");
        world.Snapshot().ShouldContain("charges=0", Case.Sensitive, "第 60 步先回满、同一步那发平A 又把它吃掉——A32 那一步念出来就是 charges=0");
    }

    /// <summary>
    /// <b>A61（archer.rain：同一次激活三条 Pending 互不顶掉 + 错峰到期 + 落空读得出为什么）</b>：
    /// 三条效果各带自己的延迟（+4 / +6 / +8 步）与落点上限（8 / 8 / 7 米，用距离曲线表达，不动列）。
    /// 台账（隔 7.5 米、英雄攻击 20）：第 5 步 20、第 7 步再 20、第 9 步那发按落地当步的距离折到 0
    /// ——落空，行为流念得出 <c>value=-0</c>（伤害符号 × 衰减 0），累计仍是 40。
    /// </summary>
    [Fact]
    public void A61_一次激活三条待决互不顶掉_第三发按当步距离落空()
    {
        var world = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 7.5 },
            heroAbilities: new[] { SkillDuelRift.Skill.Rain });
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rain}|{Dummy}");
        world.Advance(1);

        var pendings = world.Sets.Units[Hero].Pendings.OrderBy(p => p.Seq).ToList();
        pendings.Count.ShouldBe(3, "三发各占一条，后一条不许顶掉前一条（N-9 的归属口径）");
        pendings.Select(p => p.ExecuteAtTick).ShouldBe(new long[] { 5, 7, 9 }, "错峰到期 +4 / +6 / +8");
        pendings.Select(p => p.Seq).Distinct().Count().ShouldBe(3, "Seq 互不相同");

        world.Advance(4);
        world.Counter(Dummy, "damage_taken_total").ShouldBe(20, "第 5 步第一发");
        world.Advance(2);
        world.Counter(Dummy, "damage_taken_total").ShouldBe(40, "第 7 步第二发");
        world.Advance(2);
        world.Counter(Dummy, "damage_taken_total").ShouldBe(40, "第三发落空，不算承伤");
        world.Sets.Units[Hero].Pendings.ShouldBeEmpty();
        var lastSettle = world.LastEvent<EffectLanded>(Hero);
        lastSettle.ShouldNotBeNull("第三发也走完了结算那条路（形状 settle / EffectLanded）");
        double.IsNegative(lastSettle!.Value).ShouldBeTrue($"打空也要读得出为什么空：EffectLanded.Value 是负零（伤害符号 × 衰减 0，旧文本念成 value=-0），实到 {lastSettle.Value}");
    }

    /// <summary>
    /// <b>A62（archer.mark：tag 进计算 + 目标冷却走 attribute 口）</b>：
    /// 猎印授予 <c>status.marked</c> 后，自己那发箭雨按 <c>CalcInput.TagCount</c> 乘 1.2；
    /// 同时给目标那条技能的冷却乘 1.3——<c>cooldown.tank.guard</c> 是普通 attribute（N-7），
    /// 所以同一条效果在两处读得出：伤害那一处、冷却那一处。
    /// 台账（隔 7.5 米、攻击 20）：第一轮两发各 20 = 40（第三发落空，A61 那一格）；
    /// 第 10 步挂上猎印后第二轮两发各 24 → 累计 88；
    /// 冷却那一处：两位都在第 1~2 步过 commit，未标记者 20 步 → 第 21 步转好，
    /// 被标记者 20 × 1.3 = 26 → 第 28 步才转好（第 22 步那一格一个 Ready、一个还在 Cooling）。
    /// </summary>
    [Fact]
    public void A62_猎印让tag进计算_并把目标冷却乘上去()
    {
        var near = SkillDuelRift.Stage(new SkillTestWorld.Tuning { StartDistanceMeters = 7.5 },
            heroAbilities: new[] { SkillDuelRift.Skill.Mark, SkillDuelRift.Skill.Rain });
        near.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rain}|{Dummy}");
        near.Advance(9);
        near.Counter(Dummy, "damage_taken_total").ShouldBe(40, "第一轮整轮落地：两发各 20，第三发按 A61 那一格落空");

        near.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Mark}|{Dummy}");
        near.Advance(1);   // 第 10 步过 commit → status.marked 上身
        near.Snapshot().ShouldContain($"t;{SkillDuelRift.Tag.Marked};1");

        near.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Rain}|{Dummy}");
        near.Advance(9);   // 第 19 步：第二轮三发全落
        near.Counter(Dummy, "damage_taken_total").ShouldBe(88, "40 + 24 + 24（×1.2 走 Calc 的 tag 口），第三发照旧落空不加数");

        var plain = SkillDuelRift.Stage(Static, dummyAbilities: new[] { SkillDuelRift.Skill.Guard });
        plain.Inject("CastAbility", $"{Dummy}|{SkillDuelRift.Skill.Guard}|-");
        plain.Advance(1);   // 第 1 步过 commit
        plain.Ability(Dummy, SkillDuelRift.Skill.Guard).CooldownEndsAtTick.ShouldBe(21, "没被标记：冷却 20 步 → 第 21 步转好");

        var marked = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Mark },
            dummyAbilities: new[] { SkillDuelRift.Skill.Guard });
        marked.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Mark}|{Dummy}");
        marked.Advance(1);
        marked.Current(Dummy, SkillDuelRift.Attr.CooldownGuard).ShouldBe(1.3, "外部改冷却 = 一条普通 Mult 修饰符（N-7 那口）");
        marked.Inject("CastAbility", $"{Dummy}|{SkillDuelRift.Skill.Guard}|-");
        marked.Advance(1);   // 第 2 步过 commit
        marked.Ability(Dummy, SkillDuelRift.Skill.Guard).CooldownEndsAtTick.ShouldBe(28, "20 × 1.3 = 26 → 第 28 步才转好");

        marked.Advance(20);   // 第 22 步
        plain.Advance(21);    // 第 22 步
        plain.Ability(Dummy, SkillDuelRift.Skill.Guard).Phase.ShouldBe(AbilityPhase.Ready, "未标记那位第 21 步就转好了");
        marked.Ability(Dummy, SkillDuelRift.Skill.Guard).Phase.ShouldBe(AbilityPhase.Cooling, "被标记那位还差 6 步——这 6 步就是猎印那一笔乘出来的差");
    }

    /// <summary>
    /// <b>A63（rider.joust：两段 Pending 的独立性 + 后段带 CancelMatchingTags 打断在放的盾墙）</b>：
    /// 台账（隔 2 米、英雄攻击 20、第二段定值 12、木桩第 2 步起放盾墙）：
    /// 盾墙第 3 步过 commit → 承伤 0.5，所以第 4 步那段落 10、第 8 步那段落 6（累计 16）；
    /// 第二段的 <c>CancelMatchingTags = state.guard</c> 在第 9 步的取消队列里兑现——
    /// 打断的是<b>在放的那条技能</b>（念得出 <c>committed=1</c>），盾墙那条<b>效果</b>不随技能撤销，
    /// 承伤倍率照旧在（这正是 GAS 里 CancelAbility 的语义，本层把它写成两格读数）。
    /// </summary>
    [Fact]
    public void A63_两段待决各自到期_后段打断在放的盾墙()
    {
        var world = SkillDuelRift.Stage(Static, heroAbilities: new[] { SkillDuelRift.Skill.Joust },
            dummyAbilities: new[] { SkillDuelRift.Skill.Guard });
        world.Inject("CastAbility", $"{Hero}|{SkillDuelRift.Skill.Joust}|{Dummy}");
        world.Advance(1);

        var pendings = world.Sets.Units[Hero].Pendings.OrderBy(p => p.Seq).ToList();
        pendings.Count.ShouldBe(2, "一次激活两段，互不顶掉");
        pendings.Select(p => p.ExecuteAtTick).ShouldBe(new long[] { 4, 8 });

        world.Inject("CastAbility", $"{Dummy}|{SkillDuelRift.Skill.Guard}|-");
        world.Advance(3);   // 第 4 步第一段落地
        world.Counter(Dummy, "damage_taken_total").ShouldBe(10, "20 × 0.5（盾墙在）");

        world.Advance(4);   // 第 8 步第二段落地
        world.Counter(Dummy, "damage_taken_total").ShouldBe(16, "再加 12 × 0.5 = 6");

        world.Advance(1);   // 第 9 步：取消队列在 S3 开头兑现
        var interrupted = world.LastEvent<AbilityInterrupted>(Dummy);
        interrupted.ShouldNotBeNull("取消队列兑现要留一条（形状 interrupt / AbilityInterrupted）");
        interrupted!.Committed.ShouldBeTrue("已过 commit 的技能被打断（旧文本 committed=1）：Cost 与冷却都不返还");
        world.Ability(Dummy, SkillDuelRift.Skill.Guard).RejectedReason.ShouldBe(AbilityRejectReason.Interrupted);
        world.Current(Dummy, SkillDuelRift.Attr.DamageTaken).ShouldBe(0.5, "打断的是技能，不是身上那条效果——承伤倍率照旧在");
        world.Stacks(Dummy, SkillDuelRift.Fx.GuardWall).ShouldBe(1);
    }

    /// <summary>A53 用的那局：英雄带一条突斩与一层抵挡，木桩打不死、每 20 步平A 一下。</summary>
    private static SkillTestWorld StageForReplay()
        => SkillDuelRift.Stage(Sparring(), heroAbilities: new[] { SkillDuelRift.Skill.Rift },
            heroPassives: new[] { SkillDuelRift.Fx.SteadyCharges }, dummyUnkillable: true);

    /// <summary>只留一格检查点的剧本正文（检查点那份快照由调用方改过再递进来）。</summary>
    private static ReplayRunner.Checkpoint At(long tick, string snapshot)
        => new(tick, $"第 {tick} 步", snapshot, 0);

    /// <summary>级 1 那套骨架重放一份单格剧本（钩子与 H2 词表同一份，不开第二套）。</summary>
    private static LiveReplaySession ReplayWith(ReplayRunner.Checkpoint cp, ReplayRunner.AssertLevel level)
    {
        var world = StageForReplay();
        var script = new ReplayRunner.ReplayScript(1, [], [cp], AssertLevel: level);
        var session = new LiveReplaySession(script, new ReplayRunner.Hooks
        {
            Inject = (cmd, args) => world.Inject(cmd, args),
            Snapshot = () => world.ReplaySnapshot(),
            AdvanceTo = target => { while (world.Tick < target) world.Advance(1); },
        });
        session.RunAll();
        session.Checkpoints.Count.ShouldBe(1, "这一格真跑到了（不是空剧本）");
        return session;
    }

    /// <summary>段格式里首 token 是 <c>a</c> 的那些段（层记录的技能行）。</summary>
    private static List<string> SkillRows(string snapshot)
        => snapshot.Split(" | ", StringSplitOptions.None).Where(seg => seg.Split(' ')[0] == "a").ToList();

    /// <summary>
    /// 给某一类段各补一列——补的是<b>层文本里的一列</b>（照层自己的分号写法接在行尾），
    /// 也就是「下一次真给层改一列」时剧本会变成的样子；换空格这一步由宿主做，这里不动。
    /// </summary>
    private static string AppendColumn(string snapshot, string type, string column = ";extra=1")
        => string.Join(" | ", snapshot.Split(" | ", StringSplitOptions.None).Select(seg =>
            seg.Split(' ')[0] == type ? seg + column : seg));

    /// <summary>把第一条技能行的段首第二个 token（实体身份里的名字）换掉。</summary>
    private static string RenameFirstSkillRow(string snapshot)
    {
        var done = false;
        return string.Join(" | ", snapshot.Split(" | ", StringSplitOptions.None).Select(seg =>
        {
            var tokens = seg.Split(' ');
            if (done || tokens.Length < 2 || tokens[0] != "a") return seg;
            done = true;
            return string.Join(" ", new[] { "a", "zzzRenamed" }.Concat(tokens[2..]));
        }));
    }

    private static double Total(AbilityWorld world, string unitId)
        => world.Units[unitId].Counters.GetValueOrDefault("damage_taken_total");

    private static string FirstLineDiff(string a, string b)
    {
        var la = a.Split('\n');
        var lb = b.Split('\n');
        for (var i = 0; i < Math.Min(la.Length, lb.Length); i++)
        {
            if (la[i] != lb[i]) return $"第 {i + 1} 行\n  存: {la[i]}\n  回: {lb[i]}";
        }

        return la.Length == lb.Length ? "(无差异)" : $"行数 {la.Length} vs {lb.Length}";
    }

    private static string FirstFrameDiff(List<string> a, List<string> b)
    {
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            if (a[i] == b[i]) continue;
            var at = 0;
            while (at < Math.Min(a[i].Length, b[i].Length) && a[i][at] == b[i][at]) at++;
            return $"第 {i + 1} 格 @字符 {at}\n  甲:{Slice(a[i], at)}\n  乙:{Slice(b[i], at)}";
        }

        return a.Count == b.Count ? "(无差异)" : $"格数 {a.Count} vs {b.Count}";
    }

    private static string Slice(string text, int at)
        => text.Substring(Math.Max(0, at - 40), Math.Min(120, text.Length - Math.Max(0, at - 40)));
}
