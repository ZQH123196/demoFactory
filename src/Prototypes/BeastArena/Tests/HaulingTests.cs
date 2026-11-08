#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 三级优先搬运队列与同级 FIFO、镇政府魔法电梯的 4 个名额与人先货后、
/// 上帝之手抓放（含在手物退回原位并重新入队）。
/// </summary>
public class HaulingTests
{
    private const int Seed = 7;

    private static HaulOrder Order(HaulLevel level, string itemId, double x, double z, string target, long tick)
        => new(level, itemId, new DemoFactory.GugaFramework.Pathfinding.NavPoint(x, z), target, 1.0, tick);

    [Fact]
    public void 三级优先级_取单先看级别再看入队序()
    {
        var queue = new HaulQueue();

        // 故意乱序入队：地面散货最早，尸体最晚。
        queue.Enqueue(Order(HaulLevel.GroundCargoToWarehouse, "cargo1", 1.0, 1.0, ArenaWorld.TownHallId, 1));
        queue.Enqueue(Order(HaulLevel.SparePartToRepair, "cargo3", 3.0, 3.0, ArenaWorld.HotelId, 3));
        queue.Enqueue(Order(HaulLevel.CorpseToChurch, "corpse1", 4.0, 4.0, ArenaWorld.ChurchId, 4));

        queue.TakeNext()!.ItemId.ShouldBe("corpse1", "尸体进教堂是最高那一档。");
        queue.TakeNext()!.ItemId.ShouldBe("cargo3", "备件修房第二。");
        queue.TakeNext()!.ItemId.ShouldBe("cargo1", "地面散货入库最低。");
        queue.TakeNext().ShouldBeNull("取空了。");
    }

    [Fact]
    public void 同级按FIFO_重新入队回到本级队尾()
    {
        var queue = new HaulQueue();
        queue.Enqueue(Order(HaulLevel.GroundCargoToWarehouse, "f1", 1.0, 1.0, ArenaWorld.TownHallId, 1));
        queue.Enqueue(Order(HaulLevel.GroundCargoToWarehouse, "f2", 2.0, 2.0, ArenaWorld.TownHallId, 2));
        queue.Enqueue(Order(HaulLevel.GroundCargoToWarehouse, "f3", 3.0, 3.0, ArenaWorld.TownHallId, 3));

        var first = queue.TakeNext()!;
        first.ItemId.ShouldBe("f1", "同级从队头取。");

        // 在手被抓：退回原位并重新入队 —— 回到队尾，不是队头。
        queue.Requeue(first);
        queue.Count.ShouldBe(3);

        queue.TakeNext()!.ItemId.ShouldBe("f2", "重新入队的那一纸排在队尾。");
        queue.TakeNext()!.ItemId.ShouldBe("f3");
        queue.TakeNext()!.ItemId.ShouldBe("f1", "最后才轮到退回重排的那一纸。");
    }

    [Fact]
    public void 电梯每十个游戏分钟结算四个名额()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        MagicElevator.IsSettlementTick(100).ShouldBeTrue("10 个游戏分钟 = 100 步。");
        MagicElevator.IsSettlementTick(99).ShouldBeFalse();
        MagicElevator.IsSettlementTick(0).ShouldBeFalse("开局那一步不结算。");
        MagicElevator.IsSettlementTick(200).ShouldBeTrue();

        world.Elevator.EnqueueShaft("w1");
        world.Elevator.EnqueueShaft("w2");
        world.Advance(100);

        world.Elevator.Settlements.ShouldBe(1, "100 步整那一格结算一次。");
        world.Elevator.LastSettlement.Count.ShouldBe(2, "井口就两个人，两个名额。");
        world.Elevator.LastSettlement.ShouldAllBe(s => s.Kind == "person");

