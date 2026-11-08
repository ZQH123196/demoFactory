#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;
// 控件坐标吃 Godot 的类型，「哪一军的格子」用逻辑层的 Side —— 两边同名，起别名（CS0104）。
using BattleSide = DemoFactory.Prototypes.BattleArray.Logic.Side;

namespace DemoFactory.Prototypes.BattleArray;

/// <summary>
/// 布阵期 HUD ＋ 暂停面板（M6 的「UI」那一半）——运行时挂到 UILayer，场景文件不改
/// （照无限领主那份 HUD 的做法：常驻标签一律 <c>MouseFilter = Ignore</c>，
/// 不然它会在地面上吞掉点格与中键转视角）。
///
/// <para><b>结算屏不在这里</b>：那一屏由前端层画（<c>FrontEndLayer.SetResult</c>），
/// 三个原型原来各写一套结算 UI，这一屏并进公共层之后本控件只留棋盘与阵面读数
/// —— 那些是玩法内容，不是结算屏。</para>
///
/// 这个面板只做两件事：<b>把棋盘与世界的账念出来</b>、<b>把玩家的动作递回逻辑层</b>。
/// 它不存「哪格站了谁」「还剩多少钱」——那些都在 <see cref="DeploymentBoard"/> 与
/// <see cref="BattleWorld"/> 里，每次刷新现读（无状态表现层）。
/// 四颗兵种按钮是遍历 <see cref="BattleContract.Kinds"/> 生成的，没有一处 <c>if (kind == Archer)</c>：
/// 叫法来自兵种卡，价格来自 <see cref="BattleContract.PriceOf"/>（2.1 节：价差只是面板上的数字，不做平衡）。
/// </summary>
public partial class BattleHud : Control
{
    /// <summary>按下兵种按钮 = 选中它（之后点格子就摆它）。</summary>
    public event Action<TroopKind>? KindPicked;

    /// <summary>按下并保持 = 开始拖拽（松手由主场景判落在哪一格）。</summary>
    public event Action<TroopKind>? DragStarted;

    /// <summary>拖拽松手（不管落在哪，主场景收尾清状态）。</summary>
    public event Action? DragEnded;

    /// <summary>「开局」按钮。</summary>
    public event Action? StartRequested;

    /// <summary>「暂停 / 继续」按钮（战斗期间唯一能做的事，design.md 1 节）。</summary>
    public event Action? PauseRequested;

    /// <summary>「回到布阵」按钮（兵源那一块里的那颗，结算屏上那颗由前端层画）。</summary>
    public event Action? RestartRequested;

    private readonly Dictionary<TroopKind, Button> _kindButtons = new();

    private Label _topLine = null!;
    private Label _purseLine = null!;
    private Label _whyLine = null!;
    private Label _feedbackLine = null!;
    private PanelContainer _deployPanel = null!;
    private VBoxContainer _rosterBox = null!;
    private Button _startButton = null!;
    private Button _pauseButton = null!;

    public override void _Ready()
    {
        // 根控件不吃鼠标：它只是给子节点一个坐标参照（常驻标签一律 Ignore，否则地面上点不动格）
        MouseFilter = MouseFilterEnum.Ignore;

        BuildDeployPanel();       // 状态两行就在面板里（跟着 VBox 排，不去跟绝对坐标较劲）
        BuildWhyAndFeedback();
        Arrange();
        // 窗口/出图分辨率一变就重排（下面那条「不用锚点」的理由）
        GetViewport().SizeChanged += Arrange;
    }

    /// <summary>
    /// 按当前视口尺寸给每一块**绝对坐标与宽高**。
    /// 为什么不用锚点预设：挂在 CanvasLayer 直下的 Control 拿不到可靠的父级尺寸
    /// （M6 出图实测：BottomWide 那一条算出负宽高，整句读数被挤成一列竖排的字）——
    /// 与其跟锚点算式较劲，不如照视口尺寸直接摆，改窗口时重排一次。
    /// 面板里的行交给 VBoxContainer 自己排，这里只管三块浮着的。
    /// </summary>
    private void Arrange()
    {
        var size = GetViewport().GetVisibleRect().Size;
        Size = size;
        var margin = 16f;

        _feedbackLine.Position = new Vector2(size.X * 0.5f - 320f, 8f);
        _feedbackLine.Size = new Vector2(640f, 64f);

        // 右侧那一栏（技能栏读数 + cue 读数）已删（用户确认）：核对不读那两行字，
        // 同一串数字走桥 `cues` 与 `ability_snapshot` 读得更准；留在画面上只是占右半屏，
        // 还贡献过两次只有出图才看见的排版事故（满阵下换七行、把别的字压住）。

        // 底部这一条留给「谁为什么不动」：点谁读谁的长句，横向铺开才读得完（M6 定的位置不动）
        _whyLine.Position = new Vector2(margin, size.Y - 74f);
        _whyLine.Size = new Vector2(size.X - margin * 2f, 62f);

        _deployPanel.Position = new Vector2(margin, 84f);
        _deployPanel.Size = new Vector2(356f, 372f);
    }

