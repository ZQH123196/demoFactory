extends RefCounted

# 骑乘动作库 —— 「一个人骑在一只四足坐骑上」这一类模型专用的 clip 表。
#
# 为什么另开一座库而不是往 clips_humanoid.gd 里塞：骑手模型有**两副骨架挂在同一棵树上**
# （骑手的 hips/chest/armR… + 坐骑的 m_hips/m_legFL…），而人形那六条轨道是按人形骨名写的。
# 直接套用人形 walk 会让骑手在马上蹬腿，砍掉腿又会让坐骑四条腿站着滑行 ——
# 两头都不像"骑"。所以这座库做的事只有一句：**坐骑的轨道自己写，骑手的轨道从人形库里挑着搬**。
#
# 挑的标准（每条 clip 后面注明留了哪些骨）：
#   · 骑手的大腿/小腿/脚一律不留 —— 骑乘姿态写在绑定姿势里（Humanoid.Rig 的 thighYaw/kneeYaw），
#     轨道是「相对 rest 的旋转」，一动就把跨在马上的人形腿甩出去蹬自行车；
#   · 躯干、头、手臂照搬：这些在马上也是这么动的（托弓、劈砍、被颠得前后晃）。
#
# 轴口径与 clips_humanoid.gd 完全一致：下垂的部件 rx 负 ＝ 向前摆，rx 正 ＝ 向后；
# 骨名前缀 m_ 是坐骑（见 src/Tools/QuadrupedRig.cs），不带前缀的是骑手。

const Human := preload("res://src/GugaFramework/Tools/glb/clips_humanoid.gd")

# 骑手身上「照搬人形轨道」的那几根骨 —— 腿与脚一律不在里面，理由见文件头。
const RIDER_UPPER := ["hips", "spine", "chest", "neck", "head", "shoulderL", "armL", "shoulderR", "armR"]


static func build(clip: String) -> Dictionary:
	match clip:
		"idle":
			return _idle()
		"walk":
			return _walk()
		"run":
			return _run()
		"attack_melee":
			return _attack_melee()
		# 骑手践踏（`rider.trample`）的前摇载体：坐骑后蹲→前冲压蹄，骑手那半从人形库照搬上半身。
		# 时长 0.7 秒 > 前摇 0.2 秒，理由见 clips_humanoid.gd 里那四条起手动作的文件注释。
		"windup_trample":
			return _windup_trample()
		"death":
			return _death()
	return {}


static func _track(bone: String, keys: Array) -> Array:
	# keys: [[时间秒, rx, ry, rz], ...]
	return [bone, keys]


static func _sine(steps: float, dur: float, amp: float, phase: float) -> Array:
	var keys := []
	var n := int(ceil(dur / steps))
	for i in n + 1:
		var t := minf(float(i) * steps, dur)
		keys.append([t, amp * sin(PI * 2.0 * (t / dur) + phase), 0.0, 0.0])
	return keys


## 从人形库里挑骨搬轨道（骨名与坐骑不撞，所以原样搬）
static func _rider(clip: String, keep: Array = RIDER_UPPER) -> Array:
	var out := []
	for t in Human.build(clip).tracks:
		if keep.has(String(t[0])):
			out.append(t)
	return out


static func _idle() -> Dictionary:
	var d := 3.6
	return {
		"length": d, "loop": true,
		"tracks": [
			# 站着喘：背线一涨一落，脖子慢慢抬放，尾巴甩两下赶虫
			_track("m_spine", [[0, 0, 0, 0], [d * 0.5, -1.6, 0, 0], [d, 0, 0, 0]]),
			_track("m_hips", [[0, 0, 0, 0], [d * 0.5, 1.2, 0, 0], [d, 0, 0, 0]]),
			_track("m_neck", [[0, 0, 0, 0], [d * 0.33, -5, 0, 0], [d * 0.7, 3, 6, 0], [d, 0, 0, 0]]),
			_track("m_head", [[0, 0, 0, 0], [d * 0.25, -6, 12, 0], [d * 0.6, 4, -14, 0], [d, 0, 0, 0]]),
			_track("m_tail", [[0, 0, 0, 0], [d * 0.18, 0, 14, 0], [d * 0.38, 0, -12, 0],
					[d * 0.62, 0, 9, 0], [d, 0, 0, 0]]),
			# 四条腿只换重心（±1.5°）：站着不动但没僵住
			_track("m_legFL", _sine(d / 4.0, d, 1.5, 0.0)),
			_track("m_legFR", _sine(d / 4.0, d, 1.5, PI)),
			_track("m_legBL", _sine(d / 4.0, d, 1.2, PI * 0.5)),
			_track("m_legBR", _sine(d / 4.0, d, 1.2, PI * 1.5)),
		] + _rider("idle"),
	}


