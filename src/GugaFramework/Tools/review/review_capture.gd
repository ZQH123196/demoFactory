extends Node3D

# 模型体检单 —— 引擎侧采集器（开发期工具，不进游戏、不被 csproj 编译）。
#
# 三个观测口搬到资产层（AGENTS.md「可调试性优先」）：时间可控 = 每格独立 seek 到指定的秒；
# 状态可查 = 一次运行出全套图 + 一份 facts；行为可注入 = 命令行指定原型/模型/动作/尺寸。
#
# 与任何原型无关：模型目录、清单、契约、产物路径全由命令行给（引擎侧不给契约默认值）。
# 反过来，「胶卷怎么出图」的介质量 —— 采样时刻表、机位方位表、中性棚 —— 写死在这里，
# 那是公共层，让每个原型重述一遍只会漂（《模型与动画准则》「实现分层与聚合」 第 4 条的例外条款）。
# 两处可由编排层按契约覆盖（都是可选声明，不声明就走这里的缺省）：
# 栏 3 分组（--scale-groups=，缺省单行 1 米网格）与胶卷机位（--film-angles=，缺省 iso 135° / 45°）。
#
# 跑法由编排层组织：dotnet run --project src/Tools -- review <原型> .
# 为什么走窗口模式：--headless 是 dummy renderer，光栅化根本不发生（《模型与动画准则》「窗口模式出图」）。
# 为什么产物自检四条：按场景跑时脚本挂了退出码仍是 0，所以过不过只看产物（同上）。

const Metrics := preload("res://src/GugaFramework/Tools/glb/model_metrics.gd")

# ---------------- 公共层固定项 ----------------

## 机位方位表 [文件段, 方位角(度), 俯仰角(度)]。方位 0° = 正后方，180° = 正前方
## （产物前脸朝 -Z，见 GugaFramework/Tools/README.md 引擎事实 8）。
## 定了不再各原型浮动：这张表一改，全原型的旧审阅图就不可比了。
const VIEWS := [
	["front", 180.0, 0.0],
	["right", 90.0, 0.0],
	["back", 0.0, 0.0],
	["left", 270.0, 0.0],
	["front_right", 135.0, 0.0],
	["back_right", 45.0, 0.0],
	["back_left", 315.0, 0.0],
	["front_left", 225.0, 0.0],
	["top", 180.0, 89.5],			# 正俯视：看占地形状
	["iso", 135.0, 45.0],			# 等距：俯视角游戏里实际看到的角度（TopDownRig 俯角 −89.99°~89.99°，这里取正段）
]

## 中性棚：不复用 src/Tools/PreviewStage.cs 那套给游戏观感调的地面色与环境色。
## 理由是可比 —— 换天光就换造型，两次审阅不可比的图等于没有基线（8 节）。
## 属性名与取值口径照 PreviewStage.cs 的 Environment 段（仓库现例），换色不换写法。
const BG := Color(0.55, 0.55, 0.57, 1.0)
const GROUND_COL := Color(0.5, 0.5, 0.52, 1.0)
const SUN_EUL := Vector3(-52.0, -28.0, 0.0)
const FOV := 35.0
const FRAME_MARGIN := 1.2			# 取景余量：满幅且不裁切
const BACK_MIN := 0.5				# 最小机距，防零尺寸模型贴脸

const MARK_FRONT := Color(1.0, 0.25, 0.2, 1.0)		# 前脸箭头
const MARK_ANCHOR := Color(0.2, 0.6, 1.0, 1.0)		# Anchor 十字
const MARK_FOOT := Color(1.0, 0.85, 0.15, 1.0)		# 占地投影线框
const MARK_GRID := Color(0.28, 0.28, 0.3, 1.0)		# 1 米方格网

const CAPTION_H := 30				# 画面下沿那条秒数带的高度
const GAP := 2						# 胶卷格之间的分隔像素
const STRIP_BG := Color(0.05, 0.05, 0.06, 1.0)
const LUMEN_STRIDE := 8				# 非单色检查的采样步长（像素）
const READY_TIMEOUT_MS := 5000		# 就绪等待上限：量的是「就绪没就绪」，不是帧数（8 节）
const SETTLE_FRAMES := 3			# 实例入树后要等全局变换与蒙皮姿势算好（引擎事实 4）

# ---------------- 运行期状态 ----------------

