#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.Entities;

// 实体层的事件出口。形状照建造层与技能层的先例：一个形状一个类型、没有 Kind 枚举、
// 没有要背的第二张表，Wire 与那行明细由类型自己给（与 ADR 0001 同一口径）。
//
// 「在场的事全在一本册子里」（spec 1 节）里，事件出口是八项之一：表现层与存档读数
// 都靠这三个事件知道册子变过。**只有三个**（D12）——
// 「序号推进」与「册子重置」不是业务事件：前者是 <c>Spawn</c> 那一发的副产品，
// 后者是 <c>ReadState</c> 的一次调用，念它等于让订阅者为一次读档写处理器。

/// <summary>数字的唯一格式化处。事件行、快照段、读数都走这一份（「double 与不变文化」）。</summary>
public static class EntityNumbers
{
    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// 一类实体事件携带的字段集合。<b>只有册子能发</b>：表现层与原型读它、订阅它，
/// 但没有任何途径造一条出来。
///
/// <para><b>步号住在事件类型自己身上</b>（init 属性，不是位置参数），派生类型继承而不是遮蔽
/// —— 遮蔽会出 CS0108 警告。与建造层同一口径。</para>
/// </summary>
public abstract record EntityEventPayload
{
    /// <summary>第几步。构造时递进来的时钟读数。</summary>
    public long Tick { get; init; }

    /// <summary>动的是哪一条（就是那一个 id）。</summary>
    public string Subject { get; init; } = "";

    /// <summary>事件行里的第二段名字。</summary>
    public abstract string Wire { get; }

    /// <summary>事件行的明细段：字段渲染出来的那串字，供人读与抽查，不是接口。</summary>
    public abstract string Detail { get; }

    /// <summary>给人读与供抽查的那一行字。真值在字段里，不在这里（快照另有一份）。</summary>
    public string Line => $"{EntityNumbers.Format(Tick)} {Wire} {Subject} {Detail}";
}

/// <summary>一条生成上了册（<see cref="EntityCore.Spawn"/> 那一发，序号是册子新发的那一格）。</summary>
public sealed record EntitySpawned(long Seq, string Kind) : EntityEventPayload
{
    public override string Wire => "spawned";

    public override string Detail => $"seq={EntityNumbers.Format(Seq)} kind={Kind}";
}

/// <summary>一条摘了册（<see cref="EntityCore.Remove"/> 那一发，序号是它首次登记发的那一格）。</summary>
public sealed record EntityRemoved(long Seq, string Kind) : EntityEventPayload
{
    public override string Wire => "removed";

    public override string Detail => $"seq={EntityNumbers.Format(Seq)} kind={Kind}";
}

/// <summary>
/// 一条复用了既有 id 重新上册（<see cref="EntityCore.Register"/> 那一发，D9 的不复活登记口）。
/// <b>不发新序号</b>，所以 <c>Seq</c> 是它首次登记那一格 —— 与 <c>EntitySpawned</c> 差在这一格上。
/// </summary>
public sealed record EntityReRegistered(long Seq, string Kind) : EntityEventPayload
{
    public override string Wire => "reregistered";

    public override string Detail => $"seq={EntityNumbers.Format(Seq)} kind={Kind}";
}

/// <summary>
/// 事件流的存放处：有界环形，丢最旧的。读数口只有这一份 —— 尾窗、按形状过滤、按形状计数，
/// 以及「被丢了多少」这个信号（与 <c>ConstructionEventLog</c>、<c>AbilityEventLog</c> 同一个读法：
/// 「日志里没有」≠「没发生」，AGENTS「三条铁律」第三条）。
///
/// <para>容量构造时给，不给第二份改法：两份容量等于两份实现。</para>
/// </summary>
public sealed class EntityEventLog
{
    private readonly LinkedList<EntityEventPayload> _kept = new();

    public EntityEventLog(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                "事件流容量必须是正数（模块不替你定这一格：给少了丢事件、给 0 是没接上）");
        }

        Capacity = capacity;
    }

    /// <summary>累计发出多少条，包含被丢掉的那些。</summary>
    public long Total { get; private set; }

    public int Capacity { get; }

    /// <summary>被环形挤出去的条数。读数里那一行 <c>dropped=</c> 就是它。</summary>
    public long Dropped => Total - _kept.Count;

    public int KeptCount => _kept.Count;

    public void Add(EntityEventPayload e)
    {
        Total++;
        _kept.AddLast(e);
        while (_kept.Count > Capacity)
        {
            _kept.RemoveFirst();
        }
    }

    /// <summary>全部还留着的事件，按发出顺序。</summary>
    public IReadOnlyList<EntityEventPayload> All
        => _kept.Count == 0 ? Array.Empty<EntityEventPayload>() : new List<EntityEventPayload>(_kept);

    /// <summary>最近 n 条（不足 n 就给全部）。</summary>
    public IReadOnlyList<EntityEventPayload> Tail(int n)
    {
        if (n <= 0) return Array.Empty<EntityEventPayload>();

        var take = Math.Min(n, _kept.Count);
        var from = _kept.Count - take;
        var buf = new EntityEventPayload[take];
        var i = 0;
        foreach (var e in _kept)
        {
            if (i++ < from) continue;
            buf[i - from - 1] = e;
        }

        return buf;
    }

    /// <summary>某一类事件的尾窗文本：带头一行读数（dropped= 与 total=），正文逐条 Line。</summary>
    public string TailText(int n)
    {
        var sb = new StringBuilder();
        sb.Append($"dropped={EntityNumbers.Format(Dropped)} total={EntityNumbers.Format(Total)} " +
                  $"kept={EntityNumbers.Format(KeptCount)}\n");
        foreach (var e in Tail(n))
        {
            sb.Append(e.Line).Append('\n');
        }

        return sb.ToString();
    }

    public int Count<T>() where T : EntityEventPayload
    {
        var n = 0;
        foreach (var e in _kept)
        {
            if (e is T) n++;
        }

        return n;
    }

    /// <summary>
    /// 清留着的那些（读档与换局调），<b>总数不清</b> —— 发过多少条是历史。
    ///
    /// <para>读档要清的理由不是省内存：留着的话，事件流里那些 <c>spawned</c> 念的是
    /// 上一局的东西，读数上分不出哪一条属于恢复后的这一局。</para>
    /// </summary>
    public void ClearKept() => _kept.Clear();
}
