#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DemoFactory.GugaFramework.Replay;

/// <summary>
/// 级 2 引擎重放口 —— 把一份剧本<b>按条目</b>喂进一个活着的世界，喂一条报一次。
///
/// 为什么要有它（可调试性那条账里的 D8）：<see cref="ReplayRunner.Replay"/> 是「一口气跑完」的形状（级 1，
/// 离线毫秒级）；真引擎里那条通道必须一发喂一条 —— 暂停中单步要看画面、桥一次连接只服务一发请求、
/// 跑挂了要知道停在哪一条。两边如果各写一份驱动器，「级 2 与级 1 逐字相等」就变成在比
/// 两套实现像不像，而不是同一套逻辑在引擎里跑没跑对。<b>所以驱动器只有这一份</b>：
/// <see cref="ReplayRunner.Replay"/> 已改成逐条泵这个类（时序与断言语义一字未动，
/// 公共层自己的重放测试就是它的回归网）。
///
/// 时序契约沿用 <see cref="ReplayRunner"/>：命令与检查点共用一条时间线，按 (步号, 录制序号) 稳定排序；
/// 同一步内「先命令后检查点」与「先检查点后命令」都能逐字还原。断言档位（L2 语义 / L3 逐字）也沿它。
///
/// 本文件是纯 C#（零 Godot 类型），所以测试项目与游戏程序集编的是<b>同一份源码</b> ——
/// 这正是级 1 与级 2 可比的前提。桥那一侧只转发它的 <see cref="Step"/> / <see cref="RunAll"/>，
/// 不在桥里另写一套（AGENTS 「桥只转发」）。
/// </summary>
public sealed class LiveReplaySession
{
    /// <summary>一个检查点的实跑结果：录制时那份、实跑这份、等不等。等不了也要把两份留下读。</summary>
    public sealed record CheckpointResult(long Tick, int Seq, string Label, string Expected, string Actual, bool Equal);

    /// <summary>一条命令的实跑结果：ok 翻转就是行为分歧（任何断言档位都算红）。</summary>
    public sealed record CommandResult(long Tick, int Seq, string Cmd, string Args, bool ExpectedOk, bool ActualOk, bool Equal);

    private readonly ReplayRunner.Hooks _hooks;
    private readonly List<(long Tick, int Seq, ReplayRunner.JournalEntry? Cmd, ReplayRunner.Checkpoint? Mark)> _timeline = [];
    private int _cursor;
    private bool _startHandled;

    public LiveReplaySession(ReplayRunner.ReplayScript script, ReplayRunner.Hooks hooks)
    {
        Script = script;
        _hooks = hooks;
        foreach (var e in script.Entries) _timeline.Add((e.Tick, e.Seq, e, null));
        foreach (var c in script.Checkpoints) _timeline.Add((c.Tick, c.Seq, null, c));
        _timeline.Sort((a, b) => a.Tick != b.Tick
            ? a.Tick.CompareTo(b.Tick)
            : a.Seq.CompareTo(b.Seq));
    }

    /// <summary>喂进来的那份剧本（词表与快照口径由 <see cref="Hooks"/> 提供，本层不认识具体世界）。</summary>
    public ReplayRunner.ReplayScript Script { get; }

    /// <summary>时间线总条目数（进度读数用：级 2 一发一步，得能报「第几条 / 共几条」）。</summary>
    public int ItemsTotal => _timeline.Count;

    /// <summary>已喂掉的条目数。</summary>
    public int ItemsDone => _cursor;

    /// <summary>全部喂完没（喂完后 <see cref="Step"/> 不再动世界，只回报「已到底」）。</summary>
    public bool Finished => _cursor >= _timeline.Count;

    /// <summary>跑出来的分歧清单（命令 ok 翻转 + 检查点快照分歧，按发生顺序）。</summary>
    public List<string> Divergences { get; } = [];

    /// <summary>每个检查点的实跑结果 —— <see cref="DumpSequence"/> 与逐格读快照都从这儿出。</summary>
    public List<CheckpointResult> Checkpoints { get; } = [];

    /// <summary>每条命令的实跑结果（D3 的 Journal 要看「每发都真打到了写路径」）。</summary>
    public List<CommandResult> Commands { get; } = [];

    /// <summary>有分歧吗。</summary>
    public bool HasDivergence => Divergences.Count > 0;

    /// <summary>
    /// 起点步对齐：从运行中的世界接着录的剧本（StartTick &gt; 0）先把世界跑到录制起点，
    /// 再断言起点快照。<b>必须在第一条条目之前调</b>，跑过两次也只生效一次。
    /// </summary>
    /// <param name="sink">断言消息出口：null = 不断言（级 2 只要实跑值），非空则逐字 / 语义档照常报。</param>
    public string PrepareStart(Action<string?>? sink = null)
    {
        if (_startHandled) return "起点已对齐过（本条不再动世界）";
        _startHandled = true;
        if (Script.StartTick <= 0) return "起点即开局（StartTick=0，无需对齐）";

        _hooks.AdvanceTo(Script.StartTick);
        if (string.IsNullOrEmpty(Script.StartSnapshot))
            return $"起点已推进到第 {Script.StartTick} 步（录制端没记起点快照，只对齐步号）";

        var start = _hooks.Snapshot();
        var msg = Script.AssertLevel switch
        {
            ReplayRunner.AssertLevel.Semantic => ReplayRunner.SemanticStartMsg(Script.StartSnapshot!, start, Script.StartTick),
            _ => start == Script.StartSnapshot
                ? null
                : $"起点快照分歧 @tick={Script.StartTick}（重放世界 ≠ 录制开录时的世界）"
                  + ReplayRunner.ReplayRunnerDiff.Hint(Script.StartSnapshot!, start),
        };
        if (msg != null) Divergences.Add(msg);
        sink?.Invoke(msg);
        return msg ?? $"起点快照逐字对上了（第 {Script.StartTick} 步）";
    }

