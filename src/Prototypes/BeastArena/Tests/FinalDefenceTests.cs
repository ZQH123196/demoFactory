#nullable enable
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 最终防守与立即重试：场合标识、战前快照、失败后原样还原、重试次数与尝试序号。
///
/// <para><b>L3 比对用 <c>DebugSnapshot()</c> 逐字相等</b>：快照里包含 tick（随机推进位置）、
/// 资源、人员、建筑、搬运、敌人、战报与流程读数 —— 同一份战前状态配同一段输入，
/// 四处（首次开战 / 失败 / 还原后 / 胜利）都该逐字相等。</para>
/// </summary>
public class FinalDefenceTests
{
    private const int Seed = 7;

    /// <summary>把一局推到「富城段 + 双成长完成」，再停靠富城。</summary>
    private static void ReachFinalDefenceReady(ArenaWorld world)
    {
        world.Flow.Start();
        world.Flow.NoteDocked(BigMapGrid.PoorCity.Id);
        world.Flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        world.Flow.NoteDefenceSettled(won: true);
        world.Flow.NoteDocked(BigMapGrid.DesertCity.Id);
        world.Flow.NoteAnchorPulled(BigMapGrid.DesertCity.Id);
        world.Flow.NoteDefenceSettled(won: true);
        world.Flow.NoteDocked(BigMapGrid.RichCity.Id);

        world.Growth.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        world.Growth.CheckUnlocks();
        world.Flow.NoteGluttonUnlocked(world.Tick);
        world.Growth.BeginRitual(world.Tick, 1);
        world.Growth.SettleRitual(world.Tick + 1);
        world.Flow.NoteTier2(world.Tick + 1);
        world.SwitchForm(BeastForm.Glutton);

        world.Flow.DoubleGrowthComplete.ShouldBeTrue("二阶 + 暴食解锁 + 手动切过一次。");
        world.DockedDirect(BigMapGrid.RichCity);
    }

