extends Node3D

# 预览台的自动播放开关（开发期脚手架，不进游戏运行时）。
#
# 为什么要这么一层：模型的动作要在近景下逐条看，而游戏运行时里单位由逻辑流驱动、
# 还得先让它活到出场时刻。验收台只干一件事——把这个节点下面所有 AnimationPlayer
# 的某条 clip 播起来，造型与动作对不对一眼能判。

@export var clip: StringName = &"walk"
@export var cycle: bool = true


func _ready() -> void:
	var ps := _players(self)
	if ps.is_empty():
		return
	if not cycle:
		for p in ps:
			if p.has_animation(clip):
				p.play(clip)
		return
	await _cycle_all(ps)


# 按顺序把每条动作播一遍再循环 —— 一次截图会话就能看完全部动作，
# 不必每验一条就重跑一次预览场景。
func _cycle_all(ps: Array[AnimationPlayer]) -> void:
	var names: PackedStringArray = ps[0].get_animation_list()
	if names.is_empty():
		return
	var i := 0
	while true:
		var longest := 0.0
		for p in ps:
			if p.has_animation(names[i]):
				p.play(names[i])
				longest = maxf(longest, p.get_animation(names[i]).length)
		await get_tree().create_timer(longest + 0.5).timeout
		i = (i + 1) % names.size()


func _players(n: Node) -> Array[AnimationPlayer]:
	var out: Array[AnimationPlayer] = []
	for c in n.get_children():
		if c is AnimationPlayer:
			out.append(c)
		out.append_array(_players(c))
	return out
