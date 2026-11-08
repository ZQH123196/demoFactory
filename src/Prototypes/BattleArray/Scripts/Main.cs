#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.FrontEnd;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.InputLayer;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Prototypes.BattleArray.AI;
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;
// 逻辑层用 System.Numerics 与自己的 Side，表现层同时吃 Godot 的 Vector2 / Side（对齐模式枚举）——
// 同文件里两边都出现必须起别名，别让编译器替人猜是哪个（CS0104，BattleUnitViews 同一口径）。
using BattleSide = DemoFactory.Prototypes.BattleArray.Logic.Side;

namespace DemoFactory.Prototypes.BattleArray;

/// <summary>
/// 原型主场景入口 —— M6 起这一局是<b>玩家摆出来的</b>：布阵期（网格 + 拖放 + 兵源面板）→ 开局 → 随时暂停 → 结算。
///
/// 设计思路：
/// - 每个原型拥有独立的 Main.tscn，可直接 F5 运行验证
/// - 场景节点只是被逻辑流牵引的只读表现节点：摆子的写入口只有一个（<see cref="BattleWorld.PlaceUnit"/>，
///   词表、直达口与鼠标三条路都走它），本文件负责把「鼠标落在哪一格」「按钮按下」翻译成对逻辑层的调用，
///   自己不存阵面
/// - 逻辑真值在 <see cref="DeploymentBoard"/>（哪格站了谁、钱还剩多少）与 <see cref="BattleWorld"/>
///   （打到第几步、谁为什么不动），UI 读的都是这两份
///
/// 一局走到哪一档由 <see cref="BattleWorld.Phase"/> 说：布阵期的时间轴是冻着的（只有
/// <see cref="StartBattleDebug"/> 能起跑），暂停钮在布阵期不接 —— 起跑只有一条路。
///
/// 两种进局方式（这条要记牢，它改了 M5 的复现命令）：
/// - <b>默认进布阵期</b>：F5 一开就是 15 格阵面等着摆，玩家按 Enter / 点「开局」才打；
/// - <b>直接开打</b>：环境变量 <c>BATTLE_ARRAY_AUTO_START=1</c> 或命令行用户参数 <c>++auto-start</c>
///   —— M4/M5 那条「headless 跑一局、判定行与离线报告逐字同」的复现靠它，不带这个口就得先注入一次开局。
///
/// 视觉管线：
/// - 正交 3D 节点 (World) 处理空间逻辑；灯光与地面在本文件里搭（场景文件只留骨架）
/// - 兵的镜像在 <see cref="BattleUnitViews"/>，网格在 <see cref="BattleGridView"/>，HUD 在 <see cref="BattleHud"/>
/// - 视角统一走 GugaFramework/Camera/TopDownRig（俯视角 · 相机 eval 口模板自带）
/// - 点选归属玩法语义 ⇒ 相机架只报「屏幕哪被单击」，「点中了哪一格 / 哪个兵」在这里判
/// </summary>
public partial class Main : Node
{
    /// <summary>本局的固定种子（布阵期反复重建世界都吃它 ⇒ 同一份阵摆两遍必是同一局，对照才成立）。</summary>
    private const int DefaultSeed = 1001;

    private int _seed = DefaultSeed;

    /// <summary>布阵棋盘：哪一格站着谁、钱还剩多少 —— 玩家的全部阵面决策都落在这份账上。</summary>
    private DeploymentBoard _board = new();

    /// <summary>当前这一局的世界。布阵期它已经存在（兵已入场、时间轴冻着），开局只是把档位推过去。</summary>
    private BattleWorld _world = null!;

    /// <summary>兵源面板上选中的兵种（之后点空格就摆它；null = 没选，点空格只当选中那个兵）。</summary>
    private TroopKind? _pickedKind;

    /// <summary>正从兵源面板往外拖的那个兵种（松手那一刻落到哪一格就是它）。</summary>
    private TroopKind? _draggingKind;

    /// <summary>暂停面板与调试读数当前在「读」哪个兵（点兵切换，默认我方第一个）。</summary>
    private string _readoutUnitId = "p01";

    private BattleUnitViews _units = null!;
    private BattleGridView _grid = null!;

    /// <summary>技能 cue 的上屏件（只订观测面，一个真值都不写）。它跟着世界换局，见 <see cref="RebuildWorldFromBoard"/>。</summary>
    private SkillCues _cues = null!;

    private BattleHud _hud = null!;

    /// <summary>前端层（菜单栈、标题屏、结算屏、退出）。</summary>
    private FrontEndLayer _front = null!;
    private TopDownRig _rig = null!;

    /// <summary>单挑局（含演示档）才开：每刷一次 HUD 把机位重摆到「活着的兵全进框」那一档。</summary>
    private bool _autoFrameDuel;
    private Node _brainRoot = null!;
    private double _hudRefreshAccumulator;

    /// <summary>HUD 刷新档：5 秒内每 0.2 秒念一次真值（摆子与开局这类动作另有一刷，不等这一档）。</summary>
    private const double HudRefreshSeconds = 0.2;

    public override void _Ready()
    {
        // 字体先装：缺省字体没有汉字字形，不装的话 HUD 与 Label3D 的中文上屏是整行空白
        // （实测记录见 SkillStageScene 类注）。装在读任何 Label 之前。
        DemoFactory.GugaFramework.I18n.FontSetup.Install();

        SetupCameraRig();
        SetupStage();
        EnsureInputActions();

        _units = new BattleUnitViews { Name = "Units" };
        _grid = new BattleGridView { Name = "DeployGrid" };
        _cues = new SkillCues { Name = "SkillCues" };
        _brainRoot = new Node { Name = "Brains" };
        var world3D = GetNode<Node3D>("World");
        world3D.AddChild(_units);
        world3D.AddChild(_grid);
        world3D.AddChild(_cues);
        world3D.AddChild(_brainRoot);
        _grid.Build();

        _hud = new BattleHud { Name = "Hud" };
        GetNode<CanvasLayer>("UILayer").AddChild(_hud);
        _hud.KindPicked += kind => { _pickedKind = kind; Say($"选中 {KindName(kind)} —— 点一格摆它，或直接拖到格子上"); };
        _hud.DragStarted += kind => _draggingKind = kind;
        _hud.DragEnded += () => { _draggingKind = null; _grid.ClearMark(); };
        _hud.StartRequested += () => StartBattleDebug();
        _hud.PauseRequested += () => TogglePauseDebug();
        _hud.RestartRequested += () => RestartDeployDebug(_seed);

        _board.Changed += OnBoardChanged;
        _rig.ViewClicked += OnViewClicked;

        SetupDebugBridge();
        SetupFrontEnd();
        RebuildWorldFromBoard();
        if (WantsDuelRift() || WantsShowcase())
        {
            // H3 那一局单挑直接开场，并让技能层自己把技能放出去（不暂停）——
            // 出图复核走的就是这一档：「技能进了真演化」要能在画面上看见，不能只在快照里读出来。
            // 实现搬进 MainReplayBridge.cs 的 AutoStartDuelRiftDebug：环境变量档与 MCP eval 档共用那一份，
            // 不开第二条起局路（演示档比默认档多的两件事也在那一处说明）。
            AutoStartDuelRiftDebug(WantsShowcase());
        }
        else if (WantsAutoStart())
        {
            Say("按 auto-start 直接开打（不带这个口就停在布阵期等玩家摆）");
            StartBattleDebug();
        }
        else
        {
            Say($"布阵期开始：{_board.Filled} 个兵已入场，摆完按 Enter 开局");
        }
        RefreshHudNow();
    }

