#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Tests;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// <b>H1 假数据宿主递进清单的那一层壳</b>（P3「一份清单三处跑」的三个注册壳之一）。
///
/// 壳的分工照 <c>DuelRiftSetup</c>（H3 那份）与 <c>SkillDuelRift</c>（H2 那份）的形状：
/// <b>只回答「哪一个兵、哪一条技能、哪一个数字」，一个字都不带断言</b>。
/// 断言本体在 <see cref="AbilityChecklistTests"/>，那份文件不认得原型（钉它的是 A54），
/// 也不该出现技能名 / tag 名 / 属性名——那些全在这里（A70 钉的就是这一对分工）。
///
/// H1 的价值：清单<b>最便宜的跑法</b>——没有世界、不起引擎、几行数据模拟两个单位，
/// 公理 A2（状态即数据）的全部风险在这一层暴露。所以这里的槽位照「清单要问什么」摆，
/// 不是为了做内容：名字一律 <c>ck.</c> 前缀（checklist 槽位），不借用清单页那 16 条技能的号码。
///
/// 每一格摆成什么样（数字都取整数，红了能一口说出差在哪一步）：
/// | 清单要问的形状 | 这里的载体 | 手算 |
/// |---|---|---|
/// | 有前摇 + 有消耗 + 有冷却 | <c>ck.windup</c> | 前摇 3 步、过 commit 扣怒气 30、冷却 20 步、一刀 20 血 |
/// | 在放中授予 tag | <c>ck.channeled</c> | 前摇 2 步、授予 <c>state.channeling</c>、那一刀 15 血 |
/// | 延迟结算 + 读源属性 | <c>ck.flying</c> → <c>ck.arrow</c> | 前摇 1 步、飞 6 步、伤害 = commit 抄下的攻击 10 |
/// | 一次激活两条待决 | <c>ck.two_pending</c> | +3 步落 3 血、+7 步落 5 血，互不顶掉 |
/// | 入口被 tag 挡住 | <c>ck.silenced</c>（<c>BlockedTags = state.silence</c>） | 挂了沉默探针就放不下 |
/// | 时长 ≠ 周期 + 可叠层 | <c>ck.wound</c> | 时长 60 步、周期 5 步一跳 4 血、封顶 3 层每层 +2 |
/// | 显式打断 | <c>ck.interrupter</c>（<c>CancelMatchingTags</c>） | 命中「在放中」那条，留一行 committed=0 |
/// | 效果段授予同一个 tag | <c>ck.tag_grant</c> | 与在放中的能力段合到一张计数表 ⇒ 计数 2 |
/// </summary>
internal sealed class ChecklistH1Host : AbilityChecklistTests.IChecklistHost, IAbilityVerbSink
{
    private const string Hp = "hp";

    private const string Attack = "attack";

    private const string Rage = "rage";

    private const string DamageTaken = "damage_taken";

    private const string MoveSpeed = "move_speed";

    private const string ChanneledTag = "state.channeling";

    private const string SilenceTag = "state.silence";

    private const string RootedTag = "status.rooted";

    private const string CooldownPrefix = "cooldown.";

    private const string BlockTag = "status.block_ready";

    /// <summary>事件触发的两把订阅键（ticket 10 收进公共层配置的那两列）。</summary>
    private const string DamageTakenEvent = "damage_taken";

    private const string EffectAppliedEvent = "effect_applied";

    // === 槽位名（本体里一个都不许出现，见 A70）===

    private const string Windup = "ck.windup";

    private const string Channeled = "ck.channeled";

    private const string Flying = "ck.flying";

    private const string TwoPending = "ck.two_pending";

    private const string Silenced = "ck.silenced";

    private const string Wound = "ck.wound";

    private const string Interrupter = "ck.interrupter";

    private const string TagGrant = "ck.tag_grant";

    private const string SilenceProbe = "ck.silence_probe";

    private readonly AbilityWorld _world;

    private readonly AbilityStepper _stepper;

    private ChecklistH1Host(AbilityWorld world, AbilityStepper stepper)
    {
        _world = world;
        _stepper = stepper;
    }

    /// <summary>从零起一场（D1/D4 两跑、F1/F2/F8 的对照支、D7 的两叉都要新的一场）。</summary>
    public static ChecklistH1Host Fresh()
    {
        var (world, stepper) = NewArena();
        return new ChecklistH1Host(world, stepper);
    }

