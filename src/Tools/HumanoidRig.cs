// 人形骨架画笔 —— 开发期工具公共层（不进游戏、不被游戏 csproj 编译）。
//
// 来历：第二个「有单位要上树」的原型接进来时，从那一份原型 Tools/Gen.cs 里的
// private RigHumanoid **提炼**上来。这条线的出处是 AGENTS.md「沉淀方向」：
// 反复出现的样板要提炼回公共层，原型只留薄 spec。
// 提炼当天把原型侧也一并接了过来：那份 Gen.cs 删掉 private
// RigHumanoid()/Rig/HumanoidBrush/Anchor()，改调本文件的 Humanoid.Rig()/Humanoid.Anchor()，
// 那一批源场景与 .glb 重跑逐字节全等 —— 机制与参数见 docs/模型与动画准则.md。
//
// 约定（与 src/GugaFramework/Tools/README.md 的导出契约一致）：
//   · j_ 前缀 = 关节契约，export_glb.gd 按它建骨架（见 src/Tools/Tscn.cs 的 Joint()）；
//   · 绑定姿势 = 自然站立：四肢沿 -Y 下垂，鞋底落在 y=0，面朝 +Z；
//   · 尺寸一律用 u = h/1.75 的倍数表达（u=1 那一档就是 1.75 米的标准人），
//     换物种只换 h / bulk / 几个节长，不必重摆每一件装备；
//   · 驼背靠**关节位置**逐级前移做出来，不给关节加静态旋转 —— 动作 clip 的旋转轨道
//     写的是绝对值，rest 里的前倾会在动画第一帧被抹平。躯干部件自己转同一个角度去对齐背线。

#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Tools;

/// <summary>
/// 一次人形建骨的结果：关节路径表 + u 基准尺寸 + 骨架画笔。
/// 装备一律挂在 <c>J["handL"] / J["handR"]</c> 的局部系里（经 <see cref="HumanoidBrush.Grip"/> 派生握点），
/// 不要按世界坐标手摆 —— 否则每次调手臂姿势都会把武器甩到空中。
/// </summary>
public sealed class HumanoidRig
{
    public HumanoidRig(Dictionary<string, string> j, double u, double hipY, double leanA,
        double tw, double td, double lw, double hs, double top, HumanoidBrush b)
    {
        J = j;
        U = u;
        HipY = hipY;
        LeanA = leanA;
        Tw = tw;
        Td = td;
        Lw = lw;
        Hs = hs;
        Top = top;
        B = b;
    }

    /// <summary>关节名（root / hips / spine / chest / neck / head / thighL / shinL / footL / 右侧同名 / shoulderL / armL / handL …）→ 节点路径。</summary>
    public readonly Dictionary<string, string> J;

    /// <summary>身高倍数 ＝ h / 1.75；<see cref="HumanoidBrush"/> 的画笔已按它统一放大，装备坐标只需给 u=1 基准值。</summary>
    public readonly double U;

    /// <summary>髋关节离地高度（米，绝对值）。</summary>
    public readonly double HipY;

    /// <summary>背线与竖直方向的夹角（度）；躯干部件转这个角度才对齐得上一条前倾的背。</summary>
    public readonly double LeanA;

    /// <summary>躯干宽 / 厚、肢体宽（u=1 基准）、头尺寸系数（× headK）。</summary>
    public readonly double Tw, Td, Lw, Hs;

    /// <summary>头顶高度（米，绝对值）—— 名牌锚点按它给。</summary>
    public readonly double Top;

    /// <summary>骨架画笔：坐标与尺寸全按 u=1 基准给，出口统一乘 u。</summary>
    public readonly HumanoidBrush B;
}

/// <summary>u 基准的人形画笔（对应更早那套 .mjs 工具的 <c>B.*</c>）。</summary>
public sealed class HumanoidBrush
{
    // 带默认参的画笔委托 —— Func<> 不支持可选参数，而调用侧普遍省略 opts
    public delegate string PrimDel(string par, string n, string m, double[] c, double[] sz,
        Tscn.PrimOpts? o = null);

    public delegate string PrimDelD(string par, string n, string m, double[] a, double d, double h,
        Tscn.PrimOpts? o = null);

    public required Func<double, double> U { get; init; }
    public required PrimDel Box { get; init; }
    public required PrimDel Sph { get; init; }
    public required PrimDelD Cyl { get; init; }
    public required PrimDelD Cone { get; init; }
    public required PrimDelD Disc { get; init; }
    public required PrimDelD CylEnd { get; init; }
    public required PrimDelD ConeEnd { get; init; }

