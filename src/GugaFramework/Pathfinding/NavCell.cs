#nullable enable
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 寻路网格上的一格，<b>两个整数</b>（<see cref="X"/> / <see cref="Z"/>）。
///
/// <para>整数格坐标是<b>网格的身份</b>，与 <see cref="NavPoint"/>（米）分得开：
/// 前者答「哪一格」，后者答「站在哪儿」。净空场逐格查，位置连续查，
/// 两套坐标混用会引出「格心还是格面」那种每次算出不同答案的伪规则（spec「动态代价按点求值」一节记的就是这件事）。</para>
/// </summary>
/// <param name="X">第几列（从 0 起，向 +X 数）。</param>
/// <param name="Z">第几行（从 0 起，向 +Z 数）。</param>
public readonly record struct NavCell(int X, int Z)
{
    /// <summary>逐字可读形式（invariant，「double 与不变文化」）。</summary>
    public override string ToString()
        => $"({X.ToString(CultureInfo.InvariantCulture)}, {Z.ToString(CultureInfo.InvariantCulture)})";
}
