#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DemoFactory.Tools;

/// <summary>
/// 把各原型的 <c>i18n/&lt;原型&gt;.csv</c> 里<b>有内容的目标语言</b>登记进
/// <c>project.godot</c> 的 <c>[internationalization]</c> 段 —— 「翻译表怎么接上引擎」
/// 那一条的执行端。
///
/// <para><b>先跑引擎的导入，再跑本工具</b>：<c>.translation</c> 由
/// <c>godot --headless --path . --import</c> 从 CSV 产出，本工具不写那些二进制
/// （原因见文件末尾那条注记）。本工具只回答另一个问题：<b>哪些语言该被注册</b>。</para>
///
/// <para><b>为什么要有这个工具</b>：编辑器里那份 CSV 会自动导入并自动登记，但<b>无头环境
/// 没有编辑器</b>，于是「表存在」与「引擎读得到」在命令行下会脱节：表填好了、真机却仍
/// 渲染中文，而且没有任何报错 —— 查不到就回落到键本身（中文）。这正是「查不到不白屏」
/// 的设计代价：它让缺键看起来像「没翻译」，而不是像故障。</para>
///
/// <para><b>为什么只登记有内容的语言</b>：某语言整列全空时，引擎导入器会跳过它并打
/// <c>Locale 'ja' does not contain any translation</c>（实测）。那一档在
/// <c>locale_probe</c> 的「已注册译文语言」里看不到，正是「这一格还没有译文」的
/// 可读证据；硬登记一份全空的，切过去每格都回落成中文，读起来像「翻译是空的」
/// 而不是「没做」。</para>
///
/// <para><b>为什么 UTF-8 无 BOM 写 .godot</b>：带 BOM 的话第一行段名读不出来，
/// 整个文件被当二进制跳过，表现是「改了 project.godot 却没生效」。</para>
///
/// <para><b>用法</b>（仓库根）：<c>godot --headless --path . --import</c> 打头，
/// 然后 <c>dotnet run --project src/Tools -- register-translations</c>。
/// 幂等；<c>--force</c> 强制重写清单。</para>
/// </summary>
public static class RegisterTranslations
{
    /// <summary>表头第一列固定是 <c>keys</c>，其余每列是一个目标语言。</summary>
    private const string KeysColumn = "keys";

