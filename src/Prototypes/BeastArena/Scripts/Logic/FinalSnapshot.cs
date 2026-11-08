#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 最终防守的那一份战前快照：只做「取一份」与「按份还原」两件事。
//
// **为什么不是 DebugSnapshot**：文本快照没有可写回的通道，也没有随机状态。
// 这一份存的是**世界字段本身**（资源、人员、建筑、搬运、敌人、流程与 tick），
// 还原时把这些字段写回同一个世界实例 —— 不重建世界、不重跑装配。
//
// **随机状态 = 世界种子的推进位置**：本原型的随机全是
// `PeopleRoster.DailyDraw(seed, index, day)` / `Visitors` 同款的纯函数，
// 没有可变的随机流；所以把 tick 一起还原，随机序列就自动对齐。
//
// **哪几格不还原**：一局流程里的重试次数、尝试序号与防守记录是「局」的账，
// 不是世界状态（规格「最终重试」）；它们摆在 RunFlow 上，还原时刻意不动。

/// <summary>活物一格的全量读数（含寻路层的位置与命令）。</summary>
internal sealed record UnitSnapshot(
    string Id,
    int RosterIndex,
    UnitRole Role,
    double Melee,
    double Ranged,
    double Riding,
    double Hp,
    UnitState State,
    double Loyalty,
    double Mood,
    string Post,
    double PersonalReputation,
    bool NonCombatant,
    bool LeftTown,
    bool Held,
    double DeckX,
    double DeckZ,
    bool Moving,
    long RiseTick,
    double Contribution,
    NavPoint NavPosition,
    NavCommand? NavCommand,
    bool CountedInSeparation);

internal sealed record RosterSnapshot(
    IReadOnlyList<UnitSnapshot> Units,
    IReadOnlyDictionary<string, double> UnpaidWages,
    int NextRosterIndex,
    int SpawnCursor,
    int EntityCount,
    int EntitySlots,
    long EntityIssued);

internal sealed record ItemSnapshot(
    string Id,
    PointItemKind Kind,
    string Payload,
    double DeckX,
    double DeckZ,
    bool Removed,
    bool Held);

internal sealed record ItemsSnapshot(
    IReadOnlyList<ItemSnapshot> Items,
    IReadOnlyDictionary<string, int> Counters);

internal sealed record CarrySnapshot(HaulOrder Order, HaulLeg Leg);

internal sealed record HaulingSnapshot(
    IReadOnlyList<HaulOrder> Orders,
    IReadOnlyList<HaulLanding> Landings,
    IReadOnlyDictionary<string, CarrySnapshot> Carrying);

internal sealed record CreditorSnapshot(string FactionId, double Accrued, double Repaid);

internal sealed record TownSnapshot(
    double Coin,
    double ProducedCoin,
    double SpentCoin,
    double Food,
    double ProducedFood,
    double SpentFood,
    double SoulStone,
    double ProducedSoulStone,
    double SpentSoulStone,
    double Ore,
    double ProducedOre,
    double SpentOre,
    double SpecialMaterial,
    double ProducedSpecialMaterial,
    double SpentSpecialMaterial,
    double SparePart,
    double ProducedSparePart,
    double SpentSparePart,
    double Crystal,
    double ProducedCrystal,
    double SpentCrystal,
    int CrystalUsed,
    int CrystalSold,
    double TicketIncome,
    int AvailableMana,
    int StasisMana,
    int HelperMana,
    int InstantSpentMana,
    int CrystalManaRestored,
    int RegainedMana,
    DebtPolicy Policy,
    string PriorityFactionId,
    double HotelMultiplier,
    double RestaurantMultiplier,
    double TicketMultiplier,
    IReadOnlyList<CreditorSnapshot> Creditors);

internal sealed record VisitorUnitSnapshot(
    string UnitId,
    double Budget,
    int StayGameMinutes,
    long EnteredTick,
    VisitorActivity Activity,
    string TargetBuildingId,
    long BusyUntilTick,
    long LeaveTick);

internal sealed record VisitorsSnapshot(
    int PoolAvailable,
    int Cap,
    int NextVisitorNumber,
    long LastMinute,
    double AdmitCarry,
    double TownReputation,
    double PriceFactor,
    string ArenaTarget,
    double TownHotelMultiplier,
    double TownRestaurantMultiplier,
    double TownTicketMultiplier,
    IReadOnlyList<VisitorUnitSnapshot> InTown);

internal sealed record EcoNodeSnapshot(string Id, long AvailableAtTick, bool Consumed);

internal sealed record EcologySnapshot(
    double Satiation,
    double DigestionProgress,
    int CrystalsProduced,
    double HungerDecayFactor,
    long ForageCooldownUntilTick,
    string ForageGaveUpReason,
    long LastDecayTick,
    IReadOnlyList<EcoNodeSnapshot> Nodes);

internal sealed record SoilCellSnapshot(
    int Sector,
    int Layer,
    bool Dug,
    bool Cleared,
    bool Embedded,
    double RegrowthProgress,
    int Miners,
    int DigWorkers,
    long DigDoneTick,
    bool Digging,
    double OreAccumulated,
    double MaterialAccumulated,
    int CaveMonsters);

