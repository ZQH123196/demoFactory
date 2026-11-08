#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace DemoFactory.GugaFramework.Construction;

// 放置规则这条 seam 的公共词汇：请求、裁决、拒绝原因、手势描述、读写结果。
//
// 四条形状规矩（事件形状那条照 ADR 0001 与 AbilityEvents 的口径，其余三条来自 spec 的
// D5~D7 / D27 / D37）：
//   1. 方向是 int，由放置规则解释（D37）：网格侧是 D4 八值，自由侧是量化角（整数度）。
//      模块核心只透传这个整数 —— 加第三种规则（六角格，D39）时核心一个字不改。
//   2. 拒绝原因是封闭枚举（D6）。模块不认识钱：钱不够这类原因不在枚举里，
//      枚举里只有「放不放得下」这一层答案。成功那档是 None，别拿「成功」去凑某个失败值。
//   3. 悬停态（手势描述、悬停裁决）只住在 HoverVerdicts，既不进请求也不进任何快照
//      （D28、ADR 0006）—— 视图侧状态在确定性面上零存在。
//   4. 批量手势（ticket 04）把形状接上了：手势描述的四个形状都推得进去，枚举在模块内
//      走整数格（BatchGestures），格与候选点之间的换算一律问放置规则（下面 IPlacementRule
//      的 CellOf / CandidateAt）—— 核心不解释格，它只是把一串整数格问成点（D37、D38）。

