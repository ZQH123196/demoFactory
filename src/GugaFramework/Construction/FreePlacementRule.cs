#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace DemoFactory.GugaFramework.Construction;

/// <summary>
/// 自由放置 —— 量化到步长、按圆形占地边缘间距判的那条规则（D12、D13）。
///
/// 四个数都是构造时定的：位置量化步长（缺省 0.01）、角度量化步长（缺省 15 度）、
/// 画线格点间距（缺省 1，批量手势的整数枚举域，理由见 <see cref="BatchStep"/>）、宿主。
/// <b>没有边界矩形</b>：这一侧的地形与合法性问题一律问宿主（D8），模块里再存一份边界
/// 就是第二处真值。
///
/// 间距判的是<b>边缘到边缘</b>：两座建筑要的间距 = 半径之和，够就是够（D12）。
/// 这条不是新口径 —— 迁移前那侧判的是中心距 <c>MinPlaceDistance</c>，把全部半径折成
/// <c>MinPlaceDistance/2</c> 就与现状逐字等价，ticket 05 的迁移靠的就是这个等价式。
/// </summary>
public sealed class FreePlacementRule : IPlacementRule
{
    public const double DefaultPositionStep = 0.01;
    public const int DefaultAngleStepDegrees = 15;

    /// <summary>画线 / 画矩形 / 布局三种手势共用的那套格点间距，缺省 1 个世界单位。</summary>
    public const double DefaultBatchStep = 1.0;

    private readonly List<Placed> _placed = new();
    private readonly IConstructionHost? _host;
    private readonly IReadOnlyDictionary<string, double> _gizmo;

