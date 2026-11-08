#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.GugaFramework.Pathfinding;

// 寻路层的重放那一半 —— 装配（词表 + 三钩子 + Journal 追加 + 检查点）全走 ReplayAssembly
// （「重放装配单源」：公共那半只许有一份）。这里只落三件：动词表的默认宿主、检查点快照（**含网格 +
// 净空 + 两个版本**）、以及每步推进前把建造接线读一遍的那一下次序。
//
// **算路径是读进不了录，移动是写必须进录**：重放端只重放指令与地形改动，
// 路径自己重算即得同一结果 —— 前提就是这一份把网格、净空与两个版本逐字带进快照。

/// <summary>
/// 网格的检查点快照段 —— <b>逐字真值</b>：阻挡、两档成本、净空、两个版本，一个都不省。
///
/// <para>为什么净空也要进来：它是阻挡的<b>推论</b>（每格到最近阻挡格的米数），
/// 网格逐字相同就必然逐字相同 —— 所以它不是第二份真值，而是「重放端有没有真的按同一套
/// 距离算法重算过」的一个独立凭据。少了它，净空算法被改动（3-4 chamfer 那一档）时
/// 快照仍然全绿，而走位已经在穿墙。</para>
///
/// <para>形状：<b>一整段</b>，不逐格分段。四张表各占一个 token，行内逗号、行间斜线。
/// 逐格分段的话 L2 语义档（「断言三层」）会把「第几格」当成实体身份，于是调一格地形就红一片。</para>
///
/// <para>数字一律走 <see cref="ReplayArgs.Number"/>（<c>"R"</c> 往返格式，invariant）：
/// 少一个往返格式，同一台机器不同区域设置下 Journal 就会分叉。</para>
/// </summary>
public static class NavGridSnapshot
{
    /// <summary>把一张网格排成那一段。<b>纯函数</b>：只读网格，不发东西。</summary>
    public static string Render(NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var sb = new StringBuilder();
        sb.Append("navgrid space=").Append(grid.Space)
            .Append(" cells=").Append(Int(grid.CellsX)).Append('x').Append(Int(grid.CellsZ))
            .Append(" cell=").Append(ReplayArgs.Number(grid.CellMeters))
            .Append(" origin=").Append(ReplayArgs.Number(grid.OriginX)).Append(',').Append(ReplayArgs.Number(grid.OriginZ))
            .Append(" static=").Append(Int(grid.StaticVersion))
            .Append(" dynamic=").Append(Int(grid.DynamicVersion))
            .Append(" blocked=").Append(Blocked(grid))
            .Append(" pass=").Append(Pass(grid))
            .Append(" dyn=").Append(Dynamic(grid))
            .Append(" clearance=").Append(Clearance(grid));
        return sb.ToString();
    }

    private static string Blocked(NavGrid grid)
    {
        var sb = new StringBuilder();
        for (var z = 0; z < grid.CellsZ; z++)
        {
            if (z > 0)
            {
                sb.Append('/');
            }

            for (var x = 0; x < grid.CellsX; x++)
            {
                sb.Append(grid.IsBlocked(new NavCell(x, z)) ? '1' : '0');
            }
        }

        return sb.ToString();
    }

    private static string Pass(NavGrid grid) => Rows(grid, c => grid.PassCostAt(c));

    private static string Dynamic(NavGrid grid) => Rows(grid, c => grid.DynamicCostAt(c));

    private static string Clearance(NavGrid grid)
        => Rows(grid, c => grid.Clearance.At(c.X, c.Z));

    private static string Rows(NavGrid grid, Func<NavCell, double> read)
    {
        var sb = new StringBuilder();
        for (var z = 0; z < grid.CellsZ; z++)
        {
            if (z > 0)
            {
                sb.Append('/');
            }

            for (var x = 0; x < grid.CellsX; x++)
            {
                if (x > 0)
                {
                    sb.Append(',');
                }

                sb.Append(ReplayArgs.Number(read(new NavCell(x, z))));
            }
        }

        return sb.ToString();
    }

    private static string Int(int value) => ReplayArgs.Int(value);
}

/// <summary>
/// 寻路层的重放宿主 —— <b>动词表的默认那一侧</b>，也是检查点快照的取法。
///
/// <para>它是<b>公共层自己的重放件</b>：没有它，每个原型接入时都要自己再写一遍
/// 「网格 + 净空 + 两个版本 + 移动解」怎么拼成一段全量快照，而那正是「重放装配单源」 说的
/// 「公共那半只许有一份」。工单 08 接第一个原型时递的是<b>预算档与词表</b>，
/// 不是这一份装配。</para>
///
/// <para><b>每步推进之前先读建造接线</b>（<see cref="NavTerrainLink.Pump"/>）：
/// 砌墙与绕障要在同一逻辑步里看得见；晚一步生效的话，读数与建造那一步对不上。</para>
///
/// <para><b><see cref="ApplyMove"/> 这一格接上了</b>：移动流水线上现在有落位移那个写口
/// （<see cref="NavStepper.ApplyMove"/>），所以这一格不再挂在名单外 ——
/// 「写路径全录」 那一格「连测试排布口都要进 Journal」在寻路侧终于有个能接的落点：
/// 它在 <see cref="NavVerbs.InjectNames"/> 里，<b>录制端发出去就进录</b>，
/// 重放端照同一条落点写同一个位置，而检查点快照里的位置那一列逐字带上。</para>
/// </summary>
public sealed class NavReplayWorld : IReplayWorld, INavVerbSink
{
    // 名单 = 全表：公共层的动词表里每一格这一侧都接得住（写三 + 读四）。
    // 名单从常量拼，原型侧一个动词名都不另抄 —— 与建造层那份先例同一个口径。
    private static readonly IReadOnlySet<string> Verbs =
        new HashSet<string>(NavVerbs.Names, StringComparer.Ordinal);

