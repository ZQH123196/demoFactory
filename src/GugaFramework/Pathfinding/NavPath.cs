#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>一次求解的裁决：走通了 / 只走到一半 / 压根没起步。</summary>
public enum NavPathStatus
{
    /// <summary>走通到目标。</summary>
    Reached = 0,

    /// <summary>
    /// 部分路径：没到目标，但交回一条能走的路 + 原因。
    /// 「是一等结果，不是失败」（见 <c>GLOSSARY.md</c> 11 节）——
    /// 上层要拿它做「接近到 2 米内就出手」这类决策，交回空数组等于把决策的前提删掉。
    /// </summary>
    Partial = 1,

    /// <summary>压根没起步：起点或目标不进可行域，不该启动这个命令。</summary>
    Failed = 2,
}

/// <summary>为什么是这个裁决。每一档都能在求解记录里数出来（spec「卡住分三类」）。</summary>
public enum NavPathReason
{
    /// <summary>走通了，无异议。</summary>
    Reached = 0,

    /// <summary>扩展到上限被截断 —— spec 那句「回部分路径而不是空」的落点。</summary>
    BudgetExceeded = 1,

    /// <summary>地形断开了：起点到不了目标。交回部分路径（走到的最远处），不交回空。</summary>
    GoalUnreachable = 2,

    /// <summary>目标那一格进不了可行域：落进地形阻挡，或半径比那格净空还大。</summary>
    GoalBlocked = 3,

    /// <summary>起点那一格进不了可行域：半径比那格净空还大。</summary>
    StartBlocked = 4,
}

/// <summary>
/// 一次求解的结果 —— <b>一条路</b>加一句裁决。
///
/// <para><b>只给一条路，不给「下一步站哪」</b>：那是移动解的事（工单 04），
/// 「寻路单入口」 之下这一层答的是「怎么求一条路」（seam），两者分开的理由是
/// 前者每步都要答、后者一个请求答一次。</para>
///
/// <para><b><see cref="CellPath"/> 与 <see cref="Corners"/> 一起交回来</b>：前者是拉直前的格子路径，
/// 后者是拉直后的任意角折线。agent 问「为什么绕了这么远」时，先看它经过哪几格，
/// 再看折线在哪几处拐弯 —— 只给折线就把「绕远」与「本来就远」混成同一件事。</para>
///
/// <para><b>折线永不交回格子阶梯</b>（拉直的判据见 <c>NavPathStraightener</c>）：
/// 拐点交的是格心，段与段之间是直线，拐点处朝向连续。</para>
/// </summary>
public sealed record NavPath
{
    /// <summary>拉直后的任意角折线：第一个点是请求的起点，最后一个点是请求的目标点（两者逐字照收，不换成格心）。</summary>
    public required IReadOnlyList<NavPoint> Corners { get; init; }

    /// <summary>拉直前的格子路径（从起点所在格到目标所在格，逐格一个格坐标）。</summary>
    public required IReadOnlyList<NavCell> CellPath { get; init; }

    /// <summary>裁决。</summary>
    public required NavPathStatus Status { get; init; }

    /// <summary>为什么是这个裁决。</summary>
    public required NavPathReason Reason { get; init; }

    /// <summary>
    /// 总成本 = 逐段（米 × 到达格的通过成本）累加。单位是加权米，不是米 ——
    /// 报一个不带口径的数，agent 拿它跟别的路径比会得到错的结论。
    /// </summary>
    public required double TotalCost { get; init; }

    /// <summary>扩展了多少格。缓存命中交回的那条是 <b>0</b>：命中不许复算，账本里数得出来。</summary>
    public required int ExpandedCells { get; init; }

    /// <summary>
    /// 部分路径的可达终点（那一格的格心）。走通时为 null。
    /// 「走不到位」是哪一个位置到位，这是排查的第一个读数。
    /// </summary>
    public NavPoint? PartialAt { get; init; }

    /// <summary>拐点数（<see cref="Corners"/> 的长度）。零段与一段都读得出来。</summary>
    public int CornerCount => Corners.Count;

    /// <summary>是不是部分路径（没到目标但交回了能走的路）。</summary>
    public bool IsPartial => Status == NavPathStatus.Partial;

    /// <summary>是否因为预算触顶而回的部分路径（spec「卡住分三类」里那一类）。</summary>
    public bool IsBudgetTruncated => Status == NavPathStatus.Partial && Reason == NavPathReason.BudgetExceeded;

    /// <summary>
    /// 逐字可读的一行 —— 折线本身。invariant（「double 与不变文化」），
    /// 逐字读数是重放落到 L3 那一档的凭据（「断言三层」）。
    /// </summary>
    public string DescribeCorners()
    {
        var parts = new string[Corners.Count];
        for (var i = 0; i < Corners.Count; i++)
        {
            parts[i] = Corners[i].ToString();
        }

        return string.Join(" -> ", parts);
    }

    /// <inheritdoc />
    public override string ToString()
        => $"{Status}/{Reason} 折线 {CornerCount.ToString(CultureInfo.InvariantCulture)} 点 {DescribeCorners()}"
           + $" 扩展 {ExpandedCells.ToString(CultureInfo.InvariantCulture)} 格 总成本 {TotalCost.ToString(CultureInfo.InvariantCulture)}";
}
