#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.EventBus;
using DemoFactory.GugaFramework.EventBus.Events;
using DemoFactory.GugaFramework.Scheduling;
using CorePlacedEvent = DemoFactory.GugaFramework.Construction.BuildingPlaced;
using CoreCompletedEvent = DemoFactory.GugaFramework.Construction.BuildingCompleted;
using CoreBuilding = DemoFactory.GugaFramework.Construction.Building;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 无限领主世界 — Majesty 式间接控制的纯 C# 逻辑世界。
///
/// 设计思路（对齐目标：Majesty 2 — 玩家不直接命令单位，靠金钱激励影响世界）：
/// - 第二切片新增：悬赏旗（Reward Flag）+ 英雄钱包 + 金币循环 + 升级
///   · 玩家唯一写入口：PlaceBountyFlag / RaiseBountyFlag（花钱挂赏）
///   · 英雄 AI：评估悬赏（赏金 vs 危险）→ 承接 → 击杀 → 领赏 → 市场消费
///   · 税收回流：英雄消费的 TaxRate 比例进金库，形成"赏金→消费→税收"循环
///   · 升级：击杀得 XP，升级提升战力与血量（Majesty 英雄成长）
/// - 时间轴语义：一切变化发生在"步"上；Pause() 后 Advance() 空转
/// - 剥离状态：本类不引用任何 Godot 类型；表现层订阅事件流渲染模型
///
/// 架构位置：三层响应式架构 — 纯数据逻辑层 (Pure C#)
///
/// <para><b>营建已迁到公共建造模块</b>（<see cref="ConstructionCore"/>，issue 05）：本类组合持有它，
/// 注入自由放置规则（全部蓝图半径 = <see cref="MinPlaceDistance"/>/2，与迁前中心距判定逐字等价
/// —— D12），玩家营建走 <see cref="TryPlaceBuilding"/> 这个入口：自查金库 → Commit → 成功才扣款 →
/// 发经济事件。产出链不从这个入口里发，改为模块 <c>Completed</c> 事件接通（工期全 0 ⇒ 同一次 Commit
/// 里同步连发，行为等价，D16~D17）。</para>
///
/// <para><b>场地建筑也进册</b>（2026-10-02，<see cref="RegisterFixture"/>）：种子 / 终幕 / 巢穴重生
/// 那几座由 <see cref="AddFixture"/> 登记，坐标与血量的真值一并归模块（D2、D9）。这一格修掉了
/// 「幽灵显示可放、点下去被拒」那条 bug —— 根因是它们不在册内、原型在 <see cref="TryPlaceBuilding"/>
/// 里另写了一段距离判定，于是裁决流（幽灵只读它）与那个入口两处不一致。现在<b>间距判定只剩模块一处</b>，
/// 血量也只有一个写口（<see cref="DebugHealBuilding"/> → <c>SetHealth</c>）。</para>
///
/// <para>随之清掉的旧结构：<see cref="TryPlaceBuilding"/> 里那段「玩家↔种子」距离循环（那个以
/// <c>TooCloseTo</c> 开头的私有判定，已删）、扣血与回血各自的两条分支、以及 <see cref="Building"/> 上那个
/// 「在不在册内」的判据（每个活着的句柄都接在册内那一行上，它已经恒真，已删）。</para>
/// </summary>
public sealed partial class LogicWorld : GugaFramework.Base.GameLogicBase, GugaFramework.Replay.IReplayWorld
{
    private readonly GameClock _clock;

    /// <summary>
    /// 全世界的建筑句柄（顺序 = 落位顺序）。<b>它不是第二份建筑册</b>：每座句柄的
    /// 坐标 / 血量真值在模块那一行，这里只是句柄与顺序（读数口，表现层与测试都走它）。
    /// 出册由模块事件流那一个 drain 点收口（<see cref="DrainConstructionEvents"/>）。
    /// </summary>
    private readonly List<Building> _buildings = new();

    /// <summary>公共建造模块（组合持有，不继承 —— spec 3 节第 1 条）。</summary>
    private readonly ConstructionCore _construction;

    /// <summary>
    /// 走位那一半（组合持有）：决策链只发命令、只读移动解，位置真值住在它手里
    /// （<see cref="Unit.Position"/> 是镜像）。七处直线趋近因此换成一份移动解（「寻路单入口」）。
    /// </summary>
    private readonly InfinityLordNavMove _nav;

    /// <summary>
    /// 技能层那一侧的接线（组合持有）：每类单位一条普攻，单位↔单位的扣血由它结算（Q12）。
    /// 激活检查与移速倍率也从它读（走位那一半把这两个口子递进寻路流水线）。
    /// </summary>
    private readonly InfinityLordAbilities _abilities;

    /// <summary>
    /// 模块事件流的 drain 游标。<b>它是「全局流里读到第几条」（long，跟
    /// <see cref="ConstructionEventLog.Total"/> 同一把尺），不是「留下来的第几条」</b> ——
    /// 后者在环形表灌满之后恒等于容量，游标会卡死，整个 drain 再也不跑（理由与修法写在
    /// <see cref="DrainConstructionEvents"/> 上）。
    /// </summary>
    private long _eventCursor;

    /// <summary>已经起过产出链的建筑 Id（同一座只起一条，重复触发不许让产出翻倍）。</summary>
    private readonly HashSet<string> _spawnChains = new(StringComparer.Ordinal);

    /// <summary>
    /// 已经发过「建成」事件的建筑 Id。模块的 <c>Completed</c> 在环形事件表被挤过之后会被重扫到，
    /// 表现层建模型不能因此收到两次（同 <see cref="OnCoreCompleted"/> 的注释）。
    /// </summary>
    private readonly HashSet<string> _completedSent = new(StringComparer.Ordinal);

    private readonly List<Unit> _units = new();
    private readonly List<BountyFlag> _flags = new();
    private Queue<Unit> _spawnQueue = new();
    private Random _rng;

    /// <summary>
    /// 固定种子 —— 读档续随机流要用它。<c>null</c>（跟系统共享流）那种世界存不回来，
    /// 存档那一半当场拒（见 <see cref="GugaFramework.Persistence.ISaveable.WriteState"/>）。
    /// </summary>
    private readonly int? _rngSeed;

    /// <summary>
    /// 随机源已抽次数。<b><c>Random</c> 的内部状态存不进去</b>，而世界只有这一处随机源，
    /// 所以存「抽了几次」、读档时用同一个种子从头重抽到那一处（那一发重抽与原局逐字相同）。
    /// </summary>
    private long _rngDraws;

    private int _nextUnitSeq;
    private int _nextFlagSeq;
    /// <summary>重生巢穴序号（种子世界占用 lair1/lair2，重生从 lair3 起）。</summary>
    private int _nextLairSeq = 3;
    /// <summary>士气自然衰减计时（切片7：每 MoraleDecayTicks 步全体 −1）。</summary>
    private int _moraleDecayTimer;

    /// <summary>税收比例：英雄消费的 40% 回流金库（Majesty 金币循环的调节阀）。</summary>
    public const float TaxRate = 0.4f;

    /// <summary>英雄承接悬赏的最低"勇气价格"：赏金/目标战力 比值阈值。</summary>
    public const float BraveryRatio = 1.2f;

    /// <summary>摧毁建筑悬赏的战力评估常数（建筑血厚，按血量折算威胁）。</summary>
    public const float BuildingThreatFactor = 0.02f;

    // === 经济参数（村民税保底收入 — 冒险者完全自雇，行会不发饷、无维护费概念） ===

    /// <summary>经济周期（步）：村民税按此周期结算。</summary>
    public const int EconomyCycleTicks = 60;

    /// <summary>村民税：每周期保底收入（最小收入机制）。</summary>
    public const int VillagerTaxPerCycle = 12;

    // === 需求流失参数（冒险者自雇 — 需求长期不被满足就离开，人口靠生态承载力托底） ===

    /// <summary>需求饥饿离队阈值（步）：任一需求持续不被满足满此时长即离开（约 30 秒）。</summary>
    public const int NeedStarveLeaveTicks = 300;

    // === 切片4：英雄成长参数（养英雄 vs 发悬赏的化学反应） ===

    /// <summary>装备单价：英雄在市场购买一级装备的花费。</summary>
    public const int GearPrice = 20;

    /// <summary>每级装备的攻击加成。</summary>
    public const int GearAttackBonus = 3;

    /// <summary>每级装备的血上限加成。</summary>
    public const int GearHealthBonus = 10;

    /// <summary>击杀怪物的固定 XP（升级经验来源之一，与悬赏 XP 并行）。</summary>
    public const int KillXp = 15;

    /// <summary>
    /// 击杀战利品：英雄击杀怪物时从虚空拾得的黄金（直接进英雄钱包）。
    /// 设计：战斗本能自带收入 — 不挂赏也有零钱花，金库压力靠玩法而非数值补贴。
    /// </summary>
    public const int KillLootGold = 8;

    // === 切片7：需求层级参数（士气与伤势 — 让英雄更像「人」） ===

    /// <summary>酒馆每杯酒价：士气消费的新金币出口（与装备购买并列的英雄侧消费）。</summary>
    public const int DrinkPrice = 10;

    /// <summary>每杯酒恢复的士气值。</summary>
    public const int DrinkMoraleRestore = 40;

    /// <summary>士气需求线：低于此值的英雄想去喝酒（名牌/悬停提示同用此判定）。</summary>
    public const int MoraleDrinkThreshold = 60;

    /// <summary>士气自然衰减周期（步）：全体英雄每周期 −1 — 时间流逝磨平热情。</summary>
    public const int MoraleDecayTicks = 30;

    /// <summary>士气 0 时感知威胁的最大放大增量（系数 = 1 + 该值）：低士气英雄更惜命（乘惩罚）。</summary>
    public const float MoralePenaltyMax = 1.0f;

    /// <summary>药店每瓶药剂价格。</summary>
    public const int PotionPrice = 15;

    /// <summary>每瓶药剂回复的生命值。</summary>
    public const int PotionHeal = 25;

    /// <summary>伤势需求线：血量低于此比例的英雄想去买药剂。</summary>
    public const float PotionSeekRatio = 0.7f;

    // === 插队小项：巢穴远端重生（清剿不终结威胁 — 世界保持活性的最小机制） ===

    /// <summary>巢穴被摧毁后到重生的延迟（步）：15 秒游戏时间。</summary>
    public const int LairRespawnDelayTicks = 150;

    /// <summary>重生巢穴的初始血量（与种子巢穴一致）。</summary>
    public const int LairRespawnHealth = 300;

    /// <summary>重生位置约束：距所有存活玩家方建筑的最小距离（世界单位）—「远端」的量化。</summary>
    public const float LairRespawnMinDistance = 35f;

    // === 切片8「营建」：玩家建筑放置（玩家动词 #2 — 花金库选类型即时落建） ===

    /// <summary>玩家放置建筑与任一存活建筑的最小间距（世界单位，校验即拦截不深究）。</summary>
    public const float MinPlaceDistance = 12f;

    /// <summary>
    /// 营建蓝图的占地半径（世界单位）—— 全部蓝图同值，取 <see cref="MinPlaceDistance"/> 的一半。
    /// 这一行就是 D12 的等价式：自由放置判「边缘到边缘」，两座要的间距 = 半径之和，
    /// 半径都折成 6 时半径和正好 12，与迁前的中心距 12 判定逐字等价（玩家↔玩家这一段）。
    /// 场地建筑那几行同值 —— 场地建筑跳过落位校验，但**占位照做**，
    /// 玩家读到的禁区边界与它们在半径和这一套里算的完全一样。
    /// </summary>
    private const double BlueprintRadius = MinPlaceDistance / 2.0;

    /// <summary>
    /// 营建落点的位置量化步长（世界单位）。0.01 是自由放置的缺省档，也是本原型原样传参的档 ——
    /// 迁前落点不量化，所以迁后同一批落点只有在这一档上才逐字不变（测试里那批 -50 / 40 / -40 都是
    /// 0.01 的整数倍，量化后逐字相等）。
    /// </summary>
    private const double PlacePositionStep = 0.01;

    // === 切片10「终局」：宫殿（失败条件）与终局判定 ===
    // 用户确认：**判定即记账，不冻结时间轴** — 胜负照常判定（闩锁不可改写、
    // 事件只发一次、表现层挂横幅），但世界照常演化，要结束直接关游戏。
    // （原「终局即冻结」是切片 10 会话 AI 自作主张扩大语义，未经确认；2026-09-23 拆除，
    //   它同时是 MCP 活性误判 / 重放分块分歧 / 测试推进死循环三桩坑的根。）

    /// <summary>
    /// 宫殿血量 — 终局锚点，被毁即判负（用户确认：独立实体，金库保持抽象）。
    /// 大考实测口径：一波 20 只护卫在门口围攻 ≈ 3.6 伤害/秒，2000 血 9 秒蒸发 = 一次漏防即猝死；
    /// 8000 血给约 40 秒回防窗口 — 宫殿该是「被耗死的」，不是「被踩一脚就死的」。
    /// </summary>
    public const int PalaceHealth = 8000;

    /// <summary>
    /// 剧本波次表 — 外环威胁曲线（切片 10：Majesty 2 本尊即剧本脚本，参数表驱动、确定性可断言）。
    /// 数值与强度由 AI 自定（用户确认：「不重要，我不会花时间去微调」）。
    /// 每波数值严格上台阶；巢穴涓流（30 步/只地精）保留不变 — 波次是叠在涓流之上的加压层。
    ///
    /// <para><b>时间点按修正后的移速重新定档</b>（原 1:30 / 3:00 / 5:00，即 900 / 1800 / 3000 步）。
    /// 「移速乘 10」那个真 bug 修掉之后，单位走完剧本入侵点到城下那 85 米要 500~700 步
    /// （原世界只要 50~70 步），整条曲线因此整体后移：旧时刻下三波怪与终幕护卫几乎都在一局尾巴上才到，
    /// 零干预的宫殿到 8:00 还是满血（大考3 判不出负）。现在按「落场 → 走 500~700 步 → 打得起来」
    /// 把三个时刻各提前 300~1200 步，波间仍等距，终幕仍在第三波之后压轴。</para>
    /// </summary>
    public static readonly WaveRow[] WaveTable =
    [
        new(1, 600, "强化地精", 60, 9, 1.2f, 3),
        new(2, 1200, "大地精", 120, 14, 1.1f, 4),
        new(3, 1800, "巨魔", 260, 22, 0.9f, 3),
    ];

    /// <summary>
    /// 剧本入侵点 — 波次小队落场锚点（远离玩家建筑群）。
    /// 刻意不依赖巢穴存活：清剿巢穴只关掉涓流，关不掉剧本压力，否则外环曲线可被一个内环动作永久消除。
    /// </summary>
    public static readonly Vector2 WaveSpawnPoint = new(85, 5);

    /// <summary>波次小队落场散布半径（世界单位）。</summary>
    private const float WaveSquadSpread = 4f;

    // === 切片10「终局」第 4 步：BOSS 巢穴（终幕层 — 胜利条件） ===

    /// <summary>
    /// 终幕降临时间点（3:30）— BOSS 巢穴在剧本波次之后压轴。
    ///
    /// <para>原为 6:00（3600 步）。按修正后的移速重新定档：护卫从落点走到城下要 300~900 步，
    /// 6:00 降临 ⇒ 护卫 7:30 之后才到，8:00 一局里连行会都啃不完（大考3 实测宫殿满血）。
    /// 随后又按「攻坚窗口」重量了一次：代理玩家的金库在 3:00 见顶（实测 222）、4:00 就掉到 62 ——
    /// 终幕旗在 4:00 之后才开得出来，攻坚手走到已经是 5:00 以后，宫殿先倒。
    /// 定在 3:30：波次三（3:00）刚落地、金库还在高位，攻坚窗口与守城压力错得开。</para>
    /// </summary>
    public const int BossArriveTick = 2100;

    /// <summary>
    /// BOSS 巢穴血量 — 终幕围攻战的时间尺度。
    ///
    /// <para>大考实测校准（seed 42）：成型英雄池对终幕的合 DPS ≈ 50~100 伤害/秒。
    /// 6000 血是<b>快世界</b>那一版定的（那时攻坚手 100 步就到城下，6000 = 「打得完但要梭一把」）。
    /// 按修正移速重新定档：攻坚手从城下走到落点要 300 步，而实测真正落到 BOSS 身上的那一份只有
    /// 2 伤/秒上下（攻坚手在护卫潮里活不久）——6000 血要 3000 步，宫殿先倒、BOSS 一滴血没掉。
    /// 1500 血 = 同一个实测速率下打得完的量级。</para>
    /// </summary>
    public const int BossLairHealth = 600;

    /// <summary>
    /// BOSS 威胁档 — **固定值**，不按血量折算。
    /// 普通巢穴的「血量 × 系数」口径放在终幕上会自我矛盾：血越厚 → 建议价越贵 →
    /// 大考实测 4000 血时建议价 $384，做了营建投资的玩家反而整局掏不出这一笔，终幕变成「没打过的墙」。
    /// 固定档 = 「终幕是一笔可预算的大赏」（≈ 5 场地精的价），且鼓励多面旗凑英雄围攻（一面旗只有一个承接者）。
    /// </summary>
    public const float BossThreat = 90f;

