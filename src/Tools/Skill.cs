// 技能体检单 —— 一条命令出一页三栏（数值台账 / 时序胶卷 / 对照矩阵）+ 一道前摇对表校验。
//
// 这一层与任何具体技能无关：文件里不许出现技能名、tag 名、clip 名、原型名（同 Review.cs 的口径，
// 内容由原型侧的 SkillContract 递进来）。「怎么算合格」的出处在 `docs/research/技能系统/` 三页：
// 已删的设计档第一条（前摇的真值在 clip 里、但不许运行时问播放器，也不许两个数各写一遍）、
// 已删的设计档（本工位做到什么算完：故意把某个 clip 改得跟表不一样，前摇那一栏要能报出来。
//   由用户确认：判红只咬「动画比前摇短」那一侧，「动画比前摇长」印出超出量、不判红 ——
//   起手动作演多久是表现层的事，双向绝对差会把这条合法修法当场堵住，理由见已删的验证报告）、
// 「16 条清单」P5 那行（三栏的形状与量级）。
//
// 三件刻意的设计，改之前先读懂它们挡的是什么：
// 1. **数字不抄第二份**：栏 1 与栏 2 全部从真技能表投影（契约里那个 `Numbers` 口子），
//    本文件不存任何数值。报告与表对不上由原型 Tests 那条对表测试判红（A76）。
// 2. **clip 时长走引擎那份实现**：`Facts.cs` 读的是 `glb/inspect_model.gd` 写的 facts 文件，
//    而那个文件里的数字来自 `glb/model_metrics.gd` —— 全仓只有一处在量「这条动作多长」（《模型与动画准则》「实现分层与聚合」节）。
// 3. **过不过只看产物，不看退出码**（同《模型与动画准则》「窗口模式出图」节的产物自检四条）：跑完自己复核四件事，任一不满足就返回 1，
//    免得整条线静默产出一页空表。
//
// 确定性：不写时间戳、不写绝对路径、集合一律排序 —— 同输入重跑逐字全等。

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DemoFactory.Tools;

/// <summary>一条技能在体检单上的身份（全是内容，由原型侧声明；公共层只排版，不认得这些名字）。</summary>
/// <param name="AbilityId">技能名，必须能在技能表里查到（查不到当场抛，不兜空）。</param>
/// <param name="Kind">这一条属于哪一档（兵种名或类别），只影响可读性。</param>
/// <param name="Hosts">在哪个宿主上跑过（现例：H1 假宿主 / H2 单挑场 / H3 真世界）。</param>
/// <param name="GasRows">顶住的 GAS 功能面行（照「16 条清单」那张表的行名写，指针不是复述）。</param>
/// <param name="Evidence">钉它的断言编号，空格分隔；报告里指的每一个编号都要在测试源码里真找得到。</param>
/// <param name="CarrierModel">前摇的动画载体：模型名。留空 = 这条技能目前没有动画载体（念「不适用」，不算 PASS）。</param>
/// <param name="CarrierClip">载体的 clip 名。挂号了却在 facts 里读不到 ⇒ 判红（不留静默档位）。</param>
/// <param name="Note">这一格有什么不按常理的地方，由内容侧写明（公共层只排版，不解释玩法）。</param>
public sealed record SkillRow(
    string AbilityId,
    string Kind,
    string Hosts,
    string GasRows,
    string Evidence,
    string CarrierModel,
    string CarrierClip,
    string Note = "");

/// <summary>出手节奏的一行：某个模型的某条出手动作，逻辑里每隔这么多步出手一次。</summary>
public sealed record RhythmRow(
    string Label,
    string ModelName,
    string Clip,
    long IntervalSteps);

/// <summary>技能表里那一条的可读投影（字段与 <c>AbilityDef</c> / <c>EffectDef</c> 一一对应，原型侧填）。</summary>
/// <param name="Source">这一条的数字取自哪份表（内容侧写明；16 条里如果混了两份表，报告要把这点印出来，
/// 不然读者会以为整页出自同一张表）。</param>
public sealed record SkillNumbers(
    long PreSteps,
    long PostSteps,
    long CooldownSteps,
    int MaxCharges,
    double RangeMeters,
    string CostAttribute,
    double CostAmount,
    IReadOnlyList<string> GrantedTagsWhileActive,
    IReadOnlyList<string> RequirementTags,
    IReadOnlyList<string> BlockedTags,
    IReadOnlyList<EffectNumbers> Effects,
    string Source = "");

