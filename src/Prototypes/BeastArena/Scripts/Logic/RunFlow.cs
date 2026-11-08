#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 一局流程那一件：段、当前目标、下一站、四个里程碑步号、双成长完成、最终防守资格。
//
// **为什么单独成件**：段与目标原来根本没有 —— 停靠城 id、拔锚闩、位阶三个散字段拼出来的
// 「现在大概在第几步」既没人读得出来，也没法在检查点上判红。三城航线要按贫 → 沙 → 富推进，
// 富城还要判终局资格，这些判断都得有个只读得懂流程状态的地方。
//
// **它不参与任何动词的放行判定**（规格原文「流程只决定目标与资格，不参与任何动词的放行判定」）：
// 偏离主线、出界、访问野外锚地、回旧城，动词一律照常发得出去，段只是不改。
// 所以这里没有任何 `Can...` 闸，只有一个「现在该往哪走」的读数。

/// <summary>一局的那十一段（与规格「流程状态」 的状态图一一对应）。</summary>
public enum RunStage
{
    /// <summary>还没按下开始游戏。</summary>
    NotStarted = 0,

    /// <summary>开局航程：从贫城附近的野地驶入贫城。</summary>
    ToPoor = 1,

    /// <summary>贫城段（停靠在贫城）。</summary>
    Poor = 2,

    /// <summary>第一段航行：贫城拔锚防守赢了，驶向沙城。</summary>
    ToSand = 3,

    /// <summary>沙城段（停靠在沙城）。</summary>
    Sand = 4,

    /// <summary>第二段航行：沙城拔锚防守赢了，驶向富城。</summary>
    ToRich = 5,

    /// <summary>富城段（停靠在富城）。</summary>
    Rich = 6,

    /// <summary>最终防守：双成长完成时拔锚进的那一场。</summary>
    FinalDefence = 7,

    /// <summary>普通防守：双成长未完成时富城拔锚进的那一场。</summary>
    NormalDefence = 8,

    /// <summary>终局结算（最终防守赢了）。</summary>
    Settlement = 9,

    /// <summary>结算之后继续经营，富城可以再拔锚。</summary>
    FreePlay = 10,
}

/// <summary>四个里程碑（步号进快照，重放与分段计时都读它）。</summary>
public enum RunMilestone
{
    /// <summary>暴食态解锁。</summary>
    GluttonUnlocked = 0,

    /// <summary>暴食态第一次被手动切上去。</summary>
    FirstFormSwitch = 1,

    /// <summary>二阶完成（进阶仪式走完）。</summary>
    Tier2 = 2,

    /// <summary>最终防守胜利。</summary>
    FinalVictory = 3,
}

/// <summary>
/// 一局流程的状态机。
///
/// <para><b>能脱离世界单独构造与推进</b>：不持有 <see cref="ArenaWorld"/>，也不读墙钟。
/// 调用方喂步号与「停靠了哪座城 / 拔锚了 / 防守收场了」三个事实，本件只答
/// 「现在是哪一段、目标是什么、下一站是哪、够不够格进最终防守」。</para>
///
/// <para><b>双成长完成读三处</b>（规格「双成长完成」）：位阶读 <see cref="BeastGrowth.Tier"/>、
/// 暴食态读 <see cref="BeastGrowth.FormUnlocked"/>、手动切换过一次由本件自己记
/// （<see cref="NoteFormSwitched"/>）。三项都不是现算的派生值就是永久事实，
/// 所以不需要「踏出沙漠带」那类清零语义。</para>
///
/// <para><b>偏离不改段</b>：停靠在别的城、拔锚、防守都不推段 —— 推进只发生在
/// 「该到的那座城」与「该打的那一场赢了」这两处。</para>
/// </summary>
public sealed partial class RunFlow
{
    private readonly BeastGrowth _growth;

    private long _gluttonUnlockedTick = -1;
    private long _firstSwitchTick = -1;
    private long _tier2Tick = -1;
    private long _finalVictoryTick = -1;

