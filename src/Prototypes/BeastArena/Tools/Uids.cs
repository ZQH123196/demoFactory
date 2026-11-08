// 巨兽竞技场 造型源的 uid 表 —— 由生成器持有，不留给引擎「编辑器存盘时顺手补一个」：
// 那次存盘会同时给每个节点注入 unique_id、并省掉头行的 load_steps，把生成物变成一大片
// 纯格式化 diff（无限领主实测）。字符集与 13 位长度沿用引擎惯例（仓库既有 uid 只用 0-9 与 a-z），
// 已与本仓库全部既有 uid 比对无冲突。
// 注意：改这里的字符串 = 换资源身份，按 uid 引用它的地方会失联（当前都按 path 引用，「按 res:// 引用」）。
//
// 十件模型的 uid 一次登记完 —— 造型分工（巨兽四足 / 人形 / 背上小镇的静态件）见
// src/Prototypes/BeastArena/Models/README.md，造型线六步的命令原文见 docs/《模型与动画准则》「造型线六步」。
// MVP 二十七件清单在 2026-10-14 一次登记完（新增 22 件 + 缩聚重生成 2 件沿用旧 uid）；
// 切片 1/2 剩下的六件旧件（ArrowTower / SlowTrap / Breach / Quarry / Farm / Market）
// 在 2026-10-15 接线那张 ticket 里随旧码一起退休，那六条 uid 一并删掉。
// 每加一个模型要同时在这里加一行、Tools/Gen.cs 的 Run() 加一个 Build*()、
// Tools/ExportModels.gd 的 models() 加一行、Tools/BeastArenaTools.cs 的 Review().Models 加一条 —— 四处一起动，漏一边导不出。

#nullable enable
using System.Collections.Generic;

namespace DemoFactory.Tools.BeastArena;

