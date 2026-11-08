#nullable enable
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 世界装配迁到 <c>WorldAssembly</c> 之后新钉的钉子 —— 盖「装配与退订」这一族。
///
/// <para>迁前那批功能断言证明的是「行为没变」；这一组证明的是另一件事：
/// 世界的订阅都在构造里登记进基类那张退订表，释放之后那张表回到空，
/// 时钟再推多少步都不再有世界侧的东西响应。</para>
/// </summary>
public class WorldAssemblyMigrationTests : LogicWorldTestBase
{
    /// <summary>
    /// 释放之后时钟再推 50 步：世界状态逐字不变，退订表回到空。
    ///
    /// <para><b>盖的是「接上了忘了交还」那一类漏</b>：漏掉的那一处在释放后仍然订阅着脉搏，
    /// 时钟每推一步它就跑一次，世界状态继续演化 —— 而症状与「世界本来就不动」完全一样。
    /// 所以判据分三半：① 退订表回到空（直接读）；② 状态逐字不变（症状那一侧）；
    /// ③ 一份没释放的对照世界在同一段步里<b>确实变了</b> —— 没有这一半，②在「什么都没订阅」时也成立，
    /// 那就成了一条永远绿的断言。</para>
    ///
    /// <para><b>比状态不比步号</b>：释放交还的是订阅，不是时钟（<c>Clock</c> 是公开读数口），
    /// 所以那 50 步真的走过去了，步号照涨。世界侧那一格才是「有没有东西还在响应」，
    /// 快照头部那个 <c>tick=</c> 归时钟自己。</para>
    /// </summary>
    [Fact]
    public void 释放之后时钟再推五十步_世界状态不变且退订表清空()
    {
        var opened = World.DebugSnapshot();
        World.Advance(5).ShouldBeTrue("前提：推了 5 步");
        var live = World.DebugSnapshot();
        live.ShouldNotBe(opened, "前提：这 5 步里世界确实演化了");

        World.SubscriptionCount.ShouldBeGreaterThan(0, "前提：构造期登记了退订");
        var tickAtRelease = World.Tick;

        World.Dispose();

        World.SubscriptionCount.ShouldBe(0, "退订表跑完就清空");
        World.IsDisposed.ShouldBeTrue();

        // 时钟照样走（释放交还的是订阅，不是时钟），世界不再响应。
        World.Clock.Advance(50).ShouldBeTrue("前提：这 50 步真的走过去了");
        World.Tick.ShouldBe(tickAtRelease + 50, "步号由时钟自己推进，与释放无关");

        State(World.DebugSnapshot()).ShouldBe(
            State(live),
            "释放之后再推时钟，世界状态逐字不变（订阅已全部交还）");

        // 对照：同一段步里，没释放的那一份世界变了。
        // 没有这一半，上面那条在「世界压根没订阅任何东西」时也成立 —— 那不是判据。
        var control = new LogicWorld(randomSeed: 42);
        try
        {
            control.Advance(5).ShouldBeTrue();
            control.Advance(50).ShouldBeTrue();
            State(control.DebugSnapshot()).ShouldNotBe(
                State(live),
                "对照世界在同一段步里状态确实变了 —— 上面的不变是真判据，不是本来就不动");
        }
        finally
        {
            control.Dispose();
        }
    }

    /// <summary>
    /// 快照里去掉头部那个 <c>tick=</c> —— 那一格是时钟的读数，不是世界的状态。
    /// </summary>
    private static string State(string snapshot)
    {
        var head = snapshot.IndexOf(' ');
        return head < 0 ? snapshot : snapshot[head..];
    }
}
