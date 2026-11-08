#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Tests;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// <b>H2 单挑场宿主递进清单的那一层壳</b>（P3「一份清单三处跑」的三个注册壳之二）。
///
/// 分工照 <see cref="ChecklistH1Host"/>：壳只回答「哪一个兵、哪一条技能、哪一个数字」，
/// <b>一个字都不带断言</b>（断言只有一份，后续扫这一格的是 A70）。断言本体在
/// <c>AbilityChecklistTests</c>，那份文件不认得原型（钉它的是 A54），也不该出现技能名 /
/// tag 名 / 属性名——那些全在注册壳 <see cref="SkillDuelRift"/> 里（A50 是同一对分工）。
/// 写路径只有 <see cref="AbilityVerbs"/> 那一份词表（A68），所以这一侧也搜不到动词名字面量。
///
/// 世界是 <see cref="SkillTestWorld"/>（单挑场本体，纯 C#、零引擎），槽位从注册壳那 16 条技能里挑，
/// 能复用的都复用：<c>warrior.bleed</c> 当 F4 的载体、<c>rider.joust</c> 当 F7、<c>tank.guard</c>
/// 当 F2 的「入口被挡住」那一格（它前摇 0 步，所以只当入口那一格、不当「在放中」那一格，理由在下面）。
/// 三格摆不出的形状由注册壳里的<b>清单探针件</b>补（<see cref="SkillDuelRift.Probe"/>），
/// 先例是 <c>fx.silence_probe</c>：不占 16 条技能名额，只为摆出某一格形状。
///
/// 场子摆成缺省那一档（英雄 100 血 20 攻 4 速、木桩 100 血 10 攻但<b>不还手</b>、隔 2 米、抖动关），
/// 因为这一档手算最省：2 米在箭雨那条距离曲线上折出来正好是 1.0，承伤倍率也是 1.0，
/// 木桩不出手所以「不按的那一叉一分不掉」（D7）这件事不需要额外摆。
/// 每一格摆成什么样（数字全部来自注册壳那一份，红了能一口说出差在哪一步）：
/// | 清单要问的形状 | H2 的载体 | 手算 |
/// |---|---|---|
/// | 有前摇 + 有消耗 + 有冷却 | <c>probe.windup_slash</c>（探针件） | 前摇 3 步、过 commit 扣怒气 30、冷却 20 步、那一刀写死 20 血 |
/// | 有前摇且在放中授予 tag | <c>probe.channel_slash</c>（探针件） | 前摇 2 步（第 2 步过 commit）、授予 <c>state.guard</c>、那一刀 15 血 |
/// | 过了 commit 登记成待决、读源属性 | <c>archer.rain</c> 的第一发 | 前摇 0 步 → 第 1 步过 commit、+4 步（第 5 步）落，伤害 = commit 抄下的攻击 20 |
/// | 一次激活两条待决 | <c>rider.joust</c> | +3 步落 20、+7 步落 12，各占一条、互不顶掉 |
/// | 入口被 tag 挡住 | <c>tank.guard</c>（<c>BlockedTags = state.silence</c>） | 挂了 <c>fx.silence_probe</c> 就放不下，且念得出 BlockedByTag |
/// | 时长 ≠ 周期 + 可叠层 | <c>warrior.bleed</c>（效果 <c>fx.bleed_wound</c>） | 时长 60 步、周期 5 步一跳 4 血、封顶 3 层每层 +2 |
/// | 显式打断且不带延迟 | <c>fx.probe_interrupt</c>（探针件） | 命中 <c>state.guard</c>，下一步 S3 开头留一行 committed=0 |
/// | 效果段授予同一个 tag | <c>fx.probe_tag_grant</c>（探针件） | 与在放中的能力段合到一张计数表 ⇒ <c>t;state.guard;2</c> |
///
/// 两条按提示想复用、实际复用不上的，写在下面免得下一位读者再试一遍：
/// - <c>warrior.execute</c> 当不了「有前摇 + 有消耗」那一格：它前摇 0 步，F1 要的是
///   「还在前摇里取消 → 一分不扣」与「过 commit 被打断 → 照扣」两半，前摇 0 步就没有前半；
/// - <c>tank.guard</c> 当得了「入口被挡住」（<c>Silenced</c> 槽位），当不了「在放中授予 tag」
///   （<c>Channeled</c> 槽位）：它前摇 0 步，D5 的对照那一半（「同一格不发取消就该走到 commit」）
///   会一步都不推、拿不到 commit，F3 要的 <c>committed=0</c> 也会读成 <c>committed=1</c>。
/// </summary>
internal sealed class ChecklistH2Host : AbilityChecklistTests.IChecklistHost, IAbilityVerbSink
{
    // === 场子的三个数字（就是 SkillDuelRift.Stage 的缺省那一档，写出来是为了钉住手算口径：
    //     缺省值漂了这里当场红，而不是让 Shapes 里那几笔伤害悄悄跟着漂）===

