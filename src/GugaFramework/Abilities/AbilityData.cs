#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.Abilities;

// 这一层是「每步重算的一笔账」：没有任何对象持有执行权，时间只有步这一把尺。
// 设计出处：docs/research/技能系统/调研结论摘要.md（原来那六份调研稿已并入这一份摘要）
// 四条前提 A1~A4、字段形状与新增列的收口记录见该文 4 节（4.1~4.3）。
// 本文件只装数据：不认识世界、不认识步、不认识事件（依赖方向见已删的设计档）。

/// <summary>修饰符的作用方式。三种一起汇成一个值，再过一次上下界（顺序见 4.3 节）。</summary>
public enum ModifierMode
{
    Add,
    Mult,
    Override,
}

/// <summary>效果的存活方式。Duration 的时长与 period 的周期是两列，互不相干（F4）。</summary>
public enum EffectKind
{
    Instant,
    Duration,
    Infinite,
}

/// <summary>
/// 同一 <c>StackKey</c> 的效果再次施加时怎么办（N-6）。归并只有一处实现——<c>AbilityStepper.MergesInto</c>：
/// 给了键的按 <c>StackKey</c> 归，没给的按自己的 Id 归；跨条定义共用一键时<b>谁先挂上谁当家</b>，
/// 层数与到期都记在当家那条实例上（A79/A80 钉这两格）。
/// 这一列同时被 <see cref="EffectDef.IsChargeCarrier"/> 借作「层数可归并」的存在性标记（N-15「不加列」），
/// 所以它有两重身份：改它的语义要一起看 S1 那一跳与快照那格 <c>charges=</c>。
/// </summary>
public enum StackPolicy
{
    /// <summary>加一层，时长不重置（warrior.bleed 用的是这一档）。</summary>
    AddStackKeepDuration,

    /// <summary>加一层并把时长刷新到最新一次。</summary>
    AddStackRefreshDuration,

    /// <summary>不叠层，后来者整条替换先前那条。</summary>
    Replace,
}

/// <summary>技能状态机的五个档，全是整数步驱动，没有一处等引擎。</summary>
public enum AbilityPhase
{
    Ready,
    Pre,
    Execute,
    Post,
    Cooling,
}

/// <summary>「为什么没放」有名字，进快照、进事件流。</summary>
public enum AbilityRejectReason
{
    None,
    NotInRange,
    Cooldown,
    MissingTag,
    BlockedByTag,
    NoCost,
    NoCharge,
    Dead,
    Interrupted,

    /// <summary>还在前摇或后摇里——「为什么这次没放」也要有名字，不能只回一个 false。</summary>
    Busy,

    /// <summary>
    /// 目标不在层里（<see cref="IAbilityHost.Exists"/> 说不认识它）——「目标中途被拆了」那一档。
    /// 它排在激活检查那一组的最前面：后面的射程要按目标算距离，目标都不在时那一问没有意义。
    /// 名字只说「层里没有这一位」；「是不是被摧毁」归宿主那个口回答（层不认识建筑，也不认识死）。
    /// </summary>
    TargetGone,
}

/// <summary>表里的一条修饰符规格。CalcFromEffectValue 表示数值走效果那条 Calc 链的产物。</summary>
public sealed record ModifierSpec(
    string Target,
    ModifierMode Mode,
    double Magnitude,
    bool CalcFromEffectValue = false,
    IReadOnlyList<string> RequirementTags = null!);

