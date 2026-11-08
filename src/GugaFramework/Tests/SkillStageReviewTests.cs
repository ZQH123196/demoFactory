#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using DemoFactory.Tools;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;
using DemoFactory.GugaFramework.Tests;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// <b>技能体检单接在新场子上（ticket 08）</b>：三栏报告（数值台账 / 时序胶卷 / 对照矩阵）与
/// 前摇对表校验都跑在场子这一侧，<b>报告里「跑在哪个宿主」那一列换成场子名</b>，
/// 钉它的断言编号照样要能在测试源码里真找得到。
///
/// <b>共用一份实现</b>：阈值与前摇对表的判红口径住在 <c>src/Tools/Skill.cs</c>
/// （按文件链进本测试项目），本文件只递契约（<see cref="SkillStageReviewContract"/>）与断言
/// ——<b>不重写一遍校验器</b>（「桥只转发」 同一句理由：判红的那份实现不许在场子这一侧再长一份，
/// 否则两套各自漂，而且没人当场发现得了）。
///
/// <b>报告是技能表的投影，不是第二份表</b>：<see cref="S33_三栏跑在新场子上且宿主那一列是场子名"/>
/// 读的是<b>已提交那份</b>，逐格与真表比——表改了报告没重跑就红（与某一版审查单那份同一手法）。
/// 重跑的那一发是 <see cref="S37_重跑这份报告_显式执行"/>（默认不跑，见那条的头注）。
///
/// <b>本文件与契约那份不写任何技能名 / 属性名 / tag 字面量</b>：名字一律从三份注册壳的常量取
/// （与 S8 同一口径）。
/// </summary>
public sealed class SkillStageReviewTests
{
    /// <summary>仓库根（测试目录 <c>bin/Debug/net8.0</c> 往上 6 级：net8.0→Debug→bin→Tests→GugaFramework→src→根）。</summary>
    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string ReportFile => Path.Combine(RepoRoot, SkillStageReviewContract.ReportRelative);

    /// <summary>本测试项目自己的源码目录（断言编号的检索面）。</summary>
    private static string TestsDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", ".."));

    /// <summary>已提交那份报告。读不到就是空跑，先钉住这个（S33 每一条都靠它）。</summary>
    private static string ReadCommittedReport()
    {
        File.Exists(ReportFile).ShouldBeTrue($"读不到已提交那份报告：{ReportFile}\n"
            + "（路径拼错或还没生成过 —— 生成走 S37_重跑这份报告，那一发默认不跑）");
        var text = File.ReadAllText(ReportFile);
        text.Length.ShouldBeGreaterThan(0, "报告是空的");
        return text;
    }

