#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Entities;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 在场活物那一本：三类镇民 + 召唤帮工 + 敌人 + 游客 + 怪物。
//
// 三条口径：
//   · **名单序号（RosterIndex）终生不变** —— 手抓摆位、抓放、逃跑、被掳都不改它。
//     防守配对、敌人目标分段、吓跑取人、收购与招募限额全都读它，
//     所以「手抓摆位改变 4 米拦截圈覆盖的位置」这一格才成立。
//   · **生命值只住在单位自己身上**（<see cref="ArenaUnit.Hp"/>），技能层与册子里都不留第二份副本。
//   · **定位与朝向只由寻路层写**：本文件只发命令与读位置，不算下一站。
//     日常走位（<see cref="PeopleRoster.StepDailyRound"/>）也不例外 —— 那一档挑的是「去哪一栋」，
//     「怎么走过去」仍然是寻路层那一件。

/// <summary>在场活物的类别（前三类是镇民，第四类是召唤帮工，后三类是外来活物）。</summary>
public enum UnitRole
{
    /// <summary>工作人员：有日薪、耗口粮、可上采矿与施工岗位。</summary>
    Worker = 0,

    /// <summary>角斗士：有日薪、耗口粮、可上场与上训练场。</summary>
    Gladiator = 1,

    /// <summary>常驻自由人：无薪水、住宿舍。</summary>
    FreePerson = 2,

    /// <summary>召唤帮工（无薪、无口粮、无尸体、<c>Hp</c> 归零即消散）。</summary>
    Helper = 3,

    /// <summary>拔锚防守的来敌。</summary>
    Enemy = 4,

    /// <summary>洞穴涌上甲板的原生怪（对兽战力口径）。</summary>
    Monster = 5,

    /// <summary>在镇游客。</summary>
    Visitor = 6,
}

/// <summary>一个活物的生死状态。</summary>
public enum UnitState
{
    /// <summary>站着（可参战、可派工）。</summary>
    Standing = 0,

    /// <summary>重伤退场（在牧师那 5 个游戏分钟里躺着）。</summary>
    Wounded = 1,

    /// <summary>死（已生成尸体点物件并自动入账魂石）。</summary>
    Dead = 2,
}

/// <summary>
/// 一个在场活物。<b>生命值是唯一一本账</b>（<see cref="Hp"/>）：上限
/// <see cref="ArenaTuning.UnitMaxHp"/>，落在 <c>-100</c> 或更低当场死，落在 <c>-100</c> 与 <c>0</c> 之间是重伤。
/// </summary>
public sealed class ArenaUnit
{
    /// <summary>建一个活物（由 <see cref="PeopleRoster"/> 那几口统一发，不在外面直接 new）。</summary>
    public ArenaUnit(string id, int rosterIndex, UnitRole role, NavPoint atDeck)
    {
        Id = id;
        RosterIndex = rosterIndex;
        Role = role;
        Hp = ArenaTuning.UnitMaxHp;
        DeckX = atDeck.X;
        DeckZ = atDeck.Z;
        PersonalReputation = ArenaTuning.StartingPersonalReputation;

        // 开局三本事：外来活物（怪物与来敌）统一 MonsterStat，其余都是 StartingStat。
        // 开局忠心：三类镇民、帮工、怪物、来敌、游客在这一个构造器里都开成 LoyaltyMax；
        // spec §3.4 的「来路给忠诚初值」那一档不是按类别分的，是按招募来路分的，
        // 由 ArenaSetup.LoyaltyOf(source) 在 Recruit 录取之后覆写（见 ArenaSetup.Recruit）。
        var stat = role is UnitRole.Monster or UnitRole.Enemy
            ? ArenaTuning.MonsterStat
            : ArenaTuning.StartingStat;
        Melee = stat;
        Ranged = stat;
        Riding = stat;
        Loyalty = ArenaTuning.LoyaltyMax;
    }

    /// <summary>单位 id（永不复发：册子那一条）。</summary>
    public string Id { get; }

    /// <summary>名单序号（终生不变）。</summary>
    public int RosterIndex { get; }

    /// <summary>类别。</summary>
    public UnitRole Role { get; set; }

    /// <summary>近战本事。</summary>
    public double Melee { get; set; }

    /// <summary>远程本事。</summary>
    public double Ranged { get; set; }

    /// <summary>骑术本事。</summary>
    public double Riding { get; set; }

    /// <summary><b>生命值，唯一一本账。</b></summary>
    public double Hp { get; set; }

    /// <summary>生死状态。</summary>
    public UnitState State { get; set; } = UnitState.Standing;

    /// <summary>忠心（0..100；敌人与怪物恒 100）。</summary>
    public double Loyalty { get; set; }

    /// <summary>情绪（-100..100；敌人、怪物与游客不记情绪，读作 0）。</summary>
    public double Mood { get; set; }

