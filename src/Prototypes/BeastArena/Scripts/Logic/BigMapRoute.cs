#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 大地图那一半：一张 400×400 米的真网格寻路空间 + 巨兽在上面的航行状态机。
//
// 这一块与切片 1/2 那份 WorldMap（节点图 + 边上进度）不是同一件事：那一份把航线钉成三条固定折线边，
// 进度是一个 0..1 的数；这一份走真网格，巨兽的位置与朝向是真值住在 NavStepper 里，
// 两城之间那 240 米是真的走 600 秒走完（10 游戏小时）。
//
// **一把尺子**：格长 1 米，逻辑坐标即真实米（表现层不再乘那一档倍率）。
// 寻路网格的格号从 −58 起（`BigMapGrid.GridOriginMeters`），格号与米号差一个常数 58。

/// <summary>
/// 一个固定点位的规格：坐标、物价与游客池（停靠那一侧读的几列都在这儿）。
/// </summary>
/// <param name="Id">点位标识（贫城 / 富城 / 沙城 / 野外锚地）。</param>
/// <param name="Name">中文名，只给人看。</param>
/// <param name="Position">城的逻辑坐标（米）—— <b>城门</b>：城模型朝航线那一侧的边缘点，模型本体整个在它身后。</param>
/// <param name="OutwardNormal">外法线（单位向量，水平）：从城体指向航线的那一侧。</param>
/// <param name="InwardRings">向心环数（拔锚防守敌人数 = 20 + 4 × 环数）。</param>
/// <param name="PriceFactor">物价系数（买卖与空艇成交价都乘它）。</param>
/// <param name="VisitorPoolCap">游客池上限。</param>
public sealed record MapPlace(
    string Id,
    string Name,
    NavPoint Position,
    NavPoint OutwardNormal,
    int InwardRings,
    double PriceFactor,
    int VisitorPoolCap)
{
    /// <summary>
    /// 兽的停靠点：城门沿外法线再外推一个兽半宽
    /// （<see cref="ArenaTuning.BeastHalfWidthFor"/>，按传入位阶取那一档）。
    ///
    /// <para><b>为什么要显式递位阶</b>：二阶的兽比一阶宽（半宽 26.45 → 30.00 米），
    /// 停靠点跟着外扩 3.55 米 —— 位阶留在调用方，这一口就不存在「悄悄按一阶算」的那一档。</para>
    ///
    /// <para><b>为什么兽停在门外而不是城门上</b>：兽体沿法线那一个方向正好占掉这一个档的半宽
    /// （一阶：实测 52.9 ÷ 2 = 26.45），
    /// 停在这个点上，兽身贴着城门那一半正好抵住城体的边缘 —— 兽不压城，城也不骑在兽身上。</para>
    /// </summary>
    public NavPoint StopPoint(BeastTier tier)
    {
        var halfWidth = ArenaTuning.BeastHalfWidthFor(tier);
        return new NavPoint(
            Position.X + (OutwardNormal.X * halfWidth),
            Position.Z + (OutwardNormal.Z * halfWidth));
    }

    /// <summary>
    /// 城模型的中心：城门沿外法线<b>反推</b>半个城深（<see cref="ArenaTuning.CityHalfDepthMeters"/> 米）。
    /// 于是城体整个在城门后面，朝航线那一侧的边缘正好落在城门上（停靠点落在模型上）。
    /// </summary>
    public NavPoint CityCenter => new(
        Position.X - (OutwardNormal.X * ArenaTuning.CityHalfDepthMeters),
        Position.Z - (OutwardNormal.Z * ArenaTuning.CityHalfDepthMeters));

    /// <summary>
    /// 兽停靠时<b>兽身躺着的那条轴</b>（单位向量，水平）：与外法线垂直。
    ///
    /// <para><b>为什么停靠时要横过来</b>：停靠点离城门一个兽半宽
    /// （<see cref="ArenaTuning.BeastHalfWidthFor"/>，按位阶取那一档），那一档是按「兽身的<b>宽</b>正对城门」算的 ——
    /// 兽身沿法线那一个方向的占位是它的半宽（一阶 26.45 米）。
    /// 巨兽节点跟着航向转（模型的长边 89.58 米顺着航向，宽 52.93 米横过来），
    /// 于是沿航线（东西向）到的两座老城，兽停下来天然是横着的；
    /// 沿支路（南北向）到的沙城与野外锚地，兽停下来是<b>头朝城</b>的 ——
    /// 那时伸进城里的是兽的<b>长</b>（44.79 米），兽身压城 18.35 米。
    /// 停靠时把兽身摆到与外法线垂直的那条轴上，伸进城里的又回到当前位阶的半宽那一档，
    /// 与停靠点的偏移同一个数，压城归零（净空 0.0 米，兽身贴着城门）。</para>
    ///
    /// <para><b>方向取「X 分量为正」那一侧</b>：同一座城不因外法线正负而翻向，
    /// 兽从哪边来就朝哪边停（那一档由表现层按当前航向取符号）。</para>
    /// </summary>
    public NavPoint ParkAxis
    {
        get
        {
            var axis = new NavPoint(OutwardNormal.Z, -OutwardNormal.X);
            return axis.X >= 0.0 ? axis : new NavPoint(-axis.X, -axis.Z);
        }
    }
}

/// <summary>
/// 大地图这张网格：空间名、圆盘定尺、固定点位与世界边缘那几档算术。
///
/// <para><b>世界形状是圆盘</b>：圆心 <see cref="DiskCenterX"/>,<see cref="DiskCenterZ"/> 米、
/// 陆地半径 <see cref="LandRadiusMeters"/> 米。判界是半径判据
/// （<c>hypot(x − 200, z − 200) &gt; 256</c>），圆外那一圈是星空出界区。</para>
///
/// <para><b>寻路网格是 516 × 516 米、格号原点 −58</b>：格长 1 米，
/// 逻辑坐标即真实米（表现层不再乘那一档倍率）。圆盘整个落在网格里，
/// 出界目标那一圈也在里面 —— 四个方向都出得去。</para>
///
/// <para><b>出界与送回是一对径向点</b>：出界那一发朝「从圆心经兽当前位置向外、半径
/// <see cref="OutboundTargetRadiusMeters"/> 米」那一点走；魂石扣完送回同一方向、半径
/// <see cref="SendBackRadiusMeters"/> 米那一点。</para>
/// </summary>
public static class BigMapGrid
{
    /// <summary>空间的名字。</summary>
    public static readonly SpaceId Space = new("大地图");