internal sealed record SoilSnapshot(
    IReadOnlyList<SoilCellSnapshot> Cells,
    IReadOnlyList<SoilCollapse> Collapses);

internal sealed record DeckSnapshot(
    int SpareParts,
    IReadOnlyList<string> Lifted,
    IReadOnlyList<(string BuildingId, long DoneTick)> Repairs,
    IReadOnlyDictionary<string, long> RepairDoneTick,
    int[] ManaOccupied,
    long NextBuildingNumber);

/// <summary>
/// 建造册的战前那一份。<b>连蓝图与锚点一起记</b>（不只是血量）——
/// 防守里被敌人打空耐久的那一座会<b>出册</b>（<c>Damage</c> 的归零去向是「摧毁不是状态」），
/// 只回补血量的还原找不回它（<c>SetHealth</c> 对不在册的 id 是空动作），
/// 而规格「最终重试」要的是「战中的变化一起作废」。
/// </summary>
/// <param name="Rows">在册的每一行，按册序（= 读数的栋序）：建筑 id | 蓝图 id | 锚点列 | 锚点行 | 当前耐久。</param>
internal sealed record ConstructionSnapshot(IReadOnlyList<BuildingRecord> Rows);

/// <summary>建造册的一行（还原到「连出册的那一座也能原样请回」所需的最小一组读数）。</summary>
internal sealed record BuildingRecord(
    string Id, string BlueprintId, int Col, int Row, double Health);

internal sealed record ElevatorSnapshot(
    IReadOnlyList<string> ShaftQueue,
    IReadOnlyList<ElevatorSlot> LastSettlement,
    IReadOnlyDictionary<int, double> OreDrawn,
    IReadOnlyDictionary<int, double> MaterialDrawn,
    long Settlements);

internal sealed record GrowthSnapshot(
    BeastTier Tier,
    BeastForm Form,
    long RitualUntilTick,
    long DwellGameMinutes,
    double FedTotalFood,
    bool DesertFormUnlocked,
    bool GluttonFormUnlocked,
    bool Tier2Eligible);

internal sealed record VoyageSnapshot(
    VoyagePhase Phase,
    string? TargetCityId,
    NavPoint? ManualTarget,
    NavPoint? RouteWaypoint,
    double SoulStones,
    double GranaryFood,
    double TierSpeedFactor,
    long LastDrainMinute,
    long EatUntilTick,
    string ForageNodeId,
    string ManualNodeId,
    long ManualEatUntilTick,
    string ManualResumeCityId,
    NavPoint? ManualResumePoint,
    double AppliedSpeedFactor,
    NavPoint? OutboundTarget,
    NavPoint BeastPosition,
    NavCommand? BeastCommand);

internal sealed record SiegeSnapshot(
    bool Breached,
    long BreachTick,
    double PendingDebt,
    long StandstillUntilTick,
    string RescueFactionId,
    IReadOnlyList<string> WarLossBuildings,
    IReadOnlyList<string> TakenPeople);

internal sealed record FlowSnapshot(
    RunStage Stage,
    long GluttonUnlockedTick,
    long FirstSwitchTick,
    long Tier2Tick,
    long FinalVictoryTick,
    string DefenceCityId);

internal sealed record BattleSnapshot(
    BattleOccasion Occasion,
    long EndTick,
    IReadOnlyList<string> SideAIds,
    IReadOnlyList<string> SideBIds,
    IReadOnlyDictionary<string, string> EnemyTargets,
    IReadOnlyDictionary<string, int> EnemyLoot,
    int EnemyNumber,
    int MonsterNumber,
    int CaveSector,
    int CaveLayer,
    bool CaveCleared,
    bool FinalDefenceFailed,
    int OnFieldCount,
    int DownCount,
    int DeadCount,
    int LootedCount,
    BattleReport? LastReport,
    IReadOnlyList<string> DeadAtOpen,
    IReadOnlyList<string> WoundedAtOpen);

internal sealed record WorldSnapshot(
    long Tick,
    string DockedCityId,
    bool PulledAnchor,
    int LastPulledRings,
    int RecruitedThisDock,
    bool Wanted,
    long AirshipWindowUntilTick,
    int AirshipVisitsLeft,
    double PendingOre,
    double PendingMaterial,
    double WagesDue,
    int ArrearsDays,
    double WagesPaidThisDay,
    long LastHourTick,
    long LastDayTick,
    long LastElevatorTick,
    long LastBreachCheckTick,
    int AppliedCollapseCount,
    bool FinalDefenceCollapsed,
    IReadOnlyList<string> CapturedHelpers,
    IReadOnlyDictionary<string, (long DoneTick, string PersonId, string CorpseId)> Reviving,
    IReadOnlyDictionary<string, int> SoilCrew,
    IReadOnlyList<StarSaleRecord> StarSales,
    int StarSaleIssued,
    IReadOnlyList<(string CorpseId, string Name, long EnteredTick)> ChurchCorpses,
    TownSnapshot Town,
    RosterSnapshot Roster,
    ItemsSnapshot Items,
    HaulingSnapshot Hauling,
    VisitorsSnapshot Visitors,
    EcologySnapshot Ecology,
    SoilSnapshot Soil,
    DeckSnapshot Deck,
    ConstructionSnapshot Construction,
    ElevatorSnapshot Elevator,
    GrowthSnapshot Growth,
    VoyageSnapshot Voyage,
    SiegeSnapshot Siege,
    FlowSnapshot Flow,
    BattleSnapshot Battle);

