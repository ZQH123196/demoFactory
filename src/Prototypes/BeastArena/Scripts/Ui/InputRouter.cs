#nullable enable
using System;
using DemoFactory.GugaFramework.Camera;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 玩家输入的那一层路由 —— 左键拾取、右键点选、F1 操作说明，以及拖拽状态机。
///
/// <para><b>输入一律走 InputMap action</b>（「输入走 InputMap」）：<c>pick</c> 是左键、
/// <c>help</c> 是 F1。按下与松开分别读 <c>IsActionJustPressed</c> / <c>IsActionJustReleased</c>，
/// 坐标取视口鼠标位 —— 这样调试桥的合成输入也驱动得了它，裸键轮询做不到这一条。</para>
///
/// <para><b>UI 命中闸</b>：处理世界拾取之前先判 <c>GuiGetHoveredControl() != null</c>。
/// 否则点蓝图条会同时落到甲板上 —— 蓝图槽位与甲板在同一块屏幕空间上，
/// 只有界面层知道「这一下被谁接走了」。</para>
///
/// <para><b>模态</b>：弹层打开期间 <see cref="UiRoot.IsModal"/> 为真，世界一个拾取都不收。
/// 标题屏与暂停屏由原型递进来的 <c>worldBlocked</c> 一并算进去。</para>
///
/// <para><b>右键不抢事件</b>：<see cref="TopDownRig"/> 已经在拖拽，右键的「按下即松」
/// 由它按位移阈值判出来（<see cref="TopDownRig.ViewClickedWithButton"/>）。
/// 本件只订阅那一发，不自己再收一遍鼠标事件 —— 两条路都收就分不清谁先谁后。</para>
///
/// <para><b>拖拽只出状态机与事件</b>（本 ticket）：按下只记候选，位移超过
/// <see cref="DragThresholdPixels"/> 才转 <see cref="DragStarted"/>。
/// 抓起实体、跟手、落点判定归后续 ticket（「上帝之手」那一张 ticket）。</para>
/// </summary>
public partial class InputRouter : Node
{
    /// <summary>左键拾取的 action 名。</summary>
    public const string PickAction = "pick";

    /// <summary>操作说明的 action 名。</summary>
    public const string HelpAction = "help";

    /// <summary>拖拽阈值（像素）：按下后位移超过它才从「候选」转成「拖拽」。</summary>
    public const float DragThresholdPixels = 8f;

    private readonly UiRoot _ui;
    private readonly TopDownRig _rig;
    private readonly Func<bool> _worldBlocked;
    private readonly Func<bool> _helpOpen;

    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPos;

    /// <summary>左键在世界上按下（那一刻还没超过阈值）。</summary>
    public event Action<Vector2>? WorldPressed;

    /// <summary>位移超过阈值，转成拖拽。</summary>
    public event Action<Vector2>? DragStarted;

    /// <summary>拖拽中每帧的位置。</summary>
    public event Action<Vector2>? DragMoved;

    /// <summary>拖拽松手。</summary>
    public event Action<Vector2>? DragEnded;

    /// <summary>拖拽途中被模态挡住之后松手 —— 按「取消，原地放回」处理。</summary>
    public event Action<Vector2>? DragCancelled;

    /// <summary>按下即松（没超过阈值）—— 算一次点选。</summary>
    public event Action<Vector2>? WorldClicked;

    /// <summary>右键按下即松（由相机的带按钮事件转发，已过 UI 命中闸）。</summary>
    public event Action<Vector2>? RightClicked;

    /// <summary>F1：操作说明要开合一次。</summary>
    public event Action? HelpToggled;

    /// <summary>当前是不是在拖拽（超过阈值之后）。</summary>
    public bool IsDragging => _dragging;

    /// <summary>当前有没有按住左键（候选阶段或拖拽阶段）。</summary>
    public bool IsPressed => _pressed;

