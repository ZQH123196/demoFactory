#nullable enable
using System;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 按游戏分钟自动战斗：伤害那一式（对人 / 对兽权重、忠诚七折、牧师光环）、
/// 生死判定（<c>Hp ≤ -100</c> 死 / <c>-100 &lt; Hp ≤ 0</c> 重伤）、限时清剿的单双生死、
/// 「谁不参战」，以及镇政府攻破 → 战损折债 → 停摆 → 城卫队解救那一整条链。
/// </summary>
public class BattleTests
{
    private const int Seed = 7;

    private static ArenaUnit Probe(ArenaWorld world, string id, UnitRole role)
        => world.Roster.Add(id, role, new NavPoint(0.0, 0.0));

    [Fact]
    public void 伤害那一式_基数乘战力份额乘围攻人数()
    {
        // 等战力：各 5 / 5 → 伤害 = 10 × 0.5 × 1 = 5。
        BattleSim.DamageOf(5.0, 5.0, 1, false).ShouldBe(5.0, 1e-9);

        // 围攻两人：伤害 ×2。
        BattleSim.DamageOf(5.0, 5.0, 2, false).ShouldBe(10.0, 1e-9);

        // 攻强守弱：10 × (20 / 30) = 6.667。
        BattleSim.DamageOf(20.0, 10.0, 1, false).ShouldBe(10.0 * (20.0 / 30.0), 1e-9);

        // 牧师 2 米圈内那一档乘 0.5。
        BattleSim.DamageOf(5.0, 5.0, 1, true).ShouldBe(2.5, 1e-9);
    }

    [Fact]
    public void 对人战力按近战远程骑术_对兽战力换成骑术近战远程()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var melee = Probe(world, "mOnly", UnitRole.Worker);
        melee.Melee = 100.0;
        melee.Ranged = 0.0;
        melee.Riding = 0.0;
        var human = Probe(world, "human", UnitRole.Gladiator);
        var beast = Probe(world, "beast", UnitRole.Monster);

        // 对人：0.5×近战 + 0.3×远程 + 0.2×骑术 = 50。
        melee.PowerAgainst(human).ShouldBe(50.0, 1e-9, "对人按 0.5 / 0.3 / 0.2。");
        // 对兽：0.5×骑术 + 0.3×近战 + 0.2×远程 = 30。
        melee.PowerAgainst(beast).ShouldBe(30.0, 1e-9, "对兽按 0.5 / 0.3 / 0.2，但权重换了位置。");

