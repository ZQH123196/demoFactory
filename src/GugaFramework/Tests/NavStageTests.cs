#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Pathfinding.Stage;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 寻路试验场的契约测试 —— <b>场子那条边的五条口径</b>（通用 / 理想 / 固定 + 只读表现层 + 不给缺省）。
///
/// <para>这里守的是六件事：
/// <list type="number">
/// <item><b>通用</b>：换一块场地只改注册口，场子本身一个字不动（反射 + 源码形状两处钉）；</item>
/// <item><b>理想</b>：二维摆位、无墙/有墙两种场地都跑得起来、能定住（暂停 / 单步）、能慢放（倍率）、
/// 无经济与 UI 干扰；</item>
/// <item><b>固定</b>：同种子重跑逐字全等、逐步与大块快进逐字相等、随机项一律能关掉；</item>
/// <item><b>只读</b>：表现层只读移动解，禁止第二次计算、跑走分档不另立（源码形状钉住）；</item>
/// <item><b>不给缺省</b>：注册口每一列都必填（反射钉 <c>required</c>），漏注册口当场抛；</item>
/// <item><b>三个口</b>：时间可控 / 状态可查 / 行为可注入各有一个口，且时间控制不进快照。</item>
/// </list></para>
///
/// <para>逐字读数全部先跑探针读出来再写成断言（探针落 <c>.cache/Pathfinding/StageProbe/</c>，用完即删）——
/// 本期的探针查出<b>第 1 步跑者的净位移 1.6958 米大于标称 1.5 米</b>：同组避让在走完之后把它推开了，
/// 于是「位移 / 速度」两列读的是<b>净位移</b>，速度因此短暂地大于它自己的移速。逐字钉住是为了让这一条
/// 不是注释里的说法。</para>
/// </summary>
public class NavStageTests
{
    /// <summary>「拿 14 做一次比较」那一档分档线的形状（<c>ModelAnimator.cs:62</c> 的阈值住在那里）。</summary>
    private static readonly System.Text.RegularExpressions.Regex ThresholdComparison =
        new(@"[<>]=?\s*14\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    // ---------- 不给缺省（验收第 4 条）----------

    [Fact]
    public void 场子不给任何缺省_注册口十二列全required_只有避让档可空()
    {
        RequiredOf<NavStageSpec>().ShouldBe(new[]
        {
            nameof(NavStageSpec.BlockedCells), nameof(NavStageSpec.Capacity), nameof(NavStageSpec.CellMeters),
            nameof(NavStageSpec.CellsX), nameof(NavStageSpec.CellsZ), nameof(NavStageSpec.OriginX),
            nameof(NavStageSpec.OriginZ), nameof(NavStageSpec.RecomputeTuning),
            nameof(NavStageSpec.Roster), nameof(NavStageSpec.SolveBudget), nameof(NavStageSpec.Space),
            nameof(NavStageSpec.Tuning),
        }, Case.Sensitive, "场子一个缺省都不给：格长、格数、网格原点、阻挡布局、名册、步进档、预算档、阈值档、容量档全必填");

        // 唯一可空的那一列是二值选择（「开不开避让」），不是「忘了给就用这个数」。
        OptionalOf<NavStageSpec>().ShouldBe(new[] { nameof(NavStageSpec.Separation) }, Case.Sensitive,
            "多出任何一列可空 = 场子开始替调用方挑东西了");

        RequiredOf<NavStageCapacity>().ShouldBe(new[]
        {
            nameof(NavStageCapacity.EventRecords), nameof(NavStageCapacity.PathCacheEntries),
            nameof(NavStageCapacity.SolveRecords), nameof(NavStageCapacity.TrackedRequests),
        }, Case.Sensitive, "四本账各留多少条也是宿主观");

        RequiredOf<NavStageRoster>().ShouldBe(new[] { nameof(NavStageRoster.Units) }, Case.Sensitive);
        RequiredOf<NavStageUnit>().ShouldBe(new[]
        {
            nameof(NavStageUnit.FacingDegrees), nameof(NavStageUnit.Id),
            nameof(NavStageUnit.MoveSpeedMetersPerSecond), nameof(NavStageUnit.RadiusMeters),
            nameof(NavStageUnit.Start),
        }, Case.Sensitive, "身体半径与移速是标量，原型的词表不进这一层（「寻路单入口」）");
    }

    [Fact]
    public void 缺注册口当场抛_场子不替调用方挑一块地()
    {
        Should.Throw<ArgumentNullException>(() => new NavStage(null!))
            .Message.ShouldContain("注册口", Case.Sensitive, "抛点要说清缺的是哪一样");
    }

    [Fact]
    public void 格长与格数不成形状当场抛_不是兜一个缺省()
    {
        var bad = With(spec => spec with { CellMeters = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => new NavStage(bad))
            .Message.ShouldContain("格长", Case.Sensitive, "非正数格长当场抛");

        var badCount = With(spec => spec with { CellsX = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => new NavStage(badCount))
            .Message.ShouldContain("格数", Case.Sensitive, "非正格数当场抛");
    }

    [Fact]
    public void 空阻挡表是合法声明_与漏给那一种形状不同()
    {
        // 空表 = 明确「这片没有地形阻挡」（合法开局）。
        var open = With(spec => spec with { BlockedCells = Array.Empty<NavCell>() });
        var stage = new NavStage(open);
        stage.Grid.CellsX.ShouldBe(16);
        stage.Grid.IsBlocked(new NavCell(8, 8)).ShouldBeFalse("空阻挡表 = 这一片没有地形阻挡");
        stage.Grid.Clearance.At(8, 8).ShouldBe(8.0,
            "16×16 空场正中的净空 = 到界外虚拟阻挡那一格的中心距离（8 格 = 8 米；探针读出来的）");
        stage.Grid.Clearance.At(0, 0).ShouldBe(1.0, "贴边那格被界外虚拟阻挡压到 1 米（第一期那条口径）");

        // 漏给不是空表：required 编译期就断，运行时压根造不出「没给」这个状态。
        RequiredOf<NavStageSpec>().Contains(nameof(NavStageSpec.BlockedCells))
            .ShouldBeTrue("阻挡表那一列也是 required：空表是合法声明，漏给不是另一种形状");
    }

    // ---------- 固定：headless 同种子逐字全等（验收第 3 条）----------

    [Fact]
    public void 同种子重跑逐字全等_推进三十步两跑快照逐字相等()
    {
        var a = new NavStage(NavStageSample.Spec(), seed: 1);
        var b = new NavStage(NavStageSample.Spec(), seed: 1);
        a.Advance(30);
        b.Advance(30);

        var first = a.DebugSnapshot();
        first.ShouldBe(b.DebugSnapshot(), "同种子、同注册口、同一串推进 ⇒ 状态级快照逐字相等（「断言比快照」 只比这份）");
        first.ShouldContain("tick=30 seconds=3 seed=1", Case.Sensitive, "30 步 = 3 秒（「时间基准」：步 ÷ 10）");
    }

    [Fact]
    public void 换种子只改头部那一列_其余逐字相等因为本层不用随机做决策()
    {
        // 场子「固定」那一条口径的另一半：**随机项一律能关掉**，而这一层压根不拿随机做决策
        // （求解器那五处写死的选择就是确定性的来源）。所以换种子不改变任何演化读数 ——
        // 整份快照唯一不同的就是头部那一个种子号，而种子本来就该是读数（不然「这一局是哪一档」要回头翻代码）。
        var a = new NavStage(NavStageSample.Spec(), seed: 1);
        var b = new NavStage(NavStageSample.Spec(), seed: 987654);
        a.Advance(12);
        b.Advance(12);

        var linesA = a.DebugSnapshot().Split('\n');
        var linesB = b.DebugSnapshot().Split('\n');
        linesA.Length.ShouldBe(linesB.Length, "两跑的行数逐字相同");
        linesA[0].ShouldBe("tick=12 seconds=1.2 seed=1 random=1 agents=2");
        linesB[0].ShouldBe("tick=12 seconds=1.2 seed=987654 random=1 agents=2",
            "唯一该不同的是头部那个种子号");
        linesA.Skip(1).ShouldBe(linesB.Skip(1), "第 2 行往后（网格、每个单位、求解记录、事件账本）逐字相等");

        // 随机口本身仍然可关：关掉后每次给同一个数。
        a.RandomEnabled = false;
        a.FixedRandomValue = 0.25;
        a.NextRandom().ShouldBe(0.25);
        a.NextRandom().ShouldBe(0.25);
    }

    [Fact]
    public void 逐步推进与大块快进逐字相等_推法不改变演化()
    {
        var stepwise = new NavStage(NavStageSample.Spec());
        var block = new NavStage(NavStageSample.Spec());
        for (var i = 0; i < 20; i++)
        {
            stepwise.AdvanceStep();
        }

        block.Advance(20);
        stepwise.DebugSnapshot().ShouldBe(block.DebugSnapshot(), "推 20 次与一次推 20 是同一个答案的两种拿法");
    }

    [Fact]
    public void 步号只有一份_推N步之后求解器步号与时钟逐字相等()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.Advance(20);

        // 三本步号在这里必须逐字相等。NavStage.AdvanceOneStep 原来自己又开了一次预算，
        // 而 NavStepper 订阅的是同一本 GameClock（Advance(1) 必发 Pulse）⇒ 一个逻辑步开两次
        // ⇒ 求解器步号按 2×Tick 走，「距上次求解走了几步」那条重算触发按配置值的一半触发。
        // 快照里印的是 Stepper.StepIndex（1×），所以那一格曾经与求解器步号对不上而没人看出来。
        stage.Tick.ShouldBe(20, "时钟推了 20 步");
        stage.Stepper.StepIndex.ShouldBe(20, "步进器的步号与时钟同数");
        stage.Solver.StepIndex.ShouldBe(20, "求解器的步号与时钟逐字相等 —— 不是 40");

        stage.Advance(5);
        stage.Tick.ShouldBe(25);
        stage.Solver.StepIndex.ShouldBe(25, "再推 5 步仍然是 1:1，差值不随步数放大");
    }

    // ---------- 理想：时间可控 / 状态可查 / 行为可注入 ----------

    [Fact]
    public void 时间可控_暂停只冻逻辑而单步照走()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.Advance(5);
        var before = stage.DebugSnapshot();

        stage.Pause();
        stage.Advance(10).ShouldBeFalse("暂停中批量推进一步都不走");
        stage.AdvanceRealSeconds(1.0).ShouldBe(0, "暂停中每帧那一发也不走");
        stage.DebugSnapshot().ShouldBe(before, "暂停不改逻辑状态");

        stage.AdvanceStep();
        stage.Tick.ShouldBe(6, "暂停中单步调试口照走一步 —— 「停下来一格格看」是暂停的全部用处");
    }

    [Fact]
    public void 时间控制不进快照_暂停与慢放都不让逐字比对报分叉()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.Advance(4);
        var before = stage.DebugSnapshot();

        stage.Speed = 0.25;
        stage.Pause();
        stage.Resume();
        stage.DebugSnapshot().ShouldBe(before, "倍率与暂停是表现层状态，进快照会在没分叉的地方报分叉（「试验场随技能层」 同源理由）");
    }

