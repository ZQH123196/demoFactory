#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 账本那一半：十四项封闭拒绝原因、七本实物账（含魔晶）+ 一本按债主分户的负债账 + 一本法力格账，
// 加上定价倍率与还款策略。九条守恒等式只有 <see cref="TownAccounts.IsConserved"/> 一处判。

/// <summary>
/// 拒绝原因（14 项封闭枚举）。<b>动词返回的是其中一项，不抛</b>：
/// <c>rejected: &lt;RejectReason&gt;</c> 是注入口约定的形状。
/// </summary>
public enum RejectReason
{
    /// <summary>参数缺失、段数不对、或取值越界。</summary>
    InvalidArgs = 0,

    /// <summary>场合不对（不在停靠期开赛、窗口外成交、没在打仗时召唤……）。</summary>
    InvalidPhase = 1,

    /// <summary>金币不够。</summary>
    InsufficientCoin = 2,

    /// <summary>食物不够。</summary>
    InsufficientFood = 3,

    /// <summary>魂石不够。</summary>
    InsufficientSoulStone = 4,

    /// <summary>可用法力格不够。</summary>
    InsufficientMana = 5,

    /// <summary>矿石或特殊材料不够。</summary>
    InsufficientMaterial = 6,

    /// <summary>岗位 / 容量 / 招募上限已满。</summary>
    FullCapacity = 7,

    /// <summary>目标不存在，或不是这一类目标（抓游客、抓敌人、尸体丢进非教堂……）。</summary>
    InvalidTarget = 8,

    /// <summary>正被债主势力通缉，该城拒停。</summary>
    WantedRefused = 9,

    /// <summary>进阶门槛没到（任一象限还没挖到所需的那一层）。</summary>
    GateNotMet = 10,

    /// <summary>已经是最高位阶（三阶），不能再进阶。</summary>
    TierAtMax = 11,

    /// <summary>这个形态还没解锁（解锁条件是收集类的一次性上锁，不随计数清零而回收）。</summary>
    FormLocked = 12,

    /// <summary>没有可调遣的执行者（NPC 喂食派工：无岗的工人与帮工全部上岗、搬运、离镇、被抓或伤倒）。</summary>
    NoIdleWorker = 13,
}

/// <summary>还款策略：金币进账时自动还到哪一家。</summary>
public enum DebtPolicy
{
    /// <summary>不自动还（<b>开局默认</b>）。</summary>
    None = 0,

    /// <summary>按名单顺序自动还（哪一家先欠的先还哪一家）。</summary>
    AutoOrder = 1,

    /// <summary>优先自动还指定债主，还完再按名单顺序。</summary>
    AutoTarget = 2,
}

/// <summary>一家债主势力的独立负债账（按势力分户，不并进金币账）。</summary>
public sealed partial class CreditorAccount
{
    /// <summary>建一家债主势力的账。</summary>
    /// <param name="factionId">势力标识。</param>
    /// <param name="displayName">中文名，只给人看。</param>
    /// <param name="ownsTownIds">这家势力名下的城镇 id（Dock 对这些城回 <see cref="RejectReason.WantedRefused"/>）。</param>
    public CreditorAccount(string factionId, string displayName, IReadOnlyList<string> ownsTownIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factionId);
        ArgumentNullException.ThrowIfNull(ownsTownIds);

        FactionId = factionId;
        DisplayName = displayName;
        TownIds = ownsTownIds;
    }

    /// <summary>势力标识。</summary>
    public string FactionId { get; }

    /// <summary>中文名，只给人看。</summary>
    public string DisplayName { get; }

    /// <summary>这家势力名下的城镇 id。</summary>
    public IReadOnlyList<string> TownIds { get; }

    /// <summary>累计欠账（金币）。</summary>
    public double Accrued { get; private set; }

    /// <summary>累计已还（金币）。</summary>
    public double Repaid { get; private set; }

    /// <summary>还剩多少。</summary>
    public double Outstanding => Accrued - Repaid;

    /// <summary>记一笔新欠（不发金币）。</summary>
    public void Accrue(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "欠账不能是负数");
        Accrued += amount;
    }

    /// <summary>用金币还账，还到还清为止；回真的还掉多少。</summary>
    /// <param name="coin">可动用的金币。</param>
    public double Repay(double coin)
    {
        if (coin <= 0.0 || Outstanding <= 0.0) return 0.0;

        var paid = Math.Min(coin, Outstanding);
        Repaid += paid;
        return paid;
    }
}

