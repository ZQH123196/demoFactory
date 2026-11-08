#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 甲板那一半：网格建造、十三件建筑蓝图、开局 6 栋默认建筑、法术停滞拔起与落位、
// 施工 3 人启停、备件修房。
//
// 与切片 1/2 那份 ArenaBuildings / ArenaConstructionSink 的关系：那是「自由放置 + 圆形占地」那套，
// 这一份是「网格放置 + 格占地」那套，两套并存到表现层那一摊工单做清理为止。
// 坐标换算只有一条推导：建造模块收的是 Vector2 落点，格号经 GridPlacementRule.CandidateAt 换算。
//
// 甲板格数随位阶扩容（一阶 24×24，二阶 28×28）：本文件里的 MinCol..MaxRow 是**一阶边界**，
// 活动边界归 ArenaWorld（IsDeckCellInBounds）；寻路网格一律按最大那一档（28×28）建，
// 一阶时外圈两格带阻挡，进阶完成那一刻解阻挡。

/// <summary>十三件建筑蓝图的规格数据（占地、造价、耐久、岗位数与它干什么）。</summary>
/// <param name="Id">蓝图 id。</param>
/// <param name="Name">中文名，只给人看。</param>
/// <param name="Width">占几格宽。</param>
/// <param name="Height">占几格高。</param>
/// <param name="CoinCost">造价（金币）。</param>
/// <param name="MaxDurability">耐久上限（镇政府 200，其余 100）。</param>
/// <param name="Posts">岗位数（这一栋能派几个人）。</param>
/// <param name="Effect">这一栋干什么，一句话。</param>
public sealed record DeckBuildingSpec(
    string Id,
    string Name,
    int Width,
    int Height,
    int CoinCost,
    double MaxDurability,
    int Posts,
    string Effect);

/// <summary>
/// 甲板那十三件建筑的蓝图与规格表，外加开局 6 栋的锚点。
///
/// <para><b>占地形状按档分三组</b>：4×4（镇政府、竞技场，造价 800）、
/// 3×3（训练场、马场、靶场、工坊、空船停靠区、酒店、饭店，造价 500）、
/// 2×2（粮仓、宿舍、教堂、传送阵，造价 300）。
/// 耐久只有镇政府是 200，其余都是 100。</para>
///
/// <para><b>建造规则是网格放置</b>：开局一份 <c>GridPlacementRule(2.0, -12, -12, 11, 11)</c>，
/// 于是坐标换算只有一条路 —— <c>CandidateAt(cell)</c> 把格号落成 <c>(col × 格长, row × 格长)</c>。
/// 甲板寻路那一侧的网格<b>恒按二阶最大档 28×28 格建</b>、原点在角上，
/// 所以建造格 (c) 映成寻路格 <c>(c.Col + 14, c.Row + 14)</c>
/// —— <b>那一项平移按格数算</b>（<see cref="NavOffsetCells"/>），折成米是 <see cref="DeckGeometry.Offset"/>。
/// 一阶时外圈两格带地形阻挡，进阶完成那一刻解阻挡（扩容生效点归 ArenaWorld）。</para>
/// </summary>
public static class DeckBlueprints
{
    /// <summary>
    /// 格长（米）。<b>与寻路那张网同一份</b>（<see cref="ArenaTuning.DeckCellMeters"/>）——
    /// 两处各写一个字面量的话，某天改了一处，另一处就成了「建造按 1 米、寻路按 2 米」的错位。
    /// </summary>
    public const double CellSize = ArenaTuning.DeckCellMeters;

    /// <summary>一阶建造格下界（列与行各一份）。活动边界随位阶走，真值在 ArenaWorld.IsDeckCellInBounds。</summary>
    public const int MinCol = -ArenaTuning.DeckCellsTier1 / 2;

    public const int MinRow = MinCol;

    /// <summary>一阶建造格上界（含）。24 格 = 上界 - 下界 + 1。</summary>
    public const int MaxCol = ArenaTuning.DeckCellsTier1 / 2 - 1;

    public const int MaxRow = MaxCol;

    /// <summary>
    /// 建造格号 → 甲板寻路格号那一项平移（<b>按格数</b>，不是米）：
    /// 寻路网格恒按最大档 28 格建、原点差在角上，建造格 -14..13 因此映成寻路格 0..27。
    ///
    /// <para><b>按格数、不按米，这是两处该读同一个数的唯一理由</b>：平移的米数由它乘上格长得出
    /// （<see cref="DeckGeometry.Offset"/>），而格号平移永远是半张最大网格 ——
    /// 扩容不改变「建造网格与寻路网格原点差在角上」这件事，平移也就不跟着位阶变。</para>
    /// </summary>
    public const int NavOffsetCells = ArenaTuning.DeckCellsTier2 / 2;

