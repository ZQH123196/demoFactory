#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.Persistence;
using Godot;

namespace DemoFactory.GugaFramework.FrontEnd;

/// <summary>
/// 一局结束时报上来的那一份 —— 胜负、判定那一步、要上屏的明细、要摆的按钮。
///
/// <para><b>四样都由原型递</b>：「赢/输/平」在三个原型里是三套不同的枚举，
/// 步号的单位与读法也各不相同（无限领主是经营局没有局内计时，排兵布阵按判定那一步），
/// 框架把它们并成一个中性形状已经要靠翻译，硬要再往里加字段就成了「那一个游戏」的形状。
/// 框架只保证形状统一（「表现层只读」+「原型隔离」）。</para>
/// </summary>
/// <param name="Title">结算屏标题：胜负那一句。</param>
/// <param name="Details">明细行，一行一句。</param>
/// <param name="Tick">判定那一步（宿主读自己世界的步号递进来）。</param>
/// <param name="Won">这一局赢没赢。平局按 false 递，另由标题说明。</param>
/// <param name="Actions">结算屏上的按钮（文字 + 点下去做什么）。</param>
public sealed record RunOutcome(
    string Title,
    IReadOnlyList<string> Details,
    long Tick,
    bool Won,
    IReadOnlyList<(string Text, Action OnPressed)> Actions);

/// <summary>
/// 前端层 —— 菜单栈、屏的搭法、输入模式的归属，全在这一件里。
///
/// <para><b>这一层是「浮在游戏之外」的那一层</b>（前端，见术语表）：菜单、设置、标题屏。
/// 它跟游戏本体没关系：不认识任何原型，不读任何逻辑世界的数据，只抛请求让原型自己接。</para>
///
/// <para><b>输入模式归这一层管</b>（「输入模式单源」）：<c>Input.MouseMode</c> 与控件焦点
/// 只在这里设。原型要开菜单就调 <see cref="Open"/>，不要自己去拧鼠标模式——
/// 四处各拧一次的结果是关掉 A 屏顺手把 B 屏的光标也收了，而那种故障在画面上
/// 只在「鼠标不见了」这一格里冒出来，源码里看不出来。</para>
///
/// <para><b>暂停不归这一层管</b>：「表现层只读」加「原型隔离」两条决定了，
/// 开暂停屏只抛 <see cref="PauseRequested"/>，冻不冻结时间是原型自己的世界说了算
/// （<c>_world.SetPaused</c>）。前端层要是一手冻结一手解冻，逻辑就多了一条
/// 绕过 <c>EventBus.GamePaused</c> 的旁路，订阅它的那一格就假了。</para>
///
/// <para><b>接入（原型侧五行）</b>：
/// <code>
/// var front = new FrontEndLayer();
/// AddChild(front);
/// front.PauseRequested += paused =&gt; _world.SetPaused(paused);   // 冻不冻由你答
/// front.QuitRequested  += () =&gt; GetTree().Quit();
/// front.SetTitleEntries(("开始新局", () =&gt; StartRun()));        // 条目由原型递
/// foreach (var (v, h) in FrontEndVerbs.Vocabulary(front)) _bridge?.Register(v, h);
/// </code></para>
///
/// <para><b>局开始与局结束也归这一层抛</b>：原型在自己那一局装配好之后调
/// <see cref="NotifyRunStarted"/>，在判定落地那一刻调 <see cref="NotifyRunEnded"/>。
/// 结算屏由本层画（<see cref="SetResult"/> 接标题、明细与按钮），原型只递文字与动作 ——
/// 三处原型原来各画一套结算 UI，就是这一格没被填过。</para>
///
/// <para><b>结算屏是暂停屏的替身</b>：开它走 <see cref="MenuStack.PushExclusive"/>，
/// 栈里只剩它（见那一件的类注：为什么不能叠在暂停屏上面）。</para>
///
/// <para><b>键位一律走 InputMap action</b>（「输入走 InputMap」）：<c>frontend_menu</c> 与
/// <c>frontend_back</c> 由本类兜底建（已存在则不覆盖，项目设置里手写的绑定优先）。
/// 裸键轮询注入不进调试通道，菜单就不可实测。</para>
///
/// <para><b>文字一律 <c>Loc.T</c></b>：键就是中文原文，查不到就把中文本身显示出来，不会白屏。
/// 框架这几个字归框架那张表（<c>i18n/frontend.csv</c>），不塞进任何原型的表
/// （原型隔离：那张表不隶属任何原型，四个原型共用同一批字）。</para>
/// </summary>
public partial class FrontEndLayer : CanvasLayer
{
    // === 键位（GugaFramework 公共口径，与相机层那一份同一形状） ===

    /// <summary>开/关菜单。Esc 绑在这里。</summary>
    public const string ActionMenu = "frontend_menu";

