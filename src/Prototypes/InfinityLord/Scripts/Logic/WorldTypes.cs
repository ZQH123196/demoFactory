#nullable enable
using System;
using System.Numerics;
using CoreBuilding = DemoFactory.GugaFramework.Construction.Building;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 阵营 — 玩家的英雄方与野生怪物方。
/// </summary>
public enum Faction
{
    /// <summary>玩家方英雄（行会产出，玩家间接控制）。</summary>
    Hero,

    /// <summary>野生怪物（巢穴产出，自治威胁）。</summary>
    Monster,
}

/// <summary>
/// 英雄职业 — 差异只落在四个参数（攻击/血/速度/勇气），建筑即招募（切片 9 用户确认）。
/// </summary>
public enum HeroClass
{
    /// <summary>游侠：远程风筝，基准勇气（阈值 ×1.0）。</summary>
    Ranger,

    /// <summary>战士：近战肉盾，莽（阈值 ×0.7 — 低赏硬单也接）。</summary>
    Warrior,

    /// <summary>骑士：战士公会 Lv.2 的精英线（切片 10）— 更厚更狠更莽（阈值 ×0.6）。</summary>
    Knight,
}

/// <summary>
/// 建筑种类 — 产兵建筑（行会）与威胁建筑（巢穴）+ 玩家经济建筑。
/// 切片 9：建筑即招募 — 行会按种类产对应职业英雄，全部可由玩家放置。
/// </summary>
public enum BuildingKind
{
    /// <summary>游侠公会：周期性产出游侠（远程 — 种子行会）。</summary>
    GuildHall,

    /// <summary>战士公会：周期性产出战士（近战肉盾 — 玩家营建）。</summary>
    GuildWarrior,

    /// <summary>怪物巢穴：周期性产出怪物（威胁演化源）。</summary>
    MonsterLair,

    /// <summary>市场：英雄在此消费，税收回流金库（Majesty 的金币循环核心）。</summary>
    Market,

    /// <summary>酒馆：英雄在此喝酒恢复士气（切片7 需求层级：士气）。</summary>
    Tavern,

    /// <summary>药店：英雄在此购买药剂回血（切片7 需求层级：伤势）。</summary>
    Apothecary,

    /// <summary>宫殿：玩家方的失败条件锚点（种子实体，不可营建、不可挂赏）——被毁即判负（切片 10）。</summary>
    Palace,

    /// <summary>BOSS 巢穴：终幕降临的巨型威胁源，持续刷护卫；摧毁即判胜（切片 10）。</summary>
    BossLair,
}

/// <summary>
/// 终局判定 — 一局游戏的三种状态（切片 10「终局」，生死线大考的操作定义）。
/// 非 Running 即已判定：胜负不可再被改写（闩锁语义）。
///
/// <para><b>这一档只管记账，不管时间轴</b>：判负之后世界演不演化由宿主答
/// （局结束统一冻住，真值在基类那一处推进口）。判定照旧照跑 ——
/// 拦不住的是「下一步还推不推」，不是这一步里判下的胜负。</para>
/// </summary>
public enum GameOutcome
{
    /// <summary>进行中。</summary>
    Running,

    /// <summary>胜利 — BOSS 巢穴被摧毁（玩家侧全动词策略考卷及格）。</summary>
    Victory,

    /// <summary>失败 — 宫殿被摧毁（威胁曲线压穿了玩家的经营）。</summary>
    Defeat,
}

/// <summary>
/// 行为树意图 — 树（GDScript 或 C# 任务）每步写入、逻辑层在同一步的决策段执行的动词表。
///
/// 用 int 码而非对象：跨 GDScript/C# 边界只传标量最稳（写法与实测见 docs/GugaFramework/行为树与跨语言共存.md）。
/// 决策在树上、执行留在逻辑层，这样 LogicWorld 仍是纯 C#、能被 dotnet test 复现。
/// </summary>
public enum BrainIntent
{
    /// <summary>还没决策（树刚接管、或这一步没跑出来）。</summary>
    None = 0,

    /// <summary>与指定单位交战：射程外接近，射程内冷却好了就打。</summary>
    Engage = 1,

    /// <summary>脱离指定单位：每步背向挪一步。</summary>
    Flee = 2,

    /// <summary>站着不动。</summary>
    Hold = 3,

    /// <summary>
    /// 随机游荡：挑个附近的随机点走过去，到点再挑一个。与未被接管英雄决策链第 5 步的
    /// 「漫步」走同一条原语（<c>LogicWorld.Roam</c>），树只负责说「现在该闲逛」。
    /// 方向由世界自己的带种子随机数给出 —— 同一颗种子跑两次轨迹逐步相同。
    /// </summary>
    Wander = 4,
}

