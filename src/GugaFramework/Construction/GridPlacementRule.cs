#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace DemoFactory.GugaFramework.Construction;

/// <summary>
/// 网格放置的 D4 八值（D11）—— <b>整数编码，语义全在这一个文件里</b>（D37）。
///
/// 编码：0~3 = 旋转 k×90°，4~7 = <b>先镜像再</b>旋转 k×90°。这个「先镜像」还是「先旋转」spec
/// 没钉（它只说了 4 旋转 × 镜像），选一个定下来写在这儿的注释里，是为了 8 个值各自对应一个
/// 确定的格形、且取模关系自洽；换一种编码只要改这一个方法，别处照旧。
///
/// 变换在<b>本地包围盒内</b>做（不是绕锚点转）：绕锚点转会让 2×2 的旋转把整块格挪一格，
/// 而 D21 推出来的是「2×2 原地旋转恒成功」—— 绕盒心转才等于「原地」。
/// 顺带一个便宜的好处：盒归一化后 min 行 min 列恒为 0，变换后仍是 0，
/// 锚点格不动、偏移非负这两条不变量不用再补一次校正。
/// </summary>
public static class GridOrientation
{
    public const int R0 = 0;
    public const int R90 = 1;
    public const int R180 = 2;
    public const int R270 = 3;
    public const int M0 = 4;
    public const int M90 = 5;
    public const int M180 = 6;
    public const int M270 = 7;

    /// <summary>八值的个数。越界的方向当场抛（D7 作者错误那档），不静默折回 0。</summary>
    public const int Count = 8;

    /// <summary>读法（快照与事件行里印的），只给人看。</summary>
    public static string Label(int orientation) => (orientation & 4) != 0
        ? $"mirror+{(orientation & 3) * 90}"
        : $"rot{(orientation & 3) * 90}";

