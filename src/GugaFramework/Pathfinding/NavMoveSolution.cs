#nullable enable
using System;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 移动解的<b>状态</b> —— 这一步它是哪一种。（卡住类别是另一个读数，见 <see cref="NavStuckKind"/>。）
///
/// <para>五档都在：原地不动与到达是两件事（命令是原地不动 vs 走完了一段路站在终点），
/// 而「激活检查不放行」与「地形无路」更是两回事 —— 后者该由上层改地形或改命令，
/// 前者只该由技能层去解定身。</para>
/// </summary>
public enum NavMoveStatus
{
    /// <summary>还没发令：注册之后、第一条命令之前。不产位移，<b>但不是「原地不动」</b>。</summary>
    Unset = 0,

    /// <summary>原地不动：命令是原地不动，且已经站在驻守点上。</summary>
    Holding = 1,

    /// <summary>到了：站在本步目标点的到达容差之内。</summary>
    Arrived = 2,

    /// <summary>在走：这一步产出了位移。</summary>
    Moving = 3,

    /// <summary>地形无路：两端进不了可行域、地形断开、或一步之内某个子步撞墙。</summary>
    NoTerrainRoute = 4,

    /// <summary>移动激活检查不放行（<b>本期不接技能层</b>，工单 08 在 <see cref="NavStepper"/> 的那道口接）。</summary>
    HeldByActivationCheck = 5,
}

/// <summary>
/// 卡住类别 —— <b>spec「卡住分三类」那三类</b>，外加一个「无」。
///
/// <para>三类各带步数（<see cref="NavMoveSolution.StuckSteps"/>），<b>混成一个 <c>Stuck</c> 就把定位打回出图肉眼看</b>
/// （「可调试性优先」）：地形无路该由上层改地形或改命令、拥挤推挤该决定是抗议还是让路
/// （<c>EX-NAV-01</c>）、预算触顶该调预算档 —— 三件事的排查方向完全相反。</para>
///
/// <para>拥挤那一类还带「卡在谁身上」（<see cref="NavMoveSolution.StuckOn"/>）：
/// 「挤住了」不说出是谁挤的，等于只说「它不动了」。</para>
///
/// <para>「无」不是占位数：每一步要么说「无」，要么说那三类之一，没有 0 / −1 那种「看不懂就是没卡」的味道（「桥只转发」）。</para>
/// </summary>
public enum NavStuckKind
{
    /// <summary>没卡住（走到了、在走、原地不动、激活检查不放行 —— 激活检查那一档看 <see cref="NavMoveStatus.HeldByActivationCheck"/>）。</summary>
    None = 0,

    /// <summary>地形无路：起点或终点落不进可达集、地形断开、或一步之内被地形挡下。<b>不该启动这个命令。</b></summary>
    NoTerrainRoute = 1,

    /// <summary>
    /// 拥挤推挤：<b>有路，但避让叠加之后每步净位移低于阈值</b>。
    /// 站位冲突是叠加修正、不进网格（ADR 0008），所以它只有这一份解析读数；
    /// <see cref="NavMoveSolution.StuckOn"/> 给出「卡在谁身上」。
    /// </summary>
    Crowded = 2,

    /// <summary>
    /// 预算触顶：扩展到上限被截断，回<b>部分路径</b> + 原因 <see cref="NavPathReason.BudgetExceeded"/>
    /// （spec：触顶回部分路径而不是空）。这一条命令走不到，原因在预算档而不在地形。
    /// </summary>
    BudgetCapped = 3,
}

/// <summary>
/// 移动解 —— <b>模块每步交给宿主的那一个结果</b>（<c>GLOSSARY.md</c> 11 节）。
/// 「寻路单入口」 之下它是唯一决定「单位下一步站哪」的东西。
///
/// <para><b>逐子步的终点方向就是朝向</b>，瞬时对准、<b>不带转身时间</b>（spec「Out of Scope」：
/// 转向时间归造型与动作层）。本步一个子步都没挪出去时朝向保持原样 ——
/// 「站在原地转半圈」在读数上会变成一个查不出原因的角度跳变。</para>
/// </summary>
public sealed record NavMoveSolution
{
    /// <summary>这是谁的移动解（宿主递进来的单位 id，原样交回）。</summary>
    public required string AgentId { get; init; }

