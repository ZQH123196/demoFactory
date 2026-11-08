#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.GugaFramework.Vfx;

/// <summary>
/// 特效节点池的<b>账与读数</b> —— 演了几条、借不出几条、池里几条 / 总量。
///
/// <para>存活粒子数这本件也有一格（<see cref="AliveParticles"/>），但<b>不念进读数</b>：
/// 本轮没有调用方，恒为 0，而恒为 0 会被读成实测结论。
///
/// <para><b>为什么容量从 <see cref="CueFeedConfig.PendingPerTrack"/> 推导</b>（spec D11）：
/// 表现层的积压上限与逻辑层那条待播上限是同一个数，多设一个参数就会出现「逻辑认为满了、
/// 画面认为还有空位」这种没法从任一侧看出来的漂。构造参数可覆盖，是给场景侧那一格
/// <c>[Export]</c> 接线用的（现场能调是「压测必恢复」的前提）。</para>
///
/// <para><b>池空则丢弃并计数</b>（spec D10）：没演这件事必须读得出来，与 <c>CueFeed</c>
/// 丢 cue 时记 <c>Dropped</c> 同理。不选「临时新建」——永不丢演出，代价是峰值时节点数失控。</para>
///
/// <para>本件<b>只记账，不持有节点</b>：借哪一条、怎么定位、怎么演，都在吃引擎的那半
/// （<c>VfxPlayer</c>，留 04/05）。纯 C# 这半因此不碰任何 Godot 类型，离线可断言。</para>
///
/// <para><b>不进真值</b>：粒子节点、存活数、池占用都在真值下游，不进检查点快照、
/// 不进级 1 与级 2 的逐字基线（spec 第 3 节）。</para>
/// </summary>
public sealed class VfxLedger
{
    private readonly int _capacity;

    /// <param name="config">容量从这份背压配置的 <see cref="CueFeedConfig.PendingPerTrack"/> 推导；不给取那份缺省值。</param>
    /// <param name="capacity">显式覆盖容量（场景侧 <c>[Export]</c> 走这一格）。不给则按 <paramref name="config"/> 推导。</param>
    /// <exception cref="ArgumentException">容量小于 1。</exception>
    public VfxLedger(CueFeedConfig? config = null, int? capacity = null)
    {
        _capacity = capacity ?? (config ?? CueFeedConfig.Default).PendingPerTrack;
        if (_capacity < 1)
        {
            throw new ArgumentException(
                $"容量至少 1，收到 {_capacity.ToString(CultureInfo.InvariantCulture)}"
                + "（兜 0 = 一条都借不出，且账上念「在池 0/0」，看着像正常状态）");
        }

        InPool = _capacity;   // 新开一局时池是满的：节点在开局就都借出来了备用
    }

    /// <summary>池的总量（借出加池里恒等于它）。</summary>
    public int Capacity => _capacity;

    /// <summary>池里现在有几条空闲的。</summary>
    public int InPool { get; private set; }

    /// <summary>当前存活几条（借出去还没还的）。</summary>
    public int Alive { get; private set; }

    /// <summary>真演出去的条数（借不出的那条不算演）。</summary>
    public long Played { get; private set; }

    /// <summary>池空借不出的次数。读数里「借不出」那一段就是它。</summary>
    public long BorrowFailed { get; private set; }

    /// <summary>归还的条数。恒等式：<see cref="Played"/> == <see cref="Alive"/> + <see cref="Returned"/>。</summary>
    public long Returned { get; private set; }

    /// <summary>
    /// 当前存活粒子数 —— 由吃引擎的那半报进来（纯 C# 这半数不了粒子）。
    /// 报进来的最新值。
    ///
    /// <para><b>本轮没有调用方，所以恒为 0</b>，而 <see cref="Readout"/> 因此不念它。
    /// 恒为 0 的那一格会被读成实测结论（「真没粒子」），而它恒为 0 的真因是
    /// 「没被赋过值」——「日志里没有」≠「没发生」（契约「三条铁律」第三条那一口气）。
    /// 这个属性与 <see cref="ReportAliveParticles"/> 都留着，等真值接上直接用。</para>
    /// </summary>
    public long AliveParticles { get; private set; }

    /// <summary>借一条。返回 <c>false</c> = 池空借不出（已计入 <see cref="BorrowFailed"/>，当场丢掉，不排队）。</summary>
    public bool Borrow()
    {
        if (InPool <= 0)
        {
            BorrowFailed++;
            return false;
        }

        InPool--;
        Alive++;
        Played++;
        return true;
    }

    /// <summary>还一条回池。</summary>
    /// <exception cref="InvalidOperationException">没借出过就还 —— 账三行会对不上（池里凭空多条）。</exception>
    public void Return()
    {
        if (Alive <= 0)
        {
            throw new InvalidOperationException(
                "没借出过就归还：多还了一次。池里会凭空多条，演账也对不上。");
        }

        Alive--;
        Returned++;
        InPool++;
    }

    /// <summary>
    /// 吃引擎的那半每帧报一次存活粒子数（spec D15 本轮只加这一条读数）。
    ///
    /// <para><b>本轮没有调用方</b>：<c>VfxPlayer</c> 一次也没调它，所以
    /// <see cref="AliveParticles"/> 恒为 0，<see cref="Readout"/> 因此也不念那一段。
    /// 留着这一格是因为「谁去数粒子」那件事还没定形，而它一旦定下来，
    /// 接上真值要改的只有调用点与 <see cref="Readout"/> 的最后一段。</para>
    /// </summary>
    public void ReportAliveParticles(long count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "存活粒子数不会是负的");
        }

        AliveParticles = count;
    }

    /// <summary>
    /// 一行读数：<c>vfx 演 N｜借不出 M｜在池 K/T</c>（spec D10 的那三段）。
    /// 格式化只此一处，HUD 与调试桥读的是同一份。
    ///
    /// <para><b>存活粒子数不念</b>（见 <see cref="AliveParticles"/>）：那一格本轮没有调用方，
    /// 恒为 0，而恒为 0 会被读成「实测结论是零粒子」。少念一格好过念一格假读数。</para>
    /// </summary>
    public string Readout()
        => $"vfx 演 {Played.ToString(CultureInfo.InvariantCulture)}"
           + $"｜借不出 {BorrowFailed.ToString(CultureInfo.InvariantCulture)}"
           + $"｜在池 {InPool.ToString(CultureInfo.InvariantCulture)}/{_capacity.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>换一局：账归零、池是满的。</summary>
    public void Reset()
    {
        InPool = _capacity;
        Alive = 0;
        Played = 0;
        BorrowFailed = 0;
        Returned = 0;
        AliveParticles = 0;
    }
}
