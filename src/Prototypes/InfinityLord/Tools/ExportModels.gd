extends RefCounted

# 无限领主 · 模型导出登记表（开发期工具，不进游戏、不被 csproj 编译）。
#
# 2026-09-26 从 src/GugaFramework/Tools/glb/export_glb.gd 的文件内 const 表搬过来：
# 那三张表（源目录 / 产物目录 / 模型清单）是无限领主的数据，不是公共机制，
# 留在导出器里等于「公共层带一个原型的默认值」。搬完之后公共层只剩机制，
# --proto=<目录名> 成为必填参数 —— 与 C# 侧 IPrototypeTools 反射发现同一个口径。
#
# 跑法（项目根）：
#   godot --headless --path . --quit-after 900 \
#     --script res://src/GugaFramework/Tools/glb/export_glb.gd -- --proto=InfinityLord
#   尾参再加 -- --only=Ranger 只出一个；-- --list 只列本表
#
# 表里的 clips 名单来自 src/GugaFramework/Tools/glb/clips_humanoid.gd（六个动作全在那）。
# 会动的（单位）写 clips → 骨架 + 刚性蒙皮 + 动作；不写的（建筑/道具）→ 无骨架单网格。
# 源场景路径相对 src_root()，约定面朝 +Z、脚在 y=0，j_ 前缀是骨骼契约。

const LOCOMOTION := ["idle", "walk", "run"]


static func src_root() -> String:
	return "res://src/Prototypes/InfinityLord/Tools/Rigs/"


static func out_root() -> String:
	return "res://src/Prototypes/InfinityLord/Models/"


## 17 个已验收模型（2026-09-25 全部过了体检单）。加一个单位/建筑在这里加一行，
## 同时在 Tools/Gen.cs 的 Run() 里加一个 Build*()、Tools/Uids.cs 里登记 uid —— 三边一起动，漏一边导不出。
static func models() -> Dictionary:
	return {
		"Ranger": { src = "Units/Ranger.tscn", clips = LOCOMOTION + ["attack_shoot", "death"] },
		"Warrior": { src = "Units/Warrior.tscn", clips = LOCOMOTION + ["attack_melee", "death"] },
		"Knight": { src = "Units/Knight.tscn", clips = LOCOMOTION + ["attack_melee", "death"] },
		"Goblin": { src = "Units/Goblin.tscn", clips = LOCOMOTION + ["attack_melee", "death"] },
		"GoblinElite": { src = "Units/GoblinElite.tscn", clips = LOCOMOTION + ["attack_melee", "death"] },
		"GoblinBrute": { src = "Units/GoblinBrute.tscn", clips = LOCOMOTION + ["attack_melee", "death"] },
		"BossGuard": { src = "Units/BossGuard.tscn", clips = LOCOMOTION + ["attack_melee", "death"] },
		"Troll": { src = "Units/Troll.tscn", clips = LOCOMOTION + ["attack_melee", "death"] },
		"Palace": { src = "Buildings/Palace.tscn" },
		"GuildHall": { src = "Buildings/GuildHall.tscn" },
		"GuildWarrior": { src = "Buildings/GuildWarrior.tscn" },
		"Market": { src = "Buildings/Market.tscn" },
		"Tavern": { src = "Buildings/Tavern.tscn" },
		"Apothecary": { src = "Buildings/Apothecary.tscn" },
		"MonsterLair": { src = "Buildings/MonsterLair.tscn" },
		"BossLair": { src = "Buildings/BossLair.tscn" },
		"BountyFlag": { src = "Props/BountyFlag.tscn" },
	}
