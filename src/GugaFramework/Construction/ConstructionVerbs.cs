#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.Construction;

// 建造模块的标准动词表 —— 与 AbilityVerbs 同一个口径：「一份表、两处问」（D34）。
//
// 在此之前参数编码的做法是各原型自己抄一份（某侧那份以表现层镜像代身的编码
// 就是这个病的现成例子：靠注释维持与逻辑层同源，会漂）。现在的分工：
//   - 本文件：动词名、参数次序、缺省值、怎么切竖线、什么时候该抛 —— 只有一份；
//   - 各原型的适配器：只回答「这一侧哪个口子接得住」（SupportedVerbs），一个动词名都不写。
//
// inject（写）类与 call（只读）类分开，是因为 Journal 只录写路径（「写路径全录」、「写路径进 Journal」）：
// call 类不进录、不带 ok 字段。悬停候选口也是 call 类那一类，但它连 Journal 都不沾
// （D28：悬停态不进 Journal、不进检查点快照）。<b>写路径不等于进录</b>：场地建筑登记
// （RegisterFixture）是第三档 —— 它写世界，可是世界初始化期那一发、两端各跑一次 SeedWorld，
// 录了反而会在重放端重复登记（理由钉在 <see cref="ConstructionVerbs.InjectNames"/> 上）。
//
// 三条口径（照 AbilityVerbs，D7）：
//   1. 未知动词当场抛，不在重放里静默少打一笔；
//   2. 这一侧不接的动词也当场抛（SupportedVerbs 名单外），不拿 false 表达「没这一格」——
//      false 是给「真打不成」留的读数（会被录进账、重放要比它）；
//   3. 必填参数缺了当场抛，不兜 0（兜 0 = 静默摆在第 0 排，「桥只转发」）。
// 状态性拒绝（格被占 / 距离不够 / 出界 / 宿主说不可建 / 蓝图不存在 / 状态不允许 / 满血修复）
// 一律回读数，不抛 —— 每一个 ok=false 都是原型「这一发没花钱」的信号（D6）。

/// <summary>动词表与宿主的接缝。原型只递自己那一份实现。</summary>
public interface IConstructionVerbSink
{
    /// <summary>报错时说清是哪一侧的宿主（原型名这类身份，不是建造语义）。</summary>
    string HostLabel { get; }

    /// <summary>这一侧接得住哪几条动词。名单外的打过来 = 当场抛（口径 2）。</summary>
    IReadOnlySet<string> SupportedVerbs { get; }

    // ---- inject 类：写路径，录进 Journal ----------------------------------------

    /// <summary>
    /// 放一座。返回读数（成了没有 + 为什么没成），不是异常。
    /// <paramref name="buildDurationTicks"/> 是这一发的工期覆盖，null = 照蓝图自带那份（D15）；
    /// <paramref name="payloadText"/> 是载荷原文，编成什么对象是原型的内政（D31：模块不读它）。
    /// 载荷原文里不许出现竖线 —— 切分就是竖线、全表一份，没有转义档。
    /// </summary>
    WriteResult PlaceBuilding(string blueprintId, double x, double y, int orientation, string? buildingId,
        long? buildDurationTicks, string? payloadText);

    /// <summary>移走一座。id 不在册 = 读数 false + UnknownId。</summary>
    WriteResult RemoveBuilding(string buildingId, RemovalCause cause);

    /// <summary>
    /// 挪一座（D20）。原子：同一条 Check 流水线排除自身，失败一寸不动、<b>连事件都不发</b>。
    /// <paramref name="orientation"/> 是可省档：<b>null = 保持当前方向</b> —— 给 0 是「转成 rot0」，
    /// 在网格侧那是另一个数，两件事在 Journal 里不能是同一句话。
    /// </summary>
    WriteResult MoveBuilding(string buildingId, double x, double y, int? orientation);

    /// <summary>
    /// 原地转方向（D21）。撞邻居回读数（Occupied / OutOfBounds / TooClose）且一寸不动；
    /// 方向是绝对值，越界由放置规则当场抛（D7）。
    /// </summary>
    WriteResult RotateBuilding(string buildingId, int orientation);

