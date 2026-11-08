#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Tools;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 站位冲突、卡住三类与事件出口（工单 05）。
///
/// <para><b>逐字读数全是先跑探针读出来的</b>，再写成断言 —— 照工单 04 那期的纪律。
/// 探针写在同目录的 <c>NavSeparationProbe.cs</c>（跑完即删，不留在测试线里），
/// 它查出来的两件事写进了这一份用例：① 一趟推完<b>不是</b>「推到互不重叠」，
/// 甲乙被第二对推开之后它们俩仍然差 0.5 米；② 浮点噪声那一档（2.2e-16 的重叠）
/// 在读数上是真的会出现，所以断言给的是逐字而不是「约等于」。</para>
///
/// <para><b>断言强度</b>照「断言比快照」：比状态级读数（位置、位移、卡住类别、卡住步数、卡在谁身上、
/// 那一行事件文本）。<b>不拿事件流逐字相等当断言</b> —— 事件流逐字相等是重放那一档的事，
/// 这一份只钉「该发的那几条都发了、说法与步数分得开」。</para>
///
/// <para>「推完之后整张网格逐格读数不变」由用例里一份独立的查表判
/// （照第一、二期「真值由用例里一份独立的暴力计算给出」的纪律）。</para>
/// </summary>
public class NavSeparationTests
{
    private static readonly SpaceId Deck = new("巨兽甲板");

    private const double Cap = 0.000001;   // 只给本来就带浮点噪声的读数用

    private static NavGrid GridOf(IReadOnlyList<NavCell> blocked, int x = 8, int z = 8, double cell = 1.0)
        => new(new NavGridSpec
        {
            Space = Deck,
            CellMeters = cell,
            CellsX = x,
            CellsZ = z,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = blocked,
        });

    private static NavAgentProjection A(string id, NavPoint at, double radius, int group = 0, bool counted = true)
        => new() { Id = id, Group = group, Position = at, RadiusMeters = radius, CountedInSeparation = counted };

    private static NavSolver SolverOf(NavGrid grid, int budget = 4000)
        => new(
            new NavSpace(grid.Space, grid),
            new NavSolveBudget { PerRequestExpansions = budget },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);

    private static NavStepper StepperOf(
        NavSolver solver,
        GameClock clock,
        NavMoveEvents? events = null,
        NavSeparationTuning? separation = null,
        double lookAheadMeters = 2.0)
        => new(
            solver,
            new NavMoveTuning
            {
                StepSeconds = 0.1,                 // 「时间基准」：一律 0.1 秒
                LookAheadMeters = lookAheadMeters,
                StepSolveExpansions = 20000,
            },
            clock,
            gate: null,
            separation: separation,
            events: events);

    private static NavAgentSpec Agent(string id, NavPoint at, double speed, double radius = 0.0, int group = 0)
        => new()
        {
            Id = id,
            RadiusMeters = radius,
            MoveSpeedMetersPerSecond = speed,
            Start = at,
            FacingDegrees = 0.0,
            Group = group,
        };

    // ─────────────────── 站位冲突：叠加修正，叠在前视式跟随之上 ───────────────────

    [Fact]
    public void 三个单位重叠_按固定配对序推开_位置逐字()
    {
        // 8×8 空场、1 米一格。甲乙 0.4、丙 0.5，全员同组（缺省 0）。
        var grid = GridOf(Array.Empty<NavCell>());
        var agents = new[]
        {
            A("甲", new NavPoint(2.0, 2.0), 0.4),
            A("乙", new NavPoint(2.6, 2.0), 0.4),
            A("丙", new NavPoint(3.0, 2.0), 0.5),
        };

        var r = NavSeparation.Resolve(grid, agents);

        r.PairCount.ShouldBe(2, "甲乙重叠、丙乙重叠，甲丙不重叠（0.9 < 1.1 判否）");
        r.Pairs[0].Describe().ShouldBe("甲×乙 重叠 0.19999999999999996 沿连线各退一半");
        r.Pairs[1].Describe().ShouldBe("乙×丙 重叠 0.6000000000000002 沿连线各退一半");

        // 逐字落点。第 ② 对看到的是**第 ① 对推完之后**的乙（2.4），不是原始的 2.6。
        r.Positions[0].ShouldBe(new NavPoint(1.9, 2.0), "甲 2.0 − 0.1");
        r.Positions[1].ShouldBe(new NavPoint(2.4, 2.0), "乙 2.6 + 0.1（第一对），再 2.4 − 0.2（第二对）");
        r.Positions[2].X.ShouldBe(3.3000000000000003, "丙 3.0 + 0.30000000000000004：半份重叠 0.3000000000000001 是浮点噪声");

        r.PushedBy[0].ShouldBe("乙");
        r.PushedBy[1].ShouldBe("甲", "乙被两对推过，取配对序里第一对 —— 「卡在谁身上」要的是一个确定的 id");
        r.PushedBy[2].ShouldBe("乙");
    }

