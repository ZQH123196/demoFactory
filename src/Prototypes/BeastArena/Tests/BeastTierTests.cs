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
/// 巨兽位阶：门槛（挖掘深度）、代价（魂石 / 特殊材料 / 金币）、1 游戏小时的进阶仪式、
/// 位阶收益（移速与采矿倍率）、快照段与 <see cref="ArenaWorld.TierAdvanced"/> 事件。
///
/// <para><b>门槛怎么摆</b>：照现役 <c>UndergroundSoilTests</c> 的写法走
/// 「<see cref="UndergroundSoil.BeginDigging"/> + 派满工人 + 推进一个游戏小时」那一串，
/// 不造捷径；真值是 <see cref="UndergroundSoil.IsDug"/>。</para>
///
/// <para><b>仪式那一格的时间对齐</b>：仪式盖住 600 步（1 游戏小时），而采矿与法力恢复
/// 都挂在整游戏小时上。所以要把某一档的测量窗口对齐到整点，
/// 否则窗口里会不会落一个整点、落几个整点就成了另一回事。</para>
/// </summary>
public class BeastTierTests
{
    private const int Seed = 7;

    /// <summary>账本指纹（六本实物 + 负债 + 法力五栏）。被拒的一发前后要逐项相等。</summary>
    private readonly record struct Ledger(
        double Coin, double Food, double Soul, double Ore, double Material, double Part,
        double Debt, int ManaAvailable, int ManaStasis, int ManaHelper);

    private static Ledger Fingerprint(ArenaWorld world)
    {
        var t = world.Town;
        return new Ledger(t.Coin, t.Food, t.SoulStone, t.Ore, t.SpecialMaterial, t.SparePart,
            t.TotalDebt, t.AvailableMana, t.StasisMana, t.HelperMana);
    }

    /// <summary>从快照里取出段首是 <paramref name="head"/> 的那一段。</summary>
    private static string SegmentOf(string snapshot, string head)
    {
        foreach (var segment in snapshot.Split(" | "))
        {
            if (segment.StartsWith(head + " ", StringComparison.Ordinal)) return segment;
        }

        throw new InvalidOperationException($"这份快照里没有 {head} 那一段。");
    }

    /// <summary>从快照里取出 <c>key=value</c> 那一栏的值。</summary>
    private static string FieldOf(string segment, string key)
    {
        foreach (var token in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.StartsWith(key + "=", StringComparison.Ordinal)) continue;
            return token[(key.Length + 1)..];
        }

