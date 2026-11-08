// .tscn 离线自检引擎 —— 两类契约一起查，退出码非 0 即可据此定过不过。
// （2026-09-23 自 tscn_check.mjs 移植；Node 原版已删，史实见 git log）
//   格式：引用完整性 / Transform3D 元数 / 父子声明顺序 / load_steps / 是否被编辑器存过盘
//   几何：占地是否超上限、名牌锚点是否高于剪影、部件是否连成一块（揪悬空件）
//
// 几何算法：单位图元半轴 × 节点 basis 列长 → 8 角点沿父链累乘到世界系，取 AABB。
// 之所以能离线判几何，是因为 Tscn 约定共享图元一律单位尺寸、缩放全在 basis 列长里。
//
// 引擎本身不含任何游戏知识：扫描目录、占地上限、锚点名这些契约由调用方（各原型 Tools/Check.cs）传入。

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DemoFactory.Tools;

public static class TscnCheck
{
    /// <param name="IntentionalGaps">
    /// 登记过的「故意空着的缝」：命中 <paramref name="Pattern"/> 的场景里，落在
    /// [Bottom, Top] 这条高度带上的连通块不再按悬空件报。
    ///
    /// 为什么要有这个口：「悬空件」这一条抓的是真错误（早先 SlowTrap 的网架在 0.28 米框上、
    /// 两端搭着边梁、中间悬空，十视图上看不出来）。但**磁悬浮那一类造型里悬空就是造型本身**
    /// —— 塔尖与悬浮盘之间那道缝是它全部的意思，填上反而做错。引擎分不出这两种，
    /// 于是让调用方按场景登记，登记这件事本身就是一条要人来负责的账。
    /// </param>
    public sealed record Options(
        string Root,
        string Dir,
        int? MaxFootprint = null,
        string? FootprintPattern = null,
        string AnchorName = "",
        double Touch = 0.05,
        string RegenHint = "重跑该原型的生成器",
        string? DumpFile = null,
        IReadOnlyList<(string Pattern, double Bottom, double Top)>? IntentionalGaps = null);

    public sealed record CheckResult(int Bad, int Total, bool Dumped = false);

