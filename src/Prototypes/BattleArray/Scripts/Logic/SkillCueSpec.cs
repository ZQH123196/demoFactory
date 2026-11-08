#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 排兵布阵这一侧的 <b>cue 说明书</b>（薄 spec）—— 把技能层发的<b>事件</b>与<b>状态读数</b>
/// 翻成公共层派发台认得的那四类。
///
/// 分工照 0.3 节那条两层分法：<b>机制在公共层，内容在这里</b>。
/// <see cref="CueFeed"/> 管「同轨怎么合并、挤爆了丢哪条、丢了怎么读得出来」；
/// 这里只管三件事——<b>哪条事件算哪一类、挂在哪个兵哪条轨、念成哪几个字</b>。
/// 换一款原型（比如巨兽竞技场）要改的是这张说明书，不是那台子。
///
/// 三条边界：
/// 1. <b>只读观测面</b>：输入是事件流与派生读数，一个真值都不写（「表现层只读」）；A73 扫这一条。
/// 2. <b>不进真值</b>：cue 与 <c>dropped=</c> 计数都不进快照、不进级 1/级 2 的逐字基线 ——
///    它在真值下游，这是已删的设计档「丢帧合法」的前提。
/// 3. <b>轨名是这台原型自己的口径</b>（<c>hit</c> / <c>slow</c> / <c>stacks</c> …），
///    公共层不认识它们，也不许往公共层里长（A72）。
/// </summary>
public sealed class SkillCueSpec
{
    /// <summary>一次性：命中那一刀（飘伤害数字 + 闪白）。轨 = 每个兵一条。</summary>
    public const string HitTrack = "hit";

    /// <summary>一次性：按下了但没放成（念得出为什么没放——D2 那三问里「为什么没放」的画面版）。</summary>
    public const string RejectedTrack = "rejected";

    /// <summary>带身相的持续轨前缀：同一效果在同一个人身上只占一条轨（<c>effect:fx.slam_slow</c>）。</summary>
    public const string EffectTrackPrefix = "effect:";

    /// <summary>计数轨：叠层与 tag 计数各一条，数字变了才重画。</summary>
    public const string StackTrack = "stacks";
    public const string TagTrack = "tags";

    /// <summary>带参数轨：形状查询把半径与角度念出来（前摇期在地上画一根指向线的那个参数）。</summary>
    public const string ShapeTrack = "shape";

    /// <summary>
    /// 「在路上」那一格（<see cref="ShapeTrack"/> 的第二个载体）：登记待决结算时排一根落点指示，
    /// 落地或作废时把它灭掉。清单页 <c>archer.pierce</c> 的形状是「commit → 6 步后落地」，
    /// 画面上要看得见那 6 秒之内的账 —— 已删的验证报告欠的那句「延迟结算看起来像不像一回事」就在这条轨上。
    /// </summary>
    public const string FlightTrack = "flight";

    /// <summary>
    /// 哪些<b>效果</b>有身相（挂上就该在画面上看得见）。名字与文字都只写在这一处：
    /// 层里加一条新效果而这里没挂号，画面就不动它 —— 这是选择，不是漏。
    /// </summary>
    private static readonly Dictionary<string, string> EffectLabels = new(StringComparer.Ordinal)
    {
        [BattleAbilities.GuardWallEffectId] = "盾墙",
        [BattleAbilities.SlamSlowEffectId] = "减速",
    };

    /// <summary>哪些 <b>tag</b> 要在画面上念计数（同一条 tag 多来源叠出来的计数正是「动态 cue」那一格）。</summary>
    private static readonly Dictionary<string, string> TagLabels = new(StringComparer.Ordinal)
    {
        [BattleAbilities.GuardTagId] = "守卫",
    };

    /// <summary>
    /// 这一步的<b>状态读数</b>：某个兵此刻的 tag 计数与效果层数。由宿主（H3 的
    /// <see cref="BattleAbilities"/>、离线断言里的假宿主）现读现交 —— 本件不缓存引用。
    /// </summary>
    public sealed record BodyRead(string UnitId, IReadOnlyDictionary<string, int> TagCounts,
        IReadOnlyDictionary<string, int> EffectStacks);

    /// <summary>
    /// <b>每渲染帧问一次，每逻辑步只翻一次</b>：新账（行为流涨了）或步号变了才翻译，否则直接退。
    /// 这条规矩只准活在这一处 —— 表现件照着调即可，别在界面侧再判一遍「这一帧要不要翻」，
    /// 否则「合并」计数会刷成帧率的函数，<c>dropped=</c> 那行就读不出真积压了（「时间基准」 的同一件事）。
    /// </summary>
    public void OnStep(CueFeed feed, BattleWorld world)
    {
        if (world.Abilities is not { } layer) return;      // 全关档：一个 cue 都不发，也不该发
        var total = layer.EventTotal;
        var grew = total > _eventCursor;
        if (world.Tick == _lastTick && !grew) return;

        var fresh = layer.EventsTail((int)(total - _eventCursor));
        _eventCursor = total;
        _lastTick = world.Tick;

        var bodies = world.Units.Select(unit => new BodyRead(
            unit.Id, layer.TagsOf(unit.Id), layer.EffectStacksOf(unit.Id))).ToList();
        Step(feed, world.Tick, fresh, bodies);
    }

    /// <summary>换一局：游标归零，别让下一局把上一局的行为流重演一遍。</summary>
    public void Reset()
    {
        _eventCursor = 0;
        _lastTick = long.MinValue;
    }

