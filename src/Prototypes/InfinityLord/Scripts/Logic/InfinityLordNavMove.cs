#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 无限领主接寻路层的<b>走位那一半</b> —— 七处决策各发各的命令、只读移动解，
/// 并把每一步落定的位置写回单位那一列（那一列是镜像）。
///
/// <para><b>它不决定任何东西</b>：谁追谁、去哪家店、往哪漫步，全是 <see cref="LogicWorld"/>
/// 决策链的活；本类只把「这一步该朝哪走」翻译成命令空间里的一个命令（「寻路单入口」）。</para>
///
/// <para><b>命令映射</b>（本原型点名的两档）：
/// <list type="bullet">
/// <item><b>追实体</b>（命令空间第 3 档）：悬赏目标交战与攻坚逼近 —— 目标每步重读一次坐标递回来；</item>
/// <item><b>走到点</b>（第 2 档）：去药店 / 酒馆 / 市集、漫步、怪物进军最近的玩家建筑、逃跑那一步。</item>
/// </list>
/// 这两种之外本原型没有别的走法，「守一片区域」与「沿固定点走」都不发。</para>
///
/// <para><b>朝建筑走的那几处走的是门口点，不是楼心</b>：建筑占地是阻挡格，
/// 拿楼心当路径点时目标格被它自己挡住 —— 求解永远到不了位，单位站着不动而读数说它在走
/// （巨兽那一场实测出来的那一格）。门口点由 <see cref="InfinityLordNavGrid.DoorPoint"/> 一处给出，
/// 「到了没到」的判定读同一个数。</para>
///
/// <para><b>每步移速标量照旧由这一侧递</b>（Q4 保留那一口）：单位卡上的 <c>Speed</c> 是
/// 「米每步」，递进层里换成「米每游戏秒」，于是层里从头到尾只有一个绝对数；
/// 递出去的那一个数还要<b>乘技能层派生出来的倍率</b>（<see cref="SpeedScaleOf"/>，没挂技能层时恒 1.0）。</para>
///
/// <para><b>激活检查排在求解之前</b>（「移动激活检查」）：<see cref="BuildActivationCheck"/> 把技能层的 <c>CanMove</c>
/// 包成 <see cref="NavMoveCheck"/>，寻路流水线<b>先问允不允许动、再算站哪</b>。
/// 没挂技能层时递 null（全放行）—— 层那一半空转时走位与退休前逐字相同。</para>
///
/// <para><b>次序是契约</b>：世界那一段先跑（决策链发令、递标量），
/// <c>NavStepper</c> 那一次脉冲接着跑（照命令走位），镜像同步<b>最后跑</b> ——
/// 排在最后是契约：下一步的决策读到的必须是这一步走完之后的位置。</para>
/// </summary>
public sealed class InfinityLordNavMove
{
    /// <summary>前视距离（米）—— <b>只用来取方向，不是停靠点</b>。
    /// 2.0 米：战场上最大的每步位移是 0.16 米，前视点总在前方若干步处。</summary>
    public const double LookAheadMeters = 2.0;

    /// <summary>
    /// 每步的全局扩展上限（格）。战场总共 8800 格，而一轮长跑里同屏可以有一百多只怪物 ——
    /// 每只各求一条横跨半张图的路，最坏情况能扫掉好几万格。取 <b>6 个战场大小</b>：
    /// 给大了不会改结果，只会让「预算触顶」那一档永不出现（那正是我们要的：它该由地形说话）。
    /// </summary>
    public const int StepSolveExpansions = 60000;

    /// <summary>
    /// 单个请求的扩展上限（格）。取 <b>2 个战场大小 + 余量</b>：A* 的启发是欧氏距离，
    /// 绕障时同一批格会被反复入队，最坏情况下的展开格数可以数倍于路径长度 ——
    /// 取小了会让「绕远路能到」被读成「地形无路」，而那两个读数在这一层长得一模一样。
    /// </summary>
    public const int PerRequestExpansions = 20000;

    /// <summary>路径缓存容量（条）。同屏单位数与巢穴数同量级，一屏之外的重解由缓存兜。</summary>
    public const int PathCacheCapacity = 64;

    /// <summary>求解记录账本容量（条）。</summary>
    public const int SolveRecordCapacity = 64;

    /// <summary>追踪的请求身份容量（条）。</summary>
    public const int TrackedRequestCapacity = 32;

    /// <summary>
    /// 目标移动超过多少米算「目标动了」（米）。0.5 米 = 半个格长：
    /// 追实体那一档每步都在动，阈值取小了会让重算账本每步翻一页。
    /// </summary>
    public const double GoalMoveToleranceMeters = 0.5;

    /// <summary>没到位的步数上限（步）；超了记一次重算触发（读数用，不改走法）。</summary>
    public const int NotArrivedStepCap = 30;

