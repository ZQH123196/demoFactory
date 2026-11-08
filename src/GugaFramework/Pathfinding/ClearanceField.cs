#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 净空场 —— 「一格到最近地形阻挡的距离」，逐格查的一张表；外加<strong>保守下界</strong>这一个连续读法。
///
/// <para><b>净空的定义</b>：<c>净空(i) = 格 i 的中心到最近阻挡格中心的米数</c>。
/// 阻挡格集合 = 已封的格 <b>加上网格外那一圈虚拟阻挡</b>；网格外那一圈的净空是 0。
/// 于是<strong>边界即墙</strong>：边界附近的净空自动被压住，竞技场矩形那种「不许出去」
/// 不再是宿主侧的事后夹取（spec「现有六种阻挡的归类」把竞技场边界钳位收成网格边界，收在这里）。</para>
///
/// <para><b>距离是精确欧氏距离</b>，用两趟可分离的一维变换（下包络算法）算，不是 3-4 chamfer：
/// chamfer 偏低约 8%，那这一格就不该叫「距离」了，读数也就没法在纸面上验。
/// 代价是每趟每格 O(1)，网格小到不值得为此省事（性能排第三，「约束唯一出处」）。</para>
///
/// <para><b>保守下界</b>照 spec 逐字：<c>下界(p) = max(相邻四格中心 i 的 [净空(i) − |p − 中心(i)|])</c>，下限 0。
/// 四格 = 点所在那格，加上按 fx / fz 各取一档的邻格（<c>fx ≤ 0.5</c> 取上一格，否则取下一格）组成的 2×2 一组。
/// 对每个 i 都有 <c>|p − o*| ≥ |中心(i) − o*| − |p − 中心(i)| ≥ 净空(i) − |p − 中心(i)|</c>，
/// 取下界即得 <c>真值 ≥ 下界</c>，于是<strong>绝不会误判能站</strong>（<c>下界 ≥ r</c> ⇒ 真值 <c>≥ r</c>）。
/// 可能保守过头几厘米，观感上不可见；换的是 O(1)、不多存一份数组、不定「取格心还是格面」那种伪规则。</para>
///
/// <para>净空是<b>查表</b>不是每次重算：<see cref="At"/> 直接给格上的真值，
/// <see cref="ConservativeLowerBoundAt"/> 给格间任意点的下界。层里没有单位表，
/// 半径是请求递进来的一个标量（<see cref="NavGrid.CanStandAt"/>）。</para>
/// </summary>
public sealed class ClearanceField
{
    /// <summary>
    /// 哨兵：还没有落任何距离的格子记成它。取 1e15 的理由是「有限且大到不可能」——
    /// 换成 double.PositiveInfinity 会在下包络的 <c>(f[q] + q²) − (f[p] + p²)</c> 里出 NaN。
    /// </summary>
    private const double Unreachable = 1e15;

    private readonly bool[] _blocked;
    private readonly double[] _meters;

