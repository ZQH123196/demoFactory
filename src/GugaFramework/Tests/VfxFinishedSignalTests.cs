#nullable enable
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 一次性档那个 <c>finished</c> 信号「<b>借出时接一次、归还时断一次</b>」的离线那半。
///
/// <para><b>这一格为什么值得单独立一组用例</b>：接与断不成对时，画面上<b>完全看不出区别</b> ——
/// 多挂上去的那几个回调照样走 <c>GiveBack</c>，而它里面那道护栏把多出来的那几回挡住了，
/// 池账也是对的。于是那一格是「节点复用够多次之后才显形，显形的时候读数也读不出来」那一类慢性故障：
/// 唯一能断它的地方就是信号上那个回调个数本身。</para>
///
/// <para><b>这里测的是接线，不是引擎</b>：<c>VfxPlayer</c> 吃引擎类型，<c>Finished</c> 的连接与断开除它
/// 离线跑不了（契约「测试与实测分工」），所以 <see cref="VfxPlayerTranscript"/> 把那一段逐句转写了一份：
/// 节点上挂着 <c>List&lt;Action&gt;</c>，借出那格 <c>+=</c>、归还那格 <c>-=</c>，抛信号时把连着的回调全走一遍。
/// <b>转写与引擎那半对着看才有意义</b>：两边都接都断才叫修好，一边改了另一边没改就是漂。
/// 「引擎那个信号在抛的那一刻会不会拿拷贝」那一格离线不定，要真机看。</para>
///
/// <para>断言口径：账用 <see cref="VfxLedger"/> 的真件；<c>FinishedHandlers</c> 那个数是转写出来的读数
/// （引擎那半没有对应的读数口，它只能靠看信号上挂了几个回调数出来）。</para>
/// </summary>
public sealed class VfxFinishedSignalTests
{
    private const string Unit = "u1";

    /// <summary>一次性档那条轨（对应 <c>res://vfx/FxHit.tscn</c>，<c>one_shot = true</c>）。</summary>
    private const string Hit = "hit";

    /// <summary>持续档那条轨（对应 <c>res://vfx/FxGuardWall.tscn</c>，<c>one_shot = false</c>）。</summary>
    private const string Guard = "guard";

    // ============================ 1. 连演 N 次不积 ============================

    [Fact]
    public void 同一个节点连演二十次_信号上的回调不积_借出时恰好一个()
    {
        // 断的那一格：修掉之前是「每次借出都新挂一个闭包、归还不 -=」，
        // 于是这个节点复用 N 次之后信号上就积着 N 个回调，每演完全走一遍 N 回。
        // 那一格画面上与一个回调没有区别，池账也是对的 —— 只有这个数读得出来。
        var fx = NewStage(capacity: 1);

        for (var i = 0; i < 20; i++)
        {
            fx.Play(Hit, 0.0, 0.0).ShouldBeTrue($"第 {i + 1} 次借得出：上一条已经演完还回来了");
            fx.PooledAt(Hit).FinishedHandlers.ShouldBe(1, "借出那一刻恰好挂了一个回调");

            fx.AdvanceOneShot(Hit).ShouldBeTrue("演完了：抛 finished");
            fx.PooledAt(Hit).FinishedHandlers.ShouldBe(0, "归还那格把它断了 —— 不断的话这一格每演一次涨一个");
        }

        fx.Instances.ShouldBe(1, "复用的是同一个节点");
        fx.PooledAt(Hit).FinishedHandlers.ShouldBe(0, "演完 20 次之后信号上是空的");
        fx.Ledger.Returned.ShouldBe(20, "每一条都还了一次账");
        fx.Ledger.Alive.ShouldBe(0);
        fx.Ledger.InPool.ShouldBe(1, "池回到满的那一份");
    }

    // ============================ 2. 走空闲栈复用那一路 ============================

    [Fact]
    public void 走空闲栈翻出来的那个节点_借出时也不重复连()
    {
        // 单独钉这一格是因为它与「新建节点」是同一条 <c>PlayOneShot</c> 尾巴上的同几行，
        // 两边一起对才叫接一次：断在新建那条路上，翻栈那条路照样会积。
        var fx = NewStage(capacity: 1);

        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        var first = fx.PooledAt(Hit);
        fx.AdvanceOneShot(Hit).ShouldBeTrue();

        for (var i = 0; i < 5; i++)
        {
            fx.Play(Hit, 1.0, 1.0).ShouldBeTrue("还回来了就借得出");
            fx.PooledAt(Hit).ShouldBeSameAs(first, "翻的是空闲栈里那一个");
            first.FinishedHandlers.ShouldBe(1, $"第 {i + 2} 次借出之后仍是 1：上一次归还时断干净了");
            fx.AdvanceOneShot(Hit).ShouldBeTrue();
            first.FinishedHandlers.ShouldBe(0);
        }

        fx.Instances.ShouldBe(1, "来回五次实例数不涨");
    }

