#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Entities;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 战场上的一个兵 —— 位置、血、攻击冷却、当前目标。
/// 纯数据：表现层（M5）只读它，不改它。
/// </summary>
public sealed class BattleUnit
{
    /// <summary>稳定序号（同方内从 1 起）；Id = 方别前缀 + 序号，重跑逐字相同。</summary>
    public required int Seq { get; init; }
    public required Side Side { get; init; }
    public required TroopKind Kind { get; init; }
    public required string Id { get; init; }

    /// <summary>
    /// 种族 —— 由摆法自带（M8 的 <see cref="Formation.Race"/>），没写就由方别定
    /// （我方人族、敌方兽人，<see cref="UnitCards.RaceOf"/>）。
    /// 它回答三件事：穿哪件模型、叫什么名字、<b>吃哪一条种族规则</b>（M8 起两族不再同值）。
    /// 但结算这一层<b>不读它</b>：数值从 <see cref="Card"/> 来，规则在兵种卡那一侧就施加完了
    /// ——「逻辑层零种族分支」有测试钉着（<c>RaceRuleTests</c>）。
    /// </summary>
    public required Race Race { get; init; }

    /// <summary>这张兵在阵面上的样子（模型名 / 中文名 / 走路还是奔跑）。表现层读它，结算不读它。</summary>
    public UnitCard Look => UnitCards.Of(Race, Kind);

    /// <summary>
    /// 这个兵参战用的数值卡（未抖动）—— 「本该多快、多狠、够得着几米」的对照基准。
    /// 它就是 <see cref="Look"/> 那张卡的 <c>Stats</c>（含本族的种族规则），不是另一份账：
    /// 有测试逐兵钉 <c>Card ＝ Look.Stats</c>。
    /// </summary>
    public required TroopCard Card { get; init; }

    /// <summary>出生格中心 —— 留着只为回答「他原来站哪」，战斗期已自由移动。</summary>
    public required Vector2 HomeCell { get; init; }

    /// <summary>抖动后的移速（米/秒）；jitter 关闭时 = 兵种卡原值。</summary>
    public required float Speed { get; init; }

    /// <summary>抖动后的攻击间隔（步）；jitter 关闭时 = 兵种卡原值。</summary>
    public required int IntervalTicks { get; init; }

    /// <summary>入场坐标（含出生格抖动）。</summary>
    public Vector2 Position { get; set; }

    /// <summary>血上限。真值只有这一格（技能层每步从这里抄 <c>hp_max</c>，不另存一份）；
    /// 改它只有两处：入场按数值卡 init、演示档整局抬高血量（<c>DuelRiftSetup.NewShowcaseWorld</c>）。</summary>
    public int MaxHealth { get; set; }
    public int Health { get; set; }

    /// <summary>还要等几步才能再出手（0 = 本步可出手）。</summary>
    public int CooldownRemaining { get; set; }

    /// <summary>当前目标 Id；null = 对面已无人。</summary>
    public string? TargetId { get; set; }

    /// <summary>累计出手次数与累计造成伤害（对照报告的输出账）。</summary>
    public int AttacksMade { get; set; }
    public int DamageDealt { get; set; }

    /// <summary>阵亡于谁手（还活着时为 null）。</summary>
    public string? KilledById { get; set; }

    /// <summary>阵亡播报是否已发（同一步被两人补刀也只发一次）。</summary>
    public bool DeathAnnounced { get; set; }

    /// <summary>这一步交出来的决策（世界自己判的、或挂树时树判的）—— <c>BrainReadout()</c> 用它数「几个兵听树的」。</summary>
    public BattleBrainPolicy.Decision LastDecision { get; internal set; }

    public bool Alive => Health > 0;

