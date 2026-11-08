#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 一个请求身份 —— <b>空间 + 起点 + 宽度</b>。三列，<b>目标不在里面，两个版本也不在里面</b>。
///
/// <para><b>目标不进身份</b>：「目标移动超过阈值」那一条触发正是要拿上一次的目标比出来的，
/// 目标进了身份就每次都是一个新身份、没有上一次可比，那条触发永远判不出来。</para>
///
/// <para><b>版本不进身份</b>：版本是触发（「静态版本变了」），不是身份。
/// 身份里带着它，重算计数就会每改一次地形就归零，而「它重算了 47 次」是数重算次数的读数。</para>
///
/// <para>注意它<b>不是</b>缓存键（<see cref="NavPathCacheKey"/> 是五列 + 起点）：键答「这条路能不能复用」，
/// 身份答「这是谁在问」。</para>
/// </summary>
public readonly record struct NavRequestIdentity(SpaceId Space, NavPoint Start, double WidthMeters);

/// <summary>
/// 点到点求解器 —— <b>seam 的第一个 adapter</b>（spec「模块边界」：seam 定在「怎么求一条路」）。
/// 纯 C#、零引擎依赖：不引 <c>AStar*</c>、不引 <c>NavigationServer</c>（「寻路单入口」）。
///
/// <para><b>只答一件事：给出一条路 + 一条求解记录</b>。不答「下一步站哪」—— 那是移动解
/// （工单 04）；不在这里长出「该走哪一段」，是因为「寻路单入口」 之下「单位下一步站哪」
/// 只能有一个东西说了算，而那个东西还没落地。</para>
///
/// <para><b>确定性靠结构</b>（spec「确定性靠结构」一节，逐条落在这里）：</para>
/// <list type="bullet">
/// <item>开放集是二叉堆，键 <c>(f, 入队序号)</c> —— 同 f 时按入队先后，不按格子下标。</item>
/// <item>关闭集 / 已算 g 的标记是<b>跑号打戳的数组</b>，每轮 +1，不每轮清零。</item>
/// <item>邻居枚举顺序<b>写死</b>（<see cref="NeighborOrder"/>）：先四邻后四角。
/// 这个次序本身不含偏好，它的作用是把「f 相同按入队先后」里的那个「先后」钉死 ——
/// 换个次序就换一条同长度的路，而那条路是任意的。</item>
/// <item>回溯路径走数组下标，<b>本文件没有一处 <c>foreach</c> 遍历字典或集合</b>。</item>
/// </list>
///
/// <para><b>预算</b>：每请求一个扩展格数上限、每步一个全局上限（<see cref="BeginStep"/> 递）。
/// 触顶<b>回部分路径 + 原因 <see cref="NavPathReason.BudgetExceeded"/></b>，不抛不返空 ——
/// 「被堵死时能被发现」在读数上就长成「走不到位」这个已经存在的可读事实。</para>
/// </summary>
public sealed class NavSolver
{
    /// <summary>邻居枚举顺序：先四邻（−X, +X, −Z, +Z），后四角（−X−Z, +X−Z, −X+Z, +X+Z）。</summary>
    private static readonly (int Dx, int Dz)[] NeighborOrder =
    {
        (-1, 0), (1, 0), (0, -1), (0, +1),
        (-1, -1), (1, -1), (-1, +1), (1, +1),
    };

    /// <summary>搜索工作区 —— 一次求解的私有暂存，跨请求复用（不每次分配：每步几十个请求，每次分配 6 份数组是一笔纯浪费）。</summary>
    private readonly double[] _g;
    private readonly int[] _gStamp;       // 这一格当前这次搜索算过 g 没有（跑号打戳，免每轮清零）
    private readonly int[] _cameFrom;     // 前驱格的摊平下标
    private readonly int[] _closedStamp;  // 关闭集（跑号打戳）
    private int[] _heapCell;
    private double[] _heapF;
    private long[] _heapSeq;              // 入队序号 —— 同 f 时的次序键
    private int _heapCount;
    private int _stamp;
    private long _enqueueSeq;

    private readonly NavGrid _grid;
    private readonly NavSolveBudget _budget;
    private readonly NavRecomputeTuning _tuning;
    private readonly NavPathCache _cache;
    private readonly int _trackedRequestCapacity;

