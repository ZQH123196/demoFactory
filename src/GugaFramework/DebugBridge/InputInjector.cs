#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;

namespace DemoFactory.GugaFramework.DebugBridge;

/// <summary>
/// 输入合成（GugaFramework 通用件）—— 进程内造 <see cref="InputEvent"/> 送进引擎，让真机输入链可被自动化。
///
/// <para><b>为什么上收</b>（2026-10-05，godot-ai MCP 移除）：MCP 的 <c>game_manage(op: "input_*")</c>
/// 是本仓唯一一条能验「玩家按键到逻辑层」的入口。插件与它一起被删，替代必须先接上——按「先接后删」，
/// 本件在删除之前落地并实测通过。</para>
///
/// <para><b>接之前测过的四条引擎事实</b>（2026-10-05 headless 与窗口模式实测，命令原文见
/// 《验证操作手册》「输入合成」一节）：
/// <b>① 合成滞后一帧</b>：<see cref="Input.ParseInputEvent"/> 之后在同一次调用内读
/// <see cref="Input.IsActionJustPressed"/>，读到的是注入<b>之前</b>的状态（12 发全 false；把合成
/// 与读放进同一帧回调里照样 false，所以这不是求值延迟造成的）。⇒ 注入动词不返回读数，读数走下一次
/// <c>input_read</c>——引擎的一帧滞后与协议的请求-应答刚好对齐。
/// <b>② 原始事件能到达 <c>Node._UnhandledInput</c></b>：合成 <see cref="InputEventKey"/>（Escape）
/// 与 <see cref="InputEventMouseButton"/>（右键）都让营建模式从有变成无；对照发
/// <c>Key.F13</c> 状态不变，排除了「时间流逝自己清掉」。
/// <b>③ 合成 <see cref="InputEventMouseMotion"/> 改不了</b> <c>GetViewport().GetMousePosition()</c>：
/// 无头恒 (0,0)、窗口恒真实光标位，两种模式都不动。
/// <b>④ <see cref="Input.WarpMouse"/> 能改它</b>：窗口模式实测三个目标都跟上且隔帧不弹回；但无头
/// 模式下读数恒零，且它动的是<b>真光标</b>——窗口挪到屏幕外时会把光标带出可视区。
/// ⇒ 3D 拾取悬停只能走本件，且只在有渲染设备的模式成立；那一格在无头下无意义，
/// <c>input_read</c> 显式把它标出来而不是让它读出一个假的零。
/// <b>⑤ 合成 action 的按下态消费后即清</b>：送进去的按下态只在被消费的那一帧存在，消费完即清。
/// 桥的请求要等下一次主循环才派发，所以 <see cref="Input.IsActionPressed"/> 在桥上读到的永远是 0。
/// 实测对照（2026-10-05 真机）：注入 <c>place_market</c> 之后世界进了营建模式
/// （<c>eval</c> 读 <c>_placingKind</c> 得 <c>Market</c>），而同一时刻 <c>input_read</c> 报
/// <c>place_market=0</c>。⇒ <c>input_read</c> 不报 action 状态，改报可注入的 action 名单；
/// <b>注入有没有生效要问世界自己</b>（各原型的快照动词或 <c>eval</c> 读数），不靠桥回读输入层。</para>
///
/// <para><b>press 与 release 必须成对</b>：只发 press 不发 release，第二次同 action 的 press
/// 不产生跳变（状态还压在按下态上），营建模式进不去。实测隔 800 毫秒不够、隔 2 秒才稳。⇒ 这两个是
/// 两个动作，时序由调用方排，中间插 <c>pause</c> / <c>step</c>（「时间可控」）。</para>
///
/// <para><b>主线程纪律</b>：<see cref="Input.ParseInputEvent"/> 与 <see cref="Input.WarpMouse"/>
/// 都是 Godot API，只能在主线程摸。本件的处理器一律经 <c>DebugBridgeHost</c> 的
/// <c>CallDeferred</c> 泵回主线程执行（那条泵见 <see cref="DebugBridgeHost"/> 的类注）——「C# 文件基线」。</para>
///
/// <para><b>不进 Journal</b>：本件走的是玩家路径，不是命令路径。「写路径全录」录的是
/// <c>inject</c> 动词那份写入口，「写路径进 Journal」也明确「命令发出即录，不在 UI 输入录」。
/// ⇒ 用本件验出来的东西只能回答「接线通不通、画面反不反映」，进不了重放断言那三层；逻辑命题
/// 仍然走 <c>inject</c> 加 <c>dotnet test</c>（「测试与实测分工」）。</para>
/// </summary>
/// <para><b>入录（2026-10-05 用户裁决）</b>：合成输入本身也进 Journal，所以账上能读出
/// 「玩家这一手是按哪个键按的、点了哪个坐标」。<b>入录口是原型侧递进来的委托</b>
/// （<see cref="Vocabulary"/> 的第二个参数），本件不认得任何原型的词表也不碰
/// <c>ReplayAssembly</c> —— 那是逻辑层的账，合成输入是表现层事件，两者由原型那一句接线缝上。
/// ⇒ 录制端「点了 (900,600)」与「因此落建一座酒馆」在账上是相邻的两条，顺序可读。
///
/// <para><b>重放端复现不了这一层</b>（这一条决定了它只增可读性、不增断言能力）：
/// 重放端是纯 C#，没有引擎也没有光标，<see cref="Input.ParseInputEvent"/> 那条玩家路径
/// 根本不存在。所以重放端消费这三条时只做一件事——<b>把它们原样跳过</b>，
/// 真正的状态变更由紧随其后的逻辑写（<c>PlaceBuilding</c> 等）承担，那笔才是重放断言的落点。
/// 这就是「断言比快照」：状态级真值只有一份，在逻辑写那一侧。</para>
///
/// <para><b>为什么 push 与 release 都记</b>：漏一个会让账上出现「按下过但没抬起」的半个动作，
/// 而重放端跳过它们，所以这半条不会造成状态漂移——但它会让读账的人以为玩家手一直按着。
/// 成对记下，账上读起来才是一次完整的按键。</para>
///
/// <para><b>一帧滞后</b>：合成由<b>下一帧</b>消费（类注事实 ①），所以注入那一发记的是
/// <b>注入时刻</b>的步号，不是消费帧的步号。这与「写路径全录」那一批条目同样偏前一步，
/// 而重放端跳过这些条目 ⇒ 不会引入 tick 错位。</para>
///
/// <para><b>不进 Journal 的两条</b>：<c>input_hover</c>（动真光标属相机/表现层，
/// 「写路径进 Journal」明写相机动词严禁进录制）与 <c>input_read</c>（纯读）。
/// 这两条入录就是伪造——账上会出现一笔谁也没写的写。</para>
/// </summary>
public static class InputInjector
{
    /// <summary>
    /// 入录回调：把这笔合成输入记进 Journal。<b>动词名由调用方定</b>——
    /// 本件不规定 Journal 里叫什么，因为各原型的词表是各自的事。
    /// </summary>
    /// <param name="kind">入录的那一类（动作 / 点击）。本件用它选动词名，别处不读它。</param>
    /// <param name="args">规范化后的参数串（点位已定点化）。</param>
    public delegate void RecordInput(InputKind kind, string args);