/// <summary>
/// 波次剧本行 — 威胁曲线的一个时间点（切片 10：剧本波次表参数驱动）。
/// 纯数据，静态表在 LogicWorld.WaveTable；确定性测试可逐行断言。
/// </summary>
/// <param name="No">波号（1 起，播报与断言用）。</param>
/// <param name="AtTick">落场时间点（内部步数；对玩家表述一律换算成秒/分钟）。</param>
/// <param name="Name">怪物名（每波换一种 — 加压可读）。</param>
public sealed record WaveRow(int No, int AtTick, string Name, int Health, int Attack, float Speed, int Count);

/// <summary>
/// 世界中的一座建筑 — 纯数据。
///
/// <para><b>迁到公共建造模块之后，一个类型两种形态</b>，靠「在不在模块册内」分家：
/// 玩家营建的那几座，坐标与血量的真值在模块那一行（D2、D9），这里的 <see cref="Position"/> /
/// <see cref="Health"/> 只做转发 —— 同一件事在两处各存一份，改一处忘了另一处就是那种查不出来的
/// bug（显示即真相、D29）。种子与终幕那几座（宫殿 / 行会 / 服务建筑 / 巢穴 / BOSS 巢穴）
/// <b>不在模块册内</b>：种子布局自身的间距（宫殿↔行会 8、坊市↔药店 11.3）小于营建规则的 12，
/// <c>Commit</c> 当场判 <c>TooClose</c>，而模块没有「登记一座不校验的场地建筑」那个口
/// （理由与实测数字记在 <c>LogicWorld</c> 的类注释与本 ticket 的 Comments）。</para>
///
/// <para>不透明载荷是原型自己的那几列：<see cref="Owner"/> / <see cref="Level"/> /
/// <see cref="SpawnIntervalTicks"/>（D31：模块存而不读、原样归还）。</para>
/// </summary>
public sealed class Building
{
    public required string Id { get; init; }
    public BuildingKind Kind { get; init; }
    public Faction Owner { get; init; }

    /// <summary>产出周期（步）。</summary>
    public int SpawnIntervalTicks { get; init; }

    /// <summary>行会等级（切片 10 纵向成长：Lv.1 基础职业 → Lv.2 精英职业；非行会恒为 1）。</summary>
    public int Level { get; set; } = 1;

    /// <summary>不在模块册内时自己那份坐标；册内那几座读模块（量化后的落点才是真值）。</summary>
    private Vector2 _position;

    /// <summary>不在模块册内时自己那份血量；册内那几座读模块。</summary>
    private int _health;

    /// <summary>模块册内那一行。<b>2026-10-02 起每个活着的句柄都有它</b>（场地建筑也走
    /// <c>RegisterFixture</c> 进册），所以这一格不再对外暴露「在不在册内」的判据 ——
    /// 那个判据曾被三处分流用（扣血两条分支、回血两条分支、种子间距那一段），三处都随场地建筑
    /// 进册收成一处，留着它就只剩一个恒真的读数。</summary>
    private CoreBuilding? _row;

    /// <summary>世界坐标（模块册内那几座 = 量化后的落点，D9：只存一份真值）。</summary>
    public Vector2 Position
    {
        get => _row?.Position ?? _position;
        init => _position = value;
    }

    /// <summary>
    /// 当前生命。血量归模块写（D2），这一列只是转发。
    /// 直接赋值<b>只对还没接上册的那一瞬间有效</b>：<see cref="AttachCore"/> 之后当场抛 ——
    /// 血量真值在模块那一行，绕开它就是在模块外面留第二份（D29）。
    /// 要改走 <c>LogicWorld.DebugHealBuilding</c> / <c>DebugDamageBuilding</c>
    /// （回血那一发走模块的绝对值写口 <c>SetHealth</c>，上限一并抬）。
    /// </summary>
    public int Health
    {
        get => _row is null ? _health : (int)_row.Health;
        set
        {
            if (_row is not null)
            {
                throw new InvalidOperationException(
                    $"{Id} 在建造模块册内：血量归模块写（D2），走 LogicWorld.DebugHealBuilding / " +
                    "DebugDamageBuilding，直接赋值就是绕过模块留第二份真值（D29）");
            }

            _health = value;
        }
    }

    /// <summary>
    /// 接上模块那一行（模块发 <c>Placed</c>（玩家营建）或 <c>Registered</c>（场地建筑）
    /// 时由世界的 drain 点调 —— 挂链与发事件是同一发里两件事）。</summary>
    internal void AttachCore(CoreBuilding row) => _row = row;

