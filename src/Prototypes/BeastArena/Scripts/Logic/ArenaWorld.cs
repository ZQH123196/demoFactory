#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Base;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Entities;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Replay;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 新版逻辑世界的装配与推进 —— 大地图双城 + 甲板二十四格 + 地下八格 + 三类镇民 +
// 游客 + 搬运队列 + 按分钟自动战斗 + 攻破解救链，八样东西拼成一台核心循环验证机。
//
// 装配次序是契约（`WorldAssembly` 那一件的五步），而**空间登记的先后有讲究**：
// `Grid` / `Stepper` / `AttachTerrain` 三样都绑在**第一个**登记进来的空间上，
// 所以甲板先登记、地形接线挂甲板那张网格，大地图后登记。
//
// 世界只有这一台：空间与建造底座（大地图 + 甲板 + 土壤）、人口、账本、搬运与交战
// 都在同一个构造次序上，不再分「只把底座立起来」的那一份。

/// <summary>逻辑世界的一份配置（开局那一档，种子与起点）。</summary>
public sealed class ArenaConfig
{
    /// <summary>逻辑种子（同种子两跑逐字可复现）。</summary>
    public required int Seed { get; init; }

    /// <summary>开局粮仓食物（手动喂食那一口的原料；日结自动喂食已删）。</summary>
    public double GranaryFood { get; init; } = 500.0;

    /// <summary>开局魂石。</summary>
    public double SoulStones { get; init; } = ArenaTuning.StartingSoulStones;

    /// <summary>开局饱食度。</summary>
    public double Satiation { get; init; } = ArenaTuning.StartingSatiation;

    /// <summary>开局备件。</summary>
    public int SpareParts { get; init; } = ArenaTuning.StartingSpareParts;

    /// <summary>
    /// 一份默认开局配置：除种子外全部取本记录自己的默认值。
    ///
    /// <para>引擎里的 <c>Main</c> 与离线重放 CLI 走同一口 —— 两处各写一遍开局数，
    /// 就是「引擎里那局」与「离线重放那局」开局不同，L3 逐字断言会莫名其妙地红。</para>
    /// </summary>
    public static ArenaConfig Default(int seed) => new() { Seed = seed };
}

/// <summary>
/// 修理这一发为什么发不出去 —— <b>缺备件与没人在岗是两条原因</b>（横幅要写明是哪一条）。
/// <see cref="None"/> = 发得出去。
/// </summary>
public enum RepairBlocker
{
    /// <summary>发得出去。</summary>
    None = 0,

    /// <summary>不在册。</summary>
    UnknownBuilding = 1,

    /// <summary>耐久已满。</summary>
    FullHealth = 2,

    /// <summary>备件不足 1。</summary>
    NoSparePart = 3,

    /// <summary>这一栋岗位上没有人。</summary>
    NoWorker = 4,

    /// <summary>这一栋已经有一处在修的活。</summary>
    AlreadyRepairing = 5,
}

/// <summary>
/// 招募这一发为什么发不出去 —— 置灰与拒绝共用这一档，<see cref="None"/> = 招得下。
///
/// <para><b>判定顺序就是枚举序</b>：先看期别，再用完额度，再看岗位，最后看钱。
/// 招募框行尾只显示第一个命中的那一条（spec §8「置灰判定顺序」）。</para>
/// </summary>
public enum RecruitBlocker
{
    /// <summary>招得下。</summary>
    None = 0,

    /// <summary>不在停靠期。</summary>
    NotDocked = 1,

    /// <summary>本次停靠的 3 人额度用完了。</summary>
    LimitReached = 2,

    /// <summary>这一职业的岗位已满。</summary>
    PostFull = 3,

    /// <summary>金币不够这一档。</summary>
    NoCoin = 4,
}

/// <summary>魔晶的三条主动用途（嵌入归另一张 ticket，不在这里）。</summary>
public enum CrystalUseKind
{
    /// <summary>补魔：至少缺 5 格时恢复 5 格。</summary>
    Mana = 0,

    /// <summary>治疗：立即治好一名重伤居民。</summary>
    Heal = 1,

    /// <summary>修理：立即完成一栋正在修理的建筑。</summary>
    Repair = 2,
}

/// <summary>一次魔晶用途的读数（强类型事件用）。</summary>
/// <param name="Tick">发生在哪一步。</param>
/// <param name="Use">哪一条用途。</param>
/// <param name="TargetId">目标 id（补魔那一档是空串）。</param>
public readonly record struct CrystalUseRecord(long Tick, CrystalUseKind Use, string TargetId);

/// <summary>一次魔晶出售的读数（强类型事件用）。</summary>
/// <param name="Tick">发生在哪一步。</param>
/// <param name="Count">卖了几单位。</param>
/// <param name="UnitPrice">每单位成交价（金币）。</param>
public readonly record struct CrystalSaleRecord(long Tick, int Count, double UnitPrice);

/// <summary>一次门票入账的读数（强类型事件用）。</summary>
/// <param name="Tick">发生在哪一步。</param>
/// <param name="Coin">这一场进了多少金币。</param>
/// <param name="Audience">收场那一刻的在镇观众数。</param>
public readonly record struct TicketIncomeRecord(long Tick, double Coin, int Audience);

/// <summary>一次普通修理扣备件的读数（强类型事件用；走魔晶的立即修好归 <see cref="CrystalUseRecord"/>）。</summary>
/// <param name="Tick">发生在哪一步。</param>
/// <param name="BuildingId">修的是哪一栋。</param>
/// <param name="SpareParts">实际扣了几个备件。</param>
public readonly record struct RepairSpentRecord(long Tick, string BuildingId, double SpareParts);

/// <summary>一次法术治疗扣法力的读数（强类型事件用；走魔晶的治疗归 <see cref="CrystalUseRecord"/>）。</summary>
/// <param name="Tick">发生在哪一步。</param>
/// <param name="UnitId">治的是哪一位。</param>
/// <param name="Mana">实际扣了几格法力。</param>
public readonly record struct HealingSpentRecord(long Tick, string UnitId, double Mana);

