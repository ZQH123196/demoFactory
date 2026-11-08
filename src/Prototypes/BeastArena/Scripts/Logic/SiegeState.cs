#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 攻破与解救那一件：攻破步号、待记的债、战损与被俘名单、解救。
//
// **为什么单独成件**：防守那一线要改攻破的节奏，经济那一线要改折债与记账，
// 人口那一线要改被俘名单 —— 原来三摊都挤在 ArenaWorld 的同一段字段里。
// 这一件只管**状态机与两条算式**（该记多少债、该记给谁）；
// 破坏性后果（毁建筑、掳人、扣货、收场、写负债账、改镇政府耐久、让巨兽返航）
// 留在世界里施加 —— 那些动作要碰到建造册、人口册、账本与航行状态，不是这一件的状态。
//
// **ticket 06 起不再有停摆期**：城破当格就由城卫队解救并记债，
// `StandstillUntilTick` 只为旧读数与旧档保留，运行期恒为 0。

/// <summary>
/// 攻破 / 解救的状态机。
///
/// <para><b>能脱离世界单独构造与推进</b>：不持有 <see cref="ArenaWorld"/>，也不读墙钟。
/// 调用方喂 tick（<see cref="BeginBreach"/>），本件只答「现在攻破没有、该记多少债、该记给谁」。</para>
///
/// <para><b>战损与被俘名单只做读数</b>：名单是世界里挑出来的结果，本件存着备查；
/// 毁建筑与掳人那两步由世界按同一份名单施加。</para>
/// </summary>
public sealed partial class SiegeState
{
    private readonly List<string> _warLossBuildings = [];
    private readonly List<string> _takenPeople = [];

    /// <summary>镇政府是不是已被攻破。</summary>
    public bool Breached { get; private set; }

    /// <summary>攻破那一刻的步号（没攻破过 = -1）。</summary>
    public long BreachTick { get; private set; } = -1;

    /// <summary>攻破折出来的债额（还没记进负债账，等城卫队解救那一刻记）。</summary>
    public double PendingDebt { get; private set; }

    /// <summary>
    /// 旧停摆期到哪一步。<b>ticket 06 起恒为 0</b>（城破当格解救，不再走三个游戏日停摆）；
    /// 留着这一格是为了旧快照与旧档的读法还能一眼看出「没有停摆」。
    /// </summary>
    public long StandstillUntilTick { get; private set; }

    /// <summary>攻破时按「从高到低、同价按 id 字典序」毁掉的那几栋（读数口）。</summary>
    public IReadOnlyList<string> WarLossBuildings => _warLossBuildings;

    /// <summary>攻破时被掳走的那几个 id（读数口）。</summary>
    public IReadOnlyList<string> TakenPeople => _takenPeople;

    /// <summary>解救要记进哪一家势力的负债账（没欠就空串）。</summary>
    public string RescueFactionId { get; private set; } = string.Empty;

    /// <summary>现在是不是在停摆期（ticket 06 起恒为 false）。</summary>
    public bool InStandstillAt(long tick) => StandstillUntilTick > tick;

    /// <summary>
    /// 停摆到期了没有 —— 城卫队到场的那一格。<b>只答该不该救，不动状态</b>：
    /// 解救要改镇政府耐久、记债、恢复航行，那些是世界那一侧的事。
    /// <b>ticket 06 起城破当格即解救</b>，所以攻破之后读到 true 的那一刻就调 <see cref="Rescue"/>。
    /// </summary>
    public bool RescueDueAt(long tick) => Breached && tick >= StandstillUntilTick;

    /// <summary>
    /// 攻破那一格：落步号、待记的债与两份名单。
    ///
    /// <para><b>幂等</b>：已经攻破过就整发不受理，第一次的读数一格不动
    /// （世界里那一格每游戏分钟查一次，不能每次重折债、重挑名单）。</para>
    ///
    /// <para><b>没有停摆期</b>：ticket 06 起城破当格由城卫队解救，
    /// 所以这一格不再把到期步号排到三个游戏日之后。</para>
    /// </summary>
    /// <param name="tick">攻破那一刻的步号。</param>
    /// <param name="rescueFactionId">该记给哪一家势力（由 <see cref="NearestFaction"/> 算）。</param>
    /// <param name="estimatedLoss">战损估值（金币）—— 本件按 <see cref="DebtFrom"/> 折成待记的债。</param>
    /// <param name="warLossBuildings">毁掉的那几栋（按毁掉的次序）。</param>
    /// <param name="takenPeople">掳走的那几个人（按名单序号）。</param>
    /// <returns>这一发受理返回 true；已经攻破过返回 false。</returns>
    public bool BeginBreach(
        long tick,
        string rescueFactionId,
        double estimatedLoss,
        IReadOnlyList<string> warLossBuildings,
        IReadOnlyList<string> takenPeople)
    {
        ArgumentNullException.ThrowIfNull(rescueFactionId);
        ArgumentNullException.ThrowIfNull(warLossBuildings);
        ArgumentNullException.ThrowIfNull(takenPeople);
        if (Breached) return false;

        Breached = true;
        BreachTick = tick;
        StandstillUntilTick = 0;
        PendingDebt = DebtFrom(estimatedLoss);
        RescueFactionId = rescueFactionId;

        _warLossBuildings.Clear();
        _warLossBuildings.AddRange(warLossBuildings);
        _takenPeople.Clear();
        _takenPeople.AddRange(takenPeople);
        return true;
    }

    /// <summary>
    /// 解救：解除攻破与停摆，把待记的债与势力交还出去。
    ///
    /// <para><b>债不在这里记</b>：负债账在世界那一侧（<c>TownAccounts</c>），
    /// 这一件只把「记多少、记给谁」原样交回。名单留着备查（攻破发生了什么不随解救消失）。</para>
    /// </summary>
    /// <returns>待记的债额与势力（没欠时是 0 与空串）。</returns>
    public (double Debt, string FactionId) Rescue()
    {
        var debt = PendingDebt;
        var faction = RescueFactionId;

        Breached = false;
        StandstillUntilTick = 0;
        PendingDebt = 0.0;

        return (debt, faction);
    }

    /// <summary>
    /// 战损估值折债额：<c>max(下界 500, round(估值 × 1.5))</c>。
    /// 折算只有这一处 —— 世界里那一格与测试读的是同一条式子。
    /// </summary>
    public static double DebtFrom(double estimatedLoss)
        => Math.Max(ArenaTuning.DebtFloor, Math.Round(estimatedLoss * ArenaTuning.DebtFactor));

    /// <summary>
    /// 离兽最近的那一家城镇势力（没有候选回空串）。
    ///
    /// <para><b>量到停靠点而不是城门</b>：兽停在城外那一点（<see cref="MapPlace.StopPoint(BeastTier)"/>，
    /// 调用方按巨兽当前位阶递进来），
    /// 城门在它后面 26.45 米 —— 拿城门去量，量的是「兽到城门口」那一段，
    /// 不是兽真正站的地方。</para>
    ///
    /// <para><b>同距取先登记的那一家</b>：判据是严格小于，候选次序即登记次序。</para>
    /// </summary>
    public static string NearestFaction(
        NavPoint at, IEnumerable<(string FactionId, NavPoint StopPoint)> towns)
    {
        ArgumentNullException.ThrowIfNull(towns);

        var best = string.Empty;
        var bestDistance = double.MaxValue;
        foreach (var (factionId, stopPoint) in towns)
        {
            var distance = at.DistanceTo(stopPoint);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = factionId;
        }

        return best;
    }
}
