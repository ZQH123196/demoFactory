#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.ObjectPool;

/// <summary>
/// 对象池 —— 一件反复借还的东西，造一次，用很多次（spec D1）。
///
/// <para><b>池子答什么</b>：池里还有几个、借出一个、归还一个。
/// <b>池子不答什么</b>：借出去的那个东西该长什么样、该重置成什么样、满池时怎么销毁
/// —— 三样全是构造时递进来的委托，池子一个都不读。</para>
///
/// <para><b>形状借的是 Unity 内置 <c>UnityEngine.Pool.ObjectPool&lt;T&gt;</c></b>：
/// 构造五个委托（<c>createFunc</c> / <c>actionOnGet</c> / <c>actionOnRelease</c> /
/// <c>actionOnDestroy</c> / <c>collectionCheck</c>）+ 两个容量数。那是社区通行的抄本，
/// 微软 <c>Microsoft.Extensions.ObjectPool</c> 与各引擎的池子形状一致，换语言不用重学。</para>
///
/// <para><b>池空怎么办：现造。</b>另一条路是池空丢弃并计数，<c>VfxLedger</c> 那一档走的是它
/// （特效峰值时宁可丢演出也不要节点数失控）。两条路不能混 —— 池子不知道业务上「丢一次演出」
/// 意味着什么，那是调用点的事，所以<b>要丢弃就在 <c>createFunc</c> 里返回 null</b>
/// （见 <see cref="Get"/>）。</para>
///
/// <para><b>本件是纯 C#</b>（不碰任何 Godot 类型），因此能链进 <c>src/GugaFramework/Tests/</c>
/// 被 <c>dotnet test</c> 直接测 —— 这是它做成泛型而不是 Node 专用的唯一理由：
/// 节点专用那一版要在五处 csproj 点名排除，<b>于是永远离线测不到</b>
/// （代价记在 <c>Tests/GugaFramework.Tests.csproj</c> 头注里，那条排除面五处同形、漏一处报 CS0246）。</para>
///
/// <para><b>不进真值</b>：池里几个、借出几个都不进检查点快照、不进逐字基线 ——
/// 与「特效不进真值」同一口径，它们在真值下游。</para>
///
/// <para><b>接入（表现层，五行）</b>：
/// <code>
/// var pool = new ObjectPool&lt;Node3D&gt;(
///     createFunc: () =&gt; packed.Instantiate(),
///     actionOnGet: n =&gt; { n.Visible = true; n.SetProcessMode(ProcessModeEnum.Inherit); },
///     actionOnRelease: n =&gt; { n.Visible = false; n.SetProcessMode(ProcessModeEnum.Disabled); },
///     actionOnDestroy: n =&gt; n.QueueFree());
///
/// var node = pool.Get();
/// /* …用它… */
/// pool.Return(node);
/// </code>
/// <b>重置那三样在 <c>actionOnGet</c> 与 <c>actionOnRelease</c> 里做</b> ——
/// 三个引擎的池子都这么写（UE 那份把这条列为头号坑：「回收时必须重置所有状态，否则出严重逻辑 bug」）。
/// 本仓最典型的一处是 <c>ModelAnimator</c>：它带三档私有状态，<c>Recycle()</c> 就是为复用准备的。</para>
/// </summary>
/// <typeparam name="T">池里放的东西。引用类型即可 —— 池子不认它是什么。</typeparam>
public sealed class ObjectPool<T> where T : class
{
    /// <summary>默认的缺省容量：构造时先造这么几个备用（0 = 不预造，第一发借出时才建）。</summary>
    public const int DefaultSize = 0;

    private readonly Func<T?> _create;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onRelease;
    private readonly Action<T>? _onDestroy;
    private readonly bool _collectionCheck;
    private readonly int _maxSize;
    private readonly Stack<T> _idle;

    // 借出未还的名册。collectionCheck 开着时它兼两个职责：挡住重复归还，以及给读数念「借出」。
    // 不开时它也仍然要留着 —— 否则归还那条路走的是池里弹出，而弹出的是最后借出的那一个，
    // 与调用点手上那个未必是同一个（那正是重复归还），弹错了就把别人的东西悄悄搬走了。
    private readonly HashSet<T> _borrowed = new(ReferenceEqualityComparer.Instance);