    private static readonly Regex RxGdScene = new(@"^\[gd_scene([^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex RxLoadSteps = new(@"load_steps=(\d+)", RegexOptions.Compiled);
    private static readonly Regex RxResource = new(@"^\[(sub|ext)_resource\b([^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex RxResourceId = new(@"id=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex RxResourceType = new(@"type=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex RxNode = new(@"^\[node name=""([^""]+)""((?: [a-z_]+=(?:""[^""]*""|[^ \]]+))*)\]",
        RegexOptions.Compiled);
    private static Regex RxAttr(string k) => new($" {k}=\"([^\"]*)\"");
    private static readonly Regex RxInstance = new(@"instance=ExtResource\(""([^""]+)""\)");
    private static readonly Regex RxUniqueId = new(@"\bunique_id=");
    private static readonly Regex RxSection = new(@"^\[", RegexOptions.Compiled);
    private static readonly Regex RxProp = new(@"^([a-zA-Z_][\w/]*) = (.*)$", RegexOptions.Compiled);
    private static readonly Regex RxTransform = new(@"Transform3D\(", RegexOptions.Compiled);
    private static readonly Regex RxVector = new(@"Vector\d\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex RxSubRef = new(@"SubResource\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex RxExtRef = new(@"ExtResource\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex RxPackedScene = new(@"type=""PackedScene"" path=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex RxTransformLine = new(@"transform = Transform3D\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex RxNumber = new(@"^-?[\d.eE+-]+$", RegexOptions.Compiled);

    private sealed class Blk
    {
        public string Kind = "";
        public string? Id;
        public string? Type;
        public string? Parent;
        public string? Instance;
        public bool SavedByEditor;
        public Dictionary<string, string> Props = new();
        public string Name = "";
    }

    private sealed class SceneFile
    {
        public int? LoadSteps;
        public readonly Dictionary<string, Blk> Subs = new();
        public readonly Dictionary<string, Blk> Exts = new();
        public readonly List<Blk> Nodes = new();
        public readonly List<string> Errs = new();
    }

    private static double[] Nums(string s)
    {
        var inner = s.Substring(s.IndexOf('(') + 1, s.LastIndexOf(')') - s.IndexOf('(') - 1);
        var parts = inner.Split(',');
        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++) result[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
        return result;
    }

    private static double[]? Vec3(string? s)
    {
        if (s == null) return null;
        var m = RxVector.Match(s);
        if (!m.Success) return null;
        var parts = m.Groups[1].Value.Split(',');
        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++) result[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
        return result;
    }

    private static SceneFile Parse(string txt)
    {
        var sc = new SceneFile();
        Blk? blk = null;
        var lines = txt.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var ln = lines[i];
            var n = i + 1;
            if (ln.Trim().Length == 0) continue;
            Match m;
            if ((m = RxGdScene.Match(ln)).Success)
            {
                var ls = RxLoadSteps.Match(m.Groups[1].Value);
                sc.LoadSteps = ls.Success ? int.Parse(ls.Groups[1].Value) : null;
                if (!ln.Contains("format=3")) sc.Errs.Add($"{n}: format!=3");
                blk = null;
                continue;
            }
            if ((m = RxResource.Match(ln)).Success)
            {
                var attrs = m.Groups[2].Value;
                var id = RxResourceId.Match(attrs) is { Success: true } im ? im.Groups[1].Value : null;
                if (id == null) sc.Errs.Add($"{n}: {m.Groups[1].Value}_resource 缺 id");
                blk = new Blk
                {
                    Kind = m.Groups[1].Value,
                    Id = id,
                    Type = RxResourceType.Match(attrs) is { Success: true } tm ? tm.Groups[1].Value : null,
                };
                if (id != null) (m.Groups[1].Value == "sub" ? sc.Subs : sc.Exts)[id] = blk;
                continue;
            }
            if ((m = RxNode.Match(ln)).Success)
            {
                var at = m.Groups[2].Value;
                string? Attr(string k)
                {
                    var am = Regex.Match(at, $" {k}=\"([^\"]*)\"");
                    return am.Success ? am.Groups[1].Value : null;
                }
                var im = RxInstance.Match(at);
                blk = new Blk
                {
                    Kind = "node",
                    Name = m.Groups[1].Value,
                    Type = Attr("type"),
                    Parent = Attr("parent"),
                    Instance = im.Success ? im.Groups[1].Value : null,
                    SavedByEditor = RxUniqueId.IsMatch(at),
                };
                sc.Nodes.Add(blk);
                continue;
            }
            if (RxSection.IsMatch(ln))
            {
                sc.Errs.Add($"{n}: 无法识别的段名: {ln.Substring(0, Math.Min(70, ln.Length))}");
                blk = null;
                continue;
            }
            if (blk == null)
            {
                sc.Errs.Add($"{n}: 段外的游离属性: {ln.Substring(0, Math.Min(70, ln.Length))}");
                continue;
            }
            if ((m = RxProp.Match(ln)).Success) blk.Props[m.Groups[1].Value] = m.Groups[2].Value;
            else sc.Errs.Add($"{n}: 无法识别的属性行: {ln.Substring(0, Math.Min(70, ln.Length))}");
        }
        return sc;
    }

    // 共享图元都是单位尺寸（缩放全在节点 basis 的列长里），故半轴恒为 0.5
    private static readonly HashSet<string> Unit = new() { "BoxMesh", "SphereMesh", "CylinderMesh" };

    private sealed record Part(string Name, double[][] Box);

    private static (List<Part> Parts, double[]? Anchor) Geometry(SceneFile sc, string anchorName)
    {
        var root = sc.Nodes.Count > 0 ? sc.Nodes[0].Name : null;
        var world = new Dictionary<string, (double[][] B, double[] O)>();
        var parts = new List<Part>();
        double[]? anchor = null;
        foreach (var nd in sc.Nodes)
        {
            var t = nd.Props.TryGetValue("transform", out var tProp) && RxTransform.IsMatch(tProp) ? Nums(tProp) : null;
            var loc = t != null
                ? (B: new[] { t[0..3], t[3..6], t[6..9] }, O: new[] { t[9], t[10], t[11] })
                : (B: new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 } },
                   O: new[] { 0.0, 0.0, 0.0 });
            var isRoot = nd.Parent == null;
            var parentPath = isRoot ? null : nd.Parent == "." ? root : nd.Parent;
            (double[][] B, double[] O)? p = parentPath == null ? null : world.GetValueOrDefault(parentPath);
            if (parentPath != null && p == null) continue; // 父链断裂由格式检查报错，这里不重复
            // 世界 = 父 ∘ 局部：R = P.b·t.b，t = P.b·t.o + P.o（原点只能用父 basis 变换，用局部会把缩放乘进位置）
            var w = p != null
                ? (B: Tscn.MM(p.Value.B, loc.B), O: Add(Tscn.MV(p.Value.B, loc.O), p.Value.O))
                : loc;
            var key = isRoot ? nd.Name : nd.Parent == "." ? nd.Name : $"{nd.Parent}/{nd.Name}";
            world[key] = w;
            if (nd.Name == anchorName) anchor = w.O;
            double[]? half = null;
            if (nd.Type == "MeshInstance3D")
            {
                var m = RxSubRef.Match(nd.Props.TryGetValue("mesh", out var meshProp) ? meshProp : "");
                var id = m.Success ? m.Groups[1].Value : null;
                if (id != null && sc.Subs.TryGetValue(id, out var sub) && sub.Type != null && Unit.Contains(sub.Type))
                    half = new[] { 0.5, 0.5, 0.5 };
            }
            else if (nd.Type == "CSGBox3D")
            {
                var op = nd.Props.TryGetValue("operation", out var opProp) ? opProp.Trim() : "";
                if (op == "2") continue; // 雕切用的减法体不是实体
                var s = Vec3(nd.Props.TryGetValue("size", out var sizeProp) ? sizeProp : null);
                if (s != null) half = new[] { s[0] / 2, s[1] / 2, s[2] / 2 };
            }
            if (half == null) continue;
            // basis 列长即各轴缩放；CSGBox3D 的尺寸在 size 属性里、basis 不含缩放
            parts.Add(new Part(nd.Name, Aabb(w.B, w.O, half)));
        }
        return (parts, anchor);
    }

    private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };

    private static double[][] Aabb(double[][] b, double[] o, double[] half)
    {
        var lo = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var hi = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        for (var i = 0; i < 8; i++)
        for (var r = 0; r < 3; r++)
        {
            double s = 0;
            for (var c = 0; c < 3; c++) s += b[r][c] * ((i >> c & 1) != 0 ? 1 : -1) * half[c];
            var v = o[r] + s;
            if (v < lo[r]) lo[r] = v;
            if (v > hi[r]) hi[r] = v;
        }
        return new[] { lo, hi };
    }

    private static List<List<int>> Components(List<Part> parts, double tol)
    {
        var par = new int[parts.Count];
        for (var i = 0; i < par.Length; i++) par[i] = i;

        int Find(int i) => par[i] == i ? i : (par[i] = Find(par[i]));

        bool Touch(double[][] a, double[][] b)
        {
            for (var r = 0; r < 3; r++)
                if (!(a[0][r] - tol <= b[1][r] && b[0][r] - tol <= a[1][r])) return false;
            return true;
        }

        for (var i = 0; i < parts.Count; i++)
        for (var j = i + 1; j < parts.Count; j++)
            if (Touch(parts[i].Box, parts[j].Box))
                par[Find(i)] = Find(j);
        var g = new Dictionary<int, List<int>>();
        for (var i = 0; i < parts.Count; i++)
        {
            var r = Find(i);
            if (!g.TryGetValue(r, out var list)) g[r] = list = new List<int>();
            list.Add(i);
        }
        return new List<List<int>>(g.Values);
    }

    private static IEnumerable<string> Walk(string dir)
    {
        foreach (var f in Directory.GetFiles(dir, "*.tscn")) yield return f;
        foreach (var d in Directory.GetDirectories(dir))
        foreach (var f in Walk(d))
            yield return f;
    }

    private static string Fmt(double v) =>
        v.ToString(v >= 10 ? "0.0" : "0.00", CultureInfo.InvariantCulture);

