#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>点落进哪一格 + 格内局部坐标（<see cref="Fx"/> / <see cref="Fz"/> ∈ [0,1)）。</summary>
/// <param name="Cell">所在格。</param>
/// <param name="Fx">格内横向位置，0 = 该格靠 −X 那条边，1 = 靠 +X 那条边。</param>
/// <param name="Fz">格内进深位置，0 = 该格靠 −Z 那条边，1 = 靠 +Z 那条边。</param>
public readonly record struct NavLocal(NavCell Cell, double Fx, double Fz);

/// <summary>
/// 寻路网格 —— 带通过成本的均匀网格，模块里唯一的「哪里能过」真值。
///
/// <para><b>边界是网格的定义的一部分</b>，不是宿主事后夹取：网格外那一圈是虚拟阻挡
/// （见 <see cref="ClearanceField"/>），边界附近的净空因此自动被压住，
/// 「不许出去」由这张表自己表达。宿主侧那份竞技场矩形夹取（<c>BattleContract.ClampToArena</c>）
/// <b>没有退休</b>：它现在只在摆兵入场那一处的抖动里用着。</para>
///
/// <para><b>两个版本号分开</b>（spec「地形来源接线」）：改地形只动 <see cref="StaticVersion"/>，
/// 改动态代价层只动 <see cref="DynamicVersion"/>。合在一起就没有「静态变了一次全量作废、
/// 动态变了一次只作废那几格」这句话的落点，而在手路径的复用正是按这个键判的。</para>
///
/// <para><b>三个写入口</b>：<see cref="SetBlocked"/>（地形阻挡，连带重算净空场）、
/// <see cref="SetPassCost"/>（静态通过成本）、<see cref="SetDynamicCost"/>（动态代价层）。
/// 值没真的变就不动版本号 —— 让 agent 问「为什么路径没重算」时得到一个干净的答案。</para>
///
/// <para>算术一律 <c>double</c>（「double 与不变文化」），坐标用本层自有的 <see cref="NavPoint"/>，
/// 不吃 Godot 的单精度 <c>Vector2</c>。</para>
///
/// <para><b>网格自带一个原点</b>（<see cref="OriginX"/> / <see cref="OriginZ"/>）：
/// 说的是「第 (0,0) 格那一角落在空间坐标的哪一点」。格索引仍从 0 起算，
/// 阻挡表与净空表按格索引摊平 —— 原点挪的是整张网格的位置，不是格号。
/// 零基网格照旧把原点递 0 就够，不必改任何行为。</para>
/// </summary>
public sealed class NavGrid
{
    private readonly bool[] _blocked;
    private readonly double[] _passCost;
    private readonly double[] _dynamicCost;