/// <summary>
/// 最终防守的战前快照。**只做「取一份」与「按份还原」两件事**：
/// <see cref="Take"/> 把当前世界读成一份不可变的状态；
/// <see cref="Restore"/> 把同一份状态写回同一个世界实例（不重建世界、不重跑装配）。
/// </summary>
public sealed class FinalSnapshot
{
    private readonly WorldSnapshot _state;

    private FinalSnapshot(WorldSnapshot state) => _state = state;

    /// <summary>快照里存的步号（战前那一格）。</summary>
    public long Tick => _state.Tick;

    /// <summary>取一份：在最终防守初始化完、尚未推进首个逻辑步时调一次。</summary>
    public static FinalSnapshot Take(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new FinalSnapshot(world.CaptureFinalSnapshotState());
    }

    /// <summary>
    /// 按份还原：把快照里的世界字段写回 <paramref name="world"/>（同一个实例）。
    /// <b>重试次数、尝试序号与防守记录不在这一份里</b>，所以不会被还原抹掉。
    /// </summary>
    public void Restore(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.RestoreFinalSnapshotState(_state);
    }
}

// === 取一份 / 按份还原（partial 都住这一处，子系统只多一个 partial 关键字） ===

public sealed partial class ArenaWorld
{
    internal WorldSnapshot CaptureFinalSnapshotState()
    {
        var units = new List<UnitSnapshot>(Roster.Units.Count);
        foreach (var u in Roster.Units)
        {
            var hasAgent = Roster.DeckStepper.HasAgent(u.Id);
            units.Add(new UnitSnapshot(
                u.Id, u.RosterIndex, u.Role, u.Melee, u.Ranged, u.Riding, u.Hp, u.State,
                u.Loyalty, u.Mood, u.Post, u.PersonalReputation, u.NonCombatant, u.LeftTown,
                u.Held, u.DeckX, u.DeckZ, u.Moving, u.RiseTick, u.Contribution,
                hasAgent ? Roster.DeckStepper.PositionOf(u.Id) : new NavPoint(u.DeckX, u.DeckZ),
                hasAgent ? Roster.DeckStepper.CommandOf(u.Id) : null,
                hasAgent && Roster.DeckStepper.IsCountedInSeparation(u.Id)));
        }

        var carrying = new Dictionary<string, CarrySnapshot>(StringComparer.Ordinal);
        foreach (var (id, inHand) in _carrying)
        {
            carrying[id] = new CarrySnapshot(inHand.Order, inHand.Leg);
        }

        return new WorldSnapshot(
            Tick,
            DockedCityId,
            PulledAnchor,
            LastPulledRings,
            RecruitedThisDock,
            Wanted,
            AirshipWindowUntilTick,
            AirshipVisitsLeft,
            PendingOre,
            PendingMaterial,
            WagesDue,
            ArrearsDays,
            WagesPaidThisDay,
            _lastHourTick,
            _lastDayTick,
            _lastElevatorTick,
            _lastBreachCheckTick,
            _appliedCollapseCount,
            _finalDefenceCollapsed,
            _capturedHelpers.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            new Dictionary<string, (long, string, string)>(_reviving, StringComparer.Ordinal),
            new Dictionary<string, int>(_soilCrew, StringComparer.Ordinal),
            _starSales.ToList(),
            StarSaleIssued,
            _churchCorpses.ToList(),
            Town.CaptureFinalState(),
            Roster.CaptureFinalState(units),
            Items.CaptureFinalState(),
            Haul.CaptureFinalState(_haulLandings, carrying),
            Visitors.CaptureFinalState(),
            Ecology.CaptureFinalState(),
            Soil.CaptureFinalState(),
            Deck.CaptureFinalState(),
            CaptureConstruction(),
            Elevator.CaptureFinalState(),
            Growth.CaptureFinalState(),
            Voyage.CaptureFinalState(),
            Siege.CaptureFinalState(),
            Flow.CaptureFinalState(),
            Battle.CaptureFinalState());
    }

