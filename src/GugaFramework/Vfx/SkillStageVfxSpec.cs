#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;

namespace DemoFactory.GugaFramework.Vfx;

/// <summary>
/// <b>技能试验场这一侧的 cue 说明书（薄 spec）</b>——把技能层发的<b>事件</b>翻成公共层派发台
/// （<see cref="CueFeed"/>）认得的那四类，外加一张「轨名 → 效果 <c>res://</c> 路径」薄表
/// （spec D6：这张表由宿主递，公共层不认识任何具体效果名）。
///
/// <para><b>为什么是两份而不是一份</b>：本件<b>纯 C#、零引擎类型</b>，所以「哪条事件算哪一档、
/// 挂在哪条轨」这一层能 <c>dotnet test</c> 秒级断；吃引擎的那一半（借节点、摆位、演）
/// 是 <see cref="SkillStageVfxCues"/>。分法照最早那份宿主接线
/// （那份说明书纯 C#、那份吃引擎的接线，两半一一对应），
/// 机制在公共层、内容在宿主侧，同一个口径。</para>
///
/// <para><b>本场接哪一种 <c>CueFeed</c> 形状</b>：接，而且走<b>游标增量</b>那一档
/// （<see cref="SkillStage.EventsSinceCursor{T}"/>）。各原型的形状并不相同——有的接了派发台
/// 并在自己那份说明书里手搓一个 <c>_eventCursor</c>，有的<b>根本不接派发台</b>，只按
/// <c>DamageDealt</c> 抽出手方触发一个回调。场子这一侧选接，理由三条：
/// <list type="number">
/// <item><b>出口已经是结构化的</b>：<c>EventsSinceCursor&lt;T&gt;()</c> 除了给新增的那几条，
/// 还给 <c>DroppedSinceCursor</c>（游标之后发过、已被环形挤掉的那几条条数）。手搓游标拿不到这一格，
/// 于是「这一段有事件读不出来」这件事没处读——而「日志里没有」≠「没发生」
/// （契约「三条铁律」第三条）。本件把它累加进 <see cref="UnreadableEvents"/>。</item>
/// <item><b>四档分派只有派发台认</b>：四类 cue 的合并与背压（同轨同值不重发、同轨挤爆丢最旧）
/// 全在派发台里。不接它就得在这一侧另写一份，而另写一份的代价就是那份会漂。</item>
/// <item><b>快放那一下确实会积压</b>：<c>training.strike</c> 冷却 30 步而前摇只有 3 步，
/// 连着按会排队；同一条轨上短时间多条一次性 cue 正是派发台那条丢弃策略的用武之地。</item>
/// </list></para>
///
/// <para><b>本层不判「这一步要不要翻」</b>（有一份判了，理由记在它的 <c>OnStep</c> 头上）：
/// 那边的不翻是为了别让<b>状态</b>读数把合并计数刷成帧率的函数，而它的持续与计数两档确实按状态推。
/// 本件四档全部<b>只从事件推</b>，游标保证同一条事件只翻一次，所以按帧调与按步调逐字相同——
/// 多抄一份「步号变了没有」的判断就多一处会漂的地方，这里不抄。</para>
///
/// <para><b>轨名与效果路径都是这张表的内容</b>，公共层两样都不认识（spec D6）。表是静态的：
/// 每一档一条轨（一次性两条），因为效果路径要能一张表列全（<see cref="VfxSpec"/> 收的是一份静态表，
/// 不是逐轨回调）。<b>代价写在明处</b>：同一个单位身上同时挂两种有身相的效果时只剩最后那一档——
/// 那是这张表的容量，不是派发台的合并策略；真要同时挂两种，轨名要分开
/// （与宿主侧那条 <c>effect:</c> 前缀同一做法）。</para>
///
/// <para><b>不进真值</b>：cue 与本件那几格计数都在真值下游，不进检查点快照、不进逐字基线。</para>
/// </summary>
public sealed class SkillStageVfxSpec
{
    // === 轨名（本场自己的口径，与任何原型不同名）===

    /// <summary>一次性：命中那一刀。事件行的主体是<b>挨打那个</b>，所以这一轨挂在被打中的单位身上。</summary>
    public const string HitTrack = "hit";

    /// <summary>一次性：按下了没放成。与 <see cref="HitTrack"/> 共用那份效果资源（公共批里只有一份一次性档的）。</summary>
    public const string RejectedTrack = "rejected";

    /// <summary>持续：有身相的效果挂上就在、撤下就不在。场子这一侧一条（见类注那一笔容量账）。</summary>
    public const string GuardTrack = "guard";

