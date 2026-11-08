#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 技能前摇的<b>动画载体表</b> —— 一条技能按下之后，模型里哪条起手动作替它把前摇演出来。
///
/// 位置与 <see cref="SkillCueSpec"/> 同一类：这是一份<b>只读的表现层说明书</b>，不是真值 ——
/// 结算、快照、级 1/级 2 基线一个都不读它（「表现层只读」：表现层只读逻辑流，不写回）。
///
/// 为什么只有四条：16 条技能里只有前摇 &gt; 0 的那四条有起手段（突斩 3 步、踏地 2 步、
/// 穿刺箭 2 步、践踏 2 步），其余 12 条按下即提交、本来没有起手这件事可演。
/// 探针件（<c>ck.*</c> / <c>probe.*</c>）不算内容，一律不给它们挂号。
///
/// 为什么时长可以比前摇长：测试那侧只咬「动画比前摇短」（短了就是逻辑还在蓄力、画面已演完，读数在骗人）；
/// 而前摇只有 0.2~0.3 秒，正好等长的动作肉眼读不出是什么招，所以起手 clip 含预备与收招、演到 0.6~0.7 秒。
/// 确认与逐格实测见 `docs/prototypes/battle-array/技能层验证记录.md`
/// （那一轮的实跑报告已并入 `docs/research/技能系统/调研结论摘要.md`，原文在 git 历史）。
///
/// <b>这一份是唯一的出处</b>：开发期那台技能体检单（`Tools/BattleArraySkills.cs`）从这里读载体名，
/// 表现层（<c>Scripts/BattleUnitViews.cs</c>）也从这里读 —— 别在两边各抄一遍 clip 名（N-24 同族）。
/// clip 本身的轨道内容在动作库：<c>src/GugaFramework/Tools/glb/clips_humanoid.gd</c>（骑手那件走 <c>clips_mount.gd</c>）。
/// </summary>
public static class SkillWindups
{
    /// <summary>战士突斩的起手：举剑到顶点落在 commit 那一帧，之后劈出、收招（0.7 秒）。</summary>
    public const string SlashClip = "windup_slash";

    /// <summary>坦克踏地的起手：双盾举过顶再砸地，身体同时沉下去（0.6 秒）。</summary>
    public const string SlamClip = "windup_slam";

    /// <summary>弓手穿刺箭的起手：托弓拉满那一帧落在 commit 上，随即松弦（0.6 秒）。</summary>
    public const string PierceClip = "windup_pierce";

    /// <summary>骑手践踏的起手：坐骑后蹲再前冲压蹄，骑手同时举刃（0.7 秒，两副骨架合并演）。</summary>
    public const string TrampleClip = "windup_trample";

    /// <summary>
    /// 骑手践踏的 id。逻辑侧那张表（<see cref="BattleAbilities"/>）今天没登记它 ——
    /// 践踏只挂在 H2 单挑场那侧，H3 的宿主表里没有这一格。
    /// 这份字面量被测试钉着与 <c>SkillDuelRift.Skill.Trample</c> 逐字相等
    /// （<c>Tests/AbilitySkillReviewTests.cs</c> 的 A76 腿 ⑤），改名那天会红，漂不了。
    /// </summary>
    public const string TrampleAbilityId = "rider.trample";

    /// <summary>
    /// 技能 → （由哪个兵种槽位演、演哪条起手动作）。前三条 id 从逻辑侧那张表的常量取，
    /// 践踏那条见 <see cref="TrampleAbilityId"/> 的说明 —— 这里不重打 id 字面量。
    /// 兵种槽位回答的是「哪一档兵种演」，件名由调用方按种族从兵种卡取（两族各一件、同库同时长）。
    /// </summary>
    private static readonly Dictionary<string, (TroopKind Kind, string Clip)> Table = new(StringComparer.Ordinal)
    {
        [BattleAbilities.RiftAbilityId] = (TroopKind.Warrior, SlashClip),
        [BattleAbilities.SlamAbilityId] = (TroopKind.Tank, SlamClip),
        [BattleAbilities.PierceAbilityId] = (TroopKind.Archer, PierceClip),
        [TrampleAbilityId] = (TroopKind.Rider, TrampleClip),
    };

    /// <summary>这条技能由哪条起手动作承载；按下即提交的那 12 条返回 null（体检单据此念「不适用」）。</summary>
    public static string? ClipFor(string abilityId) =>
        Table.TryGetValue(abilityId, out var row) ? row.Clip : null;

    /// <summary>这条技能的起手动作由哪一档兵种演；没挂号的返回 null。</summary>
    public static TroopKind? KindFor(string abilityId) =>
        Table.TryGetValue(abilityId, out var row) ? row.Kind : null;

    /// <summary>挂了载体的技能有几条 —— 体检单与测试用它核对条数，别各自数一遍。</summary>
    public static int Count => Table.Count;
}
