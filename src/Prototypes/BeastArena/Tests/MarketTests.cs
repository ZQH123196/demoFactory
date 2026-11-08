#nullable enable
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 市场那一摊：空艇成交（买食物 / 卖矿石 / 卖材料）、补给采购，以及两个买食物入口的价格。
///
/// <para><b>这一份钉的是「空艇买食物是真买」</b>：早先那一版把 <c>buyFood</c> 读成
/// 「从镇上卖出这么多食物」—— 玩家的食物与金币一起被扣走，换来一件东西都没有。
/// 修正后的口径是「向空艇买这么多食物」：付金币，食物进镇仓与巨兽粮仓。</para>
///
/// <para><b>两个价格入口各钉一格</b>：空艇价（三城均价 × 空艇加价）与本地价
/// （所在城物价系数）。面板上写的价与成交那一发扣的价<b>必须是同一个数</b>，
/// 所以这里断言的是「面板那个数」与「账上扣的那个数」相等，而不是各自算一遍。</para>
///
/// <para><b>不够就一子儿不动</b>：钱不够那一格钉的是金币、食物、粮仓三样逐项相等
/// —— 拒绝那一发不许留下任何半个副作用。</para>
/// </summary>
public class MarketTests
{
    private const int Seed = 7;

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 空艇买食物_钱够则金币减食物加粮仓同步()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.InAirshipWindow.ShouldBeTrue("停稳那一刻开着空艇成交窗口。");

        var coinBefore = world.Town.Coin;
        var foodBefore = world.Town.Food;
        var granaryBefore = world.Voyage.GranaryFood;

        // Act：向空艇买十份食物。
        ArenaSetup.Inject(world, "TradeAirship", "10|0|0", out _, out var why).ShouldBeTrue(why);

