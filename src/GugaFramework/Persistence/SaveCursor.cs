#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>
/// 手写 JSON 游标 —— <see cref="SaveReader"/> 与 <see cref="SaveListReader"/> 共用那一个。
///
/// <para><b>只走五种值</b>：对象 / 数组 / 字符串 / 数字 / 布尔。存档用不到别的东西，
/// 而「用不到」是这一层能保持手写的唯一理由。</para>
///
/// <para><b>词法与语法都判</b>：缺键、类型不对、括号不配、串未闭合，全部当场抛并带上
/// 读到的那一段文本。存档读坏了之后的每一次故障都从「读到 X，此处该是 Y」开始查，
/// 抛一句「格式错误」等于把这条线索掐掉。</para>
/// </summary>
internal sealed class SaveCursor
{
    private readonly string _text;
    private int _pos;

    public SaveCursor(string text, int pos = 0)
    {
        _text = text;
        _pos = pos;
    }

    /// <summary>下一个键名，不消费它。对象已读完时返回空串。</summary>
    public string PeekKey()
    {
        SkipSeparator();
        if (AtEnd() || PeekRaw() == '}') return string.Empty;

        var mark = _pos;
        var key = ReadKey();
        _pos = mark; // 「看一眼」不许把游标往前带 —— 带走了这一段就再也读不到
        return key;
    }

    /// <summary>只核对键，不消费它的值。后面紧跟 <see cref="EnterObject"/> /
    /// <see cref="EnterArray"/> 或 <see cref="ValueText"/> 时用这一条。</summary>
    public void Key(string key)
    {
        SkipSeparator();
        RequireKey(key);
    }

    /// <summary>读一个具名标量：核对键、吃冒号、返回字面量文本。</summary>
    public string Scalar(string key)
    {
        SkipWs();
        RequireKey(key);
        return RawValue();
    }

    /// <summary>读一个无名标量（数组元素）。</summary>
    public string Scalar()
    {
        SkipSeparator();
        return RawValue();
    }

    /// <summary>读一个具名字符串值（解码转义）。</summary>
    public string Text(string key)
    {
        SkipWs();
        RequireKey(key);
        return ReadStringChecked();
    }

    /// <summary>读一个无名字符串值。</summary>
    public string Text()
    {
        SkipSeparator();
        return ReadStringChecked();
    }

    private string ReadStringChecked()
    {
        if (PeekRaw() != '"') throw new FormatException($"{Describe()}：该是一个字符串，读到 {Excerpt()}");
        return ReadString();
    }

    public void EnterObject()
    {
        SkipSeparator();
        Expect('{');
    }

    public void LeaveObject()
    {
        SkipSeparator(); // 最后一个成员后面还留着一个逗号 —— 收尾这一格先吃掉它
        Expect('}');
    }

    public void EnterArray()
    {
        SkipSeparator();
        Expect('[');
    }

    /// <summary>记下当前位置（文件头那一层用它把状态段整段原样交出去）。</summary>
    public int Mark() => _pos;

    /// <summary>回到 <see cref="Mark"/> 记下的位置。</summary>
    public void Rewind(int mark) => _pos = mark;

    /// <summary>当前位置往后的全部原文。</summary>
    public string Remaining() => _text[_pos..];

    /// <summary>
    /// 把当前位置那个值的原文整段交出去，游标停在它之后（成对的括号一个不漏）。
    ///
    /// <para>文件头那一层用它把状态段原样递给原型那一半——文件头不认状态里的字段，
    /// 所以不能逐键解析，只能整段搬走。</para>
    /// </summary>
    public string ValueText()
    {
        SkipSeparator();
        if (AtEnd()) throw new FormatException($"{Describe()}：该有一个值，读到 {Excerpt()}");

        var start = _pos;
        if (PeekRaw() is '{' or '[') ScanBalanced();
        else RawValue();
        return _text[start.._pos];
    }

    private void ScanBalanced()
    {
        var depth = 0;
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == '"')
            {
                ReadString(); // 字符串里的括号不算 —— 跳过整串就够，不必逐字符判
                continue;
            }

