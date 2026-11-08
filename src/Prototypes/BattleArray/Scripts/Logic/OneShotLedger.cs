#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 一次性动作的台账 —— 「这一局到底演了哪几条动作、各几次、有没有被吞掉」的一句话读数。
///
/// <b>这条账让哪一步变便宜</b>（N-30）：表现层接上起手动作之后，「技能放了没、画面交代没交代」
/// 原来只有两半证据 —— 胶卷图证明 clip 摆得出那个姿势、触发点与施法读条读同一个 <c>CastingOf</c>，
/// 但一局跑完读不出「突斩那 0.7 秒真演了没有」。有了这一格，那一问变成<b>一发只读调用</b>
/// （桥 <c>windups</c>），反证也变成一行：把 <c>PlayOneShot</c> 注释掉 ⇒ 读数归零。
/// 同形状的第二发顺手把 N-27 也量出来了：平A 的出手被「一次性动作没演完不许换」那道守卫吞掉几次，
/// 以前只能靠人眼盯，现在是 <c>被吞</c> 那个数。
///
/// 三条边界：
/// 1. <b>只读下游</b>：记的是表现层「演没演」这件事，一个真值都不写（「表现层只读」）；
///    不进 <c>DebugSnapshot()</c>、不进级 1/级 2 的逐字基线 —— 与 cue 与 <c>dropped=</c> 同一档。
/// 2. <b>纯 C#</b>：不碰 <c>Node</c>，所以离线可测（<c>Tests/AbilityOneShotLedgerTests.cs</c> 的 A78），
///    真接线那一半由 A78 扫源码钉住。
/// 3. <b>读数可机读且确定</b>：clip 名按序数排列、不含时间与随机量，同一条记录序列重跑逐字全等。
/// </summary>
public sealed class OneShotLedger
{
    /// <summary>每条 clip 留最近多少个「发生在第几步」—— 再多就把读数撑长，定位用这一串够了。</summary>
    public const int RecentTicksKept = 8;

    private sealed class Entry
    {
        public int Played;
        public int Dropped;
        public int Absent;
        public readonly List<long> RecentPlayed = new();
    }

    private readonly Dictionary<string, Entry> _byClip = new(StringComparer.Ordinal);

    /// <summary>真演出去的一次性动作总数（其余两类都不算）。</summary>
    public int TotalPlayed { get; private set; }

    /// <summary>想演却没演成 —— 当时正有一条一次性动作没演完（N-27 那笔账的运行时半边）。</summary>
    public int TotalDropped { get; private set; }

    /// <summary>想演而模型里没那条 clip（登记表与 `.glb` 漂了 —— 这一类必须与「被吞」分开念）。</summary>
    public int TotalAbsent { get; private set; }

    /// <summary>演出去一次：记 clip 名与发生在第几步（口径 = 逻辑步，「时间基准」；换算秒 = 步 ÷ 10）。</summary>
    public void RecordPlayed(string clip, long tick)
    {
        if (clip.Length == 0) return;
        var entry = Touch(clip);
        entry.Played++;
        TotalPlayed++;
        entry.RecentPlayed.Add(tick);
        if (entry.RecentPlayed.Count > RecentTicksKept) entry.RecentPlayed.RemoveAt(0);
    }

    /// <summary>被吞：想演的时候正有一条一次性动作没演完。只记次数，要的是「吞了几次」这个量。</summary>
    public void RecordDropped(string clip)
    {
        if (clip.Length == 0) return;
        Touch(clip).Dropped++;
        TotalDropped++;
    }

    /// <summary>模型里没登记这条 clip（或这件模型压根没有动画播放器）。</summary>
    public void RecordAbsent(string clip)
    {
        if (clip.Length == 0) return;
        Touch(clip).Absent++;
        TotalAbsent++;
    }

    /// <summary>换一局：整本台账归零，别让下一局读到上一局的演出。</summary>
    public void Reset()
    {
        _byClip.Clear();
        TotalPlayed = 0;
        TotalDropped = 0;
        TotalAbsent = 0;
    }

    /// <summary>这条 clip 演过几次（核对与桥都读这一发，别各自数一遍）。</summary>
    public int PlayedTimes(string clip) => _byClip.TryGetValue(clip, out var e) ? e.Played : 0;

    /// <summary>这条 clip 被吞过几次。</summary>
    public int DroppedTimes(string clip) => _byClip.TryGetValue(clip, out var e) ? e.Dropped : 0;

    /// <summary>这条 clip 因为「模型里没这条动作」被记了几次。</summary>
    public int AbsentTimes(string clip) => _byClip.TryGetValue(clip, out var e) ? e.Absent : 0;

    /// <summary>演过的 clip 名（按名字序，读数与核对都取这一份）。</summary>
    public IReadOnlyList<string> Clips => _byClip.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>
    /// 一句话读数：三类各一个总数，再逐条 clip 念「演 / 步号 / 被吞 / 没这条动作」。
    /// 一条都没演过时念 <c>一次性动作 演 0 次</c>（念得出来「没有」，不是空字）。
    /// </summary>
    public string Readout()
    {
        var sb = new StringBuilder();
        sb.Append($"一次性动作 演 {TotalPlayed} 次 · 被吞 {TotalDropped} · 模型没这条 {TotalAbsent}");
        foreach (var clip in Clips)
        {
            var e = _byClip[clip];
            sb.Append($"　| {clip} 演 {e.Played}");
            if (e.RecentPlayed.Count > 0)
                sb.Append($"(第 {string.Join("/", e.RecentPlayed)} 步)");
            if (e.Dropped > 0) sb.Append($" 被吞 {e.Dropped}");
            if (e.Absent > 0) sb.Append($" 模型没这条 {e.Absent}");
        }
        return sb.ToString();
    }

    private Entry Touch(string clip)
    {
        if (!_byClip.TryGetValue(clip, out var e))
        {
            e = new Entry();
            _byClip[clip] = e;
        }
        return e;
    }
}
