## 魔力宝贝调色板：256 色。0–15、240–255 为系统色，16–239 来自 .cgp 文件（224 色，BGR）。
## 索引 0 视为透明。
class_name CgPalette
extends RefCounted

# 系统色（RGB）。注意：这两组数值来自社区资料的记忆，需拿真实客户端截图核对。
const SYS_LOW := [
	0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xC0C0C0,
	0xC0DCC0, 0xA6CAF0, 0xDE0000, 0xFF5F00, 0xFFFFA0, 0x005FD2, 0x50D2FF, 0x28E128,
]
const SYS_HIGH := [
	0xF5C396, 0x1EA05F, 0xC37D46, 0x9B551E, 0x464137, 0x28231E, 0xFFFBF0, 0xA0A0A4,
	0x808080, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
]

var colors := PackedColorArray()  # 256 项

func _init() -> void:
	colors.resize(256)
	for i in 16:
		colors[i] = _rgb(SYS_LOW[i])
		colors[240 + i] = _rgb(SYS_HIGH[i])
	for i in range(16, 240):
		colors[i] = Color(1, 0, 1)  # 未加载时显示品红，便于发现问题

static func _rgb(v: int) -> Color:
	return Color8((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)

## 读取 .cgp（224 × BGR）。成功返回 true。
func load_cgp(path: String) -> bool:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		push_warning("CgPalette: 打不开 %s" % path)
		return false
	var d := f.get_buffer(f.get_length())
	var n := mini(d.size() / 3, 224)
	for i in n:
		colors[16 + i] = Color8(d[i * 3 + 2], d[i * 3 + 1], d[i * 3])
	return true

## 用图像自带的调色板（版本 ≥2 的 RD 块，BGR 三元组）覆盖得到一份新调色板。
func with_embedded(pal_bytes: PackedByteArray) -> CgPalette:
	var p := CgPalette.new()
	p.colors = colors.duplicate()
	var n := mini(pal_bytes.size() / 3, 256)
	for i in n:
		p.colors[i] = Color8(pal_bytes[i * 3 + 2], pal_bytes[i * 3 + 1], pal_bytes[i * 3])
	return p