    private readonly NavTerrainLink? _terrain;

    /// <summary>装配一个世界。数字档（预算、步进档）由宿主在造 <see cref="NavSolver"/> /
    /// <see cref="NavStepper"/> 时递进来，这里不替它们挑。</summary>
    public NavReplayWorld(NavSpace space, NavSolver solver, NavStepper stepper, GameClock clock,
        NavTerrainLink? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(stepper);
        ArgumentNullException.ThrowIfNull(clock);

        Space = space;
        Solver = solver;
        Stepper = stepper;
        Clock = clock;
        _terrain = terrain;
    }

    /// <summary>这个空间。</summary>
    public NavSpace Space { get; }

    /// <summary>求解器（call 类那两条要读它：算一条路、账本读数）。</summary>
    public NavSolver Solver { get; }

    /// <summary>移动解流水线。</summary>
    public NavStepper Stepper { get; }

    /// <summary>逻辑时钟。<b>唯一的时间来源</b>（「时钟解耦」）。</summary>
    public GameClock Clock { get; }

    /// <summary>建造接线（可省）。递了就每步推进之前读一遍。</summary>
    public NavTerrainLink? Terrain => _terrain;

    /// <summary>归一后的这一格。</summary>
    public NavGrid Grid => Space.Grid;

    // ---- IReplayWorld ------------------------------------------------------------

    /// <inheritdoc />
    public long Tick => Clock.Tick;

    /// <inheritdoc />
    public bool IsPaused => Clock.IsPaused;

