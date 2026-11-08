#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 三级搬运单的四个<b>产地</b>在生产代码里的那一摊 —— <b>本文件一次 <c>Enqueue</c> 都不调</b>。
///
/// <para><b>为什么这份用例与 <c>HaulingTests</c> / <c>HaulCompletionTests</c> 不是同一件事</b>：
/// 那两份自己排单，验的是「派单与交单走得通」；这一份验的是<b>「没人排单时世界自己排不排」</b> ——
/// 真游戏里单子从哪来。四条产地各占一格，外加「仗打完不把在途那一单钉死」那一格，
/// 还有「需求一直在时不积压单子」那一格。</para>
///
/// <para><b>每一格都真推进时间</b>：断言读的是跑完之后的账与结算记录，不是排单那一刻。
/// 九条守恒等式在推进的<b>每一逻辑步</b>都判一次（<see cref="HaulOrdersStepShouldStayConserved"/>）。</para>
/// </summary>
public class HaulOrderSourceTests
{
    private const int Seed = 23;

    // ---- ① 尸体 → 教堂 ----

    [Fact]
    public void 仗里倒下的人_当场排一张搬进教堂的单_尸体最终投进教堂那一圈负面情绪消掉()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        FightToTheEnd(world);

        // 战斗中倒下的敌人当场成尸体（生产代码那一格，不经任何测试排单）。
        world.Items.LiveCorpseCount.ShouldBeGreaterThan(0, "清剿收尾死的那一批应当留下尸体。");
        var corpse = world.Items.LiveCorpses.First();
        var at = corpse.AtDeck;

        // 圈的读数 = 圈里活着的尸体数 × 每具那一档 —— 钉的是这一式，不是「恰好一具」。
        // 甲板放大一倍之后倒在一处的敌人聚得更近：清剿收尾那一批有 3 具落进同一个 2 米圈，
        // 这一格于是从 -20 变成 -60。写死「一具」量的是旧甲板上尸体散得多开这一件偶然事，
        // 换个场地就红，而它本来要钉的是「尸体压着就掉情绪、搬走就消」。
        MoodAuraAt(world, at).ShouldBe(CorpsesNear(world, at) * ArenaTuning.MoodCorpseAuraPerGameHour, 1e-9,
            "尸体在地上还没搬走，那一圈负面情绪就在。");
        CorpsesNear(world, at).ShouldBeGreaterThan(0, "读的这一点上确实有尸体压着。");

        // 单是生产代码当场排的：仗一打完尸体就有着落，搬运人立刻领走。
        RunUntilLanding(world, HaulLevel.CorpseToChurch, corpse.Id);
        var landing = LandingsAt(world, HaulLevel.CorpseToChurch).Last(l => l.ItemId == corpse.Id);
        landing.Landed.ShouldBeTrue($"投递没走通：{landing.Reason}");
        landing.TargetBuildingId.ShouldBe(ArenaWorld.ChurchId);

        Run(world, 2400, () => world.Items.LiveCorpseCount == 0, null);