/// <summary>效果表里的一条定义（数据，不是执行体：它自己不动，由 S0/S1/S3 解释）。</summary>
public sealed record EffectDef(
    string Id,
    EffectKind Kind,
    IReadOnlyList<ModifierSpec> Modifiers,
    IReadOnlyList<string> GrantedTags,
    IReadOnlyList<string> RequirementTags,
    IReadOnlyList<string> BlockedTags,
    IReadOnlyList<string> CalcNames,
    IReadOnlyDictionary<string, double> CalcParams,
    double CalcSeed,
    long PeriodTicks,
    IReadOnlyList<string> CancelMatchingTags,
    IReadOnlyList<(string OnEvent, string EffectId)> TriggeredEffects,
    string? StackKey = null,
    int MaxStacks = 1,
    double MagnitudePerStack = 0,
    StackPolicy StackPolicy = StackPolicy.AddStackKeepDuration,
    long DurationTicks = 0,
    long DelayTicks = 0,
    IReadOnlyList<string>? ImmunityTags = null,
    IReadOnlyList<double>? CurveXY = null,
    string CurveAxis = "")
{
    /// <summary>目标挂着这里任一条 tag 时，这条效果落不上去（GAS 的 ImmunityGameplayEffectTags 同构）。</summary>
    public IReadOnlyList<string> ImmunityTagsOrEmpty => ImmunityTags ?? Array.Empty<string>();

    /// <summary>
    /// 「周期回充层数」在这里只认一种形状（N-15，已删的设计档：不加列）。回充的表达就是一常驻效果带周期——
    /// <see cref="Kind"/> / <see cref="PeriodTicks"/> / <see cref="StackKey"/> / <see cref="Modifiers"/>
    /// 四格都是既有字段，这里只把它们读成一条规则：只授予 tag、自己带周期、层数可归并的效果，
    /// 它的周期那一跳做的是「把层数加回去」，不是「再打一跳」。<b>S1 与快照读的是这同一处。</b>
    /// </summary>
    public bool IsChargeCarrier => Kind == EffectKind.Infinite && PeriodTicks > 0
        && StackKey is not null && Modifiers.Count == 0;

    /// <summary>折点数组按 <c>x0,y0,x1,y1,...</c> 平铺；采样轴由 <see cref="CurveAxis"/> 指。</summary>
    public bool HasCurve => CurveXY is { Count: >= 4 };
    /// <summary>一次性的即时效果：落地就走，不挂持续时间。</summary>
    public static EffectDef Instant(string id, ModifierSpec modifier, string? stackKey = null, int maxStacks = 1)
        => new(id, EffectKind.Instant, new[] { modifier }, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new Dictionary<string, double>(), 0, 0, Array.Empty<string>(),
            Array.Empty<(string, string)>(), stackKey, maxStacks);

    /// <summary>延迟结算（投射物 / 飞行伤害）：登记即入快照，落地那一步目标现解。</summary>
    public static EffectDef Delayed(string id, ModifierSpec modifier, long delayTicks)
        => new(id, EffectKind.Instant, new[] { modifier }, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new Dictionary<string, double>(), 0, 0, Array.Empty<string>(),
            Array.Empty<(string, string)>(), DelayTicks: delayTicks);

    /// <summary>持续效果：Modifiers 挂在身上到 ExpiresAtTick，PeriodTicks 大于 0 时每周期再来一发。</summary>
    public static EffectDef OverTime(string id, IReadOnlyList<ModifierSpec> modifiers, long durationTicks,
        long periodTicks = 0, IReadOnlyList<string>? grantedTags = null, string? stackKey = null,
        int maxStacks = 1, double magnitudePerStack = 0,
        StackPolicy stackPolicy = StackPolicy.AddStackKeepDuration,
        IReadOnlyList<string>? calcNames = null, double calcSeed = 0,
        IReadOnlyList<string>? cancelMatchingTags = null,
        EffectKind kind = EffectKind.Duration,
        IReadOnlyDictionary<string, double>? calcParams = null,
        long delayTicks = 0,
        IReadOnlyList<string>? requirementTags = null,
        IReadOnlyList<string>? blockedTags = null,
        IReadOnlyList<(string OnEvent, string EffectId)>? triggeredEffects = null,
        IReadOnlyList<string>? immunityTags = null,
        IReadOnlyList<double>? curveXY = null,
        string curveAxis = "")
        => new(id, kind, modifiers, grantedTags ?? Array.Empty<string>(),
            requirementTags ?? Array.Empty<string>(), blockedTags ?? Array.Empty<string>(),
            calcNames ?? Array.Empty<string>(), calcParams ?? new Dictionary<string, double>(),
            calcSeed, periodTicks, cancelMatchingTags ?? Array.Empty<string>(),
            triggeredEffects ?? Array.Empty<(string, string)>(), stackKey, maxStacks, magnitudePerStack,
            stackPolicy, durationTicks, delayTicks, immunityTags ?? Array.Empty<string>(), curveXY, curveAxis);
}

