#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.InputLayer;
using Godot;

namespace DemoFactory.GugaFramework.Camera;

/// <summary>
/// 俯视角相机架（GugaFramework 通用件）— 焦点 + 距离 + 偏航三自由度，俯角固定。
///
/// 设计思路：
/// - 全部相机状态只有五个数：焦点（地面点）、距离、俯仰角、偏航角、垂直焦距。节点分三层单轴串联：
///     TopDownRig(位置=焦点) → Yaw(绕 Y 转) → Tilt(绕 X 定俯角) → Camera3D(局部位置 (0,0,距离))
///   相机永远正对焦点，所以"绕屏幕中心旋转"和"推拉缩放"是结构自带的，无需每帧 LookAt；
///   每层只用一根轴，也就不依赖引擎的欧拉角顺序约定。
/// - 俯仰角可调（MinPitchDegrees~MaxPitchDegrees）：低角度才看得出纵深与剪影层次，
///   固定俯角会把"绕到侧面看"这一整个维度砍掉。**下限缺省是负的** ——
///   俯仰是自由的一维，强行卡一个正的下限会让「绕到主体下方往上看」这件事拍不出来，
///   而背着一座山的巨兽、悬在半空的岛，那一半信息全在主体下方。
///   真要守住「始终俯着看」的那类原型，把 MinPitchDegrees 设成正数即可（那一格就是留给它的）。
/// - 本组件不自带相机，只接管一台已有的（AttachCamera）— 原型场景里的 Camera3D 原位复用，
///   .tscn 零改动；也不会出现"自建一台 + 场景一台"双相机抢Current 的坑。
/// - 缩放沿视轴改距离而非改 fov：改 fov 会改变透视形变，同一模型在不同缩放下剪影不一致 —
///   灰盒阶段剪影是唯一的识别手段，不能漂。
/// - 焦距是**显式给的一档**（<see cref="SetFov"/>）：原型按「这一档看多远、看多宽」给数，
///   本件负责阻尼带过去。缩放那一路仍不动焦距 —— 剪影那一档守的是「只推拉时剪影不变」，
///   换档换焦距是原型自己决定的事，不是缩放顺手带出来的。
/// - 所有运动走指数阻尼（帧率无关）：当前值 += (目标值 - 当前值) * (1 - e^(-刚度·dt))。
///   输入只写目标值，画面永远平滑，快进/掉帧不改变手感。
/// - 输入一律走 InputMap action（代码内自建，已存在则不覆盖，项目设置可反定义）：
///   MCP 的 input_action 只认 action，裸键轮询注入不进来 — 相机不可实测就等于不可交付。
///   滚轮与中键拖拽是模拟量、进不了 action，故直读原始事件，两条路都汇到同一个缩放/旋转口。
/// - 中键一分为二：拖拽 = 绕转 + 改俯仰（跟手转，相机跟着鼠标走），
///   按下即松且位移小于阈值 = 单击，抛 ViewClicked 给原型判"点中了谁"。
/// - 只管相机，不懂游戏："点中哪个单位""锁定谁"由原型自己判，本组件只暴露 FollowTarget
///   、ViewClicked 和 FollowToggleRequested（按键请求，语义留给原型）。
/// - 逻辑层零感知：相机状态是表现层状态，不进逻辑层，确定性测试不受影响。
///
/// 接入（原型侧四行）：
///   var rig = new TopDownRig { BoundsMinXZ = new(-40,-40), BoundsMaxXZ = new(100,60) };
///   GetNode&lt;Node3D&gt;("%World").AddChild(rig);
///   rig.AttachCamera(GetNode&lt;Camera3D&gt;("%Camera3D"));
///   rig.SetPose(new Vector3(22, 0, 6), 106.5f, 0f, 50.3f);   // 初始机位：焦点/距离/偏航/俯角
///   rig.SaveHome();                                   // 记为归位点
/// </summary>
public partial class TopDownRig : Node3D
{
    // === 取景参数 ===

    /// <summary>最近距离（贴脸观察单个单位的剪影）。</summary>
    [Export] public float MinDistance { get; set; } = 25f;

    /// <summary>最远距离（一眼看完整场）。</summary>
    [Export] public float MaxDistance { get; set; } = 140f;

