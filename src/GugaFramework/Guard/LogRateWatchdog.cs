#nullable enable
using System;
using System.IO;
using System.Linq;
using Godot;

namespace DemoFactory.GugaFramework.Guard;

/// <summary>
/// 日志速率看门狗（GugaFramework 通用件）— 「日志喷涌就是内存喷涌的前兆」的自动刹车。
///
/// 事故背景（2026-09-23 级联 OOM 事故，红线见 AGENTS.md「进程与运行时安全」）：无人值守游戏局进入
/// D3D12 坏状态后每帧刷渲染 ERROR，日志 ~1.6MB/s，19 分钟喷 1.72GB 拖垮整机。
/// 本组件低频采样日志文件大小，增速超阈即触发刹车回调 — 正常局日志 ~7KB/小时，
/// 阈值给四个数量级余量（默认 20KB/s），正常玩法永不误触。
///
/// 分工：本组件只管「量出异常 + 鸣笛」，**刹车动作由原型侧回调决定**（暂停模拟 +
/// 关渲染 + 弹提示 — 渲染错误按帧×绘制调用产生，纯暂停断不掉错误源，必须连渲染一起关）。
/// 采样走 _Process 累积计时（低频，每 SampleIntervalSeconds 一次，开销可忽略）。
///
/// 表现层组件（同 Camera/DebugBridge 惯例）：测试项目 source-link 显式 Exclude。
/// </summary>
public partial class LogRateWatchdog : Node
{
    /// <summary>采样间隔（秒）— 事故里洪水 1 分钟内见势，5s 足够快。</summary>
    [Export] public double SampleIntervalSeconds { get; set; } = 5.0;

    /// <summary>刹车阈值（字节/秒）。正常局 <1B/s，洪水 ~1.6MB/s — 默认 20KB/s 居中。</summary>
    [Export] public long ThresholdBytesPerSecond { get; set; } = 20 * 1024;

    /// <summary>刹车回调（原型侧接线：暂停 + 关渲染 + 弹提示 + 记录）。触发后不再重复触发。</summary>
    public Action<long>? OnBreach { get; set; }

    /// <summary>要盯的日志文件路径（Godot 默认日志：user://logs/godot.log）。</summary>
    private string? _logPath;

    private long _lastSize;
    private double _accumulator;
    private bool _tripped;

    public override void _Ready()
    {
        // Godot 4.7 文件日志落 user://logs/godot.log（项目名子目录）；缺文件则本轮不盯
        var dir = OS.GetEnvironment("APPDATA");
        var project = ProjectSettings.GetSetting("application/config/name").AsString();
        var candidates = new[]
        {
            Path.Combine(dir, "Godot", "app_userdata", project, "logs", "godot.log"),
        };
        _logPath = candidates.FirstOrDefault(File.Exists)
            ?? candidates[0]; // 文件尚未落地时也先记路径（下次采样再试）
    }

    public override void _Process(double delta)
    {
        if (_tripped || OnBreach is null) return;
        _accumulator += delta;
        if (_accumulator < SampleIntervalSeconds) return;
        _accumulator = 0;

        try
        {
            if (_logPath is null || !File.Exists(_logPath)) return;
            var size = new FileInfo(_logPath).Length;
            var rate = (long)((size - _lastSize) / SampleIntervalSeconds);
            _lastSize = size;
            if (rate > ThresholdBytesPerSecond)
            {
                _tripped = true; // 一次触发即锁存 — 刹车由原型侧接管，不反复打扰
                GD.PushWarning($"[Watchdog] 日志增速 {rate / 1024:F0}KB/s 超阈"
                    + $"（{ThresholdBytesPerSecond / 1024}KB/s）— 触发刹车回调");
                OnBreach.Invoke(rate);
            }
        }
        catch (Exception)
        {
            // 日志文件被轮转/删除的瞬态 — 下个采样周期再看
        }
    }

    /// <summary>复位（刹车动作处理完、人工确认安全后重新武装）。</summary>
    public void Reset()
    {
        _tripped = false;
        _accumulator = 0;
        try { _lastSize = _logPath is not null && File.Exists(_logPath) ? new FileInfo(_logPath).Length : 0; }
        catch { _lastSize = 0; }
    }
}
