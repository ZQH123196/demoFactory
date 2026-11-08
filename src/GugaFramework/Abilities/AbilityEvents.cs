#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.Abilities;

// 技能层的出口：一串带字段的事件。事件类型就是名单本身——没有 Kind 枚举，
// 也没有需要背的第二张表（设计文档「A4 真值只有一处，输出只有事件」与 423 行
// 「参数随事件带」这两句的落地形态）。
//
// 三条形状规矩：
//   1. 一个 kind 一个类型。旧文本里那些「有的带尾巴、有的不带」的可选位（til= / target=- /
//      queued=1 / move= / instant）在这里各自成类——字段里不许留「先知道名字才知道用哪几个」的档位。
//   2. 名字（Wire）与那行文本（Detail）都由形状自己给，别处不再存第二份。
//   3. 只有公共层能发。宿主补记走 AbilityStepper.RecordShape，事件类型仍在这里定义。

/// <summary>数字与名单的唯一格式化处。事件行、快照行、Calc 链的台账行都走这一份。</summary>
public static class AbilityNumbers
{
    public static string Format(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    public static double Parse(string text)
        => double.Parse(text, CultureInfo.InvariantCulture);
}

/// <summary>一类事件携带的字段集合。</summary>
public abstract record EventPayload
{
    /// <summary>事件行里的第二段名字。名单只在这里。</summary>
    public abstract string Wire { get; }