    /// <summary>俯角（度）— 当前值（平滑后），初值在此给，玩家可用中键上下拖拽或 ↑/↓ 改。</summary>
    [Export] public float PitchDegrees { get; set; } = 50f;

    /// <summary>
    /// 俯角下限（度）。**缺省 −89.99：能绕到主体下方往上看**。
    ///
    /// <para>为什么缺省是负的（2026-10-15，用户判「这是 bug」）：这一格原来缺省 22°，
    /// 于是拖拽往上下推到底就推不动，「从下往上看」拍不出来 ——
    /// 偏航本来就自由，唯独俯角被卡住一处，而偏航与俯仰本是同一维自由度的两个方向。
    /// 只要主体有一部分在**上面**（背着一座山的巨兽、悬在半空的岛），
    /// 那个正下限就够不着主体底面，而那正是那类世界的一半信息。</para>
    ///
    /// <para>这一格留给**确实要守住俯视**的原型：把它设成正数即可回到旧行为，
    /// 不必在本件上再加一个开关 —— 开关会把「缺省是什么」这件事藏起来，
    /// 而缺省本身就是这次要改的那一处。</para>
    /// </summary>
    [Export] public float MinPitchDegrees { get; set; } = -89.99f;

    /// <summary>
    /// 俯角上限（度）。缺省 89.99 —— **贴近极点，但不等于极点**。
    ///
    /// <para>为什么不是 90（用户定的数，2026-10-15）：到 ±90 那一刻，相机正好落在焦点的
    /// 正上/正下方，视线与极点共线，于是「绕转」那一维塌成画面原地翻滚 ——
    /// 偏航键还在按，却不再绕着主体走。差这 0.01° 就换掉那一格退化，
    /// 与 Blender 把俯角钳在 ±89.99 是同一个做法。</para>
    /// </summary>
    [Export] public float MaxPitchDegrees { get; set; } = 89.99f;

    /// <summary>点选容差的参考距离：原型在这个距离上调好屏幕点击容差，缩放时按此换算（见 PickToleranceScale）。</summary>
    [Export] public float ReferenceDistance { get; set; } = 106.5f;

    /// <summary>焦点可活动的世界范围下限（X/Z 平面），超出即停 — 防止把相机拖到世界之外看虚空。</summary>
    [Export] public Vector2 BoundsMinXZ { get; set; } = new(-40f, -40f);

    /// <summary>焦点可活动的世界范围上限（X/Z 平面）。</summary>
    [Export] public Vector2 BoundsMaxXZ { get; set; } = new(100f, 60f);

    /// <summary>
    /// 焦点高度（米）—— 镜头对准的那个平面在多高，**相对世界 y=0**。
    ///
    /// <para>为什么要有这一格：焦点本来恒被压平到 y=0（<see cref="Flatten"/> 与 <see cref="ClampFocus"/>），
    /// 这对「棋盘在地面上」的世界是对的；但主体离地很高时（巨兽背上的小镇在 36 米高处），
    /// 压平到 y=0 就等于**镜头始终盯着主体脚下**，主体会偏出画面上沿，
    /// 唯一的补救是把俯角抬到接近正俯视 —— 而那样剪影就没了（「巨兽在画面上走」正是剪影那一档）。
    /// 这一格把「主体离地多高」显式交给原型，不让每个原型各自去抬俯角补。</para>
    ///
    /// <para>缺省 0 = 压平到 y=0，与这一格加进来之前逐字同行为；别的原型不设它就不受影响。</para>
    /// </summary>
    [Export] public float FocusHeightMeters { get; set; } = 0f;

    // === 手感参数 ===

    /// <summary>滚轮每格改变的距离比例。</summary>
    [Export] public float ZoomPerNotch { get; set; } = 0.16f;

    /// <summary>键盘连续缩放速度（每秒距离比例）。</summary>
    [Export] public float ZoomKeyRate { get; set; } = 1.6f;

    /// <summary>平移速度系数：实际速度 = 系数 × 当前距离（远看平移快、近看慢，屏幕位移感恒定）。</summary>
    [Export] public float PanSpeedFactor { get; set; } = 0.45f;

