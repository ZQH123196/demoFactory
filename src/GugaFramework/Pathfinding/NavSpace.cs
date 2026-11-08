#nullable enable
using System;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 寻路空间 —— 一个相对坐标系，加上归它所有的那张寻路网格。
///
/// <para><b>空间只有名字，没有原点与变换</b>（ADR 0009）：坐标相对空间原点，
/// 「空间→世界」的换算归宿主，模块既不持有也不施加。移动承载平台上的小镇是平台局部米，
/// 宿主施加位移与偏航 —— 本层这一档与现状完全兼容，
/// 双坐标系的换算留在宿主侧，没有被请回来。</para>
///
/// <para><b>网格自带一个原点，那不是空间的变换</b>：<see cref="NavGrid.OriginX"/> /
/// <see cref="NavGrid.OriginZ"/> 说的是「第 (0,0) 格那一角落在空间坐标的哪一点」——
/// 网格在空间里的位置。空间仍然以自己的原点为参考，仍然不持有任何换算。
/// 两者分开的原因：网格挪位置是**一场地的事**（宿主摆的场地在哪儿），
/// 空间→世界的换算是**另一场的事**（承载平台在哪儿）；合成一件就又把双坐标系请回来了。</para>
///
/// <para>构造时校验它持有的网格确实属于自己：空间与网格各报一个 id 而两者对不上，
/// 症状是「有的请求莫名其妙跨空间被抛」，而根因在装配那一刻。装配期就报出来，
/// 报错时还能把两个 id 都带出来。</para>
/// </summary>
public sealed class NavSpace
{
    /// <summary>开一个空间并挂上它的网格。</summary>
    /// <exception cref="InvalidOperationException">网格不属于这个空间。</exception>
    public NavSpace(SpaceId space, NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Space != space)
        {
            throw new InvalidOperationException(
                $"空间与网格对不上：空间是「{space}」，这张网格属于「{grid.Space}」。"
                + "空间不带变换，换算归宿主 —— 两个 id 对不上时装配就是错的，当场报出来比运行期报一句「跨空间」强。");
        }

        Space = space;
        Grid = grid;
    }

    /// <summary>空间的名字（<see cref="SpaceId"/>）。</summary>
    public SpaceId Space { get; }

    /// <summary>归这个空间所有的那张寻路网格。</summary>
    public NavGrid Grid { get; }

    /// <summary>这个空间的静态地形版本（转发那张网格的读数，省得调用方每次自己摸下去）。</summary>
    public int StaticVersion => Grid.StaticVersion;

    /// <summary>这个空间的动态代价版本。</summary>
    public int DynamicVersion => Grid.DynamicVersion;

    /// <inheritdoc />
    public override string ToString() => $"NavSpace({Space})";
}
