#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 搬运那一摊：点位物（尸体 / 散货 / 魔晶）、三级优先级搬运队列、镇政府魔法电梯。
//
// 四条口径：
//   · **队列三级**：搬尸体进教堂 > 搬备件修建筑 > 地面散货（含魔晶）入库；同级 FIFO。
//   · **电梯直通仓库**：电梯运上来的矿石与特殊材料**直接入库**，不生成地面二次搬运任务。
//   · **手抓在手物退回原位并重新入队**：入队是 append 到本级队尾（FIFO 的那一半）。
//   · **矿石累计量在土壤册里，抽取量记在电梯这一头** —— 土壤册是 ticket 10 交付的那份，
//     不回改；「这一格已经运走多少」记在本件里，差额就是这一轮该运的量。

/// <summary>点位物的三类（静止物不进实体册的活物段，单独一段印）。</summary>
public enum PointItemKind
{
    /// <summary>尸体。</summary>
    Corpse = 0,

    /// <summary>地面散货（矿石 / 特殊材料 / 备件 / 食物）。</summary>
    Cargo = 1,

    /// <summary>魔晶（巨兽消化产出的珍稀货物，落甲等工人或帮工逐件入库）。</summary>
    Crystal = 2,
}

/// <summary>一个静止点位物。</summary>
public sealed class PointItem
{
    /// <summary>建一个点位物。</summary>
    public PointItem(string id, PointItemKind kind, NavPoint atDeck, string payload = "")
    {
        Id = id;
        Kind = kind;
        DeckX = atDeck.X;
        DeckZ = atDeck.Z;
        Payload = payload;
    }

    /// <summary>点位物 id（按生成序，永不复发）。</summary>
    public string Id { get; }

    /// <summary>类别。</summary>
    public PointItemKind Kind { get; }

    /// <summary>
    /// 散货那一档装的是什么（<c>ore</c> / <c>material</c> / <c>sparepart</c> / <c>food</c>；
    /// 魔晶那一档是 <c>crystal</c>；空串 = 无载荷）。
    /// 尸体那一档恒空串 —— 载荷是「这一件是什么」那一列，不是另一个真值本。
    /// </summary>
    public string Payload { get; }

    /// <summary>甲板逻辑坐标的读数。</summary>
    public double DeckX { get; private set; }

    /// <summary>甲板逻辑坐标的读数。</summary>
    public double DeckZ { get; private set; }

    /// <summary>还在场上没有（被搬走 / 被复活 / 被扔掉）。</summary>
    public bool Removed { get; private set; }

    /// <summary>
    /// 是否被上帝之手抓着 —— <b>「手上那一件」在本件上就是真值</b>
    /// （快照那一格 <c>held=</c> 读它；活物那一档读的是 <see cref="ArenaUnit.Held"/>）。
    /// </summary>
    public bool Held { get; private set; }

    /// <summary>离场（摘册那一格：实体层没有注销口，点位物自己是一张表，标记即可）。</summary>
    public void Retire() => Removed = true;

    /// <summary>抓在手上（<c>GrabEntity</c> 那一发）。</summary>
    public void Hold() => Held = true;

    /// <summary>
    /// 放下（松手那一发）：<b>成功与被拒都算放下</b> ——
    /// 留着 <see cref="Held"/> 会让这一件一直被当成「在手」，快照那一格也清不掉。
    /// </summary>
    public void Release() => Held = false;

    /// <summary>
    /// 换落点（<c>DropToDeck</c> 那一发）：<b>只改落点</b>，
    /// 库存、在册状态与手上那一档一格不动。
    /// </summary>
    public void MoveTo(NavPoint atDeck)
    {
        DeckX = atDeck.X;
        DeckZ = atDeck.Z;
    }

    /// <summary>甲板逻辑坐标（读数口）。</summary>
    public NavPoint AtDeck => new(DeckX, DeckZ);

