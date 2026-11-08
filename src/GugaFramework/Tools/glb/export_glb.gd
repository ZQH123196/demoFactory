extends SceneTree

# 模型导出器（开发期工具，不进游戏、不被 csproj 编译）
#
# 职责：把「关节树源场景」（gen.mjs 产出的 .tscn，j_ 前缀 = 关节契约）编译成
# 真模型资产 Models/<Name>.glb —— 骨架 + 刚性蒙皮 + 动作，全部烘进这一个文件。
# 游戏侧只加载 .glb，不认识源场景，也不在运行时决定造型长什么样。
#
# 两类产物：
#   · 会动的（单位）：j_ 关节链 → Skeleton3D，部件按最近祖先关节刚性蒙皮，动作按骨名烘进 clip。
#   · 不动的（建筑/道具）：表里不写 clips，图元与 CSG 合并成一个无骨架网格，尺寸即最终尺寸。
#
# 跑法（项目根，--proto 必填）：
#   godot --headless --path . --quit-after 900 \
#     --script res://src/GugaFramework/Tools/glb/export_glb.gd -- --proto=<原型目录名>
#   尾参 -- --only=Ranger 只出一个；-- --list 只列登记表
#   三张表（源目录 / 产物目录 / 模型清单）读本原型 Tools/ExportModels.gd ——
#   本文件不含任何原型的数据，换原型只加文件、不改文件（与 C# 侧 IPrototypeTools 同口径）。
#
# 五条实测出来的 4.7 引擎事实：
#   1. skin 属性挂在 MeshInstance3D 上（4.7 从 Mesh 挪走的），Mesh/ArrayMesh 上没有 skin。
#   2. 骨骼动画轨道必须写成 "Skeleton3D:骨名"（冒号）。写成 "Skeleton3D/骨名" 不报错，
#      只在导出时留一句 "Cannot get node for animated track"，动画静默丢失。
#   3. 导出要读全局变换，节点必须在真实帧里跑过一遍 —— 所以整个流程挂在帧计数上，
#      建树与导出分帧，不要在 _initialize 里一次性做完。
#   4. CSG 的合并网格要等它自己算完（入树后轮询到出网格为止；旧实测约 0.7 秒，帧率一变就不准），而且**只有最顶层**的 CSG 节点
#      产得出网格 —— 子刷形状是它的输入，永远返回 null。bake_static_mesh() 给的是该节点
#      局部系的网格，减去体已生效。所以是「轮询到算完再导」，不能拿固定帧数当等待（《模型与动画准则》「三条固定项」节的就绪等待）。
#   5. 4.7 的 GLTFDocument 会给任何含 Skeleton3D 的场景**自己补**一个 _PhysicalBoneSimulator3D
#      空节点写进文件（实测：只有一根骨的裸 Skeleton3D 也带，而源树里根本没这个节点，摘不掉）。
#      回读树多一个空 Node3D，无害 —— 自检脚本对它开白名单，别的野节点照旧判失败。
#
# 一期不用位置轨道：骨的 position 轨道是「替换 rest」还是「叠加在 rest 上」还没实测，
# 身高起伏先用脊柱与髋的旋转近似。测清之后再补。

const Clips := preload("res://src/GugaFramework/Tools/glb/clips_humanoid.gd")
const MountClips := preload("res://src/GugaFramework/Tools/glb/clips_mount.gd")
const BeastClips := preload("res://src/GugaFramework/Tools/glb/clips_beast.gd")

const FRAME_BUILD := 3
const FRAME_DEADLINE := 600

# ---- 原型登记表（2026-09-26 改为必填）----
# 源目录 / 产物目录 / 模型清单三张表属于原型，住在各原型 Tools/ExportModels.gd 里，
# 由 --proto=<目录名> 选定。本文件只剩机制：这里曾经内建着一份默认值，
# 那份已搬进各原型自己的 Tools/ExportModels.gd（对照验证：.glb 逐字节全等）。
var _src_root := ""
var _out_root := ""
var _models: Dictionary = {}


## 绑定原型登记表；没传 --proto 或表加载失败 → 报错退出 1（静默用别人的表比报错更贵）
func _bind_proto() -> bool:
	var proto := _arg("--proto=")
	if proto == "":
		_fail("export_glb：--proto=<原型目录名> 必传（三张表在各原型 Tools/ExportModels.gd 里，公共层不给默认值）")
		return false
	var table := load("res://src/Prototypes/%s/Tools/ExportModels.gd" % proto)
	if table == null:
		_fail("找不到原型登记表 res://src/Prototypes/%s/Tools/ExportModels.gd" % proto)
		return false
	_src_root = table.src_root()
	_out_root = table.out_root()
	_models = table.models()
	return true


