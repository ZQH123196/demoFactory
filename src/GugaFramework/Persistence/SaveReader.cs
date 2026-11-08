#nullable enable
using System;

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>
/// 存档读侧游标 —— 与 <see cref="SaveWriter"/> 一一对应的读法。
///
/// <para><b>按键顺序读，不按键查找</b>：读到别的键上就当场抛，并把两个键名都念出来。
/// 这不是刁难——「读档之后某个字段没读回来」那一类故障最省钱的查法就是让游标当场说出
/// 「这里该是 <c>hp</c>，档里写的是 <c>gold</c>」，而按键查找会把那句话说成
/// 「某个键没读到」，读的人得回头逐个键比。</para>
///
/// <para><b>读不到就抛，不兜默认值</b>：缺字段是「这份档不是这份代码写的」，
/// 兜 0 只会把它变成一个跑起来才显形的世界。</para>
/// </summary>
public sealed class SaveReader
{
    private readonly SaveCursor _cursor;

    internal SaveReader(SaveCursor cursor) => _cursor = cursor;

    /// <summary>下一个键。清单盘点用（守卫、迁移、调试读数），读值仍走按键的那些口。</summary>
    public string PeekKey() => _cursor.PeekKey();

    public long Long(string key) => SaveText.ParseInteger(_cursor.Scalar(key), key);

    public int Int(string key)
    {
        var value = SaveText.ParseInteger(_cursor.Scalar(key), key);
        if (value is < int.MinValue or > int.MaxValue)
            throw new FormatException($"{key}：读到 {value}，装不进 int");
        return (int)value;
    }

    public double Num(string key) => SaveText.ParseDouble(_cursor.Scalar(key), key);

    public float Float(string key) => (float)SaveText.ParseDouble(_cursor.Scalar(key), key);

    public bool Bool(string key) => _cursor.Scalar(key) switch
    {
        "true" => true,
        "false" => false,
        var other => throw new FormatException($"{key}：读到 {other}，那不是一个布尔"),
    };

    public string Str(string key) => _cursor.Text(key);

    /// <summary>嵌套对象。回调返回时游标已经站在这一段之后。</summary>
    public void Obj(string key, Action<SaveReader> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _cursor.Key(key);
        _cursor.EnterObject();
        body(new SaveReader(_cursor));
        _cursor.LeaveObject();
    }

    /// <summary>数组。回调返回时游标已经站在这一段之后。</summary>
    public void Arr(string key, Action<SaveListReader> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _cursor.Key(key);
        _cursor.EnterArray();
        body(new SaveListReader(_cursor));
        _cursor.LeaveArray();
    }
}

/// <summary>
/// 数组读侧。元素按写入次序读出去，用 <see cref="Done"/> 判断是否读完。
///
/// <para>不提供「第 n 个」与「按索引」：集合那一档的次序由写侧定死，
/// 读侧按同一份次序读，读出来立刻就是写进去的那一份。</para>
/// </summary>
public sealed class SaveListReader
{
    private readonly SaveCursor _cursor;

    internal SaveListReader(SaveCursor cursor) => _cursor = cursor;

    /// <summary>这一段数组读到头了。</summary>
    public bool Done => _cursor.AtArrayEnd();

    public long Long() => SaveText.ParseInteger(_cursor.Scalar(), "数组元素");

    public int Int()
    {
        var value = SaveText.ParseInteger(_cursor.Scalar(), "数组元素");
        if (value is < int.MinValue or > int.MaxValue)
            throw new FormatException($"数组元素：读到 {value}，装不进 int");
        return (int)value;
    }

    public double Num() => SaveText.ParseDouble(_cursor.Scalar(), "数组元素");

    public float Float() => (float)SaveText.ParseDouble(_cursor.Scalar(), "数组元素");

    public bool Bool() => _cursor.Scalar() switch
    {
        "true" => true,
        "false" => false,
        var other => throw new FormatException($"数组元素：读到 {other}，那不是一个布尔"),
    };

    public string Str() => _cursor.Text();

    /// <summary>数组里的一个对象。回调返回时游标已经站在这一个之后。</summary>
    public void Obj(Action<SaveReader> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _cursor.EnterObject();
        body(new SaveReader(_cursor));
        _cursor.LeaveObject();
    }
}
