#!/usr/bin/env python3
"""生成原创占位测试素材，文件格式与魔力宝贝客户端一致（GraphicInfo/Graphic/.cgp/AnimeInfo/Anime/MAP）。
美术全部为程序生成的原创色块与原创角色，用于在没有原版资源的环境里测试引擎代码。
用法: python3 make_test_assets.py <输出目录>
"""
import os, struct, sys, math

OUT = sys.argv[1] if len(sys.argv) > 1 else 'testdata'
os.makedirs(f'{OUT}/bin/pal', exist_ok=True); os.makedirs(f'{OUT}/map/0', exist_ok=True)

# ---------- 调色板：索引 16..239 共 224 色（BGR 存储） ----------
def hsv(h, s, v):
    i = int(h*6) % 6; f = h*6 - int(h*6); p, q, t = v*(1-s), v*(1-f*s), v*(1-(1-f)*s)
    return [(v,t,p),(q,v,p),(p,v,t),(p,q,v),(t,p,v),(v,p,q)][i]
pal = []  # 224 (r,g,b)
for k in range(224):
    hue, shade = divmod(k, 16)            # 14 色相 × 16 明度
    r, g, b = hsv(hue/14.0, 0.65, 0.25 + 0.75*shade/15)
    pal.append((int(r*255), int(g*255), int(b*255)))
with open(f'{OUT}/bin/pal/palet_00.cgp', 'wb') as f:
    for r, g, b in pal: f.write(bytes((b, g, r)))
def idx(hue, shade): return 16 + hue*16 + shade   # 调色板索引

# ---------- RLE 编码（与解码器对应的子集：字面量/重复/透明） ----------
def rle(px):
    out = bytearray(); i = 0; n = len(px)
    while i < n:
        j = i
        while j < n and px[j] == px[i] and j - i < 0xFFFFF: j += 1
        run = j - i
        if run >= 3 or px[i] == 0:
            v = px[i]
            if v == 0:
                if run < 16: out += bytes([0xC0 | run])
                elif run < 4096: out += bytes([0xD0 | (run >> 8), run & 0xFF])
                else: out += bytes([0xE0 | (run >> 16), (run >> 8) & 0xFF, run & 0xFF])
            else:
                if run < 16: out += bytes([0x80 | run, v])
                elif run < 4096: out += bytes([0x90 | (run >> 8), v, run & 0xFF])
                else: out += bytes([0xA0 | (run >> 16), v, (run >> 8) & 0xFF, run & 0xFF])
            i = j; continue
        j = i                               # 收集字面量直到出现长重复或透明
        while j < n and px[j] != 0 and not (j + 2 < n and px[j] == px[j+1] == px[j+2]) and j - i < 4095: j += 1
        lit = px[i:j]; L = len(lit)
        out += bytes([0x00 | L]) if L < 16 else bytes([0x10 | (L >> 8), L & 0xFF])
        out += bytes(lit); i = j
    return bytes(out)

graphics = []   # (w, h, ox, oy, east, south, flag, mapid, pixels)
def add(w, h, ox, oy, px, east=1, south=1, flag=0, mapid=0):
    graphics.append((w, h, ox, oy, east, south, flag, mapid, px)); return len(graphics) - 1

# ---------- 菱形地砖 64×47 ----------
TW, TH = 64, 47
def diamond(hue, base, noise_seed):
    px = bytearray(TW*TH)
    for y in range(TH):
        for x in range(TW):
            dx = abs(x - 31.5) / 32.0; dy = abs(y - 23) / 23.5
            if dx + dy <= 1.0:
                n = ((x*7 + y*13 + noise_seed*31) * 2654435761 >> 13) & 3
                edge = 2 if dx + dy > 0.93 else 0
                px[y*TW + x] = idx(hue, max(0, min(15, base + n - edge)))
    return bytes(px)
GRASS = add(TW, TH, -32, -24, diamond(4, 8, 1), flag=1, mapid=100)
GRASS2 = add(TW, TH, -32, -24, diamond(4, 10, 2), flag=1, mapid=101)
STONE = add(TW, TH, -32, -24, diamond(13, 9, 3), flag=1, mapid=102)
WATER = add(TW, TH, -32, -24, diamond(8, 7, 4), flag=0, mapid=103)

