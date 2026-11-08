#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DemoFactory.GugaFramework.Guard;
using Shouldly;
using Xunit;

namespace DemoFactory.Tests;

/// <summary>
/// i18n 的判据检查 —— 把「上屏中文进表、键不烂在表里」钉住。
///
/// <para><b>键就是中文原文</b>（用户裁决 Q-I）。取字入口 <c>Loc.T</c> / <c>Loc.Fmt</c> 在
/// <c>GugaFramework/I18n/</c>；每原型一张表 <c>i18n/&lt;原型&gt;.csv</c>，首列即中文、目标语言列留空，
/// 交给译者填。框架那几个字另有它自己一张表 <c>FrontEnd/i18n/frontend.csv</c>。
/// 本件只保证「代码与表互相覆盖」，不校验译文本身（还没有译文）。
///
/// <para><b>为什么不做「上屏裸中文一律判红」那种全扫描</b>：静态分不出「这句中文会进 Label
/// 还是会进 Log」——同在 <c>Scripts/</c> 顶层的 <c>.Text = "中文"</c>（上屏）与 <c>GD.Print("中文")</c>
/// （只进日志）在源码上长得一样，全扫必然把日志判成上屏，那是假红。假红的检查会被 agent 学会绕过，
/// 比没有检查更糟（同一个坑在「断言三层」里已经记过）。所以这里只钉<b>能判定</b>的那几件事。</para>
///
/// <para><b>七条判据，分两组</b>。前四条按<b>原型</b>组织（<see cref="DiscoverProtos"/> 扫
/// <c>src/Prototypes/*/i18n/</c>）：① 表头对不对；② 表里有没有<b>孤儿键</b>（表里有、整个原型源码与
/// 场景里都找不到的键）；③ 代码里 <c>Loc.T(</c>/<c>Loc.Fmt(</c> 传的中文键、以及<b>直接赋给文本属性的
/// 裸中文</b>，有没有漏进表。后三条按<b>框架</b>组织（<c>frontend.csv</c>）：表头、孤儿键、漏键。
/// 后三条单独一组，是因为前端那几个字不隶属任何原型（「原型隔离」），
/// <see cref="DiscoverProtos"/> 扫不到那张表；不补这一格的症状是前端层改了字、表没跟，两边都绿，
/// 译者拿到的是一张过期的表。</para>
///
/// <para>「逻辑层数据经变量上屏」（如兵种名牌 <c>Loc.T(card.DisplayName)</c>）那条：字面量在
/// <c>Scripts/Logic/</c> 里，由第 ② 条兜住（它按「出现在源码里」判，不要求是 Loc 键），静态不再单独判。
///
/// <para><b>纯 C#</b>：本件不引用 Godot 命名空间（五个测试工程都不引 Godot.NET.Sdk），
/// 只用 <see cref="TestPaths"/> 定位仓库根、读源码与 CSV 当文本。
/// </para>
/// </summary>
public sealed class I18nRuleTests
{
    private const string ExpectedHeader = "keys,en,ja,fr,de,es,ru,ko";

