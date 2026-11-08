#nullable enable
using System;
using System.Linq;
using System.Numerics;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片 10「终局」第 3 步 — 行会升级一格 + 精英职业（2026-09-21 定稿：玩家侧纵向成长补一件）。
///
/// 设计口径：
/// - 一个方法 + 查表扩展的最小纵向成长：花金库把战士公会升到 Lv.2 → 此后产**骑士**（职业表只加一行）。
/// - 精英线本切片只开战士公会一条（游侠公会的答案是「多建一座/多养兵」）——
///   升不上去的种类明确拒绝，不做「花了钱没变化」的陷阱。
/// - 已出生的英雄不追溯变职业：升级改变的是**这条产出链的下一批**（供应链不是魔法）。
/// - 升级款是金库的焚烧口（金币转能力，与建造同款语义）→ 守恒测试同步。
/// 验证 seam：LogicWorld 公开 API（UpgradeGuild / NextUpgradeCost / Buildings.Level / Units）。
/// </summary>
public class GuildUpgradeTests : LogicWorldTestBase
{
    private const string WarriorGuildPos = "guildwarrior1";

    /// <summary>排布：在远角落成一座战士公会（种子金库只够这一座）。</summary>
    private void PlaceWarriorGuild()
    {
        World.PlaceBuilding(BuildingKind.GuildWarrior, new Vector2(-50, -50)).ShouldBeTrue("排布前提：战士公会建成");
    }

    // === 切 1：升级动作本身 ===

    [Fact]
    public void 升级战士公会_扣款置等级并发事件()
    {
        PlaceWarriorGuild();
        World.DebugSetTreasury(LogicWorld.GuildUpgradeCost + 100);
        var treasuryBefore = World.Treasury;

        var ok = World.UpgradeGuild(WarriorGuildPos);
        var guild = World.Buildings.First(b => b.Id == WarriorGuildPos);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ok.ShouldBeTrue("金库富余时升级成功");
        guild.Level.ShouldBe(2, "升到 Lv.2（本切片只有一格）");
        World.Treasury.ShouldBe(treasuryBefore - LogicWorld.GuildUpgradeCost, "升级款从金库扣掉");
        Log.Economy.Where(e => e.Kind == EconomyEventKind.GuildUpgraded).Sum(e => e.Amount).ShouldBe(LogicWorld.GuildUpgradeCost, "经济事件入账（守恒审计口）");    }

    [Fact]
    public void 金库不足_升级被拒且分文不动()
    {
        PlaceWarriorGuild();
        World.DebugSetTreasury(LogicWorld.GuildUpgradeCost - 1);
        var treasuryBefore = World.Treasury;

        var ok = World.UpgradeGuild(WarriorGuildPos);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        ok.ShouldBeFalse("金库不足 → 拒绝");
        World.Treasury.ShouldBe(treasuryBefore, "拒绝时金库分文不动（与营建同款语义）");
        World.Buildings.First(b => b.Id == WarriorGuildPos).Level.ShouldBe(1, "等级未变");    }

    [Fact]
    public void 只能升一格_重复升级被拒()
    {
        PlaceWarriorGuild();
        World.DebugSetTreasury(LogicWorld.GuildUpgradeCost * 10);

        World.UpgradeGuild(WarriorGuildPos).ShouldBeTrue("第一次升级成功");
        var treasuryAfterFirst = World.Treasury;

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.UpgradeGuild(WarriorGuildPos).ShouldBeFalse("Lv.2 是本切片的一格天花板");
        World.Treasury.ShouldBe(treasuryAfterFirst, "重复升级不得二次扣款");    }