    /// <summary>
    /// BOSS 巢穴降临点 — 远端（距玩家建筑群 &gt; <see cref="LairRespawnMinDistance"/>），与波次入侵点错开。
    ///
    /// <para><b>按修正后的移速重新定档</b>（原 (85,45)）：那是快世界那一版的落点 —— 走完 100 米只要
    /// 100 步上下。改回原速后同一段路要 900 步（游侠 1.6 米/秒），攻坚手走到就已经被护卫潮淹没：
    /// 实测两面旗派出去，两名攻坚手死在路上，城下同时少了两名防守 ⇒ 宫殿 4:21 被推平、BOSS 一滴血没掉。
    /// 现在落在城郊 46 米处：仍满足「远端 &gt; 35 米」，但一个来回 600 步，攻坚队打得成、也回得来。</para>
    /// </summary>
    public static readonly Vector2 BossLairPosition = new(35, 30);

    /// <summary>
    /// BOSS 护卫产出节奏（步/只）— 与普通巢穴同频。
    /// 大考实测教训：18 步/只 = 一局产 45 只护卫，英雄被攻坚抽走后这股潮 11 秒推平宫殿 —
    /// 终幕的威胁应当来自「啃不动」，不是来自「刷不完」。
    /// </summary>
    public const int BossGuardSpawnIntervalTicks = 60;

    /// <summary>
    /// 怪物巢穴的涓流节奏（步/只）— 种子巢穴与重生巢穴同规格。
    ///
    /// <para><b>按修正后的移速重新定档</b>（原 30）。这是全场怪物的<b>主力来源</b>：
    /// 两条种子巢穴各 30 步一只 ⇒ 6.7 只/100 步。而修正移速后英雄的击杀能力是
    /// 3.4 只/100 步（大考1 实测 143 只/4200 步）、1.5（只挂赏）、0.85（零干预）——
    /// 供给是需求的两到八倍，存量因此单调涨：实测 8:00 同屏 284 只，攻坚手走得到也站不住。
    /// 60 步一只 ⇒ 涓流 3.3 只/100 步，加上终幕护卫的 1.1，背景供给 ≈ 4.4 只/100 步：
    /// 全动词略有余量（能腾出手打终幕），零干预仍入不敷出（必败这条不许弄回去）。</para>
    /// </summary>
    public const int LairSpawnIntervalTicks = 45;

    /// <summary>BOSS 护卫数值行（血 90 / 攻 13 — 介于大地精与巨魔之间）。</summary>
    private const string BossGuardName = "BOSS 护卫";
    private const int BossGuardHealth = 90;
    private const int BossGuardAttack = 13;
    private const float BossGuardSpeed = 1.1f;

    /// <summary>市场造价（一次性，扣金库 — 无维护费概念）。</summary>
    public const int MarketCost = 100;

    /// <summary>酒馆造价（一次性，扣金库）。</summary>
    public const int TavernCost = 60;

    /// <summary>药店造价（一次性，扣金库）。</summary>
    public const int ApothecaryCost = 80;

    /// <summary>领主金库 — 发悬赏花钱、收税收进钱。</summary>
    public int Treasury { get; private set; }

    /// <summary>金库最近一次经济周期结算（收入为正 — 冒险者自雇，行会无固定支出）的净额，表现层显示资金流动方向。</summary>
    public int LastCycleNet { get; private set; }

    /// <summary>终局状态（切片 10）— Running 之外即已判定：胜负不可再改写；时间轴照常推进（不冻结）。</summary>
    public GameOutcome Outcome { get; private set; } = GameOutcome.Running;

    // === 事件流（表现层订阅渲染） ===
    public event Action<UnitDiedReport>? UnitDied;
    public event Action<CombatEventReport>? CombatEvent;
    public event Action<BountyEventReport>? BountyEvent;
    public event Action<EconomyEventReport>? EconomyEvent;

    /// <summary>建筑被摧毁（已从世界移除）— 表现层清模型。测试排布的扣血不走此流程。</summary>
    public event Action<Building>? BuildingDestroyed;

    /// <summary>巢穴远端重生（新建筑已入世界）— 表现层建新模型。</summary>
    public event Action<Building>? BuildingRespawned;

    /// <summary>玩家放置建筑成功（新建筑已入世界）— 表现层建新模型（切片8 营建）。</summary>
    public event Action<Building>? BuildingPlaced;

    /// <summary>
    /// 建成（模块 <c>Completed</c> 那一刻）— 表现层<b>建模型走这一条</b>，不是
    /// <see cref="BuildingPlaced"/>。
    ///
    /// <para>为什么分两条：Placed 是「已落座、在建」，Completed 是「建成了」。模型的时机该跟后者 ——
    /// 工期非 0 时那几座还在施工，提前摆出一座成品楼就是画面撒谎。本轮工期全 0 ⇒ 两条在同一次
    /// Commit 里连发（Placed 先、Completed 后，D16），画面上零差别；等工期接上，这一层就是
    /// 「施工中不出现成品模型」的唯一保证。</para>
    /// </summary>
    public event Action<Building>? BuildingCompleted;

    /// <summary>终局判定（胜/负各一次）— 表现层挂胜负横幅（切片 10）。</summary>
    public event Action<GameOutcome>? GameOver;

    /// <summary>波次落场播报（切片 10 剧本加压层）— 表现层做「第 N 波压境」提示与日志。</summary>
    public event Action<WaveRow>? WaveStarted;

    /// <summary>BOSS 巢穴降临（切片 10 终幕层）— 表现层落巨型模型 + 终幕横幅。</summary>
    public event Action<Building>? BossArrived;

    public LogicWorld(int? randomSeed = null)
        : this(randomSeed, false)
    {
    }

    /// <summary>
    /// 造一个<b>空世界</b> —— 读档那一发的落脚点（<see cref="GugaFramework.Persistence.ISaveable.ReadState"/>）。
    ///
    /// <para><b>与正常构造逐行同形，只少两件事</b>：不跑 <see cref="SeedWorld"/>、不排那三条开局事件。
    /// 其余（含<b>订阅次序</b>）一模一样：<c>StepDecide</c> 先订、寻路层在自己的构造里接着订、
    /// <c>StepAfterMove</c> 最后订 —— 走位那段夹在决策与镜像之间这件事是重放基线的一部分，
    /// 空世界若换个次序，读档之后世界逐步就与原局不同了。</para>
    ///
    /// <para><b>为什么要有这一档而不是「造个正常世界再读档覆盖」</b>：覆盖要先拆掉种子建筑与开局排期，
    /// 而拆建筑会走模块的 <c>Removed</c> 事件 → 摧毁那条路 → 巢穴重生。一条读档路径里
    /// 塞进「先把世界拆了再搭回来」这种动作，分叉出来的问题查不出根。</para>
    /// </summary>
    public static LogicWorld EmptyForLoad(int? randomSeed) => new(randomSeed, true);

    private LogicWorld(int? randomSeed, bool forLoad)
    {
        // 时钟由基类建，这个字段指的是那一个（派生类的字段初始化器跑在基类构造之前，
        // 所以不能在字段声明处赋值，那时 Clock 还是空的）。
        _clock = Clock;
        _empty = forLoad;
        _rngSeed = randomSeed;
        _rng = randomSeed is int seed ? new Random(seed) : Random.Shared;
        Treasury = 150;

        // 建造模块在种子世界之前建好：蓝图表一次递入（D33）。步号读数接在装配的时钟上 ——
        // 什么时候被问到都拿得到当前步号（D27）。方向段本轮恒 0，所以角度量化步长不影响任何落点。
        _construction = Assembly.BuildConstruction(
            new FreePlacementRule(positionStep: PlacePositionStep),
            PlayerBlueprints().Concat(FixtureBlueprints()));

        // 订阅次序就是执行次序（照巨兽与排兵布阵那两份）：本类决策那一段最早订阅，
        // 寻路层在自己的构造里接着订阅（照命令走位），本类走位之后那一段最后订阅。
        // 于是「决策链发令 → 按命令走位 → 镜像同步」三段按这个次序跑，这一步发的令这一步就走
        // （反过来的话命令推迟一步生效，读数是「世界发了令，而这一步它还在按上一条命令走」）。
        // 激活检查与移速倍率递的是「此刻挂着谁」那个口（技能层在这一段之后才建），所以那两发
        // 不按实例递、按查法递。
        SubscribePulse(StepDecide);

        // 技能层那一件要一个「把伤害写回建造模块」的口（层不认识建造模块）：
        // 递进去的正是 <see cref="DamageBuilding"/> —— 建筑扣血的唯一写回口，
        // 所以「层算出来的那一笔」与「战斗流程扣的那一笔」落的是同一处（不走 SetHealth）。
        _abilities = new InfinityLordAbilities(DamageBuilding);
        _nav = new InfinityLordNavMove(Assembly, id => _buildings.FirstOrDefault(b => b.Id == id), RadiusOf,
            gate: InfinityLordNavMove.BuildActivationCheck(() => _abilities),
            speedScaleOf: id => _abilities.SpeedScale(id));

        // 地形接线（装配里那一步）排在 SeedWorld 之前接上 —— 接上那一刻的游标记在当下，
        // 排在后面接的话种子那七座永远落不下阻挡（它们不发 Placed、只发 Registered）。
        // 装配在推过步之后再接这一处会当场抛。
        TrackDetach(_nav.Detach);

        if (!forLoad) SeedWorld();
        SubscribePulse(StepAfterMove);
        EventBus.GamePaused.Published += OnGamePaused;
        TrackDetach(() => EventBus.GamePaused.Published -= OnGamePaused);
        if (!forLoad)
        {
            _clock.Schedule(EventEconomy, EconomyCycleTicks, EconomyCycle);
            ScheduleWaves();
            _clock.Schedule(EventBoss, BossArriveTick, SpawnBossLair);
        }
    }

    /// <summary>
    /// 终幕降临：BOSS 巢穴在远端落地并起护卫产出链（切片 10 胜利条件）。
    /// 完成标准：新建筑入世界、产出链自续、BossArrived 播报一次；摧毁它即判胜（见 DamageBuilding）。
    /// </summary>
    private void SpawnBossLair()
    {
        var boss = AddFixture("boss", BuildingKind.BossLair, Faction.Monster, BossLairPosition,
            BossGuardSpawnIntervalTicks);
        StartSpawnChain(boss.Id);
        BossArrived?.Invoke(boss);
    }

    /// <summary>剧本波次调度（开局一次性排期 — 到期落场，时间点见 WaveTable）。</summary>
    private void ScheduleWaves()
    {
        for (var i = 0; i < WaveTable.Length; i++)
        {
            var row = WaveTable[i];
            _clock.Schedule(EventWave(i), row.AtTick, () => RunWave(row));
        }
    }

    /// <summary>
    /// 波次落场：按表在剧本入侵点放出一支强化怪小队，并向表现层播报（切片 10 外环加压）。
    /// 完成标准：Count 只按表入产出队列（下一脉搏步统一落场），WaveStarted 发一次。
    /// </summary>
    private void RunWave(WaveRow wave)
    {
        for (var i = 0; i < wave.Count; i++)
        {
            EnqueueMonster(wave.Name, wave.Health, wave.Attack, wave.Speed,
                WaveSpawnPoint + RandomOffset(WaveSquadSpread));
        }
        WaveStarted?.Invoke(wave);
    }

    /// <summary>搭建初始世界：中央行会 + 市场一角 + 一处巢穴（多线压力最小形态）。</summary>
    private void SeedWorld()
    {
        AddFixture("palace", BuildingKind.Palace, Faction.Hero, new Vector2(-8, 0), 0);
        AddFixture("guild", BuildingKind.GuildHall, Faction.Hero, new Vector2(0, 0), 20);
        AddFixture("market", BuildingKind.Market, Faction.Hero, new Vector2(-18, 8), 0);
        AddFixture("tavern", BuildingKind.Tavern, Faction.Hero, new Vector2(-18, -8), 0);
        AddFixture("apothecary", BuildingKind.Apothecary, Faction.Hero, new Vector2(-10, 16), 0);
        AddFixture("lair1", BuildingKind.MonsterLair, Faction.Monster, new Vector2(60, 20), LairSpawnIntervalTicks);
        AddFixture("lair2", BuildingKind.MonsterLair, Faction.Monster, new Vector2(65, -25), LairSpawnIntervalTicks);

        // 产出链是**同一个**实现（StartSpawnChain），两种触发：种子这一段直接起，
        // 玩家营建那几座由模块的 Completed 事件起（工期全 0 ⇒ 同步连发，行为等价 D16~D17）。
        StartSpawnChain("guild");
        StartSpawnChain("lair1");
        StartSpawnChain("lair2");
    }

    /// <summary>
    /// 登记一座<b>场地建筑</b>（种子 / 终幕 / 巢穴重生）—— 走模块的 <see cref="RegisterFixture"/>：
    /// 进册、占位、跳过落位校验，进度落在完工。坐标与血量的真值此后归模块（D2、D9），
    /// 本类这份 <see cref="Building"/> 只是句柄。
    ///
    /// <para><b>为什么必须进册</b>（2026-10-02 修掉的那条真 bug）：裁决流看得见的只有册内与
    /// 占用索引里的东西，而幽灵只读裁决流。它们不在册内的那半年里，原型于是在
    /// <see cref="TryPlaceBuilding"/> 里补了一段自己的距离判定（旧名以 <c>TooCloseTo</c> 开头，已删）—— 于是同一个点上幽灵是绿的、
    /// 点下去被拒，D29 在那条路径上不成立。让它们进册比补判定便宜：判定只有一处，
    /// 预览与结果共用它，而且这一格的回归是离线的（不必出图才看得见）。
    /// 代价是种子布局里那两对挨得比 12 近的（行会↔宫殿 8、坊市↔药店 11.31）不再有任何东西
    /// 拦着 —— 场地建筑摆在哪就是哪，那正是跳过落位校验的用意。</para>
    ///
    /// <para><b>血量不再由这一格给</b>：上限是蓝图数据（<see cref="FixtureBlueprints"/>），
    /// 放置即满值（D17）。迁前那几个字面量搬进蓝图表，为的是「上限只有一份真值」。</para>
    /// </summary>
    private Building AddFixture(string id, BuildingKind kind, Faction owner, Vector2 position,
        int spawnIntervalTicks)
    {
        var building = new Building
        {
            Id = id,
            Kind = kind,
            Owner = owner,
            SpawnIntervalTicks = spawnIntervalTicks,
        };
        _fixtureIds.Add(id);

        // 载荷就是这份句柄本体（和营建那一条同款，模块存而不读、原样归还，D31）。
        _construction.RegisterFixture(BlueprintIdOf(kind), position, id, 0, building);
        DrainConstructionEvents();
        return building;
    }

    /// <summary>
    /// 建筑产出链 — <b>Schedule 自续写法</b>：产出后重新调度下一批。
    /// 暂停时不推进，产出自然冻结；建筑已移除（巢穴被摧毁且未重生）时终止自续链。
    ///
    /// <para>迁到建造模块之后，这里<b>只起一次</b>：玩家营建那几座由模块的 Completed 事件
    /// （<see cref="DrainConstructionEvents"/>）调 <see cref="StartSpawnChain"/>，种子那几座由
    /// <see cref="SeedWorld"/> / <see cref="RespawnLair"/> 调同一个入口 —— 产出节奏这一份实现
    /// 只有一处，两个触发点不是两套逻辑（迁前是从 <c>PlaceBuilding</c> 体内直接发的那一条，已删）。</para>
    /// </summary>
    private void StartSpawnChain(string buildingId)
    {
        var building = _buildings.FirstOrDefault(b => b.Id == buildingId);
        if (building is null || building.SpawnIntervalTicks <= 0)
        {
            // 不产单位的服务建筑没有产出链（迁前只有行会与巢穴起链，迁后 Completed 对每座都发，
            // 门槛就落在这里）。时钟那一侧 <c>Schedule(0)</c> 当场抛 —— 排一条 0 步的链不是「马上产出」。
            return;
        }

        if (!_spawnChains.Add(buildingId))
        {
            // 同一座只起一条链：Completed 事件重复 drain（环形事件流被挤过之后重扫）不该让产出翻倍。
            return;
        }

        ScheduleSpawnStep(buildingId);
    }

    /// <summary>自续链的一步（下一批产出 + 再排一步）。建筑已不在册就自然断链。</summary>
    private void ScheduleSpawnStep(string buildingId)
    {
        var building = _buildings.FirstOrDefault(b => b.Id == buildingId);
        if (building is null) return; // 已被摧毁（自续链终止 — 重生链由 RespawnLair 另起）
        _clock.Schedule(EventSpawn(buildingId), building.SpawnIntervalTicks, () =>
        {
            if (building.Health > 0 && building.SpawnIntervalTicks > 0) EnqueueSpawn(building);
            ScheduleSpawnStep(buildingId);
        });
    }

