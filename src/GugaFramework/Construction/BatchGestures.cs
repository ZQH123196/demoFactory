#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace DemoFactory.GugaFramework.Construction;

// 批量手势（画线 / 画矩形）与它那两套语义（D24、D25）：
//
//   - **枚举在模块内，且只在一处**。顺序是单一真值 = 确定性：同一个手势跑两遍，
//     逐成员裁决表逐字全等，重放端才不用去猜「当时是按什么顺序放的」。
//     枚举域是<b>整数格</b>（IPlacementRule.CellOf / CandidateAt 那一对换算），
//     不是世界坐标 —— 世界坐标那一侧是规则的内政（D37）。
//   - <b>部分成功</b>（线与矩形）：能放几座放几座，逐格给原因。边放边判 ——
//     前一座刚占下的格，后一格的 Check 自己会撞上，核心不模拟占用。
//   - <b>全有或全无</b>（布局）：先全判、判过了才落，拒绝一寸不动（D23）。
//     布局这一侧多一件事：成员之间互相压着的格也得算失败，而那些格还没落进世界的
//     占用索引，规则看不见 —— 那一层比对只有核心做得了，理由写在 PlaceLayout 里。
//
// 两套批量语义刻意并存（spec D24）：画墙与拆墙是「有几格算几格」，贴一整块区域蓝图
// 是「要么整块上去要么没动」。混成一套的话，要么画墙画一半留下鬼，要么贴布局贴一半。

/// <summary>
/// 一个批量成员的逐条读数 —— 「这一格 / 这一座，怎么了」（D24 的逐格裁决）。
///
/// <b>四个身份字段各答一个问题</b>：<see cref="Cell"/> 是枚举时排到的那一格、
/// <see cref="BlueprintId"/> 是要放什么、<see cref="Candidate"/> 是递进流水线的那一点、
/// <see cref="Verdict"/> 是同一条 Check 给的裁决。查询路径不带 <see cref="BuildingId"/>
/// （还没放，没有 id）；提交路径补上。
/// </summary>
/// <param name="Cell">枚举出来的那一格。渲染不用它（键取吸附后的候选点，见 <see cref="LayoutText"/>），
/// 它是给调用方「按格号回查」用的。</param>
/// <param name="BlueprintId">这一格 / 这一座要放的蓝图。</param>
/// <param name="Offset">相对锚点的整数格偏移。<b>布局成员有值；线与矩形是 (0,0)</b> ——
/// 它们的成员位置是绝对格，偏移那一栏在批量枚举里没有意义。</param>
/// <param name="Candidate">递进流水线的候选点（规则自己吸附到的那一点）。</param>
/// <param name="Verdict">同一条 Check 流水线的裁决（D4：预览与落建共用它，D29）。</param>
/// <param name="BuildingId">打成了才有（查询路径是空串）。</param>
public sealed record BatchMember(
    GridCell Cell,
    string BlueprintId,
    GridCell Offset,
    Vector2 Candidate,
    PlacementVerdict Verdict,
    string BuildingId = "")
{
    public bool Ok => Verdict.Ok;

    public PlacementReason Reason => Verdict.Reason;

    public override string ToString() => LayoutText.Member(Verdict);
}

