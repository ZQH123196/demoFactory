#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using Shouldly;
using Xunit;

namespace DemoFactory.Tests;

/// <summary>
/// <b>第一个场子的「清单注册壳」</b>：16 条检查清单在场子这一侧要问的每一格，这里各摆一个载体，
/// 外加词表、纯函数与摆位。<b>全部字面量都在本文件里</b>——场子那几份一份都不许有（钉它的是 S8）。
///
/// 与 <see cref="SkillStageSample"/> 的分工：那份是「一条真技能跑通时间线」的示范注册壳（最小）；
/// 这一份是「16 格形状各摆一个载体」的清单注册壳。两者递的是同一种东西（技能表 + 纯函数 + 单位属性），
/// 场子分不出这两份——<b>换注册壳场子一个字都不用改</b>，这就是「通用」那一条口径的机器问法。
///
/// 每一格摆成什么样（数字全部来自本文件，红了能一口说出差在哪一步）：
/// | 清单要问的形状 | 本壳的载体 | 手算 |
/// |---|---|---|
/// | 有前摇 + 有消耗 + 有冷却 + 立即结算 | <see cref="Skill"/>.<see cref="Windup"/> | 前摇 3 步、过 commit 扣 <see cref="Attr"/>.<see cref="Focus"/> 30、冷却 20 步、那一刀写死 20 血 |
/// | 有前摇且在放中授予一个 tag | <see cref="Skill"/>.<see cref="ChannelSlash"/> | 前摇 2 步（第 2 步过 commit）、授予 <see cref="Tag"/>.<see cref="Channel"/>、那一刀 15 血 |
/// | 过 commit 登记成待决、按落地现算 | <see cref="Skill"/>.<see cref="Lob"/> | 前摇 1 步、第 1 步过 commit、+6 步落，伤害 = commit 抄下的 <see cref="Attr"/>.<see cref="Power"/> 20 |
/// | 一次激活登记两条待决 | <see cref="Skill"/>.<see cref="Pair"/> | +3 步落 3 血、+7 步落 5 血，各占一条、互不顶掉 |
/// | 入口被 tag 挡住 | <see cref="Skill"/>.<see cref="Sealed"/>（<c>BlockedTags</c> = <see cref="Tag"/>.<see cref="Silence"/>） | 挂了沉默探针就放不下，且念得出 BlockedByTag |
/// | 时长 ≠ 周期 + 可叠层 | <see cref="Fx"/>.<see cref="WoundWound"/> | 时长 60 步、周期 5 步一跳 4 血、封顶 3 层每层 +2 |
/// | 显式打断 | <see cref="Fx"/>.<see cref="Interrupt"/> | 命中 <see cref="Tag"/>.<see cref="Channel"/>，下一步 S3 开头留一行 committed=0 |
/// | 效果段授予同一个 tag | <see cref="Fx"/>.<see cref="TagGrant"/> | 与在放中的能力段合到一张计数表 ⇒ <c>t;drill.channel;2</c> |
/// | 只授予沉默 tag | <see cref="Fx"/>.<see cref="SilenceProbe"/> | 常驻（Infinite），挂了才谈得上「入口被挡住」 |
/// | 同段两笔 Override 撞界 | <see cref="Attr"/>.<see cref="Guard"/>（界 [0.2, 1]） | 0.5 → 0.1 → 界抬回 0.2 |
///
/// <b>摆位只有一根直线那一档</b>（出手那一个在 0 米、挨打那个在 <see cref="DefaultGapMeters"/> 米）：
/// 清单是「功能面验过了没有」，地形条件那几格由场子自己的台账（<c>SkillStageTerrainLedgerTests</c>）判，
/// 摆成二维不会让任何一格更真，只会让手算的数多一个根号。
/// </summary>
public static class StageChecklistShell
{
    // === 单位与属性的名字（场子只认键，字面量全在注册壳这一侧）===

    /// <summary>出手那一个。</summary>
    public const string CasterId = "drill.caster";

    /// <summary>挨打那一个（钉在原地，不还手）。</summary>
    public const string TargetId = "drill.target";

    /// <summary>属性词表。本壳自带，不与任何原型重名。</summary>
    public static class Attr
    {
        /// <summary>血量：扣血与判死读它。</summary>
        public const string Vitality = "drill.vitality";

        /// <summary>血量上限：只给表现层念血条用。</summary>
        public const string VitalityMax = "drill.vitality_max";

        /// <summary>出手强度：清单里「延迟那一发」的伤害按它算（读 commit 抄下的快照）。</summary>
        public const string Power = "drill.power";