    /// <summary>
    /// 到达容差（米）—— 追实体与走到点两档用同一个数。
    ///
    /// <para><b>为什么不是「= 攻击射程」</b>（预登记那一档）：容差是<b>提前量</b>，
    /// 单位可以在离目标点还有这么远的地方就判到达。射程那一档在本场能把停点拉开一大截 ——
    /// 游侠射程 6 米，走到门口点（离楼心 7 米）时它只要进 6 米就算到了 ⇒
    /// 最远可以停在离楼心 13 米处，而「够不够得着这座楼」那条线是 8 米 ⇒
    /// 英雄站在街上判定它没到、它自己判定到了，两边都停手。
    /// 0.4 米比最小的一步位移（0.08 米）大、比门口点与到达线之间的 1 米余量小，
    /// 于是「走到了」蕴含「够得着」。实测数字见 ticket 的 Comments。</para>
    /// </summary>
    public const double ArriveToleranceMeters = 0.4;

    private readonly NavStepper _stepper;
    private readonly NavGrid _grid;
    private readonly Func<string, Building?> _buildingOf;
    private readonly Func<Building, double> _radiusOf;
    private readonly Func<string, double>? _speedScaleOf;
    private bool _attached = true;

    /// <summary>
    /// 造流水线并<b>就地订阅逻辑时钟</b>（「时钟解耦」：吃逻辑步，不吃每帧）。
    ///
    /// <para>两个查法由世界递进来（层里没有建筑表，本类也不留第二份）：按 id 取建筑、
    /// 取一座楼的占地半径。半径是蓝图数据（D12/D33），本类不自己抄一个 6 米。</para>
    ///
    /// <para><see cref="NavMoveCheck"/> 与 <c>speedScaleOf</c> 是技能层那两个口子：
    /// 激活检查排在求解之前，移速倍率每步递一次。两者都没挂时（null）这一层的形状不变。</para>
    ///
    /// <para><b>走位流水线与地形接线都由装配建</b>：时钟与网格从装配身上取（同一个时钟 ⇒
    /// 位移与决策落在同一步上），档里的每一个数字照抄本类那些常量，一个都不在这里重写。</para>
    /// </summary>
    /// <param name="assembly">世界装配。时钟走它那一个，地形接线也建在它那张网格上。</param>
    /// <param name="buildingOf">按 id 取建筑（只读它的坐标）。</param>
    /// <param name="radiusOf">取一座楼的占地半径（米，蓝图数据）。</param>
    /// <param name="gate">移动激活检查（技能层那一份）；null = 全放行。</param>
    /// <param name="speedScaleOf">这一步的移速倍率（技能层派生的那一列）；null = 恒 1.0。</param>
    public InfinityLordNavMove(
        WorldAssembly assembly,
        Func<string, Building?> buildingOf,
        Func<Building, double> radiusOf,
        NavMoveCheck? gate = null,
        Func<string, double>? speedScaleOf = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(buildingOf);
        ArgumentNullException.ThrowIfNull(radiusOf);

        _buildingOf = buildingOf;
        _radiusOf = radiusOf;
        _speedScaleOf = speedScaleOf;
        _grid = InfinityLordNavGrid.Build();

        _stepper = assembly.BuildNavPipeline(new NavPipelineSpec
        {
            Space = InfinityLordNavGrid.Space,
            Grid = _grid,
            Budget = new NavSolveBudget { PerRequestExpansions = PerRequestExpansions },
            Recompute = new NavRecomputeTuning
            {
                GoalMoveToleranceMeters = GoalMoveToleranceMeters,
                NotArrivedStepCap = NotArrivedStepCap,
            },
            CacheCapacity = PathCacheCapacity,
            RecordCapacity = SolveRecordCapacity,
            TrackedRequestCapacity = TrackedRequestCapacity,
            Move = new NavMoveTuning
            {
                StepSeconds = 0.1,
                LookAheadMeters = LookAheadMeters,
                StepSolveExpansions = StepSolveExpansions,
            },
            Gate = gate,       // 移动激活检查：技能层的 CanMove（BuildActivationCheck 那一份包法）
            Separation = null, // 本 ticket不开站位冲突：退休前单位之间不互推，开了就是新机制
            Events = null,
        });

        // 建造 → 寻路的地形接线：圆 → 格的光栅化在宿主侧（ADR 0009「换算归宿主」）。
        // 建造格查法不递：本原型不挪建筑，那一格只服务 BuildingMoved。
        Terrain = assembly.AttachTerrain(c => new NavCell(c.Col, c.Row), NavCellsOfBuilding);
    }

    /// <summary>
    /// 建造 → 寻路的地形接线（读数口：<c>StampedCount</c> / <c>Missed</c> / <c>CellsOf</c>）。
    /// 接在建造模块那个事件出口上是<b>构造那一刻</b>的事（装配里那一步）：
    /// 接上时的游标记在当下，之前发过的事件不回放 ——
    /// 世界在构造里就把种子那几座登记完，接线要排在它之前，否则那几座永远落不下阻挡。
    /// </summary>
    public NavTerrainLink Terrain { get; }