/// <summary>
/// 镇上的账 —— 七本实物账（含魔晶）+ 一本按债主分户的负债账 + 一本法力格账，外加定价倍率与还款策略。
///
/// <para><b>九条守恒等式只有 <see cref="IsConserved"/> 一处判</b>（L1 不变量的唯一出口）：
/// 结余 = 开局 + 产出 − 消耗。<b>「产出」那一列从 0 起</b>（开局那一笔是等式左边那个常数项，
/// 记进产出就会把等式读成两倍）。花钱走 <c>TrySpend*</c>（付不起一子儿不动），
/// 退款走 <c>Refund*</c>（<b>退回记进产出、不记消耗</b>）——
/// 于是「先扣钱后落库失败」那一格原样退回，等式仍然成立。</para>
///
/// <para><b>法力格那一本两类消耗分账</b>：<c>LiftBuilding</c> / <c>SummonHelper</c>
/// 是<b>占位</b>消耗（占住不放，解除才归还），<c>CastHeal</c> 是<b>即时</b>消耗（花掉即扣，
/// 那笔永久记在 <c>InstantSpent</c> 里当它被时间充回来的证据）。
/// 固化那一档（<c>ToggleSolidify</c>）随「嵌入取代固化」整件删掉，法力账里不再有第三类占位。
/// 每游戏小时恢复 1 格，读的是「<b>可用格 + 占位格 &lt; 总格数</b>」——
/// 占着的那几格不恢复，只涨真正空着的那几格。</para>
/// </summary>
public sealed partial class TownAccounts
{
    private readonly List<CreditorAccount> _creditors = [];

    /// <summary>按开局档建一本账。</summary>
    public TownAccounts()
    {
        Policy = DebtPolicy.None;
    }

    // ---- 金币 ----

    /// <summary>金币结余。</summary>
    public double Coin { get; private set; } = ArenaTuning.OpeningCoin;

    /// <summary>累计产出金币。</summary>
    public double ProducedCoin { get; private set; }

    /// <summary>累计消耗金币。</summary>
    public double SpentCoin { get; private set; }

    // ---- 食物 ----

    public double Food { get; private set; } = ArenaTuning.OpeningFood;

    public double ProducedFood { get; private set; }

    public double SpentFood { get; private set; }

    // ---- 魂石 ----

    public double SoulStone { get; private set; } = ArenaTuning.StartingSoulStones;

    public double ProducedSoulStone { get; private set; }

    public double SpentSoulStone { get; private set; }

    // ---- 矿石 ----

    public double Ore { get; private set; } = ArenaTuning.OpeningOre;

    public double ProducedOre { get; private set; }

    public double SpentOre { get; private set; }

    // ---- 特殊材料 ----

    public double SpecialMaterial { get; private set; } = ArenaTuning.OpeningSpecialMaterial;

    public double ProducedSpecialMaterial { get; private set; }

    public double SpentSpecialMaterial { get; private set; }

    // ---- 备件 ----

    public double SparePart { get; private set; } = ArenaTuning.StartingSpareParts;

    public double ProducedSparePart { get; private set; }

    public double SpentSparePart { get; private set; }

    // ---- 魔晶 ----
    //
    // 巨兽消化产出的珍稀货物：**开局 0**，只有生态产出这一条进账。
    // 与食物分开记 —— 魔晶是货物，不是食物。

    /// <summary>仓库里的魔晶（单位）。</summary>
    public double Crystal { get; private set; }

    /// <summary>累计产出魔晶。</summary>
    public double ProducedCrystal { get; private set; }

    /// <summary>累计消耗魔晶（用途与出售）。</summary>
    public double SpentCrystal { get; private set; }

    /// <summary>累计用掉的魔晶单位（补魔 / 治疗 / 修理 / 嵌入，结算的「使用」那一栏）。</summary>
    public int CrystalUsed { get; private set; }

    /// <summary>累计卖掉的魔晶单位（结算的「出售」那一栏）。</summary>
    public int CrystalSold { get; private set; }

    /// <summary>累计门票收入（金币）：办赛收场那一格的入账，结算单列。</summary>
    public double TicketIncome { get; private set; }