    /// <summary>
    /// <b>S32 前摇对表：场子没有模型，所以每一条都读「不适用」；判红口径照旧</b>。
    ///
    /// <b>这一格比的是哪两份数据</b>：① 能力表里那条技能声明的前摇——<c>AbilityDef.PreSteps</c> 步 × 0.1 秒；
    /// ② 载体 clip 的名义时长——从一个 <c>.glb</c> 里读回的那条动画多长（走
    /// <c>src/GugaFramework/Tools/glb/inspect_model.gd</c> 写出的 facts，全仓只有那一处在量它）。
    /// 两份数据分属两本账，这一格防的是「同一个数写两遍然后漂」。
    ///
    /// <b>当前只能验「没挂号」那一档</b>：场子按 spec「先不带单位模型」
    /// （<c>src/Shared/</c> 今天只有一个 <c>.gitkeep</c>，全仓四个 <c>Models/</c> 都在各自原型下），
    /// ②那一头<b>没有件可读</b>。本壳据此把载体两格留空，校验器念「不适用：没有动画载体」
    /// ——<b>如实记「没验」，不是把这一格做绿</b>（造一份假 clip 时长塞进去才算做绿，本 ticket 明确不许）。
    ///
    /// <b>缺什么才能真验判红那一侧</b>：① 场子侧有件（原型递自己的模型，或共享资产目录放中性模型）；
    /// ② 那件带一条起手动作的 clip；③ 载体表挂号（哪条技能演哪条 clip）。三样齐了才咬得动。
    /// 属<b>接模型那一 ticket</b>的范围，本 ticket 是纯逻辑 ticket（不起 Godot、不出图、不碰 MCP）。
    /// </summary>
    [Fact]
    public void S32_前摇对表_场子没有模型所以每条都是不适用_判红口径照旧()
    {
        var contract = SkillStageReviewContract.Build();

        // 逐条问一次那一份共用校验器（与原型侧判的是同一份实现）。
        foreach (var row in contract.Rows)
        {
            var n = contract.Numbers(row.AbilityId);
            var read = Skill.JudgeWindup(row, n.PreSteps, clipSeconds: null);

            read.Verdict.ShouldBe(WindupVerdict.NoCarrier, $"{row.AbilityId}：场子侧载体两格留空，校验器该念「不适用：没有动画载体」而不是别的读数");
            (row.CarrierModel + row.CarrierClip).ShouldBeEmpty($"{row.AbilityId}：载体两格留空才读得出「不适用」——填上名字却读不到时长，"
                + "校验器会判红（CarrierNotFound），那是「挂号了却读不到」，与「没挂号」是两件事");
            read.DeltaSeconds.ShouldBe(0, $"{row.AbilityId}：没有载体时差值不该有数（免得读起来像验过了）");
        }

        // 判红口径本身没被这一 ticket 改坏。这一段喂的是**合成读数**（不是本场任何一条技能的数据）——
        // 它判的是共用那份校验器的形状：短的那一侧红、长的那一侧只印超出量。
        var probe = new SkillRow("probe", "—", "—", "—", "—", "MODEL", "clip");
        Skill.JudgeWindup(probe, 3, 0.2).Verdict.ShouldBe(WindupVerdict.ClipShorter, "动画比前摇短超过半步 ⇒ 判红（这一 ticket 没改这一侧）");
        Skill.JudgeWindup(probe, 3, 0.4).Verdict.ShouldBe(WindupVerdict.ClipLonger, "动画比前摇长 ⇒ 不判红（只入账那一侧）");
        Skill.JudgeWindup(probe, 3, 0.4).DeltaSeconds.ShouldBe(0.1, 1e-9, "「长」那一侧把超出量印出来（+0.1 秒），不是静默吞掉");
        Skill.JudgeWindup(probe, 3, 0.34).Verdict.ShouldBe(WindupVerdict.Aligned, "差在半步以内 ⇒ 对得上");
        Skill.HalfStepTolerance.ShouldBe(Skill.StepSeconds / 2, 1e-12, "阈值仍是半个步长 = 0.05 秒（口径 → 「时间基准」，1 步 = 0.1 秒）");
    }