    /// <summary>类别那一列的文本。</summary>
    public string KindText => Kind switch
    {
        PointItemKind.Corpse => "corpse",
        PointItemKind.Crystal => "crystal",
        _ => "cargo",
    };

    /// <summary>单个点位物那一段（双词段首身份）。</summary>
    public string DebugSegment() => string.Format(CultureInfo.InvariantCulture,
        "ITEM {0} kind={1} pos={2:0.##},{3:0.##}", Id, KindText, DeckX, DeckZ);
}

/// <summary>静止点位物那一本（按生成序）。</summary>
public sealed partial class PointItems
{
    private readonly List<PointItem> _items = [];
    private readonly Dictionary<string, PointItem> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    /// <summary>在册点位物（按生成序，含已离场的——顺序不能挪）。</summary>
    public IReadOnlyList<PointItem> All => _items;

    /// <summary>还在场上的那几件。</summary>
    public IEnumerable<PointItem> Live
    {
        get
        {
            foreach (var i in _items)
            {
                if (!i.Removed) yield return i;
            }
        }
    }

    /// <summary>场上还没搬走的尸体（情绪负面圈与搬运队列都读它）。</summary>
    public IEnumerable<PointItem> LiveCorpses
    {
        get
        {
            foreach (var i in _items)
            {
                if (!i.Removed && i.Kind == PointItemKind.Corpse) yield return i;
            }
        }
    }

    /// <summary>放一件点位物（发 id、记位置、进册）。</summary>
    public PointItem Add(PointItemKind kind, NavPoint atDeck, string payload = "")
    {
        var token = kind switch
        {
            PointItemKind.Corpse => "corpse",
            PointItemKind.Crystal => "crystal",
            _ => "cargo",
        };

        _counters[token] = _counters.GetValueOrDefault(token) + 1;
        var id = $"{token}{_counters[token]}";
        var item = new PointItem(id, kind, atDeck, payload);
        _items.Add(item);
        _byId[id] = item;
        return item;
    }

    /// <summary>按 id 查（查不到或已离场回 null）。</summary>
    public PointItem? Find(string id)
        => _byId.TryGetValue(id, out var item) && !item.Removed ? item : null;

    /// <summary>在场几件。</summary>
    public int LiveCount
    {
        get
        {
            var n = 0;
            foreach (var i in _items)
            {
                if (!i.Removed) n++;
            }

            return n;
        }
    }

    /// <summary>还剩几具尸体没搬走。</summary>
    public int LiveCorpseCount
    {
        get
        {
            var n = 0;
            foreach (var i in _items)
            {
                if (!i.Removed && i.Kind == PointItemKind.Corpse) n++;
            }

            return n;
        }
    }

    /// <summary>点位物那一段的读数（先总数一行，再按生成序一件一段）。</summary>
    public string DebugSegments()
    {
        var lines = new List<string>(_items.Count + 1);
        var live = 0;
        foreach (var i in _items)
        {
            if (!i.Removed) live++;
        }

        lines.Add(string.Format(CultureInfo.InvariantCulture,
            "item count={0} issued={1}", live, _items.Count));
        foreach (var i in _items) lines.Add(i.DebugSegment());
        return string.Join(" | ", lines);
    }
}

/// <summary>搬运队列的三级优先级，数字越小越优先。</summary>
public enum HaulLevel
{
    /// <summary>搬尸体进教堂。</summary>
    CorpseToChurch = 0,

    /// <summary>搬备件修建筑。</summary>
    SparePartToRepair = 1,

    /// <summary>地面散货（矿石 / 特殊材料 / 魔晶）入库。</summary>
    GroundCargoToWarehouse = 2,
}