    /// <summary>握在掌心的装备组：原点钉在手掌中心，姿势只在这一处转。</summary>
    public required Func<string, string, double[]?, string> Grip { get; init; }
}

/// <summary>人形骨架与图元选项糖 —— 原型侧的 <c>Build*()</c> 只需要这三个入口。</summary>
public static class Humanoid
{
    /// <summary>图元选项糖：旋转按度给（rx/ry/rz），flat 关掉投影（细小件用）。</summary>
    public static Tscn.PrimOpts O(double rx = 0, double ry = 0, double rz = 0, bool flat = false) =>
        new() { Rx = rx, Ry = ry, Rz = rz, Flat = flat };

    /// <summary>
    /// 头顶锚点：<c>.x</c> = 血条宽度（米），<c>.y</c> = 名牌高度（米）。
    /// 表现层读这两个数，禁止在代码里硬编码血条宽 —— 模型可缩放后血条必须跟着机位与体量走。
    /// </summary>
    public static void Anchor(string parent, double y, double barW = 0) =>
        Tscn.Node("Anchor", parent, "Marker3D",
            new List<(string, string)> { ("transform", Tscn.Tf(Tscn.I3, Tscn.Scaled(new[] { barW, y, 0.0 }))) });

    private static double[] U2(double[] a, double u) => new[] { a[0] * u, a[1] * u, a[2] * u };

    /// <summary>
    /// 髋关节离地高度（米）—— 与 <see cref="Rig"/> 用的是同一条算式（踝 0.06 ＋ 髋臼 0.02 ＋ 大腿  小腿，全乘 u）。
    /// 骑手要「坐在鞍面上」，先得知道他的髋在自己根的上方多少：座位组的落点 ＝ 鞍面高度 − 这个数。
    /// </summary>
    public static double HipY(double h, double thigh = 0.27, double shin = 0.24) =>
        (h / 1.75) * (thigh + shin + 0.02 + 0.06);

    // 只绕 X 摆一个关节角（大腿前摆 / 膝后弯 / 手臂前抬）——单位：度，与 Joint() 的 eul 同口径
    private static double[] E(double rx, double rz = 0) => new[] { rx, 0.0, rz };