        /// <summary>承伤倍率：没有这条属性时按 1.0 处理。</summary>
        public const string Incoming = "drill.incoming";

        /// <summary>带消耗的资源：清单里唯一那条带消耗的技能扣它。</summary>
        public const string Focus = "drill.focus";

        /// <summary>「同段两笔 Override 撞界」那一格用的探针属性（界 [0.2, 1]，见 <see cref="ApplyBounds"/>）。</summary>
        public const string Guard = "drill.guard";

        /// <summary>冷却时长的属性名前缀：层里读的是「前缀 + 技能Id」，缺了按 1.0 处理。</summary>
        public const string CooldownPrefix = "drill.cooldown.";
    }

    /// <summary>tag 词表。</summary>
    public static class Tag
    {
        /// <summary>「在放中」：能力段与效果段各能授予它（F5 要两段合到同一张计数表）。</summary>
        public const string Channel = "drill.channel";

        /// <summary>沉默：入口那一格被它挡住（F2）。</summary>
        public const string Silence = "drill.silence";

        /// <summary>定身：挂着它位移问一次就被挡住（场子自带的那条激活检查）。</summary>
        public const string Rooted = "drill.rooted";

        /// <summary>抵挡：挂着它伤害落不上去（层里第三处激活检查）。</summary>
        public const string Warded = "drill.warded";
    }

    /// <summary>事件触发的订阅键（ticket 10 收进公共层配置的那两列）。</summary>
    public static class Trigger
    {
        /// <summary>承伤那一发（含被倍率归零那一档）。</summary>
        public const string DamageTaken = "drill.damage_taken";

        /// <summary>一条效果落地。</summary>
        public const string EffectApplied = "drill.effect_applied";
    }

    /// <summary>技能词表。五条各顶一组形状，都带射程激活检查（摆位 2 米 < 3 米）。</summary>
    public static class Skill
    {
        public const string Windup = "drill.windup";

        public const string ChannelSlash = "drill.channel_slash";

        public const string Lob = "drill.lob";

        public const string Pair = "drill.pair";

        public const string Sealed = "drill.sealed";

        public const string Rush = "drill.rush";
    }

    /// <summary>效果词表。</summary>
    public static class Fx
    {
        public const string WindupHit = "drill.windup_hit";

        public const string ChannelHit = "drill.channel_hit";

        public const string LobHit = "drill.lob_hit";

        public const string PairFirst = "drill.pair_first";

        public const string PairSecond = "drill.pair_second";

        public const string RushHit = "drill.rush_hit";

        public const string WoundWound = "drill.wound_wound";

        public const string Interrupt = "drill.interrupt";

        public const string TagGrant = "drill.tag_grant";

        public const string SilenceProbe = "drill.silence_probe";
    }

    /// <summary>纯函数的词表。</summary>
    public static class Fn
    {
        public const string ByPower = "drill.by_power";

        public const string Negate = "drill.negate";
    }

    // === 这一份的三个数（台账能复现，红了才知道差在哪一格）===

    /// <summary>出手强度：延迟那一发的伤害就按它算（<see cref="Skill"/>.<see cref="Lob"/> 走它）。</summary>
    public const double Power = 20;

    /// <summary>出手那一个开局带的资源：清单里唯一带消耗的那条技能扣它，30 一笔，够 F1 两个对照支各扣一次。</summary>
    public const double Focus = 100;

    /// <summary><see cref="Skill"/>.<see cref="Windup"/>：前摇 3 步、过 commit 扣 <see cref="Focus"/> 30、冷却 20 步、那一刀写死 20 血。</summary>
    public const long WindupPreSteps = 3;

    public const long WindupCooldownSteps = 20;

    public const double WindupCost = 30;

    public const double WindupDamage = 20;

    /// <summary><see cref="Skill"/>.<see cref="ChannelSlash"/>：前摇 2 步、授予 <see cref="Tag"/>.<see cref="Channel"/>、那一刀 15 血。</summary>
    public const long ChannelPreSteps = 2;

    public const double ChannelDamage = 15;

    /// <summary><see cref="Skill"/>.<see cref="Lob"/>：前摇 1 步、第 1 步过 commit，+6 步落，伤害 = commit 抄下的 <see cref="Power"/>。</summary>
    public const long LobPreSteps = 1;

    public const long LobDelaySteps = 6;

    /// <summary><see cref="Skill"/>.<see cref="Pair"/>：前摇 0 步（第 1 步过 commit），两段错峰落地。</summary>
    public const long PairFirstDelay = 3;

    public const long PairSecondDelay = 7;

    public const double PairFirstDamage = 3;