    // ============================ 3. 带参数档共用同一份尾巴 ============================

    [Fact]
    public void 带参数档那一档_接与断同样成对()
    {
        // <c>Describe</c> 与 <c>Play</c> 共用同一份一次性尾巴，接 finished 就在那份尾巴上，
        // 所以这一档要单独钉一遍 —— 只在 <c>Play</c> 那边断过不算。
        var fx = NewStage(capacity: 2);

        fx.Describe(Param(2.0)).ShouldBeTrue();
        fx.PooledAt(Hit).FinishedHandlers.ShouldBe(1);
        fx.AdvanceOneShot(Hit).ShouldBeTrue();
        fx.PooledAt(Hit).FinishedHandlers.ShouldBe(0);

        fx.Describe(Param(4.0)).ShouldBeTrue("上一条已经演完还回来了");
        fx.PooledAt(Hit).FinishedHandlers.ShouldBe(1, "第二轮借出：接的还是一个，不是两个");
        fx.AdvanceOneShot(Hit).ShouldBeTrue();
        fx.PooledAt(Hit).FinishedHandlers.ShouldBe(0);
        fx.Ledger.Returned.ShouldBe(2);
        fx.Instances.ShouldBe(1, "复用的是同一个节点");
    }

    // ============================ 4. 引擎多抛一次 ============================

    [Fact]
    public void 引擎对同一个节点多抛一次finished_账仍然平()
    {
        // 断开的直接效果：多出来的那一抛没有回调可走。
        // 所以账平不平靠的是「还的时候断掉了」，不是靠 <c>GiveBack</c> 里面那道护栏。
        var fx = NewStage(capacity: 2);

        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        fx.AdvanceOneShot(Hit, times: 2).ShouldBeTrue("多抛一次也还得了账");
        fx.Ledger.Returned.ShouldBe(1, "只还一次");
        fx.Ledger.InPool.ShouldBe(2, "池回到满的那一份，没有被多还一次推高");
        fx.Ledger.Alive.ShouldBe(0, "恒等式仍然成立");
        fx.NodeIdleOf(Hit).ShouldBe(1, "节点只回空闲栈一次");
        fx.PooledAt(Hit).FinishedHandlers.ShouldBe(0);
    }

    // ============================ 5. 持续档那一路不接 ============================

    [Fact]
    public void 持续档挂上撤下那一路_不接finished_槽位靠显式撤下收()
    {
        // 持续档靠宿主显式撤下收槽位（循环发射的资源压根不抛 finished），
        // 所以那一路上信号上不该有回调 —— 而它挂的是护盾那张资源，与一次性档不是同一个节点
        // （空闲栈按轨分，两边的资源一档不对上就当场拒）。
        var fx = new VfxPlayerTranscript(capacity: 2);

        fx.Sustain(Cue(1)).ShouldBeTrue();
        fx.NodeOf(Unit, Guard).FinishedHandlers.ShouldBe(0, "持续档不接 finished：槽位靠显式撤下收");
        fx.NodeIdleOf(Guard).ShouldBe(0, "挂着的那一条不在空闲栈里");

        fx.Sustain(Cue(0)).ShouldBeTrue("撤下");
        fx.NodeIdleOf(Guard).ShouldBe(1, "撤下之后节点回空闲栈");
        fx.Ledger.InPool.ShouldBe(2, "池位还回来了");
    }

    private static VfxPlayerTranscript NewStage(int capacity)
    {
        var fx = new VfxPlayerTranscript(capacity: capacity);
        fx.RegisterEffect(Hit, "res://vfx/FxHit.tscn", oneShot: true);
        return fx;
    }

    private static CueRow Param(double value)
        => new(1, CueKind.Descriptive, Unit, Hit, "半径", value);

    private static CueRow Cue(double value)
        => new(1, CueKind.Sustained, Unit, Guard, "护盾", value);
}