# ---------- 物件：原创"晶体柱" 与 "灌木"（1×1 占位，不可通行） ----------
def crystal():
    w, h = 40, 90; px = bytearray(w*h)
    for y in range(h):
        half = int(14 * min(1, y/20) * (1 - max(0, y-70)/40))
        for x in range(w):
            d = abs(x - 20)
            if d <= half and y < h - 4:
                px[y*w+x] = idx(10, 15 - (d*10)//max(half,1) if y > 10 else 15)
    return w, h, bytes(px)
w, h, px = crystal(); CRYSTAL = add(w, h, -20, -(h - 12), px, flag=0, mapid=200)
def bush():
    w, h = 56, 44; px = bytearray(w*h)
    for y in range(h):
        for x in range(w):
            for cx, cy, r in ((16, 28, 14), (40, 28, 14), (28, 16, 15)):
                if (x-cx)**2 + (y-cy)**2 <= r*r:
                    px[y*w+x] = idx(3, 5 + ((x+y) % 5) + (4 if y < cy else 0)); break
    return w, h, bytes(px)
w, h, px = bush(); BUSH = add(w, h, -28, -(h - 10), px, flag=0, mapid=201)

# ---------- 原创角色：圆滚滚的"豆丁"，8 方向 × (站立1帧 + 行走4帧) ----------
DIRS = [(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1)]   # 屏幕朝向的单位向量
def bean(dirv, phase):
    w, h = 40, 56; px = bytearray(w*h)
    bob = [0, -2, 0, -2][phase] if phase >= 0 else 0
    cx, cy, r = 20, 32 + bob, 15
    for y in range(h):
        for x in range(w):
            if (x-cx)**2 + ((y-cy)*1.15)**2 <= r*r:
                shade = 12 - int(((x-cx)+(y-cy)) / 6)
                px[y*w+x] = idx(0, max(3, min(15, shade)))          # 暖色身体
    # 头顶小叶子
    for y in range(cy - r - 7, cy - r + 1):
        for x in range(cx - 1, cx + 5):
            if 0 <= y < h and abs((x - cx - 2) - (cy - r - y) * 0.4) < 2: px[y*w+x] = idx(4, 12)
    # 眼睛：朝向方向偏移（朝背面时不画）
    ex, ey = dirv
    if ey >= 0 or ex != 0:
        for s in (-1, 1):
            if ey < 0 and s * ex < 0: continue
            exx = int(cx + ex*6 + s*5*(1 - abs(ex)*0.5)); eyy = int(cy - 3 + ey*3)
            for yy in range(eyy-2, eyy+2):
                for xx in range(exx-1, exx+2):
                    if 0 <= xx < w and 0 <= yy < h: px[yy*w+xx] = idx(13, 1)
    # 小脚（行走时左右交替）
    for s in (-1, 1):
        lift = 2 if phase >= 0 and ((phase % 2 == 0) == (s < 0)) else 0
        for yy in range(cy + r - 2 - lift, cy + r + 2 - lift):
            for xx in range(cx + s*7 - 3, cx + s*7 + 3):
                if 0 <= yy < h: px[yy*w+xx] = idx(0, 4)
    return w, h, bytes(px)
def shifted(px, w, h, dx, dy, recolor=None):
    out = bytearray(w*h)
    for y in range(h):
        for x in range(w):
            c = px[y*w + x]
            if not c: continue
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h:
                out[ny*w + nx] = recolor(c) if recolor else c
    return bytes(out)
def squash(px, w, h):
    out = bytearray(w*h)
    for y in range(h):
        for x in range(w):
            sy = h - 1 - (h - 1 - y) * 3      # 纵向压到 1/3，贴地
            if 0 <= sy < h and px[sy*w + x]: out[y*w + x] = px[sy*w + x]
    return bytes(out)
char_frames = {}   # (dir, act) -> [gid...]
for d, v in enumerate(DIRS):
    w, h, px = bean(v, -1); char_frames[(d, 0)] = [add(w, h, -20, -(h - 6), px)]
    char_frames[(d, 1)] = []
    for ph in range(4):
        w2, h2, p2 = bean(v, ph); char_frames[(d, 1)].append(add(w2, h2, -20, -(h2 - 6), p2))
    lx, ly = int(v[0] * 6), int(v[1] * 4)
    char_frames[(d, 2)] = [add(w, h, -20, -(h - 6), shifted(px, w, h, lx // 2, ly // 2)), add(w, h, -20, -(h - 6), shifted(px, w, h, lx, ly)), add(w, h, -20, -(h - 6), px)]
    char_frames[(d, 3)] = [add(w, h, -20, -(h - 6), shifted(px, w, h, -lx // 2, -ly // 2, lambda c: idx(1, 14) if c != idx(13, 1) else c)), add(w, h, -20, -(h - 6), px)]
    char_frames[(d, 4)] = [add(w, h, -20, -(h - 6), squash(px, w, h))]

# ---------- 写 GraphicInfo / Graphic ----------
with open(f'{OUT}/bin/Graphic_test.bin', 'wb') as gf, open(f'{OUT}/bin/GraphicInfo_test.bin', 'wb') as gi:
    for gid, (w, h, ox, oy, east, south, flag, mapid, px) in enumerate(graphics):
        flipped = b''.join(px[(h - 1 - r) * w:(h - r) * w] for r in range(h))   # 图档按从下往上的行顺序存储（与原版一致）
        body = rle(flipped); blk = b'RD' + bytes([1, 0]) + struct.pack('<iii', w, h, 16 + len(body)) + body
        off = gf.tell(); gf.write(blk)
        gi.write(struct.pack('<iIIiiiiBBB5xi', gid, off, len(blk), ox, oy, w, h, east, south, flag, mapid))

# ---------- 写 AnimeInfo / Anime（BASE 格式：12 字节动作头 + 10 字节帧） ----------
with open(f'{OUT}/bin/Anime_test.bin', 'wb') as af, open(f'{OUT}/bin/AnimeInfo_test.bin', 'wb') as ai:
    off = af.tell(); acts = sorted(char_frames)
    for (d, act) in acts:
        frames = char_frames[(d, act)]; ms = {1: 600, 2: 450, 3: 400}.get(act, 1000)
        af.write(struct.pack('<HHII', d, act, ms, len(frames)))
        for g in frames: af.write(struct.pack('<I', g) + b'\0'*6)
    ai.write(struct.pack('<IIHH', 900000, off, len(acts), 0))

# ---------- 测试地图 40×40 ----------
MW, MH = 40, 40
ground = [0]*(MW*MH); objs = [0]*(MW*MH)
for y in range(MH):
    for x in range(MW):
        g = 100 if (x*3 + y*5) % 7 else 101
        if 14 <= x <= 25 and 18 <= y <= 21: g = 102                        # 石板路
        if (x-30)**2 + (y-10)**2 < 18: g = 103                              # 池塘
        ground[y*MW + x] = g
        h = (x*73856093 ^ y*19349663) & 0xFF
        if g in (100, 101) and h < 10: objs[y*MW + x] = 200 if h < 4 else 201
        if x in (0, MW-1) or y in (0, MH-1): objs[y*MW + x] = 201              # 灌木围边
with open(f'{OUT}/map/0/1.dat', 'wb') as f:
    f.write(b'MAP\x01' + b'\0'*8 + struct.pack('<ii', MW, MH))
    f.write(struct.pack('<%dH' % (MW*MH), *ground)); f.write(struct.pack('<%dH' % (MW*MH), *objs))
    f.write(struct.pack('<%dH' % (MW*MH), *([0]*(MW*MH))))
print(f'graphics={len(graphics)} anime=1 (8 dirs x 5 acts) map=40x40 -> {OUT}')

# ---------- 同一张测试地图的服务端格式（LS2MAP，大端） ----------
os.makedirs(f'{OUT}/server_map', exist_ok=True)
name = b'TestField|0'.ljust(32, b'\0')
with open(f'{OUT}/server_map/1_TestField', 'wb') as f:
    f.write(b'LS2MAP\0\0' + name + struct.pack('>HH', MW, MH))
    f.write(struct.pack('>%dH' % (MW*MH), *ground)); f.write(struct.pack('>%dH' % (MW*MH), *objs))
print('server map written')

# ---------- 服务端数据（原创占位）：两张图 + 传送 + 遇敌 + 敌人 + NPC ----------
SD = f'{OUT}/_server/data'
os.makedirs(f'{SD}/map', exist_ok=True)
def server_map(path, w, h, g, o, name):
    with open(path, 'wb') as f:
        f.write(b'LS2MAP\0\0' + name.encode('ascii').ljust(32, b'\0') + struct.pack('>HH', w, h))
        f.write(struct.pack('>%dH' % (w*h), *g)); f.write(struct.pack('>%dH' % (w*h), *o))
server_map(f'{SD}/map/1_TestField', MW, MH, ground, objs, 'TestField|0')
RW, RH = 12, 12
rg = [102 if 0 < x < RW-1 and 0 < y < RH-1 else 103 for y in range(RH) for x in range(RW)]
ro = [0]*(RW*RH); ro[1*RW + 2] = 200; ro[1*RW + 9] = 200
server_map(f'{SD}/map/2_TestRoom', RW, RH, rg, ro, 'TestRoom|0')
T = lambda n, d: '\t'.join(d.get(i, '') for i in range(n))
with open(f'{SD}/warp.txt', 'w') as f:
    f.write('# id\tfrom_map\tfrom_x\tfrom_y\tto_map\tto_x\tto_y\n')
    f.write('1\t1\t20\t19\t2\t6\t9\n')        # 石板路北端 → 小屋
    f.write('2\t2\t6\t10\t1\t21\t21\n')       # 小屋出口 → 回到石板路
with open(f'{SD}/encount.txt', 'w') as f:      # 整张野外图（石板路除外的区域也覆盖，简单起见全覆盖）
    f.write(T(34, {0: '10', 3: '1', 4: '1', 5: '1', 6: '38', 7: '38', 9: '2', 10: '3', 11: '10', 14: '20', 24: '1'}) + '\n')
with open(f'{SD}/group.txt', 'w') as f:
    f.write(T(35, {0: 'Beans', 2: '20', 3: '-1', 4: '-1', 5: '30', 6: '31', 15: '1', 16: '1', 25: '0', 26: '0'}) + '\n')
with open(f'{SD}/enemy.txt', 'w') as f:
    f.write(T(48, {0: 'BeanA', 1: 'at:8|gu:1|es:1|wa:0', 2: '30', 3: '40', 4: '1', 5: '3', 13: '7001', 23: '60'}) + '\n')   # 60% 掉落 7001
    f.write(T(48, {0: 'BeanB', 1: 'at:6|gu:2|es:2|wa:0', 2: '31', 3: '40', 4: '2', 5: '4'}) + '\n')
with open(f'{SD}/enemybase.txt', 'w') as f:
    f.write(T(46, {0: 'BeanBase', 1: '40', 15: '25', 16: '25', 17: '25', 18: '25', 30: '900000'}) + '\n')
with open(f'{SD}/npc.txt', 'w') as f:
    f.write(T(25, {0: 'Guide', 3: '1', 8: '1', 9: '18', 10: '20', 19: '2', 20: '900000', 24: 'guide.txt'}) + '\n')
    f.write(T(25, {0: 'Sign', 3: '2', 8: '1', 9: '22', 10: '20', 19: '0', 20: '200'}) + '\n')   # 静态图像外观（全局图像编号 = 晶体的地图编号 200）
# 道具：图标借用占位素材里的灌木/晶体图像
with open(f'{SD}/itemset.txt', 'w') as f:
    f.write(T(102, {0: 'Leaf', 11: '7001', 12: '201', 14: '26'}) + '\n')
    f.write(T(102, {0: 'Crystal', 11: '7002', 12: '200', 14: '21'}) + '\n')
for n in ('tech.txt', 'skill.txt'): open(f'{SD}/{n}', 'w').close()
# 经验表（自编数值）
with open(f'{SD}/explist.txt', 'w') as f: f.write('\n'.join(str(v) for v in [0, 0, 10, 30, 70, 150, 300, 600]) + '\n')
with open(f'{SD}/enemyexplist.txt', 'w') as f: f.write('\n'.join(str(v) for v in [0, 6, 8, 10, 12, 14]) + '\n')
# NPC 脚本（自编内容）：第一次对话给一个水晶并设置事件标记；之后换一句话
os.makedirs(f'{SD}/npc', exist_ok=True)
with open(f'{SD}/npc/guide.txt', 'w') as f:
    f.write('block EndEvent 1 == 1\nendwindow "Safe travels!"\n\n'
            'block\nwindow "Hello, traveler.",next\nwindow "Take this crystal?",yes,no\nwindow_no\nwindow_yes\ngiveitem 7002\nendevent 1 = 1\n')
print('server data written')