func _fail(msg: String) -> void:
	push_error(msg)
	quit(1)


var _frames := 0
var _sources := {}         # model -> 源场景实例（等 CSG 算完就编译，随后释放）
var _pending: Array = []   # 还没出活的模型名
var _written := 0


func _process(_d: float) -> bool:
	_frames += 1
	if _frames < FRAME_BUILD:
		return false
	if _frames == FRAME_BUILD:
		return _stage()
	return _pump()


## 帧 3：把要导出的源场景全部实例化进树（CSG 从这一刻才开始算，产物要等它算完）
func _stage() -> bool:
	if not _bind_proto():
		return true
	if _arg("--list") == "list":
		for m in _models:
			print("MODEL %s  %s" % [m, _models[m].src])
		print("EXPORT DONE")
		return true
	for model in _wanted():
		var packed := load(_src_root + String(_models[model].src)) as PackedScene
		if packed == null:
			push_error("源场景加载失败 " + String(_models[model].src))
			continue
		var scene := packed.instantiate()
		root.add_child(scene)
		_sources[model] = scene
		_pending.append(model)
	return false


## 想导出的模型（--only=Ranger 单出，否则整张表）
func _wanted() -> Array:
	var only := _arg("--only=")
	var out: Array = []
	for model in _models:
		if only == "" or only == String(model):
			out.append(model)
	return out


## 之后每帧推进：谁的 CSG 算完了就编译 + 写盘；全部出活（或超出帧预算）才收尾
func _pump() -> bool:
	var overdue := _frames >= FRAME_DEADLINE
	for model in _pending.duplicate():
		var scene: Node3D = _sources[model]
		if not overdue and not _csg_settled(scene):
			continue
		if overdue:
			push_error("%s 的 CSG 到 %d 帧仍没算完 —— 雕切部件会缺失" % [model, _frames])
		var built := _build(String(model), _models[model], scene)
		_sources.erase(model)
		_pending.erase(model)
		if built == null:
			continue
		root.add_child(built)
		_write(String(model), built)
		_written += 1
	if _pending.is_empty():
		print("EXPORT DONE  %d 个模型已导出" % _written)
	elif overdue:
		push_error("导出超时未完成: " + ", ".join(PackedStringArray(_pending)))
	else:
		return false
	return true


## 最顶层 CSG 节点（父链上没有 CSG）才产得出网格；子刷形状已被父节点吃掉
func _csg_settled(n: Node) -> bool:
	for c in n.get_children():
		if c is CSGShape3D:
			if (c as CSGShape3D).bake_static_mesh() == null:
				return false
		elif c is Node3D:
			if not _csg_settled(c):
				return false
	return true


## 源场景 → 产物树：Rig（转 180°）下挂 Skeleton3D+Body，静态资产只挂裸 Body；
## 登记了动作的再挂 AnimationPlayer。Anchor 标记留在根上，给表现层量头顶高度。
func _build(model: String, spec: Dictionary, scene: Node3D) -> Node3D:
	var bones: Array = []      # [{name, parent, rest}] —— 先序遍历，下标即骨骼号
	var parts: Array = []      # [{bone, xform, mesh, surfaces, im, csg}] —— 材质挂在实例上，源场景要留到合并完
	var anchors: Array = []    # [Transform3D]
	_walk(scene, -1, Transform3D.IDENTITY, bones, parts, anchors)

	var skinned := not bones.is_empty()
	var body := _body(parts, skinned)   # 顶点与材质到这一步才被新 ArrayMesh 接住，之后源场景才能放
	scene.free()
	# 源场景约定面朝 +Z，Godot 的 Node3D 前向是 -Z —— 导出时整体转 180°，游戏侧就能把实例朝向
	# 直接对准移动方向，不必在代码里补半个旋转。建筑与道具同一口径：产物一律「前脸朝 -Z」，
	# 游戏侧的朝向表按这条给（现例见宿主侧的建筑朝向表）。
	var facing := Node3D.new()
	facing.name = "Rig"
	facing.rotation_degrees = Vector3(0, 180, 0)
	var names := {}
	if skinned:
		var skel := Skeleton3D.new()
		skel.name = "Skeleton3D"
		for b in bones:
			skel.add_bone(b.name)
		for i in bones.size():
			skel.set_bone_parent(i, bones[i].parent)
			skel.set_bone_rest(i, bones[i].rest)
		skel.reset_bone_poses()
		body.skin = skel.create_skin_from_rest_transforms()
		body.skeleton = NodePath("..")
		facing.add_child(skel)
		skel.add_child(body)
		for i in bones.size():
			names[bones[i].name] = i
	else:
		facing.add_child(body)

	var out := Node3D.new()
	out.name = model
	out.add_child(facing)
	var clips: Array = spec.clips if spec.has("clips") else []
	if not clips.is_empty():
		facing.add_child(_player(clips, names, String(spec.lib) if spec.has("lib") else "human"))
	for a in anchors:
		var m := Marker3D.new()
		m.name = "Anchor"
		m.transform = a
		out.add_child(m)
	print("== %s 源→模型: 骨 %d 件 %d 面 %d 顶点 %d 动作 %d" % [
			model, bones.size(), parts.size(), body.mesh.get_surface_count(),
			_vert_count(body), clips.size()])
	return out


