#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 拔起之后那只落位幽灵 —— 轮廓跟着光标，脚下格子按建造模块的裁决涂绿 / 红。
///
/// <para><b>它与 <see cref="BuildGhost"/> 是两件事</b>：那一只是「按蓝图新造一栋」，
/// 这一只是「把拔起来的那一栋放回去」。所以两件各写各的，不合并：
/// 新造那一发要扣钱、要占格，放回去那一发不花钱、落回自己那几格是空动作。</para>
///
/// <para><b>格色的判据与落位那一发同一处</b>：
/// <c>world.Construction.Check(蓝图 id, 候选格)</c>。
/// <b>例外只有一处</b>：放回它自己现在这几格时那几格占着的就是它自己，
/// 按普通占用检查看是「格被占着」——所以那一格按绿算（判定在
/// <see cref="ArenaWorld.Deck"/>.<c>IsAtOwnAnchor</c> 那一口，与落位那一发同一句）。</para>
///
/// <para><b>写路径只有一条</b>：在甲板上点一下 → <c>Inject("DropBuilding", "建筑 id|列|行")</c>；
/// 右键取消 → 同一发发回它原来的锚点（放回原处是空动作，位置与占用一寸不动）。
/// 两者都进 Journal、能重放。</para>
///
/// <para><b>放置模式下右键只取消</b>，不开右键菜单（由 <c>Main</c> 那一侧先判放置模式）。</para>
/// </summary>
public partial class PlacementGhost : Node3D
{
    /// <summary>轮廓盒的高度（米）—— 只是给人看出「这里会立起一栋」，不是真实楼高。</summary>
    public const float OutlineHeightMeters = 2.5f;

    /// <summary>格色薄片抬离镇面的高度（米）—— 压在地面上会被 z-fighting 吃掉。</summary>
    public const float CellLiftMeters = 0.06f;

    private readonly Func<ArenaWorld?> _world;
    private readonly Picker _picker;
    private readonly Func<string, string, string> _inject;
    private readonly Func<bool> _blocked;

    private readonly Node3D _pivot = new() { Name = "Pivot" };
    private MeshInstance3D? _outline;
    private readonly List<MeshInstance3D> _cells = [];

    // 四份材质建一次、反复贴 —— 每帧新建 Resource 会在引擎里堆垃圾。
    private readonly StandardMaterial3D _greenCell = MakeMaterial(Green());
    private readonly StandardMaterial3D _redCell = MakeMaterial(Red());
    private readonly StandardMaterial3D _greenOutline = MakeMaterial(Green(fill: 0.16f));
    private readonly StandardMaterial3D _redOutline = MakeMaterial(Red(fill: 0.18f));

    private string _buildingId = string.Empty;
    private string _blueprintId = string.Empty;

    /// <summary>拔起来那一栋原来在哪儿（取消时放回这一格）。</summary>
    private GridCell _origin;

    /// <summary>现在是不是在落位模式。</summary>
    public bool Active => _buildingId.Length > 0;

    /// <summary>正在放回去的是哪一栋（不在落位模式回空串）。</summary>
    public string BuildingId => _buildingId;