    public const double PairSecondDamage = 5;

    /// <summary><see cref="Fx"/>.<see cref="WoundWound"/>：时长 60 步、周期 5 步一跳 4 血、封顶 3 层每层 +2。</summary>
    public const long WoundDurationSteps = 60;

    public const long WoundPeriodSteps = 5;

    public const double WoundDamagePerTick = 4;

    /// <summary>同段两笔 Override 撞界那一格的两个值与界（<see cref="Attr"/>.<see cref="Guard"/> 的界是 [0.2, 1]）。</summary>
    public const double ProbeFirstMagnitude = 0.5;

    public const double ProbeSecondMagnitude = 0.1;

    public const double ProbeClampedTo = 0.2;

    /// <summary>五条技能共同的射程（米）。摆位默认 2 米 < 3 米，所以清单全程在射程之内。</summary>
    public const double RangeMeters = 3;

    /// <summary>「一次激活两条待决」那一档的射程：5 米，于是台账 L1 那一圈从 3~5 米。</summary>
    public const double RushRangeMeters = 5;

    /// <summary>目标满血：清单里算各条伤害的起算点。</summary>
    public const double TargetHealth = 100;

    /// <summary>事件流环形容量（条数）。观测窗口由这一侧定，场子不替它挑。</summary>
    public const int EventCapacity = 2000;

    /// <summary>
    /// <b>长时段那一份的步数</b>（ticket 07）：1200 步 = 120 秒。
    /// 「长时段」与「单局」的唯一差别就是这一格——场子本身不认「长时段」，
    /// <c>Advance(n)</c> 本来就收任意 <c>n</c>，所以这三个数全是注册壳这一侧的参数。
    /// </summary>
    public const long LongRunSteps = 1200;

    /// <summary>
    /// <b>长时段那一份的观测窗口容量</b>（条数）：默认那 2000 条在 1200 步里会被挤掉，
    /// 挤掉了就读不到「周期到底跳了几次」——观测窗口是这一侧按局配的，场子不替它挑（「三条铁律」 第三条）。
    /// </summary>
    public const int LongRunEventCapacity = 20000;

    /// <summary>
    /// <b>长时段那一份的账本上限</b>：血量与资源共用这一个数。
    /// 默认那 100 是给「单局清单」配的（够验 16 条里的每一格），
    /// 1200 步一局里叠层跳伤与连续放技能会把它打穿——<b>目标一死，周期与到期那几格就没得量了</b>。
    /// 这是场地/账本条件，不是技能条件，所以它是注册壳的一格而不是场子的一格。
    /// </summary>
    public const double LongRunCeiling = 10000;

    /// <summary>默认摆位：出手那个在 0 米、挨打那个在 2 米（射程之内）。</summary>
    public const double DefaultGapMeters = 2;

    /// <summary>
    /// 技能栏里带射程激活检查的那几条（技能 Id + 射程米）。
    /// <b>台账逐条判射程边界两侧用的就是这一份</b>：往注册壳里加一条带射程的技能，
    /// 边界两侧那几格自动多一档，台账与用例都不用改（<c>SkillStageTerrainLedgerTests</c> 的 L1）。
    /// </summary>
    public static readonly IReadOnlyList<(string AbilityId, double RangeMeters)> RangedSkills = new[]
    {
        (Skill.Windup, RangeMeters),
        (Skill.ChannelSlash, RangeMeters),
        (Skill.Lob, RangeMeters),
        (Skill.Pair, RangeMeters),
        (Skill.Sealed, RangeMeters),
        (Skill.Rush, RushRangeMeters),
    };