    /// <summary>命令空间里的哪一档；还没发令时是 <see cref="NavCommandKind.Unset"/>。</summary>
    public required NavCommandKind Command { get; init; }

    /// <summary>本步之后的位置（米）。</summary>
    public required NavPoint Position { get; init; }

    /// <summary>朝向（度）：<b>0 = +Z，+90 = +X，−90 = −X，180 = −Z</b>，值域 [−180, 180)。</summary>
    public required double FacingDegrees { get; init; }

    /// <summary>速度（米每游戏秒）：<b>本步实际位移 ÷ 本步步长</b>，所以「读数是 0」就等于「这一步没挪」。</summary>
    public required double SpeedMetersPerSecond { get; init; }

    /// <summary>状态。</summary>
    public required NavMoveStatus Status { get; init; }

    /// <summary>卡住类别（三类 + 「无」）。</summary>
    public required NavStuckKind Stuck { get; init; }

    /// <summary>
    /// 卡住步数：<b>当前这一类卡住连续出现了几步</b>，换类归零、不卡时归零
    /// （spec「每类都带卡住步数」）。所以它与 <see cref="Stuck"/> 要一起读，单独看没有意义。
    /// </summary>
    public required int StuckSteps { get; init; }

    /// <summary>
    /// 「卡在谁身上」——<b>只有 <see cref="NavStuckKind.Crowded"/> 那一档有值</b>，其余是 null。
    /// 给出的是那一对的另一个 id（<see cref="NavSeparation"/> 的配对读数）。
    /// 换了个推它的对象就换值：读的是<b>这一步</b>卡在谁身上，不是「历来卡在谁身上」。
    /// </summary>
    public string? StuckOn { get; init; }

    /// <summary>本步切了几个子步。子步长 ≤ 0.5 格长是写死的那一档，不是入参。</summary>
    public required int Substeps { get; init; }

    /// <summary>本步判到达用的那个目标点（<see cref="NavCommand.GoalFor"/> 的答案）。</summary>
    public required NavPoint Goal { get; init; }

    /// <summary>前视点：<b>只用来取方向</b>，不是停靠点（见 <see cref="NavLookAhead"/>）。</summary>
    public required NavPoint Aim { get; init; }

    /// <summary>移动激活检查那一道的说法（放行是「无」）。<b>本期层里不接技能层</b>，这一列恒为「无」。</summary>
    public required string CheckReason { get; init; }

    /// <summary>本步实际位移（米）= 前后两个位置的距离。一个子步都没挪出去时是 0。</summary>
    public double MovedMeters { get; init; }

    /// <summary>逐字可读的一行（invariant，「double 与不变文化」）。逐字是重放落到 L3 那一档的凭据（「断言三层」）。
    ///
    /// <para><b>「卡在」那一列只在拥挤推挤那一档出现</b>：其余三档没有「卡在谁身上」这个事实，
    /// 写一列「无」等于让「没有这一档」与「这一档有但没卡住」读出同一句话。</para></summary>
    public string Describe()
        => $"{AgentId} {Command}"
           + $" 位置 {Position}"
           + $" 朝向 {FacingDegrees.ToString(CultureInfo.InvariantCulture)}"
           + $" 速度 {SpeedMetersPerSecond.ToString(CultureInfo.InvariantCulture)}"
           + $" 位移 {MovedMeters.ToString(CultureInfo.InvariantCulture)}"
           + $" 状态 {Status}"
           + $" 卡住 {Stuck}/{StuckSteps.ToString(CultureInfo.InvariantCulture)}"
           + $" 子步 {Substeps.ToString(CultureInfo.InvariantCulture)}"
           + $" 目标 {Goal}"
           + $" 前视 {Aim}"
           + $" 激活检查 {CheckReason}"
           + (StuckOn is null ? string.Empty : $" 卡在 {StuckOn} 身上");

    /// <inheritdoc />
    public override string ToString() => Describe();
}

