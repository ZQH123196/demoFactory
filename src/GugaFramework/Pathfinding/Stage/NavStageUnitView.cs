#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Animation;
using Godot;

namespace DemoFactory.GugaFramework.Pathfinding.Stage;

/// <summary>
/// <b>跟随表现层</b>——把移动解摆进模型：一个单位三样（位置 / 朝向 / 基动作）。
///
/// <para><b>只读移动解，禁止第二次计算</b>（「表现层只读」，契约照建造层「预览一致性契约」那一条）：
/// 本类一个字都不算路、不算净空、不算前视 —— 位置、朝向、速度三列全部从
/// <see cref="NavStepper.SolutionOf"/> 那一份移动解原样读出来。
/// 画面上摆的与逻辑交出的是同一个数，所以「模型站在哪」与「快照说它站哪」永远对得上；
/// 表现层自己再算一遍的话，那两件事会在某一帧分叉，而画面上看不出分叉（「约束唯一出处」 可调试性第一）。</para>
///
/// <para><b>跑 / 走分档不另立</b>：喂给 <see cref="ModelAnimator.Travel"/> 的口径是「米每游戏秒」，
/// 分档线 14 在 <c>Animation/ModelAnimator.cs:62</c>，本类<b>不持有第二个阈值</b>
/// ——它只把 <see cref="NavStage.TravelMetersPerGameSecondOf"/> 那一列交出去。
/// <b>快进倍率由调用方除掉</b>：本类喂的是游戏秒口径的数，倍率那一头走
/// <see cref="ModelAnimator.GameSpeed"/>（它把倍率装进 <c>AnimationPlayer.SpeedScale</c>），
/// 不许在这里把倍率乘进速度 —— 乘一次就等于同一件事有两处算。</para>
///
/// <para><b>造型资产按 <c>res://</c> 路径引用，不按 uid</c>（「按 res:// 引用」）：模型路径由调用方递
/// （<see cref="ModelPath"/>），场子自己不揣一份造型 —— 「真模型」 管的是「造型不许运行时拼引擎图元」，
/// 而<b>摆位色带不是造型</b>，它是场地陈设（与技能试验场那一份同口径）。给了一条
/// <see cref="ModelPath"/> 就摆真模型，没给就摆色带，两条路都不假装成有模型。</para>
///
/// <para>朝向的口径是移动解那一列（0 度 = +Z、+90 = +X，值域 [−180, 180)）：
/// 本类只做引擎那一侧的换算（度 → <c>RotationDegrees.Y</c>），不重算朝向、
/// 不做插值、不加转身时间 —— 转身时间是造型与动作层的事（spec「Out of Scope」）。</para>
/// </summary>
public sealed partial class NavStageUnitView : Node3D
{
    /// <summary>
    /// 模型资源路径（<c>res://</c>，「按 res:// 引用」）。<b>空 = 不摆模型，只摆色带与名牌</b>；
    /// 非空时按路径 <c>Load</c> 一个 <see cref="PackedScene"/> 并实例化，动画交给
    /// <see cref="ModelAnimator"/>（原型/宿主递造型，寻路层不认识任何兵种）。
    /// </summary>
    [Export] public string ModelPath { get; set; } = string.Empty;

    private MeshInstance3D? _band;
    private Label3D? _plate;
    private Node3D? _model;
    private ModelAnimator? _animator;

    /// <summary>
    /// 名牌沿 Z 错开几米（<b>沿 Z，不沿高度</b>：照技能试验场那一处的实测 ——
    /// 近俯视那一档俯角 72 度，沿高度错 1 米在画面上只剩几个像素，沿 Z 错开则不衰减）。
    /// 错开量按单位在名册里的次序给，两个单位开局挨着时不至于互相压叠。
    /// </summary>
    private readonly float _plateLayerZ;

    /// <summary>名牌上要念的那几列（人读；真值在逻辑壳的快照里）。</summary>
    public string PlateText { get; private set; } = string.Empty;

