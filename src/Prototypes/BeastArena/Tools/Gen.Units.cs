#nullable enable
// 巨兽竞技场 造型源场景生成器 —— 单位域（巨兽三形态 + 地面人形六件）。
//
// 本文件是 Gen 的部分实现（partial），按域拆分见 Gen.cs 文件头那一段。
// 骨架骨名与动作名口径（`m_` 前缀四足骨架 + clips_beast.gd / 人形骨架 + clips_humanoid.gd）
// 与拆分前一字不差。

using System;
using System.Collections.Generic;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    // ============================================================
    //  巨兽六套 —— 一副四足骨架、三套形态剪影 × 一阶 / 二阶两档
    //
    //  六件真模型：`Beast` / `BeastDesert` / `BeastGlutton`（一阶三件，现役，几何与材质不动）
    //  与 `BeastTier2` / `BeastDesertTier2` / `BeastGluttonTier2`（二阶三件，T10 新增）。
    //  骨架骨名与动作名六件一字不差（`m_` 前缀那副四足骨架 + `clips_beast.gd` 的八条），
    //  形态与位阶的差别全在下面那张形态表里。
    //
    //  **不改 `Quadruped.Rig` 的签名** —— 那是公共层，别的原型也在用。
    //  背峰、腹围、厚皮板、小腿块一律用 `BuildBeast` 里的**额外身体图元**做
    //  （照 `BuildMonster` 挂 `hump` 那种画法：挂在关节下，坐标是那个关节的局部系）。
    //
    //  **原始态那一行必须逐字复现现役那次调用**：形态表里多一个数都改 `Beast.glb` 的字节，
    //  而入库那份的 SHA256 是硬判据（ticket 06 验收口径第三条）。
    //
    //  **三件同档**（验收口径第二条）：全长 89.58 / 体宽 52.93 / 总高 30.48 米（原始态实测），
    //  彼此差 ≤ 0.5 米。相机边界、甲板高度、停靠净空都按这一档算过 ——
    //  哪一件超了是造型错，不是相机错。
    //
    //  形态表里那一列「离地高度」全是**设计米**（fit 放大之前的那把尺子，u = 肩高/1.50 = 8）。
    //  画的时候减去该关节的离地高度就是关节局部系的 y。
    // ============================================================

    /// <summary>背脊上的一件板（厚皮披肩 / 平背板）：宽 / 厚 / 长（设计米）、底面离地高度与中心 Z（脊柱局部系）。</summary>
    private sealed record BackSlab(double Width, double Thickness, double Length, double Bottom, double Z);

    /// <summary>背峰：底径 / 高（设计米）、锥底离地高度与中心 Z（脊柱局部系）。</summary>
    private sealed record BackPeak(double Diameter, double Height, double Base, double Z);

    /// <summary>腹围：宽 / 高 / 长（设计米）与它的底面离地高度。</summary>
    private sealed record Paunch(double Width, double Height, double Length, double Bottom);

    /// <summary>小腿块：宽 / 高 / 厚（设计米）与它在膝关节局部系里的中心高度（负数 ＝ 往下）。</summary>
    private sealed record ShinMass(double Width, double Height, double Depth, double CenterY);

    /// <summary>巨兽一档形态的造型参数 —— 每件真模型的差别全在这一行里。</summary>
    private sealed record BeastLook(
        string Name,
        string MatBody,
        string MatDark,
        double Bulk,
        double LegLen,
        double NeckUp,
        double Muzzle,
        BackSlab? Back = null,
        BackPeak? Peak = null,
        Paunch? Belly = null,
        ShinMass? Shin = null,
        // ---- T10 追加（默认 null：前三个形态那三行的产出字节一个字不动）----
        BackPeak? Peak2 = null,
        BackSlab? Deck = null);

    private static readonly BeastLook[] BeastLooks =
    [
        // 原始态：现役那一版，逐字复现（表里多一个数都改 Beast.glb 的字节）。
        new("Beast", "hide", "hideD", 5.8, 0.72, 0.45, 0.9),

        // 荒漠态：沙色厚皮、体更瘦、背脊一峰更高、颈略长。
        //  · bulk 5.8 → 5.15：躯干、头、颈都瘦一圈（体宽从 52.93 掉到 47.10 米）。
        //  · 厚皮披肩 26.464 宽 ＝ 原始态那一档体宽：**最宽的那一件由它守住**，
        //    于是「体更瘦」（躯干）与「三件同档」（总宽）两条同时成立（瘦的是躯干，宽的是那层皮）。
        //    披肩只盖肩背那一段（长 12 米），不盖满整条背 —— 盖满就成了一张盖住全身的板，
        //    俯视图里连「瘦」都读不出来（出图实测）。
        //  · 背峰：锥底 12.00、锥尖 15.20、底径 3.60 —— 细高的一根锥，
        //    从披肩上立起来，高出臀背（13.60）那一档，是背上唯一的一峰；
        //    锥尖压在耳尖（15.28）之下，所以总高那一档不变。
        //  · neckUp 0.45 → 0.48：颈略长（颈长 +1.6%），总高只涨 0.20 米。
        new("BeastDesert", "hideSand", "hideSandD", 5.15, 0.72, 0.48, 0.9,
            Back: new(26.464, 1.80, 12.0, 11.0, 10.5),
            Peak: new(3.6, 3.2, 12.0, 8.0)),

        // 暴食态：深色皮、腹更大、腿更短粗、背脊低平。
        //  · 腹围垂到 6.20：桶身底面在 7.92，于是大腿上半截被大肚子盖住 —— 腿看着短了一截。
        //  · legLen 0.72 → 0.95：蹄关节抬高，小腿更短、蹄更大。
        //  · 小腿块 3.4 宽：粗过腿身两倍，从肚子底下探出来。
        //  · 平背板顶面 13.60 ＝ 臀背那一档：背线一路平过去，不象荒漠态那样在背上起峰。
        new("BeastGlutton", "hideDark", "hideDarkD", 5.8, 0.95, 0.45, 0.9,
            Back: new(26.0, 2.00, 30.5, 11.6, 0.0),
            Belly: new(24.8, 3.60, 25.0, 6.2),
            Shin: new(3.4, 3.6, 3.2, -1.2)),

        // ---- T10 二阶三形态：同一副四足骨架、同一套动作名，位阶差在剪影上 ----
        // 三条判据（规格「资产范围」与 ticket 11）：体量（bulk 上一档）、背峰（更高或多一峰）、
        // 甲板承托（背上一块木质鞍板，读得出「二阶的镇子有多大一块地方要驮」）。
        // 肩高 12.0 米与腿长系数照抄一阶 —— 底座与甲板的对位高度全按这一档算过，只宽不破高。
        // 鞍板都压进桶身 ≥ 0.72 米（离线校验器的悬空件余量，与一阶背脊板同一口径）。
        //
        // 原始态二阶：bulk 5.8 → 6.4（体宽 52.93 → 58.3 米实测档），鞍板宽 30.0 米，
        // 宽过桶身、窄过臀肌 —— 从俯视看是一圈探出背线的平台；板前一根矮峰。
        new("BeastTier2", "hide", "hideD", 6.4, 0.72, 0.45, 0.9,
            Peak: new(4.4, 2.6, 11.9, 9.0),
            Deck: new(30.0, 1.5, 24.0, 11.0, 0.0)),

        // 荒漠态二阶：披肩与主峰照一阶（主峰再高 1.0 米），臀后加第二峰成双峰剪影；
        // 鞍板压在披肩后面那一段裸背上（披肩只盖肩背，见一阶那段注记）。
        new("BeastDesertTier2", "hideSand", "hideSandD", 5.7, 0.72, 0.48, 0.9,
            Back: new(26.464, 1.80, 12.0, 11.0, 10.5),
            Peak: new(3.6, 4.2, 12.0, 8.0),
            Peak2: new(2.6, 2.8, 12.0, 5.0),
            Deck: new(26.5, 1.5, 11.0, 11.0, -8.5)),

        // 暴食态二阶：平背板与腹围照一阶再加一档（腹更低、腿块更粗），
        // 鞍板压在平背板上面（板顶 13.6 → 板底 13.0 吃 0.6 米余量，宽 28 探出背板 26）。
        new("BeastGluttonTier2", "hideDark", "hideDarkD", 6.4, 0.95, 0.45, 0.9,
            Back: new(26.0, 2.00, 30.5, 11.6, 0.0),
            Peak: new(3.2, 2.4, 14.0, -6.0),
            Belly: new(26.0, 4.0, 26.0, 5.6),
            Shin: new(3.8, 4.0, 3.6, -1.2),
            Deck: new(28.0, 1.6, 22.0, 13.0, 1.5)),
    ];

    /// <summary>
    /// 巨兽（唯一的会动的巨型件，也是占地上限那一栏的锚）
    /// 四足骨架走公共层 Quadruped.Rig
    ///
    ///  2026-10-15 **简化到调试最小集：只留身体、头和四条腿。**
    ///
    ///  为什么简化（出图实测出来的，不是审美判断）：这一件原来有 83 个节点，其中 34 个是
    ///  背上那块 24 × 24 米的木质驮台（台面 + 12 块板 + 4 段栏杆 + 4 根柱 + 4 根栏柱）、
    ///  四块肩臀肌肉垫、一对后掠角、颈背两根骨刺与鼻梁上的铜箍。
    ///  镇子落在磁悬浮底座上，而底座就压在驮台正上方 —— **两层 24 米见方的木板叠在一起**，
    ///  出图上「巨兽」只剩一堆认不出头、也认不出腿的深褐色板块。
    ///  干扰项先删干净，调位姿与步态时画面上才读得出来是哪一块在动。
    ///
    ///  骨架那 20 节与 `clips_beast.gd` 的八条动作这一轮一个字不动；
    ///  尾巴走 `tail: false`（公共层不画尾几何，`m_tail` 关节照样建），下面那截短尾桩挂在 `m_tail` 下。
    ///
    ///  2026-10-28 **模型审查修复（票 03）**：读不出「一头兽」的问题出在剪影——平板躯干、横条头、
    ///  平切的墙臀。这一轮补两层件，全部锁在现有 AABB 以内（长 89.58 / 一阶宽 52.93 / 高 30.48–30.68）：
    ///  · `AddBeastAnatomy`（共用件，六件都画）：下颌、双颊、眉骨、獠牙、颈脊、喉垂、
    ///    肩胛、臀肌、大腿肉、短尾桩——头、颈、四肢、尾在十视图里都数得出来。
    ///  · `AddBeastExtras`（形态差异）：荒漠态加背脊纵脊与细高锥、暴食态腹再垂 0.2 米档、
    ///    二阶鞍板加边梁与板缝、荒漠二阶双峰拉开间距。`Beast` 这一行的附加件仍是零。
    ///
    ///  尾桩口径：关节在臀箱内部、尾根埋进臀箱接上，露出臀底的可见段 ≤ 1.5 米，
    ///  不越过臀后端这条 -Z 极值——AABB 长度一格不增。
    ///
    ///  2026-09-30 那次放大留下的数不变：肩高 12.0 米这一组参数照旧，
    ///  所以巨兽占地与离地高度与入库那份一致，逻辑层一个坐标都没动。
    /// ============================================================
    private static Tscn.EmitResult BuildBeast(string root, BeastLook look)
    {
        return Emit(PathDef.Units(root), look.Name, r =>
        {
            var q = Quadruped.Rig(r, h: 12.0, body: 3.7, legLen: look.LegLen, bulk: look.Bulk,
                neckUp: look.NeckUp, muzzle: look.Muzzle, tail: false,
                matBody: look.MatBody, matDark: look.MatDark, matHoof: "hoof");

            // 共用件：对六件都生效的头脸 / 颈 / 肩臀 / 大腿 / 尾桩。
            AddBeastAnatomy(q, look);

            // 形态附加件：背板 / 背峰 / 腹围 / 小腿块 / 鞍板与各自的形态差异。
            AddBeastExtras(q, look);

            // 头顶锚点：.x = 血条宽（米）、.y = 名牌高度。**给的是 fit 之前的数** ——
            // 锚点与图元、关节枢轴走同一个 `Scaled`，体量放大那一档（×2）会把它一并放大。
            // 21.0 × 2 = 42.0 米：剪影最高处实测 30.4 米（耳朵与颈），名牌浮在它上面。
            // 血条宽 4.0 米 × 2 = 8.0 米，是体量放大那一档按 45 米巨兽的 4.00 米折出来的。
            Humanoid.Anchor(r, 21.0, 4.0);
        });
    }

    /// <summary>
    /// 形态附加件 —— 背脊板 / 背峰 / 腹围 / 小腿块 / 鞍板，与 2026-10-28 票 03 的形态差异。
    ///
    /// <para>每件都挂在某个关节下（走骨架、跟着骨动），坐标是**那个关节的局部系**：
    /// 表里给的「离地高度」减去该关节的离地高度就是局部 y。脊柱那一件（背脊板 / 背峰 / 腹围）
    /// 挂在 <c>m_spine</c> 下，小腿块挂在四个 <c>m_knee*</c> 下。</para>
    ///
    /// <para>**为什么背脊板要那么宽**：体宽那一档（52.93 米）由原始态的臀肌守住，
    /// 而荒漠态的躯干瘦到 47.10 米 —— 那件宽皮板就是荒漠态的最宽处，
    /// 于是「体更瘦」（躯干）与「三件同档」（总宽）两条同时成立。</para>
    ///
    /// <para>**表里的形态参数一个字不动**；差异落在画法上：荒漠态的背峰改细高锥（锥顶那一档不动）、
    /// 披肩上加一道纵向脊；暴食态的腹围再垂 0.2 米、一阶小腿块再粗一档；
    /// 原始态二阶的鞍板加边梁与板缝、前峰加宽；荒漠态二阶双峰拉开间距。
    /// 原始态本体（<c>Beast</c>）除共用件外附加件仍是零。</para>
    /// </summary>
    private static void AddBeastExtras(QuadrupedRig q, BeastLook look)
    {
        var M = q.J;
        bool desert = look.Name is "BeastDesert" or "BeastDesertTier2";
        bool glutton = look.Name is "BeastGlutton" or "BeastGluttonTier2";

        if (look.Back is { } back)
        {
            // 背脊板：厚皮披肩（荒漠态）/ 平背板（暴食态）。底面压进桶身 0.92 米 ——
            // 离线校验器判「悬空件」靠的是图元之间挨着，这一层余量就是给它吃的。
            Tscn.Cbx(M["spine"], "backSlab", look.MatBody,
                new[] { 0.0, back.Bottom + back.Thickness / 2 - q.BackY, back.Z },
                new[] { back.Width, back.Thickness, back.Length });

            if (desert)
            {
                // 背脊纵脊：披肩正中立一道暗色矮脊 —— 「皮更紧、脊更硬」。
                // 底面压进披肩 0.2 米，脊顶 13.5 米（×u 那一档）仍远在耳尖之下，不破总高。
                Tscn.Cbx(M["spine"], "backRidge", look.MatDark,
                    new[] { 0.0, back.Bottom + back.Thickness + 0.25 - q.BackY, back.Z },
                    new[] { 0.9, 0.9, back.Length - 2.0 });
            }
        }

        if (look.Peak is { } peak)
        {
            // 背峰：锥底坐进厚皮披肩里，锥尖高出臀背那一档 —— 背上一峰。
            double d = peak.Diameter;
            double baseY = peak.Base;
            double h = peak.Height;
            if (desert)
            {
                // 荒漠态主峰改细高锥：底径 3.6 → 2.6，锥底沉进披肩一档，锥顶那一档原样不动
                // （一阶 15.2 / 二阶 16.2 米，×u 之后）—— 「峰值不超过当前高度档」。
                d = 2.6;
                baseY = 11.0;
                h = peak.Base + peak.Height - baseY;
            }
            else if (look.Name == "BeastTier2")
            {
                d = 6.0; // 原始态二阶：前峰加宽，锥顶那一档不动。
            }
            Tscn.Cn(M["spine"], "backPeak", look.MatBody,
                new[] { 0.0, baseY - q.BackY, peak.Z }, d, h);
        }

        // ---- T10 追加：第二峰与鞍板（一阶三行的这两格都是 null，产出字节不动）----
        if (look.Peak2 is { } peak2)
        {
            // 荒漠态二阶的第二峰：从主峰后拉远到 z=1.5，锥底坐进桶身顶 —— 双峰间距拉开才读得出位阶。
            Tscn.Cn(M["spine"], "backPeak2", look.MatBody,
                new[] { 0.0, 11.3 - q.BackY, 1.5 }, peak2.Diameter, 3.2);
        }

        if (look.Deck is { } deck)
        {
            // 鞍板：背上那块木质承托平台（甲板承托），读「二阶的镇子要驮多大一块地方」。
            // 色走 deckWood —— 与镇地面同一档木头，一眼是「给镇子垫的台」，不是另一层皮。
            // 板探出桶身那一圈边才读得出平台；板底压进背 ≥ 0.6 米给悬空件检查吃。
            Tscn.Cbx(M["spine"], "saddleDeck", "deckWood",
                new[] { 0.0, deck.Bottom + deck.Thickness / 2 - q.BackY, deck.Z },
                new[] { deck.Width, deck.Thickness, deck.Length });

            if (look.Name == "BeastTier2")
            {
                // 边梁四道沿板缘走一圈、底面压进板面 0.2 米；板缝四条纵铺，缝顶只高过板面 0.15 米。
                // 都不出板的 30×24 米范围，也不碰二阶那档 60.00 米的宽与总高。
                double top = deck.Bottom + deck.Thickness;
                double hw = deck.Width / 2;
                double hl = deck.Length / 2;
                foreach (var sgn in new[] { -1.0, 1.0 })
                {
                    Tscn.Cbx(M["spine"], "deckRimF", "deckWood",
                        new[] { 0.0, top + 0.25 - q.BackY, deck.Z + sgn * (hl - 0.35) },
                        new[] { deck.Width, 0.9, 0.7 });
                    Tscn.Cbx(M["spine"], "deckRimS", "deckWood",
                        new[] { sgn * (hw - 0.35), top + 0.25 - q.BackY, deck.Z },
                        new[] { 0.7, 0.9, deck.Length - 1.4 });
                }
                foreach (var sx in new[] { -10.0, -5.0, 5.0, 10.0 })
                    Tscn.Cbx(M["spine"], "deckSeam", "woodD",
                        new[] { sx, top - q.BackY, deck.Z },
                        new[] { 0.3, 0.3, deck.Length - 1.0 });
            }
        }

        if (look.Belly is { } belly)
        {
            // 腹围：垂在桶身底下（桶身底面 7.92），大腿上半截被它盖住 —— 腿看着短了一截。
            // 票 03：暴食态两档再垂 0.2 米，上缘那一档不动（增高补回去）。
            double bottom = glutton ? belly.Bottom - 0.2 : belly.Bottom;
            double height = glutton ? belly.Height + 0.2 : belly.Height;
            Tscn.Cbx(M["spine"], "belly", look.MatBody,
                new[] { 0.0, bottom + height / 2 - q.BackY, 0.0 },
                new[] { belly.Width, height, belly.Length });
        }

        if (look.Shin is { } shin)
        {
            // 小腿块：四条腿各一块，挂在膝关节下 —— 粗腿的「粗」在这里。
            // 票 03：暴食一阶再粗一档（宽高各 +0.6 米），仍不超体宽那一档。
            double bump = look.Name == "BeastGlutton" ? 0.6 : 0.0;
            foreach (var tag in new[] { "FL", "FR", "BL", "BR" })
                Tscn.Cbx(M["knee" + tag], "shin" + tag, look.MatBody,
                    new[] { 0.0, shin.CenterY, 0.0 },
                    new[] { shin.Width + bump, shin.Height, shin.Depth + bump });
        }
    }

    /// <summary>
    /// 共用件 —— 头脸 / 颈 / 肩臀 / 大腿 / 短尾桩，六件巨兽都画（票 03 造型包 1–5）。
    ///
    /// <para>坐标与 <c>Quadruped.Rig</c> 里 <c>U2f</c> 同一把尺：局部函数 <c>hd</c> 给的是
    /// u=1 基准值，乘 u（=8）进关节局部系，烘场景时再乘 fit=2 —— 报告里的成品米 = 这里的数 ×16。
    /// 宽度一律按 <c>look.Bulk</c> 的分数给，所以六件都不超各自那一档体宽。</para>
    ///
    /// <para>四个极值锁死（验收口径）：最高点不超耳尖、最前点不超鼻镜、
    /// 最后点不超臀后端这条 -Z 极值、最宽点不超现有体宽；每一件与相邻图元重叠，不给悬空件检查留新活。</para>
    /// </summary>
    private static void AddBeastAnatomy(QuadrupedRig q, BeastLook look)
    {
        var M = q.J;
        double s = q.U;
        double b = look.Bulk;
        var body = look.MatBody;
        var dark = look.MatDark;
        double[] hd(double[] a) => new[] { a[0] * s, a[1] * s, a[2] * s };

        // ---- 头脸（m_head 下）：颅宽 0.20b / 吻前缘到 0.424 / 鼻镜到 0.449（muzzle=0.9 那一档）----
        // 下颌：从吻底下探一块，压进吻底 0.16 米；前缘仍收在鼻镜之后。
        Tscn.Cbx(M["head"], "jaw", body, hd(new[] { 0.0, -0.16, 0.29 }), hd(new[] { 0.12 * b, 0.13, 0.28 }));
        // 双颊：贴颅两侧的肉块，把「横条」头读成有颊宽的头；顶不过耳尖。
        foreach (var side in new[] { -1.0, 1.0 })
            Tscn.Cbx(M["head"], side < 0 ? "cheekL" : "cheekR", body,
                hd(new[] { side * (0.10 * b - 0.01), -0.06, 0.10 }), hd(new[] { 0.11, 0.16, 0.20 }));
        // 眉骨：眼上一条暗横脊，压在颅顶之内。
        Tscn.Cbx(M["head"], "brow", dark, hd(new[] { 0.0, 0.10, 0.13 }), hd(new[] { 0.17 * b, 0.07, 0.12 }));
        // 一对短獠牙：嘴角外上探，牙根埋进下颌与吻的交界，牙尖不过鼻镜。
        foreach (var side in new[] { -1.0, 1.0 })
            Tscn.Cbx(M["head"], side < 0 ? "fangL" : "fangR", "bone",
                hd(new[] { side * 0.05 * b, -0.125, 0.36 }), hd(new[] { 0.03, 0.16, 0.04 }));

        // ---- 颈（m_neck 下）：喉盒宽 0.18b / 高 0.17 / 长 0.28 ----
        // 颈脊：沿颈顶一道纵向暗脊，打破斜柱的圆柱感；脊顶远在耳尖之下，不抬背线。
        Tscn.Cbx(M["neck"], "crest", dark, hd(new[] { 0.0, 0.07, -0.12 }), hd(new[] { 0.06, 0.14, 0.34 }));
        // 喉垂：喉下挂一片，颈前缘不再是一条直线。
        Tscn.Cbx(M["neck"], "dewlap", body, hd(new[] { 0.0, -0.20, 0.12 }), hd(new[] { 0.13 * b, 0.16, 0.22 }));

        // ---- 肩（m_chest 下）：肩盒半宽 sh = 0.28b+0.01，顶面就是现有背线 ----
        // 肩胛左右：外侧一块肉，收在肩盒半宽之内、顶面以下 —— 加体积，不动背线也不碰鞍面。
        double sh = 0.28 * b + 0.01;
        foreach (var side in new[] { -1.0, 1.0 })
            Tscn.Cbx(M["chest"], side < 0 ? "bladeL" : "bladeR", body,
                hd(new[] { side * 0.86 * sh, -0.10, 0.03 }), hd(new[] { 0.26 * sh, 0.32, 0.44 }));

        // ---- 臀（m_hips 下）：臀盒半宽 hh = 0.28b+0.03，它的后端就是全模 -Z 极值 ----
        // 臀肌左右：把平切的墙臀读成两瓣，x 与 z 都收进臀盒之内。
        double hh = 0.28 * b + 0.03;
        foreach (var side in new[] { -1.0, 1.0 })
            Tscn.Cbx(M["hips"], side < 0 ? "gluteL" : "gluteR", body,
                hd(new[] { side * 0.86 * hh, -0.13, -0.60 }), hd(new[] { 0.26 * hh, 0.40, 0.70 }));

        // ---- 腿（m_leg* 下）：大腿一块肉裹住上段，随腿走步态；蹄仍踩 y=0 ----
        // 段长按 Quadruped.Rig.Leg 的同一式子解：前腿角 (0,0)，后腿角 (16,-26)。
        double frontY = q.BackY + (0.04 - 0.10) * s;
        double hindY = q.BackY + (0.02 - 0.10) * s;
        double hoofY = 0.24 * look.LegLen * s;
        double kHind = Math.Cos(16 * Tscn.D) + Math.Cos(-10 * Tscn.D);
        foreach (var tag in new[] { "FL", "FR", "BL", "BR" })
        {
            bool hind = tag[0] == 'B';
            double seg = ((hind ? hindY : frontY) - hoofY) / (hind ? kHind : 2.0);
            Tscn.Cbx(M["leg" + tag], "thigh" + tag, body,
                new[] { 0.0, -0.34 * seg, 0.2 },
                new[] { 0.26 * seg * 2 + 1.0, 0.62 * seg, 0.30 * seg + 2.2 });
        }

        // ---- 短尾桩（m_tail 下）：关节在臀箱内、尾根埋进臀箱接上，向下后折 ----
        // 尾尖收在臀盒底缘下 0.2 米；柱面最低点探出臀底约 0.7 米（1.4 米档），不超 1.5 米。
        // 后端不过臀盒那条 -Z 极值 —— 全长 89.58 米一格不增。
        double hipsY = q.BackY + 0.02 * s;
        double haunchBottom = hipsY - 0.46 * s;
        double tailL = (hipsY + 0.14 * s - (haunchBottom - 0.2)) / 0.866;
        Tscn.CylEnd(M["tail"], "tailStub", dark, new[] { 0.0, 0.0, -0.05 * s },
            2.0, tailL, Humanoid.O(rx: 210));
    }

    // ============================================================
    //  员工（走动的小人 / 战斗员 / 藤爬者 / 石背兽 —— 2026-10-14 起已拆成六件独立模型，
    //  这一件是其中的工人；四身份合用一个文件是拆分前的旧形态）
    //  人形骨架走公共层 Humanoid.Rig，身高 1.72 米。剪影记号：头巾 + 皮围裙 + 腰后工具卷
    //  + 右手一把矿镐 —— 一眼是「这个镇上的工人」，这条工人底色也是拆分前四种身份的底色。
    // ============================================================
    private static Tscn.EmitResult BuildWorker(string root)
    {
        return Emit(PathDef.Units(root), "Worker", r =>
        {
            var g = Humanoid.Rig(r, h: 1.72, bulk: 0.96, arm: 0.56, lean: 0.02,
                matBody: "tunic", matCloth: "leather", matSkin: "skin");
            var J = g.J;
            var B = g.B;
            double U(double v) => v * g.U;

            // 头巾：球壳压住头顶与后颈；前沿收到 -0.10（face 盒之后），前脸完全露出来（票 04）
            B.Sph(J["head"], "hood", "cloth", new[] { 0.0, 0.16, -0.10 }, new[] { 0.40, 0.34, 0.40 });
            B.Box(J["head"], "band", "leatherD", new[] { 0.0, 0.19, 0.02 }, new[] { 0.42, 0.06, 0.42 });
            AddFace(g);
            // 围裙：胸前一片皮，背带从肩过
            B.Box(J["chest"], "apron", "leather", new[] { 0.0, -0.08, 0.17 }, new[] { 0.34, 0.42, 0.06 });
            B.Box(J["chest"], "strapL", "leatherD", new[] { -0.11, 0.14, 0.10 },
                new[] { 0.06, 0.30, 0.05 }, Humanoid.O(rz: 12));
            B.Box(J["chest"], "strapR", "leatherD", new[] { 0.11, 0.14, 0.10 },
                new[] { 0.06, 0.30, 0.05 }, Humanoid.O(rz: -12));
            // 腰后工具卷 + 挂在腰上的一把短锤
            B.Cyl(J["hips"], "toolroll", "leatherD", new[] { 0.0, 0.14, -0.20 }, 0.16, 0.42,
                Humanoid.O(rx: 90));
            B.Box(J["hips"], "pouch", "leather", new[] { -0.20, 0.02, 0.02 }, new[] { 0.12, 0.16, 0.14 });
            // 矿镐拄在右手边（票 04）：柄从掌心竖直拄到脚边（掌离地 0.69 设计米 → 柄底 -0.69），
            // 镐头横在柄顶、略高于掌心，两头铁尖顺柄下翻、尖朝下。柄穿过掌盒 —— 不是悬空件。
            var pick = B.Grip(J["handR"], "Pick", null);
            B.CylEnd(pick, "haft", "woodD", new[] { 0.0, -0.69, 0.0 }, 0.05, 0.79);
            B.Box(pick, "head", "iron", new[] { 0.0, 0.10, 0.0 }, new[] { 0.50, 0.10, 0.10 });
            B.ConeEnd(pick, "tipL", "steel", new[] { -0.25, 0.10, 0.0 }, 0.08, 0.14,
                Humanoid.O(rx: 180));
            B.ConeEnd(pick, "tipR", "steel", new[] { 0.25, 0.10, 0.0 }, 0.08, 0.14,
                Humanoid.O(rx: 180));

            Humanoid.Anchor(r, g.Top + U(0.20), U(1.02));
        });
    }

    // ============================================================
    //  地面人形（2026-10-14 MVP 五件 + T12 钻地怪一件）
    //  六件共用同一副人形骨架命名（Humanoid.Rig）与同一座人形动作库（clips_humanoid.gd）；
    //  T12 起分两套 clip：居民四件八条（idle / walk / run / attack_melee / hit / death / work / carry），
    //  原生怪两件七条（idle / walk / run / attack_melee / hit / death / emerge）。
    //  身份差异做进模型本身：比例、材质、随身那一套装备。
    // ============================================================

    /// <summary>
    /// 脸部助手（票 04）：两只深色眼盒 + 一条鼻梁，挂在 <c>J["head"]</c> 下。
    ///
    /// <para>尺寸全从 <c>g.Hs</c> 派生、经画笔乘 <c>g.U</c>，换身高换头大系数都不用改这里。
    /// 三只图元都压进骨架自带的 <c>face</c> 盒（z 跨它的前后缘、xy 落在它的面内）——
    /// 与相邻图元重叠，悬空件检查没有新活；探出 face 前缘 0.015～0.02 设计米，十视图读得出眼与鼻。</para>
    /// </summary>
    private static void AddFace(HumanoidRig g)
    {
        double hs = g.Hs;
        var B = g.B;
        var head = g.J["head"];

        // 眼：face 上半部左右各一块深色盒（stoneD，与骨架 skin/hide 档反差最大的一档深色）
        foreach (var side in new[] { -1.0, 1.0 })
            B.Box(head, side < 0 ? "eyeL" : "eyeR", "stoneD",
                new[] { side * hs * 0.18, hs * 0.50, hs * 0.46 },
                new[] { hs * 0.16, hs * 0.10, 0.08 });
        // 鼻梁：两眼之下的一条竖盒，鼻尖略收于眼线之后
        B.Box(head, "nose", "stoneD",
            new[] { 0.0, hs * 0.30, hs * 0.44 },
            new[] { hs * 0.08, hs * 0.20, 0.07 });
    }

    //  角斗士（身高 1.82 米）：铜盔 + 顶冠 + 铜护肩 + 右手短剑 + 左手圆盾。
    private static Tscn.EmitResult BuildGladiator(string root)
    {
        return Emit(PathDef.Units(root), "Gladiator", r =>
        {
            var g = Humanoid.Rig(r, h: 1.82, bulk: 1.14, arm: 0.58, lean: 0.02,
                matBody: "skin", matCloth: "leather", matSkin: "skin");
            var J = g.J;
            var B = g.B;

            B.Sph(J["head"], "helm", "bronze", new[] { 0.0, 0.16, -0.02 }, new[] { 0.34, 0.34, 0.36 });
            B.Box(J["head"], "crest", "roofRed", new[] { 0.0, 0.32, -0.06 }, new[] { 0.06, 0.12, 0.28 });
            // 铜盔眼缝（票 04）：两道暗色窄盒压在盔面前缘、探出球面 0.02 设计米 ——
            // 盔罩住整张脸，脸的记号就落在盔壳上，不走脸部助手。
            foreach (var side in new[] { -1.0, 1.0 })
                B.Box(J["head"], side < 0 ? "eyeSlitL" : "eyeSlitR", "leatherD",
                    new[] { side * 0.07, 0.13, 0.135 }, new[] { 0.09, 0.03, 0.06 });
            foreach (var j in new[] { J["shoulderL"], J["shoulderR"] })
                B.Sph(j, "pauldron", "bronze", new[] { 0.0, 0.04, 0.0 }, new[] { 0.28, 0.24, 0.28 });
            B.Box(J["hips"], "skirt", "leatherD", new[] { 0.0, 0.00, 0.0 }, new[] { 0.48, 0.20, 0.34 });

            // 短剑与圆盾都从握点派生，握把/盾面压在掌心上 —— 不压上去就是悬空件
            var sword = B.Grip(J["handR"], "Blade", new[] { 34.0, 0.0, -6.0 });
            B.CylEnd(sword, "grip", "leatherD", new[] { 0.0, -0.08, 0.0 }, 0.06, 0.26);
            B.Box(sword, "guard", "bronze", new[] { 0.0, 0.07, 0.0 }, new[] { 0.26, 0.06, 0.08 });
            // 剑身加厚（票 04）：0.10×0.68×0.04 → 0.12×0.72×0.06，十视图里读得出「一片铁」；
            // 剑尖锥底 0.72 仍埋进新剑身上缘 0.78 之内，十字护手原样保留。
            B.Box(sword, "blade", "steel", new[] { 0.0, 0.42, 0.0 }, new[] { 0.12, 0.72, 0.06 });
            B.Cone(sword, "tip", "steel", new[] { 0.0, 0.72, 0.0 }, 0.10, 0.16);
            // 圆盾法线朝前外侧（左前方，票 04）：握点绕 Y 轴 -40°，
            // 正面与右前视图里盾面转向镜头一侧，不再被躯干挡成一条边。
            var shield = B.Grip(J["handL"], "Shield", new[] { 12.0, -40.0, 0.0 });
            B.Disc(shield, "shieldFace", "bronze", new[] { 0.0, -0.06, 0.06 }, 0.42, 0.06);
            B.Sph(shield, "boss", "bronze", new[] { 0.0, -0.06, 0.10 }, new[] { 0.14, 0.14, 0.10 });

            Humanoid.Anchor(r, g.Top + g.U * 0.20, g.U * 1.02);
        });
    }

    //  镇民（身高 1.70 米）：长外套 + 宽檐帽 + 斜挎的包 + 一根手杖。
    private static Tscn.EmitResult BuildCitizen(string root)
    {
        return Emit(PathDef.Units(root), "Citizen", r =>
        {
            var g = Humanoid.Rig(r, h: 1.70, bulk: 0.92, arm: 0.54, lean: 0.03,
                matBody: "clothBlue", matCloth: "cloth", matSkin: "skin");
            var J = g.J;
            var B = g.B;

            B.Box(J["hips"], "coat", "clothBlue", new[] { 0.0, -0.08, 0.0 }, new[] { 0.38, 0.44, 0.28 });
            B.Box(J["chest"], "collar", "cloth", new[] { 0.0, 0.20, 0.02 }, new[] { 0.30, 0.10, 0.24 });
            // 宽檐帽加大档（票 04）：檐 0.50 → 0.64、顶 0.30 → 0.34 —— 十视图里「宽檐」要一眼读得出
            B.Cone(J["head"], "hatBrim", "clothRed", new[] { 0.0, 0.26, 0.0 }, 0.64, 0.10);
            B.Cone(J["head"], "hatTop", "clothRed", new[] { 0.0, 0.34, 0.0 }, 0.34, 0.14);
            AddFace(g);
            B.Box(J["hips"], "satchel", "leather", new[] { -0.20, 0.04, -0.08 }, new[] { 0.14, 0.20, 0.14 });
            B.Box(J["chest"], "strap", "leatherD", new[] { 0.0, 0.08, 0.12 }, new[] { 0.30, 0.06, 0.06 });
            // 手杖加长档（票 04）：0.90 → 1.10，从掌心里往上探到肩线，读得出「拄着一根杖」
            var staff = B.Grip(J["handR"], "Staff", new[] { -8.0, 0.0, 0.0 });
            B.CylEnd(staff, "shaft", "woodD", new[] { 0.0, -0.50, 0.0 }, 0.05, 1.10);

            Humanoid.Anchor(r, g.Top + g.U * 0.20, g.U * 0.96);
        });
    }

    //  帮手（身高 1.66 米）：兜帽 + 皮围裙 + 背后一只菜筐 + 左手一盏提灯。
    private static Tscn.EmitResult BuildHelper(string root)
    {
        return Emit(PathDef.Units(root), "Helper", r =>
        {
            var g = Humanoid.Rig(r, h: 1.66, bulk: 0.90, arm: 0.52,
                matBody: "canvas", matCloth: "leather", matSkin: "skin");
            var J = g.J;
            var B = g.B;

            // 兜帽往后收（票 04）：球壳中心 z -0.04 → -0.10，前沿收到 face 盒之后，脸整个露出来；
            // 帽沿上移到额前（y 0.24），只做收口的边，不再横盖在脸上。
            B.Sph(J["head"], "hood", "cloth", new[] { 0.0, 0.14, -0.10 }, new[] { 0.36, 0.34, 0.38 });
            B.Box(J["head"], "hoodRim", "cloth", new[] { 0.0, 0.24, 0.10 }, new[] { 0.30, 0.08, 0.10 });
            AddFace(g);
            B.Box(J["chest"], "apron", "leather", new[] { 0.0, 0.02, 0.12 }, new[] { 0.32, 0.36, 0.06 });
            // 背筐加大档（票 04）：0.34×0.26×0.20 → 0.40×0.30×0.24；z 仍压在臀箱后缘之内（重叠不减）
            B.Box(J["hips"], "basket", "woodL", new[] { 0.0, 0.14, -0.20 }, new[] { 0.40, 0.30, 0.24 });
            B.Sph(J["hips"], "produce", "crop", new[] { 0.0, 0.30, -0.20 }, new[] { 0.30, 0.14, 0.18 });

            var lamp = B.Grip(J["handL"], "Lamp", new[] { 0.0, 0.0, 0.0 });
            B.CylEnd(lamp, "hook", "iron", new[] { 0.0, -0.06, 0.0 }, 0.04, 0.24);
            // 提灯加大档（票 04）：0.16×0.20×0.16 → 0.20×0.26×0.20，灯体下缘仍搭在大腿侧
            B.Box(lamp, "lantern", "glowWarm", new[] { 0.0, -0.30, 0.0 }, new[] { 0.20, 0.26, 0.20 });

            Humanoid.Anchor(r, g.Top + g.U * 0.20, g.U * 0.92);
        });
    }

    //  怪物（身高 2.10 米，比标准人粗一圈、驼一点）：骨角 + 颈鬃 + 肩上两块肉瘤 + 右手一根大棒。
    private static Tscn.EmitResult BuildMonster(string root)
    {
        return Emit(PathDef.Units(root), "Monster", r =>
        {
            var g = Humanoid.Rig(r, h: 2.10, bulk: 1.38, headK: 0.86, arm: 0.62, lean: 0.06,
                matBody: "hide", matCloth: "hideD", matSkin: "hide");
            var J = g.J;
            var B = g.B;

            B.ConeEnd(J["head"], "hornL", "bone", new[] { -0.10, 0.14, -0.04 }, 0.10, 0.36, Humanoid.O(rz: 52));
            B.ConeEnd(J["head"], "hornR", "bone", new[] { 0.10, 0.14, -0.04 }, 0.10, 0.36, Humanoid.O(rz: -52));
            B.ConeEnd(J["neck"], "mane", "hideD", new[] { 0.0, 0.06, -0.06 }, 0.26, 0.34, Humanoid.O(rx: 24));
            AddFace(g);
            foreach (var j in new[] { J["shoulderL"], J["shoulderR"] })
                B.Sph(j, "hump", "hide", new[] { 0.0, 0.06, -0.02 }, new[] { 0.34, 0.30, 0.34 });
            // 大棒垂下（票 04）：棒头朝下拎在手里，不再朝天举 —— 深色的棒头与两只向上的骨角
            // 凑不成「三尖」了。掌离地 0.63 设计米，棒头下探 0.60 正好停在地面之上。
            var club = B.Grip(J["handR"], "Club", new[] { -12.0, 0.0, 0.0 });
            B.CylEnd(club, "haft", "woodD", new[] { 0.0, -0.30, 0.0 }, 0.07, 0.44);
            B.Sph(club, "knob", "stoneD", new[] { 0.0, -0.36, 0.0 }, new[] { 0.26, 0.30, 0.26 });
            B.ConeEnd(club, "spike", "iron", new[] { 0.0, -0.46, 0.0 }, 0.10, 0.14, Humanoid.O(rx: 180));

            Humanoid.Anchor(r, g.Top + g.U * 0.20, g.U * 1.10);
        });
    }

    // ============================================================
    //  ---- T12 第二种原生怪（对接面清单「原生怪七条」G-14）----
    //  钻地怪（MonsterBurrower）：身高 1.55 米、横向体量 1.52 —— 比 Monster（2.10 / 1.38）
    //  矮一头、宽一圈，剪影靠三样与 Monster 区分：
    //    · 背上一整片灰岩色的低平背甲（Monster 是肩头两块肉瘤 + 头顶骨角）；
    //    · 头小而扁、只有鼻吻与一块头顶甲板（Monster 的头是一对冲出天际的骨角）；
    //    · 两只前掌各一片骨白铲刃 + 一根钢尖（Monster 只右手一根大棒）。
    //  骨架与动作组与 Monster 完全共用：同一副 Humanoid.Rig 人形骨架、
    //  clips_humanoid.gd 的七条（idle / walk / run / attack_melee / hit / death / emerge）。
    //  材质全部从 Gen.cs 的 Mat 表里选名（岩石深档 stoneD + 兽皮档 hide / hideD + 骨角 bone
    //  + 钢材 steel + 泥地 dirt），不新造档。
    // ============================================================
    private static Tscn.EmitResult BuildMonsterBurrower(string root)
    {
        return Emit(PathDef.Units(root), "MonsterBurrower", r =>
        {
            var g = Humanoid.Rig(r, h: 1.55, bulk: 1.52, headK: 0.72, arm: 0.52, lean: 0.10,
                matBody: "dirt", matCloth: "hideD", matSkin: "hide");
            var J = g.J;
            var B = g.B;

            // 背甲三块：低平的灰岩壳从肩盖到腰 —— 俯看是一整块「石头板」，这是这件的主剪影。
            B.Sph(J["chest"], "shell", "stoneD", new[] { 0.0, 0.22, -0.04 }, new[] { 0.62, 0.44, 0.55 });
            B.Box(J["chest"], "ridge", "stoneD", new[] { 0.0, 0.36, -0.02 }, new[] { 0.10, 0.12, 0.44 });
            B.Box(J["spine"], "midPlate", "stoneD", new[] { 0.0, 0.24, -0.03 }, new[] { 0.52, 0.12, 0.36 });
            B.Box(J["hips"], "rumpPlate", "stoneD", new[] { 0.0, 0.16, -0.04 }, new[] { 0.60, 0.14, 0.40 });

            // 头小而扁：头顶一块甲板 + 一截前伸的鼻吻（与 Monster 的冲天骨角完全区分）
            B.Sph(J["head"], "headPlate", "stoneD", new[] { 0.0, 0.20, -0.02 }, new[] { 0.36, 0.24, 0.36 });
            B.Box(J["head"], "snout", "hide", new[] { 0.0, 0.13, 0.16 }, new[] { 0.24, 0.12, 0.16 });
            // 脸部助手照走（票 04）：眼与鼻压进 face 盒，甲板与鼻吻的前缘都在它之外 —— 脸不被盖住
            AddFace(g);

            // 前小臂两块护甲：扒土的胳膊粗过腿 —— 「这双手是干活的家伙」读在这里
            foreach (var side in new[] { "L", "R" })
                B.Box(J["arm" + side], "guard" + side, "stoneD", new[] { 0.0, -0.30, 0.0 },
                    new[] { 0.18, 0.22, 0.18 });

            // 两掌各一把铲爪（票 04）：宽骨白刃片顺手腕前倾（握点 -20°），刃根从 z 0.19 收到 0.15 ——
            // 根部直接压进掌心里（掌盒 ±0.05），离线校验器不再有「刃根差一点挨上手」的余量账；
            // 钢尖仍朝前，刃片前缘探到 0.30、尖到 0.48，比改前的 0.52 还短一档，占地只减不增。
            foreach (var side in new[] { "L", "R" })
            {
                var claw = B.Grip(J["hand" + side], "Claw" + side, new[] { -20.0, 0.0, side == "L" ? 6.0 : -6.0 });
                B.Box(claw, "blade" + side, "bone", new[] { 0.0, -0.02, 0.15 }, new[] { 0.36, 0.06, 0.30 });
                B.ConeEnd(claw, "spike" + side, "steel", new[] { 0.0, -0.02, 0.26 }, 0.12, 0.22,
                    Humanoid.O(rx: 90));
            }

            Humanoid.Anchor(r, g.Top + g.U * 0.20, g.U * 1.10);
        });
    }
}