var _w := 640
var _h := 480
var _out := ""
var _facts_path := ""
var _models_dir := ""
var _proto := ""
var _want: PackedStringArray = []
var _clips_want: PackedStringArray = []
var _cell := 0.2
var _max_cells := 8
var _ladder: PackedFloat64Array = [0.2, 0.4, 0.8, 1.6]
var _contract := {"max_footprint": 0.0, "need_clips": PackedStringArray()}
var _loops: PackedStringArray = []	# 哪些 clip 是「姿势的循环」，由契约声明（glTF 不带这个信息）
var _anchor_name := "Anchor"
var _scale_groups: Array = []	# [{"name","grid","models"}]；空 = 未声明，走旧的单行比例尺
var _film_angles: Array = []	# [{"az","el","models"}]；空 = 未声明，全部走缺省 iso 机位

var _cam: Camera3D
var _ground: MeshInstance3D
var _holder: Node3D
var _marks: Node3D
var _hud: CanvasLayer
var _label: Label

var _head: PackedStringArray = []	# facts 里 MODEL= 之前的行
var _lines: PackedStringArray = []	# facts 里逐模型的行
var _bad := 0						# 不合格条数（进报告，不决定退出码）
var _self_bad := 0					# 产物自检不合格条数（这一半决定退出码）


func _ready() -> void:
	if not _parse_args():
		get_tree().quit(1)
		return
	_setup_window()
	_setup_stage()
	await _warm_up()

	var files := _targets()
	if files.is_empty():
		_self_fail("模型目录里没有可审的 .glb：%s" % _models_dir)
	for f in files:
		await _review_model(f)
	if _scale_groups.is_empty() or not _want.is_empty():
		# --models= 是局部补图：只出这一件的十视图与胶卷，不出整组比例尺。
		await _scale_shot(files, false)
		await _scale_shot(files, true)
	else:
		# 分组路径：每组只排本组模型、按本组网格；不再出全量单行（行长线性涨、像素按件数反比缩）。
		for g in _scale_groups:
			var group_files := PackedStringArray()
			for m in g["models"]:
				group_files.append(String(m) + ".glb")
			await _scale_shot(group_files, false, float(g["grid"]), String(g["name"]))
			await _scale_shot(group_files, true, float(g["grid"]), String(g["name"]))

	_write_facts()
	if _self_bad > 0:
		push_error("产物自检 %d 项不合格 —— 图不全就等于没审" % _self_bad)
		get_tree().quit(1)
		return
	get_tree().quit(0)


# ---------------- 命令行（契约全从外面来） ----------------

func _parse_args() -> bool:
	_out = _norm(_arg("--out="))
	_facts_path = _norm(_arg("--facts="))
	_models_dir = _arg("--models-dir=")
	_proto = _arg("--proto=")
	if _out == "" or _facts_path == "" or _models_dir == "" or _proto == "":
		push_error("review_capture：--out / --facts / --models-dir / --proto 都必须给"
				+ "（契约属于原型，引擎侧不留默认值）")
		return false
	var want := _arg("--models=")
	if want != "":
		for m in want.split(","):
			if m != "":
				_want.append(m)
	var cl := _arg("--clips=")
	if cl != "":
		for c in cl.split(","):
			if c != "":
				_clips_want.append(c)
	var sz := _arg("--size=").split("x")
	if sz.size() == 2:
		_w = int(sz[0])
		_h = int(sz[1])
	_cell = float(_arg("--cell="))
	_max_cells = int(_arg("--max-cells="))
	var lad := _arg("--ladder=")
	if lad != "":
		_ladder = PackedFloat64Array()
		for s in lad.split(","):
			_ladder.append(float(s))
	_contract = {
		"max_footprint": float(_arg("--max-footprint=")),
		"need_clips": PackedStringArray(_arg("--need-clips=").split(",")),
	}
	_anchor_name = _arg("--anchor=")
	for l in _arg("--loops=").split(","):
		if l != "":
			_loops.append(l)
	for seg in _arg("--scale-groups=").split(";"):
		if seg == "":
			continue
		var fields := seg.split(":")
		if fields.size() != 3:
			push_error("review_capture：--scale-groups 段应为 组:网格:模型|模型，收到 %s" % seg)
			return false
		_scale_groups.append({
			"name": String(fields[0]),
			"grid": float(fields[1]),
			"models": PackedStringArray(fields[2].split("|")),
		})
	for seg in _arg("--film-angles=").split(";"):
		if seg == "":
			continue
		var fields := seg.split(":")
		if fields.size() != 3:
			push_error("review_capture：--film-angles 段应为 方位:仰角:模型|模型，收到 %s" % seg)
			return false
		_film_angles.append({
			"az": float(fields[0]),
			"el": float(fields[1]),
			"models": PackedStringArray(fields[2].split("|")),
		})
	return true


func _norm(p: String) -> String:
	return p.replace("\\", "/")


func _arg(prefix: String) -> String:
	for a in OS.get_cmdline_user_args():
		if String(a).begins_with(prefix):
			return String(a).trim_prefix(prefix)
	return ""


