#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DemoFactory.GugaFramework.Replay;

/// <summary>
/// 重放取证器（计划 Phase 2）— 从「断言器」升级成「事故调查组」。
///
/// 三件事（全在通用层，纯 C# 零引擎依赖，与 ReplayRunner 同构——零游戏知识）：
/// 1. 报告模式：<see cref="ReplayWithReport"/> 断言失败不抛即返，产出结构化报告
///    （分歧点 + 字段级 diff + 分歧前 Journal + 两侧快照）。**旁路**：
///    <see cref="ReplayRunner.Replay"/> 本体零改动——正常重放的逐字断言语义不动
///    （哨兵测试不许红），本文件独立成文、与本体共用 Hooks/词表。
/// 2. 结构化快照 diff：<see cref="Diff"/>/<see cref="DiffSegments"/>——快照串按
///    「 | 」切段对位、段内按空白切 token 对位（key=value 记号），输出
///    「哪个字段、两侧各是什么」（替代"首处差异字符下标"——drill A 卡点①）。
///    通用层只做对位结构比较；段格式的语义是原型侧知识，快照序列化端自兜
///    （录制/重放两端同一份代码，格式契约不变）。
/// 3. 一键二分：<see cref="Bisect"/>——「每轮只重放到裁剪点」的对半裁剪：
///    裁 k = 只注入前 k 条命令，检查点仅在**其前命令已全部注入**时才断言
///    （裁掉的命令在时间轴上位于其后，不影响被断言检查点的语义）。
///    于是「裁 k 重放是否分歧」关于 k 单调 → log₂ 步收敛到第一个分歧 prefix。
///    定性（诚实边界，无旧代码时黑盒能力的硬限）：
///    · 分歧 = 命令 ok 翻转 → **精确点名该命令**（log₂ over entries 兑现）；
///    · 分歧 = 检查点快照 → 只能收缩到**该检查点前的命令窗口**
///      （裁掉窗口内任何一条命令都会改变到点状态，任何 prefix &lt; k_C 的探针
///      根本断言不到该检查点——状态归因需原型侧结合字段级 diff 自行收口）。
///
/// 二分风险条款（计划风险表）：二分只在 StartTick==0 的脚本启用首版——
/// 起点≠0 的脚本「推进到起点」本身依赖未录命令的中间历史，裁剪语义微妙，留待真实需求。
/// </summary>
public static class DivergenceReport
{
    // ================= 结构化快照 diff =================

    /// <summary>一条字段级差异：token 对（录制侧 / 重放侧，各为一个 key=value 或裸值）。</summary>
    public sealed record TokenDiff(string Expected, string Actual)
    {
        public override string ToString() => $"录制「{Expected}」 ≠ 重放「{Actual}」";
    }

    /// <summary>一条段级差异：段头 + 该段内的字段级差异列表。</summary>
    public sealed record SegmentDiff(string SegmentHeader, IReadOnlyList<TokenDiff> Tokens)
    {
        public override string ToString()
            => $"[{SegmentHeader}] " + string.Join("; ", Tokens.Select(t => t.ToString()));
    }

    /// <summary>
    /// 结构化快照 diff（人读）— 按「 | 」切段对位，段内按空白切 token 对位。
    /// 快照完全相等 → 空串（调用方继续走逐字断言）；有差异 → 前 maxDiffs 条。
    /// </summary>
    public static string Diff(string expected, string actual, int maxDiffs = 8)
    {
        var diffs = DiffSegments(expected, actual);
        if (diffs.Count == 0)
            return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.Append("结构化 diff（前 ").Append(Math.Min(diffs.Count, maxDiffs))
          .Append('/').Append(diffs.Count).Append(" 处）：");
        for (var i = 0; i < diffs.Count && i < maxDiffs; i++)
            sb.Append('\n').Append(diffs[i]);
        return sb.ToString();
    }

