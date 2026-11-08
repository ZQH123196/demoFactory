#nullable enable
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Vfx;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 一次性档节点池的<b>离线那半</b>（ticket 04）：<c>VfxPlayer</c> 那一次借出-归还序列
/// 在账上是什么形状，按齐射的量级钉一遍。
///
/// <para><b>为什么这里测的是账而不是节点</b>：<c>VfxPlayer</c> 吃引擎类型（建节点、摆位、
/// <c>Restart()</c>、接 <c>finished</c>），离线进程里一件都跑不了（契约「测试与实测分工」）。
/// 它每次 <c>Play</c> 走的借与还，本类逐字照抄 <see cref="VfxLedger"/> 的
/// <c>Borrow()</c> / <c>Return()</c>，所以「池账会不会漂」这件事能离线判，
/// 「节点实例数涨不涨」这件事不能 —— 后者真机验（建了几个节点只有引擎知道）。</para>
///
/// <para>断言口径：借不借得出只判布尔与计数，不重抄 <c>Readout()</c> 的逐字格式
/// （那份逐字由 <c>VfxLedgerTests</c> 钉着，重复一遍只会两处一起漂）。</para>
/// </summary>
public sealed class VfxPlayerPoolTests
{
    // ============================ 齐射：同轨一次触发很多条 ============================

    [Fact]
    public void 齐射二十条同轨_池开得下就一条不丢()
    {
        // 池开得下时「演 20 条」与「借不出 0」必须同时成立（spec D10 的另一半）：
        // 丢演出与不丢演出是同一条规则的两端，端点各自都得有断言。
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 }, capacity: 32);

        for (var i = 0; i < 20; i++)
        {
            ledger.Borrow().ShouldBeTrue($"第 {i + 1} 条借得出：池里还有位");
        }

        ledger.Played.ShouldBe(20, "借出成功 20 次就是演了 20 条");
        ledger.BorrowFailed.ShouldBe(0, "池开得下就不该有任何一条被丢");
        ledger.InPool.ShouldBe(12, "32 借走 20 剩 12");
        ledger.Alive.ShouldBe(20, "这 20 条都还没演完");
    }

    [Fact]
    public void 齐射二十条同轨_池开不下就当场丢掉_不排队()
    {
        // 缺省容量从 CueFeedConfig.PendingPerTrack 推导（spec D11），齐射 20 条时只有前 2 条演得出来。
        // 关键在「当场丢」而不是「排队等一会儿」——排队会让「借不出」这个数永远读不出来。
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });

        for (var i = 0; i < 20; i++)
        {
            ledger.Borrow();
        }

        ledger.Played.ShouldBe(2, "只有池里那两条演得出来");
        ledger.BorrowFailed.ShouldBe(18, "剩下 18 条当场丢掉，每条都要在账上");
        ledger.InPool.ShouldBe(0);
        ledger.Alive.ShouldBe(2);
    }

    // ============================ 连续演：池只建一个节点 ============================

    [Fact]
    public void 连演二十次_账上一次都不丢_每一条都还回来了()
    {
        // 「演一遍再演一遍用的是同一个节点」那一格的账侧：每一条演完都还回来了，
        // 所以池始终能借出，节点实例数才有机会停在 1（实例数那半真机验）。
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 1 });

        for (var i = 0; i < 20; i++)
        {
            ledger.Borrow().ShouldBeTrue($"第 {i + 1} 次借得出：上一条已经演完还回来了");
            ledger.Return();
        }

        ledger.Played.ShouldBe(20);
        ledger.BorrowFailed.ShouldBe(0, "容量 1 就够：连演是借了又还，不是同时占着 20 条");
        ledger.Returned.ShouldBe(20, "每一条演完都还回来了");
        ledger.Alive.ShouldBe(0, "没有一条还悬在外面");
        ledger.InPool.ShouldBe(ledger.Capacity, "池回到满的那一份");
    }

    [Fact]
    public void 归还后再借_在池数归位_账三行仍然对得上()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 4 });

        ledger.Borrow();
        ledger.Borrow();
        ledger.Borrow();
        ledger.InPool.ShouldBe(1, "借走 3 条");

        ledger.Return();
        ledger.Return();
        ledger.Return();
        ledger.InPool.ShouldBe(4, "还完 3 条就回到满的");
        ledger.Borrow().ShouldBeTrue("归位之后借得出");

        ledger.Played.ShouldBe(4);
        ledger.Returned.ShouldBe(3);
        ledger.BorrowFailed.ShouldBe(0);
        ledger.Alive.ShouldBe(1, "恒等式：演 4 == 存活 1 + 已归还 3");
    }
}