    /// <summary>按建筑种类产出单位 — 出队统一在脉搏步落场（指令排队语义）。
    /// 招募无经济门槛（冒险者自雇 — 行会不发饷，金库为 0 不影响招募）。
    /// 切片 9：建筑即招募 — 行会按种类产对应职业；存活名额满 GuildCapacity 即不补员。</summary>
    private void EnqueueSpawn(Building building)
    {
        if (building.Kind == BuildingKind.MonsterLair)
        {
            EnqueueMonster("地精", 30, 5, 1.1f, SpawnPointOf(building));
            return;
        }

        if (building.Kind == BuildingKind.BossLair)
        {
            // 终幕护卫潮（切片 10）：不清场打不进去 — 攻坚与守城双重压力
            EnqueueMonster(BossGuardName, BossGuardHealth, BossGuardAttack, BossGuardSpeed,
                SpawnPointOf(building));
            return;
        }

        if (building.Kind is not (BuildingKind.GuildHall or BuildingKind.GuildWarrior)) return;

        // 招募名额（生态承载力）：本行会存活者满编即不补员（死亡/离队腾名额后自动恢复产出）
        if (_units.Count(u => u.Side == Faction.Hero
            && u.Health > 0
            && u.HomeGuildId == building.Id) >= GuildCapacity)
        {
            return;
        }

        var (cls, name, speed, health, attack) = GuildClassTable(building.Kind, building.Level);
        _spawnQueue.Enqueue(new Unit
        {
            Id = $"hero_{_nextUnitSeq++}",
            Name = name,
            Side = Faction.Hero,
            Position = SpawnPointOf(building),
            Speed = speed,
            Health = health,
            MaxHealth = health,
            Attack = attack,
            // 出手间隔与射程的住处是技能表那一份（技能层激活检查读的是同两个数）：卡上这两列从它取，
            // 不在这里另写一遍 —— 两处各写一遍就是「射程住在两处」那一格。
            AttackCooldownTicks = InfinityLordSkillTable.CooldownTicksOf(Faction.Hero),
            SightRange = 16f,
            AttackRange = (float)InfinityLordSkillTable.RangeMetersOf(cls),
            Class = cls,
            HomeGuildId = building.Id,
            Gold = 10, // 出生带一点盘缠
        });
    }

    /// <summary>
    /// 怪物入队 — 巢穴涓流与剧本波次共用的唯一出口（战斗时钟/视野/射程同规格，
    /// 差异只在数值行：血/攻/速）。
    /// </summary>
    private void EnqueueMonster(string name, int health, int attack, float speed, Vector2 position)
    {
        _spawnQueue.Enqueue(new Unit
        {
            Id = $"mob_{_nextUnitSeq++}",
            Name = name,
            Side = Faction.Monster,
            Position = position,
            Speed = speed,
            Health = health,
            MaxHealth = health,
            Attack = attack,
            AttackCooldownTicks = InfinityLordSkillTable.CooldownTicksOf(Faction.Monster),
            SightRange = 10f,
            AttackRange = (float)InfinityLordSkillTable.MonsterRangeMeters,
            Class = HeroClass.Ranger, // 怪物无职业语义 — 枚举默认值（仅占位）
        });
    }

    /// <summary>
    /// 行会职业数值表 — 职业差异四参数（速度/血/攻击/勇气另在 ClassBraveryFactor）。
    /// 切片 10 纵向成长：Lv.2 的战士公会产**骑士**（精英线，职业表加的一行）；游侠公会无精英线。
    /// </summary>
    private static (HeroClass Cls, string Name, float Speed, int Health, int Attack) GuildClassTable(
        BuildingKind kind, int level)
        => kind switch
        {
            BuildingKind.GuildHall => (HeroClass.Ranger, "游侠", 1.6f, 60, 8),      // 远程风筝
            BuildingKind.GuildWarrior when level >= 2 => (HeroClass.Knight, "骑士", 1.3f, 180, 16), // 精英近战
            BuildingKind.GuildWarrior => (HeroClass.Warrior, "战士", 1.2f, 100, 10), // 近战肉盾
            _ => (HeroClass.Ranger, "游侠", 1.6f, 60, 8),
        };

    /// <summary>随机偏移向量（出生散布/漫游用）。</summary>
    /// <summary>随机偏移（出生点周围那一圈）—— <b>世界唯一的随机源就在这一处</b>。
    ///
    /// <para>每调一次抽两个数，所以「已抽次数」按 2 记。读档那边按同一份数从头重抽到那一处
    /// （见 <c>LogicWorld.RestoreRandomStream</c>）。</para>
    /// </summary>
    private Vector2 RandomOffset(float radius)
    {
        _rngDraws += 2;
        return new((float)((_rng.NextDouble() * 2 - 1) * radius), (float)((_rng.NextDouble() * 2 - 1) * radius));
    }

    /// <summary>
    /// 建筑扣血的<b>唯一写回口</b>（袭城 / 攻坚 / 调试直达共用）：
    /// 扣血 → 摧毁判定 → 移除 + 事件 + 巢穴重生调度。摧毁语义单点收口。
    ///
    /// <para><b>它只是写回，不再是判据</b>（Q12 后半）：改前这一发是战斗流程的入口，
    /// 现在「该不该扣、扣多少」由技能层回答（<see cref="InfinityLordAbilities.StrikeBuilding"/>），
    /// 这里只把层算出来的那一笔落到模块上（层的技能行不带效果、宿主在按下那一刻施加，
    /// 所以调用点拿到的是一个已经算好的数）。</para>
    ///
    /// <para><b>只一条路径</b>：血量账归模块（D2），场地建筑 2026-10-02 起也进册，所以本类不再有
    /// 「册内 / 册外」两条分支 —— 模块那一发里已经做完「扣血 → 归零就出册 + 发 Removed(摧毁) + 腾占用」，
    /// 本类在 drain 点接住那条事件（<see cref="OnCoreRemoved"/>）发原型事件并跑摧毁后的结算。
    /// 迁前那两条分支（册内走模块、册外直接 <c>-=</c>）的分流理由是「种子那几座不在册内」，
    /// 那个前提已经不成立，留着就是一份判不到的分支。</para>
    /// </summary>
    private void DamageBuilding(Building building, double damage)
    {
        _construction.Damage(building.Id, damage);
        DrainConstructionEvents();
    }

    /// <summary>
    /// 终局判定（单点闩锁）：宫殿被毁 = 败，BOSS 巢穴被毁 = 胜。
    /// 已判定则忽略 — 胜负不可互相覆盖，也不可被后续事件改写。
    /// 完成标准：Outcome 落地 + GameOver 事件。
    ///
    /// <para><b>这里判完不冻时间轴，冻不冻由宿主答</b>：
    /// 判定是记账（谁赢、在第几步），冻结是时间轴那一件事，两件事分开。
    /// 宿主（<c>Main</c>）收到 <see cref="GameOver"/> 之后调
    /// <c>GameLogicBase.FreezeTime()</c>，真值住在基类那一处推进口，
    /// 要翻回「局结束不冻结」改那一处，不必回到这个原型改判定。</para>
    ///
    /// <para>所以<b>判定照旧照跑</b>：冻结拦的是「下一步还推不推」，
    /// 拦不住这一步里已经判下的胜负 —— 闩锁与事件都在冻结之前就落地了。</para>
    /// </summary>
    private void EndGame(GameOutcome outcome)
    {
        if (Outcome != GameOutcome.Running) return;
        Outcome = outcome;
        GameOver?.Invoke(outcome);
    }

    /// <summary>
    /// 巢穴远端重生：在远离所有存活玩家建筑的位置重建巢穴并恢复产出节奏。
    /// 种子巢穴的产出自续链随摧毁自然终止（Health>0 判定不过），新建筑重新 Schedule。
    /// </summary>
    private void RespawnLair(Vector2 oldPosition)
    {
        var lair = AddFixture($"lair{_nextLairSeq++}", BuildingKind.MonsterLair, Faction.Monster,
            PickLairRespawnPosition(oldPosition), LairSpawnIntervalTicks);
        StartSpawnChain(lair.Id);
        BuildingRespawned?.Invoke(lair);
    }

    /// <summary>
    /// 重生选址（确定性，无随机 — 测试可断言）：优先保持旧址（威胁原点延续）；
    /// 旧址距任一存活玩家建筑过近（< LairRespawnMinDistance）时，沿「旧址 − 玩家建筑质心」
    /// 方向外推到安全距离之外（远端语义：新威胁点不在玩家建筑群里长出来）。
    /// </summary>
    private Vector2 PickLairRespawnPosition(Vector2 oldPosition)
        => PickLairRespawnPosition(oldPosition, _buildings);

    /// <summary>选址核心（静态纯函数）— 与世界状态解耦，确定性测试直接枚举排布断言。</summary>
    internal static Vector2 PickLairRespawnPosition(Vector2 oldPosition, IReadOnlyList<Building> buildings)
    {
        var heroBuildings = buildings.Where(b => b.Owner == Faction.Hero && b.Health > 0).ToList();
        if (heroBuildings.Count == 0
            || heroBuildings.All(b => Vector2.Distance(b.Position, oldPosition) >= LairRespawnMinDistance))
        {
            return oldPosition;
        }

        var centroid = heroBuildings.Aggregate(Vector2.Zero, (acc, b) => acc + b.Position) / heroBuildings.Count;
        var direction = oldPosition - centroid;
        if (direction.LengthSquared() < 1f) direction = new Vector2(0, 1);
        direction /= direction.Length();
        return oldPosition + direction * (LairRespawnMinDistance + 10f);
    }

    // === 玩家 API：悬赏旗（间接控制的唯一手段） ===

    /// <summary>
    /// 挂悬赏旗：把 gold 金币挂在目标上（怪物单位或怪物建筑）。
    /// 完成标准：返回 true 表示挂旗成功（金库扣款）；余额不足、目标不存在或非敌方返回 false。
    /// </summary>
    public bool PlaceBountyFlag(string targetId, int gold)
    {
        if (gold <= 0) return false;
        if (Treasury < gold) return false;
        var target = FindBountyTarget(targetId);
        if (target is null) return false;

        Treasury -= gold;
        var flagId = $"flag_{_nextFlagSeq++}";
        _flags.Add(new BountyFlag { Id = flagId, TargetId = targetId, Gold = gold });
        BountyEvent?.Invoke(new BountyEventReport(flagId, targetId, null, gold, BountyEventKind.Placed));
        return true;
    }

    /// <summary>
    /// 提高已有悬赏金额（加价吸引更怂的英雄）。
    /// 完成标准：返回 true 表示加价成功。
    /// </summary>
    public bool RaiseBountyFlag(string flagId, int extraGold)
    {
        if (extraGold <= 0 || Treasury < extraGold) return false;
        var flag = _flags.FirstOrDefault(f => f.Id == flagId && !f.Settled);
        if (flag is null) return false;

        Treasury -= extraGold;
        flag.Gold += extraGold;
        BountyEvent?.Invoke(new BountyEventReport(flagId, flag.TargetId, flag.ClaimedByHeroId, flag.Gold, BountyEventKind.Raised));
        return true;
    }

    /// <summary>
    /// 解析悬赏目标：优先按怪物单位查找，其次按怪物建筑查找。
    /// 玩家方建筑不可挂赏（领主不悬赏自家财产）。
    /// </summary>
    private object? FindBountyTarget(string targetId)
    {
        var unit = _units.FirstOrDefault(u => u.Id == targetId && u.Side == Faction.Monster && u.Health > 0);
        if (unit is not null) return unit;

        var building = _buildings.FirstOrDefault(b => b.Id == targetId && b.Owner == Faction.Monster && b.Health > 0);
        if (building is not null) return building;

        return null;
    }

    /// <summary>当前活跃悬赏旗只读快照（表现层渲染旗子/测试断言用）。</summary>
    public IReadOnlyList<BountyFlag> Flags => _flags;

    // === 切片9「职业」：建筑即招募（第一个便宜，后面每个翻倍） ===

    /// <summary>每座行会的存活招募名额上限（死亡/离队腾出名额后自动补员）。</summary>
    public const int GuildCapacity = 4;

    /// <summary>战士公会造价基数（第 n 座 = 基数 × 2^n-1）。</summary>
    public const int GuildWarriorCost = 120;

    /// <summary>游侠公会造价基数（玩家第 1 座 = 基数；种子行会不计序）。</summary>
    public const int GuildRangerCost = 80;

    /// <summary>
    /// 职业勇气系数（TryClaimFlag 阈值乘数）：战士莽（0.7 — 低赏硬单也接），
    /// 游侠基准（1.0），骑士最莽（0.6 — 切片 10 精英线：应对威胁曲线的「敢打硬单」）。
    /// 职业差异四参数之一，其余三参数在 EnqueueSpawn 查表。
    /// </summary>
    public static float ClassBraveryFactor(HeroClass cls) => cls switch
    {
        HeroClass.Warrior => 0.7f,
        HeroClass.Knight => 0.6f,
        HeroClass.Ranger => 1.0f,
        _ => 1.0f,
    };

    // === 切片10「终局」第 3 步：行会升级一格（玩家侧纵向成长最小形态） ===

    /// <summary>行会升级造价（一次性，扣金库 — 与建造同款的焚烧口；本切片只有一格）。</summary>
    public const int GuildUpgradeCost = 200;

    /// <summary>可升级的行会种类（切片 10 定稿：精英线只开战士公会一条 → 骑士；游侠公会明确不可升，不做「花钱没变化」的陷阱）。</summary>
    private static bool IsUpgradeableGuild(Building? building)
        => building is not null && building.Kind == BuildingKind.GuildWarrior;

    /// <summary>
    /// 该建筑当前的升级报价：可升（战士公会 Lv.1）返回造价，其余返回 0（= 不可升级标记，
    /// 与 NextPlaceCost 对不可放置种类返回 0 同款语义 — 表现层据此灰显）。
    /// </summary>
    public int NextUpgradeCost(string buildingId)
    {
        var building = _buildings.FirstOrDefault(b => b.Id == buildingId);
        return IsUpgradeableGuild(building) && building!.Level < 2 ? GuildUpgradeCost : 0;
    }

    /// <summary>
    /// 升级行会（玩家动词 #3 — 花金库把战士公会升到 Lv.2，此后产出链转精英职业）。
    /// 完成标准：返回 true 表示已升级并扣款（发 GuildUpgraded 经济事件）；
    /// 金库不足 / 非行会 / 种类无精英线 / 已封顶 → 拒绝返回 false 且金库分文不动。
    /// 已出生的英雄不追溯转职 — 本动词改变的是这条产出链的下一批。
    /// </summary>
    public bool UpgradeGuild(string buildingId)
    {
        var cost = NextUpgradeCost(buildingId);
        if (cost <= 0) return false;
        if (Treasury < cost) return false;

        var building = _buildings.First(b => b.Id == buildingId);
        Treasury -= cost;
        building.Level = 2;
        EconomyEvent?.Invoke(new EconomyEventReport(building.Id, EconomyEventKind.GuildUpgraded, cost));
        return true;
    }

    /// <summary>玩家已放置该种类建筑的座数（翻倍计价指数 — 种子建筑不计序）。</summary>
    private readonly Dictionary<BuildingKind, int> _placedCount = new();

    /// <summary>
    /// 走 <see cref="AddFixture"/> 那条进来的建筑 id（场地建筑）。
    ///
    /// <para>存档读档要记住它：场地建筑走模块的 <c>RegisterFixture</c>（跳过落位校验），
    /// 玩家营建走 <c>Commit</c>（校验间距）。用错那一条，种子布局里那两对挨得比 12 近的
    /// （行会↔宫殿 8、坊市↔药店 11.31）当场放不下。</para>
    /// </summary>
    private readonly HashSet<string> _fixtureIds = new(StringComparer.Ordinal);

    /// <summary>
    /// 这一局是空世界吗（<see cref="EmptyForLoad"/> 造的）。读档那一格只往空世界里灌 ——
    /// 往已经种过世界的实例上灌，种子那七座会原地叠加，而读数上看不出多了一座。
    /// </summary>
    private readonly bool _empty;

    /// <summary>
    /// 翻倍计价（Majesty 2 本尊）：第 n 座造价 = 基数 × 2^n。
    /// n = 玩家已放置该种类的座数（种子建筑不计序）— 第 1 座 $基数，第 2 座 ×2 …
    /// </summary>
    public static int PlaceCost(BuildingKind kind, int placedCount) => kind switch
    {
        BuildingKind.Market => MarketCost,
        BuildingKind.Tavern => TavernCost,
        BuildingKind.Apothecary => ApothecaryCost,
        BuildingKind.GuildWarrior => GuildWarriorCost,
        BuildingKind.GuildHall => GuildRangerCost,
        _ => 0, // MonsterLair 不可由玩家放置
    } * (1 << Math.Max(0, placedCount));

    /// <summary>下一座该种类建筑的造价（表现层选单/金库校验 — 与 PlaceBuilding 扣款同源）。</summary>
    public int NextPlaceCost(BuildingKind kind)
        => PlaceCost(kind, _placedCount.TryGetValue(kind, out var n) ? n : 0);

    // === 切片8「营建」：玩家 API — 建筑放置（玩家动词 #2，迁到公共建造模块之后）===
    //
    // 入口的次序是契约（spec 3 节第 6 条「经济排序契约」）：
    //   自查金库 → Commit → **成功才扣款** → 发经济事件。
    // 拒绝一寸不动（金库分文不动、不入册、不发事件），且**拒绝原因读得出来**：
    //   · 经济类（金库不足 / 巢穴不可放置）由本类判 —— 模块不认识钱（D3），封闭的 PlacementReason
    //     里没有经济那一档，所以不进那个枚举（别硬塞：塞进去就是让模块替经济说话）；
    //   · 几何类（过近 / 蓝图不在册）由模块判，原样带回来（<see cref="PlaceOutcome.CoreReason"/>）。
    // 玩家点鼠标、eval 直达口、重放注入走的是**同一个**入口（「桥只转发」）。