    public static int Run(string[] args)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("找不到仓库根（往上没摸到 DemoFactory.sln）");
            return 1;
        }

        var force = args.Contains("--force", StringComparer.Ordinal);
        var csvs = Directory
            .GetFiles(Path.Combine(repoRoot, "src", "Prototypes"), "*.csv", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)!) == "i18n")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (csvs.Count == 0)
        {
            Console.Error.WriteLine("src/Prototypes 下没有 i18n/*.csv —— i18n 落盘没做完");
            return 1;
        }

        var entries = new List<string>();
        foreach (var csv in csvs)
        {
            var proto = new DirectoryInfo(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(csv))!)).Name;
            entries.AddRange(WriteTranslationsFor(csv, repoRoot, proto, force));
        }

        var projectGodot = Path.Combine(repoRoot, "project.godot");
        if (!PatchTranslationsList(projectGodot, entries, force))
        {
            Console.Error.WriteLine($"project.godot 已是最新，无需改动：{string.Join(", ", entries)}");
            return 0;
        }

        Console.WriteLine($"已注册 {entries.Count} 份译文到 project.godot：");
        foreach (var e in entries)
        {
            Console.WriteLine($"  {e}");
        }

        return 0;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DemoFactory.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    // --- CSV 读：带引号字段，内部逗号不算分隔（与判据测试那份读取器同一条规矩） ---

    private sealed record Table(List<string> Locales, Dictionary<string, Dictionary<string, string>> ByLocale);

    private static Table ReadTable(string csv)
    {
        var lines = File.ReadAllLines(csv);
        if (lines.Length < 1)
        {
            throw new InvalidOperationException($"{csv} 是空文件");
        }

        var header = SplitFields(lines[0]);
        if (header.Count == 0 || header[0] != KeysColumn)
        {
            throw new InvalidOperationException($"{csv} 首列必须是 {KeysColumn}，实际 {header.FirstOrDefault()}");
        }

        var locales = header.Skip(1).ToList();
        var byLocale = locales.ToDictionary(l => l, _ => new Dictionary<string, string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = SplitFields(lines[i]);
            var key = fields[0];
            for (int c = 1; c < locales.Count && c < fields.Count; c++)
            {
                byLocale[locales[c - 1]][key] = fields[c];
            }
        }

        return new Table(locales, byLocale);
    }

    private static List<string> SplitFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = false;
                    continue;
                }

                sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    continue;
                case ',':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    continue;
                default:
                    sb.Append(c);
                    continue;
            }
        }

        fields.Add(sb.ToString());
        return fields;
    }

    // --- .translation 写：Godot 的二进制资源格式（头部 + 键值对 + 末尾 0 终止） ---

    private static List<string> WriteTranslationsFor(string csv, string repoRoot, string proto, bool force)
    {
        var table = ReadTable(csv);
        var written = new List<string>();

        foreach (var locale in table.Locales)
        {
            var pairs = table.ByLocale[locale];
            if (pairs.Values.All(string.IsNullOrEmpty))
            {
                // 全空 = 这一格还没有译文。不生成、不注册：注册了会在切语言时静默回落成中文，
                // 读起来像「翻译是空的」而不是「没做」。它该在 locale_probe 里缺席。
                continue;
            }

            var outPath = Path.Combine(repoRoot, Path.GetDirectoryName(csv)!,
                $"{Path.GetFileNameWithoutExtension(csv)}.{locale}.translation");
            written.Add($"res://{Path.GetRelativePath(repoRoot, outPath).Replace('\\', '/')}");
        }

        Console.WriteLine($"[{proto}] 表 {csv}：{table.Locales.Count} 个目标语言，" +
                          $"{written.Count} 份要注册（全空的语言跳过）");
        if (written.Count > 0)
        {
            Console.WriteLine($"[{proto}] .translation 由引擎的 CSV 导入器产出（Godot --headless --import），" +
                              "本工具只负责挑出哪些语言该注册 —— 手写那份不是引擎认的格式" +
                              "（实测：手写二进制报 Unrecognized binary resource file）");
        }

        return written;
    }

    // 注：这里曾经有一份手写 .translation 编码器（「GDST」魔数 + 长度前缀那条路），
    // 已删。实测引擎不认那份产物：
    //   ERROR: Unrecognized binary resource file: 'res://.../<原型>.en.translation'
    // 真格式是 ResourceLoader 的二进制资源（头部 "RSRC" + OptimizedTranslation + 哈希桶 +
    // 字符串池），不是 gettext 那类自定义容器。自己重编一份「看起来对」的二进制，
    // 是在造一个只有自己能读、引擎读不了的文件 —— 比不生成更坏。
    // .translation 一律由 `godot --headless --path . --import` 从 CSV 产出。

    // --- project.godot 改写：只在 [internationalization] 段的 translations 列表里动 ---

    private static bool PatchTranslationsList(string projectGodot, List<string> entries, bool force)
    {
        var lines = File.ReadAllLines(projectGodot);
        var section = Array.FindIndex(lines, l => l.TrimStart().StartsWith("[internationalization]", StringComparison.Ordinal));
        if (section < 0)
        {
            // 幂等：清单已经一模一样就别再追加一段。缺这一句时，每跑一次工具就在文件末尾
            // 多一段 [internationalization]，同一段里出现两个同名键，Godot 读文件直接报
            // "Error parsing 'project.godot'" 并整份跳过（实测跑两次就踩到）。
            // 判据用「有没有任一条已登记」，与下面那条分支同口径。
            if (!force && entries.All(e => lines.Any(l => l.Contains(e, StringComparison.Ordinal))))
            {
                return false;
            }

            // 段不存在就整段追加在文件末尾：Godot 读段不认顺序。
            var appended = new List<string> { "", "[internationalization]", "", "locale/translations=PackedStringArray(" };
            appended.AddRange(entries.Select(e => "\"" + e + "\","));
            appended.Add(")");
            File.WriteAllLines(projectGodot, lines.Concat(appended),
                new System.Text.UTF8Encoding(false));
            return true;
        }

        // 从段名**下一行**起扫：段名行本身就以 "[" 开头，从 section 起会在第一轮就 break，
        // keyIndex 永远是 -1，于是每次都走「段在、键不在」那条分支无条件插一条 ——
        // 同一段里同名键无限增殖（实测跑三次就三份，Godot 整份文件解析失败）。
        var keyIndex = -1;
        for (int i = section + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                break; // 出了本段
            }

            if (lines[i].StartsWith("locale/translations=", StringComparison.Ordinal))
            {
                keyIndex = i;
                break;
            }
        }

        if (keyIndex < 0)
        {
            var block = new List<string> { "", "locale/translations=PackedStringArray(" };
            block.AddRange(entries.Select(e => "\"" + e + "\","));
            block.Add(")");
            lines = lines.Take(section + 1).Concat(block)
                .Concat(lines.Skip(section + 1)).ToArray();
            File.WriteAllLines(projectGodot, lines, new System.Text.UTF8Encoding(false));
            return true;
        }

        // 幂等：所有待登记条目都已经在清单里就别重写。
        // 判据方向必须是「entries 里每一条都见于块内」。写成反过来（块内每行都要含某个条目）
        // 会恒假 —— 块里那行 ")" 和空行本来就不含任何条目，于是每跑一次工具都重写一遍，
        // 而重写路径又要处理重复键，等于自己给自己制造那个坑（实测：跑两次就出两个同名段）。
        var blockLines = lines.Skip(keyIndex).TakeWhile(l => !l.TrimStart().StartsWith("[", StringComparison.Ordinal)).ToList();
        if (!force && entries.All(e => blockLines.Any(l => l.Contains(e, StringComparison.Ordinal))))
        {
            return false; // 全都已注册
        }

        var rebuilt = new List<string> { "locale/translations=PackedStringArray(" };
        rebuilt.AddRange(entries.Select(e => "\"" + e + "\","));
        rebuilt.Add(")");

        // 单遍扫出「本段内所有 locale/translations= 键及其多行值的范围」，只留第一个键的
        // 位置写重建块，其余同名块整块丢掉。
        //
        // 为什么必须清重复：Godot 同一段里读到第二个同名键就整份文件解析失败
        // （实测 "Error parsing 'project.godot' ... error code 43"），而工具那边照样打印
        // 「已注册 N 份」—— 崩的是引擎读文件那一步，离症状最远。
        // 早先的写法只认第一个键（keyIndex），后面的永远留着；跑第二次工具又会在文件末尾
        // 追加一整段，是同一种病的两条来路，所以两处都堵。
        var keyStarts = new List<int>();
        for (int i = section + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                break; // 出了本段
            }

            if (lines[i].StartsWith("locale/translations=", StringComparison.Ordinal))
            {
                keyStarts.Add(i);
            }
        }

        // 每个键块的范围：从该行起到下一个键块或下一个段名为止。
        var drop = new HashSet<int>();
        for (int k = 1; k < keyStarts.Count; k++)
        {
            for (int i = keyStarts[k]; i < (k + 1 < keyStarts.Count ? keyStarts[k + 1] : lines.Length); i++)
            {
                drop.Add(i);
            }
        }

        // 还要确认「第一个键块」恰好到段尾或下一个键块（下面 while 用的就是这个上界）。
        var firstEnd = keyStarts.Count > 1 ? keyStarts[1] : lines.Length;
        while (firstEnd - 1 > keyStarts[0] && lines[firstEnd - 1].TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            firstEnd--;
        }

        var output = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (drop.Contains(i))
            {
                continue;
            }

            if (i == keyStarts[0])
            {
                output.AddRange(rebuilt);
                i = firstEnd - 1; // 跳过旧块（含收尾的 ")"）
                continue;
            }

            output.Add(lines[i]);
        }

        lines = output.ToArray();
        File.WriteAllLines(projectGodot, lines, new System.Text.UTF8Encoding(false));
        return true;
    }
}
