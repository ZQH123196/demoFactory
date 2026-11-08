#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Abilities;

/// <summary>
/// 一条 Calc 的入参。两个属性输入都是字典、不是活对象（已删的设计档）：
/// <see cref="Source"/> 是过 commit 那一步抄下来的快照，<see cref="Target"/> 是落地那一步现读的。
/// 函数体里没有节点、没有引用、没有能改的东西，A1 从这一格守住。
/// </summary>
public readonly struct CalcInput
{
    public CalcInput(
        IReadOnlyDictionary<string, double> source,
        IReadOnlyDictionary<string, double> target,
        IReadOnlyDictionary<string, int> tagCount,
        double working,
        IReadOnlyDictionary<string, double> @params,
        long tick)
    {
        Source = source;
        Target = target;
        TagCount = tagCount;
        Working = working;
        Params = @params;
        Tick = tick;
    }

    /// <summary>施法瞬间抄下来的源属性快照。</summary>
    public IReadOnlyDictionary<string, double> Source { get; }

    /// <summary>落地那一步现读的目标属性。</summary>
    public IReadOnlyDictionary<string, double> Target { get; }

    /// <summary>目标身上的 tag 计数（mark 类算法要读这一个口，不给 tag 单开通道）。</summary>
    public IReadOnlyDictionary<string, int> TagCount { get; }

    /// <summary>链上上一条 Calc 的输出。链的起点是效果表里的 <c>CalcSeed</c>。</summary>
    public double Working { get; }

    /// <summary>表里给这条 Calc 的参数。</summary>
    public IReadOnlyDictionary<string, double> Params { get; }

    /// <summary>第几步。</summary>
    public long Tick { get; }

    /// <summary>取参数，缺了就抛——兜 0 等于静默换个算法（同一口径见「桥只转发」）。</summary>
    public double Param(string name)
    {
        if (!Params.TryGetValue(name, out var value))
        {
            throw new KeyError($"Calc 缺参数 | {name} | 已给 [{string.Join(",", Params.Keys)}]");
        }

        return value;
    }

    /// <summary>取属性，缺了就抛。</summary>
    public static double Attr(IReadOnlyDictionary<string, double> attrs, string name)
    {
        if (!attrs.TryGetValue(name, out var value))
        {
            throw new KeyError($"Calc 缺属性 | {name} | 已有 [{string.Join(",", attrs.Keys)}]");
        }

        return value;
    }
}

/// <summary>按名注册的纯函数：公式、条件、谓词都走这一个形状（公理 A1 的第二种形态）。</summary>
public delegate double CalcFn(CalcInput input);

/// <summary>
/// 名字到函数体的查找口，**由原型侧填**。公共层里不出现任何一个函数名——
/// 这条是「各原型独自维护词表」唯一能被机器守住的形式（已删的设计档）。
/// </summary>
public sealed class CalcRegistry
{
    private readonly Dictionary<string, CalcFn> _fns = new(StringComparer.Ordinal);

    public CalcRegistry Register(string name, CalcFn fn)
    {
        if (!_fns.TryAdd(name, fn))
        {
            throw new KeyError($"Calc 重名 | {name}");
        }

        return this;
    }

    public CalcFn Get(string name)
    {
        if (!_fns.TryGetValue(name, out var fn))
        {
            throw new KeyError($"Calc 未注册 | {name} | 已注册 [{string.Join(",", _fns.Keys)}]");
        }

        return fn;
    }

    public bool Contains(string name) => _fns.ContainsKey(name);

    /// <summary>链式求值：从 seed 起，逐条把输出喂给下一条的 working。</summary>
    public double RunChain(IReadOnlyList<string> names, IReadOnlyDictionary<string, double> args,
        double seed, in CalcInput head)
    {
        var working = seed;
        for (var i = 0; i < names.Count; i++)
        {
            working = Get(names[i])(new CalcInput(head.Source, head.Target, head.TagCount,
                working, args, head.Tick));
        }

        return working;
    }

    /// <summary>把一条链的输入与输出拼成一行，供快照与台账念（F6 要的就是这一行）。</summary>
    public static string DescribeChain(IReadOnlyList<string> names, double seed, double result)
        => names.Count == 0
            ? string.Empty
            : $"{string.Join(">", names)}({Format(seed)}->{Format(result)})";

    public static string Format(double value) => AbilityNumbers.Format(value);

    public static double ParseDouble(string text) => AbilityNumbers.Parse(text);
}

/// <summary>配置与数据对不上时的报错：宁可当场炸，不要静默换算法。</summary>
public sealed class KeyError : Exception
{
    public KeyError(string message) : base(message)
    {
    }
}
