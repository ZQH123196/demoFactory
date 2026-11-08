// 模型体检单 —— 审阅工位的编排层（开发期工具，与任何具体原型无关）。
//
// 一次运行出全套（《模型与动画准则》「实现分层与聚合」 第 1 条）：本文件只做「契约 → 引擎命令行 → 收产物 → 写报告」。
// 画面与数字都在 src/GugaFramework/Tools/review/review_capture.gd 里一次算完（《模型与动画准则》「实现分层与聚合」 第 3 条
// 「校验器与报告共用一次计算」），本层不重算、不自造第二套实现 —— PASS/FAIL 直接取引擎写的 VERDICT。
// 这里只补引擎管不到的那半：契约参数、产物存在性复核（《模型与动画准则》「窗口模式出图」「过不过只看产物」）、报告排版。
//
// 确定性（12 节）：报告里不写时间戳、不写绝对路径、列表一律排序 —— 同输入重跑逐字全等。

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DemoFactory.Tools;

/// <summary>栏 3 比例尺同框的一个分组：组内一行、按本组声明的网格。</summary>
/// <param name="Name">组名，进产物文件名与 facts 键（scale_&lt;组&gt;_side.png / SCALE_&lt;组&gt;=）。</param>
/// <param name="Models">落在本组的模型（不含 .glb）。</param>
/// <param name="GridMeters">脚下方格网步长（米），必须 &gt; 0。</param>
public sealed record ScaleGroup(string Name, IReadOnlyList<string> Models, double GridMeters);

/// <summary>一组模型的胶卷机位。方位 0° = 正后方、180° = 正前方，与引擎侧 VIEWS 表同口径；
/// 表里没有的模型走缺省方位 135°、仰角 45°（与旧的 iso 机位一致）。</summary>
/// <param name="Models">用这台机位的模型（不含 .glb）。</param>
/// <param name="Azimuth">方位（度）。</param>
/// <param name="Elevation">仰角（度），0–89。</param>
public sealed record FilmAngle(IReadOnlyList<string> Models, double Azimuth, double Elevation);

/// <summary>一个原型的体检单契约。公共层不给任何玩法契约默认值，必填字段由原型侧显式给；
/// ScaleGroups 与 FilmAngles 是可选声明 —— 空表 = 未声明，走旧的单行比例尺与缺省胶卷机位。</summary>
/// <param name="ModelsDir">模型目录，res 相对路径（不带 res:// 前缀、结尾不带 /）。</param>
/// <param name="Models">审哪些模型（不含 .glb）；空数组 = 该目录内全部。</param>
/// <param name="Clips">胶卷采哪些动作；空数组 = 每个模型自带的全部动作。</param>
/// <param name="MaxFootprint">占地上限（米），0 表示这一项不判。</param>
/// <param name="NeedClips">该有哪些动作（引擎只对带骨架的模型生效）。</param>
/// <param name="LoopClips">哪些 clip 是「姿势的循环」（胶卷按半开区间 [0, 长度) 采样，不补结束端点）。
///   必须由原型声明：glTF 规范里没有循环这个字段，.glb 读回来一律是 LOOP_NONE，模型自己说不清。</param>
/// <param name="AnchorName">头顶锚点节点名，用于叠层「Anchor 十字」。</param>
/// <param name="ReportPath">报告落盘路径（项目相对，提交进版本库）。</param>
public sealed record ReviewContract(
    string ModelsDir,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Clips,
    double MaxFootprint,
    IReadOnlyList<string> NeedClips,
    IReadOnlyList<string> LoopClips,
    string AnchorName,
    string ReportPath)
{
    /// <summary>栏 3 的比例尺分组（可选）。空表 = 未声明，维持旧的单行 scale_side.png / scale_top.png。
    /// 声明时每个送审模型恰好落在一组，启动期校验，违规即抛。</summary>
    public IReadOnlyList<ScaleGroup> ScaleGroups { get; init; } = Array.Empty<ScaleGroup>();

    /// <summary>胶卷机位表（可选）。空表 = 未声明，全部模型走缺省方位 135°、仰角 45°。</summary>
    public IReadOnlyList<FilmAngle> FilmAngles { get; init; } = Array.Empty<FilmAngle>();
}