    /// <summary>记一笔门票收入（办赛收场那一格调）。</summary>
    public void NoteTicketIncome(double amount)
    {
        if (amount <= 0.0) return;
        TicketIncome += amount;
    }

    /// <summary>记一笔魔晶用途消耗（补魔 / 治疗 / 修理 / 嵌入）。</summary>
    public void NoteCrystalUsed(int count = 1)
    {
        if (count <= 0) return;
        CrystalUsed += count;
    }

    /// <summary>记一笔魔晶出售。</summary>
    public void NoteCrystalSold(int count)
    {
        if (count <= 0) return;
        CrystalSold += count;
    }

    // ---- 法力格 ----

    /// <summary>可用法力格（整数格，不放半个）。</summary>
    public int AvailableMana { get; private set; } = ArenaTuning.ManaCapacity;

    /// <summary>法术停滞（拔起建筑）占住几格。</summary>
    public int StasisMana { get; private set; }

    /// <summary>召唤帮工占住几格。</summary>
    public int HelperMana { get; private set; }

    /// <summary>即时消耗累计（只增；<c>CastHeal</c> 那一档）。</summary>
    public int InstantSpentMana { get; private set; }

    /// <summary>魔晶补魔回填的法力（只增；守恒等式里当「产出」那一侧）。</summary>
    public int CrystalManaRestored { get; private set; }

    /// <summary>自然恢复累计（只增；「可用格 + 占位格 &lt; 总格数」那一支才涨）。</summary>
    public int RegainedMana { get; private set; }

    /// <summary>法力总格数。</summary>
    public const int ManaCapacity = ArenaTuning.ManaCapacity;

    /// <summary>两类占位消耗的档位。</summary>
    public enum ManaHold
    {
        /// <summary>法术停滞（<c>LiftBuilding</c>）。</summary>
        Stasis = 0,

        /// <summary>召唤帮工（<c>SummonHelper</c>）。</summary>
        Helper = 1,
    }

    /// <summary>两类占位各占几格。</summary>
    public int HeldMana(ManaHold hold) => hold switch
    {
        ManaHold.Stasis => StasisMana,
        _ => HelperMana,
    };

    /// <summary>两类占位加起来占了几格。</summary>
    public int OccupiedMana => StasisMana + HelperMana;

    /// <summary>占住 1 格（不够就一子儿不动）。</summary>
    public bool TryHoldMana(ManaHold hold)
    {
        if (AvailableMana < 1) return false;

        AvailableMana--;
        Bump(hold, 1);
        return true;
    }

    /// <summary>归还 1 格。已归零的那一档不扣成负数。</summary>
    public void ReleaseMana(ManaHold hold)
    {
        if (HeldMana(hold) <= 0) return;

        Bump(hold, -1);
        AvailableMana++;
    }

    /// <summary>即时消耗 1 格（花掉即扣；不够就一子儿不动）。</summary>
    public bool TrySpendInstantMana()
    {
        if (AvailableMana < 1) return false;

        AvailableMana--;
        InstantSpentMana++;
        return true;
    }

    /// <summary>每游戏小时恢复 1 格。<b>只填真正空着的那几格</b>（占位格不恢复）。</summary>
    /// <returns>true = 这一格补上了；false = 池已满。</returns>
    public bool RegainManaOne()
    {
        if (AvailableMana + OccupiedMana >= ManaCapacity) return false;

        AvailableMana++;
        RegainedMana++;
        return true;
    }

    // ---- 负债（按债主分户） ----

    /// <summary>还款策略（开局默认 <see cref="DebtPolicy.None"/>）。</summary>
    public DebtPolicy Policy { get; private set; } = DebtPolicy.None;

    /// <summary><c>AutoTarget</c> 下优先还的那一家（空串 = 没指定）。</summary>
    public string PriorityFactionId { get; private set; } = string.Empty;

    /// <summary>按登记次序的债主账。</summary>
    public IReadOnlyList<CreditorAccount> Creditors => _creditors;

    /// <summary>累计欠账（按债主汇总）。</summary>
    public double AccruedDebt
    {
        get
        {
            var sum = 0.0;
            foreach (var c in _creditors) sum += c.Accrued;
            return sum;
        }
    }

    /// <summary>累计已还（按债主汇总）。</summary>
    public double RepaidDebt
    {
        get
        {
            var sum = 0.0;
            foreach (var c in _creditors) sum += c.Repaid;
            return sum;
        }
    }