    /// <summary>流水线本体（调试与快照读数用）。</summary>
    public NavStepper Stepper => _stepper;

    /// <summary>这一片寻路网格（只读：本类不写任何一格，落印子只走 <see cref="Terrain"/>）。</summary>
    public NavGrid Grid => _grid;

    /// <summary>退订（「离树交还」：主动交还，不在调用点每次查有效性）。</summary>
    public void Detach()
    {
        if (!_attached) return;
        _stepper.Detach();
        Terrain.Detach();
        _attached = false;
    }

    /// <summary>登记次序那一列（<b>登记次序即步进次序</b>，不走字典枚举序）。</summary>
    public IReadOnlyList<string> AgentIds => _stepper.AgentIds;

    /// <summary>
    /// 登记表的一行 —— <b>存档那一半要的东西</b>。
    ///
    /// <para><b>位置与朝向存空间局部米</b>（网格米），不存世界坐标：位置真值住在流水线里
    /// （「寻路单入口」），世界坐标是它算出来给镜像用的那一份。换算是纯平移
    /// （<see cref="InfinityLordNavGrid.ToGrid"/> / <c>ToWorld</c>），存哪一头都逐字还原得回来，
    /// 但存真值那一头才不用在读档时反推一次。</para>
    /// </summary>
    /// <param name="Id">单位 id。</param>
    /// <param name="X">空间局部米 X。</param>
    /// <param name="Z">空间局部米 Z。</param>
    /// <param name="FacingDegrees">朝向（度）。</param>
    public readonly record struct NavAgentPose(string Id, double X, double Z, double FacingDegrees)
    {
        /// <summary>手上那条命令（还没发过令是 null）。<b>权威状态：要存</b>。</summary>
        public NavCommand? Command { get; init; }
    }

    /// <summary>
    /// 登记表按登记次序整张念出来 —— 存档那一半读的就是它。
    ///
    /// <para><b>已退场的单位也在表里</b>：步进器没有注销口，阵亡的单位只是被「定住」，
    /// 仍占一行。漏掉它们的话，读档之后这张表会一次比一次短 —— 存档读回来再存一遍，
    /// 两串就不相等了，而那种差异看不出是漏了谁。</para>
    /// </summary>
    public IReadOnlyList<NavAgentPose> Roster()
    {
        var ids = _stepper.AgentIds;
        var rows = new List<NavAgentPose>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var solution = _stepper.SolutionOf(ids[i]);
            rows.Add(new NavAgentPose(ids[i], solution.Position.X, solution.Position.Z, solution.FacingDegrees)
            {
                Command = _stepper.CommandOf(ids[i]),
            });
        }

