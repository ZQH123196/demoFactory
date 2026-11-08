#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 子系统那一层的<b>世界接线</b>：土壤、上帝之手、法术停滞、法力两类消耗、生死判定、
/// 收场清剿与人物状态四套规则。
///
/// <para><b>这一份与既有那份组件测试分工</b>：<c>UndergroundSoilTests</c> /
/// <c>DeckConstructionTests</c> / <c>BattleTests</c> 直接拿组件那一件当真值，
/// 这一份全部经 <see cref="ArenaSetup.Inject"/>（玩家、调试桥、重放端同一口）与
/// <see cref="ArenaWorld.Advance"/> 走一遍 —— 钉的是「组件接进世界之后那一格还通不通」：
/// 派工从册子上读、回长挂在整游戏日上、法力两本账（<see cref="TownAccounts"/> 与
/// <see cref="DeckConstruction"/>）各自归己、收场清剿按名单序号分生死。</para>
///
/// <para><b>摆前置条件的口子只有一处</b>：制造伤员与尸体用 <see cref="BattleSim.ApplyDamage"/>
/// （交战那一摊唯一的写血口，词表里没有「挨一下」这一发）。其余前置都走词表。</para>
/// </summary>
public class LogicSubsystemTests
{
    private const int Seed = 7;

    /// <summary>
    /// 一个航段的步数上限：两城相距 240 米、移速 0.4 米每现实秒、一步 0.1 秒
    /// —— 240 ÷ 0.4 ÷ 0.1 = 6000 步，这里留两成余量取 7200。
    /// <para>算「该在什么时候出发才能正好在某一格靠岸」的那几处都要用这一个数。</para>
    /// </summary>
    private const int VoyageSteps = ArenaTime.StepsPerGameHour * 12;

    /// <summary>发一发词表命令并要求走成写路径。</summary>
    private static void Fire(ArenaWorld world, string verb, string args)
    {
        ArenaSetup.Inject(world, verb, args, out _, out var why)
            .ShouldBeTrue($"{verb}({args}) 该走成写路径，被拒了：{why}");
    }

    private static string Reject(ArenaWorld world, string verb, string args)
    {
        ArenaSetup.Inject(world, verb, args, out _, out var why).ShouldBeFalse($"{verb}({args}) 该判否。");
        return why;
    }

    [Fact]
    public void 地下开挖_自上而下_挖开第二层后可发起清剿_四只全灭才收场()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        // 自上而下：上一层没开，这一层就开不了（第 2 层是洞穴层，这一格要验的就是它）。
        Reject(world, "ExcavateSoil", "0|2").ShouldStartWith(
            $"rejected: {RejectReason.InvalidPhase}", Case.Sensitive);

        Fire(world, "ExcavateSoil", "0|1");
        world.Soil.IsDigging(0, 1).ShouldBeTrue("派满 3 人即开工，1 游戏小时挖开。");
        world.Soil.IsDug(0, 1).ShouldBeFalse("工还没到，不能算挖开。");

        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue("工到即挖开。");
        world.Soil.CaveMonstersAt(0, 1).ShouldBe(0, "第 1 层不是洞穴层。");

