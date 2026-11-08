#nullable enable
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace _Template.Tests;

/// <summary>
/// 接线自检 — 证明「测试项目链得上 GugaFramework、逻辑时钟不吃引擎就能推进」。
///
/// 本文件不引用任何原型代码，所以复制到新原型后**原样有效**：
/// 它红了说明红的不是业务逻辑，而是 csproj 的源码链接与排除面（比如有人把
/// GugaFramework/Camera 的排除删了，引擎引用就顺着漏进测试进程）。
/// 逻辑世界接上之后把它删掉也行 —— 那时第一个真断言就接上了。
/// </summary>
public class WiringSmokeTests
{
    [Fact]
    public void 逻辑时钟不吃引擎_可独立推进()
    {
        var clock = new GameClock();
        clock.Advance(3).ShouldBeTrue();
        clock.Tick.ShouldBe(3);
    }

    [Fact]
    public void 暂停时空转_恢复后接着走()
    {
        var clock = new GameClock();
        clock.Advance(2);
        clock.Pause();
        clock.Advance(5).ShouldBeFalse("暂停中 Advance 应空转并返回 false");
        clock.Tick.ShouldBe(2);
        clock.Resume();
        clock.Advance(1);
        clock.Tick.ShouldBe(3);
    }
}