    /// <summary>当前岗位（建筑 id，或 <c>soil:&lt;象限&gt;_&lt;层&gt;</c>；空串 = 无岗位）。</summary>
    public string Post { get; set; } = string.Empty;

    /// <summary>个人名声（0..1000）。</summary>
    public double PersonalReputation { get; set; }

    /// <summary>是否被 <c>SetNonCombatant</c> 移出参战名单。</summary>
    public bool NonCombatant { get; set; }

    /// <summary>是否已离镇（逃跑、被掳、卖出、游客离镇）：离镇的还在册但不再参战、不再占岗位。</summary>
    public bool LeftTown { get; set; }

    /// <summary>是否被上帝之手抓着。</summary>
    public bool Held { get; set; }

    /// <summary>甲板逻辑坐标的缓存读数（真值在寻路层，每次从那层同步）。</summary>
    public double DeckX { get; set; }

    /// <summary>甲板逻辑坐标的缓存读数（真值在寻路层，每次从那层同步）。</summary>
    public double DeckZ { get; set; }

    /// <summary>
    /// 这一刻有没有在走动 —— <b>意思是「寻路层这一步给它产出了位移」</b>（本步位移 &gt; 0）。
    ///
    /// <para><b>为什么不是「有没有未到达的移动令」</b>：命令那一列会骗人 ——
    /// 走到位之后寻路层把命令收成原地不动，站着的人手上仍可能挂着一条走完的旧令。
    /// 位移那一列才是寻路层自己说的「这一步它真的挪了」。</para>
    ///
    /// <para><b>真值只有一处</b>：<see cref="PeopleRoster.SyncPositions"/> 每步从寻路层读一次，
    /// 与 <see cref="DeckX"/> / <see cref="DeckZ"/> 同一趟同步下来 ——
    /// 交战、游客、搬运、日常走位四头发令的都只改寻路层那一侧，这一栏一律从那里读，
    /// 所以「站着的人在播走路动作」那一类症状不可能从这一列漏出去。</para>
    ///
    /// <para>动作层据此决定播不播走路动作（表现层只读，不反过来改它）。</para>
    /// </summary>
    public bool Moving { get; internal set; }

    /// <summary>重伤起身的到期步号（0 = 还没躺下）。</summary>
    public long RiseTick { get; set; }

    /// <summary>这一场贡献读数（办赛结束后给个人名声的那一档）。</summary>
    public double Contribution { get; set; }

    /// <summary>是不是外来野兽那一类（对兽战力口径）。</summary>
    public bool IsBeast => Role is UnitRole.Monster or UnitRole.Enemy;

    /// <summary>是不是本镇人员（工作人员 / 角斗士 / 常驻自由人）。</summary>
    public bool IsTownsperson => Role is UnitRole.Worker or UnitRole.Gladiator or UnitRole.FreePerson;

    /// <summary>是不是召唤帮工。</summary>
    public bool IsHelper => Role == UnitRole.Helper;

    /// <summary>能不能上场（站着、在册、非外来活物）。</summary>
    public bool CanFight => State == UnitState.Standing && !LeftTown;

    /// <summary>情绪打折系数（<c>1 + 情绪 ÷ 200</c>；情绪 -100 即半折）。</summary>
    public double OutputFactor => 1.0 + (Mood / ArenaTuning.MoodFactorDivisor);

    /// <summary>忠心的七折那一档是否落在区间内。</summary>
    public bool LoyaltyDiscounted => Loyalty >= ArenaTuning.LoyaltyDiscountLow
        && Loyalty <= ArenaTuning.LoyaltyDiscountHigh;

    /// <summary>
    /// 战力 —— <b>权重由守方那一类选</b>：守方是外来野兽就用对兽权重，否则用对人权重。
    /// 忠心落在 30..60 区间再乘七折。
    /// </summary>
    public double PowerAgainst(ArenaUnit defender)
    {
        double power;
        if (defender.IsBeast)
        {
            power = (ArenaTuning.BeastRidingWeight * Riding)
                + (ArenaTuning.BeastMeleeWeight * Melee)
                + (ArenaTuning.BeastRangedWeight * Ranged);
        }
        else
        {
            power = (ArenaTuning.HumanMeleeWeight * Melee)
                + (ArenaTuning.HumanRangedWeight * Ranged)
                + (ArenaTuning.HumanRidingWeight * Riding);
        }

        return LoyaltyDiscounted ? power * ArenaTuning.LoyaltyPowerFactor : power;
    }

    /// <summary>三项本事总和（收购报价那一式读它）。</summary>
    public double StatTotal => Melee + Ranged + Riding;

    /// <summary>类别那一列的文本（快照读数用；不另造词）。</summary>
    public string RoleText => Role switch
    {
        UnitRole.Worker => "worker",
        UnitRole.Gladiator => "gladiator",
        UnitRole.FreePerson => "free",
        UnitRole.Helper => "helper",
        UnitRole.Enemy => "enemy",
        UnitRole.Monster => "monster",
        _ => "visitor",
    };