    [Fact]
    public void 倍率换算走既有的那份_真实秒乘倍率乘每秒步数()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.AdvanceRealSeconds(0.05).ShouldBe(0, "1× 下 0.05 秒 = 0.5 步，不够一步先攒着（「时钟解耦」）");
        stage.AdvanceRealSeconds(0.05).ShouldBe(1, "攒够一个步长才推一步");
        stage.Tick.ShouldBe(1);

        var slow = new NavStage(NavStageSample.Spec());
        slow.Speed = 0.25;
        slow.AdvanceRealSeconds(0.4).ShouldBe(1, "0.25× 下 0.4 秒 = 0.25 × 10 × 0.4 = 1 步");
        var fast = new NavStage(NavStageSample.Spec());
        fast.Speed = 20;
        fast.AdvanceRealSeconds(0.1).ShouldBe(20, "20× 下 0.1 秒 = 20 步");
    }

    [Fact]
    public void 行为可注入只有一条口_封地形走StampTerrain且静态版本加一()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.Grid.StaticVersion.ShouldBe(0, "开局静态版本 0");

        var readout = stage.InjectReadout("StampTerrain", "8|7|blocked|1");
        readout.ShouldBe("ok=1 reason=Applied cell=(8, 7) static=1 dynamic=0", "注入读数带 ok 字段（「写路径进 Journal」）");
        stage.Grid.IsBlocked(new NavCell(8, 7)).ShouldBeTrue("封的那一格真的封上了");
        stage.Grid.StaticVersion.ShouldBe(1, "封地形动静态版本（动态层没动）");
        stage.Grid.DynamicVersion.ShouldBe(0);

        // 值没真变就不动版本号（第一期那条口径）。
        stage.InjectReadout("StampTerrain", "8|7|blocked|1");
        stage.Grid.StaticVersion.ShouldBe(1, "封两次同一格不 +1 —— 在手路径会白白重算");
    }

    [Fact]
    public void 注入的参数缺一段当场抛_不兜0()
    {
        var stage = new NavStage(NavStageSample.Spec());
        Should.Throw<FormatException>(() => stage.InjectReadout("StampTerrain", "3|2|pass"))
            .Message.ShouldContain("段", Case.Sensitive, "必填段缺了当场抛（「桥只转发」：数字参数缺了当场抛，不许兜 0）");
        Should.Throw<FormatException>(() => stage.InjectReadout("StampTerrain", "三|2|pass|1"))
            .Message.Length.ShouldBeGreaterThan(0, "读不出数也要抛，消息说清");
    }

    [Fact]
    public void 注入未知动词当场抛_状态性拒绝回读数()
    {
        var stage = new NavStage(NavStageSample.Spec());
        Should.Throw<InvalidOperationException>(() => stage.InjectReadout("NoSuchVerb", ""))
            .Message.ShouldContain("NoSuchVerb", Case.Sensitive, "未知动词当场抛并念出那个名字");

        var refused = stage.InjectReadout("SetOrder", "查无此单位|GoTo|0.4|1|2");
        refused.ShouldBe("ok=0 reason=UnknownAgent id=查无此单位", "拒绝是可观察行为，回读数而不是抛");
    }

    [Fact]
    public void 状态可查一发读出全量真值_网格与两个版本与每个单位的六列读数()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.Advance(3);
        var lines = stage.DebugSnapshot().Split('\n');

        lines[0].ShouldBe("tick=3 seconds=0.3 seed=1 random=1 agents=2", "头部：步号 / 秒 / 种子 / 随机开关 / 单位数");
        lines[1].ShouldStartWith(
            "navgrid space=navstage.field cells=16x16 cell=1 origin=0,0 static=0 dynamic=0 blocked=", Case.Sensitive,
            "第二行是网格段（取法是层里那一份，不另渲染）");
        lines[1].ShouldContain("clearance=", Case.Sensitive, "网格段带净空");

        // 每个单位一行：位置 / 朝向 / 速度 / 位移 / 状态 / 卡住类别 / 卡住步数（前视点与激活检查说法另带）。
        var runner = lines.First(l => l.StartsWith(NavStageSample.RunnerId, StringComparison.Ordinal));
        runner.ShouldBe(
            "stage-runner GoTo 位置 (2.7007869082877822, 6.035272264612162) 朝向 12.98928686213664 " +
            "速度 15.000000000000002 位移 1.5000000000000002 状态 Moving 卡住 无/0 子步 3 目标 (14, 14) " +
            "前视 (2.8131713398662366, 6.522478319109423) 激活检查 无");

        var creeper = lines.First(l => l.StartsWith(NavStageSample.CreeperId, StringComparison.Ordinal));
        creeper.ShouldBe(
            "stage-creeper GoTo 位置 (1.4653999974668657, 2.7745541494628996) 朝向 16.831757280929754 " +
            "速度 1.4999999999999996 位移 0.14999999999999997 状态 Moving 卡住 无/0 子步 1 目标 (14, 13) " +
            "前视 (2.0010903729932377, 4.545298575281717) 激活检查 无");

        lines.Contains("solve kept=2 dropped=0 容量=64")
            .ShouldBeTrue("求解记录带头一行 kept / dropped（「三条铁律」 ③：账本里没有 ≠ 没发生）；"
                          + "复用这一档下 3 步里两个单位各解一次，逐步重解时是 kept=6");
        lines.Last().ShouldBe("events total=0 kept=0 dropped=0 capacity=256", "末行是观测窗口读数（带丢弃数）");
    }

    [Fact]
    public void 全程没进过阻挡格_每一步的落点对着用例自己那份阻挡表查()
    {
        // 阻挡表是**用例自己**从注册口那份声明重建的（不复用被测实现那条读法）。
        var blocked = new HashSet<NavCell>(NavStageSample.WallCells());
        var stage = new NavStage(NavStageSample.Spec());
        for (var i = 0; i < 40; i++)
        {
            stage.AdvanceStep();
            foreach (var id in stage.Stepper.AgentIds)
            {
                var at = stage.Stepper.PositionOf(id);
                var cell = new NavCell((int)Math.Floor(at.X), (int)Math.Floor(at.Z));
                blocked.ShouldNotContain(cell, $"第{i + 1}步 {id} 站进了阻挡格 {cell}");
            }
        }

        stage.Grid.StaticVersion.ShouldBe(0, "40 步里没人动过地形");
    }

    [Fact]
    public void 绕障而不是穿墙_跑者十二步走完并拐过那道缺口()
    {
        var stage = new NavStage(NavStageSample.Spec());
        var facings = new List<double>();
        for (var i = 0; i < 12; i++)
        {
            stage.AdvanceStep();
            facings.Add(stage.Stepper.SolutionOf(NavStageSample.RunnerId).FacingDegrees);
        }

        var at12 = stage.Stepper.SolutionOf(NavStageSample.RunnerId).Position;
        at12.X.ShouldBe(12.811056568447105, 1e-12,
            "第 12 步的落点逐字（探针读出来的）；复用这一档下的实测值，逐步重解时是 12.861353979194964");
        at12.Z.ShouldBe(13.479410230624687, 1e-12, "同上，逐步重解时是 13.45971532738747");

        // 朝向在第 6 步从 12.99 转到 64.62 —— 那是拐过墙角的那一步（折线拐点处的转向）。
        facings[4].ShouldBe(12.989286862136622, 1e-9, "拐点前朝向不变");
        facings[5].ShouldBe(52.60080644390021, 1e-9,
            "第 6 步拐向墙角另一侧；复用这一档下的实测值，逐步重解时是 64.61577816755086");
        facings[11].ShouldBe(66.35335234108766, 1e-9,
            "拐完之后朝向保持；复用这一档下的实测值，逐步重解时是 64.61577816755086");
    }

    [Fact]
    public void 第1步净位移大于标称步长_因为位移与速度两列读的是净位移()
    {
        // 探针查出来的一条：两个单位同组且开局只差 1.0 米（最小间距 0.9），各自走完之后重叠了，
        // 避让把跑者又推开了一段 —— 于是净位移 1.6958 米 > 标称 1.5 米，速度列读到 16.96。
        // 逐字钉住是为了让「位移读的是净位移」不是一句注释。
        var stage = new NavStage(NavStageSample.Spec());
        stage.AdvanceStep();
        var runner = stage.Stepper.SolutionOf(NavStageSample.RunnerId);
        runner.MovedMeters.ShouldBe(1.6958305900967297, 1e-12);
        runner.SpeedMetersPerSecond.ShouldBe(16.958305900967297, 1e-12);
        runner.Substeps.ShouldBe(3, "子步数仍按标称步长算（1.5 ÷ 0.5 = 3），推开的那一段不切子步");

        var creeper = stage.Stepper.SolutionOf(NavStageSample.CreeperId);
        creeper.MovedMeters.ShouldBe(0.1221198045181931, 1e-12, "慢的那一个被推得更多：净位移 0.122 < 标称 0.15");
    }

    // ---------- 只读：表现层那一半（验收第 1、2 条）----------

    [Fact]
    public void 走位喂Travel的数就是移动解那一列_场子自己不另立跑走分档()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.Advance(3);

        // 喂给 ModelAnimator.Travel 的口径是「米每游戏秒」（分档线 14 在 ModelAnimator.cs:62），
        // 场子这一侧只做一次换算：原样交出移动解那一列，**不另立第二条分档线**。
        stage.TravelMetersPerGameSecondOf(NavStageSample.RunnerId).ShouldBe(15.000000000000002, 1e-12);
        stage.TravelMetersPerGameSecondOf(NavStageSample.CreeperId).ShouldBe(1.4999999999999996, 1e-12);

        // 同一份移动解两处读出来的是同一个数（表现层没有第二个来源）。
        stage.TravelMetersPerGameSecondOf(NavStageSample.RunnerId).ShouldBe(
            stage.Stepper.SolutionOf(NavStageSample.RunnerId).SpeedMetersPerSecond);

        // 跑 / 走两档由 ModelAnimator 那一条 14 决定：跑者 15 在线上方、慢的那个 1.5 在下方。
        // 场子这一侧断言的是「喂进去的数落在哪一档」，分档本身只有一份（下一条用例钉住）。
        stage.TravelMetersPerGameSecondOf(NavStageSample.RunnerId)
            .ShouldBeGreaterThan(14.0, "15 米每秒在 ModelAnimator 的跑档线上方（那一档不是场子定的）");
        stage.TravelMetersPerGameSecondOf(NavStageSample.CreeperId)
            .ShouldBeLessThan(14.0, "慢的走档");
    }

    [Fact]
    public void 表现层只读移动解_不第二次计算且不另立分档_源码形状钉住()
    {
        // 视图层是那两个吃 Godot 类型的文件（场景壳 + 跟随层）。它们不进测试线（csproj 排除名单），
        // 所以这一条用**源码形状**钉：判据是「文件里有没有那行字」，不是「运行时会不会那样」。
        // 判据只扫**代码行**：注释里提到 NavSolver 是「为什么不自己补画折线」的说明，
        // 拿说明去判它是判自己（与守卫 ④ 跳过整份说明同一个理由）。
        var code = string.Join("\n", CodeLines(ReadStageFile("NavStageScene.cs"))
            .Concat(CodeLines(ReadStageFile("NavStageUnitView.cs"))));

        // ① 它只从移动解那一列取位置 / 朝向 / 速度，绝不自己再算一遍（「表现层只读」 表现层只读）。
        code.ShouldContain("SolutionOf", Case.Sensitive, "走位读的是移动解那一份");
        foreach (var forbidden in new[] { ".Solve(", "NavSolver", "NavGrid(", "new NavGridSpec", "SetBlocked", "Issue(" })
        {
            code.ShouldNotContain(forbidden, Case.Sensitive,
                $"叠加层与跟随层不许再算一次（{forbidden} 是第二次计算的那条路）");
        }

        // ② 跑 / 走分档只有 ModelAnimator 那一份（分档线 14 在 ModelAnimator.cs:62）。
        code.ShouldContain("ModelAnimator", Case.Sensitive, "动画走 ModelAnimator");
        code.ShouldContain("TravelMetersPerGameSecondOf", Case.Sensitive, "喂进去的数由逻辑壳那一列换算");
        code.ShouldContain("Travel(", Case.Sensitive, "喂给 Travel");
        foreach (var line in code.Split('\n'))
        {
            // 分档线的形状必然是「拿 14 做一次比较」——只判这一种，不判「文件里出现过 14」
            // （颜色字面量 0.14f 那一类会让钝判据变成假红，而假红会让人把守卫整个关掉）。
            ThresholdComparison.IsMatch(line).ShouldBeFalse(
                $"视图层不许拿 14 做跑走分档的比较（那一档只住在 ModelAnimator.cs:62）：{line}");
            line.ShouldNotContain("RunThreshold", Case.Sensitive, $"跑走分档只住在 ModelAnimator 里：{line}");
        }
    }

    // ---------- 通用：换一块场地只改注册口 ----------

    [Fact]
    public void 场子本身不认识任何一块场地_逻辑壳源码里没有任何一块地形或单位()
    {
        var logic = ReadStageFile("NavStage.cs");
        foreach (var forbidden in new[] { "new NavCell(", "new NavStageUnit(", "new NavStageRoster(" })
        {
            logic.ShouldNotContain(forbidden, Case.Sensitive,
                $"逻辑壳一个字都不许揣着场地条件（{forbidden} 是替调用方挑了一块地）");
        }
    }

    [Fact]
    public void 换一块场地只改注册口_无墙平地与有墙场子给出不同的读数()
    {
        // 「通用」那一条口径的正面证法：同一份场子代码，两份注册口，两个不同的答案。
        var flat = new NavStage(With(spec => spec with { BlockedCells = Array.Empty<NavCell>() }));
        var walled = new NavStage(NavStageSample.Spec());
        flat.Advance(6);
        walled.Advance(6);

        var flatRunner = flat.Stepper.SolutionOf(NavStageSample.RunnerId);
        var walledRunner = walled.Stepper.SolutionOf(NavStageSample.RunnerId);
        flatRunner.Position.ShouldNotBe(walledRunner.Position,
            "同一段代码 + 两份注册口 ⇒ 无墙那一局走直线、有墙那一局绕开（场子没被改过一行）");
        flat.Grid.CellsZ.ShouldBe(16);
        walled.Grid.CellsZ.ShouldBe(16);
    }

    [Fact]
    public void 名册全由调用方递_一个单位那一档也跑得起来()
    {
        var one = With(spec => spec with
        {
            Roster = new NavStageRoster
            {
                Units = new List<NavStageUnit>
                {
                    new()
                    {
                        Id = "独一个",
                        RadiusMeters = 0.3,
                        MoveSpeedMetersPerSecond = 3.0,
                        Start = new NavPoint(0.5, 0.5),
                        FacingDegrees = 0,
                        OpeningOrder = NavCommand.GoTo(new NavPoint(7.5, 7.5), 0.3),
                    },
                },
            },
        });
        var stage = new NavStage(one);
        stage.Advance(20);
        stage.Stepper.SolutionOf("独一个").Status.ShouldBe(NavMoveStatus.Moving, "3 米每秒走 7 米要 25 步，20 步还在路上");

        // 名字逐字进快照：场子不认识它，只当键用。
        stage.DebugSnapshot().ShouldContain("独一个 GoTo 位置", Case.Sensitive);
    }

    [Fact]
    public void 城市格长换一份就换一份读数_格长是宿主观()
    {
        var half = With(spec => spec with { CellMeters = 0.5, CellsX = 32, CellsZ = 32 });
        var stage = new NavStage(half);
        stage.Grid.CellMeters.ShouldBe(0.5);
        stage.Grid.Clearance.ExtentXmeters.ShouldBe(16.0, "32 格 × 0.5 米 = 16 米，与 16 格 × 1 米同一块地");
        stage.DebugSnapshot().ShouldContain("cells=32x32 cell=0.5", Case.Sensitive, "格长与格数是注册口那两列");
    }

    // ---------- 工具 ----------

    private static string[] RequiredOf<T>()
        => PropertiesOf<T>().Where(p => p.GetCustomAttribute<RequiredMemberAttribute>() is not null)
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static string[] OptionalOf<T>()
        => PropertiesOf<T>().Where(p => p.GetCustomAttribute<RequiredMemberAttribute>() is null)
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static IEnumerable<PropertyInfo> PropertiesOf<T>()
        => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>在示范注册口那份上改一格（其余全沿用）——「换一块场地只改注册口」在用例里的写法。</summary>
    private static NavStageSpec With(Func<NavStageSpec, NavStageSpec> edit) => edit(NavStageSample.Spec());

    /// <summary>读场子目录下的一个源文件（源码形状用例用；读不到当场抛，不静默跳过）。</summary>
    private static string ReadStageFile(string fileName)
    {
        var path = Path.Combine(TestPaths.RepoRoot, "src", "GugaFramework", "Pathfinding", "Stage", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"读不到场子的源文件 {path} —— 源码形状用例的判据是「文件里有没有那行字」，文件不在时它与「通过」长得一模一样");
        }

        return File.ReadAllText(path);
    }

    /// <summary>只留代码行：去掉注释行与空行（注释里出现某个数字不算「那一行有那个数」）。</summary>
    private static IEnumerable<string> CodeLines(string source)
        => source.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0
                        && !l.StartsWith("//", StringComparison.Ordinal)
                        && !l.StartsWith("*", StringComparison.Ordinal))
            .ToArray();

    /// <summary>
    /// 视图层那两个吃 Godot 类型的文件的<b>代码行</b>（场景壳 + 跟随层）。
    /// 供本文件与 <see cref="NavCornersTests"/> 共用同一份取法 —— 判据是「文件里有没有那行字」，
    /// 而文件不在时当场抛（读不到与「通过」长得一模一样，那是假绿）。
    /// </summary>
    internal static IEnumerable<string> ViewCodeLines()
        => CodeLines(ReadStageFile("NavStageScene.cs")).Concat(CodeLines(ReadStageFile("NavStageUnitView.cs")));
}
