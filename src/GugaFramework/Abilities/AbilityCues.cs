#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Abilities;

/// <summary>
/// 四类 cue 的档位（对着 GAS 的 GameplayCue 四类，落法见已删的设计档「GameplayCue」那一行）。
///
/// 名字只回答「这一条在画面上怎么存在」，不回答「是哪条技能」——技能名与 tag 名由原型侧的
/// spec 递进来（A72 扫的就是这条边界）。
/// </summary>
public enum CueKind
{
    /// <summary>一次性：按下就演一下，演完不留状态（命中闪白、飘伤害数字）。同一轨可以连着来几条。</summary>
    OneShot,

    /// <summary>持续：效果在身上这段一直亮着，只在「上身 / 下地」两个时刻各改一次画面（减速带、护盾底色）。</summary>
    Sustained,

    /// <summary>计数：画面只跟着一个整数走，数没变就不重画（叠层数、tag 计数）。</summary>
    Counted,

    /// <summary>带参数：画面由表里那几个标量决定，参数没变就不重画（范围半径、扇形角度、进度）。</summary>
    Descriptive,
}

/// <summary>
/// 一条要上屏的 cue。它<b>不是真值</b>——真值在层里，这条只是「同一份真值在画面上的那一次表达」。
/// </summary>
/// <param name="Tick">这一条产生于第几步（换算口径：步 ÷ 10 = 秒）。</param>
/// <param name="Kind">四档里的哪一档。</param>
/// <param name="UnitId">挂在哪个兵身上。</param>
/// <param name="Track">哪一条轨——<b>同轨才会互相合并与挤位</b>，不同轨各走各的（轨名由原型侧给）。</param>
/// <param name="Label">念出来的那几个字（飘字、角标、参数文字都用它）。</param>
/// <param name="Value">档位相关的那个标量：Counted = 计数，Sustained = 1 在身 / 0 撤下，Descriptive = 参数。</param>
public sealed record CueRow(long Tick, CueKind Kind, string UnitId, string Track, string Label, double Value);

/// <summary>背压策略：一条轨攒几条待播、「丢了哪条」留几行读数。</summary>
public sealed class CueFeedConfig
{
    /// <summary>同一条轨最多攒几条待播的一次性 cue。超出就丢最旧的那条并计入 <see cref="CueFeed.Dropped"/>。</summary>
    public int PendingPerTrack { get; init; } = 2;

    /// <summary>读数里留几行「丢了哪条」（有界，别跟着局长涨——照 RollingLog 那条有界缓冲的做法）。</summary>
    public int DroppedTailKept { get; init; } = 8;

    public static CueFeedConfig Default { get; } = new();
}

/// <summary>
/// cue 派发台 —— <b>表现层积压这件事的唯一一份实现</b>（P4 后半收的那块，已删的设计档）。
///
/// 为什么这半份在公共层而不是各原型的表现件里：
/// 「逻辑不阻塞、同一单位两个 clip 抢一条轨道」是这套执行模型的<b>设计后果</b>，不是某个玩法的
/// 特殊情况；每个接了技能层的原型都会遇到，而它带的那条要求（<b>丢了哪条要读得出来</b>）
/// 是「状态可查」那一条在表现层的翻版。上一轮刚实测过同一形状的另一半：分发口抄两份必然漂
/// （N-22 问的那道口、N-24 词表口），所以台子与计数只留一份，各宿主只声明「哪条事件算哪类 cue、
/// 挂在哪条轨、念成什么字」——那本 spec 在原型侧。
///
/// 三条策略，都读得出（<see cref="Readout"/>）：
/// 1. <b>丢弃</b>只发生在一次性那一档：同轨待播挤爆了，丢最旧的一条 → <see cref="Dropped"/>；
/// 2. <b>合并</b>发生在其余三档：同轨同值再推一次 = 不发第二遍，同轨换了值 = 新值顶掉还没播出去的旧值
///    → <see cref="Merged"/>。这就是设计文档说的「只播最新状态」。
///    <b>比的是上一次发出去的那条，不是「这一步排过的」</b>——那份记忆必须活过 <see cref="Drain"/>，
///    否则每步都把同一个身相当新账重报一遍（第八轮出图抓到「演 815｜合并 0」那种假账）；
/// 3. <b>丢了哪条要读得出来</b>：<see cref="DroppedLines"/> 留一段有界尾巴，
///    <see cref="Readout"/> 那一行念得出 <c>dropped=</c>。
///
/// 台子自己不发事件、不读真值、也不认识任何技能名：事件→cue 的翻译在原型侧，
/// 表现层拿到的只是 <see cref="Drain"/> 出来的那几条。<b>它不写任何真值</b>（「表现层只读」 在这层的落点）。
/// </summary>
public sealed class CueFeed
{
    private readonly CueFeedConfig _config;