    /// <summary>
    /// 建一副人形骨架（16 节）并把躯干四肢摆进绑定姿势，返回关节表与画笔。
    /// 只画骨架与肉体，**装备由调用方在返回的画笔上自己挂**。
    /// </summary>
    /// <param name="p">父节点路径（一般是场景根名）。</param>
    /// <param name="h">头顶身高（米）。</param>
    /// <param name="bulk">横向体量系数，1 ＝ 标准人。</param>
    /// <param name="headK">头大系数，兽人这类大头生物调它。</param>
    /// <param name="lean">前倾量（u=1 基准，米），驼背生物给正值。</param>
    /// <param name="thighYaw">大腿的**绑定姿势**前摆角（度，负 ＝ 向前，与 clips_humanoid.gd 的轴口径一致）——骑手的大腿要跨在坐骑上就靠它。</param>
    /// <param name="kneeYaw">小腿相对大腿的弯角（度，正 ＝ 向后弯，膝只会往后）。</param>
    /// <param name="armYawL">/</param>
    /// <param name="armYawR">手臂的绑定姿势前摆角（度，负 ＝ 向前）——举盾、牵缰、托弓都从这里起。</param>
    /// <param name="legSpread">两条腿各向外开的角度（度）——骑手要跨坐在坐骑两侧就靠它，0 ＝ 并腿站立。</param>
    /// <remarks>
    /// 这四个角写进**关节自身的旋转**（rest），不写进部件：部件一律画在关节局部系的 -Y 上，
    /// 关节一转就整体跟着转，握在手里（<c>B.Grip</c>）的装备随之走。
    /// 动作轨道是「相对 rest 的旋转」，所以轨道里 0 的那一帧正好回到这个姿势，不冲突。
    /// 四条全默认 0 ⇒ 不传就与提炼前的产物逐字节相同（提炼当天拿接入的那批模型验过）。
    /// </remarks>
    public static HumanoidRig Rig(string p, double h = 1.75, double bulk = 1, double thigh = 0.27,
        double shin = 0.24, double arm = 0.60, double spine = 0.15, double chest = 0.27, double neck = 0.30,
        double head = 0.07, double headK = 1, double lean = 0, string matBody = "cloth",
        string matCloth = "cloth", string matSkin = "skin",
        double thighYaw = 0, double kneeYaw = 0, double armYawL = 0, double armYawR = 0,
        double legSpread = 0)
    {
        var u = h / 1.75;
        double Us(double v) => v * u;
        double[] U2f(double[] a) => U2(a, u);
        var socket = 0.02;
        var hipY = HipY(h, thigh, shin); // 踝 0.06 ＋ 髋臼 0.02 ＋ 大腿 ＋ 小腿，见 HipY
        var tw = 0.46 * bulk;
        var td = 0.28 * bulk;
        var lw = 0.15 * bulk;
        var hs = 0.28 * headK;
        // 每节离髋的累计高度 —— lean 按它线性分配成「越往上越往前」，连起来就是一条前倾的背
        var cum = new[] { spine, spine + chest, spine + chest + neck, spine + chest + neck + head };
        double ZOf(int i) => Us(lean * cum[i] / cum[3]);
        var leanA = Math.Atan2(lean, cum[3]) / Tscn.D; // 背线与竖直方向的夹角（度）

        var j = new Dictionary<string, string>();
        j["root"] = Tscn.Joint("root", p, new[] { 0.0, 0.0, 0.0 });
        j["hips"] = Tscn.Joint("hips", j["root"], new[] { 0.0, hipY, 0.0 });
        j["thighL"] = Tscn.Joint("thighL", j["hips"], new[] { -Us(0.12 * bulk), -Us(socket), 0 },
            E(thighYaw, -legSpread));
        j["shinL"] = Tscn.Joint("shinL", j["thighL"], new[] { 0, -Us(thigh), 0 }, E(kneeYaw));
        j["footL"] = Tscn.Joint("footL", j["shinL"], new[] { 0, -Us(shin), 0 });
        j["thighR"] = Tscn.Joint("thighR", j["hips"], new[] { Us(0.12 * bulk), -Us(socket), 0 },
            E(thighYaw, legSpread));
        j["shinR"] = Tscn.Joint("shinR", j["thighR"], new[] { 0, -Us(thigh), 0 }, E(kneeYaw));
        j["footR"] = Tscn.Joint("footR", j["shinR"], new[] { 0, -Us(shin), 0 });
        j["spine"] = Tscn.Joint("spine", j["hips"], new[] { 0.0, Us(spine), ZOf(0) });
        j["chest"] = Tscn.Joint("chest", j["spine"], new[] { 0.0, Us(chest), ZOf(1) - ZOf(0) });
        j["neck"] = Tscn.Joint("neck", j["chest"], new[] { 0.0, Us(neck), ZOf(2) - ZOf(1) });
        j["head"] = Tscn.Joint("head", j["neck"], new[] { 0.0, Us(head), ZOf(3) - ZOf(2) });
        j["shoulderL"] = Tscn.Joint("shoulderL", j["chest"], new[] { -Us(tw / 2 + 0.02), Us(neck * 0.8), 0 });
        j["armL"] = Tscn.Joint("armL", j["shoulderL"], new[] { 0.0, 0.0, 0.0 }, E(armYawL));
        j["handL"] = Tscn.Joint("handL", j["armL"], new[] { 0.0, -Us(arm), 0 });
        j["shoulderR"] = Tscn.Joint("shoulderR", j["chest"], new[] { Us(tw / 2 + 0.02), Us(neck * 0.8), 0 });
        j["armR"] = Tscn.Joint("armR", j["shoulderR"], new[] { 0.0, 0.0, 0.0 }, E(armYawR));
        j["handR"] = Tscn.Joint("handR", j["armR"], new[] { 0.0, -Us(arm), 0 });

        // 躯干：每节填到自己与上一节关节之间，转 leanA 对齐背线；头保持竖直（驼背的生物也要看路）
        Tscn.Cbx(j["hips"], "hips", matBody, new[] { 0.0, Us(0.06), 0.0 }, U2f(new[] { tw + 0.02, 0.20, td }));
        Tscn.Cbx(j["hips"], "belt", matCloth, new[] { 0.0, Us(0.16), 0.0 }, U2f(new[] { tw + 0.04, 0.08, td + 0.04 }));
        Tscn.Cbx(j["spine"], "abdomen", matBody, new[] { 0.0, Us(chest) / 2, 0.0 },
            U2f(new[] { tw - 0.02, chest + 0.10, td - 0.02 }), O(rx: leanA));
        Tscn.Cbx(j["chest"], "chest", matBody, new[] { 0.0, Us(neck) / 2, 0.0 },
            U2f(new[] { tw, neck + 0.08, td }), O(rx: leanA));
        Tscn.Cbx(j["neck"], "neck", matSkin, new[] { 0.0, Us(0.04), Us(0.01) }, U2f(new[] { 0.13, 0.12, 0.13 }));
        Tscn.Cbx(j["head"], "head", matSkin, new[] { 0.0, Us(hs / 2 + 0.05), 0.0 }, U2f(new[] { hs, hs, hs * 0.9 }));
        Tscn.Cbx(j["head"], "face", matSkin, new[] { 0.0, Us(hs * 0.42), Us(hs * 0.45) },
            U2f(new[] { hs * 0.7, hs * 0.5, 0.05 }));

        // 腿：大腿挂髋、小腿挂膝、靴子挂踝（踝关节就在鞋底上方 ankle 处）
        Tscn.Limb(j["thighL"], "thighL", matCloth, new[] { 0.0, 0.0, 0.0 }, Us(thigh), Us(lw), Us(lw + 0.02));
        Tscn.Limb(j["shinL"], "shinL", matCloth, new[] { 0.0, 0.0, 0.0 }, Us(shin), Us(lw - 0.02), Us(lw));
        Tscn.Cbx(j["footL"], "bootL", matCloth, new[] { 0.0, 0.0, Us(0.04) },
            U2f(new[] { lw + 0.03, 0.12, lw + 0.12 }));
        Tscn.Limb(j["thighR"], "thighR", matCloth, new[] { 0.0, 0.0, 0.0 }, Us(thigh), Us(lw), Us(lw + 0.02));
        Tscn.Limb(j["shinR"], "shinR", matCloth, new[] { 0.0, 0.0, 0.0 }, Us(shin), Us(lw - 0.02), Us(lw));
        Tscn.Cbx(j["footR"], "bootR", matCloth, new[] { 0.0, 0.0, Us(0.04) },
            U2f(new[] { lw + 0.03, 0.12, lw + 0.12 }));

        // 手臂自然下垂，手掌给握点留位置
        Tscn.Limb(j["armL"], "armL", matBody, new[] { 0.0, 0.0, 0.0 }, Us(arm), Us(0.10), Us(0.10));
        Tscn.Cbx(j["handL"], "handL", matSkin, new[] { 0.0, -Us(0.05), 0.0 }, U2f(new[] { 0.10, 0.12, 0.10 }));
        Tscn.Limb(j["armR"], "armR", matBody, new[] { 0.0, 0.0, 0.0 }, Us(arm), Us(0.10), Us(0.10));
        Tscn.Cbx(j["handR"], "handR", matSkin, new[] { 0.0, -Us(0.05), 0.0 }, U2f(new[] { 0.10, 0.12, 0.10 }));

        var headY = hipY + Us(cum[3]);
        var b = Brush(u);
        return new HumanoidRig(j, u, hipY, leanA, tw, td, lw, hs, headY + Us(hs + 0.05), b);
    }

