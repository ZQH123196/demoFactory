extends Node
class_name BattleBrain

## 一个兵一座行为树宿主 —— 树的驱动、参数挂号、行为流出口都在这一个文件里。
##
## 这条线是谁（AGENTS「行为树（Beehave）」）：树的结构用 vendored 的 GDScript 组合件，
## 决策与规则用能被 dotnet test 复现的纯 C#（Logic/BattleBrainPolicy.cs），
## 中间的 C# 叶子（AI/Battle*Task.cs）只搬参数。本文件就是那棵树的宿主。
##
## 与无限领主那条线的一处刻意不同：那边宿主收 `Stepped` 信号后自己喊 `tick()`，
## 意图在**下一步**被世界执行；这边是**世界在同一步里同步问树**（表现层把
## BattleWorld.Brain 指到桥的 DecideFor，桥再回来喊 tick_for_bridge）。
## 为什么：M4 的判绿建立在「离线跑与运行时逐字同一局」上，同步问就不存在
## 「哪一步的意图被哪一步执行」这本二次账，决策顺序仍是出场顺序。
## 将来真要引入一步延迟（技能前摇、特殊兵种的多段决策），改的是 BattleWorld.Brain 这一个口子。

var bridge: Node
## 不做成 @export、也不写成桥的类型：手写 .tscn 里 @export 的节点引用取回来是 null
## （实测，坑记在 docs/GugaFramework/行为树与跨语言共存.md第 5 条），
## 引用由表现层挂树时 Set("bridge", ...) 注入。

## 射程覆盖（米）。-1 = 不覆盖，用兵种卡上的射程。
## 这一格就是「同一棵树、不同 .tscn 参数」的落点：Scenes/Brains/<兵种>.tscn 各写一档。
## 写了就必须与兵种卡相等（有测试逐字核对，Scenes 与 UnitCards 不许漂成两处真值）。
## M8 已经按种族改过一轮：人族那条规则是射程 ×1.1 ⇒ 人族四档写 1.76/1.98/9.9/2.2，
## 兽人四档照基准表 1.6/1.8/9.0/2.0 不动 —— 改的只有这些场景档与兵种卡，逻辑层一行没动。
@export var range_override := -1.0

## 行为流采样：多少步打一行（50 步 = 5 秒）。30 个兵一步一行会淹掉日志，
## 调试要细看某个兵时调 <c>Main.SetBrainLogEvery(n)</c> 现改。0 = 不打。
@export var log_every_steps := 50

## 行为流里这个名字（空 = 用兵种卡的中文名）。场景档写死，省得每行现查。
@export var brain_label := ""

var _tree: BeehaveTree
var _step := 0
var _stopped := false


func _ready() -> void:
	assert(bridge != null, "BattleBrain 没被注入 bridge 引用（表现层挂树时要 Set(\"bridge\", ...)）")
	_tree = get_node("BT") as BeehaveTree
	_tree.process_thread = BeehaveTree.ProcessThread.MANUAL
	_report_params()


## 桥喊这里：走一步树。叶子在 `tick()` 里认账并把决策交回桥，桥再把意图交给世界执行。
func tick_for_bridge() -> void:
	_step += 1
	if _stopped:
		return
	if not bridge.call("SenseAlive"):
		# 人不在了就停手：再驱动下去叶子读到的观测全是 0，行为流会把死人写成活人
		# （无限领主实测连刷过 150 步「血量 0/0，随便走走」，教训照抄）。
		# 这一行本身要留在行为流里 —— 「他什么时候不再听树的」是关键事件，不能静默。
		# 现在这条正常不触发：世界在 Step 里对死人直接 continue，根本不会来问。
		# 留着是因为决策口（BattleWorld.Brain）是外面装的 —— M7/M8 改了调用顺序时，
		# 宁可它当场在行为流里喊一句，也不要安静地把死人写成活人。
		_stopped = true
		print("step %d | %s | 离场 | 人已经不在场，树停手" % [_step, _who()])
		return
	_tree.tick()
	if log_every_steps > 0 and _step % log_every_steps == 0:
		print("step %d | %s | %s | %s | 目标 %s | hp %d/%d" % [
			_step, _who(),
			bridge.call("SenseDecisionIntent"), bridge.call("SenseDecisionWhy"),
			_target_text(),
			bridge.call("SenseHealth"), bridge.call("SenseMaxHealth"),
		])


## 这一回合他认的靶子是谁、隔多远 ——「为什么还不去打后排」的答案就在这一格：
## 规则只有「打最近」那一条（design.md 2 节 第 3 条），后排有人挡着时轮不到远处的。
## 规则不在这里重写：意图与那句 why 都来自 C# 的 BattleBrainPolicy（显示即真相一份实现）。
func _target_text() -> String:
	var tid: String = String(bridge.call("SenseTargetId"))
	if tid == "":
		return "无人（对面已没活人）"
	return "%s 隔 %.2f 米" % [tid, float(bridge.call("SenseDistance"))]


func _who() -> String:
	var label: String = brain_label if brain_label != "" else String(bridge.call("SenseDisplayName"))
	return "%s %s" % [String(bridge.call("SenseUnitId")), label]


## 可调标量的挂号处：宿主新增了什么参数，这里加一行，日志才会把它打出来。
const PARAMS := ["range_override", "log_every_steps", "brain_label"]


## 开局把**实际生效**的参数打一遍（一次，不在行为流里），并当场比对兵种卡。
## 为什么要打：这些值有两个来源 —— C# 代码里的默认值，和 .tscn 里写的覆盖值。
## 只看代码读不出这个兵此刻用的是哪一套，而「同一棵树、不同单位不同参数」正是靠场景覆盖做出来的。
## 冲突必须喊出来：场景写了射程、兵种卡又是另一个数，就是两处真值，谁赢取决于谁被读到 —— 不可接受。
func _report_params() -> void:
	var parts: Array[String] = []
	for prop in PARAMS:
		parts.append("%s=%s" % [prop, get(prop)])
	var card_range: float = bridge.call("SenseCardRange")
	print("参数 | %s | %s | 兵种卡射程=%.2f" % [_who(), " ".join(parts), card_range])
	if range_override >= 0.0 and not is_equal_approx(range_override, card_range):
		push_error("参数冲突 | %s | 场景射程覆盖 %.2f ≠ 兵种卡 %.2f —— 场景档与数据表漂了，" % [
			_who(), range_override, card_range])