    /// <summary>
    /// 递一份注册口给场子：技能表 + 纯函数 + 单位属性（词表跟着单位属性一起递）。
    /// <paramref name="eventCapacity"/> 是观测窗口那一格（ticket 07 的长时段按局配一个装得下的数）。
    /// </summary>
    public static SkillStageSpec Spec(double gapMeters = DefaultGapMeters, int eventCapacity = EventCapacity)
    {
        var caster = new StageUnit
        {
            UnitId = CasterId,
            X = 0,
            Z = 0,
            FacingDegrees = 0,
            Attributes = new[]
            {
                (Attr.Vitality, TargetHealth),
                (Attr.VitalityMax, TargetHealth),
                (Attr.Power, Power),
                (Attr.Incoming, 1.0),
                (Attr.Focus, Focus),
                (Attr.Guard, 1.0),
            },
            AbilityIds = new[]
            {
                Skill.Windup, Skill.ChannelSlash, Skill.Lob, Skill.Pair, Skill.Sealed, Skill.Rush,
            },
        };

        var target = new StageUnit
        {
            UnitId = TargetId,
            X = gapMeters,
            Z = 0,
            Attributes = new[]
            {
                (Attr.Vitality, TargetHealth),
                (Attr.VitalityMax, TargetHealth),
                (Attr.Power, 0.0),
                (Attr.Incoming, 1.0),
                (Attr.Guard, 1.0),
            },
        };

        return new SkillStageSpec
        {
            Tables = Tables(),
            Calcs = Calcs(),
            Roster = new StageRoster
            {
                MaxHealthAttribute = Attr.VitalityMax,
                Config = new AbilityLayerConfig
                {
                    HealthAttribute = Attr.Vitality,
                    DamageTakenAttribute = Attr.Incoming,
                    CooldownAttributePrefix = Attr.CooldownPrefix,
                    RootedTag = Tag.Rooted,
                    DamageBlockTag = Tag.Warded,
                    DamageTakenEvent = Trigger.DamageTaken,
                    EffectAppliedEvent = Trigger.EffectApplied,
                    EventCapacity = eventCapacity,
                },
                Units = new[] { caster, target },
            },
        };
    }

    /// <summary>
    /// 起一局：注册口递进去、上下界补上、随机项关掉。
    /// <b>上下界为什么在这里补</b>：<see cref="StageUnit"/> 那一列只有「属性名 + 基础值」，
    /// 没有界；而清单 F8 要的就是「两笔 Override 之后由界抬回 <see cref="ProbeClampedTo"/>」，
    /// 所以这一格由注册壳在起局后拧一次（<c>AbilityWorld.SetLimits</c>，与 H1 那份假数据宿主同一手法）。
    /// <b>随机项关掉</b>：清单的 D1/D4 要两跑逐字等，而层里没有任何一格问随机源——关掉是让这条口径
    /// 不依赖「这一批表碰巧没用到随机」。
    /// </summary>
    public static SkillStage NewStage(double gapMeters = DefaultGapMeters, int seed = 1,
        int eventCapacity = EventCapacity, double ceiling = TargetHealth)
    {
        var stage = new SkillStage(Spec(gapMeters, eventCapacity), seed) { RandomEnabled = false };
        ApplyBounds(stage, ceiling);
        return stage;
    }

    /// <summary>
    /// <b>长时段那一份的起手（ticket 07）</b>：<b>还是第一个场子，只是把三格参数按长时段拧一下</b>——
    /// 步数 <see cref="LongRunSteps"/>、观测窗口 <see cref="LongRunEventCapacity"/>、账本上限
    /// <see cref="LongRunCeiling"/>。场子与技能层一个字都没改，这就是「长时段并进第一个场子的参数」那笔裁的兑现处。
    ///
    /// 上限拧开之后还要把两条基值从写路径拧上去（<c>SetBase</c>，「桥只转发」 唯一那条写路径）：
    /// 界是 <c>Current</c> 的夹取范围，基值不上去，<c>Current</c> 就被压在上限那格，
    /// 1200 步里的账读出来永远是同一个数。
    /// </summary>
    public static SkillStage NewLongRunStage()
    {
        var stage = NewStage(eventCapacity: LongRunEventCapacity, ceiling: LongRunCeiling);
        stage.Inject(AbilityVerbs.SetBase, $"{TargetId}|{Attr.Vitality}|{LongRunCeiling}");
        stage.Inject(AbilityVerbs.SetBase, $"{CasterId}|{Attr.Focus}|{LongRunCeiling}");
        return stage;
    }

    /// <summary>
    /// 把三条属性（血量 / 资源 / 探针）的上下界拧到台账那一档。
    /// <paramref name="ceiling"/> 是血量与资源共用的上限（默认那档是 <see cref="TargetHealth"/>），
    /// 探针那一列的界与它无关，仍是 [<see cref="ProbeClampedTo"/>, 1]。
    /// </summary>
    public static void ApplyBounds(SkillStage stage, double ceiling = TargetHealth)
    {
        foreach (var unitId in new[] { CasterId, TargetId })
        {
            stage.Stepper.World.SetLimits(unitId, Attr.Vitality, 0, ceiling);
            stage.Stepper.World.SetLimits(unitId, Attr.Focus, 0, ceiling);
            stage.Stepper.World.SetLimits(unitId, Attr.Guard, ProbeClampedTo, 1.0);
        }
    }

