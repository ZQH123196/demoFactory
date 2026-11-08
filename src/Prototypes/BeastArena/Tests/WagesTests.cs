#nullable enable
using System.Globalization;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 发薪那一摊：日薪三档、应发累计、欠薪扣忠心、停靠期才发得出。
///
/// <para><b>工资表是手动发一次的</b>（spec §「账本面板 · 发薪」）：日结只<b>挂</b>出应发
/// （工人 2 / 角斗士 3 / 自由人 1 金每游戏日），发不发、什么时候发由玩家在停靠期决定。
/// 不发就记欠薪：欠一天扣 5 点忠心，欠薪天数往上堆；发清之后应发与欠薪天数都归零。</para>
///
/// <para><b>钱走金币账那一条路</b>：发薪只从金币账扣应发的那一份，别的一样不动 ——
/// 所以这一组用例每次都再验一遍八条守恒等式。</para>
/// </summary>
public class WagesTests
{
    private const int Seed = 7;

    /// <summary>只剩一个镇民的镇子（其余离镇，不领工钱）。</summary>
    private static ArenaWorld OneOf(UnitRole role)
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        foreach (var u in world.Roster.Units) world.Roster.LeaveTown(u);
        world.Roster.Add("solo", role);
        return world;
    }

    /// <summary>走一个游戏日，看应发多出多少（那一格日结挂上的工钱）。</summary>
    private static double AccruedInOneGameDay(ArenaWorld world)
    {
        var before = world.WagesDue;
        world.Advance(ArenaTime.StepsPerGameDay);
        return world.WagesDue - before;
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 一个游戏日_工人两金_角斗士三金_自由人一金()
    {
        // Arrange：三个只剩一种角色的镇子（其余离镇，不领工钱）。
        var worker = OneOf(UnitRole.Worker);
        var gladiator = OneOf(UnitRole.Gladiator);
        var free = OneOf(UnitRole.FreePerson);

        // Act：各走一个游戏日，看应发多出多少。
        var workerAccrued = AccruedInOneGameDay(worker);
        var gladiatorAccrued = AccruedInOneGameDay(gladiator);
        var freeAccrued = AccruedInOneGameDay(free);

        // Assert：三档日薪。
        workerAccrued.ShouldBe(2.0, 1e-9, "一个工人一个游戏日 2 金。");
        gladiatorAccrued.ShouldBe(3.0, 1e-9, "一个角斗士一个游戏日 3 金。");
        freeAccrued.ShouldBe(1.0, 1e-9, "一个自由人一个游戏日 1 金。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 发薪_只结应发的那一份_结清之后应发与欠薪天数都归零()
    {
        // Arrange：停靠，挂上今天那一份工钱。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        var due = world.WagesDue;
        due.ShouldBeGreaterThan(0.0, "开局就挂着今天那一份工钱。");
        var coinBefore = world.Town.Coin;

        // Act：发一次薪。
        ArenaSetup.Inject(world, "PayWages", string.Empty, out _, out var why).ShouldBeTrue(why);

        // Assert：只扣应发的那一份，应发与欠薪天数都归零。
        world.WagesDue.ShouldBe(0.0, 1e-9, "发清之后应发归零。");
        world.ArrearsDays.ShouldBe(0, "发清之后欠薪天数归零。");
        world.Town.Coin.ShouldBe(coinBefore - due, 1e-9, "只扣应发的那一份钱，一枚不多。");
        world.Town.IsConserved().ShouldBeTrue("发完八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 不发薪_欠一天忠心掉五()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        var unit = world.Roster.Find("w1")!;
        var loyaltyBefore = unit.Loyalty;

        // Act：一个游戏日没发薪。
        world.Advance(ArenaTime.StepsPerGameDay);

        // Assert：欠薪一天，忠心掉 5。
        world.ArrearsDays.ShouldBe(1, "一个游戏日没发薪就是欠薪一天。");
        unit.Loyalty.ShouldBe(loyaltyBefore + ArenaTuning.LoyaltyDailyUnpaid, 1e-9, "欠一天忠心掉 5。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 不发薪_忠心掉到零就不再往下掉()
    {
        // Arrange：忠心只剩 3 点。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var unit = world.Roster.Find("w1")!;
        unit.Loyalty = 3.0;

        // Act：一个游戏日没发薪。
        world.Advance(ArenaTime.StepsPerGameDay);

        // Assert：扣到 0 为止，不掉成负数。
        unit.Loyalty.ShouldBe(0.0, 1e-9, "忠心最低 0，不掉成负数。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 不在停靠期发薪_回InvalidPhase_账一格不动()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.IsDocked.ShouldBeFalse("开局不在停靠期。");
        var coinBefore = world.Town.Coin;
        var dueBefore = world.WagesDue;

        // Act
        var ok = ArenaSetup.Inject(world, "PayWages", string.Empty, out _, out var why);

        // Assert
        ok.ShouldBeFalse("不在停靠期发不出薪。");
        why.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive,
            "停靠期外回 InvalidPhase。");
        world.WagesDue.ShouldBe(dueBefore, 1e-9, "应发一金没动。");
        world.Town.Coin.ShouldBe(coinBefore, "金币一子儿没动。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 快照里有应发_欠薪天数与负债那三格()
    {
        // Arrange：一笔债 + 一个欠薪日。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.AccrueDebt(ArenaWorld.PoorFactionId, 120.0);
        world.Advance(ArenaTime.StepsPerGameDay);

        // Act
        var snapshot = world.DebugSnapshot();

        // Assert
        snapshot.ShouldContain($"wages_due={Number(world.WagesDue)}", Case.Sensitive,
            "快照头部带着应发工钱。");
        snapshot.ShouldContain("arrears_days=1", Case.Sensitive, "快照头部带着欠薪天数。");
        snapshot.ShouldContain("debts=poor_town:120", Case.Sensitive, "快照头部带着按债主分户的负债。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 快照里一笔债都没有_那一格写空()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();

        // Act
        var snapshot = world.DebugSnapshot();

        // Assert：键在、值空（读者一眼看得出「查过这一项、确实没有」）。
        var header = snapshot.Split(" | ")[0];
        header.ShouldEndWith("debts=", Case.Sensitive, "没有债时那一格写空。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 词表里有发薪这一条()
    {
        ArenaSetup.IsVerb("PayWages").ShouldBeTrue("词表里有 PayWages。");
        ArenaSetup.VerbNames.ShouldContain("PayWages", "PayWages 在词表名单里。");
    }

    /// <summary>读数上屏的数（不变文化，两位小数收尾）。</summary>
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
