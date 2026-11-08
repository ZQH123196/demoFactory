#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using DemoFactory.GugaFramework.DebugBridge;
using Godot;

namespace DemoFactory.GugaFramework.FrontEnd;

/// <summary>
/// 调试桥的前端共享词表 —— 菜单的「行为可注入」那一格。
///
/// <para><b>为什么要有它</b>：「菜单能开」是静态事实，「按 Esc 之后栈弹对了、鼠标收回来了、
/// 四项设置落到了引擎上」是运行时事实，只读代码证明不了。三发把它变成可注入、可断言的动作：
/// <c>ui_open</c> 开一屏、<c>ui_close</c> 关一屏、<c>ui_read</c> 念出栈与设置、
/// <c>ui_result</c> 填结算屏并开它。
/// 注入完配一次 <c>debug screenshot</c>，出图就能读出那一屏长什么样
/// （「落地配出图」要求出图看得见，这条链就是给它准备的）。</para>
///
/// <para><b>为什么这三发不算进标准七动词</b>：标准七动词只由 <c>DebugConsole.Attach</c> 注册
/// （「标准七动词」），原型侧只递自己那份。菜单这份与 <c>InputInjector</c> / <c>LocaleVerbs</c>
/// 同一档——公共层的共享词表，由原型侧展开注册，不占那七个的名字。</para>
///
/// <para><b>数字参数缺了当场抛</b>（「桥只转发」）：错参数当场回错，拿到一个假读数比拿不到读数更糟。</para>
///
/// <para><b>接法</b>：原型在 <c>DebugConsole.Attach</c> 之后按行展开注册（与
/// <c>InputInjector.Vocabulary</c> 同一形状，理由见各原型 Main.cs 那处注释 —
/// <c>params</c> 的 <c>..</c> 展开只在集合初始化里合法，参数列表里不认）。</para>
/// </summary>
public static class FrontEndVerbs
{
    /// <summary>
    /// 词表：<c>ui_open</c> / <c>ui_close</c> / <c>ui_read</c> / <c>ui_result</c>。
    ///
    /// <para><paramref name="layer"/> 是要注入的那一层——词表不自己找，全仓库一层就一份，
    /// 找不到时该报「没接前端层」，不是「静默开一份新的」。</para>
    /// </summary>
    public static (string Verb, Func<JsonElement, object?> Handler)[] Vocabulary(FrontEndLayer layer)
        =>
        [
            ("ui_open", a => UiOpen(layer, a.Str("screen"))),
            ("ui_close", a => UiClose(layer)),
            ("ui_read", _ => layer.DebugReadout()),
            ("ui_result", a => UiResult(layer, a)),
        ];

    private static object? UiOpen(FrontEndLayer layer, string? screen)
    {
        var name = screen ?? throw new ArgumentException("ui_open 要 screen=<title|pause|settings|result>");
        var target = name.ToLowerInvariant() switch
        {
            "title" => FrontEndScreen.Title,
            "pause" => FrontEndScreen.Pause,
            "settings" => FrontEndScreen.Settings,
            "result" => FrontEndScreen.Result,
            _ => throw new ArgumentException($"ui_open 不认识 screen=「{name}」，要 title / pause / settings / result 之一"),
        };

        var opened = layer.Open(target);
        return $"opened={opened} {layer.DebugReadout()}";
    }

    /// <summary>
    /// 填结算屏并开它 —— <c>ui_result --title "…" [--line "…"]*</c>。
    ///
    /// <para><b>为什么要单独一发</b>：<c>ui_open screen=result</c> 只能开那一屏，
    /// 开出来是「（这一屏的内容由原型递进来）」那行占位字，出图复核看不到结算屏长什么样。
    /// 这一发把占位字换成真内容，出图就能核那一格。</para>
    /// </summary>
    private static object? UiResult(FrontEndLayer layer, JsonElement args)
    {
        var title = args.Str("title", required: true);
        var lines = args.TryGetProperty("lines", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray()
            : [];
        var linesArg = args.Str("line");
        if (!string.IsNullOrEmpty(linesArg)) lines = [.. lines, linesArg];

        layer.SetResult(title, lines, []);
        var opened = layer.Open(FrontEndScreen.Result);
        return $"opened={opened} title={title} lines={lines.Length} {layer.DebugReadout()}";
    }

    private static object? UiClose(FrontEndLayer layer)
    {
        var top = layer.Close();
        return $"top_after_close={top} {layer.DebugReadout()}";
    }
}
