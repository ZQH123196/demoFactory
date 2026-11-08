// 排兵布阵的技能体检单契约（P5）—— 报告要排什么内容由这里递进公共层。
//
// 分工与 cue 那三份同一条（A72 的边界在这里同样成立）：
//   · 公共层 `src/Tools/Skill.cs` 只管排版、阈值与校验器，搜不到技能名与 clip 名；
//   · 本文件只管「报哪 16 条、每条顶住哪一格、谁钉着它、前摇的动画载体是谁」，
//     数字一个都不写 —— 全从真技能表现取（`SkillDuelRift.Tables()` 与 `BattleAbilities.TablesForReview()`）。
//
// 为什么抄这两个口子而不是在本文件里列一份数字表：报告上的数字一旦是抄的，
// 「报告说前摇 3 步、表里住 2 步」这种漂没人当场发现得了 —— 与 N-24 那两条词表各自漂同族。
// 挡它的是 Tests/AbilitySkillReviewTests.cs 那条对表的核对（A76）。
//
// 前摇载体这一格从 2026-09-28 起**不再是全空**：有前摇的那四条（突斩 / 踏地 / 穿刺箭 / 践踏）
// 各有一条起手动作挂在模型里，载体表住在 Scripts/Logic/SkillWindups.cs —— 这份契约从它读，不另抄一遍
// （抄了就会漂，与 N-24 那两条词表同族）。其余 12 条按下即提交、本来没有起手段，照旧留空、
// 栏 2 念「不适用：没有动画载体」而不是「通过」。
// 时长那一头：起手 clip 比前摇长（0.6~0.7 秒顶 0.2~0.3 秒），判红只咬「动画比前摇短」那一侧 ——
// 起手动作要演得让人看得出是什么招，正好等长的那一条早就被否掉了（已删的验证报告）。

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BattleArray.Tests;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.BattleArray.Logic;

namespace DemoFactory.Tools.BattleArray;

/// <summary>一条技能在体检单上除数字之外的那几格身份（内容，只住在这个文件里）。</summary>
public static class BattleArraySkills
{
    private sealed record Declared(string Kind, string Hosts, string GasRows, string Evidence, string Note = "");
    private const string ModelsDir = "src/Prototypes/BattleArray/Models";
    private const string ReportPath = "docs/prototypes/battle-array/skill-review.md";

    /// <summary>
    /// 16 条技能的登记表（顺序 = 「16 条清单」那四档兵种的顺序）。
    /// 名字一律从两处真表里取常量，不在这里重打一遍字面量。
    /// </summary>
    private static readonly (string Id, Declared Meta)[] Declared16 =
    {
        (SkillDuelRift.Skill.Guard, new Declared("坦克", "H2 + H3（四类 cue 上屏 A71~A75）",
            "同段两笔 modifier（排序键）·Override 撞界·Duration·tag 授予·入口激活检查", "A55 A56 A57")),
        (SkillDuelRift.Skill.Dread, new Declared("坦克", "H2",
            "光环按距离反复重新应用与撤销", "A37 A38")),
        (BattleAbilities.SlamAbilityId, new Declared("坦克", "H3（形状查询在真二维站位上）",
            "显式打断·形状查询不用物理（距离 + 角度）·减速类 modifier", "A39 A64 A65",
            "技能自己不带效果：命中那一发由宿主在 commit 那一刻按二维站位发 <c>ApplyEffect</c>"
            + "（与 N-13 同一种形状），栏 1 里那条 30 步减速就是它")),
        (SkillDuelRift.Skill.Steady, new Declared("坦克", "H2",
            "tag 计数被消耗（一次性抵挡）·计数型资源·周期回充", "A31 A32 A60")),

        (SkillDuelRift.Skill.Rift, new Declared("战士", "H2 + H3（级 1 与级 2 逐格全等）",
            "CommitAbility 提交点·Calc 链·冷却与返还口径·排队释放", "A34 A35 A36 A41")),
        (SkillDuelRift.Skill.Execute, new Declared("战士", "H2",
            "Cost 与来源属性快照·谓词激活检查·按目标已损失生命计算", "A58")),
        (SkillDuelRift.Skill.Bleed, new Declared("战士", "H2",
            "GE 堆叠策略三列·period 频率·Duration ≠ period 的正证", "A59 A60 A79 A80")),
        (SkillDuelRift.Skill.Rage, new Declared("战士", "H2",
            "GameplayEvent 触发效果·资源型属性与上限的界", "A40 A81")),

        (SkillDuelRift.Skill.Pierce, new Declared("弓手", "H2 + H3（延迟结算那条指示线）",
            "投射物＝延迟结算·目标按 Id 落地现解·距离曲线·多项 Calc 链", "A42 A43")),
        (SkillDuelRift.Skill.Rain, new Declared("弓手", "H2",
            "同一次激活多条 Pending 互不顶掉·错峰到期·打空也读得出为什么", "A61")),
        (SkillDuelRift.Skill.Mark, new Declared("弓手", "H2",
            "tag 计数进计算·冷却时长被外部修改（attribute 口 N-7）", "A62")),
        (SkillDuelRift.Skill.Evasive, new Declared("弓手", "H2",
            "被动常驻 modifier·Override 与 Min/Max 的先后（Meta 口径）", "A44")),

        (BattleAbilities.ChargeAbilityId, new Declared("骑手", "H3（位置第一次成为技能的输出）",
            "位移载荷 Pending.Move·已行走距离曲线·到位与伤害同一步", "A66 A67")),
        (SkillDuelRift.Skill.Trample, new Declared("骑手", "H2",
            "控制链·激活检查的第二处问（S5 走位前再问一次）", "A45")),
        (SkillDuelRift.Skill.Joust, new Declared("骑手", "H2",
            "多段结算·后段带 CancelMatchingTags 的打断（延迟落地也走同一队列）", "A63")),
        (SkillDuelRift.Skill.Mounted, new Declared("骑手", "H2",
            "modifier 自带条件列（RequirementTags，不满足 ≠ 撤销）", "A46 A82")),
    };