    /// <summary>建筑造价查询（表现层选单/金库校验用；不可放置种类返回 0）。</summary>
    public static int BuildingCost(BuildingKind kind) => kind switch
    {
        BuildingKind.Market => MarketCost,
        BuildingKind.Tavern => TavernCost,
        BuildingKind.Apothecary => ApothecaryCost,
        BuildingKind.GuildWarrior => GuildWarriorCost,
        BuildingKind.GuildHall => GuildRangerCost,
        _ => 0, // MonsterLair 不可由玩家放置
    };

    /// <summary>营建读数：成了没有 / 成了哪座 / 为什么没成（表现层与调试口读它，不自己拼）。</summary>
    /// <param name="Ok">这一发打成了。</param>
    /// <param name="BuildingId">成了那座的 Id；拒绝时是空串。</param>
    /// <param name="Reason">人读的拒绝原因（人读文本，不是给机器解析的）。</param>
    /// <param name="CoreReason">模块那一档拒绝原因；本类自己判的（经济类）那几档是
    /// <see cref="PlacementReason.None"/>，因为封闭枚举里没有经济原因（D3）。</param>
    public readonly record struct PlaceOutcome(bool Ok, string BuildingId, string Reason, PlacementReason CoreReason)
    {
        /// <summary>拒绝读数（本类自己判的没有模块那一档）。</summary>
        public static PlaceOutcome Rejected(string reason, PlacementReason core = PlacementReason.None)
            => new(false, "", reason, core);

        /// <summary>建成读数。</summary>
        public static PlaceOutcome Placed(string buildingId) => new(true, buildingId, "已建成", PlacementReason.None);
    }

    /// <summary>
    /// 玩家放置建筑：花金库在指定位置**即时**建成一座建筑（服务建筑或行会 — 切片 9 建筑即招募）。
    /// 校验语义同常规 RTS（不深究）：金库不足 / 与任一存活建筑过近 /
    /// 不可放置种类（巢穴）→ 拒绝返回 false，金库分文不动。
    /// 计价翻倍（Majesty 2）：第 n 座 = 基数 × 2^(n-1)（按已放置座数）。
    /// 完成标准：返回 true 表示建成入列，已发 BuildingPlaced + BuildingBuilt（金币转资产）。
    /// </summary>
    public bool PlaceBuilding(BuildingKind kind, Vector2 position) => TryPlaceBuilding(kind, position).Ok;

    /// <summary>
    /// 营建入口本体（带拒绝原因）—— 玩家路径、eval 直达口与重放注入同源（「桥只转发」）。
    /// </summary>
    public PlaceOutcome TryPlaceBuilding(BuildingKind kind, Vector2 position)
        => PlaceBuildingCore(kind, position, null, null);

    /// <summary>
    /// 这个入口的可注入形态 —— <paramref name="buildingId"/> 与 <paramref name="buildDurationTicks"/>
    /// 只有重放注入那一路会带（Journal 里那两段），玩家与 eval 两路都传 null：
    /// id 留空照本类的编号规则发（那套规则在重放端同样逐字复现，所以 Journal 不必记它），
    /// 工期留空照蓝图自带那份（本轮全部 0 ⇒ 即时建成，D16 无特判）。
    /// </summary>
    internal PlaceOutcome PlaceBuildingCore(BuildingKind kind, Vector2 position, string? buildingId,
        long? buildDurationTicks, BuildingPayload? payload = null)
    {
        var cost = NextPlaceCost(kind);
        if (cost <= 0)
        {
            return PlaceOutcome.Rejected($"{kind} 不可由玩家放置");
        }

        if (Treasury < cost)
        {
            return PlaceOutcome.Rejected($"金库不足（要 {cost}，现有 {Treasury}）");
        }

        var placed = _placedCount.TryGetValue(kind, out var count) ? count : 0;
        var id = buildingId ?? NextBuildingId(kind, placed);
        var loaded = payload ?? BuildingPayload.DefaultFor(kind);

        // 载荷先造好再 Commit：模块存而不读、原样挂在册上（D31），Placed 事件那一步直接取这一份。
        var building = new Building
        {
            Id = id,
            Kind = kind,
            Owner = loaded.Owner,
            SpawnIntervalTicks = loaded.SpawnIntervalTicks,
            Level = loaded.Level,
            Health = BuildingMaxHealth(kind),
        };

        // 间距判定全在这一次调用里（自由放置：半径和 6+6=12 = 迁前的中心距 12，D12）——
        // 玩家↔玩家、玩家↔场地建筑是同一段，因为场地建筑已经进册并占位（RegisterFixture）。
        // 方向段恒 0：本原型没有旋转需求，方向恒 0 就是它唯一的取值。
        var verdict = _construction.Commit(BlueprintIdOf(kind), position, 0, id, buildDurationTicks, building);
        if (!verdict.Ok)
        {
            return PlaceOutcome.Rejected($"模块判定放不下（{verdict.Reason}）", verdict.Reason);
        }

        // 成功才扣款：Commit 之前金库一文没动（拒绝 = 不扣款，D6）。
        Treasury -= cost;
        _placedCount[kind] = placed + 1; // 翻倍计价序号（先记账 — 再放置也按新价）
        EconomyEvent?.Invoke(new EconomyEventReport(null, EconomyEventKind.BuildingBuilt, cost));

        // 一处 drain：Placed → 原型 BuildingPlaced（表现层建模型）；Completed → 起产出链
        //（工期全 0 ⇒ 这两条在同一次 Commit 里同步连发，顺序 Placed 先，D16）。
        DrainConstructionEvents();
        return PlaceOutcome.Placed(id);
    }

    /// <summary>
    /// 下一座该种类建筑的 Id（命名规则只有这一处 —— 迁前在 PlaceBuilding 体内）：
    /// 服务建筑种子隐占 1 号（续序 2 起）；行会种子 Id 是 "guild"（不带序号）→ 玩家行会从 1 号起。
    /// </summary>
    private static string NextBuildingId(BuildingKind kind, int placed)
    {
        var isGuild = kind is BuildingKind.GuildHall or BuildingKind.GuildWarrior;
        var seq = isGuild ? placed + 1 : placed + 2;
        return $"{kind.ToString().ToLowerInvariant()}{seq.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>蓝图 id = 种类枚举名（表现层与 Journal 里那一段也是它，一份口径）。</summary>
    public static string BlueprintIdOf(BuildingKind kind) => kind.ToString();

    /// <summary>该种类建筑的血量上限（蓝图表与快照读它 —— 迁前那份 switch 搬到了这里）。</summary>
    public static int BuildingMaxHealth(BuildingKind kind) => kind switch
    {
        BuildingKind.Market => 600,      // 与种子市场同规格
        BuildingKind.Tavern => 400,      // 与种子酒馆同规格
        BuildingKind.Apothecary => 400,  // 与种子药店同规格
        BuildingKind.GuildHall => 1000,  // 与种子行会同规格
        BuildingKind.GuildWarrior => 1000,
        _ => 400,
    };

    /// <summary>玩家可营建的种类（蓝图表只有这几行 — 巢穴 / 宫殿 / BOSS 巢穴不是蓝图，D33）。</summary>
    private static readonly BuildingKind[] PlaceableKinds =
    [
        BuildingKind.Market, BuildingKind.Tavern, BuildingKind.Apothecary,
        BuildingKind.GuildHall, BuildingKind.GuildWarrior,
    ];

    /// <summary>
    /// 营建蓝图表 —— 模块构造时一次递入（D33）。工期全 0：营建是「即时建成」的玩法（切片 8 定稿），
    /// 建成那一刻模块就在同一次 Commit 里连发 Placed → Completed（D16「工期 0 无特判」）。
    ///
    /// <para>场地建筑那三行也在这张表里（<see cref="FixtureBlueprints"/>）：它们 2026-10-02 起
    /// 由 <see cref="RegisterFixture"/> 登记进册，而占地半径与血量上限都是**蓝图数据**
    /// （D12、D17）—— 没有蓝图就没有半径可占、没有上限可写。合并成一次递入而不是分两张表，
    /// 是因为它们走的是同一个 <see cref="ConstructionCore"/>，蓝图表是它唯一的蓝图真值。</para>
    /// </summary>
    private static IEnumerable<Blueprint> PlayerBlueprints()
    {
        foreach (var kind in PlaceableKinds)
        {
            yield return new Blueprint(BlueprintIdOf(kind), BuildingMaxHealth(kind),
                new RadiusFootprint(BlueprintRadius), buildDurationTicks: 0, modelPath: ModelPathOf(kind));
        }
    }

    /// <summary>
    /// 场地建筑的蓝图（宫殿 / 两种巢穴）—— 玩家放不了，但<b>照样得有</b>：场地建筑进模块册之后，
    /// 占地半径（D12）与血量上限（D17）都由蓝图表给出，模块存而不读。
    ///
    /// <para>半径与营建那五行同值（<see cref="BlueprintRadius"/>）：场地建筑跳过落位校验，
    /// 但占位照做，玩家读到的禁区边界就是这一份半径算的 —— 换成别的值的话，
    /// 「幽灵判过近」与「实际能不能摆下」会差一个半径。</para>
    ///
    /// <para><b>血量上限就是初始血量</b>（放置即满值，D17）：种子与重生巢穴同为
    /// <see cref="LairRespawnHealth"/>，这一行因此是它们两处摆位的同一份真值。</para>
    ///
    /// <para>模型引用给空串：营建幽灵只对玩家能放的五种有意义，那三种场地建筑由表现层那份
    /// 表兜（<see cref="ModelPathOf"/> 的注释）。</para>
    /// </summary>
    private static IEnumerable<Blueprint> FixtureBlueprints()
    {
        foreach (var kind in new[]
                 {
                     BuildingKind.Palace, BuildingKind.MonsterLair, BuildingKind.BossLair,
                 })
        {
            yield return new Blueprint(BlueprintIdOf(kind), FixtureMaxHealth(kind),
                new RadiusFootprint(BlueprintRadius), buildDurationTicks: 0, modelPath: ModelPathOf(kind));
        }
    }

    /// <summary>
    /// 场地建筑的血量上限（场地建筑那三张蓝图表与 <see cref="AddFixture"/> 读它）——
    /// 迁前那几个数是 <see cref="AddFixture"/> 调用点上给的字面量，搬进表里是因为
    /// 「血量上限是蓝图数据」（D17），而字面量留在调用点就等于那份表有第二处。
    /// </summary>
    public static int FixtureMaxHealth(BuildingKind kind) => kind switch
    {
        BuildingKind.Palace => PalaceHealth,          // 8000（与迁前逐字同一个数）
        BuildingKind.MonsterLair => LairRespawnHealth, // 300：种子与重生巢穴同规格
        BuildingKind.BossLair => BossLairHealth,      // 6000（终幕那一座）
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
            "场地建筑只有宫殿 / 巢穴 / BOSS 巢穴三种（其余种类在营建那一张表里）"),
    };

    /// <summary>造型资产根（<c>res://</c> 路径，不按 uid —— 「按 res:// 引用」）。与 <c>Main.BuildingModels</c> 同一个根。</summary>
    private const string ModelRoot = "res://src/Prototypes/InfinityLord/Models/";

    /// <summary>
    /// 蓝图上的模型引用（<c>res://</c>，「按 res:// 引用」）—— 营建幽灵模型从模块这张蓝图表读它，
    /// 不在表现层另抄一份 kind→模型表：那份表与蓝图一漂，幽灵摆的就是另一个造型（「真模型」
    /// 要的「真模型文件」，以及 D29 的「蓝图带模型引用字段，模块存而不读」）。
    ///
    /// <para><b>只有玩家能营建的这五种的表里有真模型路径</b>：营建幽灵按蓝图表上的引用摆造型
    /// （「真模型」 要真模型文件，「按 res:// 引用」 禁按 uid）。宫殿与两种巢穴 <b>2026-10-02 起也在蓝图表里</b>
    /// （<see cref="FixtureBlueprints"/>，它们要占地半径与血量上限这两项蓝图数据），
    /// 但模型引用给空串 —— 幽灵只对玩家能放的五种有意义，那三座由表现层那份场地建筑表兜，
    /// 两份表各管一半、互不重叠。</para>
    /// </summary>
    public static string ModelPathOf(BuildingKind kind) => kind switch
    {
        BuildingKind.GuildHall => ModelRoot + "GuildHall.glb",
        BuildingKind.GuildWarrior => ModelRoot + "GuildWarrior.glb",
        BuildingKind.Market => ModelRoot + "Market.glb",
        BuildingKind.Tavern => ModelRoot + "Tavern.glb",
        BuildingKind.Apothecary => ModelRoot + "Apothecary.glb",
        _ => "",
    };

    // === 模块事件流的 drain 点（模块只有只读出口，没有推送订阅）===

    /// <summary>
    /// 把模块事件流里新增的那几条走一遍 —— 这一侧与模块之间的<b>唯一</b>接线点。
    ///
    /// <para>模块的事件流是只追加的环形表、只给只读出口（没有推送订阅），所以「订阅」在这一侧
    /// 就是「每次模块调用之后把新增事件走一遍」。</para>
    ///
    /// <para><b>游标是「全局流里读到第几条」，不是「留下来的第几条」</b>（2026-10-02 修的
    /// 一条会静默毁世界的缺陷）。环形表满之后留下来的条数<b>恒等于容量</b>，而按留下来的条数记游标
    /// 的话，游标会停在容量上不动：下一次 drain 看到「已读 == 留下」，循环体一次都不跑 ——
    /// 于是模块后面发的每一条事件都再没人读。那不是丢事件，是<b>整个 drain 死掉</b>：
    /// 建筑被打空出册了、句柄表里那一座还在、表现层的模型也不清。
    /// 它一直藏着的理由是模块事件量很小（只有玩家写路径才发事件），
    /// 而 2026-10-02 场地建筑进册之后<b>每一次挨打都发一条 Damaged</b>（场地建筑的血量账
    /// 归模块），几百次交手就把 256 那格环形灌满，缺陷当场现形。</para>
    ///
    /// <para>两个读数是分开的：<see cref="ConstructionEventLog.Total"/> 是全局发过多少条
    /// （单调增、环形回退也不影响），<c>All.Count</c> 是还留着多少条。于是
    /// 「留下的那些在全局流里的起点」= <c>Total - All.Count</c>，游标可以直接对着全局序号走。</para>
    ///
    /// <para><b>中间真被挤掉一段时对账而不是装作没发生</b>：事件是有界表，册不是。
    /// 对账那一遍只补「状态」，一次性通知（建成）靠 <see cref="_completedSent"/> 幂等重发 ——
    /// 显示即真相这一条在这里就是「读册，不读事件流的回忆」（D29）。</para>
    /// </summary>
    private void DrainConstructionEvents()
    {
        var events = _construction.Events.All;
        var total = _construction.Events.Total;
        var keptFrom = total - events.Count;

        if (keptFrom > _eventCursor)
        {
            // 两次 drain 之间多到环形挤掉了未读的那一段：读不到的那几条永久读不到了，
            // 拿册对账补齐（见 ReconcileWithRegistry 的注释）。
            ReconcileWithRegistry();
            _eventCursor = keptFrom;
        }

        for (var g = _eventCursor; g < total; g++)
        {
            switch (events[(int)(g - keptFrom)])
            {
                case CorePlacedEvent placed:
                    OnCorePlaced(placed.Subject);
                    break;
                case BuildingRegistered registered:
                    OnCoreRegistered(registered.Subject);
                    break;
                case CoreCompletedEvent completed:
                    OnCoreCompleted(completed.Subject);
                    break;
                case BuildingRemoved removed:
                    OnCoreRemoved(removed.Subject);
                    break;
                default:
                    // Rejected / Damaged / Repaired / HealthSet / Moved / Rotated / 批量那几条：
                    // 逻辑层这一侧没有后续动作 —— 血量与坐标都是从模块那一行读，不另存一份。
                    break;
            }
        }

        _eventCursor = total;
    }

    /// <summary>
    /// 按模块册对账一遍句柄表 —— <b>事件流挤掉了未读那一段时的恢复手段</b>。
    ///
    /// <para>三件事：册里有而句柄表没有 ⇒ <see cref="AttachHandle"/> 补接上（载荷就是那份句柄本体，
    /// D31，补接不用重建任何数据）；句柄表里有而册里没有 ⇒ 走 <see cref="OnCoreRemoved"/> 那一套
    /// 收口（<b>去向按摧毁算</b>：本原型这一侧唯一会出册的路径是扣血归零 ——
    /// <c>RemoveBuilding</c> 不在 <see cref="InfinityLordConstructionSink"/> 的名单里，
    /// 这不是猜，是那一侧动词表的读数）；册里已完工而没报过建成 ⇒ <see cref="OnCoreCompleted"/>,
    /// 它被 <see cref="_completedSent"/> 挡着重复，重发是安全的。</para>
    ///
    /// <para>平时<b>不走这里</b>：只有环形挤掉未读那一段时才逐座对账。日常路径仍是走事件流
    /// （那样才带得住「哪一下打的」这类次序信息），这里是兜底而不是常规。</para>
    /// </summary>
    private void ReconcileWithRegistry()
    {
        foreach (var id in _construction.Ids)
        {
            if (!_buildings.Any(b => b.Id == id))
            {
                var row = _construction.Find(id)!;
                if (row.Payload is Building orphan)
                {
                    AttachHandle(orphan, row);
                }
            }

            if (_construction.Find(id)!.State == BuildState.Completed)
            {
                OnCoreCompleted(id); // 幂等：_completedSent 挡着重复
            }
        }

        foreach (var stale in _buildings.Where(b => _construction.Find(b.Id) is null).ToList())
        {
            OnCoreRemoved(stale.Id);
        }
    }