    /// <summary>一次性 cue 的待播队列，按轨分开（键 = 单位/轨）。</summary>
    private readonly Dictionary<string, List<CueRow>> _pending = new(StringComparer.Ordinal);

    /// <summary>这一步排出来、等着被 <see cref="Drain"/> 取走的「最新状态」槽位。</summary>
    private readonly Dictionary<string, CueRow> _latest = new(StringComparer.Ordinal);

    /// <summary>
    /// 每条状态轨<b>上一次发出去的是什么</b> —— 这份记忆必须活过 <see cref="Drain"/>。
    /// 少了它，「同值不重发」就只在一步之内成立：每步都把同一个身相当新账重报一遍，
    /// 于是读数长成「演 815｜合并 0」这种假账（第八轮出图抓到的就是它）。
    /// </summary>
    private readonly Dictionary<string, CueRow> _sent = new(StringComparer.Ordinal);

    private readonly List<string> _droppedTail = [];

    public CueFeed(CueFeedConfig? config = null)
    {
        _config = config ?? CueFeedConfig.Default;
        if (_config.PendingPerTrack < 1)
            throw new ArgumentException($"PendingPerTrack 至少 1，收到 {_config.PendingPerTrack}（兜 0 = 每条一次性 cue 都算挤爆）");
    }

    /// <summary>真演出去的条数（被合并掉的不算演，被丢掉的也不算）。</summary>
    public long Emitted { get; private set; }

    /// <summary>合并掉了多少条：同轨同值重复推、或同轨旧值被新值顶掉。</summary>
    public long Merged { get; private set; }

    /// <summary>丢掉了多少条一次性 cue（轨挤爆、丢最旧）。读数里那一行 <c>dropped=</c> 就是它。</summary>
    public long Dropped { get; private set; }

    /// <summary>还欠着几条待播的状态（<b>不是</b>「画面上正亮着几条」—— 那种话念出来就是假读数）。</summary>
    public int PendingTracks => _latest.Count;

    /// <summary>一次性：演一下就没了。返回 true = 这条进了待播；被挤进来的那条最旧的会记进 <see cref="Dropped"/>。</summary>
    public bool PushOneShot(long tick, string unitId, string track, string label, double value = 0)
    {
        var key = Key(unitId, track);
        if (!_pending.TryGetValue(key, out var queue))
        {
            queue = [];
            _pending[key] = queue;
        }

        queue.Add(new CueRow(tick, CueKind.OneShot, unitId, track, label, value));
        while (queue.Count > _config.PendingPerTrack)
        {
            var oldest = queue[0];
            queue.RemoveAt(0);
            Dropped++;
            RecordDropped(oldest);
        }

        return true;
    }

    /// <summary>持续档：在身 true / 撤下 false。同值再推 = 合并掉，不发第二遍。</summary>
    public bool PushSustained(long tick, string unitId, string track, string label, bool active)
        => PushState(tick, CueKind.Sustained, unitId, track, label, active ? 1 : 0);

    /// <summary>计数档：数没变不发（叠层与 tag 计数都归这一档）。</summary>
    public bool PushCounted(long tick, string unitId, string track, string label, int count)
        => PushState(tick, CueKind.Counted, unitId, track, label, count);

    /// <summary>带参数档：参数没变不发（半径、角度、进度）。</summary>
    public bool PushDescriptive(long tick, string unitId, string track, string label, double value)
        => PushState(tick, CueKind.Descriptive, unitId, track, label, value);

