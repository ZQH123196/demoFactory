#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace DemoFactory.GugaFramework.Construction;

// 建造模块的事件出口。形状口径照 ADR 0001（AbilityEvents 的先例）：一个 kind 一个类型、
// 没有 Kind 枚举、没有需要背的第二张表，Wire 与那行明细由类型自己给。
//
// 与技能层先例的一处不同、且是有意的：<b>步号住在事件类型自己身上</b>，不另包一层
// （技能层是 AbilityEvent(Tick, Subject, Payload) 的 struct）。理由是这一族有两股流 ——
// 世界事件进日志、悬停裁决直接推 —— 两股都要自带步号；步号包在 struct 里的话悬停那条
// 就得再包一次，同一个字段两处落。字段改成 init 属性（不是位置参数）是为了让派生类型
// 继承 Tick / Subject 而不是遮蔽它们（遮蔽会出 CS0108 警告）。

/// <summary>数字与名单的唯一格式化处。事件行、快照段、动词返回的读数都走这一份（「double 与不变文化」）。</summary>
public static class ConstructionNumbers
{
    public static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>格名单（快照与事件行里那一串）。空表印 <c>-</c>，别印空串（空串读起来像没给）。</summary>
    public static string Cells(IEnumerable<GridCell> cells)
    {
        var any = false;
        var sb = new StringBuilder();
        foreach (var c in cells)
        {
            if (any)
            {
                sb.Append(' ');
            }

            sb.Append(c.ToString());
            any = true;
        }

        return any ? sb.ToString() : "-";
    }

    /// <summary>建筑 id 名单（批量事件的成员名单）。</summary>
    public static string Ids(IEnumerable<string> ids)
    {
        var any = false;
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            if (any)
            {
                sb.Append(',');
            }

            sb.Append(id);
            any = true;
        }

        return any ? sb.ToString() : "-";
    }

    /// <summary>gizmo 只读参数的渲染。按键名排一遍序，不靠字典的遍历顺序（D29 的数要逐字稳定）。</summary>
    public static string Gizmo(IReadOnlyDictionary<string, double> parameters)
    {
        var keys = new List<string>(parameters.Keys);
        keys.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(keys[i]).Append('=').Append(Format(parameters[keys[i]]));
        }

        return sb.Length == 0 ? "-" : sb.ToString();
    }

    /// <summary>坐标（网格落点、自由量化点都走它）。</summary>
    public static string Point(Vector2 v) => $"{Format(v.X)},{Format(v.Y)}";
}

/// <summary>移除的去向（D17）：摧毁不是状态，是去向的一种。</summary>
public enum RemovalCause
{
    /// <summary>拆除（默认段）。</summary>
    Dismantle = 0,

    /// <summary>取消（施工中撤单，退款政策归原型）。</summary>
    Cancel = 1,

    /// <summary>摧毁（血量归零自动走的那条）。</summary>
    Destroyed = 2,
}

/// <summary>
/// 一类建造事件携带的字段集合。<b>只有模块能发</b>（spec 3 节第 3 条）：
/// 表现层读它、订阅它，但没有任何途径造一条出来。
/// </summary>
public abstract record ConstructionEventPayload
{
    /// <summary>第几步。构造时递进来的时钟读数（D27）。</summary>
    public long Tick { get; init; }

    /// <summary>动的是哪座建筑（拒绝那条是「想放的那个蓝图」）。</summary>
    public string Subject { get; init; } = "";

    /// <summary>事件行里的第二段名字。名单只在这里。</summary>
    public abstract string Wire { get; }

    /// <summary>事件行的明细段：字段渲染出来的那串字，供人读与抽查，不是接口。</summary>
    public abstract string Detail { get; }

    /// <summary>给人读与供抽查的那一行字。真值在字段里，不在这里（快照另有一份）。</summary>
    public string Line => $"{ConstructionNumbers.Format(Tick)} {Wire} {Subject} {Detail}";
}

// ---- 放置 ---------------------------------------------------------------------

/// <summary>落成一座，位置与占地就是裁决给的那份（Placed，D30）。</summary>
public sealed record BuildingPlaced : ConstructionEventPayload
{
    public override string Wire => "placed";

    public string BlueprintId { get; init; } = "";

    public Vector2 Position { get; init; }

    public int Orientation { get; init; }

    public IReadOnlyList<GridCell> Cells { get; init; } = Array.Empty<GridCell>();

    public override string Detail
        => $"{BlueprintId} pos={ConstructionNumbers.Point(Position)} dir={Orientation} " +
            $"cells={ConstructionNumbers.Cells(Cells)}";
}

