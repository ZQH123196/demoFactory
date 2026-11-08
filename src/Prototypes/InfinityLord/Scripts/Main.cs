#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Animation;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.FrontEnd;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.InputLayer;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Godot;
using Construction = DemoFactory.GugaFramework.Construction;
using Unit = DemoFactory.Prototypes.InfinityLord.Logic.Unit;

namespace DemoFactory.Prototypes.InfinityLord;

/// <summary>
/// 无限领主主场景 — 无状态表现层（只读表现节点）。
///
/// 设计思路：
/// - 每帧用 Observable.EveryUpdate 驱动 LogicWorld.Advance(1)（时间轴与现实帧率解耦：
///   想慢放/快进改这里一处的步数即可）
/// - 模型字典缓存单位/建筑节点，逻辑层事件流驱动增删，本脚本零游戏状态
/// - 空格键发布 EventBus.GamePaused，逻辑层订阅生效 — 顺便验证事件总线中台
/// - 玩家输入：左键点怪物/巢穴挂赏（间接控制唯一入口）；滚轮=相机缩放（GugaFramework 通用件
///   TopDownRig 接管视角），悬赏定价让位给 , / . 键
/// - 禁止在此直接修改逻辑层数据；只调用悬赏 API / Advance / SetPaused
///
/// 架构位置：三层响应式架构 — 无状态表现层 (UI/3D)
/// </summary>
public partial class Main : Node
{
    private LogicWorld _world = null!;
    private float _tickAccumulator;

    /// <summary>
    /// 当前那一座逻辑世界 —— <b>局内热替换之后是新的那一座</b>。
    ///
    /// <para>给 eval globals 的 <c>World</c> 现场取用（<see cref="InfinityLordGlobals.World"/>）：
    /// 那一份 globals 一局只建一次，所以这一格要是缓存引用，读档之后
    /// <c>eval World.*</c> 会一直答旧世界的话。字段本身仍是私有的，
    /// 表现层要改真值一律走 <c>LogicWorld</c> 的公开口（「表现层只读」）。</para>
    /// </summary>
    public LogicWorld CurrentWorld => _world;

    /// <summary>行为树接管：第一名游侠交给树管，其余英雄照旧走 LogicWorld 的决策链。
    /// 只接管一名 — 技术账见 docs/GugaFramework/行为树与跨语言共存.md。</summary>
    private HeroBrainBridge? _brainBridge;

    /// <summary>本局是否已接管过一名游侠。与上面那个字段分开记：引用须在桥离场时交还，
    /// 而「只接管一名」这道校验不跟着桥的生死放开。</summary>
    private bool _brainAttached;

    /// <summary>行为树场景（脚手架件，下划线前缀）。C# 改造换的是场景里三条叶子挂的脚本
    /// （<c>AI/Brain*Task.cs</c>），场景本身路径没动。</summary>
    private const string BrainScenePath = "res://src/Prototypes/InfinityLord/Scenes/_HeroBrain.tscn";

    /// <summary>调试速度旋钮（GugaFramework/Scheduling）— 倍率 × 逻辑频率 × delta = 本帧应推进的步数。</summary>
    private readonly SpeedControl _speed = new(tickRatePerSecond: 10.0); // 10 步/秒（0.1 秒一步）

    /// <summary>读法分派：概念名与读法都来自公共层，引擎那三个读数口在本件递进去。</summary>
    private readonly InputRead _read = new(name => Input.IsActionJustPressed(name), name => Input.IsActionPressed(name),
        name => Input.GetActionStrength(name));

    // === 节点引用（% 唯一名） ===
    private Node3D _worldRoot = null!;
    private Camera3D _camera = null!;
    private TopDownRig _rig = null!;
    private Label _tickLabel = null!;
    private Label _statsLabel = null!;
    private Label _pauseLabel = null!;

    // === 模型池 ===
    private readonly Dictionary<string, Node3D> _unitModels = new();
    private readonly Dictionary<string, Node3D> _buildingModels = new();
    private readonly Dictionary<string, Label3D> _buildingPlates = new();
    private readonly Dictionary<string, Node3D> _flagModels = new();
    private Label _hintLabel = null!;
    private Label _camLabel = null!;

    // === 切片5：行情提示与反馈 ===

    /// <summary>悬停行情标签：跟随鼠标显示目标威胁/建议价/已挂金额（行情公开 API 的可视化）。</summary>
    private Label _hoverLabel = null!;

    /// <summary>操作反馈标签：点击挂赏/加价的瞬时反馈（%HintLabel 覆盖债的修复 — 反馈不再挤占常驻提示）。</summary>
    private Label _feedbackLabel = null!;

    /// <summary>反馈剩余显示时间（秒），归零后清空反馈文本。</summary>
    private float _feedbackTimer;

    /// <summary>每次点击挂赏金额（Majesty 悬赏旗定价旋钮，, / . 切换）。</summary>
    private int _bountyAmount = 25;

    /// <summary>定价键位 action（滚轮已让给相机缩放，见 TopDownRig）— 绑定在项目设置 [input]。</summary>
    private const string ActionPriceUp = "bounty_price_up";

    // === MCP 试玩录制（重放脚本 · 调试基建计划，2026-09-23） ===

    /// <summary>
    /// 重放装配（常驻，未开录时只跑不记）。经 ReplayRecordStart/Stop eval 直达口开段落盘。
    ///
    /// 写路径唯一入口是 <see cref="ReplayAssembly.Inject"/>：玩家自己下的那一手与调试口注入的那一笔都走它，
    /// 于是两条路径同一份代码（→ 「桥只转发」）。词表与 args 规范化在
    /// <c>Scripts/Logic/ReplaySetup.cs</c>（全库唯一一份），「要不要记」在 <c>ReplayAssembly</c>。
    /// </summary>
    private ReplayAssembly _recorder = null!;

    /// <summary>开局种子（与 LogicWorld 构造一致 — 重放要 new 同种子世界）。</summary>
    private const int Seed = 42;

    /// <summary>项目根（落盘锚点 — 从 res:// 反解全局路径）。</summary>
    private static string ProjectDir => ProjectSettings.GlobalizePath("res://");

    /// <summary>
    /// 写路径唯一入口 — 走词表、按需入录、返回真实 ok。
    /// 表现层的排版与反馈不进这里：谁点的、点完给什么提示，留在各自的调用点。
    /// </summary>
    private bool Write(string cmd, string args) => _recorder.Inject(cmd, args);

    /// <summary>
    /// 合成输入的入录回调（2026-10-05 用户裁决）—— 把这一笔合成输入记进 Journal。
    ///
    /// <para><b>走的是同一条入录口</b>（<see cref="Write"/> → <c>_recorder.Inject</c>），
    /// 所以「合成输入」与「玩家真按的键」在账上不分家：一条 <c>SyntheticInputClick</c>
    /// 后面紧跟一条 <c>PlaceBuilding</c>，读账的人一眼看得出「点了哪」与「因此建了什么」。</para>
    ///
    /// <para><b>为什么不在 <c>InputInjector</c> 里直接调 <c>Inject</c></b>：那份是零游戏知识的
    /// 公共件，认得本原型的词表就等于公共层认得原型（「原型隔离」要挡的事）。
    /// 委托把这条缝留在原型侧，公共那一份只管「有一笔要记」。</para>
    ///
    /// <para><b>入录的是参数串、不重放动作</b>：重放端没有引擎也没有光标，这两条在那边一律跳过
    /// （机制与理由见 <c>ReplaySetup.SyntheticInput</c>）。</para>
    /// </summary>
    private void RecordSyntheticInput(InputInjector.InputKind kind, string args)
    {
        var cmd = kind switch
        {
            InputInjector.InputKind.Action => "SyntheticInputAction",
            InputInjector.InputKind.Click => "SyntheticInputClick",
            _ => null,
        };
        // 未开录时 Write 照跑只是不入录（ReplayAssembly 的口径），这里同款：动词总要走一遍词表
        // 才能拿到规范化 args，所以照样 Inject —— 它在未录时不会往账上添东西。
        if (cmd is not null) Write(cmd, args);
    }

    private const string ActionPriceDown = "bounty_price_down";

    /// <summary>点选容差的基准像素 — 在 rig.ReferenceDistance 那个机位下实测调好的手感值。</summary>
    private const float ClickTolerancePx = 48f;
    private const float HoverTolerancePx = 64f;

    /// <summary>
    /// 模型位移平滑刚度：逻辑层每 0.1 秒跳一步，固定远机位下看不出来，一旦拉近或锁定跟随
    /// 就是明显抖动 — 表现层用指数阻尼补齐（快进时按倍率放大刚度，保持同样跟手）。
    /// </summary>
    private const float UnitSmoothStiffness = 18f;

    /// <summary>当前锁定跟随的单位 Id（null = 自由观察）— 只有表现层知道，逻辑层零感知。</summary>
    private string? _followedUnitId;

    /// <summary>单位模型的可视部件引用（切片4：名牌/血条每帧需要刷新）。</summary>
    private readonly Dictionary<string, UnitVisual> _unitVisuals = new();

    // === 切片8「营建」：建筑放置模式状态（幽灵预览 → 点地落建） ===

    /// <summary>当前营建选中的建筑种类；null = 非营建模式（数字键 1/2/3 进入，右键/Esc 退出）。</summary>
    private BuildingKind? _placingKind;

    /// <summary>幽灵预览模型（跟随鼠标，红绿与落点只读裁决流；退出营建模式时销毁）。</summary>
    private Node3D? _ghostModel;

    /// <summary>幽灵的占地环（自由放置是圆形占地：半径与圆心都从裁决读，不是自己量的）。</summary>
    private MeshInstance3D? _ghostRing;

    /// <summary>幽灵名牌：显示名 + 能不能放 + 吸附后的落点 + 量化步长（步长从模块只读查询取）。</summary>
    private Label3D? _ghostLabel;

    /// <summary>
    /// 幽灵的半透明材质（裁决 ok 绿 / 拒红）。模型本体是真 .glb，材质只做红绿与半透 ——
    /// 幽灵是预览不是造型，改色不违反「造型进模型」（那条管的是建成品的职业/阵营差异）。
    /// </summary>
    private StandardMaterial3D? _ghostMaterial;

    /// <summary>
    /// 落点量化步长 —— <b>从模块只读查询取一次</b>（D29：gizmo 参数不许自己算）。
    /// 自由规则那份 gizmo 表没有格长与边界，量化步长就是本原型唯一用得上的那个数。
    /// </summary>
    private double _snapStep;

    // === 切片10「终局」：胜负判定 ===

    /// <param name="Model">造型实例本体 — 朝向只转它，头顶血条与名牌不跟着甩。</param>
    /// <param name="Anim">动画驱动器；模型没带 AnimationPlayer 时它的每个调用都静默不做事。</param>
    private sealed record UnitVisual(
        Label3D NameLabel, MeshInstance3D HealthBar, MeshInstance3D HealthBarBg,
        Node3D? Model, ModelAnimator Anim);

    /// <summary>
    /// 上一帧的平滑位置 — 速度由表现层自己量（逻辑层是 0.1 秒一步的离散时间轴，
    /// 没有"这一帧走了多快"的字段；位移平滑后的差商才是屏幕上真正的移动速度）。
    /// </summary>
    private readonly Dictionary<string, Vector3> _unitPrevPos = new();

    /// <summary>逐帧位移差商的低通结果（米每游戏秒）— 只喂动画挑档，不参与任何游戏判定。</summary>
    private readonly Dictionary<string, float> _unitSpeedEma = new();

    /// <summary>挑档速度的低通时间常数（秒）：要长过逻辑一步（0.1 秒）才抹得平跳格带来的瞬时尖峰。</summary>
    private const float SpeedEmaTau = 0.15f;

    public override void _Ready()
    {
        // 字体先装：缺省字体没有汉字字形，不装的话下面这些 HUD 中文上屏是整行空白
        // （实测记录见 SkillStageScene 类注）。装在读任何 Label 之前。
        DemoFactory.GugaFramework.I18n.FontSetup.Install();

        SetupPauseAction();

        SetupFastForwardAction();

        _worldRoot = GetNode<Node3D>("%World");
        _camera = GetNode<Camera3D>("%Camera3D");
        _tickLabel = GetNode<Label>("%TickLabel");
        _statsLabel = GetNode<Label>("%StatsLabel");
        _pauseLabel = GetNode<Label>("%PauseLabel");
        _hintLabel = GetNode<Label>("%HintLabel");
        _hoverLabel = GetNode<Label>("%HoverLabel");
        _feedbackLabel = GetNode<Label>("%FeedbackLabel");

        SetupCameraRig();
        SetupLighting();
        SetupHorizon();

        // 纯逻辑世界：固定种子保证开局完全可复现（调试利器）
        _world = new LogicWorld(randomSeed: 42);
        // 录制装饰器包住这座世界。**从第 0 步起就在录**（不在这里 EndSession）：树写的意图
        // 一开局就落账 —— 而它必须落账，因为树只活在表现层，重放端那个世界没有树。
        // 「逃兵」那一档的代价实测过：开录前被树驱动过的那名英雄会多抽几次世界的随机数，
        // 整条随机流从此错位（同一步差出 1.5 米，怪物位置也全错）。
        // 正式开录那一下只记起点（ReplayRecordStart），不清账。
        // 存档那两条的实现递进录制器：换世界要重建整个表现层，逻辑层拿不到它。
        _recorder = ReplaySetup.Recorder(_world, Seed,
            new ReplaySetup.SaveHooks(SaveSlotNow, LoadSlotNow));
        _cues = new SkillCues(OnUnitAttack);

        // 世界订阅集中在 WireWorld / UnwireWorld 那两件里（换世界是对象的事，不是节点的事 ——
        // 局内热替换时本节点留在树上，_ExitTree 一条都不会执行）。
        WireWorld();

        _camLabel = BuildCameraReadout();

        // 逻辑事件驱动建筑模型生成（只增不删：第切片建筑不毁灭）
        foreach (var building in _world.Buildings)
        {
            _buildingModels[building.Id] = SpawnBuildingModel(building);
        }

        GD.Print("[InfinityLord] 世界初始化完成，时间轴开始运转。");
        SetupDebugBridge();
        SetupFrontEnd();
        SetupWatchdog();
    }

