#nullable enable
using System.Collections.Generic;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 巨兽成长那一件：位阶、形态、两个解锁计数与进阶仪式。
///
/// <para><b>这一份钉的是「能脱离世界单独活」</b>：不造世界，直接 <see cref="BeastGrowth"/>
/// 构造、喂计数、喂 tick、接事件。世界那一侧的接线由既有的
/// <c>BeastTierTests</c> / <c>BeastFormTests</c> 继续钉着，两处不重复。</para>
/// </summary>
public class BeastGrowthTests
{
    [Fact]
    public void 单独构造_开局一阶原始态_两个计数与两把锁都空()
    {
        var growth = new BeastGrowth();

        growth.Tier.ShouldBe(BeastTier.Tier1, "开局一阶。");
        growth.Form.ShouldBe(BeastForm.Original, "开局原始态。");
        growth.DwellGameMinutes.ShouldBe(0, "还没在沙漠带里待过。");
        growth.FedTotalFood.ShouldBe(0.0, 1e-9, "还没喂过。");
        growth.RitualUntilTick.ShouldBe(0, "没有仪式。");
        growth.InRitualAt(0).ShouldBeFalse("没有仪式就不在仪式里。");

        growth.FormUnlocked(BeastForm.Original).ShouldBeTrue("原始态开局即有。");
        growth.FormUnlocked(BeastForm.Desert).ShouldBeFalse("荒漠态还没解锁。");
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeFalse("暴食态还没解锁。");
    }

    [Fact]
    public void 沙漠累计计时喂满阈值解锁荒漠态_离开不清零且解锁不回收()
    {
        var growth = new BeastGrowth();

        // 差一分钟：不解锁。
        growth.SetDwellGameMinutes(ArenaTuning.DesertFormDwellGameMinutes - 1);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Desert).ShouldBeFalse("差一分钟不解锁。");

        // 在沙漠带里再待一分钟：越过阈值那一刻上锁。
        growth.AdvanceDwell(inDesertBand: true);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Desert).ShouldBeTrue("累计计时到阈值即解锁。");

        // 踏出沙漠带：累计不清零，已解锁也不回收。
        growth.AdvanceDwell(inDesertBand: false);
        growth.DwellGameMinutes.ShouldBe(ArenaTuning.DesertFormDwellGameMinutes,
            "离开沙漠不清零（累计口径）。");
        growth.FormUnlocked(BeastForm.Desert).ShouldBeTrue("解锁不随计数变化而回收。");
    }

    [Fact]
    public void 暴食态要累计四百且已驶离贫城_两道门缺一不可()
    {
        var departed = false;
        var growth = new BeastGrowth(() => departed);

        growth.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeFalse("还没驶离贫城。");

        departed = true;
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("两道门都过了。");
    }

    [Fact]
    public void 累计喂食到阈值解锁暴食态_按实际吃进量累加()
    {
        var growth = new BeastGrowth();

        growth.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood - 1.0);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeFalse("差一份不解锁。");

        growth.AddFedTotalFood(1.0);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("累计喂食到阈值即解锁。");
        growth.FedTotalFood.ShouldBe(ArenaTuning.GluttonFormFedTotalFood, 1e-9, "累计值按喂进来的量走。");
    }

    [Fact]
    public void 未解锁切形态回Locked_解锁后真变才发事件_幂等不发()
    {
        var growth = new BeastGrowth();
        var fired = new List<BeastForm>();
        growth.FormSwitched += form => fired.Add(form);

        // ① 没解锁：拒绝，形态不动，不发事件。
        growth.SwitchForm(BeastForm.Desert).ShouldBe(FormSwitchOutcome.Locked, "没解锁的形态要拒。");
        growth.Form.ShouldBe(BeastForm.Original, "被拒那一发形态不动。");
        fired.ShouldBeEmpty("被拒不发事件。");

        // ② 解锁之后切过去：真变一次，发一次。
        growth.SetDwellGameMinutes(ArenaTuning.DesertFormDwellGameMinutes);
        growth.CheckUnlocks();
        growth.SwitchForm(BeastForm.Desert).ShouldBe(FormSwitchOutcome.Switched, "解锁之后切得动。");
        growth.Form.ShouldBe(BeastForm.Desert);
        fired.ShouldBe([BeastForm.Desert], "形态真变发一次。");

        // ③ 幂等：成功但形态没变，不再发。
        growth.SwitchForm(BeastForm.Desert).ShouldBe(FormSwitchOutcome.Unchanged, "切到当前形态是幂等成功。");
        fired.Count.ShouldBe(1, "幂等切换不发事件。");

        // ④ 原始态开局即有：切回去再发一次。
        growth.SwitchForm(BeastForm.Original).ShouldBe(FormSwitchOutcome.Switched);
        fired.ShouldBe([BeastForm.Desert, BeastForm.Original], "形态再变一次再发一次。");
    }

    [Fact]
    public void 仪式没到点不进阶_到点进位阶一次_事件只发一次()
    {
        var growth = new BeastGrowth();
        var fired = new List<BeastTier>();
        growth.TierAdvanced += tier => fired.Add(tier);

        growth.BeginRitual(tick: 100, durationTicks: ArenaTime.StepsPerGameHour);

        growth.InRitualAt(100).ShouldBeTrue("刚开始就在仪式里。");
        growth.InRitualAt(699).ShouldBeTrue("到点前一刻还在仪式里。");
        growth.SettleRitual(699).ShouldBeFalse("没到点不进阶。");
        growth.Tier.ShouldBe(BeastTier.Tier1);
        fired.ShouldBeEmpty("没到点不发事件。");

        growth.SettleRitual(700).ShouldBeTrue("到点进阶。");
        growth.Tier.ShouldBe(BeastTier.Tier2);
        growth.InRitualAt(700).ShouldBeFalse("到点即出仪式。");
        growth.RitualUntilTick.ShouldBe(0, "仪式读数清零。");
        fired.ShouldBe([BeastTier.Tier2], "一次进阶只发一次。");

        growth.SettleRitual(701).ShouldBeFalse("没有仪式时不再进阶。");
        fired.Count.ShouldBe(1);
    }

    [Fact]
    public void 两个计数的直写口不收负数()
    {
        var growth = new BeastGrowth();

        Should.Throw<System.ArgumentOutOfRangeException>(() => growth.SetDwellGameMinutes(-1));
        Should.Throw<System.ArgumentOutOfRangeException>(() => growth.AddFedTotalFood(-0.5));
    }
}
