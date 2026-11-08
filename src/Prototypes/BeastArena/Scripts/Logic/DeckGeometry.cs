#nullable enable
using System;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 甲板坐标只有一套，两边网格差一个平移 —— 换算收在这一个文件里。
//
// 两张网格的格长都是 <see cref="DeckBlueprints.CellSize"/> 米，但原点不同：
//   · 建造模块收的是 `Vector2` 落点，格心落在 `(格号 × 格长 + 半格)`，一阶建造格 -12..11
//     落成甲板逻辑坐标 -23..23（spec 里那些 `(0, 2)`、`(-1.5, -6)`、`(11.5, 0)` 都在这一档）；
//     二阶扩容把格号外扩到 -14..13，同一格的逻辑落点一寸不动 —— 落点只由格号 × 格长推出，
//     与边界无关，这就是「既有建筑与布局不搬迁」的那一条规则。
//   · 甲板寻路网格恒按最大档 28×28 格建、原点在角上，同一格的心在 `(格号 × 格长 + 半格)`。
// 两边差一个恒定的平移：按格数是 <see cref="DeckBlueprints.NavOffsetCells"/>（14 格），
// 折成米就是 <see cref="Offset"/>（14 格 × 2 米 = 28 米）——
// 格号平移（<see cref="DeckBlueprints.ToNavCell"/>）与米平移（<see cref="Offset"/>）同源，
// 那一项只写一次：改了格长两处一起走，扩容也不改变这两处的差。
//
// 所以：**逻辑里说甲板坐标，进寻路层之前过一次 <see cref="Nav"/>**。
// 反向那一口只给调试读数用（快照里印的是甲板坐标）。

/// <summary>
/// 甲板逻辑坐标（随位阶 48 / 56 米见方）与甲板寻路空间坐标（恒 56 米见方）之间那一个平移，
/// 外加 spec 钉死的那几处甲板几何点。边界随位阶走的读数都从「当前格数」参数推，不留第二份。
/// </summary>
public static class DeckGeometry
{
    /// <summary>
    /// 两张网格之间差的那个平移（米，沿 X 与沿 Z 各一份）。
    /// <b>由格平移乘上格长算出</b>，不另写一个字面量：格长一改，这里跟着走。
    /// </summary>
    public const double Offset = DeckBlueprints.NavOffsetCells * DeckBlueprints.CellSize;

    /// <summary>
    /// 当前 <paramref name="cells"/> 格档下，甲板最外一格的格心（甲板逻辑坐标，沿 X 与沿 Z 各一份，正负对称）。
    /// 防守刷怪落点与「出界退场往哪儿走」都落在这一格上，所以它得跟着格长与位阶走。
    /// </summary>
    public static double EdgeCellCenter(int cells)
        => (ArenaTuning.BuildMaxCol(cells) + 0.5) * DeckBlueprints.CellSize;

    /// <summary>甲板逻辑坐标 → 甲板寻路空间坐标（唯一那口写口）。</summary>
    public static NavPoint Nav(double deckX, double deckZ) => new(deckX + Offset, deckZ + Offset);

    /// <summary>甲板寻路空间坐标 → 甲板逻辑坐标（只给读数与快照用）。</summary>
    public static double DeckXOf(NavPoint at) => at.X - Offset;

    /// <summary>甲板寻路空间坐标 → 甲板逻辑坐标（只给读数与快照用）。</summary>
    public static double DeckZOf(NavPoint at) => at.Z - Offset;

    /// <summary>建造格号 → 甲板寻路格号（与 <see cref="DeckBlueprints.ToNavCell"/> 同一份口径）。</summary>
    public static NavCell NavCellOf(int col, int row)
        => new(col + DeckBlueprints.NavOffsetCells, row + DeckBlueprints.NavOffsetCells);

    /// <summary>
    /// 拔锚防守四边中点上的刷怪落点（甲板逻辑坐标，按 <paramref name="index"/> 取模 4 轮转）。
    /// 次序固定：北、南、西、东 —— 重放要逐字可复现靠的就是这一条。
    /// </summary>
    public static NavPoint DefenceSpawnPointAt(int index, int cells)
    {
        var edge = EdgeCellCenter(cells);
        return (index & 3) switch
        {
            0 => new NavPoint(0.0, -edge),
            1 => new NavPoint(0.0, edge),
            2 => new NavPoint(-edge, 0.0),
            _ => new NavPoint(edge, 0.0),
        };
    }