    /// <summary>格长（米）。</summary>
    public const double CellMeters = 1.0;

    /// <summary>
    /// 寻路网格的格数（沿 X 与沿 Z 各一份）：516 × 1.0 米 = 516 米见方。
    ///
    /// <para><b>为什么是 516 而不是 512</b>：圆盘跨的是世界 −56..456 米，
    /// 而出界那一发的目标点在<b>半径 257 米</b>那圈上（<see cref="OutboundTargetRadiusMeters"/>）——
    /// 正北、正南、正东、正西四个方向上它落在 −57 与 457 米，比圆盘的外接方框还各出 1 米。
    /// 网格不越过那一点，目标就落在网格外，求解器判「目标不可达」，兽永远出不了界 ——
    /// 那正是「四方向出界只有北/东可用」那一条的病根。</para>
    /// </summary>
    public const int Cells = 516;

    /// <summary>
    /// 寻路网格的格号原点（世界米）：第 (0, 0) 格那一角落在 −58 米。
    ///
    /// <para><b>为什么不是 −56</b>：−56 是圆盘外接方框的起点（地形那张格网用的就是它），
    /// 网格起点要比它再退 2 米，好让半径 257 那圈出界目标整圈落在网格外侧那一圈里。
    /// 退 2 米而不是 1 米：右边那一线判界外（<c>TryLocal</c> 的 <c>gx >= CellsX</c>），
    /// 只退 1 米的话东侧目标正好压在那条线上，判的还是界外。</para>
    ///
    /// <para><b>与地形那张格网的关系</b>：地形是 512 × 512、格号原点 −56；
    /// 寻路是 516 × 516、格号原点 −58。两张现在<b>只差 2 格</b>（寻路那张每边多两格余量）：
    /// 寻路格号 = 地形格号 + 2，一张图两套格号对得上。</para>
    /// </summary>
    public const double GridOriginMeters = -58.0;

    /// <summary>圆盘圆心的世界 X（米）。</summary>
    public const double DiskCenterX = 200.0;

    /// <summary>圆盘圆心的世界 Z（米）。</summary>
    public const double DiskCenterZ = 200.0;

    /// <summary>陆地半径（米）。判界是「离圆心超过这一档即出界」。</summary>
    public const double LandRadiusMeters = 256.0;

    /// <summary>
    /// 出界那一发的目标半径（米）—— 比陆地半径多 1.0 米。
    ///
    /// <para><b>为什么不是 256</b>：巨兽在目标前就停（出界那一发的到达容差
    /// <see cref="ArenaTuning.OutboundArriveToleranceMeters"/> 米），停在目标上就等于停在界内那半米上，
    /// 那样它永远不算出界、魂石永远不扣。目标必须够远，兽停下来才一定越过 256 那一线。
    /// 257 = 256 + 0.5（容差余量）+ 0.5（余量）。</para>
    /// </summary>
    public const double OutboundTargetRadiusMeters = 257.0;

    /// <summary>
    /// 魂石扣完之后的送回半径（米）—— 比陆地半径少 0.5 米，落在圆盘内侧。
    /// </summary>
    public const double SendBackRadiusMeters = 255.5;

    /// <summary>贫城点位。</summary>
    public static MapPlace PoorCity { get; } =
        new("poor_city", "贫城", new NavPoint(90.0, 210.0), new NavPoint(0.0, -1.0), 0, 0.8, 80);

    /// <summary>富城点位。</summary>
    public static MapPlace RichCity { get; } =
        new("rich_city", "富城", new NavPoint(330.0, 210.0), new NavPoint(0.0, -1.0), 1, 1.2, 150);

    /// <summary>野外锚地：两城都拒停时的保底停靠点。</summary>
    public static MapPlace WildAnchor { get; } =
        new("wild_anchor", "野外锚地", new NavPoint(210.0, 290.0), new NavPoint(0.0, -1.0), 0, 1.0, 0);

    /// <summary>
    /// 沙城点位（第三座城，落在沙漠里——用户 2026-10-19 加地形时定：
    /// 两座老城在草地上，新城在沙漠上）。
    ///
    /// <para><b>它的外法线朝北（+Z）</b>：南支路在 x 200..219、z 100..179，
    /// 就在沙城北面 —— 城体在路南、门开在朝路那一侧，所以法线是 +Z。</para>
    /// </summary>
    public static MapPlace DesertCity { get; } =
        new("desert_city", "沙城", new NavPoint(210.0, 90.0), new NavPoint(0.0, 1.0), 0, 0.9, 100);

    /// <summary>
    /// 开局那一格：贫城停靠点再往西挪 <see cref="RunStartOffsetMeters"/> 米的野地。
    ///
    /// <para><b>为什么不在停靠点上</b>：一局要从「驶入贫城、第一次停靠」开场 ——
    /// 开局就压在停靠点上的话，玩家按下停靠那一步就已经到了，开局航程那一段不存在。
    /// 挪的是<b>沿航线方向</b>（西），不是沿外法线：挪到法线一侧会让贫城 → 富城那一程
    /// 斜着走，沿线的两个食物点（离航线 20 米）就出了 30 米的觅食半径，脱队觅食永远不触发。</para>
    ///
    /// <para><b>按一阶那一档算</b>：这一格是开局的落点，而巨兽开局恒为一阶
    /// （<c>BeastGrowth.Tier</c> 的开局值），二阶的外扩够不着这一格。</para>
    /// </summary>
    public static NavPoint RunStart { get; } = new(
        PoorCity.StopPoint(BeastTier.Tier1).X - RunStartOffsetMeters,
        PoorCity.StopPoint(BeastTier.Tier1).Z);

    /// <summary>开局那一格离贫城停靠点多远（米）：40 米 = 1000 步 = 100 游戏分钟。</summary>
    public const double RunStartOffsetMeters = 40.0;