    /// <summary>英雄攻击：F6 与 F7 那两笔伤害都按它算（2 米那一格曲线是 1.0、承伤倍率是 1.0）。</summary>
    private const double HeroAttack = 20;

    /// <summary>木桩血量 = 清单里算各条伤害时的起算点（<c>Shapes.TargetHealth</c>）。</summary>
    private const double DummyHealth = 100;

    /// <summary>
    /// 开局把英雄的怒气补满：<c>probe.windup_slash</c> 是清单里唯一带消耗的那一条，
    /// 而单挑场的英雄缺省怒气是 0（<c>warrior.execute</c> 那格台账就建在「怒气不够 → 入口真拒绝」上）。
    /// 补这一发走词表而不是直接改装配：<b>怒气什么时候补的，行为流里读得出来</b>，
    /// 与 <c>SkillDuelRift.Stage</c> 里 <c>heroPassives</c> 那条口径同一条理由。
    /// </summary>
    private const double HeroRage = 100;

    /// <summary>清单要按下的那五条技能栏位（全场每兵都装，口径同 N-18：射程激活检查要读目标的技能栏）。</summary>
    private static readonly string[] Slots =
    {
        SkillDuelRift.Probe.WindupSlash, SkillDuelRift.Probe.ChannelSlash, SkillDuelRift.Skill.Rain,
        SkillDuelRift.Skill.Joust, SkillDuelRift.Skill.Guard,
    };

    /// <summary>新鲜的场子才有那一个对象；从快照分叉出来的那一支没有（见 <see cref="Fork"/>）。</summary>
    private readonly SkillTestWorld? _stage;

    /// <summary>层的那本状态：<c>world.Sets</c>，分支则是 <c>branch.World</c>——读数一律走这一份。</summary>
    private readonly AbilityWorld _sets;

    /// <summary>推进与快照那一个口：<c>world.Stepper</c>，分支则是分支自己那座步进器。</summary>
    private readonly AbilityStepper _stepper;

    private ChecklistH2Host(SkillTestWorld? stage, AbilityWorld sets, AbilityStepper stepper)
    {
        _stage = stage;
        _sets = sets;
        _stepper = stepper;
    }

    /// <summary>从零起一场（D1/D4 两跑、F1/F2/F8 的对照支、D7 的两叉都要新的一场）。</summary>
    public static ChecklistH2Host Fresh()
    {
        // 缺省那一档：Enemy = Static（木桩不还手）、打得死、隔 2 米、抖动关、seed 固定 1。
        var world = SkillDuelRift.Stage(new SkillTestWorld.Tuning(), seed: 1,
            heroAttack: HeroAttack, dummyHp: DummyHealth, heroAbilities: Slots, dummyAbilities: Slots);
        world.Inject(AbilityVerbs.SetBase, $"{SkillDuelRift.HeroId}|{SkillDuelRift.Attr.Rage}|{HeroRage}");
        return new ChecklistH2Host(world, world.Sets, world.Stepper);
    }

    public string Name => "H2 单挑场";