    /// <summary>
    /// u 基准画笔 —— 坐标与尺寸全按 u=1 基准给，出口统一乘 u。
    /// 人形与四足坐骑共用这一支笔（坐骑侧的鞍具/护甲用的就是它），所以从 <see cref="Rig"/> 里提出来。
    /// </summary>
    public static HumanoidBrush Brush(double u)
    {
        double Us(double v) => v * u;
        double[] U2f(double[] a) => U2(a, u);
        return new HumanoidBrush
        {
            U = v => Us(v),
            Box = (par, n, m, c, sz, o) => Tscn.Cbx(par, n, m, U2f(c), U2f(sz), o),
            Sph = (par, n, m, c, sz, o) => Tscn.Sp(par, n, m, U2f(c), U2f(sz), o),
            Cyl = (par, n, m, @base, d, hgt, o) => Tscn.Cy(par, n, m, U2f(@base), Us(d), Us(hgt), o),
            Cone = (par, n, m, @base, d, hgt, o) => Tscn.Cn(par, n, m, U2f(@base), Us(d), Us(hgt), o),
            Disc = (par, n, m, c, d, t, o) => Tscn.Disc(par, n, m, U2f(c), Us(d), Us(t), o),
            CylEnd = (par, n, m, end, d, hgt, o) => Tscn.CylEnd(par, n, m, U2f(end), Us(d), Us(hgt), o),
            ConeEnd = (par, n, m, end, d, hgt, o) => Tscn.ConeEnd(par, n, m, U2f(end), Us(d), Us(hgt), o),
            Grip = (par, name, eul) => Tscn.GrpT(name, par, new[] { 0.0, 0.0, 0.0 }, eul),
        };
    }
}