/// <summary>一纸搬运任务单。</summary>
/// <param name="Level">优先级档。</param>
/// <param name="ItemId">搬的那件点位物（空串 = 直接送的一批货，不经点位物）。</param>
/// <param name="OriginDeck">取货点（甲板逻辑坐标）。</param>
/// <param name="TargetBuildingId">送到哪一栋。</param>
/// <param name="Amount">这一单搬多少（矿石 / 特殊材料按件算，备件按个，食物按份）。</param>
/// <param name="QueuedTick">入队那一刻的步号（FIFO 那一档靠它读，不靠入队时刻的浮点时刻）。</param>
public sealed record HaulOrder(
    HaulLevel Level,
    string ItemId,
    NavPoint OriginDeck,
    string TargetBuildingId,
    double Amount,
    long QueuedTick);

/// <summary>一单在手物走到哪一程了。</summary>
public enum HaulLeg
{
    /// <summary>正朝取货点走（还没装货）。</summary>
    ToPickup = 0,

    /// <summary>装着货正朝目标门口走。</summary>
    ToDeliver = 1,
}

/// <summary>一个人手上那一纸连同它走到哪一程 —— 派单那一格与到达那一格共用这一份。</summary>
/// <param name="Order">手上那一纸。</param>
/// <param name="Leg">走到哪一程了。</param>
public readonly record struct HaulInHand(HaulOrder Order, HaulLeg Leg);

/// <summary>一次交单的结果（状态可查那一口：交单那一格发生了什么）。</summary>
/// <param name="Tick">交单那一刻的步号。</param>
/// <param name="CarrierId">交单的人。</param>
/// <param name="Level">那一纸的优先级档。</param>
/// <param name="ItemId">交的那一件（空串 = 没有在册的货）。</param>
/// <param name="TargetBuildingId">送进哪一栋。</param>
/// <param name="Landed">投进去了没有（被拒时货仍在甲板上）。</param>
/// <param name="Reason">被拒的原因（投进去那一档是空串）。</param>
public readonly record struct HaulLanding(
    long Tick,
    string CarrierId,
    HaulLevel Level,
    string ItemId,
    string TargetBuildingId,
    bool Landed,
    string Reason);

/// <summary>
/// 三级优先搬运队列 —— <b>取单只看级别与入队序，不看别的</b>。
/// 同级 FIFO（append 到队尾、take 从队头），跨级先取级别小的。
/// </summary>
public sealed partial class HaulQueue
{
    private readonly List<HaulOrder>[] _levels;

    /// <summary>造一条三级队列。</summary>
    public HaulQueue()
    {
        _levels = new List<HaulOrder>[ArenaTuning.HaulLevelCount];
        for (var i = 0; i < _levels.Length; i++) _levels[i] = [];
    }

    /// <summary>排队总数。</summary>
    public int Count
    {
        get
        {
            var n = 0;
            foreach (var l in _levels) n += l.Count;
            return n;
        }
    }

    /// <summary>某一级排队几纸。</summary>
    public int CountAt(HaulLevel level) => _levels[(int)level].Count;

    /// <summary>
    /// 队列里在排的那些（<b>按级别再按入队序</b>，与 <see cref="TakeNext"/> 的取单次序同序）。
    ///
    /// <para><b>给「这一类需求有没有在途」那一格读它用</b>：产地排单之前先看一遍，
    /// 才排得出「同一件货一张单、同一类需求在途一张单」那两条防单海的读数。</para>
    /// </summary>
    public IEnumerable<HaulOrder> Pending
    {
        get
        {
            foreach (var level in _levels)
            {
                foreach (var order in level) yield return order;
            }
        }
    }

    /// <summary>入队（append 到本级队尾）。</summary>
    public void Enqueue(HaulOrder order) => _levels[(int)order.Level].Add(order);

    /// <summary>取单：级别小的先取，同级从队头取。空表回 null。</summary>
    public HaulOrder? TakeNext()
    {
        foreach (var level in _levels)
        {
            if (level.Count == 0) continue;

            var order = level[0];
            level.RemoveAt(0);
            return order;
        }

        return null;
    }