    /// <summary>合成输入的两类可入录动作（<c>input_hover</c> 与 <c>input_read</c> 不在其中，理由见类注）。</summary>
    public enum InputKind
    {
        /// <summary>InputMap action 的按下与抬起（<c>name|1|0</c>）。</summary>
        Action,

        /// <summary>鼠标键点击（<c>x|y|button|1|0</c>）。</summary>
        Click,
    }

    /// <summary>
    /// 四个动词的词表（2026-10-05）。原型侧在 <c>DebugConsole.Attach</c> 的动词数组里摊一句
    /// <c>.. InputInjector.Vocabulary(this)</c> 即可；要入录就多递一个 <see cref="RecordInput"/>。
    ///
    /// <para><b>为什么实现上收、注册留在原型侧</b>：实现是零游戏知识的公共代码（「沉淀方向」分法：
    /// 产物进不进最终包——<c>GugaFramework/</c> 根下是随游戏运行的 C#），而注册进的是原型那一份
    /// 动词表，所以「标准七动词」仍是七个，契约一个字不用动。抽这个 <c>Vocabulary</c> 是为了脚手架
    /// 趋零：四个动词抄四遍就是八行样板。</para>
    ///
    /// <para>清单：<c>input_action</c>（InputMap action 的按下与抬起）、<c>input_click</c>
    /// （鼠标键，带视口坐标）、<c>input_hover</c>（把光标移到某点，3D 拾取悬停用）、
    /// <c>input_read</c>（读输入层全貌）。</para>
    ///
    /// <param name="scope">读视口那一侧的节点（<c>input_read</c> 用）。</param>
    /// <param name="record">
    /// 入录回调，缺省不记（<c>null</c>）。<b>动词名由它自己那份词表决定</b>——
    /// 本件不规定 Journal 里叫什么，因为各原型的词表是各自的事。
    /// </param>
    /// </summary>
    public static (string Verb, Func<JsonElement, object?> Handler)[] Vocabulary(
        Node scope, RecordInput? record = null)
    {
        return new (string Verb, Func<JsonElement, object?> Handler)[]
        {
            ("input_action", a => SendAction(a, record)),
            ("input_click", a => SendClick(a, record)),
            ("input_hover", SendHover),
            ("input_read", _ => ReadState(scope)),
        };
    }

