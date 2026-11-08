#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>一条命中的读数：哪条守卫、哪个文件第几行、命中的符号或那一行的原文、有没有豁免。</summary>
/// <param name="Rule">守卫的实名，三条之一（见 <see cref="NavSourceGuard"/> 那三条常量）。</param>
/// <param name="RelativePath">相对扫描根的那一段，一律正斜杠（报告是要提交进版本库比对的）。</param>
/// <param name="Line">行号，从 1 起。</param>
/// <param name="Symbol">信号 A 记方法名，信号 B 记那一行去掉缩进后的原文。</param>
/// <param name="Text">那一行的原文，去掉缩进。判红时 agent 要靠它一眼认出是哪处写法。</param>
/// <param name="ExemptionWhy">命中了豁免就带上那一条的理由（「账写目的」：每条账注明它让哪一步变便宜），没命中是 null。</param>
public readonly record struct NavGuardHit(
    string Rule,
    string RelativePath,
    int Line,
    string Symbol,
    string Text,
    string? ExemptionWhy);

/// <summary>
/// 一条豁免：<b>路径 + 符号</b>。行号只作记录，不进匹配键 —— 行号会随无关编辑漂移，
/// 拿它当键的后果是「有人在上方插了两行，守卫报出一条不存在的违规」，那是假红，
/// 而假红会让人把守卫整个关掉（这正是守卫最容易死掉的方式）。
///
/// <para>信号 B 没有方法名可记，键退化成「那一行去掉缩进的原文」：同样会漂，但比行号稳，
/// 真漂了守卫就红一次 —— 那是一次该有的对话（改名了就该重新看一眼这是不是还在自己算下一站）。</para>
/// </summary>
/// <param name="RelativePath">相对扫描根的路径，正斜杠。</param>
/// <param name="Symbol">信号 A 是方法名，信号 B 是那一行原文。</param>
/// <param name="Why">它让 agent 调试的哪一步变便宜（「账写目的」）。答不上来的条目当场删掉，不许挂账。</param>
public sealed record NavGuardExemption(string RelativePath, string Symbol, string Why);

/// <summary>
/// 寻路层的源码守卫 —— 扫一片源码树，报出「在寻路层之外自己搞了一套」的三种写法。
///
/// <para><b>为什么要有这一份</b>（spec User Story 7）：「寻路单入口」 说「寻路只有寻路层一个入口」，
/// 但规则约束不了别人不写第二条实现。实测就是四条互不相干的移动实现散在三个原型里。
/// 守卫的价值是<b>逼出一次对话</b>，不是做全覆盖静态检查 —— 所以判据只有三条，宁可漏也不铺开。</para>
///
/// <para><b>三条判据</b>（①②③）+ <b>工单 05 追加的第四条</b>：
/// ① <see cref="GridOutsideRule"/>：层自有的四个网格类型名（<c>NavGrid</c> / <c>NavGridSpec</c> /
///   <c>NavSpace</c> / <c>ClearanceField</c>）不得出现在 <c>GugaFramework/Pathfinding/</c> 与
///   <c>GugaFramework/Tests/</c> 之外 —— 挡的是「第二张网格」；
/// ② <see cref="EngineNavRule"/>：<c>NavigationServer</c> / <c>NavigationAgent</c> / <c>AStar*</c>
///   不得出现在原型里 —— 引擎导航一律不引（「寻路单入口」）；
/// ③ <see cref="OwnNextStepRule"/>：自己算下一站（直线趋近 / 相向推开），两个信号，见下；
///   ④ <see cref="AgentInGridRule"/>：<b>一行里同时出现网格那一侧与 agent 那一侧</b> ——
///   寻路网格里不许出现任何 agent 相关的格（工单 05；站位冲突是叠加修正，不进网格）。</para>
///
/// <para><b>范围为什么这么切</b>：规则 ① 把本层自己的用例行排除在外，因为用例必须按名字指认它断言的对象；
/// 规则 ③ 排除任何带 <c>Tests</c> 目录段的文件，因为测试摆位（<c>hero.Position = …</c>）是布场不是移动。
/// 这两处是<strong>范围边界</strong>，不是豁免条目：豁免是按命中点的，范围是按文件的，两者分开记，
/// 免得「范围」那一栏越攒越多、看不出真在积压什么。</para>
///
/// <para><b>纯函数</b>：<see cref="Scan"/> 只收一个源码根，不认识 <c>TestPaths</c>、不读全局状态。
/// 于是守卫本体能进主程序集（它是纯 C#，不吃 Godot 类型），而测试那一侧自己递根。</para>
/// </summary>
public static class NavSourceGuard
{
    /// <summary>守卫 ①：寻路层之外自己**声明**了一张持有地形的网格类型（认声明，不认「类型名出现」）。</summary>
    public const string GridOutsideRule = "网格外写网格";

