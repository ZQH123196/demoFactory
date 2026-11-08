#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>一局对照结果 —— 一场自动战斗的可比较三样东西：谁赢、打多久、死了多少。</summary>
public sealed record MatchResult(
    string FormationKey,
    string FormationName,
    int Seed,
    BattleOutcome Outcome,
    long DurationTicks,
    int OurDeaths,
    int EnemyDeaths,
    int OurFielded,
    int EnemyFielded,
    IReadOnlyList<int> OurDeathsAt,
    IReadOnlyList<int> EnemyDeathsAt,
    IReadOnlyList<int> SampleTicks)
{
    /// <summary>打多久（秒）= 步数 ÷ 10。</summary>
    public double DurationSeconds => DurationTicks / 10.0;

    /// <summary>这局有没有出结果（判定过就是出结果，超时判赢也算）。</summary>
    public bool Decided => Outcome != BattleOutcome.Running;

    /// <summary>是不是「打满 120 秒没死人、按契约判赢」—— 与真把对面打光区分开（对照跑有测试盯这一列）。</summary>
    public required bool DecidedByTimeout { get; init; }

    /// <summary>
    /// 开局 12 秒（第 120 步）那一格的伤亡：设计要的两个记账点之一（design.md 2 节 第 6 条）。
    /// 采样比这一格密，所以它总是记得到；若某局连 12 秒都没撑过（不会发生，实测最快 20 秒），取末样兜着。
    /// </summary>
    public int OurDeathsAtOpening => AtOrLast(OurDeathsAt);
    public int EnemyDeathsAtOpening => AtOrLast(EnemyDeathsAt);

    private int AtOrLast(IReadOnlyList<int> samples)
        => samples.Count == 0 ? 0 : samples[System.Math.Min(PlacementComparison.OpeningSampleIndex, samples.Count - 1)];
}

/// <summary>一种摆法在 20 粒种子上的汇总。</summary>
public sealed class FormationSummary
{
    public required Formation Formation { get; init; }
    public required IReadOnlyList<MatchResult> Matches { get; init; }

    public double Wins => Matches.Count(m => m.Outcome == BattleOutcome.PlayerVictory);
    public double Losses => Matches.Count(m => m.Outcome == BattleOutcome.PlayerDefeat);

    /// <summary>同归于尽的局数（既没赢也没输）。</summary>
    public double Draws => Matches.Count(m => m.Outcome == BattleOutcome.Draw);

    /// <summary>
    /// 其中「打满 120 秒没死人、按契约判赢」的局数。满编实测恒为 0 ——
    /// 这条不为统计存在，为报警存在：一旦别零，说明数值把仗拖回了上限，摆位结论要重量。
    /// </summary>
    public double TimeoutWins => Matches.Count(m => m.DecidedByTimeout);

    public double WinRate => Matches.Count == 0 ? 0 : Wins / Matches.Count;
    public double LossRate => Matches.Count == 0 ? 0 : Losses / Matches.Count;
    public double MeanDurationSeconds => Matches.Count == 0 ? 0 : Matches.Average(m => m.DurationSeconds);
    public double MinDurationSeconds => Matches.Count == 0 ? 0 : Matches.Min(m => m.DurationSeconds);
    public double MaxDurationSeconds => Matches.Count == 0 ? 0 : Matches.Max(m => m.DurationSeconds);
    public double MeanOurDeaths => Matches.Count == 0 ? 0 : Matches.Average(m => (double)m.OurDeaths);
    public double MeanEnemyDeaths => Matches.Count == 0 ? 0 : Matches.Average(m => (double)m.EnemyDeaths);
    public double MeanOurDeathsAtOpening => Matches.Count == 0 ? 0 : Matches.Average(m => (double)m.OurDeathsAtOpening);
    public double MeanEnemyDeathsAtOpening => Matches.Count == 0 ? 0 : Matches.Average(m => (double)m.EnemyDeathsAtOpening);