    /// <summary>扫描并检查。打印报告，返回 (bad, total)。</summary>
    public static CheckResult RunCheck(Options o)
    {
        if (o.DumpFile != null)
        {
            var (parts, anchor) = Geometry(Parse(File.ReadAllText(o.DumpFile)), o.AnchorName);
            foreach (var p in parts)
                Console.WriteLine(p.Name.PadRight(14) + Tscn.JsonStr(BoxStr(p.Box)));
            Console.WriteLine("anchor " + (anchor != null ? Tscn.JsonStr(VecStr(anchor)) : "null"));
            return new CheckResult(0, 1, Dumped: true);
        }

        var files = new List<string>(Walk(Path.Combine(o.Root, o.Dir)));
        files.Sort(StringComparer.Ordinal);
        var bad = 0;
        var rows = new List<string>();
        foreach (var f in files)
        {
            var txt = File.ReadAllText(f);
            var sc = Parse(txt);
            var rel = f.Substring(o.Root.Length).Replace('\\', '/').TrimStart('/');
            var segs = rel.Split('/');
            var shortName = $"{segs[^2]}/{segs[^1]}";
            var errs = new List<string>(sc.Errs);
            var declared = sc.Subs.Count + sc.Exts.Count + 1;
            if (sc.LoadSteps != null && sc.LoadSteps != declared)
                errs.Add($"load_steps={sc.LoadSteps} 应为 {declared}");
            if (sc.Nodes.Exists(nd => nd.SavedByEditor) || sc.LoadSteps == null)
                errs.Add($"被编辑器存过盘（节点带 unique_id 或头行无 load_steps）→ {o.RegenHint}");

            var paths = new HashSet<string> { "" };
            foreach (var nd in sc.Nodes)
            {
                var path = nd.Parent == null ? nd.Name : nd.Parent == "." ? nd.Name : $"{nd.Parent}/{nd.Name}";
                if (nd.Parent != null && nd.Parent != "." && !paths.Contains(nd.Parent))
                    errs.Add($"parent \"{nd.Parent}\" 未先声明（{nd.Name}）");
                if (paths.Contains(path)) errs.Add($"重复节点路径 \"{path}\"");
                paths.Add(path);
                if (nd.Type == null && nd.Instance == null)
                    errs.Add($"节点 {nd.Name} 既无 type 也无 instance");
                var props = string.Join(" ", nd.Props.Values);
                foreach (Match m in RxSubRef.Matches(props))
                    if (!sc.Subs.ContainsKey(m.Groups[1].Value))
                        errs.Add($"悬空 SubResource \"{m.Groups[1].Value}\"（{nd.Name}）");
                foreach (Match m in RxExtRef.Matches(props))
                    if (!sc.Exts.ContainsKey(m.Groups[1].Value))
                        errs.Add($"悬空 ExtResource \"{m.Groups[1].Value}\"（{nd.Name}）");
                if (nd.Instance != null && !sc.Exts.ContainsKey(nd.Instance))
                    errs.Add($"instance id \"{nd.Instance}\" 未声明（{nd.Name}）");
            }
            foreach (Match m in RxPackedScene.Matches(txt))
            {
                var p = Path.Combine(o.Root, m.Groups[1].Value.Replace("res://", ""));
                if (!File.Exists(p)) errs.Add($"引用的场景文件不存在 {m.Groups[1].Value}");
            }
            foreach (Match m in RxTransformLine.Matches(txt))
            {
                var parts2 = m.Groups[1].Value.Split(',');
                if (parts2.Length != 12) errs.Add($"Transform3D 有 {parts2.Length} 个数，应为 12");
                var badNum = false;
                foreach (var v in parts2)
                    if (!RxNumber.IsMatch(v.Trim())) badNum = true;
                if (badNum) errs.Add("transform 含非数字");
            }

            var (parts, anchor) = Geometry(sc, o.AnchorName);
            var info = "";
            if (parts.Count > 1)
            {
                var lo = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
                var hi = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
                foreach (var p in parts)
                for (var r = 0; r < 3; r++)
                {
                    if (p.Box[0][r] < lo[r]) lo[r] = p.Box[0][r];
                    if (p.Box[1][r] > hi[r]) hi[r] = p.Box[1][r];
                }
                var w = hi[0] - lo[0];
                var d = hi[2] - lo[2];
                var top = hi[1];
                info = $"占地 {Fmt(w)}×{Fmt(d)}  高 {Fmt(top)}  件 {parts.Count}"
                       + (anchor != null ? $"  锚点 {Fmt(anchor[1])}" : "");
                if (o.MaxFootprint != null && o.FootprintPattern != null
                                             && Regex.IsMatch(rel, o.FootprintPattern)
                                             && Math.Max(w, d) > o.MaxFootprint)
                    errs.Add($"占地 {Fmt(w)}×{Fmt(d)} 超上限 {o.MaxFootprint}");
                if (anchor != null && top > anchor[1])
                    errs.Add($"名牌锚点 {Fmt(anchor[1])} 低于剪影高 {Fmt(top)}");
                var comps = Components(parts, o.Touch);
                // 一件东西要么落地、要么贴上别的件，否则就是悬空（离地又孤立的块）。
                // 登记过的「故意空着的缝」那一带不算悬空 —— 见 Options.IntentionalGaps 的注记。
                var gap = DeclaredGap(o, rel);
                var floating = comps.FindAll(c =>
                {
                    var bottom = Min(c, parts);
                    if (bottom <= o.Touch) return false;
                    if (gap is null) return true;
                    return bottom < gap.Value.Bottom - o.Touch || bottom > gap.Value.Top + o.Touch;
                });
                var yard = comps.Count - floating.Count - 1;
                info += $"  落地块 {Math.Max(0, yard)}";
                if (floating.Count > 0)
                    errs.Add($"{floating.Count} 组悬空件：" + string.Join(" | ", Describe(floating, parts, 5)));
            }
            rows.Add(shortName.PadRight(26) + info);
            if (errs.Count > 0)
            {
                bad++;
                Console.WriteLine($"\n### {rel}");
                var seen = new HashSet<string>();
                foreach (var e in errs)
                {
                    if (!seen.Add(e)) continue;
                    Console.WriteLine("  " + e);
                    if (seen.Count >= 10) break;
                }
            }
        }
        Console.WriteLine("\n" + string.Join("\n", rows));
        Console.WriteLine(bad > 0 ? $"\n{bad}/{files.Count} files have problems" : $"\nOK — {files.Count} files clean");
        return new CheckResult(bad, files.Count);
    }