    [Fact]
    public void 一对推完立刻写回_先算完再统一落是另一组数()
    {
        // 钉的是次序语义本身：「一对推完立刻写回」与「先算完再统一落」在同一个输入上给出不同的数，
        // 于是这组逐字就是「用的是哪一种」的凭据（照退休前那份同侧推开的顺序语义）。
        var grid = GridOf(Array.Empty<NavCell>());
        var agents = new[]
        {
            A("甲", new NavPoint(2.0, 2.0), 0.4),
            A("乙", new NavPoint(2.6, 2.0), 0.4),
            A("丙", new NavPoint(3.0, 2.0), 0.5),
        };

        var r = NavSeparation.Resolve(grid, agents);

        // 统一落的话：第 ② 对按原始的 2.6 与 3.0 算 → 重叠 0.5 → 各退 0.25 → 乙 2.35、丙 3.25。
        var batchSecond = 2.6 + 0.25;
        var batchThird = 3.0 + 0.25;
        r.Positions[1].X.ShouldBe(2.4, "写回：第 ② 对按 2.4 与 3.0 算 → 重叠 0.6 → 各退 0.3 → 乙 2.4");
        r.Positions[1].X.ShouldNotBe(batchSecond, "统一落会给 2.35 —— 两个数不同，这一行就是次序的凭据");
        r.Positions[2].X.ShouldNotBe(batchThird, "统一落会给 3.25");
    }

    [Fact]
    public void 一趟推完不推到互不重叠_这是今天那套的形状()
    {
        // 甲乙被第二对推开之后仍然差 0.5 米（小于 0.8 的最小间距）。
        // **这不是 bug**：一趟扫完就交出结果是退休前那一份同侧推开的形状，
        // 下一条逻辑步的避让会接着把剩下的重叠清掉。断言把这个形状钉住，
        // 免得哪天有人「顺手」改成迭代到不重叠，工单 08 的零回归对读就分叉了。
        var grid = GridOf(Array.Empty<NavCell>());
        var agents = new[]
        {
            A("甲", new NavPoint(2.0, 2.0), 0.4),
            A("乙", new NavPoint(2.6, 2.0), 0.4),
            A("丙", new NavPoint(3.0, 2.0), 0.5),
        };

        var r = NavSeparation.Resolve(grid, agents);
        var gap = r.Positions[0].DistanceTo(r.Positions[1]);
        gap.ShouldBe(0.5, Cap, "0.4 + 0.4 = 0.8 的最小间距，这一步之后仍然差 0.5 米");
        (gap < 0.8).ShouldBeTrue("所以这一趟并没有推到互不重叠 —— 一趟是一趟");
    }

    [Fact]
    public void 完全重合_沿第一根轴按序号一前一后拆开()
    {
        // 距离近乎零时 delta 是 0/0，走方向会得到 NaN。
        // 口径照退休前那份同侧推开：沿第一根轴（X）按序号一前一后，各退一半。
        var grid = GridOf(Array.Empty<NavCell>());
        var r = NavSeparation.Resolve(grid, new[]
        {
            A("甲", new NavPoint(3.0, 3.0), 0.4),
            A("乙", new NavPoint(3.0, 3.0), 0.4),
        });

        r.PairCount.ShouldBe(1);
        r.Pairs[0].Coincident.ShouldBeTrue("距离 0 读的是重合那一支");
        r.Pairs[0].OverlapMeters.ShouldBe(0.8, Cap, "重叠 = 两者半径之和 0.8");
        r.Positions[0].ShouldBe(new NavPoint(2.6, 3.0), "序号小的退到前一个（3.0 − 0.4）");
        r.Positions[1].ShouldBe(new NavPoint(3.4, 3.0), "序号大的退到后一个（3.0 + 0.4）");
    }

    [Fact]
    public void 不同组之间不推_组号是请求递进来的标量()
    {
        var grid = GridOf(Array.Empty<NavCell>());
        var r = NavSeparation.Resolve(grid, new[]
        {
            A("甲", new NavPoint(2.0, 2.0), 0.4, group: 0),
            A("乙", new NavPoint(2.2, 2.0), 0.4, group: 1),
        });

        r.PairCount.ShouldBe(0, "不同组不推：0.4 + 0.4 = 0.8 的最小间距，0.2 的间距本该重叠");
        r.Positions[0].ShouldBe(new NavPoint(2.0, 2.0));
        r.Positions[1].ShouldBe(new NavPoint(2.2, 2.0));
        r.PushedBy[0].ShouldBeNull();
        r.PushedBy[1].ShouldBeNull();
    }

