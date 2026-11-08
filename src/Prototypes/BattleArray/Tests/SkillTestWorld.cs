#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;

namespace BattleArray.Tests;

/// <summary>
/// 单挑场（H2）—— 一个会放技能的单位打一个木桩的空场地，纯 C#、零引擎。
/// 设计出处：<c>docs/research/技能系统/技能单挑场-通用对战场景设计.md</c> 2 / 5 / 7 节
/// —— 那一份稿已并入 <c>docs/research/技能系统/调研结论摘要.md</c>（原文在 git 历史）；
/// 下文几处「本页 N 节」指的就是那一份原稿。
///
/// 通用性在这里怎么兑现（G2 这条的机器口径）：<b>本文件里搜不到任何技能名、tag 字面量、属性名</b>——
/// 名字、数值、光环规则、平A 那条效果全由注册壳（<c>SkillDuelRift.cs</c>）递进来。
/// 加一条技能不改这个文件，换一档可调参数也不改。
///
/// 三条边界，写明白不藏：
/// 1. <b>几何只有一根直线</b>（X 轴）。站位与配比是排兵布阵自己的课题（本页 2 节），
///    站位落在场子自己那份（<see cref="XOf"/>）、距离经 <c>DistanceOf</c> 递进层里，所以射程这类判定够用；
///    扇形与角度那把尺（<c>InsideSector</c>）在这里是退化的（夹角只有 0° 与 180°），它这一格要在纯函数那一层验。
/// 2. <b>平A 也走技能层</b>：木桩还手不是自己写一句扣血，而是 <c>ApplyEffect(受害者, 平A效果, 来源=木桩)</c>——
///    于是「抵挡挡不挡得住平A」「承伤倍率乘不乘得上平A」这类问题只有一份真值可问（公理 A1）。
/// 3. <b>两个会话动词不在写路径词表里</b>：<c>ResetWorld</c> 与 <c>Mark</c> 改的是「场子本身」，
///    不是「在场子里做事」——与本页 5 节那句「可调参数是开局参数、不进 inject 词表」同一条理由。
///    它们住在录制那半边（<see cref="SkillTestWorld.Reset"/> 与 <see cref="Recorder"/>）。
/// </summary>
public sealed class SkillTestWorld : IAbilityHost, IAbilityVerbSink
{
    /// <summary>敌方档位：不还手 / 只平A / 会自动放技能。</summary>
    public enum EnemyStance
    {
        Static,
        BasicAttack,
        AutoCast,
    }

    /// <summary>开局参数（五个可调的那五个；单位数钉死 1v1，不设档位）。是 record，所以能 <c>with</c> 改一档。</summary>
    public sealed record Tuning
    {
        public EnemyStance Enemy { get; init; } = EnemyStance.Static;
        public bool EnemyUnkillable { get; init; }
        public bool OursAutoCast { get; init; }
        public double StartDistanceMeters { get; init; } = 2;
        public bool RandomOn { get; init; }

        /// <summary>木桩那一下平A的间隔（步）。只有 <see cref="EnemyStance.BasicAttack"/> 用得到。</summary>
        public long BasicAttackIntervalTicks { get; init; } = 30;
    }

    /// <summary>
    /// 一条光环规则（N-13 的形状）：<b>判定在层里算（S5 的数据算），跨边界由这里发既有的
    /// ApplyEffect / RemoveEffect</b>——公共层不加「距离条件」那一列，S2 全程不问活世界。
    /// </summary>
    public sealed record AuraRule(string OwnerId, string EffectId, double RadiusMeters);

    /// <summary>
    /// 事件流环形容量（条数）：单挑场给层的观测窗口由场子自己定——
    /// <see cref="AbilityLayerConfig.EventCapacity"/> 在公共层没有默认那一格，0 会当场抛。
    /// </summary>
    public const int StageEventCapacity = 2000;

    /// <summary>
    /// 注册壳递进来的一整份场子形状。名字与数值都在这儿，所以场子本身谁都不认识。
    /// </summary>
    public sealed class Spec
    {
        public required string HeroId { get; init; }
        public required string DummyId { get; init; }
        public required AbilityTables Tables { get; init; }
        public required CalcRegistry Calcs { get; init; }

