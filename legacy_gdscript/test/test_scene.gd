## 运行完整场景，模拟一次点击行走，期间截两张图。
extends SceneTree
var main: Node
var frames := 0
func _init() -> void:
	main = load("res://main.tscn").instantiate()
	root.add_child(main)
func _process(_dt: float) -> bool:
	frames += 1
	if frames == 10:
		var p: Player = main.player
		print("start cell ", p.cell, " walkable=", main.view.is_walkable(p.cell))
		for t in [Vector2i(26, 12), Vector2i(25, 25), Vector2i(10, 30)]:
			print("target ", t, " walkable=", main.view.is_walkable(t), " astar_path=", p._astar.get_id_path(p.cell, t).size(), " solid=", p._astar.is_point_solid(t))
		p.walk_to(Vector2i(25, 25))
		print("path len ", p._path.size())
	if frames == 40:
		root.get_texture().get_image().save_png("res://test/out_scene_walking.png")
		print("mid cell ", main.player.cell, " dir ", main.player.dir)
	if frames == 400:
		root.get_texture().get_image().save_png("res://test/out_scene_end.png")
		print("end cell ", main.player.cell, " missing ", main.view.missing_tiles())
		return true
	return false