public static class Review
{
    // ---- 公共层常量：介质物理量，与哪个游戏无关，不让各原型重述（《模型与动画准则》「实现分层与聚合」 第 4 条的例外条款） ----

    /// <summary>胶卷格距（秒）。定死 0.2 秒 = 逻辑 2 步，好与逻辑行为流第 k 步互查。</summary>
    public const double CellSeconds = 0.2;

    /// <summary>一条胶卷最多几格；装不下按 StepLadder 升档，实际格距印在图标签上（7 节 栏 2 留痕）。</summary>
    public const int MaxCells = 8;

    /// <summary>升档阶梯（秒），全部是 0.1 秒的整数倍。</summary>
    public static readonly double[] StepLadder = { 0.2, 0.4, 0.8, 1.6 };

    public const int Width = 640;
    public const int Height = 480;

    /// <summary>引擎侧采集脚本的承载场景（公共层，不含任何原型内容）。</summary>
    private const string StageScene = "res://src/GugaFramework/Tools/review/review_stage.tscn";

    private const string FactsName = "facts.txt";

    public static int Run(string root, IPrototypeTools proto, string[] rest)
    {
        var c = proto.Review();
        var dir = proto.DirName;
        Validate(c);
        var only = "";
        var godotExe = "";
        foreach (var a in rest)
        {
            if (a.StartsWith("--only=")) only = a[7..];
            else if (a.StartsWith("--godot=")) godotExe = a[8..];
            else if (!a.StartsWith("--")) only = a;
            else throw new ArgumentException($"review：不认识的参数 {a}");
        }

        var artifacts = Artifacts.Dir(root, "Review", dir);
        var facts = Path.Join(artifacts, FactsName);
        if (only.Length > 0 && c.Models.Count > 0 && !c.Models.Contains(only))
            throw new ArgumentException($"review：{only} 不在 {dir} 的送审清单里（改 Tools/ 下的契约）");

        // 送审表为空时由目录内全部 .glb 补全。目录空着就当场拒绝：
        // 起引擎再让它自检失败，会把一次参数错误变成一局白跑的窗口（甚至挂死）。
        var models = only.Length > 0 ? new List<string> { only } : new List<string>(c.Models);
        if (models.Count == 0)
        {
            var modelsAbs = Path.Join(root, c.ModelsDir);
            if (!Directory.Exists(modelsAbs))
                throw new ArgumentException($"review：模型目录不存在：{c.ModelsDir}");
            if (!Directory.EnumerateFiles(modelsAbs, "*.glb").Any())
                throw new ArgumentException($"review：{c.ModelsDir} 里没有 .glb —— 没有可审的模型，不起引擎");
        }

        var exe = ResolveGodot(godotExe);
        Artifacts.Ensure(root, "Review", dir);   // File.Delete 在父目录不存在时会抛，先把这一层与 .gdignore 立起来
        if (only.Length == 0) CleanArtifacts(root, c, artifacts);   // --only 是局部补图，不清现场
        File.Delete(facts);

        var args = new List<string>
        {
            "--path", root, StageScene, "--",
            $"--proto={dir}",
            $"--models-dir=res://{c.ModelsDir}/",
            $"--clips={string.Join(",", c.Clips)}",
            $"--out={Path.GetFullPath(artifacts)}",
            $"--size={Width}x{Height}",
            $"--cell={N(CellSeconds)}",
            $"--max-cells={MaxCells.ToString(CultureInfo.InvariantCulture)}",
            $"--ladder={Ladder()}",
            $"--max-footprint={N(c.MaxFootprint)}",
            $"--need-clips={string.Join(",", c.NeedClips)}",
            $"--loops={string.Join(",", c.LoopClips)}",
            $"--anchor={c.AnchorName}",
            $"--facts={Path.GetFullPath(facts)}",
        };
        if (c.ScaleGroups.Count > 0)
            args.Add("--scale-groups=" + string.Join(";", c.ScaleGroups
                .Select(g => $"{g.Name}:{N(g.GridMeters)}:{string.Join("|", g.Models)}")));
        if (c.FilmAngles.Count > 0)
            args.Add("--film-angles=" + string.Join(";", c.FilmAngles
                .Select(f => $"{N(f.Azimuth)}:{N(f.Elevation)}:{string.Join("|", f.Models)}")));
        // 整批跑不给 --models=：引擎据此走整组比例尺；--only 才传单件，按局部补图出图。
        if (only.Length > 0) args.Add($"--models={only}");

        Console.WriteLine($"> {Path.GetFileName(exe)} review {dir} "
                          + $"models={(models.Count == 0 ? "ALL" : string.Join(",", models))} cell={N(CellSeconds)}s");
        var (code, tail) = Spawn(exe, args);
        if (!File.Exists(facts))
        {
            Console.Error.WriteLine($"审阅没有产出 facts（引擎退出码 {code}）。末 30 行输出：\n{tail}");
            Console.Error.WriteLine("按场景跑时脚本加载失败也是退出码 0 —— 见《AI 能力手册》故障排查「退出码口径」。");
            return 1;
        }

        var r = ReadFacts(facts);
        var missing = MissingArtifacts(artifacts, r);
        // 分组硬门：声明了组就逐组要求两张文件都在（引擎没写 facts 键时上面查不到，这里兜住）。
        // 文件名约定与引擎侧 review_capture.gd 同一份：scale_<组>_side.png / scale_<组>_top.png。
        if (only.Length == 0)
            foreach (var g in c.ScaleGroups)
                foreach (var rel in new[] { ScaleFile(g.Name, false), ScaleFile(g.Name, true) })
                    if (!missing.Contains(rel) && !File.Exists(Path.Join(artifacts, rel)))
                        missing.Add(rel);
        if (missing.Count > 0) missing.Sort(StringComparer.Ordinal);
        var selfBad = int.TryParse(r.Header.GetValueOrDefault("SELF_BAD", "0"), out var sb) ? sb : 0;
        var reportFile = Path.Join(root, c.ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        File.WriteAllText(reportFile, Render(dir, c, r, missing, selfBad), new UTF8Encoding(false));

        foreach (var m in missing) Console.Error.WriteLine($"缺产物：{m}");
        var bad = r.Models.Count(m => m.Verdict != "PASS");
        Console.WriteLine($"体检单 → {c.ReportPath}　（图在 {Artifacts.Show("Review", dir)}/，不提交进版本库）");
        Console.WriteLine($"{r.Models.Count} 个模型，{bad} 个 FAIL，{missing.Count} 个产物缺失（引擎退出码 {code}）");
        if (selfBad > 0 || missing.Count > 0)
            Console.Error.WriteLine($"工位 FAIL：引擎自检 {selfBad} 项不合格 · 缺图 {missing.Count} 张 —— 图不全等于没审。");
        return bad > 0 || missing.Count > 0 || selfBad > 0 || code != 0 ? 1 : 0;
    }

    /// <summary>启动期校验（Fail Fast，违规即抛）。两个可选声明只在非空时校验；空表 = 未声明，不校验内容。</summary>
    private static void Validate(ReviewContract c)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in c.ScaleGroups)
        {
            if (g.Name.Length == 0) throw new ArgumentException("review：ScaleGroups 里有空组名");
            if (!names.Add(g.Name)) throw new ArgumentException($"review：比例尺组名重复：{g.Name}");
            if (g.GridMeters <= 0)
                throw new ArgumentException($"review：组 {g.Name} 的 GridMeters 必须 > 0，实际 {g.GridMeters}");
            if (g.Models.Count == 0) throw new ArgumentException($"review：比例尺组 {g.Name} 是空的");
            foreach (var m in g.Models)
                if (!c.Models.Contains(m))
                    throw new ArgumentException($"review：组 {g.Name} 里的 {m} 不在送审清单 Models 里");
        }

