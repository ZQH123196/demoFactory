#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 寻路模块动词表的契约测试 —— <b>一份表、两处问</b>（口径照 <c>ConstructionVerbsTests</c>）。
///
/// 这里守的是五条分界：
/// <list type="number">
/// <item>算路径是读进不了录、移动是写必须进录（「写路径进 Journal」、「写路径全录」）；</item>
/// <item>作者错误当场抛（含「本侧不接的那一格」），状态性拒绝回读数；</item>
/// <item>参数次序、段数、缺省只有一处，且规范化把同一种事实折成一种 token；</item>
/// <item>网格 + 净空 + 两个版本逐字进检查点快照；</item>
/// <item>建造建成 / 移除自己落地形印子（漏了这一类等于墙不存在）。</item>
/// </list>
///
/// 逐字读数全部先跑探针读出来再写成断言 —— 本期探针查出一条真分叉，见
/// <see cref="重放端与录制端必须给同一份开局网格_地形相同而静态版本分叉照样判红"/> 那条用例的说明。
/// </summary>
public class NavVerbsTests
{
    // ---------- 词表形状 ----------

    [Fact]
    public void 七个动词名三条进录四条只读_两段名单不重不漏()
    {
        NavVerbs.Names.Length.ShouldBe(7);
        NavVerbs.InjectNames.Length.ShouldBe(3);
        NavVerbs.CallNames.Length.ShouldBe(4);
        NavVerbs.InjectNames.Intersect(NavVerbs.CallNames).ShouldBeEmpty("写与读是两条不相交的名单");
        NavVerbs.InjectNames.Concat(NavVerbs.CallNames).ShouldBe(NavVerbs.Names, "全表 = 进录那三条 + 只读那四条");

        // 逐个点名钉住：改词表时哪一条被挪了/漏了，当场看出来。
        NavVerbs.InjectNames.ShouldBe(new[] { NavVerbs.StampTerrainVerb, NavVerbs.SetOrderVerb, NavVerbs.ApplyMoveVerb });
        NavVerbs.CallNames.ShouldBe(new[]
        {
            NavVerbs.FindPathVerb, NavVerbs.ReadStateVerb, NavVerbs.AsciiMapVerb, NavVerbs.SolveRecordVerb,
        });
    }

    [Fact]
    public void 进录的判据只有一份_写路径与只读路径不靠各自记忆()
    {
        foreach (var cmd in NavVerbs.InjectNames)
        {
            NavVerbs.IsInject(cmd).ShouldBeTrue();
            NavVerbs.IsVerb(cmd).ShouldBeTrue();
        }

        foreach (var cmd in NavVerbs.CallNames)
        {
            NavVerbs.IsInject(cmd).ShouldBeFalse("算路径是读，进不了录（「写路径进 Journal」）");
        }

        NavVerbs.IsVerb("RaiseOrder").ShouldBeFalse();
    }

    [Fact]
    public void 段数能单独判_每条动词一个数_不用起任何世界()
    {
        NavVerbs.FieldCount(NavVerbs.StampTerrainVerb, "3|2|pass|1.5").ShouldBe(4);
        NavVerbs.FieldCount(NavVerbs.ApplyMoveVerb, "甲|1|2|90").ShouldBe(4);
        NavVerbs.FieldCount(NavVerbs.FindPathVerb, "0.5|0.5|7.5|7.5|0").ShouldBe(5);
        NavVerbs.FieldCount(NavVerbs.ReadStateVerb, "甲").ShouldBe(1);
        NavVerbs.FieldCount(NavVerbs.AsciiMapVerb, "").ShouldBe(0);
        NavVerbs.FieldCount(NavVerbs.SolveRecordVerb, "").ShouldBe(0);
    }

    [Fact]
    public void SetOrder的段数按命令档分档_五档各一个数()
    {
        // 命令空间五档的目标描述不是一个形状（照 spec 归一的那个形状），
        // 硬凑成同一个段数就得给每条命令印一串用不上的空段 —— 而空段是 Journal 里最容易漂的东西。
        NavVerbs.FieldCount(NavVerbs.SetOrderVerb, "甲|Hold|0|1|2").ShouldBe(5);
        NavVerbs.FieldCount(NavVerbs.SetOrderVerb, "甲|GoTo|0.4|1|2").ShouldBe(5);
        NavVerbs.FieldCount(NavVerbs.SetOrderVerb, "甲|Chase|0.4|乙|1|2").ShouldBe(6);
        NavVerbs.FieldCount(NavVerbs.SetOrderVerb, "甲|AlongWaypoints|0.4|1,2;3,4").ShouldBe(4);
        NavVerbs.FieldCount(NavVerbs.SetOrderVerb, "甲|GuardArea|0.4|1|2|3").ShouldBe(6);
    }

    // ---------- 三条分界：作者错误当场抛 ----------

    [Fact]
    public void 未知动词当场抛_不在重放里静默少打一笔()
    {
        var world = World();

        Should.Throw<InvalidOperationException>(() =>
            NavVerbs.Dispatch(world, "RaiseOrder", "甲|GoTo|0.4|1|2"));
    }