/// <summary>效果表里那一条的可读投影。时长/周期/延迟三列各自存在（F4「Duration ≠ period」的口径）。</summary>
public sealed record EffectNumbers(
    string Id,
    string Kind,
    long DurationSteps,
    long PeriodSteps,
    long DelaySteps,
    string? StackKey,
    int MaxStacks,
    double MagnitudePerStack,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<string> GrantedTags,
    IReadOnlyList<string> Cancels,
    string CurveAxis,
    int CurvePoints,
    IReadOnlyList<string> TriggeredEffects,
    IReadOnlyList<string> ImmunityTags);

/// <summary>
/// 技能体检单契约。<see cref="Numbers"/> 是一个口子而不是数据副本：
/// 报告要的每个数字都从真技能表现取，取不到就抛。
/// </summary>
public sealed record SkillContract(
    string ModelsDir,
    IReadOnlyList<SkillRow> Rows,
    IReadOnlyList<RhythmRow> Rhythm,
    Func<string, SkillNumbers> Numbers,
    string ReportPath);

/// <summary>前摇对表的五种读数（判红只有两格，见下）。</summary>
public enum WindupVerdict
{
    /// <summary>对得上：差在半个步长以内。</summary>
    Aligned,

    /// <summary>动画比前摇<b>短</b>超过半个步长 —— 逻辑还在蓄力、画面已经演完了，判红。</summary>
    ClipShorter,

    /// <summary>
    /// 动画比前摇<b>长</b>超过半个步长 —— 起手动作演到 commit 之后，<b>不判红</b>，超出量印在「差」那一列。
    /// 演多久是表现层的事（观感裁决归 AGENTS「验证流水线」的活人感阶段），真值那头不许漂的是「画面比逻辑先演完」这种。
    /// 与 <see cref="RhythmVerdict.Overruns"/> 同一档。
    /// </summary>
    ClipLonger,

    /// <summary>这条技能没有动画载体（表里没挂号），校验器对它不判，但念得出来。</summary>
    NoCarrier,

    /// <summary>挂号了， facts 里却没有这条动作 —— 载体名写错或模型没导出，判红。</summary>
    CarrierNotFound,
}

/// <summary>
/// 一次前摇对表的读数。<see cref="DeltaSeconds"/> 是<b>带符号</b>的（clip 名义时长 − 前摇秒数）：
/// 负数 = 动画比前摇短（判红那一侧），正数 = 动画比前摇长（只入账那一侧）。
/// </summary>
public readonly record struct WindupCheck(
    string AbilityId,
    long PreSteps,
    double PreSeconds,
    string CarrierModel,
    string CarrierClip,
    double ClipSeconds,
    double DeltaSeconds,
    WindupVerdict Verdict);

/// <summary>出手节奏的三种读数。</summary>
public enum RhythmVerdict
{
    /// <summary>动画在一次出手间隔内演得完。</summary>
    Covered,

    /// <summary>动画比出手间隔长 —— 下一次出手会在画面上被「一次性动作没演完不许换」那道守卫吞掉。</summary>
    Overruns,

    /// <summary>facts 里没有这条动作（模型没导出或 clip 名写错）。</summary>
    ClipNotFound,
}

/// <summary>一次出手节奏对表的读数。</summary>
public readonly record struct RhythmCheck(
    string Label,
    string ModelName,
    string Clip,
    long IntervalSteps,
    double IntervalSeconds,
    double ClipSeconds,
    double OverSeconds,
    RhythmVerdict Verdict);

public static class Skill
{
    /// <summary>一把尺：逻辑每推进一步 = 0.1 秒（口径 → AGENTS.md 「时间基准」）。</summary>
    public const double StepSeconds = 0.1;

    /// <summary>阈值：半个步长（0.05 秒）—— 介质数字，归公共层，不让各原型各浮一个（《模型与动画准则》「实现分层与聚合」节的例外档）。</summary>
    public const double HalfStepTolerance = StepSeconds / 2.0;

    private const string InspectScript = "res://src/GugaFramework/Tools/glb/inspect_model.gd";
    private const string FactsName = "clip-lengths.txt";

    // ---------------- 校验器本体（纯函数，可脱离引擎与技能表跑） ----------------

