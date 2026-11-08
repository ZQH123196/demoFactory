#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 左下角选中面板 —— 选中一个人之后显示「名字与职业、血 / 情绪 / 忠心、岗位」，
/// 外加一颗「钉住 / 解除钉住」按钮与四行按钮：镇民那一行（转工人、转角斗、不参战、给自由）、
/// 法术与人事那一行（卖明星、治疗、遣散）。
///
/// <para><b>选中的状态只存 id</b>（<see cref="_selectedId"/>）：面板内容每帧从世界现读
/// （<see cref="ArenaWorld.Roster"/> 里找人）。存一份 <see cref="ArenaUnit"/> 副本的话，
/// 重开一局之后面板还攥着上一局那个人 —— 画面上读到的血量与岗位全是真的，只是不是这一局的。</para>
///
/// <para><b>id 在世界里消失就自动清空</b>：<see cref="ArenaUnit.LeftTown"/>、死亡（
/// <see cref="ArenaUnit.State"/> 为 <see cref="UnitState.Dead"/>）或换局查不到人，下一帧面板自己收起。</para>
///
/// <para><b>写路径只有一条</b>：所有按钮都发 <see cref="Main.Inject"/>（失败原因由它转给
/// <see cref="Banner"/>），面板自己不摸世界字段。转职按钮的参数形状照
/// <c>ArenaSetup.ConvertRole</c>（<c>id|Worker</c> / <c>id|Gladiator</c>），
/// 不参战开关照 <c>ArenaSetup.SetNonCombatant</c>（<c>id|0</c> / <c>id|1</c>）；
/// 给自由 / 卖明星 / 治疗 / 遣散那四个动词各要一段（人 id），走 <see cref="SendOne"/>。</para>
///
/// <para><b>非镇民不显示镇民那一行</b>（含帮工、游客、敌人与怪物）：<c>SetNonCombatant</c>
/// 只收在册镇民，转职对那一档也没有意义。整行按钮藏起来，不是置灰。
/// <b>钉住那一行不受这一格管</b>：任何活物都能钉。</para>
///
/// <para><b>帮工那一档只剩「遣散」一颗</b>：法术与人事那一行里，卖明星只对角斗士出现、
/// 治疗只对重伤出现、遣散只对帮工出现，三颗互斥居多，所以那一行不会挤。</para>
///
/// <para><b>卖明星恒显示（角斗士那一档）</b>：名声没到门槛时置灰，差额写在下面那一句说明里；
/// 按钮上写的报价由 <see cref="ArenaSetup.StarOffer"/> 给出 —— 面板不另算一遍那个式子。</para>
///
/// <para><b>钉住状态独立于选中</b>：点空地只清选中，钉住与跟随都留着。
/// 这一行显示的是<b>当前钉的是谁</b>，与当前选中的是谁无关。</para>
/// </summary>
public partial class SelectionPanel : Control
{
    /// <summary>面板内边距（像素）。</summary>
    public const float PanelPadding = 8f;

    /// <summary>按钮高度（像素）。</summary>
    public const float ButtonHeight = 26f;

    /// <summary>按钮之间的间距（像素）。</summary>
    public const float ButtonGap = 6f;

    /// <summary>那一句说明的高度（像素）—— 一行字。</summary>
    public const float NoteHeight = 20f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly Func<string> _pinnedId;
    private readonly Action<string> _togglePin;

    private readonly Label _title;
    private readonly Label _vitals;
    private readonly Label _post;
    private readonly HBoxContainer _actions;
    private readonly Button _toWorker;
    private readonly Button _toGladiator;
    private readonly Button _toggleCombat;
    private readonly Button _grantFreedom;
    private readonly HBoxContainer _pinRow;
    private readonly Button _pin;
    private readonly Label _pinned;

    /// <summary>法术与人事那一行（卖明星 / 治疗 / 遣散）。</summary>
    private readonly HBoxContainer _spells;

    private readonly Button _sellStar;
    private readonly Button _castHeal;
    private readonly Button _dismiss;

    /// <summary>那一行下面的一句说明（卖明星差多少名声就写在这儿）。</summary>
    private readonly Label _note;

    private string _selectedId = string.Empty;

    /// <summary>上一次刷新时的读数签名 —— 没变就不重写那几行文字（每帧新建格式化串是纯浪费）。</summary>
    private string _signature = string.Empty;

    /// <summary>面板当前该有多高（基线一份 + 多出来那几行）。</summary>
    private float _wantedHeight = UiRoot.SelectionPanelHeight;