    /// <summary>退一层。默认也绑 Esc，两条路都收。</summary>
    public const string ActionBack = "frontend_back";

    private readonly MenuStack _stack = new();
    private readonly FrontEndSettings _settings = new();

    private Control _root = null!;
    private ColorRect _dim = null!;
    private CenterContainer _screenHost = null!;
    private VBoxContainer _titleBox = null!;
    private VBoxContainer _pauseBox = null!;

    // 暂停屏上原型递进来的那一段条目（SetPauseEntries）。单独一个盒子：
    // 框架自己的三颗（继续 / 设置 / 退出游戏）与原型那段分开建、分开清，
    // 重调 SetPauseEntries 只清这一格，不动框架的三颗。
    private VBoxContainer _pauseExtras = null!;
    private VBoxContainer _settingsBox = null!;
    private Label _readoutLabel = null!;
    private HSlider _volumeSlider = null!;

    // 设置屏那四个控件留成字段：读档之后要自己往它们身上写值（见 RefillFromSettings）。
    private OptionButton _localeOption = null!;
    private OptionButton _modeOption = null!;
    private OptionButton _resolutionOption = null!;

    // 存档区（原型递清单与两个动作，见 BindSaveSlots；不接就整区隐藏）。
    private VBoxContainer _saveSection = null!;
    private Label _slotLabel = null!;
    private Label _slotWhenLabel = null!;
    private Label _slotBytesLabel = null!;
    private Button _saveButton = null!;
    private Button _loadButton = null!;
    private Func<IReadOnlyList<SaveSlotInfo>>? _slotsOf;
    private Action<int>? _onWriteSlot;
    private Action<int>? _onReadSlot;

    private readonly Dictionary<FrontEndScreen, Control> _panels = [];

    // 结算屏那几行：原型递进来的明细一行一个标签。留成字段是为了换一局能整块清掉重填。
    private VBoxContainer _resultBox = null!;
    private Label _resultTitle = null!;
    private VBoxContainer _resultLines = null!;
    private VBoxContainer _resultButtons = null!;

    // 关菜单时要把鼠标模式放回开菜单之前的那一档。存「进来之前」而不是「关掉之后一律 Visible」：
    // 有的原型开局就把鼠标锁在游戏里（俯视角相机那类），一律置 Visible 会把它的锁定解开。
    private Input.MouseModeEnum _mouseModeBeforeOpen = Input.MouseModeEnum.Visible;

    // 已经落到引擎上的那一份。设置只在变了的当帧落一次——每帧调 DisplayServer 改窗口
    // 是拿系统调用喂帧率，滑条拖动时尤其明显。
    private string _appliedLocale = "";
    private double _appliedVolume = -1.0;
    private FrontEndWindowMode _appliedMode = (FrontEndWindowMode)(-1);
    private FrontEndResolution _appliedResolution = new(0, 0);

    /// <summary>
    /// Esc 是否关闭标题屏。<b>缺省 true = 现行行为，另两个原型一个字都不变</b>。
    ///
    /// <para>启动即停在标题屏上的原型（巨兽竞技场）把它设成 false：
    /// 那时栈里只有标题屏，误按 Esc 一下就把「开始游戏」关掉，玩家会看见一块空场。
    /// 设置屏从标题屏叠上来时栈顶是设置屏，Esc 仍然退得回去 —— 这一格只挡标题屏本身。</para>
    /// </summary>
    [Export] public bool DismissTitleOnEsc { get; set; } = true;

    /// <summary>菜单栈（纯 C# 那一半，可离线测）。</summary>
    public MenuStack Stack => _stack;

    /// <summary>设置项的值（纯 C# 那一半，可离线测）。</summary>
    public FrontEndSettings Settings => _settings;

    /// <summary>现在开着哪一屏。</summary>
    public FrontEndScreen Current => _stack.Top;

    /// <summary>菜单是否开着（栈非空）。</summary>
    public bool IsOpen => _stack.Depth > 0;

    /// <summary>请求冻结/恢复时间 —— 冻不冻由原型答（「表现层只读」）。</summary>
    public event Action<bool>? PauseRequested;

    /// <summary>请求退出游戏。</summary>
    public event Action? QuitRequested;

    /// <summary>一局开始（原型答：宿主在自己那一局装配好之后抛）。</summary>
    public event Action? RunStarted;

    /// <summary>一局结束，带胜负与那一步。</summary>
    public event Action<RunOutcome>? RunEnded;

