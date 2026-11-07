## 等距坐标换算。魔力宝贝地块 64×47：x 向东（屏幕右上），y 向南（屏幕右下）。
## 若实测方向不对，只需改这里两个函数。
class_name Iso
extends RefCounted

const HALF_W := 32.0
const HALF_H := 24.0

static func tile_to_screen(t: Vector2) -> Vector2:
	return Vector2((t.x + t.y) * HALF_W, (t.y - t.x) * HALF_H)

static func screen_to_tile(p: Vector2) -> Vector2:
	var a := p.x / HALF_W   # = x + y
	var b := p.y / HALF_H   # = y - x
	return Vector2((a - b) * 0.5, (a + b) * 0.5)

static func screen_to_cell(p: Vector2) -> Vector2i:
	var t := screen_to_tile(p)
	return Vector2i(roundi(t.x), roundi(t.y))