    [Fact]
    public void 距离正好等于最小间距就不推()
    {
        // 线上算到：重叠量 0 判否，于是「刚好挨着」是稳的（再推 1 纳米就会让它们分开，
        // 而「分开」与「挨着」在读数上是两件事）。这里用 0.25 + 0.25 与 0.5 的间距 ——
        // 都是二进制精确值，所以「等于」是真的等于（下一条用例钉另一种情况）。
        var grid = GridOf(Array.Empty<NavCell>());
        var r = NavSeparation.Resolve(grid, new[]
        {
            A("甲", new NavPoint(2.0, 2.0), 0.25),
            A("乙", new NavPoint(2.5, 2.0), 0.25),
        });

        r.PairCount.ShouldBe(0);
        r.Positions[0].ShouldBe(new NavPoint(2.0, 2.0));
        r.Positions[1].ShouldBe(new NavPoint(2.5, 2.0));
    }

    [Fact]
    public void 差一个浮点噪声就推_读数是那一点点重叠()
    {
        // 探针查出来的：2.8 − 2.0 在 double 里是 0.7999999999999998，比 0.8 的最小间距小 2.2e-16，
        // 于是判「重叠」并推开 1.1e-16 米。**这不是 bug**，是这一层用 double 的必然结果；
        // 钉住它是因为它决定了「刚好挨着」这件事在读数上不是恒定的 ——
        // 流水线上那一对会因此每隔几步抖一下卡住步数（用例 拥挤的步数是连续几步 里读得到）。
        var grid = GridOf(Array.Empty<NavCell>());
        var r = NavSeparation.Resolve(grid, new[]
        {
            A("甲", new NavPoint(2.0, 2.0), 0.4),
            A("乙", new NavPoint(2.8, 2.0), 0.4),
        });

        r.PairCount.ShouldBe(1);
        r.Pairs[0].OverlapMeters.ShouldBe(2.220446049250313E-16, 0.0, "0.8 − 0.7999999999999998");
        r.Positions[0].X.ShouldBe(2.0, "推开的那 1.11e-16 米在 2.0 的量级上被舍掉了：位置逐字不动，配对读数上却有那一对");
        r.Positions[1].X.ShouldBe(2.8);
    }

    [Fact]
    public void 贴边被推出去那一半落不成_边界即墙()
    {
        // 甲在 x = 0.2，被乙（0.5）往外推 0.25 → 候选落点 −0.05 出界。
        // 那一半落不成（NavGrid.Contains 是边界唯一那一份判读），乙照推。
        var grid = GridOf(Array.Empty<NavCell>());
        var r = NavSeparation.Resolve(grid, new[]
        {
            A("甲", new NavPoint(0.2, 4.0), 0.4),
            A("乙", new NavPoint(0.5, 4.0), 0.4),
        });

        r.PairCount.ShouldBe(1);
        r.Pairs[0].HeldByBoundary.ShouldBeTrue("这一对有一侧的候选落点出界");
        r.Pairs[0].Describe().ShouldBe("甲×乙 重叠 0.5 沿连线各退一半 一侧撞界没挪");
        r.Positions[0].ShouldBe(new NavPoint(0.2, 4.0), "出界那一半不挪：层里不另写一份矩形夹取");
        r.Positions[1].ShouldBe(new NavPoint(0.75, 4.0), "没出界那一侧照推：0.5 + 0.25");
    }

    [Fact]
    public void 推完之后整张网格逐格读数不变_站位冲突不进网格()
    {
        // 这是工单 05 那条验收的**运行时**那一半：把两对完全重合的单位推一遍，
        // 整张网格的阻挡 / 通过成本 / 动态乘数 / 净空逐格读数与两个版本号一个都不许动。
        // 查的是网格自己那份表，不复用被测实现那条读法。
        var grid = GridOf(new[] { new NavCell(2, 2) });
        var agents = new[]
        {
            A("甲", new NavPoint(3.0, 3.0), 0.4),
            A("乙", new NavPoint(3.0, 3.0), 0.4),
            A("丙", new NavPoint(3.0, 3.0), 0.5),
        };

        var before = Fingerprint(grid);
        var r = NavSeparation.Resolve(grid, agents);
        var after = Fingerprint(grid);

        r.PairCount.ShouldBe(3, "三个单位互相完全重合：三对全推（先证这一趟真的动了位置）");
        after.ShouldBe(before,
            "寻路网格里不许出现任何 agent 相关的格：把两个单位互相推开翻译成网格是错位（spec / ADR 0008）。"
            + "静态版本一动，在手路径就按版本作废 —— 「有人在推挤」与「有人在砌墙」在缓存那条路上就分不开了。");
    }

