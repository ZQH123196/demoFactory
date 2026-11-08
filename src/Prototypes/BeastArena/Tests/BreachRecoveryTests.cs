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
/// 输的路径与负债闭环 —— <c>design.md</c> 12.1 的第二条命题。
///
/// <para><b>ticket 06 改了口径</b>：城破不再停摆三个游戏日，而是<b>当格立即解救并记债</b>。
/// 本文件钉的是那之后的三件事：① 战损与债在同一格落定；② 经营没有被冻结
/// （修房 / 搬运 / 治伤 / 土壤回长照常，且根本没有停摆期）；③ 债务、通缉与拒停继续生效，
/// 失败之后还能补一次防守。</para>
///
/// <para><b>三处不是写路径的口子，出处写在这里</b>：打空建筑耐久用
/// <see cref="ArenaWorld.DamageBuilding"/>（词表里没有「打一下建筑」这一发，防守那一格是
/// <c>BattleSim</c> 内部走同一口）；制造一具尸体用 <see cref="BattleSim.ApplyDamage"/>
/// （交战那一摊唯一的写血口）；造一笔债用 <see cref="TownAccounts.AccrueDebt"/>
/// （词表里没有「欠钱」这一发，攻破折债那一格是 <c>ArenaWorld.ReportBreach</c> 内部走同一口）。
/// 其余每一发命令都走 <c>ArenaSetup.Inject</c>。</para>
/// </summary>
public class BreachRecoveryTests
{
    private const int Seed = 7;

    /// <summary>修房那一格的目标：教堂在攻破的三栋之外（毁掉的是竞技场、饭店、训练场）。</summary>
    private const string RepairTargetId = ArenaWorld.ChurchId;

    /// <summary>
    /// 摆上「停摆期该照常的那几摊」，每一样都要躲开攻破那一格会毁掉 / 掳走的那批：
    /// 毁掉的是竞技场、饭店、训练场；掳走的是名单序号前 7 名（w1..w7）。
    /// </summary>
    private static void OccupyTheFirstSeven(ArenaWorld world)
    {
        foreach (var (id, post) in new[]
        {
            ("w1", ArenaWorld.TownHallId), ("w2", ArenaWorld.TownHallId),
            ("w3", ArenaWorld.TownHallId), ("w4", ArenaWorld.GranaryId),
            ("w5", ArenaWorld.ChurchId), ("w6", "training_ground"),
            ("w7", "training_ground"),
        })
        {
            ArenaSetup.Inject(world, "AssignPost", $"{id}|{post}", out _, out var why).ShouldBeTrue(why);
        }
    }

    /// <summary>攻破那一局，外加攻破之前那本账的读数（用来复算 30% 那一扣）。</summary>
    private readonly record struct BreachRun(ArenaWorld World, double OreBeforeBreach, double MaterialBeforeBreach);

    /// <summary>停靠贫城 → 地下采矿摆上 → 修房与搬运摆上 → 拔锚防守 → 打空镇政府 → 走到攻破触发。</summary>
    private static BreachRun BreachInDefence()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 开局 12 人只够摆出「前七名在岗 + 一个搬运人」。这一局要为停摆期验三条矿工、一具尸体
        // 与两笔搬运，所以从册子这一口另发六名工人（与 HaulingTests 的探针同一口，不是词表命令）。
        foreach (var id in new[] { "w9", "w10", "w11", "w12", "w13", "w14" })
        {
            world.Roster.Add(id, UnitRole.Worker);
        }

        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Advance(ArenaTime.StepsPerGameMinute * 20);

        // ① 地下：挖开 0 号象限第 1 层派满矿工，另挖开 1 号象限第 1 层当对照。
        //    矿工用 w9..w11，两格都躲得开攻破那一格。
        ArenaSetup.Inject(world, "ExcavateSoil", "0|1", out _, out _).ShouldBeTrue();
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue("0 号象限第 1 层挖开了。");
        foreach (var id in new[] { "w9", "w10", "w11" })
        {
            ArenaSetup.Inject(world, "AssignPost", $"{id}|soil:0_1", out _, out var why).ShouldBeTrue(why);
        }

        world.Soil.MinersAt(0, 1).ShouldBe(3, "三名矿工在岗。");