    /// <summary>
    /// 机器读的结构化差异列表：同下标段对位（段头 = 段首两个 token，实体段即
    /// 「U mob_1」式 类型+Id 标识），段内同下标 token 对位；段/token 数不齐时
    /// 缺失侧记「(缺失)」。两串逐字相等 → 空表（本层不做模糊匹配——快照同构性
    /// 由序列化端保证：录制/重放两端同一份代码）。
    /// </summary>
    public static List<SegmentDiff> DiffSegments(string expected, string actual)
    {
        var result = new List<SegmentDiff>();
        if (expected == actual)
            return result;

        var expSegs = expected.Split(Separator, StringSplitOptions.None);
        var actSegs = actual.Split(Separator, StringSplitOptions.None);
        var n = Math.Min(expSegs.Length, actSegs.Length);
        for (var i = 0; i < n; i++)
        {
            if (expSegs[i] == actSegs[i])
                continue;
            var header = SegmentHeader(expSegs[i], actSegs[i], i);
            var tokens = new List<TokenDiff>();
            var expToks = expSegs[i].Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            var actToks = actSegs[i].Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            var m = Math.Min(expToks.Length, actToks.Length);
            for (var j = 0; j < m; j++)
            {
                if (expToks[j] != actToks[j])
                    tokens.Add(new TokenDiff(expToks[j], actToks[j]));
            }
            for (var j = m; j < expToks.Length; j++)
                tokens.Add(new TokenDiff(expToks[j], "(重放侧缺失)"));
            for (var j = m; j < actToks.Length; j++)
                tokens.Add(new TokenDiff("(录制侧缺失)", actToks[j]));
            result.Add(new SegmentDiff(header, tokens));
        }
        for (var i = n; i < expSegs.Length; i++)
            result.Add(new SegmentDiff(SegmentHeader(expSegs[i], null, i),
                new[] { new TokenDiff(expSegs[i].Trim(), "(重放侧缺失)") }));
        for (var i = n; i < actSegs.Length; i++)
            result.Add(new SegmentDiff(SegmentHeader(null, actSegs[i], i),
                new[] { new TokenDiff("(录制侧缺失)", actSegs[i]) }));
        return result;
    }

    /// <summary>段头：两侧各取段首两个 token（实体段 = 类型 + Id）；缺侧用「?」占位。</summary>
    private static string SegmentHeader(string? expSeg, string? actSeg, int index)
    {
        static string HeadOf(string? seg)
        {
            var toks = seg?.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            return toks.Length switch
            {
                0 => "?",
                1 => toks[0],
                _ => toks[0] + " " + toks[1],
            };
        }
        var expHead = HeadOf(expSeg);
        var actHead = HeadOf(actSeg);
        return expHead == actHead ? $"{expHead} (@段{index})" : $"{expHead}|{actHead} (@段{index})";
    }

    /// <summary>快照段分隔符（逻辑层快照契约：实体段之间「 | 」）。</summary>
    private static readonly string[] Separator = { " | " };

    private static readonly char[] Whitespace = { ' ', '\t' };

    // ================= 报告模式 =================

    /// <summary>重放分歧报告 — 一次失败重放的完整取证材料。</summary>
    public sealed record Report
    {
        /// <summary>分歧点描述：起点快照 / 检查点（label + 步号）/ 命令返回不一致。</summary>
        public required string DivergencePoint { get; init; }

        /// <summary>分歧所在步（检查点步号；起点分歧即 StartTick；命令分歧即命令所在步）。</summary>
        public required long Tick { get; init; }

        /// <summary>字段级结构化 diff（快照分歧时非空；命令返回分歧时为空表）。</summary>
        public required IReadOnlyList<SegmentDiff> Diffs { get; init; }

        /// <summary>分歧点**之前**已注入的命令（含参数与 ok）——分叉命令大概率在此。</summary>
        public required IReadOnlyList<ReplayRunner.JournalEntry> PriorEntries { get; init; }

        /// <summary>
        /// 命令窗口起点（PriorEntries 的下标）：最后一个**通过**的检查点之后的第一条命令。
        /// 状态类分歧的嫌疑窗口 = PriorEntries[PriorWindowStart..]——更早的命令已被
        /// 通过的检查点背书（其效果已断言一致）。
        /// </summary>
        public int PriorWindowStart { get; init; }

        /// <summary>两侧完整快照（命令返回分歧时为 null——该分歧无快照材料）。</summary>
        public required string? ExpectedSnapshot { get; init; }

        public required string? ActualSnapshot { get; init; }