    /// <summary>技能表：一条技能一张（或两张）效果。</summary>
    public static AbilityTables Tables()
    {
        var tables = new AbilityTables();

        // 立即结算的一刀：写死的负数加到血量上就是扣血（走层里「扣血那一段」那一份，不另写一句）。
        tables.Register(EffectDef.Instant(Fx.WindupHit, new ModifierSpec(Attr.Vitality, ModifierMode.Add, -WindupDamage)));
        tables.Register(EffectDef.Instant(Fx.ChannelHit, new ModifierSpec(Attr.Vitality, ModifierMode.Add, -ChannelDamage)));

        // 延迟那一发：数值走 Calc 链，链上读的是过 commit 那一刻抄下来的出手强度（F6 那一格）。
        tables.Register(new EffectDef(Fx.LobHit, EffectKind.Instant,
            new[] { new ModifierSpec(Attr.Vitality, ModifierMode.Add, 0, CalcFromEffectValue: true) },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.ByPower }, new Dictionary<string, double>(), 0, 0,
            Array.Empty<string>(), Array.Empty<(string, string)>())
        {
            DelayTicks = LobDelaySteps,
        });

        // 现场加的一条：前摇 0 步、射程 5 米（比其余四条远一档），写死 7 血。
        tables.Register(EffectDef.Instant(Fx.RushHit, new ModifierSpec(Attr.Vitality, ModifierMode.Add, -7)));

        // 一次激活登记两条待决：错峰各自落地、互不顶掉（F7）。
        tables.Register(EffectDef.Delayed(Fx.PairFirst,
            new ModifierSpec(Attr.Vitality, ModifierMode.Add, -PairFirstDamage), PairFirstDelay));
        tables.Register(EffectDef.Delayed(Fx.PairSecond,
            new ModifierSpec(Attr.Vitality, ModifierMode.Add, -PairSecondDamage), PairSecondDelay));

        // 持续伤害：时长与周期是两列、可叠层（F4）。
        tables.Register(EffectDef.OverTime(Fx.WoundWound, new[]
        {
            new ModifierSpec(Attr.Vitality, ModifierMode.Add, 0, CalcFromEffectValue: true),
        }, durationTicks: WoundDurationSteps, periodTicks: WoundPeriodSteps, stackKey: "drill.wound",
            maxStacks: 3, magnitudePerStack: -2, calcNames: new[] { Fn.Negate }, calcSeed: WoundDamagePerTick));

        // 打断方：带 CancelMatchingTags，命中「在放中」那条（F3 / D5）。
        tables.Register(EffectDef.OverTime(Fx.Interrupt, Array.Empty<ModifierSpec>(),
            durationTicks: 10, cancelMatchingTags: new[] { Tag.Channel }));

        // 效果段授予同一个 tag：与能力段合到同一张计数表（F5）。
        tables.Register(EffectDef.OverTime(Fx.TagGrant, Array.Empty<ModifierSpec>(),
            durationTicks: 80, grantedTags: new[] { Tag.Channel }));

        // 沉默探针：常驻（Infinite），挂了才谈得上「入口被挡住」（F2）。
        tables.Register(EffectDef.OverTime(Fx.SilenceProbe, Array.Empty<ModifierSpec>(), durationTicks: 0,
            kind: EffectKind.Infinite, grantedTags: new[] { Tag.Silence }));

        tables.Register(new AbilityDef(Skill.Windup, WindupPreSteps, WindupCooldownSteps,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fx.WindupHit }, Attr.Focus, WindupCost, RangeMeters));
        tables.Register(new AbilityDef(Skill.ChannelSlash, ChannelPreSteps, 0,
            new[] { Tag.Channel }, Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fx.ChannelHit }, string.Empty, 0, RangeMeters));
        tables.Register(new AbilityDef(Skill.Lob, LobPreSteps, 0,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fx.LobHit }, string.Empty, 0, RangeMeters));
        tables.Register(new AbilityDef(Skill.Pair, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fx.PairFirst, Fx.PairSecond }, string.Empty, 0, RangeMeters));
        tables.Register(new AbilityDef(Skill.Sealed, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), new[] { Tag.Silence },
            new[] { Fx.ChannelHit }, string.Empty, 0, RangeMeters));
        tables.Register(new AbilityDef(Skill.Rush, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fx.RushHit }, string.Empty, 0, RushRangeMeters));

        return tables;
    }

    /// <summary>纯函数：伤害取施法者抄下来的出手强度并翻成负值；周期那一跳把种子翻负。</summary>
    public static CalcRegistry Calcs() => new CalcRegistry()
        .Register(Fn.ByPower, i => -(CalcInput.Attr(i.Source, Attr.Power) + i.Working))
        .Register(Fn.Negate, i => -i.Working);
}

