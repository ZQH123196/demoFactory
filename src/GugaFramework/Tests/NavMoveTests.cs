#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 移动解（工单 04）：五个命令归一到同一条流水线、前视式跟随、每步内切子步、检查点快照。
///
/// <para><b>断言强度</b>照「断言比快照」 —— 只比状态级读数（位置、朝向、速度、状态、卡住类别、
/// 卡住步数与那一段快照文本）。<b>前视式没有「坐标等于某航点」那种断言</b>：
/// 站位冲突必然在推单位，一被推就丢轨，拿航点当期望值钉的是一件不存在的事
/// （spec「前视式跟随，不是轨道式」）。这一族断言的形状是
/// 「每步位移不超过上限 + N 步内到达 + 全程未进阻挡格」。</para>
///
/// <para><b>「全程未进阻挡格」由用例里一份独立的查表判</b>（照第一、二期
/// 「真值由用例里一份独立的暴力计算给出」的纪律）：查的是自己那份阻挡表，
/// 不复用被测实现那条读法。</para>
///
/// <para>逐字读数都是先跑探针读出来的，再写成断言 —— 上一期这么做从探针里查出两个真 bug。</para>
/// </summary>
public class NavMoveTests
{
    private static readonly SpaceId Deck = new("巨兽甲板");

    private const double StepSeconds = 0.1;   // 「时间基准」：一律 0.1 秒
    private const double Cap = 0.000001;     // 只给本来就带浮点噪声的读数用

    private static NavGridSpec Spec(IReadOnlyList<NavCell> blocked, int x = 8, int z = 8, double cell = 1.0)
        => new()
        {
            Space = Deck,
            CellMeters = cell,
            CellsX = x,
            CellsZ = z,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = blocked,
        };

    private static NavSolver SolverOf(NavGridSpec spec, int budget = 4000)
        => new(
            new NavSpace(spec.Space, new NavGrid(spec)),
            new NavSolveBudget { PerRequestExpansions = budget },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);

    private static NavStepper StepperOf(
        NavSolver solver,
        GameClock clock,
        double lookAheadMeters,
        NavMoveCheck? gate = null,
        NavSeparationTuning? separation = null)
        => new(
            solver,
            new NavMoveTuning
            {
                StepSeconds = StepSeconds,
                LookAheadMeters = lookAheadMeters,
                StepSolveExpansions = 20000,
            },
            clock,
            gate,
            separation);

    private static NavAgentSpec Agent(
        string id,
        NavPoint at,
        double speed,
        double radius = 0.0,
        double facing = 0.0)
        => new()
        {
            Id = id,
            RadiusMeters = radius,
            MoveSpeedMetersPerSecond = speed,
            Start = at,
            FacingDegrees = facing,
        };

    /// <summary>独立暴力判：点是不是落在一格地形阻挡里（不查被测实现那份读法，只查自己这份阻挡表）。</summary>
    private static bool InBlocked(IReadOnlyList<NavCell> blocked, NavPoint p)
    {
        var cell = new NavCell((int)Math.Floor(p.X), (int)Math.Floor(p.Z));
        for (var i = 0; i < blocked.Count; i++)
        {
            if (blocked[i] == cell)
            {
                return true;
            }
        }

        return false;
    }

    // ─────────────────── 五个命令：同一条流水线，到达容差统一处理 ───────────────────

    [Fact]
    public void 五个命令都在容差内_同一步都读到位()
    {
        // 五个单位都已经站在各自目标点的容差圈内。走到这一步为止它们走过的是同一条流水线，
        // 所以五个必须在**同一步**读出「到位」，且位移都是 0 —— 这一条就是「到达容差统一处理」的形状。
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        var starts = new[] { 0.5, 1.5, 2.5, 3.5, 4.5 };
        for (var i = 0; i < 5; i++)
        {
            stepper.Register(Agent("单位" + i, new NavPoint(0.5, starts[i]), 1.5));
        }

        stepper.Issue("单位0", NavCommand.Hold(new NavPoint(0.5, 0.5)));
        stepper.Issue("单位1", NavCommand.GoTo(new NavPoint(0.2, 1.5), 0.4));                 // 差 0.3 米，在 0.4 容差内
        stepper.Issue("单位2", NavCommand.Chase("敌", new NavPoint(0.1, 2.5), 0.4));          // 差 0.4 米，容差线上算到
        stepper.Issue("单位3", NavCommand.Along(new[] { new NavPoint(0.2, 3.5) }, 0.4));
        stepper.Issue("单位4", NavCommand.Guard(new NavPoint(0.2, 4.5), 2.0, 0.4));

        clock.Advance(1);

        stepper.SolutionOf("单位0").Status.ShouldBe(NavMoveStatus.Holding, "原地不动那一档到位的读法是 Holding");
        for (var i = 1; i < 5; i++)
        {
            stepper.SolutionOf("单位" + i).Status.ShouldBe(NavMoveStatus.Arrived,
                "四个有目标点的命令在容差内读的是同一个到位");
        }

        for (var i = 0; i < 5; i++)
        {
            var s = stepper.SolutionOf("单位" + i);
            s.MovedMeters.ShouldBe(0.0, Cap, "到位的那一步不产位移");
            s.Position.ShouldBe(new NavPoint(0.5, starts[i]), "位置不动");
            s.SpeedMetersPerSecond.ShouldBe(0.0);
        }
    }