/// <summary>
/// 技能表里的一条定义。射程与冷却都按步与米，不问引擎。
/// <paramref name="MaxCharges"/> 缺省 <b>0 = 不设充能激活检查</b>（冷却技能的本档）；要「3 发、每 N 秒回 1」
/// 那一档的技能显式给数字。缺省曾经写成 1，P2 的排队补放撞上去才暴露：那等于把所有技能默认成
/// 「一局只放一次」——H3 那条突斩就是被它默默放死的（见验证报告 P2 那一节）。
/// </summary>
public sealed record AbilityDef(
    string Id,
    long PreSteps,
    long CooldownTicks,
    IReadOnlyList<string> GrantedTagsWhileActive,
    IReadOnlyList<string> RequirementTags,
    IReadOnlyList<string> BlockedTags,
    IReadOnlyList<string> EffectIds,
    string CostAttribute,
    double CostAmount,
    double RangeMeters,
    int MaxCharges = 0,
    long PostSteps = 0);

/// <summary>
/// 身上的一条修饰符实例。<see cref="RequirementTags"/> 不满足时 S2 跳过它、但不撤销（N-5）。
/// </summary>
public sealed record ModifierOnBody(
    long Seq,
    string Target,
    ModifierMode Mode,
    double Magnitude,
    long AppliedAtTick,
    long? ExpiresAtTick,
    string SourceId,
    string? SourceEffectId,
    IReadOnlyList<string> RequirementTags);

/// <summary>身上挂着的一条效果实例。层数与上次周期那一步是「历史」，必须显式记（已删的设计档那笔税）。</summary>
public sealed class EffectOnBody
{
    public required string EffectId { get; init; }
    public int Stacks { get; set; } = 1;
    public long AppliedAtTick { get; init; }
    public long? ExpiresAtTick { get; set; }
    public long LastPeriodTick { get; set; } = long.MinValue;
}

/// <summary>
/// 延迟结算与飞行物的唯一载体（N-3/N-9）。它不是某个技能的从属记录：取消技能不取消已登记的结算。
/// SourceAttrsSnapshot 是过 commit 那一步抄下来的源属性——不抄的话，飞行途中施法者被杀，伤害会跟着掉。
/// </summary>
public sealed class PendingSettlement
{
    public required long Seq { get; init; }
    public required long ExecuteAtTick { get; init; }
    public required string SourceId { get; init; }
    public required string EffectId { get; init; }
    public IReadOnlyList<string> TargetIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, double> SourceAttrsSnapshot { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Calc 链在登记时算好的产物，落地直接用（曲线/距离这类输入也跟着抄）。</summary>
    public double CalcValue { get; init; }

    /// <summary>位移载荷（N-3）。本版公共层留着形状，H1 不跑它——位移归属见单挑场那页 12 节待确认项 5。</summary>
    public (double FromX, double ToX)? Move { get; init; }
}

/// <summary>
/// 一个技能在某单位身上的运行时状态（PerActor 一份）。
/// </summary>
public sealed class AbilityOnBody
{
    public required string Id { get; init; }
    public AbilityPhase Phase { get; set; } = AbilityPhase.Ready;
    public long StepsRemaining { get; set; }
    public long? CommitTick { get; set; }
    public long? CooldownEndsAtTick { get; set; }
    public int ChargesLeft { get; set; }
    public string? TargetId { get; set; }
    public AbilityRejectReason RejectedReason { get; set; } = AbilityRejectReason.None;

    /// <summary>
    /// 排队释放（N-16，已删的设计档本轮唯一动 record 的那一列）：冷却中收到的 `CastAbility` 不丢，
    /// 记成「排一个」，队列上限 1。补放走的是同一个 S4 入口（<c>TryActivate</c>），不开第二条接受路径。
    /// 排队要打的谁抄在既有的 <see cref="TargetId"/> 那一列里——不为这一格再开一列。
    /// </summary>
    public bool QueuedInput { get; set; }

    /// <summary>
    /// 位移那一格（N-3 的正主，第二批 <c>rider.charge</c> 落地时补上）：按下技能时宿主递进来的<b>起点与目的地</b>，
    /// 与 <see cref="TargetId"/> 同属「这一步的意图」，所以住在同一张 record 里、不加第二个入口。
    /// <b>两个端点都由宿主给</b>：单位身上不再有坐标（那一格已从公共层拿掉），层算不出「从哪儿出发」，
    /// 也不该替宿主猜——它只把这对标量原样登记成 <see cref="PendingSettlement.Move"/>，到位那一步发事件。
    /// <b>世界坐标的写归宿主</b>（单挑场那页 12 节 待确认项 5 已确认），层不落任何一笔坐标。
    /// </summary>
    public (double FromX, double ToX)? Move { get; set; }
}

/// <summary>一个单位的技能栏：属性 + 修饰符 + 效果 + 技能 + 待决结算 + 历史计数。宿主容器按逻辑 ID 索引。</summary>
public sealed class AbilitySet
{
    public required string UnitId { get; init; }