        /// <summary>脚本起点（== 0 才可 <see cref="Bisect"/>——风险条款）。</summary>
        public required long StartTick { get; init; }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== 分歧报告：{DivergencePoint} ===");
            if (Diffs.Count > 0)
            {
                sb.AppendLine("字段级 diff：");
                foreach (var d in Diffs.Take(8))
                    sb.Append("  ").AppendLine(d.ToString());
                if (Diffs.Count > 8)
                    sb.AppendLine($"  …（其余 {Diffs.Count - 8} 处省略）");
            }
            sb.AppendLine($"--- 分歧前 Journal（{PriorEntries.Count} 条，分叉命令大概率在此）---");
            foreach (var e in PriorEntries.Take(12))
                sb.Append("  tick=").Append(e.Tick).Append(' ').Append(e.Cmd)
                  .Append('(').Append(e.Args).Append(") ok=").AppendLine(e.Ok ? "true" : "false");
            if (PriorEntries.Count > 12)
                sb.AppendLine($"  …（其余 {PriorEntries.Count - 12} 条省略）");
            if (PriorWindowStart > 0)
                sb.AppendLine($"（前 {PriorWindowStart} 条已被通过的检查点背书——嫌疑窗口为其后 "
                    + $"{PriorEntries.Count - PriorWindowStart} 条）");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 报告模式重放 — 与 <see cref="ReplayRunner.Replay"/> 同一执行骨架
    /// （统一时间线 (步号, 录制序号) 稳定排序 + 起点对齐，逐字同构），
    /// 但断言失败**不抛即返**：记录首个分歧，产出 <see cref="Report"/>；
    /// 无分歧返回 null。命令 ok 不一致同样记为分歧（与本体同口径）。
    /// 首个分歧即停——取证器回答「第一个分叉在哪」，不是全量断言器。
    /// 检查点/起点断言与本体同档位（script.AssertLevel）：L2 语义档下
    /// 键集/同键值漂移不算分歧（否则「测试绿但取证器红」语义分裂）。
    /// </summary>
    public static Report? ReplayWithReport(ReplayRunner.ReplayScript script, ReplayRunner.Hooks hooks)
        => Walk(script, hooks, commandLimit: int.MaxValue);

    /// <summary>
    /// 统一时间线执行器（报告模式与二分探针共用）。
    /// 与 ReplayRunner.Replay 逐字同构：起点对齐（StartTick&gt;0）→ (步号, 录制序号) 稳定排序
    /// → 逐步注入/断言；差异仅在：①失败不抛即返（产报告）；②commandLimit 裁剪——
    /// 只注入前 commandLimit 条命令，遇到第 commandLimit 条即停（「只重放到裁剪点」）；
    /// 裁剪点**之前**遇到的检查点照常断言（其前命令已全部注入，断言语义有效）。
    /// </summary>
    private static Report? Walk(
        ReplayRunner.ReplayScript script, ReplayRunner.Hooks hooks, int commandLimit)
    {
        // 统一时间线：命令带序号，检查点不带——(步号, 录制序号) 稳定排序（勿动契约）
        var timeline = new List<(long Tick, int Seq, ReplayRunner.JournalEntry? Cmd, ReplayRunner.Checkpoint? Mark, int Ordinal)>();
        var ordinal = 0;
        foreach (var e in script.Entries) timeline.Add((e.Tick, e.Seq, e, null, ordinal++));
        foreach (var c in script.Checkpoints) timeline.Add((c.Tick, c.Seq, null, c, 0));
        timeline.Sort((a, b) => a.Tick != b.Tick
            ? a.Tick.CompareTo(b.Tick)
            : a.Seq.CompareTo(b.Seq));

        // 起点对齐（StartTick>0 脚本）：与本体同口径（含断言档位），起点快照分歧也产报告
        if (script.StartTick > 0)
        {
            hooks.AdvanceTo(script.StartTick);
            if (script.StartSnapshot != null && script.StartSnapshot.Length > 0)
            {
                var start = hooks.Snapshot();
                if (!CheckpointPasses(script.AssertLevel, script.StartSnapshot, start))
                    return MakeReport($"起点快照分歧（重放世界 ≠ 录制开录时的世界）"
                            + (script.AssertLevel == ReplayRunner.AssertLevel.Semantic ? "（语义档）" : ""),
                        script.StartTick, script.StartSnapshot, start, [], 0, script.StartTick);
            }
        }

        var prior = new List<ReplayRunner.JournalEntry>();
        var windowStart = 0; // 最后一个通过的检查点之后的第一条命令（嫌疑窗口起点）
        foreach (var step in timeline)
        {
            if (step.Tick > 0)
                hooks.AdvanceTo(step.Tick);

            if (step.Cmd != null)
            {
                if (step.Ordinal >= commandLimit)
                    break; // 只重放到裁剪点
                var e = step.Cmd;
                var ok = hooks.Inject(e.Cmd, e.Args);
                if (ok != e.Ok)
                    return MakeReport($"命令 {e.Cmd}({e.Args}) @tick={e.Tick} 返回与录制不一致"
                        + $"（录制 ok={e.Ok}，重放 ok={ok}）",
                        e.Tick, null, null, prior, windowStart: prior.Count, script.StartTick);
                prior.Add(e);
            }
            else if (step.Mark != null)
            {
                var cp = step.Mark;
                var snap = hooks.Snapshot();
                if (!CheckpointPasses(script.AssertLevel, cp.Snapshot, snap))
                    return MakeReport($"检查点「{cp.Label}」@tick={cp.Tick} 快照分歧"
                            + (script.AssertLevel == ReplayRunner.AssertLevel.Semantic ? "（语义档）" : ""),
                        cp.Tick, cp.Snapshot, snap, prior,
                        windowStart: WindowStart(prior, script.Checkpoints, cp),
                        script.StartTick);
                windowStart = prior.Count; // 该检查点通过——其前命令全部背书
            }
        }
        return null; // 无分歧
    }