    /// <summary>扣血。归零自动走 Removed(去向=摧毁) + 腾占用（D2）。</summary>
    WriteResult DamageBuilding(string buildingId, double amount);

    /// <summary>补血，钳到血量上限（D19）。满血回 InvalidState —— 原型据此不扣款（D6）。</summary>
    WriteResult RepairBuilding(string buildingId, double amount);

    /// <summary>
    /// 把血量设成绝对值，并把血量上限抬到 <c>max(现上限, 目标值)</c>（见
    /// <see cref="ConstructionCore.SetHealth"/>）。非正数当场抛（D7、「桥只转发」）；
    /// 未知 id 回读数 false + UnknownId。<b>满血照成</b>：它不是经济动作，不继承 Repair
    /// 那一档 InvalidState 的口径（那是给原型的「不扣款」信号）。
    /// </summary>
    WriteResult SetHealthBuilding(string buildingId, double hp);

    /// <summary>暂停施工。已完工 / 已在暂停中 = InvalidState（读数，不抛）。</summary>
    WriteResult PauseBuild(string buildingId);

    /// <summary>恢复施工。已完工 / 没在暂停中 = InvalidState（读数，不抛）。</summary>
    WriteResult ResumeBuild(string buildingId);

    /// <summary>
    /// 登记一座场地建筑（关卡摆位那一格）。跳过落位校验、占位照做，
    /// <b>不录进 Journal</b>（世界初始化期那一发，两端都跑同一个 <c>SeedWorld()</c>）。
    /// 建筑 id 必填 —— 没有自动发号那一档可兜（D7、「桥只转发」）。
    /// </summary>
    WriteResult RegisterFixture(string blueprintId, double x, double y, int orientation, string buildingId,
        string? payloadText);

    // ---- inject 类：批量手势（ticket 04）-----------------------------------------
    //
    // 批量与单件分开的理由不是「参数多」，是<b>读数的形状不同</b>：单件回一座的成败，
    // 批量回成功计数 + 逐格裁决表（D24）。所以这一族回 BatchResult / RectRemovalResult，
    // 由 VerbOutcome.FromBatch / FromRemoval 渲染成 ok 与那几行字 —— 渲染还是 GugaFramework 一份。

    /// <summary>
    /// 画一线的读数（<b>部分成功</b>，D24）：能放几座放几座。
    /// <b>一个手势一条 Journal 记录</b>（D24）：这七个参数就是那一整条，逐格录会让重放端
    /// 自己数一遍数量，而那正是要防的分叉。
    /// </summary>
    BatchResult PlaceLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation);

    /// <summary>画一个矩形的读数。部分成功，与 <see cref="PlaceLine"/> 同款（D24）。</summary>
    BatchResult PlaceRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation);

    /// <summary>
    /// 放一整份区域蓝图的读数（<b>全有或全无</b>，D23）。
    /// <paramref name="layoutText"/> 原样进 Journal 当一整格参数，所以它不许含竖线
    /// （<see cref="LayoutText.RequirePlainField"/> 判在构造那一侧）；0 成员布局当场抛（D7）。
    /// </summary>
    BatchResult PlaceLayout(string layoutText, double x, double y, int orientation);

    /// <summary>
    /// 拆掉一个矩形区域里的全部建筑（部分成功，D24）：逐成员发 Removed(去向=拆除)、
    /// <b>不加汇总事件</b>。<b>一格没人不算失败</b>，所以空区域回的是「打成了但什么都没拆」。
    /// </summary>
    RectRemovalResult RemoveRect(double x0, double y0, double x1, double y1);

    // ---- call 类：批量与布局的只读查询（不入录，「写路径进 Journal」）------------------------

    /// <summary>画线的逐格裁决表（预览 = 结果，D25）。与 <see cref="PlaceLine"/> 同一条流水线。</summary>
    string CheckLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation);

    /// <summary>画矩形的逐格裁决表（D25）。</summary>
    string CheckRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation);

    /// <summary>布局的逐成员裁决表（D25）。<b>成员之间互相压着也算失败</b>（全有或全无的前提）。</summary>
    string CheckLayout(string layoutText, double x, double y, int orientation);

    /// <summary>
    /// 捕获一个矩形区域里的建筑，回布局文本（<b>只读</b>、不入录、不改世界，ADR 0007）。
    /// 空区域回 0 成员文本，那不算错。
    /// </summary>
    string CaptureLayout(double x0, double y0, double x1, double y1);

    // ---- call 类：只读查询，不入录（「写路径进 Journal」）-----------------------------------

    /// <summary>这一处占着的是谁；没占着回空串。</summary>
    string BuildingAt(double x, double y);

    /// <summary>放不放得下（裁决那一行的读数）。</summary>
    string CheckPlacement(string blueprintId, double x, double y, int orientation);

    /// <summary>画 gizmo 用的只读参数（格长 / 边界 / 量化步长）。</summary>
    string GizmoParams();
}

