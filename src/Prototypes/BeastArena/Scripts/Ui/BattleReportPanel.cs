#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 战报弹层 —— 读 <see cref="BattleSim.LastReport"/> 那一份<b>收场那一刻</b>的结算读数上屏。
///
/// <para><b>字段照 spec §9</b>：场合、胜负、双方上场数、死亡、重伤、被抢件数、门票收入。
/// 面板自己不改世界字段（写路径仍只有 <c>Inject</c> 一条），也不参与结算 ——
/// 真值在逻辑层写一次，这里只读。</para>
///
/// <para><b>两处入口</b>：战斗结束由 <c>Main</c> 检测到新战报之后自动打开；
/// 也可以从竞技场右键菜单的「战报」再打开（没战报时那一条写「暂无战报」并置灰）。
/// 挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清；点框外或「关闭」都收起。</para>
/// </summary>
public partial class BattleReportPanel : Control, IModalCloseable
{
    /// <summary>框的宽度（像素）。</summary>
    public const float PanelWidth = 420f;

    /// <summary>框的高度（像素）。</summary>
    public const float PanelHeight = 306f;

    /// <summary>一行的高度（像素）。</summary>
    public const float RowHeight = 24f;

    /// <summary>行与行的间距（像素）。</summary>
    public const float RowGap = 4f;

    /// <summary>七个字段的上屏顺序（spec §9 那一行）。</summary>
    public static readonly string[] RowTitles =
    [
        "场合", "胜负", "双方上场", "死亡", "重伤", "被抢件数", "门票收入",
    ];

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;
    private readonly Label _subtitle;
    private readonly Label[] _values;
    private readonly Button _retry;

