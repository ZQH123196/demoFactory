#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Pathfinding;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 点到点求解器（工单 03）：拉直、缓存、预算、求解记录、四条重算触发。
///
/// <para>断言强度照「断言比快照」 —— 只比<b>状态级读数</b>（折线、格子路径、账本那一行），
/// 不拿事件流逐字相等当断言。工单点名的「同一请求两次求解逐字相等」是状态级逐字，合法。</para>
///
/// <para>拉直的判据分三档，缺一档就可能交回一条「看着能用、实际是格子阶梯」的折线：
/// ① 折点逐字；② 拐点处朝向连续（零长段 / 反向 / 同向冗余点三条都判红）；
/// ③ 每一段逐点扫过去都在可行域里（独立于被测实现的一段暴力采样，
/// 照上一期「真值由用例里一份独立的暴力计算给出」的纪律）。</para>
/// </summary>
public class NavSolverTests
{
    private static readonly SpaceId Deck = new("巨兽甲板");
    private static readonly SpaceId Arena = new("竞技场");

    private const double GoalTolerance = 0.5;
    private const int NotArrivedStepCap = 30;

    private static NavGridSpec Spec(
        IReadOnlyList<NavCell> blocked,
        int x = 8,
        int z = 8,
        double cell = 1.0,
        SpaceId? space = null)
        => new()
        {
            Space = space ?? Deck,
            CellMeters = cell,
            CellsX = x,
            CellsZ = z,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = blocked,
        };

    private static NavSolver Solver(NavGridSpec spec, int budget = 4000)
        => new(
            new NavSpace(spec.Space, new NavGrid(spec)),
            new NavSolveBudget { PerRequestExpansions = budget },
            new NavRecomputeTuning { GoalMoveToleranceMeters = GoalTolerance, NotArrivedStepCap = NotArrivedStepCap },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);

    private static NavSolver Solver(NavGridSpec spec, int budget, int stepCap)
        => new(
            new NavSpace(spec.Space, new NavGrid(spec)),
            new NavSolveBudget { PerRequestExpansions = budget },
            new NavRecomputeTuning { GoalMoveToleranceMeters = GoalTolerance, NotArrivedStepCap = stepCap },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);

    /// <summary>L 形墙：横向 (2,0)..(4,0) + 纵向 (4,1)..(4,4)。绕它走一圈再拉直，是拉直最直观的场。</summary>
    private static List<NavCell> L形墙()
    {
        var blocked = new List<NavCell>();
        for (var x = 2; x <= 4; x++)
        {
            blocked.Add(new NavCell(x, 0));
        }

        for (var z = 1; z <= 4; z++)
        {
            blocked.Add(new NavCell(4, z));
        }

        return blocked;
    }

    /// <summary>一格宽走廊：z=0 与 z=2 全封，只留 z=1 那一行（8 格全通）。</summary>
    private static List<NavCell> 走廊()
    {
        var blocked = new List<NavCell>();
        for (var x = 0; x < 8; x++)
        {
            blocked.Add(new NavCell(x, 0));
            blocked.Add(new NavCell(x, 2));
        }

        return blocked;
    }

    private static string 格路(IReadOnlyList<NavCell> cells)
    {
        var parts = new string[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            parts[i] = cells[i].ToString();
        }

        return string.Join(" ", parts);
    }

    // ─────────────────────────── 拉直 ───────────────────────────