    /// <summary>负债总额（Accrued − Repaid）。</summary>
    public double TotalDebt => AccruedDebt - RepaidDebt;

    /// <summary>登记一家债主势力（开局无债主；攻破折债与城卫队解救那一段才落一家）。</summary>
    public CreditorAccount AddCreditor(string factionId, string displayName, IReadOnlyList<string> ownsTownIds)
    {
        var existing = CreditorOf(factionId);
        if (existing is not null) return existing;

        var account = new CreditorAccount(factionId, displayName, ownsTownIds);
        _creditors.Add(account);
        return account;
    }

    /// <summary>按势力 id 查账（查不到回 null）。</summary>
    public CreditorAccount? CreditorOf(string factionId)
    {
        foreach (var c in _creditors)
        {
            if (c.FactionId == factionId) return c;
        }

        return null;
    }

    /// <summary>记一笔新欠到某一家（不发金币、不动金币账）。</summary>
    public void AccrueDebt(string factionId, double amount)
    {
        var account = CreditorOf(factionId)
            ?? AddCreditor(factionId, factionId, Array.Empty<string>());
        account.Accrue(amount);
    }

    /// <summary>手动还某一家：动金币账、减那一家欠账。回真的还掉多少。</summary>
    public double PayDebt(string factionId, double amount)
    {
        var account = CreditorOf(factionId);
        if (account is null || amount <= 0.0) return 0.0;
        if (!TrySpendCoin(amount)) return 0.0;

        var over = amount - account.Repay(amount);
        if (over > 0.0) RefundCoin(over);
        return amount - over;
    }

    /// <summary>设还款策略。<paramref name="priorityFactionId"/> 空串表示不指定。</summary>
    public void SetDebtPolicy(DebtPolicy policy, string priorityFactionId)
    {
        Policy = policy;
        PriorityFactionId = priorityFactionId ?? string.Empty;
    }

    /// <summary>进账之后按策略自动还债。回实际还掉的金额。</summary>
    public double SettleAutoRepayment(double income)
    {
        if (Policy == DebtPolicy.None || income <= 0.0) return 0.0;

        var budget = income;
        var paid = 0.0;
        if (Policy == DebtPolicy.AutoTarget && PriorityFactionId.Length > 0)
        {
            paid += RepayOne(PriorityFactionId, ref budget);
        }

        // 名单顺序：剩余的那一家一家还。
        foreach (var c in _creditors)
        {
            if (budget <= 0.0) break;
            paid += RepayOne(c.FactionId, ref budget);
        }

        return paid;
    }

    // ---- 定价倍率（SetPricing） ----

    /// <summary>酒店消费倍率（缺省 1.0）。</summary>
    public double HotelMultiplier { get; private set; } = 1.0;

    /// <summary>饭店消费倍率（缺省 1.0）。</summary>
    public double RestaurantMultiplier { get; private set; } = 1.0;

    /// <summary>门票倍率（缺省 1.0）。</summary>
    public double TicketMultiplier { get; private set; } = 1.0;

    /// <summary>设三个倍率。任一越界就一格不动、返回 false（动词那一侧回 <see cref="RejectReason.InvalidArgs"/>）。</summary>
    public bool TrySetPricing(double hotel, double restaurant, double ticket)
    {
        if (!InPricingRange(hotel) || !InPricingRange(restaurant) || !InPricingRange(ticket)) return false;

        HotelMultiplier = hotel;
        RestaurantMultiplier = restaurant;
        TicketMultiplier = ticket;
        return true;
    }

    /// <summary>倍率取不取在 <c>0.5..2</c> 之内。</summary>
    public static bool InPricingRange(double value)
        => value >= ArenaTuning.PricingMinMultiplier && value <= ArenaTuning.PricingMaxMultiplier;

    // ---- 七本实物账（含魔晶）的读与写 ----

    public bool TrySpendCoin(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "花的钱不能是负数");
        if (Coin < amount) return false;

