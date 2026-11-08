#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.FrontEnd;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.InputLayer;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Prototypes.BeastArena.Logic;
using DemoFactory.Prototypes.BeastArena.Ui;
using Godot;

namespace DemoFactory.Prototypes.BeastArena;

/// <summary>
/// 启动档位 —— 同一份 <see cref="Main"/> 与 <see cref="ArenaWorld"/> 服务两个入口场景：
/// <c>Scenes/Main.tscn</c> 走 <see cref="Mvp"/>（现役回归基线），
/// <c>Scenes/VerticalSlice.tscn</c> 走 <see cref="VerticalSlice"/>（垂直切片）。
///
/// <para>档位只决定开局配置与流程目标，不决定世界结构：两条路建的是同一个世界。</para>
/// </summary>
public enum RunProfile
{
    /// <summary>现役灰盒口径。缺省档 —— <c>Main.tscn</c> 不写这一格。</summary>
    Mvp = 0,

    /// <summary>垂直切片口径。开局数值与现役相同，流程状态由后续任务接。</summary>
    VerticalSlice = 1,
}

/// <summary>
/// 巨兽竞技场主场景入口（挂在 <see cref="ArenaWorld"/> 上）。
///
/// <para>本期命题：一只巨兽驮着小镇在贫城 / 富城 / 沙城之间连续往返 —— 核心循环能不能闭合。</para>
///
/// 分层：
/// - 逻辑世界住 Scripts/Logic/，纯 C#、零引擎依赖；本文件这一侧只订阅它的流、不改它的数据（「表现层只读」）。
/// - 视角统一走 GugaFramework/Camera/TopDownRig 的锁定跟随（「相机单源」）。
/// - 调试桥走 DebugConsole.Attach + BridgeArgs，动词只转发世界里已有的方法（「桥只转发」、「标准七动词」）。
/// - 玩家输入走 InputMap action，不做裸键轮询（「输入走 InputMap」）。
/// </summary>
public partial class Main : Node
{
    /// <summary>开局种子。三处同源：<see cref="RestartRun"/> 与桥上 <c>new_run</c> 的缺省值都走它。</summary>
    public const int DefaultSeed = 20261015;

    /// <summary>
    /// 启动档位。<c>Main.tscn</c> 不写这一格（缺省 <see cref="RunProfile.Mvp"/>，现役行为一字不改），
    /// <c>VerticalSlice.tscn</c> 显式写 <see cref="RunProfile.VerticalSlice"/>。
    /// </summary>
    [Export] public RunProfile Profile { get; set; } = RunProfile.Mvp;

    private TopDownRig _rig = null!;
    private DeckView _deck = null!;
    private readonly SpeedControl _speed = new(tickRatePerSecond: 10.0);
    private double _tickAccumulator;

    /// <summary>性能采样账本（帧间隔、进程耗时、每帧推进步数、欠速秒）。</summary>
    private readonly PerfStats _perf = new();

    /// <summary>换局后跳过几帧再采样：建世界那一帧算加载，不混进游玩帧（spec「加载与场景切换单列」）。</summary>
    private int _perfSkipFrames;

    /// <summary>采样窗内的粒子峰值（效果节点数与申报粒子数）。</summary>
    private int _vfxPeakEmitters;
    private long _vfxPeakParticles;
    private long _vfxLifetimePeakParticles;

    /// <summary>读法分派：概念名与读法都来自公共层，引擎那三个读数口在本件递进去。</summary>
    private readonly InputRead _read = new(name => Input.IsActionJustPressed(name), name => Input.IsActionPressed(name),
        name => Input.GetActionStrength(name));

    private ArenaWorld? _world;

    /// <summary>前端层（菜单栈、标题屏、结算屏、退出）。</summary>
    private FrontEndLayer _front = null!;

    /// <summary>表现层那棵 Control 树（三层父节点、弹层容器、模态标记）。</summary>
    private UiRoot _ui = null!;

    /// <summary>uitest 通道的引擎侧助手（改根视口尺寸、按名字或文字找控件并按下）。</summary>
    private UiTestDriver _uiTest = null!;

    /// <summary>居中横幅（成功中性色、失败红色带原因）。</summary>
    private Banner _banner = null!;

    /// <summary>
    /// 最近一次注入口的回复（成功是 <c>ok</c>，失败是那一串拒绝文案）。
    /// 调试读数读它 —— <c>rejected:</c> 前缀该是一层，这一格一眼看得出是不是两层。
    /// </summary>
    private string _lastInjectReply = string.Empty;

    /// <summary>右上命令栏（停靠、拔锚、吞食、召回、经营、结算、暂停 / 倍速）。</summary>
    private CommandBar _commandBar = null!;

    /// <summary>经营弹层（市场 / 账本与工资 / 人员与帮工三栏；命令栏「经营」开它）。</summary>
    private OperationsPanel _operations = null!;

    /// <summary>吞食目标面板（命令栏「吞食」开它：目标由玩家点）。</summary>
    private ForagePanel _foragePanel = null!;

    /// <summary>终局结算（最终防守胜利后自动开一次；命令栏「结算」回看）。</summary>
    private SettlementPanel _settlementPanel = null!;

    /// <summary>现实耗时记账（表现层，不进逻辑快照）。</summary>
    private readonly RunTelemetry _telemetry = new();

    /// <summary>终局结算那一刻冻结的一份现实耗时（继续经营之后不再改写）。</summary>
    private RunTelemetrySnapshot? _settlementTelemetry;

    /// <summary>这一局的终局结算是不是已经自动开过一次（关掉之后不再抢屏）。</summary>
    private bool _settlementAutoOpened;

    /// <summary>巨兽面板（点兽身开它：位阶 / 形态 / 喂食 / 魔晶用途）。</summary>
    private BeastPanel _beastPanel = null!;

    /// <summary>传送弹层（右键传送阵开它：选人、矿石与材料、费用预览）。</summary>
    private TeleportDialog _teleportDialog = null!;

    /// <summary>拔起之后那只落位幽灵（轮廓跟鼠标、格色涂绿或红、落位与取消）。</summary>
    private PlacementGhost _placementGhost = null!;

    /// <summary>操作说明（标题屏那条目与 F1 都开合它）。</summary>
    private HelpPanel _help = null!;

    /// <summary>左下选中面板（点中一个人之后显示血 / 情绪 / 忠心与三颗按钮）。</summary>
    private SelectionPanel _selection = null!;

    /// <summary>右侧土壤面板（8 格：开挖 / 嵌入魔晶 / 清剿撤退 / 派到此格）。</summary>
    private SoilPanel _soil = null!;

    /// <summary>底部蓝图条（13 个固定槽位，点一下进建造模式）。</summary>
    private BlueprintBar _blueprintBar = null!;

    /// <summary>建造模式那只幽灵（轮廓、格色、落座）。</summary>
    private BuildGhost _buildGhost = null!;

    /// <summary>右键已建建筑的菜单（修理 / 拆除，镇政府开招募、竞技场留位）。</summary>
    private BuildingMenu _buildingMenu = null!;

    /// <summary>招募框（镇政府 →「招募」开它；五行五来路，付钱必得）。</summary>
    private RecruitDialog _recruitDialog = null!;

    /// <summary>战报（战斗结束自动开；也可从竞技场菜单再开）。</summary>
    private BattleReportPanel _battleReport = null!;

    /// <summary>已经自动弹出过的那一份战报的收场步号（-1 = 这一局还没弹过）。</summary>
    private long _lastReportTick = -1;

    /// <summary>屏幕点 → 世界目标。后续 ticket（选中、建造、抓放）都从这里取。</summary>
    private Picker _picker = null!;

    /// <summary>左键 / 右键 / F1 的路由与拖拽状态机。</summary>
    private InputRouter _input = null!;

    /// <summary>上帝之手：抓起镇民、跟手、三档落点。</summary>
    private DragController _drag = null!;

    /// <summary>
    /// 局开始闸：标题屏上「开始游戏」按下之前为假，此时<b>一步都不推进</b>。
    /// 与 <see cref="_runEnded"/> 是两道独立的闸：这一道管「还没开始」，那一道管「已经结束」。
    /// </summary>
    private bool _started;

    /// <summary>
    /// 这一局是否已经结束 —— 由前端层的局结束事件写，<b>不查世界的任何字段</b>。
    /// 推进循环原来在条件里读世界，那是三处原型里唯一一处表现层主动查世界。
    /// </summary>
    private bool _runEnded;

    public override void _Ready()
    {
        // 字体先装：缺省字体没有汉字字形，不装的话 HUD 那些中文上屏是整行空白
        // （实测记录见 SkillStageScene 类注）。装在建 HUD 之前。
        DemoFactory.GugaFramework.I18n.FontSetup.Install();

        SetupCameraRig();
        _deck = new DeckView(GetNode<Node3D>("World"));
        SetupUi();
        SetupInputMap();
        SetupDebugBridge();
        SetupFrontEnd();

        // 只建世界：不推进、不开屏、不报「局已开始」。标题屏由下面这一行显式打开。
        StartWorld(BuildConfig());
        _front.Open(FrontEndScreen.Title);
        Log("[BeastArena] 主场景已加载。");
    }

    /// <summary>后续 ticket（选中、建造、抓放）取这一份换算器。</summary>
    public Picker Picker => _picker;

    /// <summary>
    /// 逻辑世界（调试 eval 与 uitest 的只读入口）。表现层拿到它也只读 —— 「表现层只读」。
    /// </summary>
    public ArenaWorld? World => _world;

