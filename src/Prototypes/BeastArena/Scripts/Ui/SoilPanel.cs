#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 右侧土壤面板 —— 8 个格位按 4 个扇区分组竖排，贴在右上命令栏下方（同一条右栏，宽 <see cref="UiRoot.CommandBarMinWidth"/>）。
///
/// <para><b>格标签固定 <c>S{扇区+1}L{层}</c></b>（扇区从 1 起，层照 1..2 的原值）：
/// 一格一行，行首是身份，行尾按状态给按钮。扇区标题只做分组，不参与身份编号。</para>
///
/// <para><b>按钮按状态给，不给第二份状态</b>：未挖开只有「开挖」（正挖着 / 上一层还悬空时置灰）；
/// 已挖开才换成「派到此格」，洞穴那一行另有「清剿 / 撤退」与「嵌入魔晶」。
/// 固化与解除固化随「嵌入取代固化」整件删掉。</para>
///
/// <para><b>写路径只有一条</b>：所有按钮都走 <see cref="Main.Inject"/> ——
/// 开挖发 <c>sector|layer</c>（<c>ExcavateSoil</c>），嵌入发 <c>sector|layer</c>（<c>EmbedCrystal</c>），
/// 清剿 / 撤退发 <c>ClearCave</c> / <c>RetreatCave</c>，派岗发 <c>personId|soil:sector_layer</c>
/// （<c>AssignPost</c>）。</para>
///
/// <para><b>选中从唯一来源读</b>：本件只收一个 <c>Func&lt;string&gt;</c>，每帧现取
/// <see cref="SelectionPanel.SelectedId"/>，自己不存第二份选中态。没选中人时「派到此格」置灰，
/// 面板底部那一行写原因（悬停提示同一句）。</para>
/// </summary>
public partial class SoilPanel : Control
{
    /// <summary>面板内边距（像素）。</summary>
    public const float PanelPadding = 6f;

    /// <summary>一格行高（像素）—— 与 <see cref="UiRoot.SoilCellHeight"/> 同一档。</summary>
    public const float RowHeight = UiRoot.SoilCellHeight;

    /// <summary>行首格标签的固定宽度（像素），让各行的按钮左缘对齐。</summary>
    public const float LabelWidth = 46f;

    /// <summary>行内按钮之间的间距（像素）。</summary>
    public const float RowGap = 4f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly Func<string> _selectedId;

    private readonly Button[] _dig = new Button[UiRoot.SoilCellCount];
    private readonly Button[] _embed = new Button[UiRoot.SoilCellCount];
    private readonly Button[] _cave = new Button[UiRoot.SoilCellCount];
    private readonly Button[] _assign = new Button[UiRoot.SoilCellCount];
    private readonly Label[] _state = new Label[UiRoot.SoilCellCount];
    private readonly Label _hint;

    /// <summary>上一次刷新时的状态签名 —— 没变就不重写各行按钮文字 / Tooltip（每帧刷是纯浪费）。</summary>
    private string _signature = string.Empty;

