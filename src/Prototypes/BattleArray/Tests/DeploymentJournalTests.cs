#nullable enable
using System;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BattleArray.Logic;
using DemoFactory.GugaFramework.Tests;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 摆子口三条写路径进 Journal（三层铺设 spec Q7）—— <c>place</c> / <c>remove</c> / <c>move</c>。
///
/// <para>这一份只钉这一张 ticket 新接的那条链：词表与参数编码那一份（<see cref="DeploymentVerbs"/>）、
/// 世界那个写入口上的前置条件、以及「录进去 → 重放逐字复现」那一段。路障那一张 ticket 的基线
/// （<c>Tests/Scripts/roadblock.json</c>）由 <c>BattleRoadblockTests</c> 自己重录，
/// 既有那两份逐字基线（<c>duel-rift-h3</c>）一个字都不动。</para>
///
/// <para><b>离线这一档谁来重摆世界</b>：棋盘一变就换一份新世界那一笔归宿主
/// （<c>Main.OnBoardChanged</c>），而宿主的动作不在 Journal 里 —— 所以离线这一档重放的是
/// 棋盘的账（快照里 <c>board</c> 那一段），兵那一列停在入场那一刻。</para>
/// </summary>
public class DeploymentJournalTests
{
    private const int Seed = DuelRiftSetup.DefaultSeed;

    // === 词表与参数编码 ===

    [Fact]
    public void 摆子三动词_参数编码走ReplayArgs那一份()
    {
        DeploymentVerbs.Names.ShouldBe(new[] { "place", "remove", "move" }, "动词名照 spec Q7 那三个写法");
        DeploymentVerbs.PlaceArgs("archer", 2, 0).ShouldBe("archer|2|0", "竖线分段、位置固定");
        DeploymentVerbs.RemoveArgs(1, 2).ShouldBe("1|2");
        DeploymentVerbs.MoveArgs(0, 2, 2, 4).ShouldBe("0|2|2|4");
        DuelRiftSetup.VerbNames.ShouldContain(DeploymentVerbs.Place, "三条都在这一侧的词表里");
        DuelRiftSetup.VerbNames.ShouldContain(DeploymentVerbs.Remove);
        DuelRiftSetup.VerbNames.ShouldContain(DeploymentVerbs.Move);

        // 折 token 那一半：兵种键认大小写，账上只留 KeyOf 那一种写法。
        var call = DeploymentVerbs.Parse(DeploymentVerbs.Place, "TANK|1|1", out var why);
        why.ShouldBeEmpty("读得动就没有理由要念");
        call.ShouldNotBeNull();
        call!.Kind.ShouldBe(TroopKind.Tank);
        call.CanonicalArgs.ShouldBe("tank|1|1", "Journal 记的是折过的那一种 token");

        DeploymentVerbs.Parse(DeploymentVerbs.Remove, "007|2", out _)!.CanonicalArgs
            .ShouldBe("7|2", "数字走 InvariantCulture 那一份格式");
    }

    [Fact]
    public void 摆子三动词_参数坏样子判否不抛且原样入录()
    {
        using var world = DeployLane(out _);

        // 兵种键不认识：打回可选清单，不静默兜一个（「桥只转发」那句「不兜 0」的同款）。
        DuelRiftSetup.Inject(world, DeploymentVerbs.Place, "tankk|1|1", out var canon, out var why)
            .ShouldBeFalse();
        why.ShouldContain("兵种键读不懂", Case.Sensitive);
        why.ShouldContain("tank / warrior / archer / rider", Case.Sensitive, "可选清单要打在理由里");
        canon.ShouldBe("tankk|1|1", "解析不动时原样入录：它照样是一条 ok=false 的账");

        // 段数不符：三条各报自己那一段数。
        DuelRiftSetup.Inject(world, DeploymentVerbs.Place, "tank|1", out _, out var placeWhy).ShouldBeFalse();
        placeWhy.ShouldContain("3 段", Case.Sensitive);
        DuelRiftSetup.Inject(world, DeploymentVerbs.Remove, "1", out _, out var removeWhy).ShouldBeFalse();
        removeWhy.ShouldContain("2 段", Case.Sensitive);
        DuelRiftSetup.Inject(world, DeploymentVerbs.Move, "0|2|2", out _, out var moveWhy).ShouldBeFalse();
        moveWhy.ShouldContain("4 段", Case.Sensitive);

        // 读不懂的格号：判否，不抛（抛出去会让录进去的东西有一类重放不了）。
        DuelRiftSetup.Inject(world, DeploymentVerbs.Move, "0|2|x|4", out _, out var badCell).ShouldBeFalse();
        badCell.ShouldContain("读不懂", Case.Sensitive);

        // 未知动词当场抛（口径 ①：不在重放里静默少打一笔）。
        Should.Throw<InvalidOperationException>(() => DeploymentVerbs.Parse("place_unit", "tank|1|1", out _));
        world.Board!.Filled.ShouldBe(1, "判否的那几发一寸不动");
    }

    // === 三条写路径与那两道关 ===

