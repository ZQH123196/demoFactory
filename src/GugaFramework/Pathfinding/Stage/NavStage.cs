#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.GugaFramework.Pathfinding.Stage;

/// <summary>
/// <b>逻辑壳</b>——寻路试验场的本体，纯 C#、零引擎类型、可 <c>dotnet test</c> 秒级跑，
/// 也可在 headless 下逐字重跑（场子「固定」那一条口径）。
///
/// <para>场子的本体就是本文件：<b>它不认识任何一条具体的单位、任何一种障碍布局、任何一个格长</b>——
/// 那些全在注册口 <see cref="NavStageSpec"/> 那边由调用方递进来。换一块场地重起一局，
/// 本文件一个字都不动（口径同技能试验场的 <c>SkillStage</c>）。</para>
///
/// <para>场子自带时间可控、状态可查、行为可注入三个口，与每个逻辑世界同一份契约：
/// <list type="bullet">
/// <item><b>时间可控</b>：<see cref="AdvanceRealSeconds"/>（表现层每帧那一发）／<see cref="Advance"/>（批量推进）
/// ／<see cref="AdvanceStep"/>（单步调试口）；<see cref="Paused"/> 只冻逻辑，<see cref="Speed"/> 倍率乘在流速上。
/// 步是唯一那把尺（1 步 = 0.1 秒，「时间基准」）。</item>
/// <item><b>状态可查</b>：<see cref="DebugSnapshot"/> 一发读出全量真值（网格 + 净空 + 每个单位的移动解 +
/// 求解记录 + 事件账本），显示即真相。</item>
/// <item><b>行为可注入</b>：<see cref="Inject"/> 是写路径的唯一分发口（「桥只转发」），
/// 动词名与参数次序住在 <see cref="NavVerbs"/> 那一份表里，本文件不另写一套实现。</item>
/// </list></para>
///
/// <para><b>三条边界，写明白不藏</b>：
/// <list type="number">
/// <item><b>写路径只有一条</b>：封地形、发命令都从 <see cref="Inject"/> 进。场子不另写一套注入实现，
/// 所以「注入的路径」与「玩家走的那条」不是两回事（「桥只转发」）。
/// <see cref="NavTerrain"/> 是唯一那条地形写路径，场子不直接碰 <c>SetBlocked</c>。</item>
/// <item><b>随机源在宿主手里，种子也在宿主手里</b>：<see cref="RandomEnabled"/> 为假时
/// <see cref="NextRandom"/> 每次给同一个数，于是同条件重跑逐字一致。</item>
/// <item><b>机位不在本文件里</b>：机位是表现层参数，不进检查点快照（「试验场随技能层」）。
/// 逻辑壳里没有任何一行与相机有关。<see cref="Paused"/> 与 <see cref="Speed"/> 同属这一类：
/// <b>时间控制不进快照</b>，否则中途停一下再放两只会在逐字比对上报「第几步起分叉」，而逻辑一个字没变。</item>
/// </list></para>
///
/// <para><b>场子不替单位走位</b>：除了注册口那份开局摆位与开局命令，位置只由
/// <see cref="Inject"/> 里的命令与 <see cref="NavStepper"/> 的位移那一条写路径改。
/// 想验「谁在射程边界哪一侧」，改注册口重起一局，而不是等场子自己走过去。</para>
///
/// <para><b>步号只有一份</b>：时间推进落在 <see cref="GameClock"/> 那本账上，场子不另持一个步号 ——
/// 不然场子自己那份与 <see cref="NavStepper"/> 那份会漂。<see cref="SpeedControl"/> 只借来做
/// 「真实秒 × 倍率 = 该推几步」的换算，暂停是场子自己那一格。</para>
/// </summary>
public sealed class NavStage
{
    /// <summary>1 步 = 0.1 秒 ⇒ 每秒 10 步（「时间基准」）。<see cref="SpeedControl"/> 换算流速用的就是这个频率。</summary>
    public const double StepsPerSecond = 10.0;

    private readonly NavStageSpec _spec;
    private readonly NavSpace _space;
    private readonly NavSolver _solver;
    private readonly NavStepper _stepper;
    private readonly GameClock _clock;
    private readonly NavMoveEvents _events;
    private readonly NavReplayWorld _world;
    private readonly Random _rng;
    private readonly SpeedControl _speed = new(StepsPerSecond);

    /// <summary>帧内余量：真实秒换算出来的步数不够一步时先攒着（「时钟解耦」：攒够一个步长才推一步）。</summary>
    private double _carry;

