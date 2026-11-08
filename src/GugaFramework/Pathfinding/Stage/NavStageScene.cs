#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.Replay;
using Godot;

namespace DemoFactory.GugaFramework.Pathfinding.Stage;

/// <summary>
/// <b>场景壳</b>——引擎侧那一半：叠加层（网格 / 阻挡格 / 净空 / 前视方向 / 求解记录浮层）、
/// 跟随表现层（<see cref="NavStageUnitView"/>）、机位与调试入口。纯表现，不碰真值。
///
/// <para>分工（照技能试验场那一份的两个壳）：<b>逻辑壳 <c>NavStage.cs</c> 是纯 C#、零引擎</b>，
/// 本文件只做「把那份真值画出来」。表现层只读（「表现层只读」）：本文件一个字都不往世界里写，
/// 推进时间走 <see cref="NavStage.AdvanceRealSeconds"/>（<b>倍率与攒步那笔账在逻辑壳里</b>，
/// 这里不再自己算一遍 —— 两处算就会漂），改状态一律走 <see cref="NavStage.Inject"/> 那一条口。</para>
///
/// <para><b>画面上六样</b>，够看清就够：
/// <list type="number">
/// <item><b>寻路网格</b>：一格一格画在地面上，兼作「这一下为什么是这个结果」的距离参照。</item>
/// <item><b>阻挡格</b>：封掉的那几格实心画出 —— 地形是网格的输入，看得见才谈得上「它绕开了哪一格」。</item>
/// <item><b>净空</b>：每一格按净空值上色（近阻挡偏红、场地中央偏蓝）。
/// <b>单位半径腐蚀掉的那一圈就是可行域的边界</b>，半径这一列只在画面上、读数在逻辑壳的网格段里。</item>
/// <item><b>前视方向</b>：每个单位从脚下画到移动解那一列的前视点，再画一小段朝向 ——
/// 「方向取自前视点、步长与前视点无关」那一条（第三期 3.3）在画面上是两段长度不同的线。</item>
/// <item><b>单位名牌 + 色带</b>：<see cref="NavStageUnitView"/>。名牌念速度 / 状态 / 净位移三列读数。</item>
/// <item><b>求解记录浮层</b>：左上角逐行念最近的求解记录，带 kept / dropped。</item>
/// </list></para>
///
/// <para><b>路径折线画的是层交出来的那一份，本文件不自己算</b>：走
/// <see cref="NavStepper.CornersOf"/> 那个只读口（工单 07 最后一轮补上的）。
/// 补这个口之前这里是<b>画不出来</b>的：移动解（<see cref="NavMoveSolution"/>）只交出
/// <b>目标</b>与<b>前视点</b>，而 <see cref="NavSolver"/> 手里那条 <see cref="NavPath"/>
/// 是流水线内部的局部变量，层里没有只读口。<b>本文件仍然不调 <c>Solve</c> 补画</b> ——
/// 那正是「第二次计算」，画面上摆的路与逻辑走的路会在某一帧分叉而画面上看不出来。
/// 折线本身的口径、画成窄带而不是 1 像素线、以及那三个只有单点的档，见
/// <see cref="DrawPathLines"/> 的头注。</para>
///
/// <para><b>上屏的字一律 ASCII</b>：Godot 缺省字体没有汉字字形，中文那行出图实测是整行空白
/// （技能试验场 2026-10-01 实测）。所以浮层那一块是<b>ASCII 投影</b>，
/// 逐字真值仍然是逻辑壳的 <see cref="NavStage.SolveRecord"/>（走 eval 直达那一口能原样取到）。</para>
///
/// <para><b>机位走既有的 <see cref="TopDownRig"/></b>（「相机单源」）：本文件只给焦点 / 距离 /
/// 俯角 / 边界这几个参数，两套预设（全景 / 跟随），「锁谁」由 <see cref="CycleLock"/> 换。
/// <b>机位是表现层动词，不进录制</b>（「写路径进 Journal」）：切预设与锁谁都不走 <see cref="NavStage.Inject"/>，
/// 所以挪镜头不会让重放误报分岔；机位参数也不进检查点快照（「试验场随技能层」）——
/// <see cref="NavStage.DebugSnapshot"/> 是逻辑壳出的，那边根本没有相机。</para>
/// </summary>
public partial class NavStageScene : Node3D
{
    // === 机位参数（「相机单源」：场子只给焦点 / 距离 / 俯角 / 边界这几个）===

