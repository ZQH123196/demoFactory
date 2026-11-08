#nullable enable
using System;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 四条重算触发，四个位。spec「路径缓存与共享目标」点名的那四条。
///
/// <para>做成位而不是单值：一次重算常常同时踩中好几条（改了地形又超了预算），
/// 单值只能留住一条，剩下的就丢了，而丢了的那条正是事后说不清的那条。
/// 「不记就事后没法说『它重算了 47 次』」—— 记全 + 记次数，才是那句话要的形状。</para>
///
/// <para><b>怎么判是纯函数</b>（<see cref="NavRecomputeProbe"/> 进去、位出来），
/// 不依赖移动解：工单 04 的移动解这一期还不存在，任何「它到底走没走到」的读数今天都拿不到。
/// 判据只吃本层自己有的那些数（两个版本号、目标坐标、上一次的裁决、距上次求解过了几步）。</para>
/// </summary>
[Flags]
public enum NavRecomputeTriggers
{
    /// <summary>没有触发（第一次求解，或上一次之后条件没变）。</summary>
    None = 0,

    /// <summary>静态地形版本变了：寻路网格封 / 解封 / 改静态通过成本各动它（规则见 <see cref="NavGrid.StaticVersion"/>）。</summary>
    StaticVersionChanged = 1 << 0,

    /// <summary>目标移动超过阈值（阈值由宿主递 —— 层里没有单位表，「寻路单入口」 之下不给缺省）。</summary>
    GoalMovedBeyondThreshold = 1 << 1,

    /// <summary>上一次求解是预算触顶：这条路本来就没走通，再要一次也没用，但得记一笔为什么又来一次。</summary>
    BudgetExceeded = 1 << 2,

    /// <summary>上一次求解之后已经过去 <c>N</c> 步而没有新的求解请求。</summary>
    NotArrivedWithinSteps = 1 << 3,
}

/// <summary>
/// 重算触发的判据输入 —— <b>一个纯值快照</b>。判据不持有求解器、不读全局状态，
/// 所以它能在离线测试里逐条钉住（工单 03 验收：四条触发各有一条用例）。
/// </summary>
/// <param name="LastStaticVersion">上一次求解时的静态地形版本。</param>
/// <param name="StaticVersion">这一次的静态地形版本。</param>
/// <param name="LastGoal">上一次求解时的目标点。</param>
/// <param name="Goal">这一次的目标点。</param>
/// <param name="GoalToleranceMeters">目标移动的阈值（米）：位移 ≤ 阈值不记这一条。</param>
/// <param name="PreviousBudgetExceeded">上一次求解是不是因为预算触顶才只回部分路径。</param>
/// <param name="StepsSinceLastSolve">距上一次求解过去了几个逻辑步。</param>
/// <param name="StepCap">「走了 N 步没到」的那个 N。</param>
public readonly record struct NavRecomputeProbe(
    int LastStaticVersion,
    int StaticVersion,
    NavPoint LastGoal,
    NavPoint Goal,
    double GoalToleranceMeters,
    bool PreviousBudgetExceeded,
    int StepsSinceLastSolve,
    int StepCap);

/// <summary>重算触发的判据 —— 一个纯函数，四条各判各的，判完合成位。</summary>
public static class NavRecomputeJudge
{
    /// <summary>
    /// 判这一条重算踩中了哪几条触发。四条各判各的、互不吞并，
    /// 所以「它到底因为什么重算」在求解记录里是一条可数的读数，而不是一个猜。
    /// </summary>
    public static NavRecomputeTriggers Judge(in NavRecomputeProbe probe)
    {
        if (probe.GoalToleranceMeters < 0
            || double.IsNaN(probe.GoalToleranceMeters)
            || double.IsInfinity(probe.GoalToleranceMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(probe),
                probe.GoalToleranceMeters,
                $"目标移动阈值必须是非负有限数，收到 {probe.GoalToleranceMeters.ToString(CultureInfo.InvariantCulture)} —— 阈值是宿主递进来的标量，兜 0 会让任何位移都算「动过了」（「桥只转发」）。");
        }

        if (probe.StepCap < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probe),
                probe.StepCap,
                $"步数上限至少为 1，收到 {probe.StepCap} —— N = 0 意味着「每一步都重算一次」，那不是重算触发，是每步全量重解。");
        }

        var flags = NavRecomputeTriggers.None;

        if (probe.StaticVersion != probe.LastStaticVersion)
        {
            flags |= NavRecomputeTriggers.StaticVersionChanged;
        }

        // 严格大于：位移恰好等于阈值不记 —— 阈值是「多少算动过了」的那条线，
        // 线上不记是钉死的口径（「时钟解耦」 那种逐字可重放的要求）。
        if (probe.LastGoal.DistanceTo(probe.Goal) > probe.GoalToleranceMeters)
        {
            flags |= NavRecomputeTriggers.GoalMovedBeyondThreshold;
        }

        if (probe.PreviousBudgetExceeded)
        {
            flags |= NavRecomputeTriggers.BudgetExceeded;
        }

        if (probe.StepsSinceLastSolve >= probe.StepCap)
        {
            flags |= NavRecomputeTriggers.NotArrivedWithinSteps;
        }

        return flags;
    }
}