    /// <summary>H1 这一侧接层用的那份配置（NewArena 与 <see cref="Fork"/> 共用一份，别拿默认值重搭）。</summary>
    private static readonly AbilityLayerConfig ArenaConfig = new()
    {
        HealthAttribute = Hp,
        DamageTakenAttribute = DamageTaken,
        CooldownAttributePrefix = CooldownPrefix,
        RootedTag = RootedTag,
        DamageBlockTag = BlockTag,
        DamageTakenEvent = DamageTakenEvent,
        EffectAppliedEvent = EffectAppliedEvent,
        EventCapacity = 2000,
    };

    private static (AbilityWorld World, AbilityStepper Stepper) NewArena()
    {
        var world = new AbilityWorld();
        foreach (var unitId in new[] { "u1", "u2" })
        {
            world.AddUnit(unitId, new[]
            {
                (Hp, 100.0), (Attack, 10.0), (Rage, 100.0), (MoveSpeed, 4.0), (DamageTaken, 1.0),
            });
            world.SetLimits(unitId, Hp, 0, 100);
            world.SetLimits(unitId, Rage, 0, 100);
            world.SetLimits(unitId, DamageTaken, 0.2, 1.0);

            // 栏位全场每兵都装（N-18 那条口径：射程激活检查要读目标的技能栏）
            foreach (var id in new[] { Windup, Channeled, Flying, TwoPending, Silenced })
            {
                world.Units[unitId].Abilities[id] = new AbilityOnBody { Id = id };
            }
        }

        var stepper = new AbilityStepper(world, new FakeWorld(world), Calcs(), Tables(), ArenaConfig);

        // 距离那把尺：H1 是纯假数据，站位是本壳自己那份（层里不再有坐标了）。
        // 两个兵固定隔 2 米 ⇒ 每对距离恒为 2。
        stepper.DistanceOf = (_, _) => 2.0;

        return (world, stepper);
    }

    public string Name => "H1 假数据";

    public AbilityChecklistTests.Shapes Shapes { get; } = new()
    {
        Caster = "u1",
        Target = "u2",
        Windup = Windup,
        Channeled = Channeled,
        Flying = Flying,
        TwoPending = TwoPending,
        Silenced = Silenced,
        Wound = Wound,
        Interrupter = Interrupter,
        TagGrant = TagGrant,
        SilenceProbe = SilenceProbe,
        ChanneledTag = ChanneledTag,
        SilenceTag = SilenceTag,
        HealthAttribute = Hp,
        AttackAttribute = Attack,
        CostAttribute = Rage,
        ProbeAttribute = DamageTaken,
        TargetHealth = 100,
        WindupPreSteps = 3,
        WindupCost = 30,
        WindupDamage = 20,
        ChanneledPreSteps = 2,
        FlyingDelaySteps = 6,
        FlyingDamage = 10,
        PendingFirstDelay = 3,
        PendingSecondDelay = 7,
        PendingFirstDamage = 3,
        PendingSecondDamage = 5,
        WoundDurationSteps = 60,
        WoundPeriodSteps = 5,
        WoundDamagePerTick = 4,
        ProbeFirstMagnitude = 0.5,
        ProbeSecondMagnitude = 0.1,
        ProbeClampedTo = 0.2,
    };

    public bool Supports(AbilityChecklistTests.Capability capability) => capability switch
    {
        // H1 有层的 S5 那把尺（TryMove 走同一条激活检查），也有 Branch/Restore 那个口
        AbilityChecklistTests.Capability.Move => true,
        AbilityChecklistTests.Capability.Fork => true,

        // 级 2 要吃真引擎与桥：离线宿主起不来，这一格只能报不适用（D8 那条把理由念出来）
        AbilityChecklistTests.Capability.LiveReplay => false,
        _ => false,
    };

    public string ReasonWithout(AbilityChecklistTests.Capability capability) => capability switch
    {
        AbilityChecklistTests.Capability.LiveReplay =>
            "级 2 要在跑着的引擎进程里按条喂同一份剧本（桥动词 replay_start / replay_run 那一轮），" +
            "离线宿主没有物理帧时间轴可比；那一格的实测在已删的验证报告",
        _ => string.Empty,
    };

    /// <summary>H1 是纯假数据，16 条里除了级 2（要起引擎）都摆得出载体，所以逐条都给「跑」。</summary>
    public string? ReasonWithout(AbilityChecklistTests.Item item) => null;

    public AbilityChecklistTests.IChecklistHost Restart() => Fresh();

