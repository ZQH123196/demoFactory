#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 造一张寻路网格要递进来的那份 —— <b>宿主全递，公共层一个缺省都不给</b>。
///
/// <para>照 <c>SkillStageSpec.cs:15-19</c> 的纪律（<c>required</c>，少一列编译期就断）：
/// 公共层不替调用方挑格长。理由是格长同时是寻路网格与净空场的分辨率，
/// 而清空的保守量由它决定（见 <see cref="ClearanceField"/>）；一个藏在层里的默认格长
/// 会让「单位为什么站不进那半米」变成一件查不到出处的事。</para>
///
/// <para><b><see cref="BlockedCells"/> 必填，空表是合法值</b>：空表是一次明确的声明
/// （这片地形没有地形阻挡），不是「忘了给」。把「没给」与「明确没有」分成两种形状，
/// 才挡得住「漏了这一类等于墙不存在」——spec 地形来源接线一节点名的正是这一类。</para>
///
/// <para>按「档与表钉死」，格长要跟原型的数据表钉在一起：离线测试逐字解析那份表、运行时宿主比对，
/// 漂了当场抛。哪张表、钉哪一格是各原型接入时的事（ticket 08），
/// 本层这一侧的口径是<b>非正数 / 非有限数当场抛</b>，不兜 0（「桥只转发」）。</para>
///
/// <para><b>网格原点（<see cref="OriginX"/> / <see cref="OriginZ"/>）也是必填那一列</b>：
/// 它说的是「第 (0,0) 格那一角落在空间坐标的哪一点」。少了它，整张网格会静默落在空间的另一处，
/// 症状是「路径算得出来，兽却走不到」—— 那是本层最贵的一种错法：
/// 读数全绿，错的是场地本身。</para>
///
/// <para><b>原点不是空间→世界的变换</b>（ADR 0009 那一条没动）：空间仍然只有名字，
/// 空间→世界的换算仍然归宿主。这一列说的是<b>网格在空间里的位置</b>，
/// 换句话说：空间仍然以自己的原点为参考，网格只是从空间原点起偏了一段。</para>
/// </summary>
public sealed record NavGridSpec
{
    /// <summary>这张网格活在哪一个相对坐标系里。必填：网格要报得出自己属于哪个空间，跨空间请求才判得出来。</summary>
    public required SpaceId Space { get; init; }

    /// <summary>一格多少米（double，必须为正且有限）。寻路网格与净空场的分辨率，<b>公共层不给缺省</b>。</summary>
    public required double CellMeters { get; init; }

    /// <summary>
    /// 网格第 (0, 0) 格那一角的 X（米，空间坐标）。<b>必填</b>。
    ///
    /// <para>格 (cx, cz) 盖住的那一块是
    /// <c>[OriginX + cx×CellMeters, OriginX + (cx+1)×CellMeters)</c>，
    /// 格心是 <c>OriginX + (cx+0.5)×CellMeters</c>。</para>
    /// </summary>
    public required double OriginX { get; init; }

    /// <summary>
    /// 网格第 (0, 0) 格那一角的 Z（米，空间坐标）。<b>必填</b>，口径同 <see cref="OriginX"/>。
    /// </summary>
    public required double OriginZ { get; init; }

    /// <summary>列数（沿 +X，至少 1）。</summary>
    public required int CellsX { get; init; }

    /// <summary>行数（沿 +Z，至少 1）。</summary>
    public required int CellsZ { get; init; }

    /// <summary>地形阻挡格。必填；空表 = 明确声明「这片没有地形阻挡」。</summary>
    public required IReadOnlyList<NavCell> BlockedCells { get; init; }
}

/// <summary>
/// 造网格时那几类当场抛的入参校验 —— 集中在这一处，错误消息给的是<b>能照着改的读数</b>。
///
/// <para>口径是「桥只转发」：数字参数缺了 / 不成当场抛，不兜 0。兜 0 在这一层等于
/// 「静默摆到第 0 排」的同族病 —— 格长兜 0 会让所有距离都变成 0，净空场全通，
/// 症状是「什么都能站」，离病根隔着一层。</para>
/// </summary>
internal static class NavGridSpecCheck
{
    /// <summary>格长：为正、为有限数。</summary>
    public static void CellMeters(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"格长必须是正的有限数，收到 {value.ToString(CultureInfo.InvariantCulture)} —— 兜 0 会让净空场恒为 0、什么都能站（「桥只转发」 不许兜默认值）。");
        }
    }

    /// <summary>格数：至少 1（一张空网格没有格心，谈不上净空）。</summary>
    public static void CellCount(int value, string name)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(
                name, value, $"格数至少为 1，收到 {value} —— 0 格网格判不出「边界在哪」，边界是网格的定义之一。");
        }
    }

    /// <summary>
    /// 网格原点：只要是有限数（<b>负数合法</b> —— 原点落在空间原点之前是常事）。
    ///
    /// <para>不兜 0：原点的缺省值看着最无害（零基网格到处都有），
    /// 但真该是 −56 的那张网格被兜成 0，整片场地就偏了 56 米，
    /// 而症状是「路算得通、兽走不到」，读数上完全看不出。</para>
    /// </summary>
    public static void Origin(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                name, value,
                $"网格原点必须是有限数，收到 {value.ToString(CultureInfo.InvariantCulture)} —— 原点错了整张网格落在别处，症状是「路算得通却走不到」（「桥只转发」 不许兜默认值）。");
        }
    }
}
