#nullable enable
using System;
using System.Reflection;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 九条守恒等式 —— <b>L1 不变量那一档的全部内容</b>。
///
/// <para><b>为什么要逐条断</b>：<see cref="TownAccounts.IsConserved"/> 只回一个 bool，
/// 那一格一旦破掉，九条里哪一条破的读不出来。所以本文件把九条各自按 spec 的式子算一遍，
/// 再把「探测器不是恒真的」钉住 —— 恒真的断言与不存在的断言是一回事。</para>
///
/// <para><b>唯一一处不是写路径的口子</b>：故意弄破两格等式用反射直接写
/// <see cref="TownAccounts"/> 的私有字段。这是有意的：探测器要验的就是「账被弄坏时它会不会喊」，
/// 而账本那一侧所有写口都带「一子儿不动」的守卫，公开面上根本造不出一个不平的账。</para>
/// </summary>
public class ConservationTests
{
    private const int Seed = 7;

    [Fact]
    public void 九条守恒等式逐条为真_开局()
    {
        var town = new TownAccounts();

        town.Coin.ShouldBe(ArenaTuning.OpeningCoin + town.ProducedCoin - town.SpentCoin, 1e-9, "① 金币");
        town.Food.ShouldBe(ArenaTuning.OpeningFood + town.ProducedFood - town.SpentFood, 1e-9, "② 食物");
        town.SoulStone.ShouldBe(
            ArenaTuning.StartingSoulStones + town.ProducedSoulStone - town.SpentSoulStone, 1e-9, "③ 魂石");
        town.Ore.ShouldBe(ArenaTuning.OpeningOre + town.ProducedOre - town.SpentOre, 1e-9, "④ 矿石");
        town.SpecialMaterial.ShouldBe(
            ArenaTuning.OpeningSpecialMaterial + town.ProducedSpecialMaterial - town.SpentSpecialMaterial,
            1e-9, "⑤ 特殊材料");
        town.SparePart.ShouldBe(
            ArenaTuning.StartingSpareParts + town.ProducedSparePart - town.SpentSparePart, 1e-9, "⑥ 备件");
        town.Crystal.ShouldBe(town.ProducedCrystal - town.SpentCrystal, 1e-9, "⑦ 魔晶 = 产出 − 消耗");
        town.TotalDebt.ShouldBe(town.AccruedDebt - town.RepaidDebt, 1e-9, "⑧ 负债总额 = 累计欠 − 累计还");
        (town.AvailableMana + town.StasisMana + town.HelperMana
            + town.InstantSpentMana - town.RegainedMana - town.CrystalManaRestored)
            .ShouldBe(TownAccounts.ManaCapacity, "⑨ 法力那一式 = 10");

        town.IsConserved().ShouldBeTrue(town.ConservedReport());
        town.ConservedReport().Split(" | ").Length.ShouldBe(9, "逐条读数一条一段。");
    }

    [Fact]
    public void 九条守恒等式逐条为真_跑过一轮完整停靠()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.Inject(world, "Dock", "poor_city", out _, out _).ShouldBeTrue();
        // 开局在贫城西边 40 米的野地上（40 米 = 1000 步），推够一整段航程再判停稳。
        for (var i = 0; i < 1400 && !world.IsDocked; i++) world.Advance(1);
        world.IsDocked.ShouldBeTrue("停到贫城上。");

