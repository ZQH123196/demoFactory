// 巨兽竞技场 造型源场景生成器 —— 产出 Tools/Rigs 下的 .tscn（相当于美术的 .blend，不进游戏）。
//
// 分工：通用 .tscn 序列化与图元 DSL 在 src/Tools/Tscn.cs；人形骨架与四足骨架画笔在
// src/Tools/HumanoidRig.cs 与 src/Tools/QuadrupedRig.cs。本文件与它的三个 partial 同伴
// 只有「谁长什么样」：配色表、各一个 Build*()、Emit() 薄封装与共享形状助手。
//
// **按域拆分（T09，2026-10 垂直切片）**：Gen 是 partial 类，Build*() 按域分文件——
//   · Gen.Units.cs  巨兽三形态 + 六件地面人形（会动的都在这）
//   · Gen.Town.cs   十三件建筑（含竞技场与工坊）+ 城齿助手
//   · Gen.Deck.cs   磁悬浮底座与镇地面（按成品米给尺寸的两件）
//   · Gen.Props.cs  静态场景件与点位物（浮桥 / 空艇 / 尸体 / 货物 / 城 / 食物点）
// 本文件只留四域共用的那一层：配色表 Mat、登记表 PathDef 与 Fits、Emit() 薄封装、
// 跨域形状助手 Gable()、构建入口 Run()。拆分的硬判据：拆分后重新 gen + export，
// 全部 .glb 与拆分前 SHA256 逐字节相同（证据落 .cache/beast-arena-vs/T09/）。
//
// 约定：图元底面/中心锚定于本地空间，脚在 y=0，面朝 +Z；j_ 前缀 = 关节契约；
//       Marker3D "Anchor" 的 .x = 头顶血条宽、.y = 名牌高度（表现层读它，不硬编码）。
//       产物都用 src/GugaFramework/Tools/glb/export_glb.gd -- --proto=BeastArena 编译成
//       Models/<Name>.glb；源场景不进游戏运行时。
//
// 造型分工（src/Prototypes/BeastArena/Models/README.md 同款口径）：
//   · 巨兽走四足骨架（Quadruped.Rig，骨名带 m_ 前缀），动作走 glb/clips_beast.gd；
//   · 六件地面人形（员工 / 角斗士 / 镇民 / 帮手 / 怪物 / 钻地怪）走同一副人形骨架 Humanoid.Rig，
//     动作走 glb/clips_humanoid.gd 的 idle / walk / run / attack_melee / death；
//   · 背上小镇的件全是静态资产：不挂骨架、不带动作，尺寸即最终尺寸。
//   · 背上小镇**不烘进 Beast.glb**：Beast 那个文件里只有巨兽自己的身体，
//     镇子那些件各自独立 —— 否则占地上限与比例尺同框两栏都会失真。
//
// 占地上限 120 米同时喂 Check.cs 与 BeastArenaTools.cs 的体检单契约，改一处要改两处。
// 巨兽实测体宽 52.9 米、含吻 89.58 米；尾是短尾桩，撑着这一条；
// 十三件建筑另按网格档位各自更严（4×4 格 × 2 米 = 16×16 米，见下面那一段）。

// 体量定档（2026-10-15）—— 锚是**人 1.7 米**，其余按它定。
//
// 为什么重新定：原来那一档是**从桌子往人身上凑**的 —— 十三件建筑与格长都按「24 格甲板摆得下」来定，
// 于是宿舍 1.05 米、谷仓 1.34 米，**比人还矮**；甲板 24×24 米塞 50 个人 13 栋楼。
// 这一档反过来：**人不动，其余放大**。人形五件（员工 / 角斗士 / 镇民 / 帮手 / 怪物）
// 就是那把尺子本身，一个数都不改。
//
// 四档放大，与逻辑层同源（`ArenaTuning.DeckCellMeters` 1.0 → 2.0、`ArenaTuning.DeckCells` 仍是 24，
// 于是甲板从 24×24 米变成 48×48 米；格数不动，逻辑坐标一格没动）：
//   · 十三件建筑 ×2 —— 宿舍 2.10→3.15 米（另给一档竖向），谷仓 2.68 米，镇政府 6.64 米。
//   · 磁悬浮底座 ×2 但**高度不动** —— 那道缝（EmitterTop 4.20 / PlateBottom 5.50）是整件造型的意思，
//     竖向放大会把缝填掉，所以竖向那一档给 1.0。
//   · 巨兽 ×2 —— 原来 26.5×45.0 米驮一块 48 米见方的镇子，驮不动；放大后 52.9×89.58 米。
//   · 大地图上的城 ×3、食物点 ×2 —— 城要明显大于兽背那个镇（48×48 米）才读得出是「城」。
//
// 按 `Tscn.EmitScene` 的 fit 走：fit 盖住图元中心与尺寸、关节枢轴与头顶锚点，
// 烘进 .tscn 再烘进 .glb —— 是造型期缩放，不是运行期缩放（「造型进模型」）。
// 几档各是什么、在哪张表上分派，见类里 `Fits` 那一处。


