#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace DemoFactory.GugaFramework.Construction;

// 建造模块的根类。原型在构造时组合持有它（不继承，spec 3 节第 1 条），
// 之后只经这几个成员与它说话：写路径只有一个入口（动词表），读路径三个单向出口
// （事件流、快照段、裁决与查询）。表现层没有任何合法途径摸到内部状态（D29 是执法条款）。
//
// ticket 01 落的是地基：建筑册、放置提交与移除、事件出口、快照段、动词表入口、悬停裁决流。
// ticket 02 往上挂了建造过程：工期（D15）、两态状态机与去向（D16、D17）、Advance 整数倒数、
// 暂停 / 恢复（D18）、扣血 / 补血（D2、D19）、不透明载荷（D31）。倒数的纯算术与状态两档在
// BuildLifecycle.cs，那一族无副作用。
// 编辑动词（Move / Rotate）在 ticket 03 挂上来：两条走同一条 Check 流水线，占用检查靠
// PlacementRequest 上的 ExcludeBuildingId 排除自身 —— 核心只递一个 id，跳过哪几格是规则的私政（D38）。
// 腾旧占新那条链是 Relocate 一处（Move 与 Rotate 只差「候选点从哪来」与「发哪条事件」）。
// 批量与区域蓝图画在 ticket 04：画线 / 画矩形是部分成功、布局是全有或全无，两套语义刻意并存（D24）。
// 枚举在 BatchGestures（整数格，顺序单一真值），格与点之间的换算问规则（CellOf / CandidateAt）——
// 核心不解释格（D37）。落一座那条链抽成 TryPlaceOne 一处：单件与批量共用，D26 的「成员逐个 Placed」
// 靠它成立。布局文本那一族在 LayoutBlueprint.cs（纯数据，ADR 0007）。
// 场地建筑登记（RegisterFixture）是后来加的那一格：与 Commit 同属那个写路径入口，但跳过落位校验 ——
// 关卡初始摆位那种建筑不是玩家手势，不该过一遍间距判定；它照样占位，所以后来的玩家建筑
// 会被 Check 正常判过近。它那一份占用几何由放置规则产（OccupyFixture，D38），核心不重算。
// 血量绝对值写口（SetHealth）是第三格补上的：Repair 是增量且钳到上限，抬过上限的血线
// （把血线抬过上限那类排布）没有别的口能写，于是它把当前血量与血量上限一起落定。
// 归零之后留不留册是又一个插件槽（removePolicy）：耐久到 0 那一格建造模块只负责问一次，
// 「留在册上还是出册」是原型的规则（同一类可空口还有步号读数、放置规则、宿主）。

/// <summary>
/// 建筑册的一行 —— 蓝图 + 位置 + 方向 + 当前血量 + 建造进度 + 不透明载荷。
///
/// <b>除载荷外字段全是只读 getter</b>：表现层只读，状态变更只走模块那几个写入口（「表现层只读」）。
/// 没有 settable 公开面，是为了让「谁改了血量」这个问题只有一个答案：模块内部那几处。
/// 载荷那一列刻意反过来，理由写在它自己身上（D31）。
/// </summary>
public sealed class Building
{
    internal Building(
        string id,
        string blueprintId,
        Vector2 position,
        GridCell? anchor,
        int orientation,
        IReadOnlyList<GridCell> cells,
        double maxHealth,
        BuildProgress progress,
        object? payload)
    {
        Id = id;
        BlueprintId = blueprintId;
        Position = position;
        Anchor = anchor;
        Orientation = orientation;
        Cells = cells;
        MaxHealth = maxHealth;
        Health = maxHealth;
        Progress = progress;
        Payload = payload;
    }

    public string Id { get; }

    public string BlueprintId { get; }

    /// <summary>世界坐标：网格侧由锚点格心推导、自由侧是量化点（D9：只存一份真值，坐标是推出来的）。</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>登记的锚点格（网格放置）；自由放置是 null（D37：格是规则解释的，核心不替自由侧编一个）。</summary>
    public GridCell? Anchor { get; internal set; }

    /// <summary>方向整数，由放置规则解释（D37）。</summary>
    public int Orientation { get; internal set; }

    /// <summary>占的格名单（网格侧）；自由侧是空表。腾占用走它。</summary>
    public IReadOnlyList<GridCell> Cells { get; internal set; }

    public double MaxHealth { get; internal set; }

    /// <summary>当前血量。放置即满值（D17）。</summary>
    public double Health { get; internal set; }

    /// <summary>建造进度（整数倒数 + 暂停标志 + 状态）。下面那三个是它的只读别名，同一份值。</summary>
    public BuildProgress Progress { get; internal set; }

    /// <summary>状态：在建还是完工（D16：摧毁是去向，不在这里）。</summary>
    public BuildState State => Progress.State;

    /// <summary>还要几步才完工。查「这个建筑为什么没完工」先读这一列，不必数步数。</summary>
    public long RemainingTicks => Progress.RemainingTicks;

    /// <summary>暂停标志。挂着就是 True —— 模块不问为什么暂停（D18）。</summary>
    public bool Paused => Progress.Paused;

    /// <summary>
    /// 不透明载荷：原型挂在 Commit 上的那一个数据对象。<b>模块存它、还它，不读它</b>（D31）——
    /// 不解析、不比较、不进快照段、不进任何事件行（原型那一列原型自己印，D31、D32）。
    ///
    /// <b>可写是刻意的，与血量那一列相反</b>：血量只有模块能改，是为了让「谁改了血量」有一个
    /// 答案；而载荷的语义归原型 —— 原地升级就是「换一份载荷」（D31），不给写入口原型就得在模块
    /// 外面另存一份自己的表，那就是两份真值。写它的是谁由原型自己负责。
    /// </summary>
    public object? Payload { get; set; }

    public override string ToString()
        => $"{Id} {BlueprintId} hp={ConstructionNumbers.Format(Health)}/{ConstructionNumbers.Format(MaxHealth)} " +
            $"{Progress.StateText} left={ConstructionNumbers.Format(Progress.RemainingTicks)}";
}

/// <summary>
/// 建造模块的根类（spec 1 节的「生命周期内完备、seam 外零泄漏」里的那个模块）。
///
/// 构造参数就是四个插件槽里本轮用到的两个 + 时钟：放置规则与宿主（宿主由规则拿着）、
/// 蓝图表（D33 一次递入）、时钟读数（事件带步号，D27），再加上归零去向那一个槽。
/// <b>不递宿主 = 全可建</b>：空地场子与测试不用写假实现。
/// <b>不递移除政策 = 归零即出册</b>：那是模块今天那一档的行为，没递就是调用方没改主意。
/// </summary>
public sealed class ConstructionCore
{
    private readonly IPlacementRule _rule;
    private readonly Dictionary<string, Blueprint> _blueprints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Building> _buildings = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private readonly Func<long> _tick;
    private readonly Func<Building, RemovalCause, bool>? _removePolicy;
    private long _nextId = 1;

    /// <param name="rule">放置规则（形状判定、间距、腾格都走它）。</param>
    /// <param name="blueprints">蓝图表，一次递入（D33）。</param>
    /// <param name="tick">时钟读数；<b>没递 = 恒读 0</b>（事件步号那一列）。</param>
    /// <param name="eventCapacity">事件流容量。</param>
    /// <param name="removePolicy">
    /// 归零去向：<b>耐久到 0 那一格问它，回 true 就出册、回 false 就留在册上</b>。
    /// 留着的这座仍占着格、仍发得出 <see cref="Find"/> 与 <see cref="Damage"/> 那一发。
    ///
    /// <para><b>为什么这一格不替调用方答</b>：「归零即出册」是一条玩法规则，不是册子的规则。
    /// 耐久打空之后那座建筑还在不在（守到最后一刻的镇政府、要留残骸的战场），
    /// 同一个模块里两种原型要的答案正相反，模块硬写死任何一种，另一半就得用别的手段绕开它
    /// （把耐久停在 0.001 那一档、事后自己补登记）。同一类可空口在这件里已经有过几处：
    /// 时钟读数（<c>tick</c> 没递读 0）、放置规则的宿主（没递全可建）、导航的通过闸（没递全放行）。
    /// 那一格还有 <see cref="SetHealth"/>：绝对值写到 0 与扣血扣到 0 是同一件事，
    /// 所以两个出口问的是同一个政策。</para>
    ///
    /// <para><b>没递 = 调用方不表态</b>，行为与递一个恒回 true 的政策逐字相同：
    /// 出册 + 腾占用 + 发 <see cref="BuildingRemoved"/>，施工中打空则
    /// <see cref="BuildingCompleted"/> 永不发。</para>
    /// </param>
    public ConstructionCore(
        IPlacementRule rule,
        IEnumerable<Blueprint> blueprints,
        Func<long>? tick = null,
        int eventCapacity = 256,
        Func<Building, RemovalCause, bool>? removePolicy = null)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _tick = tick ?? (static () => 0L);
        _removePolicy = removePolicy;
        Events = new ConstructionEventLog(eventCapacity);
        Hover = new HoverVerdicts(CheckGesture, _tick);