    /// <summary>
    /// 前摇对表：技能表写的步数换算成秒，与载体 clip 的名义时长比。
    /// <paramref name="clipSeconds"/> 传 null 表示 facts 里没有这条动作（挂号了却读不到）。
    ///
    /// <b>判红是单向的</b>（用户确认：动画演多久归表现层，不许漂的是画面比逻辑先演完）：
    /// clip 短于前摇超过半步 ⇒ 红（逻辑还在蓄力、画面已演完，这一格的读数在骗人）；
    /// clip 长于前摇超过半步 ⇒ <see cref="WindupVerdict.ClipLonger"/>，只把超出量印出来、不判红。
    /// 第九轮这发装的是双向绝对差（<c>Math.Abs</c>），于是「起手动作想演得比前摇久」这条合法修法被当场堵住 ——
    /// 那是实现比它的目的（防两个数各写一遍）更严，本轮改口；改动理由记在已删的验证报告。
    /// </summary>
    public static WindupCheck JudgeWindup(SkillRow row, long preSteps, double? clipSeconds)
    {
        var pre = preSteps * StepSeconds;
        if (row.CarrierClip.Length == 0 || row.CarrierModel.Length == 0)
            return new WindupCheck(row.AbilityId, preSteps, pre, "", "", 0, 0, WindupVerdict.NoCarrier);
        if (clipSeconds is null)
            return new WindupCheck(row.AbilityId, preSteps, pre, row.CarrierModel, row.CarrierClip,
                0, 0, WindupVerdict.CarrierNotFound);
        var len = clipSeconds.Value;
        var over = len - pre;
        var verdict = Math.Abs(over) <= HalfStepTolerance + 1e-9
            ? WindupVerdict.Aligned
            : over < 0 ? WindupVerdict.ClipShorter : WindupVerdict.ClipLonger;
        return new WindupCheck(row.AbilityId, preSteps, pre, row.CarrierModel, row.CarrierClip,
            len, over, verdict);
    }

    /// <summary>
    /// 出手节奏对表：动画演一次的名义时长，与逻辑里每隔多少步出手一次比。
    /// 动画更长 ⇒ 画面盖不住节奏（观感账，不参与退出码；理由见渲染那段的注释）。
    /// </summary>
    public static RhythmCheck JudgeRhythm(RhythmRow row, double? clipSeconds)
    {
        var interval = row.IntervalSteps * StepSeconds;
        if (clipSeconds is null)
            return new RhythmCheck(row.Label, row.ModelName, row.Clip, row.IntervalSteps, interval, 0, 0,
                RhythmVerdict.ClipNotFound);
        var len = clipSeconds.Value;
        var over = len - interval;
        return new RhythmCheck(row.Label, row.ModelName, row.Clip, row.IntervalSteps, interval, len,
            over > HalfStepTolerance + 1e-9 ? over : 0,
            over > HalfStepTolerance + 1e-9 ? RhythmVerdict.Overruns : RhythmVerdict.Covered);
    }

    // ---------------- 动词入口 ----------------

