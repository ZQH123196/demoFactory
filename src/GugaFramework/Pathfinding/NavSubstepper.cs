#nullable enable
using System;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>走完一步（或走了一半）之后的读数。</summary>
/// <param name="Position">停在哪一点（米）。撞墙时是<b>最后一个合法子步的终点</b>，不是墙那一侧。</param>
/// <param name="Steps">真的走完了几个子步。</param>
/// <param name="Planned">这一步<b>本该</b>走几个子步。<b>调用方要用它算位移</b>，
/// 别拿「走完几份」除以「量出来的距离算几份」—— 那条路上量出来的距离带浮点噪声，
/// 一步刚好等于 0.5 米的整数倍时会多切一个子步（1.5 米的一步被读成 2 米位移）。</param>
/// <param name="Blocked">是不是被地形挡下了（挡下 = 这一步到这儿为止）。</param>
public readonly record struct NavSubstepResult(NavPoint Position, int Steps, int Planned, bool Blocked);

/// <summary>
/// 每步内切子步 —— <b>这一期的存在理由是解隧穿</b>（spec「每步内切子步（解隧穿）」）。
///
/// <para><b>问题</b>：逻辑步 = 0.1 秒（「时间基准」），每步位移 = 移速 ÷ 10。
/// 跑档 15 米每秒 ⇒ <b>一步 1.5 米</b>；1 米格上<b>一个 1 格宽的墙在一步之内就被穿过去</b>，
/// 而快照里也看不出异常（位置在墙那边，速度是满的，状态是「在走」）。
///
/// <para><b>做法</b>：子步长 ≤ <see cref="CellFraction"/> × 格长，于是<b>一个子步跨不过一格</b>；
/// 再加上每一段都过一遍 <see cref="NavPathStraightener.SegmentWalkable"/>（它带「不许切角」那一支），
/// <b>任何宽度的墙都跨不过去</b>，且不依赖任何外部参数档 —— 另一条路（把格长与移速表钉在一起）
/// 会把寻路锁死在移速表上，每张移速表改动都牵动寻路分辨率，不取。</para>
///
/// <para><b>撞墙时停在最后一个合法子步的终点</b>，不贴到墙皮上、也不停在这一步的开头 ——
/// 那两种读数都把「走了多远」与「碰了墙」混成同一件事。</para>
/// </summary>
public static class NavSubstepper
{
    /// <summary>
    /// 子步长是格长的多少倍。<b>写死的常量，不是入参</b>：
    /// 它是「任何宽度的墙都跨不过去」这条保证本身，不是宿主的调项。
    /// </summary>
    public const double CellFraction = 0.5;

    /// <summary>子步长的上限（米）= 这一档 × 格长。</summary>
    public static double SubstepCapMeters(NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return CellFraction * grid.CellMeters;
    }

    /// <summary>走 <paramref name="stepMeters"/> 米要切几个子步（<b>至少 1，位移为 0 时是 0</b>）。</summary>
    /// <param name="grid">这一片寻路网格（格长从它那儿来）。</param>
    /// <param name="stepMeters">本步要走的距离（米）。</param>
    public static int PlanCount(NavGrid grid, double stepMeters)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (stepMeters < 0 || double.IsNaN(stepMeters) || double.IsInfinity(stepMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepMeters),
                stepMeters,
                $"本步位移必须是非负有限数，收到 {stepMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} —— "
                + "移速与步长是宿主递进来的标量，负数会让「往哪儿走」这件事没有读数（「桥只转发」）。");
        }

        if (stepMeters == 0.0)
        {
            return 0;
        }

        var cap = SubstepCapMeters(grid);
        return (int)Math.Ceiling(stepMeters / cap);
    }

    /// <summary>
    /// 从 <paramref name="from"/> 朝 <paramref name="to"/> 走，切成子步逐段验。
    /// 每一段都过 <see cref="NavPathStraightener.SegmentWalkable"/>：那是「不许切角」的唯一一份真值
    /// （格子怎么走与直线怎么走共用它），于是子步既跨不过墙也切不过角。
    /// </summary>
    /// <param name="grid">这一片寻路网格。</param>
    /// <param name="from">起点（米）。</param>
    /// <param name="to">这一步的终点（米）—— <b>调用方已经按「步长上限 = 沿路径剩余」封过顶</b>。</param>
    /// <param name="stepMeters">这一步<b>打算</b>走多远（米）—— 切几份按它算，不按量出来的距离算。</param>
    /// <param name="radiusMeters">身体半径（米）—— 可行域的判读用它。</param>
    public static NavSubstepResult Advance(
        NavGrid grid,
        NavPoint from,
        NavPoint to,
        double stepMeters,
        double radiusMeters)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var count = PlanCount(grid, stepMeters);
        if (count == 0)
        {
            return new NavSubstepResult(from, 0, 0, false);
        }

        // 子步终点按「第 k 个子步 = 整段的 k/count 处」算：最后一段正好落在 to 上（u = 1），
        // 于是无阻挡时走完一步位置逐字等于 to，重放与快进比的就是这个逐字。
        var cursor = from;
        for (var i = 0; i < count; i++)
        {
            var u = (double)(i + 1) / count;
            var next = new NavPoint(from.X + ((to.X - from.X) * u), from.Z + ((to.Z - from.Z) * u));
            if (!NavPathStraightener.SegmentWalkable(grid, cursor, next, radiusMeters))
            {
                return new NavSubstepResult(cursor, i, count, true);
            }

            cursor = next;
        }

        return new NavSubstepResult(cursor, count, count, false);
    }
}