    [Fact]
    public void 落位移这一格接上了_名单里全表都在_打不成的id才给false()
    {
        // 移动流水线开了落位移那个写口（NavStepper.ApplyMove），于是 ApplyMove 进了 SupportedVerbs。
        // 「这一格没接」与「这一格接了但打不成」是两种读数：前者当场抛（上一条用例那条未知动词），
        // 后者给 ok=false（false 是给「真打不成」留的）。
        var world = World();
        world.Stepper.Register(Agent("甲", 0.5, 0.5));
        world.SupportedVerbs.Contains(NavVerbs.ApplyMoveVerb).ShouldBeTrue(
            "全表都在名单里：这一侧每一格都接得住（写三 + 读四）");

        var applied = NavVerbs.Dispatch(world, NavVerbs.ApplyMoveVerb, "甲|1.5|2.5|90");
        applied.Ok.ShouldBeTrue($"落位移这一格接住了：{applied.Text}");
        applied.Text.ShouldStartWith("ok=1", Case.Sensitive, "写路径的读数带 ok 与 reason 两段（「写路径进 Journal」）");
        world.Stepper.PositionOf("甲").ShouldBe(new NavPoint(1.5, 2.5), "发出去的位置就是落点");
        world.Stepper.SolutionOf("甲").FacingDegrees.ShouldBe(90.0, 0.000001, "朝向那一段一起落到位");

        var unknown = NavVerbs.Dispatch(world, NavVerbs.ApplyMoveVerb, "乙|1.5|2.5|90");
        unknown.Ok.ShouldBeFalse("id 没登记过 = 真打不成，给 ok=false，不抛");
        unknown.Text.ShouldContain("reason=UnknownAgent", Case.Sensitive);
    }

