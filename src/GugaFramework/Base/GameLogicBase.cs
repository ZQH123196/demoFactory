#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.GugaFramework.Base;

/// <summary>
/// 纯 C# 逻辑类的基类。
///
/// 设计思路：
/// - 纯 C# 类，严禁继承 Node，严禁操作 UI 或碰撞体
/// - 子类自己持有订阅，在 <see cref="Dispose"/> 里成对退订——接上时 <c>+=</c>、离场时 <c>-=</c>，
///   同一处代码里看得见（「离树交还」 是同一件事：离场主动交还，别在调用点反复查）
/// - 状态保持朴素可快照：能用步号比较表达的（<c>ExpiresAtTick &lt;= Tick</c>）就别包成订阅，
///   否则「为什么这一帧没结算」要去翻订阅链，而不是读一个字段
///
/// 架构位置：纯数据逻辑层 (Pure C#)
/// </summary>
public abstract class GameLogicBase : IDisposable
{
    /// <summary>订阅 + 成对退订登记在一条表里 —— 接上与交还一步做完。</summary>
    private readonly List<Action> _detachers = new();

    private bool _disposed;

    /// <summary>造一个世界：时钟与装配（走位流水线 / 建造模块 / 地形接线三件）都在这儿起头。</summary>
    protected GameLogicBase()
    {
        Clock = new GameClock();
        Assembly = new WorldAssembly(Clock);
        Assembly.FrozenProbe = () => TimeFrozen;
    }

    /// <summary>逻辑时钟 —— 时间可控那个观测口的实体。</summary>
    public GameClock Clock { get; }

    /// <summary>五步装配收在它身上。</summary>
    protected WorldAssembly Assembly { get; }

    /// <summary>当前步数（只读）。</summary>
    public long Tick => Clock.Tick;

    /// <summary>逻辑暂停标记。</summary>
    public bool IsPaused => Clock.IsPaused;

    /// <summary>
    /// 时间轴是否已被局结束冻住。
    ///
    /// <para><b>冻结与暂停是两件事</b>：暂停可解冻（玩家按一下继续），
    /// 冻结是这一局的结局（结算屏上的「再来一局」走的是换世界，不是解冻）。
    /// 所以真值单独放一个字段，不复用 <see cref="IsPaused"/>——
    /// 复用的话读数会把「打完了一局」念成「玩家按了暂停」，而
    /// 「哪一格是暂停、哪一格是结局」正是要分清的那件事。</para>
    ///
    /// <para><b>真值只有这一处</b>：要翻回「局结束不冻结」改这一行加上
    /// <see cref="FreezeTime"/> 的调用点，不必逐个原型改回去。</para>
    /// </summary>
    public bool TimeFrozen { get; private set; }

    /// <summary>
    /// 冻住时间轴（局结束时由宿主调一次）。<b>不设反向口</b>：
    /// 解冻不是「这一局还没结束」，是换了一局；换局走换世界，那条路上本来就是一个新对象。
    /// </summary>
    public void FreezeTime() => TimeFrozen = true;

    /// <summary>逻辑暂停。</summary>
    public void Pause() => Clock.Pause();

    /// <summary>解除逻辑暂停。</summary>
    public void Resume() => Clock.Resume();

    /// <summary>暂停 / 恢复的单一入口。<b>可重写</b>：有的世界在某个阶段不接受暂停，
    /// 子类重写它加那一道守卫 —— 非虚的话子类那个只是遮蔽，两个入口会各做各的。</summary>
    public virtual void SetPaused(bool paused)
    {
        if (paused) Pause();
        else Resume();
    }

    /// <summary>
    /// 推进若干步。<b>可重写</b>：有的世界要在装配那三件事之外再加自己那几段。
    ///
    /// <para><b>冻结那一格不在这里判，在装配那一侧</b>：重写本方法的宿主里有的直接调
    /// <c>Assembly.Advance</c>，绕过本类的判断，所以闸开在
    /// <see cref="WorldAssembly.FrozenProbe"/> 那一处 —— 三条推进的路都从那里过。
    /// 重写而不接装配的世界自己判（暂停与冻结都在方法开头那一次判断里）；
    /// <see cref="AdvanceTo"/> 以本方法为一步，因此那条路自动跟着重写那份走。</para>
    /// </summary>
    public virtual bool Advance(int steps = 1) => Assembly.Advance(steps);

    /// <summary>
    /// 分块推进到目标步号（重放与快进共用的那一条路）。
    ///
    /// <para><b>循环只留这一份，以虚 <see cref="Advance"/> 为一步</b>：所以不接三层、
    /// 只推时钟的世界也走这一条路，不必在原型侧再手写一份推进循环
    /// （那是「三处推进循环归零」要收编掉的形状）。逐个原型的 <c>Setup</c> 只许转发
    /// <c>world.AdvanceTo(...)</c>，不许自己写 <c>while (world.Tick &lt; targetTick)</c>。</para>
    ///
    /// <para>冻结与暂停之后本方法自然推不动：<see cref="Advance"/> 一步都不走，
    /// 循环随即退出（两者对推进的效果一样，退出即收手，不死循环）。</para>
    ///
    /// <para><paramref name="chunkTicks"/> 必须至少 1：分成 0 步的循环一步也不走，
    /// 而那副样子与「已到目标步」在读数上分不开。</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">分块步数小于 1。</exception>
    public void AdvanceTo(long targetTick, int chunkTicks = 250)
    {
        if (chunkTicks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkTicks), chunkTicks, "分块步数必须 >= 1 —— 分成 0 步的循环一步也不走。");
        }

        while (Tick < targetTick)
        {
            var remaining = targetTick - Tick;
            var chunk = remaining < chunkTicks ? (int)remaining : chunkTicks;
            if (!Advance(chunk))
            {
                return;   // 暂停中或已冻结：步号不动，退出
            }
        }
    }

    /// <summary>订阅时钟脉搏并登记退订 —— 两件事一步做完，不给「接上了忘了交还」的缝。</summary>
    protected void SubscribePulse(Action<long> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Clock.Pulse += handler;
        _detachers.Add(() => Clock.Pulse -= handler);
    }

    /// <summary>登记一条成对退订（非脉搏那一类的：事件出口、模块句柄）。</summary>
    protected void TrackDetach(Action detacher)
    {
        ArgumentNullException.ThrowIfNull(detacher);
        _detachers.Add(detacher);
    }

    /// <summary>这张退订表里登记了几条。<b>不含</b>装配自己挂的那两处（那两处归 <see cref="Dispose"/> 那一发的返回值）。</summary>
    public int SubscriptionCount => _detachers.Count;

    /// <summary>这个世界放过了没有。</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// 释放并清理资源。幂等（重复调直接返回，不重复交还）。
    /// 次序是：先跑退订表（后进先出），再交还装配挂的那两处，最后 <see cref="OnDispose"/>。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (var i = _detachers.Count - 1; i >= 0; i--)
        {
            _detachers[i]();
        }

        _detachers.Clear();
        Assembly.Detach();
        OnDispose();
    }

    /// <summary>
    /// 子类重写以退订自己的订阅（如 <c>_clock.Pulse -= Step</c>）。
    /// </summary>
    protected virtual void OnDispose() { }
}
