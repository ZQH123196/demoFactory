#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 无限领主的技能表（<b>内容数据</b>）—— <b>每类单位一条普攻</b>，只收编单位↔单位那一半（Q12）。
///
/// <para><b>四个单位类别</b>：英雄三职业（游侠 / 战士 / 骑士）各一条，怪物一条。
/// 每条技能一行表项：射程与出手间隔取<b>卡上同一个数</b>（下面那几个常量就是那一份的住处，
/// 出生代码从这里读，表也从这里读 —— 一个数只有一处写得出），伤害走按名注册的纯函数
/// <see cref="BasicAttackCalcName"/>（规则只有一份，四条技能共用它）。</para>
///
/// <para><b>伤害为什么不写死在表里</b>：同一类单位的攻击力并不相同 ——
/// 英雄买装备后 <c>Attack</c> 会涨，波次怪物各波数值不同。所以效果那一行的加法修饰符是
/// <c>CalcFromEffectValue</c>，由纯函数现读<b>施法者自己那一列</b> attack。</para>
///
/// <para><b>技能行不带效果（EffectIds 为空）</b>：层的效果在 commit 那一步才交出去，
/// 而 commit 排在按下之后一步（S3 在 S4 之前跑）—— 那一笔伤害会晚一步落地，
/// 整局的战斗节奏跟着漂。所以这一条走的是宿主按下那一刻就施加效果的那条路
/// （与排兵布阵的踏地同一种形状：技能行只带激活检查，效果由宿主发起）。</para>
/// </summary>
public static class InfinityLordSkillTable
{
    // === 四条技能（每类单位一条） ===

    public const string RangerAbilityId = "ranger.basic_attack";
    public const string WarriorAbilityId = "warrior.basic_attack";
    public const string KnightAbilityId = "knight.basic_attack";
    public const string MonsterAbilityId = "monster.basic_attack";

    // === 四条命中效果（一条技能配一条：台账上读得出是哪一类单位打的那一下） ===

    public const string RangerHitEffectId = "fx.ranger_shot";
    public const string WarriorHitEffectId = "fx.warrior_blow";
    public const string KnightHitEffectId = "fx.knight_blow";
    public const string MonsterHitEffectId = "fx.monster_bite";

    /// <summary>伤害算式（按名注册的纯函数）—— 四条技能共用这一个名字，规则只有一份。</summary>
    public const string BasicAttackCalcName = "calc.il_basic_attack";

    /// <summary>层里那三列的词表（公共层一个缺省都不给，词表归原型）。</summary>
    public const string HealthAttribute = "hp";

    public const string AttackAttribute = "attack";

    public const string DamageTakenAttribute = "damage_taken";

    public const string MoveSpeedAttribute = "move_speed";

    /// <summary>出手间隔那条冷却属性的前缀（层里按「前缀 + 技能 Id」拼）。</summary>
    public const string CooldownAttributePrefix = "cooldown.";

    /// <summary>定身 tag —— 移动激活检查问的就是它（本 ticket只把形状铺上，没人授予它）。</summary>
    public const string RootedTag = "status.rooted";

    /// <summary>抵挡 tag —— 本原型没有挡伤害的机制，这一列只把公共层那七列填满。</summary>
    public const string DamageBlockTag = "status.block_ready";

    // === 射程与出手间隔（卡上那两个数的住处） ===

    /// <summary>普攻射程（米）—— 英雄按职业分档，与出生时写进 <c>Unit.AttackRange</c> 的是同一个数。</summary>
    public static double RangeMetersOf(HeroClass cls) => cls switch
    {
        HeroClass.Ranger => 6.0,
        HeroClass.Warrior => 1.5,
        HeroClass.Knight => 2.0,
        _ => 6.0,
    };

    /// <summary>怪物普攻射程（米）—— 巢穴涓流与剧本波次同规格。</summary>
    public const double MonsterRangeMeters = 1.2;

