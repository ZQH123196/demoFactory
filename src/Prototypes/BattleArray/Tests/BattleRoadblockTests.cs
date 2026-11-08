#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Tests;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 赛前路障（三层铺设 批③ ticket 06、spec Q14 + Q7）—— 排兵布阵接建造层的那一条链。
///
/// <para>这一份只钉本 ticket 新接的那条链：蓝图那几档数、布阵期那个前置条件、放置 → 占格 → 绕行、
/// 移除 → 解封、以及摆子口进 Journal 之后重放端逐字对得上账。既有摆位对照与确定性哨兵
/// 一个字都不动 —— 路障缺省是零放置（<c>BattleRoadblockTests</c> 之外的用例一局都不摆它）。</para>
///
/// <para><b>读数全从真世界与真网格上取</b>：绕行那一半比的是「那一格还挡不挡得住人」，
/// 既有路径求解记录里的扩展格数，也有真世界那一路走位；不照实现推数，也不写纸面账。</para>
///
/// <para><b>摆子口那三条（spec Q7）的账也落在这一份基线里</b>：这一局的直线上挂着一块布阵棋盘，
/// <c>place</c> / <c>move</c> / <c>remove</c> 各录一发（另加开打之后被拒的一发 <c>ok=false</c>），
/// 棋盘那一段因此在检查点里跟着变。离线这一档没有宿主，棋盘变了不换新世界
/// （那一笔归 <c>Main.OnBoardChanged</c>，不在 Journal 里）—— 所以快照上兵那一列停在入场那一刻，
/// 布阵期的真值读 <c>board</c> 那一段。</para>
/// </summary>
public class BattleRoadblockTests
{
    private const int LaneSeed = DuelRiftSetup.DefaultSeed;

    /// <summary>被挡的那一格 —— 世界 (0, 3) 那一格的格心，正落在我方骑手与敌方弓手那条直线上。</summary>
    private static readonly NavCell Blocked = BattleRoadblock.ToNavCell(new GridCell(0, 3));

    /// <summary>摆一座路障的 args（列序照 <c>ConstructionVerbs</c> 那份表：蓝图|x|y|方向|id|工期|载荷）。</summary>
    private const string PlaceArgs = "roadblock|0.5|3.5|0|-|-|-";

    /// <summary>同一格再摆一次的 args（走「拒绝也录」那一格）。</summary>
    private const string PlaceAgainArgs = "roadblock|0.5|3.5|0|-|-|-";

    /// <summary>只读裁决那一发的 args（蓝图|x|y|方向）。</summary>
    private const string CheckArgs = "roadblock|0.5|3.5|0";

    // === 蓝图那几档数 ===

