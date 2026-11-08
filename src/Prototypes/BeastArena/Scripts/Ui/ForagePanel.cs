#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 吞食目标面板 —— 命令栏「吞食」开它。列出全部生态对象与距离 / 耗时 / 收益，
/// 玩家点一行就是 <c>EatTarget|对象 id</c>；顶上一颗「召回」是 <c>RecallForage</c>。
///
/// <para><b>拒绝原因照实上屏</b>：饱食已满、战斗中、停靠、仪式、停摆、距离超出、觅食冷却
/// 都从世界读数写成一句人话（判定仍在逻辑层，本件只显示）。点得动的行才发得出去；
/// 点不动的行把原因写在按钮的悬停提示与顶上那一行状态里。</para>
///
/// <para><b>收益预览走逻辑层同一份表</b>：<see cref="BeastEcology.GainsOf"/> 与
/// <c>TryEat</c> 用的是同一组基础值与形态倍率，面板不另算一遍。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清；不暂停世界。</para>
/// </summary>
public partial class ForagePanel : Control, IModalCloseable
{
    /// <summary>弹层宽度（像素）。</summary>
    public const float PanelWidth = 520f;

    /// <summary>弹层高度（像素）。</summary>
    public const float PanelHeight = 540f;

    /// <summary>一行的高度（像素）。</summary>
    public const float RowHeight = 26f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;

    private readonly Label _status;
    private readonly Button _recall;
    private readonly VBoxContainer _rows;

    private readonly Dictionary<string, (Label Info, Button Eat)> _rowById = new(StringComparer.Ordinal);
    private string _shownIds = string.Empty;
    private string _signature = string.Empty;

