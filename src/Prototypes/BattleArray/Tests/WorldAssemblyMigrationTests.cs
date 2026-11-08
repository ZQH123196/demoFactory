#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace DemoFactory.Prototypes.BattleArray.Tests;

/// <summary>
/// 世界装配迁到 <c>WorldAssembly</c> 之后新钉的钉子 —— 盖「装配与退订成对」这一族。
///
/// <para>迁前那批功能断言证明的是「一局的结果没变」；这一组证明的是另一件事：
/// 走位镜像同步那条订阅交还了没有。迁前 <c>BattleWorld.OnDispose</c> 只摘了地形接线与 <c>Step</c>，
/// 漏了走位那一半 —— 换局之后旧世界还挂在时钟上，每推一步它照旧把坐标写回宿主那一列。</para>
///
/// <para><b>比状态不比步号</b>：释放交还的是订阅，不是时钟（<c>Clock</c> 是公开读数口），
/// 所以那 200 步真的走过去了，步号照涨。世界侧那一格才是「有没有东西还在响应」，
/// 快照头部那两个时钟读数（<c>tick=</c> 与 <c>seconds=</c>）归时钟自己。</para>
/// </summary>
public class WorldAssemblyMigrationTests
{
    /// <summary>
    /// 释放之后时钟再推 200 步：世界状态逐字不变。
    ///
    /// <para><b>盖的是「接上了忘了交还」那一类漏</b>：漏掉的那一处在释放后仍然订阅着脉搏，
    /// 时钟每推一步它就跑一次，镜像坐标继续变 —— 而症状与「世界本来就不动」完全一样。
    /// 所以判据分两半：① 释放后再推 200 步状态逐字不变（症状那一侧）；
    /// ② 一份没释放的对照世界在同一段步里<b>确实变了</b> —— 没有这一半，①在「什么都没订阅」时也成立，
    /// 那就成了一条永远绿的断言。</para>
    ///
    /// <para><b>迁前这一条是红的</b>：镜像同步没退订，<c>pos=</c> 那几格在释放之后照旧在动。
    /// 迁移前实测读数：种 1001、默认阵，第 50 步释放后再推 200 步，
    /// <c>p02</c> 从 (-1.1472327,1.5822434) 走到 (-1.1507227,1.6562086)。</para>
    /// </summary>
    [Fact]
    public void 释放之后时钟再推二百步_世界状态不变()
    {
        var board = new DeploymentBoard();
        var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);

        var opened = world.DebugSnapshot();
        world.Advance(50).ShouldBeTrue("前提：推了 50 步");
        var live = world.DebugSnapshot();
        State(live).ShouldNotBe(State(opened), "前提：这 50 步里世界确实演化了");

        var tickAtRelease = world.Tick;
        world.Dispose();

        // 时钟照样走（释放交还的是订阅，不是时钟），世界不再响应。
        world.Clock.Advance(200).ShouldBeTrue("前提：这 200 步真的走过去了");
        world.Tick.ShouldBe(tickAtRelease + 200, "步号由时钟自己推进，与释放无关");

        State(world.DebugSnapshot()).ShouldBe(
            State(live),
            "释放之后再推时钟，世界状态逐字不变（订阅已全部交还）");

        // 对照：同一段步里，没释放的那一份世界变了。
        // 没有这一半，上面那条在「世界压根没订阅任何东西」时也成立 —— 那不是判据。
        using var control = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);
        control.Advance(50).ShouldBeTrue();
        control.Advance(200).ShouldBeTrue();
        State(control.DebugSnapshot()).ShouldNotBe(
            State(live),
            "对照世界在同一段步里状态确实变了 —— 上面的不变是真判据，不是本来就不动");
    }

    /// <summary>
    /// 退订表成对：构造期登记过、释放后回到空、重复释放不抛。
    ///
    /// <para>「重复释放不抛」是退订成对的另一半：成对的东西退两次就要抛第二次 ——
    /// 症状是换局那一条路上抛一句与机制无关的异常，而那一帧正是最不该出问题的时候。</para>
    /// </summary>
    [Fact]
    public void 退订成对_释放后表清空且重复释放不抛()
    {
        var board = new DeploymentBoard();
        var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);

        world.SubscriptionCount.ShouldBeGreaterThan(0, "前提：构造期登记了退订（脉冲一条 + 走位那一半一条）");

        world.Dispose();

        world.IsDisposed.ShouldBeTrue();
        world.SubscriptionCount.ShouldBe(0, "退订表跑完就清空");

        Should.NotThrow(() => world.Dispose(), "释放是幂等的：退订成对意味着第二次没有可退的东西，也不该抛");
        world.SubscriptionCount.ShouldBe(0, "重复释放不许再登记新的一条");
    }

    /// <summary>
    /// 快照去掉头部那两个时钟读数（<c>tick=</c> 与 <c>seconds=</c>）——
    /// 它们是时钟的读数，不是世界的状态。其余每一行逐字比（头部剩下的状态那一截也留着）。
    /// </summary>
    private static string State(string snapshot)
    {
        var lines = snapshot.Split('\n');
        var head = string.Join(' ', lines[0].Split(' ').Where(cell =>
            !cell.StartsWith("tick=", StringComparison.Ordinal) &&
            !cell.StartsWith("seconds=", StringComparison.Ordinal)));
        return head + "\n" + string.Join("\n", lines.Skip(1));
    }
}