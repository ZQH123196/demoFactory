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
/// 巨兽生态那一件：饱食度与衰减、三类生态对象、手动吞食、自主觅食、消化与魔晶、
/// 形态收益与切换。逻辑层纯 C# 推进，不起引擎。
///
/// <para><b>两层各钉一半</b>：<see cref="BeastEcology"/> 的收益表与再生直接在独立构造上验
/// （不借世界，也不摆位）；航行与动词那一侧通过 <see cref="ArenaWorld"/> 验，
/// 因为「走到对象旁边」本来就是航行的事。</para>
/// </summary>
public class BeastEcologyTests
{
    private const int Seed = 7;

    private static EcoNodeSpec Tree(string id, double x, double z)
        => new(id, EcoKind.Tree, new NavPoint(x, z));

    private static EcoNodeSpec Herd(string id, double x, double z)
        => new(id, EcoKind.Herd, new NavPoint(x, z));

    private static EcoNodeSpec Ore(string id, double x, double z)
        => new(id, EcoKind.Ore, new NavPoint(x, z));

    private static void PlaceAt(ArenaWorld world, NavPoint at)
        => world.MapStepper.ApplyMove(BeastVoyage.BeastId, at, 0.0);

    private static string SegmentOf(string snapshot, string head)
    {
        foreach (var segment in snapshot.Split(" | "))
        {
            if (segment.StartsWith(head + " ", StringComparison.Ordinal)) return segment;
        }

        throw new InvalidOperationException($"这份快照里没有 {head} 那一段。");
    }

    // ---- 饱食度 ----

    [Fact]
    public void 毎游戏小时衰减四点_饿到底也不会死()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = world.Voyage.Satiation;

        world.Advance(ArenaTime.StepsPerGameHour);
        (before - world.Ecology.Satiation).ShouldBe(ArenaTuning.SatiationDecayPerGameHour, 1e-9,
            "一个游戏小时衰减 4 点。");