        // 停靠 → 消费 → 办赛 → 防守 → 航行：六本账与法力那一本都被动过。
        world.Advance(ArenaTime.StepsPerGameHour * 2);
        ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out _).ShouldBeTrue();
        world.Advance((int)(ArenaTime.StepsPerGameHour * 1.6) + 20);
        ArenaSetup.Inject(world, "PullAnchor", "", out _, out _).ShouldBeTrue();
        world.Advance((int)(ArenaTime.StepsPerGameHour * 2.1));

        var town = world.Town;
        town.IsConserved().ShouldBeTrue(town.ConservedReport());
        town.ProducedCoin.ShouldBeGreaterThan(0.0, "游客消费与门票确实进过账 —— "
            + "全零的账本让守恒等式变成一句空话。");
        town.SpentCoin.ShouldBeGreaterThan(0.0, "防守抢货与建造确实花过钱。");
        town.ProducedSoulStone.ShouldBeGreaterThan(0.0,
            "限时清剿死的敌人自动入账魂石。这一局不到一个游戏日，所以口粮那一格还没到结算点"
            + "（口粮挂在整游戏日上），这里断的是魂石那一列而不是口粮。");
        town.Coin.ShouldBe(ArenaTuning.OpeningCoin + town.ProducedCoin - town.SpentCoin, 1e-9, "① 金币");
        town.Food.ShouldBe(ArenaTuning.OpeningFood + town.ProducedFood - town.SpentFood, 1e-9, "② 食物");
        town.SoulStone.ShouldBe(
            ArenaTuning.StartingSoulStones + town.ProducedSoulStone - town.SpentSoulStone, 1e-9, "③ 魂石");
        town.Ore.ShouldBe(ArenaTuning.OpeningOre + town.ProducedOre - town.SpentOre, 1e-9, "④ 矿石");
        town.SpecialMaterial.ShouldBe(
            ArenaTuning.OpeningSpecialMaterial + town.ProducedSpecialMaterial - town.SpentSpecialMaterial,
            1e-9, "⑤ 特殊材料");
        town.SparePart.ShouldBe(
            ArenaTuning.StartingSpareParts + town.ProducedSparePart - town.SpentSparePart, 1e-9, "⑥ 备件");
        town.Crystal.ShouldBe(town.ProducedCrystal - town.SpentCrystal, 1e-9, "⑦ 魔晶");
        town.TotalDebt.ShouldBe(town.AccruedDebt - town.RepaidDebt, 1e-9, "⑧ 负债");
        (town.AvailableMana + town.StasisMana + town.HelperMana
            + town.InstantSpentMana - town.RegainedMana - town.CrystalManaRestored)
            .ShouldBe(TownAccounts.ManaCapacity, "⑨ 法力");
    }

    [Fact]
    public void 探测器不是恒真的_故意弄破一格矿石等式它就喊()
    {
        var town = new TownAccounts();
        town.IsConserved().ShouldBeTrue("动手之前是平的。");

        Corrupt(town, "Ore", 999.0);

        town.IsConserved().ShouldBeFalse("矿石账被改过，探测器判红。");
        var report = town.ConservedReport();
        report.ShouldContain("ore 999vs", Case.Sensitive, "逐条读数要指出破的是哪一条：\n" + report);
        report.Split(" | ")[3].ShouldStartWith("ore ", Case.Sensitive, "第 4 段就是矿石那一式。");
    }

    [Fact]
    public void 探测器不是恒真的_故意弄破法力那一式它也喊()
    {
        var town = new TownAccounts();
        town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue();
        town.IsConserved().ShouldBeTrue("占住一格之后仍然平。");

        Corrupt(town, "InstantSpentMana", 7);

        town.IsConserved().ShouldBeFalse("即时消耗那一列被改过，法力那一式就不等于 10。");
        town.ConservedReport().ShouldContain("mana 17", Case.Sensitive,
            "逐条读数要指出法力那一式：\n" + town.ConservedReport());
    }

    [Fact]
    public void 付不起的账_七本账一格都不动()
    {
        var town = new TownAccounts();

        town.TrySpendCoin(1.0e9).ShouldBeFalse();
        town.TrySpendFood(1.0e9).ShouldBeFalse();
        town.TrySpendSoulStone(1.0e9).ShouldBeFalse();
        town.TrySpendOre(1.0e9).ShouldBeFalse();
        town.TrySpendSpecialMaterial(1.0e9).ShouldBeFalse();
        town.TrySpendSparePart(1.0e9).ShouldBeFalse();
        town.TrySpendCrystal(1.0e9).ShouldBeFalse();

        town.Coin.ShouldBe(ArenaTuning.OpeningCoin);
        town.Food.ShouldBe(ArenaTuning.OpeningFood);
        town.SpentCoin.ShouldBe(0.0, 1e-9, "付不动的账不记消耗。");
        town.ProducedCoin.ShouldBe(0.0, 1e-9, "付不动的账也不记产出。");
        town.IsConserved().ShouldBeTrue(town.ConservedReport());
    }

    /// <summary>反射直写 <see cref="TownAccounts"/> 的一个私有字段（只在「故意弄破」那两条里用）。</summary>
    private static void Corrupt(TownAccounts town, string property, object value)
    {
        var setter = typeof(TownAccounts).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)
            ?.GetSetMethod(nonPublic: true);
        setter.ShouldNotBeNull($"TownAccounts 上得有 {property} 这一栏。");
        setter!.Invoke(town, [value]);
    }
}