    /// <summary>旋转速度（度/秒）— Q/E 与 ↑/↓ 按键通道用（MCP 注入走这条）。</summary>
    [Export] public float RotateSpeed { get; set; } = 80f;

    /// <summary>中键拖拽灵敏度（度/像素）。</summary>
    [Export] public float OrbitDegreesPerPixel { get; set; } = 0.25f;

    /// <summary>中键按下后位移超过多少像素就不算"单击"（改判为拖拽绕转）。</summary>
    [Export] public float ClickDragThresholdPx { get; set; } = 6f;

    /// <summary>中键按住超过多少秒不算"单击"。</summary>
    [Export] public float ClickMaxSeconds { get; set; } = 0.35f;

    /// <summary>距离阻尼刚度。</summary>
    [Export] public float ZoomStiffness { get; set; } = 12f;

    /// <summary>焦距阻尼刚度 —— 与 <see cref="ZoomStiffness"/> 同量级，两档切换时距离与焦距一起走。</summary>
    [Export] public float FovStiffness { get; set; } = 12f;

    /// <summary>自由观察时焦点阻尼刚度（平移跟手）。</summary>
    [Export] public float PanStiffness { get; set; } = 14f;

    /// <summary>跟随锁定时的焦点阻尼刚度（越低越柔和，也越滞后）。</summary>
    [Export] public float FollowStiffness { get; set; } = 8f;

    /// <summary>偏航阻尼刚度。</summary>
    [Export] public float RotateStiffness { get; set; } = 10f;

    /// <summary>关掉内置输入处理时用（原型想自定义按键的话）。</summary>
    [Export] public bool HandleInput { get; set; } = true;

    /// <summary>
    /// 右键拖拽是否与中键等同（绕转 + 改俯仰，按下即松且没拖出阈值仍算单击抛 <see cref="ViewClicked"/>）。
    ///
    /// <para><b>默认关</b>，由原型显式打开 —— 右键不是「相机这一层天生该管」的键：
    /// 它在别的原型那里已经被派了别的意思（无限领主拿它退出营建模式），
    /// 而本组件的 <c>_UnhandledInput</c> 收到右键后会 <c>SetInputAsHandled()</c>，
    /// 抢在原型那一格之前 —— 直接默认打开就是把那份原型的手感改掉。
    /// 「相机单源」说的是相机只有这一个架子，不是说所有键位归它管：
    /// 键位归哪一层是原型的决定，本件只提供这一格开关。</para>
    /// </summary>
    [Export] public bool RightButtonOrbits { get; set; }

    // === 运行时状态（表现层自持，不进逻辑层） ===

    // 三层单轴节点：偏航 → 俯角 → 相机。每层只用一个轴，谁也不依赖引擎的欧拉角顺序。
    private Node3D _yawNode = null!;
    private Node3D _tiltNode = null!;
    private Camera3D? _camera;
    private Vector3 _focus;
    private Vector3 _targetFocus;
    private float _distance = 106.5f;
    private float _targetDistance = 106.5f;
    private float _targetFov;
    private float _yaw;
    private float _targetYaw;
    private float _targetPitch;
    private Node3D? _followTarget;
    private Vector3 _homeFocus;
    private float _homeDistance = 106.5f;
    private float _homeYaw;
    private float _homePitch = 50f;

    // 中键：拖拽 = 绕转/俯仰，按下即松且几乎没动 = 单击（抛给原型判点中了谁）
    private bool _orbiting;
    private Vector2 _orbitFrom;
    private Vector2 _orbitLast;
    private double _orbitSeconds;
    private MouseButton _orbitButton = MouseButton.Middle;

    /// <summary>本架子当前接管的相机（未 AttachCamera 时为 null）。</summary>
    public Camera3D? Camera => _camera;

    /// <summary>当前焦点（平滑后的值，y 恒为 0）。</summary>
    public Vector3 Focus => _focus;

    /// <summary>当前距离。</summary>
    public float Distance => _distance;

    /// <summary>当前偏航角（度）。</summary>
    public float YawDegrees => _yaw;

    /// <summary>当前是否在跟随某个目标。</summary>
    public bool IsFollowing => _followTarget is not null && GodotObject.IsInstanceValid(_followTarget);