#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    // ---------------- 配色表（材质库） ----------------
    // 六类口径（规格「画面基准」：材质区分砖、石板、木材、岩石、兽皮、旧铜）
    // 与「材质名 → 用途」全表见 docs/prototypes/beast-arena/asset-pipeline.md。
    // 加新条目只加 key：一个 key 只有被某个 Build*() 引用才会写进 .tscn 与 .glb，
    // 没被引用的条目不改任何现有模型字节。
    private static readonly Dictionary<string, Tscn.MatSpec> Mat = new()
    {
        // 通用建材
        ["stoneL"] = new(new[] { 0.66, 0.63, 0.57 }),
        ["stoneM"] = new(new[] { 0.45, 0.43, 0.41 }),
        ["stoneD"] = new(new[] { 0.25, 0.24, 0.27 }),
        ["woodL"] = new(new[] { 0.50, 0.34, 0.20 }),
        ["woodD"] = new(new[] { 0.29, 0.19, 0.12 }),
        ["deckWood"] = new(new[] { 0.40, 0.30, 0.19 }),
        ["thatch"] = new(new[] { 0.68, 0.55, 0.28 }),
        ["roofRed"] = new(new[] { 0.55, 0.20, 0.17 }),
        ["roofGreen"] = new(new[] { 0.21, 0.44, 0.30 }),
        ["roofBlue"] = new(new[] { 0.19, 0.31, 0.55 }),
        ["iron"] = new(new[] { 0.39, 0.41, 0.46 }, Metallic: 0.85, Rough: 0.38),
        ["steel"] = new(new[] { 0.72, 0.75, 0.82 }, Metallic: 0.9, Rough: 0.24),
        ["gold"] = new(new[] { 0.86, 0.68, 0.20 }, Metallic: 0.7, Rough: 0.30),
        ["bone"] = new(new[] { 0.83, 0.80, 0.70 }),
        ["cloth"] = new(new[] { 0.15, 0.15, 0.19 }),
        ["skin"] = new(new[] { 0.72, 0.56, 0.44 }),
        ["dirt"] = new(new[] { 0.34, 0.29, 0.22 }),
        ["sand"] = new(new[] { 0.72, 0.64, 0.46 }),
        ["leaf"] = new(new[] { 0.20, 0.36, 0.20 }),
        ["leafD"] = new(new[] { 0.14, 0.27, 0.15 }),
        ["crop"] = new(new[] { 0.55, 0.60, 0.22 }),
        ["water"] = new(new[] { 0.24, 0.40, 0.48 }),
        // 巨兽：厚皮 + 深色鬃尾 + 铁蹄
        ["hide"] = new(new[] { 0.40, 0.34, 0.29 }),
        ["hideD"] = new(new[] { 0.28, 0.23, 0.19 }),
        ["horn"] = new(new[] { 0.58, 0.53, 0.44 }),
        ["hoof"] = new(new[] { 0.13, 0.11, 0.10 }),
        // 巨兽三形态各带一套皮色（2026-10-20）：沙色一档 + 深色一档，各配一档深色（吻与眼用）。
        // **不拿现役的 hide / hideD 顶替** —— 三件同框时颜色是唯一分得开它们的东西。
        ["hideSand"] = new(new[] { 0.74, 0.62, 0.44 }),
        ["hideSandD"] = new(new[] { 0.50, 0.40, 0.27 }),
        ["hideDark"] = new(new[] { 0.27, 0.23, 0.21 }),
        ["hideDarkD"] = new(new[] { 0.16, 0.13, 0.12 }),
        // 镇民：粗布衣 + 皮围裙 + 麻色头巾
        ["tunic"] = new(new[] { 0.53, 0.47, 0.36 }),
        ["leather"] = new(new[] { 0.36, 0.24, 0.15 }),
        ["leatherD"] = new(new[] { 0.22, 0.15, 0.10 }),
        // 发光的：竞技场的火盆、工坊的炉口、市集的灯
        // 2026-10-29 游戏内实拍复核：窗灯与火盆在日光下过曝成纯白，色相读不出。
        // albedo 与 Em 一并压深，保留橙 / 暖两档色相；Tscn 的 2.2 能量倍率不动。
        ["glowGold"] = new(new[] { 1, 0.85, 0.25 }, Em: new[] { 1, 0.85, 0.2 }),
        ["glowFire"] = new(new[] { 0.82, 0.34, 0.07 }, Em: new[] { 0.50, 0.16, 0.03 }),
        ["glowWarm"] = new(new[] { 0.78, 0.52, 0.20 }, Em: new[] { 0.42, 0.24, 0.06 }),
        // 暗一档的金：给那些**又长又直**的发光条用。2026-10-01 出图复核撞到的 ——
        // 悬浮底座那圈边条用 glowGold，在 1.6 能量方向光下整条过曝成白板，
        // 而「悬浮」那层意思恰恰是这道细缝上的光，过曝就没了。短的发射头与场发射面仍用亮档。
        ["glowDim"] = new(new[] { 0.62, 0.47, 0.16 }, Em: new[] { 0.34, 0.25, 0.07 }),
        // 琥珀色发光（2026-10-15 底座改圆形那一轮加的）：给**朝上的大平面**用。
        // glowGold 是给火盆、窗子那些**小的立面**的：那种面只吃到方向光的一小截，
        // 曝到 1.0 也就白了，问题不大。能量圈是直径 11 米的水平面，
        // 整片正对方向光，glowGold 在上面整块曝成白色 —— 看着像白垫圈，不像在发光。
        // 这一档把蓝压到 0.02、发光也压低，曝到 1.0 之后仍是**饱和的黄**，不是白。
        ["glowAmber"] = new(new[] { 1.0, 0.72, 0.18 }, Em: new[] { 0.80, 0.45, 0.05 }),
        // MVP 那批（2026-10-14）：角斗士的铜盔、酒馆的帆布篷那样的一档灰褐。
        // 只补真正用到的色，命名与上面同口径（材质名 = 一眼看得出的颜色或东西）。
        ["bronze"] = new(new[] { 0.66, 0.48, 0.22 }, Metallic: 0.75, Rough: 0.32),
        ["clothRed"] = new(new[] { 0.52, 0.20, 0.18 }),
        ["clothBlue"] = new(new[] { 0.22, 0.30, 0.46 }),
        ["clothGreen"] = new(new[] { 0.26, 0.40, 0.28 }),
        ["canvas"] = new(new[] { 0.76, 0.72, 0.58 }),
        // 六类补登（T09 材质库）：画面基准要「材质区分砖、石板、木材、岩石、兽皮、旧铜」。
        // 木材（woodL / woodD / deckWood / thatch）、岩石（stoneL / stoneM / stoneD 的岩体用法）、
        // 兽皮（hide 系与 leather 系）、旧铜（bronze）都已有；缺的是砖与石板这两类，各补一档。
        // 暖陶砖：维多利亚砖墙的暖陶土色，比 roofRed（砖红屋顶）亮一档、偏橙（画面基准「暖陶砖」）。
        ["brick"] = new(new[] { 0.62, 0.38, 0.27 }),
        // 石板：蓝灰偏冷的铺地石板，比 stoneL（象牙色岩面）灰一档、带蓝（画面基准「蓝灰」同族）。
        ["slab"] = new(new[] { 0.52, 0.54, 0.58 }),
        // ---- T11 建筑受损态 / 废墟 / 魔晶（ticket 12，只加 key，不动上面任何一条）----
        // 焦黑：烧毁木构与墙面的过火痕，比 stoneD 更黑、带一点棕（受损态剪影的读色主力之一）。
        ["char"] = new(new[] { 0.10, 0.09, 0.08 }),
        // 灰烬：过火后的浅灰残堆，压在焦黑旁读出「烧过」的两层（画面基准无新色相，纯灰）。
        ["ash"] = new(new[] { 0.52, 0.50, 0.47 }),
        // 青色魔晶光：画面基准「青色只用于少量魔法发光」那一档，只给魔晶小件与传送残晶。
        // T14 的魔晶本体、洞口腔内那一抹与嵌入构件光环引用同一 key（发光面积由各 Build*() 守住）。
        // 2026-10-29 压深两档：游戏内实拍过曝成纯白，中性棚里也仍把绿蓝两通道顶到 255。
        // albedo 与 Em 一并降，只降到不再过曝为止。
        ["glowCyan"] = new(new[] { 0.05, 0.26, 0.32 }, Em: new[] { 0.015, 0.09, 0.11 }),
    };

    private const string GenNote =
        "GENERATED BY src/Prototypes/BeastArena/Tools/Gen.cs — 勿手改；改脚本后跑 dotnet run --project src/Tools -- gen BeastArena <项目根>";

    private static class PathDef
    {
        public static string Units(string root) => $"{root}/src/Prototypes/BeastArena/Tools/Rigs/Units";

        public static string Town(string root) => $"{root}/src/Prototypes/BeastArena/Tools/Rigs/Town";

        // 静态场景件与点位物。放进第三个子目录而不是塞进 Town/，是为了让源场景分得清
        // 「盖在甲板上的建筑」与「摆在哪都能放的点物件」；
        // Tools/Check.cs 的 FootprintPattern 把这个目录也纳入占地判定。
        public static string Props(string root) => $"{root}/src/Prototypes/BeastArena/Tools/Rigs/Props";
    }

    // 体量定档（2026-10-15）的几档。锚是人 1.7 米，其余按它定；理由见文件头那一段。
    private static readonly double[] TownFit = [2.0, 2.0, 2.0];
    private static readonly double[] TownTallFit = [2.0, 3.0, 2.0]; // 宿舍与法阵：原来那档太扁，竖向多给一档
    // 驮台与悬浮盘两件原来都走 PadFit = [2,1,2]，2026-10-15 拆件之后**那一档删掉了**：
    // 两件都改成在 Build 里直接按成品米给尺寸。盘底与塔尖之间那道场的高度就是差值，
    // 差值一旦被乘一个放大倍数就不再是那个差值，而那一格正是「浮着」还是「压着」的全部意思。
    private static readonly double[] BeastFit = [2.0, 2.0, 2.0];
    // **城只放大平面，竖向不放大**（2026-10-15 出图后改的）：巨兽开局就停在城的位置上
    // （两城相距 240 米、巨兽长 89.6 米，停靠在城里就是停在城身上）。
    // 城按 [3,3,3] 放大出来是 108×96×78.2 米 —— 比巨兽（52.9×89.6×30.2 米）宽一倍、
    // 高两倍半，于是巨兽整个陷进那只高盒子里，画面上「巨兽」和「城」读成一件东西。
    // 城在尺度上应当**低于驮着小镇的那头兽**：26.1 米 < 30.2 米，巨兽的背与头从城上方冒出来，
    // 一眼分得开。平面仍放大三档（108×96 米），因为停靠点之间的间距只有 240 米，
    // 城不放大到那个量级就分不出三处。
    // **第二项是竖向**（<c>Tscn.EmitScene</c> 的 fit 序是 X / Y / Z）——
    // 写成 [3,3,1] 是把高度留在 3 倍、出图里那口盒子一点没变矮。
    private static readonly double[] CityFit = [3.0, 1.0, 3.0];
    private static readonly double[] FoodFit = [2.0, 2.0, 2.0];
    private static readonly double[] PropFit = [2.0, 2.0, 2.0];

    /// <summary>
    /// **每个模型放大几倍，一张表看完**（体量定档 2026-10-15，见文件头那一段）。
    ///
    /// <para>表里没有的模型按 1.0 走 —— 那一档就是那把尺子本身：六件地面人形与它们的配套
    /// （尸体是人的尸体、货箱是人搬的箱子）本来就该跟人一起大，不跟着放大。</para>
    ///
    /// <para>表驱动而不是逐个 <c>Emit</c> 调用点传参：27 个模型只有 15 个要放大，
    /// 而「哪几个放大了、放大几倍」这件事必须一眼看得完 —— 散进 15 个调用点就看不完了。</para>
    /// </summary>
    private static readonly Dictionary<string, double[]> Fits = new(StringComparer.Ordinal)
    {
        // 甲板上盖的十三件建筑：一律 ×2。
        ["TownHall"] = TownFit,
        ["Arena"] = TownFit,
        ["Workshop"] = TownFit,
        ["Hotel"] = TownFit,
        ["Restaurant"] = TownFit,
        ["Granary"] = TownFit,
        ["Church"] = TownFit,
        ["Stable"] = TownFit,
        ["ArcheryRange"] = TownFit,
        ["TrainingGround"] = TownFit,
        ["AirshipDock"] = TownFit,

        // 宿舍与法阵原来那档太扁（1.05 / 1.10 米，比人矮），竖向多给一档。
        ["Dormitory"] = TownTallFit,
        ["TeleportArray"] = TownTallFit,

        // 磁悬浮底座两件都按成品米给尺寸，不走造型期缩放：
        // 底座与镇地面的相对高度要对着兽背读，乘一个放大倍数就再也读不出来。
        // 拆成两件之后这一栏就空了。

        // 巨兽要驮得下那块 48 米见方的镇子（镇地面是独立一件 TownDeck，骑在兽背上的是 12 米的 LevitationBase）。
        // 三形态同走这一档：体量定档是按「现役那一件」定的，换绑时三件一起放大才同档。
        ["Beast"] = BeastFit,
        ["BeastDesert"] = BeastFit,
        ["BeastGlutton"] = BeastFit,
        // ---- T10：二阶三形态同走这一档（不加这三行，二阶只有一阶的一半大）----
        ["BeastTier2"] = BeastFit,
        ["BeastDesertTier2"] = BeastFit,
        ["BeastGluttonTier2"] = BeastFit,

        // 大地图上的城与食物点：城要明显大于兽背那个镇（48 米见方）但要矮于巨兽，
        // 食物点比城小一档（见 <see cref="CityFit"/> 那段为什么竖向不放大）。
        ["CityNode"] = CityFit,
        ["FoodNode"] = FoodFit,

        // 跟巨兽走的道具（浮桥挂在甲板边上、空艇停在镇上空）。
        ["Pontoon"] = PropFit,
        ["Airship"] = PropFit,
    };

    private static Tscn.EmitResult Emit(string dir, string rootName, Action<string> build, double[]? fit = null) =>
        Tscn.EmitScene(dir, rootName, Uids.Uid.GetValueOrDefault(rootName), GenNote, Mat, build,
            fit ?? Fits.GetValueOrDefault(rootName));

    // ---------------- 跨域共用的形状助手 ----------------

    /// <summary>
    /// 双坡顶：两片只绕 X 转的薄板 + 一道脊梁，从 <paramref name="y"/> 起拱到
    /// <paramref name="y"/> + <paramref name="rise"/>。
    ///
    /// 刻意不用 <c>Tscn.Py</c>：金字塔图元带 45° 偏航，而离线校验器（src/Tools/TscnCheck.cs）
    /// 把每个图元按**旋转过的盒子**算包围盒，于是 XZ 两向各涨 √2 倍 ——
    /// 2.6 米的底座用 Py 会被算成 3.68 米，顶破 3×3 那一档的 2.8 米上限。
    /// 只绕 X 转的薄板不吃这个亏：X 向宽度一点不涨，Z 向只多出 <c>厚·sin</c> 那一丁点。
    /// </summary>
    private static void Gable(string p, string mat, double w, double d, double y, double rise,
        double thick = 0.06)
    {
        var half = d / 2.0;
        var len = Math.Sqrt(half * half + rise * rise);
        var pitch = Math.Atan2(rise, half) / Tscn.D;
        foreach (var s in new[] { -1.0, 1.0 })
            Tscn.Cbx(p, "roofSlab", mat, new[] { 0.0, y + rise / 2.0, s * half / 2.0 },
                new[] { w, thick, len }, Humanoid.O(rx: s * pitch));
        Tscn.Cbx(p, "ridge", "woodD", new[] { 0.0, y + rise + thick / 2.0, 0.0 },
            new[] { w, thick * 1.2, thick * 1.6 });
    }

    /// <summary>排完一遍。登记进 ExportModels.gd 的那张表要同步（那边漏一边导不出）。</summary>
    public static int Run(string root)
    {
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException("usage: dotnet run --project src/Tools -- gen BeastArena <project-root>");
        var built = new List<Tscn.EmitResult>
        {
            // 巨兽三形态：同一副四足骨架，三套剪影（原始态那一版是现役那次调用的逐字复现）。
            BuildBeast(root, BeastLooks[0]),
            BuildBeast(root, BeastLooks[1]),
            BuildBeast(root, BeastLooks[2]),
            BuildWorker(root),
            BuildArena(root),
            BuildWorkshop(root),
            BuildLevitationBase(root),

            BuildTownDeck(root),
            // MVP 十三件建筑：竞技场与工坊在上面（缩聚重生成），其余十一件是这一轮新增
            BuildTownHall(root),
            BuildTrainingGround(root),
            BuildStable(root),
            BuildArcheryRange(root),
            BuildAirshipDock(root),
            BuildHotel(root),
            BuildRestaurant(root),
            BuildGranary(root),
            BuildDormitory(root),
            BuildChurch(root),
            BuildTeleportArray(root),
            // MVP 五件地面人形：员工在上面（复用），其余四件是这一轮新增
            BuildGladiator(root),
            BuildCitizen(root),
            BuildHelper(root),
            BuildMonster(root),
            // MVP 七件静态场景件与点位物
            BuildPontoon(root),
            BuildAirship(root),
            BuildCorpse(root),
            BuildCargo(root),
            BuildCityNode(root),
            BuildFoodNode(root),
            // ---- T12 第二种原生怪（列表末尾追加，不动上面任何一行的顺序）----
            BuildMonsterBurrower(root),
            // ---- T10：六套巨兽与两阶底座甲板（只追加，前面各件的产出字节不动）----
            BuildBeast(root, BeastLooks[3]),
            BuildBeast(root, BeastLooks[4]),
            BuildBeast(root, BeastLooks[5]),
            BuildLevitationBaseTier2(root),
            BuildTownDeckTier2(root),

            // ---- T14 大地图、生态、地下与船只：十二件全在末尾追加 ----
            // 域拆分同伴 Gen.World.cs：生态四类（两种树 / 矿石 / 兽群）、魔晶、远方高塔、
            // 两层地下五件套（地块 / 空腔 / 洞口 / 工作面 / 嵌入构件 / 地面出口）。
            // 排在最后与「导出顺序」同一口径：中间插条目会让后续每件 .glb 改字节。
            BuildTreePine(root),
            BuildTreeBroadleaf(root),
            BuildOreNode(root),
            BuildHerdBeast(root),
            BuildCrystalShard(root),
            BuildDistantTower(root),
            BuildSoilBlock(root),
            BuildCavernTile(root),
            BuildCaveMouth(root),
            BuildMiningFace(root),
            BuildCrystalSocket(root),
            BuildGroundExit(root),
            // ---- T13 三城地标与地表（18 件，只追加）----
            BuildLandmarkPoor(root),
            BuildLandmarkSand(root),
            BuildLandmarkRich(root),
            BuildBackPoorA(root),
            BuildBackPoorB(root),
            BuildBackPoorC(root),
            BuildBackPoorD(root),
            BuildBackSandA(root),
            BuildBackSandB(root),
            BuildBackSandC(root),
            BuildBackSandD(root),
            BuildBackRichA(root),
            BuildBackRichB(root),
            BuildBackRichC(root),
            BuildBackRichD(root),
            BuildGroundPoor(root),
            BuildGroundSand(root),
            BuildGroundRich(root),
        };
        // ---- T11 建筑受损态、脚手架、废墟与搬运小件（ticket 12）：只追加，前面顺序一字未动 ----
        built.AddRange(TownStatesRun(root));
        var total = 0;
        foreach (var b in built)
        {
            Console.WriteLine($"{b.RootName.PadRight(14)} {b.Count.ToString().PadLeft(4)} nodes");
            total += b.Count;
        }
        Console.WriteLine($"--- total {total} nodes / {built.Count} scenes");
        return 0;
    }
}
