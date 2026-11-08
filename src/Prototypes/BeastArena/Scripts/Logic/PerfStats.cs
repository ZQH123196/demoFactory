#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.Prototypes.BeastArena.Logic;

/// <summary>
/// 性能采样的纯账本：帧时间、进程时间、逻辑步数与卡顿计数。
///
/// <para><b>为什么是纯 C#</b>：均值与分位数只跟样本有关，与 Godot 无关 ——
/// 离线用例能用合成样本钉住口径。采样本身（<c>delta</c>、引擎监视器、节点数、内存）
/// 留在表现层那一件。</para>
///
/// <para><b>三个时间分开记</b>：<c>frame_ms</c> 是相邻渲染帧的墙钟间隔（平均帧率与卡顿按它判），
/// <c>step_ms</c> 是这一帧全部逻辑推进（<c>_world.Advance</c> 加表现同步）的耗时 ——
/// spec 的 p95 / p99 阈值按它判，因为场上没有刚体，引擎的物理监视器量不到东西；
/// <c>process_ms</c> 是引擎的逐帧处理耗时（诊断用；D3D12 下它把呈现等待也算进去，
/// 所以可能比帧间隔还大 —— 那一格不能当帧预算读）。</para>
/// </summary>
public sealed class PerfStats
{
    /// <summary>卡顿阈值（毫秒）：单个渲染帧超过它记一次卡顿。</summary>
    public const double StutterThresholdMs = 100.0;

    private readonly int _capacity;
    private readonly List<double> _frameWindow;
    private readonly List<double> _stepWindow;
    private readonly List<double> _processWindow;

    private double _frameSum;
    private double _stepSum;
    private double _processSum;
    private double _wallSeconds;
    private double _maxFrameMs;
    private double _maxStepMs;
    private double _maxProcessMs;
    private long _frames;
    private long _steps;
    private long _stutters;
    private long _maxStepsPerFrame;
    private double _secondWall;
    private long _secondSteps;
    private double _secondExpected;
    private long _rateSeconds;
    private long _minStepsPerSecond;
    private long _lowRateSeconds;

