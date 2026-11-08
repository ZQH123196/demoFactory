#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.EventBus;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 事件四档 —— <b>四条都发，无一静默</b>（「事件发射前置」的静默拒绝教训：
/// 没有事件的「没走到」和「压根没人管它」在读数上长得一模一样）。
/// </summary>
public enum NavMoveEventKind
{
    /// <summary>到达：站在本步目标点的到达容差之内。</summary>
    Arrived = 0,

    /// <summary>部分路径：没到目标，但交回一条能走的路 + 原因（预算触顶 / 地形断开）。</summary>
    PartialPath = 1,

    /// <summary>撞墙重算：这一条之所以要重算，是因为地形（见 <see cref="NavRecomputeCause"/>）。</summary>
    Recompute = 2,

    /// <summary>卡住：三类里任意一类，带步数；拥挤那一类带「卡在谁身上」。</summary>
    Stuck = 3,
}

/// <summary>为什么重算 —— <b>只列地形这一族</b>。</summary>
public enum NavRecomputeCause
{
    /// <summary>还没到任何原因（不发这一档事件）。</summary>
    None = 0,

    /// <summary>地形断开：起点到不了目标（求解回部分路径 + <see cref="NavPathReason.GoalUnreachable"/>）。</summary>
    TerrainUnreachable = 1,

    /// <summary>子步撞墙：这一步的某个子步被地形挡下（不隧穿，停在最后一个合法子步）。</summary>
    SubstepBlocked = 2,

    /// <summary>目标那一格进不了可行域：<b>不该启动这个命令</b>（落进地形阻挡，或半径比那格净空还大）。</summary>
    GoalBlocked = 3,

    /// <summary>起点那一格进不了可行域：半径比那格净空还大。</summary>
    StartBlocked = 4,

    /// <summary>墙在这一条逻辑步里被改过（静态地形版本变了）—— 在手路径按版本自动作废。</summary>
    StaticVersionChanged = 5,
}

/// <summary>到达事件。<b>到位那一档就发一次</b>，不是每步都发。</summary>
/// <param name="AgentId">谁的。</param>
/// <param name="StepIndex">第几个逻辑步（从 1 起）。</param>
/// <param name="Position">到位时的位置（米）。</param>
/// <param name="Goal">本步判到达用的那个目标点（米）。</param>
public readonly record struct NavArrivedEvent(string AgentId, long StepIndex, NavPoint Position, NavPoint Goal);

/// <summary>部分路径事件。<b>「是一等结果，不是失败」</b>：交回空数组等于把「走不到位」这个可读事实删掉。</summary>
/// <param name="AgentId">谁的。</param>
/// <param name="StepIndex">第几个逻辑步。</param>
/// <param name="Reason">为什么只走到一半（预算触顶 / 地形断开）。</param>
/// <param name="ExpandedCells">这一条花了多少次扩展。</param>
/// <param name="ReachedAt">部分路径的可达终点（格心）；没有推进到任何一格时是起点那一点。</param>
public readonly record struct NavPartialPathEvent(
    string AgentId,
    long StepIndex,
    NavPathReason Reason,
    int ExpandedCells,
    NavPoint ReachedAt);

/// <summary>撞墙重算事件。</summary>
/// <param name="AgentId">谁的。</param>
/// <param name="StepIndex">第几个逻辑步。</param>
/// <param name="Cause">因为哪一条地形原因要重算。</param>
public readonly record struct NavRecomputeEvent(string AgentId, long StepIndex, NavRecomputeCause Cause);

/// <summary>卡住事件。<b>三类都发</b>，各带步数；拥挤那一类带「卡在谁身上」。</summary>
/// <param name="AgentId">谁的。</param>
/// <param name="StepIndex">第几个逻辑步。</param>
/// <param name="Stuck">三类里的哪一类。</param>
/// <param name="StuckSteps">这一类连续出现了几步。</param>
/// <param name="StuckOn">拥挤那一类：卡在谁身上（<b>那一对的另一个 id</b>）；其余两类是 null。</param>
public readonly record struct NavStuckEvent(
    string AgentId,
    long StepIndex,
    NavStuckKind Stuck,
    int StuckSteps,
    string? StuckOn);

/// <summary>
/// 事件记录 —— <b>一行一条，四档共用一份</b>。
///
/// <para>每条事件同时落两处：<b>强类型通道</b>（表现层与订阅方接）与<b>有界账本</b>（agent 一发读出来）。
/// 只落通道不落账本，「它这一局卡过几次」就只能靠订阅方自己留档；只落账本不落通道，表现层就得轮询。</para>
///
/// <para>逐条追加、有界保留（照 <c>Guard/RollingLog</c> 与本层 <c>NavSolveLedger</c> 的形状）。
/// 读取一律带丢弃数（「三条铁律」 ③：<b>「账本里没有」≠「没发生」</b>）。</para>
/// </summary>
public sealed record NavMoveEventRecord
{
    /// <summary>第几条事件（全局单调，从 1 起，跨步连续；被顶掉的那些也占号）。<b>由账本发</b>，调用方不填。</summary>
    public long Sequence { get; init; }

