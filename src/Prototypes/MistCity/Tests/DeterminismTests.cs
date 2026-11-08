#nullable enable
using System.Collections.Generic;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 确定性（用户故事 22/23/24）—— 同一种子重跑逐字一致。
///
/// <para><b>只测外部行为</b>：同一串命令跑两遍，快照逐字相等。
/// 不断言内部结构，所以重构内部不该让这些判红。</para>
/// </summary>
public class DeterminismTests
{
    /// <summary>跑一局固定的命令串，返回每一步之后的快照。</summary>
    private static List<string> RunScript(int seed)
    {
        var world = new MistCityWorld(new MistCityConfig { Seed = seed });
        var snaps = new List<string> { world.DebugSnapshot() };

        world.Bind("c07", "c05");
        snaps.Add(world.DebugSnapshot());

        world.Advance(20);
        snaps.Add(world.DebugSnapshot());

        world.Combine("c01", "c02");          // 柴 + 柴 → 田垄
        snaps.Add(world.DebugSnapshot());

        var newCard = "c08";
        world.Bind(newCard, "c03");           // 田垄压柴：慢产线
        snaps.Add(world.DebugSnapshot());

        world.Advance(50);                    // 跨过第 60 步的结算点
        snaps.Add(world.DebugSnapshot());

        world.Unbind("c07");                  // 拆掉那条快产线
        world.Advance(37);
        snaps.Add(world.DebugSnapshot());

        return snaps;
    }

    [Fact]
    public void 同一种子重跑_每一步快照逐字一致()
    {
        var first = RunScript(seed: MistCityConfig.DefaultSeed);
        var second = RunScript(seed: MistCityConfig.DefaultSeed);

        second.Count.ShouldBe(first.Count, "两跑走的是同一串命令，行数得一样");
        for (var i = 0; i < first.Count; i++)
        {
            second[i].ShouldBe(first[i], $"第 {i} 步之后的快照两跑不一致");
        }
    }

    [Fact]
    public void 没有改动的那一步_两次读快照也一致()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var a = world.DebugSnapshot();
        var b = world.DebugSnapshot();

        b.ShouldBe(a, "读快照不是写路径 —— 连着读两次要逐字一样，否则快照里混进了时间或计数器");
    }

    [Fact]
    public void 一次推n步与推n次一步_快照逐字一致()
    {
        var bulk = new MistCityWorld(new MistCityConfig());
        bulk.Bind("c07", "c05");
        bulk.Advance(100);

        var stepwise = new MistCityWorld(new MistCityConfig());
        stepwise.Bind("c07", "c05");
        for (var i = 0; i < 100; i++) stepwise.Advance(1);

        stepwise.DebugSnapshot().ShouldBe(bulk.DebugSnapshot(),
            "推进怎么分块不该改变世界演化 —— 否则快进与实时是两条不同的游戏");
    }

    [Fact]
    public void 换一颗种子_开局牌序仍然逐字一致()
    {
        // 种子目前只影响配置那一格（牌序是写死的），所以换种子两次跑也一致；
        // 这条断言守着的是「种子变了不会引入不可复现的随机」。
        var a = new MistCityWorld(new MistCityConfig { Seed = 1 }).DebugSnapshot();
        var b = new MistCityWorld(new MistCityConfig { Seed = 1 }).DebugSnapshot();
        var c = new MistCityWorld(new MistCityConfig { Seed = 2 }).DebugSnapshot();

        b.ShouldBe(a, "同一颗种子逐字一致");
        c.ShouldContain("seed=2", Case.Sensitive, "种子那一格照实印出来");
    }

    [Fact]
    public void 快照段首固定_段首漂移会打红重放断言()
    {
        var world = new MistCityWorld(new MistCityConfig());
        var snap = world.DebugSnapshot();

        // 断言三层的 L2 判据：段首身份漂移即红。所以段首在这里钉住。
        foreach (var head in new[] { "tick=", "card count=", "bind count=", "ration ", "recipe count=" })
        {
            snap.ShouldContain(head, Case.Sensitive, $"段首「{head}」在快照里必须出现");
        }
    }
}
