#nullable enable
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.I18n;
using Godot;

namespace DemoFactory.GugaFramework.FrontEnd.Stage;

/// <summary>
/// <b>UI 试验场的场景壳</b>——引擎侧那一半。菜单要浮在<b>真的东西</b>上面才验得出来：
/// 空背景上打开设置屏，看着「正常」；压在一堆模型与 HUD 上面打开，才看得出挡没挡住、
/// 读数有没有被压掉。所以场子自带一块底：一台固定机位 + 一片地面 + 几个方块当参照物。
///
/// <para><b>与技能试验场同一形状</b>：那边是逻辑壳（<c>SkillStage</c>）+ 场景壳（<c>SkillStageScene</c>），
/// 菜单这边逻辑壳是 <see cref="MenuStack"/> + <see cref="FrontEndSettings"/>（纯 C#，可 <c>dotnet test</c>），
/// 场景壳是本文件。两边都不认识任何原型：「原型隔离」——场子不隶属任何原型，
/// 加原型不改编排，编造排不改编排。</para>
///
/// <para><b>它要能验的六格</b>（每格都有对应的一发调试动词或一次出图）：
/// <list type="number">
/// <item><b>开一屏</b>：<c>ui_open screen=pause</c>，出图看得见面板与遮罩。</item>
/// <item><b>栈叠对不对</b>：暂停屏里进设置，<c>ui_read</c> 念出 <c>stack=Pause &gt; Settings</c>，
/// 退格回到暂停屏而不是全关（栈的 LIFO 那一条）。</item>
/// <item><b>鼠标收没收回来</b>：<c>ui_read</c> 末尾那一格 <c>mouse=</c>；关屏后应等于开屏前那一档。</item>
/// <item><b>设置落没落到引擎上</b>：改音量/窗口模式/分辨率，<c>ui_read</c> 念出四值，
/// 出图看得见窗口尺寸变了。</item>
/// <item><b>取字走没走 Loc</b>：<c>set_locale en</c> 之后出图，那一行字应该不是中文了。</item>
/// <item><b>ESC 收不收得住</b>：连按两下 Esc 不该叠两层暂停屏（<c>MenuStack.Push</c> 那一档）。</item>
/// <item><b>结算屏是不是替身</b>：标题屏第二颗按钮抛局结束事件，开出来的栈里只剩
/// <c>Result</c> 一层（<c>ui_read</c> 看 <c>depth=1</c>），不出图也判得出它没叠在暂停屏上面。</item>
/// </list>
///
/// <para><b>机位是固定的，不提供手调</b>：这个场子要的是「同条件重跑画面一样」，
/// 手调机位会让人不自觉地绕到一个「好看」的角度，而那个角度恰好看不出压没压住。
/// 技能场给三套机位是因为它量的是剪影；这里量的是遮挡，一台正对着就够。</para>
/// </summary>
public partial class FrontEndStageScene : Node3D
{
    private FrontEndLayer _front = null!;
    private Label _hint = null!;
    private DebugBridgeHost? _bridge;

    /// <inheritdoc />
    public override void _Ready()
    {
        // 字体先装：缺省字体没有汉字字形，菜单上屏会是一片空白，
        // 而「上屏空白」的第一反应总是去查字体文件在不在（查不到问题，文件在）。
        FontSetup.Install();

        BuildBackdrop();

        _front = new FrontEndLayer { Name = "FrontEnd" };
        AddChild(_front);

        // 冻不冻时间由场子答：这一版场子里没有逻辑世界可冻，
        // 所以只把「请求冻结」这一格记进读数——真原型接上去才是冻世界。
        _front.PauseRequested += paused => GD.Print($"[FrontEndStage] 前端请求冻结={paused}（场子里没有世界，冻不冻由原型答）");
        _front.QuitRequested += () => GD.Print("[FrontEndStage] 前端请求退出（场子里不退进程，真原型接上去退）");
        _front.SetTitleEntries(
            ("占位条目 · 由原型递进来", () => GD.Print("[FrontEndStage] 占位条目被按了——真原型在这里递自己的「开始」")),
            ("跑一局（抛局结束事件）", () => _front.NotifyRunEnded(new RunOutcome(
                Loc.T("占位胜负 · 由原型递进来"),
                [Loc.T("占位明细行一"), Loc.T("占位明细行二")],
                Tick: 1234,
                Won: true,
                Actions: [("占位按钮 · 再来一局", () => _front.NotifyRunStarted())])))
        );

        // 局开始这一格：场子里没有世界，订阅方把两件事记进日志就够了。
        _front.RunStarted += () => GD.Print("[FrontEndStage] 局开始（场子里没有世界，订阅方各答各的）");

        BuildHint();
        SetupBridge();
    }