    /// <summary>每刷一次把棋盘与世界的真值念一遍（调用方控频，不必每渲染帧来）。</summary>
    public void Refresh(DeploymentBoard board, BattleWorld world, string whyReadout)
    {
        var deploying = world.Phase == BattlePhase.Deploying;
        // 「第几秒」只认判定的那一刻：判定之后时间轴还可以推（世界不再演化），
        // 拿步号当读数会念出「战斗中 · 第 164.9 秒」这种话 —— 仗早在 27.9 秒就打完了（M6 出图抓到）。
        var seconds = (world.OutcomeTick ?? world.Tick) / 10.0;
        _topLine.Text = world.Phase switch
        {
            BattlePhase.Deploying => Loc.Fmt("布阵期 · 场上 {0}/{1} 格 · 对面 {2} 个兵等着", board.Filled, DeploymentBoard.CellCount, world.Fielded(BattleSide.Enemy)),
            BattlePhase.Fighting => Loc.Fmt("战斗中 · 第 {0:F1} 秒 · 我方存活 {1}/{2} ", world.Tick / 10.0, world.AliveCount(BattleSide.Player), world.Fielded(BattleSide.Player)) +
                                   Loc.Fmt("敌方存活 {0}/{1}", world.AliveCount(BattleSide.Enemy), world.Fielded(BattleSide.Enemy)),
            _ => Loc.Fmt("已判定 · 第 {0:F1} 秒 · {1} · 我方存活 {2}/{3} ", seconds, world.Outcome, world.AliveCount(BattleSide.Player), world.Fielded(BattleSide.Player)) +
                 Loc.Fmt("敌方存活 {0}/{1}", world.AliveCount(BattleSide.Enemy), world.Fielded(BattleSide.Enemy)),
        };
        _purseLine.Text = Loc.Fmt("钱 {0}/{1}（已花 {2}）", board.Remaining, board.StartingPurse, board.Spent) +
                          Loc.T(" · 单价只是面板上的数字：坦 120 战 100 弓 80 骑 150");
        foreach (var (kind, button) in _kindButtons)
        {
            var displayName = UnitCards.Of(UnitCards.RaceOf(BattleSide.Player), kind).DisplayName;
            button.Text = Loc.Fmt("{0} ×{1}（{2}）· {3}", Loc.T(displayName), board.CountOf(kind), BattleContract.PriceOf(kind), BattleContract.KeyOf(kind));
            button.Disabled = !deploying;
        }
        _startButton.Disabled = !deploying;
        _rosterBox.Visible = deploying;   // 面板留着（战斗中要读顶行），藏起来的只是兵源那一块
        _pauseButton.Text = world.IsPaused ? Loc.T("继续（P）") : Loc.T("暂停（P）");
        // 暂停钮只在战斗中管事：布阵期没有暂停可解（起跑另有开局口），已判定也没什么可停
        _pauseButton.Disabled = world.Phase != BattlePhase.Fighting;
        _whyLine.Text = whyReadout;
    }