    /// <summary>
    /// 竞技场西侧入场点（甲板逻辑坐标）—— <b>相对解析出的那一栋的大门取</b>。
    /// 不传门点的那一档按开局固定竞技场算（与旧读数逐字相同）。
    /// </summary>
    public static NavPoint ArenaWest(NavPoint? door = null)
    {
        var at = door ?? StartingDoorOf("Arena");
        return new NavPoint(at.X + ArenaTuning.ArenaWestOffsetX, at.Z + ArenaTuning.ArenaWestOffsetZ);
    }

    /// <summary>竞技场东侧入场点（同上，相对大门点）。</summary>
    public static NavPoint ArenaEast(NavPoint? door = null)
    {
        var at = door ?? StartingDoorOf("Arena");
        return new NavPoint(at.X + ArenaTuning.ArenaEastOffsetX, at.Z + ArenaTuning.ArenaEastOffsetZ);
    }

    /// <summary>在岗牧师驻的场边点（同上，相对大门点）。</summary>
    public static NavPoint PriestEdge(NavPoint? door = null)
    {
        var at = door ?? StartingDoorOf("Arena");
        return new NavPoint(at.X + ArenaTuning.PriestEdgeOffsetX, at.Z + ArenaTuning.PriestEdgeOffsetZ);
    }

    /// <summary>开局固定建筑的大门点（甲板逻辑坐标）；不在开局六栋里回 (0,0)。</summary>
    private static NavPoint StartingDoorOf(string blueprintId)
    {
        foreach (var (_, candidate, col, row) in DeckBlueprints.StartingFixtures)
        {
            if (candidate != blueprintId) continue;
            var door = DeckBlueprints.DoorCellOf(candidate, new GridCell(col, row));
            return new NavPoint(CellCenterOf(door.Col), CellCenterOf(door.Row));
        }

        return new NavPoint(0.0, 0.0);
    }

    /// <summary>
    /// 洞穴清剿涌上甲板的落点（镇政府电梯口，甲板逻辑坐标）—— <b>镇政府门口那一格的格心</b>。
    ///
    /// <para><b>它是「门口那一格」，不是一对写死的米</b>：镇政府锚点 (-2,-2)、占地 4×4，
    /// 门口格是 (0, 2)，格心 = (格号 + 0.5) × 格长。1 米一格时落在 (0.5, 2.5)，
    /// 2 米一格时落在 (1, 5)。写成写死的米数的话，格长一改这一格就滑进镇政府的墙里，
    /// 寻路层对「涌怪落点」那一发回「地形无路」，整场清剿静悄悄地变成空转。</para>
    /// </summary>
    public static NavPoint CaveSpawn()
    {
        var door = TownHallDoorCell();
        return new NavPoint(CellCenterOf(door.Col), CellCenterOf(door.Row));
    }

    /// <summary>镇政府门口格（建造格号）：锚点在占地正中偏南一格，与 <see cref="DeckBlueprints.DoorCellOf"/> 同一款。</summary>
    private static GridCell TownHallDoorCell()
    {
        var anchor = new GridCell(0, 0);
        var width = 0;
        foreach (var (buildingId, blueprintId, col, row) in DeckBlueprints.StartingFixtures)
        {
            if (buildingId != ArenaWorld.TownHallId) continue;
            anchor = new GridCell(col, row);
            width = DeckBlueprints.SpecOf(blueprintId)?.Width ?? 0;
            break;
        }

        return new GridCell(anchor.Col + (width / 2), anchor.Row + width);
    }

    /// <summary>一个建造格号的格心落在甲板逻辑坐标几米（<b>格号 × 格长 + 半格</b>）。</summary>
    private static double CellCenterOf(int cellIndex)
        => (cellIndex + 0.5) * DeckBlueprints.CellSize;

    /// <summary>游客从浮桥上甲板的落点（甲板逻辑坐标）。</summary>
    public static NavPoint Pontoon() => new(ArenaTuning.PontoonX, ArenaTuning.PontoonZ);

    /// <summary>离给定点最近的甲板边中点（出界退场往那儿走，按 <paramref name="cells"/> 档的边算）。</summary>
    public static NavPoint NearestDeckEdge(NavPoint at, int cells)
    {
        var edge = EdgeCellCenter(cells);
        var x = Math.Abs(at.X);
        var z = Math.Abs(at.Z);

        // 平手时靠前的那条赢（比较次序固定 = 同一坐标永远得到同一个点，重放要逐字可复现）。
        if (x <= z)
        {
            return x >= at.X
                ? new NavPoint(edge, at.Z)
                : new NavPoint(-edge, at.Z);
        }

        return z >= at.Z
            ? new NavPoint(at.X, edge)
            : new NavPoint(at.X, -edge);
    }
}
