#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 终局结算那一份：胜负定格那一刻的世界读数 + 三个体验段 + 四个里程碑。
//
// **它是一份快照，不是每帧现算**：最终防守胜利那一格生成一次，之后继续经营怎么改都不再重算。
// 现实耗时（RunTelemetry）住表现层，逻辑层不读墙钟 —— 所以这里只存游戏步号，
// 表现层把现实秒数补进自己的视图模型；结算的核心字段与 L3 断言都不含现实耗时。

/// <summary>一段防守结果（结算里按贫城 / 沙城 / 富城三段列）。</summary>
/// <param name="CityId">从哪座城拔的锚。</param>
/// <param name="Won">守住了没有。</param>
/// <param name="Tick">收场步号。</param>
public sealed record SettlementDefence(string CityId, bool Won, long Tick);

/// <summary>一个体验段的读数：段内耗时（游戏步）、收支与防守结果。</summary>
/// <param name="Key">稳定 token（<c>poor</c> / <c>sand</c> / <c>rich</c>）。</param>
/// <param name="Label">中文名（贫城段 / 沙城段 / 富城段）。</param>
/// <param name="StartTick">段开始步号。</param>
/// <param name="EndTick">段结束步号。</param>
/// <param name="Income">段内经营收入（金币）。</param>
/// <param name="Expense">段内经营支出（金币）。</param>
/// <param name="Defences">段内防守记录（按收场次序）。</param>
public sealed record SettlementSegment(
    string Key,
    string Label,
    long StartTick,
    long EndTick,
    double Income,
    double Expense,
    IReadOnlyList<SettlementDefence> Defences)
{
    /// <summary>段内游戏步数。</summary>
    public long DurationTicks => EndTick - StartTick;

    /// <summary>段内防守胜场。</summary>
    public int DefenceWins
    {
        get
        {
            var n = 0;
            foreach (var d in Defences) if (d.Won) n++;
            return n;
        }
    }

    /// <summary>段内防守败场。</summary>
    public int DefenceLosses => Defences.Count - DefenceWins;
}

/// <summary>一个里程碑：达成步号与从开局起算的累计步数。</summary>
/// <param name="Kind">里程碑。</param>
/// <param name="Tick">达成步号（没达成为 -1）。</param>
/// <param name="ElapsedTicks">从开局起算的累计步数（没达成为 -1）。</param>
public sealed record SettlementMilestone(RunMilestone Kind, long Tick, long ElapsedTicks);

/// <summary>
/// 终局结算（规格「终局结算」）。字段全是确定性读数；现实耗时由表现层另填。
/// </summary>
/// <param name="VictoryTick">最终防守胜利那一刻的步号。</param>
/// <param name="Tier">位阶。</param>
/// <param name="GluttonUnlocked">暴食态解锁没有。</param>
/// <param name="Form">当前形态。</param>
/// <param name="TownHallDurability">镇政府耐久。</param>
/// <param name="AliveResidents">居民存活数（在册、未离镇、未死）。</param>
/// <param name="IntactBuildings">建筑完好数（耐久满的建筑面积）。</param>
/// <param name="Coin">当前金币。</param>
/// <param name="Debt">负债。</param>
/// <param name="ProducedCoin">累计经营收入。</param>
/// <param name="SpentCoin">累计经营支出。</param>
/// <param name="TicketIncome">门票收入。</param>
/// <param name="CrystalProduced">魔晶产出。</param>
/// <param name="CrystalUsed">魔晶使用。</param>
/// <param name="CrystalSold">魔晶出售。</param>
/// <param name="DefenceWins">防守胜场。</param>
/// <param name="DefenceLosses">防守败场。</param>
/// <param name="FinalRetryCount">最终重试次数。</param>
/// <param name="Segments">三个体验段。</param>
/// <param name="Milestones">四项里程碑。</param>
public sealed record RunSettlement(
    long VictoryTick,
    BeastTier Tier,
    bool GluttonUnlocked,
    BeastForm Form,
    double TownHallDurability,
    int AliveResidents,
    int IntactBuildings,
    double Coin,
    double Debt,
    double ProducedCoin,
    double SpentCoin,
    double TicketIncome,
    double CrystalProduced,
    int CrystalUsed,
    int CrystalSold,
    int DefenceWins,
    int DefenceLosses,
    int FinalRetryCount,
    IReadOnlyList<SettlementSegment> Segments,
    IReadOnlyList<SettlementMilestone> Milestones)
{
    /// <summary>第 1 项里程碑：暴食态解锁。</summary>
    public SettlementMilestone GluttonUnlockedMilestone => MilestoneOf(RunMilestone.GluttonUnlocked);

    /// <summary>第 2 项里程碑：首次手动切换。</summary>
    public SettlementMilestone FirstFormSwitchMilestone => MilestoneOf(RunMilestone.FirstFormSwitch);

    /// <summary>第 3 项里程碑：二阶完成。</summary>
    public SettlementMilestone Tier2Milestone => MilestoneOf(RunMilestone.Tier2);

    /// <summary>第 4 项里程碑：最终胜利。</summary>
    public SettlementMilestone FinalVictoryMilestone => MilestoneOf(RunMilestone.FinalVictory);

    /// <summary>按里程碑取那一条（四项总在表里；取不到时回 -1/-1）。</summary>
    public SettlementMilestone MilestoneOf(RunMilestone kind)
    {
        foreach (var m in Milestones)
        {
            if (m.Kind == kind) return m;
        }

        return new SettlementMilestone(kind, -1, -1);
    }
}