# ---------------- 棚子 ----------------

func _setup_window() -> void:
	DisplayServer.window_set_size(Vector2i(_w, _h))
	# 窗口挪屏外：Windows 不夹位置，出图与屏内逐字节全等（11 节实测）
	DisplayServer.window_set_position(Vector2i(6000, 6000))


func _setup_stage() -> void:
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = BG
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = BG.lightened(0.05)
	env.ambient_light_energy = 0.9
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.glow_enabled = false
	env.ssao_enabled = false
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = SUN_EUL
	sun.position = Vector3(0, 40, 0)
	sun.shadow_enabled = true
	add_child(sun)

	_ground = MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(400, 400)
	_ground.mesh = plane
	var gmat := StandardMaterial3D.new()
	gmat.albedo_color = GROUND_COL
	gmat.roughness = 0.95
	_ground.material_override = gmat
	add_child(_ground)

	_cam = Camera3D.new()
	_cam.fov = FOV
	_cam.far = 2000.0
	add_child(_cam)
	_cam.make_current()

	_holder = Node3D.new()
	add_child(_holder)
	_marks = Node3D.new()
	add_child(_marks)

	_hud = CanvasLayer.new()
	add_child(_hud)
	_label = Label.new()
	_label.position = Vector2(6, _h - CAPTION_H)
	_label.size = Vector2(_w - 12, CAPTION_H)
	_label.add_theme_font_size_override("font_size", 18)
	_label.add_theme_color_override("font_color", Color(1, 1, 1, 1))
	_hud.add_child(_label)


## 就绪等待：轮询「画面到手没有」+ 秒级超时，不写固定帧数（8 节）。
func _warm_up() -> void:
	var t0 := Time.get_ticks_msec()
	while Time.get_ticks_msec() - t0 < READY_TIMEOUT_MS:
		await get_tree().process_frame
		RenderingServer.force_draw(false)
		var img := _grab()
		if img != null and img.get_width() == _w and img.get_height() == _h:
			return
	_self_fail("窗口在 %d 毫秒内没有出画面（渲染就绪超时）" % READY_TIMEOUT_MS)


func _grab() -> Image:
	var vp := get_viewport()
	if vp == null:
		return null
	return vp.get_texture().get_image()


## 定格一帧：先等一个 process_frame 让机位/姿势/字幕都进画面，再抓。
func _shot(caption: String) -> Image:
	_label.text = caption
	await get_tree().process_frame
	RenderingServer.force_draw(false)
	var img := _grab()
	if img == null:
		_self_fail("%s 抓不到画面" % caption)
		return null
	if img.get_width() != _w or img.get_height() != _h:
		_self_fail("%s 尺寸 %d×%d 不等于设定 %d×%d" % [
				caption, img.get_width(), img.get_height(), _w, _h])
	if _is_flat(img):
		_self_fail("%s 画面单色，等于什么都没截到" % caption)
	return img


func _save(rel: String, img: Image) -> bool:
	if img == null or rel == "":
		return false
	var abs := _out + "/" + rel
	if DirAccess.make_dir_recursive_absolute(abs.get_base_dir()) != OK:
		_self_fail("%s 建目录失败" % rel)
		return false
	if img.save_png(abs) != OK:
		_self_fail("%s 写盘失败" % rel)
		return false
	if not FileAccess.file_exists(abs):
		_self_fail("%s 报了成功但文件不在" % rel)
		return false
	return true


func _is_flat(img: Image) -> bool:
	var lo := 1.0
	var hi := 0.0
	var y := 0
	# 单色检查只看 3D 视口，排除底部 CAPTION_H 字幕带：字幕文字自带对比，
	# 不排掉的话「画面全空」永远判不出空白（栏 3 空白图混过 PASS 的实锤）。
	var y_lim := img.get_height() - CAPTION_H
	while y < y_lim:
		var x := 0
		while x < img.get_width():
			var v := img.get_pixelv(Vector2i(x, y)).v
			lo = minf(lo, v)
			hi = maxf(hi, v)
			x += LUMEN_STRIDE
		y += LUMEN_STRIDE
	return hi - lo < 0.02


# ---------------- 一个模型 = 一份体检单 ----------------

