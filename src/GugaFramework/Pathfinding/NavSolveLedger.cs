#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 求解记录的<b>有界账本</b> —— 环形保留最近 <c>capacity</c> 条，超出就顶掉最老的一条。
///
/// <para>有界是必须的：账本与进程同寿，不设上限的话一场长局会把它涨到「读数本身要花掉的内存比地形还多」，
/// 而它的用途恰恰是人在排障时一发读出来。</para>
///
/// <para><b>按下标寻址，一个字面意义上的队列</b>：没有 <c>foreach</c>、
/// 没有 <c>Dictionary</c> / <c>HashSet</c>，所以「第 k 条是哪一条」是定死的
/// （spec「确定性靠结构」：任何生成路径的遍历都不许碰字典的枚举序）。
/// 要一次拿全部就调 <see cref="Snapshot"/>，它按写入次序摊平成新数组 ——
/// 遍历的是数组下标，不是哈希桶。</para>
/// </summary>
public sealed class NavSolveLedger
{
    private readonly NavSolveRecord[] _rows;
    private int _next;      // 下一个写进哪一格
    private int _stored;    // 写进去多少条（封顶后等于容量）
    private long _sequence; // 全局单调的求解序号（被顶掉的那些也占号，序号不回收）

    /// <summary>造一个有界账本。<paramref name="capacity"/> 至少 1，当场抛（「桥只转发」）。</summary>
    public NavSolveLedger(int capacity)
    {
        NavSolveCheck.Capacity(capacity, nameof(capacity));
        _rows = new NavSolveRecord[capacity];
    }

    /// <summary>容量（最多留多少条）。</summary>
    public int Capacity => _rows.Length;

    /// <summary>账本里现在存着多少条（未满时是写过的条数）。</summary>
    public int Count => _stored;

    /// <summary>被顶掉（因账本满而丢失）的条数。诊断用：一条不丢才是常态。</summary>
    public int Dropped => (int)(_sequence - _stored);

    /// <summary>最近那一条。没有记录时返回 null（<b>不</b>抛 —— 「还没求解过」是一个正常状态）。</summary>
    public NavSolveRecord? Last => _stored == 0 ? null : _rows[(_next + _rows.Length - 1) % _rows.Length];

    /// <summary>
    /// 按写入次序取第 <paramref name="index"/> 条（0 = 最老的那条仍在的那条）。下标越界当场抛。
    /// </summary>
    public NavSolveRecord At(int index)
    {
        if (index < 0 || index >= _stored)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"账本里只有 {_stored.ToString(CultureInfo.InvariantCulture)} 条（容量 {_rows.Length.ToString(CultureInfo.InvariantCulture)}），"
                + $"下标 {index} 越界 —— 越界是调用方的错，不是一个该被兜住的读数。");
        }

        var start = _stored == _rows.Length ? _next : 0;
        return _rows[(start + index) % _rows.Length];
    }

    /// <summary>按写入次序摊平成一个新数组（老的在前）。遍历的是数组下标，不是哈希桶。</summary>
    public NavSolveRecord[] Snapshot()
    {
        var copy = new NavSolveRecord[_stored];
        for (var i = 0; i < _stored; i++)
        {
            copy[i] = At(i);
        }

        return copy;
    }

    /// <summary>追加一条（求解器调用）。序号在这里发，跨步连续。</summary>
    internal NavSolveRecord Append(NavSolveRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _sequence++;
        _rows[_next] = record with { Sequence = _sequence };
        _next = (_next + 1) % _rows.Length;
        if (_stored < _rows.Length)
        {
            _stored++;
        }

        return _rows[(_next + _rows.Length - 1) % _rows.Length];
    }
}