    /// <summary>
    /// 四个点位按停靠顺序排（贫 → 野外 → 富 → 沙）。
    /// <b>新点位追加在末尾</b>：<see cref="ArenaWorld.AirshipVisitsFor"/> 按本表序号派生空艇到访数，
    /// 插到中间会让旧点位的序号平移、重放分叉。
    ///
    /// <para><b>摆位怎么来的</b>：贫城与富城同在 z=210 一行、相距 240 米
    /// （240 米 ÷ 0.4 米每现实秒 = 600 现实秒 = 10 游戏小时，这是设计的口径）；
    /// 两座老城的城门在主干道路北缘外 10 米。沙城在 z=90、离南支南缘 10 米；
    /// 野外锚地在 z=290、离北支北缘 10 米。</para>
    ///
    /// <para><b>城门与停靠点是两件事</b>：<see cref="MapPlace.Position"/> 是城门（城模型朝航线那一侧的边缘），
    /// 兽停在 <see cref="MapPlace.StopPoint(BeastTier)"/>（城门再外推一个当前位阶的兽半宽）。</para>
    /// </summary>
    public static IReadOnlyList<MapPlace> Places { get; } =
        new[] { PoorCity, WildAnchor, RichCity, DesertCity };

    /// <summary>
    /// 大地图上的生态对象摆位（地形决定分布：树与兽群在草地，矿石在沙漠带）。
    ///
    /// <para><b>贫城 → 沙城航线上固定五件</b>：两棵树、一群野兽、两块矿石，
    /// 全部压在那条直线的路肩上（离航线 0 米），于是手动指定（10 米）随时够得着，
    /// 饿着自动航行（30 米）也一定会在路上碰到。两块矿石 60 × 2 = 120 点消化进度，
    /// <b>保证抵达沙城前拿得到第一单位魔晶</b>（100 点一单位，溢出 20 点）。</para>
    ///
    /// <para><b>其余几件按地形摆</b>：富城那一侧的草地放树与兽群，沙漠深处补两块矿石，
    /// 野外锚地附近再放一群兽 —— 主线不要求重复刷取，再生只是补救路径。</para>
    /// </summary>
    public static IReadOnlyList<EcoNodeSpec> EcoNodeSpecs { get; } =
    [
        // 贫城 → 沙城航线（直线 t = 0.18 / 0.26 / 0.34 / 0.50 / 0.68）。
        new("tree_poor_1", EcoKind.Tree, new NavPoint(111.7, 171.5)),
        new("tree_poor_2", EcoKind.Tree, new NavPoint(121.3, 166.1)),
        new("herd_poor_1", EcoKind.Herd, new NavPoint(130.9, 160.7)),
        new("ore_poor_1", EcoKind.Ore, new NavPoint(150.0, 150.0)),
        new("ore_poor_2", EcoKind.Ore, new NavPoint(171.6, 137.9)),

        // 富城那一片草地。
        new("tree_rich_1", EcoKind.Tree, new NavPoint(270.0, 175.0)),
        new("herd_rich_1", EcoKind.Herd, new NavPoint(250.0, 170.0)),
        new("tree_rich_2", EcoKind.Tree, new NavPoint(265.0, 165.0)),

        // 沙漠深处的两块矿。
        new("ore_desert_3", EcoKind.Ore, new NavPoint(120.0, 120.0)),
        new("ore_desert_4", EcoKind.Ore, new NavPoint(180.0, 100.0)),

        // 野外锚地那一侧。
        new("herd_wild_1", EcoKind.Herd, new NavPoint(230.0, 270.0)),
        new("tree_wild_1", EcoKind.Tree, new NavPoint(240.0, 260.0)),
    ];

    /// <summary>
    /// 建这张网格。<b>空阻挡表是合法声明</b>：大地图上没有静态地形阻挡 ——
    /// 四种地形（<see cref="BigMapTerrain"/>）只供填色与读数，不挡路。
    ///
    /// <para><b>覆盖范围</b>：网格从 <see cref="GridOriginMeters"/> 起 516 × 516 格，覆盖世界 −58..458 米。
    /// 圆盘整个落在里面（圆盘跨 −56..456），出界目标那一圈（半径 257 米，正方向上落在 −57 与 457）
    /// 也整个落在里面 —— 四个方向都出得去，是这一格存在的全部理由。
    ///
    /// <para><b>原点这一列是框架寻路层给的</b>（<c>NavGridSpec.OriginX/OriginZ</c>）：
    /// 网格第 (0,0) 格那一角落在空间的哪一点。零基网格递 0 就行，不必改任何行为。</para>
    /// </summary>
    public static NavGrid Build()
        => new(new NavGridSpec
        {
            Space = Space,
            CellMeters = CellMeters,
            CellsX = Cells,
            CellsZ = Cells,
            OriginX = GridOriginMeters,
            OriginZ = GridOriginMeters,
            BlockedCells = Array.Empty<NavCell>(),
        });

    /// <summary>按 id 查一个停靠点（查不到回 null）。</summary>
    public static MapPlace? PlaceById(string id)
    {
        foreach (var p in Places)
        {
            if (p.Id == id) return p;
        }

        return null;
    }

    /// <summary>
    /// 坐标是不是落在圆盘外侧（星空出界区）：离圆心超过 <see cref="LandRadiusMeters"/> 米即出界。
    ///
    /// <para><b>判据是严格大于</b>：正好 256.0 米那一线算界内，256.1 米算出界。</para>
    /// </summary>
    public static bool IsOutOfBounds(NavPoint point)
    {
        var dx = point.X - DiskCenterX;
        var dz = point.Z - DiskCenterZ;
        return Math.Sqrt((dx * dx) + (dz * dz)) > LandRadiusMeters;
    }

    /// <summary>坐标是不是落在圆盘以内（界内）。</summary>
    public static bool IsInsideEdge(NavPoint point) => !IsOutOfBounds(point);

    /// <summary>
    /// 从圆心经 <paramref name="from"/> 那一侧、半径 <paramref name="radius"/> 米那一点（米）。
    ///
    /// <para><b>出界与送回是同一口径</b>：出界朝半径更大那一点走，扣完魂石送回同一方向、半径小一点那一点 ——
    /// 两点严格同一条射线上，兽是被「推回来」的，不是被横移回来的。</para>
    ///
    /// <para><b>正落在圆心上时</b>方向无从取，按固定的那一条（+X）走：重放要逐字可复现，
    /// 不能让「取不到方向」这件事每次跑出不同结果。</para>
    /// </summary>
    public static NavPoint RadialPoint(NavPoint from, double radius)
    {
        var dx = from.X - DiskCenterX;
        var dz = from.Z - DiskCenterZ;
        var length = Math.Sqrt((dx * dx) + (dz * dz));
        if (length <= 1e-12)
        {
            return new NavPoint(DiskCenterX + radius, DiskCenterZ);
        }

        return new NavPoint(
            DiskCenterX + ((dx / length) * radius),
            DiskCenterZ + ((dz / length) * radius));
    }
}