    /// <summary>
    /// <b>S33 三栏跑在新场子上，且宿主那一列是场子名</b>（读<b>已提交那份</b>报告）：
    /// ① 三栏都在，每条技能三栏里各出现一次（少一处 = 有一栏静默漏了它）；
    /// ② 栏 1 的前摇 / 冷却 / 射程 / 效果条数<b>逐格等于真表</b>——表改了报告没重跑，这里就红；
    /// ③ 栏 3 的「跑在哪个宿主」<b>每一格都是场子名</b>，没有一条留着原型侧那套 H1/H2/H3 叫法；
    /// ④ 栏 2 每一行都念「不适用：没有动画载体」（与 S32 同源，报告不许把它写成「对得上」）。
    /// </summary>
    [Fact]
    public void S33_三栏跑在新场子上且宿主那一列是场子名()
    {
        var report = ReadCommittedReport();
        var contract = SkillStageReviewContract.Build();

        foreach (var header in new[] { "## 栏 1 · 数值台账", "## 栏 2 · 时序胶卷", "## 栏 3 · 对照矩阵" })
            report.ShouldContain(header, Case.Sensitive, $"报告缺这一栏：{header}");

        foreach (var row in contract.Rows)
        {
            var hits = report.Split('\n').Count(l => l.Contains($"`{row.AbilityId}`", StringComparison.Ordinal));
            hits.ShouldBeGreaterThanOrEqualTo(3, $"`{row.AbilityId}` 在报告里只出现 {hits} 次（三栏各该有一次）");
        }

        // 栏 1 逐格对表
        var rows1 = ParseColumn(report, "## 栏 1", 8);
        rows1.Keys.ShouldBe(contract.Rows.Select(r => r.AbilityId), true, "栏 1 的技能集合与登记表不是一批 —— 谁多登记了、谁漏了");
        foreach (var row in contract.Rows)
        {
            var cells = rows1[row.AbilityId];
            var n = SkillStageReviewContract.Numbers(row.AbilityId);
            StepsOf(cells[2]).ShouldBe(n.PreSteps, $"{row.AbilityId}：报告的前摇与表里的 PreSteps 对不上（报告「{cells[2]}」）");
            StepsOf(cells[4]).ShouldBe(n.CooldownSteps, $"{row.AbilityId}：报告的冷却与表里的 CooldownSteps 对不上（报告「{cells[4]}」）");
            ((double)((double)MetersOf(cells[5]))).ShouldBe(n.RangeMeters, 0.001, $"{row.AbilityId}：报告的射程与表里的 RangeMeters 对不上（报告「{cells[5]}」）");
            CountOf(cells[7]).ShouldBe(n.Effects.Count, $"{row.AbilityId}：报告里这一条摆了几份效果（期望 {n.Effects.Count} 份，表里 EffectIds 就是这本账）");
        }

        // 栏 3 宿主那一列
        var rows3 = ParseColumn(report, "## 栏 3", 6);
        foreach (var row in contract.Rows)
        {
            var host = rows3[row.AbilityId][1];
            host.ShouldStartWith("技能试验场·", Case.Sensitive, $"{row.AbilityId}：宿主那一格该是场子名（现读「{host}」）");
            foreach (var old in new[] { "H1", "H2", "H3" })
                host.ShouldNotContain(old, Case.Sensitive, $"{row.AbilityId}：宿主那一格还留着原型侧那套叫法「{old}」（{host}）");
            host.ShouldBe(row.Hosts, $"{row.AbilityId}：宿主那一格与登记表对不上");
            rows3[row.AbilityId][3].ShouldBe(row.Evidence, $"{row.AbilityId}：「钉它的断言」那一格与登记表对不上");
        }

        // 栏 2 每行都是「不适用」
        var rows2 = ParseColumn(report, "## 栏 2", 6);
        foreach (var row in contract.Rows)
        {
            var verdict = rows2[row.AbilityId][5];
            verdict.ShouldBe("不适用：没有动画载体", $"{row.AbilityId}：栏 2 该念「不适用：没有动画载体」（实读「{verdict}」）——"
                + "场子没有模型就没有 clip 时长，这一格是「没验」，写成「对得上」才是做绿");
        }
        Regex.Matches(report, "不适用：没有动画载体").Count.ShouldBe(contract.Rows.Count, "「不适用」该恰好念出登记表的行数 —— 多出来说明有一格被别处判了红或判了对得上");

        // 「数取自哪张表」这一句逐条都要念对自己那张：两份表各自现建，判据只能是「那张表里查得到它」。
        // （第一版用引用相等去判，报告把示范注册壳那条也念成清单注册壳那张表——S33 顺手钉住这一格。）
        foreach (var row in contract.Rows)
        {
            var fromSample = InSampleTables(row.AbilityId);
            var expected = fromSample ? "示范注册壳那张表" : "清单注册壳那张表";
            SkillStageReviewContract.Numbers(row.AbilityId).Source.ShouldStartWith(expected, Case.Sensitive, $"{row.AbilityId}：报告里「数取自哪张表」念错了（应念「{expected}」）");
        }
        contract.Rows.Select(r => SkillStageReviewContract.Numbers(r.AbilityId).Source)
                .Distinct(StringComparer.Ordinal).Count().ShouldBe(2, "这一页该恰好引两份表（清单注册壳 / 示范注册壳）——只有一份说明上面那个判据退化了");
    }