    /// <summary>
    /// Esc 的第一顺位拦截 —— 游戏自己的弹层（战报 / 巨兽面板 / 经营…）比暂停菜单更靠上。
    ///
    /// <para><b>只有原型接得上</b>：框架不认识原型那些弹层。原型把「先关自己最上面那层」递进来，
    /// 返回 true 表示这一发 Esc 已经被吃掉、不要再开暂停屏；返回 false 才轮到菜单栈。
    /// 空栈与非空栈都先问它 —— 标题屏上开着的操作说明也要能按 Esc 收起。</para>
    /// </summary>
    public Func<bool>? GameEscapeInterceptor { get; set; }

    /// <summary>
    /// 报「这一局结束了」—— 原型在逻辑世界判定落地那一刻调这一发。
    ///
    /// <para><b>抛在这一层而不是逻辑世界</b>：「表现层只读」+ 原型隔离两条决定了，
    /// 逻辑世界不认识菜单，菜单也不该认识任何一个世界。中间这一发由原型接，
    /// 胜负、时刻与那几行明细都由原型递进来（框架不知道「BOSS 巢穴」是什么）。</para>
    ///
    /// <para><b>次序：先抛事件，再开结算屏</b>。订阅方在事件里冻时间轴；
    /// 开屏那一下可能顺带发一次「解除暂停」（栈里原先有暂停屏时），
    /// 冻在后面才不会被那一发解掉。</para>
    /// </summary>
    /// <param name="outcome">胜负、判定那一步与要上屏的几行明细。</param>
    public void NotifyRunEnded(RunOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        RunEnded?.Invoke(outcome);
        SetResult(outcome.Title, outcome.Details, outcome.Actions);
        Open(FrontEndScreen.Result);
    }

    /// <summary>报「一局开始了」—— 原型在自己那一局装配好之后调这一发。</summary>
    public void NotifyRunStarted() => RunStarted?.Invoke();

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = "FrontEnd";
        // 菜单浮在游戏之上：这一层号要比原型自己的 UI 层高，不然会被压住。
        Layer = 100;

        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // 压暗那一层：菜单是模态的，底下得暗一档才读得出「游戏被挡住了」。
        // 没有它，暂停屏就是几行浮在画面上的字，看不出挡住了什么——
        // 而「挡住了没有」正是这一层要验的事。
        _dim = new ColorRect
        {
            Name = "Dim",
            Color = new Color(0f, 0f, 0f, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_dim);

        _screenHost = new CenterContainer { Name = "ScreenHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        _screenHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_screenHost);

        _titleBox = BuildScreen("Title", "开始菜单");
        _pauseBox = BuildScreen("Pause", "暂停");
        _settingsBox = BuildScreen("Settings", "设置");
        _resultBox = BuildScreen("Result", "结算");
        _panels[FrontEndScreen.Title] = _titleBox;
        _panels[FrontEndScreen.Pause] = _pauseBox;
        _panels[FrontEndScreen.Settings] = _settingsBox;
        _panels[FrontEndScreen.Result] = _resultBox;

        BuildTitlePlaceholder();
        BuildPauseEntries();
        BuildSettingsEntries();
        BuildResultPlaceholder();

        _readoutLabel = new Label { Name = "Readout", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_readoutLabel);

        _root.Visible = false;
        EnsureInputMap();
        GetViewport().SizeChanged += Arrange;
        Arrange();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(ActionMenu) || Input.IsActionJustPressed(ActionBack))
        {
            // 游戏自己的弹层先收（战报 / 面板 / 经营…）：它们画在菜单之上，Esc 的第一层就是它们。
            // 拦下这一发就不再动菜单栈 —— 否则会「关掉弹层的同时开出暂停屏」。
            if (GameEscapeInterceptor?.Invoke() == true) return;

            // Esc 在空栈上开暂停屏，在有栈的栈顶上关掉最上面那屏。
            // 两种语义分在同一个动作上，靠栈深区分——分成两个动作的话
            // 「Esc 开关暂停」和「Esc 退设置」会互相抢同一发键。
            if (_stack.Depth == 0) Open(FrontEndScreen.Pause);
            else if (_stack.Top != FrontEndScreen.Title || DismissTitleOnEsc) Close();
        }

        ApplySettingsToEngine();
        if (_root.Visible) _readoutLabel.Text = DebugReadout();
    }

    // === 对外控制口（玩家与调试注入都走这里） ===

    /// <summary>打开一屏。重复开同一屏不叠层。返回是否真的开了。</summary>
    public bool Open(FrontEndScreen screen)
    {
        // 结算屏是暂停屏的替身，不是叠在它上面的一层：栈里只剩它。
        // 栈里原先有暂停屏时顺手把「解除暂停」发出去 —— 那屏已经不在了，
        // 留着暂停标记的话世界会冻在一个玩家看不见的暂停上。
        if (screen == FrontEndScreen.Result)
        {
            // 「原先有没有暂停屏」要在清栈**之前**问：清完就只剩结算屏了。
            var wasPause = _stack.Contains(FrontEndScreen.Pause);
            if (!_stack.PushExclusive(screen)) return false;

            if (_stack.Depth == 1) RememberMouseMode();
            if (wasPause) PauseRequested?.Invoke(false);

            ApplyStack();
            return true;
        }

        if (!_stack.Push(screen)) return false;

        if (_stack.Depth == 1) RememberMouseMode();
        if (screen == FrontEndScreen.Pause) PauseRequested?.Invoke(true);

        ApplyStack();
        return true;
    }

