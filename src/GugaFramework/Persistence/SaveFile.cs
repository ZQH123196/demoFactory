#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>存档文件头拆出来的那几项。</summary>
/// <param name="Version">迁移链的钥匙。</param>
/// <param name="World">所属原型名。读档先比对，不对当场报错。</param>
/// <param name="Tick">写档那一刻的步数。</param>
/// <param name="State">状态段原文（一个 JSON 对象，交给 <see cref="SaveReader"/> 逐段读）。</param>
public readonly record struct SaveHeader(int Version, string World, long Tick, string State);

/// <summary>
/// 存档文件 —— 文件头那四个键的拼与拆。
///
/// <para><b>形状</b>：<c>{"v":1,"world":"&lt;原型名&gt;","tick":4000,"state":{…}}</c>。
/// <b>只有这四个键。</b>不写 <c>seed</c>（存档是全量状态，读档后世界已经是那一刻的样子）；
/// 不写 <c>bytes</c>（那是派生数据，写进去就多一份能漂的读数）。</para>
///
/// <para><b>末尾带一个换行</b>：落盘件按仓库文本口径收尾。少它的话每次存档都把工作区弄脏一次，
/// 而那种「文件只差一个换行」的差异会在版本库里刷屏（与 <c>ReplayScriptJson.Write</c> 同一笔账）。
/// 读端逐处跳空白，多这一个换行不影响解析。</para>
/// </summary>
public static class SaveFile
{
    /// <summary>当前文件头版本。第一版只有 v1，迁移表先在（见 <see cref="SaveMigration"/>）。</summary>
    public const int CurrentVersion = 1;

    /// <summary>硬上限 10MB。它是开发期报警线，不是容量预算。</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>拼一整份存档文本。</summary>
    public static string Compose(int version, string world, long tick, string state)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(state);

        var sb = new StringBuilder(state.Length + 96);
        sb.Append(CultureInfo.InvariantCulture, $"{{\"v\":{version},\"world\":{SaveText.Quoted(world)}");
        sb.Append(CultureInfo.InvariantCulture, $",\"tick\":{tick},\"state\":{state}}}\n");
        return sb.ToString();
    }

    /// <summary>
    /// 拆一份存档文本。状态段原样交出（不解析）——
    /// 文件头这一层不认状态里的任何字段，那是原型那一半的事。
    /// </summary>
    /// <exception cref="SaveFormatException">不是一份本模块写的存档。</exception>
    public static SaveHeader Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var cursor = new SaveCursor(text);
        try
        {
            cursor.EnterObject();
            var version = (int)SaveText.ParseInteger(cursor.Scalar("v"), "v");
            var world = cursor.Text("world");
            var tick = SaveText.ParseInteger(cursor.Scalar("tick"), "tick");
            cursor.Key("state");
            var state = cursor.ValueText();
            cursor.LeaveObject();
            cursor.ExpectEnd();
            return new SaveHeader(version, world, tick, state);
        }
        catch (FormatException e)
        {
            throw new SaveFormatException($"存档文件头不对：{e.Message}", e);
        }
    }
}

/// <summary>存档读不成 / 不是本模块写的格式。</summary>
public sealed class SaveFormatException : Exception
{
    public SaveFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>读到了别的原型的档。</summary>
public sealed class SaveWorldMismatchException : Exception
{
    public SaveWorldMismatchException(string expected, string found)
        : base($"这份档属于 {found}，当前世界是 {expected} —— 读档先比对原型名，不对当场报错")
    {
        Expected = expected;
        Found = found;
    }

    /// <summary>当前世界的名字。</summary>
    public string Expected { get; }

    /// <summary>档里写的名字。</summary>
    public string Found { get; }
}

/// <summary>档比当前版本新 —— 这台代码读不了它。</summary>
public sealed class SaveVersionTooNewException : Exception
{
    public SaveVersionTooNewException(int found, int current)
        : base($"这份档是 v{found} 写的，当前代码只读到 v{current}：换新版本的游戏才能读它")
    {
        Found = found;
        Current = current;
    }

    /// <summary>档里的版本号。</summary>
    public int Found { get; }

    /// <summary>当前代码的版本号。</summary>
    public int Current { get; }
}

/// <summary>没有迁移函数能把这份档改到当前版本。</summary>
public sealed class SaveMigrationMissingException : Exception
{
    public SaveMigrationMissingException(int from, int current)
        : base($"迁移表里没有从 v{from} 到 v{current} 的函数：这一版改了存档字段，但没补迁移")
    {
        From = from;
        Current = current;
    }

    /// <summary>档里的版本号。</summary>
    public int From { get; }

    /// <summary>当前代码的版本号。</summary>
    public int Current { get; }
}

/// <summary>存档超出体积上限当场拒写。</summary>
public sealed class SaveTooLargeException : Exception
{
    public SaveTooLargeException(long bytes, long limit, IReadOnlyList<SaveSectionBytes> sections)
        : base(BuildMessage(bytes, limit, sections))
    {
        Bytes = bytes;
        Limit = limit;
        Sections = sections;
    }

    /// <summary>这一份有多大（字节）。</summary>
    public long Bytes { get; }

    /// <summary>上限（字节）。</summary>
    public long Limit { get; }

    /// <summary>各段字节数（按写入次序）。</summary>
    public IReadOnlyList<SaveSectionBytes> Sections { get; }

    private static string BuildMessage(long bytes, long limit, IReadOnlyList<SaveSectionBytes> sections)
    {
        // 先全排序再取前三 —— 先取前三再排序排出来的永远是文件里最靠前的三段，
        // 而「哪一段最大」这条报警要的恰恰是别的那三段。
        var ranked = new List<SaveSectionBytes>(sections);
        ranked.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

        var take = Math.Min(3, ranked.Count);
        var top = new string[take];
        for (var i = 0; i < take; i++) top[i] = $"{ranked[i].Key} {ranked[i].Bytes / 1024.0:F1}KB";

        var detail = take == 0 ? "（没读到分段）" : string.Join("、", top);
        return $"这份存档 {bytes / 1024.0 / 1024.0:F2}MB，超了 {limit / 1024.0 / 1024.0:F0}MB 上限，当场拒写。最大的几段：{detail}";
    }
}