    /// <summary>建一本账。窗口只留最近这么多帧的分位数样本；均值与极值按全窗口累计。</summary>
    public PerfStats(int capacity = 36_000)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "采样窗口必须为正");
        _capacity = capacity;
        _frameWindow = new List<double>(Math.Min(capacity, 4096));
        _stepWindow = new List<double>(Math.Min(capacity, 4096));
        _processWindow = new List<double>(Math.Min(capacity, 4096));
    }

    /// <summary>已采样帧数。</summary>
    public long Frames => _frames;

    /// <summary>窗口内累计推进的逻辑步数。</summary>
    public long Steps => _steps;

    /// <summary>卡顿次数（渲染帧间隔 &gt; 100 毫秒）。</summary>
    public long Stutters => _stutters;

    /// <summary>窗口墙钟秒数（各帧间隔之和）。</summary>
    public double WallSeconds => _wallSeconds;

    /// <summary>平均帧率（帧数 ÷ 墙钟秒）。</summary>
    public double AverageFps => _wallSeconds > 0.0 ? _frames / _wallSeconds : 0.0;

    /// <summary>平均渲染帧间隔（毫秒）。</summary>
    public double AverageFrameMs => _frames > 0 ? _frameSum / _frames : 0.0;

    /// <summary>平均进程耗时（毫秒，诊断用；含呈现等待）。</summary>
    public double AverageProcessMs => _frames > 0 ? _processSum / _frames : 0.0;

    /// <summary>平均逻辑帧耗时（毫秒）。</summary>
    public double AverageStepMs => _frames > 0 ? _stepSum / _frames : 0.0;

    /// <summary>渲染帧间隔最大值（毫秒）。</summary>
    public double MaxFrameMs => _maxFrameMs;

    /// <summary>进程耗时最大值（毫秒）。</summary>
    public double MaxProcessMs => _maxProcessMs;

    /// <summary>逻辑帧耗时的最大值（毫秒）。</summary>
    public double MaxStepMs => _maxStepMs;

    /// <summary>每秒实际推进的逻辑步数（窗口平均）。</summary>
    public double StepsPerSecond => _wallSeconds > 0.0 ? _steps / _wallSeconds : 0.0;

    /// <summary>单帧推进步数的最大值 —— spec「补步队列积压」那一格。</summary>
    public long MaxStepsPerFrame => _maxStepsPerFrame;

    /// <summary>完整墙钟秒数（每秒计数只在这些秒上取，不足一秒的尾巴不算）。</summary>
    public long RateSeconds => _rateSeconds;

    /// <summary>完整秒里推进步数的最小值（还没有完整秒时回 0）。</summary>
    public long MinStepsPerSecond => _rateSeconds > 0 ? _minStepsPerSecond : 0;

    /// <summary>
    /// 欠速秒数：某一完整秒推进的步数比该秒的期望少 <b>两步以上</b>。
    /// 少一步是累积器跨秒边界的进位，不是失速；spec 的「少于应推次数」按两步容差折算。
    /// </summary>
    public long LowRateSeconds => _lowRateSeconds;

    /// <summary>进程耗时的 p95（毫秒，最近窗口按最近秩取）。</summary>
    public double P95ProcessMs => Percentile(_processWindow, 0.95);

    /// <summary>进程耗时的 p99（毫秒）。</summary>
    public double P99ProcessMs => Percentile(_processWindow, 0.99);

    /// <summary>帧间隔的 p95（毫秒，诊断用；与帧率同一份样本）。</summary>
    public double P95FrameMs => Percentile(_frameWindow, 0.95);

    /// <summary>帧间隔的 p99（毫秒）。</summary>
    public double P99FrameMs => Percentile(_frameWindow, 0.99);

    /// <summary>逻辑推进耗时的 p95（毫秒）—— 验收的 p95 阈值按这一格判。</summary>
    public double P95StepMs => Percentile(_stepWindow, 0.95);

    /// <summary>逻辑推进耗时的 p99（毫秒）—— 验收的 p99 阈值按这一格判。</summary>
    public double P99StepMs => Percentile(_stepWindow, 0.99);

    /// <summary>
    /// 记一帧。样本必须是有限非负值；非法值当场抛。
    /// <paramref name="expectedStepsPerSecond"/> 是这一帧的速度倍率折算出的应推步数（步/秒）；
    /// 传 0 表示这一帧不参与欠速判定（例如还没开始推进的帧）。
    /// </summary>
    public void Add(double frameMs, double stepMs, double processMs, long steps, double expectedStepsPerSecond = 0.0)
    {
        if (!double.IsFinite(frameMs) || frameMs < 0.0)
            throw new ArgumentOutOfRangeException(nameof(frameMs), $"帧间隔必须是非负有限值，收到 {frameMs}");
        if (!double.IsFinite(stepMs) || stepMs < 0.0)
            throw new ArgumentOutOfRangeException(nameof(stepMs), $"逻辑帧耗时必须是非负有限值，收到 {stepMs}");
        if (!double.IsFinite(processMs) || processMs < 0.0)
            throw new ArgumentOutOfRangeException(nameof(processMs), $"进程耗时必须是非负有限值，收到 {processMs}");
        if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps), $"逻辑步数不能为负，收到 {steps}");
        if (!double.IsFinite(expectedStepsPerSecond) || expectedStepsPerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(expectedStepsPerSecond), $"期望步率必须是非负有限值，收到 {expectedStepsPerSecond}");

        _frames++;
        _steps += steps;
        _frameSum += frameMs;
        _stepSum += stepMs;
        _processSum += processMs;
        _wallSeconds += frameMs / 1000.0;
        if (frameMs > _maxFrameMs) _maxFrameMs = frameMs;
        if (stepMs > _maxStepMs) _maxStepMs = stepMs;
        if (processMs > _maxProcessMs) _maxProcessMs = processMs;
        if (frameMs > StutterThresholdMs) _stutters++;
        if (steps > _maxStepsPerFrame) _maxStepsPerFrame = steps;

        var seconds = frameMs / 1000.0;
        _secondWall += seconds;
        _secondSteps += steps;
        _secondExpected += expectedStepsPerSecond * seconds;
        if (_secondWall >= 1.0 - 1e-9) FlushSecond();

        Push(_frameWindow, frameMs);
        Push(_stepWindow, stepMs);
        Push(_processWindow, processMs);
    }

    /// <summary>清空全部样本与计数（窗口重新开一段）。</summary>
    public void Reset()
    {
        _frameWindow.Clear();
        _stepWindow.Clear();
        _processWindow.Clear();
        _frameSum = 0.0;
        _stepSum = 0.0;
        _processSum = 0.0;
        _wallSeconds = 0.0;
        _maxFrameMs = 0.0;
        _maxStepMs = 0.0;
        _maxProcessMs = 0.0;
        _frames = 0;
        _steps = 0;
        _stutters = 0;
        _maxStepsPerFrame = 0;
        _secondWall = 0.0;
        _secondSteps = 0;
        _secondExpected = 0.0;
        _rateSeconds = 0;
        _minStepsPerSecond = 0;
        _lowRateSeconds = 0;
    }

    /// <summary>一行 ASCII 读数（不变文化，字段名与《验证操作手册》同口径）。</summary>
    public string Readout()
        => string.Format(CultureInfo.InvariantCulture,
            "frames={0} fps={1:0.0} avg_frame_ms={2:0.00} p95_frame_ms={3:0.00} p99_frame_ms={4:0.00} "
            + "max_frame_ms={5:0.00} avg_step_ms={6:0.00} p95_step_ms={7:0.00} p99_step_ms={8:0.00} "
            + "max_step_ms={9:0.00} avg_process_ms={10:0.00} p95_process_ms={11:0.00} p99_process_ms={12:0.00} "
            + "max_process_ms={13:0.00} stutter={14} steps={15} steps_per_s={16:0.0} "
            + "max_steps_per_frame={17} rate_s={18} min_steps_per_s={19} low_rate_s={20}",
            _frames, AverageFps, AverageFrameMs, P95FrameMs, P99FrameMs,
            _maxFrameMs, AverageStepMs, P95StepMs, P99StepMs, _maxStepMs,
            AverageProcessMs, P95ProcessMs, P99ProcessMs, _maxProcessMs,
            _stutters, _steps, StepsPerSecond,
            _maxStepsPerFrame, _rateSeconds, MinStepsPerSecond, _lowRateSeconds);

    private void FlushSecond()
    {
        var expected = (long)Math.Round(_secondExpected);
        if (expected > 0)
        {
            _rateSeconds++;
            if (_rateSeconds == 1 || _secondSteps < _minStepsPerSecond) _minStepsPerSecond = _secondSteps;
            if (_secondSteps + 2 <= expected) _lowRateSeconds++;
        }

        _secondWall = 0.0;
        _secondSteps = 0;
        _secondExpected = 0.0;
    }

    private void Push(List<double> window, double value)
    {
        window.Add(value);
        if (window.Count > _capacity) window.RemoveAt(0);
    }

    /// <summary>最近秩分位数：排序后取第 ceil(p×n) 个（1 起算）。空窗口回 0。</summary>
    private static double Percentile(List<double> samples, double p)
    {
        if (samples.Count == 0) return 0.0;
        var sorted = new List<double>(samples);
        sorted.Sort();
        var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
        if (rank < 0) rank = 0;
        if (rank >= sorted.Count) rank = sorted.Count - 1;
        return sorted[rank];
    }
}
