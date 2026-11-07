## 动画库：AnimeInfo + Anime。
## AnimeInfo 每条 12 字节：uint32 id | uint32 offset | uint16 action_count | uint16 保留
## Anime 每个动作：头（BASE/EX 12 字节，V3/PUK/Joy 20 字节）+ 帧（每帧 10 字节）
##   头：uint16 dir | uint16 act | uint32 duration_ms | uint32 frame_count | [8 字节扩展]
##   帧：uint32 graphic_id | 6 字节标志（音效/事件等，暂未解析）
class_name CgAnime
extends RefCounted

class Action:
	var dir: int
	var act: int
	var duration_ms: int
	var frames: PackedInt32Array

var header_size := 12
var index := {}      # anime_id -> [offset, action_count]
var _path := ""
var _f: FileAccess
var _cache := {}     # anime_id -> { "dir,act": Action }

func load_pair(info_path: String, anime_path: String, p_header_size := 12) -> bool:
	header_size = p_header_size
	var fi := FileAccess.open(info_path, FileAccess.READ)
	if fi == null:
		push_warning("CgAnime: 打不开 %s" % info_path)
		return false
	var n := fi.get_length() / 12
	for i in n:
		var id := fi.get_32()
		var off := fi.get_32()
		var cnt := fi.get_16()
		fi.get_16()
		index[id] = [off, cnt]
	_path = anime_path
	_f = FileAccess.open(anime_path, FileAccess.READ)
	return _f != null

func has_anime(id: int) -> bool:
	return index.has(id)

## 返回 { "dir,act": Action }
func actions_for(id: int) -> Dictionary:
	if _cache.has(id):
		return _cache[id]
	var res := {}
	if not index.has(id) or _f == null:
		return res
	var off: int = index[id][0]
	var cnt: int = index[id][1]
	_f.seek(off)
	for k in cnt:
		var hdr := _f.get_buffer(header_size)
		var a := Action.new()
		a.dir = hdr.decode_u16(0)
		a.act = hdr.decode_u16(2)
		a.duration_ms = hdr.decode_u32(4)
		var fc := hdr.decode_u32(8)
		if fc > 4096:
			push_warning("CgAnime: 动画 %d 帧数异常 (%d)，可能 header_size 不对" % [id, fc])
			break
		var raw := _f.get_buffer(fc * 10)
		a.frames = PackedInt32Array()
		for j in fc:
			a.frames.append(raw.decode_u32(j * 10))
		res["%d,%d" % [a.dir, a.act]] = a
	_cache[id] = res
	return res
