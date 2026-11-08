#nullable enable
// 混音策略 —— 规格「音效」两条判据的纯 C# 真值：
// 1) 相同命中音在 80 毫秒内合并；2) 警报 / 胜负 / 成长优先于普通命中音。
// 只进不出的纯函数：时间由调用方以毫秒递进来（假时钟可测，不自持时钟、不碰主线程）。
// T17 才接线播放；本件是策略层，不含任何 Godot 类型（逻辑层纯 C# 同口径）。

using System;
using System.Collections.Generic;

namespace DemoFactory.Prototypes.BeastArena.Audio;

/// <summary>声件角色档位。数字越大越优先（Ambience 底声 → Alert 警报/胜负）。</summary>
public enum AudioPriority
{
    /// <summary>环境底声（循环床）。</summary>
    Ambience = 0,

    /// <summary>普通操作反馈（点击/建造/交易/攻击/受击/步伐）。</summary>
    Feedback = 1,

    /// <summary>生态与成长提示（吞食/魔晶/形态/进阶/广播）。</summary>
    Growth = 2,

    /// <summary>警报与胜负。</summary>
    Alert = 3,
}

/// <summary>一条正在排队的声线。时长以毫秒计（时长归调用方从资产表取）。</summary>
public sealed record AudioVoice(string Cue, AudioPriority Priority, long StartMs, long DurationMs)
{
    public bool IsPlayingAt(long nowMs) => nowMs >= StartMs && nowMs < StartMs + DurationMs;
}

public enum MixOutcome
{
    /// <summary>正常发声。</summary>
    Play,

    /// <summary>80 毫秒窗口内已有同 cue 的普通反馈在发，合并（不发第二下）。</summary>
    MergedSameCue,

    /// <summary>更高档（成长/警报）声线正在发，普通反馈让位。</summary>
    BlockedByHigherPriority,
}

/// <summary>一次混音裁决：发不发 + 该停掉哪些低档声线（抢占）。</summary>
public sealed record MixDecision(MixOutcome Outcome, IReadOnlyList<AudioVoice> ToStop)
{
    public static MixDecision Play(params AudioVoice[] stop)
        => new(MixOutcome.Play, stop);
}

public static class AudioMixPolicy
{
    /// <summary>规格「音效」：相同命中音在 80 毫秒内合并。</summary>
    public const long MergeWindowMs = 80;

    /// <summary>
    /// 纯函数裁决。active 只传「当下与刚过去」的声线（调用方负责清历史）。
    /// Feedback 档：先查同 cue 合并窗口，再查高档声线让位，都不挡才 Play。
    /// Growth / Alert 档：直接 Play，并把正在发的 Feedback 声线列为 ToStop（抢占）。
    /// </summary>
    public static MixDecision Decide(
        long nowMs, string cue, AudioPriority priority, IReadOnlyList<AudioVoice> active)
    {
        if (priority == AudioPriority.Feedback)
        {
            foreach (var v in active)
            {
                if (v.Priority != AudioPriority.Feedback || v.Cue != cue) continue;
                var since = nowMs - v.StartMs;
                if (since >= 0 && since < MergeWindowMs)
                    return new MixDecision(MixOutcome.MergedSameCue, Array.Empty<AudioVoice>());
            }
            foreach (var v in active)
            {
                if (v.Priority > AudioPriority.Feedback && v.IsPlayingAt(nowMs))
                    return new MixDecision(MixOutcome.BlockedByHigherPriority, Array.Empty<AudioVoice>());
            }
            return MixDecision.Play();
        }

        if (priority >= AudioPriority.Growth)
        {
            var stop = new List<AudioVoice>();
            foreach (var v in active)
            {
                if (v.Priority == AudioPriority.Feedback && v.IsPlayingAt(nowMs)) stop.Add(v);
            }
            return MixDecision.Play(stop.ToArray());
        }

        // Ambience 床：不参与合并与抢占，直接发（床与床的交叉接管在样本排布里做）。
        return MixDecision.Play();
    }
}
