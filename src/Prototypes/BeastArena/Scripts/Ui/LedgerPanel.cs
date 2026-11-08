#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 账本面板 —— 命令栏「账本」开它（<b>只在停靠期</b>）。四个区：定价、负债、策略、发薪。
///
/// <para><b>四个区各管一件事</b>：
/// ① 定价三档（酒店 / 饭店 / 门票的倍率，0.5..2.0，步长 0.1）；
/// ② 负债按债主分户，一户一行（势力名、余额、「还清」、金额）；
/// ③ 还款策略三选一，外加一个优先债主下拉；
/// ④ 发薪：应发金额、欠薪天数与「发薪」那一下。</para>
///
/// <para><b>写路径只有一条</b>：定价发 <c>SetPricing|酒店|饭店|门票</c>、
/// 还债发 <c>PayDebt|势力 id|金额</c>、策略发 <c>SetDebtPolicy|策略|优先债主</c>、
/// 发薪发 <c>PayWages</c>，都走 <see cref="Main.Inject"/>（进 Journal、能重放）。
/// 面板自己不改世界字段 —— 读数每帧现读世界。</para>
///
/// <para><b>应发为 0 时「发薪」置灰</b>：没有要发的那一份，按下去也发不出什么
/// （动词那一侧回 <see cref="RejectReason.InvalidArgs"/>）。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清。</para>
/// </summary>
public partial class LedgerPanel : Control, IModalCloseable
{
    /// <summary>面板宽度（像素）。</summary>
    public const float PanelWidth = 520f;

    /// <summary>面板高度（像素）。内容比它高时按最小尺寸长出去。</summary>
    public const float PanelHeight = 620f;

    /// <summary>行间距（像素）。</summary>
    public const float RowGap = 5f;

    /// <summary>数字输入框的宽度（像素）。</summary>
    public const float FieldWidth = 120f;

    /// <summary>数字输入框的高度（像素）。</summary>
    public const float FieldHeight = 28f;

    /// <summary>按钮高度（像素）。</summary>
    public const float ButtonHeight = 30f;

    /// <summary>金额输入框的上限（金）。</summary>
    public const double MaxAmount = 999999.0;

    /// <summary>定价倍率的步长（面板上拧一下的幅度，与逻辑层同一档）。</summary>
    public const double PricingStep = 0.1;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;
    private readonly bool _embedded;

    // === 定价 ===

    private readonly SpinBox _hotel;
    private readonly SpinBox _restaurant;
    private readonly SpinBox _ticket;
    private readonly Button _applyPricing;
    private readonly Label _dockHint;

    // === 负债 ===

    private readonly VBoxContainer _debtRows;
    private readonly Label _noDebt;
    private readonly List<string> _debtOrder = [];
    private readonly Dictionary<string, SpinBox> _debtAmounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _debtLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _debtShown = new(StringComparer.Ordinal);

    // === 策略 ===

    private readonly Button _policyNone;
    private readonly Button _policyAutoOrder;
    private readonly Button _policyAutoTarget;
    private readonly OptionButton _priority;
    private bool _syncing;

    // === 发薪 ===

    private readonly Label _wages;
    private readonly Button _payWages;

    /// <summary>上一次刷新时的读数签名 —— 没变就不重写文字（每帧刷会让 Godot 每帧重排版一次）。</summary>
    private string _signature = string.Empty;

    public LedgerPanel(
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

        Name = "LedgerPanel";
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
            GuiInput += OnPanelInput;
        }

        var panel = new PanelContainer { Name = "Box" };
        if (embedded)
        {
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
        }
        else
        {
            panel.AnchorLeft = 0.5f;
            panel.AnchorRight = 0.5f;
            panel.AnchorTop = 0.5f;
            panel.AnchorBottom = 0.5f;
            panel.OffsetLeft = -PanelWidth / 2f;
            panel.OffsetRight = PanelWidth / 2f;
            panel.OffsetTop = -PanelHeight / 2f;
            panel.OffsetBottom = PanelHeight / 2f;
        }
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
        rows.AddThemeConstantOverride("separation", (int)RowGap);