    /// <summary>
    /// 关掉最上面一屏。返回关完之后栈顶是哪一屏。
    ///
    /// <para><b>退到空了就归还输入模式</b>：栈非空时鼠标保持可见，玩家要能点按钮；
    /// 空了就放回开菜单之前那一档。</para>
    /// </summary>
    public FrontEndScreen Close()
    {
        var wasPause = _stack.Top == FrontEndScreen.Pause;
        var top = _stack.Pop();
        if (wasPause) PauseRequested?.Invoke(false);

        ApplyStack();
        if (_stack.Depth == 0) Input.MouseMode = _mouseModeBeforeOpen;
        return top;
    }

    /// <summary>全关（退到标题屏或直接退出游戏时用）。</summary>
    public void CloseAll()
    {
        var wasPause = _stack.Contains(FrontEndScreen.Pause);
        _stack.Clear();
        if (wasPause) PauseRequested?.Invoke(false);

        ApplyStack();
        Input.MouseMode = _mouseModeBeforeOpen;
    }

    /// <summary>
    /// 设置标题屏的条目 —— 条目文字与点下去做什么，都由原型递进来。
    ///
    /// <para>框架不知道「开始」要跳去哪：demoFactory 里没有「那一个游戏」，
    /// 那条线归原型（原型隔离）。框架只保证条目的形状与取字。</para>
    /// </summary>
    public void SetTitleEntries(params (string Text, Action OnPressed)[] entries)
    {
        ClearBox(_titleBox);
        foreach (var (text, onPressed) in entries)
            _titleBox.AddChild(MakeButton(text, onPressed));

        _titleBox.AddChild(MakeButton(Loc.T("设置"), () => Open(FrontEndScreen.Settings)));
    }

    /// <summary>
    /// 设置暂停屏上由原型递进来的那一段条目 —— 文字与点下去做什么都由原型递进来。
    ///
    /// <para><b>框架的三颗不动</b>：「继续」「设置」「退出游戏」始终由框架摆好，
    /// 原型条目落在「继续」之后、「设置」之前。不接这一发的原型还是今天那三颗。</para>
    ///
    /// <para><b>整段重建，重复调幂等</b>：每次调用先清空那一段再填，按装配次序只调一次即可。</para>
    ///
    /// <para>框架不认识「操作说明」「重开」「回标题」：这些语义归原型（原型隔离），
    /// 这一发只收「文字 + 动作」的形状。</para>
    /// </summary>
    public void SetPauseEntries(params (string Text, Action OnPressed)[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        ClearBox(_pauseExtras);
        foreach (var (text, onPressed) in entries)
            _pauseExtras.AddChild(MakeButton(text, onPressed));

        _pauseExtras.Visible = _pauseExtras.GetChildCount() > 0;
    }

    /// <summary>
    /// 设置结算屏的内容 —— 标题、几行明细、几个按钮，文字与动作都由原型递进来。
    ///
    /// <para><b>本层只摆版式</b>：框架不知道「通关」「守住了」是什么意思，
    /// 也不知道「再来一局」在某个原型那边是换世界还是换棋盘（「原型隔离」）。
    /// 三处原型原来各画一套结算 UI，这一屏把它们并成一份。</para>
    ///
    /// <para><b>末尾自动补一个「退出游戏」</b>：结算屏是模态的，栈里只剩它，
    /// 玩家如果把原型递进来的按钮都做成「不能退出」，就再也没有路离开这一局。
    /// 补这一颗不是替原型做主，是不留死路。</para>
    /// </summary>
    /// <param name="title">结算屏标题（胜负那一句）。</param>
    /// <param name="details">明细行。一行一句，原样上屏。</param>
    /// <param name="actions">按钮（文字 + 点下去做什么）。</param>
    public void SetResult(string title, IReadOnlyList<string> details,
        IReadOnlyList<(string Text, Action OnPressed)> actions)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(actions);

        _resultTitle.Text = title;

        ClearBox(_resultLines);
        foreach (var line in details)
            _resultLines.AddChild(new Label { Text = line, MouseFilter = Control.MouseFilterEnum.Ignore });

        ClearBox(_resultButtons);
        foreach (var (text, onPressed) in actions)
            _resultButtons.AddChild(MakeButton(text, onPressed));

        _resultButtons.AddChild(MakeButton(Loc.T("退出游戏"), RequestQuit));
    }