        // 第 2 层是洞穴层：挖开只是把洞露出来，不再自动开打 —— 清剿是独立的一发。
        Fire(world, "ExcavateSoil", "0|2");
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 2).ShouldBeTrue("第 2 层挖开了。");
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "挖开不自动开打。");
        world.Soil.CaveMonstersAt(0, 2).ShouldBe(ArenaTuning.NativeCaveMonsters, "洞里住着四只。");

        Fire(world, "ClearCave", "0|2");
        world.Battle.Occasion.ShouldBe(BattleOccasion.CaveClearing);
        world.Battle.EndTick.ShouldBe(
            world.Tick + ((long)(ArenaTime.StepsPerGameHour * ArenaTuning.CaveGameHours)),
            "清剿时限 1.5 游戏小时（只当保险）。");

        var monsters = world.Units.Where(u => u.Role == UnitRole.Monster).ToList();
        monsters.Count.ShouldBe(ArenaTuning.NativeCaveMonsters, "单处洞穴四只原生怪。");

        // 涌出的落点是镇政府电梯口（镇政府门口那一格的格心）。
        // 电梯口那一处不是一对写死的米：镇政府门口格是 (0, 2)，格心跟着格长走 ——
        // 1 米一格时落在 (0.5, 2.5)，2 米一格时落在 (1, 5)。
        DeckGeometry.CaveSpawn().ShouldBe(new NavPoint(1.0, 5.0), "洞穴涌怪落在镇政府电梯口（门口格心）。");
        DeckGeometry.ArenaWest().ShouldBe(new NavPoint(-1.5, -6.0), "办赛西侧入场点。");
        DeckGeometry.ArenaEast().ShouldBe(new NavPoint(1.5, -6.0), "办赛东侧入场点。");
        DeckGeometry.PriestEdge().ShouldBe(new NavPoint(0.0, -4.0), "在岗牧师驻场边。");

        // **四只全灭立即收场**，不是时限到：打死四只之后下一步（整游戏分钟）就收。
        var soulsBefore = world.Town.SoulStone;
        foreach (var monster in monsters) world.Battle.ApplyDamage(world.Tick, monster, 1000.0);
        world.Advance(ArenaTime.StepsPerGameMinute);

        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "四只全灭即收场，不等时限。");
        world.TownHallBreached.ShouldBeFalse("清剿不攻城：镇政府不倒、不折债。");
        world.Soil.IsCleared(0, 2).ShouldBeTrue("四只全灭才算清剿成功。");
        world.Soil.IsCleared(0, 1).ShouldBeFalse("别的格不跟着转档。");
        world.Items.LiveCorpseCount.ShouldBe(4, "四只各留一具尸体。");
        world.Town.ProducedSoulStone.ShouldBe(0.0, 1e-9, "洞穴原生怪不给魂石。");
        world.Town.SoulStone.ShouldBe(soulsBefore, 1e-9, "魂石一格不动。");
        world.Town.IsConserved().ShouldBeTrue("打一整场之后八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 采矿岗位派三个人_两游戏小时产一矿石_清剿后每四小时产一特殊材料()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        Fire(world, "ExcavateSoil", "0|1");
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue();

        // 采矿岗写在 AssignPost 那一发的目标里（soil:象限_层），只收工作人员，且要那一格已挖开。
        Fire(world, "AssignPost", "w1|soil:0_1");
        Fire(world, "AssignPost", "w2|soil:0_1");
        Fire(world, "AssignPost", "w3|soil:0_1");
        world.Soil.MinersAt(0, 1).ShouldBe(ArenaTuning.MinePostsPerCell, "一格 3 个岗位，派满 3 人。");

        Reject(world, "AssignPost", "g1|soil:0_1").ShouldStartWith(
            $"rejected: {RejectReason.InvalidTarget}", Case.Sensitive, "采矿岗只收工作人员。");
        Reject(world, "AssignPost", "w4|soil:9_9").ShouldStartWith(
            $"rejected: {RejectReason.InvalidArgs}", Case.Sensitive, "象限或层越界。");

        // 没清剿就只有矿石；每人在岗每 2 游戏小时 1 枚。
        world.AdvanceTo(world.Tick + (long)(ArenaTime.StepsPerGameHour * ArenaTuning.OreGameHoursPerMiner));
        world.Soil.OreAccumulatedAt(0, 1).ShouldBe(ArenaTuning.MinePostsPerCell, 1e-9,
            "3 人 × 2 游戏小时 = 3 枚矿石。");
        world.Soil.MaterialAccumulatedAt(0, 1).ShouldBe(0.0, "没清剿不出特殊材料。");
        world.Town.Ore.ShouldBe(ArenaTuning.OpeningOre + ArenaTuning.MinePostsPerCell, 1e-9,
            "电梯每 10 个游戏分钟结算一次、把地下采出的料直接运进镇政府仓库，不经地面二次搬运。");

        // 洞穴那一格清剿成功后转高危：每人在岗每 4 游戏小时 1 枚特殊材料。
        Fire(world, "ExcavateSoil", "0|2");
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        Fire(world, "AssignPost", "w1|soil:0_2");
        Fire(world, "AssignPost", "w2|soil:0_2");
        Fire(world, "AssignPost", "w3|soil:0_2");
        world.Soil.MarkCaveCleared(0, 2).ShouldBeTrue("清剿成功后转高危。");

        world.AdvanceTo(world.Tick + (long)(ArenaTime.StepsPerGameHour * ArenaTuning.MaterialGameHoursPerMiner));
        world.Soil.MaterialAccumulatedAt(0, 2).ShouldBe(ArenaTuning.MinePostsPerCell, 1e-9,
            "清剿后每 4 游戏小时 1 枚特殊材料。");
        world.Town.IsConserved().ShouldBeTrue("采矿这一段八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 回长与塌方_未嵌入的已挖格按日长满并记一次塌方()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // **塌方那一格倒着定**：停靠与日界之间要留得出「一个航段 + 三个游戏小时」。
        // 开局不再自动航行，巨兽停在贫城；这一发 Dock 富城是真的走完一个航段
        // （240 米 ÷ 0.4 米每现实秒 ÷ 0.1 秒每步 = 6000 步），到岸那一刻正好是塌方前三个游戏小时。
        //
        // 反过来「先停靠、再取日界」也不行 —— 那会把放客窗口拉长到一整个游戏日，
        // 而游客池一次放满撑不满七游戏小时（80 个 ÷ 0.5 个每游戏分钟 = 160 游戏分钟放满，
        // 停留只 2~4 游戏小时）。折中办法是提前一个航段出发，
        // 让巨兽正好在塌方前三个游戏小时靠岸 —— 那三个游戏小时既放得上客，也还没放空。
        var threeHours = ArenaTime.StepsPerGameHour * 3;
        var lead = VoyageSteps + threeHours;
        var collapseTick = ((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay;
        while (collapseTick - lead <= world.Tick) collapseTick += ArenaTime.StepsPerGameDay;

        world.AdvanceTo(collapseTick - lead);
        // 这一格验的是塌方与游客，不是生态：饱食度摆满，保证航程与原来一样正好 6000 步。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.DockedAt(BigMapGrid.RichCity.Id);
        world.AdvanceTo(collapseTick - threeHours);

        // 靠岸后开挖第 1 层：一游戏小时挖开，派三名矿工，再等整游戏日那一刻回长。
        Fire(world, "ExcavateSoil", "0|1");
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeTrue("第 1 层挖开了。");
        Fire(world, "AssignPost", "w1|soil:0_1");
        Fire(world, "AssignPost", "w2|soil:0_1");
        Fire(world, "AssignPost", "w3|soil:0_1");

        world.AdvanceTo(collapseTick - 1);
        world.Soil.Collapses.Count.ShouldBe(0, "到整游戏日之前还没塌。");

        var moodBefore = world.Roster.Townspeople.ToDictionary(u => u.Id, u => u.Mood);
        var inTown = world.Visitors.InTown.OrderBy(v => v.Unit.RosterIndex).ToList();
        var wanted = inTown.Count / 5;
        moodBefore.Count.ShouldBeGreaterThan(0, "在镇的人得先有几个，情绪才有得扣。");
        inTown.Count.ShouldBeGreaterThanOrEqualTo(5, "塌方吓跑的是在镇游客，甲板上得先有游客。"
            + world.Visitors.DebugText());

        world.AdvanceTo(collapseTick);

        world.Soil.IsDug(0, 1).ShouldBeFalse("第 1 层每游戏日 1 层速：一个游戏日长满。");
        world.Soil.Collapses.Count.ShouldBe(1, "回长填满已挖格即引发一次塌方。");
        var collapse = world.Soil.Collapses[0];
        collapse.Sector.ShouldBe(0);
        collapse.Layer.ShouldBe(1);
        collapse.MoodDrop.ShouldBe(ArenaTuning.CollapseTownMoodDrop, 1e-9);
        world.Soil.MinersAt(0, 1).ShouldBe(0, "塌方把那一格的采矿岗位腾空。");

        // ① 全镇情绪 −10：在镇人员每人各扣一档（游客与已离镇的不在这一档里）。
        //    塌方与整游戏小时的情绪回升落在同一步，期望值是「塌之前 − 10 + 每小时 2」。
        //    这一局没发过薪，所以还叠一档欠薪的情绪（−5）。
        var hourRecovery = collapseTick % ArenaTime.StepsPerGameHour == 0
            ? ArenaTuning.MoodRecoveryPerGameHour
            : 0.0;
        foreach (var (id, mood) in moodBefore)
        {
            world.Roster.Find(id)!.Mood.ShouldBe(
                mood - ArenaTuning.CollapseTownMoodDrop + hourRecovery + ArenaTuning.MoodUnpaidWagePerGameDay,
                1e-9,
                $"{id} 在镇上，塌方扣 {ArenaTuning.CollapseTownMoodDrop:0.##} 那一档，外加欠薪那一档。");
        }

        // ② 在镇游客吓跑 20% = 每 5 人取 1 人，按名单序号从头取，不掷点。
        inTown.Take(wanted).ShouldAllBe(v => v.Activity == VisitorActivity.Leaving,
            $"在镇 {inTown.Count} 人 → 吓跑 {wanted} 人（每 5 人取 1 人），按名单序号从头取。");
        inTown.Skip(wanted).ShouldAllBe(v => v.Activity != VisitorActivity.Leaving,
            "取满一批为止，后面的一个都不动。");
        inTown.Take(wanted).ShouldAllBe(v => v.Unit.LeftTown, "吓跑的当场离镇。");
        world.Soil.VisitorsFledBy(inTown.Count).ShouldBe(wanted,
            "土壤那一口算出来的人数与世界里实际吓跑的同一式（在镇每 5 人取 1 人）。");
        world.Town.IsConserved().ShouldBeTrue("塌方这一段八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 法术停滞_拔起占一格法力还留在建造册上_落位归还()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        Fire(world, "PlaceBuilding", "Workshop|-11|-6");
        var b1 = world.Construction.Ids.Last();
        var building = world.Construction.Find(b1);
        building.ShouldNotBeNull();
        building!.Anchor.ShouldBe(new GridCell(-11, -6));

        Fire(world, "LiftBuilding", b1);
        world.Deck.Lifted.ShouldContain(b1, "悬空集合记着这一栋。");
        world.Town.StasisMana.ShouldBe(1, "Town 那本账占一格。");
        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(1, "DeckConstruction 那本账也占一格。");
        world.Construction.Find(b1).ShouldNotBeNull("拔起那一发在建造模块里保持原位，不出册。");

        Reject(world, "LiftBuilding", b1).ShouldStartWith(
            $"rejected: {RejectReason.InvalidPhase}", Case.Sensitive, "已在悬空态拔不动。");

        Fire(world, "DropBuilding", $"{b1}|-11|-3");
        world.Deck.Lifted.ShouldNotContain(b1);
        world.Town.StasisMana.ShouldBe(0, "Town 那本账还一格。");
        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(0, "DeckConstruction 那本账还一格。");
        world.Construction.Find(b1)!.Anchor.ShouldBe(new GridCell(-11, -3), "落位即迁锚点。");
        world.Town.AvailableMana.ShouldBe(TownAccounts.ManaCapacity, "来回走一趟，可用格回到满。");
        world.Town.IsConserved().ShouldBeTrue("拔起落位这一串八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 上帝之手_抓人放岗位放甲板抓尸体进教堂_敌人游客抓不得()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        Fire(world, "GrabEntity", "w1");
        var held = world.Roster.Find("w1")!;
        held.Held.ShouldBeTrue("抓在手上。");

        Fire(world, "DropToBuilding", "w1|granary");
        held.Held.ShouldBeFalse();
        held.Post.ShouldBe(ArenaWorld.GranaryId, "扔进粮仓即上岗。");
        world.Roster.PostedCount(ArenaWorld.GranaryId).ShouldBe(1);

        Fire(world, "GrabEntity", "w1");
        held.Post.ShouldBe(string.Empty, "再抓一次先把岗位腾出来。");
        Fire(world, "DropToDeck", "w1|6|6");
        held.Held.ShouldBeFalse();
        held.Post.ShouldBe(string.Empty, "放回甲板即无岗位。");

        // 尸体那一档：抓到教堂即消圈（尸体只在教堂这一件里等复活）。
        var corpseOwner = world.Roster.Find("w2")!;
        corpseOwner.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, corpseOwner, 1.0);
        corpseOwner.State.ShouldBe(UnitState.Dead);
        var corpse = world.Items.LiveCorpses.Single(c => c.Payload == "w2");
        Fire(world, "GrabEntity", corpse.Id);
        Fire(world, "DropToBuilding", $"{corpse.Id}|{ArenaWorld.ChurchId}");
        world.Items.Find(corpse.Id).ShouldBeNull("搬进教堂即从点位物表退场。");
        world.Items.LiveCorpseCount.ShouldBe(0, "搬进教堂即消圈：场上没剩没搬走的尸体。");

        // 敌人与游客抓不得。
        var enemy = world.Roster.Add("e1", UnitRole.Enemy, new NavPoint(1.0, 1.0));
        Reject(world, "GrabEntity", enemy.Id).ShouldStartWith(
            $"rejected: {RejectReason.InvalidTarget}", Case.Sensitive);
        Reject(world, "GrabEntity", "no_such_one").ShouldStartWith(
            $"rejected: {RejectReason.InvalidTarget}", Case.Sensitive);
        world.Town.IsConserved().ShouldBeTrue("抓放这一串八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 拖人进不接受该职业的建筑被拒_原因写明职业对不上且人不会一直挂在手上()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        Fire(world, "GrabEntity", "w1");
        var why = Reject(world, "DropToBuilding", $"w1|{ArenaWorld.ArenaId}");

        // 拒绝原因必须是「职业不符」那一档，而不是岗位满了或者建筑不在册。
        why.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive);
        why.ShouldContain("上不了", Case.Sensitive);

        var worker = world.Roster.Find("w1")!;
        worker.Post.ShouldBe(string.Empty, "被拒不落岗。");
        worker.Held.ShouldBeFalse("被拒也要把人放下 —— 留在手上会一直停在空中。");
        world.Roster.PostedCount(ArenaWorld.ArenaId).ShouldBe(0);
    }

    [Fact]
    public void 自由人投满员岗位被拒_职业与忠心都不变()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 粮仓只有一个岗位：先让一个工作人员占满，再把自由人 f1 拖过去。
        var capacity = world.Roster.PostCapacity("Granary");
        capacity.ShouldBeGreaterThan(0);
        var worker = world.Roster.Units.First(u => u.Role == UnitRole.Worker && !u.LeftTown);
        world.Roster.AssignPost(worker, ArenaWorld.GranaryId);
        world.Roster.PostedCount(ArenaWorld.GranaryId).ShouldBe(capacity, "岗位先占满。");

        var free = world.Roster.Find("f1")!;
        free.Role.ShouldBe(UnitRole.FreePerson);
        var loyaltyBefore = free.Loyalty;

        Fire(world, "GrabEntity", free.Id);
        var why = Reject(world, "DropToBuilding", $"{free.Id}|{ArenaWorld.GranaryId}");
        why.ShouldContain("岗位已满", Case.Sensitive);

        free.Role.ShouldBe(UnitRole.FreePerson, "被拒路径不能先把人转成工人。");
        free.Loyalty.ShouldBe(loyaltyBefore, "被拒路径不能重置忠心。");
        free.Post.ShouldBe(string.Empty, "被拒不落岗。");
        free.Held.ShouldBeFalse("被拒也要把人放下 —— 留在手上会一直停在空中。");
    }

    [Fact]
    public void 帮工抓不起来()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var helper = world.Roster.Add("h1", UnitRole.Helper, new NavPoint(1.0, 1.0));
        var why = Reject(world, "GrabEntity", helper.Id);

        why.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive);
        helper.Held.ShouldBeFalse("帮工不是镇民，按下去不响应。");
    }

    [Fact]
    public void 法力两类消耗_即时花掉不占格_占住的那格不自己回来_每游戏小时恢复一格()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        // ① 占位消耗（召唤帮工 + 拔起建筑）：Town 与 DeckConstruction 各占一格。
        Fire(world, "SummonHelper", string.Empty);
        Fire(world, "PlaceBuilding", "Workshop|-11|-6");
        var b1 = world.Construction.Ids.Last();
        Fire(world, "LiftBuilding", b1);
        world.Town.AvailableMana.ShouldBe(TownAccounts.ManaCapacity - 2, "占了两格。");
        world.Town.HelperMana.ShouldBe(1);
        world.Town.StasisMana.ShouldBe(1);
        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(1, "DeckConstruction 那本账也占一格。");

        // 恢复那一格只填「真正空着的那几格」：可用 8 + 占位 2 已经等于总格数 10，一个游戏小时也不补。
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        world.Town.AvailableMana.ShouldBe(TownAccounts.ManaCapacity - 2, "占位不算用掉，恢复那一格判否。");
        world.Town.RegainedMana.ShouldBe(0, "自然恢复累计没动。");
        world.Town.HelperMana.ShouldBe(1, "占住的那一格不自己回来。");
        world.Town.StasisMana.ShouldBe(1, "拔起那一格也不自己回来。");

        // ② 即时消耗：花掉即扣、不占格（花完那一格，下一个游戏小时才补回来）。
        var wounded = world.Roster.Find("g1")!;
        wounded.Hp = -50.0;
        world.Battle.ApplyDamage(world.Tick, wounded, 1.0);
        wounded.State.ShouldBe(UnitState.Wounded);
        Fire(world, "CastHeal", "g1");
        wounded.State.ShouldBe(UnitState.Standing, "治好即起身。");
        wounded.Hp.ShouldBe(ArenaTuning.UnitMaxHp);
        world.Town.AvailableMana.ShouldBe(TownAccounts.ManaCapacity - 3, "即时消耗扣一格。");
        world.Town.InstantSpentMana.ShouldBe(1);
        world.Town.OccupiedMana.ShouldBe(2, "即时消耗不占格：三类占位格一格没多（仍是帮工 1 + 拔起 1）。");

        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        world.Town.AvailableMana.ShouldBe(TownAccounts.ManaCapacity - 2, "即时花掉的那一格下一个小时补回来。");
        world.Town.RegainedMana.ShouldBe(ArenaTuning.ManaRegainPerGameHour, "自然恢复累计只增。");
        world.Town.HelperMana.ShouldBe(1);
        world.Town.StasisMana.ShouldBe(1);
        world.Town.IsConserved().ShouldBeTrue("法力这一串八条等式仍然要平：" + world.Town.ConservedReport());

        // ③ 可用格见底时治伤回 InsufficientMana，且一格法力都不扣。
        while (world.Town.AvailableMana > 0)
        {
            world.Town.TrySpendInstantMana().ShouldBeTrue("花到见底为止。");
        }

        var second = world.Roster.Find("g2")!;
        second.Hp = -50.0;
        world.Battle.ApplyDamage(world.Tick, second, 1.0);
        var before = world.Town.AvailableMana;
        Reject(world, "CastHeal", "g2").ShouldStartWith(
            $"rejected: {RejectReason.InsufficientMana}", Case.Sensitive);
        world.Town.AvailableMana.ShouldBe(before, "被拒之后可用格一格不动。");
        second.State.ShouldBe(UnitState.Wounded, "没法力就治不了。");
        world.Town.IsConserved().ShouldBeTrue("法力见底这一格八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 扣血生死_正好落到零是重伤正好落到负一百是死()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // −100 < Hp ≤ 0：重伤退场、腾岗位、不出尸。正血量打到底正好落到 0。
        var hurt = world.Roster.Find("w1")!;
        hurt.Hp = 25.0;
        world.Battle.ApplyDamage(world.Tick, hurt, 25.0);
        hurt.Hp.ShouldBe(0.0, 1e-9, "正好落到 0。");
        hurt.State.ShouldBe(UnitState.Wounded, "正好落到 0 仍是重伤（−100 < Hp ≤ 0 那一档）。");
        hurt.RiseTick.ShouldBe(world.Tick + (ArenaTime.StepsPerGameMinute * ArenaTuning.WoundRiseGameMinutes));
        world.Items.LiveCorpseCount.ShouldBe(0, "重伤不出尸、不入账魂石。");
        world.Town.ProducedSoulStone.ShouldBe(0.0);

        // Hp ≤ −100：当场死、生成尸体、自动入账 3 魂石。
        var doomed = world.Roster.Find("w2")!;
        doomed.Hp = -99.75;
        world.Battle.ApplyDamage(world.Tick, doomed, 0.25);
        doomed.Hp.ShouldBe(ArenaTuning.DeathHpThreshold, 1e-9, "正好落到 −100。");
        doomed.State.ShouldBe(UnitState.Dead, "正好落到 −100 即死（≤ 那一档含端点）。");
        world.Items.LiveCorpses.Count(c => c.Payload == "w2").ShouldBe(1, "死即生成尸体点位物。");
        world.Town.ProducedSoulStone.ShouldBe(ArenaTuning.SoulStonesPerCorpse, 1e-9);
        world.Town.IsConserved().ShouldBeTrue("生死这一格八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 收场清剿_余敌撤退_不按序号判死或被俘()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 先在船上走到「不在停靠期」：拔锚防守要在航行段开。
        ArenaSetup.Inject(world, "Dock", BigMapGrid.PoorCity.Id, out _, out _).ShouldBeTrue();
        // 开局在贫城西边 40 米的野地上（40 米 = 1000 步），推够一整段航程再判停稳。
        for (var i = 0; i < 1400 && !world.IsDocked; i++) world.Advance(1);
        world.IsDocked.ShouldBeTrue("停到贫城上。");
        Fire(world, "PullAnchor", string.Empty);
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence);
        world.TownHallBreached.ShouldBeFalse("这一场攻破不了（镇政府 200 耐久，防守只有 2 游戏小时）。");

        // 逐步推进到收场那一刻，记下收场前站着的敌人（按名单序号）。
        var preSweep = new List<ArenaUnit>();
        var ended = false;
        for (var i = 0; i < 4000 && !ended; i++)
        {
            if (world.Battle.Occasion == BattleOccasion.Defence)
            {
                preSweep = world.Units
                    .Where(u => u.IsBeast && !u.LeftTown && u.State == UnitState.Standing)
                    .OrderBy(u => u.RosterIndex)
                    .ToList();
            }

            world.Advance(1);
            ended = world.Battle.Occasion != BattleOccasion.Defence;
        }

        ended.ShouldBeTrue("防守按 2 游戏小时计时结束。");
        preSweep.Count.ShouldBeGreaterThan(0, "收场那一刻台上还有站着的敌人 —— 这一格要真清剿，不能空转。");

        // ticket 06 起收场不再按名单序号分生死：剩下的敌人一律撤退（离镇），
        // 不生成尸体、不折角斗士。最后一分钟里真被打死的那些仍按战死算。
        var retreated = 0;
        foreach (var e in preSweep)
        {
            if (e.State == UnitState.Dead) continue;

            e.LeftTown.ShouldBeTrue("收场那一刻还站着的敌人一律撤退。");
            e.Role.ShouldBe(UnitRole.Enemy, "撤退的敌人不折成角斗士。");
            world.Items.LiveCorpses.Count(c => c.Payload == e.Id).ShouldBe(0, "撤退不出尸体。");
            retreated++;
        }

        retreated.ShouldBeGreaterThan(0, "这一格要真验到撤退，不能所有敌人都刚好战死。");
        world.Units.Where(u => u.IsBeast && u.Role == UnitRole.Gladiator).ShouldBeEmpty(
            "整场没有「按序号被俘」的敌人。");
        world.Town.IsConserved().ShouldBeTrue("收场这一格八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 人物状态_情绪每游戏小时回升尸体圈再压_欠薪一天掉五_忠心上界夹住()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var moodBefore = world.Roster.Find("w1")!.Mood;
        var loyaltyBefore = world.Roster.Find("w1")!.Loyalty;

        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        world.Roster.Find("w1")!.Mood.ShouldBe(moodBefore + ArenaTuning.MoodRecoveryPerGameHour, 1e-9,
            "每游戏小时自然回升 2。");
        world.Roster.Find("w1")!.Loyalty.ShouldBe(loyaltyBefore, 1e-9, "情绪回升不动忠心。");

        // 尸体负面情绪圈：场上有一具没搬走的尸体，圈里的人每小时再压 −20。
        // 「站在圈里」这条前置条件是摆出来的，不靠开局落点表：镇民现在每游戏小时换一次去处
        // （日常走位），开局那批人早就散到各自那一栋门口去了。
        var corpseOwner = world.Roster.Find("w2")!;
        corpseOwner.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, corpseOwner, 1.0);
        var aura = world.Items.LiveCorpses.Single(c => c.Payload == "w2");

        // 把 w1 摆到那具尸体所在的那一格（落点吸附到最近可通行格，就是尸体脚下那一格）。
        //
        // **位置直接读寻路层的真值，不读活物册那一栏缓存**：`DropToDeck` 落位走的是
        // `ApplyMove`（当场改寻路层），而 `u.DeckX/DeckZ` 是每步走完才同步的读数缓存 ——
        // 读缓存就得多推一步，那一步会跨过情绪结算，后面几格账全跟着偏。
        // 真值在寻路层那一层（「真值归真值，读数是读数」），这里就读那一层。
        Fire(world, "DropToDeck", $"w1|{aura.AtDeck.X}|{aura.AtDeck.Z}");
        var w1At = world.DeckStepper.PositionOf("w1");
        new NavPoint(DeckGeometry.DeckXOf(w1At), DeckGeometry.DeckZOf(w1At))
            .DistanceTo(aura.AtDeck)
            .ShouldBeLessThanOrEqualTo(ArenaTuning.CorpseAuraRadiusMeters, "w1 站在尸体圈里。");

        var near = world.Roster.Find("w1")!;

        var beforeAura = near.Mood;
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameHour);
        near.Mood.ShouldBe(beforeAura + ArenaTuning.MoodRecoveryPerGameHour
            + ArenaTuning.MoodCorpseAuraPerGameHour, 1e-9, "圈里再叠一档尸体负面情绪。");

        // 欠薪一天：忠诚 −15、情绪 −15。先把那具尸体搬进教堂消圈，否则下面两格都被 −20/小时压着。
        Fire(world, "GrabEntity", aura.Id);
        Fire(world, "GrabEntity", aura.Id);
        Fire(world, "DropToBuilding", $"{aura.Id}|{ArenaWorld.ChurchId}");
        world.Items.LiveCorpseCount.ShouldBe(0, "消圈之后场上没有没搬走的尸体。");

        var unpaid = world.Roster.Find("w3")!;
        var moodOfUnpaid = unpaid.Mood;
        var loyaltyOfUnpaid = unpaid.Loyalty;
        world.Roster.MarkUnpaidWage(unpaid);
        var unpaidBefore = ((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay;
        var hoursToDay = (unpaidBefore - world.Tick) / ArenaTime.StepsPerGameHour;
        world.AdvanceTo(unpaidBefore);
        unpaid.Loyalty.ShouldBe(loyaltyOfUnpaid + ArenaTuning.LoyaltyDailyUnpaid, 1e-9, "欠薪一天扣 5 忠心。");
        unpaid.Mood.ShouldBe(moodOfUnpaid + ArenaTuning.MoodUnpaidWagePerGameDay
            + (ArenaTuning.MoodRecoveryPerGameHour * hoursToDay), 1e-9,
            "欠薪一天情绪扣 5，外加这一天走过的每一个整游戏小时的自然回升。");

        // 没发薪：欠一天，每人头上扣一档欠薪（−5 忠心）。这一局从头到尾没发过薪，
        // 所以每一次日结都记欠薪 —— 角斗士与工作人员一视同仁。
        var paid = world.Roster.Find("g1")!;
        var loyaltyOfPaid = paid.Loyalty;
        var loyaltyOfLevel = world.Roster.Find("w4")!.Loyalty;
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameDay);
        paid.Loyalty.ShouldBe(loyaltyOfPaid + ArenaTuning.LoyaltyDailyUnpaid, 1e-9,
            "没发薪：欠一天，角斗士忠心 −5。");
        world.Roster.Find("w4")!.Loyalty.ShouldBe(loyaltyOfLevel + ArenaTuning.LoyaltyDailyUnpaid, 1e-9,
            "没发薪：欠一天，工作人员忠心 −5。");

        // 上界夹住：两样都摆到上界之上，日结把它们夹回 100
        // （欠薪那一档照扣：忠心在夹住之后扣，情绪在夹住之前就夹住了）。
        var maxed = world.Roster.Find("w5")!;
        maxed.Loyalty = ArenaTuning.LoyaltyMax + 50.0;
        maxed.Mood = ArenaTuning.MoodMax + 50.0;
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameDay);
        maxed.Loyalty.ShouldBe(ArenaTuning.LoyaltyMax, 1e-9, "忠心上界 100 夹住。");
        maxed.Mood.ShouldBe(ArenaTuning.MoodMax + ArenaTuning.MoodUnpaidWagePerGameDay, 1e-9,
            "情绪上界 100 夹住（欠薪那一档 −5 照扣）。");

        // 情绪折算产出：情绪 −100 即半折（OutputFactor = 1 + 情绪 / 200）。
        var halved = world.Roster.Find("w6")!;
        halved.Mood = -100.0;
        halved.OutputFactor.ShouldBe(0.5, 1e-9);
        halved.Mood = 0.0;
        halved.OutputFactor.ShouldBe(1.0, 1e-9);
        world.Town.IsConserved().ShouldBeTrue("人物状态这一串八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 忠心打折_三十到六十之间战力乘七折()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var foe = world.Roster.Add("e1", UnitRole.Enemy, new NavPoint(1.0, 1.0));

        var guard = world.Roster.Find("w1")!;
        guard.Melee = 20.0;
        guard.Ranged = 20.0;
        guard.Riding = 20.0;
        foe.Melee = 20.0;
        foe.Ranged = 20.0;
        foe.Riding = 20.0;

        guard.Loyalty = ArenaTuning.LoyaltyMax;
        var full = guard.PowerAgainst(foe);
        guard.LoyaltyDiscounted.ShouldBeFalse("忠诚 100 不打折。");

        guard.Loyalty = ArenaTuning.LoyaltyDiscountLow;
        guard.LoyaltyDiscounted.ShouldBeTrue("忠诚落到 30 进打折那一档。");
        guard.PowerAgainst(foe).ShouldBe(full * ArenaTuning.LoyaltyPowerFactor, 1e-9);

        guard.Loyalty = ArenaTuning.LoyaltyDiscountHigh;
        guard.LoyaltyDiscounted.ShouldBeTrue("忠诚 60 仍在打折那一档（区间含端点）。");

        guard.Loyalty = 0.0;
        guard.LoyaltyDiscounted.ShouldBeFalse("忠诚 0 不打折（阈值 30 以下另按逃跑那一档走）。");
        guard.PowerAgainst(foe).ShouldBe(full, 1e-9);
    }

    [Fact]
    public void 招募_五个来路各一档成本与忠诚初值_付钱必得()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 来路解析与成本表：解析不出回 InvalidArgs（不降级）。
        Reject(world, "Recruit", "抓外星人").ShouldStartWith(
            $"rejected: {RejectReason.InvalidArgs}", Case.Sensitive);
        world.Town.Coin.ShouldBe(ArenaTuning.OpeningCoin, 1e-9, "解析失败一子儿不动。");

        var table = new (RecruitSource Source, string Token, double Cost, double Loyalty)[]
        {
            (RecruitSource.TownOrCountry, "TownOrCountry", 100.0, 60.0),
            (RecruitSource.BuySlave, "BuySlave", 200.0, 30.0),
            (RecruitSource.CatchVagrant, "CatchVagrant", 0.0, 40.0),
            (RecruitSource.CaptureBandit, "CaptureBandit", 0.0, 20.0),
            (RecruitSource.CatchBeast, "CatchBeast", 150.0, 10.0),
        };

        foreach (var row in table)
        {
            ArenaSetup.RecruitCost(row.Source).ShouldBe(row.Cost, $"{row.Source} 那一档成本。");
            ArenaSetup.LoyaltyOf(row.Source).ShouldBe(row.Loyalty, 1e-9, $"{row.Source} 那一档忠诚初值。");
        }

        // 招募是停靠期那一发：停到一座城上，三次付费来路各招一次（都在每停 3 人额度内）。
        world.DockedAt(BigMapGrid.PoorCity.Id);

        var coinBefore = world.Town.Coin;
        var peopleBefore = world.Roster.Townspeople.Count();

        Fire(world, "Recruit", "TownOrCountry");
        world.Roster.Townspeople.Last().Loyalty.ShouldBe(60.0, 1e-9, "城乡招的忠诚初值 60。");

        Fire(world, "Recruit", "BuySlave");
        world.Roster.Townspeople.Last().Loyalty.ShouldBe(30.0, 1e-9, "买奴隶的忠诚初值 30。");

        Fire(world, "Recruit", "CatchBeast");
        world.Roster.Townspeople.Last().Loyalty.ShouldBe(10.0, 1e-9, "捕野兽的忠诚初值 10。");

        world.Town.Coin.ShouldBe(coinBefore - 450.0, 1e-9, "100 + 200 + 150 三发各扣各的。");
        world.Roster.Townspeople.Count().ShouldBe(peopleBefore + 3);
        world.RecruitLimitLeft.ShouldBe(0, "三次成功正好用完这一停的额度。");
        world.Town.IsConserved().ShouldBeTrue("招募这一格八条等式仍然要平：" + world.Town.ConservedReport());
    }
}
