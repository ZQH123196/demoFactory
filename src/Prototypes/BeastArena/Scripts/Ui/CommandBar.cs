#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 右上角命令栏 —— 八颗全局按钮：锁巨兽、停靠、拔锚、吞食、召回、经营、结算、暂停 / 倍速。
///
/// <para><b>独立右栏，最小宽 <see cref="UiRoot.CommandBarMinWidth"/></b>：与左上 HUD（宽 560）
/// 并排在默认 1152 宽下不重叠。锚点落在 <see cref="UiRoot.WorldLayer"/> 这块 <c>Control</c> 上
/// （<c>CanvasLayer</c> 不是 <c>Control</c>，「落地配出图」）。几颗按钮那一行按最小宽排布，
/// 放不下时按 Godot 的规矩由最小尺寸顶着走 —— 所以那一格宁可宽一点，也不要让按钮被挤出屏幕。</para>
///
/// <para><b>按钮状态每帧从世界读数派生，这里不存副本</b>（除弹出层开关）：停靠 / 拔锚的可点与文字
/// 由 <c>IsDocked</c>、<c>Voyage.Phase</c>、<c>InStandstill</c>、<c>Battle.Occasion</c> 现算；
/// 「经营」恒可点（市场 / 账本那两页自己按停靠期置灰）；「结算」只在终局结算生成之后亮；
/// 时间按钮的文字由 <c>IsPaused</c> 与当前倍率现算。世界换了、阶段变了，下一帧自己就对上。</para>
///
/// <para><b>写路径只有一条</b>：两个航行命令与召回走 <see cref="Main.Inject"/>（失败原因由它转给
/// <see cref="Banner"/>）；暂停 / 倍率按ticket 的口径走世界的 <c>Pause</c>/<c>Resume</c> 与
/// <c>Main.SetSpeed</c> —— 它们不是世界字段的写口。<b>吞食、经营、结算、巨兽那几件不自己写世界</b>：
/// 它们只把面板打开，面板里那一发才由面板走 <see cref="Main.Inject"/>。</para>
///
/// <para><b>停靠弹层</b>挂 <see cref="UiRoot.PopupLayer"/>，打开时置模态、关闭时清；
/// 每帧按命令栏的实际位置重新定位并往屏幕内收（窄窗口下不出屏）。
/// <b>经营、结算、吞食是各自的弹层</b>，不在这一个弹层里 —— 停靠弹层只管选目的地。</para>
/// </summary>
public partial class CommandBar : Control, IModalCloseable
{
    /// <summary>按钮高度（像素）。</summary>
    public const float ButtonHeight = 30f;

    /// <summary>按钮之间的间距（像素）。</summary>
    public const float ButtonGap = 6f;

    /// <summary>面板内边距（像素）。</summary>
    public const float PanelPadding = 6f;

    /// <summary>弹层与命令栏之间的间距（像素）。</summary>
    public const float PopupGap = 6f;

    /// <summary>倍率阶梯的顶端：到顶再点一下就暂停。</summary>
    public const double MaxSpeed = 8.0;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly Func<double> _speed;
    private readonly Action<double> _setSpeed;
    private readonly Action _lockBeast;
    private readonly Action _forage;
    private readonly Action _operations;
    private readonly Action _settlement;
    private readonly UiRoot _ui;

    private readonly Button _lock;
    private readonly Button _dock;
    private readonly Button _pull;
    private readonly Button _eat;
    private readonly Button _recall;
    private readonly Button _operationsButton;
    private readonly Button _settlementButton;
    private readonly Button _time;

    private readonly Control _popup;
    private readonly PanelContainer _popupBox;
    private readonly (string CityId, Button Button)[] _destinations;