    /// <summary>
    /// <b>S34 报告与技能表对不上时有断言判红</b>——本 ticket 的<b>反证</b>：
    /// <b>拿已提交那份报告的格子</b>去对真表判一次绿，再<b>把真表改掉一格</b>去对同一批格子判一次红。
    ///
    /// 为什么非得是同一批格子：只把自己渲染出来的东西再读回去比，那是<b>自证</b>——
    /// 改了什么就一定对得上什么，那条断言看着存在、其实没有校验力（这一条第一版就栽在这里，
    /// 是它自己的断言消息先报的）。钉住它的必须是<b>已提交那份产物</b>与<b>真表</b>之间那一格。
    /// </summary>
    [Fact]
    public void S34_报告与技能表对不上时判红_原表判绿()
    {
        var contract = SkillStageReviewContract.Build();
        var rows1 = ParseColumn(ReadCommittedReport(), "## 栏 1", 8);

        // 绿的那一支：已提交那份报告的每一格，对真表逐格对上（S33 判的是同一件事的完整版，
        // 这里只取「射程那一格」，留一个最小的对照支给下面那半）。
        var id = contract.Rows[0].AbilityId;
        var real = SkillStageReviewContract.Numbers(id);
        var reported = MetersOf(rows1[id][5]);
        ((double)((double)reported)).ShouldBe(real.RangeMeters, 0.001, $"{id}：已提交那份报告的射程与真表对不上（报告 {reported}，表 {real.RangeMeters}）");

        // 红的那一支：把真表的射程改掉一格，**同一批报告格子**立刻对不上
        var drifted = real with { RangeMeters = real.RangeMeters + 1 };
        // Is.Not.EqualTo(x).Within(tol) 的对位：Shouldly 没有「带容差的 ShouldNotBe」，
        // 落成「容差内相等」不成立——语义与 NUnit 那句一致（差得超过容差才算数）。
        // 注意不能写成 reported.ShouldBe(x, tol, msg).ShouldBeFalse()：ShouldBe 返回 void，接不上链。
        (Math.Abs(reported - drifted.RangeMeters) > 0.001).ShouldBeTrue(
            $"{id}：把真表的射程改掉一格之后，已提交那份报告仍然对得上 —— "
            + "这一格没有校验力（要么对表根本没比这一格，要么改表的信号到不了这里）");
    }