    [Fact]
    public void 空场拉成一条直线_不是格子阶梯()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>()));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);

        path.Status.ShouldBe(NavPathStatus.Reached);
        path.Reason.ShouldBe(NavPathReason.Reached);

        // 拉直前的格子路径逐字：8 格的对角阶梯。
        格路(path.CellPath).ShouldBe(
            "(0, 0) (1, 1) (2, 2) (3, 3) (4, 4) (5, 5) (6, 6) (7, 7)",
            "拉直前是格子路径：一条 8 格的对角阶梯");

        // 拉直后的任意角折线逐字：一个拐点都不留，起点与目标点逐字照收。
        path.DescribeCorners().ShouldBe(
            "(0.5, 0.5) -> (7.5, 7.5)",
            "拉直后是一条直线，两个点就是起点与目标点");
        path.CornerCount.ShouldBe(2);
        path.CellPath.Count.ShouldBe(8, "格路 8 格 vs 拐点 2 个 —— 折点少了六成，阶梯的指纹不成立");
        Math.Abs(path.TotalCost - 9.899494936611667).ShouldBeLessThan(1e-12, "7 段 √2 米 × 成本 1");
        path.ExpandedCells.ShouldBe(8, "对角那一链 f 最小，先被扩完；旁支一条都轮不上");

        像格子阶梯(path, 1.0).ShouldBeFalse("每一段都恰好一格长才是格子阶梯的指纹；这里一段是 √2 × 7");
        断言拐点处朝向连续(path, "直线段没有拐点，这一条是「不返工」的护栏");
    }

    [Fact]
    public void 绕墙拉直_折线逐字且拐点处朝向连续()
    {
        var solver = Solver(Spec(L形墙()));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(6.5, 6.5), 0.0);

        path.Status.ShouldBe(NavPathStatus.Reached);
        格路(path.CellPath).ShouldBe(
            "(0, 0) (1, 1) (1, 2) (2, 3) (3, 4) (3, 5) (4, 5) (5, 5) (6, 6)",
            "拉直前逐字：贴着墙根绕过那个 L 形拐角的格子阶梯");
        path.DescribeCorners().ShouldBe(
            "(0.5, 0.5) -> (3.5, 5.5) -> (6.5, 6.5)",
            "拉直后逐字：两个拐点（墙根那个拐角与出墙处），不是格子阶梯");

        断言拐点处朝向连续(path, "拐点处朝向必须连续：零长段、反向、同向冗余点三条都判红");
        像格子阶梯(path, 1.0).ShouldBeFalse("折线里至少有一段不是一格长 —— 阶梯的指纹不成立");

        // 独立于被测实现的一段暴力采样：折线每一段逐点扫过去，扫到的格都得在可行域里。
        var grid = new NavGrid(Spec(L形墙()));
        断言每段都走在可行域上(grid, path, 0.0, "拉直不许穿墙：折线是「看着能用」的另一种骗人方式");
    }

    [Fact]
    public void 不许切角_正交邻格封一个就判否()
    {
        // (2,1) 封住，其余全通。从 (1,1) 到 (2,2) 的斜穿要求 (2,1) 与 (1,2) 都通。
        var blocked = new List<NavCell> { new(2, 1) };
        var grid = new NavGrid(Spec(blocked, 5, 5));

        NavPathStraightener.SegmentWalkable(grid, new NavPoint(1.5, 1.5), new NavPoint(2.5, 2.5), 0.0)
            .ShouldBeFalse("这条斜线正好穿 (2,1)(1,2)(1,1)(2,2) 那个格角，而 (2,1) 不通 —— 不许切角");

        NavPathStraightener.SegmentWalkable(grid, new NavPoint(1.5, 1.5), new NavPoint(1.5, 2.5), 0.0)
            .ShouldBeTrue("正交的一步是通的；否掉斜线不等于把路也否掉");

        var solver = Solver(Spec(blocked, 5, 5));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(1.5, 1.5), new NavPoint(2.5, 2.5), 0.0);

        格路(path.CellPath).ShouldBe(
            "(1, 1) (1, 2) (2, 2)",
            "A* 也不许切角：(1,1)→(2,2) 的斜走被否掉，只能先下再右");
        path.DescribeCorners().ShouldBe(
            "(1.5, 1.5) -> (1.5, 2.5) -> (2.5, 2.5)",
            "拉直也只能这么绕：折线三段折成一个直角");
        断言拐点处朝向连续(path, "那个直角是真拐点：入向 (0,+1)、出向 (+1,0)");
    }

    [Fact]
    public void 腐蚀后的可行域_净空不够的格不进路()
    {
        // 9×9 空场，正中立一格阻挡 (4,4)，请求半径 1.5。
        // 净空 ≥ 1.5 才算可行：(4,4) 周围那一圈（含四个对角的 √2 = 1.414）全被腐蚀掉。
        var blocked = new List<NavCell> { new(4, 4) };
        var solver = Solver(Spec(blocked, 9, 9));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(2.5, 2.5), new NavPoint(6.5, 6.5), 1.5);

        path.Status.ShouldBe(NavPathStatus.Reached);
        path.CellPath[0].ShouldBe(new NavCell(2, 2));
        path.CellPath[^1].ShouldBe(new NavCell(6, 6));

        var eroded = new[]
        {
            new NavCell(3, 3), new NavCell(3, 4), new NavCell(3, 5),
            new NavCell(4, 3), new NavCell(4, 4), new NavCell(4, 5),
            new NavCell(5, 3), new NavCell(5, 4), new NavCell(5, 5),
        };
        for (var i = 0; i < path.CellPath.Count; i++)
        {
            eroded.ShouldNotContain(
                path.CellPath[i],
                $"第 {i} 格 {path.CellPath[i]} 净空不到 1.5 米 —— 可行域是腐蚀过的，腐蚀掉的格不许进路");
        }

        // 宽度进键：腐蚀量一变，路就变；半径小的可行域是半径大的超集，所以总成本不会更大。
        var wide = Solver(Spec(blocked, 9, 9));
        wide.BeginStep(20000);
        var near = wide.Solve(new NavPoint(2.5, 2.5), new NavPoint(6.5, 6.5), 1.0);
        near.Status.ShouldBe(NavPathStatus.Reached);
        near.TotalCost.ShouldBeLessThanOrEqualTo(
            path.TotalCost,
            "可行域随半径单调变大：半径 1.0 那一趟的最优成本不可能更大");
    }

    [Fact]
    public void 一格宽走廊能走通_折线就是走廊中线()
    {
        var solver = Solver(Spec(走廊()));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(1.5, 1.5), new NavPoint(6.5, 1.5), 1.0);

        path.Status.ShouldBe(NavPathStatus.Reached, "走廊那一行的净空恰好 1.0 米，半径 1.0 站得下");
        格路(path.CellPath).ShouldBe("(1, 1) (2, 1) (3, 1) (4, 1) (5, 1) (6, 1)", "六个格子");
        path.DescribeCorners().ShouldBe("(1.5, 1.5) -> (6.5, 1.5)", "直线穿过走廊，不需要拐点");
        path.ExpandedCells.ShouldBe(6);
    }

    // ─────────────────────── 逐字 / 确定性 ───────────────────────

    [Fact]
    public void 同一请求两次求解逐字相等()
    {
        // 两个各自带空缓存的求解器 = 两次真算（不是第二次读缓存）。
        // 随机阻挡用一条写死的 LCG 而不是 Random：同种子两跑要逐字全等，
        // 而 Random 的序列跟着运行时版本走。
        var blocked = new List<NavCell>();
        var seen = new HashSet<NavCell>();
        var seed = 20261003u;
        for (var i = 0; i < 24; i++)
        {
            seed = (seed * 1664525u) + 1013904223u;
            var bx = (int)(seed % 12u);
            seed = (seed * 1664525u) + 1013904223u;
            var bz = (int)(seed % 12u);
            if (bx >= 2 && bx <= 9 && bz >= 2 && bz <= 9 && seen.Add(new NavCell(bx, bz)))
            {
                blocked.Add(new NavCell(bx, bz));
            }
        }

        var a = Solver(Spec(blocked, 12, 12));
        var b = Solver(Spec(blocked, 12, 12));
        a.BeginStep(20000);
        b.BeginStep(20000);

        var first = a.Solve(new NavPoint(0.5, 0.5), new NavPoint(11.5, 11.5), 0.4);
        var second = b.Solve(new NavPoint(0.5, 0.5), new NavPoint(11.5, 11.5), 0.4);

        格路(second.CellPath).ShouldBe(格路(first.CellPath), "同一请求两次求解，格子路径逐字相等");
        second.DescribeCorners().ShouldBe(first.DescribeCorners(), "拉直后的折线也逐字相等");
        second.ExpandedCells.ShouldBe(first.ExpandedCells, "扩展格数是重放对得上的一部分");
        second.TotalCost.ShouldBe(first.TotalCost, "总成本逐字相等（算术一律 double）");

        // 账本那一行也逐字相等：两边的序号都是 #1，其余读数全是同一份算出来的。
        a.Records.Last!.Describe().ShouldBe(b.Records.Last!.Describe(), "求解记录逐字相等");
    }

    [Fact]
    public void 缓存命中与未命中在求解记录里数得出来()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>()));
        solver.BeginStep(20000);

        var first = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);
        var miss = solver.Records.Last!;
        miss.Sequence.ShouldBe(1L);
        miss.CacheHit.ShouldBeFalse("第一次是未命中，真算了一遍");
        miss.ExpandedCells.ShouldBe(8);
        solver.Cache.Misses.ShouldBe(1);
        solver.Cache.Hits.ShouldBe(0);

        var second = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);
        var hit = solver.Records.Last!;
        hit.Sequence.ShouldBe(2L, "序号跨请求连续");
        hit.CacheHit.ShouldBeTrue("同一 (空间, 目标, 宽度, 两版本, 起点) 第二次命中");
        hit.ExpandedCells.ShouldBe(0, "命中不许复算：第二次的扩展格数必须是 0，这是账本里数得出来的那一条");
        hit.Describe().ShouldContain("缓存 命中", Case.Sensitive, "求解记录那一行自己说命中了，不必回头数缓存");
        second.DescribeCorners().ShouldBe(first.DescribeCorners(), "命中交回的就是同一条路");
        solver.Cache.Hits.ShouldBe(1);

        // 宽度进键：半径一变就不是同一条路了。
        var wider = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.5);
        solver.Records.Last!.CacheHit.ShouldBeFalse("宽度是缓存键的一列，半径从 0 变成 0.5 就是另一条路");
        wider.Corners.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 动态代价层变一次就作废缓存_四条触发里没有它()
    {
        // 钉住一个已知的读数形状：动态层变 → 缓存条目作废（键里带着动态版本），
        // 而 spec 点名的四条触发里没有「动态版本变了」这一条，所以那一行是「未命中 + 触发 None」。
        // 这不是漏，是范围：记在 docs/GugaFramework/寻路层.md 2 节的待确认项里。
        var solver = Solver(Spec(Array.Empty<NavCell>()));
        solver.BeginStep(20000);
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);
        solver.Records.Last!.DynamicVersion.ShouldBe(0, "改之前动态版本是 0");

        solver.Space.Grid.SetDynamicCost(new NavCell(3, 3), 2.0);
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);

        var after = solver.Records.Last!;
        after.CacheHit.ShouldBeFalse("动态版本 +1，缓存键就变了");
        after.DynamicVersion.ShouldBe(1);
        after.RecomputeTriggers.ShouldBe(NavRecomputeTriggers.None, "四条触发都不含动态版本变化");
    }

    // ─────────────────────────── 预算 ───────────────────────────

    [Fact]
    public void 预算触顶回部分路径_带原因与求解记录那一行()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>(), 16, 16), budget: 5);
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(15.5, 15.5), 0.0);

        path.Status.ShouldBe(NavPathStatus.Partial);
        path.Reason.ShouldBe(NavPathReason.BudgetExceeded, "触顶回部分路径而不是抛、也不是空");
        path.IsBudgetTruncated.ShouldBeTrue();
        path.ExpandedCells.ShouldBe(5, "扩到上限就停：一个格都不多扩");
        path.PartialAt.ShouldBe(new NavPoint(4.5, 4.5), "走到 (4,4) 就停了");
        path.DescribeCorners().ShouldBe(
            "(0.5, 0.5) -> (4.5, 4.5)",
            "折线交回的是走到的那一格，不是请求的目标点 —— 交回目标点就是宣称走通了没走通的那条路");
        path.Corners.Count.ShouldBeGreaterThan(0, "不返空：折线至少有一个点");

        var record = solver.Records.Last!;
        record.Status.ShouldBe(NavPathStatus.Partial);
        record.Reason.ShouldBe(NavPathReason.BudgetExceeded);
        record.IsPartial.ShouldBeTrue();
        record.ExpandedCells.ShouldBe(5);
        record.BudgetPerRequest.ShouldBe(5, "用了哪一档预算：每请求上限 5");
        record.BudgetStepRemainingAfter.ShouldBe(19995, "本步全局 20000 减掉这一次花的 5");
        record.PartialAt.ShouldBe(new NavPoint(4.5, 4.5));
        record.Describe().ShouldContain("Partial/BudgetExceeded", Case.Sensitive, "账本那一行自己写着触顶了");
        record.Describe().ShouldContain("预算 每请求 5", Case.Sensitive, "档位也在那一行里");

        solver.Cache.Hits.ShouldBe(0);
        solver.Cache.Count.ShouldBe(0, "部分路径不进缓存：缓存一份「没走到」等于把一次触顶固化成常驻答案");
    }

    [Fact]
    public void 一步的全局预算用尽后交回起点那一个点()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>(), 16, 16));
        solver.BeginStep(6);

        var first = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(15.5, 15.5), 0.0);
        first.ExpandedCells.ShouldBe(6, "本步全局上限 6，第一个请求就吃光");
        first.Reason.ShouldBe(NavPathReason.BudgetExceeded);
        solver.StepExpansionsRemaining.ShouldBe(0);

        var second = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(15.5, 7.5), 0.0);
        second.Status.ShouldBe(NavPathStatus.Partial);
        second.Reason.ShouldBe(NavPathReason.BudgetExceeded);
        second.ExpandedCells.ShouldBe(0, "一个格都没扩：两档预算取小的那个");
        second.CornerCount.ShouldBe(1, "交回起点那一个点。不交回空，也不交回「起点直指目标点」那条假直线");
        second.PartialAt.ShouldBe(new NavPoint(0.5, 0.5), "一个格都没走到，可达终点就是出发点");
        solver.Records.Last!.BudgetStepRemainingAfter.ShouldBe(0, "那一行说得出是本步预算顶的，不是每请求档顶的");
    }

    // ─────────────────────── 四条重算触发 ───────────────────────

    [Fact]
    public void 重算触发一_静态版本变()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>()));
        solver.BeginStep(20000);
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);
        solver.Records.Last!.RecomputeTriggers.ShouldBe(NavRecomputeTriggers.None, "第一次求解没有上一次可比");

        solver.Space.Grid.SetBlocked(new NavCell(7, 0), true);
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);

        var record = solver.Records.Last!;
        record.RecomputeTriggers.HasFlag(NavRecomputeTriggers.StaticVersionChanged).ShouldBeTrue("封一格地形 = 静态版本 +1");
        record.RecomputeTriggers.HasFlag(NavRecomputeTriggers.GoalMovedBeyondThreshold).ShouldBeFalse("目标没动，那一条不许跟着亮");
        record.StaticVersion.ShouldBe(1);
        record.CacheHit.ShouldBeFalse("版本进缓存键，静态版本一变旧条目就再也取不到");
        record.RecomputeCount.ShouldBe(2, "同一请求身份第 2 次 —— 「它重算了 47 次」是数得出来的");
    }

    [Fact]
    public void 重算触发二_目标移动超过阈值()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>()));
        solver.BeginStep(20000);
        var goal = new NavPoint(7.5, 7.5);
        solver.Solve(new NavPoint(0.5, 0.5), goal, 0.0);

        // 阈值内：动了但没过线，不记这一条。
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.0), 0.0);
        solver.Records.Last!.RecomputeTriggers.HasFlag(NavRecomputeTriggers.GoalMovedBeyondThreshold)
            .ShouldBeFalse("位移 0.5 米恰好等于阈值，线上不记是钉死的口径");

        // 过线：记这一条。比的是<strong>上一次</strong>的目标，所以这一次是相对 (7.5, 7.0) 动 0.6 米。
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 6.4), 0.0);
        var record = solver.Records.Last!;
        record.RecomputeTriggers.HasFlag(NavRecomputeTriggers.GoalMovedBeyondThreshold)
            .ShouldBeTrue("相对上一次的目标位移 0.6 米，过了阈值 0.5");
        record.RecomputeCount.ShouldBe(3, "身份是（空间, 起点, 宽度）—— 目标不进身份，否则这条触发永远判不出来");
    }

    [Fact]
    public void 重算触发三_上一次预算触顶()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>(), 16, 16), budget: 5);
        solver.BeginStep(20000);
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(15.5, 15.5), 0.0);
        solver.Records.Last!.RecomputeTriggers.ShouldBe(NavRecomputeTriggers.None, "第一次没有上一次");

        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(15.5, 15.5), 0.0);
        var record = solver.Records.Last!;
        record.RecomputeTriggers.HasFlag(NavRecomputeTriggers.BudgetExceeded)
            .ShouldBeTrue("上一次是预算触顶才只回部分路径");
        record.Status.ShouldBe(NavPathStatus.Partial);
        record.ExpandedCells.ShouldBe(5, "部分路径不进缓存，所以这一次是真算的 5 次扩展");
        record.CacheHit.ShouldBeFalse();
    }

    [Fact]
    public void 重算触发四_走了N步没到()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>()));
        solver.BeginStep(20000);
        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);

        for (var i = 0; i < NotArrivedStepCap; i++)
        {
            solver.BeginStep(20000);
        }

        solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);
        var record = solver.Records.Last!;
        record.RecomputeTriggers.HasFlag(NavRecomputeTriggers.NotArrivedWithinSteps)
            .ShouldBeTrue($"距上次求解过去 {NotArrivedStepCap} 步，N = {NotArrivedStepCap}（工单 04 的移动解还没接，「没到」只能按到期判）");
        record.CacheHit.ShouldBeTrue("条件一个都没变 —— 到期重算是缓存命中，0 次扩展");
        record.ExpandedCells.ShouldBe(0, "所以「每 N 步重算一次」这件事本身不花钱，这一点是读数不是承诺");
    }

    [Fact]
    public void 重算判据本身是个纯函数()
    {
        var baseProbe = new NavRecomputeProbe(
            LastStaticVersion: 3,
            StaticVersion: 3,
            LastGoal: new NavPoint(4.0, 4.0),
            Goal: new NavPoint(4.0, 4.0),
            GoalToleranceMeters: 0.5,
            PreviousBudgetExceeded: false,
            StepsSinceLastSolve: 0,
            StepCap: 30);

        NavRecomputeJudge.Judge(baseProbe).ShouldBe(NavRecomputeTriggers.None, "四样都没动：没有触发");

        NavRecomputeJudge.Judge(baseProbe with { StaticVersion = 4 })
            .ShouldBe(NavRecomputeTriggers.StaticVersionChanged);
        NavRecomputeJudge.Judge(baseProbe with { Goal = new NavPoint(4.0, 4.4) })
            .ShouldBe(NavRecomputeTriggers.None, "位移 0.4 米没过线");
        NavRecomputeJudge.Judge(baseProbe with { Goal = new NavPoint(4.0, 4.6) })
            .ShouldBe(NavRecomputeTriggers.GoalMovedBeyondThreshold, "位移 0.6 米过了线");
        NavRecomputeJudge.Judge(baseProbe with { PreviousBudgetExceeded = true })
            .ShouldBe(NavRecomputeTriggers.BudgetExceeded);
        NavRecomputeJudge.Judge(baseProbe with { StepsSinceLastSolve = 30 })
            .ShouldBe(NavRecomputeTriggers.NotArrivedWithinSteps, "到 N 就记");
        NavRecomputeJudge.Judge(baseProbe with { StepsSinceLastSolve = 29 })
            .ShouldBe(NavRecomputeTriggers.None, "没到 N 不记");

        // 四条同时踩中也全留住：单值判据会把三条丢掉，丢掉的正是事后说不清的那条。
        NavRecomputeJudge.Judge(baseProbe with
        {
            StaticVersion = 4,
            Goal = new NavPoint(9.0, 9.0),
            PreviousBudgetExceeded = true,
            StepsSinceLastSolve = 30,
        }).ShouldBe(
            NavRecomputeTriggers.StaticVersionChanged
            | NavRecomputeTriggers.GoalMovedBeyondThreshold
            | NavRecomputeTriggers.BudgetExceeded
            | NavRecomputeTriggers.NotArrivedWithinSteps,
            "四个位都在");

        Should.Throw<ArgumentOutOfRangeException>(() => NavRecomputeJudge.Judge(baseProbe with { GoalToleranceMeters = -0.1 }))
            .Message.ShouldContain("阈值", Case.Sensitive, "报出来要指到是哪一列参数不对");
        Should.Throw<ArgumentOutOfRangeException>(() => NavRecomputeJudge.Judge(baseProbe with { StepCap = 0 }))
            .Message.ShouldContain("步数上限", Case.Sensitive, "N = 0 是每步全量重解，不是重算触发");
    }

    // ─────────────────────────── 负向 ───────────────────────────

    [Fact]
    public void 负向_起点到不了目标()
    {
        var blocked = new List<NavCell>();
        for (var z = 0; z < 8; z++)
        {
            blocked.Add(new NavCell(4, z));
        }

        var solver = Solver(Spec(blocked));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 0.5), 0.0);

        path.Status.ShouldBe(NavPathStatus.Partial, "部分路径是一等结果，不是失败（GLOSSARY.md 11 节）");
        path.Reason.ShouldBe(NavPathReason.GoalUnreachable, "地形断开了，不是预算顶的");
        path.IsBudgetTruncated.ShouldBeFalse();
        path.ExpandedCells.ShouldBe(32, "墙左边 4 列 × 8 行全扩过");
        path.PartialAt.ShouldBe(new NavPoint(3.5, 0.5), "走到离目标最近的那一格");
        path.DescribeCorners().ShouldBe(
            "(0.5, 0.5) -> (3.5, 0.5)",
            "折线交回到最远处那一格，不交回空、也不交回目标点");
        solver.Records.Last!.Reason.ShouldBe(NavPathReason.GoalUnreachable, "账本那一行也记着原因");
    }

    [Fact]
    public void 负向_目标落在阻挡里()
    {
        var solver = Solver(Spec(new List<NavCell> { new(3, 3) }));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(3.5, 3.5), 0.0);

        path.Status.ShouldBe(NavPathStatus.Failed, "不该启动这个命令：目标那一格净空 0");
        path.Reason.ShouldBe(NavPathReason.GoalBlocked);
        path.ExpandedCells.ShouldBe(0, "两端判不过就不进 A*，一个格都不扩");
        path.CornerCount.ShouldBe(1, "不返空：交回起点那一个点");
        path.PartialAt.ShouldBeNull("压根没起步，没有「走到哪儿」这一说");
        solver.Records.Last!.CacheHit.ShouldBeFalse("失败也不进缓存");
        solver.Cache.Count.ShouldBe(0);
    }

    [Fact]
    public void 负向_起点半径放不下()
    {
        // 起点在 (0,0)：它右边那格 (1,0) 封着，起点格净空 1.0 米（到 (1,0) 中心 / 到界外虚拟阻挡中心都是 1.0）。
        // 半径 1.5 放不下；半径 1.0 刚好放得下 —— 判据是 ≥ 不是 >。
        var blocked = new List<NavCell> { new(1, 0) };
        var grid = new NavGrid(Spec(blocked));
        grid.Clearance.At(0, 0).ShouldBe(1.0, "起点格净空就是 1.0 米");

        var solver = Solver(Spec(blocked));
        solver.BeginStep(20000);
        var path = solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 1.5);

        path.Status.ShouldBe(NavPathStatus.Failed);
        path.Reason.ShouldBe(NavPathReason.StartBlocked, "半径比起点那一格的净空还大");
        path.ExpandedCells.ShouldBe(0);
        path.Corners.Count.ShouldBe(1);
        solver.Records.Last!.Reason.ShouldBe(NavPathReason.StartBlocked, "账本那一行也记着原因");

        var fits = Solver(Spec(blocked));
        fits.BeginStep(20000);
        fits.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 1.0)
            .Status.ShouldBe(NavPathStatus.Reached, "净空 1.0 米放得下半径 1.0");
    }

    [Fact]
    public void 入参不成形状当场抛()
    {
        var space = new NavSpace(Deck, new NavGrid(Spec(Array.Empty<NavCell>())));
        var budget = new NavSolveBudget { PerRequestExpansions = 10 };
        var tuning = new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 };
        var solver = new NavSolver(space, budget, tuning, 8, 8, 8);
        solver.BeginStep(100);

        Should.Throw<ArgumentOutOfRangeException>(() => solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), -0.1))
            .Message.ShouldContain("半径", Case.Sensitive, "半径是请求递进来的标量，负数当场抛不兜 0");
        Should.Throw<ArgumentOutOfRangeException>(() => solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), double.NaN));
        Should.Throw<ArgumentOutOfRangeException>(() => solver.BeginStep(0))
            .Message.ShouldContain("扩展上限", Case.Sensitive, "0 扩展让每个请求都触顶，「没走到」就再也读不出是地形还是预算");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavSolver(
            space,
            new NavSolveBudget { PerRequestExpansions = 0 },
            tuning,
            8,
            8,
            8));
        Should.Throw<ArgumentOutOfRangeException>(() => new NavSolver(
            space,
            budget,
            new NavRecomputeTuning { GoalMoveToleranceMeters = double.NaN, NotArrivedStepCap = 30 },
            8,
            8,
            8)).Message.ShouldContain("阈值", Case.Sensitive, "阈值由宿主递，层里没有缺省");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavSolver(space, budget, tuning, 0, 8, 8))
            .Message.ShouldContain("容量", Case.Sensitive, "容量 0 的账本记不下任何一行");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavSolver(space, budget, tuning, 8, 8, 0));
    }

    [Fact]
    public void 两个空间各解各的()
    {
        var a = Solver(Spec(Array.Empty<NavCell>(), 8, 8, 1.0, Deck));
        var b = Solver(Spec(Array.Empty<NavCell>(), 8, 8, 1.0, Arena));
        a.BeginStep(20000);
        b.BeginStep(20000);

        a.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);
        b.Solve(new NavPoint(0.5, 0.5), new NavPoint(7.5, 7.5), 0.0);

        a.Records.Last!.Space.ShouldBe(Deck);
        b.Records.Last!.Space.ShouldBe(Arena);
        a.Cache.Misses.ShouldBe(1, "同坐标不同空间不是同一条路，缓存不许互相喂");
        b.Cache.Misses.ShouldBe(1);
        a.Records.Count.ShouldBe(1, "各自的账本各自记");
        b.Records.Count.ShouldBe(1);
    }

    [Fact]
    public void 求解记录账本是有界的()
    {
        var solver = Solver(Spec(Array.Empty<NavCell>()), 4000);
        for (var step = 0; step < 6; step++)
        {
            solver.BeginStep(20000);
            solver.Solve(new NavPoint(0.5, 0.5), new NavPoint(1.0 + step, 7.5), 0.0);
        }

        solver.Records.Capacity.ShouldBe(64);
        solver.Records.Count.ShouldBe(6, "6 条都在 64 的容量之内");
        solver.Records.Dropped.ShouldBe(0);
        solver.Records.Last!.Sequence.ShouldBe(6L, "序号跨步连续 —— 「第几步之后开始不对」是一条读数");
        solver.Records.At(0).Sequence.ShouldBe(1L, "按写入次序寻址，不是按哈希桶");
        Should.Throw<ArgumentOutOfRangeException>(() => solver.Records.At(6))
            .Message.ShouldContain("越界", Case.Sensitive, "越界是调用方的错，不是一个该被兜住的读数");
    }

    // ─────────────────────────── 断言用的小工具 ───────────────────────────

    /// <summary>
    /// 拐点处朝向连续 —— 折线与格子阶梯的判别式。三条都判红：
    /// 零长段（原地打转）、朝向反转（那是折返）、同向冗余点（那该被拉直掉）。
    /// </summary>
    private static void 断言拐点处朝向连续(NavPath path, string why)
    {
        var corners = path.Corners;
        corners.Count.ShouldBeGreaterThan(0, why);

        for (var i = 0; i + 1 < corners.Count; i++)
        {
            var length = corners[i].DistanceTo(corners[i + 1]);
            length.ShouldBeGreaterThan(0.0, $"{why}：第 {i} 段是零长段");
        }

        for (var i = 1; i + 1 < corners.Count; i++)
        {
            var (ix, iz) = (corners[i].X - corners[i - 1].X, corners[i].Z - corners[i - 1].Z);
            var (ox, oz) = (corners[i + 1].X - corners[i].X, corners[i + 1].Z - corners[i].Z);
            var il = Math.Sqrt((ix * ix) + (iz * iz));
            var ol = Math.Sqrt((ox * ox) + (oz * oz));
            var dot = ((ix * ox) + (iz * oz)) / (il * ol);
            var cross = ((ix * oz) - (iz * ox)) / (il * ol);

            dot.ShouldBeGreaterThan(-1.0 + 1e-9, $"{why}：拐点 {i} 处朝向反转（点积 {dot}），那是折返不是拐弯");
            Math.Abs(cross).ShouldBeGreaterThan(1e-12, $"{why}：拐点 {i} 处两段同向，那不是拐点，是个该被拉直掉的冗余点");
        }
    }

    /// <summary>格子阶梯的指纹：每一段都恰好一格长。</summary>
    private static bool 像格子阶梯(NavPath path, double cellMeters)
    {
        for (var i = 0; i + 1 < path.Corners.Count; i++)
        {
            if (Math.Abs(path.Corners[i].DistanceTo(path.Corners[i + 1]) - cellMeters) > 1e-9)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 独立于被测实现的暴力检查：折线每一段逐点扫过去，扫到的格都得在可行域里。
    /// 采样步长远小于一格，1 米宽的墙不可能被采样跳过。
    /// </summary>
    private static void 断言每段都走在可行域上(NavGrid grid, NavPath path, double radius, string why)
    {
        const double Step = 0.02;
        for (var i = 0; i + 1 < path.Corners.Count; i++)
        {
            var a = path.Corners[i];
            var b = path.Corners[i + 1];
            var length = a.DistanceTo(b);
            var steps = Math.Max(1, (int)Math.Ceiling(length / Step));
            for (var s = 0; s <= steps; s++)
            {
                var t = (double)s / steps;
                var p = new NavPoint(a.X + ((b.X - a.X) * t), a.Z + ((b.Z - a.Z) * t));
                grid.Clearance.TryLocal(p, out var cx, out var cz, out _, out _).ShouldBeTrue(
                    $"{why}：第 {i} 段上 t = {t.ToString(System.Globalization.CultureInfo.InvariantCulture)} 那个点 {p} 落在网格外");
                grid.Clearance.At(cx, cz).ShouldBeGreaterThanOrEqualTo(
                    radius - 1e-12,
                    $"{why}：第 {i} 段扫到了 ({cx}, {cz})，那一格净空 {grid.Clearance.At(cx, cz).ToString(System.Globalization.CultureInfo.InvariantCulture)} < 半径 {radius}");
            }
        }
    }
}
