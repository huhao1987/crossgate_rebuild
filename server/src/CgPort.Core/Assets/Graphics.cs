using System.Buffers.Binary;

namespace CgPort.Core.Assets;

/// <summary>魔力宝贝图像 RLE 解码（高 4 位决定指令类型，低 4 位是长度最高位）。</summary>
public static class Rle
{
    public static byte[] Decode(ReadOnlySpan<byte> src, int expect)
    {
        var o = new byte[expect];
        int op = 0, i = 0, n = src.Length;
        while (i < n && op < expect)
        {
            int b = src[i++], hi = b & 0xF0, lo = b & 0x0F, cnt, val = -1;
            switch (hi)
            {
                case 0x00: cnt = lo; break;
                case 0x10: cnt = (lo << 8) | src[i]; i += 1; break;
                case 0x20: cnt = (lo << 16) | (src[i] << 8) | src[i + 1]; i += 2; break;
                case 0x80: val = src[i]; i += 1; cnt = lo; break;
                case 0x90: val = src[i]; cnt = (lo << 8) | src[i + 1]; i += 2; break;
                case 0xA0: val = src[i]; cnt = (lo << 16) | (src[i + 1] << 8) | src[i + 2]; i += 3; break;
                case 0xC0: val = 0; cnt = lo; break;
                case 0xD0: val = 0; cnt = (lo << 8) | src[i]; i += 1; break;
                case 0xE0: val = 0; cnt = (lo << 16) | (src[i] << 8) | src[i + 1]; i += 2; break;
                default: return o;   // 未知指令：返回已解出的部分
            }
            cnt = Math.Min(cnt, expect - op);
            if (val < 0) { int take = Math.Min(cnt, n - i); src.Slice(i, take).CopyTo(o.AsSpan(op)); i += cnt; }
            else if (val != 0) o.AsSpan(op, cnt).Fill((byte)val);
            op += cnt;
        }
        return o;
    }
}

/// <summary>256 色调色板（RGBA8888 打包为 uint，A 在高位）。0–15、240–255 为系统色，16–239 来自 .cgp。索引 0 透明。</summary>
public sealed class Palette
{
    public readonly uint[] Colors = new uint[256];

    // 系统色（RGB）。0–9 是 Windows 默认系统色；其余取自开源 CGTool 的表（按它"低字节=红"的写法换算），
    // 并用官方客户端截图校正：4/5 号与 CGTool 相反（实测 5 号是品红），241 号实测为 (225,160,95)，247 号实测为 (160,160,164)。0 号透明。
    private static readonly int[] SysLow = { 0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xC0C0C0, 0xC0DCC0, 0xA6CAF0, 0xDE0000, 0xFF5F00, 0xFFFFA0, 0x005FD2, 0x50D2FF, 0x28E128 };
    private static readonly int[] SysHigh = { 0xF5C396, 0xE1A05F, 0xC37D46, 0x9B551E, 0x464137, 0x28231E, 0xFFFBF0, 0xA0A0A4, 0x808080, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF80FF, 0x00FFFF, 0xFFFFFF };

    public Palette()
    {
        for (int i = 0; i < 16; i++) { Colors[i] = Rgb(SysLow[i]); Colors[240 + i] = Rgb(SysHigh[i]); }
        for (int i = 16; i < 240; i++) Colors[i] = 0xFFFF00FF;   // 未加载时显示品红
    }

    private static uint Rgb(int v) => 0xFF000000u | (uint)v;
    private static uint Bgr(byte b, byte g, byte r) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    public static Palette FromCgp(string path)
    {
        var p = new Palette();
        var d = GameFiles.ReadAllBytes(path);
        for (int i = 0; i < Math.Min(224, d.Length / 3); i++) p.Colors[16 + i] = Bgr(d[i * 3], d[i * 3 + 1], d[i * 3 + 2]);
        return p;
    }

    /// <summary>图像自带调色板（BGR 三元组）覆盖后的副本。</summary>
    public Palette WithEmbedded(ReadOnlySpan<byte> bgr)
    {
        var p = new Palette();
        Array.Copy(Colors, p.Colors, 256);
        for (int i = 0; i < Math.Min(256, bgr.Length / 3); i++) p.Colors[i] = Bgr(bgr[i * 3], bgr[i * 3 + 1], bgr[i * 3 + 2]);
        return p;
    }
}

