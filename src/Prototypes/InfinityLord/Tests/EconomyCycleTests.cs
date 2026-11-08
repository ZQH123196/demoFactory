#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 经济周期（2026-09-21 设计决策：军饷/维护费概念删除 — 冒险者完全自雇）：
/// 村民税保底是唯一系统性进水；人口压力由需求流失（赏金/酒馆饥饿离队）承载。
/// 固定种子 42 下世界演化完全确定，断言数值永久可复现。
/// 需求流失测试的排布要点：先拆行会封刷新源，再把英雄钉在远离战场的角落
/// （战斗干扰 — 战死与战利品都会污染两条饥饿线），饥饿窗口才可隔离观测。
/// </summary>
public class EconomyCycleTests : LogicWorldTestBase
{
    /// <summary>远离战场（巢穴在 60±/65±）与行会的角落 — 测试排布用隔离点。</summary>
    private static System.Numerics.Vector2 Isolated => new(-80, -60);

    /// <summary>拆掉行会封住英雄刷新（排布用：离队窗口内世界不再补人）。</summary>
    private void FreezeRecruitment()
        => World.DebugDamageBuilding("guild", 1000).ShouldBeTrue("排布前提：行会已拆（无新英雄）");

    /// <summary>隔离排布：首英雄钉在角落，切断战斗/药店/市场支线对饥饿线的污染。</summary>
    private void Isolate(Unit hero, int morale, int gold)
    {
        World.PlaceUnitForTest(hero.Id, Isolated).ShouldBeTrue("排布前提：隔离点已落下");
        hero.MoveTarget = null;
        hero.Morale = morale;          // morale < 60 即开启酒馆线（测试自选开哪条线）
        hero.Gold = gold;
        hero.Health = hero.MaxHealth;  // 无伤 → 不被药店支线牵走
        // 计时清零 + 钱包基准对齐（出生到排布之间已累计的饥饿不作数 — 从隔离态起测）
        hero.NoIncomeTicks = 0;
        hero.TavernStarveTicks = 0;
        hero.LastObservedGold = gold;
    }

    [Fact]
    public void 周期结算_净额恒等于村民税()
    {
        Advance(LogicWorld.EconomyCycleTicks);

        World.LastCycleNet.ShouldBe(LogicWorld.VillagerTaxPerCycle, "净额 = 村民税（无系统性支出）");
        World.Treasury.ShouldBe(150 + LogicWorld.VillagerTaxPerCycle, "金库按净额入账");
        Log.Count(EconomyEventKind.VillagerTax).ShouldBe(1);
    }

    [Fact]
    public void 金库零不阻断招募_巢穴产出也不受影响()
    {
        // 设计决策回归：招募冻结已删（发不出军饷的理由不存在）— 金库 0 也照常刷英雄
        Advance(25);
        var heroesBefore = Heroes().Count;
        heroesBefore.ShouldBeGreaterThan(0, "排布前提：已有英雄在册");

        World.DebugSetTreasury(0);
        var monstersBefore = Monsters().Count;

        Advance(20); // 覆盖行会产出周期（20 步内的下一次出兵窗口）

        Heroes().Count.ShouldBeGreaterThan(heroesBefore, "金库为 0 → 招募照常（行会不发饷）");
        Monsters().Count.ShouldBeGreaterThanOrEqualTo(monstersBefore, "巢穴产出不受金库影响");
    }

    [Fact]
    public void 赏金饥饿_零收入满阈值即离队()
    {
        Advance(25); // 首英雄已落场
        FreezeRecruitment();
        var hero = Heroes().Single();
        Isolate(hero, morale: 100, gold: 0); // 隔离酒馆线：不想喝 → 酒馆饥饿恒清零

        EconomyEventKind? leaveReason = null;
        World.EconomyEvent += e =>
        {
            if (e.Kind is EconomyEventKind.HeroLeftNoBounty or EconomyEventKind.HeroLeftNoTavern)
            {
                leaveReason = e.Kind;
            }
        };

        // 零收入悬停 NeedStarveLeaveTicks 步 → 离队
        Advance(LogicWorld.NeedStarveLeaveTicks + 10);

        leaveReason.ShouldBe(EconomyEventKind.HeroLeftNoBounty, "零收入满阈值 → 冒险者离队（原因：赏金饥饿）");
        Heroes().Count.ShouldBe(0, "离队者已移出世界（行会已拆 — 无新人补位）");
    }

