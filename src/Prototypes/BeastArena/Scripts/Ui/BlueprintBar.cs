#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 底部蓝图条 —— 13 个固定槽位，按 <see cref="DeckBlueprints.Specs"/> 的次序排。
///
/// <para><b>买不起的槽位留空但不塌陷</b>：槽位本身按 <see cref="UiRoot.BlueprintSlotRect"/>
/// 摆在固定位置，买不起时只把文字置空、按钮置灰 —— 位置与宽高都不动，
/// 所以「按顺序数第几格」在画面上永远对得上规格表，不会因为钱少而把后面的格子往前挪。</para>
///
/// <para><b>点槽位进入建造模式</b>：本件只报「选中了哪一张蓝图」，幽灵与落座归
/// <see cref="BuildGhost"/>；再点同一格 = 退出。事件空串即退出（右键取消也走它）。</para>
/// </summary>
public partial class BlueprintBar : Control
{
    /// <summary>槽位数（与十三件建筑表同长）。</summary>
    public static int SlotCount => UiRoot.BlueprintSlotCount;

    private readonly Func<ArenaWorld?> _world;
    private readonly Button[] _slots = new Button[SlotCount];

    // 三份样式建一次、反复贴（每帧新建 StyleBox 会在引擎里堆垃圾）。
    private readonly StyleBoxFlat _styleAffordable = SlotStyle(chosen: false, affordable: true);
    private readonly StyleBoxFlat _styleEmpty = SlotStyle(chosen: false, affordable: false);
    private readonly StyleBoxFlat _styleSelected = SlotStyle(chosen: true, affordable: true);

    private string _selected = string.Empty;

    /// <summary>
    /// 上一次刷槽位时的「买得起位图 + 选中项」签名。<b>只在它变化时</b>重设文字、Tooltip 与样式 ——
    /// 13 个槽位每帧各写一次会让 Godot 每帧重排版，而这两件事与金币的具体数值无关，只与跨没跨过造价线有关。
    /// </summary>
    private string _signature = string.Empty;

    /// <summary>选中了哪一张蓝图（空串 = 没在建造模式）。</summary>
    public string SelectedBlueprintId => _selected;

    /// <summary>槽位被点中 / 取消（<b>空串 = 退出建造模式</b>）。</summary>
    public event Action<string>? BlueprintChosen;

    public BlueprintBar(UiRoot ui, Func<ArenaWorld?> world)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);

        _world = world;
        Name = "BlueprintBar";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        for (var i = 0; i < SlotCount; i++)
        {
            var spec = DeckBlueprints.Specs[i];
            var slot = new Button
            {
                Name = $"Slot{i}",
                FocusMode = FocusModeEnum.None,
                ClipText = true,
                Text = string.Empty,
                Disabled = true,
                MouseFilter = MouseFilterEnum.Stop,
            };
            slot.Pressed += () => OnSlotPressed(spec.Id);
            AddChild(slot);
            _slots[i] = slot;
        }

        ui.AddPanel(this);
    }

    /// <summary>清掉选中态（落座成功 / 右键取消之后由外面调）。</summary>
    public void ClearSelection() => _selected = string.Empty;

    public override void _Process(double delta)
    {
        var viewport = GetViewportRect().Size;
        var world = _world();
        var coin = world?.Town.Coin ?? 0.0;

        // 买得起 / 买不起只按造价线翻，不随金币的每一位数字变；选中项是另一个输入。
        var mask = new System.Text.StringBuilder(_slots.Length);
        for (var i = 0; i < _slots.Length; i++)
            mask.Append(world is not null && coin >= DeckBlueprints.Specs[i].CoinCost ? '1' : '0');

        var signature = mask.ToString() + "|" + _selected;
        var dirty = signature != _signature;
        _signature = signature;

        for (var i = 0; i < _slots.Length; i++)
        {
            var rect = UiRoot.BlueprintSlotRect(i, viewport);
            var slot = _slots[i];
            slot.Position = rect.Position;
            slot.Size = rect.Size;

            if (!dirty) continue;

            var spec = DeckBlueprints.Specs[i];
            var affordable = world is not null && coin >= spec.CoinCost;
            var chosen = _selected.Length > 0 && _selected == spec.Id;

            // 买不起 = 留空（不塌陷：位置与尺寸照旧），也点不动；造价挂在悬停提示上。
            slot.Disabled = !affordable;
            slot.Text = affordable ? Loc.T(spec.Name) : string.Empty;
            slot.TooltipText = spec.CoinCost.ToString(CultureInfo.InvariantCulture) + " " + Loc.T("金");
            slot.AddThemeStyleboxOverride("normal",
                chosen ? _styleSelected : affordable ? _styleAffordable : _styleEmpty);
            slot.AddThemeColorOverride("font_color",
                chosen ? new Color(1f, 0.88f, 0.45f) : new Color(0.90f, 0.92f, 0.95f));
        }
    }

    private void OnSlotPressed(string blueprintId)
    {
        // 点在买不起的槽位上不动选中态（按钮本身也置灰了，这一道是给合成输入兜的）。
        var world = _world();
        var spec = DeckBlueprints.SpecOf(blueprintId);
        if (world is null || spec is null || world.Town.Coin < spec.CoinCost) return;

        _selected = _selected == blueprintId ? string.Empty : blueprintId;
        BlueprintChosen?.Invoke(_selected);
    }

    private static StyleBoxFlat SlotStyle(bool chosen, bool affordable) => new()
    {
        BgColor = chosen
            ? new Color(0.28f, 0.24f, 0.12f, 0.95f)
            : new Color(0.12f, 0.13f, 0.17f, affordable ? 0.92f : 0.55f),
        BorderColor = chosen ? new Color(0.95f, 0.78f, 0.30f) : new Color(0.40f, 0.45f, 0.55f, 0.85f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 3f,
        ContentMarginRight = 3f,
        ContentMarginTop = 3f,
        ContentMarginBottom = 3f,
    };
}
