#nullable enable
using System;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 九条守恒等式、法力两类消耗、还款三策略、四十五个动词的词表与十四项拒绝原因、
/// 以及分段式检查点快照的格式。
/// </summary>
public class AccountsTests
{
    private const int Seed = 7;

    [Fact]
    public void 开局九条守恒等式逐条为真()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Town.IsConserved().ShouldBeTrue("开局就该平：" + world.Town.ConservedReport());

        // 九条各读一遍，别只读一个总的 bool。
        world.Town.Coin.ShouldBe(2000.0, 1e-9, "金币 = 2000 + 产出 − 消耗");
        world.Town.Food.ShouldBe(500.0, 1e-9, "食物 = 500 + 产出 − 消耗");
        world.Town.SoulStone.ShouldBe(30.0, 1e-9, "魂石 = 30 + 产出 − 消耗");
        world.Town.Ore.ShouldBe(10.0, 1e-9, "矿石 = 10 + 产出 − 消耗");
        world.Town.SpecialMaterial.ShouldBe(2.0, 1e-9, "特殊材料 = 2 + 产出 − 消耗");
        world.Town.SparePart.ShouldBe(3.0, 1e-9, "备件 = 3 + 产出 − 消耗");
        world.Town.Crystal.ShouldBe(0.0, 1e-9, "魔晶开局 0（只有生态产出这一条进账）");
        world.Town.TotalDebt.ShouldBe(0.0, 1e-9, "负债初始 0");
        (world.Town.AvailableMana + world.Town.StasisMana + world.Town.HelperMana
            + world.Town.InstantSpentMana - world.Town.RegainedMana
            - world.Town.CrystalManaRestored)
            .ShouldBe(10, "法力那一式 = 10");
    }

    [Fact]
    public void 花钱退款之后守恒等式仍然为真()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Town.TrySpendCoin(300.0).ShouldBeTrue("开局 2000 金够花。");
        world.Town.RefundCoin(120.0);
        world.Town.Coin.ShouldBe(1820.0, 1e-9, "退回来的记进产出。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());

        world.Town.TrySpendFood(50.0).ShouldBeTrue();
        world.Town.RefundFood(50.0);
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 推进之后守恒等式仍然为真()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Advance(600);

        world.Town.IsConserved().ShouldBeTrue("推进 600 步（100 个游戏分钟）之后仍然要平："
            + world.Town.ConservedReport());
        world.Tick.ShouldBe(600);
    }

    [Fact]
    public void 法力占位与即时消耗是两类_恢复只涨真正空着的格()
    {
        var town = new TownAccounts();

        // 开局 10 格全空 —— 恢复那一格什么也不涨（池已满）。
        town.RegainManaOne().ShouldBeFalse("池满时不涨。");

        // ① 占位消耗：拔起一栋建筑占住 1 格。
        town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue();
        town.AvailableMana.ShouldBe(9, "占住一格，可用剩 9。");
        town.IsConserved().ShouldBeTrue();

        // ② 占着的那格不恢复：可用 9 + 占位 1 = 10 已满。
        town.RegainManaOne().ShouldBeFalse("占位格不恢复。");

        // ③ 释放那一格之后可用回到 10，恢复仍不涨。
        town.ReleaseMana(TownAccounts.ManaHold.Stasis);
        town.AvailableMana.ShouldBe(10);
        town.RegainManaOne().ShouldBeFalse();
        town.IsConserved().ShouldBeTrue();

        // ④ 即时消耗：花掉即扣，那一笔永久记在 InstantSpent 里当它被充回来的证据。
        town.TrySpendInstantMana().ShouldBeTrue();
        town.AvailableMana.ShouldBe(9);
        town.InstantSpentMana.ShouldBe(1);
        town.IsConserved().ShouldBeTrue();

        town.RegainManaOne().ShouldBeTrue("空着一格，该涨。");
        town.AvailableMana.ShouldBe(10);
        town.RegainedMana.ShouldBe(1);
        town.IsConserved().ShouldBeTrue("恢复那一格在等式里是 +1 与 −1，两边抵平。");
    }

    [Fact]
    public void 两类占位各记各的账()
    {
        var town = new TownAccounts();

        town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue();
        town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue();
        town.TryHoldMana(TownAccounts.ManaHold.Helper).ShouldBeTrue();

        town.StasisMana.ShouldBe(2);
        town.HelperMana.ShouldBe(1);
        town.AvailableMana.ShouldBe(7);
        town.IsConserved().ShouldBeTrue();

        // 占到 0 为止，之后一格也占不下。
        while (town.AvailableMana > 0) town.TryHoldMana(TownAccounts.ManaHold.Helper).ShouldBeTrue();
        town.AvailableMana.ShouldBe(0);
        town.TryHoldMana(TownAccounts.ManaHold.Helper).ShouldBeFalse("池空了，占不下了。");
        town.IsConserved().ShouldBeTrue();
    }

    [Fact]
    public void 还款三种策略_开局默认是None()
    {
        var town = new TownAccounts();
        town.Policy.ShouldBe(DebtPolicy.None, "开局默认不自动还。");

        town.AccrueDebt("poor_town", 400.0);
        town.TotalDebt.ShouldBe(400.0, 1e-9);

        // None：进账不还。
        town.ProduceCoin(1000.0);
        town.SettleAutoRepayment(1000.0).ShouldBe(0.0);
        town.TotalDebt.ShouldBe(400.0, 1e-9, "None 一分不还。");

        // AutoOrder：进账按名单顺序还。
        town.SetDebtPolicy(DebtPolicy.AutoOrder, string.Empty);
        var paid = town.SettleAutoRepayment(1000.0);
        paid.ShouldBe(400.0, 1e-9, "名单上就一家，还清 400。");
        town.TotalDebt.ShouldBe(0.0, 1e-9);
        town.Coin.ShouldBe(2600.0, 1e-9, "3000 − 400 = 2600。");
    }

    [Fact]
    public void 还款AutoTarget优先还指定债主_还完再按名单顺序()
    {
        var town = new TownAccounts();
        town.AccrueDebt("poor_town", 300.0);
        town.AccrueDebt("rich_town", 900.0);
        town.SetDebtPolicy(DebtPolicy.AutoTarget, "rich_town");

        town.ProduceCoin(500.0);
        var paid = town.SettleAutoRepayment(500.0);
        paid.ShouldBe(500.0, 1e-9);
        town.CreditorOf("rich_town")!.Repaid.ShouldBe(500.0, 1e-9, "优先还指定那一家。");
        town.CreditorOf("poor_town")!.Repaid.ShouldBe(0.0, 1e-9, "钱不够轮到它。");
        town.IsConserved().ShouldBeTrue();
    }

    [Fact]
    public void 手动还债走PayDebt_钱不够一子儿不动()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        world.Town.AccrueDebt(ArenaWorld.PoorFactionId, 600.0);
        var before = world.Town.Coin;

        ArenaSetup.Inject(world, "PayDebt", $"{ArenaWorld.PoorFactionId}|9999", out _, out var why)
            .ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientCoin}",
            Case.Sensitive, "钱不够那一格。");
        world.Town.Coin.ShouldBe(before, 1e-9, "拒绝一寸不动。");

        ArenaSetup.Inject(world, "PayDebt", $"{ArenaWorld.PoorFactionId}|250", out _, out _)
            .ShouldBeTrue();
        world.Town.CreditorOf(ArenaWorld.PoorFactionId)!.Repaid.ShouldBe(250.0, 1e-9);
        world.Town.TotalDebt.ShouldBe(350.0, 1e-9);
        world.Town.IsConserved().ShouldBeTrue();
    }

    [Fact]
    public void 四十五个业务动词全部在词表里_词表里也只有这四十五个()
    {
        var expected = new[]
        {
            // 航行与城镇（7）
            "Dock", "SailTo", "PullAnchor", "StartMatch", "TradeAirship", "SteerOutOfBounds",
            "StartRun",
            // 上帝之手与法术（8）
            "GrabEntity", "DropToBuilding", "DropToDeck", "LiftBuilding", "DropBuilding",
            "SummonHelper", "DismissHelper", "CastHeal",
            // 建造与土壤（5）
            "PlaceBuilding", "RemoveBuilding", "RepairBuilding", "ExcavateSoil", "EmbedCrystal",
            // 人口与经营（7）
            "Recruit", "ConvertRole", "GrantFreedom", "SellStar", "AssignPost",
            "SetNonCombatant", "ReviveCorpse",
            // 账目与远行（6）
            "SetPricing", "BuySupply", "PayDebt", "SetDebtPolicy", "PayWages", "Teleport",
            // 巨兽本体与生态（8）
            "AdvanceTier", "SwitchForm", "Feed", "NpcFeed", "EatTarget", "RecallForage", "UseCrystal",
            "SellCrystal",
            // 地下战斗（2）
            "ClearCave", "RetreatCave",
            // 终局（2）
            "RetryFinalDefence", "ContinueBusiness",
        };

        expected.Length.ShouldBe(45, "业务动词正好 45 个。");
        foreach (var verb in expected)
        {
            ArenaSetup.IsVerb(verb).ShouldBeTrue($"{verb} 在词表里。");
        }

        // 反向那一半：词表里每一条业务动词都在这份名单内 ——
        // 少了要判红（动词名写错了），多了也要判红（词表里长出一条没人认领的动词）。
        var business = ArenaSetup.VerbNames
            .Where(v => !SyntheticVerbs.Names.Contains(v))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        var wanted = expected.OrderBy(v => v, StringComparer.Ordinal).ToList();
        business.ShouldBe(wanted, Case.Sensitive, "词表里的业务动词与这份名单逐条相等（不多不少）。");

        // 合成输入那两条由公共层登记（机制见 SyntheticVerbs），也在同一份表里。
        ArenaSetup.IsVerb("SyntheticInputAction").ShouldBeTrue("合成输入那一条显式接住了。");
        ArenaSetup.IsVerb("SyntheticInputClick").ShouldBeTrue("合成输入那一条显式接住了。");
    }

    [Fact]
    public void 每个动词都接得住_不抛_非法参数返回rejected枚举()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var zeroArg = new[]
        {
            "PullAnchor", "SteerOutOfBounds", "SummonHelper", "AdvanceTier", "PayWages",
            "StartRun", "RetreatCave", "RecallForage", "RetryFinalDefence", "ContinueBusiness",
            "NpcFeed",
        };

        foreach (var verb in ArenaSetup.VerbNames)
        {
            var ok = ArenaSetup.Inject(world, verb, "", out _, out var why);

            if (zeroArg.Contains(verb))
            {
                // 零段动词不接参数：成不成都行，但**不抛**（这里没有异常就是过了）。
                _ = why;
                continue;
            }

            ok.ShouldBeFalse($"{verb} 缺参数判否，不抛。");
            why.ShouldStartWith("rejected: ",
                Case.Sensitive, $"{verb} 的拒绝文案要带封闭枚举。");
            why.ShouldContain(RejectReason.InvalidArgs.ToString(),
                Case.Sensitive, $"{verb} 缺参数回的是 InvalidArgs。");
        }

        // 段数给歪同样按 InvalidArgs 拒，不抛。
        ArenaSetup.Inject(world, "Dock", "poor_city|extra", out _, out var segWhy).ShouldBeFalse();
        segWhy.ShouldStartWith($"rejected: {RejectReason.InvalidArgs}",
            Case.Sensitive, "多给一段也判否。");
    }

    [Fact]
    public void 十四项拒绝原因是一个封闭枚举()
    {
        var values = System.Enum.GetValues<RejectReason>();
        values.Length.ShouldBe(14, "拒绝原因就 14 项。");
        values.Select(v => v.ToString()).ShouldBe(
        [
            "InvalidArgs",
            "InvalidPhase",
            "InsufficientCoin",
            "InsufficientFood",
            "InsufficientSoulStone",
            "InsufficientMana",
            "InsufficientMaterial",
            "FullCapacity",
            "InvalidTarget",
            "WantedRefused",
            "GateNotMet",
            "TierAtMax",
            "FormLocked",
            "NoIdleWorker",
        ], Case.Sensitive, "十四项的名字与次序都是契约（拒绝文案逐字进调试桥输出）。");
    }

    [Fact]
    public void CastHeal法力不够时回InsufficientMana()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var man = world.Roster.Find("w1")!;
        man.State = UnitState.Wounded;

        // 把法力占满：拔起十栋（占位那一档）之后就一点空余都没有。
        for (var i = 0; i < 10; i++)
        {
            world.Town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue();
        }

        world.Town.AvailableMana.ShouldBe(0);
        ArenaSetup.Inject(world, "CastHeal", "w1", out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientMana}");
        man.State.ShouldBe(UnitState.Wounded, "没治好还是重伤。");

        // 放一格回来就能治了。
        world.Town.ReleaseMana(TownAccounts.ManaHold.Stasis);
        ArenaSetup.Inject(world, "CastHeal", "w1", out _, out _).ShouldBeTrue();
        man.State.ShouldBe(UnitState.Standing, "治好即起身。");
        man.Hp.ShouldBe(100.0, 1e-9);
        world.Town.InstantSpentMana.ShouldBe(1, "即时消耗那一格 +1。");
        world.Town.IsConserved().ShouldBeTrue();
    }

    [Fact]
    public void SetPricing越界按InvalidArgs拒_一格不动()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.HotelMultiplier.ShouldBe(1.0);

        ArenaSetup.Inject(world, "SetPricing", "0.4|1|1", out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.InvalidArgs}");
        world.Town.HotelMultiplier.ShouldBe(1.0, 1e-9, "越界一格不动。");

        ArenaSetup.Inject(world, "SetPricing", "1.5|0.5|2", out _, out _).ShouldBeTrue();
        world.Town.HotelMultiplier.ShouldBe(1.5, 1e-9);
        world.Town.RestaurantMultiplier.ShouldBe(0.5, 1e-9);
        world.Town.TicketMultiplier.ShouldBe(2.0, 1e-9);
    }

    [Fact]
    public void 快照分段齐全_段首是身份()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var text = world.DebugSnapshot();
        var segments = text.Split(" | ");

        segments[0].ShouldStartWith("tick=0 ", Case.Sensitive, "第 0 段段首是 tick=<N>。");
        segments[0].ShouldContain("clock=Day 1 · 06:00");
        segments[0].ShouldContain("conserved=True");

        // 巨兽段：段首两个 token 是身份（不能拿 tier= 当身份），排在头部段之后、建筑段之前。
        segments[1].ShouldBe(
            "BEAST beast tier=1 form=original ritual=- dwell=0/240 fed=0/400 "
            + "sat=80 dig=0 ovf=0 crystal=0/0/0");

        // 建筑段：B <建筑 id> <蓝图 id>
        segments.Count(s => s.StartsWith("B ")).ShouldBe(6, "开局 6 栋场地建筑（playable-spec 定的那一档）。");
        segments.ShouldContain(s => s.StartsWith("B town_hall TownHall "));

        // 土壤段：8 格固定。
        segments.Count(s => s.StartsWith("SOIL ")).ShouldBe(8, "土壤 8 格固定输出。");
        segments.ShouldContain("SOIL 0_1 dug=0 cleared=0 embedded=0 cave=0 regrowth=0.00 miners=0/3");
        segments.ShouldContain("SOIL 0_2 dug=0 cleared=0 embedded=0 cave=4 regrowth=0.00 miners=0/3");

        // 活物段：先 entity 那一行，再按登记序一个一段。
        // 因开局人口 50→12：entity count / issued 从 50 改成 12。
        segments.ShouldContain("entity count=12 issued=12");
        segments.ShouldContain(s => s.StartsWith(
            "U w1 role=worker stats=20/20/20 hp=100/100 state=standing loyalty=100 mood=0 post=- pos=",
            StringComparison.Ordinal));

        // 点位物段：开局没有尸体也没有散货。
        segments.ShouldContain("item count=0 issued=0");

        // 每段的前两个 token 都是身份（L2 语义档判红靠这个）。
        foreach (var segment in segments.Skip(1))
        {
            var tokens = segment.Split(' ').Where(t => t.Length > 0).Take(2).Count();
            tokens.ShouldBe(2, $"段「{segment[..System.Math.Min(24, segment.Length)]}…」的前两个 token 是身份。");
        }
    }

    [Fact]
    public void 重放快照在调试快照之后追加两个寻路空间段()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Advance(20);

        var replay = world.ReplaySnapshot();
        replay.ShouldStartWith(world.DebugSnapshot(), Case.Sensitive, "重放快照以调试快照打头。");
        replay.ShouldContain(" | [甲板] ", Case.Sensitive, "甲板寻路段在。");
        replay.ShouldContain(" | [大地图] ", Case.Sensitive, "大地图寻路段在。");
        replay.ShouldNotContain(";", Case.Sensitive, "分号转空格（重放段按空格分隔）。");
    }

    /// <summary>
    /// 因开局人口 50→12（25/15/10 → 8/3/1）：口粮基数与名单序号全部按新人数重算。
    /// </summary>
    [Fact]
    public void 开局十二名镇民是八工人三角斗士一自由人()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Roster.WorkerCount.ShouldBe(8);
        world.Roster.GladiatorCount.ShouldBe(3);
        world.Roster.FreePersonCount.ShouldBe(1);
        world.Roster.Townspeople.Count().ShouldBe(12, "每类人每日各耗 1 份口粮，全镇 12 份/游戏日。");

        // 名单序号终生不变：按登记序读出来逐个对上。
        var ordered = world.Roster.Units.ToList();
        ordered[0].Id.ShouldBe("w1");
        ordered[0].RosterIndex.ShouldBe(0);
        ordered[7].Id.ShouldBe("w8");
        ordered[8].Id.ShouldBe("g1");
        ordered[10].Id.ShouldBe("g3");
        ordered[11].Id.ShouldBe("f1");
        ordered[11].RosterIndex.ShouldBe(11);

        // 发人之后六本账与法力那一本仍然平（人不是账目，但开局那一步不许把账带歪）。
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }
}
