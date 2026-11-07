"""魔力宝贝资源解析（Python 参考实现，与 GDScript 版逻辑一致，用于交叉验证）"""
import struct
INFO_FMT = '<iIIiiiiBBB5xi'; INFO_SIZE = struct.calcsize(INFO_FMT)
def read_info(path):
    d = open(path, 'rb').read(); out = []
    for i in range(len(d)//INFO_SIZE):
        gid, off, ln, ox, oy, w, h, east, south, flag, mapid = struct.unpack_from(INFO_FMT, d, i*INFO_SIZE)
        out.append(dict(id=gid, off=off, len=ln, ox=ox, oy=oy, w=w, h=h, east=east, south=south, flag=flag, map=mapid))
    return out
def rle_decode(src, expect):
    out = bytearray(); i = 0; n = len(src)
    while i < n and len(out) < expect:
        b = src[i]; hi = b & 0xF0; lo = b & 0x0F; i += 1
        if hi == 0x00: c = lo; out += src[i:i+c]; i += c
        elif hi == 0x10: c = (lo<<8)|src[i]; i += 1; out += src[i:i+c]; i += c
        elif hi == 0x20: c = (lo<<16)|(src[i]<<8)|src[i+1]; i += 2; out += src[i:i+c]; i += c
        elif hi == 0x80: v = src[i]; i += 1; out += bytes([v])*lo
        elif hi == 0x90: v = src[i]; c = (lo<<8)|src[i+1]; i += 2; out += bytes([v])*c
        elif hi == 0xA0: v = src[i]; c = (lo<<16)|(src[i+1]<<8)|src[i+2]; i += 3; out += bytes([v])*c
        elif hi == 0xC0: out += b'\0'*lo
        elif hi == 0xD0: c = (lo<<8)|src[i]; i += 1; out += b'\0'*c
        elif hi == 0xE0: c = (lo<<16)|(src[i]<<8)|src[i+1]; i += 2; out += b'\0'*c
        else: raise ValueError('bad opcode %02x' % b)
    return bytes(out)
def read_graphic(f, info):
    f.seek(info['off']); blk = f.read(info['len'])
    if blk[:2] != b'RD': raise ValueError('no RD magic')
    ver = blk[2]; w, h, ln = struct.unpack_from('<iii', blk, 4); hdr = 16; pal_len = 0
    if ver >= 2: pal_len = struct.unpack_from('<i', blk, 16)[0]; hdr = 20
    body = blk[hdr:ln]; pix = rle_decode(body, w*h + pal_len) if ver & 1 else body
    return dict(ver=ver, w=w, h=h, pixels=pix[:w*h], pal=pix[w*h:w*h+pal_len] if pal_len else None)