    [Fact]
    public void 摆子三动词_布阵期三发都写进棋盘()
    {
        using var world = DeployLane(out var board);
        board.Filled.ShouldBe(1, "开局棋盘上只有那一个骑手");

        DuelRiftSetup.Inject(world, DeploymentVerbs.Place, "tank|1|1", out var placedCanon, out var why)
            .ShouldBeTrue("布阵期摆得下");
        why.ShouldBeEmpty("成了就没有理由要念");
        placedCanon.ShouldBe("tank|1|1");
        board.Filled.ShouldBe(2);
        board.At(1, 1).ShouldBe(TroopKind.Tank, "写路径落在棋盘那一格上（世界那个写入口转发过去）");

        DuelRiftSetup.Inject(world, DeploymentVerbs.Move, "1|1|2|4").ShouldBeTrue("挪位：钱不动");
        board.At(1, 1).ShouldBeNull("挪走之后那一格空了");
        board.At(2, 4).ShouldBe(TroopKind.Tank);

        DuelRiftSetup.Inject(world, DeploymentVerbs.Remove, "2|4").ShouldBeTrue("撤子退钱");
        board.Filled.ShouldBe(1);
        board.Remaining.ShouldBe(board.StartingPurse - BattleContract.PriceOf(TroopKind.Rider),
            "撤掉坦克之后钱回到只剩骑手那一份");
        world.PlaceUnit(TroopKind.Archer, 0, 0).Ok.ShouldBeTrue("世界那个写入口本身也是写口（调试口与鼠标走它）");
        board.At(0, 0).ShouldBe(TroopKind.Archer);
    }

    [Fact]
    public void 摆子三动词_只在布阵期_开打之后三发都拒且理由读得出()
    {
        using var world = DeployLane(out var board);
        world.Phase.ShouldBe(BattlePhase.Deploying);
        world.BeginBattle().ShouldContain("开局", Case.Sensitive);
        var filled = board.Filled;

        foreach (var (cmd, args) in new[]
                 {
                     (DeploymentVerbs.Place, "tank|1|1"),
                     (DeploymentVerbs.Remove, "0|2"),
                     (DeploymentVerbs.Move, "0|2|2|4"),
                 })
        {
            DuelRiftSetup.Inject(world, cmd, args, out _, out var why).ShouldBeFalse($"{cmd} 开打之后不接");
            why.ShouldContain("布阵期", Case.Sensitive, "拒的理由是给人看的那一句，不是一个光秃秃的 InvalidState");
            why.ShouldContain("已经开打", Case.Sensitive);
        }

        board.Filled.ShouldBe(filled, "被拒的那三发一寸不动");
        board.At(0, 2).ShouldBe(TroopKind.Rider, "骑手还在原格");
    }

    [Fact]
    public void 没挂棋盘的局_摆子口拒绝且理由读得出()
    {
        // 布阵期但没有棋盘：门槛两道里第二道（单挑局与预设摆法那两档就是这一档）。
        using var world = new BattleWorld(Seed,
            new Formation("no-board-ours", "单骑·我方", [new UnitPlacement(TroopKind.Rider, 0, 2)]),
            new Formation("no-board-theirs", "单弓·敌方", [new UnitPlacement(TroopKind.Archer, 0, 2)]),
            jitter: false, phase: BattlePhase.Deploying);
        world.Board.ShouldBeNull();

        DuelRiftSetup.Inject(world, DeploymentVerbs.Place, "tank|0|0", out _, out var why).ShouldBeFalse();
        why.ShouldContain("没挂布阵棋盘", Case.Sensitive);
    }

    // === 录 → 重放：三条写路径进 Journal，检查点里读得出来 ===

