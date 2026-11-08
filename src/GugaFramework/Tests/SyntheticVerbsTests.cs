#nullable enable
using DemoFactory.GugaFramework.Replay;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 合成输入那两条的形状契约（2026-10-05 用户裁决进 Journal 时落地）。
///
/// <para>它防的是「三处跳过实现各写一遍」那种漂：三个原型各自要在自己的
/// <c>Inject</c> 里接住这两条，接法一有差别就出现「某一处漏接 ⇒ 重放端 ok 翻转」
/// （那在断言三层里任何档位都红）。所以校验与规范化只许有一份实现，
/// 这一层钉住那份实现的形状，各原型那份只是转发。</para>
///
/// <para>为什么这里能替所有原型答：这两条记的是<b>玩家输入</b>，形状对每个原型都一样
/// （谁被接管无所谓——重放端一律跳过）。原型差异在别处（拾取谁、落建成什么），
/// 那类不归这一层（「原型隔离」）。</para>
///
/// <para>「跳过」这条本身也钉住：它不碰世界是重放端能逐字重放的前提 ——
/// 若哪一处在跳过时顺手改了状态，账上就多出一笔推不动的动作而快照跟着漂。</para>
/// </summary>
public class SyntheticVerbsTests
{
    [Fact]
    public void 词表两个名字与规范写法()
    {
        SyntheticVerbs.Action.ShouldBe("SyntheticInputAction");
        SyntheticVerbs.Click.ShouldBe("SyntheticInputClick");
        SyntheticVerbs.Names.ShouldBe([SyntheticVerbs.Action, SyntheticVerbs.Click]);
        SyntheticVerbs.ActionSegments.ShouldBe(2);
        SyntheticVerbs.ClickSegments.ShouldBe(4);
    }

    [Fact]
    public void 动作两段原样规范化()
    {
        SyntheticVerbs.Skip("place_market|1", SyntheticVerbs.ActionSegments, out var args, out var reason)
            .ShouldBeTrue();
        args.ShouldBe("place_market|1", "action 名与两态本就是规范 token，原样");
        reason.ShouldBeEmpty();
    }

    [Fact]
    public void 点击点位走定点化()
    {
        // 定点化是为了区域设置：中文区域若让小数点写成逗号，
        // 同一笔点击在两台机器的账上就长得不一样，逐字比对当场分叉。
        SyntheticVerbs.Skip("900.5|600|left|1", SyntheticVerbs.ClickSegments, out var args, out _)
            .ShouldBeTrue();
        args.ShouldBe("900.5|600|left|1");

        SyntheticVerbs.Skip("900|600.25|right|0", SyntheticVerbs.ClickSegments, out var args2, out _)
            .ShouldBeTrue();
        args2.ShouldBe("900|600.25|right|0", "定点文本不补零也不丢精度到能分辨的那一档");
    }

    [Fact]
    public void 坏参数判否且带原因()
    {
        SyntheticVerbs.Skip("place_market", SyntheticVerbs.ActionSegments, out _, out var r1).ShouldBeFalse();
        r1.ShouldNotBeEmpty("拒绝要带原因 —— 账是给人读的，无声判否等于账本损坏");

        SyntheticVerbs.Skip("900|600|left", SyntheticVerbs.ClickSegments, out _, out _).ShouldBeFalse();
        SyntheticVerbs.Skip("a|b|left|1", SyntheticVerbs.ClickSegments, out _, out _).ShouldBeFalse();
        SyntheticVerbs.Skip(string.Empty, SyntheticVerbs.ActionSegments, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void 空段也算坏()
    {
        // 这一条是实测踩出来的："|".Split('|') 切出两个空串，
        // 段数正好等于 action 动词要的 2 段 —— 只查段数会把空参数当合法放行。
        "\u007C".Split('|').Length.ShouldBe(2, "这条断言是前提：空串也算一段");

        SyntheticVerbs.Skip("|", SyntheticVerbs.ActionSegments, out _, out var r)
            .ShouldBeFalse("段数够但两段全空，不算合法的一笔注入");
        r.ShouldNotBeEmpty();
        SyntheticVerbs.Skip("|left|1", SyntheticVerbs.ClickSegments, out _, out _).ShouldBeFalse();
        SyntheticVerbs.Skip("900|  |left|1", SyntheticVerbs.ClickSegments, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void 失败时规范化args原样返回()
    {
        // 判否的那一笔不会入录（入录守卫在 ReplayAssembly），所以 canonicalArgs 没人读；
        // 但它必须被赋过值 —— 未赋值的 out 参数过不了编译，读代码的人也不该猜它是什么。
        SyntheticVerbs.Skip("bad", SyntheticVerbs.ActionSegments, out var args, out _).ShouldBeFalse();
        args.ShouldBe("bad");
    }
}
