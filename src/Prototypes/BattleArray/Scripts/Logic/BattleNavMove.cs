#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Prototypes.BattleArray.Logic;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 排兵布阵接寻路层的<b>走位那一半</b> —— 下三个命令（原地不动 / 追实体 / 站位冲突）、
/// 只读移动解，并把技能层的定身激活检查包成 <see cref="NavMoveCheck"/> 递进 <see cref="NavStepper"/>。
///
/// <para><b>它不决定任何东西</b>：打谁、什么时候上、什么时候打是 <see cref="BattleBrainPolicy"/> 的活；
/// 本类只把「这一步该朝哪走」翻译成命令空间里的一个命令，再把移动解那一列读回世界
/// （「寻路单入口」：单位的位置与朝向只由寻路层那一份决定）。</para>
///
/// <para><b>身体半径只作为标量递进来</b>（<see cref="NavAgentSpec.RadiusMeters"/>），
/// 移速同理 —— 层里没有兵种词表，四个兵种的数由 <see cref="BattleNavGrid"/> 现读兵种卡（「寻路单入口」 后半句）。</para>
///
/// <para><b>激活检查排在上游</b>：<see cref="BuildGate"/> 把 <c>BattleAbilities.CanMove</c> 包成
/// <see cref="NavMoveCheck"/>，<see cref="NavStepper"/> 在流水线上<b>求解之前</b>问它一次
/// （激活检查看到的是标称步长）。<b>没挂技能层时全放行</b>（null 激活检查）——
/// 世界全关档的演化与快照一个字节都不变。</para>
///
/// <para><b>逐位对齐：坐标在空间局部米与世界米之间换算</b>（ADR 0009），
/// 偏移由 <see cref="BattleNavGrid.ToGrid"/> / <see cref="BattleNavGrid.ToWorld"/> 那一对承担。</para>
/// </summary>
public sealed class BattleNavMove
{
    /// <summary>前视距离（米，正有限数）—— <b>只用来取方向，不是停靠点</b>（见 <see cref="NavLookAhead"/>）。
    /// 2.0 米：每步位移最大 0.32 米，前视点总在前方若干步处，方向不会被前视点截短。</summary>
    public const double LookAheadMeters = 2.0;

    /// <summary>每步的全局扩展上限（格）—— 排兵布阵一场最多 30 个单位，20000 远远够用，
    /// 这一档给大了不会让结果变，只会让「预算触顶」那一档永远不会出现（那正是我们要的：它该由地形说话）。</summary>
    public const int StepSolveExpansions = 20000;

    /// <summary>单个请求的扩展上限（格）。</summary>
    public const int PerRequestExpansions = 4000;

    /// <summary>站位冲突那一档的阈值（米）：净位移低于它就算「被推挤住了」。
    /// 取<b>半个最大每步位移</b>（0.32 ÷ 2 = 0.16）：跑档一步 0.32 米，爬档一步 0.12 米，
    /// 同一个绝对值在两档上意思完全不同，所以它由这一局定、公共层一个缺省都不给。</summary>
    public const double MinNetStepMeters = 0.16;

    private readonly NavStepper _stepper;
    private readonly GameClock _clock;
    private readonly NavGrid _grid;
    private readonly IReadOnlyList<BattleUnit>? _units;
    private bool _attached = true;

    /// <summary>
    /// 造流水线并<b>就地订阅逻辑时钟</b>（「时钟解耦」：吃逻辑步，不吃每帧）。
    /// 流水线本身由 <paramref name="assembly"/> 建（档照抄现值，一个数都没改）。
    /// </summary>
    /// <param name="assembly">世界装配 —— 走位流水线从它那一份建，装配同时存下网格给地形接线用。</param>
    /// <param name="world">这个世界（读单位表、读技能层；两者都可为 null —— 全关档那一路）。</param>
    public BattleNavMove(WorldAssembly assembly, BattleWorld? world)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _clock = assembly.Clock;
        _grid = BattleNavGrid.Build();