/// <summary>GraphicInfo 的一条记录（含在 Graphic 文件中的位置）。</summary>
public sealed record GraphicEntry(int Id, uint Offset, uint Length, int OffsetX, int OffsetY, int Width, int Height, byte East, byte South, byte Flag, int MapId);

/// <summary>解码后的图像：调色板索引 + 可选的内嵌调色板。</summary>
public sealed record IndexedImage(int Width, int Height, byte[] Pixels, byte[]? EmbeddedPalette)
{
    /// <summary>转成 RGBA8（每像素 4 字节，R,G,B,A），索引 0 透明。</summary>
    public byte[] ToRgba(Palette basePalette)
    {
        var pal = EmbeddedPalette is { Length: > 0 } ? basePalette.WithEmbedded(EmbeddedPalette) : basePalette;
        // 图档按"从下往上"的行顺序存储（与官方客户端截图对照确认），输出时翻成从上往下
        var o = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            int src = (Height - 1 - y) * Width, dst = y * Width;
            for (int x = 0; x < Width; x++)
            {
                byte c = Pixels[src + x];
                if (c == 0) continue;
                uint v = pal.Colors[c];
                int k = (dst + x) * 4;
                o[k] = (byte)(v >> 16); o[k + 1] = (byte)(v >> 8); o[k + 2] = (byte)v; o[k + 3] = 255;
            }
        }
        return o;
    }
}

/// <summary>一组 GraphicInfo + Graphic 文件。线程不安全（共用一个文件句柄），由调用方串行访问。</summary>
public sealed class GraphicArchive : IDisposable
{
    public string Name { get; }
    public IReadOnlyList<GraphicEntry> Entries => _entries;
    private readonly List<GraphicEntry> _entries = new();
    private readonly Dictionary<int, GraphicEntry> _local = new();
    private readonly Stream _gfx;

    /// <summary>本组内的图像编号（每组 GraphicInfo 的编号都从 0 开始，组与组之间不通用；动画帧引用的是配对图像组的编号）。</summary>
    public bool TryLocal(int id, out GraphicEntry e) => _local.TryGetValue(id, out e!);