func _review_model(file: String) -> void:
	var mname := file.trim_suffix(".glb")
	var inst := _instantiate(file)
	if inst == null:
		return
	_lines.append("MODEL=" + mname)
	for i in SETTLE_FRAMES:
		await get_tree().process_frame
	var aabb := _rest_aabb(inst)
	var anchor_pos := _anchor_pos(inst)

	# 栏 1 · 机位方位表 —— 必须在播任何动作之前出完图。
	# 教训（2026-09-25 首跑实锤）：player.stop() 不把骨骼放回绑定姿势，
	# 度量（下面要播每条 clip）一旦跑过，「绑定姿势」就变成「上一条 clip 的末姿势」。
	var views := await _views(inst, aabb, mname)
	# 叠层也走未播动作的实例，标记才与造型对得上
	var overlay := await _overlay(aabb, anchor_pos, mname)

	# 度量会播动作并留下姿势，所以排在两栏静态图之后
	var m := Metrics.measure(inst, _anchor_name)
	_write_numbers(m, anchor_pos)
	# 栏 2 · 动画胶卷（每条 clip 一条横向帧带，每格独立 seek 到绝对秒）
	var films := await _films(inst, m, mname)

	var sheet := _contact_sheet(mname, views, films, overlay)
	_lines.append("VIEWS=" + ",".join(views))
	_lines.append("FILMS=" + ",".join(films))
	_lines.append("OVERLAY=" + overlay)
	_lines.append("SHEET=" + sheet)
	var bad := Metrics.judge(m, _contract)
	_lines.append("VERDICT=" + ("PASS" if bad.is_empty() else "FAIL"))
	if not bad.is_empty():
		_lines.append("WHY=" + " ; ".join(bad))
		_bad += bad.size()
	inst.free()
	_marks_clear()


func _rest_aabb(inst: Node) -> AABB:
	var body := inst.find_child("Body", true, false) as MeshInstance3D
	return Metrics.world_aabb(body) if body != null else AABB()


func _anchor_pos(inst: Node) -> Vector3:
	var n := inst.find_child(_anchor_name, true, false)
	return (n as Node3D).global_position if n != null and n is Node3D else Vector3(INF, INF, INF)


func _instantiate(file: String) -> Node:
	var sc := load(_models_dir + file) as PackedScene
	if sc == null:
		_self_fail("%s 加载失败" % file)
		return null
	var inst := sc.instantiate()
	_holder.add_child(inst)
	# 定格的前提：mixer 不许按真实帧自走（帧 delta 每次都不同，胶卷就不可重跑）。
	# 不靠 Metrics.measure 顺手设 —— 本文件的不变式自己守住。
	var player := inst.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if player != null:
		player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
	return inst


## facts 里的数字行 —— 与校验器同一份度量（Metrics.measure），两套实现不会各漂各的。
func _write_numbers(m: Dictionary, anchor_pos: Vector3) -> void:
	var a: AABB = m["aabb"]
	var parts := PackedStringArray()
	for clip in m["clips"]:
		parts.append("%s:%.4f" % [clip, m["motion"].get(clip, 0.0)])
	_append([
		"BONES=%d" % m["bones"],
		"STATIC=%s" % ("yes" if m["static"] else "no"),
		"AABB=%.2fx%.2fx%.2f" % [a.size.x, a.size.y, a.size.z],
		"BASE_Y=%.2f" % a.position.y,
		"SURFACES=%d" % m["surfaces"],
		"ANCHOR=%s" % ("yes" if m["anchor"] else "no"),
		# Anchor 是数据容器：.x = 血条宽度（米），.y = 名牌高度（米），不是空间坐标
		"ANCHOR_BAR=%s" % ("none" if anchor_pos.x == INF else "%.2f" % anchor_pos.x),
		"ANCHOR_TOP=%s" % ("none" if anchor_pos.x == INF else "%.2f" % anchor_pos.y),
		"CLIPS=" + ",".join(m["clips"]),
		"MOTION=" + ",".join(parts),
		"STRAY=" + ",".join(m["stray"]),
	])


func _append(ls: PackedStringArray) -> void:
	for l in ls:
		_lines.append(l)


func _views(inst: Node, a: AABB, mname: String) -> PackedStringArray:
	var out := PackedStringArray()
	for i in VIEWS.size():
		var v: Array = VIEWS[i]
		_place_camera(a, float(v[1]), float(v[2]))
		var rel := "%s/views/%02d_%s.png" % [mname, i + 1, v[0]]
		var img := await _shot("%s  |  %s" % [mname, String(v[0]).to_upper()])
		if _save(rel, img):
			out.append(rel)
	return out


# ---------------- 栏 2 · 采样时刻表与胶卷 ----------------

