#nullable enable
using System.Collections.Generic;
using System.Linq;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// ticket 26：事件发射补齐 —— 每条新事件至少一格，钉四样：触发时机、载荷、被拒不发、只发一次。
///
/// <para><b>订阅一律走世界口（<see cref="ArenaWorld"/> 转发）</b>：子系统直订是冲突 C-5 要防的那一类，
/// 本文件与表现层同一口径。</para>
///
/// <para><b>最终防守那一档（DefenceStarted=FinalDefence 与 FinalRetryStarted）不在这里</b>：
/// 摆「双成长完成 + 富城段」的起点在 <see cref="FinalDefenceTests"/>，那两格钉进它已有的辅助里。</para>
/// </summary>
public class EventEmissionTests
{
    private const int Seed = 7;

    private const int Sector = UndergroundSoil.CaveSector;
    private const int Layer = UndergroundSoil.NativeCaveLayer;
    private const string TrainingGround = "training_ground";

    /// <summary>挖开洞穴那一格（自上而下两格）。</summary>
    private static void DigCave(ArenaWorld world)
    {
        world.Soil.BeginDigging(Sector, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.BeginDigging(Sector, Layer, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(Sector, Layer).ShouldBeTrue("洞穴那一格挖开了。");
    }

    private static void KillCaveMonsters(ArenaWorld world)
    {
        foreach (var monster in world.Roster.Units
                     .Where(u => u.Role == UnitRole.Monster && !u.LeftTown && u.State != UnitState.Dead)
                     .ToList())
        {
            world.Battle.ApplyDamage(world.Tick, monster, 1000.0);
        }
    }

    // ---- 魔晶嵌入与二阶层门 ----

    [Fact]
    public void CrystalEmbedded_嵌入成功发一次_被拒不发_重复嵌入不重发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        var embedded = new List<(int Sector, int Layer)>();
        world.CrystalEmbedded += (sector, layer) => embedded.Add((sector, layer));

        // ① 被拒（还没挖开）：不发。
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidPhase);
        embedded.ShouldBeEmpty("嵌入没走成不发事件。");

        // ② 三项摆齐、嵌入成功：发一次，载荷定位到那一格。
        DigCave(world);
        world.Soil.MarkCaveCleared(Sector, Layer).ShouldBeTrue();
        world.Town.ProduceCrystal(1.0);
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidArgs);
        embedded.ShouldBe([(Sector, Layer)], "嵌入成功发一次，象限与层进载荷。");

