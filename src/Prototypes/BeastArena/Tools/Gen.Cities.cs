#nullable enable
// 巨兽竞技场 造型源场景生成器 —— 三城地标域（T13：三座主地标、十二件背景建筑、三套地表）。
//
// 本文件是 Gen 的部分实现（partial），按域拆分见 Gen.cs 文件头那一段。
// 约定同老件：最低点落在本地 y=0（表现层自己决定把它摆到多高）。
//
// **城的本地系与 `CityNode` 同一条**：模型原点 = 城中心（`MapPlace.CityCenter`），
// 城门朝 +Z 面，占地沿 Z 恰好 96 米（±48）—— 这一格不是审美：
// `ArenaTuning.CityHalfDepthMeters = 48` 与 `DeckView.VerifyLandmarkSizes` 按实测进深验它，
// 差超过 0.05 米就当场报错（兽压城 / 城离航线那一格由这两处钉死）。
// 背景建筑与地表件也按同一系摆（表现层给局部偏移），城门朝向沿用「法线朝南转 180 度」那一档。
//
// 剪影互不相似（ticket 验收「三城地标剪影互不相似」）：
//   · 贫城：土台 + 歪斜木屋 + 茅草 + 木栅 —— 低、乱、木色。
//   · 沙城：土坯方城 + 风口塔 + 圆顶 —— 平、厚、沙色。
//   · 富城：对称石殿 + 蓝灰双坡顶 + 门楼双塔 —— 齐、高、象牙色。
//
// **锚点 Marker3D**（ticket「码头与道路接点锚点」）：每件地标内部登记三个接点，
// 都在模型本地系里，世界坐标 = 城中心绕 Y（法线朝南时转 180 度）换算过去：
//   · GateAnchor  (0, 0, ±本地) —— 城门接点：本地 (0, 0.3, 48)，正好落在城门口。
//   · DockAnchor  码头接点：本地 (0, 0.5, 52)，城门朝航线外 4 米。
//   · RoadAnchor  道路接点：本地 (0, 0.2, 56)，朝航线外 8 米（道路从这一点往外接）。