    [Fact]
    public void 不可升级的种类_明确拒绝()
    {
        World.DebugSetTreasury(10_000);
        Advance(21); // 种子游侠公会的英雄落场（确认世界在跑）

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.UpgradeGuild("guild").ShouldBeFalse("游侠公会本切片不开精英线 — 明确拒绝而非「花了钱没变化」");
        World.UpgradeGuild("market").ShouldBeFalse("服务建筑不是行会");
        World.UpgradeGuild("lair1").ShouldBeFalse("巢穴不是玩家建筑");
        World.UpgradeGuild("nope").ShouldBeFalse("不存在的建筑");    }

    // === 切 2：升级改变产出链的下一批（精英职业表生效） ===

    [Fact]
    public void 升级后产兵变骑士_数值表生效()
    {
        PlaceWarriorGuild();
        Advance(21);
        var oldHero = Heroes().Single(h => h.HomeGuildId == WarriorGuildPos);
        oldHero.Class.ShouldBe(HeroClass.Warrior, "排布前提：升级前产的是战士");

        World.DebugSetTreasury(LogicWorld.GuildUpgradeCost + 100);
        World.UpgradeGuild(WarriorGuildPos).ShouldBeTrue("排布前提：已升级");

        Advance(21); // 升级后的第一个产出窗口

        var knight = Heroes().Single(h => h.Class == HeroClass.Knight);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        knight.Name.ShouldBe("骑士");
        knight.MaxHealth.ShouldBe(180, "骑士血量 180（战士 100 的上一档）");
        knight.Attack.ShouldBe(16, "骑士攻击 16");
        knight.Speed.ShouldBe(1.3f, "骑士速度 1.3（比战士 1.2 略快 — 重甲冲锋的读感）");
        knight.AttackRange.ShouldBe(2f, "骑士仍是近战");
        knight.HomeGuildId.ShouldBe(WarriorGuildPos, "仍绑定这座行会（名额记账不变）");        oldHero.Class.ShouldBe(HeroClass.Warrior, "已出生的英雄不追溯转职 — 升级改的是下一批");
    }

    [Fact]
    public void 骑士名额沿用同一座行会的封顶()
    {
        PlaceWarriorGuild();
        World.DebugSetTreasury(LogicWorld.GuildUpgradeCost + 100);
        World.UpgradeGuild(WarriorGuildPos).ShouldBeTrue("排布前提：直接升级后再产兵");

        Advance(200); // 数个产出窗口

        var fromGuild = Heroes().Where(h => h.HomeGuildId == WarriorGuildPos).ToList();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        fromGuild.Count.ShouldBe(LogicWorld.GuildCapacity, "Lv.2 不放大名额（精英≠更多）");
        fromGuild.All(h => h.Class == HeroClass.Knight).ShouldBeTrue("满编后全是骑士");    }

    // === 切 3：勇气分化 — 骑士比战士更莽（应对曲线的「敢打硬单」） ===

    [Fact]
    public void 骑士勇气系数最低()
    {
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        LogicWorld.ClassBraveryFactor(HeroClass.Knight).ShouldBe(0.6f, 0.001);
        LogicWorld.ClassBraveryFactor(HeroClass.Knight).ShouldBeLessThan(LogicWorld.ClassBraveryFactor(HeroClass.Warrior), "骑士比战士还敢打");
        LogicWorld.ClassBraveryFactor(HeroClass.Knight).ShouldBeLessThan(LogicWorld.ClassBraveryFactor(HeroClass.Ranger));    }

