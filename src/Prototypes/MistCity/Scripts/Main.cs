#nullable enable
using System;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.FrontEnd;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.InputLayer;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Prototypes.MistCity.Logic;
using Godot;

namespace DemoFactory.Prototypes.MistCity;

/// <summary>
/// 迷雾之都的主场景入口 —— <b>表现层那一半</b>。
///
/// <para>从 <c>_Template</c> 复制而来，模板注释自己列的六处接线在这里全部接完：
/// ① 前端层的暂停请求 → 世界；② 局结束 → 冻时间轴；③ 时间旋钮的暂停 → 世界；
/// ④ 推进循环 → <c>_world.Advance(1)</c>；⑤ 单步 → 世界；⑥ 快照 → <c>_world.DebugSnapshot()</c>。
/// 模板自带的三段（输入层四个时间旋钮、前端层、相机与出图）原样保留。</para>
///
/// <para>视觉管线：正交 3D 节点（World）处理空间，<c>CanvasLayer</c>（UILayer）处理 UI，
/// 视角走 <c>GugaFramework/Camera/TopDownRig</c>（「相机单源」）。</para>
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        // 字体先装：缺省字体没有汉字字形，不装的话牌面中文上屏是整行空白。
        FontSetup.Install();

        SetupCameraRig();
        SetupInputMap();
        SetupDebugBridge();
        SetupFrontEnd();
        _table = new CardTable(GetNode<CanvasLayer>("UILayer"));
        StartRun(new MistCityConfig());
        Log("[MistCity] 主场景已加载。");
    }

    /// <summary>逻辑世界（三条观测口的宿主）。开一局时换新实例。</summary>
    private MistCityWorld? _world;

    /// <summary>牌桌的呈现（一张牌 = 一张卡：圆角、描边、投影、顶部条槽）。</summary>
    private CardTable _table = null!;

    /// <summary>这一局的配置。</summary>
    private MistCityConfig _config = new();

    // === 前端层（菜单栈、标题屏、暂停屏、结算屏、退出）===

    /// <summary>前端层节点。菜单浮在游戏之上，这一层号由它自己设。</summary>
    private FrontEndLayer _front = null!;

    /// <summary>
    /// 这一局是否已经结束 —— <b>由前端层的局结束事件写</b>，不查世界的判定结果。
    /// 推进循环那一格判「还推不推」，冻时间轴由 <c>RunEnded</c> 订阅里那一发管。
    /// </summary>
    private bool _runEnded;

    /// <summary>
    /// 前端层接线 —— 五行接法照模板（建层 → 答暂停 → 答退出 → 递标题屏条目 → 注册共享词表）。
    ///
    /// <para><b>接线点①</b>：暂停请求答世界，走基类那个单一入口 <c>SetPaused</c>，
    /// 不直接调 <c>Pause</c>/<c>Resume</c> 两个口（那是同一件事两个口）。</para>
    ///
    /// <para><b>接线点②</b>：局结束 = 冻住时间轴。冻在事件里而不在推进循环里：
    /// 循环那一格判的是「还推不推」，冻住之后 <c>Advance</c> 直接回 false，
    /// 两者是同一件事的两道闸，而真值只有 <c>TimeFrozen</c> 那一处。</para>
    /// </summary>
    private void SetupFrontEnd()
    {
        _front = new FrontEndLayer { Name = "FrontEnd" };
        AddChild(_front);

        // 接线点①：暂停请求 → 世界（前端层只抛请求，冻不冻由原型的世界答）。
        _front.PauseRequested += paused => _world?.SetPaused(paused);

        // 退出请求：前端层只抛请求，退不退由这一侧答（「表现层只读」）。
        _front.QuitRequested += () => GetTree().Quit();

        // 标题屏条目：文字与动作都由原型递，框架不知道「重开一局」在这个原型跳去哪。
        _front.SetTitleEntries((Loc.T("重开一局"), () => Log(RestartRun())));

        _front.RunStarted += () => _runEnded = false;

        // 接线点②：局结束 = 冻住时间轴。
        _front.RunEnded += _ =>
        {
            _runEnded = true;
            _world?.FreezeTime();
        };

        foreach (var (verb, handler) in FrontEndVerbs.Vocabulary(_front))
            _bridge?.Register(verb, handler);
    }

    /// <summary>
    /// 合成输入的入录回调 —— <b>合成输入要进 Journal</b>，所以接线时这一句必须递进去；
    /// 不递，功能照跑，只是不进账。
    ///
    /// <para>名字与形状在公共层 <c>GugaFramework/Replay/SyntheticVerbs</c> 一份，
    /// 重放端一律跳过（重放端是纯 C#，复现不了「引擎收到了一次点击」）。</para>
    ///
    /// <para>本原型不录 Journal（灰盒不验存档与重放，见 spec「Out of Scope」），
    /// 所以这一格无事可做。将来要录，照三条改：① 词表摊进自己的 <c>VerbNames</c>
    /// （名字用 <c>SyntheticVerbs.Names</c>）；② 注入分发里接成 <c>SyntheticVerbs.Skip</c>；
    /// ③ 本方法把 <paramref name="kind"/> 翻成动词名后走自己的写入口入录。</para>
    /// </summary>
    protected virtual void RecordSyntheticInput(InputInjector.InputKind kind, string args)
    {
        _ = kind;
        _ = args;
    }

    // === 相机单源（「相机单源」：俯视角相机一律用 TopDownRig）===

    private TopDownRig _rig = null!;

    /// <summary>
    /// 俯视角相机架接入（滚轮缩放 · 中键拖拽绕转/改俯仰 · WASD 平移 · Q/E 转 · ↑↓ 俯仰
    /// · Home 归位 · Tab 请求锁定）。
    /// 边界按迷雾之都的桌面尺寸给；其余是手感旋钮，相机那十二条由 <c>TopDownRig</c> 自己登记。
    /// </summary>
    private void SetupCameraRig()
    {
        var world = GetNode<Node3D>("World");

        // 一盏方向光：场景里没有默认光源，不加这一盏整幅画面是黑的（出图复核抓到的）。
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.6f,
            ShadowEnabled = true,
        };
        sun.RotationDegrees = new Vector3(-52f, -38f, 0f);
        world.AddChild(sun);

        var rig = new TopDownRig
        {
            BoundsMinXZ = new Vector2(-30f, -24f),
            BoundsMaxXZ = new Vector2(30f, 24f),
        };
        world.AddChild(rig);

        var camera = new Camera3D { Name = "Camera3D" };
        world.AddChild(camera);
        rig.AttachCamera(camera);

        rig.SetPose(new Vector3(0f, 0f, 0f), 22f, 0f, 55f);
        rig.SaveHome();
        _rig = rig;
    }

    // === 相机 eval 直达口 — 同机位截图 + 读数断言 = 视觉回归 ===

    /// <summary>视角读数直达 — eval 直达：<c>CameraReadout()</c> 返回机位。</summary>
    public string CameraReadout() => _rig.DebugReadout();

    /// <summary>机位定点直达 — eval 直达：<c>CameraSetPose(0, 0, 22, 0, 55)</c>（不过阻尼）。</summary>
    public string CameraSetPose(float focusX, float focusZ, float distance, float yawDegrees, float pitchDegrees)
    {
        _rig.SetPose(new Vector3(focusX, 0f, focusZ), distance, yawDegrees, pitchDegrees);
        return _rig.DebugReadout();
    }

    // === 输入层（「输入走 InputMap」）===

    /// <summary>读法分派 —— 概念名与读法都来自公共层，引擎那三个读数口在本件递进去。</summary>
    private readonly InputRead _read = new(
        name => Input.IsActionJustPressed(name),
        name => Input.IsActionPressed(name),
        name => Input.GetActionStrength(name));

    /// <summary>
    /// InputMap action 登记 —— 幂等，重复调不叠事件。
    ///
    /// <para><b>为什么必须走 action 而不是裸键</b>（「输入走 InputMap」）：合成事件进不了
    /// 裸键状态表，调试通道就注不进来，等于该功能不可实测。</para>
    ///
    /// <para>键位是内容决定。相机那十二条由 <c>TopDownRig</c> 自己登记，
    /// 这里只登记时间旋钮那四条。</para>
    /// </summary>
    private void SetupInputMap()
    {
        EnsureAction(InputConcept.TogglePause.Name, Key.Space);
        EnsureAction(InputConcept.StepOnce.Name, Key.Period);
        EnsureAction(InputConcept.SpeedUp.Name, Key.Bracketright);
        EnsureAction(InputConcept.SpeedDown.Name, Key.Bracketleft);
    }

    /// <summary>幂等登记一条 InputMap action（已存在且已绑这个键就直接返回）。</summary>
    private static void EnsureAction(string action, Key keycode)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventKey { PhysicalKeycode = keycode };
        if (InputMap.ActionHasEvent(action, ev)) return;
        InputMap.ActionAddEvent(action, ev);
    }

    /// <summary>
    /// 时间旋钮四条 —— <b>每帧读一次「刚按下」</b>，读法由概念自带，本件不判。
    ///
    /// <para><b>接线点③</b>：暂停走 <c>_world.SetPaused(!_world.IsPaused)</c>（基类那个单一入口）。
    /// 逐帧读而不是逐事件读：逐事件读问的是「这一个事件算不算按下」，
    /// 于是同一帧来几个按下形状的事件就触发几次。</para>
    /// </summary>
    private void PollTimeKnobs()
    {
        // 接线点③：暂停 → 世界。
        if (_read.Read(InputConcept.TogglePause)) TogglePause();

        // 调桥上那个方法，不在这里再抄一份 —— 抄一份就是「同一件事两处代码」。
        if (_read.Read(InputConcept.StepOnce)) StepDebug();

        if (_read.Read(InputConcept.SpeedUp)) SetSpeed(_speed.Multiplier * 2.0);
        if (_read.Read(InputConcept.SpeedDown)) SetSpeed(_speed.Multiplier / 2.0);
    }

    /// <summary>
    /// 暂停 / 恢复 —— <b>空格键与调试桥走的是同一个方法</b>。
    ///
    /// <para><b>停完立刻刷一次画面</b>：顶栏那一格印着暂停状态，
    /// 而推进循环那一格在暂停时一步也不推 —— 不在这里刷一次，
    /// 玩家按了空格要等下一次推进才看得见自己按了什么。</para>
    ///
    /// <para><b>每一次切换都写一行日志</b>：「连按会不会连触发」这一格只看得见
    /// 切换了几次，没有这一行就只能拿步数反推，而步数还受菜单、结算、局结束影响。</para>
    /// </summary>
    private void TogglePause()
    {
        if (_world is null) return;
        _world.SetPaused(!_world.IsPaused);
        _table.Sync(_world);
        Log($"[MistCity] 暂停切换 → {(IsPaused ? "已暂停" : "运行中")}");
    }

    /// <summary>当前是不是暂停中 —— <b>桥上那一句与日志共用它</b>。</summary>
    private bool IsPaused => _world?.IsPaused ?? false;

    // === 时间旋钮：速度旋钮 + 推进循环（「时钟解耦」）===

    /// <summary>调试速度旋钮 —— 本原型 10 步/秒（1 步 = 0.1 秒，「时间基准」）。</summary>
    private readonly SpeedControl _speed = new(tickRatePerSecond: 10.0);

    /// <summary>本帧应推进的步数（单位是<b>步</b>，所以排空的门槛写 1.0）。</summary>
    private double _tickAccumulator;

    /// <summary>
    /// 推进循环 —— 倍率只乘在逻辑流速上，表现层攒够一步才推一步（「时钟解耦」「时间基准」）。
    ///
    /// <para><b>接线点④</b>：推进走 <c>_world.Advance(1)</c>。</para>
    ///
    /// <para><b>用 while 不用 if</b>：一帧的步数可能大于 1（高倍率或低帧率），
    /// 用 if 会把多出来的步数留在累积器里越积越多，于是倍率越高实际越慢。</para>
    /// </summary>
    public override void _Process(double delta)
    {
        if (_world is null) return;

        PollTimeKnobs();

        _tickAccumulator += _speed.TicksPerFrame(delta);
        while (_tickAccumulator >= 1.0 && !_runEnded)
        {
            _tickAccumulator -= 1.0;
            _world.Advance(1);
            _table.Sync(_world);
        }
    }

    /// <summary>速度旋钮直达 — eval 直达：<c>SetSpeed(20)</c> 快进 / <c>SetSpeed(0.25)</c> 慢放。</summary>
    public string SetSpeed(double multiplier)
    {
        _speed.SetMultiplier(multiplier);
        return $"speed={_speed.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)}x";
    }

    /// <summary>
    /// 单步直达 — eval/bridge 直达：走一步自动停，返回当前步号。
    ///
    /// <para><b>接线点⑤</b>：走 <c>_world.Advance(1)</c>。玩家按键与调试桥走的是这一个方法，
    /// 不各写一份：两条触发、同一件事两处代码之后，其中一份必然先漂。</para>
    /// </summary>
    public string StepDebug()
    {
        if (_world is null) return "no-run";
        _world.Advance(1);
        _table.Sync(_world);
        return $"tick={_world.Tick.ToString(CultureInfo.InvariantCulture)}";
    }

    // === 开一局 ===

    /// <summary>开局直达 — eval/bridge：换一颗种子重开一局，回开局后的快照。</summary>
    public string StartNewRun(int seed)
    {
        StartRun(new MistCityConfig { Seed = seed });
        return _world?.DebugSnapshot() ?? "start-failed";
    }

    /// <summary>换一局（同配置）。标题屏那个「重开一局」走它。</summary>
    private string RestartRun()
    {
        StartRun(_config);
        return $"restart seed={_config.Seed.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>开一局：换世界、清累积器、报给前端层。</summary>
    private void StartRun(MistCityConfig config)
    {
        _config = config;

        // 换局即弃旧账：先交还旧世界（Dispose 幂等），再造新的 —— 次序反了旧世界的订阅永远挂着。
        _world?.Dispose();
        _world = new MistCityWorld(config);

        _tickAccumulator = 0;
        _runEnded = false;
        _clickFirst = null;
        _front.CloseAll();
        _table.Reset();
        _table.Sync(_world);
        Log($"[MistCity] 开局 seed={config.Seed.ToString(CultureInfo.InvariantCulture)}");
        _front.NotifyRunStarted();
    }

    // === 调试桥（标准七动词由 DebugConsole 注册，这里只递本原型自己那几条） ===
    //
    // 用法（项目根执行；起局走 game start —— 它起前必探端口，有人应答就拒绝起第二局）：
    //   dotnet run --project src/Tools -- game start res://src/Prototypes/MistCity/Scenes/Main.tscn --port 7777
    //   dotnet run --project src/Tools -- debug ping
    //   dotnet run --project src/Tools -- debug eval --code "Main.DebugSnapshot()"
    //   dotnet run --project src/Tools -- game stop --port 7777     # 收尾：按端口停，复认无监听

    /// <summary>eval 脚本的 globals（Roslyn 脚本编译要可访问性，字段类型要具体）。</summary>
    public sealed class MistCityGlobals
    {
        /// <summary>主节点。</summary>
        public Main Main { get; set; } = null!;
    }

    private DebugBridgeHost? _bridge;

    /// <summary>
    /// 调试桥接线 —— 本原型动词名单（spec 的那一份）：
    /// <c>place</c> / <c>bind</c> / <c>unbind</c> / <c>combine</c> / <c>advance</c> / <c>snapshot</c>，
    /// 外加 <c>drag_to</c>（叠放那个手势）与 <c>click_card</c>（<b>调试口，界面上没有</b>）。
    ///
    /// <para><b>每一条都只转发既有方法</b>（「桥只转发」）：<c>drag_to</c> 转发的是
    /// <see cref="BeginDrag"/> / <see cref="MoveDrag"/> / <see cref="EndDrag"/> 三个，
    /// 与 <c>_UnhandledInput</c> 收到事件后调的是同一个方法。</para>
    ///
    /// <para><b>参数缺了当场抛</b>（<c>BridgeArgs</c> 的 <c>required: true</c>）：不兜 0。</para>
    /// </summary>
    private void SetupDebugBridge()
    {
        _bridge = DebugConsole.Attach(this, new MistCityGlobals { Main = this },
            ("advance", a => AdvanceSteps((int)a.Num("steps", required: true))),
            ("place", a => PlaceCard(a.Str("kind", required: true))),
            ("bind", a => BindCard(a.Str("card", required: true), a.Str("onto", required: true))),
            ("unbind", a => UnbindCard(a.Str("card", required: true))),
            ("combine", a => CombineCards(a.Str("a", required: true), a.Str("b", required: true))),
            ("click_card", a => ClickCardDebug(a.Str("card", required: true))),
            ("drag_to", a => DragToDebug(a.Str("from", required: true), a.Str("onto", required: true))),
            ("speed", a => SetSpeed(a.Num("multiplier", required: true))),
            ("pause", a => PauseToggle()),
            ("step", _ => StepDebug()),
            ("snapshot", _ => DebugSnapshot()),
            ("screenshot", a => CaptureScreenshot(a.Str("path", required: true))));

        // 合成输入（「行为可注入」那一半）—— 机制与「合成滞后一帧」那个坑在 InputInjector 的类注里。
        foreach (var (verb, handler) in InputInjector.Vocabulary(this, RecordSyntheticInput))
            _bridge?.Register(verb, handler);

        foreach (var (verb, handler) in DemoFactory.GugaFramework.I18n.LocaleVerbs.Vocabulary())
            _bridge?.Register(verb, handler);

        // 悬停拾取读出口：光标挪过去之后「拾到了谁」只有这里读得到。
        _bridge?.Register("hover_pick", _ => HoverPickDebug());
        Log($"[MistCity] 调试桥已接线（{(_bridge is null ? "宿主没建起来" : "词表看 debug verbs")}）");
    }

    // === 本原型的写路径（玩家点选与调试注入调的是这一批方法本身） ===

    /// <summary>推进 n 步 — eval/bridge 直达（用户故事 20：一次推完整局不等真实时间）。</summary>
    public string AdvanceSteps(int steps)
    {
        if (_world is null) return "no-run";
        _world.Advance(steps);
        _table.Sync(_world);
        return DebugSnapshot();
    }

    /// <summary>放一张牌上桌 —— 写路径之一（用户故事 18 的注入动词）。</summary>
    public string PlaceCard(string kind)
    {
        if (_world is null) return "no-run";
        var id = _world.Place(kind);
        _table.Sync(_world);
        return id;
    }

    /// <summary>派工：把一张牌绑到另一张上 —— 写路径之一（用户故事 19）。</summary>
    public string BindCard(string card, string onto)
    {
        if (_world is null) return "no-run";
        var text = _world.Bind(card, onto);
        _table.Sync(_world);
        return text;
    }

    /// <summary>解绑一张牌 —— 写路径之一（用户故事 6 / 19）。</summary>
    public string UnbindCard(string card)
    {
        if (_world is null) return "no-run";
        var text = _world.Unbind(card);
        _table.Sync(_world);
        return text;
    }

    /// <summary>合成：两张牌换一张新牌 —— 写路径之一。</summary>
    public string CombineCards(string a, string b)
    {
        if (_world is null) return "no-run";
        var text = _world.Combine(a, b);
        _table.Sync(_world);
        return text;
    }

    /// <summary>
    /// 悬停拾取读出口 —— 返回光标位、拾到的牌 id 与层号，供 <c>debug hover_pick</c> 断言。
    /// 与玩家拿起那张读同一个 <see cref="PickAt"/>，口径只有一份。
    /// </summary>
    public string HoverPickDebug()
        => PickReadout(GetViewport().GetMousePosition(), layer: 0, exclude: _heldCard);

    /// <summary>
    /// 拾取读出口（不依赖鼠标）—— 报视口坐标那个点往下数第 <paramref name="layer"/> 层是谁。
    ///
    /// <para><b>为什么要有这一个口</b>：<c>hover_pick</c> 读的是真光标，
    /// 而 <c>Input.WarpMouse</c> 与视口鼠标位置只在窗口模式有意义
    /// （机制与限制见 <c>docs/验证操作手册.md</c>）。无头下要逐字断言「第 0 层是谁、
    /// 第 1 层是谁」，就得有一个自己收坐标的同一份实现。两者共用
    /// <see cref="PickReadout"/>，口径只有一份。</para>
    /// </summary>
    /// <param name="x">视口坐标 x。</param>
    /// <param name="y">视口坐标 y。</param>
    /// <param name="layer">往下数第几层，从 0 起（0 = 最上面那张）。</param>
    /// <param name="exclude">排除的牌 id（null = 不排除）。玩家路径传的是正在被拖的那张。</param>
    public string PickLayerDebug(double x, double y, int layer, string? exclude)
        => PickReadout(new Vector2((float)x, (float)y), layer, exclude);

    /// <summary>
    /// 把一张牌挪到视口坐标并提到最上面 —— eval/bridge 直达：
    /// <c>CardMoveDebug("c05", 400, 300)</c>。
    ///
    /// <para><b>这一发只摆位置与次序</b>，不改任何逻辑状态（摆位是表现层数据）：
    /// 没它就没有办法在真机上摆出「两张压在一起」，而摞起来层级看得见
    /// 这一格只能靠这个局面验。</para>
    /// </summary>
    /// <param name="cardId">牌 id。</param>
    /// <param name="x">视口坐标 x。</param>
    /// <param name="y">视口坐标 y。</param>
    public string CardMoveDebug(string cardId, double x, double y)
    {
        _table.MoveTo(cardId, new Vector2((float)x, (float)y));
        _table.Raise(cardId);
        if (_world is not null) _table.Sync(_world);
        return CardRectOf(cardId);
    }

    /// <summary>
    /// 把一张牌叠到一个落点上 — eval/bridge 直达：
    /// <c>drag_to --from c07 --onto c05</c>、<c>--onto blank</c>、<c>--onto discard</c>。
    ///
    /// <para><b><c>onto</c> 收三种值</b>：一张牌 id（合成 / 派工）、<c>blank</c>（拿下来）、
    /// <c>discard</c>（丢弃）。三种值折成同一个 <see cref="DropTarget"/>，
    /// 之后走玩家那条路的同一发判定（<see cref="MistCityWorld.Drop(string, DropTarget)"/>）。</para>
    ///
    /// <para><b>只收牌 id 与落点短词，不收屏幕坐标</b>：坐标是表现层的派生值，
    /// 由布局算法算出来、随时会变。让测试脚本内嵌一份坐标参数，
    /// 那份参数在第一次改布局时就静默失效。所以两端的坐标都从牌面矩形与
    /// 落点那两个固定区域读。</para>
    ///
    /// <para><b>走的是玩家那条路</b>：调 <see cref="BeginDrag"/> / <see cref="MoveDrag"/> /
    /// <see cref="EndDrag"/> 三个方法本身，与 <c>_UnhandledInput</c> 收到事件后调的是同一个。
    /// 落点判定在逻辑层，玩家与注入答的是同一个问题。</para>
    ///
    /// <para><b>返回串沿用现有写路径的形状</b>：判成了就是 <c>drag_to &lt;那一句&gt;</c>
    /// （<c>combine ok …</c> / <c>bind ok …</c> / <c>unbind ok …</c> / <c>move ok …</c> /
    /// <c>discard ok …</c>），落空就是 <c>drag_to no-op &lt;原因&gt;</c>，返回串里没有 ok ——
    /// 「什么都没发生」本身是一件要能断言的事。</para>
    /// </summary>
    /// <param name="from">上面那张（拿起来的那张）。</param>
    /// <param name="onto">落点：一张牌 id、<c>blank</c> 或 <c>discard</c>。</param>
    public string DragToDebug(string from, string onto)
    {
        if (_world is null) return "no-run";

        // 两端都解析成「拾到的就是它」的点。解析不了就是命令写错：点名一张
        // 整个被压住的牌，玩家在画面上也够不着它。
        var press = PointOn(from, "from", _ => true);
        var at = PointOnTarget(onto, press);

        BeginDrag(press.X, press.Y);
        MoveDrag(at.X, at.Y);

        // 走 EndDrag：注入与玩家抬起调的是同一个入口，入口里再分派点击与叠放。
        return $"drag_to {EndDrag(at.X, at.Y)}";
    }

    /// <summary>
    /// 把 <c>onto=</c> 的那个值解析成视口坐标上的一点 —— <b>三种落点各走各的出处</b>。
    ///
    /// <para>牌 id 走牌面那个矩形（扫到一处落得上去的点）；<c>blank</c> 走桌面上
    /// 没有牌的那一片（按固定次序扫，同一局面解析出的点逐字一致）；
    /// <c>discard</c> 走弃牌堆那一块。</para>
    /// </summary>
    /// <param name="onto">落点：牌 id、<c>blank</c> 或 <c>discard</c>。</param>
    /// <param name="press">按下的那一点（空白处要离它至少一个分界，否则那一下算点击）。</param>
    private Vector2 PointOnTarget(string onto, Vector2 press)
    {
        var target = DropTarget.Parse(onto);
        return target.Kind switch
        {
            DropTargetKind.Discard => _table.DiscardRect.GetCenter(),
            DropTargetKind.Blank => BlankPointAway(press),
            _ => PointOn(target.CardId!, "onto", p => p.DistanceTo(press) >= DragThreshold),
        };
    }

    /// <summary>
    /// 找一处「没有牌、也不在弃牌堆里、离按下点够远」的点 —— <b>空白处的出处</b>。
    ///
    /// <para>按固定次序扫一格一格的点，所以同一个局面解析出的点逐字一致
    /// （布局算法随时会变，测试脚本不能内嵌一份坐标参数）。
    /// 一处都没有就当场抛：那是这个局面没有空白处可落，不猜一个点凑合。</para>
    /// </summary>
    /// <param name="press">按下的那一点。</param>
    private Vector2 BlankPointAway(Vector2 press)
    {
        for (var y = BlankScanTop; y <= BlankScanBottom; y += BlankScanStep)
        {
            for (var x = BlankScanLeft; x <= BlankScanRight; x += BlankScanStep)
            {
                var probe = new Vector2(x, y);
                if (probe.DistanceTo(press) < DragThreshold) continue;
                if (PickAt(probe, layer: 0, exclude: null) is not null) continue;

                // 弃牌堆那一块要跳过去：落在那儿是丢牌，不是拿下来。
                if (_table.IsOnDiscard(probe)) continue;
                return probe;
            }
        }

        throw new InvalidOperationException(
            "桌面上找不到一处没有牌的空白处（扫的是牌桌下面那一片）："
            + $"在册的牌有 {_table.OnScreenIds.Count} 张，把牌挪开再拖到空白处。");
    }

    /// <summary>
    /// 把一张牌的 id 解析成视口坐标上的一点 —— <b>注入点名哪张就操作哪张</b>。
    ///
    /// <para><b>为什么要扫一遍</b>：一张牌上面压了牌之后，它自己的中心被盖住了，
    /// 而中心是解析落点最自然的出处。只取中心的话，第二次往同一张牌上叠会叠到
    /// 刚放上去的那张 —— 注入点名的是哪张牌，画面上就得落到哪张牌。</para>
    ///
    /// <para><b>落点还要离按下点至少一个分界</b>：两张摞在一起时中心可能重合，
    /// 重合的位移到不了「点与拖的分界」那一档，叠放会被当成点击。</para>
    ///
    /// <para>中心优先，扫不到再按固定步长找，所以同一个局面解析出的点逐字一致。</para>
    /// </summary>
    /// <param name="cardId">要解析的牌。</param>
    /// <param name="role">报错消息里说它是哪一端。</param>
    /// <param name="ok">还要满足的那一条（落点要多远之类）。</param>
    private Vector2 PointOn(string cardId, string role, Func<Vector2, bool> ok)
    {
        var rect = RectOf(cardId, role);
        if (ok(rect.GetCenter()) && PickAt(rect.GetCenter(), layer: 0, exclude: null) == cardId)
            return rect.GetCenter();

        for (var y = rect.Position.Y + 2f; y < rect.End.Y; y += 4f)
        {
            for (var x = rect.Position.X + 2f; x < rect.End.X; x += 4f)
            {
                var probe = new Vector2(x, y);
                if (ok(probe) && PickAt(probe, layer: 0, exclude: null) == cardId) return probe;
            }
        }

        throw new InvalidOperationException(
            $"{role} {cardId} 整个被压在别的牌底下，找不到一处落得上去的点：先把它拿上来再叠。");
    }


    /// <summary>一张牌的屏幕矩形 —— 不在画面上当场抛（那是命令写错，不是落空）。</summary>
    private Rect2 RectOf(string cardId, string role)
        => _table.TryRectOf(cardId, out var rect)
            ? rect
            : throw new InvalidOperationException(
                $"{role} {cardId} 不在画面上：在册的牌是 [{string.Join(" / ", _table.OnScreenIds)}]。");

    /// <summary>拾取读数的那一句 —— 形状只有这一处，悬停读数与注入读数共用。</summary>
    private string PickReadout(Vector2 pos, int layer, string? exclude)
    {
        var picked = PickAt(pos, layer, exclude);
        return $"pick={(picked ?? "none")}"
            + $" layer={layer.ToString(CultureInfo.InvariantCulture)}"
            + $" mouse=({pos.X.ToString("0", CultureInfo.InvariantCulture)}"
            + $",{pos.Y.ToString("0", CultureInfo.InvariantCulture)})";
    }

    /// <summary>读数里那个坐标与位移的唯一格式化处（「double 与不变文化」）。</summary>
    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // === 叠放手势：按下 → 移动 → 抬起 ===
    //
    // 这一段拆成三个方法，真实的 _UnhandledInput 与 drag_to 注入调的是这三个本身。
    // 拆开是为了 agent 能把每一截分别验到 —— 接线断在三截里的任何一截，
    // 症状都像「手感不好」：抬不起来界面不响应，抬起判定失效牌粘在光标上，
    // 落点判定失效牌落回原位。没有分截读数，这三样分不出来。
    //
    // 拖动与点击的分界是 8 像素（固定像素值，不按牌面尺寸取比例）。
    // 落点判定在逻辑层（MistCityWorld.Drop），本类不另抄一份。
    // 界面上只有拖放一条路：点选两步那张手势已经下架，
    // 桥上那条 click_card 是调试口，调的是同一个落点判定方法。

    /// <summary>
    /// 点与拖的分界（像素）—— <b>位移小于它算没拖动</b>。
    ///
    /// <para><b>固定像素值</b>，不按牌面尺寸取比例：比例阈值在换分辨率时会漂，
    /// 而重放脚本要能逐字复现同一次操作。</para>
    ///
    /// <para><b>这一档留着不是为了点选</b>：它答的是「这一下算不算一次拖动」，
    /// 手抖几像素不算。不到这一档就松手，牌停在原处，不动任何东西。</para>
    /// </summary>
    private const float DragThreshold = 8f;

    /// <summary>
    /// 解析「空白处」要扫的那一片（视口坐标，牌桌下面那一条）。
    ///
    /// <para><b>开局牌摆在那一片上面</b>（牌桌顶边 110、卡面 176 高、两排到 482），
    /// 这一片扫过去一处都没有牌。扫的步长是固定值，同一局面解析出的点逐字一致。</para>
    /// </summary>
    private const float BlankScanTop = 872f;

    /// <summary>扫到这一行为止。</summary>
    private const float BlankScanBottom = 1040f;

    /// <summary>扫的左右边界。</summary>
    private const float BlankScanLeft = 32f;

    /// <summary>扫的右边界。</summary>
    private const float BlankScanRight = 1180f;

    /// <summary>扫的步长（像素）。</summary>
    private const float BlankScanStep = 16f;

    /// <summary>按下的那一点 —— 位移从它算起。</summary>
    private Vector2 _pressPos;

    /// <summary>光标最后到的那一点（读数与截图用）。</summary>
    private Vector2 _lastPos;

    /// <summary>位移有没有到分界 —— 到之前是点击，到之后才是拖动。</summary>
    private bool _dragging;

    /// <summary>
    /// 未处理输入：左键按下、移动、抬起 —— <b>这一格只把引擎事件翻译成三个方法</b>。
    ///
    /// <para><b>走 <c>InputEventMouseButton</c> 与 <c>InputEventMouseMotion</c> 而不是 action</c>：
    /// 这一发要的是指针在哪，而 <c>InputMap</c> 那条路只答「按下了没有」，不答「按在哪」。
    /// 输入合成（<c>debug input_click</c>）送进来的也是同一类事件，所以那半截验的是真那条路。</para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_world is null) return;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                Log(BeginDrag(down.Position.X, down.Position.Y));
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } up:
                // 前缀在这一格起：那一句只进日志，画面上只有牌动了。
                // 落空也只留日志里那一句，不弹提示也不抛（落空是玩家要试的那一下）。
                Log($"drop {EndDrag(up.Position.X, up.Position.Y)}");
                return;
            case InputEventMouseMotion motion:
                MoveDrag(motion.Position.X, motion.Position.Y);
                return;
        }
    }

    /// <summary>
    /// 按下 —— <b>拿起一张牌</b>。<b>玩家路径与 <c>drag_to</c> 注入调的是同一个方法</b>。
    ///
    /// <para><b>拾取固定取第 0 层（最上面那张）</b>：手里拿着的应该是最上面那张。
    /// 按空处拿不到牌，手上就空着。</para>
    /// </summary>
    /// <param name="x">视口坐标 x。</param>
    /// <param name="y">视口坐标 y。</param>
    /// <returns>拿起的是哪张（没拿到是 <c>held=none</c>）。</returns>
    public string BeginDrag(double x, double y)
    {
        if (_world is null) return "no-run";
        _pressPos = new Vector2((float)x, (float)y);
        _lastPos = _pressPos;
        _dragging = false;

        var picked = PickAt(_pressPos, layer: 0, exclude: _heldCard);
        if (picked is null)
        {
            Log("[MistCity] 按住空处：手上空着");
            return $"held=none at=({Fmt(x)},{Fmt(y)})";
        }

        _heldCard = picked;
        _table.HoldUp(picked, _pressPos);

        // 拿起时它占的那一块 —— 「落点就是手里这张」那一格按它判。
        // 记这一份是因为手里那张之后跟着光标走，放下时的位置说不出它是从哪儿被拿起来的。
        if (_table.TryRectOf(picked, out var at))
        {
            _heldOrigin = at;
            _heldOriginCard = picked;
        }

        return $"held={picked} at=({Fmt(x)},{Fmt(y)})";
    }

    /// <summary>
    /// 移动 —— <b>位移到分界之前什么都不动，到之后手里那张跟着光标走</b>。
    ///
    /// <para>不到分界不跟手是故意的：手抖几像素就把牌挪走的话，
    /// 「点一下」与「点歪一下」会得到两种结果，而玩家分不出自己手抖了。</para>
    ///
    /// <para><b>这一格不做目标高亮也不变暗</b>（玩家明确否掉过拖动时高亮）。</para>
    /// </summary>
    /// <param name="x">视口坐标 x。</param>
    /// <param name="y">视口坐标 y。</param>
    /// <returns>手里那张、到没到分界、位移多少（供 <c>debug eval</c> 逐字断言）。</returns>
    public string MoveDrag(double x, double y)
    {
        if (_world is null) return "no-run";
        _lastPos = new Vector2((float)x, (float)y);
        if (_heldCard is null) return $"held=none drag=no at=({Fmt(x)},{Fmt(y)})";

        var dx = _lastPos.X - _pressPos.X;
        var dy = _lastPos.Y - _pressPos.Y;
        var far = MathF.Sqrt((dx * dx) + (dy * dy)) >= DragThreshold;
        if (far) _dragging = true;
        if (_dragging) _table.MoveHeld(_lastPos);

        return $"held={_heldCard} drag={(far ? "yes" : "no")}"
            + $" dx={Fmt(dx)} dy={Fmt(dy)} at=({Fmt(x)},{Fmt(y)})";
    }

    /// <summary>
    /// 抬起 —— <b>不到分界什么都不发生，到分界才算叠放</b>。
    /// <b>玩家路径与注入调的是同一个方法</b>。
    ///
    /// <para><b>返回的是那一句本身，不带前缀</b>：前缀由调用点起 ——
    /// 玩家那一侧进日志（<c>drop …</c>），注入那一侧起 <c>drag_to …</c>。
    /// 前缀在这里写死的话，两条路径就得各调一个方法，接缝就分叉了。</para>
    ///
    /// <para><b>界面上只有拖放一条路</b>：不到分界就松手等于没发生过，
    /// 牌停在按下的那一处，挂接表与牌册一个字都不动。</para>
    /// </summary>
    /// <param name="x">视口坐标 x。</param>
    /// <param name="y">视口坐标 y。</param>
    /// <returns>这一步做了什么（人读的读数）。落空时说得出一句原因。</returns>
    public string EndDrag(double x, double y)
    {
        if (_world is null) return "no-run";

        var at = new Vector2((float)x, (float)y);
        var held = _heldCard;
        _heldCard = null;
        var dragged = _dragging;
        _dragging = false;

        if (held is null) return $"held=none at=({Fmt(at.X)},{Fmt(at.Y)})";

        // 叠着别的牌与否：落点那一摞里除手里那张以外最上面是谁。
        // 先算落点再放下：PutDown 要拿底下那张来对齐位置。
        var target = dragged ? PickAt(at, layer: 0, exclude: held) : null;
        var resting = _table.PutDown(target is not null, target);

        // 不到分界：这一下没有发生过什么。牌本来就没挪过，放下就回到原样。
        if (!dragged) return "没到分界：牌停在按下的那一处";

        // 三种落点折成一个 DropTarget，之后一律交给逻辑层那一个判定方法。
        // 弃牌堆先判：它是一块有位置的区域，落在它那一块就是丢弃，
        // 不看那块底下压没压着牌（牌能拖到弃牌堆上面去）。
        var dropTarget = LandingTargetAt(at, target);
        return DropHeldOnto(held, dropTarget, resting).Text;
    }

    /// <summary>
    /// 叠放那一截的读数 —— 手里有没有牌、到没到分界、光标在哪。
    /// 断在按下的那一截读 <c>held=none</c>，断在移动的那一截读 <c>drag=no</c>。
    /// </summary>
    public string DragStateDebug()
        => $"held={(_heldCard ?? "none")}"
           + $" press=({Fmt(_pressPos.X)},{Fmt(_pressPos.Y)})"
           + $" last=({Fmt(_lastPos.X)},{Fmt(_lastPos.Y)})"
           + $" dragging={(_dragging ? "yes" : "no")}";

    /// <summary>
    /// 桥上那条点选动词 —— <b>调试口，界面上没有对应的东西</b>。
    ///
    /// <para><b>两下造出一个与玩家两步操作等价的局面</b>：第一下记下上面那张，
    /// 第二下把它放到这一下点的那张上。</para>
    ///
    /// <para><b>第二下调的是玩家拖放用的同一个落点判定方法</b>
    /// （<see cref="DropHeldOnto"/> → <c>MistCityWorld.Drop</c>），不是抄一份：
    /// 玩家与 agent 答的是同一个问题，改一次规则只改一处。
    /// 有配方就合成，底下是产粮牌就派工，底下是房屋牌就住进去；
    /// 四样都不成就是落空，返回的那一句里没有 ok。</para>
    ///
    /// <para><b>界面上那条手势已经下架</b>（左键只有拖放一条路）。
    /// 这一口留着是因为它比 <c>drag_to</c> 好写：重放脚本不必先解析落点坐标。</para>
    /// </summary>
    /// <param name="cardId">第一下记上面那张，第二下记落点那张。</param>
    /// <returns>第一下回「记下了谁」，第二下回落点判定那一句话（与 <c>drag_to</c> 同一句）。</returns>
    public string ClickCardDebug(string cardId)
    {
        if (_world is null) return "no-run";

        if (_clickFirst is null)
        {
            _clickFirst = cardId;
            return $"click_card first={cardId}";
        }

        var from = _clickFirst;
        _clickFirst = null;
        return DropHeldOnto(from, DropTarget.Onto(cardId), _table.RectCenterOf(from)).Text;
    }

    /// <summary>
    /// 桥上那条点选动词记下的「第一张」—— <b>只给 <see cref="ClickCardDebug"/> 用</b>。
    ///
    /// <para><b>换局要清掉</b>：上一局记下的那张在新一局里已经不在牌册上，
    /// 留着的话下一局的第二下会去动一张不存在的牌。</para>
    /// </summary>
    private string? _clickFirst;

    /// <summary>
    /// 落点是哪一种 —— <b>拾取结果折成三种值里的一种</b>。
    ///
    /// <para><b>「落点就是手里这张」按拿起时那个位置判</b>，不按放下时的位置判：
    /// 手里那张跟着光标走，放下时它一定压在光标下面，拿放下时的位置判的话
    /// 「压在自己身上」永远是成立，空白处就永远走不到。
    /// 拖出去又拖回来（释放点仍在拿起时那块矩形内）与注入
    /// <c>onto=c03</c>（解析出的点就在 c03 拿起时那块矩形内）答的是同一格，
    /// 由逻辑层那一格落空接住 —— 表现层不另编一句 same-card，那会变成第二份判定。</para>
    /// </summary>
    /// <param name="at">落点（视口坐标）。</param>
    /// <param name="target">拾取结果（null = 这一摞里除手里那张以外没有牌）。</param>
    private DropTarget LandingTargetAt(Vector2 at, string? target)
    {
        if (_table.IsOnDiscard(at)) return DropTarget.Discard;
        if (target is not null) return DropTarget.Onto(target);
        return _heldOrigin.HasPoint(at) ? DropTarget.Onto(_heldOriginCard!) : DropTarget.Blank;
    }

    /// <summary>
    /// 把手里那张放到一个落点上 —— <b>玩家拖放、点选第二步与 <c>drag_to</c> 注入共用这一格</b>。
    ///
    /// <para><b>判定在逻辑层</b>（<c>MistCityWorld.Drop</c>）：一张牌先问配方再问能不能接上去，
    /// 空白处拆产线（没有承载者就是单纯移位），弃牌堆丢掉。表现层只把落点摆好，不自己判。</para>
    /// </summary>
    /// <param name="fromId">上面那张。</param>
    /// <param name="target">落点：一张牌 / 空白处 / 弃牌堆。</param>
    /// <param name="resting">手里那张停在哪（视口坐标）—— 摞底那张不在画面上时的退路。</param>
    private DropResult DropHeldOnto(string fromId, DropTarget target, Vector2 resting)
    {
        if (_world is null) return new DropResult(DropAction.NoOp, "no-run", "no-run");

        // 摞底那张现在在哪 —— 整摞合成之后它就不在画面上了，位置要提前记住。
        var stackBaseAt = StackBasePositionOf(target);

        var result = _world.Drop(fromId, target);
        _table.Sync(_world);

        // 合成之后那一摞都没了，新牌落在摞底那张原来的位置（整摞合成一次吃掉几张都算它）。
        // 摞底查不到才退回手里那张停下的位置。
        if (result.ResultCard is not null)
        {
            _table.MoveTo(result.ResultCard, stackBaseAt ?? resting);
            RaiseFreedRidersOn(result.ResultCard);
        }

        return result;
    }

    /// <summary>
    /// 把还压在新牌上、却不再挂任何牌的那些提到最上面 —— <b>它们要看得见</b>。
    ///
    /// <para><b>为什么这一格要有</b>：住户牌被整摞合成腾下来之后停在房屋原来的位置，
    /// 而新牌正好落在摞底那张的原位，两者几乎重合。不提上来的话玩家只看得见新牌，
    /// 被腾下来的那张只露一条边 —— 那张牌面上的「曾住在 cXX」正是玩家要看的
    /// （这间房上原本住过人），压在底下等于没印。</para>
    ///
    /// <para><b>只提「没有承载者」的牌</b>：还压在别人身上的那张是产线或住户的一部分，
    /// 它的层级由挂接决定，不在这一格另排。</para>
    /// </summary>
    /// <param name="cardId">合成出来的那一张。</param>
    private void RaiseFreedRidersOn(string cardId)
    {
        if (_world is null || !_table.TryRectOf(cardId, out var landed)) return;

        foreach (var id in _world.Cards.Ids.ToArray())
        {
            if (string.Equals(id, cardId, StringComparison.Ordinal)) continue;
            if (_world.Bindings.OntoOf(id) is not null) continue;
            if (!_table.TryRectOf(id, out var rect) || !rect.Intersects(landed)) continue;
            _table.Raise(id);
        }
    }

    /// <summary>
    /// 落点那一摞最底下那张现在在屏幕上哪 —— <b>落手之前问逻辑层</b>。
    ///
    /// <para><b>为什么要在落手之前问</b>：整摞合成之后摞底那张已被消耗掉，
    /// 它的牌面那一格也一并摘了，那时再问位置就晚了。</para>
    /// </summary>
    private Vector2? StackBasePositionOf(DropTarget target)
    {
        if (_world is null || target.Kind != DropTargetKind.Card) return null;

        var baseId = _world.StackBaseOf(target.CardId!);
        return baseId is not null && _table.TryRectOf(baseId, out var rect) ? rect.Position : null;
    }

    /// <summary>
    /// 视口坐标 → 命中矩形内第 <paramref name="layer"/> 层的那张牌。
    ///
    /// <para><b>按叠放次序取，不按离光标最近取</b>：自由平面上两张牌压在一起时，
    /// 最近的那张未必是最上面那张，按最近取会出现「我点的是这张，它响应了那一张」。</para>
    ///
    /// <para><b>排除值只有一个出处</b>：正在被拖的那张牌。不排除的话，玩家把牌原地往下一放
    /// 就会拾到自己，而配方表里有一条「柴 + 柴 = 田垄」，当场自己跟自己合。</para>
    /// </summary>
    /// <param name="screenPos">视口坐标。</param>
    /// <param name="layer">往下数第几层，从 0 起（0 = 最上面那张）。</param>
    /// <param name="exclude">排除的牌 id（null = 不排除）。</param>
    public string? PickAt(Vector2 screenPos, int layer = 0, string? exclude = null)
    {
        if (_world is null) return null;
        return _table.TryPick(screenPos, layer, exclude, out var cardId) ? cardId : null;
    }

    /// <summary>
    /// 正在被拖的那张牌 —— <b>拾取要排除它</b>。
    ///
    /// <para>按下那一截写这一格，抬起那一截清空。玩家拖放、悬停读数与注入读数都读它，
    /// 所以三条路径的排除值只有一份。</para>
    /// </summary>
    private string? _heldCard;

    /// <summary>拿起时手里那张占的那一块 —— 「落点就是手里这张」那一格按它判。</summary>
    private Rect2 _heldOrigin;

    /// <summary>与 <see cref="_heldOrigin"/> 对应的那张 id（空矩形那一格用不上它）。</summary>
    private string? _heldOriginCard;

    // === 状态快照 ===

    /// <summary>
    /// 某张牌在屏幕上占的矩形 — eval/bridge 直达：<c>CardRectOf("c01")</c>。
    ///
    /// <para><b>为什么要开这一个口</b>：拖放落点与合成输入要的是坐标。
    /// 坐标从哪来只有一处出处 —— 牌面那个矩形。让 agent 也从同一个口读，
    /// 才不必在测试脚本里把牌面尺寸抄一遍（抄一份就是下一次布局改动就要改两处）。</para>
    /// </summary>
    public string CardRectOf(string cardId)
        => _table.TryRectOf(cardId, out var rect)
            ? $"card={cardId} x={rect.Position.X.ToString(CultureInfo.InvariantCulture)}" +
              $" y={rect.Position.Y.ToString(CultureInfo.InvariantCulture)}" +
              $" w={rect.Size.X.ToString(CultureInfo.InvariantCulture)}" +
              $" h={rect.Size.Y.ToString(CultureInfo.InvariantCulture)}"
            : $"card={cardId} not-on-screen";

    /// <summary>
    /// 暂停 / 恢复直达 — eval/bridge 直达：<c>PauseToggle()</c> 返回当前那一格。
    ///
    /// <para><b>试玩时人需要「冻住看一会儿」</b>：产线的数字在爬、结算在倒数，
    /// 要指着一格跟人讲清楚就得先把时间按住。空格键走同一条路。</para>
    /// </summary>
    public string PauseToggle()
    {
        if (_world is null) return "no-run";
        TogglePause();
        return _world.IsPaused ? "paused" : "running";
    }

    /// <summary>状态快照 —— <b>接线点⑥</b>：转发 <c>_world.DebugSnapshot()</c>（显示即真相那份实现）。
    /// 用户故事 21：从快照里读出全部真值。
    /// </summary>
    public string DebugSnapshot() => _world?.DebugSnapshot() ?? "no-run";

    // === 出图复核（「落地配出图」）===

    /// <summary>视口截图 — eval/bridge 直达：<c>debug screenshot --path &lt;绝对路径&gt;</c>。</summary>
    public string CaptureScreenshot(string path) => ViewportCapture.SavePng(this, path);

    // === 离树交还 ===

    /// <summary>离树交还 —— 世界成对交还（<c>Dispose</c> 幂等，重复调不重复交）。</summary>
    public override void _ExitTree() => _world?.Dispose();

    /// <summary>日志双写：<c>GD.Print</c> 给人看，<c>RollingLog</c> 给 <c>debug logs</c> 读。</summary>
    protected static void Log(string message)
    {
        GD.Print(message);
        RollingLog.Write(message);
    }
}