    /// <summary>把占地按方向变换一次（纯函数，同输入同输出，无副作用）。</summary>
    public static CellFootprint Transform(CellFootprint footprint, int orientation)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        if (orientation is < 0 or >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation,
                $"网格放置的方向是 D4 八值 0~{Count - 1}，超出范围是作者错误（D7：当场抛，不折回 0）");
        }

        var cells = new List<GridCell>(footprint.Offsets.Count);
        foreach (var o in footprint.Offsets)
        {
            cells.Add(o);
        }

        var width = footprint.Width;
        var height = footprint.Height;
        for (var i = 0; i < cells.Count; i++)
        {
            cells[i] = TransformPoint(cells[i], ref width, ref height, orientation);
        }

        // 归一化一次（重排 + 去掉可能的重复）。已经归一过了，但走同一份代码比在这里
        // 另写一套校正便宜：这一份就是 FromCells 那条唯一真值。
        return CellFootprint.FromCells(cells);
    }

    /// <summary>
    /// 一个格在盒内被方向变换一次（<b>纯函数</b>）。
    ///
    /// <para>box 是就地改的：每次 90° 旋转都让盒宽高互换，而下一次旋转要用换过之后的盒 ——
    /// 漏掉那一步，「转 180°」会拿原盒去算两次 90°，宽高就不对了。这是 <see cref="Transform"/>
    /// 与布局变换共用的同一份算术：占地变换与布局变换必须同款，否则同一块地在两处会转出两个形。</para>
    /// </summary>
    /// <param name="cell">盒内坐标（非负）。</param>
    /// <param name="width">盒宽，就地更新为变换后的宽。</param>
    /// <param name="height">盒高，就地更新为变换后的高。</param>
    /// <param name="orientation">D4 八值 0~7（越界当场抛，理由同 <see cref="Transform"/>）。</param>
    private static GridCell TransformPoint(GridCell cell, ref int width, ref int height, int orientation)
    {
        if (orientation is < 0 or >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation,
                $"网格放置的方向是 D4 八值 0~{Count - 1}，超出范围是作者错误（D7：当场抛，不折回 0）");
        }

        var col = cell.Col;
        var row = cell.Row;

        if ((orientation & 4) != 0)
        {
            // 镜像：沿盒子的竖中线翻，列变、行不变。盒宽不变。
            col = width - 1 - col;
        }

        for (var k = 0; k < (orientation & 3); k++)
        {
            // 旋转 90°：(列,行) → (高-1-行, 列)，盒宽高互换。转完再转回 0 是恒等的。
            var next = new GridCell(height - 1 - row, col);
            col = next.Col;
            row = next.Row;
            var swap = width;
            width = height;
            height = swap;
        }

        return new GridCell(col, row);
    }

    /// <summary>
    /// 两个方向值的复合：<b>先走 <paramref name="inner"/>，再走 <paramref name="outer"/></b>
    /// 之后该给的方向整数（纯函数）。
    ///
    /// <para>为什么核心不能自己做：方向整数是「4 旋转 × 镜像」的八值编码，而<b>镜像与旋转
    /// 不可交换</b> —— 先镜像再转 90° 与先转 90° 再镜像是两块不同的形。复合法则
    /// <c>rot = outer.k ± inner.k（outer 是不是镜像决定正负）、mirror = 两边异或</c>
    /// 是一份 D4 的群结构知识，它属于解释方向的规则，不属于只透传整数的核心（D37）。
    /// 自由放置那一份是角度相加，形状完全不同：两个 adapter 各给一份，核心一个都不碰。</para>
    ///
    /// <para>它要垫在两处：编辑器的「转一步」与布局的整体旋转（成员自身方向要跟着布局一起转）。</para>
    /// </summary>
    public static int Compose(int inner, int outer)
    {
        RequireRange(inner, nameof(inner));
        RequireRange(outer, nameof(outer));

        var innerMirror = (inner & 4) != 0;
        var outerMirror = (outer & 4) != 0;
        var innerRot = inner & 3;
        var outerRot = outer & 3;

        // 镜像会把旋转方向反过来（S·R = R⁻¹·S），所以 outer 是镜像时 inner 的转角要取负。
        var rot = outerRot + (outerMirror ? -innerRot : innerRot);
        rot = ((rot % 4) + 4) % 4;
        var mirror = (innerMirror ^ outerMirror) ? 4 : 0;
        return mirror | rot;
    }

    /// <summary>
    /// 把一份区域蓝图按 D4 八值转 / 镜像一次（<b>纯函数：数据进数据出、无副作用</b>，D23、ADR 0007）。
    ///
    /// <para><b>在布局自己的包围盒内做</b>，与 <see cref="Transform"/>（占地）同一条路：盒取全部
    /// 成员偏移的最小行最小列，转完 min 仍是 0，所以成员偏移转完依然是非负格
    /// （D9 那条归一化不变量在布局这一侧照样成立）。</para>
    ///
    /// <para><b>成员自身方向跟着一起复合</b>（<see cref="Compose"/>）：只转偏移的话每座建筑
    /// 还朝着原来的方向，那不叫转了一整块。</para>
    /// </summary>
    public static Layout TransformLayout(Layout layout, int orientation)
    {
        ArgumentNullException.ThrowIfNull(layout);
        RequireRange(orientation, nameof(orientation));

        if (layout.Members.Count == 0)
        {
            return layout;
        }

        var min = layout.MinOffset();
        var width = 1;
        var height = 1;
        foreach (var m in layout.Members)
        {
            width = Math.Max(width, m.Offset.Col - min.Col + 1);
            height = Math.Max(height, m.Offset.Row - min.Row + 1);
        }

        var moved = new List<LayoutMember>(layout.Members.Count);
        foreach (var m in layout.Members)
        {
            var w = width;
            var h = height;
            var offset = TransformPoint(new GridCell(m.Offset.Col - min.Col, m.Offset.Row - min.Row),
                ref w, ref h, orientation);
            moved.Add(new LayoutMember(m.BlueprintId, offset, Compose(m.Orientation, orientation)));
        }

        return new Layout(layout.Name, moved);
    }

    private static void RequireRange(int orientation, string what)
    {
        if (orientation is < 0 or >= Count)
        {
            throw new ArgumentOutOfRangeException(what, orientation,
                $"网格放置的方向是 D4 八值 0~{Count - 1}，超出范围是作者错误（D7：当场抛，不折回 0）");
        }
    }
}

