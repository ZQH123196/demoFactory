#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 排兵布阵接寻路层的<b>场地注册口</b> —— 竞技场 ±12 米收成一张 24×24 格、1 米一格的寻路网格，
/// 外加方向换算与两边各校验一道的格长档（「档与表钉死」）。
///
/// <para><b>这一份只管「哪片地形能过」与坐标换算，不管「单位下一步站哪」</b>：
/// 走位是 <c>NavStepper</c> 的活（「寻路单入口」）。本文件不造单位表 ——
/// 身体半径与移速由 <see cref="RadiusOf"/> / <see cref="SpeedOf"/> 两个纯函数现读兵种卡，
/// 公共层那边一个兵种词表都没有。</para>
///
/// <para><b>空间→世界的换算归宿主</b>（ADR 0009，<c>docs/adr/0009</c>）：
/// 寻路网格是空间局部米、原点在角上（格心 <c>(下标+0.5)×格长</c>），
/// 而排兵布阵的世界坐标以中线为 0（±12 米）。空间本身不持有原点与变换，
/// 所以偏移量住在这里，<b>不另立第二份</b>。</para>
///
/// <para><b>格长为什么是 1.0 米</b>（工单 08 定档，数字见 <c>docs/GugaFramework/寻路层.md</c> 7 节）：
/// <list type="bullet">
/// <item>24 格 × 1.0 米 = 竞技场 24×24 米，边界与 <see cref="BattleContract.ArenaHalfWidth"/> 逐字对齐，
/// 「不许出去」由网格外那一圈虚拟阻挡表达（第一期 1.2 节），宿主侧那份矩形夹取随退休一起不需要了；</item>
/// <item>每步位移最大 0.32 米（骑手 3.2 米每秒 ÷ 10）＜ 子步上限 0.5 米 ⇒ 每步一刀切完，
/// 子步不引入任何额外取整；</item>
/// <item>实测净空保守下界在这张网格上的最大保守量 <b>1.2171027843833055 米</b>（第一期推的上界是 √2 ≈ 1.4142），
/// 而布阵起点最远那格（世界 z = 11.35，离墙 0.65 米）净空下界 <b>1.013845853834198</b>，
/// 仍大于最大身体半径 0.75 ⇒ 「半径进净空」这一格在排兵布阵咬不到任何位置。</item>
/// </list></para>
///
/// <para><b>「档与表钉死」：格长与单位卡表钉在一起，两边各校验一道。</b>
/// 层那一侧校验「格长为正、为有限数」（<c>NavGridSpecCheck.CellMeters</c>，非正当场抛，不兜 0）；
/// 本文件这一侧校验三件事，任一条对不上当场抛：
/// <list type="number">
/// <item><b>格数 × 格长 == 竞技场边长</b>（逐字）：网格要正好罩住竞技场，
/// 少一格是「墙内还有一条能走的空隙」，多一格是「场外还有一格能站」；</item>
/// <item><b>格长 ≥ 最大每步位移</b>：一步跨不了一格，子步那一档才真的在解隧穿
/// （这一条是能力前提，不是性能偏好 —— 格长小于步长时子步切得再细也拦不住）；</item>
/// <item><b>最大身体半径 ≤ 布阵最远那格的净空下界</b>：起手就站不下的话，第一条求解直接
/// <c>StartBlocked</c>，而症状离「半径进净空咬到了」隔着一整层。</item>
/// </list></para>
/// </summary>
public static class BattleNavGrid
{
    /// <summary>空间的名字（<see cref="NavSpace"/> 报身份、跨空间抛错时念的就是它）。</summary>
    public static readonly SpaceId Space = new("排兵布阵竞技场");

    /// <summary>
    /// 格长（米）—— <b>寻路网格与净空场的分辨率</b>。工单 08 定档为 1.0 米，
    /// 理由与实测数见类型注释那三条。
    /// </summary>
    public const double CellMeters = 1.0;

    /// <summary>格数（沿 X 与沿 Z 各一份，竞技场是正方形）。24 × 1.0 米 = 24 米。</summary>
    public const int Cells = 24;

    /// <summary>竞技场半宽（米）—— 由 <see cref="BattleContract.ArenaHalfWidth"/> 抄来，格数 × 格长要正好是它的两倍。</summary>
    public const float HalfExtentMeters = BattleContract.ArenaHalfWidth;

    /// <summary>
    /// 建这张网格。<b>空阻挡表是合法声明</b>：排兵布阵这片地没有静态地形阻挡，
    /// 边界那一圈是网格外自带的虚拟阻挡（第一期 1.2 节）。
    /// </summary>
    public static NavGrid Build()
    {
        RequirePinnedCellLength();
        return new NavGrid(new NavGridSpec
        {
            Space = Space,
            CellMeters = CellMeters,
            CellsX = Cells,
            CellsZ = Cells,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = Array.Empty<NavCell>(),
        });
    }