/// <summary>一个格。列与行都是整数；网格侧的坐标一律经它进出，核心不持别的格表示。</summary>
/// <param name="Col">列号（x 方向）。</param>
/// <param name="Row">行号（y 方向）。</param>
public readonly record struct GridCell(int Col, int Row)
{
    public static GridCell operator +(GridCell a, GridCell b) => new(a.Col + b.Col, a.Row + b.Row);

    public static GridCell operator -(GridCell a, GridCell b) => new(a.Col - b.Col, a.Row - b.Row);

    /// <summary>快照与事件行里的写法（区域无关，「double 与不变文化」）。</summary>
    public override string ToString()
        => $"{Col.ToString(CultureInfo.InvariantCulture)},{Row.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// 放置拒绝的封闭原因（D6）。
///
/// 封闭的用处有两个：原型拿它当「不扣款」的信号（拒绝 = 这一发没花钱），以及
/// 逐字序列化进 inject 的 out reason（有限枚举才谈得上「可重放」，arch-review issues/10）。
/// 经济类原因（钱不够、没人口）刻意不在这里 —— 模块不认识钱（D3）。
/// </summary>
public enum PlacementReason
{
    /// <summary>裁决成了。带成功语义的那一档：不是「原因未知」。</summary>
    None = 0,

    /// <summary>落在有限矩形之外（网格侧的边界，D10）。</summary>
    OutOfBounds = 1,

    /// <summary>有别的建筑占着要占的格（网格侧）。</summary>
    Occupied = 2,

    /// <summary>边缘到边缘间距不够（自由侧，D12）。</summary>
    TooClose = 3,

    /// <summary>宿主说这一处不可建（D8 的可建性查询）。</summary>
    NotBuildable = 4,

    /// <summary>蓝图 id 不在表里。</summary>
    UnknownBlueprint = 5,

    /// <summary>建筑 id 不在册。</summary>
    UnknownId = 6,

    /// <summary>建筑当前状态不允许这个动作（暂停已完工建筑、满血 Repair 那类）。</summary>
    InvalidState = 7,
}

/// <summary>占地的家族成员（Blueprint 那一列数据）。Box 是 D14 的留待未来，加变体不改接口。</summary>
public enum FootprintKind
{
    /// <summary>一组格（网格放置）。</summary>
    Cells = 0,

    /// <summary>一个圆（自由放置）。</summary>
    Radius = 1,
}

/// <summary>
/// 手势描述 —— 表现层推进模块的原始描述，不是坐标（D27）。
///
/// 表现层推的是「想在这放这个、朝向这样」，不是「这个点行不行」；吸附、校验、裁决全在模块里。
/// <b>四个形状都接上了</b>（ticket 04，D25）：单件 / 画线 / 画矩形 / 布局。
/// 每一格放什么由 <see cref="Shape"/> 说，缺件当场抛（<see cref="Validate"/>）——
/// 作者错误抛，不静默演成「只放了一个」。
/// </summary>
/// <param name="BlueprintId">蓝图 id。单件 / 画线 / 画矩形是主蓝图；<b>布局形状用不到这一格</b>（留空串）。</param>
/// <param name="Candidate">候选点（表现层那边读到的原始位置，未吸附）。画线是起点、画矩形是 min 那一角、布局是锚点。</param>
/// <param name="Orientation">方向整数，由放置规则解释（D37）。</param>
/// <param name="LayoutText">布局文本（区域蓝图，ticket 04）；只有布局形状读它。</param>
/// <param name="Shape">批量形状：单件 / 画线 / 画矩形 / 布局（D25）。</param>
/// <param name="LineEnd">画线的终点；<b>画线形状缺这一格当场抛</b>（否则只剩起点那一个）。</param>
/// <param name="RectMax">画矩形的 max 那一角；<b>画矩形形状缺这一格当场抛</b>。
/// 与 min 的次序无关 —— 核心自己按 min/max 归一，表现层从哪一角起拖都一样。</param>
public sealed record GestureSpec(
    string BlueprintId,
    Vector2 Candidate,
    int Orientation = 0,
    string? LayoutText = null,
    GestureShape Shape = GestureShape.Single,
    Vector2? LineEnd = null,
    Vector2? RectMax = null)
{
    /// <summary>
    /// 手势自检（D7 的作者错误那档）：<b>缺件当场抛，多给的件不挡</b>。
    ///
    /// <para>缺件必须抛的理由与旧版那条「推非单件形状当场抛」是同一条：画线手势没给终点，
    /// 静默放一个就等于「画了 7 格的墙却只建起 1 座」，而表现层看不出来 —— 那正是
    /// 「显示即真相」要挡的病（D27）。</para>
    ///
    /// <para>多给的不挡（画矩形顺手带了个布局文本、布局形状带了个蓝图 id）是有意的：
    /// 表现层切工具时手上还握着上一份参数很常见，为一个没造成错误的状态抛异常
    /// 只会让人学会在调用点外面包 try。</para>
    ///
    /// <b>在 <see cref="HoverVerdicts.Push"/> 存进当前手势之前调</b>：存进去再抛，
    /// 当前手势就成了一颗雷 —— 之后每次占用变化触发的重算都会跟着抛第二次。
    /// </summary>
    public void Validate()
    {
        switch (Shape)
        {
            case GestureShape.Single:
            case GestureShape.Line:
            case GestureShape.Rect:
                if (string.IsNullOrEmpty(BlueprintId))
                {
                    throw new InvalidOperationException(
                        $"{Shape} 手势要带蓝图 id：空 id 没有裁决可读（D7 作者错误当场抛）");
                }

                break;
            case GestureShape.Layout:
                if (string.IsNullOrEmpty(LayoutText))
                {
                    throw new InvalidOperationException(
                        $"布局手势要带布局文本：空文本放下去是 0 个成员，那是作者错误（D7）");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Shape), Shape,
                    $"批量形状只有 {GestureShape.Single} / {GestureShape.Line} / {GestureShape.Rect} / " +
                    $"{GestureShape.Layout} 四个值（D25）");
        }

        if (Shape == GestureShape.Line && LineEnd is null)
        {
            throw new InvalidOperationException(
                "画线手势缺终点：只放起点那一个等于静默折成单件（D7 作者错误当场抛）");
        }

        if (Shape == GestureShape.Rect && RectMax is null)
        {
            throw new InvalidOperationException(
                "画矩形手势缺 max 那一角：只放 min 那一格等于静默折成单件（D7 作者错误当场抛）");
        }
    }
}

/// <summary>批量形状的家族（D25）。单件是这一轮唯一能推的。</summary>
public enum GestureShape
{
    Single = 0,
    Line = 1,
    Rect = 2,
    Layout = 3,
}

