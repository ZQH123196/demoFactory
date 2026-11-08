// 巨兽竞技场的开发期工具注册 —— 机制抄自 src/Prototypes/_Template/Tools/TemplateTools.cs。
// src/Tools/DemoFactory.Tools.csproj 按 `Prototypes\*\Tools\*.cs` 通配源码链接本文件，
// src/Tools/Program.cs 反射发现 IPrototypeTools 的实现 —— 所以接新原型不需要改公共层一行。
//
// 造型线六步（命令原文见 docs/《模型与动画准则》「造型线六步」）：
//   1 gen     本文件 RunVerb → Tools/Gen.cs     产 Tools/Rigs/**/*.tscn（关节树源场景）
//   2 check   本文件 RunVerb → Tools/Check.cs   离线几何校验器（读 .tscn，不起引擎）
//   3 export  引擎跑 glb/export_glb.gd -- --proto=BeastArena  → Models/*.glb
//   4 import  引擎跑 --headless --import       生成 .glb.import 伴随文件（漏这步第 5 步必假 FAIL）
//   5 inspect 引擎跑 glb/inspect_model.gd -- --models-dir=… --all --max-footprint=48
//   6 review  公共层 Review.Run（本文件只提供契约）—— 十视图 + 动画胶卷 + 比例尺同框
// 第 3~5 步要引擎，本文件管不着；preview 是本原型的验收台（.glb 那台要第 3 步的产物才能看）。
// verify = 第 2 步 + 第 5 步的一条命令编排（只转发两个既有校验器，不重算）。

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace DemoFactory.Tools.BeastArena;

public sealed class BeastArenaTools : IPrototypeTools
{
    public string DirName => "BeastArena";

