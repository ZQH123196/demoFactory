#nullable enable
using System;
using System.Globalization;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 上帝之手 —— 抓起镇民、尸体与散货，跟手，三档落点（spec §5）。
///
/// <para><b>状态机三档</b>：<c>Idle</c> → <c>Candidate</c>（左键按在可拖的那一件身上、位移还没过
/// <see cref="InputRouter.DragThresholdPixels"/>）→ <c>Dragging</c>（过阈值、<c>GrabEntity</c> 已发成）。
/// 候选阶段<b>一个命令都不发</b>：没过阈值就松手是点选，由 <c>Main.OnWorldClicked</c> 接，
/// 所以点一下人不会清岗位（<c>GrabEntity</c> 里那一发 <c>ClearPost</c> 也就不会发生）。</para>
///
/// <para><b>跟手只动表现</b>：拖拽期间每帧把那一件的场景节点挪到光标的地面点
/// （<see cref="Picker.TryGroundPoint"/> → <see cref="DeckView.LevitationNode"/> 的 <c>ToLocal</c>），
/// 不写世界的任何字段。逻辑位置仍由 <c>GrabEntity</c> 那一发决定；松手后
/// <c>Main.Inject</c> 会对成功与失败都刷一次 <c>DeckView.Sync</c>，位置以世界读数为准 ——
/// 节点的最终去处不归本类摆。</para>
///
/// <para><b>落点三档</b>（松手时依次判，先判界面）：
/// <list type="number">
/// <item>鼠标压在任何 <see cref="Control"/> 上（<c>GuiGetHoveredControl</c> 非空）→ 取消，原地放回。</item>
/// <item>甲板格上有建筑 → <c>DropToBuilding</c>（那一件 | 建筑 id）。</item>
/// <item>甲板范围内的空地 → <c>DropToDeck</c>（那一件 | 甲板局部 x | 甲板局部 z）。
/// x / z 走 <c>ToLocal</c>，不手工减原点 —— 悬浮层带偏航，减原点只在没转过时才对。</item>
/// </list>
/// 甲板外（空地 / 天边）按「取消」处理，同样放回原位：不能因为拖到屏幕边缘就把那一件从世界外丢掉。</para>
///
/// <para><b>可抓的三类</b>：镇民、尸体、散货。活人那一档先判
/// <see cref="ArenaUnit.IsTownsperson"/>（帮工、游客、敌人连候选都不进）；
/// 尸体与散货那一档由逻辑层的 <c>GrabEntity</c> 另有一道同样的守卫，两条路都拦住空发。</para>
/// </summary>
public partial class DragController : Node
{
    private readonly InputRouter _input;
    private readonly Picker _picker;
    private readonly DeckView _deck;
    private readonly Func<ArenaWorld?> _worldOf;
    private readonly Func<string, string, string> _inject;
    private readonly Func<bool> _blocked;

    /// <summary>按下时压在谁身上（空串 = 这一下没压到可拖的那一类）。</summary>
    private string _candidateId = string.Empty;

    /// <summary>按下时那一件的类别（<see cref="PickTarget.Empty"/> = 没有候选）。</summary>
    private PickTarget _candidateKind = PickTarget.Empty;

    /// <summary>已经抓起来、正在跟着光标走的那一件（空串 = 没在拖）。</summary>
    private string _draggedId = string.Empty;

    /// <summary>拖拽中那一件的类别（<see cref="PickTarget.Empty"/> = 没在拖）。</summary>
    private PickTarget _draggedKind = PickTarget.Empty;

