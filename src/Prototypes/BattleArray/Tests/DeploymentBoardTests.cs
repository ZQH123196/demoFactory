#nullable enable
using System.Linq;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// M6-a 的账：布阵棋盘（摆子/撤子/挪位/钱）＋ 格子与坐标互逆换算 ＋ 一局走到哪一档（布阵/开打/已判定）。
///
/// 这一包把「玩家摆的阵」接进了 M4 那台自动战斗，所以最要紧的一条是<b>等价性</b>：
/// 棋盘摆出来的阵开局，必须与「直接把摆法递给世界」逐字同一局 ——
/// 否则布阵 UI 一上，M4 那条判绿就不再是同一个机制了。
/// </summary>
public class DeploymentBoardTests
{
    [Fact]
    public void 摆一个兵到指定格_棋盘上就是它_回执说清花了多少()
    {
        var board = new DeploymentBoard(fillFromDefaultFormation: false);

        var outcome = board.Place(TroopKind.Tank, row: 0, column: 2);

        outcome.Ok.ShouldBeTrue(outcome.Reason);
        board.At(0, 2).ShouldBe(TroopKind.Tank);
        board.Filled.ShouldBe(1);
        outcome.Reason.ShouldContain("盾卫", Case.Sensitive, "回执要报得出人看得懂的那两个字（中文名来自兵种卡）");
        board.Remaining.ShouldBe(board.StartingPurse - BattleContract.PriceOf(TroopKind.Tank));
    }

    [Fact]
    public void 同一格摆第二个兵_被拒且理由说清站着谁()
    {
        var board = new DeploymentBoard(fillFromDefaultFormation: false);
        board.Place(TroopKind.Tank, 1, 3);

        var second = board.Place(TroopKind.Archer, 1, 3);

        second.Ok.ShouldBeFalse("一格只站一个兵（5.1 节占格：全兵种一律 1 格）");
        second.Reason.ShouldContain("盾卫", Case.Sensitive);
        second.Reason.ShouldContain("1,3", Case.Sensitive, "拒绝必须带理由，不许静默换格或覆盖");
        board.At(1, 3).ShouldBe(TroopKind.Tank, "被拒的那一笔不该改动棋盘");
    }

    [Fact]
    public void 行列号越界_被拒且理由给出合法范围()
    {
        var board = new DeploymentBoard(fillFromDefaultFormation: false);

        var outOfRange = board.Place(TroopKind.Warrior, row: BattleContract.Rows, column: 0);

        outOfRange.Ok.ShouldBeFalse();
        outOfRange.Reason.ShouldContain("越界", Case.Sensitive);
        outOfRange.Reason.ShouldContain($"0~{BattleContract.Rows - 1}", Case.Sensitive);
        board.Filled.ShouldBe(0);
    }

    [Fact]
    public void 最贵的兵种摆满15格_钱正好够()
    {
        // 2 节 第 8 条「钱够买满格子」的硬面：拿最贵的兵种摆满也不能差一分钱，
        // 于是初始额度写成 15 × 最贵单价（代码算），将来调价差也不会把这条买坏。
        var board = new DeploymentBoard(fillFromDefaultFormation: false);
        var riderPrice = BattleContract.PriceOf(TroopKind.Rider);
        riderPrice.ShouldBe(BattleContract.Kinds.Max(BattleContract.PriceOf), "本条压的是「最贵」那一档，改了谁最贵就要重看这条");

        for (var row = 0; row < BattleContract.Rows; row++)
        {
            for (var column = 0; column < BattleContract.Columns; column++)
            {
                board.Place(TroopKind.Rider, row, column).Ok.ShouldBeTrue($"({row},{column}) 摆第 {board.Filled + 1} 个骑手被拒了");
            }
        }

        board.Filled.ShouldBe(DeploymentBoard.CellCount);
        board.Remaining.ShouldBe(0);
        DeploymentBoard.CellCount.ShouldBe(BattleContract.Rows * BattleContract.Columns);
    }

