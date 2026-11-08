#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.Construction;

// 区域蓝图（Layout）= 名字 + 成员列表（蓝图 id、相对偏移、自身方向）—— 纯数据，不是状态
// （D23、ADR 0007）。这一族一个副作用都没有：序列化 / 反序列化 / 取最小偏移全是纯函数，
// 离线就能单测；「怎么旋转 / 镜像」也不在这儿，在放置规则那一侧（网格的 D4 八值，
// 见 GridOrientation.TransformLayout）—— 方向整数由放置规则解释（D37），核心只透传。
//
// 文本格式定成一行、分号分段、逗号分字段，是为了同时满足三条硬约束：
//   1. 布局文本是 PlaceLayout 动词的<b>一整格参数</b>，要原样进 Journal（ADR 0007 的
//      「一条记录自足重放」）。所以它不许有竖线（竖线是参数分隔，全表一份，没有转义档）、
//      不许有换行、不许有控制字符 —— 这三条在 LayoutMember / Layout 的构造器里当场判。
//   2. 成员偏移是<b>整数格</b>：网格侧是整数格（D9），自由侧是画线那套格点上的整数
//      （FreePlacementRule.BatchStep）。三种批量手势共用同一个整数枚举域，
//      枚举顺序才有单一真值（D24）；小数偏移会让布局与画线 / 画矩形分家。
//   3. 名字与蓝图 id 不许含分号、逗号、竖线 —— 分隔符进字段就没有转义档，
//      与「不许有竖线」是同一条道理。撞上就在构造时抛，不等到重放端才发现解析不出来。
//
// 文本样例（三个成员）：`v1;north-gate;wall,0,0,0;wall,1,0,0;tower,2,0,1`
// 开头的 `v1` 是格式版本：Journal 里逐字比着这一长串（D34 的 L3 口径天然盖住它），
// 格式改了就该换版本号，而不是让老账解析不出新东西。

/// <summary>
/// 布局的一个成员：哪张蓝图、相对锚点偏哪一格、自己朝哪（D23）。
///
/// <b>成员偏移是整数格</b>，理由写在这一族文件头上。三格分别独立 —— 偏移相对布局的锚点，
/// 方向是<b>成员自己</b>的方向（与布局整体方向复合过的那一份，变换规则写在
/// <see cref="GridOrientation.TransformLayout"/>）。两个都是绝对值，核心不替规则做加法（D37）。
/// </summary>
/// <param name="BlueprintId">蓝图 id。不许含分号、逗号、竖线与控制字符（见文件头第 3 条）。</param>
/// <param name="Offset">相对锚点的整数格偏移。</param>
/// <param name="Orientation">成员自身方向整数，由放置规则解释（D37）。</param>
public sealed record LayoutMember
{
    public LayoutMember(string blueprintId, GridCell offset, int orientation = 0)
    {
        if (string.IsNullOrEmpty(blueprintId))
        {
            throw new ArgumentException("布局成员要带蓝图 id：空 id 那一座放下去无从查表", nameof(blueprintId));
        }

        LayoutText.RequirePlainField(blueprintId, nameof(blueprintId), "布局成员的蓝图 id");
        BlueprintId = blueprintId;
        Offset = offset;
        Orientation = orientation;
    }

    public string BlueprintId { get; }

    public GridCell Offset { get; }

    public int Orientation { get; }

    /// <summary>这一段文本（不含分隔符）。序列化只有这一处拼，解析只有那一处切。</summary>
    public string ToSegment()
        => $"{BlueprintId},{Offset.Col.ToString(CultureInfo.InvariantCulture)}," +
            $"{Offset.Row.ToString(CultureInfo.InvariantCulture)}," +
            $"{Orientation.ToString(CultureInfo.InvariantCulture)}";

    public override string ToString() => ToSegment();
}

