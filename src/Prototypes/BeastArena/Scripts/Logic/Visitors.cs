#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 游客那一摊：池子、上甲板、消费与看赛、轮结好评差评、吓跑按名单序号取人。
//
// 三条口径：
//   · **池上限按城市档**（贫城 80、富城 150），每游戏日回速 25，**换城即回满**。
//   · **每客预算 15 金币**：花光或停留满 2..4 游戏小时即离镇。停留时长由
//     （种子, 名单序号）派生，不掷点（同种子必然同一个数，重放逐字可复现）。
//   · **好评差评只看在镇游客的平均情绪**：≥ 0 好评给镇名声 +2，< 0 差评 −2。
//     游客情绪那一列在进入时是 0，消费完成 +1、看赛完成 +2、付不起时 −5。

/// <summary>游客在镇上干什么。</summary>
public enum VisitorActivity
{
    /// <summary>刚上甲板，正在走向下一个去处。</summary>
    Walking = 0,

    /// <summary>在饭店或酒店入座消费。</summary>
    Eating = 1,

    /// <summary>在竞技场看赛。</summary>
    Watching = 2,

    /// <summary>要走了，正在往浮桥走。</summary>
    Leaving = 3,
}

/// <summary>一名游客的账（预算、停留、活动与到期步号）。</summary>
public sealed partial class VisitorRecord
{
    /// <summary>建一条游客账。</summary>
    public VisitorRecord(ArenaUnit unit, double budget, int stayGameMinutes, long enteredTick)
    {
        Unit = unit;
        Budget = budget;
        StayGameMinutes = stayGameMinutes;
        EnteredTick = enteredTick;
        Activity = VisitorActivity.Walking;
    }

    /// <summary>对应的活物。</summary>
    public ArenaUnit Unit { get; }

    /// <summary>剩余预算（金币）。</summary>
    public double Budget { get; private set; }

    /// <summary>这一趟总共待几个游戏分钟（2..4 游戏小时之间派生）。</summary>
    public int StayGameMinutes { get; }

    /// <summary>上甲板那一刻的步号。</summary>
    public long EnteredTick { get; }

    /// <summary>当前在干什么。</summary>
    public VisitorActivity Activity { get; set; }

    /// <summary>当前去处的建筑 id（空串 = 在路上）。</summary>
    public string TargetBuildingId { get; set; } = string.Empty;

    /// <summary>这一段活动的到期步号（0 = 不限时）。</summary>
    public long BusyUntilTick { get; set; }

    /// <summary>预定的离镇步号（预算与时长两个条件里先到的那一个）。</summary>
    public long LeaveTick { get; set; }

    /// <summary>花掉一筆（不够就返回 false，一子儿不动）。</summary>
    public bool TrySpend(double gold)
    {
        if (gold <= 0.0) return true;
        if (Budget < gold) return false;

        Budget -= gold;
        return true;
    }

    /// <summary>情绪那一列的增减。</summary>
    public void AddMood(double delta)
        => Unit.Mood = Math.Clamp(Unit.Mood + delta, ArenaTuning.MoodMin, ArenaTuning.MoodMax);
}

/// <summary>
/// 游客池 + 在镇游客的行为机。
///
/// <para><b>池与场上两本数</b>：池子记「还能来多少人」（上限按城市档、回速按游戏日），
/// 场上记「此刻在甲板上有多少人」（进场上限由 <see cref="ArenaTuning.VisitorEatGameMinutes"/> 那档节奏限着）。
/// 快照头部印的是<b>场上 / 池上限</b> —— HUD 要看的是现在街上几个人。</para>
/// </summary>
public sealed partial class VisitorPool
{
    private readonly PeopleRoster _roster;
    private readonly List<VisitorRecord> _inTown = [];
    private readonly int _seed;
    private int _poolAvailable;
    private int _cap;
    private int _nextVisitorNumber;
    private long _lastMinute;
    private double _admitCarry;
    private double _townReputation = ArenaTuning.StartingTownReputation;
    private double _priceFactor = 1.0;
    private string _arenaTarget = "arena";
    private double _townHotelMultiplier = 1.0;
    private double _townRestaurantMultiplier = 1.0;
    private double _townTicketMultiplier = 1.0;

