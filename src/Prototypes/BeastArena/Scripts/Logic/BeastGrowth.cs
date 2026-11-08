#nullable enable
using System;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 巨兽成长那一件：位阶、形态、两个解锁计数与进阶仪式。
//
// **为什么单独成件**：生态那一线要改形态与解锁条件，地下那一线要改进阶门槛，
// 防守那一线要读位阶收益 —— 三摊各改各的，但原来全挤在 ArenaWorld 的同一段字段里。
// 把这一份状态与它的推进收成一件，三条线就能并行改而不互相踩。
//
// **状态只有一份**：两个解锁计数原来住在 BeastVoyage（地形读数在那一层）。
// 搬进来之后 Voyage 只把「这一分钟在不在沙漠带」「这一口喂进去多少」递过来，
// 读数仍由 Voyage 转发同一格 —— 世界里不存在第二份计数。

/// <summary>
/// 切形态那一发的三种结局 —— 世界那一侧据此翻成拒绝码，本件不依赖 <c>RejectReason</c>。
/// </summary>
public enum FormSwitchOutcome
{
    /// <summary>形态没变（切到当前形态）：幂等成功。</summary>
    Unchanged = 0,

    /// <summary>形态真的变了：已发出 <see cref="BeastGrowth.FormSwitched"/>。</summary>
    Switched = 1,

    /// <summary>这个形态还没解锁。</summary>
    Locked = 2,
}

/// <summary>
/// 巨兽成长状态机：当前位阶 / 形态、两个解锁进度、进阶仪式。
///
/// <para><b>能脱离世界单独构造与推进</b>：不持有 <see cref="ArenaWorld"/>，也不读墙钟。
/// 调用方喂 tick（<see cref="BeginRitual"/> / <see cref="SettleRitual"/>）与两个计数的增量
/// （<see cref="AdvanceDwell"/> / <see cref="AddFedTotalFood"/>），本件只负责状态与事件。</para>
///
/// <para><b>两个解锁标记是独立的永久位</b>，不是每次现算的派生值 ——
/// 沙漠计时按「累计」走（离开不清零），越过阈值之后更是永久有效。</para>
/// </summary>
public sealed partial class BeastGrowth
{
    private readonly Func<bool> _departedPoorCity;

    /// <summary>
    /// 造一件成长状态。
    /// <b>「已驶离贫城」这一条是可注入的谓词</b>：世界递的是局流程的读数
    /// （<see cref="RunFlow.DepartedPoorCity"/>），成长件自己不读流程、也不读位置；
    /// 单独构造（测试）默认不设门。
    /// </summary>
    public BeastGrowth(Func<bool>? departedPoorCity = null)
    {
        _departedPoorCity = departedPoorCity ?? (() => true);
    }

    /// <summary>当前位阶（开局一阶）。</summary>
    public BeastTier Tier { get; private set; } = BeastTier.Tier1;

    /// <summary>当前形态（开局原始态，没有出身差别）。</summary>
    public BeastForm Form { get; private set; } = BeastForm.Original;

    /// <summary>进阶仪式到哪一步（没在仪式 = 0）。</summary>
    public long RitualUntilTick { get; private set; }

    /// <summary>在沙漠带里<b>累计</b>待过的游戏分钟（离开不清零）。</summary>
    public long DwellGameMinutes { get; private set; }

    /// <summary>累计有效喂进去的食物（手动喂食与吞食之外的食物入口都算）。</summary>
    public double FedTotalFood { get; private set; }

    /// <summary>荒漠态已解锁没有（上锁之后永久有效，计数清零不回收）。</summary>
    public bool DesertFormUnlocked { get; private set; }

    /// <summary>暴食态已解锁没有（上锁之后永久有效，计数清零不回收）。</summary>
    public bool GluttonFormUnlocked { get; private set; }

    /// <summary>
    /// 二阶的进阶资格有没有落下（<b>永久</b>）。
    /// 三项条件 —— 同一象限挖到第二层、该象限洞穴已清剿、该格已嵌入魔晶 ——
    /// 在同一次嵌入成功时一起成立，所以这里只记「落下来了没有」这一位；
    /// 之后不再回读格状态（嵌入本身也永久停止回长，没有回退的机会）。
    /// </summary>
    public bool Tier2Eligible { get; private set; }

    /// <summary>
    /// 落下二阶的进阶资格（嵌入魔晶成功后由 <see cref="ArenaWorld.EmbedCrystal"/> 调一次）。
    /// 重复调是幂等的 —— 同格不可重复嵌入，走不到第二次；换一格嵌入也不会把资格弄丢。
    /// <b>第一次落下时抛一次 <see cref="TierGateSatisfied"/></b>，之后不再重发。
    /// </summary>
    public void RecordTier2Eligibility(int sector, int layer)
    {
        if (Tier2Eligible) return;

        Tier2Eligible = true;
        TierGateSatisfied?.Invoke(sector, layer);
    }

    /// <summary>
    /// 位阶进阶到点时抛一次（表现层出横幅用）。
    /// <b>被拒的那几发不发，一次进阶只发一次</b>。
    /// </summary>
    public event Action<BeastTier>? TierAdvanced;

    /// <summary>
    /// 形态真变时抛一次（表现层出横幅用）。
    /// <b>形态确实变了才发</b>：未解锁、参数不认识、切到当前形态（幂等）都不发。
    /// </summary>
    public event Action<BeastForm>? FormSwitched;