    /// <summary>这一场防守是从哪座城拔的锚（防守收场时判该不该推段）。</summary>
    private string _defenceCityId = string.Empty;

    /// <summary>拔锚那一刻所在的段（防守记录归到贫城 / 沙城 / 富城哪一段）。</summary>
    private RunStage _defenceStage = RunStage.NotStarted;

    /// <summary>这一局的防守记录（按收场次序；结算按段取）。</summary>
    private readonly List<DefenceRecord> _defenceRecords = [];

    /// <summary>造一份未开始的流程。</summary>
    public RunFlow(BeastGrowth growth)
    {
        ArgumentNullException.ThrowIfNull(growth);
        _growth = growth;
    }

    /// <summary>现在走到哪一段。</summary>
    public RunStage Stage { get; private set; } = RunStage.NotStarted;

    /// <summary>这一段的归属城（未开始没有归属城，回空串）。</summary>
    public string HomeCityId => Stage switch
    {
        RunStage.NotStarted => string.Empty,
        RunStage.ToPoor or RunStage.Poor => BigMapGrid.PoorCity.Id,
        RunStage.ToSand or RunStage.Sand => BigMapGrid.DesertCity.Id,
        _ => BigMapGrid.RichCity.Id,
    };

    /// <summary>
    /// 下一站（照航线给，不因为「还没打赢」就空着）：开局航程与贫城段都是贫城，
    /// 贫城段之后的下一站是沙城，沙城段之后是富城。<b>富城是终点，回空串</b>。
    ///
    /// <para>段内也给下一站，是为了目标驱动的 HUD 能提前指出「防守赢了往哪开」。</para>
    /// </summary>
    public string NextCityId => Stage switch
    {
        RunStage.NotStarted or RunStage.ToPoor => BigMapGrid.PoorCity.Id,
        RunStage.Poor or RunStage.ToSand => BigMapGrid.DesertCity.Id,
        RunStage.Sand or RunStage.ToRich => BigMapGrid.RichCity.Id,
        _ => string.Empty,
    };

    /// <summary>当前目标（一句中文，目标驱动的 HUD 与结算都用它）。</summary>
    public string Goal => Stage switch
    {
        RunStage.NotStarted => "按「开始游戏」出发",
        RunStage.ToPoor => "驶入贫城，第一次停靠",
        RunStage.Poor => "经营贫城：一次比赛、第一象限前两层、洞穴清剿、拔锚防守",
        RunStage.ToSand => "驶向沙城：吞食、消化，凑出第一单位魔晶",
        RunStage.Sand => "经营沙城：魔晶嵌入、二阶代价、拔锚防守",
        RunStage.ToRich => "驶向富城",
        RunStage.Rich => "经营富城，备好拔锚防守",
        RunStage.FinalDefence => "守住富城的最终防守",
        RunStage.NormalDefence => "守住富城的防守",
        RunStage.Settlement => "最终防守已胜，等结算",
        _ => "自由经营：富城只触发普通防守",
    };

    /// <summary>段的中文短名（HUD 那一行开头用）。</summary>
    public string PhaseLabel => Stage switch
    {
        RunStage.NotStarted => "未开始",
        RunStage.ToPoor => "开局航程",
        RunStage.Poor => "贫城段",
        RunStage.ToSand => "第一段航行",
        RunStage.Sand => "沙城段",
        RunStage.ToRich => "第二段航行",
        RunStage.Rich => "富城段",
        RunStage.FinalDefence => "最终防守",
        RunStage.NormalDefence => "普通防守",
        RunStage.Settlement => "结算",
        _ => "继续经营",
    };

    /// <summary>
    /// 双成长完成没有（规格「双成长完成」）：二阶 + 暴食态已解锁 + 暴食态至少手动切过一次。
    /// 荒漠态不要求解锁，当前形态也不要求是暴食态。
    /// </summary>
    public bool DoubleGrowthComplete
        => _growth.Tier >= BeastTier.Tier2
            && _growth.FormUnlocked(BeastForm.Glutton)
            && _firstSwitchTick >= 0;