    /// <summary>
    /// UI 壳：三层 Control、横幅、操作说明、拾取换算与输入路由。
    ///
    /// <para>HUD 挂在 <see cref="UiRoot.WorldLayer"/> 上 —— 锚点给的是 <c>Control</c>，
    /// 不是 <c>CanvasLayer</c>（「落地配出图」）。</para>
    /// </summary>
    private void SetupUi()
    {
        _ui = new UiRoot(GetNode<CanvasLayer>("UILayer"));
        BuildHud(_ui.WorldLayer);

        // uitest 通道的引擎侧助手：只做「改根视口」「找控件并真的按下」两件事。
        // 驱动器在 src/Tools/UiTest.cs（纯 .NET），经调试桥 ui_viewport / ui_click 两个动词过来。
        _uiTest = new UiTestDriver { Name = "UiTestDriver" };
        AddChild(_uiTest);

        // 经营弹层：市场 / 账本与工资 / 人员与帮工三页并成一个入口（内部分栏）。
        // 三页都是现成面板的嵌入档，写路径仍各走各的 Inject。
        _operations = new OperationsPanel(_ui, () => _world, Inject);

        // 吞食目标面板：命令栏「吞食」开它，目标由玩家点；状态行照实写拒绝原因。
        _foragePanel = new ForagePanel(_ui, () => _world, Inject);

        // 终局结算：最终防守胜利后自动开一次；继续经营之后从命令栏「结算」回看同一份。
        _settlementPanel = new SettlementPanel(_ui, () => _world, Inject, () => _settlementTelemetry);

        // 巨兽面板：点兽身开它（拾取那一档是 PickTarget.Beast）。
        // 面板自己置模态、关时清；进阶 / 切形态 / 喂食 / 魔晶用途都由面板走 Inject。
        _beastPanel = new BeastPanel(_ui, () => _world, Inject);

        // 传送弹层：右键传送阵开它（仅停靠期）。选人与两件货都由弹层走 Inject。
        _teleportDialog = new TeleportDialog(_ui, () => _world, Inject);

        _commandBar = new CommandBar(_ui, () => _world, Inject, () => _speed.Multiplier, m => SetSpeed(m),
            LockBeast, OpenForage, OpenOperations, OpenSettlement);
        _banner = new Banner(_ui);
        _help = new HelpPanel(_ui);

        _picker = new Picker(_rig.Camera!, _rig, _deck, () => _world);

        // 左下选中面板：选中只记 id，内容每帧现读世界；三颗按钮的写路径走 Inject。
        // 「钉住 / 解除钉住」走 Main 那一档视角的接线（钉住状态独立于选中，所以另递两个口）。
        _selection = new SelectionPanel(_ui, () => _world, Inject, () => PinnedUnitId, TogglePinUnit);

        // 右侧土壤面板：挂在命令栏下方；选中从 SelectionPanel.SelectedId 现取，不另存一份。
        _soil = new SoilPanel(_ui, () => _world, Inject, () => _selection.SelectedId);

        // 底部蓝图条 → 建造模式：槽位只报「选了哪张蓝图」，幽灵与落座归 BuildGhost。
        _blueprintBar = new BlueprintBar(_ui, () => _world);
        _blueprintBar.BlueprintChosen += OnBlueprintChosen;

        // 幽灵挂在悬浮层上，跟着光标走；格色读建造模块的 Check（与落座同一条流水线）。
        _buildGhost = new BuildGhost(_deck, _picker, () => _world, Inject,
            () => _front is null || _front.IsOpen || _ui.IsModal);

        // 落位幽灵：拔起之后那只。跟建造幽灵是两件事，各挂各的。
        _placementGhost = new PlacementGhost(_deck, _picker, () => _world, Inject,
            () => _front is null || _front.IsOpen || _ui.IsModal);

        // 右键建筑菜单：修理 / 拆除走 Inject；镇政府那一格开招募框，竞技场那一格开赛 / 战报。
        _recruitDialog = new RecruitDialog(_ui, () => _world, Inject);
        _battleReport = new BattleReportPanel(_ui, () => _world, Inject);
        _buildingMenu = new BuildingMenu(_ui, () => _world, Inject, (text, ok) => _banner.Show(text, ok));
        _buildingMenu.RecruitRequested += _recruitDialog.Open;
        _buildingMenu.BattleReportRequested += _battleReport.Open;

        // 拔起 / 落位走成了 → 进放置模式；传送那一格 → 开传送弹层。
        _buildingMenu.PlacementRequested += OnPlacementRequested;
        _buildingMenu.TeleportRequested += OpenTeleport;

        // 标题屏 / 暂停屏 / 结算屏开着时世界不收拾取（那是前端层的模态）。
        // 操作说明自己开着时 F1 要能关掉它，所以单独递一口 helpOpen。
        _input = new InputRouter(_ui, _rig, () => _front is not null && _front.IsOpen, () => _help.IsOpen);
        AddChild(_input);
        _input.HelpToggled += _help.Toggle;
        _input.WorldClicked += OnWorldClicked;
        _input.RightClicked += OnWorldRightClicked;

        // 上帝之手：候选 / 抓取 / 跟手 / 落点。写路径仍只走 Inject
        // （成功与失败都会在里面刷 DeckView.Sync 与横幅）。松手时再判一次模态：按下之后
        // 才弹出的操作说明 / 战报不能把落点漏到世界上。
        _drag = new DragController(_input, _picker, _deck, () => _world, Inject,
            () => _ui.IsModal || (_front is not null && _front.IsOpen));
        AddChild(_drag);
    }

    /// <summary>
    /// 左键点选：建造模式里落座；否则点中人 → 选中、点兽身 → 开巨兽面板、点空地 → 取消选中。
    ///
    /// <para>甲板格<b>不改选中</b>（那是铺格子的地方）；建筑左键也不改选中 ——
    /// 建筑菜单归右键（<see cref="OnWorldRightClicked"/>）。</para>
    /// </summary>
    private void OnWorldClicked(Vector2 screen)
    {
        // 落位模式优先：在甲板上点一下落位；被拒就停在原地（横幅已由 Inject 写了原因）。
        if (_placementGhost.Active)
        {
            if (_placementGhost.TryDropAt(screen)) _placementGhost.Exit();
            return;
        }

        // 建造模式优先：在甲板上点一下落座；被拒就停在原地（横幅已由 Inject 写了原因）。
        if (_buildGhost.Active)
        {
            if (_buildGhost.TryPlaceAt(screen))
            {
                // 落座成功 = 这一张蓝图用完了：幽灵与槽位选中态一起退，下一发右键不再被当成「退出建造」。
                _buildGhost.Exit();
                _blueprintBar.ClearSelection();
            }

            return;
        }

        var pick = _picker.Pick(screen);
        switch (pick.Target)
        {
            case PickTarget.Person:
                _selection.Select(pick.Person);
                break;

            // 点兽身：开巨兽面板。<b>不改选中</b>（那是点人那一条的事）。
            case PickTarget.Beast:
                _beastPanel.Open();
                break;

            // 点空地 / 甲板空格都算「点空地」：取消选中（spec 那一格）。
            case PickTarget.Empty:
            case PickTarget.DeckCell:
                _selection.Clear();
                break;
        }
    }

    /// <summary>
    /// 右键：建造模式里退出建造；点在建筑上打开建筑菜单；点在地图空地上令巨兽驶向那一点。
    /// <b>不抢 Esc</b>（Esc 归前端层）。
    ///
    /// <para><b>右键拖动仍是转视角</b>：拖动那一路走 <see cref="TopDownRig"/> 的带按钮事件
    /// （<c>ViewClickedWithButton</c>），只有「按下即松、位移没过阈值」那一种才走到本件。</para>
    ///
    /// <para><b>点甲板不算点空地</b>：甲板格、站在甲板上的人、楼上的建筑都归原来那几档 ——
    /// 点建筑开菜单那一条照旧，点甲板与点人只是收起菜单，不发航行令。</para>
    /// </summary>
    private void OnWorldRightClicked(Vector2 screen)
    {
        // 落位模式：右键只取消（把那一栋放回它原来那一格），**不开右键菜单**。
        if (_placementGhost.Active)
        {
            _placementGhost.Cancel();
            _placementGhost.Exit();
            return;
        }

        if (_buildGhost.Active)
        {
            _buildGhost.Exit();
            _blueprintBar.ClearSelection();
            return;
        }

        var pick = _picker.Pick(screen);
        if (pick.Target == PickTarget.Building)
        {
            _buildingMenu.Open(screen, pick.BuildingId);
            return;
        }

        _buildingMenu.Close();

        // 点在甲板上（甲板格或站在甲板上的人）不当成「点地图空地」：那一格不开船。
        if (pick.Target != PickTarget.Empty) return;

        // 点在地图空地上：令巨兽驶向那一点（走 Inject —— 同一口写路径，进 Journal、能重放）。
        if (!TryMapGroundPoint(screen, out var ground)) return;

        Inject("SailTo", ReplayArgs.Join(ReplayArgs.Number(ground.X), ReplayArgs.Number(ground.Z)));
    }

    /// <summary>
    /// 屏幕点 → 大地图地面上那一点（世界米）。
    ///
    /// <para><b>为什么不在镇面那一档高度上求交</b>：镇面在 <see cref="DeckView.TownWorldY"/>
    /// （63.34 米）高处，而大地图地面在 y=0。战略档俯角 22° 那一档，同一条视线上两面差
    /// 63.34 ÷ tan(22°) ≈ 157 米 —— 拿镇面那一档的点当目标，兽会开到玩家点的位置以北一百多米。
    /// 拾取器那一档（<c>Picker.TryGroundPoint</c>）是给镇面用的（点甲板格、点建筑、点人），
    /// 地图空地是另一个面，本件自己求交。</para>
    /// </summary>
    private bool TryMapGroundPoint(Vector2 screen, out Vector3 ground)
    {
        ground = Vector3.Zero;

        var camera = _rig.Camera;
        if (camera is null) return false;

        var origin = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);

        // 视线与地面平行时没有交点；镜头上仰到地平线以上也是这一档。
        if (Math.Abs(direction.Y) < 1e-6f) return false;

        var distance = (DeckView.GroundY - origin.Y) / direction.Y;
        if (distance <= 0f) return false;