/// <summary>一次命令解析出来的样子。<b>参数次序与缺省值只有这一处定义。</b></summary>
/// <param name="Cmd">动词名。</param>
/// <param name="BlueprintId">蓝图 id。</param>
/// <param name="X">世界坐标 x。</param>
/// <param name="Y">世界坐标 y。</param>
/// <param name="Orientation">方向整数（网格 = D4 八值，自由 = 整数度），由放置规则解释（D37）。</param>
/// <param name="BuildingId">建筑 id；<c>-</c> 与空 = 按登记序自动发（放置）/ 必有（其余动词）。</param>
/// <param name="Cause">移除去向；<c>-</c> 与空 = 拆除。</param>
/// <param name="BuildDurationTicks">这一发的工期覆盖（D15）；<c>-</c> 与空 = 照蓝图自带那份。</param>
/// <param name="PayloadText">不透明载荷原文（D31）；<c>-</c> 与空 = 不挂载荷。</param>
/// <param name="Amount">扣血 / 补血的数值；别的动词这一格是 null。</param>
/// <param name="Health">SetHealthBuilding 的<b>绝对血量</b>（不是增量）。与 <paramref name="Amount"/>
/// 分成两格不是重复，是「同一种数在两条动词里意思相反」：给 Repair 的数是加多少，
/// 给这一条的是落成多少，而落成的那个数要抬血量上限（见 <see cref="ConstructionCore.SetHealth"/>）。
/// 合成一格的话，动词表里就得靠动词名去猜那一段该加还是该设 —— 而 Journal 回放时
/// 读的人只有 args 那一串，猜错就是把「修」当成「设」。</param>
/// <param name="Direction">MoveBuilding 的方向可省档：<b>null = 保持当前方向</b>。
/// 与 <paramref name="Orientation"/> 分成两格不是重复，是「这一格有没有缺省值」：
/// PlaceBuilding / CheckPlacement 的方向缺省 0，MoveBuilding 的方向<b>不能</b>缺省 0（D20 的「方向?」
/// 要的是保持，不是 rot0）—— 0 在网格侧是一个真值，缺省撞上真值，重放端就再也分不出这一发
/// 到底转没转。</param>
/// <param name="X2">第二个点的 x。画线是终点、画矩形是 max 那一角（D24：那一条两个角是等价的，
/// 核心自己按 min/max 归一）；PlaceLayout / CheckLayout / RemoveRect / CaptureLayout 这一族
/// 的四角都走 X / Y / X2 / Y2 四格，<b>一条坐标形状</b>，不为矩形另开一套格。</param>
/// <param name="Y2">第二个点的 y。</param>
/// <param name="LayoutText">布局文本原样一整格（PlaceLayout / CheckLayout）。竖线是参数分隔符、
/// 全表一份没有转义档，所以布局文本自己不许带竖线（<see cref="LayoutText"/>）。</param>
public sealed record ConstructionVerbCall(
    string Cmd,
    string BlueprintId = "",
    double X = 0,
    double Y = 0,
    int Orientation = 0,
    string? BuildingId = null,
    RemovalCause Cause = RemovalCause.Dismantle,
    long? BuildDurationTicks = null,
    string? PayloadText = null,
    double? Amount = null,
    double? Health = null,
    int? Direction = null,
    double X2 = 0,
    double Y2 = 0,
    string? LayoutText = null);

