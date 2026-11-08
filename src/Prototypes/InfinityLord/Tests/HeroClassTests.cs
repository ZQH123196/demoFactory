#nullable enable
using System;
using System.Linq;
using System.Numerics;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片 9「职业」— 英雄职业分化：
/// - 职业差异只落在四个参数：攻击/血/速度/勇气（不搞复杂）
/// - 所有人自带初始职业；建筑即招募（Majesty 2 本尊）— 行会可放置，
///   建什么行会就产什么职业；翻倍计价 + 每公会名额上限（第一个便宜，后面每个翻倍）
/// - 游侠远程（AttackRange=6 — 零代码风筝）、战士近战肉盾
/// 验证 seam：LogicWorld 公开 API（PlaceBuilding / Units 快照 / 静态公式）。
/// </summary>
public class HeroClassTests : LogicWorldTestBase
{
    // === 切 1：种子行会产游侠 — 职业数值表正确 ===

    [Fact]
    public void 行会产出游侠_职业数值表正确()
    {
        Advance(21); // 种子游侠公会（guild）20 步产出窗口
        var hero = Heroes().Single();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        hero.Class.ShouldBe(HeroClass.Ranger, "种子行会 = 游侠公会 → 产游侠");
        hero.Name.ShouldBe("游侠");
        hero.MaxHealth.ShouldBe(60, "游侠血量 60");
        hero.Health.ShouldBe(60, "出生满血");
        hero.Attack.ShouldBe(8, "游侠攻击 8");
        hero.Speed.ShouldBe(1.6f, "游侠速度 1.6");
        hero.AttackRange.ShouldBe(6f, "游侠远程射程 6（零代码风筝）");
        hero.HomeGuildId.ShouldBe("guild", "出生绑定产出行会（招募名额记账键）");    }

    // === 切 2：建战士公会 → 产战士 ===

    [Fact]
    public void 建战士公会_按周期产出战士()
    {
        // 远离种子建筑（最小间距 12 外）：(-50,-50) 距酒馆(-18,-8) 约 52
        var ok = World.PlaceBuilding(BuildingKind.GuildWarrior, new Vector2(-50, -50));
        ok.ShouldBeTrue("行会可由玩家放置（切片 9 翻转 — 建筑即招募）");

        Advance(21); // 新行会首个产出窗口（ScheduleBuildingSpawns 建成即起调度）

        var warrior = Heroes().Single(h => h.Class == HeroClass.Warrior);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        warrior.Name.ShouldBe("战士");
        warrior.MaxHealth.ShouldBe(100, "战士血量 100（肉盾）");
        warrior.Attack.ShouldBe(10, "战士攻击 10");
        warrior.Speed.ShouldBe(1.2f, "战士速度 1.2（比游侠慢）");
        warrior.AttackRange.ShouldBe(1.5f, "战士近战");
        warrior.HomeGuildId.ShouldBe("guildwarrior1", "绑定产出行会");    }

    // === 切 3：招募名额 — 每公会存活上限 4，死亡腾名额后补员 ===

    [Fact]
    public void 行会招募名额封顶_补员到上限即停()
    {
        Advance(21); // 首名已落场

        Advance(120); // 数个产出窗口（20 步/名 × 4 名上限绰绰有余）

        Heroes().Count.ShouldBe(LogicWorld.GuildCapacity, $"游侠公会名额 {LogicWorld.GuildCapacity} — 到顶即停（生态承载力从流失制变名额制）");
    }

    [Fact]
    public void 英雄阵亡腾出名额_行会自动补员()
    {
        Advance(100); // 补员满编（4 名）
        Heroes().Count.ShouldBe(LogicWorld.GuildCapacity, "排布前提：满编");

        // 杀掉一名英雄 → 名额腾出 → 下一产出窗口补员
        var victim = Heroes().First();
        victim.Health = 0;
        Advance(60);

        Heroes().Count.ShouldBe(LogicWorld.GuildCapacity, "死亡腾名额 → 自动补员回满编");
    }

    // === 切 4：勇气分化 — 同一面旗，战士接、游侠拒（对照实验） ===

    [Fact]
    public void 勇气阈值职业系数_战士莽游侠挑()
    {
        // 静态公式：Warrior 0.7 / Ranger 1.0（探针裸载体，战力基准 28 → 感知系数 1）
        LogicWorld.ClassBraveryFactor(HeroClass.Warrior).ShouldBe(0.7f, 0.001);
        LogicWorld.ClassBraveryFactor(HeroClass.Ranger).ShouldBe(1.0f, 0.001);
    }

    [Fact]
    public void 同一面低赏旗_战士接_游侠拒()
    {
        // 排布：满编游侠 + 一名战士（建战士公会 → 快进产出），
        // 挂一面「战士阈值内、游侠阈值外」的旗：mob 威胁 16 → 游侠阈值价 = 16×1.2 = 19.2 → $18 拒。
        // 战士：感知威胁 = 16×(28/40)=11.2 → appeal = 18/11.2 = 1.6 ≥ 1.2×0.7=0.84 → 接。
        // 视野隔离：旗挂 mob（主战场），战士单独挪到 mob 身边（否则游侠公会先抢/战士看不到）。
        Advance(21);
        var ok = World.PlaceBuilding(BuildingKind.GuildWarrior, new Vector2(-50, -50));
        ok.ShouldBeTrue("排布前提：战士公会建成");
        Advance(LogicWorld.LairSpawnIntervalTicks); // 战士落场，且首只怪已按涓流节奏落场

        var mob = Monsters().First();
        var warrior = Heroes().First(h => h.Class == HeroClass.Warrior);
        World.PlaceUnitForTest(warrior.Id, mob.Position + new Vector2(1, 0)).ShouldBeTrue(); // 贴脸：进战士视野与射程
        World.PlaceBountyFlag(mob.Id, 18).ShouldBeTrue("排布前提：低赏旗挂出");

        Advance(1);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        warrior.ClaimedFlagId.ShouldNotBeNull("战士勇气系数 0.7 → 低赏也接");
        Heroes().Where(h => h.Class == HeroClass.Ranger && h.ClaimedFlagId is not null).ShouldBeEmpty("游侠勇气系数 1.0 → 同一旗不够勇不接（定价策略的职业分化）");    }
}