        Coin -= amount;
        SpentCoin += amount;
        return true;
    }

    public void RefundCoin(double amount)
    {
        if (amount <= 0.0) return;
        Coin += amount;
        ProducedCoin += amount;
    }

    public void ProduceCoin(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "产出的钱不能是负数");
        Coin += amount;
        ProducedCoin += amount;
    }

    public bool TrySpendFood(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣的食物不能是负数");
        if (Food < amount) return false;

        Food -= amount;
        SpentFood += amount;
        return true;
    }

    public void RefundFood(double amount)
    {
        if (amount <= 0.0) return;
        Food += amount;
        ProducedFood += amount;
    }

    public void ProduceFood(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "产出的食物不能是负数");
        Food += amount;
        ProducedFood += amount;
    }

    public bool TrySpendSoulStone(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣的魂石不能是负数");
        if (SoulStone < amount) return false;

        SoulStone -= amount;
        SpentSoulStone += amount;
        return true;
    }

    public void RefundSoulStone(double amount)
    {
        if (amount <= 0.0) return;
        SoulStone += amount;
        ProducedSoulStone += amount;
    }

    public void ProduceSoulStone(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "产出的魂石不能是负数");
        SoulStone += amount;
        ProducedSoulStone += amount;
    }

    public bool TrySpendOre(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣的矿石不能是负数");
        if (Ore < amount) return false;

        Ore -= amount;
        SpentOre += amount;
        return true;
    }

    public void ProduceOre(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "产出的矿石不能是负数");
        Ore += amount;
        ProducedOre += amount;
    }

    public bool TrySpendSpecialMaterial(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣的特殊材料不能是负数");
        if (SpecialMaterial < amount) return false;

        SpecialMaterial -= amount;
        SpentSpecialMaterial += amount;
        return true;
    }

    public void ProduceSpecialMaterial(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "产出的特殊材料不能是负数");
        SpecialMaterial += amount;
        ProducedSpecialMaterial += amount;
    }

    public bool TrySpendSparePart(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣的备件不能是负数");
        if (SparePart < amount) return false;

        SparePart -= amount;
        SpentSparePart += amount;
        return true;
    }

    public void ProduceSparePart(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "产出的备件不能是负数");
        SparePart += amount;
        ProducedSparePart += amount;
    }

    public bool TrySpendCrystal(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣的魔晶不能是负数");
        if (Crystal < amount) return false;

        Crystal -= amount;
        SpentCrystal += amount;
        return true;
    }

    public void ProduceCrystal(double amount)
    {
        if (amount < 0.0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "产出的魔晶不能是负数");
        Crystal += amount;
        ProducedCrystal += amount;
    }

    /// <summary>
    /// 退回一笔试扣的魔晶（与其余 <c>Refund*</c> 同一口径：退回记进产出、不记消耗）。
    /// 先扣后落库失败那一格用它把账原样退回，守恒等式仍然成立。
    /// </summary>
    public void RefundCrystal(double amount)
    {
        if (amount <= 0.0) return;
        Crystal += amount;
        ProducedCrystal += amount;
    }

    /// <summary>
    /// 魔晶补魔：直接回填 <paramref name="cells"/> 格可用法力。
    /// <b>记进 <see cref="CrystalManaRestored"/></b>，守恒第 8 条据此把这一笔当「产出」扣掉。
    /// </summary>
    public void RestoreManaFromCrystal(int cells)
    {
        if (cells <= 0) return;

        // 能补的只有「真正空着的那几格」：占位格（停滞 / 帮工 / 固化）不算空。
        // 即时消耗已经体现在 AvailableMana 里，不再从余量里减一遍。
        var room = Math.Max(0, ManaCapacity - AvailableMana - OccupiedMana);
        var gain = Math.Min(cells, room);
        if (gain <= 0) return;

        AvailableMana += gain;
        CrystalManaRestored += gain;
    }

    // ---- 九条守恒等式（L1 不变量的唯一出口） ----

    /// <summary>
    /// 九条守恒等式逐条判。任一条不平返回 false。
    ///
    /// <list type="number">
    /// <item>金币 = 2000 + 产出 − 消耗</item>
    /// <item>食物 = 500 + 产出 − 消耗</item>
    /// <item>魂石 = 30 + 产出 − 消耗</item>
    /// <item>矿石 = 10 + 产出 − 消耗</item>
    /// <item>特殊材料 = 2 + 产出 − 消耗</item>
    /// <item>备件 = 3 + 产出 − 消耗</item>
    /// <item>魔晶 = 0 + 产出 − 消耗（生态产出与用途 / 出售两端都要对得上）</item>
    /// <item>负债总额 = 累计欠 − 累计还（按债主分户汇总，初始 0）</item>
    /// <item>可用 + 停滞 + 帮工 + 即时消耗 − 自然恢复 − 魔晶补魔 = 10</item>
    /// </list>
    /// </summary>
    public bool IsConserved()
    {
        const double tol = 1e-9;
        return Nearly(Coin, ArenaTuning.OpeningCoin + ProducedCoin - SpentCoin, tol)
            && Nearly(Food, ArenaTuning.OpeningFood + ProducedFood - SpentFood, tol)
            && Nearly(SoulStone, ArenaTuning.StartingSoulStones + ProducedSoulStone - SpentSoulStone, tol)
            && Nearly(Ore, ArenaTuning.OpeningOre + ProducedOre - SpentOre, tol)
            && Nearly(SpecialMaterial, ArenaTuning.OpeningSpecialMaterial
                + ProducedSpecialMaterial - SpentSpecialMaterial, tol)
            && Nearly(SparePart, ArenaTuning.StartingSpareParts
                + ProducedSparePart - SpentSparePart, tol)
            && Nearly(Crystal, ProducedCrystal - SpentCrystal, tol)
            && Nearly(TotalDebt, AccruedDebt - RepaidDebt, tol)
            && AvailableMana + StasisMana + HelperMana + InstantSpentMana
                - RegainedMana - CrystalManaRestored == ManaCapacity;
    }

    /// <summary>守恒逐条读数（测试与排障用：一处不平要说得清是哪一条）。</summary>
    public string ConservedReport()
    {
        return string.Join(" | ", new[]
        {
            Item("coin", Coin, ArenaTuning.OpeningCoin + ProducedCoin - SpentCoin),
            Item("food", Food, ArenaTuning.OpeningFood + ProducedFood - SpentFood),
            Item("soul", SoulStone, ArenaTuning.StartingSoulStones + ProducedSoulStone - SpentSoulStone),
            Item("ore", Ore, ArenaTuning.OpeningOre + ProducedOre - SpentOre),
            Item("mat", SpecialMaterial,
                ArenaTuning.OpeningSpecialMaterial + ProducedSpecialMaterial - SpentSpecialMaterial),
            Item("part", SparePart, ArenaTuning.StartingSpareParts + ProducedSparePart - SpentSparePart),
            Item("crystal", Crystal, ProducedCrystal - SpentCrystal),
            Item("debt", TotalDebt, AccruedDebt - RepaidDebt),
            string.Format(CultureInfo.InvariantCulture, "mana {0}",
                AvailableMana + StasisMana + HelperMana + InstantSpentMana
                - RegainedMana - CrystalManaRestored),
        });
    }

    /// <summary>状态可查那一口：结算余与七本账的法力那一列。</summary>
    public string ResourcesText() => string.Format(CultureInfo.InvariantCulture,
        "coin:{0:0.##},food:{1:0.##},soul:{2:0.##},ore:{3:0.##},mat:{4:0.##},part:{5:0.##}",
        Coin, Food, SoulStone, Ore, SpecialMaterial, SparePart);

    /// <summary>法力那一列（<c>可用+停滞+帮工</c> 与分母总格数）。</summary>
    public string ManaText() => string.Format(CultureInfo.InvariantCulture,
        "{0}+{1}+{2}/{3}", AvailableMana, StasisMana, HelperMana, ManaCapacity);

    private void Bump(ManaHold hold, int delta)
    {
        switch (hold)
        {
            case ManaHold.Stasis:
                StasisMana += delta;
                break;
            default:
                HelperMana += delta;
                break;
        }
    }

    private double RepayOne(string factionId, ref double budget)
    {
        var account = CreditorOf(factionId);
        if (account is null) return 0.0;

        var want = Math.Min(budget, account.Outstanding);
        if (want <= 0.0) return 0.0;
        if (!TrySpendCoin(want)) return 0.0;

        var over = want - account.Repay(want);
        if (over > 0.0) RefundCoin(over);
        budget -= want - over;
        return want - over;
    }

    private static string Item(string label, double actual, double expected) => string.Format(
        CultureInfo.InvariantCulture, "{0} {1:0.###}vs{2:0.###}", label, actual, expected);

    private static bool Nearly(double a, double b, double tol) => Math.Abs(a - b) <= tol;
}