/// <summary>一条命令打出去的读数。<b>inject 与 call 共用一个出口</b>：调用方要 ok 判 Journal，
/// 要 text 打给人看，缺哪个都不至于把另一个塞进异常里。</summary>
/// <param name="Ok">这一发打成了（call 类恒为 true —— 它读不到「打不成」那档）。</param>
/// <param name="Text">读数正文（区域无关，「double 与不变文化」）。</param>
public readonly record struct VerbOutcome(bool Ok, string Text)
{
    public static VerbOutcome From(WriteResult result)
        => new(result.Ok,
            $"ok={(result.Ok ? 1 : 0)} reason={result.Reason}" +
            (result.BuildingId.Length == 0 ? string.Empty : $" id={result.BuildingId}"));

    /// <summary>
    /// 批量放置的读数（线 / 矩形 / 布局同款）。<b>ok 的读法是「这一发打成了没有」</b> ——
    /// 成功数为 0 就是没打成；3/7 也是打成了（逐格成败在 table= 那一行里，原型按成员算钱，D6）。
    /// </summary>
    public static VerbOutcome FromBatch(BatchResult result)
        => new(result.Ok, BatchText.Line(result));

    /// <summary>布局那一发的读数：多一行布局名与失败成员表（D26 的失败成员 + 原因）。</summary>
    public static VerbOutcome FromLayout(string layoutName, BatchResult result)
        => new(result.Ok, BatchText.LayoutLine(layoutName, result));

    /// <summary>批量拆除的读数。一格没人不算失败，所以「什么都没拆」也是 ok=true。</summary>
    public static VerbOutcome FromRemoval(RectRemovalResult result)
        => new(result.Ok, BatchText.Line(result));
}

/// <summary>
/// 词表本体：21 个动词名 + <see cref="Parse"/> + <see cref="Dispatch"/>。
/// 新增一条动词只改这一个文件与宿主那一份适配器的一个方法。
/// </summary>
public static class ConstructionVerbs
{
    public const string Place = "PlaceBuilding";
    public const string Remove = "RemoveBuilding";
    public const string Move = "MoveBuilding";
    public const string Rotate = "RotateBuilding";
    public const string Damage = "DamageBuilding";
    public const string Repair = "RepairBuilding";
    public const string SetHealthVerb = "SetHealthBuilding";
    public const string Pause = "PauseBuild";
    public const string Resume = "ResumeBuild";
    public const string RegisterFixtureVerb = "RegisterFixture";
    public const string BuildingAtVerb = "BuildingAt";
    public const string Check = "CheckPlacement";
    public const string Gizmo = "GizmoParams";
    public const string PlaceLineVerb = "PlaceLine";
    public const string PlaceRectVerb = "PlaceRect";
    public const string PlaceLayoutVerb = "PlaceLayout";
    public const string RemoveRectVerb = "RemoveRect";
    public const string CheckLineVerb = "CheckLine";
    public const string CheckRectVerb = "CheckRect";
    public const string CheckLayoutVerb = "CheckLayout";
    public const string CaptureLayoutVerb = "CaptureLayout";

    /// <summary>动词名全表（录制包装、桥、CLI 与检查清单都从这里取，不各自抄一份）。</summary>
    public static readonly string[] Names =
    [
        Place, Remove, Move, Rotate, Damage, Repair, SetHealthVerb, Pause, Resume, RegisterFixtureVerb,
        BuildingAtVerb, Check, Gizmo,
        PlaceLineVerb, PlaceRectVerb, PlaceLayoutVerb, RemoveRectVerb,
        CheckLineVerb, CheckRectVerb, CheckLayoutVerb, CaptureLayoutVerb,
    ];

