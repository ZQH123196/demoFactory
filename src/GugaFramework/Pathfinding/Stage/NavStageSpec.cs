#nullable enable
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.Pathfinding.Stage;

/// <summary>
/// <b>注册口</b>——寻路试验场认的这一份形状。格长、格数、阻挡布局、单位名册、步进档、
/// 预算档、容量档<b>全部由调用方递</b>，本文件一个缺省都不给。
///
/// <para>为什么一列都不给：技能试验场那一期的教训是「场子自带一份默认词表，
/// 换原型不改场子就又是假的」（<c>SkillStageSpec.cs:15-19</c>）。寻路层更不能揣一份缺省 ——
/// 格长取 1 米还是 0.5 米、障碍摆不摆、单位走多快，每一个都是<b>原型的场地条件</b>，
/// 公共层替谁挑一份都等于替某个原型做了决定。</para>
///
/// <para>可选的那一列是<b>避让档</b>（<see cref="Separation"/>）：开不开站位冲突是一个二值选择，
/// 不是「忘了给就用这个数」。缺省 = 不开，与 <see cref="NavStepper"/> 那个可空参数的既有形状同源。</para>
/// </summary>
public sealed record NavStageSpec
{
    /// <summary>空间 id（只名字，没有原点与变换，ADR 0009）。</summary>
    public required SpaceId Space { get; init; }

    /// <summary>格长（米，正有限数）。<b>由调用方定</b>：不同原型的体型精度要求不一样。</summary>
    public required double CellMeters { get; init; }

    /// <summary>网格格数（X 方向，正整数）。</summary>
    public required int CellsX { get; init; }

    /// <summary>网格格数（Z 方向，正整数）。</summary>
    public required int CellsZ { get; init; }

    /// <summary>
    /// 网格第 (0, 0) 格那一角的 X（米，场子坐标）。<b>必填</b>：场子的场地在空间的哪一段是场地条件，
    /// 场子不替调用方挑（零基场子递 0）。
    /// </summary>
    public required double OriginX { get; init; }

    /// <summary>网格第 (0, 0) 格那一角的 Z（米，场子坐标）。口径同 <see cref="OriginX"/>。</summary>
    public required double OriginZ { get; init; }

    /// <summary>
    /// 开局的静态阻挡格（<b>全由调用方递</b>）。<b>空表是合法声明</b>（明确「这片没有地形阻挡」），
    /// 与「漏给了」分成两种形状才挡得住后者（口径同 <see cref="NavGridSpec.BlockedCells"/>）。
    /// </summary>
    public required IReadOnlyList<NavCell> BlockedCells { get; init; }

    /// <summary>单位名册：谁在场、站哪、走多快、开局拿哪一道命令。</summary>
    public required NavStageRoster Roster { get; init; }

    /// <summary>步进档（步长秒数 / 前视距离 / 本步扩展上限）。</summary>
    public required NavMoveTuning Tuning { get; init; }

    /// <summary>求解预算档（每请求扩展上限）。</summary>
    public required NavSolveBudget SolveBudget { get; init; }

    /// <summary>重算档（目标移动阈值 / 没到步数上限）。</summary>
    public required NavRecomputeTuning RecomputeTuning { get; init; }

    /// <summary>容量档（四本账各留多少条）。有界账本读数带丢弃数，所以容量本身就是读数的一部分。</summary>
    public required NavStageCapacity Capacity { get; init; }

    /// <summary>站位冲突那一档（<b>可空 = 不开避让</b>）。递了就开（叠加修正，不动网格）。</summary>
    public NavSeparationTuning? Separation { get; init; }
}

/// <summary>
/// 四本账的容量档。<b>不并进 <see cref="NavStageSpec"/> 那一堆</b>：容量是「本场记多少条」，
/// 场地条件是「这片地长什么样」，两组数换的是不同的东西；混在一份里就分不清哪一列是场地了。
/// </summary>
public sealed record NavStageCapacity
{
    /// <summary>路径缓存留多少条。</summary>
    public required int PathCacheEntries { get; init; }

    /// <summary>求解记录账本留多少条。</summary>
    public required int SolveRecords { get; init; }

    /// <summary>在跟踪的请求身份留多少个（数「同一份命令重算了几次」的那本账）。</summary>
    public required int TrackedRequests { get; init; }

    /// <summary>事件账本留多少条（到达 / 部分路径 / 撞墙重算 / 卡住）。</summary>
    public required int EventRecords { get; init; }
}

/// <summary>单位名册。<b>按列表序</b>：遍历次序是确定性的一部分，所以是 <c>List</c> 不是字典。</summary>
public sealed record NavStageRoster
{
    /// <summary>场上全部单位，按摆位顺序 —— 这一份的次序就是步进次序（<c>NavStepper.Register</c> 的口径）。</summary>
    public required IReadOnlyList<NavStageUnit> Units { get; init; }
}

/// <summary>
/// 场上一个单位：<b>进场参数 + 开局命令</b>。身体半径与移速是标量（原型的词表不进这一层，「寻路单入口」），
/// 起始位置与朝向是开局的场地条件。
/// </summary>
public sealed record NavStageUnit
{
    /// <summary>单位 id（原样进快照与激活检查请求）。</summary>
    public required string Id { get; init; }

    /// <summary>身体半径（米，非负有限数）。同时是网格里「可行域腐蚀量」那一份的宽度。</summary>
    public required double RadiusMeters { get; init; }

    /// <summary>移速（米每游戏秒，非负有限数）。表现层喂 <c>ModelAnimator.Travel</c> 用的就是它。</summary>
    public required double MoveSpeedMetersPerSecond { get; init; }

    /// <summary>起始摆位（米，场子空间里的一点）。</summary>
    public required NavPoint Start { get; init; }

    /// <summary>起始朝向（度，口径同 <c>NavMoveSolution.FacingDegrees</c>：0 = +Z、+90 = +X）。</summary>
    public required double FacingDegrees { get; init; }

    /// <summary>同组标识（标量，缺省 0 = 全员同组）。<b>只有站位冲突那一档读它。</b></summary>
    public int Group { get; init; }

    /// <summary>
    /// 开局命令（<b>可空 = 还没发令</b>）。发令走 <c>NavStepper.Issue</c> 那一条写路径，
    /// 与 <c>Inject("SetOrder", …)</c> 是同一次调用 —— 场子不给命令第二条口。
    /// </summary>
    public NavCommand? OpeningOrder { get; init; }
}