    /// <summary>守卫 ②：引用引擎导航。</summary>
    public const string EngineNavRule = "引用引擎导航";

    /// <summary>守卫 ③：自己算下一站（直线趋近 / 相向推开）。</summary>
    public const string OwnNextStepRule = "自己算下一站";

    /// <summary>
    /// 守卫 ④：<b>寻路网格里不许出现任何 agent 相关的格</b>（工单 05）。
    ///
    /// <para>判的是<b>一行里同时出现两样东西</b>：网格那一侧（造网格 / 造格 / 三个写入口）与
    /// agent 投影那一侧（投影类型、组号、身体半径、拥挤、卡住、推开）。也就是说
    /// 「有人把两个单位互相推开翻译成了网格」这一处错位在源码上有一行可认的形状：
    /// <c>grid.SetBlocked(new NavCell((int)a.Position.X, (int)a.Position.Z), true)</c>。</para>
    ///
    /// <para>写这一条是因为它<b>会变便宜的那一步</b>是：agent 问「为什么它推不动了」时，
    /// 答案不该是「有一格被某个单位标成了阻挡」—— 那条路一旦铺开，
    /// 「谁挡的」与「地形变了」在缓存作废那条路上就再也分不开了。</para>
    ///
    /// <para><b>整份说明不判</b>：只跳过纯注释不判（理由与守卫 ③ 一样 ——
    /// 注释里写「这一处原本是把推开翻译成网格」不是违规），而这一份的说明里
    /// 必然同时出现两侧的词，所以规则 ④ 自己额外跳过 <c>///</c> 与 <c>*</c> 开头的行。
    /// <b>测试文件也不参与</b>（与守卫 ③ 同口径）：用例要按名字写出违规样本才能证它抓得住，
    /// 样本被自己判红就是假红。规则 ①②③ 的口径一个字没动。</para>
    /// </summary>
    public const string AgentInGridRule = "寻路网格里写 agent";

    private const string LayerDir = "GugaFramework/Pathfinding/";
    private const string LayerTestsDir = "GugaFramework/Tests/";
    private const string PrototypesDir = "Prototypes/";