/// <summary>
/// 一次放置查询的请求 —— <b>模块内部的 seam 词汇</b>。
///
/// 蓝图由模块查表解析好才递进来（外面对着的是蓝图 id，见 <see cref="ConstructionCore"/>），
/// 这么定是为了表只有一份：让规则再拿 id 查一遍就多了一份真值。
/// </summary>
/// <param name="Blueprint">已解析的蓝图（占地数据在它身上）。</param>
/// <param name="Candidate">候选点，规则自己吸附（网格 floor / 自由量化）。</param>
/// <param name="Orientation">方向整数，由规则解释（D37）。</param>
/// <param name="ExcludeBuildingId">占用检查排除谁 —— 编辑动词（Move / Rotate）把自己递进来（D20、D21）。
/// 置空就是一次普通的新放置，不排除任何东西。
///
/// <para>为什么是请求上的一个 id 而不是核心先把旧占用腾掉再查：腾了再查的那一瞬间占用索引与册
/// 是不同步的，中途任何一次回读（悬停重算、事件订阅者回查）读到的就是「这座不在了」；
/// 而且两条规则各有各的占用结构（格表 / 间距表），核心要腾就得先懂那份结构，那正是 D38 要挡的。
/// 规则自己跳过「占用者是不是它自己」，几何与索引都留在自己这一侧。</para></param>
public sealed record PlacementRequest(
    Blueprint Blueprint,
    Vector2 Candidate,
    int Orientation,
    string? ExcludeBuildingId = null);

/// <summary>
/// 一次放置查询的裁决（D5）—— 悬停、落建、直达口三路共用的唯一真值（D4）。
///
/// <b>拒绝时落点与占地照样给全</b>：幽灵要画红的那个形状，缺了形状红在哪就说不清了。
/// 网格侧给受影响格名单（<paramref name="Cells"/>），自由侧给圆形占地
/// （<paramref name="Circle"/> 的半径，中心就是 <paramref name="SnappedWorld"/>——中心不进
/// 轮廓字段是因为落点那一列本来就有，同一个数存两份就是两份真值）。
/// 表现层画幽灵用哪边就读哪边，另一边是空；不许自己再算一遍（D29）。
/// </summary>
/// <param name="Ok">放不放得下。</param>
/// <param name="Reason">拒绝原因（D6 封闭枚举）；成了是 <see cref="PlacementReason.None"/>。</param>
/// <param name="SnappedWorld">吸附后的落点（网格 = 锚点格心推导；自由 = 量化点）。</param>
/// <param name="Orientation">吸附/量化之后的方向整数。</param>
/// <param name="Cells">受影响格名单（网格侧）；自由侧是空表。</param>
/// <param name="Anchor">登记的锚点格（网格侧，D9：登记锚点格就等于登记占地）；
/// 自由侧是 null —— 圆形占地的中心就是 <paramref name="SnappedWorld"/>，不存第二份。</param>
/// <param name="Circle">圆形占地（自由侧）；网格侧是 null。</param>
/// <param name="Members">批量手势的逐成员裁决表（ticket 04）；单件是 null。
///
/// <para>批量形状（画线 / 画矩形 / 布局）这一次查询的整体裁决：<b>全部成员都放得下才是
/// <c>Ok=true</c></b>，<see cref="Reason"/> 取<b>枚举序里第一个失败成员</b>的原因。
/// 「全有或全无」是汇总那一层的读法 —— 逐格能不能放全在 <c>Members</c> 里
/// （线与矩形是部分成功语义，D24），表现层画红格读的是 <c>Members</c>，不是这一个 <c>Ok</c>。</para>
///
/// <para>汇总那几列（落点 / 占地 / 锚点 / 圆）刻意留空：批量没有「一个落点」，
/// 硬凑一个出来就是在两处各给一个答案（D29）。唯一要看的是 <c>Members</c>。</para></param>
public sealed record PlacementVerdict(
    bool Ok,
    PlacementReason Reason,
    Vector2 SnappedWorld,
    int Orientation,
    IReadOnlyList<GridCell> Cells,
    GridCell? Anchor,
    RadiusFootprint? Circle,
    IReadOnlyList<PlacementVerdict>? Members = null);