    /// <summary>动画诊断读数（想要哪条 clip / 实际在播哪条 / clip 在不在表里）。</summary>
    public string AnimationReadout() => _animator?.DebugReadout() ?? "anim=none";

    /// <summary>造这个单位的视图。<paramref name="unitId"/> 只当键用，场子不认识它。</summary>
    public NavStageUnitView(string unitId, Color color, double plateHeightMeters, int plateLayerIndex)
    {
        Name = unitId;
        _plateLayerZ = plateLayerIndex * 2.4f;
        var band = new MeshInstance3D
        {
            Name = "Band",
            Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.05f, 0.6f) },
            MaterialOverride = FlatMaterial(color),
            Position = new Vector3(0f, 0.025f, 0f),
        };
        AddChild(band);
        _band = band;

        var plate = new Label3D
        {
            Name = "Plate",
            Position = new Vector3(0f, (float)plateHeightMeters, 0f),
            PixelSize = 0.02f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Modulate = Colors.White,
        };
        AddChild(plate);
        _plate = plate;
    }

    /// <summary>按路径把真模型挂上来（<b>按 res:// 路径，不按 uid</b>，「按 res:// 引用」）。没给路径时什么都不做。</summary>
    public void AttachModel()
    {
        if (_model is not null || ModelPath.Length == 0)
        {
            return;
        }

        if (ResourceLoader.Load<PackedScene>(ModelPath) is not { } packed)
        {
            GD.PushWarning($"[navstage] 这一件没有那条造型资源：{ModelPath} —— 按路径没读到，画面上只有色带");
            return;
        }

        // 先 Instantiate 再判类型：根节点不是三维节点时 `Instantiate<Node3D>()` 会把已经造出来的
        // 那个节点丢掉（不释放），所以这里用不指定类型的那一发，判完再决定留不留。
        var instance = packed.Instantiate();
        if (instance is not Node3D model)
        {
            GD.PushWarning($"[navstage] 造型资源的根节点不是三维节点：{ModelPath}");
            instance.QueueFree();
            return;
        }

        model.Name = "Model";
        AddChild(model);
        _model = model;
        _animator = new ModelAnimator(model);
    }

    /// <summary>
    /// 把移动解那一列摆进画面。<b>纯只读</b>：位置 / 朝向 / 速度三列都从
    /// <paramref name="solution"/> 读，本方法不碰网格、不碰求解器。
    /// </summary>
    public void Apply(in NavMoveSolution solution, double plateHeightMeters, float platePixelSize,
        float gameSpeed)
    {
        var at = new Vector3((float)solution.Position.X, 0f, (float)solution.Position.Z);

        // 朝向：0 度 = +Z、+90 = +X。引擎的 Y 轴旋转是 +Z 起算、与本层同口径（第三期 3.6）。
        var yaw = (float)solution.FacingDegrees;
        Position = at;
        RotationDegrees = new Vector3(0f, yaw, 0f);
        if (_band is not null)
        {
            _band.RotationDegrees = Vector3.Zero;
        }

        if (_plate is not null)
        {
            _plate.Position = new Vector3(0f, (float)plateHeightMeters, _plateLayerZ);
            _plate.PixelSize = platePixelSize;
            PlateText = $"{solution.AgentId}  v={solution.SpeedMetersPerSecond:0.##}  {solution.Status}";
            _plate.Text = PlateText;
        }

        // 跑 / 走分档只在 ModelAnimator 那一处（分档线 14 在 ModelAnimator.cs:62）。
        // 这里喂的是**米每游戏秒**，快进倍率走 GameSpeed 那一头，不乘进这里 ——
        // 乘一次就等于「速度」这件事有两处算，两处会漂。
        if (_animator is not null)
        {
            _animator.GameSpeed = Mathf.Max(gameSpeed, 0.0001f);
            _animator.Travel((float)solution.SpeedMetersPerSecond);
        }
    }

    private static StandardMaterial3D FlatMaterial(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
    };

    /// <summary>本件占的画面位置（米，地面点）——机位跟随与点选用它。</summary>
    public Vector3 Ground => new(Position.X, 0f, Position.Z);
}