    /// <summary>伤亡差（我方阵亡 − 敌方阵亡）：越小越好，摆位有没有用主要看这一列的位移。</summary>
    public double MeanDeathMargin => MeanOurDeaths - MeanEnemyDeaths;

    /// <summary>开局第 12 秒的伤亡差（我方 − 敌方）——「站位的好处能撑到第几秒」的头一格。</summary>
    public double MeanDeathMarginAtOpening => MeanOurDeathsAtOpening - MeanEnemyDeathsAtOpening;

    /// <summary>按采样点平均双方累计阵亡 —— 「站位的好处撑到第几秒」这条曲线的原始数据（首次取用时算一次）。</summary>
    private IReadOnlyList<(int SampleTick, double OurDeaths, double EnemyDeaths)>? _timeline;

    public IReadOnlyList<(int SampleTick, double OurDeaths, double EnemyDeaths)> Timeline
        => _timeline ??= BuildTimeline();

    private IReadOnlyList<(int, double, double)> BuildTimeline()
    {
        var rows = new List<(int, double, double)>();
        if (Matches.Count == 0) return rows;
        var longest = Matches.Max(m => m.SampleTicks.Count);
        for (var i = 0; i < longest; i++)
        {
            var tick = Matches.Where(m => m.SampleTicks.Count > i).Select(m => m.SampleTicks[i]).First();
            var ours = Matches.Where(m => m.OurDeathsAt.Count > i).Average(m => (double)m.OurDeathsAt[i]);
            var theirs = Matches.Where(m => m.EnemyDeathsAt.Count > i).Average(m => (double)m.EnemyDeathsAt[i]);
            rows.Add((tick, ours, theirs));
        }
        return rows;
    }
}

/// <summary>对照跑的裁决 —— 判绿＝摆位有因果力，判红＝摆位看不出有因果力，判不准＝样本不够、如实记。</summary>
public enum PlacementVerdict
{
    /// <summary>判绿：换摆位真的换结果 —— 核心循环成立，M5~M8 可以继续。</summary>
    Matters,

    /// <summary>判红：三种摆法结果基本一样 —— 「排兵布阵」这句话的因果力不存在。</summary>
    NoEffect,

    /// <summary>拿不准：过了「判绿」的线但没到「判红」的线 —— 加样本再判，不硬判。</summary>
    Unclear,
}

/// <summary>对照跑的结果与裁决。</summary>
public sealed class ComparisonResult
{
    public required IReadOnlyList<FormationSummary> Summaries { get; init; }
    public required IReadOnlyList<int> Seeds { get; init; }
    public required PlacementVerdict Verdict { get; init; }
    public required string Reason { get; init; }

    /// <summary>我方阵亡最少 / 最多两种摆法（要对照的就是这一对）。</summary>
    public required FormationSummary Best { get; init; }
    public required FormationSummary Worst { get; init; }

    /// <summary>同种子配对下，「最好摆法比最差摆法死得少（或持平且更优）」的局数。</summary>
    public required int PairedAgreement { get; init; }

    /// <summary>最好与最差摆法的我方平均阵亡差（兵数）。</summary>
    public double MeanDeathsGap => Worst.MeanOurDeaths - Best.MeanOurDeaths;
}

/// <summary>
/// 摆位对照测试（design.md 2 节 第 6 条、3 节那张「测什么」的说明）——
/// 兵不动、种子不动、只换摆位：我方三种摆法各跑同一组 20 粒固定种子，
/// 敌方与我方镜像同阵容、固定用常规摆法，于是三档之间任何差异都只能来自「谁站哪一格」。
///
/// 比三样：谁赢、打多久、死了多少；开局第 12 秒记一次、打完再记一次
/// （两格各记一次的额外产出：站位的好处能撑到第几秒）。
///
/// 定死的是**跑之前就登记好的三个数**（下面的常量），不是看完结果再挑：
/// 判绿的成立条件与判红的成立条件之间留了一段「拿不准」区间 ——
/// 落在那段里就如实写「样本不够」，不硬判。
/// </summary>
public static class PlacementComparison
{
    /// <summary>每种摆法跑几局（= 固定种子清单的大小）。</summary>
    public const int SampleCount = 20;

