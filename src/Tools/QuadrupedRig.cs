// 四足坐骑骨架画笔 —— 开发期工具公共层（不进游戏、不被游戏 csproj 编译）。
//
// 为什么要有这一件：有原型的兵种里骑手占了两条（骑兵 / 狼骑），
// 而坐骑**不吃人形骨架**（造型准则里明写的唯一例外）。战马与战狼是同一副骨架换参数，
// 所以骨架本身属于公共层，「这匹马长什么样」才留在原型侧 —— 与 HumanoidRig.cs 同一条分工线。
//
// 约定（与 src/Tools/Tscn.cs、src/GugaFramework/Tools/README.md 的导出契约一致）：
//   · 骨名一律带 `m_` 前缀：骑手的人形骨（hips/chest/armR…）与坐骑骨挂在**同一副骨架**上，
//     两边不能撞名 —— 导出器按「骨名 → 序号」给动作轨道寻骨，重名会静默覆盖；
//   · 绑定姿势 = 四脚站在 y=0、面朝 +Z（与步战单位同一条朝向契约）；
//   · 尺寸一律用 u = 肩高/1.50 的倍数表达（u=1 ＝ 肩高 1.5 米的马；狼给 u≈0.63）；
//   · 骑手挂在 <c>J["saddle"]</c> 下面 —— 挂在这里 ⇒ 坐骑背线一颠，骑手跟着颠。
//   · 腿的三节链：上段（肩胛/大腿）→ 下段（膝/飞节以下）→ 蹄，串在各自关节局部系的 -Y 上；
//     关节角 rx 正 ＝ 向后摆（与 clips_humanoid.gd / clips_mount.gd 同一条轴口径）。

#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Tools;

/// <summary>
/// 一次四足建骨的结果：关节路径表 + u 基准尺寸 + 画笔。
/// 鞍具/鬃毛/护甲一律由调用方挂在返回的关节上（用 <see cref="HumanoidBrush"/> 这支 u 基准画笔）。
/// </summary>
public sealed class QuadrupedRig
{
    public QuadrupedRig(Dictionary<string, string> j, double u, double withers, double backY,
        double saddleY, double bodyLen, HumanoidBrush b)
    {
        J = j;
        U = u;
        Withers = withers;
        BackY = backY;
        SaddleY = saddleY;
        BodyLen = bodyLen;
        B = b;
    }

    /// <summary>关节名（root / hips / spine / chest / neck / head / tail / saddle ＋ 四条腿 legX/kneeX/hoofX，X ∈ FL FR BL BR）→ 节点路径。</summary>
    public readonly Dictionary<string, string> J;

    /// <summary>身高倍数 ＝ 肩高 / 1.50；画笔已按它统一放大，装备坐标只需给 u=1 基准值。</summary>
    public readonly double U;

    /// <summary>肩隆（withers）离地高度（米）—— 马鞍与骑手座位按它给。</summary>
    public readonly double Withers;

    /// <summary>背线（m_spine 关节）离地高度（米）。</summary>
    public readonly double BackY;

    /// <summary>鞍面（m_saddle 关节）离地高度（米）—— 骑手座位组的落点 ＝ 这个数 − 骑手的髋高。</summary>
    public readonly double SaddleY;

    /// <summary>躯干长（米，髋关节到肩关节）。占地那道检查吃的是含颈含尾的全长，见原型侧 Check。</summary>
    public readonly double BodyLen;

    /// <summary>u 基准画笔（与人形共用同一支，出口统一乘 u）。</summary>
    public readonly HumanoidBrush B;
}