    private static ArenaWorld FinalDefenceWorld()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ReachFinalDefenceReady(world);
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out var why).ShouldBeTrue(why);
        return world;
    }

    /// <summary>把镇政府打空，让下一分钟触发最终防守失败。</summary>
    private static void BreachTownHall(ArenaWorld world)
    {
        world.DamageBuilding(ArenaWorld.TownHallId, ArenaTuning.RescueTownHallDurability + 1000.0);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "最终防守收场。");
        world.Flow.Stage.ShouldBe(RunStage.FinalDefence, "失败留在最终防守等重试。");
    }

    /// <summary>
    /// 一段会失败的输入：资源、建筑、人员、敌人、搬运与击杀都在这一分钟里变了，
    /// 用来证明重试把这些变化全部作废（不只是镇政府那一格耐久）。
    /// </summary>
    private static void ApplyFailureInput(ArenaWorld world)
    {
        world.Town.ProduceCoin(123.0);
        world.Town.TrySpendFood(5.0).ShouldBeTrue();
        world.Town.ProduceCrystal(1.0);
        world.DamageBuilding(ArenaWorld.GranaryId, 30.0);

        var worker = world.Roster.Find("w2")!;
        worker.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, worker, 1.0);
        worker.State.ShouldBe(UnitState.Dead, "居民在战斗中阵亡。");

        var enemy = world.Roster.Units.First(u => u.IsBeast && !u.LeftTown);
        enemy.Hp = -99.5;
        world.Battle.ApplyDamage(world.Tick, enemy, 1.0);
        enemy.State.ShouldBe(UnitState.Dead, "来敌在战斗中阵亡。");

        // 战斗中召唤一个帮工：重试要把这个人、实体序号与法力占用一起作废。
        world.SummonHelper().ShouldNotBeNull("法力格够，帮工召得出来。");

        var cargo = world.Items.Add(PointItemKind.Cargo, new NavPoint(3.0, 3.0), "ore");
        world.Haul.Enqueue(ArenaWorld.Order(
            HaulLevel.GroundCargoToWarehouse, cargo.Id, new NavPoint(4.0, 4.0),
            ArenaWorld.TownHallId, 1.0, world.Tick));

        world.DamageBuilding(ArenaWorld.TownHallId, ArenaTuning.RescueTownHallDurability + 1000.0);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "最终防守收场。");
        world.Flow.Stage.ShouldBe(RunStage.FinalDefence, "失败留在最终防守等重试。");
    }

    [Fact]
    public void 最终防守_战报场合可读_失败立即重试回到战前同一份快照()
    {
        var world = FinalDefenceWorld();

        world.Flow.Stage.ShouldBe(RunStage.FinalDefence);
        world.Battle.Occasion.ShouldBe(BattleOccasion.FinalDefence);
        world.Battle.EndTick.ShouldBe(
            world.Tick + ((long)(ArenaTime.StepsPerGameHour * ArenaTuning.DefenceGameHours)),
            "与普通防守共用时限。");
        world.Roster.Units.Where(u => u.IsBeast && !u.LeftTown).Count().ShouldBe(24, "富城 1 环：20 + 4 × 1。");
        world.FinalSnapshotTick.ShouldBe(world.Tick, "初始化完、首个逻辑步之前取快照。");

        var before = world.DebugSnapshot();
        var tickBefore = world.Tick;
        var roster = world.Roster;

        BreachTownHall(world);
        world.Battle.LastReport.ShouldNotBeNull();
        world.Battle.LastReport!.Occasion.ShouldBe(BattleOccasion.FinalDefence, "战报场合显示最终防守。");
        world.Flow.FinalRetryCount.ShouldBe(1, "失败累计一次重试。");
        world.Flow.FinalAttemptIndex.ShouldBe(1, "首次尝试序号 1。");
        world.CanRetryFinalDefence.ShouldBeTrue();

        ArenaSetup.Inject(world, "RetryFinalDefence", string.Empty, out _, out var retryWhy)
            .ShouldBeTrue($"立即重试发得出去：{retryWhy}");

        world.Tick.ShouldBe(tickBefore, "还原把步号也拨回战前（随机推进一起作废）。");
        world.Roster.ShouldBeSameAs(roster, "还原不重建世界。");
        world.Flow.Stage.ShouldBe(RunStage.FinalDefence);
        world.Battle.Occasion.ShouldBe(BattleOccasion.FinalDefence, "直接重开同一场。");
        world.FinalRetryCount.ShouldBe(1, "重试次数不随世界还原清零。");
        world.FinalAttemptIndex.ShouldBe(2, "每次重试有独立尝试序号。");
        world.DebugSnapshot().ShouldBe(before, "还原后的世界与首次开战前逐字一致（L3）。");
    }

    /// <summary>
    /// 战中被打破、按「归零即出册」离开建造册的那一座（粮仓），重试要连人带格一起请回来。
    ///
    /// <para>只回补血量的那一版还原对<b>不在册</b>的 id 是空动作 —— 敌人啃穿粮仓的那一局重试完，
    /// 粮仓就永久没了（ticket 08 的固定输入主线在富城那一场真啃穿了它，判红才露出来）。
    /// 归位还要对得上<b>册序</b>：读数按登记顺序逐栋印，补到末尾那一格会让「恢复后」与「首次开战」差一整截。</para>
    /// </summary>
    [Fact]
    public void 最终防守里被打破出册的建筑_重试按原来那一格请回建造册()
    {
        var world = FinalDefenceWorld();
        var before = world.DebugSnapshot();

        // 打空粮仓的耐久：它不像镇政府那样留在册上，当场出册（D16「摧毁是去向，不是状态」）。
        world.DamageBuilding(ArenaWorld.GranaryId, 10_000.0).ShouldBeTrue("粮仓耐久打得空。");
        world.Construction.Find(ArenaWorld.GranaryId).ShouldBeNull("归零即出册：粮仓已经不在建造册上。");

        // 同一分钟里把镇政府也打空 → 这一场失败。
        world.DamageBuilding(ArenaWorld.TownHallId, ArenaTuning.RescueTownHallDurability + 1000.0);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "最终防守收场。");

        ArenaSetup.Inject(world, "RetryFinalDefence", string.Empty, out _, out var why)
            .ShouldBeTrue($"立即重试发得出去：{why}");

        world.Construction.Find(ArenaWorld.GranaryId).ShouldNotBeNull("重试把战中被打破的那一座请回建造册。");
        world.DebugSnapshot().ShouldBe(before, "恢复后（L3）：那一行回到它原来那一格，栋序一起复原。");
    }

    [Fact]
    public void 最终防守开始与立即重试_事件各发一次_被拒不发()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ReachFinalDefenceReady(world);

        var started = new List<BattleOccasion>();
        var retries = new List<int>();
        world.DefenceStarted += occasion => started.Add(occasion);
        world.FinalRetryStarted += attempt => retries.Add(attempt);

        // ① 还没打过：没有战前快照，重试被拒，不发。
        ArenaSetup.Inject(world, "RetryFinalDefence", string.Empty, out _, out _).ShouldBeFalse();
        started.ShouldBeEmpty("防守还没开起来不发事件。");
        retries.ShouldBeEmpty("重试没走成不发事件。");

        // ② 最终防守开起来：DefenceStarted 发一次，场合 = FinalDefence。
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out var why).ShouldBeTrue(why);
        started.ShouldBe([BattleOccasion.FinalDefence], "最终防守开始发一次，载荷是场合。");

        // ③ 打输本身不再发这两条；立即重试还原成功：发一次，带尝试序号（第二次尝试）。
        BreachTownHall(world);
        started.Count.ShouldBe(1, "收场不重发 DefenceStarted。");
        retries.ShouldBeEmpty("失败本身不发重试事件。");

        ArenaSetup.Inject(world, "RetryFinalDefence", string.Empty, out _, out var retryWhy)
            .ShouldBeTrue(retryWhy);
        retries.ShouldBe([2], "还原成功发一次，序号 2（首次 = 1）。");
    }

    [Fact]
    public void 终局结算列出规格字段_三段与四里程碑_继续经营后不改写()
    {
        var world = FinalDefenceWorld();
        world.Battle.ForceEnd(world.Tick);

        world.Flow.Stage.ShouldBe(RunStage.Settlement);
        var settlement = world.Settlement;
        settlement.ShouldNotBeNull("最终防守赢了，结算已经在世界上（事件弹出来之前）。");

        settlement!.Tier.ShouldBe(BeastTier.Tier2, "位阶。");
        settlement.GluttonUnlocked.ShouldBeTrue("暴食态状态。");
        settlement.Form.ShouldBe(BeastForm.Glutton, "当前形态。");
        settlement.TownHallDurability.ShouldBe(200.0, 1e-9, "镇政府耐久。");
        settlement.AliveResidents.ShouldBe(12, "居民存活数。");
        settlement.IntactBuildings.ShouldBe(6, "建筑完好数（耐久满）。");
        settlement.Coin.ShouldBe(world.Town.Coin, 1e-9, "当前金币。");
        settlement.Debt.ShouldBe(world.Town.TotalDebt, 1e-9, "负债。");
        settlement.ProducedCoin.ShouldBe(world.Town.ProducedCoin, 1e-9, "累计经营收入。");
        settlement.SpentCoin.ShouldBe(world.Town.SpentCoin, 1e-9, "累计经营支出。");
        settlement.TicketIncome.ShouldBe(world.Town.TicketIncome, 1e-9, "门票收入。");
        settlement.CrystalProduced.ShouldBe(world.Town.ProducedCrystal, 1e-9, "魔晶产出。");
        settlement.CrystalUsed.ShouldBe(world.Town.CrystalUsed, "魔晶使用。");
        settlement.CrystalSold.ShouldBe(world.Town.CrystalSold, "魔晶出售。");
        settlement.DefenceWins.ShouldBeGreaterThanOrEqualTo(3, "贫城、沙城、最终各一胜。");
        settlement.DefenceLosses.ShouldBe(0);
        settlement.FinalRetryCount.ShouldBe(0);
        settlement.VictoryTick.ShouldBe(world.Tick, "胜利那一刻的步号。");

        settlement.Segments.Count.ShouldBe(3, "三个体验段各一。");
        settlement.Segments[0].Key.ShouldBe("poor");
        settlement.Segments[0].Label.ShouldBe("贫城段");
        settlement.Segments[1].Key.ShouldBe("sand");
        settlement.Segments[1].Label.ShouldBe("沙城段");
        settlement.Segments[2].Key.ShouldBe("rich");
        settlement.Segments[2].Label.ShouldBe("富城段");
        foreach (var segment in settlement.Segments)
        {
            segment.DurationTicks.ShouldBeGreaterThanOrEqualTo(0, $"{segment.Label} 的耗时。");
            segment.Income.ShouldBeGreaterThanOrEqualTo(0.0, $"{segment.Label} 的经营收入。");
            segment.Expense.ShouldBeGreaterThanOrEqualTo(0.0, $"{segment.Label} 的经营支出。");
        }

        settlement.Segments[0].Defences.Count.ShouldBeGreaterThanOrEqualTo(1, "贫城段的防守结果。");
        settlement.Segments[1].Defences.Count.ShouldBeGreaterThanOrEqualTo(1, "沙城段的防守结果。");
        settlement.Segments[2].Defences.Count.ShouldBeGreaterThanOrEqualTo(1, "富城段的防守结果。");
        settlement.Segments[2].DefenceWins.ShouldBe(1, "富城段最终防守一胜。");

        settlement.Milestones.Count.ShouldBe(4, "四项里程碑。");
        settlement.GluttonUnlockedMilestone.Tick.ShouldBe(0);
        settlement.FirstFormSwitchMilestone.Tick.ShouldBe(0);
        settlement.Tier2Milestone.Tick.ShouldBe(1);
        settlement.FinalVictoryMilestone.Tick.ShouldBe(settlement.VictoryTick);
        settlement.FinalVictoryMilestone.ElapsedTicks.ShouldBe(settlement.VictoryTick);

        // 继续经营：结算不随之后的变化改写；目标改成自由经营。
        world.ContinueBusiness().ShouldBeTrue();
        world.Flow.Stage.ShouldBe(RunStage.FreePlay);
        world.Flow.Goal.ShouldContain("自由经营", Case.Sensitive);

        var coinAtVictory = settlement.Coin;
        world.Town.ProduceCoin(999.0);
        world.Advance(50);
        world.Settlement.ShouldBeSameAs(settlement, "回看的那一份就是胜利时留下的那一份。");
        world.Settlement!.Coin.ShouldBe(coinAtVictory, 1e-9, "继续经营不改写结算。");
        (System.Math.Abs(world.Town.Coin - coinAtVictory) > 1e-9).ShouldBeTrue(
            "世界本身已经赚过了，结算仍是胜利那一刻。");

        // 继续经营后再次在富城拔锚：只触发普通防守，结算不再生成 / 覆盖。
        world.DockedDirect(BigMapGrid.RichCity);
        ArenaSetup.Inject(world, "PullAnchor", string.Empty, out _, out var pullWhy)
            .ShouldBeTrue($"自由经营拔得出去：{pullWhy}");
        world.Flow.Stage.ShouldBe(RunStage.NormalDefence, "不再进最终防守。");
        world.Battle.Occasion.ShouldBe(BattleOccasion.Defence, "普通防守的场合标识。");
        world.Settlement.ShouldBeSameAs(settlement, "终局结算不被第二次拔锚覆盖。");
    }

    [Fact]
    public void 同一份战前状态配同一输入段_失败与胜利都逐字一致()
    {
        var world = FinalDefenceWorld();
        var before = world.DebugSnapshot();

        // 第一次：同一输入段（资源 / 建筑 / 人员 / 敌人 / 搬运都变）+ 走一分钟 → 失败。
        ApplyFailureInput(world);
        var failure = world.DebugSnapshot();

        // 立即重试 → 还原后逐字回到战前。
        ArenaSetup.Inject(world, "RetryFinalDefence", string.Empty).ShouldBeTrue();
        world.DebugSnapshot().ShouldBe(before, "恢复后（L3）。");

        // 同一失败输入段重放，仍得到同一失败（L3）。
        ApplyFailureInput(world);
        world.DebugSnapshot().ShouldBe(failure, "同一失败输入段重放（L3）。");
        world.Flow.FinalRetryCount.ShouldBe(2, "第二次失败再累计一次。");

        // 换一个输入段（不再打镇政府，走满时限）→ 胜利；与另一局同输入段的胜利逐字一致。
        ArenaSetup.Inject(world, "RetryFinalDefence", string.Empty).ShouldBeTrue();
        world.DebugSnapshot().ShouldBe(before, "第二次恢复后（L3）。");
        world.AdvanceTo(world.Battle.EndTick + ArenaTime.StepsPerGameMinute);
        world.Flow.Stage.ShouldBe(RunStage.Settlement, "最终防守赢了进结算。");
        world.Battle.LastReport!.Occasion.ShouldBe(BattleOccasion.FinalDefence);
        var victory = world.DebugSnapshot();

        // 对照局：不失败，直接按同一胜利输入段跑一遍。
        var control = FinalDefenceWorld();
        control.DebugSnapshot().ShouldBe(before, "首次开战（L3）：同一份战前状态逐字一致。");
        control.AdvanceTo(control.Battle.EndTick + ArenaTime.StepsPerGameMinute);
        control.Flow.Stage.ShouldBe(RunStage.Settlement);
        control.DebugSnapshot().ShouldBe(victory, "胜利（L3）：重试过的那一局与对照局逐字一致。");

        // 重试次数不随还原清零，胜负记在流程账上。
        world.Flow.FinalRetryCount.ShouldBe(2);
        control.Flow.FinalRetryCount.ShouldBe(0);
        world.Flow.DefenceLosses.ShouldBe(2, "两次失败各记一场败。");
        world.Flow.DefenceWins.ShouldBeGreaterThanOrEqualTo(3, "贫城、沙城、最终各一胜。");
    }
}