    /// <summary>现在够不够格进最终防守：在富城段且双成长完成。</summary>
    public bool FinalDefenceReady => Stage == RunStage.Rich && DoubleGrowthComplete;

    /// <summary>
    /// 已经驶离贫城没有（暴食态解锁的第二道门）。
    ///
    /// <para><b>判据是「曾经进入过 <see cref="RunStage.ToSand"/> 那一程」</b>：贫城拔锚防守赢下来、
    /// 段推到第一段航行，就是真的驶离了。光是下一条离城航行令不算 —— 那条令可能被中途撤回，
    /// 兽也可能一直停在贫城边上；反过来，进了第一段航行之后段再也不会退回贫城段
    /// （富城那两档与结算都在它后面），所以这一个比较就是「曾经」。</para>
    /// </summary>
    public bool DepartedPoorCity => Stage >= RunStage.ToSand;

    /// <summary>暴食态解锁那一刻的步号（没解锁 = -1）。</summary>
    public long GluttonUnlockedTick => _gluttonUnlockedTick;

    /// <summary>第一次手动切到暴食态的步号（没切过 = -1）。</summary>
    public long FirstSwitchTick => _firstSwitchTick;

    /// <summary>二阶完成那一刻的步号（没报过 = -1）。</summary>
    public long Tier2Tick => _tier2Tick;

    /// <summary>最终防守胜利那一刻的步号（没报过 = -1）。</summary>
    public long FinalVictoryTick => _finalVictoryTick;

    /// <summary>最终防守失败后「立即重试」累计了几次（只增不清零，不随世界还原归零）。</summary>
    public int FinalRetryCount { get; private set; }

    /// <summary>最终防守打到第几次尝试（首次开战 = 1；每次重试 +1）。</summary>
    public int FinalAttemptIndex { get; private set; }

    /// <summary>这一局防守胜场（含三次拔锚与最终防守）。</summary>
    public int DefenceWins { get; private set; }

    /// <summary>这一局防守败场（含最终防守每一次失败尝试）。</summary>
    public int DefenceLosses { get; private set; }

    /// <summary>防守记录（按收场次序；结算按贫城 / 沙城 / 富城三段取）。</summary>
    public IReadOnlyList<DefenceRecord> DefenceRecords => _defenceRecords;

    /// <summary>一次防守收场的记录（归到拔锚时所在的那一段）。</summary>
    /// <param name="CityId">从哪座城拔的锚。</param>
    /// <param name="HomeStage">拔锚那一刻所在的段（Poor / Sand / Rich）。</param>
    /// <param name="Won">守住了没有。</param>
    /// <param name="Tick">收场步号（未知 = -1）。</param>
    public sealed record DefenceRecord(string CityId, RunStage HomeStage, bool Won, long Tick);

    /// <summary>段进入（状态变更之后发一次；同一段重进不发）。</summary>
    public event Action<RunStage>? PhaseEntered;

    /// <summary>目标变更（随段换；文案真的变了才发）。</summary>
    public event Action<string>? GoalChanged;

    /// <summary>里程碑达成（一个里程碑只发一次，参数是步号）。</summary>
    public event Action<RunMilestone, long>? MilestoneReached;

    /// <summary>结算打开（最终防守赢了）。</summary>
    public event Action? SettlementOpened;

    /// <summary>从结算回到继续经营。</summary>
    public event Action? FreePlayEntered;

    /// <summary>
    /// 开始游戏：只把「未开始」推到开局航程。
    ///
    /// <para><b>只推一段</b>：开局位置是贫城附近的野地、未停靠，
    /// 「抵达贫城段」得等玩家自己按停靠 —— 这一发替身不了玩家的那一按。</para>
    /// </summary>
    /// <returns>受理返回 true；已经在局里返回 false。</returns>
    public bool Start()
    {
        if (Stage != RunStage.NotStarted) return false;

        Enter(RunStage.ToPoor);
        return true;
    }

