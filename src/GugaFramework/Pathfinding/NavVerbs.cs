#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DemoFactory.GugaFramework.Replay;

namespace DemoFactory.GugaFramework.Pathfinding;

// 寻路模块的标准动词表 —— 与 ConstructionVerbs / AbilityVerbs 同一个口径：「一份表、两处问」。
//
// 在此之前三类写各走各的口子：改地形、下指令、落位移。漏录一次就让重放分叉一次（「写路径全录」），
// 而这三类各抄一份参数编码，等于每接一个原型就多一格能漂的地方。分工照建造层：
//   - 本文件：动词名、参数次序、段数、缺省、怎么切竖线、什么时候该抛 —— 只有一份；
//   - 宿主的适配器：只回答「这一侧哪个口子接得住」（SupportedVerbs），一个动词名都不写。
//
// inject（写）与 call（只读）分开，是因为 Journal 只录写路径（「写路径全录」、「写路径进 Journal」）：
// <b>算路径是读，进不了录；移动是写，必须进录</b>。重放端只重放指令与地形改动，
// 路径自己重算即得同一结果 —— 前提是网格 + 净空 + 两个版本逐字进快照（见 NavReplayWorld）。
//
// 三条口径（照 ConstructionVerbs，D7）：
//   1. 未知动词当场抛（<see cref="Parse"/> / <see cref="Dispatch"/>），不在重放里静默少打一笔；
//   2. 这一侧不接的动词也当场抛（SupportedVerbs 名单外），不拿 false 表达「没这一格」——
//      false 是给「真打不成」留的读数（会被录进账、重放要比它）；
//   3. 必填参数缺了当场抛，不兜 0（兜 0 = 静默摆到第 0 排，「桥只转发」）。
// 状态性拒绝（单位没登记过）回读数，不抛 —— 每一个 ok=false 都是宿主「这一发没花钱」的信号（D6）。
//
// <b>动词名与档名都不给别名集</b>：Journal 里同一件事只有一种 token 写法，
// 而别名集正是「同一逻辑事实在账本里有几种写法」那条病（ReplayAssembly 规范化那一段的来由）。
// 需要短名的那一头（桥 / CLI）自己去映射，不在表里另开一档。

/// <summary>改地形动哪一层 —— <see cref="NavVerbs.StampTerrainVerb"/> 那一段的第三个 token。</summary>
public enum NavTerrainLayer
{
    /// <summary>地形阻挡层。值只认 0（通）与 1（阻挡）—— 它是布尔的数形，不给别档。</summary>
    Blocked = 0,

    /// <summary>静态通过成本层（正有限数，1 是基准）。写它只动静态版本。</summary>
    PassCost = 1,

    /// <summary>动态代价层乘数（正有限数，1 = 不减速）。写它只动动态版本。</summary>
    DynamicCost = 2,
}

/// <summary>一次写路径的读数。<b>不是异常</b>：ok=false 是「这一发真没打成」，会被录进账、重放要比它。</summary>
/// <param name="Ok">这一发打成了没有。</param>
/// <param name="Reason">为什么没打成（打成了时是 <c>Applied</c> / <c>Issued</c>）。封闭的一小组，别的当场抛。</param>
/// <param name="Detail">读数正文：打成了之后世界变成什么样（版本号、落点），不进 Journal。</param>
public readonly record struct NavVerbResult(bool Ok, string Reason, string Detail = "")
{
    /// <summary>写成了。</summary>
    public static NavVerbResult Applied(string detail) => new(true, "Applied", detail);

    /// <summary>写没打成（状态性拒绝，可观察行为，重放要比它）。</summary>
    public static NavVerbResult Refused(string reason, string detail = "") => new(false, reason, detail);
}