    /// <summary>
    /// 会进 Journal 的那几条（写路径全录，「写路径全录」）。
    /// 批量与布局这一族也在里面，而且<b>一个手势一条</b>（D24）：录的是
    /// <c>PlaceLine|蓝图|x0|y0|x1|y1|方向</c> 这么一整条，不逐格录。
    ///
    /// <para><b><see cref="RegisterFixtureVerb"/> 刻意不在这一份里</b>，而它仍然是写路径：它调的是
    /// 世界初始化期（<c>SeedWorld()</c>），录制端与重放端<b>两边都跑同一个 SeedWorld</b>，
    /// 所以不录也逐字复现；录一遍反而会在重放端把同一座登记第二次，当场撞重名那道抛。
    /// 「写路径全录」 立的是「重放不能漏掉一次写」，这一发两端都发生，漏不掉。
    /// 「写路径全录」的那句话因此在这一条上有唯一的例外，例外连理由都钉在这份名单旁边，
    /// 不留给读代码的人自己推。</para>
    ///
    /// <para><b><see cref="SetHealthVerb"/> 在这一份里</b>，尽管它和 RegisterFixture 一样常在
    /// 世界初始化期之外的排布里被调到：区别是它<b>不在两端各跑一次的 SeedWorld 里</b>。
    /// 终局脚本里那一发 <c>HealBuilding palace|200000</c> 就是它 —— 那一发是剧本里
    /// 的一步，只在录制端发生一次，重放端不跑它，漏录就是漏掉一次写（「写路径全录」）。
    /// 那条命令在 Journal 里是原型自己词表的 <c>HealBuilding</c>（D34：一份表、两处问，
    /// 模块那一份给动词名与参数次序，原型那一份给入录形态），两层各答自己那半，
    /// 于是模块这一条照样进录名单。</para>
    /// </summary>
    public static readonly string[] InjectNames =
    [
        Place, Remove, Move, Rotate, Damage, Repair, SetHealthVerb, Pause, Resume,
        PlaceLineVerb, PlaceRectVerb, PlaceLayoutVerb, RemoveRectVerb,
    ];