    /// <summary>
    /// input_action：送一发 <see cref="InputEventAction"/>。按下与抬起是两个动作。
    /// 两者都入录（成对，理由见类注）。
    /// </summary>
    private static object? SendAction(JsonElement args, RecordInput? record)
    {
        var name = args.Str("name", required: true);
        var pressed = args.Bool("pressed", required: true);
        Input.ParseInputEvent(new InputEventAction
        {
            Action = name,
            Pressed = pressed,
            Strength = pressed ? 1.0f : 0.0f,
        });
        // 入录在 ParseInputEvent 之后：注入本身没有成败可言（引擎不回报「这个 action 收下了」），
        // 所以记不记只取决于有没有递 sink，不取决于返回什么。
        record?.Invoke(InputKind.Action, $"{name}|{(pressed ? 1 : 0)}");
        // 不回读任何状态，也不指向 input_read —— 见类注事实 ⑤：action 的按下态消费后即清，
        // 桥上读到的永远是 0。读数要问世界自己（各原型的快照动词 / eval 读数）。
        return $"input=action name={name} pressed={pressed}";
    }

    /// <summary>input_click：送一发 <see cref="InputEventMouseButton"/>，带视口坐标。</summary>
    private static object? SendClick(JsonElement args, RecordInput? record)
    {
        var button = ButtonArg(args);
        var x = args.Num("x", required: true);
        var y = args.Num("y", required: true);
        var pressed = args.Bool("pressed", required: true);
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = pressed,
            Position = new Vector2((float)x, (float)y),
        });
        // 参数串用定点文本（Fmt），与日志同一份口径：账上逐字可比，
        // 「(900.5,600)|left|1」不会因为区域设置在两台机器上长得不一样。
        record?.Invoke(InputKind.Click, $"{Fmt(x)}|{Fmt(y)}|{ButtonName(button)}|{(pressed ? 1 : 0)}");
        return $"input=click button={ButtonName(button)} pressed={pressed} x={Fmt(x)} y={Fmt(y)}";
    }

    /// <summary>input_hover：把真光标移到视口某点，让读鼠标位置那类逻辑（3D 拾取悬停）有输入可吃。</summary>
    private static object? SendHover(JsonElement args)
    {
        var x = args.Num("x", required: true);
        var y = args.Num("y", required: true);
        Input.WarpMouse(new Vector2((float)x, (float)y));
        return $"input=hover x={Fmt(x)} y={Fmt(y)} note=warps-the-real-cursor";
    }

    /// <summary>
    /// input_read：读输入层里**留得住**的那两样——视口鼠标位置，与可注入的 action 名单。
    ///
    /// <para><b>不报 action 的按下态</b>（类注事实 ⑤）：合成 <see cref="InputEventAction"/> 的按下态
    /// 只在被消费的那一帧存在，消费完即清。桥的请求要等下一次主循环才派发，所以
    /// <see cref="Input.IsActionPressed"/> 在这里读到的永远是 0——报出来就是个假读数。
    /// 注入有没有生效要问世界自己：各原型的快照动词或 <c>eval</c> 读数。</para>
    ///
    /// <para>不报游戏状态：那个由 <c>snapshot_debug</c> 报。两条真值会漂，而「显示即真相」要的是
    /// 同一份数。</para>
    /// </summary>
    private static object? ReadState(Node scope)
    {
        var display = DisplayServer.GetName();
        // 无头没有光标，那一格恒零且无意义（类注事实 ③④）。显式标出来，别让读数看着像「鼠标在原点」。
        var mouseMeaningful = !string.Equals(display, "headless", StringComparison.Ordinal);
        var mouse = scope.GetViewport().GetMousePosition();
        var names = new List<string>();
        foreach (var action in InputMap.GetActions()) names.Add(action.ToString());
        names.Sort(StringComparer.Ordinal);
        return new Dictionary<string, object?>
        {
            ["display"] = display,
            ["mouse"] = new { x = mouse.X, y = mouse.Y },
            ["mouseMeaningful"] = mouseMeaningful,
            ["injectableActions"] = names.ToArray(),
        };
    }

    /// <summary>
    /// 读鼠标键档。三档，写错的档当场抛，不折回 <see cref="MouseButton.Left"/>——折回去就是
    /// 「我说了右键、实际按了左键」而读数上分不开。
    ///
    /// <para><b>为什么这个档位不落 <see cref="BridgeArgs"/></b>：那份是 <c>DebugBridge/</c> 里
    /// 唯一的纯 C# 件（零 Godot 类型，所以能被测试项目链进去），而 <see cref="MouseButton"/> 是
    /// Godot 枚举。⇒ 与 Godot 无关的标量走 <c>BridgeArgs</c>，枚举档留在认识那个枚举的代码旁边。</para>
    /// </summary>
    private static MouseButton ButtonArg(JsonElement args)
    {
        var name = args.Str("button", required: true);
        return name switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => throw new ArgumentException($"参数 --button 只接 left / right / middle，收到 \"{name}\""),
        };
    }

    private static string ButtonName(MouseButton button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => button.ToString(),
    };

    /// <summary>定点文本：日志一律不变文化（「double 与不变文化」），别让区域设置把小数点写成逗号。</summary>
    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