    /// <summary>
    /// 造一份<b>新的</b>网格放置规则：格长 <see cref="CellSize"/> 米，格号按 <paramref name="cells"/> 格见方。
    /// 开局递一阶档（<see cref="ArenaTuning.DeckCellsTier1"/>），扩容走规则自己的外扩口。
    ///
    /// <para><b>不能是共享的那一份</b>：规则自己持有一张「哪一格占着谁」的占用表，
    /// 而那张表是会变的 —— 造完建筑、挪走建筑都在改它。共用一份实例的话，
    /// 第二个世界登记开局建筑时撞的就是上一个世界留下的占用（登记当场抛「重复登记会漏腾一次格」）。
    /// 一个世界一份规则，占用与册一起走，这是「一本册一份状态」的同一口径。</para>
    /// </summary>
    public static GridPlacementRule BuildRule(int cells)
        => new(CellSize, ArenaTuning.BuildMinCol(cells), ArenaTuning.BuildMinCol(cells),
            ArenaTuning.BuildMaxCol(cells), ArenaTuning.BuildMaxCol(cells));

    /// <summary>
    /// 寻路网格上「<paramref name="outerCells"/> 见方之内、<paramref name="innerCells"/> 见方之外」
    /// 那一圈格号（扩容要解阻挡 / 开局要砌阻挡的那一批，行列次序固定）。
    /// </summary>
    public static IReadOnlyList<NavCell> NavRingCells(int innerCells, int outerCells)
    {
        var ring = new List<NavCell>();
        for (var col = ArenaTuning.BuildMinCol(outerCells); col <= ArenaTuning.BuildMaxCol(outerCells); col++)
        {
            for (var row = ArenaTuning.BuildMinCol(outerCells); row <= ArenaTuning.BuildMaxCol(outerCells); row++)
            {
                var inInner = col >= ArenaTuning.BuildMinCol(innerCells) && col <= ArenaTuning.BuildMaxCol(innerCells)
                    && row >= ArenaTuning.BuildMinCol(innerCells) && row <= ArenaTuning.BuildMaxCol(innerCells);
                if (inInner) continue;
                ring.Add(ToNavCell(new GridCell(col, row)));
            }
        }

        return ring;
    }

    /// <summary>13 件建筑的规格表，按造价档（高→低）再按 id 排。</summary>
    public static IReadOnlyList<DeckBuildingSpec> Specs { get; } = new[]
    {
        // 4×4，造价 800
        new DeckBuildingSpec("TownHall", "镇政府", 4, 4, 800, 200, 3, "仓库容量 200 件，不可拆除"),
        new DeckBuildingSpec("Arena", "竞技场", 4, 4, 800, 100, 2, "办赛场地"),

        // 3×3，造价 500
        new DeckBuildingSpec("TrainingGround", "训练场", 3, 3, 500, 100, 3, "近战训练 +2/游戏小时"),
        new DeckBuildingSpec("Stable", "马场", 3, 3, 500, 100, 3, "骑术训练 +2/游戏小时"),
        new DeckBuildingSpec("ArcheryRange", "靶场", 3, 3, 500, 100, 3, "远程训练 +2/游戏小时"),
        new DeckBuildingSpec("Workshop", "工坊", 3, 3, 500, 100, 3, "耗 5 矿石 + 20 金币每 8 游戏小时产 1 备件"),
        new DeckBuildingSpec("AirshipDock", "空船停靠区", 3, 3, 500, 100, 1, "航段空艇到访数从 0..2 提升为 1..3"),
        new DeckBuildingSpec("Hotel", "酒店", 3, 3, 500, 100, 2, "基础消费 10 金币/次"),
        new DeckBuildingSpec("Restaurant", "饭店", 3, 3, 500, 100, 2, "基础消费 5 金币/次"),

        // 2×2，造价 300
        new DeckBuildingSpec("Granary", "粮仓", 2, 2, 300, 100, 1, "食物容量 2000"),
        new DeckBuildingSpec("Dormitory", "宿舍", 2, 2, 300, 100, 0, "床位 20，只管常驻自由人住处"),
        new DeckBuildingSpec("Church", "教堂", 2, 2, 300, 100, 2, "治伤；10 魂石 + 1 游戏小时复活 24 游戏小时内尸体"),
        new DeckBuildingSpec("TeleportArray", "常规传送阵", 2, 2, 300, 100, 1, "单向传送：50 金币/人、10 金币/件，小货单次最多 5 件"),
    };