    [Fact]
    public void 身体半径与组号是标量_层里没有单位表()
    {
        // 「寻路单入口」 后半句：寻路层不持有单位表。用反射钉住投影的形状 ——
        // 五列，一个不多；站着不动的单位表就是从第六列开始的。
        var columns = typeof(NavAgentProjection)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .ToArray();
        columns.ShouldBe(
            new[] { "Id:String", "Group:Int32", "Position:NavPoint", "RadiusMeters:Double", "CountedInSeparation:Boolean" },
            "投影就这五列：id、一个标量组号、位置、身体半径，外加「这一趟算不算它」。血量 / 射程 / 冷却 / 兵种这些词一个都不该出现");

        // 避让那一档是纯函数：它不持有任何东西，只有一个常量与一个入口。
        var members = typeof(NavSeparation)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        members.ShouldBe(
            new[] { "CoincidentEpsilonMeters", "Resolve" },
            "一个写死的重合门限 + 一个入口：没有状态、没有单位表、没有第二份真值");
    }

    [Fact]
    public void 推完不改调用方那份投影()
    {
        var grid = GridOf(Array.Empty<NavCell>());
        var agents = new[]
        {
            A("甲", new NavPoint(2.0, 2.0), 0.4),
            A("乙", new NavPoint(2.5, 2.0), 0.4),
        };

        var r = NavSeparation.Resolve(grid, agents);

        agents[0].Position.ShouldBe(new NavPoint(2.0, 2.0), "位置那一份真值住在流水线上，写回是流水线的活");
        agents[1].Position.ShouldBe(new NavPoint(2.5, 2.0));
        r.Positions[0].ShouldBe(new NavPoint(1.85, 2.0), "0.8 的最小间距、0.5 的间距 → 重叠 0.3 → 各退 0.15");
        r.Positions[1].ShouldBe(new NavPoint(2.65, 2.0));
    }

    [Fact]
    public void 缺参数当场抛_不许兜零半径()
    {
        var grid = GridOf(Array.Empty<NavCell>());

        Should.Throw<ArgumentOutOfRangeException>(
            () => NavSeparation.Resolve(grid, new[] { A("", new NavPoint(2.0, 2.0), 0.4) }))
            .Message.ShouldContain("空白", Case.Sensitive, "id 空白落进账本就是一行没有主体的记录");
        Should.Throw<ArgumentOutOfRangeException>(
            () => NavSeparation.Resolve(grid, new[] { A("甲", new NavPoint(2.0, 2.0), -0.1) }))
            .Message.ShouldContain("半径", Case.Sensitive, "负半径让最小间距算成「两人互相躲」，那是一条恒真的重叠");
        Should.Throw<ArgumentOutOfRangeException>(
            () => NavSeparation.Resolve(grid, new[] { A("甲", new NavPoint(double.NaN, 2.0), 0.4) }))
            .Message.ShouldContain("有限", Case.Sensitive, "非有限坐标让每一处距离判定都退化成一条恒值读数");
        Should.Throw<ArgumentNullException>(() => NavSeparation.Resolve(null!, Array.Empty<NavAgentProjection>()));
    }

    // ─────────────────── 卡住三类：各带步数，拥挤那一类读得出卡在谁身上 ───────────────────

