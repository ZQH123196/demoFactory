#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 右键已建建筑弹出的菜单 —— 镇政府给「招募」、竞技场给「开赛」、其余给「修理 / 拆除」。
///
/// <para><b>条目按蓝图分支</b>（建筑 id 是 <c>town_hall</c> / <c>bN</c> 这一类，蓝图 id 才是
/// <c>TownHall</c> / <c>Arena</c>）。镇政府那一格开 <see cref="RecruitDialog"/>（ticket 07），
/// 竞技场那一格给「开赛」与「战报」（ticket 08）——开赛的挑人与分队在逻辑层
/// <see cref="BattleSim.TryBuildMatchArgs"/> 那一处，菜单只负责下发与弹横幅；
/// 「战报」在没有战报时写「暂无战报」并置灰。修理与拆除是真的写路径。</para>
///
/// <para><b>修理只在 <see cref="ArenaWorld.CanRepair"/> 为真时出现</b>（耐久未满、备件 ≥ 1、
/// 在岗 ≥ 1）。点下去之前先读 <see cref="ArenaWorld.RepairBlockerOf"/> ——
/// <b>缺备件与没人在岗在逻辑层就是两条原因</b>，横幅据此各写各的，UI 不从一句笼统的拒绝里猜。</para>
///
/// <para><b>拔起与落位</b>：任意一栋（镇政府除外）给「拔起」，走成了就进放置模式 ——
/// 幽灵由 <c>Main</c> 挂的那一件打开（<see cref="PlacementRequested"/>）。
/// 悬空的那一栋再点一次只给「落位」（重进放置模式），并且菜单里写明它占着几格法力。</para>
///
/// <para><b>教堂那一格多出在册尸体那几行</b>：每具在册尸体一行「救人：名字（10 魂石）」，
/// 过期与魂石不足两档都置灰并各写各的差额。</para>
///
/// <para><b>传送阵那一格多出「传送」</b>：开传送弹层（弹层自己管选人与两件货）。</para>
///
/// <para>菜单挂在 <see cref="UiRoot.PopupLayer"/> 上，开时置模态、关时清；
/// 在鼠标位置弹出，四边往屏幕内收。</para>
/// </summary>
public partial class BuildingMenu : Control, IModalCloseable
{
    /// <summary>菜单最小宽度（像素）。</summary>
    public const float MenuMinWidth = 176f;

    /// <summary>一行按钮的高度（像素）。</summary>
    public const float RowHeight = 28f;

    /// <summary>行间距（像素）。</summary>
    public const float RowGap = 4f;

    /// <summary>面板内边距（像素）。</summary>
    public const float Padding = 8f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Func<string, string, string> _inject;
    private readonly Action<string, bool> _notify;
    private readonly UiRoot _ui;

    private readonly PanelContainer _box;
    private readonly VBoxContainer _rows;

    private string _buildingId = string.Empty;

    /// <summary>镇政府那一格点了「招募」—— 菜单先收起，招募框由 <c>Main</c> 挂的那一件打开。</summary>
    public event Action? RecruitRequested;

    /// <summary>竞技场那一格点了「战报」—— 菜单先收起，战报由 <c>Main</c> 挂的那一件打开。</summary>
    public event Action? BattleReportRequested;

    /// <summary>
    /// 「拔起」或「落位」走成了 —— 菜单先收起，落位幽灵由 <c>Main</c> 挂的那一件打开。
    /// </summary>
    public event Action<string>? PlacementRequested;

    /// <summary>「传送」按下去了 —— 菜单先收起，传送弹层由 <c>Main</c> 挂的那一件打开。</summary>
    public event Action? TeleportRequested;

    /// <summary>教堂救人那几行（按钮 + 对应的那一具尸体）。</summary>
    private readonly List<(Button Button, string CorpseId)> _rescueRows = [];

