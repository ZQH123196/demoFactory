#nullable enable
using System;

namespace DemoFactory.GugaFramework.Scheduling;

/// <summary>
/// 调试速度控制 — AI 调试操控台的速度旋钮（纯 C#，零引擎依赖）。
///
/// 设计（2026-09-22 AI 调试操控台设计定稿 3 节）：
/// - 语义：每帧应推进多少逻辑步 —— 由"游戏秒/帧"换算，与帧率解耦
///   （tickRate 是游戏的逻辑频率，如 10 步/秒；1x = 每帧 tickRate/fps 个步）。
/// - 档位：0.25x 慢放 · 1x 正常 · 20x 快进 —— AI 随时拧、立即生效；
///   人调试的方式 = 无关紧要处加速、关键处放慢甚至暂停一步一步走。
/// - 暂停/单步不在这里：那是"要不要推"的开关（GameClock.Pause / Advance(1)），
///   本类只回答"推多快"。表现层每帧问一次 TicksThisFrame(fps)。
///
/// 架构位置：GugaFramework/Scheduling — 表现层驱动的换算器，逻辑层零感知
/// （哨兵测试已证明：推进分块方式不影响世界演化，见 DeterminismSentinelTests）。
/// </summary>
public sealed class SpeedControl
{
    /// <summary>当前速度倍率（1 = 实时；&lt;1 慢放；&gt;1 快进）。</summary>
    public double Multiplier { get; private set; } = 1.0;

    /// <summary>游戏的逻辑频率（步/秒）— 由构造方给定（常见取 10，即一步 0.1 秒）。</summary>
    private readonly double _tickRate;

    public SpeedControl(double tickRatePerSecond)
    {
        if (tickRatePerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(tickRatePerSecond));
        _tickRate = tickRatePerSecond;
    }

    /// <summary>拧旋钮 — AI eval 直达口。立即生效；负数抛异常（挡笔误）。</summary>
    public void SetMultiplier(double multiplier)
    {
        if (multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(multiplier), "速度倍率必须 > 0");
        Multiplier = multiplier;
    }

    /// <summary>恢复正常速度。</summary>
    public void Reset() => Multiplier = 1.0;

    /// <summary>
    /// 本帧应推进的步数（浮点累积在表现层持有，本类只做单帧换算）。
    /// 例：tickRate=10、1x、60fps → 0.166 步/帧（累积器模式消化）；
    /// 20x → 3.33；0.25x → 0.0416（约每 24 帧走一步 = 慢放）。
    /// </summary>
    public double TicksPerFrame(double frameDeltaSeconds) => Multiplier * _tickRate * frameDeltaSeconds;
}
