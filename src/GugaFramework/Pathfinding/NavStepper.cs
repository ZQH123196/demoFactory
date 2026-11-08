#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 移动解的流水线 —— <b>五个命令跑的是同一条</b>。
///
/// <para><b>每步的次序是契约</b>（ADR 0008、「桥只转发」：注入的路径与玩家走的路径不许漂成两回事）：
///
/// <list type="number">
/// <item><b>到达容差统一判一次</b>（<see cref="NavArrive"/>）。五档命令在第 ① 步之后<b>形状完全一样</b>：
/// 都只剩「本步该朝哪个点走」这一个答案。原地不动那一档的目标是驻守点，
/// 追实体那一档的目标是目标这一轮的坐标，沿固定点走的那一档到位就推游标。</item>
/// <item><b>移动激活检查排在上游</b>（<see cref="NavMoveCheck"/>）：先问允不允许动，再算站哪。
/// 这是流水线上<b>唯一一道</b>位移裁决点，工单 08 在那里接技能层的
/// <c>CanMove</c> / <c>MoveBlocked</c>；<b>本期层里不接技能层</b>，没递激活检查就是全放行。
/// 激活检查拒了不是静默跳过 —— 移动解交出 <see cref="NavMoveStatus.HeldByActivationCheck"/> 与那句说法。</item>
/// <item>算站哪：<see cref="NavSolver"/> 给一条路（seam 的第一个 adapter）。</item>
/// <item>取<b>方向</b>：朝前视点那个方向（<see cref="NavLookAhead"/>）。</item>
/// <item>取<b>步长</b>：本步能走的距离，上限是沿路径到终点还剩多远 ——
/// <b>与前视点在哪无关</b>（前视点只是方向来源，详见 <see cref="NavLookAhead"/>）。</item>
/// <item>切子步逐段验，落地位移与朝向。</item>
/// </list>
///
/// <para><b>吃逻辑步，不吃每帧</b>（「时钟解耦」）：订阅 <see cref="GameClock.Pulse"/>，
/// 渲染帧率只负责「推」<c>Advance()</c>。表现层按渲染帧率调 <c>Advance(1)</c> 还是
/// <c>Advance(50)</c> 与本层无关 —— 那是同一个答案的两种拿法，逐字相等。</para>
///
/// <para><b>步进次序 = 登记次序</b>，不是字典枚举序（spec「确定性靠结构」）：
/// 哈希桶的枚举序跟着插入历史走，拿它当次序就是让重放分叉的那种依赖。</para>
///
/// <para><b>对外三个口</b>：<see cref="Issue"/>（下命令）、<see cref="ApplyMove"/>（落位置，
/// 唯一的写口）、<see cref="SetCountedInSeparation"/>（这一趟避让算不算它）。
/// 另有两个<b>每步换一次的标量口</b>：<see cref="SetMoveSpeed"/>（移速）与登记时的
/// <see cref="NavAgentSpec"/>。位置与朝向的真值<b>只住在流水线这一份</b>，
/// 宿主照 <see cref="PositionOf"/> 读回去写自己的那一列（「寻路单入口」）。</para>
/// </summary>
public sealed class NavStepper
{
    private readonly NavGrid _grid;
    private readonly NavSolver _solver;
    private readonly NavMoveTuning _tuning;
    private readonly NavMoveCheck? _gate;
    private readonly NavSeparationTuning? _separation;
    private readonly NavMoveEvents? _events;
    private readonly GameClock _clock;

    /// <summary>重算判据那两个数（与求解器用的是同一个档 —— 复用判据与重算账本同源）。</summary>
    private readonly NavRecomputeTuning _recomputeTuning;

    private readonly List<Agent> _order = new();
    private readonly Dictionary<string, int> _at = new(StringComparer.Ordinal);
    private long _stepIndex;
    private int _staticVersionAtStepStart;
    private bool _attached = true;

    /// <summary>
    /// 造一个流水线并<b>就地订阅逻辑时钟</b>。不递激活检查就是全放行（本期层里不接技能层）。
    /// </summary>
    /// <param name="solver">求解器（seam 的那一个 adapter）。网格从它所属的空间里取。</param>
    /// <param name="tuning">步进档（步长秒数、前视距离、本步扩展上限）。</param>
    /// <param name="clock">逻辑时钟（只吃它的 <see cref="GameClock.Pulse"/>，不吃每帧）。</param>
    /// <param name="gate">移动激活检查那一道裁决。缺省 = 全放行；<b>工单 08 在这里递技能层那一版</b>。</param>
    /// <param name="separation">站位冲突的档。递了就开避让（<b>叠加修正，不动网格</b>）；缺省 = 不开。</param>
    /// <param name="events">事件出口。递了就发那四条（到达 / 部分路径 / 撞墙重算 / 卡住）；缺省 = 不发。</param>
    /// <exception cref="ArgumentOutOfRangeException">步进档不成形状。</exception>
    public NavStepper(
        NavSolver solver,
        NavMoveTuning tuning,
        GameClock clock,
        NavMoveCheck? gate = null,
        NavSeparationTuning? separation = null,
        NavMoveEvents? events = null)
    {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(clock);

        NavCheck.Positive(tuning.StepSeconds, nameof(tuning.StepSeconds),
            "一个逻辑步的秒数必须为正 —— 「时间基准」 是一律 0.1 秒，但速度读数是「米每游戏秒」，除以 0 得到的是一条恒为无穷的读数。");
        NavCheck.Positive(tuning.LookAheadMeters, nameof(tuning.LookAheadMeters),
            "前视距离必须为正 —— 0 前视让「朝路径前方走」这件事与不跟随读不出区别。");
        NavSolveCheck.Expansions(tuning.StepSolveExpansions, nameof(tuning.StepSolveExpansions));
        if (separation is not null)
        {
            NavCheck.NonNegative(separation.MinNetStepMeters, nameof(separation.MinNetStepMeters), "拥挤推挤的净位移阈值（米）");
        }

        _solver = solver;
        _tuning = tuning;
        _clock = clock;
        _gate = gate;
        _separation = separation;
        _events = events;
        _recomputeTuning = solver.RecomputeTuning;
        _grid = solver.Space.Grid;
        _staticVersionAtStepStart = _grid.StaticVersion;

        _clock.Pulse += OnPulse;
    }