    [Fact]
    public void 路障蓝图_单格网格放置_方向段恒零_工期零血量大值_没有打它的路()
    {
        var blueprint = BattleRoadblock.Blueprint;
        blueprint.Id.ShouldBe("roadblock");
        blueprint.BuildDurationTicks.ShouldBe(0L, "工期 0 步：放下那一刻就建成");
        blueprint.MaxHealth.ShouldBe(5000.0, "血量大值（占位读数：掉过血要一眼看得出来）");
        blueprint.ModelPath.ShouldBeEmpty("模型引用留空 = 这一侧不摆模型也不画幽灵（没有真模型就不画）");
        BattleRoadblock.Orientation.ShouldBe(0, "方向段恒 0（八值编码里的 rot0）");

        var footprint = blueprint.Footprint.ShouldBeOfType<CellFootprint>("网格放置吃格占地");
        footprint.Offsets.Count.ShouldBe(1, "单格：锚点 + 无偏移");
        footprint.Offsets[0].ShouldBe(new GridCell(0, 0));
        footprint.Width.ShouldBe(1);
        footprint.Height.ShouldBe(1);

        // 「档与表钉死」的宿主侧那一道：建造格罩住整个竞技场，而且两端换算落在寻路网格的角上。
        Should.NotThrow(BattleRoadblock.RequirePinnedBounds, "建造格与寻路格的两条档都对得上");
        (BattleRoadblock.MaxCol - BattleRoadblock.MinCol + 1).ShouldBe(
            BattleNavGrid.Cells, "建造格数与寻路格数同一档（1.0 米一格、24 格）");
        BattleRoadblock.ToNavCell(new GridCell(BattleRoadblock.MinCol, BattleRoadblock.MinRow))
            .ShouldBe(new NavCell(0, 0), "建造格的最小角 = 寻路网格的最小角");
        BattleRoadblock.ToNavCell(new GridCell(BattleRoadblock.MaxCol, BattleRoadblock.MaxRow))
            .ShouldBe(new NavCell(BattleNavGrid.Cells - 1, BattleNavGrid.Cells - 1), "建造格的最大角 = 寻路网格的最大角");

        // 无攻击路径：这一侧一条打建筑的写口都没接（连扣血都当场抛）。
        using var world = StraightLane();
        var sink = new BattleConstructionSink(world);
        Should.Throw<NotSupportedException>(() => sink.DamageBuilding("b1", 10.0),
            "路障没有攻击路径：谁都不打它，扣血那一条这一侧不接");
        Should.Throw<NotSupportedException>(() => sink.RegisterFixture("roadblock", 0, 0, 0, "r1", null),
            "本原型没有场地建筑：路障全部由手势摆下");
    }

    // === 前置条件 ===

    [Fact]
    public void 布阵期才让放_开打之后被拒且理由读得出()
    {
        using var world = StraightLane();
        world.Phase.ShouldBe(BattlePhase.Deploying, "这一局停在布阵期");

        DuelRiftSetup.Inject(world, ConstructionVerbs.Place, PlaceArgs, out _, out var firstReason)
            .ShouldBeTrue("布阵期摆得下");
        world.Construction.Count.ShouldBe(1);
        firstReason.ShouldBeEmpty("成了就没有理由要念");

        world.BeginBattle().ShouldContain("开局", Case.Sensitive);

        var refused = DuelRiftSetup.Inject(world, ConstructionVerbs.Place, "roadblock|-0.5|-3.5|0|-|-|-",
            out _, out var reason);
        refused.ShouldBeFalse("开打之后阵地已经定下，摆不动了");
        reason.ShouldContain("布阵期", Case.Sensitive, "拒的理由是给人看的那一句，不是一个光秃秃的 InvalidState");
        reason.ShouldContain("已经开打", Case.Sensitive);
        world.Construction.Count.ShouldBe(1, "被拒那一发一寸不动");

        var removeRefused = DuelRiftSetup.Inject(world, ConstructionVerbs.Remove, "b1|dismantle", out _, out var removeReason);
        removeRefused.ShouldBeFalse("撤也只在布阵期：阵面是同一件事");
        removeReason.ShouldContain("布阵期", Case.Sensitive);

        // 只读裁决那一行与落建口同源：预览说行、点下去被拒是这一层最贵的一种假绿。
        world.CheckRoadblockPlacement("roadblock", 0.5, 3.5, 0)
            .ShouldContain("why=", Case.Sensitive, "不在布阵期时那一行念的是同一句拒绝理由");
        world.BeginBattle().ShouldContain("没开打", Case.Sensitive, "开局口只管布阵期那一档（连点两次不该变两局）");
    }

    // === 放置 → 占格 → 绕行；移除 → 解封 ===