    private readonly Dictionary<NavRequestIdentity, RequestTrack> _tracks;
    private readonly Queue<NavRequestIdentity> _trackFifo;
    private readonly NavSolveLedger _ledger;

    private int _stepIndex;
    private int _stepRemaining;

    /// <summary>
    /// 造一个求解器。<b>所有数字参数必递，公共层一个缺省都不给</b>（照 <c>NavGridSpec</c> 的纪律）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">预算档 / 阈值 / 容量不成形状。</exception>
    public NavSolver(
        NavSpace space,
        NavSolveBudget budget,
        NavRecomputeTuning tuning,
        int cacheCapacity,
        int recordCapacity,
        int trackedRequestCapacity)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(tuning);
        NavSolveCheck.Expansions(budget.PerRequestExpansions, nameof(budget.PerRequestExpansions));
        NavSolveCheck.Capacity(cacheCapacity, nameof(cacheCapacity));
        NavSolveCheck.Capacity(recordCapacity, nameof(recordCapacity));
        NavSolveCheck.Capacity(trackedRequestCapacity, nameof(trackedRequestCapacity));
        if (tuning.GoalMoveToleranceMeters < 0
            || double.IsNaN(tuning.GoalMoveToleranceMeters)
            || double.IsInfinity(tuning.GoalMoveToleranceMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tuning),
                tuning.GoalMoveToleranceMeters,
                $"目标移动阈值必须是非负有限数，收到 {tuning.GoalMoveToleranceMeters.ToString(CultureInfo.InvariantCulture)} —— 阈值由宿主递，层里没有缺省（「桥只转发」）。");
        }

        NavSolveCheck.Expansions(tuning.NotArrivedStepCap, nameof(tuning.NotArrivedStepCap));

        _grid = space.Grid;
        Space = space;
        _budget = budget;
        _tuning = tuning;
        _trackedRequestCapacity = trackedRequestCapacity;
        _cache = new NavPathCache(cacheCapacity);
        _ledger = new NavSolveLedger(recordCapacity);
        _tracks = new Dictionary<NavRequestIdentity, RequestTrack>(trackedRequestCapacity);
        _trackFifo = new Queue<NavRequestIdentity>(trackedRequestCapacity);

        var cells = _grid.CellsX * _grid.CellsZ;
        _g = new double[cells];
        _gStamp = new int[cells];
        _cameFrom = new int[cells];
        _closedStamp = new int[cells];
        _heapCell = new int[cells + 8];
        _heapF = new double[cells + 8];
        _heapSeq = new long[cells + 8];
    }

    /// <summary>这个求解器解的是哪一个空间。</summary>
    public NavSpace Space { get; }

    /// <summary>求解记录账本（有界，环形）。</summary>
    public NavSolveLedger Records => _ledger;

    /// <summary>路径缓存（有界）。命中与未命中在这里也各计一次。</summary>
    public NavPathCache Cache => _cache;

    /// <summary>当前是第几个逻辑步（从 0 起；每 <see cref="BeginStep"/> 一次 +1）。</summary>
    public int StepIndex => _stepIndex;

    /// <summary>本步全局预算还剩多少次扩展。</summary>
    public int StepExpansionsRemaining => _stepRemaining;

    /// <summary>预算档：每请求的扩展上限。</summary>
    public int PerRequestExpansions => _budget.PerRequestExpansions;

    /// <summary>重算判据那一档（目标移动阈值、没到位步数上限）—— <b>与求解器用的是同一份</b>，
    /// 所以步进那一侧的复用判据（<c>NavStepper.ReuseValidPath</c>）与求解器的重算账本读的是同一组数，不会两处各一套。</summary>
    public NavRecomputeTuning RecomputeTuning => _tuning;

    /// <summary>
    /// 开一个新逻辑步，并把<b>本步全局扩展上限</b>装上（spec「预算」：每请求一个 + 每步一个）。
    /// 第一次求解之前不必调 —— 步数是 0，全局预算是 0，于是那一发直接触顶回一条只有一个点的部分路径，
    /// 这与「没调就开跑」是同一件可读的事。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">上限小于 1。</exception>
    public void BeginStep(int maxExpansionsInStep)
    {
        NavSolveCheck.Expansions(maxExpansionsInStep, nameof(maxExpansionsInStep));
        _stepIndex++;
        _stepRemaining = maxExpansionsInStep;
    }

    /// <summary>
    /// 求解一条从 <paramref name="start"/> 到 <paramref name="goal"/> 的路。
    /// 返回值<b>永不为空折线</b>：拿不到路时交回起点那一个点 + 状态 + 原因（规则：不抛不返空）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">半径为负数 / 非有限数。</exception>
    public NavPath Solve(NavPoint start, NavPoint goal, double radiusMeters)
    {
        NavSolveCheck.Radius(radiusMeters, nameof(radiusMeters));

        var identity = new NavRequestIdentity(Space.Space, start, radiusMeters);
        var key = new NavPathCacheKey
        {
            Space = Space.Space,
            Start = start,
            Goal = goal,
            WidthMeters = radiusMeters,
            StaticVersion = _grid.StaticVersion,
            DynamicVersion = _grid.DynamicVersion,
        };

        var track = FindTrack(identity);
        var triggers = track is null
            ? NavRecomputeTriggers.None
            : NavRecomputeJudge.Judge(new NavRecomputeProbe(
                track.LastStaticVersion,
                _grid.StaticVersion,
                track.LastGoal,
                goal,
                _tuning.GoalMoveToleranceMeters,
                track.LastReason == NavPathReason.BudgetExceeded,
                _stepIndex - track.LastSolveStep,
                _tuning.NotArrivedStepCap));

        // 缓存命中：复用同一条路，扩展 0 次。命中那一行的 ExpandedCells 是 0，账本里数得出来。
        if (_cache.TryGet(key, out var cached))
        {
            var hit = cached with { ExpandedCells = 0 };
            return Commit(identity, track, key, hit, true, triggers);
        }

        var path = Compute(start, goal, radiusMeters, identity, track, key, triggers);

        // 只有走通的那条进缓存：缓存一份「没走到」等于把一次预算触顶固化成常驻答案。
        if (path.Status == NavPathStatus.Reached)
        {
            _cache.Store(key, path);
        }

        return path;
    }

    /// <summary>真算一遍：先判两端进不进可行域，再跑 A*，最后拉直。</summary>
    private NavPath Compute(
        NavPoint start,
        NavPoint goal,
        double radiusMeters,
        NavRequestIdentity identity,
        RequestTrack? track,
        NavPathCacheKey key,
        NavRecomputeTriggers triggers)
    {
        if (!_grid.Clearance.TryLocal(start, out var sx, out var sz, out _, out _))
        {
            return Commit(identity, track, key, Degenerate(start, NavPathReason.StartBlocked), false, triggers);
        }

        if (!_grid.Clearance.TryLocal(goal, out var gx, out var gz, out _, out _))
        {
            return Commit(identity, track, key, Degenerate(start, NavPathReason.GoalBlocked), false, triggers);
        }

        var startCell = new NavCell(sx, sz);
        var goalCell = new NavCell(gx, gz);

        // 端点判据：那一格的净空得放得下这个半径。半径放不下就不该启动这个命令
        // （spec「卡住分三类」第一类：地形无路 → 不该启动）。
        if (!NavPathStraightener.Feasible(_grid, startCell, radiusMeters))
        {
            return Commit(identity, track, key, Degenerate(start, NavPathReason.StartBlocked), false, triggers);
        }

        if (!NavPathStraightener.Feasible(_grid, goalCell, radiusMeters))
        {
            return Commit(identity, track, key, Degenerate(start, NavPathReason.GoalBlocked), false, triggers);
        }

        // 两档预算取小的那一档：触顶只记一个原因（BudgetExceeded），但「扩展了几格」与
        // 「本步还剩多少」两个读数会把是谁顶的说清楚。
        var limit = Math.Min(_budget.PerRequestExpansions, _stepRemaining);
        var result = Search(startCell, goalCell, goal, radiusMeters, limit);
        _stepRemaining -= result.Expanded;

        var cells = Reconstruct(startCell, result.Best);
        NavPathStatus status;
        NavPathReason reason;
        NavPoint? partialAt = null;
        NavPoint pullTarget = goal;

        if (result.Reached)
        {
            status = NavPathStatus.Reached;
            reason = NavPathReason.Reached;
        }
        else
        {
            // 触顶与穷尽都交回部分路径，区别只在原因：spec「卡住分三类」把「预算触顶」
            // 单列一档，正是因为它和「地形无路」的处置完全不同（前者上层可以决定再要一次）。
            status = NavPathStatus.Partial;
            reason = result.BudgetHit ? NavPathReason.BudgetExceeded : NavPathReason.GoalUnreachable;
            partialAt = _grid.CenterOf(result.Best);

            // 拉直的终点是<strong>走到的那一格</strong>，不是请求的目标点。
            // 交回一条「从起点直指目标点」的折线等于宣称走通了没走通的那条路 ——
            // 而这一层存在的理由正是「它到底走没走到」要是一条读数而不是一个猜。
            pullTarget = partialAt.Value;
        }

        var corners = NavPathStraightener.Pull(_grid, cells, start, pullTarget, radiusMeters);

        var path = new NavPath
        {
            Corners = corners,
            CellPath = cells,
            Status = status,
            Reason = reason,
            TotalCost = result.Cost,
            ExpandedCells = result.Expanded,
            PartialAt = partialAt,
        };

        return Commit(identity, track, key, path, false, triggers);
    }

    /// <summary>压根没起步的那一档：交回起点那一个点，不交回空数组。</summary>
    private static NavPath Degenerate(NavPoint start, NavPathReason reason)
        => new()
        {
            Corners = new[] { start },
            CellPath = Array.Empty<NavCell>(),
            Status = NavPathStatus.Failed,
            Reason = reason,
            TotalCost = 0.0,
            ExpandedCells = 0,
        };

    /// <summary>记一行求解记录 + 更新请求追踪 —— 每一次求解都走这里，所以「请求了但没到」永远是可读事实。</summary>
    private NavPath Commit(
        NavRequestIdentity identity,
        RequestTrack? track,
        NavPathCacheKey key,
        NavPath path,
        bool cacheHit,
        NavRecomputeTriggers triggers)
    {
        var count = (track?.SolveCount ?? 0) + 1;

        _ledger.Append(new NavSolveRecord
        {
            Space = key.Space,
            Start = key.Start,
            Goal = key.Goal,
            RadiusMeters = identity.WidthMeters,
            StaticVersion = key.StaticVersion,
            DynamicVersion = key.DynamicVersion,
            Status = path.Status,
            Reason = path.Reason,
            IsPartial = path.Status == NavPathStatus.Partial,
            ExpandedCells = path.ExpandedCells,
            TotalCost = path.TotalCost,
            CornerCount = path.CornerCount,
            CellCount = path.CellPath.Count,
            BudgetPerRequest = _budget.PerRequestExpansions,
            BudgetStepRemainingAfter = _stepRemaining,
            CacheHit = cacheHit,
            RecomputeTriggers = triggers,
            RecomputeCount = count,
            PartialAt = path.PartialAt,
        });

        SaveTrack(identity, new RequestTrack
        {
            SolveCount = count,
            LastStaticVersion = _grid.StaticVersion,
            LastGoal = key.Goal,
            LastReason = path.Reason,
            LastSolveStep = _stepIndex,
        });

        return path;
    }

    private RequestTrack? FindTrack(NavRequestIdentity identity) => _tracks.GetValueOrDefault(identity);

    private void SaveTrack(NavRequestIdentity identity, RequestTrack track)
    {
        if (_tracks.ContainsKey(identity))
        {
            _tracks[identity] = track;
            return;
        }

        // 有界 + 先进先出淘汰。走 Queue 的次序而不是枚举字典：哈希桶的枚举序跟着插入历史走，
        // 拿它挑「最老的那条」就是随机的，重放会在同一份命令流上分叉。
        if (_trackFifo.Count >= _trackedRequestCapacity)
        {
            _tracks.Remove(_trackFifo.Dequeue());
        }

        _tracks.Add(identity, track);
        _trackFifo.Enqueue(identity);
    }

    /// <summary>一次 A* 的结果。全部出参打包 —— 七个 out 参数记不住哪两个是哪一个。</summary>
    private readonly record struct SearchResult(NavCell Best, bool Reached, bool BudgetHit, int Expanded, double Cost);

    /// <summary>
    /// A* 本体。<b>逐点可重放</b>：堆键 (f, 入队序号)、打戳数组、写死的邻居次序、double 算术（「double 与不变文化」）。
    /// </summary>
    private SearchResult Search(NavCell startCell, NavCell goalCell, NavPoint goal, double radiusMeters, int limit)
    {
        _stamp++;
        _enqueueSeq = 0;
        _heapCount = 0;

        var minCost = MinEffectiveCost();
        var cellMeters = _grid.CellMeters;
        var diagonal = Math.Sqrt(2.0) * cellMeters;
        var startIndex = Index(startCell);
        var goalIndex = Index(goalCell);

        _g[startIndex] = 0.0;
        _gStamp[startIndex] = _stamp;
        _cameFrom[startIndex] = -1;

        var startH = Heuristic(startCell, goalCell, goal, minCost);
        var bestCell = startCell;
        var bestH = startH;
        Push(startIndex, startH);

        var expanded = 0;
        var reached = false;

        while (true)
        {
            if (_heapCount == 0)
            {
                // 开放集空了：地形断开了。不是触顶 —— 触顶那支在下面。
                break;
            }

            if (expanded >= limit)
            {
                return new SearchResult(bestCell, false, true, expanded, _g[Index(bestCell)]);
            }

            var current = PopMin();
            if (_closedStamp[current] == _stamp)
            {
                continue; // 过期条目（同 f 的重复入队），跳过
            }

            _closedStamp[current] = _stamp;
            expanded++;

            if (current == goalIndex)
            {
                bestCell = new NavCell(current % _grid.CellsX, current / _grid.CellsX);
                reached = true;
                break;
            }

            var cur = new NavCell(current % _grid.CellsX, current / _grid.CellsX);
            var gCur = _g[current];
            var hCur = Heuristic(cur, goalCell, goal, minCost);
            if (hCur < bestH)
            {
                bestH = hCur;
                bestCell = cur;
            }

            for (var k = 0; k < NeighborOrder.Length; k++)
            {
                var (dx, dz) = NeighborOrder[k];
                var next = new NavCell(cur.X + dx, cur.Z + dz);
                if (!NavPathStraightener.Feasible(_grid, next, radiusMeters))
                {
                    continue;
                }

                var isDiagonal = dx != 0 && dz != 0;
                if (isDiagonal && !NavPathStraightener.DiagonalStepLegal(_grid, cur, dx, dz, radiusMeters))
                {
                    continue; // 不许切角
                }

                // 计价口径：逐段的米 × **到达格**的通过成本（静态 × 动态）。
                var gNext = gCur + ((isDiagonal ? diagonal : cellMeters) * _grid.EffectiveCostAt(next));
                var nextIndex = Index(next);
                if (_gStamp[nextIndex] == _stamp && gNext >= _g[nextIndex])
                {
                    continue;
                }

                _g[nextIndex] = gNext;
                _gStamp[nextIndex] = _stamp;
                _cameFrom[nextIndex] = current;
                Push(nextIndex, gNext + Heuristic(next, goalCell, goal, minCost));
            }
        }

        return new SearchResult(
            bestCell,
            reached,
            false,
            expanded,
            _g[Index(bestCell)]);
    }

    /// <summary>
    /// 启发函数：欧氏距离 × 全场最低通过成本。
    ///
    /// <para><b>乘最低成本而不是乘 1</b>：<c>SetPassCost</c> 允许成本低于 1（只要求为正），
    /// 乘 1 会高估，高估让最优解丢掉一半。</para>
    ///
    /// <para><b>目标那一格记 0</b>：g 记到格心、终点却是请求给的目标点，
    /// 最后一段的 h 会高估（真值是 0），不钉 0 就不可采纳。</para>
    ///
    /// <para>这样 h <b>一致</b>（逐段成本 ≥ 欧氏距离 × 最低成本），所以关闭的格不必重开，
    /// 「扩展格数」这个读数才是可比的。</para>
    /// </summary>
    private double Heuristic(NavCell cell, NavCell goalCell, NavPoint goal, double minCost)
        => cell == goalCell ? 0.0 : (_grid.CenterOf(cell).DistanceTo(goal) * minCost);

    /// <summary>全场最低通过成本（静态 × 动态）。每次求解扫一遍：网格是采样的表，不是长驻索引。</summary>
    private double MinEffectiveCost()
    {
        var min = double.MaxValue;
        for (var z = 0; z < _grid.CellsZ; z++)
        {
            for (var x = 0; x < _grid.CellsX; x++)
            {
                var cost = _grid.EffectiveCostAt(new NavCell(x, z));
                if (cost < min)
                {
                    min = cost;
                }
            }
        }

        return min;
    }

    /// <summary>回溯格子路径。走数组下标（<see cref="_cameFrom"/>），不碰任何字典或集合的枚举序。</summary>
    private NavCell[] Reconstruct(NavCell startCell, NavCell best)
    {
        var chain = new List<NavCell>(16) { best };
        var at = Index(best);
        var guard = _g.Length + 1; // 环形的护栏：正成本下链必然收到起点，这里只防「万一」不变成死循环
        while (at != Index(startCell) && guard-- > 0)
        {
            at = _cameFrom[at];
            if (at < 0)
            {
                break;
            }

            chain.Add(new NavCell(at % _grid.CellsX, at / _grid.CellsX));
        }

        chain.Reverse();
        return chain.ToArray();
    }

    private int Index(NavCell cell) => cell.Z * _grid.CellsX + cell.X;

    /// <summary>
    /// 入堆。键是 <c>(f, 入队序号)</c>：同 f 时按入队先后，
    /// 所以同一条命令流重跑一次得到逐字相同的路径（「断言三层」 的 L3 那一档）。
    /// </summary>
    private void Push(int cell, double f)
    {
        if (_heapCount == _heapCell.Length)
        {
            Array.Resize(ref _heapCell, _heapCell.Length * 2);
            Array.Resize(ref _heapF, _heapF.Length * 2);
            Array.Resize(ref _heapSeq, _heapSeq.Length * 2);
        }

        var i = _heapCount++;
        _heapCell[i] = cell;
        _heapF[i] = f;
        _heapSeq[i] = _enqueueSeq++;
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (!Less(i, parent))
            {
                break;
            }

            Swap(i, parent);
            i = parent;
        }
    }

    private int PopMin()
    {
        var top = _heapCell[0];
        _heapCount--;
        if (_heapCount > 0)
        {
            _heapCell[0] = _heapCell[_heapCount];
            _heapF[0] = _heapF[_heapCount];
            _heapSeq[0] = _heapSeq[_heapCount];
            var i = 0;
            while (true)
            {
                var left = (i * 2) + 1;
                if (left >= _heapCount)
                {
                    break;
                }

                var right = left + 1;
                var best = right < _heapCount && Less(right, left) ? right : left;
                if (!Less(best, i))
                {
                    break;
                }

                Swap(i, best);
                i = best;
            }
        }

        return top;
    }

    /// <summary>f 小的在前；f 相同按入队序号小的在前（先来先得，逐字可重放）。</summary>
    private bool Less(int a, int b)
    {
        var fa = _heapF[a];
        var fb = _heapF[b];
        if (fa != fb)
        {
            return fa < fb;
        }

        return _heapSeq[a] < _heapSeq[b];
    }

    private void Swap(int a, int b)
    {
        (_heapCell[a], _heapCell[b]) = (_heapCell[b], _heapCell[a]);
        (_heapF[a], _heapF[b]) = (_heapF[b], _heapF[a]);
        (_heapSeq[a], _heapSeq[b]) = (_heapSeq[b], _heapSeq[a]);
    }

    /// <summary>一个请求身份的追踪账（一次求解之后更新）：只留重算判据要用的那几个数。</summary>
    private sealed class RequestTrack
    {
        public int SolveCount { get; init; }

        public int LastStaticVersion { get; init; }

        public NavPoint LastGoal { get; init; }

        public NavPathReason LastReason { get; init; }

        public int LastSolveStep { get; init; }
    }
}