    public FreePlacementRule(
        double positionStep = DefaultPositionStep,
        int angleStepDegrees = DefaultAngleStepDegrees,
        IConstructionHost? host = null,
        double batchStep = DefaultBatchStep)
    {
        if (positionStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positionStep), positionStep, "量化步长必须是正数");
        }

        if (angleStepDegrees <= 0 || angleStepDegrees > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(angleStepDegrees), angleStepDegrees,
                "角度量化步长要在 1~180 度之间（180 = 只分上下两面，再大就退化成一面了）");
        }

        if (batchStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchStep), batchStep,
                "画线格点间距必须是正数：它是这一侧三种批量手势枚举用的整数域的刻度");
        }

        PositionStep = positionStep;
        AngleStepDegrees = angleStepDegrees;
        BatchStep = batchStep;
        _host = host;
        _gizmo = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["positionStep"] = positionStep,
            ["angleStepDegrees"] = angleStepDegrees,
            ["batchStep"] = batchStep,
        };
    }

    public string RuleLabel => "自由放置";

    public FootprintKind Kind => FootprintKind.Radius;

    public double PositionStep { get; }

    public int AngleStepDegrees { get; }

    /// <summary>
    /// 画线 / 画矩形 / 布局三种手势共用的格点间距（世界单位）。
    ///
    /// <para><b>自由放置的世界本来没有格</b>，批量手势却要在整数域上枚举（枚举顺序单一真值
    /// 要落在整数上，D24），所以这一侧自己定了一套格点：<c>第 (列,行) 格 = (列×步长, 行×步长)</c>。
    /// 缺省 1 个世界单位。</para>
    ///
    /// <para>它是构造参数而不是从蓝图半径推出来的：半径是<b>蓝图</b>数据，间距是<b>场子</b>数据，
    /// 同一场子里不同蓝图半径不一样（柱子与城墙），从哪一张蓝图推都不对。步长定得太密的话
    /// 一排建筑会自己撞上（TooClose），那是逐格裁决表里如实报出来的一行，不是 bug。</para>
    /// </summary>
    public double BatchStep { get; }

    public string BoundsText => "unbounded";

    public IReadOnlyDictionary<string, double> GizmoParams => _gizmo;

    /// <summary>自由放置没有格：这个数是占着地的座数（快照摘要行只印这一个数，两条规则同形）。</summary>
    public int OccupiedCellCount => _placed.Count;

    /// <summary>位置量化：<b>中点取远离零</b>，不用四舍六入五成双 —— 同一格里两侧的候选点
    /// 折到同一档，判的那个点与落的那个点必须是同一个数（D29 预览一致性的前提）。</summary>
    public static double Quantize(double value, double step)
        => Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

    /// <summary>角度量化到整数度（D13：整数度进 Journal，重放那边读的是整数不是浮点）。</summary>
    public static int QuantizeDegrees(int degrees, int step)
        => (int)Math.Round(degrees / (double)step, MidpointRounding.AwayFromZero) * step;

    public PlacementVerdict Check(PlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var footprint = request.Blueprint.Footprint as RadiusFootprint
            ?? throw new InvalidOperationException(
                $"{RuleLabel} 吃圆形占地，蓝图 {request.Blueprint.Id} 给的是 {request.Blueprint.Footprint.Kind}" +
                "（形状与规则不匹配当场抛，D33）");

        var world = new Vector2(
            (float)Quantize(request.Candidate.X, PositionStep),
            (float)Quantize(request.Candidate.Y, PositionStep));
        var orientation = QuantizeDegrees(request.Orientation, AngleStepDegrees);

        PlacementVerdict Verdict(bool ok, PlacementReason reason)
            => new(ok, reason, world, orientation, Array.Empty<GridCell>(), null, footprint);

        // 问宿主的是量化之后那个点：判的点与落的点必须是同一个数，否则表现层画的地方
        // 与逻辑判的地方能差半格，那正是「两处真值」的形状。
        if (_host is not null && !_host.IsBuildable(BuildSite.At(world)))
        {
            return Verdict(false, PlacementReason.NotBuildable);
        }

        foreach (var other in _placed)
        {
            if (other.Id == request.ExcludeBuildingId)
            {
                // 同网格那一侧的理由：编辑动词自己不算邻居（D20）。这里要是漏了这条，
                // 自由放置侧连「挪回原处」都过不了 —— 距离 0 永远小于半径和。
                continue;
            }

            if (Distance(other.Center, world) < other.Radius + footprint.Radius)
            {
                return Verdict(false, PlacementReason.TooClose);
            }
        }

        return Verdict(true, PlacementReason.None);
    }

    /// <summary>
    /// 两点距离 —— <b>在 double 上算</b>（「double 与不变文化」）。<see cref="Vector2.Distance"/> 出的是 float，
    /// 半径和那个阈值是 double：拿 float 的结果比 double 的阈值，正好在 12.0 这种
    /// 边界上会差最后一格，而这条判定就是那次迁移的逐字等价式（D12）。
    /// </summary>
    private static double Distance(Vector2 a, Vector2 b)
    {
        var dx = (double)a.X - b.X;
        var dy = (double)a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public void Occupy(string buildingId, PlacementVerdict placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        if (placed.Circle is null)
        {
            throw new InvalidOperationException(
                $"登记 {buildingId} 的占用时圆形占地是空的：那是裁决没跑就被登记了，查 Check 那一侧");
        }

        foreach (var other in _placed)
        {
            if (other.Id == buildingId)
            {
                throw new InvalidOperationException($"{buildingId} 已经占着地了，重复登记会漏腾一次间距位");
            }
        }

        _placed.Add(new Placed(buildingId, placed.SnappedWorld, placed.Circle.Radius));
    }

    /// <summary>
    /// 场地建筑登记：<b>照 <see cref="Check"/> 同一份算术</b>吸一次位置与角度，然后把那一份占下来。
    /// 刻意不查宿主、不比任何一座的距离（间距表里可以有两座挨着 —— 那正是关卡摆位要的），
    /// 但<b>进索引</b>：后来的 Check 于是看得见它。
    /// </summary>
    public PlacementVerdict OccupyFixture(string buildingId, Blueprint blueprint, Vector2 position,
        int orientation)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var footprint = blueprint.Footprint as RadiusFootprint
            ?? throw new InvalidOperationException(
                $"{RuleLabel} 吃圆形占地，蓝图 {blueprint.Id} 给的是 {blueprint.Footprint.Kind}" +
                "（形状与规则不匹配当场抛，D33）");

        // 与 Check 逐字同一份算术：判的点与落的点必须是同一个数（D29），
        // 场地建筑占的那块地因此与玩家读到的禁区边界严丝合缝。
        var world = new Vector2(
            (float)Quantize(position.X, PositionStep),
            (float)Quantize(position.Y, PositionStep));
        var verdict = new PlacementVerdict(true, PlacementReason.None, world,
            QuantizeDegrees(orientation, AngleStepDegrees), Array.Empty<GridCell>(), null, footprint);

        // 重名那道防线复用 Occupy 的：登记两次等于漏腾一次间距位（间距表是追加的，撤不掉第二次）。
        Occupy(buildingId, verdict);
        return verdict;
    }

    public void Vacate(string buildingId)
    {
        for (var i = 0; i < _placed.Count; i++)
        {
            if (_placed[i].Id == buildingId)
            {
                _placed.RemoveAt(i);
                return;
            }
        }

        throw new InvalidOperationException(
            $"{buildingId} 没有占用登记，腾占用这一发打空了 —— 移除链与损毁链都得走 Vacate");
    }

    public string? BuildingIdAt(Vector2 point)
    {
        // 按登记顺序第一个命中的。半径 0 的建筑只在自己那一点上命中（距离 0 <= 半径 0）。
        foreach (var p in _placed)
        {
            if (Distance(p.Center, point) <= p.Radius)
            {
                return p.Id;
            }
        }

        return null;
    }

    // ---- 批量手势要问规则的三件事（与网格侧同一个接口，各自一份实现，D37）----------

    public GridCell CellOf(Vector2 world)
        => new(
            (int)Math.Floor(world.X / BatchStep),
            (int)Math.Floor(world.Y / BatchStep));

    public Vector2 CandidateAt(GridCell cell)
        => new((float)(cell.Col * BatchStep), (float)(cell.Row * BatchStep));

    /// <summary>
    /// 自由侧的布局整体方向：<b>本轮只有「不转」这一档</b>，其余当场抛。
    ///
    /// <para>为什么不是角度相加了事：布局成员的偏移是那套格点上的<b>整数</b>，转过之后
    /// 15° 之类就变成小数，成员位置与画线 / 画矩形共用的整数枚举域就分家了 —— 那时要动的是
    /// 布局数据的形状（成员偏移允许小数、格点不再唯一），属于要改接口的那一类决定，
    /// 不该在实现里顺手定（D39 那种「先把扩展点留着」的做法）。</para>
    ///
    /// <para>抛 <see cref="NotSupportedException"/> 而不是回一个失败读数：这不是「这一发
    /// 打不成」，是「这一侧的这条能力还没有」，按动词表的口径 2 走（不适用要写明理由，
    /// 不许静默少一条）。</para>
    /// </summary>
    public Layout TransformLayout(Layout layout, int orientation)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (QuantizeDegrees(orientation, AngleStepDegrees) % 360 == 0)
        {
            return layout;
        }

        throw new NotSupportedException(
            $"自由放置这一侧还不接布局的整体方向 {orientation}°：成员偏移是格点上的整数，" +
            "转出来的小数会让布局与画线 / 画矩形的整数枚举域分家（D24 的单一真值）。" +
            "不转（0°）照旧可用。");
    }

    public override string ToString()
        => $"{RuleLabel} step={PositionStep.ToString(CultureInfo.InvariantCulture)} angle={AngleStepDegrees}";

    private readonly record struct Placed(string Id, Vector2 Center, double Radius);
}
