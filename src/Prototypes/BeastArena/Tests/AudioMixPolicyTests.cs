#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.Prototypes.BeastArena.Audio;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 混音策略纯函数测试（规格「音效」两条判据）。
/// 时间全部是传进来的毫秒整数（假时钟），不真等、不起线程。
/// 怕什么：① 80 毫秒合并窗口写错（差一）；② 高档声线不挡普通命中；③ 抢占名单漏人或多停人。
/// </summary>
public class AudioMixPolicyTests
{
    private static AudioVoice Voice(string cue, AudioPriority p, long startMs, long durMs = 300)
        => new(cue, p, startMs, durMs);

    [Theory]
    [InlineData(0)]   // 同一毫秒再来一下：合并。
    [InlineData(79)]  // 窗口内最后一个毫秒：合并。
    public void 相同命中音在80毫秒内合并(long deltaMs)
    {
        // Arrange
        var active = new[] { Voice("impact_hit", AudioPriority.Feedback, 1000) };

        // Act
        var d = AudioMixPolicy.Decide(1000 + deltaMs, "impact_hit", AudioPriority.Feedback, active);

        // Assert
        d.Outcome.ShouldBe(MixOutcome.MergedSameCue, $"相隔 {deltaMs} 毫秒的同 cue 应当合并发 0 次");
    }

    [Fact]
    public void 恰好80毫秒出窗正常发声()
    {
        var active = new[] { Voice("impact_hit", AudioPriority.Feedback, 1000) };
        var d = AudioMixPolicy.Decide(1080, "impact_hit", AudioPriority.Feedback, active);
        d.Outcome.ShouldBe(MixOutcome.Play, "窗口是左闭右开：第 80 毫秒整该发就发");
    }

    [Fact]
    public void 不同cue不互相合并()
    {
        var active = new[] { Voice("attack_shot", AudioPriority.Feedback, 1000) };
        var d = AudioMixPolicy.Decide(1010, "impact_hit", AudioPriority.Feedback, active);
        d.Outcome.ShouldBe(MixOutcome.Play);
    }

    [Fact]
    public void 警报声线正在发时普通命中让位()
    {
        var active = new[] { Voice("alarm_warning", AudioPriority.Alert, 1000, 1750) };
        var d = AudioMixPolicy.Decide(1500, "impact_hit", AudioPriority.Feedback, active);
        d.Outcome.ShouldBe(MixOutcome.BlockedByHigherPriority, "规格：警报优先于普通命中");
    }

    [Fact]
    public void 警报收声后普通命中恢复()
    {
        var active = new[] { Voice("alarm_warning", AudioPriority.Alert, 1000, 400) };
        var d = AudioMixPolicy.Decide(1500, "impact_hit", AudioPriority.Feedback, active);
        d.Outcome.ShouldBe(MixOutcome.Play);
    }

    [Fact]
    public void 成长与警报发声时抢占在发的普通命中()
    {
        var hit = Voice("impact_hit", AudioPriority.Feedback, 1000, 400);
        var d = AudioMixPolicy.Decide(1100, "tier_advance", AudioPriority.Growth, new[] { hit });
        d.Outcome.ShouldBe(MixOutcome.Play);
        d.ToStop.ShouldBe(new[] { hit }, "抢占名单只收正在发的 Feedback 声线");
    }

    [Fact]
    public void 抢占不停另一条高档声线()
    {
        var grow = Voice("beast_swallow", AudioPriority.Growth, 1000, 1000);
        var d = AudioMixPolicy.Decide(1100, "result_victory", AudioPriority.Alert, new[] { grow });
        d.ToStop.ShouldBeEmpty("警报只压普通命中，高档之间叠着发由混音响度差保证");
    }

    [Fact]
    public void 环境床不参与合并与抢占()
    {
        var bed = Voice("amb_dock", AudioPriority.Ambience, 0, 8000);
        var d = AudioMixPolicy.Decide(1000, "amb_dock", AudioPriority.Ambience, new[] { bed });
        d.Outcome.ShouldBe(MixOutcome.Play);
        d.ToStop.ShouldBeEmpty();
    }

    [Fact]
    public void 目录里每个cue都取得到且档位覆盖八类()
    {
        // Arrange / Act
        var cats = new HashSet<AudioCategory>();
        foreach (var e in AudioCatalog.All)
        {
            AudioCatalog.Get(e.Cue).ShouldBe(e, "cue 名单与条目必须同表同值");
            cats.Add(e.Category);
        }

        // Assert：八类一类不缺（规格「音效」清单）。
        cats.Count.ShouldBe(8, "八类覆盖有缺");
        foreach (var c in Enum.GetValues<AudioCategory>()) cats.Contains(c).ShouldBeTrue($"{c} 一类没有任何声件");
    }

    [Fact]
    public void 不认识的cue当场抛()
    {
        Should.Throw<ArgumentException>(() => AudioCatalog.Get("no_such_cue"));
    }
}