    [Fact]
    public void 放置后单位绕行_移除后恢复()
    {
        // 基线那一局（不摆路障）：从 (0,7) 走到 (0,-7) 那一路正穿中间那一格。
        using var plain = StraightLane();
        var plainGrid = plain.NavMove.Grid;
        var baseline = Solve(plainGrid);
        baseline.Path.CellPath.ShouldContain(Blocked, "基线那一路正穿世界 (0,3) 那一格");
        plain.Find("p01")!.Position.ShouldBe(new System.Numerics.Vector2(0f, 7f), "骑手起手站在 (0,7)");

        // 摆一座路障在那一格上：网格当场封上，求解记录里的扩展格数变多。
        using var blockedWorld = StraightLane();
        var blockedGrid = blockedWorld.NavMove.Grid;
        DuelRiftSetup.Inject(blockedWorld, ConstructionVerbs.Place, PlaceArgs).ShouldBeTrue();
        var id = blockedWorld.Construction.Ids[0];
        blockedGrid.IsBlocked(Blocked).ShouldBeTrue("建成即落阻挡（NavTerrainLink 的网格侧）");
        blockedWorld.Construction.Find(id)!.Cells.ShouldBe(new[] { new GridCell(0, 3) }, "占的就是建造格 (0,3)");

        var detour = Solve(blockedGrid);
        detour.Path.CellPath.ShouldNotContain(Blocked, "绕行：那一路被挡");
        detour.Record.ExpandedCells.ShouldBeGreaterThan(baseline.Record.ExpandedCells,
            "路径求解记录里扩展格数变多（被挡的那一格逼着求解器多算）");

        // 真世界那一局：走过去，从没落进被挡的那一格；旁边那一路走得到。
        var rider = blockedWorld.Find("p01")!;
        blockedWorld.BeginBattle();
        RideAndWatch(blockedWorld, rider, Blocked).ShouldBeFalse("真走位也绕开那一格");
        rider.Position.Y.ShouldBeLessThan(3.0f, "绕过去之后接着往敌方那侧走（不是卡在原地）");
        blockedWorld.Construction.Find(id)!.Health.ShouldBe(BattleRoadblock.MaxHealth,
            "无攻击路径：打了一整局它还是满血");

        // 基线那一局的真走位：正穿那一格 —— 被挡的确实是同一条路。
        var plainRider = plain.Find("p01")!;
        plain.BeginBattle();
        RideAndWatch(plain, plainRider, Blocked).ShouldBeTrue("基线那一局正穿那一格");

        // 移除那一半：另起一局（摆与撤都只在布阵期），解封之后又走回原路。
        using var removedWorld = StraightLane();
        var removedGrid = removedWorld.NavMove.Grid;
        DuelRiftSetup.Inject(removedWorld, ConstructionVerbs.Place, PlaceArgs).ShouldBeTrue();
        removedGrid.IsBlocked(Blocked).ShouldBeTrue();
        DuelRiftSetup.Inject(removedWorld, ConstructionVerbs.Remove, "b1|dismantle").ShouldBeTrue();
        removedGrid.IsBlocked(Blocked).ShouldBeFalse("移除即解封");
        var restored = Solve(removedGrid);
        restored.Path.CellPath.ShouldContain(Blocked, "恢复：那一格又能走了");
        restored.Record.ExpandedCells.ShouldBe(baseline.Record.ExpandedCells, "扩展格数回到基线那一份");

        var restoredRider = removedWorld.Find("p01")!;
        removedWorld.BeginBattle();
        RideAndWatch(removedWorld, restoredRider, Blocked).ShouldBeTrue("移除后恢复：真走位又走回那一格");

        TestLog.Ledger($"绕行读数（被挡的那一格 {Blocked}）：基线 扩展={baseline.Record.ExpandedCells} 格路={baseline.Path.CellPath.Count}｜" +
                       $"挡上之后 扩展={detour.Record.ExpandedCells} 格路={detour.Path.CellPath.Count}｜" +
                       $"移除之后 扩展={restored.Record.ExpandedCells} 格路={restored.Path.CellPath.Count}");
    }

    // === 地形接线（建成落阻挡 / 移除解封 / 静态阻挡表仍空）===