    /// <summary>英雄出手间隔（步）—— 写进 <c>Unit.AttackCooldownTicks</c> 的就是它。</summary>
    public const int HeroCooldownTicks = 6;

    /// <summary>怪物出手间隔（步）。</summary>
    public const int MonsterCooldownTicks = 8;

    /// <summary>
    /// 一类单位的出手间隔（步）—— <b>卡上那个数，出生代码与技能表都从这一处读</b>（一个数只有一处写得出）。
    ///
    /// <para><b>层里那把尺与它逐字相同，不增不减</b>：这一侧四条普攻都是零前摇（<c>PreSteps = 0</c>），
    /// 它们在按下那一步就过 commit（<c>AbilityStepper.TryActivate</c> 里那一发）。层里的冷却从 commit
    /// 起算 ⇒ 按下在第 T 步、到期步 T + 出手间隔，而世界那把尺同一步归零：两把尺同起点、同终点。
    /// 这里早先写的是「卡上数减一」，那是拿算术去补 commit 晚一步的缝 —— 表里的数因此与卡上不同，
    /// 而「表里数 == 卡面数」这句话就再也验不了。</para>
    ///
    /// <para>对齐由两条用例各钉一半：<c>出手的步差与卡上的出手间隔逐字相同</c> 钉实际步差，
    /// <c>表里那条冷却与卡上的出手间隔逐字相同</c> 钉表里那个数。</para>
    /// </summary>
    public static int CooldownTicksOf(Faction side)
        => side == Faction.Hero ? HeroCooldownTicks : MonsterCooldownTicks;

    /// <summary>这个单位领哪一条普攻：英雄按职业，怪物一律一条。</summary>
    public static string AbilityOf(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Side == Faction.Hero ? AbilityOfClass(unit.Class) : MonsterAbilityId;
    }

    /// <summary>职业 → 那一条技能。<b>这是一张表，不是一串 if</b>：加第四个职业只动这一处。</summary>
    public static string AbilityOfClass(HeroClass cls) => cls switch
    {
        HeroClass.Ranger => RangerAbilityId,
        HeroClass.Warrior => WarriorAbilityId,
        HeroClass.Knight => KnightAbilityId,
        _ => RangerAbilityId,
    };

    /// <summary>那一条技能对应的命中效果。</summary>
    public static string HitEffectOf(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Side == Faction.Hero ? HitEffectOfClass(unit.Class) : MonsterHitEffectId;
    }

    /// <summary>职业 → 那一条命中效果（与技能同表同序）。</summary>
    public static string HitEffectOfClass(HeroClass cls) => cls switch
    {
        HeroClass.Ranger => RangerHitEffectId,
        HeroClass.Warrior => WarriorHitEffectId,
        HeroClass.Knight => KnightHitEffectId,
        _ => RangerHitEffectId,
    };

    /// <summary>那一条技能的射程（米）—— 激活检查与出生写进卡里的是同一个数。</summary>
    public static double RangeMetersOfAbility(string abilityId) => abilityId switch
    {
        RangerAbilityId => RangeMetersOf(HeroClass.Ranger),
        WarriorAbilityId => RangeMetersOf(HeroClass.Warrior),
        KnightAbilityId => RangeMetersOf(HeroClass.Knight),
        MonsterAbilityId => MonsterRangeMeters,
        _ => throw new KeyError($"表里没有这条普攻：{abilityId}"),
    };

    /// <summary>这条技能是哪一类单位的（层里那条冷却的步数按它算）。</summary>
    public static Faction SideOfAbility(string abilityId) => abilityId switch
    {
        MonsterAbilityId => Faction.Monster,
        RangerAbilityId or WarriorAbilityId or KnightAbilityId => Faction.Hero,
        _ => throw new KeyError($"表里没有这条普攻：{abilityId}"),
    };

    /// <summary>表里那四条技能 Id（检查清单与体检单挨个问一遍用）。</summary>
    public static readonly string[] AbilityIds =
    [
        RangerAbilityId, WarriorAbilityId, KnightAbilityId, MonsterAbilityId,
    ];