        throw new InvalidOperationException($"那一段里没有 {key}= 那一栏：{segment}");
    }

    /// <summary>「下一个整游戏小时的步号」（严格大于当前步号）。</summary>
    private static long NextHourTick(ArenaWorld world)
        => ((world.Tick / ArenaTime.StepsPerGameHour) + 1) * ArenaTime.StepsPerGameHour;

    /// <summary>量两个游戏小时里那一格攒了多少矿石（窗口对齐到整点，窗口里正好两个整点）。</summary>
    private static double MeasureOreTwoHours(ArenaWorld world, int sector, int layer)
    {
        world.AdvanceTo(NextHourTick(world));
        var before = world.Soil.OreAccumulatedAt(sector, layer);
        world.Advance(ArenaTime.StepsPerGameHour * 2);
        return world.Soil.OreAccumulatedAt(sector, layer) - before;
    }

    // ---- 门槛 ----

    [Fact]
    public void 门槛未到_回GateNotMet_资源一分不扣()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();   // 资源全够 —— 拒绝的原因只该是门槛

        var before = Fingerprint(world);
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.GateNotMet}", Case.Sensitive,
            "第二层、洞穴清剿、魔晶嵌入三项都还没到，回 GateNotMet。");
        why.ShouldContain($"第 {ArenaTuning.Tier2GateLayer} 层", Case.Sensitive,
            "具体层数由 rejected: 那一行的明细带。");

        Fingerprint(world).ShouldBe(before, "门槛没过，一分不扣。");
        world.Tier.ShouldBe(BeastTier.Tier1, "位阶不动。");
        world.InRitual.ShouldBeFalse("仪式没开起来。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- 代价与仪式 ----

    [Fact]
    public void 门槛三项齐了_进阶走成_代价按表扣_快照给剩余六十分钟()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();

        var soul = world.Town.SoulStone;
        var material = world.Town.SpecialMaterial;
        var coin = world.Town.Coin;

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.InRitual.ShouldBeTrue("仪式开起来了。");
        world.Tier.ShouldBe(BeastTier.Tier1, "仪式还没走完，位阶不动。");
        world.RitualUntilTick.ShouldBe(
            world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours),
            "仪式到点 = 现在 + 1 游戏小时。");

        world.Town.SoulStone.ShouldBe(soul - ArenaTuning.CostOf(BeastTier.Tier2).SoulStone, 1e-9,
            "魂石扣 40。");
        world.Town.SpecialMaterial.ShouldBe(
            material - ArenaTuning.CostOf(BeastTier.Tier2).SpecialMaterial, 1e-9, "特殊材料扣 10。");
        world.Town.Coin.ShouldBe(coin - ArenaTuning.CostOf(BeastTier.Tier2).Coin, 1e-9, "金币扣 2000。");

        // BEAST 段：段首两个 token 是身份（不能拿 tier= 当身份），ritual 给剩余游戏分钟。
        var beast = SegmentOf(world.DebugSnapshot(), "BEAST");
        beast.ShouldStartWith(
            "BEAST beast tier=1 form=original ritual=60 dwell=0/240 fed=0/400 sat=", Case.Sensitive,
            "段首到 fed 那一段逐字不变，生态三项接在后面。");
        FieldOf(beast, "ritual").ShouldBe("60", "ritual 那一栏给剩余游戏分钟。");
        FieldOf(beast, "tier").ShouldBe("1", "tier 那一栏给位阶。");

        // 重放快照带同一段。
        world.ReplaySnapshot().ShouldContain("BEAST beast tier=1 form=original ritual=60", Case.Sensitive,
            "重放快照带同一段。");

        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    // ---- 停靠中进阶：兽半宽换档，停靠点跟着外扩 ----

    [Fact]
    [Trait("测试类别", "慢测试")] // 慢的原因：真跑一整段停靠航程（贫城 40 米）加两游戏小时挖掘与一小时仪式。
    public void 停靠中二阶进阶完成_兽走到二阶停靠点_不留在旧点压城()
    {
        // Arrange：停在贫城门外（一阶那一点，离城门 26.45 米），门槛与三本账摆齐。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);

        // Act：一发进阶，仪式走到点（1 游戏小时）—— 位阶 +1 那一刻停靠点外扩到 30.00 米。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours));
        world.Tier.ShouldBe(BeastTier.Tier2, "仪式走到点位阶 +1。");

        // 新停靠点在旧点城外 3.55 米：3.55 ÷ (0.4 × 1.15 × 0.1) ≈ 78 步，循环给足余量。
        for (var i = 0; i < 100 && world.Voyage.Phase != VoyagePhase.Holding; i++)
        {
            world.Advance(10);
        }

        // Assert：站到了二阶停靠点上，旧的一阶点已经离开。
        var stop2 = BigMapGrid.PoorCity.StopPoint(BeastTier.Tier2);
        NavArrive.Reached(world.Voyage.Position, stop2, ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"进阶完成应当让停靠中的兽走到二阶停靠点，实际停在 {world.Voyage.Position}");
        NavArrive.Reached(world.Voyage.Position, BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1),
            ArenaTuning.ArriveToleranceMeters).ShouldBeFalse("不该还站在一阶旧点上（兽身压城 3.55 米）。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "到新点重新停稳。");
        world.DockedCityId.ShouldBe(BigMapGrid.PoorCity.Id, "还是那座城，停靠读数不随重新落位而变。");
    }

    // ---- 仪式期间冻结的那几摊 ----

    [Fact]
    public void 仪式期间采矿与游客入口冻结_移动与办赛回InvalidPhase_满一小时后位阶二()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();
        world.Soil.AssignMiners(0, ArenaTuning.Tier2GateLayer, 3).ShouldBe(3, "那一格派满矿工。");

        // 走到整点：先让那一格真的攒出矿石（不然「冻结」那一格是空断言）。
        var hour = NextHourTick(world);
        world.AdvanceTo(hour);
        world.Soil.OreAccumulatedAt(0, ArenaTuning.Tier2GateLayer).ShouldBeGreaterThan(0.0,
            "整点上那一格攒出矿石了。");

        // 再走 100 步离开整点：仪式正好盖住下一个整点，采矿那一格才看得出被冻住。
        world.AdvanceTo(hour + 100);

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.InRitual.ShouldBeTrue();

        // 移动与办赛：仪式期间一律回 InvalidPhase。
        foreach (var (verb, args) in new (string, string)[]
                 {
                     ("Dock", BigMapGrid.RichCity.Id),
                     ("PullAnchor", string.Empty),
                     ("SteerOutOfBounds", string.Empty),
                     ("StartMatch", "g1,g2|g3"),
                 })
        {
            ArenaSetup.Inject(world, verb, args, out _, out var badWhy).ShouldBeFalse();
            badWhy.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive,
                $"{verb} 在仪式期间回 InvalidPhase。");
        }

        var ore = world.Soil.OreAccumulatedAt(0, ArenaTuning.Tier2GateLayer);
        var visitors = world.Visitors.InTownCount;

        for (var i = 0; i < ArenaTime.StepsPerGameHour; i++)
        {
            world.Advance(1);
            if (!world.InRitual) continue;

            world.Soil.OreAccumulatedAt(0, ArenaTuning.Tier2GateLayer)
                .ShouldBe(ore, 1e-9, "仪式期间采矿冻结。");
            world.Visitors.InTownCount
                .ShouldBeLessThanOrEqualTo(visitors, "仪式期间游客不进来。");
        }

        world.Tier.ShouldBe(BeastTier.Tier2, "满一个游戏小时后位阶 +1。");
        world.InRitual.ShouldBeFalse("仪式结束。");
    }

    [Fact]
    public void 仪式期间整点结算冻结_法力不恢复_仪式结束后照常恢复()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();

        // 走到整点之后 100 步：仪式正好盖住下一个整点。
        var hour = NextHourTick(world);
        world.AdvanceTo(hour + 100);

        // 立刻花掉一格法力，给整点恢复留出空位（否则池是满的，恢复那一格读不出来）。
        world.Town.TrySpendInstantMana().ShouldBeTrue("花掉一格法力。");
        var mana = world.Town.AvailableMana;

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);

        world.AdvanceTo(hour + ArenaTime.StepsPerGameHour);   // 整点那一刻：仪式还在
        world.InRitual.ShouldBeTrue("整点那一刻仪式还没走完。");
        world.Town.AvailableMana.ShouldBe(mana, "仪式期间整点法力恢复冻结。");

        world.AdvanceTo(hour + (ArenaTime.StepsPerGameHour * 2));
        world.Town.AvailableMana.ShouldBe(mana + 1, "仪式结束后法力照常恢复。");
    }

    // ---- 仪式期间照常的那几摊 ----

    [Fact]
    public void 仪式期间修房与复活照常()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();

        // 修房：教堂砸出缺口、派一个人上工、把修房发出去。
        world.DamageBuilding(ArenaWorld.ChurchId, 60.0).ShouldBeTrue("教堂先砸出缺口。");
        ArenaSetup.Inject(world, "AssignPost", "w1|church", out _, out var postWhy).ShouldBeTrue(postWhy);
        ArenaSetup.Inject(world, "RepairBuilding", ArenaWorld.ChurchId, out _, out var repairWhy)
            .ShouldBeTrue(repairWhy);
        world.Deck.IsRepairing(ArenaWorld.ChurchId).ShouldBeTrue("在途的修房。");

        // 复活：造一具尸体（词表里没有「打一下人」这一发，走交战那一摊的写血口）。
        var fallen = world.Roster.Find("w2")!;
        fallen.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, fallen, 1.0);
        var corpse = world.Items.LiveCorpses.Single(c => c.Payload == "w2");

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.InRitual.ShouldBeTrue();

        ArenaSetup.Inject(world, "ReviveCorpse", corpse.Id, out _, out var reviveWhy).ShouldBeTrue(reviveWhy);

        world.Advance(ArenaTime.StepsPerGameHour);
        world.Construction.Find(ArenaWorld.ChurchId)!.Health
            .ShouldBeGreaterThan(40.0, "仪式期间修房照常完工。");
        fallen.State.ShouldBe(UnitState.Standing, "仪式期间治伤复活照常。");
    }

    [Fact]
    public void 仪式期间搬运照常_新排的那一单照样被人领走()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.InRitual.ShouldBeTrue();

        // 仪式里排一张地面散货的单：取货点钉在甲板东南角。
        world.Haul.Enqueue(ArenaWorld.Order(
            HaulLevel.GroundCargoToWarehouse, "cargoZ", new NavPoint(11.0, 11.0),
            ArenaWorld.TownHallId, 1.0, world.Tick));

        world.Advance(1);
        var carrier = world.Roster.Units.FirstOrDefault(u => world.CarryingOf(u.Id)?.ItemId == "cargoZ");
        carrier.ShouldNotBeNull("仪式期间照常派单。");
        world.Haul.Count.ShouldBe(0, "领走之后队列里没有了。");
    }

    [Fact]
    public void 仪式期间土壤回长照常()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();

        // 门槛那一格挖到第 2 层（第 3 象限只挖第 1 层，好让回长那一格单独长回来）。
        world.Tier2Ready();
        world.DugTo(3, 1);
        world.Soil.IsDug(3, 1).ShouldBeTrue("第 3 象限第 1 层挖开了。");

        // 仪式盖住日结那一刻：回长挂在整游戏日上。
        var day = ((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay;
        world.AdvanceTo(day - 300);

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);

        world.AdvanceTo(day);
        world.InRitual.ShouldBeTrue("日结那一刻仪式还没走完。");
        world.Soil.IsDug(3, 1).ShouldBeFalse("仪式期间土壤回长照常（那一格长满了）。");
    }

    // ---- 位阶收益 ----

    [Fact]
    public void 位阶二_移速一点一五倍_采矿快百分之二十五()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();
        world.Soil.AssignMiners(0, ArenaTuning.Tier2GateLayer, 3).ShouldBe(3, "那一格派满矿工。");

        // ① 一阶基线：三个矿工，两个游戏小时 = 3 矿石。
        var baseline = MeasureOreTwoHours(world, 0, ArenaTuning.Tier2GateLayer);
        baseline.ShouldBe(3 * (1.0 / ArenaTuning.OreGameHoursPerMiner) * 2, 1e-9, "一阶基线。");

        // ② 进二阶。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours));
        world.Tier.ShouldBe(BeastTier.Tier2);

        // ③ 二阶：同样两个游戏小时，多 25%。
        var scaled = MeasureOreTwoHours(world, 0, ArenaTuning.Tier2GateLayer);
        scaled.ShouldBe(baseline * 1.25, 1e-9, "二阶采矿倍率 1.25。");
        ArenaTuning.StatsOf(BeastTier.Tier2).MiningFactor.ShouldBe(1.25, 1e-9);
        ArenaTuning.StatsOf(BeastTier.Tier1).MiningFactor.ShouldBe(1.00, 1e-9);

        // ④ 移速：位阶倍率乘进总倍率。
        world.Voyage.TierSpeedFactor.ShouldBe(1.15, 1e-9, "二阶移速倍率 1.15。");
        world.Voyage.SpeedFactor().ShouldBe(1.15, 1e-9, "饱食度满档时总倍率就是位阶那一档。");
        world.Voyage.DebugSegment().ShouldContain("speed=0.46", Case.Sensitive,
            "读数那一列是 0.4 × 1.15。");

        // 实走：0.4 米每现实秒 × 1.15 × 0.1 现实秒每步 = 0.046 米每步。
        ArenaSetup.Inject(world, "Dock", BigMapGrid.RichCity.Id, out _, out var sailWhy)
            .ShouldBeTrue(sailWhy);
        var before = world.Voyage.Position;
        world.Advance(1);
        world.Voyage.Position.DistanceTo(before).ShouldBe(0.046, 1e-9,
            "二阶一步 0.046 米（0.4 × 1.15 × 0.1）。");
    }

    // ---- 三阶（切片外） ----

    [Fact]
    public void 三阶不在切片内_地下只有两层_二阶之后永远GateNotMet()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Tier2Ready();

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out _).ShouldBeTrue("二阶那一发走成了。");
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours));
        world.Tier.ShouldBe(BeastTier.Tier2);

        // 三阶的门是关的：第三层不存在，GateMet 恒回 false。
        world.GateMet(BeastTier.Tier3).ShouldBeFalse("地下只有两层，第三层不存在。");
        UndergroundSoil.Layers.ShouldBe(2);
        ArenaTuning.Tier3GateLayer.ShouldBe(3);

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var gateWhy).ShouldBeFalse();
        gateWhy.ShouldStartWith($"rejected: {RejectReason.GateNotMet}", Case.Sensitive);
        gateWhy.ShouldContain("三阶不在本切片内", Case.Sensitive, "明细里要说清是切片外，不是「再去挖一层」。");

        // 再试一次也一样：没有「把第 3 层挖出来」这条路。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var againWhy).ShouldBeFalse();
        againWhy.ShouldStartWith($"rejected: {RejectReason.GateNotMet}", Case.Sensitive);
        world.Tier.ShouldBe(BeastTier.Tier2, "位阶停在二阶。");
    }

    // ---- 资源不足 ----

    [Fact]
    public void 三样资源分别不足_各回对应原因_整笔不扣()
    {
        // ① 魂石不够：特殊材料与金币都够，但一格都不许扣。
        {
            var world = ArenaWorldBuilder.New(Seed).Build();
            world.DockedAt(BigMapGrid.PoorCity.Id);
            world.Tier2Ready();

            world.Town.ProduceSpecialMaterial(100.0);
            world.Town.ProduceCoin(50000.0);
            world.Town.TrySpendSoulStone(world.Town.SoulStone - 10.0).ShouldBeTrue("魂石只剩 10。");

            var before = Fingerprint(world);
            ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();
            why.ShouldStartWith($"rejected: {RejectReason.InsufficientSoulStone}", Case.Sensitive);
            Fingerprint(world).ShouldBe(before, "魂石不够：特殊材料与金币一格不许扣。");
        }

        // ② 特殊材料不够：魂石与金币都够。
        {
            var world = ArenaWorldBuilder.New(Seed).Build();
            world.DockedAt(BigMapGrid.PoorCity.Id);
            world.Tier2Ready();

            world.Town.ProduceSoulStone(1000.0);
            world.Town.ProduceCoin(50000.0);
            world.Town.TrySpendSpecialMaterial(world.Town.SpecialMaterial - 1.0).ShouldBeTrue("特殊材料只剩 1。");

            var before = Fingerprint(world);
            ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();
            why.ShouldStartWith($"rejected: {RejectReason.InsufficientMaterial}", Case.Sensitive);
            Fingerprint(world).ShouldBe(before, "特殊材料不够：魂石与金币一格不许扣。");
        }

        // ③ 金币不够：魂石与特殊材料都够。
        {
            var world = ArenaWorldBuilder.New(Seed).Build();
            world.DockedAt(BigMapGrid.PoorCity.Id);
            world.Tier2Ready();

            world.Town.ProduceSoulStone(1000.0);
            world.Town.ProduceSpecialMaterial(100.0);
            world.Town.TrySpendCoin(world.Town.Coin - 1999.0).ShouldBeTrue("金币差 1 枚。");

            var before = Fingerprint(world);
            ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();
            why.ShouldStartWith($"rejected: {RejectReason.InsufficientCoin}", Case.Sensitive);
            Fingerprint(world).ShouldBe(before, "金币不够：魂石与特殊材料一格不许扣。");
        }
    }

    // ---- 事件 ----

    [Fact]
    public void TierAdvanced_进阶成功发一次_门槛资源与三阶都不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();

        var fired = new List<BeastTier>();
        world.TierAdvanced += tier => fired.Add(tier);

        // ① 门槛未到：不发。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var gateWhy).ShouldBeFalse();
        gateWhy.ShouldStartWith($"rejected: {RejectReason.GateNotMet}", Case.Sensitive);
        fired.ShouldBeEmpty("门槛未到不发事件。");

        // ② 资源不足：不发。
        world.Tier2Ready();
        world.Town.TrySpendSoulStone(world.Town.SoulStone - 10.0).ShouldBeTrue("魂石只剩 10。");
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var costWhy).ShouldBeFalse();
        costWhy.ShouldStartWith($"rejected: {RejectReason.InsufficientSoulStone}", Case.Sensitive);
        fired.ShouldBeEmpty("资源不足不发事件。");

        // ③ 资源补足：仪式开起来，事件在仪式到点那一步发一次。
        world.Town.ProduceSoulStone(1000.0);
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out _).ShouldBeTrue();
        fired.ShouldBeEmpty("仪式还没走完，不发事件。");

        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours));
        fired.ShouldBe([BeastTier.Tier2], "仪式到点发一次，值是二阶。");

        // ④ 三阶在切片外：进阶被拒，事件不再发。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var maxWhy).ShouldBeFalse();
        maxWhy.ShouldStartWith($"rejected: {RejectReason.GateNotMet}", Case.Sensitive,
            "三阶的门是关的（地下只有两层）。");
        fired.Count.ShouldBe(1, "被拒的那一发不发事件。");
    }
}
