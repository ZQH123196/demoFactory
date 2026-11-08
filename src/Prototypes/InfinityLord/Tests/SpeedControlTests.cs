#nullable enable
using System;
using System.Linq;
using DemoFactory.GugaFramework.Scheduling;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// SpeedControl 换算测试 — 调试操控台速度旋钮的纯数学部分（零引擎依赖）。
/// 语义对齐 2026-09-22 操控台设计定稿 3 节：
/// 慢放 0.25x · 正常 1x · 快进 20x；帧率解耦 — 换算只吃 delta，不吃帧序号。
/// </summary>
public class SpeedControlTests
{
    private const double TickRate = 10.0; // 无限领主：10 步/秒

    [Fact]
    public void 实时档_每帧推游戏频率乘delta()
    {
        var ctl = new SpeedControl(TickRate);
        ctl.Multiplier.ShouldBe(1.0); // 初始 1x
        // 60fps 一帧 ≈ 0.0167s → 0.1667 tick
        ctl.TicksPerFrame(1.0 / 60).ShouldBe(TickRate / 60, 1e-12);
    }

    [Fact]
    public void 快进二十倍_与慢放四分之一档_换算正确()
    {
        var ctl = new SpeedControl(TickRate);
        ctl.SetMultiplier(20);
        ctl.TicksPerFrame(1.0 / 60).ShouldBe(20 * TickRate / 60, 1e-12);
        ctl.SetMultiplier(0.25);
        ctl.TicksPerFrame(1.0 / 60).ShouldBe(0.25 * TickRate / 60, 1e-12);
    }

    [Fact]
    public void 立即生效_Reset回1x_非法值抛异常()
    {
        var ctl = new SpeedControl(TickRate);
        ctl.SetMultiplier(20);
        ctl.Reset();
        ctl.Multiplier.ShouldBe(1.0);
        Should.Throw<ArgumentOutOfRangeException>(() => ctl.SetMultiplier(0));
        Should.Throw<ArgumentOutOfRangeException>(() => ctl.SetMultiplier(-3));
    }

    [Fact]
    public void 同一倍率不同帧率_单帧乘delta守恒_总tick不变()
    {
        // 速度旋钮语义的数学版本（哨兵测试已证明分块不影响演化）：
        // 同样 1 秒的现实时间，30fps 与 60fps 累积出的 步数总和必须相同
        var ctl = new SpeedControl(TickRate);
        ctl.SetMultiplier(20);
        var at60 = Enumerable.Range(0, 60).Sum(_ => ctl.TicksPerFrame(1.0 / 60));
        var at30 = Enumerable.Range(0, 30).Sum(_ => ctl.TicksPerFrame(1.0 / 30));
        at30.ShouldBe(at60, 1e-9);
    }
}