    [Fact]
    public void 摆子三动词进Journal_重放逐字复现_棋盘那一段跟着变()
    {
        var script = RecordDeployScript(out var recordedBoard);

        // 录了什么：三条各一发（都成了），外加开打之后那一发 ok=false（拒绝也录）。
        script.Entries.Select(e => e.Cmd).ShouldContain(DeploymentVerbs.Place, "place 进了 Journal");
        script.Entries.Select(e => e.Cmd).ShouldContain(DeploymentVerbs.Remove);
        script.Entries.Select(e => e.Cmd).ShouldContain(DeploymentVerbs.Move);
        script.Entries.ShouldContain(e => e.Cmd == DeploymentVerbs.Place && e.Ok,
            "摆那一发是打成的那一档");
        script.Entries.ShouldContain(e => e.Cmd == DeploymentVerbs.Place && !e.Ok,
            "开打之后那一发在账上是 ok=false（重放端要比它）");
        script.Entries.Single(e => e.Cmd == DeploymentVerbs.Move).Args.ShouldBe("0|2|2|4",
            "Journal 记的是规范化之后的那一串");

        var first = ReplayDeployScript(script);
        var second = ReplayDeployScript(script);

        first.Dump.ShouldBe(second.Dump, "同一份剧本两跑，检查点指纹逐字相同（L3）");
        first.CheckpointLines.ShouldBe(second.CheckpointLines, "检查点全文逐字相同");
        first.CommandLines.ShouldBe(second.CommandLines, "每发命令照录制复现的那一行逐字相同");
        first.CommandLines.ShouldContain(line => line.Contains("重放 ok=True", StringComparison.Ordinal),
            "成了的那几发在重放端也回 true");
        first.CommandLines.ShouldContain(line => line.Contains("重放 ok=False", StringComparison.Ordinal),
            "拒的那一发在重放端也回 false（任何档位都红的那一条）");

        // 棋盘那一段在检查点里跟着写路径变 —— 重放比的是它，所以「漏录一次」当场就分叉。
        var afterPlace = first.CheckpointLines.Single(
            line => line.StartsWith("摆上一个坦克之后", StringComparison.Ordinal));
        afterPlace.ShouldContain("board kind=deploy cells=2/15", Case.Sensitive, "place 那一发在快照里读得出来");
        afterPlace.ShouldContain("C r1c1 kind=tank", Case.Sensitive);
        var afterBoth = first.CheckpointLines.Single(
            line => line.StartsWith("撤掉又挪走之后", StringComparison.Ordinal));
        afterBoth.ShouldContain("board kind=deploy cells=1/15", Case.Sensitive, "remove 那一发也读得出来");
        afterBoth.ShouldContain("C r2c4 kind=rider", Case.Sensitive, "move 那一发也读得出来");

        // 重放端那一局的棋盘落在同一处 —— 账不是只对上文字，世界那一侧也对得上。
        first.Board.ShouldBe(second.Board, "两跑之后棋盘的读数逐字相同");
        first.Board.ShouldBe(recordedBoard, "重放端与录制端收在同一份棋盘上");

        TestLog.Ledger("摆子口重放读数：\n" + string.Join("\n", first.CommandLines)
            + "\n检查点：\n" + string.Join("\n", first.CheckpointLines) + "\n重放端棋盘：" + first.Board);
    }

    // === 帮手 ===

    /// <summary>布阵期那一局：一个骑手在 (0,2)，棋盘上摆的是同一个兵（棋盘是布阵期的真值）。</summary>
    private static BattleWorld DeployLane(out DeploymentBoard board)
    {
        board = new DeploymentBoard(fillFromDefaultFormation: false);
        board.Place(TroopKind.Rider, 0, 2).Ok.ShouldBeTrue("开局棋盘先摆上那一个骑手");
        return new BattleWorld(Seed,
            new Formation("deploy-rider", "单骑·我方", [new UnitPlacement(TroopKind.Rider, 0, 2)]),
            new Formation("deploy-archer", "单弓·敌方", [new UnitPlacement(TroopKind.Archer, 0, 2)]),
            jitter: false, phase: BattlePhase.Deploying, board: board);
    }

    /// <summary>
    /// 录一份摆子剧本：布阵期那三发（摆 / 撤 / 挪，都成了），开打之后那一发 <c>place</c> 被前置条件拒。
    /// 检查点抓在「摆完之后」与「撤挪完之后」，于是棋盘那一段在快照里读得出来。
    /// </summary>
    private static ReplayRunner.ReplayScript RecordDeployScript(out string recordedBoard)
    {
        using var world = DeployLane(out var board);
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
        Command(0, DeploymentVerbs.Place, DeploymentVerbs.PlaceArgs("tank", 1, 1));
        Mark(0, "摆上一个坦克之后");
        Command(0, DeploymentVerbs.Remove, DeploymentVerbs.RemoveArgs(1, 1));
        Command(0, DeploymentVerbs.Move, DeploymentVerbs.MoveArgs(0, 2, 2, 4));
        Mark(0, "撤掉又挪走之后");
        Command(120, DeploymentVerbs.Place, DeploymentVerbs.PlaceArgs("archer", 2, 0));
        recordedBoard = board.DebugSnapshot().Split('\n')[0];
        return recorder.ToScript();
    }

    /// <summary>跑一遍剧本：逐字档比检查点，并把棋盘读数与每一发的重放行一起带出来。</summary>
    private static (string Dump, string[] CheckpointLines, string Board, string[] CommandLines) ReplayDeployScript(
        ReplayRunner.ReplayScript script)
    {
        using var world = DeployLane(out var board);
        var session = new LiveReplaySession(script, DuelRiftSetup.Hooks(world));
        session.RunAll();
        session.Divergences.ShouldBeEmpty("级 1 重放必须逐字复现录制时的每一格：\n"
            + string.Join("\n", session.Divergences));
        session.Commands.ShouldAllBe(c => c.Equal == true, "每一发注入命令的 ok 都照录制复现（拒绝那几发也是）");

        // 一行一个检查点（标签在前、快照在后），逐字可比也按标签直接找得到。
        var checkpoints = session.Checkpoints
            .Select(c => $"{c.Label} @tick={c.Tick} {c.Actual}")
            .ToArray();
        var lines = session.Commands
            .Select(c => $"命令 {c.Cmd}({c.Args}) 录制 ok={c.ExpectedOk} 重放 ok={c.ActualOk}")
            .ToArray();
        return (session.DumpSequence(), checkpoints, board.DebugSnapshot().Split('\n')[0], lines);
    }
}
