using System.Buffers.Binary;
using System.Text;

namespace CgPort.Core.Assets;

/// <summary>
/// 读官方客户端 exe 里的资源（.rsrc 节）。这个私服客户端的新界面图大多放在 exe 里：
/// RCDATA(10) 是 PNG（208 自動尋路列表、213 設置地圖座標點、231 大地图底、234 标题条、245 指南针、103 聊天表情…），
/// BITMAP(2) 是 BMP（黑色 = 透明；209/210 地图窗口蓝球按钮、211/212 設定、233/235 红/蓝定位点、246 寻路小人…），
/// 自定义类型 "TEXT" 238 是小地图标注表（GBK 文本）。
/// </summary>
public sealed class PeResources
{
    private readonly Dictionary<(string Type, int Id), byte[]> _res = new();
    public IEnumerable<(string Type, int Id)> Keys => _res.Keys;

    public byte[]? Get(string type, int id) => _res.TryGetValue((type, id), out var b) ? b : null;
    /// <summary>BMP 资源加上 14 字节文件头，变成能直接解码的 .bmp。</summary>
    public byte[]? Bmp(int id)
    {
        if (Get("2", id) is not { } b || b.Length < 40) return null;
        int hdr = BinaryPrimitives.ReadInt32LittleEndian(b), bpp = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(14));
        int clr = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(32));
        if (clr == 0 && bpp <= 8) clr = 1 << bpp;
        int off = 14 + hdr + clr * 4;
        var f = new byte[14 + b.Length];
        f[0] = (byte)'B'; f[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(2), f.Length);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(10), off);
        b.CopyTo(f, 14);
        return f;
    }

    /// <summary>
    /// 解 BITMAP 资源成 RGBA（8 位无压缩 / RLE8 / 24 位，资源里只有这三种）。黑色 (0,0,0) 当透明（官方实测）。
    /// Godot 自带的 BMP 读取不支持 RLE（211/212「設定」就是 RLE8）。
    /// </summary>
    public (int W, int H, byte[] Rgba)? DecodeBmp(int id)
    {
        if (Get("2", id) is not { } b || b.Length < 40) return null;
        int hs = BinaryPrimitives.ReadInt32LittleEndian(b), w = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4)), h0 = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8));
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(14)), comp = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16));
        int clr = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(32)); if (clr == 0 && bpp <= 8) clr = 1 << bpp;
        bool topDown = h0 < 0; int h = Math.Abs(h0);
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
        int pal = hs, pix = hs + clr * 4;
        var idx = new int[w * h];   // 每像素：调色板号，或 24 位时直接 0xRRGGBB | 0x1000000
        if (bpp == 8 && comp == 0)
        {
            int stride = (w + 3) & ~3;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int o = pix + y * stride + x; if (o < b.Length) idx[y * w + x] = b[o]; }
        }
        else if (bpp == 8 && comp == 1)
        {
            int x = 0, y = 0, o = pix;
            while (o + 1 < b.Length && y < h)
            {
                int n = b[o++], v = b[o++];
                if (n > 0) { for (int k = 0; k < n && x < w; k++) idx[y * w + x++] = v; continue; }
                if (v == 0) { x = 0; y++; }
                else if (v == 1) break;
                else if (v == 2) { if (o + 1 >= b.Length) break; x += b[o++]; y += b[o++]; }
                else { for (int k = 0; k < v && o < b.Length; k++) { if (x < w && y < h) idx[y * w + x] = b[o]; x++; o++; } if ((v & 1) != 0) o++; }
            }
        }
        else if (bpp == 24 && comp == 0)
        {
            int stride = (w * 3 + 3) & ~3;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int o = pix + y * stride + x * 3; if (o + 2 < b.Length) idx[y * w + x] = 0x1000000 | b[o + 2] << 16 | b[o + 1] << 8 | b[o]; }
        }
        else return null;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int v = idx[y * w + x], r, g, bl;
                if ((v & 0x1000000) != 0) { r = (v >> 16) & 255; g = (v >> 8) & 255; bl = v & 255; }
                else { int po = pal + v * 4; if (po + 2 >= b.Length) continue; bl = b[po]; g = b[po + 1]; r = b[po + 2]; }
                int dy = topDown ? y : h - 1 - y, d = (dy * w + x) * 4;
                rgba[d] = (byte)r; rgba[d + 1] = (byte)g; rgba[d + 2] = (byte)bl; rgba[d + 3] = (byte)(r == 0 && g == 0 && bl == 0 ? 0 : 255);
            }
        return (w, h, rgba);
    }

    public static PeResources? Load(string path)
    {
        try { return Parse(GameFiles.ReadAllBytes(path)); } catch { return null; }
    }

    public static PeResources? Parse(byte[] d)
    {
        if (d.Length < 0x40 || d[0] != 'M' || d[1] != 'Z') return null;
        int pe = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(0x3C));
        if (pe <= 0 || pe + 24 > d.Length || d[pe] != 'P' || d[pe + 1] != 'E') return null;
        int nSec = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pe + 6));
        int optSize = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pe + 20));
        int opt = pe + 24;
        bool pe32p = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(opt)) == 0x20b;
        int dirs = opt + (pe32p ? 112 : 96);
        uint rsrcRva = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(dirs + 2 * 8));
        if (rsrcRva == 0) return null;
        var secs = new List<(uint Va, uint Size, uint Raw)>();
        int sh = opt + optSize;
        for (int i = 0; i < nSec; i++, sh += 40)
            secs.Add((BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(sh + 12)), Math.Max(BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(sh + 8)), BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(sh + 16))), BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(sh + 20))));
        int Off(uint rva) { foreach (var s in secs) if (rva >= s.Va && rva < s.Va + s.Size) return (int)(rva - s.Va + s.Raw); return -1; }
        int root = Off(rsrcRva);
        if (root < 0) return null;
        var r = new PeResources();

        string Name(uint v)
        {
            if ((v & 0x80000000) == 0) return v.ToString();
            int p = root + (int)(v & 0x7fffffff);
            int n = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p));
            return Encoding.Unicode.GetString(d, p + 2, n * 2);
        }
        IEnumerable<(uint Name, uint Target)> Entries(int dir)
        {
            int n = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(dir + 12)) + BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(dir + 14));
            for (int i = 0; i < n; i++)
                yield return (BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(dir + 16 + i * 8)), BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(dir + 20 + i * 8)));
        }
        foreach (var (tn, tt) in Entries(root))
        {
            if ((tt & 0x80000000) == 0) continue;
            var type = Name(tn);
            foreach (var (idn, it) in Entries(root + (int)(tt & 0x7fffffff)))
            {
                if ((idn & 0x80000000) != 0 || (it & 0x80000000) == 0) continue;
                foreach (var (_, lt) in Entries(root + (int)(it & 0x7fffffff)))
                {
                    if ((lt & 0x80000000) != 0) continue;
                    int de = root + (int)lt;
                    uint rva = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(de)), size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(de + 4));
                    int o = Off(rva);
                    if (o < 0 || o + size > d.Length) continue;
                    r._res[(type, (int)idn)] = d.AsSpan(o, (int)size).ToArray();
                    break;
                }
            }
        }
        return r;
    }
}
