#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>一个槽位当前的样子 —— 设置屏那一格画的就是它。</summary>
/// <param name="Slot">槽号。</param>
/// <param name="Exists">这个槽位上有没有档。</param>
/// <param name="World">所属原型名（读文件头取来的）。</param>
/// <param name="Tick">写档那一刻的步数。</param>
/// <param name="WrittenAt">存盘时刻（取文件写下来的时刻）。</param>
/// <param name="Bytes">文件字节数。</param>
public sealed record SaveSlotInfo(
    int Slot,
    bool Exists,
    string World,
    long Tick,
    DateTimeOffset WrittenAt,
    long Bytes)
{
    /// <summary>没有档时给 UI 用的那一行（存盘时刻那一格显示「空」）。</summary>
    public string WhenText => Exists
        ? WrittenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : "空";

    /// <summary>读数（调试桥的存档读数靠它）。</summary>
    public string Readout()
        => Exists
            ? $"槽位 {Slot} {World} tick={Tick} {Bytes}B {WhenText}"
            : $"槽位 {Slot} 空";
}

/// <summary>
/// 存档库 —— 一叠槽位，按目录落盘。
///
/// <para><b>目录由调用方给</b>（Godot 侧递 <c>user://</c> 那一处的真实路径）。
/// 公共层不引引擎，于是这一整件在五条测试线里都能跑。</para>
///
/// <para><b>写盘走原子</b>：先写 <c>.tmp</c> 再改名。半份档比没有档更难查——
/// 读的那一侧看不出自己读到的是残件。</para>
///
/// <para><b>冻不冻时间不归这里</b>：写档那一刻世界必须冻着（复用暂停链），
/// 但那是原型答的。这里只管「点下去之后到写完之间别让人插队」。</para>
/// </summary>
public sealed class SaveStore
{
    private readonly string _directory;

    /// <summary>建一个存档库。目录不存在时第一次写档再建。</summary>
    public SaveStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("存档目录不能为空", nameof(directory));

        _directory = directory;
    }

    /// <summary>这一叠槽位落在哪个目录。</summary>
    public string Root => _directory;

    /// <summary>槽位的文件名（目录 + <c>slotN.json</c>）。</summary>
    public string PathOf(int slot)
    {
        if (slot < 1) throw new ArgumentOutOfRangeException(nameof(slot), "槽号从 1 起");
        return Path.Combine(_directory, $"slot{slot}.json");
    }

    /// <summary>这个槽位上有档吗。</summary>
    public bool HasSlot(int slot) => File.Exists(PathOf(slot));

    /// <summary>念一个槽位的清单。档坏了不抛 —— 设置屏要的是读数，不是崩溃。</summary>
    public SaveSlotInfo Probe(int slot)
    {
        var path = PathOf(slot);
        var info = new FileInfo(path);
        if (!info.Exists) return new SaveSlotInfo(slot, false, "-", 0, DateTimeOffset.MinValue, 0);

        try
        {
            var header = SaveFile.Split(File.ReadAllText(path));
            return new SaveSlotInfo(slot, true, header.World, header.Tick,
                info.LastWriteTimeUtc, info.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SaveFormatException)
        {
            return new SaveSlotInfo(slot, true, "<读不出>", 0, info.LastWriteTimeUtc, info.Length);
        }
    }

    /// <summary>枚举前 <paramref name="count"/> 个槽位 —— 接口要能枚举，多槽是增量。</summary>
    public IReadOnlyList<SaveSlotInfo> Enumerate(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "槽位数至少 1");
        return Enumerable.Range(1, count).Select(Probe).ToList();
    }

    /// <summary>
    /// 写一个槽位。超上限当场拒写（见 <see cref="SaveTooLargeException"/>），不落半份档。
    /// </summary>
    /// <param name="slot">槽号。</param>
    /// <param name="worldName">所属原型名，进文件头。</param>
    /// <param name="target">世界的存档那一半。</param>
    /// <param name="tick">写档那一刻的步数，进文件头。</param>
    public SaveSlotInfo Write(int slot, string worldName, ISaveable target, long tick)
    {
        ArgumentNullException.ThrowIfNull(target);

        var writer = new SaveWriter();
        target.WriteState(writer);
        var text = SaveFile.Compose(target.SchemaVersion, worldName, tick, writer.Result);

        var bytes = SaveText.ByteLength(text);
        if (bytes > SaveFile.MaxBytes) throw new SaveTooLargeException(bytes, SaveFile.MaxBytes, writer.Sections);

        Directory.CreateDirectory(_directory);
        var path = PathOf(slot);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
        return Probe(slot);
    }

    /// <summary>
    /// 读一个槽位进 <paramref name="target"/>。原型名不符当场报错，不读进半个世界。
    /// </summary>
    /// <param name="slot">槽号。</param>
    /// <param name="worldName">当前世界的名字，与档里比对。</param>
    /// <param name="target">世界的存档那一半。</param>
    public SaveHeader Read(int slot, string worldName, ISaveable target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = PathOf(slot);
        if (!File.Exists(path)) throw new SaveFormatException($"槽位 {slot} 上没有档");

        var header = SaveFile.Split(File.ReadAllText(path));
        if (!string.Equals(header.World, worldName, StringComparison.Ordinal))
            throw new SaveWorldMismatchException(worldName, header.World);

        header = SaveMigration.BringForward(header);
        var cursor = new SaveCursor(header.State);
        cursor.EnterObject();
        target.ReadState(new SaveReader(cursor));
        cursor.LeaveObject();
        cursor.ExpectEnd();
        return header;
    }

    /// <summary>读数 —— 调试桥的存档读数靠它。</summary>
    public string Readout(int count = 1)
        => $"{SaveMigration.Readout()} | 目录 {_directory} | " +
           string.Join(" ", Enumerate(count).Select(s => s.Readout()));
}
