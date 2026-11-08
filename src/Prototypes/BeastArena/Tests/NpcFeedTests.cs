#nullable enable
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// ticket 28：停靠期由无岗工人或帮工喂 5 食物。判定与扣粮复用
/// <see cref="ArenaWorld.NpcFeedBlocker"/> / <see cref="ArenaWorld.FeedBeast"/>，本轮不新增自动派活队列。
/// </summary>
public class NpcFeedTests
{
    private const int Seed = 7;

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 停靠期无岗工人喂食_两处存粮各扣五份_饱食加一点()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        var foodBefore = world.Town.Food;
        var granaryBefore = world.Voyage.GranaryFood;
        var satiationBefore = world.Voyage.Satiation;

        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out var canonical, out var why)
            .ShouldBeTrue(why);

        canonical.ShouldBe(string.Empty, "无参数动词入录的是空串。");
        world.Town.Food.ShouldBe(foodBefore - 5.0, 1e-9, "镇仓扣 5 份（与手动 Feed|5 同一扣粮口径）。");
        world.Voyage.GranaryFood.ShouldBe(granaryBefore - 5.0, 1e-9, "兽粮扣 5 份。");
        world.Voyage.Satiation.ShouldBe(satiationBefore + 1.0, 1e-9, "5 份食物换 1 点饱食（5:1）。");
        world.Voyage.FedTotalFood.ShouldBe(5.0, 1e-9, "有效喂食累计 +5。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 连续两次喂食_各扣五份_暴食进度按实际量累计()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        var foodBefore = world.Town.Food;
        var granaryBefore = world.Voyage.GranaryFood;
        var satiationBefore = world.Voyage.Satiation;

        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out _, out var why1).ShouldBeTrue(why1);
        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out _, out var why2).ShouldBeTrue(why2);

        world.Town.Food.ShouldBe(foodBefore - 10.0, 1e-9);
        world.Voyage.GranaryFood.ShouldBe(granaryBefore - 10.0, 1e-9);
        world.Voyage.Satiation.ShouldBe(satiationBefore + 2.0, 1e-9);
        world.Voyage.FedTotalFood.ShouldBe(10.0, 1e-9);
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 饱食已满_拒绝且不扣粮不计进度()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        var foodBefore = world.Town.Food;
        var granaryBefore = world.Voyage.GranaryFood;

        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out _, out var why).ShouldBeFalse();
        why.ShouldContain("饱食已满");

        world.Town.Food.ShouldBe(foodBefore, 1e-9, "拒绝时一口都不扣。");
        world.Voyage.GranaryFood.ShouldBe(granaryBefore, 1e-9);
        world.Voyage.FedTotalFood.ShouldBe(0.0, 1e-9);
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 没有无岗工人或帮工_拒绝并给出原因()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        foreach (var unit in world.Roster.Units)
        {
            if (unit.Role is UnitRole.Worker or UnitRole.Helper) unit.Post = "granary";
        }

        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out _, out var why).ShouldBeFalse();
        why.ShouldContain("没有无岗的工人或帮工");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 不在停靠期_拒绝且不扣粮()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var foodBefore = world.Town.Food;

        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out _, out var why).ShouldBeFalse();
        why.ShouldContain("停靠期");

        world.Town.Food.ShouldBe(foodBefore, 1e-9);
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 战斗中_拒绝且不扣粮()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.Battle.Occasion.ShouldNotBe(BattleOccasion.None);
        var foodBefore = world.Town.Food;

        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out _, out var reject).ShouldBeFalse();
        reject.ShouldContain("战斗");

        world.Town.Food.ShouldBe(foodBefore, 1e-9);
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 存粮不够五份_拒绝且不扣粮()
    {
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(0.0).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Town.TrySpendFood(world.Town.Food).ShouldBeTrue("把镇仓食物清掉，制造存粮不足。");
        var foodBefore = world.Town.Food;

        ArenaSetup.Inject(world, "NpcFeed", string.Empty, out _, out var why).ShouldBeFalse();
        why.ShouldContain("不够 5 份");

        world.Town.Food.ShouldBe(foodBefore, 1e-9);
        world.Voyage.FedTotalFood.ShouldBe(0.0, 1e-9);
    }
}