/// <summary>
/// <b>16 条检查清单在第一个场子（<see cref="SkillStage"/>）上跑的那一层壳</b>。
///
/// 分工照仓里另外三份注册壳：壳只回答「哪一个兵、哪一条技能、哪一个数字」，<b>一个字都不带断言</b>——
/// 断言只有 <see cref="AbilityChecklistTests"/> 那一份，它住在公共层（<c>src/GugaFramework/Tests/</c>），
/// 本壳不认得任何原型的世界。本壳自己那份词表与表在 <see cref="StageChecklistShell"/>。
///
/// 写路径只有 <see cref="AbilityVerbs"/> 那一份词表（「桥只转发」）：本文件里一个动词名字面量都不许有。
/// 三个口各走场子自己那一个：时间 = <see cref="SkillStage.Advance"/>、状态 = <see cref="SkillStage.DebugSnapshot"/>、
/// 注入 = <see cref="SkillStage.Inject"/>——<b>壳不新写第三条路</b>。
/// </summary>
internal sealed class ChecklistStageHost : AbilityChecklistTests.IChecklistHost, IAbilityVerbSink
{
    /// <summary>新鲜的场子才有那一个对象；从快照分叉出来的那一支没有（见 <see cref="Fork"/>）。</summary>
    private readonly SkillStage? _stage;

    /// <summary>层的那本状态：<c>stage.Stepper.World</c>，分支则是 <c>branch.World</c>——读数一律走这一份。</summary>
    private readonly AbilityWorld _sets;

    /// <summary>推进与快照那一个口：<c>stage.Stepper</c>，分支则是分支自己那座步进器。</summary>
    private readonly AbilityStepper _stepper;

    private ChecklistStageHost(SkillStage? stage, AbilityWorld sets, AbilityStepper stepper)
    {
        _stage = stage;
        _sets = sets;
        _stepper = stepper;
    }

    /// <summary>从零起一场（D1/D4 两跑、F1/F2/F8 的对照支、D7 的两叉都要新的一场）。</summary>
    public static ChecklistStageHost Fresh() => For(StageChecklistShell.NewStage());

    /// <summary>把一座起好的场子包成壳（台账与用例要按自己的摆位起局时走这一发）。</summary>
    public static ChecklistStageHost For(SkillStage stage) => new(stage, stage.Stepper.World, stage.Stepper);

    public string Name => "第一个场子";

    public AbilityChecklistTests.Shapes Shapes { get; } = new()
    {
        Caster = StageChecklistShell.CasterId,
        Target = StageChecklistShell.TargetId,
        Windup = StageChecklistShell.Skill.Windup,
        Channeled = StageChecklistShell.Skill.ChannelSlash,
        Flying = StageChecklistShell.Skill.Lob,
        TwoPending = StageChecklistShell.Skill.Pair,
        Silenced = StageChecklistShell.Skill.Sealed,
        Wound = StageChecklistShell.Fx.WoundWound,
        Interrupter = StageChecklistShell.Fx.Interrupt,
        TagGrant = StageChecklistShell.Fx.TagGrant,
        SilenceProbe = StageChecklistShell.Fx.SilenceProbe,
        ChanneledTag = StageChecklistShell.Tag.Channel,
        SilenceTag = StageChecklistShell.Tag.Silence,
        HealthAttribute = StageChecklistShell.Attr.Vitality,
        AttackAttribute = StageChecklistShell.Attr.Power,
        CostAttribute = StageChecklistShell.Attr.Focus,
        ProbeAttribute = StageChecklistShell.Attr.Guard,
        TargetHealth = StageChecklistShell.TargetHealth,
        WindupPreSteps = StageChecklistShell.WindupPreSteps,
        WindupCost = StageChecklistShell.WindupCost,
        WindupDamage = StageChecklistShell.WindupDamage,
        ChanneledPreSteps = StageChecklistShell.ChannelPreSteps,
        FlyingDelaySteps = StageChecklistShell.LobDelaySteps,
        // 延迟那一发走 calc.by_power：读的是 commit 抄下来的出手强度 20，承伤倍率 1.0 ⇒ 正好 20。
        FlyingDamage = StageChecklistShell.Power,
        PendingFirstDelay = StageChecklistShell.PairFirstDelay,
        PendingSecondDelay = StageChecklistShell.PairSecondDelay,
        PendingFirstDamage = StageChecklistShell.PairFirstDamage,
        PendingSecondDamage = StageChecklistShell.PairSecondDamage,
        WoundDurationSteps = StageChecklistShell.WoundDurationSteps,
        WoundPeriodSteps = StageChecklistShell.WoundPeriodSteps,
        WoundDamagePerTick = StageChecklistShell.WoundDamagePerTick,
        ProbeFirstMagnitude = StageChecklistShell.ProbeFirstMagnitude,
        ProbeSecondMagnitude = StageChecklistShell.ProbeSecondMagnitude,
        ProbeClampedTo = StageChecklistShell.ProbeClampedTo,
    };