    /// <summary>看一眼下一纸是哪一级（不取走；空表回 null）—— 上帝之手抓人那一格读它。</summary>
    public HaulOrder? PeekNext()
    {
        foreach (var level in _levels)
        {
            if (level.Count > 0) return level[0];
        }

        return null;
    }

    /// <summary>
    /// 退回原位并重新入队（手抓在手物那一格）：<b>回到本级队尾</b> ——
    /// 重新入队不是回到队头，同级 FIFO 那一半就靠这一条。
    /// </summary>
    public bool Requeue(HaulOrder order)
    {
        if ((int)order.Level < 0 || (int)order.Level >= _levels.Length) return false;

        _levels[(int)order.Level].Add(order);
        return true;
    }

    /// <summary>丢掉某一纸（送到了、或者目标已经没了）。</summary>
    public bool Drop(HaulOrder order) => _levels[(int)order.Level].Remove(order);

    /// <summary>状态可查那一口：三级各排几纸（级别小的在前）。</summary>
    public string DebugText()
    {
        var parts = new List<string>(_levels.Length);
        for (var i = 0; i < _levels.Length; i++)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}={1}", (HaulLevel)i, _levels[i].Count));
        }

        return string.Join("/", parts);
    }
}

/// <summary>电梯一次结算里占掉的那一个名额（状态可查那一口）。</summary>
/// <param name="Slot">第几个名额（0..3）。</param>
/// <param name="Kind">这一名额派给了什么（<c>person</c> / <c>ore</c> / <c>material</c>）。</param>
/// <param name="Subject">人的 id 或那一格的 <c>象限_层</c>。</param>
/// <param name="Amount">矿石 / 特殊材料运了几件（人那一档是 0）。</param>
public sealed record ElevatorSlot(int Slot, string Kind, string Subject, double Amount);

/// <summary>
/// 镇政府里的魔法电梯 —— 每 <see cref="ArenaTuning.ElevatorGameMinutes"/> 个游戏分钟结算
/// <see cref="ArenaTuning.ElevatorSlotsPerSettlement"/> 个名额，
/// <b>上下井的工作人员优先、剩余名额把地下采出的矿石与特殊材料直接运进镇政府仓库</b>。
///
/// <para>电梯直通仓库：运上来的料<b>直接入库，不生成地面二次搬运任务</b>。</para>
///
/// <para><b>土壤册里没有「抽走」的写口</b>（那份是 ticket 10 交付的，不回改），
/// 所以「这一格已经运走多少」记在本件里，每次运的是 <c>累计 − 已运</c> 的差额。
/// 这一格是纯账本，不改任何一格土壤的状态。</para>
///
/// <para><b>那本「已运走」的账是电梯与地面二次搬运共用的</b>：电梯名额用完了、
/// 某一格还欠着的整件由 <see cref="TakeOreOffCell"/> / <see cref="TakeMaterialOffCell"/>
/// 搬上甲板，两条路都记进同一本账 —— 同一件矿不会被电梯再运一遍，
/// 也不会被两条路各运一遍（那一份矿石会凭空多出 1 件进仓库）。</para>
/// </summary>
public sealed partial class MagicElevator
{
    private readonly List<string> _shaftQueue = [];
    private readonly List<ElevatorSlot> _lastSettlement = [];
    private readonly Dictionary<int, double> _oreDrawn = [];
    private readonly Dictionary<int, double> _materialDrawn = [];
    private long _settlements;

    /// <summary>镇政府仓库那一栋的 id（电梯运上来的料直接进它）。</summary>
    public string TownHallId { get; init; } = "town_hall";

    /// <summary>上一次结算派出去的那几个名额（读数口）。</summary>
    public IReadOnlyList<ElevatorSlot> LastSettlement => _lastSettlement;

    /// <summary>结算过几轮。</summary>
    public long Settlements => _settlements;

    /// <summary>井口队列里还有几个人等着上下（读数口）。</summary>
    public int ShaftQueueLength => _shaftQueue.Count;