    /// <summary>属性名 → 基础值。伤害扣血改的是这里，Current 只在 S2 派生。</summary>
    public Dictionary<string, double> Base { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, double> Min { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Max { get; } = new(StringComparer.Ordinal);

    /// <summary>S2 出口那份派生值。判定与显示都读它，不在调用点现算。</summary>
    public Dictionary<string, double> Current { get; } = new(StringComparer.Ordinal);

    /// <summary>S2 派生的 tag 计数（唯一写入处是 S2）。</summary>
    public Dictionary<string, int> TagCounts { get; } = new(StringComparer.Ordinal);

    public List<ModifierOnBody> Modifiers { get; } = new();
    public List<EffectOnBody> Effects { get; } = new();
    public Dictionary<string, AbilityOnBody> Abilities { get; } = new(StringComparer.Ordinal);
    public List<PendingSettlement> Pendings { get; } = new();

    /// <summary>历史计数（快照里那行 stats=）：本设计里凡有历史依赖的行为都得在这儿显式记一笔。</summary>
    public Dictionary<string, double> Counters { get; } = new(StringComparer.Ordinal);
}

/// <summary>世界向技能层供的两样：单位查询口、随机源（已删的设计档「取数」口）。
/// <b>事件出口不在这一格里</b>——事件一直是层自己发的，宿主只读它（名单与形状见 AbilityEvents.cs）。</summary>
public interface IAbilityHost
{
    /// <summary>这个单位在哪个位置；不存在返回 null。</summary>
    AbilitySet? Find(string unitId);

    /// <summary>还活着没（hp 归零即死）。</summary>
    bool IsAlive(string unitId);

    /// <summary>
    /// 这个 Id 此刻是不是<b>有效目标</b> —— <b>「目标表查法」就这一格</b>。
    ///
    /// <para><b>为什么激活检查要问它</b>：层的目标册（<see cref="AbilityWorld.Units"/>）是宿主填的，
    /// 而目标可能在<b>这一步的结算之前</b>就没了（建筑被拆、单位阵亡）。宿主那一册可能不摘册
    /// （摘了的话层快照的段数会随局势伸缩），所以「还在不在」只有宿主答得出。不问就往下走的话，
    /// 层会拿一个已经不在场的 Id 去算射程与效果 —— 那不是「打不着」，是「这一位不存在了」，
    /// 两件事的理由必须分开（<see cref="AbilityRejectReason.TargetGone"/>）。</para>
    ///
    /// <para><b>这一格与 <see cref="IsAlive"/> 的关系由宿主定</b>：只装单位的宿主两格等价；
    /// 还装建筑这类没有「死」这个状态的目标时，宿主按自己两册的血量回答，仍是一处实现。</para>
    /// </summary>
    bool Exists(string entityId);

    /// <summary>场上全部单位的 Id，顺序稳定（同一步遍历次序是确定性的一部分）。</summary>
    IReadOnlyList<string> UnitIds { get; }

    /// <summary>随机源走宿主，种子在宿主手里；技能层不自带 Random。</summary>
    double NextRandom();
}

/// <summary>整局技能层的根状态：第几步 + 每个单位的技能栏 + 事件流 + 自增序号。</summary>
public sealed class AbilityWorld
{
    public long Tick { get; set; }

    /// <summary>modifier 与 Pending 的排序键来源，由施加动作自增分配（两跑必然一致）。</summary>
    public long NextSeq { get; set; } = 1;

    public List<string> UnitOrder { get; } = new();
    public Dictionary<string, AbilitySet> Units { get; } = new(StringComparer.Ordinal);
    public AbilityEventLog Events { get; } = new();

    public AbilitySet AddUnit(string unitId, IEnumerable<(string Name, double Value)> attributes)
    {
        var set = new AbilitySet { UnitId = unitId };
        foreach (var (name, value) in attributes)
        {
            set.Base[name] = value;
            if (!set.Min.ContainsKey(name))
            {
                set.Min[name] = double.MinValue;
            }

            if (!set.Max.ContainsKey(name))
            {
                set.Max[name] = double.MaxValue;
            }
        }

        Units[unitId] = set;
        UnitOrder.Add(unitId);
        return set;
    }

    public void SetLimits(string unitId, string attribute, double min, double max)
    {
        var set = Units[unitId];
        set.Min[attribute] = min;
        set.Max[attribute] = max;
    }
}
