extends RefCounted

# 巨兽竞技场 · 模型导出登记表（开发期工具，不进游戏、不被 csproj 编译）。
#
# 为什么要有这个文件：导出器 src/GugaFramework/Tools/glb/export_glb.gd 必须在引擎里跑
# （CSG 合并网格与 GLTFDocument 导出都是引擎内的活），C# 侧那套 IPrototypeTools 反射够不到它。
# 于是「换原型只改表」这条落在 GDScript 侧：导出器带 --proto=BeastArena 时来 load 本文件，
# 三张表（源目录 / 产物目录 / 模型清单）全在本原型手里，公共层不动一行。
#
# 跑法（项目根）：
#   godot --headless --path . --quit-after 900 \
#     --script res://src/GugaFramework/Tools/glb/export_glb.gd -- --proto=BeastArena
#   尾参再加 -- --only=Beast 只出一个；-- --list 只列本表
#   导出完必须补一次 `godot --headless --path . --import`，否则第 5 步校验器 GD.Load 不到
#   Models/*.glb（.glb.import 伴随文件不在）—— 详见《模型与动画准则》「造型线六步」与坑 1。
#
# 表里的 clips 名单来自两个动作库：人形走 glb/clips_humanoid.gd，巨兽走 glb/clips_beast.gd。
# 背上小镇那八件静态资产**不写 clips** ⇒ 该 .glb 不挂 AnimationPlayer，导成一个无骨架网格。
# 源场景路径相对 src_root()，约定面朝 +Z、脚在 y=0，j_ 前缀是骨骼契约。
#
# 每加一个模型要三处一起动：本表 models()、Tools/Gen.cs 的 Run()、Tools/Uids.cs。
# 漏一边导不出。**2026-10-20 起是四处**：再加 Tools/BeastArenaTools.cs 的 Review().Models
# （体检单那一份显式清单，少一条就少审一件）。

## 居民八条 / 原生怪七条（对接面清单「人物八条」「原生怪七条」，T12 补齐）。
## LOCOMOTION 是两条动作库都齐的 locomotion 三条；人形库另带近战、受击、死亡、工作、搬运与出土。
const LOCOMOTION := ["idle", "walk", "run"]
const GROUND_HUMANOID := LOCOMOTION + ["attack_melee", "hit", "death", "work", "carry"]
const MONSTER_GROUND := LOCOMOTION + ["attack_melee", "hit", "death", "emerge"]

## 巨兽八条（T10，规格「动作」）：locomotion 三条 + 吞食 / 伏下 / 起身 / 形态切换 / 进阶五条单发。
## 名单与 clips_beast.gd 的 build() 一案一对；起止与接触点口径的冻结清单在 interface.md。
## 六套巨兽（三形态 × 一 / 二阶）都带这八条 —— 骨架一副、动作一组，位阶差在剪影不在动作名。
const BEAST_CLIPS := LOCOMOTION + ["eat", "lie_down", "get_up", "form_shift", "advance"]


static func src_root() -> String:
	return "res://src/Prototypes/BeastArena/Tools/Rigs/"


static func out_root() -> String:
	return "res://src/Prototypes/BeastArena/Models/"