    // === OOM 防线（看门狗 + 日志防爆；进程安全红线见 AGENTS.md「进程与运行时安全」） ===

    /// <summary>日志速率看门狗 — 日志喷涌就是内存喷涌的前兆，自动刹车（复盘防线 #2）。</summary>
    private LogRateWatchdog _watchdog = null!;

    /// <summary>看门狗接线：采样默认 godot.log，超阈 20KB/s → 暂停模拟 + 关渲染（HUD 保留）+ 提示。</summary>
    private void SetupWatchdog()
    {
        _watchdog = new LogRateWatchdog();
        AddChild(_watchdog);
        _watchdog.OnBreach = rate =>
        {
            _world.SetPaused(true);
            _worldRoot.Visible = false; // 渲染错误按帧×绘制调用产生 — 必须断渲染才断错误源
            GD.PushWarning($"[InfinityLord] 看门狗刹车：日志增速 {rate / 1024:F0}KB/s 超阈 — "
                + "已自动暂停模拟并关闭 3D 渲染（HUD 保留）。确认环境安全后可 resume + SetRendering(true)。");
        };
    }

    /// <summary>滚动日志包装 — 原型日志全部经此（环形缓冲有界，bridge logs 动词读尾部）。</summary>
    private static void Log(string message)
    {
        GD.Print(message);
        RollingLog.Write(message);
    }

    // === DebugBridge 接线（计划 Phase 1：eval 降级为兜底，调试通道自有化） ===

    /// <summary>
    /// eval globals（强类型，public 嵌套类 — Roslyn 脚本编译需要可访问性）—
    /// 脚本里 Main.SetSpeed(20) / World.Treasury 直达全量 C#，零 dynamic。
    /// 字段就是调试会话的常用入口（主节点 + 逻辑世界）。
    /// </summary>
    public sealed class InfinityLordGlobals : IEvalImports
    {
        /// <summary>主节点（Debug 直达 API 全在这上面）。</summary>
        public Main Main { get; set; } = null!;

        /// <summary>
        /// 逻辑世界（LINQ 查 Units/Buildings/Flags 用）。
        ///
        /// <para><b>每次求值现读 <see cref="Main.CurrentWorld"/>，不缓存引用</b>。
        /// 局内热替换之后 <c>Main</c> 上的 <c>_world</c> 换了对象，而这一份 globals 是
        /// <c>SetupDebugBridge</c> 建一次就用整局的 —— 缓存引用的话，<c>eval World.Tick</c>
        /// 读的是<b>已交还的旧世界</b>，读数看着还有理有据（它是那一局真的走到的步号），
        /// 于是「读档之后世界到底退没退回去」只能靠另一发读数才发现。
        /// 实测踩过：<c>DebugSnapshot</c> 念新世界（step=2015），<c>World.Tick</c> 念旧世界（4031）。
        /// 公共层那份机制记在 <c>DebugConsole</c> 类注第 ③ 条（换 globals 的通路），
        /// 这里是同一条问题更省的一解：不换 globals，让它自己跟着换。</para>
        /// </summary>
        public LogicWorld World => Main.CurrentWorld;

        /// <summary>
        /// 本原型的 eval 脚本额外导入哪些命名空间 —— 2026-09-28 从
        /// <c>GugaFramework/DebugBridge/DebugBridgeEval.cs</c> 里那句写死的位置搬回来：
        /// 桥的公共层此前认得 <c>DemoFactory.Prototypes.InfinityLord.Logic</c> 这个原型命名空间。
        /// 脚本里 <c>World.Units</c> 之类要能按短名取，靠的就是这一行。
        /// </summary>
        public IReadOnlyList<string> EvalImports { get; } = ["DemoFactory.Prototypes.InfinityLord.Logic"];
    }

    /// <summary>桥接宿主（未配置端口时内部不启动监听 — 默认关，零开销）。</summary>
    private DebugBridgeHost? _bridge;

    /// <summary>
    /// 技能 cue（表现层在技能层那一侧的<b>唯一</b>订阅者，原型侧三件里的第三件）。
    /// 它只回答「谁出手了」，动作怎么演在这一侧（<see cref="OnUnitAttack"/>）。
    /// 字段初始化不能引用实例方法，所以在 <c>_Ready</c> 里装配、在 <c>_ExitTree</c> 里交还。
    /// </summary>
    private SkillCues _cues = null!;

    /// <summary>
    /// 接线 DebugBridge。标准七动词（ping / verbs / targets / call / eval / evaldiag / logs）
    /// 与日志读口都在 <see cref="DebugConsole.Attach"/> 那份里（那里同时是整条桥读参数的唯一出口
    /// <see cref="BridgeArgs"/> 的落点），本函数只递无限领主自己的 11 条：重放三钩子
    /// （inject / snapshot / advance）＋ 操控台八条（speed / pause / resume / step /
    /// snapshot_debug / mark / record_start / record_stop）。
    ///
    /// 一份协议贯通三个消费者（dotnet test / 重放 / 真机 bridge）。接线纪律的措辞在
    /// AGENTS.md 「桥只转发」 与 5.12，词表两边同加那条在「写路径全录」（此处只放指针，不复述）。
    /// <c>inject</c> 的词表唯一事实源是 <c>Scripts/Logic/ReplaySetup.cs</c>：
    /// <see cref="JournalDispatch"/>、测试端、离线取证 CLI 全部转发到它。
    /// </summary>
    private void SetupDebugBridge()
    {
        _bridge = DebugConsole.Attach(this, new InfinityLordGlobals { Main = this },
            // --- 重放三钩子 ---
            ("inject", a => JournalDispatch(a.Str("cmd", required: true), a.Str("args"))),
            ("snapshot", _ => _world.ReplaySnapshot()),
            ("advance", a => AdvanceToTickDebug((long)a.Num("tick", required: true))),

            // --- 操控台八动词 ---
            ("speed", a => SetSpeed(a.Num("multiplier", required: true))),
            ("pause", _ => PauseReadout(paused: true)),
            ("resume", _ => PauseReadout(paused: false)),
            ("step", _ => StepDebug()),
            ("snapshot_debug", _ => DebugSnapshot()),
            ("mark", a => ReplayMark(a.Str("label") is { Length: > 0 } label ? label : "t?")),
            ("record_start", _ => ReplayRecordStart()),
            ("record_stop", a => ReplayRecordStop(
                a.Str("name") is { Length: > 0 } name ? name : "bridge-record")),

            // --- 出图（「落地配出图」：表现层动过的部分要看得见） ---
            // 一行转发公共层那一份截图件（照 _Template 与巨兽竞技场的现成写法），
            // 本类不自己拼 Viewport 与路径处理。
            ("screenshot", a => CaptureScreenshot(a.Str("path", required: true) ?? "")));

        // --- 输入合成（「行为可注入」那一半） ---
        // 四条：input_action / input_click / input_hover / input_read。Attach 之后单独注册：
        // 它的词表参数是 `params`，`..` 展开只在集合初始化里合法，参数列表里不认。
        // 机制、四条实测读数与「合成滞后一帧」那个坑在 InputInjector 的类注里。
        //
        // 第二个参数是把合成输入记进 Journal 的委托（2026-10-05 用户裁决）。
        // 动词名与参数编码全在这一侧（ReplaySetup），公共那一份不认得本原型的词表。
        foreach (var (verb, handler) in InputInjector.Vocabulary(this, RecordSyntheticInput))
            _bridge?.Register(verb, handler);

        // --- i18n 两动词（「行为可注入」在翻译上的那一格）---
        // set_locale 切语言、locale_probe 读当前语言/字体链/某个键的取字结果。
        // 切完配一发 debug screenshot，出图就能直接读出目标语言那行长什么样 ——
        // 「键在表里」是静态事实，「切过去真的变了」只能真跑一次才证明得了。
        // 机制与接法见 GugaFramework/I18n/LocaleVerbs 类注。
        foreach (var (verb, handler) in DemoFactory.GugaFramework.I18n.LocaleVerbs.Vocabulary())
            _bridge?.Register(verb, handler);

        // --- 3D 拾取读出口（上面那组 input_hover 的读数） ---
        // 光标挪过去之后「拾到了谁」只有这里读得到：悬停标签那份文本给人看，这行给 agent 断言。
        // 落在输入合成之后是因为它服务的就是那条链 —— 移了光标却读不到拾取结果，
        // 「3D 拾取」就只能靠出图人眼看（机制与窗口模式限制见 HoverPickDebug 的类注）。
        _bridge?.Register("hover_pick", _ => HoverPickDebug());

        // --- 存档读数 ---
        // 写档与读档那两条本身已经在 inject 里（走 ReplaySetup 的词表，不进 Journal）。
        // 这一发只读：槽位清单、每槽的时刻与字节数、当前世界步号，一次读全。
        // 单独给一发的理由是「写入是否成功」只有断言读得动 —— 屏幕上的反馈条留不住时刻。
        _bridge?.Register("save_readout", _ => SaveReadout());

        GD.Print($"[InfinityLord] 调试桥已接线（{_bridge?.Name ?? "宿主没挂上"}；"
                 + "没配 DEBUG_BRIDGE_PORT 时它根本不监听 —— 出货零开销）");
    }

    /// <summary>
    /// 出图那一发动词的方法体 —— 一行转发公共层的 <see cref="ViewportCapture"/>：
    /// 目录会自动建、漏写扩展名自动补 <c>.png</c>（那一份口径在公共层，原型不另写一遍）。
    /// </summary>
    public string CaptureScreenshot(string path) => ViewportCapture.SavePng(this, path);

    /// <summary>
    /// 暂停 / 恢复那两发动词的读数：转发自已有的 <see cref="PauseDebug"/> / <see cref="ResumeDebug"/>
    /// 之后报「现在第几步」（显示即真相 —— 拧完旋钮要能立刻读到世界认哪个状态）。
    /// 两行文本只差一个词，收成一座是因为桥读数要拿去做前后对照。
    /// </summary>
    private string PauseReadout(bool paused)
    {
        if (paused)
        {
            PauseDebug();
        }
        else
        {
            ResumeDebug();
        }
        return $"{(paused ? "paused" : "resumed")} tick={_world.Tick}";
    }

    /// <summary>
    /// inject 词表分发器 — 转发到 <c>Scripts/Logic/ReplaySetup.cs</c>（全库唯一那份词表），
    /// 经 <see cref="Write"/> 走同一条写路径：开录时自动入录，未开录时只跑不记。
    /// bridge 与「玩家自己下的那一手」共用这个入口，两条路径不再是两回事（→ 「桥只转发」）。
    ///
    /// 失败一律返 false（未知命令同款），不抛 —— 录制的 ok 是权威，重放端要能重放任何录到的东西。
    /// </summary>
    private bool JournalDispatch(string cmd, string args)
    {
        if (!ReplaySetup.VerbNames.Contains(cmd))
        {
            GD.PushWarning($"[DebugBridge] inject 未知命令 {cmd}（词表在 Scripts/Logic/ReplaySetup.cs）");
        }

        return Write(cmd, args);
    }

    /// <summary>
    /// 登记公共概念 <c>input_toggle_pause</c> 的键位 —— 菜单键（Esc）。
    ///
    /// <para>概念名是公共的、绑哪个键是本原型的内容决定，这里只做后一件。
    /// 已存在就原样保留（项目设置里手写的绑定优先，代码只兜底）。</para>
    /// </summary>
    private static void SetupPauseAction()
    {
        var action = InputConcept.TogglePause.Name;
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventKey { PhysicalKeycode = Key.Escape };
        if (InputMap.ActionHasEvent(action, ev)) return;
        InputMap.ActionAddEvent(action, ev);
    }

    /// <summary>
    /// 登记调试旋钮 <c>toggle_fast_forward</c> 的键位（F）。
    ///
    /// <para>这是调试概念不是玩法键位，所以不进项目设置的 <c>[input]</c> 段，就地自建。
    /// 照 <c>BeastArena/Main.cs</c> 那个 <c>EnsureAction</c> 的形状：已存在就原样保留，
    /// 代码只兜底。</para>
    /// </summary>
    private static void SetupFastForwardAction()
    {
        if (!InputMap.HasAction("toggle_fast_forward")) InputMap.AddAction("toggle_fast_forward");
        var ev = new InputEventKey { PhysicalKeycode = Key.F };
        if (InputMap.ActionHasEvent("toggle_fast_forward", ev)) return;
        InputMap.ActionAddEvent("toggle_fast_forward", ev);
    }