/// <summary>
/// 一次批量放置的读数：<b>成功计数 + 逐成员裁决表</b>（D24）。
///
/// <para><see cref="Ok"/> 是<b>「这一发打成了没有」</b>，供动词表那个 ok 字段用：
/// 成功数为 0 就是没打成，<see cref="Reason"/> 给枚举序里第一个失败成员的原因
/// （封闭枚举，能逐字进重放，D34）。打成了但只成了 3/7 也是 ok=true —— 逐格成败在
/// <see cref="Members"/> 与 <see cref="Failures"/> 里，原型按成员算钱（D6 的分界在这一层）。</para>
///
/// <para>布局整体被拒时这一份仍然带着<b>全部</b>成员（谁过了谁没过都在表里），
/// 但 <see cref="Placed"/> 是空的 —— 一条都没落下（D23 的「拒绝一寸不动」）。
/// 事件那一侧只发一条 <see cref="LayoutRejected"/>，成员零事件（D26）。</para>
/// </summary>
public sealed record BatchResult
{
    public BatchResult(IReadOnlyList<BatchMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        Members = members;

        var placed = new List<BatchMember>(members.Count);
        var failed = new List<BatchMember>(members.Count);
        foreach (var m in members)
        {
            // <b>两把尺子，不是一道分界线</b>：Placed 量「落下去了几个」，Failures 量「判不过几个」。
            // 线与矩形（部分成功，D24）里两者互补，一句话说得完；布局整体被拒时（D23）两者中间
            // 空出一段 —— 判过了却没落下的那些成员被整批退回，它们既不在 Placed 也不在 Failures，
            // 只在 <see cref="Members"/> 全表里看得见（谁过了谁没过）。
            //
            // 这里按「有没有发出去号」而不是按裁决分线：按裁决分的话，布局被拒的那一次会报成
            // 「落下了 1 个」，而世界里一条也没落下 —— 原型拿 PlacedCount 去算钱就会付错，
            // 拿 Ok 去决定「这一发打成了没有」也会答错（D34 的 ok 字段）。
            if (m.BuildingId.Length > 0)
            {
                placed.Add(m);
            }

            if (!m.Ok)
            {
                failed.Add(m);
            }
        }

        Placed = placed;
        Failures = failed;
    }

    public IReadOnlyList<BatchMember> Members { get; }

    /// <summary>
    /// 真正落下去了的成员（带建筑 id）。查询路径那一份是空的 —— 还没放，没有号；
    /// 布局整体被拒时也是空的（D23 的「一寸不动」），哪怕表里有成员判过了。
    /// </summary>
    public IReadOnlyList<BatchMember> Placed { get; }

    /// <summary>
    /// 判不过的成员（带原因）。<b>判过了却没落下的不在这里</b> —— 那一档只对布局整体被拒发生，
    /// 它们在 <see cref="Members"/> 全表里（见构造器上的注释）。
    /// </summary>
    public IReadOnlyList<BatchMember> Failures { get; }

    /// <summary>落下去了几个。原型按这一列算钱（D6 的分界在这一层）。</summary>
    public int PlacedCount => Placed.Count;

    /// <summary>判不过几个。逐格原因在 <see cref="Failures"/> 里逐条读。</summary>
    public int FailedCount => Failures.Count;

    public bool Ok => PlacedCount > 0;

    /// <summary>没打成时的原因：枚举序里第一个失败成员的那个。封闭枚举（D6）。</summary>
    public PlacementReason Reason
    {
        get
        {
            foreach (var m in Failures)
            {
                return m.Reason;
            }

            // 没有失败成员却没打成 = 一个成员都没枚举出来。这是回读数不是作者错误
            // （枚举为空的两条路都被各自的构造挡住了），所以给一个封闭档而不是抛。
            return Members.Count == 0 ? PlacementReason.InvalidState : PlacementReason.None;
        }
    }

    /// <summary>打成的那些建筑 id（成功名单，事件与读数都读它）。</summary>
    public IReadOnlyList<string> PlacedIds => IdsOf(Placed);

    public override string ToString()
        => $"placed={ConstructionNumbers.Format(PlacedCount)} failed={ConstructionNumbers.Format(FailedCount)}";

    internal static IReadOnlyList<string> IdsOf(IReadOnlyList<BatchMember> members)
    {
        var ids = new string[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            ids[i] = members[i].BuildingId;
        }

        return ids;
    }
}