        // ③ 同格重复嵌入被拒：不重发。
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidPhase);
        embedded.Count.ShouldBe(1, "重复嵌入被拒，事件不重发。");
    }

    [Fact]
    public void TierGateSatisfied_三项首次凑齐发一次_永久不重复()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        var gates = new List<(int Sector, int Layer)>();
        world.TierGateSatisfied += (sector, layer) => gates.Add((sector, layer));

        // 只挖到第二层：不发。
        DigCave(world);
        gates.ShouldBeEmpty("只挖到第二层不发门槛事件。");

        // 加上清剿：还不发。
        world.Soil.MarkCaveCleared(Sector, Layer).ShouldBeTrue();
        gates.ShouldBeEmpty("还差嵌入魔晶不发门槛事件。");

        // 三项在同一次嵌入成功时凑齐：发一次，载荷带象限与层。
        world.Town.ProduceCrystal(1.0);
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidArgs);
        gates.ShouldBe([(Sector, Layer)], "三项首次同时满足发一次。");

        // 永久不重复：门槛旗标已落，之后再走嵌入路径也不会发第二发（同格已嵌入，直接被拒）。
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidPhase);
        gates.Count.ShouldBe(1, "门槛满足事件一生只发一次。");
    }

    // ---- 进阶开始 ----

    [Fact]
    public void TierUpgradeStarted_仪式开起来发一次_被拒不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();

        var fired = new List<(BeastTier Target, long DoneTick)>();
        world.TierUpgradeStarted += (target, doneTick) => fired.Add((target, doneTick));

        // ① 门槛未到被拒：不发。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var gateWhy).ShouldBeFalse();
        gateWhy.ShouldStartWith($"rejected: {RejectReason.GateNotMet}", Case.Sensitive);
        fired.ShouldBeEmpty("被拒的进阶不发事件。");

        // ② 受理成功：发一次，带目标位阶与仪式到点步号。
        world.Tier2Ready();
        var tickAtStart = world.Tick;
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        var expectedDoneTick = tickAtStart
            + (long)(ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours);
        fired.ShouldBe([(BeastTier.Tier2, expectedDoneTick)],
            "载荷 = 目标位阶 + 完成步号（仪式时长查数值表）。");

        // ③ 仪式期间再发被拒：不重发。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out _).ShouldBeFalse();
        fired.Count.ShouldBe(1, "被拒的进阶不重发事件。");
    }

    // ---- 拔锚与防守开始 ----

    [Fact]
    public void AnchorPulled_拔锚成功发一次带城id_被拒不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var cities = new List<string>();
        world.AnchorPulled += cityId => cities.Add(cityId);

        // ① 不停靠期：被拒，不发。
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out _).ShouldBeFalse();
        cities.ShouldBeEmpty("拔锚没走成不发事件。");

        // ② 停靠后拔锚：发一次，带拔锚那座城的 id。
        world.DockedAt(BigMapGrid.PoorCity.Id);
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out var why).ShouldBeTrue(why);
        cities.ShouldBe([BigMapGrid.PoorCity.Id], "拔锚成功发一次，载荷是城 id。");
    }

    [Fact]
    public void DefenceStarted_防守开起来发一次带场合_办赛与清剿不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var occasions = new List<BattleOccasion>();
        world.DefenceStarted += occasion => occasions.Add(occasion);

        // ① 拔锚被拒：不发。
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out _).ShouldBeFalse();
        occasions.ShouldBeEmpty("防守没开起来不发事件。");

        // ② 普通防守开起来：发一次，场合 = Defence。
        world.DockedAt(BigMapGrid.PoorCity.Id);
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out var why).ShouldBeTrue(why);
        occasions.ShouldBe([BattleOccasion.Defence], "普通防守开始发一次，载荷是场合。");

        // ③ 办赛与洞穴清剿不发这一条。
        var second = ArenaWorldBuilder.New(Seed).Build();
        second.OnDocked(BigMapGrid.PoorCity);
        var secondOccasions = new List<BattleOccasion>();
        second.DefenceStarted += occasion => secondOccasions.Add(occasion);
        ArenaSetup.Inject(second, "StartMatch", "g1,g2|g3", out _, out _).ShouldBeTrue();
        second.Advance(ArenaTime.StepsPerGameHour * 2);   // 先把这一场打完，场合回到 None。
        second.DugTo(Sector, Layer);
        ArenaSetup.Inject(second, "ClearCave", $"{Sector}|{Layer}", out _, out var caveWhy)
            .ShouldBeTrue(caveWhy);
        secondOccasions.ShouldBeEmpty("办赛与清剿不是防守，不发 DefenceStarted。");
    }

    // ---- 洞穴五连 ----

    [Fact]
    public void CaveOpened_挖开洞穴格发一次_非洞穴格不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        var opened = new List<(int Sector, int Layer)>();
        world.CaveOpened += (sector, layer) => opened.Add((sector, layer));

        // 第 1 层不是洞穴格：挖开发空。
        world.Soil.BeginDigging(Sector, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        opened.ShouldBeEmpty("非洞穴格不发洞口事件。");

        // 第 2 层是洞穴格：挖开那一刻发一次。
        world.Soil.BeginDigging(Sector, Layer, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        opened.ShouldBe([(Sector, Layer)], "挖开洞穴格发一次。");
    }

    [Fact]
    public void CaveClearStarted_受理发一次_被拒不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);

        var started = new List<(int Sector, int Layer)>();
        world.CaveClearStarted += (sector, layer) => started.Add((sector, layer));

        // ① 正在打另一场之前的普通被拒：没挖开的象限 1 第 2 层不是洞穴格 → InvalidTarget。
        ArenaSetup.Inject(world, "ClearCave", $"1|{Layer}", out _, out _).ShouldBeFalse();
        started.ShouldBeEmpty("清剿没被受理不发事件。");

        // ② 受理成功：发一次带象限与层。
        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out var why).ShouldBeTrue(why);
        started.ShouldBe([(Sector, Layer)], "清剿受理发一次。");
    }

    [Fact]
    public void CaveCleared_四只全灭收场发一次()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);

        var cleared = new List<(int Sector, int Layer)>();
        var failed = new List<(int Sector, int Layer, CaveFailReason Reason)>();
        world.CaveCleared += (sector, layer) => cleared.Add((sector, layer));
        world.CaveClearFailed += (sector, layer, reason) => failed.Add((sector, layer, reason));

        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out _).ShouldBeTrue();
        KillCaveMonsters(world);
        world.Advance(ArenaTime.StepsPerGameMinute);

        cleared.ShouldBe([(Sector, Layer)], "四只全灭收场发一次清剿胜利。");
        failed.ShouldBeEmpty("胜利的那一场不发失败事件。");
        world.Soil.IsCleared(Sector, Layer).ShouldBeTrue("事件与格状态同一步成立（先改状态后发事件）。");
    }

    [Fact]
    public void CaveClearFailed_撤退带Retreated_超时带Timeout_各发一次()
    {
        // ① 撤退那一发：载荷是封闭枚举的 Retreated。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);

        var failed = new List<(int Sector, int Layer, CaveFailReason Reason)>();
        world.CaveClearFailed += (sector, layer, reason) => failed.Add((sector, layer, reason));

        ArenaSetup.Inject(world, "RetreatCave", string.Empty, out _, out _).ShouldBeFalse();
        failed.ShouldBeEmpty("没在清剿时撤退被拒，不发事件。");

        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out _).ShouldBeTrue();
        ArenaSetup.Inject(world, "RetreatCave", string.Empty, out _, out var retreatWhy)
            .ShouldBeTrue(retreatWhy);
        failed.ShouldBe([(Sector, Layer, CaveFailReason.Retreated)], "撤退失败发一次，原因是撤退。");

        // ② 超时那一发：全员不参战，只能等到时限，原因是 Timeout。
        var second = ArenaWorldBuilder.New(Seed).Build();
        second.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(second);
        foreach (var u in second.Roster.Townspeople) u.NonCombatant = true;

        var secondFailed = new List<CaveFailReason>();
        second.CaveClearFailed += (_, _, reason) => secondFailed.Add(reason);

        ArenaSetup.Inject(second, "ClearCave", $"{Sector}|{Layer}", out _, out _).ShouldBeTrue();
        second.AdvanceTo(second.Battle.EndTick + ArenaTime.StepsPerGameMinute);
        secondFailed.ShouldBe([CaveFailReason.Timeout], "超时失败发一次，原因是超时。");
    }

    [Fact]
    public void CaveReset_洞穴格回长填满发一次()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);

        var reset = new List<(int Sector, int Layer)>();
        world.CaveReset += (sector, layer) => reset.Add((sector, layer));

        // 回长按整游戏日推进：第 2 层每游戏日 2 层速，一天必填满塌掉。
        world.Advance(ArenaTime.StepsPerGameDay);
        reset.ShouldBe([(Sector, Layer)], "洞穴格回长重置发一次。");
        world.Soil.IsDug(Sector, Layer).ShouldBeFalse("事件与格状态同步（先重置后发事件）。");
        world.Soil.CaveMonstersAt(Sector, Layer).ShouldBe(ArenaTuning.NativeCaveMonsters,
            "重置 = 洞里重新住满四只。");
    }

    // ---- 经济 ----

    [Fact]
    public void TicketIncome_办赛收场入账后发一次_金额与账本一致()
    {
        // ① 办赛被拒（不停靠）：不发。
        var idle = ArenaWorldBuilder.New(Seed).Build();
        var idleIncomes = new List<TicketIncomeRecord>();
        idle.TicketIncome += record => idleIncomes.Add(record);
        ArenaSetup.Inject(idle, "StartMatch", "g1,g2|g3", out _, out _).ShouldBeFalse();
        idle.Advance(ArenaTime.StepsPerGameHour * 2);
        idleIncomes.ShouldBeEmpty("办赛没开起来不发门票事件。");

        // ② 收场入账：发一次，金额与镇账的门票累计同一份。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);

        var incomes = new List<TicketIncomeRecord>();
        world.TicketIncome += record => incomes.Add(record);

        ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out var why).ShouldBeTrue(why);
        incomes.ShouldBeEmpty("仗还没打完不发门票事件。");

        world.Advance(ArenaTime.StepsPerGameHour * 2);
        incomes.Count.ShouldBe(1, "一场办赛收场发一次门票入账。");
        incomes[0].Coin.ShouldBe(world.Town.TicketIncome, 1e-9, "载荷金额与账本同源。");
        incomes[0].Audience.ShouldBeGreaterThan(0, "停靠期有观众，读数带观众数。");
        world.Battle.LastReport!.GateIncome.ShouldBe(incomes[0].Coin, 1e-9, "战报与事件是同一笔账。");
    }

    [Fact]
    public void RepairSpent_备件实扣后发一次_被拒不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        var spent = new List<RepairSpentRecord>();
        world.RepairSpent += record => spent.Add(record);

        // ① 满耐久被拒：不发。
        ArenaSetup.Inject(world, "RepairBuilding", TrainingGround, out _, out _).ShouldBeFalse();
        spent.ShouldBeEmpty("修理没受理不发事件。");

        // ② 打掉一点耐久、派上一名工作人员，再修：备件实扣，发一次。
        ArenaSetup.Inject(world, "AssignPost", $"w1|{TrainingGround}", out _, out var assignWhy)
            .ShouldBeTrue(assignWhy);
        world.DamageBuilding(TrainingGround, 30.0);
        var partsBefore = world.Town.SparePart;
        ArenaSetup.Inject(world, "RepairBuilding", TrainingGround, out _, out var repairWhy)
            .ShouldBeTrue(repairWhy);
        world.Town.SparePart.ShouldBe(partsBefore - ArenaTuning.RepairSpareParts, 1e-9,
            "备件真的扣了（事件钉在实扣之后）。");
        spent.Count.ShouldBe(1, "修理受理发一次。");
        spent[0].BuildingId.ShouldBe(TrainingGround);
        spent[0].SpareParts.ShouldBe(ArenaTuning.RepairSpareParts, 1e-9, "载荷带实际消耗。");
    }

    [Fact]
    public void HealingSpent_法力实扣后发一次_被拒不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var wounded = world.Roster.Find("w1")!;
        wounded.State = UnitState.Wounded;

        var spent = new List<HealingSpentRecord>();
        world.HealingSpent += record => spent.Add(record);

        // ① 没伤的被治：被拒，不发。
        ArenaSetup.Inject(world, "CastHeal", "w2", out _, out _).ShouldBeFalse();
        spent.ShouldBeEmpty("治疗没走成不发事件。");

        // ② 治好重伤：法力实扣一格，发一次带对象与消耗。
        var manaBefore = world.Town.AvailableMana;
        ArenaSetup.Inject(world, "CastHeal", "w1", out _, out var why).ShouldBeTrue(why);
        (manaBefore - world.Town.AvailableMana).ShouldBe(1, "即时消耗真的扣了 1 格。");
        spent.Count.ShouldBe(1, "治疗成交发一次。");
        spent[0].UnitId.ShouldBe("w1");
        spent[0].Mana.ShouldBe(1.0, 1e-9, "载荷带法力消耗。");
    }

    // ---- ForageGaveUp 枚举化 ----

    [Fact]
    public void ForageGaveUp_载荷是封闭枚举_两句文案由枚举派生()
    {
        // 纯函数一格：两项枚举各映射回原来的那句中文（横幅与读数同源，契约「唯一来源」）。
        ForageGiveUpText.TextOf(ForageGiveUpReason.NoTargetInRange)
            .ShouldBe("附近 30 米没有可吞的生态对象，返航（一游戏小时内不再自动尝试）");
        ForageGiveUpText.TextOf(ForageGiveUpReason.NoTargetLeft)
            .ShouldBe("周围没有可吞的生态对象了，返航（一游戏小时内不再自动尝试）");
    }
}