    /// <summary>
    /// 推进 <paramref name="steps"/> 步。<b>每一步先读建造接线再推逻辑步</b>（次序是契约，
    /// 见 <see cref="NavTerrainLink.Pump"/>）。暂停中或步数不足 1 返 false 且什么都不做。
    /// </summary>
    public bool Advance(int steps = 1)
    {
        if (steps < 1 || Clock.IsPaused)
        {
            return false;
        }

        for (var i = 0; i < steps; i++)
        {
            _terrain?.Pump();
            if (!Clock.Advance(1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>推进到目标步（<see cref="ReplayAssembly"/> 三钩子里那一支；「暂停也照走」由本层自己兜：暂停中直接收手）。</summary>
    public void AdvanceTo(long target)
    {
        while (Clock.Tick < target && !Clock.IsPaused)
        {
            Advance(1);
        }
    }

    /// <summary>
    /// 全量快照（检查点快照的取法，录制端与重放端同一份）。
    /// <b>头部以 <c>tick=N</c> 起</b>，段间 <c>" | "</c>，每个实体段前两个 token 是身份 ——
    /// 取证器的 L2 语义档（「断言三层」）只认这个形状。
    /// </summary>
    public string ReplaySnapshot()
    {
        var sb = new StringBuilder();
        sb.Append("tick=").Append(Int(Clock.Tick))
            .Append(" seconds=").Append(ReplayArgs.Number(Clock.Tick / 10.0))
            .Append(" space=").Append(Space.Space)
            .Append(" step=").Append(Int(Stepper.StepIndex))
            .Append(" agents=").Append(Int(Stepper.AgentCount))
            .Append(" | ")
            .Append(NavGridSnapshot.Render(Grid))
            .Append(" | ")
            .Append(Flat(Stepper.ReplaySnapshotSegment()));
        return sb.ToString();
    }

    /// <summary>网格那一段（<b>调试用</b>：出图复核与「它为什么重算了」先读它）。逐字与快照里那一段同源。</summary>
    public string GridSnapshotSegment() => NavGridSnapshot.Render(Grid);

    /// <summary>退订（换局 / 停机时调一次，「离树交还」）。</summary>
    public void Detach()
    {
        Stepper.Detach();
        _terrain?.Detach();
    }

    // ---- INavVerbSink ------------------------------------------------------------

    /// <inheritdoc />
    public string HostLabel => $"寻路层重放宿主（空间 {Space.Space}）";

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedVerbs => Verbs;

    /// <inheritdoc />
    public NavVerbResult StampTerrain(NavCell cell, NavTerrainLayer layer, double value)
        => NavTerrain.Apply(Grid, cell, layer, value);

    /// <inheritdoc />
    public NavVerbResult SetOrder(string agentId, NavCommand command)
    {
        if (!Registered(agentId))
        {
            return NavVerbResult.Refused("UnknownAgent", $"id={agentId}");
        }

        Stepper.Issue(agentId, command);
        return NavVerbResult.Applied(
            $"id={agentId} kind={command.Kind} tolerance={ReplayArgs.Number(command.ArriveToleranceMeters)}");
    }

    /// <inheritdoc />
    /// <remarks>
    /// 这一格在 <see cref="NavVerbs.InjectNames"/> 里 ⇒ <b>发出去就进 Journal</b>（「写路径全录」：
    /// 写路径必须全录，连测试排布口都要录），重放端照同一条落点写同一个位置。
    /// 位置那一列在检查点快照里逐字带上下一次脉冲的读数，所以「落点没落上」当场对得出来。
    /// id 没登记过按「打不成」回报（<c>ok=false</c>）而不是抛 —— 抛留给「这一格压根没接」。
    /// </remarks>
    public NavVerbResult ApplyMove(string agentId, NavPoint to, double facingDegrees)
    {
        if (!Registered(agentId))
        {
            return NavVerbResult.Refused("UnknownAgent", $"id={agentId}");
        }

        Stepper.ApplyMove(agentId, to, facingDegrees);
        return NavVerbResult.Applied($"id={agentId} to={to} 朝向={ReplayArgs.Number(facingDegrees)}");
    }

    /// <inheritdoc />
    public string FindPath(NavPoint from, NavPoint to, double radiusMeters)
    {
        var path = Solver.Solve(from, to, radiusMeters);
        return $"from={from} to={to} r={ReplayArgs.Number(radiusMeters)} {path}";
    }

    /// <inheritdoc />
    public string ReadState(string agentId)
        => Registered(agentId) ? Stepper.SolutionOf(agentId).Describe() : $"未知单位 id={agentId}";

    /// <inheritdoc />
    public string AsciiMap()
    {
        var grid = Grid;
        var sb = new StringBuilder();
        sb.Append("navgrid space=").Append(grid.Space)
            .Append(" cells=").Append(Int(grid.CellsX)).Append('x').Append(Int(grid.CellsZ))
            .Append(" cell=").Append(ReplayArgs.Number(grid.CellMeters))
            .Append(" origin=").Append(ReplayArgs.Number(grid.OriginX)).Append(',').Append(ReplayArgs.Number(grid.OriginZ))
            .Append(" static=").Append(Int(grid.StaticVersion))
            .Append(" dynamic=").Append(Int(grid.DynamicVersion))
            .Append(" 图例 #地形阻挡 p静态成本高 d动态乘数高 x两者都高 .通行")
            .Append(' ');
        for (var z = 0; z < grid.CellsZ; z++)
        {
            if (z > 0)
            {
                sb.Append('/');
            }

            for (var x = 0; x < grid.CellsX; x++)
            {
                var cell = new NavCell(x, z);
                if (grid.IsBlocked(cell))
                {
                    sb.Append('#');
                    continue;
                }

                var slow = grid.PassCostAt(cell) > 1.0;
                var drag = grid.DynamicCostAt(cell) > 1.0;
                sb.Append((slow, drag) switch
                {
                    (false, false) => '.',
                    (true, false) => 'p',
                    (false, true) => 'd',
                    _ => 'x',
                });
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string SolveRecord()
    {
        var records = Solver.Records;
        var sb = new StringBuilder();
        sb.Append("solve kept=").Append(Int(records.Count))
            .Append(" dropped=").Append(Int(records.Dropped))
            .Append(" 容量=").Append(Int(records.Capacity));
        var rows = records.Snapshot();
        for (var i = 0; i < rows.Length; i++)
        {
            sb.Append('\n').Append(rows[i].Describe());
        }

        return sb.ToString();
    }

    // ---- 装配那一行 ---------------------------------------------------------------

    /// <summary>
    /// 装一个重放装配 —— <b>动词名单递 <see cref="NavVerbs.InjectNames"/> 而不是全表</b>：
    /// 递全表的话 call 类会被录进去（「写路径进 Journal」：只读不进录）。
    /// </summary>
    public static ReplayAssembly OpenSession(NavReplayWorld world, int seed, bool recording = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new ReplayAssembly(world, seed, NavVerbs.InjectNames, NavVerbs.InjectorFor(world),
            world.AdvanceTo, recording: recording);
    }

    // ---- 内部 ---------------------------------------------------------------------

    /// <summary>这个 id 登记过没有（移动解只认登记过的单位，抛的那一处在它自己那儿）。
    /// <b>按登记次序扫数组</b>，不走字典枚举序（spec「确定性靠结构」）。</summary>
    private bool Registered(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return false;
        }

        var ids = Stepper.AgentIds;
        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], agentId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>层内那一段是多行的（每个单位一行），并进段格式时把换行换成空格 ——
    /// 与建造层那份快照、宿主侧技能层那一段同一口径。</summary>
    private static string Flat(string segment)
    {
        var lines = segment.Split('\n');
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(" | ");
            }

            sb.Append(line);
        }

        return sb.ToString();
    }

    private static string Int(int value) => ReplayArgs.Int(value);

    private static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);
}