    /// <summary>接活物册（发游客与结算情绪）与逻辑种子。</summary>
    public VisitorPool(PeopleRoster roster, int seed)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _roster = roster;
        _seed = seed;
    }

    /// <summary>把本镇的定价倍率、当地物价系数与竞技场那一栋的 id 递进来（消费那一式乘它们）。</summary>
    public void SetPricing(double hotel, double restaurant, double ticket, double cityPriceFactor, string arenaId)
    {
        _townHotelMultiplier = hotel;
        _townRestaurantMultiplier = restaurant;
        _townTicketMultiplier = ticket;
        _priceFactor = cityPriceFactor;
        if (arenaId.Length > 0) _arenaTarget = arenaId;
    }

    /// <summary>池上限（按当前所在城市档）。</summary>
    public int Cap => _cap;

    /// <summary>池里还能来多少人（换城即回满）。</summary>
    public int PoolAvailable => _poolAvailable;

    /// <summary>此刻在甲板上的游客数。</summary>
    public int InTownCount => _inTown.Count;

    /// <summary>在镇游客（按名单序号）。</summary>
    public IReadOnlyList<VisitorRecord> InTown => _inTown;

    /// <summary>在镇游客的平均情绪（好评差评那一式读它）。</summary>
    public double AverageMood
    {
        get
        {
            if (_inTown.Count == 0) return 0.0;

            var sum = 0.0;
            foreach (var v in _inTown) sum += v.Unit.Mood;
            return sum / _inTown.Count;
        }
    }

    /// <summary>镇名声那一列（游客池上限与办赛收益都乘它）。</summary>
    public double TownReputation => _townReputation;

    /// <summary>换城：池子回满、场上清空（拔锚离城时清空在镇游客那一格走 <see cref="ClearTown"/>）。</summary>
    public void RefillFor(MapPlace place)
    {
        ArgumentNullException.ThrowIfNull(place);

        _cap = place.VisitorPoolCap;
        _poolAvailable = _cap;
    }

    /// <summary>每游戏日回速（<see cref="ArenaTuning.VisitorRegenPerGameDay"/>，不超过上限）。</summary>
    public void RegenDay() => _poolAvailable = Math.Min(_cap, _poolAvailable + ArenaTuning.VisitorRegenPerGameDay);

    /// <summary>拔锚离城：清空在镇游客（池子留到换城再回满）。</summary>
    public void ClearTown()
    {
        foreach (var v in _inTown) _roster.LeaveTown(v.Unit);
        _inTown.Clear();
    }

    /// <summary>办赛收益乘 <c>1 + 镇名声 ÷ 500</c>。</summary>
    public double TicketIncomeFactor => 1.0 + (_townReputation / ArenaTuning.TicketReputationDivisor);

    /// <summary>给镇名声加一档（好评 +2 / 差评 −2）。</summary>
    public void AddTownReputation(double delta)
        => _townReputation = Math.Clamp(
            _townReputation + delta, ArenaTuning.ReputationMin, ArenaTuning.ReputationMax);

    /// <summary>
    /// 每满 1 个游戏分钟推进一次：放新客上线、结算进行中的活动、预算花光或停留满的离镇。
    /// </summary>
    /// <param name="tick">当前步号。</param>
    /// <param name="docked">是否停靠（不在停靠期镇上没有游客，池子也不放人）。</param>
    /// <param name="matchOn">竞技场这一场有没有观众（有观众时游客去看赛）。</param>
    /// <param name="eatTarget">饭店那一栋的 id。</param>
    /// <param name="hotelTarget">酒店那一栋的 id。</param>
    /// <param name="arenaTarget">竞技场那一栋的 id。</param>
    /// <param name="doorOf">取某一栋的门口点。</param>
    /// <param name="onSpend">游客花掉的钱进镇（<c>金币</c>）。</param>
    public void StepMinute(
        long tick,
        bool docked,
        bool matchOn,
        string eatTarget,
        string hotelTarget,
        string arenaTarget,
        Func<string, NavPoint> doorOf,
        Action<double> onSpend)
    {
        ArgumentNullException.ThrowIfNull(doorOf);
        ArgumentNullException.ThrowIfNull(onSpend);

        var minute = tick / ArenaTime.StepsPerGameMinute;
        if (minute == _lastMinute) return;
        _lastMinute = minute;

        // 竞技场可能被拆掉：这一格跟着递进来的现查结果走（空串 = 不在册），
        // 不再留着上一帧那个固定 id。
        _arenaTarget = arenaTarget;

        if (!docked)
        {
            if (_inTown.Count > 0) ClearTown();
            return;
        }

        AdvanceActivities(tick, matchOn, eatTarget, hotelTarget, arenaTarget, doorOf, onSpend);
        RetireDeparted(tick);
        AdmitVisitors(tick, eatTarget, hotelTarget, arenaTarget, doorOf);
    }

    /// <summary>轮结评价：在镇游客平均情绪 ≥ 0 记好评、&lt; 0 记差评。回 true = 好评。</summary>
    public bool ReviewAtPullAnchor()
    {
        if (_inTown.Count == 0)
        {
            AddTownReputation(-ArenaTuning.ReviewReputationDelta);
            return false;
        }

        var good = AverageMood >= 0.0;
        AddTownReputation(good ? ArenaTuning.ReviewReputationDelta : -ArenaTuning.ReviewReputationDelta);
        return good;
    }

    /// <summary>
    /// 吓跑：<b>按名单序号取人，不掷点</b>。每 <paramref name="per"/> 个人取
    /// <paramref name="take"/> 个，取不满一批时按同一比例从头取。
    /// </summary>
    public int Frighten(int take, int per)
    {
        if (take <= 0 || per <= 0 || _inTown.Count == 0) return 0;

        var wanted = (int)Math.Floor((double)_inTown.Count * take / per);
        if (wanted <= 0) return 0;

        // 按名单序号取：先按序号排，再从头数 wanted 个。
        var ordered = new List<VisitorRecord>(_inTown);
        ordered.Sort((a, b) => a.Unit.RosterIndex.CompareTo(b.Unit.RosterIndex));

        var gone = 0;
        for (var i = 0; i < wanted && i < ordered.Count; i++)
        {
            ordered[i].Activity = VisitorActivity.Leaving;
            _roster.LeaveTown(ordered[i].Unit);
            gone++;
        }

        return gone;
    }

    /// <summary>状态可查那一口：池子、在场、平均情绪、镇名声。</summary>
    public string DebugText() => string.Format(CultureInfo.InvariantCulture,
        "VISITORS 在镇={0} 池={1}/{2} 均价情={3:0.##} 镇名声={4:0.##}",
        _inTown.Count, _poolAvailable, _cap, AverageMood, _townReputation);

    // ---- 私产 ----

    private void AdmitVisitors(
        long tick, string eatTarget, string hotelTarget, string arenaTarget, Func<string, NavPoint> doorOf)
    {
        // 放客是一个「每游戏分钟 0.5 个」的小数配额：余额累进来，整数那一支才放人，
        // 余数留在余额里到下一个游戏分钟。不掷点、只按游戏分钟推进 —— 重放逐字可复现靠的就是这一条。
        _admitCarry += ArenaTuning.VisitorEnterPerGameMinute;
        var admit = (int)Math.Floor(_admitCarry);
        if (admit <= 0) return;
        _admitCarry -= admit;

        for (var n = 0; n < admit; n++)
        {
            if (_poolAvailable <= 0 || _inTown.Count >= _cap) return;

            var id = $"v{++_nextVisitorNumber}";
            var unit = _roster.Add(id, UnitRole.Visitor, DeckGeometry.Pontoon());
            _poolAvailable--;

            var stayMinutes = StayMinutesFor(_seed, unit.RosterIndex);
            var record = new VisitorRecord(unit, ArenaTuning.VisitorBudgetGold, stayMinutes, tick)
            {
                LeaveTick = tick + (ArenaTime.StepsPerGameMinute * stayMinutes),
            };

            _inTown.Add(record);
            ChooseNext(record, tick, eatTarget, hotelTarget, arenaTarget, doorOf);
        }
    }

    /// <summary>
    /// 这一趟待多久（游戏分钟）：<b>由（种子, 名单序号）派生</b>，落在
    /// 2..4 游戏小时之间含两端。不掷点。
    /// </summary>
    public static int StayMinutesFor(int seed, int rosterIndex)
    {
        var hours = ArenaTuning.VisitorStayMinGameHours
            + (int)(PeopleRoster.DailyDraw(seed ^ 0x5F3A, rosterIndex, 0)
                * (ArenaTuning.VisitorStayMaxGameHours - ArenaTuning.VisitorStayMinGameHours + 1));
        hours = Math.Clamp(hours, ArenaTuning.VisitorStayMinGameHours, ArenaTuning.VisitorStayMaxGameHours);
        return hours * ArenaTime.GameMinutesPerDay / 24;
    }

    private void ChooseNext(
        VisitorRecord record, long tick, string eatTarget, string hotelTarget, string arenaTarget,
        Func<string, NavPoint> doorOf)
    {
        var draw = PeopleRoster.DailyDraw(_seed, record.Unit.RosterIndex, ArenaTime.GameDaysAt(tick));
        var target = draw switch
        {
            < 0.4 => eatTarget,
            < 0.8 => hotelTarget,
            _ => arenaTarget,
        };

        // 抽中的那一栋不在册（开局没有酒店、或刚被拆掉）：回退到还在的那几栋。
        // 顺次是饭店 → 酒店 → 竞技场；三栋都不在就留在原地（不指向 (0,0)）。
        if (target.Length == 0)
        {
            target = eatTarget.Length > 0
                ? eatTarget
                : hotelTarget.Length > 0 ? hotelTarget : arenaTarget;
        }

        if (target.Length == 0) return;

        record.TargetBuildingId = target;
        record.Activity = VisitorActivity.Walking;
        record.BusyUntilTick = 0;
        Issue(record, doorOf(target));
    }

    /// <summary>这一条游客账此刻指的那个建筑还在不在「当前三个去处」里（空串一律算不在）。</summary>
    private static bool IsCurrentTarget(string target, string eat, string hotel, string arena)
        => target.Length > 0 && (target == eat || target == hotel || target == arena);

    private void AdvanceActivities(
        long tick, bool matchOn, string eatTarget, string hotelTarget, string arenaTarget,
        Func<string, NavPoint> doorOf, Action<double> onSpend)
    {
        // ① 在路上的：到了门口就入座定身。
        for (var i = _inTown.Count - 1; i >= 0; i--)
        {
            var v = _inTown[i];
            if (v.Activity != VisitorActivity.Walking) continue;
            if (v.TargetBuildingId.Length == 0) continue;

            // 目的地不在册了（那一栋被拆掉 / 换了一栋）：当场重选，不朝 (0,0) 走。
            if (!IsCurrentTarget(v.TargetBuildingId, eatTarget, hotelTarget, arenaTarget))
            {
                ChooseNext(v, tick, eatTarget, hotelTarget, arenaTarget, doorOf);
                continue;
            }

            // 两边都得是甲板逻辑坐标：`doorOf` 给的门口点就是甲板坐标，
            // `DeckX` / `DeckZ` 也是甲板坐标（寻路坐标只在 `Issue` / `Hold` 发令那一刻换算）。
            // 拿寻路坐标去比甲板坐标，两轴各差 12 米，`Reached` 永远为假 ——
            // 症状是游客永远在路上走，一单也不消费，镇上的金币账一格不动。
            var door = doorOf(v.TargetBuildingId);
            var at = new NavPoint(v.Unit.DeckX, v.Unit.DeckZ);
            if (!NavArrive.Reached(at, door, ArenaTuning.VisitorArriveToleranceMeters)) continue;

            var isArena = v.TargetBuildingId == arenaTarget;
            if (isArena && !matchOn)
            {
                ChooseNext(v, tick, eatTarget, hotelTarget, arenaTarget, doorOf);
                continue;
            }

            // 入店 / 入座：定身并关闭避让（与镇民入槽同款那一口）。
            Hold(v, door);
            v.Activity = isArena ? VisitorActivity.Watching : VisitorActivity.Eating;
            v.BusyUntilTick = tick + (ArenaTime.StepsPerGameMinute
                * (isArena ? ArenaTuning.VisitorWatchGameMinutes : ArenaTuning.VisitorEatGameMinutes));
        }

        // ② 消费与看赛到点的：结算那一笔，然后挑下一个去处。
        for (var i = _inTown.Count - 1; i >= 0; i--)
        {
            var v = _inTown[i];
            if (v.Activity == VisitorActivity.Eating && tick >= v.BusyUntilTick)
            {
                CompleteSpend(v, isHotel: v.TargetBuildingId == hotelTarget, onSpend, moodGain: 1.0);
                ChooseNext(v, tick, eatTarget, hotelTarget, arenaTarget, doorOf);
                continue;
            }

            if (v.Activity == VisitorActivity.Watching && tick >= v.BusyUntilTick)
            {
                CompleteSpend(v, isHotel: false, onSpend, moodGain: 2.0);
                ChooseNext(v, tick, eatTarget, hotelTarget, arenaTarget, doorOf);
            }
        }
    }

    /// <summary>
    /// 结算一次消费：基础消费 × 本镇定价倍率 × 当地物价系数。
    /// <b>付得起才进镇账、情绪才 +1 / +2；付不起则情绪 −5（这一格就是差评的来源）。</b>
    /// </summary>
    private void CompleteSpend(VisitorRecord v, bool isHotel, Action<double> onSpend, double moodGain)
    {
        double basePrice;
        double multiplier;
        if (isHotel)
        {
            basePrice = ArenaTuning.HotelBaseSpend;
            multiplier = _townHotelMultiplier;
        }
        else if (v.TargetBuildingId == _arenaTarget)
        {
            basePrice = ArenaTuning.RestaurantBaseSpend * TicketIncomeFactor;
            multiplier = _townTicketMultiplier;
        }
        else
        {
            basePrice = ArenaTuning.RestaurantBaseSpend;
            multiplier = _townRestaurantMultiplier;
        }

        var gold = basePrice * multiplier * _priceFactor;
        if (gold > 0.0 && !v.TrySpend(gold))
        {
            v.AddMood(-5.0);
            return;
        }

        onSpend(gold);
        v.AddMood(moodGain);
    }

    private void RetireDeparted(long tick)
    {
        for (var i = _inTown.Count - 1; i >= 0; i--)
        {
            var v = _inTown[i];
            if (tick < v.LeaveTick && v.Budget > 0.0) continue;

            _roster.LeaveTown(v.Unit);
            _inTown.RemoveAt(i);
        }
    }

    private void Issue(VisitorRecord v, NavPoint doorDeck)
    {
        var stepper = _roster.DeckStepper;
        stepper.SetCountedInSeparation(v.Unit.Id, true);
        stepper.Issue(v.Unit.Id, NavCommand.GoTo(DeckGeometry.Nav(doorDeck.X, doorDeck.Z),
            ArenaTuning.VisitorArriveToleranceMeters));
    }

    private void Hold(VisitorRecord v, NavPoint doorDeck)
    {
        var stepper = _roster.DeckStepper;
        stepper.SetCountedInSeparation(v.Unit.Id, false);
        stepper.Issue(v.Unit.Id, NavCommand.Hold(DeckGeometry.Nav(doorDeck.X, doorDeck.Z)));
    }
}