    /// <summary>刚摆完子 / 刚开局 / 被拒绝的那一笔：把回执念出来（同一句话也进日志）。</summary>
    public void ShowFeedback(string message)
    {
        _feedbackLine.Text = message;
        _feedbackLine.Modulate = new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>反馈字自己变淡（3 秒后基本看不见；主场景每帧推一下，不占定时器）。</summary>
    public void FadeFeedback(double delta)
    {
        if (_feedbackLine.Text.Length == 0) return;
        var alpha = _feedbackLine.Modulate.A;
        if (alpha <= 0.01f) return;
        _feedbackLine.Modulate = new Color(_feedbackLine.Modulate.R, _feedbackLine.Modulate.G,
            _feedbackLine.Modulate.B, alpha - (float)delta * 0.22f);
    }

    // === 建控件 ===

    private void BuildDeployPanel()
    {
        _deployPanel = new PanelContainer { Name = "DeployPanel" };
        AddChild(_deployPanel);

        var box = new VBoxContainer { Name = "Rows" };
        box.AddThemeConstantOverride("separation", 8);
        _deployPanel.AddChild(box);

        // 状态两行摆在面板最上面：布阵期念格数，开打后念打到第几秒、两边还剩几个活人；
        // 第二行是钱 —— 2.1 节说它退化成名义额度，那就只配占一行小字。
        // 这两行<b>开打后也要看得见</b>：整个面板一起藏起来就等于战斗中没有任何 HUD 读数（M6 出图抓到）。
        _topLine = MakeRowLabel(string.Empty, 22);
        box.AddChild(_topLine);
        _purseLine = MakeRowLabel(string.Empty, 16);
        box.AddChild(_purseLine);

        // 兵源那一块（标题 + 四颗按钮 + 开局 + 提示）只在布阵期出现，藏的是这一块、不是整块面板
        _rosterBox = new VBoxContainer { Name = "Roster" };
        _rosterBox.AddThemeConstantOverride("separation", 8);
        box.AddChild(_rosterBox);

        _rosterBox.AddChild(MakeRowLabel(Loc.T("兵源（拖到格子上 · 或按 1~4 选中再点格）"), 20));

        // 四颗按钮从兵种槽位表生成 —— 加第五个兵种时这里一行都不用改
        foreach (var kind in BattleContract.Kinds)
        {
            var button = new Button { Name = $"Pick{BattleContract.KeyOf(kind)}", FocusMode = FocusModeEnum.None };
            var captured = kind;
            button.ButtonDown += () =>
            {
                KindPicked?.Invoke(captured);
                DragStarted?.Invoke(captured);
            };
            button.ButtonUp += () => DragEnded?.Invoke();
            _rosterBox.AddChild(button);
            _kindButtons[kind] = button;
        }

        _startButton = new Button { Text = Loc.T("开局（Enter）"), Name = "StartBattle", FocusMode = FocusModeEnum.None };
        _startButton.Pressed += () => StartRequested?.Invoke();
        _rosterBox.AddChild(_startButton);

        var hint = MakeRowLabel(Loc.T("摆不下的格子涂红；中央接敌带与场外不属于任何格。"), 16);
        hint.Modulate = new Color(1f, 1f, 1f, 0.6f);
        _rosterBox.AddChild(hint);

        _pauseButton = new Button { Text = Loc.T("暂停（P）"), Name = "TogglePause", FocusMode = FocusModeEnum.None };
        _pauseButton.Pressed += () => PauseRequested?.Invoke();
        box.AddChild(_pauseButton);

        var restart = new Button { Text = Loc.T("回到布阵（R）"), Name = "BackToDeploy", FocusMode = FocusModeEnum.None };
        restart.Pressed += () => RestartRequested?.Invoke();
        box.AddChild(restart);
    }

    private void BuildWhyAndFeedback()
    {
        // 暂停时读出的那句「谁为什么不动」：屏幕底部一整条（长句靠自动换行，位置见 Arrange()）
        _whyLine = MakeFloatingLabel("WhyLine", 18);
        _whyLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_whyLine);

        _feedbackLine = MakeFloatingLabel("FeedbackLine", 20);
        _feedbackLine.HorizontalAlignment = HorizontalAlignment.Center;
        _feedbackLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_feedbackLine);
    }

    // === 小工厂 ===

    /// <summary>
    /// 挂在本控件上的浮字：只建对象与设字号，**坐标与宽高交给 <see cref="Arrange"/>**；
    /// 一律 <c>MouseFilter = Ignore</c> —— 常驻标签若不忽略鼠标，会在地面上吞掉点格与中键转视角（无限领主 HUD 那条复盘）。
    /// </summary>
    private static Label MakeFloatingLabel(string name, int fontSize)
    {
        var label = new Label { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    /// <summary>容器里的行：宽高交给容器，只管字号与换行。</summary>
    private static Label MakeRowLabel(string text, int fontSize)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }
}