    /// <summary>
    /// 「档与表钉死」 的宿主侧那一道：三条各校验一遍，漂了当场抛（不兜、不静默按一个缺省跑）。
    /// 层那一侧的口径是「格长为正、为有限数」，两边各管各的形状 ——
    /// 层管不了「这个原型的场地多大」，宿主也管不了「一格能不能是负数」。
    /// </summary>
    /// <exception cref="InvalidOperationException">三条里任一条对不上。</exception>
    public static void RequirePinnedCellLength()
    {
        var arena = BattleContract.ArenaHalfWidth * 2.0f;   // 24 米
        var grid = Cells * CellMeters;
        if (Math.Abs(grid - arena) > 1e-9)
        {
            throw new InvalidOperationException(
                $"格数 × 格长 = {Cells.ToString(System.Globalization.CultureInfo.InvariantCulture)} × "
                + $"{CellMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} = "
                + $"{grid.ToString(System.Globalization.CultureInfo.InvariantCulture)} 米，"
                + $"与竞技场边长 {arena.ToString(System.Globalization.CultureInfo.InvariantCulture)} 米对不上 —— "
                + "格长这一档与场地尺寸是钉在一起的一份数（「档与表钉死」），改一个就要改另一个。");
        }

        var worstStep = MaxStepMeters();
        if (CellMeters < worstStep)
        {
            throw new InvalidOperationException(
                $"格长 {CellMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} 米小于最大每步位移 "
                + $"{worstStep.ToString(System.Globalization.CultureInfo.InvariantCulture)} 米 —— "
                + "一步跨一格以上时子步切得再细也拦不住隧穿，那一档的安全保证就没了。");
        }

        var worstRadius = MaxBodyRadiusMeters();
        var gridForRead = BuildGridWithoutPinCheck();
        var clearance = ClearanceAtFarthestDeploymentCell(gridForRead);
        if (clearance < worstRadius)
        {
            throw new InvalidOperationException(
                $"布阵最远那格的净空下界 {clearance.ToString(System.Globalization.CultureInfo.InvariantCulture)} 米"
                + $" < 最大身体半径 {worstRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)} 米 —— "
                + "起手就站不下，第一条求解会直接 StartBlocked，而症状离「半径进净空咬到了」隔着一整层。");
        }
    }

    // === 坐标换算（ADR 0009：空间不带变换，换算归宿主）===

    /// <summary>世界坐标（X 与 Y，中线为 0）→ 空间局部米（格心在下标 + 0.5 处）。</summary>
    public static NavPoint ToGrid(float worldX, float worldY) => new(worldX - GridOrigin, worldY - GridOrigin);

    /// <summary>空间局部米 → 世界坐标。</summary>
    public static (float X, float Y) ToWorld(NavPoint point) => ((float)(point.X + GridOrigin), (float)(point.Z + GridOrigin));

    /// <summary>网格原点在世界坐标上的位置（米，负数）。竞技场半宽取负 ⇒ 格 (0,0) 罩住的是西南角那格。</summary>
    public const float GridOrigin = -HalfExtentMeters;

    // === 兵种卡 → 标量（「寻路单入口」：词表不许进公共层，这一侧现读）===

    /// <summary>该兵种的身体半径（米）—— 身体半径只作为标量递进移动解，不带任何兵种语义。</summary>
    public static double RadiusOf(TroopKind kind) => TroopTable.Of(kind).BodyRadius;

    /// <summary>该兵种的移速（米每秒）。</summary>
    public static double SpeedOf(TroopKind kind) => TroopTable.Of(kind).Speed;

    /// <summary>四个兵种里最大的身体半径（米）—— 格长校验第三条用。</summary>
    public static double MaxBodyRadiusMeters()
    {
        var worst = 0.0;
        foreach (var kind in BattleContract.Kinds)
        {
            var radius = RadiusOf(kind);
            if (radius > worst) worst = radius;
        }

        return worst;
    }

    /// <summary>四个兵种里最大的每步位移（米）= 最大移速 × 一个逻辑步的秒数（「时间基准」：一律 0.1 秒）。</summary>
    public static double MaxStepMeters()
    {
        var worst = 0.0;
        foreach (var kind in BattleContract.Kinds)
        {
            var step = SpeedOf(kind) * BattleContract.StepSeconds;
            if (step > worst) worst = step;
        }

        return worst;
    }

    /// <summary>
    /// 布阵最远那一格中心（世界坐标）—— 两方里离墙最远的那个格心。
    /// 取「最远一排 × 最远一列」不是取实测最远点：布阵是规则摆位，
    /// 那一格就是公式能摆到的最远处，格心好算也好复核。
    /// </summary>
    public static (float X, float Y) FarthestDeploymentCellCenter()
    {
        var x = BattleContract.CellCenter(Side.Player, BattleContract.Rows - 1, BattleContract.Columns - 1);
        return (x.X, x.Y);
    }

    /// <summary>布阵最远那一格中心的净空保守下界（米）—— 格长校验第三条读的就是它。</summary>
    public static double ClearanceAtFarthestDeploymentCell(NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (x, y) = FarthestDeploymentCellCenter();
        return grid.Clearance.ConservativeLowerBoundAt(ToGrid(x, y));
    }

    /// <summary>造网格但跳过格长校验 —— 只给校验自己内部量净空用（否则递归）。</summary>
    private static NavGrid BuildGridWithoutPinCheck()
        => new(new NavGridSpec
        {
            Space = Space,
            CellMeters = CellMeters,
            CellsX = Cells,
            CellsZ = Cells,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = Array.Empty<NavCell>(),
        });
}