    /// <summary>
    /// 请求退出游戏。
    ///
    /// <para><b>为什么只抛请求而不自己 <c>GetTree().Quit()</c></b>：退出时机由原型答
    /// （要不要先存盘、先落一条日志）。前端层擅自退会把原型的收尾动作吞掉。</para>
    /// </summary>
    public void RequestQuit() => QuitRequested?.Invoke();

    /// <summary>
    /// 读数 —— 调试桥的 <c>ui_read</c> 靠它。
    ///
    /// <para>栈 + 设置 + 输入模式三项一起念：菜单卡住时最容易分不清是
    /// 「栈没弹对」还是「鼠标模式没收回去」，分开念要发两发才看得出关系。</para>
    /// </summary>
    public string DebugReadout()
        => $"{_stack.Readout()} | {_settings.Readout()} | mouse={Input.MouseMode}"
           + (_slotsOf is null ? string.Empty : $" | 存档 {SlotReadout()}");

    /// <summary>存档区那一格的读数（调试桥的 <c>ui_read</c> 靠它；没接存档区是空串）。</summary>
    private string SlotReadout()
    {
        var slots = _slotsOf?.Invoke() ?? [];
        return slots.Count == 0 ? "未接" : string.Join(" ", slots.Select(s => s.Readout()));
    }

    // === 搭屏 ===