    /// <summary>
    /// 模型体检单契约（《模型与动画准则》「实现分层与聚合」第 4 条：清单与契约属于原型，介质参数属于公共层）。
    ///
    /// 占地上限 120 米的来路：巨兽是这批造型里水平最大的一件。2026-10-15 体量定档整体放大一倍后
    /// 实测占地 52.9 × 89.6 米（放大前 26.5 × 45.0 米 —— 那一档驮不动一块 48 米见方的镇子），
    /// 大地图上的城 108 × 96 米也在这条线内，于是**一条上限套得住全部**，不必分目录给两套契约。
    /// 同一个数也写死在 Tools/Check.cs 的 MaxFootprint（整米），两处同值，改一处要改两处。
    /// 背上小镇不烘进 Beast.glb（只有巨兽自己的身体）—— 否则这一栏与比例尺同框都失真。
    ///
    /// 十三件建筑另按蓝图档位各自更严：4×4 格 ≤ 3.80 / 3×3 格 ≤ 2.80 / 2×2 格 ≤ 1.80 米
    /// （设计米，放大后为 7.60 / 5.60 / 3.60 米）。体检单这一栏是逐件实测占地，比对着核。
    ///
    /// NeedClips 只判三条：巨兽（clips_beast.gd）与地面人形六件（clips_humanoid.gd）都齐
    /// idle/walk/run，静态件不挂 AnimationPlayer、这一项对它们直接跳过（那一条写在
    /// glb/model_metrics.gd 的 judge()）。人形多带的那几条（attack_melee / hit / death /
    /// work / carry 与原生怪的 emerge）、巨兽多带的五条单发（eat / lie_down / get_up /
    /// form_shift / advance，T10）都不进 NeedClips —— 「模型四件套」 要的正是所有带骨架的
    /// 模型都有 idle/walk/run 这三条。
    /// LoopClips 与动作库里 "loop": true 的那几条一致（T12 起补 work / carry 两条循环姿势；
    /// 巨兽新五条是单发，不进这一栏）；glTF 规范里没有循环字段，模型自己说不清，这里替它声明。
    /// Clips 十四名 = 人形组的九条（T12 口径）+ 巨兽八条里多出的五条单发（T10）；
    /// 胶片采集按「这件模型带了哪条」过滤，人形不带 eat / lie_down 就不出那五格，巨兽同理不出 attack 那一格。
    /// Models 八十八件显式登记（spec 的 MVP 清单 + 各资产波次的追加件）：
    /// 少一个名字体检单就少审一件，
    /// 而「审目录内全部」会把将来的临时文件也一起送审。切片 1/2 剩下的六件旧件
    /// （ArrowTower / SlowTrap / Breach / Quarry / Farm / Market）在 2026-10-15 随旧码退休。
    /// 这张表与 Models/ 目录现在都恰好是这八十八件；第 5 步校验器扫的是整个目录，两边应当同数。
    /// （2026-10-20 加了巨兽另两形态 BeastDesert / BeastGlutton；T12 加了第二种原生怪
    /// MonsterBurrower；T11 追加 23 件受损态、脚手架、废墟与搬运小件；
    /// T14 大地图波次追加 12 件世界与地下静态件。清单里数一遍为准。）
    /// </summary>
    public ReviewContract Review() => new(
        ModelsDir: "src/Prototypes/BeastArena/Models",
        Models: new[]
        {
            // 磁悬浮底座、镇地面、巨兽三形态（5）
            "Beast", "BeastDesert", "BeastGlutton", "LevitationBase", "TownDeck",
            // 十三件建筑（13）
            "Arena", "ArcheryRange", "AirshipDock", "Church", "Dormitory", "Granary",
            "Hotel", "Restaurant", "Stable", "TeleportArray", "TownHall", "TrainingGround", "Workshop",
            // 居民四件与原生怪两件（6）：居民带八条、原生怪带七条（对接面清单 2.2 / 2.3）
            "Citizen", "Gladiator", "Helper", "Worker", "Monster",
            // 六件静态场景件与点位物（6）
            "Airship", "Cargo", "CityNode", "Corpse", "FoodNode", "Pontoon",
            // ---- T12 第二种原生怪（G-14；与 Monster 同副人形骨架、同套七条动作）----
            "MonsterBurrower",
            // ---- T11 建筑受损态 / 脚手架 / 废墟 / 搬运小件（ticket 12，追加 23 件，总数 52）----
            "TownHallDamaged", "ArenaDamaged", "TrainingGroundDamaged", "StableDamaged",
            "ArcheryRangeDamaged", "WorkshopDamaged", "AirshipDockDamaged", "HotelDamaged",
            "RestaurantDamaged", "GranaryDamaged", "DormitoryDamaged", "ChurchDamaged", "TeleportArrayDamaged",
            "ScaffoldSmall", "ScaffoldMedium", "ScaffoldLarge",
            "RuinSmall", "RuinMedium", "RuinLarge",
            "OreChunk", "SpecialMaterial", "SparePart", "Crystal",
            // ---- T10：二阶三形态 + 二阶底座/甲板（5）----
            "BeastTier2", "BeastDesertTier2", "BeastGluttonTier2", "LevitationBaseTier2", "TownDeckTier2",
            // ---- T14 大地图、生态、地下与船只（12）----
            // 生态四类（两种树 / 矿石 / 兽群）、魔晶、远方高塔、两层地下五件套。
            "TreePine", "TreeBroadleaf", "OreNode", "HerdBeast", "CrystalShard", "DistantTower",
            "SoilBlock", "CavernTile", "CaveMouth", "MiningFace", "CrystalSocket", "GroundExit",
            // ---- T13 三城地标与地表（18）----
            "LandmarkPoor", "LandmarkSand", "LandmarkRich",
            "BackPoorA", "BackPoorB", "BackPoorC", "BackPoorD",
            "BackSandA", "BackSandB", "BackSandC", "BackSandD",
            "BackRichA", "BackRichB", "BackRichC", "BackRichD",
            "GroundPoor", "GroundSand", "GroundRich",
        },
        Clips: new[] { "idle", "walk", "run", "attack_melee", "hit", "death", "work", "carry", "emerge",
            "eat", "lie_down", "get_up", "form_shift", "advance" },
        MaxFootprint: 120,
        NeedClips: new[] { "idle", "walk", "run" },
        // ---- T12：work / carry 是循环姿势（在岗与在途状态，靠下一次状态切换停）----
        LoopClips: new[] { "idle", "walk", "run", "work", "carry" },
        AnchorName: "Anchor",
        ReportPath: "docs/prototypes/beast-arena/model-review.md")
    {
        // ---- 栏 3 比例尺按占地档分组（ADR 0019）：十组、合计 88 件，与送审清单一一对应。
        // 组内一行、间隔 1 米；网格按档位（小件 0.5 米 / 底座与巨兽 10 米 / 其余 1 米），
        // 判据是 640×480 出图下组内最窄件 ≥ 20 像素。
        ScaleGroups = new[]
        {
            new ScaleGroup("small",
                new[] { "Cargo", "Corpse", "OreChunk", "SpecialMaterial", "SparePart", "Crystal" }, 0.5),
            new ScaleGroup("units",
                new[] { "Citizen", "Gladiator", "Helper", "Worker", "Monster", "MonsterBurrower", "HerdBeast" }, 1.0),
            new ScaleGroup("town",
                new[]
                {
                    "Arena", "ArcheryRange", "AirshipDock", "Church", "Dormitory", "Granary",
                    "Hotel", "Restaurant", "Stable", "TeleportArray", "TownHall", "TrainingGround", "Workshop",
                }, 1.0),
            new ScaleGroup("town_damaged",
                new[]
                {
                    "TownHallDamaged", "ArenaDamaged", "TrainingGroundDamaged", "StableDamaged",
                    "ArcheryRangeDamaged", "WorkshopDamaged", "AirshipDockDamaged", "HotelDamaged",
                    "RestaurantDamaged", "GranaryDamaged", "DormitoryDamaged", "ChurchDamaged", "TeleportArrayDamaged",
                }, 1.0),
            new ScaleGroup("town_build",
                new[] { "ScaffoldSmall", "ScaffoldMedium", "ScaffoldLarge", "RuinSmall", "RuinMedium", "RuinLarge" }, 1.0),
            new ScaleGroup("back",
                new[]
                {
                    "BackPoorA", "BackPoorB", "BackPoorC", "BackPoorD",
                    "BackSandA", "BackSandB", "BackSandC", "BackSandD",
                    "BackRichA", "BackRichB", "BackRichC", "BackRichD",
                }, 1.0),
            new ScaleGroup("outdoor",
                new[]
                {
                    "Pontoon", "TreePine", "TreeBroadleaf", "OreNode", "CrystalShard",
                    "CrystalSocket", "GroundExit", "MiningFace",
                }, 1.0),
            new ScaleGroup("world",
                new[] { "Airship", "FoodNode", "DistantTower", "SoilBlock", "CavernTile", "CaveMouth" }, 1.0),
            new ScaleGroup("bases",
                new[] { "LevitationBase", "LevitationBaseTier2", "TownDeck", "TownDeckTier2" }, 10.0),
            new ScaleGroup("large",
                new[]
                {
                    "Beast", "BeastDesert", "BeastGlutton", "BeastTier2", "BeastDesertTier2", "BeastGluttonTier2",
                    "LandmarkPoor", "LandmarkSand", "LandmarkRich",
                    "GroundPoor", "GroundSand", "GroundRich", "CityNode",
                }, 10.0),
        },
        // ---- 胶卷机位：巨兽六件侧向（方位 90°、仰角 10°）才能数出四条腿的步态；
        // 人形与其他件不声明，走缺省 iso 135° / 45°。----
        FilmAngles = new[]
        {
            new FilmAngle(
                new[] { "Beast", "BeastDesert", "BeastGlutton", "BeastTier2", "BeastDesertTier2", "BeastGluttonTier2" },
                90.0, 10.0),
        },
    };