    /// <summary>判绿的伤亡门槛：最好与最差摆法的我方平均阵亡差 ≥ 1.5 兵（满编 15 的 10%）。</summary>
    public const float MattersMeanDeathsGap = 1.5f;

    /// <summary>判绿的配对门槛：20 局里 ≥ 15 局方向一致（75%）—— 防的是「一局极值拉平平均数」。</summary>
    public const int MattersPairedAgreement = 15;

    /// <summary>判红的伤亡门槛：平均阵亡差 &lt; 0.5 兵就认为摆位看不出作用。</summary>
    public const float NoEffectMeanDeathsGap = 0.5f;

    /// <summary>判红的配对门槛：同向局数 &lt; 12（60%）就认为摆位看不出作用。</summary>
    public const int NoEffectPairedAgreement = 12;

    /// <summary>固定种子清单（1001~1020）—— 三种摆法用同一组，配对比较才成立。</summary>
    public static IReadOnlyList<int> Seeds { get; } = [.. Enumerable.Range(1001, SampleCount)];

    /// <summary>敌方固定摆法：与我方镜像同阵容，用常规线（阵容差异必须消掉）。</summary>
    public static Formation EnemyFormation => Formations.ShieldWall;

    /// <summary>
    /// 对照跑的种族档（M8 补的<b>控制变量</b>）：两侧穿同一族。
    ///
    /// 为什么必须钉这一格：M4 立这把尺时的前提是「敌我是同一张兵种表的镜像 ⇒ 三档之间任何差都只能来自摆位」。
    /// M8 之后两族各带一条规则（人族射程 ×1.1、兽人攻击 ×1.1），照默认对阵跑就等于把「种族」放进了变量里 ——
    /// 那不是摆位的差。这一格把它钉住，与 M7「每一档都按同一个排布函数入场」是同一条思路。
    /// 选哪一族都一样（两侧同族才谈得上对称），写死一族是为了报告可逐字复现。
    /// </summary>
    public const Race ComparisonRace = Race.Human;

    /// <summary>把一份摆法钉到对照档的种族上（格子一个不动，只定「谁穿哪族」）。</summary>
    public static Formation Controlled(Formation formation) => formation.Wearing(ComparisonRace);

    /// <summary>
    /// 伤亡采样间隔：**每 3 秒一格**（= 30 步）。
    /// 为什么不照设计原话「每 30 秒记一次」采：一局 25~31 秒就打完（契约已按此定稿，
    /// design.md 「已定」第 5 条），30 秒那一格永远等于终局那一格 —— 曲线塌成一个点，
    /// 「站位的好处撑到第几秒」根本问不出来。采密了才能既保住开局那一格
    /// （<see cref="OpeningSampleIndex"/>，第 12 秒），又画出整局的走向。
    /// </summary>
    public const int SampleEverySeconds = 3;

    /// <summary>采样间隔换算成步。</summary>
    public const int SampleEveryTicks = SampleEverySeconds * BattleContract.StepsPerSecond;

    /// <summary>开局那一格（第 12 秒）在采样序列里的下标。</summary>
    public const int OpeningSampleIndex = BattleContract.OpeningCheckpointTicks / SampleEveryTicks - 1;

    /// <summary>报告里伤亡曲线每隔多少秒开一列（6 秒 = 60 步；3 秒采一格、6 秒晒一列，一半留给明细）。</summary>
    public const int ReportColumnEverySeconds = 6;

    /// <summary>报告列距换算成步。</summary>
    public const int ReportColumnEveryTicks = ReportColumnEverySeconds * BattleContract.StepsPerSecond;

