#nullable enable
using System;
using System.Globalization;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 巨兽面板那一张 ticket：<c>Feed</c> 动词（喂食 / 封顶 / 粮不足）、进阶按钮的九个置灰档、
/// 形态锁定拒绝、快照里的形态解锁旗标。
///
/// <para><b>九个置灰档怎么摆</b>：每一档都只留<b>一个</b>理由 ——
/// 其余的门槛与三本账都先备齐，缺的那一档才是唯一的拦路。
/// 断言三件事：面板那一档的拦路（<see cref="ArenaSetup.AdvanceBlockerOf"/>）、
/// 拦路对应的拒绝码（<see cref="ArenaSetup.RejectReasonOf"/>）、
/// 以及逻辑层自己判的拒绝码（<see cref="ArenaWorld.AdvanceTier"/>）——
/// 面板那一档是后者的只读镜像，两处必须一致。</para>
///
/// <para><b>挖开门槛那一格怎么摆</b>：照现役 <c>UndergroundSoilTests</c> 的写法走
/// 「<see cref="UndergroundSoil.BeginDigging"/> + 派满工人 + 推进一个游戏小时」那一串，
/// 不造捷径；真值是 <see cref="UndergroundSoil.IsDug"/>。</para>
/// </summary>
public class BeastPanelTests
{
    private const int Seed = 7;

    /// <summary>面板上那颗「喂 50 食物」按下去的份数。</summary>
    private const int FeedAmount = 50;

    /// <summary>账本指纹（被拒的一发前后要逐项相等）。</summary>
    private readonly record struct Ledger(
        double Coin, double Food, double Soul, double Ore, double Material,
        double Satiation, double Granary, double FedTotal);

    private static Ledger Fingerprint(ArenaWorld world)
    {
        var t = world.Town;
        return new Ledger(t.Coin, t.Food, t.SoulStone, t.Ore, t.SpecialMaterial,
            world.Voyage.Satiation, world.Voyage.GranaryFood, world.Voyage.FedTotalFood);
    }

    /// <summary>面板那一档的置灰与逻辑层的拒绝码要对得上。</summary>
    private static void ShouldBeBlocked(ArenaWorld world, AdvanceBlocker blocker, RejectReason reason)
    {
        ArenaSetup.AdvanceBlockerOf(world).ShouldBe(blocker, "面板上那颗进阶按钮的拦路。");
        ArenaSetup.RejectReasonOf(blocker).ShouldBe(reason, "拦路那一档对应词表里的拒绝码。");
        world.AdvanceTier().ShouldBe(reason, "逻辑层自己判的拒绝码与面板那一档一致。");
    }