    public SoilPanel(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject,
        Func<string> selectedId)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);
        ArgumentNullException.ThrowIfNull(selectedId);

        _world = world;
        _inject = inject;
        _selectedId = selectedId;

        Name = "SoilPanel";
        MouseFilter = MouseFilterEnum.Stop;

        // 右侧一列：右缘对齐屏幕边距，宽度与命令栏同宽；上缘贴命令栏下缘，下缘让出底部蓝图条。
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = -UiRoot.SoilPanelRightMargin - UiRoot.CommandBarMinWidth;
        OffsetRight = -UiRoot.SoilPanelRightMargin;
        OffsetTop = UiRoot.SoilPanelTopOffset;
        OffsetBottom = -UiRoot.SoilPanelBottomOffset;

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

        // 8 行在默认 648 高下正好放得下；窗口再矮时只滚动这一块，面板本身不越过蓝图条。
        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        rows.AddChild(scroll);

        var cells = new VBoxContainer { Name = "Cells", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cells.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(cells);

        for (var sector = 0; sector < UndergroundSoil.Sectors; sector++)
        {
            cells.AddChild(SectionLabel(Loc.Fmt("扇区 {0}", sector + 1)));

            for (var layer = 1; layer <= UndergroundSoil.Layers; layer++)
            {
                cells.AddChild(BuildCellRow(sector, layer));
            }
        }

        _hint = new Label
        {
            Name = "Hint",
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        rows.AddChild(_hint);

        ui.AddPanel(this);
    }

    public override void _Process(double delta)
    {
        var world = _world();
        var selected = _selectedId();
        var (canAssign, reason) = AssignReadiness(world, selected);

        var signature = new System.Text.StringBuilder(80);
        signature.Append(world is null ? '-' : 'w').Append('|').Append(selected).Append('|')
            .Append(canAssign).Append('|')
            .Append(world is null ? '-' : (int)world.Battle.Occasion).Append('|')
            // 嵌入按钮的置灰直接读这一口（停靠、战斗、魔晶余量都在里面）。
            // 把它自己进签名，任何一条资格变化都会触发重算；只盯仓库数的旧写法会漏掉
            // 「货已入库、账本魔晶还没跟上」那一帧，按钮就停在禁用态。
            .Append(world is null
                ? '-'
                : (int)world.EmbedBlockerOf(UndergroundSoil.CaveSector, UndergroundSoil.NativeCaveLayer));
        for (var sector = 0; sector < UndergroundSoil.Sectors; sector++)
        {
            for (var layer = 1; layer <= UndergroundSoil.Layers; layer++)
            {
                var read = world?.Soil.CellAt(sector, layer);
                signature.Append(read?.Dug == true ? '1' : '0')
                    .Append(read?.Digging == true ? '1' : '0')
                    .Append(read?.Cleared == true ? '1' : '0')
                    .Append(read?.Embedded == true ? '1' : '0')
                    .Append(read?.CaveMonsters ?? 0)
                    .Append(':')
                    .Append((read?.RegrowthProgress ?? 0.0).ToString("0.00", CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(read?.Miners ?? 0);
            }
        }

        var current = signature.ToString();
        if (current == _signature) return;
        _signature = current;

        _hint.Text = canAssign ? Loc.Fmt("派岗：{0}", selected) : reason;

        for (var sector = 0; sector < UndergroundSoil.Sectors; sector++)
        {
            for (var layer = 1; layer <= UndergroundSoil.Layers; layer++)
            {
                var i = Index(sector, layer);
                var cell = world?.Soil.CellAt(sector, layer);

                UpdateState(i, cell, sector, layer);

                if (cell?.Dug != true)
                {
                    UpdateDigButton(i, world, sector, layer, cell);
                    continue;
                }

                UpdateDugButtons(i, world, cell.Value, sector, layer,
                    world?.CanAssignSoil(selected, sector, layer) == true, reason);
            }
        }
    }

    /// <summary>
    /// 每格的状态列：开挖 / 清剿 / 回长 / 嵌入四档一起读。
    /// <b>回长进度照实报</b>（0..1 游戏日），嵌入后写「不回长」而不是 0 ——
    /// 两者在读数上必须分得开（0 是「还没长」，不回长是「永远不长了」）。
    /// </summary>
    private void UpdateState(int i, SoilCellReadout? cell, int sector, int layer)
    {
        if (cell is null)
        {
            _state[i].Text = Loc.T("—");
            _state[i].TooltipText = string.Empty;
            return;
        }

        var read = cell.Value;
        if (!read.Dug)
        {
            _state[i].Text = read.Digging ? Loc.T("挖开中") : Loc.T("未挖");
            _state[i].TooltipText = read.Digging
                ? Loc.T("正在挖：3 人 · 1 游戏小时")
                : Loc.T("挖开需 3 人 · 1 游戏小时");
            return;
        }

        if (read.Embedded)
        {
            _state[i].Text = Loc.T("已嵌入");
            _state[i].TooltipText = Loc.T("嵌入魔晶：永久停止回长；特殊材料每岗 2 游戏小时一件");
            return;
        }

        _state[i].Text = Loc.Fmt("回长 {0:0.00}/1 日", read.RegrowthProgress);

        var cave = UndergroundSoil.IsCaveCell(sector, layer)
            ? Loc.Fmt("；洞里还剩 {0} 只", read.CaveMonsters)
            : string.Empty;
        _state[i].TooltipText = Loc.Fmt(
            "回长填满会塌方（{0}_{1}）{2}；嵌入魔晶可永久停止回长",
            sector, layer, cave);
    }

    /// <summary>未挖开那一档：只留「开挖」，正挖着或上一层还悬空时点不动。</summary>
    private void UpdateDigButton(int i, ArenaWorld? world, int sector, int layer, SoilCellReadout? cell)
    {
        _dig[i].Visible = true;
        _embed[i].Visible = false;
        _cave[i].Visible = false;
        _assign[i].Visible = false;

        var digging = cell?.Digging == true;
        var waitingAbove = layer > 1 && world is not null && !world.Soil.IsDug(sector, layer - 1);

        _dig[i].Disabled = world is null || digging || waitingAbove;
        _dig[i].TooltipText = digging
            ? Loc.T("正在挖：3 人 · 1 游戏小时")
            : waitingAbove
                ? Loc.T("同象限自上而下，上一层还没挖开")
                : Loc.T("挖开需 3 人 · 1 游戏小时");
    }

    /// <summary>已挖开那一档：派到此格；洞穴那一行再给清剿 / 撤退与嵌入魔晶。</summary>
    private void UpdateDugButtons(
        int i, ArenaWorld? world, SoilCellReadout cell, int sector, int layer,
        bool canAssign, string reason)
    {
        _dig[i].Visible = false;
        _assign[i].Visible = true;
        _assign[i].Disabled = !canAssign;
        _assign[i].TooltipText = canAssign ? string.Empty : reason;

        var isCave = UndergroundSoil.IsCaveCell(sector, layer);

        // 嵌入只对那一处洞穴格有意义 —— 别的格没有可嵌入的对象，按钮直接不出现。
        _embed[i].Visible = isCave && world is not null;
        if (isCave && world is not null)
        {
            var blocker = world.EmbedBlockerOf(sector, layer);
            _embed[i].Text = cell.Embedded ? Loc.T("已嵌入") : Loc.T("嵌入魔晶");
            _embed[i].Disabled = blocker != SoilEmbedBlocker.None;
            _embed[i].TooltipText = EmbedHint(world, blocker, sector, layer);
        }

        _cave[i].Visible = isCave && world is not null;
        if (isCave && world is not null)
        {
            var fighting = world.Battle.Occasion == BattleOccasion.CaveClearing;
            _cave[i].Text = fighting
                ? Loc.T("撤退")
                : cell.Cleared ? Loc.T("已清剿") : Loc.T("清剿");

            var otherBattle = world.Battle.Occasion != BattleOccasion.None && !fighting;
            _cave[i].Disabled = cell.Cleared || cell.CaveMonsters <= 0 || otherBattle;
            _cave[i].TooltipText = cell.Cleared
                ? Loc.T("四只全灭，已经清剿过")
                : otherBattle
                    ? Loc.T("先打完这一场")
                    : Loc.T("发起清剿：四只原生怪全灭才算成功");
        }
    }

    /// <summary>嵌入那一颗的悬停提示：缺什么写什么（缺魔晶写仓库余量）。</summary>
    private static string EmbedHint(
        ArenaWorld world, SoilEmbedBlocker blocker, int sector, int layer)
    {
        return blocker switch
        {
            SoilEmbedBlocker.None => Loc.T("嵌入 1 单位魔晶：永久停止回长，特殊材料产出翻倍"),
            SoilEmbedBlocker.NotDocked => Loc.T("嵌入只限停靠期"),
            SoilEmbedBlocker.OutOfRange => Loc.T("那一格不存在"),
            SoilEmbedBlocker.NotDug => Loc.Fmt("{0}_{1} 还没挖开", sector, layer),
            SoilEmbedBlocker.NotCaveLayer => Loc.T("只有第二层的洞穴格能嵌入"),
            SoilEmbedBlocker.NotCleared => Loc.T("洞穴还没清剿成功"),
            SoilEmbedBlocker.AlreadyEmbedded => Loc.T("同格不可重复嵌入"),
            SoilEmbedBlocker.NoCrystal => Loc.Fmt("仓库里没有魔晶（有 {0}）",
                world.Town.Crystal.ToString("0.##", CultureInfo.InvariantCulture)),
            _ => Loc.T("打仗、停摆或仪式期间嵌入发不出去"),
        };
    }

    private Control BuildCellRow(int sector, int layer)
    {
        var row = new HBoxContainer { Name = $"Cell{sector + 1}_{layer}" };
        row.AddThemeConstantOverride("separation", (int)RowGap);

        row.AddChild(new Label
        {
            Name = "Tag",
            Text = $"S{sector + 1}L{layer}",
            CustomMinimumSize = new Vector2(LabelWidth, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var i = Index(sector, layer);

        _state[i] = new Label
        {
            Name = $"State{sector + 1}_{layer}",
            CustomMinimumSize = new Vector2(84f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true,
        };
        _state[i].AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(_state[i]);

        _dig[i] = MakeButton($"Dig{sector + 1}_{layer}", Loc.T("开挖"));
        _dig[i].Pressed += () => _inject("ExcavateSoil", $"{sector}|{layer}");
        row.AddChild(_dig[i]);

        _embed[i] = MakeButton($"Embed{sector + 1}_{layer}", Loc.T("嵌入魔晶"));
        _embed[i].Pressed += () => _inject("EmbedCrystal", $"{sector}|{layer}");
        row.AddChild(_embed[i]);

        _cave[i] = MakeButton($"Cave{sector + 1}_{layer}", Loc.T("清剿"));
        _cave[i].Pressed += () => OnCavePressed(sector, layer);
        row.AddChild(_cave[i]);

        _assign[i] = MakeButton($"Assign{sector + 1}_{layer}", Loc.T("派到此格"));
        _assign[i].Pressed += () => Assign(sector, layer);
        row.AddChild(_assign[i]);

        return row;
    }

    /// <summary>
    /// 洞穴那一颗按当下的场合决定发哪一发：清剿在打 → 撤退；否则发起清剿。
    /// <b>判定读的是世界，不存副本</b> —— 中途收场 / 攻破之后，下一帧自然回落到「清剿」。
    /// </summary>
    private void OnCavePressed(int sector, int layer)
    {
        var world = _world();
        if (world is null) return;

        if (world.Battle.Occasion == BattleOccasion.CaveClearing)
        {
            _inject("RetreatCave", string.Empty);
            return;
        }

        _inject("ClearCave", $"{sector}|{layer}");
    }

    /// <summary>派到此格：目标 id 是 <c>soil:象限_层</c>，人取当下选中的那一位。</summary>
    private void Assign(int sector, int layer)
    {
        var world = _world();
        if (world is null) return;

        var id = _selectedId();
        if (!world.CanAssignSoil(id, sector, layer)) return;

        _inject("AssignPost", $"{id}|soil:{sector}_{layer}");
    }

    /// <summary>
    /// 「派到此格」的提示行能不能写成可派 —— <b>判据只读逻辑层 <see cref="ArenaWorld.SoilAssignBlockerOf(string)"/></b>，
    /// 本件不另写在册 / 活着 / 职业三道判断。
    /// </summary>
    private static (bool Ready, string Reason) AssignReadiness(ArenaWorld? world, string id)
    {
        if (world is null) return (false, Loc.T("派岗：没有在跑的一局"));
        if (id.Length == 0) return (false, Loc.T("派岗：先在左下选中一个工人"));

        return world.SoilAssignBlockerOf(id) switch
        {
            SoilAssignBlocker.None => (true, string.Empty),
            SoilAssignBlocker.NotInRoster => (false, Loc.T("派岗：选中的人不在册")),
            SoilAssignBlocker.Dead => (false, Loc.T("派岗：选中的人已经死了")),
            SoilAssignBlocker.NotWorker => (false, Loc.T("派岗：采矿岗只收工作人员")),
            _ => (false, Loc.T("派岗：这一格派不了")),
        };
    }

    private static int Index(int sector, int layer)
        => (sector * UndergroundSoil.Layers) + (layer - 1);

    private static Label SectionLabel(string text)
    {
        var label = new Label
        {
            Name = "Sector",
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    private static Button MakeButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        CustomMinimumSize = new Vector2(0f, RowHeight),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        FocusMode = FocusModeEnum.None,
        ClipText = true,
    };
}