    /// <summary>跑整套对照（3 摆法 × 20 种子 = 60 场；纯逻辑，毫秒~秒级）。</summary>
    public static ComparisonResult Run(IReadOnlyList<Formation>? playerFormations = null, bool jitter = true)
    {
        var formations = (playerFormations ?? Formations.ComparisonSet).Select(Controlled).ToList();
        var enemy = Controlled(EnemyFormation);
        var summaries = new List<FormationSummary>(formations.Count);

        foreach (var formation in formations)
        {
            var matches = new List<MatchResult>(Seeds.Count);
            foreach (var seed in Seeds)
            {
                matches.Add(RunMatch(seed, formation, enemy, jitter));
            }
            summaries.Add(new FormationSummary { Formation = formation, Matches = matches });
        }

        var best = summaries.OrderBy(s => s.MeanOurDeaths).First();
        var worst = summaries.OrderByDescending(s => s.MeanOurDeaths).First();
        var agreement = PairedAgreement(best, worst);
        var gap = worst.MeanOurDeaths - best.MeanOurDeaths;

        var verdict = PlacementVerdict.Unclear;
        var reason = "";
        if (gap >= MattersMeanDeathsGap && agreement >= MattersPairedAgreement)
        {
            verdict = PlacementVerdict.Matters;
            reason = $"{best.Formation.DisplayName} 比 {worst.Formation.DisplayName} 平均少死 {F2(gap)} 兵，" +
                     $"且 {agreement}/{SampleCount} 局同向 ⇒ 摆位有因果力";
        }
        else if (gap < NoEffectMeanDeathsGap || agreement < NoEffectPairedAgreement)
        {
            verdict = PlacementVerdict.NoEffect;
            reason = $"平均阵亡差 {F2(gap)} 兵（低于 {F2(NoEffectMeanDeathsGap)}）或同向 {agreement}/{SampleCount} 局" +
                     $"（低于 {NoEffectPairedAgreement}） ⇒ 摆位看不出因果力";
        }
        else
        {
            reason = $"平均阵亡差 {F2(gap)} 兵、同向 {agreement}/{SampleCount} 局 —— 过了判绿线的一半、没到判红线，" +
                     $"如实记「样本不够」，加种子再判";
        }

        return new ComparisonResult
        {
            Summaries = summaries,
            Seeds = Seeds,
            Verdict = verdict,
            Reason = reason,
            Best = best,
            Worst = worst,
            PairedAgreement = agreement,
        };
    }

    /// <summary>
    /// 跑一场：同一套兵、一粒种子，从开打跑到判定或 120 秒上限，每 3 秒记一次双方累计阵亡。
    /// 机制断言也用它（可以传小摆法、关抖动，拿到能手算的数字）。
    /// </summary>
    public static MatchResult RunMatch(int seed, Formation player, Formation enemy, bool jitter = true)
    {
        using var world = new BattleWorld(seed, player, enemy, jitter);
        var sampleTicks = new List<int>();
        var ourDeaths = new List<int>();
        var enemyDeaths = new List<int>();

        while (world.Outcome == BattleOutcome.Running && world.Tick < world.MaxBattleTicks)
        {
            var remaining = (int)(world.MaxBattleTicks - world.Tick);
            world.Advance(Math.Min(SampleEveryTicks, remaining));
            sampleTicks.Add((int)world.Tick);
            ourDeaths.Add(world.Deaths(Side.Player));
            enemyDeaths.Add(world.Deaths(Side.Enemy));
        }

        var duration = world.OutcomeTick ?? world.Tick;
        return new MatchResult(
            player.Key, player.DisplayName, seed, world.Outcome, duration,
            world.Deaths(Side.Player), world.Deaths(Side.Enemy),
            world.Fielded(Side.Player), world.Fielded(Side.Enemy),
            ourDeaths, enemyDeaths, sampleTicks)
        {
            DecidedByTimeout = world.DecidedByTimeout,
        };
    }