    /// <summary>计数：层数 / tag 计数那一个整数。画面只跟着这个数走。</summary>
    public const string StacksTrack = "stacks";

    /// <summary>带参数：一发扇形的半径（米）。值当<b>倍数</b>落在发射器节点的大小上。</summary>
    public const string ShapeTrack = "shape";

    /// <summary>
    /// 轨名 → 效果 <c>res://</c> 路径那张薄表（spec D6 的那张表）。<b>按 <c>res://</c> 引用，不按 uid</b>
    /// （spec D3）。四份资源在公共批 <c>src/Shared/vfx/</c>，四档各配一份：
    /// 一次性要 <c>one_shot = true</c> 的（演一遍靠 <c>finished</c> 收槽位），
    /// 持续要 <c>one_shot = false</c> 的（循环发射，槽位由显式撤下时收）——
    /// 对不上当场被 VFX 层拒并推警告，所以这张表与资源那一格必须成对改。
    /// </summary>
    private static readonly Dictionary<string, string> TrackEffects = new(StringComparer.Ordinal)
    {
        [HitTrack] = "res://src/Shared/vfx/FxHit.tscn",
        [RejectedTrack] = "res://src/Shared/vfx/FxHit.tscn",
        [GuardTrack] = "res://src/Shared/vfx/FxGuardWall.tscn",
        [StacksTrack] = "res://src/Shared/vfx/FxStackMark.tscn",
        [ShapeTrack] = "res://src/Shared/vfx/FxShapeRing.tscn",
    };

    /// <summary>
    /// 轨名 → 四档里的哪一档（spec D5：与 <see cref="CueKind"/> 一一对齐，不另起一套分类轴）。
    /// <b>与 <see cref="TrackEffects"/> 的键集逐字相同</b>——两表分开写是为了一个只查档位、
    /// 一个只查路径；离线有用例钉住两表键集相等（少一条就是「那条轨查得到档位却查不到效果」）。
    /// </summary>
    private static readonly Dictionary<string, CueKind> TrackKinds = new(StringComparer.Ordinal)
    {
        [HitTrack] = CueKind.OneShot,
        [RejectedTrack] = CueKind.OneShot,
        [GuardTrack] = CueKind.Sustained,
        [StacksTrack] = CueKind.Counted,
        [ShapeTrack] = CueKind.Descriptive,
    };

    /// <summary>轨名 → 效果路径那份表（给 <see cref="VfxSpec"/> 构造用；这一份是只读的）。</summary>
    public static IReadOnlyDictionary<string, string> Effects => TrackEffects;

    /// <summary>在册的轨有几条（读数那一格）。</summary>
    public static int TrackCount => TrackEffects.Count;

