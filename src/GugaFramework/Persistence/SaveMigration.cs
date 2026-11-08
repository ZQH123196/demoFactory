#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>迁移表里的一条：从某个版本改到下一个版本的函数。</summary>
/// <param name="From">档里的版本号。</param>
/// <param name="Raise">改成下一版的那一步。返回改好的文件头（<c>Version</c> 要加一）。</param>
public sealed record SaveMigrationEntry(int From, Func<SaveHeader, SaveHeader> Raise);

/// <summary>
/// 版本迁移链 —— 留一张空表 + 一套分派。
///
/// <para><b>第一版只有 v1，表却是先在的</b>：改第一个字段时没有插入点就晚了。
/// 到那一步只要往 <see cref="Table"/> 加一条 <see cref="SaveMigrationEntry"/>，
/// 逻辑层一行不动。</para>
///
/// <para><b>档比当前新，当场报错不猜</b>：拿一台旧机器的档去新档上跑，那份档的字段
/// 这一版根本没见过，硬读出来的世界没有意义。</para>
/// </summary>
public static class SaveMigration
{
    /// <summary>
    /// 迁移表（空）。按 <c>From</c> 查第一条匹配的 <see cref="SaveMigrationEntry"/>，
    /// 查到就调，循环调到 <see cref="SaveFile.CurrentVersion"/>。
    /// </summary>
    public static IReadOnlyList<SaveMigrationEntry> Table { get; } = Array.Empty<SaveMigrationEntry>();

    /// <summary>把一份档改到当前版本。已是当前版本时原样交出。</summary>
    /// <exception cref="SaveVersionTooNewException">档比当前代码新。</exception>
    /// <exception cref="SaveMigrationMissingException">迁移表里缺从那个版本到当前版本的某一步。</exception>
    public static SaveHeader BringForward(SaveHeader header)
    {
        var current = header;
        var guard = 0;
        while (current.Version != SaveFile.CurrentVersion)
        {
            if (current.Version > SaveFile.CurrentVersion)
                throw new SaveVersionTooNewException(current.Version, SaveFile.CurrentVersion);

            var step = Table.FirstOrDefault(t => t.From == current.Version);
            if (step is null) throw new SaveMigrationMissingException(current.Version, SaveFile.CurrentVersion);

            current = step.Raise(current) with { Version = current.Version + 1 };
            if (++guard > 64) throw new InvalidOperationException("迁移链转了 64 步还没到当前版本：迁移表里有环");
        }

        return current;
    }

    /// <summary>读数 —— 迁移表现在有几条、从哪几版到哪几版。</summary>
    public static string Readout()
        => Table.Count == 0
            ? $"迁移表空（当前 v{SaveFile.CurrentVersion}）"
            : $"迁移表 {Table.Count} 条：" +
              string.Join("、", Table.OrderBy(t => t.From).Select(t => $"v{t.From}→v{t.From + 1}"));
}
