#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 招募框 —— 五个来路固定顺序各一行，点一行发 <c>Inject("Recruit", source)</c>。
///
/// <para><b>顺序写死</b>（spec §8）：城乡招 100/60、捉游民 0/40、买奴隶 200/30、
/// 俘强盗 0/20、捕野兽 150/10。枚举序不是这个顺序（<see cref="RecruitSource.BuySlave"/>
/// 排在 <see cref="RecruitSource.CatchVagrant"/> 前面），所以这里用一张常量表，
/// 不遍历 <c>Enum.GetValues</c>。</para>
///
/// <para><b>置灰只读逻辑层</b>：每行读 <see cref="ArenaWorld.CanRecruit"/> 拿到第一个命中的原因，
/// 行尾写那一句；额度用完之后整屏照常能开，五行一起置灰并写「本次停靠已招满」。
/// 框自己不改世界字段，写路径仍只有 <c>Inject</c> 一条。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清；
/// 点空白处与「关闭」按钮都收起。</para>
/// </summary>
public partial class RecruitDialog : Control, IModalCloseable
{
    /// <summary>框的宽度（像素）。</summary>
    public const float PanelWidth = 520f;

    /// <summary>框的高度（像素）。</summary>
    public const float PanelHeight = 344f;

    /// <summary>一行按钮的高度（像素）。</summary>
    public const float RowHeight = 30f;

    /// <summary>行间距（像素）。</summary>
    public const float RowGap = 5f;

    /// <summary>
    /// 五个来路的<b>上屏顺序</b>（spec §8 那张表的 1~5 行）。
    /// 这张表是顺序的唯一来源，不依赖枚举序。
    /// </summary>
    public static readonly RecruitSource[] SourceOrder =
    [
        RecruitSource.TownOrCountry,
        RecruitSource.CatchVagrant,
        RecruitSource.BuySlave,
        RecruitSource.CaptureBandit,
        RecruitSource.CatchBeast,
    ];

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;

    private readonly Label _subtitle;
    private readonly List<(RecruitSource Source, Button Button, Label Reason)> _rows = [];

    /// <summary>上一次刷新时世界的那几个读数（钱、岗位、额度、停靠）—— 没变就不重排版。</summary>
    private string _signature = string.Empty;

    public RecruitDialog(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);

        _ui = ui;
        _world = world;
        _inject = inject;

        Name = "RecruitDialog";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
        GuiInput += OnDialogInput;

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
            BgColor = new Color(0.12f, 0.13f, 0.17f, 0.98f),
            BorderColor = new Color(0.45f, 0.50f, 0.62f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 18f,
            ContentMarginRight = 18f,
            ContentMarginTop = 14f,
            ContentMarginBottom = 14f,
        });
        AddChild(panel);

        var box = new VBoxContainer { Name = "Rows" };
        box.AddThemeConstantOverride("separation", (int)RowGap);
        panel.AddChild(box);

        box.AddChild(new Label
        {
            Text = Loc.T("招募"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _subtitle = new Label
        {
            Name = "Subtitle",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddChild(_subtitle);

        foreach (var source in SourceOrder)
        {
            var row = new HBoxContainer { Name = source.ToString() };
            row.AddThemeConstantOverride("separation", 8);
            box.AddChild(row);

            var button = new Button
            {
                Name = "Pick",
                Text = RowText(source),
                CustomMinimumSize = new Vector2(0f, RowHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None,
            };
            var picked = source;
            button.Pressed += () => OnPick(picked);
            row.AddChild(button);

            var reason = new Label
            {
                Name = "Reason",
                CustomMinimumSize = new Vector2(150f, 0f),
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(reason);

            _rows.Add((source, button, reason));
        }

        var close = new Button
        {
            Name = "Close",
            Text = Loc.T("关闭"),
            CustomMinimumSize = new Vector2(0f, RowHeight),
            FocusMode = FocusModeEnum.None,
        };
        close.Pressed += Close;
        box.AddChild(close);

        ui.AddPopup(this);
    }

    /// <summary>框开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开招募框（每次都按世界现况重算置灰）。</summary>
    public void Open()
    {
        if (_world() is null) return;
        Refresh();
        Visible = true;
        _ui.EnterModal(this);
    }

    /// <summary>关掉招募框并摘掉自己那一份模态。</summary>
    public void Close()
    {
        Visible = false;
        _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        // 开着的时候世界的钱与岗位都可能变（产出、派工），行尾那一句要跟着现况走。
        if (Visible) Refresh();
    }

    // === 行 ===

    private void OnPick(RecruitSource source)
    {
        // 只发标识符；付钱必得，成败都由 Inject 记 Journal 并转横幅。
        _inject("Recruit", source.ToString());
        Refresh();
    }

    /// <summary>按世界现况重算每一行的置灰与行尾原因。</summary>
    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        // 这几个读数不变时整块结果就不变；每帧重写 Label 会让 Godot 每帧重排版一次。
        var signature = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4:0.##}",
            world.IsDocked ? world.DockedCityId : "-",
            world.RecruitLimitLeft, world.Roster.WorkerCount, world.Roster.GladiatorCount, world.Town.Coin);
        if (signature == _signature) return;
        _signature = signature;

        _subtitle.Text = Loc.Fmt("每停靠最多招 {0} 人，本次还剩 {1} 人",
            ArenaTuning.RecruitPerDock, world.RecruitLimitLeft);

        foreach (var row in _rows)
        {
            var blocker = world.CanRecruit(row.Source);
            row.Button.Disabled = blocker != RecruitBlocker.None;
            row.Reason.Text = blocker == RecruitBlocker.None ? string.Empty : BlockerText(blocker);
        }
    }

    /// <summary>
    /// 置灰那一句 —— <b>第一个命中的原因</b>由 <see cref="ArenaWorld.CanRecruit"/> 给出，
    /// 顺序是额度 → 岗位 → 金币（期别那一条只在框被别的路径打开时出现）。
    /// </summary>
    public static string BlockerText(RecruitBlocker blocker) => blocker switch
    {
        RecruitBlocker.NotDocked => Loc.T("不在停靠期"),
        RecruitBlocker.LimitReached => Loc.T("本次停靠已招满"),
        RecruitBlocker.PostFull => Loc.T("岗位已满"),
        RecruitBlocker.NoCoin => Loc.T("金币不够"),
        _ => string.Empty,
    };

    /// <summary>一行按钮上的字：来路、成本、忠诚初值（spec §8 那三列）。</summary>
    public static string RowText(RecruitSource source)
        => string.Format(CultureInfo.InvariantCulture, "{0}　{1:0.##} 金 · 忠心 {2:0.##}",
            Loc.T(NameOf(source)), ArenaSetup.RecruitCost(source), ArenaSetup.LoyaltyOf(source));

    /// <summary>来路的中文名（键与 <see cref="ArenaSetup.ParseRecruitSource"/> 认的名字同一份）。</summary>
    public static string NameOf(RecruitSource source) => source switch
    {
        RecruitSource.TownOrCountry => "城乡招",
        RecruitSource.CatchVagrant => "捉游民",
        RecruitSource.BuySlave => "买奴隶",
        RecruitSource.CaptureBandit => "俘强盗",
        RecruitSource.CatchBeast => "捕野兽",
        _ => source.ToString(),
    };

    /// <summary>点框外的空白处收起（框内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnDialogInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
