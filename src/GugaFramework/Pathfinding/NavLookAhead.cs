#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 前视式跟随的取点 —— <b>方向从它来，步长不从它来</b>（spec「前视式跟随，不是轨道式」）。
///
/// <para><b>为什么是前视而不是轨道游标</b>：站位冲突必然在推单位，一被推就丢轨；
/// 丢轨之后模块得再有一套「找回轨道」的机制，那比前视贵得多。所以模块不给
/// 「下一航点的坐标」，给「朝路径前方某个距离的那个点」—— 被推偏了也能靠它把方向拉回路径。</para>
///
/// <para><b>前视点不是停靠点（这一条卡着 08 号的零回归）</b>：若拿它当停靠点，
/// 「离前视点只剩 0.1 米、一步能走 0.3 米」就变成这一步只挪 0.1 米、下一步前视点又近了 ——
/// 爬行，读数上的速度与实际位移对不上。所以：
/// <list type="bullet">
/// <item><b>方向</b> = 朝前视点那个方向（<see cref="Direction"/>）；</item>
/// <item><b>步长</b> = 本步能走的距离，<b>上限是「沿路径到终点还剩多远」</b>（<see cref="RemainingAlong"/>），
/// 与前视点在哪无关。前视距离小到不足一步时<b>仍然走满一步</b>。</item>
/// </list>
/// 全可走地形上折线是一条直线，于是位移 = 旧实现（直线趋近 × 步长）逐字相同，
/// 08 号那条「零回归、没有一条既有期望值被改过」才立得住。</para>
///
/// <para><b>前视点取「直走得到的那一个」</b>：沿折线等距取样，取<b>直着走得到的最远那一个</b>。
/// 少了这一条，前视距离一远就会出现「朝一个穿不过去的方向走满一步」——
/// 子步把墙挡住了（不隧穿，这一条照样成立），可单位从此每步都卡在同一格，
/// 而折线本身明明就在前方可走。夹缝那一条用例钉的就是这个形状。</para>
/// </summary>
public static class NavLookAhead
{
    /// <summary>
    /// 沿折线取样的份数。<b>写死的常量，不是入参</b>：它是「前视点多远算取得到」的那一档分辨率，
    /// 不是宿主的调项。最后一份正好落在前视距离上，于是全可走地形上前视点逐字就是
    /// 「前方前视距离那么远的那一点」。
    /// </summary>
    public const int Samples = 8;

    /// <summary>
    /// 前视点：沿折线从 <paramref name="at"/> 往前、<b>直着走得到的最远那一点</b>，
    /// 上限是 <paramref name="lookaheadMeters"/> 米。走完整条折线都不够前视距离时，
    /// 取的是折线末尾那一点（不外推）—— 外推出来的点不在路径上，而方向必须来自路径。
    /// </summary>
    /// <param name="grid">这一片寻路网格（直走可不通要问它）。</param>
    /// <param name="corners">拉直后的任意角折线（永不为空）。</param>
    /// <param name="at">当前位置（米）。</param>
    /// <param name="lookaheadMeters">前视距离（米，正有限数）。</param>
    /// <param name="radiusMeters">身体半径（米）—— 可行域的判读用它。</param>
    public static NavPoint PointAlong(
        NavGrid grid,
        IReadOnlyList<NavPoint> corners,
        NavPoint at,
        double lookaheadMeters,
        double radiusMeters)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(corners);
        NavCheck.Positive(lookaheadMeters, nameof(lookaheadMeters),
            "前视距离必须为正 —— 0 前视就是「朝当前位置的方向」，那一条读数与不跟随无法区分（「桥只转发」）。");

        var best = at;
        for (var k = 1; k <= Samples; k++)
        {
            var candidate = PointAtAlongDistance(corners, at, lookaheadMeters * k / Samples);
            if (!NavPathStraightener.SegmentWalkable(grid, at, candidate, radiusMeters))
            {
                // 不通就收手：再远的一份也不取。可走性沿折线并不严格单调（折线可以绕回来），
                // 所以这一条是「保守」而不是「等价」—— 取近一点的那一份永远安全，
                // 而取远一点赌它通，是把一个「大概率不卡」换成一个「偶尔卡死在夹缝口」。
                break;
            }

            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// 沿折线从 <paramref name="at"/> 到<b>最后一个拐点</b>还剩多远（米）。
    /// 这是步长的上限 —— <b>不是到前视点还剩多远</b>（<see cref="PointAlong"/>）。
    /// </summary>
    /// <param name="corners">拉直后的任意角折线。</param>
    /// <param name="at">当前位置（米）。</param>
    public static double RemainingAlong(IReadOnlyList<NavPoint> corners, NavPoint at)
    {
        ArgumentNullException.ThrowIfNull(corners);
        if (corners.Count == 0)
        {
            return 0.0;
        }

        var remaining = 0.0;
        var cursor = at;
        for (var i = 0; i + 1 < corners.Count; i++)
        {
            var next = corners[i + 1];
            remaining += cursor.DistanceTo(next);
            cursor = next;
        }

        return remaining;
    }

    /// <summary>
    /// 这一步该朝哪个方向走（单位向量，长度 1；<b>一个可走方向都没有时给 (0,0)</b>）。
    /// 方向取自前视点，<b>与前视点离得多远无关</b>。
    /// </summary>
    /// <param name="grid">这一片寻路网格。</param>
    /// <param name="corners">拉直后的任意角折线。</param>
    /// <param name="at">当前位置（米）。</param>
    /// <param name="lookaheadMeters">前视距离（米）。</param>
    /// <param name="radiusMeters">身体半径（米）。</param>
    public static (double Dx, double Dz) Direction(
        NavGrid grid,
        IReadOnlyList<NavPoint> corners,
        NavPoint at,
        double lookaheadMeters,
        double radiusMeters)
    {
        var aim = PointAlong(grid, corners, at, lookaheadMeters, radiusMeters);
        var dx = aim.X - at.X;
        var dz = aim.Z - at.Z;
        var len = Math.Sqrt((dx * dx) + (dz * dz));
        if (len > 0.0)
        {
            return (dx / len, dz / len);
        }

        // 一个可走方向都没有（前视点就落在当前位置）：改用「终点减当前位置」那个方向，
        // 仍是路径给的方向，不是猜的。两者也重合时给零方向 —— 那就是「这一步没有可走的方向」。
        if (corners.Count > 0)
        {
            var last = corners[corners.Count - 1];
            dx = last.X - at.X;
            dz = last.Z - at.Z;
            len = Math.Sqrt((dx * dx) + (dz * dz));
            if (len > 0.0)
            {
                return (dx / len, dz / len);
            }
        }

        return (0.0, 0.0);
    }

    /// <summary>折线上离 <paramref name="at"/> 沿路 <paramref name="distance"/> 米的那一点（越界就交回最后一个拐点）。</summary>
    private static NavPoint PointAtAlongDistance(IReadOnlyList<NavPoint> corners, NavPoint at, double distance)
    {
        if (corners.Count == 0)
        {
            return at;
        }

        var need = distance;
        var cursor = at;
        for (var i = 0; i + 1 < corners.Count; i++)
        {
            var next = corners[i + 1];
            var seg = cursor.DistanceTo(next);
            if (seg >= need)
            {
                return Lerp(cursor, next, need / seg);
            }

            need -= seg;
            cursor = next;
        }

        return corners[corners.Count - 1];
    }

    private static NavPoint Lerp(NavPoint from, NavPoint to, double u)
        => new NavPoint(from.X + ((to.X - from.X) * u), from.Z + ((to.Z - from.Z) * u));
}
