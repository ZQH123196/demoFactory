#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.MistCity.Logic;

namespace DemoFactory.Prototypes.MistCity;

/// <summary>
/// 牌桌的呈现 —— <b>一张牌 = 一张卡：圆角、描边、投影、顶部条槽</b>。
///
/// <para><b>为什么走 2D 卡牌而不是 3D 真模型</b>：spec「Out of Scope」写着造型线本轮不要求，
/// 「牌用什么呈现还没定」记在待确认区。这张 ticket 把呈现方式<b>定成卡牌外观</b>，
/// 不碰真模型那条线（契约「真模型」针对造型，牌走不走那条线还没定）。
/// 读数口在牌面上直接看得见 —— 挂在谁身上、还差几步产出、住满没有。</para>
///
/// <para><b>牌面上那一格读什么数，一律读世界给的数</b>：产线读绑定那一份，
/// 房屋读生人那一份，本件一个数都不自己算（「表现层只读」）。
/// 玩家点选走 <c>Main</c> 那几个世界方法，不在本类里另写一套。</para>
///
/// <para><b>牌位是自由平面</b>：牌面那个矩形的位置就是它在桌面上的位置，
/// 没有格子号，也没有「加了新牌就多一格」那件事。<b>位置与叠放次序都是表现层数据，
/// 逻辑层不持有</b>（绑定两端是引用，不是坐标 —— ADR 0017）。</para>
/// </summary>
public sealed partial class CardTable
{
    /// <summary>一张牌在画面上的那一格。</summary>
    public sealed class CardView
    {
        /// <summary>牌 id（与逻辑层同一个 id，点选与读数都靠它）。</summary>
        public required string Id { get; init; }

        /// <summary>牌种。</summary>
        public required CardKind Kind { get; init; }

        /// <summary>牌面那一块矩形。</summary>
        public required Control Face { get; init; }

        /// <summary>牌面上的牌名。</summary>
        public required Label Name { get; init; }

        /// <summary>牌面上那一行「压在谁身上 / 被压几张」。</summary>
        public required Label Link { get; init; }

        /// <summary>牌面上那一行产出间隔与累计产出。</summary>
        public required Label Detail { get; init; }

        /// <summary>顶部条槽里那一条 —— <b>宽度是这一格唯一会动的地方</b>。</summary>
        public required Control Bar { get; init; }
    }

    /// <summary>牌面宽度（像素）。</summary>
    private const float CardWidth = 148f;

    /// <summary>牌面高度（像素）。</summary>
    private const float CardHeight = 176f;

    /// <summary>桌面左边留的白（像素）。</summary>
    private const float TableLeft = 24f;

    /// <summary>桌面顶边留的白（像素）—— 顶栏那三行字占了上面。</summary>
    private const float TableTop = 110f;

    /// <summary>
    /// 初始摆位时一行摊几张。<b>这一格只管开局那一次</b>：
    /// 之后位置由人拖，一张牌摆在哪与它排第几无关。
    /// </summary>
    private const int FitPerRow = 5;

    /// <summary>初始摆位的基准间距（像素）—— 比牌面大，两张之间留一道缝。</summary>
    private static readonly Vector2 BasePitch = new(CardWidth + 28f, CardHeight + 20f);

    /// <summary>一张牌在自己那一格里最多挪多少（像素）—— 比间距小，挪完也不压着别张。</summary>
    private const float JitterSpan = 20f;

    /// <summary>下一排相对上一排往右挪多少（像素）—— 错开摆开，像一桌牌而不像一排格子。</summary>
    private const float RowStepX = 37f;

    // ---- 卡牌外观：顶部条槽（进度条住在里面）----

    /// <summary>条槽离牌面左右边多少（像素）。</summary>
    private const float SlotMarginX = 12f;

    /// <summary>条槽离牌面顶边多少（像素）。</summary>
    private const float SlotTop = 12f;

    /// <summary>条槽多高（像素）。</summary>
    private const float SlotHeight = 14f;

    /// <summary>槽里那一条白边离槽边多少（像素）。</summary>
    private const float SlotInset = 3f;

    /// <summary>槽里那一条最长有多宽（像素）—— 走满一格时的宽度。</summary>
    private static float SlotInnerWidth => CardWidth - ((2f * SlotMarginX) + (2f * SlotInset));

    /// <summary>槽里那一条多高（像素）。</summary>
    private static float SlotInnerHeight => SlotHeight - (2f * SlotInset);

    /// <summary>条槽底色 —— 压暗一档，白条压在上面看得清。</summary>
    private static readonly Color SlotBase = new(0f, 0f, 0f, 0.42f);

    /// <summary>槽边那一圈线 —— 比牌面描边亮一档，看得出这是一条槽不是一块色。</summary>
    private static readonly Color SlotEdge = new(0f, 0f, 0f, 0.6f);

    /// <summary>条的颜色 —— 近白。<b>形状靠长度给，精确值靠牌面那一行字</b>。</summary>
    private static readonly Color BarFill = new(0.96f, 0.95f, 0.88f, 0.96f);

    /// <summary>牌面四周留的白（像素）—— 文字区都用它，别让字贴到描边上。</summary>
    private const float TextInset = 8f;