/// <summary>
/// 批量拆除（<see cref="ConstructionCore.RemoveRect"/>）的读数。
///
/// <b>回读数恒为打成了</b>：空区域不是错，「这一格没人」不进失败档 ——
/// 腾不出东西来的是世界状态，而批量拆除的「状态性拒绝」只有一种：那一座拆不动
/// （<see cref="ConstructionCore.Remove"/> 回读数失败，那要报出来而不是吞掉）。
/// </summary>
/// <param name="RemovedIds">拆掉的建筑 id，按枚举序首次命中的次序（确定性）。</param>
/// <param name="CellsVisited">走过的格数（矩形的格数，含空格）。</param>
/// <param name="CellsEmpty">其中没占着的格数。</param>
public readonly record struct RectRemovalResult(IReadOnlyList<string> RemovedIds, int CellsVisited, int CellsEmpty)
{
    public int RemovedCount => RemovedIds.Count;

    public bool Ok { get; init; } = true;

    public PlacementReason Reason { get; init; } = PlacementReason.None;

    public override string ToString()
        => $"removed={ConstructionNumbers.Format(RemovedCount)} cells={ConstructionNumbers.Format(CellsVisited)}";
}

/// <summary>
/// 批量枚举 —— <b>模块内那一份顺序的单一真值</b>（D24）。纯函数、无副作用、零引擎依赖。
///
/// <para>两条枚举都返回<b>整数格</b>，不碰世界坐标：格与点之间的换算问放置规则
/// （<see cref="IPlacementRule.CellOf"/> / <see cref="IPlacementRule.CandidateAt"/>），
/// 格长、量化步长、画线步长这些数一个都不进核心（D37、D38）。</para>
///
/// <para>顺序是契约而不是实现细节：先走的先占，先占的先报。事件流与逐成员裁决表都按它，
/// 所以「为什么这座编号是 b4」永远有同一个答案。</para>
/// </summary>
public static class BatchShapes
{
    /// <summary>
    /// 画一条线的成员格：<b>从起点走到终点，两端都含</b>（Bresenham 整数画线）。
    ///
    /// <para>为什么是 Bresenham 而不是「先走横再走竖」：后者在斜线上会走出两倍长的路
    /// （走 5 步变 9 格），而画一排墙时表现层画的那条线与放下来的那排建筑必须是同一条。
    /// 每一格里恰好走一次（列或行严格递增），所以成员表里不会有重复格。</para>
    ///
    /// <para><b>反向也合法</b>：终点在起点前面就是反向走 —— 方向是这一发手势的语义，
    /// 不做归一（与画矩形不同，那一条两个角是等价的，见 <see cref="Rect"/>）。</para>
    /// </summary>
    public static IReadOnlyList<GridCell> Line(GridCell from, GridCell to)
    {
        // 整数上的 Bresenham，误差项走 2 倍整数，全程没有浮点（「double 与不变文化」 那条是给算术的，
        // 这里连 double 都不用：整条枚举的每一步都必须逐字可复现）。
        var dx = Math.Abs(to.Col - from.Col);
        var dy = -Math.Abs(to.Row - from.Row);
        var stepX = from.Col < to.Col ? 1 : -1;
        var stepY = from.Row < to.Row ? 1 : -1;
        var err = dx + dy;
        var col = from.Col;
        var row = from.Row;

        var cells = new List<GridCell>();
        while (true)
        {
            cells.Add(new GridCell(col, row));
            if (col == to.Col && row == to.Row)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                col += stepX;
            }

            if (e2 <= dx)
            {
                err += dx;
                row += stepY;
            }
        }

        return cells;
    }

    /// <summary>
    /// 画一个矩形的成员格：<b>两角都含，按行再按列</b>（先行后列，与
    /// <see cref="CellFootprint"/> 的偏移表同一个次序口径）。
    ///
    /// <para><b>两角自己按 min/max 归一</b>：表现层从哪一角起拖都给出同一个成员表 ——
    /// 矩形的两个角是等价的，不等价的是「起点 / 终点」（那是画线）。</para>
    /// </summary>
    public static IReadOnlyList<GridCell> Rect(GridCell a, GridCell b)
    {
        var min = new GridCell(Math.Min(a.Col, b.Col), Math.Min(a.Row, b.Row));
        var max = new GridCell(Math.Max(a.Col, b.Col), Math.Max(a.Row, b.Row));

        var cells = new List<GridCell>((max.Col - min.Col + 1) * (max.Row - min.Row + 1));
        for (var row = min.Row; row <= max.Row; row++)
        {
            for (var col = min.Col; col <= max.Col; col++)
            {
                cells.Add(new GridCell(col, row));
            }
        }

        return cells;
    }
}

