#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 技能体检单（P5，2026-09-28 第九轮）要钉的是 —— <b>报告是技能表的投影，不是第二份表</b>，
/// 而那份工具（<c>src/Tools/Skill.cs</c>）不认得任何一条技能。
///
/// 为什么这一条要长在测试里而不是长在工具里：工具自己没法知道「表改过、报告没跟着改」——
/// 它每次运行都重新投影，所以报告永远自洽。漂的形态是<b>报告提交之后表又动了</b>，
/// 那只有拿真表现比回来才看得见。与 A54（清单本体不认得原型）、A70（壳里不许长出第二份断言）同族。
///
/// 校验器本体那三道（差超过半步就红、没挂号念得出、挂号了读不到也念得出）不在这里判：
/// <c>Skill.cs</c> 住在开发期工具程序集里（不进游戏、不进测试项目，同模型体检单的边界），
/// 判它走一条命令 <c>dotnet run --project src/Tools -- skill BattleArray --selfcheck</c> —— 8 条合成读数逐条核对，
/// 里面就有已删的设计档要的那一发「故意把 clip 改长 ⇒ 前摇那一栏红」。
/// </summary>
public class AbilitySkillReviewTests
{
    private const string ReportRelative = "../../../../docs/prototypes/battle-array/skill-review.md";

    /// <summary>16 条技能：H2 那份注册壳里的 14 条 + 踏地与冲锋（台账按用户确认的口径落 H3 那侧）。</summary>
    private static readonly string[] All16 =
    {
        SkillDuelRift.Skill.Guard, SkillDuelRift.Skill.Dread, BattleAbilities.SlamAbilityId,
        SkillDuelRift.Skill.Steady, SkillDuelRift.Skill.Rift, SkillDuelRift.Skill.Execute,
        SkillDuelRift.Skill.Bleed, SkillDuelRift.Skill.Rage, SkillDuelRift.Skill.Pierce,
        SkillDuelRift.Skill.Rain, SkillDuelRift.Skill.Mark, SkillDuelRift.Skill.Evasive,
        BattleAbilities.ChargeAbilityId, SkillDuelRift.Skill.Trample, SkillDuelRift.Skill.Joust,
        SkillDuelRift.Skill.Mounted,
    };

    /// <summary>报告与两张真表的分工一致：这两条从 H3 那份表投影，其余 14 条从 H2 那份。</summary>
    private static AbilityTables TablesFor(string abilityId)
        => abilityId is BattleAbilities.SlamAbilityId or BattleAbilities.ChargeAbilityId
            ? BattleAbilities.TablesForReview()
            : SkillDuelRift.Tables();

    // === A76：报告是投影，不是第二份表 ===

