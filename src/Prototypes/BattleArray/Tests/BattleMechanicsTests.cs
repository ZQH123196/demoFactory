#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 机制断言 —— 关掉抖动，让每个数字都能手算（这是「规则成不成立」那一层的台账）。
/// 抖动开着时的统计行为归 <see cref="PlacementComparisonTests"/>，逐字复现归 <see cref="BattleDeterminismTests"/>。
/// </summary>
public class BattleMechanicsTests
{
    /// <summary>手搓摆法：每个 placement 一格（一兵一格，全兵种一律占 1 格）。</summary>
    private static Formation Lineup(string key, params UnitPlacement[] placements)
        => new(key, key, placements);

    private static UnitPlacement At(TroopKind kind, int row, int column) => new(kind, row, column);

    [Fact]
    public void 近战一对一_赶路到够得着那一步才开手_伤害按间隔扣()
    {
        // 我方剑士（人族：170血/20伤/10步/射程 1.8×1.1＝1.98 米/半径0.45/1.5米每秒）
        //   vs 敌方蛮牛（兽人：260血/12×1.1≈13伤/14步/射程1.6 米/半径0.55/1.2米每秒）。
        // 两军前排相距 14 米，每步合近 0.27 米（剑士 0.15 ＋ 蛮牛 0.12）。
        // 剑士够得着的距离 = 1.98+0.45+0.55 = 2.98 米 ⇒ 第 41 步走完距离成 2.93 米 ⇒ 第 42 步先出手；
        //   （M4 记的那本账是射程 1.8 时第 43 步首刀 —— 差的这一步就是 M8 人族那条「手长」。）
        // 蛮牛够得着的距离 = 1.6+0.55+0.45 = 2.6 米：剑士停手后它自己每步再合 0.12 米，
        //   2.93→2.81（42 步）→2.69（43 步）→2.57（44 步）⇒ 第 45 步才开手。
        // 剑士 13 刀砍满 260 血 ⇒ 第 42+12×10 = 162 步打死蛮牛；
        // 此前蛮牛出手 45,59,…,157 共 9 刀 = 9×13 = 117 伤 ⇒ 剑士剩 170−117 = 53 血。
        using var world = new BattleWorld(
            seed: 1,
            Lineup("我", At(TroopKind.Warrior, 0, 2)),
            Lineup("敌", At(TroopKind.Tank, 0, 2)),
            jitter: false);

        world.Advance(200);

        world.Outcome.ShouldBe(BattleOutcome.PlayerVictory, "战士该赢下这单挑");
        world.OutcomeTick.ShouldBe(162, "13 刀 × 10 步间隔 ＋ 第 42 步首刀");
        world.Deaths(Side.Enemy).ShouldBe(1);
        world.Deaths(Side.Player).ShouldBe(0);

        var warrior = world.Units.Single(u => u.Side == Side.Player);
        warrior.Health.ShouldBe(53, "蛮牛只来得及出 9 刀（每刀 13，兽人那条力大已经算在里面）");
        warrior.AttacksMade.ShouldBe(13);
        warrior.DamageDealt.ShouldBe(260);
    }

    [Fact]
    public void 打最近_前排没倒之前后排不是目标()
    {
        // 敌方前排一个坦克、后排一个坦克（两名敌兵都比我方两人近的那个先倒下）。
        // 断言的是「打最近」这条唯一目标规则：前排还活着时，谁都不许转头去打后排；
        // 前排倒了，目标才换成仍然最近的活人。
        using var world = new BattleWorld(
            seed: 1,
            Lineup("我", At(TroopKind.Warrior, 0, 1), At(TroopKind.Rider, 0, 3)),
            Lineup("敌", At(TroopKind.Tank, 0, 2), At(TroopKind.Tank, 2, 2)),
            jitter: false);
        var attacks = new List<AttackRow>();
        var deaths = new List<DeathRow>();
        world.Attacked += attacks.Add;
        world.Died += deaths.Add;

        world.Advance(400);

        var ours = attacks.Where(a => a.AttackerId is "p01" or "p02").ToList();
        ours.ShouldNotBeEmpty();
        var frontDeath = deaths.First(d => d.UnitId == "e01").Tick;
        frontDeath.ShouldBeLessThan(400, "两个兵打一个坦克，400 步内该倒下");
        ours.Where(a => a.Tick <= frontDeath).All(a => a.TargetId == "e01").ShouldBeTrue("前排坦克还没倒，不该有人转头打后排");
        ours.Where(a => a.Tick > frontDeath).All(a => a.TargetId == "e02").ShouldBeTrue("前排倒了就该换成还活着的最近敌人");
        ours.Count(a => a.TargetId == "e02").ShouldNotBe(0, "换了目标就要真打出去");
    }