    [Fact]
    public void 必填参数缺了当场抛_不兜0()
    {
        var world = World();

        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, ""), "缺格列号");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, "3"), "缺格行号");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, "3|2"), "缺层");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, "3|2|pass"), "缺值");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.ReadStateVerb, ""), "缺单位 id");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.SetOrderVerb, "|GoTo|0.4|1|2"), "缺单位 id");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.SetOrderVerb, "甲|GoTo|0.4|"), "缺点 x");
    }

    [Fact]
    public void 段数不够当场抛_连这一条要几段都定不下来()
    {
        var world = World();

        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.ApplyMoveVerb, "甲|1.5|2.5"));
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.FindPathVerb, "0.5|0.5|7.5|7.5"));
    }

    [Fact]
    public void 数值读不出当场抛_层读不懂当场抛()
    {
        var world = World();

        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, "三|2|pass|1.5"));
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, "3|2.5|pass|1.5"), "格号要是整数");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, "3|2|left|1.5"), "层是封闭枚举");
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.FindPathVerb, "0.5|0.5|7.5|7.5|wide"));
    }

    [Fact]
    public void 命令档不给别名集_短名读不懂()
    {
        // Journal 里同一件事只有一种 token 写法。`along` 这种短名不进表 ——
        // 需要短名的那一头（桥 / CLI）自己去映射，表里另开一档就多一处能漂的地方。
        var world = World();

        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.SetOrderVerb, "甲|along|0.4|1,2"));
        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.SetOrderVerb, "甲|Unset|0|1|2"), "未下令不是一条可重放的命令");
    }

    [Fact]
    public void 原地不动那一档的容差必须为0_给别的数当场抛()
    {
        var world = World();

        Should.Throw<FormatException>(() => NavVerbs.Dispatch(world, NavVerbs.SetOrderVerb, "甲|Hold|0.5|1|2"));
    }

    [Fact]
    public void 阻挡层只认0和1_第三档当场抛()
    {
        var world = World();

        // 校验只有 NavTerrain.Apply 那一处（表里再判一道只会让同一条作者错误有两个报错点）。
        Should.Throw<ArgumentOutOfRangeException>(() =>
            NavVerbs.Dispatch(world, NavVerbs.StampTerrainVerb, "1|1|blocked|0.5"));
    }

    // ---------- 规范化 ----------

    [Fact]
    public void 规范化把同一种事实折成一种token()
    {
        NavVerbs.Canonicalize(NavVerbs.StampTerrainVerb, "03|02|pass|1.500")
            .ShouldBe("3|2|pass|1.5", "同一个数在 Journal 里只有一种写法（ReplayArgs 的往返格式）");
        NavVerbs.Canonicalize(NavVerbs.SetOrderVerb, "甲|GoTo|0.40|7.50|7.50|多给的")
            .ShouldBe("甲|GoTo|0.4|7.5|7.5", "多给的段当场不报错，但账本里只剩该有的那些段");
        NavVerbs.Canonicalize(NavVerbs.SetOrderVerb, "甲|AlongWaypoints|0.4|1,2;3.50,4")
            .ShouldBe("甲|AlongWaypoints|0.4|1,2;3.5,4", "航点串占一整格：点内逗号、点间分号");
        NavVerbs.Canonicalize(NavVerbs.SetOrderVerb, "甲|GuardArea|0.4|1|2|3")
            .ShouldBe("甲|GuardArea|0.4|1|2|3");
        NavVerbs.Canonicalize(NavVerbs.AsciiMapVerb, "").ShouldBe(string.Empty);
    }

    [Fact]
    public void 规范化是纯函数_解析也是_都不发东西不起世界()
    {
        var a = NavVerbs.Parse(NavVerbs.SetOrderVerb, "甲|GoTo|0.4|1|2");
        var b = NavVerbs.Parse(NavVerbs.SetOrderVerb, "甲|GoTo|0.4|1|2");

        a.ShouldBe(b);
        NavVerbs.Canonicalize(NavVerbs.SetOrderVerb, "甲|GoTo|0.4|1|2")
            .ShouldBe(NavVerbs.Canonicalize(a), "排回那一段 args 只有一处排版");
    }

    [Fact]
    public void 归一后的命令走命令空间那五个工厂_原封不动交出去()
    {
        var call = NavVerbs.Parse(NavVerbs.SetOrderVerb, "甲|AlongWaypoints|0.4|1,2;3,4");
        var command = call.ToCommand();

        command.Kind.ShouldBe(NavCommandKind.AlongWaypoints);
        command.ArriveToleranceMeters.ShouldBe(0.4);
        command.Target.Waypoints.ShouldBe(new[] { new NavPoint(1, 2), new NavPoint(3, 4) });
        command.GoalFor(0).ShouldBe(new NavPoint(1, 2));
        command.GoalFor(1).ShouldBe(new NavPoint(3, 4));
    }

    // ---------- 写进录 / 读不进录 ----------

    [Fact]
    public void 注入类进Journal且记的是规范化后的args()
    {
        var world = World();
        world.Stepper.Register(Agent("甲", 0.5, 0.5));
        using var assembly = NavReplayWorld.OpenSession(world, 7, recording: true);

        assembly.Inject(NavVerbs.StampTerrainVerb, "03|02|pass|1.500").ShouldBeTrue();
        assembly.Inject(NavVerbs.SetOrderVerb, "甲|GoTo|0.40|7.50|7.50").ShouldBeTrue();

        assembly.Journal.Count.ShouldBe(2);
        assembly.Journal[0].Args.ShouldBe("3|2|pass|1.5");
        assembly.Journal[1].Args.ShouldBe("甲|GoTo|0.4|7.5|7.5");
        assembly.Journal.Select(e => e.Seq).ShouldBe(new[] { 0, 1 });
    }

    [Fact]
    public void 拒绝是可观察行为_入录且ok为假_重放要比它()
    {
        var world = World();
        using var assembly = NavReplayWorld.OpenSession(world, 7, recording: true);

        assembly.Inject(NavVerbs.SetOrderVerb, "乙|GoTo|0.4|7.5|7.5", out var reason).ShouldBeFalse();

        assembly.Journal.Count.ShouldBe(1, "拒绝也是一次写（它真的打过写路径），漏录就等于把这次分叉藏起来");
        assembly.Journal[0].Ok.ShouldBeFalse();
        reason.ShouldContain("UnknownAgent");
    }

    [Fact]
    public void call类不进Journal_读数也不带ok字段()
    {
        var world = World(8, 8);
        world.Stepper.Register(Agent("甲", 0.5, 0.5));
        using var assembly = NavReplayWorld.OpenSession(world, 7, recording: true);

        var path = NavVerbs.Dispatch(world, NavVerbs.FindPathVerb, "0.5|0.5|7.5|7.5|0");
        var state = NavVerbs.Dispatch(world, NavVerbs.ReadStateVerb, "甲");
        var map = NavVerbs.Dispatch(world, NavVerbs.AsciiMapVerb, "");
        var records = NavVerbs.Dispatch(world, NavVerbs.SolveRecordVerb, "");

        assembly.Journal.ShouldBeEmpty("算路径是读进不了录（「写路径进 Journal」）：重放端要的是自己重算");
        path.Text.ShouldStartWith("from=", Case.Sensitive);
        state.Text.ShouldStartWith("甲", Case.Sensitive);
        map.Text.ShouldNotContain("ok=", Case.Sensitive, "只读读数没有「成没成」这件事，印一个 ok=1 只是多认一个词");
        records.Text.ShouldStartWith("solve kept=", Case.Sensitive);

        // 走装配那一支也不入录：动词名单递的就是 InjectNames。
        assembly.Inject(NavVerbs.AsciiMapVerb, "").ShouldBeTrue();
        assembly.Journal.ShouldBeEmpty();
    }

    [Fact]
    public void 未知动词不是一次写_装配那一支返false且不入录()
    {
        var world = World();
        using var assembly = NavReplayWorld.OpenSession(world, 7, recording: true);

        assembly.Inject("RaiseOrder", "甲|GoTo|0.4|1|2", out var reason).ShouldBeFalse();
        assembly.Journal.ShouldBeEmpty("它没走成任何写，不是一次写");
        reason.ShouldNotBeEmpty();
    }

    [Fact]
    public void 参数给成坏样子返false不抛_ok字段是权威()
    {
        var world = World();
        using var assembly = NavReplayWorld.OpenSession(world, 7, recording: true);

        assembly.Inject(NavVerbs.StampTerrainVerb, "3|2", out var reason).ShouldBeFalse();
        reason.ShouldContain("要 4 段");
    }

    // ---------- 只读那四条的读数逐字 ----------

    [Fact]
    public void call类那四条的读数逐字()
    {
        var grid = Grid(4, 3, new NavCell(1, 1), new NavCell(2, 1));
        grid.SetPassCost(new NavCell(0, 2), 1.5);
        grid.SetDynamicCost(new NavCell(3, 2), 4.0);
        var world = WorldOn(grid);
        world.Stepper.Register(Agent("甲", 0.5, 0.5));

        NavVerbs.Dispatch(world, NavVerbs.AsciiMapVerb, "").Text
            .ShouldBe("navgrid space=竞技场 cells=4x3 cell=1 origin=0,0 static=1 dynamic=1 "
                + "图例 #地形阻挡 p静态成本高 d动态乘数高 x两者都高 .通行 ..../.##./p..d");
        NavVerbs.Dispatch(world, NavVerbs.ReadStateVerb, "甲").Text
            .ShouldBe("甲 Unset 位置 (0.5, 0.5) 朝向 0 速度 0 位移 0 状态 Unset 卡住 None/0 子步 0 "
                + "目标 (0.5, 0.5) 前视 (0.5, 0.5) 激活检查 无");
        NavVerbs.Dispatch(world, NavVerbs.ReadStateVerb, "乙").Text.ShouldBe("未知单位 id=乙");
        world.Solver.BeginStep(64);   // 每步的全局扩展上限：不开的话一发请求立刻触顶预算
        var path = NavVerbs.Dispatch(world, NavVerbs.FindPathVerb, "0.5|0.5|3.5|2.5|0").Text;
        var records = NavVerbs.Dispatch(world, NavVerbs.SolveRecordVerb, "").Text;
        path.ShouldBe("from=(0.5, 0.5) to=(3.5, 2.5) r=0 Reached/Reached 折线 3 点 "
            + "(0.5, 0.5) -> (3.5, 0.5) -> (3.5, 2.5) 扩展 10 格 总成本 8",
            "算路径是读：折线、裁决、扩展格数、总成本四列都在，且不带 ok 字段");
        records.ShouldBe(
            "solve kept=1 dropped=0 容量=16\n"
            + "#1 竞技场 起点 (0.5, 0.5) 目标 (3.5, 2.5) 半径 0 [1/1] Reached/Reached 部分 否 扩展 10 总成本 8 "
            + "拐点 3 格路 6 预算 每请求 4000 本步剩 54 缓存 未命中 触发 None 重算第 1 次 到位 无",
            "账本读数带头一行 kept / dropped —— 「账本里没有」≠「没发生」（「三条铁律」 ③）");
    }

    // ---------- 检查点快照 ----------

    [Fact]
    public void 网格净空两版本逐字进检查点快照()
    {
        var grid = Grid(4, 3, new NavCell(1, 1), new NavCell(2, 1));
        grid.SetPassCost(new NavCell(0, 0), 2.5);
        grid.SetDynamicCost(new NavCell(3, 2), 4.0);

        NavGridSnapshot.Render(grid).ShouldBe(
            "navgrid space=竞技场 cells=4x3 cell=1 origin=0,0 static=1 dynamic=1 "
            + "blocked=0000/0110/0000 "
            + "pass=2.5,1,1,1/1,1,1,1/1,1,1,1 "
            + "dyn=1,1,1,1/1,1,1,1/1,1,1,4 "
            + "clearance=1,1,1,1/1,0,0,1/1,1,1,1",
            "阻挡、两档成本、净空、两个版本，一个都不省；数字走 ReplayArgs 的往返格式");
    }

    [Fact]
    public void 写入口动哪个版本_另一档一个数都不动()
    {
        var grid = Grid(4, 3);

        NavTerrain.Apply(grid, new NavCell(1, 1), NavTerrainLayer.Blocked, 1.0)
            .Detail.ShouldBe("cell=(1, 1) static=1 dynamic=0");
        NavTerrain.Apply(grid, new NavCell(0, 0), NavTerrainLayer.PassCost, 1.5)
            .Detail.ShouldBe("cell=(0, 0) static=2 dynamic=0", "静态成本只动静态版本");
        NavTerrain.Apply(grid, new NavCell(3, 2), NavTerrainLayer.DynamicCost, 4.0)
            .Detail.ShouldBe("cell=(3, 2) static=2 dynamic=1", "动态层只动动态版本");

        grid.SetBlocked(new NavCell(1, 1), true);
        grid.StaticVersion.ShouldBe(2, "值没真变就不动版本号 —— 让 agent 问「为什么路径没重算」时得到一个干净的答案");
    }

    [Fact]
    public void 同一份阻挡表造两张网格_快照段逐字相等()
    {
        var a = Grid(6, 6, new NavCell(2, 1), new NavCell(3, 1));
        var b = Grid(6, 6, new NavCell(2, 1), new NavCell(3, 1));

        NavGridSnapshot.Render(a).ShouldBe(NavGridSnapshot.Render(b), "重放要落到逐字那一档（「断言三层」 的 L3）");
    }

    [Fact]
    public void 全量快照的段格式对得上取证器的契约_头部以tick起段间竖线()
    {
        var world = World(4, 3);
        world.Stepper.Register(Agent("甲", 0.5, 0.5));
        world.Advance(2);

        var segments = world.ReplaySnapshot().Split(" | ");
        segments[0].ShouldBe("tick=2 seconds=0.2 space=竞技场 step=2 agents=1");
        segments[1].ShouldStartWith("navgrid space=竞技场 cells=4x3", Case.Sensitive);
        segments[2].ShouldBe("pathfinding space=竞技场 step=2 agents=1");
        segments[3].ShouldStartWith("甲 Unset 位置 (0.5, 0.5)", Case.Sensitive);
    }

    // ---------- 建造接线 ----------

    [Fact]
    public void 建造建成与移除自动落StampTerrain_端到端()
    {
        var grid = Grid(6, 6);
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));
        var core = new ConstructionCore(new GridPlacementRule(1, 0, 0, 6, 6), Blueprints());
        link.Attach(core.Events);

        var placed = core.Commit("墙", new System.Numerics.Vector2(2.5f, 1.5f), 0, "w1");
        placed.Ok.ShouldBeTrue();

        var stamps = link.Pump();
        stamps.Count.ShouldBe(4, "放下与建成各落一遍，一栋两格 ⇒ 四枚");
        stamps[0].ShouldBe(new NavTerrainStamp("placed", "w1", new NavCell(2, 1), true));
        stamps[1].ShouldBe(new NavTerrainStamp("placed", "w1", new NavCell(3, 1), true));
        stamps[2].ShouldBe(new NavTerrainStamp("completed", "w1", new NavCell(2, 1), true),
            "建成那一发不带格名单，靠自己落印子时记下的那几格重落一遍");
        stamps[3].ShouldBe(new NavTerrainStamp("completed", "w1", new NavCell(3, 1), true));
        grid.IsBlocked(new NavCell(2, 1)).ShouldBeTrue("漏了这一类等于墙不存在");
        grid.IsBlocked(new NavCell(3, 1)).ShouldBeTrue();
        grid.StaticVersion.ShouldBe(2, "建成那一遍落的是同一个值，写入口是空操作，版本号一个都不动");
        grid.Clearance.At(2, 2).ShouldBe(1, "净空场跟着地形一起重算：净空是阻挡的推论");
        link.CellsOf("w1").ShouldBe(new[] { new NavCell(2, 1), new NavCell(3, 1) });
        NavGridSnapshot.Render(grid).ShouldContain("blocked=000000/001100/000000/000000/000000/000000");

        core.Remove("w1", RemovalCause.Dismantle).Ok.ShouldBeTrue();

        var off = link.Pump();
        off.Count.ShouldBe(2);
        off[0].ShouldBe(new NavTerrainStamp("removed", "w1", new NavCell(2, 1), false));
        grid.IsBlocked(new NavCell(2, 1)).ShouldBeFalse();
        grid.StaticVersion.ShouldBe(4);
        grid.Clearance.At(2, 2).ShouldBe(3, "墙拆了净空要长回去：离 (4,3) 与 (2,4) 那两个最近阻挡各 √2+1");
        link.CellsOf("w1").ShouldBeEmpty();
    }

    [Fact]
    public void 落不下的那几条是读数的一部分_不静默少落()
    {
        // 建造层那个出口是有界环形账本：两次 Pump 之间发的事件超过容量，最老的那几条就再也读不到了。
        // 那一档必须变成一个数 —— 「账本里没有」≠「没发生」（「三条铁律」 ③）。
        // 这里一栋两格建筑发两条（放下 + 建成），账本只留 2 条 ⇒ 三栋里只有最新那栋读得到。
        var grid = Grid(6, 6);
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));
        var core = new ConstructionCore(new GridPlacementRule(1, 0, 0, 6, 6), Blueprints(), eventCapacity: 2);
        link.Attach(core.Events);

        core.Commit("墙", new System.Numerics.Vector2(0.5f, 0.5f), 0, "w1");
        core.Commit("墙", new System.Numerics.Vector2(2.5f, 0.5f), 0, "w2");
        core.Commit("墙", new System.Numerics.Vector2(4.5f, 0.5f), 0, "w3");

        var stamps = link.Pump();

        link.Missed.ShouldBe(4, "w1 与 w2 那四条被环形顶掉了 —— 报出来，不当成「没砌墙」");
        stamps.Count.ShouldBe(4, "读得到的只有 w3 的放下与建成（两格建筑 ⇒ 每条事件落两枚）");
        stamps[0].Source.ShouldBe("placed");
        grid.IsBlocked(new NavCell(4, 0)).ShouldBeTrue("最新那栋照落");
        grid.IsBlocked(new NavCell(0, 0)).ShouldBeFalse("w1 那两条读不到了：症状与「没砌墙」一模一样，所以必须报数");
    }

    [Fact]
    public void 挪动那一条没有查法就计入落不下_不留撤不掉的墙()
    {
        var grid = Grid(6, 6);
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));
        var core = new ConstructionCore(new GridPlacementRule(1, 0, 0, 6, 6), Blueprints());
        link.Attach(core.Events);

        core.Commit("墙", new System.Numerics.Vector2(0.5f, 0.5f), 0, "w1");
        link.Pump();
        core.Move("w1", new System.Numerics.Vector2(4.5f, 0.5f), 0).Ok.ShouldBeTrue();
        link.Pump();

        link.Missed.ShouldBe(1, "挪动那一条事件不带格名单，没有查法就落不成 —— 报出来，不留一堵撤不掉的墙");
        grid.IsBlocked(new NavCell(0, 0)).ShouldBeFalse("旧格撤掉了");
    }

    [Fact]
    public void 挪动那一条递了查法就落得下_旧格撤新格封()
    {
        var grid = Grid(6, 6);
        var core = new ConstructionCore(new GridPlacementRule(1, 0, 0, 6, 6), Blueprints());
        var link = new NavTerrainLink(grid,
            c => new NavCell(c.Col, c.Row), id => core.Find(id)?.Cells ?? (IReadOnlyList<GridCell>)Array.Empty<GridCell>());
        link.Attach(core.Events);

        core.Commit("墙", new System.Numerics.Vector2(0.5f, 0.5f), 0, "w1");
        link.Pump();
        core.Move("w1", new System.Numerics.Vector2(4.5f, 0.5f), 0).Ok.ShouldBeTrue();
        link.Pump();

        link.Missed.ShouldBe(0);
        grid.IsBlocked(new NavCell(0, 0)).ShouldBeFalse();
        grid.IsBlocked(new NavCell(4, 0)).ShouldBeTrue();
        grid.IsBlocked(new NavCell(5, 0)).ShouldBeTrue();
    }

    // ---------- 自由放置的地形来源 ----------

    [Fact]
    public void 自由放置的建筑落阻挡_自带空表时走宿主递的寻路格查法()
    {
        // 自由放置那一侧没有建造格：事件自带的 Cells 恒为空表（给的是圆，见 PlacementVerdict.Circle）。
        // 圆 → 格的光栅化写在宿主侧（ADR 0009「换算归宿主」），层里不认识圆形占地。
        var grid = Grid(6, 6);
        var core = FreeCore();
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row), null,
            id => HostNavCellsOf(grid, core, id));
        link.Attach(core.Events);

        core.Commit("营", new System.Numerics.Vector2(2.5f, 2.5f), 0, "f1").Ok.ShouldBeTrue();

        var stamps = link.Pump();

        stamps.Count.ShouldBe(10, "放下与建成各落一遍，半径 1 米在 1 米一格上占五格 ⇒ 十枚");
        stamps.ShouldAllBe(s => s.Source == "placed" || s.Source == "completed");
        link.StampedCount.ShouldBe(10, "「已落」那一列逐枚累加");
        link.Missed.ShouldBe(0, "查法递了：放下与建成两条都落得下");
        grid.IsBlocked(new NavCell(2, 2)).ShouldBeTrue("圆心那一格");
        grid.IsBlocked(new NavCell(1, 2)).ShouldBeTrue("圆心左边那一格：离圆心正好 1 米");
        grid.StaticVersion.ShouldBe(5, "五格各封一次；建成那一遍落同一个值，一个版本都不动");
        link.CellsOf("f1").Count.ShouldBe(5);
        link.ToString().ShouldContain("已落=10 落不下=0");
    }

    [Fact]
    public void 自由放置的建筑移除后解封_占过的格回空表()
    {
        var grid = Grid(6, 6);
        var core = FreeCore();
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row), null,
            id => HostNavCellsOf(grid, core, id));
        link.Attach(core.Events);
        core.Commit("营", new System.Numerics.Vector2(2.5f, 2.5f), 0, "f1");
        link.Pump();
        grid.IsBlocked(new NavCell(2, 2)).ShouldBeTrue();

        core.Remove("f1", RemovalCause.Dismantle).Ok.ShouldBeTrue();

        var off = link.Pump();

        off.Count.ShouldBe(5, "移除那一条靠记忆表撤：五格各解封一次");
        off.ShouldAllBe(s => s.Source == "removed" && !s.Blocked);
        grid.IsBlocked(new NavCell(2, 2)).ShouldBeFalse("这一栋占过的格全解封");
        grid.IsBlocked(new NavCell(3, 2)).ShouldBeFalse();
        link.CellsOf("f1").ShouldBeEmpty("读数口回空表 = 那堵墙不在了");
        link.Missed.ShouldBe(0);
        link.ToString().ShouldContain("已落=15 落不下=0", Case.Sensitive, "十枚落下 + 五枚撤掉");
    }

    [Fact]
    public void 自由放置的登记与转方向也走查法_自带空表的三条都接上()
    {
        var grid = Grid(6, 6);
        var core = FreeCore();
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row), null,
            id => HostNavCellsOf(grid, core, id));
        link.Attach(core.Events);

        core.RegisterFixture("营", new System.Numerics.Vector2(3.5f, 2.5f), "fx1").Ok.ShouldBeTrue();

        var registered = link.Pump();

        registered.Count.ShouldBe(5, "场地建筑登记：五格各封一次");
        registered.ShouldAllBe(s => s.Source == "registered");
        link.Missed.ShouldBe(0);

        core.Rotate("fx1", 90).Ok.ShouldBeTrue();

        var turned = link.Pump();

        turned.Count.ShouldBe(10, "转方向自带的格名单也是空表：先撤旧五格，再照查法重落五格");
        turned.ShouldAllBe(s => s.Source == "rotated");
        grid.IsBlocked(new NavCell(3, 2)).ShouldBeTrue("圆形占地转方向之后照旧挡路");
        link.Missed.ShouldBe(0);
    }

    [Fact]
    public void 不递新查法时自由放置照旧_落不下的计数与旧行为一致()
    {
        // 旧行为零回归：没有新查法时，自带空表的那两条什么都不落，也不多记一笔；
        // 「建成」那一条照旧因为记忆表里没有它而计入落不下。
        var grid = Grid(6, 6);
        var core = FreeCore();
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));
        link.Attach(core.Events);

        core.Commit("营", new System.Numerics.Vector2(2.5f, 2.5f), 0, "f1");
        core.RegisterFixture("营", new System.Numerics.Vector2(4.5f, 4.5f), "fx1");

        link.Pump().ShouldBeEmpty("放下与登记两条照旧不落地形");
        link.StampedCount.ShouldBe(0, "一枚印子都没有");
        link.Missed.ShouldBe(1, "只有建成那一条落不下 —— 与旧行为同一个数");
        grid.StaticVersion.ShouldBe(0, "网格一个版本都不动");
        link.CellsOf("f1").ShouldBeEmpty();
        link.ToString().ShouldContain("已落=0 落不下=1");
    }

    [Fact]
    public void 查法答不占格的那一栋走建成链_落不下不计数()
    {
        // 查法回空表是一句明确的答案：这一栋不占格（地标与驻守点就属这一档）。
        // 它与「查法没递」是两种事实 —— 前者查过了、答案是空的；后者根本没查。
        var grid = Grid(6, 6);
        var core = FreeCore();
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row), null,
            id => Array.Empty<NavCell>());
        link.Attach(core.Events);

        core.Commit("营", new System.Numerics.Vector2(2.5f, 2.5f), 0, "f1").Ok.ShouldBeTrue();
        core.Events.All.OfType<BuildingPlaced>().Count().ShouldBe(1, "这一栋走的是「放下 → 建成」那条链");
        core.Events.All.OfType<BuildingCompleted>().Count().ShouldBe(1);

        link.Pump().ShouldBeEmpty("查法答「不占格」：一枚印子都不落");
        link.Missed.ShouldBe(0, "「不占格」是一句答案，不是漏进：落不下那一列一动不动");
        link.StampedCount.ShouldBe(0, "已落那一列也一动不动");
        grid.StaticVersion.ShouldBe(0, "网格一个版本都不动");
        link.CellsOf("f1").ShouldBeEmpty("它没占格，读数口就回空表");
        link.ToString().ShouldContain("已落=0 落不下=0");
    }

    [Fact]
    public void 不递查法时建成那一条照旧计数()
    {
        // 零回归：查法没递 ⇒ 记忆表里根本没有这一栋（第三档），「建成」照旧记一笔落不下。
        var grid = Grid(6, 6);
        var core = FreeCore();
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));
        link.Attach(core.Events);

        core.Commit("营", new System.Numerics.Vector2(2.5f, 2.5f), 0, "f1");

        link.Pump().ShouldBeEmpty("放下那一条自带空表、又没有查法：一枚印子都不落");
        link.Missed.ShouldBe(1, "记忆表里没有这一栋：建成那一条照旧记一笔");
        link.StampedCount.ShouldBe(0, "已落那一列一枚都不加");
        link.CellsOf("f1").ShouldBeEmpty();
        link.ToString().ShouldContain("已落=0 落不下=1");
    }

    [Fact]
    public void 接上那一刻之前的建造事件不回放一遍()
    {
        var grid = Grid(6, 6);
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));
        var core = new ConstructionCore(new GridPlacementRule(1, 0, 0, 6, 6), Blueprints());

        core.Commit("墙", new System.Numerics.Vector2(0.5f, 0.5f), 0, "w1");
        link.Attach(core.Events);
        link.Pump().ShouldBeEmpty("接的时候世界已经在那儿了，回放一遍等于把已有的墙再砌一次");
        grid.StaticVersion.ShouldBe(0, "寻路网格压根没听说过这件事 —— 起始地形归宿主的 SeedWorld，不归这条接线管");
    }

    // ---------- L3：录一段绕障的走位，重放端逐字全等 ----------

    [Fact]
    public void L3_录一段绕障的走位_重放端逐字全等()
    {
        // 两端起手那张网格必须同一份：录制端与重放端各跑同一个 SeedWorld，
        // 而开局就砌在 NavGridSpec.BlockedCells 里的墙两端都由那一次 SeedWorld 生成（不录）。
        // 录制期内每一次地形改动都必须走 StampTerrain —— 那才是录进 Journal 的部分。
        var wall = new NavCell[7];
        for (var x = 0; x <= 6; x++)
        {
            wall[x] = new NavCell(x, 3);
        }

        var recorded = WorldOn(Grid(8, 8));
        recorded.Stepper.Register(Agent("甲", 0.5, 0.5));
        using var assembly = NavReplayWorld.OpenSession(recorded, 7, recording: true);

        foreach (var cell in wall)
        {
            assembly.Inject(NavVerbs.StampTerrainVerb, $"{cell.X}|{cell.Z}|blocked|1").ShouldBeTrue();
        }

        assembly.Inject(NavVerbs.SetOrderVerb, "甲|GoTo|0.4|7.5|7.5").ShouldBeTrue();
        assembly.Mark("墙砌好发令");
        recorded.Advance(5);
        assembly.Mark("走了5步");

        // 中途把唯一的缺口封上：地形一变就该重算，且读数上看得见（地形无路 / 卡住步数在涨）。
        assembly.Inject(NavVerbs.StampTerrainVerb, "7|3|blocked|1").ShouldBeTrue();
        recorded.Advance(4);
        assembly.Mark("缺口封上");
        var sealed_ = recorded.Stepper.SolutionOf("甲");
        sealed_.Stuck.ShouldBe(NavStuckKind.NoTerrainRoute);
        sealed_.StuckSteps.ShouldBe(4, "封死之后卡住步数逐涨 —— 「它为什么不动」在这一档读得出原因");

        assembly.Inject(NavVerbs.StampTerrainVerb, "7|3|blocked|0").ShouldBeTrue();
        for (var i = 0; i < 16; i++)
        {
            recorded.Advance();
        }

        assembly.Mark("缺口解开");
        var arrived = recorded.Stepper.SolutionOf("甲");
        arrived.Status.ShouldBe(NavMoveStatus.Arrived, "解开之后自己走到目标");
        // 落点逐字是 (7.499999999999999, 7.5)：沿路径封顶那一档带的浮点噪声，
        // 是第三期就记下的待确认项（寻路层文档 3.12「位置那一份要等 08 号看清旧实现的读数形状再定」），
        // 所以这里判「进容差圈」而不是判逐字压在目标点上。
        recorded.Stepper.PositionOf("甲").DistanceTo(new NavPoint(7.5, 7.5)).ShouldBeLessThan(0.4);

        var script = assembly.ToScript();
        script.Checkpoints.Count.ShouldBe(4);
        assembly.Journal.Count.ShouldBe(10, "7 枚地形 + 1 条发令 + 2 次封 / 解");

        // 换一份全新世界按同一份脚本重放：检查点逐字、命令 ok 逐条一致（L3 那一档）。
        var replayed = WorldOn(Grid(8, 8));
        replayed.Stepper.Register(Agent("甲", 0.5, 0.5));
        using var replayAssembly = NavReplayWorld.OpenSession(replayed, script.Seed);
        var divergences = new List<string>();
        var compared = 0;
        ReplayRunner.Replay(script, replayAssembly.Hooks(), message =>
        {
            if (message is null)
            {
                compared++;
                return;
            }

            divergences.Add(message);
        });

        replayed.Stepper.PositionOf("甲").ShouldBe(recorded.Stepper.PositionOf("甲"));
        replayed.Grid.StaticVersion.ShouldBe(recorded.Grid.StaticVersion);
        compared.ShouldBe(script.Entries.Count + script.Checkpoints.Count,
            "命令与检查点各比一次；逐字相等是 L3 那一档，键集漂移容忍是 L2");
        divergences.ShouldBeEmpty("重放端与录制端分叉时这里逐条列出第一处差异");
        replayed.Detach();
    }

    [Fact]
    public void 重放端与录制端必须给同一份开局网格_地形相同而静态版本分叉照样判红()
    {
        // 这条是探针查出来的真分叉：录制端把墙砌在 NavGridSpec.BlockedCells 里（静态版本 0，
        // 六枚 StampTerrain 因为值没变而一个版本都不动），重放端从空网格起手（六枚各 +1）。
        // 两边**地形逐字相同**（blocked 行一模一样），只有 static= 那一列差 6 ——
        // 把它放进快照才看得见，否则「同一片地形走了不同的写入次数」在账本里查不出来。
        var recorded = WorldOn(Grid(8, 8));
        recorded.Grid.SetBlocked(new NavCell(3, 3), true);
        using var assembly = NavReplayWorld.OpenSession(recorded, 7, recording: true);
        assembly.Mark("录制端");

        var replayed = WorldOn(Grid(8, 8));
        using var replayAssembly = NavReplayWorld.OpenSession(replayed, 7);
        var divergences = new List<string>();
        ReplayRunner.Replay(assembly.ToScript(), replayAssembly.Hooks(), m =>
        {
            if (m is not null)
            {
                divergences.Add(m);
            }
        });

        replayed.Grid.IsBlocked(new NavCell(3, 3)).ShouldBeFalse("地形这一边相同，版本那一边不同");
        divergences.Count.ShouldBe(1, "静态版本从第 0 步就分叉 —— 快照里带着它正是为了在这里判红");
        recorded.Detach();
        replayed.Detach();
    }

    // ---------- 装配 ----------

    [Fact]
    public void 推进之前先读建造接线_砌墙与绕障在同一逻辑步里看得见()
    {
        var grid = Grid(8, 8);
        var core = new ConstructionCore(new GridPlacementRule(1, 0, 0, 6, 6), Blueprints());
        var link = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));
        var space = new NavSpace(new SpaceId("竞技场"), grid);
        var clock = new GameClock();
        var solver = new NavSolver(space, Budget(), Recompute(), 64, 16, 16);
        var stepper = new NavStepper(solver, MoveTuning(), clock);
        stepper.Register(Agent("甲", 0.5, 0.5));
        var world = new NavReplayWorld(space, solver, stepper, clock, link);
        link.Attach(core.Events);

        core.Commit("墙", new System.Numerics.Vector2(0.5f, 3.5f), 0, "w1");
        world.Advance(1);

        world.Tick.ShouldBe(1, "接线在推进之前读完了：第 1 步就看得见那堵墙");
        grid.IsBlocked(new NavCell(0, 3)).ShouldBeTrue();
        world.Detach();
    }

    [Fact]
    public void 暂停中推进不前进_与逻辑时钟同一口径()
    {
        var world = World();
        world.Advance(3);
        world.Clock.Pause();

        world.Advance(5).ShouldBeFalse();
        world.Tick.ShouldBe(3);
        world.AdvanceTo(10);  // 暂停中照走那一支自己兜住：收手而不是死循环
        world.Tick.ShouldBe(3);
    }

    // ---------- 摆位 ----------

    private static NavGrid Grid(int x, int z, params NavCell[] blocked) => new(new NavGridSpec
    {
        Space = new SpaceId("竞技场"),
        CellMeters = 1.0,
        CellsX = x,
        CellsZ = z,
        OriginX = 0.0,
        OriginZ = 0.0,
        BlockedCells = blocked,
    });

    private static NavSolveBudget Budget() => new() { PerRequestExpansions = 4000 };

    private static NavRecomputeTuning Recompute() => new() { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 8 };

    private static NavMoveTuning MoveTuning() => new()
    {
        StepSeconds = 0.1, LookAheadMeters = 0.5, StepSolveExpansions = 64,
    };

    private static NavAgentSpec Agent(string id, double x, double z) => new()
    {
        Id = id,
        RadiusMeters = 0.0,
        MoveSpeedMetersPerSecond = 15.0,   // 一步 1.5 米 > 1 格：绕障读得出子步与位移
        Start = new NavPoint(x, z),
        FacingDegrees = 0.0,
    };

    private static NavReplayWorld WorldOn(NavGrid grid)
    {
        var space = new NavSpace(new SpaceId("竞技场"), grid);
        var clock = new GameClock();
        var solver = new NavSolver(space, Budget(), Recompute(), 64, 16, 16);
        var stepper = new NavStepper(solver, MoveTuning(), clock);
        return new NavReplayWorld(space, solver, stepper, clock);
    }

    private static NavReplayWorld World(int x = 4, int z = 3) => WorldOn(Grid(x, z));

    private static IEnumerable<Blueprint> Blueprints()
    {
        yield return new Blueprint("墙", 10, CellFootprint.FromCells(new[] { new GridCell(0, 0), new GridCell(1, 0) }));
    }

    private static ConstructionCore FreeCore()
        => new(new FreePlacementRule(), new[] { new Blueprint("营", 100, new RadiusFootprint(1)) });

    /// <summary>假宿主那一侧的查法：这一栋在寻路网格里占哪几格。</summary>
    /// <remarks>圆 → 格的光栅化写在宿主侧（ADR 0009「换算归宿主」）：格心落在半径内就算占。
    /// 层里不认识圆形占地，也不知道半径住在蓝图里。</remarks>
    private static IReadOnlyList<NavCell> HostNavCellsOf(NavGrid grid, ConstructionCore core, string buildingId)
    {
        var building = core.Find(buildingId);
        if (building is null)
        {
            return Array.Empty<NavCell>();
        }

        var footprint = (RadiusFootprint)core.Blueprints[building.BlueprintId].Footprint;
        var cells = new List<NavCell>();
        for (var z = 0; z < grid.CellsZ; z++)
        {
            for (var x = 0; x < grid.CellsX; x++)
            {
                var dx = ((x + 0.5) * grid.CellMeters) - building.Position.X;
                var dz = ((z + 0.5) * grid.CellMeters) - building.Position.Y;
                if (Math.Sqrt((dx * dx) + (dz * dz)) <= footprint.Radius)
                {
                    cells.Add(new NavCell(x, z));
                }
            }
        }

        return cells;
    }
}
