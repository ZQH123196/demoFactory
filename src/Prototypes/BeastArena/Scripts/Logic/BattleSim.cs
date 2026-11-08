#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 按游戏分钟自动战斗那一摊：办赛、拔锚防守、洞穴清剿三种场合共用同一套配对 / 走位 / 交手结算。
//
// 四条口径：
//   · **配对每满 1 个游戏分钟重算一次**：办赛与洞穴清剿双方全员入池；
//     拔锚防守只把「距任一我方站立参战人员 ≤ 4.0 米、且没抢满 3 件」的敌人入池。
//   · **生命值只住在单位自己身上**（<see cref="ArenaUnit.Hp"/>），这一件不另留第二份副本。
//   · **生死是确定的**：`Hp ≤ -100` 当场死（生成尸体；镇民与来敌每具入账 3 魂石，洞穴原生怪不给）；
//     `-100 < Hp ≤ 0` 是重伤退场。帮工 `Hp` 归零直接消散，无尸无魂石。
//   · **攻破那一格**：普通防守里耐久锁在 0、不出册、立刻结束防守并清退余敌，
//     然后按造价从高到低毁 3 栋普通建筑、按名单序号掳 7 人、扣仓库货 30%、按 1.5 倍折债，
//     <b>立即由城卫队解救</b>（恢复 100 耐久并把债记进对应势力的负债账），不再停摆三个游戏日。
//     最终防守被攻破只收场，不走这串记债与解救 —— 失败后按战前快照立即重试。

/// <summary>三种场合共用一套模拟，这一列是「现在打的是哪一种」。</summary>
public enum BattleOccasion
{
    /// <summary>无仗（五列全填 0）。</summary>
    None = 0,

    /// <summary>办赛（1.5 游戏小时）。</summary>
    Match = 1,

    /// <summary>拔锚防守（2 游戏小时）。</summary>
    Defence = 2,

    /// <summary>最终防守（与普通防守共用时限、编制与胜负条件，只是场合标识不同）。</summary>
    FinalDefence = 4,

    /// <summary>洞穴清剿（挖开洞穴格时）。</summary>
    CaveClearing = 3,
}

/// <summary>
/// 洞穴清剿那一发为什么算失败（<c>CaveClearFailed</c> 的封闭载荷；规格「事件规则 · 洞穴」）。
/// 攻破收场没有独立一档：怪没打死就收场，与到点同口径归 <see cref="Timeout"/>。
/// </summary>
public enum CaveFailReason
{
    /// <summary>时限到点（含镇政府被攻破提前收场）。</summary>
    Timeout = 0,

    /// <summary>玩家主动撤退。</summary>
    Retreated = 1,
}

/// <summary>
/// 办赛（<see cref="BattleOccasion.Match"/>）的胜负口径 —— 也是战报上屏的那一列。
///
/// <para><b>口径照 spec §9</b>：时长打满时场上人数多的一方胜、相等记平；全员倒地记负。
/// 「场上人数」读的是<b>收场那一刻</b>还站着的人数（收场之后伤员会被扶起来，那一档不算）。</para>
/// </summary>
public enum MatchOutcome
{
    /// <summary>不评胜负（没打过仗，或不是办赛场合）。</summary>
    None = 0,

    /// <summary>甲队胜。</summary>
    SideAWins = 1,

    /// <summary>乙队胜。</summary>
    SideBWins = 2,

    /// <summary>平（双方场上人数相等，且都还有人站着）。</summary>
    Draw = 3,

    /// <summary>负：我方（甲队）全员倒地，或双方都倒光了。</summary>
    Defeat = 4,
}

/// <summary>
/// 一场仗<b>收场那一刻</b>的结算读数（战报与快照 <c>R</c> 段的唯一真值）。
///
/// <para>在 <c>BattleSim.EndBattle</c> 里写入一次，不每帧重算 —— 所以收场之后
/// 把人扶起来、清空场上名单，都不会改掉这份读数。数字全是确定性的结算量，
/// 不含任何表现层读数（特效、cue 与动画都不进这里）。</para>
/// </summary>
/// <param name="Occasion">打的哪一种仗。</param>
/// <param name="Outcome">办赛的胜负（非办赛场合是 <see cref="MatchOutcome.None"/>）。</param>
/// <param name="SideACount">甲队上场数（收场时按开场名单计）。</param>
/// <param name="SideBCount">乙队上场数。</param>
/// <param name="DeadCount">这一场死掉的人数（按开场时的死者名单取差）。</param>
/// <param name="WoundedCount">这一场重伤的人数（同样取差，收场扶人之前算）。</param>
/// <param name="LootedCount">这一场被抢走的件数。</param>
/// <param name="GateIncome">办赛门票收入（金币）；非办赛场合是 0。</param>
/// <param name="EndedTick">收场步号。</param>
public sealed record BattleReport(
    BattleOccasion Occasion,
    MatchOutcome Outcome,
    int SideACount,
    int SideBCount,
    int DeadCount,
    int WoundedCount,
    int LootedCount,
    double GateIncome,
    long EndedTick);

/// <summary>一场对子：一侧最多两个围攻一个。</summary>
public sealed class EngagementPair
{
    /// <summary>建一对。</summary>
    public EngagementPair(int index, ArenaUnit leader, IReadOnlyList<ArenaUnit> attackers,
        IReadOnlyList<ArenaUnit> defenders)
    {
        Index = index;
        Leader = leader;
        Attackers = attackers;
        Defenders = defenders;
    }

    /// <summary>第几对（按名单序号配出来的那一档）。</summary>
    public int Index { get; }

    /// <summary>这一对的头一个（战力、伤害与走位都读他）。</summary>
    public ArenaUnit Leader { get; }

    /// <summary>攻方（1..2 个）。</summary>
    public IReadOnlyList<ArenaUnit> Attackers { get; }

    /// <summary>守方（1..2 个）。</summary>
    public IReadOnlyList<ArenaUnit> Defenders { get; }

    /// <summary>这一对结算出来的伤害读数（进攻方 → 防守方那一笔）。</summary>
    public double LastDamage { get; internal set; }
}

/// <summary>敌人在拔锚防守里分到的目标建筑（按名单序号每 10 人切一段）。</summary>
/// <param name="BuildingId">目标建筑 id。</param>
/// <param name="SegmentSlot">这一名在每 10 人那一段里的位次（0..9）。</param>
public sealed record EnemyTarget(string BuildingId, int SegmentSlot);