    /// <summary>按 <see cref="NavGridSpec"/> 造网格。缺参数与不成参数的形状都在入参处当场抛（「桥只转发」）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">格长不为正 / 格数不足 / 阻挡表里有越界或重复的格。</exception>
    public NavGrid(NavGridSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        NavGridSpecCheck.CellMeters(spec.CellMeters);
        NavGridSpecCheck.CellCount(spec.CellsX, nameof(spec.CellsX));
        NavGridSpecCheck.CellCount(spec.CellsZ, nameof(spec.CellsZ));
        NavGridSpecCheck.Origin(spec.OriginX, nameof(spec.OriginX));
        NavGridSpecCheck.Origin(spec.OriginZ, nameof(spec.OriginZ));
        ArgumentNullException.ThrowIfNull(spec.BlockedCells, "阻挡表必填：空表是「明确没有地形阻挡」，漏给是「没声明」（「桥只转发」）。");

        Space = spec.Space;
        CellsX = spec.CellsX;
        CellsZ = spec.CellsZ;
        CellMeters = spec.CellMeters;
        OriginX = spec.OriginX;
        OriginZ = spec.OriginZ;

        var count = spec.CellsX * spec.CellsZ;
        _blocked = new bool[count];
        _passCost = new double[count];
        _dynamicCost = new double[count];
        for (var i = 0; i < count; i++)
        {
            _passCost[i] = 1.0;      // 走一格要付的权重，1 是基准
            _dynamicCost[i] = 1.0;  // 动态代价层的缺省是「不减速」—— 这是那层自己的语义缺省，不是入参缺省
        }

        foreach (var cell in spec.BlockedCells)
        {
            if (!IsInside(cell))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spec.BlockedCells),
                    cell,
                    $"阻挡表里的格 {cell} 越界（网格 {CellsX}×{CellsZ}）—— 网格外那一圈是虚拟阻挡，替它封堵等于把墙砌在场地上。");
            }

            var at = Index(cell.X, cell.Z);
            if (_blocked[at])
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spec.BlockedCells),
                    cell,
                    $"阻挡表里 {cell} 出现了两次 —— 同一格封两次会让「这份表是什么」读不出来，构造期就报出来比运行期少一格地形强。");
            }

            _blocked[at] = true;
        }

        Clearance = new ClearanceField(CellsX, CellsZ, CellMeters, OriginX, OriginZ, _blocked);
    }

    /// <summary>
    /// 这张网格活在哪一个相对坐标系里（ADR 0009：空间只有名字，没有原点与变换。
    /// 网格自己在空间里的位置见 <see cref="OriginX"/>，那是另一件事）。
    /// </summary>
    public SpaceId Space { get; }

    /// <summary>列数（沿 +X）。</summary>
    public int CellsX { get; }

    /// <summary>行数（沿 +Z）。</summary>
    public int CellsZ { get; }

    /// <summary>一格多少米。与造网格那份同一个数，不另立第二份（「档与表钉死」）。</summary>
    public double CellMeters { get; }

    /// <summary>
    /// 网格第 (0, 0) 格那一角的 X（米，空间坐标）。
    ///
    /// <para><b>这一列说的是网格在空间里的位置，不是空间→世界的变换</b>（ADR 0009 那一条没动）：
    /// 空间仍然只有名字，换算仍然归宿主。格索引仍从 0 起算 ——
    /// 阻挡表、净空表、快照那一段用的全是格索引，原点只挪整张网格的位置。</para>
    /// </summary>
    public double OriginX { get; }

    /// <summary>网格第 (0, 0) 格那一角的 Z（米，空间坐标）。口径同 <see cref="OriginX"/>。</summary>
    public double OriginZ { get; }

    /// <summary>网格的边长（米，<see cref="CellsX"/> × <see cref="CellMeters"/>）。<b>宽，不是起点</b>。</summary>
    public double ExtentXmeters => CellsX * CellMeters;

    /// <summary>网格的进深（米，<see cref="CellsZ"/> × <see cref="CellMeters"/>）。<b>宽，不是起点</b>。</summary>
    public double ExtentZmeters => CellsZ * CellMeters;

    /// <summary>网格在 X 方向的起点（米，空间坐标）＝ <see cref="OriginX"/>。</summary>
    public double MinXMeters => OriginX;

    /// <summary>网格在 X 方向的终点（米，空间坐标，开区间）＝ 原点 + 边长。</summary>
    public double MaxXMeters => OriginX + ExtentXmeters;

    /// <summary>网格在 Z 方向的起点（米，空间坐标）＝ <see cref="OriginZ"/>。</summary>
    public double MinZMeters => OriginZ;

    /// <summary>网格在 Z 方向的终点（米，空间坐标，开区间）＝ 原点 + 进深。</summary>
    public double MaxZMeters => OriginZ + ExtentZmeters;

    /// <summary>净空场：逐格距离表 + 连续点的保守下界。</summary>
    public ClearanceField Clearance { get; }

    /// <summary>静态地形版本。地形阻挡 / 静态通过成本每真变一次 +1，动态层怎么动都不碰它。</summary>
    public int StaticVersion { get; private set; }

    /// <summary>动态代价版本。动态代价层每真变一次 +1，地形怎么动都不碰它。</summary>
    public int DynamicVersion { get; private set; }

    /// <summary>格坐标是否在网格内。</summary>
    public bool IsInside(NavCell cell) => cell.X >= 0 && cell.X < CellsX && cell.Z >= 0 && cell.Z < CellsZ;

    /// <summary>点是否在网格内（边界那一线算界外）。</summary>
    public bool Contains(NavPoint p) => Clearance.Contains(p);

    /// <summary>一格的中心（米）。</summary>
    public NavPoint CenterOf(NavCell cell) => Clearance.CenterOf(cell.X, cell.Z);

    /// <summary>
    /// 请求的空间必须就是这张网格的空间，否则当场抛。
    ///
    /// <para>抛而不是静默换算：空间不带变换，换算归宿主（ADR 0009），
    /// 模块手里没有做换算需要的东西 —— 硬着头皮算出来的坐标是<b>看着能用、实际是另一个场地</b>的数。
    /// 消息带出双方 id，因为「哪两个空间」是唯一有用的现场。</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">请求的空间与本网格不一致。</exception>
    public void RequireSameSpace(SpaceId requested)
    {
        if (requested == Space)
        {
            return;
        }

        throw new InvalidOperationException(
            $"跨空间请求当场抛：请求的空间是「{requested}」，这张网格属于「{Space}」。"
            + "空间不带变换，空间→世界的换算归宿主，本层没有做换算需要的东西 —— 静默换算出来的坐标是另一个场地的数。");
    }

    /// <summary>点落进哪一格。界外当场抛（越界是调用方的错，不是一个该被兜住的读数，「桥只转发」）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">点在网格外。</exception>
    public NavLocal RequireCellOf(NavPoint p)
    {
        if (Clearance.TryLocal(p, out var cx, out var cz, out var fx, out var fz))
        {
            return new NavLocal(new NavCell(cx, cz), fx, fz);
        }

        throw new ArgumentOutOfRangeException(
            nameof(p),
            p,
            $"点 {p} 落在网格外 —— 网格在空间里占的是 X {MinXMeters.ToString(CultureInfo.InvariantCulture)}..{MaxXMeters.ToString(CultureInfo.InvariantCulture)}、"
            + $"Z {MinZMeters.ToString(CultureInfo.InvariantCulture)}..{MaxZMeters.ToString(CultureInfo.InvariantCulture)} 米"
            + $"（{CellsX}×{CellsZ} 格，每格 {CellMeters.ToString(CultureInfo.InvariantCulture)} 米）—— 界外没有格可落。"
            + "要判「界外」而不抛用 Contains；要判「能不能站」用 CanStandAt。");
    }

    /// <summary>这一格是不是地形阻挡（界外那一圈按阻挡算 —— 边界即墙）。</summary>
    public bool IsBlocked(NavCell cell) => !IsInside(cell) || _blocked[Index(cell.X, cell.Z)];

    /// <summary>静态通过成本（1 是基准）。界外格读到 0：走不过去的东西不报价。</summary>
    public double PassCostAt(NavCell cell)
        => IsInside(cell) ? _passCost[Index(cell.X, cell.Z)] : 0.0;

    /// <summary>动态代价层在这一格上的乘数（1 = 不减速）。界外格读到 0。</summary>
    public double DynamicCostAt(NavCell cell)
        => IsInside(cell) ? _dynamicCost[Index(cell.X, cell.Z)] : 0.0;

    /// <summary>通过成本 = 静态通过成本 × 动态乘数。两个版本分开的读数在这里合成一个标量。</summary>
    public double EffectiveCostAt(NavCell cell) => PassCostAt(cell) * DynamicCostAt(cell);

    /// <summary>
    /// 半径 <paramref name="radiusMeters"/> 能不能站在 p。
    ///
    /// <para>判的是<strong>保守下界</strong>：<c>下界 ≥ r</c> ⇒ 真值 <c>≥ r</c>，
    /// 所以这条<strong>绝不会误判能站</strong>（单位不会插进墙里），代价是可能保守过头几厘米。
    /// 界外一律 false（界外那一圈是虚拟阻挡，连零半径也站不下），不抛 ——
    /// 与 <see cref="ClearanceField.ConservativeLowerBoundAt"/> 同一口径：
    /// 避让可能把单位推出边界几厘米，游戏循环里不该因为一次推挤崩掉。</para>
    /// </summary>
    public bool CanStandAt(NavPoint p, double radiusMeters)
    {
        if (radiusMeters < 0 || double.IsNaN(radiusMeters) || double.IsInfinity(radiusMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMeters),
                radiusMeters,
                $"半径必须是正的有限数，收到 {radiusMeters.ToString(CultureInfo.InvariantCulture)} —— 半径是请求递进来的标量，层里没有单位表，兜 0 等于「所有单位都算零半径」（「桥只转发」）。");
        }

        return Contains(p) && Clearance.ConservativeLowerBoundAt(p) >= radiusMeters;
    }

    /// <summary>封 / 解封一格地形。值没变不动版本号；真变了重算净空场并把静态版本 +1（动态版本不动）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">格在网格外。</exception>
    public void SetBlocked(NavCell cell, bool blocked)
    {
        RequireInsideCell(cell);
        var at = Index(cell.X, cell.Z);
        if (_blocked[at] == blocked)
        {
            return;
        }

        _blocked[at] = blocked;
        Clearance.RebuildAgainst(_blocked);
        StaticVersion++;
    }

    /// <summary>改一格的静态通过成本。倍数表达减速；<b>加不动不表阻挡</b>，走不动是 <see cref="SetBlocked"/> 那一档。</summary>
    /// <exception cref="ArgumentOutOfRangeException">格在网格外，或成本不为正 / 非有限。</exception>
    public void SetPassCost(NavCell cell, double cost)
    {
        RequireInsideCell(cell);
        NavGridSpecCheck.CellMeters(cost);
        var at = Index(cell.X, cell.Z);
        if (_passCost[at].Equals(cost))
        {
            return;
        }

        _passCost[at] = cost;
        StaticVersion++;
    }

    /// <summary>改动态代价层在这一格上的乘数（减速陷阱、临时占位）。只把动态版本 +1，静态版本一个数都不动。</summary>
    /// <remarks>
    /// 动态代价按点解析求值、不烘进静态格（spec「动态代价按点求值」一节：采样会引入对齐误差，
    /// 半径 2.6 米的陷阱在 1 米格上实际半径会飘 1.4~3.6 米）。这一格存的只是
    /// 「这一片有减速」的那份乘数，求值仍在每步为每个 agent 算。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">格在网格外，或乘数不为正 / 非有限。</exception>
    public void SetDynamicCost(NavCell cell, double multiplier)
    {
        RequireInsideCell(cell);
        NavGridSpecCheck.CellMeters(multiplier);
        var at = Index(cell.X, cell.Z);
        if (_dynamicCost[at].Equals(multiplier))
        {
            return;
        }

        _dynamicCost[at] = multiplier;
        DynamicVersion++;
    }

    private void RequireInsideCell(NavCell cell)
    {
        if (!IsInside(cell))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cell),
                cell,
                $"格 {cell} 在 {CellsX}×{CellsZ} 的网格外 —— 网格外那一圈是虚拟阻挡，不是可以逐格改写的地形。");
        }
    }

    private int Index(int x, int z) => z * CellsX + x;
}