    /// <summary>机位偏航（度）。缺省 0（俯视，+Z 朝画面上方）。</summary>
    [Export] public float CameraYawDegrees { get; set; }

    /// <summary>全景俯角（度）：近俯视 —— 地面透视压缩最小的一档，网格与净空那一圈不压扁。</summary>
    private const double OverviewPitchDegrees = 72;

    /// <summary>跟随俯角（度）：斜到能看出前后层次。</summary>
    private const double FollowPitchDegrees = 55;

    /// <summary>全景那一套：画面里至少装下这么宽的一块地（米，见方），按场地尺寸推算。</summary>
    private const double OverviewSpanMeters = 26;

    /// <summary>跟随那一套：画面里装下这么宽（米，见方）——看得见锁定那个单位与它的前视线。</summary>
    private const double FollowSpanMeters = 16;

    /// <summary>名牌的参考距离（米）：字高按机位距离缩放，不缩放的话特写那档会宽到出画。</summary>
    private const double PlateReferenceDistanceMeters = 26;

    /// <summary>名牌在参考距离上的字高系数（世界高 = 字号 × 这一格）。</summary>
    private const float PlatePixelSize = 0.022f;

    /// <summary>名牌离地多高（米）。</summary>
    private const double PlateHeightMeters = 1.1;

    // === 时间控制（表现层这一侧只管「本帧该推几步」，那笔账在逻辑壳里）===

    /// <summary>倍率：乘在逻辑流速上。1 = 现实时间（1 步 = 0.1 秒，「时间基准」）。</summary>
    [Export] public double Speed { get; set; } = 1.0;

    /// <summary>暂停：只冻逻辑，画面照跑。</summary>
    [Export] public bool Paused { get; set; }

    /// <summary>本局种子。读数里念出来：不然「这一局是哪一档」要回头翻代码。</summary>
    [Export] public int Seed { get; set; } = 1;

    /// <summary>起局就在哪一套机位（<c>overview</c> / <c>follow</c>）。出图复核靠这一格把起点钉死。</summary>
    [Export] public string StartView { get; set; } = "overview";

    // === 读数口 ===

    /// <summary>逻辑壳。场子只把它读出来画，一句话都不往里写（「表现层只读」）。</summary>
    public NavStage? Stage { get; private set; }

    /// <summary>接新原型时递自己那一份注册口进来。为 null 时本场用示范注册壳（不是缺省词表）。</summary>
    public NavStageSpec? Spec { get; set; }

    /// <summary>每个单位的模型路径（<c>res://</c>，「按 res:// 引用」），键是单位 id。<b>没给的单位只摆色带。</b></summary>
    public Dictionary<string, string> ModelPaths { get; } = new(StringComparer.Ordinal);

    private TopDownRig? _rig;
    private ImmediateMesh? _cellMesh;
    private ImmediateMesh? _gridMesh;
    private ImmediateMesh? _aimMesh;
    private ImmediateMesh? _pathMesh;
    private MeshInstance3D? _cellHost;
    private MeshInstance3D? _gridHost;
    private MeshInstance3D? _aimHost;
    private MeshInstance3D? _pathHost;
    private Label? _panel;
    private StandardMaterial3D? _cellMaterial;
    private StandardMaterial3D? _lineMaterial;
    private StandardMaterial3D? _aimMaterial;
    private StandardMaterial3D? _pathMaterial;
    private readonly List<NavStageUnitView> _views = new();
    private int _lockIndex = -1;

    public override void _Ready()
    {
        var spec = Spec ?? NavStageSample.Spec();
        Stage = new NavStage(spec, Seed);
        Stage.Speed = Speed;
        Stage.Paused = Paused;

        BuildOverlay();
        BuildViews(spec);
        BuildPanel();
        BuildCamera();

        _lockIndex = _views.Count > 0 ? 0 : -1;
        ApplyViewByName(StartView);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (Stage is null)
        {
            return;
        }

        Stage.Speed = Speed;
        Stage.Paused = Paused;
        if (Stage.AdvanceRealSeconds(delta) > 0)
        {
            Refresh();
        }
    }

    // === 叠加层 ===

