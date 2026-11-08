#nullable enable
// 巨兽竞技场 造型源场景生成器 —— 建筑状态域（T11 / ticket 12，只追加）。
//
// 本文件是 Gen 的部分实现（partial），管四摊：
//   · 十三件建筑各一份受损态 <Name>Damaged —— 缺损从剪影读出（断脊、墙面缺口、焦黑、残骸），
//     不靠换色；占地与档位沿用各自完工件那一档（4×4 / 3×3 / 2×2，上限见 Gen.Town.cs 文件头）。
//   · 三套脚手架 Scaffold{Small,Medium,Large} 与三套废墟 Ruin{Small,Medium,Large}——
//     按占地三档给（Small=2×2 ≤ 1.8 / Medium=3×3 ≤ 2.8 / Large=4×4 ≤ 3.8 米，设计档，×2 后进成品米）。
//     建造中与拆除后由表现层按档选用（本票只出模型，接线不归这里）。
//   · 四件搬运小件 OreChunk / SpecialMaterial / SparePart / Crystal——人尺度，不放大（Fits 无条目 = 1.0，
//     与 Cargo / Corpse 同口径）；尸体与通用货物复用 Corpse / Cargo，不重做。
//   · 门锚点助手 DoorAnchor()：十三件完工建筑（Gen.Town.cs）与各受损态统一用它放 Marker3D "Door"。
//
// 导出器（src/GugaFramework/Tools/glb/export_glb.gd）只把名为 Anchor 的 Marker3D 带进 .glb，
// Door 留在源场景 .tscn 里作真值 —— 「模型 → 门锚点 → 蓝图入口」对照表随票提交，
// 蓝图入口唯一出处是 DeckBlueprints.DoorCellOf（南侧居中一格，本文件不复制它的数字）。