    /// <summary>事件行的明细段：字段渲染出来的那串字，供人读与抽查，不是接口。</summary>
    public abstract string Detail { get; }
}

// ---- 施加与落地 ---------------------------------------------------------------

/// <summary>延迟那一发登记上了（旧 pending，不带位移尾巴）。:499</summary>
public sealed record SettlementRegistered(string EffectId, long LandingTick, double Value) : EventPayload
{
    public override string Wire => "pending";
    public override string Detail => $"{EffectId}@{LandingTick} value={AbilityNumbers.Format(Value)}";
}

/// <summary>延迟那一发登记上了，且带着位移载荷（旧 pending 的 move= 尾巴）。:499</summary>
public sealed record SettlementRegisteredWithMove(string EffectId, long LandingTick, double Value,
    double FromX, double ToX) : EventPayload
{
    public override string Wire => "pending";
    public override string Detail => $"{EffectId}@{LandingTick} value={AbilityNumbers.Format(Value)} " +
        $"move={AbilityNumbers.Format(FromX)}>{AbilityNumbers.Format(ToX)}";
}

/// <summary>延迟那一发落地了，效果打到目标身上。:182</summary>
public sealed record EffectLanded(string EffectId, string Target, long Seq, double Value) : EventPayload
{
    public override string Wire => "settle";
    public override string Detail =>
        $"{EffectId}→{Target} seq={Seq} value={AbilityNumbers.Format(Value)}";
}

/// <summary>延迟那一发作废：落地那一步目标不在了。:167</summary>
public sealed record EffectMissed(string EffectId, string Target) : EventPayload
{
    public override string Wire => "settle_void";
    public override string Detail => $"{EffectId} target={Target}";
}

/// <summary>效果整条落不上去：目标挂着免疫表里的 tag。:810</summary>
public sealed record EffectRefusedByTags(string EffectId, IReadOnlyList<string> Tags) : EventPayload
{
    public override string Wire => "effect_immune";
    public override string Detail => $"{EffectId} by={string.Join(",", Tags)}";
}

/// <summary>效果被要求条件的档挡下，这一发没走。:817</summary>
public sealed record EffectSkipped(string EffectId) : EventPayload
{
    public override string Wire => "effect_skipped";
    public override string Detail => EffectId;
}

/// <summary>即时效果施加成功（旧 apply_effect 的 x1 档）。:875</summary>
public sealed record EffectApplied(string EffectId) : EventPayload
{
    public override string Wire => "apply_effect";
    public override string Detail => $"{EffectId}x1";
}

/// <summary>即时效果施加成功且当场走完（旧 apply_effect 的 x1 instant 档）。:833</summary>
public sealed record EffectAppliedInstantly(string EffectId) : EventPayload
{
    public override string Wire => "apply_effect";
    public override string Detail => $"{EffectId}x1 instant";
}

/// <summary>身上这条效果的层数变成了 n。:853 / :1012 / :1095</summary>
public sealed record EffectStacksChanged(string EffectId, int Stacks) : EventPayload
{
    public override string Wire => "stack";
    public override string Detail => $"{EffectId}={Stacks}";
}

/// <summary>消耗一层之后身上还剩 n 层。:1012 / :1095</summary>
public sealed record EffectStackConsumed(string EffectId, int Stacks) : EventPayload
{
    public override string Wire => "unstack";
    public override string Detail => $"{EffectId}={Stacks}";
}

/// <summary>叠层撞到上限，层数停在 capacity。:859</summary>
public sealed record EffectStackCapped(string EffectId, int Capacity) : EventPayload
{
    public override string Wire => "stack_capped";
    public override string Detail => $"{EffectId}={Capacity}";
}

/// <summary>回充载体把层数加回来了（N-15 那一格）。:351</summary>
public sealed record ChargeRefilled(string EffectId, int Stacks) : EventPayload
{
    public override string Wire => "recharge";
    public override string Detail => $"{EffectId}={Stacks}";
}

/// <summary>周期那一跳跳了一发，身上 n 层。:364</summary>
public sealed record PeriodTicked(string EffectId, int Stacks) : EventPayload
{
    public override string Wire => "period";
    public override string Detail => $"{EffectId}x{Stacks}";
}

/// <summary>身上这条效果到期。:213</summary>
public sealed record EffectExpired(string EffectId) : EventPayload
{
    public override string Wire => "expire";
    public override string Detail => EffectId;
}

/// <summary>身上这条效果被摘走（摘动作的落点，不是到期）。:1102</summary>
public sealed record EffectRemoved(string EffectId) : EventPayload
{
    public override string Wire => "remove_effect";
    public override string Detail => EffectId;
}

/// <summary>触发链点火：某个事件名唤起了哪条效果。:1051</summary>
public sealed record TriggerFired(string EventName, string EffectId) : EventPayload
{
    public override string Wire => "trigger";
    public override string Detail => $"{EventName}->{EffectId}";
}

// ---- 修饰符与属性 -------------------------------------------------------------

/// <summary>挂上一条修饰符，身上留着它直到步数到点。:890</summary>
public sealed record ModifierAppliedUntil(string Target, ModifierMode Mode, double Magnitude,
    long UntilTick) : EventPayload
{
    public override string Wire => "modifier";
    public override string Detail =>
        $"{Target}:{Mode}~{AbilityNumbers.Format(Magnitude)} til={UntilTick}";
}

/// <summary>挂上一条修饰符、这一发不设到期步（旧文本里那个 til=-）。:890</summary>
public sealed record ModifierAppliedOpenEnded(string Target, ModifierMode Mode, double Magnitude)
    : EventPayload
{
    public override string Wire => "modifier";
    public override string Detail =>
        $"{Target}:{Mode}~{AbilityNumbers.Format(Magnitude)} til=-";
}

/// <summary>一条只活一步的修饰符过去了（一次性的加/乘）。:924</summary>
public sealed record ModifierFlewBy(string Target, ModifierMode Mode) : EventPayload
{
    public override string Wire => "modifier_transient";
    public override string Detail => $"{Target}:{Mode}";
}

/// <summary>身上那条修饰符到期。:223</summary>
public sealed record ModifierExpiredOnBody(string Target, ModifierMode Mode) : EventPayload
{
    public override string Wire => "modifier_expired";
    public override string Detail => $"{Target}:{Mode}";
}

/// <summary>基础值直接加了一笔（Add 档的一次性写法）。:915</summary>
public sealed record BaseRaisedBy(string Attribute, double Delta) : EventPayload
{
    public override string Wire => "instant_add";
    public override string Detail => $"{Attribute}{AbilityNumbers.Format(Delta)}";
}

/// <summary>基础值整格改成这个数。:1122</summary>
public sealed record BaseSetTo(string Attribute, double Value) : EventPayload
{
    public override string Wire => "set_base";
    public override string Detail => $"{Attribute}={AbilityNumbers.Format(Value)}";
}

/// <summary>这一发扣掉的代价。:453</summary>
public sealed record CostPaid(string Attribute, double Amount) : EventPayload
{
    public override string Wire => "cost";
    public override string Detail => $"{Attribute}-{AbilityNumbers.Format(Amount)}";
}

// ---- 伤害的三条路 -------------------------------------------------------------

/// <summary>伤害落地，因果都带在身上：谁打的、经哪条效果。:972</summary>
public sealed record DamageDealt(double Final, string Source, string Via) : EventPayload
{
    public override string Wire => "damage";
    public override string Detail => $"{AbilityNumbers.Format(Final)} from={Source} via={Via}";
}

/// <summary>伤害被身上那条抵挡效果整条吃掉。:964</summary>
public sealed record DamageRefused(string EffectId, double Raw) : EventPayload
{
    public override string Wire => "damage_nullified";
    public override string Detail => $"{EffectId} raw={AbilityNumbers.Format(Raw)}";
}

/// <summary>伤害被身上那条效果挡下一部分，消耗一层的落点在这里。:999</summary>
public sealed record DamageBlockedByEffect(string EffectId, double Raw, string Source, string Via)
    : EventPayload
{
    public override string Wire => "damage_blocked";
    public override string Detail =>
        $"{EffectId} raw={AbilityNumbers.Format(Raw)} from={Source} via={Via}";
}

// ---- 技能的相位与激活检查 ---------------------------------------------------------

/// <summary>按下技能，打在自己身上（旧 activate 的 target=- 档）。:670</summary>
public sealed record AbilityActivated(string AbilityId) : EventPayload
{
    public override string Wire => "activate";
    public override string Detail => $"{AbilityId} target=-";
}

/// <summary>按下技能，打向某个目标。:670</summary>
public sealed record AbilityActivatedAt(string AbilityId, string Target) : EventPayload
{
    public override string Wire => "activate";
    public override string Detail => $"{AbilityId} target={Target}";
}

/// <summary>过了 commit 那一步，效果在这一刻交出去。:527</summary>
public sealed record AbilityCommitted(string AbilityId) : EventPayload
{
    public override string Wire => "commit";
    public override string Detail => AbilityId;
}

/// <summary>这一发从头到尾走完了。:402</summary>
public sealed record AbilityCompleted(string AbilityId) : EventPayload
{
    public override string Wire => "ability_done";
    public override string Detail => AbilityId;
}

/// <summary>这次没放出去，为什么没放有名字。:654</summary>
public sealed record AbilityRejected(string AbilityId, AbilityRejectReason Reason) : EventPayload
{
    public override string Wire => "reject";
    public override string Detail => $"{AbilityId} reason={Reason}";
}

/// <summary>这次没放出去，但队列把它收下了（冷却中的排队补放，N-16）。:654</summary>
public sealed record AbilityRejectedAndQueued(string AbilityId, AbilityRejectReason Reason) : EventPayload
{
    public override string Wire => "reject";
    public override string Detail => $"{AbilityId} reason={Reason} queued=1";
}

/// <summary>队列里那一发被放出去，打向某个目标。:578</summary>
public sealed record QueuedCastTakenAt(string AbilityId, string Target) : EventPayload
{
    public override string Wire => "queue_drain";
    public override string Detail => $"{AbilityId} target={Target}";
}

/// <summary>队列里那一发被放出去，打在自己身上。:578</summary>
public sealed record QueuedCastTaken(string AbilityId) : EventPayload
{
    public override string Wire => "queue_drain";
    public override string Detail => $"{AbilityId} target=-";
}

/// <summary>技能被打断：什么原因、是否已经过了 commit。:437</summary>
public sealed record AbilityInterrupted(string AbilityId, string Cause, bool Committed) : EventPayload
{
    public override string Wire => "interrupt";
    public override string Detail =>
        $"{AbilityId} cause={Cause} committed={(Committed ? 1 : 0)}";
}

/// <summary>技能被撤，前摇未到 commit 所以代价一分未扣。:1158</summary>
public sealed record CancelRefunded(string AbilityId) : EventPayload
{
    public override string Wire => "cancel_refund";
    public override string Detail => $"{AbilityId} cost_untouched";
}

/// <summary>技能被撤。:1164</summary>
public sealed record AbilityCanceled(string AbilityId) : EventPayload
{
    public override string Wire => "cancel";
    public override string Detail => AbilityId;
}

/// <summary>冷却开始，到第几步转好。:1175</summary>
public sealed record CooldownStarted(string AbilityId, long EndsAtTick) : EventPayload
{
    public override string Wire => "cooldown_set";
    public override string Detail => $"{AbilityId}@{EndsAtTick}";
}

/// <summary>冷却转好了。:236</summary>
public sealed record CooldownReady(string AbilityId) : EventPayload
{
    public override string Wire => "cooldown_ready";
    public override string Detail => AbilityId;
}

/// <summary>冷却被清掉（调试注入口与某些效果的附带动作都走这里）。:1139</summary>
public sealed record CooldownCleared(string AbilityId) : EventPayload
{
    public override string Wire => "cooldown_cleared";
    public override string Detail => AbilityId;
}

/// <summary>前摇剩余步数被改成 n（分支对照与剧本注入要这一发）。:1288</summary>
public sealed record StepsReset(string AbilityId, long Remaining) : EventPayload
{
    public override string Wire => "steps_set";
    public override string Detail => $"{AbilityId} remaining={Remaining}";
}

// ---- 位移 ---------------------------------------------------------------------

/// <summary>层自己那根镜像坐标落到了新位置。:190</summary>
public sealed record UnitMoved(double ToX, double FromX) : EventPayload
{
    public override string Wire => "move";
    public override string Detail =>
        $"x={AbilityNumbers.Format(ToX)} from={AbilityNumbers.Format(FromX)}";
}

/// <summary>想走没走成（定身那一档的激活检查）。:701</summary>
public sealed record MoveBlocked(double Dx) : EventPayload
{
    public override string Wire => "move_blocked";
    public override string Detail => $"dx={AbilityNumbers.Format(Dx)}";
}

// ---- 宿主补记的形状命中 -------------------------------------------------------

/// <summary>扇形那一发打到了人：半径、全角、命中名单都带字段（旧 slam_shape 的 hits= 档）。
/// 由 <see cref="AbilityStepper.RecordShape"/> 发出——名字与形状仍归公共层，标量由宿主递。</summary>
public sealed record ShapeHitLanded(double Radius, double ConeDegrees, IReadOnlyList<string> HitUnitIds)
    : EventPayload
{
    public override string Wire => "slam_shape";
    public override string Detail =>
        $"r={AbilityNumbers.Format(Radius)} cone={AbilityNumbers.Format(ConeDegrees)} " +
        $"hits={string.Join(",", HitUnitIds)}";
}

/// <summary>扇形那一发打空了。空集也念得出——「打空要读得出为什么空」。</summary>
public sealed record ShapeHitMissed(double Radius, double ConeDegrees) : EventPayload
{
    public override string Wire => "slam_shape";
    public override string Detail =>
        $"r={AbilityNumbers.Format(Radius)} cone={AbilityNumbers.Format(ConeDegrees)} hits=-";
}

/// <summary>一条事件：第几步、谁的、带什么字段。</summary>
public readonly record struct AbilityEvent(long Tick, string Subject, EventPayload Payload)
{
    /// <summary>给人读与供抽查的那一行字。真值在字段里，不在这里（快照另有一份）。</summary>
    public string Line => $"{Tick} {Payload.Wire} {Subject} {Payload.Detail}";
}

/// <summary>
/// 事件流的存放处：有界环形，丢最旧的。读数口只有这一份——尾窗、按形状过滤、按形状计数，
/// 以及「被丢了多少」这个信号（与 RollingLog、CueFeed.Readout 同一个读法：没有 ≠ 没发生）。
/// </summary>
public sealed class AbilityEventLog
{
    private readonly LinkedList<AbilityEvent> _kept = new();
    private int _capacity;

