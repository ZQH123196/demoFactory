#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 拉直（string pulling）—— 把格子阶梯换成<b>任意角折线</b>。seam 之后处理器的一部分，
/// 纯函数、不持有状态、不吃引擎类型。
///
/// <para><b>可行域是腐蚀过的</b>：半径 r 的可行域 = 净空 ≥ r 的格。
/// 判据用<b>格级</b>净空而不是连续点的保守下界，理由是 spec 点名了这一档
/// （「拉直在腐蚀后的可行域上做」）；格级可行不蕴含点级
/// <c>NavGrid.CanStandAt</c>，这一档的待确认项记在 <c>docs/GugaFramework/寻路层.md</c> 2 节。</para>
///
/// <para><b>不许切角</b>：直线段正好穿过一个格角时，两个正交邻格都得在可行域里，
/// 否则单位会从两个阻挡格的夹角里斜穿过去。那是 DDA（<c>SegmentWalkable</c>）里
/// <c>tMaxX == tMaxZ</c> 那一支唯一的判据，A* 的对角扩展用同一条
/// （<see cref="DiagonalStepLegal"/>），所以「格子怎么走」与「直线怎么走」只有一份真值。</para>
///
/// <para><b>怎么拉</b>：贪心视线法 —— 从当前锚点往后找<b>最远</b>那个视线可达的节点，
/// 把它当拐点交出去，再从那儿继续。节点序列 = 格子路径 + 目标点（目标点不当格心用）。
/// 锚点与终点逐字照收，所以折线的首尾就是请求的起点与目标点，不换成格心。</para>
///
/// <para><b>为什么交回的是折线而不是格子阶梯</b>：贪心视线把「能直着走的一段」
/// 合成一条直线段，拐点只留在必须转弯的地方。格子阶梯的指纹是「每段恰好一格长、
/// 拐点全在格心、方向逐段交替」，拉直之后指纹不成立 ——
/// 长直段与斜段是它的常态（用例 <c>不是格子阶梯</c> 断言的正是这一条）。</para>
/// </summary>
public static class NavPathStraightener
{
    /// <summary>半径 r 的可行域判据：格在网内、不是地形阻挡、净空 ≥ r。</summary>
    public static bool Feasible(NavGrid grid, NavCell cell, double radiusMeters)
    {
        if (!grid.IsInside(cell) || grid.IsBlocked(cell))
        {
            return false;
        }

        return grid.Clearance.At(cell) >= radiusMeters;
    }

    /// <summary>
    /// 一次对角扩展合不合法：<b>两个正交邻格都得在可行域里</b>（不许切角）。
    /// 与 <see cref="SegmentWalkable"/> 的格角那一支是同一条判据 —— 两处各写一遍就成了两套真值。
    /// </summary>
    public static bool DiagonalStepLegal(
        NavGrid grid,
        NavCell from,
        int stepX,
        int stepZ,
        double radiusMeters)
        => Feasible(grid, new NavCell(from.X + stepX, from.Z), radiusMeters)
           && Feasible(grid, new NavCell(from.X, from.Z + stepZ), radiusMeters);

    /// <summary>
    /// 拉直。输入是从起点格到目标格的格子路径（首尾两格必须在场），输出是任意角折线。
    ///
    /// <para>折线永远<b>至少一个点</b>：格子路径空、或只有起点格那一种退化输入，
    /// 交回的就是请求的起点 / 目标点本身，不是空数组（spec「不返空」）。</para>
    /// </summary>
    public static NavPoint[] Pull(
        NavGrid grid,
        IReadOnlyList<NavCell> cellPath,
        NavPoint start,
        NavPoint goal,
        double radiusMeters)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(cellPath);

        // 节点序列：0 = 请求的起点，其后是格子路径各格的格心，最后一个追加的是请求的目标点。
        // 起点与目标点不当格心处理 —— 请求给的是米，换成格心就是「请求的落点」与「我算的落点」两回事。
        var nodeCount = cellPath.Count + 1;
        var corners = new List<NavPoint>(4) { start };

        if (cellPath.Count == 0)
        {
            corners.Add(goal);
            return corners.ToArray();
        }

        var anchor = 0;
        var i = 1;
        while (i < nodeCount)
        {
            // 从最远的那个节点往回退，退到视线通行为止。退到 i 本身还不通也照样收下 ——
            // 终点永远是折线的最后一点，退无可退（「不通就退」在这里没有别的选择）。
            var from = NodePoint(grid, cellPath, start, goal, anchor);
            var j = nodeCount - 1;
            while (j > i && !SegmentWalkable(grid, from, NodePoint(grid, cellPath, start, goal, j), radiusMeters))
            {
                j--;
            }

            // 落在上一个拐点上的那一格不收：收下来是一条零长段。零长段对「按前视跟随」那一侧
            // 没有任何意义，而「一个点都没走出去」那一档交回的就是起点那一个点（不返空）。
            var to = NodePoint(grid, cellPath, start, goal, j);
            if (to != from)
            {
                corners.Add(to);
            }

            anchor = j;
            i = j + 1;
        }