    /// <summary>
    /// 把一条命令按位置切成结构。<b>这条不发东西</b>，纯函数 —— 所以「第几段到底该有几个」
    /// 能单独判，不用起任何一个世界。参数一律竖线切，位置固定；缺的按位置留空，
    /// 缺省值都写在这一份表里。必填缺了当场抛，不兜 0（「桥只转发」）。
    /// </summary>
    public static ConstructionVerbCall Parse(string cmd, string args)
    {
        var f = (args ?? string.Empty).Split('|');
        string At(int index) => index < f.Length ? f[index] : string.Empty;
        string? Name(int index) => DashToNull(At(index));

        switch (cmd)
        {
            case Place:
                // 蓝图Id|x|y|方向(缺省 0)|建筑id(可省)|工期(可省)|载荷(可省)
                return new ConstructionVerbCall(cmd,
                    BlueprintId: Required(At(0), "蓝图Id", cmd),
                    X: Number(At(1), "x", cmd),
                    Y: Number(At(2), "y", cmd),
                    Orientation: OptionalInteger(At(3), "方向", cmd),
                    BuildingId: Name(4),
                    BuildDurationTicks: OptionalTicks(At(5), "工期", cmd),
                    PayloadText: DashToNull(At(6)));
            case Remove:
                // 建筑id|去向(缺省 拆除)
                return new ConstructionVerbCall(cmd,
                    BuildingId: Required(At(0), "建筑Id", cmd),
                    Cause: Cause(At(1), cmd));
            case Move:
                // 建筑id|x|y|方向(可省，省 = 保持当前方向)
                return new ConstructionVerbCall(cmd,
                    BuildingId: Required(At(0), "建筑Id", cmd),
                    X: Number(At(1), "x", cmd),
                    Y: Number(At(2), "y", cmd),
                    Direction: OptionalDirection(At(3), "方向", cmd));
            case Rotate:
                // 建筑id|方向（方向是这一条动词的全部，缺了就是作者错误，不兜当前方向）
                return new ConstructionVerbCall(cmd,
                    BuildingId: Required(At(0), "建筑Id", cmd),
                    Orientation: RequiredInteger(At(1), "方向", cmd));
            case Damage:
            case Repair:
                // 建筑id|数值
                return new ConstructionVerbCall(cmd,
                    BuildingId: Required(At(0), "建筑Id", cmd),
                    Amount: Number(At(1), "数值", cmd));
            case SetHealthVerb:
                // 建筑id|绝对血量（不是增量；上限一并抬到 max(现上限, 目标值)）
                return new ConstructionVerbCall(cmd,
                    BuildingId: Required(At(0), "建筑Id", cmd),
                    Health: Number(At(1), "绝对血量", cmd));
            case Pause:
            case Resume:
                // 建筑id
                return new ConstructionVerbCall(cmd,
                    BuildingId: Required(At(0), "建筑Id", cmd));
            case RegisterFixtureVerb:
                // 蓝图Id|x|y|方向(缺省 0)|建筑id(必填)|载荷(可省)
                // 建筑 id 必填且不兜自动发号（场地建筑的 id 是关卡摆位的一部分，「桥只转发」）。
                return new ConstructionVerbCall(cmd,
                    BlueprintId: Required(At(0), "蓝图Id", cmd),
                    X: Number(At(1), "x", cmd),
                    Y: Number(At(2), "y", cmd),
                    Orientation: OptionalInteger(At(3), "方向", cmd),
                    BuildingId: Required(At(4), "建筑id", cmd),
                    PayloadText: DashToNull(At(5)));
            case BuildingAtVerb:
                // x|y
                return new ConstructionVerbCall(cmd,
                    X: Number(At(0), "x", cmd),
                    Y: Number(At(1), "y", cmd));
            case Check:
                // 蓝图Id|x|y|方向(缺省 0)
                return new ConstructionVerbCall(cmd,
                    BlueprintId: Required(At(0), "蓝图Id", cmd),
                    X: Number(At(1), "x", cmd),
                    Y: Number(At(2), "y", cmd),
                    Orientation: OptionalInteger(At(3), "方向", cmd));
            case Gizmo:
                // 无参
                return new ConstructionVerbCall(cmd);
            case PlaceLineVerb:
            case CheckLineVerb:
                // 蓝图Id|起点x|起点y|终点x|终点y|方向(缺省 0)
                return new ConstructionVerbCall(cmd,
                    BlueprintId: Required(At(0), "蓝图Id", cmd),
                    X: Number(At(1), "起点x", cmd),
                    Y: Number(At(2), "起点y", cmd),
                    X2: Number(At(3), "终点x", cmd),
                    Y2: Number(At(4), "终点y", cmd),
                    Orientation: OptionalInteger(At(5), "方向", cmd));
            case PlaceRectVerb:
            case CheckRectVerb:
                // 蓝图Id|minx|miny|maxx|maxy|方向(缺省 0)（两角谁大谁小由模块归一，不当作者错误）
                return new ConstructionVerbCall(cmd,
                    BlueprintId: Required(At(0), "蓝图Id", cmd),
                    X: Number(At(1), "minx", cmd),
                    Y: Number(At(2), "miny", cmd),
                    X2: Number(At(3), "maxx", cmd),
                    Y2: Number(At(4), "maxy", cmd),
                    Orientation: OptionalInteger(At(5), "方向", cmd));
            case PlaceLayoutVerb:
            case CheckLayoutVerb:
                // 布局文本|锚点x|锚点y|整体方向(缺省 0)
                // 布局文本占一整格：竖线是分隔符，布局构造那一侧就禁掉了它（LayoutText）。
                return new ConstructionVerbCall(cmd,
                    LayoutText: Required(At(0), "布局文本", cmd),
                    X: Number(At(1), "锚点x", cmd),
                    Y: Number(At(2), "锚点y", cmd),
                    Orientation: OptionalInteger(At(3), "整体方向", cmd));
            case RemoveRectVerb:
            case CaptureLayoutVerb:
                // minx|miny|maxx|maxy
                return new ConstructionVerbCall(cmd,
                    X: Number(At(0), "minx", cmd),
                    Y: Number(At(1), "miny", cmd),
                    X2: Number(At(2), "maxx", cmd),
                    Y2: Number(At(3), "maxy", cmd));
            default:
                throw new InvalidOperationException(
                    $"未知命令 {cmd}（词表由开发者现场扩——录制包装、桥、CLI 三边同加，改 {nameof(ConstructionVerbs)} 那一份）");
        }
    }