    public PlacementGhost(
        DeckView deck,
        Picker picker,
        Func<ArenaWorld?> world,
        Func<string, string, string> inject,
        Func<bool> blocked)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inject);
        ArgumentNullException.ThrowIfNull(blocked);

        _picker = picker;
        _world = world;
        _inject = inject;
        _blocked = blocked;

        Name = "PlacementGhost";
        Visible = false;
        deck.LevitationNode.AddChild(this);
        AddChild(_pivot);
    }

    /// <summary>进入落位模式（把拔起来的那一栋放回去）。</summary>
    public void Enter(string buildingId)
    {
        var world = _world();
        var building = world?.Construction.Find(buildingId);
        if (world is null || building is null) return;

        _buildingId = buildingId;
        _blueprintId = building.BlueprintId;
        _origin = building.Anchor ?? default;

        RebuildVisual(_blueprintId);
        Visible = true;
    }

    /// <summary>退出落位模式（落位成功 / 取消之后）。</summary>
    public void Exit()
    {
        _buildingId = string.Empty;
        _blueprintId = string.Empty;
        Visible = false;
        ClearVisual();
    }

    /// <summary>
    /// 在甲板上点一下落位。回 true = 注入走成了（调用方据此退出落位模式）。
    /// 不在甲板上、或注入被拒都回 false（被拒的原因由 <c>Inject</c> 转给横幅）。
    /// </summary>
    public bool TryDropAt(Vector2 screen)
    {
        if (!Active) return false;
        if (!_picker.TryDeckLocal(screen, out var local)) return false;

        var cell = _picker.CellOfLocal(local);
        var result = _inject("DropBuilding", $"{_buildingId}|{cell.Col}|{cell.Row}");
        return string.Equals(result, "ok", StringComparison.Ordinal);
    }

    /// <summary>
    /// 取消落位：把那一栋放回它原来那一格（同一发 <c>DropBuilding</c>，锚点用原来那个）。
    /// 回 true = 放回去了。
    /// </summary>
    public bool Cancel()
    {
        if (!Active) return false;

        var result = _inject("DropBuilding", $"{_buildingId}|{_origin.Col}|{_origin.Row}");
        return string.Equals(result, "ok", StringComparison.Ordinal);
    }

    public override void _Process(double delta)
    {
        if (!Active) return;

        // 前端层开着（标题屏 / 暂停屏）或弹层开着：幽灵收起来，别在菜单上面晃。
        if (_blocked())
        {
            Visible = false;
            return;
        }

        Visible = true;
        UpdatePreview();
    }

    // === 预览 ===

    private void UpdatePreview()
    {
        var world = _world();
        if (world is null || _outline is null)
        {
            HidePreview();
            return;
        }

        var mouse = GetViewport().GetMousePosition();
        if (!_picker.TryDeckLocal(mouse, out var local))
        {
            HidePreview();
            return;
        }

        var cell = _picker.CellOfLocal(local);
        var spec = DeckBlueprints.SpecOf(_blueprintId);
        if (spec is null)
        {
            HidePreview();
            return;
        }

        // 放回自己现在这几格是空动作：那一格按绿算（判据与落位那一发同一句）。
        var ok = world.Deck.IsAtOwnAnchor(_buildingId, cell.Col, cell.Row)
            || world.Construction.Check(_blueprintId, DeckBlueprints.CandidateOf(cell)).Ok;

        // 锚点是占地里最小行最小列那一格；模型 / 轮廓摆在地盘中心（格心从 col + 0.5 起算）。
        var size = DeckBlueprints.CellSize;
        var centerX = (cell.Col + (spec.Width / 2.0)) * size;
        var centerZ = (cell.Row + (spec.Height / 2.0)) * size;
        Position = new Vector3((float)centerX, DeckView.TownY, (float)centerZ);

        _outline.Visible = true;
        foreach (var cellView in _cells) cellView.Visible = true;

        var cellMaterial = ok ? _greenCell : _redCell;
        foreach (var cellView in _cells) cellView.MaterialOverride = cellMaterial;
        _outline.MaterialOverride = ok ? _greenOutline : _redOutline;
    }

    private void HidePreview()
    {
        if (_outline is not null) _outline.Visible = false;
        foreach (var cellView in _cells) cellView.Visible = false;
    }

    private void RebuildVisual(string blueprintId)
    {
        ClearVisual();

        var spec = DeckBlueprints.SpecOf(blueprintId);
        if (spec is null) return;

        var size = DeckBlueprints.CellSize;
        var width = spec.Width * size;
        var height = spec.Height * size;

        _outline = new MeshInstance3D
        {
            Name = "Outline",
            Mesh = new BoxMesh { Size = new Vector3((float)width, OutlineHeightMeters, (float)height) },
            Position = new Vector3(0f, OutlineHeightMeters / 2f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _pivot.AddChild(_outline);

        // 每一格一片薄板：整片占地同色（Check 是整个占地一次判的，不逐格另算一份）。
        for (var row = 0; row < spec.Height; row++)
        {
            for (var col = 0; col < spec.Width; col++)
            {
                var plate = new MeshInstance3D
                {
                    Name = $"Cell{col}_{row}",
                    Mesh = new BoxMesh
                    {
                        Size = new Vector3((float)(size * 0.94), 0.04f, (float)(size * 0.94)),
                    },
                    Position = new Vector3(
                        (float)(((col + 0.5) * size) - (width / 2.0)),
                        CellLiftMeters,
                        (float)(((row + 0.5) * size) - (height / 2.0))),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _pivot.AddChild(plate);
                _cells.Add(plate);
            }
        }
    }

    private void ClearVisual()
    {
        foreach (var plate in _cells) plate.QueueFree();
        _cells.Clear();

        if (_outline is not null)
        {
            _outline.QueueFree();
            _outline = null;
        }
    }

    private static StandardMaterial3D MakeMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static Color Green(float fill = 0.45f) => new(0.25f, 0.85f, 0.35f, fill);

    private static Color Red(float fill = 0.50f) => new(0.92f, 0.25f, 0.22f, fill);
}