    [Fact]
    public void 拥挤推挤读得出卡在谁身上()
    {
        // 两个 0.4 半径的单位一前一后朝同一个点走（0.5 米每秒 ⇒ 每步 0.05 米），
        // 起点间距 0.5 < 0.8 的最小间距 ⇒ 每一步都被对方推开。阈值递 0.2 米。
        var clock = new GameClock();
        var grid = GridOf(Array.Empty<NavCell>());
        var stepper = StepperOf(
            SolverOf(grid),
            clock,
            new NavMoveEvents(64),
            new NavSeparationTuning { MinNetStepMeters = 0.2 });
        stepper.Register(Agent("甲", new NavPoint(2.0, 2.0), 0.5, radius: 0.4));
        stepper.Register(Agent("乙", new NavPoint(2.5, 2.0), 0.5, radius: 0.4));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 2.0), 0.1));
        stepper.Issue("乙", NavCommand.GoTo(new NavPoint(7.5, 2.0), 0.1));

        clock.Advance(1);

        var a = stepper.SolutionOf("甲");
        a.Stuck.ShouldBe(NavStuckKind.Crowded, "有路、自己在走（位移 0.05），但净位移被推到 0.1");
        a.StuckSteps.ShouldBe(1);
        a.StuckOn.ShouldBe("乙", "「挤住了」不说出是谁挤的，等于只说「它不动了」");
        a.Position.ShouldBe(new NavPoint(1.9, 2.0), "自己走了 0.05（2.05），被推开 0.15 → 1.9");
        a.MovedMeters.ShouldBe(0.10000000000000009, 0.0, "位移那一列读的是**净位移**：本步开头到推完之后的落点");

        var b = stepper.SolutionOf("乙");
        b.Stuck.ShouldBe(NavStuckKind.Crowded);
        b.StuckOn.ShouldBe("甲");
        b.Position.ShouldBe(new NavPoint(2.6999999999999997, 2.0));
        b.MovedMeters.ShouldBe(0.19999999999999973, 0.0);

        // 「卡在」那一列只在拥挤那一档出现：其余三档没有这个事实，
        // 写一列「无」等于让「没有这一档」与「这一档有但没卡住」读出同一句话。
        // 前视点逐字 (4, 2)：第 1 步的位置还是 2.0，前视距离 2.0 米落在 4.0 上（推开的落点是 1.9）。
        a.Describe().ShouldBe(
            "甲 GoTo 位置 (1.9, 2) 朝向 90 速度 1.0000000000000009 位移 0.10000000000000009 "
            + "状态 Moving 卡住 Crowded/1 子步 1 目标 (7.5, 2) 前视 (4, 2) 激活检查 无 卡在 乙 身上");
    }

    [Fact]
    public void 拥挤的步数是连续几步_不挤了就归零()
    {
        var clock = new GameClock();
        var grid = GridOf(Array.Empty<NavCell>());
        var stepper = StepperOf(
            SolverOf(grid),
            clock,
            new NavMoveEvents(64),
            new NavSeparationTuning { MinNetStepMeters = 0.2 });
        stepper.Register(Agent("甲", new NavPoint(2.0, 2.0), 0.5, radius: 0.4));
        stepper.Register(Agent("乙", new NavPoint(2.5, 2.0), 0.5, radius: 0.4));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 2.0), 0.1));
        stepper.Issue("乙", NavCommand.GoTo(new NavPoint(7.5, 2.0), 0.1));

        var readings = new List<(NavStuckKind Stuck, int Steps, string? On)>();
        for (var i = 1; i <= 5; i++)
        {
            clock.Advance(1);
            var s = stepper.SolutionOf("甲");
            readings.Add((s.Stuck, s.StuckSteps, s.StuckOn));
        }

        // 第 3 步两者恰好散到 0.8（最小间距，线上算到不推）⇒ 那一类断了，卡住步数归零；
        // 第 4 步又被推上 ⇒ 归 1，不是接着数。
        readings.ShouldBe(new[]
        {
            (NavStuckKind.Crowded, 1, "乙"),
            (NavStuckKind.Crowded, 2, "乙"),
            (NavStuckKind.None, 0, null),
            (NavStuckKind.Crowded, 1, "乙"),
            (NavStuckKind.Crowded, 2, "乙"),
        });
    }

    [Fact]
    public void 预算触顶_回部分路径加原因那一档()
    {
        // 16×16 空场、每请求 5 次扩展 ⇒ 这一条被预算截断（Partial/BudgetExceeded）。
        // 处置是「回部分路径而不是空」：单位照着部分路径走，卡住类别读「预算触顶」。
        var clock = new GameClock();
        var grid = GridOf(Array.Empty<NavCell>(), 16, 16);
        var events = new NavMoveEvents(64);
        var stepper = StepperOf(SolverOf(grid, budget: 5), clock, events);
        stepper.Register(Agent("甲", new NavPoint(0.5, 0.5), 1.5));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(15.5, 15.5), 0.25));

        clock.Advance(1);

        var s = stepper.SolutionOf("甲");
        s.Stuck.ShouldBe(NavStuckKind.BudgetCapped, "地形通、也在走，是预算把这一条截断了 —— 与地形无路分得开");
        s.StuckSteps.ShouldBe(1);
        s.StuckOn.ShouldBeNull("预算触顶没有「卡在谁身上」这个事实：没有人在挤它");
        s.Status.ShouldBe(NavMoveStatus.Moving, "部分路径照走（交回空数组等于把「走不到位」这个可读事实删掉）");
        s.Describe().ShouldContain("卡住 BudgetCapped/1");

        var rows = events.Read(10).Rows;
        rows.Count.ShouldBe(2, "预算触顶这一步发两条：部分路径 + 卡住（预算触顶）。漏一条就等于那一档少了一个读数");
        rows[0].Describe().ShouldBe("#1 PartialPath 甲 第1步 部分路径 BudgetExceeded 扩展 5 走到 (4.5, 4.5)");
        rows[1].Describe().ShouldBe("#2 Stuck 甲 第1步 卡住 预算触顶/1");
    }

    [Fact]
    public void 地形无路那一档压过拥挤()
    {
        // 甲被墙堵在原地（子步撞墙 → 地形无路），乙紧贴着它。
        // 撞墙与被推是两件事，排查方向完全相反：撞墙要改地形或改命令，被推要改站位。
        var clock = new GameClock();
        var blocked = new List<NavCell>();
        for (var x = 0; x <= 6; x++)
        {
            blocked.Add(new NavCell(x, 7));   // z = 7 一整排墙，x = 7 留缺口
        }

        var grid = GridOf(blocked, 8, 16);
        var stepper = StepperOf(
            SolverOf(grid),
            clock,
            new NavMoveEvents(64),
            new NavSeparationTuning { MinNetStepMeters = 0.2 });
        stepper.Register(Agent("甲", new NavPoint(0.5, 7.5), 1.5, radius: 0.2));
        stepper.Register(Agent("乙", new NavPoint(0.9, 7.5), 1.5, radius: 0.2));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(0.5, 14.5), 0.25));
        stepper.Issue("乙", NavCommand.GoTo(new NavPoint(0.5, 14.5), 0.25));

        for (var i = 0; i < 5; i++)
        {
            clock.Advance(1);
        }

        var a = stepper.SolutionOf("甲");
        a.Stuck.ShouldBe(NavStuckKind.NoTerrainRoute, "撞墙那一档压过拥挤");
        a.StuckSteps.ShouldBe(5, "卡住步数 = 这一类连续出现了几步");
        a.StuckOn.ShouldBeNull("地形无路没有「卡在谁身上」这个事实");
    }

    [Fact]
    public void 卡住类别是那三类加一个无()
    {
        // 钉的是「不许用 0 / -1 那种占位数」：三类都在，外加一个「无」，
        // 每一步要么说「无」要么说那三类之一。混成一个 Stuck 就把定位打回出图肉眼看（「约束唯一出处」）。
        Enum.GetValues<NavStuckKind>().ShouldBe(new[]
        {
            NavStuckKind.None,
            NavStuckKind.NoTerrainRoute,
            NavStuckKind.Crowded,
            NavStuckKind.BudgetCapped,
        });
    }

    [Fact]
    public void 三类在检查点快照里分得开()
    {
        // 快照里那一列读的是中文说法，三类各有各的名字，没有「其他」那一档。
        var rows = new List<string>();
        foreach (var kind in Enum.GetValues<NavStuckKind>())
        {
            rows.Add(TextOf(kind));
        }

        // 读不出是哪一个的类别等于没有类别（「桥只转发」：不许兜一个说不清的默认值）。
        rows.ShouldBe(new[] { "无", "地形无路", "拥挤推挤", "预算触顶" });
    }

    // ─────────────────── 事件出口：四条都发，无一静默 ───────────────────

    [Fact]
    public void 四条事件都发_无一静默()
    {
        // 一个走到底、一个撞墙、一个被预算截断、一个被挤住：四档事件各发一次。
        var clock = new GameClock();
        var events = new NavMoveEvents(64);
        var grid = GridOf(new[] { new NavCell(2, 0) });   // 一格阻挡 = 子步撞墙那一档
        var stepper = StepperOf(
            SolverOf(grid, budget: 1),
            clock,
            events,
            new NavSeparationTuning { MinNetStepMeters = 0.2 });

        stepper.Register(Agent("到位", new NavPoint(1.0, 1.0), 1.5));
        stepper.Register(Agent("撞墙", new NavPoint(0.5, 0.5), 1.5, radius: 0.1));
        stepper.Register(Agent("被挤", new NavPoint(5.0, 3.0), 0.5, radius: 0.4));
        stepper.Register(Agent("同袍", new NavPoint(5.5, 3.0), 0.5, radius: 0.4));
        stepper.Issue("到位", NavCommand.GoTo(new NavPoint(1.05, 1.0), 0.1));
        stepper.Issue("撞墙", NavCommand.GoTo(new NavPoint(2.5, 0.5), 0.25));   // 目标落进 (2,0) 那格地形阻挡
        stepper.Issue("被挤", NavCommand.GoTo(new NavPoint(7.5, 3.0), 0.1));
        stepper.Issue("同袍", NavCommand.GoTo(new NavPoint(7.5, 3.0), 0.1));

        clock.Advance(1);

        var rows = events.Read(64);
        rows.Rows.Select(r => r.Kind).Distinct().OrderBy(k => (int)k).ToArray()
            .ShouldBe(new[]
            {
                NavMoveEventKind.Arrived,
                NavMoveEventKind.PartialPath,
                NavMoveEventKind.Recompute,
                NavMoveEventKind.Stuck,
            },
                "到达 / 部分路径 / 撞墙重算 / 卡住，四条都发。漏一条就等于那一类情况在读数上不存在（「事件发射前置」的静默拒绝教训）");

        rows.Rows.ShouldAllBe(r => r.Text.Length > 0);
        rows.Rows.ShouldContain(r => r.Kind == NavMoveEventKind.Recompute && r.Text.Contains("目标进不了可行域", StringComparison.Ordinal),
            "撞墙那一档的说法要指名是哪一条地形原因，不是一句「撞墙了」");
        TestLog.Ledger("四档事件 " + rows.Total + " 条：" + string.Join(" | ", rows.Rows.Select(r => r.Describe())));
    }

    [Fact]
    public void 卡住事件带卡在谁身上_说法逐字()
    {
        var clock = new GameClock();
        var events = new NavMoveEvents(64);
        var grid = GridOf(Array.Empty<NavCell>());
        var stepper = StepperOf(
            SolverOf(grid), clock, events, new NavSeparationTuning { MinNetStepMeters = 0.2 });
        stepper.Register(Agent("甲", new NavPoint(2.0, 2.0), 0.5, radius: 0.4));
        stepper.Register(Agent("乙", new NavPoint(2.5, 2.0), 0.5, radius: 0.4));
        stepper.Issue("甲", NavCommand.GoTo(new NavPoint(7.5, 2.0), 0.1));
        stepper.Issue("乙", NavCommand.GoTo(new NavPoint(7.5, 2.0), 0.1));

        NavStuckEvent? seen = null;
        events.Stuck.Published += e =>
        {
            if (e.AgentId == "甲")
            {
                seen = e;
            }
        };

        clock.Advance(1);

        seen.ShouldNotBeNull("强类型通道与账本落的是同一件事：只落一处就有一半的读数是空的");
        seen!.Value.Stuck.ShouldBe(NavStuckKind.Crowded);
        seen.Value.StuckOn.ShouldBe("乙");
        seen.Value.StepIndex.ShouldBe(1);
        events.Ledger.At(0).Describe().ShouldBe("#1 Stuck 甲 第1步 卡住 拥挤推挤/1 卡在 乙 身上",
            "按写入次序：甲是登记次序里的第一个（第 1 条），乙是第 2 条");
    }

    [Fact]
    public void 事件账本有界_读取带丢弃数()
    {
        // 容量 3、发 7 条 ⇒ 丢 4。「账本里没有」与「没发生」只有在 Dropped == 0 时才是同一件事（「三条铁律」 ③）。
        var events = new NavMoveEvents(3);
        for (var i = 1; i <= 7; i++)
        {
            events.EmitStuck(new NavStuckEvent("甲", i, NavStuckKind.Crowded, i, "乙"));
        }

        var read = events.Read(10);
        read.Dropped.ShouldBe(4, "环形保留最近 3 条，被顶掉 4 条");
        read.Total.ShouldBe(7);
        read.Rows.Count.ShouldBe(3);
        read.Rows[0].Sequence.ShouldBe(5, "留的是最近那三条，序号跨步连续（第几步之后开始不对是一条读数）");
        read.Rows[2].Sequence.ShouldBe(7);
        read.Rows[2].Describe().ShouldBe("#7 Stuck 甲 第7步 卡住 拥挤推挤/7 卡在 乙 身上");
    }

    [Fact]
    public void 求解记录也带丢弃数_两本账都读得出丢过()
    {
        // 求解记录那一本在第二期就有丢弃数（NavSolveLedger.Dropped），这一条钉住两本一起读。
        var grid = GridOf(Array.Empty<NavCell>(), 16, 16);
        var solver = new NavSolver(
            new NavSpace(Deck, grid),
            new NavSolveBudget { PerRequestExpansions = 1 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            cacheCapacity: 4,
            recordCapacity: 3,
            trackedRequestCapacity: 8);

        solver.BeginStep(20000);
        for (var i = 0; i < 7; i++)
        {
            solver.Solve(new NavPoint(0.5 + i, 0.5), new NavPoint(15.5, 15.5), 0.0);
        }

        solver.Records.Count.ShouldBe(3, "容量 3");
        solver.Records.Dropped.ShouldBe(4, "丢了 4 条 —— 不带这一列，「账本里没有」会被读成「没发生」");
        solver.Records.At(0).Sequence.ShouldBe(5);
    }

    [Fact]
    public void 事件账本下标越界当场抛_容量零当场抛()
    {
        var ledger = new NavEventLedger(2);
        ledger.Count.ShouldBe(0);
        ledger.Dropped.ShouldBe(0);
        ledger.Last.ShouldBeNull("「还没发生过」是一个正常状态，不抛");
        Should.Throw<ArgumentOutOfRangeException>(() => ledger.At(0))
            .Message.ShouldContain("越界", Case.Sensitive, "越界是调用方的错，不是一个该被兜住的读数");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavEventLedger(0))
            .Message.ShouldContain("容量", Case.Sensitive, "容量 0 的账本记不下任何一行");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavMoveEvents(0).Read(0));
    }

    // ─────────────────── 守卫 ④：寻路网格里不许出现任何 agent 相关的格 ───────────────────

    [Fact]
    public void 真实源码里没有把agent推进网格的那一行()
    {
        var src = Path.Combine(TestPaths.RepoRoot, "src");
        var hits = NavSourceGuard.Scan(src).Where(h => h.Rule == NavSourceGuard.AgentInGridRule).ToList();
        foreach (var hit in hits)
        {
            TestLog.Ledger("  " + NavSourceGuard.Describe(hit));
        }

        hits.ShouldBeEmpty(
            "守卫 ④（测试文件不参与这一条：它要按名字写出违规样本才能证判据抓得住）：网格是采样场，成对相对查询是解析量 —— 把两个单位互相推开翻译成网格是错位（ADR 0008）。\n"
            + "站位冲突走 NavSeparation（叠加修正），不写网格的三个写入口。");
    }

    [Fact]
    public void 临时违规树证它抓得住_把推开翻译成网格判红()
    {
        // 先证它抓得住：当场造一棵违规树，里面那一行正是「有人挡住了」的错误翻译。
        // 样本落在 .cache/ 下、用完删掉（落在仓库里的违规 .cs 会被 SDK 通配编进测试工程）。
        var root = Artifacts.Ensure(TestPaths.RepoRoot, "Pathfinding", "AgentInGridProbe");
        var file = Path.Combine(root, "Prototypes", "Probe", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, new[]
        {
            "#nullable enable",
            "namespace Probe;",
            "public sealed class Probe",
            "{",
            "    private readonly NavGrid _grid = null!;",
            "    public void SeparateSameSide(NavAgentProjection a, NavAgentProjection b)",
            "    {",
            "        var cell = new NavCell((int)a.Position.X, (int)a.Position.Z);",
            "        _grid.SetDynamicCost(new NavCell((int)b.Position.X, (int)b.Position.Z), 5.0);",
            "    }",
            "}",
        });

        try
        {
            var hits = NavSourceGuard.Scan(root);
            foreach (var hit in hits)
            {
                TestLog.Ledger("  " + NavSourceGuard.Describe(hit));
            }

            var rule4 = hits.Where(h => h.Rule == NavSourceGuard.AgentInGridRule).ToList();
            rule4.Count.ShouldBe(2, "造格那一行与写入口那一行各判红一处（信号是「一行里两侧都出现」）");
            rule4.ShouldAllBe(h => h.ExemptionWhy == null, "样本在临时树里、不在豁免名单的路径上");
            rule4.ShouldContain(h => h.Symbol.Contains("SetDynamicCost", StringComparison.Ordinal));
            rule4.ShouldContain(h => h.Symbol.Contains("new NavCell", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 规则四的说明不判自己()
    {
        // 守卫 ④ 的判红条件是「一行里两侧都出现」，而它的说明里必然两侧都出现 ——
        // 整份说明不判，否则这一条规则会永远判自己（假红是守卫最容易死掉的方式）。
        var layer = Path.Combine(TestPaths.RepoRoot, "src", "GugaFramework", "Pathfinding");
        var hits = NavSourceGuard.Scan(layer).Where(h => h.Rule == NavSourceGuard.AgentInGridRule).ToList();
        hits.ShouldBeEmpty("层自己的源码（包括这一条规则的说明）不许被自己判红");
    }

    // ─────────────────── 小工具 ───────────────────

    /// <summary>
    /// 整张网格的指纹：逐格的阻挡 / 静态成本 / 动态乘数 / 净空 + 两个版本号。
    /// 站位冲突跑完之后它必须一字不差 —— 这是工单 05 那条验收的运行时那一半。
    /// </summary>
    private static string Fingerprint(NavGrid grid)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("版本 ").Append(grid.StaticVersion).Append('/').Append(grid.DynamicVersion).Append('\n');
        for (var z = 0; z < grid.CellsZ; z++)
        {
            for (var x = 0; x < grid.CellsX; x++)
            {
                var cell = new NavCell(x, z);
                sb.Append(x).Append(',').Append(z)
                    .Append(' ').Append(grid.IsBlocked(cell) ? "挡" : "通")
                    .Append(' ').Append(grid.PassCostAt(cell).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(' ').Append(grid.DynamicCostAt(cell).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(' ').Append(grid.Clearance.At(x, z).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>快照里卡住那一列的中文说法（照 <c>NavStepper.StuckText</c> 那一份口径）。</summary>
    private static string TextOf(NavStuckKind kind) => kind switch
    {
        NavStuckKind.None => "无",
        NavStuckKind.NoTerrainRoute => "地形无路",
        NavStuckKind.Crowded => "拥挤推挤",
        NavStuckKind.BudgetCapped => "预算触顶",
        _ => "无",
    };
}