    /// <summary>
    /// <b>S35 报告里「钉它的断言」每一个编号都要真能在测试源码里搜到</b>
    /// ——写一个编造的编号等于这张表没有校验力（与某一版审查单那份同一手法）。
    /// 检索面是本测试项目的源码（<c>src/GugaFramework/Tests/</c>，含清单本体那份）。
    /// </summary>
    [Fact]
    public void S35_报告里的断言编号在测试源码里真找得到()
    {
        var contract = SkillStageReviewContract.Build();
        var sources = ReadTestSources();
        var joined = string.Concat(sources.Values);

        var cited = contract.Rows
            .SelectMany(r => r.Evidence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        cited.Count.ShouldBeGreaterThan(10, $"登记表的断言编号该有十几个，读这么少说明登记表是空的：{cited.Count}");

        foreach (var id in cited)
            joined.ShouldContain($"public void {id}_", Case.Sensitive, $"报告指着 {id}，测试源码里却没有这个测试方法 —— 那一格的证据是空的");

        // 反向：本 ticket 这两份不许自己长出那些断言（钉它的断言只准在清单本体或场子用例那几份里，
        // 自己再写一个就等于那一格有两份断言，与 S22/S23 同一口径）。
        foreach (var name in new[] { "SkillStageReviewContract.cs", "SkillStageReviewTests.cs" })
        {
            var self = sources[name];
            foreach (var id in cited.Where(i => char.IsDigit(i[^1]) && !i.StartsWith("S3", StringComparison.Ordinal)))
                self.ShouldNotContain($"public void {id}_", Case.Sensitive, $"{name} 里定义了 {id} —— 那一格有两份断言了（与 S22/S23 同一口径）");
        }
    }

    /// <summary>
    /// <b>S36 场子与契约这两份里搜不到原型的词表</b>（「原型隔离」 + S8 同一族）：
    /// 体检单是公共层那一份，场子这一侧只递契约、不认任何原型名。
    /// <b>只扫字符串字面量，不扫注释</b>——注释里提一句「同某一版审查单那一手法」是在讲出处，
    /// 不等于「场子认识那个原型」（S8 的 <c>StringLiterals</c> 同一口径）。
    /// 顺带钉住<b>本 ticket 不往公共层塞任何东西</b>：场子本体那几份一个字节都没动
    /// ——它们一行都不该提到体检单的行类型（与 S10 的文件清单互为反证）。
    /// </summary>
    [Fact]
    public void S36_场子与契约不认任何原型()
    {
        var sources = ReadTestSources();

        // 禁词表在运行时拼出来（"Proto" + "types" 那种拆法）：直接写字面量的话，
        // 本文件自己那份「找禁词」的代码就成了唯一命中——检查自己检查自己，等于没检查。
        var banned = new[] { "Proto" + "types", "Battle" + "Array", "Beast" + "Arena", "Infinity" + "Lord" };
        foreach (var name in new[] { "SkillStageReviewContract.cs", "SkillStageReviewTests.cs" })
            foreach (var literal in StringLiterals(sources[name]))
                foreach (var word in banned)
                    literal.ShouldNotContain(word, Case.Sensitive, $"{name} 里出现了字符串字面量 {literal}（含「{word}」）—— "
                        + "场子随项目走但不隶属任何原型，别的原型的词表也不许进公共层（「原型隔离」）");

        var stageDir = Path.Combine(RepoRoot, "src", "GugaFramework", "Abilities", "Stage");
        foreach (var file in Directory.GetFiles(stageDir, "*.cs", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).ShouldNotContain("SkillRow", Case.Sensitive, $"{Path.GetFileName(file)} 里出现了体检单的行类型 —— 契约由场子侧递，场子本体不该引它（「原型隔离」）");
        }
    }

    /// <summary>
    /// 抽出源文件里的字符串字面量（<b>排除注释</b>：注释里提一句出处不算「代码认识它」）。
    /// 口径与 S8 的同名件逐字同族，连处理 <c>//</c> 与 <c>/* */</c> 的方式都一样。
    /// </summary>
    private static IReadOnlyList<string> StringLiterals(string source)
    {
        var found = new List<string>();
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                i = Math.Min(source.Length, i + 2);
                continue;
            }

            if (source[i] == '"')
            {
                var j = i + 1;
                while (j < source.Length && source[j] != '"')
                {
                    if (source[j] == '\\') j++;
                    j++;
                }

                if (j >= source.Length) break;
                found.Add(source[(i + 1)..j]);
                i = j + 1;
                continue;
            }

            i++;
        }