static func _walk() -> Dictionary:
	var d := 1.15
	# 慢步是四步落脚：左后 → 左前 → 右后 → 右前，相位各错开四分之一周期
	var ph := {"BL": 0.0, "FL": PI * 0.5, "BR": PI, "FR": PI * 1.5}
	return {
		"length": d, "loop": true,
		"tracks": [
			_track("m_legBL", _sine(0.1, d, 15.0, ph["BL"])),
			_track("m_legFL", _sine(0.1, d, 16.0, ph["FL"])),
			_track("m_legBR", _sine(0.1, d, 15.0, ph["BR"])),
			_track("m_legFR", _sine(0.1, d, 16.0, ph["FR"])),
			# 膝/飞节只往后收（rx 正），且离地相才弯
			_track("m_kneeFL", [[0, 4, 0, 0], [d * 0.25, 20, 0, 0], [d * 0.5, 6, 0, 0],
					[d * 0.75, 3, 0, 0], [d, 4, 0, 0]]),
			_track("m_kneeFR", [[0, 6, 0, 0], [d * 0.25, 3, 0, 0], [d * 0.5, 4, 0, 0],
					[d * 0.75, 20, 0, 0], [d, 6, 0, 0]]),
			_track("m_kneeBL", [[0, 10, 0, 0], [d * 0.25, 4, 0, 0], [d * 0.5, 16, 0, 0],
					[d * 0.75, 6, 0, 0], [d, 10, 0, 0]]),
			_track("m_kneeBR", [[0, 16, 0, 0], [d * 0.25, 6, 0, 0], [d * 0.5, 10, 0, 0],
					[d * 0.75, 4, 0, 0], [d, 16, 0, 0]]),
			_track("m_hoofFL", [[0, -4, 0, 0], [d * 0.3, 10, 0, 0], [d, -4, 0, 0]]),
			_track("m_hoofFR", [[0, 10, 0, 0], [d * 0.5, -4, 0, 0], [d * 0.8, 10, 0, 0], [d, 10, 0, 0]]),
			_track("m_hoofBL", [[0, -3, 0, 0], [d * 0.35, 8, 0, 0], [d, -3, 0, 0]]),
			_track("m_hoofBR", [[0, 8, 0, 0], [d * 0.6, -3, 0, 0], [d, 8, 0, 0]]),
			# 背随步伐起伏 + 一点侧摆；头颈反向平衡
			_track("m_spine", [[0, 2, 0, 0], [d * 0.25, 0, 2, 0], [d * 0.5, -2, 0, 0],
					[d * 0.75, 0, -2, 0], [d, 2, 0, 0]]),
			_track("m_chest", [[0, -2, 0, 0], [d * 0.5, 2, 0, 0], [d, -2, 0, 0]]),
			_track("m_neck", [[0, -3, 0, 0], [d * 0.25, -1, 3, 0], [d * 0.5, -4, 0, 0],
					[d * 0.75, -1, -3, 0], [d, -3, 0, 0]]),
			_track("m_head", [[0, 4, 0, 0], [d * 0.25, 1, 4, 0], [d * 0.5, 6, 0, 0],
					[d * 0.75, 1, -4, 0], [d, 4, 0, 0]]),
			_track("m_tail", [[0, 0, 6, 0], [d * 0.5, 0, -6, 0], [d, 0, 6, 0]]),
		] + _rider("walk", ["hips", "spine", "chest", "head"]),
	}