/// <summary>
/// 三种场合共用的按分钟自动战斗模拟。
///
/// <para><b>每一步的次序</b>：① 判到时（结束 / 清剿）→ ② 重算配对并向双方下 <c>GoTo</c> →
/// ③ 交手结算（≤ 2.0 米）→ ④ 打耐久 / 抢货 → ⑤ 判攻破。
/// 这一段跑在走位那一次脉冲<b>之后</b>，读的是走完之后的位置。</para>
///
/// <para><b>不掷点</b>：刷怪落点、敌人目标分段、清剿生死、吓跑取人全按名单序号或固定次序。</para>
/// </summary>
public sealed partial class BattleSim
{
    private readonly ArenaWorld _world;
    private readonly List<EngagementPair> _pairs = [];
    private readonly Dictionary<string, string> _enemyTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _enemyLoot = new(StringComparer.Ordinal);
    private readonly List<ArenaUnit> _sideA = [];
    private readonly List<ArenaUnit> _sideB = [];
    private readonly HashSet<string> _deadAtOpen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _woundedAtOpen = new(StringComparer.Ordinal);
    private int _enemyNumber;
    private int _monsterNumber;

    /// <summary>这一场洞穴清剿是哪个象限的哪一层（-1 = 没有清剿在场）。</summary>
    private int _caveSector = -1;
    private int _caveLayer = -1;

    /// <summary>这一场洞穴清剿是不是「四只全灭」收场（超时 / 撤退 / 攻破都是 false）。</summary>
    private bool _caveCleared;

    /// <summary>这一场洞穴清剿收场为失败时属于哪一档原因（每场开场先摆回 <see cref="CaveFailReason.Timeout"/>）。</summary>
    private CaveFailReason _caveFailReason = CaveFailReason.Timeout;

    /// <summary>这一场最终防守是不是被攻破收场（城破但不走普通城破的记债与解救）。</summary>
    private bool _finalDefenceFailed;