    /// <summary>全精度坐标（"R" 往返格式）—— 确定性哨兵拿它逐字比对，精度丢在这里就是假绿。</summary>
    public string PositionFull => $"({F(Position.X)},{F(Position.Y)})";

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>出手播报 —— 活人感阶段的微动作要有钩子可订阅，现在先把账发出来（AGENTS「验证流水线」前置契约）。</summary>
public readonly record struct AttackRow(long Tick, string AttackerId, string TargetId, int Damage, bool TargetDied);

/// <summary>阵亡播报。</summary>
public readonly record struct DeathRow(long Tick, string UnitId, Side Side, TroopKind Kind, string KilledById);

/// <summary>终局播报。</summary>
public readonly record struct EndRow(long Tick, BattleOutcome Outcome, int OurDeaths, int EnemyDeaths);

/// <summary>
/// M4 确定性自动战斗世界 —— 移动 → 打最近 → 结算，固定种子跑到一方全灭或 120 秒上限。
///
/// design.md 2 节那四条已定规则逐条落在这里：
/// - **打最近**（第 3 条）：目标 = 存活敌人里直线距离最近的那个，唯一目标规则、不做仇恨优先级；
///   前排天然吸火、弓手天然被保护，是这条规则的副产品而不是第二套机制。
/// - **只平A**（第 2 条）：伤害 = 兵种卡攻击值，每攻击间隔扣一次血；无暴击无护甲。
///   技能**不在这份逻辑层手搓** —— 冷却、施法、状态效果由公共层
///   <c>src/GugaFramework/Abilities/</c> 承担，本世界只挂一个可关的 H3 技能层
///   （null = 全关档，世界的演化与快照一个字节都不变）。
/// - **连续空间**（第 1 条）：网格只在开局占格用，战斗期是以米为单位的连续位移。
/// - **固定种子**（灰盒阶段结论 M4）：时间轴走 GugaFramework 的 <see cref="GameClock"/>（0.1 秒一步），
///   随机只有一个 <see cref="Random"/>（入场抖动，按出场顺序抽取），遍历只走 List 不用字典序。
///
/// 先后手偏差的处理：本步所有出手在决策阶段**声明**、在结算阶段**同时扣血**——
/// 「谁先被遍历到」不决定谁先死。敌我是同一张兵种表的镜像，任何系统性偏差都会污染对照测试的归因。
///
/// 架构位置：三层响应式架构 —— 纯数据逻辑层（零 Godot 类型，进 dotnet test）。
/// </summary>
public sealed class BattleWorld : GugaFramework.Base.GameLogicBase, GugaFramework.Replay.IReplayWorld,
    GugaFramework.Entities.IEntityHost
{
    /// <summary>出生格内的位置抖动幅度（米）—— 让同格同兵种的两次入场不完全重合。</summary>
    public const float JitterPositionMeters = 0.35f;

    /// <summary>移速抖动比例（±8%）。</summary>
    public const float JitterSpeedRatio = 0.08f;

    /// <summary>
    /// 册子里那一列 <c>kind</c> 的值 —— 排兵布阵这一档只有兵这一种。
    /// <b>路障不进这一本</b>：它在建造层那一份册子里，两边各答各的域（D5：建筑并入册子、
    /// 但建造层保权威）。这一档册子里的建筑与那两条规程都没有调用点。
    /// </summary>
    public const string UnitKind = "unit";

    /// <summary>攻击间隔抖动（±1 步 = ±0.1 秒）。</summary>
    public const int JitterIntervalTicks = 1;

    private readonly GameClock _clock;

    /// <summary>
    /// 在场的那本册子（实体层，<c>src/GugaFramework/Entities/</c>）—— id 身份、发号、登记次序、
    /// 事件出口、快照段、读数都在它那一份。
    ///
    /// <para><b>它答「谁在场」，不答「他血多少」</b>：血量在 <see cref="BattleUnit.Health"/>，
    /// 册子只通过 <see cref="IEntityHost.Alive"/> 问本类（那一格就是 <see cref="BattleUnit.Alive"/>），
    /// 册子里一个字都不存血量。<b>寻路层与建造层各答各的域</b>：位置真值在寻路层那一份流水线里，
    /// 路障的血在建造层那一份册子里，本类的快照照旧各念各的。</para>
    /// </summary>
    private readonly EntityCore _entities;

    /// <summary>
    /// 全部单位（含已阵亡，按出场顺序）—— 表现层镜像与快照的遍历面。
    /// <b>它不是「在场」的真值</b>，只是一份按出场顺序摆好的玩法视图；
    /// 「在场」问 <see cref="_entities"/>，两边由 <see cref="Populate"/> 一次登记接上。
    /// </summary>
    private readonly List<BattleUnit> _units = [];
    private readonly Random _rng;
    private readonly bool _jitter;

    /// <summary>本局的种子（技能层要另一条随机流，从它派生；快照与对照报告也念它）。</summary>
    public int Seed { get; }

    /// <summary>
    /// 贴在这一局上的技能层（H3）。<b>null = 全关档</b>：世界的演化与快照一个字节都不变，
    /// 「接了技能会不会碰坏已验证的机制」就靠这一档回答。
    /// </summary>
    public BattleAbilities? Abilities { get; set; }

    /// <summary>出手流 / 阵亡流 / 终局流。</summary>
    public event Action<AttackRow>? Attacked;
    public event Action<DeathRow>? Died;
    public event Action<EndRow>? BattleEnded;

    /// <summary>
    /// 摆一个世界：两方各一套摆法。
    /// 抖动开着时换种子才真的换一场仗 —— 摆位对照测试的「20 局」就是 20 粒固定种子；
    /// 关掉抖动得到纯确定战斗，机制断言用它算手上台账（13 刀砍死 260 血的坦克＝130 步，这类能手算的账）。
    /// </summary>
    /// <param name="maxBattleTicks">
    /// 步数上限 —— 默认取契约的 120 秒（design.md 「已定」第 5 条；对外只说秒，这里是换算后的步）。
    /// 只有一处允许改它：测试要用一个小上限证明「到点按契约判我方赢、并另记一笔超时」，
    /// 而这套不许 retreat 的规则里真仗 20~31 秒就打完，不改上限就构造不出那个状态。
    /// </param>
    /// <param name="phase">
    /// 世界出生在哪一档。<see cref="BattlePhase.Deploying"/> ＝ 布阵期（M6 的布阵棋盘用它开出一局还没打的阵面：
    /// 兵已入场、时间轴是冻着的、<see cref="Step"/> 也不认这一档 ⇒ 摆子期间演化量为零），
    /// 由 <see cref="BeginBattle"/> 推到 <see cref="BattlePhase.Fighting"/>。
    /// 缺省 <see cref="BattlePhase.Fighting"/>：M4 的对照跑与 M5 的运行时复现吃的就是这一档，一个字节都没改。
    /// </param>
    /// <param name="board">
    /// 这一局挂着的布阵棋盘（摆子口那个写入口的落点，见 <see cref="PlaceUnit"/>）。
    /// 缺省 null ＝ 不接摆子口：单挑局与预设摆法那两档不摆子。
    /// 棋盘不归世界所有 —— 它在世界换局之间活着，交还归调用方（与 <see cref="Abilities"/> 同一档）。
    /// </param>
    public BattleWorld(int seed, Formation player, Formation enemy, bool jitter = true,
        int maxBattleTicks = BattleContract.MaxBattleTicks, BattlePhase phase = BattlePhase.Fighting,
        DeploymentBoard? board = null)
    {
        // 时钟由基类建，这个字段指的是那一个（派生类的字段初始化器跑在基类构造之前，
        // 所以不能在字段声明处赋值，那时 Clock 还是空的）。
        _clock = Clock;
        _board = board;
        if (maxBattleTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxBattleTicks), "步数上限必须 >= 1");
        MaxBattleTicks = maxBattleTicks;
        Phase = phase;
        Seed = seed;
        _rng = new Random(seed);
        _jitter = jitter;
        // 册子要在摆兵之前就位：摆兵那一条生成路就是往它那一份登记（D13 迁册子）。
        // 移除接线递的是本类自己（不是 _navMove）—— 寻路层那半在 Populate 之后才建好，
        // 接线按 id 回头找它，所以登记期不碰那一步，而那一步本来也只在移除时才发生。
        _entities = new EntityCore(this, new BattleEntityExit(this), () => _clock.Tick);
        Populate(player, Side.Player);
        Populate(enemy, Side.Enemy);
        SubscribePulse(Step);
        // 走位那一半接寻路层，**订阅次序就是执行次序**：世界的 Step 先跑（攒命令、递标量与标记），
        // NavStepper 那一次脉冲接着跑（照命令走位 + 站位冲突）。
        // 两句为什么不能反：反过来的话，避让用的「这一趟算不算它」会拿上一批标记，
        // 于是「本步刚倒下的那一个」在它自己倒下的那一步里还会推开别人 ——
        // 而那正是退休前 SeparateSameSide 靠 `!a.Alive` 挡住的那一处。
        _navMove = new BattleNavMove(Assembly, this);
        // 走位这一半的退订登记进基类那张表（镜像同步那条订阅原来没人交还：
        // 换局之后旧世界还挂在时钟上，坐标照旧在动）。
        TrackDetach(_navMove.Detach);
        // 建造层与地形接线：蓝图表一次递入；路障的阻挡走 NavTerrainLink 的**网格侧** ——
        // 事件自带的格名单非空时它直接落，两个查法（建造格 / 寻路格）都不用递。
        // 静态阻挡表仍是空表（BattleNavGrid.Build 那一句注释）：阻挡全部来自运行时放置。
        _construction = Assembly.BuildConstruction(BattleRoadblock.NewRule(), BattleRoadblock.All);
        _terrain = Assembly.AttachTerrain(BattleRoadblock.ToNavCell);
        // 布阵期把时间轴冻着：这样「还没开打」只由 Phase 一处说话，暂停钮碰不到它
        if (phase == BattlePhase.Deploying) _clock.Pause();
    }

    /// <summary>
    /// 走位端口（工单 08：直线趋近与站位冲突都已退休，改走寻路层那一份）。
    /// 世界这一侧<b>只下命令、递标量、读移动解</b>；位置的真值住在流水线（「寻路单入口」）。
    /// </summary>
    private readonly BattleNavMove _navMove;

    /// <summary>
    /// 建造模块（赛前路障那一册）。本类<b>组合持有</b>它（不继承），写路径只有下面那三个口 ——
    /// 「路障占着哪一格、落了没有」从此只有模块册一处真值。
    /// </summary>
    private readonly ConstructionCore _construction;

    /// <summary>
    /// 建造占用 → 寻路网格的地形接线（<b>网格侧</b>：路障的格名单由建造事件自带）。
    /// 每步推进逻辑之前 <see cref="NavTerrainLink.Pump"/> 一次，次序是那一份契约。
    /// </summary>
    private readonly NavTerrainLink _terrain;

    /// <summary>
    /// 这一局挂着的布阵棋盘（null = 不接摆子口）。它是<b>布阵期的唯一真值</b>：
    /// 摆子口那三条命令写它，快照里那一段也念它（<see cref="BoardRows"/>）。
    /// </summary>
    private readonly DeploymentBoard? _board;

    /// <summary>本局走到哪一档（布阵 / 开打 / 已判定）—— 快照第一行报的就是它。</summary>
    public BattlePhase Phase { get; private set; }

    /// <summary>
    /// 开局：布阵期 → 战斗。改这一档 + 解冻时间轴，两件事一起做（「布阵期」与「时间轴冻着」是同一件事的两面，
    /// 分开改就会漂）。已经在战斗或已判定时它什么都不做，只把当前状态回报回去 —— 摆 UI 时手滑连点两次不该变两局。
    /// </summary>
    public string BeginBattle()
    {
        if (Phase != BattlePhase.Deploying) return $"没开打：当前是 {Phase}，开局口只管布阵期那一档";
        Phase = BattlePhase.Fighting;
        _clock.Resume();
        return $"开局：{Fielded(Side.Player)} 个兵对 {Fielded(Side.Enemy)} 个兵，时间轴已解冻（第 0 秒）";
    }

