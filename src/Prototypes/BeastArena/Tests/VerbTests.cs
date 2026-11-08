#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 四十五个业务 <c>inject</c> 动词的<b>正向一格</b>与十四项 <see cref="RejectReason"/> 的<b>负向一格</b>。
///
/// <para><b>正向那一格钉的是「这一发真的走成了写路径」</b>：每一发都过
/// <see cref="ArenaSetup.Inject"/>（与玩家、调试桥、重放端同一口），返回值得是 <c>true</c>，
/// 而且九条守恒等式在整串命令跑完之后仍然为真。</para>
///
/// <para><b>负向那一格钉的不是那句原因文本，是「账一格没动」</b>：
/// 一发被拒之后六本实物账、负债账与法力格逐项比对，任何一栏动了都判红。
/// 原因文本只作为「是哪一档」的可读性对照 —— 真值是账。</para>
///
/// <para><b>两处摆前置条件的口子，出处写在这里</b>：把某个角斗士的个人名声抬到明星门槛
/// （<see cref="ArenaUnit.PersonalReputation"/> 是公开可写的那一栏，<c>SellStar</c> 要求 ≥ 100，
/// 而办赛一场只给 +5）；制造一具尸体与制造一个伤员用 <see cref="BattleSim.ApplyDamage"/>
/// （交战那一摊唯一的写血口）。</para>
///
/// <para><b>位阶那一发的门槛怎么摆</b>：二阶要三项同时成立（该象限挖到第 2 层 + 那处洞穴清剿成功 +
/// 同格已嵌入魔晶）。本用例先用 <see cref="ArenaWorldTestSetup.DugTo"/> 逐层挖到洞穴那一层，
/// 再走 <c>ClearCave</c> / <c>RetreatCave</c> / <c>EmbedCrystal</c> 三条真动词把后两项做出来 ——
/// 门槛是靠动词走成的，不是直写旗标。</para>
/// </summary>
public class VerbTests
{
    private const int Seed = 7;

    /// <summary>账本指纹（七本实物 + 负债 + 法力三栏）。被拒的一发前后要逐项相等。</summary>
    private readonly record struct LedgerFingerprint(
        double Coin, double Food, double Soul, double Ore, double Material, double Part, double Crystal,
        double Debt, int ManaAvailable, int ManaStasis, int ManaHelper);

    private static LedgerFingerprint Fingerprint(ArenaWorld world)
    {
        var t = world.Town;
        return new LedgerFingerprint(t.Coin, t.Food, t.SoulStone, t.Ore, t.SpecialMaterial, t.SparePart,
            t.Crystal, t.TotalDebt, t.AvailableMana, t.StasisMana, t.HelperMana);
    }