    /// <summary>接世界本体（建筑、账本、活物册与点位物都在那一头）。</summary>
    public BattleSim(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>当前场合。</summary>
    public BattleOccasion Occasion { get; private set; } = BattleOccasion.None;

    /// <summary>现在打的是不是拔锚防守（普通或最终那一档）。</summary>
    public bool IsDefence => IsDefenceOccasion(Occasion);

    /// <summary>某一档是不是拔锚防守（普通与最终共用同一套规则）。</summary>
    public static bool IsDefenceOccasion(BattleOccasion occasion)
        => occasion is BattleOccasion.Defence or BattleOccasion.FinalDefence;

    /// <summary>
    /// 拔锚防守收场那一发（<c>true</c> = 守住了，<c>false</c> = 城破或没守住）。
    ///
    /// <para><b>为什么由这一件发</b>：防守的胜负只在收场那一刻成立 ——
    /// 到点收场与攻破提前收场都走 <c>EndBattle</c>，收场之后
    /// <see cref="Occasion"/> 与镇政府耐久的读数都不再带「这一场赢没赢」。
    /// 别的场合（办赛、洞穴清剿）不发。</para>
    /// </summary>
    public event Action<bool>? DefenceSettled;

    /// <summary>
    /// 一场防守开起来那一刻抛一次（普通防守与最终防守都算，载荷是场合标识）。
    /// 办赛与洞穴清剿不发。
    /// </summary>
    public event Action<BattleOccasion>? DefenceStarted;

    /// <summary>洞穴清剿被受理那一刻抛一次（象限 + 层）。</summary>
    public event Action<int, int>? CaveClearStarted;

    /// <summary>洞穴清剿胜利（四只全灭、那一格转高危）那一刻抛一次（象限 + 层）。</summary>
    public event Action<int, int>? CaveCleared;

    /// <summary>洞穴清剿失败（超时 / 撤退 / 攻破）那一刻抛一次（象限 + 层 + 封闭原因）。</summary>
    public event Action<int, int, CaveFailReason>? CaveClearFailed;

    /// <summary>这一场开到哪一步（步号）。</summary>
    public long EndTick { get; private set; }

    /// <summary>场上人数（双方站着的那几个）。</summary>
    public int OnFieldCount { get; private set; }

    /// <summary>倒地数（重伤）。</summary>
    public int DownCount { get; private set; }

    /// <summary>死亡数。</summary>
    public int DeadCount { get; private set; }

    /// <summary>抢走件数（这一场敌人从镇上手里拿走的）。</summary>
    public int LootedCount { get; private set; }

    /// <summary>
    /// 上一场仗收场那一刻的结算读数（没打过仗回 null）。
    /// <b>只在 <c>EndBattle</c> 里写一次</b> —— 战报与快照 <c>R</c> 段都读这一份。
    /// </summary>
    public BattleReport? LastReport { get; private set; }

    /// <summary>在岗牧师（最多 2 名）：驻在教堂岗位上的镇民，位置读得出来的。</summary>
    public int PriestOnDuty
    {
        get
        {
            var n = 0;
            foreach (var u in StandingPriests())
            {
                if (n >= ArenaTuning.PriestMaxOnDuty) break;
                n++;
            }

            return n;
        }
    }

    /// <summary>本场配出来的对子（读数口；配对表每分钟重算一次）。</summary>
    public IReadOnlyList<EngagementPair> Pairs => _pairs;

    /// <summary>敌人在拔锚防守里分到的目标（按敌人 id 读）。</summary>
    public EnemyTarget? TargetOf(string enemyId)
        => _enemyTargets.TryGetValue(enemyId, out var t)
            ? new EnemyTarget(t, SegmentSlotOf(enemyId))
            : null;

    /// <summary>某一类敌人这一场已经抢了几件（够 3 件就退场）。</summary>
    public int LootOf(string enemyId) => _enemyLoot.GetValueOrDefault(enemyId);

    // ---- 三种场合的起手 ----

    /// <summary>办赛：停靠期开赛，持续 <see cref="ArenaTuning.MatchGameHours"/> 个游戏小时。</summary>
    public RejectReason StartMatch(IReadOnlyList<string> sideAIds, IReadOnlyList<string> sideBIds)
    {
        ArgumentNullException.ThrowIfNull(sideAIds);
        ArgumentNullException.ThrowIfNull(sideBIds);

        if (Occasion != BattleOccasion.None) return RejectReason.InvalidPhase;
        if (!_world.IsDocked) return RejectReason.InvalidPhase;

        // 竞技场可能是玩家新建的 bN —— 固定 id 只在开局那一栋还在册时成立。
        var arenaId = _world.FindByBlueprint("Arena");
        if (arenaId.Length == 0) return RejectReason.InvalidTarget;
        if (sideAIds.Count == 0 || sideBIds.Count == 0) return RejectReason.InvalidArgs;

        // 先收到本地的两份名单里校验完，再开战 —— `Open` 会清空 `_sideA/_sideB`，
        // 先收进字段再开战的话，两队会在开赛那一刻一起被清掉，整场变成空转。
        var ours = new List<ArenaUnit>();
        var theirs = new List<ArenaUnit>();
        var rejected = Collect(sideAIds, ours);
        if (rejected != RejectReason.InvalidArgs) return rejected;
        rejected = Collect(sideBIds, theirs);
        if (rejected != RejectReason.InvalidArgs) return rejected;

        Open(BattleOccasion.Match, ArenaTuning.MatchGameHours * ArenaTime.StepsPerGameHour);
        _sideA.AddRange(ours);
        _sideB.AddRange(theirs);

        // 双方从竞技场东西两侧入场；在岗牧师驻场边。入场点相对解析出的那一栋大门取，
        // 玩家新建的 bN 竞技场因此不会被拉回开局那一栋的坐标。
        var arenaDoor = _world.DoorDeckOf(arenaId);
        EnterAt(_sideA, DeckGeometry.ArenaWest(arenaDoor));
        EnterAt(_sideB, DeckGeometry.ArenaEast(arenaDoor));
        EnterPriests(arenaDoor);
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// 拔锚防守：持续 <see cref="ArenaTuning.DefenceGameHours"/> 个游戏小时，
    /// 敌人数 = <see cref="ArenaTuning.DefenceEnemyBase"/> + <see cref="ArenaTuning.DefenceEnemyPerRing"/> × 向心环数。
    /// </summary>
    public RejectReason PullAnchor(int inwardRings)
    {
        if (Occasion != BattleOccasion.None) return RejectReason.InvalidPhase;
        if (_world.IsDocked) return RejectReason.InvalidPhase;
        if (_world.InStandstill) return RejectReason.InvalidPhase;
        if (inwardRings < 0) return RejectReason.InvalidArgs;

        // 最终防守与普通防守共用时限、敌人编制与胜负条件，只有场合标识不同。
        // 资格由一局流程在拔锚那一刻判过（Rich → FinalDefence 只在双成长完成时成立）。
        var occasion = _world.Flow.Stage == RunStage.FinalDefence
            ? BattleOccasion.FinalDefence
            : BattleOccasion.Defence;
        Open(occasion, ArenaTuning.DefenceGameHours * ArenaTime.StepsPerGameHour);

        // 我方上场名单只给战报数人用（防守的配对仍走 OurFighters，不读这一列）。
        _sideA.Clear();
        _sideA.AddRange(OurFighters());

        var count = ArenaTuning.DefenceEnemyBase + (ArenaTuning.DefenceEnemyPerRing * inwardRings);
        for (var i = 0; i < count; i++) _sideB.Add(SpawnEnemy(i));
        EnterPriests();

        // 最终防守初始化完、首个逻辑步还没走 —— 这一格取战前全量快照。
        _world.NoteFinalDefenceInitialized();

        // 防守开起来：普通与最终共用这一口，场合标识由 Open 定下。
        DefenceStarted?.Invoke(occasion);
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// 发起洞穴清剿：单处洞穴（象限 0 第 2 层）里剩下的原生怪涌上甲板。
    ///
    /// <para><b>四只全灭才算清剿成功</b>；时限只当保险，到点、撤退或镇政府被攻破都算失败。
    /// 失败时洞里还剩几只就记几只 —— 下一次发起只涌出剩下的那几只，不重新刷满。</para>
    ///
    /// <para><b>象限与层一起记下来</b>：清剿成功那一格要把这一格转成高危生产区
    /// （特殊材料的唯一产地）。单处洞穴虽然只有一处，仍按同一形状记，便于读数与重放。</para>
    /// </summary>
    public RejectReason ClearCave(int sector, int layer)
    {
        if (Occasion != BattleOccasion.None) return RejectReason.InvalidPhase;
        if (layer < 1 || layer > UndergroundSoil.Layers) return RejectReason.InvalidArgs;
        if (!UndergroundSoil.IsCaveCell(sector, layer)) return RejectReason.InvalidTarget;
        if (!_world.Soil.IsDug(sector, layer)) return RejectReason.InvalidPhase;
        if (_world.Soil.IsCleared(sector, layer)) return RejectReason.InvalidPhase;

        var remaining = _world.Soil.CaveMonstersAt(sector, layer);
        if (remaining <= 0) return RejectReason.InvalidPhase;

        _caveSector = sector;
        _caveLayer = layer;
        _caveCleared = false;
        _caveFailReason = CaveFailReason.Timeout;

        Open(BattleOccasion.CaveClearing, ArenaTuning.CaveGameHours * ArenaTime.StepsPerGameHour);

        _sideA.Clear();
        foreach (var u in _world.Roster.Units)
        {
            if (u.CanFight && !u.NonCombatant && u.IsTownsperson) _sideA.Add(u);
        }

        for (var i = 0; i < remaining; i++)
        {
            var jitter = new NavPoint(
                DeckGeometry.CaveSpawn().X + (((i % 3) - 1) * ArenaTuning.SpawnJitterMeters),
                DeckGeometry.CaveSpawn().Z + (((i / 3) - 1) * ArenaTuning.SpawnJitterMeters));
            var monster = _world.Roster.Add($"m{++_monsterNumber}", UnitRole.Monster, jitter);
            _sideB.Add(monster);
        }

        // 清剿的怪一上甲板就朝我方站立的人冲（配对那一段再逐分钟重算）。
        // 受理成功、怪已涌上甲板 —— 清剿开始那一刻发事件。
        CaveClearStarted?.Invoke(sector, layer);
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// 撤退：只对洞穴清剿有效，<b>算失败</b>（伤亡保留、剩下的怪留在洞里）。
    /// 不是清剿场合回 <see cref="RejectReason.InvalidPhase"/>，不做任何破坏性动作。
    /// </summary>
    public RejectReason RetreatCave()
    {
        if (Occasion != BattleOccasion.CaveClearing) return RejectReason.InvalidPhase;

        _caveCleared = false;
        _caveFailReason = CaveFailReason.Retreated;
        EndBattle(_world.Tick);
        return RejectReason.InvalidArgs;
    }

    // ---- 每游戏分钟的推进 ----

    /// <summary>
    /// 推进这一场（跑在走位那一次脉冲<b>之后</b>，读走完之后的位置）。
    /// 非游戏分钟的那几步只走到这里就返回 —— 配对、移动与交手全挂在整游戏分钟上。
    /// </summary>
    public void Step(long tick)
    {
        if (Occasion == BattleOccasion.None) return;

        // 配对、移动与交手全挂在整游戏分钟上：一步一算会让重放段与快照在非整分钟处多出一格抖动。
        if (tick % ArenaTime.StepsPerGameMinute != 0) return;

        if (_world.TownHallBreached)
        {
            _caveCleared = false;
            EndBattle(tick);
            return;
        }

        // 洞穴清剿的收场判据是「洞里那四只全死」，不是时限 ——
        // 这一条排在时限前面：最后一分钟打死最后一只也算清剿成功。
        if (Occasion == BattleOccasion.CaveClearing && RemainingCaveMonsters() == 0)
        {
            _caveCleared = true;
            EndBattle(tick);
            return;
        }

        if (tick >= EndTick)
        {
            _caveCleared = false;
            EndBattle(tick);
            return;
        }

        RePair();
        SettleHits(tick);
        RaidBuildings();
        CountBoard();
    }

    /// <summary>这一场里还活着（未死）的原生怪只数 —— 收场判据与「留在洞里」的读数共用。</summary>
    private int RemainingCaveMonsters()
    {
        var alive = 0;
        foreach (var u in _sideB)
        {
            if (u.State != UnitState.Dead) alive++;
        }

        return alive;
    }

    /// <summary>立刻结束这一场（攻破那一格与手动收尾都走它）。</summary>
    public void ForceEnd(long tick)
    {
        if (Occasion == BattleOccasion.None) return;
        EndBattle(tick);
    }

    /// <summary>
    /// 最终防守被攻破：<b>只收场、不记债、不解救、不毁建筑</b>。
    ///
    /// <para>失败后由玩家选择「立即重试」，世界按 <c>FinalSnapshot</c> 回到战前状态；
    /// 所以这一格不做普通城破那串破坏性后果 —— 做了也会被还原抹掉，
    /// 却会在「不重试」的一小段窗口里留下错误的债与通缉。</para>
    /// </summary>
    public void FailFinalDefence(long tick)
    {
        if (Occasion != BattleOccasion.FinalDefence) return;

        _finalDefenceFailed = true;
        EndBattle(tick);
    }

    /// <summary>状态可查那一口：场合、场上、倒地、死亡、抢走。</summary>
    public string DebugText()
    {
        if (Occasion == BattleOccasion.None) return "none,0,0,0,0";
        return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
            OccasionText(Occasion), OnFieldCount, DownCount, DeadCount, LootedCount);
    }

    /// <summary>场合那一列的文本（无仗时是 <c>none</c>）。</summary>
    public static string OccasionText(BattleOccasion occasion) => occasion switch
    {
        BattleOccasion.Match => "match",
        BattleOccasion.Defence => "defence",
        BattleOccasion.FinalDefence => "finaldefence",
        BattleOccasion.CaveClearing => "cave",
        _ => "none",
    };

    /// <summary>胜负那一列的文本（战报与快照 <c>R</c> 段共用）。</summary>
    public static string OutcomeText(MatchOutcome outcome) => outcome switch
    {
        MatchOutcome.SideAWins => "a_win",
        MatchOutcome.SideBWins => "b_win",
        MatchOutcome.Draw => "draw",
        MatchOutcome.Defeat => "defeat",
        _ => "none",
    };

    /// <summary>
    /// 竞技场菜单「开赛」那一发要的命令参数：<c>"{甲队id,id}|{乙队id,id}"</c>。
    ///
    /// <para><b>挑人口径照 spec §9</b>：<c>Role == Gladiator &amp;&amp; CanFight &amp;&amp;
    /// !LeftTown &amp;&amp; !NonCombatant</c>。<b>按名单序交替分两队</b>（a、b、a、b…），
    /// 允许 2/1 —— 所以三人时甲队两人、乙队一人。</para>
    ///
    /// <para><b>凑不出两队就一个命令都不发</b>：回 false 并把原因写在
    /// <paramref name="reason"/>（「角斗士不足」），由菜单原样弹横幅。</para>
    /// </summary>
    public static bool TryBuildMatchArgs(
        ArenaWorld world, out string args, out string reason)
    {
        ArgumentNullException.ThrowIfNull(world);

        args = string.Empty;
        reason = string.Empty;

        var sideA = new List<string>();
        var sideB = new List<string>();
        var picked = 0;
        foreach (var u in world.Roster.Units)
        {
            if (u.Role != UnitRole.Gladiator || !u.CanFight || u.LeftTown || u.NonCombatant) continue;

            if ((picked & 1) == 0) sideA.Add(u.Id);
            else sideB.Add(u.Id);
            picked++;
        }

        if (picked < 2)
        {
            reason = "角斗士不足";
            return false;
        }

        // 队内分隔符与 ArenaSetup.SplitIds 的解析同一份（逗号）。
        args = string.Join(',', sideA) + "|" + string.Join(',', sideB);
        return true;
    }

    /// <summary>
    /// 伤害那一式：<c>基数 × (攻方战力 ÷ (攻守战力之和)) × 围攻人数</c>。
    /// 守方在在岗牧师 2.0 米圈内那一档再乘 0.5。
    /// </summary>
    public static double DamageOf(double attackerPower, double defenderPower, int attackerCount, bool inPriestAura)
    {
        var share = attackerPower / (attackerPower + defenderPower);
        var damage = ArenaTuning.DamageBase * share * attackerCount;
        return inPriestAura ? damage * ArenaTuning.PriestAuraFactor : damage;
    }

    /// <summary>某一格是否落在某个在岗牧师的光环内（半径 2.0 米）。</summary>
    public static bool InsidePriestAura(NavPoint at, IReadOnlyList<NavPoint> priests)
    {
        foreach (var p in priests)
        {
            if (at.DistanceTo(p) <= ArenaTuning.PriestAuraRadiusMeters) return true;
        }

        return false;
    }

    // === 私产 ===

    private void Open(BattleOccasion occasion, double lengthTicks)
    {
        Occasion = occasion;
        EndTick = _world.Tick + (long)lengthTicks;
        DownCount = 0;
        DeadCount = 0;
        LootedCount = 0;
        _pairs.Clear();
        _enemyTargets.Clear();
        _enemyLoot.Clear();
        _sideA.Clear();
        _sideB.Clear();
        _caveCleared = false;
        _finalDefenceFailed = false;

        // 死伤读数按「开场时的死者 / 伤员名单」取差：上一场留下的尸体不算这一场，
        // 收场扶人之前算出来的数才是这一场真死真伤的人数。
        _deadAtOpen.Clear();
        _woundedAtOpen.Clear();
        foreach (var u in _world.Roster.Units)
        {
            if (u.State == UnitState.Dead) _deadAtOpen.Add(u.Id);
            else if (u.State == UnitState.Wounded) _woundedAtOpen.Add(u.Id);
        }
    }

    private RejectReason Collect(IReadOnlyList<string> ids, List<ArenaUnit> into)
    {
        foreach (var id in ids)
        {
            var unit = _world.Roster.Find(id);
            if (unit is null || !unit.CanFight || unit.LeftTown || unit.IsHelper) return RejectReason.InvalidTarget;
            into.Add(unit);
        }

        into.Sort((a, b) => a.RosterIndex.CompareTo(b.RosterIndex));
        return RejectReason.InvalidArgs;
    }

    private void EnterAt(List<ArenaUnit> side, NavPoint atDeck)
    {
        foreach (var u in side) PlaceUnit(u, atDeck);
    }

    private void EnterPriests(NavPoint? arenaDoor = null)
    {
        // 防守 / 洞穴清剿没有传门点：有竞技场就驻在它场边（可能是新建的 bN），没有就按开局那一栋算。
        if (arenaDoor is null)
        {
            var arenaId = _world.FindByBlueprint("Arena");
            if (arenaId.Length > 0) arenaDoor = _world.DoorDeckOf(arenaId);
        }

        var priests = StandingPriests();
        var edge = DeckGeometry.PriestEdge(arenaDoor);
        for (var i = 0; i < priests.Count && i < ArenaTuning.PriestMaxOnDuty; i++)
        {
            PlaceUnit(priests[i], edge);
        }
    }

    private List<ArenaUnit> StandingPriests()
    {
        var priests = new List<ArenaUnit>();

        // 教堂可能是玩家新建的 bN：先按蓝图找，固定 id 只在开局那一栋还在册时成立。
        var churchId = _world.FindByBlueprint("Church");
        if (churchId.Length == 0) return priests;

        foreach (var id in _world.Roster.PostedTo(churchId))
        {
            var u = _world.Roster.Find(id);
            if (u is null || !u.CanFight) continue;
            priests.Add(u);
            if (priests.Count >= ArenaTuning.PriestMaxOnDuty) break;
        }

        return priests;
    }

    private void PlaceUnit(ArenaUnit unit, NavPoint atDeck)
    {
        var stepper = _world.Roster.DeckStepper;
        stepper.ApplyMove(unit.Id, DeckGeometry.Nav(atDeck.X, atDeck.Z), FacingTo(atDeck));
        stepper.SetCountedInSeparation(unit.Id, true);
    }

    /// <summary>
    /// 防守刷怪落点用的甲板档。<b>钉一阶边长（48 米）那一档，不跟二阶甲板外移</b>：
    /// spec「普通防守」写「沿用现役防守时限与敌人编制」，
    /// 「二阶进阶」的生效表只有移速、采矿倍率与甲板格数三项 —— 刷怪落点不在其列。
    /// 实测：落点跟甲板外移到 56 米边时，主线最终防守第二次尝试的镇政府恰好见底（已按「出入只报告」报用户）。
    /// 出界退场那一路（<see cref="DeckGeometry.NearestDeckEdge"/>）仍跟着当前甲板 —— 那是「界」的语义，不是防守编制。
    /// </summary>
    private static int DefenceSpawnCells => ArenaTuning.DeckCellsTier1;

    private ArenaUnit SpawnEnemy(int index)
    {
        var spawn = DeckGeometry.DefenceSpawnPointAt(index % 4, DefenceSpawnCells);
        var jitter = new NavPoint(
            spawn.X + (((index / 4) % 3 - 1) * ArenaTuning.SpawnJitterMeters),
            spawn.Z + (((index / 12) % 3 - 1) * ArenaTuning.SpawnJitterMeters));
        var enemy = _world.Roster.Add($"e{++_enemyNumber}", UnitRole.Enemy, jitter);
        _enemyLoot[enemy.Id] = 0;
        _enemyTargets[enemy.Id] = PickTarget(index);
        return enemy;
    }

    /// <summary>
    /// 敌人目标分配：<b>按名单序号每 10 人切一段</b> —— 前 5 打镇政府、次 3 冲粮仓、
    /// 末 2 打就近产出建筑。末尾不足 10 人时按同一比例从头取（<c>序号 % 10</c> 自动做到）。
    /// </summary>
    private string PickTarget(int index)
    {
        var slot = index % ArenaTuning.DefenceTargetSegment;
        if (slot < 5) return ArenaWorld.TownHallId;
        if (slot < 8) return ArenaWorld.GranaryId;
        var spawn = DeckGeometry.DefenceSpawnPointAt(index % 4, DefenceSpawnCells);
        return _world.NearestProductionId(new NavPoint(spawn.X, spawn.Z));
    }

    private int SegmentSlotOf(string enemyId)
    {
        foreach (var u in _world.Roster.Units)
        {
            if (u.Id == enemyId) return u.RosterIndex % ArenaTuning.DefenceTargetSegment;
        }

        return 0;
    }

    /// <summary>我方站立参战名单（按名单序号）。</summary>
    private List<ArenaUnit> OurFighters()
    {
        var list = new List<ArenaUnit>();
        foreach (var u in _world.Roster.Units)
        {
            if (u.CanFight && !u.NonCombatant && !u.LeftTown && u.IsTownsperson) list.Add(u);
        }

        return list;
    }

    /// <summary>
    /// 拔锚防守的拦截池：<b>只收距任一我方站立参战人员 ≤ 4.0 米、
    /// 且没抢满 3 件的敌人</b>。其余的继续朝目标建筑门口走。
    /// </summary>
    private List<ArenaUnit> InterceptedEnemies(IReadOnlyList<ArenaUnit> ours)
    {
        var list = new List<ArenaUnit>();
        foreach (var u in _world.Roster.Units)
        {
            if (u.LeftTown || !u.IsBeast || u.State != UnitState.Standing) continue;
            if (LootOf(u.Id) >= ArenaTuning.EnemyStealLimit) continue;

            var at = new NavPoint(u.DeckX, u.DeckZ);
            foreach (var ours1 in ours)
            {
                if (at.DistanceTo(new NavPoint(ours1.DeckX, ours1.DeckZ)) > ArenaTuning.InterceptionRadiusMeters) continue;
                list.Add(u);
                break;
            }
        }

        return list;
    }

    private List<ArenaUnit> HostileSide(IReadOnlyList<ArenaUnit> ours)
    {
        var list = new List<ArenaUnit>();
        if (IsDefence)
        {
            list.AddRange(InterceptedEnemies(ours));
        }
        else
        {
            foreach (var u in _sideB)
            {
                if (u.CanFight && !u.LeftTown) list.Add(u);
            }
        }

        list.Sort((a, b) => a.RosterIndex.CompareTo(b.RosterIndex));
        return list;
    }

    private void RePair()
    {
        _pairs.Clear();
        var ours = Occasion == BattleOccasion.Match
            ? new List<ArenaUnit>(_sideA)
            : OurFighters();
        var side = HostileSide(ours);
        var pairs = Math.Min(ours.Count, side.Count);
        if (pairs == 0)
        {
            foreach (var u in ours)
            {
                if (u.CanFight && !u.LeftTown) HoldUnit(u);
            }

            return;
        }

        var attackers = new List<ArenaUnit>[pairs];
        var defenders = new List<ArenaUnit>[pairs];
        for (var i = 0; i < pairs; i++)
        {
            attackers[i] = [ours[i]];
            defenders[i] = [side[i]];
        }

        // 多出来的人按序号轮流围攻，最多 2 围 1；围不上的就地定身。
        var paired = new HashSet<string>(StringComparer.Ordinal);
        for (var i = pairs; i < ours.Count; i++)
        {
            var slot = i % pairs;
            if (attackers[slot].Count < ArenaTuning.MaxSwarmAgainstOne)
            {
                attackers[slot].Add(ours[i]);
                paired.Add(ours[i].Id);
            }
        }

        for (var i = pairs; i < side.Count; i++)
        {
            var slot = i % pairs;
            if (defenders[slot].Count < ArenaTuning.MaxSwarmAgainstOne)
            {
                defenders[slot].Add(side[i]);
                paired.Add(side[i].Id);
            }
        }

        for (var i = 0; i < pairs; i++)
        {
            var pair = new EngagementPair(i, attackers[i][0], attackers[i], defenders[i]);
            _pairs.Add(pair);
            foreach (var a in attackers[i]) paired.Add(a.Id);
            foreach (var d in defenders[i]) paired.Add(d.Id);

            var leaderDef = new NavPoint(defenders[i][0].DeckX, defenders[i][0].DeckZ);
            var leaderAtk = new NavPoint(attackers[i][0].DeckX, attackers[i][0].DeckZ);
            foreach (var a in attackers[i]) IssueGoTo(a, leaderDef);
            foreach (var d in defenders[i]) IssueGoTo(d, leaderAtk);
        }

        // 配不上对的那些（也含没被拦到的敌人）：进防守那一档就继续朝目标门口走，
        // 其余场合就地定身。
        foreach (var u in ours)
        {
            if (paired.Contains(u.Id)) continue;
            if (IsDefence && u.IsBeast) continue;
            HoldUnit(u);
        }

        foreach (var u in side)
        {
            if (paired.Contains(u.Id)) continue;
            if (IsDefence) continue;   // 漏网的敌人交给 RaidBuildings
            HoldUnit(u);
        }
    }

    private void SettleHits(long tick)
    {
        var priests = new List<NavPoint>();
        if (PriestOnDuty > 0)
        {
            foreach (var p in StandingPriests())
            {
                priests.Add(new NavPoint(p.DeckX, p.DeckZ));
            }
        }

        foreach (var pair in _pairs)
        {
            var attacker = pair.Attackers[0];
            var defender = pair.Defenders[0];
            var at = new NavPoint(attacker.DeckX, attacker.DeckZ);
            var def = new NavPoint(defender.DeckX, defender.DeckZ);
            var touching = at.DistanceTo(def) <= ArenaTuning.HitRangeMeters;
            pair.LastDamage = 0.0;

            if (touching)
            {
                foreach (var d in pair.Defenders)
                {
                    var dmg = DamageOf(
                        attacker.PowerAgainst(d),
                        d.PowerAgainst(attacker),
                        pair.Attackers.Count,
                        InsidePriestAura(new NavPoint(d.DeckX, d.DeckZ), priests));
                    pair.LastDamage += dmg;
                    ApplyDamage(tick, d, dmg);
                }

                foreach (var a in pair.Attackers)
                {
                    var dmg = DamageOf(
                        defender.PowerAgainst(a),
                        a.PowerAgainst(defender),
                        pair.Defenders.Count,
                        InsidePriestAura(new NavPoint(a.DeckX, a.DeckZ), priests));
                    ApplyDamage(tick, a, dmg);
                }

                var dealt = pair.LastDamage / Math.Max(1, pair.Defenders.Count);
                _world.Roster.AddContribution(attacker, dealt);
            }

            // 死伤之后这一对可能已经不成对（有人重伤或死了）：剩的人重新定身。
            if (touching)
            {
                foreach (var d in pair.Defenders)
                {
                    if (d.CanFight) continue;
                    HoldUnit(d);
                }

                foreach (var a in pair.Attackers)
                {
                    if (a.CanFight) continue;
                    HoldUnit(a);
                }
            }
        }

        RiseWounded(tick);
    }

    /// <summary>
    /// 挨一下的那一口 —— <b>交战这一摊唯一的写血口</b>（交手结算、将来的法术与攻城伤害都走它）。
    /// 生死判定就住在这里：<c>Hp ≤ -100</c> 当场死（生成尸体；镇民与来敌每具 3 魂石，洞穴原生怪不给），
    /// <c>-100 &lt; Hp ≤ 0</c> 是重伤退场；洞穴原生怪是例外 —— 归零即死（重伤的怪退出配对，
    /// 就再也挨不到下一击，「四只全灭」会永远差一只）。帮工 <c>Hp</c> 归零直接消散（无尸无魂石）。
    /// </summary>
    public void ApplyDamage(long tick, ArenaUnit target, double amount)
    {
        if (!target.CanFight) return;
        if (amount <= 0.0) return;

        target.Hp -= amount;

        if (target.IsHelper && target.Hp <= 0.0)
        {
            // 帮工归零直接消散：无尸无魂石（那一格 spec 单独写了，因为它与「死」不同账）。
            target.State = UnitState.Dead;
            _world.Roster.ClearPost(target);
            HoldUnit(target);
            DeadCount++;
            return;
        }

        if (target.Hp <= ArenaTuning.DeathHpThreshold)
        {
            Die(target, Occasion != BattleOccasion.None);
            return;
        }

        // 洞穴原生怪被打到 0 血即死，不走「重伤退场」那一档。
        // 为什么：交手只在双方都 `CanFight` 时配对，重伤的怪会立刻退出配对、再也挨不到下一击 ——
        // 而「四只全灭才算清剿」要求它们死。怪不是镇民，没有教堂救治这一说，
        // 留一具重伤的怪躺在甲板上既打不死也清不掉，那一格永远清不了。
        if (target.Role == UnitRole.Monster && target.Hp <= 0.0)
        {
            Die(target, Occasion != BattleOccasion.None);
            return;
        }

        if (target.Hp <= 0.0)
        {
            target.State = UnitState.Wounded;
            target.RiseTick = tick + (ArenaTime.StepsPerGameMinute * ArenaTuning.WoundRiseGameMinutes);
            _world.Roster.ClearPost(target);
            HoldUnit(target);
            DownCount++;
        }
    }

    private void Die(ArenaUnit unit, bool inBattle)
    {
        unit.State = UnitState.Dead;
        _world.Roster.ClearPost(unit);
        _world.AbandonHaulOf(unit.Id);
        HoldUnit(unit);

        // 尸体的载荷记着是谁倒下的 —— 教堂复活那一格靠它把人找回来。
        var corpse = _world.Items.Add(PointItemKind.Corpse, new NavPoint(unit.DeckX, unit.DeckZ), unit.Id);

        // 魂石口径按「是谁的尸体」分：洞穴原生怪（Monster）不给，来敌（Enemy）与镇民照旧每具 3 枚。
        // 洞穴怪是地下长出来的、不是打进来的敌人 —— 给魂石会让「反复开洞」变成刷魂石的路径。
        if (unit.Role != UnitRole.Monster)
        {
            _world.Town.ProduceSoulStone(ArenaTuning.SoulStonesPerCorpse);
        }

        // 仗里倒下的人当场排一张「搬进教堂」的单：甲板上那一圈负面情绪从这一刻起有人在清
        // （这一格是生产代码里第一处会排单的地方 —— 之前队列只被测试排过）。
        // **口径是「死在仗里」**：交手结算里倒下的人走这一条，尸体才有人搬。
        if (inBattle) _world.HaulOrders.RaiseCorpseOrder(corpse);

        DeadCount++;
    }

    private void RiseWounded(long tick)
    {
        var priest = PriestOnDuty > 0;
        foreach (var u in _world.Roster.Units)
        {
            if (u.State != UnitState.Wounded) continue;
            if (!priest) continue;
            if (tick < u.RiseTick) continue;

            u.State = UnitState.Standing;
            u.Hp = Math.Max(1.0, ArenaTuning.UnitMaxHp * 0.5);   // 起身记伤：回一半血
            if (DownCount > 0) DownCount--;
        }
    }

    private void RaidBuildings()
    {
        if (!IsDefence) return;

        foreach (var u in _world.Roster.Units)
        {
            if (!u.IsBeast || u.LeftTown || u.State != UnitState.Standing) continue;

            var loot = LootOf(u.Id);
            if (loot >= ArenaTuning.EnemyStealLimit)
            {
                // 抢满 3 件：朝最近甲板边退场。
                IssueGoTo(u, DeckGeometry.NearestDeckEdge(new NavPoint(u.DeckX, u.DeckZ), _world.DeckCells));
                if (NavArrive.Reached(new NavPoint(u.DeckX, u.DeckZ),
                        DeckGeometry.NearestDeckEdge(new NavPoint(u.DeckX, u.DeckZ), _world.DeckCells),
                        ArenaTuning.HaulArriveToleranceMeters))
                {
                    _world.Roster.LeaveTown(u);
                }

                continue;
            }

            if (!_enemyTargets.TryGetValue(u.Id, out var target)) continue;
            var door = _world.DoorDeckOf(target);
            var at = new NavPoint(u.DeckX, u.DeckZ);
            IssueGoTo(u, door);
            if (at.DistanceTo(door) > ArenaTuning.HitRangeMeters) continue;

            // 每 1 游戏分钟：抢得到就抢一件，抢不到就打 1 点耐久。
            if (StealOne())
            {
                _enemyLoot[u.Id] = loot + 1;
                LootedCount++;
                continue;
            }

            _world.DamageBuilding(target, ArenaTuning.EnemyDurabilityPerGameMinute);
        }
    }

    private bool StealOne()
    {
        var town = _world.Town;
        if (town.Coin >= ArenaTuning.EnemyStealGold && town.TrySpendCoin(ArenaTuning.EnemyStealGold)) return true;
        if (town.Ore >= 1.0 && town.TrySpendOre(1.0)) return true;
        if (town.SpecialMaterial >= 1.0 && town.TrySpendSpecialMaterial(1.0)) return true;
        if (town.SparePart >= 1.0 && town.TrySpendSparePart(1.0)) return true;
        if (town.Food >= 1.0 && town.TrySpendFood(1.0)) return true;
        return false;
    }

    private void CountBoard()
    {
        var onField = 0;
        var down = 0;
        var dead = 0;
        foreach (var u in _world.Roster.Units)
        {
            switch (u.State)
            {
                case UnitState.Wounded:
                    down++;
                    break;
                case UnitState.Dead:
                    dead++;
                    break;
                default:
                    if (u.CanFight && !u.NonCombatant && !u.LeftTown) onField++;
                    break;
            }
        }

        OnFieldCount = onField;
        DownCount = down;
        DeadCount = dead;
    }

    /// <summary>
    /// 收场：拔锚防守的胜负只看「时限到点且镇政府未被破」——
    /// 守住就固定奖励 10 魂石，<b>击杀魂石在 <see cref="Die"/> 那一格已经入账、照常保留</b>；
    /// 余敌一律撤退（离镇），不再按名单序号判死或俘。办赛按贡献读数给个人名声。
    /// 伤员到点就起身（无牧师时躺到仗打完为止）。
    /// <b>手上有一单的搬运人不收场</b> —— 他的走位令留着，在途那一单跑完两程才回到空闲。
    /// </summary>
    private void EndBattle(long tick)
    {
        var occasion = Occasion;
        Occasion = BattleOccasion.None;
        _pairs.Clear();

        var defenceWon = IsDefenceOccasion(occasion)
            && !_finalDefenceFailed
            && !_world.TownHallBreached;

        // 洞穴清剿收场：**只有四只全灭**才把那一格转成高危生产区。
        // 高危那一档每人在岗每 4 游戏小时额外产 1 特殊材料，那是二阶代价里
        // 「特殊材料 10」的唯一产地；超时 / 撤退 / 攻破都不算清完，一格不动。
        //
        // 失败时洞里还剩几只就记几只，并把剩下的怪从甲板上带走（「留在洞穴」不是
        // 「站在甲板上」）—— 下一次发起只涌出这几只，人不用先把上一场的漏网之鱼清掉。
        if (occasion == BattleOccasion.CaveClearing)
        {
            var remaining = RemainingCaveMonsters();
            if (_caveCleared && remaining == 0 && !_world.TownHallBreached)
            {
                _world.Soil.MarkCaveCleared(_caveSector, _caveLayer);
                CaveCleared?.Invoke(_caveSector, _caveLayer);
            }
            else
            {
                _world.Soil.MarkCaveFailed(_caveSector, _caveLayer, remaining);
                CaveClearFailed?.Invoke(_caveSector, _caveLayer, _caveFailReason);
                foreach (var u in _sideB)
                {
                    if (u.State != UnitState.Dead) _world.Roster.LeaveTown(u);
                }
            }

            _caveSector = -1;
            _caveLayer = -1;
            _caveCleared = false;
        }

        var gateIncome = 0.0;
        if (occasion == BattleOccasion.Match)
        {
            gateIncome = _world.SettleMatchIncome();
        }
        // 战报在「扶起伤员」之前写：重伤是这一场的读数，不是收场之后的状态。
        LastReport = BuildReport(occasion, tick, gateIncome);

        // 防守收场：赢的那一场给固定 10 魂石；无论胜负，剩余敌人一律撤退（离镇），
        // 不生成尸体、不折算成角斗士 —— 旧的「按名单序号单死双被俘」到此删除。
        if (IsDefenceOccasion(occasion))
        {
            RetireEnemies();
            if (defenceWon) _world.Town.ProduceSoulStone(ArenaTuning.DefenceWinSoulStones);
        }

        // 伤员起身：牧师在场的回一半，没人管的那一批躺到仗打完为止、起来只剩四分之一血。
        var priest = PriestOnDuty > 0;
        foreach (var u in _world.Roster.Units)
        {
            if (u.State != UnitState.Wounded) continue;
            u.State = UnitState.Standing;
            u.Hp = Math.Max(1.0, ArenaTuning.UnitMaxHp * (priest ? 0.5 : 0.25));
        }

        foreach (var u in _world.Roster.Units)
        {
            if (!u.CanFight || u.LeftTown) continue;

            // 手上有单的搬运人不收场：把搬运那一程的走位令还给他，他跑完两程投进去，
            // 结单那一格自然回到空闲（这一发 Hold 全场会把在途那一单钉死在半路上）。
            if (_world.ResumeHaulOf(u.Id)) continue;

            HoldUnit(u);
        }

        DownCount = 0;
        OnFieldCount = 0;
        _sideA.Clear();
        _sideB.Clear();
        _enemyTargets.Clear();
        _enemyLoot.Clear();

        // 收场的读数都落好之后再报「这一场守住了没有」：订阅方（一局流程）读的是
        // 收场之后的世界，报早了它看到的是「还在打」的那一档。
        if (IsDefenceOccasion(occasion))
        {
            DefenceSettled?.Invoke(defenceWon);
        }

        _finalDefenceFailed = false;
    }

    /// <summary>
    /// 余敌撤退：站着的敌人都离镇（<b>不生成尸体、不给魂石、不折角斗士</b>）。
    /// 已经倒下的那些留在原地 —— 他们的尸体与魂石是这一场打出来的，不随撤退消失。
    /// </summary>
    private void RetireEnemies()
    {
        foreach (var u in _world.Roster.Units)
        {
            if (!u.IsBeast || u.LeftTown) continue;
            if (u.State == UnitState.Dead) continue;
            _world.Roster.LeaveTown(u);
        }
    }

    /// <summary>
    /// 收场那一刻把这一场的读数收成一份 <see cref="BattleReport"/>（只算一次）。
    ///
    /// <para><b>场上人数</b>读的是双方名单里还站着的人数 —— 此刻伤员还没被扶起来，
    /// 「全员倒地记负」因此成立。<b>死伤</b>按开场名单取差，上一场留下的尸体不算这一场。</para>
    /// </summary>
    private BattleReport BuildReport(BattleOccasion occasion, long tick, double gateIncome)
    {
        var standingA = 0;
        foreach (var u in _sideA)
        {
            if (u.CanFight) standingA++;
        }

        var standingB = 0;
        foreach (var u in _sideB)
        {
            if (u.CanFight) standingB++;
        }

        var dead = 0;
        var wounded = 0;
        foreach (var u in _world.Roster.Units)
        {
            if (u.State == UnitState.Dead)
            {
                if (!_deadAtOpen.Contains(u.Id)) dead++;
            }
            else if (u.State == UnitState.Wounded)
            {
                if (!_woundedAtOpen.Contains(u.Id)) wounded++;
            }
        }

        return new BattleReport(
            occasion,
            OutcomeOf(occasion, standingA, standingB),
            _sideA.Count,
            _sideB.Count,
            dead,
            wounded,
            LootedCount,
            gateIncome,
            tick);
    }

    /// <summary>
    /// 胜负口径（spec §9）：<b>全员倒地记负</b>；否则场上人数多的一方胜、相等记平。
    /// 非办赛场合不评胜负。
    /// </summary>
    private static MatchOutcome OutcomeOf(BattleOccasion occasion, int standingA, int standingB)
    {
        if (occasion != BattleOccasion.Match) return MatchOutcome.None;

        // 双方都倒光也是「负」：这一条款必须排在「乙队没人 → 甲队胜」前面。
        if (standingA == 0) return MatchOutcome.Defeat;
        if (standingB == 0) return MatchOutcome.SideAWins;
        if (standingA > standingB) return MatchOutcome.SideAWins;
        if (standingB > standingA) return MatchOutcome.SideBWins;
        return MatchOutcome.Draw;
    }

    private void IssueGoTo(ArenaUnit unit, NavPoint atDeck)
    {
        var stepper = _world.Roster.DeckStepper;
        stepper.SetCountedInSeparation(unit.Id, true);
        stepper.Issue(unit.Id, NavCommand.GoTo(
            DeckGeometry.Nav(atDeck.X, atDeck.Z), ArenaTuning.HaulArriveToleranceMeters));
    }

    private void HoldUnit(ArenaUnit unit)
    {
        var stepper = _world.Roster.DeckStepper;
        var at = stepper.PositionOf(unit.Id);
        stepper.SetCountedInSeparation(unit.Id, false);
        stepper.Issue(unit.Id, NavCommand.Hold(at));
    }

    private static double FacingTo(NavPoint target)
        => Math.Atan2(target.X, target.Z) * (180.0 / Math.PI);
}