/// <summary>一条命令打出去的读数。<b>inject 与 call 共用一个出口</b>：调用方要 ok 判 Journal，要 text 打给人看。</summary>
/// <param name="Ok">这一发打成了（call 类恒为 true —— 它读不到「打不成」那档）。</param>
/// <param name="Text">
/// 读数正文。inject 那一档<b>带 <c>ok=</c> 字段</b>，call 那一档<b>不带</b>（「写路径进 Journal」：
/// 读数没有「成没成」这件事，印一个 ok=1 只是让读的人多认一个词）。
/// </param>
public readonly record struct NavVerbOutcome(bool Ok, string Text)
{
    /// <summary>把一次写路径的读数渲染成一行。<c>ok</c> 与 <c>reason</c> 是读数的固定两段。</summary>
    public static NavVerbOutcome FromWrite(NavVerbResult result)
        => new(result.Ok, $"ok={(result.Ok ? 1 : 0)} reason={result.Reason}"
            + (result.Detail.Length == 0 ? string.Empty : $" {result.Detail}"));

    /// <summary>把一段只读正文原样交出去（<b>不加 ok 字段</b>）。</summary>
    public static NavVerbOutcome FromRead(string text) => new(true, text);
}

/// <summary>动词表与宿主的接缝。宿主只递自己那一份实现，一个动词名都不另抄。</summary>
public interface INavVerbSink
{
    /// <summary>报错时说清是哪一侧的宿主（身份，不是寻路语义）。</summary>
    string HostLabel { get; }

    /// <summary>这一侧接得住哪几条动词。名单外的打过来 = 当场抛（口径 2）。</summary>
    IReadOnlySet<string> SupportedVerbs { get; }

    // ---- inject 类：写路径，录进 Journal -----------------------------------------

    /// <summary>
    /// 改一格地形（<see cref="NavTerrainLayer"/> 三档之一）。<b>落成与没落成都在读数里</b>，
    /// 版本号是这一格最有用的一列：「为什么路径重算了」先读它。
    /// </summary>
    NavVerbResult StampTerrain(NavCell cell, NavTerrainLayer layer, double value);

    /// <summary>给一个单位发一条命令（命令空间五档，归一后的那个形状）。</summary>
    NavVerbResult SetOrder(string agentId, NavCommand command);

    /// <summary>
    /// 把一个单位落到一个绝对位置（带朝向）。<b>排布口也走它</b>（「写路径全录」：写路径全录，
    /// 连测试排布口都要进 Journal）。
    /// </summary>
    NavVerbResult ApplyMove(string agentId, NavPoint to, double facingDegrees);

    // ---- call 类：只读查询，不入录、不带 ok 字段（「写路径进 Journal」）-----------------------

    /// <summary>算一条路（<b>读</b>，不进录）：折线 + 裁决 + 扩展格数 + 总成本。</summary>
    string FindPath(NavPoint from, NavPoint to, double radiusMeters);

    /// <summary>一个单位最新一步的移动解（<see cref="NavMoveSolution.Describe"/> 那一行）。</summary>
    string ReadState(string agentId);

    /// <summary>整张网格的字符图（<b>读数</b>：量化给人看，<b>不是</b>逐字真值 —— 逐字真值是检查点快照那一段）。</summary>
    string AsciiMap();

    /// <summary>求解记录账本的读数（带头一行 kept / dropped，正文逐条）。</summary>
    string SolveRecord();
}