    // === 赛前路障：建造层那三个口（摆一座 / 撤一座 / 只读裁决）===
    //
    // 三个口都走建造模块那一份裁决与那一册，本类只做三件事：加前置条件、把落点递进去、每发之后
    // 把地形接线 Pump 一次（落阻挡当场生效）。路障不进经济（模块不认识钱，这一局也没有战时经济），
    // 也没有攻击路径（这一侧一条打建筑的写口都没接上）。

    /// <summary>建造模块（只读那一半：册、事件出口、裁决查询）。写路径走下面那三个口。</summary>
    public ConstructionCore Construction => _construction;

    /// <summary>
    /// 建造 → 寻路的地形接线（读数口：<c>StampedCount</c> / <c>Missed</c> / <c>CellsOf</c>）。
    /// 落下的阻挡就是路障占的那一格。
    /// </summary>
    public NavTerrainLink Terrain => _terrain;

    /// <summary>
    /// 这一发路障能不能放 —— <b>只有布阵期让放</b>（spec Q14：开打之后阵地已经定下）。
    /// 不能就把理由给出来：拒绝必须带理由，不许静默兜到别的格或别的兵种
    /// （「事件发射前置」：关键决策要有一句读得出的结果）。
    /// </summary>
    /// <param name="reason">不能放时的理由（能放时是空串）—— 直接是给人看的那一句。</param>
    public bool CanPlaceRoadblock(out string reason)
    {
        if (Phase == BattlePhase.Deploying)
        {
            reason = string.Empty;
            return true;
        }

        reason = Phase == BattlePhase.Fighting
            ? $"布阵期才能摆路障：这一局已经开打（{Phase}）—— 阵面在开局那一刻定下，要改先回布阵期（restart_deploy 那一口）"
            : $"布阵期才能摆路障：这一局已经判定（{Phase}）—— 要重摆先回布阵期（restart_deploy 那一口）";
        return false;
    }

    /// <summary>
    /// 摆一座路障（<b>唯一一处落建入口</b>：词表转发、调试口与测试排布都走它）。
    /// 成不成、为什么不成由模块那一份裁决给（拒绝是读数，不是异常）。
    /// 落完当场 Pump 一次地形 —— 那一格立刻封上，绕障的读数与建造这一发对得上。
    /// </summary>
    internal WriteResult PlaceRoadblockCore(string blueprintId, Vector2 position, string? buildingId,
        long? buildDurationTicks)
    {
        var verdict = _construction.Commit(blueprintId, position, BattleRoadblock.Orientation, buildingId,
            buildDurationTicks);
        _terrain.Pump();

        // 新落的那一座在册尾（登记序）。自动发号那一档的发号在模块里，这一侧不另编一套。
        return verdict.Ok
            ? WriteResult.Done(_construction.Ids[^1])
            : WriteResult.Fail(verdict.Reason, buildingId ?? string.Empty);
    }

    /// <summary>
    /// 撤掉一座路障：腾占用 + 出册 + 发 Removed(去向)，并当场 Pump 一次地形（那一格立刻解封）。
    /// 未知 id 由模块回 <c>UnknownId</c>（拒绝是读数，不是异常）。
    /// </summary>
    internal WriteResult RemoveRoadblock(string buildingId, RemovalCause cause)
    {
        var result = _construction.Remove(buildingId, cause);
        _terrain.Pump();
        return result;
    }

    /// <summary>
    /// 放不放得下那一行的读数（只读查询，与落建口同一条流水线）。
    /// <b>前置条件也念在这一行里</b>：不在布阵期时它说的与落建口被拒是同一句话 ——
    /// 预览说行、点下去被拒是这一层最贵的一种假绿。
    /// </summary>
    public string CheckRoadblockPlacement(string blueprintId, double x, double y, int orientation)
    {
        if (!CanPlaceRoadblock(out var why))
        {
            return $"ok=no reason={PlacementReason.InvalidState} why={why}";
        }

        return ConstructionVerbs.VerdictLine(_construction.Check(blueprintId, new Vector2((float)x, (float)y),
            orientation));
    }

    // === 摆子口：DeploymentBoard 那三条写路径收进那个写入口 ===
    //
    // 词表转发（DuelRiftSetup.Inject）、调试直达口（Main 的三个 Debug 口）与鼠标拖放都走这三条，
    // 于是「注入的路径」与「玩家的路径」是同一条（「桥只转发」、「写路径进 Journal」）。
    //
    // 为什么那个写入口开在世界这一侧：重放的写入口是 DuelRiftSetup.Inject(世界, 命令, 参数)，
    // 它只拿得到世界；棋盘与录制端因此要挂在同一处，否则三个调用点各自直调棋盘，
    // 录制期间那一笔就漏了（漏录一次就让重放分叉一次）。
    //
    // 谁是布阵期的真值：棋盘。世界这一列的兵是**入场那一刻**按棋盘摆出来的，棋盘再变一次，
    // 换新世界那一笔归宿主（Main 订阅棋盘的 Changed）—— 宿主那一笔不在 Journal 里，
    // 所以离线这一档（没有宿主）重放的是棋盘的账。

    /// <summary>
    /// 这一局挂着的布阵棋盘（null = 不接摆子口：单挑局与预设摆法那两档）。
    /// 它是布阵期的唯一真值，下面三条写路径都写它。
    /// </summary>
    public DeploymentBoard? Board => _board;

    /// <summary>摆一个兵到指定格 —— <c>place</c> 那条写路径的落点（<b>唯一一处落子口</b>）。</summary>
    public PlacementOutcome PlaceUnit(TroopKind kind, int row, int column)
        => RefuseDeploy(kind, row, column) ?? _board!.Place(kind, row, column);

    /// <summary>撤掉某一格的兵 —— <c>remove</c> 那条写路径的落点。</summary>
    public PlacementOutcome RemoveUnit(int row, int column)
        => RefuseDeploy(TroopKind.Tank, row, column) ?? _board!.Remove(row, column);

    /// <summary>把一格的兵挪到另一格 —— <c>move</c> 那条写路径的落点。</summary>
    public PlacementOutcome MoveUnit(int fromRow, int fromColumn, int toRow, int toColumn)
        => RefuseDeploy(TroopKind.Tank, toRow, toColumn) ?? _board!.Move(fromRow, fromColumn, toRow, toColumn);

    /// <summary>
    /// 摆子口的两道关：**只在布阵期**，而且这一局得挂着棋盘。
    /// <b>拒了回一条带理由的回执</b>（「事件发射前置」：关键决策要有一句读得出的结果），
    /// 回 null 表示这一发放行。理由与路障那个写入口同一句话，读的人不必猜 InvalidState 是什么意思。
    /// </summary>
    private PlacementOutcome? RefuseDeploy(TroopKind kind, int row, int column)
    {
        if (Phase != BattlePhase.Deploying)
        {
            return new PlacementOutcome(false, Phase == BattlePhase.Fighting
                ? $"布阵期才能摆子：这一局已经开打（{Phase}）—— 阵面在开局那一刻定下，要改先回布阵期（restart_deploy 那一口）"
                : $"布阵期才能摆子：这一局已经判定（{Phase}）—— 要重摆先回布阵期（restart_deploy 那一口）",
                kind, row, column);
        }

        return _board is null
            ? new PlacementOutcome(false, "这一局没挂布阵棋盘（单挑局与预设摆法那两档不接摆子口）",
                kind, row, column)
            : null;
    }


    /// <summary>本局的步数上限（默认 = 契约的 120 秒换算成 1200 步）。</summary>
    public int MaxBattleTicks { get; }

    /// <summary>
    /// 这局是不是「打满上限没死人、按契约判赢」—— 与真把对面打光区分开记账。
    /// 对照跑有测试守着它：满编 15v15 实测 20~31 秒就该打完，任何一局走到这条线都是机制退化的信号。
    /// </summary>
    public bool DecidedByTimeout { get; private set; }

