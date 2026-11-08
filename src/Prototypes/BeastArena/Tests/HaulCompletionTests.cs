#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 搬运单走完两程之后的<b>结单</b>那一格：走到取货点装货、走到目标门口投进去、人回到空闲。
///
/// <para>四个档次各走一遍，投递走玩家 <c>DropToBuilding</c> 那一发的同一份写路径
/// （<c>ArenaSetup.DeliverItem</c>）—— 本文件不另造一套投递口径。
/// 另外钉住三条「不把某一单钉死在一个人手上」的读数：途中阵亡、目标那一栋被拆、
/// 还有交完一单之后立刻能接下一单。</para>
///
/// <para><b>每一档都真推进时间</b>：断言读的是走完两程之后的账，不是构造那一刻的账。</para>
/// </summary>
public class HaulCompletionTests
{
    private const int Seed = 11;

    /// <summary>取货点钉在甲板西南角一格空地上（离开局那批人的落点三米，走得完也读得出「真走了」）。</summary>
    private static readonly NavPoint Pickup = new(-11.5, -8.5);

    /// <summary>第二单的取货点（离教堂门口两米，让「同一个人接下一单」这一格不用跑满一趟）。</summary>
    private static readonly NavPoint PickupNearChurch = new(4.5, -4.5);

    [Fact]
    public void 尸体单走完两程_进教堂消掉那一圈负面情绪_人当场回到空闲()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var corpse = world.Items.Add(PointItemKind.Corpse, Pickup, "w1");

        // 圈内每小时 −20：这一格钉的是「还在场上没搬走时圈在，搬进教堂就消」。
        MoodAtCorpse(world, Pickup).ShouldBe(ArenaTuning.MoodCorpseAuraPerGameHour, 1e-9,
            "没搬走的那具尸体在取货点上，圈还在。");

        world.Haul.Enqueue(Order(world, HaulLevel.CorpseToChurch, corpse, ArenaWorld.ChurchId));

        RunUntilSettled(world, corpse.Id);
        world.Tick.ShouldBeLessThan(ArenaTime.StepsPerGameHour,
            "这一趟跑在第一个整游戏小时之前 —— 那一格上饱食度会每小时 −1，与本用例无关。");

        world.Items.LiveCorpseCount.ShouldBe(0, "尸体搬进教堂即消圈。");
        MoodAtCorpse(world, Pickup).ShouldBe(0.0, 1e-9, "圈没了。");