    public bool Supports(AbilityChecklistTests.Capability capability) => capability switch
    {
        // 场子有位移那一把尺（写路径只有 Inject 一条，Move 走 TryMove + 场子自己那份站位），
        // 也有 Stepper.Branch 那个口（往返与分叉两格靠它）
        AbilityChecklistTests.Capability.Move => true,
        AbilityChecklistTests.Capability.Fork => true,

        // 级 2 要吃真引擎与桥：场子起在跑着的编辑器里才谈得上，dotnet test 这一层跑不了它
        AbilityChecklistTests.Capability.LiveReplay => false,
        _ => false,
    };

    public string ReasonWithout(AbilityChecklistTests.Capability capability) => capability switch
    {
        AbilityChecklistTests.Capability.LiveReplay =>
            "级 2 要在跑着的引擎进程里按条喂同一份剧本（桥动词 replay_start / replay_run 那一轮），" +
            "离线清单这一层没有物理帧时间轴可比；第一个场子在场子里跑得起来，但要靠桥与真机，" +
            "所以这一格只能报不适用，那一格的实测在已删的验证报告",
        _ => string.Empty,
    };

    /// <summary>
    /// 16 格在第一个场子上都摆得出载体（能复用的都复用，摆不出的用探针件补），所以没有一格走「不适用」。
    /// </summary>
    public string? ReasonWithout(AbilityChecklistTests.Item item) => null;

    public AbilityChecklistTests.IChecklistHost Restart() => Fresh();