# 递归走源场景：j_ 节点 = 骨骼，其余图元 = 刚体件，蒙皮给它最近的祖先骨骼。
# 顶点一律烘进**模型空间**（从场景根累乘下来）—— 蒙皮的绑定矩阵负责「模型空间 → 骨骼空间」，
# 若在这里就用关节局部系，每个部件都会缩到自己关节的原点附近，模型塌成一堆（实测踩过）。
func _walk(node: Node, bone: int, abs: Transform3D, bones: Array, parts: Array, anchors: Array) -> void:
	for c in node.get_children():
		if not (c is Node3D):
			continue
		var n := c as Node3D
		if String(n.name) == "Anchor":
			anchors.append(abs * n.transform)
			continue
		if String(n.name).begins_with("j_"):
			var idx := bones.size()
			# 骨骼的 rest 是「相对父骨」的局部变换 —— 源场景里关节本来就是嵌套的，直接取
			bones.append({ "name": String(n.name).trim_prefix("j_"), "parent": bone, "rest": n.transform })
			_walk(n, idx, abs * n.transform, bones, parts, anchors)
		elif n is CSGShape3D:
			# 合并网格已经含全部子刷形状，这一棵子树到此为止，不再往下递归
			var baked: ArrayMesh = (n as CSGShape3D).bake_static_mesh()
			if baked != null:
				parts.append({ "bone": bone, "xform": abs * n.transform, "mesh": baked,
						"surfaces": _surfaces(baked), "im": null, "csg": n as GeometryInstance3D })
		elif n is MeshInstance3D and (n as MeshInstance3D).mesh != null:
			var m: Mesh = (n as MeshInstance3D).mesh
			parts.append({ "bone": bone, "xform": abs * n.transform, "mesh": m,
					"surfaces": _surfaces(m), "im": n as MeshInstance3D, "csg": null })
			_walk(n, bone, abs * n.transform, bones, parts, anchors)
		else:
			_walk(n, bone, abs * n.transform, bones, parts, anchors)


func _surfaces(m: Mesh) -> Array:
	var s: Array = []
	for i in m.get_surface_count():
		s.append(i)
	return s


func _body(parts: Array, skinned: bool) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	mi.name = "Body"
	mi.mesh = _merge(parts, skinned)
	return mi


func _part_material(p: Dictionary, surface: int) -> Material:
	if p.im != null:
		# 图元的表面材质挂在实例上（surface_material_override/N），不在共享的 Mesh 资源上
		var ov: Material = p.im.get("surface_material_override/%d" % surface)
		return ov if ov != null else p.mesh.surface_get_material(surface)
	return p.csg.material_override


# 按材质分桶合并成一个 ArrayMesh；蒙皮时每个部件的顶点 100% 归它自己的骨骼（刚性蒙皮）
func _merge(parts: Array, skinned: bool) -> ArrayMesh:
	var buckets: Array = []
	for p in parts:
		var m: Mesh = p.mesh
		var xf: Transform3D = p.xform
		for s in p.surfaces:
			var a: Array = m.surface_get_arrays(s)
			var bk := _bucket_for(buckets, _part_material(p, s))
			var base: int = bk.verts.size()
			# 表面数组按可缺省处理：CSG 烘出来的是**无序（非索引）且无 UV** 的网格，直接赋给带类型的变量会报 Nil 赋值错
			var src_v = a[Mesh.ARRAY_VERTEX] if a[Mesh.ARRAY_VERTEX] != null else PackedVector3Array()
			var src_n = a[Mesh.ARRAY_NORMAL] if a[Mesh.ARRAY_NORMAL] != null else PackedVector3Array()
			var src_uv = a[Mesh.ARRAY_TEX_UV] if a[Mesh.ARRAY_TEX_UV] != null else PackedVector2Array()
			for i in src_v.size():
				bk.verts.append(xf * src_v[i])
				if i < src_n.size():
					bk.norms.append((xf.basis * src_n[i]).normalized())
				bk.uvs.append(src_uv[i] if i < src_uv.size() else Vector2.ZERO)
				if skinned:
					bk.bones.append_array([p.bone, 0, 0, 0])
					bk.weights.append_array([1.0, 0.0, 0.0, 0.0])
			var src_i = a[Mesh.ARRAY_INDEX] if a[Mesh.ARRAY_INDEX] != null else PackedInt32Array()
			if src_i.is_empty():
				for i in src_v.size():
					bk.idx.append(base + i)
			else:
				for i in src_i:
					bk.idx.append(base + i)

	var am := ArrayMesh.new()
	for bk in buckets:
		var arrays: Array = []
		arrays.resize(Mesh.ARRAY_MAX)
		arrays[Mesh.ARRAY_VERTEX] = bk.verts
		arrays[Mesh.ARRAY_NORMAL] = bk.norms
		arrays[Mesh.ARRAY_TEX_UV] = bk.uvs
		if skinned:
			arrays[Mesh.ARRAY_BONES] = bk.bones
			arrays[Mesh.ARRAY_WEIGHTS] = bk.weights
		arrays[Mesh.ARRAY_INDEX] = bk.idx
		am.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		am.surface_set_material(am.get_surface_count() - 1, bk.mat)
	return am


