#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Pathfinding.Stage;
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 「本步正在走的那条折线」那个只读口（<c>NavStepper.CornersOf</c>）的契约测试。
///
/// <para>它存在只为一件事：**让视图画出逻辑实际在走的那条路，而不必自己再算一遍**
/// （「表现层只读」 表现层只读；自己 <c>Solve</c> 一次就是第二次计算，画面上摆的路与逻辑走的路
/// 会在某一帧分叉而画面上看不出来）。</para>
///
/// <para>这里钉的是三组：
/// <list type="number">
/// <item><b>走位中</b>：交出的就是这一步送进 <c>NavLookAhead</c> 的那一份，首位是步首位置、
/// 末位是目标（逐字钉住）；</item>
/// <item><b>本步压根没求解的那三步</b>（未发令 / 已到位 / 激活检查拒了）：交出<b>脚下那一个点</b>，
/// 各有一条用例 —— 激活检查排在上游，那一步没有「正在走的路」，交上一步那条会骗人；</item>
/// <item><b>没路那几步</b>：已经求解过了，交出<b>那一条走不到目标的路径本身</b>
/// （末端不是目标点 —— 把它补成通到目标的线才是骗人）。</item>
/// </list></para>
///
/// <para>逐字读数全部先跑探针读出来再写成断言（探针落 <c>.cache/Pathfinding/StageProbe/</c>，用完即删）。</para>
/// </summary>
public class NavCornersTests
{
    private const double StepSeconds = 0.1;   // 「时间基准」：一律 0.1 秒

    // ---------- 走位中 ----------

    [Fact]
    public void 走位中交出本步那一条折线_首位是步首位置末位是目标()
    {
        var stage = new NavStage(NavStageSample.Spec());
        for (var i = 0; i < 8; i++)
        {
            stage.AdvanceStep();
        }

        var corners = stage.Stepper.CornersOf(NavStageSample.RunnerId);
        corners.Count.ShouldBe(2, "跑者那条折线在这一步是两个点");
        corners[0].ShouldBe(new NavPoint(5.940782971206433, 10.471198020846646),
            "首位是这一步开头那个位置（折线从那儿起算）；复用这一档下实测值，"
            + "逐步重解时是 (6.08545363531178, 10.244567658964797)");
        corners[^1].ShouldBe(new NavPoint(14, 14), "末位是目标点");

        // 慢的那个同一步是三个点（绕那道墙多一个拐点）—— 两个单位走同一条路的那一份。
        var slow = stage.Stepper.CornersOf(NavStageSample.CreeperId);
        slow.Count.ShouldBe(3);
        slow[^1].ShouldBe(new NavPoint(14, 13));
    }

    [Fact]
    public void 折线首位是步首位置_不是这一步走完之后的位置()
    {
        // 这一条把「首位读什么」钉死：它读的是**求解时的起点**，不是当前所在点。
        // 两者相等的那几步看不出来，被避让推开的那一步才看得出差别。
        var stage = new NavStage(NavStageSample.Spec());
        var startOfStep = stage.Stepper.PositionOf(NavStageSample.RunnerId);
        stage.AdvanceStep();
        var corners = stage.Stepper.CornersOf(NavStageSample.RunnerId);
        corners[0].ShouldBe(startOfStep, "折线首位 = 推这一步之前的位置");
        stage.Stepper.PositionOf(NavStageSample.RunnerId).ShouldNotBe(
            corners[0], "推完之后的位置与折线首位不是同一个点（正常步进就会分家）");
    }

