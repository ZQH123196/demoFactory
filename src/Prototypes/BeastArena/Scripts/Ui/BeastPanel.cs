#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 巨兽面板 —— 点兽身打开。三个区：位阶、形态、喂食。
///
/// <para><b>写路径只有一条</b>：进阶发 <c>AdvanceTier</c>、切形态发 <c>SwitchForm|形态</c>、
/// 喂食发 <c>Feed|50</c>，都走 <see cref="Main.Inject"/>（进 Journal、能重放）。
/// 面板自己不改世界字段 —— 读数每帧现读世界。</para>
///
/// <para><b>模态但时间照走</b>：开面板时 <see cref="UiRoot.EnterModal"/> 记一份模态
/// （世界不再收拾取），<b>但不碰世界的暂停</b> —— 巨兽照常航行、仪式照常走完。
/// 关面板时 <see cref="UiRoot.ExitModal"/> 摘掉自己那一份，世界恢复拾取。</para>
///
/// <para><b>按钮的置灰与原因</b>：进阶那一颗的判定顺序取自
/// <see cref="ArenaSetup.AdvanceBlockerOf"/>（<see cref="ArenaWorld.AdvanceTier"/> 的只读镜像），
/// 差多少写在按钮下面那一行 —— 差魂石、差材料、差金币都写数字，不写一句笼统的「不够」。</para>
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清。</para>
/// </summary>
public partial class BeastPanel : Control, IModalCloseable
{
    /// <summary>面板宽度（像素）。</summary>
    public const float PanelWidth = 480f;

    /// <summary>面板高度（像素）。内容比它高时按最小尺寸长出去。</summary>
    public const float PanelHeight = 660f;

    /// <summary>行间距（像素）。</summary>
    public const float RowGap = 5f;

    /// <summary>按钮高度（像素）。</summary>
    public const float ButtonHeight = 30f;

    /// <summary>一次喂多少食物（份）。设计口径：50 食物换 10 点饱食度（5:1），饱食封顶 100。</summary>
    public const int FeedAmount = 50;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly UiRoot _ui;

    private readonly Label _tier;
    private readonly Label _gate;
    private readonly Label _cost;
    private readonly Label _ritual;
    private readonly Button _advance;
    private readonly Label _advanceHint;

    private readonly Label _form;
    private readonly Button _formOriginal;
    private readonly Button _formDesert;
    private readonly Button _formGlutton;
    private readonly Label _formHint;

    private readonly Label _feed;
    private readonly Button _feedButton;
    private readonly Label _feedHint;
    private readonly Button _npcFeedButton;
    private readonly Label _npcFeedHint;

    // === 魔晶用途 ===

    private readonly Label _crystal;
    private readonly Button _manaButton;
    private readonly OptionButton _healTarget;
    private readonly Button _healButton;
    private readonly OptionButton _repairTarget;
    private readonly Button _repairButton;
    private readonly Label _crystalHint;

    private readonly List<string> _healOrder = [];
    private readonly List<string> _repairOrder = [];

    /// <summary>上一次刷新时的读数签名 —— 没变就不重写文字（每帧刷会让 Godot 每帧重排版一次）。</summary>
    private string _signature = string.Empty;

