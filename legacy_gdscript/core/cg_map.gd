## 地图文件，自动识别两种格式：
##
## 1) 客户端地图（map/<楼层>/<编号>.dat），小端：
##    'MAP' + 版本(1) + 8 字节保留 | int32 宽(东向) | int32 高(南向)
##    uint16 地面层[宽×高] | uint16 物件层[宽×高] | uint16 标志层[宽×高]
##
## 2) 服务端地图（服务端 data/map/<编号>_<地名>），大端：
##    'LS2MAP' + 2 字节 | 32 字节地图名（GBK，形如 "地名|数字"）| uint16 宽 | uint16 高
##    uint16 地面层[宽×高] | uint16 物件层[宽×高] | [少数地图还有第三层及尾部数据]
##
## 层内按行存储：index = y * 宽 + x（x 向东，y 向南）。
## 地面/物件里的数值是"地图编号"，对应 GraphicInfo 的 map_id 字段，不是图像编号。
class_name CgMap
extends RefCounted

var w := 0
var h := 0
var ground := PackedInt32Array()
var objects := PackedInt32Array()
var flags := PackedInt32Array()
var format := ""          # "client" / "server"
var display_name := ""    # 服务端地图自带的名称（"|" 前面的部分）

func load(path: String) -> bool:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		push_warning("CgMap: 打不开 %s" % path)
		return false
	var d := f.get_buffer(f.get_length())
	if d.size() >= 44 and d.slice(0, 6).get_string_from_ascii() == "LS2MAP":
		return _load_server(d)
	if d.size() >= 20 and d[0] == 0x4D and d[1] == 0x41 and d[2] == 0x50:
		return _load_client(d)
	push_warning("CgMap: %s 不是可识别的地图文件" % path)
	return false

func _load_client(d: PackedByteArray) -> bool:
	format = "client"
	w = d.decode_s32(12)
	h = d.decode_s32(16)
	var n := w * h
	if d.size() < 20 + n * 4:
		return false
	ground = _u16s(d, 20, n, false)
	objects = _u16s(d, 20 + n * 2, n, false)
	flags = _u16s(d, 20 + n * 4, n, false) if d.size() >= 20 + n * 6 else PackedInt32Array()
	return true

func _load_server(d: PackedByteArray) -> bool:
	format = "server"
	var name_raw := d.slice(8, 0x28)
	var z := name_raw.find(0)
	if z >= 0:
		name_raw = name_raw.slice(0, z)
	display_name = _gbk_guess(name_raw).get_slice("|", 0)
	w = (d[0x28] << 8) | d[0x29]
	h = (d[0x2A] << 8) | d[0x2B]
	var n := w * h
	if d.size() < 44 + n * 4:
		return false
	ground = _u16s(d, 44, n, true)
	objects = _u16s(d, 44 + n * 2, n, true)
	flags = _u16s(d, 44 + n * 4, n, true) if d.size() >= 44 + n * 6 else PackedInt32Array()
	return true

static func _u16s(d: PackedByteArray, off: int, n: int, big_endian: bool) -> PackedInt32Array:
	var out := PackedInt32Array()
	out.resize(n)
	if big_endian:
		for i in n:
			var o := off + i * 2
			out[i] = (d[o] << 8) | d[o + 1]
	else:
		for i in n:
			out[i] = d.decode_u16(off + i * 2)
	return out

## Godot 没有内置 GBK 解码；地图名只用于显示，非 ASCII 时先原样返回十六进制占位。
## （正式版会从服务端文件名 "<编号>_<地名>" 取名，文件名本身是 UTF-8。）
static func _gbk_guess(raw: PackedByteArray) -> String:
	var ascii := true
	for b in raw:
		if b >= 0x80:
			ascii = false
			break
	return raw.get_string_from_ascii() if ascii else ""

func idx(x: int, y: int) -> int:
	return y * w + x

func in_bounds(x: int, y: int) -> bool:
	return x >= 0 and y >= 0 and x < w and y < h
