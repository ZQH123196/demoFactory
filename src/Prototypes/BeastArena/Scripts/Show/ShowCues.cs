#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.Prototypes.BeastArena.Audio;

namespace DemoFactory.Prototypes.BeastArena.Show;

/// <summary>
/// 规格「演出」的九类事件 —— 表现层只按这九格取「动作 / 特效 / 音效」三样，
/// 不另起第十类（对接面清单冻结口径）。
/// </summary>
public enum ShowCategory
{
    Eat = 0,
    Crystal = 1,
    FormShift = 2,
    TierAdvance = 3,
    Build = 4,
    Damage = 5,
    Battle = 6,
    CaveEmerge = 7,
    FinalDefence = 8,
}

/// <summary>一类事件的三样表现件：特效轨名、音效 cue、巨兽 clip（没有就空串）。</summary>
public sealed record ShowCue(string VfxTrack, string AudioCue, string BeastClip);

/// <summary>
/// 九类事件的对照表 —— <b>唯一一份</b>。特效轨名映射到 <c>src/Shared/vfx</c> 里那四件，
/// 音效 cue 必须在 <see cref="AudioCatalog"/> 里能查到，巨兽 clip 必须在 <c>clips_beast.gd</c> 里。
/// 纯 C#，离线测试逐条能查（ShowCueTests）。
/// </summary>
public static class ShowCues
{
    /// <summary>特效轨名 → 共享粒子场景。公共层不认识具体效果名，这张表由原型递进去。</summary>
    public static readonly IReadOnlyDictionary<string, string> VfxTracks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["impact"] = "res://src/Shared/vfx/FxHit.tscn",
        ["ring"] = "res://src/Shared/vfx/FxShapeRing.tscn",
        ["mark"] = "res://src/Shared/vfx/FxStackMark.tscn",
    };

    private static readonly ShowCue[] Table =
    [
        new("ring", "beast_swallow", "eat"),
        new("mark", "crystal_drop", ""),
        new("ring", "form_shift", "form_shift"),
        new("ring", "tier_advance", "advance"),
        new("ring", "build_done", ""),
        new("impact", "impact_hit", ""),
        new("impact", "impact_hit", ""),
        new("mark", "alarm_warning", ""),
        new("mark", "alarm_critical", "lie_down"),
    ];

    public static ShowCue Of(ShowCategory category) => Table[(int)category];

    /// <summary>九类各自的默认显示名 —— 结果标记与战报同一份词（不另造名称）。</summary>
    public static string NameOf(ShowCategory category) => category switch
    {
        ShowCategory.Eat => "吞食",
        ShowCategory.Crystal => "魔晶",
        ShowCategory.FormShift => "形态切换",
        ShowCategory.TierAdvance => "进阶",
        ShowCategory.Build => "建造",
        ShowCategory.Damage => "受损",
        ShowCategory.Battle => "战斗",
        ShowCategory.CaveEmerge => "洞穴出土",
        _ => "最终防守",
    };
}