    private long _eventCursor;
    private long _lastTick = long.MinValue;

    /// <summary>
    /// 把「这一步新增的事件 + 这一步的状态读数」翻译成派发台里的动作。
    /// 返回这一步<b>新起了几条</b>一次性 cue（给断言与读数用，不代表画面演了几条 —— 演几条看 <see cref="CueFeed.Drain"/>）。
    /// 离线核对直接喂这一发（不起世界也能判）；真局走 <see cref="OnStep"/>。
    /// </summary>
    public int Step(CueFeed feed, long tick, IReadOnlyList<AbilityEvent> newEvents, IReadOnlyList<BodyRead> bodies)
    {
        var started = 0;
        foreach (var ev in newEvents)
        {
            switch (ev.Payload)
            {
                case DamageDealt damage:
                    // 伤害数字直接读形状字段（Final），不再从文本里抠第一段。
                    feed.PushOneShot(ev.Tick, ev.Subject, HitTrack, $"-{AbilityNumbers.Format(damage.Final)}");
                    started++;
                    break;
                case AbilityRejected rejected:
                    feed.PushOneShot(ev.Tick, ev.Subject, RejectedTrack, rejected.Reason.ToString());
                    started++;
                    break;
                case AbilityRejectedAndQueued queued:
                    feed.PushOneShot(ev.Tick, ev.Subject, RejectedTrack, queued.Reason.ToString());
                    started++;
                    break;
                case ShapeHitLanded shape:
                    // 形状查询的参数随字段带（GAS 的 cue 参数那一格）：半径与角度在地上画得出来。
                    feed.PushDescriptive(ev.Tick, ev.Subject, ShapeTrack, ShapeLabel(shape.ConeDegrees), shape.Radius);
                    break;
                case ShapeHitMissed missed:
                    // 打空也念得出参数（旧文本 hits=- 那一档在形状里自立成类）。
                    feed.PushDescriptive(ev.Tick, ev.Subject, ShapeTrack, ShapeLabel(missed.ConeDegrees), missed.Radius);
                    break;
                case SettlementRegistered pending:
                    // 登记即上屏：那一发还在路上，画面上就该有「第几步落地」这一格（值 = 落地步号）。
                    feed.PushDescriptive(ev.Tick, ev.Subject, FlightTrack, LandingLabel(pending.LandingTick),
                        pending.LandingTick);
                    break;
                case SettlementRegisteredWithMove moved:
                    feed.PushDescriptive(ev.Tick, ev.Subject, FlightTrack, LandingLabel(moved.LandingTick),
                        moved.LandingTick);
                    break;
                case EffectLanded:
                    // 落地：把「在路上」那一格灭掉（值 0 = 撤下）。
                    feed.PushDescriptive(ev.Tick, ev.Subject, FlightTrack, "落地", 0);
                    break;
                case EffectMissed:
                    // 作废：打空也要读得出为什么空，所以两种都走撤下这一发。
                    feed.PushDescriptive(ev.Tick, ev.Subject, FlightTrack, "落空：目标已不在", 0);
                    break;
            }
        }

        // 持续档与计数档不看事件、看状态：身上了就亮、撤了就不亮，数字变了才重画。
        // 同一步内多条命令改同一个状态也不会多演一次 —— 这正是派发台「只播最新状态」那一档。
        foreach (var body in bodies)
        {
            foreach (var (effectId, stacks) in body.EffectStacks)
            {
                if (!EffectLabels.TryGetValue(effectId, out var label)) continue;
                feed.PushSustained(tick, body.UnitId, EffectTrackPrefix + effectId, label, stacks > 0);
            }

            // 层数那一格：同一条效果叠到 2 层以上才念（1 层就是「挂上了」，持续档已经报过）
            var deepest = body.EffectStacks
                .Where(pair => pair.Value > 1
                               && (EffectLabels.ContainsKey(pair.Key) || StackLabels.ContainsKey(pair.Key)))
                .OrderByDescending(pair => pair.Value)
                .FirstOrDefault();
            if (deepest.Value > 1)
            {
                feed.PushCounted(tick, body.UnitId, StackTrack,
                    $"{StackLabel(deepest.Key)}×{deepest.Value.ToString(CultureInfo.InvariantCulture)}",
                    deepest.Value);
            }

            foreach (var (tag, count) in body.TagCounts)
            {
                if (!TagLabels.TryGetValue(tag, out var tagLabel)) continue;
                feed.PushCounted(tick, body.UnitId, TagTrack, $"{tagLabel}×{count.ToString(CultureInfo.InvariantCulture)}", count);
            }
        }

        return started;
    }

    /// <summary>哪些效果在画面上要念层数（叠层类的才念；减速那种单层的不占这一格）。</summary>
    private static readonly Dictionary<string, string> StackLabels = new(StringComparer.Ordinal)
    {
        [BattleAbilities.ChecklistProbe.WoundEffectId] = "伤",
    };

    private static string StackLabel(string effectId)
        => StackLabels.TryGetValue(effectId, out var label) ? label : effectId;

    // === 要念上屏的字：全部从形状字段现取，不再解析 detail 文本（档位与措辞留原型侧） ===

    private static string ShapeLabel(double coneDegrees)
        => $"范围 {AbilityNumbers.Format(coneDegrees)}°";

    /// <summary>落地那一步是第几步（读 SettlementRegistered / SettlementRegisteredWithMove 的 LandingTick 字段）。</summary>
    private static string LandingLabel(long landingTick)
        => $"在路上 · 第 {landingTick} 步落地";
}
