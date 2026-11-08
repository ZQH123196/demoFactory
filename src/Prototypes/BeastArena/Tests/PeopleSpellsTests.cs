#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 人事与法术那一摊：给自由、卖明星、治疗、帮工召唤与遣散、拔起与落位、传送、教堂救人。
///
/// <para><b>这一份盯的是「在册尸体」那一档</b>（ticket 07 本 ticket负责的那件）：
/// 教堂在册表是那一栏真值，入册记步号，超过 <see cref="ArenaTuning.ReviveWindowGameHours"/>
/// 个游戏小时就过期；过期的那一具救不回来，账一格不动。</para>
///
/// <para><b>拔起占法力、放回原处那两档</b>另有钉住的地方（<c>VerbTests</c> 与
/// <c>LogicSubsystemTests</c> 走的是同一条写路径），这一份不重复它们，
/// 只补本 ticket新加的那几件：救人扣魂石、过期不可救、传送帮工被拒、召唤与遣散那一对。</para>
/// </summary>
public class PeopleSpellsTests
{
    private const int Seed = 7;

    /// <summary>头部段里 <c>key=</c> 那一格的值（键在、值为空也读得出空串）。</summary>
    private static string ValueOf(string snapshot, string key)
    {
        var header = snapshot.Split(" | ")[0];
        foreach (var token in header.Split(' '))
        {
            if (token.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return token[(key.Length + 1)..];
            }
        }

        return "这一格不在快照里";
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 教堂救人扣魂石_扣完那具从在册表里摘掉()
    {
        // Arrange：一具在册的尸体，记着是谁倒下；魂石够。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.RegisterChurchCorpse("corpse_a", "w1", world.Tick);
        var soulBefore = world.Town.SoulStone;

        // Act
        ArenaSetup.Inject(world, "ReviveCorpse", "corpse_a", out _, out var why).ShouldBeTrue(why);

        // Assert：魂石扣掉那一档，在册表里那一具摘掉。
        world.Town.SoulStone.ShouldBe(soulBefore - ArenaTuning.ReviveSoulStones, 1e-9,
            $"救人扣 {ArenaTuning.ReviveSoulStones} 魂石。");
        world.FindChurchCorpse("corpse_a").ShouldBeNull("救回来的那一具不在在册表里了。");
        world.Town.IsConserved().ShouldBeTrue("扣完魂石，八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 在册超过一天的那具_救不回来_账一格不动()
    {
        // Arrange：在册年龄超过复活窗口（24 游戏小时）的那一具。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var entered = world.Tick
            - (ArenaTuning.ReviveWindowGameHours * ArenaTime.StepsPerGameHour)
            - ArenaTime.StepsPerGameMinute;
        world.RegisterChurchCorpse("corpse_old", "w1", entered);
        var soulBefore = world.Town.SoulStone;

        world.IsChurchCorpseExpired(world.FindChurchCorpse("corpse_old")!).ShouldBeTrue(
            "超过 24 游戏小时那一具算过期。");

        // Act
        var ok = ArenaSetup.Inject(world, "ReviveCorpse", "corpse_old", out _, out var why);

        // Assert：拒、账一格不动、在册表里那一具还在（还能查到过期）。
        ok.ShouldBeFalse("过期的那一具救不回来。");
        why.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive,
            "过期按「不是有效目标」那一档拒。");
        world.Town.SoulStone.ShouldBe(soulBefore, 1e-9, "一格魂石都没扣。");
        world.FindChurchCorpse("corpse_old").ShouldNotBeNull("过期的那一具还挂在在册表上。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 刚好满二十四小时那一具_还算数()
    {
        // Arrange：年龄正好等于窗口（不是超过）。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var entered = world.Tick - (ArenaTuning.ReviveWindowGameHours * ArenaTime.StepsPerGameHour);
        world.RegisterChurchCorpse("corpse_edge", "w1", entered);

        // Act / Assert
        world.IsChurchCorpseExpired(world.FindChurchCorpse("corpse_edge")!).ShouldBeFalse(
            "刚好等于窗口的那一档还算数 —— 判据是严格大于。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 传送只收镇民_帮工传不过去()
    {
        // Arrange：停靠 + 摆一座传送阵 + 召一个帮工。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Deck.PlaceBuilding("TeleportArray", -2, 4, ArenaWorld.TeleportArrayId)
            .Ok.ShouldBeTrue("传送阵在旧锚点上放得下。");

        ArenaSetup.Inject(world, "SummonHelper", string.Empty, out _, out var summonWhy).ShouldBeTrue(summonWhy);
        var helper = world.Roster.Units.Single(u => u.IsHelper);
        var coinBefore = world.Town.Coin;

        // Act
        var ok = ArenaSetup.Inject(world, "Teleport", $"{helper.Id}|0|0", out _, out var why);

        // Assert：拒、帮工没走、钱没扣。
        ok.ShouldBeFalse("帮工不是镇民，传不过去。");
        why.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive,
            "帮工与游客那一档按「不是有效目标」拒。");
        world.Roster.Find(helper.Id)!.LeftTown.ShouldBeFalse("帮工还在镇上。");
        world.Town.Coin.ShouldBe(coinBefore, 1e-9, "一子儿没扣。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 在册表按入册次序记年龄_窗口里只列没过期的()
    {
        // Arrange：一新一旧两具。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.RegisterChurchCorpse("corpse_new", "w1", world.Tick);
        world.RegisterChurchCorpse("corpse_old", "w2",
            world.Tick - (ArenaTuning.ReviveWindowGameHours * ArenaTime.StepsPerGameHour) - ArenaTime.StepsPerGameMinute);

        // Act
        var all = world.ChurchCorpses;
        var inWindow = world.ChurchCorpsesInWindow;

        // Assert
        all.Count.ShouldBe(2, "在册表两具都在。");
        all[0].CorpseId.ShouldBe("corpse_new", "按入册次序。");
        all[0].Name.ShouldBe("w1", "记着是谁倒下。");
        all[0].AgeGameMinutes.ShouldBe(0, "刚入册，年龄 0 个游戏分钟。");
        all[1].AgeGameMinutes.ShouldBe((ArenaTuning.ReviveWindowGameHours * 60) + 1,
            "旧的那一具年龄是窗口 + 1 个游戏分钟。");
        inWindow.Count.ShouldBe(1, "窗口里只列没过期的。");
        inWindow[0].CorpseId.ShouldBe("corpse_new", "窗口里是新入册那一具。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 快照里带着教堂在册那一格_一具都没有就写空()
    {
        // Arrange：空表。
        var world = ArenaWorldBuilder.New(Seed).Build();

        // Act / Assert：键在、值空（读者一眼看得出「查过这一项、确实没有」）。
        ValueOf(world.DebugSnapshot(), "church").ShouldBe(string.Empty, "一具都没有时那一格写空。");

        // Act / Assert：入册一具之后，那一格写得出 id、人与游戏分钟。
        world.RegisterChurchCorpse("corpse_x", "w1", world.Tick);
        ValueOf(world.DebugSnapshot(), "church").ShouldBe("corpse_x:w1:0",
            "在册那一具写得出 id:人:游戏分钟。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 召唤帮工占一格法力_遣散归还那一格()
    {
        // Arrange：停靠期才召得下帮工。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        var manaBefore = world.Town.AvailableMana;

        // Act
        ArenaSetup.Inject(world, "SummonHelper", string.Empty, out _, out var why).ShouldBeTrue(why);
        var helper = world.Roster.Units.Single(u => u.IsHelper);

        // Assert：占住一格法力、帮工在册。
        world.Town.HelperMana.ShouldBe(1, "帮工占住 1 格法力。");
        world.Town.AvailableMana.ShouldBe(manaBefore - 1, "可用法力少一格。");
        world.Roster.HelperCount.ShouldBe(1, "帮工在册。");

        // Act
        ArenaSetup.Inject(world, "DismissHelper", helper.Id, out _, out var dismissWhy).ShouldBeTrue(dismissWhy);

        // Assert：归还那一格、帮工离镇。
        world.Town.HelperMana.ShouldBe(0, "遣散归还那一格。");
        world.Town.AvailableMana.ShouldBe(manaBefore, "可用法力回到原样。");
        world.Roster.Find(helper.Id)!.LeftTown.ShouldBeTrue("帮工离镇了。");
        world.Town.IsConserved().ShouldBeTrue("占与还各一次，法力那一列仍然平。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 遣散一个不是帮工的_遣散不掉()
    {
        // Arrange：街上一个普通镇民，不是帮工。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        var worker = world.Roster.Find("w1")!;
        var manaBefore = world.Town.AvailableMana;

        // Act
        var ok = ArenaSetup.Inject(world, "DismissHelper", worker.Id, out _, out var why);

        // Assert
        ok.ShouldBeFalse("普通镇民遣散不掉。");
        why.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive);
        world.Town.AvailableMana.ShouldBe(manaBefore, "一格法力也没动。");
        world.Roster.Find(worker.Id)!.LeftTown.ShouldBeFalse("镇民还在镇上。");
    }
}
