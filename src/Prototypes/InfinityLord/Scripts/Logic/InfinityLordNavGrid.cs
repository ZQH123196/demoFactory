#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 无限领主接寻路层的<b>场地注册口</b> —— 整块战场收成一张 110 × 80 格、
/// 1 米一格的寻路网格，外加坐标换算、圆 → 格的光栅化与两边各校验一道的格长档
/// （「档与表钉死」）。
///
/// <para><b>这一份只管「哪片地形能过」与坐标换算，不管「单位下一步站哪」</b>：
/// 走位是 <see cref="NavStepper"/> 的活（「寻路单入口」）。本文件不造单位表 ——
/// 身体半径与移速由 <see cref="RadiusMeters"/> 现读，公共层那边一个英雄词表都没有。</para>
///
/// <para><b>空间 → 世界的换算归宿主</b>（ADR 0009）：网格是空间局部米、原点在角上
/// （格心 = <c>(下标 + 0.5) × 格长</c>），而本原型的坐标就是世界坐标（战场以 (0,0) 为原点）。
/// 空间本身不持有原点与变换，所以偏移量住在这里，<b>不另立第二份</b>。</para>
///
/// <para><b>格长为什么是 1.0 米</b>（与排兵布阵、巨兽那两档同值，理由各自量过）：
/// <list type="bullet">
/// <item>110 格 × 1.0 米 = 战场宽 110 米、进深 80 米（X∈[-20,90]、Z∈[-30,50]），
/// 边界罩住全部场地建筑与波次入侵点 (85,5)（终幕巢按修正移速重定档后落在 (35,30)，也在界内）；</item>
/// <item>全场最大的每步位移是游侠的 <b>0.16 米</b>（1.6 米每秒 ÷ 10）＜ 子步上限 0.5 米，
/// 子步在这一场是空转，不引入额外取整；</item>
/// <item>建筑半径 6 米 ⇒ 场地建筑之间最窄的那条缝（宫殿↔行会 8 米）也留得下身体半径 0.5 米。</item>
/// </list></para>
///
/// <para><b>战场上没有静态地形阻挡</b>：<c>BlockedCells</c> 是空表，那是「明确没有地形阻挡」
/// 这一种合法声明。边界那一圈是网格自带的虚拟阻挡，不占表里的格。
/// 运行时落下的阻挡来自建筑占地的圆形光栅化（<see cref="CircleCells"/>，每步推进之前读一次事件流）。</para>
/// </summary>
public static class InfinityLordNavGrid
{
    /// <summary>空间的名字（<c>NavSpace</c> 报身份、跨空间抛错时念的就是它）。</summary>
    public static readonly SpaceId Space = new("无限领主战场");

    /// <summary>格长（米）—— 寻路网格与净空场的分辨率。理由见类型注释那三条。</summary>
    public const double CellMeters = 1.0;

    /// <summary>战场在 X 方向的西边界（米，世界坐标）。</summary>
    public const double MinX = -20.0;

    /// <summary>战场在 Z 方向的南边界（米，世界坐标）。</summary>
    public const double MinZ = -30.0;

    /// <summary>沿 X 的格数（110 格 × 1.0 米 = 110 米，罩住 X ∈ [-20, 90]）。</summary>
    public const int CellsX = 110;

    /// <summary>沿 Z 的格数（80 格 × 1.0 米 = 80 米，罩住 Z ∈ [-30, 50]）。</summary>
    public const int CellsZ = 80;

    /// <summary>战场在 X 方向的东边界（米，世界坐标）。</summary>
    public const double MaxX = MinX + (CellsX * CellMeters);

    /// <summary>战场在 Z 方向的北边界（米，世界坐标）。</summary>
    public const double MaxZ = MinZ + (CellsZ * CellMeters);

    /// <summary>身体半径（米）—— 只作为标量递进移动解，不带任何兵种语义。</summary>
    public const double BodyRadiusMeters = 0.5;

    /// <summary>
    /// 建这张网格。<b>空阻挡表是合法声明</b>：战场上没有静态地形阻挡，
    /// 边界那一圈是网格外自带的虚拟阻挡。建筑占地由 <c>NavTerrainLink</c> 每步落上来。
    /// </summary>
    public static NavGrid Build()
    {
        RequirePinnedCellLength();
        return BuildWithoutPinCheck();
    }

