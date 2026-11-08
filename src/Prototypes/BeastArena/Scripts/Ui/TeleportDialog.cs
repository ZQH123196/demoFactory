#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 传送弹层 —— 右键传送阵开它（<b>只在停靠期</b>）。三块：人名单、矿石与材料、费用预览。
///
/// <para><b>人名单只列镇民</b>：帮工、游客、敌人一列都不列（判据在
/// <see cref="ArenaWorld.Townspersons"/> 那一口，与逻辑层 <c>Teleport</c> 的守卫同一处）。
/// 名单每帧现读世界 —— 有人离镇或被卖掉了，那一行自己消失。</para>
///
/// <para><b>费用预览</b>：人 {每人一价} 金、货 {每件一价} 金、小货上限 {上限} 件 ——
/// 三个数都取 <see cref="ArenaTuning"/>，弹层不另写一份。</para>
///
/// <para><b>写路径只有一条</b>：一发 <c>Teleport|人 id 列表|矿石|材料</c>，
/// 走 <see cref="Main.Inject"/>（进 Journal、能重放）。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清。</para>
/// </summary>
public partial class TeleportDialog : Control, IModalCloseable
{
    /// <summary>弹层宽度（像素）。</summary>
    public const float PanelWidth = 460f;

    /// <summary>行间距（像素）。</summary>
    public const float RowGap = 5f;

    /// <summary>按钮高度（像素）。</summary>
    public const float ButtonHeight = 28f;

    /// <summary>数字输入框的宽度（像素）。</summary>
    public const float FieldWidth = 120f;

    /// <summary>数字输入框的高度（像素）。</summary>
    public const float FieldHeight = 28f;

    /// <summary>面板内边距（像素）。</summary>
    public const float Padding = 12f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;

    private readonly ItemList _people;
    private readonly SpinBox _ore;
    private readonly SpinBox _material;
    private readonly Label _cost;
    private readonly Button _teleport;

    /// <summary>名单上每一行对应的人 id（与 <see cref="_people"/> 的行号一一对应）。</summary>
    private readonly List<string> _peopleIds = [];

    /// <summary>名单上一次摆的是谁（变了才重摆那几行）。</summary>
    private string _shownIds = string.Empty;