    /// <summary>
    /// 要不要一进来就开打 —— 环境变量或命令行用户参数（<c>++auto-start</c>）任给一个就算。
    /// 为什么留这个口：M4/M5 那两条复现（headless 判定行逐字同离线报告、同种子重跑逐字全等）
    /// 比的都是「一整局」，而 M6 之后默认停在布阵期不会自己打 —— 没有这个口，复现命令就得改成
    /// 「先起桥、注入开局、再等判定」，那是三次往返而不是一发。
    /// </summary>
    private static bool WantsAutoStart() => WantsFlag(AutoStartEnvVar, "auto-start");

    /// <summary>直接开打的环境变量名（复现命令里写它，别写魔法数字）。</summary>
    public const string AutoStartEnvVar = "BATTLE_ARRAY_AUTO_START";

    /// <summary>一进来就开 H3 那局单挑（我方剑士带一条突斩 打 敌方蛮牛）的环境变量名。</summary>
    public const string DuelRiftEnvVar = "BATTLE_ARRAY_DUEL";

    /// <summary>要不要一进来就开那一局单挑 —— 与 <see cref="WantsAutoStart"/> 同一形状（环境变量或 <c>--duel</c>）。</summary>
    private static bool WantsDuelRift() => WantsFlag(DuelRiftEnvVar, "duel");

    /// <summary>单挑局的<b>演示档</b>：血 ×3、两边各一条主动、机位拉近 —— 出图与录像吃这一档。</summary>
    public const string ShowcaseEnvVar = "BATTLE_ARRAY_SHOWCASE";

    /// <summary>要不要走演示档（环境变量或 <c>--showcase</c>）。给了它就不用再给 <c>BATTLE_ARRAY_DUEL</c>。</summary>
    private static bool WantsShowcase() => WantsFlag(ShowcaseEnvVar, "showcase");

    /// <summary>
    /// 真玩法那一局的<b>技能档</b>开关（P4「每兵种一条」）：环境变量或 <c>--abilities</c>。
    /// <b>缺省关</b> —— 关着时世界一个字节都不碰技能层，于是全关档那本旧账（首刀第 42 步、
    /// 第 162 步分胜负）与摆位/配比对照那 60 场都不必重跑：这一格是回归校验档，不是可选项。
    /// </summary>
    public const string AbilitiesEnvVar = "BATTLE_ARRAY_ABILITIES";

    /// <summary>要不要给真玩法那一局按兵种铺技能（四兵种各一条）。</summary>
    private static bool WantsAbilities() => WantsFlag(AbilitiesEnvVar, "abilities");

    /// <summary>
    /// 按开关给<b>当前这一局</b>铺技能：开着就每兵种一条（坦克盾墙 / 战士突斩 / 弓手穿刺箭 / 骑手冲锋），
    /// 关着什么都不做。单挑局与演示档不走这一发 —— 它们的装配在
    /// <see cref="DuelRiftSetup"/>（那里已经有自己的突斩 + 踏地口径，两处都装会长成两套装配）。
    /// </summary>
    private void AttachAbilitiesIfRequested()
    {
        if (!WantsAbilities()) return;
        var layer = BattleAbilities.AttachPerTroop(_world);
        layer.AutoCast = true;
        var line = $"技能档已开：四兵种各一条（{string.Join(" / ", BattleContract.Kinds.Select(BattleAbilities.AbilityOfTroopKind))}）";
        GD.Print(line);
        RollingLog.Write(line);
    }

