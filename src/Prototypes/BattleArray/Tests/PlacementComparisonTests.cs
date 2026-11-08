#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace BattleArray.Tests;

/// <summary>
/// 摆位对照测试 —— M4 的生死线（design.md 2 节 第 6 条 + 3 节「这个测试在测什么」）。
///
/// 做法：兵不动（三种摆法都是 4 坦 4 战 4 弓 3 骑）、种子不动（同一组 20 粒固定种子）、
/// 只换摆位；敌方与我方镜像同阵容、固定用常规摆法。每局记三样：谁赢、打多久、死了多少，
/// 开局第 12 秒记一次、打完再记一次。
///
/// 门槛在跑之前就登记（三个数写在 <see cref="PlacementComparison"/>），本测试不挑数字：
/// 结构断言（跑满了 60 场、每场都在 120 秒上限内、开局那一格真记了、没有一局靠超时判赢、
/// 报告可逐字复现）在这里，
/// 判红判绿的**数字结论**由报告给出，由人写回 `docs/prototypes/battle-array/灰盒阶段结论.md`。
/// </summary>
public class PlacementComparisonTests
{
    [Fact]
    public void 三种摆法各跑20局_对照报告落地并给出判红判绿结论()
    {
        var result = PlacementComparison.Run();
        var report = PlacementComparison.RenderReport(result);
        var path = Path.Combine(AppContext.BaseDirectory, "placement-comparison-report.md");
        File.WriteAllText(path, report, new UTF8Encoding(false));

        TestLog.Ledger(report);
        TestLog.Ledger("报告已写入: " + path);

        // 结构断言：样本量与记账点是真的到位了，不是空跑
        result.Summaries.Select(s => s.Formation.Key).ShouldBe(new[] { "shield-wall", "archers-forward", "archers-midline" });
        foreach (var summary in result.Summaries)
        {
            summary.Matches.Count.ShouldBe(PlacementComparison.SampleCount, $"{summary.Formation.Key} 不是每档 20 局");
            summary.Matches.Select(m => m.Seed).ShouldBe(PlacementComparison.Seeds, $"{summary.Formation.Key} 用的不是同一组固定种子（配对设计被破坏）");

            foreach (var match in summary.Matches)
            {
                match.DurationTicks.ShouldBeLessThanOrEqualTo(BattleContract.MaxBattleTicks, $"{summary.Formation.Key} seed={match.Seed} 打满了 {BattleContract.MaxBattleSeconds} 秒上限");
                match.OurFielded.ShouldBe(Formations.FullRosterSize);
                match.EnemyFielded.ShouldBe(Formations.FullRosterSize);
                match.SampleTicks.First().ShouldBe(PlacementComparison.SampleEveryTicks, "伤亡采样第一格不在第 3 秒 —— 采样口径漂了");
                match.SampleTicks.ShouldContain(_e => Equals(_e, BattleContract.OpeningCheckpointTicks), $"开局那一格（第 {BattleContract.OpeningCheckpointSeconds} 秒）没采到 —— 采样间隔与记账点对不上");
                match.SampleTicks.Last().ShouldBeGreaterThanOrEqualTo((int)match.DurationTicks, "最后一格没盖到终局，终局那格记不上");
                match.SampleTicks.ShouldBeUnique("采样点重复了");
                match.Decided.ShouldBeTrue("状态没收敛 —— 既没判定也没到上限？");
            }
        }

        // 裁决自洽：最好/最差确实是按平均阵亡挑出来的
        result.Best.MeanOurDeaths.ShouldBeLessThanOrEqualTo(result.Worst.MeanOurDeaths);
        result.MeanDeathsGap.ShouldBe(result.Worst.MeanOurDeaths - result.Best.MeanOurDeaths, 1e-9);
        result.Verdict.ShouldBe(VerdictFor(result.MeanDeathsGap, result.PairedAgreement), "裁决与预先登记的三个数不一致");
        report.ShouldContain("## 结论：");
    }

    [Fact]
    public void 地基报警器_对照跑仍判绿_且没有一局靠超时判赢()
    {
        // 这条不是又一遍统计，是**地基用例**：M5 之后每改一次（上屏、行为树、兵种数据、M7 换阵容）
        // 都要能在 1 秒内回答「摆位还有没有因果力、仗还在不在 120 秒内打完」。
        // 判绿线是 M4 跑之前登记的三个数，不在这儿重设 —— 红了就是地基塌了，M5~M8 该停手重议。
        var result = PlacementComparison.Run();
        var all = result.Summaries.SelectMany(s => s.Matches).ToList();

        result.Verdict.ShouldBe(PlacementVerdict.Matters, $"摆位看不出因果力了（平均阵亡差 {result.MeanDeathsGap:F2} 兵、同向 {result.PairedAgreement}/{PlacementComparison.SampleCount} 局）" +
            "—— M4 判绿的地基塌了，后面每个包都是在装修空壳");
        all.Where(m => m.DecidedByTimeout).ShouldBeEmpty($"有 {all.Count(m => m.DecidedByTimeout)} 局打满 {BattleContract.MaxBattleSeconds} 秒靠超时判赢 —— " +
            "数值把仗拖回了上限，胜率与「站位好处撑到第几秒」这条曲线都要重量");
        all.Max(m => m.DurationSeconds).ShouldBeLessThan(BattleContract.MaxBattleSeconds / 2.0, $"最慢一局 {all.Max(m => m.DurationSeconds):F1} 秒，已经吃掉兜底上限的一半余量了");
    }

    [Fact]
    public void 对照跑重跑一遍_报告逐字相同()
    {
        // 60 场全是固定种子的确定演化 ⇒ 报告必须可逐字复现（做不到就是逻辑层混进了未受控状态）
        var first = PlacementComparison.RenderReport(PlacementComparison.Run());
        var second = PlacementComparison.RenderReport(PlacementComparison.Run());
        second.ShouldBe(first, "同一组种子两次对照跑的报告不一致");
    }

    /// <summary>阈值的第三处实现？不是 —— 这条断言只是把 PlacementComparison 里的同一个函数调一遍，
    /// 防的是「以后有人改了阈值却忘了改报告文案」。阈值单点仍在 PlacementComparison。</summary>
    private static PlacementVerdict VerdictFor(double meanDeathsGap, int pairedAgreement)
    {
        if (meanDeathsGap >= PlacementComparison.MattersMeanDeathsGap
            && pairedAgreement >= PlacementComparison.MattersPairedAgreement) return PlacementVerdict.Matters;
        if (meanDeathsGap < PlacementComparison.NoEffectMeanDeathsGap
            || pairedAgreement < PlacementComparison.NoEffectPairedAgreement) return PlacementVerdict.NoEffect;
        return PlacementVerdict.Unclear;
    }
}