    public override void _Process(double delta)
    {
        // 暂停 / 恢复 —— 公共概念 input_toggle_pause，绑菜单键（Esc，见 SetupPauseAction）。
        // 发布到全局总线（逻辑层与表现层都各自订阅生效）。
        // 营建模式下这一键归「取消营建」（_UnhandledInput 那一处读同一个菜单键），
        // 两个模式互斥，所以这一处让开 —— 否则按一次 Esc 会既取消营建又切暂停。
        if (_placingKind is null && _read.Read(InputConcept.TogglePause))
        {
            GugaFramework.EventBus.EventBus.GamePaused.Publish(
                new GugaFramework.EventBus.Events.GamePaused(!_world.IsPaused));
        }

        // , / . 调悬赏定价（Majesty 玩家唯一的策略旋钮之一）— 滚轮这一全局手势已让给相机缩放。
        // 走 InputMap action 而非裸键轮询：MCP 的 input_action 注不进裸键（复盘坑 4）
        if (Input.IsActionPressed(ActionPriceUp))
        {
            _bountyAmount += (int)(delta * 40) + 1;
        }
        else if (Input.IsActionPressed(ActionPriceDown))
        {
            _bountyAmount = Math.Max(5, _bountyAmount - ((int)(delta * 40) + 1));
        }
        _bountyAmount = Math.Min(_bountyAmount, Math.Max(5, _world.Treasury));

        // F 键循环切换速度档（实时 → 5x → 1x）— 人的调试手感旋钮；AI 用 eval SetSpeed 精确拧
        // 复盘坑 4 修复：走 InputMap action（IsActionJustPressed 可被 MCP input_action 注入）
        if (Input.IsActionJustPressed("toggle_fast_forward"))
        {
            var next = _speed.Multiplier switch
            {
                1.0 => 5.0,
                _ => 1.0,
            };
            _speed.SetMultiplier(next);
            Log($"[InfinityLord] 速度切换：{next}x");
        }

        // 切片8 营建：数字键 1/2/3 选建筑类型进入放置模式（互斥切换，再按同键退出）
        if (Input.IsActionJustPressed("place_market"))
        {
            TogglePlacing(BuildingKind.Market, Loc.T("市场"));
        }
        if (Input.IsActionJustPressed("place_tavern"))
        {
            TogglePlacing(BuildingKind.Tavern, Loc.T("酒馆"));
        }
        if (Input.IsActionJustPressed("place_apothecary"))
        {
            TogglePlacing(BuildingKind.Apothecary, Loc.T("药店"));
        }

        // 切片9 职业建筑即招募：数字键 4/5 选行会（翻倍计价在反馈中显示当前价）
        if (Input.IsActionJustPressed("place_guild_warrior"))
        {
            TogglePlacing(BuildingKind.GuildWarrior, Loc.T("战士公会"));
        }
        if (Input.IsActionJustPressed("place_guild_ranger"))
        {
            TogglePlacing(BuildingKind.GuildHall, Loc.T("游侠公会"));
        }

        // 时间轴推进：暂停时 Advance 内部空转（逻辑冻结），帧率照常渲染
        // 速度旋钮：倍率 × 10 步/秒 × delta = 本帧应推进的步数（浮点累积，帧率解耦）
        if (!_world.IsPaused)
        {
            _tickAccumulator += (float)_speed.TicksPerFrame(delta);
            // 攒进来的单位是「步」⇒ 排空的门槛是 1 步（1 步 = 0.1 秒游戏时间，10 步/秒）。
            // 这里曾经写的是 0.1：等于每 0.1 步就当一步走，1x 名义实时、实际十倍速。
            while (_tickAccumulator >= 1f)
            {
                _tickAccumulator -= 1f;
                _world.Advance(1);
            }
        }

        // 切片5：鼠标悬停行情提示（拾取逻辑与点击一致 — 最近邻屏幕距离匹配）
        UpdateHover();

        // 切片5：反馈标签计时消退
        if (_feedbackTimer > 0f)
        {
            _feedbackTimer -= (float)delta;
            if (_feedbackTimer <= 0f)
            {
                _feedbackLabel.Text = "";
                _feedbackLabel.Visible = false;
            }
        }

        RenderUnits((float)delta);
        RenderFlags();
        RenderBuildingPlates();
        RenderGhost();
        RenderHud();
    }

    /// <summary>数字键切换营建模式：选同类键再按 = 取消；换类键 = 切换预览。</summary>
    private void TogglePlacing(BuildingKind kind, string label)
    {
        if (_placingKind == kind)
        {
            ExitPlacingMode(Loc.T("营建取消"));
            return;
        }
        _placingKind = kind;
        // 换种类就重建幽灵：模型是蓝图带来的真 .glb（「真模型」），一份幽灵装不下两种造型。
        // 旧的那只先收掉 —— 引用交还按「离树交还」，字段与节点的生命周期分开管。
        if (_ghostModel is not null)
        {
            _ghostModel.QueueFree();
        }
        _ghostModel = BuildGhostModel(kind);
        _worldRoot.AddChild(_ghostModel);
        ShowFeedback(Loc.Fmt("营建：{0} ${1}（左键落建 · 右键取消）", label, _world.NextPlaceCost(kind)));
    }

    /// <summary>退出营建模式：推 null 收幽灵（取消防幽灵由模块那个出口发，不在本类自己判断）+ 清状态。</summary>
    private void ExitPlacingMode(string reason)
    {
        _placingKind = null;
        _world.Construction.Hover.Clear();
        if (_ghostModel is not null)
        {
            _ghostModel.QueueFree();
            _ghostModel = null;
        }
        _ghostRing = null;
        _ghostLabel = null;
        _ghostMaterial = null;
        ShowFeedback(reason);
    }

    /// <summary>
    /// 幽灵预览每帧<b>只推手势</b>：能不能放 / 落在哪 / 占多大 / 什么角度全部从裁决流读回来
    /// （<see cref="OnHoverVerdict"/>，D27~D29）。
    ///
    /// <para>迁前这里是「每帧自己算一遍合法性」（那份表现层合法性镜像，已删）：它与逻辑层的
    /// 判定靠注释维持同源，会漂，而且只能给出「合法/不合法」，说不出是哪一条原因。这一侧现在只做
    /// 一件不涉及判定的事：把鼠标射线打在地上取候选点。手势字面没变时模块连校验都不跑，吸附到
    /// 同一处时连事件都不发（D27 的两层去重）。</para>
    /// </summary>
    private void RenderGhost()
    {
        if (_placingKind is not { } kind || _ghostModel is null)
        {
            return;
        }

        var ground = ScreenToGround(_ghostModel.GetViewport().GetMousePosition());
        _world.Construction.Hover.Push(new Construction.GestureSpec(
            LogicWorld.BlueprintIdOf(kind),
            new System.Numerics.Vector2(ground.X, ground.Z),
            Orientation: 0));
    }

    /// <summary>
    /// 裁决流落地：幽灵的落点、占地、红绿与名牌全部读这一条，<b>不第二次计算</b>（D29）。
    /// 参数为 null = 这一轮没有候选点了（推 null 与 Clear 同一个出口），把幽灵收起来。
    ///
    /// <para>金库那一半留在本类：模块不认识经济（D3），「买不买得起」是原型的话，所以它与裁决
    /// 是两个来源的与，不是同一份判定的两份算 —— 迁前那份镜像把两者揉在一个表达式里，那才是漂的地方。</para>
    /// </summary>
    private void OnHoverVerdict(Construction.HoverVerdict? verdict)
    {
        // 幽灵只在营建模式里存在（两处同生同灭），所以「没在营建模式」这一条一并挡在这里。
        if (_placingKind is not { } kind || _ghostModel is null)
        {
            return;
        }

        if (verdict is null)
        {
            _ghostModel.Visible = false; // 取消防幽灵
            return;
        }

        _ghostModel.Visible = true;
        _ghostModel.Position = new Vector3(verdict.Position.X, 0, verdict.Position.Y);

        // 圆形占地：圆心就是落点，半径是模块判 TooClose 用的那一个 —— 环画的就是它判的那块地。
        if (verdict.Circle is { } circle && _ghostRing is not null)
        {
            _ghostRing.Scale = new Vector3((float)circle.Radius, 1f, (float)circle.Radius);
        }

        var cost = _world.NextPlaceCost(kind);
        var affordable = cost <= _world.Treasury;
        var legal = verdict.Ok && affordable;
        _ghostMaterial!.AlbedoColor = legal
            ? new Color(0.3f, 1f, 0.4f, 0.5f)
            : new Color(1f, 0.25f, 0.2f, 0.5f);

        if (_ghostLabel is not null)
        {
            // 两处不合法各有各的说法：模块判的那条给封闭原因，金库那条给价 —— 分开说，
            // 不把「钱不够」硬塞进 PlacementReason（经济不进模块，D3）。
            var note = !verdict.Ok
                ? GhostReason(verdict.Reason)
                : affordable ? Loc.T("可放") : Loc.Fmt("金库不足（要 ${0}）", cost);
            _ghostLabel.Text = Loc.Fmt(
                "{0} · {1} · 落点 ({2:0.00}, {3:0.00}) · 步长 {4:0.####}",
                KindDisplayName(kind), note, verdict.Position.X, verdict.Position.Y, _snapStep);
        }
    }

    /// <summary>
    /// 拒绝原因的中文读法 —— 只给玩家真会撞上的那几条，其余照枚举名印。
    /// 封闭枚举只有这一份（D6），翻译一份就够：为「显得完整」把七条都编一遍，等于多一份
    /// 要跟着模块同步的表，而它在 Journal 与快照里一律按枚举名逐字比。
    /// </summary>
    private static string GhostReason(Construction.PlacementReason reason) => reason switch
    {
        Construction.PlacementReason.TooClose => Loc.T("距离过近"),
        Construction.PlacementReason.NotBuildable => Loc.T("这一种不可营建"),
        Construction.PlacementReason.UnknownBlueprint => Loc.T("蓝图不在册"),
        _ => reason.ToString(),
    };

    /// <summary>屏幕坐标 → 地面（y=0 平面）世界点。落建与幽灵共用这一份反投，避免两处各写一遍。</summary>
    private Vector3 ScreenToGround(Vector2 screenPos)
    {
        var origin = _camera.ProjectRayOrigin(screenPos);
        var dir = _camera.ProjectRayNormal(screenPos);
        return origin + dir * ((-origin.Y) / dir.Y);
    }

    /// <summary>
    /// 悬停行情：检测鼠标下的怪物/巢穴，显示威胁值/建议价/已挂金额（切片3 行情 API 可视化）；
    /// 切片7：悬停英雄显示士气与低士气惩罚说明（「为什么没人接单」的可读性投资）。
    /// 点选口径与左键完全共用一套（容差放宽到 64px 基准，便于悬停）。
    /// </summary>
    private void UpdateHover()
    {
        var mousePos = _hoverLabel.GetViewport().GetMousePosition();
        var radius = TolerancePx(HoverTolerancePx);

        // 全部单位（含英雄 — 切片7 悬停读士气）→ 全部建筑（巢穴看行情，自家行会看升级价）
        var pickedId = PickUnit(mousePos, radius, u => u.Health > 0)
            ?? PickBuilding(mousePos, radius, b => b.Health > 0);

        if (pickedId is null)
        {
            _hoverLabel.Visible = false;
            return;
        }

        // 玩家方建筑：升级报价（与 UpgradeGuild 扣款同源 — 显示即真相）
        var ownBuilding = _world.Buildings.FirstOrDefault(b => b.Id == pickedId && b.Owner == Logic.Faction.Hero);
        if (ownBuilding is not null)
        {
            var cost = _world.NextUpgradeCost(ownBuilding.Id);
            _hoverLabel.Visible = true;
            _hoverLabel.Text = cost > 0
                ? Loc.Fmt("{0} Lv.{1} · 升级 ${2} → 产骑士（左键确认）", DisplayName(ownBuilding), ownBuilding.Level, cost)
                : Loc.Fmt("{0} Lv.{1} · 无可升级项", DisplayName(ownBuilding), ownBuilding.Level);
            _hoverLabel.Position = mousePos + new Vector2(16, 24);
            return;
        }

        // 切片7：英雄悬停 — 显示士气（与勇气公式惩罚联动）与伤势需求提示
        var hero = _world.Units.FirstOrDefault(u => u.Id == pickedId && u.Side == Logic.Faction.Hero);
        if (hero is not null)
        {
            _hoverLabel.Visible = true;
            var moraleNote = hero.Morale < LogicWorld.MoraleDrinkThreshold
                ? Loc.Fmt(" · 低士气×{0:F1}（想去酒馆）",
                    1f + LogicWorld.MoralePenaltyMax * (LogicWorld.MoraleDrinkThreshold - hero.Morale) / LogicWorld.MoraleDrinkThreshold)
                : Loc.T(" · 士气高昂");
            var woundNote = hero.Health < hero.MaxHealth * LogicWorld.PotionSeekRatio ? Loc.T(" · 想买药剂") : "";
            _hoverLabel.Text = Loc.Fmt("{0} 士气 {1}{2}{3}", Loc.T(hero.Name), hero.Morale, moraleNote, woundNote);
            _hoverLabel.Position = mousePos + new Vector2(16, 24);
            return;
        }

        var threat = _world.EstimateThreat(pickedId);
        if (threat is null)
        {
            _hoverLabel.Visible = false;
            return;
        }

        var activeBounty = _world.Flags
            .Where(f => !f.Settled && f.TargetId == pickedId)
            .Sum(f => f.Gold);

        _hoverLabel.Visible = true;
        _hoverLabel.Text = Loc.Fmt("威胁 {0:F0} · 建议 ${1}", threat, _world.SuggestedPrice(pickedId))
            + (activeBounty > 0 ? Loc.Fmt(" · 已挂 ${0}", activeBounty) : "");
        _hoverLabel.Position = mousePos + new Vector2(16, 24);
    }