    /// <summary>战斗判定（Running = 还在打）。</summary>
    public BattleOutcome Outcome { get; private set; } = BattleOutcome.Running;

    /// <summary>判定发生在第几步（没判定为 null）。</summary>
    public long? OutcomeTick { get; private set; }

    /// <summary>全部单位（含已阵亡，按出场顺序）—— 表现层镜像与快照的遍历面。</summary>
    public IReadOnlyList<BattleUnit> Units => _units;

    /// <summary>
    /// 决策来源钩子 —— 运行时（M5 起）由表现层装上「问这兵的树」；离线测试与没挂树时留 null，
    /// 走同一条纯 C# 规则 <see cref="BattleBrainPolicy.Decide"/>。
    /// 两条路调用的是同一个函数、同一份观测口径 ⇒ 挂不挂树，演化逐字相同
    /// （<c>BrainReadout()</c> 会把「几个兵真的在听树」报出来，0 个就是树没接上，不许静默兜底）。
    /// </summary>
    public Func<BattleUnit, BattleBrainPolicy.Senses, BattleBrainPolicy.Decision>? Brain { get; set; }

    /// <summary>
    /// 暂停 / 恢复。design.md 1 节那句「战斗期间只有一件事可以做——随时暂停」就是这一格。
    /// 布阵期不接：那一档的时间轴本来就冻着，解冻它的口子是 <see cref="BeginBattle"/>，
    /// 让暂停钮也能开打就等于两条路改同一件事（会漂）。
    /// </summary>
    public override void SetPaused(bool paused)
    {
        if (Phase == BattlePhase.Deploying) return;
        if (paused) Pause();
        else Resume();
    }

    /// <summary>
    /// 单步调试口 —— <b>暂停中也照走一步</b>：「随时暂停」的手感就是停下来之后一格格看（M6）。
    /// 直接 <see cref="Advance"/> 在暂停中会空转（那是「暂停冻住时间轴」的正确行为，不是 bug），
    /// 所以这里临时解冻、推一步、再冻回去 —— 步数照常可核对，暂停状态不被这个口改掉。
    /// 布阵期返回 false（还没开打，没有「下一步」可言）。
    /// </summary>
    public bool AdvanceOneDebugStep()
    {
        if (Phase != BattlePhase.Fighting) return false;
        var wasPaused = _clock.IsPaused;
        if (wasPaused) _clock.Resume();
        var pulsed = _clock.Advance(1);
        if (wasPaused) _clock.Pause();
        return pulsed;
    }

    /// <summary>推到判定或推到本局步数上限（长跑便捷口）。返回是否在上限内判定掉了。</summary>
    public bool AdvanceUntilEnd()
    {
        while (Outcome == BattleOutcome.Running && Tick < MaxBattleTicks)
        {
            Advance(1);
        }
        return Outcome != BattleOutcome.Running;
    }

    /// <summary>
    /// 调试直达 —— <b>暂停中也照走 n 步</b>：与 <see cref="AdvanceOneDebugStep"/> 同一件事，只是批量。
    /// 它是 D1 还缺的那一半的口子（「<c>Advance(k)</c> 直达与逐步推到的快照逐字等」），
    /// 也是级 2 重放里 <c>AdvanceTo</c> 那一钩：重放端要能一步跳到剧本记的那一步，而不是等画面把它喂到那儿。
    /// 布阵期不做事（还没开打，没有「下一步」可言），返回实际走了几步。
    /// </summary>
    public int AdvanceDebugSteps(int steps)
    {
        if (Phase != BattlePhase.Fighting) return 0;
        var from = _clock.Tick;
        var wasPaused = _clock.IsPaused;
        if (wasPaused) _clock.Resume();
        _clock.Advance(Math.Max(0, steps));
        if (wasPaused) _clock.Pause();
        return (int)(_clock.Tick - from);
    }

    /// <summary>
    /// 查一个兵。**在场那一问已经归实体层**：本类不再自留一份 id 表，
    /// 查法是「册子里那一条的不透明载荷」（D7：册子存它、还它、不读它）。
    /// </summary>
    public BattleUnit? Find(string? id)
        => id is null ? null : _entities.Find(id)?.Payload as BattleUnit;

    /// <summary>
    /// 实体层那一个宿主查询口（D6）—— <b>整个接口就这一个成员</b>。
    /// 「活着」的真值在 <see cref="BattleUnit.Health"/>，册子只问不存（D2）。
    /// </summary>
    public bool Alive(string id) => Find(id) is { } unit && unit.Alive;

    /// <summary>
    /// 移除时寻路层那一步（D11 ②）：<b>就地定住 + 退出站位冲突那一档</b>。
    ///
    /// <para><b>载荷从册子交回来的那一条上取</b>，不回头查册子：这一步跑在①摘册之后，
    /// 那时 <see cref="Find"/> 已经查不到它了，而「拿到 null 就静默不做事」正是
    /// 「退场还在挪」那一坑的形状（D11 收编的就是它）。</para>
    ///
    /// <para>寻路层没有注销口（<c>BeastNavMove.RetireAbsentResidents</c> 注释里记着这一条），
    /// 所以「退注册」就是这两句既有 API —— 不给寻路层加能力（「寻路单入口」）。
    /// 定住必须是先手：位置真值住在流水线里，不换命令它会照着手上那条继续走，
    /// 而一个已经不在册的单位还在挪，<b>读数上查不出来</b>（三家各踩过一次的那一坑）。</para>
    ///
    /// <para><b>本原型这一档没有调用点</b>：战斗中零生成零删除，只置血零不出表（D13）。
    /// 上面那句「每步把不活的那个发一条驻守命令」是另一件事——那是活着的单位里的停步令，
    /// 不是册子上的移除。移除那一档的接线在这里，真发起来是迁第三步的事。</para>
    /// </summary>
    private void DetainUnitInNav(EntityRecord record)
    {
        if (record.Payload is BattleUnit unit) _navMove.Retire(unit);
    }

    /// <summary>该方存活数。</summary>
    public int AliveCount(Side side)
    {
        var count = 0;
        foreach (var unit in _units)
        {
            if (unit.Side == side && unit.Alive) count++;
        }
        return count;
    }

    /// <summary>该方累计阵亡数。</summary>
    public int Deaths(Side side)
    {
        var count = 0;
        foreach (var unit in _units)
        {
            if (unit.Side == side && !unit.Alive) count++;
        }
        return count;
    }

    /// <summary>该方出场数（摆法给了几个兵）。</summary>
    public int Fielded(Side side)
    {
        var count = 0;
        foreach (var unit in _units)
        {
            if (unit.Side == side) count++;
        }
        return count;
    }

    // === 每步结算：同步 → 技能 → 决策攒令 → 伤害 → 定住不再动的 → 清账判终局 ===
    //
    // 位移与站位冲突两步在**寻路层那一次脉冲里**（NavStepper.OnPulse），它在本函数之后跑：
    // 订阅次序写在构造函数那两行。次序仍是契约 —— 决策看到的是上一步落定的位置，
    // 伤害与「这一趟算不算它」看到的都是本步的存活表。

