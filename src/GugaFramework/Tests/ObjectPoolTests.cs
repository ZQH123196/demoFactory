#nullable enable
using System;
using DemoFactory.GugaFramework.ObjectPool;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 对象池的判红线 —— 计数从零来、池空现造、满池销毁、重复归还抛、借不出是 null。
///
/// <para><b>为什么这一份能离线判红</b>：池子是一个字都不碰引擎的纯 C# 件 ——
/// 借、还、造、销毁、计数全是纯函数与计数器。所以做成泛型而不是 Node 专用，
/// 就是为了这一份能链进测试进程跑（Node 专用那版要在五处 csproj 点名排除，于是永远离线测不到）。</para>
///
/// <para>用例名带 spec 那一行的原词，是为了让「这一条判据还有没有钉着」变成一件 grep 就能答的事。</para>
/// </summary>
public sealed class ObjectPoolTests
{
    // 池里放的东西：一个纯 POCO，带一个名字，用来断言「借出/归还搬动的确实是那一个」。
    private sealed class Thing
    {
        public Thing(string name) => Name = name;

        public string Name { get; set; }

        public int BornCount { get; set; }
    }

    // === 计数从零来：构造后那一格不是「忘了初始化」而是真的零 ===

    [Fact]
    public void 构造后计数全从零来_读数念得出每一格()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"));

        pool.InPool.ShouldBe(0, "没预造，池里就是空的");
        pool.Borrowed.ShouldBe(0, "一个都没借出");
        pool.CreatedCount.ShouldBe(0, "池空之前没造过东西");
        pool.ReturnedCount.ShouldBe(0, "一个都没归还");
        pool.DestroyedCount.ShouldBe(0, "没发生过销毁");
        pool.CreateFailedCount.ShouldBe(0, "借出从没失败过");