    public static int Run(string root, IPrototypeTools proto, string[] rest)
    {
        var godotExe = "";
        var selfCheck = false;
        foreach (var a in rest)
        {
            if (a.StartsWith("--godot=")) godotExe = a[8..];
            else if (a == "--selfcheck") selfCheck = true;
            else if (!a.StartsWith("--") && a != ".") { /* 位置参数只认项目根那个「.」 */ }
            else throw new ArgumentException($"skill：不认识的参数 {a}（可用：--godot=<exe> / --selfcheck）");
        }

        if (selfCheck) return SelfCheck();

        var c = proto.Skills() ?? throw new InvalidOperationException(
            $"{proto.DirName} 没有技能体检单契约 —— 在 src/Prototypes/{proto.DirName}/Tools/ 下实现 IPrototypeTools.Skills()。");

        var dir = proto.DirName;
        var artifacts = Artifacts.Dir(root, "Skill", dir);
        var facts = Path.Join(artifacts, FactsName);
        Directory.CreateDirectory(artifacts);
        Artifacts.Ensure(root, "Skill", dir);
        File.Delete(facts);

        var lengths = ReadClipLengths(root, dir, c.ModelsDir, facts, ResolveGodot(godotExe));

        var windups = new List<WindupCheck>();
        var rhythms = new List<RhythmCheck>();
        foreach (var row in c.Rows)
        {
            var n = c.Numbers(row.AbilityId);
            windups.Add(JudgeWindup(row, n.PreSteps, Lookup(lengths, row.CarrierModel, row.CarrierClip)));
        }
        foreach (var r in c.Rhythm)
            rhythms.Add(JudgeRhythm(r, Lookup(lengths, r.ModelName, r.Clip)));

        var report = Render(dir, c, windups, rhythms, lengths);
        var reportFile = Path.Join(root, c.ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        File.WriteAllText(reportFile, report, new UTF8Encoding(false));

        var problems = SelfVerify(reportFile, c, report);
        var bad = windups.Count(w => w.Verdict is WindupVerdict.ClipShorter or WindupVerdict.CarrierNotFound);
        var longer = windups.Count(w => w.Verdict == WindupVerdict.ClipLonger);
        Console.WriteLine($"技能体检单 → {c.ReportPath}　（读到的 clip 时长在 {Artifacts.Show("Skill", dir)}/，不提交进版本库）");
        Console.WriteLine($"{c.Rows.Count} 条技能 · 前摇对表 {bad} 红（只咬动画比前摇短的那一侧）/ "
                          + $"{longer} 条动画比前摇长 · "
                          + $"{windups.Count(w => w.Verdict == WindupVerdict.NoCarrier)} 条没有动画载体 · "
                          + $"出手节奏 {rhythms.Count(x => x.Verdict == RhythmVerdict.Overruns)} 条动画盖不住");
        foreach (var p in problems) Console.Error.WriteLine($"体检单不合格：{p}");
        return bad > 0 || problems.Count > 0 ? 1 : 0;
    }

    /// <summary>载体没挂号时不去 facts 里查（查了也是 null，会被判成「读不到」——那是两种不同的事）。</summary>
    private static double? Lookup(Dictionary<string, Dictionary<string, double>> lengths,
        string model, string clip)
    {
        if (model.Length == 0 || clip.Length == 0) return null;
        return lengths.TryGetValue(model, out var clips) && clips.TryGetValue(clip, out var v) ? v : null;
    }

    private static Dictionary<string, Dictionary<string, double>> ReadClipLengths(
        string root, string dir, string modelsDir, string factsPath, string exe)
    {
        var args = new List<string>
        {
            "--headless", "--path", root, "--quit-after", "120",
            "--script", InspectScript, "--",
            $"--models-dir=res://{modelsDir.TrimEnd('/')}/",
            $"--facts={Path.GetFullPath(factsPath)}",
        };
        Console.WriteLine($"> {Path.GetFileName(exe)} 无头读 clip 名义时长　{dir} / {modelsDir}");
        var (code, tail) = Spawn(exe, args);
        if (!File.Exists(factsPath))
            throw new InvalidOperationException(
                $"没读到 clip 时长（引擎退出码 {code}）。末 30 行输出：\n{tail}\n"
                + "无头 --script 里脚本加载失败也是退出码 0 —— 见《AI 能力手册》故障排查「退出码口径」。");
        var out2 = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var m in Facts.Read(factsPath).Models)
            out2[m.Name] = m.Lengths;
        return out2;
    }

    private static string ResolveGodot(string flag) => GodotBin.Resolve(flag,
        "（这一步只读时长，可以无头；和模型体检单那一步不同 —— 那一步要出图，不能无头。）");

    private static (int Code, string Tail) Spawn(string exe, List<string> args) =>
        GodotProcess.Run(exe, args, 300_000, "读 clip 时长");

    // ---------------- 产物自检：过不过只看产物，不看退出码 ----------------

    private static List<string> SelfVerify(string reportFile, SkillContract c, string report)
    {
        var bad = new List<string>();
        if (!File.Exists(reportFile)) { bad.Add("报告文件没落地"); return bad; }
        if (new FileInfo(reportFile).Length == 0) bad.Add("报告是空的");
        foreach (var header in new[] { "## 栏 1 · 数值台账", "## 栏 2 · 时序胶卷", "## 栏 3 · 对照矩阵" })
            if (!report.Contains(header, StringComparison.Ordinal)) bad.Add($"报告缺这一栏：{header}");
        // 每条技能在三栏里各出现一次：少一处就是有一栏静默漏了它（过不过只看产物，不看退出码）
        foreach (var row in c.Rows)
        {
            var hits = report.Split('\n').Count(l => l.Contains($"`{row.AbilityId}`", StringComparison.Ordinal));
            if (hits < 3) bad.Add($"`{row.AbilityId}` 在报告里只出现 {hits} 次（三栏各该有一次）");
        }
        if (report.Contains(Path.DirectorySeparatorChar + "Users", StringComparison.OrdinalIgnoreCase)
            || report.Contains("C:/", StringComparison.OrdinalIgnoreCase))
            bad.Add("报告里漏了绝对路径（重跑不可比）");
        return bad;
    }