    /// <summary>嫌疑窗口起点 = 通过链上最后一个检查点（Seq 小于失败者）之后的命令数。</summary>
    private static int WindowStart(
        List<ReplayRunner.JournalEntry> prior,
        IReadOnlyList<ReplayRunner.Checkpoint> checkpoints,
        ReplayRunner.Checkpoint failing)
    {
        // 通过的检查点 = Seq < 失败者 Seq 的最大者；其后第一条命令 = PriorEntries 下标。
        // 用 Seq 全序（与时间线同源）而非步号——同一步里先检查点后命令也能对齐。
        var lastPassedSeq = -1;
        foreach (var c in checkpoints)
        {
            if (c.Seq < failing.Seq && c.Seq > lastPassedSeq)
                lastPassedSeq = c.Seq;
        }
        // 录制端 Seq 全局单调 → prior 中 Seq ≤ lastPassedSeq 的命令都在失败检查点之前
        var idx = 0;
        while (idx < prior.Count && prior[idx].Seq <= lastPassedSeq)
            idx++;
        return idx;
    }

    private static Report MakeReport(
        string point, long tick,
        string? expectedSnapshot, string? actualSnapshot,
        IReadOnlyList<ReplayRunner.JournalEntry> prior, int windowStart, long startTick)
        => new()
        {
            DivergencePoint = point,
            Tick = tick,
            Diffs = expectedSnapshot != null && actualSnapshot != null
                ? DiffSegments(expectedSnapshot, actualSnapshot)
                : [],
            PriorEntries = prior,
            PriorWindowStart = windowStart,
            ExpectedSnapshot = expectedSnapshot,
            ActualSnapshot = actualSnapshot,
            StartTick = startTick,
        };

    /// <summary>
    /// 检查点怎么算通过（与本体 ReplayRunner 同口径——单一实现，不复制第二份）：
    /// L3 逐字 = 串相等；L2 语义 = 结构一致（<see cref="ReplayRunner.SemanticDiff"/> 空）。
    /// </summary>
    private static bool CheckpointPasses(ReplayRunner.AssertLevel level, string expected, string actual)
        => level switch
        {
            ReplayRunner.AssertLevel.Semantic => ReplayRunner.SemanticDiff(expected, actual).Count == 0,
            _ => expected == actual,
        };

    // ================= 一键二分 =================

    /// <summary>二分结果 — 第一个分歧 prefix 的定位与定性。</summary>
    public sealed record BisectResult
    {
        /// <summary>分歧已定位（false = 前提不成立/无可二分内容，Summary 有说明）。</summary>
        public required bool DivergenceFound { get; init; }

        /// <summary>定性："command-ok"（精确到命令）| "checkpoint"（收缩到命令窗口）| null（未定位）。</summary>
        public string? Kind { get; init; }

        /// <summary>分叉命令（Kind=="command-ok" 时非 null——精确 culprit）。</summary>
        public ReplayRunner.JournalEntry? FirstDivergingEntry { get; init; }

        /// <summary>该命令在 entries 中的下标（Kind=="command-ok"）。</summary>
        public int Index { get; init; }

        /// <summary>检查点定性时的嫌疑命令窗口（首个失败检查点之前、最后通过检查点之后）。</summary>
        public IReadOnlyList<ReplayRunner.JournalEntry> Window { get; init; } = [];

        /// <summary>检查点定性时的首个失败检查点（Kind=="checkpoint"）。</summary>
        public string? CheckpointLabel { get; init; }

        /// <summary>二分轮数（含整本复验与前缀 0 探针）。</summary>
        public int Rounds { get; init; }