        world.Advance(100);
        world.Elevator.Settlements.ShouldBe(2, "再一个 100 步结算一次。");
    }

    [Fact]
    public void 电梯人先货后_人满四名就不再运料()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 先挖开一格并派满矿工，让地下有货可运（挖开 + 一个整游戏小时产矿）。
        world.Soil.BeginDigging(0, 1, ArenaTuning.DigWorkerThreshold, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue("挖满 1 游戏小时即挖开。");
        world.Soil.AssignMiners(0, 1, 3);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.OreAccumulatedAt(0, 1).ShouldBeGreaterThanOrEqualTo(1.0, "三个人各每 2 游戏小时产 1。");

        var oreAfterMining = world.Town.Ore;

        // 井口排 6 个人。下一轮结算的 4 个名额全给了人。
        foreach (var id in new[] { "w1", "w2", "w3", "w4", "w5", "w6" })
        {
            world.Elevator.EnqueueShaft(id);
        }

        world.Elevator.Settle(world.Soil, world.DeliverToWarehouse, world.BoardShaft);

        world.Elevator.LastSettlement.Count.ShouldBe(4, "每轮就 4 个名额。");
        world.Elevator.LastSettlement.ShouldAllBe(s => s.Kind == "person", "人先货后：4 个名额全给了上下井的人。");
        world.Town.Ore.ShouldBe(oreAfterMining, 1e-9, "人占满了，这一轮一克矿都不运。");
        world.Elevator.ShaftQueueLength.ShouldBe(2, "剩下两个还在井口排着。");
    }

    [Fact]
    public void 电梯把矿石直送镇政府仓库_不生成地面二次搬运()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Soil.BeginDigging(1, 1, ArenaTuning.DigWorkerThreshold, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(1, 1).ShouldBeTrue();
        world.Soil.AssignMiners(1, 1, 3);
        world.Advance(ArenaTime.StepsPerGameHour);

        world.Soil.OreAccumulatedAt(1, 1).ShouldBeGreaterThanOrEqualTo(1.0, "地下有货。");
        world.Town.Ore.ShouldBe(11.0, 1e-9, "电梯那一轮把 1 矿石直接送进镇政府仓库。");
        world.Elevator.Settlements.ShouldBeGreaterThan(0, "结算过轮。");
        world.Haul.CountAt(HaulLevel.GroundCargoToWarehouse)
            .ShouldBe(0, "电梯直通仓库，不生成地面二次搬运任务。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 上帝之手抓放_尸体进教堂消掉那一圈负面情绪()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var corpse = world.Items.Add(PointItemKind.Corpse, new(-2.0, 2.0), "w1");
        world.Items.LiveCorpseCount.ShouldBe(1, "场上有一具没搬走的尸体。");

        // 情绪圈：以尸体为心、半径 2.0 米，圈内每人每游戏小时 −20。
        var inside = world.DeckStepper.PositionOf("w1");
        var moodDelta = MoodAtCorpse(world, inside);
        moodDelta.ShouldBe(0.0, "w1 在 (−2.5,2.5)，离那具尸体 1 米开外，不在圈里。");

        var victim = world.Roster.Add("probe", UnitRole.Worker,
            new DemoFactory.GugaFramework.Pathfinding.NavPoint(-2.0, 2.0));
        MoodAtCorpse(world, new(victim.DeckX, victim.DeckZ))
            .ShouldBe(ArenaTuning.MoodCorpseAuraPerGameHour, 1e-9, "圈内每小时 −20。");

        // 抓 → 扔进教堂。
        ArenaSetup.Inject(world, "GrabEntity", corpse.Id, out _, out _).ShouldBeTrue();
        ArenaSetup.Inject(world, "DropToBuilding", $"{corpse.Id}|restaurant", out _, out var wrong)
            .ShouldBeFalse("尸体只进教堂。");
        wrong.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive);

        ArenaSetup.Inject(world, "DropToBuilding", $"{corpse.Id}|{ArenaWorld.ChurchId}", out _, out _)
            .ShouldBeTrue();
        world.Items.LiveCorpseCount.ShouldBe(0, "尸体进教堂即消圈。");
        MoodAtCorpse(world, new(victim.DeckX, victim.DeckZ)).ShouldBe(0.0, 1e-9, "圈没了。");
    }

    [Fact]
    public void 上帝之手抓放_备件触发修房矿石入库食物喂兽()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // ① 备件直接触发修房。
        world.DamageBuilding(ArenaWorld.RestaurantId, 30.0);
        var hpBefore = world.Construction.Find(ArenaWorld.RestaurantId)!.Health;
        var part = world.Items.Add(PointItemKind.Cargo, new(1.0, 1.0), "sparepart");
        ArenaSetup.Inject(world, "GrabEntity", part.Id, out _, out _).ShouldBeTrue();
        ArenaSetup.Inject(world, "DropToBuilding", $"{part.Id}|{ArenaWorld.RestaurantId}", out _, out _)
            .ShouldBeTrue();
        world.Town.SparePart.ShouldBe(2.0, 1e-9, "备件扣 1 件。");
        world.Deck.IsRepairing(ArenaWorld.RestaurantId).ShouldBeTrue("修房那一档开起来了。");
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.RepairGameMinutes);
        world.Construction.Find(ArenaWorld.RestaurantId)!.Health
            .ShouldBe(Math.Min(100.0, hpBefore + ArenaTuning.RepairDurability), 1e-9, "修完 +50 耐久，钳到上限。");
        world.Town.IsConserved().ShouldBeTrue();

        // ② 矿石直接进镇政府仓库。
        var ore = world.Items.Add(PointItemKind.Cargo, new(1.0, 1.0), "ore");
        var oreBefore = world.Town.Ore;
        ArenaSetup.Inject(world, "DropToBuilding", $"{ore.Id}|{ArenaWorld.TownHallId}", out _, out _)
            .ShouldBeTrue();
        world.Town.Ore.ShouldBe(oreBefore + 1.0, 1e-9);

        // ③ 食物直接喂巨兽：5 食物换 1 点饱食度。
        var food = world.Items.Add(PointItemKind.Cargo, new(1.0, 1.0), "food");
        world.Voyage.SetSatiation(50.0);
        var foodBefore = world.Town.Food;
        ArenaSetup.Inject(world, "DropToBuilding", $"{food.Id}|{ArenaWorld.GranaryId}", out _, out _)
            .ShouldBeTrue();
        world.Voyage.Satiation.ShouldBe(50.2, 1e-9, "1 食物 ÷ 5 = 0.2 点饱食度。");
        world.Town.Food.ShouldBe(foodBefore - 1.0, 1e-9, "粮仓扣 1 份食物。");
        world.Town.IsConserved().ShouldBeTrue();
    }

    [Fact]
    public void 上帝之手抓在手物_退回原位并重新入队()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var spot = new DemoFactory.GugaFramework.Pathfinding.NavPoint(5.0, 5.0);

        // 先排一单，再推一步让空闲工作人员领走（手上就有东西了）。
        var order = Order(HaulLevel.GroundCargoToWarehouse, "cargo9", 5.0, 5.0, ArenaWorld.TownHallId, 0);
        world.Haul.Enqueue(order);
        world.Advance(1);

        var carrier = world.Roster.Units.First(u => world.CarryingOf(u.Id) is not null);
        world.CarryingOf(carrier.Id)!.ItemId.ShouldBe("cargo9");
        world.Haul.Count.ShouldBe(0, "领走之后队列里没有了。");

        // 上帝之手抓他：岗位解除、在手那一单退回原位并重新入队。
        ArenaSetup.Inject(world, "GrabEntity", carrier.Id, out _, out _).ShouldBeTrue();
        world.CarryingOf(carrier.Id).ShouldBeNull("手上的那一单放下了。");
        world.Haul.Count.ShouldBe(1, "退回原位并重新入队。");
        world.Haul.CountAt(HaulLevel.GroundCargoToWarehouse).ShouldBe(1);

        var requeued = world.Haul.PeekNext()!;
        requeued.ItemId.ShouldBe("cargo9");
        requeued.OriginDeck.ShouldBe(spot, "退回的是原取货点。");
        carrier.Post.ShouldBeEmpty("抓人那一发解除当前岗位。");
    }

    [Fact]
    public void 上帝之手抓不得敌人与游客()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        world.Advance(ArenaTime.StepsPerGameMinute * 5);
        var visitor = world.Roster.Units.FirstOrDefault(u => u.Role == UnitRole.Visitor);
        visitor.ShouldNotBeNull("停靠几个游戏分钟就该有游客上甲板。");

        ArenaSetup.Inject(world, "GrabEntity", visitor!.Id, out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}",
            Case.Sensitive, "游客抓不得。");

        var enemy = world.Roster.Add("probeEnemy", UnitRole.Enemy, new(0.0, 0.0));
        ArenaSetup.Inject(world, "GrabEntity", enemy.Id, out _, out var enemyWhy).ShouldBeFalse();
        enemyWhy.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}",
            Case.Sensitive, "敌人抓不得。");
    }

    [Fact]
    public void 召唤帮工占一格法力_遣散归还()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        world.Town.AvailableMana.ShouldBe(10);
        ArenaSetup.Inject(world, "SummonHelper", "", out _, out _).ShouldBeTrue();
        world.Town.AvailableMana.ShouldBe(9, "占住一格。");
        world.Town.HelperMana.ShouldBe(1);
        world.Roster.HelperCount.ShouldBe(1);
        world.Town.IsConserved().ShouldBeTrue();

        var helper = world.Roster.Units.First(u => u.IsHelper);
        helper.State.ShouldBe(UnitState.Standing);

        // 帮工无薪、无口粮、无尸体：Hp 归零直接消散。
        var soulsBefore = world.Town.SoulStone;
        helper.Hp = 0.0;
        world.Battle.ApplyDamage(world.Tick, helper, 1.0);
        helper.State.ShouldBe(UnitState.Dead);
        world.Items.LiveCorpseCount.ShouldBe(0, "帮工消散无尸。");
        world.Town.SoulStone.ShouldBe(soulsBefore, 1e-9, "帮工消散无魂石。");
        world.Town.IsConserved().ShouldBeTrue();

        ArenaSetup.Inject(world, "DismissHelper", helper.Id, out _, out _).ShouldBeTrue();
        world.Town.AvailableMana.ShouldBe(10, "遣散归还那一格。");
        world.Town.IsConserved().ShouldBeTrue();
    }

    [Fact]
    public void 法力被占满时SummonHelper按InsufficientMana拒()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        for (var i = 0; i < 10; i++)
        {
            world.Town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue();
        }

        ArenaSetup.Inject(world, "SummonHelper", "", out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientMana}", Case.Sensitive);
        world.Roster.HelperCount.ShouldBe(0, "一格也没占上。");
    }

    private static double MoodAtCorpse(
        ArenaWorld world, DemoFactory.GugaFramework.Pathfinding.NavPoint at)
    {
        var delta = 0.0;
        foreach (var corpse in world.Items.LiveCorpses)
        {
            if (at.DistanceTo(corpse.AtDeck) > ArenaTuning.CorpseAuraRadiusMeters) continue;
            delta += ArenaTuning.MoodCorpseAuraPerGameHour;
        }

        return delta;
    }
}