static func _run() -> Dictionary:
	var d := 0.66
	# 跑步按两步落脚（猎兽的 gallop 简化）：前腿一对、后腿一对，后腿先推、前腿跟着落
	return {
		"length": d, "loop": true,
		"tracks": [
			_track("m_legFL", [[0, -30, 0, 0], [d * 0.25, -6, 0, 0], [d * 0.5, 26, 0, 0],
					[d * 0.75, 4, 0, 0], [d, -30, 0, 0]]),
			_track("m_legFR", [[0, -26, 0, 0], [d * 0.25, -2, 0, 0], [d * 0.5, 24, 0, 0],
					[d * 0.75, 6, 0, 0], [d, -26, 0, 0]]),
			_track("m_legBL", [[0, 24, 0, 0], [d * 0.25, 2, 0, 0], [d * 0.5, -28, 0, 0],
					[d * 0.75, -6, 0, 0], [d, 24, 0, 0]]),
			_track("m_legBR", [[0, 26, 0, 0], [d * 0.25, 4, 0, 0], [d * 0.5, -30, 0, 0],
					[d * 0.75, -4, 0, 0], [d, 26, 0, 0]]),
			_track("m_kneeFL", [[0, 30, 0, 0], [d * 0.3, 8, 0, 0], [d * 0.6, 52, 0, 0], [d, 30, 0, 0]]),
			_track("m_kneeFR", [[0, 26, 0, 0], [d * 0.3, 6, 0, 0], [d * 0.6, 48, 0, 0], [d, 26, 0, 0]]),
			_track("m_kneeBL", [[0, 8, 0, 0], [d * 0.35, 44, 0, 0], [d * 0.7, 14, 0, 0], [d, 8, 0, 0]]),
			_track("m_kneeBR", [[0, 6, 0, 0], [d * 0.35, 40, 0, 0], [d * 0.7, 12, 0, 0], [d, 6, 0, 0]]),
			_track("m_hoofFL", [[0, -14, 0, 0], [d * 0.4, 16, 0, 0], [d, -14, 0, 0]]),
			_track("m_hoofFR", [[0, -12, 0, 0], [d * 0.4, 14, 0, 0], [d, -12, 0, 0]]),
			_track("m_hoofBL", [[0, 14, 0, 0], [d * 0.5, -12, 0, 0], [d, 14, 0, 0]]),
			_track("m_hoofBR", [[0, 12, 0, 0], [d * 0.5, -10, 0, 0], [d, 12, 0, 0]]),
			# 跑步靠背伸缩：脊柱前后弓（这是"跑起来"读得出来的主因），颈反向
			_track("m_spine", [[0, 7, 0, 0], [d * 0.5, -8, 0, 0], [d, 7, 0, 0]]),
			_track("m_chest", [[0, -6, 0, 0], [d * 0.5, 6, 0, 0], [d, -6, 0, 0]]),
			_track("m_hips", [[0, 5, 0, 0], [d * 0.5, -5, 0, 0], [d, 5, 0, 0]]),
			_track("m_neck", [[0, 6, 0, 0], [d * 0.5, -7, 0, 0], [d, 6, 0, 0]]),
			_track("m_head", [[0, -8, 0, 0], [d * 0.5, 7, 0, 0], [d, -8, 0, 0]]),
			_track("m_tail", [[0, -10, 8, 0], [d * 0.5, -4, -8, 0], [d, -10, 8, 0]]),
		] + _rider("run", ["hips", "spine", "chest", "head"]),
	}


static func _attack_melee() -> Dictionary:
	var d := 1.0
	# 骑砍：骑手举刀劈下（人形轨道照搬手臂与躯干），坐骑同时往前一冲、脖子压低
	return {
		"length": d, "loop": false,
		"tracks": [
			_track("m_spine", [[0, 0, 0, 0], [d * 0.42, -7, 0, 0], [d * 0.6, 4, 0, 0], [d, 0, 0, 0]]),
			_track("m_chest", [[0, 0, 0, 0], [d * 0.42, -6, -6, 0], [d * 0.6, 3, 3, 0], [d, 0, 0, 0]]),
			_track("m_neck", [[0, 0, 0, 0], [d * 0.42, 14, 0, 0], [d * 0.6, -4, 0, 0], [d, 0, 0, 0]]),
			_track("m_head", [[0, 0, 0, 0], [d * 0.42, 10, 0, 0], [d, 0, 0, 0]]),
			_track("m_legFL", [[0, 0, 0, 0], [d * 0.42, -12, 0, 0], [d * 0.7, 6, 0, 0], [d, 0, 0, 0]]),
			_track("m_legFR", [[0, 0, 0, 0], [d * 0.42, -10, 0, 0], [d * 0.7, 5, 0, 0], [d, 0, 0, 0]]),
			_track("m_legBL", [[0, 0, 0, 0], [d * 0.42, 12, 0, 0], [d, 0, 0, 0]]),
			_track("m_legBR", [[0, 0, 0, 0], [d * 0.42, 10, 0, 0], [d, 0, 0, 0]]),
		] + _rider("attack_melee"),
	}