## 采样时刻表规则（7 节 栏 2）：格距取 0.1 秒的整数倍；循环动作取半开区间 [0, 长度)
## （末点与首点同姿势，白占一格）；非循环补一个结束端点（收势那一帧本身要看）；
## 格数封顶后沿阶梯升档，实际格距印在每格字幕上 —— 升档必须留痕。
func _sampling(len: float, loop: bool) -> Dictionary:
	for si in _ladder.size():
		var step: float = _ladder[si]
		var times := PackedFloat64Array()
		var i := 0
		while true:
			var t: float = step * i
			if t >= len - 1e-4:
				if not loop:
					times.append(len)
				break
			times.append(t)
			i += 1
		if times.size() <= _max_cells:
			return {"step": step, "times": times, "clipped": false}
		# 阶梯走到头还装不下：截到上限并如实标 clipped，报告里看得见
		if si == _ladder.size() - 1:
			times.resize(_max_cells)
			return {"step": step, "times": times, "clipped": true}
	return {"step": _ladder[0], "times": PackedFloat64Array([0.0]), "clipped": false}


func _films(inst: Node, m: Dictionary, mname: String) -> PackedStringArray:
	var out := PackedStringArray()
	var player := inst.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if player == null:
		return out
	var a: AABB = m["aabb"]
	var angles := _film_angles_for(mname)
	for clip in m["clips"]:
		if not _clips_want.is_empty() and not _clips_want.has(clip):
			continue
		# 每条 clip 出图前显式摆机位：缺省 = iso 135° / 45°（与叠层后的旧机位一致，重摆不改画面）；
		# 侧向看四肢的模型（巨兽）由编排层声明别的方位/仰角。
		_place_camera(a, float(angles[0]), float(angles[1]))
		var anim := player.get_animation(clip)
		# 循环与否以契约声明为准：glTF 规范里没有这个字段，读回来的 loop_mode 一律是 NONE，
		# 真去问模型就等于永远按「一次性动作」出图（idle 会被迫升一档格距）。
		var loop: bool = _loops.has(clip) or anim.loop_mode != Animation.LOOP_NONE
		var s := _sampling(anim.length, loop)
		var cells: Array = []
		for t in s["times"]:
			# 每格独立 seek(exact=true)，不用逐格 advance 累加 —— 累加会把上一格的
			# 时间误差传给下一格，重跑就做不到逐字等（7 节 栏 2）
			player.play(clip)
			player.seek(float(t), true)
			var img := await _shot("%s  |  %s  t=%.2fs  step=%.2fs" % [mname, clip, float(t), float(s["step"])])
			if img != null:
				cells.append(img)
		_lines.append("TIMES=%s:%.2f:%.2f:%d:%s:%s%s" % [
				clip, anim.length, s["step"], (s["times"] as PackedFloat64Array).size(),
				"loop" if loop else "once", _join_times(s["times"]),
				"  (超上限已截断)" if s["clipped"] else ""])
		# 产物自检第四条：格数与采样时刻表齐
		if cells.size() != (s["times"] as PackedFloat64Array).size():
			_self_fail("%s/%s 出了 %d 格，采样表要 %d 格" % [
					mname, clip, cells.size(), (s["times"] as PackedFloat64Array).size()])
		var rel := "%s/film/%s.png" % [mname, clip]
		if not cells.is_empty() and _save(rel, _strip(cells)):
			out.append(rel)
	return out


## 该模型的胶卷机位 [方位, 仰角]：编排层 --film-angles= 声明的表优先，缺省 = iso 135° / 45°。
func _film_angles_for(mname: String) -> Vector2:
	for fa in _film_angles:
		if (fa["models"] as PackedStringArray).has(mname):
			return Vector2(float(fa["az"]), float(fa["el"]))
	return Vector2(135.0, 45.0)


func _join_times(times: PackedFloat64Array) -> String:
	var parts := PackedStringArray()
	for t in times:
		parts.append("%.2f" % t)
	return ";".join(parts)


## 胶卷 = 横向拼盘。字幕已经烧在每格里，所以拼完不必再标刻度。
## 拼盘画布必须与抓帧同格式 —— blit_rect 不同格式直接报错（实测：窗口抓来是 RGB8，
## 自拼 RGBA8 时整条胶卷全空）。
func _strip(cells: Array) -> Image:
	var cw: int = cells[0].get_width()
	var ch: int = cells[0].get_height()
	var img := Image.create(cw * cells.size() + GAP * (cells.size() - 1), ch, false, cells[0].get_format())
	img.fill(STRIP_BG)
	var x := 0
	for c in cells:
		img.blit_rect(c, Rect2i(0, 0, cw, ch), Vector2i(x, 0))
		x += cw + GAP
	return img


# ---------------- 叠层 · 数字校验器与人眼之间缺的那层证据 ----------------