        return corners.ToArray();
    }

    /// <summary>节点 k 的坐标：k = 0 是请求的起点；1..格子数−1 是那一格的格心；k = 格子数是请求的目标点。</summary>
    private static NavPoint NodePoint(
        NavGrid grid,
        IReadOnlyList<NavCell> cellPath,
        NavPoint start,
        NavPoint goal,
        int k)
    {
        if (k == 0)
        {
            return start;
        }

        return k < cellPath.Count ? grid.CenterOf(cellPath[k]) : goal;
    }

    /// <summary>
    /// 直线段 <c>from → to</c> 是不是整条都在可行域里（DDA / Amanatides–Woo 走格）。
    ///
    /// <para><b>两处判据</b>：① 每一格都得在可行域里；② 正好穿格角时，
    /// 两个正交邻格也得在可行域里（不许切角）。</para>
    ///
    /// <para>循环有硬上限（<c>cellsX + cellsZ + 4</c> 步）：一条直线段最多穿过
    /// <c>cellsX + cellsZ + 1</c> 格，浮点把「正好落在格线上」判成越过去一点点时
    /// 那一圈会多走一两步 —— 与其让它绕圈，不如到这里返回判定结果。</para>
    /// </summary>
    public static bool SegmentWalkable(NavGrid grid, NavPoint from, NavPoint to, double radiusMeters)
    {
        if (!grid.Clearance.TryLocal(from, out var cx, out var cz, out _, out _))
        {
            return false;
        }

        if (!Feasible(grid, new NavCell(cx, cz), radiusMeters))
        {
            return false;
        }

        var cell = grid.CellMeters;
        var goalCell = new NavCell(cx, cz);
        if (grid.Clearance.TryLocal(to, out var gx, out var gz, out _, out _))
        {
            goalCell = new NavCell(gx, gz);
        }

        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        if (dx == 0.0 && dz == 0.0)
        {
            // 零长段：起终点在同一格里，上面已经判过那一格。
            return true;
        }

        var stepX = dx > 0.0 ? 1 : (dx < 0.0 ? -1 : 0);
        var stepZ = dz > 0.0 ? 1 : (dz < 0.0 ? -1 : 0);
        var tDeltaX = stepX != 0 ? Math.Abs(cell / dx) : double.PositiveInfinity;
        var tDeltaZ = stepZ != 0 ? Math.Abs(cell / dz) : double.PositiveInfinity;

        // 边界坐标是**世界米**，不是格号乘格长：格 (cx, cz) 的右边界在
        // OriginX + (cx+1)×cell 那儿。少了原点这一项，DDA 会在零基网格上碰巧算对、
        // 在带原点的网格上整段错位 —— 而错位的方式是「拉直判据错」，症状是折线穿墙，
        // 读数上却看不出哪一步错了。
        var boundaryX = grid.OriginX + ((double)(stepX > 0 ? cx + 1 : cx) * cell);
        var boundaryZ = grid.OriginZ + ((double)(stepZ > 0 ? cz + 1 : cz) * cell);
        var tMaxX = stepX != 0 ? (boundaryX - from.X) / dx : double.PositiveInfinity;
        var tMaxZ = stepZ != 0 ? (boundaryZ - from.Z) / dz : double.PositiveInfinity;

        // 整条段都落在起终点所在的那一格里（t 到 1 还没出格）：这一格上面已经判过，直接给真。
        // 不在这里收口的话，同格内那一小段会先走进「正好穿格角」那一支，
        // 然后拿格角外侧的邻格来否掉一段根本没出过格的路 —— 那是把可行判成不可行。
        if (tMaxX > 1.0 && tMaxZ > 1.0)
        {
            return true;
        }

        var guard = grid.CellsX + grid.CellsZ + 4;
        while (guard-- > 0)
        {
            if (tMaxX < tMaxZ)
            {
                tMaxX += tDeltaX;
                cx += stepX;
            }
            else if (tMaxZ < tMaxX)
            {
                tMaxZ += tDeltaZ;
                cz += stepZ;
            }
            else
            {
                // 正好穿格角：两个正交邻格都得通，否则不许切角。
                if (!Feasible(grid, new NavCell(cx + stepX, cz), radiusMeters)
                    || !Feasible(grid, new NavCell(cx, cz + stepZ), radiusMeters))
                {
                    return false;
                }

                cx += stepX;
                cz += stepZ;
                tMaxX += tDeltaX;
                tMaxZ += tDeltaZ;
            }

            if (!Feasible(grid, new NavCell(cx, cz), radiusMeters))
            {
                return false;
            }

            if (cx == goalCell.X && cz == goalCell.Z)
            {
                return true;
            }

            // 两轴都走过了 t = 1：整条段已经走完（终点格没对上是因为终点落在格线上，浮点差一步）。
            if (tMaxX > 1.0 && tMaxZ > 1.0)
            {
                return true;
            }
        }

        return false;
    }
}
