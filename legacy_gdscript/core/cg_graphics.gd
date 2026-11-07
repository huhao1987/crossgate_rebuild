## 图像库：一组 GraphicInfo + Graphic 文件。按编号或"地图编号"取图，解码成 Texture2D 并缓存。
##
## GraphicInfo 每条 40 字节：
##   int32 id | uint32 offset | uint32 length | int32 ox | int32 oy | int32 w | int32 h
##   uint8 east | uint8 south | uint8 flag | 5 字节保留 | int32 map_id
## Graphic 块：'RD' | ver(1) | ?(1) | int32 w | int32 h | int32 len | [ver>=2: int32 pal_len] | 数据
##   ver 奇数 = RLE 压缩；ver>=2 时像素后面跟 pal_len 字节的内嵌调色板（BGR）。
class_name CgGraphics
extends RefCounted

const ENTRY_SIZE := 40

class Info:
	var id: int
	var offset: int
	var length: int
	var ox: int
	var oy: int
	var w: int
	var h: int
	var east: int
	var south: int
	var flag: int
	var map_id: int

var name := ""
var infos: Array[Info] = []
var by_id := {}        # id -> Info
var by_map_id := {}    # map_id -> Info
var palette: CgPalette

var _gfx_path := ""
var _gfx: FileAccess
var _tex_cache := {}   # id -> Texture2D
var _cache_order: Array[int] = []
var cache_limit := 4096

func _init(p_palette: CgPalette) -> void:
	palette = p_palette

## 加载一对文件。重复加载多组时，后加载的同编号会覆盖先加载的（与客户端的覆盖顺序一致）。
func load_pair(info_path: String, gfx_path: String, p_name := "") -> bool:
	name = p_name
	var f := FileAccess.open(info_path, FileAccess.READ)
	if f == null:
		push_warning("CgGraphics: 打不开 %s" % info_path)
		return false
	var n := f.get_length() / ENTRY_SIZE
	infos.resize(0)
	for i in n:
		var it := Info.new()
		it.id = _s32(f.get_32())
		it.offset = f.get_32()
		it.length = f.get_32()
		it.ox = _s32(f.get_32())
		it.oy = _s32(f.get_32())
		it.w = _s32(f.get_32())
		it.h = _s32(f.get_32())
		it.east = f.get_8()
		it.south = f.get_8()
		it.flag = f.get_8()
		f.get_buffer(5)
		it.map_id = _s32(f.get_32())
		infos.append(it)
		by_id[it.id] = it
		if it.map_id != 0:
			by_map_id[it.map_id] = it
	_gfx_path = gfx_path
	_gfx = FileAccess.open(gfx_path, FileAccess.READ)
	if _gfx == null:
		push_warning("CgGraphics: 打不开 %s" % gfx_path)
		return false
	return true

static func _s32(v: int) -> int:
	return v - 0x100000000 if v >= 0x80000000 else v

func has_id(id: int) -> bool:
	return by_id.has(id)

func info_for_map_id(map_id: int) -> Info:
	return by_map_id.get(map_id)

## 解码出调色板索引数组；失败返回空字典。
## 返回 { w, h, pixels: PackedByteArray, pal: PackedByteArray }
func decode_indices(it: Info) -> Dictionary:
	if _gfx == null or it == null:
		return {}
	_gfx.seek(it.offset)
	var blk := _gfx.get_buffer(it.length)
	if blk.size() < 16 or blk[0] != 0x52 or blk[1] != 0x44:  # 'RD'
		return {}
	var ver := blk[2]
	var w := blk.decode_s32(4)
	var h := blk.decode_s32(8)
	var ln := blk.decode_s32(12)
	var hdr := 16
	var pal_len := 0
	if ver >= 2:
		pal_len = blk.decode_s32(16)
		hdr = 20
	var body := blk.slice(hdr, mini(ln, blk.size()))
	var pix: PackedByteArray
	if ver & 1:
		pix = CgRle.decode(body, w * h + pal_len)
	else:
		pix = body
	if pix.size() < w * h:
		pix.resize(w * h)  # 残缺数据补透明，避免崩溃
	var out := {"w": w, "h": h, "pixels": pix.slice(0, w * h), "pal": PackedByteArray()}
	if pal_len > 0 and pix.size() >= w * h + pal_len:
		out["pal"] = pix.slice(w * h, w * h + pal_len)
	return out

## 取 RGBA 图像（索引 0 透明）。
func image_for(it: Info) -> Image:
	var d := decode_indices(it)
	if d.is_empty():
		return null
	var pal := palette
	if (d["pal"] as PackedByteArray).size() > 0:
		pal = palette.with_embedded(d["pal"])
	var w: int = d["w"]
	var h: int = d["h"]
	var src: PackedByteArray = d["pixels"]
	var rgba := PackedByteArray()
	rgba.resize(w * h * 4)
	var cols := pal.colors
	for i in w * h:
		var c := src[i]
		if c == 0:
			continue  # 透明（rgba 默认 0）
		var col := cols[c]
		var o := i * 4
		rgba[o] = int(col.r8)
		rgba[o + 1] = int(col.g8)
		rgba[o + 2] = int(col.b8)
		rgba[o + 3] = 255
	return Image.create_from_data(w, h, false, Image.FORMAT_RGBA8, rgba)

func texture_for(it: Info) -> Texture2D:
	if it == null:
		return null
	if _tex_cache.has(it.id):
		return _tex_cache[it.id]
	var img := image_for(it)
	if img == null:
		return null
	var tex := ImageTexture.create_from_image(img)
	_tex_cache[it.id] = tex
	_cache_order.append(it.id)
	if _cache_order.size() > cache_limit:
		_tex_cache.erase(_cache_order.pop_front())
	return tex