    // 造过几个（累计，含已被销毁的）。它是唯一能证伪池子有没有用的那一格：
    // 借还各走一圈之后它不涨，才说明复用真的发生了；它照涨，说明池子在空转。
    private long _created;

    /// <summary>借出总数（累计）。与 <see cref="Returned"/> 的差额恒等于 <see cref="Borrowed"/>。</summary>
    public long BorrowedCount { get; private set; }

    /// <summary>归还总数（累计）。</summary>
    public long ReturnedCount { get; private set; }

    /// <summary>造过的总数（累计，含已被 <c>actionOnDestroy</c> 销毁的）。</summary>
    public long CreatedCount => _created;

    /// <summary>满池后归还而被销毁的总数（累计）。持续涨 = <see cref="MaxSize"/> 给小了。</summary>
    public long DestroyedCount { get; private set; }

    /// <summary>池里现在有几个空闲的。</summary>
    public int InPool => _idle.Count;

    /// <summary>借出未还的有几个。</summary>
    public int Borrowed => _borrowed.Count;

    /// <summary>容量上限。归还时池里已有 <see cref="MaxSize"/> 个，多余的那个走销毁那一路。</summary>
    public int MaxSize => _maxSize;

    /// <summary>借出未归零、池空、<c>createFunc</c> 交回 null 的次数。记它是因为「借不出」必须读得出来。</summary>
    public long CreateFailedCount { get; private set; }