        var landing = OnlyLandingOf(world, corpse.Id);
        landing.Landed.ShouldBeTrue("投递走通了。");
        landing.TargetBuildingId.ShouldBe(ArenaWorld.ChurchId);
        ShouldBeIdleAndAvailable(world, landing.CarrierId, "交完手就空了。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 备件单走完两程_修房那一档开起来_十个游戏分钟后耐久加五十()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DamageBuilding(ArenaWorld.RestaurantId, 30.0);
        var healthBefore = world.Construction.Find(ArenaWorld.RestaurantId)!.Health;
        var partsBefore = world.Town.SparePart;

        var part = world.Items.Add(PointItemKind.Cargo, Pickup, "sparepart");
        world.Haul.Enqueue(Order(world, HaulLevel.SparePartToRepair, part, ArenaWorld.RestaurantId));

        RunUntilSettled(world, part.Id);

        OnlyLandingOf(world, part.Id).Landed.ShouldBeTrue("备件投进饭店那一档。");
        world.Town.SparePart.ShouldBe(partsBefore - 1.0, 1e-9, "备件扣 1 件。");
        world.Deck.IsRepairing(ArenaWorld.RestaurantId).ShouldBeTrue("修房那一档开起来了。");

        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.RepairGameMinutes);
        world.Construction.Find(ArenaWorld.RestaurantId)!.Health
            .ShouldBe(Math.Min(100.0, healthBefore + ArenaTuning.RepairDurability), 1e-9,
                "修完 +50 耐久，钳到上限。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 地面散货单走完两程_矿石进镇政府仓库()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var oreBefore = world.Town.Ore;

        var cargo = world.Items.Add(PointItemKind.Cargo, Pickup, "ore");
        world.Haul.Enqueue(Order(world, HaulLevel.GroundCargoToWarehouse, cargo, ArenaWorld.TownHallId));

        RunUntilSettled(world, cargo.Id);

        OnlyLandingOf(world, cargo.Id).Landed.ShouldBeTrue("矿石投进镇政府仓库那一档。");
        world.Town.Ore.ShouldBe(oreBefore + 1.0, 1e-9, "仓库进 1 矿石。");
        world.Items.Find(cargo.Id).ShouldBeNull("投进去的那一件离场。");
        ShouldBeIdleAndAvailable(world, OnlyLandingOf(world, cargo.Id).CarrierId, "交完手就空了。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 交完一单_同一个人当场接下一张单()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var first = world.Items.Add(PointItemKind.Cargo, Pickup, "ore");
        var second = world.Items.Add(PointItemKind.Cargo, PickupNearChurch, "material");

        world.Haul.Enqueue(Order(world, HaulLevel.GroundCargoToWarehouse, first, ArenaWorld.TownHallId));
        RunUntilSettled(world, first.Id);

        // 第一单刚落完就排第二张：结单那一格已把人放回空闲，他要能立刻领走。
        world.Haul.Enqueue(Order(world, HaulLevel.GroundCargoToWarehouse, second, ArenaWorld.TownHallId));
        RunUntilSettled(world, second.Id);

        var firstLanding = OnlyLandingOf(world, first.Id);
        var secondLanding = OnlyLandingOf(world, second.Id);
        secondLanding.CarrierId.ShouldBe(firstLanding.CarrierId,
            "不是另一个人领走 —— 交完一单的那个就地接了下一张。");
        secondLanding.Landed.ShouldBeTrue();
        world.Haul.Count.ShouldBe(0, "两张单都结完了。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 搬运人半路阵亡_货退回本级队尾_只交一次()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var oreBefore = world.Town.Ore;
        var cargo = world.Items.Add(PointItemKind.Cargo, Pickup, "ore");
        world.Haul.Enqueue(Order(world, HaulLevel.GroundCargoToWarehouse, cargo, ArenaWorld.TownHallId));

        // 走起来之后才动手：那一格要验的是「在途」那一档，不是「刚领单」。
        for (var i = 0; i < 20; i++) world.Advance(1);
        var carrier = OnlyCarrier(world);
        carrier.State.ShouldBe(UnitState.Standing);

        carrier.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, carrier, 1.0);
        carrier.State.ShouldBe(UnitState.Dead, "Hp 落到 −100 以下当场死。");

        world.CarryingOf(carrier.Id).ShouldBeNull("死者手上没有单了。");
        world.Items.Find(cargo.Id).ShouldNotBeNull("货没丢 —— 那一件还在甲板上。");
        world.Haul.Count.ShouldBe(1, "货还在册就退回本级队尾，换一双手再送。");

        RunUntilSettled(world, cargo.Id);

        LandingsOf(world, cargo.Id).Count.ShouldBe(1, "只交一次 —— 不重交。");
        OnlyLandingOf(world, cargo.Id).CarrierId.ShouldNotBe(carrier.Id, "换了一双手送。");
        world.Town.Ore.ShouldBe(oreBefore + 1.0, 1e-9, "仓库只进 1 矿石。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 目标那一栋被拆_那一纸作废_货仍在甲板上人回到空闲()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var corpse = world.Items.Add(PointItemKind.Corpse, Pickup, "w1");
        world.Haul.Enqueue(Order(world, HaulLevel.CorpseToChurch, corpse, ArenaWorld.ChurchId));

        // 走到第二程（货已在手、正朝教堂门口走）再拆 —— 那一格要验的是「在途目标没了」。
        world.Advance(ArenaTime.StepsPerGameHour / 2);
        var carrier = OnlyCarrier(world);
        world.Items.Find(corpse.Id).ShouldNotBeNull("拆之前货还在甲板上。");

        ArenaSetup.Inject(world, "RemoveBuilding", ArenaWorld.ChurchId, out _, out var why)
            .ShouldBeTrue($"教堂拆得掉：{why}");

        world.Advance(ArenaTime.StepsPerGameMinute);

        var landing = OnlyLandingOf(world, corpse.Id);
        landing.Landed.ShouldBeFalse("目标没了，投不进去。");
        landing.Reason.ShouldContain(ArenaWorld.ChurchId, Case.Sensitive, "原因里写着拆掉的是哪一栋。");
        world.Items.LiveCorpseCount.ShouldBe(1, "没投进去的那一具仍在甲板上 —— 不丢。");
        world.Haul.Count.ShouldBe(0, "那一纸作废，不在队列里转。");
        ShouldBeIdleAndAvailable(world, carrier.Id, "人不被一张送不掉的单钉死。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 塌方那一栏的吓跑人数_按在镇游客数算_不是写死的零()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 先挖开一格并让它回长 —— 回长填满已挖格才引发塌方。
        world.Soil.BeginDigging(0, 1, ArenaTuning.DigWorkerThreshold, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue("1 号象限第 1 层挖开了。");

        // 停靠掐在塌方之前三个游戏小时：**池子靠「换城即回满」回满**，整局早停早开的话
        // 池子在到塌方之前就被人走空了（放客 0.5 个每游戏分钟，每人待 2..4 游戏小时）。
        world.AdvanceTo(ArenaTime.StepsPerGameDay - (ArenaTime.StepsPerGameHour * 3) - 1);
        world.OnDocked(BigMapGrid.PoorCity);
        world.AdvanceTo(ArenaTime.StepsPerGameDay - 1);

        var inTownBefore = world.Visitors.InTown.OrderBy(v => v.Unit.RosterIndex).ToList();
        inTownBefore.Count.ShouldBeGreaterThanOrEqualTo(5,
            "甲板上得先有游客，这一栏才读得出数：" + world.Visitors.DebugText());
        var fled = world.Soil.VisitorsFledBy(inTownBefore.Count);

        world.AdvanceTo(ArenaTime.StepsPerGameDay);

        world.Soil.Collapses.Count.ShouldBe(1, "回长填满已挖格即引发一次塌方。");
        var collapse = world.Soil.Collapses[0];
        collapse.VisitorsFled.ShouldBe(fled,
            "记录里的吓跑人数按塌方那一刻的在镇游客数算（在镇每 5 人取 1 人）。");
        collapse.VisitorsFled.ShouldBeGreaterThan(0, "游客在镇上，就不该报一个写死的 0。");
        inTownBefore.Take(fled).ShouldAllBe(v => v.Unit.LeftTown,
            $"记录里那 {fled} 个就是在世界里真的吓跑的那 {fled} 个（按名单序号从头取）。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- 小工具 ----

    private static HaulOrder Order(
        ArenaWorld world, HaulLevel level, PointItem item, string targetBuildingId)
        => ArenaWorld.Order(level, item.Id, item.AtDeck, targetBuildingId, 1.0, world.Tick);

    /// <summary>真推进时间推到那一件交单为止（预算之内没交单就红，红在带上读数的那一句）。</summary>
    private static void RunUntilSettled(ArenaWorld world, string itemId)
    {
        const int budget = 500;
        for (var i = 0; i < budget; i++)
        {
            world.Advance(1);
            if (LandingsOf(world, itemId).Count > 0) return;
        }

        LandingsOf(world, itemId).Count.ShouldBeGreaterThan(0,
            $"{budget} 步之内没交单：货物仍在册={world.Items.Find(itemId) is not null}，"
            + $"队列={world.Haul.DebugText()}，手上={world.CarryingCount}，{world.HaulLandingText()}");
    }

    private static List<HaulLanding> LandingsOf(ArenaWorld world, string itemId)
        => world.HaulLandings.Where(l => l.ItemId == itemId).ToList();

    private static HaulLanding OnlyLandingOf(ArenaWorld world, string itemId)
    {
        var all = LandingsOf(world, itemId);
        all.Count.ShouldBe(1, "那一件只该交一次单。");
        return all[0];
    }

    /// <summary>场上唯一手上有一单的人（只排一单时就是领走它的那一个）。</summary>
    private static ArenaUnit OnlyCarrier(ArenaWorld world)
    {
        var carriers = world.Roster.Units.Where(u => world.CarryingOf(u.Id) is not null).ToList();
        carriers.Count.ShouldBe(1, "场上只有一个人手上有一单。");
        return carriers[0];
    }

    /// <summary>
    /// 「空闲可用」的那几条 —— 与派单那一格读的是同一组条件
    /// （有单、无岗位、在册、站着、身份是工作人员或帮工）。
    /// </summary>
    private static void ShouldBeIdleAndAvailable(ArenaWorld world, string unitId, string because)
    {
        world.CarryingOf(unitId).ShouldBeNull($"{because}：手上没有单。");
        var u = world.Roster.Find(unitId);
        u.ShouldNotBeNull($"{because}：还在活物册里。");
        u!.Post.Length.ShouldBe(0, $"{because}：没有占着岗位。");
        u.LeftTown.ShouldBeFalse($"{because}：还在镇上。");
        u.CanFight.ShouldBeTrue($"{because}：站着。");
        (u.Role == UnitRole.Worker || u.IsHelper).ShouldBeTrue($"{because}：能领这一类单。");
    }

    /// <summary>尸体负面情绪圈那一式（与世界里那一口同一式：半径 2 米，每小时 −20）。</summary>
    private static double MoodAtCorpse(ArenaWorld world, NavPoint at)
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
