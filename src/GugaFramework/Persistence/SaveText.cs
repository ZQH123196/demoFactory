#nullable enable
using System;
using System.Globalization;
using System.Text;
using DemoFactory.GugaFramework.Replay;

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>
/// JSON 最小集的字面量口径 —— 写侧转义、读侧还原、两侧共用的数字文本校验。
///
/// <para><b>为什么不引三方库</b>：这一层只需要「对象 / 数组 / 字符串 / 数字 / 布尔」五种值，
/// 而存档格式是一份对外承诺——每多一个依赖，格式就多一处「升级依赖就换一份存档」的可能。
/// 手写的那一份与 <c>ReplayScriptJson</c> 同一形状，照抄它的转义集（引号 / 反斜杠 / 控制符）。</para>
///
/// <para><b>数字口径复用 <see cref="ReplayArgs"/></b>：invariant 加 <c>"R"</c> 往返。
/// 少一个 <c>CultureInfo</c>，同一台机器不同区域设置下存档就会分叉。</para>
/// </summary>
internal static class SaveText
{
    /// <summary>包上引号的字符串字面量（最小转义集，与 <c>ReplayScriptJson</c> 同形状）。</summary>
    public static string Quoted(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>UTF-8 字节数 —— 体积上限按字节算，不按字符算。</summary>
    public static int ByteLength(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>
    /// 浮点字面量。非有限数当场拒：<c>NaN</c> 与无穷写进 JSON 就是一个读不回来的档，
    /// 而它出现的原因永远是写出口没守住（除零、空向量），不该在这里兜成一个数。
    /// </summary>
    public static string Double(double value, string key)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(key, value, "存档里不能有非有限数（NaN / 正负无穷）");

        return ReplayArgs.Number(value);
    }

    /// <summary>单精度字面量。走 float 那一路，<c>"R"</c> 才逐字等于写出去的数。</summary>
    public static string Single(float value, string key)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(key, value, "存档里不能有非有限数（NaN / 正负无穷）");

        return ReplayArgs.Number(value);
    }

    /// <summary>整数字面量。</summary>
    public static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>布尔字面量。</summary>
    public static string Flag(bool value) => value ? "true" : "false";

    /// <summary>读侧的浮点还原。文本不是数就当场抛，不兜 0（「桥只转发」同一条理由）。</summary>
    public static double ParseDouble(string text, string key)
    {
        if (!ReplayArgs.TryNumber(text, out var value))
            throw new FormatException($"{key}：读到 {text}，那不是一个数");

        return value;
    }

    /// <summary>读侧的整数还原。</summary>
    public static long ParseInteger(string text, string key)
    {
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"{key}：读到 {text}，那不是一个整数");

        return value;
    }
}
