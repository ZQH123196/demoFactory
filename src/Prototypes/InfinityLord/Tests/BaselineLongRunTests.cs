#nullable enable
using System;
using System.Linq;
using System.Text;
using System.IO;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace InfinityLord.Tests;

/// <summary>
/// 零干预基线长跑 — 切片 10「终局」前置实验（2026-09-21 会话九）。
/// 目的：
/// 1. design.md 3 节闭环验证标准「零干预下威胁↔定价↔税收动态平衡或螺旋」的定量回答；
/// 2. design.md「基线长跑实测」那一节的实测数据；
/// 3. 为威胁曲线（切片 10）校准提供基线：无干预世界的刷怪/清怪/经济产出水平。
/// 方法：固定种子 42，零玩家输入（不挂赏/不营建/不排布），6000 步（10 分钟）每 600 步（1 分钟）采样，
/// 曲线写 baseline-run-report.md（测试程序集目录）。断言只守恒不变量——
/// 基线是测量不是规范，数值结论（人口收敛点/怪物堆积与否）先记录后定断言。
/// </summary>
public class BaselineLongRunTests : LogicWorldTestBase
{
    private const int RunTicks = 6000;
    private const int SampleEvery = 600;

    [Fact]
    public void 放任演化6000步_零干预基线采样()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("# 零干预基线长跑（seed 42，6000 步 ≈ 游戏时间 10 分钟，零玩家输入）");
        report.AppendLine();
        report.AppendLine("| tick | 金库 | 周期净额 | 英雄 | 均Lv | 均装备 | 钱包Σ | 存活怪 | 累计击杀 | 英雄阵亡 | 离队(赏/酒) | 村民税 | 战利品 | 消费 | 回流 | 悬赏结算 | 建筑 |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        for (var block = 0; block < RunTicks / SampleEvery; block++)
        {
            World.Advance(SampleEvery);

            var heroes = World.Units.Where(u => u.Side == Faction.Hero && u.Health > 0).ToList();
            var mobs = World.Units.Where(u => u.Side == Faction.Monster && u.Health > 0).ToList();
            var heroDeaths = Log.Deaths.Count(d => d.Side == Faction.Hero);
            var kills = Log.Combats.Count(c => c.TargetDied && c.TargetId.StartsWith("mob_"));
            var leftNoBounty = Log.Count(EconomyEventKind.HeroLeftNoBounty);
            var leftNoTavern = Log.Count(EconomyEventKind.HeroLeftNoTavern);
            var villagerTax = Log.Economy.Where(e => e.Kind == EconomyEventKind.VillagerTax).Sum(e => (long)e.Amount);
            var loot = Log.Economy.Where(e => e.Kind == EconomyEventKind.KillLoot).Sum(e => (long)e.Amount);
            var spent = Log.Economy.Where(e => e.Kind == EconomyEventKind.HeroSpent).Sum(e => (long)e.Amount);
            var taxBack = Log.Economy.Where(e => e.Kind == EconomyEventKind.TaxCollected).Sum(e => (long)e.Amount);
            var bountyPaid = Log.Bounties.Count(b => b.Kind == BountyEventKind.Paid);
            var avgLv = heroes.Count == 0 ? 0 : heroes.Average(h => (double)h.Level);
            var avgGear = heroes.Count == 0 ? 0 : heroes.Average(h => (double)h.Gear);
            var heroGold = heroes.Sum(h => (long)h.Gold);
            var bldgs = string.Join(",", World.Buildings.Select(b => b.Id));

            report.AppendLine(
                $"| {World.Tick} | {World.Treasury} | {World.LastCycleNet} | {heroes.Count} " +
                $"| {avgLv:F1} | {avgGear:F1} | {heroGold} | {mobs.Count} | {kills} | {heroDeaths} " +
                $"| {leftNoBounty}/{leftNoTavern} | {villagerTax} | {loot} | {spent} | {taxBack} | {bountyPaid} | {bldgs} |");

            // 守恒不变量（采样点断言）— 清单与公式在 LogicWorldTestBase.AssertConservation 单点维护
            AssertConservation($"（第 {World.Tick} 步 — 零干预长跑中途）");
        }

        // 终局状态明细（人口构成与动机分布 — 供分析读）
        report.AppendLine();
        report.AppendLine("## 终局快照（第 " + World.Tick + " 步）");
        report.AppendLine("```");
        foreach (var line in World.DebugSnapshot().Split('\n')) report.AppendLine(line);
        report.AppendLine("```");

        var path = Path.Combine(AppContext.BaseDirectory, "baseline-run-report.md");
        File.WriteAllText(path, report.ToString());
        TestLog.Ledger("报告已写入: " + path);
    }
}