/// <summary>
/// 批量手势读数的渲染 —— 事件明细、动词返回值、测试断言都读这一份。
///
/// <para>「同一句话在一处生成」在这里的具体含义：预览的逐成员表（查询路径）与事件的
/// 失败格表（提交路径）渲染的是同一次手势的同一条流水线，两者必须逐字对得上（D29）。
/// 各拼一次的话，表现层会拿预览那串红格去对事件那串失败行，对不上时它只会以为显示有 bug。</para>
/// </summary>
public static class BatchText
{
    /// <summary>线与矩形：<c>ok=… reason=… placed=N failed=M table=…</c>。</summary>
    public static string Line(BatchResult result)
    {
        var sb = new StringBuilder();
        sb.Append("ok=").Append(result.Ok ? '1' : '0')
            .Append(" reason=").Append(result.Reason)
            .Append(" placed=").Append(ConstructionNumbers.Format(result.PlacedCount))
            .Append(" failed=").Append(ConstructionNumbers.Format(result.FailedCount))
            .Append(" table=").Append(Table(result.Members))
            .Append(" ids=").Append(ConstructionNumbers.Ids(result.PlacedIds));
        return sb.ToString();
    }

    /// <summary>布局：同一条读数加一行成员表与名字（名字是 Subject，失败成员表多带蓝图 id）。</summary>
    public static string LayoutLine(string layoutName, BatchResult result)
        => Line(result) + $" layout={layoutName} failures={LayoutFailures(result)}";

    /// <summary>批量拆除：<c>ok=… reason=… removed=N cells=M empty=K</c>。</summary>
    public static string Line(RectRemovalResult result)
    {
        var sb = new StringBuilder();
        sb.Append("ok=").Append(result.Ok ? '1' : '0')
            .Append(" reason=").Append(result.Reason)
            .Append(" removed=").Append(ConstructionNumbers.Format(result.RemovedCount))
            .Append(" cells=").Append(ConstructionNumbers.Format(result.CellsVisited))
            .Append(" empty=").Append(ConstructionNumbers.Format(result.CellsEmpty))
            .Append(" ids=").Append(ConstructionNumbers.Ids(result.RemovedIds));
        return sb.ToString();
    }

    /// <summary>逐成员全表（成的与败的都在）。查询预览与逐格断言读它。</summary>
    public static string Table(IReadOnlyList<BatchMember> members)
    {
        if (members.Count == 0)
        {
            return "-";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            sb.Append(LayoutText.Member(members[i].Verdict));
        }

        return sb.ToString();
    }

    /// <summary>线与矩形的失败格表（D24：只列失败的，全成功回 <c>-</c>）。</summary>
    public static string FailureCells(BatchResult result)
    {
        if (result.Failures.Count == 0)
        {
            return "-";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < result.Failures.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            var m = result.Failures[i];
            sb.Append(ConstructionNumbers.Point(m.Verdict.SnappedWorld)).Append(':').Append(m.Reason);
        }

        return sb.ToString();
    }

    /// <summary>布局的失败成员表（D26：位置 + 蓝图 id + 原因）。</summary>
    public static string LayoutFailures(BatchResult result)
    {
        if (result.Failures.Count == 0)
        {
            return "-";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < result.Failures.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            var m = result.Failures[i];
            sb.Append(ConstructionNumbers.Point(m.Verdict.SnappedWorld))
                .Append(':').Append(m.BlueprintId)
                .Append(':').Append(m.Reason);
        }

        return sb.ToString();
    }
}