/// <summary>放不下去（Rejected，D30）。<b>每次拒绝都是原型「不扣款」的信号</b>（D6）。</summary>
public sealed record PlacementRejected : ConstructionEventPayload
{
    public override string Wire => "rejected";

    public PlacementReason Reason { get; init; }

    public override string Detail => $"reason={Reason}";
}

//// <summary>
/// 登记了一座场地建筑（Registered，D30）。<b>不是一次放置手势</b>：没有候选点、没有校验、
/// 也就没有对应的 Rejected —— 它是关卡摆位，不是玩家点下去的那一下。
///
/// <para><b>工期 0 的场地建筑不发 Completed</b>，理由写在 <see cref="BuildProgress.Finished"/>：
/// 「建成」这一发对它不存在，发出去只会逼订阅者各写一个例外分支。</para>
/// </summary>
public sealed record BuildingRegistered : ConstructionEventPayload
{
    public override string Wire => "registered";

    public string BlueprintId { get; init; } = "";

    public Vector2 Position { get; init; }

    public int Orientation { get; init; }

    public IReadOnlyList<GridCell> Cells { get; init; } = Array.Empty<GridCell>();

    public override string Detail
        => $"{BlueprintId} pos={ConstructionNumbers.Point(Position)} dir={Orientation} " +
            $"cells={ConstructionNumbers.Cells(Cells)}";
}

// ---- 建造过程（进度与血量，ticket 02 起发）------------------------------------

/// <summary>建成（Completed，D30）。工期 0 时与 Placed 在同一次 Commit 里连发，顺序 Placed 先（D16）。</summary>
public sealed record BuildingCompleted : ConstructionEventPayload
{
    public override string Wire => "completed";

    public override string Detail => "done";
}

/// <summary>挨了一下（Damaged，D30）。</summary>
public sealed record BuildingDamaged : ConstructionEventPayload
{
    public override string Wire => "damaged";

    public double Amount { get; init; }

    public double Remaining { get; init; }

    public override string Detail
        => $"amount={ConstructionNumbers.Format(Amount)} remaining={ConstructionNumbers.Format(Remaining)}";
}

/// <summary>补了一点血，钳在血量上限（D19）。</summary>
public sealed record BuildingRepaired : ConstructionEventPayload
{
    public override string Wire => "repaired";

    public double Amount { get; init; }

    public double Remaining { get; init; }

    public override string Detail
        => $"amount={ConstructionNumbers.Format(Amount)} remaining={ConstructionNumbers.Format(Remaining)}";
}

/// <summary>
/// 血量被设成绝对值，且血量上限随之被合法抬高（SetHealth，D19 的补充决定）。
///
/// <para><b>明细里带上限而 Damaged / Repaired 不带</b>：那两条的上限从不改，只有这一条会改，
/// 而订阅者要判断「这次写完这座还能不能再修」就得读到新的上限 —— 事件行里少这一列，
/// 表现层就只能自己去册里查，等于又多一处读法。</para>
///
/// <para><b>不发「没变化」那一档</b>：设成与当前同一个数照样发这一条。事件记录的是
/// 「这一次写发生了」，不是「世界变了没有」；订阅者要判断变化与否，比的是前后两个数。</para>
/// </summary>
public sealed record BuildingHealthSet : ConstructionEventPayload
{
    public override string Wire => "health_set";

    public double Health { get; init; }

    public double MaxHealth { get; init; }

    public override string Detail
        => $"hp={ConstructionNumbers.Format(Health)}/{ConstructionNumbers.Format(MaxHealth)}";
}

/// <summary>施工暂停（BuildPaused，D30）。模块不问为什么暂停（D18）。</summary>
public sealed record BuildPaused : ConstructionEventPayload
{
    public override string Wire => "build_paused";

    public long RemainingTicks { get; init; }

    public override string Detail => $"remaining={ConstructionNumbers.Format(RemainingTicks)}";
}

/// <summary>施工恢复（BuildResumed，D30）。</summary>
public sealed record BuildResumed : ConstructionEventPayload
{
    public override string Wire => "build_resumed";

    public long RemainingTicks { get; init; }

    public override string Detail => $"remaining={ConstructionNumbers.Format(RemainingTicks)}";
}

// ---- 移除与编辑 ---------------------------------------------------------------

/// <summary>移走一座，带去向（Removed，D30）。腾占用与这一条同一次调用里做完。</summary>
public sealed record BuildingRemoved : ConstructionEventPayload
{
    public override string Wire => "removed";

    public RemovalCause Cause { get; init; }

    public override string Detail => $"cause={Cause}";
}