    public BuildingMenu(
        UiRoot ui,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject,
        Action<string, bool> notify)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);
        ArgumentNullException.ThrowIfNull(notify);

        _ui = ui;
        _world = world;
        _inject = inject;
        _notify = notify;

        Name = "BuildingMenu";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
        GuiInput += OnPopupInput;

        _box = new PanelContainer
        {
            Name = "Box",
            CustomMinimumSize = new Vector2(MenuMinWidth, 0f),
        };
        _box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.11f, 0.14f, 0.98f),
            BorderColor = new Color(0.45f, 0.50f, 0.62f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = Padding,
            ContentMarginRight = Padding,
            ContentMarginTop = Padding,
            ContentMarginBottom = Padding,
        });
        AddChild(_box);

        _rows = new VBoxContainer { Name = "Rows" };
        _rows.AddThemeConstantOverride("separation", (int)RowGap);
        _box.AddChild(_rows);

        ui.AddPopup(this);
    }

    /// <summary>菜单开着没有。</summary>
    public bool IsOpen => Visible;

    /// <summary>在鼠标位置弹出某一栋的菜单（建筑不在册就什么都不做）。</summary>
    public void Open(Vector2 screen, string buildingId)
    {
        var world = _world();
        var building = world?.Construction.Find(buildingId);
        if (world is null || building is null) return;

        _buildingId = buildingId;
        Rebuild(world, building);
        Visible = true;
        _ui.EnterModal(this);
        Place(screen);
    }

    /// <summary>关掉菜单并摘掉自己那一份模态。</summary>
    public void Close()
    {
        Visible = false;
        _ui.ExitModal(this);
    }

    public override void _Process(double delta)
    {
        // 菜单开着的那一栋被拆掉 / 被攻破毁掉：菜单自己收起来，不留在屏幕上点不动。
        if (!Visible) return;
        if (_world()?.Construction.Find(_buildingId) is null) Close();
        else RefreshRescueRows();
    }

    // === 条目 ===

    private void Rebuild(ArenaWorld world, DemoFactory.GugaFramework.Construction.Building building)
    {
        while (_rows.GetChildCount() > 0)
        {
            var child = _rows.GetChild(0);
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        _rescueRows.Clear();

        var spec = DeckBlueprints.SpecOf(building.BlueprintId);
        _rows.AddChild(new Label
        {
            Text = spec is null ? Loc.T(building.BlueprintId) : Loc.T(spec.Name),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        switch (building.BlueprintId)
        {
            case "TownHall":
                var recruit = MakeButton("Recruit", Loc.T("招募"), enabled: true);
                recruit.Pressed += OnRecruitPressed;
                _rows.AddChild(recruit);
                return;

            case "Arena":
                // 开赛恒可点：凑不出两队时点下去弹「角斗士不足」，一个命令都不发。
                var start = MakeButton("StartMatch", Loc.T("开赛"), enabled: true);
                start.Pressed += OnStartMatchPressed;
                _rows.AddChild(start);

                // 没战报时写「暂无战报」并置灰 —— 条目不让点，也不假装有东西可看。
                var hasReport = world.Battle.LastReport is not null;
                var report = MakeButton("BattleReport",
                    hasReport ? Loc.T("战报") : Loc.T("暂无战报"), enabled: hasReport);
                report.Pressed += OnReportPressed;
                _rows.AddChild(report);
                break;
        }

        // 拔起 / 落位：镇政府是锚，拔不起来；悬空的那一栋只给「落位」（重进放置模式）。
        if (ArenaSnapshot.IsLifted(world, building.Id))
        {
            var drop = MakeButton("Drop", Loc.T("落位"), enabled: true);
            drop.Pressed += OnDropPressed;
            _rows.AddChild(drop);

            _rows.AddChild(new Label
            {
                Name = "LiftedNote",
                Text = Loc.Fmt("悬空 · 占 {0} 格法力",
                    world.Town.StasisMana.ToString(CultureInfo.InvariantCulture)),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        else if (building.BlueprintId != "TownHall")
        {
            var lift = MakeButton("Lift", Loc.T("拔起"), enabled: true);
            lift.Pressed += OnLiftPressed;
            _rows.AddChild(lift);
        }

        // 教堂那一格：在册尸体逐具一行「救人：名字（10 魂石）」。
        if (building.BlueprintId == "Church") AddRescueRows(world);

        // 传送阵那一格：开传送弹层（弹层自己管选人与两件货）。
        if (building.BlueprintId == "TeleportArray")
        {
            var teleport = MakeButton("Teleport", Loc.T("传送"), enabled: true);
            teleport.Pressed += OnTeleportPressed;
            _rows.AddChild(teleport);
        }

        // 「任意（镇政府除外）」：修理只在 CanRepair 为真时出现，拆除恒在。
        if (world.CanRepair(building.Id))
        {
            var repair = MakeButton("Repair", Loc.T("修理"), enabled: true);
            repair.Pressed += OnRepairPressed;
            _rows.AddChild(repair);
        }

        var remove = MakeButton("Remove", Loc.T("拆除"), enabled: true);
        remove.Pressed += OnRemovePressed;
        _rows.AddChild(remove);
    }

    /// <summary>
    /// 教堂救人那几行：一具在册尸体一行。
    ///
    /// <para><b>两档都置灰，各写各的</b>：过期的那一具写「过期」，魂石不够的那一具写差多少 ——
    /// 玩家一眼看得出是「救不回来」还是「钱不够」。</para>
    /// </summary>
    private void AddRescueRows(ArenaWorld world)
    {
        foreach (var record in world.ChurchCorpses)
        {
            var corpseId = record.CorpseId;
            var button = MakeButton("Rescue" + corpseId, string.Empty, enabled: true);
            button.Pressed += () => OnRescuePressed(corpseId);
            _rows.AddChild(button);
            _rescueRows.Add((button, corpseId));
        }

        RefreshRescueRows();
    }

    /// <summary>救人那几行的文字与置灰（魂石存量会变，每帧对一次账）。</summary>
    private void RefreshRescueRows()
    {
        var world = _world();
        if (world is null) return;

        var missing = ArenaTuning.ReviveSoulStones - world.Town.SoulStone;

        foreach (var (button, corpseId) in _rescueRows)
        {
            var record = world.FindChurchCorpse(corpseId);
            if (record is null)
            {
                button.Text = Loc.Fmt("救人：{0}（已不在册）", corpseId);
                button.Disabled = true;
                continue;
            }

            if (world.IsChurchCorpseExpired(record))
            {
                button.Text = Loc.Fmt("救人：{0}（过期）", record.Name);
                button.Disabled = true;
                continue;
            }

            if (missing > 0.0)
            {
                button.Text = Loc.Fmt("救人：{0}（缺 {1} 魂石）", record.Name,
                    missing.ToString("0.##", CultureInfo.InvariantCulture));
                button.Disabled = true;
                continue;
            }

            button.Text = Loc.Fmt("救人：{0}（{1} 魂石）", record.Name,
                ArenaTuning.ReviveSoulStones.ToString("0.##", CultureInfo.InvariantCulture));
            button.Disabled = false;
        }
    }

    private Button MakeButton(string name, string text, bool enabled) => new()
    {
        Name = name,
        Text = text,
        Disabled = !enabled,
        CustomMinimumSize = new Vector2(0f, RowHeight),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        FocusMode = FocusModeEnum.None,
    };

    private void OnRepairPressed()
    {
        var world = _world();
        if (world is null || _buildingId.Length == 0) return;

        // 先读逻辑层分好的那一条原因：缺备件 / 没人在岗 / 已在修 / 满耐久。
        var blocker = world.RepairBlockerOf(_buildingId);
        if (blocker != RepairBlocker.None)
        {
            _notify(RepairText(blocker), false);
            Close();
            return;
        }

        var result = _inject("RepairBuilding", _buildingId);
        if (string.Equals(result, "ok", StringComparison.Ordinal))
            _notify(Loc.T("已下令修理"), true);

        Close();
    }

    private void OnRecruitPressed()
    {
        // 先收起菜单（清模态），再让招募框置模态 —— 两件弹层不叠着抢模态标记。
        Close();
        RecruitRequested?.Invoke();
    }

    /// <summary>
    /// 竞技场「开赛」：挑人与分队全在逻辑层那一口做完，这里只管下发与弹横幅。
    /// <b>凑不出两队时一个命令都不发</b>（原因原样上横幅）。
    /// </summary>
    private void OnStartMatchPressed()
    {
        var world = _world();
        if (world is null) return;

        if (!BattleSim.TryBuildMatchArgs(world, out var args, out var why))
        {
            _notify(Loc.T(why), false);
            Close();
            return;
        }

        var result = _inject("StartMatch", args);
        if (string.Equals(result, "ok", StringComparison.Ordinal))
            _notify(Loc.T("已开赛"), true);

        Close();
    }

    /// <summary>竞技场「战报」：菜单先收起（清模态），战报框随后置模态。</summary>
    private void OnReportPressed()
    {
        Close();
        BattleReportRequested?.Invoke();
    }

    private void OnRemovePressed()
    {
        if (_buildingId.Length == 0) return;

        var result = _inject("RemoveBuilding", _buildingId);
        if (string.Equals(result, "ok", StringComparison.Ordinal))
            _notify(Loc.T("已拆除"), true);

        Close();
    }

    /// <summary>
    /// 「拔起」：一发 <c>LiftBuilding|建筑 id</c>。
    /// 走成了就进放置模式 —— 幽灵由 <c>Main</c> 挂的那一件打开（菜单自己不碰画面）。
    /// </summary>
    private void OnLiftPressed()
    {
        if (_buildingId.Length == 0) return;

        var buildingId = _buildingId;
        var result = _inject("LiftBuilding", buildingId);
        if (string.Equals(result, "ok", StringComparison.Ordinal))
            _notify(Loc.T("已拔起"), true);

        Close();

        if (string.Equals(result, "ok", StringComparison.Ordinal))
            PlacementRequested?.Invoke(buildingId);
    }

    /// <summary>「落位」：重进放置模式（那一栋还悬着，幽灵接着跟鼠标）。</summary>
    private void OnDropPressed()
    {
        if (_buildingId.Length == 0) return;

        var buildingId = _buildingId;
        Close();
        PlacementRequested?.Invoke(buildingId);
    }

    /// <summary>「传送」：开传送弹层（弹层自己管选人与两件货）。</summary>
    private void OnTeleportPressed()
    {
        Close();
        TeleportRequested?.Invoke();
    }

    /// <summary>「救人」：一发 <c>ReviveCorpse|尸体 id</c>（扣 10 魂石，摘掉在册那一条）。</summary>
    private void OnRescuePressed(string corpseId)
    {
        var result = _inject("ReviveCorpse", corpseId);
        if (string.Equals(result, "ok", StringComparison.Ordinal))
            _notify(Loc.T("已下令救人"), true);

        Close();
    }

    /// <summary>修理被拒那一句 —— <b>缺备件与没人在岗各写一句</b>，不用一句笼统的「修不了」。</summary>
    public static string RepairText(RepairBlocker blocker) => blocker switch
    {
        RepairBlocker.NoSparePart => Loc.T("修理要 1 个备件（缺备件）"),
        RepairBlocker.NoWorker => Loc.T("修理要有人在这栋楼在岗（没人在岗）"),
        RepairBlocker.FullHealth => Loc.T("耐久已满，不用修"),
        RepairBlocker.AlreadyRepairing => Loc.T("这一栋已经在修了"),
        RepairBlocker.UnknownBuilding => Loc.T("这一栋不在册"),
        _ => Loc.T("这一下没做成"),
    };

    // === 摆位 ===

    private void Place(Vector2 screen)
    {
        _box.ResetSize();

        var viewport = GetViewportRect().Size;
        var size = _box.Size;
        var maxX = Math.Max(UiRoot.ScreenMargin, viewport.X - size.X - UiRoot.ScreenMargin);
        var maxY = Math.Max(UiRoot.ScreenMargin, viewport.Y - size.Y - UiRoot.ScreenMargin);

        _box.Position = new Vector2(
            Math.Clamp(screen.X, UiRoot.ScreenMargin, maxX),
            Math.Clamp(screen.Y, UiRoot.ScreenMargin, maxY));
    }

    /// <summary>点菜单外面的空白处关掉它（菜单内的点击被子控件吃掉，到不了这里）。</summary>
    private void OnPopupInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Close();
    }
}