    [Fact]
    public void 射程差_弓手先出手_近战要走近了才还手()
    {
        // 我方弓手（射程 9 米）vs 敌方战士（射程 1.8 米）：弓手第 16 步就能打到人，
        // 战士要合到 2.65 米内才够得着 —— 那 6 米多的射程差就是「后排弓手天然被保护」的来源。
        // 取第 40 步这个点：战士还差一步（第 41 步才够着），弓手已经开了三枪。
        using var world = new BattleWorld(
            seed: 1,
            Lineup("我", At(TroopKind.Archer, 0, 2)),
            Lineup("敌", At(TroopKind.Warrior, 0, 2)),
            jitter: false);
        var attacks = new List<AttackRow>();
        world.Attacked += attacks.Add;

        world.Advance(40);

        attacks.Count.ShouldBe(3, "第 16、28、40 步各一枪");
        attacks.All(a => a.AttackerId == "p01" && a.TargetId == "e01").ShouldBeTrue("这 4 秒里只有弓手在输出");
        var warrior = world.Units.Single(u => u.Side == Side.Enemy);
        var archer = world.Units.Single(u => u.Side == Side.Player);
        warrior.Health.ShouldBe(170 - 3 * 16, "三枪各 16 伤");
        archer.Health.ShouldBe(90, "近战还没够着弓手");
    }

    [Fact]
    public void 判定即闩锁_终局之后世界不再演化()
    {
        using var world = new BattleWorld(
            seed: 1,
            Lineup("我", At(TroopKind.Rider, 0, 2)),
            Lineup("敌", At(TroopKind.Archer, 2, 2)),
            jitter: false);

        world.AdvanceUntilEnd().ShouldBeTrue("骑手 3.2 米每秒，该在 120 秒上限内追到弓手");
        var positionAtEnd = world.Units[0].Position;
        var bodyAtEnd = SnapshotBody(world.DebugSnapshot());
        var attacks = new List<AttackRow>();
        world.Attacked += attacks.Add;

        world.Advance(500);

        attacks.ShouldBeEmpty("判定后不该再有任何出手");
        world.Units[0].Position.ShouldBe(positionAtEnd, "判定后坐标冻在原地");
        SnapshotBody(world.DebugSnapshot()).ShouldBe(bodyAtEnd, "判定后每个兵的状态逐字不变");
        world.OutcomeTick.ShouldNotBeNull();
        world.Tick.ShouldBeGreaterThan(world.OutcomeTick!.Value, "判定后时间轴照走 —— 判定是记账不是暂停");
    }

    [Fact]
    public void 打满时长上限还没分胜负_按契约判我方赢并另记一笔超时()
    {
        // 八坦对八坦（镜像同摆法）：每边伤害 12/14 步、血 260，100 步时一个人都没死。
        // 这里压的是**上限**、不是规则 —— 满编 20~31 秒就打完，120 秒这条兜底线真仗碰不到，
        // 不压上限就构造不出「到点还没一方全灭」这个状态，而 「已定」第 5 条那句「到点判赢」必须能被测到。
        var eightTanks = Tanks(8);
        using var world = new BattleWorld(1, eightTanks, eightTanks, jitter: false, maxBattleTicks: 100);

        world.Advance(150);

        world.Outcome.ShouldBe(BattleOutcome.PlayerVictory, "到上限还没一方全灭，按契约判我方赢");
        world.DecidedByTimeout.ShouldBeTrue("判赢记成了超时这一笔 —— 对照报告里分不出「打光的胜」和「守到的胜」");
        world.OutcomeTick.ShouldBe(100);
        world.AliveCount(Side.Player).ShouldBe(8);
        world.AliveCount(Side.Enemy).ShouldBe(8);
    }

    [Fact]
    public void 伤害守恒_谁打了多少等于对面掉了多少血()
    {
        // L1 不变量（长跑中途与终局各查一次）：全场合一伤害 = 敌方掉血；敌方合一伤害 = 我方掉血。
        // 任何「凭空掉血 / 治疗 / 漏结算」都会在这里爆，而不是等到看报告才发现数字对不上。
        using var world = new BattleWorld(seed: 1001, Formations.ShieldWall, PlacementComparison.EnemyFormation);

        for (var block = 0; block < 20; block++)
        {
            world.Advance(BattleContract.OpeningCheckpointTicks);
            AssertConservation(world, $"（第 {world.Tick} 步）");
            if (world.Outcome != BattleOutcome.Running) break;
        }

        // 迁移前是 Is.EqualTo(A).Or.EqualTo(B).Or.EqualTo(C)：Shouldly 的 ShouldBe 返回 void，接不了 .Or. 链，拆成三条
        var expectedOutcomes = new[] { BattleOutcome.Draw, BattleOutcome.PlayerVictory, BattleOutcome.PlayerDefeat };
        expectedOutcomes.ShouldContain(_o => _o == world.Outcome, "满编对满编该有明确状态（三种终局之一，不许是别的读数）");
    }

