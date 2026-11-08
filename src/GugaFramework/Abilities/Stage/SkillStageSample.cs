#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.GugaFramework.Abilities.Stage;

/// <summary>
/// <b>示范注册壳</b>——「原型侧只递三样」的一份可运行实例：技能表、纯函数、单位属性。
///
/// 它扮演的角色是<b>接层的那一侧</b>：换任何原型都照这份的形状写自己那一份，
/// 而 <see cref="SkillStage"/> / <see cref="SkillStageSpec"/> / <see cref="SkillStageScene"/> 一个字都不动。
///
/// 词表全部是本壳自带的，与任何原型都不同名——<b>这正是它该有的样子</b>：
/// 公共层那五列不带缺省（规则由 ticket 01 钉住），换一份词表不该让场子改一行。
/// 现在场子能跑起来，靠的是本壳递进来，不是靠场子自己揣了一份缺省。
///
/// 场子里一条真技能：<c>training.strike</c>——前摇 3 步（0.3 秒）、冷却 30 步（3 秒）、射程 3 米、
/// 伤害按施法者的 <c>training.power</c> 算，落地走 S0 扣 <c>training.vitality</c>。
/// 一条够看清「前摇落在第几步、命中落在第几步、距离够不够」这三件事，多了就不是最小了。
///
/// <b>摆位由本壳递，场子自己不挪</b>：<see cref="Spec"/> 给的是「谁站在哪、朝着哪」这份开局场地条件，
/// 场地条件一变就是换一份注册口重起一局（「表现层只读」 表现层只读、「桥只转发」 写路径只有 <c>Inject</c> 一条）。
/// 所以远近两侧、二维摆位、扇形界内外都摆得出：改本壳的坐标，不改场子。
/// </summary>
public static class SkillStageSample
{
    // === 单位与属性的名字（场子只认键，字面量全在这份壳里）===

    /// <summary>出手那一个。</summary>
    public const string CasterId = "trainee";

    /// <summary>挨打那一个（钉在原地，不还手）。</summary>
    public const string DummyId = "post";

    /// <summary>单位与属性的词表。本壳自带，不与任何原型重名。</summary>
    public static class Attr
    {
        /// <summary>血量：扣血与判死读它。</summary>
        public const string Vitality = "training.vitality";

        /// <summary>血量上限：只给表现层念血条用。</summary>
        public const string VitalityMax = "training.vitality_max";

        /// <summary>出手强度：伤害按它算。</summary>
        public const string Power = "training.power";

        /// <summary>承伤倍率：没有这条属性时按 1.0 处理。</summary>
        public const string IncomingScale = "training.incoming_scale";

        /// <summary>冷却时长的属性名前缀：层里读的是「前缀 + 技能Id」。</summary>
        public const string CooldownPrefix = "training.cooldown.";
    }

    /// <summary>tag 的词表。</summary>
    public static class Tag
    {
        /// <summary>定身：挂着它位移问一次就被挡住。</summary>
        public const string Held = "training.held";
    }

    /// <summary>事件触发的订阅键（ticket 10 收进公共层配置的那两列）。</summary>
    public static class Trigger
    {
        /// <summary>承伤那一发（含被倍率归零那一档）。示范注册壳没有事件触发型被动，这格只把配置填满。</summary>
        public const string DamageTaken = "training.damage_taken";

        /// <summary>一条效果落地。同一份理由：示范注册壳不发那一发。</summary>
        public const string EffectApplied = "training.effect_applied";
    }

    /// <summary>技能与效果的词表。</summary>
    public static class Skill
    {
        public const string Strike = "training.strike";
    }

    /// <summary>效果的词表。</summary>
    public static class Fx
    {
        public const string StrikeHit = "training.strike_hit";
    }

    /// <summary>纯函数的词表。</summary>
    public static class Fn
    {
        public const string StrikeByPower = "training.strike_by_power";
    }

    // === 这一条真技能的三个数 ===

    /// <summary>前摇 3 步（0.3 秒）。</summary>
    public const long StrikePreSteps = 3;

    /// <summary>冷却 30 步（3 秒）。</summary>
    public const long StrikeCooldownSteps = 30;

    /// <summary>射程 3 米。</summary>
    public const double StrikeRangeMeters = 3;

    /// <summary>事件流环形容量（条数）。观测窗口由这一侧定，场子不替它挑。</summary>
    public const int EventCapacity = 2000;

    /// <summary>默认摆位：出手那个在 0 米，挨打那个在 2 米（射程之内）。</summary>
    public const double DefaultGapMeters = 2;