    /// <summary>读数块离牌面顶边多少（像素）—— 它落在牌面下半，与叠放露出的那一段对齐。</summary>
    private const float DetailTop = 104f;

    /// <summary>
    /// 顶栏那三行的层号 —— <b>比牌高</b>。
    ///
    /// <para><b>这一格是刻意抬的</b>：牌一摞叠高两三张就会顶到顶栏那块，
    /// 盖住暂停状态与牌位那一行 —— 而那一行正是玩家判断「我还能不能再拖」的地方。
    /// 靠建节点的次序不够稳（牌那一层的兄弟次序会被 <c>ApplyZOrder</c> 刷），
    /// 所以用层号把这件事钉死。</para>
    /// </summary>
    private const int HudZ = 10;

    /// <summary>
    /// 弃牌堆那一块在桌面上的位置（视口坐标）—— <b>左下角</b>（spec「拖到画面左下角弃牌堆」）。
    ///
    /// <para><b>位置与判据只有这一处</b>：画面上那一块画在哪儿（<c>BuildDiscardPile</c>）
    /// 与「这一点算不算丢牌」（<see cref="IsOnDiscard"/>）读的是同一个矩形，
    /// 所以玩家拖放与 <c>drag_to onto=discard</c> 答的是同一个问题。</para>
    /// </summary>
    private static readonly Rect2 DiscardZone = new(24f, 700f, 216f, 144f);

    /// <summary>这一点是不是落在弃牌堆那一块里 —— <b>丢牌的判据只有这一处</b>。</summary>
    /// <param name="at">视口坐标。</param>
    public bool IsOnDiscard(Vector2 at) => DiscardZone.HasPoint(at);

    /// <summary>弃牌堆那一块（注入解析落点用它的中心）—— 读取那一块本身的那一口。</summary>
    public Rect2 DiscardRect => DiscardZone;

    /// <summary>牌按牌种上色 —— <b>颜色是认牌的一个提示，不是唯一线索</b>。</summary>
    /// <remarks>
    /// 三种房屋牌走同一个色系、按等级逐级压暗：它们是一条升级链，
    /// 一眼能看出「这一摞是同一族，而这一张比那张大一级」。
    /// 色系与前六种（柴的棕、石的灰、泉眼的蓝、田垄的绿）分得开。
    /// 缺一项的话 <see cref="Build"/> 里 <c>Tint[kind]</c> 当场抛。
    /// </remarks>
    private static readonly Dictionary<CardKind, Color> Tint = new()
    {
        [CardKind.Wood] = new(0.62f, 0.44f, 0.24f),
        [CardKind.Stone] = new(0.48f, 0.50f, 0.54f),
        [CardKind.Spring] = new(0.25f, 0.58f, 0.72f),
        [CardKind.Furrow] = new(0.42f, 0.58f, 0.28f),
        [CardKind.Kiln] = new(0.72f, 0.40f, 0.24f),
        [CardKind.DewVial] = new(0.52f, 0.34f, 0.66f),
        [CardKind.Shed] = new(0.76f, 0.68f, 0.38f),
        [CardKind.House] = new(0.62f, 0.56f, 0.32f),
        [CardKind.Granary] = new(0.46f, 0.42f, 0.26f),
    };

    /// <summary>弃牌堆底板的底色 —— <b>压暗一档，与牌面那九种底色都分得开</b>。</summary>
    private static readonly Color DiscardBase = new(0.26f, 0.17f, 0.18f);

    /// <summary>摊在弃牌堆里那几片牌背的底色。</summary>
    private static readonly Color DiscardChip = new(0.45f, 0.31f, 0.31f);

    /// <summary>弃牌堆里摊几片牌背 —— <b>只说「丢掉的牌堆在这儿」</b>，不印张数。</summary>
    private const int DiscardChips = 3;

    /// <summary>一片牌背多大（像素）。</summary>
    private static readonly Vector2 ChipSize = new(48f, 62f);

    /// <summary>
    /// 叠放次序，从底到顶 —— <b>显式数据，画面上谁压着谁由这一份决定</b>。
    /// 第 0 层是最上面那张（<see cref="TryPick"/> 从表尾往前数）。
    ///
    /// <para><b>为什么不能靠节点创建顺序</b>：那是引擎的兄弟次序，
    /// 谁先建谁先画。合成之后牌会重建节点、重建的先后与叠放无关，
    /// 拿它当出处，同一颗种子两次跑出来的拾取结果可能不一样。</para>
    ///
    /// <para><b>这是表现层数据，逻辑层不持有</b>：逻辑层不知道谁压着谁。</para>
    /// </summary>
    private readonly List<string> _zOrder = [];

    private readonly Dictionary<string, CardView> _views = new(StringComparer.Ordinal);
    private readonly Control _root;
    private readonly Control _cardLayer;
    private readonly Label _hint;
    private readonly Label _status;
    private readonly Label _gestureHint;

    /// <summary>已经摆过几张牌 —— 初始摆位按它算，所以同一次开局的落点逐字一致。</summary>
    private int _placed;

    /// <summary>
    /// 拿起那张牌放大多少 —— <b>固定的倍数</b>，浮起来的一眼能认出是「手里拿着」。
    /// </summary>
    private const float HeldScale = 1.08f;