    /// <summary>
    /// 「档与表钉死」 的宿主侧那一道：四条各校验一遍，漂了当场抛（不兜、不静默按一个缺省跑）。
    /// 层那一侧的口径是「格长为正、为有限数」，两边各管各的形状 ——
    /// 层管不了「这块战场多大」，宿主也管不了「一格能不能是负数」。
    /// </summary>
    /// <exception cref="InvalidOperationException">四条里任一条对不上。</exception>
    public static void RequirePinnedCellLength()
    {
        var worstStep = MaxStepMeters();
        if (CellMeters < worstStep)
        {
            throw new InvalidOperationException(
                $"格长 {CellMeters.ToString(CultureInfo.InvariantCulture)} 米小于最大每步位移 "
                + $"{worstStep.ToString(CultureInfo.InvariantCulture)} 米 —— "
                + "一步跨一格以上时子步切得再细也拦不住隧穿，那一档的安全保证就没了。");
        }

        // 场地建筑的落点与三个远端锚点都要在网格内：落在网格外的建筑占不到任何一格，
        // 症状与「这座楼不挡路」一模一样（本场地建筑半径 6 米，出界 6 米以上才完全没有格）。
        RequireInside(new Vector2(85, 5), "波次入侵点");
        RequireInside(new Vector2(85, 45), "BOSS 巢");
        RequireInside(new Vector2(60, 20), "巢穴 lair1");
        RequireInside(new Vector2(65, -25), "巢穴 lair2");
    }

    /// <summary>一个世界坐标点在不在战场网格内，不在就当场抛（读数带出那一点的坐标与边界）。</summary>
    /// <exception cref="InvalidOperationException">点在网格外。</exception>
    public static void RequireInside(Vector2 at, string what)
        => RequireInside(at.X, at.Y, what);

    /// <summary>同上，按两个分量给（不经过 <c>Vector2</c> 的调用点用这一档）。</summary>
    /// <exception cref="InvalidOperationException">点在网格外。</exception>
    public static void RequireInside(float x, float z, string what)
    {
        if (x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{what} ({x.ToString(CultureInfo.InvariantCulture)}, {z.ToString(CultureInfo.InvariantCulture)}) "
            + $"落在战场网格外（X ∈ [{MinX.ToString(CultureInfo.InvariantCulture)}, {MaxX.ToString(CultureInfo.InvariantCulture)}]、"
            + $"Z ∈ [{MinZ.ToString(CultureInfo.InvariantCulture)}, {MaxZ.ToString(CultureInfo.InvariantCulture)}]）—— "
            + "网格外没有格，那里的求解直接报地形无路，而症状会读成「它站着不动」。");
    }

    // === 坐标换算（ADR 0009：空间不带变换，换算归宿主）===

    /// <summary>世界坐标 → 空间局部米（格心在下标 + 0.5 处）。</summary>
    public static NavPoint ToGrid(double x, double z) => new(x - MinX, z - MinZ);

    /// <summary>空间局部米 → 世界坐标。</summary>
    public static (double X, double Z) ToWorld(NavPoint point) => (point.X + MinX, point.Z + MinZ);