    /// <summary>四档里的哪一档。</summary>
    public required NavMoveEventKind Kind { get; init; }

    /// <summary>谁的。</summary>
    public required string AgentId { get; init; }

    /// <summary>第几个逻辑步。</summary>
    public required long StepIndex { get; init; }

    /// <summary>这一条的说法（invariant，「double 与不变文化」）。</summary>
    public required string Text { get; init; }

    /// <summary>逐字可读的一行。</summary>
    public string Describe()
        => $"#{Sequence.ToString(CultureInfo.InvariantCulture)}"
           + $" {Kind}"
           + $" {AgentId}"
           + $" 第{StepIndex.ToString(CultureInfo.InvariantCulture)}步"
           + $" {Text}";

    /// <inheritdoc />
    public override string ToString() => Describe();
}

/// <summary>
/// 事件账本 —— 环形保留最近 <c>capacity</c> 条，超出顶掉最老的一条。
///
/// <para><b>按下标寻址</b>，与 <see cref="NavSolveLedger"/> 同一份形状：没有 <c>foreach</c>、
/// 没有字典或集合的枚举（spec「确定性靠结构」），所以「第 k 条是哪一条」是定死的。</para>
/// </summary>
public sealed class NavEventLedger
{
    private readonly NavMoveEventRecord[] _rows;
    private int _next;
    private int _stored;
    private long _sequence;

    /// <summary>造一个有界账本。容量至少 1，当场抛（「桥只转发」）。</summary>
    public NavEventLedger(int capacity)
    {
        NavSolveCheck.Capacity(capacity, nameof(capacity));
        _rows = new NavMoveEventRecord[capacity];
    }

    /// <summary>容量（最多留多少条）。</summary>
    public int Capacity => _rows.Length;

    /// <summary>现在存着多少条（未满时是写过的条数）。</summary>
    public int Count => _stored;

    /// <summary>被顶掉（因账本满而丢失）的条数。<b>读取时必看这一列</b>：非 0 就说明账本里那一段已经没了。</summary>
    public int Dropped => (int)(_sequence - _stored);

    /// <summary>写进去过多少条（含被顶掉的）。</summary>
    public long Total => _sequence;

    /// <summary>最近那一条。没有记录时是 null（<b>不</b>抛 —— 「还没发生过」是一个正常状态）。</summary>
    public NavMoveEventRecord? Last => _stored == 0 ? null : _rows[(_next + _rows.Length - 1) % _rows.Length];

    /// <summary>按写入次序取第 <paramref name="index"/> 条（0 = 最老的那条仍在的那条）。下标越界当场抛。</summary>
    public NavMoveEventRecord At(int index)
    {
        if (index < 0 || index >= _stored)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"事件账本里只有 {_stored.ToString(CultureInfo.InvariantCulture)} 条（容量 {_rows.Length.ToString(CultureInfo.InvariantCulture)}），"
                + $"下标 {index} 越界 —— 越界是调用方的错，不是一个该被兜住的读数。");
        }

        var start = _stored == _rows.Length ? _next : 0;
        return _rows[(start + index) % _rows.Length];
    }

    /// <summary>按写入次序摊平成新数组（老的在前）。遍历数组下标，不碰哈希桶的枚举序。</summary>
    public NavMoveEventRecord[] Snapshot()
    {
        var copy = new NavMoveEventRecord[_stored];
        for (var i = 0; i < _stored; i++)
        {
            copy[i] = At(i);
        }

        return copy;
    }

    /// <summary>追加一条。<b>序号在这里发，跨步连续。</b></summary>
    internal NavMoveEventRecord Append(NavMoveEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _sequence++;
        _rows[_next] = record with { Sequence = _sequence };
        _next = (_next + 1) % _rows.Length;
        if (_stored < _rows.Length)
        {
            _stored++;
        }

        return _rows[(_next + _rows.Length - 1) % _rows.Length];
    }
}

/// <summary>读出来的一截 —— <b>必带丢弃数</b>（「三条铁律」 ③）。</summary>
/// <param name="Rows">按发生次序摊平的那一截（老的在前）。</param>
/// <param name="Dropped">因为账本满而丢掉的条数。</param>
/// <param name="Total">一共发生过多少条。</param>
public readonly record struct NavEventReadout(IReadOnlyList<NavMoveEventRecord> Rows, int Dropped, long Total);