## 三条标记都来自 4 节，现在只有数字没有图（8 节）。
func _overlay(a: AABB, anchor_pos: Vector3, mname: String) -> String:
	var y := a.position.y + 0.01
	var x0 := a.position.x
	var x1 := a.end.x
	var z0 := a.position.z
	var z1 := a.end.z
	_mark("foot", PackedVector3Array([
			Vector3(x0, y, z0), Vector3(x1, y, z0), Vector3(x1, y, z0), Vector3(x1, y, z1),
			Vector3(x1, y, z1), Vector3(x0, y, z1), Vector3(x0, y, z1), Vector3(x0, y, z0)]), MARK_FOOT)
	# 前脸箭头指向 -Z：产物前脸朝 -Z 是约定，以前只能靠猜
	var tip := Vector3(0, maxf(0.15, a.size.y * 0.15), z0 - maxf(0.2, a.size.z * 0.4))
	var bar := maxf(0.1, a.size.x * 0.15)
	_mark("front", PackedVector3Array([
			Vector3(0, tip.y, z0), tip,
			tip + Vector3(bar, 0, bar), tip,
			tip + Vector3(-bar, 0, bar), tip,
			tip + Vector3(0, 0, bar), tip]), MARK_FRONT)
	if anchor_pos.x != INF:
		# Anchor 不是空间位置而是数据容器：.x = 头顶血条宽度、.y = 名牌高度
		# （口径见原型 Tools/Gen.cs 的 Anchor() 与 Main.cs 的读法）。
		# 所以这里画的是「血条挂在多高、有多宽」，不是把十字钉在那个点上。
		var cx := a.position.x + a.size.x * 0.5
		var cz := a.position.z + a.size.z * 0.5
		var half := anchor_pos.x * 0.5
		var hy := anchor_pos.y
		var tick := maxf(0.05, a.size.y * 0.05)
		_mark("anchor", PackedVector3Array([
				Vector3(cx - half, hy, cz), Vector3(cx + half, hy, cz),
				Vector3(cx - half, hy - tick, cz), Vector3(cx - half, hy + tick, cz),
				Vector3(cx + half, hy - tick, cz), Vector3(cx + half, hy + tick, cz),
				# 头顶挂点：从模型中轴引一段过去，说明「血条居中在轴线上」
				Vector3(cx, a.position.y + a.size.y, cz), Vector3(cx, hy, cz)]), MARK_ANCHOR)
	_place_camera(a, float(VIEWS[9][1]), float(VIEWS[9][2]))
	var rel := "%s/overlay.png" % mname
	var img := await _shot("%s  |  OVERLAY  |  黄=占地 红=前脸 蓝=血条 %.2f 米宽 @ %.2f 米高" % [
			mname, anchor_pos.x, anchor_pos.y])
	# 标记必须在这里清掉：漏到后面，胶卷每一格都带着这套叠层（2026-09-25 看图实锤）
	_marks_clear()
	return rel if _save(rel, img) else ""


func _mark(n: String, pts: PackedVector3Array, col: Color) -> void:
	var mi := MeshInstance3D.new()
	var am := ArrayMesh.new()
	var arrays: Array = []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = pts
	am.add_surface_from_arrays(Mesh.PRIMITIVE_LINES, arrays)
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = col
	mi.mesh = am
	mi.material_override = mat
	mi.name = n
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_marks.add_child(mi)


func _marks_clear() -> void:
	for c in _marks.get_children():
		_marks.remove_child(c)
		c.free()


## 自适应取景：距离由世界 AABB 算（Troll 高数米、BountyFlag 一米不到，
## 定距机位必然一个顶天一个占仨像素，十视图当场作废 —— 7 节 栏 1）。
## 按「相机左右轴 / 上下轴各自的半跨度」分别适配，而不是按包围球配垂直 FOV：
## 栏 3 那种 90 米宽的一排，用包围球会被撑到 175 米机距，整排只占画面一小条。
func _place_camera(a: AABB, az: float, el: float) -> void:
	var c := a.position + a.size * 0.5
	var aspect := _w / float(_h)
	var tan_v := tan(deg_to_rad(FOV * 0.5))
	var tan_h := tan_v * aspect
	# 相机右轴（画面横轴）在世界里的朝向；正俯视时按 _redraw 里给的 up = -Z 推得右轴 = +X
	var right := Vector3(1, 0, 0) if el > 89.0 else Vector3(cos(deg_to_rad(az)), 0.0, -sin(deg_to_rad(az)))
	var half_h := absf(a.size.dot(right)) * 0.5
	# 画面纵轴上量的是「站得起来的高度」，俯视时则是 Z 向进深
	var half_v := a.size.z * 0.5 if el > 89.0 else a.size.y * 0.5
	# 机距从 AABB 中心量，但「最近的那一面」才是成像面：沿视线方向的那一半厚度要补上，
	# 否则正俯视会落到模型肚子里（实测：5 米高的 BountyFlag，相机离中心 3.9 米）。
	var along := absf(a.size.dot(Vector3(
			cos(deg_to_rad(el)) * sin(deg_to_rad(az)),
			sin(deg_to_rad(el)),
			cos(deg_to_rad(el)) * cos(deg_to_rad(az))))) * 0.5
	var dist := maxf(maxf(half_h / tan_h, half_v / tan_v) * FRAME_MARGIN + along, BACK_MIN)
	# 远裁剪面随机距走：写死 2000 会把千米级长排整排裁掉（88 件单行 1681 米、机距约 2400 米实锤）。
	_cam.far = maxf(2000.0, dist * 2.0)
	var dir := Vector3(
			cos(deg_to_rad(el)) * sin(deg_to_rad(az)),
			sin(deg_to_rad(el)),
			cos(deg_to_rad(el)) * cos(deg_to_rad(az)))
	var eye := c + dir * dist
	if el > 89.0:
		_cam.look_at_from_position(eye, c, Vector3(0, 0, -1))
	else:
		_cam.look_at_from_position(eye, c, Vector3.UP)
	_ground.position = Vector3(c.x, a.position.y, c.z)


