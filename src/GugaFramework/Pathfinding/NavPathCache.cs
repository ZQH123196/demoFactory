#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 路径缓存的键 —— spec「路径缓存与共享目标」逐字点名的五列，
/// 外加 v1 点到点补的第六列 <see cref="Start"/>。
///
/// <para><b>前五列</b>：<see cref="Space"/> 空间、<see cref="Goal"/> 目标、
/// <see cref="WidthMeters"/> 宽度、<see cref="StaticVersion"/> 静态版本、
/// <see cref="DynamicVersion"/> 动态版本。两个版本进键是这个设计能成立的前提 ——
/// 地形一变旧条目就再也取不到，「在手路径的复用」于是自动按版本作废，
/// 不需要另写一套作废逻辑（规则：重算的触发只看版本号）。</para>
///
/// <para><b>第六列是本层补的</b>：起点不同的两条路不是同一条路，v1 的 seam 是点到点，
/// 拿 A 走通的那条去喂 B，等于假装成了一条从 B 出发也没错的路。
/// spec 那句「尾巴自己错开」说的是流场那条 adapter（v1 不做，见 spec「Out of Scope」），
/// 把它塞进点到点就是范畴错误。这条待确认项记在 <c>docs/GugaFramework/寻路层.md</c> 2 节。</para>
/// </summary>
public readonly record struct NavPathCacheKey
{
    /// <summary>空间。</summary>
    public SpaceId Space { get; init; }

    /// <summary>目标点（米）。</summary>
    public NavPoint Goal { get; init; }

    /// <summary>宽度（半径，米）。</summary>
    public double WidthMeters { get; init; }

    /// <summary>静态地形版本。</summary>
    public int StaticVersion { get; init; }

    /// <summary>动态代价版本。</summary>
    public int DynamicVersion { get; init; }

    /// <summary>起点（米）。v1 点到点补的第六列 —— 理由见类型注释。</summary>
    public NavPoint Start { get; init; }

    /// <inheritdoc />
    public override string ToString()
        => $"{Space} 起点 {Start.ToString()} 目标 {Goal.ToString()} 宽 {WidthMeters.ToString(CultureInfo.InvariantCulture)}"
           + $" [{StaticVersion.ToString(CultureInfo.InvariantCulture)}/{DynamicVersion.ToString(CultureInfo.InvariantCulture)}]";
}

/// <summary>
/// 路径缓存 —— <b>这是缓存不是新能力</b>（spec 原话）。它不产生任何新的路：
/// 命中的那一条必须是之前真算出来的同一条，逐字照交。
///
/// <para><b>只有走通的那条进缓存。</b>部分路径与失败不进：
/// 缓存一份「没走到」等于把一次预算触顶固化成常驻答案，
/// 而「请求了但没走到」正是这一层要暴露的那件事（spec user story 4）。</para>
///
/// <para><b>有界 + 先到先得淘汰</b>：超出容量顶掉最老的那条。淘汰走一条
/// <see cref="Queue{T}"/> 的先进先出次序，<b>不枚举字典</b> ——
/// 哈希桶的枚举序跟着插入历史与哈希函数走，用它挑「最老的那条」就是随机的（「C# 文件基线」 那种逐字可重放的要求）。</para>
/// </summary>
public sealed class NavPathCache
{
    private readonly Dictionary<NavPathCacheKey, NavPath> _byKey;
    private readonly Queue<NavPathCacheKey> _fifo;
    private int _hits;
    private int _misses;

    /// <summary>造一个缓存。<paramref name="capacity"/> 至少 1，当场抛（「桥只转发」）。</summary>
    public NavPathCache(int capacity)
    {
        NavSolveCheck.Capacity(capacity, nameof(capacity));
        Capacity = capacity;
        _byKey = new Dictionary<NavPathCacheKey, NavPath>(capacity);
        _fifo = new Queue<NavPathCacheKey>(capacity);
    }

    /// <summary>容量（最多留多少条）。</summary>
    public int Capacity { get; }

    /// <summary>现在存着多少条。</summary>
    public int Count => _byKey.Count;

    /// <summary>命中过多少次。</summary>
    public int Hits => _hits;

    /// <summary>未命中过多少次。</summary>
    public int Misses => _misses;

    /// <summary>取一条。<b>命中与未命中各计一次</b>，所以「命中率」是账本能直接读出的数，不必回头数求解记录。</summary>
    public bool TryGet(in NavPathCacheKey key, out NavPath path)
    {
        if (_byKey.TryGetValue(key, out var found))
        {
            _hits++;
            path = found;
            return true;
        }

        _misses++;
        path = null!;
        return false;
    }

    /// <summary>存一条。容量满时顶掉最老的那条（先进先出）。</summary>
    public void Store(in NavPathCacheKey key, NavPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_byKey.ContainsKey(key))
        {
            _byKey[key] = path;
            return;
        }

        if (_fifo.Count >= Capacity)
        {
            _byKey.Remove(_fifo.Dequeue());
        }

        _byKey.Add(key, path);
        _fifo.Enqueue(key);
    }

    /// <summary>逐字可读的一行（命中 / 未命中 / 存了多少条）。</summary>
    public override string ToString()
        => $"路径缓存 存 {Count.ToString(CultureInfo.InvariantCulture)}/{Capacity.ToString(CultureInfo.InvariantCulture)} 条"
           + $" 命中 {Hits.ToString(CultureInfo.InvariantCulture)} 未命中 {Misses.ToString(CultureInfo.InvariantCulture)}";
}