    public CommandBar(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject,
        Func<double> speed,
        Action<double> setSpeed,
        Action lockBeast,
        Action forage,
        Action operations,
        Action settlement)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);
        ArgumentNullException.ThrowIfNull(speed);
        ArgumentNullException.ThrowIfNull(setSpeed);
        ArgumentNullException.ThrowIfNull(lockBeast);
        ArgumentNullException.ThrowIfNull(forage);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(settlement);

        _ui = ui;
        _world = world;
        _inject = inject;
        _speed = speed;
        _setSpeed = setSpeed;
        _lockBeast = lockBeast;
        _forage = forage;
        _operations = operations;
        _settlement = settlement;

        Name = "CommandBar";
        MouseFilter = MouseFilterEnum.Stop;

        // 右上角：右缘对齐屏幕边距，上缘与 HUD 首行齐（同一个 HudTopMargin）。
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -UiRoot.ScreenMargin - UiRoot.CommandBarMinWidth;
        OffsetRight = -UiRoot.ScreenMargin;
        OffsetTop = UiRoot.HudTopMargin;
        OffsetBottom = UiRoot.HudTopMargin + ButtonHeight + (PanelPadding * 2f);

        var panel = new PanelContainer { Name = "Box" };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.13f, 0.17f, 0.92f),
            BorderColor = new Color(0.45f, 0.50f, 0.60f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = PanelPadding,
            ContentMarginRight = PanelPadding,
            ContentMarginTop = PanelPadding,
            ContentMarginBottom = PanelPadding,
        });
        AddChild(panel);

        var row = new HBoxContainer { Name = "Buttons" };
        row.AddThemeConstantOverride("separation", (int)ButtonGap);
        panel.AddChild(row);

        // 「锁巨兽」放最左：它是视角那一档的命令（跟航行与时间不是一摊），
        // 离屏幕右缘最远，窄窗口下先挤掉的也不是它。
        _lock = MakeButton("LockBeast", Loc.T("锁巨兽"));
        _lock.Pressed += OnLockPressed;
        row.AddChild(_lock);

        _dock = MakeButton("Dock", Loc.T("停靠"));
        _dock.Pressed += OnDockPressed;
        row.AddChild(_dock);

        _pull = MakeButton("PullAnchor", Loc.T("拔锚"));
        _pull.Pressed += OnPullPressed;
        row.AddChild(_pull);

        // 生态那两颗挨着航行命令：一个开目标选择（吞谁由玩家点）、一个把觅食中的兽叫回来。
        _eat = MakeButton("EatTarget", Loc.T("吞食"));
        _eat.Pressed += OnEatPressed;
        row.AddChild(_eat);

        _recall = MakeButton("RecallForage", Loc.T("召回"));
        _recall.Pressed += OnRecallPressed;
        row.AddChild(_recall);

        // 「经营」把市场、账本与工资、人员与帮工并成一个入口（内部分栏）。
        // 它只开面板，不自己写世界；三页各自按停靠期置灰。
        _operationsButton = MakeButton("Operations", Loc.T("经营"));
        _operationsButton.Pressed += OnOperationsPressed;
        row.AddChild(_operationsButton);

        // 「结算」在终局结算生成之后才亮（最终防守胜利那一次）；继续经营之后仍可回看。
        _settlementButton = MakeButton("Settlement", Loc.T("结算"));
        _settlementButton.Pressed += OnSettlementPressed;
        row.AddChild(_settlementButton);

        _time = MakeButton("Time", Loc.T("▶ 继续"));
        _time.Pressed += OnTimePressed;
        row.AddChild(_time);

        // === 停靠弹层 ===

        _popup = new Control
        {
            Name = "DockPopup",
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        _popup.SetAnchorsPreset(LayoutPreset.FullRect);
        _popup.GuiInput += OnPopupInput;

        _popupBox = new PanelContainer
        {
            Name = "Box",
            CustomMinimumSize = new Vector2(UiRoot.CommandBarMinWidth, 0f),
        };
        _popupBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.11f, 0.14f, 0.98f),
            BorderColor = new Color(0.45f, 0.50f, 0.62f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 10f,
            ContentMarginRight = 10f,
            ContentMarginTop = 8f,
            ContentMarginBottom = 8f,
        });
        _popup.AddChild(_popupBox);

        var rows = new VBoxContainer { Name = "Rows" };
        rows.AddThemeConstantOverride("separation", (int)ButtonGap);
        _popupBox.AddChild(rows);

        rows.AddChild(new Label
        {
            Text = Loc.T("选择停靠点"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 四个目的地按 BigMapGrid.Places 的顺序（贫 → 野外 → 富 → 沙），不另排。
        _destinations = new (string, Button)[BigMapGrid.Places.Count];
        for (var i = 0; i < BigMapGrid.Places.Count; i++)
        {
            var place = BigMapGrid.Places[i];
            var button = MakeButton($"Destination{i}", Loc.T(place.Name));
            button.Pressed += () => OnDestinationPressed(place.Id);
            rows.AddChild(button);
            _destinations[i] = (place.Id, button);
        }

        ui.AddPopup(_popup);
        ui.WorldLayer.AddChild(this);

        UpdateButtons();
    }

    /// <summary>现在开着没有（弹出层开关；重置阶段判断不另存副本）。</summary>
    public bool IsPopupOpen => _popup.Visible;

    public override void _Process(double delta)
    {
        UpdateButtons();
        if (_popup.Visible) PlacePopup();
    }

    // === 四颗按钮 ===

    /// <summary>锁巨兽：焦点跟兽、距离进战略档，顺带解除钉住（视角那一档，不是世界的写口）。</summary>
    private void OnLockPressed() => _lockBeast();

    private void OnDockPressed()
    {
        if (!CanDock(_world())) return;
        _popup.Visible = true;
        _ui.EnterModal(this);
        PlacePopup();
    }

    private void OnPullPressed()
    {
        if (!CanPull(_world())) return;
        _inject("PullAnchor", string.Empty);
    }

    /// <summary>「吞食」：开出目标选择面板（目标由玩家点，不再自动取最近那一个）。</summary>
    private void OnEatPressed()
    {
        if (_world() is null) return;
        _forage();
    }

    /// <summary>召回：只在觅食途中有效（其余阶段按钮置灰，悬停写原因）。</summary>
    private void OnRecallPressed()
    {
        if (!CanRecall(_world())) return;
        _inject("RecallForage", string.Empty);
    }

    /// <summary>「经营」：按下只是把经营弹层打开（三页各自走 Inject）。</summary>
    private void OnOperationsPressed()
    {
        if (_world() is null) return;
        _operations();
    }

    /// <summary>「结算」：终局结算生成之后才亮；按下只开面板回看。</summary>
    private void OnSettlementPressed()
    {
        if (_world()?.Settlement is null) return;
        _settlement();
    }

    /// <summary>
    /// 暂停 → 1× → 2× → 4× → 8× → 暂停。倍率不在阶梯上（[ ] 键拧出来的 0.25 / 0.5 那几档）
    /// 就视作「下一档是 1×」—— 阶梯只认这五格，不另存一份倍率副本。
    /// </summary>
    private void OnTimePressed()
    {
        var world = _world();
        if (world is null) return;

        if (world.IsPaused)
        {
            world.Resume();
            _setSpeed(1.0);
            return;
        }

        var multiplier = _speed();
        if (multiplier >= MaxSpeed - 1e-9)
        {
            world.Pause();
            return;
        }

        if (IsNear(multiplier, 1.0)) _setSpeed(2.0);
        else if (IsNear(multiplier, 2.0)) _setSpeed(4.0);
        else if (IsNear(multiplier, 4.0)) _setSpeed(8.0);
        else _setSpeed(1.0);
    }

    private void OnDestinationPressed(string cityId)
    {
        var world = _world();
        if (!CanDock(world)) return;

        // 通缉拒停的判定收在 ArenaWorld.CanDockTo（与 Dock 动词同一处）；这里只读。
        if (!world!.CanDockTo(cityId)) return;

        ClosePopup();
        _inject("Dock", cityId);
    }

    // === 每帧派生 ===

    /// <summary>上一次刷按钮时的读数签名 —— 没变就不重写文字 / 不新建格式化串。</summary>
    private string _signature = string.Empty;

    private void UpdateButtons()
    {
        var world = _world();
        var multiplier = _speed();

        var signature = string.Format(CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3}|{4:0.#}|{5:0.##}|{6}|{7:0.##}|{8}|{9}",
            world is null ? "-" : world.IsDocked ? "dock" : InTransit(world) ? "transit" : "sail",
            CanDock(world), CanPull(world),
            world?.Battle.Occasion == BattleOccasion.Defence,
            world is null ? 0.0 : DefenceHoursLeft(world), multiplier,
            world?.Voyage.Phase.ToString() ?? "-",
            world?.Ecology.Satiation ?? 0.0,
            CanRecall(world),
            world?.Settlement is not null);

        if (signature != _signature)
        {
            _signature = signature;

            _dock.Text = world is null
                ? Loc.T("停靠")
                : world.IsDocked
                    ? Loc.T("已停靠")
                    : InTransit(world) ? Loc.T("进港中") : Loc.T("停靠");
            _dock.Disabled = !CanDock(world);

            // 拔锚防守期间拔锚按钮不可点，文字改成「还剩多久」—— 读数每帧现算，不存副本。
            // 78px 的按钮放不下「防守剩 1.8 游戏小时」，缩成「防守 1.8h」。
            // 吞食那一颗恒可点（只要有一局）：点不动的理由写在吞食面板顶上那一行 ——
            // 饱食已满 / 战斗中 / 停靠 / 无目标 / 冷却中都要玩家看得到。
            _eat.Disabled = world is null;
            _recall.Disabled = !CanRecall(world);
            _recall.TooltipText = CanRecall(world)
                ? Loc.T("把觅食中的巨兽叫回航线")
                : Loc.T("不在觅食途中（停靠 / 战斗 / 仪式期间也发不出去）");

            _pull.Text = world is not null && world.Battle.Occasion == BattleOccasion.Defence
                ? Loc.Fmt("防守 {0}h", DefenceHoursLeft(world).ToString("0.#", CultureInfo.InvariantCulture))
                : Loc.T("拔锚");
            _pull.Disabled = !CanPull(world);

            // 经营：三页里市场与账本自己按停靠期置灰，人员与帮工恒可用 —— 所以这颗恒可点。
            _operationsButton.Disabled = world is null;
            _operationsButton.TooltipText = world is null
                ? Loc.T("还没有一局在跑")
                : Loc.T("市场 · 账本与工资 · 人员与帮工");

            // 结算：只有终局结算生成之后才亮；继续经营之后仍可回看那一份旧结算。
            _settlementButton.Disabled = world?.Settlement is null;
            _settlementButton.TooltipText = world?.Settlement is null
                ? Loc.T("终局结算在最终防守胜利后生成")
                : Loc.T("回看终局结算");

            _time.Disabled = world is null;

            // 暂停态下点它是恢复，所以写「继续」而不是「暂停」。
            _time.Text = world?.IsPaused == true
                ? Loc.T("▶ 继续")
                : Loc.Fmt("{0}×", multiplier.ToString("0.##", CultureInfo.InvariantCulture));
        }

        if (!_popup.Visible) return;

        foreach (var (cityId, button) in _destinations)
            button.Disabled = world is null || !world.CanDockTo(cityId);

        // 世界换了 / 阶段走掉了：弹层自己收起来，不留在屏幕上点不动。
        if (!CanDock(world)) ClosePopup();
    }

    /// <summary>未停靠、不在进港途中、不在拔锚防守里。</summary>
    private static bool CanDock(ArenaWorld? world)
        => world is not null
           && !world.IsDocked
           && !InTransit(world)
           && world.Battle.Occasion != BattleOccasion.Defence;

    /// <summary>已停靠、未停摆、无战斗。</summary>
    private static bool CanPull(ArenaWorld? world)
        => world is not null
           && world.IsDocked
           && !world.InStandstill
           && world.Battle.Occasion == BattleOccasion.None;

    /// <summary>召回只在觅食状态里有效。</summary>
    private static bool CanRecall(ArenaWorld? world)
        => world is not null
           && world.Voyage.Phase is VoyagePhase.DetouringToFood or VoyagePhase.Eating;

    /// <summary>已经下了航行令、还没抵达那一段（含觅食、指定进食与返航）。</summary>
    private static bool InTransit(ArenaWorld world) => world.Voyage.Phase is
        VoyagePhase.Sailing or VoyagePhase.DetouringToFood
        or VoyagePhase.Eating or VoyagePhase.ManualEating or VoyagePhase.ReturningToWaypoint;

    /// <summary>
    /// 这一场防守还剩多少游戏小时（向上取到 0.1，不写步数）。
    /// 读的是战斗自己的收场步号 <see cref="BattleSim.EndTick"/> 与当前步号之差，不另存一份计时。
    /// </summary>
    private static double DefenceHoursLeft(ArenaWorld world)
    {
        var steps = Math.Max(0L, world.Battle.EndTick - world.Tick);
        return Math.Ceiling(steps * 10.0 / ArenaTime.StepsPerGameHour) / 10.0;
    }

    private static bool IsNear(double value, double target) => Math.Abs(value - target) < 1e-9;

    // === 弹层 ===

    /// <summary>收起停靠弹层并摘掉自己那一份模态。</summary>
    public void ClosePopup()
    {
        _popup.Visible = false;
        _ui.ExitModal(this);
    }

    /// <summary>Esc 逐层关闭时的收口（命令栏这一档只开着目的地弹层）。</summary>
    public void Close() => ClosePopup();

    /// <summary>点弹层外的空白处关掉它（弹层内的事件被子控件吃掉，到不了这里）。</summary>
    private void OnPopupInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            ClosePopup();
    }

    /// <summary>贴着命令栏下缘摆，四边收进屏幕内（窄窗口下整块往左 / 往上让）。</summary>
    private void PlacePopup()
    {
        _popupBox.ResetSize();

        var viewport = GetViewportRect().Size;
        var bar = GetGlobalRect();
        var size = _popupBox.Size;

        var maxX = Math.Max(UiRoot.ScreenMargin, viewport.X - size.X - UiRoot.ScreenMargin);
        var maxY = Math.Max(UiRoot.ScreenMargin, viewport.Y - size.Y - UiRoot.ScreenMargin);
        var x = Math.Clamp(bar.End.X - size.X, UiRoot.ScreenMargin, maxX);
        var y = Math.Clamp(bar.End.Y + PopupGap, UiRoot.ScreenMargin, maxY);

        _popupBox.Position = new Vector2(x, y);
    }

    private static Button MakeButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        CustomMinimumSize = new Vector2(0f, ButtonHeight),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        FocusMode = FocusModeEnum.None,
    };
}
