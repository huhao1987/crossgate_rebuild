## 地图显示：地面层只画屏幕可见范围；物件和角色放进 y_sort 层按深度排序。
## 同时生成通行网格供寻路使用。
class_name MapView
extends Node2D

signal tile_clicked(cell: Vector2i)

var lib: CgLibrary
var map: CgMap
var cfg: Dictionary
var walkable := PackedByteArray()      # 1 = 可走
var ground_layer: Node2D
var entity_layer: Node2D
var collision_enabled := true
var _last_view := Rect2i()
var _missing := 0

func setup(p_lib: CgLibrary, p_map: CgMap, p_cfg: Dictionary) -> void:
	lib = p_lib
	map = p_map
	cfg = p_cfg
	ground_layer = _GroundLayer.new()
	ground_layer.view = self
	add_child(ground_layer)
	entity_layer = Node2D.new()
	entity_layer.y_sort_enabled = true
	add_child(entity_layer)
	_build_walkable()
	_spawn_objects()

func _build_walkable() -> void:
	var gflag: int = int(cfg.get("ground_walk_flag", 1))
	var oflag: int = int(cfg.get("object_pass_flag", 1))
	walkable.resize(map.w * map.h)
	for y in map.h:
		for x in map.w:
			var i := map.idx(x, y)
			var ok := false
			var g := lib.graphic_by_map_id(map.ground[i])
			if not g.is_empty():
				ok = (g[1] as CgGraphics.Info).flag == gflag
			walkable[i] = 1 if ok else 0
	# 可走物件（桥、木板路等）把占到的格子变成可走。
	# 依据：用真实数据验证，加上这条后传送落点可走率 78.6% → 82.4%，连通性不变（99.8%）。
	if cfg.get("passable_objects_make_floor", true):
		for y in map.h:
			for x in map.w:
				var pm := map.objects[map.idx(x, y)]
				if pm == 0:
					continue
				var po := lib.graphic_by_map_id(pm)
				if po.is_empty() or (po[1] as CgGraphics.Info).flag != oflag:
					continue
				var pinf: CgGraphics.Info = po[1]
				for dy in maxi(pinf.south, 1):
					for dx in maxi(pinf.east, 1):
						if map.in_bounds(x + dx, y - dy):
							walkable[map.idx(x + dx, y - dy)] = 1
	# 物件占位：从锚点往东（+x）east 格、往北（-y）south 格。
	# 依据：真实 GraphicInfo 中多格物件的 ox ≈ -32×south、w ≈ 32×(east+south)，
	# 即南向格数让图像向屏幕左上延伸，锚点是占位区最靠前（屏幕最下方）的格子。
	# 不可通行的物件（flag != object_pass_flag）把占到的格子标为不可走。
	for y in map.h:
		for x in map.w:
			var mid := map.objects[map.idx(x, y)]
			if mid == 0:
				continue
			var o := lib.graphic_by_map_id(mid)
			if o.is_empty():
				continue
			var inf: CgGraphics.Info = o[1]
			if inf.flag == oflag:
				continue
			for dy in maxi(inf.south, 1):
				for dx in maxi(inf.east, 1):
					var cx := x + dx
					var cy := y - dy
					if map.in_bounds(cx, cy):
						walkable[map.idx(cx, cy)] = 0

func is_walkable(c: Vector2i) -> bool:
	if not map.in_bounds(c.x, c.y):
		return false
	return (not collision_enabled) or walkable[map.idx(c.x, c.y)] == 1

func _spawn_objects() -> void:
	for y in map.h:
		for x in map.w:
			var mid := map.objects[map.idx(x, y)]
			if mid == 0:
				continue
			var o := lib.graphic_by_map_id(mid)
			if o.is_empty():
				_missing += 1
				continue
			var gs: CgGraphics = o[0]
			var inf: CgGraphics.Info = o[1]
			var tex := gs.texture_for(inf)
			if tex == null:
				continue
			var s := Sprite2D.new()
			s.texture = tex
			s.centered = false
			# 锚点本身就是占位区最靠前的格子，直接用它做深度排序点
			s.position = Iso.tile_to_screen(Vector2(x, y))
			s.offset = Vector2(inf.ox, inf.oy)
			entity_layer.add_child(s)

func missing_tiles() -> int:
	return _missing + (ground_layer as _GroundLayer).missing

func _process(_dt: float) -> void:
	var cam := get_viewport().get_camera_2d()
	if cam == null:
		return
	var vp := get_viewport_rect().size / cam.zoom
	var r := Rect2(cam.get_screen_center_position() - vp * 0.5, vp)
	var corners := [r.position, r.position + Vector2(r.size.x, 0), r.position + Vector2(0, r.size.y), r.end]
	var mn := Vector2i(1 << 30, 1 << 30)
	var mx := Vector2i(-(1 << 30), -(1 << 30))
	for p in corners:
		var c := Iso.screen_to_cell(p)
		mn = Vector2i(mini(mn.x, c.x), mini(mn.y, c.y))
		mx = Vector2i(maxi(mx.x, c.x), maxi(mx.y, c.y))
	var view := Rect2i(mn - Vector2i(2, 2), mx - mn + Vector2i(5, 5))
	if view != _last_view:
		_last_view = view
		(ground_layer as _GroundLayer).visible_cells = view
		ground_layer.queue_redraw()

func _unhandled_input(e: InputEvent) -> void:
	var press := false
	var pos := Vector2.ZERO
	if e is InputEventMouseButton and e.pressed and e.button_index == MOUSE_BUTTON_LEFT:
		press = true
		pos = e.position
	elif e is InputEventScreenTouch and e.pressed:
		press = true
		pos = e.position
	if press:
		var world := get_canvas_transform().affine_inverse() * pos
		tile_clicked.emit(Iso.screen_to_cell(world - global_position))


class _GroundLayer extends Node2D:
	var view: MapView
	var visible_cells := Rect2i()
	var missing := 0

	func _draw() -> void:
		if view == null:
			return
		var m := view.map
		var x0 := clampi(visible_cells.position.x, 0, m.w)
		var y0 := clampi(visible_cells.position.y, 0, m.h)
		var x1 := clampi(visible_cells.end.x, 0, m.w)
		var y1 := clampi(visible_cells.end.y, 0, m.h)
		missing = 0
		for y in range(y0, y1):
			for x in range(x0, x1):
				var mid := m.ground[m.idx(x, y)]
				if mid == 0:
					continue
				var g := view.lib.graphic_by_map_id(mid)
				if g.is_empty():
					missing += 1
					continue
				var inf: CgGraphics.Info = g[1]
				var tex := (g[0] as CgGraphics).texture_for(inf)
				if tex:
					draw_texture(tex, Iso.tile_to_screen(Vector2(x, y)) + Vector2(inf.ox, inf.oy))
				if view.cfg.get("debug_grid", false) and not view.is_walkable(Vector2i(x, y)):
					draw_circle(Iso.tile_to_screen(Vector2(x, y)), 4, Color(1, 0, 0, 0.6))