/// <summary>一次命令解析出来的样子。<b>参数次序、段数与缺省值只有这一处定义。</b></summary>
/// <param name="Cmd">动词名。</param>
/// <param name="CellX">StampTerrain 的格列号。</param>
/// <param name="CellZ">StampTerrain 的格行号。</param>
/// <param name="Layer">StampTerrain 动哪一层。</param>
/// <param name="Value">StampTerrain 的值：阻挡层 0/1，两档成本层是正有限数。</param>
/// <param name="AgentId">单位 id（SetOrder / ApplyMove / ReadState）。</param>
/// <param name="Kind">SetOrder 是命令空间里的哪一档。<c>Unset</c> 不是一条命令，不许写进 Journal。</param>
/// <param name="ToleranceMeters">SetOrder 的到达容差（米，非负）。原地不动那一档只认 0。</param>
/// <param name="Point">点目标：驻守点 / 落点 / 区域中心 / 追实体那一轮的目标坐标。</param>
/// <param name="Waypoints">沿固定点走那一档的航点串（点间分号、点内逗号）。其余四档是 null。</param>
/// <param name="EntityId">追实体那一档的目标 id（只作读数，层里没有单位表）。</param>
/// <param name="AreaRadiusMeters">守一片区域那一档的区域半径（只作读数交回）。</param>
/// <param name="FacingDegrees">ApplyMove 的朝向（度）：0 = +Z，+90 = +X，值域 [−180, 180)。</param>
/// <param name="From">FindPath 的起点（米）。</param>
/// <param name="To">FindPath 的目标点（米）。</param>
/// <param name="RadiusMeters">FindPath 的身体半径（米，非负）。</param>
public sealed record NavVerbCall(
    string Cmd,
    int CellX = 0,
    int CellZ = 0,
    NavTerrainLayer Layer = NavTerrainLayer.Blocked,
    double Value = 0.0,
    string? AgentId = null,
    NavCommandKind Kind = NavCommandKind.Unset,
    double ToleranceMeters = 0.0,
    NavPoint Point = default,
    IReadOnlyList<NavPoint>? Waypoints = null,
    string? EntityId = null,
    double AreaRadiusMeters = 0.0,
    double FacingDegrees = 0.0,
    NavPoint From = default,
    NavPoint To = default,
    double RadiusMeters = 0.0)
{
    /// <summary>StampTerrain 那一格（格坐标是网格的身份，与米分开）。</summary>
    public NavCell Cell => new(CellX, CellZ);

    /// <summary>SetOrder 那一档的落点（航点串那一档取首点，判到位要等游标）。</summary>
    public NavPoint GoalPoint => Waypoints is { Count: > 0 } ? Waypoints[0] : Point;

    /// <summary>归一后的那条命令 —— <b>命令空间那五个工厂只有这一处被调用</b>。
    /// 词表负责把 token 变成命令，命令空间负责语义，两边不各编一份。</summary>
    /// <exception cref="FormatException">命令档是 <see cref="NavCommandKind.Unset"/>（不许写进 Journal）。</exception>
    public NavCommand ToCommand()
    {
        if (Kind == NavCommandKind.Unset)
        {
            throw new FormatException(
                $"{Cmd} 的命令档不能是「未下令」—— 那一档是「注册之后、发令之前」的状态，不是可以重放的一条命令。");
        }

        return Kind switch
        {
            NavCommandKind.Hold => NavCommand.Hold(Point),
            NavCommandKind.GoTo => NavCommand.GoTo(Point, ToleranceMeters),
            NavCommandKind.Chase => NavCommand.Chase(EntityId!, Point, ToleranceMeters),
            NavCommandKind.AlongWaypoints => NavCommand.Along(Waypoints!, ToleranceMeters),
            NavCommandKind.GuardArea => NavCommand.Guard(Point, AreaRadiusMeters, ToleranceMeters),
            _ => throw new FormatException($"{Cmd} 的命令档读不懂 | {Kind}"),
        };
    }
}

/// <summary>
/// 寻路层的<b>唯一一条地形写路径</b> —— 动词表的 sink 与建造接线（<see cref="NavTerrainLink"/>）都走它。
///
/// <para>「桥只转发」 那一半在这里落地：「注入的路径」与「玩家走的路径」不许漂成两回事。
/// 建造建成时落的阻挡与注入 <c>StampTerrain</c> 落的阻挡是同一次 <see cref="NavGrid.SetBlocked"/> ——
/// 两处各写一遍的话，静态版本的 +1 会在某一条路上少发生一次，而那正是重放分叉的形状。</para>
///
/// <para><b>校验只有这一处</b>（照 ConstructionVerbs 的做法：表里再判一道只会让同一条作者错误
/// 有两个报错点）：阻挡层只认 0 与 1，两档成本层交给 <see cref="NavGrid"/> 自己抛正有限数那一道。</para>
/// </summary>
public static class NavTerrain
{
    /// <summary>落一枚地形印子。返回值里带两个版本号 —— 「为什么路径重算了」先读它。</summary>
    /// <exception cref="ArgumentOutOfRangeException">阻挡层给了 0/1 以外的数。</exception>
    public static NavVerbResult Apply(NavGrid grid, NavCell cell, NavTerrainLayer layer, double value)
    {
        ArgumentNullException.ThrowIfNull(grid);
        switch (layer)
        {
            case NavTerrainLayer.Blocked:
                if (value != 0.0 && value != 1.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value), value,
                        $"地形阻挡层只认 0（通）与 1（阻挡），收到 {value.ToString(CultureInfo.InvariantCulture)} —— "
                        + "它是布尔的数形，给第三档就得到一个「既不是墙也不是空地」的地形（「桥只转发」）。");
                }