    private void Step(long tick)
    {
        // 地形先落地形再推这一步（次序是契约，见 NavTerrainLink.Pump）：反过来的话，
        // 「这一发摆的路障」要到下一步才挡人，绕障的读数与建造那一发对不上。
        // 统一推进次序那一处（WorldAssembly.Advance）推这一步之前也泵一次；
        // 这一行留着是因为那三个调试口直接推时钟、绕开那一处（收编它们是另一张 ticket 的活）。
        _terrain.Pump();
        if (Outcome != BattleOutcome.Running) return;
        // 布阵期就算时间轴被谁推了一把也不演化 —— 「摆子期间兵不会偷偷打起来」由这一行守住，
        // 不靠构造方记得暂停（那是两道校验，不是一道）。
        if (Phase != BattlePhase.Fighting) return;

        var attacks = new List<(BattleUnit Attacker, BattleUnit Target)>();

        // 阶段 0a：宿主那一列（BattleUnit.Position）已经是**上一步落定之后**的位置 ——
        // 走位与站位冲突跑完那一刻，寻路层把全量位置写回了这一列（BattleNavMove 订阅次序在最后）。
        // 所以这里不用再同步一次：再同步一次读到的还是同一份数，而多一次遍历就是多一处能漂的地方。

        // 阶段 0b：技能层先演化（H3 接上才有这一段；null = 全关，下面一行为都不做）。
        // 排在平A之前，于是技能造成的死亡在同一步的阵亡播报与终局判定里就说得出「死于第几步、谁打的」。
        // 带位移的那一发（rider.charge）在这里落位，走的是寻路层那个写口（BattleNavMove.ApplyMoveTo）。
        Abilities?.AdvanceStep();

        // 阶段 1：按出场顺序决策 —— 冷却递减、锁最近敌人、问一次「这一步该怎么办」
        // （规则在 BattleBrainPolicy；挂了行为树就问树，两边同一个函数），
        // 然后把「这一步该怎么走」翻译成命令递进寻路层（决定出手或对面无人 = 原地不动）。
        foreach (var unit in _units)
        {
            if (!unit.Alive) continue;

            if (unit.CooldownRemaining > 0) unit.CooldownRemaining--;

            // 移速这一格每步递一次：入场抖动之后的那个数乘技能层派生出来的倍率。
            // 递在命令之前 —— 激活检查与步长读的都是这一刻的值（「桥只转发」）。
            _navMove.SetSpeedOf(unit, unit.Speed * SpeedScaleOf(unit.Id));

            var target = NearestEnemy(unit);
            unit.TargetId = target?.Id;
            var decision = DecideFor(unit, SensesOf(unit, target));
            unit.LastDecision = decision;

            if (target is null)
            {
                _navMove.IssueFor(unit, null);   // 对面已无人：站着（决策已记账，行为流照读）
                continue;
            }

            switch (decision.Intent)
            {
                // `Hold` 那一档（对面已无人 / 在射程内但冷却没好）**必须显式发令**，不能落空。
                //
                // 为什么这一行不是可有可无：退休前那一格是「不进 moves 那一趟」，而 `moves` 每步重建，
                // 所以「不发令」在那套形状里天然等于「站着不动」。迁到「命令由寻路层持有」之后，
                // 这两件事**不再等价**：不发令 = 留着**上一步**那条命令。若上一步是出手发的
                // `NavCommand.Hold`（驻守点 = 那一刻的坐标、容差 0 = 走回驻守点），这个单位就会
                // 每步往一个**已经过时**的驻守点走回去，跟推开它的同袍抢位置 ——
                // 读数上表现为「命令是 Hold 却走了满步、状态 Moving、卡住 Crowded」，
                // 而且驻守点在连续几步里逐字不变。
                //
                // 实测（`.cache/Pathfinding/Probe09/H5Hold/`，200 粒种子 × 3 摆法，测试口径）：
                // 缺这一格时同向 141/200（CI [63.8, 76.4]）、摆位对照判绿线从 Matters 掉成 Unclear；
                // 补上后 167/200（CI [77.7, 88.0]）、20 局 3.850 / 19 局 ⇒ Matters，
                // 与退休前的基线**逐字相同**（600 局里 599 局逐字一致，三档均值 13.505 / 14.370 / 10.485）。
                case BattleBrainPolicy.Intent.Hold:
                    _navMove.IssueFor(unit, null);   // 与出手那一格同一句话：驻守点 = 它此刻站的那一点
                    break;
                case BattleBrainPolicy.Intent.Strike:
                    attacks.Add((unit, target));
                    unit.CooldownRemaining = unit.IntervalTicks;
                    _navMove.IssueFor(unit, null);   // 出手这一格站住脚（退休前是「不进 moves 那一趟」）
                    break;
                case BattleBrainPolicy.Intent.CloseIn:
                    _navMove.IssueFor(unit, target);
                    break;
            }
        }

        // 阶段 2：伤害同时落地（不分先后手；同一目标本步挨两刀都算数）
        // 扣的是「承伤」而不是「出招伤害」：目标只剩 4 血时那一刀也记 4 —— 守恒账才对得上，
        // 快照里也不会出现负血条。
        // 技能层给这一刀乘的是目标的 damage_taken 派生值（S2 出口那份，不在调用点现算）；
        // 倍率为 1.0 时这一乘是恒等，全关档与未挂技能栏的兵逐字不变。
        foreach (var (attacker, target) in attacks)
        {
            var struck = (int)Math.Round(attacker.Card.Attack * DamageTakenScaleOf(target.Id), MidpointRounding.AwayFromZero);
            var absorbed = Math.Min(struck, target.Health);
            target.Health -= absorbed;
            // 打死他的人记第一位出手致死的（同一目标本步挨两刀时按出场顺序算头一位）——
            // 「他死于谁手」是快照与暂停读数要念得出的事实，M6 之前这里一直没写，读出来全是「未知击杀」。
            if (target.Health <= 0 && target.KilledById is null) target.KilledById = attacker.Id;
            attacker.AttacksMade++;
            attacker.DamageDealt += absorbed;
            Attacked?.Invoke(new AttackRow(tick, attacker.Id, target.Id, absorbed, target.Health <= 0));
        }

        // 阶段 3：把「这一趟算不算它」按本步的存活表递进去，并定住已经不再动的那些
        // （退休前是阶段 3 的「本步阵亡的不再位移」+ 阶段 4 的 `!a.Alive` 跳过，合成这一段）。
        // 排在伤害之后：判断依据是**这一步扣完血之后**的存活表，与退休前那两段的判据同一个数。
        foreach (var unit in _units)
        {
            _navMove.SetCountedInSeparationOf(unit, unit.Alive);
            if (!unit.Alive) _navMove.IssueFor(unit, null);
        }

        // 阶段 4：站位冲突 —— 归寻路层那一次脉冲（它在本函数之后跑）。
        // 退休前是这里的 SeparateSameSide（照同方两兵各推开一半的形状），现已退休；
        // 身体半径的差异在那一边照旧体现（骑手 0.75 把弓手 0.40 挤开）。

        // 阶段 5：血量清账 + 阵亡播报（每人只发一次）+ 终局判定
        foreach (var unit in _units)
        {
            if (unit.Alive || unit.DeathAnnounced) continue;
            unit.Health = 0; // 超杀不外溢：快照里不许出现 −14
            unit.DeathAnnounced = true;
            Died?.Invoke(new DeathRow(tick, unit.Id, unit.Side, unit.Kind, unit.KilledById ?? "未知"));
        }
        Judge(tick);

        // 判定之后世界那五段直接返回（不再发令），而寻路层那一次脉冲还在跑 ——
        // 不把驻守点钉住的话，手上那一条「追实体」会让全场照旧往目标走（退休前不存在这一格：
        // 位移在世界自己手里，世界一停它就停）。判定落定这一刻统一钉住，位移与退休前一样停。
        if (Outcome != BattleOutcome.Running) _navMove.HoldAll();
    }

