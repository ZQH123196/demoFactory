#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace DemoFactory.Prototypes.BattleArray.Tests;

/// <summary>
/// 工单 08：排兵布阵接寻路层的<b>走位那一半</b> —— 三个命令（原地不动 / 追实体 / 站位冲突）、
/// 技能层激活检查排在上游的调用顺序、「定身期间不位移」，以及退休之后那三个口
/// （落位移 / 每步的移速标量 / 这一趟算不算它）在真世界里的落点。
///
/// <para><b>手写实现已退休</b>：<c>BattleWorld.StepToward</c> 与 <c>SeparateSameSide</c> 删掉了
/// （用户裁决：接受末位差、退休旧路 float）。判据随之从「新旧逐字相同」改成
/// <b>「判定没被偷换」</b> —— <see cref="判定没被偷换_射程与进射程那一步逐步相等"/> 把
/// 射程、够不够得着、首次进射程那一步、翻转次数逐项钉住（那些数旧路新路一致），
/// 位置末位差不钉（它就是这次要改的那一格）。
/// 末位差在真战场上会放大：15v15 那一局从 27.5 秒 / 敌方亡 12 变成 31.1 秒 / 敌方亡 14，
/// 实测见工单 08 与 <c>docs/GugaFramework/寻路层.md</c> 7 节。</para>
///
/// <para><b>断言强度只比检查点快照</b>（「断言比快照」）。逐字读数先跑探针读出来再写成断言。</para>
/// </summary>
public class BattleNavMoveTests
{
    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // === 激活检查调用顺序（工单 08 验收项：钉死 + 有用例）===

