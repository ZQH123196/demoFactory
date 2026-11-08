#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 终局结算 —— 最终防守胜利后自动开一次，之后从命令栏「结算」回看。
///
/// <para><b>数值全部读 <see cref="RunSettlement"/></b>（胜利那一刻生成的一份拷贝）：
/// 继续经营之后金币、负债、建筑怎么变都不再改写这一份。现实耗时读
/// <see cref="RunTelemetrySnapshot"/>（表现层的账，逻辑层不读墙钟）。</para>
///
/// <para><b>「继续经营」是唯一写口</b>：走 <c>ContinueBusiness</c>，段进自由经营；
/// 这一屏之后还能从命令栏再开，看的是同一份旧结算。</para>
/// </summary>
public partial class SettlementPanel : Control, IModalCloseable
{
    /// <summary>弹层宽度（像素）。</summary>
    public const float PanelWidth = 620f;

    /// <summary>弹层高度（像素）。</summary>
    public const float PanelHeight = 700f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly Func<RunTelemetrySnapshot?> _telemetry;
    private readonly UiRoot _ui;

    private readonly Label _title;
    private readonly VBoxContainer _body;
    private readonly Button _continue;
    private readonly Button _close;
    private string _signature = string.Empty;

    public SettlementPanel(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject,
        Func<RunTelemetrySnapshot?> telemetry)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);
        ArgumentNullException.ThrowIfNull(telemetry);

        _ui = ui;
        _world = world;
        _inject = inject;
        _telemetry = telemetry;

        Name = "SettlementPanel";
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

        var rows = new VBoxContainer { Name = "Rows" };
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);

        _title = new Label
        {
            Name = "Title",
            Text = Loc.T("终局结算"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rows.AddChild(_title);

        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        rows.AddChild(scroll);

        _body = new VBoxContainer { Name = "Body", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_body);

        _continue = new Button
        {
            Name = "ContinueBusiness",
            Text = Loc.T("继续经营"),
            CustomMinimumSize = new Vector2(0f, 30f),
            FocusMode = FocusModeEnum.None,
        };
        _continue.Pressed += OnContinuePressed;
        rows.AddChild(_continue);

        _close = new Button
        {
            Name = "Close",
            Text = Loc.T("关闭"),
            CustomMinimumSize = new Vector2(0f, 30f),
            FocusMode = FocusModeEnum.None,
        };
        _close.Pressed += Close;
        rows.AddChild(_close);

        ui.AddPopup(this);
    }

    /// <summary>弹层开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开终局结算（有结算就重摆一遍；没有就写「还没到」）。</summary>
    public void Open()
    {
        if (_world() is null) return;

        _signature = string.Empty;
        Refresh();
        Visible = true;
        _ui.EnterModal(this);
    }

    /// <summary>关掉终局结算并摘掉自己那一份模态。</summary>
    public void Close()
    {
        Visible = false;
        _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (_world() is null)
        {
            Close();
            return;
        }

        Refresh();
    }

    // === 写路径 ===

    /// <summary>「继续经营」：走 <c>ContinueBusiness</c>，成了才收起这一屏。</summary>
    private void OnContinuePressed()
    {
        var reply = _inject("ContinueBusiness", string.Empty);
        if (reply == "ok") Close();
    }

    // === 每帧派生 ===

    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        var settlement = world.Settlement;
        var snapshot = _telemetry();
        var signature = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2:0.0}",
            settlement?.VictoryTick ?? -1, (int)world.Flow.Stage, snapshot?.TotalSeconds ?? -1.0);
        if (signature == _signature) return;
        _signature = signature;

        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }

        if (settlement is null)
        {
            _title.Text = Loc.T("终局结算");
            _body.AddChild(Line(Loc.T("还没有终局结算：最终防守胜利之后才会生成这一份")));
            _body.AddChild(Line(Loc.Fmt("当前段：{0} · 目标：{1}",
                world.Flow.PhaseLabel, world.Flow.Goal)));
            _continue.Disabled = true;
            _continue.TooltipText = Loc.T("不在结算那一档，继续经营发不出去");
            return;
        }

        _title.Text = Loc.Fmt("终局结算 · 第 {0} 步",
            settlement.VictoryTick.ToString(CultureInfo.InvariantCulture));

        AddHeader(Loc.T("整局"));
        AddLine(Loc.Fmt("位阶 {0} · 暴食态 {1} · 当前形态 {2}",
            Loc.T(BeastTierText.NameOf(settlement.Tier)),
            settlement.GluttonUnlocked ? Loc.T("已解锁") : Loc.T("未解锁"),
            Loc.T(BeastFormText.NameOf(settlement.Form))));
        AddLine(Loc.Fmt("镇政府耐久 {0} · 居民存活 {1} · 建筑完好 {2}",
            Num(settlement.TownHallDurability),
            settlement.AliveResidents.ToString(CultureInfo.InvariantCulture),
            settlement.IntactBuildings.ToString(CultureInfo.InvariantCulture)));
        AddLine(Loc.Fmt("金币 {0} · 负债 {1}",
            Num(settlement.Coin), Num(settlement.Debt)));
        AddLine(Loc.Fmt("累计经营收入 {0} · 支出 {1} · 门票收入 {2}",
            Num(settlement.ProducedCoin), Num(settlement.SpentCoin), Num(settlement.TicketIncome)));
        AddLine(Loc.Fmt("魔晶 产出 {0} · 使用 {1} · 出售 {2}",
            Num(settlement.CrystalProduced),
            settlement.CrystalUsed.ToString(CultureInfo.InvariantCulture),
            settlement.CrystalSold.ToString(CultureInfo.InvariantCulture)));
        AddLine(Loc.Fmt("防守 胜 {0} · 负 {1} · 最终重试 {2} 次",
            settlement.DefenceWins.ToString(CultureInfo.InvariantCulture),
            settlement.DefenceLosses.ToString(CultureInfo.InvariantCulture),
            settlement.FinalRetryCount.ToString(CultureInfo.InvariantCulture)));

        AddHeader(Loc.T("三个体验段"));
        foreach (var segment in settlement.Segments)
        {
            var real = snapshot is null
                ? Loc.T("现实耗时未接线")
                : Loc.Fmt("现实 {0:0.0} 秒", SegmentSeconds(snapshot, segment.Key));
            AddLine(Loc.Fmt("{0} · {1} · 游戏 {2} 步 · 收入 {3} / 支出 {4} · 防守 胜 {5} 负 {6}",
                Loc.T(segment.Label), real,
                segment.DurationTicks.ToString(CultureInfo.InvariantCulture),
                Num(segment.Income), Num(segment.Expense),
                segment.DefenceWins.ToString(CultureInfo.InvariantCulture),
                segment.DefenceLosses.ToString(CultureInfo.InvariantCulture)));
        }

        AddHeader(Loc.T("四项里程碑（从开局起算）"));
        AddLine(MilestoneLine(snapshot, settlement, RunMilestone.GluttonUnlocked, "暴食态解锁"));
        AddLine(MilestoneLine(snapshot, settlement, RunMilestone.FirstFormSwitch, "首次手动切换"));
        AddLine(MilestoneLine(snapshot, settlement, RunMilestone.Tier2, "二阶完成"));
        AddLine(MilestoneLine(snapshot, settlement, RunMilestone.FinalVictory, "最终胜利"));

        _continue.Disabled = world.Flow.Stage != RunStage.Settlement;
        _continue.TooltipText = _continue.Disabled
            ? Loc.T("不在结算那一档，继续经营发不出去")
            : Loc.T("结算之后回到自由经营；富城再拔锚只触发普通防守");
    }

    // === 摆件 ===

    private void AddHeader(string text)
    {
        var label = Line(text);
        label.AddThemeFontSizeOverride("font_size", 15);
        _body.AddChild(label);
    }

    private void AddLine(string text) => _body.AddChild(Line(text));

    private static Label Line(string text) => new()
    {
        Text = text,
        MouseFilter = MouseFilterEnum.Ignore,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    private static double SegmentSeconds(RunTelemetrySnapshot snapshot, string key) => key switch
    {
        "poor" => snapshot.PoorSeconds,
        "sand" => snapshot.SandSeconds,
        _ => snapshot.RichSeconds,
    };

    private static string MilestoneLine(
        RunTelemetrySnapshot? snapshot, RunSettlement settlement, RunMilestone kind, string title)
    {
        var milestone = settlement.MilestoneOf(kind);
        if (milestone.Tick < 0) return Loc.Fmt("{0}：未达成", Loc.T(title));

        if (snapshot is null || snapshot.MilestoneOf(kind) < 0)
        {
            return Loc.Fmt("{0}：第 {1} 步", Loc.T(title),
                milestone.Tick.ToString(CultureInfo.InvariantCulture));
        }

        return Loc.Fmt("{0}：第 {1} 步 · 现实 {2:0.0} 秒", Loc.T(title),
            milestone.Tick.ToString(CultureInfo.InvariantCulture), snapshot.MilestoneOf(kind));
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>点框外的空白处收起（框内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
