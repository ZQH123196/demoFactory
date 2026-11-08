#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 金币守恒 — 系统级不变量：任意时刻「金库 + 全体英雄钱包 + 在途悬赏」
/// 恒等于「初始金库 + 累计注水 − 累计焚烧」。
///
/// 增减口清点（经济路径全进出清单 — 2026-09-21 设计决策：军饷/维护费概念删除）：
/// - 注水（凭空增加）：村民税 VillagerTax、击杀战利品 KillLoot、英雄出生盘缠
/// - 焚烧（凭空消失）：英雄消费未回流部分（市场/酒馆/药店的 60% 沉淀 — TaxRate 设计语义）、
///   阵亡英雄随身金币（遗体消失）、建造款 BuildingBuilt（切片 8）、行会升级款 GuildUpgraded（切片 10）
/// - 搬运但需对账：挂赏/发赏/退款/税收回流（40% 份额回金库，其余沉淀）
/// 清单公式单点维护在 <see cref="LogicWorldTestBase.AssertConservation"/> — 本文件各测试按不同排布复用它。
/// 该测试把 MCP 实测各轮「经济闭环无泄漏」结论升级为不变量断言：
/// 任意演化长度下守恒成立，任何未来切片引入泄漏立即爆断言。
/// </summary>
public class EconomyConservationTests : LogicWorldTestBase
{
    /// <summary>系统内金币总量 = 金库 + 英雄钱包 + 在途悬赏（未结算旗）。</summary>
    private static long TotalGold(LogicWorld world)
        => world.Treasury
           + world.Units.Sum(u => (long)u.Gold)
           + world.Flags.Where(f => !f.Settled).Sum(f => (long)f.Gold);

    /// <summary>累计注水/焚烧/盘缠清单与守恒断言已上收 LogicWorldTestBase.AssertConservation（单点维护）。</summary>
    [Fact]
    public void 放任演化600步_金币守恒成立()
    {
        for (var i = 0; i < 600; i++)
        {
            Advance(1);
            AssertConservation($"（第 {World.Tick} 步 — 泄漏检查）");
        }
    }

    [Fact]
    public void 挂赏到结算_金币只在系统内搬运()
    {
        var before = TotalGold(World);
        World.PlaceBountyFlag("lair2", 25).ShouldBeTrue();
        TotalGold(World).ShouldBe(before, "挂赏：金库 → 在途旗（搬运，总量不变）");

        World.DebugDamageBuilding("lair2", 300).ShouldBeTrue();
        Advance(1);
        TotalGold(World).ShouldBe(before, "无人承接结算：在途旗 → 退款回金库（总量不变）");
    }

    [Fact]
    public void 放置建筑_金币转资产_守恒可对账()
    {
        // 切片 9 翻倍计价：60→120→240（金库 150 首轮只买得起 1 座酒馆 + 村民税补入的第二座）
        const long initial = 150;
        var pos = new System.Numerics.Vector2(-50, -50);
        var i = 0;
        while (World.NextPlaceCost(BuildingKind.Tavern) <= World.Treasury && i < 10)
        {
            var cost = World.NextPlaceCost(BuildingKind.Tavern); // 扣款额取放置前快照（翻倍计价）
            Advance(1);
            pos += new System.Numerics.Vector2(14, 0); // 每座错开 ≥12 间距
            World.PlaceBuilding(BuildingKind.Tavern, pos).ShouldBeTrue("排布：金库富余时放置成功");
            i++;
            // 循环条件每轮重读 — 翻倍价最终超过金库 + 周期税时停止（金库只进不出的排布期）
        }

        // Amount/Treasury 是 int、initial 是 const long —— NUnit 的 Is.EqualTo 跨数值类型比，
        // Shouldly 的 int 重载不跨，收侧补成 long 让两侧同型（守恒是对账，值必须逐分相等）。
        ((long)Log.Economy.Where(e => e.Kind == EconomyEventKind.BuildingBuilt).Sum(e => e.Amount)).ShouldBe(
            initial + Log.Economy.Where(e => e.Kind == EconomyEventKind.VillagerTax).Sum(e => e.Amount)
                - World.Treasury,
            "建造扣款累计 = 金库减少额（建造与村民税是仅有的金库异动）");
        AssertConservation($"（第 {World.Tick} 步 — 建造焚烧口入账后）");
    }

    [Fact]
    public void 升级行会_焚烧口入账_守恒成立()
    {
        // 切片 10 纵向成长：升级款是金库的又一个焚烧口（建造之后）— 不入账则守恒必爆。
        // 排布刻意不用 DebugSetTreasury：它直改金库、绕过注水记账，会把不变量本身弄脏。
        World.PlaceBuilding(BuildingKind.GuildWarrior, new System.Numerics.Vector2(-50, -50)).ShouldBeTrue();
        while (World.Treasury < LogicWorld.GuildUpgradeCost && World.Tick < 3000)
        {
            Advance(1); // 靠村民税攒升级款（唯一合法注水路径）
        }

        World.UpgradeGuild("guildwarrior1").ShouldBeTrue("排布前提：升级成功");

        for (var i = 0; i < 200; i++)
        {
            Advance(1);
            AssertConservation($"（第 {World.Tick} 步 — 升级焚烧口入账后）");
        }

        Log.Economy.Where(e => e.Kind == EconomyEventKind.GuildUpgraded).Sum(e => e.Amount).ShouldBe(LogicWorld.GuildUpgradeCost, "升级款全额记入焚烧口");
    }

    [Fact]
    public void 英雄消费_税收回流_搬运分解可对账()
    {
        Advance(25);
        var hero = Heroes().Single();
        hero.Gold = 30;
        World.PlaceUnitForTest(hero.Id, new System.Numerics.Vector2(-18, 8)).ShouldBeTrue("排布前提：站到市场门口");
        var treasuryBefore = World.Treasury;

        Advance(2);
        var spent = Log.Economy.Where(e => e.Kind == EconomyEventKind.HeroSpent && e.UnitId == hero.Id).Sum(e => e.Amount);
        var tax = Log.Economy.Where(e => e.Kind == EconomyEventKind.TaxCollected && e.UnitId == hero.Id).Sum(e => e.Amount);
        spent.ShouldBeGreaterThan(0, "消费发生");
        tax.ShouldBe((int)(spent * LogicWorld.TaxRate), "回流 = 消费×40%");
        World.Treasury.ShouldBe(treasuryBefore + tax, "金库增量 = 回流份额（其余 60% 沉淀市场 — 设计内沉淀口）");
    }
}