    // === 喂食 ===

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 喂五十份食物_饱食加十点_两处存粮各扣五十()
    {
        // Arrange：一局新世界，先记下喂之前的饱食度。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = world.Voyage.Satiation;

        // Act
        ArenaSetup.Inject(world, "Feed", FeedAmount.ToString(CultureInfo.InvariantCulture),
            out var canonical, out var why).ShouldBeTrue(why);

        // Assert：入录的参数是 50；饱食加 10 点；镇仓与兽粮各扣 50 份。
        canonical.ShouldBe("50", "入录的参数是规范化后的那一份数。");
        (world.Voyage.Satiation - before).ShouldBe(10.0, 1e-9, "50 份食物换 10 点饱食（5:1）。");
        world.Town.Food.ShouldBe(500.0 - FeedAmount, 1e-9, "镇仓扣 50 份。");
        world.Voyage.GranaryFood.ShouldBe(500.0 - FeedAmount, 1e-9, "兽粮扣 50 份。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 饱食九十五时喂五十份_喂不饱就当场拒_一格不动()
    {
        // Arrange：饱食度摆到 95，先记下账本指纹。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(95.0);

        var before = Fingerprint(world);

        // Act
        ArenaSetup.Inject(world, "Feed", FeedAmount.ToString(CultureInfo.InvariantCulture),
            out _, out var why).ShouldBeFalse();

        // Assert：回 InsufficientFood；被拒的一发一格不动。
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientFood}", Case.Sensitive,
            "饱食度只够 25 份，喂不满 50 份就当场拒。");
        why.ShouldContain("还能吃 25 份", Case.Sensitive, "缺口那一句话写清还能吃几份。");
        Fingerprint(world).ShouldBe(before, "被拒的一发一格不动（吃不下就不许白扣那 50 份）。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 饱食九十五时喂二十五份_吃得下_饱食封顶一百()
    {
        // Arrange：饱食度摆到 95。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(95.0);

        // Act
        ArenaSetup.Inject(world, "Feed", "25", out _, out var why).ShouldBeTrue(why);

        // Assert：饱食封顶 100，镇仓与兽粮各扣 25 份。
        world.Voyage.Satiation.ShouldBe(ArenaTuning.SatiationMax, 1e-9, "25 份食物换 5 点饱食，封顶 100。");
        world.Town.Food.ShouldBe(475.0, 1e-9, "镇仓扣 25 份。");
        world.Voyage.GranaryFood.ShouldBe(475.0, 1e-9, "兽粮扣 25 份。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 满饱食度喂五十份_当场拒_五十份食物一格不扣()
    {
        // Arrange：饱食度摆满，先记下账本指纹。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);

        var before = Fingerprint(world);

        // Act
        ArenaSetup.Inject(world, "Feed", FeedAmount.ToString(CultureInfo.InvariantCulture),
            out _, out var why).ShouldBeFalse();

        // Assert：回 InsufficientFood；白扣 50 份食物那一格不许再发生。
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientFood}", Case.Sensitive,
            "兽已经吃饱了，喂不进去。");
        Fingerprint(world).ShouldBe(before, "白扣 50 份食物那一格不许再发生。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 镇仓存粮不够_回InsufficientFood_一格不动()
    {
        // Arrange：镇仓只剩 30 份，先记下账本指纹。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.TrySpendFood(world.Town.Food - 30.0).ShouldBeTrue("镇仓只剩 30 份。");

        var before = Fingerprint(world);

        // Act
        ArenaSetup.Inject(world, "Feed", FeedAmount.ToString(CultureInfo.InvariantCulture),
            out _, out var why).ShouldBeFalse();

        // Assert：回 InsufficientFood；被拒的一发一格不动。
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientFood}", Case.Sensitive,
            "存粮不够回 InsufficientFood。");
        Fingerprint(world).ShouldBe(before, "被拒的一发一格不动。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 兽粮只有三十份_回InsufficientFood_不喂一半()
    {
        // Arrange：兽粮只有 30 份。
        // 镇仓 500 份，兽粮只有 30 份 —— 那一发要喂 50 份，喂不满。
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(30.0).Build();

        var before = Fingerprint(world);

        // Act
        ArenaSetup.Inject(world, "Feed", FeedAmount.ToString(CultureInfo.InvariantCulture),
            out _, out var why).ShouldBeFalse();

        // Assert：喂不满就当场拒；饱食与两处存粮一格不动。
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientFood}", Case.Sensitive,
            "喂不满就当场拒。");
        Fingerprint(world).ShouldBe(before, "不喂一半：饱食与两处存粮一格不动。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 喂食参数不是正整数_回InvalidArgs_一口都不喂()
    {
        // Arrange：一局新世界，先记下账本指纹。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = Fingerprint(world);

        // Act：喂食参数逐个试。
        foreach (var args in new[] { string.Empty, "0", "-5", "abc", "12.5" })
        {
            // Assert：参数不合法的那一发发不出去，回 InvalidArgs。
            ArenaSetup.Inject(world, "Feed", args, out _, out var why).ShouldBeFalse(
                $"参数「{args}」那一发发不出去。");
            why.ShouldStartWith($"rejected: {RejectReason.InvalidArgs}", Case.Sensitive,
                $"参数「{args}」回 InvalidArgs（不兜 0）。");
        }

        Fingerprint(world).ShouldBe(before, "参数不合法的一发一口都不喂。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 词表里有喂食这一条()
    {
        // Arrange：面板那一档的动词词表。
        // Act：查词表里有没有 Feed。
        var isFeed = ArenaSetup.IsVerb("Feed");

        // Assert：词表里有 Feed 这一条。
        isFeed.ShouldBeTrue("词表里有 Feed。");
        ArenaSetup.VerbNames.ShouldContain("Feed", "Feed 在词表名单里。");
    }

    // === 进阶按钮的九个置灰档 ===

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_二阶之后_三阶不在切片内回GateNotMet()
    {
        // Arrange：停靠贫城、备齐三本账、二阶门槛三项摆齐。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Stocked();
        world.Tier2Ready();

        // Act：上到二阶。
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours));
        world.Tier.ShouldBe(BeastTier.Tier2, "先上二阶。");

        // Assert：三阶的门是关的（地下只有两层）—— 面板那一档回 GateNotMet，不是 TierAtMax。
        ShouldBeBlocked(world, AdvanceBlocker.GateNotMet, RejectReason.GateNotMet);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_不在停靠期_回InvalidPhase()
    {
        // Arrange：备齐三本账、挖到二阶门槛那一层，但这一局还没停靠。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Stocked();
        world.DugTo(0, ArenaTuning.Tier2GateLayer);
        world.IsDocked.ShouldBeFalse("这一局还没停靠。");

        // Act：进阶那一发，逻辑层自己判那一档。
        // Assert：面板那一档回 NotDocked，对应的拒绝码是 InvalidPhase。
        ShouldBeBlocked(world, AdvanceBlocker.NotDocked, RejectReason.InvalidPhase);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_正在打仗_回InvalidPhase()
    {
        // Arrange：停靠贫城、备齐三本账、挖到二阶门槛那一层。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Stocked();
        world.DugTo(0, ArenaTuning.Tier2GateLayer);

        // Act
        ArenaSetup.Inject(world, "StartMatch", "g1,g2|g3", out _, out var why).ShouldBeTrue(why);

        // Assert：场上有一场；面板那一档回 InBattle，对应的拒绝码是 InvalidPhase。
        world.Battle.Occasion.ShouldBe(BattleOccasion.Match, "场上有一场。");
        ShouldBeBlocked(world, AdvanceBlocker.InBattle, RejectReason.InvalidPhase);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 城破当格解救_进阶不再被停摆拦()
    {
        // Arrange：停靠贫城、挖到二阶门槛那一层。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.DugTo(0, ArenaTuning.Tier2GateLayer);

        // Act：攻破当格解救（ticket 06 起没有三个游戏日的停摆期），账随后备齐。
        world.ReportBreach();
        world.InStandstill.ShouldBeFalse("城破当格解救，不再进停摆期。");
        world.StandstillUntilTick.ShouldBe(0, "停摆到期步号恒为 0。");
        world.Stocked();

        // Assert：面板不再回 InStandstill；这一局的门槛没过，判在 GateNotMet。
        ShouldBeBlocked(world, AdvanceBlocker.GateNotMet, RejectReason.GateNotMet);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 城破当格解救之后重摆竞技场开赛_面板判在打仗那一档()
    {
        // Arrange：停靠贫城、挖到二阶门槛那一层。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.DugTo(0, ArenaTuning.Tier2GateLayer);

        // Act：① 攻破当格解救（那一格顺带拆掉最贵的三栋 —— 竞技场也在里头），
        // 但没有停摆期挡住后面的事。
        world.ReportBreach();
        world.InStandstill.ShouldBeFalse("城破当格解救。");

        // ② 重摆一座竞技场，再直接开一场 —— 打仗这一档成立。
        world.Deck.PlaceBuilding("Arena", -2, -8, "arena2")
            .Ok.ShouldBeTrue("竞技场摆得下（原来那座被攻破拆了）。");
        world.Battle.StartMatch(new[] { "g1", "g2" }, new[] { "g3" })
            .ShouldBe(RejectReason.InvalidArgs, "场上开起一场。");
        world.Battle.Occasion.ShouldBe(BattleOccasion.Match, "正在打仗。");
        world.Stocked();

        // Assert：面板那一档回 InBattle，对应的拒绝码是 InvalidPhase。
        ShouldBeBlocked(world, AdvanceBlocker.InBattle, RejectReason.InvalidPhase);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_仪式期间_回InvalidPhase()
    {
        // Arrange：停靠贫城、备齐三本账、挖到二阶门槛那一层。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Stocked();
        world.Tier2Ready();

        // Act
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);

        // Assert：仪式开着；面板那一档回 InRitual，对应的拒绝码是 InvalidPhase。
        world.InRitual.ShouldBeTrue("仪式开着。");
        ShouldBeBlocked(world, AdvanceBlocker.InRitual, RejectReason.InvalidPhase);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_门槛未到_回GateNotMet()
    {
        // Arrange：停靠贫城、备齐三本账，但第二层还没挖开。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Stocked();
        world.Soil.IsDug(0, ArenaTuning.Tier2GateLayer).ShouldBeFalse("还没挖到第二层。");

        // Act：进阶那一发，逻辑层自己判那一档。
        // Assert：面板那一档回 GateNotMet，对应的拒绝码也是 GateNotMet。
        ShouldBeBlocked(world, AdvanceBlocker.GateNotMet, RejectReason.GateNotMet);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_魂石不足_回InsufficientSoulStone()
    {
        // Arrange：停靠贫城、挖到二阶门槛那一层，账备到只差魂石。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Tier2Ready();

        world.Town.ProduceSpecialMaterial(200.0);
        world.Town.ProduceCoin(50000.0);
        world.Town.TrySpendSoulStone(world.Town.SoulStone - 10.0).ShouldBeTrue("魂石只剩 10。");

        // Act：进阶那一发，逻辑层自己判那一档。
        // Assert：面板那一档回 NotEnoughSoulStone，对应的拒绝码是 InsufficientSoulStone。
        ShouldBeBlocked(world, AdvanceBlocker.NotEnoughSoulStone, RejectReason.InsufficientSoulStone);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_特殊材料不足_回InsufficientMaterial()
    {
        // Arrange：停靠贫城、挖到二阶门槛那一层，账备到只差特殊材料。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Tier2Ready();

        world.Town.ProduceSoulStone(1000.0);
        world.Town.ProduceCoin(50000.0);
        world.Town.TrySpendSpecialMaterial(world.Town.SpecialMaterial - 1.0)
            .ShouldBeTrue("特殊材料只剩 1。");

        // Act：进阶那一发，逻辑层自己判那一档。
        // Assert：面板那一档回 NotEnoughMaterial，对应的拒绝码是 InsufficientMaterial。
        ShouldBeBlocked(world, AdvanceBlocker.NotEnoughMaterial, RejectReason.InsufficientMaterial);
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 进阶置灰_金币不足_回InsufficientCoin()
    {
        // Arrange：停靠贫城、挖到二阶门槛那一层，账备到只差金币。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Tier2Ready();

        world.Town.ProduceSoulStone(1000.0);
        world.Town.ProduceSpecialMaterial(200.0);
        world.Town.TrySpendCoin(world.Town.Coin - 100.0).ShouldBeTrue("金币只剩 100。");

        // Act：进阶那一发，逻辑层自己判那一档。
        // Assert：面板那一档回 NotEnoughCoin，对应的拒绝码是 InsufficientCoin。
        ShouldBeBlocked(world, AdvanceBlocker.NotEnoughCoin, RejectReason.InsufficientCoin);
    }

    // === 形态 ===

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 切到未解锁形态_回FormLocked_形态不变()
    {
        // Arrange：一局新世界，荒漠态开局没解锁。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.FormUnlocked(BeastForm.Desert).ShouldBeFalse("荒漠态开局没解锁。");

        // Act
        ArenaSetup.Inject(world, "SwitchForm", "desert", out _, out var why).ShouldBeFalse();

        // Assert：回 FormLocked；形态不变。
        why.ShouldStartWith($"rejected: {RejectReason.FormLocked}", Case.Sensitive,
            "没解锁回 FormLocked。");
        world.Form.ShouldBe(BeastForm.Original, "形态不变。");
    }

    // === 快照 ===

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 快照头部带两个形态的解锁旗标()
    {
        // Arrange：一局新世界。
        var world = ArenaWorldBuilder.New(Seed).Build();

        // Act：取快照头部那一段。
        var header = world.DebugSnapshot().Split(" | ")[0];

        // Assert：头部带着两个形态的解锁旗标。
        header.ShouldContain("form_unlocked=desert:0/1,glutton:0/1", Case.Sensitive,
            "两个形态都没解锁。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 荒漠态解锁之后_快照那一格翻成desert1()
    {
        // Arrange：一局新世界，荒漠态累计计时摆到阈值。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetDwellGameMinutes(ArenaTuning.DesertFormDwellGameMinutes);

        // Act：推进一步。
        world.Advance(1);

        // Assert：荒漠态解锁了，快照那一格翻成 desert1。
        world.FormUnlocked(BeastForm.Desert).ShouldBeTrue("沙漠累计计时到阈值就解锁。");

        var header = world.DebugSnapshot().Split(" | ")[0];
        header.ShouldContain("form_unlocked=desert:1/1,glutton:0/1", Case.Sensitive,
            "荒漠态那一格翻成 1。");
    }
}
