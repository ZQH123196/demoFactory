#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Construction;

// 蓝图表 = 一种建筑的静态规格（血量、工期、占地、模型引用），模块构造时一次递入（D33）。
// 蓝图不可变（GLOSSARY「蓝图」）：换血量上限或换占地 = 另一张蓝图 + 移除重放，不在原地改。
// 于是这一族全是只读数据，句柄替代不了（改了句柄里的字段就等于第二份可改真值）。

/// <summary>占地家族。Box（OBB + 分离轴）是 D14 的留待未来 —— 加一个变体，接口不翻。</summary>
public abstract record Footprint
{
    /// <summary>这一份占地是哪种形状。放置规则与它不匹配当场抛（D33）。</summary>
    public abstract FootprintKind Kind { get; }
}

/// <summary>
/// 格占地：一组格，存成<b>锚点 + 非负偏移</b>（D9）。
///
/// 归一化在构造时一次做完：原始格表可以是任意顺序、可以带重复、可以带负数，
/// 进来一律折成「最小行最小列那格是锚点、其余是非负偏移、按行再按列排好」。
/// <b>锚点格 = 最小行最小列</b> 这条是网格放置所有不变量的根（登记锚点格就等于登记占地），
/// 现场再算一次 min 就多了一个能漂的地方。偏移是去重排过序的，所以 D4 变换、快照行、
/// 受影响格名单三处读的是同一份、同一顺序。
/// </summary>
public sealed record CellFootprint : Footprint
{
    private CellFootprint(IReadOnlyList<GridCell> offsets, int width, int height)
    {
        Offsets = offsets;
        Width = width;
        Height = height;
    }

    public override FootprintKind Kind => FootprintKind.Cells;

    /// <summary>相对锚点的非负偏移，已去重排序。</summary>
    public IReadOnlyList<GridCell> Offsets { get; }

    /// <summary>本地包围盒宽（格数）。D4 变换在盒内做，落点才不会漂（D21 的 2×2 恒成功就是这么来的）。</summary>
    public int Width { get; }

    /// <summary>本地包围盒高（格数）。</summary>
    public int Height { get; }

    /// <summary>
    /// 从任意一组格建占地（世界格表，通常是美术/关卡摆位用的形状）。
    /// 空表当场抛 —— 空占地是作者错误（D7 那档），静默折成「零格建筑」只会等表现层发现。
    /// </summary>
    public static CellFootprint FromCells(IEnumerable<GridCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var seen = new HashSet<GridCell>();
        var minCol = int.MaxValue;
        var minRow = int.MaxValue;
        var maxCol = int.MinValue;
        var maxRow = int.MinValue;
        foreach (var c in cells)
        {
            if (!seen.Add(c))
            {
                continue;
            }

            minCol = Math.Min(minCol, c.Col);
            minRow = Math.Min(minRow, c.Row);
            maxCol = Math.Max(maxCol, c.Col);
            maxRow = Math.Max(maxRow, c.Row);
        }

        if (seen.Count == 0)
        {
            throw new ArgumentException("占地是空表：零格建筑放下去没有任何一格被占，那不是建筑", nameof(cells));
        }

        var offsets = new List<GridCell>(seen.Count);
        foreach (var c in seen)
        {
            offsets.Add(new GridCell(c.Col - minCol, c.Row - minRow));
        }

        // 顺序固定为「先行后列」：快照行、事件行、受影响格名单都读这一份顺序，
        // 换个顺序就是同一座建筑在两处读出两个序。
        offsets.Sort(static (a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));

        return new CellFootprint(offsets, maxCol - minCol + 1, maxRow - minRow + 1);
    }

    /// <summary>落在这座占地上的世界格名单（按行再按列）。</summary>
    public IReadOnlyList<GridCell> At(GridCell anchor)
    {
        var cells = new List<GridCell>(Offsets.Count);
        foreach (var o in Offsets)
        {
            cells.Add(anchor + o);
        }

        return cells;
    }

    public override string ToString() => $"cells={Offsets.Count} box={Width}x{Height}";
}

/// <summary>
/// 圆形占地（自由放置）：半径是蓝图数据（D12）。
///
/// 半径是<b>边缘到边缘</b>判定的那一半：两座建筑之间要的间距 = 半径之和。
/// 这条不是随便定的 —— 迁移前那侧判的是中心距 <c>MinPlaceDistance</c>，把全部半径
/// 折成 <c>MinPlaceDistance/2</c> 就与现状逐字等价（ticket 05 的迁移前提）。
/// </summary>
/// <param name="Radius">半径，世界坐标单位。0 = 一点不占（柱子与门这类）。</param>
public sealed record RadiusFootprint : Footprint
{
    public RadiusFootprint(double radius)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "占地半径不能是负数");
        }

        Radius = radius;
    }

    public override FootprintKind Kind => FootprintKind.Radius;

    public double Radius { get; }

    public override string ToString()
        => $"radius={Radius.ToString("0.####", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// 蓝图表的一行（D36 的 <c>Blueprint.cs</c>）。
///
/// <para>四个字段对应 spec 的分法：血量上限与工期是建造过程要的（D15/D17，ticket 02 接），
/// 占地是放置要的，模型引用是表现层要的（<c>res://</c> 路径，不按 uid，「按 res:// 引用」）。
/// 模块<b>存而不读</b>模型路径：幽灵模型用真模型文件（「真模型」），但怎么摆、摆多大不在模块里。</para>
///
/// <param name="Id">蓝图 id，动词表与悬停手势都拿它说话。</param>
/// <param name="MaxHealth">血量上限（算术一律 double，「double 与不变文化」）。</param>
/// <param name="Footprint">占地形状。与放置规则不匹配在模块构造时当场抛（D33）。</param>
/// <param name="BuildDurationTicks">建造工期（步数，逻辑每步 0.1 秒，「时间基准」）。缺省 0；
/// 进度倒数是 ticket 02 的活，这一轮字段先占位。</param>
/// <param name="ModelPath">模型文件路径（<c>res://</c>，「按 res:// 引用」）。空串 = 没有。</param>
public sealed record Blueprint
{
    public Blueprint(
        string id,
        double maxHealth,
        Footprint footprint,
        long buildDurationTicks = 0,
        string modelPath = "")
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("蓝图 id 不能空：动词表与手势都靠它说话", nameof(id));
        }

        if (buildDurationTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buildDurationTicks), buildDurationTicks,
                "工期不能是负数");
        }

        Id = id;
        MaxHealth = maxHealth;
        Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        BuildDurationTicks = buildDurationTicks;
        ModelPath = modelPath;
    }

    public string Id { get; }

    public double MaxHealth { get; }

    public Footprint Footprint { get; }

    public long BuildDurationTicks { get; }

    public string ModelPath { get; }

    public override string ToString() => $"{Id} hp={MaxHealth.ToString("0.####", CultureInfo.InvariantCulture)} {Footprint}";
}
