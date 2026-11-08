#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 巨兽形态：两套解锁条件（荒漠态按沙漠累计计时、暴食态按累计喂食且已驶离贫城）、
/// 切换（瞬态、无代价、幂等）、两套效果（食性与土壤偏向）、快照那一段
/// 与 <see cref="ArenaWorld.FormSwitched"/> 事件。
///
/// <para><b>把巨兽摆到沙漠格怎么摆</b>：走 <see cref="NavStepper.ApplyMove"/>（寻路层公开的落点写口，
/// 落点即位置）。判据是「脚下那一格」，所以把落点摆进沙漠带那一格，验的就是那一条规则本身 ——
/// 摆位只是把巨兽放到那一格，计时与解锁判定没有一步被绕过。</para>
///
/// <para><b>解锁的两条路各验一次</b>：直写口（<see cref="BeastVoyage.SetDwellGameMinutes"/> /
/// <see cref="BeastVoyage.AddFedTotalFood"/>）验「计数越过阈值就上锁」，
/// 真在沙漠格里推进分钟验「按所在格判」。</para>
/// </summary>
public class BeastFormTests
{
    private const int Seed = 7;

    /// <summary>把巨兽落到沙城那一格（沙漠带里）。</summary>
    private static void PlaceInDesert(ArenaWorld world)
    {
        world.MapStepper.ApplyMove(BeastVoyage.BeastId, BigMapGrid.DesertCity.Position, 0.0);
        BigMapTerrain.TerrainAt(world.Voyage.Position).ShouldBe(TerrainKind.Desert, "落点那一格是沙漠。");
    }

    /// <summary>把巨兽落到贫城那一格（草地上）。</summary>
    private static void PlaceInGrass(ArenaWorld world)
    {
        world.MapStepper.ApplyMove(BeastVoyage.BeastId, BigMapGrid.PoorCity.Position, 0.0);
        BigMapTerrain.TerrainAt(world.Voyage.Position).ShouldBe(TerrainKind.Grass, "落点那一格是草地。");
    }

    /// <summary>解锁荒漠态：脚下是沙漠格，累计计时摆到阈值，再走一步让世界判解锁。</summary>
    private static void UnlockDesert(ArenaWorld world)
    {
        PlaceInDesert(world);
        world.Voyage.SetDwellGameMinutes(ArenaTuning.DesertFormDwellGameMinutes);
        world.Advance(1);
        world.FormUnlocked(BeastForm.Desert).ShouldBeTrue("沙漠累计计时到阈值就要解锁。");
    }

    /// <summary>解锁暴食态：先把局流程推到「已驶离贫城」，再把有效喂食摆到阈值，走一步让世界判解锁。</summary>
    private static void UnlockGlutton(ArenaWorld world)
    {
        world.LeftPoorCity();
        world.Voyage.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        world.Advance(1);
        world.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("驶离贫城且累计喂食到阈值就要解锁。");
    }

    /// <summary>切形态这一发（走注入口那一口，与玩家、调试桥、重放端同一路）。</summary>
    private static bool Switch(ArenaWorld world, string form)
        => ArenaSetup.Inject(world, "SwitchForm", form, out _, out _);

    /// <summary>「下一个整游戏小时的步号」（严格大于当前步号，且不落在整日那一档上）。</summary>
    private static long NextHourTick(ArenaWorld world)
    {
        var tick = ((world.Tick / ArenaTime.StepsPerGameHour) + 1) * ArenaTime.StepsPerGameHour;
        while (tick % ArenaTime.StepsPerGameDay == 0) tick += ArenaTime.StepsPerGameHour;
        return tick;
    }