                grid.SetBlocked(cell, value == 1.0);
                break;
            case NavTerrainLayer.PassCost:
                grid.SetPassCost(cell, value);
                break;
            case NavTerrainLayer.DynamicCost:
                grid.SetDynamicCost(cell, value);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(layer), layer,
                    $"地形层读不懂 | {layer}（只有 blocked 地形阻挡 / pass 静态通过成本 / dynamic 动态代价层）。");
        }

        return NavVerbResult.Applied(
            $"cell={cell} static={Int(grid.StaticVersion)} dynamic={Int(grid.DynamicVersion)}");
    }

    private static string Int(int value) => ReplayArgs.Int(value);
}

/// <summary>
/// 寻路层的动词表本体：7 个动词名 + <see cref="FieldCount"/> + <see cref="Parse"/> +
/// <see cref="Canonicalize"/> + <see cref="Dispatch"/> + <see cref="InjectorFor"/>。
/// 新增一条动词只改这一个文件与宿主那一份适配器的一个方法。
/// </summary>
public static class NavVerbs
{
    /// <summary>改地形（inject）：<c>x|z|层|值</c>，4 段，全必填。</summary>
    public const string StampTerrainVerb = "StampTerrain";

    /// <summary>下指令（inject）：<c>id|命令档|到达容差|…</c>，4/5/6 段，段数按命令档定（见 <see cref="FieldCount"/>）。</summary>
    public const string SetOrderVerb = "SetOrder";

    /// <summary>落位移（inject）：<c>id|x|z|朝向</c>，4 段，全必填。</summary>
    public const string ApplyMoveVerb = "ApplyMove";

    /// <summary>算一条路（call）：<c>起点x|起点z|目标x|目标z|半径</c>，5 段。</summary>
    public const string FindPathVerb = "FindPath";

    /// <summary>读一个单位的移动解（call）：<c>单位id</c>，1 段。</summary>
    public const string ReadStateVerb = "ReadState";

    /// <summary>整张网格的字符图（call）：无参。</summary>
    public const string AsciiMapVerb = "AsciiMap";

    /// <summary>求解记录账本的读数（call）：无参。</summary>
    public const string SolveRecordVerb = "SolveRecord";

    /// <summary>动词名全表（录制包装、桥、CLI 与检查清单都从这里取，不各自抄一份）。</summary>
    public static readonly string[] Names =
    [
        StampTerrainVerb, SetOrderVerb, ApplyMoveVerb,
        FindPathVerb, ReadStateVerb, AsciiMapVerb, SolveRecordVerb,
    ];

    /// <summary>
    /// 会进 Journal 的那三条（写路径全录，「写路径全录」）。
    /// <b>装配时递的就是这一份</b>（<c>new ReplayAssembly(world, seed, NavVerbs.InjectNames, …)</c>）——
    /// 递 <see cref="Names"/> 的话 call 类会被录进去，而 <see cref="FindPathVerb"/> 那几条重放时
    /// 要的是「自己重算」，重放端再算一遍只会多出一条与录制端语义不同的账。
    /// </summary>
    public static readonly string[] InjectNames = [StampTerrainVerb, SetOrderVerb, ApplyMoveVerb];

    /// <summary>只读的那四条：<b>不进录、不带 ok 字段</b>（「写路径进 Journal」）。</summary>
    public static readonly string[] CallNames = [FindPathVerb, ReadStateVerb, AsciiMapVerb, SolveRecordVerb];

    /// <summary>这一串是不是词表里的动词（区分大小写，与 Journal 里的写法一致）。</summary>
    public static bool IsVerb(string cmd) => Array.IndexOf(Names, cmd) >= 0;

    /// <summary>这一条是写路径（会进 Journal）还是只读。</summary>
    public static bool IsInject(string cmd) => Array.IndexOf(InjectNames, cmd) >= 0;