    /// <summary>
    /// 技能表本体 —— 四条普攻 + 四条命中效果。谁要用这张表都从这一发取（公共层不认识它们）。
    /// </summary>
    public static AbilityTables BuildTables()
    {
        var tables = new AbilityTables();

        // 命中效果：一次出手 = 立刻生效的一条 hp 加法，数值走那条共用的纯函数（产物是负数＝扣血）。
        foreach (var effectId in HitEffectIds)
        {
            tables.Register(new EffectDef(
                effectId,
                EffectKind.Instant,
                new[] { new ModifierSpec(HealthAttribute, ModifierMode.Add, 0, CalcFromEffectValue: true) },
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { BasicAttackCalcName },
                new Dictionary<string, double>(),
                CalcSeed: 0,
                PeriodTicks: 0,
                Array.Empty<string>(),
                Array.Empty<(string OnEvent, string EffectId)>()));
        }

        // 技能行：不带效果（效果由宿主在按下那一刻施加），只带激活检查的两把尺。
        // 冷却那格写的就是卡上那个数（CooldownTicksOf）—— 层里那把尺是唯一真值。
        foreach (var abilityId in AbilityIds)
        {
            tables.Register(new AbilityDef(
                abilityId,
                PreSteps: 0,
                CooldownTicks: CooldownTicksOf(SideOfAbility(abilityId)),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                string.Empty,
                0,
                RangeMeters: RangeMetersOfAbility(abilityId)));
        }

        // 两条探针效果：一条只授予定身 tag，一条只改移速倍率。两条都是常驻（挂上就一直在）。
        tables.Register(EffectDef.OverTime(Probe.RootedEffectId, Array.Empty<ModifierSpec>(),
            durationTicks: 0, kind: EffectKind.Infinite, grantedTags: new[] { RootedTag }));
        tables.Register(EffectDef.OverTime(Probe.SlowEffectId,
            new[] { new ModifierSpec(MoveSpeedAttribute, ModifierMode.Mult, Probe.SlowSpeedMult) },
            durationTicks: 0, kind: EffectKind.Infinite));

        return tables;
    }

    /// <summary>
    /// <b>探针件</b>（与排兵布阵检查清单那一批同一种形状）：不占技能名额，也不是内容 ——
    /// 它们只为把两条形状摆出来：<b>移动激活检查</b>（定身 tag）与<b>每步移速倍率</b>（乘性减速）。
    ///
    /// <para>默认一个都不挂：玩法里没有任何东西授予它们，整局演化与本 ticket之前逐字相同。
    /// 谁要用谁施加（<c>ApplyEffect</c> 那一发动词，或层上的 <c>ApplyEffect</c>）——
    /// 用例与调试直达口是仅有的两个调用方。</para>
    /// </summary>
    public static class Probe
    {
        /// <summary>只授予定身 tag 的常驻效果（激活检查问的就是这个 tag）。</summary>
        public const string RootedEffectId = "fx.rooted_probe";

        /// <summary>只改移速倍率的常驻效果（乘 0.5 —— 走位读数上读得出减半）。</summary>
        public const string SlowEffectId = "fx.slow_probe";

        public const double SlowSpeedMult = 0.5;
    }

    /// <summary>命中效果 → 它属于哪一方（表与技能同源，漂了编译就断）。</summary>
    private static readonly string[] HitEffectIds =
    [
        RangerHitEffectId,
        WarriorHitEffectId,
        KnightHitEffectId,
        MonsterHitEffectId,
    ];

    /// <summary>
    /// 那套纯函数（按名注册，公共层里不出现任何一个函数名）。
    /// 普攻这一条只做一件事：取<b>施法者自己那一列 attack</b>，产物取负（负数＝扣血）。
    /// </summary>
    public static CalcRegistry BuildCalcs() => new CalcRegistry()
        .Register(BasicAttackCalcName,
            i => -(CalcInput.Attr(i.Source, AttackAttribute) + i.Working));
}