/// <summary>
/// 写路径的读数：成了没有 + 为什么没成 + 动的是哪座。
///
/// <b>拒绝也是读数不是异常</b>（D7）：它会被录进账、重放要比它。抛异常的只有作者错误
/// （未知动词、参数段不对、数值解析不出、占地形状与规则不匹配）。
/// </summary>
/// <param name="Ok">这一发打成了。</param>
/// <param name="Reason">失败原因；成了是 <see cref="PlacementReason.None"/>。</param>
/// <param name="BuildingId">动到的建筑 id；连蓝图都查不到时是空串。</param>
public readonly record struct WriteResult(bool Ok, PlacementReason Reason, string BuildingId = "")
{
    public static WriteResult Fail(PlacementReason reason, string buildingId = "")
        => new(false, reason, buildingId);

    public static WriteResult Done(string buildingId) => new(true, PlacementReason.None, buildingId);
}

/// <summary>
/// Core（建筑册与生命周期）↔ 规则（几何与占用索引私有）这条内部 seam（D38）。
///
/// <para>它是 public 只因为插件槽要原型在构造时把实现类型递进来；「对外不可见」那句
/// 落在<b>用法</b>上：原型不存一份这个接口的引用去调 Check / Occupy / Vacate，
/// 表现层更碰不到它 —— 走模块那几个成员就够了（spec 3 节第 3 条）。</para>
///
/// <para>占用索引是规则的私政：网格侧是格表、自由侧是间距表，各自在自己类里，
/// 模块只问「有几格被占」「谁占着这」。第三种规则（六角格，D39）要新写一份实现，
/// 这个接口不加成员。</para>
/// </summary>
public interface IPlacementRule
{
    /// <summary>报错时说清是哪一条规则（「网格放置」这类身份）。</summary>
    string RuleLabel { get; }

    /// <summary>这条规则吃哪种占地。蓝图与它不匹配 = 构造时当场抛（D33）。</summary>
    FootprintKind Kind { get; }

    /// <summary>边界的一行文字，进快照摘要行。自由放置不限边界，印 unbounded。</summary>
    string BoundsText { get; }

    /// <summary>画 gizmo 用的只读参数（格长 / 边界 / 量化步长）。表现层只准从这儿取（D29）。</summary>
    IReadOnlyDictionary<string, double> GizmoParams { get; }

    /// <summary>当前被占的格数，进快照摘要行。真值在规则的私产索引里，模块只问数。</summary>
    int OccupiedCellCount { get; }

    /// <summary>
    /// 纯查询：这次放不放得下、落在哪、占哪几格、什么方向。悬停、落建、直达口三路共用它（D4）。
    /// <b>这条不发任何事件</b>：查询不是状态变更，事件流里出现查询结果就是两处真值。
    /// 编辑动词（Move / Rotate）也走它，只是请求里带上「排除谁」，所以它们与新放置共用同一条流水线（D20、D21）。
    /// </summary>
    PlacementVerdict Check(PlacementRequest request);

    /// <summary>登记占用。只在同一次 Commit 判定成了之后调。</summary>
    void Occupy(string buildingId, PlacementVerdict placed);

    /// <summary>
    /// 登记一座<b>预先摆好</b>的建筑（场地建筑：关卡初始摆位、剧情建筑、不可由玩家放置的那些），
    /// 并把那份<b>占用几何造出来</b>。不判越界、不判间距、不问宿主 —— 它不是玩家手势，
    /// 没有「换个点」这个选项；但它<b>照样占位</b>，后来的玩家建筑会被 <see cref="Check"/>
    /// 正常判成 Occupied / TooClose。
    ///
    /// <para><b>为什么独立一个成员，而不是给 <see cref="PlacementRequest"/> 加一个「跳过校验」的开关</b>：
    /// 那个开关会把「这一发不判」放进裁决流内部，而裁决流是悬停幽灵、落建点击、直达口三路共用的
    /// 唯一真值（D4、D29）。开关一旦存在，幽灵读到的那一档与点击落下的那一档就有两条走法 ——
    /// 那正是「幽灵绿、点下去被拒」那个 bug 的形状。分成两个成员之后，<see cref="Check"/>
    /// 永远只做判定，场地建筑走的是另一条不判的登记路，两条路在模块里没有交叉点。</para>
    ///
    /// <para><b>占用几何必须由规则产</b>（D38）：位置量化、格吸附、D4 变换都是规则解释的东西，
    /// 核心自己算一遍就是「判的点与落的点差半个量化步长」，禁区整体偏一格。
    /// 返回的那份裁决就是入册要存的几列（落点、锚点或圆、方向），核心不重算任何一列（D9、D29）。</para>
    /// </summary>
    PlacementVerdict OccupyFixture(string buildingId, Blueprint blueprint, Vector2 position, int orientation);