        // 档照抄迁移前的逐字重写，一个数都没动（步长仍是 BattleContract.StepSeconds）。
        _stepper = assembly.BuildNavPipeline(new NavPipelineSpec
        {
            Space = BattleNavGrid.Space,
            Grid = _grid,
            Budget = new NavSolveBudget { PerRequestExpansions = PerRequestExpansions },
            Recompute = new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            CacheCapacity = 64,
            RecordCapacity = 64,
            TrackedRequestCapacity = 32,
            Move = new NavMoveTuning
            {
                StepSeconds = BattleContract.StepSeconds,
                LookAheadMeters = LookAheadMeters,
                StepSolveExpansions = StepSolveExpansions,
            },
            Gate = BuildActivationCheck(() => world?.Abilities),
            Separation = new NavSeparationTuning { MinNetStepMeters = MinNetStepMeters },
        });

        if (world is not null)
        {
            foreach (var unit in world.Units)
            {
                Register(unit);
            }
        }

        // 订阅次序就是执行次序：世界自己的 Step 先跑（它更早订阅），NavStepper 走位与站位冲突，
        // **这一发最后跑** —— 它把流水线那一份位置写回宿主那一列。
        // 排在最后是契约：世界那五段读到的必须是**这一步落定之后**的位置
        // （「状态可查」：推完一步读到的就是那一步的结果，不许是上一步的）。
        _units = world?.Units;
        _clock.Pulse += SyncAllMirrors;
    }

    /// <summary>
    /// 技能层激活检查包成寻路层那道口。<b>没挂技能层 = 全放行</b>（null 激活检查，层里全放行）。
    ///
    /// <para><b>调用顺序由这一包钉死</b>：激活检查看到的是<b>标称步长</b>（移速 × 0.1 秒），
    /// 因为它在流水线上排在求解之前 —— 「沿路径还剩多远」要等求解之后才知道（ADR 0008、「桥只转发」）。</para>
    /// </summary>
    public static NavMoveCheck? BuildGate(BattleAbilities? abilities)
        => abilities is null
            ? null
            : (string agentId, long stepIndex, double plannedMeters) =>
                abilities.CanMove(agentId)
                    ? NavMoveCheckVerdict.Allow
                    : new NavMoveCheckVerdict(false, "定身");

    /// <summary>
    /// 同一个包，递的是<b>「此刻挂着谁」那个口</b>而不是一个实例。
    ///
    /// <para><b>为什么要这一份</b>：技能层是接在世界<b>构造之后</b>的
    /// （<c>world.Abilities = …</c>），构造这一刻还没有它 ——
    /// 按实例递进来的话激活检查会永远是 null，症状是「定身挂上了却还能走」，
    /// 而那一档本该由定身说了算（「事件发射前置」：关键决策必须有事件发射，静默放行是最坏的一种）。
    /// 名字带个 <c>Of</c> 后缀而不是重载：重载的话 <c>BuildGate(null)</c> 两边都吃，
    /// 编译器当场报二义 —— 而「挂没挂技能层」正是这一格最常问的一句。</para>
    /// </summary>
    public static NavMoveCheck BuildActivationCheck(Func<BattleAbilities?> abilitiesOf)
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

    /// <summary>退订（「离树交还」：主动交还，不在调用点每次查有效性）。</summary>
    public void Detach()
    {
        if (!_attached) return;
        _stepper.Detach();
        _clock.Pulse -= SyncAllMirrors;
        _attached = false;
    }

    /// <summary>
    /// 每一步的最后把流水线那一份位置<b>全量</b>写回宿主那一列（出场顺序遍历）。
    ///
    /// <para>为什么全量而不是只写动过的那几个：漏一个位置的代价是「那个单位的坐标永远差一步」，
    /// 而症状（快照上某一行的 <c>pos=</c> 与其余行错开一步）离这一处隔着两层；
    /// 30 个单位一次循环的成本读不出来，漏写的那一类错永远读不出来。</para>
    /// </summary>
    private void SyncAllMirrors(long tick)
    {
        if (_units is null) return;
        for (var i = 0; i < _units.Count; i++)
        {
            SyncMirror(_units[i]);
        }
    }

    /// <summary>流水线本体（调试与快照读数用）。</summary>
    public NavStepper Stepper => _stepper;

    /// <summary>登记的单位 id 列表（<b>登记次序即步进次序</b>，不走字典枚举序）。</summary>
    public IReadOnlyList<string> AgentIds() => _stepper.AgentIds;

    /// <summary>这一片寻路网格（只读：它不写任何一格）。</summary>
    public NavGrid Grid => _grid;

    // === 登记与发令 ===

    /// <summary>登记一个单位。<b>数字标量由兵种卡现读</b>，层里没有词表。</summary>
    public void Register(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _stepper.Register(new NavAgentSpec
        {
            Id = unit.Id,
            RadiusMeters = BattleNavGrid.RadiusOf(unit.Kind),
            MoveSpeedMetersPerSecond = BattleNavGrid.SpeedOf(unit.Kind),
            Start = BattleNavGrid.ToGrid(unit.Position.X, unit.Position.Y),
            FacingDegrees = 0.0,   // 朝向由寻路层算（逐子步终点方向），入场给 0 只是占位
            Group = GroupOf(unit.Side),
        });
    }

    /// <summary>同组标识：<b>只有站位冲突这一档读它</b>，只推同组的。
    /// 层里不许出现「同方」那个词（那是原型的知识），组号是宿主递进去的一个标量。</summary>
    public static int GroupOf(Side side) => side == Side.Player ? 1 : 2;

    /// <summary>
    /// 下一步的命令 —— 三个命令，逐字对应工单点名的三档：
    /// <list type="bullet">
    /// <item><b>原地不动</b>（对面已无人 / 决定出手）：驻守点就是<b>它此刻站的那一点</b>
    /// （读流水线上那一份，不是世界那一列 —— 走位真值只有一份，「寻路单入口」），
    /// 容差 0 ⇒ 站着一步都不挪；</item>
    /// <item><b>追实体</b>（<see cref="BattleBrainPolicy.Intent.CloseIn"/>）：目标 id + <b>目标此刻的坐标</b>
    /// （同样读流水线那份：层里没有单位表，坐标每步由宿主把层里已有的那一数递进去）。
    /// 容差 0：<b>「够得着就停」那一档由 <see cref="BattleBrainPolicy"/> 判</b>，
    /// 层这一侧不替它藏一个「接近到几米内算到了」的缺省；</item>
    /// <item><b>站位冲突</b>：不发令，由 <see cref="NavStepper"/> 每步末尾统一避让（叠加修正，不动网格）。</item>
    /// </list></summary>
    public void IssueFor(BattleUnit unit, BattleUnit? target)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (target is null)
        {
            _stepper.Issue(unit.Id, NavCommand.Hold(_stepper.PositionOf(unit.Id)));
            return;
        }

        _stepper.Issue(unit.Id, NavCommand.Chase(
            target.Id,
            _stepper.PositionOf(target.Id),
            0.0));
    }

    /// <summary>
    /// 一名单位退场（实体册上被摘掉那一发）：<b>就地定住</b>，并退出站位冲突那一档。
    ///
    /// <para><b>定住是必须的</b>：位置真值住在流水线里，不换命令的话它会照着手上那条命令继续走
    /// —— 一个已经不在册的单位还在挪，读数是查不出来的（与 <c>InfinityLordNavMove.Retire</c>、
    /// <c>BeastNavMove.RetireMonster</c> 同一形状；那条坑三家各踩过一次，
    /// 收编进实体层之后由 <c>EntityCore.Remove</c> 的次序一次管住）。
    /// 驻守点取它此刻站的那一点，容差 0，所以那一步读数是「原地不动」、位移 0。</para>
    ///
    /// <para><b>层里没有注销口</b>（<c>BeastNavMove.RetireAbsentResidents</c> 的注释记着这一条），
    /// 所以「退注册」就这两句既有 API：换一条驻守命令 + 退出站位冲突那一档。</para>
    /// </summary>
    public void Retire(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!_stepper.HasAgent(unit.Id)) return;
        IssueFor(unit, null);
        SetCountedInSeparationOf(unit, false);
    }

    // === 每步递进来的标量与标记 ===

    /// <summary>
    /// 这一步的<b>移速标量</b>（米每秒）—— 宿主每步递一次，递的是「这一局此刻」的数：
    /// 入场抖动之后的 <see cref="BattleUnit.Speed"/> 乘技能层派生出来的倍率。
    ///
    /// <para>层里留的是<b>一个绝对数</b>，不是倍率：倍率是技能层的知识，
    /// 寻路层不留「谁被慢了几成」那种词（「寻路单入口」 后半句）。
    /// 登记时递的是卡上的数（<see cref="BattleNavGrid.SpeedOf"/>），没有它每步这一步就递的是这一格。</para>
    /// </summary>
    public void SetSpeedOf(BattleUnit unit, double metersPerSecond)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _stepper.SetMoveSpeed(unit.Id, metersPerSecond);
    }

    /// <summary>
    /// 这一趟站位冲突<b>算不算它</b>（宿主每步递一次）。
    ///
    /// <para>这一格就是排兵布阵退休前 <c>SeparateSameSide</c> 里那个
    /// <c>!a.Alive || !b.Alive</c> 的位置 —— 层里不该有「阵亡」那个词，
    /// 所以宿主递的是「这一趟算不算它」，层里只按这一列两头都不参与（「禁自造术语」、「寻路单入口」 后半句）。</para>
    /// </summary>
    public void SetCountedInSeparationOf(BattleUnit unit, bool counted)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _stepper.SetCountedInSeparation(unit.Id, counted);
    }

    /// <summary>
    /// 落位移 —— <b>带位移的那一发技能走它</b>：深度轴那一列改成
    /// <paramref name="depthAxisMeters"/>，横向那一列不动，朝向照旧。
    ///
    /// <para><b>走寻路层那个写口而不是直接改世界那一列</b>：位置的真值住在流水线
    /// （「寻路单入口」）。绕过去直接写 <c>BattleUnit.Position</c> 的后果是
    /// 「下一次同步把这一笔抹掉」—— 而抹掉之后那一发技能的位移在快照上完全看不出发生过。</para>
    /// </summary>
    public void ApplyMoveTo(BattleUnit unit, double depthAxisMeters)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var here = _stepper.PositionOf(unit.Id);
        var to = new NavPoint(here.X, depthAxisMeters - BattleNavGrid.GridOrigin);
        _stepper.ApplyMove(unit.Id, to, _stepper.SolutionOf(unit.Id).FacingDegrees);
        SyncMirror(unit);
    }

    /// <summary>
    /// 全员原地不动 —— <b>判定之后世界不再演化，位移也跟着停</b>。
    ///
    /// <para>为什么显式说一句：世界那五段在判定之后直接返回（<c>Outcome != Running</c>），
    /// 于是「不再发令」这一件就成立了；而寻路层那一次脉冲仍在跑，
    /// 手上一条 <see cref="NavCommand.Chase"/> 会让它照旧往目标走。
    /// 驻守点取<b>它此刻站的那一点</b>，容差 0 ⇒ 那一步读数是「原地不动」，位移 0。</para>
    /// </summary>
    public void HoldAll()
    {
        var ids = _stepper.AgentIds;
        for (var i = 0; i < ids.Count; i++)
        {
            _stepper.Issue(ids[i], NavCommand.Hold(_stepper.PositionOf(ids[i])));
        }
    }

    /// <summary>
    /// 把流水线那一份位置<b>写回宿主那一列</b>（<c>BattleUnit.Position</c>）。
    ///
    /// <para>宿主那一列是<b>镜像</b>，真值在流水线（「寻路单入口」）：每步开头同步一次，
    /// 之后那一段里读位置（决策、伤害、技能层的形状查询、快照）读到的都是刚落定的那一份。
    /// 换算是 <see cref="BattleNavGrid.ToWorld"/> 一处做的，调用点不另拼一遍
    /// （ADR 0009：空间不带变换，换算归宿主）。</para>
    /// </summary>
    public void SyncMirror(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var (x, y) = BattleNavGrid.ToWorld(_stepper.PositionOf(unit.Id));
        unit.Position = new Vector2(x, y);
    }

    // === 读移动解 ===

    /// <summary>这个单位最新一步的移动解（不发令时是「未下令」那一行）。</summary>
    public NavMoveSolution SolutionOf(string unitId) => _stepper.SolutionOf(unitId);

    /// <summary>把移动解那一列的位置换算回世界坐标，<b>交回宿主写进 <c>BattleUnit.Position</c></b>。</summary>
    public (float X, float Y) WorldPositionOf(string unitId)
    {
        var (x, y) = BattleNavGrid.ToWorld(_stepper.PositionOf(unitId));
        return (x, y);
    }

    /// <summary>
    /// 检查点快照段（状态级）—— <b>状态可查那个口的读数口</b>。
    /// 网格与净空那一段取 <c>NavGridSnapshot.Render</c>，本类不另渲染第二套。
    /// </summary>
    public string DebugSnapshotSegment() => _stepper.DebugSnapshotSegment();
}