    private void BuildOverlay()
    {
        _cellMaterial = Flat(0.55f);
        _lineMaterial = Flat(0.5f);
        _aimMaterial = Flat(0.9f);

        _cellMesh = new ImmediateMesh();
        _cellHost = new MeshInstance3D { Name = "CellBands", Mesh = _cellMesh, MaterialOverride = _cellMaterial };
        AddChild(_cellHost);

        _gridMesh = new ImmediateMesh();
        _gridHost = new MeshInstance3D { Name = "GridLines", Mesh = _gridMesh, MaterialOverride = _lineMaterial };
        AddChild(_gridHost);

        _aimMesh = new ImmediateMesh();
        _aimHost = new MeshInstance3D { Name = "AimLines", Mesh = _aimMesh, MaterialOverride = _aimMaterial };
        AddChild(_aimHost);

        _pathMaterial = Flat(1f);
        _pathMesh = new ImmediateMesh();
        _pathHost = new MeshInstance3D { Name = "PathLines", Mesh = _pathMesh, MaterialOverride = _pathMaterial };
        AddChild(_pathHost);

        DrawCells();
        DrawGridLines();
    }

    private void BuildViews(NavStageSpec spec)
    {
        var palette = new[] { new Color(0.35f, 0.75f, 1f), new Color(1f, 0.72f, 0.25f), new Color(0.55f, 0.95f, 0.5f) };
        var units = spec.Roster.Units;
        for (var i = 0; i < units.Count; i++)
        {
            var view = new NavStageUnitView(units[i].Id, palette[i % palette.Length], PlateHeightMeters, i);
            AddChild(view);
            if (ModelPaths.TryGetValue(units[i].Id, out var path))
            {
                view.ModelPath = path;
            }

            view.AttachModel();
            _views.Add(view);
        }
    }

