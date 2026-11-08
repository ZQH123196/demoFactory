#nullable enable
using System;
using System.Linq;
using System.Numerics;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片 8「营建」— 玩家建筑放置（玩家动词 #2）。
/// 花金库在指定位置即时建成服务建筑（Majesty 2 本尊核心玩法）。
/// 设计决策：一次性造价无维护费；即时建成（无建造时长，
/// 故「建后不退款/途中退 100%」两条退款规则自然无代码）；拆除不在本切片。
/// 校验语义同常规 RTS：金库不足 / 与存活建筑过近 / 不可放置种类 → 拒绝，不深究。
/// </summary>
public class BuildingPlacementTests : LogicWorldTestBase
{
    [Fact]
    public void 放置酒馆_金库扣款_建筑入列_事件齐()
    {
        var treasuryBefore = World.Treasury;
        var pos = new Vector2(-50, -50); // 远离所有种子建筑（最小间距外）

        var ok = World.PlaceBuilding(BuildingKind.Tavern, pos);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ok.ShouldBeTrue("合法放置应成功");
        World.Treasury.ShouldBe(treasuryBefore - LogicWorld.TavernCost, "金库扣建造造价");        var placed = World.Buildings.Single(b => b.Id == "tavern2"); // 种子酒馆隐占 1 号，续序 2
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        placed.Kind.ShouldBe(BuildingKind.Tavern);
        placed.Owner.ShouldBe(Faction.Hero);
        placed.Position.ShouldBe(pos);
        placed.Health.ShouldBe(400, "血量沿用同种种子建筑规格");
        placed.SpawnIntervalTicks.ShouldBe(0, "服务建筑不产单位");        Log.BuildingPlaced.Single().Id.ShouldBe("tavern2", "表现层模型靠此事件建立");
        Log.Count(EconomyEventKind.BuildingBuilt).ShouldBe(1, "建造扣款入经济事件流（守恒可对账）");
    }

    [Fact]
    public void 金库不足_拒绝且金库分文不动()
    {
        World.DebugSetTreasury(LogicWorld.TavernCost - 1);

        var ok = World.PlaceBuilding(BuildingKind.Tavern, new Vector2(-50, -50));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ok.ShouldBeFalse("金库不足应拒绝");
        World.Treasury.ShouldBe(LogicWorld.TavernCost - 1, "拒绝不扣款");
        World.Buildings.Where(b => b.Id == "tavern2").ShouldBeEmpty("无建筑入列");
        Log.BuildingPlaced.ShouldBeEmpty("无事件外泄");    }

    [Fact]
    public void 过近放置_拒绝()
    {
        // 种子酒馆在 (-18,-8)；取其 12 间距内、远离其他建筑的点
        var nearTavern = new Vector2(-18, -8) + new Vector2(8, 0); // 距酒馆 8 < 12
        var treasuryBefore = World.Treasury;

        var ok = World.PlaceBuilding(BuildingKind.Tavern, nearTavern);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ok.ShouldBeFalse("与存活建筑过近应拒绝");
        World.Treasury.ShouldBe(treasuryBefore, "拒绝不扣款");
        Log.BuildingPlaced.ShouldBeEmpty("无事件外泄");    }

    [Fact]
    public void 不可放置种类_巢穴拒绝_行会可放置且按翻倍价扣款()
    {
        // 切片 9 设计翻转：建筑即招募 — 行会可放置（GuildHall=游侠公会 $80 基数）；
        // 巢穴仍不可放置。翻倍计价：第 1 座 $80，第 2 座 $160（金库 150 不够 → 拒绝且分文不动）。
        var far = new Vector2(-50, -50);
        var treasuryBefore = World.Treasury;

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.PlaceBuilding(BuildingKind.MonsterLair, far).ShouldBeFalse("巢穴不可由玩家放置");
        World.PlaceBuilding(BuildingKind.GuildHall, far).ShouldBeTrue("行会可放置（切片 9 建筑即招募）");
        World.Treasury.ShouldBe(treasuryBefore - LogicWorld.GuildRangerCost, "第 1 座 = 基数价");        Log.BuildingPlaced.Single().Kind.ShouldBe(BuildingKind.GuildHall, "行会放置事件照发");

        World.DebugSetTreasury(159); // 第 2 座游侠公会 $160 — 差 $1
        var mid = new Vector2(-70, -50);
        World.PlaceBuilding(BuildingKind.GuildHall, mid).ShouldBeFalse("第 2 座 $160 > 金库 159");
        World.Treasury.ShouldBe(159, "拒绝不扣款");

        World.DebugSetTreasury(200);
        World.PlaceBuilding(BuildingKind.GuildHall, mid).ShouldBeTrue("金库足够 → 第 2 座建成");
        World.Treasury.ShouldBe(40, "第 2 座翻倍 $160 扣款（第一个便宜，后面每个翻倍）");
    }

    [Fact]
    public void 建酒馆改变动线_饥饿清零_英雄喝上酒()
    {
        // 切片 8 核心验证点：酒馆饥饿线随酒馆可及性变化 — 玩家建酒馆 = 改变英雄动线。
        // 排布复用需求流失那三步：拆行会封刷新源 → 隔离英雄 → Tankify 防路上误伤。
        Advance(25);
        World.DebugDamageBuilding("guild", 1000).ShouldBeTrue("排布前提：行会已拆");
        var hero = Heroes().Single();

        // 第一步：拆掉种子酒馆 → 低士气+有钱的英雄「想喝却无酒馆」→ 酒馆线开始累计
        World.DebugDamageBuilding("tavern", 1000).ShouldBeTrue("排布前提：种子酒馆已拆");
        World.PlaceUnitForTest(hero.Id, new Vector2(-80, -60)).ShouldBeTrue("排布前提：隔离点已落下");
        hero.MoveTarget = null;
        hero.Morale = LogicWorld.MoraleDrinkThreshold - 1; // 想喝
        hero.Gold = 50;                                    // 买得起 — 只缺酒馆
        hero.Health = hero.MaxHealth;
        hero.NoIncomeTicks = 0;
        hero.TavernStarveTicks = 0;
        hero.LastObservedGold = 50;
        Tankify(hero);

        Advance(30);
        hero.TavernStarveTicks.ShouldBeGreaterThan(0, "无酒馆且想喝 → 饥饿线累计");
        Log.Count(EconomyEventKind.HeroDrank).ShouldBe(0, "无酒馆可喝");

        // 第二步：在英雄身边建新酒馆 → 可及性翻转 → 饥饿线清零、英雄走向酒馆喝上酒
        var tavernPos = new Vector2(-76, -60); // 距英雄 4（<2 不满足 — 走路才到），距种子建筑均 ≥12
        var treasuryBefore = World.Treasury;
        World.PlaceBuilding(BuildingKind.Tavern, tavernPos).ShouldBeTrue("排布前提：新酒馆建成");

        // 英雄钱包富余（有钱会去市场）→ 先 Tankify 已做；等待赶路+喝酒
        Advance(80);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hero.TavernStarveTicks.ShouldBe(0, "有酒馆+有钱 → 酒馆线清零（canDrink 满足）");
        Log.Count(EconomyEventKind.HeroDrank).ShouldBeGreaterThan(0, "英雄走向新酒馆并喝上（动线改变）");
        World.Treasury.ShouldBeLessThan(treasuryBefore + 12 * 2, "酒水消费走税收回流（金库无建造外的异动）");    }
}