    /// <summary>某人要上下井（入列按入列序，同 FIFO）。已在列里不重复入列。</summary>
    public bool EnqueueShaft(string unitId)
    {
        if (_shaftQueue.Contains(unitId)) return false;

        _shaftQueue.Add(unitId);
        return true;
    }

    /// <summary>这一轮该不该结算（挂在整 <see cref="ArenaTuning.ElevatorGameMinutes"/> 个游戏分钟上）。</summary>
    public static bool IsSettlementTick(long tick)
        => tick > 0 && tick % (ArenaTime.StepsPerGameMinute * ArenaTuning.ElevatorGameMinutes) == 0;

    /// <summary>
    /// 结算一轮：人先货后，人满 4 名就不再运料。
    /// </summary>
    /// <param name="soil">地下土壤册（读累计产量）。</param>
    /// <param name="deliver">把矿石 / 特殊材料直接送进镇政府仓库那一口（<c>(矿, 材)</c>）。</param>
    /// <param name="board">把某个人送上井那一口（<c>unitId</c>；回 false = 那一格不算，占的名额退回）。</param>
    public IReadOnlyList<ElevatorSlot> Settle(UndergroundSoil soil, Action<double, double> deliver, Func<string, bool> board)
    {
        ArgumentNullException.ThrowIfNull(soil);
        ArgumentNullException.ThrowIfNull(deliver);
        ArgumentNullException.ThrowIfNull(board);

        _lastSettlement.Clear();
        _settlements++;

        // ① 人先：上下井的工作人员按入列序占名额，满 4 名就不再往下运料。
        var used = 0;
        while (used < ArenaTuning.ElevatorSlotsPerSettlement && _shaftQueue.Count > 0)
        {
            var unitId = _shaftQueue[0];
            var accepted = board(unitId);
            _shaftQueue.RemoveAt(0);   // 人已离镇 / 已死 / 不该上井 —— 从井口队列里摘掉，不再占名额
            if (!accepted) continue;

            _lastSettlement.Add(new ElevatorSlot(used, "person", unitId, 0.0));
            used++;
        }

        // ② 剩余名额：矿石与特殊材料按象限再按层的固定次序，直通镇政府仓库。
        var ore = 0.0;
        var mat = 0.0;
        foreach (var (sector, layer) in CellOrder())
        {
            if (used >= ArenaTuning.ElevatorSlotsPerSettlement) break;
            if (!soil.IsDug(sector, layer)) continue;

            var takeOre = Math.Floor(OreLeftAt(soil, sector, layer));
            if (takeOre >= 1.0)
            {
                _oreDrawn[KeyOf(sector, layer)] = _oreDrawn.GetValueOrDefault(KeyOf(sector, layer)) + takeOre;
                _lastSettlement.Add(new ElevatorSlot(used, "ore", $"{sector}_{layer}", takeOre));
                used++;
                ore += takeOre;
            }

            if (used >= ArenaTuning.ElevatorSlotsPerSettlement) break;

            var takeMat = Math.Floor(MaterialLeftAt(soil, sector, layer));
            if (takeMat >= 1.0)
            {
                _materialDrawn[KeyOf(sector, layer)] =
                    _materialDrawn.GetValueOrDefault(KeyOf(sector, layer)) + takeMat;
                _lastSettlement.Add(new ElevatorSlot(used, "material", $"{sector}_{layer}", takeMat));
                used++;
                mat += takeMat;
            }
        }

        // 电梯直通仓库：不生成地面二次搬运任务。
        deliver(ore, mat);
        return _lastSettlement;
    }

    // ---- 「已运走」那一本账：电梯与地面二次搬运共用同一份 ----

    /// <summary>某一格的坐标折成账本里那一个键（象限 0..3 再层 1..3）。</summary>
    private static int KeyOf(int sector, int layer)
        => (sector * (UndergroundSoil.Layers + 1)) + layer;

