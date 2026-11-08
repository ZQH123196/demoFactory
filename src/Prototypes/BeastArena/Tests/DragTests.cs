#nullable enable
using System;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 拖拽扩围那一摊 —— <b>尸体与散货也抓得起来、也跟手、也放得下</b>。
///
/// <para><b>这一份盯的是「手上那一件」那一条链</b>：抓起要记下手上那一件
/// （快照头部那一格 <c>held=&lt;类别&gt;:&lt;id&gt;</c> 读它），松手要清空；
/// 尸体拖进教堂入册（不再只是摘掉，那一具才救得回来）；
/// 散货放甲板只改落点，库存一格不动。</para>
///
/// <para><b>跟手那一格（幽灵节点与鼠标世界点偏差 ≤ 0.5 米）要真机才验得</b> ——
/// 那是表现层的事，这一层测不到；那一格留给真机验收那一轮。</para>
/// </summary>
public class DragTests
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

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 抓起尸体那一发_手上那一件记在尸体上_快照写出手上有尸体()
    {
        // Arrange：甲板上一具尸体（载荷记着是谁倒下的）。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var corpse = world.Items.Add(PointItemKind.Corpse, new NavPoint(-2.0, 2.0), "w1");

        // Act
        ArenaSetup.Inject(world, "GrabEntity", corpse.Id, out _, out var why)
            .ShouldBeTrue($"尸体抓得起来：{why}");

        // Assert：手上那一件记在本体上，快照那一格写得出类别与 id。
        corpse.Held.ShouldBeTrue("抓起那一发把手上的状态记在这一件上。");
        ValueOf(world.DebugSnapshot(), "held").ShouldBe($"corpse:{corpse.Id}",
            "快照头部那一格写着手上正拿着哪一件。");
        world.FindChurchCorpse(corpse.Id).ShouldBeNull("还没进教堂，不在册。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 尸体拖进教堂_入册且救得回来_放别处与放甲板都拒()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        var corpse = world.Items.Add(PointItemKind.Corpse, new NavPoint(-2.0, 2.0), "w1");
        ArenaSetup.Inject(world, "GrabEntity", corpse.Id, out _, out _).ShouldBeTrue();

        // Act ①：拖进饭店 —— 尸体只认教堂。
        var wrong = ArenaSetup.Inject(world, "DropToBuilding",
            $"{corpse.Id}|{ArenaWorld.RestaurantId}", out _, out var whyWrong);
        wrong.ShouldBeFalse("尸体只进教堂。");
        whyWrong.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive,
            "按「不是有效目标」那一档拒。");

        // Act ②：拖到甲板上 —— 那一档不接尸体。
        var onDeck = ArenaSetup.Inject(world, "DropToDeck", $"{corpse.Id}|4.00|4.00", out _, out var whyDeck);
        onDeck.ShouldBeFalse("尸体不放甲板。");
        whyDeck.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive,
            "按「不是有效目标」那一档拒。");

        // Act ③：拖进教堂 —— 入册。
        ArenaSetup.Inject(world, "DropToBuilding", $"{corpse.Id}|{ArenaWorld.ChurchId}", out _, out var why)
            .ShouldBeTrue($"进教堂这一发走得通：{why}");

        // Assert：入册那一本记着是谁倒下的；场上不再有这具尸体；手上那一件清空。
        var registered = world.FindChurchCorpse(corpse.Id);
        registered.ShouldNotBeNull("进教堂即入册。");
        registered!.Name.ShouldBe("w1", "在册那一条记着是谁倒下的。");
        world.Items.LiveCorpseCount.ShouldBe(0, "场上不再有这具尸体，负面情绪那一圈随之消掉。");
        ValueOf(world.DebugSnapshot(), "held").ShouldBe(string.Empty,
            "松手之后手上那一件清空。");

        // 救回来：入册那一具走复活那一发。
        ArenaSetup.Inject(world, "ReviveCorpse", corpse.Id, out _, out var whyRevive)
            .ShouldBeTrue($"入册那一具救得回来：{whyRevive}");
        world.FindChurchCorpse(corpse.Id).ShouldBeNull("救回来的那一具从在册表里摘掉。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 尸体放回原处_那一发走得通_不弹拒绝_落点一格不动()
    {
        // Arrange：甲板上一具尸体，抓起来。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var corpse = world.Items.Add(PointItemKind.Corpse, new NavPoint(-2.0, 2.0), "w1");
        ArenaSetup.Inject(world, "GrabEntity", corpse.Id, out _, out _).ShouldBeTrue();

        // Act：取消那一发（拖到界面控件上 / 模态挡住 / 甲板外）发的是原位。
        var putBack = ArenaSetup.Inject(world, "DropToDeck", $"{corpse.Id}|-2.00|2.00", out _, out var why);
        putBack.ShouldBeTrue($"放回原处走得通 —— 取消不该被当成一次失败操作：{why}");

        // Assert：落点没动，手上那一件清空，场上还留着这一具。
        corpse.DeckX.ShouldBe(-2.0, 1e-9, "放回原处不挪落点。");
        corpse.DeckZ.ShouldBe(2.0, 1e-9, "放回原处不挪落点。");
        ValueOf(world.DebugSnapshot(), "held").ShouldBe(string.Empty, "松手之后手上那一件清空。");
        world.Items.LiveCorpseCount.ShouldBe(1, "这一具还在甲板上 —— 没进教堂就不该入册。");
        world.FindChurchCorpse(corpse.Id).ShouldBeNull("放回原处不入册。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 散货抓起放甲板_只改落点_库存与在册一格不动()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        var cargo = world.Items.Add(PointItemKind.Cargo, new NavPoint(1.0, 1.0), "ore");
        var oreBefore = world.Town.Ore;
        ArenaSetup.Inject(world, "GrabEntity", cargo.Id, out _, out _).ShouldBeTrue();
        ValueOf(world.DebugSnapshot(), "held").ShouldBe($"cargo:{cargo.Id}",
            "抓起散货那一件也记在快照上。");

        // Act
        ArenaSetup.Inject(world, "DropToDeck", $"{cargo.Id}|6.00|8.00", out _, out var why)
            .ShouldBeTrue($"散货放甲板走得通：{why}");

        // Assert：落点改了，库存与在册状态一格没动。
        cargo.DeckX.ShouldBe(6.0, 1e-9, "落点那一格改了。");
        cargo.DeckZ.ShouldBe(8.0, 1e-9, "落点那一格改了。");
        world.Town.Ore.ShouldBe(oreBefore, 1e-9, "放甲板不改库存。");
        world.Items.Find(cargo.Id).ShouldNotBeNull("这一件还在甲板上。");
        ValueOf(world.DebugSnapshot(), "held").ShouldBe(string.Empty,
            "松手之后手上那一件清空。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 矿石拖进镇政府_入库那一格()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        var cargo = world.Items.Add(PointItemKind.Cargo, new NavPoint(1.0, 1.0), "ore");
        var oreBefore = world.Town.Ore;
        ArenaSetup.Inject(world, "GrabEntity", cargo.Id, out _, out _).ShouldBeTrue();

        // Act
        ArenaSetup.Inject(world, "DropToBuilding", $"{cargo.Id}|{ArenaWorld.TownHallId}", out _, out var why)
            .ShouldBeTrue($"矿石进镇政府走得通：{why}");

        // Assert：仓库进 1 矿石，这一件离场。
        world.Town.Ore.ShouldBe(oreBefore + 1.0, 1e-9, "仓库进 1 矿石。");
        world.Items.Find(cargo.Id).ShouldBeNull("投进镇政府之后这一件离场。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 食物投饭店_喂巨兽_粮仓扣一份()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(50.0);
        var foodBefore = world.Town.Food;
        var food = world.Items.Add(PointItemKind.Cargo, new NavPoint(1.0, 1.0), "food");
        ArenaSetup.Inject(world, "GrabEntity", food.Id, out _, out _).ShouldBeTrue();

        // Act
        ArenaSetup.Inject(world, "DropToBuilding", $"{food.Id}|{ArenaWorld.RestaurantId}", out _, out var why)
            .ShouldBeTrue($"食物投饭店走得通：{why}");

        // Assert：5 食物换 1 点饱食度，粮仓扣 1 份。
        world.Voyage.Satiation.ShouldBe(50.2, 1e-9, "1 食物 ÷ 5 = 0.2 点饱食度。");
        world.Town.Food.ShouldBe(foodBefore - 1.0, 1e-9, "粮仓扣 1 份食物。");
        world.Items.Find(food.Id).ShouldBeNull("喂掉了这一件。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 取消那一发_回原处_落点一格不动()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();
        var cargo = world.Items.Add(PointItemKind.Cargo, new NavPoint(1.0, 3.0), "ore");
        ArenaSetup.Inject(world, "GrabEntity", cargo.Id, out _, out _).ShouldBeTrue();

        // Act：取消那一发发的是原位（界面控件 / 模态 / 甲板外都走这一条路）。
        ArenaSetup.Inject(world, "DropToDeck", $"{cargo.Id}|1.00|3.00", out _, out var why)
            .ShouldBeTrue($"回原处那一发走得通：{why}");

        // Assert：落点回原处，不挪到别处；手上那一件清空。
        cargo.DeckX.ShouldBe(1.0, 1e-9, "落点回原处。");
        cargo.DeckZ.ShouldBe(3.0, 1e-9, "落点回原处。");
        ValueOf(world.DebugSnapshot(), "held").ShouldBe(string.Empty,
            "松手之后手上那一件清空。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 镇民抓起那一发_快照写出手上有人_松手清空()
    {
        // Arrange
        var world = ArenaWorldBuilder.New(Seed).Build();

        // Act
        ArenaSetup.Inject(world, "GrabEntity", "w1", out _, out var why)
            .ShouldBeTrue($"镇民抓得起来：{why}");

        // Assert
        ValueOf(world.DebugSnapshot(), "held").ShouldBe("person:w1",
            "快照头部那一格写着手上是哪一个人。");

        ArenaSetup.Inject(world, "DropToDeck", "w1|2.00|2.00", out _, out _).ShouldBeTrue();
        ValueOf(world.DebugSnapshot(), "held").ShouldBe(string.Empty,
            "松手之后手上那一件清空。");
    }
}
