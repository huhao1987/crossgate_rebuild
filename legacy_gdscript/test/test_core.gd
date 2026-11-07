## 无头测试：用原创占位素材验证资源层，并把地图合成一张 PNG 检查等距排布。
## 运行：godot --headless --path . -s res://test/test_core.gd
extends SceneTree

func _init() -> void:
	var root := ProjectSettings.globalize_path("res://testdata")
	var cfg: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://config.testdata.json"))
	var lib := CgLibrary.new()
	lib.setup(root, cfg["sets"], cfg["palette"])
	var gs: CgGraphics = lib.graphic_sets[0]
	var sums := []
	for it in gs.infos:
		var d := gs.decode_indices(it)
		var s := 0
		for b in (d["pixels"] as PackedByteArray):
			s = (s * 31 + b) % 1000000007
		sums.append(s)
	print("GRAPHICS ", gs.infos.size(), " CHECKSUM ", hash(str(sums)), " FIRST ", sums.slice(0, 4))
	var acts := lib.anime_actions(900000)
	print("ANIME actions=", acts.size(), " walk dir2 frames=", (acts["2,1"] as CgAnime.Action).frames)
	var m := CgMap.new()
	print("MAP ok=", m.load(root.path_join("map/0/1.dat")), " ", m.w, "x", m.h)
	# CPU 合成：先地面，再按深度画物件
	var img := Image.create(64 * 42, 48 * 42, false, Image.FORMAT_RGBA8)
	img.fill(Color(0.1, 0.1, 0.12))
	var origin := Vector2(40, 48 * 21)
	for y in m.h:
		for x in m.w:
			var g := lib.graphic_by_map_id(m.ground[m.idx(x, y)])
			if g.is_empty(): continue
			var inf: CgGraphics.Info = g[1]
			var gi := (g[0] as CgGraphics).image_for(inf)
			img.blend_rect(gi, Rect2i(0, 0, gi.get_width(), gi.get_height()), Vector2i(origin + Iso.tile_to_screen(Vector2(x, y)) + Vector2(inf.ox, inf.oy)))
	var objs := []
	for y in m.h:
		for x in m.w:
			if m.objects[m.idx(x, y)] != 0: objs.append(Vector2i(x, y))
	objs.sort_custom(func(a, b): return Iso.tile_to_screen(Vector2(a)).y < Iso.tile_to_screen(Vector2(b)).y)
	for c in objs:
		var o := lib.graphic_by_map_id(m.objects[m.idx(c.x, c.y)])
		var inf: CgGraphics.Info = o[1]
		var oi := (o[0] as CgGraphics).image_for(inf)
		img.blend_rect(oi, Rect2i(0, 0, oi.get_width(), oi.get_height()), Vector2i(origin + Iso.tile_to_screen(Vector2(c)) + Vector2(inf.ox, inf.oy)))
	# 角色 8 个方向站立帧排成一行
	for d in 8:
		var a: CgAnime.Action = acts["%d,0" % d]
		var r := lib.graphic_by_id(a.frames[0])
		var ci := (r[0] as CgGraphics).image_for(r[1])
		img.blend_rect(ci, Rect2i(0, 0, ci.get_width(), ci.get_height()), Vector2i(60 + d * 50, 20))
	img.save_png("res://test/out_map.png")
	print("PNG saved")
	quit()