/// <summary>
/// 巨兽竞技场 MVP 逻辑世界 —— 「核心循环验证机」那一台。
///
/// <para><b>构造次序是契约</b>：① 订阅推进入口 → ② 建甲板那条走位流水线（第一个登记的空间）
/// → ③ 建大地图那条 → ④ 建建造模块 → ⑤ 接地形接线（挂甲板那张网格）
/// → ⑥ 登记开局 6 栋场地建筑 → ⑦ 建大地图巨兽与地下土壤 → ⑧ 发开局 12 名镇民。
/// ⑤ 早于 ⑥：阻挡从建造事件流里落，接线之前砌的墙这一局永远不在网格里。</para>
///
/// <para><b>每步两段的次序也是契约</b>：本类订阅的那一段排在走位那一次脉冲<b>之前</b>
/// （饱食度衰减、移速改令、法力恢复、日结与电梯都在这一段）；
/// <see cref="OnAfterStep"/> 排在其<b>之后</b>（同步定位、每分钟交战与游客都读走完之后的位置）。</para>
///
/// <para><b>一处地形决定</b>：镇政府、竞技场、教堂这三件<b>不落阻挡</b>。
/// spec 给的竞技场入场点 <c>(-1.5, -6)</c> 与 <c>(1.5, -6)</c>、镇政府电梯口 <c>(0, 2)</c>
/// 都在这三件的地盘上，而建造接线把每一栋都封成阻挡 —— 人就永远进不去，
/// 办赛与洞穴清剿两场仗会安静地变成空转。建造接线只认格名单不认「哪一栋可进」，
/// 所以这一头用寻路层那唯一的写口把三件的格重新开回来（<see cref="OpenWalkableBuildings"/>）。</para>
/// </summary>
public sealed partial class ArenaWorld : GameLogicBase, IReplayWorld
{
    private readonly List<NavPoint> _spawnPoints = [];
    private readonly GridPlacementRule _deckRule;
    private readonly Dictionary<string, HaulInHand> _carrying = new(StringComparer.Ordinal);
    private readonly HashSet<string> _capturedHelpers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long DoneTick, string PersonId, string CorpseId)> _reviving = [];
    private readonly Dictionary<string, int> _soilCrew = [];
    private readonly List<HaulLanding> _haulLandings = [];
    private readonly List<StarSaleRecord> _starSales = [];
    private int _appliedCollapseCount;
    private long _lastHourTick;
    private long _lastDayTick;
    private long _lastElevatorTick;
    private long _lastBreachCheckTick;

    /// <summary>
    /// 这一场最终防守已经被攻破过（<b>只用来拦住同一格每分钟重复报攻破</b>）。
    /// 最终防守不走普通城破的记债与解救；失败后由重试把世界按战前快照还原，
    /// 还原那一格会把这个标记清掉。
    /// </summary>
    private bool _finalDefenceCollapsed;

    /// <summary>最终防守的战前全量快照（只在第一次进最终防守时取一份）。</summary>
    private FinalSnapshot? _finalSnapshot;

    /// <summary>造一台新世界。装配次序见类型注释那一条。</summary>
    public ArenaWorld(ArenaConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;

        // 成长那一件的第二个解锁条件（暴食态）要问「已驶离贫城没有」——
        // 判据由世界递进去，成长件自己不读位置：它读的是局流程的段（见 DepartedPoorCity）。
        // 谓词是延迟求值，建成长件时流程还未建（流程要读成长件），调用时一定已经建好。
        Growth = new BeastGrowth(() => Flow?.DepartedPoorCity ?? false);

        // ① 本世界的推进入口：排在两条走位流水线之前（订阅先后 = 执行先后）。
        SubscribePulse(OnBeforeStep);

        // ② 甲板：第一个登记的空间 → Grid / Stepper / AttachTerrain 三样都落在它身上。
        DeckGrid = BuildDeckGrid();
        DeckStepper = Assembly.BuildNavPipeline(new NavPipelineSpec
        {
            Space = DeckSpace,
            Grid = DeckGrid,
            Budget = new NavSolveBudget { PerRequestExpansions = 4000 },
            Recompute = new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            CacheCapacity = 64,
            RecordCapacity = 64,
            TrackedRequestCapacity = 32,
            Move = new NavMoveTuning
            {
                StepSeconds = ArenaTime.SecondsPerStep,
                LookAheadMeters = 2.0,
                StepSolveExpansions = 20000,
            },
            Gate = null,
            Separation = new NavSeparationTuning { MinNetStepMeters = 0.02 },
        });

        // ③ 大地图：第二个登记的空间（查得用 StepperOf，不读 Grid / Stepper 那两个属性）。
        MapGrid = BigMapGrid.Build();
        MapStepper = Assembly.BuildNavPipeline(new NavPipelineSpec
        {
            Space = BigMapGrid.Space,
            Grid = MapGrid,
            Budget = new NavSolveBudget { PerRequestExpansions = 4000 },
            Recompute = new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            CacheCapacity = 64,
            RecordCapacity = 64,
            TrackedRequestCapacity = 32,
            Move = new NavMoveTuning
            {
                StepSeconds = ArenaTime.SecondsPerStep,
                LookAheadMeters = 2.0,
                StepSolveExpansions = 20000,
            },
            Gate = null,
            Separation = null,
        });

        // ④ 建造模块：网格放置规则 + 十三件蓝图。归零去向那一条由本原型的规则答（RemoveOnZero）。
        // 规则开局按一阶边界建，二阶扩容走规则自己的外扩口（ApplyDeckTier）。
        _deckRule = DeckBlueprints.BuildRule(ArenaTuning.DeckCellsTier1);
        Construction = Assembly.BuildConstruction(_deckRule, DeckBlueprints.Build(),
            RemoveOnZero);

        // ⑤ 地形接线：挂甲板那张网格。
        Terrain = Assembly.AttachTerrain(DeckBlueprints.ToNavCell);

        // ⑥ 开局 6 栋场地建筑（完工态登记，不进 Journal）。
        Deck = new DeckConstruction(Construction, IsDeckCellInBounds);
        Deck.RegisterStartingFixtures();
        OpenWalkableBuildings();

        // ⑦ 巨兽生态 + 大地图航行 + 地下土壤 + 一局流程。生态先建：航行与饱食度都要读它；
        // 成长那一件先建：航行的两个解锁计数住在它里面，流程那一件也读它
        // （位阶与暴食态解锁是双成长的两项）。
        Ecology = new BeastEcology(BigMapGrid.EcoNodeSpecs);
        Voyage = new BeastVoyage(Growth, Ecology, Assembly.StepperOf(BigMapGrid.Space), () => Tick);
        Soil = new UndergroundSoil();
        Flow = new RunFlow(Growth);

        // 魔晶落甲：生态只记账，落成点位物与排搬运单是世界的事。
        Ecology.CrystalsSpawned += SpawnCrystals;

        // 成长的两个派生后果挂在事件上，而不是散在推进那一段：
        // 位阶换移速、形态换食性与衰减系数。事件订阅在构造期，排在表现层之前。
        // 位阶还换甲板：二阶进阶完成那一刻（仪式到点抛 TierAdvanced）甲板从 24 格扩到 28 格，永久生效。
        // 位阶还换兽半宽：停靠点跟着外扩（一阶 26.45 → 二阶 30.00 米），还停靠的兽要走到新停靠点，
        // 不留在旧档那一点压城。仪式只在停靠期开得起来、仪式期间拔不了锚，所以这一刻读到的就是「仍停靠」。
        Growth.TierAdvanced += tier =>
        {
            Voyage.SetTierSpeedFactor(ArenaTuning.StatsOf(tier).SpeedFactor);
            ApplyDeckTier(tier);
            if (IsDocked && !PulledAnchor) Voyage.RedockAtCurrentTier();
        };
        Growth.FormSwitched += _ => ApplyFormFactors();

        // ⑧ 账本、活物册、游客、搬运、电梯、交战。
        Town = new TownAccounts();
        Town.AddCreditor(PoorFactionId, "贫城行会", [BigMapGrid.PoorCity.Id]);
        Town.AddCreditor(RichFactionId, "富城行会", [BigMapGrid.RichCity.Id]);
        Town.AddCreditor(DesertFactionId, "沙城行会", [BigMapGrid.DesertCity.Id]);
        Entities = new EntityCore(tick: () => Tick);
        BuildSpawnPoints();
        Roster = new PeopleRoster(DeckStepper, Entities, _spawnPoints, config.Seed, Deck.BlueprintIdOf,
            () => DeckCellBounds);
        Roster.StartOpeningRoster();

        // 开局就挂着今天那一份工钱：发薪是停靠期手动发一次的事（spec §「账本面板 · 发薪」），
        // 工资表从第一秒起就是「欠着的」，不是等到日结才凭空冒出来。
        WagesDue = WageBill();
        Items = new PointItems();
        Haul = new HaulQueue();
        Elevator = new MagicElevator { TownHallId = TownHallId };
        Visitors = new VisitorPool(Roster, config.Seed);
        Battle = new BattleSim(this);
        HaulOrders = new HaulOrders(this);

        // 防守收场报到流程：赢了才推段、城破（没赢）段停在原地。
        // 挂在这里而不是让表现层轮询，是因为「这一场赢没赢」只有收场那一格知道
        // （到点结束与攻破提前结束都走 EndBattle，之后的读数已经不带胜负了）。
        Battle.DefenceSettled += OnDefenceSettled;

        // 结算那一格：本件排在表现层之前订阅 —— 事件弹出来时结算已经在世界上。
        Flow.SettlementOpened += CaptureSettlement;

        Voyage.AddGranaryFood(config.GranaryFood);
        Voyage.SetSatiation(config.Satiation);
    }

    /// <summary>甲板那个寻路空间的名字。</summary>
    public static SpaceId DeckSpace { get; } = new("甲板");

    /// <summary>开局那份配置。</summary>
    public ArenaConfig Config { get; }

    /// <summary>逻辑种子（逃跑掷点、游客停留时长都由它派生）。</summary>
    public int Seed => Config.Seed;

    // ---- 装配产物 ----

    /// <summary>甲板那张网格（恒 28×28 格按最大档建；一阶时外圈两格带阻挡）。</summary>
    public NavGrid DeckGrid { get; }

    /// <summary>甲板那条走位流水线。</summary>
    public NavStepper DeckStepper { get; }

    /// <summary>大地图那张 20×20 格网格。</summary>
    public NavGrid MapGrid { get; }

    /// <summary>大地图那条走位流水线。</summary>
    public NavStepper MapStepper { get; }

    /// <summary>
    /// 按蓝图 id 查建筑 id（<b>先找固定 id，找不到再按蓝图找</b>）——
    /// 酒店 / 宿舍 / 传送阵 / 教堂这几处的消费者都走它，新建的 <c>bN</c> 因此可用，
    /// 固定 id 那一栋拆掉之后自动回退到同蓝图的新建那一栋。
    /// </summary>
    public string FindByBlueprint(string blueprintId) => Deck.FindByBlueprint(blueprintId);

    /// <summary>「建筑 id 或蓝图 id」→ 在册建筑 id（日常走位那一侧递的是固定 id）。</summary>
    public string ResolveBuildingId(string buildingIdOrBlueprintId)
        => Deck.ResolveBuildingId(buildingIdOrBlueprintId);

    /// <summary>
    /// 这一栋现在能不能修（<b>只读口</b>，菜单置灰用，不改写路径）：
    /// 耐久未满、备件 ≥ 1、在岗 ≥ 1，且没有一处在修的活。
    /// </summary>
    public bool CanRepair(string buildingId) => RepairBlockerOf(buildingId) == RepairBlocker.None;

    /// <summary>
    /// 修理发不出去的原因（横幅文案的分类依据）：<b>缺备件与没人在岗分开报</b>，
    /// 不让 UI 从一句笼统的拒绝里猜。
    /// </summary>
    public RepairBlocker RepairBlockerOf(string buildingId)
    {
        var building = Construction.Find(buildingId);
        if (building is null) return RepairBlocker.UnknownBuilding;
        if (building.Health >= building.MaxHealth) return RepairBlocker.FullHealth;
        if (Town.SparePart < ArenaTuning.RepairSpareParts) return RepairBlocker.NoSparePart;
        if (Roster.PostedCount(buildingId) <= 0) return RepairBlocker.NoWorker;
        if (Deck.IsRepairing(buildingId)) return RepairBlocker.AlreadyRepairing;
        return RepairBlocker.None;
    }

    /// <summary>建造模块（甲板那一本册）。</summary>
    public ConstructionCore Construction { get; }

    /// <summary>地形接线。</summary>
    public NavTerrainLink Terrain { get; }

    /// <summary>甲板建造动作。</summary>
    public DeckConstruction Deck { get; }

    /// <summary>巨兽在大地图上的航行状态机。</summary>
    public BeastVoyage Voyage { get; }

    /// <summary>地下 8 格土壤（4 象限 × 2 层）。</summary>
    public UndergroundSoil Soil { get; }

    /// <summary>巨兽成长（位阶、形态、两个解锁计数、进阶仪式）。</summary>
    public BeastGrowth Growth { get; }

    /// <summary>巨兽生态（饱食度、消化进度、生态对象、魔晶产出）。</summary>
    public BeastEcology Ecology { get; }

    /// <summary>
    /// 「已驶离贫城」的当前读数（暴食态解锁的第二道门）。
    /// <b>判据在局流程那一件</b>：<see cref="RunFlow.DepartedPoorCity"/> 答「已经进入过
    /// <see cref="RunStage.ToSand"/> 那一程没有」—— 贫城防守赢了才驶得出去，
    /// 光是下一条离城航行令不算。本类只转这一口，不另造位置判据。
    /// </summary>
    public bool DepartedPoorCity => Flow.DepartedPoorCity;

    /// <summary>攻破、停摆、待记的债、战损与被俘名单、解救。</summary>
    public SiegeState Siege { get; } = new();

    /// <summary>一局流程：段、目标、下一站、四个里程碑、双成长完成与终局资格。</summary>
    public RunFlow Flow { get; }

    // ---- 人口与账本 ----

    /// <summary>镇上的账（六本实物 + 按债主分户的负债 + 法力格）。</summary>
    public TownAccounts Town { get; }

    /// <summary>实体册（id 永不复发、稳定遍历次序）。</summary>
    public EntityCore Entities { get; }

    /// <summary>在册活物。</summary>
    public PeopleRoster Roster { get; }

    /// <summary>静止点位物（尸体 / 散货 / 魔晶）。</summary>
    public PointItems Items { get; }

    /// <summary>三级优先搬运队列。</summary>
    public HaulQueue Haul { get; }

    /// <summary>
    /// 三级搬运单的四个产地（生产代码里唯一调 <see cref="HaulQueue.Enqueue"/> 的那一头）。
    /// </summary>
    public HaulOrders HaulOrders { get; }

    /// <summary>镇政府魔法电梯。</summary>
    public MagicElevator Elevator { get; }

    /// <summary>游客池与在镇游客。</summary>
    public VisitorPool Visitors { get; }

    /// <summary>按游戏分钟自动战斗（三种场合共用）。</summary>
    public BattleSim Battle { get; }

    /// <summary>在场游手派活的写口（召唤帮工、发令都走它）。</summary>
    public IReadOnlyList<ArenaUnit> Units => Roster.Units;

    // ---- 工资（发薪那一摊） ----

    /// <summary>
    /// 应发工资（金币）—— 日结按角色累计，没发就一直挂着。
    ///
    /// <para><b>钱在这一格，不在金币账里</b>：发薪那一刻才从金币账扣
    /// （<see cref="TownAccounts.TrySpendCoin"/>），挂着的时候一枚金币也没动 ——
    /// 于是「欠着」与「付掉了」在账上是两件能分开看的事。</para>
    /// </summary>
    public double WagesDue { get; private set; }

    /// <summary>
    /// 欠薪天数 —— 一天没发就往上堆；发清归零。
    ///
    /// <para>每一个欠薪日按 <see cref="ArenaTuning.LoyaltyDailyUnpaid"/> 扣一次忠心（最低 0）。</para>
    /// </summary>
    public int ArrearsDays { get; private set; }

    /// <summary>本日实发工资（金币）—— 日结时按人头分摊用；日结完清零。</summary>
    private double WagesPaidThisDay;

    // ---- 几个钉死的建筑 id ----

    /// <summary>镇政府那一栋。</summary>
    public const string TownHallId = "town_hall";

    /// <summary>竞技场那一栋。</summary>
    public const string ArenaId = "arena";

    /// <summary>教堂那一栋。</summary>
    public const string ChurchId = "church";

    /// <summary>粮仓那一栋。</summary>
    public const string GranaryId = "granary";

    /// <summary>宿舍那一栋。</summary>
    public const string DormitoryId = "dormitory";

    /// <summary>酒店那一栋。</summary>
    public const string HotelId = "hotel";

    /// <summary>饭店那一栋。</summary>
    public const string RestaurantId = "restaurant";

    /// <summary>工坊那一栋。</summary>
    public const string WorkshopId = "workshop";

    /// <summary>传送阵那一栋。</summary>
    public const string TeleportArrayId = "teleport_array";

    /// <summary>贫城那家债主势力（名下城镇：贫城）。</summary>
    public const string PoorFactionId = "poor_town";

    /// <summary>富城那家债主势力（名下城镇：富城）。</summary>
    public const string RichFactionId = "rich_town";

    /// <summary>沙城那家债主势力（名下城镇：沙城；2026-10-19 加第三座城时立）。</summary>
    public const string DesertFactionId = "desert_town";

    /// <summary>不打防守目标的四栋产出建筑（酒店、饭店、工坊 + 空船停靠区那一档）。</summary>
    public static readonly string[] ProductionBlueprintIds = ["Hotel", "Restaurant", "Workshop", "AirshipDock"];

    // ---- 阶段与账 ----

    /// <summary>当前停靠的城市 id（空串 = 在航）。</summary>
    public string DockedCityId { get; private set; } = string.Empty;

    /// <summary>现在是不是停靠着。</summary>
    public bool IsDocked => DockedCityId.Length > 0;

    /// <summary>当前停靠点（不在停靠点回 null）。</summary>
    public MapPlace? DockedPlace
        => DockedCityId.Length == 0 ? null : BigMapGrid.PlaceById(DockedCityId);

    /// <summary>本次停靠已经招了几个人（停靠抵达那一刻清零）。</summary>
    public int RecruitedThisDock { get; private set; }

    /// <summary>本次停靠还剩几个招募额度 —— 招募框行尾与 <see cref="CanRecruit"/> 都读这一口。</summary>
    public int RecruitLimitLeft => Math.Max(0, ArenaTuning.RecruitPerDock - RecruitedThisDock);

    /// <summary>
    /// 这一发招不招得下，以及第一个命中的原因（<see cref="RecruitBlocker.None"/> = 招得下）。
    ///
    /// <para><b>只读口</b>：招募框按它置灰，写路径 <c>ArenaSetup.Recruit</c> 也读它 ——
    /// 判定顺序只有一份，UI 与拒绝不各写一遍。</para>
    /// </summary>
    public RecruitBlocker CanRecruit(RecruitSource source)
    {
        if (!IsDocked) return RecruitBlocker.NotDocked;
        if (RecruitLimitLeft <= 0) return RecruitBlocker.LimitReached;

        var role = ArenaSetup.RoleOf(source);
        var cap = role == UnitRole.Worker ? ArenaTuning.WorkerPostCap : ArenaTuning.GladiatorPostCap;
        var head = role == UnitRole.Worker ? Roster.WorkerCount : Roster.GladiatorCount;
        if (head >= cap) return RecruitBlocker.PostFull;

        if (ArenaSetup.RecruitCost(source) > Town.Coin) return RecruitBlocker.NoCoin;
        return RecruitBlocker.None;
    }

    /// <summary>招到一个人之后记一笔额度（只由 <c>Recruit</c> 写路径调）。</summary>
    public void NoteRecruited() => RecruitedThisDock++;

    /// <summary>被通缉没有（债超阈值、或攻破后第 7 游戏日期满未还清即置通缉）。</summary>
    public bool Wanted { get; private set; }

    /// <summary>
    /// 这座城市是不是归某家「真有欠账」的债主势力所有 —— 通缉期拒停的<b>唯一口径</b>。
    ///
    /// <para><b>只读口</b>：<c>Dock</c> 动词与命令栏的置灰都读这一处，规则只有一份。
    /// 「债主势力」是真有欠账的那一家，不是「登记在册的债主」—— 开局两家都登记在册，
    /// 按登记判会让两座城一起拒停，「其余城镇照常停靠」那一格就永远验不到。</para>
    /// </summary>
    public bool IsOwnedByCreditor(string cityId)
    {
        foreach (var c in Town.Creditors)
        {
            if (c.Outstanding <= 0.0) continue;

            foreach (var t in c.TownIds)
            {
                if (t == cityId) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 这一座城现在能不能停靠（<b>只读口</b>）：通缉期只拒债主势力名下的城，
    /// 其余照常。动词与命令栏都读这一处，不各自再拼一遍条件。
    /// </summary>
    public bool CanDockTo(string cityId) => !Wanted || !IsOwnedByCreditor(cityId);

    /// <summary>
    /// 「派到此格」的<b>只读判据</b>（在册 / 活着 / 是工作人员 / 那一格已挖开）。
    /// 面板与 <c>AssignPost</c> 动词都只读这一处，不各写一份。
    /// </summary>
    public SoilAssignBlocker SoilAssignBlockerOf(string personId, int sector, int layer)
    {
        var identity = SoilAssignBlockerOf(personId);
        if (identity != SoilAssignBlocker.None) return identity;

        if (sector < 0 || sector >= UndergroundSoil.Sectors
            || layer < 1 || layer > UndergroundSoil.Layers)
        {
            return SoilAssignBlocker.OutOfRange;
        }

        return Soil.IsDug(sector, layer) ? SoilAssignBlocker.None : SoilAssignBlocker.NotDug;
    }

    /// <summary>「派到此格」里只管人那一半的只读判据（提示行用，不看格状态）。</summary>
    public SoilAssignBlocker SoilAssignBlockerOf(string personId)
    {
        if (personId.Length == 0) return SoilAssignBlocker.NotInRoster;

        var unit = Roster.Find(personId);
        if (unit is null || unit.LeftTown) return SoilAssignBlocker.NotInRoster;
        if (unit.State == UnitState.Dead) return SoilAssignBlocker.Dead;
        return unit.Role == UnitRole.Worker ? SoilAssignBlocker.None : SoilAssignBlocker.NotWorker;
    }

    /// <summary>这一格现在能不能派这个人去（<see cref="SoilAssignBlockerOf(string,int,int)"/> 的布尔口）。</summary>
    public bool CanAssignSoil(string personId, int sector, int layer)
        => SoilAssignBlockerOf(personId, sector, layer) == SoilAssignBlocker.None;

    // ---- 攻破与停摆（状态住在 SiegeState，这里只转发公开读数） ----

    /// <summary>攻破那一刻的步号（没攻破过 = -1）。</summary>
    public long BreachTick => Siege.BreachTick;

    /// <summary>攻破折出来的债额（还没记进负债账，等城卫队解救那一刻记）。</summary>
    public double PendingDebt => Siege.PendingDebt;

    /// <summary>停摆期到哪一步（没在停摆 = 0）。</summary>
    public long StandstillUntilTick => Siege.StandstillUntilTick;

    /// <summary>现在是不是在停摆期。</summary>
    public bool InStandstill => Siege.InStandstillAt(Tick);

    /// <summary>当前位阶（开局一阶）。</summary>
    public BeastTier Tier => Growth.Tier;

    /// <summary>进阶仪式到哪一步（没在仪式 = 0）。</summary>
    public long RitualUntilTick => Growth.RitualUntilTick;

    /// <summary>现在是不是在进阶仪式里（与停摆期分开：快照要分得清那两件事）。</summary>
    public bool InRitual => Growth.InRitualAt(Tick);

    /// <summary>
    /// 位阶进阶到点时抛一次（表现层出横幅用）。
    /// <b>被拒的那几发不发，一次进阶只发一次</b>。
    /// </summary>
    public event Action<BeastTier>? TierAdvanced
    {
        add => Growth.TierAdvanced += value;
        remove => Growth.TierAdvanced -= value;
    }

    /// <summary>当前形态（开局原始态，没有出身差别）。</summary>
    public BeastForm Form => Growth.Form;

    /// <summary>
    /// 形态真变时抛一次（表现层出横幅用）。
    /// <b>形态确实变了才发</b>：未解锁、参数不认识、切到当前形态（幂等）都不发。
    /// </summary>
    public event Action<BeastForm>? FormSwitched
    {
        add => Growth.FormSwitched += value;
        remove => Growth.FormSwitched -= value;
    }

    /// <summary>
    /// 这个形态已经解锁没有。原始态开局即有；另两种靠计数越过阈值那一刻上锁。
    ///
    /// <para><b>「已解锁」是独立的永久标记</b>，不是每次现算的派生值 ——
    /// 离开沙漠之后（累计计时不清零），荒漠态仍然是已解锁的。</para>
    /// </summary>
    public bool FormUnlocked(BeastForm form) => Growth.FormUnlocked(form);

    /// <summary>某个形态第一次解锁时抛一次（生态那一摊的强类型事件）。</summary>
    public event Action<BeastForm>? FormUnlockedEvent
    {
        add => Growth.FormUnlockedEvent += value;
        remove => Growth.FormUnlockedEvent -= value;
    }

    /// <summary>一口吞食开始（第二项 = 自主觅食还是玩家指定）。</summary>
    public event Action<EcoNode, bool>? EatStarted
    {
        add => Voyage.EatStarted += value;
        remove => Voyage.EatStarted -= value;
    }

    /// <summary>一口吞食完成（按实际补进量记）。</summary>
    public event Action<EatResult>? EatCompleted
    {
        add => Ecology.EatCompleted += value;
        remove => Ecology.EatCompleted -= value;
    }

    /// <summary>吞食被拒（动词那一侧拒的也走这一口）。</summary>
    public event Action<string, RejectReason>? EatRejected;

    /// <summary>自主觅食脱队开始。</summary>
    public event Action<EcoNode>? ForageStarted
    {
        add => Voyage.ForageStarted += value;
        remove => Voyage.ForageStarted -= value;
    }

    /// <summary>玩家召回或吃完返航。</summary>
    public event Action? ForageRecalled
    {
        add => Voyage.ForageRecalled += value;
        remove => Voyage.ForageRecalled -= value;
    }

    /// <summary>附近没有可吞对象、进入一游戏小时冷却（载荷是封闭枚举，文案由 <see cref="ForageGiveUpText"/> 派生）。</summary>
    public event Action<ForageGiveUpReason>? ForageGaveUp
    {
        add => Voyage.ForageGaveUp += value;
        remove => Voyage.ForageGaveUp -= value;
    }

    /// <summary>消化进度跨过 100 的整倍数、落了几单位魔晶到甲板。</summary>
    public event Action<int>? CrystalsProduced;

    /// <summary>一件魔晶搬进镇政府仓库（那件点位物的 id）。</summary>
    public event Action<string>? CrystalWarehoused;

    /// <summary>一次魔晶用途落地。</summary>
    public event Action<CrystalUseRecord>? CrystalUsed;

    /// <summary>一次魔晶出售落地。</summary>
    public event Action<CrystalSaleRecord>? CrystalSold;

    /// <summary>魔晶嵌入成功（第二项 = 嵌入的层号 1..2；那一格从此永久停止回长）。</summary>
    public event Action<int, int>? CrystalEmbedded;

    /// <summary>二阶三项门槛首次同时满足（象限 + 层；只发一次，之后不再重发）。</summary>
    public event Action<int, int>? TierGateSatisfied
    {
        add => Growth.TierGateSatisfied += value;
        remove => Growth.TierGateSatisfied -= value;
    }

    /// <summary>进阶仪式开起来（目标位阶 + 仪式到点的步号）；被拒的那一发不发。</summary>
    public event Action<BeastTier, long>? TierUpgradeStarted;

    /// <summary>拔锚离城成功（从哪座城拔的）。</summary>
    public event Action<string>? AnchorPulled;

    /// <summary>一场防守开起来（普通防守或最终防守）。</summary>
    public event Action<BattleOccasion>? DefenceStarted
    {
        add => Battle.DefenceStarted += value;
        remove => Battle.DefenceStarted -= value;
    }

    /// <summary>最终防守立即重试还原成功（打到第几次尝试；首次 = 1）。</summary>
    public event Action<int>? FinalRetryStarted;

    /// <summary>挖开洞穴格（象限 + 层；洞里四只原生怪就位）。</summary>
    public event Action<int, int>? CaveOpened
    {
        add => Soil.CaveOpened += value;
        remove => Soil.CaveOpened -= value;
    }

    /// <summary>发起洞穴清剿被受理（象限 + 层）。</summary>
    public event Action<int, int>? CaveClearStarted
    {
        add => Battle.CaveClearStarted += value;
        remove => Battle.CaveClearStarted -= value;
    }

    /// <summary>洞穴清剿胜利：洞里四只全灭，那一格转成高危生产区（象限 + 层）。</summary>
    public event Action<int, int>? CaveCleared
    {
        add => Battle.CaveCleared += value;
        remove => Battle.CaveCleared -= value;
    }

    /// <summary>洞穴清剿失败（象限 + 层 + 封闭原因：超时 / 撤退；攻破归入超时那一档）。</summary>
    public event Action<int, int, CaveFailReason>? CaveClearFailed
    {
        add => Battle.CaveClearFailed += value;
        remove => Battle.CaveClearFailed -= value;
    }

    /// <summary>洞穴重置：那一格回长填满，清剿状态清零、洞里重新住满四只（象限 + 层）。</summary>
    public event Action<int, int>? CaveReset
    {
        add => Soil.CaveReset += value;
        remove => Soil.CaveReset -= value;
    }

    /// <summary>办赛收场的一笔门票入账。</summary>
    public event Action<TicketIncomeRecord>? TicketIncome;

    /// <summary>一次普通修理实际扣了备件。</summary>
    public event Action<RepairSpentRecord>? RepairSpent;

    /// <summary>一次法术治疗实际扣了法力。</summary>
    public event Action<HealingSpentRecord>? HealingSpent;

    /// <summary>镇政府是不是已被攻破。</summary>
    public bool TownHallBreached => Siege.Breached;

    /// <summary>空艇成交窗口到哪一步（0 = 没开窗）。</summary>
    public long AirshipWindowUntilTick { get; private set; }

    /// <summary>这一航段还有几艘空艇要成交（<c>AirshipDock</c> 在册时是 1..3，否则 0..2）。</summary>
    public int AirshipVisitsLeft { get; private set; }

    /// <summary>这一航段待运的矿石（电梯之外的地面散货那一档）。</summary>
    public double PendingOre { get; private set; }

    /// <summary>这一航段待运的特殊材料。</summary>
    public double PendingMaterial { get; private set; }

    /// <summary>
    /// 「本航段还欠多少矿石 / 特殊材料」那一对读数的<b>唯一写口</b>
    /// —— 由 <see cref="HaulOrders"/> 在电梯结算那一格刷：
    /// 地底还欠着的整件（电梯与地面两路都没搬走的）加上甲板上还没入库的那几件。
    /// </summary>
    public void RecordPendingGroundCargo(double ore, double material)
    {
        PendingOre = ore;
        PendingMaterial = material;
    }

    /// <summary>攻破时按「从高到低、同价按 id 字典序」毁掉的那几栋（读数口）。</summary>
    public IReadOnlyList<string> WarLossBuildings => Siege.WarLossBuildings;

    /// <summary>攻破时被掳走的那几个 id（读数口）。</summary>
    public IReadOnlyList<string> TakenPeople => Siege.TakenPeople;

    /// <summary>解救要记进哪一家势力的负债账（没欠就空串）。</summary>
    public string RescueFactionId => Siege.RescueFactionId;

    /// <summary>罗盘时钟那一行。</summary>
    public string CompassText() => ArenaTime.CompassAt(Tick);

    /// <summary>
    /// 建甲板那张网格。<b>恒按二阶最大档 28×28 格建</b>（2 米一格、原点在角上）：
    /// 一阶时外圈两格带地形阻挡（可用区即 24×24），进阶完成那一刻解阻挡（<see cref="ApplyDeckTier"/>）。
    /// 寻路格号与建造格号的平移差因此永远不动 —— 扩容不改两张网格的相对位置，只改哪些格通着。
    /// </summary>
    private static NavGrid BuildDeckGrid()
        => new(new NavGridSpec
        {
            Space = DeckSpace,
            CellMeters = ArenaTuning.DeckCellMeters,
            CellsX = ArenaTuning.DeckCellsTier2,
            CellsZ = ArenaTuning.DeckCellsTier2,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = DeckBlueprints.NavRingCells(
                ArenaTuning.DeckCellsTier1, ArenaTuning.DeckCellsTier2),
        });

    /// <summary>
    /// 当前甲板格数：一阶 24 格（48 米见方）、二阶 28 格（56 米见方）。
    /// <b>真值只在这一个字段</b>，建造边界、落点序列、刷怪落点与边长读数全部由它推；
    /// 位阶 → 格数的推导只有 <see cref="ArenaTuning.DeckCellsFor"/> 一条。
    /// </summary>
    public int DeckCells { get; private set; } = ArenaTuning.DeckCellsTier1;

    /// <summary>当前甲板边长（米，沿两轴各一份）。</summary>
    public double DeckSpanMeters => ArenaTuning.DeckSpanMeters(DeckCells);

    /// <summary>当前建造格边界（下界沿列与行各一份、上界含）。扩容生效那一刻随 <see cref="DeckCells"/> 外扩。</summary>
    public (int MinCol, int MinRow, int MaxCol, int MaxRow) DeckCellBounds => (
        ArenaTuning.BuildMinCol(DeckCells), ArenaTuning.BuildMinCol(DeckCells),
        ArenaTuning.BuildMaxCol(DeckCells), ArenaTuning.BuildMaxCol(DeckCells));

    /// <summary>这一格号在不在当前位阶的甲板边界内（活动边界的唯一判口）。</summary>
    public bool IsDeckCellInBounds(GridCell cell)
    {
        var (minCol, minRow, maxCol, maxRow) = DeckCellBounds;
        return cell.Col >= minCol && cell.Col <= maxCol && cell.Row >= minRow && cell.Row <= maxRow;
    }

    /// <summary>
    /// 位阶 → 甲板格数的<b>唯一生效口</b>：开局不叫（一阶缺省），进阶完成与最终重试还原各叫一次。
    ///
    /// <para><b>只扩不缩</b>：扩容永久（spec「二阶进阶」），缩边会让已经压在新格上的建筑
    /// 「在册却在界外」，问到更小的档位当场抛。同档再叫一次是空动作（还原路径与事件路径可能都落到这里）。</para>
    ///
    /// <para><b>既有建筑与布局不搬迁的那条规则</b>：建造格的世界落点只由「格号 × 格长」推出
    /// （<see cref="DeckBlueprints.CandidateOf"/>），与边界无关 —— 外扩只把界外的格变成界内，
    /// 任何一格的格号与落点一寸不动。寻路格号 → 空间坐标的平移（<see cref="DeckBlueprints.NavOffsetCells"/>）
    /// 恒按最大档算，扩容也不改。</para>
    /// </summary>
    private void ApplyDeckTier(BeastTier tier)
    {
        var cells = ArenaTuning.DeckCellsFor(tier);
        if (cells == DeckCells) return;
        if (cells < DeckCells)
        {
            throw new InvalidOperationException(
                $"甲板扩容不缩边：当前 {DeckCells} 格，要求回到位阶 {tier} 的 {cells} 格");
        }

        var prev = DeckCells;

        // 三步各管一件事实：放置边界（建造册）、通过性（寻路网）、落点序列（活物册）。
        // 谁先谁后不互相依赖，但都必须在 DeckCells 改写之前拿旧档算差集。
        _deckRule.ExpandBounds(
            ArenaTuning.BuildMinCol(cells), ArenaTuning.BuildMinCol(cells),
            ArenaTuning.BuildMaxCol(cells), ArenaTuning.BuildMaxCol(cells));
        foreach (var nav in DeckBlueprints.NavRingCells(prev, cells))
        {
            DeckGrid.SetBlocked(nav, false);
        }

        DeckCells = cells;
        AppendDeckSpawnPoints(prev, cells);
    }

    /// <summary>
    /// 把扩容新增那圈可通行格的格心<b>按列再按行</b>接到落点序列末尾。
    /// 次序固定 = 同一档扩容永远接出同一段（重放逐字可复现）；旧落点一寸不动，
    /// 发号游标继续按整表取模。
    /// </summary>
    private void AppendDeckSpawnPoints(int prevCells, int cells)
    {
        for (var col = ArenaTuning.BuildMinCol(cells); col <= ArenaTuning.BuildMaxCol(cells); col++)
        {
            for (var row = ArenaTuning.BuildMinCol(cells); row <= ArenaTuning.BuildMaxCol(cells); row++)
            {
                var inPrev = col >= ArenaTuning.BuildMinCol(prevCells) && col <= ArenaTuning.BuildMaxCol(prevCells)
                    && row >= ArenaTuning.BuildMinCol(prevCells) && row <= ArenaTuning.BuildMaxCol(prevCells);
                if (inPrev) continue;
                var cell = new GridCell(col, row);
                if (!IsDeckCellInBounds(cell)) continue;
                if (DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(cell))) continue;
                _spawnPoints.Add(new NavPoint(CellCenterOf(col), CellCenterOf(row)));
            }
        }
    }


    // === 读数口（供上层那一件用） ===

    /// <summary>某一栋的门口点（甲板逻辑坐标）；不在册或没有锚点回 null（那一档跳过这个人）。</summary>
    public NavPoint DoorDeckOf(string buildingId)
    {
        var building = Construction.Find(buildingId);
        if (building is null || building.Anchor is not { } anchor) return new NavPoint(0.0, 0.0);

        var at = Deck.DoorPointOf(building.BlueprintId, anchor);
        return new NavPoint(at.X, at.Y);
    }

    /// <summary>
    /// 某一栋门前那一小片空地上第 <paramref name="spread"/> 个可站点（甲板逻辑坐标）。
    ///
    /// <para><b>为什么不是门口那一格本身</b>（2026-10-15 补的）：门口格只有一格，站得下一个人。
    /// 日常走位把全镇民发往四栋建筑的门口，于是几十个人抢同一格 ——
    /// 寻路层的推挤只推得开「站得住」的落点，于是绝大多数人停在门口格旁边，
    /// 一个人也到不了门口（实测：50 人里到门口 0 个）。
    /// 门口邻域 3×3 里有 5~9 格可站，按名单序号错开才是一栋楼门前该有的样子。</para>
    ///
    /// <para><b>邻域取 5×5 而不是 3×3</b>：一格站得下六个人（身体半径 0.4 米 ×2 = 0.8 米，
    /// 格长 2 米），全镇 50 人分去四栋门前，每栋门前要有十几格可站才排得开。
    /// 3×3 邻域只有 9 格，实测 50 人里到位的只有 4 个。</para>
    ///
    /// <para><b>次序列固定</b>（门口格先，再按行再按列扫邻域），同一个
    /// <paramref name="spread"/> 永远给出同一格 —— 重放要逐字可复现靠的就是这一条。
    /// 全被建筑占死时退回门口格本身（那一格此刻也是墙，走不到，但不制造新事实）。</para>
    /// </summary>
    public NavPoint DoorSpreadDeckOf(string buildingId, int spread)
    {
        var door = DoorDeckOf(buildingId);
        var doorCol = (int)Math.Floor(door.X / ArenaTuning.DeckCellMeters);
        var doorRow = (int)Math.Floor(door.Z / ArenaTuning.DeckCellMeters);

        var free = new List<GridCell>();
        for (var dRow = -2; dRow <= 2; dRow++)
        {
            for (var dCol = -2; dCol <= 2; dCol++)
            {
                var cell = new GridCell(doorCol + dCol, doorRow + dRow);
                if (!IsDeckCellInBounds(cell)) continue;
                if (DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(cell))) continue;
                free.Add(cell);
            }
        }

        if (free.Count == 0) return door;
        var pick = free[Math.Abs(spread) % free.Count];
        return new NavPoint(CellCenterOf(pick.Col), CellCenterOf(pick.Row));
    }

    /// <summary>
    /// 与 <see cref="DoorSpreadDeckOf"/> 同一份读数，但<b>先按固定 id 或蓝图 id 解析到在册建筑</b>；
    /// 不在册回 null（那一档由调用方回落）。
    ///
    /// <para>日常走位那一侧递进来的是固定 id（宿舍 / 饭店可能是玩家新建的 <c>bN</c>），
    /// 所以「解析」只走 <see cref="ResolveBuildingId"/> 这一处；测试也直接驱动这一口，
    /// 不再在测试里复刻一份解析规则。</para>
    /// </summary>
    public NavPoint? DoorSpreadDeckOrNull(string buildingIdOrBlueprintId, int spread)
    {
        var resolved = ResolveBuildingId(buildingIdOrBlueprintId);
        return resolved.Length == 0 ? null : DoorSpreadDeckOf(resolved, spread);
    }

    /// <summary>
    /// 甲板上已经有那一摊在管走位没有 —— <b>交战、搬运、游客，三处任一为真就成立</b>。
    ///
    /// <para>日常走位是<b>空甲板上</b>才做的事：这三处的移动权各归自己
    /// （交战那一摊配对后成对发令、游客每一分钟改一次去处、搬运那一单两程各有各的落点），
    /// 日常走位插一脚就是把同一个人的移动权分成两处，两处都不知道对方朝哪儿走。</para>
    /// </summary>
    private bool DeckBusyForWalk()
        => Battle.Occasion != BattleOccasion.None || _carrying.Count > 0 || Visitors.InTownCount > 0;

    /// <summary>离给定点最近的那一栋产出建筑（防守最后 2 人那一档用）。</summary>
    public string NearestProductionId(NavPoint fromDeck)
    {
        var best = string.Empty;
        var bestDistance = double.MaxValue;
        foreach (var id in Construction.Ids)
        {
            var b = Construction.Find(id);
            if (b is null || b.Anchor is null) continue;
            if (Array.IndexOf(ProductionBlueprintIds, b.BlueprintId) < 0) continue;

            var at = new NavPoint(
                b.Cells.Count > 0 ? CellCenterOf(b.Cells[0].Col) : 0.0,
                b.Cells.Count > 0 ? CellCenterOf(b.Cells[0].Row) : 0.0);
            var distance = at.DistanceTo(fromDeck);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = id;
        }

        return best.Length > 0 ? best : FindByBlueprint("Hotel");
    }

    /// <summary>
    /// 归零去向：<b>镇政府留在册上</b>，耐久停在真 0；其余建筑照旧归零即出册。
    ///
    /// <para>spec 要的「锁定耐久为 0 <b>且不从建造册移除</b>」里，那一半是玩法决定：
    /// 攻破之后这一局还要拿镇政府的耐久读数判「已经打空了」（<see cref="CheckBreach"/>），
    /// 还要能查得到它、还能报快照。所以建造模块在耐久到 0 那一格问本件，回 false 就把它留在册上。
    /// 战损那三栋（竞技场 / 旅馆 / 饭馆）走同一道问，它们回 true，所以照旧出册。</para>
    ///
    /// <para><paramref name="cause"/> 在那条缝上恒为摧毁（建造模块两个归零出口都这么问），
    /// 这里用不到，写出来是为了签名读得懂。</para>
    /// </summary>
    private static bool RemoveOnZero(Building building, RemovalCause cause)
    {
        _ = cause;
        return building.Id != TownHallId;
    }

    /// <summary>打一点耐久（镇政府归零后留在册上，出不出册由 <see cref="RemoveOnZero"/> 答）。</summary>
    public bool DamageBuilding(string buildingId, double amount)
    {
        if (amount <= 0.0) return false;
        if (Construction.Find(buildingId) is null) return false;
        return Construction.Damage(buildingId, amount).Ok;
    }

    /// <summary>搬一件点位物（上帝之手与菜单派活共用同一套写路径）。</summary>
    public void DropHaulOf(string unitId) => _carrying.Remove(unitId);

    /// <summary>这个人手上正在搬的那一单（没有回 null）。</summary>
    public HaulOrder? CarryingOf(string unitId)
        => _carrying.TryGetValue(unitId, out var inHand) ? inHand.Order : null;

    /// <summary>这个人手上那一单走到哪一程（手上没有单回 null）。</summary>
    public HaulLeg? CarryingLegOf(string unitId)
        => _carrying.TryGetValue(unitId, out var inHand) ? inHand.Leg : null;

    /// <summary>
    /// 手上有一单时，把那一程的走位令<b>重新发一遍</b>（<b>不新算下一站</b> ——
    /// 目标点就是手上那张单上的取货点或目标门口，与派单那一发发的是同一条令）。
    ///
    /// <para><b>为什么要有这一口</b>：仗打着的每一分钟，<c>BattleSim.RePair</c> 会把场上所有人
    /// 改成打斗那一发的走位令 —— 手上正搬着东西的人也不例外。仗一打完，若只把他「不定住」，
    /// 他手上留着的是打斗那一条令，于是那一单永远走不到取货点、也永远投不出去。
    /// 把搬运那一程的令还给他，那张单才跑得完（跑完之后结单那一格自然把他放回空闲）。</para>
    /// </summary>
    /// <returns>手上没有单回 false。</returns>
    public bool ResumeHaulOf(string unitId)
    {
        if (!_carrying.TryGetValue(unitId, out var inHand)) return false;

        var u = Roster.Find(unitId);
        if (u is null) return false;

        if (inHand.Leg == HaulLeg.ToPickup) WalkToPickup(u, inHand.Order);
        else WalkToDelivery(u, inHand.Order);

        return true;
    }

    /// <summary>
    /// 人不在场那一档（阵亡 / 离镇）：手上那一纸退出，<b>货还在册就退回本级队尾</b>，
    /// 货已经不在册就作废 —— 两种走法都不丢也不重：退回的那一纸由另一双手从头再送一次。
    ///
    /// <para><b>为什么要区别对待</b>：无条件作废会让货永远躺在甲板上；
    /// 无条件退回会让一张指向已不在册之物的空单在队列里转不出来。</para>
    /// </summary>
    public void AbandonHaulOf(string unitId)
    {
        if (!_carrying.Remove(unitId, out var inHand)) return;
        if (Items.Find(inHand.Order.ItemId) is null) return;

        Haul.Requeue(inHand.Order with { QueuedTick = Tick });
    }

    /// <summary>此刻有多少人手上有一单（状态可查那一口：队伍里在途几单）。</summary>
    public int CarryingCount => _carrying.Count;

    /// <summary>最近若干次交单的结果（状态可查那一口；旧的先掉出去，账是定长的）。</summary>
    public IReadOnlyList<HaulLanding> HaulLandings => _haulLandings;

    /// <summary>
    /// 卖明星角斗士的留名账（读数口）。这笔账长在<b>本世界这一台实例</b>上：
    /// 同一进程里造两台世界，两台的成交记录互不相干。
    /// 定长 <see cref="StarSaleLedgerSize"/> 条，旧的先掉出去；掉了几条由
    /// <see cref="StarSaleCount"/> 与 <see cref="StarSaleIssued"/> 那一对读数摆在明面上。
    /// </summary>
    public IReadOnlyList<StarSaleRecord> StarSales => _starSales;

    /// <summary>成交后留名入账那一格（<c>SellStar</c> 调它）。</summary>
    public void NoteStarSale(ArenaUnit seller, double price)
    {
        _starSales.Add(new StarSaleRecord(
            seller.Id, Tick, price, seller.PersonalReputation, seller.StatTotal));
        StarSaleIssued++;
        if (_starSales.Count > StarSaleLedgerSize) _starSales.RemoveAt(0);
    }

    /// <summary>留名账上还留着的成交条数。</summary>
    public int StarSaleCount => _starSales.Count;

    /// <summary>留名账一生累计成交了几笔（与 <see cref="StarSaleCount"/> 之差即掉出去的那几条）。</summary>
    public int StarSaleIssued { get; private set; }

    /// <summary>交单那一格的读数（最近一次那条）。</summary>
    public string HaulLandingText()
    {
        if (_haulLandings.Count == 0) return "HAUL -";

        var last = _haulLandings[^1];
        return string.Format(CultureInfo.InvariantCulture,
            "HAUL tick={0} {1} {2}->{3} {4}{5}",
            last.Tick, last.CarrierId, last.ItemId, last.TargetBuildingId,
            last.Landed ? "落" : "拒", last.Reason.Length > 0 ? $":{last.Reason}" : string.Empty);
    }

    /// <summary>
    /// 办赛结束那一口的门票结算（在镇游客 × 基础消费 × 镇名声那一档 × 门票倍率）。
    /// <b>回这一场进了多少金币</b> —— 战报的「门票收入」读它，不另算第二遍。
    /// </summary>
    public double SettleMatchIncome()
    {
        var audience = Visitors.InTownCount;
        if (audience <= 0) return 0.0;

        var gold = audience * ArenaTuning.RestaurantBaseSpend * Visitors.TicketIncomeFactor
            * Town.TicketMultiplier;
        if (gold <= 0.0) return 0.0;

        Town.ProduceCoin(gold);
        Town.NoteTicketIncome(gold);
        Town.SettleAutoRepayment(gold);
        TicketIncome?.Invoke(new TicketIncomeRecord(Tick, gold, audience));

        // 贡献读数最高的角斗士 +5 个人名声。
        ArenaUnit? top = null;
        foreach (var u in Roster.Units)
        {
            if (u.Role != UnitRole.Gladiator || u.LeftTown) continue;
            if (top is null || u.Contribution > top.Contribution) top = u;
        }

        if (top is null || top.Contribution <= 0.0) return gold;

        top.PersonalReputation = Math.Min(
            ArenaTuning.ReputationMax, top.PersonalReputation + ArenaTuning.MatchPersonalReputationGain);
        top.Contribution = 0.0;
        return gold;
    }

    // === 推进 ===

    /// <summary>走位之前那一段：航行驶令、土壤、修房、法力恢复、日结、电梯、攻破判。</summary>
    private void OnBeforeStep(long tick)
    {
        // 形态那一格的饥饿衰减系数要在走位之前定：荒漠态在沙漠带里减半、暴食态乘 1.5，
        // 其余是 1.0。这一格每一步现算，不缓存 —— 巨兽走出沙漠带，下一步就恢复原速衰减。
        ApplyFormDecayFactor();

        Voyage.DecideStep(tick);

        // 解锁判定排在 DecideStep 之后：荒漠态的累计计时是 DecideStep 那一格里加的。
        Growth.CheckUnlocks();
        NoteFlowMilestones();

        // 日常走位排在走位之前：这一档发的是移动令，要赶在本步那一次脉冲之前递。
        // 宿舍 / 饭店不在册时，那一档回落到人脚下最近的可通行格（不站着等一条永远不来的令）。
        Roster.StepDailyRound(tick, DeckBusyForWalk(), DoorSpreadDeckOrNull,
            unit => NearestFreeDeckCell(unit.DeckX, unit.DeckZ));

        // 塌方那一栏的「吓跑在镇游客」要的是塌方那一刻的在镇游客数 ——
        // 这一格与下面 ApplyFreshCollapses 读的是同一个数（两者之间没有一步改动游客），
        // 所以记录里的 VisitorsFled 与世界里实际吓跑的人数是同一个数。
        //
        // 仪式期间停采矿（开挖完成与回长照常）：停摆的早退排在这一格之后，
        // 所以停摆本身并没有停掉采矿累积 —— 仪式要停，得在这里单独加闸。
        //
        // 采矿倍率只剩位阶那一档：形态不再改采矿产出（切片把它改成食性与衰减）。
        var tierMining = ArenaTuning.StatsOf(Tier).MiningFactor;

        Soil.AdvanceStep(tick, Visitors.InTownCount,
            miningPaused: InRitual,
            oreFactor: tierMining,
            materialFactor: tierMining);

        // 塌方的后果住在 ApplyCollapse 那一口，土壤自己只记「塌了这一格」。
        // 接线放在推进土壤的同一格：塌一次调一次、次序与发生次序一致，不重不漏。
        ApplyFreshCollapses();

        Deck.AdvanceRepairs(tick);
        SyncSoilCrew();
        SettleRevives(tick);

        // 抵达停靠点那一格：巨兽停住且这一局还没拔过锚，就是到了。
        if (Voyage.Phase == VoyagePhase.Holding && !PulledAnchor && Voyage.TargetCityId is { } arrivedId)
        {
            var place = BigMapGrid.PlaceById(arrivedId);
            if (place is not null) OnArrived(place);
        }

        // 停摆期到点：城卫队解救。
        //
        // **这一格放在 `InStandstill` 分支的外面**：`InStandstill` 读作
        // `StandstillUntilTick > Tick`，所以进到那个分支里就意味着「还没到点」，
        // 分支内部的 `Tick >= StandstillUntilTick` 恒为假 —— 挪进去解救就成了一段
        // 永远进不去的死代码，停摆期从此没有尽头、债也永远记不进负债账。
        SettleStandstill();

        // 进阶仪式到点：位阶 +1，移速倍率换档，抛一次事件。
        // 与 SettleStandstill 同一位置（早退分支的外面）：挪进那个分支就成了死代码。
        if (Growth.SettleRitual(Tick)) Flow.NoteTier2(Tick);

        if (InStandstill || InRitual)
        {
            // 停摆期与进阶仪式：产出、采矿、办赛、游客进出全停；
            // 修房、搬运、治伤复活、土壤回长照常。
            return;
        }

        if (tick > 0 && tick % ArenaTime.StepsPerGameHour == 0 && tick != _lastHourTick)
        {
            _lastHourTick = tick;
            Town.RegainManaOne();
            Roster.SettleHour(CorpseMoodDeltaAt);
        }

        if (tick > 0 && tick % ArenaTime.StepsPerGameDay == 0 && tick != _lastDayTick)
        {
            _lastDayTick = tick;
            SettleGameDay(tick);
        }

        if (MagicElevator.IsSettlementTick(tick) && tick != _lastElevatorTick)
        {
            _lastElevatorTick = tick;
            Elevator.Settle(Soil, DeliverToWarehouse, BoardShaft);

            // 电梯名额只有 4 个，排不到的格靠人搬：搬料与「还欠多少」那一对读数都在这一格刷。
            HaulOrders.RaiseGroundCargoOrders();
        }
    }

    /// <summary>走位之后那一段：出界扣魂石、同步定位、按分钟交战、搬运派单、游客。</summary>
    private void OnAfterStep()
    {
        // 出界扣魂石排在这一段的最前：它读的是走位那一次脉冲走完之后的位置，
        // 而这一段剩下的几摊都不改巨兽的坐标。
        Voyage.SettleStep(Tick);

        Roster.SyncPositions();
        Battle.Step(Tick);

        // 停摆期不冻结搬运：spec 写的是「修房、搬运、治伤复活、土壤回长照常」。
        // 冻住的是产出、采矿、办赛与游客进出那几摊，不包括这一摊。
        //
        // **先排单、后结算到达、再派单**：补单放在最前，结单那一格把上一张放回来的人
        // 紧接着就在同一发里接上下一张 —— 顺序反了的话每个搬运人结完一单都要空一整个逻辑步才接活。
        HaulOrders.RaiseStandingOrders();
        SettleHaulArrivals();
        DispatchHaul();

        // 停靠期才放游客上甲板；拔锚防守、停摆期与进阶仪式都不放（那一格由这一个布尔说了算）。
        //
        // 三个去处**按蓝图现查**（不是写死的固定 id）：开局六栋里没有酒店，玩家新建的
        // 酒店 / 饭店 / 竞技场是 bN；固定 id 那一栋被拆掉之后这里回空串，
        // 游客那一侧自行回退到还在的那几栋（不再指向 (0,0)）。
        Visitors.StepMinute(
            Tick,
            IsDocked && !InStandstill && !InRitual && !Battle.IsDefence,
            Battle.Occasion == BattleOccasion.Match,
            FindByBlueprint("Restaurant"), FindByBlueprint("Hotel"), FindByBlueprint("Arena"),
            DoorDeckOf,
            gold => Town.ProduceCoin(gold));

        if (Tick - _lastBreachCheckTick >= ArenaTime.StepsPerGameMinute)
        {
            _lastBreachCheckTick = Tick;
            CheckBreach();
        }
    }

    /// <summary>
    /// 推进一整段：先跑本世界那一段（排在走位之前），再交装配推（内部是「落地形 → 推时钟 → 推建造」），
    /// 最后跑走位之后那一段。
    /// </summary>
    public override bool Advance(int steps = 1)
    {
        var remaining = steps;
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, 250);
            if (!Assembly.Advance(chunk, afterEachStep: OnAfterStep))
            {
                return false;
            }

            remaining -= chunk;
        }

        return true;
    }

    /// <summary>重放快照：调试快照 + 两个寻路空间的重放段（分号转空格）。</summary>
    public string ReplaySnapshot()
        => ArenaSnapshot.Replay(this);

    /// <summary>调试快照（状态可查那一口，分段格式）。</summary>
    public string DebugSnapshot() => ArenaSnapshot.Debug(this);

    // === 词表那一侧要的几口 ===

    /// <summary>停靠：清空在镇游客、回满游客池、开下一航段的空艇到访数。</summary>
    public void OnDocked(MapPlace place)
    {
        DockedCityId = place.Id;
        RecruitedThisDock = 0;
        PulledAnchor = false;
        Visitors.RefillFor(place);
        Visitors.ClearTown();
        AirshipVisitsLeft = AirshipVisitsFor(place.Id);
        OpenAirshipWindow();

        // 只有「该到的那座城」才推段；顺路停别的城、回旧城、去野外锚地都只算停靠。
        Flow.NoteDocked(place.Id);
        MarkSegmentIfNeeded();
    }

    /// <summary>拔锚离城那一发是否已经发过（发了就不再自动「抵达停靠点」）。</summary>
    public bool PulledAnchor { get; private set; }

    /// <summary>
    /// 下令朝一个停靠点航行 —— <b>顺带清掉「本局已经拔过锚」那一闩</b>。
    ///
    /// <para><b>为什么这一闩在这条命令上清</b>：它只该管住「拔锚之后、还没收到新的航行令」
    /// 那一段 —— 那时候巨兽还站在城边的位置上，而 <c>TargetCityId</c> 仍指着刚才那座城，
    /// 不闩住就会原地重新停靠一遍。可是「重新停靠」由玩家下 <c>Dock</c> 令来发起，
    /// 于是这一闩的归还在这条命令上：<c>Dock</c>（玩家路径）与 <c>Rescue</c>（城卫队解救后恢复航行）
    /// 都走它。闩不在这里清，巨兽就只会在城边站住、永远不再停靠 —— 连续第 2 轮停靠根本发不起来。</para>
    /// </summary>
    public void OrderSailTo(string cityId)
    {
        PulledAnchor = false;
        Voyage.SailToCity(cityId);
    }

    /// <summary>拔锚时带走的那个向心环数（防守敌人数 = 20 + 4 × 环数）。</summary>
    public int LastPulledRings { get; private set; }

    /// <summary>拔锚离城：清空在镇游客、打开防守。</summary>
    public void OnPulled(int inwardRings)
    {
        // 先记下从哪座城拔的：清掉 DockedCityId 之后，「这一场防守是从哪拔的」就没人知道了 ——
        // 而流程要拿它判「赢了该不该往前推一段」。
        var cityId = DockedCityId;

        DockedCityId = string.Empty;
        PulledAnchor = true;
        LastPulledRings = inwardRings;
        Visitors.ClearTown();
        AirshipWindowUntilTick = 0;

        Flow.NoteAnchorPulled(cityId);
        AnchorPulled?.Invoke(cityId);
    }

    /// <summary>
    /// 抵达停靠点那一格（由推进那一段判，不在词表里）。
    ///
    /// <para><b>「已经在这一座城停着」要当场返回</b>：推进那一段判的是
    /// <c>Phase == Holding 且有目标城</c>，而停稳之后这两个条件会一直成立、一直成立到拔锚为止。
    /// 不在这里闩住，<see cref="OnDocked"/> 就每个逻辑步重跑一遍 ——
    /// 游客池每步回满（每游戏日 25 那一档回速等于作废）、在镇游客每步被清空（一个人也留不下）、
    /// 空艇成交窗口每步重开（一趟能成交到天荒地老）。</para>
    /// </summary>
    public void OnArrived(MapPlace place)
    {
        if (PulledAnchor) return;
        if (DockedCityId == place.Id) return;

        OnDocked(place);
    }

    /// <summary>开一个空艇成交窗口（30 个游戏分钟）。</summary>
    public void OpenAirshipWindow()
        => AirshipWindowUntilTick = Tick + (ArenaTime.StepsPerGameMinute * ArenaTuning.AirshipWindowGameMinutes);

    /// <summary>记一笔空艇成交（窗口内成交一次；还剩几艘就再开一扇窗）。</summary>
    public void RecordAirshipTrade()
    {
        AirshipVisitsLeft = Math.Max(0, AirshipVisitsLeft - 1);
        if (AirshipVisitsLeft > 0) OpenAirshipWindow();
    }

    /// <summary>现在是不是在空艇成交窗口内。</summary>
    public bool InAirshipWindow => AirshipWindowUntilTick > Tick && AirshipVisitsLeft > 0;

    /// <summary>
    /// 空艇成交价（相对市价的系数那一档）：沿途各城物价系数均值 × <see cref="ArenaTuning.AirshipPriceFactor"/>。
    /// <b>均值扫全部城镇点位、跳过野外锚地</b>（锚地无物价）；2026-10-19 加沙城之前是
    /// 「贫富两城均值」，三城之后照旧写死两城会让沙城的货永远按旧均价折算。
    /// </summary>
    public static double AirshipPriceFactor
    {
        get
        {
            var sum = 0.0;
            var count = 0;
            foreach (var place in BigMapGrid.Places)
            {
                if (place.Id == BigMapGrid.WildAnchor.Id) continue;
                sum += place.PriceFactor;
                count++;
            }

            return (sum / count) * ArenaTuning.AirshipPriceFactor;
        }
    }

    /// <summary>当前所在城的物价系数（不在停靠点按 1.0）。</summary>
    public double CityPriceFactor => DockedPlace?.PriceFactor ?? 1.0;

    /// <summary>
    /// 还能吃进多少份食物 —— <b>饱食度封顶那一档的余量 × 每点饱食要的份数</b>。
    ///
    /// <para><b>满饱食度时是 0</b>：兽吃不下更多。那一档不拒，食物就白扣了
    /// （扣 50 份、饱食度纹丝不动 —— 玩家白亏）。</para>
    /// </summary>
    public double SatiationRoomFood
        => Math.Max(0.0, (ArenaTuning.SatiationMax - Ecology.Satiation) * ArenaTuning.FoodPerSatiation);

    /// <summary>
    /// 喂巨兽：食物换饱食度（每 5 食物换 1 点）。不够就一子儿不动。
    ///
    /// <para><b>饱食度满了也喂不进去</b>：那一档回 <see cref="RejectReason.InsufficientFood"/>,
    /// 一口都不扣 —— 判据在 <see cref="SatiationRoomFood"/>，与 <c>ArenaSetup.FeedCapacity</c> 同一口。</para>
    ///
    /// <para><b>只有实际扣粮且有效恢复饱食度的食物进暴食进度</b>：饱食已满时先拒、一口不扣，
    /// 所以「白扣食物」那一格不会发生；喂进去的那一份按实际补进的饱食点数折回食物量记账。</para>
    /// </summary>
    public RejectReason FeedBeast(double food)
    {
        if (food <= 0.0) return RejectReason.InvalidArgs;
        if (food > SatiationRoomFood) return RejectReason.InsufficientFood;
        if (!Town.TrySpendFood(food)) return RejectReason.InsufficientFood;

        // 兽粮那一本不够就退回镇仓：两本账各扣自己实际拿得出的那一份。
        var taken = Voyage.TakeGranaryFood(food);
        Town.RefundFood(food - taken);
        if (taken <= 0.0) return RejectReason.InsufficientFood;

        var points = Ecology.Feed(taken);
        Growth.AddFedTotalFood(Math.Min(taken, points * ArenaTuning.FoodPerSatiation));

        // 手动喂食这一条路也要立刻判解锁：否则手动凑够 400 却要等到下一步才解锁。
        Growth.CheckUnlocks();
        NoteFlowMilestones();
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// NPC 喂食的执行者：按名单固定次序取第一个<b>站着、未离镇、未被抓、无岗位、也没在搬东西</b>
    /// 的工作人员或帮工（<see cref="DispatchHaul"/> 派单圈的是同一批人）。
    ///
    /// <para><b>遍历序即入册序</b>：重放要逐字可复现，执行者不能靠运行期偶然挑出来；
    /// 挑中谁不写进任何账 —— 这一发只由 <see cref="FeedBeast"/> 记账。</para>
    /// </summary>
    /// <returns>找到的那一个；一个都没有回 null。</returns>
    public ArenaUnit? FindNpcFeedExecutor()
    {
        foreach (var u in Roster.Units)
        {
            if (u.Role is not (UnitRole.Worker or UnitRole.Helper)) continue;
            if (u.State != UnitState.Standing || u.LeftTown || u.Held) continue;
            if (u.Post.Length > 0) continue;
            if (CarryingOf(u.Id) is not null) continue;
            return u;
        }

        return null;
    }

    /// <summary>
    /// 这一发 NPC 喂食现在会被哪一条挡住（没被挡回 <see cref="RejectReason.InvalidArgs"/>）。
    /// <b>动词与面板共用这一份判据</b>，不各写一套。
    /// </summary>
    public RejectReason NpcFeedBlocker()
    {
        if (!IsDocked) return RejectReason.InvalidPhase;
        if (Battle.Occasion != BattleOccasion.None || InRitual || InStandstill)
            return RejectReason.InvalidPhase;
        if (Ecology.Satiation >= ArenaTuning.SatiationMax) return RejectReason.InsufficientFood;
        if (FindNpcFeedExecutor() is null) return RejectReason.NoIdleWorker;
        if (SatiationRoomFood < ArenaTuning.NpcFeedFoodAmount) return RejectReason.InsufficientFood;
        if (Town.Food + Voyage.GranaryFood < ArenaTuning.NpcFeedFoodAmount)
            return RejectReason.InsufficientFood;
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// NPC 喂食：停靠期由一名无岗工人或帮工喂 5 食物，走 <see cref="FeedBeast"/> 同一本账。
    /// </summary>
    /// <returns>拒绝原因；成功回 <see cref="RejectReason.InvalidArgs"/>。</returns>
    public RejectReason NpcFeed()
    {
        var blocker = NpcFeedBlocker();
        if (blocker != RejectReason.InvalidArgs) return blocker;
        return FeedBeast(ArenaTuning.NpcFeedFoodAmount);
    }

    /// <summary>
    /// 解锁判定的后果报到流程：暴食态解锁那一刻的步号记进里程碑。
    /// <b>与 <see cref="BeastGrowth.CheckUnlocks"/> 的每一处调用并排</b> ——
    /// 漏一处，手动喂食凑够阈值的那一局里程碑步号就晚了整整一步。
    /// </summary>
    private void NoteFlowMilestones()
    {
        if (Growth.GluttonFormUnlocked) Flow.NoteGluttonUnlocked(Tick);
    }

    /// <summary>
    /// 切形态：瞬态、无代价、非战斗时随时可切。
    ///
    /// <para><b>判定顺序三档</b>：正在打仗 → <see cref="RejectReason.InvalidPhase"/>；
    /// 没解锁 → <see cref="RejectReason.FormLocked"/>；切到当前形态是<b>幂等成功</b>。</para>
    ///
    /// <para><b>解锁永久</b>：解锁标记是独立的一份，不随沙漠计时变化而回收。</para>
    /// </summary>
    /// <returns>拒绝原因（没被拒回 <see cref="RejectReason.InvalidArgs"/>，与本类其余写口同一口径）。</returns>
    public RejectReason SwitchForm(BeastForm form)
    {
        if (Battle.Occasion != BattleOccasion.None) return RejectReason.InvalidPhase;

        var outcome = Growth.SwitchForm(form);
        if (outcome == FormSwitchOutcome.Locked) return RejectReason.FormLocked;

        // 双成长那一项「暴食态至少手动切换过一次」由流程自己记：
        // 幂等成功（切到当前形态）不算一次切换，形态没变。
        if (outcome == FormSwitchOutcome.Switched) Flow.NoteFormSwitched(form, Tick);

        // 形态真变那一发已经由事件订阅换过衰减系数（见构造期那一条）；
        // 幂等成功与真变在动词那一侧是同一档「没被拒」。
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// 开始游戏：只把流程从「未开始」推到开局航程。
    ///
    /// <para><b>为什么是一条动词</b>：标题屏「开始游戏」那一按也是玩家的一发写路径 ——
    /// 不挂在词表上，重放账里就少了开局那一发，重放端的世界从头到尾停在「未开始」。
    /// 它不替玩家按停靠：开局位置在贫城西边的野地上，进港那一程要玩家自己下 <c>Dock</c>。</para>
    /// </summary>
    /// <returns>拒绝原因（没被拒回 <see cref="RejectReason.InvalidArgs"/>，与本类其余写口同一口径）。</returns>
    public RejectReason StartRun()
        => Flow.Start() ? RejectReason.InvalidArgs : RejectReason.InvalidPhase;

    /// <summary>
    /// 最终防守初始化完、<b>尚未推进首个逻辑步</b>时取一份战前快照。
    /// 由拔锚那条写路径在战事开起来之后调一次；重试不再重取。
    /// </summary>
    internal void NoteFinalDefenceInitialized()
    {
        if (Flow.Stage != RunStage.FinalDefence) return;
        if (Battle.Occasion != BattleOccasion.FinalDefence) return;
        _finalSnapshot ??= FinalSnapshot.Take(this);
    }

    /// <summary>现在能不能「立即重试」最终防守：段在最终防守、仗已收场、战前快照在。</summary>
    public bool CanRetryFinalDefence
        => Flow.Stage == RunStage.FinalDefence
            && Battle.Occasion == BattleOccasion.None
            && _finalSnapshot is not null;

    /// <summary>战前快照的步号（没有快照 = -1；读数与用例用）。</summary>
    public long FinalSnapshotTick => _finalSnapshot?.Tick ?? -1;

    /// <summary>最终防守失败累计几次重试（只增不清零）。</summary>
    public int FinalRetryCount => Flow.FinalRetryCount;

    /// <summary>最终防守打到第几次尝试（首次 = 1）。</summary>
    public int FinalAttemptIndex => Flow.FinalAttemptIndex;


    /// <summary>
    /// 立即重试：按战前快照原样还原同一个世界，直接重开同一场。
    ///
    /// <para><b>不重建世界、不重跑装配</b>：<see cref="FinalSnapshot.Restore"/> 只写回字段。
    /// <b>重试次数不在这里清零</b> —— 失败那一格已经在 <see cref="RunFlow"/> 里累计过，
    /// 还原只还原世界状态，局流程的账保持不动；这里只把尝试序号 +1。</para>
    /// </summary>
    /// <returns>拒绝原因（没被拒回 <see cref="RejectReason.InvalidArgs"/>）。</returns>
    public RejectReason RetryFinalDefence()
    {
        if (Flow.Stage != RunStage.FinalDefence) return RejectReason.InvalidPhase;
        if (Battle.Occasion != BattleOccasion.None) return RejectReason.InvalidPhase;
        if (_finalSnapshot is null) return RejectReason.InvalidPhase;

        _finalSnapshot.Restore(this);
        Flow.NoteFinalRetryStarted();
        FinalRetryStarted?.Invoke(Flow.FinalAttemptIndex);
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// 结算屏上的「继续经营」：段进自由经营，目标改成自由经营；终局结算保留可回看，
    /// 之后的任何经营变化都不再改写它。
    /// </summary>
    /// <returns>受理返回 true；不在结算那一档返回 false。</returns>
    public bool ContinueBusiness() => Flow.ContinueBusiness();

    /// <summary>
    /// 防守收场的统一接线：先记流程（胜负、重试次数、防守记录），
    /// 最终防守赢了再钉「最终胜利」里程碑并开结算。
    /// </summary>
    private void OnDefenceSettled(bool won)
    {
        Flow.NoteDefenceSettled(won, Tick);
    }

    /// <summary>
    /// 形态带来的系数换档（切形态那一发调它）：只剩饥饿衰减这一档。
    /// </summary>
    private void ApplyFormFactors() => ApplyFormDecayFactor();

    /// <summary>
    /// 饥饿衰减系数每一步现算：荒漠态在沙漠带里 ×0.5，暴食态 ×1.5，其余 1.0。
    /// <b>判据是「在不在沙漠带里」</b>（与荒漠计时同一把尺子），不是「那一格填的是沙子」。
    /// </summary>
    private void ApplyFormDecayFactor()
    {
        var inDesert = BigMapTerrain.InDesertBand(Voyage.Position);
        var factor = Form switch
        {
            BeastForm.Desert when inDesert => ArenaTuning.DesertFormSatiationDecayFactor,
            BeastForm.Glutton => ArenaTuning.GluttonFormHungerDecayFactor,
            _ => 1.0,
        };

        Ecology.SetHungerDecayFactor(factor);
    }

    /// <summary>
    /// 玩家指定 10 米内一个生态对象，原地吞食五游戏分钟。
    ///
    /// <para><b>拒绝顺序</b>：参数 → 目标在册 → 场合（战斗 / 停靠 / 仪式 / 停摆）→ 饱食满 →
    /// 可吞状态 → 距离。饱食满那一档回 <see cref="RejectReason.FullCapacity"/> 并带「已经吃饱了」的明细，
    /// 不白扣任何东西。</para>
    /// </summary>
    public RejectReason EatTarget(string nodeId)
    {
        if (nodeId.Length == 0) return RejectEat(string.Empty, RejectReason.InvalidArgs);

        var node = Ecology.Find(nodeId);
        if (node is null) return RejectEat(nodeId, RejectReason.InvalidTarget);

        if (Battle.Occasion != BattleOccasion.None || IsDocked || InRitual || InStandstill)
        {
            return RejectEat(nodeId, RejectReason.InvalidPhase);
        }

        if (Ecology.Satiation >= ArenaTuning.SatiationMax - 1e-9)
        {
            return RejectEat(nodeId, RejectReason.FullCapacity);
        }

        if (!node.AvailableAt(Tick)) return RejectEat(nodeId, RejectReason.InvalidTarget);
        if (Voyage.Position.DistanceTo(node.Position) > ArenaTuning.ManualEatRangeMeters)
        {
            return RejectEat(nodeId, RejectReason.InvalidTarget);
        }

        // 觅食途中先召回再吃（手动那一口不接觅食断点）；其余阶段由 BeginManualEat 判。
        if (Voyage.Phase is VoyagePhase.DetouringToFood or VoyagePhase.Eating) Voyage.RecallForage();

        if (!Voyage.BeginManualEat(nodeId, Tick)) return RejectEat(nodeId, RejectReason.InvalidPhase);
        return RejectReason.InvalidArgs;
    }

    /// <summary>玩家召回觅食中的巨兽（回航线断点）。</summary>
    public RejectReason RecallForage()
    {
        if (Battle.Occasion != BattleOccasion.None || IsDocked || InRitual) return RejectReason.InvalidPhase;
        return Voyage.RecallForage() ? RejectReason.InvalidArgs : RejectReason.InvalidPhase;
    }

    private RejectReason RejectEat(string nodeId, RejectReason reason)
    {
        EatRejected?.Invoke(nodeId, reason);
        return reason;
    }

    // === 魔晶（生态产出 → 甲板 → 仓库 → 用途 / 出售 / 土壤嵌入） ===

    /// <summary>仓库里的魔晶（整单位；账本那一列是 double）。</summary>
    public int CrystalWarehouse => (int)Math.Floor(Town.Crystal + 1e-9);

    /// <summary>
    /// 甲板上还没被人拿走的魔晶（<b>与「搬运中」互斥</b>：拿在手上的那一件算搬运中）。
    /// </summary>
    public int CrystalOnDeck
    {
        get
        {
            var n = 0;
            foreach (var item in Items.Live)
            {
                if (item.Kind != PointItemKind.Crystal) continue;
                if (IsCarried(item.Id)) continue;
                n++;
            }

            return n;
        }
    }

    /// <summary>正被人拿在手上的魔晶（搬运第二程那一档）。</summary>
    public int CrystalCarrying
    {
        get
        {
            var n = 0;
            foreach (var unit in Roster.Units)
            {
                if (CarryingOf(unit.Id) is not { } order) continue;
                if (Items.Find(order.ItemId)?.Kind == PointItemKind.Crystal) n++;
            }

            return n;
        }
    }

    /// <summary>这一件点位物是不是正被某个人拿在手上（搬运第二程）。</summary>
    private bool IsCarried(string itemId)
    {
        foreach (var unit in Roster.Units)
        {
            if (CarryingOf(unit.Id) is { } order && order.ItemId == itemId) return true;
        }

        return false;
    }

    /// <summary>一单位魔晶的成交价：基准 500 × 空艇（窗口内）或城市物价倍率。</summary>
    public double CrystalUnitPrice
        => ArenaTuning.CrystalBaseValue * (InAirshipWindow ? AirshipPriceFactor : CityPriceFactor);

    /// <summary>
    /// 魔晶的三条主动用途（补魔 / 治疗 / 修理），<b>一律从仓库扣</b>；三条都可在战斗中使用。
    /// </summary>
    public RejectReason UseCrystal(CrystalUseKind use, string targetId)
    {
        if (Town.Crystal < 1.0) return RejectReason.InsufficientMaterial;

        switch (use)
        {
            case CrystalUseKind.Mana:
                if (ArenaTuning.ManaCapacity - Town.AvailableMana < ArenaTuning.CrystalManaMinGap)
                {
                    return RejectReason.InsufficientMana;
                }

                if (!Town.TrySpendCrystal(1.0)) return RejectReason.InsufficientMaterial;
                Town.NoteCrystalUsed();
                Town.RestoreManaFromCrystal(ArenaTuning.CrystalManaRestore);
                CrystalUsed?.Invoke(new CrystalUseRecord(Tick, use, string.Empty));
                return RejectReason.InvalidArgs;

            case CrystalUseKind.Heal:
                {
                    var unit = Roster.Find(targetId);
                    if (unit is null || unit.LeftTown) return RejectReason.InvalidTarget;
                    if (unit.State != UnitState.Wounded) return RejectReason.InvalidPhase;

                    if (!Town.TrySpendCrystal(1.0)) return RejectReason.InsufficientMaterial;
                    Town.NoteCrystalUsed();
                    unit.State = UnitState.Standing;
                    unit.Hp = ArenaTuning.UnitMaxHp;
                    CrystalUsed?.Invoke(new CrystalUseRecord(Tick, use, targetId));
                    return RejectReason.InvalidArgs;
                }

            case CrystalUseKind.Repair:
                if (Construction.Find(targetId) is null) return RejectReason.InvalidTarget;
                if (!Deck.IsRepairing(targetId)) return RejectReason.InvalidPhase;

                if (!Town.TrySpendCrystal(1.0)) return RejectReason.InsufficientMaterial;
                Town.NoteCrystalUsed();
                Deck.FinishRepairNow(targetId);
                CrystalUsed?.Invoke(new CrystalUseRecord(Tick, use, targetId));
                return RejectReason.InvalidArgs;

            default:
                return RejectReason.InvalidArgs;
        }
    }

    /// <summary>
    /// 出售仓库里的魔晶：基准价 × 城市或空艇价格倍率；停靠期或空艇成交窗口内才卖得出去。
    /// </summary>
    public RejectReason SellCrystal(int count)
    {
        if (count <= 0) return RejectReason.InvalidArgs;
        if (!IsDocked && !InAirshipWindow) return RejectReason.InvalidPhase;
        if (Town.Crystal < count) return RejectReason.InsufficientMaterial;

        var unitPrice = CrystalUnitPrice;
        Town.TrySpendCrystal(count);
        Town.NoteCrystalSold(count);
        Town.ProduceCoin(unitPrice * count);
        CrystalSold?.Invoke(new CrystalSaleRecord(Tick, count, unitPrice));
        return RejectReason.InvalidArgs;
    }

    /// <summary>某件魔晶入库之后抛一次（搬运那一格与上帝之手那一格共用）。</summary>
    public void NoteCrystalWarehoused(string itemId) => CrystalWarehoused?.Invoke(itemId);

    /// <summary>一次普通修理扣完备件之后抛一次（修理受理与备件投递两条成交路径共用）。</summary>
    public void NoteRepairSpent(string buildingId, double spareParts)
        => RepairSpent?.Invoke(new RepairSpentRecord(Tick, buildingId, spareParts));

    /// <summary>一次法术治疗扣完法力之后抛一次（<c>CastHeal</c> 成交那一格调）。</summary>
    public void NoteHealingSpent(string unitId, double mana)
        => HealingSpent?.Invoke(new HealingSpentRecord(Tick, unitId, mana));

    /// <summary>生态产出魔晶：在甲板指定位置逐件落点位物，等人搬进仓库。</summary>
    private void SpawnCrystals(int count)
    {
        if (count <= 0) return;

        var at = NearestFreeDeckCell(ArenaTuning.CrystalDropDeckX, ArenaTuning.CrystalDropDeckZ);
        for (var i = 0; i < count; i++) Items.Add(PointItemKind.Crystal, at, "crystal");

        CrystalsProduced?.Invoke(count);
    }


    /// <summary>
    /// 进阶门槛：<b>二阶三项同时满足</b> —— 同一象限挖到第二层、该象限洞穴已清剿、
    /// 该格已嵌入魔晶。三项在同一次嵌入成功时一起落进
    /// <see cref="BeastGrowth.Tier2Eligible"/> 这个永久标记，之后不再随格子状态回退
    /// （嵌入本身就永久停止回长，所以它也没有回退的机会）。
    ///
    /// <para><b>三阶在本切片内永远不成立</b>：地下只有两层，第三层不存在；
    /// 这里对三阶恒回 false。三阶的代价与收益表留在
    /// <see cref="ArenaTuning.TierCostByTarget"/> / <see cref="ArenaTuning.TierStatsByTier"/>，
    /// 但通向它的门是关的 —— 不把「删一半」的半截规则留在代码里。</para>
    /// </summary>
    public bool GateMet(BeastTier target) => target switch
    {
        BeastTier.Tier2 => Growth.Tier2Eligible,
        _ => false,
    };

    /// <summary>
    /// 「嵌入魔晶」的<b>只读判据</b>（停靠期、已挖开、洞穴层、已清剿、同格未嵌入、仓库有 1 单位）。
    /// 面板置灰与 <see cref="EmbedCrystal"/> 都只读这一处，不各写一份。
    /// </summary>
    public SoilEmbedBlocker EmbedBlockerOf(int sector, int layer)
    {
        if (!IsDocked) return SoilEmbedBlocker.NotDocked;
        if (Battle.Occasion != BattleOccasion.None || InStandstill || InRitual)
        {
            return SoilEmbedBlocker.Busy;
        }

        if (sector < 0 || sector >= UndergroundSoil.Sectors
            || layer < 1 || layer > UndergroundSoil.Layers)
        {
            return SoilEmbedBlocker.OutOfRange;
        }

        if (!Soil.IsDug(sector, layer)) return SoilEmbedBlocker.NotDug;
        if (layer != UndergroundSoil.NativeCaveLayer) return SoilEmbedBlocker.NotCaveLayer;
        if (!Soil.IsCleared(sector, layer)) return SoilEmbedBlocker.NotCleared;
        if (Soil.IsEmbedded(sector, layer)) return SoilEmbedBlocker.AlreadyEmbedded;
        if (Town.Crystal < 1.0) return SoilEmbedBlocker.NoCrystal;
        return SoilEmbedBlocker.None;
    }

    /// <summary>
    /// 把 1 单位魔晶嵌入某一格：<b>永久</b>停止回长，特殊材料产出提速到每岗 2 游戏小时一件，
    /// 并把二阶的进阶资格永久记下来。
    ///
    /// <para><b>扣魔晶与改格状态分两步，先判后扣</b>：先问 <see cref="EmbedBlockerOf"/>
    /// （含仓库余量），全过才扣、才改格 —— 扣完才发现落不成会留下一笔半截的账。
    /// 魔晶那一本只有 <see cref="TownAccounts.Crystal"/> 一处（生态产出、搬运入库、用途 / 出售
    /// 与这里的嵌入扣减都读同一本）。</para>
    /// </summary>
    /// <returns>拒绝原因（没被拒回 <see cref="RejectReason.InvalidArgs"/>，与本类其余写口同一口径）。</returns>
    public RejectReason EmbedCrystal(int sector, int layer)
    {
        var blocker = EmbedBlockerOf(sector, layer);
        if (blocker != SoilEmbedBlocker.None) return RejectReasonOf(blocker);

        if (!Town.TrySpendCrystal(1.0)) return RejectReason.InsufficientMaterial;
        Town.NoteCrystalUsed();
        if (!Soil.MarkEmbedded(sector, layer))
        {
            // 判据与写入之间没有任何一步会改这一格；万一落不成，把扣掉的那一单位原路退回。
            Town.RefundCrystal(1.0);
            return RejectReason.InvalidPhase;
        }

        CrystalEmbedded?.Invoke(sector, layer);
        Growth.RecordTier2Eligibility(sector, layer);
        return RejectReason.InvalidArgs;
    }

    /// <summary>嵌入那一档拦路 → 词表用的拒绝码（<see cref="RejectReason"/> 仍是那 13 项，不加新的）。</summary>
    public static RejectReason RejectReasonOf(SoilEmbedBlocker blocker) => blocker switch
    {
        SoilEmbedBlocker.NotDocked => RejectReason.InvalidPhase,
        SoilEmbedBlocker.OutOfRange => RejectReason.InvalidArgs,
        SoilEmbedBlocker.NotDug => RejectReason.InvalidPhase,
        SoilEmbedBlocker.NotCaveLayer => RejectReason.InvalidArgs,
        SoilEmbedBlocker.NotCleared => RejectReason.InvalidPhase,
        SoilEmbedBlocker.AlreadyEmbedded => RejectReason.InvalidPhase,
        SoilEmbedBlocker.NoCrystal => RejectReason.InsufficientMaterial,
        _ => RejectReason.InvalidPhase,
    };

    /// <summary>
    /// 发起进阶仪式：门槛过了、代价付得起，就开一场 1 游戏小时的仪式。
    ///
    /// <para><b>判定顺序固定四档</b>：最高位阶 → 阶段 → 门槛 → 资源。
    /// 资源那一档内部再按「魂石 → 材料 → 金币」读三本余额，<b>全够了才动手扣</b> ——
    /// 三本账的扣是三个独立动作，先扣两项才发现第三项不够，就成了一笔半截的账。</para>
    ///
    /// <para><b>仪式期间</b>：产出、采矿、办赛、游客入口全停；已在镇游客的消费与离场、
    /// 修房、搬运、治伤复活、土壤回长照常。巨兽原地不动（它本来就在停靠态）。</para>
    /// </summary>
    /// <returns>拒绝原因（没被拒回 <see cref="RejectReason.InvalidArgs"/>，与本类其余写口同一口径）。</returns>
    public RejectReason AdvanceTier()
    {
        if (Tier == BeastTier.Tier3) return RejectReason.TierAtMax;
        if (!IsDocked) return RejectReason.InvalidPhase;
        if (Battle.Occasion != BattleOccasion.None) return RejectReason.InvalidPhase;
        if (InStandstill) return RejectReason.InvalidPhase;
        if (InRitual) return RejectReason.InvalidPhase;

        var target = (BeastTier)((int)Tier + 1);
        if (!GateMet(target)) return RejectReason.GateNotMet;

        var cost = ArenaTuning.CostOf(target);
        var soul = cost.SoulStone;
        var material = cost.SpecialMaterial;
        var coin = cost.Coin;

        if (Town.SoulStone < soul) return RejectReason.InsufficientSoulStone;
        if (Town.SpecialMaterial < material) return RejectReason.InsufficientMaterial;
        if (Town.Coin < coin) return RejectReason.InsufficientCoin;

        Town.TrySpendSoulStone(soul);
        Town.TrySpendSpecialMaterial(material);
        Town.TrySpendCoin(coin);

        Growth.BeginRitual(Tick, ArenaTime.StepsPerGameHour * ArenaTuning.TierRitualGameHours);
        TierUpgradeStarted?.Invoke(target, Growth.RitualUntilTick);
        return RejectReason.InvalidArgs;
    }

    /// <summary>
    /// 离给定点最近的一格可通行格心（那栋楼不在册时的落点）。
    /// <para><paramref name="deckX"/> / <paramref name="deckZ"/> 是<b>甲板逻辑坐标（米）</b>，
    /// 进这一口先除格长换成格号 —— 拿米当格号用，格长一改就整片甲板错位。</para>
    /// </summary>
    public NavPoint NearestFreeDeckCell(double deckX, double deckZ)
    {
        var anchorCol = (int)Math.Floor(deckX / ArenaTuning.DeckCellMeters);
        var anchorRow = (int)Math.Floor(deckZ / ArenaTuning.DeckCellMeters);
        for (var radius = 0; radius <= DeckCells; radius++)
        {
            for (var dRow = -radius; dRow <= radius; dRow++)
            {
                for (var dCol = -radius; dCol <= radius; dCol++)
                {
                    if (radius > 0 && Math.Abs(dRow) != radius && Math.Abs(dCol) != radius) continue;
                    var col = anchorCol + dCol;
                    var row = anchorRow + dRow;
                    if (!IsDeckCellInBounds(new GridCell(col, row))) continue;
                    if (DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(new GridCell(col, row)))) continue;
                    return new NavPoint(CellCenterOf(col), CellCenterOf(row));
                }
            }
        }

        return new NavPoint(deckX, deckZ);
    }

    /// <summary>
    /// 一个建造格号的格心落在甲板逻辑坐标几米（<b>格号 × 格长 + 半格</b>）。
    /// 全仓只有这一口算「格 → 米」的中心，写死字面量的话格长翻倍必然漏掉一处。
    /// </summary>
    private static double CellCenterOf(int cellIndex)
        => (cellIndex + 0.5) * ArenaTuning.DeckCellMeters;

    /// <summary>
    /// 空艇到访数：<c>AirshipDock</c> 在册是 1..3，否则 0..2。
    /// <b>由（种子, 城市序号）派生，不掷点</b> —— 城市 id 的字符串哈希每次进程都不一样，
    /// 拿它当种子会让重放跨进程分叉，所以那一档只按城市在 <see cref="BigMapGrid.Places"/> 里的序号。
    /// </summary>
    public int AirshipVisitsFor(string cityId)
    {
        var cityIndex = -1;
        for (var i = 0; i < BigMapGrid.Places.Count; i++)
        {
            if (BigMapGrid.Places[i].Id == cityId) cityIndex = i;
        }

        if (cityIndex < 0) return 0;

        var withDock = HasBlueprint("AirshipDock");
        var lowest = withDock ? 1 : 0;
        var span = withDock ? 3 : 2;
        var draw = PeopleRoster.DailyDraw(Seed, cityIndex, 7);
        return lowest + (int)(draw * span);
    }

    /// <summary>册上有没有某一件蓝图（<b>按蓝图查</b>：新建的 bN 也算；固定 id 被拆后自动回退）。</summary>
    public bool HasBlueprint(string blueprintId) => FindByBlueprint(blueprintId).Length > 0;

    /// <summary>停摆期到点：城卫队解救。</summary>
    public void Rescue()
    {
        // 状态先落（解除攻破与停摆、把待记的债交回来），破坏性后果仍在世界这一侧施加。
        var (debt, faction) = Siege.Rescue();

        var hall = Construction.Find(TownHallId);
        if (hall is not null) Construction.SetHealth(TownHallId, ArenaTuning.RescueTownHallDurability);

        if (debt > 0.0 && faction.Length > 0) Town.AccrueDebt(faction, debt);

        // 巨兽恢复向原目标城航行（没有原目标就留在原地）。
        if (Voyage.Phase == VoyagePhase.Holding && Voyage.TargetCityId is { } back)
        {
            OrderSailTo(back);
        }
    }

    /// <summary>
    /// 攻破那一格：结束防守 → 清退余敌 → 结算战损 → 折债 → <b>立即解救并记债</b>。
    ///
    /// <para><b>ticket 06 起不再停摆三个游戏日</b>：城破当格由城卫队到场，
    /// 镇政府耐久回到 100、债记进最近那家势力的负债账、巨兽恢复向原目标城航行。
    /// 债务、通缉与拒停仍按原规则继续生效（通缉在下一个整游戏日判）。</para>
    ///
    /// <para><b>最终防守被攻破只收场</b>：不毁建筑、不掳人、不记债、不解救；
    /// 玩家在战报上选「立即重试」，世界按战前快照原样回到开战前。</para>
    /// </summary>
    public void ReportBreach()
    {
        if (Siege.Breached) return;

        // 最终防守：城破只算这一场失败，不落到普通城破那串后果上。
        // 第二次进来（同一格之后的每分钟复查）靠 `_finalDefenceCollapsed` 拦住。
        if (Battle.Occasion == BattleOccasion.FinalDefence || _finalDefenceCollapsed)
        {
            if (Battle.Occasion != BattleOccasion.FinalDefence) return;
            _finalDefenceCollapsed = true;
            Battle.FailFinalDefence(Tick);
            return;
        }

        // ① 按造价从高到低（同造价按 id 字典序）挑出 3 栋普通建筑。
        //    这里只挑不拆：拆之前要先把攻破状态落下来 —— 收场那一格靠它判「这一场不算清完」。
        var ordinary = new List<Building>();
        foreach (var id in Construction.Ids)
        {
            var b = Construction.Find(id);
            if (b is null || b.Id == TownHallId) continue;
            ordinary.Add(b);
        }

        ordinary.Sort((a, b) =>
        {
            var costA = CostOf(a.BlueprintId);
            var costB = CostOf(b.BlueprintId);
            if (costA != costB) return costB.CompareTo(costA);      // 从高到低
            return string.CompareOrdinal(a.Id, b.Id);                // 同价按 id 字典序
        });

        var warLoss = new List<string>();
        var estimated = 0.0;
        for (var i = 0; i < ordinary.Count && i < ArenaTuning.WarLossBuildings; i++)
        {
            warLoss.Add(ordinary[i].Id);
            estimated += CostOf(ordinary[i].BlueprintId);
        }

        // ② 按名单序号挑前 7 名镇民（每人折 100 金币，不留尸不出魂石）。
        var taken = new List<string>();
        foreach (var u in Roster.Townspeople)
        {
            if (taken.Count >= ArenaTuning.WarLossTakenPeople) break;
            taken.Add(u.Id);
            estimated += ArenaTuning.WarLossPersonValue;
        }

        // ③ 仓库矿石与特殊材料各扣 30%（向下取整）；扣多少先算，状态落了再动账。
        var ore = Math.Floor(Town.Ore * ArenaTuning.WarLossGoodsShare);
        var mat = Math.Floor(Town.SpecialMaterial * ArenaTuning.WarLossGoodsShare);
        estimated += (ore * ArenaTuning.OreValue) + (mat * ArenaTuning.MaterialValue);

        // ④ 折债与挑解救势力住在 SiegeState 里，状态在这一格落下。
        Siege.BeginBreach(Tick, NearestFactionId(), estimated, warLoss, taken);

        // ⑤ 破坏性后果在世界这一侧施加（顺序照旧：毁建筑 → 掳人 → 扣货 → 收场 → 兽离镇）。
        foreach (var id in warLoss)
        {
            Roster.ClearPostsOf(id);
            Construction.Remove(id, RemovalCause.Destroyed);
        }

        foreach (var id in taken)
        {
            if (Roster.Find(id) is { } unit) Roster.LeaveTown(unit);
        }

        if (ore > 0.0) Town.TrySpendOre(ore);
        if (mat > 0.0) Town.TrySpendSpecialMaterial(mat);

        // 当场结束防守（余敌在 BattleSim 收场那一格撤退）。
        Battle.ForceEnd(Tick);

        // 城卫队当格到场：解除攻破、镇政府耐久回到 100、债记进最近那家势力的负债账，
        // 巨兽恢复向原目标城航行。**不再排三个游戏日的停摆期。**
        Rescue();
    }

    /// <summary>按名单序号吓跑游客（塌方每 5 个取 1、攻破每 10 个取 3，不掷点）。</summary>
    public int FrightenVisitors(int take, int per) => Visitors.Frighten(take, per);

    /// <summary>塌方那一格：情绪 −10、游客吓跑 20%（<b>在镇每 5 人取 1 人</b>）。</summary>
    public void ApplyCollapse()
    {
        foreach (var u in Roster.Units)
        {
            if (!u.IsTownsperson || u.LeftTown) continue;
            u.Mood = Math.Clamp(u.Mood - ArenaTuning.CollapseTownMoodDrop, ArenaTuning.MoodMin, ArenaTuning.MoodMax);
        }

        FrightenVisitors(1, 5);
    }

    /// <summary>复活一具尸体（教堂：10 魂石 + 1 游戏小时，限 24 游戏小时内死的）。</summary>
    public bool BeginRevive(string corpseId, string personId, long tick)
    {
        if (_reviving.ContainsKey(corpseId)) return false;
        if (!Town.TrySpendSoulStone(ArenaTuning.ReviveSoulStones)) return false;

        _reviving[corpseId] = (tick + (ArenaTime.StepsPerGameHour * ArenaTuning.ReviveGameHours), personId, corpseId);
        return true;
    }

    /// <summary>召唤一个帮工（占 1 格法力；无薪、无口粮、无尸体）。</summary>
    public ArenaUnit? SummonHelper()
    {
        if (!Town.TryHoldMana(TownAccounts.ManaHold.Helper)) return null;

        var id = $"h{Roster.Units.Count + 1}";
        while (_capturedHelpers.Contains(id)) id = $"h{Roster.Units.Count + 1}";
        var helper = Roster.Add(id, UnitRole.Helper);
        _capturedHelpers.Add(id);
        return helper;
    }

    /// <summary>遣散一个帮工（归还那一格法力）。</summary>
    public bool DismissHelper(string helperId)
    {
        var helper = Roster.Find(helperId);
        if (helper is null || !helper.IsHelper || helper.LeftTown) return false;

        Roster.LeaveTown(helper);
        Town.ReleaseMana(TownAccounts.ManaHold.Helper);
        return true;
    }

    // === 教堂在册尸体（拖进教堂的那几具） ===

    /// <summary>
    /// 教堂在册尸体的一条 —— <b>一具尸体的登记行</b>。
    ///
    /// <param name="CorpseId">尸体 id（点位物那一件）。</param>
    /// <param name="Name">倒下的是谁（人 id —— 这一仓没有第二个名字可写）。</param>
    /// <param name="EnteredTick">入册那一刻的步号。</param>
    /// <param name="AgeGameMinutes">入册之后过了多少游戏分钟。</param>
    public sealed record ChurchCorpseRecord(
        string CorpseId, string Name, long EnteredTick, long AgeGameMinutes);

    /// <summary>教堂在册表（私有那一本：id、倒下的是谁、入册步号）。</summary>
    private readonly List<(string CorpseId, string Name, long EnteredTick)> _churchCorpses = [];

    /// <summary>
    /// 把一具尸体记进教堂在册表 —— <b>拖进教堂那一路（ticket 08）调它</b>。
    ///
    /// <para><b>入册即计时</b>：年龄从 <paramref name="enteredTick"/> 起算，
    /// 超过 <see cref="ArenaTuning.ReviveWindowGameHours"/> 个游戏小时就过期（救不回来）。
    /// 同一个尸体 id 再入册一次就改写那一条（不新增第二条）。</para>
    /// </summary>
    /// <param name="corpseId">尸体 id（点位物那一件）。</param>
    /// <param name="name">倒下的是谁（人 id）。</param>
    /// <param name="enteredTick">入册那一刻的步号。</param>
    public void RegisterChurchCorpse(string corpseId, string name, long enteredTick)
    {
        if (corpseId.Length == 0) throw new ArgumentException("尸体 id 不能是空串", nameof(corpseId));

        for (var i = 0; i < _churchCorpses.Count; i++)
        {
            if (_churchCorpses[i].CorpseId != corpseId) continue;
            _churchCorpses[i] = (corpseId, name, enteredTick);
            return;
        }

        _churchCorpses.Add((corpseId, name, enteredTick));
    }

    /// <summary>教堂在册尸体（全部，按入册次序；每一条带当下的年龄）。</summary>
    public IReadOnlyList<ChurchCorpseRecord> ChurchCorpses
    {
        get
        {
            var list = new List<ChurchCorpseRecord>(_churchCorpses.Count);
            foreach (var (corpseId, name, enteredTick) in _churchCorpses)
            {
                list.Add(new ChurchCorpseRecord(
                    corpseId, name, enteredTick, (Tick - enteredTick) / ArenaTime.StepsPerGameMinute));
            }

            return list;
        }
    }

    /// <summary>教堂在册尸体里<b>没过期的</b>那些（年龄不超过复活窗口的那些）。</summary>
    public IReadOnlyList<ChurchCorpseRecord> ChurchCorpsesInWindow
    {
        get
        {
            var list = new List<ChurchCorpseRecord>();
            foreach (var record in ChurchCorpses)
            {
                if (!IsChurchCorpseExpired(record)) list.Add(record);
            }

            return list;
        }
    }

    /// <summary>在册表里按尸体 id 查一条（查不到回 null）。</summary>
    public ChurchCorpseRecord? FindChurchCorpse(string corpseId)
    {
        foreach (var record in ChurchCorpses)
        {
            if (string.Equals(record.CorpseId, corpseId, StringComparison.Ordinal)) return record;
        }

        return null;
    }

    /// <summary>从在册表里摘掉一条（复活那一发调它；不在册回 false）。</summary>
    public bool RemoveChurchCorpse(string corpseId)
    {
        for (var i = 0; i < _churchCorpses.Count; i++)
        {
            if (_churchCorpses[i].CorpseId != corpseId) continue;

            _churchCorpses.RemoveAt(i);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 这一具在册尸体过期了没有 —— <b>年龄超过 <see cref="ArenaTuning.ReviveWindowGameHours"/>
    /// 个游戏小时就过期</b>（刚好等于窗口的那一档还算数）。
    /// </summary>
    public bool IsChurchCorpseExpired(ChurchCorpseRecord record)
        => Tick - record.EnteredTick > (ArenaTuning.ReviveWindowGameHours * ArenaTime.StepsPerGameHour);

    // === 镇民与帮工那两份名单（判据只有 <see cref="ArenaUnit.IsTownsperson"/> 一处） ===

    /// <summary>
    /// 这一件是不是在册镇民 —— <b>「镇民」只有这一处定义</b>（<see cref="ArenaUnit.IsTownsperson"/>）。
    ///
    /// <para>帮工、游客、敌人、怪物都不是镇民；离镇的也不在册。</para>
    /// </summary>
    public bool IsTownsperson(string unitId)
    {
        var unit = Roster.Find(unitId);
        return unit is not null && !unit.LeftTown && unit.IsTownsperson;
    }

    /// <summary>在册镇民（传送那一份名单用这一口）。</summary>
    public IReadOnlyList<ArenaUnit> Townspersons
    {
        get
        {
            var list = new List<ArenaUnit>();
            foreach (var unit in Roster.Units)
            {
                if (unit.LeftTown || !unit.IsTownsperson) continue;
                list.Add(unit);
            }

            return list;
        }
    }

    /// <summary>在册帮工（帮工弹层那一份名单用这一口）。</summary>
    public IReadOnlyList<ArenaUnit> Helpers
    {
        get
        {
            var list = new List<ArenaUnit>();
            foreach (var unit in Roster.Units)
            {
                if (unit.LeftTown || !unit.IsHelper) continue;
                list.Add(unit);
            }

            return list;
        }
    }

    /// <summary>把矿石 / 特殊材料直接送进镇政府仓库（电梯那一口；不经地面二次搬运）。</summary>
    public void DeliverToWarehouse(double ore, double material)
    {
        if (ore > 0.0) Town.ProduceOre(ore);
        if (material > 0.0) Town.ProduceSpecialMaterial(material);
    }

    /// <summary>人上下井那一格（电梯派活；回 false = 这一格不算）。</summary>
    public bool BoardShaft(string unitId)
    {
        var u = Roster.Find(unitId);
        return u is not null && u.CanFight && !u.LeftTown;
    }

    /// <summary>一张空的搬运单（供词表那一侧与测试构造用）。</summary>
    public static HaulOrder Order(
        HaulLevel level, string itemId, NavPoint origin, string target, double amount, long tick)
        => new(level, itemId, origin, target, amount, tick);

    // === 私产 ===

    /// <summary>交单那一本留最近这么几条（定长：旧账先掉出去，不进快照也不进检查点）。</summary>
    private const int HaulLandingLedgerSize = 16;

    /// <summary>
    /// 留名账留最近这么几条（定长：与交单账同一口径）。
    /// 这一本进检查点快照，所以掉条数由快照头部那一对读数摆着，不靠读源码才看得出来。
    /// </summary>
    public const int StarSaleLedgerSize = 16;

    private void SettleStandstill()
    {
        // 到期判定住在 SiegeState；解救的破坏性后果（改耐久、记债、恢复航行）仍在世界这一侧。
        if (Siege.RescueDueAt(Tick)) Rescue();
    }

    private void SettleGameDay(long tick)
    {
        // 工资：上一份工钱还欠着 —— 欠薪天数 +1，忠心按天扣（最低 0）。
        // 扣忠心那一格在 Roster.SettleDay 里面（记欠薪 → 日结按 LoyaltyDailyUnpaid 扣一次）。
        if (WagesDue > 0.0)
        {
            ArrearsDays += 1;
            MarkTownspersonsUnpaid();
        }

        // 今天的工钱挂上：按角色算（工作人员 2 / 角斗士 3 / 自由人 1）。
        WagesDue += WageBill();

        // 口粮：每人每日 1 份。巨兽的日结自动喂食已删，这一格只扣人口口粮。
        var demand = Roster.SettleDay(tick, WagesPaidThisDay);
        if (demand > 0.0) Town.TrySpendFood(demand);
        WagesPaidThisDay = 0.0;

        Visitors.RegenDay();
        CheckWanted(tick);
    }

    /// <summary>
    /// 今天的工钱该发多少（金币）—— <b>按角色算</b>：工作人员
    /// <see cref="ArenaTuning.WorkerDailyWage"/>、角斗士
    /// <see cref="ArenaTuning.GladiatorDailyWage"/>、自由人
    /// <see cref="ArenaTuning.FreePersonDailyWage"/>。
    ///
    /// <para>只算在册且没离镇的镇民（离镇的人不领工钱）。
    /// 三个日薪的数只写一份，就在 <see cref="ArenaTuning"/> 那一处。</para>
    /// </summary>
    public double WageBill()
    {
        var bill = 0.0;
        foreach (var u in Roster.Units)
        {
            if (!u.IsTownsperson || u.LeftTown) continue;
            bill += u.Role switch
            {
                UnitRole.Gladiator => ArenaTuning.GladiatorDailyWage,
                UnitRole.FreePerson => ArenaTuning.FreePersonDailyWage,
                _ => ArenaTuning.WorkerDailyWage,
            };
        }

        return bill;
    }

    /// <summary>
    /// 发薪：把应发的工钱一次结清（<b>只结应发的那一份</b>，不多给也不少给）。
    ///
    /// <para><b>钱走金币账那一条路</b>（<see cref="TownAccounts.TrySpendCoin"/>）：
    /// 付不动就一子儿不动，八条守恒等式照样平。
    /// 结清之后应发归零、欠薪天数归零。</para>
    /// </summary>
    /// <returns>结清返回 true；没有应发或金币不够返回 false。</returns>
    public bool TryPayWages()
    {
        if (WagesDue <= 0.0) return false;
        if (!Town.TrySpendCoin(WagesDue)) return false;

        WagesPaidThisDay += WagesDue;
        WagesDue = 0.0;
        ArrearsDays = 0;
        return true;
    }

    /// <summary>把在册的镇民都记成欠薪一天（下一格日结按那一档扣忠心与情绪）。</summary>
    private void MarkTownspersonsUnpaid()
    {
        foreach (var u in Roster.Units)
        {
            if (!u.IsTownsperson || u.LeftTown) continue;
            Roster.MarkUnpaidWage(u);
        }
    }

    private void CheckWanted(long tick)
    {
        var overDebt = Town.TotalDebt > ArenaTuning.WantedDebtThreshold;
        var overdue = BreachTick >= 0
            && (ArenaTime.GameDaysAt(tick) - ArenaTime.GameDaysAt(BreachTick))
                >= ArenaTuning.WantedGameDaysAfterBreach
            && Town.TotalDebt > 0.0;

        Wanted = overDebt || overdue;

        // 讨债队：通缉期间每 7 个游戏日来一队，上门扣走金币账里够抵的部分。
        if (!Wanted) return;
        if (tick % (ArenaTime.StepsPerGameDay * ArenaTuning.DebtCollectionIntervalGameDays) != 0) return;
        if (Town.Coin <= 0.0) return;

        foreach (var c in Town.Creditors)
        {
            if (Town.Coin <= 0.0) break;
            Town.PayDebt(c.FactionId, Town.Coin);
        }
    }

    private void CheckBreach()
    {
        var hall = Construction.Find(TownHallId);
        if (hall is null) return;

        // 耐久是真的 0：建造模块在归零那一格问过 <see cref="RemoveOnZero"/>，镇政府答「留」，
        // 所以这一行读得到它在册、读得到它打空了。不带容差也不需要带。
        if (hall.Health > 0.0) return;

        ReportBreach();
    }

    private double CorpseMoodDeltaAt(NavPoint at)
    {
        var delta = 0.0;
        foreach (var corpse in Items.LiveCorpses)
        {
            if (at.DistanceTo(corpse.AtDeck) > ArenaTuning.CorpseAuraRadiusMeters) continue;
            delta += ArenaTuning.MoodCorpseAuraPerGameHour;
        }

        return delta;
    }

    private void SettleRevives(long tick)
    {
        foreach (var key in new List<string>(_reviving.Keys))
        {
            var (done, personId, corpseId) = _reviving[key];
            if (tick < done) continue;

            _reviving.Remove(key);
            var corpse = Items.Find(corpseId);
            corpse?.Retire();
            var u = Roster.Find(personId);
            if (u is null) continue;

            u.State = UnitState.Standing;
            u.Hp = ArenaTuning.UnitMaxHp;
            u.LeftTown = false;
        }
    }

    private void SyncSoilCrew()
    {
        foreach (var buildingId in Construction.Ids)
        {
            var b = Construction.Find(buildingId);
            if (b is null) continue;
            Deck.SetBuildWorkers(buildingId, Roster.PostedCount(buildingId));
        }
    }

    /// <summary>
    /// 把土壤这一步<b>新</b>产出的塌方按发生次序各应用一次。
    ///
    /// <para><b>为什么要一个游标</b>：<see cref="UndergroundSoil.Collapses"/> 只追加、不清空，
    /// 所以上一次已经应用到第几格记一个游标就够 —— 这一格只处理游标之后新冒出来的那几格，
    /// 塌一次调一次，旧的不会被重调一遍。</para>
    /// </summary>
    private void ApplyFreshCollapses()
    {
        var collapses = Soil.Collapses;
        for (var i = _appliedCollapseCount; i < collapses.Count; i++) ApplyCollapse();

        _appliedCollapseCount = collapses.Count;
    }

    /// <summary>
    /// 搬运派单：空闲的工作人员与帮工共享一条队列，<b>人先货后</b>由电梯那一档管，
    /// 这一档只管地面那三级优先级。
    /// </summary>
    private void DispatchHaul()
    {
        foreach (var u in Roster.Units)
        {
            if (_carrying.ContainsKey(u.Id)) continue;
            if (!u.CanFight || u.LeftTown) continue;
            if (u.Post.Length > 0) continue;
            if (u.Role != UnitRole.Worker && !u.IsHelper) continue;

            var order = Haul.TakeNext();
            if (order is null) return;

            _carrying[u.Id] = new HaulInHand(order, HaulLeg.ToPickup);
            WalkToPickup(u, order);
        }
    }

    /// <summary>
    /// 交单那一格：走到取货点就装货转第二程，走到目标门口就投进去并把人放回空闲。
    ///
    /// <para><b>四个档次走同一条交单写路</b>（<see cref="ArenaSetup.DeliverItem"/>）——
    /// 与上帝之手 <c>DropToBuilding</c> 是同一份实现，搬运那一摊不另写一套。</para>
    ///
    /// <para><b>三条「不等货到就不结单」的读数</b>，每一条都是为了不把某一单永远钉在一个人手上：
    /// 货不在册就不进第二程（等货重新入册，而不是凭空交单）；
    /// 目标那一栋已不在册就作废这一纸（塌方 / 建筑被拆）；货在半路上没了就作废
    /// （已被别人投掉 —— 那是防重交的那一道）。</para>
    /// </summary>
    private void SettleHaulArrivals()
    {
        foreach (var u in Roster.Units)
        {
            if (!_carrying.TryGetValue(u.Id, out var inHand)) continue;

            // 人已经不在镇里：手上那一纸退回重排或作废（货不丢）。
            if (u.LeftTown)
            {
                AbandonHaulOf(u.Id);
                continue;
            }

            var order = inHand.Order;
            if (inHand.Leg == HaulLeg.ToPickup)
            {
                if (Items.Find(order.ItemId) is null) continue;
                if (!ArrivedAt(u, order.OriginDeck)) continue;

                _carrying[u.Id] = new HaulInHand(order, HaulLeg.ToDeliver);
                WalkToDelivery(u, order);
                continue;
            }

            if (Construction.Find(order.TargetBuildingId) is null)
            {
                NoteLanding(u, order, false, $"目标不在册（{order.TargetBuildingId}）");
                ReleaseHaulOf(u.Id);
                continue;
            }

            if (!ArrivedAt(u, DoorDeckOf(order.TargetBuildingId))) continue;

            DeliverHaul(u, order);
        }
    }

    /// <summary>交单：走玩家那一发投递的同一份写路径，投进去与被拒都把人放回空闲。</summary>
    private void DeliverHaul(ArenaUnit carrier, HaulOrder order)
    {
        var item = Items.Find(order.ItemId);
        var reason = item is null ? "货不在册" : string.Empty;
        var landed = item is not null
            && ArenaSetup.DeliverItem(this, item, order.TargetBuildingId, ref reason);

        NoteLanding(carrier, order, landed, landed ? string.Empty : reason);

        // 被拒也是结单：那一件仍在甲板上（八条等式由投递那一口自己兜住），
        // 钉着不放只会让这个人这一局再领不到别的单。
        ReleaseHaulOf(carrier.Id);
    }

    /// <summary>交单账上记一笔（定长，旧的先掉出去）。</summary>
    private void NoteLanding(ArenaUnit carrier, HaulOrder order, bool landed, string reason)
    {
        _haulLandings.Add(new HaulLanding(Tick, carrier.Id, order.Level, order.ItemId,
            order.TargetBuildingId, landed, reason));
        if (_haulLandings.Count > HaulLandingLedgerSize) _haulLandings.RemoveAt(0);
    }

    /// <summary>结单放人：手上那一纸落地，人当场回到空闲（下一发就能领下一张）。</summary>
    private void ReleaseHaulOf(string unitId)
    {
        if (!_carrying.Remove(unitId)) return;

        var u = Roster.Find(unitId);
        if (u is null) return;

        var stepper = Roster.DeckStepper;
        var at = stepper.PositionOf(u.Id);
        stepper.SetCountedInSeparation(u.Id, false);
        stepper.Issue(u.Id, NavCommand.Hold(at));
    }

    private static bool ArrivedAt(ArenaUnit u, NavPoint deckAt)
        => NavArrive.Reached(new NavPoint(u.DeckX, u.DeckZ), deckAt, ArenaTuning.HaulArriveToleranceMeters);

    private void WalkToPickup(ArenaUnit u, HaulOrder order)
        => WalkHaul(u, DeckGeometry.Nav(order.OriginDeck.X, order.OriginDeck.Z));

    private void WalkToDelivery(ArenaUnit u, HaulOrder order)
    {
        var door = DoorDeckOf(order.TargetBuildingId);
        WalkHaul(u, DeckGeometry.Nav(door.X, door.Z));
    }

    private void WalkHaul(ArenaUnit u, NavPoint atDeck)
    {
        var stepper = Roster.DeckStepper;
        stepper.SetCountedInSeparation(u.Id, true);
        stepper.Issue(u.Id, NavCommand.GoTo(atDeck, ArenaTuning.HaulArriveToleranceMeters));
    }

    private static double CostOf(string blueprintId)
        => DeckBlueprints.SpecOf(blueprintId)?.CoinCost ?? 0;

    /// <summary>
    /// 离兽最近的那一家城镇势力（没有债主回空串）。
    ///
    /// <para><b>「谁更近」那一条算式住在 <see cref="SiegeState.NearestFaction"/> 里</b>，
    /// 这里只把地理候选拼出来：债主名下的城镇点位、量到停靠点（按巨兽当前位阶外推那一档）。
    /// 候选次序照登记次序，同距取先登记的那一家。</para>
    /// </summary>
    private string NearestFactionId()
    {
        var towns = new List<(string FactionId, NavPoint StopPoint)>();
        foreach (var c in Town.Creditors)
        {
            foreach (var townId in c.TownIds)
            {
                var place = BigMapGrid.PlaceById(townId);
                if (place is null) continue;
                towns.Add((c.FactionId, place.StopPoint(Tier)));
            }
        }

        return SiegeState.NearestFaction(Voyage.Position, towns);
    }

    private void BuildSpawnPoints()
    {
        // 落点序列：甲板上每一格可通行格的格心，按列再按行，顺序固定（重放要逐字可复现）。
        // 开局按一阶边界铺；二阶扩容由 AppendDeckSpawnPoints 把新增那圈按同一次序接到末尾。
        var (minCol, minRow, maxCol, maxRow) = DeckCellBounds;
        for (var col = minCol; col <= maxCol; col++)
        {
            for (var row = minRow; row <= maxRow; row++)
            {
                var navCell = DeckBlueprints.ToNavCell(new GridCell(col, row));
                if (DeckGrid.IsBlocked(navCell)) continue;
                _spawnPoints.Add(new NavPoint(CellCenterOf(col), CellCenterOf(row)));
            }
        }
    }

    /// <summary>
    /// 把镇政府、竞技场、教堂这三件重新开成可通行。
    ///
    /// <para><b>为什么开这三格</b>：spec 给的竞技场入场点与镇政府电梯口都落在这三件的地盘里；
    /// 建造接线把每一栋都封成阻挡，于是人永远走不进去，办赛与洞穴清剿两场仗会安静地变成空转。
    /// 建造接线只认格名单、不认「哪一栋可进」，所以这一头走寻路层那一个写口
    /// （<see cref="NavTerrain.Apply"/>）把三件的格开回来。</para>
    /// </summary>
    public void OpenWalkableBuildings()
    {
        foreach (var buildingId in new[] { TownHallId, ArenaId, ChurchId })
        {
            var building = Construction.Find(buildingId);
            if (building is null) continue;
            foreach (var cell in building.Cells)
            {
                NavTerrain.Apply(DeckGrid, DeckBlueprints.ToNavCell(cell), NavTerrainLayer.Blocked, 0.0);
            }
        }
    }
}