    /// <summary>存活敌人里直线距离最近者；同距离按 Id 字典序取小（确定性，且不靠遍历巧合）。</summary>
    private BattleUnit? NearestEnemy(BattleUnit unit)
    {
        BattleUnit? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var other in _units)
        {
            if (!other.Alive || other.Side == unit.Side) continue;
            var distance = (unit.Position - other.Position).Length();
            if (best is null || distance < bestDistance - 1e-6f
                || (distance <= bestDistance + 1e-6f && string.CompareOrdinal(other.Id, best.Id) < 0))
            {
                best = other;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>问一步决策：挂了树问树，没挂就走同一条纯 C# 规则（两条路逐字等价的根据在 <see cref="Brain"/>）。</summary>
    private BattleBrainPolicy.Decision DecideFor(BattleUnit unit, BattleBrainPolicy.Senses senses)
        => (Brain?.Invoke(unit, senses) ?? BattleBrainPolicy.Decide(senses));

    /// <summary>一个兵这一步看得见的东西 —— 全是从状态现算的标量，没有第二份账。</summary>
    public BattleBrainPolicy.Senses SensesOf(BattleUnit unit, BattleUnit? target)
        => target is null
            ? new BattleBrainPolicy.Senses(false, 0f, 0f, unit.CooldownRemaining)
            : new BattleBrainPolicy.Senses(
                true,
                (unit.Position - target.Position).Length(),
                unit.Card.ReachAgainst(target.Card),
                unit.CooldownRemaining);

    /// <summary>
    /// 走位那一段的读数（状态级，「状态可查」那个口）——
    /// 每个单位最新一步的<b>位置、朝向、位移、状态、卡住类别、激活检查说法</b>。
    ///
    /// <para>为什么世界要念它：世界的 <see cref="DebugSnapshot"/> 那一列 <c>why=</c> 说「为什么这么决定」，
    /// 而「它这一动走了多远 / 有没有被推开 / 激活检查为什么不让它动」只在寻路层那一份读数里。
    /// 没有这一段，位置不对时排查要跨到流水线的源码里（「可调试性优先」）。</para>
    /// </summary>
    public string NavDebugSnapshotSegment() => _navMove.DebugSnapshotSegment();

    /// <summary>
    /// 一个单位最新一步的移动解（读数口）—— 位置、朝向、位移、状态、卡住类别、激活检查说法。
    /// 与 <see cref="NavDebugSnapshotSegment"/> 同一份数据，一个给整段、一个给单个。
    /// </summary>
    public NavMoveSolution NavSolutionOf(string unitId) => _navMove.SolutionOf(unitId);

    /// <summary>
    /// 走位端口本体（<b>行为可注入</b>那个口）——
    /// 调试通道要能改「这一趟算不算它」这类每步标记、要能看登记次序与网格，不必另开第二个实例。
    ///
    /// <para><b>为什么不多这一件就看不到</b>：端口的三个写口（落位移 / 移速标量 / 参与标记）
    /// 正常都由世界自己每步递；调试时想固定其中一件（例如「这一趟让甲不算」去对照读数），
    /// 没有这个把手就只能另造一条流水线 —— 那样量到的就不是这一局了。</para>
    /// </summary>
    public BattleNavMove NavMove => _navMove;

    /// <summary>
    /// 带位移的那一发技能落地：深度轴（<c>Position.Y</c>）那一列改成给定值，横向那一列不动。
    ///
    /// <para><b>为什么走这一发而不是技能层直接改 <c>Position</c></b>：位置的真值住在寻路层
    /// （「寻路单入口」），世界这一列只是每步同步一次的镜像 ——
    /// 技能层直接改它，下一步的同步就把这一笔抹掉，而快照上完全看不出发生过（「事件发射前置」：
    /// 关键决策必须有事件发射，抹掉的那一笔就是一次静默）。</para>
    /// </summary>
    internal void PlaceDepthAxis(BattleUnit unit, double depthAxisMeters) => _navMove.ApplyMoveTo(unit, depthAxisMeters);

    /// <summary>承伤倍率：技能层挂着就读它 S2 派生出的 <c>damage_taken</c>，没挂就是 1.0（一份真值，不另算）。</summary>
    private double DamageTakenScaleOf(string unitId) => Abilities?.DamageTakenScale(unitId) ?? 1.0;

    /// <summary>移速倍率：同上，读 <c>move_speed</c>。口径与限制见 <see cref="BattleAbilities"/> 抬头边界 2。</summary>
    private double SpeedScaleOf(string unitId) => Abilities?.SpeedScale(unitId) ?? 1.0;

    /// <summary>
    /// 技能造成的一次伤害进世界账（由 <see cref="BattleAbilities.AdvanceStep"/> 每步写回时调用）。
    /// 它<b>不碰</b> <c>AttacksMade</c> / <c>DamageDealt</c> 那两本平A账 —— 平A与技能分开记，
    /// 「全关档逐字等」才谈得上；这一发只发播报，让 HUD 与行为流看得见「第几步挨了一刀技能」。
    /// </summary>
    internal void RaiseAbilityAttack(string targetId, string sourceId, int damage)
    {
        var target = Find(targetId);
        if (target is null || damage <= 0) return;
        Attacked?.Invoke(new AttackRow(Tick, sourceId, targetId, damage, target.Health <= 0));
    }

    /// <summary>终局判定：一方全灭即定（同时全灭记和局）；打满 120 秒双方还有人 ⇒ 按契约判我方赢，另记一笔超时。</summary>
    private void Judge(long tick)
    {
        var ours = AliveCount(Side.Player);
        var theirs = AliveCount(Side.Enemy);
        var byTimeout = false;
        BattleOutcome outcome;
        if (ours == 0 && theirs == 0) outcome = BattleOutcome.Draw;
        else if (theirs == 0) outcome = BattleOutcome.PlayerVictory;
        else if (ours == 0) outcome = BattleOutcome.PlayerDefeat;
        else if (tick >= MaxBattleTicks)
        {
            // design.md 「已定」第 5 条（2026-09-26 用户定）：120 秒没打完就判赢 —— 守住就是赢。
            // 这一档在满编实测里不该被走到（最快 20.0 秒、最慢 31.0 秒），走到了就是数值退化的信号，
            // 所以它照样计入胜，但 DecidedByTimeout 单独留痕，对照跑有一条测试专门盯它。
            outcome = BattleOutcome.PlayerVictory;
            byTimeout = true;
        }
        else outcome = BattleOutcome.Running;

        if (outcome == BattleOutcome.Running) return;

        Outcome = outcome;
        DecidedByTimeout = byTimeout;
        OutcomeTick = tick;
        Phase = BattlePhase.Settled;   // 结算面板读这一档；摆子口从此也不再管事
        BattleEnded?.Invoke(new EndRow(tick, outcome, Deaths(Side.Player), Deaths(Side.Enemy)));
    }

    // === 摆兵 ===

    /// <summary>按摆法入场：出场顺序 = 行 0→2、列 0→4；随机数也按这个顺序抽，所以同种子同序列。</summary>
    private void Populate(Formation formation, Side side)
    {
        // M8：这一族穿谁（摆法没写就照对阵口径），数值一律从兵种卡取 —— 世界这里不认识「种族规则」这四个字，
        // 规则已经在 UnitCard.Stats 里施加过了。换一条规则改的是那张表，不是这一行。
        var race = formation.Race ?? UnitCards.RaceOf(side);
        var seq = 0;
        foreach (var placement in formation.Placements)
        {
            seq++;
            var card = UnitCards.Of(race, placement.Kind).Stats;
            var home = BattleContract.CellCenter(side, placement.Row, placement.Column);
            var position = home;
            var speed = card.Speed;
            var interval = card.AttackIntervalTicks;

            if (_jitter)
            {
                position = BattleContract.ClampToArena(new Vector2(
                    home.X + Jitter(JitterPositionMeters),
                    home.Y + Jitter(JitterPositionMeters)));
                speed *= 1f + Jitter(JitterSpeedRatio);
                interval = Math.Max(1, interval + JitterInt(JitterIntervalTicks));
            }

            var unit = new BattleUnit
            {
                Seq = seq,
                Side = side,
                Race = race,
                Kind = placement.Kind,
                Id = $"{(side == Side.Player ? "p" : "e")}{seq:D2}",
                Card = card,
                HomeCell = home,
                Speed = speed,
                IntervalTicks = interval,
                Position = position,
                MaxHealth = card.Health,
                Health = card.Health,
                CooldownRemaining = 0,
            };
            _units.Add(unit);
            // 上册：id 字符串仍由本类拼（`p01` 进了 Journal 与重放基线，格式一个字不动，D8），
            // 册子发的是它那一份序号、并把「谁在场、按什么次序」收到一处。
            // 载荷就是兵自己 —— 册子存而不读（D7），查它走 Find。
            _entities.Spawn(unit.Id, UnitKind, unit);
        }
    }

    /// <summary>±magnitude 的浮点抖动（一次一发 NextDouble，抽取顺序写死 ⇒ 同种子逐字同序列）。</summary>
    private float Jitter(float magnitude) => (float)((_rng.NextDouble() * 2 - 1) * magnitude);

    /// <summary>−magnitude ~ +magnitude 的整数抖动。</summary>
    private int JitterInt(int magnitude) => _rng.Next(-magnitude, magnitude + 1);

    // === 观测口之「状态可查」：显示即真相 ===

    /// <summary>
    /// 一发读出全量真值 —— 每个兵的血量、坐标、目标、冷却、造成伤害，外加「他为什么没动」。
    /// 遍历顺序 = 出场顺序，浮点走 "R" 全精度：确定性哨兵测试直接拿它逐字比对。
    /// </summary>
    public string DebugSnapshot()
    {
        var culture = CultureInfo.InvariantCulture;
        // rules= 这一格是 M8 加的：场上到底带着哪几条种族规则，从兵身上现读（不是照抄对阵口径）——
        // 两侧同族的归因局只会念一条，异族局念两条。读数与结算同一份数据（显示即真相）。
        var rules = string.Join('、', _units.Select(u => u.Race).Distinct().Select(RaceRules.Describe));
        var lines = new List<string>
        {
            $"tick={Tick} seconds={F2(Tick / (10.0f))} phase={Phase} outcome={Outcome} decidedAt={(OutcomeTick?.ToString(culture) ?? "-")} " +
            $"by_timeout={(DecidedByTimeout ? 1 : 0)} " +
            $"ours={AliveCount(Side.Player)}/{Fielded(Side.Player)} theirs={AliveCount(Side.Enemy)}/{Fielded(Side.Enemy)} " +
            $"jitter={(_jitter ? "on" : "off")} rules={rules}",
        };
        foreach (var unit in _units)
        {
            lines.Add($"{unit.Id} {unit.Side}/{unit.Race} {unit.Kind}「{unit.Look.DisplayName}」 " +
                      $"hp={unit.Health}/{unit.MaxHealth} atk={unit.Card.Attack} rng={F(unit.Card.Range)} " +
                      $"pos={unit.PositionFull} home=({F(unit.HomeCell.X)},{F(unit.HomeCell.Y)}) " +
                      $"cd={unit.CooldownRemaining}/{unit.IntervalTicks} speed={F(unit.Speed)} " +
                      $"dealt={unit.DamageDealt}/{unit.AttacksMade} target={unit.TargetId ?? "-"} why={WhyFor(unit)}");
        }

        // [实体层] 这一段永远在场（构造期就摆满了，战斗中零生成零删除）——
        // 格式化是册子那一处生成的，本类只按行搬，一个数都不重算。
        // 它答的是「谁在场、第几个、哪一类」；上面那段答的是「他血多少、站哪、打过谁」，两件事。
        lines.Add("[实体层]");
        lines.AddRange(EntityRows());

        // [布阵棋盘] 这一段只有挂了棋盘才念（单挑局与预设摆法那两档都没有 ⇒ 既有逐字基线一个字都不用重录）。
        // 摆子口那三条写路径的效果就念在这里：棋盘是布阵期的真值。
        if (_board is not null)
        {
            lines.Add("[布阵棋盘]");
            lines.AddRange(BoardRows());
        }

        // [建造层] 这一段只有场上真有路障时才念（模块那一份格式化，本类只按段搬 token）。
        // 路障缺省是零放置 ⇒ 这一段一个字都不念，既有逐字基线一个字都不用重录。
        if (_construction.Count > 0)
        {
            lines.Add("[建造层]");
            lines.AddRange(ConstructionRows());
        }

        // [技能层] 这一段只有接了 H3 才出现（全关档这一行都不念 ⇒ 既有逐字基线一个字都不用重录）。
        // 它是层自己的那份快照，不是第二套读数：世界的 hp= 与层里 b;hp; 那两行读的是同一笔账（写回只有一处）。
        if (Abilities is { } abilities)
        {
            lines.Add("[技能层]");
            lines.Add(abilities.Snapshot());
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 重放用的那份快照 —— 与 <see cref="DebugSnapshot"/> 同源，只是<b>排成重放契约要求的段格式</b>：
    /// 段间 <c>" | "</c>、头部以 <c>tick=N</c> 起、每个实体段前两个 token 是身份（类型 + Id）。
    /// 为什么要两份排版而不是两份真值：取证器的 L2 语义档（良性改动不逼全红）只认这个格式
    /// （AGENTS 「断言三层」），而读数这件事仍然只有 <see cref="DebugSnapshot"/> 那一处。
    /// 没接技能时它照样只念世界那几段 —— 级 1 与级 2 比的从来不是排版，是同一串真值。
    /// </summary>
    public string ReplaySnapshot()
    {
        var segments = new List<string>
        {
            $"tick={Tick} seconds={F2(Tick / (10.0f))} phase={Phase} outcome={Outcome} " +
            $"ours={AliveCount(Side.Player)}/{Fielded(Side.Player)} theirs={AliveCount(Side.Enemy)}/{Fielded(Side.Enemy)}",
        };
        foreach (var unit in _units)
        {
            segments.Add($"u {unit.Id} side={unit.Side} kind={unit.Kind} hp={unit.Health}/{unit.MaxHealth} " +
                         $"pos={unit.PositionFull} cd={unit.CooldownRemaining} dealt={unit.DamageDealt}/{unit.AttacksMade}");
        }

        // [实体层] 那几段（摘要 + 每条一段）。**本世界自己的段首一个字没动** ——
        // 上面那些 `u {id}` 段是大写小写各原型自己排的既有重放基线（D15），改它要重录基线。
        foreach (var row in EntityRows())
        {
            segments.Add(row);
        }

        // [布阵棋盘] 那几段（摘要 + 每格一段，段首两个 token 恒为 C + 格子号）。没挂棋盘时一段都不加。
        foreach (var row in BoardRows())
        {
            segments.Add(row);
        }

        // [建造层] 那几段（摘要 + 每座一段，段首两个 token 恒为 B + 建筑 id）。零放置时一段都不加。
        foreach (var row in ConstructionRows())
        {
            segments.Add(row);
        }

        if (Abilities is { } abilities)
        {
            // 层的行形状是 `类型;字段;…`，并进段格式时**整行的分号都换成空格**（不是只换第一个）——
            // 只换第一个会把后半截留在同一个 token 里，L2 语义档就把整行当「段首身份」比，
            // 「给层加一列」这种良性改动在 L2 也会全红（账本 N-19，2026-09-27 实测；A53）。
            // 换空格的前提是层文本里没有带空格的值：字段全是名字、枚举、数字与逗号表。
            foreach (var row in abilities.Snapshot().Split('\n'))
            {
                if (row.Length == 0) continue;
                segments.Add(row.Replace(';', ' '));
            }
        }
        return string.Join(" | ", segments);
    }

    /// <summary>
    /// 实体册那一段的行 —— <b>格式化由册子那一处生成</b>，本类只按段搬 token，一个数都不重算
    /// （与 <see cref="ConstructionRows"/> 同一形状）。
    /// 第一行是摘要（<c>entity count=… issued=…</c>），之后每条一段、<b>段首两个 token 就是身份</b>
    /// （<c>{id} seq=…</c>）——「断言三层」L2 语义档拿段首两个 token 当身份，
    /// 而 id 与序号在同种子重跑下逐字不变，所以良性改动不误红、真漂移一定红。
    ///
    /// <para>与本世界那几段 <c>u {id}</c> 的分工：那几段答「他血多少、站哪、打过谁」，
    /// 这一段答「谁在场、第几个、哪一类」。两段不合成一段——册子不持有血量（D2），
    /// 本类也不知道发过几个 id。</para>
    /// </summary>
    private List<string> EntityRows()
    {
        var rows = new List<string>();
        foreach (var line in _entities.DebugSnapshotSegment().Split('\n'))
        {
            if (line.Length == 0) continue;
            rows.Add(string.Join(" ", line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }

        return rows;
    }

    /// <summary>
    /// 布阵棋盘那一段的行 —— 摆子口写进去的东西要在重放快照里读得出来（没挂棋盘就一行都不念）。
    ///
    /// <para>第一行是摘要（<c>board kind=deploy cells=… purse=… spent=…</c>），
    /// 之后每格一行、段首两个 token 恒为 <c>C</c> + 格子号 ——「断言三层」 L2 语义档
    /// 拿段首两个 token 当身份，所以格子号写在第二个 token 上，数量与钱写在后面的键上。</para>
    ///
    /// <para>零默认那一档：单挑局与预设摆法两档没有棋盘 ⇒ 一段都不念，
    /// 既有逐字基线（<c>duel-rift-h3</c> 那两份）一个字都不用重录。</para>
    /// </summary>
    private List<string> BoardRows()
    {
        var rows = new List<string>();
        if (_board is null) return rows;

        rows.Add($"board kind=deploy cells={_board.Filled}/{DeploymentBoard.CellCount} " +
                 $"purse={_board.Remaining}/{_board.StartingPurse} spent={_board.Spent}");
        foreach (var placement in _board.Placements)
        {
            rows.Add($"C r{placement.Row}c{placement.Column} kind={BattleContract.KeyOf(placement.Kind)}");
        }

        return rows;
    }

    /// <summary>
    /// 建造层那一段的行 —— <b>格式化由模块那一处生成</b>，本类只按段搬 token，一个数都不重算。
    /// 第一行是摘要（<c>construction kind=… buildings=… occupied=… bounds=…</c>），
    /// 之后每座建筑一行、段首两个 token 恒为 <c>B</c> + 建筑 id（「断言三层」 L2 语义档的身份判据）。
    ///
    /// <para><b>零放置回空表</b>：路障一座都没有时，调试快照与重放快照都不念这一段 ——
    /// 缺省档就是「场上没有建筑」，既有逐字基线因此一个字都不用重录。</para>
    /// </summary>
    private List<string> ConstructionRows()
    {
        var rows = new List<string>();
        if (_construction.Count == 0) return rows;

        var first = true;
        foreach (var line in _construction.DebugSnapshotSegment().Split('\n'))
        {
            if (line.Length == 0) continue;

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (first)
            {
                first = false;
                rows.Add(string.Join(" ", tokens));
                continue;
            }

            rows.Add($"B {tokens[0]} " + string.Join(" ", tokens[1..]));
        }

        return rows;
    }

    /// <summary>
    /// 「谁为什么不动」的那半句答案 —— 直接问 <see cref="BattleBrainPolicy"/>，规则与结算同源。
    /// 快照的 <c>why=</c>、行为流的「为什么」、暂停面板读的都是这一句（只有一份实现）。
    /// 布阵期单独回答：那时还没有「打谁」这回事，不许把「对面已无人」当成答案念出去
    /// —— 那是还没问，不是问出来没人。
    /// </summary>
    public string WhyFor(BattleUnit unit)
    {
        if (!unit.Alive) return $"阵亡（{unit.KilledById ?? "未知"}击杀）";
        if (Phase == BattlePhase.Deploying) return "布阵期还没开打（开局才问打谁）";
        return BattleBrainPolicy.Decide(SensesOf(unit, Find(unit.TargetId))).Why;
    }

    /// <summary>
    /// 暂停时读出的整句「谁为什么不动」（M6 要的那一句）。
    /// 它不重新判 anything：why 半句来自 <see cref="WhyFor"/>（＝规则本体），
    /// 「他这一回合认的靶子是谁、隔多远」来自同一次观测 —— 于是「为什么还不去打后排」这类问题
    /// 有答案可读，而规则仍然只有 <see cref="BattleBrainPolicy"/> 那一份。
    /// </summary>
    public string ReadoutFor(string unitId)
    {
        if (Find(unitId) is not { } unit)
        {
            var ids = string.Join(' ', _units.Take(6).Select(u => u.Id));
            return $"查无此兵：{unitId}（兵号形如 p01/e07；前几个：{ids}…）";
        }

        var target = Find(unit.TargetId);
        var where = unit.Side == Side.Player ? "我方" : "敌方";
        var standing = $"站在 ({F(unit.HomeCell.X)},{F(unit.HomeCell.Y)}) 那一格，此刻在 {unit.PositionFull}";
        if (!unit.Alive) return $"{unit.Id}「{unit.Look.DisplayName}」{where} 阵亡 —— {WhyFor(unit)}";
        // 布阵期还没问过「打谁」，所以那句「对面已没有活人」在这一档是假话（敌人明明站在对面）
        if (Phase == BattlePhase.Deploying)
            return $"{unit.Id}「{unit.Look.DisplayName}」{where} {standing}｜还没开打，没算过靶子｜为什么：{WhyFor(unit)}";
        var nearest = target is null
            ? "对面已没有活人"
            : $"最近的活人是 {target.Id}「{target.Look.DisplayName}」，隔 {F2((unit.Position - target.Position).Length())} 米（够得着的距离 {F2(unit.Card.ReachAgainst(target.Card))} 米）";
        return $"{unit.Id}「{unit.Look.DisplayName}」{where} {standing}｜{nearest}｜为什么：{WhyFor(unit)}";
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string F2(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>
    /// 本类<b>不留任何退订语句</b>：<c>Step</c> 与走位那一半（镜像同步）都在构造里登记进基类那张退订表，
    /// 地形接线与走位流水线由 <c>Assembly.Detach()</c> 交还 —— 释放那一发跑完这一份清单就齐了。
    /// </summary>
    protected override void OnDispose()
    {
    }

    /// <summary>
    /// 移除那两步的接线（D11 ②③）—— 册子只钉次序，做事的是各自的属主。
    ///
    /// <para><b>它只拿世界，不拿寻路层那一半</b>：登记期（<see cref="Populate"/>）在寻路层建好之前就
    /// 走完了，而这一步本来就只在移除时才发生，所以按 id 回头找那一半是安全的。
    /// 写成构造期就抓 <c>_navMove</c> 的话，摆兵那一条生成路就得等寻路层先建好 ——
    /// 而那会把构造次序倒过来，是一处看不见的耦合。</para>
    /// </summary>
    private sealed class BattleEntityExit : IEntityRemoval
    {
        private readonly BattleWorld _world;

        public BattleEntityExit(BattleWorld world) => _world = world;

        /// <summary>② 寻路层那一步：就地定住 + 退出站位冲突那一档。</summary>
        public void DetainInNav(EntityRecord record) => _world.DetainUnitInNav(record);

        /// <summary>
        /// ③ 宿主那一步：放旗、腾引用。
        ///
        /// <para><b>本原型这一档没有调用点</b>：战斗中零删除，只置血零不出表（D13），
        /// 所以没有「谁还拿着这个兵」要放。真有移除那一档时它从
        /// <c>_units</c> 摘掉那一行 —— 那一行就是本类最后一处持有它的地方。
        /// 次序由 <c>EntityCore.Remove</c> 钉死，迁第三步（巨兽竞技场居民收割）时才有第一条真调用。</para>
        /// </summary>
        public void Release(EntityRecord record)
        {
            if (record.Payload is BattleUnit unit) _world._units.Remove(unit);
        }
    }
}
