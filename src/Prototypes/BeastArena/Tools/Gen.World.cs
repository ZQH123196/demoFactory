#nullable enable
// 巨兽竞技场 造型源场景生成器 —— 世界域：生态对象、魔晶、远方高塔与两层地下模块（T14）。
//
// 本文件是 Gen 的部分实现（partial），按域拆分见 Gen.cs 文件头那一段。
// 产物全部是静态件（不挂骨架、不带动作），源场景照旧落 Tools/Rigs/Props/ ——
// Check.cs 的 FootprintPattern 认这三个子目录，摆进第四个子目录会漏掉占地判定。
//
// 摆位与逻辑层的关系（一件都不改规则，只是给规则里已有的数据配剪影）：
//   · 生态三类的坐标读 `BigMapGrid.EcoNodeSpecs`（树与兽群在草地、矿石在沙漠带，摆位已定）；
//   · 地下两层 × 四象限的格状态读 `UndergroundSoil`，象限边长 = `ArenaTuning.SoilSectorSpanMeters`、
//     单层深 = `ArenaTuning.SoilLayerDepthMeters`；表现层只按读数切显隐（「表现层只读」）。
//   · 地下模块摆在浮山腰侧（镇地面以下），洞口、采矿工作面、嵌入构件、地面出口
//     沿同一格串成一串，规格「地下」要的「清楚连接」在这一串上表达。
//
// 尺寸口径（体量定档 2026-10-15 之后的大地图一档）：
//   城 108×96 米 / 高 26.1，食物点 36×32 米 / 高 11.4 ——
//   树 9 米、矿石 4.8 米、兽群单体 2.6 米：比城与食物点矮一档，一眼读得出「野外的东西」而不是「又一处建筑」。
//   远方高塔 43 米：立在地平线那一圈，比城高、比云的留白低，出界视角里它是「陆地还有多远」的参照物。