/// <summary>
/// 寻路层的<b>事件出口</b> —— 四条强类型通道 + 一本有界账本。
///
/// <para>通道是 <see cref="EventChannel{T}"/>（「C# 优先与总线」：系统间通信走强类型通道，不直接引用耦合）。
/// 四档分别是 <b>到达 / 部分路径 / 撞墙重算 / 卡住</b>，<b>无一静默</b>：
/// 「事件发射前置」那条静默拒绝的教训就出在同一种病上 —— 「没走到」没有事件，
/// 于是它与「压根没人管它」在读数上一样，排查只能出图肉眼看。</para>
///
/// <para>事件<b>只发不发判断</b>：这一层答「发生了什么」，不答「上层该怎么办」
/// （拥挤推挤那一档的处置是上层决定是抗议还是让路，<c>EX-NAV-01</c>）。</para>
/// </summary>
public sealed class NavMoveEvents
{
    /// <summary>造事件出口。<paramref name="ledgerCapacity"/> 至少 1，当场抛。</summary>
    public NavMoveEvents(int ledgerCapacity = 256)
    {
        Ledger = new NavEventLedger(ledgerCapacity);
    }

    /// <summary>到达。</summary>
    public EventChannel<NavArrivedEvent> Arrived { get; } = new();

    /// <summary>部分路径（没到目标但交回了能走的路）。</summary>
    public EventChannel<NavPartialPathEvent> PartialPath { get; } = new();

    /// <summary>撞墙重算。</summary>
    public EventChannel<NavRecomputeEvent> Recompute { get; } = new();

    /// <summary>卡住（三类，各带步数；拥挤那一类带「卡在谁身上」）。</summary>
    public EventChannel<NavStuckEvent> Stuck { get; } = new();

    /// <summary>有界账本（照 <c>Guard/RollingLog</c> 的形状：环形 + 丢弃数）。</summary>
    public NavEventLedger Ledger { get; }

    /// <summary>
    /// 读最近 <paramref name="tail"/> 条，<b>连丢弃数一起交回</b>。
    /// 「账本里没有」与「没发生」只有在 <c>Dropped == 0</c> 时才是同一件事。
    /// </summary>
    public NavEventReadout Read(int tail)
    {
        NavSolveCheck.Capacity(tail, nameof(tail));
        var all = Ledger.Snapshot();
        var take = Math.Min(tail, all.Length);
        var start = all.Length - take;
        var rows = new NavMoveEventRecord[take];
        for (var i = 0; i < take; i++)
        {
            rows[i] = all[start + i];
        }

        return new NavEventReadout(rows, Ledger.Dropped, Ledger.Total);
    }

    /// <summary>发一条到达：<b>通道 + 账本两处都落</b>，缺一处就有一半的读数是空的。</summary>
    internal void EmitArrived(in NavArrivedEvent e)
    {
        Ledger.Append(new NavMoveEventRecord
        {
            Kind = NavMoveEventKind.Arrived,
            AgentId = e.AgentId,
            StepIndex = e.StepIndex,
            Text = $"到位 位置 {e.Position} 目标 {e.Goal}",
        });

        Arrived.Publish(e);
    }

    /// <summary>发一条部分路径。</summary>
    internal void EmitPartialPath(in NavPartialPathEvent e)
    {
        Ledger.Append(new NavMoveEventRecord
        {
            Kind = NavMoveEventKind.PartialPath,
            AgentId = e.AgentId,
            StepIndex = e.StepIndex,
            Text = $"部分路径 {e.Reason} 扩展 {e.ExpandedCells.ToString(CultureInfo.InvariantCulture)} 走到 {e.ReachedAt}",
        });

        PartialPath.Publish(e);
    }

    /// <summary>发一条撞墙重算。</summary>
    internal void EmitRecompute(in NavRecomputeEvent e)
    {
        Ledger.Append(new NavMoveEventRecord
        {
            Kind = NavMoveEventKind.Recompute,
            AgentId = e.AgentId,
            StepIndex = e.StepIndex,
            Text = $"撞墙重算 {CauseText(e.Cause)}",
        });

        Recompute.Publish(e);
    }

    /// <summary>发一条卡住。<b>三档都从这里过</b>，说法带类别与步数，拥挤那一类还带「卡在谁身上」。</summary>
    internal void EmitStuck(in NavStuckEvent e)
    {
        Ledger.Append(new NavMoveEventRecord
        {
            Kind = NavMoveEventKind.Stuck,
            AgentId = e.AgentId,
            StepIndex = e.StepIndex,
            Text = $"卡住 {StuckText(e.Stuck)}/{e.StuckSteps.ToString(CultureInfo.InvariantCulture)}"
                   + (e.StuckOn is null ? string.Empty : $" 卡在 {e.StuckOn} 身上"),
        });

        Stuck.Publish(e);
    }

    private static string CauseText(NavRecomputeCause cause) => cause switch
    {
        NavRecomputeCause.TerrainUnreachable => "地形断开",
        NavRecomputeCause.SubstepBlocked => "子步撞墙",
        NavRecomputeCause.GoalBlocked => "目标进不了可行域",
        NavRecomputeCause.StartBlocked => "起点进不了可行域",
        NavRecomputeCause.StaticVersionChanged => "墙在这一步被改过",
        _ => "无",
    };

    private static string StuckText(NavStuckKind stuck) => stuck switch
    {
        NavStuckKind.NoTerrainRoute => "地形无路",
        NavStuckKind.Crowded => "拥挤推挤",
        NavStuckKind.BudgetCapped => "预算触顶",
        _ => "无",
    };
}