    public AbilityChecklistTests.Shapes Shapes { get; } = new()
    {
        Caster = SkillDuelRift.HeroId,
        Target = SkillDuelRift.DummyId,
        Windup = SkillDuelRift.Probe.WindupSlash,
        Channeled = SkillDuelRift.Probe.ChannelSlash,
        // 箭雨第一发：过了 commit 登记成待决、按落地那一格现算距离折损（F6 / D2 第一问）
        Flying = SkillDuelRift.Skill.Rain,
        TwoPending = SkillDuelRift.Skill.Joust,
        Silenced = SkillDuelRift.Skill.Guard,
        Wound = SkillDuelRift.Fx.BleedWound,
        Interrupter = SkillDuelRift.Probe.Interrupt,
        TagGrant = SkillDuelRift.Probe.TagGrant,
        SilenceProbe = SkillDuelRift.Fx.SilenceProbe,
        ChanneledTag = SkillDuelRift.Tag.Guard,
        SilenceTag = SkillDuelRift.Tag.Silence,
        HealthAttribute = SkillDuelRift.Attr.Health,
        AttackAttribute = SkillDuelRift.Attr.Attack,
        CostAttribute = SkillDuelRift.Attr.Rage,
        ProbeAttribute = SkillDuelRift.Attr.DamageTaken,
        TargetHealth = DummyHealth,
        WindupPreSteps = SkillDuelRift.Probe.WindupPreSteps,
        WindupCost = SkillDuelRift.Probe.WindupCostRage,
        WindupDamage = SkillDuelRift.Probe.WindupDamage,
        ChanneledPreSteps = SkillDuelRift.Probe.ChannelPreSteps,
        FlyingDelaySteps = SkillDuelRift.RainDelayA,
        // 箭雨那一发走 calc.stage_attack 那一份注册函数，读的是 commit 抄下来的攻击；
        // 2 米在它的距离曲线上（折点 0→1.0、8→1.0）折出来是 1.0，承伤倍率 1.0 ⇒ 正好 = 攻击力。
        FlyingDamage = HeroAttack,
        PendingFirstDelay = SkillDuelRift.JoustDelayFirst,
        PendingSecondDelay = SkillDuelRift.JoustDelaySecond,
        // 连刺第一段也是按攻击算（同一份 stage_attack），第二段是表里写死的定值
        PendingFirstDamage = HeroAttack,
        PendingSecondDamage = SkillDuelRift.JoustSecondDamage,
        WoundDurationSteps = SkillDuelRift.BleedDurationSteps,
        WoundPeriodSteps = SkillDuelRift.BleedPeriodSteps,
        WoundDamagePerTick = SkillDuelRift.BleedDamagePerTick,
        ProbeFirstMagnitude = 0.5,
        ProbeSecondMagnitude = 0.1,
        ProbeClampedTo = 0.2,
    };

    public bool Supports(AbilityChecklistTests.Capability capability) => capability switch
    {
        // H2 有层的 S5 那把尺（词表第九条 Move 走 TryMove，定身挂着就返回 false 并留一行 move_blocked），
        // 也有 Stepper.Branch 那个口（A51/A52 两格台账就是它）
        AbilityChecklistTests.Capability.Move => true,
        AbilityChecklistTests.Capability.Fork => true,

        // 级 2 要吃真引擎与桥：单挑场连引擎都不起，这一格只能报不适用（D8 那条把理由念出来）
        AbilityChecklistTests.Capability.LiveReplay => false,
        _ => false,
    };

    public string ReasonWithout(AbilityChecklistTests.Capability capability) => capability switch
    {
        AbilityChecklistTests.Capability.LiveReplay =>
            "级 2 要在跑着的引擎进程里按条喂同一份剧本（桥动词 replay_start / replay_run 那一轮），" +
            "离线宿主没有物理帧时间轴可比；H2 这一侧连引擎都不起（单挑场是纯 C#、零引擎的开发期资产），" +
            "所以这一格只能报不适用，那一格的实测在已删的验证报告",
        _ => string.Empty,
    };