    /// <summary>
    /// 按注册口起一局，给一个固定种子（默认 1）。同种子两跑逐字一致。
    /// </summary>
    /// <param name="spec">注册口：格长、格数、阻挡布局、单位名册、步进档、预算档、容量档。<b>少一列编译期就断。</b></param>
    /// <param name="seed">本局种子。读数里念出来：不然「这一局是哪一档」要回头翻代码。</param>
    /// <exception cref="ArgumentNullException">注册口为 null。</exception>
    public NavStage(NavStageSpec spec, int seed = 1)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec),
                "起一局要一份注册口：格长、格数、阻挡布局、单位名册、步进档、预算档、容量档，场子自己一个都不替你挑");
        }

        _spec = spec;
        Seed = seed;
        _rng = new Random(seed);

        // 网格由注册口那份声明造出来：格长、格数、阻挡表都是场地条件，场子不替调用方挑。
        var grid = new NavGrid(new NavGridSpec
        {
            Space = spec.Space,
            CellMeters = spec.CellMeters,
            CellsX = spec.CellsX,
            CellsZ = spec.CellsZ,
            OriginX = spec.OriginX,
            OriginZ = spec.OriginZ,
            BlockedCells = spec.BlockedCells,
        });
        _space = new NavSpace(spec.Space, grid);

        _solver = new NavSolver(
            _space,
            spec.SolveBudget,
            spec.RecomputeTuning,
            spec.Capacity.PathCacheEntries,
            spec.Capacity.SolveRecords,
            spec.Capacity.TrackedRequests);

        _clock = new GameClock();
        _events = new NavMoveEvents(spec.Capacity.EventRecords);
        _stepper = new NavStepper(
            _solver,
            spec.Tuning,
            _clock,
            gate: null,
            separation: spec.Separation,
            events: _events);

        // 单位名册：登记次序就是步进次序（层里的契约），场子不重排。
        foreach (var unit in spec.Roster.Units)
        {
            _stepper.Register(new NavAgentSpec
            {
                Id = unit.Id,
                RadiusMeters = unit.RadiusMeters,
                MoveSpeedMetersPerSecond = unit.MoveSpeedMetersPerSecond,
                Start = unit.Start,
                FacingDegrees = unit.FacingDegrees,
                Group = unit.Group,
            });
        }

        // 开局命令走层里那一条写路径（与 Inject 的 SetOrder 是同一次 Issue），不是第二条口。
        foreach (var unit in spec.Roster.Units)
        {
            if (unit.OpeningOrder is not null)
            {
                _stepper.Issue(unit.Id, unit.OpeningOrder);
            }
        }

        _world = new NavReplayWorld(_space, _solver, _stepper, _clock);
    }

    /// <summary>本局的种子。读数里念出来：不然「这一局是哪一档」要回头翻代码。</summary>
    public int Seed { get; }

    /// <summary>注册口这一份。场子只认它，不另存一份副本（那会与调用点漂）。</summary>
    public NavStageSpec Spec => _spec;

    /// <summary>归一化后的网格。<b>只读</b>：写地形走 <see cref="Inject"/>（<c>StampTerrain</c>）。</summary>
    public NavGrid Grid => _space.Grid;

    /// <summary>空间（只名字，没有原点与变换，ADR 0009）。</summary>
    public NavSpace Space => _space;

    /// <summary>求解器。表现层要算一条路时从这儿拿（只读用法）。</summary>
    public NavSolver Solver => _solver;

    /// <summary>移动解流水线：单位的位置、朝向、速度、状态、卡住类别全在它那份移动解里。</summary>
    public NavStepper Stepper => _stepper;

    /// <summary>事件出口（四条强类型通道 + 一本有界账本）。</summary>
    public NavMoveEvents Events => _events;

    /// <summary>重放宿主：场子的动词表默认那一侧（<b>不另写一份 sink</b>，「重放装配单源」 同一句理由）。</summary>
    public NavReplayWorld World => _world;

    /// <summary>当前第几步（步 ÷ 10 = 秒，「时间基准」）。这一格归 <see cref="GameClock"/>，场子不另持一个。</summary>
    public long Tick => _clock.Tick;

    /// <summary>
    /// 随机项总开关。<b>为假时 <see cref="NextRandom"/> 每次给同一个数</b> ——
    /// 场子「固定」那一条口径的实现处（随机项一律能关掉，关掉后逐字一致）。
    /// </summary>
    public bool RandomEnabled { get; set; } = true;

    /// <summary>关掉随机时每次给的那个数。缺省 0.5（区间中点，不偏向任何一端）。</summary>
    public double FixedRandomValue { get; set; } = 0.5;

    // === 时间可控 ===

    /// <summary>
    /// 逻辑暂停：<b>只冻逻辑</b>。冻结之后 <see cref="Advance"/> 与 <see cref="AdvanceRealSeconds"/> 一步都不走，
    /// 而 <see cref="AdvanceStep"/>（单步调试口）照走一步 —— 「停下来一格格看」是暂停的全部用处。
    /// 这一格<b>不进快照</b>，理由与机位参数不进快照同源（「试验场随技能层」）。
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>
    /// 流速倍率：1 = 现实时间（1 步 = 0.1 秒），0.25 = 慢放，20 = 快进。
    /// 换算走既有的 <see cref="SpeedControl"/>，本文件不自己算那一乘。
    /// 必须 &gt; 0，缺了当场抛，不兜默认值（「桥只转发」 同一句理由）。
    /// </summary>
    public double Speed
    {
        get => _speed.Multiplier;
        set => _speed.SetMultiplier(value);
    }

    /// <summary>暂停：只冻逻辑，画面照跑。</summary>
    public void Pause() => Paused = true;

    /// <summary>解除逻辑暂停。</summary>
    public void Resume() => Paused = false;

    /// <summary>
    /// 表现层每帧那一发：按 <paramref name="realSeconds"/> 的现实时间推进。
    /// 倍率乘在流速上、攒够一个步长才推一步（「时钟解耦」）；暂停中返回 0 且什么都不做。
    /// 返回真正推了几步 —— 表现层拿它判「这一帧要不要重画」。
    /// </summary>
    public long AdvanceRealSeconds(double realSeconds)
    {
        if (Paused || !(realSeconds > 0))
        {
            return 0;
        }

        _carry += _speed.TicksPerFrame(realSeconds);
        var steps = (long)Math.Floor(_carry);
        if (steps <= 0)
        {
            return 0;
        }

        _carry -= steps;
        for (var i = 0L; i < steps; i++)
        {
            AdvanceOneStep();
        }

        return steps;
    }

    /// <summary>推 <paramref name="steps"/> 步。<b>暂停中一步都不走</b>，返回是否真的前进了。</summary>
    public bool Advance(long steps)
    {
        if (Paused || steps < 1)
        {
            return false;
        }

        for (var i = 0L; i < steps; i++)
        {
            AdvanceOneStep();
        }

        return true;
    }

    /// <summary>单步调试口：<b>暂停中也照走一步</b>（「停下来一格格看」是暂停的全部用处）。</summary>
    public void AdvanceStep() => AdvanceOneStep();

    /// <summary>批量单步调试口：<b>暂停中也照走 n 步</b>，返回真走了几步。给「跳到第 k 步看那一格」用。</summary>
    public long AdvanceDebug(long steps)
    {
        var moved = 0L;
        for (var i = 0L; i < Math.Max(0, steps); i++)
        {
            AdvanceOneStep();
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// 推一个逻辑步：<b>只推时钟，预算与求解由 <see cref="NavStepper"/> 的 <c>OnPulse</c> 开</b>。
    ///
    /// <para><b>这里原来多开了一次预算，已去掉</b>：本方法原先先调
    /// <see cref="NavSolver.BeginStep"/> 再推时钟，而 <see cref="NavStepper"/> 订阅的
    /// 正是这同一本 <see cref="GameClock"/>（<c>Advance(1)</c> 必发 <c>Pulse</c>，暂停时两者都不发），
    /// 于是<b>一个逻辑步开了两次预算</b> ⇒ <c>NavSolver.StepIndex</c> 按 <b>2×Tick</b> 走
    /// ⇒ 「距上次求解走了几步」那条重算触发在试验场里按配置值的<b>一半</b>触发
    /// （配 20 实际 10）。与第 44 行「步号只有一份」那条自述也对不上。
    /// 接了推进的宿主不受影响：那边只有 <c>OnPulse</c> 一处调用。
    /// 用例 <c>步号只有一份_推N步之后求解器步号与时钟逐字相等</c> 钉住。</para>
    /// </summary>
    private void AdvanceOneStep()
    {
        _clock.Advance(1);
    }

    // === 状态可查 ===

    /// <summary>
    /// 一发读出全量真值（状态级，「断言比快照」：断言只比这份，不比事件流）。
    /// 逐行逐字段念出，不摘要：<c>tick</c> → 网格（阻挡 / 静态成本 / 动态乘数 / 净空 / 两个版本）
    /// → 每个单位的移动解（位置、朝向、速度、位移、状态、卡住类别 / 步数、前视点、激活检查说法）
    /// → 求解记录账本 → 事件账本读数（含丢弃数）。
    ///
    /// <para><b>时间控制（<see cref="Paused"/> / <see cref="Speed"/>）与机位参数一样不进这一份</b>：
    /// 停一下、慢放一次都不改逻辑状态，进快照只会让逐字比对在没分叉的地方报分叉。</para>
    /// </summary>
    public string DebugSnapshot()
    {
        var lines = new List<string>
        {
            $"tick={Tick} seconds={ReplayArgs.Number(Tick / 10.0)} seed={Seed} " +
            $"random={(RandomEnabled ? 1 : 0)} agents={Stepper.AgentCount}",
        };
        lines.Add(_world.GridSnapshotSegment());
        foreach (var row in Stepper.DebugSnapshotSegment().Split('\n'))
        {
            if (row.Length > 0)
            {
                lines.Add(row);
            }
        }

        foreach (var row in SolveRecord().Split('\n'))
        {
            lines.Add(row);
        }

        lines.Add(Readout());
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 观测窗口读数。<b>「日志里没有」≠「没发生」</b>（「三条铁律」 第三条）：
    /// <c>dropped</c> 那一格是环形缓冲挤掉多少条，读数不带它就等于把「丢了」说成「没有」。
    /// <b>求解记录账本的 kept / dropped 在 <see cref="SolveRecord"/> 的头一行</b>，这里不重复一遍 ——
    /// 同一个数在快照里出现两次，改一处忘一处就是两份读数分叉的入口。
    /// </summary>
    public string Readout()
    {
        var events = _events.Ledger;
        return $"events total={ReplayArgs.Number(events.Total)} kept={ReplayArgs.Number(events.Count)} " +
               $"dropped={ReplayArgs.Number(events.Dropped)} capacity={ReplayArgs.Number(events.Capacity)}";
    }

    /// <summary>事件尾窗的行文本（人读；真值在 <see cref="DebugSnapshot"/> 里）。</summary>
    public IReadOnlyList<string> EventTailLines(int count)
    {
        var read = _events.Read(count);
        var lines = new string[read.Rows.Count];
        for (var i = 0; i < read.Rows.Count; i++)
        {
            lines[i] = read.Rows[i].Text;
        }

        return lines;
    }

    /// <summary>求解记录那几行（带头一行 kept / dropped；<b>取法是层里那一份</b>，不另渲染）。</summary>
    public string SolveRecord() => _world.SolveRecord();

    /// <summary>网格那一段（出图复核与「它为什么重算了」先读它；取法是层里那一份）。</summary>
    public string GridSegment() => _world.GridSnapshotSegment();

    /// <summary>全量检查点快照（取法是层里那一份：录制端与重放端同一形状，「重放装配单源」）。</summary>
    public string ReplaySnapshot() => _world.ReplaySnapshot();

    /// <summary>走位喂 <c>ModelAnimator.Travel</c> 的那个数（米每游戏秒）。<b>快进倍率由调用方除掉。</b></summary>
    public double TravelMetersPerGameSecondOf(string agentId) => Stepper.SolutionOf(agentId).SpeedMetersPerSecond;

    // === 行为可注入 ===

    /// <summary>
    /// 写路径的唯一分发口：注入、桥、录制与测试喂的都是这一发（「桥只转发」）。
    /// 动词名与参数次序住在 <see cref="NavVerbs"/>，本文件只是「这一侧接得住哪些」那份适配器
    /// —— 真正的实现是 <see cref="NavReplayWorld"/>，场子不重写一遍。
    /// </summary>
    public bool Inject(string cmd, string args) => NavVerbs.Dispatch(_world, cmd, args).Ok;

    /// <summary>注入那一发的完整读数（<c>ok</c> 字段 + 说法）。出图与逐字比对读它。</summary>
    public string InjectReadout(string cmd, string args)
    {
        var outcome = NavVerbs.Dispatch(_world, cmd, args);
        return outcome.Text;
    }

    /// <summary>换局 / 停机时调一次：退订时钟脉搏（「离树交还」：主动交还，不在调用点每次查有效性）。</summary>
    public void Detach() => _world.Detach();

    // === 随机（场子「固定」那一条口径）===

    /// <summary>
    /// 下一个随机数。<b><see cref="RandomEnabled"/> 为假时每次给 <see cref="FixedRandomValue"/></b>，
    /// 所以同条件重跑逐字一致。<b>本层不用随机做决策</b>：求解器那五处写死的选择（邻居序、入队序、
    /// 打戳数组）就是确定性的来源，场子递种子是给「将来递进来的那部分」一个同种子可复现的入口。
    /// </summary>
    public double NextRandom() => RandomEnabled ? _rng.NextDouble() : FixedRandomValue;
}
