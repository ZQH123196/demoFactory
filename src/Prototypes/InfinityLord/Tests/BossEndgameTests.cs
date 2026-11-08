#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片 10「终局」第 4 步 — BOSS 巢穴 + 胜利判定。
///
/// 设计口径：
/// - BOSS = 巨型巢穴（终幕 6:00 远端降临），**持续刷护卫**（不清场打不进去），摧毁即判胜；
///   复用英雄攻坚链（EngageBuilding）与产出链（ScheduleBuildingSpawns）—— 攻坚与守城双重压力。
/// - BOSS 威胁按**专属系数**折算：沿用普通巢穴的 0.02 会把一头终局巨兽折成 $8 的廉价目标
///   （建筑威胁 = 血量 × 系数，血量越厚越便宜 = 自相矛盾）。
/// - BOSS 不走普通巢穴的「摧毁 → 15 秒远端重生」分支；判胜后无复活窗口。
/// - 终局判定是闩锁：先判者定，胜负不互相覆盖；时间轴照常推进（不冻结，2026-09-23 拆冻结）。
/// 验证 seam：LogicWorld 公开 API（Buildings / EstimateThreat / SuggestedPrice / DebugDamageBuilding / Outcome）。
/// </summary>
public class BossEndgameTests : LogicWorldTestBase
{
    private Building Boss() => World.Buildings.First(b => b.Kind == BuildingKind.BossLair);

    /// <summary>护卫观察窗口（60 步 = 6 秒）：比 BOSS 产间隔宽一倍，容错一次自续链调度。</summary>
    private const int GuardWindowTicks = 60;

    // === 切 1：终幕降临 ===

    [Fact]
    public void 终幕到点_BOSS巢穴远端降临()
    {
        FortifyPalace(); // 单看终幕层：排除「宫殿提前被毁 → 判负」的干扰

        Advance(LogicWorld.BossArriveTick - 1);
        World.Buildings.Any(b => b.Kind == BuildingKind.BossLair).ShouldBeFalse("提前不到场");

        Advance(2);

        var boss = Boss();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        boss.Health.ShouldBe(LogicWorld.BossLairHealth, "巨型巢穴（普通巢穴 300 的十余倍）");
        boss.Owner.ShouldBe(Faction.Monster, "怪物方 — 可被英雄攻坚、可被挂赏");
        boss.SpawnIntervalTicks.ShouldBe(LogicWorld.BossGuardSpawnIntervalTicks, "护卫产出链已起（大考实测：节奏刻意与普通巢穴同频 — 终幕的威胁来自啃不动，不是刷不完）");
        Log.BossArrivals.Count.ShouldBe(1, "降临播报一次（表现层挂终幕提示）");    }

    [Fact]
    public void BOSS降临在远端_不在玩家建筑群里长出来()
    {
        FortifyPalace();
        Advance(LogicWorld.BossArriveTick + 1);

        var boss = Boss();
        var nearest = World.Buildings
            .Where(b => b.Owner == Faction.Hero)
            .Min(b => System.Numerics.Vector2.Distance(b.Position, boss.Position));

        nearest.ShouldBeGreaterThan(LogicWorld.LairRespawnMinDistance, "终幕压力从远端压过来，给玩家留出集结窗口");
    }

    [Fact]
    public void BOSS巢穴持续刷护卫()
    {
        FortifyPalace();
        Advance(LogicWorld.BossArriveTick + 1);
        Monsters().Count(m => m.Name == "BOSS 护卫").ShouldBe(0, "降临当步还没产出");

        Advance(GuardWindowTicks);

        var guards = Monsters().Where(m => m.Name == "BOSS 护卫").ToList();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        guards.ShouldNotBeEmpty("BOSS 自带护卫产出链 — 不清场打不进去");
        guards.All(g => g.MaxHealth == 90).ShouldBeTrue("护卫数值行：血 90");
        guards.All(g => g.Attack == 13).ShouldBeTrue("护卫数值行：攻 13（比地精 5 上两个台阶）");    }

    // === 切 2：胜利判定 ===

    [Fact]
    public void 摧毁BOSS巢穴_终局判胜()
    {
        FortifyPalace();
        Advance(LogicWorld.BossArriveTick + 1);

        World.DebugDamageBuilding(Boss().Id, LogicWorld.BossLairHealth).ShouldBeTrue();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Outcome.ShouldBe(GameOutcome.Victory, "摧毁 BOSS 巢穴 = 通关");
        Log.Outcomes.ShouldBe(new[] { GameOutcome.Victory });    }