    /// <summary>退订。游戏进程停机或换局时调一次（「离树交还」：主动交还，不在调用点每次查有效性）。</summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _clock.Pulse -= OnPulse;
        _attached = false;
    }

    /// <summary>已经推了几个逻辑步（从 1 起；每收到一次脉搏 +1）。</summary>
    public long StepIndex => _stepIndex;

    /// <summary>
    /// 把步号恢复到 <paramref name="value"/>（存档读档用；用户 2026-10-07 确认许可开这一口）。
    ///
    /// <para><b>只加恢复口，不动移动解算</b>：这一层仍是单位位置与朝向的唯一出处，
    /// 步号只是「推到了第几步」这个读数与后续解算的游标。</para>
    ///
    /// <para><b>为什么必须能恢复</b>：它与 <c>world.Tick</c> 是<b>两个独立计数器</b>
    /// （重放快照里头部 <c>tick=</c> 与寻路段 <c>step=</c> 各一份）。
    /// 读档只恢复前者、后者从头计，快照里那两列就对不上存档那一刻。</para>
    /// </summary>
    public void RestoreStepIndex(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "步号不能是负数");
        if (value < _stepIndex)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"步号只能往前拨（现在 {_stepIndex}，收到 {value}）");

        _stepIndex = value;
    }

    /// <summary>登记了几个单位。<b>登记次序就是步进次序</b>。</summary>
    public int AgentCount => _order.Count;

    /// <summary>单位 id 列表（登记次序）。</summary>
    public IReadOnlyList<string> AgentIds
    {
        get
        {
            var ids = new string[_order.Count];
            for (var i = 0; i < _order.Count; i++)
            {
                ids[i] = _order[i].Spec.Id;
            }

            return ids;
        }
    }

    /// <summary>
    /// 这个 id 登记过没有（<b>登记表里那一份查询，O(1)</b>）。
    ///
    /// <para>为什么要单开这一口：宿主每步都要问一次「它还在不在流水线上」
    /// （镜像同步、发令前的守卫），而 <see cref="AgentIds"/> 每问一次就重建一份名单 ——
    /// 单位数一多就是每步几万次字符串比较，成本读不出来、症状也读不出来。
    /// 另有「没登记过」的一档：<see cref="Issue"/> 与 <see cref="SolutionOf"/> 对它当场抛，
    /// 这两个口对它的语义是「查一下」，所以给的是 bool 而不是抛。</para>
    /// </summary>
    public bool HasAgent(string agentId)
    {
        NavCheck.Text(agentId, nameof(agentId));
        return _at.ContainsKey(agentId);
    }

    /// <summary>
    /// 注销一个单位（<b>只给「全量快照原样还原」用</b>）：战斗中新召的人要连登记一起作废，
    /// 否则重开那一局它会继续参与站位冲突，逐字比对漂在别人身上。
    /// </summary>
    /// <returns>注销了 = true；本来就没登记 = false。</returns>
    public bool Unregister(string agentId)
    {
        NavCheck.Text(agentId, nameof(agentId));
        if (!_at.Remove(agentId, out var index)) return false;

        _order.RemoveAt(index);
        for (var i = index; i < _order.Count; i++) _at[_order[i].Spec.Id] = i;
        return true;
    }

    /// <summary>登记一个单位。<b>数字标量（半径 / 移速）由宿主递，层里没有单位表。</b></summary>
    /// <exception cref="ArgumentOutOfRangeException">id 空白 / 重复，半径或移速不成形状，坐标非有限。</exception>
    public void Register(NavAgentSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        NavCheck.Text(spec.Id, nameof(spec.Id));
        NavSolveCheck.Radius(spec.RadiusMeters, nameof(spec.RadiusMeters));
        NavCheck.NonNegative(spec.MoveSpeedMetersPerSecond, nameof(spec.MoveSpeedMetersPerSecond), "移速（米每游戏秒）");
        NavCheck.Finite(spec.Start.X, "起始位置的 X");
        NavCheck.Finite(spec.Start.Z, "起始位置的 Z");
        NavCheck.Finite(spec.FacingDegrees, "起始朝向（度）");

        if (_at.ContainsKey(spec.Id))
        {
            throw new ArgumentOutOfRangeException(
                nameof(spec.Id),
                spec.Id,
                $"单位 {spec.Id} 已经登记过了 —— 同一个 id 登两次，快照里就有两行同名记录，事后读不出哪一行是现在这一个。");
        }

        var facing = Degrees(spec.FacingDegrees);
        _at.Add(spec.Id, _order.Count);
        _order.Add(new Agent
        {
            Spec = spec,
            Position = spec.Start,
            Facing = facing,
            Solution = NewSolution(
                spec.Id,
                NavCommandKind.Unset,
                spec.Start,
                facing,
                NavMoveStatus.Unset,
                NavStuckKind.None,
                0,
                0,
                spec.Start,
                spec.Start,
                NavMoveCheckVerdict.Allow,
                0.0),
        });
    }

    /// <summary>
    /// 发一条命令。<b>发令即把游标归零</b>：换一条沿固定点走的命令时，
    /// 留着上一条命令的游标会让「它为什么从第三个航点开始走」查不出来。
    /// </summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public void Issue(string agentId, NavCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var agent = Order()[IndexOf(agentId)];
        NavCheck.Tolerance(command.ArriveToleranceMeters);
        agent.Command = command;
        agent.Cursor = 0;
    }

    /// <summary>这个单位最新一步的移动解。<b>注册之后一发就有</b>（还没发令时是「未下令」那一行）。</summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public NavMoveSolution SolutionOf(string agentId) => Order()[IndexOf(agentId)].Solution;

    /// <summary>
    /// 这个单位手上那条命令（还没发过令是 null）—— 存档那一半读它。
    ///
    /// <para><b>为什么命令也要存</b>（用户 2026-10-07 许可开的恢复口之一）：
    /// 追实体那一档的目标坐标是<b>发令那一刻</b>的读数，层里不缓存也不外推。
    /// 读档后若不把手上那条命令放回去，下一步决策链重发的那一条带着新的目标坐标，
    /// 走的路就与存档那一局不同——差 3 厘米，然后<b>永远差下去</b>。</para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public NavCommand? CommandOf(string agentId) => Order()[IndexOf(agentId)].Command;

    /// <summary>这个单位现在的位置（米）。</summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public NavPoint PositionOf(string agentId) => Order()[IndexOf(agentId)].Position;

    /// <summary>
    /// <b>本步正在走的那条折线</b>（只读口，「表现层只读」 表现层只读那一半）：
    /// 交出的是<b>这一步送进 <c>NavLookAhead</c> 的那一份</b>，不是重新算一遍的。
    ///
    /// <para><b>为什么需要它</b>：视图要画「它这一刻在走哪条路」，而移动解只交出目标与前视点，
    /// 不交出折线，于是表现层唯一的补法是自己调一次 <c>Solve</c> —— 那正是<b>第二次计算</b>：
    /// 画面上摆的路与逻辑走的路会在某一帧分叉，而画面上看不出分叉（「约束唯一出处」 可调试性第一）。
    /// 视图层拿 <c>NavPathCache</c> 反查也不行：缓存键含「求解时的起点」＝步首位置，视图拿不到那个数。</para>
    ///
    /// <para><b>只交出、不判定、不重算</b>：本口不夹、不滤、不补、不裁。
    /// 部分路径（预算触顶 / 地形断开）交出的是<b>那一条部分路径本身</b>，末端就是它走到的那一格
    /// —— 调试视图要看的正是「它试了这么远」，把它补成一条通到目标的线才是骗人。</para>
    ///
    /// <para><b>哪几步交出什么</b>（写死，用例钉住）：
    /// <list type="bullet">
    /// <item><b>本步没求解的那三步</b>（还没发令 / 已到位 / 激活检查拒了）交出<b>脚下那一个点</b>。
    /// 激活检查排在求解上游是契约（「桥只转发」），所以那一步压根没有「正在走的路」——
    /// 交上一步那条会让人以为它还按旧路走；交空则与「折线永不为空」那条口径冲突（2.3）。</item>
    /// <item><b>没路那几步</b>（一步都走不了 / 子步被挡下）<b>已经求解过了</b>，交出那一条失败或被截断的路径。</item>
    /// </list></para>
    ///
    /// <para><b>不额外分配</b>：直接挂 <see cref="NavPath"/> 那份 <c>Corners</c> 的引用
    /// （缓存命中时是同一个对象；建好之后不再被改写）。</para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public IReadOnlyList<NavPoint> CornersOf(string agentId) => Order()[IndexOf(agentId)].Corners;

    /// <summary>
    /// 「本步压根没求解」那三步交出的那一个点。<b>永不为空</b>（口径同 <c>NavPath</c>：折线永不为空）。
    /// </summary>
    private static IReadOnlyList<NavPoint> SinglePoint(in NavPoint at) => new[] { at };

    /// <summary>
    /// 落位移 —— <b>把一个单位放到一个绝对位置（带朝向）</b>。流水线对外的<b>唯一</b>写口。
    ///
    /// <para><b>写什么、不写什么</b>：位置与朝向两列立刻改（所以
    /// <see cref="DebugSnapshotSegment"/> / <see cref="ReplaySnapshotSegment"/> 下一发就念得到新落点）；
    /// 位移 / 状态 / 卡住 / 子步 / 目标那几列<b>一个字都不动</b> ——
    /// 那些是「这一步走位」的账，这一发不是走位，是外部直接把它放到某一点。
    /// 一并改掉就等于把「它刚才走了多远」与「它现在被放到哪」并成一个数，
    /// 而那两件事的排查方向相反（「表现层只读」：这一层不替表现层与判定那一层改账）。</para>
    ///
    /// <para><b>不夹取、不判界</b>：给在哪就是哪。落点出界时这一格不抛也不夹，
    /// 下一次求解会照实报出 <see cref="NavPathReason.StartBlocked"/> ——
    /// 「落点出界了」在读数上看得见，比在这一格偷偷兜一手好查。
    /// 「落不成 vs 夹回」那个口径仍记在寻路层文档的待确认项一节。</para>
    ///
    /// <para><b>命令不清</b>：<see cref="Issue"/> 发的那一条留着，下一步照它继续走 ——
    /// 落位置与换命令是两件事，宿主自己决定要不要换。</para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    /// <exception cref="ArgumentOutOfRangeException">坐标或朝向非有限数。</exception>
    public void ApplyMove(string agentId, NavPoint to, double facingDegrees)
    {
        NavCheck.Finite(to.X, "落点的 X");
        NavCheck.Finite(to.Z, "落点的 Z");
        NavCheck.Finite(facingDegrees, "落点朝向（度）");

        var agent = Order()[IndexOf(agentId)];
        agent.Position = to;
        agent.Facing = Degrees(facingDegrees);
        agent.Solution = agent.Solution with { Position = to, FacingDegrees = agent.Facing };
    }

    /// <summary>
    /// 换一个已登记单位的<b>移速标量</b>（米每秒）—— 宿主每步递一次，递的是「这一局此刻」的数。
    ///
    /// <para><b>为什么是标量而不是倍率</b>：倍率是技能层那一侧的知识，层里留着它就得知道
    /// 「谁被减速了、慢几成」；而登记时递进来的本来就是「这个兵每秒走多少米」这一个绝对数，
    /// 宿主把它派生完再递回来，层里从头到尾只有一个数（「寻路单入口」 后半句）。</para>
    ///
    /// <para><b>登记那一刻递卡上的数，之后每步换成本步该用的数</b>：
    /// 入场抖动与技能层的派生倍率都落在后者上。
    /// <b>要在这一步的脉冲之前递</b>：激活检查与步长读的都是这一刻的值
    /// （激活检查看的是标称步长 ＝ 移速 × 步长秒数，「桥只转发」）。</para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    /// <exception cref="ArgumentOutOfRangeException">移速为负或非有限数。</exception>
    public void SetMoveSpeed(string agentId, double metersPerSecond)
    {
        NavCheck.NonNegative(metersPerSecond, nameof(metersPerSecond), "移速（米每游戏秒）");
        var agent = Order()[IndexOf(agentId)];
        agent.Spec = agent.Spec with { MoveSpeedMetersPerSecond = metersPerSecond };
    }

    /// <summary>
    /// 这一趟站位冲突<b>算不算它</b> —— 宿主每步递一次。
    ///
    /// <para>记成 <c>false</c> 的那一个：<b>既不被别人推开，也不推别人</b>（两头都不参与）。
    /// 层里不知道也不问「为什么不算」—— 那是宿主的知识（「寻路单入口」 后半句）；
    /// 这一列只负责让「它这一趟没参与」在读数上是一个数而不是一次缺席。</para>
    ///
    /// <para>缺省 <b>true</b>（登记时就是 true）：不发这一发的宿主拿到的还是全参与那一种行为，
    /// 而「不发 = 全参与」是登记语义自带的，不是一处兜底默认值。</para>
    ///
    /// <para>它<b>只管站位冲突这一档</b>：记成 false 的单位照样按命令走位、照样被网格与激活检查管着 ——
    /// 不参与避让不等于不许动（「禁自造术语」：这两个是不同的事，混起来就会出现「不动了但读数说挤住」）。</para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public void SetCountedInSeparation(string agentId, bool counted)
    {
        var agent = Order()[IndexOf(agentId)];
        agent.CountedInSeparation = counted;
    }

    /// <summary>
    /// 把一条命令<b>原样</b>放回去（全量快照还原用）：<paramref name="command"/> 为 null 时清空命令。
    ///
    /// <para><b>为什么要单独一口</b>：<see cref="Issue"/> 不接受 null，
    /// 而「战前这个人手上没有命令」与「手上挂着一条 Hold」在走位上是两件事
    /// （后者参与站位冲突、前者不参与）。快照要能还出「没有命令」那一档。</para>
    /// </summary>
    public void RestoreCommand(string agentId, NavCommand? command)
    {
        var agent = Order()[IndexOf(agentId)];
        agent.Command = command;
        agent.Cursor = 0;
        agent.Corners = Array.Empty<NavPoint>();
        agent.CornersCommand = null;
    }

    /// <summary>这个单位这一趟算不算进站位冲突（读数用；缺省 true）。</summary>
    /// <exception cref="KeyNotFoundException">单位没登记过。</exception>
    public bool IsCountedInSeparation(string agentId) => Order()[IndexOf(agentId)].CountedInSeparation;

    /// <summary>
    /// 检查点快照段（状态级）。<b>格式化只有这一处</b>：调试段与重放段两个名字、一份格式
    /// （照建造层 <c>ConstructionCore.RenderSegment</c> 的做法），列要是两处各拼一次就会漂。
    ///
    /// <para>每一行六列读数都在：位置、朝向、速度、状态、卡住类别、卡住步数
    /// （另带位移、子步数、目标、前视点、激活检查说法）。<b>网格、净空与两个版本号不在这儿</b> ——
    /// 那些是工单 06 的活，本期不越界。</para>
    /// </summary>
    public string DebugSnapshotSegment() => RenderSegment();

    /// <summary>重放快照段。与 <see cref="DebugSnapshotSegment"/> 同一份格式化。</summary>
    public string ReplaySnapshotSegment() => RenderSegment();

    private void OnPulse(long tick)
    {
        _stepIndex++;
        _staticVersionAtStepStart = _grid.StaticVersion;
        _solver.BeginStep(_tuning.StepSolveExpansions);

        // 走登记次序（数组下标），不走字典枚举。
        for (var i = 0; i < _order.Count; i++)
        {
            _order[i].StepStart = _order[i].Position;
            Step(_order[i]);
        }

        // 站位冲突：叠加修正叠在前视式跟随之上（③ 之后、每步的最后一步）。
        // 次序也是契约：先各自走到自己想去的地方，再统一推开 —— 反过来的话，
        // 被推的那一方会拿别人的旧位置当自己的起点，同一个数就有两套来源了。
        if (_separation is not null)
        {
            Avoid();
        }

        // 卡住事件在这一步的最后统一发：<b>每一步每单位最多一条</b>，
        // 发在避让之后 —— 那一档是叠加修正算出来的，漏发就等于「挤住了」这件事没有事件。
        for (var i = 0; i < _order.Count; i++)
        {
            PublishStuck(_order[i].Solution);
            _order[i].LastStuck = _order[i].Solution.Stuck;
            _order[i].LastStuckSteps = _order[i].Solution.StuckSteps;
        }
    }

    /// <summary>
    /// 这一步的避让叠加：<b>成对、相对、随局面变</b>，所以它是解析量不是网格（ADR 0008）。
    /// 推完之后按「净位移低于阈值」判拥挤推挤，并给出「卡在谁身上」。
    /// </summary>
    private void Avoid()
    {
        var count = _order.Count;
        var agents = new NavAgentProjection[count];
        for (var i = 0; i < count; i++)
        {
            var agent = _order[i];
            agents[i] = new NavAgentProjection
            {
                Id = agent.Spec.Id,
                Group = agent.Spec.Group,
                Position = agent.Position,
                RadiusMeters = agent.Spec.RadiusMeters,
                CountedInSeparation = agent.CountedInSeparation,
            };
        }

        var pushed = NavSeparation.Resolve(_grid, agents);
        for (var i = 0; i < count; i++)
        {
            var agent = _order[i];
            var to = pushed.Positions[i];
            var partner = pushed.PushedBy[i];
            agent.Position = to;

            var solution = agent.Solution;

            // 位移那一列读的是<b>净位移</b>（这一步开头到推完之后的落点）：
            // 宿主看到的位置是推完之后那个，拿推之前的数当位移，
            // 「它这一动走了多远」与「它现在站在哪」就对不上了。
            var net = agent.StepStart.DistanceTo(to);
            if (net == solution.MovedMeters && to == solution.Position)
            {
                continue;   // 没被推过：位移与位置一个数都不动（叠加修正在没重叠时必须是零读数）
            }

            // 挤住的判据：<b>自己确实想往前走</b>（位移非 0）+ <b>被推了一把</b> + <b>净位移低于阈值</b>。
            // 少前一条，站桩单位（移速 0）被同袍碰到时会被读成「挤住了」，而那一档是它的速度读数。
            var crowded = partner is not null
                          && solution.Status == NavMoveStatus.Moving
                          && solution.MovedMeters > 0.0
                          && net < _separation!.MinNetStepMeters;

            // 地形那一档压过拥挤：撞墙时它压根没走到能推的地方，排查方向完全相反。
            if (solution.Stuck == NavStuckKind.NoTerrainRoute)
            {
                crowded = false;
            }

            var stuck = crowded ? NavStuckKind.Crowded : solution.Stuck;
            agent.Solution = solution with
            {
                Position = to,
                MovedMeters = net,
                SpeedMetersPerSecond = net / _tuning.StepSeconds,
                Stuck = stuck,
                StuckSteps = NextStuck(agent, stuck),
                StuckOn = crowded ? partner : null,
            };
        }
    }

    /// <summary>卡住事件：<b>三类都发</b>，各带步数；拥挤那一类带「卡在谁身上」。「无」不发。</summary>
    private void PublishStuck(NavMoveSolution solution)
    {
        if (_events is null || solution.Stuck == NavStuckKind.None)
        {
            return;
        }

        _events.EmitStuck(new NavStuckEvent(
            solution.AgentId,
            _stepIndex,
            solution.Stuck,
            solution.StuckSteps,
            solution.StuckOn));
    }

    /// <summary>一个单位的一步。①②③④⑤⑥ 的次序见类型注释，那里是契约。</summary>
    private void Step(Agent agent)
    {
        var from = agent.Position;
        var facing = agent.Facing;

        // ① 没发令：交出「未下令」这一行。不产位移，也不假装是原地不动。
        if (agent.Command is null)
        {
            agent.Solution = NewSolution(agent.Spec.Id, NavCommandKind.Unset, from, facing,
                NavMoveStatus.Unset, NavStuckKind.None, 0, 0, from, from, NavMoveCheckVerdict.Allow, 0.0);
            agent.Corners = SinglePoint(from);
            return;
        }

        var command = agent.Command;
        var kind = command.Kind;
        var goal = command.GoalFor(agent.Cursor);

        // ① 到达容差统一处理：五档命令在这一点上形状完全一样。
        if (NavArrive.Reached(from, goal, command.ArriveToleranceMeters))
        {
            if (kind == NavCommandKind.AlongWaypoints && agent.Cursor + 1 < command.Target.Waypoints.Count)
            {
                agent.Cursor++; // 到位就推游标 —— 沿固定点走的「到位」是同一处处理
            }

            var landed = kind == NavCommandKind.Hold ? NavMoveStatus.Holding : NavMoveStatus.Arrived;
            agent.Solution = NewSolution(agent.Spec.Id, kind, from, facing, landed,
                NavStuckKind.None, 0, 0, goal, goal, NavMoveCheckVerdict.Allow, 0.0);
            agent.Corners = SinglePoint(from);   // 已到位：本步没有「正在走的路」，只交脚下那一点
            if (landed == NavMoveStatus.Arrived)
            {
                Emit(new NavArrivedEvent(agent.Spec.Id, _stepIndex, from, goal));
            }

            return;
        }

        // ② 移动激活检查排在上游：先问允不允许动，再算站哪。激活检查看到的是标称步长 ——
        //    「沿路径还剩多远」要等求解之后才知道，而激活检查排在求解之前正是契约。
        var planned = agent.Spec.MoveSpeedMetersPerSecond * _tuning.StepSeconds;
        var verdict = _gate is null ? NavMoveCheckVerdict.Allow : _gate(agent.Spec.Id, _stepIndex, planned);
        if (!verdict.Allowed)
        {
            agent.Solution = NewSolution(agent.Spec.Id, kind, from, facing, NavMoveStatus.HeldByActivationCheck,
                NavStuckKind.None, 0, 0, goal, goal, verdict, 0.0);
            // 激活检查排在求解上游 ⇒ 这一步压根没求解过，所以「正在走的路」不存在。
            // 交上一步那条会让人以为它还按旧路走；交脚下那一点与「折线永不为空」同口径。
            agent.Corners = SinglePoint(from);
            return;
        }

        // ③ 算站哪。④ 取方向。⑤ 取步长：与前视点无关，只以「沿路径剩余」封顶。
        //
        // ③ 之前多一道：手上那条路还成立时不重解（见 ReuseValidPath）。
        // 判据用的是层里已有的四条重算触发，触发一响立刻重解。
        var path = ReuseValidPath(agent, goal, out var reusableCorners)
            ? null
            : _solver.Solve(from, goal, agent.Spec.RadiusMeters);
        var corners = path?.Corners ?? reusableCorners;
        agent.Corners = corners;   // 只交出，不判定：后面那几步（走不了 / 被挡下）用的就是这一份

        // 记下交出这条折线的那一次求解的现场（复用判据下一次拿它比）：
        // 复用的那几步不更新它 —— 「上一次求解」说的是真解过的那一次，不是这一步。
        if (path is not null)
        {
            agent.CornersCommand = agent.Command;
            agent.LastSolveStaticVersion = _grid.StaticVersion;
            agent.LastSolveGoal = goal;
            agent.LastSolveBudgetExceeded = path.IsBudgetTruncated;
            agent.LastSolveStepIndex = _stepIndex;
            agent.LastSolveNoTerrainRoute = NoTerrainRouteIn(path);
        }

        var (dx, dz) = NavLookAhead.Direction(
            _grid, corners, from, _tuning.LookAheadMeters, agent.Spec.RadiusMeters);
        var aim = NavLookAhead.PointAlong(
            _grid, corners, from, _tuning.LookAheadMeters, agent.Spec.RadiusMeters);
        var remaining = NavLookAhead.RemainingAlong(corners, from);

        // 地形无路那一档有两个来源：求解压根没起步（两端进不了可行域 / 地形断开），
        // 与一步之内被地形挡下（格级可行域不蕴含点级 CanStandAt，见寻路层文档 2.9）。
        // 复用那几步没有新的求解结果：沿用交出这条折线的那一次求解的判读，不把它读成「没有」。
        var terrain = path is not null ? NoTerrainRouteIn(path) : agent.LastSolveNoTerrainRoute;
        var stepMeters = Math.Min(planned, remaining);
        if (stepMeters <= 0.0 || (dx == 0.0 && dz == 0.0))
        {
            // 一步都走不了：要么折线到头了（死路，由地形决定），要么这一局的移速是 0。
            // 后者不是卡住 —— 「速度 0」那一列就是它的读数。
            var held = terrain ? NavMoveStatus.NoTerrainRoute : NavMoveStatus.Moving;
            var idle = terrain ? NavStuckKind.NoTerrainRoute : BudgetStuck(path);
            agent.LastStepBlocked = false;   // 这一步压根没往墙那边走
            agent.Solution = NewSolution(agent.Spec.Id, kind, from, facing, held,
                idle,
                NextStuck(agent, idle),
                0,
                goal,
                aim,
                NavMoveCheckVerdict.Allow,
                0.0);
            RaisePathEvents(agent.Spec.Id, path, substepBlocked: false);
            return;
        }

        // ⑥ 切子步逐段验：跨不过墙，切不过角。
        var to = new NavPoint(from.X + (dx * stepMeters), from.Z + (dz * stepMeters));
        var sub = NavSubstepper.Advance(_grid, from, to, stepMeters, agent.Spec.RadiusMeters);
        agent.Position = sub.Position;
        agent.LastStepBlocked = sub.Blocked;
        if (sub.Steps > 0)
        {
            agent.Facing = Degrees(Heading(from, sub.Position));
        }

        // 位移按「走完了几份」算，不回代距离：所有子步共线，那份数就是位移的精确值，
        // 而 sqrt 那一路会带出 1.5000000000000002 这种读数（它与旧实现同样会带出来，
        // 但读数本身该是干净的 —— 位置与位移都要能逐字比）。
        var moved = stepMeters * sub.Steps / sub.Planned;

        // 「卡住」判的是「走不到命令说的地方」而不是「这一动没动」：部分路径上还在往前走时，
        // 位置每步都在变，把它计成卡住，卡住步数就成了一个数不出东西的读数。
        // 三类的次序写死：<b>地形 &gt; 拥挤 &gt; 预算</b>。地形那一档压过其余两档（撞墙与被推是两件事），
        // 拥挤压过预算（挤住是这一动的原因，触顶是这一条命令的毛病）。
        // 卡住步数 = 这一类卡住连续出现的步数（换类归零），于是
        // 「卡住 地形无路/0」这种读法不会出现 —— 那一对要么是「无/0」，要么是「地形无路/n」且 n ≥ 1。
        var stuck = terrain || sub.Blocked ? NavStuckKind.NoTerrainRoute : BudgetStuck(path);
        var status = sub.Steps > 0 ? NavMoveStatus.Moving : NavMoveStatus.NoTerrainRoute;

        agent.Solution = NewSolution(agent.Spec.Id, kind, sub.Position, agent.Facing, status, stuck,
            NextStuck(agent, stuck),
            sub.Steps,
            goal,
            aim,
            NavMoveCheckVerdict.Allow,
            moved);

        RaisePathEvents(agent.Spec.Id, path, sub.Blocked);
    }

    /// <summary>
    /// 手上那条路还成立吗 —— 成立就把它的折线交回（复用），不成立就返回假让调用方重解。
    ///
    /// <para><b>为什么要有这一道</b>：<c>NavStepper</c> 对每个「没到位」的单位每步都 <c>Solve</c> 一次，
    /// 而路径缓存的键含起点、起点每步都在动 ⇒ 命中率≈0。小场地（576 格、≤30 单位）量不出来，
    /// 一大场地就现形：8800 格的战场、同屏上百只怪，实测同一条长跑
    /// 从 4.7 秒涨到 302 秒，而单次全场求解 4.987 毫秒、每步平均 22 个单位在解。</para>
    ///
    /// <para><b>四条触发一条都没踩中才算成立</b>（<see cref="NavRecomputeJudge"/>）：地形版本没变、
    /// 目标移动没超阈值、上一次不是预算触顶、距上次求解没到步数上限。反过来，
    /// 地形一改或目标真的动了，路立刻重解 —— 这是「绕远路能到」不被读成「地形无路」的那条保证。</para>
    ///
    /// <para><b>只认同一条命令留下的折线</b>：换了命令（游标归零、目标形状都换了）就重解，
    /// 因为「沿固定点走」那一档的下一个航点、与「追实体」那一档的新目标，都不是旧路能代表的。</para>
    ///
    /// <para><b>前视点必须真的取得到才复用</b>：一个可走方向都取不到（<c>(0,0)</c>）时返回假重解 ——
    /// 否则这一步会退化成「站着不动而读数说它在走」，而那正是复用这一档最坏的失败形状。</para>
    ///
    /// <para><b>上一步被墙挡下也不复用</b>（<see cref="Agent.LastStepBlocked"/>）：那条路已经走不通了，
    /// 挡下的下一步就换一条 —— 不复用时这一步本来就会重解，所以这不是新开销，是补回被省掉的那一次。</para>
    ///
    /// <para><b>交回的折线先重新接到脚下这一点上</b>（<see cref="HeadAt"/>）：复用那几步没有求解，
    /// 折线的头就留在上一次求解的地方。留着它，前视取样会先朝身后走 —— 实测单位在墙角
    /// 一步向前一步向后地来回摆，读数说「在走」，位置两步一循环。</para>
    /// </summary>
    private bool ReuseValidPath(Agent agent, in NavPoint goal, out IReadOnlyList<NavPoint> corners)
    {
        corners = Array.Empty<NavPoint>();
        if (agent.Command is null
            || agent.LastStepBlocked
            || !ReferenceEquals(agent.Command, agent.CornersCommand)
            || agent.Corners.Count == 0)
        {
            return false;
        }

        var triggers = NavRecomputeJudge.Judge(new NavRecomputeProbe(
            agent.LastSolveStaticVersion,
            _grid.StaticVersion,
            agent.LastSolveGoal,
            goal,
            _recomputeTuning.GoalMoveToleranceMeters,
            agent.LastSolveBudgetExceeded,
            (int)(_stepIndex - agent.LastSolveStepIndex),
            _recomputeTuning.NotArrivedStepCap));
        if (triggers != NavRecomputeTriggers.None)
        {
            return false;
        }

        var head = HeadAt(agent.Corners, agent.Position);
        if (head.Count < 2)
        {
            // 折线只剩身后那一个点：它指的是来路，不是去路。
            return false;
        }

        var (dx, dz) = NavLookAhead.Direction(
            _grid, head, agent.Position, _tuning.LookAheadMeters, agent.Spec.RadiusMeters);
        if (dx == 0.0 && dz == 0.0)
        {
            return false;
        }

        corners = head;
        return true;
    }

    /// <summary>
    /// 把折线重新接到当前位置上：<b>首位换成脚下这一点，身后已经拐过去的拐点摘掉</b>。
    ///
    /// <para><b>为什么必须重接</b>：前视取样是从当前位置<b>沿折线往前</b>走的。
    /// 复用那几步没有求解，折线的头就留在上一次求解的地方，于是取样先朝身后走 ——
    /// 实测单位在墙角一步向前一步向后地来回摆。</para>
    ///
    /// <para><b>接在哪一段上</b>：取「离脚下最近的那一段」，从它的下一个拐点接起。
    /// 折线可以折回来，所以不按拐点次序推定「已经走到第几个了」。</para>
    ///
    /// <para><b>逐个判「拐过去了没有」试过、不成立</b>：直角拐弯上「沿来路的投影越过拐点」
    /// 这个判法在单位贴着墙走时永远不成立（投影停在 1 之前），折线的头就一直钉在那个拐点上，
    /// 实测两条用例从「绕过墙到了」退化成「200 步还没到」。</para>
    /// </summary>
    private static IReadOnlyList<NavPoint> HeadAt(IReadOnlyList<NavPoint> corners, NavPoint at)
    {
        var nearest = 0;
        var nearestGap = double.PositiveInfinity;
        for (var i = 0; i + 1 < corners.Count; i++)
        {
            var gap = DistanceToSegment(at, corners[i], corners[i + 1]);
            if (gap < nearestGap)
            {
                nearestGap = gap;
                nearest = i;
            }
        }

        var head = new List<NavPoint>(corners.Count - nearest) { at };
        for (var i = nearest + 1; i < corners.Count; i++)
        {
            head.Add(corners[i]);
        }

        return head;
    }

    /// <summary>点到线段的最短距离（米）—— 取「最近的那一段」用它。</summary>
    private static double DistanceToSegment(NavPoint at, NavPoint a, NavPoint b)
    {
        var abx = b.X - a.X;
        var abz = b.Z - a.Z;
        var lengthSquared = (abx * abx) + (abz * abz);
        if (lengthSquared <= 0.0)
        {
            return at.DistanceTo(a);
        }

        var t = (((at.X - a.X) * abx) + ((at.Z - a.Z) * abz)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return at.DistanceTo(new NavPoint(a.X + (abx * t), a.Z + (abz * t)));
    }

    /// <summary>
    /// 这一条求解结果读不读得出「地形无路」：求解失败，或原因是目标不可达。
    /// <b>两处用同一份判读</b>（本步求解那一路与复用那一路），免得两处各一套。
    /// </summary>
    private static bool NoTerrainRouteIn(in NavPath path)
        => path.Status == NavPathStatus.Failed || path.Reason == NavPathReason.GoalUnreachable;

    /// <summary>
    /// 预算触顶那一档：<b>这一条被预算截断了</b>就是它（spec：触顶回部分路径而不是空，
    /// 于是「走不到位」长成了「已经存在的那个可读事实」）。走通或压根没起步都不是它。
    ///
    /// <para><b>这一步压根没求解时（<paramref name="path"/> 为 null，即复用了上一条路）</b>
    /// 给 <see cref="NavStuckKind.None"/>：复用说明上一次解出来的是好路，没有「被截断」这件事。</para>
    /// </summary>
    private static NavStuckKind BudgetStuck(NavPath? path)
        => path is { IsBudgetTruncated: true } ? NavStuckKind.BudgetCapped : NavStuckKind.None;

    /// <summary>
    /// 两条与「这一条求解结果」有关的事件：<b>部分路径</b>与<b>撞墙重算</b>。
    ///
    /// <para>撞墙只认地形这一族（<see cref="NavRecomputeCause"/>）：因为预算不够而重算是预算那一档的账，
    /// 因为目标动了而重算是命令那一档的账 —— 混进「撞墙」就等于把三件排查方向相反的事并成一件。</para>
    ///
    /// <para><b>这一步没求解（复用）时只报子步被挡下那一档</b>：没有新的求解结果，
    /// 「部分路径」与「求解给的地形原因」都无从谈起；而「子步真的撞了墙」是这一步自己的事实，
    /// 与求解过没求解过无关 —— 少报它就是把「撞墙」这件事丢了一步。</para>
    /// </summary>
    private void RaisePathEvents(string agentId, NavPath? path, bool substepBlocked)
    {
        if (_events is null)
        {
            return;
        }

        if (path is null)
        {
            if (substepBlocked)
            {
                _events.EmitRecompute(new NavRecomputeEvent(
                    agentId, _stepIndex, NavRecomputeCause.SubstepBlocked));
            }

            return;
        }

        if (path.Status == NavPathStatus.Partial)
        {
            var reached = path.PartialAt ?? path.Corners[0];
            _events.EmitPartialPath(new NavPartialPathEvent(
                agentId, _stepIndex, path.Reason, path.ExpandedCells, reached));
        }

        var cause = RecomputeCause(path, substepBlocked);
        if (cause != NavRecomputeCause.None)
        {
            _events.EmitRecompute(new NavRecomputeEvent(agentId, _stepIndex, cause));
        }
    }

    /// <summary>
    /// 这一条为什么撞墙：次序写死，<b>先说这一步本身</b>（子步真的被挡下了），
    /// 再说这一条逻辑步里墙变没变（静态版本），最后才是求解给的那几条地形原因。
    /// </summary>
    private NavRecomputeCause RecomputeCause(in NavPath path, bool substepBlocked)
    {
        if (substepBlocked)
        {
            return NavRecomputeCause.SubstepBlocked;
        }

        if (_grid.StaticVersion != _staticVersionAtStepStart)
        {
            return NavRecomputeCause.StaticVersionChanged;
        }

        return path.Reason switch
        {
            NavPathReason.GoalUnreachable => NavRecomputeCause.TerrainUnreachable,
            NavPathReason.GoalBlocked => NavRecomputeCause.GoalBlocked,
            NavPathReason.StartBlocked => NavRecomputeCause.StartBlocked,
            _ => NavRecomputeCause.None,
        };
    }

    private void Emit(in NavArrivedEvent e) => _events?.EmitArrived(e);

    /// <summary>
    /// 卡住步数 = 当前这一类卡住<b>连续出现了几步</b>：同类别 +1、换类别归 1、「无」归零
    /// （spec「每类都带卡住步数」）。它与 <see cref="NavStuckKind"/> 要一起读，单独看没有意义。
    ///
    /// <para>比的是 <b>上一步定稿之后</b>的类别（<see cref="Agent.LastStuck"/>），不是上一步算到一半的：
    /// 拥挤那一类是在避让叠加之后才定下来的，拿半成品当底会把步数按成 1（那是一条数不出东西的读数）。</para>
    /// </summary>
    private static int NextStuck(Agent agent, NavStuckKind stuck)
    {
        if (stuck == NavStuckKind.None)
        {
            return 0;
        }

        return agent.LastStuck == stuck ? agent.LastStuckSteps + 1 : 1;
    }

    private NavMoveSolution NewSolution(
        string agentId,
        NavCommandKind command,
        NavPoint position,
        double facing,
        NavMoveStatus status,
        NavStuckKind stuck,
        int stuckSteps,
        int substeps,
        NavPoint goal,
        NavPoint aim,
        NavMoveCheckVerdict gate,
        double moved)
        => new()
        {
            AgentId = agentId,
            Command = command,
            Position = position,
            FacingDegrees = facing,
            SpeedMetersPerSecond = moved / _tuning.StepSeconds,
            Status = status,
            Stuck = stuck,
            StuckSteps = stuckSteps,
            Substeps = substeps,
            Goal = goal,
            Aim = aim,
            CheckReason = gate.Reason,
            MovedMeters = moved,
        };

    private List<Agent> Order() => _order;

    private int IndexOf(string agentId)
    {
        NavCheck.Text(agentId, nameof(agentId));
        if (!_at.TryGetValue(agentId, out var index))
        {
            throw new KeyNotFoundException(
                $"单位 {agentId} 没有登记过 —— 流水线只步进登记过的单位，"
                + "拿一个没登记的 id 来问位置，问出来的是「上一个查过的单位」，那比抛更坏。");
        }

        return index;
    }

    private string RenderSegment()
    {
        var sb = new StringBuilder();
        sb.Append("pathfinding space=").Append(_solver.Space.Space)
            .Append(" step=").Append(_stepIndex.ToString(CultureInfo.InvariantCulture))
            .Append(" agents=").Append(_order.Count.ToString(CultureInfo.InvariantCulture))
            .Append('\n');

        foreach (var agent in _order)
        {
            var s = agent.Solution;
            sb.Append(s.AgentId)
                .Append(' ').Append(s.Command)
                .Append(" 位置 ").Append(s.Position)
                .Append(" 朝向 ").Append(s.FacingDegrees.ToString(CultureInfo.InvariantCulture))
                .Append(" 速度 ").Append(s.SpeedMetersPerSecond.ToString(CultureInfo.InvariantCulture))
                .Append(" 位移 ").Append(s.MovedMeters.ToString(CultureInfo.InvariantCulture))
                .Append(" 状态 ").Append(s.Status)
                .Append(" 卡住 ").Append(StuckText(s.Stuck))
                .Append('/').Append(s.StuckSteps.ToString(CultureInfo.InvariantCulture))
                .Append(" 子步 ").Append(s.Substeps.ToString(CultureInfo.InvariantCulture))
                .Append(" 目标 ").Append(s.Goal)
                .Append(" 前视 ").Append(s.Aim)
                .Append(" 激活检查 ").Append(s.CheckReason);

            if (s.StuckOn is not null)
            {
                sb.Append(" 卡在 ").Append(s.StuckOn).Append(" 身上");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>卡住类别的中文读法。<b>四档都有名字，没有「其他」</b>：读不出是哪一个的类别等于没有类别。</summary>
    private static string StuckText(NavStuckKind stuck) => stuck switch
    {
        NavStuckKind.None => "无",
        NavStuckKind.NoTerrainRoute => "地形无路",
        NavStuckKind.Crowded => "拥挤推挤",
        NavStuckKind.BudgetCapped => "预算触顶",
        _ => "无",
    };

    /// <summary>
    /// 逐子步终点方向的朝向（度）：<b>0 = +Z，+90 = +X，−90 = −X，180 记成 −180</b>，值域 [−180, 180)。
    /// 瞬时对准、<b>不带转身时间</b>（转向时间归造型与动作层，spec「Out of Scope」）。
    /// </summary>
    private static double Heading(NavPoint from, NavPoint to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        if (dx == 0.0 && dz == 0.0)
        {
            return 0.0;
        }

        return Degrees(Math.Atan2(dx, dz) * (180.0 / Math.PI));
    }

    /// <summary>度规整进 [−180, 180)：atan2 给的是 (−180, 180]，那条右端点要折回来，否则同一件事有两种读法。</summary>
    private static double Degrees(double degrees)
    {
        if (degrees >= 180.0)
        {
            degrees -= 360.0;
        }
        else if (degrees < -180.0)
        {
            degrees += 360.0;
        }

        return degrees == 0.0 ? 0.0 : degrees; // 抹掉 −0.0：它是同一个数，两种逐字读法
    }

    /// <summary>一个单位的层内状态。<b>坐标只存一份</b>：移动解与它同源，不各留一份。</summary>
    private sealed class Agent
    {
        public required NavAgentSpec Spec { get; set; }

        public NavPoint Position { get; set; }

        public double Facing { get; set; }

        public NavCommand? Command { get; set; }

        /// <summary>沿固定点走那一档的游标（其余四档恒为 0，不读它）。</summary>
        public int Cursor { get; set; }

        /// <summary>这一步开始时的位置（米）—— 避让算净位移的基准。</summary>
        public NavPoint StepStart { get; set; }

        /// <summary>
        /// 本步正在走的那条折线（<see cref="CornersOf"/> 那一口的存储）。
        /// <b>由 <c>Step</c> 赋值，别处一个字都不改</b>；没求解的那几步交出脚下那一个点。
        /// </summary>
        public IReadOnlyList<NavPoint> Corners { get; set; } = Array.Empty<NavPoint>();

        /// <summary>
        /// 交出 <see cref="Corners"/> 那条折线的<b>那一次求解</b>手上的命令（对象的同一性，不是相等）。
        ///
        /// <para>换了命令（哪怕是同形的另一条）就重解：目标形状、游标都换了，
        /// 旧折线代表不了新命令。用同一性而不是相等，是因为两条同形命令的「同形」是宿主的判据，
        /// 层里不该替它判一次。</para>
        /// </summary>
        public NavCommand? CornersCommand { get; set; }

        /// <summary>交出当前折线那一次求解时的静态地形版本（复用判据的第一条）。</summary>
        public int LastSolveStaticVersion { get; set; }

        /// <summary>交出当前折线那一次求解时的目标点（复用判据的第二条：目标移动超阈值就重解）。</summary>
        public NavPoint LastSolveGoal { get; set; }

        /// <summary>交出当前折线那一次求解是不是预算触顶（复用判据的第三条）。</summary>
        public bool LastSolveBudgetExceeded { get; set; }

        /// <summary>
        /// 交出当前折线那一次求解是不是读出了「地形无路」（求解失败，或原因是目标不可达）。
        ///
        /// <para><b>这一条要跨步记住</b>：复用那几步没有求解结果，若把那几步的「地形无路」读成「没有」，
        /// 同一个事实就会一步说有、下一步说没有 —— 卡住步数的连续计数被截断，
        /// 单位在死路前走的那几步会读成「一切正常」。所以复用时沿用上一次求解的判读。</para>
        /// </summary>
        public bool LastSolveNoTerrainRoute { get; set; }

        /// <summary>交出当前折线那一次求解发生在第几步（复用判据的第四条：走满 N 步没到就重解）。</summary>
        public long LastSolveStepIndex { get; set; }

        /// <summary>
        /// 上一步的子步在墙前停下（<see cref="NavSubstepResult.Blocked"/>；一步都没走成也是它）。
        ///
        /// <para><b>它也是复用的一条判据</b>：手上那条路在这一步被墙挡下了，
        /// 说明它已经不再是「还成立的那条」。不复用就等于每一步都重解，被挡下的下一步自然换一条路；
        /// 复用之后少了这一次重解，单位会在墙角读到「地形无路」而一直照着旧折线试，
        /// 最多要等满 <c>NotArrivedStepCap</c> 步才换路 —— 那是「站着不动」，不是省一次求解。</para>
        /// </summary>
        public bool LastStepBlocked { get; set; }

        /// <summary>
        /// 这一趟站位冲突算不算它（宿主每步递，缺省 true）。<b>只管避让这一档</b>，
        /// 与「能不能动」是两件事 —— 那一件归 <see cref="NavMoveCheck"/>。
        /// </summary>
        public bool CountedInSeparation { get; set; } = true;

        /// <summary>上一步<b>定稿之后</b>的卡住类别（卡住步数的连续性比它，见 <see cref="NextStuck"/>）。</summary>
        public NavStuckKind LastStuck { get; set; }

        /// <summary>上一步定稿的卡住步数。</summary>
        public int LastStuckSteps { get; set; }

        public required NavMoveSolution Solution { get; set; }
    }
}