    public DragController(
        InputRouter input,
        Picker picker,
        DeckView deck,
        Func<ArenaWorld?> worldOf,
        Func<string, string, string> inject,
        Func<bool> blocked)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _deck = deck ?? throw new ArgumentNullException(nameof(deck));
        _worldOf = worldOf ?? throw new ArgumentNullException(nameof(worldOf));
        _inject = inject ?? throw new ArgumentNullException(nameof(inject));
        _blocked = blocked ?? throw new ArgumentNullException(nameof(blocked));
        Name = "DragController";
    }

    /// <summary>当前手指缝里夹着谁（调试桥与排障用；空串 = 没抓人）。</summary>
    public string DraggedId => _draggedId;

    /// <summary>按下时选中的候选（没过阈值前只记 id，不发命令）。</summary>
    public string CandidateId => _candidateId;

    public override void _Ready()
    {
        _input.WorldPressed += OnWorldPressed;
        _input.DragStarted += OnDragStarted;
        _input.DragMoved += OnDragMoved;
        _input.DragEnded += OnDragEnded;
        _input.DragCancelled += OnDragCancelled;
    }

    public override void _ExitTree()
    {
        _input.WorldPressed -= OnWorldPressed;
        _input.DragStarted -= OnDragStarted;
        _input.DragMoved -= OnDragMoved;
        _input.DragEnded -= OnDragEnded;
        _input.DragCancelled -= OnDragCancelled;
    }

    /// <summary>换局时清掉抓取状态（旧世界的人已经不在册，不能再往下发命令）。</summary>
    public void Reset()
    {
        _candidateId = string.Empty;
        _candidateKind = PickTarget.Empty;
        _draggedId = string.Empty;
        _draggedKind = PickTarget.Empty;
    }

    public override void _Process(double delta)
    {
        if (_draggedId.Length == 0) return;
        Follow(_draggedId, GetViewport().GetMousePosition());
    }

    /// <summary>按下：只登记候选，不碰世界。</summary>
    private void OnWorldPressed(Vector2 screen)
    {
        _candidateId = string.Empty;
        _candidateKind = PickTarget.Empty;

        // 拖拽走「抓得住的那一类」那一口：地上躺着的东西要能抓，
        // 而它们不该抢走点选与右键那两条路要的建筑（点尸体别被脚下的建筑吃掉，反过来也一样）。
        var pick = _picker.PickDraggable(screen);
        switch (pick.Target)
        {
            case PickTarget.Person:
                // 只有镇民抓得起来：游客、敌人、帮工按下去不响应（spec §5 / 逻辑层 12）。
                var person = pick.Person;
                if (person is null || !person.IsTownsperson) return;
                _candidateId = person.Id;
                _candidateKind = PickTarget.Person;
                return;

            case PickTarget.Corpse:
            case PickTarget.Cargo:
                var item = pick.Item;
                if (item is null) return;
                _candidateId = item.Id;
                _candidateKind = pick.Target;
                return;
        }
    }

    /// <summary>过阈值：这一下才真的抓起来（命令只在这里发）。</summary>
    private void OnDragStarted(Vector2 screen)
    {
        var id = _candidateId;
        var kind = _candidateKind;
        _candidateId = string.Empty;
        _candidateKind = PickTarget.Empty;
        if (id.Length == 0) return;

        // 抓取失败（那一件已经被移出世界等）就不进入拖拽 —— 手上没东西时不应该跟手。
        if (_inject("GrabEntity", id) != "ok") return;

        _draggedId = id;
        _draggedKind = kind;
        Follow(id, screen);
    }

    /// <summary>跟手：只挪节点，不写世界。</summary>
    private void OnDragMoved(Vector2 screen)
    {
        if (_draggedId.Length == 0) return;
        Follow(_draggedId, screen);
    }

    /// <summary>松手：先判界面 / 模态，再判建筑 / 甲板格，最后按「取消」放回。</summary>
    private void OnDragEnded(Vector2 screen)
    {
        var id = _draggedId;
        var kind = _draggedKind;
        _draggedId = string.Empty;
        _draggedKind = PickTarget.Empty;
        _candidateId = string.Empty;
        _candidateKind = PickTarget.Empty;
        if (id.Length == 0) return;

        // 那一件已经不在世界上了（拖拽途中被搬走 / 被拆了）：没有落点可发。
        if (!InWorld(kind, id)) return;

        // ⓪ 按下之后才出现的模态（F1 / 自动战报）：这一发不算落点，原地放回。
        // InputRouter 在松手时也判过一次；两处都判，任一条路都不会把事件漏到世界上。
        if (_blocked())
        {
            ReturnToOrigin(kind, id);
            return;
        }

        // ① 界面控件上：取消。必须放在世界拾取之前 —— 蓝图条与甲板在同一块屏幕空间上。
        if (GetViewport().GuiGetHoveredControl() is not null)
        {
            ReturnToOrigin(kind, id);
            return;
        }

        // ② 甲板内：有建筑进楼，没建筑落格子（甲板局部坐标，不手工减原点）。
        if (!_picker.TryDeckLocal(screen, out var local))
        {
            ReturnToOrigin(kind, id);
            return;
        }

        var buildingId = _picker.BuildingIdAt(_picker.CellOfLocal(local));
        if (buildingId.Length > 0)
        {
            _inject("DropToBuilding", $"{id}|{buildingId}");
            return;
        }

        _inject("DropToDeck", $"{id}|{Number(local.X)}|{Number(local.Z)}");
    }

    /// <summary>拖拽途中被模态挡住：取消，原地放回（不发落点命令）。</summary>
    private void OnDragCancelled(Vector2 _)
    {
        var id = _draggedId;
        var kind = _draggedKind;
        _draggedId = string.Empty;
        _draggedKind = PickTarget.Empty;
        _candidateId = string.Empty;
        _candidateKind = PickTarget.Empty;
        if (id.Length == 0) return;

        if (InWorld(kind, id)) ReturnToOrigin(kind, id);
    }

    /// <summary>把节点挪到光标地面点（悬浮层局部系；抬起高度与 <c>DeckView.SyncUnits</c> 同一档）。</summary>
    private void Follow(string id, Vector2 screen)
    {
        if (!_deck.TryNodeOf(id, out var node)) return;
        if (!_picker.TryGroundPoint(screen, out var worldPoint)) return;

        var local = _deck.LevitationNode.ToLocal(worldPoint);
        node.Position = new Vector3(local.X, DeckView.TownY + DeckView.LiftHeight, local.Z);
    }

    /// <summary>
    /// 取消：按世界里的原位放回去。
    ///
    /// <para><b>人是就近找一格空地</b>（<c>DropToDeck</c> 那一档的老口径）；
    /// <b>点位物按原坐标落回</b> —— 落点那一格照写，落点一格不动。</para>
    /// </summary>
    private void ReturnToOrigin(PickTarget kind, string id)
    {
        var world = _worldOf();
        if (world is null) return;

        if (kind == PickTarget.Person)
        {
            var unit = world.Roster.Find(id);
            if (unit is null) return;
            _inject("DropToDeck", $"{id}|{Number((float)unit.DeckX)}|{Number((float)unit.DeckZ)}");
            return;
        }

        var item = world.Items.Find(id);
        if (item is null) return;
        _inject("DropToDeck", $"{id}|{Number((float)item.DeckX)}|{Number((float)item.DeckZ)}");
    }

    /// <summary>那一件还在不在世界里（活物读名册，点位物读点位物那一本）。</summary>
    private bool InWorld(PickTarget kind, string id)
    {
        var world = _worldOf();
        if (world is null) return false;

        return kind == PickTarget.Person
            ? world.Roster.Find(id) is not null
            : world.Items.Find(id) is not null;
    }

    /// <summary>甲板局部坐标按 <c>0.00</c> 上发（与 <c>ReplayArgs.Number</c> 的规范化同形）。</summary>
    private static string Number(float value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);
}