    [Fact]
    public void 五个命令_同一步读出同一种形状()
    {
        // 同一批单位、同一步、离各自目标都还远：五档命令在流水线上是同一种形状 ——
        // 都只剩「朝目标走一步」，差别只在目标描述那一列。
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        for (var i = 0; i < 5; i++)
        {
            stepper.Register(Agent("单位" + i, new NavPoint(0.5, i + 0.5), 1.5));
        }

        stepper.Issue("单位0", NavCommand.Hold(new NavPoint(7.5, 0.5)));      // 驻守点远在另一头
        stepper.Issue("单位1", NavCommand.GoTo(new NavPoint(7.5, 1.5), 0.25));
        stepper.Issue("单位2", NavCommand.Chase("敌", new NavPoint(7.5, 3.0), 0.25));
        stepper.Issue("单位3", NavCommand.Along(new[] { new NavPoint(2.5, 4.5), new NavPoint(6.5, 4.5) }, 0.25));
        stepper.Issue("单位4", NavCommand.Guard(new NavPoint(6.5, 6.0), 2.0, 0.25));

        clock.Advance(1);

        // 一条真实读数样例：五个单位的每一步解读出来是同一件事 —— 位置、朝向、速度、状态、卡住、卡住步数。
        stepper.SolutionOf("单位1").Describe().ShouldBe(
            "单位1 GoTo 位置 (0.65, 1.5) 朝向 90 速度 1.5000000000000002 位移 0.15000000000000002 "
            + "状态 Moving 卡住 None/0 子步 1 目标 (7.5, 1.5) 前视 (2.5, 1.5) 激活检查 无",
            "移动解的六列读数逐字：位置、朝向、速度、状态、卡住类别/卡住步数");

        foreach (var (id, target) in new[]
        {
            ("单位0", new NavPoint(7.5, 0.5)),
            ("单位1", new NavPoint(7.5, 1.5)),
            ("单位2", new NavPoint(7.5, 3.0)),
            ("单位3", new NavPoint(2.5, 4.5)),
            ("单位4", new NavPoint(6.5, 6.0)),
        })
        {
            var s = stepper.SolutionOf(id);
            s.Status.ShouldBe(NavMoveStatus.Moving, "离目标都还远，五档在这一点上形状一样");
            s.Stuck.ShouldBe(NavStuckKind.None);
            s.StuckSteps.ShouldBe(0);
            s.Substeps.ShouldBe(1, "一步 0.15 米 < 子步上限 0.5 米：一个子步走完");
            s.Goal.ShouldBe(target, "目标点就是命令说的那个点（追实体是目标这一轮的坐标）");
            s.MovedMeters.ShouldBe(0.15, 0.0001);
            InBlocked(Array.Empty<NavCell>(), s.Position).ShouldBeFalse();
        }
    }