    public GraphicArchive(string infoPath, string gfxPath, string name = "")
    {
        Name = name;
        var d = GameFiles.ReadAllBytes(infoPath);
        for (int i = 0; i + 40 <= d.Length; i += 40)
        {
            var e = d.AsSpan(i, 40);
            _entries.Add(new GraphicEntry(
                BinaryPrimitives.ReadInt32LittleEndian(e), BinaryPrimitives.ReadUInt32LittleEndian(e[4..]), BinaryPrimitives.ReadUInt32LittleEndian(e[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(e[12..]), BinaryPrimitives.ReadInt32LittleEndian(e[16..]),
                BinaryPrimitives.ReadInt32LittleEndian(e[20..]), BinaryPrimitives.ReadInt32LittleEndian(e[24..]),
                e[28], e[29], e[30], BinaryPrimitives.ReadInt32LittleEndian(e[36..])));
            _local[_entries[^1].Id] = _entries[^1];
        }
        _gfx = GameFiles.OpenRead(gfxPath);
    }

    public IndexedImage? Decode(GraphicEntry e)
    {
        if (e.Length < 16) return null;
        var blk = new byte[e.Length];
        _gfx.Seek(e.Offset, SeekOrigin.Begin);
        int got = 0;
        while (got < blk.Length) { int r = _gfx.Read(blk, got, blk.Length - got); if (r <= 0) break; got += r; }
        if (blk[0] != 'R' || blk[1] != 'D') return null;
        int ver = blk[2], w = BinaryPrimitives.ReadInt32LittleEndian(blk.AsSpan(4)), h = BinaryPrimitives.ReadInt32LittleEndian(blk.AsSpan(8));
        int len = BinaryPrimitives.ReadInt32LittleEndian(blk.AsSpan(12)), hdr = 16, palLen = 0;
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
        if (ver >= 2) { palLen = BinaryPrimitives.ReadInt32LittleEndian(blk.AsSpan(16)); hdr = 20; }
        var body = blk.AsSpan(hdr, Math.Max(0, Math.Min(len, blk.Length) - hdr));
        byte[] pix = (ver & 1) == 1 ? Rle.Decode(body, w * h + Math.Max(0, palLen)) : body.ToArray();
        if (pix.Length < w * h) Array.Resize(ref pix, w * h);
        byte[]? pal = palLen > 0 && pix.Length >= w * h + palLen ? pix.AsSpan(w * h, palLen).ToArray() : null;
        return new IndexedImage(w, h, pix.AsSpan(0, w * h).ToArray(), pal);
    }

    public void Dispose() => _gfx.Dispose();
}

/// <summary>
/// 多组图像库。全局"图像编号"是 GraphicInfo 第 36 字节起的那个字段（地图地块、NPC 静态外观、道具图标都用它），
/// 后加载的组覆盖先加载的。GraphicInfo 自己的编号字段每组都从 0 开始，只在组内有效（<see cref="GraphicArchive.TryLocal"/>）。
/// </summary>
public sealed class GraphicLibrary : IDisposable
{
    private readonly List<GraphicArchive> _sets = new();
    private readonly Dictionary<int, (GraphicArchive, GraphicEntry)> _byId = new(), _byMapId = new();

    public void Add(GraphicArchive a)
    {
        _sets.Add(a);
        foreach (var e in a.Entries)
        {
            _byId[e.Id] = (a, e);
            if (e.MapId != 0) _byMapId[e.MapId] = (a, e);
        }
    }

    /// <summary>按组内编号查（跨组会冲突，只适合只加载了一组的场合，例如测试素材）。</summary>
    public bool TryById(int id, out GraphicArchive set, out GraphicEntry entry) => Try(_byId, id, out set, out entry);
    public bool TryByMapId(int mapId, out GraphicArchive set, out GraphicEntry entry) => Try(_byMapId, mapId, out set, out entry);

    private static bool Try(Dictionary<int, (GraphicArchive, GraphicEntry)> d, int k, out GraphicArchive s, out GraphicEntry e)
    {
        if (d.TryGetValue(k, out var v)) { (s, e) = v; return true; }
        s = null!; e = null!; return false;
    }

    public void Dispose() { foreach (var s in _sets) s.Dispose(); }
}

/// <summary>动画库。动作头 BASE/EX 为 12 字节、V3/PUK/Joy 为 20 字节；每帧 10 字节（图像编号 + 6 字节标志）。</summary>
public sealed class AnimeArchive : IDisposable
{
    /// <summary>Frames 是配对图像组（<see cref="Graphics"/>）里的组内编号。</summary>
    /// <summary>
    /// Sounds：每帧最后 2 字节（官方 Anime_4 实测）：0 无；1–9999 = 这一帧播放的音效号；10000 起 = 命中帧（伤害在这一帧出现），
    /// 再减 10000 不为 0 就是命中时的音效号（例 10111 = 命中 + 音效 111 cgply05a）。
    /// </summary>
    public sealed record Action(int Dir, int Act, int DurationMs, int[] Frames, GraphicArchive? Set)
    {
        public ushort[] Sounds { get; init; } = Array.Empty<ushort>();
        /// <summary>高版本动作：帧用这张图（地图编号）带的调色板；0 = 帧自己的 / 当前调色板。</summary>
        public int PaletteImage { get; init; }
        public const int HitFlag = 10000;
    }
    /// <summary>与这组动画配对的图像组（同一套文件，例如 AnimeEx 配 GraphicEx）。</summary>
    public GraphicArchive? Graphics { get; set; }
    private readonly Dictionary<int, (uint Off, int Count)> _index = new();
    private readonly Dictionary<int, Dictionary<(int, int), Action>> _cache = new();
    private readonly Stream _f;
    private readonly int _hdr;

    public AnimeArchive(string infoPath, string animePath, int headerSize)
    {
        _hdr = headerSize;
        var d = GameFiles.ReadAllBytes(infoPath);
        for (int i = 0; i + 12 <= d.Length; i += 12)
            _index[BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(i))] = (BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(i + 4)), BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(i + 8)));
        _f = GameFiles.OpenRead(animePath);
    }

    public bool Has(int id) => _index.ContainsKey(id);

    public IReadOnlyDictionary<(int Dir, int Act), Action> Actions(int id)
    {
        if (_cache.TryGetValue(id, out var c)) return c;
        c = new();
        if (_index.TryGetValue(id, out var ix))
        {
            _f.Seek(ix.Off, SeekOrigin.Begin);
            var hdr = new byte[20];
            for (int k = 0; k < ix.Count; k++)
            {
                // 高版本动作（PUK3 起）：头 20 字节，第 16–19 字节 = FF FF FF FF；12–13 = 调色板、14–15 = 翻转等旗标。
                // 这种动作的帧没有自己的调色板，要用"地图编号 = 动画编号"那张图里带的调色板（CGTool Anime.cs 同样做法）
                long at = _f.Position;
                int got = _f.Read(hdr, 0, 20);
                bool high = got == 20 && hdr[16] == 0xFF && hdr[17] == 0xFF && hdr[18] == 0xFF && hdr[19] == 0xFF;
                int hl = high ? 20 : _hdr;
                if (got < hl) break;
                _f.Seek(at + hl, SeekOrigin.Begin);
                int dir = BinaryPrimitives.ReadUInt16LittleEndian(hdr), act = BinaryPrimitives.ReadUInt16LittleEndian(hdr.AsSpan(2));
                int ms = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(4)), fc = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(8));
                if (fc < 0 || fc > 4096) break;
                var raw = new byte[fc * 10];
                if (_f.Read(raw, 0, raw.Length) < raw.Length) break;
                var frames = new int[fc];
                var sounds = new ushort[fc];
                for (int j = 0; j < fc; j++)
                {
                    frames[j] = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(j * 10));
                    sounds[j] = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(j * 10 + 8));
                }
                c[(dir, act)] = new Action(dir, act, ms, frames, Graphics) { Sounds = sounds, PaletteImage = high ? id : 0 };
            }
        }
        _cache[id] = c;
        return c;
    }

    public void Dispose() => _f.Dispose();
}