        // Assert：钱扣了，食物到货了，镇仓与粮仓同步。
        world.Town.Coin.ShouldBe(coinBefore - (10 * ArenaSetup.AirshipFoodPrice), 1e-9,
            "买十份食物按空艇价扣钱。");
        world.Town.Food.ShouldBe(foodBefore + 10.0, 1e-9,
            "买来的食物进镇仓（不再是从镇仓扣走）。");
        world.Voyage.GranaryFood.ShouldBe(granaryBefore + 10.0, 1e-9,
            "粮仓同步加上同一份。");
        world.Town.IsConserved().ShouldBeTrue("买完八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 空艇买食物_钱不够一子儿不动_回InsufficientCoin()
    {
        // Arrange：把钱花到只剩十金 —— 一份都买不起。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Town.TrySpendCoin(world.Town.Coin - 10.0).ShouldBeTrue("把钱花到只剩十金。");

        var coinBefore = world.Town.Coin;
        var foodBefore = world.Town.Food;
        var granaryBefore = world.Voyage.GranaryFood;

        // Act
        var ok = ArenaSetup.Inject(world, "TradeAirship", "1000|0|0", out _, out var why);

        // Assert：金币、食物、粮仓逐项相等 —— 拒绝那一发不留半个副作用。
        ok.ShouldBeFalse("钱不够，这一发发不出去。");
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientCoin}", Case.Sensitive,
            "钱不够回 InsufficientCoin。");
        world.Town.Coin.ShouldBe(coinBefore, "金币一子儿没动。");
        world.Town.Food.ShouldBe(foodBefore, "食物一子儿没动。");
        world.Voyage.GranaryFood.ShouldBe(granaryBefore, "粮仓一子儿没动。");
        world.Town.IsConserved().ShouldBeTrue("被拒之后八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 空艇卖矿与卖材料_照旧折成金币进账()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Town.ProduceOre(5.0);
        world.Town.ProduceSpecialMaterial(3.0);

        var coinBefore = world.Town.Coin;
        var oreBefore = world.Town.Ore;
        var materialBefore = world.Town.SpecialMaterial;
        var foodBefore = world.Town.Food;

        // Act：卖两件矿、一件材料（不买食物）。
        ArenaSetup.Inject(world, "TradeAirship", "0|2|1", out _, out var why).ShouldBeTrue(why);

        // Assert
        world.Town.Ore.ShouldBe(oreBefore - 2.0, 1e-9, "卖掉的矿从镇仓扣走。");
        world.Town.SpecialMaterial.ShouldBe(materialBefore - 1.0, 1e-9, "卖掉的材料从镇仓扣走。");
        world.Town.Coin.ShouldBe(coinBefore + ((2 * ArenaTuning.OreValue) + (1 * ArenaTuning.MaterialValue)), 1e-9,
            "矿石按一件 20 金、材料按一件 100 金折成金币进账。");
        world.Town.Food.ShouldBe(foodBefore, 1e-9, "不买食物就不动食物。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 空艇价是三城均价乘一点二_十一金六一份()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        // Assert：三个城的物价系数（野外锚地不是城，不进均价）。
        BigMapGrid.PoorCity.PriceFactor.ShouldBe(0.8, 1e-9, "贫城物价系数 0.8。");
        BigMapGrid.RichCity.PriceFactor.ShouldBe(1.2, 1e-9, "富城物价系数 1.2。");
        BigMapGrid.DesertCity.PriceFactor.ShouldBe(0.9, 1e-9, "沙城物价系数 0.9。");
        ArenaSetup.AirshipFoodPrice.ShouldBe(11.6, 1e-9,
            "空艇价 = (0.8 + 1.2 + 0.9) ÷ 3 × 1.2 × 基准 10 金 = 11.6 金一份。");

        // 面板上写的那个数，就是成交那一发扣的钱。
        var coinBefore = world.Town.Coin;
        ArenaSetup.Inject(world, "TradeAirship", "1|0|0", out _, out var why).ShouldBeTrue(why);
        world.Town.Coin.ShouldBe(coinBefore - ArenaSetup.AirshipFoodPrice, 1e-9,
            "买一份扣的钱就是面板上写的那个空艇价。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 本地价是所在城价_面板上写的与采购扣的是同一个数()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.CityPriceFactor.ShouldBe(0.8, 1e-9, "停在贫城上，本地价按贫城物价系数。");
        ArenaSetup.FoodPriceOf(world.CityPriceFactor).ShouldBe(8.0, 1e-9,
            "本地价 = 基准 10 金 × 所在城物价系数 0.8 = 8 金一份。");

        var coinBefore = world.Town.Coin;
        var foodBefore = world.Town.Food;

        // Act：在城里采购十份食物。
        ArenaSetup.Inject(world, "BuySupply", "10", out _, out var why).ShouldBeTrue(why);

        // Assert：面板上写的那个本地价，就是采购扣的钱。
        world.Town.Coin.ShouldBe(coinBefore - (10 * ArenaSetup.FoodPriceOf(world.CityPriceFactor)), 1e-9,
            "采购十份扣的钱就是面板上写的那个本地价。");
        world.Town.Food.ShouldBe(foodBefore + 10.0, 1e-9, "采购来的食物进镇仓。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 补给采购_不在停靠期回InvalidPhase()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.IsDocked.ShouldBeFalse("开局不在停靠期。");
        var coinBefore = world.Town.Coin;
        var foodBefore = world.Town.Food;

        // Act
        var ok = ArenaSetup.Inject(world, "BuySupply", "10", out _, out var why);

        // Assert
        ok.ShouldBeFalse("不在停靠期买不到食物。");
        why.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive,
            "停靠期外回 InvalidPhase。");
        world.Town.Coin.ShouldBe(coinBefore, "金币一子儿没动。");
        world.Town.Food.ShouldBe(foodBefore, "食物一子儿没动。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 快照里那一格是空艇窗口剩余的游戏分钟()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        ArenaSetup.AirshipMinutesLeft(world).ShouldBe(ArenaTuning.AirshipWindowGameMinutes,
            "停稳那一刻窗口是满的（30 个游戏分钟）。");

        // Act
        var snapshot = world.DebugSnapshot();

        // Assert
        snapshot.ShouldContain($"airship={ArenaTuning.AirshipWindowGameMinutes}", Case.Sensitive,
            "快照头部带着空艇窗口剩余的游戏分钟。");

        // 窗口走完 → 那一格归零（面板上那一行改写「本段没有空艇」）。
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.AirshipWindowGameMinutes);
        ArenaSetup.AirshipMinutesLeft(world).ShouldBe(0, "窗口过了就没有空艇了。");
        world.DebugSnapshot().ShouldContain("airship=0", Case.Sensitive, "快照那一格跟着归零。");
    }
}