public sealed partial class ArenaWorld
{
    private readonly Dictionary<RunStage, SegmentMark> _segmentMarks = [];

    private readonly record struct SegmentMark(long Tick, double ProducedCoin, double SpentCoin);

    /// <summary>终局结算（最终防守胜利时生成一次；继续经营不再改写）。</summary>
    public RunSettlement? Settlement { get; private set; }

    /// <summary>在流程进到某一座的段时记下起点读数（贫城 / 沙城 / 富城各一次）。</summary>
    private void MarkSegmentIfNeeded()
    {
        var stage = Flow.Stage;
        if (stage is not (RunStage.Poor or RunStage.Sand or RunStage.Rich)) return;
        if (_segmentMarks.ContainsKey(stage)) return;

        _segmentMarks[stage] = new SegmentMark(Tick, Town.ProducedCoin, Town.SpentCoin);
    }

    /// <summary>
    /// 生成终局结算（<b>只生成一次</b>）。订阅在 <c>Flow.SettlementOpened</c> 上，
    /// 排在表现层之前 —— 结算事件弹出来的时候，这一份已经在世界上了。
    /// </summary>
    private void CaptureSettlement()
    {
        if (Settlement is not null) return;
        if (Flow.Stage != RunStage.Settlement || Flow.FinalVictoryTick < 0) return;

        var victory = Flow.FinalVictoryTick;
        var poorStart = MarkOf(RunStage.Poor);
        var sandStart = MarkOf(RunStage.Sand);
        var richStart = MarkOf(RunStage.Rich);

        var poor = BuildSegment("poor", "贫城段", poorStart, sandStart, victory);
        var sand = BuildSegment("sand", "沙城段", sandStart, richStart, victory);
        var rich = BuildSegment("rich", "富城段", richStart, null, victory);

        var milestones = new List<SettlementMilestone>(4);
        milestones.Add(Milestone(RunMilestone.GluttonUnlocked, Flow.GluttonUnlockedTick));
        milestones.Add(Milestone(RunMilestone.FirstFormSwitch, Flow.FirstSwitchTick));
        milestones.Add(Milestone(RunMilestone.Tier2, Flow.Tier2Tick));
        milestones.Add(Milestone(RunMilestone.FinalVictory, Flow.FinalVictoryTick));

        Settlement = new RunSettlement(
            victory,
            Growth.Tier,
            Growth.GluttonFormUnlocked,
            Growth.Form,
            Construction.Find(TownHallId)?.Health ?? 0.0,
            CountAliveResidents(),
            CountIntactBuildings(),
            Town.Coin,
            Town.TotalDebt,
            Town.ProducedCoin,
            Town.SpentCoin,
            Town.TicketIncome,
            Town.ProducedCrystal,
            Town.CrystalUsed,
            Town.CrystalSold,
            Flow.DefenceWins,
            Flow.DefenceLosses,
            Flow.FinalRetryCount,
            [poor, sand, rich],
            milestones);
    }

    private int CountAliveResidents()
    {
        var alive = 0;
        foreach (var u in Roster.Units)
        {
            if (!u.IsTownsperson || u.LeftTown) continue;
            if (u.State == UnitState.Dead) continue;
            alive++;
        }

        return alive;
    }

    private int CountIntactBuildings()
    {
        var intact = 0;
        foreach (var id in Construction.Ids)
        {
            var b = Construction.Find(id);
            if (b is null) continue;
            if (b.Health >= b.MaxHealth) intact++;
        }

        return intact;
    }

    private SegmentMark MarkOf(RunStage stage)
        => _segmentMarks.TryGetValue(stage, out var mark) ? mark : new SegmentMark(0, 0.0, 0.0);

    /// <summary>
    /// 一段的读数：段内收入 / 支出是<b>段末累计账 − 段初累计账</b>。
    /// 缺段（玩家跳过某城）时按 0 起算，但三段的位子都在。
    /// </summary>
    private SettlementSegment BuildSegment(
        string key,
        string label,
        SegmentMark start,
        SegmentMark? nextStart,
        long victoryTick)
    {
        // 富城段的段末是胜利那一刻的总账；沙城 / 贫城用下一段起点。
        var endTick = nextStart?.Tick ?? victoryTick;
        var endProduced = nextStart?.ProducedCoin ?? Town.ProducedCoin;
        var endSpent = nextStart?.SpentCoin ?? Town.SpentCoin;

        var defeats = new List<SettlementDefence>();
        var homeStage = key switch
        {
            "poor" => RunStage.Poor,
            "sand" => RunStage.Sand,
            _ => RunStage.Rich,
        };

        foreach (var record in Flow.DefenceRecords)
        {
            if (record.HomeStage != homeStage) continue;
            defeats.Add(new SettlementDefence(record.CityId, record.Won, record.Tick));
        }

        return new SettlementSegment(
            key, label, start.Tick, endTick,
            endProduced - start.ProducedCoin,
            endSpent - start.SpentCoin,
            defeats);
    }

    private static SettlementMilestone Milestone(RunMilestone kind, long tick)
        => new(kind, tick, tick < 0 ? -1 : tick);
}