    private void BuildPanel()
    {
        var canvas = new CanvasLayer { Name = "Panel" };
        AddChild(canvas);
        var label = new Label
        {
            Name = "SolveRecord",
            Position = new Vector2(10, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // 字号是**表现层陈设**：缺省 16 号那块板会盖住半个场地（出图实测：L 形墙那一行整行被白字压住，
        // 「墙画出来没有」这件事在图上答不了）。压到 13 号 + 少列几行之后，板与场地各占各的。
        label.AddThemeFontSizeOverride("font_size", 13);
        canvas.AddChild(label);
        _panel = label;
    }

    // === 机位（「相机单源」：相机一律走 TopDownRig，本文件只给参数）===

    private void BuildCamera()
    {
        var extent = (float)Stage!.Grid.ExtentXmeters;
        var rig = new TopDownRig
        {
            Name = "TopDownRig",
            MinDistance = 4f,
            MaxDistance = 160f,
            ReferenceDistance = (float)PlateReferenceDistanceMeters,
            BoundsMinXZ = new Vector2(-extent, -extent),
            BoundsMaxXZ = new Vector2(extent, extent),
        };
        AddChild(rig);
        if (GetNodeOrNull<Camera3D>("Camera3D") is { } camera)
        {
            rig.AttachCamera(camera);
        }

        _rig = rig;
    }

    /// <summary>切到哪一套机位（<c>overview</c> / <c>follow</c>）。<b>表现层动词，不进注入</b>（「写路径进 Journal」）。</summary>
    public void ApplyViewByName(string view)
    {
        if (_rig is null || Stage is null)
        {
            return;
        }

        if (view == "follow" && _lockIndex >= 0 && _lockIndex < _views.Count)
        {
            var target = _views[_lockIndex];
            _rig.FollowTarget(target);
            _rig.SetPose(target.Ground, FitDistance(FollowSpanMeters), CameraYawDegrees, (float)FollowPitchDegrees);
            return;
        }

        _rig.FollowTarget(null);
        _rig.SetPose(
            new Vector3((float)Stage.Grid.ExtentXmeters / 2f, 0f, (float)Stage.Grid.ExtentZmeters / 2f),
            FitDistance(OverviewSpanMeters), CameraYawDegrees, (float)OverviewPitchDegrees);
    }

    /// <summary>按视场角与画面比例算「装下那么宽的一块地要退多远」（不是量出来的常数）。</summary>
    private float FitDistance(double spanMeters)
    {
        var fov = _rig?.Camera?.Fov ?? 75f;
        var aspect = GetViewport().GetVisibleRect().Size.X / Math.Max(GetViewport().GetVisibleRect().Size.Y, 1f);
        return (float)(spanMeters / (2.0 * Math.Tan(fov * Math.PI / 360.0)));
    }

    /// <summary>换一个锁定对象（Tab 轮换）。「锁谁」是玩法语义，留在场子侧（「相机单源」 那半句）。</summary>
    public void CycleLock()
    {
        if (_views.Count == 0)
        {
            return;
        }

        _lockIndex = (_lockIndex + 1) % _views.Count;
        ApplyViewByName("follow");
        Refresh();
    }

    // === 重画（只读）===

    /// <summary>把逻辑那份真值重画一遍。<b>只读，不写</b>。</summary>
    public void Refresh()
    {
        if (Stage is null)
        {
            return;
        }

        var plateScale = (float)(Math.Max(_rig?.Distance ?? PlateReferenceDistanceMeters, 0.5)
                               / PlateReferenceDistanceMeters);

        for (var i = 0; i < _views.Count; i++)
        {
            // 走位只读移动解：位置 / 朝向 / 速度三列全从这一份读，本文件不再算一遍（「表现层只读」）。
            var solution = Stage.Stepper.SolutionOf(Stage.Stepper.AgentIds[i]);
            _views[i].Apply(solution, PlateHeightMeters, PlatePixelSize * plateScale, (float)Stage.Speed);
        }

        DrawAimLines();
        DrawPathLines();
        DrawCells();
        if (_panel is not null)
        {
            _panel.Text = PanelText();
        }
    }

    /// <summary>
    /// 求解记录浮层那一块（<b>ASCII 投影</b>：上屏的字一律 ASCII，理由见类型注释）。
    /// 逐字真值仍然是 <see cref="NavStage.SolveRecord"/>，走 <see cref="SolveRecordText"/> 原样取。
    /// </summary>
    public string PanelText()
    {
        if (Stage is null)
        {
            return "stage=none";
        }

        var lines = new List<string>
        {
            $"step {Stage.Tick} | {ReplayArgs.Number(Stage.Tick / 10.0)} s | speed {ReplayArgs.Number(Stage.Speed)}" +
            $" | {(Stage.Paused ? "paused" : "running")}",
            $"grid {Stage.Grid.CellsX}x{Stage.Grid.CellsZ} cell {ReplayArgs.Number(Stage.Grid.CellMeters)} m" +
            $" | origin {ReplayArgs.Number(Stage.Grid.OriginX)},{ReplayArgs.Number(Stage.Grid.OriginZ)}" +
            $" | static v{Stage.Grid.StaticVersion} | dynamic v{Stage.Grid.DynamicVersion}",
            $"camera {(_rig?.DebugReadout() ?? "none")}",
            "lines magenta=path-in-hand cyan=aim green=goal",
            Stage.Readout(),
            string.Empty,
        };
        lines.AddRange(SolvePanelLines(5));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 求解记录那一块的逐行 ASCII 投影：<c>kept / dropped</c> 带头一行（「三条铁律」 ③），
    /// 每行念序号 / 状态 / 原因 / 扩展格数 / 拐点数 / 缓存命中。够定位「它为什么重算了」。
    /// </summary>
    public IReadOnlyList<string> SolvePanelLines(int tail)
    {
        if (Stage is null)
        {
            return Array.Empty<string>();
        }

        var records = Stage.Solver.Records;
        var sb = new List<string> { $"solve kept={records.Count} dropped={records.Dropped} capacity={records.Capacity}" };
        var rows = records.Snapshot();
        var from = Math.Max(0, rows.Length - Math.Max(0, tail));
        for (var i = from; i < rows.Length; i++)
        {
            var r = rows[i];
            sb.Add($"#{r.Sequence} {r.Status}/{r.Reason} exp={r.ExpandedCells} corners={r.CornerCount} " +
                   $"cost={ReplayArgs.Number(r.TotalCost)} cache={(r.CacheHit ? "hit" : "miss")} " +
                   $"{(r.IsPartial ? "partial" : "full")}");
        }

        return sb;
    }

    // === 画那三样 ===

    private void DrawCells()
    {
        if (Stage is null || _cellMesh is null || _cellMaterial is null)
        {
            return;
        }

        var grid = Stage.Grid;
        var cell = (float)grid.CellMeters;
        var originX = (float)grid.OriginX;
        var originZ = (float)grid.OriginZ;

        // 归一化上界取**这张网格自己的最大净空**（扫一遍求 max），不是贴边那格 ——
        // 拿边缘值当上界的话，场内每一格都饱和成同一个颜色（出图实测：16×16 那一局整片全蓝）。
        var maxClearance = 0.0;
        for (var z = 0; z < grid.CellsZ; z++)
        {
            for (var x = 0; x < grid.CellsX; x++)
            {
                maxClearance = Math.Max(maxClearance, grid.Clearance.At(x, z));
            }
        }

        _cellMaterial.VertexColorUseAsAlbedo = true;

        _cellMesh.ClearSurfaces();
        _cellMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _cellMaterial);
        for (var z = 0; z < grid.CellsZ; z++)
        {
            for (var x = 0; x < grid.CellsX; x++)
            {
                var c = new NavCell(x, z);
                // 每格的世界位置从网格原点起算：零基场子起点是 0，带原点的场子起点是原点那一格。
                var left = originX + (x * cell);
                var top = originZ + (z * cell);
                var color = grid.IsBlocked(c)
                    ? new Color(0.12f, 0.12f, 0.14f, 0.95f)
                    : ClearanceColor(grid.Clearance.At(c), maxClearance);
                Quad(left, top, cell, color);
            }
        }

        _cellMesh.SurfaceEnd();
    }

    /// <summary>净空那一层上色：贴阻挡偏红、场地中央偏蓝。读数在逻辑壳的网格段里（逐格净空米数）。</summary>
    private static Color ClearanceColor(double clearance, double maxClearance)
    {
        var t = (float)Math.Clamp(clearance / Math.Max(maxClearance, 1e-6), 0.0, 1.0);
        return new Color(0.95f - (0.65f * t), 0.25f + (0.35f * t), 0.25f + (0.7f * t), 0.42f);
    }

    private void DrawGridLines()
    {
        if (Stage is null || _gridMesh is null || _lineMaterial is null)
        {
            return;
        }

        var grid = Stage.Grid;
        var cell = (float)grid.CellMeters;
        var originX = (float)grid.OriginX;
        var originZ = (float)grid.OriginZ;
        var maxX = (float)grid.MaxXMeters;
        var maxZ = (float)grid.MaxZMeters;
        _lineMaterial.VertexColorUseAsAlbedo = true;
        var gray = new Color(0.75f, 0.78f, 0.85f, 0.5f);

        _gridMesh.ClearSurfaces();
        _gridMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMaterial);
        for (var x = 0; x <= grid.CellsX; x++)
        {
            var at = originX + (x * cell);
            Line(_gridMesh, at, originZ, at, maxZ, gray);
        }

        for (var z = 0; z <= grid.CellsZ; z++)
        {
            var at = originZ + (z * cell);
            Line(_gridMesh, originX, at, maxX, at, gray);
        }

        _gridMesh.SurfaceEnd();
    }

    /// <summary>
    /// 前视方向那一段：每个单位画两段 —— 到前视点的（亮青）与到目标的（暗绿）。
    /// <b>两段长度不同正是要看的</b>：方向取自前视点，步长与前视点无关（第三期 3.3）。
    /// 朝向不另画短线，它就是节点的 Y 轴旋转，名牌与色带跟着一起转。
    /// 逐单位重画（<see cref="ImmediateMesh"/> 每一帧重建，容量是「单位数 × 两段」）。
    /// </summary>
    private void DrawAimLines()
    {
        if (Stage is null || _aimMesh is null || _aimMaterial is null)
        {
            return;
        }

        _aimMaterial.VertexColorUseAsAlbedo = true;
        _aimMesh.ClearSurfaces();
        _aimMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _aimMaterial);
        for (var i = 0; i < _views.Count; i++)
        {
            var solution = Stage.Stepper.SolutionOf(Stage.Stepper.AgentIds[i]);
            var here = solution.Position;
            var aim = solution.Aim;
            var cyan = new Color(0.35f, 1f, 0.95f, 0.95f);
            // 前视线：长段（方向取自它）。
            Line(_aimMesh, here.X, here.Z, aim.X, aim.Z, cyan);
            // 目标连线：细一点的一段（目标只作读数交回，不当停靠点）。
            var goal = solution.Goal;
            var dim = new Color(0.35f, 0.95f, 0.35f, 0.4f);
            Line(_aimMesh, here.X, here.Z, goal.X, goal.Z, dim);
        }

        _aimMesh.SurfaceEnd();
    }