        ArenaSetup.Inject(world, "ExcavateSoil", "1|1", out _, out _).ShouldBeTrue();
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(1, 1).ShouldBeTrue("1 号象限第 1 层挖开了。");

        // ② 采矿跑满一个整游戏小时，电梯把货送进仓库（攻破之前该有矿进账）。
        world.Advance(ArenaTime.StepsPerGameHour * 2);
        world.Soil.OreAccumulatedAt(0, 1).ShouldBeGreaterThan(0.0, "地下有货。");
        world.Town.Ore.ShouldBeGreaterThan(ArenaTuning.OpeningOre, "电梯把矿石送进镇政府仓库。");

        // ③ 修房与搬运：这两样要还在攻破之后的途中，才验得到「停摆期照常」。
        OccupyTheFirstSeven(world);
        world.DamageBuilding(RepairTargetId, 60.0);
        ArenaSetup.Inject(world, "RepairBuilding", RepairTargetId, out _, out var repairWhy)
            .ShouldBeTrue($"修房发得出去：{repairWhy}");
        world.Deck.IsRepairing(RepairTargetId).ShouldBeTrue("在途的修房。");

        // 取货点放在甲板东南角：离发令那几人的落点二十米开外，停摆期之前绝不可能走到 ——
        // 「停摆期搬运没冻」这一格要靠「起点在取货点二十米外」才读得出来。
        world.Haul.Enqueue(ArenaWorld.Order(
            HaulLevel.GroundCargoToWarehouse, "cargoX", new NavPoint(11.0, 11.0),
            ArenaWorld.TownHallId, 1.0, world.Tick));
        world.Advance(1);
        var carrier = world.Roster.Units.First(u => world.CarryingOf(u.Id) is not null);
        carrier.Id.ShouldBe("w8", "前七名都在岗上，所以这一单落到 w8 手上（攻破不掳他）。");