    /// <summary>「下一个整游戏日的步号」（严格大于当前步号）。</summary>
    private static long NextDayTick(ArenaWorld world)
        => ((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay;

    /// <summary>量两个游戏小时里那一格攒了多少矿石（窗口对齐到整点，窗口里正好两个整点）。</summary>
    private static double MeasureOreTwoHours(ArenaWorld world, int sector, int layer)
    {
        world.AdvanceTo(NextHourTick(world));
        var before = world.Soil.OreAccumulatedAt(sector, layer);
        world.Advance(ArenaTime.StepsPerGameHour * 2);
        return world.Soil.OreAccumulatedAt(sector, layer) - before;
    }

    /// <summary>量两个游戏小时里那一格攒了多少特殊材料（高危生产区那一档）。</summary>
    private static double MeasureMaterialTwoHours(ArenaWorld world, int sector, int layer)
    {
        world.AdvanceTo(NextHourTick(world));
        var before = world.Soil.MaterialAccumulatedAt(sector, layer);
        world.Advance(ArenaTime.StepsPerGameHour * 2);
        return world.Soil.MaterialAccumulatedAt(sector, layer) - before;
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

    // ---- 解锁条件一：沙漠累计计时 ----

    [Fact]
    public void 沙漠格内待六百游戏分钟_dwell到六百_走出沙漠格仍是六百()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        PlaceInDesert(world);

        // 十个游戏小时 = 六百个游戏分钟 = 6000 步。
        world.Advance(ArenaTime.StepsPerGameMinute * 600);
        world.Voyage.DwellGameMinutes.ShouldBe(600, "在沙漠格里累计六百个游戏分钟。");

        // 走出沙漠格：累计口径不清零（旧口径「连续」在这里归零）。
        PlaceInGrass(world);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Voyage.DwellGameMinutes.ShouldBe(600, "离开沙漠不清零（累计）。");
    }

    [Fact]
    public void 累计二百四十游戏分钟_荒漠态解锁_没解锁时SwitchForm回FormLocked()
    {
        // ① 没解锁：那一发回 FormLocked，形态不动。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.DwellGameMinutes.ShouldBe(0, "开局没在沙漠格里待过。");
        world.FormUnlocked(BeastForm.Desert).ShouldBeFalse("荒漠态还没解锁。");

        ArenaSetup.Inject(world, "SwitchForm", "desert", out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.FormLocked}", Case.Sensitive,
            "没解锁的形态回 FormLocked。");
        world.Form.ShouldBe(BeastForm.Original, "被拒的那一发形态不动。");

        // ② 计数越过阈值：解锁。
        UnlockDesert(world);
        Switch(world, "desert").ShouldBeTrue("解锁之后那一发走成写路径。");
        world.Form.ShouldBe(BeastForm.Desert, "形态切到荒漠态。");

        // ③ 解锁是永久的：离开沙漠之后仍然是已解锁的（且累计不清零）。
        PlaceInGrass(world);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Voyage.DwellGameMinutes.ShouldBe(ArenaTuning.DesertFormDwellGameMinutes,
            "离开沙漠不清零。");
        world.FormUnlocked(BeastForm.Desert).ShouldBeTrue("解锁不随计数清零而回收。");
        Switch(world, "original").ShouldBeTrue("解锁之后仍能切回去。");
        Switch(world, "desert").ShouldBeTrue("解锁之后仍能切到荒漠态。");
        world.Form.ShouldBe(BeastForm.Desert);
    }

    [Fact]
    public void 形态名不认识回InvalidArgs_切到当前形态是幂等成功()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        ArenaSetup.Inject(world, "SwitchForm", "lava", out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.InvalidArgs}", Case.Sensitive,
            "形态名不认识回 InvalidArgs。");
        world.Form.ShouldBe(BeastForm.Original, "被拒的那一发形态不动。");

        // 原始态开局就有：切到当前形态是幂等成功。
        var rec = ArenaSetup.Recorder(world);
        rec.BeginSession();
        rec.Inject("SwitchForm", "original").ShouldBeTrue("切到当前形态也是成功。");
        world.Form.ShouldBe(BeastForm.Original);

