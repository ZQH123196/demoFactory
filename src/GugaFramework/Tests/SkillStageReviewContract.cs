#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using DemoFactory.Tools;
using StageChecklistShell = DemoFactory.Tests.StageChecklistShell;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// <b>场子侧的技能体检单契约</b>（ticket 08）——新场子把体检单那三栏接上时，<b>契约由这一侧递</b>：
/// 报哪几条技能、每条的数从哪张真表取、钉它的是哪几个断言编号，全在本文件；
/// <see cref="Skill"/>（公共层那份共用实现）只管排版、阈值与校验器，一个技能名都不认。
///
/// <b>三份注册壳各管一段</b>，与场子那份注册口同一形状（场子分不出这三份）：
/// <list type="bullet">
/// <item><b>清单注册壳</b>（<see cref="StageChecklistShell"/>）：16 格清单那 5~6 条技能，
///   跑在<b>第一个场子</b>（<see cref="SkillStage"/>）上，钉它的是清单本体的 F/D 编号。</item>
/// <item><b>示范注册壳</b>（<see cref="SkillStageSample"/>）：一条真技能跑通时间线，
///   三个场子都用它，钉它的是场子自己的 S 编号。</item>
/// </list>
///
/// <b>数字一个都不在这里抄</b>：全部经 <see cref="Numbers"/> 从真表现取，取不到当场抛
/// （与另一侧那份契约同一手法，「原型隔离」 的边界：公共层不替内容侧兜一份默认数字）。
///
/// <b>「前摇载体」那两格在本壳一律留空</b>，理由见 <see cref="StageReviewRows"/>：
/// 场子按 spec 不带单位模型，体检单的前摇对表那一头要读 <c>.glb</c> 里的 clip 名义时长，
/// 没有件就没有那一头 —— 这是<b>如实记「不适用」</b>，不是把这一格做绿。
/// </summary>
public static class SkillStageReviewContract
{
    /// <summary>报告落哪（相对仓库根）。生成方是 <c>dotnet test</c> 里的渲染那一发，不是 <c>src/Tools</c> 那条命令。</summary>
    public const string ReportRelative = "docs/GugaFramework/技能试验场-技能体检单.md";

    /// <summary>宿主那一列写的名字：<b>新场子</b>（ticket 07 的开阔多点），另两个场子按行注明。</summary>
    public const string OpenFieldHost = "技能试验场·开阔多点";

    /// <summary>第一个场子那一份（长时段是它的参数档，不另算一个宿主）。</summary>
    public const string FirstStageHost = "技能试验场·贴脸单挑";

    /// <summary>示范注册壳那条真技能：三个场子都跑它。</summary>
    public const string SampleHost = "技能试验场·贴脸单挑 + 开阔多点（三个场子共用一份注册口）";

    /// <summary>
    /// <b>登记表</b>：每条除了「是哪条技能」与「钉它的是哪几个断言」之外什么都不写——
    /// 数一律现取（<see cref="Numbers"/>），宿主一律是场子名，载体一律留空。
    ///
    /// <b>断言编号必须真能在测试源码里搜到</b>：<c>S32</c> 逐个编号拿检索证明
    /// （与某一版审查单那份同一手法）——报告里写一个编造的编号等于这张表没有校验力。
    /// </summary>
    public static IReadOnlyList<SkillRow> StageReviewRows { get; } = new[]
    {
        // === 清单注册壳那几条（跑在第一个场子上，钉它的是清单本体 F/D 编号）===

        new SkillRow(StageChecklistShell.Skill.Windup, "清单注册壳·出手", FirstStageHost,
            "有前摇 + 有消耗 + 有冷却 + 立即结算·commit 前后取消的返还差",
            "F1 D5 D7 D1",
            CarrierModel: "", CarrierClip: "",
            Note: "长时段那一份也是它（同一个类，只把步数/观测窗口/账本上限三格拧一下）"),

        new SkillRow(StageChecklistShell.Skill.ChannelSlash, "清单注册壳·出手", FirstStageHost,
            "在放中授予 tag·入口被挡只管入口·显式打断",
            "F2 F3 F5 D5",
            CarrierModel: "", CarrierClip: ""),

        new SkillRow(StageChecklistShell.Skill.Lob, "清单注册壳·出手", FirstStageHost,
            "过 commit 登记成待决·按落地现算·源属性是快照",
            "F6 D2",
            CarrierModel: "", CarrierClip: ""),

        new SkillRow(StageChecklistShell.Skill.Pair, "清单注册壳·出手", FirstStageHost,
            "一次激活两条待决·错峰各自落地·互不顶掉",
            "F7",
            CarrierModel: "", CarrierClip: ""),

        new SkillRow(StageChecklistShell.Skill.Sealed, "清单注册壳·出手", FirstStageHost,
            "入口被 tag 挡住的那一条",
            "F2",
            CarrierModel: "", CarrierClip: ""),

        new SkillRow(StageChecklistShell.Skill.Rush, "清单注册壳·出手", FirstStageHost,
            "射程比其余各条远一档（5 米 vs 3 米）",
            "L1b",
            CarrierModel: "", CarrierClip: "",
            Note: "16 格清单本体不直接钉它（清单那页没有这一条），台账 L1b 逐条判射程边界两侧时钉它"),

        // === 示范注册壳那一条真技能（三个场子都跑它，钉它的是场子自己的 S 编号）===

        new SkillRow(SkillStageSample.Skill.Strike, "示范注册壳·出手", SampleHost,
            "一条技能从按下走到落地：前摇落在第几步、命中落在第几步、距离够不够",
            "S1 S2 S16 S25 S26 S27 S30",
            CarrierModel: "", CarrierClip: "",
            Note: "开阔多点那一份群体施加走的就是这条的效果（F7 那条多目标入口在层里还没有，见常驻文档缺口第 3 条）"),
    };