    /// <summary>
    /// 停靠了（玩家按停靠并把兽停稳）。<b>只有该到的那座城才推段</b>：
    /// 顺路停别的城、回旧城、去野外锚地都只是停靠，段不动。
    /// </summary>
    /// <returns>段真的变了返回 true。</returns>
    public bool NoteDocked(string cityId)
    {
        ArgumentNullException.ThrowIfNull(cityId);

        if (Stage == RunStage.ToPoor && cityId == BigMapGrid.PoorCity.Id)
        {
            return Enter(RunStage.Poor);
        }

        if (Stage == RunStage.ToSand && cityId == BigMapGrid.DesertCity.Id)
        {
            return Enter(RunStage.Sand);
        }

        if (Stage == RunStage.ToRich && cityId == BigMapGrid.RichCity.Id)
        {
            return Enter(RunStage.Rich);
        }

        return false;
    }

    /// <summary>
    /// 拔锚了（防守马上开打）。<b>富城这一拔要当场判终局资格</b>
    /// （规格「流程状态」：Rich → FinalDefence 是「双成长完成且拔锚」）；
    /// 其余各段只记下「这一场是从哪座城拔的锚」，等防守收场再判。
    /// </summary>
    public void NoteAnchorPulled(string cityId)
    {
        ArgumentNullException.ThrowIfNull(cityId);

        _defenceCityId = cityId;
        _defenceStage = Stage;

        if (cityId != BigMapGrid.RichCity.Id) return;

        if (Stage == RunStage.Rich)
        {
            if (DoubleGrowthComplete)
            {
                FinalAttemptIndex++;
                Enter(RunStage.FinalDefence);
            }
            else
            {
                Enter(RunStage.NormalDefence);
            }
        }
        else if (Stage == RunStage.FreePlay)
        {
            // 继续经营之后富城再拔一次锚：照状态图仍走普通防守（不再弹一次结算）。
            Enter(RunStage.NormalDefence);
        }
    }

    /// <summary>
    /// 防守收场。<paramref name="won"/> 是「守住了」——<b>城破也算没赢</b>。
    ///
    /// <para><b>赢了才推段</b>：贫城赢了进第一段航行、沙城赢了进第二段航行；
    /// 城破那一场不算防守胜利，段停在原地，补救之后再拔一次锚、赢了才往前走。</para>
    ///
    /// <para>富城两档：普通防守收场（胜也好败也好）回到富城段；
    /// 最终防守赢了进结算，输了留在最终防守等重试（重试本身不在这一件里），
    /// 并把<b>重试次数 +1</b> —— 这一格只增不清零，不随世界状态还原归零。</para>
    /// </summary>
    /// <param name="won">守住了没有。</param>
    /// <param name="tick">收场步号（由世界递；单独构造的用例可以不给）。</param>
    public void NoteDefenceSettled(bool won, long tick = -1)
    {
        var stage = Stage;

        if (stage is RunStage.Poor or RunStage.Sand or RunStage.NormalDefence or RunStage.FinalDefence)
        {
            if (won) DefenceWins++;
            else DefenceLosses++;
            _defenceRecords.Add(new DefenceRecord(_defenceCityId, _defenceStage, won, tick));
        }

        if (stage == RunStage.NormalDefence)
        {
            Enter(RunStage.Rich);
            return;
        }

        if (stage == RunStage.FinalDefence)
        {
            if (!won)
            {
                FinalRetryCount++;
                return;
            }

            if (tick >= 0) NoteFinalVictory(tick);
            Enter(RunStage.Settlement);
            SettlementOpened?.Invoke();
            return;
        }

        if (!won) return;

        if (stage == RunStage.Poor && _defenceCityId == BigMapGrid.PoorCity.Id)
        {
            Enter(RunStage.ToSand);
        }
        else if (stage == RunStage.Sand && _defenceCityId == BigMapGrid.DesertCity.Id)
        {
            Enter(RunStage.ToRich);
        }
    }

    /// <summary>
    /// 「立即重试」那一发：把尝试序号 +1。<b>重试次数不在这里加</b> ——
    /// 它记的是失败次数，在 <see cref="NoteDefenceSettled"/> 的失败分支里累加。
    /// </summary>
    public void NoteFinalRetryStarted() => FinalAttemptIndex++;

