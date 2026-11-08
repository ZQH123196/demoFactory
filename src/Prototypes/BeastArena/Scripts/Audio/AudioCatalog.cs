#nullable enable
// 声件目录 —— cue 名 → 资产路径 / 八类归属 / 角色档位 的唯一一张表（「唯一来源与派生优先」）。
// 路径一律按 res:// 引用（「按 res:// 引用」）；触发语义与对接面清单（interface.md）事件名对齐，
// 对照表见 docs/prototypes/beast-arena/audio-review.md。T17 接线播放时只认这里的 cue 名。
// 本件是纯 C#，不含 Godot 类型；资产本身由 audiogen 动词从零重跑（「资产可重跑」同口径）。

using System;
using System.Collections.Generic;

namespace DemoFactory.Prototypes.BeastArena.Audio;

/// <summary>规格「音效」要求的八类。</summary>
public enum AudioCategory
{
    Ui,
    BuildTrade,
    EatCrystal,
    FormGrowth,
    AttackHit,
    AlertResult,
    BeastStep,
    AmbienceBroadcast,
}

public sealed record AudioEntry(string Cue, string ResPath, AudioCategory Category, AudioPriority Priority);

public static class AudioCatalog
{
    /// <summary>环境床 cue 名前缀（循环床与单条 one-shot 的区分就靠这个前缀，只此一份）。</summary>
    public const string AmbiencePrefix = "amb_";

    private static readonly AudioEntry[] Entries =
    {
        // cue 名与 .scratch 声原型（audio-prototype.html）的 16 个单项事件同名。
        new("click", "res://src/Prototypes/BeastArena/Audio/ui_click.wav", AudioCategory.Ui, AudioPriority.Feedback),
        new("deny", "res://src/Prototypes/BeastArena/Audio/ui_deny.wav", AudioCategory.Ui, AudioPriority.Feedback),
        new("build_done", "res://src/Prototypes/BeastArena/Audio/build_done.wav", AudioCategory.BuildTrade, AudioPriority.Feedback),
        new("trade_done", "res://src/Prototypes/BeastArena/Audio/trade_done.wav", AudioCategory.BuildTrade, AudioPriority.Feedback),
        new("beast_swallow", "res://src/Prototypes/BeastArena/Audio/beast_swallow.wav", AudioCategory.EatCrystal, AudioPriority.Growth),
        new("crystal_drop", "res://src/Prototypes/BeastArena/Audio/crystal_drop.wav", AudioCategory.EatCrystal, AudioPriority.Growth),
        new("form_shift", "res://src/Prototypes/BeastArena/Audio/form_shift.wav", AudioCategory.FormGrowth, AudioPriority.Growth),
        new("tier_advance", "res://src/Prototypes/BeastArena/Audio/tier_advance.wav", AudioCategory.FormGrowth, AudioPriority.Growth),
        new("attack_shot", "res://src/Prototypes/BeastArena/Audio/attack_shot.wav", AudioCategory.AttackHit, AudioPriority.Feedback),
        new("impact_hit", "res://src/Prototypes/BeastArena/Audio/impact_hit.wav", AudioCategory.AttackHit, AudioPriority.Feedback),
        new("alarm_warning", "res://src/Prototypes/BeastArena/Audio/alarm_warning.wav", AudioCategory.AlertResult, AudioPriority.Alert),
        new("alarm_critical", "res://src/Prototypes/BeastArena/Audio/alarm_critical.wav", AudioCategory.AlertResult, AudioPriority.Alert),
        new("result_victory", "res://src/Prototypes/BeastArena/Audio/result_victory.wav", AudioCategory.AlertResult, AudioPriority.Alert),
        new("result_defeat", "res://src/Prototypes/BeastArena/Audio/result_defeat.wav", AudioCategory.AlertResult, AudioPriority.Alert),
        new("beast_step", "res://src/Prototypes/BeastArena/Audio/beast_step.wav", AudioCategory.BeastStep, AudioPriority.Feedback),
        new("broadcast_horn", "res://src/Prototypes/BeastArena/Audio/broadcast_horn.wav", AudioCategory.AmbienceBroadcast, AudioPriority.Growth),
        new("amb_dock", "res://src/Prototypes/BeastArena/Audio/amb_dock.wav", AudioCategory.AmbienceBroadcast, AudioPriority.Ambience),
        new("amb_sail", "res://src/Prototypes/BeastArena/Audio/amb_sail.wav", AudioCategory.AmbienceBroadcast, AudioPriority.Ambience),
        new("amb_defense", "res://src/Prototypes/BeastArena/Audio/amb_defense.wav", AudioCategory.AmbienceBroadcast, AudioPriority.Ambience),
    };

    public static IReadOnlyList<AudioEntry> All => Entries;

    /// <summary>按 cue 名取条目；不认识的名字当场抛（Fail Fast，不兜 null）。</summary>
    public static AudioEntry Get(string cue)
    {
        foreach (var e in Entries)
        {
            if (string.Equals(e.Cue, cue, StringComparison.Ordinal)) return e;
        }
        throw new ArgumentException($"声件目录里没有 cue '{cue}'（cue 名单的唯一出处就是这张表）", nameof(cue));
    }

    /// <summary>res:// 路径换成仓库根相对路径（校验文件在不在盘上用）。</summary>
    public static string ToRepoRelative(string resPath)
    {
        const string Prefix = "res://";
        if (!resPath.StartsWith(Prefix, StringComparison.Ordinal))
            throw new ArgumentException($"路径不是 res:// 起头：{resPath}", nameof(resPath));
        return resPath[Prefix.Length..].Replace('/', System.IO.Path.DirectorySeparatorChar);
    }
}