    /// <summary>
    /// 模块「建成」这一条：起产出链（<see cref="StartSpawnChain"/> 自己幂等）＋ 向表现层发一次建成事件
    /// （建模型走这条，见 <see cref="BuildingCompleted"/>）。
    ///
    /// <para><see cref="_completedSent"/> 挡的是环形事件流重扫：挤过之后同一座会再走进来一次，
    /// 而表现层按这条事件建模型 —— 让它收到两次就是同一处摆两座楼（<see cref="OnCorePlaced"/> 的
    /// 入列去重是同一个道理，只是那条挡在「册」上、这条挡在「事件」上）。</para>
    /// </summary>
    private void OnCoreCompleted(string buildingId)
    {
        // 读档那一发不起产出链：产出链是往时钟里排一条新的自续事件，而读档要按存档里那张表重排。
        // 不挡这一下的话，每恢复一座楼就多排一条 —— 多出来的那条不在表里，下一次存档两串就不相等。
        if (!_restoring) StartSpawnChain(buildingId);
        if (!_completedSent.Add(buildingId))
        {
            return;
        }

        var building = _buildings.FirstOrDefault(b => b.Id == buildingId);
        if (building is null)
        {
            return; // 已出册（重扫时挨了一次摧毁）：没有活着的建筑可报
        }

        BuildingCompleted?.Invoke(building);
    }

    /// <summary>模块落座：句柄接上册内那一行、进列表、发原型事件（表现层建模型靠它）。</summary>
    private void OnCorePlaced(string buildingId)
    {
        if (_buildings.Any(b => b.Id == buildingId))
        {
            return; // 重扫到了同一次落座（环形挤过之后重读）：不重复入列、不重复发事件
        }

        var row = _construction.Find(buildingId)
            ?? throw new InvalidOperationException(
                $"{buildingId} 发了 Placed 却在册里查不到：模块册与事件流分家了，去查 Commit");
        if (row.Payload is not Building building)
        {
            throw new InvalidOperationException(
                $"{buildingId} 的载荷不是本类的建筑对象：载荷是原型的私产（D31），形状变了");
        }

        AttachHandle(building, row);
        BuildingPlaced?.Invoke(building);
    }

    /// <summary>
    /// 挂句柄：接上册内那一行 + 进列表。<b>接上与入列只有这一个实现</b>（玩家营建、
    /// 场地建筑登记、对账补接三条路共用）—— 两处各写一遍的话，漏掉的那一处就是
    /// 「册里有一座、句柄表里没有」，而那正是快照与表现层各自少一行的形状。
    ///
    /// <para><b>技能层那一册也在这里登记</b>：建筑进技能目标域（Q12 后半）之后，
    /// 「世界这一侧认得这一座楼」与「技能层认得它」必须是同一件事 —— 分两处写的话，
    /// 漏掉的那一处症状是「这一座打不着」（技能层激活检查判它 <c>TargetGone</c>），
    /// 而现场看是「怪物围着楼站着不出手」。挂链与登记是同一发里的两件事。</para>
    /// </summary>
    private void AttachHandle(Building building, CoreBuilding row)
    {
        building.AttachCore(row);
        _buildings.Add(building);
        _abilities.RegisterBuilding(building);
    }

    /// <summary>
    /// 模块登记场地建筑（种子 / 终幕 / 巢穴重生）：句柄接上册内那一行、进列表。
    ///
    /// <para><b>不发 <see cref="BuildingPlaced"/> 那一发</b>：它读作「玩家放下了一座」。
    /// 场地建筑不是玩家手势（模块只发 Registered 不发 Completed，理由在模块那边），
    /// 而表现层开局照 <c>_world.Buildings</c> 逐座摆模型（<c>Main</c>）、
    /// 重生那几座跟 <see cref="BuildingRespawned"/> —— 两条都不等这条事件。
    /// 补发它只会让订阅者多写一个「这一座是场地建筑」的例外分支。</para>
    /// </summary>
    private void OnCoreRegistered(string buildingId)
    {
        if (_buildings.Any(b => b.Id == buildingId))
        {
            return; // 重扫到了同一次登记（环形挤过之后重读）：不重复入列
        }

        var row = _construction.Find(buildingId)
            ?? throw new InvalidOperationException(
                $"{buildingId} 发了 Registered 却在册里查不到：模块册与事件流分家了，去查 RegisterFixture");
        if (row.Payload is not Building building)
        {
            throw new InvalidOperationException(
                $"{buildingId} 的载荷不是本类的建筑对象：载荷是原型的私产（D31），形状变了");
        }

        AttachHandle(building, row);
    }

    /// <summary>
    /// 模块出册（摧毁 / 拆除）：句柄摘掉册内那一行、出列表、发原型事件（表现层清模型），
    /// 然后跑摧毁后的结算。
    ///
    /// <para><b>结算三档挂在这里而不是在扣血那一侧</b>：模块那一发里已经把「打空 → 出册 + 腾占用 +
    /// 发 Removed」做完，本类能看见摧毁的<b>唯一</b>入口就是这条事件。挂在别处的话，
    /// 「摧毁之后要做什么」就有第二个可以长出来的地方 —— 而那一处不会跟着模块的出册链一起被测到。</para>
    /// </summary>
    private void OnCoreRemoved(string buildingId)
    {
        var building = _buildings.FirstOrDefault(b => b.Id == buildingId);
        if (building is null)
        {
            return; // 已经出册（重扫）
        }

        building.DetachCore();
        _buildings.Remove(building);
        _abilities.RetireBuilding(building.Id);
        BuildingDestroyed?.Invoke(building);

        // 玩家营建那几座是服务建筑 / 行会，三档都落不到（巢穴与宫殿不是蓝图，PlaceableKinds 里没有）。
        if (building.Kind == BuildingKind.MonsterLair)
        {
            _clock.Schedule(LairRespawnDelayTicks, () => RespawnLair(building.Position));
        }
        else if (building.Kind == BuildingKind.Palace)
        {
            EndGame(GameOutcome.Defeat); // 宫殿被毁 = 失败（切片 10 终局判定）
        }
        else if (building.Kind == BuildingKind.BossLair)
        {
            EndGame(GameOutcome.Victory); // BOSS 巢穴被毁 = 通关（不挂重生调度 — 终幕不复活）
        }
    }

    // === 经济周期 — 村民税保底（金库无固定支出 — 冒险者自雇，压力全来自玩家悬赏消费） ===

    /// <summary>
    /// 经济周期结算（每 60 步）：村民税（保底收入）入账，净额 = 收入（无系统性支出）。
    /// 冒险者完全自雇：行会不发饷、无维护费 — 金库的下行压力只来自玩家的悬赏消费。
    /// 完成标准：结算后发 VillagerTax 经济事件。
    /// </summary>
    private void EconomyCycle()
    {
        var income = VillagerTaxPerCycle;
        Treasury += income;
        LastCycleNet = income;
        EconomyEvent?.Invoke(new EconomyEventReport(null, EconomyEventKind.VillagerTax, income));
        ScheduleNextCycle();
    }

    /// <summary>经济周期自续调度（产出链同款 Schedule 自续写法）。</summary>
    private void ScheduleNextCycle()
        => _clock.Schedule(EventEconomy, EconomyCycleTicks, EconomyCycle);

    // === 需求流失 — 冒险者自雇的退场机制（「行会信誉破产/市场枯竭 → 自利离开」） ===

    /// <summary>
    /// 需求饥饿累计（每步）：赏金饥饿与酒馆饥饿两条线。
    /// 赏金饥饿 = 连续零收入（既无悬赏承接也无任何金币进账）；
    /// 酒馆饥饿 = 想喝（士气低于阈值）却喝不上（没钱或世界无存活酒馆）。
    /// 任一计时满 NeedStarveLeaveTicks → 冒险者自利离队（走清尸流程，防旗子孤儿化）。
    /// </summary>
    private void AccumulateHeroNeeds()
    {
        var tavernExists = _buildings.Any(b => b.Kind == BuildingKind.Tavern && b.Health > 0);
        for (var i = _units.Count - 1; i >= 0; i--)
        {
            var unit = _units[i];
            if (unit.Side != Faction.Hero || unit.Health <= 0) continue;

            // 赏金饥饿：承接悬赏（有活干）或任何金币进账（战利品/结算等 — 零收入口径）即被满足；
            // 「上步以来钱包变化」是进账侧的统一信号（测试排布直接改 Gold 同样算被满足）
            if (unit.ClaimedFlagId is not null || unit.Gold > unit.LastObservedGold)
            {
                unit.NoIncomeTicks = 0;
            }
            else
            {
                unit.NoIncomeTicks++;
            }
            unit.LastObservedGold = unit.Gold;
            if (unit.NoIncomeTicks >= NeedStarveLeaveTicks)
            {
                HeroLeaves(unit, EconomyEventKind.HeroLeftNoBounty);
                continue;
            }

            // 酒馆饥饿：想喝却喝不上（没钱 or 无酒馆）；有钱+有酒馆时正在赶路/已喝到 → 被满足
            var canDrink = tavernExists && unit.Gold >= DrinkPrice;
            if (unit.Morale >= MoraleDrinkThreshold || canDrink)
            {
                unit.TavernStarveTicks = 0;
            }
            else
            {
                unit.TavernStarveTicks++;
                if (unit.TavernStarveTicks >= NeedStarveLeaveTicks)
                {
                    HeroLeaves(unit, EconomyEventKind.HeroLeftNoTavern);
                }
            }
        }
    }