/// <summary>巨兽在大地图上的航行阶段。</summary>
public enum VoyagePhase
{
    /// <summary>停着不动（起始态与抵达后）。</summary>
    Holding = 0,

    /// <summary>航行中：朝目标城镇走（自动航行），或朝玩家点的那一点走（手动航行）。</summary>
    Sailing = 1,

    /// <summary>脱队觅食：朝食物点走。</summary>
    DetouringToFood = 2,

    /// <summary>到达食物点，原地进食。</summary>
    Eating = 3,

    /// <summary>吃完返航：先回航线原断点。</summary>
    ReturningToWaypoint = 4,

    /// <summary>被令出界：朝圆盘外那一侧走（此间按游戏分钟扣魂石）。</summary>
    Outbound = 5,

    /// <summary>玩家指定目标，原地吞食五游戏分钟。</summary>
    ManualEating = 6,
}

/// <summary>
/// 巨兽在大地图上的航行状态机 —— 两个观测口里的另两件：
/// <b>时间可控</b>（<see cref="DecideStep"/> / <see cref="SettleStep"/> 吃逻辑步）
/// 与 <b>状态可查</b>（<see cref="DebugSegment"/> 一行读完当前阶段、坐标、饱食度与两本小账）。
///
/// <para><b>两段推进的次序是契约</b>：<see cref="DecideStep"/> 排在走位那一次脉冲<b>之前</b>
/// （饱食度衰减、按饱食度改移速、到点判定与改令都在这一段，移速那一口要赶在本步脉冲之前递），
/// <see cref="SettleStep"/> 排在<b>之后</b>（出界扣魂石、落位读数都读这一步走完之后的位置）。</para>
///
/// <para><b>所有按时间的量都挂在步号整除上</b>（游戏整小时 / 整分钟 / 整日各一次），
/// 不按「每步累加一个浮点增量」—— 后者 600 步下来是 1.0000000000000002 那一档，
/// 而饱食度与扣魂石都要能被测试逐字读出来。</para>
/// </summary>
public sealed partial class BeastVoyage
{
    /// <summary>巨兽在大地图上的单位 id（这一空间里它是唯一单位）。</summary>
    public const string BeastId = "beast";

    private readonly NavStepper _stepper;
    private readonly BeastGrowth _growth;
    private readonly BeastEcology _ecology;
    private long _lastDrainMinute;
    private long _eatUntilTick;
    private string _forageNodeId = string.Empty;
    private string _manualNodeId = string.Empty;
    private long _manualEatUntilTick;
    private string _manualResumeCityId = string.Empty;
    private NavPoint? _manualResumePoint;
    private double _appliedSpeedFactor = 1.0;

    /// <summary>
    /// 出界那一发的目标点（径向，半径 <see cref="BigMapGrid.OutboundTargetRadiusMeters"/> 米）。
    /// 下令那一刻定下来，之后每一步都朝它走 —— 目标不随兽自己挪，兽才不会一路追着一条动线跑。
    /// </summary>
    private NavPoint? _outboundTarget;

    /// <summary>自主觅食开始脱队时抛一次（表现层出横幅用）。</summary>
    public event Action<EcoNode>? ForageStarted;

    /// <summary>玩家召回、或吃完返航时抛一次。</summary>
    public event Action? ForageRecalled;

    /// <summary>附近没有可吞对象、进入一游戏小时冷却时抛一次。</summary>
    public event Action<ForageGiveUpReason>? ForageGaveUp;

    /// <summary>一口吞食开始时抛一次（第二项 = 自主觅食还是玩家指定）。</summary>
    public event Action<EcoNode, bool>? EatStarted;

    /// <summary>
    /// 接大地图那一条走位流水线（<c>WorldAssembly.StepperOf(BigMapGrid.Space)</c>），
    /// 就地登记巨兽。寻路半径 0 合法（它是那只唯一在场的单位），移速原样递米每现实秒。
    ///
    /// <para><b>生态那一件由外面递进来</b>：饱食度、消化进度与生态对象都住在
    /// <see cref="BeastEcology"/> 里，本件只负责「走到哪、吃不吃了」以及几段返航状态
    /// —— 状态只有一份，读数仍从本类转发同一格。</para>
    /// </summary>
    public BeastVoyage(BeastGrowth growth, BeastEcology ecology, NavStepper stepper, Func<long>? tick = null)
    {
        ArgumentNullException.ThrowIfNull(growth);
        ArgumentNullException.ThrowIfNull(ecology);
        ArgumentNullException.ThrowIfNull(stepper);
        _growth = growth;
        _ecology = ecology;
        _stepper = stepper;
        var fallback = stepper.StepIndex;
        Tick = tick ?? (() => fallback);

        _stepper.Register(new NavAgentSpec
        {
            Id = BeastId,
            RadiusMeters = ArenaTuning.BeastRadiusMeters,
            MoveSpeedMetersPerSecond = ArenaTuning.BeastSpeedMetersPerSecond,
            // 开局停在贫城西边的野地上（离停靠点 40 米、未停靠）：第一段的开头是
            // 玩家自己按停靠驶入贫城那一程，不是一开局就压在城门外。
            Start = BigMapGrid.RunStart,
            FacingDegrees = 0.0,
            Group = 1,
        });
    }

    /// <summary>当前步号读数（宿主递进来；不递就读步进器自己的步号）。</summary>
    public Func<long> Tick { get; }

    /// <summary>走位流水线本体（读数与调试用）。</summary>
    public NavStepper Stepper => _stepper;

    /// <summary>当前航行阶段。</summary>
    public VoyagePhase Phase { get; private set; } = VoyagePhase.Holding;

    /// <summary>当前目标城镇 id（<see cref="VoyagePhase.Sailing"/> 那一栏读它）。</summary>
    public string? TargetCityId { get; private set; }

    /// <summary>
    /// 手动目标点（世界米）—— 玩家右键点地图空地那一发落在这里。
    ///
    /// <para><b>与 <see cref="TargetCityId"/> 互斥</b>：手动那一发清目标城，
    /// 「停靠」那一发清手动目标 —— 同一时刻只有一个去处。</para>
    /// </summary>
    public NavPoint? ManualTarget { get; private set; }

    /// <summary>饱食度读数（0..100）。<b>真值住在 <see cref="BeastEcology"/></b>，这里只转发。</summary>
    public double Satiation => _ecology.Satiation;

