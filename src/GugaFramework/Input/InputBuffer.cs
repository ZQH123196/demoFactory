#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.InputLayer;

// 输入缓冲 —— 补「这一发本来会作废」的输入。
//
// 纯 C#、留在表现层、不进 Journal：玩家输入按契约走玩家路径，玩家路径本就不进录制。
//
// 这一格的判据（否则它与契约「同种子重跑逐字全等」矛盾）：
//   缓冲**只补这一发本来会作废的输入，不改变任何已生效命令的时机**。
//   · 刚按下 —— 补。窗口内消费时命令与参数与「没丢」完全相同。
//   · 按住 / 模拟量 —— 不补。本来每帧读，不存在「作废」。
// 补的是**触发时刻**不同，不是**命令内容**不同。
//
// 时间窗按步数算（一步 = 0.1 秒）。窗长 2 步是判断不是实测，要改改构造参数一个数即可。

/// <summary>输入缓冲 —— 一小段步数窗口内没接住的那一发，补回来。</summary>
public sealed class InputBuffer
{
    /// <summary>一个概念名对应一条待消费记录。</summary>
    private sealed record Pending(string Concept, long PressedAtTick);

    /// <summary>缺省窗长 2 步（0.2 秒）。</summary>
    public const int DefaultWindowSteps = 2;

    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    /// <summary>建一个窗长 <paramref name="windowSteps"/> 步的缓冲。</summary>
    /// <param name="windowSteps">窗长（步）。至少 1 —— 0 步的窗口等于没有缓冲。</param>
    public InputBuffer(int windowSteps = DefaultWindowSteps)
    {
        if (windowSteps < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowSteps), windowSteps, "窗长至少 1 步：0 步的窗口接不住任何一发。");
        }

        WindowSteps = windowSteps;
    }

    /// <summary>窗长（步）。</summary>
    public int WindowSteps { get; }

    /// <summary>缓冲里还挂着几条。</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// 记一发「刚按下」。
    ///
    /// <para>同一概念还挂着的时候再记一次 = 玩家又按了一次：新的那发顶掉旧的那发，
    /// 旧的发数不再单独占一格（否则「狂点两下」会在窗口里堆出两条同概念的记录）。</para>
    /// </summary>
    /// <param name="concept">概念。只收刚按下那一档。</param>
    /// <param name="currentTick">接住这一发时的步号。</param>
    /// <exception cref="ArgumentException">读法不是刚按下 —— 按住与模拟量那一格不补。</exception>
    public void Record(InputConcept concept, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(concept);
        RequireJustPressed(concept, nameof(Record));
        _pending[concept.Name] = new Pending(concept.Name, currentTick);
    }

    /// <summary>
    /// 试消费一条：还在窗口内就消费掉并回传它是第几步按下的。
    /// 出窗的那一发当场作废（而不是留在缓冲里等人来收）。
    /// </summary>
    /// <param name="concept">概念。</param>
    /// <param name="currentTick">当前步号。</param>
    /// <param name="pressedAtTick">消费成功时回传按下那一步的步号。</param>
    /// <returns>消费到了返回 true。</returns>
    public bool TryConsume(InputConcept concept, long currentTick, out long pressedAtTick)
    {
        ArgumentNullException.ThrowIfNull(concept);
        pressedAtTick = 0L;

        if (!_pending.TryGetValue(concept.Name, out var entry)) return false;

        var age = currentTick - entry.PressedAtTick;
        if (age < 0 || age > WindowSteps)
        {
            // 出窗即作废：留在缓冲里等人来收，就会变成「玩家早就想按、现在才生效」。
            _pending.Remove(concept.Name);
            return false;
        }

        _pending.Remove(concept.Name);
        pressedAtTick = entry.PressedAtTick;
        return true;
    }

    /// <summary>清掉所有出窗的记录（每帧调一次，或者在窗口边界那一处调）。</summary>
    /// <param name="currentTick">当前步号。</param>
    /// <returns>这次清掉了几条。</returns>
    public int ExpirePast(long currentTick)
    {
        var doomed = new List<string>();
        foreach (var pair in _pending)
        {
            var age = currentTick - pair.Value.PressedAtTick;
            if (age < 0 || age > WindowSteps) doomed.Add(pair.Key);
        }

        foreach (var name in doomed) _pending.Remove(name);
        return doomed.Count;
    }

    /// <summary>这个概念当前挂着的那一发是第几步按下的（没挂就是 false）。</summary>
    public bool Peek(InputConcept concept, out long pressedAtTick)
    {
        ArgumentNullException.ThrowIfNull(concept);
        if (_pending.TryGetValue(concept.Name, out var entry))
        {
            pressedAtTick = entry.PressedAtTick;
            return true;
        }

        pressedAtTick = 0L;
        return false;
    }

    /// <summary>
    /// 这一格能不能进缓冲 —— 只放刚按下那一档过。
    /// 「按住 / 模拟量不补」这条判据钉在这里，不是钉在每个调用点。
    /// </summary>
    public static bool Bufferable(InputConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return concept.Read == ReadMode.JustPressed;
    }

    private static void RequireJustPressed(InputConcept concept, string caller)
    {
        if (!Bufferable(concept))
        {
            throw new ArgumentException(
                $"概念 {concept.Name} 的读法是 {concept.Read}，不进缓冲（{caller}）："
                + "按住与模拟量每帧都读，不存在「这一发作废」，补它只会让连续量变成一次性触发。",
                caller);
        }
    }
}