/// <summary>
/// 网格放置 —— 吸附到整数格、按格占用的那条规则（D9~D11）。
///
/// 格长与有限矩形的四角都在构造时定；四角之后只允许 <see cref="ExpandBounds"/> 外扩
/// （甲板扩容那一路），不许内缩。格表与边界都是这一步钉死的，gizmo 从
/// <see cref="GizmoParams"/> 读同一组数（画出来的网格与逻辑判定的网格因此是同一组，D29）。
/// </summary>
public sealed class GridPlacementRule : IPlacementRule
{
    private readonly Dictionary<GridCell, string> _occupants = new();
    private readonly Dictionary<string, IReadOnlyList<GridCell>> _owned = new(StringComparer.Ordinal);
    private readonly IConstructionHost? _host;
    private readonly Dictionary<string, double> _gizmo;

    public GridPlacementRule(
        double cellSize,
        int minCol,
        int minRow,
        int maxCol,
        int maxRow,
        IConstructionHost? host = null)
    {
        if (cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "格长必须是正数");
        }

        if (minCol > maxCol || minRow > maxRow)
        {
            throw new ArgumentException(
                $"边界矩形的角反了：col {minCol}..{maxCol}、row {minRow}..{maxRow}", nameof(minCol));
        }

        CellSize = cellSize;
        MinCol = minCol;
        MinRow = minRow;
        MaxCol = maxCol;
        MaxRow = maxRow;
        _host = host;
        _gizmo = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["cellSize"] = cellSize,
            ["minCol"] = minCol,
            ["minRow"] = minRow,
            ["maxCol"] = maxCol,
            ["maxRow"] = maxRow,
        };
    }

    public string RuleLabel => "网格放置";

    public FootprintKind Kind => FootprintKind.Cells;

    public double CellSize { get; }

    public int MinCol { get; private set; }

    public int MinRow { get; private set; }

    public int MaxCol { get; private set; }

    public int MaxRow { get; private set; }

    /// <summary>
    /// 边界外扩（扩容用，<b>只准外扩、不准内缩</b>）：新矩形必须完整盖住旧矩形，
    /// 缩边当场抛 —— 内缩会让已经压在旧边界上的建筑「在册却在界外」，
    /// 拆掉它之后 <see cref="Vacate"/> 腾回的格落在界外，占用表与边界当场对不上账。
    ///
    /// <para><b>占用表不动</b>：扩容只是把「界外」那圈格变成「界内」，
    /// 已在册建筑的格照旧占着；<see cref="GizmoParams"/> 里那四个数同步改写，
    /// 画出来的网格与逻辑判定的网格保持同一组（D29）。</para>
    /// </summary>
    public void ExpandBounds(int minCol, int minRow, int maxCol, int maxRow)
    {
        if (minCol > maxCol || minRow > maxRow)
        {
            throw new ArgumentException(
                $"扩容后的角反了：col {minCol}..{maxCol}、row {minRow}..{maxRow}", nameof(minCol));
        }

        if (minCol > MinCol || maxCol < MaxCol || minRow > MinRow || maxRow < MaxRow)
        {
            throw new ArgumentException(
                $"扩容只准外扩不准内缩：{BoundsText} → col {minCol}..{maxCol}、row {minRow}..{maxRow}"
                + "（内缩会让在册建筑落在界外，占用表与边界对不上账）", nameof(maxCol));
        }

        if (minCol == MinCol && maxCol == MaxCol && minRow == MinRow && maxRow == MaxRow) return;

        MinCol = minCol;
        MinRow = minRow;
        MaxCol = maxCol;
        MaxRow = maxRow;
        _gizmo["minCol"] = minCol;
        _gizmo["minRow"] = minRow;
        _gizmo["maxCol"] = maxCol;
        _gizmo["maxRow"] = maxRow;
    }

    public string BoundsText
        => $"col={MinCol}..{MaxCol},row={MinRow}..{MaxRow}";

    public IReadOnlyDictionary<string, double> GizmoParams => _gizmo;

    public int OccupiedCellCount => _occupants.Count;

    /// <summary>世界坐标 → 格。<b>floor 不是截断</b>（D9）：负坐标那侧也必须能落进 -1 格。</summary>
    public static GridCell CellAt(Vector2 world, double cellSize)
        => new((int)Math.Floor(world.X / cellSize), (int)Math.Floor(world.Y / cellSize));

    /// <summary>格 → 世界坐标（格心）。世界落点只有这一条推导，不存第二份（D9）。
    /// 算术在 double 上做完，落进 <see cref="Vector2"/> 时才收成 float（「double 与不变文化」 的算术与
    /// 坐标容器是两件事：格长与落点推导不许先被 float 截一刀）。</summary>
    public static Vector2 CenterOf(GridCell cell, double cellSize)
        => new((float)((cell.Col + 0.5) * cellSize), (float)((cell.Row + 0.5) * cellSize));

    public PlacementVerdict Check(PlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var footprint = request.Blueprint.Footprint as CellFootprint
            ?? throw new InvalidOperationException(
                $"{RuleLabel} 吃格占地，蓝图 {request.Blueprint.Id} 给的是 {request.Blueprint.Footprint.Kind}" +
                "（形状与规则不匹配当场抛，D33；不静默折成「零格建筑」）");

        var anchor = CellAt(request.Candidate, CellSize);
        var cells = GridOrientation.Transform(footprint, request.Orientation).At(anchor);
        var world = CenterOf(anchor, CellSize);

        PlacementVerdict Verdict(bool ok, PlacementReason reason)
            => new(ok, reason, world, request.Orientation, cells, anchor, null);

        foreach (var c in cells)
        {
            if (c.Col < MinCol || c.Col > MaxCol || c.Row < MinRow || c.Row > MaxRow)
            {
                return Verdict(false, PlacementReason.OutOfBounds);
            }
        }

        foreach (var c in cells)
        {
            if (_occupants.TryGetValue(c, out var holder) && holder == request.ExcludeBuildingId)
            {
                // 编辑动词排除的是「占用者是不是它自己」（D20）：挪一格时新占地与旧占地压着同一格
                // 是常事，不排除就一格都动不了。别人的一格照样挡住 —— 这里放行的只有它自己那几格。
                continue;
            }

            if (_occupants.ContainsKey(c))
            {
                return Verdict(false, PlacementReason.Occupied);
            }
        }

        if (_host is not null)
        {
            foreach (var c in cells)
            {
                if (!_host.IsBuildable(BuildSite.At(c)))
                {
                    return Verdict(false, PlacementReason.NotBuildable);
                }
            }
        }

        return Verdict(true, PlacementReason.None);
    }

    public void Occupy(string buildingId, PlacementVerdict placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        if (placed.Cells.Count == 0)
        {
            throw new InvalidOperationException(
                $"登记 {buildingId} 的占用时格名单是空的：那是裁决没跑就被登记了，查 Check 那一侧");
        }

        if (_owned.ContainsKey(buildingId))
        {
            throw new InvalidOperationException(
                $"{buildingId} 已经在册了，重复登记占用会漏腾一次格");
        }

        // 先验后改：这批格里有一格被占着就整条不落，索引不留半套状态。
        foreach (var c in placed.Cells)
        {
            if (_occupants.TryGetValue(c, out var holder))
            {
                throw new InvalidOperationException(
                    $"格 {c} 已经被 {holder} 占着，还要登记 {buildingId} —— 同一批格里有一格被数了两次");
            }
        }

        foreach (var c in placed.Cells)
        {
            _occupants[c] = buildingId;
        }

        _owned[buildingId] = placed.Cells;
    }

    /// <summary>
    /// 场地建筑登记：<b>照 <see cref="Check"/> 同一份算术</b>算锚点格与变换后的占地（D4、D9），
    /// 然后把那批<b>还空着的</b>格占下。刻意不查边界、不查宿主、不查有没有人占着 ——
    /// 场地建筑摆在哪就是哪。
    ///
    /// <para><b>与 <see cref="Occupy"/> 的差别只有一处，而那一处是被格表的形状逼出来的</b>：
    /// 格表是<b>一格一主</b>（<c>_occupants</c> 一格一个占用者），所以已经被占的那几格
    /// <b>原地留给原占用者</b>，场地建筑不夺。让后来者夺走是能写的，但拆掉场地建筑之后
    /// 那几格就没人认领了 —— 册里那座建筑变成「在册却一格都不占」，
    /// 「这一格占着谁」与「册上有什么」当场给出两个答案（显示即真相破掉，D16 的注释里写着这一条）。
    /// 要记住被夺走的原占用者就得在格表上再叠一层归属，那是规则的私政（D38），而这一格
    /// 今天还没有真实消费者。</para>
    ///
    /// <para><b>「先登记的先占」这件事的后果要说在明处</b>：场地建筑压着的那几格，
    /// 在原占用者被拆掉之后不再受它保护（禁区会缩回去）。要一块永久成立的禁区，
    /// 就把场地建筑摆在没人占的格上 —— 自由规则那一侧没有这个限制（间距表里两座挨着是常事）。</para>
    /// </summary>
    public PlacementVerdict OccupyFixture(string buildingId, Blueprint blueprint, Vector2 position,
        int orientation)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var footprint = blueprint.Footprint as CellFootprint
            ?? throw new InvalidOperationException(
                $"{RuleLabel} 吃格占地，蓝图 {blueprint.Id} 给的是 {blueprint.Footprint.Kind}" +
                "（形状与规则不匹配当场抛，D33；不静默折成「零格建筑」）");

        if (_owned.ContainsKey(buildingId))
        {
            throw new InvalidOperationException(
                $"{buildingId} 已经占着格了，重复登记会漏腾一次格");
        }

        var anchor = CellAt(position, CellSize);
        var cells = GridOrientation.Transform(footprint, orientation).At(anchor);
        var verdict = new PlacementVerdict(true, PlacementReason.None, CenterOf(anchor, CellSize),
            orientation, cells, anchor, null);

        // 一格一主：只把还空着的那些格认下来（保住「册上有的都占着地」这条恒等式）。
        var claimed = new List<GridCell>(cells.Count);
        foreach (var c in cells)
        {
            if (!_occupants.ContainsKey(c))
            {
                _occupants[c] = buildingId;
                claimed.Add(c);
            }
        }

        _owned[buildingId] = claimed;
        return verdict;
    }

    public void Vacate(string buildingId)
    {
        if (!_owned.TryGetValue(buildingId, out var cells))
        {
            throw new InvalidOperationException(
                $"{buildingId} 没有占用登记，腾占用这一发打空了 —— 移除链与损毁链都得走 Vacate");
        }

        foreach (var c in cells)
        {
            if (_occupants.TryGetValue(c, out var holder) && holder == buildingId)
            {
                _occupants.Remove(c);
            }
        }

        _owned.Remove(buildingId);
    }

    public string? BuildingIdAt(Vector2 point)
        => _occupants.TryGetValue(CellAt(point, CellSize), out var id) ? id : null;

    public GridCell CellOf(Vector2 world) => CellAt(world, CellSize);

    public Vector2 CandidateAt(GridCell cell) => CenterOf(cell, CellSize);

    public Layout TransformLayout(Layout layout, int orientation)
        => GridOrientation.TransformLayout(layout, orientation);

    public override string ToString()
        => $"{RuleLabel} cellSize={CellSize.ToString(CultureInfo.InvariantCulture)} {BoundsText}";
}