        ArgumentNullException.ThrowIfNull(blueprints);
        foreach (var bp in blueprints)
        {
            // 形状与规则不匹配当场抛（D33）：这张蓝图在这一侧永远放不下，
            // 让它进册只会把问题推到「鼠标点下去没反应」那一刻。
            if (bp.Footprint.Kind != rule.Kind)
            {
                throw new InvalidOperationException(
                    $"{rule.RuleLabel} 吃 {rule.Kind} 占地，蓝图 {bp.Id} 给的是 {bp.Footprint.Kind}" +
                    "（形状与放置规则不匹配当场抛，D33；不静默折成零格建筑）");
            }

            if (!_blueprints.TryAdd(bp.Id, bp))
            {
                throw new InvalidOperationException(
                    $"蓝图 id {bp.Id} 递进来两次：蓝图表是模块唯一的蓝图真值，重名等于两份");
            }
        }
    }

    /// <summary>事件流。三个只读出口之一（另外两个是快照段与裁决/查询）。</summary>
    public ConstructionEventLog Events { get; }

    /// <summary>候选点输入口 + 悬停裁决推送（ADR 0006）。悬停态只活在这儿，不进 Journal 也不进快照（D28）。</summary>
    public HoverVerdicts Hover { get; }

    /// <summary>蓝图表（只读）。表现层从这里取模型引用（<c>res://</c> 路径，「按 res:// 引用」），模块存而不读（D29）。</summary>
    public IReadOnlyDictionary<string, Blueprint> Blueprints
        => new ReadOnlyDictionary<string, Blueprint>(_blueprints);

    /// <summary>在册建筑数（快照摘要行那一列）。</summary>
    public int Count => _order.Count;

    /// <summary>被占的格数（自由放置 = 占着地的座数，那一侧没有格）。读数走规则，不进模块私产。</summary>
    public int OccupiedCount => _rule.OccupiedCellCount;

    /// <summary>按登记顺序的建筑 id 名单。快照行序就是它 —— 不按字典遍历序，逐字基线才稳。</summary>
    public IReadOnlyList<string> Ids => _order.ToArray();

    /// <summary>取一座（没这号回 null）。</summary>
    public Building? Find(string buildingId)
        => _buildings.TryGetValue(buildingId, out var b) ? b : null;

    /// <summary>画 gizmo 用的只读参数（格长 / 边界 / 量化步长）。表现层只准从这儿取（D29）。</summary>
    public IReadOnlyDictionary<string, double> GizmoParams => _rule.GizmoParams;

    /// <summary>这一处占着的是谁（拾取）。没占着回空串 —— 读数是字符串，空串比 null 少一层判。</summary>
    public string BuildingIdAt(double x, double y)
        => _rule.BuildingIdAt(new Vector2((float)x, (float)y)) ?? "";

    /// <summary>
    /// 纯查询：放不放得下、落在哪、占哪几格、什么方向。悬停、落建、直达口三路共用它（D4）。
    /// <b>不发事件</b>：查询不是状态变更。蓝图 id 不在册 = 拒绝 <see cref="PlacementReason.UnknownBlueprint"/>。
    /// </summary>
    public PlacementVerdict Check(string blueprintId, Vector2 candidate, int orientation = 0)
        => CheckOne(blueprintId, candidate, orientation);

    /// <summary>
    /// 放置一座 —— 事务性：<b>拒绝一寸不动</b>（不登记、不腾、不改册），成功则占位 + 入册 + 发事件。
    ///
    /// 返回的就是那次 Check 的裁决，调用方拿它就知道落在哪、占哪几格（<b>不消费悬停值</b>，
    /// D28 第 3 条：Journal 录的就是这里显式给的候选点，重放自包含）。
    /// </summary>
    /// <param name="blueprintId">蓝图 id（表里没有就回拒绝，不抛）。</param>
    /// <param name="candidate">候选点，模块自己吸附。</param>
    /// <param name="orientation">方向整数，由放置规则解释（D37）。</param>
    /// <param name="buildingId">建筑 id；不给就按登记序自动发 b1、b2……给了重名当场抛（作者错误，D7）。</param>
    /// <param name="buildDurationTicks">这次这一发用几步建；不给就照蓝图自带那份（D15 的逐次覆盖，
    /// 走的是动词表的缺省段机制）。负数当场抛。</param>
    /// <param name="payload">不透明载荷：原型自己的数据对象，模块存而不读、原样归还（D31）。</param>
    public PlacementVerdict Commit(string blueprintId, Vector2 candidate, int orientation = 0,
        string? buildingId = null, long? buildDurationTicks = null, object? payload = null)
    {
        if (!_blueprints.TryGetValue(blueprintId, out var blueprint))
        {
            Emit(new PlacementRejected
            {
                Tick = _tick(),
                Subject = blueprintId,
                Reason = PlacementReason.UnknownBlueprint,
            });
            return new PlacementVerdict(false, PlacementReason.UnknownBlueprint, candidate, orientation,
                Array.Empty<GridCell>(), null, null);
        }

        var verdict = _rule.Check(new PlacementRequest(blueprint, candidate, orientation));
        if (!verdict.Ok)
        {
            // 拒绝也是可观察行为：发一条，原型拿它当「不扣款」的信号（D6）。
            Emit(new PlacementRejected
            {
                Tick = _tick(),
                Subject = blueprintId,
                Reason = verdict.Reason,
            });
            return verdict;
        }

        // 落座那条链只有一处（TryPlaceOne）：单件与批量成员共用，D26 的「成功成员逐个
        // BuildingPlaced」靠它成立 —— 两处各写一遍的话，现存订阅者迟早只接到其中一半。
        TryPlaceOne(blueprint, verdict, buildingId, buildDurationTicks, payload);

        // 占用变了 → 当前手势重算（幽灵该从绿变红了）。顺序是契约：先落占用，再重算。
        Hover.Recompute();
        return verdict;
    }

    /// <summary>
    /// 登记一座<b>场地建筑</b> —— 预先摆好在世界里的那一座（关卡初始摆位、剧情建筑、
    /// 不可由玩家放置的那些）：进册、占位、<b>跳过落位校验</b>、发 <see cref="BuildingRegistered"/>。
    ///
    /// <para><b>为什么要有这一格</b>：裁决流（<see cref="Check"/>）看得见的只有册内与占用索引里的东西。
    /// 一座不在册内的建筑对悬停幽灵是不可见的，而幽灵与点击同源是 D29 的执法条款 —— 于是原型只剩两条路：
    /// 让幽灵自己再算一遍（两处真值），或者在原型的落建入口里补一段自己的距离判定（同一件事的第二份实现）。
    /// 「幽灵绿、点下去被拒」那条 bug 就是走第二条路走出来的。让场地建筑进册比补判定便宜：
    /// 判定只有一处，预览与结果共用它，且这一格的回归测试是离线的（不必出图才看得见）。</para>
    ///
    /// <para><b>跳过的只有落位校验</b>：不查 <see cref="PlacementReason.OutOfBounds"/>、
    /// 不查 <see cref="PlacementReason.TooClose"/>、不查宿主可建性 —— 场地建筑摆在哪就是哪。
    /// <b>占位照做</b>，所以后来的玩家建筑会被 <see cref="Check"/> 正常判过近 / 占格，
    /// 那正是这一格存在的目的。「不判自己」不等于「不占地方」。</para>
    ///
    /// <para><b>它是一座已经在那里的建筑，不是一座刚建成的</b>：进度直接落在完工
    /// （<see cref="BuildProgress.Finished"/>），<b>不发 Completed</b>。发出去就是谎报「这一发刚建成」，
    /// 订阅者（起产出链、表现层建模型）不得不为它各写一个「这一座是场地建筑」的例外分支。</para>
    ///
    /// <para><b>不走 Journal</b>：它在世界初始化期调用，而录制端与重放端<b>都跑同一个
    /// <c>SeedWorld()</c></b>，不录也复现；录一遍反而会在重放端重复登记，当场撞上重名那道抛。
    /// 这是写路径那个入口上唯一一条不录的动词，理由与名单口径记在
    /// <see cref="ConstructionVerbs.InjectNames"/> 上（「写路径全录」 的立意是重放不漏分叉，不是「每一发都录」）。</para>
    ///
    /// <para><b>作者错误当场抛</b>（D7）：建筑 id 必填、蓝图要在册、id 不能重名，三条都是抛。
    /// 这一格没有「打不成」的读数档 —— 它要么登记成功，要么调用方写错了；
    /// 场地建筑的 id 是关卡摆位的一部分，没有玩家手势那个自动发号的档可兜（「桥只转发」：不兜 0）。</para>
    /// </summary>
    /// <param name="blueprintId">蓝图 id（占地半径也是蓝图数据，D12）；不在册 = 当场抛。</param>
    /// <param name="position">摆好的那一点，<b>由放置规则吸一次</b>（与 Check 同一份算术，D29）。</param>
    /// <param name="buildingId">建筑 id（关卡摆位给定的那个）；空 = 当场抛，重名 = 当场抛。</param>
    /// <param name="orientation">方向整数，由放置规则解释（D37）。</param>
    /// <param name="payload">不透明载荷：原型自己的数据对象，模块存而不读、原样归还（D31）。</param>
    /// <param name="insertAt">
    /// 册上的第几格（<b>默认 null = 排在末尾</b>，关卡摆位就按摆的先后）。
    /// 给数就插进那一格（越界钳到册头 / 册尾）—— 最终重试还原要把中途出册的那一座请回它原来那一格，
    /// 排在末尾会让按册序印的读数错位。
    /// </param>
    public WriteResult RegisterFixture(string blueprintId, Vector2 position, string buildingId,
        int orientation = 0, object? payload = null, int? insertAt = null)
    {
        if (string.IsNullOrEmpty(buildingId))
        {
            // 不兜自动发号：场地建筑的 id 是关卡摆位的一部分，兜一个出来等于在重放端换个名，
            // 而「这一座是哪个」正是关卡要钉住的东西（D7 作者错误当场抛，「桥只转发」 不兜 0）。
            throw new InvalidOperationException(
                "场地建筑要带建筑 id：这一格没有自动发号那一档 —— id 是关卡摆位的一部分，" +
                "兜一个出来等于让它换个名字进册（D7 作者错误当场抛）");
        }

        if (!_blueprints.TryGetValue(blueprintId, out var blueprint))
        {
            throw new InvalidOperationException(
                $"{blueprintId} 不在蓝图表里，而这一发要登记场地建筑：占地半径是蓝图数据（D12），" +
                "没有蓝图就没有半径可占，兜一个 0 等于这块地谁也判不过近（D7、「桥只转发」）");
        }

        if (_buildings.ContainsKey(buildingId))
        {
            throw new InvalidOperationException(
                $"{buildingId} 已经在册了：场地建筑的 id 由关卡定，重名就是两份摆位，" +
                "去查那一条命令");
        }

        // 占用几何由放置规则产（D38）：量化 / 格吸附 / D4 变换都是规则解释的东西，
        // 核心自己算一遍，场地建筑占的那块地与玩家读到的禁区边界就会差半个量化步长。
        var verdict = _rule.OccupyFixture(buildingId, blueprint, position, orientation);
        var building = new Building(buildingId, blueprint.Id, verdict.SnappedWorld, verdict.Anchor,
            verdict.Orientation, verdict.Cells, blueprint.MaxHealth, BuildProgress.Finished, payload);
        _buildings[buildingId] = building;
        // 册序：默认排在末尾；给了 insertAt 就落回那一格（越界钳到册头 / 册尾）。
        _order.Insert(insertAt.HasValue ? Math.Clamp(insertAt.Value, 0, _order.Count) : _order.Count,
            buildingId);

        Emit(new BuildingRegistered
        {
            Tick = _tick(),
            Subject = buildingId,
            BlueprintId = blueprint.Id,
            Position = verdict.SnappedWorld,
            Orientation = verdict.Orientation,
            Cells = verdict.Cells,
        });

        // 占用变了 → 当前手势重算（幽灵该当场从绿变红）。顺序是契约：先落占用，再重算。
        Hover.Recompute();
        return WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 移走一座：腾占用 + 出册 + 发 Removed(去向)。<b>连同那条链只在模块内一处</b>
    /// （<see cref="Detach"/>，损毁致死走的是同一个方法）。
    /// id 不在册 = 读数 <c>false</c> + <see cref="PlacementReason.UnknownId"/>，不抛（拒绝是读数，D7）。
    /// </summary>
    public WriteResult Remove(string buildingId, RemovalCause cause = RemovalCause.Dismantle)
    {
        return _buildings.TryGetValue(buildingId, out var building)
            ? Detach(building, cause)
            : WriteResult.Fail(PlacementReason.UnknownId, buildingId);
    }

    /// <summary>
    /// 推进建造进度 <paramref name="steps"/> 步。<b>原型在自己的 Advance 固定点调它</b> ——
    /// 顺序即契约（D17）：先抽检查点快照、再推这一步，事件才带得上正确的步号（「先快照后命令」）。
    ///
    /// <b>模块这边没有定时器对象</b>：进度是整数倒数，原型给几步就是几步。推完这一步里到头的
    /// 那些转完工并发 <see cref="BuildingCompleted"/>；暂停中的跳过倒数（D18，模块不问为什么）。
    /// 同一发里多座同时到头，Completed 的先后就是登记先后 —— 事件流的逐字基线不能靠遍历序。
    /// </summary>
    public void Advance(long steps)
    {
        if (steps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), steps,
                "步数不能是负的：进度是整数倒数，不是双向的（模块没有倒着走的时间）");
        }

        if (steps == 0)
        {
            return;
        }

        // 直接走 _order：这一发里只改进度，不出册不腾占用，册不会在遍历中被改。
        foreach (var id in _order)
        {
            Countdown(_buildings[id], steps);
        }
    }

    /// <summary>
    /// 扣血（D2）。<b>任何状态都可受损</b>，包括施工中（D17）。
    ///
    /// 归零那一格问一次移除政策（构造时递的 <c>removePolicy</c>）：<b>答出册就走
    /// <see cref="Detach"/> 发 Removed(去向=摧毁) 并腾占用；答留就停在册上、耐久 0，
    /// 这一发照样回 ok=true</b>——耐久确实打到了 0，「留不留」是规则给的答案，不是这一次没打中。
    /// 答案要怎么给归原型（「归零去向」那一个插件槽的类注有那一段）。
    /// 答出册时施工中打空则 <see cref="BuildingCompleted"/> 永不发 ——
    /// 建筑已经不在册，<see cref="Advance"/> 走不到它。
    /// <b>摧毁不是状态</b>（D16），所以册里不会留下一座「已摧毁」的占位。
    ///
    /// id 不在册 = 读数 false + <see cref="PlacementReason.UnknownId"/>（不抛，拒绝是读数，D7）。
    /// </summary>
    public WriteResult Damage(string buildingId, double amount)
    {
        RequirePositive(amount, "伤害");
        if (!_buildings.TryGetValue(buildingId, out var building))
        {
            return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        }

        building.Health = Math.Max(0, building.Health - amount);
        Emit(new BuildingDamaged
        {
            Tick = _tick(),
            Subject = buildingId,
            Amount = amount,
            Remaining = building.Health,
        });

        // 先记这一下、再记它没了：致死的那一下要看得见（表现层的小动作词表挂在 Damaged 上）。
        return building.Health <= 0 && DetachOnZero(building, RemovalCause.Destroyed)
            ? Detach(building, RemovalCause.Destroyed)
            : WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 补血（D19），与 <see cref="Damage"/> 对称，钳到血量上限。
    ///
    /// <b>满血回 <see cref="PlacementReason.InvalidState"/>，不是「补了 0」</b>：这一发什么都没
    /// 发生，回读数 <c>false</c> 就是原型「不扣款」的信号（D6）—— 为一口没修上的血付钱是 bug。
    /// 事件里记的是<b>真修上去的那截</b>（钳过之后），不是发过来的那个数。
    /// </summary>
    public WriteResult Repair(string buildingId, double amount)
    {
        RequirePositive(amount, "修复量");
        if (!_buildings.TryGetValue(buildingId, out var building))
        {
            return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        }

        if (building.Health >= building.MaxHealth)
        {
            return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        }

        var applied = Math.Min(amount, building.MaxHealth - building.Health);
        building.Health += applied;
        Emit(new BuildingRepaired
        {
            Tick = _tick(),
            Subject = buildingId,
            Amount = applied,
            Remaining = building.Health,
        });
        return WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 把血量设成<b>绝对值</b>，并把这座的血量上限抬到 <c>max(现有上限, hp)</c>。
    ///
    /// <para><b>为什么上限要一起抬</b>（用户裁决 2026-10-02）：<see cref="Repair"/> 是增量且钳到上限，
    /// 而「堡垒化」要的正是一个<b>抬过上限</b>的数。只抬当前血量不抬上限的话，之后一次
    /// <see cref="Repair"/> 会把血量<b>反向扣回</b>原上限 —— 那不是「修血」，那是扣血，而且
    /// 读数上完全看不出来（回 ok=true，因为「修上了」）。抬完上限之后 Repair 钳到的是抬高后的
    /// 那个新上限，<b>只增不减</b>，D19「Repair 钳到血量上限」照样成立，
    /// 只是那个「上限」被这一条合法抬高过。</para>
    ///
    /// <para><b>上限只抬不降</b>（<c>max</c> 的另一半）：这一条是「抬血线」，不是「改规格」。
    /// 给一个比现上限小的数把上限压回去，那是在没有 Remove + 重新放置的情况下偷换建筑 —— 而
    /// 载荷与血量上限的更换口径是「换一座」（D31）。想要更小的上限，走那一条。</para>
    ///
    /// <para><b>满血照样回 ok=true</b>，与 <see cref="Repair"/> 的满血 <see cref="PlacementReason.InvalidState"/>
    /// 刻意不同款：那一档是 <b>给原型看的「不扣款」信号</b>（D6）—— Repair 是经济动作，
    /// 为一口没修上的血付钱是 bug，所以它得回「什么都没发生」。而这一条不是经济动作，
    /// 它是绝对值写入：值已经是那个数的话这一发<b>照样打成了</b>，回 false 等于让同一条命令
    /// 在重放端因为「上一步已经抬过了」而翻成另一种结果（「断言三层」：命令 ok 翻转任何档位都红）。
    /// 原型要不要为它扣钱是原型的账（D3：经济不进模块），模块不该替它猜。</para>
    ///
    /// <para><b>0 归谁答，负数永远当场抛</b>：没递移除政策时（构造时递的 <c>removePolicy</c> 为空）
    /// 非正数当场抛（D7、「桥只转发」，不兜 0）—— 血量 0 与「归零」是同一件事，而归零在模块里的
    /// 语义是<b>走 Detach 出册</b>（<see cref="Damage"/> 那一侧，D16 摧毁不是状态）。
    /// 让这一条兜 0 就等于给同一件事造了第二个出口，绕过 <see cref="Damage"/> 那条链，
    /// 而那条链带着腾占用与 Removed 事件。血量要归零，走 Damage。</para>
    ///
    /// <para><b>递了移除政策时 0 是合法写入</b>：出不出册已经交给那个政策答，于是这一条
    /// 也问同一个政策（D2 那一侧与这一侧问的是同一条缝，两个出口不会各答各的）：
    /// 答出册走同一条 <see cref="Detach"/> 链、答留就停在册上、耐久 0，回 ok=true；
    /// 血量上限按 <c>max</c> 不动（0 抬不高任何上限）。
    /// <b>负数两种情形都当场抛</b>：册里没有「比 0 还少」这个耐久值，
    /// 0 是一个状态（归零之后留不留由政策答），负数不是。</para>
    ///
    /// <para><b>未知 id 回读数 false + UnknownId</b>（与 Damage / Repair 同款：拒绝是读数，D7）。
    /// <b>已知 id + 合法值永远 ok</b>，没有别的拒绝档。</para>
    /// </summary>
    /// <param name="buildingId">建筑 id。</param>
    /// <param name="hp">目标血量（绝对值；没递移除政策时必须是正数）。</param>
    public WriteResult SetHealth(string buildingId, double hp)
    {
        if (hp <= 0 && _removePolicy is null)
        {
            throw new ArgumentOutOfRangeException(nameof(hp), hp,
                "目标血量必须是正数：归零在模块里的语义是走 Damage 那条链（出册 + 发 Removed + 腾占用），" +
                "从这一条兜 0 等于给它造第二个出口、把那条链绕过去（D7 作者错误当场抛，「桥只转发」）；" +
                "要留住一座归零的建筑就递移除政策，那一格由它答");
        }

        if (hp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hp), hp,
                "目标血量不能是负数：册里没有「比 0 还少」这个耐久值（0 是一个状态，" +
                "归零之后留不留由移除政策答，那不是负数那一档）");
        }

        if (!_buildings.TryGetValue(buildingId, out var building))
        {
            return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        }

        building.Health = hp;
        building.MaxHealth = Math.Max(building.MaxHealth, hp);
        Emit(new BuildingHealthSet
        {
            Tick = _tick(),
            Subject = buildingId,
            Health = building.Health,
            MaxHealth = building.MaxHealth,
        });

        // 与 Damage 那一处同一个问法：写完、发完，再问去不去向。先发的那一发是「耐久落到这个数」。
        return hp <= 0 && DetachOnZero(building, RemovalCause.Destroyed)
            ? Detach(building, RemovalCause.Destroyed)
            : WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 暂停施工（D18）。暂停只是把倒数停住，<b>模块不问为什么</b> —— 「要建造者才施工」这类政策
    /// 留在原型（spec 4 节分工）。
    ///
    /// 已完工 = <see cref="PlacementReason.InvalidState"/>（没东西可暂停）；已经在暂停中同样是
    /// InvalidState —— 这一发什么都不会发生，回 ok=true 就是骗原型说「暂停了」。
    /// 读数不是异常，动词表照录 ok=false（「写路径全录」）。
    /// </summary>
    public WriteResult PauseBuild(string buildingId)
    {
        if (!_buildings.TryGetValue(buildingId, out var building))
        {
            return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        }

        if (building.Progress.State == BuildState.Completed)
        {
            return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        }

        if (building.Paused)
        {
            return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        }

        building.Progress = building.Progress with { Paused = true };
        Emit(new BuildPaused
        {
            Tick = _tick(),
            Subject = buildingId,
            RemainingTicks = building.RemainingTicks,
        });
        return WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 恢复施工（D18）。不在暂停中 = <see cref="PlacementReason.InvalidState"/>，理由与
    /// <see cref="PauseBuild"/> 里那两条一样：没发生的事不许回成事。
    /// </summary>
    public WriteResult ResumeBuild(string buildingId)
    {
        if (!_buildings.TryGetValue(buildingId, out var building))
        {
            return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        }

        if (building.Progress.State == BuildState.Completed)
        {
            return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        }

        if (!building.Paused)
        {
            return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        }

        building.Progress = building.Progress with { Paused = false };
        Emit(new BuildResumed
        {
            Tick = _tick(),
            Subject = buildingId,
            RemainingTicks = building.RemainingTicks,
        });
        return WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 挪一座 —— <b>原子</b>（D20）：走同一条 <see cref="Check"/> 流水线，占用检查排除自己，
    /// 失败一寸不动（位置、占用、载荷、进度、事件流全不变），成了则腾旧占新 + 发一条
    /// <see cref="BuildingMoved"/>。
    ///
    /// <para><b>排除自身是这一条能成立的根</b>：挪一格时新占地与旧占地压着同一格是常事（网格侧锚点
    /// 挪一格就压着两格），不排除的话没有任何建筑动得了，只剩「拆了重建」那条绕行 ——
    /// 而那正是这一条动词要替掉的（D20）。</para>
    ///
    /// <para><b>id / 载荷 / 血量 / 建造进度全保留</b>：这一发只写位置、锚点、方向、占地那四列。
    /// 在建可移、暂停中可移，<b>没有特判</b> —— 进度是整数倒数，挪位置与它无关（D17、D20）。</para>
    ///
    /// <para><b>拒绝不发事件</b>（与 <see cref="Commit"/> 发 PlacementRejected 刻意不同）：编辑器里
    /// 一次没成的拖拽不是「放下一座被拒」，表现层当场读这次回读数就够了；发进事件流只会让「失败
    /// 一寸不动」在一个只追加的口子上不成立。占用没变，悬停也就不重算。</para>
    /// </summary>
    /// <param name="buildingId">建筑 id；不在册 = 读数 false + UnknownId（不抛，拒绝是读数，D7）。</param>
    /// <param name="candidate">候选点，模块自己吸附（与 Commit 同一份吸附，不另写一遍）。</param>
    /// <param name="orientation">方向整数；<b>null = 保持当前方向</b>（动词表那一格可省，D20 的「方向?」）。
    /// 给 0 是「转成 rot0」，不是「别转」—— 这在网格侧是两个不同的数，所以缺省不能兜 0。</param>
    public WriteResult Move(string buildingId, Vector2 candidate, int? orientation = null)
    {
        if (!_buildings.TryGetValue(buildingId, out var building))
        {
            return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        }

        // From 取的是动之前那一点：Relocate 动完，册上那一行已经是新坐标了。
        var from = building.Position;
        if (!Relocate(building, candidate, orientation ?? building.Orientation, out var verdict))
        {
            return WriteResult.Fail(verdict.Reason, buildingId);
        }

        Emit(new BuildingMoved
        {
            Tick = _tick(),
            Subject = buildingId,
            From = from,
            To = verdict.SnappedWorld,
            Orientation = verdict.Orientation,
        });

        // 占用变了 → 当前手势重算（幽灵要从红变绿）。顺序是契约：先落占用，再重算。
        Hover.Recompute();
        return WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 原地转方向（D21）：<b>候选点就是它自己现在那一点</b>，方向给新的那个整数，
    /// 规则自己吸回同一个锚点格（格心再 floor 一次是同一格，D9），所以「原地」不是核心算出来的。
    /// 撞邻居 = 拒绝且一寸不动（连事件都不发）。
    ///
    /// <b>2×2 原地旋转恒成功是自然推论不是特判</b>（D21）：2×2 绕盒心转 90°、180° 落回同一组格，
    /// 排除自身之后那一组格没有别人占着，于是判定与 1×1、3×3 走的是同一条流水线。模块里没有
    /// 一处「占地是不是 2×2」的分支，加特判反而会把「3×3 转 90° 要挪一格」那种真会撞的情况盖掉。
    ///
    /// 方向是<b>绝对值</b>不是「转几步」：那个整数由放置规则解释（D37，网格侧是 D4 八值），
    /// 核心替它做加法就等于把 D4 搬进了核心。「转一步」该给几是规则或表现层那一侧的事。
    /// 方向越界由规则当场抛（D7 作者错误那档），不静默折回 0。
    /// </summary>
    public WriteResult Rotate(string buildingId, int orientation)
    {
        if (!_buildings.TryGetValue(buildingId, out var building))
        {
            return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        }

        if (!Relocate(building, building.Position, orientation, out var verdict))
        {
            return WriteResult.Fail(verdict.Reason, buildingId);
        }

        Emit(new BuildingRotated
        {
            Tick = _tick(),
            Subject = buildingId,
            Orientation = verdict.Orientation,
            Cells = verdict.Cells,
        });

        Hover.Recompute();
        return WriteResult.Done(buildingId);
    }

    // ---- 批量手势：画线 / 画矩形（部分成功，D24）---------------------------------

    /// <summary>
    /// 画线的纯查询（D25）：逐格给裁决，<b>与 <see cref="PlaceLine"/> 同一条流水线</b> ——
    /// 预览读到的那一串红格与落建报出来的那一串失败格是同一句话（D29）。
    /// <b>不发事件</b>，不占任何格。
    /// </summary>
    public PlacementVerdict CheckLine(string blueprintId, Vector2 start, Vector2 end, int orientation = 0)
        => Summarize(CheckCells(blueprintId, BatchShapes.Line(_rule.CellOf(start), _rule.CellOf(end)), orientation),
            start, orientation);

    /// <summary>画矩形的纯查询（D25）。两个角自动按 min/max 归一（表现层从哪一角起拖都一样）。</summary>
    public PlacementVerdict CheckRect(string blueprintId, Vector2 min, Vector2 max, int orientation = 0)
        => Summarize(CheckCells(blueprintId, BatchShapes.Rect(_rule.CellOf(min), _rule.CellOf(max)), orientation),
            min, orientation);

    /// <summary>
    /// 画一线的读数：<b>部分成功</b> —— 能放几座放几座，返回成功计数 + 逐格裁决（D24）。
    ///
    /// <para><b>边判边落</b>：每一格判成了立刻落座，下一格的 Check 于是看得见前一格刚占下的地，
    /// 规则自己把「两个锚点压着」的那些格挡成 Occupied。核心不预先模拟占用 —— 那是规则的私政（D38），
    /// 而边判边落正是「部分成功」这个语义本身。</para>
    ///
    /// <para><b>失败格不逐个发 Rejected</b>（D26）：裁决流（<see cref="BatchResult.Members"/>）
    /// 与汇总事件（一条 <see cref="BatchPlaced"/>）两处都有了，逐格再发一遍只是把同一件事
    /// 说三遍。成功成员逐个发 <see cref="BuildingPlaced"/>，那一条不能省（D26：现有订阅者零新接线）。</para>
    ///
    /// <para><b>一个手势一条 Journal 记录</b>（D24）：这一发就是动词表里的一个动词名 +
    /// 六个参数，不逐格录 —— 逐格录等于让重放端自己数一遍数量，而那正是要防的分叉。</para>
    /// </summary>
    public BatchResult PlaceLine(string blueprintId, Vector2 start, Vector2 end, int orientation = 0)
        => PlaceCells(GestureShape.Line, blueprintId, BatchShapes.Line(_rule.CellOf(start), _rule.CellOf(end)),
            orientation);

    /// <summary>画一个矩形的读数。<b>部分成功</b>，与 <see cref="PlaceLine"/> 同款（D24）。</summary>
    public BatchResult PlaceRect(string blueprintId, Vector2 min, Vector2 max, int orientation = 0)
        => PlaceCells(GestureShape.Rect, blueprintId, BatchShapes.Rect(_rule.CellOf(min), _rule.CellOf(max)),
            orientation);

    // ---- 区域蓝图：捕获（只读）与放置（全有或全无，D23、ADR 0007）------------------

    /// <summary>
    /// 捕获一个矩形区域里的建筑，回一份布局文本（<b>只读</b>：call 类、不入录、不改世界，
    /// ADR 0007）。空区域回 0 成员文本，那不算错。
    ///
    /// <para><b>判据是「锚点格落在区域里」</b>：网格侧按行再按列枚举，第一个命中的格就是那座
    /// 建筑的锚点格（D9：锚点 = 最小行最小列），于是跨出区域的建筑照原样带出整份占地 ——
    /// 截一半的占地放下去是一座缺着角的建筑，比不放更坏。</para>
    ///
    /// <para><b>成员偏移相对「成员自己那一堆」的最小角</b>归一（与 D9 占地归一同一口径），
    /// 所以文本与位置无关：把锚点给回区域最小角那一格，还原出来就是原来那一块。
    /// 名字由区域最小角生成（<c>capture-c列-r行</c>）—— 捕获是纯函数，同一区域两次捕获逐字同文，
    /// 那是逐字重放能成立的前提。</para>
    ///
    /// <para>网格侧那几列取建筑自己登记的<b>方向整数</b>（D4 八值），自由侧取量化角；
    /// 模块只透传那个整数（D37）。</para>
    /// </summary>
    public string CaptureLayout(Vector2 min, Vector2 max)
    {
        var origin = _rule.CellOf(min);
        var cells = BatchShapes.Rect(origin, _rule.CellOf(max));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<LayoutMember>();

        foreach (var cell in cells)
        {
            var id = _rule.BuildingIdAt(_rule.CandidateAt(cell));
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                continue;
            }

            var building = FindRegistered(id);
            members.Add(new LayoutMember(building.BlueprintId, cell, building.Orientation));
        }

        // 归一：偏移相对成员自己那一堆的最小角，不是相对区域最小角（理由写在上面的注释里）。
        var minOf = members.Count == 0
            ? new GridCell(0, 0)
            : new GridCell(
                MinOf(members, static m => m.Offset.Col),
                MinOf(members, static m => m.Offset.Row));
        var normalized = new List<LayoutMember>(members.Count);
        foreach (var m in members)
        {
            normalized.Add(new LayoutMember(m.BlueprintId,
                new GridCell(m.Offset.Col - minOf.Col, m.Offset.Row - minOf.Row), m.Orientation));
        }

        return new Layout(NameForRegion(origin), normalized).Serialize();
    }

    /// <summary>
    /// 布局放置的纯查询（D25）：逐成员 Check，与 <see cref="PlaceLayout"/> 同一条流水线。
    /// <b>成员之间互相压着的格也算失败</b>（见 <see cref="PlaceLayout"/> 的理由），
    /// 布局的成员偏移不合法（例如网格侧给了小数）由放置规则当场抛（D7）。
    /// </summary>
    public PlacementVerdict CheckLayout(string layoutText, Vector2 anchor, int orientation = 0)
    {
        var layout = Layout.Parse(layoutText);
        var members = CheckLayoutMembers(_rule.TransformLayout(layout, orientation), _rule.CellOf(anchor));
        return Summarize(members, anchor, orientation);
    }

    /// <summary>
    /// 放一整份区域蓝图：<b>全有或全无</b>（D23）—— 逐成员先判，有一个判不过就一寸不动，
    /// 只发一条 <see cref="LayoutRejected"/>、成员零事件（D26）。全判过了才逐个落座
    /// （成员逐个 <see cref="BuildingPlaced"/> + 一条 <see cref="LayoutPlaced"/>，D26 的并存那一档）。
    ///
    /// <para><b>为什么这一路要多判一层「成员之间压着」</b>：成员是挨个判的，轮到第二个的时候
    /// 第一个还没落，规则的占用索引里没有它，于是两个成员压在同一格的话两次都判成 ——
    /// 落座那一步就会在规则的 Occupy 上当场抛，而那时前面的已经落下去了，正好违反「一寸不动」。
    /// 规则看不见还没落的东西（那是它的私政，D38），这一层比对只有核心做得了。
    /// 报的仍然是封闭枚举里那一档 Occupied，理由是「这一格已经有东西了」。</para>
    ///
    /// <para><b>0 成员布局当场抛</b>（D7、ADR 0007）：空区域捕获出来的文本是合法数据，
    /// 但「放一个什么都不干的布局」是脚本作者错误。捕获不算错与放置算错，分界在这两个动作上。</para>
    ///
    /// <para><b>整体方向是绝对值</b>（D37）：变换由放置规则做，核心只透传那个整数。</para>
    /// </summary>
    public BatchResult PlaceLayout(string layoutText, Vector2 anchor, int orientation = 0)
    {
        var layout = Layout.Parse(layoutText);
        if (layout.Members.Count == 0)
        {
            throw new InvalidOperationException(
                $"布局 {layout.Name} 是 0 个成员，放下去什么也不会发生：这是脚本作者错误，" +
                "当场抛（D7、ADR 0007）。空区域捕获回 0 成员文本是对的，放置它不是。");
        }

        var prepared = _rule.TransformLayout(layout, orientation);
        var members = CheckLayoutMembers(prepared, _rule.CellOf(anchor));
        var result = new BatchResult(members);

        // 判据是「有没有判不过的成员」，不是「落下几个」：这一发要么整份都落，要么一条都不落，
        // 拿 PlacedCount 去比成员数是绕一道弯 —— 而绕出来的那个数在整体被拒时并不等于落了几个
        // （判过了却没落下的那些成员不在 Placed 里，见 BatchResult 的构造器）。
        if (result.FailedCount > 0)
        {
            // 拒绝一寸不动：没占、没入册、没改任何一列，悬停也不重算（占用没变）。
            Emit(new LayoutRejected
            {
                Tick = _tick(),
                Subject = prepared.Name,
                Failures = BatchText.LayoutFailures(result),
            });
            return result;
        }

        var placed = new List<BatchMember>(members.Count);
        var ids = new List<string>(members.Count);
        for (var i = 0; i < members.Count; i++)
        {
            var member = prepared.Members[i];
            // 全判过了，每一个成员的蓝图必然在册 —— 走不到 RequireRegistered 那条抛。
            var blueprint = RequireRegistered(member.BlueprintId);
            var id = TryPlaceOne(blueprint, members[i].Verdict);
            ids.Add(id);
            placed.Add(members[i] with { BuildingId = id });
        }

        Emit(new LayoutPlaced
        {
            Tick = _tick(),
            Subject = prepared.Name,
            LayoutName = prepared.Name,
            Members = ConstructionNumbers.Ids(ids),
        });

        Hover.Recompute();
        return new BatchResult(placed);
    }

    // ---- 批量拆除（D24）-----------------------------------------------------------

    /// <summary>
    /// 拆掉一个矩形区域里的全部建筑：<b>部分成功</b>、逐成员发
    /// <see cref="BuildingRemoved"/>(去向=拆除)、<b>不加汇总事件</b>（D24）。
    ///
    /// <para><b>先收集后拆除</b>：拆除过程中占用在变，边走边拆要判两遍「这一格现在归谁」。
    /// 收集阶段的次序就是成员表次序 —— 一座 2×2 的建筑占四格，只拆一次（第一格就认出了它，
    /// 后三格跳过），逐成员发的事件也就只有一条。</para>
    ///
    /// <para><b>空格不算失败</b>：「这一格没人」是世界的常态，不是错，所以读数恒为打成了
    /// （<see cref="RectRemovalResult"/> 的说明写在那儿）。逐格清扫与逐座退款都按
    /// <see cref="RectRemovalResult.RemovedIds"/> 算，汇总事件在这里没有信息量：有一条
    /// <see cref="BatchPlaced"/> 那种汇总就够画 HUD，而 D24 明写不加。</para>
    /// </summary>
    public RectRemovalResult RemoveRect(Vector2 min, Vector2 max)
    {
        var cells = BatchShapes.Rect(_rule.CellOf(min), _rule.CellOf(max));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var removed = new List<string>();
        var empty = 0;

        foreach (var cell in cells)
        {
            var id = _rule.BuildingIdAt(_rule.CandidateAt(cell));
            if (string.IsNullOrEmpty(id))
            {
                empty++;
                continue;
            }

            if (!seen.Add(id))
            {
                continue;
            }

            removed.Add(id);
        }

        var ok = true;
        var reason = PlacementReason.None;
        foreach (var id in removed)
        {
            // id 来自规则自己的占用索引，与册必然同步（Occupy 只对落座成功的那几座调过）。
            // 万一不是，那是册与索引分家了，回读数不如当场抛。
            var building = Find(id) ?? throw new InvalidOperationException(
                $"{id} 占着地却不在册：册与占用索引分家了，去查那一条命令（RemoveRect 收集阶段）");
            var result = Detach(building, RemovalCause.Dismantle, recomputeHover: false);
            ok = ok && result.Ok;
            if (!result.Ok)
            {
                reason = result.Reason;
            }
        }

        if (removed.Count > 0)
        {
            Hover.Recompute();
        }

        return new RectRemovalResult(removed, cells.Count, empty) { Ok = ok, Reason = reason };
    }

    /// <summary>检查点快照段（状态级）。悬停态一个字都不进（D28 第 2 条）。</summary>
    public string DebugSnapshotSegment() => RenderSegment();

    /// <summary>重放快照段。内容与调试段同一份格式化 —— 两个入口两个名字，格式只有一处（D32）。</summary>
    public string ReplaySnapshotSegment() => RenderSegment();

    /// <summary>推出悬停裁决用的那一发查询；手势为 null 时回 null（= 这一轮没有候选点）。
    ///
    /// <b>四个形状都从这里过</b>（D25、D27）：表现层推什么形状，悬停就查什么形状，
    /// 走的是同名的公开查询 —— 悬停里没有第二份枚举，也没有「批量版的 Check」。
    /// 手势的缺件在 <see cref="HoverVerdicts.Push"/> 存进当前手势之前就抛了（D7），
    /// 所以这里的两个 <c>!</c> 走不到：<see cref="GestureSpec.Validate"/> 是它的前提。</summary>
    private PlacementVerdict? CheckGesture(GestureSpec? gesture)
    {
        if (gesture is null)
        {
            return null;
        }

        return gesture.Shape switch
        {
            GestureShape.Single => CheckOne(gesture.BlueprintId, gesture.Candidate, gesture.Orientation),
            GestureShape.Line => CheckLine(gesture.BlueprintId, gesture.Candidate, gesture.LineEnd!.Value,
                gesture.Orientation),
            GestureShape.Rect => CheckRect(gesture.BlueprintId, gesture.Candidate, gesture.RectMax!.Value,
                gesture.Orientation),
            GestureShape.Layout => CheckLayout(gesture.LayoutText!, gesture.Candidate, gesture.Orientation),
            _ => throw new ArgumentOutOfRangeException(nameof(gesture), gesture.Shape,
                "批量形状只有单件 / 画线 / 画矩形 / 布局四个值（D25）"),
        };
    }

    // ---- 批量与布局的内部共用（枚举、逐成员判、汇总）------------------------------

    /// <summary>
    /// 一串格逐个送进 <see cref="CheckOne"/>，回成员表（<b>纯查询，不发事件</b>）。
    ///
    /// <para>蓝图不在册时<b>不去跑 N 次校验</b>：每个成员直接回 UnknownBlueprint。
    /// 逐格表照样是全的（N 行，一行一个原因），而规则那边一次都不用问。</para>
    /// </summary>
    private List<BatchMember> CheckCells(string blueprintId, IReadOnlyList<GridCell> cells, int orientation)
    {
        var members = new List<BatchMember>(cells.Count);
        foreach (var cell in cells)
        {
            var candidate = _rule.CandidateAt(cell);
            members.Add(new BatchMember(cell, blueprintId, default, candidate,
                CheckOne(blueprintId, candidate, orientation)));
        }

        return members;
    }

    /// <summary>线与矩形那条落座链：<b>边判边落</b> + 一条汇总事件（部分成功，D24）。</summary>
    private BatchResult PlaceCells(GestureShape shape, string blueprintId, IReadOnlyList<GridCell> cells,
        int orientation)
    {
        var members = new List<BatchMember>(cells.Count);
        foreach (var cell in cells)
        {
            // 判与落必须在同一个循环里：这一格判成、立刻落座，下一格的 Check 才看得见它刚占下的地。
            // 先判完整排再落（那是布局的全有或全无）的话，两个锚点压着的那些格会双双判成，
            // 落座那一步就在规则的 Occupy 上当场抛 —— 抛出来时前面的已经落下去了，
            // 「逐格报原因」变成「整个手势炸掉」。
            var candidate = _rule.CandidateAt(cell);
            var verdict = CheckOne(blueprintId, candidate, orientation);
            var member = new BatchMember(cell, blueprintId, default, candidate, verdict);
            if (verdict.Ok)
            {
                member = member with { BuildingId = TryPlaceOne(RequireRegistered(blueprintId), verdict) };
            }

            members.Add(member);
        }

        var result = new BatchResult(members);
        Emit(new BatchPlaced
        {
            Tick = _tick(),
            Subject = blueprintId,
            Shape = shape,
            Placed = ConstructionNumbers.Ids(result.PlacedIds),
            Failures = BatchText.FailureCells(result),
        });

        // 占用变了 → 重算一次。逐个成员各重算一遍是 N 份全量校验，而这一发里当前手势的裁决
        // 只能变一次（D29 的封口）。
        if (result.PlacedCount > 0)
        {
            Hover.Recompute();
        }

        return result;
    }

    /// <summary>
    /// 布局的逐成员判定：<b>先全判，一个都不落</b>（D23）。
    /// 成员之间的压格在这一层算掉（理由写在 <see cref="PlaceLayout"/> 上）。
    /// </summary>
    private List<BatchMember> CheckLayoutMembers(Layout layout, GridCell anchorCell)
    {
        var members = new List<BatchMember>(layout.Members.Count);
        var taken = new HashSet<GridCell>();
        foreach (var member in layout.Members)
        {
            var cell = anchorCell + member.Offset;
            var candidate = _rule.CandidateAt(cell);
            var verdict = CheckOne(member.BlueprintId, candidate, member.Orientation);
            if (verdict.Ok && Clashes(verdict.Cells, taken))
            {
                // 同一份布局里两个成员压着同一格：报的还是 Occupied 那一档 ——
                // 那一格确实已经有东西了，只不过那个东西还没落进世界的占用索引。
                verdict = verdict with { Ok = false, Reason = PlacementReason.Occupied };
            }
            else if (verdict.Ok)
            {
                foreach (var c in verdict.Cells)
                {
                    taken.Add(c);
                }
            }

            members.Add(new BatchMember(cell, member.BlueprintId, member.Offset, candidate, verdict));
        }

        return members;
    }

    /// <summary>批量手势的汇总裁决（<see cref="PlacementVerdict.Members"/> 那几列的读法）。</summary>
    private static PlacementVerdict Summarize(IReadOnlyList<BatchMember> members, Vector2 anchor, int orientation)
    {
        var ok = members.Count > 0;
        var reason = PlacementReason.None;
        foreach (var m in members)
        {
            if (m.Ok)
            {
                continue;
            }

            ok = false;
            reason = m.Reason;
            break;
        }

        var table = new List<PlacementVerdict>(members.Count);
        foreach (var m in members)
        {
            table.Add(m.Verdict);
        }

        // 落点那几列刻意留空：批量没有「一个落点」，硬凑一个就是在两处各给一个答案（D29）。
        return new PlacementVerdict(ok, reason, members.Count == 0 ? anchor : members[0].Verdict.SnappedWorld,
            orientation, Array.Empty<GridCell>(), null, null, table);
    }

    private static bool Clashes(IReadOnlyList<GridCell> cells, HashSet<GridCell> taken)
    {
        foreach (var c in cells)
        {
            if (taken.Contains(c))
            {
                return true;
            }
        }

        return false;
    }

    private static int MinOf(IReadOnlyList<LayoutMember> members, Func<LayoutMember, int> pick)
    {
        var min = int.MaxValue;
        foreach (var m in members)
        {
            min = Math.Min(min, pick(m));
        }

        return min;
    }

    /// <summary>捕获时用：id 来自规则自己的占用索引，册里必然有。</summary>
    private Building FindRegistered(string buildingId)
        => Find(buildingId) ?? throw new InvalidOperationException(
            $"{buildingId} 占着地却不在册：册与占用索引分家了，去查那一条命令（捕获阶段）");

    private Blueprint RequireRegistered(string blueprintId)
        => _blueprints.TryGetValue(blueprintId, out var blueprint)
            ? blueprint
            : throw new InvalidOperationException(
                $"{blueprintId} 不在蓝图表里，而这一格判成了：判定与查表分家了，去查 CheckOne");

    /// <summary>捕获出来的布局名：区域最小角生成的，捕获是纯函数就得有个确定的来源。</summary>
    private static string NameForRegion(GridCell min)
        => $"capture-c{min.Col.ToString(CultureInfo.InvariantCulture)}-r{min.Row.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// 推进一座的进度，转完工的那一发也在这里发 —— <b>Commit（工期 0，推 0 步）与 Advance
    /// （工期 n，推 n 步）调的是这一个方法</b>。D16「工期 0 无特判」落在这里：全模块没有第二处
    /// 「剩步到 0 就转完工」的代码，所以这两条路不会长出两套行为。
    /// </summary>
    private void Countdown(Building building, long steps)
    {
        var before = building.Progress;
        var after = before.Step(steps);
        if (after == before)
        {
            // 暂停中或已完工：这一发什么都没发生，一条事件都不发。
            return;
        }

        building.Progress = after;
        if (before.State != after.State)
        {
            Emit(new BuildingCompleted
            {
                Tick = _tick(),
                Subject = building.Id,
            });
        }
    }

    /// <summary>
    /// 归零那一格的去向：<b>问移除政策，没递政策就算答出册</b>。
    /// <b>两个出口共用这一个问法</b>（<see cref="Damage"/> 扣到 0 与 <see cref="SetHealth"/> 写进 0），
    /// 所以「同一个原因在两条路上给两个答案」这件事没有发生的地方。
    /// 政策在那条缝上看的永远是 <see cref="RemovalCause.Destroyed"/>。
    /// </summary>
    private bool DetachOnZero(Building building, RemovalCause cause)
        => _removePolicy is null || _removePolicy(building, cause);

    /// <summary>
    /// 出册那条链：腾占用 + 除册 + 发 Removed(去向) + 重算悬停。<b>只有这一处</b> ——
    /// 移除（拆除 / 取消）与扣血归零（摧毁）都走它，所以「腾没腾干净」这件事没有第二条路径能漏。
    /// （归零那一格先问移除政策：政策答留的那一次走不到这里，走到这里的就是真出册。）
    /// <paramref name="recomputeHover"/> 给批量拆除用：那一发拆掉若干座，重算在整发末尾做一次
    /// 就够，逐座各跑一遍 N 份全量校验是白花的钱（而裁决没变时它还不发事件，所以只是慢，不是错）。
    /// </summary>
    private WriteResult Detach(Building building, RemovalCause cause, bool recomputeHover = true)
    {
        _rule.Vacate(building.Id);
        _buildings.Remove(building.Id);
        _order.Remove(building.Id);
        Emit(new BuildingRemoved
        {
            Tick = _tick(),
            Subject = building.Id,
            Cause = cause,
        });

        if (recomputeHover)
        {
            Hover.Recompute();
        }

        return WriteResult.Done(building.Id);
    }

    /// <summary>
    /// 落一座那条链：定工期 + 发号 + 占位 + 入册 + 发 Placed + 推 0 步（转完工）。
    /// <b>只有这一处</b>（<see cref="Commit"/> 与批量 / 布局三条路共用），理由是 D26 那条：
    /// 成功成员要逐个发 BuildingPlaced，现存订阅者零新接线 —— 一旦有第二条落座的实现，
    /// 那条订阅就会只接到一半事件，而「接到一半」在画面上表现为「有的建筑有音效有的没有」。
    ///
    /// <para>调用方必须已经判成：<b>不在这里判</b>。线与矩形是边判边落（部分成功，D24），
    /// 布局是先全判后落（全有或全无，D23），两条路的时机不同但落座这一步同款。
    /// 任何一条走到这里还没判成，就说明上面漏了一道 —— 规则的 Occupy 见到重名会当场抛，
    /// 那道防线是对的（登记两次等于漏腾一次格）。</para>
    /// </summary>
    /// <returns>发出去那个建筑 id。</returns>
    private string TryPlaceOne(Blueprint blueprint, PlacementVerdict verdict, string? buildingId = null,
        long? buildDurationTicks = null, object? payload = null)
    {
        // 工期先定：它是这一座建成要推几步，与放置判定无关，但拒绝时不许有任何状态动。
        var duration = ResolveDuration(blueprint, buildDurationTicks);

        // 自动 id 撞上显式给过的那个就往后挪一号，不当场抛：撞名是调用方自己起的名，
        // 抛出来会让人误以为是放置出了问题。显式重名才是作者错误。
        var id = buildingId;
        if (string.IsNullOrEmpty(id))
        {
            do
            {
                id = $"b{_nextId++}";
            }
            while (_buildings.ContainsKey(id));
        }
        else if (_buildings.ContainsKey(id))
        {
            throw new InvalidOperationException(
                $"{id} 已经在册了：给 id 的调用方要么重名要么在重放里重编了一次，查那一条命令");
        }

        _rule.Occupy(id, verdict);
        var building = new Building(id, blueprint.Id, verdict.SnappedWorld, verdict.Anchor,
            verdict.Orientation, verdict.Cells, blueprint.MaxHealth, BuildProgress.Start(duration), payload);
        _buildings[id] = building;
        _order.Add(id);

        Emit(new BuildingPlaced
        {
            Tick = _tick(),
            Subject = id,
            BlueprintId = blueprint.Id,
            Position = verdict.SnappedWorld,
            Orientation = verdict.Orientation,
            Cells = verdict.Cells,
        });

        // 推 0 步：工期 0 的当场翻成完工并连发 Completed，工期 n 的这一步什么也不发生（D16
        // 「无特判」就是这个意思 —— 这里没有 if 工期 == 0，只有同一个方法调两次）。
        Countdown(building, 0);
        return id;
    }

    /// <summary>
    /// 一次放置查询的唯一实现：查表 + 问规则（D4）。蓝图不在册 = 回读数 UnknownBlueprint，不抛。
    /// 单件的 <see cref="Check"/> 与批量三条查询都走它 —— <b>预览与落建因此是同一条流水线</b>
    /// （D25、D29），批量那一族没有第二个「批量版 Check」。
    /// </summary>
    private PlacementVerdict CheckOne(string blueprintId, Vector2 candidate, int orientation)
        => _blueprints.TryGetValue(blueprintId, out var blueprint)
            ? _rule.Check(new PlacementRequest(blueprint, candidate, orientation))
            : new PlacementVerdict(false, PlacementReason.UnknownBlueprint, candidate, orientation,
                Array.Empty<GridCell>(), null, null);

    /// <summary>
    /// 换落点那条链：<b>Move 与 Rotate 共用这一处</b>。两条动词的差别只有「候选点从哪来」与
    /// 「发哪条事件」，判定与写入必须同款 —— 各写一遍的话，D20 的原子与 D21 的「一寸不动」
    /// 就有两个实现要各自保证，而它们迟早会漂。
    ///
    /// <para><b>顺序即原子性</b>：先 Check 后写，判成了才腾旧占新。「腾」先于「占」是因为规则的
    /// Occupy 见到重名会当场抛 —— 那道防线是对的（登记两次等于漏腾一次格）。新格与别人撞着的话
    /// 会在 Occupy 那一侧抛而不是静默通过，那说明刚才那次 Check 漏了，规则私政有 bug。</para>
    ///
    /// <para><b>回 false 时一寸不动</b>：没腾、没占、没改册、没发事件、悬停也没重算（调用方在
    /// 这一侧返回读数就够）。排除自身这件事整个模块只有这一句：
    /// <c>ExcludeBuildingId = building.Id</c>。</para>
    /// </summary>
    private bool Relocate(Building building, Vector2 candidate, int orientation, out PlacementVerdict verdict)
    {
        if (!_blueprints.TryGetValue(building.BlueprintId, out var blueprint))
        {
            // 走不到的路：蓝图构造时一次递入且标不可变（D33），而这一列的 id 进了册就没被谁改过。
            // 留着这一档是因为它一旦发生只能是「册与表分家」，回读数比抛 KeyNotFound 好查。
            verdict = new PlacementVerdict(false, PlacementReason.UnknownBlueprint, candidate, orientation,
                Array.Empty<GridCell>(), null, null);
            return false;
        }

        verdict = _rule.Check(new PlacementRequest(blueprint, candidate, orientation, building.Id));
        if (!verdict.Ok)
        {
            return false;
        }

        _rule.Vacate(building.Id);
        _rule.Occupy(building.Id, verdict);

        // 只写这四列。id / 载荷 / 血量 / 进度那一律不碰 —— 挪位置与它们无关（D20）。
        building.Position = verdict.SnappedWorld;
        building.Anchor = verdict.Anchor;
        building.Orientation = verdict.Orientation;
        building.Cells = verdict.Cells;
        return true;
    }

    /// <summary>这次这一发用几步建：给了覆盖值就用它，没给就用蓝图自带那份（D15 的逐次覆盖）。
    /// 负数是作者错误当场抛（D7）—— 静默折成 0 等于「秒建」，那比报错难查得多。</summary>
    private static long ResolveDuration(Blueprint blueprint, long? buildDurationTicks)
    {
        if (buildDurationTicks is not { } given)
        {
            return blueprint.BuildDurationTicks;
        }

        if (given < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buildDurationTicks), given,
                $"工期覆盖不能是负数：{blueprint.Id} 自带 {ConstructionNumbers.Format(blueprint.BuildDurationTicks)} 步，" +
                "静默折成 0 等于秒建，比报错难查得多（D7 作者错误当场抛）");
        }

        return given;
    }

    /// <summary>
    /// 扣血与补血的数值都要正数。0 与负数这一发什么都没发生，而回 ok=true 会让原型为一次没打中
    /// 的攻击 / 一次没修上的血付钱（D6 的拒绝信号就是这么用的）—— 与满血 Repair 回 InvalidState
    /// 同一条理由。负数更不能放行：从这里过去等于绕过 Repair 那条带满血检查的路偷偷回血。
    /// </summary>
    private static void RequirePositive(double amount, string what)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount,
                $"{what}必须是正数（0 与负数这一发什么都没发生，回读数 true 就是骗原型付钱；" +
                "满血那一档请让模块回 InvalidState，别在这里兜成 0）");
        }
    }

    private void Emit(ConstructionEventPayload payload) => Events.Add(payload);

    /// <summary>
    /// 快照段：一行摘要 + 每建筑一行（D32）。<b>格式化只有这一处</b> —— 原型把这段嵌进自己的
    /// DebugSnapshot / ReplaySnapshot，列要是两处各拼一次就会漂。
    /// 摘要与每行都只印模块自己的列；不透明载荷与原型那一列归原型自己印（D31）。
    /// </summary>
    private string RenderSegment()
    {
        var sb = new StringBuilder();
        sb.Append("construction kind=").Append(_rule.Kind)
            .Append(" buildings=").Append(ConstructionNumbers.Format(Count))
            .Append(" occupied=").Append(ConstructionNumbers.Format(OccupiedCount))
            .Append(" bounds=").Append(_rule.BoundsText)
            .Append('\n');

        foreach (var id in _order)
        {
            var b = _buildings[id];
            // 列序照 D32：id、蓝图、锚点或坐标、HP、状态、剩余步、暂停标志、方向。
            // 只印模块自己的列：不透明载荷与原型那一列不在这儿（Payload 那张表是原型的私产，
            // 印进检查点快照就等于让模块替原型保管它会不会变 —— D31、D32）。
            sb.Append(id).Append(' ').Append(b.BlueprintId)
                .Append(b.Anchor is { } anchor ? $" anchor={anchor}" : $" pos={ConstructionNumbers.Point(b.Position)}")
                .Append(" hp=").Append(ConstructionNumbers.Format(b.Health))
                .Append('/').Append(ConstructionNumbers.Format(b.MaxHealth))
                .Append(" state=").Append(b.Progress.StateText)
                .Append(" left=").Append(ConstructionNumbers.Format(b.RemainingTicks))
                .Append(" paused=").Append(b.Paused ? "1" : "0")
                .Append(" dir=").Append(ConstructionNumbers.Format(b.Orientation))
                .Append('\n');
        }

        return sb.ToString();
    }

    public override string ToString()
        => $"construction {_rule.RuleLabel} buildings={ConstructionNumbers.Format(Count)}";
}