    /// <summary>
    /// 3D 拾取的读出口 —— <c>input_hover</c> 移了光标之后，拾到了谁只有这里能读。
    ///
    /// <para><b>为什么需要它</b>：<see cref="UpdateHover"/> 每帧按光标位做 3D 拾取，结果只写进
    /// 悬停标签的文本（表现层）。合成输入把光标挪到某个模型上之后，画面上确实变了，
    /// 但「拾到的到底是不是那个模型」没有任何可断言的读数 —— 只能靠出图人眼看。
    /// ⇒ 这一行把同一个 <c>pickedId</c> 记下来供桥读，**不新增判定、不改玩家路径**：
    /// 拾取的口径仍是 <see cref="PickUnit"/> / <see cref="PickBuilding"/> 那一份，
    /// 悬停标签照旧照它显示，两边读的是同一个值（「显示即真相」）。</para>
    ///
    /// <para><b>只在窗口模式下有意义</b>：合成 <c>InputEventMouseMotion</c> 改不了视口鼠标位置
    /// （无头恒 (0,0)），<c>Input.WarpMouse</c> 又只在窗口模式成立 —— 所以悬停拾取整条链
    /// 在无头下恒为空，这不是缺陷，机制见 <c>InputInjector</c> 类注事实 ③④。</para>
    /// </summary>
    public string HoverPickDebug()
    {
        var pos = _hoverLabel.GetViewport().GetMousePosition();
        var radius = TolerancePx(HoverTolerancePx);
        var picked = PickUnit(pos, radius, u => u.Health > 0)
            ?? PickBuilding(pos, radius, b => b.Health > 0);
        return $"pick={picked ?? "none"} mouse=({pos.X.ToString("0", CultureInfo.InvariantCulture)},"
               + $"{pos.Y.ToString("0", CultureInfo.InvariantCulture)}) tolerance={radius.ToString("0", CultureInfo.InvariantCulture)}"
               + $" hoverVisible={_hoverLabel.Visible}";
    }