    /// <summary>腾占用。移除与损毁致死都必须走它（GLOSSARY「占用」）。</summary>
    void Vacate(string buildingId);

    /// <summary>
    /// 这一处占着的是谁（拾取，ticket 03 的动词取它）。<b>给的是坐标不是格</b> ——
    /// 坐标换格是规则的内政：网格侧自己 floor，自由侧自己比半径。核心把坐标转成格就等于
    /// 把格长搬到了核心，那正是 D38 要防的「规则解释的东西跑进核心」。
    /// 没占着回 null。
    /// </summary>
    string? BuildingIdAt(Vector2 point);

    // ---- 批量手势要问规则的三件事（ticket 04 加，D37 的兑现）--------------------
    //
    // 批量手势（画线 / 画矩形 / 布局）枚举出来的每一个成员都是<b>一个整数格</b>，而落点判定
    // 要的是候选点。这中间的三步换算是规则的内政，核心不碰（D38）：核心手里只有一串整数格，
    // 换算就问规则。加这三个成员是纯函数、零副作用，第三种规则（未来六角格，D39）自己实现一份，
    // 这个接口不加成员。理由逐条写在那三个的注释里。

    /// <summary>
    /// 世界坐标落在那一个格（<see cref="CandidateAt"/> 的反函数）。
    ///
    /// <para>网格侧 = <c>floor(坐标 / 格长)</c>，与 Check 那一次吸附走的是同一份算术（D9）——
    /// 画矩形从 (0.4, 0.4) 拖到 (2.9, 2.9)，枚举的必须是 0..2 那九个格，不是别的。
    /// 两处各算一次的话，预览画的红格与落建占的格迟早会差一格（D29）。</para>
    ///
    /// <para>自由侧 = 那套画线格点的下标（<c>floor(坐标 / 画线步长)</c>）。自由放置的世界
    /// 本来没有格，是这一条规则自己给它定了一套画线用的格点，让画线 / 画矩形 / 布局三种手势
    /// 共用同一个整数域 —— 枚举顺序单一真值要落在整数上（D24）。</para>
    /// </summary>
    GridCell CellOf(Vector2 world);

    /// <summary>
    /// 那一格上的候选点（<see cref="CellOf"/> 的反函数，往返必须恒等）。
    ///
    /// 网格侧 = 格心推导（D9：世界落点只有这一条推导）；自由侧 = 格点下标乘画线步长。
    /// 核心拿它把「一串整数格」问成「一串候选点」再送进 Check —— 与落建那发走同一条流水线，
    /// 所以预览与结果逐格同源（D25、D29）。
    /// </summary>
    Vector2 CandidateAt(GridCell cell);

    /// <summary>
    /// 把一份布局按方向整数变换一次（<b>数据进数据出、无副作用</b>，D23、ADR 0007）。
    ///
    /// <para>成员偏移与成员自身方向一起转：只转偏移会让每座建筑都还朝着原来的方向，
    /// 那不叫转了一整块。偏移转完仍是整数格 —— 布局的成员位置就是那套整数格上的偏移，
    /// 转出小数就等于布局的枚举域与画线 / 画矩形那一套分家了。</para>
    ///
    /// <para>方向整数是<b>绝对值</b>（D37），核心不替规则做加法。</para>
    /// </summary>
    Layout TransformLayout(Layout layout, int orientation);
}