    /// <param name="createFunc">
    /// 池空时造一个。<b>交回 null = 这一发借不出</b>（丢弃那一条路由它开）。
    /// 签名是 <c>Func&lt;T?&gt;</c> 不是 <c>Func&lt;T&gt;</c>：借不出是<b>一等答案</b>，
    /// 不是异常、也不是「造一个假的出来」—— 特效峰值时宁可丢演出也不要节点数失控，那一路就是它。
    /// </param>
    /// <param name="actionOnGet">借出那一刻做什么。放进树、打开可见、复位状态，多半在这一格。</param>
    /// <param name="actionOnRelease">归还那一刻做什么。停处理、藏起来、清状态，多半在这一格。</param>
    /// <param name="actionOnDestroy">满池时销毁那一个。给 null = 超额归还的对象<b>被静默丢弃</b>（既不进池也不销毁，是一条泄漏）。</param>
    /// <param name="collectionCheck">
    /// 重复归还是否当场抛。<b>建议开发期开着</b>：多还一次在画面上完全看不出区别
    /// （池账照样平），只有复用次数够多才显形，而那时读数读不出来。
    /// 不给 = 恒为 <c>true</c>。
    /// </param>
    /// <param name="defaultSize">构造时先造这么几个备用。给 0 = 不预造。</param>
    /// <param name="maxSize">池里最多留几个。归还时超出这一格就销毁。至少 1。</param>
    /// <exception cref="ArgumentNullException"><paramref name="createFunc"/> 是 null —— 池空时没有任何办法造出来。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSize"/> 或 <paramref name="defaultSize"/> 越界。</exception>
    public ObjectPool(
        Func<T?> createFunc,
        Action<T>? actionOnGet = null,
        Action<T>? actionOnRelease = null,
        Action<T>? actionOnDestroy = null,
        bool collectionCheck = true,
        int defaultSize = DefaultSize,
        int maxSize = int.MaxValue)
    {
        _create = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
        _onGet = actionOnGet;
        _onRelease = actionOnRelease;
        _onDestroy = actionOnDestroy;
        _collectionCheck = collectionCheck;
        _maxSize = maxSize < 1
            ? throw new ArgumentOutOfRangeException(
                nameof(maxSize), maxSize, "容量至少 1，0 会让池永远存不下东西")
            : maxSize;
        _idle = new Stack<T>(defaultSize > 0 ? defaultSize : DefaultCapacity);

        if (defaultSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSize), defaultSize, "缺省容量不会是负的");
        }

        for (var i = 0; i < defaultSize; i++)
        {
            var pre = _create();
            if (pre is null)
            {
                // 预造阶段造不出来就少造一个，不抛 —— 预造是可选的优化，它失败不该让整局起不来。
                // 但「造过几个」照记，缺口在读数上看得出来。
                CreateFailedCount++;
                break;
            }

            _idle.Push(pre);
            _created++;
        }
    }

    /// <summary>没有预造容量时那个栈的起始容量（避一次扩容，1KB 一级）。</summary>
    private const int DefaultCapacity = 8;

    /// <summary>
    /// 借一个 —— 池里有就弹一个，没有就现造一个。
    ///
    /// <para><b>池空且 <c>createFunc</c> 交回 null 就是「借不出」</b>：返回 null，
    /// 已计入 <see cref="CreateFailedCount"/>，不排队。特效那一路的「峰值丢弃」就是这么开的。</para>
    /// </summary>
    /// <returns>借到的那一个；<c>null</c> = 借不出。</returns>
    public T? Get()
    {
        T? item;
        if (_idle.Count > 0)
        {
            item = _idle.Pop();
        }
        else
        {
            item = _create();
            if (item is null)
            {
                CreateFailedCount++;
                return null;
            }

            _created++;
        }

        _borrowed.Add(item);
        BorrowedCount++;
        _onGet?.Invoke(item);
        return item;
    }

    /// <summary>
    /// 还一个 —— 池没满就推回去，满了就交给 <c>actionOnDestroy</c>。
    ///
    /// <para><b>没借出过就还，当场抛</b>：那会把一个从没进过池的东西塞进去，
    /// 池里凭空多一个，且它没有对应的借出，<see cref="BorrowedCount"/> 与
    /// <see cref="ReturnedCount"/> 从此对不上。</para>
    ///
    /// <para><b>同一个东西还两次也是抛</b>（<c>collectionCheck</c> 开着时）：
    /// 它在池里已经有一份，再推一份就成了同一个对象的两个副本，弹出时会拿走同一个两次。</para>
    /// </summary>
    /// <param name="item">要还的那一个。</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> 是 null。</exception>
    /// <exception cref="InvalidOperationException">没借出过，或已经还过了。</exception>
    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!_borrowed.Remove(item))
        {
            throw new InvalidOperationException(
                _collectionCheck
                    ? "这一件没借出过，或者已经还过了。重复归还会让池里凭空多一份，"
                      + "弹出时同一个对象被拿走两次。"
                    : "这一件没借出过。（collectionCheck 关着，所以这一格没能提前抓住重复归还）");
        }

        ReturnedCount++;

        // 满池那一路：多出来的这一件不进池，交给销毁那一格。
        // 池满时它若被推回 _idle，栈就会涨过 _maxSize，于是「池里几个」这一格与
        // MaxSize 念得不一样，下一轮开始每归还一次都超一格 —— 那是静默失配，账上读不出来。
        if (_idle.Count >= _maxSize)
        {
            DestroyedCount++;
            _onDestroy?.Invoke(item);
            return;
        }

        _onRelease?.Invoke(item);
        _idle.Push(item);
    }

    /// <summary>
    /// 清空池里那些空闲的 —— <b>借出去未还的不在此列</b>（它们不在池里，本件也不管它们）。
    ///
    /// <para>每个空闲的那一个交给 <c>actionOnDestroy</c> 处置（Godot 侧那一格就是
    /// <c>QueueFree</c>）。<b>清完池子还能继续用</b>：借还计数不清零
    /// （<see cref="Borrowed"/> 那个名册与几个累计数照旧是这一份池的账），
    /// 要连账一起清是换一局那一档的事（<c>Reset</c> 那条路，不在池里）。</para>
    ///
    /// <para><b>为什么要有这一格</b>：节点随场景树一起回收之后，池里那份引用就全是失效引用；
    /// 下一次借出弹一个已释放的节点，那一格当场就崩在池里 —— 崩得响反而好，
    /// 但真要它安静地烂下去（跳过已释放的、悄悄造新的）就难查了。</para>
    /// </summary>
    /// <returns>清掉了几个。</returns>
    public int Clear()
    {
        var cleared = 0;
        while (_idle.Count > 0)
        {
            var item = _idle.Pop();
            _onDestroy?.Invoke(item);
            cleared++;
        }

        return cleared;
    }

    /// <summary>
    /// 池里现在几个、借出几个、造过几个、销毁几个、容量上限多少。
    /// 格式化只此一处，HUD 与调试桥读的是同一份。
    /// </summary>
    public string Readout()
        => $"池 在池 {Num(InPool)}｜借出 {Num(Borrowed)}"
          + $"｜新建 {Num(CreatedCount)}｜销毁 {Num(DestroyedCount)}"
          + $"｜借不出 {Num(CreateFailedCount)}｜上限 {Num(_maxSize)}";

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
}