    public ForagePanel(
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

        Name = "ForagePanel";
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
            ContentMarginLeft = 16f,
            ContentMarginRight = 16f,
            ContentMarginTop = 12f,
            ContentMarginBottom = 12f,
        });
        AddChild(panel);

        var rows = new VBoxContainer { Name = "Rows" };
        rows.AddThemeConstantOverride("separation", 5);
        panel.AddChild(rows);

        rows.AddChild(new Label
        {
            Name = "Title",
            Text = Loc.T("吞食"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _status = new Label
        {
            Name = "Status",
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        rows.AddChild(_status);

        _recall = new Button
        {
            Name = "Recall",
            Text = Loc.T("召回觅食"),
            CustomMinimumSize = new Vector2(0f, 28f),
            FocusMode = FocusModeEnum.None,
        };
        _recall.Pressed += OnRecallPressed;
        rows.AddChild(_recall);

        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        rows.AddChild(scroll);

        _rows = new VBoxContainer { Name = "Targets", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_rows);

        var close = new Button
        {
            Name = "Close",
            Text = Loc.T("关闭"),
            CustomMinimumSize = new Vector2(0f, 28f),
            FocusMode = FocusModeEnum.None,
        };
        close.Pressed += Close;
        rows.AddChild(close);

        ui.AddPopup(this);
    }

    /// <summary>面板开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开吞食面板（按世界现况重算目标行）。</summary>
    public void Open()
    {
        if (_world() is null) return;

        _signature = string.Empty;
        Refresh();
        Visible = true;
        _ui.EnterModal(this);
    }

    /// <summary>关掉吞食面板并摘掉自己那一份模态。</summary>
    public void Close()
    {
        Visible = false;
        _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        Refresh();
    }

    // === 写路径 ===

    private void OnEatPressed(EcoNode node) => _inject("EatTarget", node.Id);

    private void OnRecallPressed() => _inject("RecallForage", string.Empty);

    // === 每帧派生 ===

    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        BuildRowsIfNeeded(world);

        var phase = world.Voyage.Phase;
        var canRecall = phase is VoyagePhase.DetouringToFood or VoyagePhase.Eating;

        var satiated = world.Ecology.Satiation >= ArenaTuning.SatiationMax - 1e-9;
        var busy = world.Battle.Occasion != BattleOccasion.None || world.IsDocked
            || world.InRitual || world.InStandstill;
        var cooling = world.Ecology.InForageCooldownAt(world.Tick);

        var availability = AvailabilitySignature(world);
        var signature = string.Format(CultureInfo.InvariantCulture,
            "{0:0.##}|{1:0.0}|{2:0.0}|{3}|{4}|{5}|{6}|{7}|{8:0.0}|{9}",
            world.Ecology.Satiation, world.Voyage.Position.X, world.Voyage.Position.Z,
            (int)world.Form, (int)phase, (int)world.Battle.Occasion, world.IsDocked,
            Math.Max(0, (world.Ecology.ForageCooldownUntilTick - world.Tick) / ArenaTime.StepsPerGameMinute),
            world.Ecology.DigestionOverflow, availability);
        var sameRows = signature == _signature;
        _signature = signature;

        _status.Text = StatusText(world, satiated, busy, cooling);
        _recall.Disabled = !canRecall;
        _recall.TooltipText = canRecall ? string.Empty : Loc.T("不在觅食途中（停靠 / 战斗 / 仪式期间也发不出去）");

        if (sameRows) return;

        foreach (var node in world.Ecology.Nodes)
        {
            if (!_rowById.TryGetValue(node.Id, out var row)) continue;

            var distance = world.Voyage.Position.DistanceTo(node.Position);
            var available = node.AvailableAt(world.Tick);
            var (satiationGain, digestionGain) = BeastEcology.GainsOf(node.Kind, world.Form);

            row.Info.Text = Loc.Fmt("{0} · {1} · 距离 {2} 米 · {3} 游戏分钟 · 饱食 +{4} / 消化 +{5}",
                KindText(node.Kind),
                node.Id,
                distance.ToString("0.0", CultureInfo.InvariantCulture),
                ArenaTuning.EatGameMinutes.ToString(CultureInfo.InvariantCulture),
                satiationGain.ToString("0.#", CultureInfo.InvariantCulture),
                digestionGain.ToString("0.#", CultureInfo.InvariantCulture));

            var reason = EatReasonOf(world, node, distance, satiated, busy);
            row.Eat.Disabled = reason.Length > 0;
            row.Eat.TooltipText = reason;
            row.Info.Text += available ? string.Empty : Loc.T(" · 已被吃掉 / 再生中");
        }
    }

    /// <summary>目标行只在名单变了才重建（12 个对象的 id 固定，重建一次就够）。</summary>
    private void BuildRowsIfNeeded(ArenaWorld world)
    {
        var ids = new List<string>(world.Ecology.Nodes.Count);
        foreach (var node in world.Ecology.Nodes) ids.Add(node.Id);
        var signature = string.Join('|', ids);
        if (signature == _shownIds) return;

        _shownIds = signature;
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        _rowById.Clear();

        foreach (var node in world.Ecology.Nodes)
        {
            var row = new HBoxContainer { Name = $"Target_{node.Id}" };
            row.AddThemeConstantOverride("separation", 6);

            var info = new Label
            {
                Name = "Info",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                ClipText = true,
            };
            info.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(info);

            var eat = new Button
            {
                Name = $"Eat_{node.Id}",
                Text = Loc.T("吞食"),
                CustomMinimumSize = new Vector2(58f, RowHeight),
                FocusMode = FocusModeEnum.None,
            };

            var target = node;
            eat.Pressed += () => OnEatPressed(target);
            row.AddChild(eat);

            _rows.AddChild(row);
            _rowById[node.Id] = (info, eat);
        }
    }

    /// <summary>这一行为什么点不动（空串 = 点得动）。文案按逻辑层现有那几档原因写。</summary>
    private static string EatReasonOf(
        ArenaWorld world, EcoNode node, double distance, bool satiated, bool busy)
    {
        if (busy)
        {
            if (world.Battle.Occasion != BattleOccasion.None) return Loc.T("正在打仗，吞不下去");
            if (world.IsDocked) return Loc.T("停靠期间不进食");
            if (world.InRitual) return Loc.T("进阶仪式期间巨兽匍匐不动");
            return Loc.T("停摆期巨兽 Hold 在大地图上");
        }

        if (satiated)
        {
            return Loc.Fmt("已经吃饱了（{0}/{1}）",
                world.Ecology.Satiation.ToString("0.#", CultureInfo.InvariantCulture),
                ArenaTuning.SatiationMax.ToString("0.#", CultureInfo.InvariantCulture));
        }

        // 觅食 / 指定进食 / 召回返航途中不接新的目标：等这一口结束（或先按「召回觅食」回航线）。
        if (world.Voyage.Phase is VoyagePhase.DetouringToFood or VoyagePhase.Eating
            or VoyagePhase.ManualEating or VoyagePhase.ReturningToWaypoint)
        {
            return Loc.T("正在觅食 / 进食（或返航）：等这一口结束再选目标");
        }

        if (!node.AvailableAt(world.Tick)) return Loc.T("这个对象已经被吃掉，正在再生（矿石不再生）");

        if (distance > ArenaTuning.ManualEatRangeMeters)
        {
            return Loc.Fmt("离它 {0} 米，超出 {1} 米",
                distance.ToString("0.0", CultureInfo.InvariantCulture),
                ArenaTuning.ManualEatRangeMeters.ToString("0.0", CultureInfo.InvariantCulture));
        }

        return string.Empty;
    }

    /// <summary>顶上一行：先说场合，再说冷却与距离提示。</summary>
    private static string StatusText(ArenaWorld world, bool satiated, bool busy, bool cooling)
    {
        if (busy)
        {
            if (world.Battle.Occasion != BattleOccasion.None) return Loc.T("战斗中：吞不下去");
            if (world.IsDocked) return Loc.T("停靠期间：巨兽不进食");
            if (world.InRitual) return Loc.T("进阶仪式期间：巨兽匍匐不动");
            return Loc.T("停摆期：巨兽 Hold 在大地图上");
        }

        if (satiated)
        {
            return Loc.Fmt("饱食已满（{0}/{1}）：吃不下了",
                world.Ecology.Satiation.ToString("0.#", CultureInfo.InvariantCulture),
                ArenaTuning.SatiationMax.ToString("0.#", CultureInfo.InvariantCulture));
        }

        if (world.Voyage.Phase is VoyagePhase.DetouringToFood or VoyagePhase.Eating
            or VoyagePhase.ManualEating or VoyagePhase.ReturningToWaypoint)
        {
            return Loc.T("正在觅食 / 进食（或返航）：等这一口结束，或先按「召回觅食」");
        }

        if (cooling)
        {
            var left = (world.Ecology.ForageCooldownUntilTick - world.Tick) / ArenaTime.StepsPerGameMinute;
            var reason = world.Ecology.ForageGaveUpReason;
            return reason.Length > 0
                ? Loc.Fmt("觅食冷却中，还剩 {0} 游戏分钟：{1}",
                    Math.Max(0, left).ToString(CultureInfo.InvariantCulture), reason)
                : Loc.Fmt("觅食冷却中，还剩 {0} 游戏分钟",
                    Math.Max(0, left).ToString(CultureInfo.InvariantCulture));
        }

        if (!AnyAvailable(world))
        {
            return Loc.T("没有可吞对象：地图上的树 / 兽群 / 矿石都已经被吃掉，正在再生");
        }

        return Loc.Fmt("只吃得了 {0} 米内的对象；超出距离先把巨兽开过去",
            ArenaTuning.ManualEatRangeMeters.ToString("0.0", CultureInfo.InvariantCulture));
    }

    /// <summary>地图上还有没有可吞对象（不可吞的那些行仍列出来，写清「正在再生」）。</summary>
    private static bool AnyAvailable(ArenaWorld world)
    {
        foreach (var node in world.Ecology.Nodes)
        {
            if (node.AvailableAt(world.Tick)) return true;
        }

        return false;
    }

    /// <summary>每个对象「现在可不可吞」压成一串 0/1，进刷新签名（再生到点那一格要重写行）。</summary>
    private static string AvailabilitySignature(ArenaWorld world)
    {
        var bits = new char[world.Ecology.Nodes.Count];
        for (var i = 0; i < world.Ecology.Nodes.Count; i++)
        {
            bits[i] = world.Ecology.Nodes[i].AvailableAt(world.Tick) ? '1' : '0';
        }

        return new string(bits);
    }

    private static string KindText(EcoKind kind) => kind switch
    {
        EcoKind.Tree => Loc.T("树木"),
        EcoKind.Herd => Loc.T("兽群"),
        _ => Loc.T("矿石"),
    };

    /// <summary>点框外的空白处收起（框内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
