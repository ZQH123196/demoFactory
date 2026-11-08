extends Node

## 一次性验证：GDScript 能不能按小写方法名调到 C# 侧的方法，以及 C# 方法在引擎里登记成什么签名。
## 结果既 print 也写进 res://.cache/probe_result.txt（进程退出前日志可能来不及上传）。
## 结论记录在 docs/research/README.md。

const CANDIDATES: Array[String] = [
	"tick",
	"_safe_tick",
	"interrupt",
	"TickPascal",
	"tick_pascal",
	"get_class_name",
	"Ping",
	"ping",
	"not_written_at_all",
]

var _signatures: Dictionary = {}
var _out: PackedStringArray = PackedStringArray()


func _ready() -> void:
	var cs_script: Script = load("res://src/_Probe/BeetProbe.cs")
	_log("cs_script_loaded=%s" % (cs_script != null))
	if cs_script == null:
		_finish()
		return

	var subject: Node = Node.new()
	subject.set_script(cs_script)
	add_child(subject)

	var fake_blackboard := Node.new()
	fake_blackboard.name = "FakeBlackboard"
	add_child(fake_blackboard)

	_collect_signatures(subject)

	for candidate in CANDIDATES:
		_probe_call(subject, candidate, fake_blackboard)

	var duck = subject
	_log("direct_syntax tick=%s" % duck.tick(self, fake_blackboard))

	subject.free()
	fake_blackboard.free()
	_finish()


func _collect_signatures(target: Object) -> void:
	var names: Array[String] = []
	for info in target.get_method_list():
		var mname := str(info["name"])
		if _signatures.has(mname):
			continue
		var arg_types: Array[String] = []
		for arg in info["args"]:
			var arg_class := str(arg.get("class_name", ""))
			arg_types.append(arg_class if arg_class != "" else _type_name(int(arg.get("type", 0))))
		_signatures[mname] = {
			"argc": (info["args"] as Array).size(),
			"args": arg_types,
			"ret": _describe_return(info.get("return", {})),
		}
		names.append(mname)
	_log("method_count=%d" % names.size())
	names.sort()
	for mname in names:
		if not (mname in CANDIDATES):
			continue
		var sig: Dictionary = _signatures[mname]
		_log("sig %s argc=%s args=%s ret=%s" % [mname, sig.argc, sig.args, sig.ret])


func _probe_call(target: Object, mname: String, blackboard: Node) -> void:
	if not target.has_method(mname):
		_log("call %-18s has_method=false" % mname)
		return
	var sig: Dictionary = _signatures.get(mname, {})
	var argc: int = int(sig.get("argc", -1))
	var args: Array = []
	for i in argc:
		args.append(self if i == 0 else blackboard)
	var callable := Callable(target, mname)
	if not callable.is_valid():
		_log("call %-18s has_method=true callable=INVALID" % mname)
		return
	var ret = callable.callv(args)
	_log("call %-18s has_method=true callable=ok ret=%s" % [mname, ret])


func _log(line: String) -> void:
	_out.append(line)
	print("PROBE ", line)


func _finish() -> void:
	DirAccess.make_dir_recursive_absolute("res://.cache")
	var file := FileAccess.open("res://.cache/probe_result.txt", FileAccess.WRITE)
	if file != null:
		file.store_string("\n".join(_out) + "\n")
		file.close()
	get_tree().quit()


func _describe_return(ret_info: Variant) -> String:
	if ret_info == null or typeof(ret_info) != TYPE_DICTIONARY:
		return "?"
	var ret_class := str(ret_info.get("class_name", ""))
	if ret_class != "":
		return ret_class
	return _type_name(int(ret_info.get("type", 0)))


func _type_name(type_id: int) -> String:
	match type_id:
		TYPE_NIL:
			return "nil"
		TYPE_BOOL:
			return "bool"
		TYPE_INT:
			return "int"
		TYPE_FLOAT:
			return "float"
		TYPE_STRING:
			return "String"
		TYPE_ARRAY:
			return "Array"
		TYPE_DICTIONARY:
			return "Dictionary"
		TYPE_OBJECT:
			return "Object"
		TYPE_STRING_NAME:
			return "StringName"
		_:
			return "type:%d" % type_id