            if (c is '{' or '[') depth++;
            else if (c is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    _pos++;
                    return;
                }
            }

            _pos++;
        }

        throw new FormatException($"{Describe()}：括号没有收尾（{Excerpt()}）");
    }

    /// <summary>档应当在哪儿结束。多出来的东西当场判否。</summary>
    public void ExpectEnd()
    {
        SkipWs();
        if (!AtEnd()) throw new FormatException($"{Describe()}：档的末尾还有没读完的内容（{Excerpt()}）");
    }

    public bool AtArrayEnd()
    {
        SkipSeparator();
        return AtEnd() || PeekRaw() == ']';
    }

    public void LeaveArray()
    {
        SkipSeparator();
        Expect(']');
    }

    // === 词法 ===

    private char SkipWs()
    {
        while (_pos < _text.Length && (_text[_pos] is ' ' or '\t' or '\r' or '\n')) _pos++;
        return AtEnd() ? '\0' : _text[_pos];
    }

    /// <summary>
    /// 吃掉成员之间的那个逗号（最多一个）。
    ///
    /// <para><b>为什么值读完之后才吃</b>：<c>RawValue</c> 靠逗号判断值到哪儿为止，
    /// 所以分隔符只能由下一次读入口自己吃掉。这一处是唯一吃逗号的地方，
    /// 散在各处各吃一次的结果是「多读一个元素」这种静默分叉。</para>
    /// </summary>
    private void SkipSeparator()
    {
        SkipWs();
        if (!AtEnd() && PeekRaw() == ',') _pos++;
        SkipWs();
    }

    private char PeekRaw() => _text[_pos];

    private bool AtEnd() => _pos >= _text.Length;

    private void Expect(char expected)
    {
        SkipWs();
        if (AtEnd()) throw new FormatException($"{Describe()}：档在 {Describe()} 处就断了，此处该是 '{expected}'");
        var c = _text[_pos++];
        if (c != expected)
            throw new FormatException($"{Describe()}：此处该是 '{expected}'，读到 '{c}'（{Excerpt()}）");
    }

    private void RequireKey(string key)
    {
        SkipSeparator();
        if (AtEnd()) throw new FormatException($"{Describe()}：档在此处就断了，此处该是键 {key}");
        if (PeekRaw() == '}')
            throw new FormatException($"{Describe()}：这一段读完了，但还在找键 {key}（少了一个字段）");

        var found = ReadKey();
        if (!string.Equals(found, key, StringComparison.Ordinal))
            throw new FormatException($"{Describe()}：此处该是键 {key}，档里写的是 {found}");

        Expect(':');
    }

    private string ReadKey()
    {
        var c = PeekRaw();
        if (c != '"') throw new FormatException($"{Describe()}：该是一个键名，读到 {Excerpt()}");
        var key = ReadString();
        return key;
    }

    private string RawValue()
    {
        var start = _pos;
        while (_pos < _text.Length && _text[_pos] is not (',' or '}' or ']' or ' ' or '\t' or '\r' or '\n')) _pos++;
        if (_pos == start) throw new FormatException($"{Describe()}：该有一个值，读到 {Excerpt()}");
        return _text[start.._pos];
    }

    private string ReadString()
    {
        _pos++; // 开引号
        var sb = new StringBuilder();
        while (true)
        {
            if (_pos >= _text.Length) throw new FormatException($"{Describe()}：字符串没有收尾引号");
            var c = _text[_pos++];
            if (c == '"') return sb.ToString();
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (_pos >= _text.Length) throw new FormatException($"{Describe()}：转义符后面没有了");
            var esc = _text[_pos++];
            sb.Append(esc switch
            {
                '"' => '"',
                '\\' => '\\',
                '/' => '/',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'b' => '\b',
                'f' => '\f',
                'u' => ReadUnicode(),
                _ => throw new FormatException($"{Describe()}：不认的转义 \\{esc}"),
            });
        }
    }

    private char ReadUnicode()
    {
        if (_pos + 4 > _text.Length) throw new FormatException($"{Describe()}：\\u 后面不足四位");
        var hex = _text.Substring(_pos, 4);
        _pos += 4;
        if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            throw new FormatException($"{Describe()}：\\u{hex} 不是四个十六进制位");

        return (char)code;
    }

    /// <summary>出错时指位置用：出错的键名（若有）与字符偏移。</summary>
    private string Describe() => $"存档偏移 {_pos}";

    /// <summary>出错时贴一小段原文，省得读的人回去翻文件。</summary>
    private string Excerpt()
    {
        var from = Math.Max(0, _pos - 12);
        var len = Math.Min(24, _text.Length - from);
        return len <= 0 ? "<空>" : "'" + _text.Substring(from, len).Replace("\n", "\\n").Replace("\r", "\\r") + "'";
    }
}