    /// <summary>开局默认落位 6 栋（通过 <c>RegisterFixture</c> 完工态登记，不进 Journal）。</summary>
    public static IReadOnlyList<(string BuildingId, string BlueprintId, int Col, int Row)> StartingFixtures { get; } =
        new[]
        {
            ("town_hall", "TownHall", -2, -2),
            ("arena", "Arena", -2, -8),
            ("training_ground", "TrainingGround", -8, -8),
            ("church", "Church", 4, -8),
            ("restaurant", "Restaurant", 4, 3),
            ("granary", "Granary", -8, -2),
        };

    /// <summary>
    /// 逐字递给建造模块的那张蓝图表。工期是 <see cref="ArenaTuning.BuildDurationGameDays"/> 个游戏日，
    /// 耐久与造价从 <see cref="Specs"/> 同一份读（不另立第二张表）。
    /// </summary>
    public static IReadOnlyList<Blueprint> Build()
    {
        var list = new List<Blueprint>(Specs.Count);
        foreach (var s in Specs)
        {
            list.Add(new Blueprint(
                s.Id,
                s.MaxDurability,
                FootprintOf(s.Width, s.Height),
                BuildDurationTicks));
        }

        return list;
    }

    /// <summary>新开工期折多少逻辑步（1 游戏日）。</summary>
    public static long BuildDurationTicks => (long)(ArenaTuning.BuildDurationGameDays * ArenaTime.StepsPerGameDay);

    /// <summary>宽 × 高的格占地（锚点在最小行最小列那一格，偏移非负）。</summary>
    public static CellFootprint FootprintOf(int width, int height)
    {
        var cells = new List<GridCell>(width * height);
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                cells.Add(new GridCell(col, row));
            }
        }

        return CellFootprint.FromCells(cells);
    }

    /// <summary>按 id 查规格（查不到回 null）。</summary>
    public static DeckBuildingSpec? SpecOf(string blueprintId)
    {
        foreach (var s in Specs)
        {
            if (s.Id == blueprintId) return s;
        }

        return null;
    }

    /// <summary>
    /// 格号 → 世界落点（建造模块收的那一档 <see cref="Vector2"/>）。
    /// 这一步是纯函数（格号 × 格长），不读规则那本占用表，所以走静态那一口。
    /// </summary>
    public static Vector2 CandidateOf(GridCell cell)
        => GridPlacementRule.CenterOf(cell, CellSize);

    /// <summary>
    /// 门口点：南侧那一格（行号 + 高），也就是人从甲板南边走过来撞门的那一格。
    /// 出界或被别人占着就顺次找最近的可通行邻格。
    /// </summary>
    public static GridCell DoorCellOf(string blueprintId, GridCell anchor, Func<GridCell, bool>? passable = null)
    {
        var spec = SpecOf(blueprintId);
        if (spec is null)
        {
            throw new ArgumentException(
                $"{blueprintId} 不在十三件建筑表里", nameof(blueprintId));
        }

        passable ??= IsFreeCell;
        var south = new GridCell(anchor.Col + (spec.Width / 2), anchor.Row + spec.Height);
        if (IsInTier1Bounds(south) && passable(south)) return south;

        // 南侧那一格站不下就沿四邻找：先左右再往前，行列次序固定，落到哪一个都一样。
        var probe = new[]
        {
            new GridCell(south.Col - 1, south.Row),
            new GridCell(south.Col + 1, south.Row),
            new GridCell(south.Col, south.Row + 1),
            new GridCell(south.Col, south.Row - 1),
        };

        foreach (var cell in probe)
        {
            if (IsInTier1Bounds(cell) && passable(cell)) return cell;
        }

        return south;
    }

    /// <summary>门口点的世界落点（建造模块收的那一档 <see cref="Vector2"/>）。</summary>
    public static Vector2 DoorPointOf(string blueprintId, GridCell anchor, Func<GridCell, bool>? passable = null)
        => CandidateOf(DoorCellOf(blueprintId, anchor, passable));

    /// <summary>格号是不是在一阶建造边界内。活动边界（随位阶扩容）真值在 ArenaWorld.IsDeckCellInBounds。</summary>
    public static bool IsInTier1Bounds(GridCell cell)
        => cell.Col >= MinCol && cell.Col <= MaxCol
        && cell.Row >= MinRow && cell.Row <= MaxRow;

    private static bool IsFreeCell(GridCell cell) => IsInTier1Bounds(cell);

    /// <summary>建造格 → 甲板寻路格（寻路网格原点在角上，建造格号是负数，两边差半个网格的格数）。</summary>
    public static NavCell ToNavCell(GridCell cell)
        => new(cell.Col + NavOffsetCells, cell.Row + NavOffsetCells);
}