    public TeleportDialog(
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

        Name = "TeleportDialog";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;

        var box = new PanelContainer { Name = "Box" };
        box.AnchorLeft = 0.5f;
        box.AnchorRight = 0.5f;
        box.AnchorTop = 0.5f;
        box.AnchorBottom = 0.5f;
        box.OffsetLeft = -PanelWidth / 2f;
        box.OffsetRight = PanelWidth / 2f;
        box.OffsetTop = -200f;
        box.OffsetBottom = 200f;
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
            Text = Loc.T("传送"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        rows.AddChild(new Label
        {
            Name = "PeopleTitle",
            Text = Loc.T("人（只列镇民，可多选）"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _people = new ItemList
        {
            Name = "People",
            SelectMode = ItemList.SelectModeEnum.Multi,
            CustomMinimumSize = new Vector2(0f, 160f),
            FocusMode = FocusModeEnum.None,
        };
        _people.ItemSelected += OnPeopleChanged;
        rows.AddChild(_people);

        _ore = MakeField("Ore");
        _ore.ValueChanged += OnFieldChanged;
        rows.AddChild(MakeFieldRow("OreRow", Loc.T("矿石"), _ore));

        _material = MakeField("Material");
        _material.ValueChanged += OnFieldChanged;
        rows.AddChild(MakeFieldRow("MaterialRow", Loc.T("材料"), _material));

        _cost = new Label
        {
            Name = "Cost",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rows.AddChild(_cost);

        _teleport = MakeButton("Teleport", Loc.T("传送"));
        _teleport.Pressed += OnTeleportPressed;
        rows.AddChild(_teleport);

        var close = MakeButton("Close", Loc.T("关闭"));
        close.Pressed += Close;
        rows.AddChild(close);

        ui.AddPopup(this);
    }

    /// <summary>弹层开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开传送弹层 —— <b>不在停靠期不开</b>（传送那一发也只在停靠期发得出去）。</summary>
    public void Open()
    {
        if (_world() is not { IsDocked: true }) return;

        _shownIds = string.Empty;
        Refresh();
        Visible = true;
        _ui.EnterModal(this);
    }

    /// <summary>关掉传送弹层并摘掉自己那一份模态（世界照常收拾取）。</summary>
    public void Close()
    {
        Visible = false;
        _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // 拔锚离港了：弹层自己收起来（传送是停靠期的事）。
        if (_world() is not { IsDocked: true })
        {
            Close();
            return;
        }

        Refresh();
    }

    // === 写路径 ===

    /// <summary>「传送」：一发 <c>Teleport|人 id 列表|矿石|材料</c>。</summary>
    private void OnTeleportPressed()
    {
        var ids = SelectedIds();
        var ore = (int)_ore.Value;
        var material = (int)_material.Value;

        _inject("Teleport", string.Join('|',
            ids.Count > 0 ? string.Join(',', ids) : "-",
            ore.ToString(CultureInfo.InvariantCulture),
            material.ToString(CultureInfo.InvariantCulture)));
        Refresh();
    }

    /// <summary>名单上选中的人 id（按名单次序）。</summary>
    private List<string> SelectedIds()
    {
        var ids = new List<string>();
        foreach (var index in _people.GetSelectedItems())
        {
            if (index < 0 || index >= _peopleIds.Count) continue;
            ids.Add(_peopleIds[index]);
        }

        return ids;
    }

    private void OnPeopleChanged(long index) => RefreshCost();

    /// <summary>两个数量输入框改了值：费用预览跟着变。</summary>
    private void OnFieldChanged(double value) => RefreshCost();

    // === 每帧派生 ===

    /// <summary>按世界现况重算名单、费用预览与那颗「传送」的置灰。</summary>
    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        RefreshPeople(world);
        RefreshCost();
    }

    /// <summary>人名单：只列在册镇民（判据在 <see cref="ArenaWorld.Townspersons"/> 那一口）。</summary>
    private void RefreshPeople(ArenaWorld world)
    {
        var townspersons = world.Townspersons;

        var signature = string.Empty;
        foreach (var unit in townspersons) signature += unit.Id + ",";
        if (signature == _shownIds) return;

        _shownIds = signature;
        _people.Clear();
        _peopleIds.Clear();

        foreach (var unit in townspersons)
        {
            _people.AddItem($"{unit.Id}  {SelectionPanel.RoleNameOf(unit.Role)}");
            _peopleIds.Add(unit.Id);
        }
    }

    /// <summary>费用预览与「传送」的置灰（三个数都取 <see cref="ArenaTuning"/>）。</summary>
    private void RefreshCost()
    {
        var people = SelectedIds().Count;
        var ore = (int)_ore.Value;
        var material = (int)_material.Value;
        var goods = ore + material;

        var total = (people * ArenaTuning.TeleportCoinPerPerson)
            + (goods * ArenaTuning.TeleportCoinPerSmallGood);

        _cost.Text = Loc.Fmt("费用 {0} 金（人 {1} × {2} · 货 {3} × {4}）· 小货上限 {5} 件",
            total.ToString("0.##", CultureInfo.InvariantCulture),
            people.ToString(CultureInfo.InvariantCulture),
            ArenaTuning.TeleportCoinPerPerson.ToString("0.##", CultureInfo.InvariantCulture),
            goods.ToString(CultureInfo.InvariantCulture),
            ArenaTuning.TeleportCoinPerSmallGood.ToString("0.##", CultureInfo.InvariantCulture),
            ArenaTuning.TeleportMaxSmallGoods.ToString(CultureInfo.InvariantCulture));

        // 一样都没选就没有可传的；小货超过上限那一发会被词表按参数不对拒掉。
        _teleport.Disabled = people == 0 && goods == 0;
        _teleport.Disabled = _teleport.Disabled || goods > ArenaTuning.TeleportMaxSmallGoods;
    }

    // === 摆件 ===

    /// <summary>一个数量输入框（步长 1；上限取小货那一条档，允许填超 —— 填超时按钮置灰）。</summary>
    private static SpinBox MakeField(string name) => new()
    {
        Name = name,
        MinValue = 0.0,
        MaxValue = ArenaTuning.TeleportMaxSmallGoods,
        Step = 1.0,
        Value = 0.0,
        AllowGreater = true,
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
            CustomMinimumSize = new Vector2(120f, 0f),
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
}