    [Fact]
    public void 交出的那一份与移动解对得上_前视点就落在这条折线上()
    {
        // 「只交出、不重算」的可观察后果：前视点是**沿这一步那一条折线**取的，
        // 所以交出来的折线与移动解的前视点必然自洽 —— 视图照着画才对得上画面。
        // 判法是**一份独立的几何检查**（点在折线某一段上：叉积 ≈ 0 且投影落在段内），
        // 不复用 `NavLookAhead` 那份实现。
        var stage = new NavStage(NavStageSample.Spec());
        for (var i = 0; i < 6; i++)
        {
            stage.AdvanceStep();
        }

        var solution = stage.Stepper.SolutionOf(NavStageSample.RunnerId);
        var corners = stage.Stepper.CornersOf(NavStageSample.RunnerId);
        corners.ShouldNotBeEmpty();
        solution.Goal.ShouldBe(corners[^1], "目标 = 折线末位");
        LiesOnPolyline(solution.Aim, corners).ShouldBeTrue(
            $"前视点 {solution.Aim} 必须落在这条折线上（交出来的要是别的路，画面与逻辑就对不上）");
    }

    // ---------- 本步压根没求解的那三步 ----------

    [Fact]
    public void 已到位那一步交出脚下那一个点_不交上一步那条()
    {
        var stage = new NavStage(NavStageSample.Spec());
        var walking = 0;
        // 第 0 步还没走（那一行是「未下令」），所以先走一步再进循环 ——
        // 用 `== Moving` 当循环条件时，第一圈的读数是「未下令」，直接跳出就断言错了那一步。
        stage.AdvanceStep();
        while (stage.Stepper.SolutionOf(NavStageSample.RunnerId).Status == NavMoveStatus.Moving)
        {
            stage.AdvanceStep();
            walking++;
            if (walking > 200)
            {
                throw new InvalidOperationException("200 步还没到位，这一局的读数变了");
            }
        }

        stage.Stepper.SolutionOf(NavStageSample.RunnerId).Status.ShouldBe(NavMoveStatus.Arrived);
        var corners = stage.Stepper.CornersOf(NavStageSample.RunnerId);
        corners.Count.ShouldBe(1, "已到位那一步没有「正在走的路」");
        corners[0].ShouldBe(new NavPoint(14, 14), "交出的是脚下那一点");
    }

    [Fact]
    public void 还没发令那一步交出脚下那一个点()
    {
        var stage = new NavStage(OneUnit(id: "站着的", start: new NavPoint(2.5, 2.5), order: null));
        stage.AdvanceStep();
        stage.Stepper.SolutionOf("站着的").Status.ShouldBe(NavMoveStatus.Unset);
        var corners = stage.Stepper.CornersOf("站着的");
        corners.Count.ShouldBe(1);
        corners[0].ShouldBe(new NavPoint(2.5, 2.5), "没发令：脚下那一点");
    }