    public void Advance(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            _stepper.AdvanceStep();
        }
    }

    /// <summary>写只走词表那一份口（「桥只转发」）：清单不许直接调层的写方法。</summary>
    public bool Inject(string cmd, string args) => AbilityVerbs.Dispatch(this, cmd, args);

    public string Snapshot => _stepper.TakeSnapshot();

    /// <summary>逐字比的那份读数：头部以 <c>tick=N</c> 起（单挑场那页 5 节的段格式口径）。</summary>
    public string Frame => $"tick={_world.Tick} | " + _stepper.TakeSnapshot().Replace('\n', ' ');

    public double Value(string unit, string attribute) => _world.Units[unit].Current[attribute];

    public int TagCount(string unit, string tag) => _world.Units[unit].TagCounts.GetValueOrDefault(tag);

    public int Stacks(string unit, string effect)
        => _world.Units[unit].Effects.FirstOrDefault(e => e.EffectId == effect)?.Stacks ?? -1;

    /// <summary>按形状计数：只转发层那份 <see cref="AbilityEventLog"/> 的形状读数口（H1 只有层这一本账）。</summary>
    public int Count<T>(Func<T, bool>? keep = null) where T : EventPayload
        => keep is null
            ? _world.Events.Count<T>()
            : _world.Events.Where<T>((_, payload) => keep(payload)).Count();

    public bool HasEvent<T>(Func<T, bool>? keep = null) where T : EventPayload => Count(keep) > 0;

    /// <summary>
    /// 从一份快照文本分叉出另一场：<see cref="AbilityStepper.Branch"/>，起跳 O(1)。
    /// <b>已知的账（N-21）</b>：分支出来的步进器仍绑着父宿主，所以读数一律走分支<b>自己那份世界</b>
    /// （<see cref="Value"/> 读的是 <c>branch.World</c>），不去问「谁还活着」。
    /// </summary>
    public AbilityChecklistTests.IChecklistHost Fork(string snapshot)
    {
        var branch = _stepper.Branch(snapshot);
        return new ChecklistH1Host(branch.World, branch);
    }

    // === IAbilityVerbSink：H1 这一侧的落点（层本来就归 H1 直管）===

    string IAbilityVerbSink.HostLabel => "H1 假数据";

    IReadOnlySet<string> IAbilityVerbSink.SupportedVerbs => AllVerbs;

    private static readonly IReadOnlySet<string> AllVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        AbilityVerbs.Cast, AbilityVerbs.Cancel, AbilityVerbs.ApplyEffect, AbilityVerbs.RemoveEffect,
        AbilityVerbs.SetAttribute, AbilityVerbs.SetBase, AbilityVerbs.ClearCooldown,
        AbilityVerbs.SetStepsRemaining, AbilityVerbs.Move,
    };

    bool IAbilityVerbSink.AcceptsCastDestination => true;

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

    bool IAbilityVerbSink.Move(string unitId, double dx) => _stepper.TryMove(unitId, dx);

    // === 那张表：清单要问的每一格各摆一个载体 ===

    private static AbilityTables Tables()
    {
        var none = Array.Empty<string>();
        var noMods = Array.Empty<ModifierSpec>();
        var noTriggers = Array.Empty<(string OnEvent, string EffectId)>();
        var noArgs = new Dictionary<string, double>();
        var tables = new AbilityTables();

        // 立即结算的一刀：伤害按施法者的攻击算（commit 时抄的快照），链的起点是 0
        tables.Register(new EffectDef("ck.hit_by_attack", EffectKind.Instant,
            new[] { new ModifierSpec(Hp, ModifierMode.Add, 0, CalcFromEffectValue: true) },
            none, none, none, new[] { "calc.attack_flat" }, noArgs, 0, 0, none, noTriggers));
        // 有前摇那条的固定伤害：不走 calc，F1 只问 commit 前后的消耗与冷却
        tables.Register(EffectDef.Instant("ck.flat20", new ModifierSpec(Hp, ModifierMode.Add, -20)));
        // 蓄力那一刀：过 commit 才落，前摇 2 步（F2/F3/F5/D5 的载体）
        tables.Register(EffectDef.Instant("ck.flat15", new ModifierSpec(Hp, ModifierMode.Add, -15)));
        // 飞行物：延迟 6 步结算，读 commit 时抄下的攻击（F6 与 D2 第一问）
        tables.Register(new EffectDef("ck.arrow", EffectKind.Instant,
            new[] { new ModifierSpec(Hp, ModifierMode.Add, 0, CalcFromEffectValue: true) },
            none, none, none, new[] { "calc.attack_flat" }, noArgs, 0, 0, none, noTriggers)
        {
            DelayTicks = 6,
        });
        // 两段待决：同一次登记两条、错峰各自落地（F7）
        tables.Register(EffectDef.Delayed("ck.part1", new ModifierSpec(Hp, ModifierMode.Add, -3), 3));
        tables.Register(EffectDef.Delayed("ck.part2", new ModifierSpec(Hp, ModifierMode.Add, -5), 7));
        // 持续伤害：时长 60 步、周期 5 步一跳 4 血、可叠三层（F4 与 D2 第三问）
        tables.Register(EffectDef.OverTime(Wound, new[]
        {
            new ModifierSpec(Hp, ModifierMode.Add, 0, CalcFromEffectValue: true),
        }, durationTicks: 60, periodTicks: 5, stackKey: "wound", maxStacks: 3, magnitudePerStack: -2,
            calcNames: new[] { "calc.negate" }, calcSeed: 4));
        // 打断方：带 CancelMatchingTags（F3 / D5），自己不带 modifier，只留一条效果行
        tables.Register(EffectDef.OverTime(Interrupter, noMods, durationTicks: 10,
            cancelMatchingTags: new[] { ChanneledTag }));
        // 只授予 tag 的探针：F5 的「效果段那一层」，与在放中的能力段合到同一张计数表
        tables.Register(EffectDef.OverTime(TagGrant, noMods, durationTicks: 80, grantedTags: new[] { ChanneledTag }));
        // 沉默探针：F2 入口那一格用它摆出「被挡住」，不占槽位数字
        tables.Register(EffectDef.OverTime(SilenceProbe, noMods, durationTicks: 0, kind: EffectKind.Infinite,
            grantedTags: new[] { SilenceTag }));

        tables.Register(new AbilityDef(Windup, PreSteps: 3, CooldownTicks: 20,
            GrantedTagsWhileActive: none, RequirementTags: none, BlockedTags: none,
            EffectIds: new[] { "ck.flat20" }, CostAttribute: Rage, CostAmount: 30,
            RangeMeters: 30, MaxCharges: 0));
        tables.Register(new AbilityDef(Channeled, PreSteps: 2, CooldownTicks: 0,
            GrantedTagsWhileActive: new[] { ChanneledTag }, RequirementTags: none, BlockedTags: none,
            EffectIds: new[] { "ck.flat15" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 30, MaxCharges: 0));
        tables.Register(new AbilityDef(Flying, PreSteps: 1, CooldownTicks: 0,
            GrantedTagsWhileActive: none, RequirementTags: none, BlockedTags: none,
            EffectIds: new[] { "ck.arrow" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 30, MaxCharges: 0));
        tables.Register(new AbilityDef(TwoPending, PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: none, RequirementTags: none, BlockedTags: none,
            EffectIds: new[] { "ck.part1", "ck.part2" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 30, MaxCharges: 0));
        tables.Register(new AbilityDef(Silenced, PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: none, RequirementTags: none, BlockedTags: new[] { SilenceTag },
            EffectIds: new[] { "ck.hit_by_attack" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 30, MaxCharges: 0));

        return tables;
    }

    private static CalcRegistry Calcs() => new CalcRegistry()
        .Register("calc.attack_flat", i => -(CalcInput.Attr(i.Source, Attack) + i.Working))
        .Register("calc.negate", i => -i.Working);

    /// <summary>假世界向层供的三样：单位查询、存活判定、随机源（恒定 0.5，D1 两跑才逐字等）。</summary>
    private sealed class FakeWorld : IAbilityHost
    {
        private readonly AbilityWorld _world;

        public FakeWorld(AbilityWorld world)
        {
            _world = world;
        }

        public AbilitySet? Find(string unitId) => _world.Units.TryGetValue(unitId, out var set) ? set : null;

        public bool IsAlive(string unitId) => Find(unitId) is { } set && set.Current[Hp] > 0;

        /// <summary>这一侧的目标册只装兵：在册就是在册（死活仍归 <see cref="IsAlive"/>）。</summary>
        public bool Exists(string entityId) => Find(entityId) is not null;

        public IReadOnlyList<string> UnitIds => _world.UnitOrder;

        public double NextRandom() => 0.5;
    }
}

/// <summary>
/// <b>清单在 H1 上跑的那一份 fixture</b>：断言全在基类（<see cref="AbilityChecklistTests"/>），
/// 这里只把壳递进去——<b>这 16 条在仓里只准有这一份</b>（A70）。
/// 三个 fixture（H1 / H2 / H3）跑的是同一批方法名，所以 <c>dotnet test</c> 的输出里
/// 每一条都带着宿主名重复出现三次，跑不了的带着理由标成不适用。
/// </summary>
public class AbilityChecklistOnH1 : AbilityChecklistTests
{
    protected override IChecklistHost NewHost() => ChecklistH1Host.Fresh();
}
