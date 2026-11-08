#nullable enable
using System;
using System.Text.Json;
using DemoFactory.GugaFramework.DebugBridge;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// BridgeArgs 取参口径测试 —— 整条调试桥读动词参数的唯一出口（2026-09-28 从排兵布阵的私有
/// <c>Num</c>/<c>Str</c> 与 <c>IntArg</c> 上收，此前三个原型三形状）。
/// 这四件事就是收编要保住的内容：数字与字符串两种写法都接、缺必填参数当场抛并指名、
/// 非必填缺参返回零值而不是凭空造一个档、解析与区域设置无关。
/// 本文件能编进测试项目，靠的是四份 csproj 把 DebugBridge 的排除面从通配改成按文件点名
/// （宿主/求值器/接线那三件是表现层组件，取参件是纯 C#）。
/// </summary>
public class BridgeArgsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void 数字与字符串两种写法都接得住()
    {
        // CLI 侧 Debug.cs 的 Coerce 会把 "20" 转成数；eval 脚本手写的 args 可能就是串。
        Args("""{"m":20}""").Num("m", required: true).ShouldBe(20d);
        Args("""{"m":"20"}""").Num("m", required: true).ShouldBe(20d);
        Args("""{"m":"0.25"}""").Num("m", required: true).ShouldBe(0.25d);
    }

    [Fact]
    public void 缺必填参数_当场抛并把参数名带上()
    {
        // 这条是整件事的核心：兜 0 会让「摆兵」变成「静默摆到第 0 排」，看起来像成功。
        var ex = Should.Throw<ArgumentException>(
            () => Args("""{"kind":"archer"}""").Num("row", required: true));
        ex!.Message.ShouldContain("--row");
    }

    [Fact]
    public void 非必填缺参_返回零值与空串_不凭空造档()
    {
        Args("""{"m":1}""").Num("count").ShouldBe(0d);
        Args("""{"m":1}""").Str("label").ShouldBe(string.Empty);
        // required 的字符串版同样抛（少写一个参数不该悄悄改名成空串发出去）
        Should.Throw<ArgumentException>(() => Args("{}").Str("cmd", required: true));
    }

    [Fact]
    public void 值不是数字_抛并回显收到的是什么()
    {
        var ex = Should.Throw<ArgumentException>(
            () => Args("""{"seed":"abc"}""").Num("seed", required: true));
        ex!.Message.ShouldContain("seed", Case.Sensitive);
        ex!.Message.ShouldContain("abc", Case.Sensitive);
    }

    [Fact]
    public void 计数档取整_缺省走调用方给的那一个()
    {
        // Int 是给 replay_step 的条数、duel 的种子这类「真有合理缺省」的口子的。
        // 与上收前那份 IntArg 的唯一差别：值写成了串也接（旧版遇串就悄悄回落到缺省值）。
        Args("""{"items":5}""").Int("items", 1).ShouldBe(5);
        Args("""{"items":"5"}""").Int("items", 1).ShouldBe(5);
        Args("{}").Int("items", 1).ShouldBe(1);
    }

    [Fact]
    public void 布尔_真值与串两种写法都接得住()
    {
        // CLI 侧 Coerce 把字面 true/false 转成 JSON 布尔；eval 脚本手写的 args 可能就是串。
        Args("""{"pressed":true}""").Bool("pressed", required: true).ShouldBeTrue();
        Args("""{"pressed":"true"}""").Bool("pressed", required: true).ShouldBeTrue();
        Args("""{"pressed":false}""").Bool("pressed", required: true).ShouldBeFalse();
        Args("""{"pressed":"false"}""").Bool("pressed", required: true).ShouldBeFalse();
    }

    [Fact]
    public void 布尔_缺必填参数当场抛_不是兜false()
    {
        // 输入合成按错一次就是往世界里注入一个不该有的动作，所以这一口不许兜。
        // 漏写 --pressed 若兜成 false，表现是「按了没反应」，与真没按同一读数，分不开。
        var ex = Should.Throw<ArgumentException>(
            () => Args("""{"name":"place_market"}""").Bool("pressed", required: true));
        ex!.Message.ShouldContain("--pressed");
    }

    [Fact]
    public void 布尔_非必填缺参返回false_值不是布尔则抛并回显()
    {
        Args("""{"m":1}""").Bool("pressed").ShouldBeFalse();
        var ex = Should.Throw<ArgumentException>(
            () => Args("""{"pressed":"yes"}""").Bool("pressed", required: true));
        ex!.Message.ShouldContain("pressed", Case.Sensitive);
        ex!.Message.ShouldContain("yes", Case.Sensitive);
    }

    [Fact]
    public void 小数点与区域设置无关()
    {
        // 「C# 文件基线」 那份 InvariantCulture 口径在读参数这一环的落地：
        // 逗号小数点的区域设置下，"1.5" 仍然读作 1.5 而不是 15 或抛。
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            Args("""{"m":"1.5"}""").Num("m", required: true).ShouldBe(1.5d);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