    /// <summary>
    /// 这一条命令要几段（必填段数）。<b>纯函数，可单独判</b>：不发东西、不起世界 ——
    /// 「第几段该有几个」要能在没有任何一个逻辑世界在场的时候答出来，
    /// 照 ConstructionVerbs.Parse 那个口径（参数次序只有一份，段数也就只有一份）。
    ///
    /// <para>只有 <see cref="SetOrderVerb"/> 按命令档分档（命令空间五档的目标描述不是一个形状，
    /// 硬凑成同一个段数就得给每条命令印一串用不上的空段）。其余动词的段数写死在表里。</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">动词名不认识。</exception>
    /// <exception cref="FormatException">SetOrder 的命令档那一段读不懂（连段数都定不下来）。</exception>
    public static int FieldCount(string cmd, string args)
    {
        var f = (args ?? string.Empty).Split(ReplayArgs.Separator);
        return cmd switch
        {
            StampTerrainVerb => 4,
            SetOrderVerb => OrderFieldCount(At(f, 1), cmd),
            ApplyMoveVerb => 4,
            FindPathVerb => 5,
            ReadStateVerb => 1,
            AsciiMapVerb => 0,
            SolveRecordVerb => 0,
            _ => throw new InvalidOperationException(
                $"未知命令 {cmd}（词表由开发者现场扩——录制包装、桥、CLI 三边同加，改 {nameof(NavVerbs)} 那一份）"),
        };
    }

    /// <summary>
    /// 把一条命令按位置切成结构。<b>这条不发东西</b>，纯函数 ——
    /// 参数一律竖线切（分隔符是 <see cref="ReplayArgs.Separator"/>，全表一份，没有转义档），
    /// 位置固定；缺省值都写在这一份表里。必填段缺了当场抛，不兜 0（「桥只转发」）。
    ///
    /// <para>比 <see cref="FieldCount"/> 多给的段当场不报错，但 <see cref="Canonicalize(string, string)"/>
    /// 之后 Journal 里只剩该有的那些段 —— 一个手势在账本里只有一种段数。</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">动词名不认识。</exception>
    /// <exception cref="FormatException">段数不够 / 必填段空 / 数值读不出 / 枚举读不懂。</exception>
    public static NavVerbCall Parse(string cmd, string args)
    {
        var text = args ?? string.Empty;
        var f = text.Split(ReplayArgs.Separator);
        var need = FieldCount(cmd, text);
        if (f.Length < need)
        {
            throw new FormatException(
                $"{cmd} 要 {Int(need)} 段，收到 {Int(f.Length)} 段（必填参数不兜默认值，「桥只转发」）");
        }

        switch (cmd)
        {
            case StampTerrainVerb:
                return new NavVerbCall(cmd,
                    CellX: Int32(At(f, 0), "格列号", cmd),
                    CellZ: Int32(At(f, 1), "格行号", cmd),
                    Layer: Layer(At(f, 2), cmd),
                    Value: Number(At(f, 3), "值", cmd));
            case SetOrderVerb:
                {
                    var agentId = Required(At(f, 0), "单位id", cmd);
                    var kind = Order(At(f, 1), cmd);
                    var tolerance = Number(At(f, 2), "到达容差", cmd);
                    if (kind == NavCommandKind.Hold && tolerance != 0.0)
                    {
                        throw new FormatException(
                            $"{cmd} 原地不动那一档的到达容差必须是 0，收到 {ReplayArgs.Number(tolerance)}"
                            + "—— 那一档的容差由 NavCommand.Hold 定死，给别的数在 Journal 里与「容差 0」是同一句话，"
                            + "重放端再也分不出这一发到底是什么。");
                    }

                    return kind switch
                    {
                        NavCommandKind.Hold or NavCommandKind.GoTo => new NavVerbCall(cmd,
                            AgentId: agentId,
                            Kind: kind,
                            ToleranceMeters: tolerance,
                            Point: new NavPoint(Number(At(f, 3), "点x", cmd), Number(At(f, 4), "点z", cmd))),
                        NavCommandKind.Chase => new NavVerbCall(cmd,
                            AgentId: agentId,
                            Kind: kind,
                            ToleranceMeters: tolerance,
                            EntityId: Required(At(f, 3), "实体id", cmd),
                            Point: new NavPoint(Number(At(f, 4), "目标x", cmd), Number(At(f, 5), "目标z", cmd))),
                        NavCommandKind.AlongWaypoints => new NavVerbCall(cmd,
                            AgentId: agentId,
                            Kind: kind,
                            ToleranceMeters: tolerance,
                            Waypoints: Waypoints(At(f, 3), cmd)),
                        _ => new NavVerbCall(cmd,
                            AgentId: agentId,
                            Kind: kind,
                            ToleranceMeters: tolerance,
                            Point: new NavPoint(Number(At(f, 3), "中心x", cmd), Number(At(f, 4), "中心z", cmd)),
                            AreaRadiusMeters: Number(At(f, 5), "区域半径", cmd)),
                    };
                }

            case ApplyMoveVerb:
                return new NavVerbCall(cmd,
                    AgentId: Required(At(f, 0), "单位id", cmd),
                    Point: new NavPoint(Number(At(f, 1), "落点x", cmd), Number(At(f, 2), "落点z", cmd)),
                    FacingDegrees: Number(At(f, 3), "朝向", cmd));
            case FindPathVerb:
                return new NavVerbCall(cmd,
                    From: new NavPoint(Number(At(f, 0), "起点x", cmd), Number(At(f, 1), "起点z", cmd)),
                    To: new NavPoint(Number(At(f, 2), "目标x", cmd), Number(At(f, 3), "目标z", cmd)),
                    RadiusMeters: Number(At(f, 4), "半径", cmd));
            case ReadStateVerb:
                return new NavVerbCall(cmd, AgentId: Required(At(f, 0), "单位id", cmd));
            case AsciiMapVerb:
            case SolveRecordVerb:
                return new NavVerbCall(cmd);
            default:
                throw new InvalidOperationException($"未知命令 {cmd}");
        }
    }