        foreach (var f in c.FilmAngles)
        {
            if (f.Models.Count == 0) throw new ArgumentException("review：FilmAngles 里有空的模型表");
            if (f.Elevation < 0 || f.Elevation > 89)
                throw new ArgumentException($"review：FilmAngles 的仰角必须在 0–89，实际 {f.Elevation}");
            foreach (var m in f.Models)
                if (!c.Models.Contains(m))
                    throw new ArgumentException($"review：FilmAngles 里的 {m} 不在送审清单 Models 里");
        }

        if (c.ScaleGroups.Count > 0 && c.Models.Count > 0)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in c.ScaleGroups)
                foreach (var m in g.Models)
                    if (!seen.Add(m))
                        throw new ArgumentException($"review：{m} 落在不止一个比例尺组里");
            foreach (var m in c.Models)
                if (!seen.Contains(m))
                    throw new ArgumentException($"review：{m} 不在任何比例尺组里（声明了 ScaleGroups 就要全覆盖）");
        }
    }

    /// <summary>跑前清旧：删掉输出根里不在送审清单的模型目录（保留 .gdignore / facts.txt / 本次送审目录）；
    /// 声明了分组时把输出根里的旧 scale_*.png（含单行时代的 scale_side.png / scale_top.png）一并删掉。</summary>
    private static void CleanArtifacts(string root, ReviewContract c, string artifacts)
    {
        var declared = c.Models.Count > 0
            ? new HashSet<string>(c.Models, StringComparer.Ordinal)
            : new HashSet<string>(
                Directory.EnumerateFiles(Path.Join(root, c.ModelsDir), "*.glb")
                    .Select(f => Path.GetFileNameWithoutExtension(f)!), StringComparer.Ordinal);
        var removed = 0;
        foreach (var d in Directory.EnumerateDirectories(artifacts))
        {
            if (declared.Contains(Path.GetFileName(d))) continue;
            Directory.Delete(d, true);
            removed++;
        }

        if (removed > 0)
            Console.WriteLine($"清理：删掉 {removed} 个不在送审清单的旧模型目录");
        if (c.ScaleGroups.Count == 0) return;
        var staleScale = Directory.EnumerateFiles(artifacts, "scale_*.png").ToList();
        foreach (var f in staleScale) File.Delete(f);
        if (staleScale.Count > 0)
            Console.WriteLine($"清理：删掉 {staleScale.Count} 张旧比例尺图");
    }

    /// <summary>栏 3 单张图的产物名。未分组 = 旧名 scale_side.png / scale_top.png；分组 = scale_&lt;组&gt;_side.png。</summary>
    private static string ScaleFile(string group, bool top) => group.Length == 0
        ? $"scale_{(top ? "top" : "side")}.png"
        : $"scale_{group}_{(top ? "top" : "side")}.png";

    /// <summary>
    /// 引擎可执行文件：命令行 --godot= &gt; 环境变量 GODOT_BIN &gt; PATH 上的 godot。
    /// 机器级路径既不写进代码也不写进契约（契约提交进版本库，机器路径不提交进版本库）。
    /// </summary>
    private static string ResolveGodot(string flag) => GodotBin.Resolve(flag,
        "（审阅工位走窗口模式出图，--headless 出不了画，见《模型与动画准则》「窗口模式出图」）");

    private static string Ladder() => string.Join(',', StepLadder.Select(N));

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static (int Code, string Tail) Spawn(string exe, List<string> args) =>
        GodotProcess.Run(exe, args, 600_000, "审阅运行");

    // ---------------- facts ----------------
    // 解析与类型都在 Facts.cs（技能体检单读的是同一批数字，实现不许有第二份）。

    private static FactsReport ReadFacts(string file) => Facts.Read(file);

    /// <summary>栏 3 是原型级图，引擎分开写头：未分组时两键 SCALE（侧向）/ SCALE_TOP（俯视）；
    /// 分组后每组两键 SCALE_&lt;组&gt; / SCALE_TOP_&lt;组&gt;。</summary>
    private static List<string> ScaleFiles(FactsReport r)
    {
        var files = new List<string>();
        foreach (var k in new[] { "SCALE", "SCALE_TOP" })
            files.AddRange(Facts.Split(r.Header.GetValueOrDefault(k, "")));
        foreach (var k in r.Header.Keys
                     .Where(k => k.StartsWith("SCALE_") && k != "SCALE_TOP")
                     .OrderBy(k => k, StringComparer.Ordinal))
            files.AddRange(Facts.Split(r.Header[k]));
        return files;
    }

    /// <summary>facts 说写了、磁盘上却没有的图 —— 「过不过只看产物」的编排侧那半（《模型与动画准则》「窗口模式出图」）。</summary>
    private static List<string> MissingArtifacts(string artifactsDir, FactsReport r)
    {
        var missing = new List<string>();
        void Check(string rel)
        {
            if (!File.Exists(Path.Join(artifactsDir, rel.Replace('/', Path.DirectorySeparatorChar)))) missing.Add(rel);
        }
        foreach (var m in r.Models)
        {
            foreach (var f in m.ViewFiles) Check(f);
            foreach (var f in m.FilmFiles) Check(f);
            foreach (var f in m.KvList("OVERLAY")) Check(f);
            foreach (var f in m.KvList("SHEET")) Check(f);
        }
        foreach (var f in ScaleFiles(r)) Check(f);
        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    // ---------------- 报告 ----------------

    private static string Render(string dir, ReviewContract c, FactsReport r, List<string> missing, int selfBad)
    {
        var sb = new StringBuilder();
        var bad = r.Models.Count(m => m.Verdict != "PASS");
        sb.Append($"# 模型体检单 — {dir}\n\n");
        sb.Append($"> 由 `dotnet run --project src/Tools -- review {dir} .` 生成，**勿手改**。\n");
        sb.Append("> 怎么算合格出自 `docs/《模型与动画准则》「验收清单」；PASS/FAIL 由引擎侧校验器算（一次计算两处用），\n");
        sb.Append("> 本文件只是它的可读投影。图不提交进版本库（`.cache/` 整条被 gitignore），这里只留可比对的数字。\n");
        var scaleSpec = c.ScaleGroups.Count == 0
            ? "栏 3 比例尺同框 + 1 米方格网"
            : $"栏 3 比例尺同框分 {c.ScaleGroups.Count} 组（组内一行、各组按声明网格）";
        sb.Append($"› 规格：栏 1 十视图（前后左右 + 四角 45° + 正俯视 + 等距 45°，一律绑定姿势）；"
                  + $"栏 2 胶卷格距 {N(CellSeconds)} 秒（= 逻辑 2 步）封顶 {MaxCells} 格；"
                  + $"{scaleSpec}。不含时间戳，重跑逐字全等。\n\n");
        sb.Append($"- 出图 {r.Header.GetValueOrDefault("SIZE", "?")}，窗口挪到 {r.Header.GetValueOrDefault("WINDOW", "?")}（屏外），"
                  + $"引擎 {r.Header.GetValueOrDefault("ENGINE", "?")}\n");
        sb.Append($"- 占地上限 {(c.MaxFootprint > 0 ? "≤ " + N(c.MaxFootprint) + " 米" : "未设，不判")}；"
                  + $"动作契约 {(c.NeedClips.Count > 0 ? string.Join(" / ", c.NeedClips) : "未设")}\n\n");

        sb.Append("## 汇总\n\n");
        sb.Append($"{r.Models.Count} 个模型 · {r.Models.Count - bad} PASS · {bad} FAIL"
                  + (missing.Count > 0 ? $" · **{missing.Count} 个产物缺失**" : "") + "\n\n");
        foreach (var m in r.Models.Where(m => m.Verdict != "PASS"))
            sb.Append($"- **FAIL** `{m.Name}`：{m.Kv.GetValueOrDefault("WHY", "（引擎没给原因）")}\n");
        foreach (var f in missing) sb.Append($"- 缺图 `{f}`\n");
        // 工位失败与模型 FAIL 分开写：模型口径不改，这里只判「图全不全、比例尺空不空」。
        if (selfBad > 0 || missing.Count > 0)
        {
            var why = new List<string>();
            if (selfBad > 0) why.Add($"引擎自检 {selfBad} 项不合格（空白图或写盘失败）");
            if (missing.Count > 0) why.Add($"缺图 {missing.Count} 张");
            sb.Append($"- **工位 FAIL**：{string.Join(" · ", why)} —— 图不全就等于没审。\n");
        }
        if (bad > 0 || missing.Count > 0 || selfBad > 0) sb.Append('\n');

        sb.Append("## 逐模型\n\n");
        sb.Append("| 模型 | 骨 | 占地 X×Z | 高 | 底 y | 面 | 血条宽 @ 名牌高 | 最大骨偏移/米（每条 clip） | 结论 |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|\n");
        foreach (var m in r.Models)
        {
            var aabb = m.Kv.GetValueOrDefault("AABB", "?").Split('x');
            string Get(int i) => i < aabb.Length && aabb[i].Length > 0 ? aabb[i] : "?";
            var motion = m.Motion.Count == 0 ? "—" : string.Join(" ",
                m.Motion.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value:F3}"));
            sb.Append($"| `{m.Name}` | {m.Kv.GetValueOrDefault("BONES", "?")} | {Get(0)}×{Get(2)} | {Get(1)} | "
                      + $"{m.Kv.GetValueOrDefault("BASE_Y", "?")} | {m.Kv.GetValueOrDefault("SURFACES", "?")} | "
                      + $"血条 {m.Kv.GetValueOrDefault("ANCHOR_BAR", "?")} 米 @ {m.Kv.GetValueOrDefault("ANCHOR_TOP", "?")} 米 | "
                      + $"{motion} | {m.Verdict} |\n");
        }

        if (c.FilmAngles.Count > 0)
            sb.Append("\n胶卷机位：" + string.Join("；", c.FilmAngles
                    .Select(f => $"`{string.Join("` / `", f.Models)}` 方位 {N(f.Azimuth)}° / 仰角 {N(f.Elevation)}°"))
                + "；其余模型走缺省 方位 135° / 仰角 45°。\n");

        sb.Append("\n## 胶卷采样时刻（秒；格距≠默认值即本条升过档，`once` = 引擎读到的这条动作不循环）\n\n");
        sb.Append("| 模型 | clip | 长度 | 格距 | 格数 | 循环 | 采样时刻 |\n|---|---|---|---|---|---|---|\n");
        foreach (var m in r.Models)
            foreach (var line in m.Times)
            {
                var p = line.Split(':');
                if (p.Length < 6) continue;
                sb.Append($"| `{m.Name}` | {p[0]} | {p[1]} | {p[2]} | {p[3]} | {p[4]} | {p[5].Replace(';', ' ')} |\n");
            }

        sb.Append("\n## 产物清单（`" + Artifacts.Show("Review", dir) + "/` 下，本机可读）\n\n");
        foreach (var m in r.Models)
        {
            var files = m.KvList("SHEET").Concat(m.ViewFiles).Concat(m.FilmFiles).Concat(m.KvList("OVERLAY"));
            sb.Append($"- **{m.Name}**：{string.Join(" ", files.Select(f => "`" + f + "`"))}\n");
        }
        if (c.ScaleGroups.Count > 0)
        {
            foreach (var g in c.ScaleGroups)
                sb.Append($"- **比例尺同框 · `{g.Name}`**（{N(g.GridMeters)} 米网格）："
                          + $"`{ScaleFile(g.Name, false)}` `{ScaleFile(g.Name, true)}`\n");
        }
        else
        {
            var scale = ScaleFiles(r);
            if (scale.Count > 0)
                sb.Append($"- **比例尺同框**：{string.Join(" ", scale.Select(f => "`" + f + "`"))}\n");
        }
        return sb.ToString();
    }
}