    // ---------------- 三栏渲染 ----------------

    private static string Render(string dir, SkillContract c, IReadOnlyList<WindupCheck> windups,
        IReadOnlyList<RhythmCheck> rhythms, Dictionary<string, Dictionary<string, double>> lengths)
    {
        var byId = windups.ToDictionary(w => w.AbilityId, StringComparer.Ordinal);
        var sb = new StringBuilder();
        var mismatch = windups.Count(w => w.Verdict is WindupVerdict.ClipShorter or WindupVerdict.CarrierNotFound);
        var noCarrier = windups.Count(w => w.Verdict == WindupVerdict.NoCarrier);
        var longer = windups.Count(w => w.Verdict == WindupVerdict.ClipLonger);
        var overruns = rhythms.Count(x => x.Verdict == RhythmVerdict.Overruns);

        sb.Append($"# 技能体检单 — {dir}\n\n");
        sb.Append($"> 由 `dotnet run --project src/Tools -- skill {dir} .` 生成，**勿手改**。\n");
        sb.Append("> 数字不抄第二份：这一页是技能表的**可读投影**，投影与表对不上由 `dotnet test` 那条对表测试判红。\n");
        sb.Append("> 出处 `docs/research/技能系统/调研结论摘要.md`「设计骨架：四条前提 + 六个模块 + S0~S5」节"
                  + "（原来那份核心设计稿已并入这一份摘要）。\n");
        sb.Append($"> 阈值 {N(HalfStepTolerance)} 秒（半个步长）归公共层；判红是**单向**的 —— 只咬「动画比前摇短」，"
                  + "动画比前摇长只把超出量印出来（演多久归表现层，见下方校验器那一节）；"
                  + "报告不含时间戳与绝对路径，重跑逐字全等。\n\n");

        sb.Append("## 校验器\n\n");
        sb.Append($"- **前摇对表**（参与退出码）：{c.Rows.Count} 条技能 —— {mismatch} 红 · {longer} 条动画比前摇长（不判红）· "
                  + $"{noCarrier} 条**没有动画载体**（表里没挂号，校验器不判、这里念得出）。<br>"
                  + "这一栏防的是「同一个数写两遍然后漂」：真值只有技能表那一份（不许运行时问播放器），"
                  + "clip 名义时长是另一本账。**短**的那一侧判红 —— 逻辑还在蓄力、画面已经演完，读数在骗人；"
                  + "**长**的那一侧只入账 —— 起手动作演到 commit 之后是合法表现层选择，"
                  + "观感裁决归活人感阶段（AGENTS.md「验证流水线」）。（第九轮装的是双向绝对差，把合法的「演得比前摇久」也判红，"
                  + "由用户确认改成单向，见已删的验证报告。）\n");
        sb.Append($"- **出手节奏对表**（**不**参与退出码）：{rhythms.Count} 行里 {overruns} 行动画比节奏长 —— "
                  + "这一栏读的是平A，动作库是人共有的一份（改它要重导两个原型的模型），"
                  + "且「看起来像不像一回事」属活人感阶段（AGENTS.md「验证流水线」），所以只入账、不判红。\n");
        sb.Append($"- clip 名义时长来自 {lengths.Count} 个模型的 `.glb`（无头读回，与模型体检单用的同一份实现）。\n\n");

        sb.Append("## 栏 1 · 数值台账（每条一行，全按 0.1 秒一步换算）\n\n");
        var sources = c.Rows.Select(r => c.Numbers(r.AbilityId).Source)
            .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (sources.Count > 0)
            sb.Append($"数字取自这些表（报告只投影、不另存一份）：{string.Join(" · ", sources)}。\n\n");
        sb.Append("| 技能 | 兵种 | 前摇 | commit | 结算落在 | 冷却 | 射程 | 消耗 | 充能 | 效果（时长/周期/延迟/层） |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var row in c.Rows)
        {
            var n = c.Numbers(row.AbilityId);
            sb.Append($"| `{row.AbilityId}` | {row.Kind} | {Steps(n.PreSteps)} | 第 {n.PreSteps} 步 | "
                      + $"{Settles(n)} | {Cooldown(n.CooldownSteps)} | {N(n.RangeMeters)} 米 | {Cost(n)} | "
                      + $"{Charges(n)} | {Effects(n)} |\n");
        }

        sb.Append("\n## 栏 2 · 时序胶卷（按逻辑步，不按渲染帧；每格 1 步 = 0.1 秒）\n\n");
        sb.Append("| 技能 | 前摇（步｜秒） | 载体 clip | clip 名义（秒） | 差（clip − 前摇，秒） | 判 |\n");
        sb.Append("|---|---|---|---|---|---|\n");
        foreach (var w in windups)
        {
            var read = w.Verdict == WindupVerdict.NoCarrier;
            sb.Append($"| `{w.AbilityId}` | {w.PreSteps}｜{Sec(w.PreSeconds)} | {Clip(w)} | "
                      + $"{(read ? "—" : Sec(w.ClipSeconds))} | {(read ? "—" : Signed(w.DeltaSeconds))} | "
                      + $"{VerdictText(w.Verdict)} |\n");
        }
        sb.Append("\n### 每条技能的里程碑与刻度尺\n\n");
        foreach (var row in c.Rows.OrderBy(r => r.AbilityId, StringComparer.Ordinal))
        {
            var n = c.Numbers(row.AbilityId);
            sb.Append($"**`{row.AbilityId}`**\n\n```\n{Timeline(n)}\n```\n\n");
        }

        sb.Append("### 出手节奏（平A 的间隔 vs 动画长度）\n\n");
        sb.Append("| 档位 | 模型 | clip | 间隔（步｜秒） | 动画名义（秒） | 超出（秒） | 读数 |\n");
        sb.Append("|---|---|---|---|---|---|---|\n");
        foreach (var x in rhythms)
            sb.Append($"| {x.Label} | {x.ModelName} | {x.Clip} | {x.IntervalSteps}｜{Sec(x.IntervalSeconds)} | "
                      + $"{Sec(x.ClipSeconds)} | {N(x.OverSeconds)} | {RhythmVerdictText(x.Verdict)} |\n");

        sb.Append("\n## 栏 3 · 对照矩阵（这一条顶住哪一格、谁钉着它）\n\n");
        sb.Append("| 技能 | 跑在哪个宿主 | 顶住的 GAS 功能面行 | 钉它的断言 | 前摇载体 | 内容侧写明 |\n");
        sb.Append("|---|---|---|---|---|---|\n");
        foreach (var row in c.Rows)
            sb.Append($"| `{row.AbilityId}` | {row.Hosts} | {row.GasRows} | {row.Evidence} | "
                      + $"{(byId[row.AbilityId].Verdict == WindupVerdict.NoCarrier ? "未挂号" : row.CarrierClip)} "
                      + $"| {(row.Note.Length == 0 ? "—" : row.Note)} |\n");

        sb.Append("\n## 怎么读这一页\n\n");
        sb.Append("- 「没有动画载体」不是通过，也不是失败：它说的是这条技能的前摇在模型里没有对应的那段动作，"
                  + "画面要靠 cue 交代。谁给这条技能做起手动作，校验器当场开始咬人——"
                  + "**只咬一侧**：动作比前摇短（差超过 0.05 秒）就红，因为那是「逻辑还在蓄力、画面已经演完」；"
                  + "动作比前摇长不判红，超出量带正号印在「差」那一列，演多久归活人感阶段判。\n");
        sb.Append("- 「结算落在」= commit 之后再过效果的延迟步数；飞行物、位移都在这格里，"
                  + "与快照里 `p;` 那行的到期步同一个口径。\n");
        sb.Append("- 「钉它的断言」指测试方法名里的编号，报告里出现的每个编号都必须真找得到（对表测试的第二条腿）。\n");
        return sb.ToString();
    }

    private static string Timeline(SkillNumbers n)
    {
        var settle = n.Effects.Count == 0 ? n.PreSteps
            : n.PreSteps + n.Effects.Max(e => e.DelaySteps);
        var ends = new List<long> { n.PreSteps + n.CooldownSteps, settle };
        ends.AddRange(n.Effects.Where(e => e.DurationSteps > 0).Select(e => settle + e.DurationSteps));
        var width = (int)Math.Min(60, Math.Max(12L, ends.Max()));
        var sb = new StringBuilder();
        sb.Append($"按下(0) → 前摇 {n.PreSteps} 步 → commit({n.PreSteps}) → 结算({settle})");
        foreach (var e in n.Effects.Where(e => e.DurationSteps > 0))
            sb.Append($" → {e.Id} 到期({settle + e.DurationSteps})");
        if (n.CooldownSteps > 0) sb.Append($" → 冷却解除({n.PreSteps + n.CooldownSteps})");
        sb.Append('\n');
        var ruler = new char[width];
        for (var i = 0; i < width; i++) ruler[i] = '-';
        Mark(ruler, 0, width, 'a');
        Mark(ruler, n.PreSteps, width, 'c');
        Mark(ruler, settle, width, 'h');
        foreach (var e in n.Effects.Where(e => e.DurationSteps > 0))
            Mark(ruler, settle + e.DurationSteps, width, 'x');
        if (n.CooldownSteps > 0) Mark(ruler, n.PreSteps + n.CooldownSteps, width, 'r');
        sb.Append(new string(ruler));
        sb.Append($"\n  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 {width} 步封顶）");
        return sb.ToString();
    }

    private static void Mark(char[] ruler, long step, int width, char mark)
    {
        if (step < 0 || step >= width) return;
        if (ruler[step] != '-' && ruler[step] != mark) ruler[step] = '+';
        else ruler[step] = mark;
    }

    private static string Steps(long steps) => steps == 0 ? "无前摇" : $"{steps} 步｜{Sec(steps * StepSeconds)} 秒";

    /// <summary>冷却那一格：0 步要说「没有冷却」，不是「无前摇」（同一个换算件，两种措辞，别混）。</summary>
    private static string Cooldown(long steps) => steps == 0 ? "没有冷却" : $"{steps} 步｜{Sec(steps * StepSeconds)} 秒";
    private static string Cost(SkillNumbers n) => string.IsNullOrEmpty(n.CostAttribute)
        ? "无" : $"{n.CostAttribute} {N(n.CostAmount)}";
    private static string Charges(SkillNumbers n) => n.MaxCharges <= 0 ? "不设激活检查" : $"{n.MaxCharges} 层";
    private static string Clip(WindupCheck w) => w.CarrierClip.Length == 0 ? "（没有动画载体）" : $"{w.CarrierModel}·{w.CarrierClip}";

    private static string VerdictText(WindupVerdict v) => v switch
    {
        WindupVerdict.Aligned => "对得上",
        WindupVerdict.ClipShorter => "**红：动画比前摇短，逻辑还在蓄力画面已演完**",
        WindupVerdict.ClipLonger => "动画比前摇长（演到 commit 之后）· 不判红",
        WindupVerdict.NoCarrier => "不适用：没有动画载体",
        _ => "**红：挂号了却读不到那条动作**",
    };

    private static string RhythmVerdictText(RhythmVerdict v) => v switch
    {
        RhythmVerdict.Covered => "演得完",
        RhythmVerdict.Overruns => "动画盖不住（观感账）",
        _ => "读不到那条动作",
    };

    private static string Settles(SkillNumbers n)
    {
        if (n.Effects.Count == 0) return n.PostSteps > 0 ? $"第 {n.PreSteps + n.PostSteps} 步（后摇 {n.PostSteps}）" : "commit 当步";
        var parts = n.Effects.Select(e => e.DelaySteps == 0 ? "commit 当步" : $"+{e.DelaySteps} 步").Distinct();
        return string.Join(" / ", parts);
    }

    private static string Effects(SkillNumbers n)
    {
        if (n.Effects.Count == 0) return "（技能自己不带效果）";
        return string.Join("<br>", n.Effects.Select(e =>
        {
            var s = $"{e.Id} {e.Kind}";
            if (e.DurationSteps > 0) s += $" 时长 {Steps(e.DurationSteps)}";
            if (e.PeriodSteps > 0) s += $" 每 {e.PeriodSteps} 步";
            if (e.DelaySteps > 0) s += $" 延迟 {e.DelaySteps} 步";
            if (e.StackKey is not null) s += $" 叠层 {e.MaxStacks}（每层 {N(e.MagnitudePerStack)}）";
            if (e.CurvePoints >= 4) s += $" 曲线 {e.CurveAxis}";
            if (e.Modifiers.Count > 0) s += $" ｜ {string.Join(" ", e.Modifiers)}";
            if (e.GrantedTags.Count > 0) s += $" ｜ 授予 {string.Join(",", e.GrantedTags)}";
            if (e.Cancels.Count > 0) s += $" ｜ 打断 {string.Join(",", e.Cancels)}";
            if (e.TriggeredEffects.Count > 0) s += $" ｜ 触发 {string.Join(",", e.TriggeredEffects)}";
            if (e.ImmunityTags.Count > 0) s += $" ｜ 免疫 {string.Join(",", e.ImmunityTags)}";
            return s;
        }));
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>带符号的差：正数念 <c>+0.2</c>（动画比前摇长），负数念 <c>-0.2</c>（动画比前摇短）。</summary>
    private static string Signed(double v) => (v > 0 ? "+" : "") + N(v);

    /// <summary>秒这一列固定留一位小数：1 秒写成 1.0，免得「1」与「1.5」在表里看着像同一个量级。</summary>
    private static string Sec(double v) => v.ToString("0.0##", CultureInfo.InvariantCulture);

    // ---------------- 反证与自检：一条命令验三道校验，不起引擎、不读技能表 ----------------

    /// <summary>
    /// 已删的设计档做到什么算完：「故意把某个 clip 改得跟表不一样，前摇那一栏要报出来」。
    /// 这一档把那个动作做成可重跑的：喂合成数字，逐条核对读数，任一不符返回 1。
    /// 2026-09-28 起判红是单向的，所以两个方向各钉一条：改短 ⇒ 红、改长 ⇒ 不判红但超出量必须印得出来。
    /// </summary>
    public static int SelfCheck()
    {
        var rows = new[]
        {
            new SkillRow("ab.windup", "—", "—", "—", "—", "MODEL_A", "clip_a"),
            new SkillRow("ab.instant", "—", "—", "—", "—", "", ""),
            new SkillRow("ab.unlisted", "—", "—", "—", "—", "MODEL_A", "clip_missing"),
        };
        var cases = new (string Name, string Got, string Want)[]
        {
            ("前摇 3 步 vs clip 0.3 秒 → 对得上",
                JudgeWindup(rows[0], 3, 0.3).Verdict.ToString(), WindupVerdict.Aligned.ToString()),
            ("故意把 clip 改短：前摇 3 步 vs clip 0.2 秒 → 差 -0.1 秒 → 红",
                JudgeWindup(rows[0], 3, 0.2).Verdict.ToString(), WindupVerdict.ClipShorter.ToString()),
            ("故意把 clip 改长：前摇 3 步 vs clip 0.4 秒 → 动画比前摇长 → 不判红",
                JudgeWindup(rows[0], 3, 0.4).Verdict.ToString(), WindupVerdict.ClipLonger.ToString()),
            ("改长那一侧的超出量印得出来：+0.1",
                Signed(JudgeWindup(rows[0], 3, 0.4).DeltaSeconds), "+0.1"),
            ("前摇 3 步 vs clip 0.34 秒 → 差在半步内 → 仍对得上",
                JudgeWindup(rows[0], 3, 0.34).Verdict.ToString(), WindupVerdict.Aligned.ToString()),
            ("前摇 3 步 vs clip 0.26 秒 → 差在半步内 → 仍对得上",
                JudgeWindup(rows[0], 3, 0.26).Verdict.ToString(), WindupVerdict.Aligned.ToString()),
            ("没挂号 → 不适用（不是通过）",
                JudgeWindup(rows[1], 0, null).Verdict.ToString(), WindupVerdict.NoCarrier.ToString()),
            ("挂号了却读不到那条 clip → 红",
                JudgeWindup(rows[2], 2, null).Verdict.ToString(), WindupVerdict.CarrierNotFound.ToString()),
            ("出手间隔 10 步 vs 动画 1.0 秒 → 演得完",
                JudgeRhythm(new RhythmRow("档 1", "MODEL_A", "clip_a", 10), 1.0).Verdict.ToString(),
                RhythmVerdict.Covered.ToString()),
            ("出手间隔 12 步 vs 动画 1.5 秒 → 盖不住（记进观感账，不判红）",
                JudgeRhythm(new RhythmRow("档 2", "MODEL_A", "clip_a", 12), 1.5).Verdict.ToString(),
                RhythmVerdict.Overruns.ToString()),
            ("clip 读不到 → 报出来，不静默",
                JudgeRhythm(new RhythmRow("档 3", "MODEL_A", "clip_a", 11), null).Verdict.ToString(),
                RhythmVerdict.ClipNotFound.ToString()),
        };

        var bad = 0;
        Console.WriteLine($"技能体检单自检（阈值 {N(HalfStepTolerance)} 秒 = 半个步长）");
        foreach (var (name, got, want) in cases)
        {
            var ok = got == want;
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}　读数={got}{(ok ? "" : $" 期望={want}")}");
        }
        Console.WriteLine(bad == 0 ? $"共 {cases.Length} 条，全过" : $"{bad} 条不符");
        return bad == 0 ? 0 : 1;
    }
}