    // ---- CSV：首行表头 + 首个字段（键）。键用双引号包住且<b>内部可能含逗号</b>
    // （如 `定价 ${13}(, / .)`、`{2:0.00}, {3:0.00}`），所以必须按带引号字段读，
    // 不能用「第一个逗号」切——那样会把这些键从内部截断，导致与代码对不上而误判孤儿。
    private static (List<string> Keys, string Header) ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = lines.Length > 0 ? lines[0].Trim() : "";
        var keys = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            keys.Add(ReadFirstField(line));
        }

        return (keys, header);
    }

    private static string ReadFirstField(string line)
    {
        var s = line.TrimStart();
        if (!s.StartsWith('"'))
        {
            var comma = s.IndexOf(',');
            return (comma >= 0 ? s[..comma] : s).Trim();
        }

        var sb = new StringBuilder();
        int i = 1; // 跳过开引号
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }

                break; // 收尾引号
            }

            sb.Append(s[i]);
            i++;
        }

        return sb.ToString();
    }

    // ---- C# 源码扫描 ----
    //
    // Literal 记四件事：内容 Text、字面量「起始下标」Start（回溯判定它是不是 Loc 首个参数 /
    // 文本赋值的右值）、「收尾引号下标」EndQuote（沿 + 拼接续读用）、是否插值串。处理普通串、逐字串、
    // 插值串 $"..."{洞}、原始串 """..."""；注释整段跳过；洞里的嵌套引号不算串尾。
    private readonly record struct Literal(string Text, int Start, int EndQuote, bool Interpolated);

    private static bool IsCjk(char c)
        => (c >= '\u4E00' && c <= '\u9FFF')      // 表意文字
           || (c >= '\u3000' && c <= '\u303F')   // CJK 标点 。、「」【】
           || (c >= '\uFF01' && c <= '\uFF65');  // 全角标点/假名 ，：

    private static bool HasCjk(string s) => s.Any(IsCjk);

    private static List<Literal> ScanLiterals(string src)
    {
        var list = new List<Literal>();
        int i = 0, n = src.Length;
        while (i < n)
        {
            char c = src[i];

            if (c == '/' && i + 1 < n && src[i + 1] == '/')
            {
                while (i < n && src[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/'))
                {
                    i++;
                }

                i += 2;
                continue;
            }

            bool verbatim = false, interp = false;
            int p = i;
            if (p < n && src[p] == '@')
            {
                verbatim = true;
                p++;
            }

            if (p < n && src[p] == '$')
            {
                interp = true;
                p++;
                if (p < n && src[p] == '@')
                {
                    verbatim = true;
                    p++;
                }
            }

            if (p + 2 < n && src[p] == '"' && src[p + 1] == '"' && src[p + 2] == '"')
            {
                int contentStart = p + 3;
                int q = contentStart;
                while (q + 2 < n && !(src[q] == '"' && src[q + 1] == '"' && src[q + 2] == '"'))
                {
                    q++;
                }

                list.Add(new Literal(src[contentStart..Math.Min(q, n)], i, Math.Min(q, n), interp));
                i = Math.Min(q + 3, n);
                continue;
            }

            if (p < n && src[p] == '"')
            {
                int start = i;
                int contentStart = p + 1;
                int q = contentStart;
                int depth = 0;
                while (q < n)
                {
                    char d = src[q];
                    if (verbatim)
                    {
                        if (d == '"')
                        {
                            if (q + 1 < n && src[q + 1] == '"')
                            {
                                q += 2;
                                continue;
                            }

                            break;
                        }

                        q++;
                        continue;
                    }

                    if (d == '\\')
                    {
                        q += 2;
                        continue;
                    }

                    if (interp)
                    {
                        if (d == '{')
                        {
                            depth++;
                            q++;
                            continue;
                        }

                        if (d == '}' && depth > 0)
                        {
                            depth--;
                            q++;
                            continue;
                        }

                        if (d == '"' && depth > 0)
                        {
                            q++;
                            while (q < n)
                            {
                                if (src[q] == '\\')
                                {
                                    q += 2;
                                    continue;
                                }

                                if (src[q] == '"')
                                {
                                    break;
                                }

                                q++;
                            }

                            q++;
                            continue;
                        }
                    }

                    if (d == '"')
                    {
                        break;
                    }

                    q++;
                }

                int endQuote = Math.Min(q, n);
                list.Add(new Literal(src[contentStart..endQuote], start, endQuote, interp));
                i = endQuote + 1;
                continue;
            }

            i = p > i ? p : i + 1;
        }

        return list;
    }

    // 该字面量是否紧跟在 Loc.T( / Loc.Fmt( / Tr( 之后（首个参数位）。
    private static bool IsLocFirstArg(string src, int start)
    {
        var prefix = src[..start].TrimEnd();
        return prefix.EndsWith("Loc.T(", StringComparison.Ordinal)
               || prefix.EndsWith("Loc.Fmt(", StringComparison.Ordinal)
               || prefix.EndsWith("Tr(", StringComparison.Ordinal);
    }

    // 把 "a" + "b" + "c" 这种跨行拼接的模板还原成单个键（长读数行在代码里就是这么拼的）。
    private static string ReconstructConcatenatedKey(string src, Literal lit)
    {
        var sb = new StringBuilder(lit.Text);
        int i = lit.EndQuote;
        while (true)
        {
            int j = i + 1;
            while (j < src.Length && char.IsWhiteSpace(src[j]))
            {
                j++;
            }

            if (j >= src.Length || src[j] != '+')
            {
                break;
            }

            j++;
            while (j < src.Length && char.IsWhiteSpace(src[j]))
            {
                j++;
            }

            if (j >= src.Length || src[j] != '"')
            {
                break;
            }

            int cs = j + 1;
            int k = cs;
            while (k < src.Length)
            {
                if (src[k] == '\\')
                {
                    k += 2;
                    continue;
                }

                if (src[k] == '"')
                {
                    break;
                }

                k++;
            }

            sb.Append(src[cs..k]);
            i = k;
        }

        return sb.ToString();
    }

    // 源码里所有「Loc 键」：Loc.T/Loc.Fmt/Tr 首个参数是纯字符串拼接的那些（变量/三元组不算键）。
    private static List<string> LocKeys(string src)
    {
        var keys = new List<string>();
        foreach (var lit in ScanLiterals(src))
        {
            if (lit.Interpolated || !IsLocFirstArg(src, lit.Start))
            {
                continue;
            }

            keys.Add(ReconstructConcatenatedKey(src, lit));
        }

        return keys;
    }

    // 紧贴字面量之前是否是「文本属性 = 」（排除 moneyText 这种以 Text 结尾的普通局部变量）。
    private static readonly Regex TextAssign = new(
        @"(^|[^\w])(?:\.(?:Text|TooltipText|PlaceholderText)|(?:Text|TooltipText|PlaceholderText))\s*=\s*$",
        RegexOptions.Compiled);

    private static bool IsTextAssign(string src, int start)
        => TextAssign.IsMatch(src[..start].TrimEnd());

    private sealed record Proto(string Name, string CsvPath,
        string[] PresentationCs, string[] AllCs, string[] Tscn);

    private static Proto[] DiscoverProtos()
    {
        var protosDir = TestPaths.FromRoot("src", "Prototypes");
        if (!Directory.Exists(protosDir))
        {
            return [];
        }

        var result = new List<Proto>();
        foreach (var protoDir in Directory.GetDirectories(protosDir))
        {
            var i18nDir = Path.Combine(protoDir, "i18n");
            if (!Directory.Exists(i18nDir))
            {
                continue;
            }

            var csvs = Directory.GetFiles(i18nDir, "*.csv");
            if (csvs.Length == 0)
            {
                continue;
            }

            var name = new DirectoryInfo(protoDir).Name;
            var scriptsDir = Path.Combine(protoDir, "Scripts");
            var logicDir = Path.Combine(scriptsDir, "Logic");
            var uiDir = Path.Combine(protoDir, "UI");

            var presentation = new List<string>();
            if (Directory.Exists(scriptsDir))
            {
                presentation.AddRange(Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.StartsWith(logicDir, StringComparison.OrdinalIgnoreCase)));
            }

            if (Directory.Exists(uiDir))
            {
                presentation.AddRange(Directory.GetFiles(uiDir, "*.cs", SearchOption.AllDirectories));
            }

            var allCs = Directory.GetFiles(protoDir, "*.cs", SearchOption.AllDirectories);
            var tscn = Directory.GetFiles(protoDir, "*.tscn", SearchOption.AllDirectories);

            result.Add(new Proto(name, csvs[0], presentation.ToArray(), allCs, tscn));
        }

        return result.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
    }

    // 「出现在原型里」的键全集：Loc 键 + 所有字面量（含 Logic 数据与场景文本，不按中文过滤）。
    private static HashSet<string> PresentKeys(Proto p)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in p.AllCs)
        {
            var src = File.ReadAllText(f);
            foreach (var k in LocKeys(src))
            {
                set.Add(k);
            }

            foreach (var lit in ScanLiterals(src))
            {
                set.Add(lit.Text);
            }
        }

        foreach (var f in p.Tscn)
        {
            var src = File.ReadAllText(f);
            foreach (var lit in ScanLiterals(src))
            {
                set.Add(lit.Text);
            }
        }

        return set;
    }

    [Fact]
    public void 每张表头是_keys_en_ja_fr_de_es_ru_ko()
    {
        var protos = DiscoverProtos();
        protos.ShouldNotBeEmpty("src/Prototypes 下应有原型的 i18n 表（至少 _Template 那张占位）；一张都没有说明 i18n 落盘没做完");
        foreach (var p in protos)
        {
            var (_, header) = ReadCsv(p.CsvPath);
            header.ShouldBe(ExpectedHeader,
                $"{p.Name} 的翻译表首行必须是 {ExpectedHeader}（首列 keys 即中文原文，其余是目标语言列）；实际读到 {header}");
        }
    }

    [Fact]
    public void 表里没有孤儿键_每个键都能在原型源码或场景里找到()
    {
        foreach (var p in DiscoverProtos())
        {
            var (keys, _) = ReadCsv(p.CsvPath);
            var present = PresentKeys(p);
            var orphan = keys.Where(k => !present.Contains(k)).ToList();
            orphan.ShouldBeEmpty(
                $"{p.Name} 的表里有 {orphan.Count} 个键在整个原型的 .cs（表现层+逻辑层）与 .tscn 里都找不到出处 —— "
                + $"那要么是代码改了没同步删键，要么是键打错了。孤儿键：{string.Join(" ‖ ", orphan)}");
        }
    }

    [Fact]
    public void Loc传的中文键都在表里()
    {
        foreach (var p in DiscoverProtos())
        {
            var (keys, _) = ReadCsv(p.CsvPath);
            var keySet = new HashSet<string>(keys, StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (var f in p.AllCs)
            {
                var src = File.ReadAllText(f);
                foreach (var k in LocKeys(src))
                {
                    // 纯 ASCII 且无占位/无实义的调用不强制入表；中文或含 {0} 的都要
                    if (string.IsNullOrEmpty(k) || (!HasCjk(k) && !k.Contains('{', StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    if (!keySet.Contains(k))
                    {
                        missing.Add($"{Path.GetFileName(f)}:「{k}」");
                    }
                }
            }

            missing.ShouldBeEmpty(
                $"{p.Name} 有 {missing.Count} 处 Loc.T/Loc.Fmt 传了中文键却没登记进 {Path.GetFileName(p.CsvPath)} —— "
                + $"上了 Loc 的文字必须进表，否则译者拿不到它：{string.Join(" ‖ ", missing)}");
        }
    }

    [Fact]
    public void 直接赋给文本属性的中文都经过Loc()
    {
        foreach (var p in DiscoverProtos())
        {
            var (keys, _) = ReadCsv(p.CsvPath);
            var keySet = new HashSet<string>(keys, StringComparer.Ordinal);
            var locKeys = new HashSet<string>(
                p.PresentationCs.SelectMany(f => LocKeys(File.ReadAllText(f))),
                StringComparer.Ordinal);
            var bare = new List<string>();
            foreach (var f in p.PresentationCs)
            {
                var src = File.ReadAllText(f);
                foreach (var lit in ScanLiterals(src))
                {
                    if (!HasCjk(lit.Text) || !IsTextAssign(src, lit.Start))
                    {
                        continue;
                    }

                    // 是 Loc 首个参数、或整串已是表里的键（Control 文本靠 auto_translate 原生翻译）→ 放过
                    if (IsLocFirstArg(src, lit.Start) || locKeys.Contains(lit.Text) || keySet.Contains(lit.Text))
                    {
                        continue;
                    }

                    bare.Add($"{Path.GetFileName(f)}:「{lit.Text}」");
                }
            }

            bare.ShouldBeEmpty(
                $"{p.Name} 的上屏路径（Scripts 顶层 + UI）里有 {bare.Count} 处中文直接赋给了 .Text 等文本属性、"
                + $"既没走 Loc 也不在表里：{string.Join(" ‖ ", bare)}。上屏文字要么 Loc.T/Loc.Fmt，要么先补进表");
        }
    }

    // === 框架共用表（src/GugaFramework/FrontEnd/i18n/frontend.csv）===
    //
    // 为什么单独一组判据，不并进上面那四条：上面那组按「原型」组织——
    // DiscoverProtos 扫 src/Prototypes/*/i18n/。框架那几个字（设置、继续、退出游戏…）
    // 归框架自己那张表，不隶属任何原型（「原型隔离」），所以那条扫描根本看不到它。
    // 不补这一格的症状是：前端层改了字、表没跟，两边都绿，译者拿到的是一张过期的表。
    // 与原型那四条同判据、同工具（ReadCsv / ScanLiterals / LocKeys），只是扫的根不同。

    /// <summary>框架共用表（前端层那几个字）。不隶属任何原型，故不走 <see cref="DiscoverProtos"/>。</summary>
    private static string FrontendCsvPath()
        => TestPaths.FromRoot("src", "GugaFramework", "FrontEnd", "i18n", "frontend.csv");

    /// <summary>前端层的全部 .cs（不含测试与工具——那几个不产上屏文字）。</summary>
    private static string[] FrontendSources()
        => Directory.GetFiles(TestPaths.FromRoot("src", "GugaFramework", "FrontEnd"), "*.cs",
            SearchOption.AllDirectories);

    /// <summary>
    /// 「出现在前端层里」的键全集：所有字面量 <b>＋</b> 还原过 <c>+</c> 拼接的 Loc 键。
    ///
    /// <para><b>为什么后一半不能省</b>：试验场那行操作提示是
    /// <c>Loc.T("Esc 开/关菜单 · …" + "调试桥：…")</c> 拼出来的，
    /// 单个片段都不是表里那个整键。只按字面量判，它会被误报成孤儿键——
    /// 与 <c>docs/GugaFramework/i18n.md</c> 记的「<c>+</c> 拼接的长模板」那个坑同一笔账。</para>
    /// </summary>
    private static HashSet<string> FrontendPresentKeys()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in FrontendSources())
        {
            var src = File.ReadAllText(f);
            foreach (var k in LocKeys(src))
            {
                set.Add(k);
            }

            foreach (var lit in ScanLiterals(src))
            {
                set.Add(lit.Text);
            }
        }

        return set;
    }

    [Fact]
    public void 框架表头是_keys_en_ja_fr_de_es_ru_ko()
    {
        File.Exists(FrontendCsvPath()).ShouldBeTrue(
            $"前端层的共用文案表必须在 {FrontendCsvPath()}（前端几个字不隶属任何原型，不能塞进某张原型表）；"
            + "表不在，说明这一层要么没接 i18n，要么表被挪走了");

        var (_, header) = ReadCsv(FrontendCsvPath());
        header.ShouldBe(ExpectedHeader,
            $"frontend.csv 首行必须是 {ExpectedHeader}（首列 keys 即中文原文，其余是目标语言列）；实际读到 {header}");
    }

    [Fact]
    public void 框架表里没有孤儿键_每个键都能在前端层源码里找到()
    {
        var (keys, _) = ReadCsv(FrontendCsvPath());
        keys.ShouldNotBeEmpty("frontend.csv 只有表头没有键，说明前端层上了字但没登记进表");

        var present = FrontendPresentKeys();
        var orphan = keys.Where(k => !present.Contains(k)).ToList();
        orphan.ShouldBeEmpty(
            $"frontend.csv 里有 {orphan.Count} 个键在整个前端层的 .cs 里都找不到出处 —— "
            + $"那要么是代码改了没同步删键，要么是键打错了。孤儿键：{string.Join(" ‖ ", orphan)}");
    }

    [Fact]
    public void 前端层Loc传的中文键都在框架表里()
    {
        var (keys, _) = ReadCsv(FrontendCsvPath());
        var keySet = new HashSet<string>(keys, StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var f in FrontendSources())
        {
            var src = File.ReadAllText(f);
            foreach (var k in LocKeys(src))
            {
                // 纯 ASCII 且无占位/无实义的调用不强制入表；中文或含 {0} 的都要
                if (string.IsNullOrEmpty(k) || (!HasCjk(k) && !k.Contains('{', StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!keySet.Contains(k))
                {
                    missing.Add($"{Path.GetFileName(f)}:「{k}」");
                }
            }
        }

        missing.ShouldBeEmpty(
            $"前端层有 {missing.Count} 处 Loc.T/Loc.Fmt 传了中文键却没登记进 frontend.csv —— "
            + $"上了 Loc 的文字必须进表，否则译者拿不到它：{string.Join(" ‖ ", missing)}");
    }
}