        // 嵌在「经营」分栏里时让内容自己滚：账本这一页最高，分栏矮时不裁掉发薪那一块。
        if (embedded)
        {
            var scroll = new ScrollContainer
            {
                Name = "Scroll",
                SizeFlagsVertical = SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            panel.AddChild(scroll);
            scroll.AddChild(rows);
            rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        else
        {
            panel.AddChild(rows);
        }

        rows.AddChild(new Label
        {
            Name = "Title",
            Text = Loc.T("账本"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 停靠期提示：定价、还债、策略、发薪都是停靠期的事。
        _dockHint = new Label
        {
            Name = "DockHint",
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        };
        _dockHint.AddThemeFontSizeOverride("font_size", 12);
        rows.AddChild(_dockHint);

        // === 定价 ===

        rows.AddChild(new Label
        {
            Name = "PricingTitle",
            Text = Loc.T("定价"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _hotel = MakePricingField("Hotel");
        rows.AddChild(MakeFieldRow("HotelRow", Loc.T("酒店"), _hotel));

        _restaurant = MakePricingField("Restaurant");
        rows.AddChild(MakeFieldRow("RestaurantRow", Loc.T("饭店"), _restaurant));

        _ticket = MakePricingField("Ticket");
        rows.AddChild(MakeFieldRow("TicketRow", Loc.T("门票"), _ticket));

        _applyPricing = MakeButton("ApplyPricing", Loc.T("应用"));
        _applyPricing.Pressed += OnApplyPricingPressed;
        rows.AddChild(_applyPricing);

        rows.AddChild(new HSeparator { Name = "DebtSeparator" });

        // === 负债 ===

        rows.AddChild(new Label
        {
            Name = "DebtTitle",
            Text = Loc.T("负债"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _debtRows = new VBoxContainer { Name = "DebtRows" };
        _debtRows.AddThemeConstantOverride("separation", (int)RowGap);
        rows.AddChild(_debtRows);

        // 一笔债都没有时只留这一行字（那几行是空的，别的什么都不显示）。
        _noDebt = new Label
        {
            Name = "NoDebt",
            Text = Loc.T("没有负债"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rows.AddChild(_noDebt);

        rows.AddChild(new HSeparator { Name = "PolicySeparator" });

        // === 策略 ===

        rows.AddChild(new Label
        {
            Name = "PolicyTitle",
            Text = Loc.T("策略"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var policyRow = new HBoxContainer { Name = "PolicyButtons" };
        policyRow.AddThemeConstantOverride("separation", 8);
        rows.AddChild(policyRow);

        _policyNone = MakePolicyButton("PolicyNone", Loc.T("不自动"), DebtPolicy.None);
        policyRow.AddChild(_policyNone);

        _policyAutoOrder = MakePolicyButton("PolicyAutoOrder", Loc.T("按名单顺序"), DebtPolicy.AutoOrder);
        policyRow.AddChild(_policyAutoOrder);

        _policyAutoTarget = MakePolicyButton("PolicyAutoTarget", Loc.T("指定债主"), DebtPolicy.AutoTarget);
        policyRow.AddChild(_policyAutoTarget);

        var priorityRow = new HBoxContainer { Name = "PriorityRow" };
        priorityRow.AddThemeConstantOverride("separation", 8);
        rows.AddChild(priorityRow);

        priorityRow.AddChild(new Label
        {
            Name = "Tag",
            Text = Loc.T("优先债主"),
            CustomMinimumSize = new Vector2(150f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _priority = new OptionButton
        {
            Name = "Priority",
            CustomMinimumSize = new Vector2(FieldWidth, FieldHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
        };
        _priority.ItemSelected += OnPrioritySelected;
        priorityRow.AddChild(_priority);

        rows.AddChild(new HSeparator { Name = "WagesSeparator" });

        // === 发薪 ===

        rows.AddChild(new Label
        {
            Name = "WagesTitle",
            Text = Loc.T("发薪"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _wages = new Label
        {
            Name = "Wages",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rows.AddChild(_wages);

        _payWages = MakeButton("PayWages", Loc.T("发薪"));
        _payWages.Pressed += OnPayWagesPressed;
        rows.AddChild(_payWages);

        if (!embedded)
        {
            var close = MakeButton("Close", Loc.T("关闭"));
            close.Pressed += Close;
            rows.AddChild(close);
        }

        if (!embedded) ui.AddPopup(this);
    }

    /// <summary>面板开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开账本面板（每次都按世界现况重算置灰与读数）。</summary>
    public void Open()
    {
        var world = _world();
        if (world is null) return;

        _signature = string.Empty;
        SyncPricing(world);
        Refresh();
        Visible = true;
        if (!_embedded) _ui.EnterModal(this);
    }

    /// <summary>三个定价输入框拨到世界当前那三档倍率（开面板时对一次账）。</summary>
    private void SyncPricing(ArenaWorld world)
    {
        _hotel.Value = world.Town.HotelMultiplier;
        _restaurant.Value = world.Town.RestaurantMultiplier;
        _ticket.Value = world.Town.TicketMultiplier;
    }

    /// <summary>关掉账本面板并摘掉自己那一份模态（世界照常收拾取）。</summary>
    public void Close()
    {
        Visible = false;
        if (!_embedded) _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // 独立弹层那一档：开着的时候巨兽拔锚离港了就自己收起来。
        // 嵌在「经营」分栏里时不收 —— 分栏留在原地，页内写清「停靠期才能办」。
        if (!_embedded && _world() is not { IsDocked: true })
        {
            Close();
            return;
        }

        Refresh();
    }

    // === 写路径 ===

    /// <summary>「应用」：三个倍率现读三个输入框，一发 <c>SetPricing</c>。</summary>
    private void OnApplyPricingPressed()
    {
        var args = string.Join('|',
            Number(_hotel.Value), Number(_restaurant.Value), Number(_ticket.Value));
        _inject("SetPricing", args);
        Refresh();
    }

    /// <summary>「还清」：发 <c>PayDebt|势力 id|金额</c>（金额取那一行的输入框）。</summary>
    private void OnPayDebtPressed(string factionId)
    {
        if (!_debtAmounts.TryGetValue(factionId, out var field)) return;

        var amount = field.Value;
        if (amount <= 0.0) return;

        _inject("PayDebt", string.Join('|', factionId, Number(amount)));
        Refresh();
    }

    /// <summary>三颗策略按钮：按下就发 <c>SetDebtPolicy|策略|优先债主</c>（优先债主取下拉当前那一个）。</summary>
    private void OnPolicyPressed(DebtPolicy policy)
    {
        _inject("SetDebtPolicy", string.Join('|', policy.ToString(), PriorityFactionId()));
        Refresh();
    }

    /// <summary>优先债主下拉换了一个：发 <c>SetDebtPolicy|当前策略|新债主</c>。</summary>
    private void OnPrioritySelected(long index)
    {
        if (_syncing) return;

        // 第 0 项是「不指定」：写回去就是「-」，那一档是「不指定优先债主」。
        var factionId = FactionIdAt((int)index);
        _inject("SetDebtPolicy", string.Join('|', PolicyOfWorld().ToString(), factionId ?? "-"));
        Refresh();
    }

    /// <summary>「发薪」：一发 <c>PayWages</c>（没有应发时按钮是灰的，按不到这里）。</summary>
    private void OnPayWagesPressed()
    {
        _inject("PayWages", string.Empty);
        Refresh();
    }

    // === 每帧派生 ===

    /// <summary>按世界现况重算读数、置灰与那几行字。</summary>
    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        var signature = string.Format(CultureInfo.InvariantCulture, "{0:0.##}|{1:0.##}|{2:0.##}|{3}|{4}|{5:0.##}|{6}|{7}|{8}",
            world.Town.HotelMultiplier, world.Town.RestaurantMultiplier, world.Town.TicketMultiplier,
            world.Town.Policy, world.Town.PriorityFactionId,
            world.WagesDue, world.ArrearsDays, DebtsSignature(world), world.IsDocked);
        if (signature == _signature) return;
        _signature = signature;

        RefreshDebts(world);
        RefreshPolicy(world);
        RefreshWages(world);

        // 不在停靠期：账本这一页整页是置灰的，页首那一行写清原因（动词那一侧也会拒并给原因）。
        _dockHint.Visible = !world.IsDocked;
        _dockHint.Text = world.IsDocked ? string.Empty : Loc.T("停靠期才能定价、还债、发薪与改策略");
        _applyPricing.Disabled = !world.IsDocked;
        _policyNone.Disabled = !world.IsDocked;
        _policyAutoOrder.Disabled = !world.IsDocked;
        _policyAutoTarget.Disabled = !world.IsDocked;
        _priority.Disabled = !world.IsDocked;
        SetButtonsDisabled(_debtRows, !world.IsDocked);
    }

    /// <summary>负债那一区：一户一行；一笔债都没有就只留「没有负债」那一行。</summary>
    private void RefreshDebts(ArenaWorld world)
    {
        var indebted = new List<CreditorAccount>();
        foreach (var c in world.Town.Creditors)
        {
            if (c.Outstanding > 0.0) indebted.Add(c);
        }

        _noDebt.Visible = indebted.Count == 0;
        _debtRows.Visible = indebted.Count > 0;

        // 名单变了才重摆那几行（重摆会把玩家填的金额冲掉，所以尽量不重摆）。
        var wanted = new List<string>();
        foreach (var c in indebted) wanted.Add(c.FactionId);
        if (!SameOrder(wanted, _debtOrder))
        {
            ClearChildren(_debtRows);
            _debtAmounts.Clear();
            _debtLabels.Clear();
            _debtShown.Clear();
            _debtOrder.Clear();
            foreach (var c in indebted) AddDebtRow(c);
        }

        foreach (var c in indebted)
        {
            if (!_debtLabels.TryGetValue(c.FactionId, out var balanceLabel)) continue;

            // 余额没变就不动这一行（玩家自己改过的金额不冲掉）。
            if (_debtShown.TryGetValue(c.FactionId, out var shown) && Math.Abs(shown - c.Outstanding) < 1e-9)
            {
                continue;
            }

            _debtShown[c.FactionId] = c.Outstanding;
            balanceLabel.Text = Loc.Fmt("{0} 金", Number(c.Outstanding));
            if (!_debtAmounts.TryGetValue(c.FactionId, out var field)) continue;

            field.Value = Math.Min(c.Outstanding, MaxAmount);
        }
    }

    /// <summary>一家债主那一行：势力名、余额、金额输入框与「还清」。</summary>
    private void AddDebtRow(CreditorAccount creditor)
    {
        var row = new HBoxContainer { Name = $"DebtRow{creditor.FactionId}" };
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(new Label
        {
            Name = $"DebtName{creditor.FactionId}",
            Text = Loc.T(creditor.DisplayName),
            CustomMinimumSize = new Vector2(110f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var balance = new Label
        {
            Name = $"DebtBalance{creditor.FactionId}",
            CustomMinimumSize = new Vector2(90f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Text = Loc.Fmt("{0} 金", Number(creditor.Outstanding)),
        };
        row.AddChild(balance);
        _debtLabels[creditor.FactionId] = balance;

        var field = new SpinBox
        {
            Name = $"DebtAmount{creditor.FactionId}",
            MinValue = 0.0,
            MaxValue = MaxAmount,
            Step = 1.0,
            Value = Math.Min(creditor.Outstanding, MaxAmount),
            AllowGreater = true,
            CustomMinimumSize = new Vector2(FieldWidth, FieldHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
        };
        row.AddChild(field);
        _debtAmounts[creditor.FactionId] = field;
        _debtOrder.Add(creditor.FactionId);

        var pay = MakeButton($"PayDebt{creditor.FactionId}", Loc.T("还清"));
        var factionId = creditor.FactionId;
        pay.Pressed += () => OnPayDebtPressed(factionId);
        row.AddChild(pay);

        _debtRows.AddChild(row);
    }

    /// <summary>策略那一区：三颗按钮的按下态跟着世界走，下拉里是已登记的债主。</summary>
    private void RefreshPolicy(ArenaWorld world)
    {
        var policy = world.Town.Policy;
        _policyNone.ButtonPressed = policy == DebtPolicy.None;
        _policyAutoOrder.ButtonPressed = policy == DebtPolicy.AutoOrder;
        _policyAutoTarget.ButtonPressed = policy == DebtPolicy.AutoTarget;

        // 下拉里那份名单：第一项是「不指定」，后面是已登记的债主（按登记次序）。
        // 改名单时重摆，选中项按世界的优先债主归位。
        _syncing = true;
        _priority.Clear();
        _priority.AddItem(Loc.T("（不指定）"));
        foreach (var c in world.Town.Creditors)
        {
            _priority.AddItem(Loc.T(c.DisplayName));
        }

        var selected = 0;
        for (var i = 0; i < world.Town.Creditors.Count; i++)
        {
            if (world.Town.Creditors[i].FactionId == world.Town.PriorityFactionId) selected = i + 1;
        }

        if (_priority.ItemCount > 0) _priority.Selected = selected;
        _syncing = false;
    }

    /// <summary>发薪那一区：应发金额、欠薪天数；应发为 0 时「发薪」置灰。</summary>
    private void RefreshWages(ArenaWorld world)
    {
        _wages.Text = Loc.Fmt("应发 {0} 金 · 欠薪 {1} 天",
            Number(world.WagesDue),
            world.ArrearsDays.ToString(CultureInfo.InvariantCulture));
        _payWages.Disabled = !world.IsDocked || world.WagesDue <= 0.0;
    }

    // === 摆件 ===

    /// <summary>一个定价输入框（步长 0.1，范围取逻辑层那两档，不另写一遍）。</summary>
    private static SpinBox MakePricingField(string name) => new()
    {
        Name = name,
        MinValue = ArenaTuning.PricingMinMultiplier,
        MaxValue = ArenaTuning.PricingMaxMultiplier,
        Step = PricingStep,
        Value = 1.0,
        CustomMinimumSize = new Vector2(FieldWidth, FieldHeight),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        FocusMode = FocusModeEnum.None,
    };

    /// <summary>一行：左边一个标签，右边一个输入框。</summary>
    private static HBoxContainer MakeFieldRow(string name, string tag, SpinBox field)
    {
        var row = new HBoxContainer { Name = name };
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(new Label
        {
            Name = "Tag",
            Text = tag,
            CustomMinimumSize = new Vector2(150f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        row.AddChild(field);
        return row;
    }

    private static Button MakeButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        CustomMinimumSize = new Vector2(0f, ButtonHeight),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        FocusMode = FocusModeEnum.None,
    };

    /// <summary>策略那一排的按钮：按下即发 <c>SetDebtPolicy</c>；当前策略靠按下态高亮。</summary>
    private Button MakePolicyButton(string name, string text, DebtPolicy policy)
    {
        var button = MakeButton(name, text);
        button.ToggleMode = true;
        button.Pressed += () => OnPolicyPressed(policy);
        return button;
    }

    // === 小工具 ===

    /// <summary>下拉当前选中的债主 id（没选就写 <c>-</c>，那一档是「不指定」）。</summary>
    private string PriorityFactionId()
    {
        var factionId = FactionIdAt(_priority.Selected);
        return factionId ?? "-";
    }

    /// <summary>下拉里第 <paramref name="index"/> 项是哪一个债主（第 0 项是「不指定」，回 null）。</summary>
    private string? FactionIdAt(int index)
    {
        var world = _world();
        if (world is null) return null;
        if (index <= 0) return null;
        if (index - 1 >= world.Town.Creditors.Count) return null;

        return world.Town.Creditors[index - 1].FactionId;
    }

    /// <summary>世界当前那一档策略。</summary>
    private DebtPolicy PolicyOfWorld() => _world()?.Town.Policy ?? DebtPolicy.None;

    /// <summary>把一栏里的子控件摘下来（重摆那一区时用）。</summary>
    private static void ClearChildren(Node parent)
    {
        var children = new List<Node>();
        foreach (var child in parent.GetChildren()) children.Add(child);

        foreach (var child in children)
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>把一棵子树里的按钮一起置灰（负债那几行是动态建的，用不着逐个记字段）。</summary>
    private static void SetButtonsDisabled(Node node, bool disabled)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Button button) button.Disabled = disabled;
            SetButtonsDisabled(child, disabled);
        }
    }

    /// <summary>两份名单是不是同一个次序（债主那一区重摆的依据）。</summary>
    private static bool SameOrder(List<string> wanted, IEnumerable<string> current)
    {
        var index = 0;
        foreach (var id in current)
        {
            if (index >= wanted.Count || wanted[index] != id) return false;
            index++;
        }

        return index == wanted.Count;
    }

    /// <summary>负债那一区的读数签名（余额变了就重写那几行）。</summary>
    private static string DebtsSignature(ArenaWorld world)
    {
        var parts = new List<string>();
        foreach (var c in world.Town.Creditors)
        {
            if (c.Outstanding <= 0.0) continue;
            parts.Add($"{c.FactionId}:{c.Outstanding.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        return string.Join(",", parts);
    }

    /// <summary>读数上屏的数（不变文化，两位小数收尾）。</summary>
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // === 收尾 ===

    /// <summary>点面板外的空白处收起（面板内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