    /// <summary>推 n 步。有场子对象走场子那一个口；分支只有层那六段（站位与场子那份读数不跟过去，见 <see cref="Fork"/>）。</summary>
    public void Advance(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            if (_stage is { } stage)
            {
                stage.Advance(1);
            }
            else
            {
                _stepper.AdvanceStep();
            }
        }
    }

    /// <summary>写只走词表那一份口（「桥只转发」）：清单不许直接调层的写方法，场子也只认这一个口。</summary>
    public bool Inject(string cmd, string args) => AbilityVerbs.Dispatch(this, cmd, args);

    /// <summary>
    /// 层那半的全量文本（<c>stage.Stepper.TakeSnapshot()</c>）。D2 三问、F5 计数行、F7 两条待决读的是它；
    /// <b>读的是层那一份而不是 <see cref="SkillStage.DebugSnapshot"/></b>：分叉出来的那一支没有场子对象，
    /// 站位与观测窗口读数不跟过来（N-21 那笔账），所以清单比的那份文本在两侧都拿得出来。
    /// </summary>
    public string Snapshot => _stepper.TakeSnapshot();

    /// <summary>
    /// 逐字比的那份读数：头部以 <c>tick=N</c> 起（D4 要拿它对行为流的步号），其后是层那一份快照的逐行。
    /// </summary>
    public string Frame => $"tick={_sets.Tick} | " + string.Join(" | ",
        _stepper.TakeSnapshot().Split('\n'));

    /// <summary>一条属性的派生值 = 层那一个口（S2 出口那份，判定与显示同一份）。</summary>
    public double Value(string unit, string attribute) => _sets.Units[unit].Current[attribute];

    /// <summary>tag 计数 = 层那一个口（S2 是它唯一的写入处）。</summary>
    public int TagCount(string unit, string tag) => _sets.Units[unit].TagCounts.GetValueOrDefault(tag);

    /// <summary>这条效果几层（没有这条效果返回 -1）。</summary>
    public int Stacks(string unit, string effect)
        => _sets.Units[unit].Effects.FirstOrDefault(e => e.EffectId == effect)?.Stacks ?? -1;

    /// <summary>按形状计数：场子的行为流只有层这一本账（场子自己不发形状事件，那一格由场子自己那把尺负责）。</summary>
    public int Count<T>(Func<T, bool>? keep = null) where T : EventPayload
        => keep is null
            ? _sets.Events.Count<T>()
            : _sets.Events.Where<T>((_, payload) => keep(payload)).Count();

    public bool HasEvent<T>(Func<T, bool>? keep = null) where T : EventPayload => Count(keep) > 0;

    /// <summary>
    /// 从一份快照文本分叉出另一场：<c>stage.Stepper.Branch(snapshot)</c>，起跳 O(1)。
    /// <b>已知的账（N-21）</b>：分支出来的步进器仍绑着<b>父场子</b>，所以这一支的读数一律走分支
    /// <b>自己那份世界</b>（<see cref="_sets"/> = <c>branch.World</c>），不去问父场子「谁还活着」。
    /// 这一支没有场子对象：站位与朝向不跟过来（层里不存坐标，那不是某一格状态），
    /// 所以位移那一发在这一支<b>当场抛</b>而不是静默走个激活检查就当挪过了。
    /// </summary>
    public AbilityChecklistTests.IChecklistHost Fork(string snapshot)
    {
        var branch = _stepper.Branch(snapshot);
        return new ChecklistStageHost(null, branch.World, branch);
    }

    // === IAbilityVerbSink：这一侧的落点（每一条都只是转发到场子那一个既有口）===

    /// <summary>报错时说清是哪一个宿主（与身份同名，不掺技能语义）。</summary>
    string IAbilityVerbSink.HostLabel => "第一个场子";

    /// <summary>场子接得住的九条全在这儿（词表那份在公共层，这里只是「这一侧接哪些」）。</summary>
    private static readonly IReadOnlySet<string> AllVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        AbilityVerbs.Cast, AbilityVerbs.Cancel, AbilityVerbs.ApplyEffect, AbilityVerbs.RemoveEffect,
        AbilityVerbs.SetAttribute, AbilityVerbs.SetBase, AbilityVerbs.ClearCooldown,
        AbilityVerbs.SetStepsRemaining, AbilityVerbs.Move,
    };

    IReadOnlySet<string> IAbilityVerbSink.SupportedVerbs => AllVerbs;

    /// <summary>
    /// 这一侧<b>不接位移目的地</b>：站位是注册壳递进来的开局场地条件，不是技能的输出，
    /// 所以带第 4 段的按下由词表当场抛，不静默丢掉那一格（「桥只转发」）。
    /// </summary>
    bool IAbilityVerbSink.AcceptsCastDestination => false;

    bool IAbilityVerbSink.Cast(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move)
        => _stepper.TryActivate(unitId, abilityId, targetId, move);

    bool IAbilityVerbSink.Cancel(string unitId, string abilityId) => _stepper.InjectCancel(unitId, abilityId);

    void IAbilityVerbSink.ApplyEffect(string targetId, string effectId, long? durationTicks, string? sourceId)
        => _stepper.InjectEffect(targetId, effectId, durationTicks, sourceId);

    void IAbilityVerbSink.RemoveEffect(string targetId, string effectId, int stacks)
        => _stepper.InjectRemoveEffect(targetId, effectId, stacks);

    void IAbilityVerbSink.SetAttribute(string targetId, string attribute, double value, string mode,
        string[]? requirementTags)
        => _stepper.InjectModifier(targetId, attribute, Enum.Parse<ModifierMode>(mode, ignoreCase: true), value,
            requirementTags: requirementTags);

    void IAbilityVerbSink.SetBase(string targetId, string attribute, double value)
        => _stepper.InjectSetBase(targetId, attribute, value);

    void IAbilityVerbSink.ClearCooldown(string unitId, string abilityId)
        => _stepper.InjectClearCooldown(unitId, abilityId);

    bool IAbilityVerbSink.SetStepsRemaining(string unitId, string abilityId, long steps)
        => _stepper.InjectSetStepsRemaining(unitId, abilityId, steps);

    /// <summary>
    /// 位移：有场子时走场子那一个口（激活检查问层、坐标写场子），分支那一支没有站位可写，
    /// 所以当场抛而不是静默只走个激活检查。
    /// </summary>
    bool IAbilityVerbSink.Move(string unitId, double dx)
        => _stage is { } stage
            ? stage.Inject(AbilityVerbs.Move, $"{unitId}|{dx.ToString(CultureInfo.InvariantCulture)}")
            : throw new NotSupportedException(
                "分叉出来的那一支没有场子那份站位（坐标不存进层里的检查点），所以这一支挪不动 —— " +
                "想验位移就在没分叉的那一局上发（能力可注入那一格走的是那一条）");
}

/// <summary>
/// <b>16 条清单在第一个场子上跑的那一份 fixture</b>：断言全在基类（<see cref="AbilityChecklistTests"/>），
/// 这里只把壳递进去。<b>这 16 条在仓里只准有这一份</b>（公共层那一份本体，原型侧与场子侧各递一个注册壳）。
/// </summary>
public class AbilityChecklistOnStage : AbilityChecklistTests
{
    protected override IChecklistHost NewHost() => ChecklistStageHost.Fresh();
}