    [Fact]
    public void 沿固定点走_到位推游标()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.Along(
            new[] { new NavPoint(2.5, 0.5), new NavPoint(5.5, 0.5) }, 0.25));

        // 走到第一个航点：游标还没推，目标仍是第一个航点。
        while (stepper.SolutionOf("甲").Status != NavMoveStatus.Arrived)
        {
            clock.Advance(1);
        }

        var first = stepper.SolutionOf("甲");
        first.Status.ShouldBe(NavMoveStatus.Arrived, "第一个航点到位");
        first.Position.DistanceTo(new NavPoint(2.5, 0.5)).ShouldBeLessThanOrEqualTo(0.25,
            "到达容差统一判在那一步的开头，所以落点在容差圈内而不是逐字压在航点上 —— 同一处处理，五档一样");

        // 再推一步：游标进了第二个航点，目标那一列换人 —— 到位与推游标是同一处处理。
        clock.Advance(1);
        var second = stepper.SolutionOf("甲");
        second.Status.ShouldBe(NavMoveStatus.Moving, "游标推进之后继续走，不是停在上一个航点");
        second.Goal.ShouldBe(new NavPoint(5.5, 0.5), "游标已经推到第二个航点");

        for (var i = 0; i < 30; i++)
        {
            clock.Advance(1);
            if (stepper.SolutionOf("甲").Status == NavMoveStatus.Arrived)
            {
                break;
            }
        }

        var last = stepper.SolutionOf("甲");
        last.Status.ShouldBe(NavMoveStatus.Arrived);
        last.Goal.ShouldBe(new NavPoint(5.5, 0.5));
        last.Position.DistanceTo(new NavPoint(5.5, 0.5)).ShouldBeLessThanOrEqualTo(0.25, "整串航点走完停在最后一个");
    }

    // ─────────────────── 前视式：方向取自前视点，步长不是 ───────────────────

    [Fact]
    public void 前视点不是停靠点_前视距离不足一步仍走满一步()
    {
        // 全可走长直路，16×8：起点 (0.5,0.5) → (15.5,0.5)，直线 15 米。
        // 前视距离 0.5 米 < 一步 1.5 米：若拿前视点当停靠点，这一步只会挪 0.5 米。
        // 方向取自前视点，位移按「本步能走的距离」与「沿路径剩余」的较小者 = 1.5 米。
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>(), 16, 8)), clock, 0.5);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 15.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(15.5, 0.5), 0.25));

        var perStep = new List<string>();
        for (var i = 1; i <= 12; i++)
        {
            clock.Advance(1);
            var s = stepper.SolutionOf("甲");
            perStep.Add(s.Describe());
            if (s.Status == NavMoveStatus.Arrived)
            {
                break;
            }
        }

        // 逐步位置逐字：0.5 → 2 → 3.5 → … → 15.5，每一步整整 1.5 米（10 步走完 15 米）。
        stepper.PositionOf("甲").ShouldBe(new NavPoint(15.5, 0.5), "10 步 × 1.5 米 = 15 米，没有一步被前视点截短");
        stepper.SolutionOf("甲").Status.ShouldBe(NavMoveStatus.Arrived);

        // 每一步的位移都是 1.5 米，前视点始终只在前 0.5 米处 —— 两者无关就是这一条。
        for (var i = 0; i < 10; i++)
        {
            perStep[i].ShouldContain("位移 1.5 ", Case.Sensitive);
            perStep[i].ShouldContain("前视 (", Case.Sensitive);
        }

        perStep[0].ShouldBe(
            "甲 GoTo 位置 (2, 0.5) 朝向 90 速度 15 位移 1.5 状态 Moving 卡住 None/0 子步 3 "
            + "目标 (15.5, 0.5) 前视 (1, 0.5) 激活检查 无",
            "第 1 步逐字：前视点在前 0.5 米，位移整整 1.5 米");
    }

    [Fact]
    public void 步长上限是沿路径剩余_不是前视距离()
    {
        // 那一侧跑得最慢的那档移速：1.2 米每秒 ⇒ 每步 0.12 米，前视距离 0.5 米。
        // 中途的步：位移就是整整 0.12 米（没被前视点截短，也没被别的东西截短）。
        // 倒数第二步：沿路径只剩不足 0.12 米，位移就是剩下那一截，落点逐字是目标点。
        // 最后一步：已经在容差圈内，读出到位、位移 0。
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>(), 16, 8)), clock, 0.5);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.2));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(15.4, 0.5), 0.01));

        var steps = new List<NavMoveSolution>();
        NavMoveSolution solution = null!;
        while (steps.Count < 400)
        {
            clock.Advance(1);
            solution = stepper.SolutionOf("甲");
            steps.Add(solution);
            if (solution.Status == NavMoveStatus.Arrived)
            {
                break;
            }
        }

        steps[0].MovedMeters.ShouldBe(0.12, 0.0001, "一步就是 0.12 米");
        steps[steps.Count - 3].MovedMeters.ShouldBe(0.12, 0.0001, "中途每一步都是整整 0.12 米，不被前视点截短");

        var capped = steps[steps.Count - 2];
        capped.MovedMeters.ShouldBeLessThan(0.12, "最后那一动被「沿路径到终点还剩多远」封了顶");
        capped.MovedMeters.ShouldBeGreaterThan(0.0, "封顶不是不走");
        capped.Position.X.ShouldBe(15.4, Cap, "封顶的那一步落点就是目标点");

        var last = steps[steps.Count - 1];
        last.Status.ShouldBe(NavMoveStatus.Arrived);
        last.MovedMeters.ShouldBe(0.0, "已经在容差圈内的那一步不产位移");
    }

    [Fact]
    public void 前视式断言形状_每步位移不超过上限且全程未进阻挡格()
    {
        // L 形墙绕行：横向 (2,0)..(4,0) + 纵向 (4,1)..(4,4)。断言形状就是工单点名的三条。
        var blocked = new List<NavCell>();
        for (var x = 2; x <= 4; x++)
        {
            blocked.Add(new NavCell(x, 0));
        }

        for (var z = 1; z <= 4; z++)
        {
            blocked.Add(new NavCell(4, z));
        }

        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(blocked)), clock, 1.5);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 2.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(6.5, 6.5), 0.25));

        var cap = 2.0 * StepSeconds; // 每步能走的距离
        var steps = 0;
        NavMoveSolution solution = null!;
        while (steps < 60)
        {
            clock.Advance(1);
            steps++;
            solution = stepper.SolutionOf("甲");
            solution.MovedMeters.ShouldBeLessThanOrEqualTo(cap + Cap, $"第 {steps} 步位移不超过上限");
            InBlocked(blocked, solution.Position).ShouldBeFalse(
                $"第 {steps} 步落在 {solution.Position}：全程未进阻挡格（用例自己查那份阻挡表）");
            if (solution.Status == NavMoveStatus.Arrived)
            {
                break;
            }
        }

        solution!.Status.ShouldBe(NavMoveStatus.Arrived, "60 步内绕到了");
        steps.ShouldBeLessThan(60, "N 步内到达这条读数是数得出来的");
        solution.Position.DistanceTo(new NavPoint(6.5, 6.5)).ShouldBeLessThanOrEqualTo(0.25,
            "落在容差圈内（不是「坐标等于某航点」那种断言 —— 绕行路径上落点是算出来的）");
    }

    // ─────────────────── 每步内切子步：解隧穿 ───────────────────

    [Fact]
    public void 隧穿_15米每秒对着1格宽的墙走100步全程不穿()
    {
        // 8×16 空场，z = 8 上一排墙只封 x = 0..6（1 格宽，7 格长），x = 7 留一个缺口。
        // 15 米每秒 ⇒ 每步 1.5 米 > 1 格 —— 不切子步时一格宽的墙在一步之内就被穿过去，
        // 而快照里看不出异常（位置在墙那边、速度是满的、状态是「在走」）。
        var blocked = new List<NavCell>();
        for (var x = 0; x <= 6; x++)
        {
            blocked.Add(new NavCell(x, 8));
        }

        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(blocked, 8, 16)), clock, 3.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 15.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(0.5, 14.5), 0.25));

        NavSubstepper.SubstepCapMeters(new NavGrid(Spec(blocked, 8, 16))).ShouldBe(0.5,
            "子步长上限是 0.5 格长，1 米一格就是 0.5 米");

        var crossed = new List<string>();
        var substepCounts = new HashSet<int>();
        var steps = 0;
        NavMoveSolution solution = null!;
        while (steps < 100)
        {
            clock.Advance(1);
            steps++;
            solution = stepper.SolutionOf("甲");
            if (InBlocked(blocked, solution.Position))
            {
                crossed.Add($"第 {steps.ToString(CultureInfo.InvariantCulture)} 步落在 {solution.Position}");
            }

            substepCounts.Add(solution.Substeps);
            solution.MovedMeters.ShouldBeLessThanOrEqualTo(1.5 + Cap, "每步位移不超过 15 ÷ 10 = 1.5 米");
            if (solution.Status == NavMoveStatus.Arrived)
            {
                break;
            }
        }

        crossed.ShouldBeEmpty("全程没进过一格阻挡格 —— 隧穿这一档就是为它存在的");
        solution!.Status.ShouldBe(NavMoveStatus.Arrived, "100 步之内绕过了那排墙");
        steps.ShouldBe(15, "实测：15 步绕到 (0.5, 14.5)");
        var sortedSubsteps = new List<int>(substepCounts);
        sortedSubsteps.Sort();
        string.Join(",", sortedSubsteps).ShouldBe("0,1,3",
            "1.5 米的一步 = 3 个子步；快到终点那一步被剩余封到 0.5 米以内 = 1 个子步；0 = 到位那一步不产位移");

        // 撞墙那一档的读数：子步在最后一格合法处停下，速度读数归 0，位置不出墙。
        var wall = new GameClock();
        var sealedWall = new List<NavCell>();
        for (var x = 0; x < 8; x++)
        {
            sealedWall.Add(new NavCell(x, 8));
        }

        var walled = StepperOf(SolverOf(Spec(sealedWall, 8, 16)), wall, 3.0);
        walled.Register(Agent("甲", new NavPoint(0.5, 0.5), 15.0));
        walled.Issue("甲", NavCommand.GoTo(new NavPoint(0.5, 14.5), 0.25));
        for (var i = 0; i < 100; i++)
        {
            wall.Advance(1);
            InBlocked(sealedWall, walled.SolutionOf("甲").Position).ShouldBeFalse(
                $"封死那一步（第 {i + 1} 步）也没进墙：那一格 (0.5, 7.5) 是墙前最后一格");
        }

        var pinned = walled.SolutionOf("甲");
        pinned.Position.ShouldBe(new NavPoint(0.5, 7.5), "停在墙前最后一格，不贴在墙皮上、也不退回本步开头");
        pinned.Status.ShouldBe(NavMoveStatus.NoTerrainRoute);
        pinned.Stuck.ShouldBe(NavStuckKind.NoTerrainRoute, "本期卡住类别只有「无」与「地形无路」两档");
        pinned.StuckSteps.ShouldBe(100, "卡住步数 = 这一类卡住连续出现了几步");
        pinned.SpeedMetersPerSecond.ShouldBe(0.0, "速度读数是本步实际位移换算的，没挪就是 0");
    }

    [Fact]
    public void 对角子步不许切角_两格对角夹缝()
    {
        // 封 (2,1) 与 (1,2)：格 (1,1) 与 (2,2) 之间是一个只能绕、不能斜穿的夹缝。
        var blocked = new List<NavCell> { new(2, 1), new(1, 2) };
        var grid = new NavGrid(Spec(blocked));

        NavPathStraightener.SegmentWalkable(grid, new NavPoint(1.5, 1.5), new NavPoint(2.5, 2.5), 0.0)
            .ShouldBeFalse("直着斜穿那两格之间的夹角是不通的 —— 这条用例钉的是真挤缝，不是一个本来就通的场");

        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(blocked)), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(1.5, 1.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(2.5, 2.5), 0.1));

        var touched = new List<string>();
        var steps = 0;
        NavMoveSolution solution = null!;
        while (steps < 40)
        {
            clock.Advance(1);
            steps++;
            solution = stepper.SolutionOf("甲");
            if (InBlocked(blocked, solution.Position))
            {
                touched.Add($"第 {steps.ToString(CultureInfo.InvariantCulture)} 步进了一格阻挡：{solution.Position}");
            }

            if (solution.Position == new NavPoint(1.0, 2.0))
            {
                touched.Add($"第 {steps.ToString(CultureInfo.InvariantCulture)} 步正好落在两个阻挡格的公共角上");
            }

            if (solution.Status == NavMoveStatus.Arrived)
            {
                break;
            }
        }

        touched.ShouldBeEmpty("全程没有斜穿过那个夹角，也没踩在它的角点上");
        solution!.Status.ShouldBe(NavMoveStatus.Arrived, "绕过去了");
        solution.Position.DistanceTo(new NavPoint(2.5, 2.5)).ShouldBeLessThanOrEqualTo(0.1);
    }

    [Fact]
    public void 朝向是逐子步的终点方向_瞬时对准不带转身时间()
    {
        // 三个方向各钉一条：+X、+Z、以及一个斜的。朝向逐字，不带转身时间。
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5, facing: 0.0));
        stepper.Register(Agent("乙", new NavPoint(2.5, 0.5), 1.5, facing: 0.0));
        stepper.Register(Agent("丙", new NavPoint(1.5, 1.5), 1.5, facing: 0.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        stepper.Issue("乙", NavCommand.GoTo(new NavPoint(2.5, 7.5), 0.25));
        stepper.Issue("丙", NavCommand.GoTo(new NavPoint(6.5, 6.5), 0.25));

        clock.Advance(1);

        stepper.SolutionOf("甲").FacingDegrees.ShouldBe(90.0, "0 度是 +Z，+X 是 +90");
        stepper.SolutionOf("乙").FacingDegrees.ShouldBe(0.0, "+Z 就是 0 度");
        stepper.SolutionOf("丙").FacingDegrees.ShouldBe(45.0, Cap, "对角是 +45（逐子步的终点方向，瞬时对准）");
    }

    [Fact]
    public void 走到头不动时朝向保持原样()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 1.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5, facing: 137.5));
        stepper.Issue("甲", NavCommand.Hold(new NavPoint(0.5, 0.5)));

        clock.Advance(1);
        clock.Advance(1);

        stepper.SolutionOf("甲").FacingDegrees.ShouldBe(137.5,
            "一个子步都没挪出去就保持原朝向 —— 「站在原地转半圈」在读数上是一个查不出原因的角度跳变");
    }

    // ─────────────────── 检查点快照 ───────────────────

    [Fact]
    public void 移动解进检查点快照_六列读数都在()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        clock.Advance(1);

        var segment = stepper.DebugSnapshotSegment();
        segment.ShouldBe(
            "pathfinding space=巨兽甲板 step=1 agents=1\n"
            + "甲 GoTo 位置 (0.65, 0.5) 朝向 90 速度 1.5000000000000002 位移 0.15000000000000002 "
            + "状态 Moving 卡住 无/0 子步 1 目标 (7.5, 0.5) 前视 (2.5, 0.5) 激活检查 无\n",
            "快照段逐字：位置、朝向、速度、状态、卡住类别、卡住步数六列都在");

        // 网格、净空与两个版本号不在这儿 —— 那是工单 06 的活，本期不越界。
        segment.ShouldNotContain("净空", Case.Sensitive);
        segment.ShouldNotContain("静态版本", Case.Sensitive);

        stepper.ReplaySnapshotSegment().ShouldBe(segment,
            "调试段与重放段两个名字、一份格式：列要是两处各拼一次就会漂");
    }

    [Fact]
    public void 逐步与大块快进逐字相等()
    {
        static NavStepper Rig(GameClock clock)
        {
            var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 1.5);
            stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 3.0));
            stepper.Register(Agent("乙", new NavPoint(0.5, 3.5), 1.2));
            stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 7.5), 0.25));
            stepper.Issue("乙", NavCommand.Along(
                new[] { new NavPoint(4.5, 3.5), new NavPoint(4.5, 6.5) }, 0.25));
            return stepper;
        }

        var steppedClock = new GameClock();
        var stepped = Rig(steppedClock);
        var trail = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            steppedClock.Advance(1);
            trail.Add(stepped.DebugSnapshotSegment());
        }

        var fastClock = new GameClock();
        var fast = Rig(fastClock);
        fastClock.Advance(50);

        fast.DebugSnapshotSegment().ShouldBe(trail[49],
            "Advance(1) 推 50 次与 Advance(50) 推一次，第 50 步的快照逐字相等：吃的是逻辑步，不是每帧");
        fast.ReplaySnapshotSegment().ShouldBe(trail[49]);
        fast.SolutionOf("甲").Describe().ShouldBe(stepped.SolutionOf("甲").Describe());
        fast.SolutionOf("乙").Describe().ShouldBe(stepped.SolutionOf("乙").Describe());
        fast.SolutionOf("甲").Position.ShouldBe(new NavPoint(7.5, 7.5), "50 步 3 米每秒走完对角");
    }

    // ─────────────────── 移动激活检查那一道口 ───────────────────

    [Fact]
    public void 激活检查拒了交出读数_不许静默跳过()
    {
        var asked = new List<string>();
        var clock = new GameClock();
        var stepper = StepperOf(
            SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0,
            (id, step, meters) =>
            {
                asked.Add($"{id}@{step.ToString(CultureInfo.InvariantCulture)} 步 打算走 {meters} 米");
                return new NavMoveCheckVerdict(false, "定身");
            });
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        clock.Advance(1);

        var s = stepper.SolutionOf("甲");
        s.Status.ShouldBe(NavMoveStatus.HeldByActivationCheck, "激活检查拒了是一条读数，不是「站在原地不动」");
        s.CheckReason.ShouldBe("定身", "拒绝必须带一句说法，否则读数上与原地不动长得一样");
        s.Position.ShouldBe(new NavPoint(0.5, 0.5), "不放行就不位移");
        s.SpeedMetersPerSecond.ShouldBe(0.0);

        asked.ShouldBe(new[] { "甲@1 步 打算走 0.15000000000000002 米" }, Case.Sensitive,
            "激活检查排在求解之前，看到的是标称步长（移速 × 0.1 秒）");
    }

    [Fact]
    public void 不递激活检查就是全放行()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        clock.Advance(1);

        stepper.SolutionOf("甲").Status.ShouldBe(NavMoveStatus.Moving);
        stepper.SolutionOf("甲").CheckReason.ShouldBe("无");
    }

    // ─────────────────── 负向与入参 ───────────────────

    [Fact]
    public void 卡住类别是那三类加一个无_工单05补齐了另两档()
    {
        // 钉的是「不许用 0 / -1 那种占位数」：每一步要么说「无」，要么说 spec 那三类之一。
        // 这一条断言第三期写的是两档（只有「无」与「地形无路」），工单 05 把另两档补齐了：
        // 拥挤推挤（站位冲突叠加修正）与预算触顶（回部分路径）。
        // 逐条类别与步数的用例在 Tests/NavSeparationTests.cs。
        Enum.GetValues<NavStuckKind>().ShouldBe(new[]
        {
            NavStuckKind.None,
            NavStuckKind.NoTerrainRoute,
            NavStuckKind.Crowded,
            NavStuckKind.BudgetCapped,
        });
    }

    [Fact]
    public void 目标落不进可行域交出地形无路()
    {
        var blocked = new List<NavCell> { new(3, 3) };
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(blocked)), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(3.5, 3.5), 0.25));

        for (var i = 0; i < 5; i++)
        {
            clock.Advance(1);
        }

        var s = stepper.SolutionOf("甲");
        s.Status.ShouldBe(NavMoveStatus.NoTerrainRoute, "目标那一格净空 0，不该启动这个命令");
        s.Stuck.ShouldBe(NavStuckKind.NoTerrainRoute);
        s.StuckSteps.ShouldBe(5, "连着 5 步走不了");
        s.Position.ShouldBe(new NavPoint(0.5, 0.5), "不返空也不乱挪：位置与速度都读得出来");
    }

    [Fact]
    public void 入参不成形状当场抛()
    {
        var clock = new GameClock();
        var solver = SolverOf(Spec(Array.Empty<NavCell>()));

        Should.Throw<ArgumentOutOfRangeException>(() => NavCommand.GoTo(new NavPoint(1.0, 1.0), -0.1))
            .Message.ShouldContain("到达容差", Case.Sensitive, "容差由命令递进来，层里没有缺省");
        Should.Throw<ArgumentOutOfRangeException>(() => NavCommand.GoTo(new NavPoint(1.0, 1.0), double.NaN));
        Should.Throw<ArgumentOutOfRangeException>(() => NavCommand.Guard(new NavPoint(1.0, 1.0), 0.0, 0.5))
            .Message.ShouldContain("守区域", Case.Sensitive, "半径 0 的守区域就是走到点那一档");
        Should.Throw<ArgumentOutOfRangeException>(() => NavCommand.Chase("  ", new NavPoint(1.0, 1.0), 0.5))
            .Message.ShouldContain("id", Case.Sensitive);
        Should.Throw<ArgumentOutOfRangeException>(() => NavCommand.Along(Array.Empty<NavPoint>(), 0.5))
            .Message.ShouldContain("航点", Case.Sensitive, "空航点串读作哪也不去，那一档叫原地不动");
        Should.Throw<ArgumentOutOfRangeException>(() => NavCommand.GoTo(new NavPoint(double.NaN, 1.0), 0.5))
            .Message.ShouldContain("坐标", Case.Sensitive, "非有限坐标让每一处距离判定都退化成恒值读数");

        Should.Throw<ArgumentOutOfRangeException>(() => new NavStepper(
            solver,
            new NavMoveTuning { StepSeconds = 0.1, LookAheadMeters = 0.0, StepSolveExpansions = 100 },
            clock)).Message.ShouldContain("前视距离", Case.Sensitive);
        Should.Throw<ArgumentOutOfRangeException>(() => new NavStepper(
            solver,
            new NavMoveTuning { StepSeconds = 0.1, LookAheadMeters = 1.0, StepSolveExpansions = 0 },
            clock)).Message.ShouldContain("扩展上限", Case.Sensitive);
    }

    [Fact]
    public void 登记与发令的入参当场抛()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));

        Should.Throw<ArgumentOutOfRangeException>(() => stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5)))
            .Message.ShouldContain("登记过", Case.Sensitive, "同一个 id 登两次，快照里就有两行同名记录");
        Should.Throw<ArgumentOutOfRangeException>(() => stepper.Register(Agent("乙", new NavPoint(0.5, 0.5), -1.0)))
            .Message.ShouldContain("移速", Case.Sensitive, "负移速让「往哪儿走多远」没有读数");
        Should.Throw<KeyNotFoundException>(() => stepper.SolutionOf("丙"))
            .Message.ShouldContain("登记过", Case.Sensitive, "问一个没登记的 id，问出来的是上一个查过的单位，那比抛更坏");
    }

    [Fact]
    public void 没发令交出未下令_不假装是原地不动()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));

        stepper.SolutionOf("甲").Status.ShouldBe(NavMoveStatus.Unset, "注册之后一发就有读数");
        clock.Advance(1);
        var s = stepper.SolutionOf("甲");
        s.Status.ShouldBe(NavMoveStatus.Unset, "没人给它下令，它与「原地不动」是两件事");
        s.Command.ShouldBe(NavCommandKind.Unset);
        s.Position.ShouldBe(new NavPoint(0.5, 0.5));
    }

    [Fact]
    public void 退订之后不再步进()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        clock.Advance(2);
        stepper.Detach();
        var parked = stepper.PositionOf("甲");

        clock.Advance(10);
        stepper.PositionOf("甲").ShouldBe(parked, "退订之后逻辑时钟再推也不动它");
        stepper.StepIndex.ShouldBe(2);
    }

    // ─────────────────── 工单 08 开的三个口：落位移 / 移速标量 / 这一趟算不算它 ───────────────────

    private static NavSeparationTuning Crowded() => new() { MinNetStepMeters = 0.16 };

    [Fact]
    public void 落位移_位置与朝向立刻改_位移与状态那几列一个字不动()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        clock.Advance(1);

        var before = stepper.SolutionOf("甲");
        before.MovedMeters.ShouldBe(0.15, 0.000001, "先走一步拿到一个非零的位移读数");

        stepper.ApplyMove("甲", new NavPoint(3.5, 4.5), 137.5);

        var after = stepper.SolutionOf("甲");
        after.Position.ShouldBe(new NavPoint(3.5, 4.5), "位置那一列立刻是新落点");
        after.FacingDegrees.ShouldBe(137.5, "朝向一起落到位");
        after.MovedMeters.ShouldBe(before.MovedMeters, "位移那一列是「这一步走位」的账 —— 落位置不写它，"
            + "否则「它刚才走了多远」与「它被放到哪」在读数上并成一个数");
        after.Status.ShouldBe(before.Status, "状态与卡住那几列同理：落位置不是走位，不改判定");
        after.Stuck.ShouldBe(before.Stuck);
        stepper.PositionOf("甲").ShouldBe(new NavPoint(3.5, 4.5), "位置真值就在流水线这一份");

        // 快照段逐字带上新落点（检查点快照那一格是接上的理由）。
        stepper.DebugSnapshotSegment().ShouldContain("位置 (3.5, 4.5) 朝向 137.5", Case.Sensitive,
            "落点进检查点快照：重放端对读时「这一发落位没落上」当场看得出来");
    }

    [Fact]
    public void 落位移出界不夹也不抛_下一次求解照实报地形无路()
    {
        // 8×8 格、1 米一格 ⇒ 网格局部米 0~8。落点放到 20 米（界外 12 米）。
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));

        stepper.ApplyMove("甲", new NavPoint(20.0, 20.0), 0.0);
        stepper.PositionOf("甲").ShouldBe(new NavPoint(20.0, 20.0), "给在哪就是哪：这一格不夹、不抛");

        clock.Advance(1);
        var s = stepper.SolutionOf("甲");
        s.Status.ShouldBe(NavMoveStatus.NoTerrainRoute, "界外落点下一次求解报地形无路 —— "
            + "「落点出界了」在读数上看得见，比在这一格偷偷夹回一手好查");
        s.Stuck.ShouldBe(NavStuckKind.NoTerrainRoute);
    }

    [Fact]
    public void 移速标量每步可换_换完那一步的步长就变了()
    {
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));   // 一步 0.15 米
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        clock.Advance(1);
        stepper.SolutionOf("甲").MovedMeters.ShouldBe(0.15, 0.000001, "1.5 米每秒 × 0.1 秒 = 0.15 米");

        // 宿主每步递一次「这一局此刻」的数（抖动后的速度 × 技能层派生倍率）。
        stepper.SetMoveSpeed("甲", 3.0);
        clock.Advance(1);
        stepper.SolutionOf("甲").MovedMeters.ShouldBe(0.30, 0.000001, "3.0 米每秒 × 0.1 秒 = 0.30 米 —— "
            + "换完那一步立刻按新数走");
        stepper.SolutionOf("甲").SpeedMetersPerSecond.ShouldBe(3.0, 0.000001, "速度那一列是净位移 ÷ 步长秒数");

        Should.Throw<ArgumentOutOfRangeException>(() => stepper.SetMoveSpeed("甲", -1.0))
            .Message.ShouldContain("移速", Case.Sensitive, "负移速让「往哪儿走多远」没有读数");
        Should.Throw<KeyNotFoundException>(() => stepper.SetMoveSpeed("乙", 3.0));
    }

    [Fact]
    public void 这一趟不算它的那个_既不被推开也不推别人()
    {
        // 两个单位完全重合（0.4 + 0.4 的最小间距 ⇒ 整趟都该被推开）。
        // 把乙记成「这一趟不算它」：这一对一个数都不动，**两头都不参与**。
        var clock = new GameClock();
        var stepper = StepperOf(SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0, separation: Crowded());
        stepper.Register(Agent("甲", new NavPoint(3.0, 3.0), 1.5, radius: 0.4));
        stepper.Register(Agent("乙", new NavPoint(3.0, 3.0), 1.5, radius: 0.4));
        stepper.Issue("甲", NavCommand.Hold(new NavPoint(3.0, 3.0)));
        stepper.Issue("乙", NavCommand.Hold(new NavPoint(3.0, 3.0)));
        stepper.SetCountedInSeparation("乙", false);

        stepper.IsCountedInSeparation("甲").ShouldBeTrue("缺省是「算」—— 不递这一发的宿主拿到的还是全参与那一种行为");
        stepper.IsCountedInSeparation("乙").ShouldBeFalse();

        clock.Advance(1);

        stepper.PositionOf("甲").ShouldBe(new NavPoint(3.0, 3.0),
            "不参与避让的那一个确实没被推开 —— 也不推别人，所以「被推的那一半」同样一个数都不动");
        stepper.PositionOf("乙").ShouldBe(new NavPoint(3.0, 3.0));
        stepper.SolutionOf("甲").Stuck.ShouldBe(NavStuckKind.None, "没有配对就没有「卡在谁身上」那一档可读");

        // 把它放回参与那一边：同一份输入立刻给出分开的结果（证明上一条不是「压根没开避让」）。
        stepper.SetCountedInSeparation("乙", true);
        clock.Advance(1);
        stepper.PositionOf("甲").ShouldBe(new NavPoint(2.6, 3.0), "记成算 ⇒ 沿第一根轴各退一半（0.8 的一半 = 0.4）");
        stepper.PositionOf("乙").ShouldBe(new NavPoint(3.4, 3.0));

        Should.Throw<KeyNotFoundException>(() => stepper.SetCountedInSeparation("丙", false));
    }

    [Fact]
    public void 这一趟不算它只管避让_能不能动是激活检查那一件()
    {
        // 两件不同的事：激活检查管「许不许动」，参与标记管「避让算不算它」。
        // 混起来最糟的读数是「不动了，而读数说它被挤住了」。
        var clock = new GameClock();
        var held = false;
        var stepper = StepperOf(
            SolverOf(Spec(Array.Empty<NavCell>())), clock, 2.0,
            gate: (id, step, planned) => held ? new NavMoveCheckVerdict(false, "定身") : NavMoveCheckVerdict.Allow,
            separation: Crowded());
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5, radius: 0.4));
        stepper.Register(Agent("乙", new NavPoint(0.5, 0.5), 1.5, radius: 0.4));
        stepper.SetCountedInSeparation("乙", false);
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        stepper.Issue("乙", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));

        clock.Advance(1);
        stepper.SolutionOf("乙").Status.ShouldBe(NavMoveStatus.Moving,
            "不参与避让的那个照样按命令走位 —— 不参与避让不等于不许动");
        stepper.SolutionOf("乙").MovedMeters.ShouldBe(0.15, 0.000001);

        held = true;
        clock.Advance(1);
        stepper.SolutionOf("乙").Status.ShouldBe(NavMoveStatus.HeldByActivationCheck, "激活检查拒了照旧交出读数与那句说法");
        stepper.SolutionOf("乙").CheckReason.ShouldBe("定身");
        stepper.SolutionOf("乙").MovedMeters.ShouldBe(0.0, "激活检查拒了就是一步都不挪");
    }

    // === 复用一条还成立的路（本 ticket 新增：大网格 + 上百单位那一档的求解体积） ===

    /// <summary>求解账本里一共记了几条（<c>Count</c> 是还留着的、<c>Dropped</c> 是被环形顶掉的，两者之和才是总数）。</summary>
    private static int Solves(NavSolver solver) => solver.Records.Count + solver.Records.Dropped;

    /// <summary>开了复用之后：目标没动、地形没变的那几步，<b>求解账本不再增长</b>（那几步压根没求解）。
    ///
    /// <para>判据走账本：<c>Records</c> 是有界的环形账本，每一步求解各追加一条。
    /// 复用那几步一条都不追加 —— 这就是「它没有重解」的机器证据。</para>
    /// </summary>
    [Fact]
    public void 复用还成立的路_目标没动的那几步不重解()
    {
        var clock = new GameClock();
        var solver = SolverOf(Spec(Array.Empty<NavCell>()));
        var stepper = StepperOf(solver, clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));

        clock.Advance(1);
        var afterFirst = Solves(solver);
        afterFirst.ShouldBe(1, "第一步求解一次");

        for (var i = 0; i < 5; i++)
        {
            clock.Advance(1);
        }

        Solves(solver).ShouldBe(afterFirst,
            "目标没动、地形没变的这五步不该再求解 —— 复用这一档的全部意义就在这里");
        stepper.SolutionOf("甲").Status.ShouldBe(NavMoveStatus.Moving, "复用不等于停下：它照那条路继续走");
        stepper.SolutionOf("甲").MovedMeters.ShouldBeGreaterThan(0.0, "复用的那几步照样有位移");
    }

    /// <summary>
    /// 复用是唯一行为、<b>没有开关可关</b>，而且<b>按单位各算各的</b>：
    /// 目标不动的那个一步只解一次，每步换命令的那个每步都解一次。
    /// </summary>
    [Fact]
    public void 复用是唯一行为_按单位各算各的()
    {
        var clock = new GameClock();
        var solver = SolverOf(Spec(Array.Empty<NavCell>()));
        var stepper = StepperOf(solver, clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.0));
        stepper.Register(Agent("乙", new NavPoint(0.5, 3.5), 1.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));
        stepper.Issue("乙", NavCommand.GoTo(new NavPoint(7.5, 3.5), 0.25));

        clock.Advance(1);
        Solves(solver).ShouldBe(2, "第一步两个单位各求解一次");

        for (var i = 0; i < 4; i++)
        {
            // 乙每步换一条命令、目标挪出 0.5 米阈值之外 ⇒ 它每步都重解
            stepper.Issue("乙", NavCommand.GoTo(new NavPoint(7.5, 3.5 + ((i + 1) * 2.0)), 0.25));
            clock.Advance(1);
        }

        // 甲：1 次；乙：首次 1 次 + 这四步各 1 次 = 5 次
        Solves(solver).ShouldBe(6,
            "不动的那个没重解、换命令的那个每步重解 —— 复用按单位各算各的，且没有开关");
    }

    /// <summary>
    /// 目标移动超过阈值 <b>立刻重解</b>：复用不许把「它其实该换路了」这件事吞掉。
    /// </summary>
    [Fact]
    public void 复用期间目标动了超过阈值_立刻重解()
    {
        var clock = new GameClock();
        var solver = SolverOf(Spec(Array.Empty<NavCell>()));
        var stepper = StepperOf(solver, clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));

        clock.Advance(3);
        var before = Solves(solver);

        // 换一条命令、目标挪出阈值之外（阈值 0.5 米，这里挪 4 米）
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 4.5), 0.25));
        clock.Advance(1);

        Solves(solver).ShouldBeGreaterThan(before, "目标真的动了就必须重解");
        stepper.SolutionOf("甲").Goal.ShouldBe(new NavPoint(7.5, 4.5), "目标读数是新的那一个");
    }

    /// <summary>
    /// 地形变了（网格封了一格）<b>立刻重解</b>：这一条是「绕远路能到」不被读成
    /// 「地形无路」的那条保证 —— 复用着的路在墙砌上之后就作废了。
    /// </summary>
    [Fact]
    public void 复用期间地形变了_立刻重解()
    {
        var clock = new GameClock();
        var grid = new NavGrid(Spec(Array.Empty<NavCell>()));
        var solver = new NavSolver(
            new NavSpace(Deck, grid),
            new NavSolveBudget { PerRequestExpansions = 4000 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);
        var stepper = StepperOf(solver, clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 3.5), 1.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 3.5), 0.25));

        clock.Advance(2);
        var before = Solves(solver);

        NavTerrain.Apply(grid, new NavCell(4, 3), NavTerrainLayer.Blocked, 1.0);
        clock.Advance(1);

        Solves(solver).ShouldBeGreaterThan(before, "地形版本变了就必须重解");
    }

    /// <summary>
    /// 上一次求解是<b>预算触顶</b>（每请求只给 1 格扩展，第一条路必然被截断）：
    /// 下一步必须重解 —— 手上那条是被截断的路，照着它走会停在那儿。
    /// </summary>
    [Fact]
    public void 预算触顶的那一步_下一步必须重解()
    {
        var clock = new GameClock();
        var solver = SolverOf(Spec(Array.Empty<NavCell>()), budget: 1);
        var stepper = StepperOf(solver, clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));

        clock.Advance(1);
        stepper.SolutionOf("甲").Stuck.ShouldBe(NavStuckKind.BudgetCapped,
            "每请求 1 格 ⇒ 这一条求解一定触顶，卡住那一列读的就是预算");

        clock.Advance(3);
        Solves(solver).ShouldBe(4, "上一次触顶 ⇒ 这四步每一步都要重解（预算那一条触发）");
    }

    /// <summary>
    /// 走满「没到位步数上限」的那一步必须重解：<b>上限是 N 步，第 N 步上重解</b>，
    /// 重解之后重新开始攒步数。
    /// </summary>
    [Fact]
    public void 走满没到位步数上限_必须重解()
    {
        var clock = new GameClock();
        var solver = new NavSolver(
            new NavSpace(Deck, new NavGrid(Spec(Array.Empty<NavCell>()))),
            new NavSolveBudget { PerRequestExpansions = 4000 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 3 },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);
        var stepper = StepperOf(solver, clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 0.5), 0.25));

        clock.Advance(1);
        Solves(solver).ShouldBe(1, "第一步求解一次");

        clock.Advance(2);
        Solves(solver).ShouldBe(1, "没满 3 步，手上那条路还成立");

        clock.Advance(1);
        Solves(solver).ShouldBe(2, "第 3 步走满上限 ⇒ 重解一次");

        clock.Advance(2);
        Solves(solver).ShouldBe(2, "重解之后重新攒步数，这两步不重解");

        clock.Advance(1);
        Solves(solver).ShouldBe(3, "又满 3 步，再解一次");
    }

    /// <summary>
    /// 直路被一堵墙封住时，<b>复用期间照样绕得到</b>：第一条路就是绕过墙的那一条，
    /// 复用只是照着它继续走，于是「绕远路」不会被读成「地形无路」。
    /// </summary>
    [Fact]
    public void 墙封了直路_复用期间照样绕得到()
    {
        // 12×8 的场地，x=6 那一列封到 z=5，z∈[6,8) 留两格宽的缺口
        var blocked = new List<NavCell>();
        for (var z = 0; z <= 5; z++)
        {
            blocked.Add(new NavCell(6, z));
        }

        var clock = new GameClock();
        var solver = new NavSolver(
            new NavSpace(Deck, new NavGrid(Spec(blocked, x: 12))),
            new NavSolveBudget { PerRequestExpansions = 4000 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 1000 },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);
        var stepper = StepperOf(solver, clock, 2.0);
        stepper.Register(Agent("甲", new NavPoint(0.5, 2.5), 2.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(11.5, 2.5), 0.25));

        clock.Advance(1);
        Solves(solver).ShouldBe(1, "第一条路一步就解出来");
        stepper.SolutionOf("甲").Stuck.ShouldBe(NavStuckKind.None, "绕远路不是卡住");

        var arrived = false;
        var steps = 0;
        var blockedSteps = 0;
        while (!arrived && steps < 200)
        {
            clock.Advance(1);
            steps++;
            arrived = stepper.SolutionOf("甲").Status == NavMoveStatus.Arrived;
            if (stepper.SolutionOf("甲").Stuck != NavStuckKind.None)
            {
                blockedSteps++;
            }
        }

        arrived.ShouldBeTrue($"绕墙的那条路必须走到头：步数={steps} 解过={Solves(solver)} 被挡下的步数={blockedSteps}");
        blockedSteps.ShouldBeLessThanOrEqualTo(3, "墙角上被挡下的步数是个位数 —— 挡下一次就重解，不是每步都撞");
        Solves(solver).ShouldBe(1 + blockedSteps, "只解一次 + 每被挡下一次补一次重解，没有别的重解");
        stepper.SolutionOf("甲").Status.ShouldBe(NavMoveStatus.Arrived);
    }
}
