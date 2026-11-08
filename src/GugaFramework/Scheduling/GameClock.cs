#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DemoFactory.GugaFramework.Scheduling;

/// <summary>待触发事件表里的一行。</summary>
/// <param name="Id">调度时给的名字（没给名字就是空串）。</param>
/// <param name="RemainingTicks">还差几步触发。</param>
public readonly record struct ScheduledEvent(string Id, int RemainingTicks);

/// <summary>
/// 全局时钟（中台）— 接管所有时间推移，支持逻辑暂停。
///
/// 设计思路（docs/prototypes/各原型设计初始基准.md·表四：全局时间/时钟 — 独立步进循环 Standalone Tick）：
/// - 时间唯一来源是 Advance()：引擎帧率只负责"推"（表现层每帧调用），
///   游戏时间以"步"为单位离散推进，与帧率彻底解耦
/// - 逻辑暂停：IsPaused 时 Advance() 直接空转 — 即使表现层继续推帧，
///   游戏时间也完全冻结，暂停状态由逻辑层自持，不依赖引擎
/// - 定时事件：Schedule(delay, action) 在未来第 N 步触发，单点堆调度，
///   同一步内按调度顺序触发，保证确定性
/// - 防同一步级联：执行期间新调度的事件因 delay>=1 最早下一步生效，
///   结构上杜绝"事件触发事件"在同一步内无限递归
/// - 纯 C# 类，零引擎依赖；表现层用 Observable.EveryUpdate(GodotFrameProvider.*)
///   驱动 Advance() 即可获得完整时间轴能力
///
/// 架构位置：核心中台系统 — 全局时间/时钟
/// </summary>
public sealed class GameClock
{
    private sealed class TimedEvent
    {
        public string Id = "";
        public long DueTick;
        public ulong Seq;
        public required Action Action;
        public bool Cancelled;
    }

    /// <summary>待触发事件表里的一行。</summary>
    /// <param name="Id">调度时给的名字（没给名字就是空串）。</param>
    /// <param name="RemainingTicks">还差几步触发。</param>


    /// <summary>
    /// 可取消的定时事件句柄 — Dispose 即取消（已触发/已取消则无效）。
    /// </summary>
    public sealed class EventHandle : IDisposable
    {
        private readonly Action _cancel;
        internal EventHandle(Action cancel) => _cancel = cancel;
        public void Cancel() => _cancel();
        public void Dispose() => _cancel();
    }

    private readonly PriorityQueue<TimedEvent, (long DueTick, ulong Seq)> _future = new();
    private ulong _seq;

    /// <summary>当前步数（从 0 开始，随 Advance 递增）。</summary>
    public long Tick { get; private set; }

    /// <summary>逻辑暂停标记。</summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// 世界脉搏：每推进一步触发一次，先于该步的到期事件执行。
    /// 持续性系统（移动、每步结算）订阅脉搏做离散步进；
    /// 一次性时刻（刷兵、定时裁决）用 Schedule。两者共同构成时间轴。
    /// </summary>
    public event Action<long>? Pulse;

    /// <summary>逻辑暂停 — 之后所有 Advance 调用不推进时间、不触发事件。</summary>
    public void Pause() => IsPaused = true;

    /// <summary>解除逻辑暂停。</summary>
    public void Resume() => IsPaused = false;

    /// <summary>
    /// 调度一个延迟 delayTicks 步后执行的动作（delayTicks >= 1）。
    /// 返回可取消句柄。
    /// </summary>
    public EventHandle Schedule(int delayTicks, Action action) => Schedule("", delayTicks, action);

    /// <summary>
    /// 调度一个延迟 delayTicks 步后执行的动作，并给它一个<b>可报出来的名字</b>。
    ///
    /// <para><b>带名的那一档给存档用</b>：<see cref="Pending"/> 按调度次序念出
    /// （名字、还差几步），世界的存档那一半据此重建待触发事件表。
    /// 不带名的那些照样能念（名字是空串），只是念不出「这一条是哪件事」。</para>
    /// </summary>
    public EventHandle Schedule(string id, int delayTicks, Action action)
    {
        if (delayTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(delayTicks), "延迟必须>=1步（本步逻辑请直接调用）");
        }

        var e = new TimedEvent { Id = id ?? string.Empty, DueTick = Tick + delayTicks, Seq = _seq++, Action = action };
        _future.Enqueue(e, (e.DueTick, e.Seq));
        return new EventHandle(() => e.Cancelled = true);
    }

    /// <summary>
    /// 待触发事件表，按调度次序（同一步内先排的先念）。
    ///
    /// <para><b>时钟不恢复它自己</b>：排进去的是<b>闭包委托</b>，反射拿不到可识别的句柄，
    /// 所以复现它只有一条路——世界按这张表重排一遍自己的那一份。</para>
    /// </summary>
    public IReadOnlyList<ScheduledEvent> Pending()
    {
        var rows = new List<ScheduledEvent>();
        var ordered = _future.UnorderedItems
            .Select(p => p.Element)
            .OrderBy(e => e.DueTick)
            .ThenBy(e => e.Seq);
        foreach (var e in ordered)
        {
            if (!e.Cancelled) rows.Add(new ScheduledEvent(e.Id, (int)(e.DueTick - Tick)));
        }

        return rows;
    }

    /// <summary>
    /// 把步号恢复到 <paramref name="tick"/>（读档用）。时间推进仍然只走 <see cref="Advance"/>。
    ///
    /// <para><b>只许往前拨</b>：倒退步号会让已触发的定时事件永远不会再触发，
    /// 而那种世界跑起来不报错、只是「某批结算从此不再发生」。</para>
    /// </summary>
    public void RestoreTick(long tick)
    {
        if (tick < Tick)
            throw new ArgumentOutOfRangeException(nameof(tick),
                $"步号只能往前拨（现在 {Tick}，收到 {tick}）");

        Tick = tick;
    }

    /// <summary>
    /// 把步号拨回 <paramref name="tick"/> —— <b>只给「全量快照原样还原」那一档用</b>
    /// （巨兽竞技场的最终防守重试：失败后回到战前那一格，连随机推进一起作废）。
    ///
    /// <para><b>为什么与 <see cref="RestoreTick"/> 分开</b>：读档是换一局、步号只会往前走；
    /// 重试是同一局回到过去，必须让步号也退回去。两条语义混在一个口上，
    /// 「只许往前」那条守卫就会被一次重试需求悄悄放开。</para>
    ///
    /// <para><b>调用方负责</b>：所有以步号为绝对坐标的状态（定时到期、上次结算步号、
    /// 各子系统的 tick 游标）都要在同一发里按快照还原；本件只改时钟自己那一个数。
    /// 已排进 <c>_future</c> 的定时事件不受影响 —— 竞技场不排这一类事件。</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">步号为负。</exception>
    public void RewindTick(long tick)
    {
        if (tick < 0)
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "步号不能是负数");

        Tick = tick;
    }

    /// <summary>
    /// 推进 steps 步：每步先发脉搏（持续性系统步进），再触发到期事件。
    /// 完成标准：返回 true 表示时间真的前进了；暂停中返回 false 且什么都不做。
    /// </summary>
    public bool Advance(int steps = 1)
    {
        if (IsPaused || steps < 1) return false;
        for (var step = 0; step < steps; step++)
        {
            Tick++;
            Pulse?.Invoke(Tick);
            while (_future.TryPeek(out var e, out _) && e.DueTick <= Tick)
            {
                _future.Dequeue();
                if (!e.Cancelled) e.Action();
            }
        }
        return true;
    }
}