    /// <summary>魂石（开局 <see cref="ArenaTuning.StartingSoulStones"/>）。</summary>
    public double SoulStones { get; private set; } = ArenaTuning.StartingSoulStones;

    /// <summary>粮仓食物（喂食那一口的原料；八条守恒等式那一侧归账本）。</summary>
    public double GranaryFood { get; private set; } = 0.0;

    /// <summary>当前坐标（大地图逻辑米，位置真值在走位层，这里是读数）。</summary>
    public NavPoint Position => _stepper.PositionOf(BeastId);

    /// <summary>
    /// 兽此刻停在哪座城的门外（不在任何停靠点回 null）。
    ///
    /// <para><b>判据是「站在当前位阶的停靠点上」</b>：走位层按到达容差（1 米）停在停靠点附近，
    /// 于是「站没站在城门外」这一格由位置答，不必另记一个状态 ——
    /// 另记一份状态就得跟位置对账，对不上时反而查不出哪边错了。
    /// 位阶读 <see cref="BeastGrowth.Tier"/>：二阶的外扩之后，旧档那一点不再算停着。</para>
    ///
    /// <para><b>它管什么</b>：表现层据此把兽身摆到横过来那一档（<see cref="MapPlace.ParkAxis"/>），
    /// 兽身贴着城门而不压城。</para>
    /// </summary>
    public MapPlace? ParkedPlace
    {
        get
        {
            var at = Position;
            var tier = _growth.Tier;
            foreach (var place in BigMapGrid.Places)
            {
                if (at.DistanceTo(place.StopPoint(tier)) <= ArenaTuning.ArriveToleranceMeters) return place;
            }

            return null;
        }
    }

    /// <summary>
    /// 航线原断点：脱队觅食那一刻记下的坐标。返航先回这一点，到点才恢复原目标城镇
    /// —— 没有这一格，脱队就等于把航线丢了。
    /// </summary>
    public NavPoint? RouteWaypoint { get; private set; }

    /// <summary>位阶移速倍率（默认 1.0；进阶仪式到点时由 <see cref="SetTierSpeedFactor"/> 写上）。
    /// 与饱食度那一档<b>相乘</b>，不是二选一。
    /// </summary>
    public double TierSpeedFactor { get; private set; } = 1.0;

    /// <summary>写位阶移速倍率（进阶仪式到点那一发由世界调它）。</summary>
    public void SetTierSpeedFactor(double factor)
    {
        if (factor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "位阶移速倍率不能是零或负数");
        }