    /// <summary>
    /// 查一条轨是四档里的哪一档。返回 <c>false</c> = 没这条轨，
    /// 此时不兜任何一档（兜一档就是「拼错轨名当成别的轨演了一遍」）。
    /// </summary>
    public static bool TryGetKind(string track, out CueKind kind)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (TrackKinds.TryGetValue(track, out var found))
        {
            kind = found;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>
    /// 「游标之后发过、已被环形挤掉」的累计条数——<b>这一件读不出来的那些事件</b>。
    /// 每一次 <see cref="OnStep"/> 把四类游标的 <c>DroppedSinceCursor</c> 累加进来。
    /// 它不为 0 就说明<b>有事件发生过但这一件没演</b>，而事件流里读不到它们
    /// （被挤掉的那几条连形状都没了，问不出是哪一类）。
    /// </summary>
    public long UnreadableEvents { get; private set; }

    /// <summary>换一局：那条计数归零（游标在逻辑壳那一侧，本件不持）。</summary>
    public void Reset() => UnreadableEvents = 0;

    /// <summary>
    /// <b>每渲染帧问一次</b>：把这一步新增的四类事件翻成派发台里的动作。
    /// <b>游标增量</b>（<see cref="SkillStage.EventsSinceCursor{T}"/>）保证同一条事件只翻一次，
    /// 所以按帧调与按步调逐字相同（本件不判「这一步要不要翻」，理由见类注）。
    /// </summary>
    /// <param name="feed">派发台（同轨怎么合并、挤爆丢哪条、丢了怎么读得出来，那份机制在公共层）。</param>
    /// <param name="stage">逻辑壳那一侧：读事件走它（场子的坐标与朝向也走它）。</param>
    public void OnStep(CueFeed feed, SkillStage stage)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(stage);

        // 一次性 · 命中：事件行的主体是挨打那个（层里那一发是 Emit(target.UnitId, …)），
        // 所以这一刀落在被打中的单位脚下 —— 「打在谁身上」在画面上认得出是谁。
        var hits = stage.EventsSinceCursor<DamageDealt>();
        UnreadableEvents += hits.DroppedSinceCursor;
        foreach (var ev in hits.Events)
        {
            if (ev.Payload is not DamageDealt damage) continue;
            feed.PushOneShot(ev.Tick, ev.Subject, HitTrack, DamageLabel(damage));
        }

        // 一次性 · 没放成：主体是按下的那个，理由照事件形状里的字段念（不从文本反解析）。
        var rejected = stage.EventsSinceCursor<AbilityRejected>();
        UnreadableEvents += rejected.DroppedSinceCursor;
        foreach (var ev in rejected.Events)
        {
            if (ev.Payload is not AbilityRejected why) continue;
            feed.PushOneShot(ev.Tick, ev.Subject, RejectedTrack, why.Reason.ToString());
        }

        // 持续 · 有身相的效果在身 / 撤下。挂上、撤下、到期三处都发一条，
        // 于是「挂上了却没人来撤」这件事在派发台的待播轨数与 VFX 层的挂上条数两处都读得出。
        var applied = stage.EventsSinceCursor<EffectApplied>();
        UnreadableEvents += applied.DroppedSinceCursor;
        foreach (var ev in applied.Events)
        {
            if (ev.Payload is EffectApplied effect) feed.PushSustained(ev.Tick, ev.Subject, GuardTrack, effect.EffectId, true);
        }

        var removed = stage.EventsSinceCursor<EffectRemoved>();
        UnreadableEvents += removed.DroppedSinceCursor;
        foreach (var ev in removed.Events)
        {
            if (ev.Payload is EffectRemoved effect) feed.PushSustained(ev.Tick, ev.Subject, GuardTrack, effect.EffectId, false);
        }

        var expired = stage.EventsSinceCursor<EffectExpired>();
        UnreadableEvents += expired.DroppedSinceCursor;
        foreach (var ev in expired.Events)
        {
            if (ev.Payload is EffectExpired effect) feed.PushSustained(ev.Tick, ev.Subject, GuardTrack, effect.EffectId, false);
        }

        // 计数：层数变了才重画（派发台那一档负责「数没变不发」，这一件只负责把新数递进去）。
        var stacks = stage.EventsSinceCursor<EffectStacksChanged>();
        UnreadableEvents += stacks.DroppedSinceCursor;
        foreach (var ev in stacks.Events)
        {
            if (ev.Payload is not EffectStacksChanged count) continue;
            feed.PushCounted(ev.Tick, ev.Subject, StacksTrack,
                $"{count.EffectId} x{count.Stacks.ToString(CultureInfo.InvariantCulture)}", count.Stacks);
        }

        // 带参数：一发扇形的半径。打空那一条也发（打空要读得出半径与角度，画面上照样画那个圈）。
        var landed = stage.EventsSinceCursor<ShapeHitLanded>();
        UnreadableEvents += landed.DroppedSinceCursor;
        foreach (var ev in landed.Events)
        {
            if (ev.Payload is ShapeHitLanded shape)
            {
                feed.PushDescriptive(ev.Tick, ev.Subject, ShapeTrack, ShapeLabel(shape), shape.Radius);
            }
        }

        var missed = stage.EventsSinceCursor<ShapeHitMissed>();
        UnreadableEvents += missed.DroppedSinceCursor;
        foreach (var ev in missed.Events)
        {
            if (ev.Payload is ShapeHitMissed shape)
            {
                feed.PushDescriptive(ev.Tick, ev.Subject, ShapeTrack, ShapeLabel(shape), shape.Radius);
            }
        }
    }

    /// <summary>命中那一刀的标签：伤害按事件形状里的 <c>Final</c> 现取，不从文本反解析。</summary>
    private static string DamageLabel(DamageDealt damage) => AbilityNumbers.Format(damage.Final);

    /// <summary>形状那一档的标签：半径与全角都照事件形状里的字段现取。</summary>
    private static string ShapeLabel(ShapeHitLanded shape)
        => $"r{AbilityNumbers.Format(shape.Radius)} cone{AbilityNumbers.Format(shape.ConeDegrees)}";

    /// <summary>打空那一条的标签（半径与全角同形）。</summary>
    private static string ShapeLabel(ShapeHitMissed shape)
        => $"r{AbilityNumbers.Format(shape.Radius)} cone{AbilityNumbers.Format(shape.ConeDegrees)}";
}
