#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>
/// 存档写侧游标 —— 手写 JSON 对象，成员按写入次序排定，不排序。
///
/// <para><b>输出即判据</b>：两串存档逐字相等，就是「同一份字段清单、同一批值」的直接证据
/// （往返测试）。少一个字段，两串立刻不等；多写一个派生数据，读回来再写一遍就不等。</para>
///
/// <para><b>键次序固定</b>：写侧不排序。同一个键写两次当场抛——
/// 那说明两份清单拼到了一起，而「哪一份是权威清单」正是这一层要挡的事。</para>
///
/// <para><b>记每段的字节数</b>：<see cref="Sections"/> 是体积上限那道报警线的凭据。
/// 超限时报错要说「哪一段最大」，光说「11MB」等于让读的人自己去猜。</para>
/// </summary>
public sealed class SaveWriter
{
    private readonly StringBuilder _body = new();
    private readonly List<SaveSectionBytes> _sections = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private int _members;

    /// <summary>写完的内容（含最外那对花括号）。</summary>
    public string Result => "{" + _body + "}";

    /// <summary>每个顶层键各占多少字节 —— 按 UTF-8 算，含键名本身，不含成员之间的逗号。</summary>
    public IReadOnlyList<SaveSectionBytes> Sections => _sections;

    public void Long(string key, long value) => Member(key, SaveText.Integer(value));

    public void Int(string key, int value) => Member(key, SaveText.Integer(value));

    public void Num(string key, double value) => Member(key, SaveText.Double(value, key));

    public void Float(string key, float value) => Member(key, SaveText.Single(value, key));

    public void Bool(string key, bool value) => Member(key, SaveText.Flag(value));

    public void Str(string key, string value) => Member(key, SaveText.Quoted(value ?? string.Empty));

    /// <summary>嵌套对象。段名与字节数记在外层。</summary>
    public void Obj(string key, Action<SaveWriter> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var inner = new SaveWriter();
        body(inner);
        Member(key, inner.Result);
    }

    /// <summary>数组。段名与字节数记在外层。</summary>
    public void Arr(string key, Action<SaveListWriter> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var inner = new SaveListWriter();
        body(inner);
        Member(key, inner.Result);
    }

    private void Member(string key, string rendered)
    {
        if (!_keys.Add(key))
            throw new InvalidOperationException($"存档里同一个键写了两遍：{key}（一份存档只能有一份权威字段清单）");

        if (_members > 0) _body.Append(',');
        var piece = SaveText.Quoted(key) + ":" + rendered;
        _body.Append(piece);
        _sections.Add(new SaveSectionBytes(key, SaveText.ByteLength(piece)));
        _members++;
    }
}

/// <summary>一段（一个顶层键）占多少字节 —— 体积上限报警时按它排序。</summary>
/// <param name="Key">段名，即写入时的那个键。</param>
/// <param name="Bytes">该段 UTF-8 字节数。</param>
public readonly record struct SaveSectionBytes(string Key, int Bytes);

/// <summary>
/// 数组写侧。元素按写入次序排定：集合那一档的次序由调用方定死（一般按 Id 序数排），
/// 这一层不替它排——排了就等于把「集合按什么序」的决定搬进通用件。
/// </summary>
public sealed class SaveListWriter
{
    private readonly StringBuilder _body = new();
    private int _items;

    /// <summary>写完的内容（含最外那对方括号）。</summary>
    public string Result => "[" + _body + "]";

    public void Long(long value) => Raw(SaveText.Integer(value));

    public void Int(int value) => Raw(SaveText.Integer(value));

    public void Num(double value) => Raw(SaveText.Double(value, "数组元素"));

    public void Float(float value) => Raw(SaveText.Single(value, "数组元素"));

    public void Bool(bool value) => Raw(SaveText.Flag(value));

    public void Str(string value) => Raw(SaveText.Quoted(value ?? string.Empty));

    /// <summary>数组里的一个对象。</summary>
    public void Obj(Action<SaveWriter> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var inner = new SaveWriter();
        body(inner);
        Raw(inner.Result);
    }

    private void Raw(string rendered)
    {
        if (_items > 0) _body.Append(',');
        _body.Append(rendered);
        _items++;
    }
}
