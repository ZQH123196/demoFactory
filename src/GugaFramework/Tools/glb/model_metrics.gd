extends RefCounted

# 读模型出数字 —— 「一份数字，校验器与体检单共用」（《模型与动画准则》「实现分层与聚合」 第 3 条）。
#
# 为什么单独一层：占地、骨数、每条 clip 到底动没动骨骼、野节点、锚点这些检查，
# 原来只长在 inspect_model.gd 里。体检单要出同样的结论，抄一份就成了两份实现各自漂移
# —— 那是规律不是风险。所以现在校验器（inspect_model.gd）与体检单（review/review_capture.gd）
# 都调这里的 measure()/judge()，要改只改这一个文件。
#
# 「怎么算合格」的字面话在 docs/《模型与动画准则》「验收清单」（定稿），本文件不复述，只实现它。

# 产物树该有什么是写死的：Rig / Skeleton3D / Body / AnimationPlayer / Anchor，
# 外加 4.7 的 GLTFDocument 自己塞进文件的 _PhysicalBoneSimulator3D（摘不掉，见 README 引擎事实 7）。
# 别的一律算野节点 —— 源场景里漏掉的组节点、CSG 子树都会在这里被逮住。
const KNOWN := ["Rig", "Skeleton3D", "Body", "AnimationPlayer", "Anchor", "root"]

# 2 毫米：只问「这条 clip 到底动没动骨骼」，不问「动得够不够显眼」
const MOTION_MIN := 0.002

# 量动作的姿势取在 clip 中段：起点多半等于绑定姿势，终点是收势，中段最容易看出有没有动
const MOTION_PROBE_MAX := 0.25


## 量一个已入树的模型实例。inst 需要已经在场景树里且至少推进过一帧，
## 否则全局变换与蒙皮姿势还没算好（无头/窗口模式都是这个规矩）。
static func measure(inst: Node, anchor_name: String) -> Dictionary:
	var skel := inst.find_child("Skeleton3D", true, false) as Skeleton3D
	var player := inst.find_child("AnimationPlayer", true, false) as AnimationPlayer
	var body := inst.find_child("Body", true, false) as MeshInstance3D

	var clips := PackedStringArray()
	if player != null:
		clips = player.get_animation_list()
		clips = _sorted(clips)

	var m := {
		"bones": 0 if skel == null else skel.get_bone_count(),
		"static": skel == null,
		"clips": clips,
		"surfaces": 0 if body == null or body.mesh == null else body.mesh.get_surface_count(),
		"verts": _vert_count(body),
		"skinned": body != null and body.skin != null and body.skin.get_bind_count() > 0,
		"aabb": AABB(),
		"anchor": anchor_name != "" and inst.find_child(anchor_name, true, false) != null,
		"stray": _stray(inst, ""),
		"motion": {},
		"length": {},
		"rest": PackedStringArray(),
		"has_body": body != null and body.mesh != null,
		"has_player": player != null,
	}
	if body != null and body.mesh != null:
		m["aabb"] = world_aabb(body)
	if skel == null:
		return m

	m["rest"] = _rest_positions(skel)
	if player == null:
		return m
	# 手动推进：默认模式下 mixer 按真实帧自走，seek 之后那一帧的 delta 每跑一次都不同，
	# 量出来的偏移与胶卷定格都会漂（2026-09-25 实测：48 张胶卷重跑 md5 不等）。
	# 切到 Manual 后只有 advance/seek 说话才算，姿势与指定的秒严格对应。
	player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
	for clip in clips:
		player.play(clip)
		player.seek(0.0, true)
		var before := _origins(skel)
		var len: float = player.get_animation(clip).length
		player.advance(minf(MOTION_PROBE_MAX, len * 0.5))
		var moved := 0.0
		var after := _origins(skel)
		for i in before.size():
			moved = maxf(moved, before[i].distance_to(after[i]))
		m["motion"][clip] = moved
		m["length"][clip] = len
	return m