    internal void RestoreFinalSnapshotState(WorldSnapshot state)
    {
        // 时钟最后拨 —— 期间任何一格读到 Tick 都不该读到「战后的那一格」，
        // 所以先拨回去，再按快照写各子系统。
        Clock.RewindTick(state.Tick);

        DockedCityId = state.DockedCityId;
        PulledAnchor = state.PulledAnchor;
        LastPulledRings = state.LastPulledRings;
        RecruitedThisDock = state.RecruitedThisDock;
        Wanted = state.Wanted;
        AirshipWindowUntilTick = state.AirshipWindowUntilTick;
        AirshipVisitsLeft = state.AirshipVisitsLeft;
        PendingOre = state.PendingOre;
        PendingMaterial = state.PendingMaterial;
        WagesDue = state.WagesDue;
        ArrearsDays = state.ArrearsDays;
        WagesPaidThisDay = state.WagesPaidThisDay;
        _lastHourTick = state.LastHourTick;
        _lastDayTick = state.LastDayTick;
        _lastElevatorTick = state.LastElevatorTick;
        _lastBreachCheckTick = state.LastBreachCheckTick;
        _appliedCollapseCount = state.AppliedCollapseCount;
        _finalDefenceCollapsed = state.FinalDefenceCollapsed;

        _capturedHelpers.Clear();
        foreach (var id in state.CapturedHelpers) _capturedHelpers.Add(id);

        _reviving.Clear();
        foreach (var (key, value) in state.Reviving) _reviving[key] = value;

        _soilCrew.Clear();
        foreach (var (key, value) in state.SoilCrew) _soilCrew[key] = value;

        _starSales.Clear();
        _starSales.AddRange(state.StarSales);
        StarSaleIssued = state.StarSaleIssued;

        _churchCorpses.Clear();
        _churchCorpses.AddRange(state.ChurchCorpses);

        Town.RestoreFinalState(state.Town);
        Roster.RestoreFinalState(state.Roster);
        Items.RestoreFinalState(state.Items);
        Haul.RestoreFinalState(state.Hauling);
        _carrying.Clear();
        foreach (var (id, carry) in state.Hauling.Carrying)
        {
            _carrying[id] = new HaulInHand(carry.Order, carry.Leg);
        }

        _haulLandings.Clear();
        _haulLandings.AddRange(state.Hauling.Landings);

        Visitors.RestoreFinalState(state.Visitors, Roster);
        Ecology.RestoreFinalState(state.Ecology);
        Soil.RestoreFinalState(state.Soil);
        Deck.RestoreFinalState(state.Deck);
        RestoreConstruction(state.Construction);
        Elevator.RestoreFinalState(state.Elevator);
        // 甲板尺寸是位阶的派生值（真值只有 DeckCells 一个字段）：还原位阶前先把甲板拧到那一档，
        // 后面各件读到的边界与阻挡才是快照那一份的。只扩不缩：还原更早档位的快照当场抛。
        ApplyDeckTier(state.Growth.Tier);
        Growth.RestoreFinalState(state.Growth);
        Voyage.RestoreFinalState(state.Voyage, MapStepper);
        Siege.RestoreFinalState(state.Siege);
        Battle.RestoreFinalState(state.Battle, Roster);
        Flow.RestoreFromFinalSnapshot(
            state.Flow.Stage,
            state.Flow.GluttonUnlockedTick,
            state.Flow.FirstSwitchTick,
            state.Flow.Tier2Tick,
            state.Flow.FinalVictoryTick,
            state.Flow.DefenceCityId);
    }
}

public sealed partial class TownAccounts
{
    internal TownSnapshot CaptureFinalState() => new(
        Coin, ProducedCoin, SpentCoin,
        Food, ProducedFood, SpentFood,
        SoulStone, ProducedSoulStone, SpentSoulStone,
        Ore, ProducedOre, SpentOre,
        SpecialMaterial, ProducedSpecialMaterial, SpentSpecialMaterial,
        SparePart, ProducedSparePart, SpentSparePart,
        Crystal, ProducedCrystal, SpentCrystal, CrystalUsed, CrystalSold, TicketIncome,
        AvailableMana, StasisMana, HelperMana, InstantSpentMana, CrystalManaRestored, RegainedMana,
        Policy, PriorityFactionId,
        HotelMultiplier, RestaurantMultiplier, TicketMultiplier,
        _creditors.Select(c => new CreditorSnapshot(c.FactionId, c.Accrued, c.Repaid)).ToList());

    internal void RestoreFinalState(TownSnapshot state)
    {
        Coin = state.Coin;
        ProducedCoin = state.ProducedCoin;
        SpentCoin = state.SpentCoin;
        Food = state.Food;
        ProducedFood = state.ProducedFood;
        SpentFood = state.SpentFood;
        SoulStone = state.SoulStone;
        ProducedSoulStone = state.ProducedSoulStone;
        SpentSoulStone = state.SpentSoulStone;
        Ore = state.Ore;
        ProducedOre = state.ProducedOre;
        SpentOre = state.SpentOre;
        SpecialMaterial = state.SpecialMaterial;
        ProducedSpecialMaterial = state.ProducedSpecialMaterial;
        SpentSpecialMaterial = state.SpentSpecialMaterial;
        SparePart = state.SparePart;
        ProducedSparePart = state.ProducedSparePart;
        SpentSparePart = state.SpentSparePart;
        Crystal = state.Crystal;
        ProducedCrystal = state.ProducedCrystal;
        SpentCrystal = state.SpentCrystal;
        CrystalUsed = state.CrystalUsed;
        CrystalSold = state.CrystalSold;
        TicketIncome = state.TicketIncome;
        AvailableMana = state.AvailableMana;
        StasisMana = state.StasisMana;
        HelperMana = state.HelperMana;
        InstantSpentMana = state.InstantSpentMana;
        CrystalManaRestored = state.CrystalManaRestored;
        RegainedMana = state.RegainedMana;
        Policy = state.Policy;
        PriorityFactionId = state.PriorityFactionId;
        HotelMultiplier = state.HotelMultiplier;
        RestaurantMultiplier = state.RestaurantMultiplier;
        TicketMultiplier = state.TicketMultiplier;

        foreach (var snapshot in state.Creditors)
        {
            var creditor = CreditorOf(snapshot.FactionId);
            creditor?.RestoreLedger(snapshot.Accrued, snapshot.Repaid);
        }
    }
}