        ground = origin + (direction * distance);
        return true;
    }

    /// <summary>蓝图条那一发：空串 = 退出建造模式，否则把幽灵切到那一张蓝图。</summary>
    private void OnBlueprintChosen(string blueprintId)
    {
        if (blueprintId.Length == 0)
        {
            _buildGhost.Exit();
            return;
        }

        _buildingMenu.Close();
        _buildGhost.Enter(blueprintId);
    }

    /// <summary>
    /// 前端层接线（与另两个原型同一份五行接法）。
    ///
    /// <para><b>标题屏条目由这一侧递</b>：框架不认识「开始游戏」在这个原型那边跳去哪
    /// （要开推进、要报局开始、要换世界），那条线归原型。</para>
    ///
    /// <para><b>局结束那一格：接线在，触发口由 <see cref="ReportRunEnded"/> 递</b>。
    /// 新版逻辑世界是连续循环，攻破之后停摆三个游戏日再恢复，<b>没有终局</b>
    /// （见 <c>ArenaWorld.StandstillUntilTick</c>），所以这一侧不轮询世界去问「打完了没有」——
    /// 那是「表现层只读」要防的那一类倒挂。收尾那一发改由调试桥调
    /// <c>call ReportRunEnded "标题"</c>：冻结与结算屏走的是同一条
    /// <see cref="OnRunEnded"/>，与另两个原型同一份接法。</para>
    /// </summary>
    private void SetupFrontEnd()
    {
        _front = new FrontEndLayer
        {
            Name = "FrontEnd",
            // 开局停在标题屏上：误按 Esc 不能把「开始游戏」关掉（另两个原型照旧缺省 true）。
            DismissTitleOnEsc = false,
        };
        AddChild(_front);

        // 前端层（标题屏 / 暂停屏 / 设置 / 结算）挂在 UILayer 之外、自成一张 CanvasLayer，
        // 所以 ui_rects 那一口要单独注册它 —— 否则「开始游戏」那一颗的矩形取不到，
        // 真机验收第一下又得回到目测坐标（这正是 ui_rects 要消灭的东西）。
        _ui.RegisterLayer("front", _front);

        _front.PauseRequested += paused =>
        {
            if (_world is null) return;
            if (paused) _world.Pause();
            else _world.Resume();
        };
        _front.QuitRequested += () => GetTree().Quit();

        // Esc 逐层关闭：游戏自己的弹层（战报 / 巨兽面板 / 经营 / 吞食 / 结算…）比暂停菜单更靠上。
        // 拦截器收掉一层就吃掉这一发；收不掉才轮到菜单栈开暂停屏。
        _front.GameEscapeInterceptor = () => _ui.TryCloseTopModal();

        // 三条条目按 spec：开始游戏 / 操作说明 / 退出。
        // 「开始游戏」那一条是整条开局链：关屏 → 报局开始 → 开推进。
        _front.SetTitleEntries(
            (Loc.T("开始游戏"), StartGame),
            (Loc.T("操作说明"), _help.Toggle),
            (Loc.T("退出"), _front.RequestQuit));

        // 暂停屏按切片规格「标题与暂停」补齐五条：继续与设置与退出游戏由框架摆，
        // 这里只递操作说明 / 重开一局 / 回标题三条。
        _front.SetPauseEntries(
            (Loc.T("操作说明"), _help.Toggle),
            (Loc.T("重开一局"), () => RestartRun()),
            (Loc.T("回标题"), ReturnToTitle));
        _front.RunStarted += () => { _runEnded = false; };

        // 局结束 = 冻住时间轴（三个原型统一的口径，真值在基类那一处推进口）。
        // 冻在事件里而不在推进循环里：循环那一格判的是「还推不推」，冻住之后 Advance 直接回 false，
        // 两者是同一件事的两道闸，而真值只有 TimeFrozen 那一处。
        _front.RunEnded += _ =>
        {
            _runEnded = true;
            _world?.FreezeTime();
        };

        foreach (var (verb, handler) in FrontEndVerbs.Vocabulary(_front))
            _bridge?.Register(verb, handler);
    }

    /// <summary>
    /// HUD —— 常驻五组（spec「信息结构」：当前目标、下一站、关键资源、双成长进度、饱食 / 消化 / 形态），
    /// 一组一个组名 + 一行读数；顶上另有罗盘时钟一行、底下另有立场与人口两行辅助读数。
    /// 行名与读数格按对接面清单「HUD」那张表走，行名与 <see cref="DeckView"/> 里那一处同名。
    /// 锚点落在 <see cref="UiRoot.WorldLayer"/> 这块 <c>Control</c> 上
    /// （<c>CanvasLayer</c> 不是 <c>Control</c>，锚点预设对它无效，「落地配出图」）。
    ///
    /// <para><b>固定宽 560 且开自动折行</b>（spec）：一行最长那句会把右侧命令栏顶出去。
    /// 折行只影响长句的换行位置，不改任何一段的字。</para>
    /// </summary>
    private void BuildHud(Control worldLayer)
    {
        var root = new Control
        {
            Name = "Hud",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        worldLayer.AddChild(root);

        // 摆进左上角一块固定宽度的盒子里：VBoxContainer 自己按子控件的最小尺寸排，
        // 不给宽度的话最长那一行会把整块面板撑到画面外（「落地配出图」那一族）。
        var box = new VBoxContainer
        {
            Name = "Rows",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(UiRoot.HudLeftMargin, UiRoot.HudTopMargin),
            CustomMinimumSize = new Vector2(UiRoot.HudWidth, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        box.AddThemeConstantOverride("separation", 3);
        root.AddChild(box);

        // 「原八行里的在镇游客 / 战斗摘要」一行按对接面清单 HUD 表去掉：
        // 战斗摘要归命令栏的防守倒计时与战报面板，游客数归调试读数。
        AddHudRow(box, "Clock", header: null, small: true);
        AddHudRow(box, "Goal", Loc.T("当前目标"));
        AddHudRow(box, "Station", Loc.T("下一站"));
        AddHudRow(box, "Accounts", Loc.T("关键资源"));
        AddHudRow(box, "Growth", Loc.T("双成长进度"));
        AddHudRow(box, "Beast", Loc.T("饱食 · 消化 · 形态"));
        AddHudRow(box, "Standing", Loc.T("立场"), small: true);
        AddHudRow(box, "Population", Loc.T("人口"), small: true);

        _deck.AttachHud(box);
    }

    /// <summary>HUD 一行：先摆组名（小字冷色），再摆读数（名字给 <see cref="DeckView.AttachHud"/> 取）。</summary>
    private static void AddHudRow(VBoxContainer box, string name, string? header, bool small = false)
    {
        if (header is not null)
        {
            var caption = new Label
            {
                Text = header,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            caption.AddThemeFontSizeOverride("font_size", 12);
            caption.Modulate = new Color(0.66f, 0.72f, 0.82f);
            box.AddChild(caption);
        }

        box.AddChild(new Label
        {
            Name = name,
            HorizontalAlignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        if (small || header is null)
        {
            var row = box.GetChild<Label>(box.GetChildCount() - 1);
            row.AddThemeFontSizeOverride("font_size", 12);
        }
    }

    public override void _Process(double delta)
    {
        if (_world is null) return;

        PollTimeKnobs();
        CheckPinnedTarget();

        // 标题屏上一步都不推进（_started 闸）：累积器也不攒，否则开局的头几秒会一口气补回来。
        // 位姿仍然每帧刷 —— 地标要量到尺寸才摆得准，那几帧不能省。
        if (!_started)
        {
            _deck.SyncPose(_world, 0.0);
            return;
        }

        // 现实耗时只从「开始游戏」之后算（标题屏挂着的时间不进结算）。
        _telemetry.Tick(delta);

        // 时间可控：倍率只乘在逻辑流速上，累积器的单位是「步」，攒满 1 步才推 1 步（「时钟解耦」、「时间基准」）。
        // 停止推进看的是 _runEnded（局结束事件写的那一格），不读世界的任何字段。
        var tickBefore = _world.Tick;
        var stepStartUsec = Time.GetTicksUsec();
        _tickAccumulator += _speed.TicksPerFrame(delta);
        while (_tickAccumulator >= 1.0 && !_runEnded)
        {
            _tickAccumulator -= 1.0;
            _world.Advance(1);
            SyncDeck();
        }
        var stepMs = (Time.GetTicksUsec() - stepStartUsec) / 1000.0;
        SamplePerf(delta, _world.Tick - tickBefore, stepMs);

        // 位姿**每个渲染帧**都刷：内容仍按逻辑步更新，位姿不能 ——
        // 逻辑 10 步/秒而渲染 30~60 帧/秒，只在步上刷位姿就是三帧一动的台阶。
        // 累积器剩下的那不足一步的量就是补偿量：把它按比例补进去，位姿于是连续。
        _deck.SyncPose(_world, _tickAccumulator);

        // 战斗结束自动弹战报：读的是逻辑层收场那一刻写下的那一份读数（不是每帧重算）。
        OpenNewReport();

        // 最终防守胜利后先看战报，关掉之后再进终局结算（spec「最终防守」/「终局结算」）。
        OpenSettlementIfNeeded();
    }

    /// <summary>
    /// 记一帧性能样本。只采游玩帧：标题屏与换局那几帧不采，暂停与收场后的帧不判欠速。
    /// <c>step_ms</c> 是这一帧全部逻辑推进加表现同步的耗时；<c>process_ms</c> 是引擎的逐帧处理耗时（诊断）。
    /// </summary>
    private void SamplePerf(double delta, long steps, double stepMs)
    {
        if (_perfSkipFrames > 0)
        {
            _perfSkipFrames--;
            return;
        }
        if (delta <= 0.0) return;

        var frameMs = delta * 1000.0;
        var processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        var expected = _runEnded || _world is { IsPaused: true } ? 0.0 : 10.0 * _speed.Multiplier;
        _perf.Add(frameMs, stepMs, processMs, steps, expected);

        var emitters = _deck.VfxEmitters;
        if (emitters > _vfxPeakEmitters) _vfxPeakEmitters = emitters;
        var particles = _deck.VfxParticles;
        if (particles > _vfxPeakParticles) _vfxPeakParticles = particles;
        if (particles > _vfxLifetimePeakParticles) _vfxLifetimePeakParticles = particles;
    }

    /// <summary>
    /// 拔锚横幅 —— 只在 <c>PullAnchor</c> 命令成功那一刻弹一次（对接面清单冲突 C-6：
    /// 原实现每帧轮询 <c>Battle.IsDefence</c> 的状态翻转闩，改订命令路径）。
    /// 防守只有拔锚能开（规格「普通防守」），所以命令那一刻就是横幅该出现的那一刻；
    /// 富城双成长完成那一拔进最终防守，段读数当场可分。
    /// </summary>
    private void AnnounceAnchorPulled()
    {
        if (_world is null) return;

        var hours = ArenaTuning.DefenceGameHours.ToString("0.##", CultureInfo.InvariantCulture);
        _banner.Show(_world.Flow.Stage == RunStage.FinalDefence
            ? Loc.Fmt("已拔锚，最终防守 {0} 游戏小时", hours)
            : Loc.Fmt("已拔锚，防守 {0} 游戏小时", hours));
    }

    /// <summary>
    /// 防守收场横幅 —— 只订 E-17 <see cref="BattleSim.DefenceSettled"/>（冲突 C-6：原攻破横幅走
    /// <c>TownHallBreached</c> 轮询，改订事件后由 <c>won == false</c> 那一分支给出）。
    ///
    /// <para>办赛与洞穴清剿不发这一条事件，所以那两种收场不会误弹防守横幅
    /// （对接面清单 E-17 触发时机那一栏写明）。</para>
    /// </summary>
    private void OnDefenceSettled(bool won)
    {
        if (won)
        {
            _banner.Show(Loc.T("防守守住：镇政府无恙"));
            return;
        }

        _banner.Show(Loc.T("镇政府被攻破：城卫队当格解救并记债"), success: false);
    }

    /// <summary>
    /// 位阶进阶到点：弹一条横幅（「进阶完成 · 二阶」那一档）。
    ///
    /// <para><b>走事件不轮询</b>：逻辑层在仪式到点那一步抛一次
    /// <see cref="ArenaWorld.TierAdvanced"/>，本方法只接那一次 ——
    /// 每帧去问「位阶变了没有」正是「表现层只读」要防的那一类倒挂。</para>
    /// </summary>
    private void OnTierAdvanced(BeastTier tier)
    {
        _banner.Show(Loc.Fmt("进阶完成 · {0}", Loc.T(BeastTierText.NameOf(tier))));
    }

    /// <summary>
    /// 形态切换：弹一条横幅（「形态切换 · 荒漠态」那一档）。
    ///
    /// <para><b>走事件不轮询</b>：逻辑层只在形态真变那一次抛
    /// <see cref="ArenaWorld.FormSwitched"/>，本方法只接那一次 ——
    /// 未解锁与幂等切换都不抛，屏上也就不会有那两条。</para>
    /// </summary>
    private void OnFormSwitched(BeastForm form)
    {
        _banner.Show(Loc.Fmt("形态切换 · {0}", Loc.T(BeastFormText.NameOf(form))));
    }

    /// <summary>形态第一次解锁：弹一条横幅（与切换那一条分开，玩家知道「刚解锁」）。</summary>
    private void OnFormUnlocked(BeastForm form)
    {
        _banner.Show(Loc.Fmt("解锁形态 · {0}", Loc.T(BeastFormText.NameOf(form))));
    }

    /// <summary>吞食完成：补了多少饱食、记了多少消化。</summary>
    private void OnEatCompleted(EatResult result)
    {
        _banner.Show(Loc.Fmt("吞食 · 饱食 +{0} / 消化 +{1}",
            result.SatiationGain.ToString("0.#", CultureInfo.InvariantCulture),
            result.DigestionGain.ToString("0.#", CultureInfo.InvariantCulture)));
    }

    /// <summary>魔晶落到甲板：提醒玩家让工人或帮工搬进仓库。</summary>
    private void OnCrystalsProduced(int count)
    {
        _banner.Show(Loc.Fmt("魔晶 ×{0} 落在甲板", count.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>自主觅食没找到对象：把原因照实说一遍（不弹空白横幅）。</summary>
    private void OnForageGaveUp(ForageGiveUpReason reason)
    {
        _banner.Show(ForageGiveUpText.TextOf(reason), success: false);
    }

    /// <summary>魔晶用途落地：补魔 / 治疗 / 修理各一句。</summary>
    private void OnCrystalUsed(CrystalUseRecord record)
    {
        var what = record.Use switch
        {
            CrystalUseKind.Mana => "补魔 5 格",
            CrystalUseKind.Heal => "治疗",
            _ => "立即修好",
        };
        _banner.Show(Loc.Fmt("魔晶 · {0}", what));
    }

    /// <summary>魔晶出售：报数量与成交价。</summary>
    private void OnCrystalSold(CrystalSaleRecord record)
    {
        _banner.Show(Loc.Fmt("售出魔晶 ×{0} · {1} 金/单位",
            record.Count.ToString(CultureInfo.InvariantCulture),
            record.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// 新战报自动上屏：<b>只在这一份还没弹过时</b>开一次。
    ///
    /// <para>场合不限：办赛、防守、洞穴清剿三种收场都出战报，打完都该让玩家看见
    /// （交互规格 9）。以前只放 Match 一种，防守打完那一份只能自己去竞技场菜单里翻。</para>
    ///
    /// <para>别的弹层正开着（招募框 / 右键菜单 / 操作说明）就先不抢模态 ——
    /// <c>_lastReportTick</c> 也没记下，等它关掉之后下一帧照常弹。</para>
    /// </summary>
    private void OpenNewReport()
    {
        if (_world?.Battle.LastReport is not { } report) return;

        // 只弹「比上一份更新」的战报：最终防守重试会把世界还原到战前快照，
        // LastReport 跟着退回更早那一场 —— 等值比较会把它当成新战报又弹一次，挡住重试后的真正收场。
        if (report.EndedTick <= _lastReportTick) return;

        if (_ui.IsModal) return;

        // 暂停屏 / 标题屏开着时不抢开：战报不能叠在暂停屏上面（那两屏是前端层的整屏模态）。
        if (_front.IsOpen) return;

        _lastReportTick = report.EndedTick;
        _buildingMenu.Close();
        _battleReport.Open();
    }

    /// <summary>终局结算那一刻：现实耗时取一份快照（之后继续经营不再改写）。</summary>
    private void OnSettlementOpened() => _settlementTelemetry = _telemetry.Snapshot();

    /// <summary>
    /// 终局结算自动上屏 —— <b>每局只开一次</b>，而且要等战报先关掉（spec：胜利后先战报、再结算）。
    /// 判定只读流程段与结算那一份，不轮询胜负。
    /// </summary>
    private void OpenSettlementIfNeeded()
    {
        if (_settlementAutoOpened) return;
        if (_world?.Flow.Stage != RunStage.Settlement) return;
        if (_world.Settlement is null) return;

        // 战报 / 其它弹层开着、或者前端层占屏时不抢：等它们关掉之后下一帧照常开。
        if (_ui.IsModal || _front.IsOpen) return;

        _settlementAutoOpened = true;
        _settlementPanel.Open();
    }

    /// <summary>
    /// 动画时钟倍率：与排兵布阵同一份口径（<c>Math.Max(1.0, Multiplier)</c>）——
    /// 慢放时动作照常长度演，只有快进才让动作跟着时间轴一起走。
    /// </summary>
    private float AnimGameSpeed => (float)Math.Max(1.0, _speed.Multiplier);

    // === 相机：只管相机，跟谁由原型判（「相机单源」） ===

    /// <summary>战略档距离（米）—— 整兽整镇那一档。</summary>
    public const float StrategicDistanceMeters = 105f;

    /// <summary>战术档距离（米）—— 街上的人那一档。</summary>
    public const float TacticalDistanceMeters = 26f;

    /// <summary>
    /// 战略档的焦点高度（米）—— 镇面高度的一半。
    /// 取一半而不是镇面本身：钉在镇面高度时视线与镇面齐平，倒三角那座山与锥尖底下那道悬浮场
    /// 全被镇面本身挡住，画面上又回到「一块木板浮着」。
    /// </summary>
    public const float StrategicFocusHeightMeters = DeckView.TownWorldY * 0.5f;

    /// <summary>
    /// 战术档的焦点高度（米）—— 镇面本身。
    /// 钉住的人在镇面上，焦点得压在镇面上才看得见他：焦点低 31.67 米时，
    /// 人相对视线偏出 64°，出了半 FOV，画面上根本没有他。
    /// </summary>
    public const float TacticalFocusHeightMeters = DeckView.TownWorldY;

    /// <summary>
    /// 战略档的相机垂直 FOV（度）—— 画幅比例（可见宽 ÷ 可见高）由它定。
    /// 取 75 是**现状机位那一档**的取景（Godot 缺省值，2026-10-15 出图定的「正够装全」那一版）：
    /// 105 米那一档看得见 161.2 米高、286.6 米宽，整兽连四条腿带脚都在画面里。
    /// 53° 那一档在同一个 105 米看得见 143.6 米高、255.2 米宽，实测兽占 916×864 像素、
    /// 下沿出框 191 像素 —— 装不下整兽，所以战略档只能是 75。
    /// </summary>
    public const float StrategicFovDegrees = 75f;

    /// <summary>
    /// 战术档的相机垂直 FOV（度）—— 街景那一档。
    /// 取 53 是照设计那一档的口径（<c>design.md</c>「两档视角」那一段：
    /// 可见高度 = 2 × 距离 × tan(FOV ÷ 2)）：26 米那一档看得见 26.0 米高、46.2 米宽，
    /// 正好一条街（甲板 48 米见方）。75° 那一档 26 米看得见 71 米宽 —— 整个镇加外圈，不是「一条街」。
    /// </summary>
    public const float TacticalFovDegrees = 53f;

    /// <summary>钉住的那个人的 id（空串 = 没钉）。钉住状态独立于选中。</summary>
    private string _pinnedUnitId = string.Empty;

    private void SetupCameraRig()
    {
        var world = GetNode<Node3D>("World");

        // 一盏方向光：场景里没有默认光源，不加这一盏整幅画面是黑的（出图复核抓到的第二个问题）。
        // 地形改成不透明圆盘之后影子落得上去了；把太阳当成有角直径的面光源，影子边缘软一档，
        // 兽与城投下来的影子不至于变成硬黑斑（旧版地形不吃影，就是因为怕这块黑斑）。
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.6f,
            ShadowEnabled = true,
            LightAngularDistance = 1.0f,
            ShadowBlur = 1.0f,
            ShadowOpacity = 0.8f,
        };
        sun.RotationDegrees = new Vector3(-52f, -38f, 0f);
        world.AddChild(sun);

        SetupEnvironment(world);

        var rig = new TopDownRig
        {
            // 巨兽在大地图上自己走，边界要盖住整张大地图，否则会被钳住。
            // 大地图逻辑坐标 0..400 米（逻辑坐标即世界坐标，一把尺子），
            // 边界按它每边再放 80 米余量（巨兽含吻含尾 89.6 米）。
            // 注意跟随路径不过 ClampFocus（`TopDownRig._Process` 直接读目标全局坐标），
            // 所以这一格只管平移与定点摆位，锁跟着走时钳不到。
            BoundsMinXZ = new Vector2(-120f, -120f),
            BoundsMaxXZ = new Vector2(520f, 520f),

            // **焦点高度那一格是这一轮新加的**（2026-10-15）：焦点本来恒被压平到 y=0，
            // 而镇地面在 51.3 米高处 —— 压平就等于镜头一直盯着小镇脚下，
            // 巨兽的身体与四条腿永远在画面下沿之外。之前那台架子只能靠「把俯角抬到 68°」
            // 把主体拉回画面中央，而那样正好是剪影最差的一档（正俯视）。
            // 这一格把「主体离地多高」显式交给原型，俯角于是可以低回来看得见腿。
            //
            // **取「镇面高度的一半」**（2026-10-15 浮山改造后重定）：焦点本来恒被压平到 y=0，
            // 而这一件的主体从地面一直到 63.34 米高的镇面 —— 压平就等于镜头一直盯着它脚下。
            // 这一格把「主体离地多高」显式交给原型。
            //
            // 取一半（31.67 米）而不是镇面本身：钉在镇面高度时视线与镇面齐平，
            // 倒三角那座山与锥尖底下那道悬浮场全被镇面本身挡住，画面上又回到「一块木板浮着」。
            // 焦点压在一半，兽身与四条腿落下半张、浮山与镇面占上另外半张，一眼看得出「山背着镇」。
            FocusHeightMeters = StrategicFocusHeightMeters,

            // 铺上地面之后 140 米那一档画面下半张全是空地；
            // 浮山改造之后主体又长高了 26 米 —— 140 米那一档连山带兽装不进画面。
            // 收下限放到 12 米（贴脸看镇面）、上限放到 320 米（一眼看全兽 + 山 + 地面）。
            MinDistance = 12f,
            MaxDistance = 320f,

            // **自由俯仰**：俯角上下限缺省就是 −89.99°~89.99°（公共层 2026-10-15 那一轮把
            // 「下限必须为正」判成 bug 改掉了，同一天用户又定了 89.99），所以这里不用再显式打开任何开关。
            // 这一件世界**一半的信息在主体的下面**：倒三角那座山的山体与锥尖、
            // 锥尖与底座之间那道悬浮场，全在镇面之下 ——
            // 旧的 22° 下限那一档，永远只能从上面看一块木板。

            // **右键拖拽与中键等同**（2026-10-15，用户提的「将右键的作用与中键等同」）。
            // 公共层那一格缺省是关的，右键在别的原型那里已经被派了别的意思
            // （无限领主拿它退出营建模式），所以由本原型显式打开 —— 见 TopDownRig 那一格的注记。
            RightButtonOrbits = true,
        };
        world.AddChild(rig);

        var camera = new Camera3D { Name = "Camera3D", Fov = StrategicFovDegrees };
        world.AddChild(camera);
        rig.AttachCamera(camera);
        // 机位（3/4 侧俯）：焦点 / 距离 / 偏航 / 俯角，**这一格是重新出图定的**。
        // 浮山改造之后主体从 30 米高的镇面变成「兽 30 米 + 山 27 米」共 63 米高的一摞，
        // 105 米那一档正够装全（2026-10-15 出图实测）；140 是那台架子的旧上限，
        // 铺上地面之后那一档画面下半张全是空地。取 **105 / 22°**：兽、底座、悬浮场、
        // 倒三角与地面都占住画面中段。俯角再压低就看不全山，再抬高就回到「一块木板」的读法。
        // 偏航取 205°：巨兽沿 +X 出发，34° 是**顺着兽身看**，那条路上镇地面正好挡住躯干与四肢；
        // 205° 是斜侧后方，同一组参数下躯干、四条腿与那颗头才都在画面里。
        // 巨兽开出去之后朝向会转，这一格随之改变侧面程度 —— 那是「锁跟随 + 固定偏航」的固有结果。
        // 想要贴着山体底面看，把俯角往负的方向拖即可，不必改这一格。
        rig.SetPose(new Vector3(0f, 0f, 0f), 105f, 205f, 22f);
        rig.SaveHome();
        _rig = rig;
    }

    /// <summary>
    /// 天光与雾 —— <b>有了地面就得有天</b>（2026-10-15 用户提「添加简易的地面」那一轮补的）。
    ///
    /// <para>早先场景里只有一盏方向灯、背景是引擎默认的那块深灰：地面铺上之后，
    /// 画面上半张仍是纯色深灰、地面边缘在天地之间切出一条硬直线 ——
    /// 有地面反而比没有更假。补三样：天顶渐变的天空（给天地一个可读的边界）、
    /// 天光作环境光（背光面不至于全黑）、指数雾（远处地面化进天色里，收掉那条边）。</para>
    ///
    /// <para>纯环境设置，不含任何造型 —— 与 <see cref="DeckView.BuildGround"/> 那一块地面同一族。</para>
    /// </summary>
    private static void SetupEnvironment(Node3D world)
    {
        var env = new WorldEnvironment
        {
            Name = "Env",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky
                {
                    SkyMaterial = new ProceduralSkyMaterial
                    {
                        SkyTopColor = new Color(0.33f, 0.48f, 0.70f),
                        SkyHorizonColor = new Color(0.72f, 0.76f, 0.78f),
                        GroundBottomColor = new Color(0.40f, 0.42f, 0.38f),
                        GroundHorizonColor = new Color(0.72f, 0.76f, 0.78f),
                    },
                },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.9f,

                // **辉光与色调映射（2026-10-15 底座改圆形那一轮补的）**：
                // 能量圈、镇地面板沿那圈亮边、火盆与窗子全是自发光材质，
                // 而方向光能量 1.6 乘上去就过了 1.0 —— 缺了色调映射，那些格子在画面上
                // 一律曝成**白塑料**：金色能量圈看着像白色垫圈，那正是「读不出是磁悬浮」那一格。
                // Filmic 保住高光段的色相，辉光再加一层晕，两者一起才让发光的东西读成「在发光」。
                //
                // 阈值 1.2：只有真的自发光那一档晕起来，普通受光面（石头、木板、兽皮、地面）不参与。
                //
                // **强度 0.65 → 0.30、阈值 1.0 → 1.2**（2026-10-15 出图实测）：0.65 太满，
                // 连地面与兽皮都糊进一层白雾，整幅画褪成灰蓝；Filmic 色调映射一并去掉，
                // 它压掉对比度，兽背与镇子都变灰。收回来之后只有发光的那几格晕起来，其余画面一点不动。
                GlowEnabled = true,
                GlowIntensity = 0.30f,
                GlowStrength = 1.0f,
                GlowBloom = 0.04f,
                GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
                GlowHdrThreshold = 1.2f,

                // 指数雾：远处地面化进天色，地面边缘那条硬线于是看不出来。
                FogEnabled = true,
                FogMode = Godot.Environment.FogModeEnum.Exponential,
                FogLightColor = new Color(0.72f, 0.76f, 0.78f),
                FogDensity = 0.0016f,
            },
        };
        world.AddChild(env);
    }

    /// <summary>锁定跟随巨兽甲板 — 返回摆完的读数。</summary>
    public string LockOnBeast(Node3D beast)
    {
        _rig.FollowTarget(beast);
        return _rig.DebugReadout();
    }

    /// <summary>解除锁定（<c>FollowTarget</c> 传 null 即解）。</summary>
    public string UnlockCamera()
    {
        _rig.FollowTarget(null);
        return _rig.DebugReadout();
    }

    // === 两档视角：战略 105 米 / 战术 26 米 ===

    /// <summary>当前钉住的那个人的 id（空串 = 没钉）。</summary>
    public string PinnedUnitId => _pinnedUnitId;

    /// <summary>
    /// 锁巨兽 —— 焦点跟兽、距离进战略档，顺带解除钉住。
    /// 右上命令栏那一颗按钮走这一口；开局也是这个状态。
    /// </summary>
    public void LockBeast()
    {
        _pinnedUnitId = string.Empty;
        _rig.SetFov(StrategicFovDegrees);

        // 高度先改再跟：<c>FollowTarget</c> 落焦点时用的是当下的 FocusHeightMeters。
        _rig.FocusHeightMeters = StrategicFocusHeightMeters;
        LockOnBeast(_deck.BeastNode);
        _rig.ZoomTo(StrategicDistanceMeters);
    }

    /// <summary>
    /// 打开吞食目标面板 —— 命令栏「吞食」那一颗走这一口。
    /// <b>不按期别挡</b>：饱食已满 / 战斗中 / 停靠 / 无目标 / 冷却中的原因由面板顶上那一行写清。
    /// </summary>
    private void OpenForage()
    {
        if (_world is null) return;
        _foragePanel.Open();
    }

    /// <summary>
    /// 打开经营弹层 —— 命令栏「经营」那一颗走这一口（市场 / 账本与工资 / 人员与帮工）。
    /// <b>不按期别挡</b>：市场与账本两页在页内按停靠期置灰，人员与帮工恒可用。
    /// </summary>
    private void OpenOperations()
    {
        if (_world is null) return;
        _operations.Open();
    }

    /// <summary>
    /// 打开终局结算 —— 命令栏「结算」回看那一口。
    /// <b>只在结算生成之后开</b>（命令栏那一颗先按 <c>Settlement</c> 置灰）。
    /// </summary>
    private void OpenSettlement()
    {
        if (_world?.Settlement is null) return;
        _settlementPanel.Open();
    }

    /// <summary>
    /// 打开传送弹层 —— 右键传送阵那一格走这一口。
    /// <b>不在停靠期不开</b>（传送那一发也只在停靠期发得出去）；弹层自己置模态、收起时清。
    /// </summary>
    private void OpenTeleport()
    {
        if (_world is null || !_world.IsDocked) return;
        _teleportDialog.Open();
    }

    /// <summary>
    /// 拔起 / 落位走成了 → 进放置模式（落位幽灵跟鼠标，格色按建造模块的裁决涂）。
    /// </summary>
    private void OnPlacementRequested(string buildingId) => _placementGhost.Enter(buildingId);

    /// <summary>
    /// 钉住一个人 —— 焦点跟人、距离进战术档。
    /// 钉住的是活物（镇民、游客、敌人、怪物都行）；钉不住就不动（不换档）。
    /// </summary>
    public void PinUnit(string unitId)
    {
        if (!TryResolveLivingNode(unitId, out var node)) return;

        _pinnedUnitId = unitId;
        _rig.SetFov(TacticalFovDegrees);

        // 高度先改再跟（同上）。焦点高度一换，焦点平面从山腰抬到镇面，
        // 相机是阻尼着升上去的，不跳变。
        _rig.FocusHeightMeters = TacticalFocusHeightMeters;
        _rig.FollowTarget(node);
        _rig.ZoomTo(TacticalDistanceMeters);
    }

    /// <summary>解除钉住 —— 回无锁定 + 战略档。</summary>
    public void UnpinUnit()
    {
        _pinnedUnitId = string.Empty;
        _rig.SetFov(StrategicFovDegrees);
        _rig.FollowTarget(null);
        _rig.FocusHeightMeters = StrategicFocusHeightMeters;

        // 焦点平面回战略档那一层：<c>PanBy(0,0)</c> 把焦点按当下的 FocusHeightMeters 重钳一遍
        // （钳位在 <c>TopDownRig.ClampFocus</c> 里），焦点坐标本身不动，所以是阻尼着落回去的。
        _rig.PanBy(0f, 0f);
        _rig.ZoomTo(StrategicDistanceMeters);
    }

    /// <summary>
    /// 选中面板那一颗「钉住 / 解除钉住」按钮走这一口：钉的是当前选中的人，
    /// 再点同一个人的按钮即解除。
    /// </summary>
    public void TogglePinUnit(string unitId)
    {
        if (_pinnedUnitId == unitId)
        {
            UnpinUnit();
            return;
        }

        PinUnit(unitId);
    }

    /// <summary>
    /// 钉住的人没了就自动回战略档（死了 / 离镇了 / 场景节点被收走了）。
    /// 每帧重新解析一次那个人 —— 节点是表现层的，回收它的是 <c>DeckView.SyncUnits</c>。
    /// </summary>
    private void CheckPinnedTarget()
    {
        if (_pinnedUnitId.Length == 0) return;
        if (TryResolveLivingNode(_pinnedUnitId, out _)) return;
        UnpinUnit();
    }

    /// <summary>
    /// 那个人还在不在：在册、没死、没离镇、场景节点也还在。
    /// 四条缺一条就当没了 —— 钉住的是「画面上那个活物」，不是「名单上那个 id」。
    /// </summary>
    private bool TryResolveLivingNode(string unitId, out Node3D node)
    {
        node = null!;
        if (_world is null) return false;

        var unit = _world.Roster.Find(unitId);
        if (unit is null) return false;
        if (unit.State == UnitState.Dead || unit.LeftTown) return false;

        return _deck.TryNodeOf(unitId, out node);
    }

    /// <summary>视角读数直达。</summary>
    public string CameraReadout() => _rig.DebugReadout();

    /// <summary>
    /// 巨兽动画读数：正在想要哪条动作、实际在播哪条、那条 clip 在不在模型里。
    /// 「巨兽在画面上走」是两件事 —— 位置在动（快照看得见）与走路动作真在播（只有这里看得见）。
    /// 两者对不上时画面上分不出来：静站姿完全可能就是某一帧恰好对上。
    /// </summary>
    public string BeastAnimDebug() => _deck.BeastAnimReadout();

    /// <summary>九类演出读数（调试桥 show 与 uitest 的 eval 都读它）。</summary>
    public string ShowReadout() => _deck.ShowReadout();

    /// <summary>
    /// 地形表现读数：地形节点在不在、圆盘几个面、三张贴图在不在。
    /// 出图脚本用它钉住「地形不是还停在纯色平面」这一格；法线与颜色细节仍由出图复核。
    /// </summary>
    public string TerrainReadout()
    {
        var terrain = _deck.GetNodeOrNull<MeshInstance3D>("Terrain");
        if (terrain is null) return "terrain=missing";
        if (terrain.MaterialOverride is not StandardMaterial3D material) return "terrain=no-material";
        return $"terrain surface={terrain.Mesh.GetSurfaceCount()} "
               + $"albedo={(material.AlbedoTexture is not null ? "yes" : "no")} "
               + $"normal={(material.NormalEnabled ? "yes" : "no")} "
               + $"rough={(material.RoughnessTexture is not null ? "yes" : "no")}";
    }

    /// <summary>
    /// 位姿 / 悬浮层读数（转发 <c>DeckView.LevitationReadout</c>）——「小镇脚下是不是平的」「巨兽在大地图哪一格」那两格。
    /// </summary>
    public string LevitationDebug() => _deck.LevitationReadout();

    /// <summary>机位定点直达（不过阻尼）—— 截图要的是「停在某个机位」。</summary>
    public string CameraSetPose(float focusX, float focusZ, float distance, float yawDegrees, float pitchDegrees)
    {
        _rig.SetPose(new Vector3(focusX, 0f, focusZ), distance, yawDegrees, pitchDegrees);
        return _rig.DebugReadout();
    }

    /// <summary>
    /// 把当前视口存成 PNG。**出图复核是这一格唯一的证据手段**（「落地配出图」），
    /// 而「读数说它在那儿」与「画面上看得见」是两回事 —— HUD 排版有没有重叠、
    /// 建筑有没有穿模、悬空那一档抬没抬起来，都只有图能判，所以这一发进调试桥而不是每次手工截屏。
    ///
    /// 落盘路径由调用方给绝对路径。实现上收公共层（<c>ViewportCapture</c>）。
    /// </summary>
    public string CaptureScreenshot(string path) => ViewportCapture.SavePng(this, path);

    // === 玩家输入：走 InputMap action（「输入走 InputMap」） ===

    private void SetupInputMap()
    {
        EnsureAction(InputConcept.TogglePause.Name, Key.Space);
        EnsureAction(InputConcept.StepOnce.Name, Key.Period);
        EnsureAction(InputConcept.SpeedUp.Name, Key.Bracketright);
        EnsureAction(InputConcept.SpeedDown.Name, Key.Bracketleft);
    }

    /// <summary>幂等登记一条 InputMap action（「输入走 InputMap」：玩家输入走 action，不做裸键轮询）。</summary>
    private static void EnsureAction(string action, Key keycode)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventKey { PhysicalKeycode = keycode };
        if (InputMap.ActionHasEvent(action, ev)) return;
        InputMap.ActionAddEvent(action, ev);
    }

    /// <summary>
    /// 时间旋钮四条 —— 每帧读一次「刚按下」。
    ///
    /// <para><b>2026-10-12 从逐事件读改成逐帧读</b>：原来在 <c>_UnhandledInput</c> 里
    /// 用 <c>@event.IsActionPressed(...)</c> 读，那条路读的是「这一个事件算不算按下」，
    /// 于是同一帧来几个按下形状的事件就触发几次。改成逐帧读引擎的刚按下读数口之后，
    /// 引擎自己把边沿闩住 —— 一次物理按下只成立一次。</para>
    /// </summary>
    private void PollTimeKnobs()
    {
        if (_world is null) return;

        if (_read.Read(InputConcept.TogglePause))
        {
            if (_world.IsPaused) _world.Resume();
            else _world.Pause();
        }

        if (_read.Read(InputConcept.StepOnce))
        {
            // 调桥上那个方法，不在这里再抄一份 —— 抄一份就是「同一件事两处代码」。
            StepDebug();
        }

        if (_read.Read(InputConcept.SpeedUp)) SetSpeed(_speed.Multiplier * 2.0);
        if (_read.Read(InputConcept.SpeedDown)) SetSpeed(_speed.Multiplier / 2.0);
    }

    // === 时间旋钮（调试桥直达口：与上面那四条是同一件事，键位是玩家的、桥是 agent 的） ===

    /// <summary>速度旋钮直达 — <c>SetSpeed(4)</c> 快进 / <c>SetSpeed(0.25)</c> 慢放 / <c>SetSpeed(1)</c> 实时。</summary>
    public string SetSpeed(double multiplier)
    {
        _speed.SetMultiplier(multiplier);
        return $"speed={_speed.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)}x";
    }

    /// <summary>单步直达 — 走一步自动停，返回当前步号。
    ///
    /// <para><b>玩家按键与调试桥走的是这一个方法</b>（<c>PollTimeKnobs</c> 的单步那一档调它），
    /// 不各写一份：两条触发、同一件事两处代码之后，其中一份必然先漂。</para>
    /// </summary>
    public string StepDebug()
    {
        if (_world is null) return "no-run";
        _world.Advance(1);
        SyncDeck();
        return $"tick={_world.Tick.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 推进到目标步号（调试桥直达口）—— <b>与离线重放走同一条推进路</b>。
    ///
    /// <para><b>为什么是这一口</b>：录基线要「命令发在第 N 步」这件事逐字可复现，
    /// 而渲染帧那圈推进（<c>_Process</c>）的步数由帧率与倍率决定，落不到指定的步号上。
    /// 这一口走 <see cref="GameLogicBase.AdvanceTo"/>（分块推进到目标步），
    /// 与重放端 <c>ArenaSetup.Hooks</c> 的 <c>advanceTo</c> 是同一个方法 ——
    /// 录制端与重放端于是走同一条路，差一步就分叉。</para>
    ///
    /// <para>推进只动逻辑，不入 Journal（它是时间旋钮，不是写路径）。</para>
    /// </summary>
    public string AdvanceToDebug(long targetTick)
    {
        if (_world is null) return "no-run";
        _world.AdvanceTo(targetTick);
        SyncDeck();
        return $"tick={_world.Tick.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 把这一局的录制导出成重放脚本 json（调试桥直达口）—— <b>连检查点快照一起落盘</b>。
    ///
    /// <para>写口径走公共层那一份 <see cref="ReplayScriptJson.Write"/>（离线重放 CLI 读的就是它），
    /// 不在原型侧另写一份序列化。</para>
    /// </summary>
    public string ExportReplayScript(string path)
    {
        if (_recording is null) return "no-run";
        if (string.IsNullOrEmpty(path)) return "no-path";

        var file = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var script = _recording.ToScript();
        File.WriteAllText(file, ReplayScriptJson.Write(script));
        return $"export_script path={file} entries={script.Entries.Count.ToString(CultureInfo.InvariantCulture)} " +
               $"checkpoints={script.Checkpoints.Count.ToString(CultureInfo.InvariantCulture)}";
    }

    // === 画面同步（唯一入口：四个调用点都走这一口） ===

    /// <summary>
    /// 界面同步的那一个入口 —— 把世界当前的样子摆到画面上。
    ///
    /// <para><b>形态也在这一格里跟</b>：<c>_deck.SetBeastForm(world.Form)</c> 只切显隐与动画器指向，
    /// 不重建世界、不走 <c>StartWorld</c>（重建一次就得重挂磁悬浮底座与整座小镇）。
    /// 那一格自己判「变了没有」，所以每个逻辑步调一次也不白费。</para>
    ///
    /// <para>四个调用点（推进循环 / 单步 / 开局 / 注入）都走这一口：散着写的时候，
    /// 迟早有一处忘了跟形态 —— 那一处的画面上是上一形态的巨兽，而读数里看不出不对。</para>
    ///
    /// <para><b>放在 <c>StepDebug</c> 之后</b>：公共层那条源码文本判据
    /// （<c>InputLayerTests.巨兽的单步_玩家按键调的是桥上那个方法_不自己再抄一份</c>）
    /// 取的是「<c>private void PollTimeKnobs()</c> 到 <c>public string StepDebug</c> 之间那一段」，
    /// 这一件落在那一段里会被它圈进去（它要断的是 <c>PollTimeKnobs</c> 的方法体，
    /// 取法是按两个标记截的，标记之间多一个方法就多截一段）。</para>
    /// </summary>
    private void SyncDeck()
    {
        if (_world is null) return;
        _deck.Sync(_world, AnimGameSpeed);
        _deck.SetBeastForm(_world.Form);
    }

    // === 一局的装配 ===

    /// <summary>开一局并立即推进（调试桥与「重开一局」走这一口）。</summary>
    public string StartNewRun(int seed)
    {
        StartWorld(BuildConfig(seed));
        _front.CloseAll();

        // 调试桥的新局与「重开一局」都不经过标题屏，于是这里补发开局那一发 ——
        // 少了它，这一局开到天荒地老都停在「未开始」，重放账里也缺开局那一行。
        Inject("StartRun", string.Empty);
        BeginAdvance();
        return _world?.DebugSnapshot() ?? "start-failed";
    }

    /// <summary>
    /// 按启动档位配一局的开局。**开局数只在 <see cref="ArenaConfig.Default"/> 里写一份** ——
    /// 离线重放 CLI 也走那一口，两处各写一遍就会出现「引擎那局」与「离线那局」开局不同。
    ///
    /// <para>本期两个档位落到的配置完全一样：这一格只是分叉点，切片自己的开局数值与流程目标
    /// 由后续任务接进来。重开一局也走这一口，档位因此跟着场景走，不会中途换回灰盒口径。</para>
    /// </summary>
    private ArenaConfig BuildConfig(int seed = DefaultSeed) => Profile switch
    {
        RunProfile.Mvp => ArenaConfig.Default(seed),
        RunProfile.VerticalSlice => ArenaConfig.Default(seed),
        _ => throw new ArgumentOutOfRangeException(nameof(Profile), Profile, "未知启动档位"),
    };

    /// <summary>
    /// 一份 UI 的复位清单：所有弹层 / 模态 / 选中 / 建造幽灵 / 拖拽 / 横幅。
    /// <b>换局只有一个入口调它</b>（<see cref="StartWorld"/>）—— 散着写的时候总有一两件漏掉，
    /// 下一局就会带着上一局的招募框或拖拽状态。
    /// </summary>
    private void ResetUi()
    {
        _help.SetOpen(false);
        _selection.Clear();
        _buildGhost.Exit();
        _placementGhost.Exit();
        _blueprintBar.ClearSelection();
        _buildingMenu.Close();
        _recruitDialog.Close();
        _battleReport.Close();
        _operations.Close();
        _foragePanel.Close();
        _settlementPanel.Close();
        _beastPanel.Close();
        _teleportDialog.Close();
        _commandBar.ClosePopup();
        _drag.Reset();
        _input.ResetPressed();
        _banner.Clear();
    }

    /// <summary>
    /// 只建世界，不推进、不开屏、不报「局已开始」。
    ///
    /// <para>开局的第一屏是标题屏，所以这一口与 <see cref="BeginAdvance"/> 分开：
    /// <c>_Ready</c> 调它之后显式 <c>_front.Open(Title)</c>，「开始游戏」那一下才走另一半。
    /// 合成一条的话，标题屏还没上屏，时间轴已经在跑了。</para>
    /// </summary>
    private void StartWorld(ArenaConfig config)
    {
        // 换局即弃旧账：录制器攥着上一局那个世界，所以先交还它，再交还世界本身。
        // 次序反了就是拿新世界去 Dispose（Dispose 幂等，结果是旧世界的订阅永远挂着）。
        _recording?.Dispose();
        _world?.Dispose();

        _world = new ArenaWorld(config);
        _world.TierAdvanced += OnTierAdvanced;
        _world.FormSwitched += OnFormSwitched;
        _world.FormUnlockedEvent += OnFormUnlocked;
        _world.EatCompleted += OnEatCompleted;
        _world.CrystalsProduced += OnCrystalsProduced;
        _world.ForageGaveUp += OnForageGaveUp;
        _world.CrystalUsed += OnCrystalUsed;
        _world.CrystalSold += OnCrystalSold;

        // 现实耗时换一局从零记：它住表现层，逻辑世界读不到它（spec「终局结算」）。
        _telemetry.Reset(_world.Flow);
        _settlementTelemetry = null;
        _settlementAutoOpened = false;

        // 终局结算那一刻把现实耗时冻一份：之后继续经营怎么走都不再改写。
        _world.Flow.SettlementOpened += OnSettlementOpened;

        // 防守收场横幅只订 E-17（冲突 C-6：攻破横幅改订事件，轮询闩已删；拔锚横幅改在命令成功那一刻弹）。
        _world.Battle.DefenceSettled += OnDefenceSettled;

        _recording = ArenaSetup.Recorder(_world);
        _recording.BeginSession();
        _tickAccumulator = 0;
        _started = false;
        _runEnded = false;
        _lastReportTick = -1;

        // 换局即换一段性能账：上一局的帧不该压在下一局的验收窗里。
        // 建世界那一帧与紧随的一帧算加载，跳掉（spec「加载与场景切换单列」）。
        _perf.Reset();
        _perfSkipFrames = 2;
        _vfxPeakEmitters = 0;
        _vfxPeakParticles = 0;

        // 全部界面状态只在这一处复位（弹层 / 模态 / 选中 / 幽灵 / 拖拽 / 横幅）。
        ResetUi();

        SyncDeck();
        LockBeast();
        Log($"[BeastArena] 开局 seed={config.Seed}");
    }

    /// <summary>开始推进：标题屏「开始游戏」与调试桥的新局都落到这一口。</summary>
    private void BeginAdvance()
    {
        _started = true;
        _front.NotifyRunStarted();
    }

    /// <summary>
    /// 标题屏「开始游戏」那一条链：关屏 → 报局开始（流程从「未开始」推到开局航程）→ 开始推进。
    /// 三件事的次序不能换 —— 先关屏再报，前端层才不会把「局已开始」记在还开着菜单的那一帧上；
    /// 而「开始游戏」那一发也走词表（<c>StartRun</c>），否则重放账里少了开局那一发，
    /// 重放端的世界从头到尾停在「未开始」。
    /// </summary>
    private void StartGame()
    {
        _front.CloseAll();
        Inject("StartRun", string.Empty);
        BeginAdvance();
    }

    /// <summary>uitest 用：收起全部弹层（只动界面状态，不动世界）。</summary>
    public string ClosePanels()
    {
        ResetUi();
        return "panels-closed";
    }

    /// <summary>uitest 用：打开暂停屏（与 Esc 走同一个 <c>Open</c>），返回菜单栈读数。</summary>
    public string OpenPauseMenu()
    {
        _front.Open(FrontEndScreen.Pause);
        return _front.DebugReadout();
    }

    /// <summary>前端层读数（菜单栈、设置、鼠标模式）—— 标题与暂停屏的验收读它。</summary>
    public string FrontReadout() => _front.DebugReadout();

    /// <summary>重开一局（沿用默认种子）—— 暂停屏递它。</summary>
    public string RestartRun() => StartNewRun(DefaultSeed);

    /// <summary>
    /// 回标题 —— 暂停屏递它：收掉菜单、重建一局、停在标题屏。
    ///
    /// <para><b>必须重建世界</b>：标题屏「开始游戏」走 <see cref="StartGame"/>，
    /// 那一发对还开着的世界再 Inject 一次 StartRun 会撞「已在局中」。
    /// <see cref="StartWorld"/> 自己复位 <c>_started</c> 与 <c>_runEnded</c>，
    /// 回到标题屏上时间不推进（推进循环的 <c>_started</c> 闸），和冷启动同一形状。</para>
    /// </summary>
    private void ReturnToTitle()
    {
        _front.CloseAll();
        StartWorld(BuildConfig());
        _front.Open(FrontEndScreen.Title);
    }

    /// <summary>
    /// 收尾这一局 —— 冻结时间轴 + 弹结算屏（调试桥直达口：<c>call ReportRunEnded "标题"</c>）。
    ///
    /// <para><b>为什么由外面调而不是世界自己抛事件</b>：新版逻辑世界是连续循环，
    /// 攻破后停摆三个游戏日再恢复，没有终局可等；而表现层每帧去问一次「打完了没有」
    /// 正是「表现层只读」要防的那一类倒挂。所以收尾这一发走外部触发，
    /// 与另两个原型的 <c>BattleDecided</c> 事件落到的是同一条 <see cref="OnRunEnded"/>。</para>
    ///
    /// <para>幂等：同一局已经报过就不再报一次（第二次只是把结算屏上的字改回来，
    /// 冻结重复调没有副作用）。</para>
    /// </summary>
    public string ReportRunEnded(string title)
    {
        if (_world is null) return "no-run";
        if (_runEnded) return $"already-ended tick={_world.Tick.ToString(CultureInfo.InvariantCulture)}";

        OnRunEnded(title);
        return $"ended tick={_world.Tick.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>收尾那一件事：报给前端层（它随后抛局结束事件，冻结与收尾按钮都在那一头）。</summary>
    private void OnRunEnded(string title)
    {
        if (_world is null) return;

        _front.NotifyRunEnded(new RunOutcome(
            title,
            [
                Loc.Fmt("第 {0} 步", _world.Tick.ToString(CultureInfo.InvariantCulture)),
                Loc.Fmt("本局 {0}", _world.CompassText()),
            ],
            _world.Tick,
            !_world.TownHallBreached,
            [("重开一局", () => RestartRun())]));
    }

    /// <summary>
    /// 离树交还 —— 录制器与世界都成对交还（<c>Dispose</c> 幂等，重复调不重复交）。
    /// 与另两个原型同一条次序：先录制器（它攥着世界），后世界。
    /// </summary>
    public override void _ExitTree()
    {
        _recording?.Dispose();
        _recording = null;
        _world?.Dispose();
    }

    // === 三个观测口的运行时通道（「写路径进 Journal」：写操作走 inject 进 Journal） ===

    /// <summary>eval 脚本的 globals（Roslyn 脚本编译要可访问性）。</summary>
    public sealed class BeastArenaGlobals
    {
        public Main Main { get; set; } = null!;
    }

    private DebugBridgeHost? _bridge;

    private void SetupDebugBridge()
    {
        // 数字参数缺了当场抛，不兜 0（「桥只转发」）。动词只转发世界里已有的方法。
        _bridge = DebugConsole.Attach(this, new BeastArenaGlobals { Main = this },
            ("speed", a => SetSpeed(a.Num("multiplier", required: true))),
            ("step", _ => StepDebug()),
            ("snapshot_debug", _ => DebugSnapshot()),
            ("readout", _ => WorldReadout()),
            ("new_run", a => StartNewRun((int)a.Num("seed", required: true))),
            ("inject", a => Inject(
                a.Str("cmd", required: true) ?? "",
                a.Str("args") ?? "")),
            ("verbs_mvp", _ => string.Join(" ", ArenaSetup.VerbNames)),
            ("journal", _ => JournalDebug()),
            ("screenshot", a => CaptureScreenshot(a.Str("path", required: true) ?? "")),
            ("ui_rects", _ => _ui.DescribeControls()),
            ("ui_viewport", a => _uiTest.SetViewportSize(
                (int)a.Num("width", required: true),
                (int)a.Num("height", required: true))),
            ("ui_click", a => _uiTest.ClickControl(a.Str("name", required: true) ?? "")),
            ("ui_set", a => _uiTest.SetControlValue(
                a.Str("name", required: true) ?? "",
                a.Str("value", required: true) ?? "")),
            ("beast_anim", _ => BeastAnimDebug()),
            ("show", _ => _deck.ShowReadout()),
            ("perf", _ => PerfReadout()),
            ("perf_reset", _ => ResetPerf()),
            ("vfx_stress_x2", _ => StressDoubleVfxPeak()),
            ("vfx_stress_clear", _ => ClearVfxStress()),
            ("levitation", _ => LevitationDebug()),
            ("camera", _ => CameraReadout()),
            ("camera_set", a => CameraSetPose(
                (float)a.Num("focusX", required: true),
                (float)a.Num("focusZ", required: true),
                (float)a.Num("distance", required: true),
                (float)a.Num("yaw", required: true),
                (float)a.Num("pitch", required: true))),
            ("checkpoint", a => CaptureCheckpoint(a.Str("label", required: true) ?? "")),
            ("advance_to", a => AdvanceToDebug((long)a.Num("tick", required: true))),
            ("export_script", a => ExportReplayScript(a.Str("path", required: true) ?? "")));

        // 输入合成那四条（「行为可注入」那一半）：input_action / input_click / input_hover / input_read。
        // Attach 之后单独注册：它的词表参数是 `params`，`..` 展开只在集合初始化里合法。
        foreach (var (verb, handler) in InputInjector.Vocabulary(this, RecordSyntheticInput))
            _bridge?.Register(verb, handler);

        // i18n 两动词：set_locale 切语言、locale_probe 读当前语言/字体链/某键的取字结果。
        foreach (var (verb, handler) in DemoFactory.GugaFramework.I18n.LocaleVerbs.Vocabulary())
            _bridge?.Register(verb, handler);

        Log($"[BeastArena] 调试桥已接线（{(_bridge is null ? "进程没建起来" : "词表看 debug verbs")}）");
    }

    private ReplayAssembly? _recording;

    /// <summary>
    /// 注入口 — 走词表、规范化后入 Journal，重放端与玩家走同一条路径（「桥只转发」、「写路径进 Journal」）。
    ///
    /// 返回值：成功是逐字的 <c>ok</c>（Journal 与「断言三层」 判 <c>ok</c> 翻转都靠它，不能动），
    /// 失败是 <c>rejected: &lt;原因&gt;</c>。
    /// </summary>
    public string Inject(string cmd, string args)
    {
        if (_recording is null) return "no-run";
        if (!_recording.Inject(cmd, args, out var reason))
        {
            if (_world is not null) SyncDeck();
            _banner.ShowReject(reason);

            // 前缀只拼一层：rejected: 那一层由 ArenaSetup.Bad 加（动词那一侧写进原因串），
            // 这里原样回传 —— 再拼一层就成了两遍前缀。
            _lastInjectReply = string.IsNullOrEmpty(reason) ? "rejected" : reason;
            return _lastInjectReply;
        }

        if (_world is not null) SyncDeck();
        _lastInjectReply = "ok";

        // 拔锚横幅走命令路径（冲突 C-6：原每帧轮询 Battle.IsDefence 的翻转闩已删）。
        if (cmd == "PullAnchor") AnnounceAnchorPulled();

        return "ok";
    }

    /// <summary>
    /// 合成输入的入录回调 —— 与本原型其余写路径同一条口。
    ///
    /// <para>走 <see cref="Inject"/> 而不是自己摸 <c>_recording</c>：它已经处理了
    /// 「没在录就返回 <c>no-run</c>」那一档，绕过它自己摸 _recording 会把那一档漏掉
    /// —— 而漏录一次就让重放分叉一次（「写路径全录」）。</para>
    /// </summary>
    private void RecordSyntheticInput(InputInjector.InputKind kind, string args)
    {
        Inject(kind == InputInjector.InputKind.Action
            ? SyntheticVerbs.Action
            : SyntheticVerbs.Click, args);
    }

    /// <summary>到点抓一次检查点快照（重放比对的单位是它）。</summary>
    public string CaptureCheckpoint(string label)
    {
        if (_recording is null) return "no-run";
        _recording.Mark(label);
        return $"checkpoint seq={_recording.Checkpoints.Count} tick={_world?.Tick.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Journal 读数 —— 一行一条，<c>{步号, seq, 命令, 规范化参数, ok}</c>。
    ///
    /// 「写路径全录」要求写路径整条进账，而**录进去还得取得出来**才叫得上一条可核对的账。
    /// 未知命令不入录，所以这里少掉的行就是那些压根没走成的动词。
    /// </summary>
    public string JournalDebug()
    {
        if (_recording is null) return "no-run";
        var lines = _recording.Journal
            .Select(e => $"tick={e.Tick.ToString(CultureInfo.InvariantCulture)} seq={e.Seq.ToString(CultureInfo.InvariantCulture)} " +
                         $"{e.Cmd}|{e.Args} ok={e.Ok.ToString().ToLowerInvariant()}");
        return $"journal={_recording.Journal.Count.ToString(CultureInfo.InvariantCulture)} " +
               $"recording={_recording.IsRecording.ToString().ToLowerInvariant()} " +
               $"checkpoints={_recording.Checkpoints.Count.ToString(CultureInfo.InvariantCulture)}\n" +
               (lines.Any() ? string.Join("\n", lines) : "（空）");
    }

    // === 读数 ===

    /// <summary>全量状态读数 —— 转发逻辑世界那份实现，显示即真相。</summary>
    public string DebugSnapshot() => _world?.DebugSnapshot() ?? "no-run";

    /// <summary>一句话读数（不进快照）：罗盘时钟、游客、电梯、搬运、交战、法力、负债。</summary>
    public string WorldReadout() => _world is null
        ? "no-run"
        : ArenaSnapshot.Readout(_world) + " | BANNER " + _banner.DebugReadout()
            + $" last_inject={_lastInjectReply}";

    /// <summary>
    /// 性能读数（不进快照）：采样账 + 引擎节点 / 资源 / 静态内存 + 人形单位数 + 粒子池账。
    /// 采样区间从上一次 <c>perf_reset</c>（或换局）算起；<c>units</c> 不含巨兽本体、不含已死与离镇。
    /// </summary>
    public string PerfReadout()
    {
        if (_world is null) return "no-run";

        var units = 0;
        var enemies = 0;
        foreach (var unit in _world.Units)
        {
            if (unit.State == UnitState.Dead || unit.LeftTown || unit.Role == UnitRole.Monster) continue;
            units++;
            if (unit.Role == UnitRole.Enemy) enemies++;
        }

        var nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        var memMb = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024.0 * 1024.0);
        return _perf.Readout()
            + $" nodes={nodes.ToString(CultureInfo.InvariantCulture)}"
            + $" resources={resources.ToString(CultureInfo.InvariantCulture)}"
            + $" mem_mb={memMb.ToString("0.0", CultureInfo.InvariantCulture)}"
            + $" units={units.ToString(CultureInfo.InvariantCulture)}"
            + $" enemies={enemies.ToString(CultureInfo.InvariantCulture)}"
            + $" vfx_peak_emitters={_vfxPeakEmitters.ToString(CultureInfo.InvariantCulture)}"
            + $" vfx_peak_particles={_vfxPeakParticles.ToString(CultureInfo.InvariantCulture)}"
            + $" | {_deck.VfxReadout()}";
    }

    /// <summary>清空性能采样窗（验收脚本在预热之后调它）。</summary>
    public string ResetPerf()
    {
        _perf.Reset();
        _perfSkipFrames = 0;
        _vfxPeakEmitters = 0;
        _vfxPeakParticles = 0;
        return $"perf_reset tick={_world?.Tick.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 按整局见过的粒子峰值翻倍挂一个持续发射器（验收的「两倍峰值压力」）。
    ///
    /// <para><b>取整局峰值，不取当前采样窗</b>：压力窗开跑前会先 <c>perf_reset</c>，
    /// 窗口局部峰值只剩压力场自己那一段，翻出来不到终局峰值两倍。整局峰值不过
    /// <c>perf_reset</c>，压力档才是「最终峰值的两倍」。</para>
    /// </summary>
    public string StressDoubleVfxPeak()
    {
        var particles = Math.Max(16L, _vfxLifetimePeakParticles * 2);
        var made = _deck.StressVfxParticles(particles);
        if (_deck.VfxEmitters > _vfxPeakEmitters) _vfxPeakEmitters = _deck.VfxEmitters;
        if (_deck.VfxParticles > _vfxPeakParticles) _vfxPeakParticles = _deck.VfxParticles;
        if (_deck.VfxParticles > _vfxLifetimePeakParticles) _vfxLifetimePeakParticles = _deck.VfxParticles;
        return made;
    }

    /// <summary>撤掉压力测试的发射器。</summary>
    public string ClearVfxStress() => _deck.ClearVfxStress();

    /// <summary>日志双写：GD.Print 给人看，RollingLog 给 <c>debug logs</c> 读（读它必看 dropped_count）。</summary>
    protected static void Log(string message)
    {
        GD.Print(message);
        RollingLog.Write(message);
    }
}