        // 读数六格都在，且不抛（数字格式化那一格是本仓最容易静默出错的地方）。
        var readout = pool.Readout();
        readout.ShouldContain("在池 0");
        readout.ShouldContain("借出 0");
        readout.ShouldContain("新建 0");
    }

    // === 池空现造 ===

    [Fact]
    public void 池空就现造_借一次造一个_新建那一格涨一()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"));

        var a = pool.Get();
        var b = pool.Get();

        a.ShouldNotBeNull();
        b.ShouldNotBeNull();
        pool.CreatedCount.ShouldBe(2, "借两次、池两次都空，所以造了两个");
        pool.InPool.ShouldBe(0, "造出来的立刻借出去了，池里还是空的");
    }

    // === 归还进池 ===

    [Fact]
    public void 归还推回池_池里多一个_借出少一个()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"));
        var item = pool.Get();
        item.ShouldNotBeNull();

        pool.Return(item!);

        pool.InPool.ShouldBe(1, "还了一个，池里就有一个");
        pool.Borrowed.ShouldBe(0, "还了就不算借出未还了");
        pool.ReturnedCount.ShouldBe(1);
        pool.CreatedCount.ShouldBe(1, "归还不造东西");
    }

    // === 借还一圈：不新建才说明复用真的发生 ===

    [Fact]
    public void 借还再借_第二次不新建_搬回的是同一个()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"));
        var first = pool.Get();
        first.ShouldNotBeNull();

        pool.Return(first!);
        var second = pool.Get();

        second.ShouldBeSameAs(first, "池里有就弹同一个，不该造新的");
        pool.CreatedCount.ShouldBe(1, "两发借出只造过一个，新建那一格没涨 —— 复用真的发生了");
    }

    // === 满池销毁 ===

    [Fact]
    public void 满池后归还走销毁_池不超过上限_销毁那一格涨()
    {
        var destroyed = 0;
        var pool = new ObjectPool<Thing>(
            createFunc: () => new Thing("new"),
            actionOnDestroy: _ => destroyed++,
            maxSize: 1);

        var a = pool.Get(); a.ShouldNotBeNull();
        var b = pool.Get(); b.ShouldNotBeNull();   // 池空现造，造了两个、都借出
        pool.Return(a!);                          // 池 0→1
        pool.Return(b!);                          // 池满(1)，销毁这一个

        pool.InPool.ShouldBe(1, "满了就停，池里最多 maxSize 个");
        pool.DestroyedCount.ShouldBe(1, "超出的那一个走了销毁那一路");
        destroyed.ShouldBe(1, "actionOnDestroy 被调到了");
    }

    // === collectionCheck：重复归还抛 ===

    [Fact]
    public void 重复归还当场抛_不抛的话池里会多出一份()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"));
        var item = pool.Get();
        item.ShouldNotBeNull();

        pool.Return(item!);

        Should.Throw<InvalidOperationException>(() => pool.Return(item!),
            "同一个东西还两次，会在池里塞进两份同一个对象，弹出时拿走两次 —— 当场抛才对");
    }

    [Fact]
    public void 没借出过就还当场抛()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"));
        var stray = new Thing("stray");

        Should.Throw<InvalidOperationException>(() => pool.Return(stray),
            "没进过池的东西还给池，池里凭空多一个，借还账从此对不上");
    }

    [Fact]
    public void 关掉collectionCheck_没借出过就还照旧抛_只是理由换一句()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"), collectionCheck: false);
        var stray = new Thing("stray");

        Should.Throw<InvalidOperationException>(() => pool.Return(stray),
            "collectionCheck 只管重复归还，不管「没借出」—— 那是借还账不平，仍然抛");
    }

    // === 借不出：createFunc 交回 null ===

    [Fact]
    public void createFunc交回null就是借不出_返回null且记一次()
    {
        var pool = new ObjectPool<Thing>(() => null!);   // 造不出来

        var got = pool.Get();

        got.ShouldBeNull("造不出来就是借不出，不是崩");
        pool.CreateFailedCount.ShouldBe(1, "借不出必须读得出来");
        pool.Borrowed.ShouldBe(0, "没借到就不算借出");
    }

    // === 容量上限：借还不让池涨过 maxSize ===

    [Fact]
    public void 容量上限内归还池不涨过上限_且读数念得出上限()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"), maxSize: 2);

        var a = pool.Get(); a.ShouldNotBeNull();
        var b = pool.Get(); b.ShouldNotBeNull();
        var c = pool.Get(); c.ShouldNotBeNull();

        pool.Return(a!);
        pool.Return(b!);
        pool.Return(c!);

        pool.InPool.ShouldBe(2, "归还三个、上限两个，于是池停在上限，第三个走销毁");
        pool.DestroyedCount.ShouldBe(1);
        pool.MaxSize.ShouldBe(2);
        pool.Readout().ShouldContain("上限 2");
    }

    // === 重置委托：归还那一刻被调到了 ===

    [Fact]
    public void 归还回调被调到_且在推回池之前()
    {
        var released = 0;
        var pool = new ObjectPool<Thing>(
            createFunc: () => new Thing("new"),
            actionOnRelease: _ => released++);

        var item = pool.Get();
        item.ShouldNotBeNull();
        pool.Return(item!);

        released.ShouldBe(1, "归还那一路要走 actionOnRelease —— 藏起来/停处理都在那一格");
    }

    // === Clear：清空闲的，借出去的不动 ===

    [Fact]
    public void Clear只清空闲的_借出去未还的不动_且池子还能继续用()
    {
        var destroyed = 0;
        var pool = new ObjectPool<Thing>(
            createFunc: () => new Thing("new"),
            actionOnDestroy: _ => destroyed++);

        var held = pool.Get();
        held.ShouldNotBeNull();
        pool.Return(held!);      // 这一个进池
        var out2 = pool.Get();   // 又借出一个 —— 池空
        out2.ShouldNotBeNull();

        var cleared = pool.Clear();   // 池里空（out2 在外头），所以清 0

        cleared.ShouldBe(0, "借出去的那一个不在池里，Clear 不归它管");
        pool.Borrowed.ShouldBe(1, "借出未还的名册没被动过");
        pool.Get().ShouldNotBeNull("Clear 之后池子还能继续借 —— 它清的是空闲的那一份");
    }

    [Fact]
    public void Clear把空闲的交给actionOnDestroy_并报清掉几个()
    {
        var destroyed = 0;
        var pool = new ObjectPool<Thing>(
            createFunc: () => new Thing("new"),
            actionOnDestroy: _ => destroyed++);

        pool.Get().ShouldNotBeNull();
        var a = pool.Get(); a.ShouldNotBeNull();
        pool.Return(a!);
        pool.InPool.ShouldBe(1, "先归一个，池里有一个");

        var cleared = pool.Clear();

        cleared.ShouldBe(1, "清掉了池里那一个");
        destroyed.ShouldBe(1, "清掉的交给 actionOnDestroy 处置");
        pool.InPool.ShouldBe(0, "清完池子是空的");
    }

    // === 读数：新建那一格是唯一能证伪池子有没有用的数字 ===

    [Fact]
    public void 读数六格_借还之后各念各的()
    {
        var pool = new ObjectPool<Thing>(() => new Thing("new"), maxSize: 4);

        var a = pool.Get();
        pool.Get().ShouldNotBeNull();
        pool.Return(a!);

        var readout = pool.Readout();
        readout.ShouldContain("在池 1");
        readout.ShouldContain("借出 1");
        readout.ShouldContain("新建 2");   // 借了两次都池空，造了两个
    }
}