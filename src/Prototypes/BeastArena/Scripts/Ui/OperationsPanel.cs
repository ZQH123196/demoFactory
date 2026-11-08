#nullable enable
using System;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 「经营」入口 —— 市场、账本与工资、人员与帮工三块并成一个弹层，内部按组分栏。
///
/// <para><b>三页都是现成面板改的</b>（<see cref="MarketPanel"/> / <see cref="LedgerPanel"/> /
/// <see cref="HelperPopover"/> 的 <c>embedded</c> 那一档），不复制它们的控件与写路径：
/// 面板仍然是各自那一份代码，只是不再自己铺满视口、不再自己置模态。</para>
///
/// <para><b>不暂停世界</b>（spec「标题与暂停」）：开这一层只压住拾取，时间照走 ——
/// 与原来的市场 / 账本 / 帮工三颗按钮同一档。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清；Esc 由
/// <see cref="UiRoot.TryCloseTopModal"/> 按打开次序逐层收。</para>
/// </summary>
public partial class OperationsPanel : Control, IModalCloseable
{
    /// <summary>弹层宽度（像素）。</summary>
    public const float PanelWidth = 660f;

    /// <summary>弹层高度（像素）。</summary>
    public const float PanelHeight = 660f;

    private readonly Func<ArenaWorld?> _world;
    private readonly UiRoot _ui;

    /// <summary>市场那一页（嵌入档；测试与调试桥按这一份读）。</summary>
    public MarketPanel Market { get; }

    /// <summary>账本与工资那一页。</summary>
    public LedgerPanel Ledger { get; }

    /// <summary>人员与帮工那一页。</summary>
    public HelperPopover Helpers { get; }

    /// <summary>三页的分栏容器。</summary>
    public TabContainer Tabs { get; }

    public OperationsPanel(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);

        _ui = ui;
        _world = world;

        Name = "OperationsPanel";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
        GuiInput += OnPanelInput;

        var panel = new PanelContainer { Name = "Box" };
        panel.AnchorLeft = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorTop = 0.5f;
        panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -PanelWidth / 2f;
        panel.OffsetRight = PanelWidth / 2f;
        panel.OffsetTop = -PanelHeight / 2f;
        panel.OffsetBottom = PanelHeight / 2f;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.12f, 0.16f, 0.98f),
            BorderColor = new Color(0.45f, 0.50f, 0.62f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14f,
            ContentMarginRight = 14f,
            ContentMarginTop = 12f,
            ContentMarginBottom = 12f,
        });
        AddChild(panel);

        var rows = new VBoxContainer { Name = "Rows" };
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);

        rows.AddChild(new Label
        {
            Name = "Title",
            Text = Loc.T("经营"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        Tabs = new TabContainer
        {
            Name = "Tabs",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        rows.AddChild(Tabs);

        Market = new MarketPanel(ui, world, inject, embedded: true) { Name = "MarketPage" };
        Tabs.AddChild(Market);

        Ledger = new LedgerPanel(ui, world, inject, embedded: true) { Name = "LedgerPage" };
        Tabs.AddChild(Ledger);

        Helpers = new HelperPopover(ui, world, inject, embedded: true) { Name = "HelperPage" };
        Tabs.AddChild(Helpers);

        Tabs.SetTabTitle(0, Loc.T("市场"));
        Tabs.SetTabTitle(1, Loc.T("账本与工资"));
        Tabs.SetTabTitle(2, Loc.T("人员与帮工"));

        var close = new Button
        {
            Name = "Close",
            Text = Loc.T("关闭"),
            CustomMinimumSize = new Vector2(0f, 30f),
            FocusMode = FocusModeEnum.None,
        };
        close.Pressed += Close;
        rows.AddChild(close);

        ui.AddPopup(this);
    }

    /// <summary>弹层开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开经营（三页各自按世界现况刷新）。</summary>
    public void Open()
    {
        if (_world() is null) return;

        Visible = true;
        _ui.EnterModal(this);
    }

    /// <summary>关掉经营并摘掉自己那一份模态。</summary>
    public void Close()
    {
        Visible = false;
        _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (_world() is null) Close();
    }

    /// <summary>点框外的空白处收起（框内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