        TierSpeedFactor = factor;
        ApplySpeedFactor();
    }

    // ---- 形态带来的两个解锁计数（系数那一摊已搬进生态件） ----

    /// <summary>
    /// 在沙漠带里<b>累计</b>待过的游戏分钟（离开不清零）。
    /// <b>读数转发给 <see cref="BeastGrowth"/></b>：状态只有那一份，这里不存第二份。
    /// </summary>
    public long DwellGameMinutes => _growth.DwellGameMinutes;

    /// <summary>累计有效喂进去的食物。读数转发给 <see cref="BeastGrowth"/>。</summary>
    public double FedTotalFood => _growth.FedTotalFood;

    /// <summary>调试直写口：把沙漠累计计时摆到某个数（真跑要 4 游戏小时，测试等不起）。</summary>
    public void SetDwellGameMinutes(long minutes) => _growth.SetDwellGameMinutes(minutes);

    /// <summary>调试直写口：往累计喂食里加一份（真跑要 400 食物，测试等不起）。</summary>
    public void AddFedTotalFood(double amount) => _growth.AddFedTotalFood(amount);

    /// <summary>沿饱食度算当前该有的移速倍率（读数口：喂饱了与饿着的差别从这一列读）。</summary>
    public double SpeedFactor()
    {
        var satiationFactor = Satiation < ArenaTuning.StarvingSatiationThreshold
            ? ArenaTuning.StarvingSpeedFactor
            : Satiation < ArenaTuning.SlowSatiationThreshold ? ArenaTuning.SlowSpeedFactor : 1.0;

        return TierSpeedFactor * satiationFactor;
    }

    /// <summary>
    /// 下命令：朝一个停靠点航行（换目标城镇走这一口）——「停靠」那一发走它。
    ///
    /// <para><b>名字带 City 是为了跟手动那一口分得开</b>：<see cref="SailTo(double, double)"/>
    /// 是玩家右键点地图空地那一发（点到哪开到哪），本口是开到城门外。
    /// 两口互斥：谁下令谁作废另一口的目标。</para>
    /// </summary>
    public void SailToCity(string cityId)
    {
        var place = BigMapGrid.PlaceById(cityId);
        if (place is null)
        {
            var known = new List<string>(BigMapGrid.Places.Count);
            foreach (var p in BigMapGrid.Places) known.Add(p.Id);
            throw new ArgumentException(
                $"{cityId} 不是大地图上的停靠点（已登记：{string.Join("、", known)}）—— "
                + "停靠点是关卡摆位的一部分，没有自动挑一个的档",
                nameof(cityId));
        }

        TargetCityId = place.Id;
        RouteWaypoint = null;
        ManualTarget = null;   // 手动那一口的目标作废（两口互斥）
        ClearManualEat();
        Phase = VoyagePhase.Sailing;

        // 目标是当前位阶的停靠点（城门再外推一个兽半宽），不是城门：兽停在城门外，兽体不压城。
        _stepper.Issue(BeastId, NavCommand.GoTo(place.StopPoint(_growth.Tier), ArenaTuning.ArriveToleranceMeters));
    }

    /// <summary>
    /// 位阶换了兽半宽之后，把停靠中的兽重新开到<b>当前位阶</b>的停靠点 ——
    /// 二阶进阶完成那一刻由世界调一次：兽还站在旧档那一点上，会比新档的余量窄出 3.55 米，压城。
    ///
    /// <para><b>判据读航行状态、不读几何（<see cref="ParkedPlace"/>）</b>：进阶完成的那一步位阶
    /// 已经换新，ParkedPlace 拿新档停靠点判位置 —— 兽站在旧点上，离新点 3.55 米、
    /// 超出 1 米到达容差，读回来是 null，找不到它停在哪座城门外。
    /// 「停靠」读 Holding + 目标城，与世界那格抵达判定同一口径；
    /// 手动停在野地的兽（没有目标城）不在范围。航行中的不用动：到达判定已读新档。</para>
    /// </summary>
    public void RedockAtCurrentTier()
    {
        if (Phase != VoyagePhase.Holding) return;
        if (TargetCityId is not { } cityId) return;
        if (ManualTarget is not null) return;   // 两口互斥：带着手动目标的不是停靠

        SailToCity(cityId);
    }

    /// <summary>
    /// 下命令：朝地图上一点航行（玩家右键点地图空地那一发）。<b>到达即停</b>。
    ///
    /// <para><b>改道即取消进港</b>：目标城清空 —— 兽不再往那座城开，也不会在那儿进港
    /// （进港那一格读的是 <see cref="TargetCityId"/>，清了就进不去）。
    /// 反过来「停靠」那一发（<see cref="SailToCity"/>）把手动目标清空：两口互斥。</para>
    ///
    /// <para><b>手动这一段不脱队觅食</b>：玩家在掌舵，饿兽也照玩家点的方向走 ——
    /// 脱队觅食是自动航行那一侧的机制（见 <see cref="StepSailing"/>）。</para>
    ///
    /// <para><b>到达容差与通航同一档</b>（<see cref="ArenaTuning.ArriveToleranceMeters"/>）：
    /// 玩家点的那一点就是兽停的那一点，与进港那一格同一个判据。</para>
    /// </summary>
    public void SailTo(double x, double z)
    {
        var target = new NavPoint(x, z);

        TargetCityId = null;    // 改道取消进港
        RouteWaypoint = null;   // 觅食那个断点作废
        ManualTarget = target;
        ClearManualEat();
        Phase = VoyagePhase.Sailing;

        _stepper.Issue(BeastId, NavCommand.GoTo(target, ArenaTuning.ArriveToleranceMeters));
    }

    /// <summary>
    /// 下命令：朝圆盘外走（出界扣魂石从这一步之后开始计）。
    ///
    /// <para><b>目标是径向点</b>：从圆心经兽当前位置向外、半径
    /// <see cref="BigMapGrid.OutboundTargetRadiusMeters"/> 米那一点。
    /// 目标是 257 而不是 256：兽在目标前就停（容差 <see cref="ArenaTuning.OutboundArriveToleranceMeters"/> 米），
    /// 停在 256 那一线上就还在界内，魂石永远不扣。</para>
    ///
    /// <para><b>这一发用自己那一档到达容差</b>：那一档比通航那一档紧一档，
    /// 出界要的就是「真的跨出去」。</para>
    /// </summary>
    public void SteerOutOfBounds()
    {
        _outboundTarget = BigMapGrid.RadialPoint(Position, BigMapGrid.OutboundTargetRadiusMeters);
        ClearManualEat();
        Phase = VoyagePhase.Outbound;
        _stepper.Issue(BeastId, NavCommand.GoTo(_outboundTarget.Value, ArenaTuning.OutboundArriveToleranceMeters));
    }

    /// <summary>调试直写口：把饱食度设到某个数（自然衰减要几十游戏小时，测试等不起）。</summary>
    public void SetSatiation(double value) => _ecology.SetSatiation(value);

    /// <summary>调试直写口：往粮仓放食物（喂食那一口的原料）。</summary>
    public void AddGranaryFood(double amount)
    {
        if (amount < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "放进粮仓的食物不能是负数");
        }

        GranaryFood += amount;
    }

    /// <summary>从粮仓取食（喂食那一口）；不够就只取得出那么多。</summary>
    /// <returns>实际取走的食物份数。</returns>
    public double TakeGranaryFood(double food)
    {
        if (food <= 0.0) return 0.0;

        var taken = Math.Min(food, GranaryFood);
        GranaryFood -= taken;
        return taken;
    }

    /// <summary>
    /// 推进的第一段，<b>排在走位那一次脉冲之前</b>：饱食度衰减、按饱食度改移速、
    /// 到点判定与改令都在这一段。移速那一口要赶在本步脉冲之前递（寻路层读的是这一刻的值）。
    /// </summary>
    public void DecideStep(long tick)
    {
        TickDwell(tick);
        _ecology.DecayHourly(tick);
        ApplySpeedFactor();

        switch (Phase)
        {
            case VoyagePhase.Sailing:
                StepSailing(tick);
                break;
            case VoyagePhase.DetouringToFood:
                StepDetouring(tick);
                break;
            case VoyagePhase.Eating:
                StepEating(tick);
                break;
            case VoyagePhase.ManualEating:
                StepManualEating(tick);
                break;
            case VoyagePhase.ReturningToWaypoint:
                StepReturning();
                break;
            case VoyagePhase.Outbound:
                StepOutbound();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 推进的第二段，<b>排在走位那一次脉冲之后</b>：出界扣魂石与送回都读这一步走完之后的位置。
    /// </summary>
    public void SettleStep(long tick)
    {
        // 分钟标记每一步都跟着走（不管在不在界外）：只在这一支里更新的话，
        // 第一次出界那一帧会把「从开局到出界」攒下的整段分钟一次性扣光 ——
        // 30 魂石一步扣成 0，症状是「一下界就被送回来」，而读数看着还挺合理。
        var minute = tick / ArenaTime.StepsPerGameMinute;
        var elapsed = minute - _lastDrainMinute;
        _lastDrainMinute = minute;

        if (BigMapGrid.IsInsideEdge(Position)) return;
        if (elapsed <= 0) return;

        SoulStones -= ArenaTuning.SoulStoneDrainPerGameMinute * elapsed;

        if (SoulStones <= 0.0)
        {
            SoulStones = 0.0;

            // 送回：同一方向、半径 255.5 米那一点（落在圆盘内侧 0.5 米）。
            // 兽是被「推回来」的，不是被横移回来的 —— 两点严格同一条射线上。
            var back = BigMapGrid.RadialPoint(Position, BigMapGrid.SendBackRadiusMeters);
            _stepper.ApplyMove(BeastId, back, FacingDegreesTo(back));
            _stepper.Issue(BeastId, NavCommand.Hold(back));

            // 回到停泊态，**不是**终态：送回之后玩家照样能再下航行令，兽照样能再出界。
            Phase = VoyagePhase.Holding;
            TargetCityId = null;
            RouteWaypoint = null;
            ManualTarget = null;   // 送回之后不再朝任何去处走（手动那一口的目标也作废）
        }
    }

    /// <summary>罗盘时钟那一行（开局 <c>Day 1 · 06:00</c>，随游戏分钟连续推进）。</summary>
    public string CompassText() => ArenaTime.CompassAt(Tick());

    /// <summary>状态可查那一口：一行读完阶段、坐标、饱食度与两本小账。</summary>
    public string DebugSegment()
    {
        var at = Position;
        var phase = Phase switch
        {
            VoyagePhase.DetouringToFood => "觅食途中",
            VoyagePhase.Eating => "进食",
            VoyagePhase.ManualEating => "指定进食",
            VoyagePhase.ReturningToWaypoint => "返航",
            VoyagePhase.Outbound => "出界",
            VoyagePhase.Sailing => ManualTarget is null ? "航行" : "手动航行",
            _ => "停泊",
        };

        var waypoint = RouteWaypoint is { } w
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0}", w.X, w.Z)
            : "-";

        // 手动目标点（世界米）：玩家右键点的那一点。没有手动目标时是 "-"。
        var manual = ManualTargetText();

        return string.Format(CultureInfo.InvariantCulture,
            "MAP phase={0} target={1} pos={2:0.0},{3:0.0} sat={4:0.0} speed={5:0.00} soul={6:0.0} food={7:0.0} waypoint={8} terrain={9} manual={10}",
            phase, TargetCityId ?? "-", at.X, at.Z, Satiation, ArenaTuning.BeastSpeedMetersPerSecond * SpeedFactor(),
            SoulStones, GranaryFood, waypoint, BigMapTerrain.NameOf(BigMapTerrain.TerrainAt(at)), manual);
    }

    /// <summary>
    /// 手动目标点那一格的读数（世界米，<c>x,z</c>；没有手动目标是 <c>-</c>）。
    ///
    /// <para><b>这一格文本只有这一处</b>：大地图读数段与检查点快照头部都读它 ——
    /// 两处各拼一遍，改了一处另一处就漂。</para>
    /// </summary>
    public string ManualTargetText()
        => ManualTarget is { } m
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0}", m.X, m.Z)
            : "-";

    // === 私产 ===

    /// <summary>
    /// 沙漠累计计时：<b>每满一个游戏分钟读一次</b>脚下在不在沙漠带里，是在就 +1，不在不动。
    ///
    /// <para>这一格排在 <see cref="DecideStep"/> 的最前，于是它在停摆期与进阶仪式里照走
    /// —— 那两件事的早退在世界那一侧、在 <see cref="DecideStep"/> 之后。拔锚作战与脱队觅食
    /// 也不打断：判据是「在不在沙漠带里」，不是「在干什么」。</para>
    ///
    /// <para><b>判据是「在不在沙漠带里」不是「脚下那一格填的是沙子」</b>：南支路从沙漠带里穿过去，
    /// 城模型改成锚点之后兽停在沙城城门外那一点，脚下正是穿过沙漠带的那一段路 ——
    /// 按「填的是沙子」判，兽在沙城停一整天也攒不出计时，荒漠态就永远解不开锁。
    /// （带子那一块在 <see cref="BigMapTerrain.InDesertBand"/> 里，只有一处定义。）</para>
    /// </summary>
    private void TickDwell(long tick)
    {
        // 记的是「已经走完的那一分钟」：第 9 步走完第 0 个游戏分钟，第 10 步走完第 1 个。
        if ((tick + 1) % ArenaTime.StepsPerGameMinute != 0) return;

        _growth.AdvanceDwell(BigMapTerrain.InDesertBand(Position));
    }

    private void ApplySpeedFactor()
    {
        var factor = SpeedFactor();
        if (Math.Abs(factor - _appliedSpeedFactor) < 1e-12) return;

        _appliedSpeedFactor = factor;
        _stepper.SetMoveSpeed(BeastId, ArenaTuning.BeastSpeedMetersPerSecond * factor);
    }

    private void StepSailing(long tick)
    {
        // 手动那一段：目标是玩家点的那一点，到点即停。
        // **这一段不脱队觅食** —— 玩家在掌舵，饿兽也照玩家点的方向走；
        // 脱队觅食是自动航行（朝城开）那一侧的机制，在下面那一支里。
        if (ManualTarget is { } manual)
        {
            StepManualSail(manual);
            return;
        }

        if (TargetCityId is not { } cityId) return;
        var place = BigMapGrid.PlaceById(cityId);
        if (place is null) return;

        // 到点判在**当前位阶的停靠点**上（城门再外推一个兽半宽），不是城门 —— 兽停在城门外。
        if (NavArrive.Reached(Position, place.StopPoint(_growth.Tier), ArenaTuning.ArriveToleranceMeters))
        {
            Phase = VoyagePhase.Holding;
            _stepper.Issue(BeastId, NavCommand.Hold(place.StopPoint(_growth.Tier)));
            return;
        }

        // 脱队条件两样都成立才走：饿着，且顺路够得着（觅食半径 30 米欧氏）。
        // 无目标返航后一游戏小时内不再自动尝试 —— 冷却只挡自动那一档，手动指定随时可发。
        if (Satiation >= ArenaTuning.DetourSatiationThreshold) return;
        if (_ecology.InForageCooldownAt(tick)) return;

        var node = _ecology.NearestEdible(Position, ArenaTuning.FoodSenseMeters, tick);
        if (node is null)
        {
            var reason = ForageGiveUpReason.NoTargetInRange;
            _ecology.NoteForageGaveUp(tick, reason);
            ForageGaveUp?.Invoke(reason);
            return;
        }

        RouteWaypoint = Position;
        _forageNodeId = node.Id;
        Phase = VoyagePhase.DetouringToFood;
        _stepper.Issue(BeastId, NavCommand.GoTo(node.Position, ArenaTuning.ArriveToleranceMeters));
        ForageStarted?.Invoke(node);
    }

    /// <summary>
    /// 手动那一段：走到玩家点的那一点就停（<see cref="VoyagePhase.Holding"/>）。
    /// 到达容差与通航同一档 —— 玩家点的那一点就是兽停的那一点。
    /// </summary>
    private void StepManualSail(NavPoint target)
    {
        if (!NavArrive.Reached(Position, target, ArenaTuning.ArriveToleranceMeters)) return;

        Phase = VoyagePhase.Holding;
        ManualTarget = null;
        _stepper.Issue(BeastId, NavCommand.Hold(target));
    }

    private void StepDetouring(long tick)
    {
        var node = _ecology.Find(_forageNodeId);
        if (node is null || !node.AvailableAt(tick))
        {
            CancelForage();
            return;
        }

        if (!NavArrive.Reached(Position, node.Position, ArenaTuning.ArriveToleranceMeters)) return;

        Phase = VoyagePhase.Eating;
        _eatUntilTick = tick + (ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);
        _stepper.Issue(BeastId, NavCommand.Hold(node.Position));
        EatStarted?.Invoke(node, true);
    }

    private void StepEating(long tick)
    {
        if (tick < _eatUntilTick) return;

        var nodeId = _forageNodeId;
        _forageNodeId = string.Empty;

        if (!_ecology.TryEat(nodeId, tick, _growth.Form, auto: true, out _))
        {
            CancelForage();
            return;
        }

        // 吃到 80 就返航；没到 80 还有得吃就接着吃 —— 自主觅食是「持续吞食至 80」。
        if (Satiation >= ArenaTuning.SatiationAfterMeal)
        {
            ReturnToWaypoint();
            return;
        }

        var next = _ecology.NearestEdible(Position, ArenaTuning.FoodSenseMeters, tick);
        if (next is null)
        {
            var reason = ForageGiveUpReason.NoTargetLeft;
            _ecology.NoteForageGaveUp(tick, reason);
            ForageGaveUp?.Invoke(reason);
            ReturnToWaypoint();
            return;
        }

        _forageNodeId = next.Id;
        Phase = VoyagePhase.DetouringToFood;
        _stepper.Issue(BeastId, NavCommand.GoTo(next.Position, ArenaTuning.ArriveToleranceMeters));
    }

    /// <summary>
    /// 玩家指定目标、原地吞食五游戏分钟。<b>不改变航线目标</b>：吃完原路接着开。
    ///
    /// <para>只有 Sailing / Holding 两档接得住；觅食途中先召回再发这一口（动词那一侧判）。</para>
    /// </summary>
    /// <returns>这一发受理返回 true；当前阶段接不住返回 false。</returns>
    public bool BeginManualEat(string nodeId, long tick)
    {
        if (Phase is not (VoyagePhase.Sailing or VoyagePhase.Holding)) return false;

        var node = _ecology.Find(nodeId);
        if (node is null) return false;

        _manualResumeCityId = TargetCityId ?? string.Empty;
        _manualResumePoint = ManualTarget;
        RouteWaypoint = null;   // 手动那一口不接觅食断点
        _manualNodeId = nodeId;
        _manualEatUntilTick = tick + (ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);
        Phase = VoyagePhase.ManualEating;
        _stepper.Issue(BeastId, NavCommand.Hold(Position));
        EatStarted?.Invoke(node, false);
        return true;
    }

    private void StepManualEating(long tick)
    {
        if (tick < _manualEatUntilTick) return;

        var nodeId = _manualNodeId;
        _manualNodeId = string.Empty;
        _ecology.TryEat(nodeId, tick, _growth.Form, auto: false, out _);

        // 吃完恢复原来那一趟：有目标城回目标城，有手动点回手动点，都没有就停在原地。
        if (_manualResumeCityId.Length > 0) SailToCity(_manualResumeCityId);
        else if (_manualResumePoint is { } point) SailTo(point.X, point.Z);
        else Phase = VoyagePhase.Holding;

        _manualResumeCityId = string.Empty;
        _manualResumePoint = null;
    }

    /// <summary>新的航行令把「吃完回原处」那一段作废（否则下一次手动进食会回到旧目标）。</summary>
    private void ClearManualEat()
    {
        _manualNodeId = string.Empty;
        _manualEatUntilTick = 0;
        _manualResumeCityId = string.Empty;
        _manualResumePoint = null;
    }

    /// <summary>
    /// 玩家召回：觅食途中随时可以掉头回航线断点。<b>只在觅食状态里有效</b>。
    /// </summary>
    /// <returns>这一发受理返回 true；不在觅食途中返回 false。</returns>
    public bool RecallForage()
    {
        if (Phase is not (VoyagePhase.DetouringToFood or VoyagePhase.Eating)) return false;

        _forageNodeId = string.Empty;
        _ecology.SuppressForage(Tick());
        ForageRecalled?.Invoke();
        ReturnToWaypoint();
        return true;
    }

    /// <summary>吃不成 / 目标没了：清觅食读数并返航。</summary>
    private void CancelForage()
    {
        _forageNodeId = string.Empty;
        ReturnToWaypoint();
    }

    /// <summary>返航：有断点就回断点，没有就直接恢复原目标城。</summary>
    private void ReturnToWaypoint()
    {
        if (RouteWaypoint is { } waypoint)
        {
            Phase = VoyagePhase.ReturningToWaypoint;
            _stepper.Issue(BeastId, NavCommand.GoTo(waypoint, ArenaTuning.ArriveToleranceMeters));
            return;
        }

        ResumeSailing();
    }

    private void StepReturning()
    {
        if (RouteWaypoint is not { } waypoint) return;

        if (!NavArrive.Reached(Position, waypoint, ArenaTuning.ArriveToleranceMeters)) return;

        RouteWaypoint = null;
        ResumeSailing();
    }

    private void StepOutbound()
    {
        if (_outboundTarget is not { } target) return;
        if (NavArrive.Reached(Position, target, ArenaTuning.OutboundArriveToleranceMeters)) return;

        _stepper.Issue(BeastId, NavCommand.GoTo(target, ArenaTuning.OutboundArriveToleranceMeters));
    }

    private void ResumeSailing()
    {
        if (TargetCityId is not { } cityId)
        {
            Phase = VoyagePhase.Holding;
            return;
        }

        var place = BigMapGrid.PlaceById(cityId);
        if (place is null) return;

        Phase = VoyagePhase.Sailing;
        _stepper.Issue(BeastId, NavCommand.GoTo(place.StopPoint(_growth.Tier), ArenaTuning.ArriveToleranceMeters));
    }

    /// <summary>
    /// 当前朝向转多少度能正对 <paramref name="target"/>（度）。<b>只算角度，不挪位置</b> ——
    /// 位移是寻路层那一口（<c>ApplyMove</c>）的事，这里只是给落位那一刻配一个朝向。
    /// </summary>
    private double FacingDegreesTo(NavPoint target)
    {
        var at = Position;
        var dx = target.X - at.X;
        var dz = target.Z - at.Z;
        if (Math.Abs(dx) < 1e-12 && Math.Abs(dz) < 1e-12) return 0.0;

        return Math.Atan2(dx, dz) * (180.0 / Math.PI);
    }
}