    /// <summary>
    /// 血量上限 —— 与 <see cref="Health"/> 同一条口径：册内读模块那一行（真值）。
    /// 出册之后没有第二份可抄（<see cref="DetachCore"/> 只留当前血），那时读
    /// <see cref="int.MaxValue"/>：这座楼已经不在场，「上限」这一问没有意义，
    /// 而<b>技能层不会读到它</b> —— 层那一册的上限只在册内登记与镜像时抄一次。
    ///
    /// <para>为什么要这一列：技能层那一册要 <c>hp_max</c>，而层读的是<b>血 / 上限</b>两格，
    /// 两格必须同一个来源 —— 只抄血不抄上限的话，一层快照里会出现
    /// 「这一座 200/8000」而世界说 8000/8000 这种自相矛盾的读数。</para>
    /// </summary>
    public int MaxHealth => _row is null
        ? int.MaxValue
        : (int)Math.Round(_row.MaxHealth, MidpointRounding.AwayFromZero);

    /// <summary>
    /// 出册时把最后一份读数留在本地（血量与坐标抄一份，<c>_row</c> 摘掉）——
    /// 摧毁那一发之后表现层与事件订阅者还要读这座建筑（判定「这一下打死了吗」就在那之后一步）。
    /// </summary>
    internal void DetachCore()
    {
        if (_row is not { } row)
        {
            return;
        }

        _health = (int)row.Health;
        _position = row.Position;
        _row = null;
    }
}

/// <summary>
/// 悬赏旗 — 玩家（领主）间接控制英雄的唯一手段。
///
/// 设计思路（Majesty 的 Reward Flag）：
/// - 玩家把金币挂在目标上（怪物单位或巢穴建筑），英雄自主权衡"赏金 vs 危险"决定是否承接
/// - 完成后金币支付给英雄，英雄去市场消费 → 税收回流金库（金币循环）
/// - 玩家不能直接命令任何单位，这是核心设计约束
/// </summary>
public sealed class BountyFlag
{
    public required string Id { get; init; }
    /// <summary>目标 Id（悬赏挂在谁头上：怪物单位或怪物建筑）。</summary>
    public required string TargetId { get; init; }
    /// <summary>悬赏金额（金币，玩家自定）。</summary>
    public int Gold { get; set; }
    /// <summary>是否已被某英雄承接。</summary>
    public string? ClaimedByHeroId { get; set; }
    /// <summary>是否已支付完毕（目标死亡+奖金发放）。</summary>
    public bool Settled { get; set; }
}

/// <summary>
/// 战斗单位（英雄或怪物）— 纯数据。
///
/// 设计思路：
/// - AI 状态与位置都是普通数据，由 LogicWorld 在脉搏步统一驱动
/// - 英雄的钱包：赚赏金 → 市场消费 → 税收回流（Majesty 金币循环）
/// </summary>
public sealed class Unit
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public Faction Side { get; init; }
    public Vector2 Position { get; set; }
    /// <summary>移动速度：每步位移量（世界单位/步）。</summary>
    public float Speed { get; init; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    /// <summary>每次攻击伤害（装备购买后成长）。</summary>
    public int Attack { get; set; }
    /// <summary>攻击冷却（步）。</summary>
    public int AttackCooldownTicks { get; init; }
    /// <summary>索敌半径（世界单位）。</summary>
    public float SightRange { get; init; }
    /// <summary>攻击距离（世界单位）。</summary>
    public float AttackRange { get; init; }

    // === 切片 9：英雄职业（怪物侧忽略） ===
    /// <summary>职业（出生即定 — 建筑即招募）：差异 = 攻击/血/速度/勇气四参数。</summary>
    public HeroClass Class { get; init; }
    /// <summary>产出行会 Id（招募名额记账键 — 公会只养自家存活者）。</summary>
    public string? HomeGuildId { get; init; }

    // === 英雄专属经济状态（怪物侧忽略） ===
    /// <summary>钱包余额（赏金收入；市场消费支出）。</summary>
    public int Gold { get; set; }
    /// <summary>经验值（击杀怪物积累）。</summary>
    public int Xp { get; set; }
    /// <summary>等级（Xp 达阈值自动升级，战力成长）。</summary>
    public int Level { get; set; }
    /// <summary>装备等级（市场购买；每级攻击+3、血上限+10 — 切片4「养英雄」闭环）。</summary>
    public int Gear { get; set; }

    // === 切片7：需求层级状态（士气 — 酒馆消费可恢复） ===
    /// <summary>士气 0~100，出生满值。拒绝出工时衰减；喝酒恢复（切片7「士气+伤势」）。</summary>
    public int Morale { get; set; } = 100;

    // === 需求流失状态（冒险者自雇 — 需求长期不被满足就离开） ===
    /// <summary>赏金饥饿计时：连续无收入（既无悬赏承接也无任何金币进账）的步数。</summary>
    internal int NoIncomeTicks;
    /// <summary>酒馆饥饿计时：想喝（士气低于阈值）却喝不上的连续步数（正在路上不算饿）。</summary>
    internal int TavernStarveTicks;

    // === AI 运行时状态（内部使用） ===
    /// <summary>当前移动目标；null = 站立。</summary>
    internal Vector2? MoveTarget;
    /// <summary>当前交战目标单位 Id；null = 无。</summary>
    internal string? TargetUnitId;
    /// <summary>距下次可攻击的步数。</summary>
    internal int CooldownRemaining;
    /// <summary>承接的悬赏旗 Id；null = 无（英雄只为钱出工）。</summary>
    internal string? ClaimedFlagId;
    /// <summary>上步钱包快照（需求流失的「任何金币进账」检测基准）。</summary>
    internal int LastObservedGold;
}