    /// <summary>
    /// 16 格在 H2 都摆得出载体：能复用现成技能的就复用（<c>warrior.bleed</c> 顶 F4、<c>rider.joust</c> 顶 F7、
    /// <c>archer.rain</c> 顶 F6、<c>tank.guard</c> 顶 F2 入口那一半），摆不出的四格用清单探针件补
    /// （<see cref="SkillDuelRift.Probe"/>，注释写明不占 16 条名额）——所以没有一格走「不适用」。
    /// </summary>
    public string? ReasonWithout(AbilityChecklistTests.Item item) => null;

    public AbilityChecklistTests.IChecklistHost Restart() => Fresh();

    /// <summary>
    /// 推 n 步。有场子对象就走 <c>world.AdvanceStep()</c>（层的六段 + 场子那三件：光环重应用、
    /// 木桩平A、打不死回血）；分支只有层那六段——那三件不在快照文本里（N-21 那笔账）。
    /// 两条路在本壳里推出来的是同一份演化：场子摆的是「不还手 + 无光环 + 打得死」那一档，三件全是空转。
    /// </summary>
    public void Advance(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            if (_stage is { } stage)
            {
                stage.AdvanceStep();
            }
            else
            {
                _stepper.AdvanceStep();
            }
        }
    }

    /// <summary>写只走词表那一份口（「桥只转发」）：清单不许直接调层的写方法。</summary>
    public bool Inject(string cmd, string args) => AbilityVerbs.Dispatch(this, cmd, args);

    /// <summary>层那半的全量文本 = <c>world.Snapshot()</c>（那个口就是转调这一发；分支没有场子对象，读同一座步进器）。</summary>
    public string Snapshot => _stepper.TakeSnapshot();

    /// <summary>
    /// 逐字比的那份读数：<c>world.ReplaySnapshot()</c>，头部以 <c>tick=N</c> 起（D4 要拿它对行为流的步号，
    /// 段格式口径见单挑场那页 5 节）。分支那一支没有场子的开局参数那一行可念（参数不在快照文本里），
    /// 所以头部只念步号、其余段用同一个 <see cref="SkillTestWorld.LayerRowAsSegment"/> 拼——
    /// 清单里没有任何一格比过分支的这份读数（D6 比的是 <see cref="Snapshot"/>，D7 只读数值）。
    /// </summary>
    public string Frame => _stage is { } stage
        ? stage.ReplaySnapshot()
        : $"tick={_sets.Tick} | " + string.Join(" | ",
            _stepper.TakeSnapshot().Split('\n').Select(SkillTestWorld.LayerRowAsSegment));

    /// <summary>一条属性的派生值 = <c>world.Current(unit, attr)</c> 那一个口（分支对着自己那份世界读）。</summary>
    public double Value(string unit, string attribute) => _sets.Units[unit].Current[attribute];

    /// <summary>tag 计数 = <c>world.Sets.Units[unit].TagCounts</c> 那一个口（S2 是它唯一的写入处）。</summary>
    public int TagCount(string unit, string tag) => _sets.Units[unit].TagCounts.GetValueOrDefault(tag);

    /// <summary>这条效果几层 = <c>world.Stacks</c> 那一个口，清单看的就是这一个数（没有这条效果返回 -1）。</summary>
    public int Stacks(string unit, string effect)
        => _sets.Units[unit].Effects.FirstOrDefault(e => e.EffectId == effect)?.Stacks ?? -1;

    /// <summary>
    /// 行为流计数<b>两本账都要数</b>：层那本（<c>Sets.Events</c>）与场子自己那本（光环那两发形状）。
    /// 只看一本就是仓里记着的 N-22 那一类「两条路径各问一次」的坑——同一类形状在两本里都可能出现。
    /// 有场子时转发 <see cref="SkillTestWorld.Count{T}"/>（它已把两本按步号归并、按形状数）；
    /// 分支那一支只剩层那本（事件不进快照文本，分支从空账本起步，
    /// 这一点与 <see cref="Fork"/> 里 N-21 那笔账同源）。
    /// </summary>
    public int Count<T>(Func<T, bool>? keep = null) where T : EventPayload
        => _stage is { } stage
            ? stage.Count(keep)
            : keep is null
                ? _sets.Events.Count<T>()
                : _sets.Events.Where<T>((_, payload) => keep(payload)).Count();

    public bool HasEvent<T>(Func<T, bool>? keep = null) where T : EventPayload => Count(keep) > 0;

    /// <summary>
    /// 从一份快照文本分叉出另一场：<c>world.Stepper.Branch(snapshot)</c>，起跳 O(1)。
    /// <b>已知的账（N-21）</b>：分支出来的步进器仍绑着<b>父场子</b>那个 <see cref="IAbilityHost"/>，
    /// 所以这一支的读数一律走分支<b>自己那份世界</b>（<see cref="_sets"/> = <c>branch.World</c>、
    /// <see cref="_stepper"/> = 分支那座步进器），不去问父场子「谁还活着」。
    /// 代价是这一支没有场子对象：平A / 光环 / 打不死那三件与场子那本事件账都不跟过来
    /// （缺省那一档本来就不使那三件，见 <see cref="Advance"/>）。
    /// </summary>
    public AbilityChecklistTests.IChecklistHost Fork(string snapshot)
    {
        var branch = _stepper.Branch(snapshot);
        return new ChecklistH2Host(null, branch.World, branch);
    }

    // === IAbilityVerbSink：H2 这一侧的落点（每一条都只是转发到 _stepper 那一个既有口）===

    /// <summary>报错时说清是 H2 这一侧（与 <c>SkillTestWorld</c> 那一份适配器同一个身份）。</summary>
    string IAbilityVerbSink.HostLabel => "H2 单挑场";

    /// <summary>单挑场九条全接（八条原有的 + 现场扩的 <see cref="AbilityVerbs.Move"/>）。</summary>
    private static readonly IReadOnlySet<string> AllVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        AbilityVerbs.Cast, AbilityVerbs.Cancel, AbilityVerbs.ApplyEffect, AbilityVerbs.RemoveEffect,
        AbilityVerbs.SetAttribute, AbilityVerbs.SetBase, AbilityVerbs.ClearCooldown,
        AbilityVerbs.SetStepsRemaining, AbilityVerbs.Move,
    };

    IReadOnlySet<string> IAbilityVerbSink.SupportedVerbs => AllVerbs;

    /// <summary>
    /// H2 不接位移目的地（口径与 <c>SkillTestWorld</c> 那一份适配器一致）：单挑场里位置是输入的常量、
    /// 不是技能的输出，所以带第 4 段的按下由词表<b>当场抛</b>，不是静默把那一格丢掉（A69 腿 ②）。
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
    /// 激活检查问层、坐标写场子：有场子时交给 <see cref="SkillTestWorld"/> 那一份适配器（它改的是场子自己的站位），
    /// 分支那一支没有场子对象，就只问激活检查——那一支的站位本来也不跟过来（N-21 那笔账，见 <see cref="Fork"/>）。
    /// </summary>
    bool IAbilityVerbSink.Move(string unitId, double dx)
        => _stage is { } stage ? stage.Inject(AbilityVerbs.Move, $"{unitId}|{dx.ToString(CultureInfo.InvariantCulture)}")
            : _stepper.TryMove(unitId, dx);
}

/// <summary>
/// <b>清单在 H2 上跑的那一份 fixture</b>：断言全在基类（<see cref="AbilityChecklistTests"/>），
/// 这里只把壳递进去。三个 fixture（H1 / H2 / H3）跑的是同一批方法名，所以 <c>dotnet test</c> 的输出里
/// 每一条都带着宿主名重复出现，跑不了的带着理由标成不适用（D8 那一格三处都不适用）。
/// </summary>
public class AbilityChecklistOnH2 : AbilityChecklistTests
{
    protected override IChecklistHost NewHost() => ChecklistH2Host.Fresh();
}