    [Fact]
    public void 撤掉一个兵_钱退回额度()
    {
        var board = new DeploymentBoard(fillFromDefaultFormation: false);
        var price = BattleContract.PriceOf(TroopKind.Archer);
        board.Place(TroopKind.Archer, 2, 0);
        var afterPlace = board.Remaining;

        var removed = board.Remove(2, 0);

        removed.Ok.ShouldBeTrue(removed.Reason);
        board.At(2, 0).ShouldBeNull();
        board.Remaining.ShouldBe(afterPlace + price);
    }

    [Fact]
    public void 把兵挪到空格_钱一分没动()
    {
        // 买过一次就不再收钱：挪位是「换个位置」这个玩家动作，不是「再买一个」。
        var board = new DeploymentBoard(fillFromDefaultFormation: false);
        board.Place(TroopKind.Rider, 0, 0);
        var before = board.Remaining;

        var moved = board.Move(0, 0, 2, 4);

        moved.Ok.ShouldBeTrue(moved.Reason);
        board.At(0, 0).ShouldBeNull();
        board.At(2, 4).ShouldBe(TroopKind.Rider);
        board.Remaining.ShouldBe(before);
    }

    [Fact]
    public void 挪进已占的格_被拒且两边都没动()
    {
        var board = new DeploymentBoard(fillFromDefaultFormation: false);
        board.Place(TroopKind.Tank, 0, 0);
        board.Place(TroopKind.Warrior, 1, 1);

        var blocked = board.Move(0, 0, 1, 1);

        blocked.Ok.ShouldBeFalse(blocked.Reason);
        board.At(0, 0).ShouldBe(TroopKind.Tank);
        board.At(1, 1).ShouldBe(TroopKind.Warrior);
    }

    [Fact]
    public void 默认棋盘就是常规线_与M4那一档一字不差()
    {
        var board = new DeploymentBoard();

        board.Placements.Select(p => (p.Kind, p.Row, p.Column)).ShouldBe(Formations.ShieldWall.Placements.Select(p => (p.Kind, p.Row, p.Column)), "布阵期一进来看到的阵，必须就是对照跑里那条常规线 —— 否则「开局看到的」与「判绿用的」是两份账");
        board.Filled.ShouldBe(Formations.FullRosterSize);
    }

    [Fact]
    public void 格中心反算回同一格_两侧都要对()
    {
        foreach (var side in new[] { Side.Player, Side.Enemy })
        {
            for (var row = 0; row < BattleContract.Rows; row++)
            {
                for (var column = 0; column < BattleContract.Columns; column++)
                {
                    var center = BattleContract.CellCenter(side, row, column);
                    BattleContract.TryCellFromWorldPoint(center.X, center.Y, out var gotSide, out var gotRow, out var gotColumn).ShouldBeTrue($"{side} ({row},{column}) 的格中心 ({center.X},{center.Y}) 居然算不回自己这格");
                    (gotSide, gotRow, gotColumn).ShouldBe((side, row, column));
                }
            }
        }
    }

    [Fact]
    public void 接敌带与场外落点_不认格子()
    {
        // 中间 12 米接敌带不属于任何布阵格：拖到那儿就该没反应，不许兜到最近的格（5.1 节）。
        BattleContract.TryCellFromWorldPoint(0f, 0f, out _, out _, out _).ShouldBeFalse("战场正中还接敌呢");
        BattleContract.TryCellFromWorldPoint(0f, 5.9f, out _, out _, out _).ShouldBeFalse("差一点就到前排格，也还是接敌带");
        BattleContract.TryCellFromWorldPoint(0f, 12f, out _, out _, out _).ShouldBeFalse("后排格的远边算出界");
        BattleContract.TryCellFromWorldPoint(5f, 7f, out _, out _, out _).ShouldBeFalse("布阵区横向半宽是 5 米，正好落在边线上算外面");
        BattleContract.TryCellFromWorldPoint(-5f + 0.01f, 7f, out _, out var row, out var column).ShouldBeTrue();
        (row, column).ShouldBe((0, 0));
        // 敌方侧同样认（回放与对照要用）
        BattleContract.TryCellFromWorldPoint(0f, -7f, out var enemySide, out var enemyRow, out _).ShouldBeTrue();
        (enemySide, enemyRow).ShouldBe((Side.Enemy, 0));
    }