    /// <summary>底景：一台固定机位 + 一片地面 + 几个方块参照物。菜单要压在这些东西上面才看得出遮挡。</summary>
    private void BuildBackdrop()
    {
        var world = new Node3D { Name = "World" };
        AddChild(world);

        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(60f, 60f) },
            Position = Vector3.Zero,
        };
        // 材质挂在 Mesh 上（MeshInstance3D 不是 Node 的容器，AddChild 一个材质编译不过）。
        ((PlaneMesh)ground.Mesh).Material = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.19f, 0.22f) };
        world.AddChild(ground);

        // 几个高低不一的方块：菜单压上去时，「被挡住的那一格」能不能认出来靠它们。
        var heights = new[] { 2f, 4f, 6f, 3f, 5f };
        for (int i = 0; i < heights.Length; i++)
        {
            var mesh = new BoxMesh { Size = new Vector3(4f, heights[i], 4f) };
            mesh.Material = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.45f, 0.55f) };
            world.AddChild(new MeshInstance3D
            {
                Name = $"Marker{i}",
                Mesh = mesh,
                Position = new Vector3(-12f + i * 6f, heights[i] / 2f, -8f),
            });
        }

        var camera = new Camera3D
        {
            Name = "Camera3D",
            Position = new Vector3(0f, 26f, 34f),
            Current = true,
        };
        camera.LookAtFromPosition(camera.Position, new Vector3(0f, 2f, -4f), Vector3.Up);
        world.AddChild(camera);

        // 一盏平行光。少了它，底景是纯黑的：菜单压上去看着像浮在虚空里，
        // 「挡住的是不是真东西」这一格就验不成——而那正是这块底存在的理由。
        world.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            Position = new Vector3(10f, 20f, 10f),
            RotationDegrees = new Vector3(-50f, -30f, 0f),
            LightEnergy = 1.1f,
        });
        world.AddChild(new WorldEnvironment
        {
            Name = "Env",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.32f, 0.36f, 0.45f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.55f, 0.65f),
                AmbientLightEnergy = 0.6f,
            },
        });
    }

    private void BuildHint()
    {
        // 操作提示常驻：这一层的验收大半靠「知道现在能按什么」，
        // 提示只在日志里的话，出图复核的人得先开桥才看得见。
        _hint = new Label { Name = "Hint", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_hint);
        _hint.Text = Loc.T("Esc 开/关菜单 · 暂停屏里点「设置」进设置屏，退格回暂停屏 · "
                           + "标题屏第二颗按钮抛局结束事件开结算屏 · "
                           + "调试桥：ui_open / ui_close / ui_read / ui_result / set_locale");
        Arrange();
        GetViewport().SizeChanged += Arrange;
    }

    /// <summary>
    /// 提示行放<b>顶端</b>，不放底端。
    ///
    /// <para>放底端的第一版出过一场排版事故：菜单面板是居中的，设置屏一多就往下长到
    /// 底端那一带，提示行正好从「分辨率」那一行底下横穿过去（出图实测，2026-10-06）。
    /// 顶端那一带面板永远到不了，两边各让一步就再也不会撞。</para>
    /// </summary>
    private void Arrange()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _hint.Position = new Vector2(12f, 10f);
        _hint.Size = new Vector2(size.X - 24f, 24f);
    }

    private void SetupBridge()
    {
        _bridge = DebugConsole.Attach(this, new Globals { Front = _front, Stage = this },
            // 出图复核那一格：菜单压没压住、字上没上屏，只有出图看得见。
            ("screenshot", a => ViewportCapture.SavePng(this, a.Str("path") ?? "user://frontend-stage.png")));

        // 前端三发：开一屏、关一屏、念出栈与设置。共用层那份词表（FrontEndVerbs）。
        foreach (var (verb, handler) in FrontEndVerbs.Vocabulary(_front))
            _bridge?.Register(verb, handler);

        // i18n 两发：切语言、查当前语言与字体链。共用层那份词表（LocaleVerbs）。
        foreach (var (verb, handler) in LocaleVerbs.Vocabulary())
            _bridge?.Register(verb, handler);

        GD.Print("[FrontEndStage] UI 试验场就绪：Esc 开菜单；调试桥 ui_open / ui_close / ui_read / ui_result / set_locale / screenshot");
    }

    /// <summary>eval 脚本的强类型 globals：<c>Main.Front.Open(...)</c> 走全量 C#，不用 dynamic。</summary>
    public sealed class Globals
    {
        public FrontEndLayer Front { get; init; } = null!;

        public FrontEndStageScene Stage { get; init; } = null!;
    }
}