    /// <summary>
    /// 点选容差换算系数：屏幕上"一个单位多大"与距离成反比，所以参考距离上调好的像素容差
    /// 要乘这个系数，才能在拉近后仍然等于"贴着一个单位"的手感（拉近 → 系数 > 1，容差放宽）。
    /// </summary>
    public float PickToleranceScale => ReferenceDistance / Math.Max(_distance, 0.01f);

    /// <summary>Tab 之类的"锁定切换"按键请求 — 锁定谁是游戏语义（点中了谁），交给原型订阅处理。</summary>
    public event Action? FollowToggleRequested;

    /// <summary>中键单击（按下即松、没拖出阈值）— 给屏幕坐标，"点中了谁"由原型判。</summary>
    public event Action<Vector2>? ViewClicked;

    /// <summary>
    /// 与 <see cref="ViewClicked"/> 同一时机、多带一个按钮 —— 原型拿它区分「右键点了一下」与
    /// 「中键点了一下」。只在「按下即松、位移小于阈值」时发出，天然区分「点一下」与「拖着转视角」。
    ///
    /// <para><b>旧事件一个字不动</b>：签名、触发条件、触发顺序都照旧，三个既有订阅者不受影响。
    /// 本事件与它并排发，不替换它。</para>
    /// </summary>
    public event Action<Vector2, MouseButton>? ViewClickedWithButton;

    // === 输入 action 名（GugaFramework 公共口径，各原型一致） ===
    //
    // 名字与读法都取自公共层那一份概念表，这里不再各写一个字面量 ——
    // 抄一份就是下一次漂移的起点，而漂掉的恰好是「哪条该刚按下、哪条该按住」。

    /// <summary>读法分派 —— 引擎那三个读数口在本件构造时递进来。</summary>
    private readonly InputRead _read = new(name => Input.IsActionJustPressed(name), name => Input.IsActionPressed(name),
        name => Input.GetActionStrength(name));

    public static string ActionZoomIn => InputConcept.CameraZoomIn.Name;

    public static string ActionZoomOut => InputConcept.CameraZoomOut.Name;

    public static string ActionRotateLeft => InputConcept.CameraRotateLeft.Name;

    public static string ActionRotateRight => InputConcept.CameraRotateRight.Name;

    public static string ActionPitchUp => InputConcept.CameraPitchUp.Name;

    public static string ActionPitchDown => InputConcept.CameraPitchDown.Name;

    public static string ActionPanLeft => InputConcept.CameraPanLeft.Name;

    public static string ActionPanRight => InputConcept.CameraPanRight.Name;

    public static string ActionPanForward => InputConcept.CameraPanForward.Name;

    public static string ActionPanBack => InputConcept.CameraPanBack.Name;

    public static string ActionFollowToggle => InputConcept.CameraFollowToggle.Name;

    public static string ActionResetPose => InputConcept.CameraResetPose.Name;

