#nullable enable
using System;
using DemoFactory.GugaFramework.Tests;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 确定性哨兵 —— <b>三个观测口里「时间可控」那一口的体检</b>。
///
/// <para><b>三条</b>：① 同种子跑两遍逐字相等；② 逐步推进与大块快进逐字相等；
/// ③ 暂停时推进世界冻住不动，恢复后接着走。三条都拿 <c>ReplaySnapshot()</c> 比 ——
/// 「断言比快照」：断言强度只比检查点快照，不去比事件流。</para>
///
/// <para>跑的那一局要<b>真的动起来</b>：停靠 → 消费 → 办赛 → 防守 → 航行，
/// 只跑开局那 600 步的话三条哨兵全都在比一份静止的世界，等于什么也没验。</para>
/// </summary>
public class DeterminismTests
{
    private const int Seed = 7;

    /// <summary>跑一段带全部场合的一局（停靠、消费、办赛、防守、航行），回落到一个固定步号。</summary>
    private static ArenaWorld RunOneGame(int seed, int chunkTicks)
    {
        var world = ArenaWorldBuilder.New(seed).Build();
        ArenaSetup.Inject(world, "Dock", "poor_city", out _, out _).ShouldBeTrue();
        for (var i = 0; i < 400 && !world.IsDocked; i++) world.Advance(1);

        // 消费（游客进镇花钱）。
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * 2), chunkTicks);

        // 办赛（走位会动、伤害会算、魂石会进账）。
        ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out _).ShouldBeTrue();
        world.AdvanceTo(world.Tick + (int)(ArenaTime.StepsPerGameHour * 1.6), chunkTicks);

        // 拔锚防守（配对、生死、清剿、抢货）。
        ArenaSetup.Inject(world, "PullAnchor", "", out _, out _).ShouldBeTrue();
        world.AdvanceTo(world.Tick + (int)(ArenaTime.StepsPerGameHour * 2.1), chunkTicks);

        // 航行（脱队觅食那一档没触发也要走到富城边上）。
        ArenaSetup.Inject(world, "Dock", "rich_city", out _, out _).ShouldBeTrue();
        world.AdvanceTo(world.Tick + 2000, chunkTicks);
        return world;
    }

    [Fact]
    public void 同种子两跑_重放快照逐字相等()
    {
        var a = RunOneGame(Seed, 250);
        var b = RunOneGame(Seed, 250);

        a.Tick.ShouldBe(b.Tick);
        b.ReplaySnapshot().ShouldBe(a.ReplaySnapshot(),
            "同种子两跑漂了 —— 随机泄漏会让分布好看，这一局本身却不可信。");
    }

    [Fact]
    public void 换个种子_跑出的是另一局()
    {
        // 哨兵的反面：换一颗种子就该跑出不同的一局，否则「两跑相等」也可能只是「根本没动」。
        var a = RunOneGame(Seed, 250);
        var b = RunOneGame(Seed + 1, 250);

        b.ReplaySnapshot().ShouldNotBe(a.ReplaySnapshot(), "换种子没有换出差别 —— "
            + "「同种子两跑相等」就可能只是这局压根没被种子牵着走。");
    }

    [Fact]
    public void 逐步推进与大块快进_重放快照逐字相等()
    {
        // 表现层的帧率不该改写逻辑：一步一步走与一大块一大块走得是同一局。
        var stepwise = RunOneGame(Seed, 1);
        var chunked = RunOneGame(Seed, 250);

        stepwise.Tick.ShouldBe(chunked.Tick);
        chunked.ReplaySnapshot().ShouldBe(stepwise.ReplaySnapshot(), "逐步 vs 大块快进漂了。");
    }

    [Fact]
    public void 暂停时推进_世界冻住不动()
    {
        var world = RunOneGame(Seed, 250);
        var tickAtPause = world.Tick;
        var frozen = world.ReplaySnapshot();

        world.Pause();
        world.Advance(500);
        world.Tick.ShouldBe(tickAtPause, "暂停中步号不该动。");
        world.ReplaySnapshot().ShouldBe(frozen, "暂停中世界不该演化。");

        world.Advance(500);
        world.ReplaySnapshot().ShouldBe(frozen, "暂停多久都不该演化。");

        world.Resume();
        world.Advance(1);
        world.Tick.ShouldBe(tickAtPause + 1, "恢复后接着走。");
        world.ReplaySnapshot().ShouldNotBe(frozen, "恢复之后世界重新动起来。");
    }

    [Fact]
    public void 暂停与不暂停各跑一遍_解冻之后的走向与一路跑到底一致()
    {
        // 暂停只冻逻辑、不改演化：中途停一下再继续，终局快照与一路跑完的那一局逐字相同。
        var straight = RunOneGame(Seed, 250);
        var paused = RunOneGame(Seed, 250);

        paused.Pause();
        paused.Advance(37);
        paused.Resume();
        paused.AdvanceTo(straight.Tick, 250);

        paused.Tick.ShouldBe(straight.Tick);
        paused.ReplaySnapshot().ShouldBe(straight.ReplaySnapshot(), "暂停过的那一局与没暂停的漂了。");
        TestLog.Ledger($"确定性哨兵对照步号 {straight.Tick}，快照长度 {straight.ReplaySnapshot().Length} 字符。");
    }
}