/// <summary>
/// 一份区域蓝图：名字 + 成员列表（D23）。<b>数据不是状态</b> —— 模块里没有布局注册表，
/// 捕获只读回文本、放置动词参数自带全文（ADR 0007 的两条 Consequences）。
///
/// <para>不可变：成员表是构造时排定的只读表，没有加成员的入口。要改布局就在外面重新拼一份
/// （原型手上的纯数据），模块不留可改的真值 —— 蓝图不可变是同一条理由（D31）。</para>
///
/// <para><b>0 成员是合法数据</b>：捕获一片空区域就该回 0 成员文本，那不算错（ADR 0007）。
/// 但<b>放置</b> 0 成员布局是脚本作者错误、<b>当场抛</b>（D7）—— 两类失败的分界就落在
/// 「捕获」与「放置」这两个动作上，不在数据本身。</para>
/// </summary>
public sealed record Layout
{
    public Layout(string name, IEnumerable<LayoutMember> members)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("布局要带名字：Capture 那一栏是它、LayoutPlaced 的 Subject 也是它",
                nameof(name));
        }

        LayoutText.RequirePlainField(name, nameof(name), "布局名字");
        ArgumentNullException.ThrowIfNull(members);

        Name = name;
        Members = new List<LayoutMember>(members);
    }

    public string Name { get; }

    /// <summary>成员表，按给定的次序。捕获按枚举序给，变换保持原序（D24 的顺序单一真值）。</summary>
    public IReadOnlyList<LayoutMember> Members { get; }

    /// <summary>0 成员（捕获空区域的那一份）。名字照给，成员是空表。</summary>
    public static Layout Empty(string name) => new(name, Array.Empty<LayoutMember>());

    /// <summary>
    /// 序列化成一行文本（<b>纯函数</b>）。0 成员回 <c>v1;名字;</c> ——
    /// 末尾那个分号是段落分隔，解析那一侧照它认「没有成员」。
    /// </summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append(LayoutText.FormatVersion).Append(';').Append(Name).Append(';');
        for (var i = 0; i < Members.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            sb.Append(Members[i].ToSegment());
        }

        return sb.ToString();
    }

    /// <summary>
    /// 解析一份布局文本（<b>纯函数</b>，不碰世界）。读不出就当场抛（D7 的作者错误那档：
    /// 格式版本不对、字段数不对、数值读不出、名字或蓝图 id 里混进分隔符）。
    ///
    /// <b>0 成员不在这里抛</b>：一份 0 成员文本是合法数据（捕获空区域给的），
    /// 该不该由「放置」来判，判在 <see cref="ConstructionCore.PlaceLayout"/>。
    /// </summary>
    public static Layout Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new FormatException("布局文本是空的：PlaceLayout 少给了一格参数（D7 作者错误当场抛）");
        }

        var parts = text.Split(';');
        if (parts.Length < 2 || parts[0] != LayoutText.FormatVersion)
        {
            throw new FormatException(
                $"布局文本读不懂 | {text}（要的是 {LayoutText.FormatVersion};名字;成员…；" +
                $"当前格式版本是 {LayoutText.FormatVersion}，换版本了就该换这份文本）");
        }

        var name = parts[1];
        if (name.Length == 0)
        {
            throw new FormatException($"布局文本读不懂 | {text}（布局名字不能空，它是 Capture 那一栏与事件 Subject）");
        }

        // 末尾那个分号留出一个空段：它就是「0 成员」。中间任何空段都是写坏的文本，当场抛。
        var memberCount = parts.Length - 2;
        if (memberCount > 0 && parts[^1].Length == 0)
        {
            memberCount--;
        }

        var members = new List<LayoutMember>(memberCount);
        for (var i = 0; i < memberCount; i++)
        {
            var segment = parts[i + 2];
            if (segment.Length == 0)
            {
                throw new FormatException($"布局文本读不懂 | {text}（第 {i + 1} 个成员是空段）");
            }

            var fields = segment.Split(',');
            if (fields.Length != 4)
            {
                throw new FormatException(
                    $"布局文本读不懂 | {segment}（一个成员四个字段：蓝图id,列偏移,行偏移,方向；" +
                    $"读到 {fields.Length} 个。字段里不许再写逗号 —— 没有转义档）");
            }

            members.Add(new LayoutMember(
                fields[0],
                new GridCell(Field(fields[1], "列偏移", segment), Field(fields[2], "行偏移", segment)),
                Field(fields[3], "方向", segment)));
        }

        return new Layout(name, members);
    }

    /// <summary>成员偏移的最小行最小列（变换时取包围盒用）。0 成员回 (0,0)。</summary>
    public GridCell MinOffset()
    {
        if (Members.Count == 0)
        {
            return default;
        }

        var min = Members[0].Offset;
        foreach (var m in Members)
        {
            min = new GridCell(Math.Min(min.Col, m.Offset.Col), Math.Min(min.Row, m.Offset.Row));
        }

        return min;
    }

    public override string ToString() => $"{Name} members={Members.Count}";

    /// <summary>
    /// 一个字段读成整数。<b>解析不出当场抛</b>（D7）：布局文本是原样进 Journal 的东西，
    /// 读不出就说明那条命令写错了 —— 静默折成 0 会把「写错了一个数」变成「布局少了一格，
    /// 落下去的位置和当初想的不一样」，而那一格错在哪没人查得出来。
    /// </summary>
    private static int Field(string text, string what, string segment)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException(
                $"布局文本读不懂 | {segment}（{what} 读不出 |{text}|，要的是整数）");
        }

        return value;
    }
}