    /// <summary>造一张净空场。<paramref name="blocked"/> 按 <c>z * cellsX + x</c> 摊平，长度必须够。</summary>
    /// <param name="cellsX">列数（沿 +X）。</param>
    /// <param name="cellsZ">行数（沿 +Z）。</param>
    /// <param name="cellMeters">一格多少米。</param>
    /// <param name="originX">网格第 (0, 0) 格那一角的 X（米，空间坐标）。</param>
    /// <param name="originZ">网格第 (0, 0) 格那一角的 Z（米，空间坐标）。</param>
    /// <param name="blocked">阻挡表（摊平下标 <c>z * cellsX + x</c>）。</param>
    public ClearanceField(
        int cellsX, int cellsZ, double cellMeters, double originX, double originZ, bool[] blocked)
    {
        NavGridSpecCheck.CellCount(cellsX, nameof(cellsX));
        NavGridSpecCheck.CellCount(cellsZ, nameof(cellsZ));
        NavGridSpecCheck.CellMeters(cellMeters);
        NavGridSpecCheck.Origin(originX, nameof(originX));
        NavGridSpecCheck.Origin(originZ, nameof(originZ));
        if (blocked.Length != cellsX * cellsZ)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blocked),
                blocked.Length,
                $"阻挡表长度 {blocked.Length} 与 {cellsX}×{cellsZ} = {cellsX * cellsZ} 不符 —— 摊平下标算错时长度对不上是唯一当场看得到的信号。");
        }

        CellsX = cellsX;
        CellsZ = cellsZ;
        CellMeters = cellMeters;
        OriginX = originX;
        OriginZ = originZ;
        _blocked = (bool[])blocked.Clone();
        _meters = new double[cellsX * cellsZ];
        Rebuild();
    }

    /// <summary>列数（沿 +X）。</summary>
    public int CellsX { get; }

    /// <summary>行数（沿 +Z）。</summary>
    public int CellsZ { get; }

    /// <summary>一格多少米。与造网格那份同一个数，两处不另立（「档与表钉死」 的口径）。</summary>
    public double CellMeters { get; }

    /// <summary>
    /// 网格第 (0, 0) 格那一角的 X（米，空间坐标）。
    ///
    /// <para><b>格索引仍是 0 基</b>：原点动的是「格心在空间里的位置」，不是「格号从几起算」。
    /// 阻挡表、净空表、快照那一段用的全是格索引，原点不碰它们 ——
    /// 碰了就得把整张表按原点重排一次，而那一次重排没有任何含义。</para>
    /// </summary>
    public double OriginX { get; }

    /// <summary>网格第 (0, 0) 格那一角的 Z（米，空间坐标）。口径同 <see cref="OriginX"/>。</summary>
    public double OriginZ { get; }

    /// <summary>格坐标是否在网格内。</summary>
    public bool IsInside(int cx, int cz) => cx >= 0 && cx < CellsX && cz >= 0 && cz < CellsZ;

    /// <summary>点是否在网格内。<b>边界算界外</b>（贴着右/上边界那一线已经在场地之外）。</summary>
    public bool Contains(NavPoint p) => TryLocal(p, out _, out _, out _, out _);

    /// <summary>一格的中心（米）。界外格也算得出来 —— 虚拟阻挡那一圈就是靠它才有中心。</summary>
    public NavPoint CenterOf(int cx, int cz)
        => new NavPoint(OriginX + ((cx + 0.5) * CellMeters), OriginZ + ((cz + 0.5) * CellMeters));

    /// <summary>
    /// 一格的净空（米）：到最近阻挡格中心的距离。
    /// 界外格与阻挡格都是 0 —— 前者是网格外那一圈虚拟阻挡，后者自己是阻挡。
    /// </summary>
    public double At(int cx, int cz) => IsInside(cx, cz) ? _meters[cz * CellsX + cx] : 0.0;

    /// <summary>一格的净空（米）。</summary>
    public double At(NavCell cell) => At(cell.X, cell.Z);

    /// <summary>半开格宽的乘积：网格的边长（米）。边界是网格的定义，读法走这一格而不是让宿主自己乘。
    /// <b>这是宽不是起点</b>：网格在空间里从 <see cref="OriginX"/> 起、到 <c>OriginX + 这一档</c> 止。</summary>
    public double ExtentXmeters => CellsX * CellMeters;

    /// <summary>网格的进深（米）。口径同 <see cref="ExtentXmeters"/>：宽，不是起点。</summary>
    public double ExtentZmeters => CellsZ * CellMeters;

    /// <summary>
    /// 连续点 p 的净空<strong>保守下界</strong>（米，下限 0）。
    ///
    /// <para><b>p 在网格外返回 0，不抛</b>：避让可能把单位推出边界几厘米（那是站位冲突的事，不是地形判错），
    /// 游戏循环里不该因为一次推挤崩掉。要判界内用 <see cref="Contains"/>。</para>
    /// </summary>
    public double ConservativeLowerBoundAt(NavPoint p)
    {
        if (!TryLocal(p, out var cx, out var cz, out var fx, out var fz))
        {
            return 0.0;
        }

        // 四格 = 本格 + 按 fx / fz 各取一档的邻格（2×2 一组）。任取一格都给出合法下界
        // （三角不等式对每个 i 都成立），这里取哪一档是定死的，不是「哪个下界大取哪个」——
        // 那会让读数依赖遍历序，违反 spec「确定性靠结构」一节。
        var ox = fx <= 0.5 ? -1 : 1;
        var oz = fz <= 0.5 ? -1 : 1;

        // 四格逐格展开，次序写死（本格 → 横向 → 进深 → 对角），读数逐字可重放。
        // 不走集合也不走迭代器：这条每步每个 agent 都要问一次，分配一个枚举器不值得。
        var best = 0.0;
        best = Math.Max(best, TermAt(p, cx, cz));
        best = Math.Max(best, TermAt(p, cx + ox, cz));
        best = Math.Max(best, TermAt(p, cx, cz + oz));
        best = Math.Max(best, TermAt(p, cx + ox, cz + oz));
        return best;
    }

    /// <summary>一格给出的那一份下界：<c>净空(i) − |p − 中心(i)|</c>。界外格（含虚拟阻挡那一圈）净空是 0，项自然是负的。</summary>
    private double TermAt(NavPoint p, int cx, int cz)
        => At(cx, cz) - p.DistanceTo(CenterOf(cx, cz));

    /// <summary>点 p 落进哪一格 + 格内局部坐标（<paramref name="fx"/> / <paramref name="fz"/> ∈ [0,1)）。界外返回 false。</summary>
    public bool TryLocal(NavPoint p, out int cx, out int cz, out double fx, out double fz)
    {
        // 右/上边界判界外，左/下边界算界内 —— 钉死这一条是为了重放逐字：同一份 double 每次算出同一个格。
        // 减原点那一步是「格号 = 世界坐标 − 原点」：格索引仍从 0 起算，原点只挪整张网格的位置。
        var gx = (p.X - OriginX) / CellMeters;
        if (gx < 0 || gx >= CellsX)
        {
            cx = cz = 0;
            fx = fz = 0;
            return false;
        }

        var gz = (p.Z - OriginZ) / CellMeters;
        if (gz < 0 || gz >= CellsZ)
        {
            cx = cz = 0;
            fx = fz = 0;
            return false;
        }

        cx = (int)gx;
        cz = (int)gz;
        fx = gx - cx;
        fz = gz - cz;
        return true;
    }

    /// <summary>地形变了要重算净空场（换一份阻挡表，摊平下标 <c>z * cellsX + x</c>）。</summary>
    internal void RebuildAgainst(bool[] blocked)
    {
        ArgumentNullException.ThrowIfNull(blocked);
        if (blocked.Length != _meters.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blocked),
                blocked.Length,
                $"阻挡表长度 {blocked.Length} 与 {CellsX}×{CellsZ} = {_meters.Length} 不符。");
        }

        Array.Copy(blocked, _blocked, blocked.Length);
        Rebuild();
    }

    private void Rebuild()
    {
        var nx = CellsX;
        var nz = CellsZ;
        var stride = nx + 2;
        var tmp = new double[stride * nz];
        var row = new double[stride];

        // 第一趟：固定 z 沿 x 走。x 两端各垫一格虚拟阻挡（摊平下标 0 与 nx+1）。
        for (var z = 0; z < nz; z++)
        {
            row[0] = 0.0;
            row[nx + 1] = 0.0;
            for (var x = 0; x < nx; x++)
            {
                row[x + 1] = _blocked[z * nx + x] ? 0.0 : Unreachable;
            }

            var d = DistanceSquaredRow(row);
            for (var x = 0; x < nx; x++)
            {
                tmp[z * stride + (x + 1)] = d[x + 1];
            }
        }

        // 第二趟：固定 x 沿 z 走。z 两端同样各垫一格虚拟阻挡。
        var col = new double[nz + 2];
        for (var x = 0; x < nx; x++)
        {
            col[0] = 0.0;
            col[nz + 1] = 0.0;
            for (var z = 0; z < nz; z++)
            {
                col[z + 1] = tmp[z * stride + (x + 1)];
            }

            var d = DistanceSquaredRow(col);
            for (var z = 0; z < nz; z++)
            {
                _meters[z * nx + x] = Math.Sqrt(d[z + 1]) * CellMeters;
            }
        }
    }

    /// <summary>
    /// 一维平方距离变换（下包络算法，精确）。<c>f</c> 里 0 = 源、<see cref="Unreachable"/> = 还没量到。
    /// 返回 <c>min_k ((i − k)² + f[k])</c>。
    /// </summary>
    private static double[] DistanceSquaredRow(double[] f)
    {
        var n = f.Length;
        var d = new double[n];
        var v = new int[n];        // 下包络上各段抛物线的顶点下标
        var z = new double[n + 1]; // 段与段的分界

        var k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;

        for (var q = 1; q < n; q++)
        {
            var s = Crossing(f, v[k], q);
            while (s <= z[k])
            {
                k--;
                s = Crossing(f, v[k], q);
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }

        k = 0;
        for (var q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
            {
                k++;
            }

            var dx = q - v[k];
            d[q] = (dx * dx) + f[v[k]];
        }

        return d;
    }

    private static double Crossing(double[] f, int p, int q)
        => ((f[q] + ((double)q * q)) - (f[p] + ((double)p * p))) / ((2.0 * q) - (2.0 * p));
}