func _bucket_for(buckets: Array, mat: Material) -> Dictionary:
	for b in buckets:
		if b.mat == mat:
			return b
	var nb := { "mat": mat, "verts": PackedVector3Array(), "norms": PackedVector3Array(),
			"uvs": PackedVector2Array(), "bones": PackedInt32Array(),
			"weights": PackedFloat32Array(), "idx": PackedInt32Array() }
	buckets.append(nb)
	return nb


## 动作库按模型选：登记表里 `lib = "mount"` 走骑乘库（坐骑四肢 + 骑手上半身），`lib = "beast"`
## 走四足库，缺省走人形库。
## 为什么要三座库而不是往人形库里塞几条 —— 骑手模型身上挂着**两副骨架**，
## 人形的 walk 会让骑手在马上蹬腿，砍掉腿坐骑就站着滑行；见 clips_mount.gd 文件头。
## 四足同理：人形的骨名与轨道套不上四足骨架，巨兽会靠人形的步态原地掰腿。
func _player(clip_names: Array, boneIndex: Dictionary, clip_lib: String = "human") -> AnimationPlayer:
	var table := Clips
	if clip_lib == "mount":
		table = MountClips
	elif clip_lib == "beast":
		table = BeastClips
	elif clip_lib != "human":
		_fail("未知动作库 lib=\"" + clip_lib + "\"（现有：human / mount / beast）")
	var ap := AnimationPlayer.new()
	ap.name = "AnimationPlayer"
	var lib := AnimationLibrary.new()
	for cn in clip_names:
		var spec := table.build(String(cn))
		if spec.is_empty():
			push_error("动作库 " + clip_lib + " 里没有 " + String(cn))
			continue
		var a := Animation.new()
		a.length = float(spec.length)
		a.loop_mode = Animation.LOOP_LINEAR if bool(spec.loop) else Animation.LOOP_NONE
		for tr in spec.tracks:
			var bone := String(tr[0])
			if not boneIndex.has(bone):
				push_error("骨架里没有骨骼 " + bone + "（动作 " + String(cn) + "）")
				continue
			var t := a.add_track(Animation.TYPE_ROTATION_3D)
			a.track_set_path(t, NodePath("Skeleton3D:" + bone))
			for k in tr[1]:
				a.track_insert_key(t, float(k[0]), Clips.q(float(k[1]), float(k[2]), float(k[3])))
		lib.add_animation(String(cn), a)
	ap.add_animation_library("", lib)
	return ap


func _write(model: String, out: Node3D) -> void:
	var path := _out_root + model + ".glb"
	var dir := DirAccess.open("res://")
	dir.make_dir_recursive(path.get_base_dir().trim_prefix("res://"))
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(out, state)
	var werr := doc.write_to_filesystem(state, path)
	print("== %s 导出 %s append=%d write=%d %d 字节 动画 %d 蒙皮 %d" % [
			model, path, err, werr, _filesize(path), state.animations.size(), state.skins.size()])
	out.free()


func _vert_count(mi: MeshInstance3D) -> int:
	var total := 0
	for s in mi.mesh.get_surface_count():
		total += mi.mesh.surface_get_array_len(s)
	return total


func _arg(prefix: String) -> String:
	for a in OS.get_cmdline_user_args():
		if String(a).begins_with(prefix):
			return String(a).trim_prefix(prefix)
	return ""


func _filesize(path: String) -> int:
	var f := FileAccess.open(path, FileAccess.READ)
	return f.get_length() if f != null else -1