/// <summary>挪了位置（Moved，D30）。id / 载荷 / 血量 / 进度全保留（D20）。</summary>
public sealed record BuildingMoved : ConstructionEventPayload
{
    public override string Wire => "moved";

    public Vector2 From { get; init; }

    public Vector2 To { get; init; }

    public int Orientation { get; init; }

    public override string Detail
        => $"from={ConstructionNumbers.Point(From)} to={ConstructionNumbers.Point(To)} dir={Orientation}";
}

/// <summary>原地转了方向（Rotated，D30）。撞邻居就拒绝，一寸不动（D21）。</summary>
public sealed record BuildingRotated : ConstructionEventPayload
{
    public override string Wire => "rotated";

    public int Orientation { get; init; }

    public IReadOnlyList<GridCell> Cells { get; init; } = Array.Empty<GridCell>();

    public override string Detail
        => $"dir={Orientation} cells={ConstructionNumbers.Cells(Cells)}";
}

// ---- 批量与区域蓝图（ticket 04 起发）-------------------------------------------

/// <summary>布局放下：成员逐个 Placed 之后的一条汇总（D26）。</summary>
public sealed record LayoutPlaced : ConstructionEventPayload
{
    public override string Wire => "layout_placed";

    public string LayoutName { get; init; } = "";

    public string Members { get; init; } = "-";

    public override string Detail => $"{LayoutName} members={Members}";
}

/// <summary>布局整体被拒：一条汇总，成员零事件（D26）。</summary>
public sealed record LayoutRejected : ConstructionEventPayload
{
    public override string Wire => "layout_rejected";

    public string Failures { get; init; } = "-";

    public override string Detail => $"failures={Failures}";
}

/// <summary>画线 / 画矩形放完：成功几个、失败几格连原因一起给（D24、D26，<b>部分成功</b>语义）。</summary>
public sealed record BatchPlaced : ConstructionEventPayload
{
    public override string Wire => "batch_placed";

    public GestureShape Shape { get; init; }

    public string Placed { get; init; } = "-";

    public string Failures { get; init; } = "-";

    public override string Detail => $"{Shape} placed={Placed} failed={Failures}";
}

/// <summary>
/// 事件流的存放处：有界环形，丢最旧的。读数口只有这一份 —— 尾窗、按形状过滤、按形状计数，
/// 以及「被丢了多少」这个信号（与 AbilityEventLog 同一个读法：没有 ≠ 没发生，「三条铁律」 ③）。
///
/// 容量构造时给，不给第二份改法：两份容量等于两份实现。
/// </summary>
public sealed class ConstructionEventLog
{
    private readonly LinkedList<ConstructionEventPayload> _kept = new();

    public ConstructionEventLog(int capacity)
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

    /// <summary>被环形挤出去的条数。读数里那一行 dropped= 就是它。</summary>
    public long Dropped => Total - _kept.Count;

    public int KeptCount => _kept.Count;

    public void Add(ConstructionEventPayload e)
    {
        Total++;
        _kept.AddLast(e);
        while (_kept.Count > Capacity)
        {
            _kept.RemoveFirst();
        }
    }

    /// <summary>全部还留着的事件，按发出顺序。</summary>
    public IReadOnlyList<ConstructionEventPayload> All
        => _kept.Count == 0 ? Array.Empty<ConstructionEventPayload>() : new List<ConstructionEventPayload>(_kept);

    /// <summary>最近 n 条（不足 n 就给全部）。</summary>
    public IReadOnlyList<ConstructionEventPayload> Tail(int n)
    {
        if (n <= 0)
        {
            return Array.Empty<ConstructionEventPayload>();
        }

        var take = Math.Min(n, _kept.Count);
        var from = _kept.Count - take;
        var i = 0;
        var buf = new ConstructionEventPayload[take];
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
        sb.Append($"dropped={ConstructionNumbers.Format(Dropped)} total={ConstructionNumbers.Format(Total)} " +
            $"kept={ConstructionNumbers.Format(KeptCount)}\n");
        foreach (var e in Tail(n))
        {
            sb.Append(e.Line).Append('\n');
        }

        return sb.ToString();
    }

    public IEnumerable<T> Where<T>() where T : ConstructionEventPayload
    {
        foreach (var e in _kept)
        {
            if (e is T typed)
            {
                yield return typed;
            }
        }
    }

    public int Count<T>() where T : ConstructionEventPayload
    {
        var n = 0;
        foreach (var e in _kept)
        {
            if (e is T)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>清场（分支与重建用）：只清留着的，总数不清 —— 发过多少条是历史。</summary>
    public void ClearKept() => _kept.Clear();
}
