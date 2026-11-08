#nullable enable
using System;
using System.Globalization;
using System.Text.Json;

namespace DemoFactory.GugaFramework.DebugBridge;

/// <summary>
/// 调试桥动词的<b>取参口径</b> —— 整条桥读 args 只有这一处（各原型共用，2026-09-28 从某一侧的
/// 私有 <c>Num</c>/<c>Str</c> 与 <c>IntArg</c> 上收；此前三家三形状：
/// 一家缺参即抛、一家用 <c>GetProperty().GetDouble()</c>（收到字符串就抛）、
/// 一家缺参静默兜档）。
///
/// 本文件是 <c>DebugBridge/</c> 里唯一的纯 C# 件（只吃 <see cref="JsonElement"/>，零 Godot 类型），
/// 所以四份 csproj 的排除清单按文件点名排 <c>DebugBridgeHost.cs</c>/<c>DebugBridgeEval.cs</c>/
/// <c>DebugConsole.cs</c> 三件，本文件照常源码链接进测试项目 —— 取参口径是能被断言的那一半。
///
/// 四个方法的形状：<paramref name="required"/> 为真时缺参当场抛并指名是哪个参数（不兜 0），
/// 为假时缺参返回 0 / 空串 / false；<see cref="Int"/> 多给一个显式缺省值，留给「计数档」这类可选参数。
/// 数字与字符串两种写法都接：CLI 侧 <c>Debug.cs</c> 的 <c>Coerce</c> 已把数字串转成数，
/// 而 eval 脚本里手写的 args 可能就是串。解析一律 <see cref="CultureInfo.InvariantCulture"/>。
/// </summary>
public static class BridgeArgs
{
    /// <summary>读一个数字参数（返回 double，调用方按需要窄化）。</summary>
    public static double Num(this JsonElement args, string name, bool required = false)
    {
        if (!args.TryGetProperty(name, out var value))
        {
            if (required) throw Missing(name);
            return 0d;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;

        var text = value.GetString();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"参数 --{name} 不是数字，收到 \"{text}\"");
    }

    /// <summary>读一个整数参数，缺省时返回 <paramref name="fallback"/>（计数档：如一次跑几格）。</summary>
    public static int Int(this JsonElement args, string name, int fallback)
        => args.TryGetProperty(name, out _) ? (int)args.Num(name, required: true) : fallback;

    /// <summary>读一个字符串参数；缺参返回空串。</summary>
    public static string Str(this JsonElement args, string name, bool required = false)
    {
        if (!args.TryGetProperty(name, out var value))
        {
            if (required) throw Missing(name);
            return string.Empty;
        }
        return value.GetString() ?? string.Empty;
    }

    /// <summary>
    /// 读一个布尔参数（返回 false）。数字与字符串两种写法都接：CLI 侧 <c>Debug.cs</c> 的
    /// <c>Coerce</c> 已把字面 <c>true</c>/<c>false</c> 转成 JSON 布尔，而 eval 脚本里手写的
    /// args 可能就是串。
    ///
    /// <para>为什么按缺参即 false 处理，而不是兜 true：<see cref="Str"/> 那两个兜 0/空串，
    /// 兜出来的值至少还能一眼看出「没给」。布尔兜 true 会把「忘了写 pressed」变成一次真实
    /// 按下，而输入合成按错一次就是往世界里注入了一个不该有的动作——所以
    /// <c>input_action</c> 那一侧一律 <c>required: true</c>，这个缺省档只留给真可选的口子。</para>
    /// </summary>
    public static bool Bool(this JsonElement args, string name, bool required = false)
    {
        if (!args.TryGetProperty(name, out var value))
        {
            if (required) throw Missing(name);
            return false;
        }

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;

        var text = value.ToString();
        return bool.TryParse(text, out var parsed)
            ? parsed
            : throw new ArgumentException($"参数 --{name} 不是布尔，收到 \"{text}\"（只接 true / false）");
    }

    private static ArgumentException Missing(string name)
        => new($"动词缺参数 --{name}（缺它我不会兜 0：兜 0 就是静默摆到第 0 排）");
}