    [Fact]
    public void 落阻挡走地形接线_建成封格移除解封_静态阻挡表仍空()
    {
        using var world = StraightLane();
        Blocked.ShouldBe(new NavCell(12, 15), "世界 (0,3) 那一格 = 寻路格 (12,15)（差 12 格由网格原点推出来）");
        world.Terrain.StampedCount.ShouldBe(0L, "缺省零放置：一枚印子都没有");
        world.Terrain.Missed.ShouldBe(0L);

        // 静态阻挡表仍是空表：Build() 造出来的网格一格都没封（边界那一圈是网格外自带的虚拟阻挡）。
        CountBlocked(BattleNavGrid.Build()).ShouldBe(0, "静态阻挡表仍是空表 —— 阻挡全部来自运行时放置");

        DuelRiftSetup.Inject(world, ConstructionVerbs.Place, PlaceArgs).ShouldBeTrue();
        var id = world.Construction.Ids[0];

        world.Terrain.CellsOf(id).ShouldBe(new[] { Blocked }, "这一栋在寻路侧占的就是那一格");
        world.Terrain.StampedCount.ShouldBe(2L,
            "落下一枚 + 建成那一枚（工期 0 当场完工，建成那一条照记忆表重落一遍）");
        world.Terrain.Missed.ShouldBe(0L, "两条链都没落下东西");
        world.Construction.Events.Where<BuildingPlaced>().Count(e => e.Subject == id).ShouldBe(1);
        world.Construction.Events.Where<BuildingCompleted>().Count(e => e.Subject == id).ShouldBe(1);
        world.Construction.Find(id)!.State.ShouldBe(BuildState.Completed, "工期 0：放下那一刻就完工");

        DuelRiftSetup.Inject(world, ConstructionVerbs.Remove, $"{id}|dismantle").ShouldBeTrue();
        world.Terrain.CellsOf(id).ShouldBeEmpty("解封之后这一栋一枚印子都不占");
        world.Terrain.StampedCount.ShouldBe(3L, "解封那一枚也算一枚印子");
        world.Terrain.Missed.ShouldBe(0L);
        world.NavMove.Grid.IsBlocked(Blocked).ShouldBeFalse("那一格又能走了");
        world.Construction.Count.ShouldBe(0, "移除之后册里没有它");
        world.Construction.Events.Where<BuildingRemoved>().Count(e => e.Subject == id).ShouldBe(1);

        // 同一格再摆一座：占位与封格两条又都成立（移除真的腾干净了）。
        DuelRiftSetup.Inject(world, ConstructionVerbs.Place, PlaceArgs).ShouldBeTrue();
        world.Construction.Count.ShouldBe(1);
        world.NavMove.Grid.IsBlocked(Blocked).ShouldBeTrue();
        world.Terrain.StampedCount.ShouldBe(5L, "重摆那一发又落两枚（落下 + 建成）");
        world.Terrain.Missed.ShouldBe(0L, "重摆这一发也落得下");

        TestLog.Ledger($"路障印子（那一格 {Blocked}）：放一座落下 + 建成共 2 枚、移除 1 枚、重摆 2 枚，"
                       + $"合计 StampedCount={world.Terrain.StampedCount}；Missed={world.Terrain.Missed}");
    }

    // === 摆子三动词进重放（Q7）===

