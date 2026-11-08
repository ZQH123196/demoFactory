#nullable enable
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 接线自检 — 证明「测试项目链得上 GugaFramework、逻辑时钟不吃引擎就能推进」，
/// 外加空世界能被推进而不炸（ticket 01 的验收）。
///
/// <para>本文件不引用任何引擎类型，所以复制到新原型后原样有效：
/// 它红了说明红的不是业务逻辑，而是 csproj 的源码链接与排除面。</para>
/// </summary>
public class WiringSmokeTests
{
    [Fact]
    public void 逻辑时钟不吃引擎_可独立推进()
    {
        var world = new MistCityWorld(new MistCityConfig());
        world.Advance(3).ShouldBeTrue();
        world.Tick.ShouldBe(3);
    }

    [Fact]
    public void 暂停时空转_恢复后接着走()
    {
        var world = new MistCityWorld(new MistCityConfig());
        world.Advance(2);
        world.Pause();
        world.Advance(5).ShouldBeFalse("暂停中 Advance 应空转并返回 false");
        world.Tick.ShouldBe(2);
        world.Resume();
        world.Advance(1);
        world.Tick.ShouldBe(3);
    }

    [Fact]
    public void 空世界可被推进与快照_推进零步不炸()
    {
        var world = new MistCityWorld(new MistCityConfig { Seed = 7 });

        // 空壳阶段快照只有头部与空桌那一段 —— 断言它读得出，而不是断言它的内容形状
        // （内容形状由后面几张 ticket各自的真断言管）。
        world.DebugSnapshot().ShouldContain("tick=0");
        world.DebugSnapshot().ShouldContain("seed=7");

        world.Advance(0).ShouldBeFalse("推进零步是空转，不是前进");
        world.Tick.ShouldBe(0);
        world.Advance(600);
        world.Tick.ShouldBe(600, "一步 = 0.1 秒，推进 600 步就是 60 秒");
    }
}