    /// <summary>
    /// 规范化：<b>Journal 记的就是这一份</b>，不是调用方原样传进来的串（ReplayAssembly 那份约定）。
    /// 参数编码、规范化一律走 <see cref="ReplayArgs"/>（「重放装配单源」：公共那半只许有一份）——
    /// 同一个数在 Journal 里只有一种 token 写法。
    ///
    /// <para>顺带把多给的段与别名写法一起收掉：<c>1.50</c> 折成 <c>1.5</c>，
    /// 航点串折成点间分号、点内逗号那一副。<b>规范化是纯函数</b>，可单独判。</para>
    /// </summary>
    /// <exception cref="FormatException">与 <see cref="Parse"/> 同一批当场抛。</exception>
    public static string Canonicalize(string cmd, string args) => Canonicalize(Parse(cmd, args));

    /// <summary>把一次解析结果排回那一段 args（<see cref="Canonicalize(string, string)"/> 的另一半，
    /// 录制的包装与重放端共用它 —— 同一件事只有一处排版）。</summary>
    public static string Canonicalize(NavVerbCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Cmd switch
        {
            StampTerrainVerb => ReplayArgs.Join(
                ReplayArgs.Int(call.CellX), ReplayArgs.Int(call.CellZ), LayerToken(call.Layer),
                ReplayArgs.Number(call.Value)),
            SetOrderVerb => ReplayArgs.Join([call.AgentId!, OrderToken(call.Kind),
                ReplayArgs.Number(call.ToleranceMeters), .. OrderArgs(call)]),
            ApplyMoveVerb => ReplayArgs.Join(call.AgentId!,
                ReplayArgs.Number(call.Point.X), ReplayArgs.Number(call.Point.Z),
                ReplayArgs.Number(call.FacingDegrees)),
            FindPathVerb => ReplayArgs.Join(
                ReplayArgs.Number(call.From.X), ReplayArgs.Number(call.From.Z),
                ReplayArgs.Number(call.To.X), ReplayArgs.Number(call.To.Z),
                ReplayArgs.Number(call.RadiusMeters)),
            ReadStateVerb => call.AgentId!,
            AsciiMapVerb or SolveRecordVerb => string.Empty,
            _ => throw new InvalidOperationException($"未知命令 {call.Cmd}"),
        };
    }