public sealed partial class CreditorAccount
{
    internal void RestoreLedger(double accrued, double repaid)
    {
        Accrued = accrued;
        Repaid = repaid;
    }
}

public sealed partial class PeopleRoster
{
    internal RosterSnapshot CaptureFinalState(IReadOnlyList<UnitSnapshot> units) => new(
        units,
        new Dictionary<string, double>(_unpaidWages, StringComparer.Ordinal),
        _nextRosterIndex,
        _spawnCursor,
        _entities.Count,
        _entities.IssuedIds,
        _entities.Issued);

    internal void RestoreFinalState(RosterSnapshot state)
    {
        // 战斗里多出来的（召唤帮工等）：从册子上摘掉。实体册的 issued 是单调序号，
        // 不会回收 —— 它不参与战场读数；没有新增单位时两份 issued 自然相等。
        var keep = new HashSet<string>(state.Units.Select(u => u.Id), StringComparer.Ordinal);
        for (var i = _units.Count - 1; i >= 0; i--)
        {
            var id = _units[i].Id;
            if (keep.Contains(id)) continue;
            _units.RemoveAt(i);
            _byId.Remove(id);
            _deck.Unregister(id);
            _entities.Remove(id);
        }

        var ordered = new List<ArenaUnit>(state.Units.Count);
        foreach (var snapshot in state.Units)
        {
            var unit = Find(snapshot.Id);
            if (unit is null) continue;
            RestoreUnit(unit, snapshot);
            ordered.Add(unit);
        }

        _units.Clear();
        _units.AddRange(ordered);
        _byId.Clear();
        foreach (var unit in _units) _byId[unit.Id] = unit;

        _posts.Clear();
        foreach (var unit in _units)
        {
            if (unit.Post.Length == 0) continue;
            if (!_posts.TryGetValue(unit.Post, out var list))
            {
                list = [];
                _posts[unit.Post] = list;
            }

            list.Add(unit.Id);
        }

        _unpaidWages.Clear();
        foreach (var (id, days) in state.UnpaidWages) _unpaidWages[id] = days;

        // 战斗里多发的实体槽与序号也一并作废：重开那一局再召同一个人，
        // 拿到的还是原来那个 id 与序号（逐字比对靠这一条）。
        _entities.TruncateTo(state.EntitySlots, state.EntityIssued);

        _nextRosterIndex = state.NextRosterIndex;
        _spawnCursor = state.SpawnCursor;

        // 寻路层的位置与命令按同一份快照还回去：位置是屏幕读数的真值，
        // 命令决定重试之后第一步还走不走。命令为空的那一档按原地驻守还。
        foreach (var snapshot in state.Units)
        {
            if (!_deck.HasAgent(snapshot.Id)) continue;
            _deck.ApplyMove(snapshot.Id, snapshot.NavPosition, 0.0);
            _deck.SetCountedInSeparation(snapshot.Id, snapshot.CountedInSeparation);
            if (snapshot.NavCommand is { } command) _deck.Issue(snapshot.Id, command);
            else _deck.RestoreCommand(snapshot.Id, null);
        }
    }

    private static void RestoreUnit(ArenaUnit unit, UnitSnapshot s)
    {
        unit.Role = s.Role;
        unit.Melee = s.Melee;
        unit.Ranged = s.Ranged;
        unit.Riding = s.Riding;
        unit.Hp = s.Hp;
        unit.State = s.State;
        unit.Loyalty = s.Loyalty;
        unit.Mood = s.Mood;
        unit.Post = s.Post;
        unit.PersonalReputation = s.PersonalReputation;
        unit.NonCombatant = s.NonCombatant;
        unit.LeftTown = s.LeftTown;
        unit.Held = s.Held;
        unit.DeckX = s.DeckX;
        unit.DeckZ = s.DeckZ;
        unit.Moving = s.Moving;
        unit.RiseTick = s.RiseTick;
        unit.Contribution = s.Contribution;
    }
}

public sealed partial class PointItems
{
    internal ItemsSnapshot CaptureFinalState() => new(
        _items.Select(i => new ItemSnapshot(
            i.Id, i.Kind, i.Payload, i.DeckX, i.DeckZ, i.Removed, i.Held)).ToList(),
        new Dictionary<string, int>(_counters, StringComparer.Ordinal));

