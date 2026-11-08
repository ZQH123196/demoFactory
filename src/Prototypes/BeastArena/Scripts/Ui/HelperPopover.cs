#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 帮工弹层 —— 命令栏「帮工」开它。三个区：法力占用、帮工名单、召唤与遣散。
///
/// <para><b>「召唤」的置灰判据只有一处</b>：<see cref="ArenaSetup.CanSummonHelper"/>
/// （停靠期或交战期才召得下）—— 动词 <c>SummonHelper</c> 与这颗按钮读的是同一个口，
/// 两处各判一遍的话，按钮能点而动词拒、或者按钮灰着而动词收得到。</para>
///
/// <para><b>写路径只有一条</b>：召唤发 <c>SummonHelper</c>（无参数），
/// 遣散发 <c>DismissHelper|帮工 id</c>，都走 <see cref="Main.Inject"/>。
/// 弹层自己不摸世界字段 —— 名单与法力每帧现读世界。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清。</para>
/// </summary>
public partial class HelperPopover : Control, IModalCloseable
{
    /// <summary>弹层宽度（像素）。</summary>
    public const float PanelWidth = 360f;

    /// <summary>行间距（像素）。</summary>
    public const float RowGap = 5f;

    /// <summary>按钮高度（像素）。</summary>
    public const float ButtonHeight = 28f;

    /// <summary>面板内边距（像素）。</summary>
    public const float Padding = 10f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;
    private readonly bool _embedded;

    private readonly Label _mana;
    private readonly VBoxContainer _helperRows;
    private readonly Button _summon;

    /// <summary>名单上一次摆的是谁（变了才重摆那几行）。</summary>
    private string _shownIds = string.Empty;

    public HelperPopover(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject,
        bool embedded = false)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);

        _ui = ui;
        _world = world;
        _inject = inject;
        _embedded = embedded;

        Name = "HelperPopover";
        MouseFilter = MouseFilterEnum.Stop;

        if (embedded)
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            Visible = true;
        }
        else
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            Visible = false;
        }

        var box = new PanelContainer { Name = "Box" };
        if (embedded)
        {
            box.SetAnchorsPreset(LayoutPreset.FullRect);
        }
        else
        {
            box.AnchorLeft = 0.5f;
            box.AnchorRight = 0.5f;
            box.AnchorTop = 0.5f;
            box.AnchorBottom = 0.5f;
            box.OffsetLeft = -PanelWidth / 2f;
            box.OffsetRight = PanelWidth / 2f;
            box.OffsetTop = -160f;
            box.OffsetBottom = 160f;
        }
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.11f, 0.14f, 0.98f),
            BorderColor = new Color(0.45f, 0.50f, 0.62f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = Padding,
            ContentMarginRight = Padding,
            ContentMarginTop = Padding,
            ContentMarginBottom = Padding,
        });
        AddChild(box);

        var rows = new VBoxContainer { Name = "Rows" };
        rows.AddThemeConstantOverride("separation", (int)RowGap);
        box.AddChild(rows);

        rows.AddChild(new Label
        {
            Name = "Title",
            Text = Loc.T("帮工"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 法力占用：召唤一个帮工占一格，遣散一个还一格。
        _mana = new Label
        {
            Name = "Mana",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rows.AddChild(_mana);

        _helperRows = new VBoxContainer { Name = "HelperRows" };
        _helperRows.AddThemeConstantOverride("separation", (int)RowGap);
        rows.AddChild(_helperRows);

        _summon = MakeButton("Summon", Loc.T("召唤"));
        _summon.Pressed += OnSummonPressed;
        rows.AddChild(_summon);

        if (!embedded)
        {
            var close = MakeButton("Close", Loc.T("关闭"));
            close.Pressed += Close;
            rows.AddChild(close);
        }

        if (!embedded) ui.AddPopup(this);
    }

    /// <summary>弹层开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开帮工弹层（每次都按世界现况重算置灰与名单）。</summary>
    public void Open()
    {
        if (_world() is null) return;

        _shownIds = string.Empty;
        Refresh();
        Visible = true;
        if (!_embedded) _ui.EnterModal(this);
    }

    /// <summary>关掉帮工弹层并摘掉自己那一份模态（世界照常收拾取）。</summary>
    public void Close()
    {
        Visible = false;
        if (!_embedded) _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        Refresh();
    }

    // === 写路径 ===

    /// <summary>「召唤」：一发 <c>SummonHelper</c>（无参数；占 1 格法力）。</summary>
    private void OnSummonPressed()
    {
        _inject("SummonHelper", string.Empty);
        Refresh();
    }

    /// <summary>「遣散」：一发 <c>DismissHelper|帮工 id</c>（归还那一格法力）。</summary>
    private void OnDismissPressed(string helperId)
    {
        _inject("DismissHelper", helperId);
        Refresh();
    }

    // === 每帧派生 ===

    /// <summary>按世界现况重算法力占用、名单与那颗「召唤」的置灰。</summary>
    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        _mana.Text = Loc.Fmt("法力占用 {0} / {1} 格（召唤一个占 1 格，遣散一个还 1 格）",
            world.Town.HelperMana.ToString(CultureInfo.InvariantCulture),
            TownAccounts.ManaCapacity.ToString(CultureInfo.InvariantCulture));

        // 判据在 ArenaSetup.CanSummonHelper 一处：不在停靠期又没在打仗就召不下。
        _summon.Disabled = !ArenaSetup.CanSummonHelper(world);

        RefreshHelperRows(world);
    }

    /// <summary>帮工名单：一个帮工一行（名字 + 遣散）。名单变了才重摆。</summary>
    private void RefreshHelperRows(ArenaWorld world)
    {
        var helpers = world.Helpers;

        var signature = string.Empty;
        foreach (var helper in helpers) signature += helper.Id + ",";
        if (signature == _shownIds) return;

        _shownIds = signature;

        while (_helperRows.GetChildCount() > 0)
        {
            var child = _helperRows.GetChild(0);
            _helperRows.RemoveChild(child);
            child.QueueFree();
        }

        if (helpers.Count == 0)
        {
            _helperRows.AddChild(new Label
            {
                Name = "NoHelper",
                Text = Loc.T("还没有帮工"),
                MouseFilter = MouseFilterEnum.Ignore,
            });
            return;
        }

        foreach (var helper in helpers)
        {
            var row = new HBoxContainer { Name = $"HelperRow{helper.Id}" };
            row.AddThemeConstantOverride("separation", 8);

            row.AddChild(new Label
            {
                Name = $"HelperName{helper.Id}",
                Text = Loc.Fmt("{0}  {1}", helper.Id, Loc.T("帮工")),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            });

            var helperId = helper.Id;
            var dismiss = MakeButton($"Dismiss{helper.Id}", Loc.T("遣散"));
            dismiss.Pressed += () => OnDismissPressed(helperId);
            row.AddChild(dismiss);

            _helperRows.AddChild(row);
        }
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