        return found;
    }

    /// <summary>
    /// <b>重跑这份报告</b>——<b>默认不跑</b>（<c>Explicit</c>）：它是「改完表重新生成产物」那一发，
    /// 不是断言。日常 <c>dotnet test</c> 只判已提交那份与表对不对得上（S33）。
    ///
    /// 跑法：<c>dotnet test src/GugaFramework/Tests --filter "S37"</c>
    /// 幂等：同输入重跑逐字全等（不写时间戳与绝对路径），所以重跑它不该产生 diff。
    /// </summary>
    /// <para>迁移前是 NUnit 的 <c>[Explicit]</c>（默认不跑）。xUnit 没有「按名字点名才跑」这档语义，
    /// 等价且保守的走法是 <c>[Fact(Skip = …)]</c>：日常验证一律不执行这条
    /// （它会重写已提交那份报告，是维护动作不是断言）。</para>
    [Fact(Skip = "改完技能表后重新生成已提交那份报告；日常验证不跑它")]
    public void S37_重跑这份报告_显式执行()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportFile)!);
        File.WriteAllText(ReportFile, Render(), new UTF8Encoding(false));
        TestLog.Ledger($"已重写 {SkillStageReviewContract.ReportRelative}");
        File.ReadAllText(ReportFile).ShouldBe(Render(), "重跑要幂等（同输入逐字全等）");
    }

    // === 生成：排版在��一侧，阈值与判红走共用那份 ===

    /// <summary>
    /// 报告的排版。共用层那份渲染是私有的，<b>公共层不许为体检单单开一份</b>，
    /// 所以场子这一侧自己排版——但<b>阈值与前摇对表那一份校验器走的是共用的</b>（<see cref="Skill"/>）。
    /// 不写时间戳与绝对路径 ⇒ 同输入重跑逐字全等。
    /// </summary>
    private static string Render()
    {
        var contract = SkillStageReviewContract.Build();
        var windups = contract.Rows
            .Select(r => Skill.JudgeWindup(r, contract.Numbers(r.AbilityId).PreSteps, clipSeconds: null))
            .ToList();

        var sb = new StringBuilder();
        sb.Append("# 技能体检单 — 技能试验场\n\n");
        sb.Append("> 由 `dotnet test src/GugaFramework/Tests --filter \"S37\"` 生成，**勿手改**。\n");
        sb.Append("> 数字不抄第二份：栏 1 与栏 2 全部从三份注册壳的真技能表投影，"
                  + "投影与表对不上由 S33 判红。\n");
        sb.Append($"> 阈值 {N(Skill.HalfStepTolerance)} 秒（半个步长，1 步 = 0.1 秒，「时间基准」）归公共层；"
                  + "判红是**单向**的 —— 只咬「动画比前摇短」，动画比前摇长只把超出量印出来。\n\n");

        sb.Append("## 校验器\n\n");
        sb.Append($"- **前摇对表**：{contract.Rows.Count} 条技能 —— 0 红 · 0 条动画比前摇长 · "
                  + $"**{windups.Count} 条没有动画载体**。\n");
        sb.Append("  这一格比的是**两份数据**：① 能力表里那条技能声明的前摇步数（× 0.1 秒）；"
                  + "② 载体 clip 的名义时长（从一个 `.glb` 无头读回，全仓只有一处量它）。\n");
        sb.Append("  **当前这一格只验得到「没挂号」那一档**：场子按 spec 不带单位模型，②那一头没有件可读，"
                  + "所以每一条都念「不适用」。缺件、缺带起手动作的 clip、缺载体表这三样齐了才咬得动判红那一侧。\n");
        sb.Append("  「不适用」不是通过：它说的是这一条的前摇在模型里没有对应的那段动作，验不了。\n\n");

        sb.Append("## 栏 1 · 数值台账（每条一行，全按 0.1 秒一步换算）\n\n");
        var sources = contract.Rows
            .Select(r => contract.Numbers(r.AbilityId).Source)
            .Distinct(StringComparer.Ordinal).ToList();
        sb.Append($"数字取自这些表（报告只投影、不另存一份）：{string.Join(" · ", sources)}。\n\n");
        sb.Append("| 技能 | 哪一份注册壳 | 前摇 | commit | 冷却 | 射程 | 消耗 | 效果条数 |\n");
        sb.Append("|---|---|---|---|---|---|---|---|\n");
        foreach (var row in contract.Rows)
        {
            var n = contract.Numbers(row.AbilityId);
            sb.Append($"| `{row.AbilityId}` | {row.Kind} | {Steps(n.PreSteps)} | 第 {n.PreSteps} 步 | "
                      + $"{Cooldown(n.CooldownSteps)} | {N(n.RangeMeters)} 米 | {Cost(n)} | {n.Effects.Count} |\n");
        }

        sb.Append("\n## 栏 2 · 时序胶卷（按逻辑步，不按渲染帧；每格 1 步 = 0.1 秒）\n\n");
        sb.Append("| 技能 | 前摇（步｜秒） | 载体 clip | clip 名义（秒） | 差（秒） | 判 |\n");
        sb.Append("|---|---|---|---|---|---|\n");
        foreach (var w in windups)
            sb.Append($"| `{w.AbilityId}` | {w.PreSteps}｜{Sec(w.PreSeconds)} | （没有动画载体） | — | — | "
                      + "不适用：没有动画载体 |\n");

        sb.Append("\n## 栏 3 · 对照矩阵（这一条顶住哪一格、在哪个场子上验过、谁钉着它）\n\n");
        sb.Append("| 技能 | 跑在哪个宿主 | 顶住的功能面 | 钉它的断言 | 前摇载体 | 内容侧写明 |\n");
        sb.Append("|---|---|---|---|---|---|\n");
        foreach (var row in contract.Rows)
            sb.Append($"| `{row.AbilityId}` | {row.Hosts} | {row.GasRows} | {row.Evidence} | 未挂号 "
                      + $"| {(row.Note.Length == 0 ? "—" : row.Note)} |\n");

        sb.Append("\n## 怎么读这一页\n\n");
        sb.Append("- 「钉它的断言」指测试方法名里的编号，报告里出现的每个编号都必须真找得到（S35 逐个检索）。\n");
        sb.Append("- 「跑在哪个宿主」是**场子名**，不是原型侧那套 H1/H2/H3 —— 换原型换技能时它不变。\n");
        sb.Append("- 「未挂号」不是缺陷，是本 ticket 的边界：场子不带模型，前摇对表那一格因此只验得到「没挂号」。\n");
        return sb.ToString();
    }

    // === 解析与取参 ===

    private static Dictionary<string, string> ReadTestSources()
    {
        var files = Directory.GetFiles(TestsDir, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);
        files.Count.ShouldBeGreaterThan(3, $"没读到本测试项目的源码（{TestsDir}）");
        return files;
    }

    /// <summary>取某一栏那张表：返回 技能名 → 那一行的各格（按 <c>|</c> 切，去掉首尾两格）。</summary>
    private static Dictionary<string, string[]> ParseColumn(string report, string header, int expectColumns)
    {
        var outRows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var inside = false;
        foreach (var line in report.Split('\n'))
        {
            if (line.StartsWith(header, StringComparison.Ordinal)) { inside = true; continue; }
            if (inside && line.StartsWith("## ", StringComparison.Ordinal)) break;
            if (!inside || !line.StartsWith("| `", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            cells.Length.ShouldBe(expectColumns, $"{header} 那一行的列数变了（读到 {cells.Length} 列，期望 {expectColumns}）——报告形状漂了");
            var id = cells[0].Trim('`');
            if (!outRows.TryAdd(id, cells))
                throw new InvalidOperationException($"{header} 里 {id} 出现了两次");
        }
        outRows.ShouldNotBeEmpty($"{header} 一行都没读到（报告形状漂了，不是这一条该松）");
        return outRows;
    }

    private static string Steps(long steps) => steps == 0 ? "无前摇" : $"{steps} 步｜{Sec(steps * Skill.StepSeconds)} 秒";

    private static string Cooldown(long steps)
        => steps == 0 ? "没有冷却" : $"{steps} 步｜{Sec(steps * Skill.StepSeconds)} 秒";

    private static string Cost(SkillNumbers n)
        => string.IsNullOrEmpty(n.CostAttribute) ? "无" : $"{n.CostAttribute} {N(n.CostAmount)}";

    /// <summary>「3 步｜0.3 秒」取 3；「无前摇」「没有冷却」取 0。</summary>
    private static long StepsOf(string cell)
        => cell.StartsWith("无", StringComparison.Ordinal) || cell.StartsWith("没有", StringComparison.Ordinal)
            ? 0
            : long.Parse(cell.Split(' ')[0], CultureInfo.InvariantCulture);

    /// <summary>「4 米」取 4。</summary>
    private static double MetersOf(string cell)
        => double.Parse(cell.Split(' ')[0], CultureInfo.InvariantCulture);

    private static int CountOf(string cell)
        => int.Parse(cell.Trim(), CultureInfo.InvariantCulture);

    /// <summary>这条技能在不在示范注册壳那张表里（表自己查，查不到它抛——与契约那份同一手法）。</summary>
    private static bool InSampleTables(string abilityId)
    {
        try
        {
            SkillStageSample.Tables().Ability(abilityId);
            return true;
        }
        catch (KeyError)
        {
            return false;
        }
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Meters(double v) => N(v);

    private static string Sec(double v) => v.ToString("0.0##", CultureInfo.InvariantCulture);
}