    public BattleReportPanel(
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

        Name = "BattleReportPanel";
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
            Text = Loc.T("战报"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _subtitle = new Label
        {
            Name = "Subtitle",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddChild(_subtitle);

        var grid = new GridContainer { Name = "Fields", Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", (int)RowGap);
        box.AddChild(grid);

        _values = new Label[RowTitles.Length];
        for (var i = 0; i < RowTitles.Length; i++)
        {
            grid.AddChild(new Label
            {
                Name = RowTitles[i] + "Name",
                Text = Loc.T(RowTitles[i]),
                CustomMinimumSize = new Vector2(110f, RowHeight),
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            });

            var value = new Label
            {
                Name = RowTitles[i] + "Value",
                CustomMinimumSize = new Vector2(0f, RowHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            grid.AddChild(value);
            _values[i] = value;
        }

        var close = new Button
        {
            Name = "Close",
            Text = Loc.T("关闭"),
            CustomMinimumSize = new Vector2(0f, 28f),
            FocusMode = FocusModeEnum.None,
        };
        close.Pressed += Close;
        box.AddChild(close);

        _retry = new Button
        {
            Name = "RetryFinal",
            Text = Loc.T("立即重试"),
            CustomMinimumSize = new Vector2(0f, 28f),
            FocusMode = FocusModeEnum.None,
            Visible = false,
        };
        _retry.Pressed += OnRetryPressed;
        box.AddChild(_retry);

        ui.AddPopup(this);
    }

    /// <summary>面板开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开战报（每次按逻辑层现况重读那份收场读数）。</summary>
    public void Open()
    {
        if (_world() is null) return;
        Refresh();
        Visible = true;
        _ui.EnterModal(this);
    }

    /// <summary>关掉战报并摘掉自己那一份模态。</summary>
    public void Close()
    {
        Visible = false;
        _ui.ExitModal(this);
    }

    /// <summary>场合那一格的上屏文本。</summary>
    public static string OccasionText(BattleOccasion occasion) => occasion switch
    {
        BattleOccasion.Match => Loc.T("办赛"),
        BattleOccasion.Defence => Loc.T("拔锚防守"),
        BattleOccasion.FinalDefence => Loc.T("最终防守"),
        BattleOccasion.CaveClearing => Loc.T("洞穴清剿"),
        _ => Loc.T("—"),
    };

    /// <summary>
    /// 胜负那一格：办赛读 <see cref="MatchOutcome"/>；拔锚防守（含最终防守）读
    /// <see cref="RunFlow.DefenceRecords"/> 里<b>同一收场步号</b>那一条 —— 战报自己不带防守胜负字段。
    /// 洞穴清剿读土壤册当下的清剿标记（只在收场那一格对得上；旧战报看到的是当下那一格）。
    /// </summary>
    public static string OutcomeValue(ArenaWorld world, BattleReport report)
    {
        if (report.Occasion is BattleOccasion.Defence or BattleOccasion.FinalDefence)
        {
            foreach (var record in world.Flow.DefenceRecords)
            {
                if (record.Tick != report.EndedTick) continue;
                return record.Won ? Loc.T("守住") : Loc.T("没守住");
            }

            return Loc.T("—");
        }

        if (report.Occasion == BattleOccasion.CaveClearing)
        {
            return world.Soil.IsCleared(UndergroundSoil.CaveSector, UndergroundSoil.NativeCaveLayer)
                ? Loc.T("清剿成功")
                : Loc.T("清剿失败");
        }

        return OutcomeText(report.Outcome);
    }

    /// <summary>胜负那一格的上屏文本（口径见 <see cref="MatchOutcome"/>）。</summary>
    public static string OutcomeText(MatchOutcome outcome) => outcome switch
    {
        MatchOutcome.SideAWins => Loc.T("A 队胜"),
        MatchOutcome.SideBWins => Loc.T("B 队胜"),
        MatchOutcome.Draw => Loc.T("平局"),
        MatchOutcome.Defeat => Loc.T("负（全员倒地）"),
        _ => Loc.T("—"),
    };

    /// <summary>按逻辑层现况重写七行（没有战报时整块写「暂无战报」）。</summary>
    private void Refresh()
    {
        var world = _world();
        var report = world?.Battle.LastReport;
        if (world is null || report is null)
        {
            _subtitle.Text = Loc.T("暂无战报");
            foreach (var value in _values) value.Text = Loc.T("—");
            _retry.Visible = false;
            return;
        }

        _subtitle.Text = Loc.Fmt("第 {0} 步收场 · {1}",
            report.EndedTick, OccasionText(report.Occasion));
        _values[0].Text = OccasionText(report.Occasion);
        _values[1].Text = OutcomeValue(world, report);
        _values[2].Text = Loc.Fmt("甲 {0} · 乙 {1}", report.SideACount, report.SideBCount);
        _values[3].Text = report.DeadCount.ToString(CultureInfo.InvariantCulture);
        _values[4].Text = report.WoundedCount.ToString(CultureInfo.InvariantCulture);
        _values[5].Text = report.LootedCount.ToString(CultureInfo.InvariantCulture);
        _values[6].Text = Loc.Fmt("{0:0.##} 金", report.GateIncome);

        // 最终防守失败之后才有「立即重试」：判定在逻辑层 ArenaWorld.CanRetryFinalDefence，
        // 界面只按它显隐（不在这一件里重判「是不是最终防守 / 还打不打」）。
        _retry.Visible = world.CanRetryFinalDefence;
        _retry.Disabled = !world.CanRetryFinalDefence;
        _retry.TooltipText = world.CanRetryFinalDefence
            ? Loc.T("按战前快照原样重开同一场；重试次数只增不清零")
            : Loc.T("不在最终防守的失败收场，重试发不出去");
    }

    /// <summary>「立即重试」：走 <c>RetryFinalDefence</c>，成了才收起战报（不成就把原因留在横幅上）。</summary>
    private void OnRetryPressed()
    {
        var reply = _inject("RetryFinalDefence", string.Empty);
        if (reply == "ok") Close();
    }

    /// <summary>点框外的空白处收起（框内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