    /// <summary>
    /// 未处理输入：左键点怪挂赏（已有旗则加价）— Majesty 间接控制入口。
    /// 中键点单位 = 锁定跟随（模拟人生式"盯住一个人看"），点空地解除。
    /// 点选用最近邻屏幕距离匹配（无需碰撞体）；容差按相机距离换算，见 TolerancePx。
    /// 滚轮不在这里 — 已由 GugaFramework 相机架接管为缩放。
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        // 切片8 营建模式优先消费输入：右键/Esc 取消；左键在合法位置落建
        if (_placingKind is { } placingKind)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                ExitPlacingMode(Loc.T("营建取消"));
                return;
            }
            if (@event.IsActionPressed(FrontEndLayer.ActionMenu))
            {
                ExitPlacingMode(Loc.T("营建取消"));
                return;
            }
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } placeClick)
            {
                TryPlaceBuilding(placingKind, placeClick.Position);
                return;
            }
            return; // 营建模式下悬赏点击不生效（模式互斥）
        }

        // 中键 = 锁定/解除跟随（点单位锁上去，点空地解除）— 视角是观察手段，不抢左键的玩法动词
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle } middle)
        {
            ToggleFollowAt(middle.Position);
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }

        var clickPos = click.Position;
        var radius = TolerancePx(ClickTolerancePx);

        // 候选优先级不变：怪物单位（单体悬赏）→ 巢穴（清剿源头）→ 自家建筑（升级）
        var pickedId = PickUnit(clickPos, radius, u => u.Side == Logic.Faction.Monster && u.Health > 0)
            ?? PickBuilding(clickPos, radius, b => b.Owner == Logic.Faction.Monster && b.Health > 0)
            ?? PickBuilding(clickPos, radius, b => b.Owner == Logic.Faction.Hero && b.Health > 0);

        if (pickedId is null)
        {
            ShowFeedback(Loc.T("空地：点怪物或巢穴挂赏"));
            return;
        }

        var own = _world.Buildings.FirstOrDefault(b => b.Id == pickedId && b.Owner == Logic.Faction.Hero);
        if (own is not null)
        {
            TryUpgradeGuild(own);
            return;
        }

        // 已有该目标的活跃旗 → 加价；否则新挂一旗。
        // 与调试口注入同一份词表与入录纪律（→ 「桥只转发」）；入录的 args 是规范化后的那份。
        var existing = _world.Flags.FirstOrDefault(f => !f.Settled && f.TargetId == pickedId);
        var bountyArgs = ReplaySetup.Args(pickedId, _bountyAmount);
        var bountyCmd = existing is null ? "PlaceBountyFlag" : "RaiseBountyFlag";
        if (existing is not null) bountyArgs = ReplaySetup.Args(existing.Id, _bountyAmount);
        var ok = Write(bountyCmd, bountyArgs);

        ShowFeedback(ok
            ? (existing is not null
                ? Loc.Fmt("加价 +${0}（建议 ${1}）", _bountyAmount, _world.SuggestedPrice(pickedId))
                : Loc.Fmt("挂赏 ${0}（建议 ${1}）", _bountyAmount, _world.SuggestedPrice(pickedId)))
            : (existing is not null ? Loc.T("金库不足") : Loc.T("挂赏失败（金库不足）")));
    }

    /// <summary>
    /// 点击反馈走独立标签（3 秒消退）— %HintLabel 只保留常驻操作说明，
    /// 不再被 RenderHud 每帧覆盖（architecture.md 7 节已知债的修复）。
    /// </summary>
    private void ShowFeedback(string text)
    {
        _feedbackLabel.Text = text;
        _feedbackLabel.Visible = true;
        _feedbackTimer = 3f;
    }

    /// <summary>
    /// 营建落建：鼠标屏幕坐标反投地面 → 逻辑层 PlaceBuilding（玩家动词 #2 唯一写入口）。
    /// 成功退出营建模式；失败给反馈（金库不足/过近），模式保留可换位重试。
    /// </summary>
    private void TryPlaceBuilding(BuildingKind kind, Vector2 screenPos)
    {
        var hit = ScreenToGround(screenPos);
        var logicPos = new System.Numerics.Vector2(hit.X, hit.Z);
        var costSnapshot = _world.NextPlaceCost(kind); // 落建前快照（这一座的实际翻倍价）

        // 与调试口注入走同一份词表（→ 「桥只转发」）。失败也入录 —— 拒绝是可观察行为，
        // 重放分叉时「当时试了什么但没成」是最值钱的证据。
        if (Write("PlaceBuilding", ReplaySetup.Args(kind, logicPos)))
        {
            Log($"[InfinityLord] 营建成功 {kind} @({hit.X:F0},{hit.Z:F0}) ${costSnapshot}");
            ExitPlacingMode(Loc.Fmt("已建成 {0}（${1}）", kind, costSnapshot));
        }
        else
        {
            ShowFeedback(costSnapshot > _world.Treasury ? Loc.T("金库不足") : Loc.T("距离过近，无法放置"));
        }
    }

    /// <summary>点击玩家方建筑 → 升级行会（玩家动词 #3 唯一写入口；报价与扣款与 NextUpgradeCost 同源）。</summary>
    private void TryUpgradeGuild(Building building)
    {
        var cost = _world.NextUpgradeCost(building.Id);
        if (cost <= 0)
        {
            ShowFeedback(Loc.Fmt("{0} 无可升级项", DisplayName(building)));
            return;
        }

        // 报价那一条是纯读，不入录；真正落笔升级走词表（→ 「桥只转发」），失败也入录。
        if (!Write("UpgradeGuild", building.Id))
        {
            ShowFeedback(Loc.Fmt("升级需 ${0}，金库不足（现有 ${1}）", cost, _world.Treasury));
            return;
        }

        ShowFeedback(Loc.Fmt("已升级 {0} → Lv.2（${1}）此后产骑士", DisplayName(building), cost));
    }

    /// <summary>
    /// 点选容差（像素）：容差本质是"贴着一个单位多大"的手感，而单位在屏幕上的大小与相机距离成反比 —
    /// 不换算的话拉近了点身子边缘判成空地，拉远了一点命中一片重叠的怪。
    /// 上下限是为了不让极端机位把容差放到不可用（远到小于一个像素、近到覆盖半屏）。
    /// </summary>
    private float TolerancePx(float basePx) => Mathf.Clamp(basePx * _rig.PickToleranceScale, 20f, 160f);

    /// <summary>屏幕位置 → 最近的存活单位 Id（filter 决定阵营/存活口径；无命中 null）。</summary>
    private string? PickUnit(Vector2 screenPos, float radiusPx, Func<Unit, bool> filter)
    {
        string? picked = null;
        var best = radiusPx;
        foreach (var unit in _world.Units)
        {
            if (!filter(unit)) continue;
            if (!_unitModels.TryGetValue(unit.Id, out var view)) continue;
            var dist = ScreenDistance(screenPos, view.Position);
            if (dist >= best) continue;
            best = dist;
            picked = unit.Id;
        }
        return picked;
    }

    /// <summary>屏幕位置 → 最近的建筑 Id（与单位同一套点选口径 — 显示即真相）。</summary>
    private string? PickBuilding(Vector2 screenPos, float radiusPx, Func<Building, bool> filter)
    {
        string? picked = null;
        var best = radiusPx;
        foreach (var building in _world.Buildings)
        {
            if (!filter(building)) continue;
            if (!_buildingModels.TryGetValue(building.Id, out var view)) continue;
            var dist = ScreenDistance(screenPos, view.Position);
            if (dist >= best) continue;
            best = dist;
            picked = building.Id;
        }
        return picked;
    }

    /// <summary>
    /// 中键/Tab 的跟随切换：锁到鼠标下的单位（用模型坐标，自带位移平滑），点空地或已锁定则解除。
    /// 逻辑层零感知 — 视角是观察手段，不是游戏状态。
    /// </summary>
    private void ToggleFollowAt(Vector2 screenPos)
    {
        if (_followedUnitId is not null)
        {
            _followedUnitId = null;
            _rig.FollowTarget(null);
            ShowFeedback(Loc.T("视角：自由观察"));
            return;
        }
        var id = PickUnit(screenPos, TolerancePx(ClickTolerancePx), u => u.Health > 0);
        if (id is null || !_unitModels.TryGetValue(id, out var view))
        {
            ShowFeedback(Loc.T("视角：中键点单位可锁定跟随"));
            return;
        }
        _followedUnitId = id;
        _rig.FollowTarget(view);
        ShowFeedback(Loc.Fmt("视角：锁定 {0}（中键/Tab 解除）", Loc.T(_world.Units.First(u => u.Id == id).Name)));
    }

    /// <summary>世界点到屏幕像素的距离（点选用；点在目标后方时给大距离惩罚）。</summary>
    private float ScreenDistance(Vector2 screenPoint, Vector3 worldPos)
    {
        var projected = _camera.UnprojectPosition(worldPos);
        var behind = _camera.IsPositionBehind(worldPos);
        var dx = screenPoint.X - projected.X;
        var dy = screenPoint.Y - projected.Y;
        return behind ? float.MaxValue : MathF.Sqrt(dx * dx + dy * dy);
    }

    public override void _ExitTree()
    {
        // 离树退订走的是 UnwireWorld 那一件（「离树交还」：主动交还，不在调用点每次查有效性）。
        // 换世界时的退订也走它 —— 两个时机同一件，但**换世界时本节点不离树**，
        // 所以那一发必须由换世界自己调，不靠这里。
        UnwireWorld();
        _world.Dispose();
    }

    /// <summary>全局总线上那条暂停消息的表现层落点：只翻横幅，不碰任何真值（「表现层只读」）。</summary>
    private void OnGamePaused(GugaFramework.EventBus.Events.GamePaused e)
        => _pauseLabel.Visible = e.IsPaused;

    // === 渲染：每帧把逻辑状态同步到模型（模型无状态，纯镜像） ===

    private void RenderUnits(float dt)
    {
        var seen = new HashSet<string>();
        // 逻辑每 0.1 秒跳一步（离散时间轴），模型直接钉在逻辑坐标上就是 10 次/秒的跳动 —
        // 固定远机位看不出来，拉近或锁定跟随时就是明晃晃的抖动。这里补一层位移平滑。
        // 倍率越大目标跳变越大，刚度同步放大才不会被甩在后面。
        var gameSpeed = Math.Max(1.0, _speed.Multiplier);
        var smooth = 1f - Mathf.Exp(-UnitSmoothStiffness * (float)gameSpeed * dt);
        var plate = PlateScale;
        foreach (var unit in _world.Units)
        {
            seen.Add(unit.Id);
            if (!_unitModels.TryGetValue(unit.Id, out var view))
            {
                view = SpawnUnitModel(unit);
                _unitModels[unit.Id] = view;
            }
            var target = new Vector3(unit.Position.X, 0.5f, unit.Position.Y);
            var prev = _unitPrevPos.TryGetValue(unit.Id, out var p) ? p : target;
            view.Position = view.Position.Lerp(target, smooth);
            _unitPrevPos[unit.Id] = view.Position;

            // 切片4：每帧刷新名牌与血条（模型仍无状态 — 数据全部来自逻辑层快照）
            var visual = _unitVisuals[unit.Id];

            // 位移差商除以快进倍率 = 「米每游戏秒」，动画挑档要的是这个口径（见 ModelAnimator.RunThreshold）。
            // 但差商是逐帧的：逻辑 0.1 秒跳一格，平滑追赶在跳完的第一帧会给出约 1.5 倍的瞬时速度，
            // 直接喂会让 11 米/秒的地精集体跑起来 — 先低通一步（时间常数 0.15 秒）再给。
            // 朝向只在真动的时候改 — 一直转的话原地站着的人会抖成陀螺。
            var delta = view.Position - prev;
            var rawSpeed = delta.Length() / Math.Max(dt, 1e-4f) / (float)gameSpeed;
            var ema = _unitSpeedEma.TryGetValue(unit.Id, out var s)
                ? Mathf.Lerp(s, rawSpeed, 1f - Mathf.Exp(-dt / SpeedEmaTau))
                : rawSpeed;
            _unitSpeedEma[unit.Id] = ema;
            visual.Anim.GameSpeed = (float)gameSpeed;
            visual.Anim.Travel(ema);
            if (visual.Model is { } mdl && delta.LengthSquared() > 1e-4f)
                mdl.Rotation = new Vector3(mdl.Rotation.X, Mathf.Atan2(-delta.X, -delta.Z), mdl.Rotation.Z);
            if (unit.Side == Logic.Faction.Hero)
            {
                // 切片7：名牌带士气（低士气标红提示「为什么他不去接单」）
                var moraleTag = unit.Morale < 60 ? Loc.Fmt(" ☹{0}", unit.Morale) : Loc.Fmt(" ☺{0}", unit.Morale);
                visual.NameLabel.Text = Loc.Fmt("{0} Lv.{1}+{2} ${3}{4}",
                    Loc.T(unit.Name), unit.Level, unit.Gear, unit.Gold, moraleTag);
            }
            else
            {
                visual.NameLabel.Text = Loc.T(unit.Name);
            }
            visual.NameLabel.Scale = Vector3.One * plate;
            var hpRatio = unit.MaxHealth > 0 ? (float)unit.Health / unit.MaxHealth : 0f;
            UpdateHealthBar(visual, hpRatio, unit.Side == Logic.Faction.Hero);
        }
        // 阵亡模型由 OnUnitDied 事件流移除，这里不兜底清理（禁兜底代码）
    }

    /// <summary>
    /// 头顶文字的缩放系数：字号是世界单位，相机距离一变屏幕大小就跟着变（拉近糊屏、拉远读不见）。
    /// 乘上 距离/参考距离 后，任意机位下的屏幕大小都等于当初在参考机位上调好的那一版。
    /// </summary>
    private float PlateScale => _rig.Distance / _rig.ReferenceDistance;

    /// <summary>刷新头顶血条：缩放内条 + 低血变红（英雄蓝/怪物红同色系）。</summary>
    private static void UpdateHealthBar(UnitVisual visual, float ratio, bool isHero)
    {
        ratio = Math.Clamp(ratio, 0f, 1f);
        visual.HealthBar.Scale = new Vector3(ratio, 1f, 1f);
        var mat = (StandardMaterial3D)visual.HealthBar.MaterialOverride!;
        mat.AlbedoColor = ratio > 0.5f
            ? (isHero ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.45f, 0.25f))
            : new Color(1f, 0.15f, 0.1f);
    }

    private void RenderHud()
    {
        var flow = _world.LastCycleNet >= 0 ? "+" : "−"; // 资金净流动方向（每 6 秒一结）
        var state = _world.Outcome switch
        {
            GameOutcome.Victory => Loc.T("通关"),
            GameOutcome.Defeat => Loc.T("陷落"),
            _ => _world.IsPaused ? Loc.T("暂停") : Loc.T("运转中"),
        };
        var heroes = _world.Units.Count(u => u.Side == Logic.Faction.Hero);
        var mobs = _world.Units.Count(u => u.Side == Logic.Faction.Monster);
        // 切片 10：建筑被推平是常态 — 行会不再假定存在（原 First 在行会塌陷后直接抛异常）
        var guild = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.GuildHall);
        var palace = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Palace);
        var boss = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.BossLair);
        var warriorGuilds = _world.Buildings.Where(b => b.Kind == BuildingKind.GuildWarrior).ToList();
        // 切片 10 终局层读数：宫殿血 = 血条，波次 = 曲线进度（由步号推导，表现层不自持状态），BOSS 血 = 终幕目标。
        // 放在 TickLabel（短行）而非 StatsLabel —— 后者实测宽 1907px 已超出 1152 视口，读数会被裁掉。
        var wavesArrived = LogicWorld.WaveTable.Count(w => w.AtTick <= _world.Tick);
        var bossText = boss is not null ? Loc.Fmt("BOSS {0}", boss.Health)
            : _world.Tick >= LogicWorld.BossArriveTick ? Loc.T("终幕已破") : Loc.T("终幕未至");
        _tickLabel.Text = Loc.Fmt(
            "Tick: {0}  ({1})  金库: {2}（周期净额 {3}{4}） | 宫殿{5} 波{6}/{7} {8}",
            _world.Tick, state, _world.Treasury, flow, Math.Abs(_world.LastCycleNet),
            palace?.Health ?? 0, wavesArrived, LogicWorld.WaveTable.Length, bossText);
        var lairs = _world.Buildings.Where(b => b.Kind == BuildingKind.MonsterLair).ToList();
        var lairText = string.Join(" ", lairs.Select(l => $"{l.Id}:{Math.Max(0, l.Health)}"));
        var avgLevel = heroes > 0 ? _world.Units.Where(u => u.Side == Logic.Faction.Hero).Average(u => u.Level) : 0;
        var totalGear = _world.Units.Where(u => u.Side == Logic.Faction.Hero).Sum(u => u.Gear);
        // 切片9 可观测性：职业构成 — 英雄池构成一眼可读（几游侠几战士）
        var heroList = _world.Units.Where(u => u.Side == Logic.Faction.Hero && u.Health > 0).ToList();
        var rangers = heroList.Count(u => u.Class == HeroClass.Ranger);
        var warriors = heroList.Count(u => u.Class == HeroClass.Warrior);
        var knights = heroList.Count(u => u.Class == HeroClass.Knight);
        var minMorale = heroList.Count > 0 ? heroList.Min(u => u.Morale) : 0;
        var lowMorale = heroList.Count(u => u.Morale < LogicWorld.MoraleDrinkThreshold);
        var wounded = heroList.Count(u => u.Health < u.MaxHealth * LogicWorld.PotionSeekRatio);
        var guildText = warriorGuilds.Count > 0
            ? Loc.Fmt("游侠公会{0} 战士公会x{1}(Lv.{2})", guild?.Health ?? 0, warriorGuilds.Count, warriorGuilds.Max(g => g.Level))
            : Loc.Fmt("游侠公会{0}", guild?.Health ?? 0);
        _statsLabel.Text = Loc.Fmt(
            "英雄x{0}(游{1}战{2}骑{3} 均Lv.{4:F1} 总装备{5}) 怪物x{6} | 旗x{7} | "
            + "{8} | 巢穴 {9} | 最低士气{10} 低士气x{11} 伤员x{12} | 定价 ${13}(, / .)",
            heroes, rangers, warriors, knights, avgLevel, totalGear, mobs,
            _world.Flags.Count(f => !f.Settled), guildText, lairText, minMorale, lowMorale, wounded, _bountyAmount);
        _hintLabel.Text = Loc.T(
            "左键点怪物/巢穴挂赏 · 再点同目标加价 · , / . 调定价 · 1/2/3 营建(市场/酒馆/药店) · 4/5 建行会(战士/游侠) "
            + "· 点战士公会升级出骑士 · 空格暂停 · 悬停看行情");
        // 视角读数常驻（可观测性第一）：截图里自带机位证据，不必猜"画面变了没有"
        _camLabel.Text = Loc.T("视角 滚轮缩放 · 中键拖拽绕转/改俯仰 · 中键单击锁定跟随 · WASD 平移 · Q/E 或 ←→ 转 · ↑↓ 俯仰 · Home 归位　")
            + _rig.DebugReadout();
    }

    // === 悬赏旗渲染 ===

    // === 调试直达 API（复盘提案 2/快照方案 ②：MCP game_eval 可直接调用，绕开输入注入与 GDScript 子集限制） ===

    /// <summary>
    /// 调试速度旋钮 — eval 直达：SetSpeed(20) 一发切 20 倍速（快进赶路），
    /// SetSpeed(0.25) 慢放（关键段细看），SetSpeed(1) 恢复实时。
    /// 暂停用 PauseDebug；单步用 StepDebug。
    /// </summary>
    public string SetSpeed(double multiplier)
    {
        _speed.SetMultiplier(multiplier);
        return $"speed={_speed.Multiplier}x";
    }

    /// <summary>暂停直达 — eval 直达：时间冻结（画面定格），停下读快照/发命令。</summary>
    public void PauseDebug() => _world.SetPaused(true);

    /// <summary>恢复直达 — eval 直达：解除暂停，按当前倍率继续。</summary>
    public void ResumeDebug() => _world.SetPaused(false);

    /// <summary>
    /// 试玩录制三连 · 开始 — eval 直达：ReplayRecordStart() 记下正式起点（从当前世界接着录，
    /// 固定种子 42）。之后照常试玩（或用 *Debug 口发命令），期间随时 ReplayMark("t300")
    /// 打检查点，收尾 ReplayRecordStop("名字") 落 json 进 Tests/Scripts/。
    ///
    /// <para><b>它只挪起点，不清账</b>：这一局的账从第 0 步就在记（见 <c>_Ready</c> 里那一行），
    /// 因为树写的意图<b>一开局就在写</b>，而重放端那个世界（<c>dotnet test</c> 里新建的）
    /// 没有树也没有桥 —— 开头那一段不落账，重放端的世界就与录制端不同源
    /// （实测后果：随机流错位，同一步差出 1.5 米）。</para>
    ///
    /// <para><b>补一笔接管</b>：起点之前已经在树上的英雄，补一条 <c>DelegateHeroToBrain</c>，
    /// 让重放端在起点那一刻就有同一张接管表。它在 Journal 开头、序号占 0；
    /// 重放端会先把世界推到起点、再按 (步号, 序号) 喂条目，所以这一笔与起点同刻生效。</para>
    /// </summary>
    public string ReplayRecordStart()
    {
        _recorder.MarkStart();
        var taken = _world.BrainTakenHeroIds;
        foreach (var heroId in taken)
        {
            _recorder.SeedPreStartWrite("DelegateHeroToBrain", ReplaySetup.DelegateArgs(heroId));
        }

        return $"recording startTick={_recorder.StartTick} brainTaken={taken.Count} "
             + $"journal={_recorder.Journal.Count}";
    }

    /// <summary>
    /// 试玩录制 · 打检查点 — eval 直达：ReplayMark("t300") 在当前步抓一份全量快照。
    /// 检查点是重放时的断言单位，关键状态点（挂赏后/营建后/波次后）各打一个。
    /// </summary>
    public string ReplayMark(string label)
    {
        if (!_recorder.IsRecording) return "not-recording";
        _recorder.Mark(label);
        return $"mark {label}@tick={_world.Tick} ({_recorder.Checkpoints.Count} checkpoints)";
    }

    /// <summary>
    /// 试玩录制 · 结束落盘 — eval 直达：ReplayRecordStop("replay-v2-xxx") 写
    /// Tests/Scripts/&lt;名&gt;.json（与测试读的同一目录），下次 dotnet test 自动跑。
    /// 返回落盘路径与条数；落盘后录制会话清空（要续录再 Start）。
    /// </summary>
    public string ReplayRecordStop(string name)
    {
        if (!_recorder.IsRecording) return "not-recording";
        var script = new GugaFramework.Replay.ReplayRunner.ReplayScript(
            Seed, _recorder.Journal, _recorder.Checkpoints,
            _recorder.StartTick, _recorder.StartSnapshot);
        var dir = Path.Combine(ProjectDir, "src", "Prototypes", "InfinityLord", "Tests", "Scripts");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{name}.json");
        File.WriteAllText(file, GugaFramework.Replay.ReplayScriptJson.Write(script));
        var n = _recorder.Journal.Count;
        var c = _recorder.Checkpoints.Count;
        _recorder.EndSession();
        return $"saved {n} entries + {c} checkpoints -> {file}";
    }

    /// <summary>
    /// 单步直达 — eval 直达：走一步自动停（最细调试档），返回步进后的快照首行。
    /// 暂停与否皆可调；暂停中步进等效"看下一格"。
    /// </summary>
    public string StepDebug()
    {
        _world.Advance(1);
        return $"tick={_world.Tick}";
    }

    /// <summary>
    /// 画面开关直达 — eval 直达：关 = 3D 世界不渲染（HUD 保留 — 读数是快照的镜子），
    /// 黑屏纯逻辑全速跑；开 = 立刻恢复画面跟随。同一模拟，只切不画。
    /// </summary>
    public string SetRendering(bool visible)
    {
        _worldRoot.Visible = visible;
        return visible ? "rendering=on" : "rendering=off (hud kept)";
    }

    /// <summary>全量状态快照 — eval 直达：DebugSnapshot() 返回所有英雄的需求状态与动机（见 LogicWorld.DebugSnapshot）。</summary>
    public string DebugSnapshot() => _world.DebugSnapshot();

    /// <summary>
    /// 重放快照直达 — eval 直达：ReplaySnapshotDebug() 返回检查点用的全量状态单行
    /// （LogicWorld.ReplaySnapshot — 录制/重放/试玩三端共用的那份口径，含 outcome）。
    /// 整局录制时读首行核对 `tick` / `treasury` / `outcome` 三个键，或单独读回看终局状态。
    /// </summary>
    public string ReplaySnapshotDebug() => _world.ReplaySnapshot();

    /// <summary>
    /// 宫殿段直达 — eval 直达：PalaceLineDebug() 返回快照里宫殿那一小段
    /// （tick/outcome 头 + palace 血量）。eval 子集跑不了 find+循环（EVAL_COMPILE_ERROR
    /// 实测两连），文本检索放 C# 侧。
    /// </summary>
    public string PalaceLineDebug()
    {
        var snap = _world.ReplaySnapshot();
        var i = snap.IndexOf("palace", StringComparison.Ordinal);
        return i < 0
            ? $"head={snap.AsSpan(0, Math.Min(48, snap.Length))} palace=已毁"
            : $"head={snap.AsSpan(0, Math.Min(48, snap.Length))} …{snap.Substring(i, Math.Min(70, snap.Length - i))}";
    }

    /// <summary>
    /// 时间快进直达 — eval 直达：AdvanceDebug(50) 一次推进 n 步（确定性时间轴，
    /// 不依赖窗口持续活跃 — 快照读数与挂旗实验可在冻结窗口间原子衔接）。
    /// </summary>
    public bool AdvanceDebug(int steps) => _world.Advance(steps);

    /// <summary>挂赏直达 — eval 直达：PlaceBountyDebug("lair1", 25) 等价玩家点击挂赏（验证拒单行为用）。</summary>
    public bool PlaceBountyDebug(string targetId, int gold)
        => Write("PlaceBountyFlag", ReplaySetup.Args(targetId, gold));

    /// <summary>
    /// 低赏对照直达 — eval 直达：给**首个满血怪物单位**挂指定金额（读快照与挂赏同一发 C# 调用内完成，
    /// 免 eval 侧的挂赏竞态 — 目标在世界推进中随时可能被杀）。返回 "挂赏结果 目标Id" 便于日志确认。
    /// </summary>
    public string PlaceLowBountyDebug(int gold)
    {
        var target = _world.Units.FirstOrDefault(u => u.Side == Logic.Faction.Monster && u.Health == u.MaxHealth);
        if (target is null) return "no-fullhp-mob";
        var ok = Write("PlaceBountyFlag", ReplaySetup.Args(target.Id, gold));
        return $"{ok} {target.Id}";
    }

    /// <summary>
    /// 建筑扣血直达 — eval 直达：DamageBuildingDebug("lair1", 300) 走逻辑层 DamageBuilding
    /// 完整收口（摧毁事件 + 巢穴重生调度），与战斗流程同语义。
    /// </summary>
    public bool DamageBuildingDebug(string buildingId, int damage)
        => Write("DamageBuilding", ReplaySetup.Args(buildingId, damage));

    /// <summary>建筑回血直达 — eval 直达：HealBuildingDebug("palace", 200000) 把宫殿堡垒化，
    /// 用于观测后段波次与终幕表现（放任世界撑不到 6:00）。
    /// 录制期间也合法：写路径进录（重放端同款回血），与 DebugSetTreasury 同为"进录的排布口"。</summary>
    public bool HealBuildingDebug(string buildingId, int amount)
        => Write("HealBuilding", ReplaySetup.Args(buildingId, amount));

    /// <summary>营建直达 — eval 直达：PlaceBuildingDebug("tavern", new Vector2(-50,-50)) 等价玩家营建落建（切片8 验证用）。
    /// 切片 9：guild_warrior / guild_ranger（行会 — 建筑即招募）。
    /// kind 原样透传给词表，规范化与别名解析都在那边（拼错就整条失败，不降级）。</summary>
    public bool PlaceBuildingDebug(string kind, Godot.Vector2 position)
        => Write("PlaceBuilding", ReplaySetup.Args(kind, new System.Numerics.Vector2(position.X, position.Y)));

    /// <summary>行会升级直达 — eval 直达：UpgradeGuildDebug("guildwarrior1") 等价玩家点击升级（切片 10 动词 #3）。</summary>
    public bool UpgradeGuildDebug(string buildingId)
        => Write("UpgradeGuild", buildingId);

    /// <summary>
    /// 行会升级直达（免 Id 版）— eval 直达：UpgradeFirstGuildDebug() 找玩家方第一座
    /// 战士行会升级。eval 子集解析不了快照文本（多行 if 触发 EVAL_COMPILE_ERROR，
    /// 复盘坑 5），Id 查找挪 C# 侧；行为等价「点第一座可升级的战士行会」。
    /// </summary>
    public bool UpgradeFirstGuildDebug()
    {
        var building = _world.Buildings.FirstOrDefault(b =>
            b.Owner == Logic.Faction.Hero && b.Kind == BuildingKind.GuildWarrior && b.Level == 1);
        if (building is null) return false;
        return Write("UpgradeGuild", building.Id);
    }

    /// <summary>
    /// 推进直达（到点版）— eval 直达：AdvanceToTickDebug(3600) 把世界推进到目标步
    /// （推进器与重放端同款，在 Scripts/Logic/ReplaySetup.cs；暂停空转、步号不动，
    /// 循环自然退出不死等。终局不冻结时间轴（2026-09-23 拆冻结），无需 Outcome 护栏
    /// 也能推进到任意步）。返回读数 = Main 自己的排版，不进 Journal。
    /// </summary>
    public string AdvanceToTickDebug(long targetTick)
    {
        ReplaySetup.AdvanceTo(_world, targetTick);
        return $"tick={_world.Tick} outcome={_world.Outcome}";
    }

    /// <summary>
    /// 升级金光直达 — eval 直达：LevelUpFxDebug("hero3") 立即演一次特效；传空串则挑第一个**画面上有模型**的英雄。
    /// 特效只活 0.6 秒，等逻辑层真升级再截图必然错过；表现层验收走这一发（纯演出，不碰逻辑层）。
    /// </summary>
    public string LevelUpFxDebug(string unitId)
    {
        var id = string.IsNullOrEmpty(unitId)
            ? _world.Units.FirstOrDefault(u =>
                u.Side == Logic.Faction.Hero && u.Health > 0 && _unitVisuals.ContainsKey(u.Id))?.Id ?? ""
            : unitId;
        PlayLevelUpFx(id);
        return id;
    }

    /// <summary>
    /// 终局读数直达 — eval 直达：DebugEndgame() 一发返回胜负/时刻/宫殿血/波次/BOSS 血，
    /// 供切片 10 的终幕与宫殿表现层实测（不必扫 HUD 文本）。
    /// </summary>
    public string DebugEndgame()
    {
        var palace = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Palace);
        var boss = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.BossLair);
        var waves = LogicWorld.WaveTable.Count(w => w.AtTick <= _world.Tick);
        return $"outcome={_world.Outcome} tick={_world.Tick} treasury={_world.Treasury} " +
            $"palace={palace?.Health ?? 0} waves={waves}/{LogicWorld.WaveTable.Length} " +
            $"boss={boss?.Health ?? 0} heroes={_world.Units.Count(u => u.Side == Logic.Faction.Hero)}";
    }

    /// <summary>视角读数直达 — eval 直达：CameraReadout() 返回机位（焦点/距离/偏航/跟随目标）。</summary>
    public string CameraReadout() => _rig.DebugReadout();

    /// <summary>
    /// 机位定点直达 — eval 直达：CameraSetPose(22, 6, 40, 90, 30) 立即把相机摆到指定机位（不过阻尼）。
    /// 注入按键只能证明"会响应"，截图要的是"停在某个机位"，两条通道各证一半。
    /// </summary>
    public string CameraSetPose(float focusX, float focusZ, float distance, float yawDegrees, float pitchDegrees)
    {
        _rig.SetPose(new Vector3(focusX, 0f, focusZ), distance, yawDegrees, pitchDegrees);
        return _rig.DebugReadout();
    }

    /// <summary>跟随锁定直达 — eval 直达：CameraFollowDebug("hero3") 锁到该单位；传空串解除。</summary>
    public string CameraFollowDebug(string unitId)
    {
        if (string.IsNullOrEmpty(unitId))
        {
            _followedUnitId = null;
            _rig.FollowTarget(null);
            return _rig.DebugReadout();
        }
        if (!_unitModels.TryGetValue(unitId, out var view)) return $"no-view:{unitId}";
        _followedUnitId = unitId;
        _rig.FollowTarget(view);
        return _rig.DebugReadout();
    }

    /// <summary>
    /// 挑一个可点的怪物 — eval 直达：PickMob() 一发返回 "Id 屏幕X,屏幕Y 前后景"。
    /// 取目标与取屏幕坐标必须在同一发里完成：战斗在推进，分两发读时那只怪可能已经被杀了
    /// （与 PlaceLowBountyDebug 同源的竞态）。
    /// </summary>
    public string PickMob()
    {
        var mob = _world.Units.FirstOrDefault(u => u.Side == Logic.Faction.Monster
            && u.Health > 0 && _unitModels.ContainsKey(u.Id));
        if (mob is null) return "no-mob";
        var view = _unitModels[mob.Id];
        var p = _camera.UnprojectPosition(view.Position);
        return $"{mob.Id} {p.X:F0},{p.Y:F0} {(_camera.IsPositionBehind(view.Position) ? "behind" : "front")}";
    }

    private void RenderFlags()
    {
        foreach (var flag in _world.Flags)
        {
            var target = _world.Units.FirstOrDefault(u => u.Id == flag.TargetId && u.Health > 0)
                as dynamic
                ?? _world.Buildings.FirstOrDefault(b => b.Id == flag.TargetId && b.Health > 0);

            if (target is null)
            {
                // 目标已消失但未结算（结算由逻辑层下一步清理）：先隐藏
                if (_flagModels.TryGetValue(flag.Id, out var gone))
                {
                    gone.Visible = false;
                }
                continue;
            }

            if (!_flagModels.TryGetValue(flag.Id, out var view))
            {
                view = BuildFlagModel();
                _worldRoot.AddChild(view);
                _flagModels[flag.Id] = view;
            }

            view.Visible = true;
            Vector3 pos;
            if (target is Unit unitTarget)
            {
                // 旗子跟着模型（已含位移平滑）而不是逻辑坐标 — 跟逻辑坐标会比分平滑后的单位早跳一步，看着脱节
                var carrier = _unitModels[unitTarget.Id].Position;
                pos = new Vector3(carrier.X, 0f, carrier.Z);
            }
            else
            {
                var b = (Building)target;
                pos = new Vector3(b.Position.X, 0, b.Position.Y);
            }
            view.Position = pos;

            // 承接状态写在金额上 —— 旗子的造型与旗面颜色是资产，运行时不改（准则：不缩放几何、不换色）
            var claimed = flag.ClaimedByHeroId is not null;
            var label = view.GetNode<Label3D>("Amount");
            label.Text = claimed ? Loc.Fmt("${0} · 已接单", flag.Gold) : $"${flag.Gold}";
            label.Modulate = claimed ? new Color(1f, 0.87f, 0.3f) : Colors.White;
            label.Scale = Vector3.One * PlateScale;
        }
        // 已结算旗子的模型在 OnBountyEvent(Paid) 中移除
    }

    private Node3D BuildFlagModel()
    {
        var view = new Node3D();
        var model = InstantiateModel(ModelRoot + "BountyFlag.glb");
        if (model is not null)
        {
            model.Rotation = new Vector3(0, Mathf.DegToRad(FlagYaw), 0);
            view.AddChild(model);
        }

        var label = new Label3D
        {
            Name = "Amount",
            Text = "$0",
            FontSize = 44,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            // 高度取自旗子自己的 Anchor（5.9，旗顶之上），运行时不缩旗杆
            Position = new Vector3(0, model?.GetNodeOrNull<Marker3D>("Anchor")?.Position.Y ?? 5.9f, 0),
        };
        view.AddChild(label);
        return view;
    }

    /// <summary>
    /// 幽灵预览工厂：<b>真模型</b>（.glb，路径来自蓝图的模型引用）+ 占地环 + 名牌。
    ///
    /// <para>为什么不再拼方块（「真模型」）：迁前那个 BoxMesh 是运行时拼的图元，玩家看到的是一块
    /// 与建成后毫无关系的方板 —— 幽灵该是「这座楼会长成什么样」的预告。材质只做红绿半透，
    /// 那是裁决的可见化，不是造型决策（「造型进模型」 管的是建成品的职业/阵营差异）。</para>
    ///
    /// <para>初值 <c>Visible = false</c>：幽灵的第一帧位置由第一条裁决决定，构造完就摆在原点
    /// 会闪一下。占地环是 gizmo（画「这一发占哪块地」），不是造型，用引擎图元不违反「真模型」。</para>
    /// </summary>
    private Node3D BuildGhostModel(BuildingKind kind)
    {
        _ghostMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        var view = new Node3D { Visible = false };
        if (InstantiateModel(ModelPathFor(kind)) is { } model)
        {
            TintGhost(model, _ghostMaterial);
            model.Rotation = new Vector3(0, Mathf.DegToRad(BuildingYaw[kind]), 0);
            view.AddChild(model);
        }
        else
        {
            GD.PushError($"[InfinityLord] 营建幽灵没有模型：蓝图 {LogicWorld.BlueprintIdOf(kind)} "
                + "的模型引用为空或加载失败");
        }

        // 单位半径的环，落座时按裁决给的占地半径缩放（自由放置的占地半径是蓝图数据，网格规则那边是空表）。
        _ghostRing = new MeshInstance3D
        {
            Name = "Footprint",
            Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 24, RingSegments = 6 },
            MaterialOverride = _ghostMaterial,
            Position = new Vector3(0, 0.1f, 0),
        };
        view.AddChild(_ghostRing);

        _ghostLabel = new Label3D
        {
            Name = "GhostPlate",
            Text = Loc.T("预览"),
            FontSize = 40,
            Position = new Vector3(0, 4.2f, 0),
        };
        view.AddChild(_ghostLabel);
        return view;
    }

    /// <summary>把模型里的每个网格都挂上同一份预览材质（幽灵整体一个色，红绿才是一句话）。</summary>
    private static void TintGhost(Node node, Material material)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh)
            {
                mesh.MaterialOverride = material;
            }

            TintGhost(child, material);
        }
    }

    private void OnBountyEvent(BountyEventReport report)
    {
        switch (report.Kind)
        {
            case BountyEventKind.Placed:
                Log($"[InfinityLord] 挂赏 flag={report.FlagId} 目标={report.TargetId} 金额=${report.Gold}");
                break;
            case BountyEventKind.Released:
                Log($"[InfinityLord] 承接英雄阵亡，悬赏释放 flag={report.FlagId} 原承接={report.HeroId}");
                break;
            case BountyEventKind.Paid:
                if (_flagModels.TryGetValue(report.FlagId, out var view))
                {
                    view.QueueFree();
                    _flagModels.Remove(report.FlagId);
                }
                Log($"[InfinityLord] 悬赏结算 flag={report.FlagId} 目标={report.TargetId} 赏金=${report.Gold}");
                break;
            case BountyEventKind.Refunded:
                if (_flagModels.TryGetValue(report.FlagId, out var refunded))
                {
                    refunded.QueueFree();
                    _flagModels.Remove(report.FlagId);
                }
                Log($"[InfinityLord] 悬赏无人领赏，退款 ${report.Gold} flag={report.FlagId} 目标={report.TargetId}");
                break;
        }
    }

    // === 经济事件处理（切片3：账本流动可视化） ===

    private void OnEconomyEvent(EconomyEventReport report)
    {
        switch (report.Kind)
        {
            case EconomyEventKind.VillagerTax:
                Log($"[InfinityLord] 村民税 +${report.Amount}");
                break;
            case EconomyEventKind.TaxCollected:
                Log($"[InfinityLord] 英雄消费税收回流 +${report.Amount}");
                break;
            case EconomyEventKind.HeroLeftNoBounty:
                Log($"[InfinityLord] 冒险者 {report.UnitId} 长期零收入，离开行会");
                break;
            case EconomyEventKind.HeroLeftNoTavern:
                Log($"[InfinityLord] 冒险者 {report.UnitId} 长期喝不上酒，离开行会");
                break;
            case EconomyEventKind.HeroLeveledUp:
                Log($"[InfinityLord] 英雄 {report.UnitId} 升到 Lv.{report.Amount}！");
                PlayLevelUpFx(report.UnitId ?? "");
                break;
            case EconomyEventKind.HeroBoughtGear:
                Log($"[InfinityLord] 英雄 {report.UnitId} 购买装备 → Gear+1（现 {report.Amount}）");
                break;
            case EconomyEventKind.KillLoot:
                Log($"[InfinityLord] 英雄 {report.UnitId} 击杀拾得虚空黄金 +${report.Amount}");
                break;
            case EconomyEventKind.HeroDrank:
                Log($"[InfinityLord] 英雄 {report.UnitId} 喝酒 → 士气 {report.Amount}");
                break;
            case EconomyEventKind.HeroBoughtPotion:
                Log($"[InfinityLord] 英雄 {report.UnitId} 购买药剂 → 血量 {report.Amount}");
                break;
            case EconomyEventKind.BuildingBuilt:
                Log($"[InfinityLord] 玩家花费 ${report.Amount} 建造建筑（金币转资产）");
                break;
            case EconomyEventKind.GuildUpgraded:
                Log($"[InfinityLord] 行会 {report.UnitId} 升级（${report.Amount}）→ 此后产骑士");
                RefreshBuildingModel(report.UnitId ?? "");
                break;
        }
    }

    /// <summary>
    /// 重建某建筑模型（切片 10：升级改了等级，而名牌在生成时写死 — 事件驱动重挂，模型仍无状态）。
    /// </summary>
    private void RefreshBuildingModel(string buildingId)
    {
        var building = _world.Buildings.FirstOrDefault(b => b.Id == buildingId);
        if (building is null) return;
        if (_buildingModels.TryGetValue(buildingId, out var old))
        {
            old.QueueFree();
            _buildingModels.Remove(buildingId);
            _buildingPlates.Remove(buildingId);
        }
        _buildingModels[buildingId] = SpawnBuildingModel(building);
    }

    /// <summary>升级特效：整身金光脉冲 — 交给动画驱动器叠材质，表现层不碰模型自带材质。</summary>
    private void PlayLevelUpFx(string unitId)
    {
        if (_unitVisuals.TryGetValue(unitId, out var visual)) visual.Anim.Flash();
    }

    // === 模型工厂：真模型（.glb） ===

    /// <summary>
    /// 造型资产根。`Models/` 下是**真模型**（.glb：骨架 + 蒙皮 + 动作，由
    /// GugaFramework/Tools/glb/export_glb.gd 从 `Tools/Rigs/` 的关节树源场景导出）。源场景是开发期产物，
    /// 不进游戏；表里存的是完整 res:// 路径 —— 游戏按路径加载资产，不在运行时决定造型长什么样。
    /// </summary>
    private const string ModelRoot = "res://src/Prototypes/InfinityLord/Models/";

    /// <summary>职业 → 模型：游侠持弓（射程 6 的远程）、战士圆盾高剑（勇气 0.7 的莽夫）、骑士重甲鸢盾长剑（精英线）。</summary>
    private static readonly Dictionary<HeroClass, string> HeroModels = new()
    {
        [HeroClass.Ranger] = ModelRoot + "Ranger.glb",
        [HeroClass.Warrior] = ModelRoot + "Warrior.glb",
        [HeroClass.Knight] = ModelRoot + "Knight.glb",
    };

    /// <summary>
    /// 怪物名 → 模型 — 名字口径来自 LogicWorld.WaveTable 与巢穴涓流。
    /// 波次每升一阶体型与武器同步上台阶，威胁曲线因此肉眼可读（体型做在模型里，不在运行时缩放）。
    /// </summary>
    private static readonly Dictionary<string, string> MonsterModels = new()
    {
        ["地精"] = ModelRoot + "Goblin.glb",
        ["强化地精"] = ModelRoot + "GoblinElite.glb",
        ["大地精"] = ModelRoot + "GoblinBrute.glb",
        ["巨魔"] = ModelRoot + "Troll.glb",
        ["BOSS 护卫"] = ModelRoot + "BossGuard.glb",
    };

    /// <summary>
    /// 场地建筑的模型表 —— 只剩<b>不在营建蓝图表里</b>的那三座（宫殿 / 两种巢穴，D33 的蓝图表
    /// 只有玩家能营建的五种）。玩家营建那五种一律问蓝图（<see cref="ModelPathFor"/>），两个表
    /// 各管一半、互不重叠：同一座楼因此只有一处能说出它长什么样。
    /// </summary>
    private static readonly Dictionary<BuildingKind, string> BuildingModels = new()
    {
        [BuildingKind.Palace] = ModelRoot + "Palace.glb",
        [BuildingKind.BossLair] = ModelRoot + "BossLair.glb",
        [BuildingKind.MonsterLair] = ModelRoot + "MonsterLair.glb",
    };

    /// <summary>
    /// 建筑模型路径：<b>先问模块的蓝图表</b>，再落到场地建筑表（D29 —— 模型引用长在蓝图上，
    /// 模块存而不读，读的是表现层）。营建幽灵与建成模型走同一条口，幽灵摆的与落下的必然是同一个。
    /// </summary>
    private string ModelPathFor(BuildingKind kind)
        => _world.Construction.Blueprints.TryGetValue(LogicWorld.BlueprintIdOf(kind), out var blueprint)
           && blueprint.ModelPath.Length > 0
            ? blueprint.ModelPath
            : BuildingModels.GetValueOrDefault(kind) ?? "";

    /// <summary>
    /// 建筑朝向（度）。导出器把源场景的 +Z 前脸转成产物里的 -Z（`Rig` 节点自带 180°），
    /// 所以「对着初始镜头」（镜头在 +Z 侧看回原点）的都要写 180；巢穴的洞口原本朝 -X，
    /// 翻 180 后指向村庄要写 +90。悬赏旗同款，见 <see cref="BuildFlagModel"/>。
    /// </summary>
    private static readonly Dictionary<BuildingKind, float> BuildingYaw = new()
    {
        [BuildingKind.Palace] = 180f,
        [BuildingKind.GuildHall] = 180f,
        [BuildingKind.GuildWarrior] = 180f,
        [BuildingKind.Market] = 180f,
        [BuildingKind.Tavern] = 180f,
        [BuildingKind.Apothecary] = 180f,
        [BuildingKind.MonsterLair] = 90f,
        [BuildingKind.BossLair] = 90f,
    };

    /// <summary>悬赏旗旗面（产物里朝 -Z）转向初始镜头的偏航角。</summary>
    private const float FlagYaw = 180f;

    private readonly Dictionary<string, PackedScene?> _modelScenes = new();
    private readonly HashSet<string> _unmodelledMonsters = new();

    private PackedScene? LoadModelScene(string path)
    {
        if (_modelScenes.TryGetValue(path, out var cached)) return cached;
        var packed = GD.Load<PackedScene>(path);
        if (packed is null) GD.PushError($"[InfinityLord] 模型加载失败：{path}");
        _modelScenes[path] = packed;
        return packed;
    }

    /// <summary>
    /// 实例化真模型。颜色、体型、装备全在 .glb 里 —— 运行时只加载，不改色、不缩放几何、不拼图元。
    /// </summary>
    private Node3D? InstantiateModel(string? path)
    {
        if (path is null || LoadModelScene(path) is not { } packed || packed.Instantiate() is not Node3D model)
            return null;
        model.Name = "Model";
        return model;
    }

    /// <summary>单位 → 模型路径。怪物名未登记时回退地精并就该名字告警一次（数据表加了新怪而美术没跟上）。</summary>
    private string? UnitModelPath(Unit unit)
    {
        if (unit.Side == Logic.Faction.Hero) return HeroModels.GetValueOrDefault(unit.Class);
        if (MonsterModels.TryGetValue(unit.Name, out var path)) return path;
        if (_unmodelledMonsters.Add(unit.Name))
            GD.PushWarning($"[InfinityLord] 怪物「{unit.Name}」没有对应模型，回退用地精。");
        return MonsterModels["地精"];
    }

    private static StandardMaterial3D Unshaded(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
    };

    private Node3D SpawnUnitModel(Unit unit)
    {
        var view = new Node3D
        {
            // 出生即落位：位移平滑从这一帧起才生效，否则新模型会从原点滑过来
            Position = new Vector3(unit.Position.X, 0.5f, unit.Position.Y),
        };
        var model = InstantiateModel(UnitModelPath(unit));
        if (model is not null)
        {
            model.Position = new Vector3(0, -0.5f, 0);   // 鞋底落在 y=0，模型原点仍留在 0.5 高度作拾取基准
            view.AddChild(model);
        }

        // 头顶血条与名牌的尺寸取自模型自带的 Anchor 标记 —— 巨魔不该用人类尺寸的细血条
        var anchor = model?.GetNodeOrNull<Marker3D>("Anchor");
        var barY = (anchor?.Position.Y ?? 1.7f) - 0.25f;
        var barWidth = anchor?.Position.X ?? 1.1f;

        // 头顶血条（背景黑条 + 前景彩条）；宽度取自模型的 Anchor
        var barBg = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(barWidth, 0.14f, 0.02f) },
            MaterialOverride = Unshaded(new Color(0.05f, 0.05f, 0.05f)),
            Position = new Vector3(0, barY, 0),
        };
        view.AddChild(barBg);

        var bar = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(barWidth - 0.1f, 0.09f, 0.02f) },
            MaterialOverride = Unshaded(new Color(0.3f, 1f, 0.4f)),
            Position = new Vector3(0, barY, 0.01f),
        };
        view.AddChild(bar);

        var label = new Label3D
        {
            Text = Loc.T(unit.Name),
            FontSize = 40,
            Position = new Vector3(0, barY + 0.5f, 0),
        };
        view.AddChild(label);

        _worldRoot.AddChild(view);
        _unitVisuals[unit.Id] = new UnitVisual(label, bar, barBg, model, new ModelAnimator(model));
        _unitPrevPos[unit.Id] = view.Position;
        _unitSpeedEma[unit.Id] = 0f;
        AttachBrain(view, unit);
        return view;
    }

    /// <summary>
    /// 行为树接管接线：第一名游侠挂上「桥 + 树」（组合节点是 GDScript、五条任务节点是 C#），其余单位不受影响。
    /// 只挂一名（本局接管过就不再挂，<see cref="_brainAttached"/>），因为这一轮要验的是「一名英雄听树」
    /// 能不能与世界一起跑通，不是全量换脑。桥离场后引用交还，但校验器不放开 —— 不换下一名。
    /// </summary>
    private void AttachBrain(Node3D view, Unit unit)
    {
        if (_brainAttached || unit.Side != Faction.Hero || unit.Class != HeroClass.Ranger) return;
        if (GD.Load<PackedScene>(BrainScenePath)?.Instantiate() is not Node brain)
        {
            GD.PushError($"[InfinityLord] 行为树场景加载失败，本局不接管：{BrainScenePath}");
            return;
        }

        var bridge = new HeroBrainBridge { Name = "BrainBridge" };
        brain.Name = "Brain";
        brain.Set("bridge", bridge);       // 必须先注入再入树：Brain._ready 里就要用 bridge
        view.AddChild(bridge);
        view.AddChild(brain);
        bridge.Wire(_world, unit.Id, Write);
        _brainBridge = bridge;
        _brainAttached = true;
        // 交还得靠离场信号：托管字段非 null 不等于节点还活着，挡不住下一步对死桥 EmitSignal
        bridge.TreeExiting += () => _brainBridge = null;
        GD.Print($"[InfinityLord] 英雄 {unit.Id}「{unit.Name}」交给行为树");
    }

    private Node3D SpawnBuildingModel(Building building)
    {
        var view = new Node3D();
        var model = InstantiateModel(ModelPathFor(building.Kind));
        if (model is not null)
        {
            model.Rotation = new Vector3(0, Mathf.DegToRad(BuildingYaw[building.Kind]), 0);
            view.AddChild(model);
        }
        var top = model?.GetNodeOrNull<Marker3D>("Anchor")?.Position.Y ?? 3f;

        var label = new Label3D
        {
            Name = "Plate",
            Text = building.Level > 1
                ? Loc.Fmt("{0} Lv.{1}", DisplayName(building), building.Level)
                : DisplayName(building),
            FontSize = building.Kind is BuildingKind.Palace or BuildingKind.BossLair ? 60 : 48,
            Position = new Vector3(0, top + 0.7f, 0),
        };
        view.AddChild(label);
        _buildingPlates[building.Id] = label;

        view.Position = new Vector3(building.Position.X, 0, building.Position.Y);
        _worldRoot.AddChild(view);
        return view;
    }

    /// <summary>建筑名牌的屏幕尺寸补偿（楼不会动，但相机会动 — 拉近了 60 号字糊满半屏）。</summary>
    private void RenderBuildingPlates()
    {
        var scale = Vector3.One * PlateScale;
        foreach (var plate in _buildingPlates.Values) plate.Scale = scale;
    }

    /// <summary>建筑展示名 — 表现层口径（逻辑层只有 BuildingKind 枚举）。</summary>
    private static string DisplayName(Building building) => KindDisplayName(building.Kind);

    /// <summary>
    /// 种类 → 展示名（名牌与幽灵名牌共用这一份）。按种类取是因为幽灵手上只有
    /// <see cref="BuildingKind"/>，还没有建成的那一座可读。
    /// </summary>
    private static string KindDisplayName(BuildingKind kind) => kind switch
    {
        BuildingKind.Palace => Loc.T("宫殿"),
        BuildingKind.BossLair => Loc.T("BOSS 巢穴"),
        BuildingKind.GuildWarrior => Loc.T("战士公会"),
        BuildingKind.GuildHall => Loc.T("游侠公会"),
        BuildingKind.Market => Loc.T("市场"),
        BuildingKind.Tavern => Loc.T("酒馆"),
        BuildingKind.Apothecary => Loc.T("药店"),
        _ => Loc.T("巢穴"),
    };

    // === 逻辑事件处理 ===

    /// <summary>波次播报：反馈标签 + 日志（切片 10 外环加压的可观测点 — 玩家知道该攒钱了）。</summary>
    private void OnWaveStarted(WaveRow wave)
    {
        ShowFeedback(Loc.Fmt("第 {0} 波：{1} ×{2} 压境！", wave.No, Loc.T(wave.Name), wave.Count));
        Log($"[InfinityLord] 第 {wave.No} 波 {wave.Name} ×{wave.Count} 从剧本入侵点落场（Tick {wave.AtTick}）。");
    }

    /// <summary>终幕降临：BOSS 巢穴模型 + 横幅式提示。</summary>
    private void OnBossArrived(Building boss)
    {
        _buildingModels[boss.Id] = SpawnBuildingModel(boss);
        ShowFeedback(Loc.Fmt("终幕：BOSS 巢穴降临（{0} 血）— 摧毁它即通关", boss.Health));
        Log($"[InfinityLord] BOSS 巢穴降临 @({boss.Position.X:F0},{boss.Position.Y:F0})，血量 {boss.Health}。");
    }

    /// <summary>
    /// 胜负判定（终局）—— 报给前端层，结算屏由公共层画。
    ///
    /// <para><b>这里原来自己画一个居中 Label</b>，三个原型各写一套结算 UI。
    /// 现在只递「标题 + 几行明细 + 按下去做什么」，摆版式归 <c>FrontEndLayer.SetResult</c>。</para>
    ///
    /// <para><b>不递「再来一局」</b>：这个原型没有重开入口（换世界要连录制器与表现层一起
    /// 重建，不是一颗按钮的事），摆一颗按了没反应的按钮比不摆更糟。
    /// 「退出游戏」那颗由前端层保证有（<c>SetResult</c> 末尾自动补）。</para>
    /// </summary>
    private void OnGameOver(GameOutcome outcome)
    {
        var won = outcome == GameOutcome.Victory;
        _front.NotifyRunEnded(new RunOutcome(
            won ? Loc.T("通关：BOSS 巢穴已被摧毁") : Loc.T("陷落：宫殿被摧毁"),
            [
                Loc.T(won ? "生死线大考：通关" : "生死线大考：判负"),
                Loc.Fmt("第 {0} 步判定", _world.Tick.ToString(CultureInfo.InvariantCulture)),
            ],
            _world.Tick,
            won,
            [("回到标题屏", () => { _front.CloseAll(); _front.Open(FrontEndScreen.Title); })]));

        Log($"[InfinityLord] 终局判定：{outcome}（Tick {_world.Tick}）。");
    }

    /// <summary>
    /// 视角读数标签工厂：左下角小字，同样运行时挂到 HUD（不改场景文件）。
    /// MouseFilter 显式 Ignore — 常驻标签若不忽略鼠标，会在它下面吞掉点选和中键跟随。
    /// </summary>
    private Label BuildCameraReadout()
    {
        var hud = _tickLabel.GetParent() as Control;
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0.6f),
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        // SetAnchorsPreset 只动锚点不动偏移，四个偏移必须自己给全：
        // 只设 left/bottom 会算出负宽高，标签存在但永远画不出来
        label.OffsetLeft = 16f;
        label.OffsetTop = -30f;
        label.OffsetRight = 1000f;
        label.OffsetBottom = -8f;
        hud!.AddChild(label);
        return label;
    }

    /// <summary>
    /// 出手 → 攻击动作。这一发只认「谁出手了」，来源那两条在 <see cref="SkillCues"/> 里分派：
    /// 单位↔单位读技能层的事件出口，单位↔建筑读世界的 CombatEvent（那半没进层）。
    /// 驱动器保证一次攻击只演一遍（演完之前位移不改变基动作，见 ModelAnimator）。
    /// </summary>
    private void OnUnitAttack(string attackerId)
    {
        if (_unitVisuals.TryGetValue(attackerId, out var visual))
            visual.Anim.TriggerAttack();
    }

    private void OnUnitDied(UnitDiedReport report)
    {
        if (_unitModels.TryGetValue(report.UnitId, out var view))
        {
            _unitModels.Remove(report.UnitId);
            var seconds = _unitVisuals.TryGetValue(report.UnitId, out var visual) ? visual.Anim.Die() : 0f;
            if (seconds > 0f)
            {
                // 演完再收尸。这是表现层唯一的挂起队列，只影响演出，绝不回写逻辑层。
                var fade = CreateTween();
                fade.TweenInterval(seconds);
                fade.TweenCallback(Callable.From(view.QueueFree));
            }
            else
            {
                view.QueueFree();
            }
        }
        _unitVisuals.Remove(report.UnitId);
        _unitPrevPos.Remove(report.UnitId);
        _unitSpeedEma.Remove(report.UnitId);
        // 跟的人死了就解锁 — 模型 QueueFree 延到帧末，相机架不能继续持有一个待释放节点
        if (_followedUnitId == report.UnitId)
        {
            _followedUnitId = null;
            _rig.FollowTarget(null);
        }
        Log($"[InfinityLord] {report.Name}({report.Side}) 阵亡。");
    }

    /// <summary>建筑被摧毁 → 清模型（与 UnitDied 同款事件驱动增删，模型保持无状态）。</summary>
    private void OnBuildingDestroyed(Building building)
    {
        if (_buildingModels.TryGetValue(building.Id, out var view))
        {
            view.QueueFree();
            _buildingModels.Remove(building.Id);
            _buildingPlates.Remove(building.Id);
        }
        Log($"[InfinityLord] 建筑 {building.Id}({building.Kind}) 被摧毁。");
    }

    /// <summary>巢穴远端重生 → 新模型落位（插队小项：清剿不终结威胁 — 世界保持活性）。</summary>
    private void OnBuildingRespawned(Building lair)
    {
        _buildingModels[lair.Id] = SpawnBuildingModel(lair);
        Log($"[InfinityLord] 巢穴 {lair.Id} 远端重生 @({lair.Position.X:F0},{lair.Position.Y:F0})。");
    }

    /// <summary>
    /// 营建建成 → 新模型落位（切片8 营建；事件驱动增删，模型无状态）。
    ///
    /// <para>跟的是模块的 <b>Completed</b>（LogicWorld.BuildingCompleted），不是 Placed：落座那一刻
    /// 楼还在工，工期接上之后那几秒不该先摆出一座成品。工期全 0 时两条同一次 Commit 里连发，
    /// 画面上与从前逐字一样。</para>
    /// </summary>
    private void OnBuildingCompleted(Building building)
    {
        if (_buildingModels.ContainsKey(building.Id))
        {
            return; // 已有一座（重复建成事件不许在同一处摆两座楼）
        }

        _buildingModels[building.Id] = SpawnBuildingModel(building);
    }

    // === 场景搭建 ===

    /// <summary>
    /// 相机架接管视角（GugaFramework 通用件 TopDownRig）— 场景里的 %Camera3D 原位复挂，.tscn 零改动
    /// （编辑器内存旧场景回写覆盖磁盘的老坑，与终局横幅同款"运行时挂节点"处理）。
    ///
    /// 初始机位换算自切片 10 手调的那一版：焦点 (22,0,6)、相机在其 +（0,82,68）处
    /// → 距离 106.5、俯角 50.3°、偏航 0。所以"接入前后画面一模一样"本身就是这一步的验收标准。
    /// 焦点取在宫殿(-8,0) 与 BOSS 降临点(85,45) 之间偏村庄的一侧 — 失败条件与胜利目标同框。
    /// </summary>
    private void SetupCameraRig()
    {
        _rig = new TopDownRig
        {
            ReferenceDistance = 106.5f,
            PitchDegrees = 50.3f,
            MinDistance = 25f,
            MaxDistance = 140f,
            // 俯角上下限都不在这里钉：公共层 2026-10-15 把「下限必须为正」判成 bug 改掉了，
            // 2026-10-15 用户又定了 89.99。缺省 −89.99°~89.99°（能绕到主体下方往上看）。
            // 上一行原来写死 MinPitchDegrees = 22f，那只是复述旧缺省。
            MaxPitchDegrees = 89.99f,
            // 战场世界范围 X ∈ [-18, 85]、Z ∈ [-25, 45]（宫殿/村庄/两座巢穴/入侵点/BOSS 点），留一圈余量
            BoundsMinXZ = new Vector2(-40f, -45f),
            BoundsMaxXZ = new Vector2(100f, 60f),
        };
        _worldRoot.AddChild(_rig);
        _rig.AttachCamera(_camera);
        _rig.SetPose(new Vector3(22f, 0f, 6f), 106.5f, 0f, 50.3f);
        _rig.SaveHome();
        // 中键单击与 Tab 都是"锁定跟随"的入口，区别只在点哪儿：前者用点击位置，后者用当前鼠标位置
        _rig.ViewClicked += ToggleFollowAt;
        _rig.FollowToggleRequested += () => ToggleFollowAt(GetViewport().GetMousePosition());
    }

    /// <summary>
    /// 低俯角看出去的地平线：俯角压到 37.5° 以下时画面上半截会抬到地面之上，
    /// 露出地面板边缘和清屏色 — 场景没有远景，只能把"世界的尽头"化成"远处的雾"：
    /// 天空底色与雾同色，地面在到达边界前淡进同一片颜色里。
    /// 环境光与 SDFGI 显式钉死，避免挂 WorldEnvironment 顺带把整体明暗改了。
    /// </summary>
    private void SetupHorizon()
    {
        var horizon = new Color(0.13f, 0.16f, 0.15f);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = horizon,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.3f, 0.3f, 0.3f),
            SdfgiEnabled = false,
            FogEnabled = true,
            FogLightColor = horizon,
            FogDensity = 0.0012f,
        };
        _worldRoot.AddChild(new WorldEnvironment { Name = "Horizon", Environment = env });
    }

    private void SetupLighting()
    {
        var sun = GetNode<DirectionalLight3D>("%Sun");
        sun.RotationDegrees = new Vector3(-55, -30, 0);
        sun.ShadowEnabled = true;

        // 地面参照板：要盖到最远机位（140）的取景边缘。实测 460 见边 — 方形板在偏航 55° 时
        // 角尖朝里，顶角露出虚空；1200 让最远机位也只剩"一片平原"，一块 quad 不花钱。
        var ground = new MeshInstance3D();
        var plane = new PlaneMesh { Size = new Vector2(1200, 1200) };
        ground.Mesh = plane;
        var groundMat = new StandardMaterial3D();
        groundMat.AlbedoColor = new Color(0.18f, 0.22f, 0.18f);
        ground.MaterialOverride = groundMat;
        ground.Position = new Vector3(22, 0, 8);
        _worldRoot.AddChild(ground);
    }
}
