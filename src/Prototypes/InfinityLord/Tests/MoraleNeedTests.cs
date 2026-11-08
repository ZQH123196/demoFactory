#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片7 — 需求层级：士气衰减 → 阈值 → 喝酒恢复；低士气拒单（勇气惩罚真实改变行为）。
/// （原为 MCP 实测「快进 ×5 + 快照采样 + 挂旗对照」的行为学实验，现确定性断言）
/// </summary>
public class MoraleNeedTests : LogicWorldTestBase
{
    [Fact]
    public void 士气按周期自然衰减()
    {
        Advance(25); // 首个英雄已落场
        var hero = Heroes().First();
        var before = hero.Morale;

        Advance(LogicWorld.MoraleDecayTicks);
        hero.Morale.ShouldBe(before - 1, "每 30 步（3 秒）全体 −1（磨平热情）");

        Advance(LogicWorld.MoraleDecayTicks * 3);
        hero.Morale.ShouldBe(before - 4, "衰减速率恒定");
    }

    [Fact]
    public void 跌破阈值_英雄走向酒馆并喝酒回满()
    {
        Advance(25);
        var hero = Heroes().Single();
        hero.Morale = LogicWorld.MoraleDrinkThreshold - 1; // 排布：59 → want_drink
        hero.Position = new System.Numerics.Vector2(-18, -8); // 排布：站到酒馆门口
        var treasuryBefore = World.Treasury;

        Advance(2);

        // 喝酒语义：喝到满但**零钱不足为止**（出生 $10 只够 1 杯：59 → 99，差 1 点卡在没钱）
        Log.Count(EconomyEventKind.HeroDrank).ShouldBe(1, "1 杯（$10 盘缠的购买力上限）");
        hero.Morale.ShouldBe(99, "59 + 40 = 99（钱尽杯止 — 需求层级与钱包耦合的张力）");
        hero.Gold.ShouldBeLessThanOrEqualTo(10, "盘缠花尽（可能先战斗拾金，钳 0）");
        // 金币循环：HeroSpent → 40% 税收回流
        var spent = Log.Economy.Where(e => e.Kind == EconomyEventKind.HeroSpent && e.UnitId == hero.Id).Sum(e => e.Amount);
        var tax = Log.Economy.Where(e => e.Kind == EconomyEventKind.TaxCollected && e.UnitId == hero.Id).Sum(e => e.Amount);
        spent.ShouldBe(10, "1 杯 × $10");
        tax.ShouldBe((int)(spent * LogicWorld.TaxRate), "税收回流 = 消费 ×40%（闭环不泄漏）");
        World.Treasury.ShouldBe(treasuryBefore + tax, "金库入账恰为税收回流");
    }

    [Fact]
    public void 低士气放大感知威胁_高士气不受影响()
    {
        // 静态公式不依赖世界实例 — 构造标准新兵参数载体（战力 8×2+60/5 = 28 → 感知系数 1）
        var hero = new Unit
        {
            Id = "probe",
            Name = "探针",
            Side = Faction.Hero,
            Position = System.Numerics.Vector2.Zero,
            Speed = 1f,
            Health = 60,
            MaxHealth = 60,
            Attack = 8,
            AttackCooldownTicks = 6,
            SightRange = 1f,
            AttackRange = 1f,
            Gold = 0,
        };

        LogicWorld.PerceivedThreat(hero, 16f).ShouldBe(16f, 0.001f, "满血新兵（战力 28 = 基准）感知系数 = 1");

        // 士气惩罚系数（与 TryClaimFlag 同源公式）：士气 0 → 感知威胁 ×2；惩罚随士气线性
        var appealFull = 25f / 16f;
        var appealNearThreshold = 25f / (16f * (1f + LogicWorld.MoralePenaltyMax * (LogicWorld.MoraleDrinkThreshold - 59) / LogicWorld.MoraleDrinkThreshold));
        var appealZero = 25f / (16f * (1f + LogicWorld.MoralePenaltyMax));
        ((double)appealZero).ShouldBe(appealFull / 2f, 0.001, "士气 0 → 感知威胁 ×2");
        appealNearThreshold.ShouldBeGreaterThan(appealZero, "惩罚随士气线性增长");
    }

    [Fact]
    public void 低士气英雄_同一面旗拒绝承接()
    {
        // 对照实验（复现 MCP 挂旗对照）：同一旗、同一英雄，仅改士气 — 接单差异即因果。
        // 目标选怪物单位（威胁 16）：士气 100 → appeal 25/16=1.56 ≥ 1.2 接；
        // 士气 0 → 感知威胁 ×2 → appeal 0.78 < 1.2 拒。（选巢穴会漏：6 威胁的旗士气 0 也接）
        Advance(LogicWorld.LairSpawnIntervalTicks + 1); // 首英雄（tick 21）与首怪物（tick 31）均已落场
        var mob = Monsters().First();
        World.PlaceBountyFlag(mob.Id, 25).ShouldBeTrue();
        var hero = Heroes().First();

        hero.Morale = 100;
        Advance(1);
        hero.ClaimedFlagId.ShouldNotBeNull("高士气 → 正常承接");

        // 重置：释放旗子，压士气到谷底（仅改士气 — 其余排布不动）
        var flag = World.Flags.First(f => f.ClaimedByHeroId == hero.Id);
        flag.ClaimedByHeroId = null;
        hero.ClaimedFlagId = null;
        hero.Morale = 0;
        hero.TargetUnitId = null;
        hero.CooldownRemaining = 0;

        Advance(1);
        hero.ClaimedFlagId.ShouldBeNull("士气 0 → 感知威胁 ×2 → appeal 减半 → 拒绝承接");
    }

    [Fact]
    public void 伤势需求_低血英雄买药剂回血()
    {
        Advance(25);
        var hero = Heroes().Single();
        hero.Health = 40; // < 70% × 60 = 42 → 想买药剂
        hero.Gold = 60;   // 足够多瓶
        hero.Position = new System.Numerics.Vector2(-10, 16); // 药店门口
        var treasuryBefore = World.Treasury;

        Advance(2);

        // 40 → 一瓶 +25 触顶 60（买到满），恰花 $15
        Log.Count(EconomyEventKind.HeroBoughtPotion).ShouldBe(1);
        hero.Health.ShouldBe(60, "买到满血（40+25 → 上限 60）");
        hero.Gold.ShouldBe(45, "60 − 15×1（一瓶即满）");
        World.Treasury.ShouldBe(treasuryBefore + 6, "药剂钱 40% 税收回流");
    }
}