    internal void RestoreFinalState(ItemsSnapshot state)
    {
        _items.Clear();
        _byId.Clear();
        foreach (var snapshot in state.Items)
        {
            var item = new PointItem(
                snapshot.Id, snapshot.Kind, new NavPoint(snapshot.DeckX, snapshot.DeckZ), snapshot.Payload);
            if (snapshot.Removed) item.Retire();
            if (snapshot.Held) item.Hold();
            _items.Add(item);
            _byId[item.Id] = item;
        }

        _counters.Clear();
        foreach (var (key, value) in state.Counters) _counters[key] = value;
    }
}

public sealed partial class HaulQueue
{
    internal HaulingSnapshot CaptureFinalState(
        IReadOnlyList<HaulLanding> landings,
        IReadOnlyDictionary<string, CarrySnapshot> carrying)
        => new(Pending.ToList(), landings.ToList(), carrying);

    internal void RestoreFinalState(HaulingSnapshot state)
    {
        foreach (var level in _levels) level.Clear();
        foreach (var order in state.Orders) _levels[(int)order.Level].Add(order);
    }
}

public sealed partial class VisitorRecord
{
    /// <summary>还原剩余预算（快照那一份写回；不经过消费路径）。</summary>
    internal void RestoreBudget(double budget) => Budget = budget;
}

public sealed partial class VisitorPool
{
    internal VisitorsSnapshot CaptureFinalState() => new(
        _poolAvailable,
        _cap,
        _nextVisitorNumber,
        _lastMinute,
        _admitCarry,
        _townReputation,
        _priceFactor,
        _arenaTarget,
        _townHotelMultiplier,
        _townRestaurantMultiplier,
        _townTicketMultiplier,
        _inTown.Select(v => new VisitorUnitSnapshot(
            v.Unit.Id, v.Budget, v.StayGameMinutes, v.EnteredTick, v.Activity,
            v.TargetBuildingId, v.BusyUntilTick, v.LeaveTick)).ToList());

    internal void RestoreFinalState(VisitorsSnapshot state, PeopleRoster roster)
    {
        _poolAvailable = state.PoolAvailable;
        _cap = state.Cap;
        _nextVisitorNumber = state.NextVisitorNumber;
        _lastMinute = state.LastMinute;
        _admitCarry = state.AdmitCarry;
        _townReputation = state.TownReputation;
        _priceFactor = state.PriceFactor;
        _arenaTarget = state.ArenaTarget;
        _townHotelMultiplier = state.TownHotelMultiplier;
        _townRestaurantMultiplier = state.TownRestaurantMultiplier;
        _townTicketMultiplier = state.TownTicketMultiplier;

        _inTown.Clear();
        foreach (var snapshot in state.InTown)
        {
            var unit = roster.Find(snapshot.UnitId);
            if (unit is null) continue;
            var record = new VisitorRecord(unit, snapshot.Budget, snapshot.StayGameMinutes, snapshot.EnteredTick)
            {
                Activity = snapshot.Activity,
                TargetBuildingId = snapshot.TargetBuildingId,
                BusyUntilTick = snapshot.BusyUntilTick,
                LeaveTick = snapshot.LeaveTick,
            };
            record.RestoreBudget(snapshot.Budget);
            _inTown.Add(record);
        }
    }
}

public sealed partial class BeastEcology
{
    internal EcologySnapshot CaptureFinalState() => new(
        Satiation,
        DigestionProgress,
        CrystalsProduced,
        HungerDecayFactor,
        ForageCooldownUntilTick,
        ForageGaveUpReason,
        _lastDecayTick,
        _nodes.Select(n => new EcoNodeSnapshot(n.Id, n.AvailableAtTick, n.Consumed)).ToList());

    internal void RestoreFinalState(EcologySnapshot state)
    {
        Satiation = state.Satiation;
        DigestionProgress = state.DigestionProgress;
        CrystalsProduced = state.CrystalsProduced;
        HungerDecayFactor = state.HungerDecayFactor;
        ForageCooldownUntilTick = state.ForageCooldownUntilTick;
        ForageGaveUpReason = state.ForageGaveUpReason;
        _lastDecayTick = state.LastDecayTick;

        var byId = new Dictionary<string, EcoNodeSnapshot>(StringComparer.Ordinal);
        foreach (var n in state.Nodes) byId[n.Id] = n;
        foreach (var node in _nodes)
        {
            if (!byId.TryGetValue(node.Id, out var snapshot)) continue;
            node.AvailableAtTick = snapshot.AvailableAtTick;
            node.Consumed = snapshot.Consumed;
        }
    }
}

public sealed partial class UndergroundSoil
{
    internal SoilSnapshot CaptureFinalState()
    {
        var cells = new List<SoilCellSnapshot>(CellCount);
        foreach (var cell in _cells.Values.OrderBy(c => c.Sector).ThenBy(c => c.Layer))
        {
            cells.Add(new SoilCellSnapshot(
                cell.Sector, cell.Layer, cell.Dug, cell.Cleared, cell.Embedded,
                cell.RegrowthProgress, cell.Miners, cell.DigWorkers, cell.DigDoneTick,
                cell.Digging, cell.OreAccumulated, cell.MaterialAccumulated, cell.CaveMonsters));
        }

        return new SoilSnapshot(cells, _collapses.ToList());
    }