using System;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    // ---------------- 生态对象（草地两树、草地兽群、沙漠矿石） ----------------

    //  针叶树（两种树之一，草地）：直干 + 三层锥冠，总高 8.2 米。
    //  三层锥逐层收径、层层压住下一层的底 —— 校验器按 AABB 判连通，压住即连成一块。
    private static Tscn.EmitResult BuildTreePine(string root)
    {
        return Emit(PathDef.Props(root), "TreePine", r =>
        {
            var p = r;
            Tscn.Cy(p, "trunk", "woodD", new[] { 0.0, 0.0, 0.0 }, 0.90, 2.40);
            Tscn.Cn(p, "skirt", "leafD", new[] { 0.0, 1.80, 0.0 }, 5.20, 3.40);
            Tscn.Cn(p, "mid", "leaf", new[] { 0.0, 3.60, 0.0 }, 4.00, 3.20);
            Tscn.Cn(p, "crown", "leaf", new[] { 0.0, 5.40, 0.0 }, 2.60, 2.80);

            Humanoid.Anchor(p, 9.20, 2.00);
        });
    }

    //  阔叶树（两种树之二，草地）：粗干 + 一大一小两团冠，总高 6.4 米。
    //  与针叶树的区别做在剪影本身（团状 vs 塔状），不在表现层换皮（「造型进模型」）。
    private static Tscn.EmitResult BuildTreeBroadleaf(string root)
    {
        return Emit(PathDef.Props(root), "TreeBroadleaf", r =>
        {
            var p = r;
            Tscn.Cy(p, "trunk", "woodL", new[] { 0.0, 0.0, 0.0 }, 1.10, 3.20);
            Tscn.Bx(p, "branch", "woodL", new[] { 0.35, 2.30, 0.0 }, new[] { 1.10, 0.28, 0.28 });
            Tscn.Sp(p, "canopyA", "leaf", new[] { 0.0, 4.60, 0.0 }, new[] { 4.80, 3.40, 4.80 });
            Tscn.Sp(p, "canopyB", "leafD", new[] { 1.30, 4.10, -0.90 }, new[] { 3.00, 2.20, 3.00 });

            Humanoid.Anchor(p, 7.40, 2.00);
        });
    }

    //  矿石点（一种矿石，沙漠带）：砂岩底座 + 岩体带棱面 + 两根岩刺 + 三道青色细脉，总高 4.2 米。
    //  票 06（可读性清单「矿点有脉」）：岩体主体走 stoneM 把暗部抬到可辨，斜置的 stoneD / stoneL
    //  棱面读出「岩体有面」；矿脉从 bronze 改 glowCyan 细脉——青色一眼读成「这块石头里有东西」。
    //  AABB 仍 5.20 × 4.20 米：底座给满占地，棱面与矿脉全部收在岩体轮廓内。
    private static Tscn.EmitResult BuildOreNode(string root)
    {
        return Emit(PathDef.Props(root), "OreNode", r =>
        {
            var p = r;
            Tscn.Bx(p, "shelf", "sand", new[] { 0.0, 0.0, 0.0 }, new[] { 5.20, 0.50, 4.20 });
            Tscn.Cbx(p, "rock", "stoneM", new[] { 0.0, 1.30, 0.0 }, new[] { 4.20, 1.60, 3.40 });
            Tscn.Cn(p, "spireA", "stoneM", new[] { -0.90, 1.80, 0.30 }, 1.70, 2.40);
            Tscn.Cn(p, "spireB", "stoneM", new[] { 1.10, 1.70, -0.50 }, 1.30, 2.00);
            // 棱面：两块斜置的岩面压在岩体顶沿上，一眼读得出切面而不是圆疙瘩。
            Tscn.Cbx(p, "facetA", "stoneD", new[] { 1.30, 2.20, 0.90 }, new[] { 1.50, 0.70, 1.20 },
                Humanoid.O(rx: 14, ry: 18));
            Tscn.Cbx(p, "facetB", "stoneL", new[] { -1.45, 2.05, -0.85 }, new[] { 1.20, 0.60, 1.30 },
                Humanoid.O(rx: -12, ry: 22));
            // 青色细脉：两道贴在岩体正面与侧面，一道走棱面之间。
            Tscn.Cbx(p, "veinA", "glowCyan", new[] { 0.30, 1.30, 1.62 }, new[] { 2.00, 0.22, 0.26 });
            Tscn.Cbx(p, "veinB", "glowCyan", new[] { -1.20, 1.75, 0.80 }, new[] { 0.24, 1.30, 0.24 });
            Tscn.Cbx(p, "veinC", "glowCyan", new[] { 1.10, 2.45, 0.90 }, new[] { 1.10, 0.20, 0.20 });

            Humanoid.Anchor(p, 4.80, 2.40);
        });
    }

    //  兽群里的单体（一种兽类，草地）：椭身四腿带角，肩高 2.0 米。
    //  兽群点位在表现层摆三只（`ArenaTuning.HerdSilhouetteCount`），这件是其中一只的剪影。
    //  票 06：四条腿改错步站姿——前左在前、前右在后，后腿反相，读出一站一动的重心；
    //  腿根仍压进躯干（腿顶 1.10 落在椭身 0.80..1.90 里），仍无骨、无动画。
    //  AABB 不增：前后极值仍由头（+1.875）与尾（-1.525）给出，错步只挪腿不扩轮廓。
    private static Tscn.EmitResult BuildHerdBeast(string root)
    {
        return Emit(PathDef.Props(root), "HerdBeast", r =>
        {
            var p = r;
            Tscn.Sp(p, "body", "hide", new[] { 0.0, 1.35, 0.0 }, new[] { 1.40, 1.10, 2.30 });
            Tscn.Cbx(p, "neck", "hide", new[] { 0.0, 1.80, 1.05 }, new[] { 0.50, 0.70, 0.60 });
            Tscn.Sp(p, "head", "hideD", new[] { 0.0, 2.05, 1.50 }, new[] { 0.55, 0.50, 0.75 });
            foreach (var s in new[] { -1.0, 1.0 })
            {
                Tscn.Cn(p, "horn", "bone", new[] { s * 0.24, 2.25, 1.46 }, 0.16, 0.55);
                Tscn.Cbx(p, "ear", "hideD", new[] { s * 0.34, 2.18, 1.26 }, new[] { 0.10, 0.30, 0.34 });
            }

            // 错步：前左 / 后右踩实靠前，前右 / 后左让到身后——对角同相，前后腿反相。
            foreach (var (sx, sz) in new[] { (-0.45, 0.92), (0.45, 0.72), (-0.45, -0.72), (0.45, -0.92) })
                Tscn.Cbx(p, "leg", "hideD", new[] { sx, 0.55, sz }, new[] { 0.26, 1.10, 0.26 });
            Tscn.Cbx(p, "tail", "hideD", new[] { 0.0, 1.45, -1.30 }, new[] { 0.18, 0.50, 0.45 });

            Humanoid.Anchor(p, 3.20, 1.60);
        });
    }

    //  魔晶（一种魔晶）：石座 + 三支青色晶锥，总高 3.1 米。
    //  青色只在这里少量发光（画面基准「青色只用于少量魔法发光」）—— 发光只给晶锥本身，
    //  石座用哑石。这一件只进地下嵌入构件的环心（嵌入态）；搬运小件那一档走 T11 的 `Crystal`。
    private static Tscn.EmitResult BuildCrystalShard(string root)
    {
        return Emit(PathDef.Props(root), "CrystalShard", r =>
        {
            var p = r;
            Tscn.Cy(p, "socket", "stoneD", new[] { 0.0, 0.0, 0.0 }, 2.20, 0.50);
            Tscn.Cn(p, "main", "glowCyan", new[] { 0.0, 0.45, 0.0 }, 0.90, 2.60);
            Tscn.Cn(p, "sideA", "glowCyan", new[] { 0.55, 0.45, 0.35 }, 0.60, 1.70);
            Tscn.Cn(p, "sideB", "glowCyan", new[] { -0.50, 0.45, -0.30 }, 0.50, 1.30);

            Humanoid.Anchor(p, 3.60, 1.60);
        });
    }

    // ---------------- 大地图远景 ----------------

    //  远方高塔：方基 + 石柱 + 两道铁箍 + 蓝灰陡顶 + 塔顶信标与旗，总高 43 米。
    //  它回答的是出界视角里「陆地的边在哪」—— 站得比城高、占地只 9×9 米，
    //  一眼是「远处的地标」而不是「又一栋建筑」。代价：三座摆在地平圈，战略档里只有轮廓。
    private static Tscn.EmitResult BuildDistantTower(string root)
    {
        return Emit(PathDef.Props(root), "DistantTower", r =>
        {
            var p = r;
            Tscn.Bx(p, "base", "stoneD", new[] { 0.0, 0.0, 0.0 }, new[] { 9.00, 3.00, 9.00 });
            Tscn.Cy(p, "shaft", "stoneL", new[] { 0.0, 3.00, 0.0 }, 5.00, 30.00);
            Tscn.Cy(p, "bandA", "iron", new[] { 0.0, 12.00, 0.0 }, 5.30, 0.60);
            Tscn.Cy(p, "bandB", "iron", new[] { 0.0, 26.00, 0.0 }, 5.30, 0.60);
            Tscn.Cy(p, "gallery", "stoneM", new[] { 0.0, 33.00, 0.0 }, 7.00, 1.60);
            Tscn.Cn(p, "roof", "roofBlue", new[] { 0.0, 34.60, 0.0 }, 7.40, 4.50);
            Tscn.Cy(p, "beacon", "glowGold", new[] { 0.0, 39.10, 0.0 }, 2.20, 1.40);
            Tscn.Cy(p, "pole", "woodD", new[] { 0.0, 40.50, 0.0 }, 0.30, 2.00);
            Tscn.Cbx(p, "banner", "roofRed", new[] { 0.90, 41.40, 0.0 }, new[] { 1.60, 0.90, 0.10 });

            Humanoid.Anchor(p, 44.50, 2.40);
        });
    }

    // ---------------- 两层地下模块（地块 / 洞口 / 采矿工作面 / 嵌入构件 / 地面出口） ----------------
    //
    //  这一组是「一块格」的五种说法，边长统一 12 米、层高统一 3 米（`SoilLayerDepthMeters`）：
    //   · SoilBlock —— 没挖开的实心地块（未开挖态）；
    //   · CavernTile —— 挖开的空腔格（开挖态：地面、顶板、两侧壁与支护齐全，中间走人）；
    //   · CaveMouth —— 洞口（洞穴格挖开那一刻亮出来，洞内一抹青光）；
    //   · MiningFace —— 采矿工作面（岩壁 + 矿脉 + 支护 + 碎石堆 + 一把镐）；
    //   · CrystalSocket —— 魔晶嵌入构件（底座 + 三爪 + 青光环；嵌入态由表现层往环心里放 CrystalShard）；
    //   · GroundExit —— 地面出口（6.5 米一节竖井，可串多节接回镇地面）。
    //  模块可组合：同一格按读数在 SoilBlock / CavernTile 之间切显隐，洞口、工作面、嵌入件各按
    //  Dug / Cleared / Embedded 亮灭 —— 规格「地下」的三态在这一组件上摆得出，不写进规则。

    //  实心地块：土体一整块，内嵌两处岩斑（断面读得出「这是没挖的土」）。
    private static Tscn.EmitResult BuildSoilBlock(string root)
    {
        return Emit(PathDef.Props(root), "SoilBlock", r =>
        {
            var p = r;
            Tscn.Bx(p, "body", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 12.00, 3.00, 12.00 });
            Tscn.Cbx(p, "rockA", "stoneD", new[] { 2.50, 1.20, 1.50 }, new[] { 1.60, 0.90, 1.20 });
            Tscn.Cbx(p, "rockB", "stoneD", new[] { -2.20, 1.80, -2.60 }, new[] { 1.20, 0.80, 1.40 });

            Humanoid.Anchor(p, 3.80, 2.00);
        });
    }

    //  空腔格：地板 + 顶板 + 四壁（留前后开口）+ 两道木支护。
    //  前后壁故意只留半高矮墙 —— 人要从这一格走到下一格，全封死就连不成串。
    //  票 06（可读性清单「腔体格看得见腔体」）：顶板从前缘缩进 3.6 米，前脸从外面直接看进腔内；
    //  腔内加一面深色内壁（stoneD 压在底沿与后矮墙之间），断面不再读成实心方块。
    //  AABB 仍 12.00 × 12.00 米：地板、侧壁、矮墙没动，缩的是顶板、加的是腔内件。
    private static Tscn.EmitResult BuildCavernTile(string root)
    {
        return Emit(PathDef.Props(root), "CavernTile", r =>
        {
            var p = r;
            Tscn.Bx(p, "floor", "dirt", new[] { 0.0, 0.0, 0.0 }, new[] { 12.00, 0.50, 12.00 });
            Tscn.Bx(p, "ceiling", "stoneD", new[] { 0.0, 2.50, -1.80 }, new[] { 12.00, 0.50, 8.40 });
            foreach (var s in new[] { -1.0, 1.0 })
            {
                Tscn.Bx(p, "sideWall", "stoneM", new[] { s * 5.60, 0.50, 0.0 }, new[] { 0.80, 2.00, 12.00 });
                Tscn.Bx(p, "curb", "stoneM", new[] { 0.0, 0.50, s * 5.60 }, new[] { 12.00, 0.90, 0.80 });
            }

            Tscn.Bx(p, "backWall", "stoneD", new[] { 0.0, 0.50, -4.90 }, new[] { 10.40, 2.00, 0.70 });

            foreach (var sx in new[] { -3.50, 3.50 })
                Tscn.Cy(p, "post", "woodD", new[] { sx, 0.50, 0.0 }, 0.50, 2.00);
            Tscn.Cbx(p, "lintel", "woodL", new[] { 0.0, 2.30, 0.0 }, new[] { 7.80, 0.40, 0.50 });

            Humanoid.Anchor(p, 3.80, 2.00);
        });
    }

    //  洞口：岩丘 + 石拱 + 近黑洞内 + 洞口上沿一条青光 + 前缘碎石。摆在洞穴格朝向通道的那一面。
    //  票 06（可读性清单「洞口有暗口」）：喉口从 cloth 改 char——「黑」是看得见的负形，一眼读成「能进」；
    //  开口围五块深色拱石（左右门柱 + 顶部三块拱心石），洞口轮廓由拱石钉出来；
    //  青光只留 rimGlow 那一条上沿，不再往里加发光面。
    //  AABB 仍 10.00 × 8.90 米：拱石前沿 4.50，前后极值仍由岩丘（-4.0）与碎石（+4.9）给出。
    private static Tscn.EmitResult BuildCaveMouth(string root)
    {
        return Emit(PathDef.Props(root), "CaveMouth", r =>
        {
            var p = r;
            Tscn.Cbx(p, "mound", "stoneD", new[] { 0.0, 1.60, 0.0 }, new[] { 10.00, 3.20, 8.00 });
            Tscn.Cbx(p, "lintel", "stoneM", new[] { 0.0, 3.60, 1.60 }, new[] { 6.00, 1.20, 4.00 });
            // 喉口与拱石凸出岩丘前脸（z=4.0）0.3~0.5 米——埋进岩丘就读不出「洞」。
            Tscn.Cbx(p, "throat", "char", new[] { 0.0, 1.50, 3.95 }, new[] { 3.40, 3.00, 0.80 });
            Tscn.Cbx(p, "rimGlow", "glowCyan", new[] { 0.0, 3.05, 4.05 }, new[] { 3.80, 0.12, 0.60 });
            // 石拱：五块 stoneM 拱石围住喉口一圈，压在岩丘前脸外沿。
            foreach (var s in new[] { -1.0, 1.0 })
                Tscn.Cbx(p, "archJamb", "stoneM", new[] { s * 2.10, 1.40, 4.05 }, new[] { 0.80, 2.80, 0.70 });
            Tscn.Cbx(p, "archKeyW", "stoneM", new[] { -1.10, 3.30, 4.05 }, new[] { 1.60, 0.80, 0.70 });
            Tscn.Cbx(p, "archKey", "stoneM", new[] { 0.0, 3.35, 4.10 }, new[] { 1.40, 0.70, 0.80 });
            Tscn.Cbx(p, "archKeyE", "stoneM", new[] { 1.10, 3.30, 4.05 }, new[] { 1.60, 0.80, 0.70 });
            Tscn.Cbx(p, "rubbleA", "stoneM", new[] { 1.60, 0.40, 4.30 }, new[] { 1.40, 0.80, 1.20 });
            Tscn.Cbx(p, "rubbleB", "stoneM", new[] { -1.90, 0.35, 4.10 }, new[] { 1.10, 0.70, 1.00 });

            Humanoid.Anchor(p, 5.20, 2.00);
        });
    }

    //  采矿工作面：岩壁 + 内凹开采面 + 凹面上的两道矿脉 + 木支护 + 碎石堆 + 一把插着的镐。
    //  摆在挖开格靠里那一面。
    //  票 06：两根支柱之间加一块内凹面（stoneD，前沿比岩壁面退 0.3 米），矿脉从外凸改贴在凹面上——
    //  一眼读成「这里被挖过」；框、支护、碎石堆保留原样。
    //  AABB 仍 8.00 × 5.30 米：凹面与矿脉全部收在岩壁厚度以内，前后极值仍由岩壁与镐头给出。
    private static Tscn.EmitResult BuildMiningFace(string root)
    {
        return Emit(PathDef.Props(root), "MiningFace", r =>
        {
            var p = r;
            Tscn.Bx(p, "wall", "stoneM", new[] { 0.0, 0.0, 0.0 }, new[] { 8.00, 6.00, 2.40 });
            Tscn.Cbx(p, "seam", "stoneD", new[] { 0.0, 3.80, 0.10 }, new[] { 7.60, 0.80, 1.60 });
            // 内凹面：夹在两柱之间，前沿 z=0.90，比岩壁面（z=1.20）退 0.3 米。
            Tscn.Cbx(p, "recess", "stoneD", new[] { 0.0, 2.60, 0.75 }, new[] { 3.60, 3.20, 0.30 });
            Tscn.Cbx(p, "veinA", "bronze", new[] { -1.30, 1.90, 0.95 }, new[] { 1.60, 0.45, 0.24 });
            Tscn.Cbx(p, "veinB", "gold", new[] { 1.20, 3.40, 0.95 }, new[] { 1.20, 0.40, 0.24 });
            foreach (var sx in new[] { -2.20, 2.20 })
                Tscn.Cy(p, "post", "woodD", new[] { sx, 0.0, 1.30 }, 0.40, 4.40);
            Tscn.Cbx(p, "lintel", "woodL", new[] { 0.0, 4.60, 1.30 }, new[] { 5.20, 0.40, 0.60 });
            Tscn.Cbx(p, "spill", "dirt", new[] { 0.0, 0.50, 1.90 }, new[] { 4.50, 1.00, 1.40 });
            Tscn.Cbx(p, "pickHandle", "woodD", new[] { 1.80, 0.35, 3.00 }, new[] { 0.12, 0.12, 1.50 });
            Tscn.Cbx(p, "pickHead", "iron", new[] { 1.80, 0.35, 3.65 }, new[] { 0.14, 0.50, 0.90 });

            Humanoid.Anchor(p, 6.80, 2.40);
        });
    }

    //  嵌入构件：石座 + 一圈青光环 + 三只铁爪。环心就是 CrystalShard 该落的位 ——
    //  「模块可组合」在这一件上最直接：嵌入态 = 本件亮 + 魔晶件摆进环心，未嵌 = 魔晶件灭。
    private static Tscn.EmitResult BuildCrystalSocket(string root)
    {
        return Emit(PathDef.Props(root), "CrystalSocket", r =>
        {
            var p = r;
            Tscn.Cy(p, "plinth", "stoneM", new[] { 0.0, 0.0, 0.0 }, 4.00, 0.80);
            Tscn.Cy(p, "stem", "stoneD", new[] { 0.0, 0.80, 0.0 }, 2.60, 0.50);
            Tscn.Ring(p, "ring", "glowCyan", new[] { 0.0, 1.32, 0.0 }, 2.60, 0.10);
            foreach (var a in new[] { 90.0, 210.0, 330.0 })
            {
                var rad = a * (Math.PI / 180.0);
                Tscn.Cbx(p, "prong", "iron",
                    new[] { Math.Cos(rad) * 1.35, 1.90, Math.Sin(rad) * 1.35 }, new[] { 0.50, 1.40, 0.50 });
            }

            Humanoid.Anchor(p, 3.40, 1.80);
        });
    }

    //  地面出口：地面石圈 + 深色井口 + 四柱 + 逐档横档 + 顶盖，一节 6.5 米。
    //  多节首尾相接即接回镇地面 —— 「地下 ↔ 地面」那条连通在这一件上用重复摆放表达，
    //  不另造一件更高模型（层高 3 米 × 2 层的井口口径不变）。
    //  票 06：底部一圈 stoneM 地面石圈把井口钉在地上；梯子（-Z 面）正下方一块 char 深色井口，
    //  顺着梯子往下看就知道「再往下是洞」。四柱与横档保留。
    //  AABB 仍 2.60 × 2.60 米：石圈直径 2.60 就是这件的轮廓，井口收在石圈内沿。
    private static Tscn.EmitResult BuildGroundExit(string root)
    {
        return Emit(PathDef.Props(root), "GroundExit", r =>
        {
            var p = r;
            Tscn.Plate(p, "kerbRing", "stoneM", new[] { 0.0, 0.18, 0.0 }, 2.60, 0.36);
            Tscn.Cbx(p, "throat", "char", new[] { 0.0, 0.20, -0.75 }, new[] { 1.60, 0.40, 0.90 });
            foreach (var sx in new[] { -1.10, 1.10 })
                foreach (var sz in new[] { -1.10, 1.10 })
                    Tscn.Cy(p, "post", "woodD", new[] { sx, 0.0, sz }, 0.35, 6.00);
            for (var i = 0; i < 7; i++)
                Tscn.Cbx(p, "rung", "woodL", new[] { 0.0, 0.40 + i * 0.75, -1.10 }, new[] { 2.30, 0.14, 0.20 });
            Tscn.Cy(p, "cap", "stoneM", new[] { 0.0, 6.00, 0.0 }, 2.60, 0.50);

            Humanoid.Anchor(p, 7.20, 1.60);
        });
    }
}