        /// <summary>
        /// 接层用的那份配置。<b>必填</b>：公共层那五列词表一个缺省都没有，场子自己也不给缺省
        /// ——场子要是自带一份默认词表，「换原型不改场子」就又是假的（词表归注册壳，见 <c>SkillDuelRift</c>）。
        /// </summary>
        public required AbilityLayerConfig Config { get; init; }
        public IReadOnlyList<(string Name, double Value)> HeroAttributes { get; init; } =
            Array.Empty<(string, double)>();
        public IReadOnlyList<(string Name, double Value)> DummyAttributes { get; init; } =
            Array.Empty<(string, double)>();

        /// <summary>每个技能栏开局就装好的那几个栏位（全场每兵都有一个空技能栏，口径同 N-18）。</summary>
        public IReadOnlyList<string> HeroAbilityIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> DummyAbilityIds { get; init; } = Array.Empty<string>();

        /// <summary>我方「会自己放一条」那一档放的是哪条技能；<see cref="Tuning.OursAutoCast"/> 为假时不问。</summary>
        public string? AutoCastAbilityId { get; init; }

        /// <summary>木桩平A 那一下走效果表里注册好的哪条（伤害按来源属性算，见抬头边界 2）。</summary>
        public string? BasicAttackEffectId { get; init; }

        /// <summary>敌方「会自动放技能」那一档放的是哪条技能。</summary>
        public string? EnemyAutoCastAbilityId { get; init; }

        public IReadOnlyList<AuraRule> Auras { get; init; } = Array.Empty<AuraRule>();

        /// <summary>属性名：场子只认键，不认字面量。</summary>
        public required string HealthAttribute { get; init; }
        public required string MaxHealthAttribute { get; init; }
        public required string AttackAttribute { get; init; }
    }

    private readonly Spec _spec;
    private readonly Tuning _tuning;
    private readonly AbilityWorld _sets = new();
    private readonly AbilityStepper _stepper;
    private readonly Random _rng;
    private readonly List<string> _order = [];

    /// <summary>
    /// 站位（米）：<b>场子自己那份</b>，不进技能层——单位身上那根坐标已从公共层拿掉。
    /// 层要距离时问 <see cref="AbilityStepper.DistanceOf"/>，那一条现算这一格的差。
    /// </summary>
    private readonly Dictionary<string, double> _xByUnit = new(StringComparer.Ordinal);

    /// <summary>每条光环规则此刻在谁身上生效着——只存这个，跨边界才比得出来（不存引用，A1）。</summary>
    private readonly Dictionary<int, HashSet<string>> _auraOn = [];

    private long _lastBasicAttackTick;

    public SkillTestWorld(Spec spec, Tuning tuning, int seed)
    {
        _spec = spec;
        _tuning = tuning;
        _rng = new Random(seed);
        Seed = seed;

        var hero = _sets.AddUnit(spec.HeroId, spec.HeroAttributes);
        var dummy = _sets.AddUnit(spec.DummyId, spec.DummyAttributes);
        _xByUnit[spec.HeroId] = 0;
        _xByUnit[spec.DummyId] = tuning.StartDistanceMeters;
        _order.AddRange(new[] { spec.HeroId, spec.DummyId });
        PrimeAbilities(hero, spec.HeroAbilityIds);
        PrimeAbilities(dummy, spec.DummyAbilityIds);

        _stepper = new AbilityStepper(_sets, this, spec.Calcs, spec.Tables, spec.Config);
        _stepper.DistanceOf = (fromId, toId) => Math.Abs(XOf(fromId) - XOf(toId));
        for (var i = 0; i < spec.Auras.Count; i++)
        {
            _auraOn[i] = [];
        }

        StartSnapshot = ReplaySnapshot();
        AutoCast = tuning.OursAutoCast || tuning.Enemy == EnemyStance.AutoCast;
    }

    public int Seed { get; }

    /// <summary>某个单位此刻的站位（场子自己那份；层里读不到它，要距离经 <c>DistanceOf</c> 问出来）。</summary>
    public double XOf(string unitId) => _xByUnit.GetValueOrDefault(unitId);

    public long Tick => _sets.Tick;

    public AbilityWorld Sets => _sets;

    public AbilityStepper Stepper => _stepper;

    /// <summary>接层用的那份配置（Branch/Restore 重建步进器时要递同一份——事件流容量那一格也在里面）。</summary>
    public AbilityLayerConfig Config => _spec.Config;

    public Tuning CurrentTuning => _tuning;

    /// <summary>这一局的开局文本快照（D6 往返与 <see cref="Reset"/> 的落点）。</summary>
    public string StartSnapshot { get; private set; } = string.Empty;