    /// <summary>
    /// 冒险者自利离队（需求长期不被满足）— 同时释放其承接的旗子（复用 ReleaseFlagsOf 防孤儿化）。
    /// 实现说明：走清尸流程而非另开删除通道 — 先 ReleaseFlagsOf，再发 UnitDied
    /// 让表现层清模型，最后从单位表移除；离队原因走 HeroLeft 事件（含 EconomyEvent 便于聚合统计）。
    /// </summary>
    private void HeroLeaves(Unit hero, EconomyEventKind reason)
    {
        ReleaseFlagsOf(hero);
        hero.Health = 0;
        _nav.Retire(hero);
        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, reason, hero.Gold));
        UnitDied?.Invoke(new UnitDiedReport(hero.Id, hero.Name, Faction.Hero, hero.Level, hero.Gold));
        _units.Remove(hero);
    }

    // === 切片3：行情公开 API — 让玩家「算得出行情」（AI 可预期红线） ===

    /// <summary>
    /// 估算目标威胁值（勇气公式的分母，公开给表现层做行情提示）。
    /// 完成标准：单位返回 攻击×2+血量/5；建筑返回 血量×折算系数（见 BuildingThreat）；目标无效返回 null。
    /// </summary>
    public float? EstimateThreat(string targetId)
    {
        var unit = _units.FirstOrDefault(u => u.Id == targetId && u.Side == Faction.Monster && u.Health > 0);
        if (unit is not null) return unit.Attack * 2 + unit.MaxHealth / 5f;

        var building = _buildings.FirstOrDefault(b => b.Id == targetId && b.Owner == Faction.Monster && b.Health > 0);
        if (building is not null) return BuildingThreat(building);

        return null;
    }

    /// <summary>
    /// 建筑威胁折算 — 单一口径（悬停行情与勇气公式共用，显示即真相）：
    /// 普通巢穴按血量 ×BuildingThreatFactor；BOSS 巢穴取固定档 BossThreat（切片 10，理由见该常量）。
    /// </summary>
    private static float BuildingThreat(Building building)
        => building.Kind == BuildingKind.BossLair
            ? BossThreat
            : building.Health * BuildingThreatFactor;

    /// <summary>
    /// 建议定价：恰好达到满血英雄勇气阈值（BraveryRatio）的金额。
    /// 定价低于此值 → 无人接单；高于此值 → 秒接且有溢价浪费。向上取整便于阅读。
    /// </summary>
    public int SuggestedPrice(string targetId)
        => EstimateThreat(targetId) is not { } threat ? 0 : (int)MathF.Ceiling(threat * BraveryRatio);

    // === 切片4：战力感知 — 养出的强英雄眼中的世界更安全（「养英雄」的回报机制） ===

    /// <summary>英雄战力评估（勇气公式内用）：攻击×2 + 血量/5 + 装备×3 — 与威胁同构，可横向比较。</summary>
    public static float HeroPower(Unit hero)
        => hero.Attack * 2f + hero.MaxHealth / 5f + hero.Gear * 3f;

    /// <summary>
    /// 英雄自估威胁：目标威胁 × (基准战力/自身战力) — 装备精良的英雄对同一目标感知威胁更低，
    /// 因而敢接更硬的单（勇气公式的战力感知修正）。下限 0.4 防止无限自我膨胀。
    /// </summary>
    public static float PerceivedThreat(Unit hero, float baseThreat)
    {
        const float baselinePower = 28f; // 新兵基准战力：8×2 + 60/5 = 28（感知系数=1）
        var factor = Math.Clamp(baselinePower / MathF.Max(HeroPower(hero), 1f), 0.4f, 1f);
        return baseThreat * factor;
    }

    // === 世界脉搏步进 ===
    //
    // 一次脉搏里跑三段，订阅次序就是执行次序（两段的订阅点见构造）：
    //   ① StepDecide —— 落场、技能层推进、行为树驱动、逐单位决策（决策链发令）；
    //   ② 寻路层自己的那一次订阅 —— 照命令走位；
    //   ③ StepAfterMove —— 镜像同步、悬赏结算、阵亡清理、士气衰减、需求累积。

    /// <summary>
    /// 每步的第一段 —— <b>决策那一段</b>（订阅在寻路层之前，所以它先跑）：
    /// 落场 → 技能层推进一步 → 行为树驱动 → 逐单位决策（决策链发令）。
    ///
    /// <para><b>为什么决策排在走位之前</b>：位置真值住在寻路层，层那一次订阅排在决策之后 ——
    /// 于是这一步发的命令这一步就走。走位之后那一段见 <see cref="StepAfterMove"/>。</para>
    /// </summary>
    private void StepDecide(long tick)
    {
        // 终局判定后世界照常演化（不冻结）：无早退。

        while (_spawnQueue.TryDequeue(out var fresh))
        {
            _units.Add(fresh);
            if (fresh.Health > 0) _nav.Register(fresh);
            _abilities.Register(fresh);
        }

        // 技能层那一段排在决策之前：层的冷却 / 效果 / tag 从这一步起算，
        // 于是「世界按卡上的间隔出手时层里正好转好」这件事逐步成立（对齐口径见技能表）。
        // 层这一步读到的血是上一步落定之后的数（世界是血的真值）。
        _abilities.AdvanceStep();

        // 行为树驱动口排在这里：树这一步写的意图紧接着被下面的决策链读走，
        // 于是树发的令也在同一步生效（排在决策之后或排到步骤末尾，那一发令要等下一步的决策段
        // 才被读走 —— 树说「追」，它却先按上一条命令走完这一步）。
        // 树读到的读数与决策链读到的是同一份状态：上一步走位落定之后的位置。
        StepStarted?.Invoke(tick);

        foreach (var unit in _units)
        {
            if (unit.Health <= 0) continue;
            if (unit.Side == Faction.Hero) StepHero(unit);
            else StepMonster(unit);
        }
    }

    /// <summary>
    /// 每步的第二段 —— <b>走位之后那一段</b>（订阅在寻路层之后，所以它后跑）：
    /// 镜像同步 → 悬赏结算 → 阵亡清理 → 士气衰减 → 需求累积。
    ///
    /// <para>它读到的位置是这一步走完之后的落点（寻路层那一次订阅已经跑过），
    /// 下一步的决策读到的也是它。</para>
    /// </summary>
    private void StepAfterMove(long tick)
    {
        // 走位那一段：这一步的名单先抓一份（阵亡退场的那几个不该被镜像写回旧点），
        // 决策链发完令之后按命令走位，走完这一步的位置写回镜像。
        _nav.SyncMirrors(AliveUnits());

        SettleFlags();

        for (var i = _units.Count - 1; i >= 0; i--)
        {
            if (_units[i].Health > 0) continue;
            var dead = _units[i];
            _units.RemoveAt(i);
            _nav.Retire(dead);
            ReleaseFlagsOf(dead);
            // 守恒审计口：阵亡英雄的随身金币随遗体消失 — 报告显式带出（可观测，不再无声蒸发）
            UnitDied?.Invoke(new UnitDiedReport(dead.Id, dead.Name, dead.Side, dead.Level, dead.Gold));
        }

        // 切片7：士气自然衰减（每 MoraleDecayTicks 步全体 −1，磨平热情 — 需求的压力源）
        if (++_moraleDecayTimer >= MoraleDecayTicks)
        {
            _moraleDecayTimer = 0;
            foreach (var hero in _units.Where(u => u.Side == Faction.Hero && u.Morale > 0))
            {
                hero.Morale--;
            }
        }

        // 需求流失：冒险者自雇 — 需求长期不被满足就离开（人口靠生态承载力托底）
        AccumulateHeroNeeds();

        // 「这一步演化完了」那一发在最后：走位落定、镜像写好、结算与清理都跑完。
        // 技能层 cue 与确定性测试挂它逐步读数；树不挂它（见 StepStarted）。
        Stepped?.Invoke(tick);
    }

    /// <summary>
    /// 释放阵亡英雄承接的悬赏旗（旗子回到待接池，其他英雄可接手）。
    /// 否则旗子被死去的英雄永久占用 → 悬赏孤儿化（实测踩坑）。
    /// </summary>
    private void ReleaseFlagsOf(Unit dead)
    {
        foreach (var flag in _flags)
        {
            if (flag.ClaimedByHeroId == dead.Id && !flag.Settled)
            {
                flag.ClaimedByHeroId = null;
                BountyEvent?.Invoke(new BountyEventReport(flag.Id, flag.TargetId, dead.Id, flag.Gold, BountyEventKind.Released));
            }
        }
    }

    /// <summary>
    /// 英雄 AI — Majesty 式自利决策链：
    /// 有悬赏且目标活着 → 追击；否则拾取新悬赏（勇气评估）；否则近市场消费/漫游。
    /// </summary>
    private void StepHero(Unit hero)
    {
        if (hero.CooldownRemaining > 0) hero.CooldownRemaining--;

        // 0) 行为树接管：只执行树写入的意图，下面的 Majesty 决策链整个跳过
        if (_brainIntents.TryGetValue(hero.Id, out var brain))
        {
            ApplyBrainIntent(hero, brain);
            return;
        }

        // 1) 已承接悬赏：追击目标（怪物单位）或攻坚（怪物建筑）
        if (hero.ClaimedFlagId is not null)
        {
            var flag = _flags.FirstOrDefault(f => f.Id == hero.ClaimedFlagId && !f.Settled);
            if (flag is not null)
            {
                var targetUnit = _units.FirstOrDefault(u => u.Id == flag.TargetId && u.Health > 0);
                if (targetUnit is not null)
                {
                    hero.TargetUnitId = targetUnit.Id;
                    Engage(hero, targetUnit);
                    return;
                }
                var targetBuilding = _buildings.FirstOrDefault(b => b.Id == flag.TargetId && b.Health > 0);
                if (targetBuilding is not null)
                {
                    EngageBuilding(hero, targetBuilding);
                    return;
                }
            }
            // 目标已亡/建筑已毁：悬赏由 SettleFlags 统一结算，解除承接
            hero.ClaimedFlagId = null;
        }

        // 2) 拾取新悬赏：勇气评估（赏金/目标战力 ≥ 阈值才出工）
        if (TryClaimFlag(hero)) return;

        // 3) 战斗 instinct：视野内敌人直接交战（保命+保护行会）
        var enemy = FindNearestEnemy(hero);
        if (enemy is not null)
        {
            hero.TargetUnitId = enemy.Id;
            Engage(hero, enemy);
            return;
        }

        // 4) 消费欲望：按需求选建筑（切片7 需求层级 — 决策链第 4 步的落点）
        //    优先级：伤势（低血买药剂回血，惜命本能）→ 士气（低士气去酒馆）→ 购物（钱包 ≥30 逛市场）
        if (hero.Health < hero.MaxHealth * PotionSeekRatio && hero.Gold >= PotionPrice)
        {
            var apothecary = _buildings.FirstOrDefault(b => b.Kind == BuildingKind.Apothecary && b.Health > 0);
            if (apothecary is not null)
            {
                if (Vector2.Distance(hero.Position, apothecary.Position)
                    < InfinityLordNavGrid.InteractionRangeMeters(RadiusOf(apothecary)))
                {
                    // 买药剂回血：金币出口之一（走 HeroSpent → 40% 税收回流，闭环不泄漏）
                    var spend = 0;
                    while (hero.Gold >= PotionPrice && hero.Health < hero.MaxHealth)
                    {
                        hero.Gold -= PotionPrice;
                        spend += PotionPrice;
                        hero.Health = Math.Min(hero.Health + PotionHeal, hero.MaxHealth);
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroBoughtPotion, hero.Health));
                    }
                    if (spend > 0)
                    {
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroSpent, spend));
                        var tax = (int)(spend * TaxRate);
                        Treasury += tax;
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.TaxCollected, tax));
                    }
                }
                else
                {
                    _nav.GoToBuilding(hero, apothecary);
                    return;
                }
            }
        }

        if (hero.Morale < MoraleDrinkThreshold && hero.Gold >= DrinkPrice)
        {
            var tavern = _buildings.FirstOrDefault(b => b.Kind == BuildingKind.Tavern && b.Health > 0);
            if (tavern is not null)
            {
                if (Vector2.Distance(hero.Position, tavern.Position)
                    < InfinityLordNavGrid.InteractionRangeMeters(RadiusOf(tavern)))
                {
                    // 喝酒恢复士气：喝到满（酒馆是场合不是补给站）；零钱不足为止
                    var spend = 0;
                    while (hero.Gold >= DrinkPrice && hero.Morale < 100)
                    {
                        hero.Gold -= DrinkPrice;
                        spend += DrinkPrice;
                        hero.Morale = Math.Min(hero.Morale + DrinkMoraleRestore, 100);
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroDrank, hero.Morale));
                    }
                    if (spend > 0)
                    {
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroSpent, spend));
                        var tax = (int)(spend * TaxRate);
                        Treasury += tax;
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.TaxCollected, tax));
                    }
                }
                else
                {
                    _nav.GoToBuilding(hero, tavern);
                    return;
                }
            }
        }

        // 购物：钱包 >= 30 去市场买装备（金币循环的起点；零钱倾倒语义保留 — 税收回流保底）
        if (hero.Gold >= 30)
        {
            var market = _buildings.FirstOrDefault(b => b.Kind == BuildingKind.Market && b.Health > 0);
            if (market is not null)
            {
                if (Vector2.Distance(hero.Position, market.Position)
                    < InfinityLordNavGrid.InteractionRangeMeters(RadiusOf(market)))
                {
                    // 切片4：优先买装备（$20/级：攻击+3、血上限+10）—「养英雄」闭环：
                    // 玩家税收 → 英雄钱包 → 装备战力 → 敢接硬单/更耐打 → 更容易赚更多赏金
                    var spend = 0;
                    while (hero.Gold >= GearPrice)
                    {
                        hero.Gold -= GearPrice;
                        spend += GearPrice;
                        hero.Gear++;
                        hero.Attack += GearAttackBonus;
                        hero.MaxHealth += GearHealthBonus;
                        hero.Health = Math.Min(hero.Health + GearHealthBonus, hero.MaxHealth);
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroBoughtGear, hero.Gear));
                    }
                    spend += hero.Gold; // 剩余零钱零花（同为市场消费；喝酒已拆到酒馆）
                    hero.Gold = 0;
                    if (spend > 0)
                    {
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroSpent, spend));
                        var tax = (int)(spend * TaxRate);
                        Treasury += tax;
                        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.TaxCollected, tax));
                    }
                }
                else
                {
                    _nav.GoToBuilding(hero, market);
                    return;
                }
            }
        }

        // 5) 漫步
        Roam(hero);
    }

    /// <summary>
    /// 漫步原语 —— 挑个附近的随机点走过去，到点再挑一个。随机偏移出自世界的带种子随机数，
    /// 所以同一颗种子跑两次轨迹逐步相同。两个入口共用这一条：Majesty 决策链的第 5 步、
    /// 以及行为树写回的 <see cref="BrainIntent.Wander"/>（树只说「现在该闲逛」，怎么走在这儿）。
    ///
    /// <para>「到没到」读的是<b>层里那一条移动解</b>：目标点由 <see cref="Unit.MoveTarget"/> 记着，
    /// 但「走到了没有」不再由这一侧自己算 —— 上一版那个 <c>Reached</c> 与直线趋近一起退休了。</para>
    /// </summary>
    private void Roam(Unit hero)
    {
        if (hero.MoveTarget is null || _nav.ArrivedAt(hero.Id, hero.MoveTarget.Value))
        {
            // 漫步点收进战场：界外的点解不出路线，发出去就是「这一步白站」
            // （实测有英雄一路走到 X≈-35，从此再也回不来）。
            hero.MoveTarget = ClampIntoField(hero.Position + RandomOffset(8f));
        }

        _nav.GoToPoint(hero, hero.MoveTarget.Value);
    }

    /// <summary>
    /// 把一个世界坐标收进战场网格内（各留 <paramref name="margin"/> 米边距）。
    /// 界外的目标对寻路层是「地形无路」，收进来才解得出路线。
    /// </summary>
    private static Vector2 ClampIntoField(Vector2 p, float margin = 1f)
        => new(
            Math.Clamp(p.X, (float)InfinityLordNavGrid.MinX + margin, (float)InfinityLordNavGrid.MaxX - margin),
            Math.Clamp(p.Y, (float)InfinityLordNavGrid.MinZ + margin, (float)InfinityLordNavGrid.MaxZ - margin));

    /// <summary>
    /// 勇气评估并承接悬赏 — 英雄自主决策（玩家无法强令）：
    /// 赏金/(目标战力×风险系数) ≥ BraveryRatio 才接；战损过半的英雄更惜命。
    /// 目标是建筑时按血量折算威胁（BuildingThreatFactor）。
    /// 完成标准：返回 true 表示成功承接一面旗。
    /// </summary>
    /// <summary>
    /// 勇气评估并承接悬赏 — 英雄自主决策（玩家无法强令）：
    /// 赏金/(目标战力×风险系数) ≥ BraveryRatio 才接；战损过半的英雄更惜命；
    /// 低士气放大感知威胁（切片7 乘惩罚 — 拒绝原因可通过悬停解释）。
    /// 目标是建筑时按血量折算威胁（BuildingThreatFactor）。
    /// 完成标准：返回 true 表示成功承接一面旗。
    /// </summary>
    private bool TryClaimFlag(Unit hero)
    {
        foreach (var flag in _flags)
        {
            if (flag.Settled || flag.ClaimedByHeroId is not null) continue;

            var hpFactor = hero.Health / (float)hero.MaxHealth; // 血越少越惜命
            var threshold = BraveryRatio * ClassBraveryFactor(hero.Class) / hpFactor; // 切片9：职业勇气分化（战士阈值 ×0.7 — 更莽）

            var targetUnit = _units.FirstOrDefault(u => u.Id == flag.TargetId && u.Health > 0);
            float appeal;
            if (targetUnit is not null)
            {
                // 切片4：战力感知 — 同一面旗，强英雄眼中威胁更低（PerceivedThreat 修正）
                var baseThreat = targetUnit.Attack * 2 + targetUnit.MaxHealth / 5f;
                appeal = flag.Gold / PerceivedThreat(hero, baseThreat);
            }
            else
            {
                var targetBuilding = _buildings.FirstOrDefault(b => b.Id == flag.TargetId && b.Owner == Faction.Monster && b.Health > 0);
                if (targetBuilding is null) continue;
                var baseThreat = BuildingThreat(targetBuilding);
                appeal = flag.Gold / PerceivedThreat(hero, baseThreat);
            }

            // 切片7：士气惩罚 — appeal = 赏金/(感知威胁×士气系数)。士气越低 → 分母越大 → 越不接。
            // 惩罚随士气线性：士气 60 以下开始放大，士气 0 时感知威胁 ×2（MoralePenaltyMax=1.0）。
            var moraleFactor = hero.Morale < MoraleDrinkThreshold
                ? 1f + MoralePenaltyMax * (MoraleDrinkThreshold - hero.Morale) / MoraleDrinkThreshold
                : 1f;
            appeal /= moraleFactor;

            if (appeal < threshold) continue; // 不够勇，不接

            flag.ClaimedByHeroId = hero.Id;
            hero.ClaimedFlagId = flag.Id;
            BountyEvent?.Invoke(new BountyEventReport(flag.Id, flag.TargetId, hero.Id, flag.Gold, BountyEventKind.Claimed));
            return true;
        }
        return false;
    }

    /// <summary>
    /// 悬赏结算 — 目标死亡时赏金支付给承接英雄。
    /// 单点结算（海关）：所有赏金流动只发生在这里。
    /// </summary>
    private void SettleFlags()
    {
        foreach (var flag in _flags)
        {
            if (flag.Settled) continue;

            var targetUnit = _units.FirstOrDefault(u => u.Id == flag.TargetId);
            var targetBuilding = targetUnit is null
                ? _buildings.FirstOrDefault(b => b.Id == flag.TargetId)
                : null;

            // 目标存活：不结算（单位活着，或建筑未摧毁）
            if ((targetUnit is not null && targetUnit.Health > 0)
                || (targetBuilding is not null && targetBuilding.Health > 0))
            {
                continue;
            }

            // 目标已死/建筑已毁：若被承接则发赏；无人承接则退款（玩家不白花钱）
            var refunded = false;
            if (flag.ClaimedByHeroId is not null)
            {
                var hero = _units.FirstOrDefault(u => u.Id == flag.ClaimedByHeroId && u.Health > 0);
                if (hero is not null)
                {
                    hero.Gold += flag.Gold;
                    hero.Xp += flag.Gold; // 赏金即战利品声望：换算经验
                    EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroEarned, flag.Gold));
                    CheckLevelUp(hero);
                }
                else
                {
                    // 承接者已亡且旗子未释放（理论少见）：赏金退回金库
                    Treasury += flag.Gold;
                    EconomyEvent?.Invoke(new EconomyEventReport(null, EconomyEventKind.BountyRefunded, flag.Gold));
                    refunded = true;
                }
            }
            else
            {
                // 无人承接（目标被战斗本能顺手杀死等）：悬赏原路退回金库
                Treasury += flag.Gold;
                EconomyEvent?.Invoke(new EconomyEventReport(null, EconomyEventKind.BountyRefunded, flag.Gold));
                refunded = true;
            }
            flag.Settled = true;
            BountyEvent?.Invoke(new BountyEventReport(
                flag.Id, flag.TargetId, flag.ClaimedByHeroId, flag.Gold,
                refunded ? BountyEventKind.Refunded : BountyEventKind.Paid));
        }

        // 已结算旗子出队（结算流已通知表现层移除模型）
        _flags.RemoveAll(f => f.Settled);
    }

    /// <summary>升级检查：XP 阈值 = 50×当前等级，升级 +15 最大血、回满血。</summary>
    private void CheckLevelUp(Unit hero)
    {
        var threshold = 50 * (hero.Level + 1);
        if (hero.Xp < threshold) return;
        hero.Level++;
        hero.MaxHealth += 15;
        hero.Health = hero.MaxHealth;
        EconomyEvent?.Invoke(new EconomyEventReport(hero.Id, EconomyEventKind.HeroLeveledUp, hero.Level));
    }

    /// <summary>怪物 AI：视野内有敌人交战，否则进军最近的玩家建筑（袭城压力）。</summary>
    private void StepMonster(Unit monster)
    {
        if (monster.CooldownRemaining > 0) monster.CooldownRemaining--;

        if (monster.TargetUnitId is not null)
        {
            var target = _units.FirstOrDefault(u => u.Id == monster.TargetUnitId);

            // 目标没了、死了、或者走到了战场界外 —— 三种都撤令重选。
            // 界外那一条是实测踩出来的：界外目标解不出路线，盯着它不放就等于把这一名废掉
            // （症状是「地形无路」的步数一直涨，而它的目标坐标从头到尾不变）。
            if (target is null || target.Health <= 0 || !IsInsideField(target.Position))
            {
                monster.TargetUnitId = null;
            }
            else
            {
                Engage(monster, target);
                return;
            }
        }

        var enemy = FindNearestEnemy(monster);
        if (enemy is not null)
        {
            monster.TargetUnitId = enemy.Id;
            Engage(monster, enemy);
            return;
        }

        var targetBuilding = _buildings
            .Where(b => b.Owner == Faction.Hero && b.Health > 0)
            .OrderBy(b => Vector2.DistanceSquared(monster.Position, b.Position))
            .FirstOrDefault();
        if (targetBuilding is not null)
        {
            // 逼近与出手共用同一个数（与 Engage 那一侧同形）：够不着就逼近，够了就问层。
            // 这里原先带 1 米容差，而层按技能表那一份射程判 —— 两把尺差 1 米的那条带里
            // 世界不逼近、层也拒，症状是「围着楼站着一步不出手」，账上只看得出伤害变少。
            var gap = DistanceToWall(monster, targetBuilding);
            if (gap > monster.AttackRange)
            {
                _nav.ChaseBuilding(monster, targetBuilding);
            }
            else if (monster.CooldownRemaining == 0
                && _abilities.StrikeBuilding(monster, targetBuilding, gap))
            {
                monster.CooldownRemaining = monster.AttackCooldownTicks;
                var damage = (int)Math.Round(_abilities.LastDamage, MidpointRounding.AwayFromZero);
                CombatEvent?.Invoke(new CombatEventReport(monster.Id, targetBuilding.Id, damage,
                    targetBuilding.Health <= 0, TargetIsUnit: false));
            }
        }
    }

    /// <summary>
    /// 单位到一座楼<b>外墙</b>的距离（米）—— 楼占着半径那么大一块地，
    /// 单位最近也只能站在墙外，所以「够不够得着」判的是墙皮到墙皮，不是楼心到楼心。
    ///
    /// <para><b>为什么必须扣掉半径</b>：建筑占地是阻挡格，半径 6 米的楼在网格上形成
    /// 半径 6.5 米的禁区，单位最近站在离楼心 6.5 米处。而攻坚与袭城的停手线原本写的是
    /// 「离楼心 ≤ 攻击射程 + 1 米」（怪物 2.2 米）—— 那条线整条落在楼里，
    /// 单位走到墙脚仍判「还没到」，于是<b>一局 6000 步七座楼全是满血</b>（实测）。
    /// 扣掉半径之后停手线落在墙外 2.2 米处，与移动层的路径点（墙脚）接得上。</para>
    /// </summary>
    private float DistanceToWall(Unit unit, Building building)
    {
        var gap = Vector2.Distance(unit.Position, building.Position) - (float)RadiusOf(building);
        return gap > 0f ? gap : 0f;
    }

    /// <summary>
    /// 与建筑交战：射程外逼近，射程内冷却好了就拆。
    /// 怪物袭城与英雄攻坚共用（同一段物理语义）。
    ///
    /// <para>逼近走的是<b>「追实体」那一档</b>：目标是这座楼朝自己这一侧的门口点
    /// （<see cref="InfinityLordNavGrid.DoorPoint"/>）。楼心不能当路径点 ——
    /// 它被这座楼自己的占地挡住，求解永远到不了位。</para>
    ///
    /// <para><b>扣血改由技能层结算</b>（Q12 后半，与 <see cref="Engage"/> 同一条口径）：
    /// 这一侧只回答「够不够得着、这一步要不要出手」，激活检查（冷却 / 射程 / tag）与那一笔伤害的算法
    /// 都在层里。写回建造模块的那一发由 <see cref="InfinityLordAbilities.StrikeBuilding"/> 递回来
    /// （层不认识建造模块，只认识「把这一笔写回血的那一侧」这个动作）。
    /// <b>两把冷却尺仍同起点</b>：世界这一侧照旧按 <c>CooldownRemaining</c> 判「这一步要不要出手」，
    /// 层里那条冷却写的是卡上那个数（<see cref="InfinityLordSkillTable.CooldownTicksOf"/>），
    /// 于是攻坚与袭城的节奏与层进册之前逐字相同。</para>
    /// </summary>
    private void EngageBuilding(Unit unit, Building building)
    {
        var gap = DistanceToWall(unit, building);
        if (gap > unit.AttackRange)
        {
            _nav.ChaseBuilding(unit, building);
            return;
        }
        if (unit.CooldownRemaining == 0
            && _abilities.StrikeBuilding(unit, building, gap))
        {
            unit.CooldownRemaining = unit.AttackCooldownTicks;
            var damage = (int)Math.Round(_abilities.LastDamage, MidpointRounding.AwayFromZero);
            var destroyed = building.Health <= 0;
            CombatEvent?.Invoke(new CombatEventReport(unit.Id, building.Id, damage, destroyed, TargetIsUnit: false));
        }
    }

    /// <summary>
    /// 与目标交战：射程外追击，射程内冷却好了就打。
    ///
    /// <para>追击走的是<b>「追实体」那一档</b>：目标坐标每步由这一侧重读后递进层里
    /// （层没有单位表，不缓存也不外推）。</para>
    ///
    /// <para><b>扣血改由技能层结算</b>（Q12）：这一侧只回答「够不够得着、这一步要不要出手」，
    /// 激活检查（冷却 / 射程 / tag）与那一笔伤害的算法都在层里（「决策落纯 C#」同款口径：
    /// 规则只有一份，调用点不另算一遍）。层拒了就不出手，理由读
    /// <see cref="InfinityLordAbilities.LastRejectReason"/> 与层的事件流。</para>
    /// </summary>
    private void Engage(Unit unit, Unit target)
    {
        var distance = Vector2.Distance(unit.Position, target.Position);
        if (distance > unit.AttackRange)
        {
            _nav.ChaseUnit(unit, target);
            return;
        }
        if (unit.CooldownRemaining == 0)
        {
            if (!_abilities.Strike(unit, target, distance)) return;
            var damage = (int)Math.Round(_abilities.LastDamage, MidpointRounding.AwayFromZero);
            unit.CooldownRemaining = unit.AttackCooldownTicks;
            var died = target.Health <= 0;
            // 切片4：英雄击杀得固定 XP（升级经验双通道之二：悬赏声望 + 实战战功）
            // 切片6：击杀战利品 — 怪物尸体掉落虚空黄金，直接进英雄钱包（战斗本能自带收入）
            if (died && unit.Side == Faction.Hero)
            {
                unit.Xp += KillXp;
                unit.Gold += KillLootGold;
                EconomyEvent?.Invoke(new EconomyEventReport(unit.Id, EconomyEventKind.KillLoot, KillLootGold));
                CheckLevelUp(unit);
            }
            CombatEvent?.Invoke(new CombatEventReport(unit.Id, target.Id, damage, died, TargetIsUnit: true));
        }
    }

    /// <summary>视野内最近敌对单位；没有则 null。</summary>
    private Unit? FindNearestEnemy(Unit unit)
        => _units.Where(u => u.Side != unit.Side && u.Health > 0 && IsInsideField(u.Position))
            .OrderBy(u => Vector2.DistanceSquared(unit.Position, u.Position))
            .FirstOrDefault(u => Vector2.Distance(unit.Position, u.Position) <= unit.SightRange);

    /// <summary>
    /// 世界坐标在不在战场网格里（含边界）。
    ///
    /// <para><b>为什么发令之前先问这一格</b>：走位只有寻路层一个入口，它解不出界外目标的路线，
    /// 界外目标一律得到「地形无路」。而世界这一侧原先每步照旧发同一个令 ⇒ 那一名永远卡在那儿、
    /// 追击队列整段废掉。实测（大考全动词那一版）：战士公会被建在界外 (-50,-50)（营建候选点写在
    /// (-50,-50) 这种界外坐标上），出来的 19 名英雄生成在界外 (-35,-25) 再也走不回来，
    /// 连带 140 只追它们的怪报「地形无路/212」——279 个在场单位里 153 个卡住。</para>
    /// </summary>
    private static bool IsInsideField(Vector2 p)
        => p.X >= (float)InfinityLordNavGrid.MinX && p.X <= (float)InfinityLordNavGrid.MaxX
            && p.Y >= (float)InfinityLordNavGrid.MinZ && p.Y <= (float)InfinityLordNavGrid.MaxZ;

    /// <summary>
    /// 一座楼的出场点：楼心朝<b>这个方向</b>那一侧、站得下的那一点（世界坐标）。
    ///
    /// <para>方向由世界的带种子随机数给出（同一颗种子跑两次落点逐字相同 —— 出生散布这条观感保留），
    /// 但落点<b>由移动层挑一个站得下的</b>：建筑占地是阻挡格，照搬「楼心 + 半径 3 的偏移」
    /// 会把单位生在墙里，它一出生就报地形无路、一辈子不动。</para>
    ///
    /// <para><b>瞄准点先收进战场</b>：营建口不校战场边界，楼可以建在界外（实测战士公会落在 (-50,-50)）。
    /// 照楼心散出去，单位就生成在界外，再也走不回来 —— 所以先把瞄准点收进网格，再让移动层挑落脚点。</para>
    /// </summary>
    private Vector2 SpawnPointOf(Building building)
        => _nav.SpawnPoint(building, ClampIntoField(building.Position + RandomOffset(8f), margin: 2f));

    /// <summary>
    /// 一座楼的占地半径（米）—— <b>蓝图数据</b>（模块册那一份，D12/D33），
    /// 本类不另抄一个 6 米。移动层读它算门口点，光栅化读它落阻挡，两处同一个数。
    /// </summary>
    private double RadiusOf(Building building)
        => _construction.Blueprints[building.Kind.ToString()].Footprint is RadiusFootprint circle
            ? circle.Radius
            : 0.0;

    /// <summary>这一步开始时还在场上的单位（走位那一段的名单）。</summary>
    private IReadOnlyList<Unit> AliveUnits()
    {
        var alive = new List<Unit>(_units.Count);
        foreach (var unit in _units)
        {
            if (unit.Health > 0) alive.Add(unit);
        }

        return alive;
    }

    /// <summary>全局总线上那条暂停消息的落点。退订在构造里登记的那张表。</summary>
    private void OnGamePaused(GamePaused e) => SetPaused(e.IsPaused);

    /// <summary>
    /// 推进世界 n 步 — 表现层每帧调用 Advance(1) 即为实时模式；
    /// 测试中可直接 Advance(n) 快进时间轴。
    ///
    /// <para><b>每一步的四件事由装配按固定次序做</b>：先落地形，再推这一步的逻辑，
    /// 再推建造进度，最后是本类那一句排事件（次序是契约，见 <c>NavTerrainLink.Pump</c>）。
    /// 反过来的话「这一发砌的墙」要到下一步才生效，绕障的读数与建造那一步对不上。
    /// 一次 <c>Advance(n)</c> 里就按这一步一次地排 —— 与调 n 次 <c>Advance(1)</c> 逐字等价。
    /// 暂停中什么都不做（连地形都不读）。</para>
    ///
    /// <para><b>建造模块的推进排在时钟之后</b>（D17 的时序契约）：这一步里到头的建造过程发的
    /// 事件带的是这一步的步号（D27），而暂停空转时（<c>GameClock.Advance</c> 回 false）模块也不推 ——
    /// 时间冻结对两边是同一件事。本轮全部蓝图工期 0，所以这一步实际不发任何事件，
    /// 接线是为「工期不为 0 的那一天」准备的。</para>
    /// </summary>
    public override bool Advance(int steps = 1) => Assembly.Advance(steps, DrainConstructionEvents);

    /// <summary>只读单位快照（表现层渲染用）。</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>只读建筑快照（表现层渲染用）。</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>
    /// 公共建造模块的只读读数口（表现层与测试经它读册内那几座的行、快照段与只读查询 ——
    /// 「桥只转发」：调试桥只转发既有读数，不在桥那一层另写一份实现）。
    /// <b>写路径不走这里</b>：营建落建走 <see cref="TryPlaceBuilding"/> 这个入口。
    /// </summary>
    public ConstructionCore Construction => _construction;

    /// <summary>
    /// 走位那一半的读数口（调试与测试经它读移动解、地形接线与网格 ——
    /// 「状态可查」：推完一步读到的就是那一步的结果）。
    /// </summary>
    public InfinityLordNavMove Nav => _nav;

    /// <summary>
    /// 技能层那一侧的读数口（调试、桥的动词与测试经它读技能栏、激活检查、事件流与快照 ——
    /// 「桥只转发」：写入只走层自己那几个入口）。
    /// </summary>
    public InfinityLordAbilities Abilities => _abilities;

    /// <summary>
    /// 把一个在场单位原样放到一个绝对落点 —— <b>测试排布专用</b>（表现层与 eval 禁用）。
    ///
    /// <para>位置真值住在移动解里，测试直接写单位那一列只在「这一步还没走位」的窗口里有效 ——
    /// 走位一落定镜像就被写回旧点，症状是「明明挪过去了，下一步它又回到原处」。
    /// 走这一口（落位移那一口）才是把排布交给唯一那份真值。</para>
    ///
    /// <para><b>镜像那一笔也照层里的值抄一遍</b>：层的镜像同步排在每一步末尾，
    /// 而这一步之内还有决策链在读 <c>unit.Position</c>（射程判定、找最近敌人）——
    /// 不抄的话排布完的那一步它按旧坐标做决定。实测拿掉这一笔（跑 AbilityLayerTests 那三条类）：
    /// 普攻走层 / 拒绝理由 / 激活检查拒了不动 / 出手的步差四条红，红在「贴脸摆好了却没出手」。
    /// 抄的是层刚落下的那个点，不是第二份真值。</para>
    /// </summary>
    /// <returns>放下了回 true；那一名不在场或没登记过回 false（不静默挪一个不存在的单位）。</returns>
    internal bool PlaceUnitForTest(string unitId, Vector2 at)
    {
        if (!_nav.HasAgent(unitId) || _units.All(u => u.Id != unitId || u.Health <= 0))
        {
            return false;
        }

        _nav.PlaceAt(unitId, at);
        var unit = _units.First(u => u.Id == unitId);
        SyncMirrorOf(unit);

        // 命令改成「原地不动」：落位移那一口**不清命令**（它的口径是「落位置与换命令是两件事」），
        // 而测试排布要的是「把它放到这儿站着」—— 留着上一条命令的话，它会被那条旧命令带回去，
        // 症状是「明明挪过去了，下一步它又走开了」。
        _nav.HoldInPlace(unit);
        return true;
    }

    /// <summary>
    /// 把一名单位那一列位置照层里那一份抄一遍（镜像同步的单名版）—— <b>只给排布那一口用</b>。
    ///
    /// <para>抄的是 <see cref="InfinityLordNavMove.SolutionOf"/> 交回的落点，不是调用方手上那个参数：
    /// 真值只有层那一份，这一笔是把它读回来，不是另写一个值。</para>
    /// </summary>
    private void SyncMirrorOf(Unit unit)
    {
        var (x, z) = InfinityLordNavGrid.ToWorld(_nav.SolutionOf(unit.Id).Position);
        unit.Position = new Vector2((float)x, (float)z);
    }

    // === 行为树接管口 ===
    //
    // 决策在树上（组合节点是 GDScript、任务节点是 C#；早期那份 GDScript 叶子版只当对照用，2026-09-25 已删），
    // 执行留在这里：树每推进一步（0.1 秒）写回一个意图码，StepHero 对这名英雄跳过自己的 Majesty 决策链、只执行意图。
    // LogicWorld 因此保持纯 C# —— 同一套意图能被 dotnet test 直接喂，不需要场景树。

    /// <summary>被接管的英雄 Id → 上一步树写入的意图。遍历仍按 _units 顺序，字典只做查询，不引入顺序依赖。</summary>
    private readonly Dictionary<string, (BrainIntent Intent, string? TargetId)> _brainIntents = new();

    /// <summary>把这名英雄交给行为树。此后他的 Majesty 决策链整个让位。</summary>
    public void DelegateHeroToBrain(string heroId)
    {
        _brainIntents[heroId] = (BrainIntent.None, null);
    }

    /// <summary>交还英雄（阵亡/撤下树时用）—— 没有这一条，Id 会永远占着接管表。</summary>
    public void ReleaseHeroFromBrain(string heroId) => _brainIntents.Remove(heroId);

    /// <summary>树每步写入意图（表现层与 GDScript/C# 任务共用这一个口；targetId 空串表示无目标）。</summary>
    public void ReportBrainIntent(string heroId, int intent, string targetId)
    {
        if (!_brainIntents.ContainsKey(heroId)) return;
        _brainIntents[heroId] = ((BrainIntent)intent, string.IsNullOrEmpty(targetId) ? null : targetId);
    }

    /// <summary>当前意图码；没被接管返回 -1（快照与观测口用）。</summary>
    public int BrainIntentOf(string heroId)
        => _brainIntents.TryGetValue(heroId, out var entry) ? (int)entry.Intent : -1;

    /// <summary>
    /// 接管表里现在有谁 —— 按 <c>Id</c> 序数排（不按字典迭代顺序，那个顺序不作保证）。
    /// 读它的是录制端：开录那一刻已经在树上的英雄，它的接管这一笔要补进 Journal 开头，
    /// 否则重放端从第 0 步重建的世界里没有树、也没有这笔接管（→ 「写路径进 Journal」）。
    /// </summary>
    public IReadOnlyList<string> BrainTakenHeroIds
        => _brainIntents.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();

    /// <summary>按 Id 取存活单位（接管侧观测用）；找不到或已阵亡返回 null。</summary>
    public Unit? FindAliveUnit(string unitId) => _units.FirstOrDefault(u => u.Id == unitId && u.Health > 0);

    /// <summary>
    /// 行为树驱动口 —— 「逻辑帧时间又推进了一步」，每步在<b>决策那一段的开头</b>发一次
    /// （落场与技能层推进之后、逐单位决策之前）。
    ///
    /// <para><b>为什么排在决策之前</b>：树也会发令。排在决策之前，树这一步写的意图紧接着被
    /// 同一步的决策链读走 —— 树发的令也在同一步生效；排在决策之后或步骤末尾，那一发令要等
    /// 下一步的决策段才被读走。树读到的读数与决策链读到的是同一份状态：上一步走位落定之后的位置。</para>
    ///
    /// <para>四个 <c>Advance</c> 调用点（帧循环、StepDebug、AdvanceDebug、大考快进）都经同一次
    /// 脉搏，所以树不会只在哪条路上才动。它<b>不</b>兼作「这一步演化完了」的口 ——
    /// 那一个口径是 <see cref="Stepped"/>。</para>
    /// </summary>
    public event Action<long>? StepStarted;

    /// <summary>
    /// 一步演化完成 —— 「这一步的世界演化已经落定」的唯一对外口径。
    /// 在 <see cref="StepAfterMove"/> 末尾发：走位落定、镜像写好、阵亡清理与结算都跑完。
    ///
    /// <para>表现层与确定性测试挂它做逐步读数（四个 <c>Advance</c> 调用点都经它，
    /// 见 <see cref="StepStarted"/>）。<b>它不驱动行为树</b>：树发的令要对同一步生效，
    /// 得排在决策之前，那是 <see cref="StepStarted"/> 的活。</para>
    /// </summary>
    public event Action<long>? Stepped;

    /// <summary>接管侧观测口：视野内最近敌方单位与其距离（与 FindNearestEnemy 同一条口径，不另起一份判断）。没有敌人时 Id 为 null、距离 -1。</summary>
    public (string? Id, float Distance) SenseNearestEnemy(string heroId)
    {
        var hero = FindAliveUnit(heroId);
        if (hero is null) return (null, -1f);
        var enemy = FindNearestEnemy(hero);
        return enemy is null
            ? (null, -1f)
            : (enemy.Id, Vector2.Distance(hero.Position, enemy.Position));
    }

    /// <summary>接管侧观测口：两名单位之间的距离；任一名不在场返回 -1。</summary>
    public float SenseDistance(string fromId, string toId)
    {
        var a = FindAliveUnit(fromId);
        var b = FindAliveUnit(toId);
        return a is null || b is null ? -1f : Vector2.Distance(a.Position, b.Position);
    }

    /// <summary>
    /// 执行树上一步的意图。目标已死或树没给意图时**宁可原地发呆也不替它做决定** —— 对照组要的是「树说什么做什么」。
    ///
    /// <para><b>「发呆」要显式发一条原地不动</b>：位置真值住在移动解里，不发令的话它照旧执行
    /// <b>手上那条命令</b>（上一步的「走到点」或者游荡），于是「树没给意图」这一步它照样在走 ——
    /// 读数是「它动了，而树什么都没说」。发一条驻守点 = 此刻脚下这一点的原地不动，
    /// 才算真正「发呆」。</para>
    /// </summary>
    private void ApplyBrainIntent(Unit hero, (BrainIntent Intent, string? TargetId) brain)
    {
        var target = brain.TargetId is null
            ? null
            : _units.FirstOrDefault(u => u.Id == brain.TargetId && u.Health > 0);
        switch (brain.Intent)
        {
            case BrainIntent.Engage when target is not null:
                hero.TargetUnitId = target.Id;
                Engage(hero, target);
                return;
            case BrainIntent.Flee when target is not null:
                FleeFrom(hero, target);
                return;
            case BrainIntent.Wander:
                Roam(hero);
                return;
            default:
                _nav.HoldInPlace(hero);
                return;
        }
    }

    /// <summary>
    /// 背向目标走一步（0.1 秒）—— 单位上行为树那条线的逃跑原语。
    ///
    /// <para><b>这一侧只回答「背对谁」</b>：落点怎么算、落在哪一格、发哪条命令，全在
    /// <see cref="InfinityLordNavMove.StepAwayFrom"/> 里。位置真值住在寻路流水线里，
    /// 这一侧直写 <c>unit.Position</c> 会被这一步末尾的镜像同步抹掉 ——
    /// 读数看着像挪了而下一步它还在原处，症状是「该跑的时候站着不动」。</para>
    /// </summary>
    private void FleeFrom(Unit unit, Unit from) => _nav.StepAwayFrom(unit, from.Position);

    /// <summary>
    /// 调试直达：对存活建筑扣血并走完整摧毁收口（<see cref="DamageBuilding"/> 单一写回口：
    /// 移除 + BuildingDestroyed + 巢穴重生调度）— MCP eval 实验用，与战斗流程同语义。
    /// 完成标准：返回 true 表示扣血已执行（目标存在且存活）。
    ///
    /// <para><b>这一发走技能层同源</b>（「桥只转发」，与 <see cref="DebugDamageUnit"/> 同一口径）：
    /// 扣血那笔算术由层做，这一发只把层算出来的那一笔写回建造模块 ——
    /// 原型不再自己写一遍 <c>Damage</c>，两处各写一遍就是两套真值。</para>
    /// </summary>
    public bool DebugDamageBuilding(string buildingId, int damage)
    {
        var building = _buildings.FirstOrDefault(b => b.Id == buildingId && b.Health > 0);
        if (building is null || damage <= 0) return false;
        return _abilities.DebugHitBuilding(building, damage);
    }

    /// <summary>
    /// 调试直达：给存活单位扣血（阵亡清理仍走 Step 里那一遍收口，与战斗同语义）。
    /// 用途：行为树对照组的实验剧本要把血线打到低阈值以下，才能录到「逃跑」那一态 ——
    /// 挂在这里而不是挂表现层，是为了让 GDScript 版与 C# 版共用**同一个**实验条件。
    /// 完成标准：返回 true 表示扣血已执行（目标存在且存活）。
    ///
    /// <para><b>这一发走技能层同源</b>（「桥只转发」）：扣血那笔算术由层做
    /// （<see cref="InfinityLordAbilities.DebugHit"/> 加一条本步生效、下一步就撤的修饰符），
    /// 原型不再自己写一遍 <c>Health -=</c> —— 两处各写一遍就是两套真值。</para>
    /// </summary>
    public bool DebugDamageUnit(string unitId, int damage)
    {
        var unit = FindAliveUnit(unitId);
        if (unit is null || damage <= 0) return false;
        return _abilities.DebugHit(unit, damage);
    }

    /// <summary>
    /// 调试直达：给存活建筑回血（不走任何摧毁/终局判定 — 纯观测辅助）。
    /// 用途：MCP 实测终幕表现时把宫殿堡垒化，否则放任世界撑不到 BOSS 降临
    /// （与确定性测试的 FortifyPalace 排布同款）。
    /// 完成标准：返回 true 表示目标存在且已回血。
    ///
    /// <para><b>只一条路径</b>：本原型的血量账归模块（D2），场地建筑 2026-10-02 起也进册，
    /// 于是「册内走 Repair、册外直接 <c>+=</c>」那两条分支合成一条 ——
    /// 全部走 <c>SetHealth(id, 当前 + amount)</c>。修复量是<b>增量</b>（这一发的语义是「回这么多」，
    /// 终局脚本的 <c>HealBuilding palace|200000</c> 要的就是 8000 + 200000 = 208000），
    /// 而<b>上限一并被抬</b>：走 Repair 的话它钳在 8000，那次「回血」会把 208000 反向扣回 8000 ——
    /// 堡垒化要的正是抬过上限。</para>
    /// </summary>
    public bool DebugHealBuilding(string buildingId, int amount)
    {
        var building = _buildings.FirstOrDefault(b => b.Id == buildingId && b.Health > 0);
        if (building is null || amount <= 0) return false;

        var result = _construction.SetHealth(building.Id, building.Health + amount);
        DrainConstructionEvents();
        return result.Ok;
    }

    // === 测试排布接口（internal — source-linked 测试同程序集可达；表现层与 eval 禁用） ===

    /// <summary>确定性测试排布：直接设定金库（破产/冻结场景的排布捷径）。</summary>
    internal void DebugSetTreasury(int value) => Treasury = value;

    /// <summary>
    /// 重放检查点快照（状态级全量单行）— 唯一一份口径，录制端（测试/MCP 试玩）与
    /// 重放端共用；改字段时两端同时生效，不可能漂移。浮点 "R" 全精度防往返漂移，
    /// 集合按 Id 序数排序 — 不依赖任何遍历顺序（确定性红线）。
    /// </summary>
    public string ReplaySnapshot()
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();

        // 建筑段：模块段（册内那几座：id / 蓝图 / 坐标 / 血量 / 状态 / 在建剩余步 / 暂停 / 方向）
        // + 原型自己那两列（Owner / lv，D31/D32）。模块的摘要行折进头部段 —— 头部段是快照的
        // 第 0 段，多加一段会让 L2 语义档的「实体段数」判据漂移（「断言三层」）。
        var (coreSummary, coreRows) = CoreSegment();
        sb.Append(CultureInfo.InvariantCulture,
            $"tick={Tick} treasury={Treasury} net={LastCycleNet} outcome={Outcome} {coreSummary}");
        foreach (var b in _buildings.OrderBy(b => b.Id, StringComparer.Ordinal))
            sb.Append(" | ").Append(BuildingSegment(b, coreRows));
        foreach (var u in _units.OrderBy(u => u.Id, StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture,
                $" | U {u.Id} \"{u.Name}\" {u.Side} {u.Class} home={u.HomeGuildId ?? "-"}" +
                $" hp={u.Health}/{u.MaxHealth} atk={u.Attack} cd={u.CooldownRemaining}" +
                $" pos=({F(u.Position.X)},{F(u.Position.Y)})" +
                $" gold={u.Gold} xp={u.Xp} lv={u.Level} gear={u.Gear} morale={u.Morale}" +
                $" noIncome={u.NoIncomeTicks} tavernStarve={u.TavernStarveTicks}" +
                $" target={u.TargetUnitId ?? "-"} claim={u.ClaimedFlagId ?? "-"} lastGold={u.LastObservedGold}");
        foreach (var f in _flags.OrderBy(f => f.Id, StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture,
                $" | F {f.Id} -> {f.TargetId} gold={f.Gold} claim={f.ClaimedByHeroId ?? "-"} settled={f.Settled}");

        // 走位那一段（层里那一份格式化，本类只把它的换行折成 token，一个数都不重算）：
        // 「这一步它到底在走哪条命令、走到哪了」是移动换实现之后最该读得出来的一列。
        sb.Append(" | ").Append(NavSegment());

        // 技能层那一段（同上：层自己那一份格式化整段搬进来，一个数都不重算）：
        // 「这一下为什么是这个结果」在层里是一条账 —— 技能栏、修饰符、效果、冷却、计数全在里面。
        sb.Append(" | ").Append(AbilitySegment());
        return sb.ToString();
    }

    /// <summary>
    /// 技能层那一段在单行快照里的形态 —— 与走位那一段同一种搬法：
    /// 层给的是一段多行文本，按「 | 」分段所以折成一行（token 原样搬，不重算任何值）。
    /// </summary>
    private string AbilitySegment()
        => string.Join(" ", _abilities.Snapshot()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    /// <summary>
    /// 移动解那一段在单行快照里的形态 —— 层给的是一行摘要 + 一行一单位，
    /// 单行快照按「 | 」分段，所以折成一行（token 原样搬，不重算任何值）。
    /// </summary>
    private string NavSegment()
        => string.Join(" ", _nav.Stepper
            .ReplaySnapshotSegment()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    /// <summary>
    /// 模块快照段的两个出口（格式化由模块那一处生成，D32；本类只搬 token，不重算任何值）：
    /// ① 摘要行 —— 折进快照的头部段；② 每建筑一行 —— 按建筑 id 索引，缺行就是「不在册内」。
    ///
    /// <para>模块段是<b>多行</b>文本（一行摘要 + 一行一建筑），而本原型的检查点快照是<b>单行</b>
    /// 以「 | 」分段，两者只能按 token 折行：这里做的事是「把模块那一行的 token 原样搬进一个段」，
    /// 一个数都不重算（D29）。</para>
    /// </summary>
    private (string Summary, Dictionary<string, string[]> Rows) CoreSegment()
    {
        var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var summary = string.Empty;
        var lines = _construction.ReplaySnapshotSegment().Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            var tokens = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (i == 0)
            {
                summary = string.Join(" ", tokens);
                continue;
            }

            rows[tokens[0]] = tokens; // 行的第一个 token 就是建筑 id（模块的列序：id、蓝图、…）
        }

        return (summary, rows);
    }

    /// <summary>
    /// 一座建筑在快照里的那一段 —— 段首两个 token 恒为 <c>B + 建筑 id</c>（L2 语义档的身份判据，
    /// 「断言三层」），后面先是模块那一行的 token 逐字搬过来（蓝图 / 坐标 / 血量 / 状态 / 在建剩余步 /
    /// 暂停 / 方向），再补原型自己那两列（Owner / lv）。
    ///
    /// <para><b>只有这一种形态</b>（2026-10-02）：场地建筑进册之前，这里还有一个「不在册内就本类自己印」
    /// 的第二形态。现在本类的建筑句柄<b>全部</b>接在模块册内那一行上（玩家营建走
    /// <c>Placed</c>、场地建筑走 <c>Registered</c>，两个 drain 分支都先 <c>AttachCore</c>），
    /// 那个形态已经是走不到的分支 —— 留着它就是把一份判不到的真值印在快照里。
    /// 一个数都不在这里重算（D29）。</para>
    /// </summary>
    private static string BuildingSegment(Building b, Dictionary<string, string[]> coreRows)
        => coreRows.TryGetValue(b.Id, out var tokens)
            ? $"B {tokens[0]} {tokens[1]} {b.Owner} lv={b.Level.ToString(CultureInfo.InvariantCulture)}"
                + (tokens.Length > 2 ? " " + string.Join(" ", tokens[2..]) : string.Empty)
            // 走不到的一行：句柄不在册内时没有模块那一行可搬。与其印一份原型自己算的假状态，
            // 不如当场说出「模块册与句柄表分家了」—— 那种错本来就不该被快照悄悄圆过去。
            : throw new InvalidOperationException(
                $"{b.Id} 在句柄表里却不在模块册内：模块册与句柄表分家了，去查 AddFixture / OnCoreRegistered");

    /// <summary>
    /// 全量状态快照（建筑 / 英雄）— 可观测性公开 API（复盘提案 1/2 的落点）。
    ///
    /// 设计动机（2026-09-20 切片 7 验证复盘）：行为型切片验证需要「状态快照」，
    /// 事件日志只能看到结果尾巴。此方法一发返回所有英雄的需求状态与当前动机，
    /// 表现层 HUD 直读、eval 直达调用，替代「扫场景树 47 个 Label3D」的昂贵验证。
    /// 需求判定与决策链同源（MoraleDrinkThreshold / PotionSeekRatio），显示即真相。
    /// 建筑行是迁到建造模块之后补上的（D32：模块段 + 原型段嵌进同一个快照）。
    /// </summary>
    public string DebugSnapshot()
    {
        var (coreSummary, coreRows) = CoreSegment();
        var lines = new List<string> { $"tick={Tick} treasury={Treasury} {coreSummary}" };

        // 走位那一段（层里那一份格式化，与重放快照读同一份）：多行形态在调试快照里原样给。
        lines.Add(NavSegment());

        // 建筑行与重放快照同一份拼法（同一组 helper，两个快照不各拼一次）。
        foreach (var b in _buildings)
        {
            lines.Add(BuildingSegment(b, coreRows));
        }

        foreach (var mob in _units.Where(u => u.Side == Faction.Monster && u.Health > 0))
        {
            lines.Add($"{mob.Id} \"{mob.Name}\" hp={mob.Health}/{mob.MaxHealth} pos=({mob.Position.X:F0},{mob.Position.Y:F0})");
        }
        foreach (var hero in _units.Where(u => u.Side == Faction.Hero && u.Health > 0))
        {
            // 动机推导：与 StepHero 决策链同源（悬赏→战斗→伤势→士气→购物→漫步）
            string intent;
            if (hero.ClaimedFlagId is not null) intent = "追击悬赏";
            else if (hero.TargetUnitId is not null) intent = "交战中";
            else if (hero.Health < hero.MaxHealth * PotionSeekRatio && hero.Gold >= PotionPrice) intent = "去药店";
            else if (hero.Morale < MoraleDrinkThreshold && hero.Gold >= DrinkPrice) intent = "去酒馆";
            else if (hero.Gold >= 30) intent = "去市场";
            else intent = "漫步";

            // 被行为树接管的英雄：意图不再来自上面那条链，改读树写入的意图码（显示即真相）
            var brainCode = BrainIntentOf(hero.Id);
            if (brainCode >= 0)
            {
                intent = brainCode switch
                {
                    (int)BrainIntent.Engage => "树:交战",
                    (int)BrainIntent.Flee => "树:逃跑",
                    (int)BrainIntent.Hold => "树:站着",
                    (int)BrainIntent.Wander => "树:游荡",
                    _ => "树:未决策",
                };
            }
            lines.Add(
                $"{hero.Id} \"{hero.Name}\" hp={hero.Health}/{hero.MaxHealth} morale={hero.Morale} " +
                $"gold={hero.Gold} gear={hero.Gear} lv={hero.Level} pos=({hero.Position.X:F0},{hero.Position.Y:F0}) " +
                $"want_drink={(hero.Morale < MoraleDrinkThreshold ? 1 : 0)} claim={(hero.ClaimedFlagId ?? "-")} " +
                $"starve={hero.NoIncomeTicks}/{hero.TavernStarveTicks} intent={intent}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 本类没有要额外交还的东西：订阅与模块句柄都在构造里登记进基类那张退订表，
    /// 基类 <c>Dispose</c> 按表跑完（后进先出），再交还装配自己挂的那两处。
    /// </summary>
    protected override void OnDispose()
    {
    }
}