    private VBoxContainer BuildScreen(string name, string title)
    {
        var panel = new PanelContainer
        {
            Name = name + "Panel",
            Visible = false,
            // 不给最小宽度的话，按钮按文字自适应、面板窄到读起来像散字；
            // 设置屏那一列下拉与滑条也会各按各的宽度排，标签和控件对不齐。
            CustomMinimumSize = new Vector2(420f, 0f),
        };
        // 显式给一层不透明底。缺省主题的 PanelContainer 是半透明的，压在压暗层上之后
        // 整块面板与背景糊成一片，菜单读起来像几行浮字（「暂停」那一格尤其明显，出图实测 2026-10-06）。
        // 一块实心底 + 底下的压暗层 = 「这是浮在游戏之上的一屏」这个读法才立得住。
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.13f, 0.14f, 0.18f, 0.98f),
            BorderColor = new Color(0.45f, 0.5f, 0.62f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 20f,
            ContentMarginRight = 20f,
            ContentMarginTop = 16f,
            ContentMarginBottom = 16f,
        });
        _screenHost.AddChild(panel);

        var box = new VBoxContainer { Name = name };
        box.AddChild(new Label { Text = Loc.T(title) });
        panel.AddChild(box);
        return box;
    }

    private void BuildTitlePlaceholder()
    {
        // 没递条目前这一屏是空的，只留一行说明——不假装有游戏。
        _titleBox.AddChild(new Label { Text = Loc.T("（本屏条目由原型递进来）") });
    }

    /// <summary>
    /// 结算屏的骨架：标题、明细那几行、按钮那几行，三块分开建、分开清。
    ///
    /// <para>分成三块是因为三块的重填频率不同：明细每局换，标题每局换，
    /// 按钮由原型递（<see cref="SetResult"/> 里整块重建）。</para>
    /// </summary>
    private void BuildResultPlaceholder()
    {
        _resultTitle = new Label { Name = "ResultTitle", MouseFilter = Control.MouseFilterEnum.Ignore };
        _resultBox.AddChild(_resultTitle);

        _resultLines = new VBoxContainer { Name = "ResultLines", MouseFilter = Control.MouseFilterEnum.Ignore };
        _resultBox.AddChild(_resultLines);

        _resultLines.AddChild(new Label { Text = Loc.T("（这一屏的内容由原型递进来）"), MouseFilter = Control.MouseFilterEnum.Ignore });

        _resultButtons = new VBoxContainer { Name = "ResultButtons" };
        _resultBox.AddChild(_resultButtons);
    }

    private void BuildPauseEntries()
    {
        _pauseBox.AddChild(MakeButton(Loc.T("继续"), () => CloseAll()));

        // 原型条目落在「继续」之后、「设置」之前；没递条目时这一格收起（不占分隔位）。
        _pauseExtras = new VBoxContainer { Name = "PauseExtras", Visible = false };
        _pauseBox.AddChild(_pauseExtras);

        _pauseBox.AddChild(MakeButton(Loc.T("设置"), () => Open(FrontEndScreen.Settings)));
        _pauseBox.AddChild(MakeButton(Loc.T("退出游戏"), RequestQuit));
    }

    private void BuildSettingsEntries()
    {
        _settingsBox.AddChild(new Label { Text = Loc.T("语言") });
        _localeOption = new OptionButton { Name = "LocaleOption" };
        foreach (var tag in FrontEndSettings.LocaleTags)
            _localeOption.AddItem(LocaleDisplay(tag), (int)tag[0]);
        _localeOption.Selected = 0;
        _localeOption.ItemSelected += i => _settings.SetLocale(FrontEndSettings.LocaleTags[i]);
        _settingsBox.AddChild(_localeOption);

        _settingsBox.AddChild(new Label { Text = Loc.T("主音量") });
        _volumeSlider = new HSlider
        {
            Name = "VolumeSlider",
            MinValue = FrontEndSettings.MinMasterVolume,
            MaxValue = FrontEndSettings.MaxMasterVolume,
            Step = 0.05,
            Value = (float)_settings.MasterVolume,
            CustomMinimumSize = new Vector2(320f, 0f),
        };
        _volumeSlider.ValueChanged += v => _settings.SetMasterVolume(v);
        _settingsBox.AddChild(_volumeSlider);

        _settingsBox.AddChild(new Label { Text = Loc.T("窗口模式") });
        _modeOption = new OptionButton { Name = "ModeOption" };
        foreach (var mode in Enum.GetValues<FrontEndWindowMode>())
            _modeOption.AddItem(Loc.T(WindowModeText(mode)), (int)mode);
        _modeOption.Selected = 0;
        _modeOption.ItemSelected += i => _settings.SetWindowMode((FrontEndWindowMode)i);
        _settingsBox.AddChild(_modeOption);

        _settingsBox.AddChild(new Label { Text = Loc.T("分辨率") });
        _resolutionOption = new OptionButton { Name = "ResolutionOption" };
        foreach (var res in FrontEndSettings.Resolutions)
            _resolutionOption.AddItem(res.ToString(), res.Width);
        _resolutionOption.Selected = 2;
        _resolutionOption.ItemSelected += i =>
        {
            foreach (var r in FrontEndSettings.Resolutions)
                if (r.Width == i)
                {
                    _settings.SetResolution(r);
                    break;
                }
        };
        _settingsBox.AddChild(_resolutionOption);

        // 存档区在四项之后另起一段（一条分隔 + 一个盒子）。既有的四个控件位置一个字都不动 ——
        // 套分组会让「落地配出图」要重新核对一次排版，而它们现在是对的。
        BuildSaveSection();

        _settingsBox.AddChild(MakeButton(Loc.T("返回"), () => Close()));
    }

    // === 存档区（条目与读数由存档模块递进来，这一层只负责画） ===

    /// <summary>
    /// 存档区那一格：<b>一条</b>槽位条目，不是列表。
    ///
    /// <para>摆四样：槽号（固定「槽位 1」）、存盘时刻（没档显示「空」）、
    /// 文件字节数（没有就不显示）、两个按钮。<b>不做列表、不做缩略图</b> ——
    /// 一个槽位不需要列表，第一版也没有内容可截。</para>
    ///
    /// <para><b>未接线时整区隐藏</b>（spec「面板」）：不接 <see cref="BindSaveSlots"/> 的原型
    /// 没有存档可存，摆着两颗按不动的按钮只会让玩家以为坏了。接上那一发再整区显出来。</para>
    ///
    /// <para><b>这一层不认识存档的文件格式</b>：槽位清单与两个动作都由原型经
    /// <see cref="BindSaveSlots"/> 递进来。存档区画完就退，存档模块不认识 UI 控件。</para>
    /// </summary>
    private void BuildSaveSection()
    {
        _saveSection = new VBoxContainer { Name = "SaveSection", Visible = _slotsOf is not null };
        _settingsBox.AddChild(_saveSection);

        _saveSection.AddChild(new HSeparator());
        _saveSection.AddChild(new Label { Text = Loc.T("存档") });

        _slotLabel = new Label { Name = "SlotLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
        _saveSection.AddChild(_slotLabel);

        _slotWhenLabel = new Label { Name = "SlotWhenLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
        _saveSection.AddChild(_slotWhenLabel);

        _slotBytesLabel = new Label { Name = "SlotBytesLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
        _saveSection.AddChild(_slotBytesLabel);

        var buttons = new HBoxContainer();
        _saveButton = MakeButton(Loc.T("保存"), () => RunSaveSlot(1));
        _saveButton.Name = "SaveButton";
        buttons.AddChild(_saveButton);
        _loadButton = MakeButton(Loc.T("读取"), () => RunLoadSlot(1));
        _loadButton.Name = "LoadButton";
        buttons.AddChild(_loadButton);
        _saveSection.AddChild(buttons);

        RefreshSaveSlots();
    }

    /// <summary>
    /// 存档区接上：<b>槽位清单从哪来、写档与读档各做什么</b>，都由原型递进来。
    ///
    /// <para>清单是<b>取值的函数</b>而不是一份快照：写完档那一发要立刻念出新的一份，
    /// 递一份数组进来的话那一格得自己记得刷新。</para>
    ///
    /// <para>不接就那一格是「槽位 1 / 空」且两个按钮不做事 —— 场子（UI 试验场）没存档时正是这一档，
    /// 而一个不接也能画出来的存档区才验得出排版对不对。</para>
    /// </summary>
    /// <param name="slotsOf">念槽位清单（存档模块那一半产，前端层只画）。</param>
    /// <param name="onWriteSlot">点「保存」做什么（原型答：冻时间 → 写盘 → 解冻）。</param>
    /// <param name="onReadSlot">点「读取」做什么（原型答：换世界那一整套次序）。</param>
    public void BindSaveSlots(Func<IReadOnlyList<SaveSlotInfo>> slotsOf,
        Action<int> onWriteSlot, Action<int> onReadSlot)
    {
        _slotsOf = slotsOf ?? throw new ArgumentNullException(nameof(slotsOf));
        _onWriteSlot = onWriteSlot ?? throw new ArgumentNullException(nameof(onWriteSlot));
        _onReadSlot = onReadSlot ?? throw new ArgumentNullException(nameof(onReadSlot));

        if (_saveSection is not null) _saveSection.Visible = true;
        RefreshSaveSlots();
    }

    /// <summary>把槽位清单念到那一格的三个标签上。没接存档区就什么都不做。</summary>
    public void RefreshSaveSlots()
    {
        if (_slotsOf is null || _slotLabel is null) return;

        var slots = _slotsOf();
        if (slots.Count == 0)
        {
            _slotLabel.Text = Loc.T("槽位 1");
            _slotWhenLabel.Text = Loc.T("空");
            _slotBytesLabel.Visible = false;
            _saveButton.Disabled = false;
            _loadButton.Disabled = true;
            return;
        }

        var slot = slots[0];
        // 槽号第一版固定显示「槽位 1」：写成字面量而不是插值出来的，
        // 插值出来的键 i18n 那张表按字面量扫不到（「表里有、源码里没有」当场判红）。
        _slotLabel.Text = Loc.T("槽位 1");
        _slotWhenLabel.Text = slot.Exists ? slot.WhenText : Loc.T("空");
        _slotBytesLabel.Text = string.Create(CultureInfo.InvariantCulture,
            $"{slot.Bytes / 1024.0:F1} KB");
        _slotBytesLabel.Visible = true;
        _saveButton.Disabled = false;
        _loadButton.Disabled = !slot.Exists;
    }

    private void RunSaveSlot(int slot)
    {
        if (_onWriteSlot is null) return;

        try
        {
            _onWriteSlot(slot);
        }
        finally
        {
            // 写档失败（超上限、目录不可写）也要把读数刷新：屏幕上留着一份「旧时刻」，
            // 玩家会以为刚才那一下存上了。
            RefreshSaveSlots();
        }
    }

    private void RunLoadSlot(int slot)
    {
        if (_onReadSlot is null) return;

        try
        {
            _onReadSlot(slot);
        }
        finally
        {
            RefreshSaveSlots();
        }
    }

    /// <summary>
    /// 把设置值<b>写回控件</b>，并重置那四个「已落到引擎上」的字段。
    ///
    /// <para><b>读档填回 <see cref="FrontEndSettings"/> 之后 UI 不会自动跟着走</b>：
    /// 四个控件的初值是构造时写死的（语言 0、分辨率 2），而这些屏的控件一次建好、永不重建。
    /// 少了这一格，屏幕上还是旧读数。</para>
    ///
    /// <para><b>为什么还要重置那四个字段</b>：<c>ApplySettingsToEngine</c> 每帧调，靠它们去重
    /// 「只落变了的」。不重置的话引擎那一侧认为「没变」而跳过落地——<b>读数对、画面不对</b>，
    /// 而那种错在源码里看不出来。</para>
    ///
    /// <para>音量那条另有一处非线性：<c>LinearToDb</c> 在 <c>linear &lt;= 0.0001</c> 时直接给
    /// <c>-80.0</c>，不是线性往返——存 0 与存 0.0001 读回来听感一样，但两个值本身各自往返得住。</para>
    /// </summary>
    public void RefillFromSettings()
    {
        if (_localeOption is null) return;

        var localeIndex = Array.IndexOf(FrontEndSettings.LocaleTags, _settings.Locale);
        if (localeIndex >= 0) _localeOption.Selected = localeIndex;

        _volumeSlider.Value = (float)_settings.MasterVolume;

        _modeOption.Selected = (int)_settings.WindowMode;

        foreach (var r in FrontEndSettings.Resolutions)
        {
            if (r == _settings.Resolution)
            {
                _resolutionOption.Selected = r.Width;
                break;
            }
        }

        _appliedLocale = "";
        _appliedVolume = -1.0;
        _appliedMode = (FrontEndWindowMode)(-1);
        _appliedResolution = new(0, 0);
    }

    // 语言名本身也要能翻：下拉里那三行是上屏文字，走 Loc 才进得了翻译表。
    private static string LocaleDisplay(string tag) => tag switch
    {
        "zh" => Loc.T("中文"),
        "en" => Loc.T("English"),
        "ja" => Loc.T("日本語"),
        _ => tag,
    };

    private static string WindowModeText(FrontEndWindowMode mode) => mode switch
    {
        FrontEndWindowMode.Windowed => "窗口化",
        FrontEndWindowMode.Borderless => "无边框",
        _ => "独占全屏",
    };

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += onPressed;
        return button;
    }

    private static void ClearBox(Node box)
    {
        foreach (var child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    // === 内部 ===

    private void RememberMouseMode()
    {
        // 只在「从空栈进第一屏」时记一次：连开两屏不该把已经设成 Visible 的那一档
        // 记成「进来之前的那一档」，那样退出时会把原型开局那个锁定解开。
        _mouseModeBeforeOpen = Input.MouseMode;
    }

    private void ApplyStack()
    {
        foreach (var (screen, panel) in _panels)
            panel.GetParent<Control>().Visible = screen == _stack.Top;

        // 设置屏每次被翻出来都重念一遍槽位：别处写完档之后那一格得自己跟上，
        // 而「玩家什么时候看那一格」与「档什么时候变的」没有任何关系。
        if (_stack.Top == FrontEndScreen.Settings) RefreshSaveSlots();

        _root.Visible = _stack.Depth > 0;
        // 菜单开着就把鼠标放出来：玩家要点按钮。反之不在这里设——
        // 关屏时的模式在 Close/CloseAll 里归还成进来之前那一档。
        if (_stack.Depth > 0) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>
    /// 把设置值落到引擎上。纯表现层动作，不碰任何逻辑世界。
    ///
    /// <para><b>只落变了的那些</b>：四个字段各记一份已落值，相同就跳过。
    /// 每帧无条件调 <c>DisplayServer.WindowSetMode</c> 是拿系统调用喂帧率。</para>
    /// </summary>
    private void ApplySettingsToEngine()
    {
        if (_settings.Locale != _appliedLocale)
        {
            _appliedLocale = _settings.Locale;
            if (_appliedLocale == "system") TranslationServer.SetLocale(OS.Singleton.GetLocaleLanguage());
            else TranslationServer.SetLocale(_appliedLocale);
        }

        if (Math.Abs(_settings.MasterVolume - _appliedVolume) > double.Epsilon)
        {
            _appliedVolume = _settings.MasterVolume;
            var bus = AudioServer.GetBusIndex("Master");
            if (bus >= 0) AudioServer.SetBusVolumeDb(bus, (float)LinearToDb(_appliedVolume));
        }

        if (_settings.WindowMode != _appliedMode)
        {
            _appliedMode = _settings.WindowMode;
            DisplayServer.WindowSetMode(_appliedMode switch
            {
                FrontEndWindowMode.Windowed => DisplayServer.WindowMode.Windowed,
                FrontEndWindowMode.Borderless => DisplayServer.WindowMode.Fullscreen,
                _ => DisplayServer.WindowMode.ExclusiveFullscreen,
            });
        }

        if (_settings.Resolution != _appliedResolution)
        {
            _appliedResolution = _settings.Resolution;
            // 只在窗口化下改尺寸：全屏下改窗口尺寸会被系统按屏幕来定，
            // 传进去的数字不生效还容易被误读成「设置没落上」。
            if (_settings.WindowMode == FrontEndWindowMode.Windowed)
                DisplayServer.WindowSetSize(new Vector2I(_appliedResolution.Width, _appliedResolution.Height));
        }
    }

    /// <summary>按视口尺寸给读数那一行定位（不跟锚点预设较劲，理由同 <c>BattleHud.Arrange</c>）。</summary>
    private void Arrange()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _readoutLabel.Position = new Vector2(12f, size.Y - 28f);
        _readoutLabel.Size = new Vector2(size.X - 24f, 24f);
    }

    private static double LinearToDb(double linear)
        => linear <= 0.0001 ? -80.0 : Math.Log10(linear) * 20.0;

    /// <summary>
    /// 建 action（已存在则原样保留 —— 项目设置里手写的绑定优先，代码只兜底）。
    /// 两条都绑 Esc：菜单键与退格键在桌面端是同一发键，分开绑反而要玩家记两遍。
    /// </summary>
    private static void EnsureInputMap()
    {
        AddKeyAction(ActionMenu, Key.Escape);
        AddKeyAction(ActionBack, Key.Escape);
    }

    private static void AddKeyAction(StringName action, params Godot.Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