/// <summary>
/// 移动裁决的结果。<b>拒绝必须带一句说法</b>：静默跳过那一步，
/// 读数上就与「站在原地不动」长得一模一样（「事件发射前置」的静默拒绝教训）。
/// </summary>
/// <param name="Allowed">放不放行这一步的位移。</param>
/// <param name="Reason">一句话说法（放行时给「无」）。原样进检查点快照。</param>
public readonly record struct NavMoveCheckVerdict(bool Allowed, string Reason)
{
    /// <summary>放行（说法是「无」）。</summary>
    public static NavMoveCheckVerdict Allow => new(true, "无");
}

/// <summary>
/// 流水线上<b>唯一一道</b>「本步允不允许位移」的裁决点 —— 这一层自带的形状，接什么由宿主递。
///
/// <para>spec / ADR 0008 点名的那一处：<b>移动激活检查排在寻路层上游</b>，
/// 「先问允不允许动，再算站哪」是调用顺序的契约（「桥只转发」：注入的路径与玩家走的路径不许漂成两回事）。
/// 本期<b>层里不接技能层</b>；<b>工单 08 在这里递技能层那一版</b> ——
/// 把 <c>AbilityStepper.CanMove</c> / <c>MoveBlocked</c> 包成这个委托递进来，
/// 别的什么都不用改。激活检查拒了不是静默跳过：移动解交出 <see cref="NavMoveStatus.HeldByActivationCheck"/> 与那句说法。</para>
/// </summary>
/// <param name="agentId">这一步要动的那个单位（宿主递进来的 id，原样交回）。</param>
/// <param name="stepIndex">第几个逻辑步（从 1 起）。</param>
/// <param name="plannedMeters">这一步打算走多远（米）—— 激活检查要判的是「多远」时看这一列。</param>
public delegate NavMoveCheckVerdict NavMoveCheck(string agentId, long stepIndex, double plannedMeters);

/// <summary>
/// 步进档 —— 三个数字，<b>公共层一个缺省都不给</b>（口径同 <c>NavSolveBudget</c>）。
/// </summary>
public sealed record NavMoveTuning
{
    /// <summary>
    /// 一个逻辑步多少秒（<b>「时间基准」：一律 0.1 秒</b>）。递进来的原因不是这一层知道规则，
    /// 是速度读数是「米每游戏秒」，换算离不开它 —— 藏在层里就成了一条查不出出处的常数。
    /// </summary>
    public required double StepSeconds { get; init; }

    /// <summary>前视距离（米，正有限数）：<b>只用来取方向</b>，不许拿它当停靠点（见 <see cref="NavLookAhead"/>）。</summary>
    public required double LookAheadMeters { get; init; }

    /// <summary>本步的全局扩展上限，转发给求解器（<c>NavSolver.BeginStep</c>）。</summary>
    public required int StepSolveExpansions { get; init; }
}

/// <summary>
/// 一个单位的进场参数 —— <b>只有标量，没有单位表</b>（spec user story 9）。
/// 移速与身体半径由宿主递，层里没有词表。
/// </summary>
public sealed record NavAgentSpec
{
    /// <summary>单位 id（原样进快照与激活检查请求）。</summary>
    public required string Id { get; init; }

    /// <summary>身体半径（米，非负有限数）。0 是合法值（不占地方的探针单位）。</summary>
    public required double RadiusMeters { get; init; }

    /// <summary>移速（米每游戏秒，非负有限数）。0 是合法值（站桩单位：每步算「走多远」都得到 0）。</summary>
    public required double MoveSpeedMetersPerSecond { get; init; }

    /// <summary>起始位置（米）。</summary>
    public required NavPoint Start { get; init; }

    /// <summary>起始朝向（度，口径同 <see cref="NavMoveSolution.FacingDegrees"/>；会规整进 [−180, 180)）。</summary>
    public required double FacingDegrees { get; init; }

    /// <summary>
    /// 同组标识（标量，缺省 0 = 全员同组）。<b>只有站位冲突这一档读它</b>：只推同组的。
    ///
    /// <para>「谁和谁是一伙的」是宿主的知识，不是寻路网格的（「寻路单入口」：层满足不了的需求当场上报，
    /// 不许在层里长出一张单位表）。缺省 0 之所以合法，是因为最常见的那一局就是全员同组
    /// —— 与退休前那份世界「同方两兵各推开一半」的形状一致。</para>
    /// </summary>
    public int Group { get; init; }
}