    /// <summary>
    /// 原型自有动词。造型线的前两格与验收台在这里分发；没实现的返回 -1
    /// （= 这个动词不归本原型管，公共层继续往下派）。
    /// </summary>
    public int RunVerb(string verb, string[] argv)
    {
        var root = argv.Length > 2 ? argv[2] : "";
        var extra = argv.Length > 3 ? argv[3] : null;
        return verb switch
        {
            "gen" when argv.Length >= 3 => Gen.Run(root),
            "check" when argv.Length >= 3 => Check.Run(root, extra),
            "verify" when argv.Length >= 3 => Verify(root, argv),
            "preview" when argv.Length >= 3 => GenPreview.Run(root, extra),
            "replay" when argv.Length >= 3 => ReplayCli.Run(argv),
            "audiogen" when argv.Length >= 3 => AudioGen.Run(root, extra),
            _ => -1,
        };
    }

    /// <summary>
    /// 一条命令出模型的最终结论 —— 造型线第 2 格（离线几何校验）与第 5 格（引擎回读校验）串成一步。
    ///
    /// 为什么要这层薄封装：这两步本来就只转发既有校验器（Check.cs 与 glb/inspect_model.gd），
    /// 这里不重算、不自造第二套实现，只补「两步才能出结论」那一段编排。
    /// 参数全部取自本文件的 ReviewContract —— 上限与动作契约不许在这里各写一份。
    ///
    /// 跑法（项目根；引擎定位三级找法见 src/Tools/GodotBin.cs）：
    ///   dotnet run --project src/Tools -- verify BeastArena . [--godot=&lt;exe&gt;]
    /// 前提：第 3~4 步（export + import）的 .glb 与 .glb.import 已在 Models/ 里，
    /// 否则第 5 步会报「加载失败」——那是资源没进过导入，不是造型不合格（《模型与动画准则》「造型线六步」坑 1）。
    /// </summary>
    private static int Verify(string root, string[] argv)
    {
        var offline = Check.Run(root, null);
        if (offline != 0)
        {
            Console.Error.WriteLine("verify：第 2 步离线几何校验未过，不再起引擎。");
            return offline;
        }

        var c = new BeastArenaTools().Review();
        var exe = GodotBin.Resolve(argv.FirstOrDefault(a => a.StartsWith("--godot="))?.Substring("--godot=".Length) ?? "",
            "（verify 第 5 步要引擎回读 .glb；只读校验可无头。）");
        var args = new List<string>
        {
            "--headless", "--path", root, "--quit-after", "120",
            "--script", "res://src/GugaFramework/Tools/glb/inspect_model.gd", "--",
            $"--models-dir=res://{c.ModelsDir}/", "--all",
            $"--max-footprint={c.MaxFootprint.ToString(CultureInfo.InvariantCulture)}",
            $"--need-clips={string.Join(",", c.NeedClips)}",
        };
        Console.WriteLine($"> {System.IO.Path.GetFileName(exe)} verify BeastArena inspect "
                          + $"models-dir=res://{c.ModelsDir}/ max-footprint={c.MaxFootprint} need-clips={string.Join(",", c.NeedClips)}");
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Godot 进程没起来：" + exe);
        Console.WriteLine(p.StandardOutput.ReadToEnd().Trim());
        var se = p.StandardError.ReadToEnd().Trim();
        if (!p.WaitForExit(300_000))
        {
            try { p.Kill(true); } catch { /* 已经自己退了 */ }
            Console.Error.WriteLine("verify：引擎回读超过 300 秒没结束，已终止 Godot。");
            return 1;
        }
        if (se.Length > 0) Console.Error.WriteLine(se);
        var engine = p.ExitCode;
        Console.WriteLine(engine == 0
            ? "verify 结论：第 2 步与第 5 步都过 —— 模型体检 PASS。"
            : "verify 结论：第 5 步引擎回读判了 FAIL，看上面 FAIL 行。");
        return engine;
    }
}