    public BeastPanel(
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

        Name = "BeastPanel";
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

        // 标题与「关闭」钉在框上，中间那一叠读数可滚 —— ticket 07 加了魔晶用途组之后
        // 内容比 660 高，不滚的话最后那几行（含关闭）会被挤出面板。
        var outer = new VBoxContainer { Name = "Rows" };
        outer.AddThemeConstantOverride("separation", (int)RowGap);
        panel.AddChild(outer);

        outer.AddChild(new Label
        {
            Name = "Title",
            Text = Loc.T("巨兽"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        outer.AddChild(scroll);

        var rows = new VBoxContainer { Name = "Body", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", (int)RowGap);
        scroll.AddChild(rows);

        // === 位阶 ===

        rows.AddChild(new Label
        {
            Name = "TierTitle",
            Text = Loc.T("位阶"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _tier = MakeLabel("Tier");
        rows.AddChild(_tier);

        // 门槛那一行：写清已达与未达的是哪几个象限、哪一层。
        // 逐象限的深度不再在这里重复（spec「删除巨兽面板与土壤面板重复的象限深度」，
        // 八格状态只归土壤面板一处）。
        _gate = MakeLabel("Gate");
        rows.AddChild(_gate);

        _cost = MakeLabel("Cost");
        rows.AddChild(_cost);

        _ritual = MakeLabel("Ritual");
        rows.AddChild(_ritual);

        _advance = MakeButton("Advance", Loc.T("进阶"));
        _advance.Pressed += OnAdvancePressed;
        rows.AddChild(_advance);

        _advanceHint = MakeLabel("AdvanceHint");
        rows.AddChild(_advanceHint);

        rows.AddChild(new HSeparator { Name = "FormSeparator" });

        // === 形态 ===

        rows.AddChild(new Label
        {
            Name = "FormTitle",
            Text = Loc.T("形态"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _form = MakeLabel("Form");
        rows.AddChild(_form);

        var formRow = new HBoxContainer { Name = "FormButtons" };
        formRow.AddThemeConstantOverride("separation", 8);
        rows.AddChild(formRow);

        _formOriginal = MakeFormButton("FormOriginal", Loc.T("原始态"), BeastForm.Original);
        formRow.AddChild(_formOriginal);

        _formDesert = MakeFormButton("FormDesert", Loc.T("荒漠态"), BeastForm.Desert);
        formRow.AddChild(_formDesert);

        _formGlutton = MakeFormButton("FormGlutton", Loc.T("暴食态"), BeastForm.Glutton);
        formRow.AddChild(_formGlutton);

        _formHint = MakeLabel("FormHint");
        rows.AddChild(_formHint);

        rows.AddChild(new HSeparator { Name = "FeedSeparator" });

        // === 喂食 ===

        rows.AddChild(new Label
        {
            Name = "FeedTitle",
            Text = Loc.T("喂食"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _feed = MakeLabel("Feed");
        rows.AddChild(_feed);

        _feedButton = MakeButton("FeedButton", Loc.Fmt("喂 {0} 食物", FeedAmount));
        _feedButton.Pressed += OnFeedPressed;
        rows.AddChild(_feedButton);

        _feedHint = MakeLabel("FeedHint");
        rows.AddChild(_feedHint);

        _npcFeedButton = MakeButton("NpcFeedButton",
            Loc.Fmt("让工人喂 {0} 食物", ArenaTuning.NpcFeedFoodAmount));
        _npcFeedButton.Pressed += OnNpcFeedPressed;
        rows.AddChild(_npcFeedButton);

        _npcFeedHint = MakeLabel("NpcFeedHint");
        rows.AddChild(_npcFeedHint);

        rows.AddChild(new HSeparator { Name = "CrystalSeparator" });

        // === 魔晶用途 ===

        rows.AddChild(new Label
        {
            Name = "CrystalTitle",
            Text = Loc.T("魔晶用途"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _crystal = MakeLabel("Crystal");
        rows.AddChild(_crystal);

        _manaButton = MakeButton("UseMana", Loc.T("补魔"));
        _manaButton.Pressed += () => _inject("UseCrystal", "mana");
        rows.AddChild(_manaButton);

        var healRow = new HBoxContainer { Name = "HealRow" };
        healRow.AddThemeConstantOverride("separation", 8);
        rows.AddChild(healRow);

        healRow.AddChild(new Label
        {
            Name = "HealTag",
            Text = Loc.T("治疗目标"),
            CustomMinimumSize = new Vector2(92f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _healTarget = new OptionButton
        {
            Name = "HealTarget",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
        };
        healRow.AddChild(_healTarget);

        _healButton = MakeButton("UseHeal", Loc.T("治疗"));
        _healButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _healButton.CustomMinimumSize = new Vector2(88f, ButtonHeight);
        _healButton.Pressed += OnHealPressed;
        healRow.AddChild(_healButton);

        var repairRow = new HBoxContainer { Name = "RepairRow" };
        repairRow.AddThemeConstantOverride("separation", 8);
        rows.AddChild(repairRow);

        repairRow.AddChild(new Label
        {
            Name = "RepairTag",
            Text = Loc.T("修理目标"),
            CustomMinimumSize = new Vector2(92f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        _repairTarget = new OptionButton
        {
            Name = "RepairTarget",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
        };
        repairRow.AddChild(_repairTarget);

        _repairButton = MakeButton("UseRepair", Loc.T("修理"));
        _repairButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _repairButton.CustomMinimumSize = new Vector2(88f, ButtonHeight);
        _repairButton.Pressed += OnRepairPressed;
        repairRow.AddChild(_repairButton);

        _crystalHint = MakeLabel("CrystalHint");
        rows.AddChild(_crystalHint);

        var close = MakeButton("Close", Loc.T("关闭"));
        close.Pressed += Close;
        outer.AddChild(close);

        ui.AddPopup(this);
    }

    /// <summary>面板开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>打开巨兽面板（每次都按世界现况重算置灰与读数）。</summary>
    public void Open()
    {
        if (_world() is null) return;

        _signature = string.Empty;
        Refresh();
        Visible = true;

        // 模态只是压住世界的拾取，<b>不暂停时间</b>：世界该走的步照走，
        // 仪式倒计时与饱食衰减在面板开着的时候一样在跑。
        _ui.EnterModal(this);
    }

    /// <summary>关掉巨兽面板并摘掉自己那一份模态（世界照常收拾取）。</summary>
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

    /// <summary>进阶那一发：无参数（判定顺序在逻辑层那一档）。</summary>
    private void OnAdvancePressed() => _inject("AdvanceTier", string.Empty);

    /// <summary>切形态那一发：形态名用词表 token（小写英文）。</summary>
    private void OnSwitchForm(BeastForm form)
        => _inject("SwitchForm", BeastFormText.TokenOf(form));

    /// <summary>喂食那一发：一次 <see cref="FeedAmount"/> 份食物。</summary>
    private void OnFeedPressed()
        => _inject("Feed", FeedAmount.ToString(CultureInfo.InvariantCulture));

    /// <summary>NPC 喂食那一发：无参数，判定与拒绝原因都在世界那一侧。</summary>
    private void OnNpcFeedPressed() => _inject("NpcFeed", string.Empty);

    /// <summary>治疗：目标取下拉当前那个人（重伤名单里选）。</summary>
    private void OnHealPressed()
    {
        var target = SelectedOf(_healTarget, _healOrder);
        if (target.Length == 0) return;
        _inject("UseCrystal", "heal|" + target);
    }

    /// <summary>修理：目标取下拉当前那栋建筑（正在修理名单里选）。</summary>
    private void OnRepairPressed()
    {
        var target = SelectedOf(_repairTarget, _repairOrder);
        if (target.Length == 0) return;
        _inject("UseCrystal", "repair|" + target);
    }

    /// <summary>下拉当前那一项对应的 id（名单空或选中越界回空串）。</summary>
    private static string SelectedOf(OptionButton option, List<string> order)
    {
        var index = option.Selected;
        return index >= 0 && index < order.Count ? order[index] : string.Empty;
    }

    // === 每帧派生 ===

    /// <summary>按世界现况重算读数、置灰与那一行原因。</summary>
    private void Refresh()
    {
        var world = _world();
        if (world is null) return;

        // 当前形态那一排的高亮每帧归位：点了没解锁的形态时按钮自己会按下，
        // 而形态没变 —— 不归位就留在按下态，看着像已经切过去了。
        SyncFormHighlight(world);

        var blocker = ArenaSetup.AdvanceBlockerOf(world);
        var signature = string.Format(CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3}|{4:0.##}|{5:0.##}|{6:0.##}|{7:0.##}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}",
            (int)world.Tier, (int)world.Form, blocker,
            world.InRitual ? world.RitualUntilTick - world.Tick : -1,
            world.Voyage.Satiation, world.Town.Food, world.Voyage.GranaryFood,
            world.Town.SoulStone, world.Town.SpecialMaterial, world.Town.Coin,
            world.Voyage.DwellGameMinutes, world.Voyage.FedTotalFood,
            SoilMask(world), (int)world.Battle.Occasion,
            world.CrystalWarehouse, world.Town.AvailableMana,
            TargetSignature(world), world.Form, (int)world.NpcFeedBlocker());
        if (signature == _signature) return;
        _signature = signature;

        RefreshTier(world, blocker);
        RefreshForms(world);
        RefreshFeed(world);
        RefreshCrystal(world);
    }

    /// <summary>治疗 / 修理两个下拉的候选签名（名单变了才重摆）。</summary>
    private static string TargetSignature(ArenaWorld world)
    {
        var ids = new List<string>();
        foreach (var unit in world.Roster.Units)
        {
            if (unit.LeftTown || unit.State != UnitState.Wounded) continue;
            if (!unit.IsTownsperson && !unit.IsHelper) continue;
            ids.Add("h:" + unit.Id);
        }

        foreach (var id in world.Construction.Ids)
        {
            if (world.Deck.IsRepairing(id)) ids.Add("r:" + id);
        }

        return string.Join(',', ids);
    }

    /// <summary>
    /// 八格土壤的三档状态压成一个整数（挖开 / 清剿 / 嵌入各一段）——
    /// 门槛那一行跟着它变（逐象限那一行已删，深度只归土壤面板），所以它得进刷新签名。
    /// </summary>
    private static int SoilMask(ArenaWorld world)
    {
        var mask = 0;
        for (var sector = 0; sector < UndergroundSoil.Sectors; sector++)
        {
            for (var layer = 1; layer <= UndergroundSoil.Layers; layer++)
            {
                var bit = 1 << ((sector * UndergroundSoil.Layers) + (layer - 1));
                if (world.Soil.IsDug(sector, layer)) mask |= bit;
                if (world.Soil.IsCleared(sector, layer)) mask |= bit << 8;
                if (world.Soil.IsEmbedded(sector, layer)) mask |= bit << 16;
            }
        }

        return mask;
    }

    /// <summary>位阶那一区：当前阶、门槛、三项代价、仪式倒计时、进阶按钮与原因。</summary>
    private void RefreshTier(ArenaWorld world, AdvanceBlocker blocker)
    {
        _tier.Text = Loc.Fmt("当前位阶：{0}", Loc.T(BeastTierText.NameOf(world.Tier)));

        if (world.Tier == BeastTier.Tier3)
        {
            _gate.Text = Loc.T("已到最高位阶（三阶）");
            _cost.Text = Loc.T("没有下一阶，没有代价");
            _advance.Text = Loc.T("已到三阶");
        }
        else if (world.Tier == BeastTier.Tier1)
        {
            var cost = ArenaTuning.CostOf(BeastTier.Tier2);
            _gate.Text = Loc.Fmt("门槛：第 {0} 层 + 洞穴清剿 + 魔晶嵌入（{1}）",
                UndergroundSoil.NativeCaveLayer, Tier2GateText(world));
            _cost.Text = Loc.Fmt("代价：魂石 {0}（有 {1}） · 特殊材料 {2}（有 {3}） · 金币 {4}（有 {5}）",
                cost.SoulStone.ToString(CultureInfo.InvariantCulture),
                world.Town.SoulStone.ToString("0.##", CultureInfo.InvariantCulture),
                cost.SpecialMaterial.ToString(CultureInfo.InvariantCulture),
                world.Town.SpecialMaterial.ToString("0.##", CultureInfo.InvariantCulture),
                cost.Coin.ToString(CultureInfo.InvariantCulture),
                world.Town.Coin.ToString("0.##", CultureInfo.InvariantCulture));
            _advance.Text = Loc.Fmt("进阶到{0}", Loc.T(BeastTierText.NameOf(BeastTier.Tier2)));
        }
        else
        {
            // 三阶不在切片内：地下只有两层，第三层挖不到，按钮永远进不去。
            _gate.Text = Loc.Fmt("三阶不在本切片内（地下只有 {0} 层）", UndergroundSoil.Layers);
            _cost.Text = Loc.T("没有下一阶的代价（切片外）");
            _advance.Text = Loc.T("三阶不在切片内");
        }

        // 仪式倒计时：只有仪式开着的时候才有数。
        _ritual.Text = world.InRitual
            ? Loc.Fmt("仪式剩余 {0} 游戏分钟",
                ((world.RitualUntilTick - world.Tick) / ArenaTime.StepsPerGameMinute)
                    .ToString(CultureInfo.InvariantCulture))
            : Loc.T("仪式未开");

        _advance.Disabled = blocker != AdvanceBlocker.None;
        _advanceHint.Text = AdvanceHint(world, blocker);
    }

    /// <summary>二阶三项门槛的现况（哪一项到、哪一项没到）。</summary>
    private static string Tier2GateText(ArenaWorld world)
    {
        var sector = UndergroundSoil.CaveSector;
        var layer = UndergroundSoil.NativeCaveLayer;
        var dug = world.Soil.IsDug(sector, layer);
        var cleared = world.Soil.IsCleared(sector, layer);
        var embedded = world.Soil.IsEmbedded(sector, layer);

        return Loc.Fmt("第 {0} 层：{1}；清剿：{2}；嵌入：{3}",
            layer,
            dug ? Loc.T("已达") : Loc.T("未达"),
            cleared ? Loc.T("已达") : Loc.T("未达"),
            embedded ? Loc.T("已达") : Loc.T("未达"));
    }

    /// <summary>形态那一区：当前形态、三个按钮（当前那个高亮）、未解锁的条件与进度、战斗中切换限制。</summary>
    private void RefreshForms(ArenaWorld world)
    {
        _form.Text = Loc.Fmt("当前形态：{0}", Loc.T(BeastFormText.NameOf(world.Form)));

        var inBattle = world.Battle.Occasion != BattleOccasion.None;
        var switchLine = inBattle
            ? Loc.T("战斗中切不了形态（打完再切）")
            : Loc.T("非战斗时可免费立即切换，无冷却");

        _formHint.Text = Loc.Fmt(
            "荒漠态：沙漠累计 {0}/{1} 游戏分钟（{2}）—— 吃矿石饱食 ×2、其他 ×0.5，沙漠饥饿 ×0.5；暴食态：累计喂食 {3}/{4} 食物且已驶离贫城（{5}）—— 吞食饱食与消化 ×1.5，饥饿 ×1.5；{6}",
            world.Voyage.DwellGameMinutes, ArenaTuning.DesertFormDwellGameMinutes,
            UnlockedText(world, BeastForm.Desert),
            world.Voyage.FedTotalFood, ArenaTuning.GluttonFormFedTotalFood,
            UnlockedText(world, BeastForm.Glutton),
            switchLine);

        _formOriginal.Disabled = inBattle;
        _formDesert.Disabled = inBattle;
        _formGlutton.Disabled = inBattle;
    }

    /// <summary>当前形态那一颗按下（高亮），其余两颗弹起来。</summary>
    private void SyncFormHighlight(ArenaWorld world)
    {
        _formOriginal.ButtonPressed = world.Form == BeastForm.Original;
        _formDesert.ButtonPressed = world.Form == BeastForm.Desert;
        _formGlutton.ButtonPressed = world.Form == BeastForm.Glutton;
    }

    /// <summary>喂食那一区：饱食度、两处存粮、喂食按钮与缺口。</summary>
    private void RefreshFeed(ArenaWorld world)
    {
        _feed.Text = Loc.Fmt("饱食度 {0}/{1} · 镇仓食物 {2} · 兽粮 {3}",
            world.Voyage.Satiation, ArenaTuning.SatiationMax,
            world.Town.Food, world.Voyage.GranaryFood);

        var capacity = ArenaSetup.FeedCapacity(world);
        _feedButton.Disabled = capacity < FeedAmount;
        _feedHint.Text = FeedShortText(world, capacity);

        var npcBlocker = world.NpcFeedBlocker();
        _npcFeedButton.Disabled = npcBlocker != RejectReason.InvalidArgs;
        _npcFeedHint.Text = NpcFeedHintText(world, npcBlocker);
    }

    /// <summary>
    /// 魔晶那一区：库存三处、可用法力、三条用途的候选与原因。
    /// <b>判定不在这里</b> —— 按下去仍走 <c>UseCrystal</c>，被拒原因由横幅照实显示；
    /// 这里只把「有没有目标」与「仓库有没有魔晶」写清。
    /// </summary>
    private void RefreshCrystal(ArenaWorld world)
    {
        _crystal.Text = Loc.Fmt("仓库魔晶 {0} · 甲板 {1} · 搬运中 {2} · 可用法力 {3}/{4}",
            world.CrystalWarehouse.ToString(CultureInfo.InvariantCulture),
            world.CrystalOnDeck.ToString(CultureInfo.InvariantCulture),
            world.CrystalCarrying.ToString(CultureInfo.InvariantCulture),
            world.Town.AvailableMana.ToString(CultureInfo.InvariantCulture),
            ArenaTuning.ManaCapacity.ToString(CultureInfo.InvariantCulture));

        _manaButton.Disabled = world.CrystalWarehouse <= 0;
        _manaButton.TooltipText = world.CrystalWarehouse <= 0
            ? Loc.T("仓库里没有魔晶")
            : Loc.Fmt("用 1 单位魔晶补 {0} 格法力（至少缺 {1} 格才值得用）",
                ArenaTuning.CrystalManaRestore.ToString(CultureInfo.InvariantCulture),
                ArenaTuning.CrystalManaMinGap.ToString(CultureInfo.InvariantCulture));

        SyncTargets(world);

        _healButton.Disabled = _healOrder.Count == 0 || world.CrystalWarehouse <= 0;
        _healButton.TooltipText = _healOrder.Count == 0
            ? Loc.T("没有重伤居民")
            : world.CrystalWarehouse <= 0 ? Loc.T("仓库里没有魔晶") : string.Empty;

        _repairButton.Disabled = _repairOrder.Count == 0 || world.CrystalWarehouse <= 0;
        _repairButton.TooltipText = _repairOrder.Count == 0
            ? Loc.T("没有正在修理的建筑")
            : world.CrystalWarehouse <= 0 ? Loc.T("仓库里没有魔晶") : string.Empty;

        _crystalHint.Text = Loc.T("补魔 / 治疗 / 修理都从仓库扣 1 单位，战斗里也能用；嵌入归土壤面板");
    }

    /// <summary>两个目标下拉：名单变了才重摆，选中项按 id 尽量保住。</summary>
    private void SyncTargets(ArenaWorld world)
    {
        var wounded = new List<string>();
        foreach (var unit in world.Roster.Units)
        {
            if (unit.LeftTown || unit.State != UnitState.Wounded) continue;
            if (!unit.IsTownsperson && !unit.IsHelper) continue;
            wounded.Add(unit.Id);
        }

        var repairing = new List<string>();
        foreach (var id in world.Construction.Ids)
        {
            if (world.Deck.IsRepairing(id)) repairing.Add(id);
        }

        SyncOneTarget(_healTarget, _healOrder, wounded, id => id);
        SyncOneTarget(_repairTarget, _repairOrder, repairing, id => BuildingName(world, id));
    }

    /// <summary>一份下拉的名单：次序没变就不动；变了重摆并把旧选中项找回来。</summary>
    private void SyncOneTarget(
        OptionButton option, List<string> order, List<string> wanted, Func<string, string> labelOf)
    {
        if (SameOrder(order, wanted)) return;

        var previous = SelectedOf(option, order);
        order.Clear();
        order.AddRange(wanted);

        option.Clear();
        foreach (var id in order) option.AddItem(labelOf(id));

        var index = order.IndexOf(previous);
        option.Selected = index >= 0 ? index : (order.Count > 0 ? 0 : -1);
        option.Disabled = order.Count == 0;
    }

    private static bool SameOrder(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    /// <summary>建筑 id → 蓝图中文名（查不到就写 id 本身）。</summary>
    private static string BuildingName(ArenaWorld world, string buildingId)
    {
        var blueprint = world.Deck.BlueprintIdOf(buildingId);
        foreach (var spec in DeckBlueprints.Specs)
        {
            if (spec.Id == blueprint) return Loc.T(spec.Name);
        }

        return buildingId;
    }

    // === 摆件 ===

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

    /// <summary>形态那一排的按钮：按下即发 <c>SwitchForm</c>；当前形态靠按下态高亮。</summary>
    private Button MakeFormButton(string name, string text, BeastForm form)
    {
        var button = MakeButton(name, text);
        button.ToggleMode = true;
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.Pressed += () => OnSwitchForm(form);
        return button;
    }

    // === 派生文本 ===

    /// <summary>解锁旗标那一档（1 = 已解锁）。</summary>
    private static string UnlockedText(ArenaWorld world, BeastForm form)
        => world.FormUnlocked(form) ? Loc.T("已解锁") : Loc.T("未解锁");

    /// <summary>
    /// 进阶按钮下面那一行原因 —— <b>差多少写多少</b>（差魂石、差材料、差金币都带数字）。
    /// </summary>
    private static string AdvanceHint(ArenaWorld world, AdvanceBlocker blocker)
    {
        switch (blocker)
        {
            case AdvanceBlocker.None:
                return string.Empty;

            case AdvanceBlocker.TierAtMax:
                return Loc.T("已经到三阶了");

            case AdvanceBlocker.NotDocked:
                return Loc.T("先停靠，仪式要停着才开得起来");

            case AdvanceBlocker.InBattle:
                return Loc.T("打完了再进阶");

            case AdvanceBlocker.InStandstill:
                return Loc.T("停摆期开不了进阶仪式");

            case AdvanceBlocker.InRitual:
                return Loc.T("进阶仪式正在开着");

            case AdvanceBlocker.GateNotMet:
                if (world.Tier != BeastTier.Tier1)
                {
                    return Loc.Fmt("三阶不在本切片内（地下只有 {0} 层）", UndergroundSoil.Layers);
                }

                return Loc.Fmt("门槛没过：{0}", Tier2GateText(world));

            case AdvanceBlocker.NotEnoughSoulStone:
                var soul = ArenaTuning.CostOf((BeastTier)((int)world.Tier + 1)).SoulStone;
                return Loc.Fmt("缺 {0} 魂石", (soul - world.Town.SoulStone).ToString("0.##", CultureInfo.InvariantCulture));

            case AdvanceBlocker.NotEnoughMaterial:
                var material = ArenaTuning.CostOf((BeastTier)((int)world.Tier + 1)).SpecialMaterial;
                return Loc.Fmt("缺 {0} 特殊材料",
                    (material - world.Town.SpecialMaterial).ToString("0.##", CultureInfo.InvariantCulture));

            case AdvanceBlocker.NotEnoughCoin:
                var coin = ArenaTuning.CostOf((BeastTier)((int)world.Tier + 1)).Coin;
                return Loc.Fmt("缺 {0} 金币", (coin - world.Town.Coin).ToString("0.##", CultureInfo.InvariantCulture));

            default:
                return Loc.T("这一下发不出去");
        }
    }

    /// <summary>
    /// 喂食按钮下面那一行 —— 存粮不够时写「缺 N 食物」并点名差在哪个仓。
    /// </summary>
    private static string FeedShortText(ArenaWorld world, double capacity)
    {
        if (capacity >= FeedAmount) return string.Empty;

        var shortBy = (FeedAmount - capacity).ToString("0.##", CultureInfo.InvariantCulture);
        var townShort = FeedAmount - world.Town.Food;
        var granaryShort = FeedAmount - world.Voyage.GranaryFood;

        if (townShort > 0 && granaryShort > 0) return Loc.Fmt("缺 {0} 食物（镇仓与兽粮）", shortBy);
        if (townShort > 0) return Loc.Fmt("缺 {0} 食物（镇仓）", shortBy);
        return Loc.Fmt("缺 {0} 食物（兽粮）", shortBy);
    }

    /// <summary>NPC 喂食那一行：能喂就写这一口给多少，不能喂就写是哪一条挡着。</summary>
    private static string NpcFeedHintText(ArenaWorld world, RejectReason blocker) => blocker switch
    {
        RejectReason.InvalidPhase => world.IsDocked
            ? Loc.T("战斗 / 仪式 / 停摆期间不喂")
            : Loc.T("只在停靠期让工人喂"),
        RejectReason.NoIdleWorker => Loc.T("没有无岗的工人或帮工"),
        RejectReason.InsufficientFood => world.Ecology.Satiation >= ArenaTuning.SatiationMax
            ? Loc.T("饱食已满")
            : world.SatiationRoomFood < ArenaTuning.NpcFeedFoodAmount
                ? Loc.T("饱食余量不足，喂不进这一份")
                : Loc.T("镇仓与兽粮不够 5 份"),
        _ => Loc.Fmt("工人或帮工喂 {0} 食物（+{1} 饱食）",
            ArenaTuning.NpcFeedFoodAmount, ArenaTuning.NpcFeedFoodAmount / ArenaTuning.FoodPerSatiation),
    };

    // === 收尾 ===

    /// <summary>点面板外的空白处收起（面板内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
