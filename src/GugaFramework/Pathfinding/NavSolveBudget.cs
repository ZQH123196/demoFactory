#nullable enable
using System;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 预算 —— spec「预算」一节那两档。<b>公共层一个缺省都不给</b>：
/// 上限多少是宿主的决定（宿主知道这一局有多少个单位、每步能花多少），
/// 层里藏一个缺省会变成「为什么这个单位突然不走了」查不到出处的第二类原因。
/// </summary>
public sealed record NavSolveBudget
{
    /// <summary>单个请求最多扩展多少格。<b>触顶回部分路径 + 原因 <see cref="NavPathReason.BudgetExceeded"/>，不抛不返空。</b></summary>
    public required int PerRequestExpansions { get; init; }
}

/// <summary>
/// 重算触发的两个阈值。递进来而不是给缺省，理由同 <see cref="NavSolveBudget"/>：
/// 「目标动了多远算动了」与「几步没到算没到」都是这一局的规模问题，不是层的性质。
/// </summary>
public sealed record NavRecomputeTuning
{
    /// <summary>目标移动的阈值（米）。位移 ≤ 阈值不记「目标移动」那条触发。</summary>
    public required double GoalMoveToleranceMeters { get; init; }

    /// <summary>「走了 N 步没到」的那个 N（至少 1）。</summary>
    public required int NotArrivedStepCap { get; init; }
}

/// <summary>预算档的入参校验，集中在这一处（口径同 <c>NavGridSpecCheck</c>）。</summary>
internal static class NavSolveCheck
{
    /// <summary>扩展上限：至少 1。0 会让每个请求都立刻触顶，那是一条恒为真的读数。</summary>
    public static void Expansions(int value, string name)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"扩展上限至少为 1，收到 {value.ToString(CultureInfo.InvariantCulture)} —— 0 扩展让每个请求都触顶回一条只有一个点的部分路径，"
                + "「没走到」这件事就再也读不出是地形问题还是预算问题。");
        }
    }

    /// <summary>容量（有界账本 / 有界请求追踪）：至少 1。</summary>
    public static void Capacity(int value, string name)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"容量至少为 1，收到 {value.ToString(CultureInfo.InvariantCulture)} —— 容量 0 的账本记不下任何一行，"
                + "而「请求了但没到」要成为可读事实，第一前提是那一行真的在。");
        }
    }

    /// <summary>半径：非负、为有限数。0 是合法值（一个不占地方的探针单位），负数与非有限数当场抛。</summary>
    public static void Radius(double value, string name)
    {
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"半径必须是非负有限数，收到 {value.ToString(CultureInfo.InvariantCulture)} —— 半径是请求递进来的标量，层里没有单位表，"
                + "兜 0 等于「所有单位都算零半径」，负半径则让可行域算成空的（「桥只转发」）。");
        }
    }
}