        // ④ 拔锚防守 → 打空镇政府 → 走满一个游戏分钟让攻破触发。
        ArenaSetup.Inject(world, "PullAnchor", "", out _, out _).ShouldBeTrue();
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence, "拔锚即开 2 游戏小时防守。");

        var oreBefore = world.Town.Ore;
        var materialBefore = world.Town.SpecialMaterial;

        world.DamageBuilding(ArenaWorld.TownHallId, 200.0);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.TownHallBreached.ShouldBeFalse("城破当格就由城卫队解救，攻破状态不跨步。");
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "当场结束防守。");
        world.CarryingOf(carrier.Id).ShouldNotBeNull("在途那一单还在。");
        return new BreachRun(world, oreBefore, materialBefore);
    }

    [Fact]
    public void 攻破那一格_镇政府当格解救_毁三栋掳七人折一点五倍债()
    {
        var run = BreachInDefence();
        var world = run.World;

        // ① 城破当格已解救：镇政府留在册上、耐久回到 100，快照不再标 breached。
        world.Construction.Find(ArenaWorld.TownHallId)
            .ShouldNotBeNull("镇政府还在建造册上 —— spec 要的是「锁 0 耐久且不从册移除」，解救后回满。");
        world.Construction.Find(ArenaWorld.TownHallId)!.Health
            .ShouldBe(ArenaTuning.RescueTownHallDurability, 1e-9, "城卫队当格把耐久恢复到 100。");
        world.DebugSnapshot().ShouldNotContain("breached=1", Case.Sensitive, "当格已解救，不再标攻破。");
        world.DebugSnapshot().ShouldContain("B town_hall ", Case.Sensitive, "镇政府仍在建造册那一段里。");

        // ② 正好三栋普通建筑被毁，且是造价从高到低、同造价按 id 字典序那三栋。
        world.WarLossBuildings.Count.ShouldBe(ArenaTuning.WarLossBuildings, "正好三栋。");
        world.WarLossBuildings.ShouldBe(
            [ArenaWorld.ArenaId, ArenaWorld.RestaurantId, "training_ground"], Case.Sensitive);
        foreach (var id in world.WarLossBuildings)
        {
            world.Construction.Find(id).ShouldBeNull($"{id} 已被移出建造册。");
        }

        // ③ 前 7 名镇民被掳走（这里七名都在岗上，占的是建造册里在的岗位）。
        world.TakenPeople.Count.ShouldBe(ArenaTuning.WarLossTakenPeople, "正好七人。");
        world.TakenPeople.ShouldBe(["w1", "w2", "w3", "w4", "w5", "w6", "w7"], Case.Sensitive);
        world.TakenPeople.All(id => world.Roster.Find(id)!.LeftTown).ShouldBeTrue("按名单序号从头取。");
        world.TakenPeople.All(id => world.Roster.Find(id)!.Post.Length == 0).ShouldBeTrue("掳走之前腾岗位。");

        // ④ 仓库矿石与特殊材料各扣 30%（向下取整），扣的是攻破那一刻账上的数。
        world.Town.Ore.ShouldBe(
            run.OreBeforeBreach - Math.Floor(run.OreBeforeBreach * ArenaTuning.WarLossGoodsShare), 1e-9,
            $"矿石扣 {Math.Floor(run.OreBeforeBreach * ArenaTuning.WarLossGoodsShare):0.##}"
            + $"（攻破前 {run.OreBeforeBreach:0.##} 的 30%，向下取整）。");
        world.Town.SpecialMaterial.ShouldBe(
            run.MaterialBeforeBreach - Math.Floor(run.MaterialBeforeBreach * ArenaTuning.WarLossGoodsShare), 1e-9,
            "特殊材料同样扣 30%（向下取整）。");

        // ⑤ 债额 = max(500, round(战损估值 × 1.5))，估值由那一格按造价 + 人头 + 货重算；
        //    城卫队当格把它记进最近那家势力的负债账，所以待记的那一格是 0。
        var debt = world.Town.TotalDebt;
        debt.ShouldBeGreaterThanOrEqualTo(ArenaTuning.DebtFloor, "债额有下界 500。");
        debt.ShouldBe(Math.Round(debt), 1e-9, "债额是整数金币。");
        world.PendingDebt.ShouldBe(0.0, 1e-9, "当格已记进负债账，不留待记的那一份。");
        world.Town.CreditorOf(ArenaWorld.PoorFactionId)!.Accrued.ShouldBe(debt, 1e-9,
            "巨兽停在贫城边，债记给最近的贫城行会。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());

        // ⑥ 没有停摆期：到期步号 0、不在停摆、巨兽已恢复航行。
        world.StandstillUntilTick.ShouldBe(0, "ticket 06 起不再排三个游戏日的停摆。");
        world.InStandstill.ShouldBeFalse("城破当格就解除。");
        world.PulledAnchor.ShouldBeFalse("解救后恢复航行，拔锚那一闩清掉。");
    }

    [Fact]
    public void 城破当格解救_修房搬运治伤土壤回长照常()
    {
        var world = BreachInDefence().World;

        world.InStandstill.ShouldBeFalse("城破当格解除，根本没有停摆期。");
        world.TownHallBreached.ShouldBeFalse();
        world.PulledAnchor.ShouldBeFalse("巨兽恢复向原目标城航行的发令（拔锚那一闩清掉）。");

        // 在途的修房：攻破没有把它停掉，10 个游戏分钟内照常完工（+50 耐久）。
        var healthBeforeRepair = world.Construction.Find(RepairTargetId)!.Health;
        world.Advance(ArenaTime.StepsPerGameMinute * 10);
        world.Construction.Find(RepairTargetId)!.Health.ShouldBe(
            healthBeforeRepair + ArenaTuning.RepairDurability, 1e-9, "修房照常完工。");
        world.Deck.IsRepairing(RepairTargetId).ShouldBeFalse("活已了结。");

        // 搬运照常：再排一单，下一格就有人领走，人真的朝取货点走。
        var pickup = new NavPoint(11.0, 11.0);
        world.Haul.Enqueue(ArenaWorld.Order(
            HaulLevel.GroundCargoToWarehouse, "cargoY", pickup,
            ArenaWorld.TownHallId, 1.0, world.Tick));
        world.Advance(1);
        var spare = world.Roster.Units.First(u => world.CarryingOf(u.Id)?.ItemId == "cargoY");
        var spareBefore = new NavPoint(spare.DeckX, spare.DeckZ);
        for (var i = 0; i < 200; i++) world.Advance(1);
        new NavPoint(spare.DeckX, spare.DeckZ).DistanceTo(pickup)
            .ShouldBeLessThan(spareBefore.DistanceTo(pickup), "搬运人朝取货点走，不是站着不动。");

        // 治伤复活照常：一个游戏小时后起身、血回满。
        var fallen = world.Roster.Find("w12")!;
        fallen.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, fallen, 1.0);
        fallen.State.ShouldBe(UnitState.Dead, "Hp 落到 −100 以下当场死。");
        var corpse = world.Items.LiveCorpses.Single(c => c.Payload == "w12");
        ArenaSetup.Inject(world, "ReviveCorpse", corpse.Id, out _, out var reviveWhy)
            .ShouldBeTrue($"教堂复活发得出去：{reviveWhy}");
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Roster.Find("w12")!.State.ShouldBe(UnitState.Standing, "复活照常把人救起来。");
        world.Roster.Find("w12")!.Hp.ShouldBe(ArenaTuning.UnitMaxHp, 1e-9);

        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 债记进最近势力_失败之后可以再拔一次锚()
    {
        var world = BreachInDefence().World;
        var debt = world.Town.TotalDebt;
        var faction = ArenaWorld.PoorFactionId;

        debt.ShouldBeGreaterThan(0.0, "当格记了债。");
        world.Town.CreditorOf(faction)!.Accrued.ShouldBe(debt, 1e-9,
            "巨兽停在贫城边，最近的城镇势力是贫城那家。");
        world.Town.CreditorOf(faction)!.Outstanding.ShouldBe(debt, 1e-9);
        world.PendingDebt.ShouldBe(0.0, 1e-9, "待记的债额清零。");

        // 恢复航行之后重新停靠上贫城，还能再拔一次锚 —— 失败后的经营补救路径。
        for (var i = 0; i < ArenaTime.StepsPerGameDay && !world.IsDocked; i++) world.Advance(1);
        world.IsDocked.ShouldBeTrue("解救之后重新停靠上贫城。");
        ArenaSetup.Inject(world, "PullAnchor", "", out _, out var pullWhy)
            .ShouldBeTrue($"补救那一场拔得出去：{pullWhy}");
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence, "补救那一场就是普通防守。");

        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 债超阈值被通缉_债主势力的城镇拒停_另一座城与野外锚地照常停()
    {
        var world = BreachInDefence().World;
        var debt = world.Town.TotalDebt;

        // 债超 3000，通缉判在每个整游戏日那一格。
        debt.ShouldBeGreaterThan(ArenaTuning.WantedDebtThreshold, "这一局折出来的债本来就过线。");
        world.Town.TotalDebt.ShouldBe(debt, 1e-9, "解救那一步把这笔债记进了负债账。");
        world.AdvanceTo(((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay);
        world.Wanted.ShouldBeTrue("总负债过阈值，置通缉。");

        // 通缉只拒债主势力名下城镇的停靠；野外锚地与另一座城照常停。
        ArenaSetup.Inject(world, "Dock", BigMapGrid.PoorCity.Id, out _, out var refused)
            .ShouldBeFalse("贫城属债主势力名下。");
        refused.ShouldStartWith($"rejected: {RejectReason.WantedRefused}", Case.Sensitive);

        // 航行那两条先把饱食度摆满：验的是通缉拒停，不是饿着会不会脱队。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        ArenaSetup.Inject(world, "Dock", BigMapGrid.WildAnchor.Id, out _, out _)
            .ShouldBeTrue("野外锚地无主，照常停。");
        world.AdvanceTo(world.Tick + 8000);
        world.DockedCityId.ShouldBe(BigMapGrid.WildAnchor.Id, "野外锚地真的停下来了。");

        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        ArenaSetup.Inject(world, "Dock", BigMapGrid.RichCity.Id, out _, out _)
            .ShouldBeTrue("富城不是这家债主名下的，照常停。");
        world.AdvanceTo(world.Tick + 9000);
        world.DockedCityId.ShouldBe(BigMapGrid.RichCity.Id, "富城真的停下来了。");

        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 三种还债策略与手动还债_都走SetDebtPolicy与PayDebt两个动词()
    {
        foreach (var policy in new[] { DebtPolicy.None, DebtPolicy.AutoOrder, DebtPolicy.AutoTarget })
        {
            var world = ArenaWorldBuilder.New(Seed).Build();
            world.DockedAt(BigMapGrid.PoorCity.Id);
            world.Advance(ArenaTime.StepsPerGameHour * 2);   // 让街上有人，办赛才有观众
            world.Town.AccrueDebt(ArenaWorld.PoorFactionId, 400.0);
            world.Town.AccrueDebt(ArenaWorld.RichFactionId, 900.0);

            var priority = policy == DebtPolicy.AutoTarget ? ArenaWorld.RichFactionId : "-";
            ArenaSetup.Inject(world, "SetDebtPolicy", $"{policy}|{priority}", out _, out var setWhy)
                .ShouldBeTrue($"{policy} 设得下：{setWhy}");
            world.Town.Policy.ShouldBe(policy);
            world.Town.PriorityFactionId.ShouldBe(
                policy == DebtPolicy.AutoTarget ? ArenaWorld.RichFactionId : string.Empty);

            // 进账那一口：办赛门票结算走 SettleAutoRepayment。
            ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out _).ShouldBeTrue();
            world.Advance((int)(ArenaTime.StepsPerGameHour * 1.6) + 20);

            var poorPaid = world.Town.CreditorOf(ArenaWorld.PoorFactionId)!.Repaid;
            var richPaid = world.Town.CreditorOf(ArenaWorld.RichFactionId)!.Repaid;

            switch (policy)
            {
                case DebtPolicy.None:
                    poorPaid.ShouldBe(0.0, 1e-9, "None 一分不还。");
                    richPaid.ShouldBe(0.0, 1e-9, "None 一分不还。");
                    break;
                case DebtPolicy.AutoOrder:
                    poorPaid.ShouldBeGreaterThan(0.0, "AutoOrder 进账就还，名单上第一家先还。");
                    richPaid.ShouldBe(0.0, 1e-9, "钱不够轮到第二家。");
                    break;
                default:
                    richPaid.ShouldBeGreaterThan(0.0, "AutoTarget 优先还指定那一家。");
                    poorPaid.ShouldBe(0.0, 1e-9, "优先那一家没还完之前轮不到别家。");
                    break;
            }

            // 手动还债：钱不够一子儿不动。
            var coinBeforeOver = world.Town.Coin;
            ArenaSetup.Inject(world, "PayDebt", $"{ArenaWorld.RichFactionId}|999999", out _, out var overWhy)
                .ShouldBeFalse();
            overWhy.ShouldStartWith($"rejected: {RejectReason.InsufficientCoin}", Case.Sensitive);
            world.Town.Coin.ShouldBe(coinBeforeOver, 1e-9, "拒绝一寸不动。");
            world.Town.IsConserved().ShouldBeTrue($"{policy} 拒绝后仍然要平。");

            // 手动还债：还够就只扣要还的那一份。
            var outstanding = world.Town.CreditorOf(ArenaWorld.RichFactionId)!.Outstanding;
            var coinBeforePay = world.Town.Coin;
            var amount = Math.Min(outstanding, coinBeforePay);
            amount.ShouldBeGreaterThan(0.0, "这一局该还得起一笔，好验手动那一档。");

            ArenaSetup.Inject(world, "PayDebt",
                $"{ArenaWorld.RichFactionId}|{amount.ToString(CultureInfo.InvariantCulture)}",
                out _, out var payWhy).ShouldBeTrue($"手动还债：{payWhy}");
            world.Town.CreditorOf(ArenaWorld.RichFactionId)!.Repaid.ShouldBe(
                richPaid + amount, 1e-9, "手动还的记进那一家。");
            world.Town.Coin.ShouldBe(coinBeforePay - amount, 1e-9, "金币账按还掉的数扣。");

            // 还给一家不存在的势力 / 给非正数，都按拒绝走且账不动。
            ArenaSetup.Inject(world, "PayDebt", $"no_such_faction|10", out _, out var who)
                .ShouldBeFalse();
            who.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive);
            ArenaSetup.Inject(world, "PayDebt", $"{ArenaWorld.RichFactionId}|0", out _, out var zero)
                .ShouldBeFalse();
            zero.ShouldStartWith($"rejected: {RejectReason.InvalidArgs}", Case.Sensitive);
            world.Town.IsConserved().ShouldBeTrue($"{policy}：{world.Town.ConservedReport()}");
        }
    }
}