    /// <summary>世界坐标那一点在不在网格内。</summary>
    public static bool Contains(double x, double z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    // === 单位标量（「寻路单入口」：词表不许进公共层，这一侧现读）===

    /// <summary>
    /// 战场里最大的每步位移（米）= 最大移速 × 一个逻辑步的秒数（「时间基准」：一律 0.1 秒）。
    /// 参与比较的是游侠的 1.6 米每秒（骑士 1.3、战士 1.2、BOSS 护卫 1.1 都在它之下）。
    /// </summary>
    public static double MaxStepMeters() => 1.6 * 0.1;

    /// <summary>
    /// 圆 → 寻路格的光栅化（米进、格出）—— <b>判据是「格心落在圆内」</b>。
    ///
    /// <para>这一格归宿主（ADR 0009「换算归宿主」）：寻路层不认识圆形占地，建造层给的是圆
    /// （裁决里的半径），两边之间的这一步换算是场地自己的事。建筑靠它落阻挡 ——
    /// 名单为空就与「没砌墙」长得一模一样。</para>
    ///
    /// <para>顺序是「先列后行、行列都从下界起」：印子的次序因此逐字确定，
    /// 重放比对与快照都读得到同一串。半径 ≤ 0 回空表（那是一座不占格的建筑）。</para>
    /// </summary>
    /// <param name="grid">要光栅化的那张网格。</param>
    /// <param name="centerX">圆心的 X（空间局部米）。</param>
    /// <param name="centerZ">圆心的 Z（空间局部米）。</param>
    /// <param name="radius">半径（米）。</param>
    public static IReadOnlyList<NavCell> CircleCells(NavGrid grid, double centerX, double centerZ, double radius)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (radius <= 0.0)
        {
            return Array.Empty<NavCell>();
        }

        var step = grid.CellMeters;
        var minX = (int)Math.Floor((centerX - radius) / step);
        var maxX = (int)Math.Floor((centerX + radius) / step);
        var minZ = (int)Math.Floor((centerZ - radius) / step);
        var maxZ = (int)Math.Floor((centerZ + radius) / step);

        var cells = new List<NavCell>();
        for (var x = minX; x <= maxX; x++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                var cell = new NavCell(x, z);
                if (!grid.IsInside(cell)) continue;

                var at = grid.CenterOf(cell);
                var dx = at.X - centerX;
                var dz = at.Z - centerZ;
                if (Math.Sqrt((dx * dx) + (dz * dz)) > radius) continue;

                cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>
    /// 一座建筑此刻在寻路网格里占哪几格（世界坐标进、寻路格出）。
    /// <b>只给 <c>NavTerrainLink</c> 那个查法用</b>：层不认识圆形占地，也不认识建造规则。
    /// </summary>
    /// <param name="grid">落地形的那张网格。</param>
    /// <param name="center">建筑中心（世界坐标）。</param>
    /// <param name="radius">占地半径（米，蓝图数据）。</param>
    public static IReadOnlyList<NavCell> BuildingCells(NavGrid grid, Vector2 center, double radius)
    {
        var at = ToGrid(center.X, center.Y);
        return CircleCells(grid, at.X, at.Z, radius);
    }

    /// <summary>
    /// 建筑中心朝 <paramref name="toward"/> 那一侧的<b>靠近点</b>（世界坐标）——
    /// 想跟这座楼打交道的单位从这一侧走过来。
    ///
    /// <para><b>为什么必须外移</b>：建筑占地是阻挡格，路径点是楼心时目标格被它自己挡住 ——
    /// 求解永远到不了位，单位站在原地不动而读数说它在走（巨兽那一场实测出来的那一格）。
    /// 外移量 = 半径 + <see cref="DoorMarginMeters"/>，<b>离墙一段余量是给净空场的</b>：
    /// 紧贴阻挡格的格心进不了可行域，而症状同样是「它站着不动」。</para>
    ///
    /// <para><b>它只是候选，不是落点</b>：本场地的镇子很挤（宫殿↔行会 8 米，半径各 6 米），
    /// 一个方向上的靠近点经常落进隔壁那座的占地里。真正当路径点用的是
    /// <see cref="DoorPoint"/> —— 它在候选的八个方向与几档半径里挑一个站得下的。</para>
    /// </summary>
    /// <param name="center">建筑中心（世界坐标）。</param>
    /// <param name="radius">占地半径（米）。</param>
    /// <param name="toward">从哪一侧靠近（世界坐标）—— 取楼心指向它的方向，方向退化成零时取 +X。</param>
    public static Vector2 ApproachPoint(Vector2 center, double radius, Vector2 toward)
        => center + (DirectionFrom(center, toward) * (float)(radius + DoorMarginMeters));

    /// <summary>
    /// 站得下的<b>门口点</b>（世界坐标）—— 移动层拿它当路径点，「够不够得着」那条线
    /// 读的是 <see cref="InteractionRangeMeters"/>，两处同一个数。
    ///
    /// <para><b>为什么不能只算一个径向点</b>：镇子里两座楼的占地会互相咬
    /// （宫殿 (-8,0) 与行会 (0,0) 相距 8 米、半径各 6 米，宫殿朝行会那一侧的靠近点
    /// 直接落进行会的占地里）。那时目标格进不了可行域，求解报「地形无路」，
    /// 而症状读成「它站着不动」。所以这里沿八个方向 × 几档半径找第一个站得下的点。</para>
    ///
    /// <para><b>次序写死、结果逐字确定</b>：先按「单位那一侧」的方向，再按 ±22.5° / ±45° /
    /// ±67.5° / 反方向铺开；半径从「占地半径 + 门口余量」起，每档加 1 米。
    /// 一个都站不下时退回最初那个候选点（照实交出去，让求解报它自己的读数），
    /// <b>不返回楼心</b> —— 楼心是本场地唯一确定站不下的那一点。</para>
    /// </summary>
    /// <param name="grid">要判站得下站不下的那张网格。</param>
    /// <param name="center">建筑中心（世界坐标）。</param>
    /// <param name="radius">占地半径（米）。</param>
    /// <param name="toward">从哪一侧靠近（世界坐标）。</param>
    public static Vector2 DoorPoint(NavGrid grid, Vector2 center, double radius, Vector2 toward)
        => SearchEdge(grid, center, radius, DoorMarginMeters, DirectionFrom(center, toward));

    /// <summary>
    /// 站得下的<b>墙脚</b>（世界坐标）—— 想跟这座楼<b>动手</b>的单位走到这里就够得着。
    ///
    /// <para>与 <see cref="DoorPoint"/> 同一条搜索，只有起始半径不同：这里从占地半径本身起，
    /// 于是落点是<b>贴着墙</b>的那一格，而不是离墙一米的门口。
    /// 攻坚与袭城要的是这一档 —— 它们的停手线是「离楼心 攻击射程 + 1 米」（怪物 2.2 米），
    /// 整条落在半径 6 米的占地里面；路径点摆在门口（离楼心 7 米）的话单位判到位却打不着，
    /// 读数是「它到了」而城池一滴血不掉。</para>
    /// </summary>
    /// <param name="grid">要判站得下站不下的那张网格。</param>
    /// <param name="center">建筑中心（世界坐标）。</param>
    /// <param name="radius">占地半径（米）。</param>
    /// <param name="toward">从哪一侧靠近（世界坐标）。</param>
    public static Vector2 EdgePoint(NavGrid grid, Vector2 center, double radius, Vector2 toward)
        => SearchEdge(grid, center, radius, 0.0, DirectionFrom(center, toward));

    /// <summary>
    /// 同上，但方向直接给（<b>产出链落单位走这一档</b>：方向出自世界的带种子随机数，
    /// 而落点仍要按「站得下」挑出来 —— 随机数给的是「哪一侧」，不是「踩在墙上还是地上」）。
    /// </summary>
    /// <param name="grid">要判站得下站不下的那张网格。</param>
    /// <param name="center">建筑中心（世界坐标）。</param>
    /// <param name="radius">占地半径（米）。</param>
    /// <param name="direction">朝哪一侧（不必是单位向量；长度为零时取 +X）。</param>
    public static Vector2 DoorPointAlong(NavGrid grid, Vector2 center, double radius, Vector2 direction)
        => SearchEdge(grid, center, radius, DoorMarginMeters, direction);

    /// <summary>
    /// 楼心朝某一侧、站得下的那一点（世界坐标）—— 门口点与墙脚共用这一条搜索。
    ///
    /// <para><b>为什么不能只算一个径向点</b>：镇子里两座楼的占地会互相咬
    /// （宫殿 (-8,0) 与行会 (0,0) 相距 8 米、半径各 6 米，宫殿朝行会那一侧的径向点
    /// 直接落进行会的占地里）。那时目标格进不了可行域，求解报「地形无路」，
    /// 而症状读成「它站着不动」。所以这里沿八个方向 × 几档半径找第一个站得下的点。</para>
    ///
    /// <para><b>次序写死、结果逐字确定</b>：先按「单位那一侧」的方向，再按 ±22.5° / ±45° /
    /// ±67.5° / 反方向铺开；半径从起始那一档起，每档加 1 米。
    /// 一个都站不下时退回最初那个候选点（照实交出去，让求解报它自己的读数），
    /// <b>不返回楼心</b> —— 楼心是本场地唯一确定站不下的那一点。</para>
    /// </summary>
    /// <param name="grid">要判站得下站不下的那张网格。</param>
    /// <param name="center">建筑中心（世界坐标）。</param>
    /// <param name="radius">占地半径（米）。</param>
    /// <param name="margin">起点离占地半径还有多少余量（米；门口点那一档是 <see cref="DoorMarginMeters"/>，墙脚是 0）。</param>
    /// <param name="direction">朝哪一侧（不必是单位向量；长度为零时取 +X）。</param>
    private static Vector2 SearchEdge(NavGrid grid, Vector2 center, double radius, double margin, Vector2 direction)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (direction.LengthSquared() < 1e-12f)
        {
            direction = new Vector2(1f, 0f);
        }
        else
        {
            direction = Vector2.Normalize(direction);
        }

        var first = center + (direction * (float)(radius + margin));
        var baseAngle = Math.Atan2(direction.Y, direction.X);

        for (var ring = 0; ring <= DoorSearchRings; ring++)
        {
            var distance = radius + margin + (ring * DoorSearchStepMeters);
            for (var k = 0; k < DoorSearchSectors.Length; k++)
            {
                var angle = baseAngle + DoorSearchSectors[k];
                var at = center + new Vector2(
                    (float)(Math.Cos(angle) * distance),
                    (float)(Math.Sin(angle) * distance));
                if (Standable(grid, at))
                {
                    return at;
                }
            }
        }

        return first;
    }

    /// <summary>楼心指向 <paramref name="toward"/> 的单位方向；退化成零时取 +X。</summary>
    private static Vector2 DirectionFrom(Vector2 center, Vector2 toward)
    {
        var direction = toward - center;
        return direction.LengthSquared() < 1e-12f ? new Vector2(1f, 0f) : Vector2.Normalize(direction);
    }


    /// <summary>
    /// 那一点站得下站不下：落在网格内、所在格没被封、且净空下界放得下身体半径。
    /// 三条缺一不可 —— 少了净空那一条，「贴墙的候选点」会被当成好点，
    /// 而症状是求解报地形无路、单位站在原地不动。
    ///
    /// <para><b>判完要把那个点换算回世界坐标再回答</b>：<c>TryLocal</c> 收的是空间局部米，
    /// 而 <c>CenterOf</c> 交回的也是空间局部米 —— 拿着它去跟世界坐标比，是在两个坐标系之间比，
    /// 那一步会把「网格东边界」读成世界 X = 110，于是任何点都判成界内（实测踩过这一处）。</para>
    /// </summary>
    /// <param name="grid">判的那张网格。</param>
    /// <param name="at">候选点（世界坐标）。</param>
    public static bool Standable(NavGrid grid, Vector2 at)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var local = ToGrid(at.X, at.Y);
        if (!grid.Contains(local))
        {
            return false;
        }

        if (grid.IsBlocked(CellOf(grid, local)))
        {
            return false;
        }

        return grid.Clearance.ConservativeLowerBoundAt(local) >= BodyRadiusMeters;
    }