    /// <summary>
    /// 解析 + 发到这一侧宿主。<b>返回值两种意思</b>：<c>Ok=true</c> = 这一发打成了；
    /// <c>Ok=false</c> = 世界那儿真没打成（格被占 / 距离不够 / 出界 / 蓝图不存在 /
    /// 状态不允许 / 满血修复），这个 false 会被录进账、重放要比它。「这一侧不接这条词」不走 false，走
    /// <see cref="NotSupportedException"/>（口径 2）。作者错误（参数段不对、数值读不出、
    /// 动词名不认识）当场抛（D7）。
    /// </summary>
    public static VerbOutcome Dispatch(IConstructionVerbSink sink, string cmd, string args)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var call = Parse(cmd, args);
        if (!sink.SupportedVerbs.Contains(cmd))
        {
            throw new NotSupportedException(
                $"{sink.HostLabel} 这一侧不接 {cmd}：不适用要在检查清单里写明理由，不许静默少一条");
        }

        return cmd switch
        {
            Place => VerbOutcome.From(sink.PlaceBuilding(call.BlueprintId, call.X, call.Y,
                call.Orientation, call.BuildingId, call.BuildDurationTicks, call.PayloadText)),
            Remove => VerbOutcome.From(sink.RemoveBuilding(call.BuildingId!, call.Cause)),
            Move => VerbOutcome.From(sink.MoveBuilding(call.BuildingId!, call.X, call.Y, call.Direction)),
            Rotate => VerbOutcome.From(sink.RotateBuilding(call.BuildingId!, call.Orientation)),
            Damage => VerbOutcome.From(sink.DamageBuilding(call.BuildingId!, call.Amount!.Value)),
            Repair => VerbOutcome.From(sink.RepairBuilding(call.BuildingId!, call.Amount!.Value)),
            SetHealthVerb => VerbOutcome.From(sink.SetHealthBuilding(call.BuildingId!, call.Health!.Value)),
            Pause => VerbOutcome.From(sink.PauseBuild(call.BuildingId!)),
            Resume => VerbOutcome.From(sink.ResumeBuild(call.BuildingId!)),
            RegisterFixtureVerb => VerbOutcome.From(sink.RegisterFixture(call.BlueprintId, call.X, call.Y,
                call.Orientation, call.BuildingId!, call.PayloadText)),
            BuildingAtVerb => new VerbOutcome(true, sink.BuildingAt(call.X, call.Y)),
            Check => new VerbOutcome(true, sink.CheckPlacement(call.BlueprintId, call.X, call.Y,
                call.Orientation)),
            Gizmo => new VerbOutcome(true, sink.GizmoParams()),
            PlaceLineVerb => VerbOutcome.FromBatch(sink.PlaceLine(call.BlueprintId, call.X, call.Y,
                call.X2, call.Y2, call.Orientation)),
            PlaceRectVerb => VerbOutcome.FromBatch(sink.PlaceRect(call.BlueprintId, call.X, call.Y,
                call.X2, call.Y2, call.Orientation)),
            PlaceLayoutVerb => VerbOutcome.FromLayout(call.LayoutText!,
                sink.PlaceLayout(call.LayoutText!, call.X, call.Y, call.Orientation)),
            RemoveRectVerb => VerbOutcome.FromRemoval(sink.RemoveRect(call.X, call.Y, call.X2, call.Y2)),
            CheckLineVerb => new VerbOutcome(true, sink.CheckLine(call.BlueprintId, call.X, call.Y,
                call.X2, call.Y2, call.Orientation)),
            CheckRectVerb => new VerbOutcome(true, sink.CheckRect(call.BlueprintId, call.X, call.Y,
                call.X2, call.Y2, call.Orientation)),
            CheckLayoutVerb => new VerbOutcome(true, sink.CheckLayout(call.LayoutText!, call.X, call.Y,
                call.Orientation)),
            CaptureLayoutVerb => new VerbOutcome(true, sink.CaptureLayout(call.X, call.Y, call.X2, call.Y2)),
            _ => throw new InvalidOperationException($"未知命令 {cmd}"),
        };
    }

    /// <summary>裁决那一行的读数。表现层与调试口都读这一份文字，不自己拼（避免两处拼法）。
    ///
    /// <b>批量形状多一段 members= 与 table=</b>（D25）：预览要能画红格，就不能只给
    /// 「能不能放」那一个数。逐成员表与提交路径报的失败格是同一份渲染（D29）。</summary>
    public static string VerdictLine(PlacementVerdict verdict)
    {
        var sb = new StringBuilder();
        sb.Append(verdict.Ok ? "ok" : "no")
            .Append(" reason=").Append(verdict.Reason)
            .Append(" pos=").Append(ConstructionNumbers.Point(verdict.SnappedWorld))
            .Append(" dir=").Append(ConstructionNumbers.Format(verdict.Orientation))
            .Append(" cells=").Append(ConstructionNumbers.Cells(verdict.Cells));
        if (verdict.Circle is { } circle)
        {
            sb.Append(" radius=").Append(ConstructionNumbers.Format(circle.Radius));
        }

        if (verdict.Members is { } members)
        {
            sb.Append(" members=").Append(ConstructionNumbers.Format(members.Count))
                .Append(" table=").Append(LayoutText.Table(members));
        }

        return sb.ToString();
    }

    private static string Required(string text, string what, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺{what}（必填参数不兜默认值）");
        }

        return text;
    }

    private static double Number(string text, string what, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺{what}（必填参数不兜默认值）");
        }

        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>可省的那几格：空串与 <c>-</c> 都算「这一格没给」，缺省值写在这份表里。</summary>
    private static int OptionalInteger(string text, string what, string cmd)
    {
        var given = DashToNull(text);
        if (given is null)
        {
            return 0;
        }

        if (!double.TryParse(given, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{cmd} 的{what}读不懂 | {given}（要的是整数）");
        }

        return (int)value;
    }

    /// <summary>
    /// MoveBuilding 的方向可省档：空串与 <c>-</c> = <b>保持当前方向</b>，回 null（不兜 0）。
    ///
    /// <b>为什么这一格不能像其余可省格那样兜 0</b>：0 在网格侧是一个合法方向（rot0），
    /// 兜 0 的话「保持当前」与「转回正」在 Journal 里成了同一句话，重放端再也分不出这一发
    /// 到底转没转 —— 少一个字段的代价是一条再也分不开的命令流，那笔账不划算。
    /// </summary>
    private static int? OptionalDirection(string text, string what, string cmd)
    {
        var given = DashToNull(text);
        if (given is null)
        {
            return null;
        }

        if (!int.TryParse(given, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{cmd} 的{what}读不懂 | {given}（要的是整数）");
        }

        return value;
    }

    /// <summary>必填整数：不给或读不出都当场抛。RotateBuilding 的方向走这条 —— 那一格是这条动词的全部，
    /// 兜一个「保持当前」的话，「转」这个动作在 Journal 里就可能整条不存在。</summary>
    private static int RequiredInteger(string text, string what, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺{what}（必填参数不兜默认值）");
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{cmd} 的{what}读不懂 | {text}（要的是整数）");
        }

        return value;
    }

    /// <summary>
    /// 工期那一格：空串与 <c>-</c> 都算「没给」，照蓝图自带那份走（D15 的逐次覆盖）。
    /// <b>负数不在这里判</b>：两道入口（录制包装与调试桥）都从模块那一个方法过，模块当场抛一次就够，
    /// 表里再判一道只会让同一条作者错误有两个报错点。读不出整数（"3.5"、"快一点"）才是这一层的活。
    /// </summary>
    private static long? OptionalTicks(string text, string what, string cmd)
    {
        var given = DashToNull(text);
        if (given is null)
        {
            return null;
        }

        return long.TryParse(given, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{cmd} 的{what}读不懂 | {given}（要的是整数步数）");
    }

    /// <summary>去向着色。写得出来的只有三个（封闭枚举，D17），别的当场抛。</summary>
    private static RemovalCause Cause(string text, string cmd)
    {
        var given = DashToNull(text);
        if (given is null)
        {
            return RemovalCause.Dismantle;
        }

        return given.ToLowerInvariant() switch
        {
            "dismantle" or "拆除" => RemovalCause.Dismantle,
            "cancel" or "取消" => RemovalCause.Cancel,
            "destroyed" or "摧毁" => RemovalCause.Destroyed,
            _ => throw new FormatException(
                $"{cmd} 的去向读不懂 | {given}（只有 dismantle 拆除 / cancel 取消 / destroyed 摧毁）"),
        };
    }

    private static string? DashToNull(string text) => text is "" or "-" ? null : text;
}