## 怎么算合格：返回不合格原因列表，空 = PASS。
## contract 由调用方给（引擎侧不给任何契约默认值）：
##   max_footprint: float（<=0 不判）  need_clips: PackedStringArray（只管有骨架的模型）
## 「模型四件套」的四样在这里逐样判：顶点数据 / 骨架 / 蒙皮 / 动作 clip，缺一样即失败。
## 静态件（建筑与道具）按契约本来就只有前两样：顶点数据照判，骨架与蒙皮与 clip 跳过。
static func judge(m: Dictionary, contract: Dictionary) -> PackedStringArray:
	var bad := PackedStringArray()
	if not m["has_body"]:
		bad.append("没有 Body 网格")
		return bad
	if m["verts"] <= 0:
		bad.append("没有顶点数据（Body 网格 0 顶点）")
	var a: AABB = m["aabb"]
	var cap: float = contract.get("max_footprint", 0.0)
	if cap > 0.0 and maxf(a.size.x, a.size.z) > cap:
		bad.append("占地 %.1f×%.1f 超上限 %.1f" % [a.size.x, a.size.z, cap])
	var stray: PackedStringArray = m["stray"]
	if not stray.is_empty():
		bad.append("树里有预期外的节点: " + ", ".join(stray))
	if m["static"]:
		return bad
	if m["bones"] <= 0:
		bad.append("没有骨架（Skeleton3D 骨数 0）")
	if not m["skinned"]:
		bad.append("没有蒙皮（Body 的 skin 绑定为空）")
	var clips: PackedStringArray = m["clips"]
	if not m["has_player"]:
		bad.append("有骨架却没有 AnimationPlayer")
		return bad
	for want in contract.get("need_clips", PackedStringArray()):
		if want != "" and not clips.has(want):
			bad.append("缺动作 %s" % want)
	var motion: Dictionary = m["motion"]
	for clip in clips:
		if not motion.has(clip):
			continue
		if motion[clip] <= MOTION_MIN:
			bad.append("动作 %s 没有让任何骨离开绑定姿势（%.4f 米）" % [clip, motion[clip]])
	return bad


## 顶点总数：四样齐备里的「顶点数据」判的就是这个数（与导出器打的「顶点 N」同一口径，
## 都是 surface_get_array_len 逐面求和）。
static func _vert_count(body: MeshInstance3D) -> int:
	if body == null or body.mesh == null:
		return 0
	var total := 0
	for s in body.mesh.get_surface_count():
		total += body.mesh.surface_get_array_len(s)
	return total


## 世界系 AABB：八角点逐个乘变换再取范围（变换与 AABB 之间没有可用的 * 运算符）
static func world_aabb(mi: MeshInstance3D) -> AABB:
	var local := mi.get_aabb()
	var g := mi.global_transform
	var lo := Vector3(INF, INF, INF)
	var hi := Vector3(-INF, -INF, -INF)
	for i in range(8):
		var c: Vector3 = local.position + Vector3(
				(i & 1) * local.size.x, ((i >> 1) & 1) * local.size.y, ((i >> 2) & 1) * local.size.z)
		var w := g * c
		lo = lo.min(w)
		hi = hi.max(w)
	return AABB(lo, hi - lo)


## 绑定姿势关键骨的世界坐标 —— 和源场景对不上就是蒙皮烘错了空间（README 引擎事实 2）
static func _rest_positions(skel: Skeleton3D) -> PackedStringArray:
	var out := PackedStringArray()
	for want in ["hips", "chest", "neck", "handL", "footL"]:
		var i := skel.find_bone(want)
		if i >= 0:
			var o := skel.get_bone_global_rest(i).origin
			out.append("%s %.2f,%.2f,%.2f" % [want, o.x, o.y, o.z])
	return out


static func _origins(skel: Skeleton3D) -> Array:
	var out: Array = []
	for i in skel.get_bone_count():
		out.append(skel.get_bone_global_pose(i).origin)
	return out


static func _stray(n: Node, path: String) -> PackedStringArray:
	var out := PackedStringArray()
	for c in n.get_children():
		var p := path + "/" + String(c.name)
		if not KNOWN.has(String(c.name)) and not String(c.name).begins_with("_PhysicalBoneSimulator"):
			out.append(p + "[" + c.get_class() + "]")
		out.append_array(_stray(c, p))
	return out


static func _sorted(a: PackedStringArray) -> PackedStringArray:
	var out := PackedStringArray()
	out.append_array(a)
	out.sort()
	return out