    /// <summary>
    /// 表现层每渲染帧取走一次：返回<b>确定序</b>的待播条（先按步号、同一步按 单位/轨 的序数比较），
    /// 取完即清空。<b>确定序是硬要求</b>——同种子重跑要逐字全等，字典序会跟着哈希种子漂。
    /// </summary>
    public IReadOnlyList<CueRow> Drain()
    {
        var rows = new List<CueRow>();
        foreach (var queue in _pending.Values)
        {
            rows.AddRange(queue);
        }

        _pending.Clear();
        rows.AddRange(_latest.Values);
        _latest.Clear();

        rows.Sort((a, b) =>
        {
            if (a.Tick != b.Tick) return a.Tick.CompareTo(b.Tick);
            var byUnit = string.CompareOrdinal(a.UnitId, b.UnitId);
            if (byUnit != 0) return byUnit;
            var byTrack = string.CompareOrdinal(a.Track, b.Track);
            if (byTrack != 0) return byTrack;
            return ((int)a.Kind).CompareTo((int)b.Kind);
        });

        Emitted += rows.Count;
        return rows;
    }

    /// <summary>有没有等着上屏的（表现件用它判「这一帧要不要动」）。</summary>
    public bool HasPending => _pending.Count > 0 || _latest.Count > 0;

    /// <summary>「丢了哪条」那段有界尾巴（最新在后）。空 = 一条没丢过，画面没欠账。</summary>
    public IReadOnlyList<string> DroppedLines => _droppedTail;

    /// <summary>
    /// 一行读数：<c>cue 演 n｜合并 n｜dropped=n｜待播 n</c>（<b>短版，给 HUD</b>）。
    /// 已删的设计档要的就是这一行 —— 「为什么画面跟不上」不许只靠人眼撞。
    /// </summary>
    public string ReadoutBrief()
        => $"cue 演 {Emitted.ToString(CultureInfo.InvariantCulture)}" +
           $"｜合并 {Merged.ToString(CultureInfo.InvariantCulture)}" +
           $"｜dropped={Dropped.ToString(CultureInfo.InvariantCulture)}" +
           $"｜待播 {PendingTracks.ToString(CultureInfo.InvariantCulture)} 轨";

    /// <summary>
    /// 长版读数（桥的 <c>cues</c> 动词用它）：短版那一行 + 「丢了哪条」那段有界尾巴。
    /// 尾巴不进 HUD —— 一行念成三行会把底部那一片字全压住（第八轮出图抓到的）。
    /// </summary>
    public string Readout()
    {
        var text = ReadoutBrief();
        return _droppedTail.Count == 0
            ? text
            : $"{text}｜最近丢弃: {string.Join(" ; ", _droppedTail)}";
    }

    /// <summary>换一局（重开直达口、演示档重跑都要它）：队列、计数与「发过什么」那份记忆一起归零。</summary>
    public void Reset()
    {
        _pending.Clear();
        _latest.Clear();
        _sent.Clear();
        _droppedTail.Clear();
        Emitted = 0;
        Merged = 0;
        Dropped = 0;
    }

    private bool PushState(long tick, CueKind kind, string unitId, string track, string label, double value)
    {
        var key = Key(unitId, track);
        var row = new CueRow(tick, kind, unitId, track, label, value);
        if (_sent.TryGetValue(key, out var sent) && SameState(sent, kind, label, value))
        {
            Merged++;     // 与上一次<b>发出去</b>的那条一模一样：不是新账，不发第二遍
            return false;
        }

        if (_latest.ContainsKey(key))
        {
            Merged++;     // 这一轨上一步排的还没播出去就被顶掉了 = 合并一条，不是丢一条
        }

        _sent[key] = row;
        _latest[key] = row;
        return true;
    }

    private static bool SameState(CueRow last, CueKind kind, string label, double value)
        => last.Kind == kind && last.Value.Equals(value) && string.Equals(last.Label, label, StringComparison.Ordinal);

    private void RecordDropped(CueRow row)
    {
        _droppedTail.Add($"{row.Tick} {row.UnitId}/{row.Track} {row.Label}");
        while (_droppedTail.Count > _config.DroppedTailKept)
        {
            _droppedTail.RemoveAt(0);
        }
    }

    private static string Key(string unitId, string track) => $"{unitId}/{track}";
}