    /// <summary>累计发出多少条，包含被丢掉的那些。</summary>
    public long Total { get; private set; }

    /// <summary>由 <see cref="AbilityLayerConfig.EventCapacity"/> 在接层时写进来；0 = 还没配。</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Capacity),
                    $"事件流容量必须是正数，本层收到 {value}——AbilityLayerConfig.EventCapacity 没给还是给成 0 了？");
            }

            if (_capacity != 0 && _capacity != value)
            {
                throw new InvalidOperationException(
                    $"事件流容量已经定成 {_capacity}，不给第二次改（两份容量等于两份实现）：现值 {value}");
            }

            _capacity = value;
        }
    }

    /// <summary>被环形挤出去的条数。读数里那一行 dropped= 就是它。</summary>
    public long Dropped => Total - _kept.Count;

    public int KeptCount => _kept.Count;

    public void Add(AbilityEvent e)
    {
        if (_capacity == 0)
        {
            throw new InvalidOperationException(
                "事件流容量还没配：AbilityLayerConfig.EventCapacity 要给一个正数（公共层不替你定这一格）。");
        }

        Total++;
        _kept.AddLast(e);
        while (_kept.Count > _capacity)
        {
            _kept.RemoveFirst();
        }
    }

    /// <summary>全部还留着的事件，按发出顺序。</summary>
    public IReadOnlyList<AbilityEvent> All => _kept.Count == 0 ? Array.Empty<AbilityEvent>() : new List<AbilityEvent>(_kept);

    /// <summary>最近 n 条（不足 n 就给全部）。</summary>
    public IReadOnlyList<AbilityEvent> Tail(int n)
    {
        if (n <= 0)
        {
            return Array.Empty<AbilityEvent>();
        }

        var take = Math.Min(n, _kept.Count);
        var from = _kept.Count - take;
        var i = 0;
        var buf = new AbilityEvent[take];
        foreach (var e in _kept)
        {
            if (i++ < from)
            {
                continue;
            }

            buf[i - from - 1] = e;
        }

        return buf;
    }

    /// <summary>某一类事件的尾窗文本：带头一行读数（dropped= 与 total=），正文逐条 Line。</summary>
    public string TailText(int n)
    {
        var sb = new StringBuilder();
        sb.Append($"dropped={Dropped} total={Total} kept={KeptCount}\n");
        foreach (var e in Tail(n))
        {
            sb.Append(e.Line).Append('\n');
        }

        return sb.ToString();
    }

    public IEnumerable<T> Where<T>() where T : EventPayload
    {
        foreach (var e in _kept)
        {
            if (e.Payload is T typed)
            {
                yield return typed;
            }
        }
    }

    public IEnumerable<AbilityEvent> Where<T>(Func<AbilityEvent, T, bool> keep) where T : EventPayload
    {
        foreach (var e in _kept)
        {
            if (e.Payload is T typed && keep(e, typed))
            {
                yield return e;
            }
        }
    }

    public int Count<T>() where T : EventPayload
    {
        var n = 0;
        foreach (var e in _kept)
        {
            if (e.Payload is T)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>清场（分支与重建用）：只清留着的，总数不清——发过多少条是历史。</summary>
    public void ClearKept() => _kept.Clear();

    /// <summary>整份换过去（Restore / Branch 用），容量沿用现有那份。</summary>
    public void ReplaceWith(IEnumerable<AbilityEvent> events)
    {
        _kept.Clear();
        Total = 0;
        foreach (var e in events)
        {
            Add(e);
        }
    }
}