    /// <summary>「环境变量 = 1 或 用户参数 --名字」任给一个就算 —— 三档开关共用这一发。</summary>
    private static bool WantsFlag(string envVar, string argName)
    {
        if (OS.GetEnvironment(envVar) == "1") return true;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == argName || arg == "--" + argName) return true;
        }
        return false;
    }

    // === 世界与棋盘的接合 ===

    /// <summary>棋盘一变就重开一局布阵期的世界：兵按新阵入场、时间轴冻着、树与桥一起重挂。</summary>
    private void OnBoardChanged()
    {
        RebuildWorldFromBoard();
        RefreshHudNow();
    }

    /// <summary>
    /// 按棋盘当前这份阵重建世界（<c>phase = Deploying</c>）。
    /// 换一局要把上一局的订阅与模型与树全撤干净 —— 这条路径与 <see cref="RestartBattleDebug"/> 共用，
    /// M5 记的那条「重启后树悄悄掉光也看不出来」的漏口就是靠 <see cref="BrainReadout"/> 堵的。
    /// </summary>
    private void RebuildWorldFromBoard()
    {
        if (_world is not null)
        {
            _world.BattleEnded -= OnBattleEnded;
            _world.Dispose();
        }
        _world = new BattleWorld(_seed, _board.ToFormation(), PlacementComparison.EnemyFormation,
            phase: BattlePhase.Deploying, board: _board);
        _world.BattleEnded += OnBattleEnded;
        _tickAccumulator = 0f;
        AttachAbilitiesIfRequested();
        RebuildOverlays(_world);
        _grid.Refresh(_board);
        _readoutUnitId = _world.Units.FirstOrDefault(u => u.Side == BattleSide.Player)?.Id ?? "p01";
        MarkSelectedCell();
        _front.NotifyRunStarted();
    }

    /// <summary>
    /// 换一局要把三件观测件<b>一起</b>重挂：模型 / 技能 cue / 行为树。
    ///
    /// 为什么要收成这一发而不是三处各写三行：第八轮出图抓到过漏挂 —— 单挑局那条路径只重挂了模型与树，
    /// cue 件还指着上一局那个已释放的世界，于是画面上一个 cue 都没有、HUD 那行是空的，
    /// 而 <c>dotnet test</c> 与日志全绿（表现层不进测试）。这类「静默什么都没有」的漏口只能靠一个入口堵。
    ///
    /// 三件都走 <c>Wire</c> 不走 <c>Rebuild</c>：<c>Rebuild</c> 是「沿用上一次存下的相机架」的便利口，
    /// <b>第一局</b>调用它会抛（相机架还没存过）—— 这条是本轮收口时实跑抓到的，见《验证操作手册》「故障排查」。
    /// <c>Wire</c> 自己先 <c>Unwire</c> 再重摆，所以首局与换局是同一条路。
    /// </summary>
    private void RebuildOverlays(BattleWorld world)
    {
        _units.Wire(world, _rig);
        _cues.Wire(world, _rig);
        AttachBrains(world);
    }

    /// <summary>回布阵期（结算后「再来一局」）：棋盘恢复默认满阵（钱退回额度），世界重新摆出来。</summary>
    public string RestartDeployDebug(int seed = DefaultSeed)
    {
        _seed = seed;
        _front.CloseAll();
        _board.FillFrom(Formations.ShieldWall);   // 会喊 Changed ⇒ 世界跟着重建
        var message = $"已回到布阵期：seed={seed}，默认常规线满阵（钱 {_board.Remaining}/{_board.StartingPurse}）";
        Say(message);
        RefreshHudNow();
        return message;
    }

    // === 玩家动作：摆子 / 开局 / 暂停（写逻辑层只有这三条路） ===
    //
    // 三条直达口与鼠标那两条路都走 WriteDebug 那一发（MainReplayBridge.cs）：
    // 命令经词表打到 DuelRiftSetup.Inject → BattleWorld 那个写入口 → 布阵棋盘。
    // 于是录制期间玩家摆的每一手都进 Journal（「写路径进 Journal」），
    // 参数编码走 DeploymentVerbs（「重放装配单源」），这一层只转发（「桥只转发」）。

    /// <summary>
    /// 摆子直达口 —— M6 要的那一口：<c>PlaceUnitDebug("archer", 2, 0)</c> 把一个兵放到指定格。
    /// 兵种键不认、不在布阵期这两档都由词表与那个写入口判否，理由照原样打回去（不静默兜一个）。
    /// </summary>
    public string PlaceUnitDebug(string kindKey, int row, int column)
        => DeployDebug(DeploymentVerbs.Place, DeploymentVerbs.PlaceArgs(kindKey, row, column));

    /// <summary>撤子直达口：<c>RemoveUnitDebug(0, 2)</c>（一格只站一个兵，撤掉退钱）。</summary>
    public string RemoveUnitDebug(int row, int column)
        => DeployDebug(DeploymentVerbs.Remove, DeploymentVerbs.RemoveArgs(row, column));

    /// <summary>挪位直达口：把 (0,0) 的兵挪到 (2,4)（钱不动 —— 这是「换个位置」，不是「再买一个」）。</summary>
    public string MoveUnitDebug(int fromRow, int fromColumn, int toRow, int toColumn)
        => DeployDebug(DeploymentVerbs.Move, DeploymentVerbs.MoveArgs(fromRow, fromColumn, toRow, toColumn));

    /// <summary>
    /// 摆子口那三条桥动词共用这一发：走同一个 journaled 入口，再念一行棋盘读数。
    /// 棋盘那一行是「这一手落成了什么样」的即时答案（钱与格数），与 HUD 读的是同一份账。
    /// </summary>
    private string DeployDebug(string cmd, string args)
    {
        var text = WriteDebug(cmd, args);
        var message = text + "\n" + _board.DebugSnapshot().Split('\n')[0];
        Say(message);
        return message;
    }

    /// <summary>棋盘读数直达口：<c>BoardReadoutDebug()</c> 一发读出 15 格现在站着谁、钱还剩多少。</summary>
    public string BoardReadoutDebug() => _board.DebugSnapshot();

    /// <summary>开局直达口：布阵期 → 战斗。已开打或已判定时它什么都不做，只把当前状态回报回去。</summary>
    public string StartBattleDebug()
    {
        if (_world.Phase != BattlePhase.Deploying) return $"不用开局：当前是 {_world.Phase}";
        var message = _world.BeginBattle();
        Say($"{message}（我方 {_world.Fielded(BattleSide.Player)} 对 敌方 {_world.Fielded(BattleSide.Enemy)}）");
        RefreshHudNow();
        return message;
    }

    /// <summary>
    /// 暂停 / 继续，并把「谁为什么不动」那一句一起报回去（M6：暂停时读得出这一句）。
    /// 这一口不自己造句：那句 why 来自 <see cref="BattleWorld.ReadoutFor"/>，
    /// 而它读的是 <see cref="BattleBrainPolicy"/> —— 与结算、与行为流同一份实现。
    /// </summary>
    public string TogglePauseDebug()
    {
        if (_world.Phase == BattlePhase.Deploying) return "布阵期还没开打，暂停钮不接（要起跑用 StartBattleDebug）";
        if (_world.Phase == BattlePhase.Settled)
        {
            var at = (_world.OutcomeTick ?? _world.Tick) / 10.0;
            return $"已判定（第 {at:F1} 秒），没什么可暂停 —— 要重摆喊 RestartDeployDebug";
        }
        var toPaused = !_world.IsPaused;
        _world.SetPaused(toPaused);
        var readout = _world.ReadoutFor(_readoutUnitId);
        var message = (toPaused ? $"已暂停（第 {_world.Tick / 10.0:F1} 秒）· " : "已继续 · ") + readout;
        Say(message);
        RefreshHudNow();
        return message;
    }

    /// <summary>单兵读数直达口：<c>WhyReadoutDebug("p07")</c> 读出「这人为什么这么站着」。</summary>
    public string WhyReadoutDebug(string unitId)
    {
        _readoutUnitId = unitId;
        MarkSelectedCell();
        return _world.ReadoutFor(unitId);
    }

    /// <summary>把某个兵选中（点兵的另一条路，注入按键之外还能一发指定）。</summary>
    public string SelectUnitDebug(string unitId) => WhyReadoutDebug(unitId);

    // === 鼠标：拖放落格 + 点格 + 点兵 ===

    public override void _Input(InputEvent @event)
    {
        if (_draggingKind is not { } kind) return;

        // 拖拽中的落点：面板按下后鼠标在地面上走到哪，就把那一格涂绿（非法涂红）；松手才真落子。
        // 鼠标位移与松手是模拟量、进不了 InputMap action（与相机架那条同样的口径），
        // 所以这条路径的自动化验证走 PlaceUnitDebug 直达口，这里只保证「人手拖得动」。
        if (@event is InputEventMouseMotion motion)
        {
            if (TryCellFromScreen(motion.GlobalPosition, out _, out var row, out var column))
            {
                _grid.MarkCell(row, column, _board.At(row, column) is null && _world.Phase == BattlePhase.Deploying);
            }
            else
            {
                _grid.MarkCell(-1, -1, false);
            }
            return;
        }

        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release) return;
        _draggingKind = null;
        if (_world.Phase != BattlePhase.Deploying)
        {
            Say("已经开打了，拖过来的兵落不下去 —— 要重摆先按 R 回布阵期");
            _grid.ClearMark();
            return;
        }
        if (TryCellFromScreen(release.Position, out var side, out var hitRow, out var hitColumn) && side == BattleSide.Player)
        {
            // 玩家这一手与桥动词 place 走同一条写路径（MainReplayBridge.WriteDebug）：
            // 录制期间它照样进 Journal，不留「鼠标那一手没人录」的缝。
            Say(DeployDebug(DeploymentVerbs.Place,
                DeploymentVerbs.PlaceArgs(BattleContract.KeyOf(kind), hitRow, hitColumn)));
            MarkSelectedCell();
        }
        else
        {
            Say("没落在布阵格里（接敌带与场外不属于任何格）—— 松手取消");
        }
        _grid.ClearMark();
    }

    /// <summary>
    /// 单击（相机架判过「按下即松且位移小于阈值」才喊这里）：布阵期点空格摆选中的兵种、
    /// 点有人那格就选中那个兵；战斗中点哪都只当「我要读这个兵」。
    /// </summary>
    private void OnViewClicked(Vector2 screenPos)
    {
        if (_draggingKind is not null) return;   // 拖拽路径自己收尾，别摆两次

        if (TryCellFromScreen(screenPos, out var side, out var row, out var column) && side == BattleSide.Player)
        {
            if (_board.At(row, column) is { } occupant)
            {
                SelectUnitAtCell(row, column);
                return;
            }
            if (_world.Phase == BattlePhase.Deploying && _pickedKind is { } kind)
            {
                Say(DeployDebug(DeploymentVerbs.Place,
                    DeploymentVerbs.PlaceArgs(BattleContract.KeyOf(kind), row, column)));
                MarkSelectedCell();
                return;
            }
            if (_world.Phase == BattlePhase.Deploying)
            {
                Say("这格空着 —— 先在兵源面板按 1~4 选一个兵种，或直接把它拖过来");
                return;
            }
        }

        // 落点不在格上（接敌带 / 场外）：战斗中就当作「点最近的活人」，布阵期什么都不做
        if (_world.Phase == BattlePhase.Fighting && NearestUnitToScreen(screenPos) is { } nearest)
        {
            _readoutUnitId = nearest.Id;
            MarkSelectedCell();
            Say(_world.ReadoutFor(nearest.Id));
        }
    }

    private void SelectUnitAtCell(int row, int column)
    {
        var home = BattleContract.CellCenter(BattleSide.Player, row, column);
        var unit = _world.Units.FirstOrDefault(u => u.Side == BattleSide.Player
            && u.HomeCell.X == home.X && u.HomeCell.Y == home.Y);
        if (unit is null)
        {
            Say($"格子 ({row},{column}) 上写着有兵，世界里却找不着他 —— 棋盘与世界漂了（这条要当 bug 查）");
            return;
        }
        _readoutUnitId = unit.Id;
        MarkSelectedCell();
        Say($"{unit.Id}「{unit.Look.DisplayName}」已选中：{_world.WhyFor(unit)}");
    }

    private void MarkSelectedCell()
    {
        if (_world.Find(_readoutUnitId) is not { } unit || unit.Side != BattleSide.Player) return;
        if (BattleContract.TryCellFromWorldPoint(unit.HomeCell.X, unit.HomeCell.Y, out _, out var row, out var column))
            _grid.MarkSelected(row, column);
    }

    /// <summary>屏幕坐标反投地面 → 布阵格（5.1 节的换算在逻辑层，这里只做投影）。落点不属于任何格时 false。</summary>
    private bool TryCellFromScreen(Vector2 screenPos, out BattleSide side, out int row, out int column)
    {
        row = -1;
        column = -1;
        side = BattleSide.Player;
        if (_rig.Camera is not { } cam) return false;
        var origin = cam.ProjectRayOrigin(screenPos);
        var dir = cam.ProjectRayNormal(screenPos);
        if (Godot.Mathf.Abs(dir.Y) < 1e-5f) return false;
        var t = -origin.Y / dir.Y;
        if (t <= 0f) return false;
        var hit = origin + dir * t;
        return BattleContract.TryCellFromWorldPoint(hit.X, hit.Z, out side, out row, out column);
    }

    /// <summary>离这次单击的地面落点最近的活人（容差按机位换算：拉远了点不准就是点不准）。</summary>
    private BattleUnit? NearestUnitToScreen(Vector2 screenPos)
    {
        if (_rig.Camera is not { } cam) return null;
        var origin = cam.ProjectRayOrigin(screenPos);
        var dir = cam.ProjectRayNormal(screenPos);
        if (Godot.Mathf.Abs(dir.Y) < 1e-5f) return null;
        var hit = origin + dir * ((-origin.Y) / dir.Y);
        var point = new System.Numerics.Vector2(hit.X, hit.Z);
        var tolerance = 1.5f * _rig.PickToleranceScale;
        return _world.Units.Where(u => u.Alive)
            .OrderBy(u => System.Numerics.Vector2.DistanceSquared(u.Position, point))
            .FirstOrDefault(u => System.Numerics.Vector2.DistanceSquared(u.Position, point) <= tolerance * tolerance);
    }

    // === 键盘：一律走 InputMap action（合成事件注得进来，才谈得上实测） ===

    /// <summary>本原型用的 action（名字与无限领主同一条口径：动作说人话，别造名词）。</summary>
    /// <remarks>
    /// 暂停这一条取公共层那一个概念名（<c>input_toggle_pause</c>）—— 三个原型共用一个概念名，
    /// 绑的键各自不同（这里是 P）。其余五条是本原型自己的内容决定（开局 / 布阵槽 / 回布阵期），
    /// 不进公共层，名字与读法就地声明。
    /// </remarks>
    public static string ActionPause => InputConcept.TogglePause.Name;

    public const string ActionStart = "battle_start";
    public const string ActionPickTank = "deploy_slot_tank";
    public const string ActionPickWarrior = "deploy_slot_warrior";
    public const string ActionPickArcher = "deploy_slot_archer";
    public const string ActionPickRider = "deploy_slot_rider";
    public const string ActionRestart = "deploy_restart";

    /// <summary>
    /// 代码内自建 action（已存在就不覆盖，项目设置可反定义）—— 照 <c>TopDownRig</c> 那份做法：
    /// 裸键轮询注不进合成事件，走 action 才谈得上被 MCP <c>input_action</c> 与重放脚本驱动。
    /// </summary>
    private static void EnsureInputActions()
    {
        AddKeyAction(ActionStart, Key.Enter, Key.Space, Key.KpEnter);
        AddKeyAction(ActionPause, Key.P);
        AddKeyAction(ActionPickTank, Key.Key1, Key.Kp1);
        AddKeyAction(ActionPickWarrior, Key.Key2, Key.Kp2);
        AddKeyAction(ActionPickArcher, Key.Key3, Key.Kp3);
        AddKeyAction(ActionPickRider, Key.Key4, Key.Kp4);
        AddKeyAction(ActionRestart, Key.R);
    }

    private static void AddKeyAction(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private void PollActions()
    {
        // 暂停走公共概念（读法由概念自己带）；其余五条是本原型自己的 action，就地读。
        if (_read.Read(InputConcept.TogglePause)) TogglePauseDebug();
        if (Input.IsActionJustPressed(ActionStart)) StartBattleDebug();
        if (Input.IsActionJustPressed(ActionRestart)) RestartDeployDebug(_seed);
        if (Input.IsActionJustPressed(ActionPickTank)) PickKind(TroopKind.Tank);
        if (Input.IsActionJustPressed(ActionPickWarrior)) PickKind(TroopKind.Warrior);
        if (Input.IsActionJustPressed(ActionPickArcher)) PickKind(TroopKind.Archer);
        if (Input.IsActionJustPressed(ActionPickRider)) PickKind(TroopKind.Rider);
    }

    private void PickKind(TroopKind kind)
    {
        _pickedKind = kind;
        Say($"选中 {KindName(kind)} —— 点一格摆它（或按 Enter 直接开打）");
    }

    /// <summary>兵种叫什么（键 + 兵种卡上的中文名）—— 别叫 <c>Name</c>：这个类继承自 Node，Name 是它的属性。</summary>
    private static string KindName(TroopKind kind)
        => $"{BattleContract.KeyOf(kind)}/{UnitCards.Of(UnitCards.RaceOf(BattleSide.Player), kind).DisplayName}";

    // === 观测口（三个口：时间可控 · 状态可查 · 行为可注入） ===

    /// <summary>
    /// 全量状态快照 —— eval/bridge 直达：一行棋盘 + 一行世界头 + 每兵一行（带 why=）。
    /// 「显示即真相」：这里念的规则与结算用的是同一份（Logic/BattleWorld.DebugSnapshot）。
    /// </summary>
    public string DebugSnapshot() => $"[棋盘]\n{_board.DebugSnapshot()}\n[世界]\n{_world.DebugSnapshot()}";

    /// <summary>暂停面板与 HUD 底部那句：读的就是 <see cref="BattleWorld.ReadoutFor"/>。</summary>
    private string CurrentReadout() => _world.ReadoutFor(_readoutUnitId);

    private void Say(string message)
    {
        GD.Print($"[BattleArray] {message}");
        RollingLog.Write($"[BattleArray] {message}");   // 有界缓冲：debug logs 读的就是这份（OOM 防线之一）
        _hud.ShowFeedback(message);
    }

    /// <summary>当前这一局的世界（eval/bridge 现读用 —— 摆一次子换一份，别在 globals 里存字段）。</summary>
    public BattleWorld World => _world;

    /// <summary>布阵棋盘（同上：读数走属性，永远拿到当前那份）。</summary>
    public DeploymentBoard Board => _board;

    private void RefreshHudNow() => _hud.Refresh(_board, _world, CurrentReadout());

    /// <summary>
    /// 重开一局直达口 —— eval/bridge 直达：<c>RestartBattleDebug(1001, "archers-midline")</c>。
    /// 摆法键：shield-wall / archers-forward / archers-midline（不认识时返回可选清单，不静默兜底）。
    /// 这一口是<b>跳过布阵期直接开打</b>的那条：M4 的对照跑与 M5 的复现都靠它，
    /// 玩家走的那条路是棋盘（<see cref="RestartDeployDebug"/>）。
    /// </summary>
    public string RestartBattleDebug(int seed, string formationKey)
    {
        var formation = Formations.Find(formationKey);
        if (formation is null)
        {
            var keys = string.Join(" / ", Formations.ComparisonSet.Select(f => f.Key));
            return $"未知摆法 '{formationKey}'，可选：{keys}";
        }

        _seed = seed;
        _world.BattleEnded -= OnBattleEnded;
        _world.Dispose();
        _world = new BattleWorld(seed, formation, PlacementComparison.EnemyFormation);
        _world.BattleEnded += OnBattleEnded;
        _tickAccumulator = 0f;
        AttachAbilitiesIfRequested();
        RebuildOverlays(_world);  // 模型 / cue / 树一起重挂 —— 三处换局点走同一个入口，漏一件就静默什么都没有
        _front.CloseAll();
        _readoutUnitId = _world.Units.FirstOrDefault(u => u.Side == BattleSide.Player)?.Id ?? "p01";
        RefreshHudNow();
        _front.NotifyRunStarted();
        return $"restarted seed={seed} formation={formation.Key}\n{SnapshotFirstLine()}";
    }

    /// <summary>终局播报（逻辑层判定 → 日志一行 + 结算屏）。</summary>
    private void OnBattleEnded(EndRow row)
    {
        var verdict = row.Outcome switch
        {
            BattleOutcome.PlayerVictory when _world.DecidedByTimeout => Loc.T("守住就是赢：打满 120 秒双方还有人，按契约判我方赢"),
            BattleOutcome.PlayerVictory => Loc.T("我方胜：敌方全灭"),
            BattleOutcome.PlayerDefeat => Loc.T("我方败：我方全灭"),
            BattleOutcome.Draw => Loc.T("同归于尽：两边在同一步倒完"),
            _ => Loc.T("还没打完"),
        };

        var log = $"[BattleArray] 第 {row.Tick / 10.0:F1} 秒判定：{row.Outcome} 我方阵亡 {row.OurDeaths} 敌方阵亡 {row.EnemyDeaths}";
        GD.Print(log);
        RollingLog.Write(log);   // 这一行是复现比对的锚点：M4 离线报告与运行时在这里逐字对

        // 冻时间轴与结算屏都在这一发里：冻由订阅方做（真值在基类那一处推进口），
        // 屏由前端层画（三个原型原来各画一套）。本方法只报事实。
        _front.NotifyRunEnded(new RunOutcome(
            Loc.Fmt("第 {0:F1} 秒判定 · {1}", row.Tick / 10.0, verdict),
            [Loc.Fmt("我方阵亡 {0}/{1} · 敌方阵亡 {2}/{3}",
                row.OurDeaths, _world.Fielded(BattleSide.Player),
                row.EnemyDeaths, _world.Fielded(BattleSide.Enemy))],
            row.Tick,
            row.Outcome == BattleOutcome.PlayerVictory,
            [(Loc.T("回到布阵重摆一盘"), () => Say(RestartDeployDebug(_seed)))]));

        RefreshHudNow();   // 顶行那一格从「战斗中」翻成「已判定」，不等下一次定时刷新
    }

    /// <summary>局结束这一格：冻住时间轴（三个原型统一的口径，真值在基类那一处推进口）。</summary>
    private void OnRunEnded(RunOutcome outcome)
        => _world.FreezeTime();

    private string SnapshotFirstLine() => _world.DebugSnapshot().Split('\n')[0];

    /// <summary>
    /// 前端层接线（与另两个原型同一份接法）。
    ///
    /// <para><b>结算屏内容不在这里拼</b>：那一屏由前端层画，
    /// <see cref="OnBattleEnded"/> 只递标题、明细与按钮（「回到布阵重摆一盘」那一条）。</para>
    /// </summary>
    private void SetupFrontEnd()
    {
        _front = new FrontEndLayer { Name = "FrontEnd" };
        AddChild(_front);

        _front.PauseRequested += paused => _world.SetPaused(paused);
        _front.QuitRequested += () => GetTree().Quit();
        _front.SetTitleEntries(
            (Loc.T("开始一局（沿用当前棋盘）"), () => Say(StartBattleDebug())),
            (Loc.T("回到布阵期重摆"), () => Say(RestartDeployDebug(_seed))));

        // 局开始与局结束各订阅一格（这两个事件在三个原型里都有订阅方）。
        _front.RunStarted += () => Say($"[BattleArray] 局开始（seed={_seed} · {_world.Phase}）。");
        _front.RunEnded += OnRunEnded;

        foreach (var (verb, handler) in FrontEndVerbs.Vocabulary(_front))
            _bridge?.Register(verb, handler);
    }

    // === 相机与底座（M5 那份，动过一处：初始机位改成摆阵看得清的那一档） ===

    /// <summary>
    /// 俯视角相机架接入（滚轮缩放 · 中键拖拽绕转/改俯仰 · WASD 平移 · Q/E 或 ←→ 转 · ↑↓ 俯仰
    /// · Home 归位 · Tab 请求锁定）。相机架只管相机：点中了谁属于玩法语义，在这里判完再说。
    /// </summary>
    private void SetupCameraRig()
    {
        ReadCameraOverride();   // 覆盖档必须先读：下面每个数都要问它一遍（没覆盖时全部走缺省常量）
        var world = GetNode<Node3D>("World");
        var rig = new TopDownRig
        {
            BoundsMinXZ = new Vector2(-BattleContract.ArenaHalfWidth, -BattleContract.ArenaHalfDepth),
            BoundsMaxXZ = new Vector2(BattleContract.ArenaHalfWidth, BattleContract.ArenaHalfDepth),
            // 参考机位＝名牌调字号的那一挡，头顶文字按「距离 ÷ 参考距离」缩放
            ReferenceDistance = CamVal("ref", PlateReferenceDistance),
            MinDistance = CamVal("min", MinCameraDistance),
            MaxDistance = CamVal("max", MaxCameraDistance),
            // **俯角下限钉在 22°**（公共层 2026-10-15 把缺省放开到 −89.99° 之后，这一格由本原型显式钉回）。
            // 理由是**布阵点格走地面射线**（`TryCellFromScreen`：射线与 y=0 平面求交换算格子），
            // 相机一旦压到地平线以下，射线打到的格子在画面上方远处 —— 点哪都选中不了手底下那一格，
            // 而且**不报错**：选中的兵会安静地摆到别处。那是「显示与操作对不上」那一类静默故障。
            // 认人走的是屏幕最近邻（`SelectUnitAtCell` 那条链），俯角无关；唯独布阵这一条依赖地面。
            MinPitchDegrees = 22f,
            PitchDegrees = InitialPitch,
        };
        world.AddChild(rig);

        var camera = new Camera3D { Name = "Camera3D", Fov = CamVal("fov", CameraFov) };
        world.AddChild(camera);
        rig.AttachCamera(camera);

        rig.SetPose(new Vector3(0f, 0f, CamVal("focusz", CameraFocusZ)),
            CamVal("dist", InitialDistance), CamVal("yaw", InitialYaw), CamVal("pitch", InitialPitch));
        rig.SaveHome();
        _rig = rig;
    }

    /// <summary>
    /// 初始机位（米）：这一档要一眼看完整场 —— 24×24 的场地转 45° 之后在屏幕上是 34 米宽的对角菱形，
    /// 36 米恰好把它装进框还留一圈余量（实测出图 <c>.cache/Movie</c> 2026-09-29）。
    /// M5/M6 那两版是 17 米配 75° 视场角只看清布阵区，整场留给滚轮；现在整场就是默认档。
    /// </summary>
    private const float InitialDistance = 36f;

    /// <summary>初始焦点深度（米）：整场默认档不往哪一侧偏，取场地正中（偏我方 1 米，让名牌密一些的那一侧多留一点画面）。</summary>
    private const float CameraFocusZ = 1f;

    /// <summary>
    /// 名牌字号的参考机位（米）：头顶文字与血条走「距离 ÷ 参考距离」换算（「相机单源」），
    /// 所以**视场角一变这一档就得跟着变** —— 屏幕尺寸 ∝ 世界尺寸 ÷ (距离 × tan(视场角/2))，
    /// 距离与视场角的乘积不变，字才还是原来那么大。
    /// 换算实测：原 15 米 @ 75° ⇒ 32° 下等价于 15 × tan(37.5°)/tan(16°) = 40 米；
    /// 出图比了 40（字偏小）与 28（字挤成一堆）两档，取 <b>34</b>（15 v 15 满编时名牌不互相压）。
    /// </summary>
    private const float PlateReferenceDistance = 34f;

    /// <summary>
    /// 初始俯角（度）：30° 是照参考图量出来的，不是挑的。
    /// 量法 —— 地面方格投影到屏幕上，其像素二阶矩的 y/x 标准差之比 = sin(俯角)（与偏航无关），
    /// 两幅参考图各取十余块地面方格反解，中位值 28.4° 与 29.0° ⇒ 取整 30°（正好是像素等距的 2:1 菱形）。
    /// 相机架的俯角上下限缺省是 −89.99°~89.99°（公共层 2026-10-15 那一轮改的，能绕到主体下方往上看），
    /// 这一档在范围内；再低那一段靠 <see cref="SetupStage"/> 的雾糊掉。
    /// </summary>
    private const float InitialPitch = 30f;

    /// <summary>
    /// 初始偏航（度）：45° 让场地的两条边在屏幕上对称斜开，格子的边不再是横平竖直而是菱形。
    /// 这一转换来两件事：我方与敌方分列左下/右上，两侧前排不再互相正对遮挡；
    /// 单挑局里两个兵一前一后也不会像 0° 那样叠成一个人（0° 档实测出图对比过，就是叠）。
    /// </summary>
    private const float InitialYaw = 45f;

    /// <summary>
    /// 相机垂直视场角（度）：低俯角下「平不平」其实由这一档决定 —— 75° 时近处的兵是远处的三倍大，
    /// 菱形场地被拉成鱼眼；32° 把形变压到 1.3 倍左右，才出来参考图那种「等距但不死板」的观感。
    /// 缩放仍走距离不改视场角（剪影不漂，见 <c>TopDownRig</c> 头注），所以这一档是固定值而不是旋钮。
    /// </summary>
    private const float CameraFov = 32f;

    /// <summary>贴脸一档的距离下限（米）：32° 视场角下 9 米已经能看清一个兵的剪影与出手。</summary>
    private const float MinCameraDistance = 9f;

    /// <summary>一眼看完整场的距离上限（米）：地面与雾的边界按这一档铺（见 <see cref="SetupStage"/>）。</summary>
    private const float MaxCameraDistance = 80f;

    /// <summary>
    /// 单挑局的自动取景：整场那一档 36 米是给 15 v 15 用的，1v1 两个兵在画面里只剩一小撮、
    /// 前摇与落点根本看不出来 ⇒ 焦点落在活着的兵中间、距离按他们的间距倒推（开局隔 12 米就拉到
    /// <see cref="DuelCameraMaxDistance"/>，贴上之后收到 <see cref="DuelCameraMinDistance"/> 特写）。
    /// 相机架只管机位（AGENTS 「相机单源」），这一发不碰任何逻辑真值。
    /// </summary>
    private void FrameDuelCamera()
    {
        var alive = _world.Units.Where(u => u.Alive).ToList();
        if (alive.Count == 0) return;
        var focusX = alive.Average(u => u.Position.X);
        var focusDepth = alive.Average(u => u.Position.Y);   // 逻辑的深度轴 Y 就是世界的 Z
        var diameter = 0f;
        foreach (var a in alive)
        {
            foreach (var b in alive)
            {
                diameter = Math.Max(diameter, System.Numerics.Vector2.Distance(a.Position, b.Position));
            }
        }

        var distance = Math.Clamp(DuelCameraPadding + diameter * DuelCameraSpreadScale,
            DuelCameraMinDistance, DuelCameraMaxDistance);
        // 偏航与俯角跟整场同一档（两档视角不一致，看客会以为换了个游戏）；距离可被覆盖口另调
        _rig.SetPose(new Vector3(focusX, 0f, focusDepth), CamVal("dueldist", distance),
            CamVal("yaw", InitialYaw), CamVal("duelpitch", DuelCameraPitch));
    }

    /// <summary>单挑局取景的最近一档（米）：贴上之后要能看清起手与落点，但不顶到模型脸上。</summary>
    private const float DuelCameraMinDistance = 11f;

    /// <summary>单挑局取景的最远一档（米）：开局两人隔着 12 米，22 米把两个都装进框还有余量。</summary>
    private const float DuelCameraMaxDistance = 22f;

    /// <summary>取景余量（米）：间距为零时也要留的这一截，免得特写把人顶出画面。</summary>
    private const float DuelCameraPadding = 8f;

    /// <summary>间距到机位的换算系数：两人每多拉开 1 米，机位退 0.9 米。</summary>
    private const float DuelCameraSpreadScale = 0.9f;

    /// <summary>单挑局的俯角（度）：与整场同一档（45° 偏航下前后站位本来就分得开，不必再压低）。</summary>
    private const float DuelCameraPitch = 30f;

    /// <summary>
    /// 场景底座：太阳、地面、地平线。没有这三样，.glb 会糊在一片清屏色里连剪影都读不出。
    /// </summary>
    private void SetupStage()
    {
        var world = GetNode<Node3D>("World");

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55f, -30f, 0f),
            ShadowEnabled = true,
        };
        world.AddChild(sun);

        // 地面要盖到最远机位（MaxCameraDistance 那一档）的取景边缘之外；留到 400 米不花钱，一块 quad 而已。
        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(400f, 400f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.18f, 0.22f, 0.18f),
            },
        };
        world.AddChild(ground);

        // 低俯角看出去的地平线：天空底色与雾同色，地面在到达边界前淡进同一片颜色里。
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
            FogDensity = 0.006f,
        };
        world.AddChild(new WorldEnvironment { Name = "Horizon", Environment = env });
    }

    // === 行为树（M5-c）：一棵树、八个兵种参数档 ===

    /// <summary>兵种参数档所在目录 —— 一个兵种一份 .tscn，文件名照兵种卡的模型名（数据驱动，不在这里开分支）。</summary>
    private const string BrainRootPath = "res://src/Prototypes/BattleArray/Scenes/Brains/";

    private readonly Dictionary<string, BattleBrainBridge> _bridges = new(System.StringComparer.Ordinal);

    private readonly Dictionary<string, Node> _brains = new(System.StringComparer.Ordinal);

    /// <summary>
    /// 给每个兵挂一棵树，并把 <see cref="BattleWorld.Brain"/> 指到「问这兵的树」。
    /// 树没挂上的兵不留空档：世界照旧走同一条纯 C# 规则，但 <see cref="BrainReadout"/> 会报出
    /// 「几个兵在听树」——挂了 0 个也是「跑通了」的样子，所以这一格必须能被读出来。
    /// </summary>
    private void AttachBrains(BattleWorld world)
    {
        DetachBrains();
        foreach (var unit in world.Units)
        {
            var path = BrainRootPath + unit.Look.ModelName + ".tscn";
            if (GD.Load<PackedScene>(path)?.Instantiate() is not Node brain)
            {
                GD.PushError($"[BattleArray] 兵种参数档加载失败，这个兵退回纯 C# 规则：{path}");
                continue;
            }

            var bridge = new BattleBrainBridge { Name = $"Bridge{unit.Id}" };
            brain.Name = $"Brain{unit.Id}";
            brain.Set("bridge", bridge);   // 必须先注入再入树：宿主 _ready 里就要用 bridge
            bridge.Wire(world, unit.Id, brain);   // 也要先接线再入树：宿主 _ready 里要读兵种卡核对参数档
            _brainRoot.AddChild(bridge);
            _brainRoot.AddChild(brain);
            _bridges[unit.Id] = bridge;
            _brains[unit.Id] = brain;
        }

        world.Brain = (unit, senses) => _bridges.TryGetValue(unit.Id, out var bridge)
            ? bridge.DecideFor(senses)
            : BattleBrainPolicy.Decide(senses);
        GD.Print($"[BattleArray] 行为树接管 {_bridges.Count}/{world.Units.Count} 个兵" +
                 $"（一棵树、八个兵种参数档在 {BrainRootPath}）");
    }

    private void DetachBrains()
    {
        foreach (var child in _brainRoot.GetChildren())
        {
            child.QueueFree();
        }
        _bridges.Clear();
        _brains.Clear();
    }

    /// <summary>行为树读数 — eval/bridge 直达：BrainReadout() 报「几个兵挂了树 / 树实际判了多少步」。</summary>
    /// <remarks>
    /// 那条不变量写的是「至多一次」而不是「正好一次」：死人不再被问、判定后整局停手，
    /// 所以 <c>treeDecisions</c> 只会小于「兵数 × 步数」——等号在残局永远不成立，
    /// 拿等号当尺子会把「树没在管事」和「打到残局了」读成同一句话（M6 实测踩到，已改口）。
    /// 真要出事的是反向：树静默没接上时 decisions 会远小于上限且 <c>brains</c> 报得出挂了几个。
    /// </remarks>
    public string BrainReadout()
    {
        var decisions = 0;
        foreach (var bridge in _bridges.Values) decisions += bridge.DecisionsTaken;
        return $"brains={_bridges.Count}/{_world.Units.Count} treeDecisions={decisions} " +
               $"tick={_world.Tick} phase={_world.Phase} alive={_world.Units.Count(u => u.Alive)} " +
               $"每人每步至多一次={decisions <= (long)_bridges.Count * _world.Tick}";
    }

    /// <summary>
    /// 行为流采样档 — eval/bridge 直达：SetBrainLogEvery(1) 让每个兵每步打一行（细看某个兵为什么这么判），
    /// SetBrainLogEvery(0) 全静音，SetBrainLogEvery(50) 回默认（5 秒一行）。
    /// </summary>
    public string SetBrainLogEvery(int everySteps)
    {
        foreach (var brain in _brains.Values) brain.Set("log_every_steps", everySteps);
        return $"log_every_steps={everySteps}（brains={_brains.Count}）";
    }

    // === 相机 eval 直达口 — 模板自带，AI 可远程摆机位/读机位（视觉回归的基准面） ===

    /// <summary>视角读数直达 — eval/bridge 直达：CameraReadout() 返回机位（焦点/距离/偏航/俯角/跟随目标）。</summary>
    public string CameraReadout() => _rig.DebugReadout();

    /// <summary>
    /// 机位定点直达 — eval/bridge 直达：CameraSetPose(focusX, focusZ, distance, yaw, pitch)
    /// 立即把相机摆到指定机位（不过阻尼），返回摆位后的读数。
    /// </summary>
    public string CameraSetPose(float focusX, float focusZ, float distance, float yawDegrees, float pitchDegrees)
    {
        _rig.SetPose(new Vector3(focusX, 0f, focusZ), distance, yawDegrees, pitchDegrees);
        return _rig.DebugReadout();
    }

    // === AI 调试操控台（模板那份：SpeedControl 攒够一个步长才 Advance(1)） ===

    /// <summary>调试速度旋钮 — 本原型 10 步/秒（0.1 秒一步，报时间只说秒）。</summary>
    private readonly SpeedControl _speed = new(tickRatePerSecond: 10.0);

    /// <summary>本帧应推的步数累积器（表现层持有的浮点累积器 — 攒满一步的游戏时长才 Advance）。</summary>
    private float _tickAccumulator;

    /// <summary>读法分派：概念名与读法都来自公共层，引擎那三个读数口在本件递进去。</summary>
    private readonly InputRead _read = new(name => Input.IsActionJustPressed(name), name => Input.IsActionPressed(name),
        name => Input.GetActionStrength(name));

    /// <summary>速度旋钮直达 — eval/bridge 直达：SetSpeed(20) 快进赶路 / SetSpeed(0.25) 慢放细看 / SetSpeed(1) 实时。</summary>
    public string SetSpeed(double multiplier)
    {
        _speed.SetMultiplier(multiplier);
        return $"speed={_speed.Multiplier}x";
    }

    /// <summary>逻辑暂停直达 — eval/bridge 直达：PauseDebug() 冻时间轴（渲染照跑），ResumeDebug() 续。</summary>
    public string PauseDebug()
    {
        if (_world.Phase == BattlePhase.Deploying) return "布阵期还没开打（暂停钮不接）";
        _world.SetPaused(true);
        return $"paused tick={_world.Tick}";
    }

    /// <summary>恢复逻辑 — eval/bridge 直达：ResumeDebug()。</summary>
    public string ResumeDebug()
    {
        if (_world.Phase == BattlePhase.Deploying) return "布阵期还没开打（要起跑用 StartBattleDebug）";
        _world.SetPaused(false);
        return $"resumed tick={_world.Tick}";
    }

    /// <summary>
    /// 单步直达 — eval/bridge 直达：走一个逻辑步（**暂停中也照走**，M6 的「随时暂停」手感就是停下来一格格看），
    /// 返回步进后的步数与暂停状态。布阵期与已判定时不做事，并如实报出为什么没走。
    /// </summary>
    public string StepDebug()
    {
        var stepped = _world.AdvanceOneDebugStep();
        return stepped
            ? $"tick={_world.Tick} phase={_world.Phase} paused={_world.IsPaused}"
            : $"没走一步：当前 {_world.Phase}（布阵期还没开打；已判定则不再演化）";
    }

    // === 调试桥（M6-c：接的是 GugaFramework/DebugBridge/DebugConsole 那份公共样板） ===

    /// <summary>
    /// eval 脚本的 globals（public 嵌套类 —— Roslyn 脚本编译要可访问性）。
    /// 只放 <see cref="Main"/>：世界与棋盘每次摆子都换一份，走 <see cref="World"/>/<see cref="Board"/>
    /// 两个属性现读，globals 里那份永不会过期（InfinityLord 那边世界是长命的，可以直接塞字段）。
    /// </summary>
    public sealed class BattleArrayGlobals
    {
        /// <summary>主节点（脚本里 <c>Main.DebugSnapshot()</c>、<c>Main.PlaceUnitDebug("archer",2,0)</c> 直达）。</summary>
        public Main Main { get; set; } = null!;
    }

    /// <summary>桥宿主（没配 DEBUG_BRIDGE_PORT / --debug-bridge-port 时内部不监听，零开销）。</summary>
    private DebugBridgeHost? _bridge;

    /// <summary>
    /// 接桥：<b>标准七个动词由 <see cref="DebugConsole"/> 给</b>（ping/verbs/targets/call/eval/evaldiag/logs），
    /// 这里只递本原型的动词表 —— 摆子 / 开局 / 暂停 这三条玩家写入口，与快照 / 棋盘 / 树读数。
    /// 一条纪律：动词一律转发到已有的 eval 直达方法，<b>不许再造第二套实现</b>。
    /// </summary>
    private void SetupDebugBridge()
    {
        _bridge = DebugConsole.Attach(this, new BattleArrayGlobals { Main = this },
            ("speed", a => SetSpeed(a.Num("multiplier", required: true))),
            ("pause", _ => PauseDebug()),
            ("resume", _ => ResumeDebug()),
            ("step", _ => StepDebug()),
            ("start", _ => StartBattleDebug()),
            ("toggle_pause", _ => TogglePauseDebug()),
            ("place", a => PlaceUnitDebug(a.Str("kind"), (int)a.Num("row", required: true),
                (int)a.Num("column", required: true))),
            ("remove", a => RemoveUnitDebug((int)a.Num("row", required: true), (int)a.Num("column", required: true))),
            ("move", a => MoveUnitDebug((int)a.Num("from_row", required: true), (int)a.Num("from_column", required: true),
                (int)a.Num("to_row", required: true), (int)a.Num("to_column", required: true))),
            ("board", _ => BoardReadoutDebug()),
            ("why", a => WhyReadoutDebug(a.Str("unit"))),
            ("snapshot_debug", _ => DebugSnapshot()),
            ("brain", _ => BrainReadout()),
            ("brain_log", a => SetBrainLogEvery((int)a.Num("every", required: true))),
            ("camera_pose", a => CameraSetPose((float)a.Num("x", required: true), (float)a.Num("z", required: true),
                (float)a.Num("distance", required: true), (float)a.Num("yaw", required: true),
                (float)a.Num("pitch", required: true))),
            ("restart", a => RestartBattleDebug((int)a.Num("seed", required: true), a.Str("formation"))),
            ("restart_deploy", a => RestartDeployDebug((int)a.Num("seed", required: true))),
            // === 技能层 H3 与级 2 重放（D8 / D3 / D1 的运行时半边，实现见 Scripts/MainReplayBridge.cs）===
            // 一条纪律：写操作只走 inject（词表 → Journal），下面这几发都不另实现一遍逻辑。
            ("duel", a => StartDuelRiftDebug(a.Int("seed", DuelRiftSetup.DefaultSeed))),
            ("advance", a => AdvanceDebug((int)a.Num("steps", required: true))),
            ("ability_snapshot", _ => AbilitySnapshotDebug()),
            ("ability_log", a => AbilityLogDebug(a.Int("count", 30))),
            // cue 派发台的只读读数（演/合并/dropped + 丢了哪条）。读侧，不写任何真值。
            ("cues", _ => CueReadoutDebug()),
            // 一次性动作台账（起手动作与平A 各演了几次、被吞几次）—— N-30 那一格，同样是读侧。
            ("windups", _ => WindupReadoutDebug()),
            ("inject", a => InjectDebug(a.Str("cmd"), a.Str("args"))),
            ("record_start", _ => RecordStartDebug()),
            ("mark", a => MarkDebug(a.Str("label"))),
            ("record_stop", a => RecordStopDebug(a.Str("name"))),
            ("replay_start", a => ReplayStartDebug(a.Str("path"))),
            ("replay_step", a => ReplayStepDebug(a.Int("items", 1))),
            ("replay_run", _ => ReplayRunDebug()),
            ("replay_dump", _ => ReplayDumpDebug()),
            ("replay_detail", a => ReplayDetailDebug((int)a.Num("seq", required: true))));

        // 输入合成那四条（「行为可注入」那一半）：input_action / input_click / input_hover / input_read。
        // Attach 之后单独注册：它的词表参数是 `params`，`..` 展开只在集合初始化里合法。
        // 机制、四条实测读数与「合成滞后一帧」那个坑在 InputInjector 的类注里。
        foreach (var (verb, handler) in InputInjector.Vocabulary(this, RecordSyntheticInput))
            _bridge?.Register(verb, handler);

        // i18n 两动词：set_locale 切语言、locale_probe 读当前语言/字体链/某键的取字结果。
        // 机制与接法见 GugaFramework/I18n/LocaleVerbs 类注。
        foreach (var (verb, handler) in DemoFactory.GugaFramework.I18n.LocaleVerbs.Vocabulary())
            _bridge?.Register(verb, handler);

        GD.Print($"[BattleArray] 调试桥已接线（{_bridge?.Name ?? "宿主没挂上"}；" +
                 "没配 DEBUG_BRIDGE_PORT 时它根本不监听 —— 出货零开销）");
    }

    public override void _Process(double delta)
    {
        PollActions();

        // 时间轴推进：逻辑步长与帧率解耦 —— 攒满一步（1 步 = 0.1 秒游戏时间）才推一步
        // （AGENTS「报时间只认逻辑时钟」）。排空的门槛是 <b>1 步</b>不是 0.1：
        // SpeedControl.TicksPerFrame 返回的单位是「步」（它自己的注释：1x·60fps → 0.166 步/帧），
        // 拿 0.1 当门槛就是每 0.1 步当一步走 ⇒ 每帧多推 10 倍，1x 名义实时、实际十倍速。
        if (!_world.IsPaused)
        {
            _tickAccumulator += (float)_speed.TicksPerFrame(delta);
            while (_tickAccumulator >= 1f)
            {
                _tickAccumulator -= 1f;
                _world.Advance(1);
            }
        }

        // 镜像照跑：暂停冻的是逻辑演化，不是画面 —— 机位还能转、血条与名牌还得跟着相机缩放
        _units.MirrorAll((float)delta, _speed.Multiplier);
        _cues.Mirror((float)delta, _speed.Multiplier);   // cue 只订观测面；暂停时没有新账，画面不刷计数
        _hud.FadeFeedback(delta);

        _hudRefreshAccumulator += delta;
        if (_hudRefreshAccumulator >= HudRefreshSeconds)
        {
            _hudRefreshAccumulator = 0.0;
            RefreshHudNow();
            // 单挑局跟着两个兵走：每刷一次 HUD 重摆一次机位（间距在收，特写就得跟着推近）
            if (_autoFrameDuel) FrameDuelCamera();
        }
    }

    public override void _ExitTree()
    {
        _world.BattleEnded -= OnBattleEnded;
        _board.Changed -= OnBoardChanged;
        _rig.ViewClicked -= OnViewClicked;
        _world.Dispose();
    }
}
