#nullable enable
using System;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 性能采样账本：均值、最近秩分位数、卡顿计数与重置。
/// 采样值来自表现层的帧间隔、物理帧与进程耗时；这一层只算数，用例用合成样本钉住口径。
/// </summary>
public class PerfStatsTests
{
    [Fact]
    public void SteadySixtyFps_ReportsAverageAndZeroStutters()
    {
        var stats = new PerfStats();

        for (var i = 0; i < 100; i++) stats.Add(frameMs: 16.0, stepMs: 2.0, processMs: 5.0, steps: 1);

        stats.Frames.ShouldBe(100);
        stats.AverageFps.ShouldBe(62.5, 1e-9);
        stats.AverageFrameMs.ShouldBe(16.0, 1e-9);
        stats.AverageStepMs.ShouldBe(2.0, 1e-9);
        stats.AverageProcessMs.ShouldBe(5.0, 1e-9);
        stats.P95StepMs.ShouldBe(2.0, 1e-9);
        stats.P99StepMs.ShouldBe(2.0, 1e-9);
        stats.P95ProcessMs.ShouldBe(5.0, 1e-9);
        stats.P99ProcessMs.ShouldBe(5.0, 1e-9);
        stats.Stutters.ShouldBe(0);
        stats.Steps.ShouldBe(100);
        stats.StepsPerSecond.ShouldBe(62.5, 1e-9);
    }

    [Fact]
    public void Percentiles_TakeNearestRankFromTheWindow()
    {
        var stats = new PerfStats();

        for (var ms = 1; ms <= 100; ms++) stats.Add(frameMs: 10.0, stepMs: ms, processMs: ms, steps: 0);

        stats.P95StepMs.ShouldBe(95.0, 1e-9);
        stats.P99StepMs.ShouldBe(99.0, 1e-9);
        stats.MaxStepMs.ShouldBe(100.0, 1e-9);
        stats.P95ProcessMs.ShouldBe(95.0, 1e-9);
        stats.P99ProcessMs.ShouldBe(99.0, 1e-9);
        stats.MaxProcessMs.ShouldBe(100.0, 1e-9);
        stats.P95FrameMs.ShouldBe(10.0, 1e-9);
        stats.P99FrameMs.ShouldBe(10.0, 1e-9);
    }

    [Fact]
    public void LongFrame_CountsAsStutter_AndRaisesMax()
    {
        var stats = new PerfStats();
        stats.Add(frameMs: 16.0, stepMs: 2.0, processMs: 4.0, steps: 1);
        stats.Add(frameMs: 150.0, stepMs: 30.0, processMs: 140.0, steps: 3);

        stats.Stutters.ShouldBe(1);
        stats.MaxFrameMs.ShouldBe(150.0, 1e-9);
        stats.MaxStepMs.ShouldBe(30.0, 1e-9);
        stats.MaxProcessMs.ShouldBe(140.0, 1e-9);
        stats.Steps.ShouldBe(4);
    }

    [Fact]
    public void WindowCapacity_KeepsCountsButDropsOldPercentileSamples()
    {
        var stats = new PerfStats(capacity: 5);

        for (var ms = 1; ms <= 10; ms++) stats.Add(frameMs: 100.0, stepMs: 1.0, processMs: ms, steps: 0);

        stats.Frames.ShouldBe(10);
        stats.AverageProcessMs.ShouldBe(5.5, 1e-9);
        stats.P95ProcessMs.ShouldBe(10.0, 1e-9);
        stats.MaxProcessMs.ShouldBe(10.0, 1e-9);
    }

    [Fact]
    public void Reset_ClearsWindowAndCounters()
    {
        var stats = new PerfStats();
        for (var i = 0; i < 10; i++) stats.Add(frameMs: 120.0, stepMs: 20.0, processMs: 90.0, steps: 2);

        stats.Reset();

        stats.Frames.ShouldBe(0);
        stats.Steps.ShouldBe(0);
        stats.Stutters.ShouldBe(0);
        stats.AverageFps.ShouldBe(0.0);
        stats.P95StepMs.ShouldBe(0.0);
        stats.P95ProcessMs.ShouldBe(0.0);
        stats.Readout().ShouldContain("frames=0");
    }

    [Fact]
    public void InvalidSamples_ThrowImmediately()
    {
        var stats = new PerfStats();

        Should.Throw<ArgumentOutOfRangeException>(() => stats.Add(-1.0, 1.0, 1.0, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => stats.Add(1.0, -1.0, 1.0, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => stats.Add(1.0, 1.0, double.NaN, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => stats.Add(1.0, 1.0, 1.0, -1));
        Should.Throw<ArgumentOutOfRangeException>(() => stats.Add(1.0, 1.0, 1.0, 0, -10.0));
    }

    [Fact]
    public void FullSecond_CountsRateAndMaxCatchUpSteps()
    {
        var stats = new PerfStats();

        // 20 帧 × 50 毫秒 = 1 秒，每帧 4 步 → 80 步/秒，正好是 8 倍速的期望。
        for (var i = 0; i < 20; i++) stats.Add(frameMs: 50.0, stepMs: 8.0, processMs: 10.0, steps: 4, expectedStepsPerSecond: 80.0);

        stats.RateSeconds.ShouldBe(1);
        stats.MinStepsPerSecond.ShouldBe(80);
        stats.LowRateSeconds.ShouldBe(0);
        stats.MaxStepsPerFrame.ShouldBe(4);
        stats.StepsPerSecond.ShouldBe(80.0, 1e-9);
    }

    [Fact]
    public void OneMissingStep_IsAccumulatorCarry_LowRateNeedsTwo()
    {
        var stats = new PerfStats();

        // 一秒推 79 步：差一步是累加器跨秒边界的进位，不算失速。
        for (var i = 0; i < 20; i++)
        {
            var steps = i == 0 ? 3 : 4;
            stats.Add(frameMs: 50.0, stepMs: 8.0, processMs: 10.0, steps: steps, expectedStepsPerSecond: 80.0);
        }

        stats.MinStepsPerSecond.ShouldBe(79);
        stats.LowRateSeconds.ShouldBe(0);

        // 下个一秒只推 77 步：差三步，记一次欠速。
        for (var i = 0; i < 20; i++)
        {
            var steps = i < 3 ? 3 : 4;
            stats.Add(frameMs: 50.0, stepMs: 8.0, processMs: 10.0, steps: steps, expectedStepsPerSecond: 80.0);
        }

        stats.RateSeconds.ShouldBe(2);
        stats.MinStepsPerSecond.ShouldBe(77);
        stats.LowRateSeconds.ShouldBe(1);
    }

    [Fact]
    public void ZeroExpected_DoesNotJudgeRate()
    {
        var stats = new PerfStats();

        for (var i = 0; i < 60; i++) stats.Add(frameMs: 16.0, stepMs: 2.0, processMs: 4.0, steps: 0, expectedStepsPerSecond: 0.0);

        stats.RateSeconds.ShouldBe(0);
        stats.MinStepsPerSecond.ShouldBe(0);
        stats.LowRateSeconds.ShouldBe(0);
    }
}