/// <summary>四足坐骑骨架 —— 原型侧的 <c>Build*()</c> 只需要 <see cref="Rig"/> 这一个入口。</summary>
public static class Quadruped
{
    /// <summary>
    /// 建一副四足骨架（17 节：躯干 6 + 尾 1 + 骑手座 1 + 四条腿各 3 节）并把躯干四肢摆进绑定姿势。
    /// 只画骨架与肉体，**鞍具、鬃毛、护甲由调用方在返回的画笔上自己挂**。
    /// </summary>
    /// <param name="p">父节点路径（一般是场景根名）。</param>
    /// <param name="h">肩高（米），u ＝ h / 1.50。</param>
    /// <param name="body">躯干长系数（u=1 基准）——马 1.0，狼 1.08 更 elongated。</param>
    /// <param name="legLen">腿长系数（u=1 基准）——马 1.0，狼 0.80（腿短、背贴近地面）。</param>
    /// <param name="bulk">横向体量系数（u=1 基准）。</param>
    /// <param name="neckUp">颈上抬系数 —— 马 1.0（脖子长而高），狼 0.45（短而平，头探在身前）。</param>
    /// <param name="muzzle">口鼻长系数 —— 马的长吻 1.0，狼的短宽吻 0.72。</param>
    /// <param name="tailDrop">尾巴下垂系数（度）。</param>
    /// <param name="tail">画不画那两段尾巴几何。缺省 true；传 false 的调用方只拿到骨架（见下面那一段的注记）。</param>
    public static QuadrupedRig Rig(string p, double h = 1.50, double body = 1, double legLen = 1,
        double bulk = 1, double neckUp = 1, double muzzle = 1, double tailDrop = 1, bool tail = true,
        string matBody = "hide", string matDark = "dark", string matHoof = "hoof")
    {
        var u = h / 1.50;
        double Us(double v) => v * u;
        double[] U2f(double[] a) => new[] { a[0] * u, a[1] * u, a[2] * u };

        // ---- u=1 基准：背线 1.44、肩胛关节 1.34、蹄 0.24 ⇒ 腿要合起来够到地面（Leg 里按角度自动算段长）----
        var backY = 1.44;
        var halfBody = 0.52 * body;
        var bw = 0.28 * bulk;      // 半宽
        var hoofY = 0.24 * legLen; // 蹄关节离地

        var j = new Dictionary<string, string>();
        j["root"] = Tscn.Joint("m_root", p, new[] { 0.0, 0.0, 0.0 });
        j["hips"] = Tscn.Joint("m_hips", j["root"], new[] { 0.0, Us(backY + 0.02), -Us(halfBody) });
        j["spine"] = Tscn.Joint("m_spine", j["hips"], new[] { 0.0, Us(-0.02), Us(halfBody * 0.98) });
        j["chest"] = Tscn.Joint("m_chest", j["spine"], new[] { 0.0, Us(0.04), Us(halfBody * 0.98) });
        j["neck"] = Tscn.Joint("m_neck", j["chest"], new[] { 0.0, Us(0.22 * neckUp), Us(0.17) });
        j["head"] = Tscn.Joint("m_head", j["neck"], new[] { 0.0, Us(0.20 * neckUp), Us(0.17) });
        j["tail"] = Tscn.Joint("m_tail", j["hips"], new[] { 0.0, Us(0.14), -Us(0.40) });
        // 骑手座位：背线正上方一个空关节。原型侧在它下面挂人形骨架 —— 坐骑背一颠，骑手跟着颠。
        j["saddle"] = Tscn.Joint("m_saddle", j["spine"], new[] { 0.0, Us(0.14), Us(0.04) });

        // ---- 四条腿：前腿挂在胸（几乎竖直，膝往后弯），后腿挂在髋（大腿往后下、飞节再把小腿带向前）----
        // jointY/hoofAbs 都是**离地绝对高度**（米）—— 段长由 Leg 按角度解出来，四蹄一律踩在 y=0。
        var hoofAbs = Us(hoofY);
        var frontY = Us(backY + 0.04 - 0.10);
        var hindY = Us(backY + 0.02 - 0.10);
        Leg(j, "FL", j["chest"], new[] { -Us(bw * 0.70), Us(-0.10), Us(0.40) }, 0, 0, frontY, hoofAbs,
            matBody, matHoof);
        Leg(j, "FR", j["chest"], new[] { Us(bw * 0.70), Us(-0.10), Us(0.40) }, 0, 0, frontY, hoofAbs,
            matBody, matHoof);
        Leg(j, "BL", j["hips"], new[] { -Us(bw * 0.72), Us(-0.10), -Us(0.34) }, 16, -26, hindY, hoofAbs,
            matBody, matHoof);
        Leg(j, "BR", j["hips"], new[] { Us(bw * 0.72), Us(-0.10), -Us(0.34) }, 16, -26, hindY, hoofAbs,
            matBody, matHoof);

        // ---- 肉体：胸-腹-臀三段箱体 + 一坨臀肌，全挂在各自关节上（骨骼一动，肉跟着走）----
        Tscn.Cbx(j["chest"], "shoulder", matBody, U2f(new[] { 0.0, -0.18, 0.02 }),
            U2f(new[] { bw * 2 + 0.02, 0.50, 0.46 }));
        Tscn.Cbx(j["spine"], "barrel", matBody, U2f(new[] { 0.0, -0.20, 0.0 }),
            U2f(new[] { bw * 2 - 0.04, 0.50, 0.94 * body }));
        Tscn.Cbx(j["hips"], "haunch", matBody, U2f(new[] { 0.0, -0.18, -0.04 }),
            U2f(new[] { bw * 2 + 0.06, 0.56, 0.54 * body }));
        Tscn.Cbx(j["hips"], "rump", matBody, U2f(new[] { 0.0, 0.10, -0.24 }),
            U2f(new[] { bw * 2 - 0.04, 0.28, 0.30 * body }));

        // ---- 颈：一根斜柱从肩隆顶送到头颅，长度与仰角都从「前伸 / 上抬」两个位移算出来 ----
        var neckRun = 0.34;
        var neckRise = 0.42 * neckUp;
        var neckL = Math.Sqrt(neckRun * neckRun + neckRise * neckRise);
        var neckA = Math.Atan2(neckRun, neckRise) / Tscn.D;
        Tscn.CylEnd(j["chest"], "neck", matBody, U2f(new[] { 0.0, 0.06, 0.12 }), 0.26 * bulk, Us(neckL),
            Humanoid.O(rx: neckA));
        Tscn.Cbx(j["neck"], "throat", matBody, U2f(new[] { 0.0, -0.05, 0.06 }),
            U2f(new[] { 0.18 * bulk, 0.17, 0.28 }));

        // ---- 头：颅 + 吻 + 鼻镜 + 两片耳 + 两只眼（吻长走 muzzle：马长吻、狼短宽吻）----
        Tscn.Cbx(j["head"], "skull", matBody, U2f(new[] { 0.0, 0.02, 0.06 }),
            U2f(new[] { 0.20 * bulk, 0.20, 0.26 }));
        Tscn.Cbx(j["head"], "muzzle", matBody, U2f(new[] { 0.0, -0.05, 0.19 + 0.13 * muzzle }),
            U2f(new[] { 0.15 * bulk, 0.13, 0.26 * muzzle }));
        Tscn.Cbx(j["head"], "nose", matDark, U2f(new[] { 0.0, -0.03, 0.19 + 0.26 * muzzle }),
            U2f(new[] { 0.11 * bulk, 0.08, 0.05 }));
        Tscn.Cbx(j["head"], "earL", matBody, U2f(new[] { -0.08 * bulk, 0.15, -0.02 }),
            U2f(new[] { 0.05, 0.15, 0.09 }), Humanoid.O(rz: -10));
        Tscn.Cbx(j["head"], "earR", matBody, U2f(new[] { 0.08 * bulk, 0.15, -0.02 }),
            U2f(new[] { 0.05, 0.15, 0.09 }), Humanoid.O(rz: 10));
        Tscn.Cbx(j["head"], "eyeL", matDark, U2f(new[] { -0.10 * bulk, 0.04, 0.07 }),
            U2f(new[] { 0.03, 0.05, 0.05 }), Humanoid.O(flat: true));
        Tscn.Cbx(j["head"], "eyeR", matDark, U2f(new[] { 0.10 * bulk, 0.04, 0.07 }),
            U2f(new[] { 0.03, 0.05, 0.05 }), Humanoid.O(flat: true));

        // ---- 尾：从屁股后沿起、向下后方甩两段（第一版起在臀箱内部，十视图里整条尾巴被吃掉）----
        // `tail: false` 只画骨架不画这两段几何（m_tail 关节照样建，骨数与动作轨道一个字不动），
        // 供「只要身体、头和四肢」那种调用方用 —— 尾巴一节甩到背上方就既读不出剪影、
        // 又要在调用侧额外补一件接两段。缺省 true：另有原型在用这份骨架画坐骑。
        if (tail)
        {
            var tailA = Tscn.CylEnd(j["tail"], "tailA", matBody, U2f(new[] { 0.0, 0.0, -0.04 }), 0.12,
                Us(0.34), Humanoid.O(rx: 200 * tailDrop));
            var tailTip = Tscn.AxisAt(U2f(new[] { 0.0, 0.0, -0.04 }), Us(0.34), Humanoid.O(rx: 200 * tailDrop));
            Tscn.CylEnd(tailA, "tailB", matDark, new[] { tailTip[0], tailTip[1], tailTip[2] }, 0.08, Us(0.28),
                Humanoid.O(rx: 210 * tailDrop));
        }

        return new QuadrupedRig(j, u, h, Us(backY), Us(backY + 0.14), Us(halfBody * 2), Humanoid.Brush(u));
    }

