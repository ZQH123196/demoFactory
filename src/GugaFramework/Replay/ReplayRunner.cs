#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DemoFactory.GugaFramework.Replay;

/// <summary>
/// 重放脚本基建 — 「第几步做了什么」的通用数据形态与重放执行器。
///
/// 分工（调试基建计划，2026-09-23 收编）：
/// - 本文件只管**通用层**：脚本数据（journal + 检查点）、按步注入的执行骨架、
///   快照断言；不含任何游戏知识（命令词表、快照字段由原型通过委托注入）。
/// - 原型侧提供三件事：①命令注入器（cmd+args → 世界写路径，返回 ok）
///   ②快照序列化器（世界 → 单行状态串）③推进器（推进到目标步）。
/// - 红线：这里只认 {tick, cmd, args, ok} 与检查点快照——相机/表现层严禁进脚本，
///   由录制端保证；本层无从感知引擎类型，零 Godot 依赖。
///
/// 断言强度（已确认）：只比检查点快照（状态级）；事件流逐字相等不做。
/// 断言分层（计划 Phase 4.1）：脚本头部 <see cref="ReplayScript.AssertLevel"/> 选档——
/// L3 逐字（默认，存量脚本零迁移）| L2 语义（结构必须一致，键集/同键值漂移容忍——
/// 良性改动「加字段/调数值」时开发者显式降档，避免全脚本全红逼 agent 怕测试）。
/// L1（不变量，如守恒）是测试资产层的既有断言（如 EconomyConservationTests），
/// 不进重放脚本头部——重放只管 L2/L3。
/// </summary>
public static class ReplayRunner
{
    /// <summary>一条命令记录 — 命令发出即录（在逻辑写路径，不在 UI 输入录）。</summary>
    public sealed record JournalEntry(long Tick, string Cmd, string Args, bool Ok, int Seq = 0);

    /// <summary>一个检查点 — 到点抓的全量快照（状态级，重放的断言单位）。</summary>
    public sealed record Checkpoint(long Tick, string Label, string Snapshot, int Seq = 0);

    /// <summary>
    /// 检查点断言档位（计划 Phase 4.1）—「快照逐字」从唯一口径降为末档。
    /// L3 = 逐字（默认，存量脚本零迁移）；L2 = 语义（结构必须一致，键集/同键值漂移容忍）；
    /// L1 = 不变量断言，属测试资产层（如守恒测试），不进脚本头部。
    /// </summary>
    public enum AssertLevel
    {
        /// <summary>L1 — 不变量层（守恒等）：由原型测试资产承担，脚本头部不表达。</summary>
        Invariants = 1,

        /// <summary>L2 — 语义层：结构（段数/段首 token/步号）必须一致；键集与同键值漂移容忍。</summary>
        Semantic = 2,

        /// <summary>L3 — 逐字层（默认）：快照串逐字相等（现状口径，存量脚本零迁移）。</summary>
        Literal = 3,
    }

    /// <summary>
    /// 重放脚本的中性数据形态（录制端产出、重放端消费，序列化互为逆操作）。
    /// Seq：**全局发生顺序**（命令与检查点共用一个计数器，录制端按发生顺序编）；
    /// 重放端 (步号, 录制序号) 稳定排序后执行 —— 同一步内「先命令后检查点」与
    /// 「先检查点后命令」都能逐字还原（各列表独立编号会在同一步撞号，排序不稳定）。
    /// </summary>
    public sealed record ReplayScript(
        int Seed,
        List<JournalEntry> Entries,
        List<Checkpoint> Checkpoints,
        long StartTick = 0,
        string? StartSnapshot = null,
        AssertLevel AssertLevel = AssertLevel.Literal);

    /// <summary>原型侧注入的三个世界钩子 — 全是纯 C# 委托，本层不认得任何具体世界。</summary>
    public sealed class Hooks
    {
        /// <summary>命令注入：把 {cmd, args} 打到世界写路径上，返回实际 ok（与录制时必须一致）。</summary>
        public required Func<string, string, bool> Inject { get; init; }

        /// <summary>快照：当前世界状态序列化为单行串（录制/重放两端必须同一份代码）。</summary>
        public required Func<string> Snapshot { get; init; }

        /// <summary>推进：把世界推进到目标步（终局闩锁/暂停空转由实现方自兜，不会死循环）。</summary>
        public required Action<long> AdvanceTo { get; init; }
    }

    /// <summary>
    /// 重放一条脚本：同种子 new 世界（调用方建好）→ 按 (步号, 录制序号) 时间线注入命令 /
    /// 断言检查点快照，逐字复现录制时序。
    ///
    /// 时序契约（统一时间线后由 Seq 保证，勿动）：录制端按发生顺序编 Seq；重放端
    /// (步号, 录制序号) 稳定排序后逐步执行 —— 「同一步内先命令后检查点」与
    /// 「先检查点后命令」都能还原。旧双游标只认步号不认顺序，MCP 场景
    /// （命令与 Mark 在同一步、命令在先）会先断言后注入，快照系统性差一笔。
    ///
    /// StartTick：MCP 试玩录制的脚本从运行中的世界接着录（&gt;0），重放端先推进到该 tick
    /// 并断言 StartSnapshot（录制端开录时抓的起点快照），之后才开始按时间线执行；
    /// 手写与测试内录制的脚本为 0（从开局录，无起点快照）。
    ///
    /// <b>本方法只是一层壳：驱动逻辑在 <see cref="LiveReplaySession"/>，级 2（引擎内按条喂）跑的
    /// 也是那一份。</b>这里曾经自带一份时间线遍历，两条通道各一份就会漂成两套实现，
    /// 于是「级 2 与级 1 逐字相等」测的变成了两个实现像不像 —— 已收成一处。
    /// 断言消息的调用次序保持原样（每条命令、每个检查点各一发，等与不等都发），
    /// 公共层自己的重放测试就是这次收编的回归网。
    /// </summary>
    public static void Replay(ReplayScript script, Hooks hooks, Action<string?> assertEqual)
    {
        var session = new LiveReplaySession(script, hooks);
        session.RunAll(assertEqual);
    }