using System;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    // 城模型的进深（米）：与 ArenaTuning.CityHalfDepthMeters ×2 同值，改一处要回读那两处。
    private const double CityDepth = 96.0;

    // ---------------- 三座主地标 ----------------

    //  贫城主地标：土台聚落 —— 歪斜木屋两座、高脚粮仓一座、土窑一座、木栅环台、南面木城门。
    //  占地 84 × 96 米（Z 向恰好 96，见文件头那条），最高 14.2 米，锚点 24 米。
    private static Tscn.EmitResult BuildLandmarkPoor(string root)
    {
        return Emit(PathDef.Props(root), "LandmarkPoor", r =>
        {
            var p = r;
            // 土台：整件的底，Z 向 -48..48 —— 城的进深就是这一块给的。
            Tscn.Bx(p, "mound", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 84.0, 2.0, CityDepth });
            // 北面木栅墙 + 侧栅梁，压在土台顶沿上。
            Tscn.Bx(p, "backWall", "woodD", new[] { 0.0, 2.0, -47.0 }, new[] { 84.0, 4.0, 2.0 });
            Tscn.Bx(p, "railW", "woodD", new[] { -41.0, 2.0, -24.0 }, new[] { 1.5, 3.0, 44.0 });
            Tscn.Bx(p, "railE", "woodD", new[] { 41.0, 2.0, -24.0 }, new[] { 1.5, 3.0, 44.0 });
            for (var i = 0; i < 7; i++)
            {
                var z = -40.0 + i * 12.0;
                Tscn.Bx(p, "postW", "woodD", new[] { -40.5, 2.0, z - 0.8 }, new[] { 1.6, 5.0, 1.6 });
                Tscn.Bx(p, "postE", "woodD", new[] { 40.5, 2.0, z - 0.8 }, new[] { 1.6, 5.0, 1.6 });
            }

            // 歪木屋 A：西前角，正摆。
            var shackA = Tscn.GrpT("ShackA", p, new[] { -20.0, 2.0, 14.0 }, null);
            Tscn.Bx(shackA, "wall", "woodL", new[] { 0.0, 0.0, 0.0 }, new[] { 14.0, 6.0, 10.0 });
            Tscn.Bx(shackA, "door", "woodD", new[] { -1.5, 0.0, 4.6 }, new[] { 3.0, 4.2, 1.2 });
            Tscn.Cbx(shackA, "window", "glowWarm", new[] { 7.1, 3.4, 0.0 }, new[] { 0.4, 1.6, 2.4 });
            Gable(shackA, "thatch", 15.0, 11.0, 6.0, 2.6, 0.5);

            // 歪木屋 B：西后角，整组斜 8 度 —— 剪影里那一点歪是贫城的全部意思。
            var shackB = Tscn.GrpT("ShackB", p, new[] { -24.0, 2.0, -20.0 }, new[] { 0.0, 0.0, 8.0 });
            Tscn.Bx(shackB, "wall", "woodL", new[] { 0.0, 0.0, 0.0 }, new[] { 11.0, 7.0, 9.0 });
            Gable(shackB, "thatch", 12.0, 10.0, 7.0, 2.4, 0.5);

            // 高脚粮仓：东后角，四腿撑一座木箱（floor/body 的底面锚定按中心给）。
            var granary = Tscn.GrpT("GranaryStilt", p, new[] { 24.0, 2.0, -22.0 }, null);
            foreach (var sx in new[] { -3.5, 3.5 })
                foreach (var sz in new[] { -2.5, 2.5 })
                    Tscn.Bx(granary, "stilt", "woodD", new[] { sx, 0.0, sz }, new[] { 1.0, 4.0, 1.0 });
            Tscn.Bx(granary, "floor", "deckWood", new[] { 0.0, 4.0, 0.0 }, new[] { 8.0, 0.6, 6.0 });
            Tscn.Bx(granary, "body", "woodL", new[] { 0.0, 4.4, 0.0 }, new[] { 7.0, 4.5, 5.0 });
            Gable(granary, "thatch", 8.0, 7.0, 8.9, 2.0, 0.45);

            // 土窑：台前偏东，窑口那点光是夜里唯一的光源。
            Tscn.Cy(p, "kiln", "stoneD", new[] { 14.0, 2.0, 12.0 }, 7.0, 8.0);
            Tscn.Cbx(p, "kilnMouth", "glowFire", new[] { 14.0, 3.2, 15.3 }, new[] { 2.2, 2.2, 1.2 });
            Tscn.Bx(p, "kilnChimney", "brick", new[] { 16.2, 6.0, 12.0 }, new[] { 1.2, 4.0, 1.2 });

            // 柴垛与土堆：把台面摆乱。
            Tscn.Sp(p, "dirtPileA", "dirt", new[] { -6.0, 2.6, 26.0 }, new[] { 10.0, 3.0, 8.0 });
            Tscn.Sp(p, "dirtPileB", "dirt", new[] { 30.0, 2.4, 8.0 }, new[] { 8.0, 2.6, 7.0 });
            Tscn.Bx(p, "brushA", "thatch", new[] { -34.0, 2.0, 2.0 }, new[] { 4.0, 1.8, 2.6 });
            Tscn.Bx(p, "brushB", "thatch", new[] { 4.0, 2.0, -34.0 }, new[] { 3.0, 1.6, 4.0 });

            // 木城门：两根门柱 + 一道横梁，门柱贴在 +Z 面沿（z=45..48）。
            Tscn.Bx(p, "gatePostW", "woodD", new[] { -5.5, 2.0, 45.0 }, new[] { 3.0, 10.0, 3.0 });
            Tscn.Bx(p, "gatePostE", "woodD", new[] { 5.5, 2.0, 45.0 }, new[] { 3.0, 10.0, 3.0 });
            Tscn.Cbx(p, "gateBeam", "woodL", new[] { 0.0, 12.4, 46.4 }, new[] { 14.0, 1.8, 2.6 });
            Tscn.Cy(p, "bollardW", "woodD", new[] { -3.5, 2.0, 43.5 }, 1.2, 2.4);
            Tscn.Cy(p, "bollardE", "woodD", new[] { 3.5, 2.0, 43.5 }, 1.2, 2.4);

            Humanoid.Anchor(p, 24.0, 10.0);
            CityAnchors(p);
        });
    }

    //  沙城主地标：土坯方城 —— 环墙 + 风口塔 + 圆顶仓 + 平顶坊，南面拱门。
    //  占地 90 × 96 米（Z 向恰好 96），最高 24.4 米，锚点 27 米。
    private static Tscn.EmitResult BuildLandmarkSand(string root)
    {
        return Emit(PathDef.Props(root), "LandmarkSand", r =>
        {
            var p = r;
            Tscn.Bx(p, "plate", "sand", new[] { 0.0, 0.0, 0.0 }, new[] { 90.0, 1.6, CityDepth });
            // 环墙：北、西、东三段，压在台沿上，南面开口留给拱门。
            Tscn.Cbx(p, "wallN", "sand", new[] { 0.0, 4.0, -44.0 }, new[] { 90.0, 4.8, 3.0 });
            Tscn.Cbx(p, "wallW", "sand", new[] { -43.5, 4.0, -6.0 }, new[] { 3.0, 4.8, 78.0 });
            Tscn.Cbx(p, "wallE", "sand", new[] { 43.5, 4.0, -6.0 }, new[] { 3.0, 4.8, 78.0 });

            // 风口塔：高 box + 三排风缝 + 顶盖，全件的最高剪影。
            var tower = Tscn.GrpT("Windcatcher", p, new[] { -20.0, 1.6, -8.0 }, null);
            Tscn.Bx(tower, "shaft", "sand", new[] { 0.0, 0.0, 0.0 }, new[] { 11.0, 22.0, 11.0 });
            for (var i = 0; i < 3; i++)
                Tscn.Cbx(tower, "slot", "woodD", new[] { 5.7, 6.5 + i * 5.5, 0.0 }, new[] { 0.6, 3.2, 6.5 });
            Tscn.Cbx(tower, "cap", "woodD", new[] { 0.0, 21.8, 0.0 }, new[] { 12.0, 0.8, 12.0 });

            // 圆顶仓：鼓座 + 扁圆顶。
            var dome = Tscn.GrpT("DomeHall", p, new[] { 16.0, 1.6, -14.0 }, null);
            Tscn.Bx(dome, "drum", "stoneL", new[] { 0.0, 0.0, 0.0 }, new[] { 14.0, 3.0, 14.0 });
            Tscn.Sp(dome, "shell", "sand", new[] { 0.0, 3.0, 0.0 }, new[] { 14.0, 7.0, 14.0 });
            Tscn.Bx(dome, "door", "woodD", new[] { -1.4, 0.0, 6.2 }, new[] { 2.8, 3.4, 1.2 });

            // 平顶坊两进：矮 box + 女儿墙点。
            var blockA = Tscn.GrpT("BlockA", p, new[] { 30.0, 1.6, 10.0 }, null);
            Tscn.Bx(blockA, "body", "sand", new[] { 0.0, 0.0, 0.0 }, new[] { 12.0, 6.0, 10.0 });
            foreach (var sx in new[] { -5.0, 0.0, 5.0 })
                Tscn.Bx(blockA, "merlon", "stoneM", new[] { sx - 0.7, 6.0, 4.4 }, new[] { 1.4, 1.2, 1.2 });
            var blockB = Tscn.GrpT("BlockB", p, new[] { -6.0, 1.6, 16.0 }, null);
            Tscn.Bx(blockB, "body", "sand", new[] { 0.0, 0.0, 0.0 }, new[] { 10.0, 4.5, 8.0 });
            Tscn.Bx(blockB, "step", "stoneM", new[] { -1.2, 0.0, 4.4 }, new[] { 2.4, 1.0, 1.6 });

            // 拱门：双土坯墩 + 木梁 + 顶上一对小穹隆，贴在 +Z 面沿。
            Tscn.Cbx(p, "gateTowerW", "sand", new[] { -7.5, 4.6, 44.5 }, new[] { 6.0, 6.0, 6.0 });
            Tscn.Cbx(p, "gateTowerE", "sand", new[] { 7.5, 4.6, 44.5 }, new[] { 6.0, 6.0, 6.0 });
            Tscn.Cbx(p, "gateLintel", "woodL", new[] { 0.0, 7.4, 44.5 }, new[] { 13.0, 1.6, 3.2 });
            Tscn.Cn(p, "gateCupW", "stoneL", new[] { -7.5, 7.4, 44.5 }, 4.6, 2.6);
            Tscn.Cn(p, "gateCupE", "stoneL", new[] { 7.5, 7.4, 44.5 }, 4.6, 2.6);
            Tscn.Cbx(p, "roadSlab", "slab", new[] { 0.0, 1.75, 46.0 }, new[] { 9.0, 0.3, 3.6 });

            Humanoid.Anchor(p, 27.0, 10.0);
            CityAnchors(p);
        });
    }

    //  富城主地标：对称石殿 —— 中殿双坡蓝灰顶 + 中央金顶塔 + 列柱长廊 + 门楼双塔 + 前庭喷泉。
    //  占地 108 × 96 米（Z 向恰好 96），最高 30.6 米，锚点 33 米。
    private static Tscn.EmitResult BuildLandmarkRich(string root)
    {
        return Emit(PathDef.Props(root), "LandmarkRich", r =>
        {
            var p = r;
            Tscn.Bx(p, "plate", "stoneL", new[] { 0.0, 0.0, 0.0 }, new[] { 108.0, 1.8, CityDepth });
            Tscn.Cbx(p, "plaza", "slab", new[] { 0.0, 1.95, 18.0 }, new[] { 90.0, 0.5, 46.0 });

            // 中殿 + 双坡顶。
            var hall = Tscn.GrpT("MainHall", p, new[] { 0.0, 1.8, -16.0 }, null);
            Tscn.Bx(hall, "wall", "stoneL", new[] { 0.0, 0.0, 0.0 }, new[] { 40.0, 16.0, 24.0 });
            for (var i = 0; i < 5; i++)
                Tscn.Cbx(hall, "window", "glowWarm", new[] { -16.0 + i * 8.0, 9.0, 12.1 }, new[] { 2.0, 3.6, 0.5 });
            Tscn.Bx(hall, "step", "stoneM", new[] { 0.0, 0.0, 11.4 }, new[] { 18.0, 1.4, 5.0 });
            Gable(hall, "roofBlue", 42.0, 26.0, 16.0, 7.0, 0.7);
            // 中央塔穿过屋顶，金顶是全局的最高点。
            Tscn.Cbx(hall, "tower", "stoneL", new[] { 0.0, 20.0, 0.0 }, new[] { 12.0, 12.0, 12.0 });
            Tscn.Cy(hall, "goldCap", "gold", new[] { 0.0, 25.6, 0.0 }, 4.0, 2.0);

            // 两侧列柱长廊：六根一列，梁压柱头。柱底压在台面上（台面顶 1.8）。
            foreach (var sx in new[] { -44.0, 44.0 })
            {
                for (var i = 0; i < 6; i++)
                {
                    var z = -20.0 + i * 10.0;
                    Tscn.Cy(p, "col", "stoneL", new[] { sx, 1.8, z }, 2.6, 8.0);
                }
                Tscn.Cbx(p, "architrave", "stoneL", new[] { sx, 9.7, 5.0 }, new[] { 3.0, 1.6, 52.0 });
            }

            // 门楼：双塔 + 中间门墙 + 青铜楣，贴在 +Z 面沿（塔体 z=41..47，坡顶投影不过 48）。
            foreach (var sx in new[] { -9.0, 9.0 })
            {
                var t = Tscn.GrpT("GateTower", p, new[] { sx, 1.8, 44.0 }, null);
                Tscn.Cbx(t, "body", "stoneL", new[] { 0.0, 9.0, 0.0 }, new[] { 7.0, 18.0, 6.0 });
                Gable(t, "roofBlue", 8.0, 7.0, 18.0, 4.0, 0.6);
                Tscn.Cy(t, "finial", "gold", new[] { 0.0, 21.8, 0.0 }, 1.6, 1.6);
            }
            Tscn.Cbx(p, "gateWall", "stoneL", new[] { 0.0, 5.8, 44.6 }, new[] { 34.0, 8.0, 2.2 });
            Tscn.Cbx(p, "gateDoor", "woodL", new[] { 0.0, 5.3, 44.4 }, new[] { 7.0, 7.0, 1.6 });
            Tscn.Cbx(p, "gateBronze", "bronze", new[] { 0.0, 9.4, 44.6 }, new[] { 8.0, 1.2, 2.6 });

            // 前庭喷泉：石盆 + 水面 + 中央短柱（盆是实心台，水面压在盆沿上才看得见）。
            var fountain = Tscn.GrpT("Fountain", p, new[] { 0.0, 2.2, 28.0 }, null);
            Tscn.Cy(fountain, "basin", "stoneM", new[] { 0.0, 0.0, 0.0 }, 10.0, 1.3);
            Tscn.Plate(fountain, "water", "water", new[] { 0.0, 1.42, 0.0 }, 8.2, 0.3);
            Tscn.Cy(fountain, "jet", "stoneL", new[] { 0.0, 0.6, 0.0 }, 1.4, 2.2);

            Humanoid.Anchor(p, 33.0, 10.0);
            CityAnchors(p);
        });
    }

    // ---------------- 十二件背景建筑 ----------------

    //  贫城四件：木板歪屋 / 圆顶草屋 / 高脚草粮仓 / 砖土窑。
    private static Tscn.EmitResult BuildBackPoorA(string root)
        => Emit(PathDef.Props(root), "BackPoorA", r =>
        {
            var p = r;
            Tscn.Bx(p, "wall", "woodL", new[] { 0.0, 0.0, 0.0 }, new[] { 10.0, 6.0, 8.0 });
            Tscn.Bx(p, "door", "woodD", new[] { -1.4, 0.0, 3.6 }, new[] { 2.8, 4.0, 1.0 });
            Tscn.Cbx(p, "window", "glowWarm", new[] { 5.1, 3.4, 0.0 }, new[] { 0.4, 1.5, 2.2 });
            Gable(p, "thatch", 11.0, 9.0, 6.0, 2.2, 0.45);
            Humanoid.Anchor(p, 9.6, 6.0);
        });

    private static Tscn.EmitResult BuildBackPoorB(string root)
        => Emit(PathDef.Props(root), "BackPoorB", r =>
        {
            var p = r;
            Tscn.Cy(p, "body", "woodL", new[] { 0.0, 0.0, 0.0 }, 8.0, 4.0);
            Tscn.Cn(p, "roof", "thatch", new[] { 0.0, 3.8, 0.0 }, 9.5, 3.5);
            Tscn.Bx(p, "door", "woodD", new[] { -1.3, 0.0, 3.6 }, new[] { 2.6, 3.2, 1.0 });
            Humanoid.Anchor(p, 8.2, 5.0);
        });

    //  票 06：粮仓主体正面在梯顶处加一扇暗门——woodD 门框 + char 门板贴在 +Z 面，
    //  门与梯子、仓体三面搭接，读得出「顺着梯子上去有一口能进仓的门」。
    private static Tscn.EmitResult BuildBackPoorC(string root)
        => Emit(PathDef.Props(root), "BackPoorC", r =>
        {
            var p = r;
            foreach (var sx in new[] { -3.5, 3.5 })
                foreach (var sz in new[] { -2.5, 2.5 })
                    Tscn.Bx(p, "stilt", "woodD", new[] { sx, 0.0, sz }, new[] { 1.0, 4.0, 1.0 });
            Tscn.Bx(p, "floor", "deckWood", new[] { 0.0, 4.0, 0.0 }, new[] { 8.0, 0.6, 6.0 });
            Tscn.Bx(p, "body", "woodL", new[] { 0.0, 4.4, 0.0 }, new[] { 7.0, 4.5, 5.0 });
            Gable(p, "thatch", 8.0, 7.0, 8.9, 2.0, 0.45);
            Tscn.Bx(p, "ladder", "woodD", new[] { 3.6, 0.0, 2.2 }, new[] { 0.5, 4.3, 0.5 });
            Tscn.Cbx(p, "hatchFrame", "woodD", new[] { 3.05, 4.95, 2.55 }, new[] { 1.3, 1.5, 0.25 });
            Tscn.Cbx(p, "hatchDoor", "char", new[] { 3.05, 4.95, 2.64 }, new[] { 0.95, 1.15, 0.14 });
            Humanoid.Anchor(p, 12.0, 5.0);
        });

    private static Tscn.EmitResult BuildBackPoorD(string root)
        => Emit(PathDef.Props(root), "BackPoorD", r =>
        {
            var p = r;
            Tscn.Cy(p, "kiln", "stoneD", new[] { 0.0, 0.0, 0.0 }, 7.0, 8.0);
            Tscn.Cbx(p, "mouth", "glowFire", new[] { 0.0, 1.2, 3.3 }, new[] { 2.2, 2.2, 1.2 });
            Tscn.Bx(p, "chimney", "brick", new[] { 2.5, 5.8, 0.0 }, new[] { 1.2, 4.0, 1.2 });
            Tscn.Bx(p, "brush", "thatch", new[] { -4.6, 0.0, 1.0 }, new[] { 2.4, 1.6, 3.0 });
            Humanoid.Anchor(p, 10.6, 5.0);
        });

    //  沙城四件：土坯方坊 / 圆顶库房 / 小风口塔 / 集市帐篷。
    private static Tscn.EmitResult BuildBackSandA(string root)
        => Emit(PathDef.Props(root), "BackSandA", r =>
        {
            var p = r;
            Tscn.Cbx(p, "body", "sand", new[] { 0.0, 4.0, 0.0 }, new[] { 14.0, 8.0, 10.0 });
            foreach (var sx in new[] { -6.0, -2.0, 2.0, 6.0 })
                Tscn.Bx(p, "merlon", "sand", new[] { sx - 0.7, 8.0, 4.2 }, new[] { 1.4, 1.2, 1.4 });
            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 2.5, 4.7 }, new[] { 2.4, 5.0, 1.2 });
            Humanoid.Anchor(p, 10.2, 7.0);
        });

    private static Tscn.EmitResult BuildBackSandB(string root)
        => Emit(PathDef.Props(root), "BackSandB", r =>
        {
            var p = r;
            Tscn.Bx(p, "drum", "stoneL", new[] { 0.0, 0.0, 0.0 }, new[] { 12.0, 4.0, 12.0 });
            Tscn.Sp(p, "dome", "sand", new[] { 0.0, 4.0, 0.0 }, new[] { 12.0, 6.0, 12.0 });
            Tscn.Bx(p, "door", "woodD", new[] { -1.3, 0.0, 5.6 }, new[] { 2.6, 3.4, 1.0 });
            Humanoid.Anchor(p, 8.0, 6.0);
        });

    private static Tscn.EmitResult BuildBackSandC(string root)
        => Emit(PathDef.Props(root), "BackSandC", r =>
        {
            var p = r;
            Tscn.Bx(p, "shaft", "sand", new[] { 0.0, 0.0, 0.0 }, new[] { 8.0, 16.0, 8.0 });
            for (var i = 0; i < 3; i++)
                Tscn.Cbx(p, "slot", "woodD", new[] { 4.1, 5.5 + i * 4.0, 0.0 }, new[] { 0.5, 2.4, 5.0 });
            Tscn.Cbx(p, "cap", "woodD", new[] { 0.0, 16.4, 0.0 }, new[] { 9.0, 0.7, 9.0 });
            Tscn.Bx(p, "house", "sand", new[] { -5.6, 0.0, -2.4 }, new[] { 4.6, 5.0, 6.0 });
            Humanoid.Anchor(p, 18.0, 5.0);
        });

    private static Tscn.EmitResult BuildBackSandD(string root)
        => Emit(PathDef.Props(root), "BackSandD", r =>
        {
            var p = r;
            foreach (var sx in new[] { -4.6, 4.6 })
                foreach (var sz in new[] { -2.6, 2.6 })
                    Tscn.Bx(p, "pole", "woodD", new[] { sx - 0.3, 0.0, sz - 0.3 }, new[] { 0.6, 4.2, 0.6 });
            Tscn.Cbx(p, "awning", "canvas", new[] { 0.0, 4.4, 0.0 }, new[] { 11.0, 0.5, 7.5 });
            Tscn.Bx(p, "crateA", "woodL", new[] { -3.2, 0.0, 0.6 }, new[] { 1.6, 1.4, 1.6 });
            Tscn.Bx(p, "crateB", "woodL", new[] { 1.8, 0.0, -0.4 }, new[] { 1.4, 1.2, 1.4 });
            Tscn.Sp(p, "sack", "crop", new[] { 3.4, 0.5, 0.8 }, new[] { 1.6, 1.0, 1.6 });
            Humanoid.Anchor(p, 5.8, 7.0);
        });

    //  富城四件：石塔 / 宅邸 / 列柱厅 / 喷泉亭。
    //  票 06：单座大窗改三排窗（每排 3 扇，char 深色窗框 + 内凹 glowWarm 玻璃），
    //  檐口加一圈挑出的石线脚（stoneM 宽带压在塔顶，比塔身每边多出 0.5 米）。
    //  AABB 不增（11.50 × 11.87 米）：窗贴塔身正面收在 5.35 以里，线脚 11.0 米小于屋顶 11.5 米。
    private static Tscn.EmitResult BuildBackRichA(string root)
        => Emit(PathDef.Props(root), "BackRichA", r =>
        {
            var p = r;
            Tscn.Bx(p, "shaft", "stoneL", new[] { 0.0, 0.0, 0.0 }, new[] { 10.0, 18.0, 10.0 });
            for (var row = 0; row < 3; row++)
            {
                var y = 6.5 + row * 4.2;
                foreach (var x in new[] { -3.4, 0.0, 3.4 })
                {
                    // 四边线框围出窗洞，玻璃退在洞口里——实心框板会把玻璃整个盖住。
                    foreach (var s in new[] { -1.0, 1.0 })
                    {
                        Tscn.Cbx(p, "winJamb", "char", new[] { x + s * 0.85, y, 5.02 }, new[] { 0.3, 2.8, 0.45 });
                        Tscn.Cbx(p, "winRail", "char", new[] { x, y + s * 1.25, 5.02 }, new[] { 1.4, 0.3, 0.45 });
                    }

                    Tscn.Cbx(p, "winGlass", "glowWarm", new[] { x, y, 4.92 }, new[] { 1.4, 2.2, 0.30 });
                }
            }

            Tscn.Cbx(p, "cornice", "stoneM", new[] { 0.0, 17.90, 0.0 }, new[] { 11.0, 0.55, 11.0 });
            Gable(p, "roofBlue", 11.5, 11.5, 18.0, 4.6, 0.6);
            Tscn.Cy(p, "tip", "bronze", new[] { 0.0, 22.4, 0.0 }, 1.4, 1.8);
            Humanoid.Anchor(p, 25.0, 6.0);
        });

    private static Tscn.EmitResult BuildBackRichB(string root)
        => Emit(PathDef.Props(root), "BackRichB", r =>
        {
            var p = r;
            Tscn.Bx(p, "plinth", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 18.0, 1.2, 14.0 });
            Tscn.Bx(p, "wall", "stoneL", new[] { 0.0, 1.2, 0.0 }, new[] { 16.0, 8.0, 12.0 });
            for (var i = 0; i < 4; i++)
                Tscn.Cbx(p, "window", "glowWarm", new[] { -6.0 + i * 4.0, 5.0, 6.1 }, new[] { 1.6, 2.6, 0.4 });
            Gable(p, "roofBlue", 17.0, 13.0, 9.2, 3.6, 0.6);
            Humanoid.Anchor(p, 13.6, 9.0);
        });

    private static Tscn.EmitResult BuildBackRichC(string root)
        => Emit(PathDef.Props(root), "BackRichC", r =>
        {
            var p = r;
            Tscn.Bx(p, "stylobate", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 17.0, 1.5, 12.0 });
            foreach (var sx in new[] { -6.5, 6.5 })
                foreach (var sz in new[] { -4.5, 0.0, 4.5 })
                    Tscn.Cy(p, "column", "stoneL", new[] { sx, 1.5, sz }, 1.9, 7.0);
            Tscn.Cbx(p, "entablature", "stoneL", new[] { 0.0, 8.8, 0.0 }, new[] { 15.0, 1.2, 10.5 });
            Gable(p, "roofBlue", 14.0, 10.0, 9.4, 2.6, 0.5);
            Humanoid.Anchor(p, 12.8, 9.0);
        });

    private static Tscn.EmitResult BuildBackRichD(string root)
        => Emit(PathDef.Props(root), "BackRichD", r =>
        {
            var p = r;
            Tscn.Cy(p, "pool", "stoneM", new[] { 0.0, 0.0, 0.0 }, 10.0, 1.2);
            // 池是实心台，水面压在池沿上才读得出是一盘水
            Tscn.Plate(p, "water", "water", new[] { 0.0, 1.28, 0.0 }, 8.0, 0.3);
            foreach (var sx in new[] { -3.5, 3.5 })
                foreach (var sz in new[] { -3.5, 3.5 })
                    Tscn.Bx(p, "post", "stoneL", new[] { sx - 0.4, 0.0, sz - 0.4 }, new[] { 0.8, 5.0, 0.8 });
            Tscn.Cbx(p, "roof", "roofGreen", new[] { 0.0, 4.85, 0.0 }, new[] { 9.5, 0.5, 9.5 });
            Tscn.Sp(p, "lantern", "glowWarm", new[] { 0.0, 4.2, 0.0 }, new[] { 1.2, 1.2, 1.2 });
            Humanoid.Anchor(p, 7.0, 6.0);
        });

    // ---------------- 三套地表 ----------------

    //  地表件：一块薄台 + 地表/植被色块。与地标同系摆（表现层叠在地标底下），
    //  进深同样 96 米 —— 三件都靠色块一眼分开（泥地+杂草 / 沙地+砾石 / 石板+草坪+水渠）。

    //  票 06：加十字路网（ash 压实土色带贴台面）、两道田埂与三件杂物（柴箱 / 木桶 / 歪桩）。
    //  AABB 仍 100 × 96 米、厚度与锚点不动：路网收在 ±45 / ±44，全部薄贴在台面顶沿。
    private static Tscn.EmitResult BuildGroundPoor(string root)
        => Emit(PathDef.Props(root), "GroundPoor", r =>
        {
            var p = r;
            Tscn.Bx(p, "soil", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 100.0, 0.5, CityDepth });
            // 十字路网：一道横贯 X、一道纵贯 Z，窄条压在土台面上，俯视一眼辨路。
            Tscn.Cbx(p, "roadEW", "ash", new[] { 0.0, 0.57, -8.0 }, new[] { 90.0, 0.14, 3.2 });
            Tscn.Cbx(p, "roadNS", "ash", new[] { 14.0, 0.57, 0.0 }, new[] { 3.2, 0.14, 88.0 });
            for (var i = 0; i < 6; i++)
                Tscn.Cbx(p, "weedPatch", "crop", new[] { -42.0 + i * 16.0, 0.58, -30.0 + (i % 3) * 26.0 },
                    new[] { 9.0, 0.16, 6.0 });
            // 田埂：两道窄棱贴着田块边，读出「田是规整过的」。
            Tscn.Cbx(p, "ridgeA", "stoneD", new[] { -42.0, 0.61, -26.2 }, new[] { 9.6, 0.18, 0.5 });
            Tscn.Cbx(p, "ridgeB", "stoneD", new[] { -26.0, 0.61, 20.3 }, new[] { 0.5, 0.18, 6.6 });
            foreach (var (sx, sz) in new[] { (-20.0, 30.0), (34.0, -18.0), (-8.0, -38.0) })
                Tscn.Sp(p, "rock", "stoneD", new[] { sx, 0.6, sz }, new[] { 4.0, 1.4, 3.4 });
            Tscn.Bx(p, "strawA", "thatch", new[] { 12.0, 0.5, 26.0 }, new[] { 3.0, 1.2, 4.2 });
            Tscn.Bx(p, "strawB", "thatch", new[] { -34.0, 0.5, 4.0 }, new[] { 4.2, 1.2, 3.0 });
            // 杂物三件：路口的柴箱、路边的木桶、一根歪桩。
            Tscn.Bx(p, "crate", "woodL", new[] { 8.5, 0.5, -13.0 }, new[] { 1.6, 1.4, 1.6 });
            Tscn.Cy(p, "barrel", "woodD", new[] { 11.2, 0.5, -14.5 }, 1.4, 1.3);
            Tscn.Cbx(p, "stub", "char", new[] { -16.5, 0.9, -10.0 }, new[] { 0.4, 1.1, 0.4 }, Humanoid.O(rz: 14));
            Humanoid.Anchor(p, 2.0, 20.0);
        });

    //  票 06：加两座压扁的球作沙丘、一条砾石带（dirt 砂砾床压几块 stoneD 砾石）与一条纵贯的
    //  stoneM 踩踏路。AABB 仍 96 × 96 米、厚度与锚点不动：沙丘顶 1.8 米不碰名牌线。
    private static Tscn.EmitResult BuildGroundSand(string root)
        => Emit(PathDef.Props(root), "GroundSand", r =>
        {
            var p = r;
            Tscn.Bx(p, "dune", "sand", new[] { 0.0, 0.0, 0.0 }, new[] { 96.0, 0.4, CityDepth });
            // 沙丘：压扁的球，底面陷进沙台 0.6 米，坡脚自然衔接。
            Tscn.Sp(p, "duneCrestA", "sand", new[] { -18.0, 0.7, 24.0 }, new[] { 16.0, 2.2, 12.0 });
            Tscn.Sp(p, "duneCrestB", "sand", new[] { 20.0, 0.7, -30.0 }, new[] { 14.0, 2.0, 10.0 });
            // 砾石带：一条砂砾床斜过场西，上面压四块砾石。
            Tscn.Cbx(p, "gritBed", "dirt", new[] { 10.0, 0.55, -30.0 }, new[] { 30.0, 0.3, 4.0 },
                Humanoid.O(ry: 12));
            foreach (var (gx, gz) in new[] { (2.0, -30.5), (9.0, -29.0), (16.0, -31.0), (22.0, -29.5) })
                Tscn.Cbx(p, "gritStone", "stoneD", new[] { gx, 0.72, gz }, new[] { 1.4, 0.5, 1.0 });
            // 穿场道路：一条 stoneM 踩踏路纵贯南北。
            Tscn.Cbx(p, "roadNS", "stoneM", new[] { 2.0, 0.53, 0.0 }, new[] { 3.6, 0.36, 88.0 });
            foreach (var (sx, sz) in new[] { (-30.0, 26.0), (28.0, -20.0), (-14.0, -36.0), (38.0, 18.0) })
                Tscn.Sp(p, "grit", "dirt", new[] { sx, 0.5, sz }, new[] { 10.0, 0.8, 8.0 });
            foreach (var (sx, sz) in new[] { (-22.0, -12.0), (18.0, 34.0), (36.0, -6.0) })
                Tscn.Bx(p, "pebble", "stoneD", new[] { sx, 0.4, sz }, new[] { 2.2, 0.9, 1.8 });
            foreach (var (sx, sz) in new[] { (-6.0, 20.0), (26.0, 8.0), (-34.0, -26.0) })
                Tscn.Bx(p, "tuft", "leafD", new[] { sx, 0.4, sz }, new[] { 1.4, 1.0, 1.4 });
            Humanoid.Anchor(p, 2.0, 20.0);
        });

    //  票 06：加十字石板路网（stoneL 带贴 slab 台面）、两块大草坪，水渠边石改为沿渠通长两道。
    //  AABB 仍 100 × 96 米、厚度与锚点不动：路网与边石全部收在台面以内。
    private static Tscn.EmitResult BuildGroundRich(string root)
        => Emit(PathDef.Props(root), "GroundRich", r =>
        {
            var p = r;
            Tscn.Bx(p, "pave", "slab", new[] { 0.0, 0.0, 0.0 }, new[] { 100.0, 0.5, CityDepth });
            // 石板路网：一横一纵，stoneL 比 slab 台面亮一档，俯视读得出路网。
            Tscn.Cbx(p, "roadNS", "stoneL", new[] { -14.0, 0.56, -6.0 }, new[] { 4.0, 0.12, 76.0 });
            Tscn.Cbx(p, "roadEW", "stoneL", new[] { 4.0, 0.56, 6.0 }, new[] { 76.0, 0.12, 4.0 });
            for (var i = 0; i < 5; i++)
                Tscn.Cbx(p, "lawn", "leaf", new[] { -40.0 + i * 20.0, 0.58, -26.0 + (i % 3) * 22.0 },
                    new[] { 11.0, 0.16, 8.0 });
            // 两块大草坪贴着路网交叉口两侧。
            Tscn.Cbx(p, "lawnWideA", "leaf", new[] { -28.0, 0.58, 14.0 }, new[] { 16.0, 0.16, 10.0 });
            Tscn.Cbx(p, "lawnWideB", "leaf", new[] { 26.0, 0.58, -16.0 }, new[] { 16.0, 0.16, 10.0 });
            Tscn.Cbx(p, "canal", "water", new[] { 0.0, 0.5, 34.0 }, new[] { 72.0, 0.24, 3.0 });
            // 水渠边石：沿渠两道通长石棱，比渠沿高半档。
            Tscn.Cbx(p, "kerbN", "stoneL", new[] { 0.0, 0.75, 32.1 }, new[] { 72.0, 0.5, 0.8 });
            Tscn.Cbx(p, "kerbS", "stoneL", new[] { 0.0, 0.75, 35.9 }, new[] { 72.0, 0.5, 0.8 });
            Humanoid.Anchor(p, 2.0, 20.0);
        });

    // ---------------- 共用 ----------------

    /// <summary>
    /// 三件地标共用的三个接点锚（Marker3D，本地系，城原点 = 城中心）：
    /// 城门沿 +Z 面（本地 z=48），码头与道路沿外法线方向往航线一侧接。
    /// 世界坐标 = 城中心（`MapPlace.CityCenter`）按外法线换算，对照表见 `docs/prototypes/beast-arena/`。
    /// </summary>
    private static void CityAnchors(string p)
    {
        Marker(p, "GateAnchor", 0.0, 0.3, 48.0);
        Marker(p, "DockAnchor", 0.0, 0.5, 52.0);
        Marker(p, "RoadAnchor", 0.0, 0.2, 56.0);
    }

    private static void Marker(string parent, string name, double x, double y, double z)
        => Tscn.Node(name, parent, "Marker3D",
            new System.Collections.Generic.List<(string, string)>
            {
                ("transform", Tscn.Tf(Tscn.I3, new[] { x, y, z })),
            });
}
