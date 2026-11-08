#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace InfinityLord.Tests;

/// <summary>
/// 只挂赏基线长跑 — design.md 3 节闭环验证标准的正式实验（2026-09-21 会话九）。
/// 命题：玩家只挂赏（唯一动词）、不碰其他任何系统的情况下，世界能否进入
/// 「威胁↔定价↔税收」的动态平衡或螺旋——两种走向都成立才算循环健康。
/// 策略（最简确定性代理玩家，不用建议价——按建议价挂是作弊，Majesty 玩家读行情但有自己的策略）：
/// - 每 30 步（3 秒）决策一次；
/// - 威胁响应：若存在距任一玩家建筑 < 20 的怪物，按固定 $30 挂赏（覆盖地精阈值价 19.2 有余）；
/// - 巢穴压制：若金库 ≥ $100 且无近身威胁，对最近巢穴按其威胁折价 ×1.2 挂赏（清剿动机）。
/// 对照组：BaselineLongRunTests（零干预）已证明英雄灭绝、怪物无界堆积——
/// 本测试回答「只挂赏能否守住」。
/// </summary>
public class BountyOnlyLongRunTests : LogicWorldTestBase
{
    private const int RunTicks = 6000;
    private const int SampleEvery = 600;
    private const int DecideEvery = 30;
    private const float NearThreatRadius = 20f;
    private const int ThreatBounty = 30;

    [Fact]
    public void 只挂赏演化6000步_闭环健康检验()
    {
        var report = new StringBuilder();
        report.AppendLine("# 只挂赏基线长跑（seed 42，6000 步，每 30 步确定性挂赏策略）");
        report.AppendLine();
        report.AppendLine("| tick | 金库 | 英雄 | 均Lv | 均装备 | 存活怪 | 累计击杀 | 英雄阵亡 | 离队(赏/酒) | 悬赏支付Σ | 活跃旗 | 建筑 |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");

        for (var block = 0; block < RunTicks / SampleEvery; block++)
        {
            // 采样窗口内每 30 步跑一次代理玩家决策（确定性，读快照 → 挂赏）
            for (var d = 0; d < SampleEvery / DecideEvery; d++)
            {
                RunBountyOnlyPolicy();
                World.Advance(DecideEvery);
            }

            var heroes = World.Units.Where(u => u.Side == Faction.Hero && u.Health > 0).ToList();
            var mobs = World.Units.Where(u => u.Side == Faction.Monster && u.Health > 0).ToList();
            var heroDeaths = Log.Deaths.Count(d => d.Side == Faction.Hero);
            var kills = Log.Combats.Count(c => c.TargetDied && c.TargetId.StartsWith("mob_"));
            var paid = Log.Bounties.Where(b => b.Kind == BountyEventKind.Paid).Sum(b => (long)b.Gold);
            var activeFlags = World.Flags.Count(f => !f.Settled);
            var bldgs = string.Join(",", World.Buildings.Select(b => b.Id));

            report.AppendLine(
                $"| {World.Tick} | {World.Treasury} | {heroes.Count} " +
                $"| {(heroes.Count == 0 ? 0 : heroes.Average(h => (double)h.Level)):F1} " +
                $"| {(heroes.Count == 0 ? 0 : heroes.Average(h => (double)h.Gear)):F1} | {mobs.Count} | {kills} | {heroDeaths} " +
                $"| {Log.Count(EconomyEventKind.HeroLeftNoBounty)}/{Log.Count(EconomyEventKind.HeroLeftNoTavern)} " +
                $"| {paid} | {activeFlags} | {bldgs} |");

            // 守恒不变量（采样点）— 清单与公式在 LogicWorldTestBase.AssertConservation 单点维护
            AssertConservation($"（第 {World.Tick} 步 — 只挂赏长跑中途）");
        }

        report.AppendLine();
        report.AppendLine("## 终局快照（第 " + World.Tick + " 步）");
        report.AppendLine("```");
        foreach (var line in World.DebugSnapshot().Split('\n')) report.AppendLine(line);
        report.AppendLine("```");

        var path = Path.Combine(AppContext.BaseDirectory, "bounty-only-run-report.md");
        File.WriteAllText(path, report.ToString());
        TestLog.Ledger("报告已写入: " + path);
    }

    /// <summary>
    /// 最简确定性挂赏策略（代理玩家）：
    /// 1) 撤销无效旗不设——退款机制自动处理；
    /// 2) 近身威胁优先：距任一玩家建筑 &lt; 20 的怪挂 $30；
    /// 3) 无近身威胁且金库 ≥ $100：对距玩家建筑最近的巢穴挂其威胁×1.2 取整（清剿价）。
    /// 同一目标已有活跃旗则不再追加（避免无限抬价）。
    /// </summary>
    private void RunBountyOnlyPolicy()
    {
        var playerBuildings = World.Buildings.Where(b => b.Owner == Faction.Hero).ToList();
        if (playerBuildings.Count == 0) return; // 全毁 — 无可守护（终局已定）

        // 锚点 = 最近的行会（切片 10 波次上线后行会可能被推平 — 退化为任一玩家建筑，
        // 原 First 谓词无匹配时会抛，代理玩家不能因建筑损失而崩）
        var anchor = playerBuildings
            .FirstOrDefault(b => b.Kind is BuildingKind.GuildHall or BuildingKind.GuildWarrior)
            ?.Position ?? playerBuildings[0].Position;

        // 1) 近身威胁响应
        var nearMob = World.Units
            .Where(u => u.Side == Faction.Monster && u.Health > 0)
            .Select(m => (Mob: m, Dist: playerBuildings.Min(b => Vector2.Distance(m.Position, b.Position))))
            .Where(x => x.Dist < NearThreatRadius)
            .OrderBy(x => x.Dist)
            .FirstOrDefault();
        if (nearMob.Mob is not null)
        {
            var already = World.Flags.Any(f => !f.Settled && f.TargetId == nearMob.Mob.Id);
            if (!already) World.PlaceBountyFlag(nearMob.Mob.Id, ThreatBounty);
            return;
        }

        // 2) 巢穴清剿（金库富余才发动 — 机会成本决策的代理）
        if (World.Treasury >= 100)
        {
            var lair = World.Buildings
                .Where(b => b.Owner == Faction.Monster && b.Health > 0)
                .OrderBy(b => Vector2.Distance(b.Position, anchor))
                .FirstOrDefault();
            if (lair is not null)
            {
                var already = World.Flags.Any(f => !f.Settled && f.TargetId == lair.Id);
                if (!already)
                {
                    var price = World.SuggestedPrice(lair.Id);
                    if (price > 0) World.PlaceBountyFlag(lair.Id, price);
                }
            }
        }
    }
}