    /// <summary>生死状态那一列的文本。</summary>
    public string StateText => State switch
    {
        UnitState.Wounded => "wounded",
        UnitState.Dead => "dead",
        _ => "standing",
    };

    /// <summary>岗位那一列的文本（无岗位印 <c>-</c>）。</summary>
    public string PostText => Post.Length == 0 ? "-" : Post;

    /// <summary>单个活物那一段（状态可查那一口，双词段首身份）。</summary>
    public string DebugSegment() => string.Format(CultureInfo.InvariantCulture,
        "U {0} role={1} stats={2:0.##}/{3:0.##}/{4:0.##} hp={5:0.##}/{6:0} state={7} loyalty={8:0.##} mood={9:0.##} post={10} pos={11:0.##},{12:0.##} moving={13}",
        Id, RoleText, Melee, Ranged, Riding, Hp, (int)ArenaTuning.UnitMaxHp, StateText,
        Loyalty, Mood, PostText, DeckX, DeckZ, Moving ? "1" : "0");
}

/// <summary>
/// 在册活物那本 —— 名单序号、岗位表、情绪与忠心的定时结算、逃跑掷点、招募与收购。
///
/// <para><b>逃跑掷点由逻辑种子派生，不靠 <see cref="Random"/></b>：取
/// <c>(种子, 名单序号, 游戏日)</c> 三项混一个整数再折成 0..1。
/// 好处是<b>同种子同序号必然同一个数</b>，与求值次序、运行时实现都无关 ——
/// 重放要的就是这个。</para>
/// </summary>
public sealed partial class PeopleRoster
{
    private readonly NavStepper _deck;
    private readonly EntityCore _entities;
    private readonly IReadOnlyList<NavPoint> _spawnPoints;
    private readonly List<ArenaUnit> _units = [];
    private readonly Dictionary<string, ArenaUnit> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _posts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _unpaidWages = new(StringComparer.Ordinal);
    private readonly Func<string, string> _blueprintOfPost;
    private readonly Func<(int MinCol, int MinRow, int MaxCol, int MaxRow)> _deckBounds;
    private readonly int _seed;
    private int _nextRosterIndex;
    private int _spawnCursor;

    /// <summary>接甲板那条走位流水线与实体册，按给定的落点序列发人。</summary>
    /// <param name="blueprintOfPost">
    /// 建筑 id → 蓝图 id 的查询口（转职清岗要判「新角色站不站得了原岗位」）。
    /// 递空则退回开局六栋的静态表 —— <b>新建的 bN 只有递了建造册那一口才翻得出来</b>。
    /// </param>
    public PeopleRoster(
        NavStepper deck,
        EntityCore entities,
        IReadOnlyList<NavPoint> spawnPoints,
        int seed,
        Func<string, string>? blueprintOfPost = null,
        Func<(int MinCol, int MinRow, int MaxCol, int MaxRow)>? deckBounds = null)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(spawnPoints);
        if (spawnPoints.Count == 0)
        {
            throw new ArgumentException("落点序列是空表 —— 甲板上没有一格站得下，整本册子发不出人",
                nameof(spawnPoints));
        }