    internal void RestoreFinalState(SoilSnapshot state)
    {
        foreach (var snapshot in state.Cells)
        {
            var cell = Require(snapshot.Sector, snapshot.Layer);
            cell.Dug = snapshot.Dug;
            cell.Cleared = snapshot.Cleared;
            cell.Embedded = snapshot.Embedded;
            cell.RegrowthProgress = snapshot.RegrowthProgress;
            cell.Miners = snapshot.Miners;
            cell.DigWorkers = snapshot.DigWorkers;
            cell.DigDoneTick = snapshot.DigDoneTick;
            cell.Digging = snapshot.Digging;
            cell.OreAccumulated = snapshot.OreAccumulated;
            cell.MaterialAccumulated = snapshot.MaterialAccumulated;
            cell.CaveMonsters = snapshot.CaveMonsters;
        }

        _collapses.Clear();
        _collapses.AddRange(state.Collapses);
    }
}

public sealed partial class DeckConstruction
{
    internal DeckSnapshot CaptureFinalState() => new(
        SpareParts,
        _lifted.OrderBy(x => x, StringComparer.Ordinal).ToList(),
        _repairs.ToList(),
        new Dictionary<string, long>(_repairDoneTick, StringComparer.Ordinal),
        (int[])_manaOccupied.Clone(),
        _nextBuildingNumber);

    internal void RestoreFinalState(DeckSnapshot state)
    {
        SpareParts = state.SpareParts;
        _lifted.Clear();
        foreach (var id in state.Lifted) _lifted.Add(id);
        _repairs.Clear();
        _repairs.AddRange(state.Repairs);
        _repairDoneTick.Clear();
        foreach (var (id, tick) in state.RepairDoneTick) _repairDoneTick[id] = tick;
        Array.Copy(state.ManaOccupied, _manaOccupied, _manaOccupied.Length);
        _nextBuildingNumber = state.NextBuildingNumber;
    }
}

public sealed partial class ArenaWorld
{
    private ConstructionSnapshot CaptureConstruction()
    {
        var rows = new List<BuildingRecord>();
        foreach (var id in Construction.Ids)
        {
            var building = Construction.Find(id);
            if (building is null) continue;
            var anchor = building.Anchor ?? default;
            rows.Add(new BuildingRecord(id, building.BlueprintId, anchor.Col, anchor.Row, building.Health));
        }

        return new ConstructionSnapshot(rows);
    }

    private void RestoreConstruction(ConstructionSnapshot state)
    {
        for (var i = 0; i < state.Rows.Count; i++)
        {
            var (id, blueprintId, col, row, health) = state.Rows[i];
            // 战中被打破、已经出册的那一座先请回册上（落回它原来那一格 —— 快照的栋序就是册序），
            // 再补耐久：SetHealth 对不在册的 id 是空动作，只补血的话那一座就永远回不来了。
            if (Construction.Find(id) is null)
                Deck.ReRegisterFixture(blueprintId, col, row, id, i);
            Construction.SetHealth(id, health);
        }
    }
}

public sealed partial class MagicElevator
{
    internal ElevatorSnapshot CaptureFinalState() => new(
        _shaftQueue.ToList(),
        _lastSettlement.ToList(),
        new Dictionary<int, double>(_oreDrawn),
        new Dictionary<int, double>(_materialDrawn),
        _settlements);

    internal void RestoreFinalState(ElevatorSnapshot state)
    {
        _shaftQueue.Clear();
        _shaftQueue.AddRange(state.ShaftQueue);
        _lastSettlement.Clear();
        _lastSettlement.AddRange(state.LastSettlement);
        _oreDrawn.Clear();
        foreach (var (key, value) in state.OreDrawn) _oreDrawn[key] = value;
        _materialDrawn.Clear();
        foreach (var (key, value) in state.MaterialDrawn) _materialDrawn[key] = value;
        _settlements = state.Settlements;
    }
}

public sealed partial class BeastGrowth
{
    internal GrowthSnapshot CaptureFinalState() => new(
        Tier, Form, RitualUntilTick, DwellGameMinutes, FedTotalFood,
        DesertFormUnlocked, GluttonFormUnlocked, Tier2Eligible);

    internal void RestoreFinalState(GrowthSnapshot state)
    {
        Tier = state.Tier;
        Form = state.Form;
        RitualUntilTick = state.RitualUntilTick;
        DwellGameMinutes = state.DwellGameMinutes;
        FedTotalFood = state.FedTotalFood;
        DesertFormUnlocked = state.DesertFormUnlocked;
        GluttonFormUnlocked = state.GluttonFormUnlocked;
        Tier2Eligible = state.Tier2Eligible;
    }
}

