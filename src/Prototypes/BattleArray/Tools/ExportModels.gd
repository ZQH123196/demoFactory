extends RefCounted

# 排兵布阵 · 模型导出登记表（开发期工具，不进游戏、不被 csproj 编译）。
#
# 为什么要有这个文件：导出器 src/GugaFramework/Tools/glb/export_glb.gd 必须在引擎里跑
# （CSG 合并网格与 GLTFDocument 导出都是引擎内的活），C# 侧那套 IPrototypeTools 反射够不到它。
# 于是「换原型只改表」这条落在 GDScript 侧：导出器带 --proto=BattleArray 时来 load 本文件，
# 三张表（源目录 / 产物目录 / 模型清单）全在本原型手里，公共层不动一行。
#
# 跑法（项目根）：
#   godot --headless --path . --quit-after 900 \
#     --script res://src/GugaFramework/Tools/glb/export_glb.gd -- --proto=BattleArray
#   尾参再加 -- --only=HumanWarrior 只出一个；-- --list 只列本表
#
# 表里的 clips 名单来自 src/GugaFramework/Tools/glb/clips_humanoid.gd（出手与四条技能前摇的起手动作全在那），
# 出手动作按兵种二选一：近战 attack_melee、弓手 attack_shoot。
# 起手动作每个兵种一条，与技能表里那条有前摇的技能一一对应（warrior.rift / tank.slam / archer.pierce /
# rider.trample —— 谁挂号看 Scripts/Logic/SkillWindups.cs 那一份表，别在这里各写一遍）。
# 源场景路径相对 src_root()，约定面朝 +Z、脚在 y=0，j_ 前缀是骨骼契约。

const LOCOMOTION := ["idle", "walk", "run"]

## 每个兵种一条起手动作（技能前摇的动画载体）。名字与时长定在 clips_humanoid.gd / clips_mount.gd，
## 「哪条技能挂哪条」那一份表在 Scripts/Logic/SkillWindups.cs —— 这里只负责把它塞进对应模型的 glb。
## 两族同骨架同动作库，所以人族兽人各挂同一条名。
const WARRIOR_WINDUP := ["windup_slash"]
const TANK_WINDUP := ["windup_slam"]
const ARCHER_WINDUP := ["windup_pierce"]
const RIDER_WINDUP := ["windup_trample"]


static func src_root() -> String:
	return "res://src/Prototypes/BattleArray/Tools/Rigs/"


static func out_root() -> String:
	return "res://src/Prototypes/BattleArray/Models/"


## 已建成的模型。八张兵种卡的名单与命名定在
## docs/prototypes/battle-array/design.md 2.2 节，M2 / M3 每建一个在这里加一行
## （同时在 Tools/Gen.cs 的 Run() 里加一个 Build*()）——两边一起动，漏一边导不出。
static func models() -> Dictionary:
	return {
		# M1 的接通探针：人族 · 剑士（M2 按体检单返修三处造型缺陷）
		"HumanWarrior": { src = "Units/HumanWarrior.tscn", clips = LOCOMOTION + ["attack_melee", "death"] + WARRIOR_WINDUP },
		# M2 人族其余两条（骑兵要坐骑骨架，单独带 lib = "mount"，见下）
		"HumanTank": { src = "Units/HumanTank.tscn", clips = LOCOMOTION + ["attack_melee", "death"] + TANK_WINDUP },
		"HumanArcher": { src = "Units/HumanArcher.tscn", clips = LOCOMOTION + ["attack_shoot", "death"] + ARCHER_WINDUP },
		# 骑手：身上挂着**两副骨架**（坐骑 m_* ＋ 骑手人形），动作走骑乘库而不是人形库
		# —— lib = "mount" 就是这件事的开关，见 src/GugaFramework/Tools/glb/clips_mount.gd 文件头。
		"HumanRider": {
			src = "Units/HumanRider.tscn", lib = "mount",
			clips = LOCOMOTION + ["attack_melee", "death"] + RIDER_WINDUP
		},
		# M3 兽人四条：同骨架改比例与配色 + 一条种族特征（獠牙），命名与 2.2 节的对称槽位一致
		"OrcTank": { src = "Units/OrcTank.tscn", clips = LOCOMOTION + ["attack_melee", "death"] + TANK_WINDUP },
		"OrcWarrior": { src = "Units/OrcWarrior.tscn", clips = LOCOMOTION + ["attack_melee", "death"] + WARRIOR_WINDUP },
		"OrcArcher": { src = "Units/OrcArcher.tscn", clips = LOCOMOTION + ["attack_shoot", "death"] + ARCHER_WINDUP },
		"OrcRider": {
			src = "Units/OrcRider.tscn", lib = "mount",
			clips = LOCOMOTION + ["attack_melee", "death"] + RIDER_WINDUP
		},
	}
