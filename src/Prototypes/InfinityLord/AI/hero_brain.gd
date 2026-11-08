extends Node
class_name HeroBrain

## 游戏内的行为树宿主：一座桥对一名英雄，逻辑帧时间每推进一步（0.1 秒）收到一次 Stepped 信号，
## 就驱动树走一遍；树写回的意图在这一步的决策段就被 LogicWorld 读走（同一步生效）。
##
## 宿主不分决策用哪种语言：早期那份 GDScript 叶子版（对照组）与 C# 任务版共用本文件、
## 共用 log_decision 的口径。对照组已在 2026-09-25 删掉 —— 它只有三条支路、没有游荡与离场校验，
## 与现在这棵五条支路的树已经不可比（要回来看：git 提交 682c461）。
## C# 侧的账见 docs/GugaFramework/行为树与跨语言共存.md一节。

var bridge: Node
## 刻意不做成 @export、也不写成 HeroBrainBridge 类型：手写 .tscn 里 @export 的节点引用
## 取回来是 null（实测，坑记在docs/GugaFramework/行为树与跨语言共存.md第 5 条），
## 引用由表现层在挂树时 Set("bridge", ...) 注入。

## 实验剧本：交战中的某一步把这名英雄的血打到只剩 injure_to_ratio 的上限。
## 为什么要剧本 —— 真游戏里他清完视野内的敌人就站着不再挨打，血线自然跌不到逃跑阈值，
## 「逃跑」那一态就录不进行为流。等交上了手才动手：没敌人时逃也无从逃起。
## 剧本挂在**树上**而不是挂在人身上：换实现语言不用把实验条件再抄一遍，
## 记录才可能对齐（现在这份宿主只有 C# 任务版在用）。
## 写比例不写数额：英雄可能已升级把上限顶高，定额要么打不死要么一巴掌打死。
## 起点取 80 步（= 8 秒）：早于交火（实测 5.1 秒才照面）会让三态只剩逃跑，一局里要连出
## 追击 → 攻击 → 逃跑 才算把这三条与敌人相关的支路都录进同一条流
## （两条游荡支路不用剧本 —— 开局第 0.1 秒眼前没敌人就开始刷了）。
@export var injure_after_step := 80
@export var injure_to_ratio := 0.25

var _tree: BeehaveTree
var _step := 0
var _injured := false
var _stopped := false


func _ready() -> void:
	assert(bridge != null, "HeroBrain 没被注入 bridge 引用")
	_tree = get_node("BT") as BeehaveTree
	_tree.process_thread = BeehaveTree.ProcessThread.MANUAL
	bridge.connect("Stepped", _on_stepped)
	_report_params()


## 可调标量的挂号处：叶子新增了什么参数，这里加一行，日志才会把它打出来。
const PARAMS := {
	"宿主": ["injure_after_step", "injure_to_ratio"],
	"Flee": ["RatioBelow", "SafeDistance"],
	"HurtWander": ["RatioBelow"],
}


## 开局把**实际生效**的标量参数打一遍（一次，不在行为流里）。
## 为什么要打：这些值有两个来源——C# 代码里的默认值，和 .tscn 里写的覆盖值（两条路实测可通：
## 场景写 RatioBelow=0.42 就读回 0.42，删掉那行就退回 0.3，且行为流跟着变）。
## 只看代码读不出这名单位此刻用的是哪一套，而「同一棵树、不同单位不同参数」正是靠场景覆盖做出来的。
func _report_params() -> void:
	for label in PARAMS:
		var node: Node = self if label == "宿主" else get_node_or_null("BT/Selector/" + label)
		if node == null:
			print("参数 | %s | 节点不在树上（改树了要同步 PARAMS）" % label)
			continue
		var parts: Array[String] = []
		for prop in PARAMS[label]:
			parts.append("%s=%s" % [prop, node.get(prop)])
		print("参数 | %s | %s" % [label, " ".join(parts)])


func _on_stepped() -> void:
	_step += 1
	if _stopped:
		return
	if not bridge.call("SenseAlive"):
		# 人不在了（阵亡 / 被行会开除 / 离场）就停手：再驱动下去叶子读到的观测全是 0，
		# 行为流会把死人写成活人（实测连刷 150 步、约 15 秒「血量 0/0，随便走走」）。
		# 这一行本身要留在行为流里 —— 「他什么时候不再听树的」是关键事件，不能静默。
		_stopped = true
		print("step %d | hero %s | 离场 | 人已经不在场，树停手" % [_step, bridge.call("SenseHeroId")])
		return
	_maybe_injure()
	_tree.tick()


func _maybe_injure() -> void:
	if _injured or _step < injure_after_step:
		return
	var enemy_id: String = bridge.call("SenseNearestEnemyId")
	if enemy_id.is_empty():
		return
	var hp: int = bridge.call("SenseHealth")
	var max_hp: int = bridge.call("SenseMaxHealth")
	var floor_hp: int = int(max_hp * injure_to_ratio)
	if hp <= floor_hp:
		return
	bridge.call("DebugHurt", hp - floor_hp)
	_injured = true
	print("step %d | 剧本 | 扣血 %d → %d/%d，低于 %.0f%% 就该转逃跑"
			% [_step, hp - floor_hp, floor_hp, max_hp, (1.0 - injure_to_ratio) * 100.0])


## 行为流的唯一出口：树里每个 C# 任务决策完都到这里落一行，别另起一处 print（口径一分叉，事后就没法读）。
func log_decision(what: String, detail: String) -> void:
	var hero_id: String = bridge.call("SenseHeroId")
	var hp: int = bridge.call("SenseHealth")
	var max_hp: int = bridge.call("SenseMaxHealth")
	print("step %d | hero %s | %s | %s | hp %d/%d" % [_step, hero_id, what, detail, hp, max_hp])