    /// <summary>体检单契约：16 条技能 + 八张兵种卡的出手节奏 + 从真表现取数字的那两个口子。</summary>
    public static SkillContract Build() => new(
        ModelsDir: ModelsDir,
        Rows: Declared16.Select(d => new SkillRow(
            d.Id, d.Meta.Kind, d.Meta.Hosts, d.Meta.GasRows, d.Meta.Evidence,
            CarrierModel: CarrierModelFor(d.Id), CarrierClip: SkillWindups.ClipFor(d.Id) ?? "",
            Note: NoteWithCarrier(d))).ToList(),
        Rhythm: RhythmRows(),
        Numbers: NumbersFromTables,
        ReportPath: ReportPath);

    /// <summary>
    /// 这条技能的前摇由哪一件模型演 —— 载体表（<see cref="SkillWindups"/>）只回答「哪一档兵种」，
    /// 件名从兵种卡取。两族各一件、同一份动作库、时长相同，体检单按登记表上第一件报（一格只装得下一个件名）。
    /// 没挂号的那 12 条退成空字，栏 2 随之念「不适用：没有动画载体」。
    /// </summary>
    private static string CarrierModelFor(string abilityId)
        => SkillWindups.KindFor(abilityId) is { } kind ? UnitCards.FirstOfKind(kind).ModelName : "";

    /// <summary>挂了载体的那四条，把「两族各一件、报的是第一件」这句写在内容侧那一栏，别让读者以为另一族没做。</summary>
    private static string NoteWithCarrier((string Id, Declared Meta) d)
    {
        if (SkillWindups.KindFor(d.Id) is not { } kind) return d.Meta.Note;
        var both = string.Join(" 与 ", UnitCards.ModelsOfKind(kind));
        var tail = $"起手动作两族各一件（{both}，同库同时长），这一格报第一件作代表";
        return d.Meta.Note.Length == 0 ? tail : $"{d.Meta.Note}｜{tail}";
    }

    /// <summary>
    /// 平A 的出手节奏：间隔取自兵种卡的 <c>AttackIntervalTicks</c>（过了种族规则的那份），
    /// clip 名取自同一张卡 —— 逻辑「每隔多少步出手一次」与动画「这一刀演多久」是两本账，
    /// 报告把它们并排印出来，谁长于谁当场读得出来。
    /// </summary>
    private static IReadOnlyList<RhythmRow> RhythmRows()
        => UnitCards.All.Select(u => new RhythmRow(
            $"{u.Race}·{u.DisplayName}", u.ModelName, u.AttackClip,
            u.Stats.AttackIntervalTicks)).ToList();

    /// <summary>
    /// 两条表各管一段：踏地与冲锋的定义表在 H3 宿主侧（用户确认：这两格的台账落真世界那侧），
    /// 其余 14 条在 H2 单挑场的注册壳里。查不到就抛 —— 兜一份默认数字等于让报告说谎。
    /// </summary>
    private static SkillNumbers NumbersFromTables(string abilityId)
    {
        var h3 = BattleAbilities.TablesForReview();
        var fromH3 = abilityId is BattleAbilities.SlamAbilityId or BattleAbilities.ChargeAbilityId;
        var tables = fromH3 ? h3 : SkillDuelRift.Tables();
        var a = tables.Ability(abilityId);
        return new SkillNumbers(
            a.PreSteps, a.PostSteps, a.CooldownTicks, a.MaxCharges, a.RangeMeters,
            a.CostAttribute, a.CostAmount,
            a.GrantedTagsWhileActive, a.RequirementTags, a.BlockedTags,
            a.EffectIds.Select(id => Numbers(tables.Effect(id))).ToList(),
            Source: fromH3 ? "H3 宿主侧那张表（<c>BattleAbilities</c>）" : "H2 单挑场注册壳那张表（<c>SkillDuelRift</c>）");
    }

    /// <summary>一条效果的可读投影（字段照 <c>EffectDef</c> 的原名排，不改写、不重算）。</summary>
    private static EffectNumbers Numbers(EffectDef e) => new(
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

    // 表里这几列的缺省是 null（record 的默认值写法就是 null!），报告不替它兜——投影这一层兜住，
    // 免得一条没写 RequirementTags 的效果把整页报告炸掉（实测：第一次跑就撞在 ModifierSpec 上）。
    private static bool Has(IReadOnlyList<string>? list) => list is { Count: > 0 };
    private static IReadOnlyList<string> OrEmpty(IReadOnlyList<string>? list) => list ?? Array.Empty<string>();
    private static IReadOnlyList<(string OnEvent, string EffectId)> OrEmpty(
        IReadOnlyList<(string OnEvent, string EffectId)>? list) => list ?? Array.Empty<(string, string)>();

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