    /// <summary>某一格「累计 − 已运走」的矿石（负数钳到 0）。</summary>
    public double OreLeftAt(UndergroundSoil soil, int sector, int layer)
        => Math.Max(0.0, soil.OreAccumulatedAt(sector, layer) - _oreDrawn.GetValueOrDefault(KeyOf(sector, layer)));

    /// <summary>某一格「累计 − 已运走」的特殊材料（负数钳到 0）。</summary>
    public double MaterialLeftAt(UndergroundSoil soil, int sector, int layer)
        => Math.Max(0.0,
            soil.MaterialAccumulatedAt(sector, layer) - _materialDrawn.GetValueOrDefault(KeyOf(sector, layer)));

    /// <summary>
    /// 地面那一档从某一格搬走 <paramref name="amount"/> 件矿石：<b>「累计 − 已运走」不足一整件就一件不给</b>；
    /// 给成即把这笔记进「已运走」—— 电梯下一轮不会再运同一件（两条路共用这一本账，不重不漏）。
    /// </summary>
    /// <returns>这一格这一轮搬走的件数（0 = 没有可搬的整件）。</returns>
    public double TakeOreOffCell(UndergroundSoil soil, int sector, int layer, double amount)
        => TakeOffCell(soil, sector, layer, amount, _oreDrawn, OreLeftAt);

    /// <summary>地面那一档从某一格搬走特殊材料（口径与 <see cref="TakeOreOffCell"/> 同一条）。</summary>
    public double TakeMaterialOffCell(UndergroundSoil soil, int sector, int layer, double amount)
        => TakeOffCell(soil, sector, layer, amount, _materialDrawn, MaterialLeftAt);

    /// <summary>全部挖开格上还欠着的矿石整件数（电梯与地面两路都没搬走的那部分）。</summary>
    public double OreBacklog(UndergroundSoil soil) => Backlog(soil, OreLeftAt);

    /// <summary>全部挖开格上还欠着的特殊材料整件数。</summary>
    public double MaterialBacklog(UndergroundSoil soil) => Backlog(soil, MaterialLeftAt);

    private static double TakeOffCell(
        UndergroundSoil soil,
        int sector,
        int layer,
        double amount,
        Dictionary<int, double> drawn,
        Func<UndergroundSoil, int, int, double> leftAt)
    {
        if (amount <= 0.0) return 0.0;

        var key = KeyOf(sector, layer);
        if (Math.Floor(leftAt(soil, sector, layer)) < amount) return 0.0;

        drawn[key] = drawn.GetValueOrDefault(key) + amount;
        return amount;
    }

    private static double Backlog(
        UndergroundSoil soil, Func<UndergroundSoil, int, int, double> leftAt)
    {
        var total = 0.0;
        foreach (var (sector, layer) in CellOrder())
        {
            if (!soil.IsDug(sector, layer)) continue;
            total += Math.Floor(leftAt(soil, sector, layer));
        }

        return total;
    }

    /// <summary>8 格的固定次序（象限 0..3 再层 1..2）—— 与快照那一段同序。</summary>
    public static IEnumerable<(int Sector, int Layer)> CellOrder()
    {
        for (var sector = 0; sector < UndergroundSoil.Sectors; sector++)
        {
            for (var layer = 1; layer <= UndergroundSoil.Layers; layer++)
            {
                yield return (sector, layer);
            }
        }
    }

    /// <summary>状态可查那一口：井口队列人数、结算轮数、上一轮的名额派法。</summary>
    public string DebugText() => string.Format(CultureInfo.InvariantCulture,
        "ELEVATOR 井口={0} 结算={1} 上轮={2}",
        _shaftQueue.Count, _settlements,
        _lastSettlement.Count == 0
            ? "-"
            : string.Join(",", _lastSettlement.Select(s => $"{s.Slot}:{s.Kind}")));
}
