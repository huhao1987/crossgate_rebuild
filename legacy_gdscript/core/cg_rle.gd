## 魔力宝贝图像 RLE 解码。每条指令的高 4 位决定类型，低 4 位是长度的最高位：
##   0x0n / 0x1n nn / 0x2n nn nn       原样复制 n 个字节
##   0x8n X / 0x9n X nn / 0xAn X nn nn  重复颜色 X
##   0xCn / 0xDn nn / 0xEn nn nn        透明（索引 0）
class_name CgRle
extends RefCounted

static func decode(src: PackedByteArray, expect: int) -> PackedByteArray:
	var out := PackedByteArray()
	out.resize(expect)
	var o := 0
	var i := 0
	var n := src.size()
	while i < n and o < expect:
		var b := src[i]
		var hi := b & 0xF0
		var lo := b & 0x0F
		i += 1
		var cnt := 0
		var val := -1  # -1 表示"复制"
		match hi:
			0x00:
				cnt = lo
			0x10:
				cnt = (lo << 8) | src[i]; i += 1
			0x20:
				cnt = (lo << 16) | (src[i] << 8) | src[i + 1]; i += 2
			0x80:
				val = src[i]; i += 1; cnt = lo
			0x90:
				val = src[i]; cnt = (lo << 8) | src[i + 1]; i += 2
			0xA0:
				val = src[i]; cnt = (lo << 16) | (src[i + 1] << 8) | src[i + 2]; i += 3
			0xC0:
				val = 0; cnt = lo
			0xD0:
				val = 0; cnt = (lo << 8) | src[i]; i += 1
			0xE0:
				val = 0; cnt = (lo << 16) | (src[i] << 8) | src[i + 1]; i += 2
			_:
				push_warning("CgRle: 未知指令 0x%02x" % b)
				break
		cnt = mini(cnt, expect - o)
		if val < 0:
			var take := mini(cnt, n - i)
			for k in take:
				out[o + k] = src[i + k]
			i += cnt
		elif val != 0:
			for k in cnt:
				out[o + k] = val
		# val == 0：out 已经是 0，直接跳过
		o += cnt
	return out
