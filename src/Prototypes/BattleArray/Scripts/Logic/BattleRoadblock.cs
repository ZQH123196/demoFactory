#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BattleArray.Logic;

// 路障在建造层那一侧的「原型数据」全在这一份里：
//   ① 蓝图一条（血量上限 / 占地 / 工期 / 模型引用），模块构造时一次递入；
//   ② 建造格 ↔ 寻路格的换算 —— 换算归宿主（ADR 0009），层里不认识建造规则，也不知道这座占哪几格。
//
// 这一条蓝图是本原型唯一的建筑：赛前路障。它只在布阵期放得下（前置条件在 BattleWorld 那一侧），
// 不进经济（模块不认识钱），也没有任何攻击路径（这一侧一条打建筑的写口都没接）。

/// <summary>
/// 赛前路障的蓝图与两边网格的换算 —— <b>网格放置</b>（事件自带的格名单就是它的占地）。
///
/// <para><b>四个数据档</b>：占地 = 单格（锚点 + 无偏移）、方向段恒 0、工期 0 步、血量上限 5000。
/// 工期 0 走的是模块那条「工期 0 无特判」的流水线：放下那一刻就翻完工并连发 <c>completed</c>，
/// 于是「它建成了没有」在事件流上读得出，不是靠「工期是 0 就当它成了」这句推断。</para>
///
/// <para><b>血量为什么取大值</b>：这一轮没有东西打它（无攻击路径），血量是个占位读数。
/// 取 5000 是为了让「它掉过血」一眼看得出来 —— 真有哪个沾伤害的口子误伤了它，
/// 读数上当场显形，不必等一座 260 血的路障被打没才发现。</para>
///
/// <para><b>模型引用留空串</b>：这一侧不摆模型、也不画幽灵。没有真模型就不画
/// （「真模型」：画了就得走造型线四件套），表现留给垂直切片 —— 这条待确认项记在原型文档的待确认区。</para>
/// </summary>
public static class BattleRoadblock
{
    /// <summary>蓝图 id（动词表第一段那个 token；建筑 id 由模块按登记序发，是另一列）。</summary>
    public const string BlueprintId = "roadblock";

    /// <summary>血量上限（大值，理由见类型注释）。算术一律 double（「double 与不变文化」）。</summary>
    public const double MaxHealth = 5000;

    /// <summary>工期 0 步：放下那一刻就建成。</summary>
    public const long BuildDurationTicks = 0;

    /// <summary>方向段恒 0（八值编码里的 rot0）—— 单格占地转哪个方向都占同一格，方向在这一条上没有信息量。</summary>
    public const int Orientation = GridOrientation.R0;

    /// <summary>建造格的格长（米）—— <b>与寻路网格同一档</b>（「档与表钉死」：一处定档、两处读同一个数）。</summary>
    public const double CellMeters = BattleNavGrid.CellMeters;

    /// <summary>
    /// 建造格与寻路格的下标差：建造格以世界中线为 0（<c>floor(世界坐标 ÷ 格长)</c>），
    /// 寻路网格以场地西南角为 0。<b>差几格由 <see cref="BattleNavGrid.GridOrigin"/> 推出来</b>，
    /// 不另写一个 12（写死一个数就是第二份真值）。
    /// </summary>
    public const int NavColumnOffset = (int)(-BattleNavGrid.GridOrigin / BattleNavGrid.CellMeters);

    /// <summary>建造格的最左列（含）—— 世界 X = −12 米那一列。</summary>
    public const int MinCol = -NavColumnOffset;

    /// <summary>建造格的最右列（含）—— 世界 X = 11 米那一列。</summary>
    public const int MaxCol = NavColumnOffset - 1;

    /// <summary>建造格的最下行（含）。竞技场是正方形，行与列同档。</summary>
    public const int MinRow = MinCol;

    /// <summary>建造格的最上行（含）。</summary>
    public const int MaxRow = MaxCol;

    /// <summary>单格占地：锚点格 + 无偏移（<see cref="CellFootprint.FromCells"/> 归一之后就是它）。</summary>
    public static readonly CellFootprint Footprint = CellFootprint.FromCells([new GridCell(0, 0)]);

    /// <summary>这一条蓝图（模块构造时一次递入）。</summary>
    public static readonly Blueprint Blueprint = new(BlueprintId, MaxHealth, Footprint, BuildDurationTicks);

    /// <summary>本原型的蓝图表（只有路障这一条）。</summary>
    public static readonly IReadOnlyList<Blueprint> All = [Blueprint];

    /// <summary>
    /// 造一条网格放置规则。格长与边界都钉在上面那几个常量上，<b>构造之前先校验一遍</b>
    /// （「档与表钉死」的宿主侧那一道，与 <see cref="BattleNavGrid.RequirePinnedCellLength"/> 同款）。
    /// </summary>
    public static GridPlacementRule NewRule()
    {
        RequirePinnedBounds();
        return new GridPlacementRule(CellMeters, MinCol, MinRow, MaxCol, MaxRow);
    }

    /// <summary>
    /// 「档与表钉死」 的宿主侧那一道：两条各校验一遍，漂了当场抛（不兜、不静默按一个缺省跑）。
    /// <list type="number">
    /// <item><b>格数 × 格长 == 竞技场边长</b>：少一格是「墙边还有一条能放的空隙」，多一格是「场外也放得下」；</item>
    /// <item><b>两端换算落在寻路网格的第一格与最后一格</b>：对不上的话，建造格与寻路格各说各的坐标，
    /// 而症状是「路障落在旁边那一格」——离「换算写错了」隔着两层。</item>
    /// </list>
    /// </summary>
    /// <exception cref="InvalidOperationException">两条里任一条对不上。</exception>
    public static void RequirePinnedBounds()
    {
        var arena = BattleContract.ArenaHalfWidth * 2.0;
        var count = MaxCol - MinCol + 1;
        var grid = count * CellMeters;
        if (Math.Abs(grid - arena) > 1e-9)
        {
            throw new InvalidOperationException(
                $"建造格格数 × 格长 = {Number(count)} × {Number(CellMeters)} = {Number(grid)} 米，"
                + $"与竞技场边长 {Number(arena)} 米对不上 —— 格长这一档与场地尺寸是钉在一起的一份数"
                + "（「档与表钉死」），改一个就要改另一个。");
        }

        var first = ToNavCell(new GridCell(MinCol, MinRow));
        var last = ToNavCell(new GridCell(MaxCol, MaxRow));
        if (first.X != 0 || first.Z != 0 || last.X != BattleNavGrid.Cells - 1 || last.Z != BattleNavGrid.Cells - 1)
        {
            throw new InvalidOperationException(
                $"建造格 {new GridCell(MinCol, MinRow)} 换算成寻路格 {first}、"
                + $"{new GridCell(MaxCol, MaxRow)} 换算成 {last}，"
                + $"而这张寻路网格的角是 (0,0) 与 ({Number(BattleNavGrid.Cells - 1)},{Number(BattleNavGrid.Cells - 1)}) —— "
                + "两边对不上的话，路障会落在旁边那一格。");
        }
    }

    /// <summary>建造格 → 寻路格（栅格化那一半；换算归宿主，ADR 0009）。</summary>
    public static NavCell ToNavCell(GridCell cell) => new(cell.Col + NavColumnOffset, cell.Row + NavColumnOffset);

    /// <summary>世界坐标 → 建造格（<c>floor</c>，与放置规则那一份算术同款）。</summary>
    public static GridCell CellAt(float worldX, float worldY)
        => GridPlacementRule.CellAt(new System.Numerics.Vector2(worldX, worldY), CellMeters);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