    [Fact]
    public void 金币进账_赏金饥饿清零不离队()
    {
        Advance(25);
        FreezeRecruitment();
        var hero = Heroes().Single();
        Isolate(hero, morale: 100, gold: 0);

        // 每 100 步（10 秒）注 1 金币（模拟战利品零星进账）— 任何进账都清零饥饿计时
        for (var i = 0; i < 5; i++)
        {
            Advance(100);
            hero.Gold += 1;
            Heroes().ShouldContain(_e => Equals(_e, hero), $"第 {World.Tick} 步：进账即被满足，不离队");
        }

        Heroes().Count.ShouldBe(1, "持续零星收入 → 冒险者不退场（自养闭环不受流失机制破坏）");
    }

    [Fact]
    public void 酒馆饥饿_想喝喝不上满阈值离队()
    {
        Advance(25);
        FreezeRecruitment();
        var hero = Heroes().Single();
        Isolate(hero, morale: LogicWorld.MoraleDrinkThreshold - 1, gold: 0); // 想喝 + 没钱 = 喝不上

        EconomyEventKind? leaveReason = null;
        World.EconomyEvent += e =>
        {
            if (e.Kind is EconomyEventKind.HeroLeftNoBounty or EconomyEventKind.HeroLeftNoTavern)
            {
                leaveReason = e.Kind;
            }
        };

        // 赏金线与酒馆线同时累积；NeedStarveLeaveTicks 处两线同满 — 离队按检查顺序报赏金饥饿
        Advance(LogicWorld.NeedStarveLeaveTicks + 10);
        leaveReason.ShouldBe(EconomyEventKind.HeroLeftNoBounty, "两线同满时按检查顺序离队（赏金线先判）");
        Heroes().Count.ShouldBe(0, "需求长期不被满足 → 离队，无新人补位");
    }

    [Fact]
    public void 酒馆可及_想喝喝得上不离队()
    {
        Advance(25);
        FreezeRecruitment();
        var hero = Heroes().Single();
        Isolate(hero, morale: LogicWorld.MoraleDrinkThreshold - 1, gold: 50); // 有钱 → canDrink → 酒馆线清零

        // 酒馆线被满足；赏金线用零星进账压住（模拟自养零花收入）— 冒险者永不离队
        for (var i = 0; i < 5; i++)
        {
            hero.Gold += 1; // 注入先于推进：进账即清零赏金饥饿
            Tankify(hero);  // 隔离：钱包 > 装备价会走去市场，路上被怪群误伤也不掉线
            Advance(LogicWorld.NeedStarveLeaveTicks / 5);
            Heroes().ShouldContain(_e => Equals(_e, hero), $"第 {World.Tick} 步：需求可被满足 → 不离队");
        }
    }

    [Fact]
    public void 不想喝_酒馆线恒清零_赏金线照常累计()
    {
        Advance(25);
        FreezeRecruitment();
        var hero = Heroes().Single();
        Isolate(hero, morale: 100, gold: 0); // 不想喝 → 酒馆线清零；零钱 → 赏金线走

        Advance(LogicWorld.NeedStarveLeaveTicks - 1);
        hero.TavernStarveTicks.ShouldBe(0, "不想喝 → 酒馆线恒清零");
        hero.NoIncomeTicks.ShouldBe(LogicWorld.NeedStarveLeaveTicks - 1, "零收入线照常累计");
        Heroes().ShouldContain(_e => Equals(_e, hero), "未满阈值 → 不离队");
    }
}