    /// <summary>
    /// 本步正在走的那条折线（<b>洋红</b>，一段一条直线连成折线）——
    /// 走的是 <see cref="NavStepper.CornersOf"/> 那个只读口，**本文件不求解**。
    ///
    /// <para><b>三种线三种颜色，别混成一种</b>：洋红 = 这一步在走的那条路（折线）、
    /// 亮青 = 到前视点那一段（方向取自它）、暗绿 = 到目标那一段（目标只作读数交回）。
    /// 颜色写进浮层的图例那一行，所以出图那张图自己说得清哪条是哪条。</para>
    ///
    /// <para><b>画的是折线本身，一个点都不加不减</b>：首位就是求解时的起点（步首位置），
    /// 被避让推开的那一步之后它与脚下有一点点错位 —— 那是真实读数，不在视图这一层抹平。
    /// 只有一个点的那几步（已到位 / 激活检查拒了 / 还没发令）画不出线段，于是画面上那一处没有折线：
    /// 那正是「这一步没有正在走的路」。</para>
    ///
    /// <para><b>为什么画成窄带而不是 <see cref="Mesh.PrimitiveType.Lines"/> 那一种 1 像素线</b>：
    /// 出图实测过 —— 1 像素洋红压在粉紫底格上，一个洋红像素都扫不出来，整条线看着发白，
    /// 和亮青那条分不开。Godot 4 的 3D 线段没有笔宽可调，于是这里按半宽 <see cref="PathHalfWidthMeters"/>
    /// 摊成两个三角形的窄带，材质不透明，出图上颜色就是读出来的那一列。
    /// 摊宽只加笔宽，<b>不加折点</b>。</para>
    ///
    /// <para><b>一个线段都没有时不开面</b>：全体到位那几步每条折线都只有一个点、画不出线段，
    /// 照样 <c>SurfaceEnd</c> 会被 Godot 判成「No vertices were added, surface can't be created」
    /// 每帧刷一条 error（实测一局 283 条）。所以先数线段，数到 0 就只清面、不开面。</para>
    /// </summary>
    private void DrawPathLines()
    {
        if (Stage is null || _pathMesh is null || _pathMaterial is null)
        {
            return;
        }

        _pathMaterial.VertexColorUseAsAlbedo = true;
        _pathMesh.ClearSurfaces();

        var segments = 0;
        for (var i = 0; i < _views.Count; i++)
        {
            segments += Math.Max(0, Stage.Stepper.CornersOf(Stage.Stepper.AgentIds[i]).Count - 1);
        }

        if (segments == 0)
        {
            return;
        }

        _pathMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _pathMaterial);
        var magenta = new Color(1f, 0.45f, 0.8f, 1f);
        for (var i = 0; i < _views.Count; i++)
        {
            var corners = Stage.Stepper.CornersOf(Stage.Stepper.AgentIds[i]);
            for (var k = 0; k + 1 < corners.Count; k++)
            {
                Ribbon(_pathMesh, corners[k].X, corners[k].Z, corners[k + 1].X, corners[k + 1].Z, magenta);
            }
        }