    /// <summary>
    /// 这个场景有没有登记过「故意空着的缝」。命中 <see cref="Options.IntentionalGaps"/> 里
    /// 正则匹配的那一条就给回那条高度带；一条都没登记就是 null（照旧按悬空件报）。
    /// </summary>
    private static (double Bottom, double Top)? DeclaredGap(Options o, string rel)
    {
        if (o.IntentionalGaps is null) return null;
        foreach (var g in o.IntentionalGaps)
        {
            if (Regex.IsMatch(rel, g.Pattern)) return (g.Bottom, g.Top);
        }
        return null;
    }

    private static double Min(List<int> c, List<Part> parts)
    {
        var m = double.PositiveInfinity;
        foreach (var i in c) m = Math.Min(m, parts[i].Box[0][1]);
        return m;
    }

    private static IEnumerable<string> Describe(List<List<int>> floating, List<Part> parts, int take)
    {
        var result = new List<string>();
        foreach (var c in floating)
        {
            var lo2 = Min(c, parts);
            var names = string.Join("+", c.ConvertAll(i => parts[i].Name));
            result.Add($"{names}@离地 {Fmt(lo2)}");
            if (result.Count >= take) break;
        }
        return result;
    }

    private static string BoxStr(double[][] box)
    {
        var sb = new System.Text.StringBuilder("[");
        for (var i = 0; i < 2; i++)
        {
            sb.Append(i == 0 ? "[" : ", [");
            for (var r = 0; r < 3; r++)
            {
                if (r > 0) sb.Append(", ");
                sb.Append(Math.Round(box[i][r], 2, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
        }
        return sb.Append(']').ToString();
    }

    private static string VecStr(double[] v)
    {
        var sb = new System.Text.StringBuilder("[");
        for (var i = 0; i < v.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Math.Round(v[i], 2, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }
}