    [Fact]
    public void 摆子三动词进重放_写路径入录拒绝也入录_逐字重跑全等()
    {
        var script = RecordRoadblockScript();

        script.Entries.Select(e => e.Cmd).ShouldContain(ConstructionVerbs.Place, "写路径进 Journal");
        script.Entries.Select(e => e.Cmd).ShouldContain(ConstructionVerbs.Remove);
        script.Entries.ShouldContain(e => e.Cmd == ConstructionVerbs.Place && !e.Ok,
            "拒绝也录：开打之后那一发在账上是 ok=false（重放端要比它）");
        script.Entries.ShouldNotContain(e => e.Cmd == DuelRiftSetup.CheckPlacementVerb,
            "只读裁决是 call 类：注入得动、不进录（与建造层那一份 InjectNames 同口径）");

        // 摆子口那三条（spec Q7）：各录一发，另有开打之后被拒的一发 ok=false。
        script.Entries.Select(e => e.Cmd).ShouldContain(DeploymentVerbs.Place, "摆子口 place 也进 Journal");
        script.Entries.Select(e => e.Cmd).ShouldContain(DeploymentVerbs.Remove);
        script.Entries.Select(e => e.Cmd).ShouldContain(DeploymentVerbs.Move);
        script.Entries.ShouldContain(e => e.Cmd == DeploymentVerbs.Place && e.Ok, "布阵期那一发是成了的");
        script.Entries.ShouldContain(e => e.Cmd == DeploymentVerbs.Place && !e.Ok,
            "开打之后摆不动：那一发也在账上（ok=false）");

        var first = ReplayRoadblockScript(script);
        var second = ReplayRoadblockScript(script);

        first.Dump.ShouldBe(second.Dump, "同一份剧本两跑，检查点指纹逐字相同（L3）");
        first.Checkpoints.ShouldBe(second.Checkpoints, "检查点全文逐字相同");
        first.Facts.ShouldBe(second.Facts, "世界那一侧的事实逐字相同（册、被挡的格、相位、棋盘）");
        first.Checkpoints.ShouldContain("board kind=deploy cells=2/15", Case.Sensitive,
            "摆子那一发落在快照里（棋盘那一段跟着写路径变）");
        first.Checkpoints.ShouldContain("C r1c1 kind=tank", Case.Sensitive, "place 那一发读得出来");
        first.Checkpoints.ShouldContain("C r2c4 kind=tank", Case.Sensitive, "move 那一发读得出来");
        first.Facts.ShouldContain("board=1", Case.Sensitive, "撤掉那个坦克之后棋盘回到一个骑手");

        // 重放基线重录：这一版剧本里含新动词那几发（两发建造层写路径 + 一发只读查询 +
        // 摆子口三条 + 开打之后三发 ok=false），指纹那份基线里含 [建造层] 与 [布阵棋盘] 两段。
        // 行尾写死 \n（与既有那两份基线同一口径）。
        var scriptPath = ScriptPath("roadblock.json");
        var fingerprintPath = ScriptPath("roadblock.level1.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(scriptPath, ReplayScriptJson.Write(script));
        File.WriteAllText(fingerprintPath, first.Dump + "\n" + first.Checkpoints);
        var scriptText = File.ReadAllText(scriptPath);
        scriptText.ShouldContain(ConstructionVerbs.Place, Case.Sensitive, "新基线里含放置那一发");
        scriptText.ShouldContain(ConstructionVerbs.Remove, Case.Sensitive, "新基线里含移除那一发");
        scriptText.ShouldContain(ConstructionVerbs.Remove + "\",\"args\":\"b1|dismantle\",\"ok\":false",
            Case.Sensitive, "开打之后撤那一发在基线里是 ok=false（拒绝也录）");
        scriptText.ShouldContain($"\"cmd\":\"{DeploymentVerbs.Place}\"", Case.Sensitive, "新基线里含摆子口 place");
        scriptText.ShouldContain($"\"cmd\":\"{DeploymentVerbs.Remove}\"", Case.Sensitive, "新基线里含摆子口 remove");
        scriptText.ShouldContain($"\"cmd\":\"{DeploymentVerbs.Move}\"", Case.Sensitive, "新基线里含摆子口 move");
        var fingerprintText = File.ReadAllText(fingerprintPath);
        fingerprintText.ShouldContain("B b1 roadblock", Case.Sensitive, "新基线里含建造层那一段（路障那一行）");
        fingerprintText.ShouldContain("board kind=deploy", Case.Sensitive, "新基线里含布阵棋盘那一段");
        fingerprintText.ShouldContain("C r2c4 kind=tank", Case.Sensitive, "摆子三动词的痕迹也进了基线");
        TestLog.Ledger($"路障剧本与指纹基线已落盘：{scriptPath}｜{fingerprintPath}");
    }

    /// <summary>这一侧那两份基线与既有 H3 那两份同住一个目录（级 1 的 CLI 与级 2 都从这儿读）。</summary>
    private static string ScriptPath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Scripts", name));

    // === 剧本与帮手 ===

    /// <summary>
    /// 录一份路障剧本：布阵期那三发（查 / 摆 / 再摆被拒）打在同一格上，开打之后那两发按前置条件被拒；
    /// 另加摆子口那三条（<c>place</c> / <c>move</c> / <c>remove</c>，spec Q7）与开打之后被拒的一发。
    /// 三条摆子动词都走同一个 inject 口 —— 只读那一发不入录，写路径逐发入录（含 ok=false 的那几发）。
    ///
    /// <para>痕迹次序：摆一个坦克（棋盘那一列多一格，检查点里读得出来）→ 挪到后排 → 撤掉。
    /// 于是最后一个布阵期检查点上棋盘又回到那一个骑手，而中间那一格留着「写入真落下了」的读数。</para>
    /// </summary>
    private static ReplayRunner.ReplayScript RecordRoadblockScript()
    {
        using var world = StraightLane();
        using var recorder = DuelRiftSetup.Recorder(world);
        recorder.BeginSession();

        void Mark(long atTick, string label)
        {
            DuelRiftSetup.AdvanceTo(world, atTick);
            recorder.Mark(label);
        }

        void Command(long atTick, string cmd, string args)
        {
            DuelRiftSetup.AdvanceTo(world, atTick);
            recorder.Inject(cmd, args);
        }

        Mark(0, "布阵期开局");
        Command(0, DuelRiftSetup.CheckPlacementVerb, CheckArgs);
        Command(0, ConstructionVerbs.Place, PlaceArgs);
        Command(0, DuelRiftSetup.CheckPlacementVerb, CheckArgs);
        Command(0, ConstructionVerbs.Place, PlaceAgainArgs);
        Mark(0, "路障摆好之后");
        Command(0, DeploymentVerbs.Place, DeploymentVerbs.PlaceArgs("tank", 1, 1));
        Mark(0, "摆上一个坦克之后");
        Command(0, DeploymentVerbs.Move, DeploymentVerbs.MoveArgs(1, 1, 2, 4));
        Mark(0, "坦克挪到后排之后");
        Command(0, DeploymentVerbs.Remove, DeploymentVerbs.RemoveArgs(2, 4));
        Mark(0, "摆子三条都写完");
        Command(400, ConstructionVerbs.Place, "roadblock|-0.5|-3.5|0|-|-|-");
        Command(400, DeploymentVerbs.Place, DeploymentVerbs.PlaceArgs("archer", 2, 0));
        Command(600, ConstructionVerbs.Remove, "b1|dismantle");
        Mark(900, "开打之后阵面不动");
        return recorder.ToScript();
    }

    /// <summary>跑一遍剧本：第三档（逐字）比检查点，并把世界那一侧的事实一起带出来。</summary>
    private static (string Dump, string Checkpoints, string Facts) ReplayRoadblockScript(ReplayRunner.ReplayScript script)
    {
        using var world = StraightLane();
        var session = new LiveReplaySession(script, DuelRiftSetup.Hooks(world));
        session.RunAll();
        session.Divergences.ShouldBeEmpty("级 1 重放必须逐字复现录制时的每一格：\n" + string.Join("\n", session.Divergences));
        session.Commands.ShouldAllBe(c => c.Equal == true, "每一发注入命令的 ok 都照录制复现（拒绝那几发也是）");

        var facts = $"phase={world.Phase} buildings={world.Construction.Count} " +
                    $"blocked={world.NavMove.Grid.IsBlocked(Blocked)} stamped={world.Terrain.StampedCount} " +
                    $"missed={world.Terrain.Missed} board={world.Board!.Filled}/{world.Board.Remaining} " +
                    $"tick={world.Tick}";
        var checkpoints = string.Join("\n", session.Checkpoints.Select(c => $"--- {c.Label} @tick={c.Tick} ---\n{c.Actual}"));
        return (session.DumpSequence(), checkpoints, facts);
    }

    /// <summary>
    /// 直线上那一局：我方一个骑手站 (0,7)、敌方一个弓手站 (0,-7)（同列 ⇒ 基线那一路是直的）。
    /// 抖动静止、世界停在布阵期（路障那三个口只认这一档），并挂上一块同形的布阵棋盘 ——
    /// 摆子口那三条写路径的落点是它（spec Q7），初值照着入场名单摆同一个骑手。
    /// </summary>
    private static BattleWorld StraightLane()
    {
        var board = new DeploymentBoard(fillFromDefaultFormation: false);
        board.Place(TroopKind.Rider, 0, 2).Ok.ShouldBeTrue("棋盘上先摆上那一个骑手（与入场名单同一个兵）");
        return new BattleWorld(LaneSeed,
            new Formation("lane-rider", "单骑·我方", [new UnitPlacement(TroopKind.Rider, 0, 2)]),
            new Formation("lane-archer", "单弓·敌方", [new UnitPlacement(TroopKind.Archer, 0, 2)]),
            jitter: false, phase: BattlePhase.Deploying, board: board);
    }

    /// <summary>走一整局，回报「这一路有没有落进被挡的那一格」（走位读数，不猜）。</summary>
    private static bool RideAndWatch(BattleWorld world, BattleUnit rider, NavCell cell)
    {
        var entered = false;
        for (var step = 0; step < world.MaxBattleTicks && world.Outcome == BattleOutcome.Running; step++)
        {
            world.Advance(1);
            if (NavCellOf(rider.Position) == cell) entered = true;
        }

        return entered;
    }

    /// <summary>世界坐标落在寻路网格的哪一格（1 米一格、原点在角上 ⇒ 下标就是 floor）。</summary>
    private static NavCell NavCellOf(System.Numerics.Vector2 world)
    {
        var point = BattleNavGrid.ToGrid(world.X, world.Y);
        return new NavCell((int)Math.Floor(point.X), (int)Math.Floor(point.Z));
    }

    /// <summary>
    /// 在<b>这一局那张网格上</b>独立解一次路（骑手从 (0,7) 到 (0,-7)）——
    /// 同一份求解器配置（与 <see cref="BattleNavMove"/> 同一组数），所以扩展格数可比。
    /// </summary>
    private static (NavPath Path, NavSolveRecord Record) Solve(NavGrid grid)
    {
        var solver = new NavSolver(
            new NavSpace(BattleNavGrid.Space, grid),
            new NavSolveBudget { PerRequestExpansions = BattleNavMove.PerRequestExpansions },
            new NavRecomputeTuning { GoalMoveToleranceMeters = 0.5, NotArrivedStepCap = 30 },
            cacheCapacity: 64,
            recordCapacity: 64,
            trackedRequestCapacity: 32);
        solver.BeginStep(BattleNavMove.StepSolveExpansions);

        var path = solver.Solve(
            BattleNavGrid.ToGrid(0f, 7f),
            BattleNavGrid.ToGrid(0f, -7f),
            BattleNavGrid.RadiusOf(TroopKind.Rider));
        return (path, solver.Records.Last!);
    }

    /// <summary>这张网格上封了几格（判「静态阻挡表仍是空表」用）。</summary>
    private static int CountBlocked(NavGrid grid)
    {
        var count = 0;
        for (var x = 0; x < grid.CellsX; x++)
        {
            for (var z = 0; z < grid.CellsZ; z++)
            {
                if (grid.IsBlocked(new NavCell(x, z))) count++;
            }
        }

        return count;
    }
}