    /// <summary>
    /// 递一份注册口给场子。<b>这就是「原型侧只递三样」的全部内容</b>：
    /// 技能表 + 纯函数 + 单位属性（词表跟着单位属性一起递——那五列本来就是属性名与 tag 名）。
    ///
    /// <paramref name="gapMeters"/> 是「挨打那个离多远」；<paramref name="lateralMeters"/> 是
    /// 沿 Z 那一根挪多少（默认 0 = 摆成一根直线那一档，给非 0 就是二维摆位那一档），
    /// <paramref name="facingDegrees"/> 是出手那一个的朝向（0 = 朝 +X）。
    /// <b>场子只读这份摆位</b>：远近两侧、二维摆位、扇形界内外都从这里出，场子一个字不改。
    /// </summary>
    public static SkillStageSpec Spec(
        double gapMeters = DefaultGapMeters,
        double lateralMeters = 0,
        double facingDegrees = 0)
    {
        var caster = new StageUnit
        {
            UnitId = CasterId,
            X = 0,
            Z = 0,
            FacingDegrees = facingDegrees,
            Attributes = new[]
            {
                (Attr.Vitality, 100.0),
                (Attr.VitalityMax, 100.0),
                (Attr.Power, 25.0),
                (Attr.IncomingScale, 1.0),
                (Attr.CooldownPrefix + Skill.Strike, 1.0),
            },
            AbilityIds = new[] { Skill.Strike },
        };

        var dummy = new StageUnit
        {
            UnitId = DummyId,
            X = gapMeters,
            Z = lateralMeters,
            Attributes = new[]
            {
                (Attr.Vitality, 100.0),
                (Attr.VitalityMax, 100.0),
                (Attr.Power, 0.0),
                (Attr.IncomingScale, 1.0),
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
                    DamageTakenAttribute = Attr.IncomingScale,
                    CooldownAttributePrefix = Attr.CooldownPrefix,
                    RootedTag = Tag.Held,
                    DamageBlockTag = "training.warded",
                    DamageTakenEvent = Trigger.DamageTaken,
                    EffectAppliedEvent = Trigger.EffectApplied,
                    EventCapacity = EventCapacity,
                },
                Units = new[] { caster, dummy },
            },
        };
    }

    /// <summary>
    /// <b>多单位同框那一份</b>：六个单位散在场地里（出手那个与挨打那个仍贴 3 米，其余四个离得远）。
    ///
    /// 为什么单独一份：场子自带的默认样子是 <see cref="Spec"/> 那 1 打 1，那是验技能时间线的最小形状；
    /// 而机位那几格要验的是「<b>五六个单位同框时能不能切跟谁</b>」「全景能不能看清群体」——
    /// 两个人同框的场子答不了这两问。摆位是注册壳的场地条件（「表现层只读」 表现层只读），
    /// 所以加人要加在<b>壳</b>这一侧，场子与技能层一个字都不用改。
    ///
    /// 挨打的那四个全是挨打的桩：没有技能栏、也不会自己走位（钉住那一档由台账 L3 判）。
    /// </summary>
    public static SkillStageSpec SpecMultiUnit()
    {
        var spec = Spec();
        var caster = spec.Roster.Units[0] with { X = -12, Z = -12, FacingDegrees = 0 };
        var dummy = spec.Roster.Units[1] with { X = -9, Z = -12 };
        StageUnit Idle(string unitId, double x, double z)
            => dummy with { UnitId = unitId, X = x, Z = z };

        return spec with
        {
            Roster = spec.Roster with
            {
                Units = new[]
                {
                    caster,
                    dummy,
                    Idle("post.a", -12, 0),
                    Idle("post.b", 0, -12),
                    Idle("post.c", 12, -12),
                    Idle("post.d", 0, 0),
                },
            },
        };
    }

    /// <summary>
    /// <b>开阔多点那一份</b>（ticket 07）：<b>一个出手、六个目标</b>，散在 6~12 米的开阔地上，
    /// 专门用来把「范围」与「扇形」这两问分开判。
    ///
    /// 为什么单独一份：<see cref="Spec"/> 是 1 打 1（验技能时间线的最小形状），
    /// <see cref="SpecMultiUnit"/> 是「五六个单位同框」（验机位那几格，挨打的桩隔得远、看不清角度）；
    /// 量扇形与范围要的是<b>一群目标的角度分布</b>——同样 6 米，一个在正前 0°、一个在 80°、一个在 180°，
    /// 分开它们的只有角度。这一份就是按这个摆的，桩隔得开、角度各不相同。
    ///
    /// 摆位是极坐标（米 + 度）换算过来的，换算在 <see cref="Polar"/> 一处（度 → 二维点，「double 与不变文化」）。
    /// 出手那一个朝 +X；场地半边长给 <see cref="OpenField.GridHalfExtentMeters"/> 米——
    /// 开阔那一档的「开阔」在这一格里，而它只改画面、不改逻辑（单位坐标的单位是米）。
    /// </summary>
    public static SkillStageSpec SpecOpenField()
    {
        var spec = Spec();
        var caster = spec.Roster.Units[0] with { X = 0, Z = 0, FacingDegrees = 0 };
        var dummy = spec.Roster.Units[1];
        StageUnit Target((string unitId, double meters, double degrees) at) => dummy with
        {
            UnitId = at.unitId,
            X = Polar(at.meters, at.degrees).X,
            Z = Polar(at.meters, at.degrees).Z,
        };

        return spec with
        {
            Roster = spec.Roster with
            {
                GridHalfExtentMeters = OpenField.GridHalfExtentMeters,
                Units = new[] { caster }.Concat(OpenFieldTargets.Select(Target)).ToArray(),
            },
        };
    }

    /// <summary>极坐标（米 + 角度）→ 二维点（度，0 = 朝 +X）。只在这一处换算。</summary>
    private static (double X, double Z) Polar(double meters, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return (meters * Math.Cos(radians), meters * Math.Sin(radians));
    }

    /// <summary>
    /// <b>开阔多点那一份的词表与布局</b>：六个目标各是谁、各摆在哪（米 + 度，度 0 = 朝 +X），
    /// 外加两问各用的半径与全角。<b>布局的数全在这儿</b>，场子那几份一份都不许有（钉它的是 S8）。
    /// </summary>
    public static class OpenField
    {
        /// <summary>8 米 / 正前 0°：半径内 + 扇内。</summary>
        public const string Front = "post.front";

        /// <summary>6 米 / 正前 40°：半径内 + 扇内。</summary>
        public const string Mid = "post.mid";

        /// <summary>6 米 / 正前 55°：距 60° 那条界还差 5 度，稳稳在扇内（界上那档由 L2 判）。</summary>
        public const string NearEdge = "post.near_edge";

        /// <summary>6 米 / 正前 80°：半径内、扇外——与 <see cref="Mid"/> 分开的<b>只有角度</b>。</summary>
        public const string Wide = "post.wide";

        /// <summary>6 米 / 正前 180°：半径内、扇外（在背后）。</summary>
        public const string Back = "post.back";

        /// <summary>12 米 / 正前 0°：半径外——与 <see cref="Front"/> 分开的<b>只有距离</b>。</summary>
        public const string Far = "post.far";

        /// <summary>范围那一问用的半径（米）。</summary>
        public const double RadiusMeters = 8;

        /// <summary>扇形那一问用的全角（度）：正前 ±60。</summary>
        public const double ConeDegrees = 120;

        /// <summary>场地半边长（米）：60 米见方，开阔那一档。</summary>
        public const double GridHalfExtentMeters = 30;
    }

    /// <summary>
    /// 六个目标各摆在哪。<b>次序就是名单的次序</b>（场子按注册口那份摆位顺序遍历，
    /// 遍历次序是确定性的一部分），所以它是 List 不是 Dictionary。
    /// </summary>
    public static readonly IReadOnlyList<(string UnitId, double Meters, double Degrees)> OpenFieldTargets = new[]
    {
        (OpenField.Front, 8.0, 0.0),
        (OpenField.Mid, 6.0, 40.0),
        (OpenField.NearEdge, 6.0, 55.0),
        (OpenField.Wide, 6.0, 80.0),
        (OpenField.Back, 6.0, 180.0),
        (OpenField.Far, 12.0, 0.0),
    };

    /// <summary>技能表：一条技能一张效果。</summary>
    public static AbilityTables Tables()
    {
        var tables = new AbilityTables();

        // 伤害走效果表里的「扣血那条 modifier + Calc 链算数值」那一条路，
        // 所以承伤倍率与判死问得到同一份真值（与层里既有的形状同形，不是场子另写一句扣血）。
        tables.Register(new EffectDef(Fx.StrikeHit, EffectKind.Instant,
            new[] { new ModifierSpec(Attr.Vitality, ModifierMode.Add, 0, CalcFromEffectValue: true) },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.StrikeByPower }, new Dictionary<string, double>(), 0, 0,
            Array.Empty<string>(), Array.Empty<(string, string)>()));

        tables.Register(new AbilityDef(Skill.Strike, StrikePreSteps, StrikeCooldownSteps,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fx.StrikeHit }, string.Empty, 0, RangeMeters: StrikeRangeMeters));

        return tables;
    }

    /// <summary>纯函数：伤害取施法者的出手强度并翻成负值（加到血量上就是扣血）。</summary>
    public static CalcRegistry Calcs() => new CalcRegistry()
        .Register(Fn.StrikeByPower, i => -(CalcInput.Attr(i.Source, Attr.Power) + i.Working));
}