/// <summary>客户端资源配置：一组资源文件（相对游戏根目录）。</summary>
public sealed record AssetSet(string Name, string Graphic, string GraphicInfo, string? Anime = null, string? AnimeInfo = null, int AnimeHeader = 12);

/// <summary>客户端用的全部资源：调色板 + 多组图像 + 多组动画。</summary>
public sealed class AssetLibrary : IDisposable
{
    public string Root { get; }
    public Palette Palette { get; }
    public GraphicLibrary Graphics { get; } = new();
    private readonly List<AnimeArchive> _animes = new();

    public AssetLibrary(string root, IEnumerable<AssetSet> sets, string palette = "bin/pal/palet_00.cgp")
    {
        Root = root;
        string P(string rel) => Path.Combine(root, rel.Replace('\\', '/'));
        Palette = GameFiles.Exists(P(palette)) ? Palette.FromCgp(P(palette)) : new Palette();
        foreach (var s in sets)
        {
            GraphicArchive? ga = null;
            if (GameFiles.Exists(P(s.GraphicInfo)) && GameFiles.Exists(P(s.Graphic))) Graphics.Add(ga = new GraphicArchive(P(s.GraphicInfo), P(s.Graphic), s.Name));
            if (s.Anime != null && s.AnimeInfo != null && GameFiles.Exists(P(s.AnimeInfo)) && GameFiles.Exists(P(s.Anime)))
                _animes.Add(new AnimeArchive(P(s.AnimeInfo), P(s.Anime), s.AnimeHeader) { Graphics = ga });
        }
    }

    private readonly Dictionary<int, Palette> _palettes = new();
    /// <summary>bin/pal/palet_NN.cgp（00–15 随时间/地图切换）。没有就用默认调色板。</summary>
    public Palette PaletteFor(int n)
    {
        if (n < 0) return Palette;
        if (_palettes.TryGetValue(n, out var p)) return p;
        var path = Path.Combine(Root, "bin", "pal", $"palet_{n:00}.cgp");
        p = GameFiles.Exists(path) ? Palette.FromCgp(path) : Palette;
        _palettes[n] = p; return p;
    }

    public IReadOnlyDictionary<(int Dir, int Act), AnimeArchive.Action> AnimeActions(int id)
    {
        for (int i = _animes.Count - 1; i >= 0; i--) if (_animes[i].Has(id)) return _animes[i].Actions(id);
        return new Dictionary<(int, int), AnimeArchive.Action>();
    }

    public void Dispose() { Graphics.Dispose(); foreach (var a in _animes) a.Dispose(); }
}