    [Fact]
    public void 激活检查顺序_先问允不允许动再算站哪_激活检查看到的是标称步长()
    {
        // 探针读数：激活检查收到的是「打算走 0.15 米」这一档（移速 1.5 × 0.1 秒），
        // 而不是求解之后才知道的「沿路径还剩多远」—— 排在求解之前正是契约（ADR 0008、「桥只转发」）。
        var grid = BattleNavGrid.Build();
        var clock = new GameClock();
        var seen = new List<(string Id, long Step, double Meters)>();
        var solver = new NavSolver(
            new NavSpace(BattleNavGrid.Space, grid),
            new NavSolveBudget { PerRequestExpansions = 4000 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            64, 64, 32);

        var stepper = new NavStepper(
            solver,
            new NavMoveTuning
            {
                StepSeconds = BattleContract.StepSeconds,
                LookAheadMeters = BattleNavMove.LookAheadMeters,
                StepSolveExpansions = BattleNavMove.StepSolveExpansions,
            },
            clock,
            (agentId, stepIndex, plannedMeters) =>
            {
                seen.Add((agentId, stepIndex, plannedMeters));
                return NavMoveCheckVerdict.Allow;
            });

        stepper.Register(new NavAgentSpec
        {
            Id = "p01",
            RadiusMeters = BattleNavGrid.RadiusOf(TroopKind.Warrior),
            MoveSpeedMetersPerSecond = BattleNavGrid.SpeedOf(TroopKind.Warrior),
            Start = BattleNavGrid.ToGrid(0.0f, 7.0f),
            FacingDegrees = 0.0,
        });
        stepper.Issue("p01", NavCommand.Chase("e01", BattleNavGrid.ToGrid(0.0f, -7.0f), 0.0));
        clock.Advance(1);

        seen.Count.ShouldBe(1, "激活检查每步每个单位问一次");
        seen[0].Id.ShouldBe("p01", "激活检查拿到的是宿主递进来的单位 id");
        seen[0].Step.ShouldBe(1L, "激活检查拿到的是第几个逻辑步（从 1 起）");
        seen[0].Meters.ShouldBe(0.15000000223517418, 1e-12,
            "激活检查看到的是<b>标称步长</b>（移速 1.5 × 0.1f 秒；StepSeconds 是 float 常量 0.1f，"
            + "升成 double 带出 0.10000000149011612 这一档残差，照实钉住），不是沿路径剩余 —— "
            + "后者要等求解之后才知道，而激活检查排在求解之前");

        // 激活检查放行 ⇒ 真的走了。
        stepper.SolutionOf("p01").Status.ShouldBe(NavMoveStatus.Moving, "放行之后这一档要产位移");
    }

    [Fact]
    public void 激活检查顺序_没挂技能层时全放行_激活检查递的是null()
    {
        // 全关档（Abilities = null）⇒ null 激活检查 ⇒ 层里全放行。
        // 这一档的意义：接了寻路层之后，「没挂技能栏」的那一局演化与快照不许变一个字节。
        BattleNavMove.BuildGate(null).ShouldBeNull("没挂技能层就不递激活检查（层里 null = 全放行）");

        var grid = BattleNavGrid.Build();
        var clock = new GameClock();
        var solver = new NavSolver(
            new NavSpace(BattleNavGrid.Space, grid),
            new NavSolveBudget { PerRequestExpansions = 4000 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            64, 64, 32);
        var stepper = new NavStepper(
            solver,
            new NavMoveTuning
            {
                StepSeconds = BattleContract.StepSeconds,
                LookAheadMeters = BattleNavMove.LookAheadMeters,
                StepSolveExpansions = BattleNavMove.StepSolveExpansions,
            },
            clock,
            BattleNavMove.BuildGate(null));

        stepper.Register(new NavAgentSpec
        {
            Id = "p01",
            RadiusMeters = BattleNavGrid.RadiusOf(TroopKind.Warrior),
            MoveSpeedMetersPerSecond = BattleNavGrid.SpeedOf(TroopKind.Warrior),
            Start = BattleNavGrid.ToGrid(0.0f, 7.0f),
            FacingDegrees = 0.0,
        });
        stepper.Issue("p01", NavCommand.Chase("e01", BattleNavGrid.ToGrid(0.0f, -7.0f), 0.0));
        clock.Advance(1);

        stepper.SolutionOf("p01").CheckReason.ShouldBe("无", "全放行时激活检查那一列是「无」");
        stepper.SolutionOf("p01").Status.ShouldBe(NavMoveStatus.Moving, "全放行 ⇒ 照走");
    }

    [Fact]
    public void 定身期间不位移_激活检查拒了交出HeldByActivationCheck与那句说法()
    {
        // 工单 08 验收项：「定身期间不位移」这条用例。
        // 既有测试里没有单位被定身 ⇒ 激活检查在那一局是空转、不破坏零回归；这一条是新钉的。
        var grid = BattleNavGrid.Build();
        var clock = new GameClock();
        var solver = new NavSolver(
            new NavSpace(BattleNavGrid.Space, grid),
            new NavSolveBudget { PerRequestExpansions = 4000 },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            64, 64, 32);

        var rooted = true;
        var stepper = new NavStepper(
            solver,
            new NavMoveTuning
            {
                StepSeconds = BattleContract.StepSeconds,
                LookAheadMeters = BattleNavMove.LookAheadMeters,
                StepSolveExpansions = BattleNavMove.StepSolveExpansions,
            },
            clock,
            (agentId, stepIndex, plannedMeters) => rooted
                ? new NavMoveCheckVerdict(false, "定身")
                : NavMoveCheckVerdict.Allow);

        var start = BattleNavGrid.ToGrid(0.0f, 7.0f);
        stepper.Register(new NavAgentSpec
        {
            Id = "p01",
            RadiusMeters = BattleNavGrid.RadiusOf(TroopKind.Warrior),
            MoveSpeedMetersPerSecond = BattleNavGrid.SpeedOf(TroopKind.Warrior),
            Start = start,
            FacingDegrees = 0.0,
        });
        stepper.Issue("p01", NavCommand.Chase("e01", BattleNavGrid.ToGrid(0.0f, -7.0f), 0.0));

        // 定身期间连推 5 步：一步都不许挪。
        for (var i = 0; i < 5; i++) clock.Advance(1);

        var held = stepper.SolutionOf("p01");
        held.Status.ShouldBe(NavMoveStatus.HeldByActivationCheck, "激活检查拒了要交出这一档状态，不许静默跳过（「事件发射前置」）");
        held.CheckReason.ShouldBe("定身", "拒了必须带一句说法，「他为什么不动」在快照上读得出原因");
        held.MovedMeters.ShouldBe(0.0, "定身期间位移是 0");
        held.Position.ShouldBe(start, "定身期间坐标一个数都不动");
        stepper.PositionOf("p01").ShouldBe(start, "层内那份坐标也不动（位移只有一条写路径）");

        // 解定身：同一个单位、同一��命令，下一步立刻走起来 —— 证明拦的是「这一步」不是「这个单位」。
        rooted = false;
        clock.Advance(1);

        var freed = stepper.SolutionOf("p01");
        freed.Status.ShouldBe(NavMoveStatus.Moving, "解定身后下一步照走");
        freed.CheckReason.ShouldBe("无", "放行时激活检查那一列是「无」");
        freed.MovedMeters.ShouldBeGreaterThan(0.0, "解定身后真的产了位移");
    }

    // === 三个命令（工单点名的三档）===

    [Fact]
    public void 命令一_原地不动_对面已无人时站着一步都不挪()
    {
        var (stepper, clock) = FreshStepper();

        stepper.Issue("p01", NavCommand.Hold(BattleNavGrid.ToGrid(0.0f, 7.0f)));
        clock.Advance(3);

        var solution = stepper.SolutionOf("p01");
        solution.Status.ShouldBe(NavMoveStatus.Holding, "原地不动到位读 Holding（与「到了」不是同一档）");
        solution.MovedMeters.ShouldBe(0.0, "原地不动一步都不挪");
        solution.Position.ShouldBe(BattleNavGrid.ToGrid(0.0f, 7.0f), "坐标不动");
    }

    [Fact]
    public void 命令二_追实体_目标每步重读坐标_目标动了它跟得上()
    {
        var (stepper, clock) = FreshStepper();
        var target = BattleNavGrid.ToGrid(0.0f, -7.0f);

        // 第 1 步：朝静止靶走。
        stepper.Issue("p01", NavCommand.Chase("e01", target, 0.0));
        clock.Advance(1);
        stepper.SolutionOf("p01").Status.ShouldBe(NavMoveStatus.Moving, "追实体那一档在走");
        stepper.SolutionOf("p01").Goal.ShouldBe(target, "目标描述是目标这一轮的坐标（层里没有单位表）");

        // 第 2 步：目标往侧面挪 3 米，宿主重读后重发命令 —— 单位朝新坐标转。
        target = BattleNavGrid.ToGrid(3.0f, -7.0f);
        stepper.Issue("p01", NavCommand.Chase("e01", target, 0.0));
        clock.Advance(1);

        var after = stepper.SolutionOf("p01");
        after.Goal.ShouldBe(target, "目标坐标每步由宿主重读（层不缓存也不外推）");
        after.Position.X.ShouldBeGreaterThan(0.0, "目标挪到 +X 侧 ⇒ 这一步的落点 X 变成正的（朝新目标走）");
    }

    [Fact]
    public void 命令三_站位冲突_同组两兵身体重叠各推开一半()
    {
        // 语义与 BattleWorld.SeparateSameSide 逐字对齐（第四期已钉），这里钉的是「宿主递组号、层只推同组」。
        // 坐标直接用<b>空间局部米</b>（NavSeparation 是层里的纯函数，不认世界坐标那一次换算）。
        var grid = BattleNavGrid.Build();
        var agents = new[]
        {
            Projection("甲", 1, 2.0, 2.0, 0.4),
            Projection("乙", 1, 2.6, 2.0, 0.4),   // 同组、间距 0.6 < 最小间距 0.8 ⇒ 推
            Projection("丙", 2, 2.0, 2.0, 0.4),   // 异组 ⇒ 不推
        };

        var result = NavSeparation.Resolve(grid, agents);

        // 探针读数：最小间距 0.8、当前间距 0.6 ⇒ 重叠 0.2、各退一半 0.1 ⇒ 甲 1.9、乙 2.7。
        // （第四期 4.3 节那组 2.4 是<b>三个</b>单位的读数：丙与乙同组又把乙推了 0.3。
        // 这里丙与甲乙异组、不参与，所以乙只退自己那 0.1。）
        result.PairCount.ShouldBe(1, "只推同组的那一对（异组之间不推）");
        result.Pairs[0].FirstId.ShouldBe("甲", "配对序按列表序");
        result.Pairs[0].SecondId.ShouldBe("乙", "配对序按列表序");
        result.Pairs[0].OverlapMeters.ShouldBe(0.2, 1e-9, "重叠量 = 最小间距 0.8 − 当前间距 0.6");
        result.Positions[0].X.ShouldBe(1.9, 1e-9, "甲沿连线各退一半 0.1 米");
        result.Positions[1].X.ShouldBe(2.7, 1e-9, "乙沿连线各退一半 0.1 米（异组的丙不参与这一对）");
        result.Positions[2].X.ShouldBe(2.0, 1e-9, "异组那个一个数都不动");

        // 寻路网格一个格都没动（站位冲突是叠加修正，不进网格 —— ADR 0008）。
        grid.StaticVersion.ShouldBe(0, "推人不动地形（静态版本不动）");
    }

    [Fact]
    public void 组号_只推同组的_所以方别由宿主用标量表达()
    {
        // 层里不许出现「同方」那个词（那是原型的知识），组号是宿主递进去的标量。
        BattleNavMove.GroupOf(Side.Player).ShouldBe(1, "我方一个组号");
        BattleNavMove.GroupOf(Side.Enemy).ShouldBe(2, "敌方另一个组号");
        BattleNavMove.GroupOf(Side.Player).ShouldNotBe(BattleNavMove.GroupOf(Side.Enemy),
            "两个组号必须不同，否则敌我会被当成一伙推开");
    }

    // === 端到端：真世界的走位现在走寻路层（退休之后那一发）===

    [Fact]
    public void 端到端_真世界推一步_位移由寻路层那份移动解交出()
    {
        // 这一条证明世界真的接上了（工单 08 退休那一发）：世界只下命令，
        // 走位与站位冲突由 NavStepper 那一次脉冲算，落点写回宿主那一列。
        // 「宿主那一列 = 寻路层那一列」就是「寻路单入口」 那条「位置真值只有一份」的可观测形状。
        using var world = new BattleWorld(7, Formations.ShieldWall, Formations.ShieldWall, jitter: false);
        var p01 = world.Find("p01")!;
        var before = p01.Position;

        world.Advance(1);

        world.NavDebugSnapshotSegment()
            .ShouldStartWith("pathfinding space=排兵布阵竞技场 step=1 agents=30", Case.Sensitive,
                "走位那一段的读数在世界里念得出来（「状态可查」那个口）");

        var solution = world.NavSolutionOf("p01");
        solution.AgentId.ShouldBe("p01");
        solution.Command.ShouldBe(NavCommandKind.Chase, "有敌人 ⇒ 追实体那一档");
        solution.MovedMeters.ShouldBeGreaterThan(0.0, "这一步要产位移");

        // 宿主那一列与寻路层那一列逐字同一个数：换算那一对做完了，没有第二份位置。
        var (x, y) = BattleNavGrid.ToWorld(solution.Position);
        p01.Position.X.ShouldBe(x, "宿主那一列就是寻路层那一列换算回来的数（「寻路单入口」）");
        p01.Position.Y.ShouldBe(y);
        ((double)(p01.Position - before).Length()).ShouldBeGreaterThan(0.0, "推一步位置真的动了");
    }

    [Fact]
    public void 带位移那一发走寻路层的落位移口_下一步同步不把它抹掉()
    {
        // rider.charge 落地：技能层发 UnitMoved，宿主把它写成深度轴坐标。
        // 退休前那处直接改 BattleUnit.Position —— 位置真值搬进寻路层之后那样写会被下一步的
        // 同步抹掉，而快照上完全看不出发生过。现在走 BattleNavMove.ApplyMoveTo（工单 08 裁决一口）。
        using var world = new BattleWorld(1, RiderDuel(), ArcherDuel(), jitter: false);
        var abilities = BattleAbilities.AttachRift(world, "p01");
        abilities.Grant("p01", BattleAbilities.ChargeAbilityId);
        world.AdvanceDebugSteps(4);

        var rider = world.Find("p01")!;
        var from = rider.Position.Y;
        var to = from - 3.0;
        abilities.TryActivate("p01", BattleAbilities.ChargeAbilityId, "e01", (from, to)).ShouldBeTrue(
            "带位移的按下走的是同一个入口");

        world.AdvanceDebugSteps((int)BattleAbilities.ChargeArriveSteps + 1);

        // 到位那一步：深度落到目的地附近（±同一步世界自己的走位，探针实测 ≤ 0.35 米那一档）。
        ((double)rider.Position.Y).ShouldBe(to, 0.35, "落位移口把深度轴写到目的地附近（单挑场 A66 那一格）");

        // 关键一格：再推若干步，落点不许被「同步」抹回 charge 之前那一份 ——
        // 抹掉了就说明落位移写在了镜像上而不是寻路层那一份（那正是退休前那种写法的后果）。
        var landed = rider.Position.Y;
        world.AdvanceDebugSteps(20);
        ((double)rider.Position.Y).ShouldBeLessThan(landed,
            "往敌侧（−Y）继续走位，深度只会更小 —— 变回 charge 之前那一份就是这一发被同步抹掉了");
        ((double)rider.Position.Y).ShouldBeLessThan(from, "终局读数离出发点仍有一段距离（−3 米那一发不会凭空消失）");
    }

    [Fact]
    public void 不参与避让的那一个确实没被推开_距离只剩走位那一份()
    {
        // 退休前 SeparateSameSide 里那一句 `!a.Alive || !b.Alive` 的落点：
        // 宿主递「这一趟算不算它」，寻路层两头都不参与（工单 08 裁决二）。
        // 两个骑手挤同一格：起手完全重叠，两具身体 0.75 + 0.75 = 1.5 米。
        //
        // 这一条用**独立时钟上的独立端口**驱动：世界自己那一份端口每步按存活表覆盖这个标记
        // （那正是生产路径），要对照读「标记记成不算」的那一种，只能在世界的脉冲之外单独递一次。
        var world = new BattleWorld(3, RiderDuel(), ArcherDuel(), jitter: false);
        var clock = new GameClock();
        // 独立时钟上的独立装配：这一份装配只服务这一条用例的独立端口，不碰世界那一份。
        var move = new BattleNavMove(new WorldAssembly(clock), world);
        try
        {
            var a = world.Find("p01")!;
            var b = world.Find("p02")!;
            a.Position.ShouldBe(b.Position, "摆位让两个骑手挤同一格 —— 重叠这一格造得出来才有得验");

            // 基准：都算 ⇒ 走位 0.32 米之外还各推开 0.75 米，中心距 1.5 米（重合拆开那一支）。
            move.SetSpeedOf(a, a.Speed);
            move.SetSpeedOf(b, b.Speed);
            move.IssueFor(a, world.Find("e01"));
            move.IssueFor(b, world.Find("e01"));
            clock.Advance(1);
            ((double)(a.Position - b.Position).Length()).ShouldBe(1.5, 0.000001,
                "基准这一格：两个骑手被沿第一根轴一前一后拆开，各退 0.75 米");

            // 另一局：把 b 记成「这一趟不算它」⇒ 中心距只剩走位那一份。
            using var marked = new BattleWorld(3, RiderDuel(), ArcherDuel(), jitter: false);
            var markClock = new GameClock();
            var markedMove = new BattleNavMove(new WorldAssembly(markClock), marked);
            var c = marked.Find("p01")!;
            var d = marked.Find("p02")!;
            markedMove.SetSpeedOf(c, c.Speed);
            markedMove.SetSpeedOf(d, d.Speed);
            markedMove.IssueFor(c, marked.Find("e01"));
            markedMove.IssueFor(d, marked.Find("e01"));
            markedMove.SetCountedInSeparationOf(d, false);
            markClock.Advance(1);

            ((double)(c.Position - d.Position).Length()).ShouldBe(0.0,
                "不参与避让的那一个确实没被推开：两具照同一个目标同向走位，仍完全重合（基准那一局是 1.5 米）");
            markedMove.SolutionOf("p02").StuckOn.ShouldBeNull("没被推过就没有「卡在谁身上」那一档可读");
            markedMove.SolutionOf("p01").StuckOn.ShouldBeNull("推人那一侧同样没有 —— 参与标记是两头都不参与");
            markedMove.Detach();
        }
        finally
        {
            move.Detach();
        }
    }

    [Fact]
    public void 倒下的那个坐标一步都不再动_世界按存活表递参与标记()
    {
        // 参与标记在真世界里由宿主每步递（BattleWorld.Step 阶段 3，按本步扣完血之后的存活表）。
        // 这一条钉住那条真路径：倒下的那个既不再走位，也不会被同袍推开；
        // 而这一局还在打 —— 旁边的人照旧走位（否则这条只钉住了「全场冻住」那一种坏）。
        using var world = new BattleWorld(1001, new DeploymentBoard().ToFormation(), PlacementComparison.EnemyFormation);
        var p01 = world.Units.First(u => u.Side == Side.Player);

        var fallen = -1;
        for (var i = 0; i < 400 && fallen < 0; i++)
        {
            world.Advance(1);
            if (!p01.Alive) fallen = (int)world.Tick;
        }

        fallen.ShouldBeGreaterThan(0, "400 步里 p01 没倒 —— 这条用例的前提没了（数值退化）");
        var resting = p01.Position;
        var othersBefore = world.Units.Where(u => u != p01).Select(u => u.Position).ToList();

        world.Advance(40);

        p01.Position.ShouldBe(resting, "倒下的那个坐标一步都不再动：它既不被推开，也不推别人");
        world.Units.Where(u => u != p01).Select(u => u.Position).ShouldNotBe(
            othersBefore, "这一局还在打，别的单位照旧走位（否则这条只钉住了「全场冻住」那一种坏）");
    }

    [Fact]
    public void 判定没被偷换_射程与进射程那一步逐步相等()
    {
        // 工单 08 判据换法那一格：新旧两条路不逐字相同（末位差），所以不比位置，
        // 改比「判定」—— 射程、够不够得着、首次进射程那一步、翻转次数、终局血量。
        // 下面五个数是探针在**旧路**（手写 float）与**新路**（寻路层 double）上各读一遍得到的，
        // 两边一致：语义没被偷换，变的只有位置末位。
        using var world = new BattleWorld(7, RiderSolo(), RiderSolo(), jitter: false);
        var a = world.Find("p01")!;
        var b = world.Find("e01")!;

        var firstInReach = -1;
        var flips = 0;
        var inReach = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (var s = 0; s < 1200; s++)
        {
            world.Advance(1);
            foreach (var u in world.Units)
            {
                var t = world.Find(u.TargetId);
                if (t is null) continue;
                var now = (u.Position - t.Position).Length() <= u.Card.ReachAgainst(t.Card);
                if (inReach.TryGetValue(u.Id, out var was) && was != now) flips++;
                inReach[u.Id] = now;
                if (now && firstInReach < 0) firstInReach = s + 1;
            }
        }

        ((double)a.Card.ReachAgainst(b.Card)).ShouldBe(3.700000047683716, 0.000001,
            "两个骑手对位的射程 3.7 米（两条路同一个数）");
        firstInReach.ShouldBe(23, "首次进射程在第 23 步（两条路一致）—— 「够得着就停」那一档没被偷换");
        flips.ShouldBe(2, "1200 步里判定翻转 2 次（两条路一致）—— 贴身那一档在边界上抖的形状没变");
        b.Health.ShouldBe(6, "终局血量逐字相同（两条路一致）—— 判定与结算没被偷换");
        ((double)b.Position.Y).ShouldBe(-1.6399997472763062, 0.000001,
            "终局坐标只剩末位差（旧路 −1.6399991512298584，新路 −1.6399997472763062）");
    }

    // === 摆位帮手（探针与用例共用同一份口径）===

    /// <summary>两个骑手挤同一格（同族同格 ⇒ 起手就重叠；退休前靠抖动分开，这一局关了抖动）。</summary>
    private static Formation RiderDuel()
        => Lineup("我", new UnitPlacement(TroopKind.Rider, 1, 2), new UnitPlacement(TroopKind.Rider, 1, 2));

    /// <summary>一个弓手（拉开距离用：让重叠那一格先只由同袍自己产生）。</summary>
    private static Formation ArcherDuel()
        => Lineup("敌", new UnitPlacement(TroopKind.Archer, 0, 0), new UnitPlacement(TroopKind.Archer, 0, 1));

    /// <summary>一个骑手（判定那一格用：探针的读数就是在这份摆法上读出来的，别换阵容）。</summary>
    private static Formation RiderSolo()
        => Lineup("单骑", new UnitPlacement(TroopKind.Rider, 1, 2));

    /// <summary>按名 + 格摆一支小队。</summary>
    private static Formation Lineup(string name, params UnitPlacement[] placements)
        => new(name, name, placements);

    // === 内部 ===

    /// <summary>造一条 agent 投影。<b>坐标是空间局部米</b>——层里的纯函数不认世界坐标那一次换算。</summary>
    private static NavAgentProjection Projection(string id, int group, double x, double z, double radius)
        => new()
        {
            Id = id,
            Group = group,
            Position = new NavPoint(x, z),
            RadiusMeters = radius,
            CountedInSeparation = true,
        };

    private static (NavStepper Stepper, GameClock Clock) FreshStepper()
    {
        var grid = BattleNavGrid.Build();
        var clock = new GameClock();
        var solver = new NavSolver(
            new NavSpace(BattleNavGrid.Space, grid),
            new NavSolveBudget { PerRequestExpansions = BattleNavMove.PerRequestExpansions },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            64, 64, 32);
        var stepper = new NavStepper(
            solver,
            new NavMoveTuning
            {
                StepSeconds = BattleContract.StepSeconds,
                LookAheadMeters = BattleNavMove.LookAheadMeters,
                StepSolveExpansions = BattleNavMove.StepSolveExpansions,
            },
            clock);
        stepper.Register(new NavAgentSpec
        {
            Id = "p01",
            RadiusMeters = BattleNavGrid.RadiusOf(TroopKind.Warrior),
            MoveSpeedMetersPerSecond = BattleNavGrid.SpeedOf(TroopKind.Warrior),
            Start = BattleNavGrid.ToGrid(0.0f, 7.0f),
            FacingDegrees = 0.0,
        });
        return (stepper, clock);
    }
}