    [Fact]
    public void 同一面硬单_骑士敢接_游侠拒()
    {
        // 目标：大地精（波次第 2 波，威胁 = 14×2 + 120/5 = 52）→ 游侠满血阈值价 = 52×1.2 = 62.4
        // 骑士战力 = 16×2 + 180/5 = 68 → 感知系数 = 28/68 = 0.41 → 感知威胁 21.4
        //   appeal = 55/21.4 = 2.57 ≥ 1.2×0.6 = 0.72 → 接
        // 游侠战力 28 → 感知威胁 52 → appeal = 55/52 = 1.06 < 1.2 → 拒
        //
        // 前提要显式定住 —— 这条判据读英雄自己的战力（感知威胁含等级与装备），
        // 而这一局已经跑了 1801 步：实测第 1801 步上有一名 lv.6 的游侠（战力 46）算得过来，
        // 它排在骑士之前先把旗拿走了。承接不判距离，谁排在场上次序前面谁先叫牌，
        // 所以「谁该接」在这里只该由职业决定 ⇒ 全场英雄先写回卡面基准（见 PinToCardBaseline）。
        FortifyPalace();
        PlaceWarriorGuild();
        World.DebugSetTreasury(LogicWorld.GuildUpgradeCost + 100);
        World.UpgradeGuild(WarriorGuildPos).ShouldBeTrue("排布前提：升级产骑士");
        Advance(1801); // 第 2 波大地精落场 + 骑士落场

        var ogre = Monsters().First(m => m.Name == "大地精");
        var knight = Heroes().First(h => h.Class == HeroClass.Knight);
        var ranger = Heroes().First(h => h.Class == HeroClass.Ranger);
        World.PlaceUnitForTest(knight.Id, ogre.Position + new Vector2(1f, 0f)).ShouldBeTrue();
        // 游侠摆在 10 米外：既进得了「同一面旗」的候选（离骑士不到 1 米就没意义），
        // 又在怪物视野（10 米）之外 —— 免得这一局的接单顺序由「谁先踏进交战距离」决定，
        // 那与本节要证的「勇气公式的职业分化」是两件事（移动换实现之后实测踩到这一处）。
        World.PlaceUnitForTest(ranger.Id, ogre.Position + new Vector2(10f, 0f)).ShouldBeTrue();

        // 前提显式定住：全场英雄写回卡面基准（职业那三个数 + 满血满士气）。
        // 只定两名候选不够 —— 次序在候选之前的同名职业一样会把旗先拿走（实测那名 lv.6 的游侠）。
        foreach (var hero in Heroes())
        {
            PinToCardBaseline(hero);
        }

        // 前提读数：两名候选此刻的感知威胁（公式见 LogicWorld.TryClaimFlag）
        LogicWorld.PerceivedThreat(ranger, 52f).ShouldBe(52f, 0.01f, "卡面基准游侠：战力 28 = 基准 → 感知系数 1");
        LogicWorld.PerceivedThreat(knight, 52f).ShouldBe(21.4f, 0.1f, "卡面基准骑士：战力 68 → 感知系数 0.41");

        World.PlaceBountyFlag(ogre.Id, 55).ShouldBeTrue("排布前提：硬单挂出");

        Advance(1);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        knight.ClaimedFlagId.ShouldNotBeNull(
            $"骑士（系数 0.6 + 高战力）敢接硬单；实测 游侠旗={ranger.ClaimedFlagId} "
            + $"承接数={Heroes().Count(h => h.ClaimedFlagId is not null)}/{Heroes().Count} "
            + $"骑士 士气={knight.Morale} 血={knight.Health}/{knight.MaxHealth} 目标={knight.TargetUnitId} "
            + $"旗={knight.ClaimedFlagId}");
        ranger.ClaimedFlagId.ShouldBeNull("同一面旗游侠算不过来 → 拒（定价策略的职业分化）");    }

    // === 切 4：升级价可预读（表现层选单与扣款同源） ===

    [Fact]
    public void 升级价查询_与扣款同源且已封顶返回零()
    {
        PlaceWarriorGuild();
        World.NextUpgradeCost(WarriorGuildPos).ShouldBe(LogicWorld.GuildUpgradeCost, "可升级时报价 = 常数（表现层显示用）");

        World.DebugSetTreasury(LogicWorld.GuildUpgradeCost);
        World.UpgradeGuild(WarriorGuildPos).ShouldBeTrue();
        World.NextUpgradeCost(WarriorGuildPos).ShouldBe(0, "已封顶 → 报价 0 = 不可升级标记");
    }
}
