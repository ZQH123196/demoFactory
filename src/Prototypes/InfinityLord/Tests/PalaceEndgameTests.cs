#nullable enable
using System;
using System.Linq;
using System.Numerics;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片 10「终局」第 1 步 — 宫殿实体 + 失败判定。
///
/// 定稿口径：
/// - 失败判定 = 独立宫殿实体（种子建筑、大血量、被毁即败），金库保持抽象数值不落地
/// - 袭城 AI **零改动**：怪物照旧「谁近打谁」，宫殿不享任何优先级（用户确认）
/// - 终局是闩锁：一旦判定，胜负不可被改写；**时间轴照常推进（不冻结）** —
///   ：要结束直接关游戏（原「判定即冻结」已拆除）
/// 验证 seam：LogicWorld 公开 API（Outcome / Buildings / DebugDamageBuilding）。
/// </summary>
public class PalaceEndgameTests : LogicWorldTestBase
{
    // === 切 1：宫殿作为种子建筑进场 ===

    [Fact]
    public void 种子世界含宫殿_大血量玩家方建筑()
    {
        var palace = World.Buildings.SingleOrDefault(b => b.Kind == BuildingKind.Palace);

        palace.ShouldNotBeNull("开局宫殿落地（税收集线 + 失败条件的物理化身）");
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        palace!.Owner.ShouldBe(Faction.Hero, "宫殿属于玩家方 — 怪物袭城会打它");
        palace.Health.ShouldBe(LogicWorld.PalaceHealth, "宫殿大血量（比行会 1000 更厚）");
        palace.SpawnIntervalTicks.ShouldBe(0, "宫殿不产兵 — 纯终局锚点");    }

    [Fact]
    public void 宫殿不可由玩家放置_也不可被挂赏()
    {
        World.DebugSetTreasury(100_000);

        World.PlaceBuilding(BuildingKind.Palace, new Vector2(-60, -60)).ShouldBeFalse("宫殿是种子实体，不是营建选项（翻倍计价表里没有它）");
        World.NextPlaceCost(BuildingKind.Palace).ShouldBe(0, "造价 0 = 不可放置标记");

        var palace = World.Buildings.First(b => b.Kind == BuildingKind.Palace);
        World.PlaceBountyFlag(palace.Id, 25).ShouldBeFalse("玩家方建筑不可挂赏（领主不悬赏自家财产 — 宫殿同样适用）");
    }

    // === 切 2：宫殿被毁 = 失败，判定不可改写 ===

    [Fact]
    public void 宫殿被毁_终局判负()
    {
        World.Outcome.ShouldBe(GameOutcome.Running, "开局进行中");

        var destroyed = World.DebugDamageBuilding("palace", LogicWorld.PalaceHealth);
        Advance(1);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        destroyed.ShouldBeTrue("排布前提：宫殿被扣血摧毁");
        World.Outcome.ShouldBe(GameOutcome.Defeat, "宫殿被毁 = 失败");
        Log.Outcomes.ShouldBe(new[] { GameOutcome.Defeat }, false, "终局事件恰好发一次（表现层挂横幅）");    }

    [Fact]
    public void 终局后世界照常演化_判定只记账不冻结()
    {
        World.DebugDamageBuilding("palace", LogicWorld.PalaceHealth);
        Advance(1);
        var tickAtEnd = World.Tick;

        var advanced = World.Advance(120);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        advanced.ShouldBeTrue("终局不冻结时间轴（要结束直接关游戏）");
        World.Tick.ShouldBeGreaterThan(tickAtEnd, "判负后时间轴继续推进");
        World.Outcome.ShouldBe(GameOutcome.Defeat, "判定仍是闩锁，不可被后续事件改写");    }

    [Fact]
    public void 宫殿未毁_终局保持进行中()
    {
        Advance(600); // 放任演化 1 分钟：英雄会死、建筑会掉血，但胜负未定

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Outcome.ShouldBe(GameOutcome.Running, "只有宫殿被毁才判负");
        Log.Outcomes.ShouldBeEmpty("未终局不发事件");
        World.Buildings.Any(b => b.Kind == BuildingKind.Palace).ShouldBeTrue("宫殿仍在场");    }

    // === 切 3：袭城 AI 零改动 — 宫殿不享优先级，谁近打谁 ===

    [Fact]
    public void 怪物袭城谁近打谁_宫殿不享优先也不被回避()
    {
        // 排布：等首只怪落场 → 支开英雄（视野 16，免得战斗 instinct 抢走这一步）→
        // 把怪挪到宫殿门口（宫殿距怪 2，行会距怪 6 — 宫殿仍是最近建筑）。
        var palace = World.Buildings.First(b => b.Kind == BuildingKind.Palace);
        Advance(LogicWorld.LairSpawnIntervalTicks + 1);
        foreach (var hero in Heroes()) World.PlaceUnitForTest(hero.Id, new Vector2(-80, -80)).ShouldBeTrue();
        var mob = Monsters().First();
        World.PlaceUnitForTest(mob.Id, palace.Position + new Vector2(2f, 0f)).ShouldBeTrue("排布前提：怪已挪到宫殿墙下");
        mob.TargetUnitId = null;

        Advance(10);

        var hitPalace = Log.Combats.Any(c => c.TargetId == palace.Id);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hitPalace.ShouldBeTrue("怪物在宫殿门口 → 就近攻宫殿（AI 零改动，宫殿既不加权也不降权）");
        palace.Health.ShouldBeLessThan(LogicWorld.PalaceHealth, "宫殿确实挨了打");    }

    [Fact]
    public void 宫殿挨打时其他建筑更近_则先打其他建筑()
    {
        // 对照：把怪放在行会门口（行会比宫殿更近）→ 打行会，宫殿无损。
        var palace = World.Buildings.First(b => b.Kind == BuildingKind.Palace);
        var guild = World.Buildings.First(b => b.Kind == BuildingKind.GuildHall);
        Advance(LogicWorld.LairSpawnIntervalTicks + 1);
        foreach (var hero in Heroes()) World.PlaceUnitForTest(hero.Id, new Vector2(-80, -80)).ShouldBeTrue();
        var mob = Monsters().First();
        World.PlaceUnitForTest(mob.Id, guild.Position + new Vector2(2f, 0f)).ShouldBeTrue("排布前提：怪已挪到行会墙下");
        mob.TargetUnitId = null;

        Advance(10);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        palace.Health.ShouldBe(LogicWorld.PalaceHealth, "宫殿没被优先照顾 — 谁近打谁");
        guild.Health.ShouldBeLessThan(1000, "行会先挨打");    }
}