    /// <summary>空间局部米落在哪一格（调用方已经判过界内）。</summary>
    private static NavCell CellOf(NavGrid grid, NavPoint local)
    {
        var cx = (int)Math.Floor(local.X / grid.CellMeters);
        var cz = (int)Math.Floor(local.Z / grid.CellMeters);
        return new NavCell(cx, cz);
    }

    /// <summary>门口点在占地半径之外的那段余量（米）。1.0 米 ⇒ 半径 6 的楼候选点落在离楼心 7 米处。</summary>
    public const double DoorMarginMeters = 1.0;

    /// <summary>门口点找不着时往外加的那一档（米）。</summary>
    public const double DoorSearchStepMeters = 1.0;

    /// <summary>最多往外找几档（0 那一档就是「占地半径 + 余量」）。</summary>
    public const int DoorSearchRings = 6;

    /// <summary>
    /// 门口点先试哪几个方向（弧度，加在「单位那一侧」的方向上）。
    /// 次序写死 ⇒ 同一个局面每次挑到同一个点（「确定性靠结构」）。
    /// </summary>
    public static readonly double[] DoorSearchSectors =
    [
        0.0,
        0.39269908169872414,   // +22.5°
        -0.39269908169872414,  // −22.5°
        0.7853981633974483,    // +45°
        -0.7853981633974483,   // −45°
        1.1780972450961724,    // +67.5°
        -1.1780972450961724,   // −67.5°
        3.141592653589793,     // 反方向
    ];

    /// <summary>
    /// 想跟这座楼打交道时「算不算到了」的那条线（米，离楼心）——半径 + 2 米。
    /// 与 <see cref="ApproachPoint"/> 同源：候选点在半径 + 1 米处，再留 1 米余量，
    /// 于是「走到了」蕴含「够得着」，两处不会各判各的。
    /// </summary>
    /// <param name="radius">占地半径（米）。</param>
    public static double InteractionRangeMeters(double radius) => radius + 2.0;

    /// <summary>造网格但跳过格长校验 —— 把校验与构造分开，校验自己内部判形状时不必递归。</summary>
    private static NavGrid BuildWithoutPinCheck()
        => new(new NavGridSpec
        {
            Space = Space,
            CellMeters = CellMeters,
            CellsX = CellsX,
            CellsZ = CellsZ,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = Array.Empty<NavCell>(),
        });
}