    /// <summary>两个技能栏里先把栏位摆好（PerActor 一份，冷却/相位都从 Ready 起）。</summary>
    private static void PrimeAbilities(AbilitySet set, IReadOnlyList<string> abilityIds)
    {
        foreach (var id in abilityIds)
        {
            set.Abilities[id] = new AbilityOnBody { Id = id };
        }
    }

    // === 时间可控：一步一息 ===

    /// <summary>
    /// 推进一步：层跑完 S0~S5，场子再补两件只有宿主会做的事——
    /// 光环按边界重应用（N-13）、木桩按间隔平A、打不死那档把血回满。顺序固定，重放才复现得出。
    /// </summary>
    public void AdvanceStep()
    {
        _stepper.AdvanceStep();
        ApplyAuras();
        RunBasicAttack();
        ReviveUnkillableDummy();
    }

    /// <summary>一步推 <paramref name="steps"/> 发。</summary>
    public void Advance(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            AdvanceStep();
        }
    }

    /// <summary>
    /// 光环重应用：把「谁在半径内」交给层去算（S5 那段数据算，输入只有坐标与半径标量），
    /// 边界内外各一笔——进界发 <c>ApplyEffect</c>、出界发 <c>RemoveEffect</c>，都走写路径那同一条口。
    /// </summary>
    private void ApplyAuras()
    {
        for (var i = 0; i < _spec.Auras.Count; i++)
        {
            var rule = _spec.Auras[i];
            var inside = _stepper.UnitsInRange(rule.OwnerId, rule.RadiusMeters)
                .ToHashSet(StringComparer.Ordinal);
            var on = _auraOn[i];
            foreach (var target in inside.Where(t => !on.Contains(t)).OrderBy(t => t, StringComparer.Ordinal))
            {
                on.Add(target);
                ApplyEffect(target, rule.EffectId, null, rule.OwnerId);
                EmitStage(target, new AuraEffectApplied(rule.EffectId, rule.OwnerId, rule.RadiusMeters));
            }

            foreach (var target in on.Where(t => !inside.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList())
            {
                on.Remove(target);
                _stepper.InjectRemoveEffect(target, rule.EffectId);
                EmitStage(target, new AuraEffectRemoved(rule.EffectId, rule.OwnerId, rule.RadiusMeters));
            }
        }
    }

    /// <summary>木桩还手那一发：到点就打，走的是与技能同一条 S0（所以抵挡与承伤倍率都问得到）。</summary>
    private void RunBasicAttack()
    {
        if (_tuning.Enemy != EnemyStance.BasicAttack || _spec.BasicAttackEffectId is null)
        {
            return;
        }

        if (_sets.Tick - _lastBasicAttackTick < _tuning.BasicAttackIntervalTicks)
        {
            return;
        }

        if (!IsAlive(_spec.DummyId))
        {
            return;
        }

        _lastBasicAttackTick = _sets.Tick;
        ApplyEffect(_spec.HeroId, _spec.BasicAttackEffectId, null, _spec.DummyId);
    }

    /// <summary>
    /// 「打不死」那一档（本页 2 节）：承伤后回到上限。它是一列显式数据——累计承伤读
    /// 层自己那本账 <c>damage_taken_total</c>，不是靠一个 if 藏在结算里。
    /// </summary>
    private void ReviveUnkillableDummy()
    {
        if (!_tuning.EnemyUnkillable)
        {
            return;
        }

        var dummy = _sets.Units[_spec.DummyId];
        var max = dummy.Base.TryGetValue(_spec.MaxHealthAttribute, out var m) ? m : 0;
        if (dummy.Base[_spec.HealthAttribute] < max)
        {
            _stepper.InjectSetBase(_spec.DummyId, _spec.HealthAttribute, max);
        }
    }

    // === 行为可注入：剧本词表（本页 5 节那八条，Move 是现场扩的那一发）===

    /// <summary>
    /// 写路径的唯一分发口——桥、CLI、录制与测试喂的都是这一发（AGENTS 「桥只转发」）。
    /// <b>动词名、参数次序、缺省值都不在这个文件里</b>：那一份表住在
    /// <see cref="AbilityVerbs"/>（P3「一份清单三处跑」收的第一块，钉它的是 A68），
    /// 这里只是 H2 这一侧的适配器。未知命令当场抛；表里查无这条技能/效果返回 <c>false</c>
    /// （false 本身进账，那就是 D3 要比的东西）。
    /// </summary>
    public bool Inject(string cmd, string args) => AbilityVerbs.Dispatch(this, cmd, args);

    // === IAbilityVerbSink：H2 这一侧接得住哪几条、从哪个口子打进去 ===

    /// <summary>报错时说清是 H2 这一侧（同一条命令在 H3 那侧的下落不一样）。</summary>
    string IAbilityVerbSink.HostLabel => "H2 单挑场";

    /// <summary>单挑场九条全接（八条原有的 + 现场扩的 <see cref="AbilityVerbs.Move"/>）。</summary>
    private static readonly IReadOnlySet<string> SupportedVerbNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            AbilityVerbs.Cast, AbilityVerbs.Cancel, AbilityVerbs.ApplyEffect, AbilityVerbs.RemoveEffect,
            AbilityVerbs.SetAttribute, AbilityVerbs.SetBase, AbilityVerbs.ClearCooldown,
            AbilityVerbs.SetStepsRemaining, AbilityVerbs.Move,
        };

    IReadOnlySet<string> IAbilityVerbSink.SupportedVerbs => SupportedVerbNames;

    /// <summary>
    /// <b>H2 不接位移目的地</b>（口径 3）：单挑场里位置是输入的常量、不是技能的输出——那页 3 节与
    /// 12 节 待确认项 5 确认过，那格冲锋的台账按用户确认的口径落 <b>H3</b>（要动 <c>Pending.Move</c> 的那一格）。
    /// 所以带第 4 段的 <c>CastAbility</c> 打到这一侧是<b>当场抛</b>，不是静默把那一格丢掉：
    /// 「注入的路径」与「玩家的路径」不许在这格长成两回事（A69 腿 ②）。
    /// </summary>
    bool IAbilityVerbSink.AcceptsCastDestination => false;

    bool IAbilityVerbSink.Cast(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move)
        => _stepper.TryActivate(unitId, abilityId, targetId, move);

    bool IAbilityVerbSink.Cancel(string unitId, string abilityId)
        => _stepper.InjectCancel(unitId, abilityId);

    void IAbilityVerbSink.ApplyEffect(string targetId, string effectId, long? durationTicks, string? sourceId)
        => ApplyEffect(targetId, effectId, durationTicks, sourceId);

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

    /// <summary>激活检查问层、坐标写场子（层里那根坐标已从公共层拿掉）。</summary>
    bool IAbilityVerbSink.Move(string unitId, double dx)
    {
        if (!_stepper.TryMove(unitId, dx))
        {
            return false;
        }

        _xByUnit[unitId] = XOf(unitId) + dx;
        return true;
    }

    /// <summary>施加效果走层里那一条（带来源 = 平A 与光环都要有名字的「谁打的」）。</summary>
    private void ApplyEffect(string targetId, string effectId, long? durationTicks, string? sourceId)
        => _stepper.InjectEffect(targetId, effectId, durationTicks, sourceId);

    /// <summary>我方自动放一条那一档：会自己放 = 每步 S4 问一发，与手动注入同一条 TryActivate。</summary>
    public bool AutoCast
    {
        get => _autoCast;
        set
        {
            _autoCast = value;
            _stepper.IntentPolicy = value ? AskIntent : null;
        }
    }

    private bool _autoCast;

    private (string? AbilityId, string? TargetId) AskIntent(string unitId)
    {
        var (hero, dummy) = (_spec.HeroId, _spec.DummyId);
        if (_tuning.OursAutoCast && unitId == hero && _spec.AutoCastAbilityId is { } ours)
        {
            return (ours, dummy);
        }

        if (_tuning.Enemy == EnemyStance.AutoCast && unitId == dummy && _spec.EnemyAutoCastAbilityId is { } theirs)
        {
            return (theirs, hero);
        }

        return (null, null);
    }

    // === 状态可查：读数 ===

    /// <summary>
    /// 重放段格式那份快照：头部以 <c>tick=N</c> 起、段间 <c>" | "</c>、每段段首两 token 是实体身份
    /// （本页 5 节：单挑场的快照必须天生满足这个格式，否则 L2 语义档用不上）。
    /// 头部把五个可调参数原样念出来——不然「这一格是哪一档」要回头翻代码。
    /// </summary>
    public string ReplaySnapshot()
    {
        var segments = new List<string>
        {
            $"tick={_sets.Tick} seconds={AbilityNumbers.Format(_sets.Tick / 10.0)} stage=1v1 " +
            $"enemy={_tuning.Enemy} enemy_unkillable={(_tuning.EnemyUnkillable ? 1 : 0)} " +
            $"ours_auto={(_tuning.OursAutoCast ? 1 : 0)} distance={AbilityNumbers.Format(Distance())} " +
            $"random={OnOff(_tuning.RandomOn)}",
        };

        foreach (var unitId in _order)
        {
            var set = _sets.Units[unitId];
            segments.Add($"u {unitId} hp={AbilityNumbers.Format(set.Base.GetValueOrDefault(_spec.HealthAttribute))}/" +
                         $"{AbilityNumbers.Format(set.Base.GetValueOrDefault(_spec.MaxHealthAttribute))} " +
                         $"atk={AbilityNumbers.Format(set.Current.GetValueOrDefault(_spec.AttackAttribute))} " +
                         $"x={AbilityNumbers.Format(XOf(unitId))} " +
                         $"took={AbilityNumbers.Format(set.Counters.GetValueOrDefault("damage_taken_total"))} " +
                         $"blocked={AbilityNumbers.Format(set.Counters.GetValueOrDefault("damage_blocked"))}");
        }

        foreach (var row in _stepper.TakeSnapshot().Split('\n'))
        {
            if (row.Length == 0) continue;
            segments.Add(LayerRowAsSegment(row));
        }

        return string.Join(" | ", segments);
    }

    /// <summary>
    /// 层的一行（<c>类型;字段;字段…</c>）并进重放段格式：<b>整行的分号都换成空格</b>，不是只换第一个。
    /// 只换第一个会把后半截留在同一个 token 里，L2 语义档就把整行当成「段首身份」来比——
    /// 于是「给层加一列」这种良性改动在 L2 也照样六格全红（账本 N-19，2026-09-27 实测；A53）。
    /// 换成分号的前提是层文本里没有带空格的值：字段全是名字、枚举、数字与逗号表，取证见 N-19。
    /// </summary>
    internal static string LayerRowAsSegment(string row) => row.Replace(';', ' ');

    /// <summary>
    /// 层那本账与场子自己那本账按步号归并（稳定排序：同一步里层先发的排在场子补记的前——
    /// 与真实发出次序一致，「第几步发生了什么」只有一个顺序）。
    /// </summary>
    private List<AbilityEvent> MergedEvents()
        => _sets.Events.All.Concat(_stageEvents).OrderBy(e => e.Tick).ToList();

    /// <summary>行为流尾窗的行文本（形状渲染出的 Line，两本账归并后取最近 n 条）。</summary>
    public IReadOnlyList<string> EventTailLines(int count)
        => MergedEvents().TakeLast(Math.Max(1, count)).Select(e => e.Line).ToList();

    /// <summary>尾窗拼成一段字（失败消息里念它；「尾窗里没有」先看层那本账的 dropped）。</summary>
    public string EventTailText(int count) => string.Join("\n", EventTailLines(count));

    /// <summary>某一类形状留了几条（两本账一起数：层那本 + 场子自己那本；比的是数字，不读字符串）。</summary>
    public int Count<T>(Func<T, bool>? keep = null) where T : EventPayload
    {
        var n = 0;
        foreach (var e in MergedEvents())
        {
            if (e.Payload is T typed && (keep is null || keep(typed)))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>某一类形状出现过没有。</summary>
    public bool HasEvent<T>(Func<T, bool>? keep = null) where T : EventPayload => Count(keep) > 0;

    /// <summary>某主体身上某一类形状的最后一条载荷（「为什么没走」「为什么作废」读字段，不读文本）；没有返回 null。</summary>
    public T? LastEvent<T>(string subject) where T : EventPayload
    {
        T? last = default;
        foreach (var e in MergedEvents())
        {
            if (e.Subject == subject && e.Payload is T typed)
            {
                last = typed;
            }
        }

        return last;
    }

    /// <summary>层的原始快照（D2 那三问读的就是它）。</summary>
    public string Snapshot() => _stepper.TakeSnapshot();

    /// <summary>
    /// 场子自己那本账的两类形状：一条光环规则越界应用 / 撤下。
    /// 名字的口径（Wire）沿用旧事件行，供读数比对；但明细由字段渲染，不再拼字符串记账。
    /// 这两类住在原型侧（场子的事），不进公共层那份名单。
    /// </summary>
    public sealed record AuraEffectApplied(string EffectId, string OwnerId, double RadiusMeters) : EventPayload
    {
        public override string Wire => "aura_applied";
        public override string Detail => $"{EffectId} by={OwnerId} r={AbilityNumbers.Format(RadiusMeters)}";
    }

    /// <summary>见 <see cref="AuraEffectApplied"/>：出界那一发。</summary>
    public sealed record AuraEffectRemoved(string EffectId, string OwnerId, double RadiusMeters) : EventPayload
    {
        public override string Wire => "aura_removed";
        public override string Detail => $"{EffectId} by={OwnerId} r={AbilityNumbers.Format(RadiusMeters)}";
    }

    private readonly List<AbilityEvent> _stageEvents = [];

    private void EmitStage(string subject, EventPayload payload)
        => _stageEvents.Add(new AbilityEvent(_sets.Tick, subject, payload));

    /// <summary>两个兵此刻隔多远（米，一根直线）——读场子自己那份站位，与层里那把尺同一份实现。</summary>
    public double Distance()
        => Math.Round(_stepper.Distance(_spec.HeroId, _spec.DummyId), 2, MidpointRounding.AwayFromZero);

    /// <summary>读一条属性的派生值（S2 出口那份，判定与显示同一份）。</summary>
    public double Current(string unitId, string attribute) => _sets.Units[unitId].Current[attribute];

    /// <summary>读一层历史计数（打不死那档的累计承伤、抵挡吃掉几次，都在这种行里）。</summary>
    public double Counter(string unitId, string name) => _sets.Units[unitId].Counters.GetValueOrDefault(name);

    /// <summary>读某个单位某个技能的运行时状态（相位 / 排队旗标 / 拒绝原因）。</summary>
    public AbilityOnBody Ability(string unitId, string abilityId) => _sets.Units[unitId].Abilities[abilityId];

    /// <summary>
    /// 给一条属性设上下界（界归属性、不归求值器，已删的设计档）。注册壳在装配那一步调它，
    /// 场子自己不知道任何一条属性的名字。
    /// </summary>
    public void SetLimits(string unitId, string attribute, double min, double max) => _sets.SetLimits(unitId, attribute, min, max);

    /// <summary>某单位身上这条效果此刻几层（没有这条效果返回 -1）。</summary>
    public int Stacks(string unitId, string effectId)
        => _sets.Units[unitId].Effects.FirstOrDefault(e => e.EffectId == effectId)?.Stacks ?? -1;

    // === 会话动词（不在写路径词表里，见抬头边界 3）===

    /// <summary>
    /// 同种子同参数重开一场（检查点重建与对照矩阵都要这一发）。参数串走 <see cref="Tuning"/>，
    /// 不走 <see cref="Inject"/>——开局参数与「在场子里做事」不许混在同一条时间轴上。
    /// </summary>
    public static SkillTestWorld Reset(Spec spec, Tuning tuning, int seed) => new(spec, tuning, seed);

    /// <summary>打一个检查点（录制那半边用；层的 <c>Mark</c> 动词在剧本里就是抓这一发）。</summary>
    public Recorder Mark(string label) => new Recorder(this, label, _sets.Tick);

    public sealed class Recorder
    {
        private readonly SkillTestWorld _world;
        private readonly string _label;

        internal Recorder(SkillTestWorld world, string label, long tick)
        {
            _world = world;
            _label = label;
            Tick = tick;
            Snapshot = world.ReplaySnapshot();
        }

        public long Tick { get; }

        public string Snapshot { get; }

        public string Label => _label;
    }

    // === IAbilityHost：世界向技能层供的那几样 ===

    public AbilitySet? Find(string unitId) => _sets.Units.TryGetValue(unitId, out var set) ? set : null;

    public bool IsAlive(string unitId)
        => _sets.Units.TryGetValue(unitId, out var set)
            && set.Current.GetValueOrDefault(_spec.HealthAttribute) > 0;

    /// <summary>这一侧的目标册只装兵：在册就是在册（死活仍归 <see cref="IsAlive"/>）。</summary>
    public bool Exists(string entityId) => _sets.Units.ContainsKey(entityId);

    public IReadOnlyList<string> UnitIds => _order;

    /// <summary>随机开关：关着算手算台账，开着测确定性（同种子两跑逐字等）。种子在场子手里，层不自带 Random。</summary>
    public double NextRandom() => _tuning.RandomOn ? _rng.NextDouble() : 0.5;

    private static string OnOff(bool value) => value ? "on" : "off";
}