    /// <summary>扫构建产物与引擎缓存：那些目录里有一份源码拷贝，扫进去只会把同一处违规数两遍。</summary>
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".godot", ".cache", ".git", "addons", "node_modules",
    };

    /// <summary>
    /// 守卫 ① 的信号：<b>在这四个类型名之前有一个类型声明关键字</b>
    /// （<c>NavGrid</c> / <c>NavGridSpec</c> / <c>NavSpace</c> / <code>ClearanceField</code>，
    /// 其余 <c>NavPoint</c> / <c>NavCell</c> / <c>SpaceId</c> 是宿主递进来的标量与名字）。
    ///
    /// <para><b>为什么认声明不认「类型名出现」</b>：宿主接这一层时**必然**要递一份网格 ——
    /// 写 <c>new NavGridSpec { … }</c> 是这一层的注册口，不是第二张网格。
    /// 按「类型名出现」判，规则 ① 会在工单 08 接入的那一刻起判红，于是每次接一个原型就得加一条豁免，
    /// 而豁免是「本来就不该红」的事 —— 那就该改判据，不该挂账。
    /// 所以这一条现在只认「<b>有人自己声明了一个持有地形的类型</b>」这一种错位。</para>
    ///
    /// <para><b>这一条口径的已知边界</b>（记在这里，别当成全覆盖）：原型若把自写的网格起个别的名字
    /// （<c>MyNavGrid</c> 那类），声明形状就落不到这四个名字上，判不到。
    /// 真正兜住「自己另写一张网格」的是「寻路单入口」 与人工走查，不是这一条。</para>
    /// </summary>
    private static readonly Regex GridTypeDecl = new(
        @"\b(?:class|struct|record|interface|enum)\s+(?:(?:sealed|abstract|static|readonly|partial|ref|struct)\s+)*(?:NavGrid|NavGridSpec|NavSpace|ClearanceField)\b",
        RegexOptions.Compiled);

    /// <summary>守卫 ② 的信号。</summary>
    private static readonly Regex EngineNavUse = new(
        @"\b(NavigationServer|NavigationAgent|AStar\w*)\b",
        RegexOptions.Compiled);

    /// <summary>守卫 ③ 信号 A：方法声明（只认声明，不认调用点 —— 调用点会把一处实现数成九处）。</summary>
    private static readonly Regex MethodDecl = new(
        @"\b(?:private|public|internal|protected)\s+(?:static\s+|override\s+|virtual\s+|async\s+|sealed\s+)*[^();=]*?\b(?<name>\w+)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// 守卫 ③ 信号 B：内联的那两种「一步 = 位置 + 方向 × 步长」写法。
    ///
    /// <para>B1：把坐标按自己的长度归一化再乘步长写回去（<c>m.X -= m.X / len * step;</c>）。
    /// B2：落点 = 旧位置 + 单位方向 × 步长（<c>new DeckPoint(r.Position.X + nx * step, …)</c>）。
    /// 两者都是逐行就能认的形状，代价是脆：改个变量名就漏。漏了记在文档的待确认项一节，不铺开第四个信号。</para>
    /// </summary>
    private static readonly Regex InlineStepB1 = new(
        @"\w+(\.\w+)?\s*[-+]=\s*\w+(\.\w+)?\s*/\s*\w*(len|Len|dist|Dist|Distance|Length)\w*\s*\*",
        RegexOptions.Compiled);

    private static readonly Regex InlineStepB2 = new(
        @"=\s*new\s+\w+\(.*\+\s*\w+\s*\*\s*\w*[Ss]tep",
        RegexOptions.Compiled);

    /// <summary>守卫 ④ 信号 A（网格那一侧）：造网格、造格、三个写入口。</summary>
    private static readonly Regex GridSide = new(
        @"\b(?:new\s+(?:NavCell|NavGrid|NavGridSpec|NavSpace|ClearanceField)|SetBlocked|SetPassCost|SetDynamicCost)\b",
        RegexOptions.Compiled);

    /// <summary>守卫 ④ 信号 B（agent 那一侧）：投影类型与它那几列，加上拥挤 / 卡住 / 推开这几个词。</summary>
    private static readonly Regex AgentSide = new(
        @"\b(?:Agent|agent|Projection|Separate|Avoid|Crowd|Stuck|Position|RadiusMeters)\b|\.Group\b",
        RegexOptions.Compiled);

    /// <summary>
    /// 豁免名单 —— 每一处都是「寻路层接进来之前就存在的移动实现」，随工单收编逐条摘掉。
    /// <b>名单已经清空</b>，守卫从此转严：三个原型的手写移动全部退休。
    ///
    /// <para><b>第一批那两条随工单 08 摘掉了</b>（一份世界自己写的趋近与它的同侧推开退休，
    /// 趋近改走命令空间、站位冲突改走避让叠加）。
    /// <b>第二批那三条也摘掉了</b>（两行内联趋近与内联走步退休，怪物改发「走到点」、居民改发「沿固定点走」）。
    /// <b>最后那一条也摘掉了</b>（一处世界级的 <c>MoveToward</c> 退休，
    /// 七个调用点分别改发「追实体」与「走到点」）。
    /// 摘掉的理由就是豁免那两句「不摘的代价」：摘完之后 agent 问
    /// 「这一步是不是寻路层算的」，读一遍那个世界的推进入口就够了，
    /// 不用先把两处实现都读一遍才知道看的是哪条路径；
    /// 「这一格走不过去」与「被同袍推开了」也分得开了 —— 快照里各有一列。</para>
    ///
    /// <para><b>空表是这份名单的常态，不是「还没填」</b>：从今往后新落下的命中一律判红，
    /// 要挂账就得写明它让哪一步变便宜（「账写目的」）。</para>
    /// </summary>
    public static IReadOnlyList<NavGuardExemption> Exemptions { get; } = Array.Empty<NavGuardExemption>();

    /// <summary>
    /// 扫一片源码树，返回<b>全部</b>命中（豁免的也在，带上豁免理由）。
    /// 全给而不是只给未豁免的，是为了让「每条豁免都对得上现存的写法」这条断言能验 ——
    /// 退休了的豁免就是一条挂账，当场发现的办法是它对不上任何命中（「账写目的」）。
    /// </summary>
    public static IReadOnlyList<NavGuardHit> Scan(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            throw new ArgumentException(
                $"扫的源码根 {sourceRoot} 不存在或不是目录 —— 扫 0 个文件会判出一个「全过」的红，"
                + "而它和「扫了 2000 个都没问题」长得一模一样。",
                nameof(sourceRoot));
        }

        var hits = new List<NavGuardHit>();
        foreach (var file in EnumerateSources(sourceRoot))
        {
            var relative = ToRelative(sourceRoot, file);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                continue;   // 正被占着或已被删掉：这一轮读不到不算违规，下一轮再判
            }

            for (var i = 0; i < lines.Length; i++)
            {
                ScanLine(relative, i + 1, lines[i], hits);
            }
        }

        return hits;
    }

    /// <summary>扫出来的命中里，没有被豁免盖住的那些。<b>这才是判红的那一份。</b></summary>
    public static IReadOnlyList<NavGuardHit> Unmatched(string sourceRoot)
    {
        var kept = new List<NavGuardHit>();
        foreach (var hit in Scan(sourceRoot))
        {
            if (hit.ExemptionWhy is null)
            {
                kept.Add(hit);
            }
        }

        return kept;
    }

    private static IEnumerable<string> EnumerateSources(string sourceRoot)
    {
        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] subdirs;
            string[] files;
            try
            {
                subdirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir, "*.cs");
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var sub in subdirs)
            {
                if (SkipDirs.Contains(Path.GetFileName(sub)))
                {
                    continue;
                }

                pending.Push(sub);
            }
        }
    }

    private static void ScanLine(string relative, int line, string raw, List<NavGuardHit> hits)
    {
        var text = raw.Trim();
        if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
        {
            return;   // 纯注释不判：注释里写「这里原本是手写趋近」不是违规
        }

        var isTest = relative.Split('/').Any(seg => string.Equals(seg, "Tests", StringComparison.OrdinalIgnoreCase));
        var inLayer = relative.StartsWith(LayerDir, StringComparison.Ordinal);
        var inLayerTests = relative.StartsWith(LayerTestsDir, StringComparison.Ordinal);

        if (!inLayer && !inLayerTests && !isTest && GridTypeDecl.IsMatch(text))
        {
            Add(hits, GridOutsideRule, relative, line, text, text);
        }

        if (relative.StartsWith(PrototypesDir, StringComparison.Ordinal) && EngineNavUse.IsMatch(text))
        {
            Add(hits, EngineNavRule, relative, line, text, text);
        }

        // 守卫 ④：这一行同时碰到网格那一侧与 agent 那一侧。整份说明不判（只跳过纯注释不判）——
        // 这一条规则的说明里必然同时出现两侧的词，拿说明去判它是判自己。
        // 测试文件不参与这一条（同守卫 ③ 的口径）：用例必须**按名字写出违规样本**才能证它抓得住，
        // 样本本身被自己判红就成了「判据抓不住」的假红 —— 那比漏判更贵。
        if (!isTest
            && !text.StartsWith("///", StringComparison.Ordinal)
            && !text.StartsWith("*", StringComparison.Ordinal)
            && GridSide.IsMatch(text)
            && AgentSide.IsMatch(text))
        {
            Add(hits, AgentInGridRule, relative, line, text, text);
        }

        if (inLayer || isTest)
        {
            return;   // 寻路层自己与测试布场不参与守卫 ③
        }

        var decl = MethodDecl.Match(text);
        if (decl.Success && IsSelfStepName(decl.Groups["name"].Value))
        {
            Add(hits, OwnNextStepRule, relative, line, text, decl.Groups["name"].Value);
        }

        if (InlineStepB1.IsMatch(text) || InlineStepB2.IsMatch(text))
        {
            Add(hits, OwnNextStepRule, relative, line, text, text);
        }
    }

    /// <summary>趋近 / 相向推开那组方法名。改名就绕过去了 —— 守卫的定位是逼出一次对话，不是全覆盖。</summary>
    private static bool IsSelfStepName(string name)
    {
        if (name.EndsWith("Toward", StringComparison.Ordinal) ||
            name.EndsWith("Towards", StringComparison.Ordinal))
        {
            return true;
        }

        return name.StartsWith("Separate", StringComparison.Ordinal) ||
               name.Equals("Approach", StringComparison.Ordinal) ||
               (name.StartsWith("Push", StringComparison.Ordinal) &&
                (name.EndsWith("Apart", StringComparison.Ordinal) ||
                 name.EndsWith("Away", StringComparison.Ordinal) ||
                 name.EndsWith("Out", StringComparison.Ordinal)));
    }

    private static void Add(
        List<NavGuardHit> hits,
        string rule,
        string relative,
        int line,
        string text,
        string symbol)
    {
        hits.Add(new NavGuardHit(rule, relative, line, symbol, text, ExemptionFor(relative, symbol)));
    }

    private static string? ExemptionFor(string relative, string symbol)
    {
        foreach (var exemption in Exemptions)
        {
            if (string.Equals(exemption.RelativePath, relative, StringComparison.Ordinal) &&
                string.Equals(exemption.Symbol, symbol, StringComparison.Ordinal))
            {
                return exemption.Why;
            }
        }

        return null;
    }

    private static string ToRelative(string root, string file)
        => Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>给账本用的一行读数（invariant，「double 与不变文化」）。</summary>
    public static string Describe(NavGuardHit hit)
        => $"{hit.RelativePath}:{hit.Line} [{hit.Rule}] {hit.Symbol}"
        + (hit.ExemptionWhy is null ? " —— 无豁免" : " —— 已豁免");
}