/// <summary>
/// 单位阵亡报告 — 逻辑层产出，表现层消费（播放死亡表现、更新计数）。
/// Gold = 阵亡时随身金币（随遗体消失 — 经济守恒审计的焚烧口之一）。
/// </summary>
public sealed record UnitDiedReport(string UnitId, string Name, Faction Side, int Level, int Gold = 0);

/// <summary>
/// 战斗事件 — 一次攻击命中的最小战报。
///
/// <para><b>挨打的那一方是不是单位，由发这一条的人写下来</b>（<c>TargetIsUnit</c>）：
/// 三个发出点各自都知道（单位↔单位、单位↔建筑、怪物↔建筑），而读的人事后查不出来 ——
/// 击杀那一发的目标血量已 ≤ 0，按血量查「还在不在场」得到的答案与建筑一样。
/// 表现层靠它分开「这一次出手该不该演在技能事件那一条上」。</para>
/// </summary>
public sealed record CombatEventReport(
    string AttackerId, string TargetId, int Damage, bool TargetDied, bool TargetIsUnit);

/// <summary>
/// 悬赏结算报告 — 目标死亡、赏金支付、英雄承接。
/// </summary>
public sealed record BountyEventReport(string FlagId, string TargetId, string? HeroId, int Gold, BountyEventKind Kind);

/// <summary>悬赏事件种类。</summary>
public enum BountyEventKind
{
    /// <summary>玩家挂上了悬赏旗。</summary>
    Placed,

    /// <summary>英雄承接了悬赏。</summary>
    Claimed,

    /// <summary>目标死亡/摧毁，赏金支付给英雄。</summary>
    Paid,

    /// <summary>玩家提高了悬赏金额。</summary>
    Raised,

    /// <summary>承接英雄阵亡，悬赏回到待接池。</summary>
    Released,

    /// <summary>悬赏无人承接/承接者亡未领，赏金退回金库（玩家不白花钱）。</summary>
    Refunded,
}

/// <summary>
/// 经济事件 — 英雄消费、税收回流与领主账本（金币循环的可观测流）。
/// </summary>
public sealed record EconomyEventReport(string? UnitId, EconomyEventKind Kind, int Amount);

/// <summary>经济事件种类。</summary>
public enum EconomyEventKind
{
    /// <summary>英雄在市场消费（支出进市场，税收比例回流金库）。</summary>
    HeroSpent,

    /// <summary>英雄收到悬赏赏金。</summary>
    HeroEarned,

    /// <summary>税收回流金库（英雄消费部分）。</summary>
    TaxCollected,

    /// <summary>英雄升级。</summary>
    HeroLeveledUp,

    /// <summary>村民税入账（周期保底收入，UnitId=null 表示系统级事件）。</summary>
    VillagerTax,

    /// <summary>冒险者因赏金饥饿离队（长期零收入 — 需求流失，UnitId=离队冒险者）。</summary>
    HeroLeftNoBounty,

    /// <summary>冒险者因酒馆饥饿离队（长期喝不上酒 — 需求流失，UnitId=离队冒险者）。</summary>
    HeroLeftNoTavern,

    /// <summary>悬赏无人承接/承接者亡未领时退款回金库（UnitId=null）。</summary>
    BountyRefunded,

    /// <summary>英雄在市场购买装备（消费 → 战力，切片4「养英雄」闭环）。</summary>
    HeroBoughtGear,

    /// <summary>英雄击杀怪物从虚空拾得黄金（战利品直接进英雄钱包，切片6）。</summary>
    KillLoot,

    /// <summary>英雄在酒馆喝酒恢复士气（士气消费，切片7 需求层级）。</summary>
    HeroDrank,

    /// <summary>英雄在药店购买药剂回血（伤势消费，切片7 需求层级）。</summary>
    HeroBoughtPotion,

    /// <summary>玩家花费金库放置建筑（金币转资产 — 一次性造价，无维护费；UnitId=null）。</summary>
    BuildingBuilt,

    /// <summary>玩家花费金库升级行会（金币转产出链能力 — 切片 10 纵向成长；UnitId=被升的行会 Id）。</summary>
    GuildUpgraded,
}