    /// <summary>某个形态第一次解锁时抛一次（一次上锁只发一次）。</summary>
    public event Action<BeastForm>? FormUnlockedEvent;

    /// <summary>二阶三项门槛首次同时满足时抛一次（象限 + 层；只发一次，永久不重复）。</summary>
    public event Action<int, int>? TierGateSatisfied;

    /// <summary>现在是不是在进阶仪式里（与停摆期分开：快照要分得清那两件事）。</summary>
    public bool InRitualAt(long tick) => RitualUntilTick > tick;

    /// <summary>
    /// 这个形态已经解锁没有。原始态开局即有；另两种靠计数越过阈值那一刻上锁。
    /// </summary>
    public bool FormUnlocked(BeastForm form) => form switch
    {
        BeastForm.Desert => DesertFormUnlocked,
        BeastForm.Glutton => GluttonFormUnlocked,
        _ => true,
    };

    /// <summary>
    /// 沙漠累计计时走一格：在沙漠带里就 +1 个游戏分钟，**不在也不清零**。
    ///
    /// <para><b>判「在不在带里」是调用方的事</b>：地形读数住在 <see cref="BeastVoyage"/> 那一层，
    /// 本件只接一个布尔 —— 于是它不依赖大地图，也不会自己再读一遍地形。</para>
    /// </summary>
    public void AdvanceDwell(bool inDesertBand)
    {
        if (inDesertBand) DwellGameMinutes++;
    }

    /// <summary>调试直写口：把沙漠累计计时摆到某个数（真跑要 4 游戏小时，测试等不起）。</summary>
    public void SetDwellGameMinutes(long minutes)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "沙漠累计分钟不能是负数");
        }

        DwellGameMinutes = minutes;
    }

    /// <summary>往累计喂食里加一份（只有实际扣粮且有效恢复饱食度的食物才走这一口）。</summary>
    public void AddFedTotalFood(double amount)
    {
        if (amount < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "加的喂食量不能是负数");
        }

        FedTotalFood += amount;
    }

    /// <summary>
    /// 解锁判定：两个计数越过阈值就把对应的形态标记为已解锁。
    ///
    /// <para><b>越过阈值那一刻就上锁</b>：之后计数不再回退 ——
    /// 所以标记是独立的一份，不做成「现查计数」。</para>
    ///
    /// <para><b>暴食态多一道门</b>：累计喂食够 400 <b>且</b>已经驶离贫城 ——
    /// 「驶离」由构造时递进来的谓词回答（世界里接的是 <see cref="RunFlow.DepartedPoorCity"/>），
    /// 本件不自己看位置。</para>
    ///
    /// <para>调用的两处：每一步（排在 <see cref="BeastVoyage.DecideStep"/> 之后）与手动喂食之后
    /// —— 手动喂食凑够 400 食物不该等到下一步才解锁。</para>
    /// </summary>
    public void CheckUnlocks()
    {
        if (!DesertFormUnlocked && DwellGameMinutes >= ArenaTuning.DesertFormDwellGameMinutes)
        {
            DesertFormUnlocked = true;
            FormUnlockedEvent?.Invoke(BeastForm.Desert);
        }

        if (!GluttonFormUnlocked
            && FedTotalFood >= ArenaTuning.GluttonFormFedTotalFood
            && _departedPoorCity())
        {
            GluttonFormUnlocked = true;
            FormUnlockedEvent?.Invoke(BeastForm.Glutton);
        }
    }

    /// <summary>
    /// 发起进阶仪式：从 <paramref name="tick"/> 起挂 <paramref name="durationTicks"/> 步。
    ///
    /// <para><b>闸在调用方</b>：位阶上限、阶段、门槛、资源那四档由世界的动词判定；
    /// 本件只落「仪式到哪一步」这一个事实。</para>
    /// </summary>
    public void BeginRitual(long tick, long durationTicks)
    {
        if (durationTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationTicks), durationTicks, "仪式时长必须是正数");
        }

        RitualUntilTick = tick + durationTicks;
    }

    /// <summary>
    /// 仪式到点：位阶 +1、清掉仪式读数、抛一次 <see cref="TierAdvanced"/>。
    ///
    /// <para><b>与停摆期分开</b>：两边都靠「到点」推进，但位阶与攻破是两件事，
    /// 快照里也要分得清（<c>ritual=</c> 与 <c>phase=standstill</c>）。</para>
    /// </summary>
    /// <returns>这一格真的进阶了返回 true；没仪式或没到点返回 false。</returns>
    public bool SettleRitual(long tick)
    {
        if (RitualUntilTick <= 0) return false;
        if (tick < RitualUntilTick) return false;

        RitualUntilTick = 0;
        Tier = (BeastTier)((int)Tier + 1);
        TierAdvanced?.Invoke(Tier);
        return true;
    }

    /// <summary>
    /// 切形态：瞬态、无代价、随时可切。
    ///
    /// <para><b>判定顺序两档</b>：没解锁 → <see cref="FormSwitchOutcome.Locked"/>；
    /// 切到当前形态是<b>幂等成功</b>（形态没变，事件也不发）。</para>
    /// </summary>
    public FormSwitchOutcome SwitchForm(BeastForm form)
    {
        if (!FormUnlocked(form)) return FormSwitchOutcome.Locked;
        if (Form == form) return FormSwitchOutcome.Unchanged;

        Form = form;
        FormSwitched?.Invoke(form);
        return FormSwitchOutcome.Switched;
    }
}