    /// <summary>
    /// 叠上去之后向上偏移多少（像素）—— <b>固定值</b>。
    ///
    /// <para><b>这一格对着牌面读数那一行给</b>：卡面 176 高、读数块从 104 起，
    /// 偏移 84 之后底下那张露出来的正好是那一段。所以摞起来的每一张
    /// 都还读得到自己的数，层级看得清而读数不丢。</para>
    ///
    /// <para>叠放的次序在画面上看得见，而不只是靠节点次序
    /// （次序只有截图叠在一起才看得出差别）。</para>
    /// </summary>
    private const float StackLift = 84f;

    /// <summary>正在被拿在手里的那张（null = 桌上没有拿起的东西）。</summary>
    private string? _held;

    /// <summary>拿起时按下的那个点与牌面左上角的差 —— 跟着光标走时手不跳。</summary>
    private Vector2 _grabOffset;

    /// <summary>建那张牌桌（挂在 HUD 那个 <c>Control</c> 下）。</summary>
    ///
    /// <para><b>画面上从下到上：弃牌堆 → 牌 → 顶栏那三行</b>，与下面建节点的次序一致。
    /// 牌压在弃牌堆上面，玩家看得清自己正往哪一块丢；
    /// 顶栏压在牌上面，一摞牌叠高了也不会把暂停状态与牌位那一行盖住 ——
    /// 而那一行正是玩家判断「我还能不能再拖」的地方。</para>
    ///
    /// <param name="layer">HUD 的顶层 <c>CanvasLayer</c>。</param>
    public CardTable(CanvasLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        _root = new Control
        {
            Name = "CardTable",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        layer.AddChild(_root);

        // 弃牌堆先建、牌后建：牌压在它上面，玩家看得清自己正往哪一块丢。
        BuildDiscardPile();

        // 牌另挂一层：叠放次序要刷成「兄弟次序」，混在顶栏那几行字里排不出上下关系。
        _cardLayer = new Control
        {
            Name = "Cards",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        _root.AddChild(_cardLayer);

        _hint = new Label
        {
            Name = "Hint",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = HudZ,
            Position = new Vector2(16f, 12f),
            Text = Loc.T("把一张牌拖到另一张上：合成、派工或住进房屋"),
        };
        _hint.AddThemeFontSizeOverride("font_size", 20);
        _root.AddChild(_hint);

        _status = new Label
        {
            Name = "Status",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = HudZ,
            Position = new Vector2(16f, 44f),
            Text = "",
        };
        _status.AddThemeFontSizeOverride("font_size", 18);
        _root.AddChild(_status);

        // 拿下来与丢弃这两个手势另起一行，不并进上面那句总提示：
        // 那一句说的是「两张牌之间会发生什么」，这两句说的是「一张牌该去哪」，
        // 并成一行读起来要在一句里答两件事。
        _gestureHint = new Label
        {
            Name = "GestureHint",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = HudZ,
            Position = new Vector2(16f, 70f),
            Text = Loc.T("拖到空白处：把牌从产线上拿下来，不损失材料"),
        };
        _gestureHint.AddThemeFontSizeOverride("font_size", 16);
        _root.AddChild(_gestureHint);
    }

    /// <summary>
    /// 某张牌在屏幕上占的那个矩形 —— <b>拾取读这一份</b>，不在别处再算一遍位置。
    ///
    /// <para>牌不在画面上（例如刚被合成消耗掉还没刷）时回 false。</para>
    /// </summary>
    /// <param name="cardId">牌 id。</param>
    /// <param name="rect">那个矩形（视口坐标）。</param>
    public bool TryRectOf(string cardId, out Rect2 rect)
    {
        if (!_views.TryGetValue(cardId, out var view))
        {
            rect = default;
            return false;
        }

        rect = new Rect2(view.Face.Position, view.Face.Size);
        return true;
    }

    /// <summary>
    /// 把一张牌挪到桌面上的一个位置 —— <b>位置自由，没有格子</b>。
    ///
    /// <para>拖动与注入解析落点都走这一格，位置在桌面上的出处只有这里。</para>
    /// </summary>
    /// <param name="cardId">牌 id。</param>
    /// <param name="at">新位置（视口坐标）。</param>
    public void MoveTo(string cardId, Vector2 at)
    {
        if (_views.TryGetValue(cardId, out var view)) view.Face.Position = at;
    }

    /// <summary>
    /// 拿起一张牌 —— <b>浮起来：放大一档 + 一圈投影</b>，并记下抓取点。
    ///
    /// <para>拿起的那张提到最上面：拿着的东西压在所有牌之上，画面上才看得出
    /// 「手里有东西」。</para>
    ///
    /// <para><b>这一格不做目标高亮也不变暗</b>：拖动过程不给任何提示，
    /// 合不合得上的信息由牌面自己说（玩家明确否掉过拖动时高亮那一格）。</para>
    /// </summary>
    /// <param name="cardId">拿起来的那张。</param>
    /// <param name="at">按下的那一点（视口坐标）。</param>
    public void HoldUp(string cardId, Vector2 at)
    {
        if (!_views.TryGetValue(cardId, out var view)) return;

        _held = cardId;
        _grabOffset = view.Face.Position - at;
        view.Face.Scale = new Vector2(HeldScale, HeldScale);
        view.Face.AddThemeStyleboxOverride("panel", HeldBoxOf(Tint[view.Kind]));
        Raise(cardId);
    }

    /// <summary>
    /// 手里那张跟着光标走 —— 落点按抓取点算，手按在哪就还按在哪。
    /// </summary>
    /// <param name="at">光标现在在哪（视口坐标）。</param>
    public void MoveHeld(Vector2 at)
    {
        if (_held is not null) MoveTo(_held, at + _grabOffset);
    }

    /// <summary>
    /// 放下 —— <b>落在另一张牌上就与它对齐，再向上偏移一个固定值</b>，露出底牌一角。
    /// 落在空处就停在放下的那个位置（拿下来与丢弃由逻辑层的落点判定管，不在本件）。
    ///
    /// <para><b>对齐而不是照抓取点摆</b>：照抓取点摆的话，落点落在底牌哪一处，
    /// 塔就歪在哪一处，第二张第三张各歪各的，摞起来看得出是几张但看不出谁压谁。
    /// 对齐之后塔是一摞，每张只差一个固定的上移。</para>
    /// </summary>
    /// <param name="stacked">落点下面压着别的牌吗（点一下不算，所以由调用点判）。</param>
    /// <param name="ontoId">底下那张（null = 没叠着谁）。</param>
    /// <returns>这一摞的位置 —— 合成之后新牌落在这里（不带那一个上移）。</returns>
    public Vector2 PutDown(bool stacked, string? ontoId)
    {
        if (_held is null || !_views.TryGetValue(_held, out var view)) return default;
        if (stacked && ontoId is not null && _views.TryGetValue(ontoId, out var under))
        {
            view.Face.Position = under.Face.Position;
            view.Face.Position += new Vector2(0f, -StackLift);
        }

        view.Face.Scale = Vector2.One;
        view.Face.AddThemeStyleboxOverride("panel", BoxOf(Tint[view.Kind]));
        var at = view.Face.Position;
        _held = null;
        return at + new Vector2(0f, StackLift);
    }

    /// <summary>一张牌的屏幕中心 —— 注入解析落点坐标的出处（坐标不在调用点写死）。</summary>
    /// <param name="cardId">牌 id。</param>
    public Vector2 RectCenterOf(string cardId)
        => TryRectOf(cardId, out var rect) ? rect.GetCenter() : Vector2.Zero;

    /// <summary>画面上在册的牌 id —— 报错消息里列得出桌上有哪些牌。</summary>
    public IReadOnlyList<string> OnScreenIds => _zOrder;

    /// <summary>
    /// 把一张牌提到叠放次序的最上面 —— <b>提升入口</b>。
    ///
    /// <para>叠放关系建立时调它：上面那张提上来，第 0 下点到的就是它。
    /// 拖放那一手接这个入口。</para>
    /// </summary>
    /// <param name="cardId">牌 id。不在册就不动次序。</param>
    public void Raise(string cardId)
    {
        if (!_zOrder.Remove(cardId)) return;
        _zOrder.Add(cardId);
        ApplyZOrder();
    }

    /// <summary>
    /// 视口坐标 → 命中矩形内第 <paramref name="layer"/> 层的那张牌（第 0 层是最上面那张）。
    ///
    /// <para><b>从最上面往下数</b>：命中的第一张是第 0 层，再下一张是第 1 层，以此类推。
    /// 自由平面上两张牌压在一起时，离光标最近的那张未必是最上面那张，所以不按距离取。</para>
    ///
    /// <para><b>排除值那一格先跳过去</b>，它不参与计层 ——
    /// 正在被拖的那张不排除的话，原地放下会拾到自己。</para>
    /// </summary>
    /// <param name="at">视口坐标。</param>
    /// <param name="layer">往下数第几层，从 0 起。</param>
    /// <param name="exclude">排除的牌 id（null = 不排除）。</param>
    /// <param name="cardId">拾到的那张。</param>
    public bool TryPick(Vector2 at, int layer, string? exclude, out string cardId)
    {
        cardId = string.Empty;
        if (layer < 0) return false;

        var found = 0;
        for (var i = _zOrder.Count - 1; i >= 0; i--)
        {
            var id = _zOrder[i];
            if (exclude is not null && string.Equals(id, exclude, StringComparison.Ordinal)) continue;
            if (!TryRectOf(id, out var rect)) continue;
            if (!rect.HasPoint(at)) continue;

            if (found == layer)
            {
                cardId = id;
                return true;
            }

            found++;
        }

        return false;
    }

    /// <summary>把叠放次序刷成节点的兄弟次序 —— 画面上的上下关系就按这一份排。</summary>
    private void ApplyZOrder()
    {
        for (var i = 0; i < _zOrder.Count; i++)
            if (_views.TryGetValue(_zOrder[i], out var view))
                _cardLayer.MoveChild(view.Face, i);
    }

    /// <summary>
    /// 把牌桌清成开局的样子 —— <b>重开一局时调它</b>。
    ///
    /// <para><b>为什么要有这一格</b>：位置与叠放次序是表现层自己攒出来的，
    /// 不清掉的话同一颗种子重开一局，牌还停在上一次被人挪到的位置，
    /// 「同一颗种子重跑逐字一致」那条就在同一个进程里也验不成。</para>
    /// </summary>
    public void Reset()
    {
        foreach (var view in _views.Values) view.Face.QueueFree();
        _views.Clear();
        _zOrder.Clear();
        _placed = 0;
        _held = null;
    }

    /// <summary>
    /// 按世界快照刷画面 —— <b>只读，不改任何逻辑状态</b>（「表现层只读」）。
    ///
    /// <para>牌种变了（新合成出来的那张）要重建那一格，所以按 id 逐张对：
    /// 有就刷文字，没有就建，多余的摘掉。</para>
    /// </summary>
    /// <param name="world">逻辑世界。</param>
    public void Sync(MistCityWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        foreach (var id in world.Cards.Ids)
        {
            var state = world.CardStateOf(id);
            if (state is null) continue;

            if (!_views.TryGetValue(id, out var view) || view.Kind != state.Kind)
            {
                // 牌种变了（合成出来的新牌）：拆掉旧那一格重建，位置沿用原来那个。
                // 旧那一格要显式交还 —— 留着它就有一块矩形永远停在屏幕上，
                // 而叠放次序按兄弟次序排，那块多出来的矩形会把次序排歪。
                var keepAt = view?.Face.Position;
                if (view is not null) view.Face.QueueFree();
                _views.Remove(id);
                view = Build(id, state.Kind, keepAt);
                _views[id] = view;
            }

            // 叠放次序：后登记的更靠上。这一份次序的出处是牌册登记次序，不是节点创建顺序。
            if (!_zOrder.Contains(id)) _zOrder.Add(id);

            // 绑在它身上的那条：印在压上去的那一张上 —— 它是这一摞里最上面那张。
            // 印成「压在谁身上」而不是「压着谁」：后者是被压的那张在报谁压着它，
            // 而一张牌能被多张压，那一句报不出「谁在我上面」。
            var binding = world.Bindings.Find(id);
            var label = Loc.T(CardCatalog.Of(state.Kind).Label);
            var resident = binding is { Kind: BindingKind.Resident };

            view.Name.Text = label;

            // 压在它身上的有几张也印一格：一张牌下面能叠多张，
            // 只看上面那张印的「压在谁身上」，底下压了几张就数不出来。
            var covered = world.Bindings.CardOnto(id);
            view.Link.Text = binding is not null
                ? Loc.Fmt("压在 {0} 上", binding.OntoId)
                : covered.Count > 0 ? Loc.Fmt("被压 {0} 张", covered.Count) : string.Empty;

            // 房屋牌自己印住客数与生人倒计时，其余牌印产出读数。
            // 没绑定的不产出，两种牌面别混成同一行。
            view.Detail.Text = DetailOf(world, id, state, binding, resident);

            // 顶部那条：走「离下一次产出还差几步」。
            // 产线读绑定那一份，房屋读生人那一份；没在产的那张留空槽。
            view.Bar.Size = new Vector2(SlotInnerWidth * FilledOf(world, id, state, binding), SlotInnerHeight);

            // 快产线的牌面提亮一档：画面上直接看得出哪条更急。
            // 住户不产食物，所以不跟着产线那一档提亮。
            view.Face.Modulate = binding is null || resident
                ? Colors.White
                : (binding.IntervalSteps <= 3 ? new Color(1.25f, 1.15f, 0.85f) : Colors.White);
        }

        // 摘掉已经不在册的那些格（合成消耗掉之后牌面上的那一格要跟着消失）。
        var live = new HashSet<string>(world.Cards.Ids, StringComparer.Ordinal);
        foreach (var id in new List<string>(_views.Keys))
        {
            if (live.Contains(id)) continue;
            _views[id].Face.QueueFree();
            _views.Remove(id);
        }

        _zOrder.RemoveAll(id => !live.Contains(id));
        ApplyZOrder();
        SyncStatus(world);
        RefreshHeld();
    }


    /// <summary>
    /// 牌面那一行读数 —— <b>房屋牌印住客与生人倒计时，其余牌印产出读数</b>。
    ///
    /// <para><b>房屋牌印「住 1/2」是这道门</b>：「满 2 人生人」是玩家要过的门，
    /// 不印住客数就看不出还差几个，顶栏与快照替代不了这一格。</para>
    ///
    /// <para><b>房屋牌面上不印累计出生数</b>：那一格用户否掉了，生人由顶栏承担。</para>
    ///
    /// <para><b>派工的那张印间隔、还差几步与累计</b>（玩家故事 3 要的就是那一格）。
    /// <b>住进房屋的那一张印「住在哪」</b>：它不产食物，
    /// 印「0 步/次」只会让玩家以为它是一条跑不动的产线。</para>
    /// </summary>
    /// <param name="world">逻辑世界。</param>
    /// <param name="id">这一张的 id。</param>
    /// <param name="state">这一张的自有字段。</param>
    /// <param name="binding">挂在它身上的那条（没有是 null）。</param>
    /// <param name="resident">那一条是不是住户挂接。</param>
    private static string DetailOf(
        MistCityWorld world, string id, CardState state, Binding? binding, bool resident)
    {
        if (CardCatalog.IsHouse(state.Kind)) return HouseDetail(world, id, state);
        if (binding is null) return CardDetail(state);
        if (resident) return Loc.Fmt("住在 {0}", binding.OntoId);

        // 底下那张压着不止一张时补一行：摞起来的牌只有最上面那张看得见，
        // 「这一摞有几张」印在底下那张上等于没印，所以印在看得见的那张上。
        var onUnder = world.Bindings.CardOnto(binding.OntoId).Count;
        var note = onUnder > 1 ? Loc.Fmt("\n{0}上面共 {1} 张", binding.OntoId, onUnder) : string.Empty;
        return Loc.Fmt(
            "{0}步/次　{1}步后　已出{2}",
            MistCityWorld.Num(binding.IntervalSteps),
            MistCityWorld.Num(binding.RemainingSteps(world.Tick)),
            MistCityWorld.Num(binding.Produced)) + note;
    }

    /// <summary>
    /// 房屋牌那一格读数 —— <b>住客数 + 离下一次生人还差几步 + 每轮多留几张</b>。
    ///
    /// <para><b>没住满那一档写「住满后每 60 步生人」</b>：那时没有计时在跑，
    /// 写一个倒计时等于编一个数出来。写这一句玩家才知道住满之后要等多久。</para>
    ///
    /// <para><b>两行写在同一个键里</b>：牌面上每格文字都是单独一条 i18n 键，
    /// 两段拼起来要过一遍字符串拼接，而拼接后的样子只有运行时才知道。</para>
    /// </summary>
    private static string HouseDetail(MistCityWorld world, string id, CardState state)
    {
        var spec = CardCatalog.Of(state.Kind);
        var live = world.Bindings.ResidentsOf(id).Count;
        return world.BirthStepsLeftOf(id) is { } stepsLeft
            ? Loc.Fmt(
                "住 {0}/{1}　{2}步后生人\n每轮多留 {3} 张",
                MistCityWorld.Num(live), MistCityWorld.Num(spec.ResidentCap),
                MistCityWorld.Num(stepsLeft), MistCityWorld.Num(spec.KeepBonus))
            : Loc.Fmt(
                "住 {0}/{1}　住满后每 {2} 步生人\n每轮多留 {3} 张",
                MistCityWorld.Num(live), MistCityWorld.Num(spec.ResidentCap),
                MistCityWorld.Num(Rations.CycleSteps), MistCityWorld.Num(spec.KeepBonus));
    }

    /// <summary>
    /// 没有承载者的那张牌那一格读数 —— <b>产粮牌印间隔与累计，
    /// 腾下来的住户牌印它原来住在谁身上</b>。
    ///
    /// <para><b>被腾下来的住户牌印「曾住在 cXX」而不是产线那一行</b>：
    /// 它退回自由平面、不在产线上，印「几步/次」会让玩家以为它还在出货。
    /// 而「它原来住在哪间房」正是玩家要看的 —— 不印的话画面上只剩一张普通牌。</para>
    ///
    /// <para>牌面读什么数一律读牌种表与牌的自有字段，不在本件另算一遍（表现层只读）。</para>
    /// </summary>
    private static string CardDetail(CardState state)
    {
        var spec = CardCatalog.Of(state.Kind);
        if (state.LivedIn is not null)
            return Loc.Fmt("曾住在 {0}　已出{1}", state.LivedIn, MistCityWorld.Num(state.Produced));

        return Loc.Fmt(
            "{0}步/次　已出{1}",
            MistCityWorld.Num(spec.IntervalSteps),
            MistCityWorld.Num(state.Produced));
    }

    /// <summary>
    /// 顶部条槽上那一条该有多长 —— <b>走过的比例（0~1）</b>。
    ///
    /// <para><b>产线读绑定那一份</b>：间隔与剩余步数都是绑定表的数。
    /// <b>房屋读生人那一份</b>：没住满是 0，那时没有计时在跑。</para>
    ///
    /// <para><b>本件不拿当前步号自己减</b>：减出来的是另一个数
    /// （住满那刻的起点不是生人倒计时的起点）。</para>
    /// </summary>
    private static float FilledOf(MistCityWorld world, string id, CardState state, Binding? binding)
    {
        if (binding is { Kind: BindingKind.Production })
            return Walked(binding.RemainingSteps(world.Tick), binding.IntervalSteps);

        return CardCatalog.IsHouse(state.Kind) && world.BirthStepsLeftOf(id) is { } stepsLeft
            ? Walked(stepsLeft, Rations.CycleSteps)
            : 0f;
    }

    /// <summary>还差 <paramref name="remaining"/> 步到点、周期 <paramref name="cycle"/> 步 —— 走过的比例。</summary>
    private static float Walked(long remaining, long cycle)
        => cycle < 1 ? 0f : Math.Clamp(1f - ((float)remaining / cycle), 0f, 1f);

    /// <summary>
    /// 顶栏那几格：步号、食物、欠账、离结算几步、在世几个人、
    /// 牌位（用了几个 / 上限几个）、暂停状态、谁死了。
    /// </summary>
    private void SyncStatus(MistCityWorld world)
    {
        var food = world.Food;
        var toSettle = Rations.CycleSteps - (world.Tick % Rations.CycleSteps);
        var died = food.LastSettlement?.DiedIds ?? Array.Empty<string>();

        // 死的是谁不写人数：玩家故事 12 要的是「我的调整有对象」，几个人死了不告诉他对象是谁。
        var note = died.Count > 0 ? Loc.Fmt("　上一轮死了 {0}", string.Join(",", died)) : string.Empty;
        _status.Text = Loc.Fmt(
            "第 {0} 步　食物 {1}　欠 {2}　离结算 {3} 步　在世 {4} 人　牌位 {5}/{6}　{7}",
            MistCityWorld.Num(world.Tick),
            MistCityWorld.Num(food.Food),
            MistCityWorld.Num(food.TotalOwe),
            MistCityWorld.Num(toSettle),
            MistCityWorld.Num(food.AliveCount),
            MistCityWorld.Num(world.Cards.Count),
            MistCityWorld.Num(world.KeepLimit),
            Loc.T(world.IsPaused ? "已暂停" : "运行中")) + note;
    }

    /// <summary>
    /// 手里拿着的那张重刷一次外观 —— <b>这一格只管手里拿着的那一张</b>。
    ///
    /// <para><b>每刷一次都要重刷它</b>：<see cref="Sync"/> 按绑定关系给牌面调亮，
    /// 那一档只对坐在产线上的牌成立。手里拿着的那张正是在产线上的那一张，
    /// 跟着调亮之后玩家认不出自己拿着的是哪张。</para>
    /// </summary>
    private void RefreshHeld()
    {
        if (_held is null || !_views.TryGetValue(_held, out var view)) return;
        view.Face.Modulate = Colors.White;
        view.Face.Scale = new Vector2(HeldScale, HeldScale);
    }

    /// <summary>
    /// 建弃牌堆那一块 —— <b>一块底板 + 几片摊开的牌背 + 两行字</b>。
    ///
    /// <para><b>这一格只画样子，不动数据</b>：丢不丢牌由逻辑层落点判定答
    /// （<see cref="IsOnDiscard"/> 就是这一块矩形），本件不登记任何回收路，
    /// 所以画面上找不到「从弃牌堆里把牌拿出来」的那条路。</para>
    ///
    /// <para><b>字上不写「满了就放这里」</b>：轮末之前桌上张数可以超过保留上限，
    /// 超出是在结算那一刻才被处置（上限由后面那张 ticket立）。这一块是那次处置的执行口，
    /// 不是「放不下了的出路」，写错了玩家会为了腾位置提前丢牌。</para>
    ///
    /// <para><b>不印丢了几张</b>：那是一个调试读数，不是玩家要读的一格。</para>
    /// </summary>
    private void BuildDiscardPile()
    {
        var panel = new Panel
        {
            Name = "Discard",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = DiscardZone.Position,
            Size = DiscardZone.Size,
        };
        panel.AddThemeStyleboxOverride("panel", BoxOf(DiscardBase));
        _root.AddChild(panel);

        var title = new Label
        {
            Name = "Title",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(0f, 8f),
            Size = new Vector2(DiscardZone.Size.X, 26f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = Loc.T("弃牌堆"),
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        panel.AddChild(title);

        var note = new Label
        {
            Name = "Note",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(6f, 36f),
            Size = new Vector2(DiscardZone.Size.X - 12f, 34f),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = Loc.T("拖到这里丢掉，拿不回来"),
        };
        note.AddThemeFontSizeOverride("font_size", 13);
        panel.AddChild(note);

        // 几片牌背摊在那一块的下半：画面上一眼认得出这一处收牌，不靠读字。
        for (var i = 0; i < DiscardChips; i++)
        {
            var chip = new Panel
            {
                Name = $"Chip{i}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Size = ChipSize,
                Position = new Vector2(30f + (i * 50f), 76f + (i * 2f)),
            };
            chip.AddThemeStyleboxOverride("panel", BoxOf(DiscardChip));
            panel.AddChild(chip);
        }
    }

    /// <summary>
    /// 建一张牌的那一格 —— <b>卡面 + 顶部条槽 + 三行字</b>。
    /// <paramref name="at"/> 传 null 就按初始摆位算一个位置。
    ///
    /// <para><b>每一格文字的高度都给足</b>：牌面上印四段（中段还会折行），
    /// 高度按最长的那一张给 —— 窄一点就会出现「第三段被卡边截断」，
    /// 而截断在读数上查不出来，只在出图里看得见。</para>
    /// </summary>
    private CardView Build(string id, CardKind kind, Vector2? at)
    {
        var face = new Panel
        {
            Name = id,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(CardWidth, CardHeight),
            Size = new Vector2(CardWidth, CardHeight),
            Position = at ?? FreePositionOf(_placed++),
            Modulate = Colors.White,
        };
        face.AddThemeStyleboxOverride("panel", BoxOf(Tint[kind]));
        _cardLayer.AddChild(face);

        // 条槽先建，白条建在它里面 —— 槽是凹进去的一道，白条压在槽底上。
        var slot = new Panel
        {
            Name = "Slot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(SlotMarginX, SlotTop),
            Size = new Vector2(CardWidth - (2f * SlotMarginX), SlotHeight),
        };
        slot.AddThemeStyleboxOverride("panel", SlotBox());
        face.AddChild(slot);

        var bar = new Panel
        {
            Name = "Bar",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(SlotInset, SlotInset),
            Size = new Vector2(0f, SlotInnerHeight),
        };
        bar.AddThemeStyleboxOverride("panel", BarBox());
        slot.AddChild(bar);

        var name = new Label
        {
            Name = "Name",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(TextInset, 34f),
            Size = new Vector2(CardWidth - (2f * TextInset), 28f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        name.AddThemeFontSizeOverride("font_size", 19);
        face.AddChild(name);

        var link = new Label
        {
            Name = "Link",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(TextInset, 64f),
            Size = new Vector2(CardWidth - (2f * TextInset), 20f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        link.AddThemeFontSizeOverride("font_size", 13);
        face.AddChild(link);

        var detail = new Label
        {
            Name = "Detail",
            MouseFilter = Control.MouseFilterEnum.Ignore,

            // 这一行压到牌面下半：被压住的那张露出来的正是这一段，
            // 而它上面还要给压着它那张的投影留出位置，否则第一行会被削掉半截。
            Position = new Vector2(TextInset, DetailTop),
            Size = new Vector2(CardWidth - (2f * TextInset), CardHeight - DetailTop - TextInset),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        detail.AddThemeFontSizeOverride("font_size", 12);
        face.AddChild(detail);

        return new CardView
        {
            Id = id,
            Kind = kind,
            Face = face,
            Name = name,
            Link = link,
            Detail = detail,
            Bar = bar,
        };
    }

    /// <summary>
    /// 初始摆位 —— 一副打了散的阵列：每张按基准间距占一格，再按自己的序号挪一点，
    /// 每一排还相对上一排往右错开。所以开局一桌不重叠，而位置不是按格子排的。
    ///
    /// <para><b>挪动量由序号的整数散列算出，不是 <c>Random</c></b>：
    /// 表现层不留随机源（契约「粒子随机不进表现层」那条同口径），
    /// 同一颗种子重跑每张牌落回同一个位置，同一坐标的拾取读数逐字一致。</para>
    /// </summary>
    /// <param name="index">这一张是第几个建出来的（从 0 起）。</param>
    private static Vector2 FreePositionOf(int index)
    {
        var row = index / FitPerRow;
        var col = index % FitPerRow;
        return new Vector2(
            TableLeft + col * BasePitch.X + MathF.Min(row * RowStepX, 240f)
                + (UnitOf(index * 2 + 1) - 0.5f) * JitterSpan,
            TableTop + row * BasePitch.Y + (UnitOf(index * 2 + 2) - 0.5f) * JitterSpan);
    }

    /// <summary>整数散列取 [0, 1) 的那一格 —— 算初始摆位的定值偏移用，不是随机源。</summary>
    private static float UnitOf(int salt)
    {
        unchecked
        {
            var h = (uint)salt * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }

    /// <summary>
    /// 牌面底板 —— <b>圆角 + 描边 + 投影</b>，三样合起来才看得出这是一张卡。
    ///
    /// <para><b>投影挂在每一张牌上，不只挂在手里那张</b>：卡牌靠它与桌面分开，
    /// 摊在桌上时也是一张一张的，不是桌面上的几块色。
    /// 手里那张的投影由 <see cref="HeldBoxOf"/> 加深一档。</para>
    /// </summary>
    private static StyleBoxFlat BoxOf(Color fill)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            BorderColor = new Color(0.08f, 0.07f, 0.06f, 0.9f),
            AntiAliasing = true,
        };
        box.SetBorderWidthAll(2);
        box.ShadowColor = new Color(0f, 0f, 0f, 0.4f);
        box.ShadowSize = 8;
        box.ShadowOffset = new Vector2(3f, 5f);
        return box;
    }

    /// <summary>
    /// 拿在手里那张的底板 —— <b>同一份底板加一圈更深的投影</b>。
    ///
    /// <para><b>投影是底板自带的一档，不另外挂节点</b>：挂出去的节点要跟着位置与
    /// 缩放一起动，漏一次就飞在原地。</para>
    /// </summary>
    private static StyleBoxFlat HeldBoxOf(Color fill)
    {
        var box = BoxOf(fill);
        box.ShadowColor = new Color(0f, 0f, 0f, 0.55f);
        box.ShadowSize = 16;
        box.ShadowOffset = new Vector2(6f, 10f);
        return box;
    }

    /// <summary>条槽的底板 —— 凹进去的一道：压暗的底加一圈更深的边。</summary>
    private static StyleBoxFlat SlotBox()
    {
        var box = new StyleBoxFlat
        {
            BgColor = SlotBase,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            BorderColor = SlotEdge,
            AntiAliasing = true,
        };
        box.SetBorderWidthAll(1);
        return box;
    }

    /// <summary>白条的底板 —— 近白，只有圆角与边长两样要定。</summary>
    private static StyleBoxFlat BarBox()
    {
        var box = new StyleBoxFlat
        {
            BgColor = BarFill,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            AntiAliasing = true,
        };
        return box;
    }
}