    public SelectionPanel(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject,
        Func<string> pinnedId,
        Action<string> togglePin)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);
        ArgumentNullException.ThrowIfNull(pinnedId);
        ArgumentNullException.ThrowIfNull(togglePin);

        _world = world;
        _inject = inject;
        _pinnedId = pinnedId;
        _togglePin = togglePin;

        Name = "SelectionPanel";
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        // 左下角：左缘对齐屏幕边距，下缘让出底部蓝图条（与土壤面板同一条留空）。
        AnchorLeft = 0f;
        AnchorRight = 0f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = UiRoot.SelectionPanelLeftMargin;
        OffsetRight = UiRoot.SelectionPanelLeftMargin + UiRoot.SelectionPanelWidth;
        OffsetTop = -UiRoot.SelectionPanelBottomOffset - UiRoot.SelectionPanelHeight;
        OffsetBottom = -UiRoot.SelectionPanelBottomOffset;

        var box = new PanelContainer { Name = "Box" };
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.13f, 0.17f, 0.92f),
            BorderColor = new Color(0.45f, 0.50f, 0.60f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = PanelPadding,
            ContentMarginRight = PanelPadding,
            ContentMarginTop = PanelPadding,
            ContentMarginBottom = PanelPadding,
        });
        AddChild(box);

        var rows = new VBoxContainer { Name = "Rows" };
        rows.AddThemeConstantOverride("separation", 4);
        box.AddChild(rows);

        _title = MakeLabel("Title");
        rows.AddChild(_title);

        _vitals = MakeLabel("Vitals");
        rows.AddChild(_vitals);

        _post = MakeLabel("Post");
        rows.AddChild(_post);

        // 钉住那一行：按钮 + 「当前钉的是谁」。**独立于选中** —— 点空地只清选中，
        // 钉住与跟随都留着；这一行显示的是钉住的那一个，与当前选中的是谁无关。
        _pinRow = new HBoxContainer { Name = "PinRow" };
        _pinRow.AddThemeConstantOverride("separation", (int)ButtonGap);
        rows.AddChild(_pinRow);

        _pin = MakeButton("Pin", Loc.T("钉住"));
        _pin.Pressed += OnPinPressed;
        _pinRow.AddChild(_pin);

        _pinned = new Label
        {
            Name = "Pinned",
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _pinRow.AddChild(_pinned);

        _actions = new HBoxContainer { Name = "Actions" };
        _actions.AddThemeConstantOverride("separation", (int)ButtonGap);
        rows.AddChild(_actions);

        _toWorker = MakeButton("ToWorker", Loc.T("转工人"));
        _toWorker.Pressed += () => Send("ConvertRole", "Worker");
        _actions.AddChild(_toWorker);

        _toGladiator = MakeButton("ToGladiator", Loc.T("转角斗"));
        _toGladiator.Pressed += () => Send("ConvertRole", "Gladiator");
        _actions.AddChild(_toGladiator);

        _toggleCombat = MakeButton("ToggleCombat", Loc.T("不参战"));
        _toggleCombat.Pressed += OnToggleCombat;
        _actions.AddChild(_toggleCombat);

        // 「给自由」也在镇民那一行里：它是身份那一档的动作，和转职、不参战同属「这个人是谁」。
        _grantFreedom = MakeButton("GrantFreedom", Loc.T("给自由"));
        _grantFreedom.Pressed += OnGrantFreedomPressed;
        _actions.AddChild(_grantFreedom);

        // 法术与人事那一行：卖明星（角斗士）、治疗（重伤）、遣散（帮工）。
        // 三颗互斥居多，同一行放得下；帮工那一档只剩「遣散」这一颗。
        _spells = new HBoxContainer { Name = "Spells" };
        _spells.AddThemeConstantOverride("separation", (int)ButtonGap);
        rows.AddChild(_spells);

        _sellStar = MakeButton("SellStar", Loc.T("卖明星"));
        _sellStar.Pressed += OnSellStarPressed;
        _spells.AddChild(_sellStar);

        _castHeal = MakeButton("CastHeal", Loc.T("治疗 1 法力"));
        _castHeal.Pressed += OnCastHealPressed;
        _spells.AddChild(_castHeal);

        _dismiss = MakeButton("DismissHelper", Loc.T("遣散"));
        _dismiss.Pressed += OnDismissHelperPressed;
        _spells.AddChild(_dismiss);

        _note = MakeLabel("Note");
        rows.AddChild(_note);

        ui.AddPanel(this);
    }

    /// <summary>当前选中的人 id（空串 = 没选中）。后续 ticket（派岗、抓放）读它。</summary>
    public string SelectedId => _selectedId;

    /// <summary>选中一个人。传 null 等于取消选中。</summary>
    public void Select(ArenaUnit? unit) => _selectedId = unit?.Id ?? string.Empty;

    /// <summary>取消选中（点空地、人被移出世界、换局都走这一口）。</summary>
    public void Clear()
    {
        _selectedId = string.Empty;
        _signature = string.Empty;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var unit = ResolveSelected();
        if (unit is null)
        {
            Clear();
            return;
        }

        if (!Visible) Visible = true;

        var signature = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3:0.##}|{4:0.##}|{5:0.##}|{6}|{7}|{8}|{9}|{10:0.##}|{11:0.##}",
            unit.Id, unit.Role, unit.State, unit.Hp, unit.Mood, unit.Loyalty, unit.Post,
            unit.IsTownsperson, unit.NonCombatant, _pinnedId(),
            unit.PersonalReputation, unit.StatTotal);
        if (signature == _signature) return;
        _signature = signature;

        _title.Text = $"{unit.Id}  {RoleNameOf(unit.Role)}";
        _vitals.Text = Loc.Fmt("血 {0:0}/{1:0}  情绪 {2:0}  忠心 {3:0}",
            unit.Hp, ArenaTuning.UnitMaxHp, unit.Mood, unit.Loyalty);
        _post.Text = Loc.Fmt("岗位：{0}", PostNameOf(unit.Post));

        // 钉住那一行先刷：任何活物都能钉，所以它不受「是不是镇民」那一格管。
        UpdatePinRow(unit);

        // 非镇民（含帮工）三个按钮都不显示 —— 转职与不参战对那一档没有意义。
        _actions.Visible = unit.IsTownsperson;
        if (_actions.Visible)
        {
            _toWorker.Disabled = unit.Role == UnitRole.Worker;
            _toGladiator.Disabled = unit.Role == UnitRole.Gladiator;
            _toggleCombat.Text = unit.NonCombatant ? Loc.T("恢复参战") : Loc.T("不参战");

            // 已经是自由人的不用再给一次自由（动词那一档也会拒，但按钮不该亮着）。
            _grantFreedom.Visible = unit.Role != UnitRole.FreePerson;
        }

        UpdateSpells(unit);
        UpdateHeight();
    }

    /// <summary>
    /// 法术与人事那一行：<b>卖明星、治疗、遣散</b>三颗按选中的是谁出现。
    ///
    /// <para><b>卖明星恒显示（角斗士那一档）</b>：名声没到门槛时置灰并把差额写在下面那一行 ——
    /// 报价由 <see cref="ArenaSetup.StarOffer"/> 给出（面板不另算一遍那个式子）。
    /// <b>治疗只在重伤时出现</b>，按钮上写着那一发的法力代价。
    /// <b>遣散只在帮工时出现</b>：帮工那一档整块面板只剩这一颗。</para>
    /// </summary>
    private void UpdateSpells(ArenaUnit unit)
    {
        var note = string.Empty;

        _sellStar.Visible = unit.Role == UnitRole.Gladiator;
        if (_sellStar.Visible)
        {
            var price = ArenaSetup.StarOffer(unit);
            _sellStar.Text = Loc.Fmt("卖明星 {0} 金", price.ToString("0.##", CultureInfo.InvariantCulture));

            var missing = ArenaTuning.StarSellReputationFloor - unit.PersonalReputation;
            _sellStar.Disabled = missing > 0.0;
            if (missing > 0.0)
            {
                note = Loc.Fmt("还差 {0} 名声才够门槛（要 {1}）",
                    missing.ToString("0.##", CultureInfo.InvariantCulture),
                    ArenaTuning.StarSellReputationFloor.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        _castHeal.Visible = unit.State == UnitState.Wounded;

        _dismiss.Visible = unit.IsHelper;

        _spells.Visible = _sellStar.Visible || _castHeal.Visible || _dismiss.Visible;
        _note.Text = note;
        _note.Visible = note.Length > 0;
    }

    /// <summary>
    /// 面板高度：基线一份（<see cref="UiRoot.SelectionPanelHeight"/>），
    /// 多出来的那一行（法术与人事那一行）与那一句说明按实际长出来 —— 面板从下缘往上长。
    /// </summary>
    private void UpdateHeight()
    {
        var extra = 0f;
        if (_spells.Visible) extra += ButtonHeight + ButtonGap;
        if (_note.Visible) extra += NoteHeight + ButtonGap;

        var wanted = UiRoot.SelectionPanelHeight + extra;
        if (Math.Abs(wanted - _wantedHeight) < 0.5f) return;

        _wantedHeight = wanted;
        OffsetTop = -UiRoot.SelectionPanelBottomOffset - wanted;
    }

    /// <summary>
    /// 钉住那一行：按钮在「钉住」与「解除钉住」之间翻（翻不翻看的是**选中的是不是钉住的那一个**），
    /// 右边那一句显示**当前钉的是谁**（与当前选中的是谁无关 —— 钉住状态独立于选中）。
    /// </summary>
    private void UpdatePinRow(ArenaUnit unit)
    {
        var pinnedId = _pinnedId();
        var isPinned = pinnedId.Length > 0 && string.Equals(pinnedId, unit.Id, StringComparison.Ordinal);

        _pin.Text = isPinned ? Loc.T("解除钉住") : Loc.T("钉住");
        _pinned.Text = pinnedId.Length == 0
            ? string.Empty
            : Loc.Fmt("钉住：{0}", PinnedNameOf(pinnedId));
    }

    /// <summary>
    /// 钉住的那一个叫什么（id + 职业）。查不到就只报 id —— 不藏事实。
    /// </summary>
    private string PinnedNameOf(string unitId)
    {
        var unit = _world()?.Roster.Find(unitId);
        return unit is null ? unitId : $"{unit.Id}  {RoleNameOf(unit.Role)}";
    }

    /// <summary>点「钉住 / 解除钉住」：目标是当前选中的那一个。</summary>
    private void OnPinPressed()
    {
        var unit = ResolveSelected();
        if (unit is null) return;
        _togglePin(unit.Id);
    }

    /// <summary>选中的那个人现在还在不在世界里（不在就回 null，面板下一帧自己收起）。</summary>
    private ArenaUnit? ResolveSelected()
    {
        if (_selectedId.Length == 0) return null;
        var unit = _world()?.Roster.Find(_selectedId);
        if (unit is null) return null;
        if (unit.LeftTown || unit.State == UnitState.Dead) return null;
        return unit;
    }

    private void OnToggleCombat()
    {
        var unit = ResolveSelected();
        if (unit is null) return;
        Send("SetNonCombatant", unit.NonCombatant ? "0" : "1");
    }

    /// <summary>「给自由」：一发 <c>GrantFreedom|人 id</c>。</summary>
    private void OnGrantFreedomPressed() => SendOne("GrantFreedom");

    /// <summary>「卖明星」：一发 <c>SellStar|人 id</c>（报价由 <see cref="ArenaSetup.StarOffer"/> 给出）。</summary>
    private void OnSellStarPressed() => SendOne("SellStar");

    /// <summary>「治疗」：一发 <c>CastHeal|人 id</c>（花 1 格法力）。</summary>
    private void OnCastHealPressed() => SendOne("CastHeal");

    /// <summary>「遣散」：一发 <c>DismissHelper|帮工 id</c>（归还那一格法力）。</summary>
    private void OnDismissHelperPressed() => SendOne("DismissHelper");

    /// <summary>
    /// 发一发只带一个 id 的写命令 —— <c>GrantFreedom</c> / <c>SellStar</c> / <c>CastHeal</c> /
    /// <c>DismissHelper</c> 这四个动词各要一段（人 id），多一段会被词表判成参数不对。
    /// </summary>
    private void SendOne(string verb)
    {
        var unit = ResolveSelected();
        if (unit is null) return;
        _inject(verb, unit.Id);
    }

    /// <summary>发一发写命令 —— 目标 id 取当下选中的那一格，不在册就什么都不发。</summary>
    private void Send(string verb, string argument)
    {
        var unit = ResolveSelected();
        if (unit is null) return;
        _inject(verb, $"{unit.Id}|{argument}");
    }

    /// <summary>职业那一格的中文名（不显示枚举原文）。</summary>
    public static string RoleNameOf(UnitRole role) => role switch
    {
        UnitRole.Worker => Loc.T("工人"),
        UnitRole.Gladiator => Loc.T("角斗士"),
        UnitRole.FreePerson => Loc.T("自由人"),
        UnitRole.Helper => Loc.T("帮工"),
        UnitRole.Enemy => Loc.T("敌人"),
        UnitRole.Monster => Loc.T("怪物"),
        _ => Loc.T("游客"),
    };

    /// <summary>
    /// 岗位那一格的中文名。<see cref="ArenaUnit.Post"/> 存的是<b>建筑 id</b>（<c>restaurant</c>），
    /// 先经建造册翻成蓝图 id，再取蓝图表里的中文名；翻不出来就原样印 id，不藏事实。
    /// </summary>
    private string PostNameOf(string post)
    {
        if (post.Length == 0) return Loc.T("无");
        if (post.StartsWith("soil:", StringComparison.Ordinal)) return Loc.T("土壤");

        var blueprintId = _world()?.Construction.Find(post)?.BlueprintId ?? post;
        return DeckBlueprints.SpecOf(blueprintId)?.Name ?? post;
    }

    private static Label MakeLabel(string name) => new()
    {
        Name = name,
        MouseFilter = MouseFilterEnum.Ignore,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    private static Button MakeButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        CustomMinimumSize = new Vector2(0f, ButtonHeight),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        FocusMode = FocusModeEnum.None,
    };
}
