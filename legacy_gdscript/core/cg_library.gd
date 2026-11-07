## 资源总管：按配置加载多组图像/动画（对应 cgmsv.yml 的 BinLoader 分组），统一按编号查找。
## 运行时从用户自己的游戏目录读取，工程本身不包含任何原版素材。
class_name CgLibrary
extends RefCounted

var root := ""                       # 游戏根目录（包含 bin/、map/）
var palette := CgPalette.new()
var graphic_sets: Array[CgGraphics] = []
var anime_sets: Array[CgAnime] = []

## sets: 数组，每项 { "name", "graphic", "graphic_info", "anime", "anime_info", "anime_header" }
## 路径相对于 root，可用 / 或 \ 分隔。
func setup(p_root: String, sets: Array, palette_file := "bin/pal/palet_00.cgp") -> void:
	root = p_root
	palette.load_cgp(_p(palette_file))
	for s in sets:
		var gi: String = s.get("graphic_info", "")
		var g: String = s.get("graphic", "")
		if gi != "" and g != "" and FileAccess.file_exists(_p(gi)):
			var gs := CgGraphics.new(palette)
			if gs.load_pair(_p(gi), _p(g), s.get("name", "")):
				graphic_sets.append(gs)
		var ai: String = s.get("anime_info", "")
		var a: String = s.get("anime", "")
		if ai != "" and a != "" and FileAccess.file_exists(_p(ai)):
			var an := CgAnime.new()
			if an.load_pair(_p(ai), _p(a), int(s.get("anime_header", 12))):
				anime_sets.append(an)

func _p(rel: String) -> String:
	return root.path_join(rel.replace("\\", "/"))

## 后加载的组优先（与客户端覆盖顺序一致）
func graphic_by_id(id: int) -> Array:
	for i in range(graphic_sets.size() - 1, -1, -1):
		var gs := graphic_sets[i]
		if gs.by_id.has(id):
			return [gs, gs.by_id[id]]
	return []

func graphic_by_map_id(map_id: int) -> Array:
	for i in range(graphic_sets.size() - 1, -1, -1):
		var gs := graphic_sets[i]
		if gs.by_map_id.has(map_id):
			return [gs, gs.by_map_id[map_id]]
	return []

func texture_by_id(id: int) -> Texture2D:
	var r := graphic_by_id(id)
	return null if r.is_empty() else (r[0] as CgGraphics).texture_for(r[1])

func anime_actions(id: int) -> Dictionary:
	for i in range(anime_sets.size() - 1, -1, -1):
		if anime_sets[i].has_anime(id):
			return anime_sets[i].actions_for(id)
	return {}