        return rows;
    }

    /// <summary>
    /// 按存档那一行重建一个登记 —— <b>读档路径专用</b>，出生落场那条路仍走 <see cref="Register"/>。
    ///
    /// <para><b>为什么单独开一格</b>：活着的单位那条路拿得到 <see cref="Unit"/>（移速、起始点都在它身上），
    /// 已退场的那些只剩存档里这一行。开一格统一两条路，读档时不必按「还在不在 <c>_units</c> 里」
    /// 分两套写法 —— 分了两次就会有一处忘了同步。</para>
    /// </summary>
    /// <param name="pose">存档那一行。</param>
    /// <param name="countedInSeparation">这一趟避让算不算它（已退场的那个为 false）。</param>
    public void RegisterRestored(NavAgentPose pose, bool countedInSeparation)
    {
        NavCheck.Text(pose.Id, nameof(pose));
        _stepper.Register(new NavAgentSpec
        {
            Id = pose.Id,
            RadiusMeters = InfinityLordNavGrid.BodyRadiusMeters,
            // 移速是派生档：发令口每步把它递进来（那是移速标量唯一的写口），
            // 所以登记这一格先给 0，下一步就被那一条覆盖掉。
            MoveSpeedMetersPerSecond = 0.0,
            Start = new NavPoint(pose.X, pose.Z),
            FacingDegrees = pose.FacingDegrees,
        });
        _stepper.SetCountedInSeparation(pose.Id, countedInSeparation);

        // 命令与指纹一起放回去。少放任何一样都会静默分叉：
        // 只放命令不放手上的指纹 → 第一步去重判「与手上那条一样」而省掉重发，
        // 但层里那条命令是新发的对象，折线判据（同一性）已经不成立；
        // 只放指纹不放命令 → 第一步判「一样」而省掉，于是这个单位永远拿不到命令，站在原地。
        if (pose.Command is { } command)
        {
            _stepper.Issue(pose.Id, command);
            _lastCommand[pose.Id] = FingerprintOf(command);
        }
    }

    /// <summary>登记了几个单位。</summary>
    public int AgentCount => _stepper.AgentCount;

    /// <summary>这个 id 在流水线上登记过没有。</summary>
    public bool HasAgent(string agentId) => _stepper.HasAgent(agentId);

    // === 登记与退场 ===

    /// <summary>登记一个单位：起始点就是它落场那一点，移速照卡上的数换成「米每游戏秒」。</summary>
    /// <param name="unit">刚落场的单位（<c>Position</c> 已是出生点）。</param>
    public void Register(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var at = InfinityLordNavGrid.ToGrid(unit.Position.X, unit.Position.Y);
        _stepper.Register(new NavAgentSpec
        {
            Id = unit.Id,
            RadiusMeters = InfinityLordNavGrid.BodyRadiusMeters,
            MoveSpeedMetersPerSecond = MoveSpeedOf(unit),
            Start = at,
            FacingDegrees = 0.0,
        });
    }

    /// <summary>
    /// 一名单位退场（阵亡 / 离队）：<b>就地定住</b>，并退出站位冲突那一档。
    ///
    /// <para>定住是必须的：位置真值住在流水线里，不换命令的话它会照着手上的那条命令继续走
    /// —— 一个已经不在世界里的单位还在挪，读数是查不出来的。</para>
    /// </summary>
    /// <param name="unit">退场的那一个。</param>
    public void Retire(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!HasAgent(unit.Id)) return;
        Issue(unit, NavCommand.Hold(_stepper.PositionOf(unit.Id)));
        _stepper.SetCountedInSeparation(unit.Id, false);
    }

    /// <summary>
    /// 技能层激活检查包成寻路层那道口。<b>没挂技能层 = 全放行</b>（null 激活检查，层里全放行）。
    ///
    /// <para><b>调用顺序由这一包钉死</b>（「移动激活检查」）：激活检查排在流水线的求解<b>之前</b>，
    /// 所以它看到的是<b>标称步长</b>（移速 × 0.1 秒）—— 「沿路径还剩多远」要等求解之后才知道。</para>
    ///
    /// <para><b>为什么递的是「此刻挂着谁」那个口</b>：技能层是接在世界构造之后
    /// （<c>world.Abilities = …</c>），构造这一刻它还没有 —— 按实例递进来的话激活检查会永远是 null，
    /// 症状是「定身挂上了却还能走」。名字带 <c>Of</c> 后缀而不是重载：重载的话
    /// <c>BuildActivationCheck(null)</c> 两边都吃，编译器当场报二义。</para>
    /// </summary>
    public static NavMoveCheck BuildActivationCheck(Func<InfinityLordAbilities?> abilitiesOf)
    {
        ArgumentNullException.ThrowIfNull(abilitiesOf);
        return (string agentId, long stepIndex, double plannedMeters) =>
        {
            var abilities = abilitiesOf();
            return abilities is null || abilities.CanMove(agentId)
                ? NavMoveCheckVerdict.Allow
                : new NavMoveCheckVerdict(false, "定身");
        };
    }

    /// <summary>
    /// 单位卡上的移速直接就是「米每游戏秒」—— <b>不乘也不除十倍</b>。
    ///
    /// <para>卡上那个数（<see cref="Unit.Speed"/>）在退休前那一版里是「每步位移」，
    /// 而一个逻辑步正好是 0.1 秒（「时间基准」），所以「每步位移」与「米每游戏秒」是同一个数的两种说法
    /// （1.6 米每秒 = 0.16 米每步，反过来说 1.6 每步 = 16 米每秒 —— 那是打字打错才会有的兵）。
    /// 层里的步长按 <c>移速 × 0.1 秒</c> 算，所以递进来的必须是米每秒：递「每步」那个数就成了十倍速。</para>
    ///
    /// <para><b>再乘技能层那一列倍率</b>（<see cref="SpeedScaleOf"/>）：减速与加速是技能层的知识，
    /// 寻路层只收一个绝对数。没挂技能层时倍率恒 1.0，递出去的就是卡上那个数。</para>
    /// </summary>
    private double MoveSpeedOf(Unit unit) => unit.Speed * SpeedScaleOf(unit);

    /// <summary>这一步的移速倍率（没挂技能层时恒 1.0；本版没有任何效果改它）。</summary>
    private double SpeedScaleOf(Unit unit) => _speedScaleOf?.Invoke(unit.Id) ?? 1.0;

    // === 发令（决策链的那几处 + 逃跑那一步） ===
    //
    // 命令只有一个写入口：Issue（递命令 + 更新指纹）。去重那一档 HoldOrIssue 也走它 ——
    // 让每个调用点自己记得更新指纹，漏一处的症状是「世界发了令而单位不动」。

    /// <summary>
    /// 追击一个<b>会动的</b>目标（悬赏目标交战）：目标坐标每步由这一侧重读后递进去，
    /// 层里不缓存也不外推（那一档的口径）。
    /// </summary>
    /// <param name="unit">追的那一个。</param>
    /// <param name="target">被追的那一个（它这一步的坐标就是目标点）。</param>
    public void ChaseUnit(Unit unit, Unit target)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(target);
        if (!Ready(unit)) return;

        HoldOrIssue(unit, NavCommand.Chase(
            target.Id,
            InfinityLordNavGrid.ToGrid(target.Position.X, target.Position.Y),
            ArriveToleranceMeters));
    }

    /// <summary>
    /// 追一座<b>不动的</b>建筑（攻坚 / 怪物进军）：目标是这座楼朝自己这一侧的<b>墙脚</b>。
    ///
    /// <para><b>为什么不是「离楼心半径 + 1 米」那个门口点</b>：攻坚与袭城的停手线是
    /// 「离楼心 ≤ 攻击射程 + 1 米」（怪物 2.2 米），而楼自己的占地半径是 6 米 ——
    /// 停手线整条落在楼里。路径点若摆在半径 + 1 米处，单位走到那儿就判到位，
    /// 而它离楼心 7 米 ＞ 2.2 米 ⇒ <b>永远走不到能出手的地方</b>，
    /// 读数是「它到了」而城池一滴血都不掉（实测：一局 6000 步七座楼全是满血）。
    /// 把路径点摆到墙脚，两条线才接得上：走到墙脚 ⇒ 站进停手线 ⇒ 出手。</para>
    /// </summary>
    /// <param name="unit">逼近的那一个。</param>
    /// <param name="building">目标建筑。</param>
    public void ChaseBuilding(Unit unit, Building building)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(building);
        if (!Ready(unit)) return;

        HoldOrIssue(unit, NavCommand.Chase(
            building.Id,
            EdgeGoal(building, unit.Position),
            ArriveToleranceMeters));
    }

    /// <summary>
    /// 走到一个固定点（漫步）。
    /// <b>容差 0</b>：「到了」就是站在那个点上，于是既有那几处判「到没到」的阈值
    /// （进店 2 米、袭城 射程 + 1 米）判的就是移动解交回的那个落点。
    /// </summary>
    /// <param name="unit">走的那一个。</param>
    /// <param name="target">落点（世界坐标）。</param>
    public void GoToPoint(Unit unit, Vector2 target)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!Ready(unit)) return;

        HoldOrIssue(unit, NavCommand.GoTo(
            InfinityLordNavGrid.ToGrid(target.X, target.Y),
            0.0));
    }

    /// <summary>走到离这座楼够得着的那一点（去药店 / 酒馆 / 市集：到了就买）。</summary>
    /// <param name="unit">走的那一个。</param>
    /// <param name="building">要去的那座楼。</param>
    public void GoToBuilding(Unit unit, Building building)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(building);
        if (!Ready(unit)) return;

        HoldOrIssue(unit, NavCommand.GoTo(EdgeGoal(building, unit.Position), 0.0));
    }

    /// <summary>
    /// 背向 <paramref name="from"/> 走一步（0.1 秒）—— 逃跑那条支路的落点走这一口。
    ///
    /// <para><b>为什么走命令空间</b>：位置真值住在寻路流水线里，直写 <c>unit.Position</c> 会被
    /// 这一步末尾的镜像同步抹掉 —— 读数看着像挪了，下一步它还在原处，
    /// 症状是「该跑的时候站着不动」。</para>
    ///
    /// <para><b>落点先按「站得下」挑</b>：背离方向上那一点可能落在网格外、或落进某座楼的占地里
    /// （建筑占地是阻挡格）。那一档求解报「地形无路」，症状与「没发令」一样，
    /// 所以这里照 <see cref="InfinityLordNavGrid.DoorPoint"/> 那一套逐档找第一个站得下的点。</para>
    ///
    /// <para><b>方向退化成零</b>（与目标重叠到 1 毫米以内）时发一条原地不动：背离方向无从谈起，
    /// 而留着手上那条命令可能是「追上去」。</para>
    /// </summary>
    /// <param name="unit">跑的那一个。</param>
    /// <param name="from">背对的那一点（世界坐标，追杀它的那个目标）。</param>
    public void StepAwayFrom(Unit unit, Vector2 from)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!Ready(unit)) return;

        // 起点读层里那一份（位置真值只有这一份），不读单位那一列镜像。
        var (x, z) = InfinityLordNavGrid.ToWorld(_stepper.PositionOf(unit.Id));
        var here = new Vector2((float)x, (float)z);
        var away = here - from;
        if (away.LengthSquared() <= 1e-6f)
        {
            HoldInPlace(unit);
            return;
        }

        var landing = FleeLanding(here, Vector2.Normalize(away));
        HoldOrIssue(unit, NavCommand.GoTo(InfinityLordNavGrid.ToGrid(landing.X, landing.Y), 0.0));
    }

    /// <summary>
    /// 逃跑那一步的落点距离（米）—— 2.0 米 = 两格。
    ///
    /// <para><b>为什么不取「这一步能走的量」</b>：到达容差是 0，而发令口按「哪一档 + 目标格号」去重
    /// （<see cref="HoldOrIssue"/>）。落点落在同一个格号里的话，下一步算出来的命令指纹与手上那条相同
    /// ⇒ 那一条被省掉，而单位正好站在那个点上（它已经到位了）⇒ 它从此一步都不动，
    /// 症状与「没发令」一样。2.0 米在<b>任何方向</b>上都至少换一格：斜着走时两个分量各 1.41 米，
    /// 都过了 1 米的格长；单位走完这一段必然换到新的一格，也就必然发得出下一条命令。</para>
    ///
    /// <para><b>为什么不更远</b>：单位每步只走移速 × 0.1 秒（本场最大 0.16 米），2.0 米要走十来步 ——
    /// 走的这段路上命令不变、手上的路照旧复用，不会每步重解；再远就与「背向目标挪一步」脱节了。</para>
    /// </summary>
    public const double FleeAwayMeters = 2.0;

    /// <summary>
    /// 逃跑落点第二档距离（米）—— 1.5 米。<b>换格的保证仍在</b>：斜着走时两个分量各 1.06 米，
    /// 都过了格长。第一档在八个方向上都站不下时才退到这一档。
    /// </summary>
    public const double FleeNearAwayMeters = 1.5;

    /// <summary>逃跑落点候选的两档距离（米）：先用第一档，八个方向都站不下再退第二档。</summary>
    private static readonly double[] FleeSearchMeters = [FleeAwayMeters, FleeNearAwayMeters];

    /// <summary>
    /// 逃跑那一步的落点：背离方向起、逐档找第一个<b>站得下</b>的点
    /// （网格内、格没被封、净空放得下身体半径 —— <see cref="InfinityLordNavGrid.Standable"/> 那一份口径）。
    ///
    /// <para><b>次序写死、结果逐字确定</b>：方向先按背离方向，再按
    /// <see cref="InfinityLordNavGrid.DoorSearchSectors"/> 那八档铺开；距离先 <see cref="FleeAwayMeters"/>、
    /// 再 <see cref="FleeNearAwayMeters"/>。同一个局面每次挑到同一个点。</para>
    ///
    /// <para><b>一个都站不下时退回最初那个候选点</b>：与门口点同一条口径 —— 照实交出去，
    /// 让层报它自己的读数（地形无路）。<b>不返回脚下那一点</b>：它的格号与手上那条命令多半相同，
    /// 会被去重判成「一样」而什么都不发。</para>
    /// </summary>
    /// <param name="here">起点（世界坐标，层里那一份）。</param>
    /// <param name="direction">背离方向（单位向量）。</param>
    private Vector2 FleeLanding(Vector2 here, Vector2 direction)
    {
        var baseAngle = Math.Atan2(direction.Y, direction.X);
        var first = here + (direction * (float)FleeAwayMeters);

        for (var ring = 0; ring < FleeSearchMeters.Length; ring++)
        {
            var distance = FleeSearchMeters[ring];
            for (var k = 0; k < InfinityLordNavGrid.DoorSearchSectors.Length; k++)
            {
                var angle = baseAngle + InfinityLordNavGrid.DoorSearchSectors[k];
                var at = here + new Vector2(
                    (float)(Math.Cos(angle) * distance),
                    (float)(Math.Sin(angle) * distance));
                if (InfinityLordNavGrid.Standable(_grid, at))
                {
                    return at;
                }
            }
        }

        return first;
    }

    /// <summary>
    /// 发令的<b>唯一那个写入口</b>：递命令，并把「手上那条命令的指纹」一起更新掉。
    ///
    /// <para><b>为什么收成一个写入口</b>：指纹是去重那一档读的那两个数（<see cref="HoldOrIssue"/>），
    /// 而改命令的口不止一个（走到点 / 追实体 / 驻守 / 退场）。哪一个口忘了更新指纹，
    /// 去重就会拿一条<b>已经不成立</b>的旧指纹做依据：世界这一步明明发了「追上去」，
    /// 同格号那一发被判「与手上那条一样」而省掉，单位站着不动
    /// （实测：树说交战、目标已死那一步先发了驻守，下一步同格号的追击命令就被省掉了）。</para>
    /// </summary>
    private void Issue(Unit unit, NavCommand command)
    {
        _stepper.SetMoveSpeed(unit.Id, MoveSpeedOf(unit));
        _stepper.Issue(unit.Id, command);
        _lastCommand[unit.Id] = FingerprintOf(command);
    }

    /// <summary>命令指纹 = 「哪一档 + 目标格号」（去重那一档读的是这两个数，口径见 <see cref="HoldOrIssue"/>）。</summary>
    private static (NavCommandKind Kind, long CellX, long CellZ) FingerprintOf(NavCommand command)
    {
        var goal = command.GoalFor(0);
        return (command.Kind, (long)Math.Floor(goal.X), (long)Math.Floor(goal.Z));
    }

    /// <summary>
    /// 发令口唯一的那一处：<b>递进来的命令与手上那条同形就不重发</b>。
    ///
    /// <para><b>为什么必须收这一道</b>（本 ticket最硬的一条实测）：决策链每步都调发令口，
    /// 于是「同一条命令」每步被重发一次。重发在层里的后果不是「换个目标再算」，
    /// 而是<b>每一步都重解一条路</b> —— 路径缓存的键含起点，而起点每步都在动，
    /// 命中率接近 0。6000 步的基线长跑里同屏一百多只怪，每步各解一条横跨半张图的路，
    /// 实测把一条 4.7 秒的用例拖到 <b>302 秒</b>（64 倍）。
    /// 收掉之后「它站在同一处、要去同一个点」这件事在层里就是一条命令，
    /// 求解只发生在命令真变的那一步。</para>
    ///
    /// <para><b>同形的判据是「哪一档 + 目标点」</b>：目标点量化到格
    /// （<see cref="InfinityLordNavGrid.ToGrid"/> 本身就是格局部米，取整到格号）——
    /// 目标在同一格里挪几厘米不算换命令。追实体的目标每步都在动，跨格才重发，
    /// 与层里「目标移动超过阈值才算动了」那条触发是同一档粒度。</para>
    ///
    /// <para><b>每步移速标量照旧每步递</b>：它不是命令的一部分（激活检查与步长读的是它），
    /// 少递一次就是「速度改了但这一步还按旧速度走」。</para>
    ///
    /// <para><b>指纹只在 <see cref="Issue"/> 那个写入口里更新</b>：省掉那一发时指纹不动
    /// （手上那条命令没变），所以下一步读到的仍是当下这一条。</para>
    /// </summary>
    private void HoldOrIssue(Unit unit, NavCommand command)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(command);

        var key = FingerprintOf(command);
        if (_lastCommand.TryGetValue(unit.Id, out var last) && last == key)
        {
            _stepper.SetMoveSpeed(unit.Id, MoveSpeedOf(unit));
            Skipped++;
            return;
        }

        Issued++;
        Issue(unit, command);
    }

    /// <summary>临时读数（本 ticket收尾即删）：真发了多少条命令。</summary>
    internal long Issued;

    /// <summary>临时读数（本 ticket收尾即删）：同一条命令省掉了多少发。</summary>
    internal long Skipped;

    /// <summary>每个单位手上那条命令的指纹（发令口去重用）。</summary>
    private readonly Dictionary<string, (NavCommandKind Kind, long CellX, long CellZ)> _lastCommand = new(StringComparer.Ordinal);

    /// <summary>某一名单位此刻的移动解（读数口）。</summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public NavMoveSolution SolutionOf(string agentId) => _stepper.SolutionOf(agentId);

    /// <summary>
    /// 这一名单位此刻是不是已经站在 <paramref name="target"/> 那一点上了 ——
    /// 漫步判「到点再挑一个」用的就是它。
    ///
    /// <para>判据是<b>层里那一条移动解的到位状态</b>（<see cref="NavMoveStatus.Arrived"/>），
    /// 不是这一侧拿两个坐标再算一遍距离：到达判定在整个模块只有一处（「寻路单入口」）。</para>
    /// </summary>
    /// <param name="agentId">判的那一名单位（没登记过时回 false，它会照旧挑一个新目标）。</param>
    /// <param name="target">那一点（世界坐标）。</param>
    public bool ArrivedAt(string agentId, Vector2 target)
    {
        if (!TrySolutionOf(agentId, out var solution))
        {
            return false;
        }

        return solution.Status == NavMoveStatus.Arrived
               && solution.Goal == InfinityLordNavGrid.ToGrid(target.X, target.Y);
    }

    /// <summary>这一名单位此刻在不在流水线上（镜像同步与调试口先问它）。</summary>
    public bool TrySolutionOf(string agentId, out NavMoveSolution solution)
    {
        if (!HasAgent(agentId))
        {
            solution = null!;
            return false;
        }

        solution = _stepper.SolutionOf(agentId);
        return true;
    }

    // === 每步：把流水线那一份位置写回单位那一列 ===

    /// <summary>
    /// 走位落定之后把位置<b>全量</b>写回单位那一列。
    ///
    /// <para>全量写而不是只写动过的那几个：漏一个的代价是「那个单位的坐标永远差一步」，
    /// 而症状（快照上某一行的 <c>pos=</c> 与其余行错开一步）离这一处隔着两层。</para>
    /// </summary>
    /// <param name="stepStart">这一步开始之前的单位名单（阵亡退场的那几个不进这一趟）。</param>
    public void SyncMirrors(IReadOnlyList<Unit> stepStart)
    {
        ArgumentNullException.ThrowIfNull(stepStart);
        for (var i = 0; i < stepStart.Count; i++)
        {
            var unit = stepStart[i];
            if (!HasAgent(unit.Id)) continue;

            var solution = _stepper.SolutionOf(unit.Id);
            var (x, z) = InfinityLordNavGrid.ToWorld(solution.Position);
            unit.Position = new Vector2((float)x, (float)z);
        }
    }

    // === 测试排布口 ===

    /// <summary>
    /// 把一名已登记单位原样放到一个绝对落点（走层的落位移那一口）—— <b>测试排布专用</b>。
    ///
    /// <para><b>为什么要有它</b>：位置真值住在流水线里，测试直接写单位那一列只在
    /// 「这一步还没走位」的窗口里有效；一旦走位落定，镜像就被写回旧点，
    /// 症状是「明明挪过去了，下一步它又回到原处」。走这一口才是把排布交给唯一那份真值。</para>
    ///
    /// <para><b>它不动命令，指纹也跟着不动</b>：落位移那一口按层里的口径不清命令，
    /// 指纹说的是「手上那条命令」，不是位置 —— 这里改指纹反而会让去重拿一条不成立的旧指纹
    /// （「把它放到这儿站着」那件事由调用方接一发 <see cref="HoldInPlace"/> 收口）。</para>
    /// </summary>
    /// <param name="unitId">要在场的单位 id。</param>
    /// <param name="at">落点（世界坐标）。</param>
    public void PlaceAt(string unitId, Vector2 at)
    {
        if (!HasAgent(unitId)) return;
        _stepper.ApplyMove(unitId, InfinityLordNavGrid.ToGrid(at.X, at.Y),
            _stepper.SolutionOf(unitId).FacingDegrees);
    }

    /// <summary>
    /// 让一名单位就地站住（命令换成「原地不动」，驻守点取它此刻站的那一点）。
    ///
    /// <para>两个调用点都用它：① 与 <see cref="PlaceAt"/> 配对（落位移那一口按层里的口径
    /// <b>不清命令</b>，所以「把它放到这儿站着」要两步 —— 少了第二步它会照着手上的旧命令走开）；
    /// ② 行为树那条线「树这一步没给意图」时 —— 不发令等于让它照旧执行上一步那条命令，
    /// 而这一档要的是真的发呆。</para>
    ///
    /// <para><b>它每次都真发一条</b>（不走去重那一档）：驻守点是「此刻脚下这一点」，
    /// 被挪过之后它与手上那条命令的驻守点就不是同一个点了。指纹照旧更新
    /// （<see cref="Issue"/> 那个写入口）—— 不更新的话，下一步一条同格号的追击命令会被判「一样」而省掉。</para>
    /// </summary>
    /// <param name="unit">要站住的那一名（没登记过时什么都不做）。</param>
    public void HoldInPlace(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!HasAgent(unit.Id)) return;
        Issue(unit, NavCommand.Hold(_stepper.PositionOf(unit.Id)));
    }

    // === 内部 ===


    /// <summary>单位还在场上、且登记过，才发得出命令（退场的那几个不再走）。</summary>
    private bool Ready(Unit unit) => unit.Health > 0 && HasAgent(unit.Id);

    /// <summary>
    /// 一座楼此刻的目标点（寻路格）：<b>楼心朝这个单位那一侧的墙脚</b>。
    ///
    /// <para>墙脚 = 楼心朝这一侧、占地半径之外站得下的那一点
    /// （<see cref="InfinityLordNavGrid.EdgePoint"/>）。它同时是两件事的落点：
    /// 单位走到这儿就够得着（<see cref="InfinityLordNavGrid.InteractionRangeMeters"/> 之内），
    /// 而攻坚与袭城的停手线（离楼心 攻击射程 + 1 米）也够得着 —— 两条线不许各算一遍。</para>
    /// </summary>
    private NavPoint EdgeGoal(Building building, Vector2 from)
    {
        var at = InfinityLordNavGrid.EdgePoint(_grid, building.Position, _radiusOf(building), from);
        return InfinityLordNavGrid.ToGrid(at.X, at.Y);
    }

    /// <summary>
    /// 一座楼朝某个方向那一侧的<b>出场点</b>（世界坐标）—— 产出链落单位用它，
    /// 判据与门口点同一套（网格内、格没被封、净空放得下身体半径）。
    ///
    /// <para><b>为什么出场点也要挑</b>：退休前那几处是「楼心 + 半径 3 的随机偏移」，
    /// 半径 3 落在半径 6 的占地里面 —— 直接照搬的话单位一出生就在阻挡格里，
    /// 求解报 <c>StartBlocked</c>，它一辈子站在原地不动（实测：整局每个单位都是这一档）。</para>
    /// </summary>
    /// <param name="building">产出的那座楼。</param>
    /// <param name="direction">朝哪一侧落（世界坐标的方向，不必归一）。</param>
    public Vector2 SpawnPoint(Building building, Vector2 direction)
    {
        ArgumentNullException.ThrowIfNull(building);
        return InfinityLordNavGrid.DoorPointAlong(_grid, building.Position, _radiusOf(building), direction);
    }

    /// <summary>圆 → 格的光栅化（<c>NavTerrainLink</c> 那个查法）。</summary>
    private IReadOnlyList<NavCell> NavCellsOfBuilding(string buildingId)
    {
        var building = _buildingOf(buildingId);
        if (building is null)
        {
            return Array.Empty<NavCell>();
        }

        return InfinityLordNavGrid.BuildingCells(_grid, building.Position, _radiusOf(building));
    }
}