    /// <summary>
    /// 解析 + 发到这一侧宿主。<b>返回值两种意思</b>：<c>Ok=true</c> = 这一发打成了；
    /// <c>Ok=false</c> = 世界那儿真没打成（单位没登记过），这个 false 会被录进账、重放要比它。
    /// 「这一侧不接这条词」不走 false，走 <see cref="NotSupportedException"/>（口径 2）。
    /// 作者错误（参数段不对、数值读不出、动词名不认识）当场抛（D7）。
    /// </summary>
    /// <exception cref="NotSupportedException">这一侧的名单里没有这一条动词。</exception>
    public static NavVerbOutcome Dispatch(INavVerbSink sink, string cmd, string args)
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
            StampTerrainVerb => NavVerbOutcome.FromWrite(
                sink.StampTerrain(call.Cell, call.Layer, call.Value)),
            SetOrderVerb => NavVerbOutcome.FromWrite(
                sink.SetOrder(call.AgentId!, call.ToCommand())),
            ApplyMoveVerb => NavVerbOutcome.FromWrite(
                sink.ApplyMove(call.AgentId!, call.Point, call.FacingDegrees)),
            FindPathVerb => NavVerbOutcome.FromRead(
                sink.FindPath(call.From, call.To, call.RadiusMeters)),
            ReadStateVerb => NavVerbOutcome.FromRead(sink.ReadState(call.AgentId!)),
            AsciiMapVerb => NavVerbOutcome.FromRead(sink.AsciiMap()),
            SolveRecordVerb => NavVerbOutcome.FromRead(sink.SolveRecord()),
            _ => throw new InvalidOperationException($"未知命令 {cmd}"),
        };
    }

    /// <summary>
    /// 装配件要的那一支（<see cref="ReplayInject"/> 那个形状）——
    /// <b>接调试桥与录制包装走的是同一次调用</b>，不另开一条（「桥只转发」）。
    ///
    /// <para>三条口径照 <see cref="ReplayAssembly"/>：① 未知动词<b>不是一次写</b>，不入录、返 false；
    /// ② 已知动词被逻辑层拒绝要入录且 ok=false（拒绝是可观察行为，重放要比它）；
    /// ③ 参数给成坏样子返 false，不抛 —— ok 字段是权威，抛异常会让「录进去的东西」有一类重放不了。
    /// <b>但「这一侧不接这条词」照旧往外抛</b>（口径 2），那不是坏参数，是装错了 sink。</para>
    /// </summary>
    public static ReplayInject InjectorFor(INavVerbSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        return (string cmd, string args, out string canonicalArgs, out string reason) =>
        {
            if (!IsVerb(cmd))
            {
                canonicalArgs = args;
                reason = $"{cmd} 不在寻路词表里（未知动词不是一次写，不入录）";
                return false;
            }

            NavVerbCall call;
            try
            {
                call = Parse(cmd, args);
            }
            catch (FormatException e)
            {
                canonicalArgs = args;
                reason = e.Message;
                return false;
            }

            canonicalArgs = Canonicalize(call);
            var outcome = Dispatch(sink, cmd, args);
            reason = outcome.Ok ? string.Empty : outcome.Text;
            return outcome.Ok;
        };
    }

    /// <summary>行分隔与点分隔：竖线是参数分隔符（全表一份），点内逗号、点间分号（航点串占一整格）。</summary>
    public const char PointSeparator = ',';

    /// <summary>航点串里点与点的分隔符。</summary>
    public const char WaypointSeparator = ';';

    // ---- 内部：读段、读数、读枚举 -----------------------------------------------

    private static int OrderFieldCount(string token, string cmd) => Order(token, cmd) switch
    {
        NavCommandKind.Hold or NavCommandKind.GoTo => 5,
        NavCommandKind.AlongWaypoints => 4,
        _ => 6,
    };

    private static string[] OrderArgs(NavVerbCall call) => call.Kind switch
    {
        NavCommandKind.Hold or NavCommandKind.GoTo =>
            [ReplayArgs.Number(call.Point.X), ReplayArgs.Number(call.Point.Z)],
        NavCommandKind.Chase =>
            [call.EntityId!, ReplayArgs.Number(call.Point.X), ReplayArgs.Number(call.Point.Z)],
        NavCommandKind.AlongWaypoints => [WaypointText(call.Waypoints!)],
        _ =>
            [ReplayArgs.Number(call.Point.X), ReplayArgs.Number(call.Point.Z),
                ReplayArgs.Number(call.AreaRadiusMeters)],
    };

    private static string At(string[] f, int index) => index < f.Length ? f[index] : string.Empty;

    private static string Required(string text, string what, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺{what}（必填参数不兜默认值，「桥只转发」）");
        }

        return text;
    }

    private static double Number(string text, string what, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺{what}（必填参数不兜默认值，「桥只转发」）");
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{cmd} 的{what}读不懂 | {text}（要的是数）");
        }

        return value;
    }

    private static int Int32(string text, string what, string cmd)
    {
        var value = Number(text, what, cmd);
        if (value != Math.Floor(value))
        {
            throw new FormatException($"{cmd} 的{what}读不懂 | {text}（要的是整数格号）");
        }

        return (int)value;
    }

    private static NavCommandKind Order(string token, string cmd)
    {
        if (token.Length == 0)
        {
            throw new FormatException($"{cmd} 缺命令档（必填参数不兜默认值，「桥只转发」）");
        }

        return Enum.TryParse<NavCommandKind>(token, false, out var kind) && kind != NavCommandKind.Unset
            ? kind
            : throw new FormatException(
                $"{cmd} 的命令档读不懂 | {token}（只有 Hold / GoTo / Chase / AlongWaypoints / GuardArea）");
    }

    private static string OrderToken(NavCommandKind kind) => kind switch
    {
        NavCommandKind.Hold => nameof(NavCommandKind.Hold),
        NavCommandKind.GoTo => nameof(NavCommandKind.GoTo),
        NavCommandKind.Chase => nameof(NavCommandKind.Chase),
        NavCommandKind.AlongWaypoints => nameof(NavCommandKind.AlongWaypoints),
        NavCommandKind.GuardArea => nameof(NavCommandKind.GuardArea),
        _ => throw new FormatException($"命令档不能是「{kind}」—— 那一档是状态，不是可以重放的一条命令。"),
    };

    private static NavTerrainLayer Layer(string token, string cmd)
    {
        if (token.Length == 0)
        {
            throw new FormatException($"{cmd} 缺层（必填参数不兜默认值，「桥只转发」）");
        }

        return token switch
        {
            "blocked" => NavTerrainLayer.Blocked,
            "pass" => NavTerrainLayer.PassCost,
            "dynamic" => NavTerrainLayer.DynamicCost,
            _ => throw new FormatException(
                $"{cmd} 的层读不懂 | {token}（只有 blocked 地形阻挡 / pass 静态通过成本 / dynamic 动态代价层）"),
        };
    }

    private static string LayerToken(NavTerrainLayer layer) => layer switch
    {
        NavTerrainLayer.Blocked => "blocked",
        NavTerrainLayer.PassCost => "pass",
        NavTerrainLayer.DynamicCost => "dynamic",
        _ => throw new FormatException($"地形层读不懂 | {layer}"),
    };

    private static IReadOnlyList<NavPoint> Waypoints(string text, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺航点串（必填参数不兜默认值，「桥只转发」）");
        }

        var raw = text.Split(WaypointSeparator);
        var points = new NavPoint[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var pair = raw[i].Split(PointSeparator);
            if (pair.Length != 2)
            {
                throw new FormatException(
                    $"{cmd} 的航点串第 {Int(i + 1)} 个点读不懂 | {raw[i]}（要的是 x,z）");
            }

            points[i] = new NavPoint(Number(pair[0], $"航点串第 {Int(i + 1)} 个点的 x", cmd),
                Number(pair[1], $"航点串第 {Int(i + 1)} 个点的 z", cmd));
        }

        return points;
    }

    private static string WaypointText(IReadOnlyList<NavPoint> points)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(WaypointSeparator);
            }

            sb.Append(ReplayArgs.Number(points[i].X)).Append(PointSeparator).Append(ReplayArgs.Number(points[i].Z));
        }

        return sb.ToString();
    }

    private static string Int(int value) => ReplayArgs.Int(value);
}