## MVP 二十七件清单（2026-10-14 一次登记齐）+ 过渡期留下的六件旧件。
## 名单与造型分工见 src/Prototypes/BeastArena/Models/README.md 与 Tools/Gen.cs 的 Run()。
## 静态件**不写 clips** ⇒ 该 .glb 不挂 AnimationPlayer，导成一个无骨架网格。
static func models() -> Dictionary:
	return {
		# 巨兽：四足骨架（骨名带 m_ 前缀，因为骨架由 src/Tools/QuadrupedRig.cs 出），
		# 动作走新建的兽用库 clips_beast.gd —— lib = "beast" 就是这件事的开关。
		# ⚠ 这条开关依赖**三件都在版本库里**：export_glb.gd 的 BeastClips preload +
		# `elif clip_lib == "beast"` 分支，加上 clips_beast.gd 本身（2026-09-30 补交，
		# 此前三件里只有后两件在工作区、源文件漏提交）。缺任一件的后果不是报错退出，
		# 而是**安静地导出动作 0 的巨兽**（`_fail` 之后整轮照样 exit 0）——
		# 「模型四件套」 要求的四样少一样，而模型评审只看 .glb 看不出这个。
		# `Tests/AssetSourceGuardTests.cs` 四条断言守着这件事，改那张表前先跑它。
		"Beast": { src = "Units/Beast.tscn", lib = "beast", clips = BEAST_CLIPS },
		# 磁悬浮底座与镇地面：一件东西的两半（2026-10-15 拆）。静态件不写 clips。
		# 下半骑在兽背上，上半保持水平 —— 拆开是因为两个半边要挂在两个不同的东西上。
		# 存在本身不是造型偏好：小镇原来直接坐在 Beast.glb 自带的木质驮台上，
		# 而那个驮台跟着走路动作起伏，于是小镇与脚下的台面一张一合。
		# 尺寸与高度来路见 src/Prototypes/BeastArena/Tools/Gen.cs 的 BuildLevitationBase / BuildTownDeck。
		"LevitationBase": { src = "Town/LevitationBase.tscn" },
		"TownDeck": { src = "Town/TownDeck.tscn" },
		# ---- 十三件建筑（全部静态件）----
		# 地标竞技场与工坊是 2026-10-14 缩聚重生成的（7.2 → 3.6 米 / 7.2 → 2.6 米，
		# 新网格格长 1.0 米），造型与占地上限见 Gen.cs 的 BuildArena / BuildWorkshop。
		"Arena": { src = "Town/Arena.tscn" },
		"TownHall": { src = "Town/TownHall.tscn" },
		"TrainingGround": { src = "Town/TrainingGround.tscn" },
		"Stable": { src = "Town/Stable.tscn" },
		"ArcheryRange": { src = "Town/ArcheryRange.tscn" },
		"Workshop": { src = "Town/Workshop.tscn" },
		"AirshipDock": { src = "Town/AirshipDock.tscn" },
		"Hotel": { src = "Town/Hotel.tscn" },
		"Restaurant": { src = "Town/Restaurant.tscn" },
		"Granary": { src = "Town/Granary.tscn" },
		"Dormitory": { src = "Town/Dormitory.tscn" },
		"Church": { src = "Town/Church.tscn" },
		"TeleportArray": { src = "Town/TeleportArray.tscn" },
		# ---- 地面人形六件：同一副人形骨架（Humanoid.Rig）+ clips_humanoid.gd ----
		# 居民四件带八条（GROUND_HUMANOID），原生怪两件带七条（MONSTER_GROUND）—— T12 补齐。
		# 差异做进模型本身（比例、材质、随身那一套装备），不在表现层换皮或缩放几何。
		"Worker": { src = "Units/Worker.tscn", clips = GROUND_HUMANOID },
		"Gladiator": { src = "Units/Gladiator.tscn", clips = GROUND_HUMANOID },
		"Citizen": { src = "Units/Citizen.tscn", clips = GROUND_HUMANOID },
		"Helper": { src = "Units/Helper.tscn", clips = GROUND_HUMANOID },
		"Monster": { src = "Units/Monster.tscn", clips = MONSTER_GROUND },
		# ---- 七件静态场景件与点位物（静态件，不写 clips）----
		"Pontoon": { src = "Props/Pontoon.tscn" },
		"Airship": { src = "Props/Airship.tscn" },
		"Corpse": { src = "Props/Corpse.tscn" },
		"Cargo": { src = "Props/Cargo.tscn" },
		"CityNode": { src = "Props/CityNode.tscn" },
		"FoodNode": { src = "Props/FoodNode.tscn" },
		# ---- 巨兽另两形态（2026-10-20）----
		# 同一副四足骨架、同一套动作名、同一档包围盒，差别在配色与体量 ——
		# 造型参数见 Tools/Gen.cs 的 BeastLooks 表。`lib = "beast"` 与 "Beast" 那条同一个开关
		# （走 clips_beast.gd 那座动作库）；骨名一字不差，所以三件的 idle / walk / run 是同一套轨道。
		#
		# **为什么排在这张表的最后**：导出器给每个含骨架的模型自己补一个
		# `_PhysicalBoneSimulator3D_<n>` 空节点（4.7 引擎自己加的，摘不掉），
		# 那个 `<n>` 是**本进程里建出来的第几个** —— 也就是**导出顺序**。
		# 往表中间插两条，后面每一个模型的 `.glb` 都会跟着改一个字节（那一位序号变了），
		# 于是「重跑逐字节相同」在整目录这一层就断了。排最后则前面 28 件一个字节都不动。
		"BeastDesert": { src = "Units/BeastDesert.tscn", lib = "beast", clips = BEAST_CLIPS },
		"BeastGlutton": { src = "Units/BeastGlutton.tscn", lib = "beast", clips = BEAST_CLIPS },
		# ---- T12 第二种原生怪（G-14）：与 Monster 同一副人形骨架、同一套七条动作 ----
		# 追加在既有各件之后：`_PhysicalBoneSimulator3D_<n>` 的序号按导出顺序数，
		# 不插队则上面各件的字节只受「新增 clip」影响（Monster 补 hit / emerge 属本 ticket 授权）。
		"MonsterBurrower": { src = "Units/MonsterBurrower.tscn", clips = MONSTER_GROUND },
		# ---- T11 建筑受损态 / 脚手架 / 废墟 / 搬运小件（ticket 12，只追加在表尾）----
		# 全是静态件不写 clips。清单与造型出处见 Tools/Gen.TownStates.cs 的 TownStatesRun()。
		"TownHallDamaged": { src = "Town/TownHallDamaged.tscn" },
		"ArenaDamaged": { src = "Town/ArenaDamaged.tscn" },
		"TrainingGroundDamaged": { src = "Town/TrainingGroundDamaged.tscn" },
		"StableDamaged": { src = "Town/StableDamaged.tscn" },
		"ArcheryRangeDamaged": { src = "Town/ArcheryRangeDamaged.tscn" },
		"WorkshopDamaged": { src = "Town/WorkshopDamaged.tscn" },
		"AirshipDockDamaged": { src = "Town/AirshipDockDamaged.tscn" },
		"HotelDamaged": { src = "Town/HotelDamaged.tscn" },
		"RestaurantDamaged": { src = "Town/RestaurantDamaged.tscn" },
		"GranaryDamaged": { src = "Town/GranaryDamaged.tscn" },
		"DormitoryDamaged": { src = "Town/DormitoryDamaged.tscn" },
		"ChurchDamaged": { src = "Town/ChurchDamaged.tscn" },
		"TeleportArrayDamaged": { src = "Town/TeleportArrayDamaged.tscn" },
		"ScaffoldSmall": { src = "Town/ScaffoldSmall.tscn" },
		"ScaffoldMedium": { src = "Town/ScaffoldMedium.tscn" },
		"ScaffoldLarge": { src = "Town/ScaffoldLarge.tscn" },
		"RuinSmall": { src = "Town/RuinSmall.tscn" },
		"RuinMedium": { src = "Town/RuinMedium.tscn" },
		"RuinLarge": { src = "Town/RuinLarge.tscn" },
		"OreChunk": { src = "Props/OreChunk.tscn" },
		"SpecialMaterial": { src = "Props/SpecialMaterial.tscn" },
		"SparePart": { src = "Props/SparePart.tscn" },
		"Crystal": { src = "Props/Crystal.tscn" },
		# ---- T10 二阶三形态 + 二阶底座/甲板（追加在表尾，不动既有行的位置）----
		# 二阶三件与一阶三件共用同一副四足骨架与同一组八条动作；位阶差在剪影
		# （体量、背峰、鞍板），造型参数见 Tools/Gen.cs 的 BeastLooks 表后三行。
		"BeastTier2": { src = "Units/BeastTier2.tscn", lib = "beast", clips = BEAST_CLIPS },
		"BeastDesertTier2": { src = "Units/BeastDesertTier2.tscn", lib = "beast", clips = BEAST_CLIPS },
		"BeastGluttonTier2": { src = "Units/BeastGluttonTier2.tscn", lib = "beast", clips = BEAST_CLIPS },
		# 二阶底座与二阶甲板：静态件不写 clips；尺寸来路见 Gen.Deck.cs 的两个 Tier2 Build。
		"LevitationBaseTier2": { src = "Town/LevitationBaseTier2.tscn" },
		"TownDeckTier2": { src = "Town/TownDeckTier2.tscn" },
		# ---- T14 大地图、生态、地下与船只（全部静态件，排在最末尾，理由同上）----
		# 生态四类 + 魔晶 + 远方高塔 + 两层地下五件套。造型参数见 Tools/Gen.World.cs。
		"TreePine": { src = "Props/TreePine.tscn" },
		"TreeBroadleaf": { src = "Props/TreeBroadleaf.tscn" },
		"OreNode": { src = "Props/OreNode.tscn" },
		"HerdBeast": { src = "Props/HerdBeast.tscn" },
		"CrystalShard": { src = "Props/CrystalShard.tscn" },
		"DistantTower": { src = "Props/DistantTower.tscn" },
		"SoilBlock": { src = "Props/SoilBlock.tscn" },
		"CavernTile": { src = "Props/CavernTile.tscn" },
		"CaveMouth": { src = "Props/CaveMouth.tscn" },
		"MiningFace": { src = "Props/MiningFace.tscn" },
		"CrystalSocket": { src = "Props/CrystalSocket.tscn" },
		"GroundExit": { src = "Props/GroundExit.tscn" },
		# ---- T13 三城地标与地表（18 件静态件，追加在表尾；生态五件由 T14 提供）----
		# 静态件不写 clips；三座主地标 / 每城四件背景建筑 / 三套地表。
		"LandmarkPoor": { src = "Props/LandmarkPoor.tscn" },
		"LandmarkSand": { src = "Props/LandmarkSand.tscn" },
		"LandmarkRich": { src = "Props/LandmarkRich.tscn" },
		"BackPoorA": { src = "Props/BackPoorA.tscn" },
		"BackPoorB": { src = "Props/BackPoorB.tscn" },
		"BackPoorC": { src = "Props/BackPoorC.tscn" },
		"BackPoorD": { src = "Props/BackPoorD.tscn" },
		"BackSandA": { src = "Props/BackSandA.tscn" },
		"BackSandB": { src = "Props/BackSandB.tscn" },
		"BackSandC": { src = "Props/BackSandC.tscn" },
		"BackSandD": { src = "Props/BackSandD.tscn" },
		"BackRichA": { src = "Props/BackRichA.tscn" },
		"BackRichB": { src = "Props/BackRichB.tscn" },
		"BackRichC": { src = "Props/BackRichC.tscn" },
		"BackRichD": { src = "Props/BackRichD.tscn" },
		"GroundPoor": { src = "Props/GroundPoor.tscn" },
		"GroundSand": { src = "Props/GroundSand.tscn" },
		"GroundRich": { src = "Props/GroundRich.tscn" },
}