public sealed partial class BeastVoyage
{
    internal VoyageSnapshot CaptureFinalState()
    {
        var at = _stepper.PositionOf(BeastId);
        return new VoyageSnapshot(
            Phase, TargetCityId, ManualTarget, RouteWaypoint, SoulStones, GranaryFood, TierSpeedFactor,
            _lastDrainMinute, _eatUntilTick, _forageNodeId, _manualNodeId, _manualEatUntilTick,
            _manualResumeCityId, _manualResumePoint, _appliedSpeedFactor, _outboundTarget,
            at, _stepper.CommandOf(BeastId));
    }

    internal void RestoreFinalState(VoyageSnapshot state, NavStepper mapStepper)
    {
        Phase = state.Phase;
        TargetCityId = state.TargetCityId;
        ManualTarget = state.ManualTarget;
        RouteWaypoint = state.RouteWaypoint;
        SoulStones = state.SoulStones;
        GranaryFood = state.GranaryFood;
        TierSpeedFactor = state.TierSpeedFactor;
        _lastDrainMinute = state.LastDrainMinute;
        _eatUntilTick = state.EatUntilTick;
        _forageNodeId = state.ForageNodeId;
        _manualNodeId = state.ManualNodeId;
        _manualEatUntilTick = state.ManualEatUntilTick;
        _manualResumeCityId = state.ManualResumeCityId;
        _manualResumePoint = state.ManualResumePoint;
        _appliedSpeedFactor = state.AppliedSpeedFactor;
        _outboundTarget = state.OutboundTarget;

        mapStepper.ApplyMove(BeastId, state.BeastPosition, 0.0);
        if (state.BeastCommand is { } command) mapStepper.Issue(BeastId, command);
        else mapStepper.RestoreCommand(BeastId, null);
    }
}

public sealed partial class SiegeState
{
    internal SiegeSnapshot CaptureFinalState() => new(
        Breached, BreachTick, PendingDebt, StandstillUntilTick, RescueFactionId,
        _warLossBuildings.ToList(), _takenPeople.ToList());

    internal void RestoreFinalState(SiegeSnapshot state)
    {
        Breached = state.Breached;
        BreachTick = state.BreachTick;
        PendingDebt = state.PendingDebt;
        StandstillUntilTick = state.StandstillUntilTick;
        RescueFactionId = state.RescueFactionId;
        _warLossBuildings.Clear();
        _warLossBuildings.AddRange(state.WarLossBuildings);
        _takenPeople.Clear();
        _takenPeople.AddRange(state.TakenPeople);
    }
}

public sealed partial class RunFlow
{
    internal FlowSnapshot CaptureFinalState() => new(
        Stage, _gluttonUnlockedTick, _firstSwitchTick, _tier2Tick, _finalVictoryTick, _defenceCityId);
}

public sealed partial class BattleSim
{
    internal BattleSnapshot CaptureFinalState() => new(
        Occasion,
        EndTick,
        _sideA.Select(u => u.Id).ToList(),
        _sideB.Select(u => u.Id).ToList(),
        new Dictionary<string, string>(_enemyTargets, StringComparer.Ordinal),
        new Dictionary<string, int>(_enemyLoot, StringComparer.Ordinal),
        _enemyNumber,
        _monsterNumber,
        _caveSector,
        _caveLayer,
        _caveCleared,
        _finalDefenceFailed,
        OnFieldCount,
        DownCount,
        DeadCount,
        LootedCount,
        LastReport,
        _deadAtOpen.OrderBy(x => x, StringComparer.Ordinal).ToList(),
        _woundedAtOpen.OrderBy(x => x, StringComparer.Ordinal).ToList());

    internal void RestoreFinalState(BattleSnapshot state, PeopleRoster roster)
    {
        Occasion = state.Occasion;
        EndTick = state.EndTick;
        OnFieldCount = state.OnFieldCount;
        DownCount = state.DownCount;
        DeadCount = state.DeadCount;
        LootedCount = state.LootedCount;
        LastReport = state.LastReport;
        _enemyNumber = state.EnemyNumber;
        _monsterNumber = state.MonsterNumber;
        _caveSector = state.CaveSector;
        _caveLayer = state.CaveLayer;
        _caveCleared = state.CaveCleared;
        _finalDefenceFailed = state.FinalDefenceFailed;

        _pairs.Clear();
        _sideA.Clear();
        foreach (var id in state.SideAIds)
        {
            var unit = roster.Find(id);
            if (unit is not null) _sideA.Add(unit);
        }

        _sideB.Clear();
        foreach (var id in state.SideBIds)
        {
            var unit = roster.Find(id);
            if (unit is not null) _sideB.Add(unit);
        }

        _enemyTargets.Clear();
        foreach (var (id, target) in state.EnemyTargets) _enemyTargets[id] = target;
        _enemyLoot.Clear();
        foreach (var (id, loot) in state.EnemyLoot) _enemyLoot[id] = loot;

        _deadAtOpen.Clear();
        foreach (var id in state.DeadAtOpen) _deadAtOpen.Add(id);
        _woundedAtOpen.Clear();
        foreach (var id in state.WoundedAtOpen) _woundedAtOpen.Add(id);
    }
}