        _pathMesh.SurfaceEnd();
    }

    private void Quad(float left, float top, float cell, Color color)
    {
        const float y = 0.01f;
        _cellMesh!.SurfaceSetColor(color);
        _cellMesh.SurfaceAddVertex(new Vector3(left, y, top));
        _cellMesh.SurfaceAddVertex(new Vector3(left + cell, y, top));
        _cellMesh.SurfaceAddVertex(new Vector3(left + cell, y, top + cell));
        _cellMesh.SurfaceSetColor(color);
        _cellMesh.SurfaceAddVertex(new Vector3(left, y, top));
        _cellMesh.SurfaceAddVertex(new Vector3(left + cell, y, top + cell));
        _cellMesh.SurfaceAddVertex(new Vector3(left, y, top + cell));
    }

    /// <summary>往给定的那张 <see cref="ImmediateMesh"/> 上加一条线段（网格线与前视线各用自己那张）。</summary>
    private static void Line(ImmediateMesh mesh, double x0, double z0, double x1, double z1, Color color)
    {
        const float y = 0.05f;
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(new Vector3((float)x0, y, (float)z0));
        mesh.SurfaceAddVertex(new Vector3((float)x1, y, (float)z1));
    }

    /// <summary>
    /// 往给定的那张 <see cref="ImmediateMesh"/> 上加一条<b>有宽度的</b>线段（两个三角形摊开）。
    /// 只摊笔宽，端点就是给的那两个点，中途不插点。
    /// 高度 <see cref="PathY"/> 比网格线与前视线高一层，三种线在同一处交叠时折线压在上面。
    /// </summary>
    private static void Ribbon(ImmediateMesh mesh, double x0, double z0, double x1, double z1, Color color)
    {
        const float y = PathY;
        var ax = (float)x0;
        var az = (float)z0;
        var bx = (float)x1;
        var bz = (float)z1;
        // 横向偏移取线段在 XZ 平面上的垂线，除以长度即单位向量，再乘半宽；点与点之间不插任何点。
        var dx = bx - ax;
        var dz = bz - az;
        var len = MathF.Sqrt((dx * dx) + (dz * dz));
        if (len < 1e-6f)
        {
            return;
        }

        var nx = (-dz / len) * PathHalfWidthMeters;
        var nz = (dx / len) * PathHalfWidthMeters;
        var a1 = new Vector3(ax + nx, y, az + nz);
        var a2 = new Vector3(ax - nx, y, az - nz);
        var b1 = new Vector3(bx + nx, y, bz + nz);
        var b2 = new Vector3(bx - nx, y, bz - nz);
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(a1);
        mesh.SurfaceAddVertex(b2);
        mesh.SurfaceAddVertex(b1);
        mesh.SurfaceAddVertex(a1);
        mesh.SurfaceAddVertex(a2);
        mesh.SurfaceAddVertex(b2);
    }

    /// <summary>折线窄带的高度：压在网格线（0.05）与前视线（0.05）之上。</summary>
    private const float PathY = 0.08f;

    /// <summary>折线窄带的半宽（米）。0.12 米 ≈ 3 像素（出图 1152 宽、盘 16 米），够认出是条线又不盖住格子。</summary>
    private const float PathHalfWidthMeters = 0.12f;

    private static StandardMaterial3D Flat(float alpha) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        AlbedoColor = new Color(1f, 1f, 1f, alpha),
    };

    // === eval 直达（游戏侧读状态 / 注入的入口；逻辑仍在逻辑壳那一份）===

    /// <summary>一发读出全量真值（值就是逻辑壳那一份快照，这里不做任何加工）。</summary>
    public string Snapshot() => Stage?.DebugSnapshot() ?? "stage=none";

    /// <summary>求解记录的<b>逐字</b>真值（含中文那一版，agent 出图后读它）。</summary>
    public string SolveRecordText() => Stage?.SolveRecord() ?? "stage=none";

    /// <summary>观测窗口读数（含 <c>dropped</c>）。</summary>
    public string Readout() => Stage?.Readout() ?? "stage=none";

    /// <summary>走位喂 <c>ModelAnimator.Travel</c> 的那个数（米每游戏秒，快进倍率由调用方除掉）。</summary>
    public string TravelReadout()
    {
        if (Stage is null)
        {
            return "stage=none";
        }

        var sb = new StringBuilder();
        foreach (var id in Stage.Stepper.AgentIds)
        {
            sb.Append(id).Append(' ').Append(ReplayArgs.Number(Stage.TravelMetersPerGameSecondOf(id)));
            var view = _views.Find(v => v.Name.ToString() == id);
            if (view is not null)
            {
                sb.Append("  anim=").Append(view.AnimationReadout());
            }

            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>注入那一发的完整读数（写路径只有 <c>Inject</c> 一条，「桥只转发」）。</summary>
    public string InjectText(string cmd, string args) => Stage?.InjectReadout(cmd, args) ?? "stage=none";

    /// <summary>推 n 步（批量单步调试口；暂停中也照走）。给「跳到第 k 步看那一格」用。</summary>
    public long AdvanceDebug(long steps)
    {
        if (Stage is null)
        {
            return 0;
        }

        var moved = Stage.AdvanceDebug(steps);
        Refresh();
        return moved;
    }

    public override void _ExitTree() => Stage?.Detach();
}
