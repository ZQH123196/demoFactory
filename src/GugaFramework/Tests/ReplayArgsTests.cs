#nullable enable
using System.Globalization;
using DemoFactory.GugaFramework.Replay;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 剧本参数的编码口径 —— 一份，所有原型共用。
///
/// <para>这里每一条都对着一种「看起来对、其实逐字会分叉」的写法：区域设置、单精度升双精度、
/// 段数不符时兜默认值。收编前这些散在三份拷贝里（一份抽了 <c>Two</c>/<c>Three</c>、
/// 一份在 switch 里内联了五次），改一处不会带着另两处走。</para>
/// </summary>
public class ReplayArgsTests
{
    [Fact]
    public void 整数token区域无关()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // 换个用逗号当小数点的区域设置：写出去的 Journal 逐字不能跟着变
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            ReplayArgs.Int(2000000).ShouldBe("2000000");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 单精度不升双精度_零一不能被打成那串长数()
    {
        ReplayArgs.Number(0.1f).ShouldBe("0.1");
        ReplayArgs.Number(0.1f).ShouldNotBe(ReplayArgs.Number((double)0.1f), "float 升 double 再走 \"R\" 就是这条 —— 逐字基线当场分叉");
    }

    [Fact]
    public void 浮点往返逐字相等()
    {
        foreach (var v in new[] { 0.1f, 1f / 3f, -45.38f, 1e-7f })
        {
            var text = ReplayArgs.Number(v);
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var back).ShouldBeTrue($"读不回 {text}");
            back.ShouldBe(v, $"{text} 读回来不等于写出去的数");
        }
    }

    [Fact]
    public void 双精度同样往返逐字相等()
    {
        var text = ReplayArgs.Number(0.1d + 0.2d);
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var back).ShouldBeTrue();
        back.ShouldBe(0.1d + 0.2d);
    }

    [Fact]
    public void 切段数不符判否_不兜默认值()
    {
        ReplayArgs.Split("a|b", 2, out var two).ShouldBeTrue();
        two.ShouldBe(new[] { "a", "b" });

        ReplayArgs.Split("a", 2, out _).ShouldBeFalse("少一段就判否 —— 兜 0 = 静默摆到第 0 排");
        ReplayArgs.Split("a|b|c", 2, out _).ShouldBeFalse();
    }

    [Fact]
    public void 拼串用同一个分隔符_空token保留位置()
    {
        ReplayArgs.Join("a", "", "c").ShouldBe("a||c", "中间那格是「没给」，位置要留着 —— 丢了就变成另一个 token");
    }
}
