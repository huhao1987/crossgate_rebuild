## 角色：8 方向动画（站立 / 行走），沿 A* 路径逐格移动。
class_name Player
extends Node2D

var lib: CgLibrary
var view: MapView
var anime_id := 0
var act_stand := 0
var act_walk := 1
var dir_offset := 0          # 动画方向编号与屏幕方向的偏移，实测不对时在 config 里调
var speed_tiles := 4.0       # 每秒走几格

var cell := Vector2i.ZERO
var dir := 4
var _path: Array[Vector2i] = []
var _from := Vector2.ZERO
var _to := Vector2.ZERO
var _t := 1.0
var _actions := {}
var _frame_time := 0.0
var _sprite := Sprite2D.new()
var _astar := AStarGrid2D.new()

# 屏幕方向（与动画方向 0..7 对应）：上、右上、右、右下、下、左下、左、左上
const SCREEN_DIRS := [Vector2(0, -1), Vector2(1, -1), Vector2(1, 0), Vector2(1, 1),
	Vector2(0, 1), Vector2(-1, 1), Vector2(-1, 0), Vector2(-1, -1)]

func setup(p_lib: CgLibrary, p_view: MapView, cfg: Dictionary, start: Vector2i) -> void:
	lib = p_lib
	view = p_view
	anime_id = int(cfg.get("player_anime", 0))
	act_stand = int(cfg.get("act_stand", 0))
	act_walk = int(cfg.get("act_walk", 1))
	dir_offset = int(cfg.get("dir_offset", 0))
	_actions = lib.anime_actions(anime_id)
	_sprite.centered = false
	add_child(_sprite)
	cell = start
	position = Iso.tile_to_screen(Vector2(cell))
	_astar.region = Rect2i(0, 0, view.map.w, view.map.h)
	_astar.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_ONLY_IF_NO_OBSTACLES
	_astar.update()
	refresh_solid()

func refresh_solid() -> void:
	for y in view.map.h:
		for x in view.map.w:
			_astar.set_point_solid(Vector2i(x, y), not view.is_walkable(Vector2i(x, y)))

func walk_to(target: Vector2i) -> void:
	if not view.is_walkable(target):
		return
	var start := cell if _t >= 1.0 else _next_cell()
	var p := _astar.get_id_path(start, target)
	_path.clear()
	for i in range(1, p.size()):
		_path.append(p[i])

func _next_cell() -> Vector2i:
	return Vector2i(Iso.screen_to_cell(_to))

func _process(dt: float) -> void:
	if _t < 1.0:
		_t = minf(1.0, _t + dt * speed_tiles)
		position = _from.lerp(_to, _t)
		if _t >= 1.0:
			cell = _next_cell()
	if _t >= 1.0 and not _path.is_empty():
		var nxt: Vector2i = _path.pop_front()
		_from = Iso.tile_to_screen(Vector2(cell))
		_to = Iso.tile_to_screen(Vector2(nxt))
		dir = _dir_for(_to - _from)
		_t = 0.0
	_animate(dt, _t < 1.0)

func _dir_for(v: Vector2) -> int:
	var best := 0
	var best_dot := -INF
	for i in SCREEN_DIRS.size():
		var d: float = v.normalized().dot((SCREEN_DIRS[i] as Vector2).normalized())
		if d > best_dot:
			best_dot = d
			best = i
	return best

func _animate(dt: float, moving: bool) -> void:
	var adir := (dir + dir_offset) % 8
	var a: CgAnime.Action = _actions.get("%d,%d" % [adir, act_walk if moving else act_stand])
	if a == null:
		a = _actions.get("%d,%d" % [adir, act_stand])
	if a == null or a.frames.is_empty():
		return
	_frame_time += dt * 1000.0
	var per := maxf(1.0, float(a.duration_ms) / a.frames.size())
	var fi := int(_frame_time / per) % a.frames.size()
	var r := lib.graphic_by_id(a.frames[fi])
	if r.is_empty():
		return
	var inf: CgGraphics.Info = r[1]
	_sprite.texture = (r[0] as CgGraphics).texture_for(inf)
	_sprite.offset = Vector2(inf.ox, inf.oy)
