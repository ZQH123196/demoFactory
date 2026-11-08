#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 市场面板 —— 上半「空艇成交」，下半「补给采购」。
///
/// <para><b>它是独立面板，不是停靠弹层</b>：停靠弹层只负责选一个目的地（那是选路），
/// 这一件管的是买卖钱粮（那是经营）。两者各开各的，不共用一层。</para>
///
/// <para><b>两个买食物入口都在这一屏上</b>，价格各自写清：「空艇价」按三城均价 × 空艇加价
/// （<see cref="ArenaSetup.AirshipFoodPrice"/>），「本地价」按所在城物价系数
/// （<see cref="ArenaSetup.FoodPriceOf"/>）。<b>面板上写的价与成交那一发扣的价是同一个数</b> ——
/// 两边都取 <see cref="ArenaSetup"/> 那两个口，不各算一遍。</para>
///
/// <para><b>不在空艇成交窗口内，上半区整块置灰</b>并写「本段没有空艇」；
/// 下半区（补给采购）照常可用 —— 那是城里的买卖，与空艇到不到无关。</para>
///
/// <para><b>写路径只有一条</b>：「成交」发 <c>TradeAirship|买食物|卖矿石|卖材料</c>，
/// 「采购」发 <c>BuySupply|食物数</c>，都走 <see cref="Main.Inject"/>（进 Journal、能重放）。
/// 面板自己不改世界字段。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清 —— 收起之后世界照常收拾取。</para>
/// </summary>
public partial class MarketPanel : Control, IModalCloseable
{
    /// <summary>面板宽度（像素）。</summary>
    public const float PanelWidth = 460f;

    /// <summary>面板高度（像素）。内容比它高时按最小尺寸长出去。</summary>
    public const float PanelHeight = 480f;

    /// <summary>行间距（像素）。</summary>
    public const float RowGap = 5f;

    /// <summary>数字输入框的宽度（像素）。</summary>
    public const float FieldWidth = 140f;

    /// <summary>数字输入框的高度（像素）。</summary>
    public const float FieldHeight = 28f;

    /// <summary>按钮高度（像素）。</summary>
    public const float ButtonHeight = 30f;

    /// <summary>数量输入框的上限（件）。够买几十万份，超出就没有意义。</summary>
    public const double MaxAmount = 999999.0;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;
    private readonly bool _embedded;

    private readonly Label _readout;
    private readonly Label _dockHint;
    private readonly Label _airshipPrice;
    private readonly Label _localPrice;
    private readonly Label _window;
    private readonly SpinBox _buyFood;
    private readonly SpinBox _sellOre;
    private readonly SpinBox _sellMaterial;
    private readonly SpinBox _sellCrystalCount;
    private readonly SpinBox _supplyFood;
    private readonly Button _trade;
    private readonly Button _buy;
    private readonly Button _sellCrystal;

    /// <summary>上一次刷新时的读数签名 —— 没变就不重写文字（每帧刷会让 Godot 每帧重排版一次）。</summary>
    private string _signature = string.Empty;

