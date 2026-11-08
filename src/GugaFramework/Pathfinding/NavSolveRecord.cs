#nullable enable
using System;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 求解记录 —— <b>一次寻路请求的裁决</b>（spec user story 4、5；<c>GLOSSARY.md</c> 11 节）。
///
/// <para><b>不是行为流</b>：行为流记的是单位的决策，求解记录记的是计算。
/// 有了它，「这个单位为什么没走到那儿」从出图肉眼看变成一发读数 ——
/// <see cref="Status"/> 说走没走通，<see cref="Reason"/> 说卡在哪一类，
/// <see cref="ExpandedCells"/> 说算了多少，<see cref="CacheHit"/> 说是不是白要的，
/// <see cref="RecomputeTriggers"/> 说上一次为什么又来一次。</para>
///
/// <para>逐条追加、有界保留（<see cref="NavSolveLedger"/>）。日志文本一律 invariant（「double 与不变文化」）。</para>
/// </summary>
public sealed record NavSolveRecord
{
    /// <summary>
    /// 第几次求解（全局单调，从 1 起）。跨步、跨请求连续，所以「第几步之后开始不对」是一条读数。
    /// <b>由账本发</b>（<see cref="NavSolveLedger.Append"/>），调用方不填 —— 序号是账本的职责，
    /// 让调用方也填就多一个能填错的数字。
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>请求落在这一个空间里。</summary>
    public required SpaceId Space { get; init; }

    /// <summary>请求的起点（米）。</summary>
    public required NavPoint Start { get; init; }

    /// <summary>请求的目标点（米）。</summary>
    public required NavPoint Goal { get; init; }

    /// <summary>请求递进来的半径（米）。</summary>
    public required double RadiusMeters { get; init; }

    /// <summary>这一次的静态地形版本。</summary>
    public required int StaticVersion { get; init; }

    /// <summary>这一次的动态代价版本。</summary>
    public required int DynamicVersion { get; init; }

    /// <summary>裁决。</summary>
    public required NavPathStatus Status { get; init; }

    /// <summary>为什么是这个裁决。</summary>
    public required NavPathReason Reason { get; init; }

    /// <summary>是不是部分路径。</summary>
    public required bool IsPartial { get; init; }

    /// <summary>扩展了多少格。缓存命中那一行是 <b>0</b> —— 命中不许复算，账本里数得出来。</summary>
    public required int ExpandedCells { get; init; }

    /// <summary>总成本（加权米，见 <see cref="NavPath.TotalCost"/>）。</summary>
    public required double TotalCost { get; init; }

    /// <summary>拉直后的拐点数。</summary>
    public required int CornerCount { get; init; }

    /// <summary>拉直前的格数（<see cref="NavPath.CellPath"/> 的长度）。</summary>
    public required int CellCount { get; init; }

    /// <summary>用了哪一档预算：<b>每请求的扩展上限</b>（宿主配置的那个数）。</summary>
    public required int BudgetPerRequest { get; init; }

    /// <summary>这一条花完之后，<b>本步全局</b>还剩多少次扩展。下次触顶的预测看它。</summary>
    public required int BudgetStepRemainingAfter { get; init; }

    /// <summary>缓存命中与否。<b>false = 真算了一遍</b>。</summary>
    public required bool CacheHit { get; init; }

    /// <summary>这一次重算踩中了哪几条触发（第一次求解是 <see cref="NavRecomputeTriggers.None"/>）。</summary>
    public required NavRecomputeTriggers RecomputeTriggers { get; init; }

    /// <summary>同一请求身份（空间 + 起点 + 目标 + 宽度，<b>不含两个版本</b>）这是第几次求解。</summary>
    public required int RecomputeCount { get; init; }

    /// <summary>部分路径的可达终点（格心）；走通时为 null。</summary>
    public NavPoint? PartialAt { get; init; }

    /// <summary>
    /// 逐字可读的一行。逐字是这一层的验收口径（「断言三层」 的 L3 那一档），
    /// 所以每个数都走 <see cref="CultureInfo.InvariantCulture"/>。
    /// </summary>
    public string Describe()
    {
        var goal = "无";
        if (PartialAt is { } at)
        {
            goal = at.ToString();
        }

        return $"#{Sequence.ToString(CultureInfo.InvariantCulture)}"
               + $" {Space}"
               + $" 起点 {Start.ToString()} 目标 {Goal.ToString()} 半径 {RadiusMeters.ToString(CultureInfo.InvariantCulture)}"
               + $" [{StaticVersion.ToString(CultureInfo.InvariantCulture)}/{DynamicVersion.ToString(CultureInfo.InvariantCulture)}]"
               + $" {Status.ToString()}/{Reason.ToString()}"
               + $" 部分 {(IsPartial ? "是" : "否")}"
               + $" 扩展 {ExpandedCells.ToString(CultureInfo.InvariantCulture)}"
               + $" 总成本 {TotalCost.ToString(CultureInfo.InvariantCulture)}"
               + $" 拐点 {CornerCount.ToString(CultureInfo.InvariantCulture)} 格路 {CellCount.ToString(CultureInfo.InvariantCulture)}"
               + $" 预算 每请求 {BudgetPerRequest.ToString(CultureInfo.InvariantCulture)}"
               + $" 本步剩 {BudgetStepRemainingAfter.ToString(CultureInfo.InvariantCulture)}"
               + $" 缓存 {(CacheHit ? "命中" : "未命中")}"
               + $" 触发 {RecomputeTriggers.ToString()}"
               + $" 重算第 {RecomputeCount.ToString(CultureInfo.InvariantCulture)} 次"
               + $" 到位 {goal}";
    }

    /// <inheritdoc />
    public override string ToString() => Describe();
}
