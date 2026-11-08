#nullable enable
using System;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 寻路空间的名字 —— 寻路网格、净空场、路径上带着的那一份「我活在哪一个相对坐标系里」。
///
/// <para><b>为什么空间只有名字没有变换</b>（ADR 0009）：空间本身不持有原点与变换，
/// 「空间→世界」的换算归宿主，模块不持有也不施加。移动承载平台上的小镇是平台局部米，
/// 宿主施加位移与偏航 —— 把可移动的参考点请回模块里，
/// 正是那两套坐标系换算，而那套换算在本仓判为「最贵」而一直没请进来。
/// 所以这里包的是<b>一个非空字符串</b>，不是原点、不是 Matrix。</para>
///
/// <para>非空是硬要求而不是顺手加的：空 id 判不出「跨空间」是哪两个空间，
/// 报出来的错会指向一个不存在的东西，agent 顺着读也读不出路。</para>
/// </summary>
public readonly record struct SpaceId
{
    /// <summary>构造一个空间 id。<paramref name="value"/> 为 null / 空白当场抛（不兜一个默认名 —— 兜出来的名字会混进错误消息，指向一个假空间）。</summary>
    /// <exception cref="ArgumentException">名字是 null、空串或纯空白。</exception>
    public SpaceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "寻路空间的名字不能是 null、空串或纯空白 —— 跨空间当场抛要靠它把双方 id 带出来（ADR 0009），空名报出来的错指向一个不存在的东西。",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>空间的名字。恒为非空。</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