    public MarketPanel(
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

        Name = "MarketPanel";
        MouseFilter = MouseFilterEnum.Stop;

        if (embedded)
        {
            // 嵌在「经营」的分栏里：自己是 TabContainer 的一页，不再铺满视口、不再自己置模态。
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

        // 嵌在「经营」分栏里时让内容自己滚：分栏高度由经营弹层定，市场这一页比它高时不被裁掉。
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
            Text = Loc.T("市场"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 停靠期提示：不在停靠期时只留空艇成交那一半可做。
        _dockHint = new Label
        {
            Name = "DockHint",
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        };
        _dockHint.AddThemeFontSizeOverride("font_size", 12);
        rows.AddChild(_dockHint);

        // 读数行常显：金币 / 食物 / 矿石 / 特殊材料 / 魔晶。
        _readout = new Label
        {
            Name = "Readout",
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        rows.AddChild(_readout);

        // 两个买食物入口的价格并列写在一行：左边空艇价，右边本地价。
        var prices = new HBoxContainer { Name = "Prices" };
        prices.AddThemeConstantOverride("separation", 16);
        rows.AddChild(prices);

        _airshipPrice = new Label
        {
            Name = "AirshipPrice",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        prices.AddChild(_airshipPrice);

        _localPrice = new Label
        {
            Name = "LocalPrice",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        prices.AddChild(_localPrice);

        rows.AddChild(new HSeparator { Name = "AirshipSeparator" });

        rows.AddChild(new Label
        {
            Name = "AirshipTitle",
            Text = Loc.T("空艇成交"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 剩余窗口时间；不在窗口内时这一行改写「本段没有空艇」。
        _window = new Label
        {
            Name = "Window",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rows.AddChild(_window);

        _buyFood = MakeField("BuyFood");
        rows.AddChild(MakeFieldRow("BuyFoodRow", Loc.T("买食物（空艇价）"), _buyFood));

        _sellOre = MakeField("SellOre");
        rows.AddChild(MakeFieldRow("SellOreRow", Loc.T("卖矿石"), _sellOre));

        _sellMaterial = MakeField("SellMaterial");
        rows.AddChild(MakeFieldRow("SellMaterialRow", Loc.T("卖材料"), _sellMaterial));

        _trade = MakeButton("Trade", Loc.T("成交"));
        _trade.Pressed += OnTradePressed;
        rows.AddChild(_trade);

        rows.AddChild(new HSeparator { Name = "SupplySeparator" });

        rows.AddChild(new Label
        {
            Name = "SupplyTitle",
            Text = Loc.T("补给采购"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _supplyFood = MakeField("SupplyFood");
        rows.AddChild(MakeFieldRow("SupplyFoodRow", Loc.T("买食物（本地价）"), _supplyFood));

        _buy = MakeButton("Buy", Loc.T("采购"));
        _buy.Pressed += OnBuyPressed;
        rows.AddChild(_buy);

        rows.AddChild(new HSeparator { Name = "CrystalSeparator" });

        rows.AddChild(new Label
        {
            Name = "CrystalTitle",
            Text = Loc.T("魔晶出售"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _sellCrystalCount = MakeField("SellCrystalCount");
        rows.AddChild(MakeFieldRow("SellCrystalRow", Loc.T("出售魔晶"), _sellCrystalCount));

        _sellCrystal = MakeButton("SellCrystal", Loc.T("出售魔晶"));
        _sellCrystal.Pressed += OnSellCrystalPressed;
        rows.AddChild(_sellCrystal);

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

    /// <summary>打开市场面板（每次都按世界现况重算置灰与读数）。</summary>
    public void Open()
    {
        if (_world() is null) return;

        _signature = string.Empty;
        Refresh();
        Visible = true;
        if (!_embedded) _ui.EnterModal(this);
    }

    /// <summary>关掉市场面板并摘掉自己那一份模态（世界照常收拾取）。</summary>
    public void Close()
    {
        Visible = false;
        if (!_embedded) _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // 独立弹层那一档：世界可能拔锚离城了，面板自己收起来。
        // 嵌在「经营」分栏里时不收 —— 分栏要留在原地，只在页内写「停靠期才能买卖」。
        if (!_embedded && _world() is not { IsDocked: true })
        {
            Close();
            return;
        }

        Refresh();
    }

    // === 写路径 ===

    /// <summary>「成交」：三段数量现读这一格的值，一发 <c>TradeAirship</c>。</summary>
    private void OnTradePressed()
    {
        var world = _world();
        if (world is null || !world.InAirshipWindow) return;

        var args = string.Join('|',
            AmountOf(_buyFood).ToString(CultureInfo.InvariantCulture),
            AmountOf(_sellOre).ToString(CultureInfo.InvariantCulture),
            AmountOf(_sellMaterial).ToString(CultureInfo.InvariantCulture));
        _inject("TradeAirship", args);
        Refresh();
    }

    /// <summary>「采购」：一段食物数现读这一格的值，一发 <c>BuySupply</c>。</summary>
    private void OnBuyPressed()
    {
        var food = AmountOf(_supplyFood);
        if (food <= 0) return;

        _inject("BuySupply", food.ToString(CultureInfo.InvariantCulture));
        Refresh();
    }

    /// <summary>「出售魔晶」：一段数量现读这一格的值，一发 <c>SellCrystal</c>。</summary>
    private void OnSellCrystalPressed()
    {
        var count = AmountOf(_sellCrystalCount);
        if (count <= 0) return;

        _inject("SellCrystal", count.ToString(CultureInfo.InvariantCulture));
        Refresh();
    }

    // === 每帧派生 ===

    /// <summary>按世界现况重算读数、价格与上半区的置灰。</summary>
    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        var signature = string.Format(CultureInfo.InvariantCulture,
            "{0}|{1:0.##}|{2:0.##}|{3:0.##}|{4:0.##}|{5:0.##}|{6:0.##}|{7}|{8}|{9}|{10}|{11}",
            world.Town.Coin, world.Town.Food, world.Town.Ore, world.Town.SpecialMaterial,
            world.Town.Crystal, world.CrystalWarehouse, world.CrystalUnitPrice,
            world.InAirshipWindow, ArenaSetup.AirshipMinutesLeft(world), world.CityPriceFactor,
            world.IsDocked, (int)world.Battle.Occasion);
        if (signature == _signature) return;
        _signature = signature;

        _readout.Text = Loc.Fmt("金币 {0} · 食物 {1} · 矿石 {2} · 特殊材料 {3} · 魔晶 {4}（每单位 {5} 金）",
            Number(world.Town.Coin), Number(world.Town.Food),
            Number(world.Town.Ore), Number(world.Town.SpecialMaterial),
            world.CrystalWarehouse.ToString(CultureInfo.InvariantCulture),
            Number(world.CrystalUnitPrice));

        // 两个价都取 ArenaSetup 那两个口 —— 面板上写的与成交那一发扣的是同一个数。
        _airshipPrice.Text = Loc.Fmt("空艇价 {0} 金/份", Number(ArenaSetup.AirshipFoodPrice));
        _localPrice.Text = Loc.Fmt("本地价 {0} 金/份", Number(ArenaSetup.FoodPriceOf(world.CityPriceFactor)));

        var inWindow = world.InAirshipWindow;
        _window.Text = inWindow
            ? Loc.Fmt("剩余 {0} 游戏分钟",
                ArenaSetup.AirshipMinutesLeft(world).ToString(CultureInfo.InvariantCulture))
            : Loc.T("本段没有空艇");

        // 不在停靠期时只留空艇成交可做；补给采购与魔晶出售都按停靠期那一档置灰。
        _dockHint.Visible = !world.IsDocked;
        _dockHint.Text = world.IsDocked
            ? string.Empty
            : Loc.T("不在停靠期：只做得了空艇成交，补给采购与魔晶出售要停靠");

        // 窗口外：上半区整块置灰（输入框不可改、成交按钮点不动）。
        _buyFood.Editable = inWindow;
        _sellOre.Editable = inWindow;
        _sellMaterial.Editable = inWindow;
        _trade.Disabled = !inWindow;

        _supplyFood.Editable = world.IsDocked;
        _buy.Disabled = !world.IsDocked;

        _sellCrystalCount.Editable = world.IsDocked || inWindow;
        _sellCrystal.Disabled = world.CrystalWarehouse <= 0 || !(world.IsDocked || inWindow);
        _sellCrystal.TooltipText = _sellCrystal.Disabled
            ? world.CrystalWarehouse <= 0
                ? Loc.T("仓库里没有魔晶")
                : Loc.T("停靠期或空艇成交窗口内才卖得出去")
            : string.Empty;
    }

    // === 摆件 ===

    /// <summary>一个数量输入框（步长 1，不许小数 —— 逻辑层那一侧只收整数）。</summary>
    private static SpinBox MakeField(string name) => new()
    {
        Name = name,
        MinValue = 0.0,
        MaxValue = MaxAmount,
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

    /// <summary>输入框里的数取整（向下取整：数量不按小数计）。</summary>
    private static int AmountOf(SpinBox field) => (int)Math.Floor(field.Value);

    /// <summary>读数上屏的数（不变文化，两位小数收尾）。</summary>
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>点面板外的空白处收起（面板内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
