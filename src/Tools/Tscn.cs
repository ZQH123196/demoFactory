// .tscn 文本格式写作库 —— 用代码正确产出 Godot 场景。与任何具体游戏无关。
// （2026-09-23 自 tscn.mjs 移植；Node 原版已删，史实见 git log。幂等要求仍在：改 Num 后
// 重跑 gen 必须零 diff，这是"能放心重跑"的前提）
//
// 这里沉淀的是踩出来的引擎事实，不是便利封装：
//   1. Godot 4 的 Transform3D 是「9 个基向量（行主序）+ 3 个原点」，不是 Godot 3 的基/原点交错式。
//      写错不报错，部件会被解算成 ±90° 旋转加负缩放，画面表现为「全被转飞」。
//   2. 默认旋转序是 YXZ，即 R = Ry·Rx·Rz。
//   3. ext_resource 必须整体排在 sub_resource 之前，否则 Parse Error: Unknown tag 'ext_resource'；
//      该错误不进 MCP logs_read 的任何缓冲区。
//   4. .tscn 不支持 # 注释（首行与行内两种放法都报 Parse Error），生成物标记只能挂根节点 metadata。
//   5. uid 由调用方持有（见各原型 Tools/Uids.cs）；让编辑器存盘补 uid 会连带逐节点注入 unique_id，
//      把生成物搅成大 diff。
//   6. 共享图元一律单位尺寸，缩放全在节点 basis 的列长里 —— 这样 check 侧能只按 basis 列长算包围盒。
//   7. 灰盒阶段不要写 metallic：没有反射环境/探针时高金属度表面渲染成纯黑。

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DemoFactory.Tools;

public static class Tscn
{
    public const double D = Math.PI / 180.0;

