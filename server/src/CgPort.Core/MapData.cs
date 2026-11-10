using System.Buffers.Binary;
using System.Text;

namespace CgPort.Core;

/// <summary>
/// 地图数据（地面层 + 物件层），x 向东、y 向南，index = y * Width + x。
/// 支持两种文件：
///  - 服务端 LS2MAP（大端）：'LS2MAP' + 2 字节 + 32 字节地图名 + u16 宽 + u16 高 + 地面层 + 物件层 [+ 其它]
///  - 客户端 MAP（小端）：'MAP' + 版本 + 8 字节 + i32 宽 + i32 高 + 地面层 + 物件层 + 标志层
/// </summary>
public sealed class MapData
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public ushort[] Ground { get; init; } = Array.Empty<ushort>();
    public ushort[] Objects { get; init; } = Array.Empty<ushort>();
    /// <summary>第三层：低 12 位是事件（服务端 aBS 第 3 段），高位是客户端自己的标志（官方缓存里收到过的格子是 0xC000）。没有就全 0。</summary>
    public ushort[] Events { get; init; } = Array.Empty<ushort>();

    /// <summary>空白地图（官方：本地没有缓存时按 GmNM 给的宽高建一张空图，内容全靠服务端 aBS 补）。</summary>
    public static MapData Blank(int id, int w, int h) => new()
    {
        Id = id, Width = w, Height = h, Format = "client",
        Ground = new ushort[w * h], Objects = new ushort[w * h], Events = new ushort[w * h],
    };

    /// <summary>存成官方客户端 map/0/楼层.dat 格式："MAP\x01" + 8 字节 0 + 宽 高(int32) + 地面 + 物件 + 第三层（小端 u16）。</summary>
    public byte[] ToClientBytes()
    {
        int n = Width * Height;
        var d = new byte[20 + n * 6];
        d[0] = (byte)'M'; d[1] = (byte)'A'; d[2] = (byte)'P'; d[3] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(12), Width);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(16), Height);
        for (int i = 0; i < n; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(20 + i * 2), Ground[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(20 + n * 2 + i * 2), Objects[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(20 + n * 4 + i * 2), Events.Length == n ? Events[i] : (ushort)0);
        }
        return d;
    }
    public string Format { get; init; } = "";
    /// <summary>地图指定的调色板编号（服务端地图名字段 "名字|编号"）；-1 = 随游戏时间变化（野外、城镇）。</summary>
    public int Palette { get; init; } = -1;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public int Index(int x, int y) => y * Width + x;

    public static MapData Load(string path, int id = 0, string? name = null)
    {
        var d = GameFiles.ReadAllBytes(path);
        if (d.Length >= 44 && Encoding.ASCII.GetString(d, 0, 6) == "LS2MAP")
        {
            int w = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x28)), h = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x2A));
            int n = w * h;
            if (d.Length < 44 + n * 4) throw new InvalidDataException($"LS2MAP 数据不完整: {path}");
            return new MapData
            {
                Id = id, Name = name ?? "", Width = w, Height = h, Format = "server", Palette = HeaderPalette(d),
                Ground = ReadU16(d, 44, n, bigEndian: true),
                Objects = ReadU16(d, 44 + n * 2, n, bigEndian: true),
                Events = d.Length >= 44 + n * 6 ? ReadU16(d, 44 + n * 4, n, bigEndian: true) : new ushort[n],
            };
        }
        if (d.Length >= 20 && d[0] == 'M' && d[1] == 'A' && d[2] == 'P')
        {
            int w = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(12)), h = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(16));
            int n = w * h;
            if (d.Length < 20 + n * 4) throw new InvalidDataException($"MAP 数据不完整: {path}");
            return new MapData
            {
                Id = id, Name = name ?? "", Width = w, Height = h, Format = "client",
                Ground = ReadU16(d, 20, n, bigEndian: false),
                Objects = ReadU16(d, 20 + n * 2, n, bigEndian: false),
                Events = d.Length >= 20 + n * 6 ? ReadU16(d, 20 + n * 4, n, bigEndian: false) : new ushort[n],
            };
        }
        throw new InvalidDataException($"无法识别的地图文件: {path}");
    }

    private static int HeaderPalette(byte[] d)
    {
        var raw = d.AsSpan(8, 32);
        int bar = raw.IndexOf((byte)'|');
        if (bar < 0) return -1;
        var tail = raw[(bar + 1)..]; int end = tail.IndexOf((byte)0); if (end >= 0) tail = tail[..end];
        return int.TryParse(Encoding.ASCII.GetString(tail), out var p) ? p : -1;
    }

    private static ushort[] ReadU16(byte[] d, int off, int n, bool bigEndian)
    {
        var r = new ushort[n];
        var s = d.AsSpan(off, n * 2);
        for (int i = 0; i < n; i++)
            r[i] = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(s[(i * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(s[(i * 2)..]);
        return r;
    }

    /// <summary>服务端地图目录：文件名形如 "<编号>_<地名>"。返回 编号 → (路径, 地名)。</summary>
    public static Dictionary<int, (string Path, string Name)> IndexServerMapDir(string dir)
    {
        var res = new Dictionary<int, (string, string)>();
        foreach (var f in GameFiles.EnumerateFiles(dir))
        {
            var fn = Path.GetFileName(f);
            int us = fn.IndexOf('_');
            var idPart = us > 0 ? fn[..us] : fn;
            if (int.TryParse(idPart, out int id)) res[id] = (f, us > 0 ? fn[(us + 1)..] : "");
        }
        return res;
    }
}


/// <summary>
/// 游戏内时间 → 室外时间调色板。实测（官方客户端在法兰城挂 4 小时，每分钟截图判别调色板）：
/// 一天 = 现实 7200 秒；以 (unix 时间 − 95) mod 7200 计：0–2700 白天 palet_00、2700–3600 黄昏 01、
/// 3600–6300 夜晚 02、6300–7200 清晨 03（实测采样 177 个全部吻合，偏移量 95 的误差约 ±3 秒）。时间用服务端时间（LI 包里给）。
/// </summary>
public static class CgClock
{
    /// <summary>
    /// 左上角天色值 0–191（exe 0x41636d）：t = 现在 − 0x3ae805e7，一个游戏日 7200 秒，值 = ((t mod 7200)×192/7200 + 168) mod 192。
    /// </summary>
    public static int Sky(long unix)
    {
        long t = ((unix - 0x3ae805e7) % 7200 + 7200) % 7200;
        return (int)((t * 192 / 7200 + 168) % 192);
    }

    public static int Palette(long unix)
    {
        long t = ((unix - 95) % 7200 + 7200) % 7200;
        return t < 2700 ? 0 : t < 3600 ? 1 : t < 6300 ? 2 : 3;
    }
}