    [Fact]
    public void 激活检查拒了那一步交出脚下那一个点_激活检查排在上游所以那一步没求解过()
    {
        // 激活检查排在求解上游是契约（「桥只转发」），所以激活检查拒的那一步**压根没有折线**。
        // 交上一步那条会让画面上出现一条「它还在走」的路，而它一步都没动 —— 那是最容易骗人的一张图。
        var clock = new GameClock();
        var solver = SolverOf(Spec());
        var stepper = new NavStepper(
            solver,
            new NavMoveTuning { StepSeconds = StepSeconds, LookAheadMeters = 2.0, StepSolveExpansions = 20000 },
            clock,
            (string _, long _, double _) => new NavMoveCheckVerdict(false, "定身"));
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 15.0));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 7.5), 0.25));

        solver.BeginStep(20000);
        clock.Advance(1);
        stepper.CornersOf("甲").Count.ShouldBe(1, "激活检查拒了：本步没求解，交出脚下那一个点");
        stepper.CornersOf("甲")[0].ShouldBe(new NavPoint(0.5, 0.5));

        // 激活检查放行之后才有折线：交出的是那一步真算出来的那一条。
        var gateOpen = new GameClock();
        var solver2 = SolverOf(Spec());
        var stepper2 = new NavStepper(
            solver2,
            new NavMoveTuning { StepSeconds = StepSeconds, LookAheadMeters = 2.0, StepSolveExpansions = 20000 },
            gateOpen);
        stepper2.Register(Agent("甲", new NavPoint(0.5, 0.5), 15.0));
        stepper2.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 7.5), 0.25));
        solver2.BeginStep(20000);
        gateOpen.Advance(1);
        var corners = stepper2.CornersOf("甲");
        corners.Count.ShouldBeGreaterThan(1, "放行那一步有折线");
        corners[0].ShouldBe(new NavPoint(0.5, 0.5), "首位仍是步首位置");
        corners[^1].ShouldBe(new NavPoint(7.5, 7.5), "末位是目标");
    }

    // ---------- 没路那几步 ----------

    [Fact]
    public void 没路那几步交出那一条走不到目标的路径_末端不是目标点()
    {
        // 封一整排墙把场子劈成两半 ⇒ 求解交回的是一条**走不通**的路径。
        // 交出它本身（末端是它试到的最远那一格），而不是补成一条通到目标的线 ——
        // 调试视图要看的正是「它试了这么远」。
        var stage = new NavStage(OneUnit(id: "撞墙的", start: new NavPoint(1.5, 1.5),
            order: NavCommand.GoTo(new NavPoint(14.0, 14.0), 0.3)));
        for (var z = 0; z <= 15; z++)
        {
            stage.InjectReadout("StampTerrain", $"8|{z}|blocked|1");
        }

        for (var i = 0; i < 6; i++)
        {
            stage.AdvanceStep();
        }

        var solution = stage.Stepper.SolutionOf("撞墙的");
        solution.Stuck.ShouldBe(NavStuckKind.NoTerrainRoute, "墙把场子劈开了，走不过去");
        var corners = stage.Stepper.CornersOf("撞墙的");
        corners.Count.ShouldBe(3, "它试出来的那条折线有三个点（探针读数）");
        corners[0].ShouldBe(new NavPoint(1.8638034375544996, 2.9552137502179985),
            "复用这一档下实测值，逐步重解时是 (1.9550837716412082, 2.9285088195775835)");
        corners[1].ShouldBe(new NavPoint(3.5, 9.5),
            "复用这一档下手上那条失败路径是更早一步解出来的（求解起点不同，试到的那一段就不同）；"
            + "逐步重解时是 (4.5, 10.5)");
        corners[2].ShouldBe(new NavPoint(7.5, 13.5), "末端是它试到的最远那一格，**不是**目标点");
        corners[^1].ShouldNotBe(new NavPoint(14, 14), "交出的是那一条失败路径本身，不补全");
    }

    [Fact]
    public void 折线永不为空_连一步都没走的那几步也交出脚下那一点()
    {
        // 「折线永不为空」是第二期立的口径（`NavPath` 交回起点那一个点而不是空数组）：
        // 空数组会让上层拿它做「走到哪儿了」这类判断时前提被删掉。
        var stage = new NavStage(NavStageSample.Spec());
        for (var i = 0; i < 40; i++)
        {
            stage.AdvanceStep();
            foreach (var id in stage.Stepper.AgentIds)
            {
                stage.Stepper.CornersOf(id).ShouldNotBeEmpty($"第{i + 1}步 {id} 的折线不能是空的");
            }
        }
    }

    [Fact]
    public void 这个口不判定_它一个字都不改移动解与判定()
    {
        // 「只交出、不判定」的可观察后果：连着读两次折线得到同一个对象（不重算、不裁），
        // 而移动解那几列一个数都不动。
        var stage = new NavStage(NavStageSample.Spec());
        for (var i = 0; i < 5; i++)
        {
            stage.AdvanceStep();
        }

        var first = stage.Stepper.CornersOf(NavStageSample.RunnerId);
        var before = stage.Stepper.SolutionOf(NavStageSample.RunnerId).Describe();
        var second = stage.Stepper.CornersOf(NavStageSample.RunnerId);
        ReferenceEquals(first, second).ShouldBeTrue("同一个引用 —— 交出的是那一份，不是每次重算的新对象");
        stage.Stepper.SolutionOf(NavStageSample.RunnerId).Describe().ShouldBe(before, "读这个口不改任何一列读数");
    }

    [Fact]
    public void 单位没登记就当场抛_不返空()
    {
        var stage = new NavStage(NavStageSample.Spec());
        Should.Throw<KeyNotFoundException>(() => stage.Stepper.CornersOf("查无此单位"))
            .Message.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 视图层只读这个口_不许自己求解也不许反查缓存()
    {
        // 源码形状（视图层两个文件吃 Godot 类型，不进测试线）：
        // ① 出现 `.Solve(` / `NavSolver` / `NavGrid(` / `SetBlocked` / `Issue(` ⇒ 第二次计算；
        // ② 出现 `Cache` / `TryGet` ⇒ 用缓存反查（键含步首位置，视图拿不到那个数）；
        // ③ 必须出现 `CornersOf` —— 折线只从这一个口来。
        // 禁的记号是<b>精确</b>的（`NavPathCache` / `Cache.TryGet`），不是「文件里出现过 TryGet」——
        // 钝记号会撞上 `Dictionary.TryGetValue` 那一类无关写法，而假红会让人把守卫整个关掉。
        var code = string.Join("\n", NavStageTests.ViewCodeLines());
        code.ShouldContain("CornersOf", Case.Sensitive, "折线只从那个只读口来");
        foreach (var forbidden in new[]
                 {
                     ".Solve(", "NavSolver", "NavGrid(", "SetBlocked", "Issue(",
                     "NavPathCache", "Cache.TryGet",
                 })
        {
            code.ShouldNotContain(forbidden, Case.Sensitive,
                $"叠加层不许第二次计算、也不许拿缓存反查（{forbidden}）");
        }
    }

    // ---------- 工具 ----------

    /// <summary>点是否落在折线的某一段上（**独立的几何判法**，不复用被测那一份实现）。</summary>
    private static bool LiesOnPolyline(in NavPoint at, IReadOnlyList<NavPoint> corners)
    {
        const double tolerance = 1e-9;
        for (var i = 0; i + 1 < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[i + 1];
            var dx = b.X - a.X;
            var dz = b.Z - a.Z;
            var lengthSquared = (dx * dx) + (dz * dz);
            if (lengthSquared <= 0.0)
            {
                continue;
            }

            // 垂距：点到直线的距离（叉积除以段长）。
            var cross = (dx * (at.Z - a.Z)) - (dz * (at.X - a.X));
            if (Math.Abs(cross) / Math.Sqrt(lengthSquared) > tolerance)
            {
                continue;
            }

            // 投影落在段内。
            var t = (((at.X - a.X) * dx) + ((at.Z - a.Z) * dz)) / lengthSquared;
            if (t >= -tolerance && t <= 1.0 + tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static NavStageSpec OneUnit(string id, NavPoint start, NavCommand? order) => NavStageSample.Spec() with
    {
        Roster = new NavStageRoster
        {
            Units = new List<NavStageUnit>
            {
                new()
                {
                    Id = id, RadiusMeters = 0.2, MoveSpeedMetersPerSecond = 3.0,
                    Start = start, FacingDegrees = 0, OpeningOrder = order,
                },
            },
        },
    };

    private static NavGridSpec Spec() => new()
    {
        Space = new SpaceId("折线只读口"),
        CellMeters = 1.0,
        CellsX = 8,
        CellsZ = 8,
        OriginX = 0.0,
        OriginZ = 0.0,
        BlockedCells = Array.Empty<NavCell>(),
    };

    private static NavSolver SolverOf(NavGridSpec spec) => new(
        new NavSpace(spec.Space, new NavGrid(spec)),
        new NavSolveBudget { PerRequestExpansions = 4000 },
        new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
        cacheCapacity: 64,
        recordCapacity: 64,
        trackedRequestCapacity: 32);

    private static NavAgentSpec Agent(string id, NavPoint at, double speed) => new()
    {
        Id = id, RadiusMeters = 0.0, MoveSpeedMetersPerSecond = speed, Start = at, FacingDegrees = 0,
    };
}
