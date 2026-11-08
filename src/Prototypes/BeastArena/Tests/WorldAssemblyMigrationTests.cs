#nullable enable
using System;
using System.Text.RegularExpressions;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 世界装配迁到 <c>WorldAssembly</c> 之后新钉的钉子 —— 盖「装配与退订成对」这一族。
///
/// <para>公共层 <c>GugaFramework/Tests/WorldAssemblyTests</c> 盖的是「退订表跑完就清空、重复释放幂等」
/// 那几格；这一组盖的是<b>症状那一侧</b>：真实世界释放之后到底还回不响应。
/// 迁前这一侧<b>没有 <c>_ExitTree</c>、没有 <c>UnwireWorld</c>、没有 <c>Dispose</c></b> —— 四条时钟订阅
/// 全留在时钟上，谁也没摘，症状是「重开一局之后两个世界一起在动」。</para>
///
/// <para><b>比状态不比步号</b>：释放交还的是订阅，不是时钟（<c>Clock</c> 是公开读数口），
/// 所以那 200 步真的走过去了，步号照涨。快照里归时钟的那两格（<c>tick=</c> 与由它推出来的
/// <c>clock=</c> 罗盘时钟）去掉再比。</para>
///
/// <para>2026-10-15：跟着 MVP 世界接线那一张 ticket 迁到 <see cref="ArenaWorld"/>
/// （原版挂在已删除的切片 1/2 世界上）。断言与两条对照一格没动。</para>
/// </summary>
public class WorldAssemblyMigrationTests
{
    private const int Seed = 20261015;

    /// <summary>
    /// 前提那一段推多少步。
    ///
    /// <para><b>为什么不是 50 步</b>（2026-10-15 迁到 MVP 世界时撞到的那一格）：切片 1/2 那个世界
    /// 每一步都动，所以 50 步就够「确实演化了」那个前提。新版世界的绝大多数结算挂在
    /// <b>整游戏小时</b> 上（饱食度每小时 -1、情绪每小时 +2，都是 600 步一次），
    /// 50 步那条路上除了 <c>tick</c> 与罗盘时钟之外一格都不动 ——
    /// 而那两格正是本条要比之前先剥掉的，于是前提恒假、这条断言变成一条永远红的空断言。</para>
    ///
    /// <para>取 700 步：跨过至少一个整点，那两栏必然各落一次，读数上就分得开。</para>
    /// </summary>
    private const int WarmUpSteps = 700;

    /// <summary>
    /// 释放之后时钟再推 200 步：世界状态逐字不变。
    ///
    /// <para>盖的是「接上了忘了交还」那一类漏</para>
    [Fact]
    public void 释放之后时钟再推二百步_世界状态不变()
    {
        using var world = ArenaWorldBuilder.New(Seed).Build();

        var opened = world.DebugSnapshot();
        world.Advance(WarmUpSteps).ShouldBeTrue($"前提：推了 {WarmUpSteps} 步");
        var live = world.DebugSnapshot();
        State(live).ShouldNotBe(State(opened), "前提：这段步里世界确实演化了");

        var tickAtRelease = world.Tick;
        world.Dispose();

        // 时钟照样走（释放交还的是订阅，不是时钟），世界不再响应。
        world.Clock.Advance(200).ShouldBeTrue("前提：这 200 步真的走过去了");
        world.Tick.ShouldBe(tickAtRelease + 200, "步号由时钟自己推进，与释放无关");

        State(world.DebugSnapshot()).ShouldBe(
            State(live),
            "释放之后再推时钟，世界状态逐字不变（订阅已全部交还）");

        // 对照：同一段步里，没释放的那一份世界变了。
        // 没有这一半，上面那条在「世界压根没订阅任何东西」时也成立 —— 那验不出东西。
        //
        // 对照走两倍暖机步（1 400 步 ≈ 2.33 游戏小时），不是「暖机 + 200」：
        // 结算挂在整游戏小时上，700 + 200 = 900 步仍落在第 1 个小时里，
        // 饱食度与情绪两栏与 `live` 逐字相同 —— 对照于是恒等，上面那条又变成空断言。
        // 跨到第 2 个小时之后那两栏各落一次，读数上分得开。
        using var control = ArenaWorldBuilder.New(Seed).Build();
        control.Advance(WarmUpSteps).ShouldBeTrue();
        control.Advance(WarmUpSteps).ShouldBeTrue();
        State(control.DebugSnapshot()).ShouldNotBe(
            State(live),
            "对照世界在同一段步里状态确实变了 —— 上面的不变是真验出来的，不是本来就不动");
    }

    /// <summary>
    /// 退订表成对：构造期登记过、释放后回到空、重复释放不抛、<c>IsDisposed</c> 断对。
    /// </summary>
    [Fact]
    public void 退订成对_释放后表清空且重复释放不抛()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.IsDisposed.ShouldBeFalse("前提：刚造出来还没放");
        world.SubscriptionCount.ShouldBeGreaterThan(0, "前提：构造期登记了退订");

        world.Dispose();

        world.IsDisposed.ShouldBeTrue();
        world.SubscriptionCount.ShouldBe(0, "退订表跑完就清空");

        Should.NotThrow(() => world.Dispose(), "释放是幂等的：退订成对意味着第二次没有可退的东西，也不该抛");
        world.SubscriptionCount.ShouldBe(0, "重复释放不再登记新的一条");
    }

    /// <summary>
    /// 快照去掉归时钟的那两格（<c>tick=</c> 与由它换算出来的罗盘时钟 <c>clock=</c>）——
    /// 它们是时钟的读数与由它推出来的数，不是世界的状态。
    ///
    /// <para><b>罗盘时钟那一格要整段一起去掉，不能只去掉 <c>clock=</c> 那一个词</b>：
    /// 它的值是 <c>Day 1 · 06:25</c>，<b>值本身带空格</b>。按空格切格再丢掉 <c>clock=</c> 那一格，
    /// 剩下的 <c>1</c> / <c>·</c> / <c>06:25</c> 三段还留在表里 —— 而它们随步号变，
    /// 于是释放之后再推时钟这一条恒红，而它要盖的那件事明明是好的。</para>
    /// </summary>
    private static string State(string snapshot)
        => ClockCell.Replace(TickCell.Replace(snapshot, " "), " ");

    private static readonly Regex TickCell = new(@"\btick=\S+", RegexOptions.Compiled);

    /// <summary>罗盘时钟那一格：从 <c>clock=</c> 起，到下一个段首（<c>phase=</c>）前止。</summary>
    private static readonly Regex ClockCell = new(@"\bclock=.*?(?=\sphase=)", RegexOptions.Compiled);
}