        // Monster 三本事统一 30，两种权重算出来都一样 30。
        beast.Melee.ShouldBe(30.0);
        beast.Ranged.ShouldBe(30.0);
        beast.Riding.ShouldBe(30.0);
        beast.PowerAgainst(human).ShouldBe(30.0, 1e-9);
    }

    [Fact]
    public void 忠心落在三十到六十战力乘七折()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var unit = Probe(world, "loyal", UnitRole.Gladiator);
        unit.Melee = 100.0;
        unit.Ranged = 0.0;
        unit.Riding = 0.0;
        var foe = Probe(world, "foe", UnitRole.Worker);

        unit.Loyalty = 80.0;
        unit.PowerAgainst(foe).ShouldBe(50.0, 1e-9, "80 在区间外，不打折。");

        unit.Loyalty = 45.0;
        unit.LoyaltyDiscounted.ShouldBeTrue();
        unit.PowerAgainst(foe).ShouldBe(35.0, 1e-9, "45 落在 30..60，乘七折。");

        unit.Loyalty = 29.0;
        unit.LoyaltyDiscounted.ShouldBeFalse("29 在区间下沿之外。");
        unit.PowerAgainst(foe).ShouldBe(50.0, 1e-9);
    }

    [Fact]
    public void 牧师光环两米圈内才减伤()
    {
        var priest = new NavPoint(0.0, 0.0);

        BattleSim.InsidePriestAura(new NavPoint(2.0, 0.0), [priest]).ShouldBeTrue("正好 2 米算圈内。");
        BattleSim.InsidePriestAura(new NavPoint(2.01, 0.0), [priest]).ShouldBeFalse("2.01 米在圈外。");
        BattleSim.InsidePriestAura(new NavPoint(5.0, 5.0), [priest]).ShouldBeFalse();
        BattleSim.InsidePriestAura(new NavPoint(0.0, 0.0), []).ShouldBeFalse("没有在岗牧师就没有圈。");
    }

    [Fact]
    public void 血量落到零以下是重伤_落到负一百是死()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var victim = Probe(world, "vic", UnitRole.Worker);

        // -100 < Hp ≤ 0：重伤退场。
        victim.Hp = -50.0;
        world.Battle.ApplyDamage(world.Tick, victim, 1.0);
        victim.State.ShouldBe(UnitState.Wounded, "−51 落在重伤那一档。");
        victim.RiseTick.ShouldBe(world.Tick + (ArenaTime.StepsPerGameMinute * ArenaTuning.WoundRiseGameMinutes));
        world.Items.LiveCorpseCount.ShouldBe(0, "重伤不出尸体。");

        // Hp ≤ -100：当场死，生成尸体并自动入账 3 魂石。
        var soulsBefore = world.Town.SoulStone;
        victim.State = UnitState.Standing;
        victim.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, victim, 1.0);
        victim.State.ShouldBe(UnitState.Dead, "−100.5 ≤ −100 即死。");
        world.Items.LiveCorpseCount.ShouldBe(1, "死即生成尸体点位物。");
        world.Town.SoulStone.ShouldBe(soulsBefore + ArenaTuning.SoulStonesPerCorpse, 1e-9, "自动入账 3 魂石。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 生命值只住在单位自己身上_册子里没有第二份()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var unit = world.Roster.Find("w1")!;

        unit.Hp = 63.0;
        unit.Hp.ShouldBe(63.0, "生命值那一本账在单位自己身上。");

        var record = world.Entities.Find("w1");
        record.ShouldNotBeNull("册子登记这个人。");
        record!.Kind.ShouldBe("worker", "册子只管在场、id、次序与类别，不管血量。");
        record.Payload.ShouldBeNull("册子那一行不带血量载荷。");
    }

    [Fact]
    public void 拔锚防守敌人数是二十加四乘向心环数()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        ArenaSetup.Inject(world, "PullAnchor", "", out _, out _).ShouldBeTrue();
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence);
        world.Battle.Pairs.Count.ShouldBe(0, "配对要等下一个整游戏分钟。");

        var enemies = world.Roster.Units.Count(u => u.IsBeast && !u.LeftTown);
        enemies.ShouldBe(20, "贫城向心环数 0 → 20 个敌人。");

        // 富城向心环数 1 → 24 个。
        var rich = ArenaWorldBuilder.New(Seed).Build();
        rich.OnDocked(BigMapGrid.RichCity);
        ArenaSetup.Inject(rich, "PullAnchor", "", out _, out _).ShouldBeTrue();
        rich.Roster.Units.Count(u => u.IsBeast && !u.LeftTown).ShouldBe(24, "20 + 4 × 1。");
    }

    [Fact]
    public void 敌人目标按名单序号每十人切一段()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        ArenaSetup.Inject(world, "PullAnchor", "", out _, out _).ShouldBeTrue();

        var enemies = world.Roster.Units.Where(u => u.IsBeast && !u.LeftTown)
            .OrderBy(u => u.RosterIndex).ToList();

        for (var i = 0; i < enemies.Count; i++)
        {
            var target = world.Battle.TargetOf(enemies[i].Id);
            target.ShouldNotBeNull("每个敌人都有目标。");
            var expected = (i % ArenaTuning.DefenceTargetSegment) switch
            {
                < 5 => ArenaWorld.TownHallId,
                < 8 => ArenaWorld.GranaryId,
                _ => world.NearestProductionId(new NavPoint(enemies[i].DeckX, enemies[i].DeckZ)),
            };

            target!.BuildingId.ShouldBe(expected, $"第 {i} 号敌人的目标按段位分。");
        }
    }

    [Fact]
    public void 普通防守时限到点即胜_固定十魂石_余敌撤退_击杀魂石保留()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        ArenaSetup.Inject(world, "PullAnchor", "", out _, out _).ShouldBeTrue();

        var enemies = world.Roster.Units.Where(u => u.IsBeast && !u.LeftTown)
            .OrderBy(u => u.RosterIndex).ToList();
        enemies.Count.ShouldBe(20);

        // 先击杀一个：这一笔 3 枚魂石是「击杀魂石保留」的那一半。
        var soulsBefore = world.Town.SoulStone;
        world.Battle.ApplyDamage(world.Tick, enemies[0], 1000.0);
        enemies[0].State.ShouldBe(UnitState.Dead);
        world.Town.SoulStone.ShouldBe(soulsBefore + ArenaTuning.SoulStonesPerCorpse, 1e-9);

        // 时限到点、镇政府未破：收场那一格判胜。
        world.TownHallBreached.ShouldBeFalse();
        world.Battle.ForceEnd(world.Tick);

        world.Town.SoulStone.ShouldBe(
            soulsBefore + ArenaTuning.SoulStonesPerCorpse + ArenaTuning.DefenceWinSoulStones, 1e-9,
            "击杀那 3 枚保留，再加胜利固定的 10 枚。");
        world.Battle.LastReport.ShouldNotBeNull();
        world.Battle.LastReport!.Occasion.ShouldBe(BattleOccasion.Defence);
        world.Battle.LastReport.EndedTick.ShouldBe(world.Tick);

        // 剩下的一律撤退（离镇），不按序号判死、也不折成角斗士。
        var remaining = enemies.Skip(1).ToList();
        remaining.All(e => e.LeftTown).ShouldBeTrue("余敌全部撤退。");
        remaining.All(e => e.Role == UnitRole.Enemy).ShouldBeTrue("不再按序号把一半折成角斗士。");
        remaining.All(e => e.State != UnitState.Dead).ShouldBeTrue("撤退的敌人不生成尸体。");
        world.Items.LiveCorpseCount.ShouldBe(1, "只有击杀的那一具尸体。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 洞穴清剿是单处四只_从镇政府电梯口上甲板_原生怪不给魂石()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        ArenaSetup.Inject(world, "ExcavateSoil", "0|1", out _, out _).ShouldBeTrue("第 1 层不是洞穴层，先挖起来。");
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue();

        ArenaSetup.Inject(world, "ExcavateSoil", "0|2", out _, out _).ShouldBeTrue();
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 2).ShouldBeTrue("第 2 层挖开了。");
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "挖开不自动开打。");

        ArenaSetup.Inject(world, "ClearCave", "0|2", out _, out var clearWhy).ShouldBeTrue(clearWhy);
        var monsters = world.Roster.Units.Where(u => u.Role == UnitRole.Monster).ToList();
        monsters.Count.ShouldBe(ArenaTuning.NativeCaveMonsters, "单处洞穴四只原生怪（不再按层数派生）。");
        world.Battle.Occasion.ShouldBe(BattleOccasion.CaveClearing);
        world.Battle.EndTick.ShouldBe(
            world.Tick + ((long)(ArenaTime.StepsPerGameHour * ArenaTuning.CaveGameHours)),
            "时限 1.5 游戏小时只是保险。");

        // 涌出的落点是镇政府电梯口（镇政府门口那一格的格心）与竞技场东西两侧那两个点。
        // 电梯口那一处不是一对写死的米：镇政府门口格是 (0, 2)，格心跟着格长走 ——
        // 1 米一格时落在 (0.5, 2.5)，2 米一格时落在 (1, 5)。
        // 写成写死的 (0, 2) 的话，2 米一格时它滑进镇政府的墙里，寻路层对那一发回「地形无路」，
        // 整场清剿静悄悄地变成空转（实测：怪全在原地不动，我方一个都走不过去）。
        DeckGeometry.CaveSpawn().ShouldBe(new NavPoint(1.0, 5.0), "洞穴涌怪落在镇政府电梯口（门口格心）。");
        DeckGeometry.ArenaWest().ShouldBe(new NavPoint(-1.5, -6.0), "办赛西侧入场点。");
        DeckGeometry.ArenaEast().ShouldBe(new NavPoint(1.5, -6.0), "办赛东侧入场点。");
        DeckGeometry.PriestEdge().ShouldBe(new NavPoint(0.0, -4.0), "在岗牧师驻场边。");

        // 洞穴原生怪死一只：出尸、不入魂石。
        var soulsBefore = world.Town.SoulStone;
        world.Battle.ApplyDamage(world.Tick, monsters[0], 1000.0);
        monsters[0].State.ShouldBe(UnitState.Dead);
        world.Town.SoulStone.ShouldBe(soulsBefore, 1e-9, "洞穴原生怪不给魂石。");
    }

    [Fact]
    public void 谁不参战_立刻移出参战名单并回原建筑槽()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.Inject(world, "AssignPost", "w1|training_ground", out _, out _).ShouldBeTrue();

        var worker = world.Roster.Find("w1")!;
        worker.NonCombatant.ShouldBeFalse();

        ArenaSetup.Inject(world, "SetNonCombatant", "w1|1", out _, out _).ShouldBeTrue();
        worker.NonCombatant.ShouldBeTrue();
        world.Advance(1);   // 定位读数是每步走完之后同步的
        worker.DeckX.ShouldBe(world.DoorDeckOf("training_ground").X, 1e-9, "回原建筑槽位（门口点）。");
        world.Roster.DeckStepper.IsCountedInSeparation("w1").ShouldBeFalse("入槽定身并关避让。");

        ArenaSetup.Inject(world, "SetNonCombatant", "w1|0", out _, out _).ShouldBeTrue();
        worker.NonCombatant.ShouldBeFalse("下一分钟重算配对时又计入。");
    }

    [Fact]
    public void 名单序号在手抓摆位之后不变()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = world.Roster.Find("g3")!.RosterIndex;

        ArenaSetup.Inject(world, "GrabEntity", "g3", out _, out _).ShouldBeTrue();
        ArenaSetup.Inject(world, "DropToDeck", "g3|6|6", out _, out _).ShouldBeTrue();
        world.Advance(1);   // 定位读数是每步走完之后同步的

        var after = world.Roster.Find("g3")!;
        after.RosterIndex.ShouldBe(before, "摆位改位置不改序号。");

        // `DropToDeck` 的两段是甲板逻辑坐标（米），不是格号：6 米现在落在第 3 格里，
        // 那一格的格心 = (3 + 0.5) × 2 = 7 米。落点吸附到最近可通行格这件事没变。
        after.DeckX.ShouldBe(7.0, 1e-9, "落到 6 米那一格的可通行格心（格长 2 米）。");
    }

    [Fact]
    public void 攻破链_锁零当格解救_毁三栋掳七人扣三成货折一点五倍债()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var hall = world.Construction.Find(ArenaWorld.TownHallId)!;
        hall.Health.ShouldBe(200.0);

        var oreBefore = world.Town.Ore;
        var coinBefore = world.Town.Coin;

        // 打空耐久：归零之后出不出册由移除政策答（镇政府答「留」），所以它停在册上的真 0。
        world.DamageBuilding(ArenaWorld.TownHallId, 200.0);
        world.Construction.Find(ArenaWorld.TownHallId)
            .ShouldNotBeNull("留册：镇政府还在建造册里。");
        world.Construction.Find(ArenaWorld.TownHallId)!.Health
            .ShouldBe(0.0, "耐久是真 0，不是停在别处的一个小数字。");

        world.Advance(ArenaTime.StepsPerGameMinute);

        // 城破当格就由城卫队解救：攻破状态只存在了这一格，随后立刻解除。
        world.TownHallBreached.ShouldBeFalse("ticket 06 起城破当格立即解救，不再停摆三个游戏日。");
        world.InStandstill.ShouldBeFalse("没有停摆期。");
        world.StandstillUntilTick.ShouldBe(0, "停摆到期步号恒为 0。");
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "当场结束防守。");

        // ① 按造价从高到低、同价按 id 字典序毁 3 栋普通建筑。
        world.WarLossBuildings.Count.ShouldBe(3);
        world.WarLossBuildings[0].ShouldBe(ArenaWorld.ArenaId, "竞技场造价 800，最高。");
        world.WarLossBuildings[1].ShouldBe(ArenaWorld.RestaurantId, "500 一档里 restaurant < training_ground。");
        world.WarLossBuildings[2].ShouldBe("training_ground");

        // ② 按名单序号掳走前 7 名镇民，不留尸不出魂石。
        world.TakenPeople.Count.ShouldBe(7);
        world.TakenPeople.ShouldBe(["w1", "w2", "w3", "w4", "w5", "w6", "w7"], Case.Sensitive);
        world.TakenPeople.All(id => world.Roster.Find(id)!.LeftTown).ShouldBeTrue();
        world.Items.LiveCorpseCount.ShouldBe(0, "不留尸。");

        // ③ 仓库矿石与特殊材料各扣 30%（向下取整）。
        world.Town.Ore.ShouldBe(oreBefore - System.Math.Floor(oreBefore * 0.30), 1e-9, "矿石扣 3（10 的 30%）。");
        world.Town.SpecialMaterial.ShouldBe(2.0, 1e-9, "2 的 30% 向下取整 = 0。");

        // ④ 债额 = max(500, 战损估值 × 1.5)：800 + 500 + 500 + 7×100 + 3×20 = 2560 → 3840，
        //    解救当格就记进最近那家势力的负债账。
        world.Town.TotalDebt.ShouldBe(3840.0, 1e-9);
        world.PendingDebt.ShouldBe(0.0, 1e-9, "待记的债在解救那一格交出去，不留第二份。");
        world.Town.Coin.ShouldBe(coinBefore, 1e-9, "攻破那一格不动金币账。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());

        // ⑤ 镇政府耐久回到 100，巨兽恢复向原目标城航行。
        world.Construction.Find(ArenaWorld.TownHallId)!.Health
            .ShouldBe(ArenaTuning.RescueTownHallDurability, 1e-9);
        world.PulledAnchor.ShouldBeFalse("解救后恢复航行，拔锚那一闩清掉。");
    }

    [Fact]
    public void 城破当格解救_不停摆_补一次防守可以再拔锚()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        world.Advance(5);

        world.DamageBuilding(ArenaWorld.TownHallId, 200.0);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.TownHallBreached.ShouldBeFalse("当格已解救。");
        world.InStandstill.ShouldBeFalse("没有停摆期。");

        // 经营没有冻结：开挖照样能开工（旧口径里这一档在停摆期也照常，现在连停摆都没有）。
        world.Soil.BeginDigging(2, 1, ArenaTuning.DigWorkerThreshold, world.Tick).ShouldBeTrue();
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(2, 1).ShouldBeTrue("开挖照常。");

        // 巨兽恢复航行了，回到停靠点之后还能再拔一次锚 —— 失败后保留经营补救路径。
        for (var i = 0; i < ArenaTime.StepsPerGameDay && !world.IsDocked; i++) world.Advance(1);
        world.IsDocked.ShouldBeTrue("解救之后重新停靠上贫城。");

        ArenaSetup.Inject(world, "PullAnchor", "", out _, out var pullWhy)
            .ShouldBeTrue($"补救那一场拔得出去：{pullWhy}");
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence, "再拔一次只打普通防守。");
    }

    [Fact]
    public void 城破当格记债_耐久回一百_金币账不动()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        world.Advance(5);
        var coinBefore = world.Town.Coin;
        world.DamageBuilding(ArenaWorld.TownHallId, 200.0);
        world.Advance(ArenaTime.StepsPerGameMinute);

        world.TownHallBreached.ShouldBeFalse("当格即解救。");
        world.Construction.Find(ArenaWorld.TownHallId)!.Health
            .ShouldBe(ArenaTuning.RescueTownHallDurability, 1e-9, "耐久恢复 100。");
        world.Town.TotalDebt.ShouldBeGreaterThan(0.0, "债当格记进最近城镇势力的负债账。");
        world.PendingDebt.ShouldBe(0.0, 1e-9, "待记的债额清零。");
        world.Town.Coin.ShouldBe(coinBefore, 1e-9, "不扣当前金币。");
        world.Town.Creditors.Sum(c => c.Accrued).ShouldBe(world.Town.TotalDebt, 1e-9);
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 通缉只拒债主势力名下城镇的停靠()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        ArenaSetup.Inject(world, "Dock", "rich_city", out _, out _).ShouldBeTrue("没被通缉时两城都停得下。");

        // 负债超 3000 即置通缉（城破那一格记进来的债也走同一本账）。
        world.Town.AccrueDebt(ArenaWorld.PoorFactionId, 4000.0);
        world.Advance(ArenaTime.StepsPerGameDay);
        world.Wanted.ShouldBeTrue("总负债 4000 > 3000。");

        ArenaSetup.Inject(world, "Dock", "poor_city", out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.WantedRefused}", Case.Sensitive, "贫城属债主势力名下。");

        ArenaSetup.Inject(world, "Dock", "wild_anchor", out _, out _).ShouldBeTrue("野外锚地无主，照常停。");
    }

    [Fact]
    public void 战斗摘要五列_无仗时全零()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Battle.DebugText().ShouldBe("none,0,0,0,0", "无仗时五列全填 0。");
        world.DebugSnapshot().ShouldContain("battle=none,0,0,0,0", Case.Sensitive);
    }

    /// <summary>
    /// ticket 08：办赛打完之后 <see cref="BattleSim.LastReport"/> 要留一份<b>收场那一刻</b>的战报 ——
    /// 场合、胜负、双方上场、死亡、重伤、抢走、门票收入全是确定性数字。
    /// 收场之后伤员会被扶起来，所以这份读数必须在扶人<b>之前</b>算好，不能战后按现况重算。
    /// </summary>
    [Fact]
    public void 战后战报_有胜负与死伤()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out var why)
            .ShouldBeTrue($"办赛开得起来：{why}");
        world.Battle.LastReport.ShouldBeNull("仗还没打完，没有战报。");

        world.Advance(ArenaTime.StepsPerGameHour * 2);
        world.Battle.Occasion.ShouldBe(BattleOccasion.None);

        var report = world.Battle.LastReport;
        report.ShouldNotBeNull("打完之后有一份战报。");
        report!.Occasion.ShouldBe(BattleOccasion.Match);
        report.SideACount.ShouldBe(2, "甲队上场两人。");
        report.SideBCount.ShouldBe(1, "乙队上场一人。");
        report.Outcome.ShouldBe(MatchOutcome.SideAWins, "乙队单人被打倒，甲队两人仍站着。");
        report.DeadCount.ShouldBe(0, "单次交手打不到 −100，这一场没人当场死。");
        report.WoundedCount.ShouldBe(1, "乙队那一个重伤退场（场上人数因此归零）。");
        report.LootedCount.ShouldBe(0, "办赛不抢东西。");
        report.GateIncome.ShouldBeGreaterThan(0.0, "停靠期有观众，门票有进账。");
        report.EndedTick.ShouldBeLessThanOrEqualTo(world.Tick, "收场步号不晚于当前步号。");
    }

    /// <summary>
    /// ticket 08：战报那一格进快照的 <c>R</c> 段 —— 一行、字段自解释，
    /// 没打过仗时也占一行（段数稳定，重放三层才对得上）。
    /// </summary>
    [Fact]
    public void 调试快照的战报段可读()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DebugSnapshot().ShouldContain(
            " | R occasion=none outcome=none a=0 b=0 dead=0 wounded=0 looted=0 ticket=0 tick=-1",
            Case.Sensitive, "没打过仗也有一行占位。");

        world.OnDocked(BigMapGrid.PoorCity);
        ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out _).ShouldBeTrue();
        world.Advance(ArenaTime.StepsPerGameHour * 2);

        var snapshot = world.DebugSnapshot();
        snapshot.ShouldContain(" | R occasion=match ", Case.Sensitive, "场合可读。");
        snapshot.ShouldContain(" outcome=a_win ", Case.Sensitive, "胜负可读。");
        snapshot.ShouldContain(" a=2 b=1 ", Case.Sensitive, "双方上场可读。");
        snapshot.ShouldContain(" dead=0 wounded=1 ", Case.Sensitive, "死伤可读。");
        snapshot.ShouldContain(" ticket=", Case.Sensitive, "门票收入可读。");
        world.ReplaySnapshot().ShouldStartWith(snapshot, Case.Sensitive, "重放快照带同一份 R 段。");
    }

    /// <summary>
    /// A4 回归：固定竞技场拆掉、玩家新建 bN 竞技场之后，办赛仍能开 ——
    /// 开赛校验与入场点都不能只认固定的 <c>arena</c>。
    /// </summary>
    [Fact]
    public void 新建bN竞技场之后仍能开赛_入场点跟着新建筑走()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        ArenaSetup.Inject(world, "RemoveBuilding", ArenaWorld.ArenaId, out _, out var removeWhy)
            .ShouldBeTrue($"固定竞技场该能拆：{removeWhy}");
        world.FindByBlueprint("Arena").ShouldBeEmpty("固定竞技场已经不在册。");

        var placed = world.Deck.PlaceBuilding("Arena", -8, 3);
        placed.Ok.ShouldBeTrue($"新建竞技场放得下：{placed.Reason}");
        placed.BuildingId.ShouldNotBe(ArenaWorld.ArenaId, "新建的是 bN。");
        world.FindByBlueprint("Arena").ShouldBe(placed.BuildingId, "按蓝图要能找到新建那一栋。");

        ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out var matchWhy)
            .ShouldBeTrue($"bN 竞技场开不了赛：{matchWhy}");
        world.Battle.Occasion.ShouldBe(BattleOccasion.Match);

        // 入场点相对解析出的那一栋大门取：新竞技场的门点不同于开局那一栋，
        // 算出来的西侧入场点也就不该还是固定坐标。
        var door = world.DoorDeckOf(placed.BuildingId);
        DeckGeometry.ArenaWest(door).ShouldNotBe(DeckGeometry.ArenaWest(),
            "bN 竞技场的入场点要跟着新建筑走，不能落回固定竞技场的老坐标。");
    }

    /// <summary>
    /// ticket 08：竞技场菜单那一发 —— 少于两名角斗士时<b>不发命令</b>，原因写「角斗士不足」。
    /// </summary>
    [Fact]
    public void 少于两名角斗士时开赛被拒_不发命令()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        var gladiators = world.Roster.Units.Where(u => u.Role == UnitRole.Gladiator).ToList();
        gladiators.Count.ShouldBe(3);
        for (var i = 1; i < gladiators.Count; i++)
        {
            ArenaSetup.Inject(world, "ConvertRole", $"{gladiators[i].Id}|Worker", out _, out _)
                .ShouldBeTrue();
        }

        // 菜单那一发只走逻辑层这一口（挑人与分队在 TryBuildMatchArgs，菜单只负责转发）。
        // 测试直接断言它的返回，不在测试里再复刻一遍菜单的 if / 下发逻辑 ——
        // 凑不出两队时 matchArgs 是空串，压根没有能发出去的 StartMatch 命令。
        var built = BattleSim.TryBuildMatchArgs(world, out var matchArgs, out var why);

        built.ShouldBeFalse("只剩一个角斗士，凑不出两队。");
        matchArgs.ShouldBeEmpty("被拒时没有可下发的命令参数。");
        why.ShouldBe("角斗士不足");
        world.Battle.Occasion.ShouldBe(BattleOccasion.None);
        world.Battle.LastReport.ShouldBeNull();
    }
}