    /// <summary>
    /// 从 <c>FinalSnapshot</c> 还原流程里属于世界状态的那几格：
    /// 段、四个里程碑步号、这一场从哪座城拔的锚。
    /// <b>重试次数、尝试序号与防守记录不参与还原</b>（它们是局流程的账，不是世界状态）。
    /// </summary>
    internal void RestoreFromFinalSnapshot(
        RunStage stage,
        long gluttonUnlockedTick,
        long firstSwitchTick,
        long tier2Tick,
        long finalVictoryTick,
        string defenceCityId)
    {
        Stage = stage;
        _gluttonUnlockedTick = gluttonUnlockedTick;
        _firstSwitchTick = firstSwitchTick;
        _tier2Tick = tier2Tick;
        _finalVictoryTick = finalVictoryTick;
        _defenceCityId = defenceCityId;
        _defenceStage = stage == RunStage.FinalDefence ? RunStage.Rich : stage;
    }

    /// <summary>
    /// 手动切到暴食态那一发（幂等：只记第一次）。
    /// <b>切到别的形态不算</b> —— 双成长那一项要求的是「暴食态」手动切过一次。
    /// </summary>
    public void NoteFormSwitched(BeastForm form, long tick)
    {
        if (form != BeastForm.Glutton) return;
        Mark(ref _firstSwitchTick, RunMilestone.FirstFormSwitch, tick);
    }

    /// <summary>暴食态解锁那一发（由世界在每次解锁判定之后递一步号；幂等）。</summary>
    public void NoteGluttonUnlocked(long tick)
        => Mark(ref _gluttonUnlockedTick, RunMilestone.GluttonUnlocked, tick);

    /// <summary>二阶完成（<b>留口</b>：本张 ticket 不接线，由进阶那一张上报）。</summary>
    public void NoteTier2(long tick) => Mark(ref _tier2Tick, RunMilestone.Tier2, tick);

    /// <summary>最终防守胜利（<b>留口</b>：由终局那一张上报）。</summary>
    public void NoteFinalVictory(long tick) => Mark(ref _finalVictoryTick, RunMilestone.FinalVictory, tick);

    /// <summary>结算之后继续经营。</summary>
    /// <returns>受理返回 true；不在结算那一档返回 false。</returns>
    public bool ContinueBusiness()
    {
        if (Stage != RunStage.Settlement) return false;

        Enter(RunStage.FreePlay);
        FreePlayEntered?.Invoke();
        return true;
    }

    /// <summary>换段（幂等：同一段不发事件）。</summary>
    private bool Enter(RunStage next)
    {
        if (Stage == next) return false;

        var before = Goal;
        Stage = next;
        PhaseEntered?.Invoke(next);
        if (Goal != before) GoalChanged?.Invoke(Goal);
        return true;
    }

    /// <summary>落一个里程碑步号（已经落过就不再动，事件也只发一次）。</summary>
    private void Mark(ref long slot, RunMilestone kind, long tick)
    {
        if (slot >= 0) return;

        slot = tick;
        MilestoneReached?.Invoke(kind, tick);
    }
}

/// <summary>段的词表 token（小写英文，进快照的 <c>flow=</c> 那一栏，是契约）。</summary>
public static class RunStageText
{
    /// <summary>token：<c>notstarted</c> / <c>topoor</c> / … / <c>freeplay</c>。</summary>
    public static string TokenOf(RunStage stage) => stage switch
    {
        RunStage.NotStarted => "notstarted",
        RunStage.ToPoor => "topoor",
        RunStage.Poor => "poor",
        RunStage.ToSand => "tosand",
        RunStage.Sand => "sand",
        RunStage.ToRich => "torich",
        RunStage.Rich => "rich",
        RunStage.FinalDefence => "finaldefence",
        RunStage.NormalDefence => "normaldefence",
        RunStage.Settlement => "settlement",
        _ => "freeplay",
    };
}