    [Fact]
    public void 血条只减不增_阵亡者不复活也不位移()
    {
        using var world = new BattleWorld(seed: 1002, Formations.ArchersForward, PlacementComparison.EnemyFormation);
        var lastHealth = world.Units.ToDictionary(u => u.Id, u => u.Health, StringComparer.Ordinal);
        var dead = new HashSet<string>(StringComparer.Ordinal);

        for (var tick = 0; tick < 2000; tick++)
        {
            world.Advance(1);
            foreach (var unit in world.Units)
            {
                unit.Health.ShouldBeLessThanOrEqualTo(lastHealth[unit.Id], $"{unit.Id} 血条回升了");
                unit.Health.ShouldBeGreaterThanOrEqualTo(0, $"{unit.Id} 出现负血条（超杀没清零）");
                if (unit.Health == 0)
                {
                    dead.Add(unit.Id);
                    unit.DeathAnnounced.ShouldBeTrue($"{unit.Id} 阵亡没播报");
                }
                else if (dead.Contains(unit.Id))
                {
                    Assert.Fail($"{unit.Id} 死了又活");
                }
                lastHealth[unit.Id] = unit.Health;
            }
        }
    }

    [Fact]
    public void 满编十五格_一兵一格不重复_三种摆法阵容完全相同()
    {
        // 对照测试的归因前提：只有「谁在哪一格」不同。这条断言就是那个前提的回归测试。
        var expected = new Dictionary<TroopKind, int>
        {
            [TroopKind.Tank] = 4,
            [TroopKind.Warrior] = 4,
            [TroopKind.Archer] = 4,
            [TroopKind.Rider] = 3,
        };

        foreach (var formation in Formations.ComparisonSet)
        {
            formation.Count.ShouldBe(Formations.FullRosterSize, $"{formation.Key} 不是满编 15 兵");
            formation.Placements.Select(p => p.Kind).GroupBy(k => k)
                    .ToDictionary(g => g.Key, g => g.Count())
                    .ShouldBe(expected, true, $"{formation.Key} 的兵种构成与另外两档不同（键值逐项相等，字典枚举次序不算）");

            var cells = formation.Placements.Select(p => (p.Row, p.Column)).ToList();
            cells.Distinct().Count().ShouldBe(cells.Count, $"{formation.Key} 有格子被占两次");
            cells.All(c => c.Row >= 0 && c.Row < BattleContract.Rows
                                     && c.Column >= 0 && c.Column < BattleContract.Columns).ShouldBeTrue($"{formation.Key} 有格子越出 5×3 布阵区");
        }
    }

    [Fact]
    public void 拥挤分离_同格重叠会被身体半径推开()
    {
        // 两个骑手（半径 0.75）挤同一列同一行的镜像格：分离后两兵中心距 ≥ 1.5 米。
        // 弓手（半径 0.40）挤在一起只会分到 0.8 米 —— 身体半径造成的拥挤差异就在这里可读。
        using var world = new BattleWorld(
            seed: 1,
            Lineup("我", At(TroopKind.Rider, 1, 2), At(TroopKind.Rider, 1, 2)),
            Lineup("敌", At(TroopKind.Archer, 2, 0)),
            jitter: false);

        world.Advance(1);

        var a = world.Units[0];
        var b = world.Units[1];
        (a.Position - b.Position).Length().ShouldBeGreaterThanOrEqualTo(1.49f, "两具骑手身体（0.75+0.75）没被分开");
    }

    /// <summary>一排坦克摆法（按格序填满 5 列再进下一行）—— 用来造「谁也打死不了谁」的僵局。</summary>
    private static Formation Tanks(int count)
    {
        var placements = new List<UnitPlacement>();
        for (var i = 0; i < count; i++)
        {
            placements.Add(new UnitPlacement(TroopKind.Tank, i / BattleContract.Columns, i % BattleContract.Columns));
        }
        return new Formation($"tanks-{count}", $"{count} 坦克", placements);
    }

    /// <summary>快照去掉表头那一行（表头带步号，判定后时间轴照走，比对要看的是兵的状态）。</summary>
    private static string SnapshotBody(string snapshot)
        => string.Join('\n', snapshot.Split('\n').Skip(1));

    private static void AssertConservation(BattleWorld world, string context)
    {
        var ourDamageDealt = world.Units.Where(u => u.Side == Side.Player).Sum(u => (long)u.DamageDealt);
        var enemyDamageDealt = world.Units.Where(u => u.Side == Side.Enemy).Sum(u => (long)u.DamageDealt);
        var enemyHealthLost = world.Units.Where(u => u.Side == Side.Enemy)
            .Sum(u => (long)u.MaxHealth - u.Health);
        var ourHealthLost = world.Units.Where(u => u.Side == Side.Player)
            .Sum(u => (long)u.MaxHealth - u.Health);

        ourDamageDealt.ShouldBe(enemyHealthLost, $"我方造成的伤害 = 敌方掉血 {context}");
        enemyDamageDealt.ShouldBe(ourHealthLost, $"敌方造成的伤害 = 我方掉血 {context}");
    }
}