    /// <summary>
    /// 喂掉下一条条目：先推进到那条的步号，再按类型做事（检查点抓快照比对 / 命令打进写路径比 ok）。
    /// 一次一条 —— 级 2 的粒度就是这个；级 1 的「跑完」是外面套一圈 <see cref="RunAll"/>。
    /// </summary>
    public string ExecuteNext(Action<string?>? sink = null)
    {
        if (Finished) return "剧本已喂完（本条没动世界）";
        if (!_startHandled) PrepareStart(sink);

        var item = _timeline[_cursor];
        _cursor++;
        if (item.Tick > 0) _hooks.AdvanceTo(item.Tick);

        if (item.Mark != null) return CaptureCheckpoint(item.Mark, sink);
        return RunCommand(item.Cmd!, sink);
    }

    /// <summary>
    /// 连喂若干条（桥一发可以要多条；缺省一条）。返回人读的多行报告，每行带「第几条 / 共几条 / 第几步」。
    /// </summary>
    public string Step(int items = 1)
    {
        var lines = new List<string>();
        for (var i = 0; i < items; i++)
        {
            if (Finished)
            {
                lines.Add($"已到底：{ItemsDone}/{ItemsTotal} 条已喂完");
                break;
            }
            var tick = _timeline[_cursor].Tick;
            var report = ExecuteNext();
            lines.Add($"第 {ItemsDone}/{ItemsTotal} 条（步 {tick}）：{report}");
        }
        lines.Add($"进度 {ItemsDone}/{ItemsTotal} 分歧 {Divergences.Count}");
        return string.Join("\n", lines);
    }

    /// <summary>一口气喂完（级 1 的形状；<see cref="ReplayRunner.Replay"/> 走的就是这一条）。</summary>
    public string RunAll(Action<string?>? sink = null)
    {
        PrepareStart(sink);
        while (!Finished) ExecuteNext(sink);
        return $"已喂完 {ItemsTotal} 条，分歧 {Divergences.Count}";
    }

    /// <summary>
    /// 级 1 / 级 2 逐字比对的产物：一行一个检查点的指纹（<c>cp;录制序号;步号;标签;长度;sha256</c>）。
    /// 为什么要指纹而不是整篇：级 2 走的是活局，一次要跨进程回传，几十 KB 的快照序列读都读不动；
    /// 指纹先定位「第几个检查点开始不一样」，整篇再按 <see cref="SnapshotOf"/> 单点取。
    /// </summary>
    public string DumpSequence() => string.Join("\n", Checkpoints.Select(c =>
        $"cp;{c.Seq};{c.Tick.ToString(CultureInfo.InvariantCulture)};{c.Label};{c.Actual.Length};"
        + Sha256Hex(c.Actual) + (c.Equal ? ";等" : ";分歧")));

    /// <summary>取某一条检查点的实跑全文（按录制序号取，指纹对不上时读这个）。</summary>
    public string SnapshotOf(int seq) => Checkpoints.FirstOrDefault(c => c.Seq == seq)?.Actual
        ?? $"查无检查点 seq={seq}（已跑的序号：{string.Join(",", Checkpoints.Select(c => c.Seq))}）";

    private string CaptureCheckpoint(ReplayRunner.Checkpoint cp, Action<string?>? sink)
    {
        var actual = _hooks.Snapshot();
        var msg = Script.AssertLevel switch
        {
            ReplayRunner.AssertLevel.Semantic => ReplayRunner.SemanticCheckpointMsg(cp, actual),
            _ => actual == cp.Snapshot
                ? null
                : $"检查点「{cp.Label}」@tick={cp.Tick} 快照分歧（重放世界 ≠ 录制时点的世界）"
                  + ReplayRunner.ReplayRunnerDiff.Hint(cp.Snapshot, actual),
        };
        var equal = msg is null;
        Checkpoints.Add(new CheckpointResult(cp.Tick, cp.Seq, cp.Label, cp.Snapshot, actual, equal));
        if (msg != null) Divergences.Add(msg);
        sink?.Invoke(msg);
        return equal
            ? $"检查点「{cp.Label}」逐字对上（实跑 {actual.Length} 字符）"
            : msg!;
    }

    private string RunCommand(ReplayRunner.JournalEntry e, Action<string?>? sink)
    {
        var ok = _hooks.Inject(e.Cmd, e.Args);
        var msg = ok == e.Ok
            ? null
            : $"命令 {e.Cmd}({e.Args}) 重放返回与录制不一致（录制 ok={e.Ok}，重放 ok={ok}）";
        Commands.Add(new CommandResult(e.Tick, e.Seq, e.Cmd, e.Args, e.Ok, ok, msg is null));
        if (msg != null) Divergences.Add(msg);
        sink?.Invoke(msg);
        return msg ?? $"命令 {e.Cmd}({e.Args}) 照录制打出 ok={ok}";
    }

    private static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