    /// <summary>
    /// 一条腿的三节链：上段 → 下段 → 蹄。两个角度写在关节的绑定姿势里（rx 正 ＝ 向后），
    /// 部件一律画在各自关节局部系的 -Y 上。
    ///
    /// **段长按角度自动解**：给定这条腿关节的离地高度 <paramref name="jointY"/> 与蹄顶高度
    /// <paramref name="hoofY"/>，两段的垂直落差之和必须正好等于这段距离 ——
    /// 于是怎么调角度四蹄都踩在地上。第一版把段长写死，改一次姿势就有两条腿悬空，
    /// 马看着像踩在棉花上（体检单十视图实测）。
    /// </summary>
    private static void Leg(Dictionary<string, string> j, string tag, string parent, double[] pivot,
        double upperA, double lowerA, double jointY, double hoofY, string matLimb, string matHoof)
    {
        var k = Math.Cos(upperA * Tscn.D) + Math.Cos((upperA + lowerA) * Tscn.D);
        var seg = (jointY - hoofY) / k; // 两段等长时的单段长
        var upper = Tscn.Joint("m_leg" + tag, parent, pivot, new[] { upperA, 0.0, 0.0 });
        Tscn.Limb(upper, "upper" + tag, matLimb, new[] { 0.0, 0.0, 0.0 }, seg, seg * 0.26, seg * 0.30);
        var lower = Tscn.Joint("m_knee" + tag, upper, new[] { 0.0, -seg, 0.0 }, new[] { lowerA, 0.0, 0.0 });
        Tscn.Limb(lower, "lower" + tag, matLimb, new[] { 0.0, 0.0, 0.0 }, seg, seg * 0.16, seg * 0.19);
        var hoof = Tscn.Joint("m_hoof" + tag, lower, new[] { 0.0, -seg, 0.0 });
        Tscn.Cbx(hoof, "hoof" + tag, matHoof, new[] { 0.0, -hoofY / 2, 0.02 },
            new[] { hoofY * 0.60, hoofY, hoofY * 0.72 });
        j["leg" + tag] = upper;
        j["knee" + tag] = lower;
        j["hoof" + tag] = hoof;
    }
}