        /// <summary>人读结论（CLI 直接打印）。</summary>
        public required string Summary { get; init; }
    }

    /// <summary>
    /// 一键二分 — 「每轮只重放到裁剪点」的对半裁剪：探针 k 只注入前 k 条命令、
    /// 只断言裁剪点之前遇到的检查点。「裁 k 是否分歧」关于 k 单调
    /// （裁剪点之前的检查点：其前命令集合与完整重放完全一致 → 同代码同轨迹），
    /// 二分收敛到第一个分歧 prefix k*。纯离线毫秒级一发，agent 零排程成本。
    /// 只认 StartTick==0 的脚本（风险条款）；先整本复验一次（无分歧 → 不硬二分）。
    /// </summary>
    /// <param name="hooksFactory">每轮新建世界并产出三钩子（调用方负责世界生命周期）。</param>
    public static BisectResult Bisect(
        ReplayRunner.ReplayScript script, Func<ReplayRunner.Hooks> hooksFactory)
    {
        if (script.StartTick != 0)
            throw new ArgumentException(
                $"二分只支持 StartTick==0 的脚本（本脚本 StartTick={script.StartTick}；"
                + "起点≠0 的裁剪语义微妙——风险条款，首版不启用）", nameof(script));

        // 整本复验（全量断言）：分歧前提不成立就不硬二分（避免拿旧材料空转）
        if (Walk(script, hooksFactory(), int.MaxValue) == null)
            return new BisectResult
            {
                DivergenceFound = false,
                Rounds = 1,
                Summary = "整本重放无分歧——分歧前提不成立（脚本/世界代码可能已修复）",
            };

        // 前缀 0 探针：只断言「任何命令之前」的检查点 —— 若已分歧，
        // 分叉不来自任何命令（种子/世界推进本身的确定性被破坏）
        var probe0 = Walk(script, hooksFactory(), 0);
        if (probe0 != null)
            return new BisectResult
            {
                DivergenceFound = true,
                Kind = "checkpoint",
                CheckpointLabel = probe0.DivergencePoint,
                Rounds = 2,
                Summary = $"前缀 0 条命令即分歧（{probe0.DivergencePoint}）——"
                          + "分叉不来自任何命令（检查种子/世界推进本身的确定性）",
            };

        // 对半裁剪：不变式——裁 hi 条必分歧、裁 lo 条必不分歧；收敛时 hi = lo+1
        var lo = 0;
        var hi = Math.Max(script.Entries.Count, 1);
        var rounds = 2;
        while (hi - lo > 1)
        {
            var mid = lo + (hi - lo) / 2;
            if (Walk(script, hooksFactory(), mid) != null) hi = mid;
            else lo = mid;
            rounds++;
        }

        // 确认轮：k* = hi 的报告定性（command-ok → 精确；checkpoint → 窗口）
        var rep = Walk(script, hooksFactory(), hi)
                  ?? throw new InvalidOperationException(
                      "二分不变式破坏：收敛 prefix 重放无分歧（单调性被违反——请报 bug）");
        rounds++;
        var window = rep.PriorEntries.Skip(rep.PriorWindowStart).ToList();

        if (rep.DivergencePoint.StartsWith("命令", StringComparison.Ordinal))
        {
            var culprit = script.Entries[hi - 1];
            return new BisectResult
            {
                DivergenceFound = true,
                Kind = "command-ok",
                FirstDivergingEntry = culprit,
                Index = hi - 1,
                Rounds = rounds,
                Summary = $"第一个分叉命令：entries[{hi - 1}] tick={culprit.Tick} "
                          + $"{culprit.Cmd}({culprit.Args}) ok={culprit.Ok}"
                          + $"（{rounds} 轮二分 / {script.Entries.Count} 条命令）",
            };
        }
        return new BisectResult
        {
            DivergenceFound = true,
            Kind = "checkpoint",
            CheckpointLabel = rep.DivergencePoint,
            Window = window,
            Index = hi - 1,
            Rounds = rounds,
            Summary = $"分叉定位到检查点前命令窗口（{rep.DivergencePoint}；"
                      + $"嫌疑窗口 {window.Count} 条："
                      + string.Join(" | ", window.Take(6).Select(e => $"{e.Tick}:{e.Cmd}")) + "…）"
                      + "（状态类分歧黑盒无法精确到单条命令——结合字段级 diff 在窗口内归因；"
                      + $"{rounds} 轮二分 / {script.Entries.Count} 条命令）",
        };
    }
}