    public InputRouter(UiRoot ui, TopDownRig rig, Func<bool> worldBlocked, Func<bool>? helpOpen = null)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));
        _worldBlocked = worldBlocked ?? throw new ArgumentNullException(nameof(worldBlocked));
        _helpOpen = helpOpen ?? (() => false);
        Name = "InputRouter";
    }

    public override void _Ready()
    {
        EnsureInputMap();
        _rig.ViewClickedWithButton += OnViewClickedWithButton;
    }

    public override void _ExitTree() => _rig.ViewClickedWithButton -= OnViewClickedWithButton;

    public override void _Process(double delta)
    {
        var mouse = GetViewport().GetMousePosition();

        // F1 只在没有别的模态时抢开：招募框 / 战报 / 建筑菜单开着时按 F1 不叠一层操作说明。
        // 操作说明自己开着那一档要能按 F1 关掉，所以看 _helpOpen 那一道例外。
        if (Input.IsActionJustPressed(HelpAction) && (!_ui.IsModal || _helpOpen()))
            HelpToggled?.Invoke();

        if (!_pressed)
        {
            if (!Input.IsActionJustPressed(PickAction)) return;
            if (WorldBlocked()) return;

            _pressed = true;
            _dragging = false;
            _pressPos = mouse;
            WorldPressed?.Invoke(mouse);
            return;
        }

        if (!_dragging && _pressPos.DistanceTo(mouse) > DragThresholdPixels)
        {
            _dragging = true;
            DragStarted?.Invoke(mouse);
        }

        if (_dragging)
            DragMoved?.Invoke(mouse);

        if (!Input.IsActionJustReleased(PickAction)) return;

        _pressed = false;

        // 松手也要再判一次：按下之后可能刚被 F1 / 自动战报弹出挡上（模态在按下那一刻还不存在）。
        var blockedOnRelease = WorldBlocked();

        if (_dragging)
        {
            _dragging = false;

            // 拖拽中被挡：DragController 收到这一发后按「取消，原地放回」处理。
            if (!blockedOnRelease) DragEnded?.Invoke(mouse);
            else DragCancelled?.Invoke(mouse);
        }
        else if (!blockedOnRelease)
        {
            WorldClicked?.Invoke(mouse);
        }
    }

    /// <summary>读数（调试桥与排障用）：按下 / 拖拽 / 模态三条状态一次念出来。</summary>
    public string DebugReadout()
        => $"pressed={_pressed.ToString().ToLowerInvariant()} " +
           $"dragging={_dragging.ToString().ToLowerInvariant()} " +
           $"modal={_ui.IsModal.ToString().ToLowerInvariant()} " +
           $"threshold={DragThresholdPixels.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}px";

    /// <summary>换局时复位按键状态机（拖拽 / 按下都清掉）。</summary>
    public void ResetPressed()
    {
        _pressed = false;
        _dragging = false;
        _pressPos = Vector2.Zero;
    }

    private void OnViewClickedWithButton(Vector2 screen, MouseButton button)
    {
        if (button != MouseButton.Right) return;
        if (WorldBlocked()) return;
        RightClicked?.Invoke(screen);
    }

    /// <summary>这一下是不是该被世界忽略：模态、标题屏 / 暂停屏、或者鼠标正压在控件上。</summary>
    private bool WorldBlocked()
    {
        if (_ui.IsModal) return true;
        if (_worldBlocked()) return true;
        return GetViewport().GuiGetHoveredControl() is not null;
    }

    /// <summary>幂等登记两条 action（已存在则原样保留 —— 项目设置里手写的绑定优先）。</summary>
    private static void EnsureInputMap()
    {
        if (!InputMap.HasAction(PickAction))
        {
            InputMap.AddAction(PickAction);
            InputMap.ActionAddEvent(PickAction, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        }

        if (!InputMap.HasAction(HelpAction))
        {
            InputMap.AddAction(HelpAction);
            InputMap.ActionAddEvent(HelpAction, new InputEventKey { PhysicalKeycode = Key.F1 });
        }
    }
}