# ---------------- 栏 3 · 比例尺同框（原型级，不是模型级） ----------------

## 体量关系只能同框判：各自满幅取景的十视图看不出弓手相对宫殿是不是太小（7 节 栏 3）。
## 排布沿 X 一字排开（脚下 y=0、Z 居中对齐、间隔 1 米），所以侧向机位必须站在 ±Z 上
## （az=0）—— 站在 +X 上会让整排往屏幕里延伸，17 件叠成一坨（2026-09-25 首跑看图实锤）。
## grid/group 是可选分组参数：缺省（grid=1、group=""）走旧的单行 scale_side.png / scale_top.png；
## 声明分组后每组一行，产物 scale_<组>_side.png / scale_<组>_top.png，facts 键带组名。
## 把网格步长排进字幕：整数值排成整数（1 → "1"），小数保留原样（0.5 → "0.5"）。
## 旧单行路径固定 grid=1.0，排出来就是 "1 米方格" —— 与历史字幕逐字一致。
func _fmt_meters(v: float) -> String:
	return str(int(v)) if is_equal_approx(v, floorf(v)) else String.num(v)


func _scale_shot(files: PackedStringArray, from_top: bool, grid := 1.0, group := "") -> void:
	if files.size() < 2:
		return
	var cursor := 0.0
	var box := AABB()
	var n := 0
	for f in files:
		var inst := _instantiate(f)
		if inst == null:
			continue
		await get_tree().process_frame
		var body := inst.find_child("Body", true, false) as MeshInstance3D
		if body == null:
			continue
		var a := Metrics.world_aabb(body)
		var off := Vector3(cursor - a.position.x, -a.position.y, -a.position.z - a.size.z * 0.5)
		inst.global_position += off
		# 纯平移，直接按位移算新框，不再量一次（省一帧，也免得读到还没刷新的变换）
		var placed := AABB(a.position + off, a.size)
		box = placed if n == 0 else box.merge(placed)
		cursor = placed.end.x + 1.0
		n += 1
	if n < 2:
		_holder_free()
		return
	for i in SETTLE_FRAMES:
		await get_tree().process_frame
	_grid_lines(box, grid)
	var rel := "scale_top.png" if from_top else "scale_side.png"
	if group != "":
		rel = "scale_%s_%s.png" % [group, "top" if from_top else "side"]
	_place_camera(box, 180.0 if from_top else 0.0, 89.5 if from_top else 8.0)
	var head := "SCALE" if group == "" else "SCALE %s" % group
	var cap := "%s  |  %d 件同框  |  %s 米方格  |  %s" % [head, n, _fmt_meters(grid), "俯视" if from_top else "侧向"]
	var img := await _shot(cap)
	if _save(rel, img):
		var key := "SCALE_TOP" if from_top else "SCALE"
		if group != "":
			key += "_" + group
		_head.append(key + "=" + rel)
	_marks_clear()
	_holder_free()


func _grid_lines(box: AABB, step := 1.0) -> void:
	var pts := PackedVector3Array()
	var y := box.position.y + 0.005
	var x0 := floorf(box.position.x / step) * step - step
	var x1 := ceilf(box.end.x / step) * step + step
	var z0 := floorf(box.position.z / step) * step - step
	var z1 := ceilf(box.end.z / step) * step + step
	var gx := x0
	while gx <= x1 + 0.001:
		pts.append(Vector3(gx, y, z0))
		pts.append(Vector3(gx, y, z1))
		gx += step
	var gz := z0
	while gz <= z1 + 0.001:
		pts.append(Vector3(x0, y, gz))
		pts.append(Vector3(x1, y, gz))
		gz += step
	_mark("grid", pts, MARK_GRID)