using System;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    // ---------------- 门锚点助手 ----------------

    /// <summary>
    /// 门锚点 Marker3D "Door"：原点落在模型**可见门**（敞口建筑取门口那格台缘）的门前落位点上，
    /// 单位米，与图元走同一条 fit 换算（「世界坐标一把尺子」）。
    /// 位置一律抄各 Build 里 door 板的前脸坐标，不另拍脑袋 —— 门与锚点漂开就是两份真值。
    /// </summary>
    private static void DoorAnchor(string parent, double x, double y, double z) =>
        Tscn.Node("Door", parent, "Marker3D",
            new System.Collections.Generic.List<(string, string)>
            {
                ("transform", Tscn.Tf(Tscn.I3, Tscn.Scaled(new[] { x, y, z }))),
            });

    // ---------------- 受损态共用的形状助手 ----------------

    /// <summary>
    /// 塌顶双坡：北坡整片还斜挂在断脊上，南坡只剩根部一截 + 塌到地的两块焦板。
    /// 与 Gable 同口径只绕 X 转（离线校验器按旋转盒子算包围盒，见 Gen.cs 的 Gable 注记）。
    /// </summary>
    private static void BrokenGable(string p, string mat, double w, double d, double y, double rise,
        double thick = 0.06)
    {
        var half = d / 2.0;
        var len = Math.Sqrt(half * half + rise * rise);
        var pitch = Math.Atan2(rise, half) / Tscn.D;
        // 北坡整片：仍搭到断脊那一头
        Tscn.Cbx(p, "roofSlab", mat, new[] { 0.0, y + rise / 2.0, -half / 2.0 },
            new[] { w, thick, len }, Humanoid.O(rx: -pitch));
        // 南坡残根：短一截、压黑（过火口从这一头烧进去的读法）；位置改贴北坡上缘，保证连着
        Tscn.Cbx(p, "roofStub", "char", new[] { 0.0, y + rise * 0.55, -half * 0.04 },
            new[] { w * 0.70, thick, len * 0.30 }, Humanoid.O(rx: pitch));
        // 塌下去的两块：一块还带顶面色压在墙脚，一块烧透了压焦黑
        Tscn.Cbx(p, "roofFallA", mat, new[] { -w * 0.24, y * 0.30, half * 0.52 },
            new[] { w * 0.34, thick, len * 0.50 }, Humanoid.O(rx: 24, rz: -10));
        Tscn.Cbx(p, "roofFallB", "char", new[] { w * 0.26, y * 0.22, half * 0.66 },
            new[] { w * 0.30, thick * 1.4, len * 0.42 }, Humanoid.O(rx: 18, rz: 14));
        // 断脊：两截，第二截歪着挂在北坡上缘
        Tscn.Cbx(p, "ridgeA", "woodD", new[] { -w * 0.26, y + rise + thick / 2.0, 0.0 },
            new[] { w * 0.48, thick * 1.2, thick * 1.6 });
        Tscn.Cbx(p, "ridgeB", "char", new[] { w * 0.20, y + rise * 0.78, 0.04 },
            new[] { w * 0.30, thick * 1.2, thick * 1.6 }, Humanoid.O(rz: -20));
    }

    /// <summary>一块过火焦痕：压在墙面（浅嵌 0.02 米，靠 Touch 容差连成一件），从墙上读「烧过」。</summary>
    private static void Scorch(string p, double x, double y, double z, double w, double h, double d) =>
        Tscn.Cbx(p, "scorch", "char", new[] { x, y, z }, new[] { w, h, d });

    // ============================================================
    //  十三件受损态（剪影记号 = 各自完工件那一处独有构件的残骸）
    // ============================================================

    //  镇政府受损（4×4）：钟楼断成半截、旗杆倒压在塌顶上，门还立着。
    private static Tscn.EmitResult BuildTownHallDamaged(string root)
    {
        return Emit(PathDef.Town(root), "TownHallDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "plinth", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 3.60, 0.34, 3.60 });
            // 西墙还到胸高，东南面豁大口（完工件的 hall 一整块 → 这里两块带缺口）
            Tscn.Bx(p, "hallW", "woodL", new[] { -0.70, 0.34, 0.0 }, new[] { 1.40, 0.84, 2.40 });
            Tscn.Bx(p, "hallE", "woodL", new[] { 1.00, 0.34, -0.55 }, new[] { 0.80, 0.60, 1.30 });
            foreach (var s in new (double X, double Z, double H)[] { (-1.30, -1.10, 1.10), (1.30, -1.10, 0.55), (1.30, 1.10, 0.40), (-1.30, 1.10, 0.85) })
                Tscn.Bx(p, "cornerPost", "char", new[] { s.X, 0.34, s.Z }, new[] { 0.18, s.H, 0.18 });
            // 檐口断两截，一截歪挂
            Tscn.Cbx(p, "corniceA", "woodD", new[] { -0.55, 1.22, 0.0 }, new[] { 1.60, 0.12, 2.60 });
            Tscn.Cbx(p, "corniceB", "char", new[] { 0.95, 1.06, -0.40 }, new[] { 0.90, 0.12, 1.40 }, Humanoid.O(rz: -9));
            BrokenGable(p, "roofRed", 3.00, 2.60, 1.18, 0.34);
            // 钟楼断半截，钟面掉在台阶边，塔帽滚到台基外
            Tscn.Bx(p, "towerStump", "stoneL", new[] { -0.60, 1.10, -0.50 }, new[] { 0.66, 0.55, 0.66 });
            Scorch(p, -0.60, 1.28, -0.14, 0.50, 0.30, 0.06);
            Tscn.Cbx(p, "clockFall", "glowWarm", new[] { -0.35, 0.17, 1.42 }, new[] { 0.34, 0.30, 0.12 }, Humanoid.O(rx: 62));
            Tscn.Cn(p, "capFall", "bronze", new[] { -1.30, 0.0, 1.30 }, 0.74, 0.36, Humanoid.O(rx: 78));
            // 门还立着（矮墙那一段就是门墙），台阶完好 —— 入口读法与完工件同一处
            Tscn.Cbx(p, "door", "woodD", new[] { 0.20, 0.72, 1.18 }, new[] { 0.62, 0.72, 0.12 });
            Tscn.Bx(p, "step", "stoneM", new[] { 0.20, 0.00, 1.30 }, new[] { 0.90, 0.20, 0.30 });
            // 旗杆倒在塌顶上、旗撕半面：镇公所唯一的「官署」剪影记号残着才认得出是它
            Tscn.Cbx(p, "flagPoleFall", "char", new[] { 0.35, 1.16, 0.60 }, new[] { 1.70, 0.08, 0.08 }, Humanoid.O(rz: 80));
            Tscn.Cbx(p, "flagTorn", "clothBlue", new[] { -0.42, 1.02, 0.62 }, new[] { 0.30, 0.34, 0.05 }, Humanoid.O(rz: 26));
            // 残骸两堆 + 门前焦痕
            Tscn.Cbx(p, "rubbleA", "stoneD", new[] { 1.05, 0.34, 0.90 }, new[] { 0.40, 0.22, 0.40 }, Humanoid.O(ry: 18));
            Tscn.Cbx(p, "rubbleB", "char", new[] { 0.85, 0.44, 0.75 }, new[] { 0.26, 0.18, 0.26 }, Humanoid.O(ry: -24));
            Scorch(p, 0.20, 0.55, 1.22, 0.70, 0.50, 0.05);
            DoorAnchor(p, 0.20, 0.20, 1.30);
            Humanoid.Anchor(p, 2.10, 1.90);
        }, TownFit);
    }

    //  竞技场受损（4×4）：四面墙塌出大口、垛口掉一地，火盆掀翻、火没了 —— 场上唯一暖光熄灭。
    private static Tscn.EmitResult BuildArenaDamaged(string root)
    {
        return Emit(PathDef.Town(root), "ArenaDamaged", r =>
        {
            var p = r;
            const double Half = 1.55;
            Tscn.Bx(p, "pad", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 3.60, 0.30, 3.60 });
            Tscn.Bx(p, "floor", "sand", new[] { 0.0, 0.30, 0.0 }, new[] { 3.10, 0.14, 3.10 });
            // 北墙塌两段中间豁口；东西墙只剩半高残片；正面门墙还在（入口读法不变）
            Tscn.Bx(p, "wallNw", "stoneL", new[] { -0.95, 0.30, -Half }, new[] { 1.30, 0.46, 0.22 });
            Tscn.Bx(p, "wallNe", "stoneL", new[] { 1.10, 0.30, -Half }, new[] { 0.90, 0.60, 0.22 });
            Tscn.Bx(p, "wallW", "stoneL", new[] { -Half, 0.30, -0.45 }, new[] { 0.22, 0.42, 1.30 });
            Tscn.Bx(p, "wallE", "stoneL", new[] { Half, 0.30, 0.35 }, new[] { 0.22, 0.55, 1.60 });
            Tscn.Bx(p, "wallSw", "stoneL", new[] { -1.10, 0.30, Half }, new[] { 1.10, 0.90, 0.22 });
            Tscn.Bx(p, "wallSe", "stoneL", new[] { 1.10, 0.30, Half }, new[] { 1.10, 0.90, 0.22 });
            // 塌下来的墙皮压在场上（连着地面读得出是同一堵墙倒的）
            Tscn.Cbx(p, "fellA", "stoneM", new[] { 0.10, 0.44, -0.95 }, new[] { 0.90, 0.20, 0.50 }, Humanoid.O(ry: 14));
            Tscn.Cbx(p, "fellB", "stoneL", new[] { -0.70, 0.46, -0.60 }, new[] { 0.40, 0.22, 0.60 }, Humanoid.O(ry: -30));
            // 垛口掉三块在门两侧
            foreach (var s in new (double X, double Z, double R)[] { (-0.55, 1.30, 12), (0.62, 1.28, -20), (0.10, 1.05, 34) })
                Tscn.Cbx(p, "merlonFall", "stoneM", new[] { s.X, 0.44, s.Z }, new[] { 0.30, 0.18, 0.30 }, Humanoid.O(ry: s.R));
            // 看台只残一圈北阶；西侧塌成两段
            Tscn.Bx(p, "tierN", "stoneM", new[] { 0.0, 0.44, -0.80 }, new[] { 2.20, 0.28, 0.40 });
            Tscn.Bx(p, "tierWa", "stoneM", new[] { -0.80, 0.44, -0.30 }, new[] { 0.40, 0.28, 0.70 });
            // 火盆掀翻：座还立、铜盆歪倒、火没了（地标失守的读法）
            Tscn.Cy(p, "pedestal", "stoneD", new[] { 0.0, 0.44, 0.0 }, 0.50, 0.30);
            Tscn.Cn(p, "bowlFall", "gold", new[] { 0.34, 0.46, 0.28 }, 0.62, 0.26, Humanoid.O(rx: 96));
            // 四角火柱：两根断桩还立在残墙头上、两根倒在场心
            Tscn.Cy(p, "torchStumpA", "char", new[] { -Half, 0.76, -Half }, 0.10, 0.26);
            Tscn.Cy(p, "torchStumpB", "char", new[] { Half, 0.90, -Half }, 0.10, 0.18);
            Tscn.Cbx(p, "torchFallA", "char", new[] { -1.05, 0.48, -0.20 }, new[] { 0.60, 0.10, 0.10 }, Humanoid.O(ry: 24));
            Tscn.Cbx(p, "torchFallB", "woodD", new[] { 1.10, 0.48, 0.10 }, new[] { 0.55, 0.10, 0.10 }, Humanoid.O(ry: -36));
            Scorch(p, 0.0, 0.52, 0.0, 0.90, 0.04, 0.90);
            DoorAnchor(p, 0.0, 0.30, 1.60);
            Humanoid.Anchor(p, 1.55, 1.80);
        }, TownFit);
    }

    //  演武场受损（3×3）：兵器架砸地、草人掉头、沙带还认得出是块练功场。
    private static Tscn.EmitResult BuildTrainingGroundDamaged(string root)
    {
        return Emit(PathDef.Town(root), "TrainingGroundDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "yard", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.10, 2.60 });
            // 练功架：一柱断桩、一柱倒平，横梁斜搭在断桩上砸进沙里
            Tscn.Bx(p, "postA", "char", new[] { -0.50, 0.10, 0.0 }, new[] { 0.14, 0.62, 0.14 });
            Tscn.Cbx(p, "postBFall", "woodD", new[] { 0.62, 0.17, 0.0 }, new[] { 0.14, 1.20, 0.14 }, Humanoid.O(rz: 84));
            Tscn.Cbx(p, "beamFall", "woodL", new[] { 0.05, 0.55, 0.0 }, new[] { 1.20, 0.14, 0.14 }, Humanoid.O(rz: -26));
            // 草人：身子倒了、头滚到一边、横臂断在架上
            Tscn.Cy(p, "dummyPost", "char", new[] { 0.70, 0.10, -0.55 }, 0.12, 0.34);
            Tscn.Sp(p, "dummyBodyFall", "thatch", new[] { 0.30, 0.33, -0.62 }, new[] { 0.46, 0.42, 0.46 }, Humanoid.O(rz: 30));
            Tscn.Sp(p, "dummyHeadFall", "thatch", new[] { 0.95, 0.24, -0.85 }, new[] { 0.28, 0.28, 0.28 });
            Tscn.Cbx(p, "dummyArmFall", "woodD", new[] { -0.90, 0.17, -0.30 }, new[] { 0.90, 0.10, 0.10 }, Humanoid.O(ry: 40));
            // 兵器架塌了：架子断桩 + 三支长杆滚一地
            Tscn.Bx(p, "rackPost", "char", new[] { -1.00, 0.10, 0.70 }, new[] { 0.12, 0.28, 0.12 });
            Tscn.Cbx(p, "rackBarFall", "woodD", new[] { -0.70, 0.14, 0.72 }, new[] { 0.50, 0.08, 0.08 }, Humanoid.O(ry: 26));
            for (var i = 0; i < 3; i++)
                Tscn.Cbx(p, "spearFall", "woodL", new[] { -0.30 + i * 0.30, 0.15, 0.98 - i * 0.12 },
                    new[] { 1.00, 0.05, 0.05 }, Humanoid.O(ry: 16 + i * 20));
            // 沙带留一条还整 —— 场子的底没烧穿
            Tscn.Bx(p, "sandBand", "sand", new[] { -0.90, 0.10, 0.90 }, new[] { 0.30, 0.04, 0.90 });
            Scorch(p, 0.55, 0.11, -0.30, 0.70, 0.02, 0.50);
            DoorAnchor(p, 0.0, 0.15, 1.30);
            Humanoid.Anchor(p, 1.30, 1.50);
        }, TownFit);
    }

    //  马厩受损（3×3）：茅顶塌半面、隔栏歪倒、料槽翻了饲料撒出。
    private static Tscn.EmitResult BuildStableDamaged(string root)
    {
        return Emit(PathDef.Town(root), "StableDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "floor", "deckWood", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.12, 2.60 });
            // 北墙烧出一个大缺口（两段残墙中间空着），东西墙塌到半高
            Tscn.Bx(p, "wallNw", "woodL", new[] { -0.80, 0.12, -1.15 }, new[] { 1.00, 0.90, 0.14 });
            Tscn.Bx(p, "wallNe", "woodL", new[] { 0.95, 0.12, -1.15 }, new[] { 0.70, 0.52, 0.14 });
            Tscn.Bx(p, "wallW", "woodL", new[] { -1.15, 0.12, -0.35 }, new[] { 0.14, 0.62, 1.40 });
            Tscn.Bx(p, "wallE", "char", new[] { 1.15, 0.12, 0.55 }, new[] { 0.14, 0.40, 1.00 });
            Scorch(p, -0.80, 0.50, -1.06, 0.80, 0.55, 0.06);
            // 隔栏从中间折断歪倒
            Tscn.Cbx(p, "dividerA", "woodD", new[] { 0.0, 0.12, -0.55 }, new[] { 0.12, 0.60, 1.00 });
            Tscn.Cbx(p, "dividerB", "char", new[] { 0.18, 0.30, 0.60 }, new[] { 0.12, 0.55, 1.00 }, Humanoid.O(rz: 34));
            // 两个料槽：一个还立（饲料剩半槽），一个翻倒撒出来
            Tscn.Bx(p, "trough", "woodD", new[] { -0.62, 0.12, 0.70 }, new[] { 0.34, 0.20, 0.60 });
            Tscn.Bx(p, "troughFeed", "crop", new[] { -0.62, 0.26, 0.70 }, new[] { 0.28, 0.08, 0.52 });
            Tscn.Cbx(p, "troughFall", "woodD", new[] { 0.72, 0.20, 0.55 }, new[] { 0.34, 0.20, 0.60 }, Humanoid.O(rz: 88));
            Tscn.Bx(p, "spill", "crop", new[] { 0.72, 0.12, 0.95 }, new[] { 0.40, 0.06, 0.34 });
            // 草垛滚出两个、一个压塌顶的残板上
            Tscn.Sp(p, "hayFallA", "thatch", new[] { 0.35, 0.24, -0.85 }, new[] { 0.46, 0.42, 0.46 }, Humanoid.O(ry: 24));
            Tscn.Sp(p, "hayFallB", "thatch", new[] { -0.30, 0.22, 1.05 }, new[] { 0.42, 0.38, 0.42 });
            BrokenGable(p, "thatch", 2.50, 2.50, 0.66, 0.26);
            DoorAnchor(p, 0.0, 0.15, 1.30);
            Humanoid.Anchor(p, 1.45, 1.50);
        }, TownFit);
    }

    //  射箭场受损（3×3）：三面草靶全躺倒、弓架塌地，射击线还画着。
    private static Tscn.EmitResult BuildArcheryRangeDamaged(string root)
    {
        return Emit(PathDef.Town(root), "ArcheryRangeDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "ground", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.10, 2.60 });
            Tscn.Bx(p, "shootLine", "sand", new[] { 0.0, 0.10, 0.90 }, new[] { 2.40, 0.05, 0.24 });
            // 三个靶：一个断桩还立、两个连桩带草靶躺地
            Tscn.Cy(p, "targetStump", "char", new[] { -0.80, 0.10, -0.70 }, 0.10, 0.36);
            Tscn.Cbx(p, "postFallA", "woodD", new[] { -0.10, 0.12, -0.86 }, new[] { 0.80, 0.12, 0.12 }, Humanoid.O(ry: 24));
            Tscn.Cn(p, "strawFallA", "thatch", new[] { 0.42, 0.22, -0.90 }, 0.44, 0.16, Humanoid.O(rx: 88));
            Tscn.Cbx(p, "postFallB", "woodD", new[] { 0.75, 0.20, -0.40 }, new[] { 0.80, 0.12, 0.12 }, Humanoid.O(ry: -32));
            Tscn.Cn(p, "strawFallB", "thatch", new[] { 0.95, 0.30, -0.30 }, 0.44, 0.16, Humanoid.O(rx: 82, rz: 18));
            Tscn.Cn(p, "bullseyeFall", "roofRed", new[] { -0.30, 0.22, -0.55 }, 0.26, 0.10, Humanoid.O(rx: 90));
            // 弓架塌了：断桩 + 挂弓的横杆砸地 + 两把弓躺平一把歪着
            Tscn.Bx(p, "rackPost", "char", new[] { 1.10, 0.10, 0.50 }, new[] { 0.12, 0.26, 0.12 });
            Tscn.Cbx(p, "rackBarFall", "woodD", new[] { 0.78, 0.15, 0.52 }, new[] { 0.50, 0.08, 0.08 }, Humanoid.O(ry: 20));
            Tscn.Bx(p, "bowFallA", "woodL", new[] { 0.30, 0.095, 0.40 }, new[] { 0.14, 0.08, 0.50 }, Humanoid.O(ry: 30));
            Tscn.Bx(p, "bowFallB", "woodL", new[] { -0.20, 0.095, 0.30 }, new[] { 0.14, 0.08, 0.50 }, Humanoid.O(ry: -18));
            Tscn.Cbx(p, "bowLean", "woodL", new[] { 1.16, 0.35, 0.10 }, new[] { 0.12, 0.50, 0.06 }, Humanoid.O(rx: -18));
            Tscn.Cy(p, "quiverFall", "leatherD", new[] { 0.60, 0.10, 0.90 }, 0.18, 0.30, Humanoid.O(rx: 86));
            Scorch(p, 0.10, 0.11, -0.20, 0.90, 0.02, 0.60);
            DoorAnchor(p, 0.0, 0.15, 1.30);
            Humanoid.Anchor(p, 1.20, 1.40);
        }, TownFit);
    }

    //  工坊受损（3×3）：烟囱断半截烟停了、炉口只剩一点暗火、铁砧掀翻。
    private static Tscn.EmitResult BuildWorkshopDamaged(string root)
    {
        return Emit(PathDef.Town(root), "WorkshopDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.24, 2.40 });
            //  hall 塌出东北缺口；窗不亮了（去掉 glowWarm 就是灭）
            Tscn.Bx(p, "hallW", "woodL", new[] { -0.50, 0.24, 0.0 }, new[] { 1.50, 0.72, 1.90 });
            Tscn.Bx(p, "hallSe", "woodL", new[] { 0.60, 0.24, 0.55 }, new[] { 0.70, 0.50, 0.80 });
            Tscn.Bx(p, "sill", "char", new[] { 0.0, 0.24, 0.0 }, new[] { 2.24, 0.10, 2.04 });
            Tscn.Bx(p, "postA", "char", new[] { -0.96, 0.34, 0.86 }, new[] { 0.14, 0.42, 0.14 });
            Tscn.Bx(p, "postB", "woodD", new[] { 0.96, 0.34, 0.86 }, new[] { 0.14, 0.60, 0.14 });
            BrokenGable(p, "thatch", 2.30, 2.10, 0.96, 0.28);
            // 烟囱断半截：压顶滚到台基上，烟（bone）整团没了 —— 停产的读法
            Tscn.Bx(p, "stackStump", "stoneD", new[] { 0.70, 0.24, -0.60 }, new[] { 0.30, 0.70, 0.30 });
            Tscn.Cbx(p, "capFall", "stoneM", new[] { 1.05, 0.29, -0.20 }, new[] { 0.42, 0.10, 0.42 }, Humanoid.O(ry: 22));
            // 炉口：火缩成一丁点暗火，楣梁歪挂
            Tscn.Cbx(p, "ember", "glowFire", new[] { -0.58, 0.42, 0.90 }, new[] { 0.22, 0.16, 0.08 });
            Tscn.Cbx(p, "forgeLintel", "iron", new[] { -0.56, 0.74, 0.92 }, new[] { 0.56, 0.08, 0.14 }, Humanoid.O(rz: -8));
            Tscn.Cbx(p, "door", "woodD", new[] { 0.34, 0.62, 0.92 }, new[] { 0.44, 0.60, 0.10 });
            // 铁砧掀翻：桩断、砧身歪压在残板上、砧角掉一边
            Tscn.Cy(p, "stump", "char", new[] { -0.86, 0.00, 0.92 }, 0.28, 0.12);
            Tscn.Cbx(p, "anvilFall", "iron", new[] { -0.80, 0.24, 1.05 }, new[] { 0.44, 0.18, 0.20 }, Humanoid.O(rz: 28, ry: -20));
            Tscn.Cn(p, "anvilHornFall", "iron", new[] { -0.40, 0.10, 1.15 }, 0.12, 0.20, Humanoid.O(rx: 84));
            // 柴垛散架：三根原木滚开
            Tscn.Cbx(p, "logA", "woodD", new[] { -1.00, 0.12, -0.40 }, new[] { 0.24, 0.24, 1.10 }, Humanoid.O(ry: 18));
            Tscn.Cbx(p, "logB", "char", new[] { -0.70, 0.12, 0.10 }, new[] { 0.22, 0.22, 0.95 }, Humanoid.O(ry: -40));
            Tscn.Cbx(p, "logC", "woodD", new[] { 0.90, 0.12, -1.05 }, new[] { 0.22, 0.22, 0.90 }, Humanoid.O(ry: 60));
            Scorch(p, -0.58, 0.55, 0.94, 0.60, 0.50, 0.04);
            DoorAnchor(p, 0.34, 0.22, 1.00);
            Humanoid.Anchor(p, 1.70, 1.60);
        }, TownFit);
    }

    //  空艇坞受损（3×3）：吊杆折断倒扣在台上、艇壳裂成两半、信号球滚落。
    private static Tscn.EmitResult BuildAirshipDockDamaged(string root)
    {
        return Emit(PathDef.Town(root), "AirshipDockDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "deck", "deckWood", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.20, 2.60 });
            // 台板断得只剩三块（完工件五块），断口读得出砸穿过
            for (var i = 0; i < 3; i++)
                Tscn.Bx(p, "plank", i == 1 ? "char" : "woodL", new[] { -1.00 + i * 0.50, 0.20, 0.0 }, new[] { 0.42, 0.04, 2.50 });
            // 系缆桩两断两倒
            Tscn.Cy(p, "bollardStumpA", "char", new[] { -1.05, 0.20, -1.05 }, 0.16, 0.14);
            Tscn.Cy(p, "bollardStumpB", "woodD", new[] { 1.05, 0.20, -1.05 }, 0.16, 0.18);
            Tscn.Cbx(p, "bollardFallA", "woodD", new[] { -0.75, 0.28, 1.02 }, new[] { 0.40, 0.16, 0.16 }, Humanoid.O(ry: 30));
            Tscn.Cbx(p, "bollardFallB", "char", new[] { 0.70, 0.28, -0.60 }, new[] { 0.36, 0.16, 0.16 }, Humanoid.O(ry: -24));
            // 桅杆齐腰折断：下半歪挂着、上半横压在断桩上
            Tscn.Cy(p, "mastStump", "char", new[] { 0.60, 0.20, -0.60 }, 0.14, 0.70);
            Tscn.Cbx(p, "mastTopFall", "woodD", new[] { -0.10, 0.27, -0.35 }, new[] { 1.40, 0.14, 0.14 }, Humanoid.O(ry: 16));
            Tscn.Cbx(p, "sparFall", "woodD", new[] { 0.20, 0.48, -0.55 }, new[] { 1.10, 0.10, 0.10 }, Humanoid.O(ry: -28));
            Tscn.Sp(p, "signalFall", "glowWarm", new[] { -0.65, 0.32, -0.80 }, new[] { 0.22, 0.22, 0.22 });
            // 艇壳裂两半错开摆着：坞台的存在记号
            Tscn.Sp(p, "hullHalfA", "woodL", new[] { 0.15, 0.40, 0.10 }, new[] { 0.72, 0.40, 0.80 }, Humanoid.O(ry: 8));
            Tscn.Sp(p, "hullHalfB", "char", new[] { 0.35, 0.36, 0.95 }, new[] { 0.66, 0.36, 0.72 }, Humanoid.O(ry: -20));
            Tscn.Cbx(p, "keelFall", "woodD", new[] { -0.35, 0.26, 0.75 }, new[] { 0.16, 0.14, 1.00 }, Humanoid.O(ry: 34));
            DoorAnchor(p, 0.0, 0.30, 1.30);
            Humanoid.Anchor(p, 1.30, 1.60);
        }, TownFit);
    }

    //  旅店受损（3×3）：二层塌去半边、阳台板斜挑出去、招牌砸在门前。
    private static Tscn.EmitResult BuildHotelDamaged(string root)
    {
        return Emit(PathDef.Town(root), "HotelDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.20, 2.40 });
            Tscn.Bx(p, "ground", "woodL", new[] { 0.0, 0.20, 0.0 }, new[] { 2.10, 0.70, 1.90 });
            // 二层只剩西半，东半塌出一层顶（断茬读得出塌向）
            Tscn.Bx(p, "upperW", "woodL", new[] { -0.45, 0.85, 0.0 }, new[] { 1.00, 0.55, 1.70 });
            Tscn.Cbx(p, "upperFallA", "char", new[] { 0.72, 0.95, 0.30 }, new[] { 0.80, 0.16, 0.90 }, Humanoid.O(rz: -16));
            Tscn.Cbx(p, "upperFallB", "woodD", new[] { 0.85, 1.02, -0.50 }, new[] { 0.50, 0.40, 0.40 }, Humanoid.O(ry: 20));
            // 阳台：东半还连墙、西半齐根折断斜挑出去（断端拄在 base 前缘）
            Tscn.Cbx(p, "balconyA", "woodD", new[] { 0.55, 0.90, 1.05 }, new[] { 1.10, 0.10, 0.30 });
            Tscn.Cbx(p, "balconyFall", "char", new[] { -0.80, 0.32, 1.18 }, new[] { 1.00, 0.10, 0.30 }, Humanoid.O(rx: -34));
            Tscn.Bx(p, "railPost", "woodD", new[] { 1.00, 0.96, 1.14 }, new[] { 0.08, 0.30, 0.08 });
            Tscn.Cbx(p, "railBarFall", "woodD", new[] { -0.20, 0.24, 1.30 }, new[] { 1.80, 0.08, 0.08 }, Humanoid.O(ry: 10));
            BrokenGable(p, "roofRed", 2.20, 2.20, 1.28, 0.30);
            // 烟囱断半截，压顶砸在塌下来的阳台板上
            Tscn.Bx(p, "chimney", "stoneM", new[] { 0.70, 0.20, -0.55 }, new[] { 0.34, 0.85, 0.34 });
            Tscn.Cbx(p, "chimneyCapFall", "stoneD", new[] { -0.70, 0.26, 1.10 }, new[] { 0.44, 0.10, 0.44 }, Humanoid.O(ry: 24));
            // 招牌砸在门前（绿板还认得出是那块牌），托臂断挂在墙上
            Tscn.Cbx(p, "signArm", "char", new[] { -0.80, 1.24, 0.80 }, new[] { 0.08, 0.08, 0.30 });
            Tscn.Cbx(p, "signFall", "clothGreen", new[] { -0.75, 0.20, 1.25 }, new[] { 0.56, 0.36, 0.06 }, Humanoid.O(rx: 76));
            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 0.55, 0.93 }, new[] { 0.44, 0.66, 0.12 });
            Scorch(p, -0.45, 1.10, 0.86, 0.70, 0.45, 0.05);
            DoorAnchor(p, 0.0, 0.20, 1.00);
            Humanoid.Anchor(p, 1.85, 1.60);
        }, TownFit);
    }

    //  酒馆受损（3×3）：篷架砸穿草顶、灯滚出门外、两张桌翻在沙土上。
    private static Tscn.EmitResult BuildRestaurantDamaged(string root)
    {
        return Emit(PathDef.Town(root), "RestaurantDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.16, 2.40 });
            // 屋身塌东半（完工件一整块 → 残西半 + 断茬）
            Tscn.Bx(p, "hutW", "woodL", new[] { -0.45, 0.16, 0.0 }, new[] { 1.10, 0.66, 1.60 });
            Tscn.Cbx(p, "hutFall", "char", new[] { 0.62, 0.26, -0.20 }, new[] { 0.70, 0.18, 1.10 }, Humanoid.O(rz: -14));
            BrokenGable(p, "roofGreen", 1.80, 1.70, 0.82, 0.24);
            Tscn.Bx(p, "chimney", "stoneM", new[] { 0.55, 0.16, -0.45 }, new[] { 0.28, 0.62, 0.28 });
            // 帆布篷砸地（撑杆倒两根、篷布塌成一斜片），灯滚到门外
            Tscn.Cbx(p, "awningFall", "canvas", new[] { 0.10, 0.30, 0.98 }, new[] { 1.50, 0.06, 0.50 }, Humanoid.O(rx: -22));
            Tscn.Cbx(p, "awningPostFallA", "char", new[] { -0.80, 0.18, 1.02 }, new[] { 0.66, 0.10, 0.10 }, Humanoid.O(ry: 88));
            Tscn.Cbx(p, "awningPostFallB", "woodD", new[] { 0.82, 0.41, 1.10 }, new[] { 0.10, 0.50, 0.10 }, Humanoid.O(rz: 24));
            Tscn.Sp(p, "lanternFall", "glowWarm", new[] { -0.20, 0.11, 1.30 }, new[] { 0.18, 0.18, 0.18 });
            // 两张桌：一条腿的桌还立（桌面裂一角），一张翻扣
            Tscn.Cy(p, "tableLeg", "woodD", new[] { -0.85, 0.16, -0.75 }, 0.10, 0.30);
            Tscn.Cn(p, "tableTop", "woodL", new[] { -0.85, 0.44, -0.75 }, 0.44, 0.08);
            Tscn.Cbx(p, "tableFall", "woodL", new[] { 0.85, 0.28, -0.70 }, new[] { 0.44, 0.08, 0.44 }, Humanoid.O(rx: 96));
            Tscn.Cy(p, "stoolFall", "char", new[] { 0.30, 0.12, 0.55 }, 0.20, 0.22, Humanoid.O(rx: 88));
            Tscn.Cbx(p, "door", "woodD", new[] { -0.40, 0.48, 0.78 }, new[] { 0.40, 0.52, 0.10 });
            Scorch(p, -0.45, 0.45, 0.82, 0.55, 0.42, 0.04);
            DoorAnchor(p, -0.40, 0.18, 0.85);
            Humanoid.Anchor(p, 1.40, 1.50);
        }, TownFit);
    }

    //  粮仓受损（2×2）：仓身拦腰断、草帽顶滚在地上、箍圈掉一圈。
    private static Tscn.EmitResult BuildGranaryDamaged(string root)
    {
        return Emit(PathDef.Town(root), "GranaryDamaged", r =>
        {
            var p = r;
            Tscn.Cy(p, "footing", "stoneM", new[] { 0.0, 0.0, 0.0 }, 1.50, 0.16);
            // 下段仓身还正、上段错位歪扣（断茬对不齐就是「折断过」的读法）
            Tscn.Cy(p, "siloLow", "woodL", new[] { 0.0, 0.16, 0.0 }, 1.10, 0.52);
            Tscn.Cy(p, "siloTop", "char", new[] { 0.22, 0.62, -0.16 }, 1.00, 0.40, Humanoid.O(rx: 0, rz: 12));
            Tscn.Cy(p, "hoopLo", "iron", new[] { 0.0, 0.30, 0.0 }, 1.16, 0.08);
            // 草帽顶滚到门内一侧、高箍砸地上平躺成一圈压向仓脚（整件仍收在 2×2 那一档 1.8 米内）
            Tscn.Cn(p, "capFall", "thatch", new[] { 0.10, 0.00, 0.70 }, 1.30, 0.38, Humanoid.O(rx: 104));
            Tscn.Ring(p, "hoopFall", "iron", new[] { 0.32, 0.05, 0.50 }, 0.90, 0.08);
            // 天窗盖歪挂、门还立
            Tscn.Cbx(p, "hatchFall", "char", new[] { 0.05, 0.55, 0.55 }, new[] { 0.28, 0.14, 0.16 }, Humanoid.O(rx: 40));
            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 0.32, 0.56 }, new[] { 0.30, 0.36, 0.10 });
            // 梯子断一根、粮袋散一只
            Tscn.Bx(p, "ladderRail", "woodD", new[] { -0.26, 0.16, 0.52 }, new[] { 0.06, 0.42, 0.06 });
            Tscn.Bx(p, "rung", "woodL", new[] { -0.16, 0.20, 0.52 }, new[] { 0.28, 0.05, 0.05 });
            Tscn.Sp(p, "sackA", "crop", new[] { 0.46, 0.24, 0.44 }, new[] { 0.28, 0.26, 0.28 });
            Tscn.Sp(p, "sackFall", "crop", new[] { -0.70, 0.14, 0.20 }, new[] { 0.30, 0.22, 0.30 }, Humanoid.O(rz: 60));
            // 撒出的粮压一条（断了口才能撒出来）
            Tscn.Bx(p, "spill", "crop", new[] { 0.46, 0.05, 0.78 }, new[] { 0.30, 0.06, 0.50 });
            Scorch(p, 0.0, 0.40, -0.56, 0.60, 0.30, 0.05);
            DoorAnchor(p, 0.0, 0.18, 0.60);
            Humanoid.Anchor(p, 1.45, 0.90);
        }, TownFit);
    }

    //  宿舍受损（2×2）：屋脊塌半面、烟囱齐根断、一排铺位只剩半间。
    private static Tscn.EmitResult BuildDormitoryDamaged(string root)
    {
        return Emit(PathDef.Town(root), "DormitoryDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 1.60, 0.12, 1.40 });
            // 铺位塌两间留一间：东段整残、西段只剩半高断墙
            Tscn.Bx(p, "hutW", "woodL", new[] { -0.48, 0.12, 0.0 }, new[] { 0.56, 0.42, 1.20 });
            Tscn.Bx(p, "hutE", "woodL", new[] { 0.42, 0.12, -0.20 }, new[] { 0.56, 0.60, 0.80 });
            Tscn.Cbx(p, "hutFall", "char", new[] { 0.30, 0.22, 0.55 }, new[] { 0.70, 0.16, 0.60 }, Humanoid.O(rz: -12));
            BrokenGable(p, "thatch", 1.55, 1.40, 0.54, 0.20);
            // 烟囱从屋脊上齐根断掉：断桩歪着、半截砖砸在阶前
            Tscn.Bx(p, "chimneyStump", "stoneM", new[] { 0.48, 0.42, -0.36 }, new[] { 0.24, 0.30, 0.24 });
            Tscn.Bx(p, "chimneyFall", "stoneD", new[] { -0.50, 0.16, 0.45 }, new[] { 0.24, 0.24, 0.60 }, Humanoid.O(ry: 20));
            Tscn.Cbx(p, "door", "woodD", new[] { -0.34, 0.36, 0.58 }, new[] { 0.32, 0.36, 0.10 });
            // 长凳翻扣在阶前：住人的屋子塌了就剩这条凳子还整
            Tscn.Cbx(p, "benchFall", "woodD", new[] { 0.30, 0.10, 0.66 }, new[] { 0.70, 0.10, 0.16 }, Humanoid.O(ry: 26));
            Scorch(p, -0.34, 0.40, 0.62, 0.40, 0.36, 0.04);
            DoorAnchor(p, -0.34, 0.18, 0.65);
            Humanoid.Anchor(p, 1.30, 0.90);
        }, TownTallFit);
    }

    //  教堂受损（2×2）：尖顶塌了、十字架倒在阶前、中殿屋顶塌出一排断椽。
    private static Tscn.EmitResult BuildChurchDamaged(string root)
    {
        return Emit(PathDef.Town(root), "ChurchDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 1.50, 0.12, 1.50 });
            // 中殿塌顶后露椽：三根断椽斜插（屋顶整片没了才读得出塌顶）
            Tscn.Bx(p, "nave", "stoneL", new[] { 0.0, 0.12, 0.10 }, new[] { 0.90, 0.42, 1.20 });
            foreach (var s in new[] { -0.24, 0.0, 0.24 })
                Tscn.Cbx(p, "rib", "char", new[] { s, 0.66, 0.10 }, new[] { 0.07, 0.10, 1.10 }, Humanoid.O(rx: 14));
            // 塔断半截，钟楼层塌成歪板
            Tscn.Bx(p, "towerStump", "stoneM", new[] { 0.0, 0.12, -0.42 }, new[] { 0.40, 0.72, 0.40 });
            Tscn.Cbx(p, "belfryFall", "char", new[] { 0.28, 0.94, -0.40 }, new[] { 0.40, 0.10, 0.40 }, Humanoid.O(rz: -14));
            // 尖顶砸在阶前地上（蓝灰顶色 + 十字）：教堂的剪影记号残在地面才认得出它是教堂
            Tscn.Cn(p, "spireFall", "roofBlue", new[] { -0.55, 0.00, 0.80 }, 0.44, 0.34, Humanoid.O(rx: 96));
            Tscn.Cbx(p, "crossStemFall", "woodD", new[] { -0.35, 0.10, 0.70 }, new[] { 0.40, 0.05, 0.05 }, Humanoid.O(ry: 20));
            Tscn.Cbx(p, "crossBarFall", "woodD", new[] { -0.30, 0.17, 0.72 }, new[] { 0.05, 0.05, 0.20 }, Humanoid.O(ry: 20));
            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 0.34, 0.68 }, new[] { 0.32, 0.40, 0.10 });
            // rose 窗灭成黑洞（glowWarm 换 stoneD 内凹）
            Tscn.Cbx(p, "roseDark", "stoneD", new[] { 0.0, 0.50, -0.24 }, new[] { 0.20, 0.20, 0.06 });
            Scorch(p, 0.48, 0.34, 0.20, 0.10, 0.35, 0.50);
            DoorAnchor(p, 0.0, 0.18, 0.75);
            Humanoid.Anchor(p, 1.35, 0.90);
        }, TownFit);
    }

    //  传送阵受损（2×2）：三根立石倒了两根、符纹断成两截、残晶还发青。
    private static Tscn.EmitResult BuildTeleportArrayDamaged(string root)
    {
        return Emit(PathDef.Town(root), "TeleportArrayDamaged", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 1.50, 0.12, 1.50 });
            // 符纹四段烧断两段（台面上一黑一亮的对比就是「断供」）
            Tscn.Bx(p, "runeN", "glowWarm", new[] { 0.0, 0.12, -0.42 }, new[] { 0.90, 0.04, 0.12 });
            Tscn.Bx(p, "runeE", "glowWarm", new[] { 0.42, 0.12, 0.0 }, new[] { 0.12, 0.04, 0.90 });
            Tscn.Bx(p, "runeSChar", "char", new[] { 0.10, 0.12, 0.42 }, new[] { 0.55, 0.04, 0.12 });
            Tscn.Bx(p, "runeWChar", "char", new[] { -0.42, 0.12, 0.10 }, new[] { 0.12, 0.04, 0.55 });
            // 立石一根歪着还立（顶晶转青：魔晶残光），两根倒台
            Tscn.Bx(p, "stoneLean", "stoneD", new[] { 0.46, 0.12, 0.46 }, new[] { 0.20, 0.60, 0.20 }, Humanoid.O(rz: -10));
            Tscn.Sp(p, "crystalLean", "glowCyan", new[] { 0.50, 0.70, 0.46 }, new[] { 0.24, 0.24, 0.24 }, Humanoid.O(rz: -10));
            Tscn.Bx(p, "stoneFallA", "char", new[] { -0.60, 0.10, 0.40 }, new[] { 0.60, 0.18, 0.18 }, Humanoid.O(ry: 24));
            Tscn.Bx(p, "stoneFallB", "stoneD", new[] { 0.10, 0.10, -0.70 }, new[] { 0.55, 0.18, 0.18 }, Humanoid.O(ry: -30));
            // 碎晶三点：青色只在这一处（画面基准「青色只用于少量魔法发光」）
            Tscn.Sp(p, "shardA", "glowCyan", new[] { -0.40, 0.17, 0.30 }, new[] { 0.14, 0.14, 0.14 });
            Tscn.Sp(p, "shardB", "glowCyan", new[] { -0.55, 0.16, 0.10 }, new[] { 0.10, 0.10, 0.10 });
            Tscn.Sp(p, "shardC", "glowCyan", new[] { 0.30, 0.16, -0.60 }, new[] { 0.12, 0.12, 0.12 });
            Scorch(p, 0.0, 0.15, 0.0, 0.70, 0.03, 0.70);
            DoorAnchor(p, 0.0, 0.16, 0.75);
            Humanoid.Anchor(p, 1.20, 0.90);
        }, TownTallFit);
    }

    // ============================================================
    //  三套脚手架（建造中）：同一种架子按占地三档放样，档位差 = 层数与边长
    // ============================================================

    /// <summary>一层围栏四根横杆（贴着四根立柱外缘围一圈）。</summary>
    private static void ScaffoldRail(string p, double y, double h)
    {
        foreach (var sz in new[] { -h, h })
            Tscn.Cbx(p, "rail", "woodD", new[] { 0.0, y, sz }, new[] { 2.0 * h, 0.08, 0.08 });
        foreach (var sx in new[] { -h, h })
            Tscn.Cbx(p, "rail", "woodD", new[] { sx, y, 0.0 }, new[] { 0.08, 0.08, 2.0 * h });
    }

    /// <summary>四角立柱 + 每层围栏 + 顶层踏板 + 南面斜靠的梯 + 台下一只料筐一只桶。</summary>
    private static Tscn.EmitResult BuildScaffold(string root, string name, double side, double postH,
        double[] levelYs, double anchorY, double barW)
    {
        return Emit(PathDef.Town(root), name, r =>
        {
            var p = r;
            var h = side / 2.0 - 0.08;
            foreach (var sx in new[] { -h, h })
                foreach (var sz in new[] { -h, h })
                    Tscn.Bx(p, "post", "woodD", new[] { sx, 0.0, sz }, new[] { 0.14, postH, 0.14 });
            foreach (var y in levelYs)
                ScaffoldRail(p, y, h);
            // 顶层踏板两幅，错开一块留上下人的口
            var top = levelYs[levelYs.Length - 1];
            Tscn.Bx(p, "deckA", "woodL", new[] { -side / 4.0, top + 0.06, 0.0 }, new[] { side / 2.0 - 0.10, 0.06, side - 0.20 });
            Tscn.Bx(p, "deckB", "woodL", new[] { side / 3.0, top + 0.06, -side / 4.0 }, new[] { side / 3.0, 0.06, side / 2.0 - 0.10 });
            // 斜梯：南面内侧架到顶层（近乎立着，脚落地、背搭在南侧立柱上，不外挑出台阶档）
            Tscn.Cbx(p, "ladder", "woodL", new[] { side * 0.22, top / 2.0, h - 0.12 },
                new[] { 0.46, 0.06, postH + 0.35 }, Humanoid.O(rx: -82));
            // 交叉斜撑两面（读得出是工地不是一堆木头）
            Tscn.Cbx(p, "braceS", "woodD", new[] { 0.0, postH * 0.42, h + 0.03 },
                new[] { side * 0.78, 0.07, 0.07 }, Humanoid.O(rz: 26));
            Tscn.Cbx(p, "braceW", "woodD", new[] { -h - 0.03, postH * 0.42, 0.0 },
                new[] { 0.07, 0.07, side * 0.78 }, Humanoid.O(rx: -24));
            // 架内的料：一只筐 + 一只桶（在建 = 架上正堆着料，全部收在占地档内）
            Tscn.Bx(p, "crate", "woodL", new[] { -h + 0.28, 0.0, h - 0.30 }, new[] { 0.34, 0.30, 0.34 });
            Tscn.Cy(p, "barrel", "woodD", new[] { h - 0.24, 0.0, h - 0.24 }, 0.30, 0.36);
            Humanoid.Anchor(p, anchorY, barW);
        }, TownFit);
    }

    //  ============================================================
    //  三套废墟（拆毁后）：台基 + 残墙墩 + 塌顶残板 + 瓦砾，同形状按三档放大
    //  ============================================================

    private static Tscn.EmitResult BuildRuin(string root, string name, double side, double stubH,
        double anchorY, double barW)
    {
        return Emit(PathDef.Town(root), name, r =>
        {
            var p = r;
            var h = side / 2.0;
            // 台基砸缺一个角（整块台基 + 一块缺口位的塌茬）
            Tscn.Bx(p, "plinth", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { side, 0.14, side });
            Tscn.Bx(p, "plinthChip", "stoneD", new[] { h * 0.55, 0.0, h * 0.55 }, new[] { h * 0.70, 0.08, h * 0.70 });
            // 三面残墙墩（高度一档一档往下塌）+ 一面只剩根部
            Tscn.Bx(p, "stubN", "stoneL", new[] { -h * 0.35, 0.14, -h + 0.10 }, new[] { side * 0.42, stubH, 0.16 });
            Tscn.Bx(p, "stubW", "brick", new[] { -h + 0.10, 0.14, h * 0.10 }, new[] { 0.16, stubH * 0.72, side * 0.45 });
            Tscn.Bx(p, "stubE", "stoneL", new[] { h - 0.12, 0.14, -h * 0.30 }, new[] { 0.16, stubH * 0.50, side * 0.30 });
            Scorch(p, -h * 0.35, 0.14 + stubH * 0.30, -h + 0.20, side * 0.30, stubH * 0.5, 0.03);
            // 塌顶：一片斜压在北墩根与台基上（断脊那根的读法），一根断梁横在台上
            Tscn.Cbx(p, "roofSlabFall", "char", new[] { 0.0, 0.20, -h + 0.55 },
                new[] { side * 0.50, 0.07, side * 0.30 }, Humanoid.O(rx: -14));
            Tscn.Cbx(p, "beamFall", "woodD", new[] { h * 0.15, 0.17, h * 0.30 },
                new[] { side * 0.60, 0.12, 0.12 }, Humanoid.O(ry: 24));
            // 瓦砾四处：大小三档，全部压在台基上
            foreach (var s in new (double X, double Z, double D, double R)[]
            {
                (-h * 0.55, h * 0.45, 0.24, 15), (h * 0.40, -h * 0.45, 0.18, -30),
                (h * 0.20, h * 0.55, 0.20, 40), (-h * 0.50, -h * 0.40, 0.15, -12),
            })
                Tscn.Cbx(p, "rubble", "stoneM", new[] { s.X, 0.115, s.Z }, new[] { s.D, s.D * 0.6, s.D }, Humanoid.O(ry: s.R));
            // 灰烬堆：塌过火那一档的读法（废墟不一定烧过，但这三套按「拆毁+过火」统一读）
            Tscn.Sp(p, "ashA", "ash", new[] { h * 0.35, 0.19, h * 0.05 }, new[] { 0.30, 0.14, 0.30 });
            Tscn.Sp(p, "ashB", "char", new[] { h * 0.50, 0.17, h * 0.18 }, new[] { 0.22, 0.10, 0.22 });
            Humanoid.Anchor(p, anchorY, barW);
        }, TownFit);
    }

    // ============================================================
    //  四件搬运小件（人尺度，不放大；尸体与通用货物复用 Corpse / Cargo）
    // ============================================================

    //  矿石：一块带棱面与浅色矿脉的岩体 + 两块碎屑（2026-10-28 审查修复：原来读成近黑方块——
    //  碎屑石改亮档 stoneL 拼出棱面、矿脉用骨色亮条压在岩面；暗部抬到可辨，仍不是发光的）
    private static Tscn.EmitResult BuildOreChunk(string root)
    {
        return Emit(PathDef.Props(root), "OreChunk", r =>
        {
            var p = r;
            Tscn.Cbx(p, "rock", "stoneM", new[] { 0.0, 0.16, 0.0 }, new[] { 0.42, 0.30, 0.36 }, Humanoid.O(ry: 16));
            Tscn.Cbx(p, "facet", "stoneL", new[] { -0.05, 0.34, 0.03 }, new[] { 0.22, 0.20, 0.18 }, Humanoid.O(rz: 20));
            Tscn.Cbx(p, "facet2", "stoneL", new[] { 0.15, 0.13, -0.12 }, new[] { 0.20, 0.14, 0.16 }, Humanoid.O(ry: -28));
            Tscn.Cbx(p, "vein", "bone", new[] { 0.02, 0.22, 0.15 }, new[] { 0.30, 0.05, 0.06 }, Humanoid.O(ry: 16, rz: 9));
            Tscn.Cbx(p, "vein2", "bone", new[] { -0.12, 0.14, -0.05 }, new[] { 0.05, 0.04, 0.22 }, Humanoid.O(ry: 16));
            Tscn.Cbx(p, "fleck", "iron", new[] { 0.13, 0.24, 0.09 }, new[] { 0.10, 0.08, 0.08 }, Humanoid.O(ry: -20));
            Tscn.Cbx(p, "chip", "stoneM", new[] { 0.26, 0.05, -0.16 }, new[] { 0.14, 0.09, 0.12 }, Humanoid.O(ry: 32));
            Humanoid.Anchor(p, 0.50, 0.45);
        });
    }

    //  特殊材料：一匹布垫上两锭旧铜，皮条捆一道（进阶用的稀货，读得出「成材」不是碎石）
    private static Tscn.EmitResult BuildSpecialMaterial(string root)
    {
        return Emit(PathDef.Props(root), "SpecialMaterial", r =>
        {
            var p = r;
            Tscn.Bx(p, "pad", "clothBlue", new[] { 0.0, 0.0, 0.0 }, new[] { 0.50, 0.06, 0.40 });
            Tscn.Bx(p, "ingotA", "bronze", new[] { 0.0, 0.06, 0.03 }, new[] { 0.34, 0.10, 0.20 });
            Tscn.Bx(p, "ingotB", "bronze", new[] { 0.02, 0.14, -0.02 }, new[] { 0.30, 0.10, 0.18 }, Humanoid.O(ry: 12));
            Tscn.Cbx(p, "strap", "leatherD", new[] { 0.20, 0.11, 0.00 }, new[] { 0.05, 0.22, 0.24 });
            Humanoid.Anchor(p, 0.50, 0.50);
        });
    }

    //  备件：一枚平放的钢齿轮 + 半根销轴（工坊出的修理件，读得出是机器件不是料）
    private static Tscn.EmitResult BuildSparePart(string root)
    {
        return Emit(PathDef.Props(root), "SparePart", r =>
        {
            var p = r;
            Tscn.Plate(p, "gear", "steel", new[] { 0.0, 0.10, 0.0 }, 0.34, 0.10);
            Tscn.Cy(p, "hub", "iron", new[] { 0.0, 0.05, 0.0 }, 0.12, 0.20);
            // 轮齿：八齿沿圆周每 45° 一枚，长轴沿半径外压过盘缘（盘半径 0.17，齿跨 0.14~0.24）
            foreach (var deg in new[] { 0.0, 45.0, 90.0, 135.0, 180.0, 225.0, 270.0, 315.0 })
            {
                var rad = deg * Math.PI / 180.0;
                Tscn.Cbx(p, "tooth", "iron", new[] { Math.Cos(rad) * 0.19, 0.10, Math.Sin(rad) * 0.19 },
                    new[] { 0.10, 0.08, 0.07 }, Humanoid.O(ry: -deg));
            }
            Tscn.Cbx(p, "pin", "steel", new[] { 0.08, 0.04, -0.22 }, new[] { 0.06, 0.06, 0.26 }, Humanoid.O(ry: 18));
            Humanoid.Anchor(p, 0.45, 0.50);
        });
    }

    //  魔晶：一块石垫上一支主晶两支碎晶（消化产出物，青光是全仓库唯一魔法发光处）
    private static Tscn.EmitResult BuildCrystal(string root)
    {
        return Emit(PathDef.Props(root), "Crystal", r =>
        {
            var p = r;
            Tscn.Bx(p, "plate", "stoneD", new[] { 0.0, 0.0, 0.0 }, new[] { 0.40, 0.06, 0.34 });
            Tscn.Sp(p, "main", "glowCyan", new[] { 0.0, 0.21, 0.0 }, new[] { 0.22, 0.30, 0.22 });
            Tscn.Sp(p, "shardA", "glowCyan", new[] { 0.13, 0.14, 0.06 }, new[] { 0.12, 0.16, 0.12 }, Humanoid.O(rz: 14));
            Tscn.Sp(p, "shardB", "glowCyan", new[] { -0.12, 0.13, -0.05 }, new[] { 0.10, 0.12, 0.10 }, Humanoid.O(rz: -18));
            Humanoid.Anchor(p, 0.55, 0.45);
        });
    }

    // ---------------- 构建入口（由 Gen.Run() 末尾追加调用） ----------------

    /// <summary>T11 全部产物：十三件受损态 + 三套脚手架 + 三套废墟 + 四件搬运小件。清单与顺序 = 导出表尾块顺序。</summary>
    internal static System.Collections.Generic.IEnumerable<Tscn.EmitResult> TownStatesRun(string root)
    {
        yield return BuildTownHallDamaged(root);
        yield return BuildArenaDamaged(root);
        yield return BuildTrainingGroundDamaged(root);
        yield return BuildStableDamaged(root);
        yield return BuildArcheryRangeDamaged(root);
        yield return BuildWorkshopDamaged(root);
        yield return BuildAirshipDockDamaged(root);
        yield return BuildHotelDamaged(root);
        yield return BuildRestaurantDamaged(root);
        yield return BuildGranaryDamaged(root);
        yield return BuildDormitoryDamaged(root);
        yield return BuildChurchDamaged(root);
        yield return BuildTeleportArrayDamaged(root);
        // 脚手架与废墟：Small=2×2 / Medium=3×3 / Large=4×4 三档
        yield return BuildScaffold(root, "ScaffoldSmall", 1.70, 1.50, new[] { 0.70, 1.40 }, 1.90, 1.60);
        yield return BuildScaffold(root, "ScaffoldMedium", 2.70, 2.20, new[] { 0.75, 1.50, 2.10 }, 2.60, 2.40);
        yield return BuildScaffold(root, "ScaffoldLarge", 3.70, 3.00, new[] { 0.90, 1.85, 2.90 }, 3.40, 3.40);
        yield return BuildRuin(root, "RuinSmall", 1.60, 0.55, 1.10, 1.50);
        yield return BuildRuin(root, "RuinMedium", 2.60, 0.75, 1.50, 2.40);
        yield return BuildRuin(root, "RuinLarge", 3.60, 1.00, 2.10, 3.40);
        // 搬运小件：人尺度、不走放大档（与 Cargo / Corpse 同口径）
        yield return BuildOreChunk(root);
        yield return BuildSpecialMaterial(root);
        yield return BuildSparePart(root);
        yield return BuildCrystal(root);
    }
}
