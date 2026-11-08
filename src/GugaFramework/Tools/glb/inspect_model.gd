extends SceneTree

# 模型自检 / 导出后校验器（开发期工具，不进游戏、不被 csproj 编译）
#
# 把导出的 .glb 读回来逐件验收，替掉「跑关卡截图猜」那一步：
#   · 骨架：骨数、绑定姿势关键骨的世界坐标（和源场景对不上就是蒙皮烘错了空间）
#   · 动作：每条 clip 都播到中段，量「动画姿势 vs 绑定姿势」的最大骨偏移 —— 大于阈值才算真在动
#   · 占地：Body 的世界 AABB，XZ 超过契约上限即判失败（建筑间距口径）
#   · 卫生：树里只该有已知节点（导出器自己补的空骨骼模拟器除外）、登记的 clip 齐不齐
# 任一不合格 → 退出码 1，可以直接串进验收流程。
#
# 度量与「怎么算合格」都不在这里：那套数字长在 glb/model_metrics.gd，体检单（review/review_capture.gd）
# 调的是同一份 —— 要改只改一处。本文件只做「按帧驱动 + 打印 + 退出码」。
#
# 跑法（项目根）：
#   godot --headless --path . --quit-after 60 \
#     --script res://src/GugaFramework/Tools/glb/inspect_model.gd -- \
#       --models-dir=res://src/Prototypes/<原型>/Models/ --all --max-footprint=12
#   单个模型：-- --model=Ranger
# 模型目录必传（引擎侧不给契约默认值）—— 把目录写死等于把校验器锁在一个原型上。
#
# --facts=<绝对路径>（2026-09-28 技能体检单加的机器可读出口）：把每个模型的
# 「clip 名 → 名义时长（秒）」按体检单工位那套 `K=V` 行式另写一份文件（`MODEL=` 起一段，
# 一段一条 `LENGTHS=clip:秒,clip:秒`，按 clip 名排序）。为什么走文件不走 stdout：
# 人读的打印给人看，机器读的数字给工具看，两者同一次计算（「校验器与报告共用一次计算」，《模型与动画准则》「实现分层与聚合」节）。
# 技能前摇与动画时长对表那道校验（`dotnet run --project src/Tools -- skill <原型>`）读的就是这份文件，
# 它自己不重算、不自造第二套「这条动作多长」的算法。这一段**不需要窗口模式**（不出图，只读骨骼与时长）。

const Metrics := preload("res://src/GugaFramework/Tools/glb/model_metrics.gd")

# 该原型的模型清单与契约由命令行给（引擎侧不给任何契约默认值）
var _models_dir := ""
var _one := ""
var _clip := ""
var _max_footprint := 0.0
var _need_clips := PackedStringArray()
var _facts_path := ""

var _frames := 0
var _files: PackedStringArray = []
var _bad := 0
var _facts_lines: PackedStringArray = []


func _initialize() -> void:
	_models_dir = _arg("--models-dir=")
	if _models_dir == "":
		push_error("inspect_model：--models-dir= 必传（把目录写死等于把校验器锁在一个原型上）")
	_one = _arg("--model=")
	_clip = _arg("--clip=")
	var mf := _arg("--max-footprint=")
	_max_footprint = float(mf) if mf != "" else 0.0
	_need_clips = PackedStringArray(_arg("--need-clips=").split(","))
	_facts_path = _arg("--facts=")


func _process(_d: float) -> bool:
	if _models_dir == "":
		quit(1)
		return true
	_frames += 1
	if _frames == 3:
		_files = _targets()
		for f in _files:
			var sc := load(_models_dir + f) as PackedScene
			if sc == null:
				_fail("%s 加载失败" % f)
				continue
			var inst := sc.instantiate()
			inst.set_meta("file", f)
			root.add_child(inst)
	elif _frames == 6:
		for n in root.get_children():
			if n.has_meta("file"):
				_report(n)
		if _facts_path != "" and not _write_facts():
			_bad += 1
		print("INSPECT DONE  %d 个模型，%d 项不合格" % [_files.size(), _bad])
		if _bad > 0:
			quit(1)
		return true
	return false


func _targets() -> PackedStringArray:
	var out := PackedStringArray()
	if _one != "":
		out.append(_one + ".glb")
		return out
	var dir := DirAccess.open(_models_dir)
	dir.list_dir_begin()
	var f := dir.get_next()
	while f != "":
		if not dir.current_is_dir() and f.ends_with(".glb"):
			out.append(f)
		f = dir.get_next()
	out.sort()
	return out


func _report(inst: Node) -> void:
	var file := String(inst.get_meta("file"))
	print("=== %s" % file.trim_suffix(".glb"))
	var m := Metrics.measure(inst, "Anchor")
	_collect_facts(file, m)
	var a: AABB = m["aabb"]
	print("  模型 AABB 占地 %.2f×%.2f  高 %.2f  底 y=%.2f  面 %d" % [
			a.size.x, a.size.z, a.end.y, a.position.y, m["surfaces"]])
	print("  顶点 %d  骨 %d  蒙皮 %s  动作 %s" % [
			m["verts"], m["bones"], "有" if m["skinned"] else "无", m["clips"]])
	if m["static"]:
		_verdict(Metrics.judge(m, _contract()))
		return
	print("  绑定: " + ", ".join(PackedStringArray(m["rest"])))
	for clip in m["clips"]:
		if _clip != "" and String(clip) != _clip:
			continue
		# 2 毫米：只问「这条 clip 到底动没动骨骼」，不问「动得够不够显眼」（阈值在 model_metrics.gd）
		var moved: float = m["motion"].get(clip, 0.0)
		var mark := "OK" if moved > Metrics.MOTION_MIN else "不动"
		print("  动作 %s 中段最大骨偏移 %.3f 米  %s" % [clip, moved, mark])
	_verdict(Metrics.judge(m, _contract()))


func _contract() -> Dictionary:
	return {"max_footprint": _max_footprint, "need_clips": _need_clips}


## 机器可读那一段：MODEL= 起一段，紧跟一条 LENGTHS=clip:秒,clip:秒（clip 名按 measure 给的序，已排序）。
## 静态模型（建筑本来就没有动作）也出一段空 LENGTHS —— 工具据此分得清「没有骨架」与「没读到」。
func _collect_facts(file: String, m: Dictionary) -> void:
	if _facts_path == "":
		return
	var parts := PackedStringArray()
	for c in m["clips"]:
		parts.append("%s:%.04f" % [c, m["length"].get(c, 0.0)])
	_facts_lines.append("MODEL=" + file.trim_suffix(".glb"))
	_facts_lines.append("LENGTHS=" + ",".join(parts))


func _write_facts() -> bool:
	var f := FileAccess.open(_facts_path, FileAccess.WRITE)
	if f == null:
		push_error("inspect_model：facts 写不出去 %s（错误码 %d）" % [_facts_path, FileAccess.get_open_error()])
		return false
	for line in _facts_lines:
		f.store_line(line)
	f.close()
	print("FACTS %d 段 → %s" % [_facts_lines.size() / 2, _facts_path])
	return true


func _verdict(bad: PackedStringArray) -> void:
	for reason in bad:
		_fail("  " + String(reason))


func _fail(msg: String) -> void:
	_bad += 1
	push_error(msg)
	print("FAIL " + msg)


func _arg(prefix: String) -> String:
	for a in OS.get_cmdline_user_args():
		if String(a).begins_with(prefix):
			return String(a).trim_prefix(prefix)
	return ""