func _holder_free() -> void:
	for c in _holder.get_children():
		_holder.remove_child(c)
		c.free()


# ---------------- 一页总览（人眼只看这一张，10 节 第 5 条） ----------------

## 一页总览：机位方位表与叠层按 5 格一行排，胶卷每条独占一行（压到整页宽）——
## 胶卷塞进单格会变成看不清的小人，而它要审的正是「腿有没有跨开」（10 节 第 5 条）。
func _contact_sheet(mname: String, views: PackedStringArray, films: PackedStringArray,
		overlay: String) -> String:
	var grid_rels := PackedStringArray()
	grid_rels.append_array(views)
	if overlay != "":
		grid_rels.append(overlay)
	var grid: Array = _load_cells(grid_rels)
	var strips: Array = _load_cells(films)
	if grid.is_empty() and strips.is_empty():
		_self_fail("%s 总页没有一张格可读" % mname)
		return ""

	var per_row := 5
	var sheet_w := per_row * (_w + GAP) + GAP
	var rows := int(ceil(grid.size() / float(per_row)))
	var heights := PackedInt32Array()
	for _r in rows:
		heights.append(_h)
	for s in strips:
		heights.append(int(round(s.get_height() * (sheet_w - 2 * GAP) / float(s.get_width()))))
	var total := GAP
	for hh in heights:
		total += hh + GAP

	var fmt: Image.Format = grid[0].get_format() if not grid.is_empty() else strips[0].get_format()
	var sheet := Image.create(sheet_w, total, false, fmt)
	sheet.fill(STRIP_BG)
	var y := GAP
	for i in grid.size():
		var cell: Image = grid[i]
		var r := int(i / per_row)
		var c := i % per_row
		sheet.blit_rect(cell, Rect2i(0, 0, cell.get_width(), cell.get_height()),
				Vector2i(GAP + c * (_w + GAP), y + r * (_h + GAP)))
	y += rows * (_h + GAP)
	for s in strips:
		var h := int(round(s.get_height() * (sheet_w - 2 * GAP) / float(s.get_width())))
		var scaled: Image = s.duplicate()
		scaled.resize(sheet_w - 2 * GAP, h, Image.INTERPOLATE_LANCZOS)
		sheet.blit_rect(scaled, Rect2i(0, 0, scaled.get_width(), scaled.get_height()), Vector2i(GAP, y))
		y += h + GAP

	var rel := "%s/contact.png" % mname
	return rel if _save(rel, sheet) else ""


func _load_cells(rels: PackedStringArray) -> Array:
	var out: Array = []
	for rel in rels:
		var img := Image.load_from_file(_out + "/" + rel)
		if img != null:
			out.append(img)
		else:
			_self_fail("总页读不回 %s" % rel)
	return out


# ---------------- 产物与退出码 ----------------

func _targets() -> PackedStringArray:
	var out := PackedStringArray()
	if not _want.is_empty():
		for m in _want:
			out.append(m + ".glb")
		out.sort()
		return out
	var dir := DirAccess.open(_models_dir)
	if dir == null:
		_self_fail("打不开模型目录 %s" % _models_dir)
		return out
	dir.list_dir_begin()
	var f := dir.get_next()
	while f != "":
		if not dir.current_is_dir() and f.ends_with(".glb"):
			out.append(f)
		f = dir.get_next()
	dir.list_dir_end()
	out.sort()
	return out


func _write_facts() -> void:
	var head := PackedStringArray([
		"# 模型体检单 facts —— review_capture.gd 产出；src/Tools/Review.cs 读它排 report.md。",
		"# 数字与 PASS/FAIL 都来自 glb/model_metrics.gd（校验器与体检单共用一次计算），本文件不要手改。",
		"REVIEW=1",
		"PROTO=" + _proto,
		"SIZE=%dx%d" % [_w, _h],
		"WINDOW=6000,6000",
		"ENGINE=" + String(Engine.get_version_info().get("string", "?")),
		"CELL=%.2f" % _cell,
		"VIEW_COUNT=%d" % VIEWS.size(),
		"BAD=%d" % _bad,
		"SELF_BAD=%d" % _self_bad,
	])
	head.append_array(_head)
	var fa := FileAccess.open(_facts_path, FileAccess.WRITE)
	if fa == null:
		push_error("facts 写不出去：%s（打开错误码 %d）" % [_facts_path, FileAccess.get_open_error()])
		return
	for l in head:
		fa.store_line(String(l))
	for l in _lines:
		fa.store_line(String(l))
	fa.flush()
	fa.close()


func _self_fail(msg: String) -> void:
	_self_bad += 1
	push_error("SELFCHECK FAIL  " + msg)
	print("SELFCHECK FAIL  " + msg)