    public override void _Ready()
    {
        Name = "TopDownRig";
        _yawNode = new Node3D { Name = "Yaw" };
        _tiltNode = new Node3D { Name = "Tilt" };
        AddChild(_yawNode);
        _yawNode.AddChild(_tiltNode);
        _targetPitch = PitchDegrees;
        ApplyPose();

        if (HandleInput) EnsureInputMap();
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        if (HandleInput) ReadInput(dt);
        if (_orbiting) _orbitSeconds += delta;

        if (IsFollowing)
            _targetFocus = Flatten(_followTarget!.GlobalPosition);

        // 指数阻尼：与帧率无关，目标值随时可被输入改写
        var focusStiffness = IsFollowing ? FollowStiffness : PanStiffness;
        _focus = _focus.Lerp(_targetFocus, 1f - Mathf.Exp(-focusStiffness * dt));
        _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-ZoomStiffness * dt));
        _yaw = Mathf.Lerp(_yaw, _targetYaw, 1f - Mathf.Exp(-RotateStiffness * dt));
        PitchDegrees = Mathf.Lerp(PitchDegrees, _targetPitch, 1f - Mathf.Exp(-RotateStiffness * dt));

        // 焦距走同一条指数阻尼：切档时距离与焦距一起走，画面不跳变。
        if (_camera is not null)
            _camera.Fov = Mathf.Lerp(_camera.Fov, _targetFov, 1f - Mathf.Exp(-FovStiffness * dt));

        ApplyPose();
    }

    /// <summary>
    /// 滚轮缩放与中键拖拽是模拟量、进不了 action，只能直读原始事件；
    /// 中键抬起时若几乎没动、且按得不久 → 判为"单击"，抛给原型去认点中了谁。
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!HandleInput) return;

        if (_orbiting && @event is InputEventMouseMotion motion)
        {
            OrbitBy(motion.Position - _orbitLast);
            _orbitLast = motion.Position;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton click) return;

        if (click.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (click.Pressed)
                ZoomBy(click.ButtonIndex == MouseButton.WheelUp ? -ZoomPerNotch : ZoomPerNotch);
        }
        else if (click.ButtonIndex == MouseButton.Middle
                 || (RightButtonOrbits && click.ButtonIndex == MouseButton.Right))
        {
            if (click.Pressed) BeginOrbit(click.Position, click.ButtonIndex);
            else EndOrbit(click.Position);
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    // === 对外控制口（原型与调试注入用） ===

    /// <summary>把场景里已有的相机挂到本架子上 — 保留 .tscn 里的相机节点，零场景改动。</summary>
    public void AttachCamera(Camera3D camera)
    {
        camera.Reparent(_tiltNode, keepGlobalTransform: false);
        camera.Rotation = Vector3.Zero; // 俯角由 Tilt 层给，相机自己必须归零，否则转两遍
        camera.Current = true;
        _camera = camera;

        // 焦距目标值取相机当前那一档：不显式给焦距的原型（多数）行为逐字不变 ——
        // 目标与当前相等，阻尼那一格等于没动。
        _targetFov = camera.Fov;

        ApplyPose();
    }

    /// <summary>
    /// 直接设定机位并立即生效（不过阻尼）。初始机位、MCP 定点截图走这里。
    /// 归位点是独立记的一笔（SaveHome）— 调试用的临时摆位不该把玩家的"回家"键带走。
    /// </summary>
    public void SetPose(Vector3 focus, float distance, float yawDegrees, float pitchDegrees)
    {
        _focus = _targetFocus = ClampFocus(focus);
        _targetDistance = _distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        _yaw = _targetYaw = yawDegrees;
        PitchDegrees = _targetPitch = ClampPitch(pitchDegrees);
        ApplyPose();
    }

    /// <summary>把当前机位记为"家"（一键归位的目标）。初始机位调好后调一次。</summary>
    public void SaveHome()
    {
        _homeFocus = _targetFocus;
        _homeDistance = _targetDistance;
        _homeYaw = _targetYaw;
        _homePitch = _targetPitch;
    }

    /// <summary>解除跟随并回家。</summary>
    public void ResetPose()
    {
        FollowTarget(null);
        SetPose(_homeFocus, _homeDistance, _homeYaw, _homePitch);
    }

    /// <summary>跟随锁定：传 null 解除锁定（解除后焦点留在原地，不跳回）。</summary>
    public void FollowTarget(Node3D? target)
    {
        _followTarget = target;
        if (target is not null && GodotObject.IsInstanceValid(target))
            _targetFocus = Flatten(target.GlobalPosition);
    }

    /// <summary>距离缩放：正数拉远、负数拉近（按比例）。</summary>
    public void ZoomBy(float ratio)
        => _targetDistance = Mathf.Clamp(_targetDistance * (1f + ratio), MinDistance, MaxDistance);

    /// <summary>
    /// 距离落到某一档（米）—— 只写目标值，仍走 <see cref="ZoomStiffness"/> 阻尼，不跳变。
    ///
    /// <para><b>与 <see cref="ZoomBy"/> 的分工</b>：<see cref="ZoomBy"/> 按比例缩放（滚轮 / 按键那一路，
    /// 相对当前距离）；本件按绝对值落档（原型给「这一档看多远」的那个数）。
    /// 原型的视角档位走本件，相机这一层仍然只有这一个架子（「相机单源」）。</para>
    /// </summary>
    public void ZoomTo(float distanceMeters)
        => _targetDistance = Mathf.Clamp(distanceMeters, MinDistance, MaxDistance);

    /// <summary>
    /// 焦距落到某一档（度，Godot 的垂直 FOV）—— 只写目标值，仍走 <see cref="FovStiffness"/> 阻尼，不跳变。
    ///
    /// <para><b>与 <see cref="ZoomTo"/> 的分工</b>：<see cref="ZoomTo"/> 给「这一档看多远」，
    /// 本件给「这一档看得多宽」—— 可见高度 = 2 × 距离 × tan(焦距 ÷ 2)，
    /// 所以「看得多远」与「看得多宽」是两个数。原型给绝对值，不自己算画幅。
    /// 焦距这一档的阻尼在本件里做，相机只有这一个架子（「相机单源」）。</para>
    /// </summary>
    public void SetFov(float degrees) => _targetFov = degrees;

    /// <summary>偏航旋转（度，累加）。</summary>
    public void RotateBy(float degrees) => _targetYaw += degrees;

    /// <summary>俯仰（度，累加，自动钳在 <see cref="MinPitchDegrees"/>~<see cref="MaxPitchDegrees"/>）。</summary>
    public void TiltBy(float degrees)
        => _targetPitch = ClampPitch(_targetPitch + degrees);

    /// <summary>平移焦点（世界 X/Z 增量）。</summary>
    public void PanBy(float dx, float dz)
        => _targetFocus = ClampFocus(_targetFocus + new Vector3(dx, 0f, dz));

    /// <summary>相机读数 — 截图只能证明"画面变了"，读数才能证明"变到了该去的地方"。</summary>
    /// <remarks>俯角与俯角范围一律 F2：缺省边界是 89.99，F0 会把它显示成「90」，
    /// 正好把「不等于极点」这件事显示没，读数就骗人了。
    /// <para>垂直 FOV 也报出来：画幅比例（可见宽 ÷ 可见高）由它算，
    /// 「这一档看得见多宽」那一格要能机器复核，不靠人推。</para></remarks>
    public string DebugReadout()
        => $"focus=({_focus.X:F1},{_focus.Z:F1}) dist={_distance:F1} yaw={_yaw:F0} pitch={PitchDegrees:F2} "
            + $"pitchRange={MinPitchDegrees.ToString("F2", CultureInfo.InvariantCulture)}"
            + $"..{MaxPitchDegrees:F2} "
            + $"follow={(IsFollowing ? _followTarget!.Name.ToString() : "off")} pick×{PickToleranceScale:F2} "
            + $"fov={(_camera?.Fov ?? 0f).ToString("F0", CultureInfo.InvariantCulture)}";

    // === 内部 ===

    private void BeginOrbit(Vector2 screenPos, MouseButton button)
    {
        _orbiting = true;
        _orbitButton = button;
        _orbitFrom = _orbitLast = screenPos;
        _orbitSeconds = 0;
    }

    private void EndOrbit(Vector2 screenPos)
    {
        if (!_orbiting) return;
        _orbiting = false;
        var dragged = screenPos.DistanceTo(_orbitFrom);
        if (dragged <= ClickDragThresholdPx && _orbitSeconds <= ClickMaxSeconds)
        {
            ViewClicked?.Invoke(screenPos);
            ViewClickedWithButton?.Invoke(screenPos, _orbitButton);
        }
    }

    /// <summary>拖拽跟手：相机跟着鼠标走 — 往右拖绕到右边，往下拖压低视角。</summary>
    private void OrbitBy(Vector2 delta)
    {
        RotateBy(delta.X * OrbitDegreesPerPixel);
        TiltBy(-delta.Y * OrbitDegreesPerPixel);
    }

    private void ReadInput(float dt)
    {
        // 读法由概念自己带（按住 / 刚按下），本件不再逐条挑「这里该用 IsActionPressed 还是
        // IsActionJustPressed」—— 挑错的后果是「按住不放重复触发」。
        if (_read.Read(InputConcept.CameraZoomIn)) ZoomBy(-ZoomKeyRate * dt);
        if (_read.Read(InputConcept.CameraZoomOut)) ZoomBy(ZoomKeyRate * dt);
        if (_read.Read(InputConcept.CameraRotateLeft)) RotateBy(-RotateSpeed * dt);
        if (_read.Read(InputConcept.CameraRotateRight)) RotateBy(RotateSpeed * dt);
        if (_read.Read(InputConcept.CameraPitchUp)) TiltBy(RotateSpeed * dt);
        if (_read.Read(InputConcept.CameraPitchDown)) TiltBy(-RotateSpeed * dt);

        var pan = Vector2.Zero;
        if (_read.Read(InputConcept.CameraPanForward)) pan.Y += 1f;
        if (_read.Read(InputConcept.CameraPanBack)) pan.Y -= 1f;
        if (_read.Read(InputConcept.CameraPanRight)) pan.X += 1f;
        if (_read.Read(InputConcept.CameraPanLeft)) pan.X -= 1f;
        if (pan != Vector2.Zero)
        {
            // 平移方向跟着偏航转：W 永远是"往屏幕上方走"（俯角不影响地面方向）
            var step = (GroundForward() * pan.Y + GroundRight() * pan.X).Normalized()
                * (PanSpeedFactor * _distance * dt);
            PanBy(step.X, step.Z);
        }

        // 这两条是「刚按下」：按住 Tab 会把跟随反复翻开合上，复位则会每帧重置一次。
        if (_read.Read(InputConcept.CameraResetPose)) ResetPose();
        if (_read.Read(InputConcept.CameraFollowToggle)) FollowToggleRequested?.Invoke();
    }

    private Vector3 GroundForward()
    {
        var r = Mathf.DegToRad(_yaw);
        return new Vector3(-Mathf.Sin(r), 0f, -Mathf.Cos(r));
    }

    private Vector3 GroundRight()
    {
        var r = Mathf.DegToRad(_yaw);
        return new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
    }

    private Vector3 Flatten(Vector3 position) => new(position.X, FocusHeightMeters, position.Z);

    /// <summary>
    /// 俯角的钳位。<see cref="SetPose"/> 与 <see cref="TiltBy"/> 共走这一口 ——
    /// 两处各夹一次的话，定点摆位与拖拽的边界会不一样，而那种差只在「刚好顶到边界」那一格显形。
    /// </summary>
    private float ClampPitch(float degrees)
        => Mathf.Clamp(degrees, MinPitchDegrees, MaxPitchDegrees);

    private Vector3 ClampFocus(Vector3 focus)
        => new(Mathf.Clamp(focus.X, BoundsMinXZ.X, BoundsMaxXZ.X), FocusHeightMeters,
            Mathf.Clamp(focus.Z, BoundsMinXZ.Y, BoundsMaxXZ.Y));

    private void ApplyPose()
    {
        Position = _focus;
        _yawNode.RotationDegrees = new Vector3(0f, _yaw, 0f);
        _tiltNode.RotationDegrees = new Vector3(-PitchDegrees, 0f, 0f);
        if (_camera is not null) _camera.Position = new Vector3(0f, 0f, _distance);
    }

    /// <summary>
    /// 建 action（已存在则原样保留 — 项目设置里手写的绑定优先，代码只兜底）。
    /// 键位分工：WASD 平移、Q/E 或 ←/→ 绕转、↑/↓ 俯仰、+/- 缩放（与滚轮同一条通道）。
    /// </summary>
    private static void EnsureInputMap()
    {
        AddKeyAction(ActionZoomIn, Key.Equal, Key.KpAdd);
        AddKeyAction(ActionZoomOut, Key.Minus, Key.KpSubtract);
        AddKeyAction(ActionRotateLeft, Key.Q, Key.Left);
        AddKeyAction(ActionRotateRight, Key.E, Key.Right);
        AddKeyAction(ActionPitchUp, Key.Up);
        AddKeyAction(ActionPitchDown, Key.Down);
        AddKeyAction(ActionPanForward, Key.W);
        AddKeyAction(ActionPanBack, Key.S);
        AddKeyAction(ActionPanLeft, Key.A);
        AddKeyAction(ActionPanRight, Key.D);
        AddKeyAction(ActionFollowToggle, Key.Tab);
        AddKeyAction(ActionResetPose, Key.Home);
    }

    private static void AddKeyAction(StringName action, params Godot.Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
