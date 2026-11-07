## 入口：读取配置 → 加载资源 → 显示地图和角色 → 点击行走。
## 配置文件查找顺序：user://config.json → res://config.json。
## 游戏目录不存在时，自动退回 res://testdata（原创占位素材），保证工程能直接跑起来。
extends Node2D

var lib := CgLibrary.new()
var map := CgMap.new()
var view: MapView
var player: Player
var cam := Camera2D.new()
var hud := Label.new()
var cfg := {}

func _ready() -> void:
	cfg = _load_config()
	var root: String = cfg.get("game_root", "")
	if root == "" or not DirAccess.dir_exists_absolute(root):
		push_warning("游戏目录不存在（%s），改用内置测试素材" % root)
		cfg = _load_json("res://config.testdata.json")
		root = ProjectSettings.globalize_path("res://testdata")
	lib.setup(root, cfg.get("sets", []), cfg.get("palette", "bin/pal/palet_00.cgp"))
	var map_path := root.path_join(String(cfg.get("map", "map/0/1.dat")))
	if not map.load(map_path):
		hud.text = "地图加载失败：%s" % map_path
		add_child(hud)
		return
	view = MapView.new()
	view.setup(lib, map, cfg)
	add_child(view)
	player = Player.new()
	var start := _find_start(Vector2i(cfg.get("start", [map.w / 2, map.h / 2])[0], cfg.get("start", [map.w / 2, map.h / 2])[1]))
	player.setup(lib, view, cfg, start)
	view.entity_layer.add_child(player)
	view.tile_clicked.connect(player.walk_to)
	cam.zoom = Vector2.ONE * float(cfg.get("zoom", 1.0))
	player.add_child(cam)
	cam.make_current()
	var layer := CanvasLayer.new()
	add_child(layer)
	hud.position = Vector2(8, 8)
	layer.add_child(hud)

func _process(_dt: float) -> void:
	if player:
		hud.text = "%s 地图 %dx%d  位置 (%d,%d)  方向 %d  缺图 %d  FPS %d\n点击/触摸地面行走 · C 切换碰撞 · G 显示不可走格 · 滚轮缩放" % [
			_map_title(), map.w, map.h, player.cell.x, player.cell.y, player.dir, view.missing_tiles(), Engine.get_frames_per_second()]

func _unhandled_input(e: InputEvent) -> void:
	if e is InputEventKey and e.pressed:
		if e.keycode == KEY_C:
			view.collision_enabled = not view.collision_enabled
			player.refresh_solid()
		elif e.keycode == KEY_G:
			cfg["debug_grid"] = not cfg.get("debug_grid", false)
			view.ground_layer.queue_redraw()
	elif e is InputEventMouseButton and e.pressed:
		if e.button_index == MOUSE_BUTTON_WHEEL_UP:
			cam.zoom = (cam.zoom * 1.1).clamp(Vector2.ONE * 0.3, Vector2.ONE * 4)
		elif e.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			cam.zoom = (cam.zoom / 1.1).clamp(Vector2.ONE * 0.3, Vector2.ONE * 4)
	elif e is InputEventMagnifyGesture:
		cam.zoom = (cam.zoom * e.factor).clamp(Vector2.ONE * 0.3, Vector2.ONE * 4)

func _map_title() -> String:
	# 服务端地图文件名形如 "1000_地名"，取下划线后面的部分；否则用文件内的名称
	var fn := String(cfg.get("map", "")).get_file()
	if fn.contains("_"):
		return "[%s]" % fn.get_slice("_", 1)
	return "[%s]" % map.display_name if map.display_name != "" else ""

func _find_start(want: Vector2i) -> Vector2i:
	# 从期望位置向外找最近的可走格
	for r in range(0, maxi(map.w, map.h)):
		for dy in range(-r, r + 1):
			for dx in range(-r, r + 1):
				var c := want + Vector2i(dx, dy)
				if view.is_walkable(c):
					return c
	return want

func _load_config() -> Dictionary:
	for p in ["user://config.json", "res://config.json"]:
		if FileAccess.file_exists(p):
			return _load_json(p)
	return {}

static func _load_json(p: String) -> Dictionary:
	var f := FileAccess.open(p, FileAccess.READ)
	if f == null:
		return {}
	var d = JSON.parse_string(f.get_as_text())
	return d if d is Dictionary else {}