    [Fact]
    public void 自己摆的阵开局_演化与直接给摆法逐字相同()
    {
        // 布阵 UI 不许改机制：棋盘递进世界，与 M4 直接递一份摆法，必须跑出同一局。
        var board = new DeploymentBoard();
        using var fromBoard = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);
        using var fromPreset = new BattleWorld(1001, Formations.ShieldWall, PlacementComparison.EnemyFormation);

        fromBoard.Advance(150);
        fromPreset.Advance(150);

        fromBoard.DebugSnapshot().ShouldBe(fromPreset.DebugSnapshot(), "换了入口（棋盘 → 摆法）就不该换演化：这条红了意味着 M6 把 M4 的判绿挪了地方");
    }

    [Fact]
    public void 布阵期推六百步_血量与位置一个都没动()
    {
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation,
            phase: BattlePhase.Deploying);
        var entry = world.DebugSnapshot();

        world.Advance(600);

        world.Tick.ShouldBe(0, "布阵期时间轴是冻着的（暂停口碰不到它，只有开局口能解冻）");
        world.DebugSnapshot().ShouldBe(entry, "摆子期间兵不该偷偷打起来");
        world.Units.All(u => u.Alive).ShouldBeTrue();
    }

    [Fact]
    public void 开局口一推才开始演化_且与直接开打那一局逐字相同()
    {
        var board = new DeploymentBoard();
        using var deploying = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation,
            phase: BattlePhase.Deploying);
        using var fighting = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);

        var started = deploying.BeginBattle();

        started.ShouldContain("开局", Case.Sensitive, started);
        deploying.Phase.ShouldBe(BattlePhase.Fighting);
        deploying.Advance(150);
        fighting.Advance(150);
        deploying.DebugSnapshot().ShouldBe(fighting.DebugSnapshot(), "「布阵期开局」与「出生就在打」必须是同一局 —— 中间那 600 步空转不能留下任何痕迹");

        deploying.BeginBattle().ShouldContain("没开打", Case.Sensitive, "已开打再喊开局口，不该重开一局");
    }

    [Fact]
    public void 布阵期按暂停钮_不接起跑_只有开局口能起跑()
    {
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1, board.ToFormation(), PlacementComparison.EnemyFormation,
            phase: BattlePhase.Deploying);

        world.SetPaused(false);   // 玩家此刻点「恢复」——布阵期没有暂停可解，不该顺手把仗开打

        world.IsPaused.ShouldBeTrue("暂停口在布阵期不碰时间轴：起跑只有 BeginBattle 一条路");
        world.Phase.ShouldBe(BattlePhase.Deploying);
    }

    [Fact]
    public void 布阵期的why那句_不许念成对面已无人()
    {
        // 还没问「打谁」与「问了、对面确实没人」是两件事：快照与暂停读数都不能把前者说成后者。
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1, board.ToFormation(), PlacementComparison.EnemyFormation,
            phase: BattlePhase.Deploying);

        var why = world.WhyFor(world.Units.First(u => u.Side == Side.Player));

        why.ShouldBe("布阵期还没开打（开局才问打谁）");
        world.DebugSnapshot().ShouldContain("phase=Deploying");
    }

    [Fact]
    public void 暂停读出来的那句why_与快照里的是同一句()
    {
        // 「显示即真相只有一份实现」：暂停面板那句不许在表现层另算一套。
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);
        world.Advance(150);

        foreach (var unit in world.Units.Where(u => u.Alive))
        {
            var readout = world.ReadoutFor(unit.Id);
            readout.ShouldContain(world.WhyFor(unit), Case.Sensitive, $"{unit.Id} 的暂停读数与快照 why= 不是同一句");
            readout.ShouldContain(unit.Id, Case.Sensitive);
            readout.ShouldContain(unit.Look.DisplayName, Case.Sensitive);
        }

        world.ReadoutFor("zz99").ShouldContain("查无此兵");
    }

    [Fact]
    public void 打完判定_阶段落到已判定那一档()
    {
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);

        var ended = world.AdvanceUntilEnd();

        ended.ShouldBeTrue();
        world.Phase.ShouldBe(BattlePhase.Settled, "结算面板与「摆子口该不该再管事」都读这一档");
        world.Outcome.ShouldNotBe(BattleOutcome.Running);
    }

    // === M6 运行时实测抓出来的四处，逐一钉住（改完复测记在 `docs/prototypes/battle-array/灰盒阶段结论.md`「M6 已完成」） ===

    [Fact]
    public void 暂停之后单步口照走一步_且不会把暂停悄悄解除()
    {
        // 「随时暂停」的手感 = 停下来还能一格格看。暂停中 Advance 空转是正确行为（时间轴该冻），
        // 所以单步走的是 AdvanceOneDebugStep：临时解冻、推一步、再冻回去。
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);
        world.SetPaused(true);

        var tickBefore = world.Tick;
        var stepped = world.AdvanceOneDebugStep();

        stepped.ShouldBeTrue("暂停中喊单步，该走一步");
        world.Tick.ShouldBe(tickBefore + 1);
        world.IsPaused.ShouldBeTrue("单步口不该把暂停悄悄解掉 —— 解了就不是「暂停中看一步」了");

        world.SetPaused(false);
        world.AdvanceOneDebugStep().ShouldBeTrue("没暂停时单步口照走");
        world.IsPaused.ShouldBeFalse("也没把没暂停的状态改成暂停");
    }

    [Fact]
    public void 布阵期喊单步_不做事并报得出为什么()
    {
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation,
            phase: BattlePhase.Deploying);

        world.AdvanceOneDebugStep().ShouldBeFalse();
        world.Tick.ShouldBe(0);
        world.Phase.ShouldBe(BattlePhase.Deploying);
    }

    [Fact]
    public void 阵亡的人记着是被谁打倒的()
    {
        // M6 之前这里一直没写：暂停读数念出来全是「阵亡（未知击杀）」——
        // 「谁打死他」是这一局最基本的因果账，读不出来就等于观测口缺了一格。
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation);
        world.AdvanceUntilEnd();

        var dead = world.Units.Where(u => !u.Alive).ToList();
        dead.ShouldNotBeEmpty("打完一局该有阵亡（没有的话这条测试要换个种子重挑）");
        foreach (var unit in dead)
        {
            unit.KilledById.ShouldNotBeNull(); unit.KilledById.ShouldNotBeEmpty();
            world.Find(unit.KilledById).ShouldNotBeNull($"{unit.Id} 的死因指向一个不存在的兵");
            world.ReadoutFor(unit.Id).ShouldContain(unit.KilledById!, Case.Sensitive, "暂停读数要把死因一起念出来");
        }
    }

    [Fact]
    public void 布阵期的读数不许说对面已没有活人()
    {
        // 那一档还没问过「打谁」，敌人明明站在对面 —— 把「没算过」念成「没人了」就是显示不即真相。
        var board = new DeploymentBoard();
        using var world = new BattleWorld(1001, board.ToFormation(), PlacementComparison.EnemyFormation,
            phase: BattlePhase.Deploying);

        var readout = world.ReadoutFor("p01");

        readout.ShouldContain("还没开打");
        readout.ShouldNotContain("对面已没有活人");
        world.AliveCount(Side.Enemy).ShouldBe(Formations.FullRosterSize, "对面其实一个没少");
    }
}
