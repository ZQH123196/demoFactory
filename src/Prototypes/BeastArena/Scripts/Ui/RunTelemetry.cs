#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.Prototypes.BeastArena.Logic;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 一段终局结算里的现实耗时快照（**表现层的账**，不进逻辑快照、不参与 L3 断言）。
/// </summary>
/// <param name="StageSeconds">按段（贫城 / 沙城 / 富城）累计的现实秒数。</param>
/// <param name="MilestoneSeconds">四个里程碑达成时的累计现实秒数。</param>
/// <param name="TotalSeconds">从开局到快照那一刻的总现实秒数。</param>
public sealed record RunTelemetrySnapshot(
    IReadOnlyDictionary<RunStage, double> StageSeconds,
    IReadOnlyDictionary<RunMilestone, double> MilestoneSeconds,
    double TotalSeconds)
{
    /// <summary>贫城段（开局航程 + 贫城段）的现实秒数。</summary>
    public double PoorSeconds => Of(RunStage.ToPoor) + Of(RunStage.Poor);

    /// <summary>沙城段（第一段航行 + 沙城段）的现实秒数。</summary>
    public double SandSeconds => Of(RunStage.ToSand) + Of(RunStage.Sand);

    /// <summary>富城段（第二段航行 + 富城 + 防守 + 结算）的现实秒数。</summary>
    public double RichSeconds => Of(RunStage.ToRich) + Of(RunStage.Rich)
        + Of(RunStage.FinalDefence) + Of(RunStage.NormalDefence)
        + Of(RunStage.Settlement) + Of(RunStage.FreePlay);

    /// <summary>某个里程碑达成时的累计现实秒数（没达成 = -1）。</summary>
    public double MilestoneOf(RunMilestone kind)
        => MilestoneSeconds.TryGetValue(kind, out var seconds) ? seconds : -1.0;

    private double Of(RunStage stage) => StageSeconds.TryGetValue(stage, out var seconds) ? seconds : 0.0;
}

/// <summary>
/// 现实耗时的记账器 —— 住表现层，**逻辑层读不到它**（spec「终局结算」）。
///
/// <para>只做两件事：按当前段累计墙钟秒数、在里程碑达成那一刻记下累计值。
/// 终局结算打开时取一份 <see cref="Snapshot"/>，之后继续经营怎么走都不再改写那一份。</para>
///
/// <para><b>不读逻辑快照</b>：段从 <see cref="RunFlow.PhaseEntered"/> 来，里程碑从
/// <see cref="RunFlow.MilestoneReached"/> 来；两个都是事件，不是去问世界「现在哪一段」。</para>
/// </summary>
public sealed class RunTelemetry
{
    private readonly Dictionary<RunStage, double> _stageSeconds = [];
    private readonly Dictionary<RunMilestone, double> _milestones = [];

    private RunFlow? _flow;
    private RunStage _stage = RunStage.NotStarted;
    private double _total;

    /// <summary>从开局到现在累计的现实秒数。</summary>
    public double TotalSeconds => _total;

    /// <summary>换一局：摘掉旧流程的订阅、清账、接上新流程。</summary>
    public void Reset(RunFlow? flow)
    {
        if (_flow is not null)
        {
            _flow.PhaseEntered -= OnPhaseEntered;
            _flow.MilestoneReached -= OnMilestoneReached;
        }

        _stageSeconds.Clear();
        _milestones.Clear();
        _total = 0.0;
        _flow = flow;
        _stage = flow?.Stage ?? RunStage.NotStarted;

        if (flow is null) return;

        flow.PhaseEntered += OnPhaseEntered;
        flow.MilestoneReached += OnMilestoneReached;
    }

    /// <summary>每帧递一次渲染帧的墙钟增量（暂停时也照走 —— 那是现实时间，不是游戏时间）。</summary>
    public void Tick(double delta)
    {
        if (delta <= 0.0) return;

        _total += delta;
        _stageSeconds[_stage] = Of(_stage) + delta;
    }

    /// <summary>取一份快照（终局结算用；取完之后继续记账也不改这一份）。</summary>
    public RunTelemetrySnapshot Snapshot()
        => new(new Dictionary<RunStage, double>(_stageSeconds),
            new Dictionary<RunMilestone, double>(_milestones),
            _total);

    private void OnPhaseEntered(RunStage stage) => _stage = stage;

    private void OnMilestoneReached(RunMilestone kind, long tick)
        => _milestones.TryAdd(kind, _total);

    private double Of(RunStage stage) => _stageSeconds.TryGetValue(stage, out var seconds) ? seconds : 0.0;
}
