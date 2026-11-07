## 两种地图格式读出来应完全一致
extends SceneTree
func _init() -> void:
	var a := CgMap.new(); var b := CgMap.new()
	var ok1 := a.load(ProjectSettings.globalize_path("res://testdata/map/0/1.dat"))
	var ok2 := b.load(ProjectSettings.globalize_path("res://testdata/server_map/1_TestField"))
	print("client ", ok1, " ", a.format, " ", a.w, "x", a.h)
	print("server ", ok2, " ", b.format, " ", b.w, "x", b.h, " name=", b.display_name)
	print("ground equal ", a.ground == b.ground, "  objects equal ", a.objects == b.objects)
	quit()