    [Fact]
    public void 判胜后BOSS不存在重生窗口_时间轴照常推进()
    {
        FortifyPalace();
        Advance(LogicWorld.BossArriveTick + 1);
        var bossId = Boss().Id;

        World.DebugDamageBuilding(bossId, 99999);
        Advance(300); // 普通巢穴的重生延迟是 150 步（15 秒）— 若误走该分支，此刻会长回来

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Tick.ShouldBeGreaterThan(LogicWorld.BossArriveTick, "终局不冻结时间轴（2026-09-23 拆冻结）");
        World.Buildings.Any(b => b.Kind == BuildingKind.BossLair).ShouldBeFalse("BOSS 不重生");
        Log.Respawned.ShouldBeEmpty("普通巢穴的重生调度未挂在 BOSS 身上（摧毁语义分岔正确）");
        World.Outcome.ShouldBe(GameOutcome.Victory, "仍是胜局");    }

    [Fact]
    public void 摧毁普通巢穴不判胜_只清剿涓流()
    {
        FortifyPalace();
        Advance(LogicWorld.BossArriveTick + 1);

        foreach (var lair in World.Buildings.Where(b => b.Kind == BuildingKind.MonsterLair).ToList())
        {
            World.DebugDamageBuilding(lair.Id, 9999);
        }
        Advance(2);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Outcome.ShouldBe(GameOutcome.Running, "胜利只认 BOSS 巢穴");
        Log.Outcomes.ShouldBeEmpty();    }

    [Fact]
    public void 终局判定_先判者定_败局不可被翻成胜局()
    {
        Advance(LogicWorld.BossArriveTick + 1); // 宫殿此刻多半早已倒；若未倒则手动判负
        if (World.Outcome == GameOutcome.Running)
        {
            World.DebugDamageBuilding("palace", LogicWorld.PalaceHealth);
        }

        World.Outcome.ShouldBe(GameOutcome.Defeat, "排布前提：先判负");

        var boss = World.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.BossLair);
        if (boss is not null) World.DebugDamageBuilding(boss.Id, 99999);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Outcome.ShouldBe(GameOutcome.Defeat, "终局判定是闩锁 — 后到的胜利不覆盖先判的失败");
        Log.Outcomes.ShouldBe(new[] { GameOutcome.Defeat }, false, "只发一次终局事件");    }

    // === 切 3：BOSS 威胁专属系数（不是廉价目标） ===

    [Fact]
    public void BOSS威胁取固定档_不随血量降价()
    {
        FortifyPalace();
        Advance(LogicWorld.BossArriveTick + 1);
        var boss = Boss();

        var threat = World.EstimateThreat(boss.Id);
        var priceAtFull = World.SuggestedPrice(boss.Id);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ((double)threat).ShouldBe(LogicWorld.BossThreat, 0.01, "威胁 = 固定档（不按血量折算）");
        priceAtFull.ShouldBe((int)MathF.Ceiling(LogicWorld.BossThreat * LogicWorld.BraveryRatio), "建议价与勇气公式同源");
        priceAtFull.ShouldBeGreaterThan(100, "终幕是一笔大赏，不是 $8 的便宜单");
        // 固定档的关键性质：啃掉一半血，这单仍然值同样的钱（否则「打越久越便宜」会奖励蹭血不攻坚）
        World.DebugDamageBuilding(boss.Id, LogicWorld.BossLairHealth / 2);
        World.SuggestedPrice(boss.Id).ShouldBe(priceAtFull, "半血 BOSS 建议价不变 — 攻坚投入不会因进度而贬值");
    }

    [Fact]
    public void 普通巢穴威胁口径不受BOSS系数影响()
    {
        var threat = World.EstimateThreat("lair1");

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ((double)threat).ShouldBe(300 * LogicWorld.BuildingThreatFactor, 0.01, "普通巢穴仍按 0.02 折算（切片 2 起的既有口径不变）");
        World.SuggestedPrice("lair1").ShouldBe(8, "巢穴建议价 $8 — 历史实测值复现");    }

    [Fact]
    public void BOSS可被挂赏_但低价照样无人接()
    {
        FortifyPalace();
        Advance(LogicWorld.BossArriveTick + 1);
        World.DebugSetTreasury(1000);
        var boss = Boss();

        // $40 低于任何英雄的承受点：感知威胁下限 = 90×0.4 = 36 → appeal ≤ 1.11 < 阈值 1.2
        var ok = World.PlaceBountyFlag(boss.Id, 40);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ok.ShouldBeTrue("BOSS 巢穴是怪物方建筑 → 悬赏旗可挂（玩家动词对终幕仍然有效）");
        World.SuggestedPrice(boss.Id).ShouldBeGreaterThan(40, "排布前提：故意挂一面低于建议价的旗");
        Advance(30);
        World.Flags.Any(f => f.TargetId == boss.Id && f.ClaimedByHeroId is not null).ShouldBeFalse("低于建议价 → 无人承接（红线：AI 可预期但不必然服从 — 终幕也不例外）");
    }
}