    public static readonly double[][] I3 = { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 } };

    // 单位图元：每个场景内联一份，尺寸靠节点 basis 的列长表达（见文件头事实 6）。
    // 六条全量内联（含没用到的 tri），与 .mjs 的 Object.values(MESH) 同序。
    public static readonly (string Id, string Type, string Body)[] Mesh =
    {
        ("BoxMesh_ub", "BoxMesh", "size = Vector3(1, 1, 1)"),
        ("SphereMesh_us", "SphereMesh", "radius = 0.5\nradial_steps = 16\nrings = 10"),
        ("CylinderMesh_uc", "CylinderMesh", "top_radius = 0.5\nbottom_radius = 0.5\nheight = 1\nradial_steps = 16"),
        ("CylinderMesh_uk", "CylinderMesh", "top_radius = 0\nbottom_radius = 0.5\nheight = 1\nradial_steps = 14"),
        ("CylinderMesh_up", "CylinderMesh", "top_radius = 0\nbottom_radius = 0.7071\nheight = 1\nradial_steps = 4"),
        ("CylinderMesh_ut", "CylinderMesh", "top_radius = 0\nbottom_radius = 0.7071\nheight = 1\nradial_steps = 3"),
    };

    private static readonly Dictionary<string, (string Id, string Type, string Body)> MeshKinds = new()
    {
        ["box"] = Mesh[0],
        ["sph"] = Mesh[1],
        ["cyl"] = Mesh[2],
        ["cone"] = Mesh[3],
        ["pyr"] = Mesh[4],
        ["tri"] = Mesh[5],
    };

    /// <summary>材质规格：albedo 必给；metallic/roughness 灰盒不给（事实 7）；emission 可选。</summary>
    public sealed record MatSpec(double[] Rgb, double? Metallic = null, double? Rough = null, double[]? Em = null);

    /// <summary>图元选项：绕图元中心的欧拉角（度）与关闭投影。</summary>
    public sealed class PrimOpts
    {
        public double Rx;
        public double Ry;
        public double Rz;
        public bool Flat;
    }

    private static PrimOpts Clone(PrimOpts? o)
    {
        var c = new PrimOpts();
        if (o != null)
        {
            c.Rx = o.Rx;
            c.Ry = o.Ry;
            c.Rz = o.Rz;
            c.Flat = o.Flat;
        }
        return c;
    }

    // 与引擎存盘格式对齐的定点数：4 位小数，噪声归零，负零归正。
    // 语义 = 复刻 JS toFixed(4)：对二进制真值的十进制展开做 4 位舍入，恰压中点时进位
    // （AwayFromZero，正负同向）。不能 Math.Round(ToEven)（二进制中点会舍向偶数），
    // 更不能走「拼 5 位字符串再 TrimEnd('5')」的分支：5 位舍入后以 5 结尾只是普通
    // 概率事件（~10% 的值），TrimEnd 会把真实的第 4 位小数一起啃掉再被整除砍半
    // （1.5496→0.1549、0.1275→0.0127，10 倍级缩水，曾致 6 座建筑 check 冒悬空件）。
    // 与 JS 的残差只剩两端中点二进制表示方向不同的 1 ulp 压线值（±1e-4，已确认可接受）。
    public static double Num(double v)
    {
        if (Math.Abs(v) < 5e-4) return 0;
        var n = Math.Round(v, 4, MidpointRounding.AwayFromZero);
        return n == 0 ? 0 : n;
    }

    public static string N(double v) => Num(v).ToString(CultureInfo.InvariantCulture);

    public static string V3(double x, double y, double z) => $"Vector3({N(x)}, {N(y)}, {N(z)})";

    public static string C3(double[] c) => $"Color({N(c[0])}, {N(c[1])}, {N(c[2])}, 1)";

    public static double[][] MM(double[][] a, double[][] b)
    {
        var o = new double[3][];
        for (var i = 0; i < 3; i++)
        {
            o[i] = new double[3];
            for (var j = 0; j < 3; j++)
            {
                double s = 0;
                for (var k = 0; k < 3; k++) s += a[i][k] * b[k][j];
                o[i][j] = s;
            }
        }
        return o;
    }

    // Godot 默认旋转序 YXZ：R = Ry*Rx*Rz
    public static double[][] Rot(double rx = 0, double ry = 0, double rz = 0)
    {
        double C(double a) => Math.Cos(a * D);
        double S(double a) => Math.Sin(a * D);
        return MM(
            new[] { new[] { C(ry), 0.0, S(ry) }, new[] { 0.0, 1.0, 0.0 }, new[] { -S(ry), 0.0, C(ry) } },
            MM(
                new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, C(rx), -S(rx) }, new[] { 0.0, S(rx), C(rx) } },
                new[] { new[] { C(rz), -S(rz), 0.0 }, new[] { S(rz), C(rz), 0.0 }, new[] { 0.0, 0.0, 1.0 } }));
    }

    public static double[] MV(double[][] m, double[] v) => new[]
    {
        m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
        m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
        m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2],
    };

    // 事实 1：先 9 个基向量（行主序），再 3 个原点分量
    public static string Tf(double[][] m, double[] p, double[]? s = null)
    {
        s ??= new[] { 1.0, 1.0, 1.0 };
        var all = new double[12];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            all[i * 3 + j] = m[i][j] * s[j];
        all[9] = p[0];
        all[10] = p[1];
        all[11] = p[2];
        var parts = new string[12];
        for (var i = 0; i < 12; i++) parts[i] = N(all[i]);
        return $"Transform3D({string.Join(", ", parts)})";
    }

    public static string TfLine(double[][] m, double[] p, double[]? s = null) => $"transform = {Tf(m, p, s)}";

    public static double[] Sub3(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

    public static double[] Cross(double[] a, double[] b) => new[]
    {
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    };

    public static double[] Nrm(double[] a)
    {
        var l = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
        return new[] { a[0] / l, a[1] / l, a[2] / l };
    }

    // 相机前向 = 本地 -Z
    public static string CamTr(double[] eye, double[] tgt)
    {
        var z = Nrm(Sub3(eye, tgt));
        var x = Nrm(Cross(new[] { 0.0, 1.0, 0.0 }, z));
        var y = Cross(z, x);
        return TfLine(
            new[] { new[] { x[0], y[0], z[0] }, new[] { x[1], y[1], z[1] }, new[] { x[2], y[2], z[2] } }, eye);
    }

    // ---------------- 场景上下文（一次生成一个场景，静态状态即可，与 .mjs 同构） ----------------

    private sealed class SceneCtx
    {
        public readonly List<NodeLine> Nodes = new();
        public readonly HashSet<string> Mats = new();
        public readonly Dictionary<string, HashSet<string>> Names = new();
    }

    private sealed class NodeLine
    {
        public NodeLine(string name, string? parent, string type, List<(string Key, string Val)> props)
        {
            Name = name;
            Parent = parent;
            Type = type;
            Props = props;
        }

        public readonly string Name;
        public readonly string? Parent;
        public readonly string Type;
        public readonly List<(string Key, string Val)> Props;
    }

    private static SceneCtx? _s;
    private static double[] _f = { 1.0, 1.0, 1.0 };
    private static IReadOnlyDictionary<string, MatSpec> _palette = new Dictionary<string, MatSpec>();

    private static SceneCtx Ctx => _s ?? throw new InvalidOperationException("EmitScene 之外不能建节点");

    public static string Node(string name, string? parent, string type, List<(string Key, string Val)>? props = null)
    {
        var ctx = Ctx;
        var key = parent ?? "";
        if (!ctx.Names.TryGetValue(key, out var used))
        {
            used = new HashSet<string>();
            ctx.Names[key] = used;
        }
        var final = name;
        var i = 2;
        while (used.Contains(final)) final = name + i++;
        used.Add(final);
        ctx.Nodes.Add(new NodeLine(final, parent, type, props ?? new List<(string, string)>()));
        return parent == null ? final : $"{parent}/{final}";
    }

    public static string Grp(string name, string? parent) => Node(name, parent, "Node3D");

    // 关节：名字带 j_ 前缀的 Node3D，节点原点 = 枢轴点，子部件的坐标写在关节局部系里。
    // 这条前缀是导出器（src/GugaFramework/Tools/glb/）认骨骼的契约：j_ 链转成骨架，
    // 其余部件按「最近祖先关节」做刚性蒙皮。关节树必须等比 fit —— 非均匀缩放会把旋转的关节切歪。
    public static string Joint(string name, string? parent, double[] pivot, double[]? eul = null)
    {
        var e = eul ?? new[] { 0.0, 0.0, 0.0 };
        return Node("j_" + name, parent, "Node3D",
            new List<(string, string)> { ("transform", Tf(Rot(e[0], e[1], e[2]), Scaled(pivot))) });
    }

    // 枢轴组：children 的坐标在旋转后的局部系里（建筑堆叠 / 躯干前倾用）
    public static string GrpT(string name, string? parent, double[] pivot, double[]? eul = null)
    {
        var e = eul ?? new[] { 0.0, 0.0, 0.0 };
        return Node(name, parent, "Node3D",
            new List<(string, string)> { ("transform", Tf(Rot(e[0], e[1], e[2]), Scaled(pivot))) });
    }

    public static string Prim(string parent, string name, string kind, string? mat, double[] center, double[] size,
        PrimOpts? o = null)
    {
        o ??= new PrimOpts();
        var props = new List<(string, string)> { ("transform", Tf(Rot(o.Rx, o.Ry, o.Rz), Scaled(center), Scaled(size))) };
        props.Add(("mesh", $"SubResource(\"{MeshKinds[kind].Id}\")"));
        if (mat != null)
        {
            Ctx.Mats.Add(mat);
            props.Add(("surface_material_override/0", $"SubResource(\"StandardMaterial3D_{mat}\")"));
        }
        if (o.Flat) props.Add(("cast_shadow", "0"));
        return Node(name, parent, "MeshInstance3D", props);
    }

    // 中心锚定
    public static string Cbx(string p, string n, string m, double[] c, double[] size, PrimOpts? o = null) =>
        Prim(p, n, "box", m, c, size, o);

    public static string Sp(string p, string n, string m, double[] c, double[] size, PrimOpts? o = null) =>
        Prim(p, n, "sph", m, c, size, o);

    // 底面锚定
    public static string Bx(string p, string n, string m, double[] @base, double[] size, PrimOpts? o = null) =>
        Prim(p, n, "box", m, new[] { @base[0], @base[1] + size[1] / 2, @base[2] }, size, o);

    public static string Cy(string p, string n, string m, double[] @base, double d, double h, PrimOpts? o = null) =>
        Prim(p, n, "cyl", m, new[] { @base[0], @base[1] + h / 2, @base[2] }, new[] { d, h, d }, o);

    public static string Cn(string p, string n, string m, double[] @base, double d, double h, PrimOpts? o = null) =>
        Prim(p, n, "cone", m, new[] { @base[0], @base[1] + h / 2, @base[2] }, new[] { d, h, d }, o);

    public static string Py(string p, string n, string m, double[] @base, double w, double h, double? d = null, PrimOpts? o = null)
    {
        var opts = Clone(o);
        opts.Ry += 45;
        return Prim(p, n, "pyr", m, new[] { @base[0], @base[1] + h / 2, @base[2] }, new[] { w, h, d ?? w }, opts);
    }

    // 圆盘（盾面/轮毂）：给中心+厚度。prim 是绕中心旋转的，用 cy 的底面锚定配 rx:90 会让盘面偏出中心
    public static string Disc(string p, string n, string m, double[] c, double d, double t, PrimOpts? o = null)
    {
        var opts = Clone(o);
        opts.Rx += 90;
        return Prim(p, n, "cyl", m, c, new[] { d, t, d }, opts);
    }

    // 水平圆盘（水面/齿轮盘面）：盘面落在 XZ 平面，中心锚定，不绕 X 旋转 —— 与 Disc 的竖直语义相对。
    public static string Plate(string p, string n, string m, double[] c, double d, double t, PrimOpts? o = null) =>
        Prim(p, n, "cyl", m, c, new[] { d, t, d }, o);

    // 水平环（铁箍/光环）：中心线圆落在 XZ 平面，中心锚定。diameter 是中心线直径，tube 是截面的高与宽。
    // 用 16 段短盒沿圆周拼一圈：段长取弦长 ×1.25，相邻段在角上重叠，整圈连通。
    // 只复用现有 box 图元、不给 MeshKinds 加条目 —— Mesh 表全量内联进每个生成的场景，
    // 加一条会改所有原型的每个 .tscn。opts 只给 Rz/Flat（逐段的角度自行叠加在 Ry 上）。
    public static string Ring(string p, string n, string m, double[] c, double diameter, double tube, PrimOpts? o = null)
    {
        const int segs = 16;
        var r = diameter / 2;
        var step = 2 * Math.PI / segs;
        var len = 2 * r * Math.Sin(step / 2) * 1.25;
        var mid = r * Math.Cos(step / 2);
        var last = p;
        for (var i = 0; i < segs; i++)
        {
            var a = i * step;
            var opts = Clone(o);
            // 段自身沿圆周的切线（-sin a, 0, cos a）摆正：Ry(θ) 把局部 X 轴映到 (cosθ, 0, -sinθ)
            opts.Ry += -(a / D + 90);
            last = Prim(p, n, "box", m,
                new[] { c[0] + mid * Math.Cos(a), c[1], c[2] + mid * Math.Sin(a) },
                new[] { len, tube, tube }, opts);
        }
        return last;
    }

    // 沿肢体/杆轴走 len 后的端点（len 取负即向下）
    public static double[] AxisAt(double[] from, double len, PrimOpts? o = null)
    {
        o ??= new PrimOpts();
        var v = MV(Rot(o.Rx, o.Ry, o.Rz), new[] { 0.0, len, 0.0 });
        return new[] { v[0] + from[0], v[1] + from[1], v[2] + from[2] };
    }

    // 肢体末端（握点/脚底）：与 limb 同一套旋转。凡是"要握在手里的东西"都必须从这里派生，
    // 不能按世界坐标手摆 —— 否则每次调姿势都会把武器甩到空中。
    public static double[] TipAt(double[] pivot, double len, PrimOpts? o = null) => AxisAt(pivot, -len, o);

    // 从枢轴下垂的肢体：中心 = pivot + R*(0,-len/2,0)，末端（手/脚）= pivot + R*(0,-len,0)
    // 旋转必须真正写进 basis —— 只用来算位移会让四肢永远是竖直盒子，握在手里的武器随之脱手
    public static string Limb(string p, string n, string m, double[] pivot, double len, double w, double d, PrimOpts? o = null) =>
        Prim(p, n, "box", m, AxisAt(pivot, -len / 2, o), new[] { w, len, d }, o);

    // 带旋转的柱/锥按「轴线一端」定位：prim 绕中心旋转，底面锚定配 rx/rz 会把末端甩离握点
    public static string CylEnd(string p, string n, string m, double[] end, double d, double h, PrimOpts? o = null) =>
        Prim(p, n, "cyl", m, AxisAt(end, h / 2, o), new[] { d, h, d }, o);

    public static string ConeEnd(string p, string n, string m, double[] end, double d, double h, PrimOpts? o = null) =>
        Prim(p, n, "cone", m, AxisAt(end, h / 2, o), new[] { d, h, d }, o);

    public static string CsgBox(string parent, string name, string? mat, double[] c, double[] size,
        string op = "union", PrimOpts? o = null)
    {
        o ??= new PrimOpts();
        var sc = Scaled(c);
        var ss = Scaled(size);
        var props = new List<(string, string)> { ("transform", Tf(Rot(o.Rx, o.Ry, o.Rz), sc)), ("size", V3(ss[0], ss[1], ss[2])) };
        if (mat != null)
        {
            Ctx.Mats.Add(mat);
            props.Add(("material_override", $"SubResource(\"StandardMaterial3D_{mat}\")"));
        }
        if (op == "subtract") props.Add(("operation", "2"));
        return Node(name, parent, "CSGBox3D", props);
    }

    // CSG 树只采用根上的 material_override（逐子形状的材质会被忽略）—— 一棵树一种材质，
    // 需要别的颜色的部件必须留在树外，用普通图元叠。
    public static string Combiner(string name, string? parent, string mat)
    {
        Ctx.Mats.Add(mat);
        return Node(name, parent, "CSGCombiner3D",
            new List<(string, string)> { ("material_override", $"SubResource(\"StandardMaterial3D_{mat}\")") });
    }

    // 把「设计坐标」换算进当前 fit —— 原型侧自己放 Marker3D 之类也要走同一个缩放。
    // fit 本身由 EmitScene 管，不在场景外暴露写入口。
    public static double[] Scaled(double[] v) => new[] { v[0] * _f[0], v[1] * _f[1], v[2] * _f[2] };

    public sealed record EmitResult(string RootName, int Count);

    // 写盘：头行 → sub_resource → 根节点 → 其余节点。
    // 场景状态是静态的（一次生成一个场景），所以 build() 抛异常也必须复位 —— 否则下一个
    // 原型脚本会在脏 fit/调色板下静默生成出错误几何。
    public static EmitResult EmitScene(string dir, string rootName, string? uid, string genNote,
        IReadOnlyDictionary<string, MatSpec> palette, Action<string> build, double[]? fit = null)
    {
        _f = fit ?? new[] { 1.0, 1.0, 1.0 };
        _palette = palette;
        _s = new SceneCtx();
        try
        {
            var file = Path.Join(dir, $"{rootName}.tscn");
            // uid 由调用方持有；磁盘上出现别的 uid 说明有人开编辑器存过盘，按表回写并喊一声
            if (uid == null) Console.Error.WriteLine($"⚠ UID 表缺 {rootName}，头行不带 uid");
            try
            {
                using var reader = new StreamReader(file);
                var first = reader.ReadLine() ?? "";
                var m = System.Text.RegularExpressions.Regex.Match(first, "uid=\"([^\"]+)\"");
                var disk = m.Success ? m.Groups[1].Value : null;
                if (disk != null && disk != uid)
                    Console.Error.WriteLine($"⚠ {rootName} 磁盘 uid={disk} 与表不符（被编辑器改过？）→ 按表回写");
            }
            catch
            {
                // 首次生成，尚无文件
            }

            var root = new NodeLine(rootName, null, "Node3D",
                new List<(string, string)> { ("metadata/_generated", JsonStr(genNote)) });
            build(rootName);

            var subs = new List<(string Head, string Body)>();
            foreach (var e in Mesh) subs.Add(($"type=\"{e.Type}\" id=\"{e.Id}\"", e.Body));
            var mats = new List<string>(_s.Mats);
            mats.Sort(StringComparer.Ordinal);
            foreach (var id in mats)
            {
                var spec = _palette[id];
                var body = $"albedo_color = {C3(spec.Rgb)}";
                // 灰盒阶段不写 metallic：项目没有反射环境/探针，高金属度表面只会渲染成纯黑
                if (spec.Metallic != null) body += $"\nroughness = {N(spec.Rough!.Value)}";
                if (spec.Em != null)
                    body += $"\nemission_enabled = true\nemission = {C3(spec.Em)}\nemission_energy_multiplier = 2.2";
                subs.Add(($"type=\"StandardMaterial3D\" id=\"StandardMaterial3D_{id}\"", body));
            }

            var lines = new List<string>
            {
                $"[gd_scene load_steps={subs.Count + 1} format=3{(uid != null ? $" uid=\"{uid}\"" : "")}]",
                "",
            };
            foreach (var (head, body) in subs)
            {
                lines.Add($"[sub_resource {head}]");
                lines.Add(body);
                lines.Add("");
            }

            void Nd(NodeLine n)
            {
                if (n.Parent == null)
                    lines.Add($"[node name=\"{n.Name}\" type=\"{n.Type}\"]");
                else
                {
                    var parentAttr = n.Parent == rootName ? "." : n.Parent.Substring(rootName.Length + 1);
                    lines.Add($"[node name=\"{n.Name}\" type=\"{n.Type}\" parent=\"{parentAttr}\"]");
                }
                foreach (var (k, v) in n.Props) lines.Add($"{k} = {v}");
                lines.Add("");
            }

            Nd(root);
            foreach (var n in _s.Nodes) Nd(n);
            WriteScene(file, lines);
            return new EmitResult(rootName, _s.Nodes.Count);
        }
        finally
        {
            _s = null;
            _f = new[] { 1.0, 1.0, 1.0 };
            _palette = new Dictionary<string, MatSpec>();
        }
    }

    /// <summary>最小 JSON 字符串字面量（生成物标记用；与 JSON.stringify 的可见输出一致）。</summary>
    public static string JsonStr(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // 唯一的写盘出口：行数组 → LF 结尾的 UTF-8 文件（缺 LF 会让整份 diff 变成全文件重写）
    public static void WriteScene(string file, IReadOnlyList<string> lines)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(file, string.Join("\n", lines), new UTF8Encoding(false));
    }
}