        _deck = deck;
        _entities = entities;
        _spawnPoints = spawnPoints;
        _seed = seed;
        _blueprintOfPost = blueprintOfPost ?? BlueprintOfStartingFixture;
        _deckBounds = deckBounds ?? StaticTier1Bounds;
    }

    /// <summary>没递活动边界那一口时的缺省：一阶边界（独立造册的测试与离线工具走这一档）。</summary>
    private static (int MinCol, int MinRow, int MaxCol, int MaxRow) StaticTier1Bounds()
        => (DeckBlueprints.MinCol, DeckBlueprints.MinRow, DeckBlueprints.MaxCol, DeckBlueprints.MaxRow);

    /// <summary>在册活物（按登记序 = 名单序号序）。</summary>
    public IReadOnlyList<ArenaUnit> Units => _units;

    /// <summary>实体册（id 永不复发与稳定遍历次序归它）。</summary>
    public EntityCore Entities => _entities;

    /// <summary>甲板那条走位流水线（发令、落位与定身都走它）。</summary>
    public NavStepper DeckStepper => _deck;

    /// <summary>镇民（工作人员 / 角斗士 / 常驻自由人）在册的，按名单序号。</summary>
    public IEnumerable<ArenaUnit> Townspeople
    {
        get
        {
            foreach (var u in _units)
            {
                if (u.IsTownsperson && !u.LeftTown) yield return u;
            }
        }
    }

    /// <summary>在岗工作人员数。</summary>
    public int WorkerCount => CountRole(UnitRole.Worker);

    /// <summary>在册角斗士数。</summary>
    public int GladiatorCount => CountRole(UnitRole.Gladiator);

    /// <summary>在册常驻自由人数。</summary>
    public int FreePersonCount => CountRole(UnitRole.FreePerson);

    /// <summary>在册帮工数。</summary>
    public int HelperCount => CountRole(UnitRole.Helper);

    /// <summary>某栋楼当前在岗人数。</summary>
    public int PostedCount(string buildingId)
    {
        if (!_posts.TryGetValue(buildingId, out var ids)) return 0;
        var live = 0;
        foreach (var id in ids)
        {
            if (_byId.TryGetValue(id, out var u) && !u.LeftTown && u.State == UnitState.Standing) live++;
        }

        return live;
    }

    /// <summary>某一栋楼的岗位上限（从十三件蓝图规格读；不在册回 0）。</summary>
    public int PostCapacity(string blueprintId)
        => DeckBlueprints.SpecOf(blueprintId)?.Posts ?? 0;

    /// <summary>摘掉某栋楼上所有人（建筑被毁 / 被攻破那一格走它）。</summary>
    public void ClearPostsOf(string buildingId)
    {
        if (!_posts.TryGetValue(buildingId, out var ids)) return;

        foreach (var id in ids)
        {
            if (_byId.TryGetValue(id, out var u)) u.Post = string.Empty;
        }

        _posts.Remove(buildingId);
    }

    /// <summary>按 id 查活物（查不到回 null）。</summary>
    public ArenaUnit? Find(string id) => _byId.TryGetValue(id, out var u) ? u : null;

    /// <summary>按名单序号查活物（查不到回 null）。</summary>
    public ArenaUnit? ByRosterIndex(int rosterIndex)
    {
        foreach (var u in _units)
        {
            if (u.RosterIndex == rosterIndex) return u;
        }

        return null;
    }

    /// <summary>岗位表读数（建筑 id → 那一栋上的人，按名单序号）。</summary>
    public IReadOnlyList<string> PostedTo(string buildingId)
        => _posts.TryGetValue(buildingId, out var ids) ? ids : Array.Empty<string>();

    /// <summary>发一个人（登记 + 寻路层登记 + 落点）。</summary>
    public ArenaUnit Add(string id, UnitRole role, NavPoint? atDeck = null)
    {
        var at = atDeck ?? NextSpawnPoint();
        var unit = new ArenaUnit(id, _nextRosterIndex, role, at);
        _nextRosterIndex++;
        _units.Add(unit);
        _byId[id] = unit;
        _entities.Spawn(id, unit.RoleText);

        _deck.Register(new NavAgentSpec
        {
            Id = id,
            RadiusMeters = BodyRadiusMeters,
            MoveSpeedMetersPerSecond = SpeedOf(role),
            Start = DeckGeometry.Nav(unit.DeckX, unit.DeckZ),
            FacingDegrees = 0.0,
            Group = GroupOf(role),
        });

        return unit;
    }

    /// <summary>发开局那 12 名镇民：8 工作人员 + 3 角斗士 + 1 常驻自由人。</summary>
    public void StartOpeningRoster()
    {
        for (var i = 1; i <= ArenaTuning.StartingWorkers; i++) Add($"w{i}", UnitRole.Worker);
        for (var i = 1; i <= ArenaTuning.StartingGladiators; i++) Add($"g{i}", UnitRole.Gladiator);
        for (var i = 1; i <= ArenaTuning.StartingFreePersons; i++) Add($"f{i}", UnitRole.FreePerson);
    }

    /// <summary>查这个人能不能上这一类岗位（工作人员 / 角斗士那一类上限）。</summary>
    public static bool RoleFitsPost(UnitRole role, string blueprintId) => role switch
    {
        UnitRole.Worker => blueprintId is not ("Arena" or "Stable" or "ArcheryRange"),
        UnitRole.Gladiator => blueprintId is not ("Granary" or "Dormitory" or "TeleportArray"),
        _ => true,
    };

    /// <summary>这一类岗位现在能不能再塞一个人。<paramref name="blueprintId"/> 给出岗位容量与类别。</summary>
    public RejectReason CheckPostCapacity(UnitRole role, string blueprintId)
    {
        var capacity = PostCapacity(blueprintId);
        if (capacity <= 0) return RejectReason.InvalidTarget;
        if (!RoleFitsPost(role, blueprintId)) return RejectReason.InvalidTarget;
        if (PostedCount(blueprintId) >= capacity) return RejectReason.FullCapacity;

        // 岗位容量之外还压一道全镇在册上限：工作人员 30、角斗士 20。
        var headCount = role switch
        {
            UnitRole.Worker => WorkerCount,
            UnitRole.Gladiator => GladiatorCount,
            UnitRole.FreePerson => FreePersonCount,
            _ => HelperCount,
        };

        var headCap = role switch
        {
            UnitRole.Worker => ArenaTuning.WorkerPostCap,
            UnitRole.Gladiator => ArenaTuning.GladiatorPostCap,
            _ => int.MaxValue,
        };

        return headCount >= headCap ? RejectReason.FullCapacity : RejectReason.InvalidArgs;
    }

    /// <summary>把人写进某栋楼的岗位表（原岗位先摘掉）。</summary>
    public void AssignPost(ArenaUnit unit, string buildingId)
    {
        ClearPost(unit);
        unit.Post = buildingId;
        if (!_posts.TryGetValue(buildingId, out var ids))
        {
            ids = [];
            _posts[buildingId] = ids;
        }

        ids.Add(unit.Id);
    }

    /// <summary>摘掉当前岗位（岗位表与那一栏同步清）。</summary>
    public void ClearPost(ArenaUnit unit)
    {
        if (unit.Post.Length == 0) return;

        if (_posts.TryGetValue(unit.Post, out var ids))
        {
            ids.Remove(unit.Id);
            if (ids.Count == 0) _posts.Remove(unit.Post);
        }

        unit.Post = string.Empty;
    }

    /// <summary>
    /// 转身份：岗位表里的位子跟着换类别（转不动那一格返回 false）。
    ///
    /// <para><b>转职清岗</b>：新角色站不了原岗位（<see cref="RoleFitsPost"/> 不成立）时，
    /// 岗位摘掉、人落到脚下那一格 —— 保留岗位会留下「角斗士在粮仓上工」这种不合身的一格。</para>
    /// </summary>
    public bool ConvertRole(ArenaUnit unit, UnitRole target)
    {
        if (unit.Role == target) return true;
        if (unit.Role == UnitRole.Helper) return false;
        if (target is UnitRole.Helper or UnitRole.Enemy or UnitRole.Monster or UnitRole.Visitor) return false;

        var post = unit.Post;
        ClearPost(unit);
        unit.Role = target;

        // 忠诚按新来路重开：买来的与捉来的不同档，转身份就是换了一副出身。
        unit.Loyalty = target == UnitRole.Worker ? 60.0 : 40.0;

        if (post.Length == 0) return true;

        if (!PostFitsRole(target, post))
        {
            SettleOnCurrentCell(unit);
            return true;
        }
        unit.Post = post;
        if (!_posts.TryGetValue(post, out var ids))
        {
            ids = [];
            _posts[post] = ids;
        }

        ids.Add(unit.Id);
        return true;
    }

    /// <summary>
    /// 岗位这一格还合不合新角色的身。
    /// <para><b>为什么要先翻一次 id</b>：<see cref="ArenaUnit.Post"/> 存的是<b>建筑 id</b>（<c>granary</c>），
    /// 而 <see cref="RoleFitsPost"/> 认的是<b>蓝图 id</b>（<c>Granary</c>）。这一翻走
    /// <see cref="_blueprintOfPost"/>（建造册那一口）：开局六栋、玩家新建的 <c>bN</c> 都翻得出来；
    /// 翻不出来（<c>soil:</c> 那几档）就把原串当蓝图 id，不在这里造第二条拒绝规则。</para>
    /// </summary>
    private bool PostFitsRole(UnitRole role, string post)
        => RoleFitsPost(role, _blueprintOfPost(post));

    /// <summary>
    /// 没递建造册时的兜底：开局六栋按 <see cref="DeckBlueprints.StartingFixtures"/> 翻，
    /// 其余原串当蓝图 id（与旧行为逐字相同 —— 新建 bN 那一档只有递了口才做得对）。
    /// </summary>
    private static string BlueprintOfStartingFixture(string post)
    {
        foreach (var (buildingId, blueprintId, _, _) in DeckBlueprints.StartingFixtures)
        {
            if (buildingId == post) return blueprintId;
        }

        return post;
    }

    /// <summary>
    /// 落到脚下那一格 —— 格心 = <c>(格号 + 0.5) × 格长</c>。
    /// <para><b>为什么这一格就是「最近可通行格」</b>：人此刻站着的格按定义是可通行的
    /// （寻路层不会把人放进阻挡格），距离为 0，再往外找一格只会更远。
    /// 这一口只收格心、<b>不猜哪一格被占</b> —— 占格真值在建造/寻路那一侧，册子里没有第二份。</para>
    /// </summary>
    private void SettleOnCurrentCell(ArenaUnit unit)
    {
        if (!_deck.HasAgent(unit.Id)) return;

        var at = _deck.PositionOf(unit.Id);
        var (minCol, minRow, maxCol, maxRow) = _deckBounds();
        var col = Math.Clamp((int)Math.Floor(DeckGeometry.DeckXOf(at) / DeckBlueprints.CellSize),
            minCol, maxCol);
        var row = Math.Clamp((int)Math.Floor(DeckGeometry.DeckZOf(at) / DeckBlueprints.CellSize),
            minRow, maxRow);

        var centerX = (col + 0.5) * DeckBlueprints.CellSize;
        var centerZ = (row + 0.5) * DeckBlueprints.CellSize;
        var nav = DeckGeometry.Nav(centerX, centerZ);

        _deck.ApplyMove(unit.Id, nav, 0.0);
        _deck.Issue(unit.Id, NavCommand.Hold(nav));
        unit.DeckX = centerX;
        unit.DeckZ = centerZ;
    }

    /// <summary>给自由：常驻自由人进宿舍那一档（仍无薪水，只是不再算买来的那一档）。</summary>
    public bool GrantFreedom(ArenaUnit unit)
    {
        if (unit.Role == UnitRole.FreePerson) return true;
        return ConvertRole(unit, UnitRole.FreePerson);
    }

    /// <summary>记一天欠薪（该人这一天没拿到钱）。</summary>
    public void MarkUnpaidWage(ArenaUnit unit) => _unpaidWages[unit.Id] = _unpaidWages.GetValueOrDefault(unit.Id) + 1.0;

    /// <summary>这一场打到哪一步（贡献读数累计；办赛结束给最高的那一位加个人名声）。</summary>
    public void AddContribution(ArenaUnit unit, double amount)
    {
        if (amount > 0.0) unit.Contribution += amount;
    }

    /// <summary>
    /// 每日结算：发薪对市价 → 忠诚增减；欠薪按日扣；忠诚低于 30 按 5% 掷跑。
    /// 回的是<b>这一天要吃多少口粮</b>（每人 1 份）—— 扣粮仓那一口归账本那边，
    /// 扣不动时的 <see cref="RejectReason.InsufficientFood"/> 也归那边判。
    /// </summary>
    /// <param name="tick">当前步号（逃跑掷点按游戏日）。</param>
    /// <param name="wagesPaid">今天实发的工资金额（全镇一份，按人头等分）。</param>
    public double SettleDay(long tick, double wagesPaid)
    {
        var people = new List<ArenaUnit>();
        foreach (var u in _units)
        {
            if (u.IsTownsperson && !u.LeftTown) people.Add(u);
        }

        if (people.Count == 0) return 0.0;

        var day = ArenaTime.GameDaysAt(tick);
        foreach (var u in people)
        {
            // 市价按角色取该角色的日薪（角斗士 3 / 工作人员 2 / 自由人 1）。
            // 三档日薪的数只写一份，就在 ArenaTuning 那一处。
            var market = u.Role switch
            {
                UnitRole.Gladiator => ArenaTuning.GladiatorDailyWage,
                UnitRole.FreePerson => ArenaTuning.FreePersonDailyWage,
                _ => ArenaTuning.WorkerDailyWage,
            };

            var unpaid = _unpaidWages.GetValueOrDefault(u.Id);
            if (unpaid > 0.0)
            {
                u.Loyalty += ArenaTuning.LoyaltyDailyUnpaid;
                u.Mood += ArenaTuning.MoodUnpaidWagePerGameDay;
                _unpaidWages.Remove(u.Id);
            }
            else
            {
                // 全镇实发按人头等分，所以人均那一笔对每个人都一样，
                // 差的是各人的市价（角色不同）。
                //
                // 三档：高于市价 → 忠诚 +2/日；低于市价 3 金以上 → 忠诚 −3/日；中间不动。
                // 下面那一档当前走不到 —— 为什么走不到写在 ArenaTuning.LoyaltyDailyBelow 那一处。
                var perHead = wagesPaid / people.Count;
                var delta = perHead - market;
                if (delta > 0.0) u.Loyalty += ArenaTuning.LoyaltyDailyAbove;
                else if (delta < ArenaTuning.LoyaltyDailyBelow) u.Loyalty += ArenaTuning.LoyaltyDailyBelow;
                else u.Loyalty += ArenaTuning.LoyaltyDailyLevel;
            }

            u.Loyalty = Math.Clamp(u.Loyalty, 0.0, ArenaTuning.LoyaltyMax);
            u.Mood = Math.Clamp(u.Mood, ArenaTuning.MoodMin, ArenaTuning.MoodMax);

            // 逃跑掷点：由（种子, 名单序号, 游戏日）派生，与求值次序和运行时实现都无关。
            if (u.Loyalty < ArenaTuning.LoyaltyFleeThreshold
                && DailyDraw(_seed, u.RosterIndex, day) < ArenaTuning.LoyaltyFleeChancePerGameDay)
            {
                LeaveTown(u);
            }
        }

        return people.Count * ArenaTuning.FoodPerPersonPerGameDay;
    }

    /// <summary>离镇：从名单上摘掉、腾岗位、定身并关避让（寻路层没有注销口，退场只有这一条路）。</summary>
    public void LeaveTown(ArenaUnit unit)
    {
        ClearPost(unit);
        unit.LeftTown = true;
        unit.State = UnitState.Standing;
        var at = _deck.PositionOf(unit.Id);
        _deck.Issue(unit.Id, NavCommand.Hold(at));
        _deck.SetCountedInSeparation(unit.Id, false);
    }

    /// <summary>每游戏小时的结算：情绪自然回升，再叠上调用方递来的那一档（尸体负面情绪圈）。</summary>
    public void SettleHour(Func<NavPoint, double> moodDeltaAt)
    {
        ArgumentNullException.ThrowIfNull(moodDeltaAt);

        foreach (var u in _units)
        {
            if (u.LeftTown) continue;

            u.Mood += ArenaTuning.MoodRecoveryPerGameHour
                + moodDeltaAt(new NavPoint(u.DeckX, u.DeckZ));
            u.Mood = Math.Clamp(u.Mood, ArenaTuning.MoodMin, ArenaTuning.MoodMax);
        }
    }

    /// <summary>
    /// 把定位读数从寻路层同步回来（真值在那一层，这一栏只是读数缓存）。
    /// <para><b>「在不在走动」也在这一趟里读</b>：它与坐标同源（寻路层的移动解），
    /// 分两处读就会出现「坐标是新的、走动标志是旧的」那一格。</para>
    /// </summary>
    public void SyncPositions()
    {
        foreach (var u in _units)
        {
            if (!_deck.HasAgent(u.Id)) continue;
            var at = _deck.PositionOf(u.Id);
            u.DeckX = DeckGeometry.DeckXOf(at);
            u.DeckZ = DeckGeometry.DeckZOf(at);
            u.Moving = _deck.SolutionOf(u.Id).MovedMeters > 0.0;
        }
    }

    /// <summary>
    /// 日常走位 —— 镇民在镇上的例行去处，每 <see cref="ArenaTuning.DailyRoundGameMinutes"/> 个游戏分钟换一次。
    ///
    /// <para><b>三档去处，依据都在已有字段上</b>：
    /// ① <see cref="ArenaUnit.Post"/> 非空 → 那一栋的门口点（上工）；
    /// ② 没岗位的角斗士 → 竞技场门口（练）；
    /// ③ 其余镇民 → 饭店门口（吃）或宿舍门口（歇），按派生掷的那一个数二选一。</para>
    ///
    /// <para><b>择点不掷点</b>：由 <see cref="DailyDraw"/> 从（种子, 名单序号, 档号）混出 0..1，
    /// 同一个三元组永远是同一个去处 —— 同种子两跑逐字相同（重放要的就是这个）。
    /// <b>档号是全局的</b>（第几个 <see cref="ArenaTuning.DailyRoundGameMinutes"/> 游戏分钟），
    /// 不是各自数自己的 —— 全镇同一档换去处，走位才看得出是「换班」而不是「各走各的」。</para>
    ///
    /// <para><b>按名单序号错开，不全镇挤在同一步</b>
    /// （<see cref="ArenaTuning.DailyRoundStaggerSteps"/>）：换去处就是重新求解一次甲板那张网，
    /// 而逐步求解预算是那张网全体共享的。50 个人同一步各换一次，排到预算后面的那几位
    /// 读到的是「目标不可达」，人卡在原地不动 —— 那一档错开之后每人每小时仍换一次，
    /// 只是同一小时内谁先谁后。</para>
    ///
    /// <para><b>让开那三处</b>：<paramref name="deckBusy"/> 为真时这一发整体不发 ——
    /// 交战（<c>Battle.Occasion</c>）、搬运（在途那一单）、游客（在镇那几位）
    /// 在场时，移动权归它们自己：日常走位是空甲板上才做的事。</para>
    ///
    /// <para>只发命令与读位置，不算下一站（「寻路单入口」）。</para>
    /// </summary>
    /// <param name="tick">当前步号。</param>
    /// <param name="deckBusy">甲板上已经有那一摊在管走位（交战 / 搬运 / 游客）。</param>
    /// <param name="doorOf">取某一栋门前第 <c>spread</c> 个可站点（不在册回 null = 那一档走回落）。</param>
    /// <param name="fallback">
    /// 那一栋不在册时的落点（<b>最近可通行格</b>）：递了就不站着不动，没递就保持旧行为（跳过这个人）。
    /// </param>
    public void StepDailyRound(long tick, bool deckBusy, Func<string, int, NavPoint?> doorOf,
        Func<ArenaUnit, NavPoint>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(doorOf);
        if (deckBusy) return;

        var stepsPerRound = ArenaTime.StepsPerGameMinute * ArenaTuning.DailyRoundGameMinutes;
        foreach (var u in _units)
        {
            if (!u.IsTownsperson || u.LeftTown || u.Held) continue;
            if (u.State != UnitState.Standing) continue;

            var mine = tick + (u.RosterIndex * ArenaTuning.DailyRoundStaggerSteps);
            if (mine % stepsPerRound != 0) continue;

            // 门口邻域按名单序号错开：门口格只站得下一个人，全镇都往那一格挤的话
            // 谁也到不了（「日常走位」那一节「让开那三处」同款：自己那一档自己管）。
            var at = DailyDestinationOf(u, mine / stepsPerRound, doorOf, fallback);
            if (at is not { } deck) continue;

            _deck.SetCountedInSeparation(u.Id, true);
            _deck.Issue(u.Id, NavCommand.GoTo(
                DeckGeometry.Nav(deck.X, deck.Z), ArenaTuning.DailyArriveToleranceMeters));
        }
    }

    /// <summary>状态可查那一口：活物段按登记序。</summary>
    public string DebugSegments()
    {
        var lines = new List<string>(_units.Count + 1);
        lines.Add(string.Format(CultureInfo.InvariantCulture,
            "entity count={0} issued={1}", _entities.Count, _entities.Issued));
        foreach (var u in _units) lines.Add(u.DebugSegment());
        return string.Join(" | ", lines);
    }

    /// <summary>活物的移速（米每现实秒）。</summary>
    public static double SpeedOf(UnitRole role) => role switch
    {
        UnitRole.Gladiator => ArenaTuning.GladiatorSpeedMetersPerSecond,
        UnitRole.FreePerson => ArenaTuning.FreePersonSpeedMetersPerSecond,
        UnitRole.Helper => ArenaTuning.HelperSpeedMetersPerSecond,
        UnitRole.Visitor => ArenaTuning.VisitorSpeedMetersPerSecond,
        UnitRole.Enemy or UnitRole.Monster => ArenaTuning.EnemySpeedMetersPerSecond,
        _ => ArenaTuning.WorkerSpeedMetersPerSecond,
    };

    /// <summary>身体半径（米）。1 米一格的甲板上取 0.4：站得下，且不会被挤进墙。</summary>
    public const double BodyRadiusMeters = 0.4;

    /// <summary>
    /// 那一天的逃跑掷点：<b>由（种子, 名单序号, 游戏日）混出来的一个 0..1 定值</b>。
    /// 同一个三元组永远是同一个数 —— 重放逐字可复现靠的就是这一条。
    /// </summary>
    public static double DailyDraw(int seed, int rosterIndex, long gameDay)
    {
        unchecked
        {
            var h = (uint)seed;
            h = (h * 2654435761u) + 0x9E3779B9u;
            h = (h * 2654435761u) + (uint)(rosterIndex * 0x27D4EB2D);
            h = (h * 2654435761u) + (uint)(gameDay * 0x85EBCA6Bu);
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFFFF) / 16777216.0;
        }
    }

    // === 私产 ===

    /// <summary>
    /// 这一档去哪儿（那一栋不在册就回 null —— 这一格站在原地，不乱发一条走不到头的令）。
    ///
    /// <para><b>门前那一小片空地按名单序号错开</b>：<c>doorOf</c> 的第二个参数就是它
    /// （见 <c>ArenaWorld.DoorSpreadDeckOf</c>）。门口格只站得下一个人，
    /// 四栋建筑门前的格子全被抢的话，五十个人一个也到不了。</para>
    ///
    /// <para><b>错开只用名单序号，不掺档号</b>：同一个人的落点是固定的那一格，
    /// 走到位就站定，等下一档再来。掺档号的话每次换档都换一个落点，
    /// 于是全镇永远在走 —— 那一格验「到达之后不再走动」时会一直红。</para>
    /// </summary>
    private NavPoint? DailyDestinationOf(ArenaUnit u, long round, Func<string, int, NavPoint?> doorOf,
        Func<ArenaUnit, NavPoint>? fallback)
    {
        if (u.Post.Length > 0)
        {
            var atPost = doorOf(u.Post, u.RosterIndex);
            if (atPost is not null) return atPost;
        }

        var draw = DailyDraw(_seed, u.RosterIndex, round);
        var buildingId = u.Role == UnitRole.Gladiator
            ? ArenaWorld.ArenaId
            : (draw < ArenaTuning.DailyMealShare
                ? ArenaWorld.RestaurantId
                : ArenaWorld.DormitoryId);

        var at = doorOf(buildingId, u.RosterIndex);
        if (at is not null) return at;

        // 那一栋不在册（宿舍 / 饭店还没建，或刚被拆掉）：回落到最近可通行格，
        // 不报错、也不站着等一条永远不来的走位令。
        return fallback?.Invoke(u);
    }

    private int CountRole(UnitRole role)
    {
        var n = 0;
        foreach (var u in _units)
        {
            if (u.Role == role && !u.LeftTown) n++;
        }

        return n;
    }

    private NavPoint NextSpawnPoint()
    {
        var at = _spawnPoints[_spawnCursor % _spawnPoints.Count];
        _spawnCursor++;
        return at;
    }

    private static int GroupOf(UnitRole role) => role switch
    {
        UnitRole.Gladiator => 2,
        UnitRole.Enemy or UnitRole.Monster => 3,
        UnitRole.Visitor => 4,
        _ => 1,
    };
}