    /// <summary>
    /// 同种子配对下，最好摆法比最差摆法「这一局我方死得更少」的局数。
    /// 死得一样多的平局不算同向 —— 它既不加分也不减分，逐局明细里看得见。
    /// </summary>
    public static int PairedAgreement(FormationSummary better, FormationSummary worse)
    {
        var agreement = 0;
        foreach (var worseMatch in worse.Matches)
        {
            var betterMatch = better.Matches.FirstOrDefault(m => m.Seed == worseMatch.Seed);
            if (betterMatch is null) continue;
            if (betterMatch.OurDeaths < worseMatch.OurDeaths) agreement++;
        }
        return agreement;
    }

    /// <summary>把对照跑渲染成 markdown 报告（数字全部来自同一次 Run，报告即证据）。</summary>
    public static string RenderReport(ComparisonResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 摆位对照测试报告（M4）");
        sb.AppendLine();
        sb.AppendLine("同一套兵（4 坦 4 战 4 弓 3 骑 = 满编 15 格）、同一组 " + result.Seeds.Count +
                      " 粒固定种子，只换摆法；敌方与我方镜像同阵容、固定用「" + EnemyFormation.DisplayName + "」。");
        sb.AppendLine("两侧同穿" + RaceRules.Name(ComparisonRace) + "（M8 起的控制变量：两族各带一条种族规则，" +
                      "不同族就把「种族」也放进了变量，量出来的差不全是摆位的）⇒ 三档之间的差只可能来自「谁站哪一格」。");
        sb.AppendLine("比三样：谁赢、打多久、死了多少。开局第 " + BattleContract.OpeningCheckpointSeconds +
                      " 秒记一次、打完再记一次；" +
                      $"伤亡每 {SampleEverySeconds} 秒采一格（每 {SampleEveryTicks} 步）。");
        sb.AppendLine();
        sb.AppendLine("## 汇总（每种摆法 " + result.Seeds.Count + " 局）");
        sb.AppendLine();
        sb.AppendLine("| 摆法 | 胜率 | 败率 | 同尽 | 超时判赢 | 用时 平均/最快/最慢(秒) | 我方平均阵亡 | 敌方平均阵亡 | 伤亡差 | 开局" +
                      BattleContract.OpeningCheckpointSeconds + "秒伤亡差 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var s in result.Summaries)
        {
            sb.AppendLine($"| {s.Formation.DisplayName} | {Pct(s.WinRate)} | {Pct(s.LossRate)} | {Pct(s.Draws / s.Matches.Count)} " +
                          $"| {(int)s.TimeoutWins} " +
                          $"| {F2(s.MeanDurationSeconds)} / {F2(s.MinDurationSeconds)} / {F2(s.MaxDurationSeconds)} " +
                          $"| {F2(s.MeanOurDeaths)} | {F2(s.MeanEnemyDeaths)} " +
                          $"| {F2(s.MeanDeathMargin)} | {F2(s.MeanDeathMarginAtOpening)} |");
        }
        sb.AppendLine();
        var allMatches = result.Summaries.SelectMany(s => s.Matches).ToList();
        var timeoutWins = (int)allMatches.Count(m => m.DecidedByTimeout);
        sb.AppendLine($"{allMatches.Count} 局里没有一局打满 {BattleContract.MaxBattleSeconds} 秒上限" +
                      $"（超时判赢 {timeoutWins} 局）—— 最慢的一局 {F2(allMatches.Max(m => m.DurationSeconds))} 秒、" +
                      $"最快 {F2(allMatches.Min(m => m.DurationSeconds))} 秒；" +
                      $"那条上限按 design.md 「已定」第 5 条只是兜底，碰到了就是数值把仗拖回去了，摆位结论要重量。");
        sb.AppendLine();
        sb.AppendLine($"## 站位的好处撑到第几秒（每 {ReportColumnEveryTicks / 10} 秒的平均累计阵亡 我/敌）");
        sb.AppendLine();
        var columnTicks = result.Summaries
            .SelectMany(s => s.Timeline.Select(t => t.SampleTick))
            .Where(t => t % ReportColumnEveryTicks == 0)
            .Distinct().OrderBy(t => t).ToList();
        if (columnTicks.Count == 0) columnTicks.Add(SampleEveryTicks);
        var header = new StringBuilder("| 摆法 |");
        var divider = new StringBuilder("|---|");
        foreach (var tick in columnTicks)
        {
            header.Append($" {tick / 10}秒 |");
            divider.Append("---|");
        }
        sb.AppendLine(header.ToString());
        sb.AppendLine(divider.ToString());
        foreach (var s in result.Summaries)
        {
            sb.Append($"| {s.Formation.DisplayName} |");
            foreach (var tick in columnTicks)
            {
                var row = s.Timeline.FirstOrDefault(t => t.SampleTick == tick);
                sb.Append(row.SampleTick == 0
                    ? " - |"
                    : $" {F1(row.OurDeaths)}/{F1(row.EnemyDeaths)} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("（每一格 = 该摆法 20 局在该时刻的平均累计阵亡；已打完的局按终局值计入，" +
                      "所以曲线走到后段会并成一条水平线。）");
        sb.AppendLine();
        sb.AppendLine("## 逐局明细（配对设计：同一行三种摆法吃的是同一粒种子）");
        sb.AppendLine();
        sb.AppendLine("| 种子 | " + string.Join(" | ", result.Summaries.Select(s => s.Formation.Key)) + " |");
        sb.AppendLine("|---|" + string.Join("---|", result.Summaries.Select(_ => "")) + "|");
        for (var i = 0; i < result.Seeds.Count; i++)
        {
            sb.Append($"| {result.Seeds[i]} |");
            foreach (var s in result.Summaries)
            {
                var m = s.Matches[i];
                sb.Append($" {ShortOutcome(m.Outcome)} {m.DurationTicks / 10.0:F0}秒 我方死{m.OurDeaths} 敌方死{m.EnemyDeaths}" +
                          $"（开局{BattleContract.OpeningCheckpointSeconds}秒 我{m.OurDeathsAtOpening}/敌{m.EnemyDeathsAtOpening}） |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("## 跑之前登记好的三个数");
        sb.AppendLine();
        sb.AppendLine($"- 判绿：最好与最差摆法的我方平均阵亡差 ≥ {F2(MattersMeanDeathsGap)} 兵，" +
                      $"且同种子配对同向 ≥ {MattersPairedAgreement}/{SampleCount} 局。");
        sb.AppendLine($"- 判红：平均阵亡差 < {F2(NoEffectMeanDeathsGap)} 兵，或同向 < {NoEffectPairedAgreement}/{SampleCount} 局。");
        sb.AppendLine("- 落在这两条之间：判「拿不准」，加样本再判，不硬判。");
        sb.AppendLine();
        sb.AppendLine($"**最好摆法**：{result.Best.Formation.DisplayName}（平均阵亡 {F2(result.Best.MeanOurDeaths)}）；" +
                      $"**最差摆法**：{result.Worst.Formation.DisplayName}（平均阵亡 {F2(result.Worst.MeanOurDeaths)}）；" +
                      $"**平均阵亡差** {F2(result.MeanDeathsGap)} 兵；**同向** {result.PairedAgreement}/{SampleCount} 局。");
        sb.AppendLine();
        sb.AppendLine($"## 结论：**{VerdictText(result.Verdict)}**");
        sb.AppendLine();
        sb.AppendLine(result.Reason);
        return sb.ToString();
    }

    public static string VerdictText(PlacementVerdict verdict) => verdict switch
    {
        PlacementVerdict.Matters => "判绿 — 摆位有因果力",
        PlacementVerdict.NoEffect => "判红 — 摆位看不出有因果力",
        PlacementVerdict.Unclear => "拿不准 — 样本不够，加种子再判",
        _ => verdict.ToString(),
    };

    private static string ShortOutcome(BattleOutcome outcome) => outcome switch
    {
        BattleOutcome.PlayerVictory => "胜",
        BattleOutcome.PlayerDefeat => "败",
        BattleOutcome.Draw => "同尽",
        _ => "进行中",
    };

    private static string Pct(double ratio) => $"{ratio * 100:F0}%";
    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
    private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