        world.Items.LiveCorpseCount.ShouldBe(0, "尸体最终都投进了教堂。");
        MoodAuraAt(world, at).ShouldBe(0.0, 1e-9, "搬走即消圈。");
        ShouldBeIdleAndAvailable(world, landing.CarrierId, "交完手就空了。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 场上一次倒下好几个人_每一具尸体恰好一张单_跑完之后不留一张残单()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        FightToTheEnd(world);

        world.Items.LiveCorpseCount.ShouldBeGreaterThan(1, "清剿那一仗倒下的不止一个人。");

        // 尸体一直躺在甲板上没人搬的那一段里逐步判：在途的 corpse 单不多于场上还活着的尸体，
        // 每一具活着的尸体恰好一张。
        var startCorpses = world.Items.LiveCorpseCount;
        Run(world, 2400, () => world.Items.LiveCorpseCount == 0, () =>
        {
            world.Haul.CountAt(HaulLevel.CorpseToChurch)
                .ShouldBeLessThanOrEqualTo(world.Items.LiveCorpseCount,
                    "在途的 corpse 单不多于场上还没搬走的尸体 —— 单不会凭空多出来。");
            foreach (var corpse in world.Items.LiveCorpses)
            {
                OutstandingOfItem(world, HaulLevel.CorpseToChurch, corpse.Id)
                    .ShouldBe(1, $"尸体 {corpse.Id} 在甲板上没人搬走的那一段里始终恰好有一张单。");
            }

            return true;
        });

        startCorpses.ShouldBeGreaterThan(1);
        world.Items.LiveCorpseCount.ShouldBe(0, "每一具尸体都被搬进了教堂。");
        world.Haul.CountAt(HaulLevel.CorpseToChurch).ShouldBe(0, "需求消失之后不留残单。");
        world.CarryingCount.ShouldBe(0);
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- ② 魔晶 → 镇政府仓库 ----

    [Fact]
    public void 甲板上有魔晶_搬运那一档自己补一张入库单_魔晶最终入库()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 生态产出落在甲板指定位置（真跑要吞两块矿石；这一格只验「甲板上有货就排单」）。
        var at = world.NearestFreeDeckCell(ArenaTuning.CrystalDropDeckX, ArenaTuning.CrystalDropDeckZ);
        var crystal = world.Items.Add(PointItemKind.Crystal, at, "crystal");

        RunUntilLanding(world, HaulLevel.GroundCargoToWarehouse, crystal.Id);

        var landing = LastLandingAt(world, HaulLevel.GroundCargoToWarehouse);
        landing.Landed.ShouldBeTrue($"魔晶入库没走通：{landing.Reason}");
        landing.TargetBuildingId.ShouldBe(ArenaWorld.TownHallId, "魔晶只进镇政府仓库。");
        world.Town.Crystal.ShouldBe(1.0, 1e-9, "仓库里多一单位魔晶。");
        world.Items.Find(crystal.Id).ShouldBeNull("入库之后这一件离场。");
        ShouldBeIdleAndAvailable(world, landing.CarrierId, "搬完就回到空闲。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 甲板上没有魔晶_魔晶入库那一档一次都不排单_仓库不动()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        Run(world, ArenaTime.StepsPerGameHour * 2, null, null);

        world.Haul.Pending.Count(o => o.Level == HaulLevel.GroundCargoToWarehouse).ShouldBe(0,
            "甲板上没有魔晶，那一档不排单。");
        world.Town.Crystal.ShouldBe(0.0, 1e-9, "仓库里没有凭空多出来的魔晶。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- ③ 备件 → 修房 ----

    [Fact]
    public void 某一栋耐久没满且账上有备件_搬运修房那一档自己补一张单_十个游戏分钟后耐久加五十()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DamageBuilding(ArenaWorld.RestaurantId, 30.0);
        var healthBefore = world.Construction.Find(ArenaWorld.RestaurantId)!.Health;
        var partsBefore = world.Town.SparePart;

        RunUntilLanding(world, HaulLevel.SparePartToRepair);

        var landing = LastLandingAt(world, HaulLevel.SparePartToRepair);
        landing.Landed.ShouldBeTrue("备件投进饭店那一档。");
        landing.TargetBuildingId.ShouldBe(ArenaWorld.RestaurantId);
        world.Town.SparePart.ShouldBe(partsBefore - 1.0, 1e-9, "备件扣 1 件。");
        world.Deck.IsRepairing(ArenaWorld.RestaurantId).ShouldBeTrue("修房那一档开起来了。");

        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.RepairGameMinutes);
        HaulOrdersStepShouldStayConserved(world);

        world.Construction.Find(ArenaWorld.RestaurantId)!.Health
            .ShouldBe(Math.Min(100.0, healthBefore + ArenaTuning.RepairDurability), 1e-9,
                "修完 +50 耐久，钳到上限。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- ④ 地面散货 → 镇政府仓库 ----

    [Fact]
    public void 电梯名额排不到的矿石_落成地面散货自己排一张入库单_材料全程由电梯带上()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 挖开六格：四个象限的第 1 层，外加象限 0、1 的第 2 层
        // （挖第 2 层要求第 1 层先开 —— 自上而下）。
        //
        // **为什么六格**：电梯那一头按「象限 0..3 再层 1..2」的固定次序取料，每轮只有 4 个名额，
        // 人先货后。挖开六格就一定有格排在名额之外，那几格靠人搬 —— 这一格要验的就是那一条路。
        //
        // **只有一处高危格**（象限 0 第 2 层那处洞穴）：特殊材料只有那一个产地，而它在电梯
        // 次序里排第二，名额从来轮得到它 —— 材料那一半因此不走地面，只验「不重不漏」。
        foreach (var (sector, layer) in new[] { (0, 1), (1, 1), (2, 1), (3, 1), (0, 2), (1, 2) })
        {
            DigCell(world, sector, layer);
        }

        foreach (var (sector, layer) in new[] { (0, 1), (1, 1), (2, 1), (3, 1), (0, 2), (1, 2) })
        {
            world.Soil.AssignMiners(sector, layer, ArenaTuning.MinePostsPerCell).ShouldBe(
                ArenaTuning.MinePostsPerCell, $"{sector}_{layer} 派满矿工。");
        }

        world.Soil.MarkCaveCleared(0, 2).ShouldBeTrue("象限 0 第 2 层那处洞穴清剿成功，转成高危生产区。");

        var oreBefore = world.Town.Ore;
        var materialBefore = world.Town.SpecialMaterial;
        var payloadOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var maxPendingOre = 0.0;
        var maxPendingMaterial = 0.0;

        // 四个游戏小时采矿（每人在岗每 2 游戏小时 1 矿石、每 4 游戏小时 1 特殊材料）：
        // 名额之外的格在这一段里攒下一堆货，攒够之后电梯才开始排它们。
        Run(world, ArenaTime.StepsPerGameHour * 4, null, () =>
        {
            foreach (var item in world.Items.Live) payloadOf[item.Id] = item.Payload;
            maxPendingOre = Math.Max(maxPendingOre, world.PendingOre);
            maxPendingMaterial = Math.Max(maxPendingMaterial, world.PendingMaterial);
            return true;
        });

        maxPendingOre.ShouldBeGreaterThan(0.0, "「本航段还欠多少矿石」那一对读数被刷出来了。");
        maxPendingMaterial.ShouldBe(0.0, "特殊材料只有一处产地，且在电梯名额之内 —— 从不积压到地面。");

        var landedOre = LandingsAt(world, HaulLevel.GroundCargoToWarehouse)
            .Count(l => l.Landed && PayloadOf(payloadOf, l.ItemId) == "ore");
        var landedMaterial = LandingsAt(world, HaulLevel.GroundCargoToWarehouse)
            .Count(l => l.Landed && PayloadOf(payloadOf, l.ItemId) == "material");

        landedOre.ShouldBeGreaterThan(0,
            "地面散货那一档自己排出了矿石的入库单并投进了镇政府仓库。"
            + $"第 {world.Tick} 步 · 手上={world.CarryingCount} · 队列={world.Haul.DebugText()} · "
            + CarriersOf(world)
            + $"镇政府门口={world.DoorDeckOf(ArenaWorld.TownHallId)} · {world.HaulLandingText()}");

        landedMaterial.ShouldBe(0,
            "特殊材料只有一处产地，且在电梯的名额之内 —— 它不走地面散货那条路。"
            + $"第 {world.Tick} 步 · 手上={world.CarryingCount} · "
            + CarriersOf(world) + $"队列={world.Haul.DebugText()}");

        world.Town.SpecialMaterial.ShouldBe(
            ArenaTuning.OpeningSpecialMaterial + Math.Floor(MinedMaterial(world)), 1e-9,
            "整件材料都经电梯入库；不满一件的零头留在地里（电梯只运整件）。");

        // 两条路共用电梯那本「已运走」的账 —— 入库的量不可能超过地里一共采出的量。
        world.Town.Ore.ShouldBeLessThanOrEqualTo(ArenaTuning.OpeningOre + MinedOre(world) + 1e-9,
            "同一件矿不会被电梯与地面各运一遍。");
        world.Town.SpecialMaterial
            .ShouldBeLessThanOrEqualTo(ArenaTuning.OpeningSpecialMaterial + MinedMaterial(world) + 1e-9,
                "同一件特殊材料同理。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- ⑤ 不积压 ----

    [Fact]
    public void 魔晶与修房的需求一直都在_同一档在途始终最多一张单()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 两个需求同时一直都在：甲板上有三件魔晶、饭店耐久没满。
        var at = world.NearestFreeDeckCell(ArenaTuning.CrystalDropDeckX, ArenaTuning.CrystalDropDeckZ);
        for (var i = 0; i < 3; i++) world.Items.Add(PointItemKind.Crystal, at, "crystal");
        world.DamageBuilding(ArenaWorld.RestaurantId, 30.0);

        var worstCrystal = 0;
        var worstPart = 0;
        Run(world, 1200, null, () =>
        {
            var crystal = OutstandingAt(world, HaulLevel.GroundCargoToWarehouse);
            var part = OutstandingAt(world, HaulLevel.SparePartToRepair);
            worstCrystal = Math.Max(worstCrystal, crystal);
            worstPart = Math.Max(worstPart, part);
            crystal.ShouldBeLessThanOrEqualTo(1, "同一类需求在途最多一张单（排队 + 在途一起算）。");
            part.ShouldBeLessThanOrEqualTo(1, "备件那一档同理。");
            return true;
        });

        worstCrystal.ShouldBe(1, "这一段里魔晶入库那一档确实排过单（不是「一直没单」蒙过去的）。");
        worstPart.ShouldBe(1, "修房那一档同样。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- ⑥ 仗打完不把在途那一单钉死 ----

    [Fact]
    public void 仗打完收场时手上有一单_那一单的走位令留着_两程跑完回到空闲()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        // 修房那一档的需求摆在场上：耗备件 1 个，饭店耐久少 30。
        world.DamageBuilding(ArenaWorld.RestaurantId, 30.0);
        var partsBefore = world.Town.SparePart;

        ArenaSetup.Inject(world, "StartMatch", "g1|g2", out _, out var why).ShouldBeTrue($"办赛开不起来：{why}");

        // 走起来之后才收场 —— 这一格要验的是「在途」那一档，不是「刚领单」。
        var carrier = RunUntilCarrierOnLeg(world, HaulLeg.ToPickup);
        world.CarryingOf(carrier.Id)!.Level.ShouldBe(HaulLevel.SparePartToRepair);
        world.Battle.Occasion.ShouldBe(BattleOccasion.Match, "收场之前仗还在打。");

        world.Battle.ForceEnd(world.Tick);

        // 收场那一发不把那一张单钉死在半路上。
        RunUntilLanding(world, HaulLevel.SparePartToRepair);

        world.Town.SparePart.ShouldBe(partsBefore - 1.0, 1e-9, "收场之后那一单照样投了出去。");
        ShouldBeIdleAndAvailable(world, carrier.Id, "投完人回到空闲。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ==== 小工具 ====

    /// <summary>
    /// 走玩家那一发 <c>ExcavateSoil</c> 挖开原生洞穴格（第 2 层）——
    /// 挖开即涌出 <c>5 + 2 × 层</c> 只原生怪，按同一套 5.11 交战。
    /// </summary>
    private static void OpenCave(ArenaWorld world)
    {
        world.Soil.BeginDigging(0, 1, ArenaTuning.DigWorkerThreshold, world.Tick).ShouldBeTrue("给 0_1 派工。");
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue("第 1 层挖开了。");

        ArenaSetup.Inject(world, "ExcavateSoil", "0|2", out _, out var why).ShouldBeTrue($"挖 0_2 不成：{why}");
    }

    private static (ArenaUnit Strong, ArenaUnit Doomed) LopsidedMatch(ArenaWorld world)
    {
        var strong = world.Roster.Find("g1")!;
        strong.Melee = ArenaTuning.StatMax;
        strong.Ranged = ArenaTuning.StatMax;
        strong.Riding = ArenaTuning.StatMax;

        var doomed = world.Roster.Find("g2")!;
        doomed.Melee = ArenaTuning.StartingStat;
        doomed.Ranged = ArenaTuning.StartingStat;
        doomed.Riding = ArenaTuning.StartingStat;
        return (strong, doomed);
    }
    /// <summary>
    /// 走玩家那一发 <c>PullAnchor</c> 开一场拔锚防守，并真推进到仗打完
    /// （战斗中倒下的人当场成尸体；收场余敌撤退，不再按名单序号判生死）。
    ///
    /// <para><b>先把工作人员标成「不参战」</b>（<c>SetNonCombatant</c>，也是玩家那一发）：
    /// 那一档管的是<b>谁去搬</b>，不是谁去打 —— 把搬运的人留在原地，
    /// 免得他们跟着仗满甲板跑、跑进建筑格子里被挤住（那是另一处缺陷，不在本工单里）。</para>
    /// </summary>
    private static void FightToTheEnd(ArenaWorld world)
    {
        foreach (var u in world.Roster.Units.Where(u => u.Role == UnitRole.Worker).ToList())
        {
            ArenaSetup.Inject(world, "SetNonCombatant", $"{u.Id}|1", out _, out var why)
                .ShouldBeTrue($"标 {u.Id} 不参战：{why}");
        }

        ArenaSetup.Inject(world, "PullAnchor", "", out _, out var anchorWhy)
            .ShouldBeTrue($"拔锚防守开不起来：{anchorWhy}");
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence);

        // 收场余敌撤退之后，尸体只能来自战斗中真倒下的人。推进到收场前一分钟，
        // 再用交战那一摊唯一的写血口（ApplyDamage）让三名来敌倒下 ——
        // 与双方交手打出来的死走同一条路径，不另造一条「测试专用」的击杀；
        // 放在收场前，尸体才留得到战后（收场不再按名单序号补一批）。
        var lastMinute = world.Battle.EndTick - ArenaTime.StepsPerGameMinute;
        while (world.Battle.Occasion != BattleOccasion.None && world.Tick < lastMinute)
        {
            world.Advance(1);
        }

        foreach (var enemy in world.Roster.Units
                     .Where(u => u.IsBeast && !u.LeftTown && u.State == UnitState.Standing)
                     .OrderBy(u => u.RosterIndex).Take(3).ToList())
        {
            enemy.Hp = -99.5;
            world.Battle.ApplyDamage(world.Tick, enemy, 1.0);
            enemy.State.ShouldBe(UnitState.Dead, "这一名在战斗中倒下。");
        }

        Run(world, ArenaTime.StepsPerGameHour * 3, () => world.Battle.Occasion == BattleOccasion.None, null);

        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "防守打满了 2 个游戏小时就该收场。");
        world.TownHallBreached.ShouldBeFalse(
            "这一格要的是「守住了」那一档 —— 被攻破会拆掉教堂，尸体就投不进去了。");
    }

    private static string RunUntilCorpseAppears(ArenaWorld world)
    {
        const int budget = ArenaTime.StepsPerGameHour * 2;
        for (var i = 0; i < budget; i++)
        {
            world.Advance(1);
            HaulOrdersStepShouldStayConserved(world);
            foreach (var item in world.Items.LiveCorpses)
            {
                if (item.Payload.Length > 0) return item.Id;
            }
        }

        world.Items.LiveCorpseCount.ShouldBeGreaterThan(0,
            $"{budget} 步之内没人死：{world.Battle.DebugText()}，血量读数 {HpText(world)}");
        return string.Empty;
    }

    private static ArenaUnit RunUntilCarrierOnLeg(ArenaWorld world, HaulLeg leg)
    {
        const int budget = 400;
        for (var i = 0; i < budget; i++)
        {
            world.Advance(1);
            foreach (var u in world.Roster.Units)
            {
                if (world.CarryingLegOf(u.Id) == leg) return u;
            }
        }

        world.CarryingCount.ShouldBeGreaterThan(0,
            $"{budget} 步之内没有人手上有一单：队列={world.Haul.DebugText()}");
        return world.Roster.Units[0];
    }

    /// <summary>推进到那一档交出第一张单为止（预算之内没交单就红，红在带上读数的那一句）。</summary>
    private static void RunUntilLanding(ArenaWorld world, HaulLevel level, string? itemId = null)
    {
        const int budget = 2400;
        for (var i = 0; i < budget; i++)
        {
            world.Advance(1);
            HaulOrdersStepShouldStayConserved(world);
            if (world.HaulLandings.Any(l => l.Level == level
                && (itemId is null || l.ItemId == itemId)))
            {
                return;
            }
        }

        (itemId is null
            ? world.HaulLandings.Any(l => l.Level == level)
            : world.HaulLandings.Any(l => l.Level == level && l.ItemId == itemId))
            .ShouldBeTrue(
                $"{budget} 步之内没有人交出 {level} 那一档的单："
                + $"场上尸体={world.Items.LiveCorpseCount}，队列={world.Haul.DebugText()}，"
                + $"手上={world.CarryingCount}，{world.HaulLandingText()}，仗={world.Battle.DebugText()}");
    }

    /// <summary>
    /// 推进 <paramref name="steps"/> 步，每一步都判一次守恒与 <paramref name="check"/>。
    /// </summary>
    private static void Run(ArenaWorld world, int steps, Func<bool>? stopWhen, Func<bool>? check)
    {
        for (var i = 0; i < steps; i++)
        {
            world.Advance(1);
            HaulOrdersStepShouldStayConserved(world);
            check?.Invoke();
            if (stopWhen is not null && stopWhen()) return;
        }
    }

    private static void HaulOrdersStepShouldStayConserved(ArenaWorld world)
        => world.Town.IsConserved().ShouldBeTrue(
            $"第 {world.Tick} 步账不平：{world.Town.ConservedReport()}");

    private static List<HaulLanding> LandingsAt(ArenaWorld world, HaulLevel level)
        => world.HaulLandings.Where(l => l.Level == level).ToList();

    private static HaulLanding LastLandingAt(ArenaWorld world, HaulLevel level)
    {
        var all = LandingsAt(world, level);
        all.Count.ShouldBeGreaterThan(0, $"{level} 那一档一张单都没交过。");
        return all[^1];
    }

    /// <summary>场上在途（排队 + 在手）的那一档一共几张。</summary>
    private static int OutstandingAt(ArenaWorld world, HaulLevel level)
    {
        var n = 0;
        foreach (var order in world.Haul.Pending)
        {
            if (order.Level == level) n++;
        }

        foreach (var u in world.Roster.Units)
        {
            var inHand = world.CarryingOf(u.Id);
            if (inHand is not null && inHand.Level == level) n++;
        }

        return n;
    }

    private static int OutstandingOfItem(ArenaWorld world, HaulLevel level, string itemId)
    {
        var n = 0;
        foreach (var order in world.Haul.Pending)
        {
            if (order.Level == level && order.ItemId == itemId) n++;
        }

        foreach (var u in world.Roster.Units)
        {
            var inHand = world.CarryingOf(u.Id);
            if (inHand is not null && inHand.Level == level && inHand.ItemId == itemId) n++;
        }

        return n;
    }

    private static string PayloadOf(IReadOnlyDictionary<string, string> payloadOf, string itemId)
        => payloadOf.TryGetValue(itemId, out var payload) ? payload : string.Empty;

    /// <summary>临时诊断：手上每一单是谁、走哪一程、人与单各在哪个甲板坐标。</summary>
    private static string CarriersOf(ArenaWorld world)
    {
        var parts = new List<string>();
        foreach (var u in world.Roster.Units)
        {
            if (world.CarryingOf(u.Id) is not { } order) continue;
            parts.Add(string.Format(CultureInfo.InvariantCulture,
                "[{0} leg={1} at={2:0.0},{3:0.0} from={4:0.0},{5:0.0} to={6} item={7}@{8:0.0},{9:0.0}]",
                u.Id, world.CarryingLegOf(u.Id), u.DeckX, u.DeckZ, order.OriginDeck.X, order.OriginDeck.Z,
                order.TargetBuildingId, order.ItemId,
                world.Items.Find(order.ItemId)?.AtDeck.X ?? -999.0,
                world.Items.Find(order.ItemId)?.AtDeck.Z ?? -999.0));
        }

        return "搬运工 " + (parts.Count == 0 ? "（一个都没有）" : string.Join(" ", parts));
    }

    /// <summary>尸体负面情绪圈那一式（与世界里那一口同一式：半径 2 米，每小时 −20）。</summary>
    private static double MoodAuraAt(ArenaWorld world, NavPoint at)
    {
        var delta = 0.0;
        foreach (var corpse in world.Items.LiveCorpses)
        {
            if (at.DistanceTo(corpse.AtDeck) > ArenaTuning.CorpseAuraRadiusMeters) continue;
            delta += ArenaTuning.MoodCorpseAuraPerGameHour;
        }

        return delta;
    }

    /// <summary>那一点周围压着几具尸体（<see cref="MoodAuraAt"/> 数的正是这些）。</summary>
    private static int CorpsesNear(ArenaWorld world, NavPoint at)
    {
        var n = 0;
        foreach (var corpse in world.Items.LiveCorpses)
        {
            if (at.DistanceTo(corpse.AtDeck) <= ArenaTuning.CorpseAuraRadiusMeters) n++;
        }

        return n;
    }

    private static void DigCell(ArenaWorld world, int sector, int layer)
    {
        world.Soil.BeginDigging(sector, layer, ArenaTuning.DigWorkerThreshold, world.Tick)
            .ShouldBeTrue($"给 {sector}_{layer} 派满开挖人手。");
        world.Advance(ArenaTime.StepsPerGameHour);
        HaulOrdersStepShouldStayConserved(world);
        world.Soil.IsDug(sector, layer).ShouldBeTrue($"{sector}_{layer} 挖开了。");
    }

    /// <summary>六格合计采出多少矿石（土壤册那一列是累计产量，电梯与地面两路都从它取）。</summary>
    private static double MinedOre(ArenaWorld world) => MinedOf(world, material: false);

    private static double MinedMaterial(ArenaWorld world) => MinedOf(world, material: true);

    private static double MinedOf(ArenaWorld world, bool material)
    {
        var total = 0.0;
        foreach (var (sector, layer) in MagicElevator.CellOrder())
        {
            if (!world.Soil.IsDug(sector, layer)) continue;
            total += material
                ? world.Soil.MaterialAccumulatedAt(sector, layer)
                : world.Soil.OreAccumulatedAt(sector, layer);
        }

        return total;
    }

    private static string HpText(ArenaWorld world)
        => string.Join(",", world.Roster.Units
            .Where(u => u.Role == UnitRole.Gladiator)
            .Take(2)
            .Select(u => string.Concat(u.Id, "=", u.Hp.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                "@", u.DeckX.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ",", u.DeckZ.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))));

    /// <summary>「空闲可用」的那几条 —— 与派单那一格读的是同一组条件。</summary>
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
}
