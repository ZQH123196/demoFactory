#nullable enable
using System.Globalization;

namespace DemoFactory.GugaFramework.Replay;

/// <summary>
/// 剧本参数的**分隔与规范化**口径 —— 一份，所有原型共用。
///
/// 参数一律竖线分隔、位置固定（每条动词要几段由它自己的词表说了算）。这层只管两件事：
/// 切段、把所有数字排成<b>唯一那一种文本</b>。后者是「同一逻辑事实在 Journal 里只有一种 token」
/// 的前提 —— 少了它，同一栋建筑会按调用方当时写的界面短名或枚举名各记一遍。
///
/// <para>口径两条：<b>缺段当场判否</b>（不兜 0，「桥只转发」 记的就是那句「兜 0 就是静默摆到第 0 排」）；
/// <b>所有数字走 <see cref="CultureInfo.InvariantCulture"/></b>，浮点走 <c>"R"</c> 往返格式 ——
/// 少一个 CultureInfo，同一台机器不同区域设置下 Journal 就会分叉。</para>
///
/// <para>它替代了逐原型各写一份的 <c>Two</c> / <c>Three</c> 与内联在 switch 里的
/// <c>args.Split('|')</c>（原先有一份内联了五处）。</para>
/// </summary>
public static class ReplayArgs
{
    /// <summary>竖线分隔符。切段与拼串共用这一个字面量，不各自写。</summary>
    public const char Separator = '|';

    /// <summary>整数 token：区域无关。</summary>
    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>浮点 token：<c>"R"</c> 往返格式，读回来逐字等于写出去的数。</summary>
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// 单精度浮点 token。<b>单精度与双精度各有一个重载，不是同一个。</b>
    /// <c>float</c> 升成 <c>double</c> 再走 <c>"R"</c>，<c>0.1f</c> 会打成
    /// <c>0.10000000149011612</c> —— 逐字基线当场分叉。走 float 那一路才是「读回来逐字等于写出去的数」。
    /// </summary>
    public static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>按位置拼回一条 args（token 已各自排好序）。</summary>
    public static string Join(params string[] tokens) => string.Join(Separator, tokens);

    public static bool TryInt(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryNumber(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>切成恰好 <paramref name="count"/> 段。段数不符判否（缺参数不兜默认值）。</summary>
    public static bool Split(string args, int count, out string[] tokens)
    {
        var parts = args.Split(Separator);
        if (parts.Length != count)
        {
            tokens = [];
            return false;
        }

        tokens = parts;
        return true;
    }
}