public static class Uids
{
    public static readonly IReadOnlyDictionary<string, string> Uid = new Dictionary<string, string>
    {
        // 会动的两件：巨兽走四足骨架（动作走 clips_beast.gd），员工/战斗员/两种怪物共用一副人形骨架
        ["Beast"] = "q6s4g4kanq8go",
        ["Worker"] = "c8gu85lrsypvr",
        // 巨兽另两形态（2026-10-20）：同一副四足骨架、同一套动作名、同一档包围盒，
        // 剪影差在配色与体量上。造型参数见 Gen.cs 的 BeastLooks 表。
        ["BeastDesert"] = "b3kq7wmx9rtdv",
        ["BeastGlutton"] = "f5nh8cvy2jzqs",
        // 背上小镇的静态件（不进游戏，按 res:// 路径引用）
        ["Arena"] = "yg0nuw3ekkmik",
        ["Workshop"] = "bgn7hcizfdka5",
        // 磁悬浮底座与镇地面（2026-10-15 拆成两件）：**一件东西的两半** ——
        // 下半是骑在兽背上的圆形底座（LevitationBase，直径 12 米 = 镇地面的四分之一，顶面一圈发光能量圈），
        // 上半是保持水平的镇地面（TownDeck，48 米见方），镇建在它上面。
        // 两半之间那道空就是磁悬浮场。造型与尺寸来路见 Gen.cs 的 BuildLevitationBase / BuildTownDeck，
        // 各件顶面高度由 Tools/Check.cs 量出来回填到 DeckView.LevitationBaseHeight / TownDeckHeight。
        ["LevitationBase"] = "h7mz3kq9vwbxd",
        ["TownDeck"] = "q7lvp3ma9xkdr",
        // ---- MVP 二十七件清单里这一轮新增的二十二件（2026-10-14）----
        // 竞技场与工坊是缩聚重生成，uid 沿用上面那两条（同一份资源，只改了造型与尺寸）。
        // 十三件建筑：底座上限 4×4 ≤ 3.8 / 3×3 ≤ 2.8 / 2×2 ≤ 1.8 米，清单见 Gen.cs 那一段。
        ["TownHall"] = "k3xr7mq2pd8vb",
        ["TrainingGround"] = "w5tn1jz9c4hds",
        ["Stable"] = "r8yv3bf6g2qlk",
        ["ArcheryRange"] = "t2mp9xd4w7nhs",
        ["AirshipDock"] = "z6hc1kt8b5yvr",
        ["Hotel"] = "h4qw7nz3v9dcm",
        ["Restaurant"] = "g9xt2bm5k8rpf",
        ["Granary"] = "d7vn4lc1s6tyq",
        ["Dormitory"] = "b5mj8zr2t9wxn",
        ["Church"] = "c9xk3vp7n5hdq",
        ["TeleportArray"] = "n4wt6zy1q8kbc",
        // 五件地面人形：员工复用上面那条，其余四件走人形骨架 + clips_humanoid.gd 五条动作。
        ["Gladiator"] = "p6yh2md8v3sft",
        ["Citizen"] = "s8kn4bw7j1zrq",
        ["Helper"] = "m3vd9tx6b2plw",
        ["Monster"] = "f7qj1kc4n8ybt",
        // 七件静态场景件与点位物：不挂骨架、不带动作。
        ["Pontoon"] = "v2zh8mw5t9rqd",
        ["Airship"] = "y5tn3bx7w1vkc",
        ["Corpse"] = "a8mp4kz2s6yhn",
        ["Cargo"] = "k1wv9rd6j3tqm",
        ["CityNode"] = "w9hd3mt6v1kzq",
        ["FoodNode"] = "p7bs2rn4k8yvm",
        // 预览舞台（GenPreview.cs）
        ["PreviewCrew"] = "iwd6mrtrba678",
        ["PreviewTown"] = "y4hqf2m1n4dcq",
        ["PreviewModels"] = "3unrqoy0vj6e3",
        // ---- T12 第二种原生怪（对接面清单「原生怪七条」G-14）----
        // 与 Monster 同一副人形骨架、同一套七条动作；uid 与仓库全部既有 uid 比对无冲突。
        ["MonsterBurrower"] = "k4rd7tq2m8xwn",
        // ---- T11 建筑受损态 / 脚手架 / 废墟 / 搬运小件（ticket 12，只追加）----
        // 十三件受损态与各自完工件同档同入口；脚手架与废墟按占地三档（Small=2×2 / Medium=3×3 / Large=4×4）。
        ["TownHallDamaged"] = "t7kq2mxv9d4sb",
        ["ArenaDamaged"] = "a4rn8wqj3z5vc",
        ["TrainingGroundDamaged"] = "h2ydv6pm9xtwk",
        ["StableDamaged"] = "s8fq3jzn7b2rd",
        ["ArcheryRangeDamaged"] = "w5mt9xkc2q8hv",
        ["WorkshopDamaged"] = "r3kz7vbq5n9md",
        ["AirshipDockDamaged"] = "z9hn4wtd8m2jk",
        ["HotelDamaged"] = "k6dq2mvj8t4rf",
        ["RestaurantDamaged"] = "x4jv8rzq3w7bn",
        ["GranaryDamaged"] = "m2tr6kqh9s4zd",
        ["DormitoryDamaged"] = "b7sw3njx8q5tv",
        ["ChurchDamaged"] = "n5qv9dmt4h2kr",
        ["TeleportArrayDamaged"] = "q8zd3hwk7m6tn",
        ["ScaffoldSmall"] = "f6mk2qxt9rv3d",
        ["ScaffoldMedium"] = "g3wt8njr5d7qk",
        ["ScaffoldLarge"] = "p9rh4kqd2vz8m",
        ["RuinSmall"] = "v2nb7mzs3t6jx",
        ["RuinMedium"] = "j5xq9wtd8k2fr",
        ["RuinLarge"] = "c7vt3rjq6m4hn",
        ["OreChunk"] = "u4mb8kzf2ws7t",
        ["SpecialMaterial"] = "e9wk6trn3h2jd",
        ["SparePart"] = "i3qd7vms5x2yn",
        ["Crystal"] = "l8zr4kwt6n2bh",
        // ---- T10：六套巨兽之二阶三件 + 二阶底座/甲板 ----
        // 与上面一阶各件同骨架同动作组；字符集 0-9a-z、13 位，已与本表全部既有 uid 逐字比对无冲突。
        ["BeastTier2"] = "t4wq8zn6b3rxe",
        ["BeastDesertTier2"] = "k9db4mt7s2hqv",
        ["BeastGluttonTier2"] = "n6jz3wh8p5tdy",
        ["LevitationBaseTier2"] = "r3vc7qk2m9xbs",
        ["TownDeckTier2"] = "d8yt5nf4k6zwp",
        // ---- T14 大地图、生态、地下与船只（2026-10，静态件十二项，造型见 Gen.World.cs）----
        ["TreePine"] = "t4wq9xkm2pdhs",
        ["TreeBroadleaf"] = "k8mb3ztr7vnqe",
        ["OreNode"] = "r2yh6fc9sxkmd",
        ["HerdBeast"] = "n7vd2qw5j8tzr",
        ["CrystalShard"] = "x3kf8mbr6t9qs",
        ["DistantTower"] = "h9tr4ncz2k5vw",
        ["SoilBlock"] = "m5xw7jdp3q8yk",
        ["CavernTile"] = "z8qn3vht6rmbd",
        ["CaveMouth"] = "w6ry9kdf2tsxh",
        ["MiningFace"] = "j3ht5mzc8qvnk",
        ["CrystalSocket"] = "d9kp2wyf6n4rx",
        ["GroundExit"] = "v7mb8tqz3h5jk",
        // ---- T13 三城地标与地表（18 件）----
        ["LandmarkPoor"] = "k2dp7wq4x9mvt",
        ["LandmarkSand"] = "v6rt3jmy8b1zh",
        ["LandmarkRich"] = "z8qw1nfx5d3cp",
        ["BackPoorA"] = "a4mn6ykt2rv9s",
        ["BackPoorB"] = "b7vx2dqj9p4hw",
        ["BackPoorC"] = "c3th8srn5k1qm",
        ["BackPoorD"] = "d9rk4bvw6t2jn",
        ["BackSandA"] = "e2jw7mpx3h5vc",
        ["BackSandB"] = "f5cv3ntk8q1rd",
        ["BackSandC"] = "g8hn2rfy4w6tp",
        ["BackSandD"] = "h3pq6tdx9m2vf",
        ["BackRichA"] = "i7mf4kwz2s8nb",
        ["BackRichB"] = "j2vb9hnq5d3tx",
        ["BackRichC"] = "k6td3mvj8r1wp",
        ["BackRichD"] = "l9wx5pqk2f6nd",
        ["GroundPoor"] = "m4nr8tdv3j7hq",
        ["GroundSand"] = "n8jv2wkq6p4md",
        ["GroundRich"] = "p2hd6qfx9t3kr",
    };
}