        // 幂等那一发照样进 Journal（写路径进 Journal）。
        rec.Journal.Count.ShouldBe(1, "幂等切换也是一发写路径。");
        rec.Journal[0].Cmd.ShouldBe("SwitchForm");
        rec.Journal[0].Args.ShouldBe("original", "入录的是规范化的 token。");
        rec.Journal[0].Ok.ShouldBeTrue("幂等成功记 ok=true。");
    }

    // ---- 解锁条件二：累计喂食 + 已驶离贫城 ----

    [Fact]
    public void 贫城段内喂满四百不解锁暴食态_驶离贫城之后才解锁()
    {
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(2000.0).Build();

        // 段推到贫城段：开局 → 停靠贫城。
        world.Flow.Start().ShouldBeTrue("开局那一发推得动。");
        world.Flow.NoteDocked(BigMapGrid.PoorCity.Id).ShouldBeTrue("停到贫城上，段推到贫城段。");
        world.Flow.Stage.ShouldBe(RunStage.Poor, "段就是贫城段。");
        world.DepartedPoorCity.ShouldBeFalse("还停在贫城，没驶离。");

        // 贫城段内喂满 400：第二道门没开，暴食态不解锁。
        world.Voyage.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        world.Advance(1);
        world.FormUnlocked(BeastForm.Glutton).ShouldBeFalse("贫城段内喂满 400 也不解锁。");

        // 贫城拔锚防守赢了 → 段推到第一段航行：同一份计数下立刻解锁。
        world.Flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        world.Flow.NoteDefenceSettled(won: true);
        world.Flow.Stage.ShouldBe(RunStage.ToSand, "贫城防守赢了才驶得出去。");
        world.Advance(1);
        world.DepartedPoorCity.ShouldBeTrue("段进了第一段航行，算已驶离贫城。");
        world.FormUnlocked(BeastForm.Glutton).ShouldBeTrue("驶离贫城之后两道门都过了。");
        Switch(world, "glutton").ShouldBeTrue("解锁之后那一发走成写路径。");
        world.Form.ShouldBe(BeastForm.Glutton);
    }

    [Fact]
    public void 手动喂食凑够四百_当那一发就解锁_不用等下一步()
    {
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(2000.0).Build();
        world.LeftPoorCity();

        world.Voyage.SetSatiation(80.0);
        world.Voyage.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood - 100.0);
        world.Advance(1);
        world.FormUnlocked(BeastForm.Glutton).ShouldBeFalse("还差 100 食物。");

        world.FeedBeast(100.0).ShouldBe(RejectReason.InvalidArgs, "喂进去了不算被拒。");
        world.FormUnlocked(BeastForm.Glutton).ShouldBeTrue(
            "手动喂食那一发之后立刻判解锁，不等下一步。");
        world.Voyage.FedTotalFood.ShouldBe(ArenaTuning.GluttonFormFedTotalFood, 1e-9,
            "只有实际扣粮且有效恢复饱食度的那 100 食物进累计。");
    }

    [Fact]
    public void 饱食已满时喂食不扣食物_也不计暴食进度()
    {
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(2000.0).Build();
        world.OrderSailTo(BigMapGrid.DesertCity.Id);
        world.Advance(1);
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);

        var food = world.Town.Food;
        var granary = world.Voyage.GranaryFood;
        var fed = world.Voyage.FedTotalFood;

        world.FeedBeast(50.0).ShouldBe(RejectReason.InsufficientFood, "饱食满了喂不进去。");
        world.Town.Food.ShouldBe(food, 1e-9, "一口食物都不扣。");
        world.Voyage.GranaryFood.ShouldBe(granary, 1e-9, "兽粮也不扣。");
        world.Voyage.FedTotalFood.ShouldBe(fed, 1e-9, "不计暴食进度。");
    }

    // ---- 效果 ----

    [Fact]
    public void 荒漠态在沙漠带每小时衰减二_在草地仍是四()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        UnlockDesert(world);
        Switch(world, "desert").ShouldBeTrue();

        // ① 沙漠带内：4 × 0.5 = 2。
        PlaceInDesert(world);
        world.Voyage.SetSatiation(80.0);
        world.AdvanceTo(NextHourTick(world));
        world.Voyage.Satiation.ShouldBe(78.0, 1e-9, "荒漠态在沙漠带内每小时掉 2。");

        // ② 草地上：仍按基础 4。
        PlaceInGrass(world);
        world.Voyage.SetSatiation(80.0);
        world.AdvanceTo(NextHourTick(world));
        world.Voyage.Satiation.ShouldBe(76.0, 1e-9, "荒漠态在草地上每小时掉 4。");
    }

    [Fact]
    public void 暴食态饥饿衰减乘一点五()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        UnlockGlutton(world);
        Switch(world, "glutton").ShouldBeTrue();

        world.Voyage.SetSatiation(80.0);
        world.AdvanceTo(NextHourTick(world));
        world.Voyage.Satiation.ShouldBe(74.0, 1e-9, "4 × 1.5 = 每小时 6 点。");
    }

    [Fact]
    public void 形态不再改采矿产出_矿石与特殊材料都按位阶基线()
    {
        // ① 荒漠态：矿石产出与原始态同值（形态不再乘 1.25）。
        var desert = ArenaWorldBuilder.New(Seed).Build();
        desert.DockedAt(BigMapGrid.DesertCity.Id);
        desert.DugTo(0, ArenaTuning.Tier2GateLayer);
        desert.Soil.AssignMiners(0, ArenaTuning.Tier2GateLayer, 3).ShouldBe(3, "那一格派满矿工。");

        var oreBase = MeasureOreTwoHours(desert, 0, ArenaTuning.Tier2GateLayer);
        oreBase.ShouldBe(3 * (1.0 / ArenaTuning.OreGameHoursPerMiner) * 2, 1e-9, "原始态基线。");

        UnlockDesert(desert);
        Switch(desert, "desert").ShouldBeTrue();
        var oreDesert = MeasureOreTwoHours(desert, 0, ArenaTuning.Tier2GateLayer);
        oreDesert.ShouldBe(oreBase, 1e-9, "荒漠态不改矿石产出。");

        // ② 暴食态：高危区特殊材料同样不乘形态倍率。
        var glutton = ArenaWorldBuilder.New(Seed).Build();
        glutton.DockedAt(BigMapGrid.PoorCity.Id);
        glutton.DugTo(0, ArenaTuning.Tier2GateLayer);
        glutton.Soil.AssignMiners(0, ArenaTuning.Tier2GateLayer, 3).ShouldBe(3, "那一格派满矿工。");
        glutton.Soil.MarkCaveCleared(0, ArenaTuning.Tier2GateLayer).ShouldBeTrue("那一格转成高危生产区。");

        var materialBase = MeasureMaterialTwoHours(glutton, 0, ArenaTuning.Tier2GateLayer);
        materialBase.ShouldBe(3 * (1.0 / ArenaTuning.MaterialGameHoursPerMiner) * 2, 1e-9, "原始态基线。");

        UnlockGlutton(glutton);
        Switch(glutton, "glutton").ShouldBeTrue();
        var materialGlutton = MeasureMaterialTwoHours(glutton, 0, ArenaTuning.Tier2GateLayer);
        materialGlutton.ShouldBe(materialBase, 1e-9, "暴食态不改特殊材料产出。");
    }

    [Fact]
    public void 位阶采矿倍率仍在()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.DesertCity.Id);
        world.Tier2Ready();
        world.Soil.AssignMiners(0, ArenaTuning.Tier2GateLayer, 3).ShouldBe(3, "那一格派满矿工。");

        var oreBase = MeasureOreTwoHours(world, 0, ArenaTuning.Tier2GateLayer);

        // 进二阶：位阶采矿倍率 1.25 仍在。
        world.Town.ProduceSoulStone(200.0);
        world.Town.ProduceSpecialMaterial(50.0);
        world.Town.ProduceCoin(5000.0);
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours));
        world.Tier.ShouldBe(BeastTier.Tier2, "仪式走完位阶 +1。");

        var oreTiered = MeasureOreTwoHours(world, 0, ArenaTuning.Tier2GateLayer);
        oreTiered.ShouldBe(oreBase * ArenaTuning.StatsOf(BeastTier.Tier2).MiningFactor, 1e-9,
            "二阶采矿倍率 1.25。");
    }

    // ---- 事件 ----

    [Fact]
    public void FormSwitched只在形态真变时发一次()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var fired = new List<BeastForm>();
        world.FormSwitched += form => fired.Add(form);

        // ① 未解锁：不发。
        ArenaSetup.Inject(world, "SwitchForm", "desert", out _, out var why).ShouldBeFalse();
        why.ShouldStartWith($"rejected: {RejectReason.FormLocked}", Case.Sensitive);
        fired.ShouldBeEmpty("未解锁不发事件。");

        // ② 形态名不认识：不发。
        ArenaSetup.Inject(world, "SwitchForm", "lava", out _, out _).ShouldBeFalse();
        fired.ShouldBeEmpty("形态名不认识不发事件。");

        // ③ 切到当前形态（幂等）：不发。
        Switch(world, "original").ShouldBeTrue();
        fired.ShouldBeEmpty("幂等切换不发事件。");

        // ④ 形态真变：发一次，值是那个形态。
        UnlockDesert(world);
        Switch(world, "desert").ShouldBeTrue();
        fired.ShouldBe([BeastForm.Desert], "形态真变发一次，值是荒漠态。");

        // ⑤ 再切一次到当前形态：仍然不发。
        Switch(world, "desert").ShouldBeTrue();
        fired.Count.ShouldBe(1, "幂等切换不发事件。");

        // ⑥ 切回原始态：再发一次。
        Switch(world, "original").ShouldBeTrue();
        fired.ShouldBe([BeastForm.Desert, BeastForm.Original], "切回去再发一次。");
    }

    // ---- 快照 ----

    [Fact]
    public void 快照那一段带形态与两个进度_段首身份仍是BEAST_beast()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        PlaceInDesert(world);
        world.Advance(ArenaTime.StepsPerGameMinute * 600);
        world.Voyage.AddFedTotalFood(250.0);
        world.Advance(1);
        UnlockGlutton(world);
        Switch(world, "glutton").ShouldBeTrue();

        var segment = SegmentOf(world.DebugSnapshot(), "BEAST");
        segment.ShouldStartWith("BEAST beast ", Case.Sensitive, "段首两个 token 是身份。");
        FieldOf(segment, "form").ShouldBe("glutton", "form 那一栏给当前形态。");
        FieldOf(segment, "dwell").ShouldBe("600/240", "dwell 那一栏给沙漠累计分钟与阈值。");
        FieldOf(segment, "fed").ShouldBe("650/400", "fed 那一栏给累计喂食与阈值。");
        segment.ShouldContain("sat=", Case.Sensitive, "快照读出饱食度。");
        segment.ShouldContain("dig=", Case.Sensitive, "快照读出消化进度。");
        segment.ShouldContain("ovf=", Case.Sensitive, "快照读出溢出。");
        segment.ShouldContain("crystal=0/0/0", Case.Sensitive, "快照读出魔晶三处。");

        // 重放快照带同一段。
        world.ReplaySnapshot().ShouldContain(segment, Case.Sensitive, "重放快照带同一段。");
    }

    // ---- 重放 ----

    [Fact]
    public void 录一轮含Dock与三次SwitchForm的Journal_重放三档全绿()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var rec = ArenaSetup.Recorder(world);
        rec.BeginSession();

        // 停到沙城：让兽站在沙漠带里攒荒漠计时。开局还没走流程，段仍是「未开始」——
        // 「已驶离贫城」由流程答，停在沙城不等于驶离过贫城。
        rec.Inject("Dock", BigMapGrid.DesertCity.Id).ShouldBeTrue("停靠那一发走成写路径。");
        world.AdvanceTo(world.Tick + ArenaTime.StepsPerGameDay);
        world.IsDocked.ShouldBeTrue("停到沙城上。");
        world.DepartedPoorCity.ShouldBeFalse("段还在「未开始」，没驶离过贫城。");

        // 累计沙漠四小时（240 个游戏分钟）解锁荒漠态。
        world.Advance(ArenaTime.StepsPerGameMinute * (int)ArenaTuning.DesertFormDwellGameMinutes);
        world.FormUnlocked(BeastForm.Desert).ShouldBeTrue("荒漠态解锁了。");
        rec.Mark("荒漠态解锁");

        rec.Inject("SwitchForm", "desert").ShouldBeTrue("切到荒漠态那一发走成写路径。");
        rec.Mark("切到荒漠态");
        rec.Inject("SwitchForm", "original").ShouldBeTrue("切回原始态那一发走成写路径。");
        rec.Inject("SwitchForm", "glutton").ShouldBeFalse("暴食态没解锁，那一发判否。");
        rec.Inject("AdvanceTier", string.Empty).ShouldBeFalse("门槛没到，那一发判否。");
        rec.Mark("收口");

        var script = rec.ToScript();
        script.Entries.Count.ShouldBe(5, "剧本里那五发都是动词写路径。");
        script.Entries.Select(e => e.Cmd).ShouldBe(
            ["Dock", "SwitchForm", "SwitchForm", "SwitchForm", "AdvanceTier"], Case.Sensitive,
            "动词次序照录制。");
        script.Entries.Where(e => e.Cmd == "SwitchForm").Select(e => e.Ok)
            .ShouldBe([true, true, false], "前两发切形态走成，第三发（暴食态未解锁）判否。");

        var afterSwitch = script.Checkpoints.First(c => c.Label == "切到荒漠态");
        FieldOf(SegmentOf(afterSwitch.Snapshot, "BEAST"), "form").ShouldBe("desert",
            "检查点那一段记着荒漠态。");

        foreach (var level in new[]
                 {
                     ReplayRunner.AssertLevel.Literal,
                     ReplayRunner.AssertLevel.Semantic,
                     ReplayRunner.AssertLevel.Invariants,
                 })
        {
            var (session, replayed) = Replay(script, level);
            session.Divergences.ShouldBeEmpty($"{level} 档要全绿：\n" + Report(session));
            session.Commands.ShouldAllBe(c => c.Equal, "每一发的 ok 都照录制复现。");
            session.Checkpoints.ShouldAllBe(c => c.Equal, "检查点快照逐字相等。");

            replayed.Form.ShouldBe(BeastForm.Original, "重放端最后停在原始态。");
            replayed.FormUnlocked(BeastForm.Desert).ShouldBeTrue("重放端照样解锁荒漠态。");
            replayed.FormUnlocked(BeastForm.Glutton).ShouldBeFalse("重放端没有解锁暴食态。");
        }
    }

    private static (LiveReplaySession Session, ArenaWorld World) Replay(
        ReplayRunner.ReplayScript script, ReplayRunner.AssertLevel level)
    {
        var world = ArenaWorldBuilder.New(script.Seed).WithGranaryFood(2000.0).Build();
        var session = new LiveReplaySession(script with { AssertLevel = level }, ArenaSetup.Hooks(world));
        session.RunAll();
        return (session, world);
    }

    private static string Report(LiveReplaySession session) => string.Join("\n", session.Divergences);
}