    /// <summary>
    /// <b>A76（已提交那份报告与技能表逐格对得上）</b>四条腿：
    /// ① 16 条一行不少、一行不多（栏 1 的行数与 Id 集合都比对上）；
    /// ② 每条的<b>前摇 / 冷却 / 射程</b>三格等于表里的数字 —— 报告说 3 步而表里住 2 步就是这条判红；
    /// ③ 每条的<b>效果条数</b>等于表里的 <c>EffectIds</c> 条数（踏地自己不带效果，那一格念得出原因）；
    /// ④ 本版边界写明：16 条里<b>没挂号的那 12 条念「不适用：没有动画载体」</b>，格数与挂号表条数对得上。
    ///    这发不是为了好看 —— 载体表与已提交的报告不同步时它红；
    /// ⑤ 挂号那份表（<c>SkillWindups</c>）自己站得住：四条 id 都在真表里、真表里都写着前摇 &gt; 0、
    ///    报告念得出各自那条 clip 名，且践踏那份字面量与 H2 表的常量逐字相等（2026-09-28 随四条起手动作加）。
    /// </summary>
    [Fact]
    public void A76_报告是技能表的投影_前摇冷却射程逐格对得上()
    {
        var report = ReadFile(ReportRelative);
        var rows = ParseColumn1(report);

        rows.Keys.ShouldBe(All16, true, "栏 1 的技能集合与清单页那 16 条不是一批 —— 谁多登记了、谁漏了");

        foreach (var id in All16)
        {
            var cells = rows[id];
            var a = TablesFor(id).Ability(id);
            StepsOf(cells[2]).ShouldBe(a.PreSteps, $"{id}：报告的前摇与表里的 PreSteps 对不上（报告「{cells[2]}」）");
            StepsOf(cells[5]).ShouldBe(a.CooldownTicks, $"{id}：报告的冷却与表里的 CooldownTicks 对不上（报告「{cells[5]}」）");
            ((double)MetersOf(cells[6])).ShouldBe(a.RangeMeters, 0.001, $"{id}：报告的射程与表里的 RangeMeters 对不上（报告「{cells[6]}」）");
            MultiCount(cells[9]).ShouldBe(a.EffectIds.Count, $"{id}：报告里这一条摆了几份效果（期望 {a.EffectIds.Count} 份，表里 EffectIds 就是这本账）");
        }

        var noCarrier = Regex.Matches(report, "不适用：没有动画载体").Count;
        noCarrier.ShouldBe(All16.Length - SkillWindups.Count, $"挂号表（SkillWindups）说现在有 {SkillWindups.Count} 条技能带起手动作，"
            + $"所以栏 2 该有 {All16.Length - SkillWindups.Count} 格念「不适用：没有动画载体」—— 实读 {noCarrier} 格。"
            + "这条红了就是「载体表」与「已提交那份报告」不同步：重跑 skill 再提交，别改数字凑它");

        // 腿 ⑤（2026-09-28 加，随四条起手动作挂号）：那份挂号表自己得站得住 ——
        // id 在真表里查得到、真表里确实写着前摇、报告念得出它挂的那条 clip。缺一都算漂。
        foreach (var id in new[] { BattleAbilities.RiftAbilityId, BattleAbilities.SlamAbilityId,
                    BattleAbilities.PierceAbilityId, SkillWindups.TrampleAbilityId })
        {
            var clip = SkillWindups.ClipFor(id);
            clip.ShouldNotBeNull($"{id}：名单里列了它，挂号表里却查不到 —— 两份表不同步了");
            TablesFor(id).Ability(id).PreSteps.ShouldBeGreaterThan(0L, $"{id}：挂了起手动作，真表里的前摇却写着 0 步 —— 按下即提交的动作没有起手段可演");
            report.Contains($"·{clip}", StringComparison.Ordinal).ShouldBeTrue($"{id}：报告栏 2 没念出它挂的那条 clip「{clip}」（挂号表改了、报告没重跑）");
        }

        // 践踏那条 id 在逻辑侧没有常量可取（H3 那张表不挂它），这里把它钉回 H2 那份表的常量
        SkillWindups.TrampleAbilityId.ShouldBe(SkillDuelRift.Skill.Trample, "SkillWindups 里那份字面量与 H2 表的 Trample 常量不是一批字 —— 改名那天这条先红");

        // 四条起手 clip 时长都比前摇长（判红那一侧不咬它），所以栏 2 该念四格「不判红」而不是四格「对得上」
        var longer = Regex.Matches(report, "动画比前摇长（演到 commit 之后）· 不判红").Count;
        longer.ShouldBe(SkillWindups.Count, $"挂号了 {SkillWindups.Count} 条起手动作，报告里该有同样数目的「动画比前摇长 · 不判红」那一格 —— 实读 {longer}");
    }

    // === A77：报告指的断言真存在，而那份工具不认得内容 ===