/// <summary>
/// 布局文本的字段规矩（不含逐成员读数渲染 —— 那一族在 <see cref="BatchText"/>，一处生成就够）。
/// </summary>
public static class LayoutText
{
    /// <summary>布局文本的格式版本。当前只有 v1；读不懂的版本当场抛（D7），不猜。</summary>
    public const string FormatVersion = "v1";

    /// <summary>字段里不许出现的东西：段落分隔符、字段分隔符、<b>竖线</b>与控制字符。</summary>
    /// <remarks>
    /// 竖线那条是 02 留下的已知限制的直系后代：动词参数是竖线切分、全表一份、没有转义档，
    /// 布局文本要原样进 Journal 当一整格参数（ADR 0007），所以它自己先不许带竖线。
    /// 现在判在构造时，是为了让这个限制在写蓝图表的那一刻就炸，而不是在录完一局重放时。
    /// </remarks>
    public static void RequirePlainField(string value, string parameterName, string what)
    {
        foreach (var ch in value)
        {
            if (ch is ';' or ',' or '|' || char.IsControl(ch))
            {
                throw new ArgumentException(
                    $"{what}里有 {Describe(ch)}：布局文本的分隔符就是 ; 与 ,，而竖线是动词参数的分隔符、" +
                    "全表一份没有转义档。写进 Journal 的布局文本要能原样解析回来，所以这几个字符一律不许进字段。",
                    parameterName);
            }
        }
    }

    /// <summary>
    /// 逐成员裁决表的一行一格：<c>候选点:ok</c> 或 <c>候选点:no:原因</c>。
    ///
    /// <b>键是吸附后的候选点</b>，不是格下标：网格侧格心就是那一格的唯一位置，自由侧是量化点，
    /// 用同一个键渲染一族，表现层拿事件行里那一串就能直接画红格，不必回查格长（D29）。
    /// 空表回 <c>-</c>，与 <see cref="ConstructionNumbers"/> 那两处的口径一样。
    /// </summary>
    public static string Member(PlacementVerdict verdict)
        => verdict.Ok
            ? $"{ConstructionNumbers.Point(verdict.SnappedWorld)}:ok"
            : $"{ConstructionNumbers.Point(verdict.SnappedWorld)}:no:{verdict.Reason}";

    /// <summary>查询预览用的全表（成的与败的都在，D25：预览 = 结果）。</summary>
    public static string Table(IReadOnlyList<PlacementVerdict> members)
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

            sb.Append(Member(members[i]));
        }

        return sb.ToString();
    }

    /// <summary>线与矩形的失败格表（D24 的「逐格裁决」，只列失败的：全成功时回 <c>-</c>）。</summary>
    private static string Describe(char ch)
        => ch switch
        {
            ';' => "分号 ;",
            ',' => "逗号 ,",
            '|' => "竖线 |",
            _ => $"控制字符 U+{(int)ch:X4}",
        };
}