    /// <summary>体检单契约：<c>ModelsDir</c> 留空——场子不带模型，那一头没有件可读（见 <see cref="StageReviewRows"/>）。</summary>
    public static SkillContract Build() => new(
        ModelsDir: "",
        Rows: StageReviewRows,
        Rhythm: Array.Empty<RhythmRow>(),
        Numbers: Numbers,
        ReportPath: ReportRelative);

    /// <summary>
    /// 三栏里那些数字的<b>唯一出处</b>：两条表各管一段（清单注册壳的表 / 示范注册壳的表），
    /// 查不到当场抛 —— 兜一份默认数字等于让报告说谎。
    /// </summary>
    public static SkillNumbers Numbers(string abilityId)
    {
        var tables = TablesFor(abilityId);
        var a = tables.Ability(abilityId);
        // 「数取自哪张表」这一句不能用引用相等去判：两份表都是**每次调用现建**的
        // （SkillStageSample.Tables() / StageChecklistShell.Tables() 各自 new 一份），
        // 拿 `tables == SampleTables` 比永远是 false —— 报告会把两条都念成清单注册壳那张表。
        // 判据只能是「示范注册壳那张表里查得到它」。
        var fromSample = Registered(SampleTables, abilityId);
        return new SkillNumbers(
            a.PreSteps, a.PostSteps, a.CooldownTicks, a.MaxCharges, a.RangeMeters,
            a.CostAttribute, a.CostAmount,
            a.GrantedTagsWhileActive, a.RequirementTags, a.BlockedTags,
            a.EffectIds.Select(id => EffectNumbersOf(tables.Effect(id))).ToList(),
            Source: fromSample
                ? "示范注册壳那张表（SkillStageSample.Tables()）"
                : "清单注册壳那张表（StageChecklistShell.Tables()）");
    }

    /// <summary>一个效果的可读投影（字段照 <see cref="EffectDef"/> 的原名排，不改写、不重算）。</summary>
    private static EffectNumbers EffectNumbersOf(EffectDef e) => new(
        e.Id, e.Kind.ToString(), e.DurationTicks, e.PeriodTicks, e.DelayTicks,
        e.StackKey, e.MaxStacks, e.MagnitudePerStack,
        e.Modifiers.Select(m => m.CalcFromEffectValue
            ? $"{m.Target} {m.Mode}（走效果的 Calc）"
            : $"{m.Target} {m.Mode} {Num(m.Magnitude)}"
              + (Has(m.RequirementTags) ? $"[需 {string.Join(",", m.RequirementTags)}]" : "")).ToList(),
        OrEmpty(e.GrantedTags), OrEmpty(e.CancelMatchingTags),
        e.CurveAxis, e.CurveXY?.Count ?? 0,
        OrEmpty(e.TriggeredEffects).Select(t => $"{t.OnEvent}→{t.EffectId}").ToList(),
        e.ImmunityTagsOrEmpty);

    private static AbilityTables SampleTables => SkillStageSample.Tables();

    private static AbilityTables ChecklistTables => StageChecklistShell.Tables();

    /// <summary>
    /// 按技能名认出它住在哪张表——名字本身是三份注册壳各自的词表，认错了当场抛。
    /// <b>「在不在表里」问的是表自己的 <see cref="AbilityTables.Ability"/>（查不到它抛）</b>，
    /// 不另写一份名字清单（抄一份就会漂，与那两条词表各自漂同族）。
    /// </summary>
    private static AbilityTables TablesFor(string abilityId)
    {
        foreach (var tables in new[] { ChecklistTables, SampleTables })
            if (Registered(tables, abilityId))
                return tables;
        throw new KeyNotFoundException(
            $"{abilityId}：两份表里都没有它 —— 登记表与真表不同步了（清单注册壳 / 示范注册壳）");
    }

    private static bool Registered(AbilityTables tables, string abilityId)
    {
        try
        {
            tables.Ability(abilityId);
            return true;
        }
        catch (KeyError)
        {
            return false;
        }
    }

    private static bool Has(IReadOnlyList<string>? list) => list is { Count: > 0 };

    private static IReadOnlyList<string> OrEmpty(IReadOnlyList<string>? list) => list ?? Array.Empty<string>();

    private static IReadOnlyList<(string OnEvent, string EffectId)> OrEmpty(
        IReadOnlyList<(string OnEvent, string EffectId)>? list) => list ?? Array.Empty<(string, string)>();

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
