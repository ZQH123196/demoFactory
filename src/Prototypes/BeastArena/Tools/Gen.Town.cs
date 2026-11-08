#nullable enable
// 巨兽竞技场 造型源场景生成器 —— 小镇建筑域（十三件建筑）。
//
// 本文件是 Gen 的部分实现（partial），按域拆分见 Gen.cs 文件头那一段。
// 配色与体量档位查 Gen.cs 的 Mat 与 Fits 两张表，本文件只有「这一栋长什么样」。

using System;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    // ============================================================
    //  MVP 十三件建筑（2026-10-14 新增这一批，竞技场与工坊是缩聚重生成）
    //
    //  底座上限按网格档位给死，三个数出自 spec「二十七个真模型清单」第 1 条：
    //      4×4 ≤ 3.80×3.80 米   3×3 ≤ 2.80×2.80 米   2×2 ≤ 1.80×1.80 米
    //  那一档跟 Tools/Check.cs 里的 48 米**不是同一件事**：48 管的是「这批造型里最大能多大」
    //  （巨兽实测 45 米撑着它），三个档位管的是「这一件放不放得进它那一格」。离线校验器只判前者，
    //  后者靠每个 Build 的底座尺寸自己守住 —— 实际占地在 `check` 输出的「占地 W×D」那一栏。
    // ============================================================

    //  镇公所（4×4，底座 3.60×3.60，最高约 3.32 米）
    //  剪影记号：一根方钟楼从屋顶里长出来 + 门上一面旗。场上唯一的「官署」体量。
    private static Tscn.EmitResult BuildTownHall(string root)
    {
        return Emit(PathDef.Town(root), "TownHall", r =>
        {
            var p = r;
            Tscn.Bx(p, "plinth", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 3.60, 0.34, 3.60 });
            Tscn.Bx(p, "hall", "woodL", new[] { 0.0, 0.34, 0.0 }, new[] { 2.80, 1.40, 2.40 });
            foreach (var sx in new[] { -1.30, 1.30 })
                foreach (var sz in new[] { -1.10, 1.10 })
                    Tscn.Bx(p, "cornerPost", "woodD", new[] { sx, 0.34, sz }, new[] { 0.18, 1.40, 0.18 });
            Tscn.Bx(p, "cornice", "woodD", new[] { 0.0, 1.74, 0.0 }, new[] { 3.00, 0.12, 2.60 });
            Gable(p, "roofRed", 3.10, 2.70, 1.86, 0.46);

            // 钟楼：方塔 + 铜顶 + 钟面
            Tscn.Bx(p, "tower", "stoneL", new[] { -0.60, 1.60, -0.50 }, new[] { 0.66, 1.40, 0.66 });
            Tscn.Cn(p, "towerCap", "bronze", new[] { -0.60, 2.96, -0.50 }, 0.74, 0.36);
            Tscn.Cbx(p, "clock", "glowWarm", new[] { -0.60, 2.62, -0.14 }, new[] { 0.34, 0.34, 0.12 });
            Tscn.Cbx(p, "belfry", "woodD", new[] { -0.60, 2.42, -0.14 }, new[] { 0.44, 0.14, 0.16 });

            // 门 + 台阶 + 窗（窗走助手：深色框 + 内凹发光面）
            Tscn.Cbx(p, "door", "woodD", new[] { 0.20, 0.82, 1.18 }, new[] { 0.62, 0.96, 0.12 });
            Tscn.Bx(p, "step", "stoneM", new[] { 0.20, 0.00, 1.30 }, new[] { 0.90, 0.20, 0.30 });
            foreach (var sx in new[] { -0.90, 0.90 })
                Win(p, sx, 1.00, 1.20, 0.30, 0.34);

            // 门廊：正门两侧两根柱 + 廊顶（台阶已有，就是上面那块 step）
            foreach (var sx in new[] { -0.32, 0.72 })
                Tscn.Cy(p, "porchCol", "stoneL", new[] { sx, 0.00, 1.42 }, 0.22, 1.58);
            Tscn.Cbx(p, "porchRoof", "woodD", new[] { 0.20, 1.62, 1.28 }, new[] { 1.64, 0.12, 0.60 });

            // 旗杆压在屋顶上：杆底 1.86 落在南坡那一片板上
            Tscn.Cy(p, "flagPole", "woodD", new[] { 1.20, 1.86, 0.60 }, 0.08, 0.90);
            Tscn.Cbx(p, "flag", "clothBlue", new[] { 0.90, 2.50, 0.60 }, new[] { 0.62, 0.36, 0.05 });

            // ---- T11 门锚点：与可见门（door + step 那组，前脸 z=1.24）一致 ----
            DoorAnchor(p, 0.20, 0.20, 1.30);
            Humanoid.Anchor(p, 3.70, 1.90);
        });
    }

    //  演武场（3×3，底座 2.60×2.60，最高约 1.38 米）
    //  剪影记号：一副练功架 + 一个草人 + 三支插在架子上的长杆。
    private static Tscn.EmitResult BuildTrainingGround(string root)
    {
        return Emit(PathDef.Town(root), "TrainingGround", r =>
        {
            var p = r;
            Tscn.Bx(p, "yard", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.10, 2.60 });
            Tscn.Bx(p, "postA", "woodD", new[] { -0.50, 0.10, 0.0 }, new[] { 0.14, 1.20, 0.14 });
            Tscn.Bx(p, "postB", "woodD", new[] { 0.50, 0.10, 0.0 }, new[] { 0.14, 1.20, 0.14 });
            Tscn.Cbx(p, "beam", "woodL", new[] { 0.0, 1.18, 0.0 }, new[] { 1.20, 0.14, 0.14 });

            // 草人：立柱 + 草身 + 草头 + 横臂（2026-10-28 审查修复：加大一档，深柱托亮草，十视图读得出人形）
            Tscn.Cy(p, "dummyPost", "woodD", new[] { 0.70, 0.10, -0.55 }, 0.14, 0.88);
            Tscn.Sp(p, "dummyBody", "thatch", new[] { 0.70, 0.98, -0.55 }, new[] { 0.56, 0.60, 0.56 });
            Tscn.Sp(p, "dummyHead", "thatch", new[] { 0.70, 1.36, -0.55 }, new[] { 0.36, 0.36, 0.36 });
            Tscn.Cbx(p, "dummyArm", "woodD", new[] { 0.70, 1.02, -0.55 }, new[] { 1.05, 0.12, 0.12 });

            // 兵器架：立柱 + 横杆 + 三支长杆（杆要压在横杆上，否则判悬空件；同批加大一档）
            Tscn.Bx(p, "rackPost", "woodD", new[] { -1.00, 0.10, 0.70 }, new[] { 0.16, 0.62, 0.16 });
            Tscn.Bx(p, "rackBar", "woodD", new[] { -1.00, 0.56, 0.70 }, new[] { 0.60, 0.10, 0.10 });
            for (var i = 0; i < 3; i++)
                Tscn.Cy(p, "spear", "iron", new[] { -1.14 + i * 0.16, 0.10, 0.70 }, 0.08, 1.12);

            // 两道沙带：读得出这是块练功的场子
            foreach (var sx in new[] { -0.90, 0.90 })
                Tscn.Bx(p, "sandBand", "sand", new[] { sx, 0.10, 0.90 }, new[] { 0.30, 0.04, 0.90 });

            // ---- T11 门锚点：敞开场子，入口取南侧场缘 ----
            DoorAnchor(p, 0.0, 0.15, 1.30);
            Humanoid.Anchor(p, 1.70, 1.50);
        });
    }

    //  马厩（3×3，底座 2.60×2.60，最高约 1.45 米）
    //  剪影记号：三面墙一面敞口 + 两个马位，隔板从当中分开。
    private static Tscn.EmitResult BuildStable(string root)
    {
        return Emit(PathDef.Town(root), "Stable", r =>
        {
            var p = r;
            Tscn.Bx(p, "floor", "deckWood", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.12, 2.60 });
            Tscn.Bx(p, "wallN", "woodL", new[] { 0.0, 0.12, -1.15 }, new[] { 2.60, 0.90, 0.14 });
            Tscn.Bx(p, "wallW", "woodL", new[] { -1.15, 0.12, 0.0 }, new[] { 0.14, 0.90, 2.10 });
            Tscn.Bx(p, "wallE", "woodL", new[] { 1.15, 0.12, 0.0 }, new[] { 0.14, 0.90, 2.10 });
            Tscn.Bx(p, "divider", "woodD", new[] { 0.0, 0.12, 0.0 }, new[] { 0.12, 0.78, 2.10 });
            foreach (var sx in new[] { -0.62, 0.62 })
            {
                Tscn.Bx(p, "trough", "woodD", new[] { sx, 0.12, 0.70 }, new[] { 0.34, 0.20, 0.60 });
                Tscn.Bx(p, "troughFeed", "crop", new[] { sx, 0.30, 0.70 }, new[] { 0.28, 0.06, 0.52 });
                Tscn.Sp(p, "hay", "thatch", new[] { sx, 0.30, -0.60 }, new[] { 0.46, 0.36, 0.46 });
            }
            foreach (var sx in new[] { -0.62, 0.0, 0.62 })
                Win(p, sx, 0.62, -1.22, 0.26, 0.28, dir: -1);
            Gable(p, "thatch", 2.50, 2.50, 1.02, 0.36);

            // 南面敞口的门口：两根门框立柱 + 楣梁（三面墙一面敞口的「门」就在这道口上）
            Door(p, 0.0, 0.12, 1.30, 1.30, 0.80, stepOut: 0.08);

            // ---- T11 门锚点：三面墙一面敞口，入口取南面敞口当中 ----
            DoorAnchor(p, 0.0, 0.15, 1.30);
            Humanoid.Anchor(p, 1.80, 1.50);
        });
    }

    //  射箭场（3×3，底座 2.65×2.60，最高约 1.16 米）
    //  剪影记号：三面草靶一字排开 + 侧边一架挂着三把弓。
    private static Tscn.EmitResult BuildArcheryRange(string root)
    {
        return Emit(PathDef.Town(root), "ArcheryRange", r =>
        {
            var p = r;
            Tscn.Bx(p, "ground", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.10, 2.60 });
            Tscn.Bx(p, "shootLine", "sand", new[] { 0.0, 0.10, 0.90 }, new[] { 2.40, 0.05, 0.24 });
            // 三面草靶（2026-10-28 审查修复：靶面加大一档、红心更亮，十视图读得出是靶）
            foreach (var sx in new[] { -0.80, 0.0, 0.80 })
            {
                Tscn.Cy(p, "targetPost", "woodD", new[] { sx, 0.10, -0.70 }, 0.12, 0.88);
                Tscn.Cn(p, "straw", "thatch", new[] { sx, 0.94, -0.70 }, 0.58, 0.20);
                Tscn.Cn(p, "bullseye", "clothRed", new[] { sx, 1.10, -0.70 }, 0.32, 0.14);
            }
            // 侧边弓架：挂三把弓（加大一档；架往外挪 0.05，弓缘仍收在 3×3 档内）
            Tscn.Bx(p, "rackPost", "woodD", new[] { 1.05, 0.10, 0.50 }, new[] { 0.14, 0.56, 0.14 });
            Tscn.Bx(p, "rackBar", "woodD", new[] { 1.05, 0.54, 0.50 }, new[] { 0.60, 0.10, 0.10 });
            foreach (var dx in new[] { -0.22, 0.0, 0.22 })
                Tscn.Bx(p, "bow", "woodL", new[] { 1.05 + dx, 0.56, 0.50 }, new[] { 0.15, 0.10, 0.50 });
            Tscn.Cy(p, "quiver", "leatherD", new[] { 0.70, 0.10, 0.80 }, 0.22, 0.34);

            // ---- T11 门锚点：敞开场子，入口取南侧射击线那一头的场缘 ----
            DoorAnchor(p, 0.0, 0.15, 1.30);
            Humanoid.Anchor(p, 1.55, 1.40);
        });
    }

    //  飞艇坞（3×3，底座 2.60×2.60，最高约 2.26 米）
    //  剪影记号：四根系缆桩围出一块木台 + 一支挂信号灯的吊杆 + 台上一截小艇壳。
    private static Tscn.EmitResult BuildAirshipDock(string root)
    {
        return Emit(PathDef.Town(root), "AirshipDock", r =>
        {
            var p = r;
            Tscn.Bx(p, "deck", "deckWood", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.20, 2.60 });
            for (var i = 0; i < 5; i++)
                Tscn.Bx(p, "plank", "woodL", new[] { -1.00 + i * 0.50, 0.20, 0.0 }, new[] { 0.42, 0.04, 2.50 });
            foreach (var sx in new[] { -1.05, 1.05 })
                foreach (var sz in new[] { -1.05, 1.05 })
                    Tscn.Cy(p, "bollard", "woodD", new[] { sx, 0.20, sz }, 0.16, 0.40);
            Tscn.Cy(p, "mast", "woodD", new[] { 0.60, 0.20, -0.60 }, 0.14, 1.80);
            Tscn.Cbx(p, "spar", "woodD", new[] { 0.60, 1.90, -0.60 }, new[] { 1.20, 0.10, 0.10 });
            Tscn.Cy(p, "rope", "leatherD", new[] { 0.10, 1.34, -0.60 }, 0.06, 0.60);
            Tscn.Sp(p, "signal", "glowWarm", new[] { 0.60, 2.04, -0.60 }, new[] { 0.22, 0.26, 0.22 });
            // 台上的小艇壳
            Tscn.Sp(p, "hull", "woodL", new[] { 0.10, 0.42, 0.55 }, new[] { 0.80, 0.40, 1.60 });
            Tscn.Cbx(p, "keel", "woodD", new[] { 0.10, 0.20, 0.55 }, new[] { 0.16, 0.20, 1.40 });

            // ---- T11 门锚点：开放木台，登台口取南侧台缘（南北向缆桩之间）----
            DoorAnchor(p, 0.0, 0.30, 1.30);
            Humanoid.Anchor(p, 2.60, 1.60);
        });
    }

    //  旅店（3×3，底座 2.60×2.41，最高约 1.85 米）
    //  剪影记号：二层 + 一圈挑出木栏杆 + 侧面挂一块绿招牌。
    private static Tscn.EmitResult BuildHotel(string root)
    {
        return Emit(PathDef.Town(root), "Hotel", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.20, 2.40 });
            Tscn.Bx(p, "ground", "woodL", new[] { 0.0, 0.20, 0.0 }, new[] { 2.10, 0.70, 1.90 });
            Tscn.Bx(p, "upper", "woodL", new[] { 0.0, 0.85, 0.0 }, new[] { 1.90, 0.55, 1.70 });
            Tscn.Bx(p, "balcony", "woodD", new[] { 0.0, 0.90, 1.05 }, new[] { 2.20, 0.10, 0.30 });
            foreach (var sx in new[] { -1.00, 1.00 })
                Tscn.Bx(p, "railPost", "woodD", new[] { sx, 0.96, 1.14 }, new[] { 0.08, 0.30, 0.08 });
            Tscn.Cbx(p, "railBar", "woodD", new[] { 0.0, 1.20, 1.14 }, new[] { 2.08, 0.08, 0.08 });
            Gable(p, "roofRed", 2.30, 2.30, 1.40, 0.38);
            Tscn.Bx(p, "chimney", "stoneM", new[] { 0.70, 0.20, -0.55 }, new[] { 0.34, 1.30, 0.34 });
            Tscn.Cbx(p, "chimneyCap", "stoneD", new[] { 0.70, 1.45, -0.55 }, new[] { 0.44, 0.10, 0.44 });

            // 招牌：托臂插进二层墙里，挂板压在托臂上
            Tscn.Cbx(p, "signArm", "woodD", new[] { -0.80, 1.28, 0.95 }, new[] { 0.08, 0.08, 0.60 });
            Tscn.Cbx(p, "sign", "clothGreen", new[] { -0.80, 1.10, 1.18 }, new[] { 0.56, 0.36, 0.06 });

            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 0.55, 0.93 }, new[] { 0.44, 0.66, 0.12 });
            Door(p, 0.0, 0.20, 0.95, 0.44, 0.66, stepOut: 0.20);
            foreach (var sx in new[] { -0.70, 0.70 })
                Win(p, sx, 0.55, 0.95, 0.30, 0.28);
            foreach (var sx in new[] { -0.60, 0.60 })
                Win(p, sx, 1.05, 0.85, 0.28, 0.26);

            // ---- T11 门锚点：door 板前脸 z=0.99，落位点贴门台阶外沿 ----
            DoorAnchor(p, 0.0, 0.20, 1.00);
            Humanoid.Anchor(p, 2.20, 1.60);
        });
    }

    //  酒馆（3×3，底座 2.60×2.40，最高约 1.36 米）
    //  剪影记号：半截草顶棚 + 两根撑杆撑起的帆布篷 + 篷下挂一盏灯 + 门外两张小桌。
    private static Tscn.EmitResult BuildRestaurant(string root)
    {
        return Emit(PathDef.Town(root), "Restaurant", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.16, 2.40 });
            Tscn.Bx(p, "hut", "woodL", new[] { 0.0, 0.16, 0.0 }, new[] { 1.70, 0.70, 1.60 });
            Gable(p, "roofGreen", 1.90, 1.80, 0.86, 0.32);
            Tscn.Bx(p, "chimney", "stoneM", new[] { 0.55, 0.16, -0.45 }, new[] { 0.28, 1.20, 0.28 });
            Tscn.Cbx(p, "awning", "canvas", new[] { 0.0, 0.86, 0.92 }, new[] { 1.80, 0.06, 0.56 },
                Humanoid.O(rx: 8));
            foreach (var sx in new[] { -0.80, 0.80 })
                Tscn.Cy(p, "awningPost", "woodD", new[] { sx, 0.16, 1.02 }, 0.10, 0.68);
            Tscn.Sp(p, "lantern", "glowWarm", new[] { 0.0, 0.82, 0.86 }, new[] { 0.18, 0.22, 0.18 });
            foreach (var sx in new[] { -0.85, 0.85 })
            {
                Tscn.Cy(p, "tableLeg", "woodD", new[] { sx, 0.16, -0.75 }, 0.10, 0.34);
                Tscn.Cn(p, "tableTop", "woodL", new[] { sx, 0.46, -0.75 }, 0.44, 0.08);
            }
            foreach (var sx in new[] { -1.05, 0.65 })
                Tscn.Cy(p, "stool", "woodD", new[] { sx, 0.16, -0.40 }, 0.20, 0.22);
            Tscn.Cbx(p, "door", "woodD", new[] { -0.40, 0.51, 0.78 }, new[] { 0.40, 0.58, 0.10 });
            Door(p, -0.40, 0.16, 0.80, 0.40, 0.58, stepOut: 0.20);
            Win(p, 0.40, 0.56, 0.80, 0.30, 0.26);

            // ---- T11 门锚点：door 板前脸 z=0.83 ----
            DoorAnchor(p, -0.40, 0.18, 0.85);
            Humanoid.Anchor(p, 1.70, 1.50);
        });
    }

    //  粮仓（2×2，底座 1.50×1.50，最高约 1.34 米）
    //  剪影记号：一只箍了三道铁圈的圆仓，顶上一个草帽锥。
    private static Tscn.EmitResult BuildGranary(string root)
    {
        return Emit(PathDef.Town(root), "Granary", r =>
        {
            var p = r;
            Tscn.Cy(p, "footing", "stoneM", new[] { 0.0, 0.0, 0.0 }, 1.50, 0.16);
            Tscn.Cy(p, "silo", "woodL", new[] { 0.0, 0.16, 0.0 }, 1.10, 0.80);
            Tscn.Cy(p, "hoopLo", "iron", new[] { 0.0, 0.34, 0.0 }, 1.16, 0.08);
            Tscn.Cy(p, "hoopHi", "iron", new[] { 0.0, 0.68, 0.0 }, 1.16, 0.08);
            Tscn.Cn(p, "cap", "thatch", new[] { 0.0, 0.96, 0.0 }, 1.30, 0.38);
            Tscn.Cbx(p, "hatch", "woodD", new[] { 0.0, 0.90, 0.42 }, new[] { 0.28, 0.16, 0.14 });
            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 0.36, 0.56 }, new[] { 0.30, 0.40, 0.10 });
            Door(p, 0.0, 0.16, 0.51, 0.30, 0.40, stepOut: 0.22);
            Tscn.Bx(p, "ladderRail", "woodD", new[] { -0.26, 0.16, 0.52 }, new[] { 0.06, 0.70, 0.06 });
            Tscn.Bx(p, "ladderRail2", "woodD", new[] { -0.06, 0.16, 0.52 }, new[] { 0.06, 0.70, 0.06 });
            for (var i = 0; i < 3; i++)
                Tscn.Bx(p, "rung", "woodL", new[] { -0.16, 0.24 + i * 0.24, 0.52 }, new[] { 0.28, 0.05, 0.05 });
            Tscn.Sp(p, "sackA", "crop", new[] { 0.46, 0.24, 0.44 }, new[] { 0.28, 0.26, 0.28 });
            Tscn.Sp(p, "sackB", "crop", new[] { 0.52, 0.22, 0.12 }, new[] { 0.24, 0.22, 0.24 });

            // ---- T11 门锚点：door 板在仓身（半径 0.55）前脸 z=0.61 ----
            DoorAnchor(p, 0.0, 0.18, 0.60);
            Humanoid.Anchor(p, 1.60, 0.90);
        });
    }

    //  宿舍（2×2，底座 1.60×1.51，最高约 1.05 米）
    //  剪影记号：一排亮着灯的低矮铺位 + 屋脊上一根砖烟囱。
    private static Tscn.EmitResult BuildDormitory(string root)
    {
        return Emit(PathDef.Town(root), "Dormitory", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 1.60, 0.12, 1.40 });
            Tscn.Bx(p, "hut", "woodL", new[] { 0.0, 0.12, 0.0 }, new[] { 1.40, 0.60, 1.20 });
            Gable(p, "thatch", 1.60, 1.40, 0.72, 0.26);
            Tscn.Bx(p, "chimney", "stoneM", new[] { 0.48, 0.12, -0.36 }, new[] { 0.24, 0.92, 0.24 });
            Tscn.Cbx(p, "door", "woodD", new[] { -0.34, 0.42, 0.58 }, new[] { 0.32, 0.44, 0.10 });
            Door(p, -0.34, 0.12, 0.60, 0.32, 0.44, stepOut: 0.16);
            foreach (var sx in new[] { 0.20, 0.56 })
                Win(p, sx, 0.46, 0.60, 0.22, 0.20);
            Tscn.Bx(p, "bench", "woodD", new[] { 0.30, 0.00, 0.72 }, new[] { 0.70, 0.10, 0.16 });

            // ---- T11 门锚点：door 板前脸 z=0.63，长凳让出落位（锚点贴门不贴凳）----
            DoorAnchor(p, -0.34, 0.18, 0.65);
            Humanoid.Anchor(p, 1.30, 0.90);
        });
    }

    //  教堂（2×2，底座 1.50×1.39，最高约 1.84 米）
    //  剪影记号：一根方塔顶着蓝尖顶，尖顶上立一个十字架。
    private static Tscn.EmitResult BuildChurch(string root)
    {
        return Emit(PathDef.Town(root), "Church", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 1.50, 0.12, 1.50 });
            Tscn.Bx(p, "nave", "stoneL", new[] { 0.0, 0.12, 0.10 }, new[] { 0.90, 0.55, 1.20 });
            Gable(p, "roofBlue", 1.00, 1.30, 0.67, 0.24);
            Tscn.Bx(p, "tower", "stoneM", new[] { 0.0, 0.12, -0.42 }, new[] { 0.40, 1.30, 0.40 });
            Tscn.Cbx(p, "belfry", "woodD", new[] { 0.0, 1.16, -0.42 }, new[] { 0.46, 0.10, 0.46 });
            Tscn.Cn(p, "spire", "roofBlue", new[] { 0.0, 1.38, -0.42 }, 0.44, 0.34);
            Tscn.Cy(p, "crossStem", "woodD", new[] { 0.0, 1.64, -0.42 }, 0.05, 0.20);
            Tscn.Cbx(p, "crossBar", "woodD", new[] { 0.0, 1.78, -0.42 }, new[] { 0.20, 0.05, 0.05 });
            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 0.37, 0.68 }, new[] { 0.32, 0.46, 0.10 });
            Door(p, 0.0, 0.12, 0.70, 0.32, 0.48, stepOut: 0.14);
            // 玫瑰窗（2026-10-28 审查修复：原来在塔后那面，外面看不见）：正门上方垫一块山墙额枋块，
            // 窗压在额枋正面 —— 深色外圈 + 竖直 Disc 亮面。
            Tscn.Bx(p, "gableFace", "stoneL", new[] { 0.0, 0.62, 0.66 }, new[] { 0.90, 0.34, 0.08 });
            Tscn.Cbx(p, "roseFrame", "stoneD", new[] { 0.0, 0.80, 0.67 }, new[] { 0.30, 0.30, 0.12 });
            Tscn.Disc(p, "roseGlass", "glowWarm", new[] { 0.0, 0.80, 0.72 }, 0.20, 0.06);
            // 中殿两侧各两扇拱窗（走窗助手，石框）
            foreach (var s in new[] { -1, 1 })
                foreach (var tz in new[] { 0.34, -0.12 })
                    Win(p, tz, 0.38, s * 0.45, 0.16, 0.26, axis: 'x', dir: s, frame: "stoneD");

            // ---- T11 门锚点：door 板前脸 z=0.73 ----
            DoorAnchor(p, 0.0, 0.18, 0.75);
            Humanoid.Anchor(p, 2.10, 0.90);
        });
    }

    //  传送阵（2×2，底座 1.50×1.50，最高约 1.10 米）
    //  剪影记号：三根立石围一圈，符纹**躺在台面上**。
    //  刻意的：符文圈不悬空 —— 离线校验器把「离地又孤立的一块」判悬空件，
    //  而这一格没有登记过「故意空着的缝」（Check.cs 的 IntentionalGaps 只有磁悬浮底座那条）。
    private static Tscn.EmitResult BuildTeleportArray(string root)
    {
        return Emit(PathDef.Town(root), "TeleportArray", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 1.50, 0.12, 1.50 });
            foreach (var s in new[] { -1.0, 1.0 })
            {
                Tscn.Bx(p, "runeNS", "glowWarm", new[] { 0.0, 0.12, s * 0.42 }, new[] { 0.90, 0.04, 0.12 });
                Tscn.Bx(p, "runeEW", "glowWarm", new[] { s * 0.42, 0.12, 0.0 }, new[] { 0.12, 0.04, 0.90 });
            }
            var stones = new (double X, double Z, double H)[]
            {
                (0.46, 0.46, 0.72), (-0.46, 0.46, 0.60), (0.00, -0.50, 0.84),
            };
            foreach (var s in stones)
            {
                Tscn.Bx(p, "stone", "stoneD", new[] { s.X, 0.12, s.Z }, new[] { 0.20, s.H, 0.20 });
                Tscn.Sp(p, "crystal", "glowWarm", new[] { s.X, s.H + 0.14, s.Z }, new[] { 0.24, 0.24, 0.24 });
            }

            // ---- T11 门锚点：法阵无门，入口取南侧台缘（runeNS 南条外沿 z=0.48 再外一档）----
            DoorAnchor(p, 0.0, 0.16, 0.75);

            Humanoid.Anchor(p, 1.40, 0.90);
        });
    }

    // ============================================================
    //  镇中央地标：竞技场（静态件，4×4 格，底座 3.60 × 3.60 米，最高约 1.95 米）
    //  它被摧毁即失守（spec 战斗与胜负节），所以造型上是全场唯一一处暖光。
    //
    //  2026-10-14 缩聚重生成：早先那版是 7.20 × 7.20 米，按旧版 2.0 米格长做的。
    //  新网格格长 1.0 米，4×4 的底座上限 3.8 米 —— 同一块地上要塞的东西多了一倍，
    //  竞技场缩到 3.60 米见方，墙与看台一档一档跟着降。
    //
    //  剪影记号：矮石墙围一圈 + 正面一道门 + 中央一座**常年点着的火盆**。
    //  夜里从大地图远处看，只有那团光是动的 —— 这就是「我家在哪」的读法。
    // ============================================================
    private static Tscn.EmitResult BuildArena(string root)
    {
        return Emit(PathDef.Town(root), "Arena", r =>
        {
            var p = r;
            const double Half = 1.55;   // 墙心离中轴
            Tscn.Bx(p, "pad", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 3.60, 0.30, 3.60 });
            Tscn.Bx(p, "floor", "sand", new[] { 0.0, 0.30, 0.0 }, new[] { 3.10, 0.14, 3.10 });

            // 墙：后与两侧整面，正面留一道门（怪从甲板边缘爬进来要有个朝向）
            Tscn.Bx(p, "wallN", "stoneL", new[] { 0.0, 0.30, -Half }, new[] { 3.30, 0.90, 0.22 });
            Tscn.Bx(p, "wallW", "stoneL", new[] { -Half, 0.30, 0.0 }, new[] { 0.22, 0.90, 2.88 });
            Tscn.Bx(p, "wallE", "stoneL", new[] { Half, 0.30, 0.0 }, new[] { 0.22, 0.90, 2.88 });
            Tscn.Bx(p, "wallSw", "stoneL", new[] { -1.10, 0.30, Half }, new[] { 1.10, 0.90, 0.22 });
            Tscn.Bx(p, "wallSe", "stoneL", new[] { 1.10, 0.30, Half }, new[] { 1.10, 0.90, 0.22 });
            // 城齿：后面整条一排，正面只在门两侧各排一段（门洞上摆垛口是悬空件）
            Crenels(p, "crenel", "stoneM", 0, -Half, 3.30, 1.20, 0.30, 0.18, 0.22, gapX: 0);
            Crenels(p, "crenel", "stoneM", 0, Half, 3.30, 1.20, 0.30, 0.18, 0.22, gapX: 0.46);

            // 看台：一圈半人高的石阶，中间是斗场
            Tscn.Bx(p, "tierN", "stoneM", new[] { 0.0, 0.44, -0.80 }, new[] { 2.20, 0.28, 0.40 });
            Tscn.Bx(p, "tierS", "stoneM", new[] { 0.0, 0.44, 0.80 }, new[] { 2.20, 0.28, 0.40 });
            Tscn.Bx(p, "tierW", "stoneM", new[] { -0.80, 0.44, 0.0 }, new[] { 0.40, 0.28, 1.40 });
            Tscn.Bx(p, "tierE", "stoneM", new[] { 0.80, 0.44, 0.0 }, new[] { 0.40, 0.28, 1.40 });

            // 正面门洞加拱楣（2026-10-28 审查修复：入口要一眼读成门洞；楣压住两侧门墙才不判悬空件）
            Tscn.Cbx(p, "gateArch", "stoneL", new[] { 0.0, 1.16, Half }, new[] { 1.30, 0.20, 0.34 });

            // 中央火盆：石座 + 铜盆（口朝上的截锥）+ 锥形火焰（全场唯一的暖光源）
            Tscn.Cy(p, "pedestal", "stoneD", new[] { 0.0, 0.44, 0.0 }, 0.50, 0.34);
            Tscn.ConeEnd(p, "bowl", "bronze", new[] { 0.0, 0.96, 0.0 }, 0.62, 0.38, Humanoid.O(rx: 180));
            Tscn.Cn(p, "flame", "glowFire", new[] { 0.0, 0.86, 0.0 }, 0.44, 0.46);

            // 四角火柱：墙头上各一支 torches（2026-10-28 审查修复：缩小一档，别和中央火盆抢读法）
            foreach (var sx in new[] { -Half, Half })
                foreach (var sz in new[] { -Half, Half })
                {
                    Tscn.Cy(p, "torchPost", "woodD", new[] { sx, 1.20, sz }, 0.07, 0.48);
                    Tscn.Sp(p, "torchFire", "glowFire", new[] { sx, 1.70, sz }, new[] { 0.14, 0.16, 0.14 });
                }

            // 名牌锚点：地标是唯一有「血条」读法的静态件（被摧毁即失守）
            // ---- T11 门锚点：正面门洞（wallSw/wallSe 之间，墙心 z=1.55、面 z=1.66）----
            DoorAnchor(p, 0.0, 0.30, 1.60);
            Humanoid.Anchor(p, 2.20, 1.80);
        });
    }

    // ============================================================
    //  工坊（两种资源都出一点，3×3 格，底座 2.60 × 2.40 米，最高约 2.51 米含烟）
    //  剪影记号：木骨架房 + 厚茅顶 + 石砌烟囱一直在冒烟 + 门口一只铁砧与开着的炉口。
    //  炉口那点橙光是镇上唯一的第二个光源（夜里与竞技场的火一南一北）。
    //
    //  2026-10-14 缩聚重生成：早先那版实测 7.21 × 7.21 米，也是按旧版 2.0 米格长做的。
    //  新网格格长 1.0 米，3×3 的底座上限 2.8 米。屋顶改走 Gable（见形状助手一节）：
    //  金字塔图元 Py 带 45° 偏航，离线校验器按旋转盒子算包围盒，XZ 两向会各涨 √2 倍 ——
    //  2.6 米的底座用 Py 会算成 3.68 米，直接顶破上限。
    // ============================================================
    private static Tscn.EmitResult BuildWorkshop(string root)
    {
        return Emit(PathDef.Town(root), "Workshop", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 2.60, 0.24, 2.40 });
            Tscn.Bx(p, "hall", "woodL", new[] { 0.0, 0.24, 0.0 }, new[] { 2.10, 0.90, 1.90 });
            Tscn.Bx(p, "sill", "woodD", new[] { 0.0, 0.24, 0.0 }, new[] { 2.24, 0.10, 2.04 });
            Tscn.Bx(p, "postA", "woodD", new[] { -0.96, 0.34, 0.86 }, new[] { 0.14, 0.80, 0.14 });
            Tscn.Bx(p, "postB", "woodD", new[] { 0.96, 0.34, 0.86 }, new[] { 0.14, 0.80, 0.14 });
            Gable(p, "thatch", 2.30, 2.10, 1.14, 0.40);

            // 烟囱：穿出屋顶，顶上一口压顶。烟 = 三颗渐小的烟团斜着往上飘
            // （2026-10-28 审查修复：原来两个大实心灰球读成烟囱顶长瘤）
            Tscn.Bx(p, "stack", "stoneD", new[] { 0.70, 0.24, -0.60 }, new[] { 0.30, 1.30, 0.30 });
            Tscn.Bx(p, "cap", "stoneM", new[] { 0.70, 1.54, -0.60 }, new[] { 0.42, 0.10, 0.42 });
            Tscn.Sp(p, "smokeA", "ash", new[] { 0.70, 1.66, -0.60 }, new[] { 0.30, 0.30, 0.30 });
            Tscn.Sp(p, "smokeB", "ash", new[] { 0.78, 1.86, -0.56 }, new[] { 0.24, 0.24, 0.24 });
            Tscn.Sp(p, "smokeC", "ash", new[] { 0.85, 2.00, -0.52 }, new[] { 0.18, 0.18, 0.18 });

            // 炉口（发光的）+ 铁楣 + 门 + 窗
            Tscn.Cbx(p, "forgeMouth", "glowFire", new[] { -0.58, 0.62, 0.92 }, new[] { 0.46, 0.40, 0.10 });
            Tscn.Cbx(p, "forgeLintel", "iron", new[] { -0.58, 0.88, 0.92 }, new[] { 0.56, 0.08, 0.14 });
            Tscn.Cbx(p, "door", "woodD", new[] { 0.34, 0.78, 0.92 }, new[] { 0.44, 0.68, 0.10 });
            Door(p, 0.34, 0.24, 0.95, 0.44, 0.68, stepOut: 0.24);
            Win(p, 0.82, 0.82, 0.95, 0.34, 0.34);

            // 门前的铁砧：桩 + 砧身 + 砧角。摆在台基之外，所以**落地面是 y=0**（不是台基顶 0.24）
            Tscn.Cy(p, "stump", "woodD", new[] { -0.86, 0.00, 0.92 }, 0.28, 0.26);
            Tscn.Cbx(p, "anvil", "iron", new[] { -0.86, 0.39, 0.92 }, new[] { 0.40, 0.20, 0.18 });
            Tscn.Cbx(p, "anvilTop", "steel", new[] { -0.86, 0.52, 0.92 }, new[] { 0.44, 0.06, 0.22 });
            Tscn.Cn(p, "anvilHorn", "iron", new[] { -0.62, 0.34, 0.92 }, 0.12, 0.20,
                Humanoid.O(rz: -90));
            // 靠墙的柴垛：躺平的两根（原木用横放的盒子，底面才真正落在 y=0）
            for (var i = 0; i < 2; i++)
                Tscn.Cbx(p, "log", "woodD", new[] { -1.00, 0.14 + i * 0.30, -0.40 },
                    new[] { 0.24, 0.24, 1.10 });

            // 名牌锚点：十三件建筑统一都带（.x = 血条宽、.y = 名牌高，表现层读它）
            // ---- T11 门锚点：door 板前脸 z=0.97（铁砧在门前西侧，锚点贴门）----
            DoorAnchor(p, 0.34, 0.22, 1.00);
            Humanoid.Anchor(p, 2.80, 1.60);
        });
    }

    // ============================================================
    //  门窗助手（2026-10-28 模型审查修复：窗不再读成贴墙白块）
    //
    //  窗 = 深色外框嵌进墙面并探出一截 + 发光面再探过框面一小截：
    //  十视图里读成「有框的洞、洞里透光」，不再是一块贴墙的亮片。
    //  门 = 两侧门框立柱 + 楣梁，可选门前台阶。
    //  坐标一律设计米；face 给门窗外那面墙的外表面；底座撑大与否按各档上限核。
    // ============================================================

    /// <summary>窗：深色框 + 内凹发光面。axis 'z' 面朝南/北、'x' 面朝东/西；dir = 墙外法线方向。</summary>
    private static void Win(string p, double cx, double cy, double face, double w, double h,
        char axis = 'z', int dir = 1, string frame = "woodD")
    {
        var f = face - dir * 0.03;   // 框中心：嵌墙 0.09、探出 0.03
        var g = face + dir * 0.02;   // 发光面中心：探过框面 0.04，读成洞里的光
        if (axis == 'z')
        {
            Tscn.Cbx(p, "winFrame", frame, new[] { cx, cy, f }, new[] { w + 0.14, h + 0.14, 0.12 });
            Tscn.Cbx(p, "winGlass", "glowWarm", new[] { cx, cy, g }, new[] { w, h, 0.10 });
        }
        else
        {
            Tscn.Cbx(p, "winFrame", frame, new[] { f, cy, cx }, new[] { 0.12, h + 0.14, w + 0.14 });
            Tscn.Cbx(p, "winGlass", "glowWarm", new[] { g, cy, cx }, new[] { 0.10, h, w });
        }
    }

    /// <summary>门框：两侧立柱 + 楣梁（门板留在各 Build 里，框贴着门板加一圈）。stepOut = 台阶探出墙面的深度，0 = 不加台阶。</summary>
    private static void Door(string p, double c, double y, double face, double w, double h,
        double stepOut = 0.24)
    {
        foreach (var s in new[] { -1.0, 1.0 })
            Tscn.Bx(p, "doorJamb", "woodD", new[] { c + s * (w / 2.0 + 0.06), y - 0.06, face - 0.03 },
                new[] { 0.12, h + 0.12, 0.12 });
        Tscn.Cbx(p, "doorHead", "woodD", new[] { c, y + h + 0.06, face - 0.03 }, new[] { w + 0.36, 0.12, 0.12 });
        if (stepOut <= 0) return;
        Tscn.Bx(p, "doorStep", "stoneM", new[] { c, 0.0, face + stepOut / 2.0 - 0.06 },
            new[] { w + 0.40, 0.14, stepOut + 0.06 });
    }

    /// <summary>
    /// 一排城齿：沿 <paramref name="z"/> 那条边等距摆 <paramref name="h"/> 高的小垛。
    /// <paramref name="gapX"/> 是中段留空的门洞半宽（0 = 整条一排）—— 门洞上摆垛口会变成悬空件。
    /// 垛身占一格的比例是 <paramref name="blk"/>：留缝才读得出是垛口不是一堵实墙。
    /// </summary>
    private static void Crenels(string p, string n, string m, double cx, double z, double w,
        double y, double step, double h, double blk, double gapX)
    {
        var nx = Math.Max(2, (int)Math.Round(w / step));
        for (var i = 0; i < nx; i++)
        {
            var x = cx - w / 2 + (i + 0.5) * w / nx;
            if (gapX > 0 && Math.Abs(x) < gapX + w / nx * blk) continue;
            Tscn.Cbx(p, n, m, new[] { x, y + h / 2, z }, new[] { w / nx * blk, h, 0.30 });
        }
    }
}