static func _windup_trample() -> Dictionary:
	# 践踏：0.2 秒（前摇结束 = commit）时坐骑后腿蹲深、骑手举刃到顶，0.4 秒整体前冲把重量砸到前蹄上，
	# 0.55 秒起、0.7 秒收。骑手上半身轨道直接从人形库那条同名 clip 挑（腿跨在马上，不留）。
	var d := 0.7
	return {
		"length": d, "loop": false,
		"tracks": [
			_track("m_spine", [[0, 0, 0, 0], [0.2, 8, 0, 0], [0.4, -12, 0, 0], [0.55, -4, 0, 0], [d, 0, 0, 0]]),
			_track("m_chest", [[0, 0, 0, 0], [0.2, 6, 0, 0], [0.4, -10, -8, 0], [d, 0, 0, 0]]),
			_track("m_neck", [[0, 0, 0, 0], [0.2, 16, 0, 0], [0.4, -18, 0, 0], [0.55, 6, 0, 0], [d, 0, 0, 0]]),
			_track("m_head", [[0, 0, 0, 0], [0.2, 12, 0, 0], [0.4, -14, 0, 0], [d, 0, 0, 0]]),
			_track("m_legFL", [[0, 0, 0, 0], [0.2, -26, 0, 0], [0.4, 18, 0, 0], [0.55, 6, 0, 0], [d, 0, 0, 0]]),
			_track("m_legFR", [[0, 0, 0, 0], [0.2, -24, 0, 0], [0.4, 16, 0, 0], [0.55, 5, 0, 0], [d, 0, 0, 0]]),
			_track("m_legBL", [[0, 0, 0, 0], [0.2, 14, 0, 0], [0.4, -12, 0, 0], [d, 0, 0, 0]]),
			_track("m_legBR", [[0, 0, 0, 0], [0.2, 12, 0, 0], [0.4, -10, 0, 0], [d, 0, 0, 0]]),
		] + _rider("windup_trample"),
	}


static func _death() -> Dictionary:
	var d := 2.2
	# 坐骑前腿先折、胸口塌下去，骑手跟着被掀向后 —— 人形侧只留躯干与手臂（腿跨在马上，不能蹬）
	return {
		"length": d, "loop": false,
		"tracks": [
			_track("m_legFL", [[0, 0, 0, 0], [d * 0.22, 46, 0, 0], [d * 0.55, 62, 0, 0], [d, 66, 0, 0]]),
			_track("m_legFR", [[0, 0, 0, 0], [d * 0.22, 42, 0, 0], [d * 0.55, 58, 0, 0], [d, 62, 0, 0]]),
			_track("m_kneeFL", [[0, 4, 0, 0], [d * 0.22, 40, 0, 0], [d, 54, 0, 0]]),
			_track("m_kneeFR", [[0, 6, 0, 0], [d * 0.22, 36, 0, 0], [d, 50, 0, 0]]),
			_track("m_legBL", [[0, 0, 0, 0], [d * 0.4, -22, 0, 0], [d, -30, 0, 0]]),
			_track("m_legBR", [[0, 0, 0, 0], [d * 0.4, -18, 0, 0], [d, -26, 0, 0]]),
			_track("m_spine", [[0, 0, 0, 0], [d * 0.4, 16, 0, 0], [d, 22, 0, 0]]),
			_track("m_chest", [[0, 0, 0, 0], [d * 0.4, 20, 0, 0], [d, 28, 0, 0]]),
			_track("m_neck", [[0, 0, 0, 0], [d * 0.4, 26, 0, 0], [d, 40, 0, 0]]),
			_track("m_head", [[0, 0, 0, 0], [d * 0.5, 22, 0, 0], [d, 34, 6, 0]]),
			_track("m_tail", [[0, 0, 0, 0], [d * 0.5, 18, 0, 0], [d, 26, 0, 0]]),
			# 骑手被掀向后：座位关节一倒，挂在它下面的人形骨架整副跟着倒（这就是"摔下马"）
			_track("m_saddle", [[0, 0, 0, 0], [d * 0.3, -18, 0, 8], [d * 0.6, -42, 0, 16], [d, -58, 0, 22]]),
		] + _rider("death", ["hips", "spine", "chest", "head", "armL", "armR"]),
	}
