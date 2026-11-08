#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 建造模式的那只幽灵 —— 建筑轮廓跟着光标，脚下格子按建造模块的裁决涂绿 / 红。
///
/// <para><b>格色的唯一判据</b>是
/// <c>world.Construction.Check(blueprintId, DeckBlueprints.CandidateOf(cell))</c>：
/// 与真正落座那一发走的是同一条流水线（预览与结果是同一句话），表现层不另算一遍距离或占用。</para>
///
/// <para><b>落座只有一条写路径</b>：在甲板上点一下 → <c>Inject("PlaceBuilding", "蓝图|列|行")</c>。
/// 成功才退出建造模式；被拒（钱不够 / 格被占 / 出界）停在原地，横幅写原因。</para>
///
/// <para><b>右键退出</b>由 <see cref="Main"/> 接 <c>RightClicked</c> 调 <see cref="Exit"/>，
/// 不抢 Esc（Esc 归前端层）。</para>
/// </summary>
public partial class BuildGhost : Node3D
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

    // 四份材质建一次、反复贴 ——— 每帧新建 Resource 会在引擎里堆垃圾。
    private readonly StandardMaterial3D _greenCell = MakeMaterial(Green());
    private readonly StandardMaterial3D _redCell = MakeMaterial(Red());
    private readonly StandardMaterial3D _greenOutline = MakeMaterial(Green(fill: 0.16f));
    private readonly StandardMaterial3D _redOutline = MakeMaterial(Red(fill: 0.18f));

    private string _blueprintId = string.Empty;

    /// <summary>现在是不是在建造模式。</summary>
    public bool Active => _blueprintId.Length > 0;

    /// <summary>正在建哪一张蓝图（不在建造模式回空串）。</summary>
    public string BlueprintId => _blueprintId;

    public BuildGhost(
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

        Name = "BuildGhost";
        Visible = false;
        deck.LevitationNode.AddChild(this);
        AddChild(_pivot);
    }

    /// <summary>进入建造模式（同一张蓝图再进一次也重建一份轮廓）。</summary>
    public void Enter(string blueprintId)
    {
        if (DeckBlueprints.SpecOf(blueprintId) is null) return;

        _blueprintId = blueprintId;
        RebuildVisual(blueprintId);
        Visible = true;
    }

    /// <summary>退出建造模式（右键 / 落座成功 / 重开一局）。</summary>
    public void Exit()
    {
        _blueprintId = string.Empty;
        Visible = false;
        ClearVisual();
    }

    /// <summary>
    /// 在甲板上点一下落座。回 true = 注入走成了（调用方据此退出建造模式与清槽位选中态）。
    /// 不在甲板上、或注入被拒都回 false（被拒的原因由 <c>Inject</c> 转给横幅）。
    /// </summary>
    public bool TryPlaceAt(Vector2 screen)
    {
        if (!Active) return false;
        if (!_picker.TryDeckLocal(screen, out var local)) return false;

        var cell = _picker.CellOfLocal(local);
        var result = _inject("PlaceBuilding",
            $"{_blueprintId}|{cell.Col}|{cell.Row}");
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

        var candidate = DeckBlueprints.CandidateOf(cell);
        var ok = world.Construction.Check(_blueprintId, candidate).Ok;

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
