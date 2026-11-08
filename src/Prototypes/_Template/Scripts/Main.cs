#nullable enable
using System;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.FrontEnd;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.InputLayer;
using DemoFactory.GugaFramework.Scheduling;
using Godot;

namespace DemoFactory.Prototypes.Template;

/// <summary>
/// 原型主场景入口脚本 — 空壳占位。
///
/// 设计思路：
/// - 每个原型拥有独立的 Main.tscn，可直接 F5 运行验证
/// - 原型之间严禁相互引用，只能通过 EventBus 全局总线通信
/// - 场景节点只是被逻辑流牵引的只读表现节点，在 _Ready() 中订阅逻辑流更新表现
/// - 订阅一律用 `+=`，并在 `_ExitTree()` 里成对 `-=`（同一处代码里看得见接上与离场）
///
/// 架构位置：三层响应式架构 — 无状态表现层 (UI/3D)
///
/// 逻辑世界那一半（三层怎么持有、每步次序、动词并集）见
/// <c>Scripts/Logic/LayerWiring.cs</c> —— 三层接线样板，这一份只留表现层那一半。
///
/// <para><b>模板已自带的三段接线，别让它重新发明一遍</b>：
/// 输入层（<see cref="SetupInputMap"/> 与 <see cref="PollTimeKnobs"/>）、
/// 前端层（<see cref="SetupFrontEnd"/>）、时间旋钮（<see cref="_Process"/>）。
/// 三段都是「不接就缺一格、缺了症状还很好伪装」的接线：
/// 没有输入层则合成事件进不来，没有前端层则菜单无处可开，
/// 没有 <c>_Process</c> 则 <see cref="SetSpeed"/> 拧了不动。</para>
///
/// 视觉管线：
/// - 正交 3D 节点 (World) 处理空间逻辑
/// - 顶层 CanvasLayer (UILayer) 处理所有 UI
/// - 视角统一走 GugaFramework/Camera/TopDownRig（本项目所有原型都是俯视角）
/// - 相机 eval 口（CameraReadout/CameraSetPose）模板自带 — 新原型天生可被 AI 摆机位
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        // 字体先装：缺省字体没有汉字字形，下个原型接 UI 中文上屏会是整行空白
        // （实测记录见 SkillStageScene 类注）。模板自带这一行，别让下个原型自己发明一遍。
        FontSetup.Install();

        SetupCameraRig();
        SetupInputMap();
        SetupDebugBridge();
        SetupFrontEnd();
        GD.Print("[Template] Prototype main scene loaded.");
    }

    /// <summary>
    /// 俯视角相机架接入（滚轮缩放 · 中键拖拽绕转/改俯仰 · WASD 平移 · Q/E 或 ←→ 转 · ↑↓ 俯仰
    /// · Home 归位 · Tab 请求锁定）。
    /// 相机架只管相机：中键/Tab 要锁定谁属于玩法语义，原型自己判完再调 rig.FollowTarget(模型)。
    /// 焦点/距离/边界按本原型的世界尺寸改；其余是手感旋钮，已 [Export]，别在原型里改代码。
    /// </summary>
    private void SetupCameraRig()
    {
        var world = GetNode<Node3D>("World");
        var rig = new TopDownRig
        {
            BoundsMinXZ = new Vector2(-60f, -60f),
            BoundsMaxXZ = new Vector2(60f, 60f),
        };
        world.AddChild(rig);

        var camera = new Camera3D { Name = "Camera3D" };
        world.AddChild(camera);
        rig.AttachCamera(camera);

        rig.SetPose(Vector3.Zero, 100f, 0f, 50f);
        rig.SaveHome();
        _rig = rig;
    }

    private TopDownRig _rig = null!;

    // === 前端层（菜单栈、标题屏、暂停屏、设置屏、结算屏、退出）===

    /// <summary>前端层节点。菜单浮在游戏之上，这一层号由它自己设。</summary>
    private FrontEndLayer _front = null!;

    /// <summary>
    /// 这一局是否已经结束 —— <b>由前端层的局结束事件写这一格</b>，不查世界的判定结果。
    ///
    /// <para><b>为什么是事件而不是每帧轮询</b>：判定发生在逻辑层，表现层每帧去问一次
    /// 「打完了没有」是这一处倒挂的唯一来源 —— 那一问让表现层知道了它不该知道的事。
    /// 三个原型原来各有一处轮询，收编时都改成事件驱动。</para>
    ///
    /// <para>推进循环读这一格判「还推不推」，冻时间轴由 <c>RunEnded</c> 订阅里那一发管。
    /// 两者是同一件事的两道闸，真值只有世界那一份 <c>TimeFrozen</c>。</para>
    /// </summary>
    private bool _runEnded;

    /// <summary>
    /// 前端层接线 —— 模板自带，别让下个原型自己造菜单场子（「UI 试验场随前端层」）。
    ///
    /// <para><b>五行接法</b>：建层 → 答暂停请求 → 答退出请求 → 递标题屏条目 → 注册共享词表。
    /// 前端层不认识任何原型（「原型隔离」），所以标题屏那一条文字与动作由本方法递进来。</para>
    ///
    /// <para><b>暂停不归前端层管</b>（「输入模式单源」）：它只抛 <c>PauseRequested</c>，
    /// 冻不冻由原型答。模板还没有逻辑世界，所以这一格先记一笔接线点：
    /// 接上世界后把 <c>_world is null</c> 那一支换成 <c>_world.SetPaused(paused)</c>
    /// （走基类那个单一入口，不直接调 <c>Pause</c>/<c>Resume</c> 两个口）。</para>
    ///
    /// <para><b>局开始与局结束都归这一层抛</b>：原型在自己那一局装配好之后调
    /// <c>NotifyRunStarted</c>，判定落地那一刻调 <c>NotifyRunEnded</c>。
    /// 结算屏由前端层画，原型只递胜负与那几行明细。</para>
    ///
    /// <para><b>现成写法</b>：<c>src/Prototypes/BeastArena/Scripts/Main.cs</c> 的
    /// <c>SetupFrontEnd</c>，与另两个原型同一份形状。</para>
    /// </summary>
    private void SetupFrontEnd()
    {
        _front = new FrontEndLayer { Name = "FrontEnd" };
        AddChild(_front);

        // 暂停请求：模板未接世界，先只记一笔。接上后改成答世界那一侧。
        _front.PauseRequested += paused => Log($"[Template] PauseRequested paused={paused}（模板未接逻辑世界）");

        // 退出请求：前端层只抛请求，退不退由这一侧答（「表现层只读」）。
        _front.QuitRequested += () => GetTree().Quit();

        // 标题屏条目：文字与动作都由原型递，框架不知道「重开一局」在这个原型跳去哪。
        _front.SetTitleEntries((Loc.T("重开一局"), () => Log("[Template] 标题屏：重开一局")));

        // 局开始把「这一局已结束」那一格清回来；换局之后那一格要复位。
        _front.RunStarted += () => _runEnded = false;

        // 局结束 = 冻住时间轴（三个原型统一口径）。接上世界后补一句 _world?.FreezeTime()。
        _front.RunEnded += _ => _runEnded = true;

        // 菜单的调试动词（ui_open / ui_close / ui_read / ui_result）：公共层的共享词表，
        // 与输入合成、i18n 两组同一档 —— 由原型侧展开注册，不占标准七动词的名字。
        foreach (var (verb, handler) in FrontEndVerbs.Vocabulary(_front))
            _bridge?.Register(verb, handler);
    }

    /// <summary>
    /// 合成输入的入录回调模板（2026-10-05 用户裁决）。
    ///
    /// <para><b>模板这一份什么都不做，是有意的</b>：模板还没有逻辑世界，
    /// 没有 Journal 可入。写一个空实现而不是留个 TODO 坑，是为了<b>让接新原型的人看见这一格存在</b> ——
    /// 合成输入要进 Journal，所以接线时这一句必须递进去；不递，功能照跑，只是不进账。</para>
    ///
    /// <para><b>接上逻辑世界后照这三条改</b>（现成写法见无限领主
    /// <c>src/Prototypes/InfinityLord/Scripts/Main.cs</c> 的 <c>RecordSyntheticInput</c>）：
    /// ① 词表摊进 <c>Scripts/Logic/ReplaySetup.cs</c> 的 <c>VerbNames</c>（名字用
    /// <c>GugaFramework/Replay/SyntheticVerbs.Names</c>，那份在公共层，形状对各原型一样）；
    /// ② <c>Inject</c> 里把这两条接成跳过（<c>SyntheticVerbs.Skip</c>，它就是那份唯一实现）；
    /// ③ 本方法把 <c>kind</c> 翻成动词名后走自己的写入口入录。</para>
    /// </summary>
    /// <remarks>
    /// 为什么重放端是「跳过」而不是「复现」：重放端是纯 C#，没有引擎也没有光标，
    /// 复现不了「引擎收到了一次点击」。硬造一个就是拿假的世界骗自己 ——
    /// 真正的状态变更由玩家路径随后落下的逻辑写承担，那笔才是断言落点（「断言比快照」）。
    /// </remarks>
    protected virtual void RecordSyntheticInput(InputInjector.InputKind kind, string args)
    {
        // 模板未接逻辑世界：无处可入录，接上后覆写本方法。
    }

    // === 相机 eval 直达口 — 模板自带，AI 可远程摆机位/读机位（视觉回归的基准面） ===
    // 用法：CameraReadout() 读机位；CameraSetPose(0, 0, 100, 0, 50) 定点（不过阻尼）。
    // 同机位截图 + 读数断言 = 视觉回归；断言收敛态（读数稳定后），不断言阻尼中途帧。

    /// <summary>视角读数直达 — eval 直达：CameraReadout() 返回机位（焦点/距离/偏航/俯角/跟随目标）。</summary>
    public string CameraReadout() => _rig.DebugReadout();

    /// <summary>
    /// 机位定点直达 — eval 直达：CameraSetPose(focusX, focusZ, distance, yaw, pitch)
    /// 立即把相机摆到指定机位（不过阻尼），返回摆位后的读数。
    /// 注入按键只能证明"会响应"，截图要的是"停在某个机位"，两条通道各证一半。
    /// </summary>
    public string CameraSetPose(float focusX, float focusZ, float distance, float yawDegrees, float pitchDegrees)
    {
        _rig.SetPose(new Vector3(focusX, 0f, focusZ), distance, yawDegrees, pitchDegrees);
        return _rig.DebugReadout();
    }

    // === 输入层（「输入走 InputMap」— 模板自带，2026-10-12 随公共层输入层一起收进来）===
    //
    // 这一层分两半：公共层定「概念叫什么、按哪种读法问」，原型定「哪个键绑哪个概念」。
    // 键位是内容决定，所以绑键在原型侧；概念名与读法一个字都不许在这里另抄
    // （现成三处：src/Prototypes/*/Scripts/Main.cs 的 SetupInputMap）。
    //
    // 读法三档：刚按下（旋转钮、暂停、单步、变速）/ 按住（平移、绕转、缩放）/ 模拟量。
    // 读法是概念自己带的必填项 —— 漏给读法编译不过，读法选错的后果是「按住重复触发」。

    /// <summary>
    /// 读法分派 —— 概念名与读法都来自公共层，引擎那三个读数口在本件递进去。
    /// 原型不再直接调引擎的按下/按住读法去问 action，那一层现在只剩这三件。
    /// </summary>
    private readonly InputRead _read = new(
        name => Input.IsActionJustPressed(name),
        name => Input.IsActionPressed(name),
        name => Input.GetActionStrength(name));

    /// <summary>
    /// InputMap action 登记 —— 幂等，重复调不叠事件。
    ///
    /// <para><b>为什么必须走 action 而不是裸键</b>（「输入走 InputMap」）：合成事件进不了
    /// 裸键状态表，调试通道就注不进来，等于该功能不可实测。玩家与 agent 走同一发键，
    /// 才是「注入的路径」与「玩家的路径」不漂的前提。</para>
    ///
    /// <para>键位是内容决定：三个原型绑的键各不相同（本模板 Space / 句号 / 方括号一对）。
    /// 概念名与读法来自公共层，本方法只递「哪个键绑哪个概念」。</para>
    ///
    /// <para>相机那十二条由 <c>TopDownRig</c> 自己登记，本方法不碰
    /// （<c>InputConcept.CameraConcepts</c>）；这里只登记时间旋钮那四条。</para>
    /// </summary>
    private void SetupInputMap()
    {
        EnsureAction(InputConcept.TogglePause.Name, Key.Space);
        EnsureAction(InputConcept.StepOnce.Name, Key.Period);
        EnsureAction(InputConcept.SpeedUp.Name, Key.Bracketright);
        EnsureAction(InputConcept.SpeedDown.Name, Key.Bracketleft);
    }

    /// <summary>幂等登记一条 InputMap action（已存在且已绑这个键就直接返回）。</summary>
    /// <param name="action">概念名（公共层那份）。</param>
    /// <param name="keycode">绑哪个物理键（内容决定）。</param>
    private static void EnsureAction(string action, Key keycode)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventKey { PhysicalKeycode = keycode };
        if (InputMap.ActionHasEvent(action, ev)) return;
        InputMap.ActionAddEvent(action, ev);
    }

    /// <summary>
    /// 时间旋钮四条 —— <b>每帧读一次「刚按下」</b>，读法由概念自带，本件不判。
    ///
    /// <para><b>为什么是每帧读而不是逐事件读</b>（三个原型踩出来的）：原来在
    /// <c>_UnhandledInput</c> 里用 <c>@event.IsActionPressed(...)</c> 读，那条路问的是
    /// 「这一个事件算不算按下」，于是同一帧来几个按下形状的事件就触发几次。
    /// 改成逐帧读引擎的刚按下读数口之后，引擎自己把边沿闩住 —— 一次物理按下只成立一次。</para>
    ///
    /// <para><b>暂停改走这一发</b>（2026-10-12）：不再绑 F8 或裸键，统一走
    /// <c>input_toggle_pause</c>，三个原型一致。</para>
    ///
    /// <para>模板还没有逻辑世界，所以「暂停」与「单步」只回读数。接上世界后：
    /// 暂停走 <c>_world.SetPaused(!_world.IsPaused)</c>（基类那个单一入口），
    /// 单步调桥上那个 <c>StepDebug</c>。</para>
    /// </summary>
    private void PollTimeKnobs()
    {
        if (_read.Read(InputConcept.TogglePause))
            Log("[Template] input_toggle_pause（模板未接逻辑世界，接上后改走 _world.SetPaused）");

        // 调桥上那个方法，不在这里再抄一份 —— 抄一份就是「同一件事两处代码」。
        if (_read.Read(InputConcept.StepOnce)) StepDebug();

        if (_read.Read(InputConcept.SpeedUp)) SetSpeed(_speed.Multiplier * 2.0);
        if (_read.Read(InputConcept.SpeedDown)) SetSpeed(_speed.Multiplier / 2.0);
    }

    // === 时间旋钮：速度旋钮 + 推进循环（「时钟解耦」— 模板自带）===
    // 原型接入样板：Main 持一个 SpeedControl（tickRate = 原型的逻辑频率），
    // _Process 里「累积 TicksPerFrame(delta) → 攒满 1 步（1 步 = 0.1 秒）就 Advance(1)」。
    // 累积器的单位是<b>步</b>（TicksPerFrame 返回步数），所以排空的门槛写 1f —— 写 0.1f 是每帧多推 10 倍。
    // 参考 src/Prototypes/InfinityLord/Scripts/Main.cs（_speed / _tickAccumulator 段）。
    // 变速不改时间轴：哨兵测试证明推进分块方式不影响世界演化（DeterminismSentinelTests）。

    /// <summary>调试速度旋钮 — 本原型 10 步/秒（按原型实际频率改 tickRate）。</summary>
    private readonly SpeedControl _speed = new(tickRatePerSecond: 10.0);

    /// <summary>
    /// 本帧应推进的步数（表现层持有的浮点累积器 — 攒满一步才 Advance）。
    /// 用 <c>double</c>：<c>TicksPerFrame</c> 回的就是 <c>double</c>，「double 与不变文化」那条算术口径。
    /// </summary>
    private double _tickAccumulator;

    /// <summary>
    /// 推进循环 — 倍率只乘在逻辑流速上，表现层攒够一步才推一步（「时钟解耦」「时间基准」）。
    ///
    /// <para><b>模板这一份把步数打出来，不推任何世界</b>：模板还没有逻辑世界。
    /// 但保留整个累积器形状是有意的 —— <see cref="SetSpeed"/> 拧了倍率要在这里才生效，
    /// 没有这一段，那两个 eval 直达口拧了不动，而症状是「旋钮没报错也没效果」。
    /// 接上世界后把 <c>Log</c> 那一行换成 <c>_world.Advance(1)</c>，
    /// 并按需补位姿同步（内容按步更新、位姿每渲染帧刷，出处见三个原型的 <c>SyncPose</c>）。</para>
    ///
    /// <para><b>用 while 不用 if</b>：一帧的步数可能大于 1（高倍率或低帧率），
    /// 用 if 会把多出来的步数留在累积器里越积越多，于是倍率越高实际越慢。</para>
    /// </summary>
    public override void _Process(double delta)
    {
        PollTimeKnobs();

        _tickAccumulator += _speed.TicksPerFrame(delta);
        while (_tickAccumulator >= 1.0 && !_runEnded)
        {
            _tickAccumulator -= 1.0;
            // 模板无逻辑世界：接上后改为 _world.Advance(1)。
            // 那一侧的推进次序（先落地形、再推逻辑步、再推建造）归 WorldAssembly，见 Scripts/Logic/LayerWiring.cs。
            Log($"[Template] tick={_tickAccumulator + 1.0:0}（模板无逻辑世界；这一行接上后换成 _world.Advance(1)）");
        }

        // 局结束就不推了：那一格由前端层的 RunEnded 事件写，不查世界的判定结果。
    }

    /// <summary>
    /// 速度旋钮直达 — eval 直达：SetSpeed(20) 快进赶路 / SetSpeed(0.25) 慢放细看 / SetSpeed(1) 实时。
    /// 人在调试时的手感：无关紧要处加速、关键处放慢甚至一步一步走（StepDebug）。
    /// </summary>
    public string SetSpeed(double multiplier)
    {
        _speed.SetMultiplier(multiplier);
        return $"speed={_speed.Multiplier}x";
    }

    /// <summary>单步直达 — eval/bridge 直达：走一步自动停（最细调试档），返回当前步号。</summary>
    public string StepDebug()
    {
        // 模板无逻辑世界：接上后改为 _world.Advance(1)，返回 _world.Tick
        // 逻辑那一侧的推进次序（先 Pump 后推逻辑步）见 Scripts/Logic/LayerWiring.cs
        return "template-has-no-logic-world";
    }

    // === 调试桥（三个观测口的运行时通道 - 模板自带，接新原型只往下面那张表里加自己的动词） ===
    // 用法（项目根执行；起局走 game start —— 它起前必探端口，有人应答就拒绝起第二局，
    // 起完回读身份 pid/端口/场景/起局时刻，于是「我连的是不是刚起的那一局」一眼可查）：
    //   dotnet run --project src/Tools -- game start res://src/Prototypes/<原型>/Scenes/Main.tscn --port 7777
    //   dotnet run --project src/Tools -- debug ping
    //   dotnet run --project src/Tools -- debug eval --code "Main.DebugSnapshot()"
    //   dotnet run --project src/Tools -- game stop --port 7777     # 收尾：按端口停，复认无监听
    // 端口没配置时宿主根本不监听 ⇒ 出货版本零开销、零暴露面（默认关这条在 DebugBridgeHost 里）；
    // 配了端口却撞号（端口上已经有别人）宿主会当场退出这一局，不留一局没有桥的进程。
    // 公共那七个动词（ping/verbs/targets/call/eval/evaldiag/logs）由 DebugConsole 注册，别在这里重抄；
    // 原型要加的是**自己的写入口**（摆子 / 开局 / 招募 / 交易……），一条纪律：动词只转发既有 eval 直达方法，
    // 不造第二套实现 —— 否则「注入的路径」与「玩家的路径」会漂成两回事。

    // === 接技能层与 cue 派发台 —— 可粘贴样板（注释形态，模板不链技能表所以不编译）===
    // 逻辑侧那一半（表、接线、每步推进、宿主向层供的三个口）在 `Scripts/Logic/LayerWiring.cs`；
    // 这一节只留表现侧那一半：订事件出口、按形状读、翻成动作。
    // 名单与形状都在 `src/GugaFramework/Abilities/AbilityEvents.cs`，接之前先读那份：一类事件一个形状，
    // 类型名就是事件名（没有 Kind 枚举这张表要背），形状里也没有可选位。
    // 三格要点：
    //   1) 接层时 `AbilityLayerConfig` 要递 `EventCapacity`（公共层不替宿主选这一格，缺了当场抛）。
    //   2) 表现层只按形状读，例如：
    //      switch (ev.Payload)
    //      {
    //          case DamageDealt d:      feed.PushOneShot(ev.Tick, ev.Subject, HitTrack, d.Final); break;
    //          case AbilityRejected r:  feed.PushOneShot(ev.Tick, ev.Subject, RejectedTrack, r.Reason); break;
    //          case AbilityRejectedAndQueued rq: /* 同上，两条形状都要念 */ break;
    //          case SettlementRegistered p:  feed.PushDescriptive(ev.Tick, ev.Subject, FlightTrack,
    //                                            p.LandingTick); break;
    //          case SettlementRegisteredWithMove pm: /* 带位移那一档 */ break;
    //      }
    //      现例见 `src/Prototypes/BattleArray/Scripts/Logic/SkillCueSpec.cs`（档位与措辞是玩法语义，留在原型侧）。
    //   3) 读数只走层那一份口：`_world.Events.TailText(n)`（头一行就是 `dropped= total= kept=`）、
    //      `Count<T>()`、`Where<T>()`；桥侧那条 `ability_log` 与标准 `logs` 同形，回三键
    //      `dropped_count` / `total` / `lines`。自己再拼一份尾窗文本 = 第二份实现。
    //   4) 宿主想留一笔自己的读数（例如形状命中打到了谁），走层的补记口 `RecordShape(...)`，
    //      名字与形状仍归公共层；绕过补记口直接往层的事件流里塞行那条路，在 BattleArray 已经拆掉了
    //      （拆的理由与适用范围记在 docs/prototypes/battle-array/技能层验证记录.md 那一轮）。
    // 做法与取舍的出处：`docs/adr/0001-ability-event-outlet-is-typed-shapes.md`、
    // `docs/prototypes/battle-array/技能层验证记录.md`「基建 — 技能层出口换成带字段的事件形状」。

    // === 接重放装配 — 可粘贴样板（注释形态，模板没有逻辑世界所以不编译）===
    // 公共那半已经在 `src/GugaFramework/Replay/ReplayAssembly.cs` 一份：词表派发、参数编码与规范化、
    // 写路径守卫、Journal 追加、检查点守卫、三钩子工厂、脚本导出，全在那边。
    // **新原型只递三样**，其余一件都不要自己写（抄一份就是下一次漂移的起点）：
    //   1) 动词名单 `string[]`；
    //   2) 注入委托 `bool (cmd, args, out canonicalArgs, out reason)` —— 每个动词那一次调用；
    //   3) 推进委托 `Action<long>` —— 推进到目标步的循环。
    // 种子也从这里递，不进 `IReplayWorld`（只有部分原型公开 `Seed`，把它塞进契约等于按常态定尺寸）。
    // 形状：
    //     public static ReplayAssembly Recorder(MyWorld world) => new(
    //         world, world.Seed, VerbNames, InjectorFor(world), target => AdvanceTo(world, target));
    //     private static ReplayInject InjectorFor(MyWorld world)
    //         => (string cmd, string args, out string canonical, out string reason)
    //             => Inject(world, cmd, args, out canonical, out reason);
    // 词表侧只要实现 `IReplayWorld` 的四个成员（`Tick` / `IsPaused` / `Advance` / `ReplaySnapshot`）——
    // 这四个是三个原型结构上真共有的，种子与推进到目标步是各自的真差异，所以留在原型侧。
    // 两条要抄走的纪律：
    //   - 写路径唯一入口是 `assembly.Inject(cmd, args)`。不要「打一次词表再单独记一笔」——
    //     那样同一发会打两遍（BattleArray 原先的 `DuelJournal` 就是这个形状）。
    //   - 有别名集（枚举名 / 界面短名 / 旧名）时 `canonicalArgs` 要把别名折成唯一那个 token，
    //     Journal 记的是它；没有别名集时原样返回并把这句话写进注释，别让后来人以为漏了。
    // 三份现役样板（收口后的样子，照最像自己那个抄）：
    //     无限领主  `src/Prototypes/InfinityLord/Scripts/Logic/ReplaySetup.cs`
    //     排兵布阵  `src/Prototypes/BattleArray/Scripts/Logic/DuelRiftSetup.cs`
    //     巨兽竞技场 `src/Prototypes/BeastArena/Scripts/Logic/ArenaSetup.cs`
    // 契约测试（形状本身的口径，改公共那半先跑它）：`src/GugaFramework/Tests/ReplayAssemblyContractTests.cs`。
    // 收编经过与账：`docs/GugaFramework/确定性与重放.md`；约束见 AGENTS.md 「重放装配单源」。

    /// <summary>eval 脚本的 globals（public 嵌套类：Roslyn 脚本编译要可访问性，字段类型要具体才不用 dynamic）。</summary>
    public sealed class TemplateGlobals
    {
        /// <summary>主节点。</summary>
        public Main Main { get; set; } = null!;
    }

    private DebugBridgeHost? _bridge;

    private void SetupDebugBridge()
    {
        // 返回值留着：重开一局要换 globals 里的世界引用时，走的通路是
        // _bridge.SetEvaluator(DebugBridgeEval.CreateEvaluator(newGlobals))（模板没有世界，也别忘了有这条路）。
        // 三个动词的读参数只有一个口：BridgeArgs 的扩展方法 —— multiplier 缺了当场抛，不兜档
        // （兜一档就等于「拧了没生效」和「没打算拧」读起来同一个样）。
        _bridge = DebugConsole.Attach(this, new TemplateGlobals { Main = this },
            ("speed", a => SetSpeed(a.Num("multiplier", required: true))),
            ("step", _ => StepDebug()),
            ("snapshot_debug", _ => DebugSnapshot()),
            ("screenshot", a => CaptureScreenshot(a.Str("path", required: true) ?? "")));

        // --- 输入合成（「行为可注入」那一半） ---
        // 2026-10-05 godot-ai MCP 移除时补上：MCP 的 game_manage(op:"input_*") 一并删掉，
        // 替代按「先接后删」先落地。模板自带，下个原型生下来就有这四条。
        // Attach 之后单独注册：它的词表参数是 `params`，`..` 展开只在集合初始化里合法。
        // 机制、四条实测读数与「合成滞后一帧」那个坑在 InputInjector 的类注里。
        foreach (var (verb, handler) in InputInjector.Vocabulary(this, RecordSyntheticInput))
            _bridge?.Register(verb, handler);

        // --- i18n 两动词（模板自带，下个原型生下来就有）---
        // set_locale 切语言、locale_probe 读当前语言/字体链/某键的取字结果。
        // 机制与接法见 GugaFramework/I18n/LocaleVerbs 类注。
        foreach (var (verb, handler) in DemoFactory.GugaFramework.I18n.LocaleVerbs.Vocabulary())
            _bridge?.Register(verb, handler);

        // --- 3D 拾取读出口（上面那组 input_hover 的读数） ---
        // 光标挪过去之后「拾到了谁」只有这里读得到。悬停/高亮那份文本给人看，这行给 agent 断言 ——
        // 否则 3D 拾取只能靠出图人眼看，等于零自动化覆盖（机制见 HoverPickDebug 的注释）。
        // 本模板还没有可拾的目标，所以读数是「待接」而不是假装拾到了谁：读一个不存在的值
        // 比读不出值更坏。接了目标之后把方法体换成自己那份 Pick 口径即可。
        _bridge?.Register("hover_pick", _ => HoverPickDebug());
        Log($"[Template] 调试桥已接线（{(_bridge is null ? "宿主没建起来" : "词表看 debug verbs")}）");
    }

    /// <summary>
    /// 3D 拾取读出口模板 — 返回光标位与拾到的目标 id，供 <c>debug hover_pick</c> 断言。
    ///
    /// <para><b>为什么动词自带而不进 <c>InputInjector.Vocabulary</c></b>：那四条是零游戏知识的公共件，
    /// 而「拾取谁」是玩法语义 —— 各原型的可拾目标与容差都不同，上收就等于在公共层认得
    /// 「单位 / 建筑」这两个概念，正是「原型隔离」要挡的事。⇒ 公共那四条不变，
    /// 读出口留原型侧一行。</para>
    ///
    /// <para><b>只在窗口模式下有值</b>：合成 <c>InputEventMouseMotion</c> 改不了视口鼠标位置
    /// （无头恒 (0,0)），只有 <c>Input.WarpMouse</c> 能在窗口模式改它 ⇒ 悬停拾取整条链在无头下
    /// 恒为空，这不是缺陷（机制见 <c>GugaFramework/DebugBridge/InputInjector.cs</c> 类注事实 ③④）。</para>
    ///
    /// <para>接上目标后：把 <c>target: "none"</c> 换成自己的拾取结果
    /// （无限领主那份现成写法见 <c>src/Prototypes/InfinityLord/Scripts/Main.cs</c> 的
    /// <c>HoverPickDebug</c>，它复用 <c>PickUnit</c>/<c>PickBuilding</c> 那一份口径，
    /// 与悬停标签读同一个值）。</para>
    /// </summary>
    public string HoverPickDebug()
    {
        var pos = GetViewport().GetMousePosition();
        return $"pick=none（模板未接拾取目标） mouse=({pos.X:0},{pos.Y:0})";
    }

    /// <summary>
    /// 状态快照 — 模板占位；接上逻辑世界后改为转发 <c>_world.DebugSnapshot()</c>（显示即真相那份实现）。
    /// 那一份里每层各出自己那一段读数，世界只搬 token、不重算（折行见 <c>LayerWiring.FoldSegment</c>）。
    /// </summary>
    public string DebugSnapshot() => "template-has-no-logic-world";

    // === 出图复核（「落地配出图」 — 全原型硬要求，模板自带，别让下个原型自己发明一遍）===
    // 2026-09-30 收编自巨兽竞技场：那个原型第一版自己写了 CaptureScreenshot，
    // 随后上收成公共层 `ViewportCapture`。留在原型侧的下场就是每个原型抄一份、
    // 抄的那份还少了健壮性处理（目录不存在、路径漏扩展名）。
    // 用法：debug screenshot --path <绝对路径>；目录会自动建、漏写扩展名自动补 .png。
    // **同机位截图 + 读数断言 = 视觉回归**（机位用上面的 CameraSetPose 定住）。
    // 读数说它在那儿与画面上看得见是两回事：HUD 锚点算偏、CanvasLayer 不是 Control、
    // 模型站没站在那儿 —— 这几类只有出图看得见。

    /// <summary>视口截图 — eval/bridge 直达：把当前视口存成 PNG，返回人读的判定串。</summary>
    public string CaptureScreenshot(string path) => ViewportCapture.SavePng(this, path);

    // === 接 ModelAnimator 必做的一根接线（2026-09-30 巨兽竞技场踩出来的）===
    // `ModelAnimator.GameSpeed` 是**「设了才生效」**的可选属性：漏设不报错、不告警，
    // 只是快进时位移按 N 倍走、动作仍按 1 倍演 —— 画面上表现为「滑着走」，不是「明显坏掉」。
    // 全仓原先只有排兵布阵那一侧写了这一行，也就是说不写它的原型快进时动画都是错的。
    // 接上动画播放器时，在推步的那一处把倍率一并递进去（本模板的形状见 `SetSpeed` 与 `_Process`）：
    //     _anim.GameSpeed = (float)Math.Max(1.0, _speed.Multiplier);
    // 口径与排兵布阵同一份：慢放不缩动作（照常长度演），只有快进才让动作跟上时间轴。
    // 想确认动画到底演没演，读 `_anim.DebugReadout()` —— 它报「想要哪条 / 实际在播哪条 /
    // 那条在不在 clip 表里」。这三栏对不上时画面上分不出来：静站姿可能恰好对上某一帧。

    /// <summary>日志双写：GD.Print 给人看，RollingLog 给 <c>debug logs</c> 读（有界缓冲，读它必看 dropped_count）。</summary>
    protected static void Log(string message)
    {
        GD.Print(message);
        RollingLog.Write(message);
    }
}