    internal static string? SemanticStartMsg(string expected, string actual, long tick)
    {
        var diffs = SemanticDiff(expected, actual);
        return diffs.Count == 0
            ? null
            : $"起点快照语义分歧 @tick={tick}（结构变化，重放世界 ≠ 录制开录时的世界）\n"
              + string.Join("\n", diffs.Take(8).Select(d => "  " + d));
    }

    /// <summary>
    /// L2 语义断言（检查点快照）— 结构必须一致，键集/同键值漂移容忍。
    ///
    /// 「结构」= ①头部段首 token（tick=N——身份漂移即分歧）②实体段数
    /// ③每段段首两个 token（实体身份 = 类型+Id；段序由录制端 Id 序数排序契约保证）。
    /// 「容忍（不失败）」= 同段内 key 集合变化（加/减字段）与同键值漂移（调数值）——
    /// 良性改动不逼全脚本全红（真相仍可从 DivergenceReport.Diff 全量读出）。
    /// 命令 ok 翻转不走此路径（行为分歧，任何档位都是分歧）。
    ///
    /// 零游戏知识：按「 | 」切段、段内按空白切 token、实体身份 = 段首两个 token——
    /// 本层不认得任何字段名，段格式的语义由快照序列化端自兜（同 DivergenceReport 契约）。
    /// </summary>
    internal static string? SemanticCheckpointMsg(Checkpoint cp, string actual)
    {
        var diffs = SemanticDiff(cp.Snapshot, actual);
        return diffs.Count == 0
            ? null
            : $"检查点「{cp.Label}」@tick={cp.Tick} 语义分歧（结构变化，重放世界 ≠ 录制时点的世界）\n"
              + string.Join("\n", diffs.Take(8).Select(d => "  " + d));
    }

    /// <summary>
    /// 语义 diff — 结构分歧返回非空表（即 L2 失败）；键集/同键值漂移属容忍项**不进返回表**。
    /// ①头部段首 token（`tick=N`）相等才算结构一致（步号漂移 = 身份漂移）；
    /// ②实体段数相等；③每段段首两个 token（类型+Id）相等。
    /// </summary>
    internal static List<string> SemanticDiff(string expected, string actual)
    {
        var structural = new List<string>();
        // ①头部段首 token（tick=N）——身份漂移即结构分歧
        var expSegs = expected.Split(SegSeparator, StringSplitOptions.None);
        var actSegs = actual.Split(SegSeparator, StringSplitOptions.None);
        var expTick = FirstToken(expSegs[0]);
        var actTick = FirstToken(actSegs[0]);
        if (expTick != actTick)
            structural.Add($"头部步号漂移（`tick` 键）：录制「{expTick}」 ≠ 重放「{actTick}」");
        // ②实体段数
        if (expSegs.Length != actSegs.Length)
            structural.Add($"实体段数：录制 {expSegs.Length} ≠ 重放 {actSegs.Length}");
        // ③段首身份（类型+Id）——头部段（下标 0）只有步号是身份，已在 ① 检查；
        //   头部段的其余 token（treasury/net/outcome 等值域）属容忍项，不做身份比较
        var n = Math.Min(expSegs.Length, actSegs.Length);
        for (var i = 1; i < n; i++)
        {
            var expHead = HeadTokens(expSegs[i]);
            var actHead = HeadTokens(actSegs[i]);
            if (!expHead.SequenceEqual(actHead))
                structural.Add($"段{i} 身份漂移：录制「{string.Join(' ', expHead)}」 ≠ 重放「{string.Join(' ', actHead)}」");
        }
        return structural;
    }

    private static string FirstToken(string seg) =>
        seg.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } t ? t[0] : "";

    /// <summary>段首两个 token（实体身份 = 类型+Id；头部段即「tick=N」单 token）。</summary>
    private static string[] HeadTokens(string seg)
    {
        var t = seg.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries);
        return t.Length <= 2 ? t : t[..2];
    }

    /// <summary>快照段分隔符（逻辑层快照契约：实体段之间「 | 」）。</summary>
    private static readonly string[] SegSeparator = { " | " };

    private static readonly char[] TokenSeparator = { ' ', '\t' };

    /// <summary>快照首处差异定位（可观测性第一）：找出第一个分歧字符，各给两侧 160 字符上下文。</summary>
    internal static class ReplayRunnerDiff
    {
        public static string Hint(string expected, string actual)
        {
            var n = Math.Min(expected.Length, actual.Length);
            var i = 0;
            while (i < n && expected[i] == actual[i]) i++;
            var from = Math.Max(0, i - 50);
            var take = 160;
            var exp = expected.Substring(from, Math.Min(take, expected.Length - from));
            var act = actual.Substring(from, Math.Min(take, actual.Length - from));
            return $" 首处差异 @字符{i}（录制长{expected.Length}/重放长{actual.Length}）"
                 + $" 录制…\"{exp}\" 重放…\"{act}\"";
        }
    }
}