    /// <summary>
    /// <b>A77（对照矩阵不许指空、工具不许长出内容）</b>三条腿：
    /// ① 报告里出现的每一个 <c>A\d+</c> 都真在 <c>Tests/</c> 的某份源码里作为测试方法名存在 ——
    ///    「钉它的断言」那一格要是漂了（断言删了、改名了），这发当场抓；
    /// ② 工具那两份源码（<c>Skill.cs</c> / <c>Facts.cs</c>）里搜不到任何技能名 / tag 名 / 效果名 / clip 名 ——
    ///    接第二个带技能的原型时不该改它们（对照矩阵 A72）；
    /// ③ 两份文件都真读到了（防路径拼错成空跑，A54 / A72 同一手法）。
    /// </summary>
    [Fact]
    public void A77_报告指的断言真找得到_工具里搜不到技能名()
    {
        var report = ReadFile(ReportRelative);
        var testDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var sources = Directory.GetFiles(testDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetFullPath(f))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f));
        sources.Count.ShouldBeGreaterThan(10, $"没读到 Tests 目录的源码（{testDir}）");
        var joined = string.Concat(sources.Values);

        var cited = Regex.Matches(report, @"A\d{2}")
            .Select(m => m.Value).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        cited.Count.ShouldBeGreaterThan(15, "对照矩阵里该有几十条断言编号，读这么少说明报告是空的");
        foreach (var id in cited)
            joined.ShouldContain(id + "_", Case.Sensitive, $"报告指着 {id}，Tests 的源码里却没有这个方法名 —— 那一格的证据是空的");

        foreach (var file in new[] { "../../../Tools/Skill.cs", "../../../Tools/Facts.cs" })
        {
            var source = ReadSource(file);
            foreach (var literal in new[]
                     {
                         "warrior.", "tank.", "archer.", "rider.", "fx.", "state.", "status.", "ck.",
                         "attack_melee", "attack_shoot",
                     })
            {
                source.ShouldNotContain($"\"{literal}\"", Case.Sensitive,
                    $"工具里出现了 {literal} 这个名字字面量 —— 接第二个原型时就得改它，那是抽象漏了");
            }
        }

        ReadSource("../../../Tools/Skill.cs").ShouldContain("class Skill", Case.Sensitive, "读到的是那份工具本体，不是一个空文件");
    }

    // === 解析：只认报告自己的形状，认不出就抛（不把格式漂当成通过） ===

    /// <summary>取栏 1 那张表：返回 Id → 那一行的各格（按 | 切，去掉首尾两格）。</summary>
    private static Dictionary<string, string[]> ParseColumn1(string report)
    {
        var outRows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var inside = false;
        foreach (var line in report.Split('\n'))
        {
            if (line.StartsWith("## 栏 1", StringComparison.Ordinal)) { inside = true; continue; }
            if (inside && line.StartsWith("## ", StringComparison.Ordinal)) break;
            if (!inside || !line.StartsWith("| `", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            var id = cells[0].Trim('`');
            if (!outRows.TryAdd(id, cells))
                throw new InvalidOperationException($"报告栏 1 里 {id} 出现了两次");
        }
        outRows.Count.ShouldBe(16, "栏 1 没读到 16 行（报告形状漂了，不是这一条该松）");
        return outRows;
    }

    /// <summary>「3 步｜0.3 秒」取 3；「无前摇」「没有冷却」取 0。</summary>
    private static long StepsOf(string cell)
        => cell.StartsWith("无", StringComparison.Ordinal) || cell.StartsWith("没有", StringComparison.Ordinal)
            ? 0
            : long.Parse(cell.Split(' ')[0], CultureInfo.InvariantCulture);

    /// <summary>「4 米」取 4。</summary>
    private static double MetersOf(string cell)
        => double.Parse(cell.Split(' ')[0], CultureInfo.InvariantCulture);

    /// <summary>效果那一格：一份效果一段（多份用 &lt;br&gt; 隔开），「（技能自己不带效果）」算 0 份。</summary>
    private static int MultiCount(string cell)
        => cell.StartsWith("（技能自己不带效果", StringComparison.Ordinal) ? 0 : cell.Split("<br>").Length;

    // === A78：一次性动作台账（N-30 那一格）——读数本身 + 接线没被拆 ===

    /// <summary>
    /// <b>A78（这一局演了哪几条一次性动作，读得出来）</b>三条腿：
    /// ① 台账件的行为：演 / 被吞 / 模型没这条动作三类各记各的，读数对同一条记录序列逐字全等，
    ///    最近步号只留 <see cref="OneShotLedger.RecentTicksKept"/> 个，空 clip 名不记，换一局归零；
    /// ② 接线在码：表现层两处调用（起手动作与平A）都把返回值落进台账，桥注册表里有 <c>windups</c> 这一发 ——
    ///    把 <c>Record(...)</c> 摘掉，这一腿当场红（就是这条账的反证）；
    /// ③ 它在真值<b>下游</b>：逻辑层的快照里不许出现这份读数（与 cue 与 <c>dropped=</c> 同一档口径）。
    /// </summary>
    [Fact]
    public void A78_一次性动作台账_读数与接线都对得上()
    {
        // 腿 ①：件本身
        var ledger = new OneShotLedger();
        ledger.Readout().ShouldStartWith("一次性动作 演 0 次", Case.Sensitive, "什么都没演时也要念得出「没有」，不许给空字（空字读起来像坏了）");
        ledger.RecordPlayed("windup_slash", 45);
        ledger.RecordPlayed("windup_slash", 78);
        ledger.RecordDropped("attack_shoot");
        ledger.RecordAbsent("windup_pierce");
        ledger.PlayedTimes("windup_slash").ShouldBe(2, "演出去的那两次该数得出来");
        ledger.DroppedTimes("attack_shoot").ShouldBe(1, "被吞的次数是 N-27 那笔账的运行时半边");
        ledger.AbsentTimes("windup_pierce").ShouldBe(1, "「模型里没这条动作」与「被吞」是两回事，混在一起就读不出登记表漂了");
        ledger.Readout().ShouldContain("第 45/78 步", Case.Sensitive, "发生在第几步要念得出来（步 ÷ 10 = 秒）");
        ledger.TotalPlayed.ShouldBe(2);
        (ledger.TotalDropped + ledger.TotalAbsent).ShouldBe(2);

        var once = ledger.Readout();
        var again = new OneShotLedger();
        again.RecordPlayed("windup_slash", 45);
        again.RecordPlayed("windup_slash", 78);
        again.RecordDropped("attack_shoot");
        again.RecordAbsent("windup_pierce");
        again.Readout().ShouldBe(once, "同一条记录序列的读数逐字全等 —— 读数里不许掺时间与随机量");

        for (var step = 0; step < OneShotLedger.RecentTicksKept + 4; step++)
            again.RecordPlayed("windup_slam", step);
        again.PlayedTimes("windup_slam").ShouldBe(OneShotLedger.RecentTicksKept + 4, "次数照记，只有步号那一串封顶（长跑不许把内存吃穿）");
        var tail = again.Readout();
        tail.ShouldContain($"/{OneShotLedger.RecentTicksKept + 3} 步)", Case.Sensitive, $"最近步号只留 {OneShotLedger.RecentTicksKept} 个，留下的该是最后那几个");
        again.Reset();
        (again.TotalPlayed + again.TotalDropped + again.TotalAbsent).ShouldBe(0, "换一局必须归零");

        // 腿 ②：接线在码（摘掉任何一处 Record，这一腿红）
        var views = ReadSource("../Scripts/BattleUnitViews.cs");
        views.ShouldContain("Record(view.Anim.PlayOneShot(clip)", Case.Sensitive, "起手动作演没演没落进台账 —— N-30 那一格又空了（把这一处 Record 摘掉重跑，读数当场归零）");
        views.ShouldContain("Record(view.Anim.TriggerAttack()", Case.Sensitive, "平A 被吞几次没落进台账 —— N-27 只剩观感账、没有数");
        // 起手动作必须由技能层的 activate 事件形状驱动（AbilityActivated / AbilityActivatedAt，2026-09-29 换成钉形状）。
        // 按渲染帧采样状态那一版实测演 0 次：桥一发 advance --steps 300 把三五步的前摇整段跨过去，帧采样什么都看不见。
        views.ShouldContain("AbilityActivated", Case.Sensitive);
        views.ShouldContain("DrainWindups", Case.Sensitive, "起手动作又退回按帧采样状态了 —— 一次喂多步（快进、级 2 重放）会漏演，且漏得读不出来；" +
            "起手只准订行为流里 activate 那一族形状（AbilityActivated / AbilityActivatedAt）");
        AssertScriptsCarryNoSecondEventLedger();
        views.ShouldContain("Windups.Reset()", Case.Sensitive, "换一局没归零，下一局会读到上一局的演出");
        var bridge = ReadSource("../Scripts/Main.cs");
        bridge.ShouldContain("(\"windups\"", Case.Sensitive, "桥注册表里没这一发 —— 读数在码上、取不出来等于没有");
        ReadSource("../Scripts/MainReplayBridge.cs").ShouldContain("WindupReadoutDebug", Case.Sensitive, "桥那一发没有实现体");

        // 腿 ③：真值下游 —— 逻辑层的快照里不许掺这份读数
        var snapshot = ReadSource("../../../GugaFramework/Abilities/AbilityStepper.cs");
        snapshot.ShouldNotContain("一次性动作", Case.Sensitive, "台账进了快照就是进了真值，级 1 与级 2 的逐字基线会被演出次数绑住");
    }

    private static string ReadFile(string relative) => File.ReadAllText(Resolve(relative));

    /// <summary>
    /// 这条缝钉的是形状（与 <see cref="AbilityCueTests"/> 的 A73 同一条，2026-09-29 换）：
    /// 事件的名字与形状归公共层那份名单（<c>AbilityEvents.cs</c>），原型侧只读字段——
    /// <c>src/Prototypes/BattleArray/Scripts</c> 下搜不到 <c>.Detail</c>（把事件行文本再抠一遍当依据）、
    /// 搜不到 <c>Events.Add(</c>（自己往层的事件流里记账）、搜不到 <c>Emit("</c>（事件名字面量写在原型侧）。
    /// 读源码找禁用形状，比钉一句旧代码稳：旧代码改了字面、这条不跟着漂。
    /// </summary>
    private static void AssertScriptsCarryNoSecondEventLedger()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts"));
        Directory.Exists(dir).ShouldBeTrue($"读不到 {dir}（路径拼错了这条就是空跑）");
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        files.Length.ShouldBeGreaterThan(0, $"{dir} 下一份 .cs 都没读到（空跑）");
        foreach (var banned in new[] { ".Detail", "Events.Add(", "Emit(\"" })
        {
            foreach (var file in files)
            {
                File.ReadAllText(file).ShouldNotContain(banned, Case.Sensitive, $"{Path.GetFileName(file)} 里出现了禁用形状 {banned} —— 事件行只准由公共层的形状自己渲染，" +
                    "原型侧只读字段，不自己记第二本账");
            }
        }
    }

    private static string ReadSource(string relative)
    {
        var full = Resolve(relative);
        if (!File.Exists(full)) throw new FileNotFoundException($"读不到源码 {relative}", full);
        return File.ReadAllText(full);
    }

    private static string Resolve(string relative)
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
        return Path.GetFullPath(Path.Combine(baseDir, relative));
    }
}