        // 80 点 ÷ 4 = 20 个游戏小时见底；再走三天也不会掉到负数。
        world.Advance(ArenaTime.StepsPerGameDay * 3);
        world.Ecology.Satiation.ShouldBe(0.0, 1e-9, "饱食度钳在 0，巨兽不会饿死。");
        world.Voyage.SpeedFactor().ShouldBe(ArenaTuning.StarvingSpeedFactor,
            "见底只是移速打折（低饱食减速保留）。");
    }

    [Fact]
    public void 日结不再自动喂食_粮仓与累计喂食都不动()
    {
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(500.0).Build();
        var granary = world.Voyage.GranaryFood;
        var fed = world.Voyage.FedTotalFood;

        world.AdvanceTo(ArenaTime.StepsPerGameDay + 1);

        world.Voyage.GranaryFood.ShouldBe(granary, 1e-9, "自动日结喂食已删。");
        world.Voyage.FedTotalFood.ShouldBe(fed, 1e-9, "没有实际喂食就不进暴食进度。");
    }

    // ---- 生态对象与消化 ----

    [Fact]
    public void 三类对象收益按表_每满一百消化产一颗魔晶_溢出保留()
    {
        var ecology = new BeastEcology([Tree("t", 0, 0), Herd("h", 1, 0), Ore("o", 2, 0)]);
        ecology.SetSatiation(10.0);

        ecology.TryEat("t", tick: 0, BeastForm.Original, auto: false, out var tree).ShouldBeTrue();
        tree.SatiationGain.ShouldBe(ArenaTuning.TreeSatiationGain, 1e-9, "树木饱食 +20。");
        tree.DigestionGain.ShouldBe(ArenaTuning.TreeDigestionGain, 1e-9, "树木消化 +20。");

        ecology.TryEat("h", tick: 0, BeastForm.Original, auto: false, out var herd).ShouldBeTrue();
        herd.SatiationGain.ShouldBe(ArenaTuning.HerdSatiationGain, 1e-9, "兽群饱食 +40。");
        herd.DigestionGain.ShouldBe(ArenaTuning.HerdDigestionGain, 1e-9, "兽群消化 +40。");

        ecology.TryEat("o", tick: 0, BeastForm.Original, auto: false, out var ore).ShouldBeTrue();
        ore.SatiationGain.ShouldBe(ArenaTuning.OreSatiationGain, 1e-9, "矿石饱食 +10。");
        ore.DigestionGain.ShouldBe(ArenaTuning.OreDigestionGain, 1e-9, "矿石消化 +60。");

        ecology.Satiation.ShouldBe(80.0, 1e-9, "10 + 20 + 40 + 10 = 80。");
        ecology.CrystalsProduced.ShouldBe(1, "20 + 40 + 60 = 120，满 100 产一颗。");
        ecology.DigestionOverflow.ShouldBe(20.0, 1e-9, "溢出 20 点保留。");
    }

    [Fact]
    public void 树木十二小时再生_兽群二十四小时_矿石不再生()
    {
        var ecology = new BeastEcology([Tree("t", 0, 0), Herd("h", 1, 0), Ore("o", 2, 0)]);

        ecology.TryEat("t", 0, BeastForm.Original, false, out _);
        ecology.TryEat("h", 0, BeastForm.Original, false, out _);
        ecology.TryEat("o", 0, BeastForm.Original, false, out _);

        ecology.Find("t")!.AvailableAt(0).ShouldBeFalse("刚吃完不可吞。");
        ecology.Find("t")!.AvailableAt(ArenaTime.StepsPerGameHour * 12).ShouldBeTrue("树木 12 小时原位再生。");
        ecology.Find("t")!.AvailableAt((ArenaTime.StepsPerGameHour * 12) - 1).ShouldBeFalse("差一步还没长回来。");

        ecology.Find("h")!.AvailableAt(ArenaTime.StepsPerGameHour * 24).ShouldBeTrue("兽群 24 小时再生。");
        ecology.Find("h")!.AvailableAt((ArenaTime.StepsPerGameHour * 24) - 1).ShouldBeFalse("差一步还没长回来。");

        ecology.Find("o")!.AvailableAt(ArenaTime.StepsPerGameHour * 24 * 365).ShouldBeFalse("矿石不再生。");
        ecology.Find("o")!.Consumed.ShouldBeTrue("矿石吃完永久离场。");
    }

    [Fact]
    public void 兽群节点摆三只_吃完读数归零()
    {
        var ecology = new BeastEcology([Herd("h", 0, 0)]);
        ecology.Find("h")!.HerdRemaining(0).ShouldBe(ArenaTuning.HerdSilhouetteCount, "可吞时节点上有三只。");

        ecology.TryEat("h", 0, BeastForm.Original, false, out _);
        ecology.Find("h")!.HerdRemaining(0).ShouldBe(0, "吃空之后那一档读数为零。");
        ecology.DebugSegments(0).Single().ShouldContain("herd=0", Case.Sensitive);
    }

    [Fact]
    public void 贫城到沙城航线固定两块矿石两棵树一群野兽_都在十米内且地形相符()
    {
        var poor = BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1);
        var desert = BigMapGrid.DesertCity.StopPoint(BeastTier.Tier1);

        var routeNodes = BigMapGrid.EcoNodeSpecs
            .Where(n => DistanceToSegment(n.Position, poor, desert) <= ArenaTuning.ManualEatRangeMeters)
            .ToList();

        routeNodes.Count(n => n.Kind == EcoKind.Ore).ShouldBe(2, "航线上固定两块矿石。");
        routeNodes.Count(n => n.Kind == EcoKind.Tree).ShouldBe(2, "航线上固定两棵树。");
        routeNodes.Count(n => n.Kind == EcoKind.Herd).ShouldBe(1, "航线上固定一群野兽。");

        foreach (var node in BigMapGrid.EcoNodeSpecs)
        {
            var terrain = BigMapTerrain.TerrainAt(node.Position);
            if (node.Kind == EcoKind.Ore)
            {
                terrain.ShouldBe(TerrainKind.Desert, $"{node.Id} 是矿石，应当摆在沙漠带。");
            }
            else
            {
                terrain.ShouldBe(TerrainKind.Grass, $"{node.Id} 是草木 / 兽群，应当摆在草地。");
            }
        }

        // 两块矿石 60 × 2 = 120 点消化：第一单位魔晶在抵达沙城前一定拿得到。
        var ore = routeNodes.Where(n => n.Kind == EcoKind.Ore).ToList();
        var digest = ore.Sum(n => ArenaTuning.OreDigestionGain);
        digest.ShouldBe(120.0, 1e-9, "两块矿石正好 120 点消化。");
        digest.ShouldBeGreaterThanOrEqualTo(ArenaTuning.DigestionPerCrystal,
            "够产出第一单位魔晶。");
    }

    // ---- 手动吞食 ----

    [Fact]
    public void 手动吞食_十米内发得出去_五游戏分钟后同时结算饱食与消化()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var node = world.Ecology.Find("ore_poor_1")!;
        PlaceAt(world, node.Position);
        world.Voyage.SetSatiation(20.0);

        world.EatTarget("ore_poor_1").ShouldBe(RejectReason.InvalidArgs, "10 米内发得出去。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.ManualEating, "先在原地吃五游戏分钟。");

        world.Advance((ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes) - 1);
        world.Ecology.DigestionProgress.ShouldBe(0.0, 1e-9, "没到五分钟不结算。");

        world.Advance(1);
        world.Ecology.DigestionProgress.ShouldBe(ArenaTuning.OreDigestionGain, 1e-9, "到点同时结算消化。");
        world.Ecology.Satiation.ShouldBe(30.0, 1e-9, "20 + 10（期间没有跨整点，衰减为 0）。");
        world.Voyage.Phase.ShouldNotBe(VoyagePhase.ManualEating, "吃完回到原来的阶段。");
    }

    [Fact]
    public void 手动吞食_饱食满拒绝_超出十米拒绝_被拒时一格不动()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var node = world.Ecology.Find("ore_poor_1")!;

        PlaceAt(world, new NavPoint(node.Position.X + 11.0, node.Position.Z));
        world.Voyage.SetSatiation(50.0);
        world.EatTarget("ore_poor_1").ShouldBe(RejectReason.InvalidTarget, "11 米够不着。");
        world.Voyage.Phase.ShouldNotBe(VoyagePhase.ManualEating);

        PlaceAt(world, node.Position);
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        var beforeDigestion = world.Ecology.DigestionProgress;
        world.EatTarget("ore_poor_1").ShouldBe(RejectReason.FullCapacity, "饱食 100 拒绝。");
        world.Ecology.DigestionProgress.ShouldBe(beforeDigestion, 1e-9, "被拒时消化不动。");
        world.Ecology.Find("ore_poor_1")!.AvailableAt(world.Tick).ShouldBeTrue("被拒时对象没被吃掉。");
    }

    // ---- 自主觅食 ----

    [Fact]
    public void 自主觅食_低于三十自动航行触发_吃到八十返航回断点()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(20.0);
        world.OrderSailTo(BigMapGrid.DesertCity.Id);

        var sawDetour = false;
        NavPoint? waypoint = null;
        for (var i = 0; i < 600 && !(sawDetour && world.Ecology.Satiation >= ArenaTuning.SatiationAfterMeal); i++)
        {
            world.Advance(10);
            if (world.Voyage.Phase == VoyagePhase.DetouringToFood && !sawDetour)
            {
                sawDetour = true;
                waypoint = world.Voyage.RouteWaypoint;
            }
        }

        sawDetour.ShouldBeTrue("饿着且 30 米内有对象，应当脱队觅食。");
        waypoint.ShouldNotBeNull("脱队那一刻要记下航线原断点。");
        world.Ecology.Satiation.ShouldBeGreaterThanOrEqualTo(ArenaTuning.SatiationAfterMeal,
            "吃到 80 才返航。");

        for (var i = 0; i < 600 && world.Voyage.Phase != VoyagePhase.Sailing; i++) world.Advance(10);

        world.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "回到断点后恢复原目标城。");
        world.Voyage.RouteWaypoint.ShouldBeNull("回到断点之后断点交还。");
        world.Voyage.TargetCityId.ShouldBe(BigMapGrid.DesertCity.Id, "恢复的是原目标城。");
    }

    [Fact]
    public void 附近没有对象_立即返航并给原因_一游戏小时内不再自动尝试()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        PlaceAt(world, BigMapGrid.RichCity.StopPoint(BeastTier.Tier1));
        world.Voyage.SetSatiation(20.0);

        ForageGiveUpReason? reason = null;
        world.ForageGaveUp += gave => reason = gave;

        world.OrderSailTo(BigMapGrid.WildAnchor.Id);
        world.Advance(1);

        reason.ShouldBe(ForageGiveUpReason.NoTargetInRange,
            "30 米内没有对象 —— 载荷是封闭枚举的「够不着」那一档（不再是自由文本）。");
        world.Ecology.ForageGaveUpReason.ShouldContain("没有可吞", Case.Sensitive,
            "中文文案由枚举派生，读数照旧可读（快照与横幅同源）。");
        world.Ecology.InForageCooldownAt(world.Tick).ShouldBeTrue("进入一游戏小时冷却。");

        var cooldownUntil = world.Ecology.ForageCooldownUntilTick;
        for (var i = 0; i < 600 && world.Tick < cooldownUntil; i++)
        {
            world.Advance(1);
            world.Voyage.Phase.ShouldNotBe(VoyagePhase.DetouringToFood,
                "冷却期间不再自动尝试。");
        }
    }

    [Fact]
    public void 玩家召回_觅食途中随时掉头回断点()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(20.0);
        world.OrderSailTo(BigMapGrid.DesertCity.Id);

        // 预算按开局位置算：野地到第一件生态对象约 70 米，饿着走（0.7 倍速）要 2500 步上下。
        for (var i = 0; i < 600 && world.Voyage.Phase != VoyagePhase.DetouringToFood; i++) world.Advance(10);
        world.Voyage.Phase.ShouldBe(VoyagePhase.DetouringToFood, "先脱队。");

        var recalled = false;
        world.ForageRecalled += () => recalled = true;

        ArenaSetup.Inject(world, "RecallForage", string.Empty, out _, out var why).ShouldBeTrue(why);
        recalled.ShouldBeTrue("召回发一次事件。");
        world.Voyage.Phase.ShouldBeOneOf(VoyagePhase.ReturningToWaypoint, VoyagePhase.Sailing);

        for (var i = 0; i < 200 && world.Voyage.Phase != VoyagePhase.Sailing; i++) world.Advance(10);
        world.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "回到航线。");
        world.Ecology.Satiation.ShouldBeLessThan(ArenaTuning.SatiationAfterMeal, "没吃完就被叫回来了。");
    }

    // ---- 消化 / 魔晶 ----

    [Fact]
    public void 魔晶落甲板_工人搬进仓库_三处读数与账本守恒()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var first = world.Ecology.Find("ore_poor_1")!;
        var second = world.Ecology.Find("ore_poor_2")!;

        PlaceAt(world, first.Position);
        world.Voyage.SetSatiation(10.0);
        world.EatTarget("ore_poor_1").ShouldBe(RejectReason.InvalidArgs);
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);
        world.Ecology.CrystalsProduced.ShouldBe(0, "一块矿石 60 点，还不满 100。");

        PlaceAt(world, second.Position);
        world.EatTarget("ore_poor_2").ShouldBe(RejectReason.InvalidArgs);
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);

        world.Ecology.CrystalsProduced.ShouldBe(1, "两块矿石 120 点，产一颗。");
        (world.CrystalOnDeck + world.CrystalCarrying).ShouldBe(1, "产出的魔晶落在甲板指定位置。");
        var beast = SegmentOf(world.DebugSnapshot(), "BEAST");
        beast.ShouldContain($"crystal={world.CrystalOnDeck}/{world.CrystalCarrying}/0", Case.Sensitive,
            "快照读出甲板 / 搬运中 / 仓库三处，甲板与搬运中互斥。");

        for (var i = 0; i < 2400 && world.Town.Crystal < 1.0; i++) world.Advance(1);

        world.Town.Crystal.ShouldBe(1.0, 1e-9, "工人或帮工逐件搬进镇政府仓库。");
        world.CrystalWarehouse.ShouldBe(1);
        world.CrystalOnDeck.ShouldBe(0, "甲板上那一件已入库。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 魔晶是货物不是食物_不进食物账()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.ProduceCrystal(2.0);

        world.Town.Crystal.ShouldBe(2.0, 1e-9);
        world.Town.Food.ShouldBe(ArenaTuning.OpeningFood, 1e-9, "魔晶不进食物账。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 魔晶用途_补魔_至少缺五格才可用()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.ProduceCrystal(2.0);

        // 缺 4 格：拒绝。
        for (var i = 0; i < 4; i++) world.Town.TrySpendInstantMana().ShouldBeTrue();
        world.UseCrystal(CrystalUseKind.Mana, string.Empty).ShouldBe(RejectReason.InsufficientMana);
        world.Town.Crystal.ShouldBe(2.0, 1e-9, "被拒时魔晶不扣。");

        // 再花 2 格 → 缺 6 格：用一颗回 5 格。
        world.Town.TrySpendInstantMana().ShouldBeTrue();
        world.Town.TrySpendInstantMana().ShouldBeTrue();
        world.UseCrystal(CrystalUseKind.Mana, string.Empty).ShouldBe(RejectReason.InvalidArgs);
        world.Town.AvailableMana.ShouldBe(9, "4 + 5 = 9 格可用。");
        world.Town.Crystal.ShouldBe(1.0, 1e-9, "仓库扣 1。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 魔晶用途_治疗一名重伤居民()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.ProduceCrystal(1.0);

        var wounded = world.Roster.Find("w3")!;
        wounded.State = UnitState.Wounded;
        wounded.Hp = -50.0;

        world.UseCrystal(CrystalUseKind.Heal, "w3").ShouldBe(RejectReason.InvalidArgs);
        wounded.State.ShouldBe(UnitState.Standing);
        wounded.Hp.ShouldBe(ArenaTuning.UnitMaxHp, 1e-9);
        world.Town.Crystal.ShouldBe(0.0, 1e-9);
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 魔晶用途_立即完成一栋正在修理的建筑()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.ProduceCrystal(1.0);
        world.DamageBuilding(ArenaWorld.RestaurantId, 40.0);

        world.Deck.RepairBuilding(ArenaWorld.RestaurantId, world.Tick).Ok.ShouldBeTrue("先开一处工坊修理。");
        world.Deck.IsRepairing(ArenaWorld.RestaurantId).ShouldBeTrue();

        world.UseCrystal(CrystalUseKind.Repair, ArenaWorld.RestaurantId).ShouldBe(RejectReason.InvalidArgs);
        world.Deck.IsRepairing(ArenaWorld.RestaurantId).ShouldBeFalse("立即完成。");
        world.Construction.Find(ArenaWorld.RestaurantId)!.Health.ShouldBe(100.0, 1e-9, "+50 耐久并钳到上限。");
        world.Town.Crystal.ShouldBe(0.0, 1e-9);
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 魔晶出售_基准五百乘城市倍率()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.ProduceCrystal(2.0);
        world.OnDocked(BigMapGrid.PoorCity);

        var coin = world.Town.Coin;
        ArenaSetup.Inject(world, "SellCrystal", "1", out _, out var why).ShouldBeTrue(why);

        world.Town.Coin.ShouldBe(coin + (ArenaTuning.CrystalBaseValue * ArenaWorld.AirshipPriceFactor), 1e-9,
            "停靠即开空艇成交窗口，按空艇价格倍率成交（500 × 1.16 = 580）。");
        world.Town.Crystal.ShouldBe(1.0, 1e-9);
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 魔晶出售_不在停靠期也不在空艇窗口时拒()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.ProduceCrystal(1.0);

        world.SellCrystal(1).ShouldBe(RejectReason.InvalidPhase);
        world.Town.Crystal.ShouldBe(1.0, 1e-9, "被拒时不动。");
    }

    [Fact]
    public void 快照读出饱食度_消化与溢出_魔晶三处_生态对象可吞状态与再生倒计时()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var node = world.Ecology.Find("ore_poor_1")!;
        PlaceAt(world, node.Position);
        world.Voyage.SetSatiation(10.0);
        world.EatTarget("ore_poor_1").ShouldBe(RejectReason.InvalidArgs);
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);

        var snapshot = world.DebugSnapshot();
        var beast = SegmentOf(snapshot, "BEAST");
        beast.ShouldContain("sat=20", Case.Sensitive);
        beast.ShouldContain("dig=60", Case.Sensitive);
        beast.ShouldContain("ovf=60", Case.Sensitive);
        beast.ShouldContain("crystal=0/0/0", Case.Sensitive);

        var ore = snapshot.Split(" | ").Single(s => s.StartsWith("ECO ore_poor_1 ", StringComparison.Ordinal));
        ore.ShouldContain("kind=ore", Case.Sensitive);
        ore.ShouldContain("state=eaten", Case.Sensitive);
        ore.ShouldContain("regrow=never", Case.Sensitive);

        var herd = snapshot.Split(" | ").Single(s => s.StartsWith("ECO herd_poor_1 ", StringComparison.Ordinal));
        herd.ShouldContain("state=ready", Case.Sensitive);
        herd.ShouldContain("herd=3", Case.Sensitive);
        herd.ShouldContain("regrow=-", Case.Sensitive);
    }

    // ---- 形态 ----

    [Fact]
    public void 沙漠累计四小时解锁_离开不清零()
    {
        var growth = new BeastGrowth();

        for (var i = 0; i < ArenaTuning.DesertFormDwellGameMinutes; i++) growth.AdvanceDwell(true);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Desert).ShouldBeTrue("累计到 240 分钟解锁。");
        growth.DwellGameMinutes.ShouldBe(ArenaTuning.DesertFormDwellGameMinutes);

        growth.AdvanceDwell(false);
        growth.DwellGameMinutes.ShouldBe(ArenaTuning.DesertFormDwellGameMinutes,
            "离开沙漠不清零（累计口径）。");
    }

    [Fact]
    public void 暴食态累计四百且已驶离贫城才解锁()
    {
        var departed = false;
        var growth = new BeastGrowth(() => departed);

        growth.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeFalse("还没驶离贫城，不解锁。");

        departed = true;
        growth.CheckUnlocks();
        growth.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("两道门都过了才解锁。");
    }

    [Fact]
    public void 战斗中切不了形态_非战斗时免费立即切()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetDwellGameMinutes(ArenaTuning.DesertFormDwellGameMinutes);
        world.Advance(1);
        world.FormUnlocked(BeastForm.Desert).ShouldBeTrue();

        world.OnDocked(BigMapGrid.PoorCity);
        ArenaSetup.Inject(world, "StartMatch", "g1|g2", out _, out var why).ShouldBeTrue(why);
        world.Battle.Occasion.ShouldNotBe(BattleOccasion.None);

        world.SwitchForm(BeastForm.Desert).ShouldBe(RejectReason.InvalidPhase, "战斗中切不了。");
        world.Form.ShouldBe(BeastForm.Original, "被拒时形态不动。");

        world.Battle.ForceEnd(world.Tick);
        world.SwitchForm(BeastForm.Desert).ShouldBe(RejectReason.InvalidArgs, "非战斗时切得动。");
        world.Form.ShouldBe(BeastForm.Desert);
    }

    [Fact]
    public void 荒漠态吃矿石饱食乘二_其他乘零点五_消化不变()
    {
        var ecology = new BeastEcology([Tree("t", 0, 0), Ore("o", 1, 0)]);

        ecology.SetSatiation(10.0);
        ecology.TryEat("o", 0, BeastForm.Desert, false, out var ore).ShouldBeTrue();
        ore.SatiationGain.ShouldBe(ArenaTuning.OreSatiationGain * 2, 1e-9, "矿石 ×2。");
        ore.DigestionGain.ShouldBe(ArenaTuning.OreDigestionGain, 1e-9, "消化不变。");

        ecology.SetSatiation(10.0);
        ecology.TryEat("t", 0, BeastForm.Desert, false, out var tree).ShouldBeTrue();
        tree.SatiationGain.ShouldBe(ArenaTuning.TreeSatiationGain * 0.5, 1e-9, "其他对象 ×0.5。");
    }

    [Fact]
    public void 暴食态吞食饱食与消化都乘一点五()
    {
        var ecology = new BeastEcology([Tree("t", 0, 0)]);
        ecology.SetSatiation(10.0);

        ecology.TryEat("t", 0, BeastForm.Glutton, false, out var tree).ShouldBeTrue();
        tree.SatiationGain.ShouldBe(ArenaTuning.TreeSatiationGain * ArenaTuning.GluttonFormEatFactor, 1e-9);
        tree.DigestionGain.ShouldBe(ArenaTuning.TreeDigestionGain * ArenaTuning.GluttonFormEatFactor, 1e-9);
    }

    [Fact]
    public void 荒漠态在沙漠带饥饿衰减减半_暴食态乘一点五()
    {
        // 荒漠态：沙漠带内每小时掉 2（4 × 0.5）。
        var desert = ArenaWorldBuilder.New(Seed).Build();
        PlaceAt(desert, BigMapGrid.DesertCity.Position);
        desert.Voyage.SetDwellGameMinutes(ArenaTuning.DesertFormDwellGameMinutes);
        desert.Advance(1);
        desert.SwitchForm(BeastForm.Desert).ShouldBe(RejectReason.InvalidArgs);
        desert.Voyage.SetSatiation(80.0);
        desert.AdvanceTo(NextHourTick(desert));
        desert.Ecology.Satiation.ShouldBe(78.0, 1e-9, "荒漠态沙漠带内每小时 2 点。");

        // 暴食态：每小时 6（4 × 1.5）。
        var glutton = ArenaWorldBuilder.New(Seed).Build();
        glutton.LeftPoorCity();
        glutton.Voyage.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        glutton.Advance(1);
        glutton.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("已驶离贫城且喂够 400。");
        glutton.SwitchForm(BeastForm.Glutton).ShouldBe(RejectReason.InvalidArgs);
        glutton.Voyage.SetSatiation(80.0);
        glutton.AdvanceTo(NextHourTick(glutton));
        glutton.Ecology.Satiation.ShouldBe(74.0, 1e-9, "暴食态每小时 6 点。");
    }

    // ---- 删旧 ----

    [Fact]
    public void 粪便那一档已经从点位物枚举里删掉()
    {
        Enum.GetNames<PointItemKind>().ShouldBe(["Corpse", "Cargo", "Crystal"], Case.Sensitive,
            "点位物只剩尸体 / 散货 / 魔晶。");
    }

    // ---- 小工具 ----

    private static long NextHourTick(ArenaWorld world)
        => ((world.Tick / ArenaTime.StepsPerGameHour) + 1) * ArenaTime.StepsPerGameHour;

    /// <summary>一点到线段的距离（米）。</summary>
    private static double DistanceToSegment(NavPoint point, NavPoint a, NavPoint b)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var lengthSquared = (dx * dx) + (dz * dz);
        if (lengthSquared <= 1e-12) return point.DistanceTo(a);

        var t = (((point.X - a.X) * dx) + ((point.Z - a.Z) * dz)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return point.DistanceTo(new NavPoint(a.X + (dx * t), a.Z + (dz * t)));
    }
}
