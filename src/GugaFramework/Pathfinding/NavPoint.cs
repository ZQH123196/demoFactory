#nullable enable
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 空间内的一个平面点，<b>两个 double</b>（<see cref="X"/> / <see cref="Z"/>，米）。
///
/// <para><b>为什么不吃 Godot 的 <c>float Vector2</c></b>：算术一律 <c>double</c>（「double 与不变文化」），
/// 而 <c>Vector2</c> 是单精度 —— 退休前那份手写趋近（它那边的 <c>Vector2</c> 已经被工单 08 摘掉）
/// 就是这么写的，
/// 与「double 与不变文化」 不合。逐字基线会分叉（见 <c>ReplayArgsTests</c> 那条「单精度升双精度」的用例：
/// <c>0.1f</c> 升成 <c>double</c> 再打印就不是 <c>0.1</c>）。</para>
///
/// <para>只给两个坐标：本层是平面寻路，不吃引擎的三维向量。表现层那头的 <c>Vector2</c> /
/// <c>Vector3</c> 换算归宿主，与 ADR 0009「换算归宿主」是同一口径。</para>
/// </summary>
/// <param name="X">横向坐标（米），相对空间原点。</param>
/// <param name="Z">进深坐标（米），相对空间原点。</param>
public readonly record struct NavPoint(double X, double Z)
{
    /// <summary>到另一点的欧氏距离（米，double）。</summary>
    public double DistanceTo(NavPoint other)
    {
        var dx = X - other.X;
        var dz = Z - other.Z;
        return System.Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>逐字可读形式（invariant，「double 与不变文化」）—— 错误消息与账本用它，别用 <c>ToString()</c>（那跟着当前区域设置走）。</summary>
    public override string ToString()
        => $"({X.ToString(CultureInfo.InvariantCulture)}, {Z.ToString(CultureInfo.InvariantCulture)})";
}