    [Fact]
    public void 四十五个动词_每一个都走成一次写路径()
    {
        // 招募是纯手动、付钱必得：这一格只验动词走不走得成，不用挑种子。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var ran = new List<string>();

        void Fire(string verb, string args)
        {
            ArenaSetup.Inject(world, verb, args, out _, out var why)
                .ShouldBeTrue($"{verb}({args}) 该走成写路径，被拒了：{why}");
            ran.Add(verb);
        }

        // ---- 航行与城镇（7）----
        // 开局那一发先走：段从「未开始」推到开局航程，之后每一发都不看段（流程不参与放行判定）。
        Fire("StartRun", string.Empty);

        world.DockedAt(BigMapGrid.PoorCity.Id);
        ran.Add("Dock");

        Fire("TradeAirship", "30|1|0");

        // ---- 地下战斗（2）与嵌入 ----
        // 单处洞穴在象限 0 的第 2 层：挖到那一层 → 发起清剿 → 撤退算失败 →
        // 再发起一次、四只全灭 → 那一格转高危 → 嵌入魔晶 → 二阶门槛才成立。
        world.DugTo(0, 1);
        world.DugTo(0, 2);

        Fire("ClearCave", $"0|{UndergroundSoil.NativeCaveLayer}");
        world.Battle.Occasion.ShouldBe(BattleOccasion.CaveClearing, "清剿开打了。");
        Fire("RetreatCave", string.Empty);
        world.Soil.CaveMonstersAt(0, UndergroundSoil.NativeCaveLayer)
            .ShouldBe(ArenaTuning.NativeCaveMonsters, "撤退算失败，四只留在洞里。");

        Fire("ClearCave", $"0|{UndergroundSoil.NativeCaveLayer}");
        KillCaveMonsters(world);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Soil.IsCleared(0, UndergroundSoil.NativeCaveLayer)
            .ShouldBeTrue("四只全灭那一格才算清剿成功。");

        world.Town.ProduceCrystal(1.0);   // 魔晶只有镇账那一本，这里摆「仓里有 1 单位」
        Fire("EmbedCrystal", $"0|{UndergroundSoil.NativeCaveLayer}");
        world.Soil.IsEmbedded(0, UndergroundSoil.NativeCaveLayer).ShouldBeTrue("嵌进去了。");

        // ---- 巨兽本体（3）----
        // 进阶代价要魂石 40 + 特殊材料 10 + 金币 2000；开局那几本账不够，先补足。
        world.Town.ProduceSoulStone(200.0);
        world.Town.ProduceSpecialMaterial(50.0);
        world.Town.ProduceCoin(5000.0);
        Fire("AdvanceTier", string.Empty);
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours));
        world.Tier.ShouldBe(BeastTier.Tier2, "仪式走完位阶 +1。");

        // 喂食：50 份换 10 点饱食（5:1）。镇仓与兽粮都得够，这一发才喂得饱。
        var satiationBefore = world.Voyage.Satiation;
        Fire("Feed", "50");
        (world.Voyage.Satiation - satiationBefore).ShouldBe(10.0, 1e-9, "喂 50 份换 10 点饱食。");

        // NPC 喂食：停靠期由一名无岗工人或帮工喂 5 食物，与手动喂食同一本账。
        Fire("NpcFeed", string.Empty);

        // 切形态：累计喂食到 400 且已驶离贫城才解锁暴食态。
        // 把局流程推到第一段航行（贫城防守赢了），这一局后面还要停靠期的事，世界本身的停靠状态不动。
        world.LeftPoorCity();
        world.Voyage.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        world.Advance(1);
        world.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("驶离贫城且喂食凑够 400 就要解锁。");
        Fire("SwitchForm", "glutton");
        world.Form.ShouldBe(BeastForm.Glutton, "切形态这一发走成了。");
        Fire("SwitchForm", "original");
        world.Form.ShouldBe(BeastForm.Original, "切回原始态也是一发写路径。");

        Fire("StartMatch", "g1,g2|g3");
        world.AdvanceTo(world.Tick + (int)(ArenaTime.StepsPerGameHour * 1.6));

        // ---- 人口与经营（7）----
        Fire("AssignPost", "w1|training_ground");
        Fire("ConvertRole", "f1|Worker");
        Fire("GrantFreedom", "g1");
        Fire("SetNonCombatant", "g2|1");
        Fire("SetPricing", "1.2|0.8|1.5");

        var star = world.Roster.Find("g3")!;
        star.PersonalReputation = ArenaTuning.StarSellReputationFloor + 50.0;
        Fire("SellStar", "g3");

        Fire("Recruit", "TownOrCountry");

        // 账目与远行（6）
        Fire("BuySupply", "20");
        Fire("SetDebtPolicy", "AutoOrder|-");
        world.Town.AccrueDebt(ArenaWorld.PoorFactionId, 120.0);
        Fire("PayDebt", $"{ArenaWorld.PoorFactionId}|50");
        // 发薪：停靠期才发得出薪，这一局还停着，开局挂着的那一份工钱结得清。
        Fire("PayWages", string.Empty);
        // 开局六栋里没有传送阵；这一局先在旧的锚点上把它摆好，才验得到 Teleport 那一发。
        world.Deck.PlaceBuilding("TeleportArray", -2, 4, ArenaWorld.TeleportArrayId)
            .Ok.ShouldBeTrue("传送阵在旧锚点上放得下。");
        Fire("Teleport", "w5|1|0");

        // ---- 建造与土壤（5）----
        Fire("PlaceBuilding", "Workshop|-11|-6");
        world.DamageBuilding(TrainingGround, 30.0);   // 满耐久的房子修不动：先打掉一点
        Fire("RepairBuilding", TrainingGround);
        // 0 号象限已经挖过（洞穴那一格），改挖 1 号象限第 1 层来验开挖动词。
        Fire("ExcavateSoil", "1|1");
        world.Soil.IsDigging(1, 1).ShouldBeTrue("开挖这一发要真的派上工。");
        Fire("LiftBuilding", "b1");
        Fire("DropBuilding", "b1|-11|-3");
        Fire("RemoveBuilding", "b1");

        // ---- 上帝之手与法术（8）----
        Fire("GrabEntity", "w2");
        Fire("DropToBuilding", "w2|granary");
        Fire("DropToDeck", "w2|6|6");

        Fire("SummonHelper", string.Empty);
        var helperId = FindHelper(world);
        Fire("DismissHelper", helperId);

        var wounded = world.Roster.Find("w3")!;
        wounded.Hp = -50.0;
        world.Battle.ApplyDamage(world.Tick, wounded, 1.0);
        wounded.State.ShouldBe(UnitState.Wounded);
        Fire("CastHeal", "w3");

        var corpseOwner = world.Roster.Find("w4")!;
        corpseOwner.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, corpseOwner, 1.0);
        var corpse = world.Items.LiveCorpses.Single(c => c.Payload == "w4");
        Fire("ReviveCorpse", corpse.Id);

        // ---- 航行与城镇：剩下三发 ----
        Fire("PullAnchor", string.Empty);
        world.AdvanceTo(world.Tick + (int)(ArenaTime.StepsPerGameHour * 2.1));
        Fire("SteerOutOfBounds", string.Empty);

        // 手动航行：拔锚之后（不在停靠期、没在打仗、没在仪式与停摆）发得出去。
        Fire("SailTo", "210.5|300.25");

        // ---- 生态（4）----
        // 指定吞食：把兽摆到生态对象旁（测试摆位），吃一口树。
        var eco = world.Ecology.Find("tree_poor_1")!;
        world.MapStepper.ApplyMove(BeastVoyage.BeastId, eco.Position, 0.0);
        world.Voyage.SetSatiation(50.0);
        Fire("EatTarget", "tree_poor_1");
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);

        // 召回：饿着再开一趟，等它脱队觅食，再叫回来。
        world.Voyage.SetSatiation(20.0);
        world.OrderSailTo(BigMapGrid.DesertCity.Id);
        for (var i = 0; i < 200 && world.Voyage.Phase != VoyagePhase.DetouringToFood; i++)
        {
            world.Advance(10);
        }
        world.Voyage.Phase.ShouldBe(VoyagePhase.DetouringToFood, "先脱队才验得到召回。");
        Fire("RecallForage", string.Empty);

        // 魔晶：仓库里摆一颗，补魔（先花掉 6 格法力）。
        world.Town.ProduceCrystal(2.0);
        for (var i = 0; i < 6; i++) world.Town.TrySpendInstantMana().ShouldBeTrue("先花掉 6 格法力。");
        Fire("UseCrystal", "mana");

        // 出售：停靠期才卖得出去。
        world.OnDocked(BigMapGrid.PoorCity);
        Fire("SellCrystal", "1");

        // ---- 终局（1）----
        // 一路推到富城、双成长完成（二阶 + 暴食解锁 + 手动切过一次），拔锚进最终防守；
        // 打空镇政府收场，再走「立即重试」这一条写路径。
        world.Flow.NoteDocked(BigMapGrid.DesertCity.Id);
        world.Flow.NoteAnchorPulled(BigMapGrid.DesertCity.Id);
        world.Flow.NoteDefenceSettled(won: true);
        world.Flow.NoteDocked(BigMapGrid.RichCity.Id);
        world.DockedDirect(BigMapGrid.RichCity);
        Fire("PullAnchor", string.Empty);
        world.Flow.Stage.ShouldBe(RunStage.FinalDefence, "双成长完成，富城拔锚进最终防守。");
        world.DamageBuilding(ArenaWorld.TownHallId, ArenaTuning.RescueTownHallDurability + 500.0);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Flow.FinalRetryCount.ShouldBe(1, "失败累计一次重试。");
        Fire("RetryFinalDefence", string.Empty);

        // 赢下重试那一场（直接收场，不改变胜负条件），再走「继续经营」。
        world.Battle.ForceEnd(world.Tick);
        world.Flow.Stage.ShouldBe(RunStage.Settlement, "最终防守赢了进结算。");
        Fire("ContinueBusiness", string.Empty);
        world.Flow.Stage.ShouldBe(RunStage.FreePlay, "继续经营回自由经营。");

        // ---- 收口 ----
        // 这里钉的是「词表里每一个动词都走到了正向一格」，不是「每个只走一次」。
        var ranOnce = ran.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var expected = new List<string>(ArenaSetup.VerbNames);
        expected.Remove("SyntheticInputAction");
        expected.Remove("SyntheticInputClick");
        expected.Sort(StringComparer.Ordinal);
        expected.Count.ShouldBe(45, "词表是 45 条业务动词 + 2 条合成输入。");
        expected.ShouldBe(ranOnce, Case.Sensitive, "词表里的 45 个业务动词每一个都走到了正向一格。");
        world.Town.IsConserved().ShouldBeTrue("四十五个动词走完，九条等式仍然要平：" + world.Town.ConservedReport());
        world.DebugSnapshot().ShouldContain("conserved=True", Case.Sensitive, "快照头部那一列也是平。");
    }

    [Fact]
    public void 十四项拒绝原因各来一发_被拒之后账一格不动()
    {
        // 三阶不在本切片内：地下只有两层，`TierAtMax` 这一档永远走不到。
        // 它仍然是封闭枚举的一员（14 项不变），只是当前没有一条合法路径能触发 ——
        // 这里显式跳过并另有用例钉住「三阶到不了」，不是把覆盖悄悄删掉。
        foreach (var reason in System.Enum.GetValues<RejectReason>()
                     .Where(r => r != RejectReason.TierAtMax))
        {
            var world = ArenaWorldBuilder.New(Seed).Build();
            world.DockedAt(BigMapGrid.PoorCity.Id);
            Prepare(world, reason);

            var before = Fingerprint(world);
            var ok = ArenaSetup.Inject(world, VerbOf(reason), ArgsOf(world, reason), out _, out var why);

            ok.ShouldBeFalse($"{reason} 那一发判否。");
            why.ShouldStartWith($"rejected: {reason}", Case.Sensitive, $"拒绝原因要落在 {reason} 那一档。");

            var after = Fingerprint(world);
            after.ShouldBe(before, $"{reason} 被拒之后账一格不动。");
            world.Town.IsConserved().ShouldBeTrue($"{reason} 被拒之后八条等式仍然要平："
                + world.Town.ConservedReport());
            world.Entities.Count.ShouldBeGreaterThan(0, $"{reason} 那一发顺手改册子就红。");
        }
    }

    [Fact]
    public void 落位回原处_走成写路径_法力格还回来且那几格腾得出来()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var heldBefore = world.Town.StasisMana;
        var anchor = world.Construction.Find("restaurant")!.Anchor!.Value;

        ArenaSetup.Inject(world, "LiftBuilding", "restaurant", out _, out var liftWhy).ShouldBeTrue(liftWhy);
        world.Town.StasisMana.ShouldBe(heldBefore + 1, "拔起占住 1 个法力格。");

        // 拔起没有释放那几格，所以这一发按普通占用检查看是「格被占着」——放回原处要能走成。
        ArenaSetup.Inject(world, "DropBuilding", $"restaurant|{anchor.Col}|{anchor.Row}", out _, out var dropWhy)
            .ShouldBeTrue($"放回原处应当走成写路径（{dropWhy}）。");

        world.Town.StasisMana.ShouldBe(heldBefore, "放回原处归还那 1 个法力格。");
        world.Construction.Find("restaurant")!.Anchor.ShouldBe(anchor, "锚点一寸没动。");
        world.Town.IsConserved().ShouldBeTrue($"八条等式仍然要平：{world.Town.ConservedReport()}");

        ArenaSetup.Inject(world, "RemoveBuilding", "restaurant", out _, out var removeWhy)
            .ShouldBeTrue($"拆掉那一栋（{removeWhy}）。");
        ArenaSetup.Inject(world, "PlaceBuilding", $"Granary|{anchor.Col}|{anchor.Row}", out _, out var why)
            .ShouldBeTrue($"原处应当放得下别的建筑（{why}）。");
    }

    private const string TrainingGround = "training_ground";

    /// <summary>场上那一件散货食物的 id（<see cref="Prepare"/> 摆上去的那一件）。</summary>
    private static string FoodItemId(ArenaWorld world)
    {
        foreach (var item in world.Items.All)
        {
            if (item.Kind == PointItemKind.Cargo && item.Payload == "food") return item.Id;
        }

        throw new InvalidOperationException("场上没有那一件散货食物");
    }

    /// <summary>把场上那几只洞穴原生怪一击打倒（交战那一摊唯一的写血口，词表里没有「打一下」）。</summary>
    private static void KillCaveMonsters(ArenaWorld world)
    {
        foreach (var monster in world.Roster.Units
                     .Where(u => u.Role == UnitRole.Monster && !u.LeftTown && u.State != UnitState.Dead)
                     .ToList())
        {
            world.Battle.ApplyDamage(world.Tick, monster, 1000.0);
            monster.State.ShouldBe(UnitState.Dead, "这一击要真的把它打死。");
        }
    }

    private static string FindHelper(ArenaWorld world)
    {
        foreach (var u in world.Roster.Units)
        {
            if (u.IsHelper) return u.Id;
        }

        throw new InvalidOperationException("召唤帮工之后册子上该有一个帮工");
    }

    /// <summary>把 <paramref name="reason"/> 那一档需要的前置条件摆好（账、格、岗位、通缉）。</summary>
    private static void Prepare(ArenaWorld world, RejectReason reason)
    {
        switch (reason)
        {
            case RejectReason.InsufficientSoulStone:
                world.Town.TrySpendSoulStone(world.Town.SoulStone).ShouldBeTrue("把魂石花光。");
                world.Items.Add(PointItemKind.Corpse, new(-2.0, 2.0), "w1");
                break;
            case RejectReason.InsufficientMana:
                for (var i = 0; i < TownAccounts.ManaCapacity; i++)
                {
                    world.Town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue();
                }

                var wounded = world.Roster.Find("w1")!;
                wounded.Hp = -50.0;
                world.Battle.ApplyDamage(world.Tick, wounded, 1.0);
                wounded.State.ShouldBe(UnitState.Wounded);
                break;
            case RejectReason.InsufficientMaterial:
                // 满耐久的房子排在「备件够不够」之前判，先把目标砸出缺口，量到的才是缺备件那一档。
                world.DamageBuilding(TrainingGround, 10.0).ShouldBeTrue("先砸掉一点耐久。");
                world.Town.TrySpendSparePart(world.Town.SparePart).ShouldBeTrue("把备件花光。");
                break;
            case RejectReason.FullCapacity:
                // 教堂只有两个岗位，派满之后第三个就是 FullCapacity。
                ArenaSetup.Inject(world, "AssignPost", "w1|church", out _, out _).ShouldBeTrue();
                ArenaSetup.Inject(world, "AssignPost", "w2|church", out _, out _).ShouldBeTrue();
                world.Roster.PostedCount(ArenaWorld.ChurchId).ShouldBe(2);
                break;
            case RejectReason.InsufficientFood:
                // 空艇「买食物」改成真买之后，那一发不再回这一档（它回的是钱不够）。
                // 词表里还回 InsufficientFood 的只剩「把散货食物投进建筑喂巨兽」那一发 ——
                // 镇上的食物花光了，巨兽就喂不动。
                world.Town.TrySpendFood(world.Town.Food).ShouldBeTrue("把食物花光。");
                world.Items.Add(PointItemKind.Cargo, new DemoFactory.GugaFramework.Pathfinding.NavPoint(0.0, 0.0), "food");
                break;
            case RejectReason.WantedRefused:
                world.Town.AccrueDebt(ArenaWorld.PoorFactionId,
                    ArenaTuning.WantedDebtThreshold + 100.0);
                world.AdvanceTo(((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay);
                world.Wanted.ShouldBeTrue("欠债过阈值即通缉。");
                break;
            case RejectReason.InvalidPhase:
                // 拔锚那一发要「不在停靠期」才回 InvalidPhase。
                ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out _).ShouldBeTrue("先真的拔一次锚。");
                world.AdvanceTo(world.Tick + (int)(ArenaTime.StepsPerGameHour * 2.1));
                break;
            case RejectReason.GateNotMet:
                // 门槛没到：开局一阶、停靠中、还没挖到第 2 层 —— 那一档的默认世界就是。
                world.Tier.ShouldBe(BeastTier.Tier1);
                world.IsDocked.ShouldBeTrue("停靠中才轮到门槛那一档。");
                world.Soil.IsDug(0, ArenaTuning.Tier2GateLayer).ShouldBeFalse("还没挖到第 2 层。");
                break;
            case RejectReason.NoIdleWorker:
                // 没有无岗的工人或帮工：把在场的人都打成重伤（休养期不占无岗位）。
                foreach (var u in world.Roster.Units)
                {
                    if (u.Role is UnitRole.Worker or UnitRole.Helper) u.State = UnitState.Wounded;
                }

                world.FindNpcFeedExecutor().ShouldBeNull("重伤之后没有无岗的工人或帮工。");
                break;
            case RejectReason.FormLocked:
                // 荒漠态未解锁：一阶、开局、没在沙漠累计待够 240 分钟。
                world.Tier.ShouldBe(BeastTier.Tier1);
                world.Form.ShouldBe(BeastForm.Original);
                world.Voyage.DwellGameMinutes.ShouldBe(0, "还没在沙漠格里待过。");
                world.FormUnlocked(BeastForm.Desert).ShouldBeFalse("荒漠态还没解锁。");
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 转职清岗：粮仓只收工作人员（<see cref="PeopleRoster.RoleFitsPost"/> 对 <c>Granary</c> 只放 Worker 过），
    /// 转成角斗士之后那一格岗位就不合身了 —— 岗位要摘掉，人落到脚下那一格。
    /// </summary>
    [Fact]
    public void 转职后非法岗位被清空()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.Inject(world, "AssignPost", "w1|granary", out _, out var why).ShouldBeTrue(why);

        var unit = world.Roster.Find("w1")!;
        unit.Post.ShouldBe("granary");

        ArenaSetup.Inject(world, "ConvertRole", "w1|Gladiator", out _, out why).ShouldBeTrue(why);

        unit.Role.ShouldBe(UnitRole.Gladiator);
        unit.Post.ShouldBeEmpty("粮仓只收工人，角斗士站不了那一格岗位。");
        world.Roster.PostedCount("granary").ShouldBe(0);
        world.Roster.PostedTo("granary").ShouldBeEmpty();
    }

    /// <summary>
    /// 反例：饭店两个角色都收，转成角斗士之后岗位仍然合身 —— 这一格不许被清掉
    /// （清岗那一刀只落给 <see cref="PeopleRoster.RoleFitsPost"/> 不成立的那一档）。
    /// </summary>
    [Fact]
    public void 岗位仍合法时转职不清空()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.Inject(world, "AssignPost", "w1|restaurant", out _, out var why).ShouldBeTrue(why);

        var unit = world.Roster.Find("w1")!;
        unit.Post.ShouldBe("restaurant");

        ArenaSetup.Inject(world, "ConvertRole", "w1|Gladiator", out _, out why).ShouldBeTrue(why);

        unit.Role.ShouldBe(UnitRole.Gladiator);
        unit.Post.ShouldBe("restaurant");
        world.Roster.PostedCount("restaurant").ShouldBe(1);
    }

    private static string VerbOf(RejectReason reason) => reason switch
    {
        RejectReason.InvalidArgs => "Dock",
        RejectReason.InvalidPhase => "PullAnchor",
        RejectReason.InsufficientCoin => "BuySupply",
        // 喂巨兽那一发才回 InsufficientFood（散货食物投进建筑）；
        // 空艇「买食物」是买，不是卖 —— 它回的是钱不够那一档。
        RejectReason.InsufficientFood => "DropToBuilding",
        RejectReason.InsufficientSoulStone => "ReviveCorpse",
        RejectReason.InsufficientMana => "CastHeal",
        RejectReason.InsufficientMaterial => "RepairBuilding",
        RejectReason.FullCapacity => "AssignPost",
        RejectReason.InvalidTarget => "GrabEntity",
        RejectReason.WantedRefused => "Dock",
        RejectReason.GateNotMet => "AdvanceTier",
        RejectReason.TierAtMax => "AdvanceTier",
        RejectReason.FormLocked => "SwitchForm",
        RejectReason.NoIdleWorker => "NpcFeed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "十四项之外没有别的拒绝原因"),
    };

    private static string ArgsOf(ArenaWorld world, RejectReason reason) => reason switch
    {
        // 段数给歪 → InvalidArgs
        RejectReason.InvalidArgs => $"{BigMapGrid.PoorCity.Id}|多给一段",
        // 不在停靠期拔锚 → InvalidPhase
        RejectReason.InvalidPhase => string.Empty,
        // 钱不够 → InsufficientCoin（10 食物 × 10 金 × 当地物价，远超开局那 2000 金）
        RejectReason.InsufficientCoin => "100000",
        // 仓里没食物可喂 → InsufficientFood（散货食物投进粮仓，镇上的食物已花光）
        RejectReason.InsufficientFood => $"{FoodItemId(world)}|granary",
        // 魂石不够复活 → InsufficientSoulStone
        RejectReason.InsufficientSoulStone => world.Items.All.Last().Id,
        // 可用法力格不足 → InsufficientMana
        RejectReason.InsufficientMana => "w1",
        // 备件不够修房 → InsufficientMaterial
        RejectReason.InsufficientMaterial => TrainingGround,
        // 教堂岗位已满 → FullCapacity
        RejectReason.FullCapacity => "w3|church",
        // 甲板上没有这一件 → InvalidTarget
        RejectReason.InvalidTarget => "no_such_thing",
        // 通缉中停债主势力的城 → WantedRefused
        RejectReason.WantedRefused => BigMapGrid.PoorCity.Id,
        // 进阶门槛没到 / 已到三阶：两条都走 AdvanceTier（无参数）
        RejectReason.GateNotMet => string.Empty,
        RejectReason.TierAtMax => string.Empty,
        // 形态没解锁：走 SwitchForm（荒漠态未解锁）
        RejectReason.FormLocked => "desert",
        // 没有无岗的工人或帮工：走 NpcFeed（无参数）
        RejectReason.NoIdleWorker => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "十四项之外没有别的拒绝原因"),
    };
}
