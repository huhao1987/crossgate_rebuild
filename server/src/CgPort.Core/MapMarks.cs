using System.Buffers.Binary;
using CgPort.Core.Text;

namespace CgPort.Core;

/// <summary>
/// 大地图标注（官方 exe 资源 TEXT 238，GBK 制表符分隔）：地图号 类别 X Y 名称 图档 [显示位置]。
/// 类别 0 地名 1 伐木 2 挖掘 3 狩猎 4 村镇级别 5 宠物纯点 6 NPC；显示位置 0/空 上、1 下、2 左、3 右；
/// 类别 1–3 图档 18 = 不显示图；类别 6 时图档 0–9 = 名字颜色。
/// </summary>
public sealed record MapLabel(int Map, int Kind, int X, int Y, string Name, int Image, int Pos)
{
    public static List<MapLabel> Parse(string text)
    {
        var r = new List<MapLabel>();
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.Length == 0 || l[0] == '#') continue;
            var f = l.Split('\t');
            if (f.Length < 5 || !int.TryParse(f[0], out var m) || !int.TryParse(f[1], out var k) || !int.TryParse(f[2], out var x) || !int.TryParse(f[3], out var y)) continue;
            int img = f.Length > 5 && int.TryParse(f[5], out var i) ? i : 0, pos = f.Length > 6 && int.TryParse(f[6], out var p) ? p : 0;
            r.Add(new MapLabel(m, k, x, y, f[4].Trim(), img, pos));
        }
        return r;
    }
}

/// <summary>
/// 自动寻路坐标点（这个客户端的 data/mappoint.dat）：每条 40 字节 = 有效(int32，1 有 0 空) 编号?(int32) 名字(24 字节 GBK) 地图(int32) 东(int16) 南(int16)。
/// 寻路列表按当前地图列出，玩家可以改名、改坐标（设定）。
/// </summary>
public sealed class MapPoint
{
    public int Used, Tag, Map, X, Y;
    public string Name = "";

    public static List<MapPoint> Load(byte[] d)
    {
        var r = new List<MapPoint>();
        for (int i = 0; i + 40 <= d.Length; i += 40)
        {
            var s = d.AsSpan(i + 8, 24); int z = s.IndexOf((byte)0); if (z >= 0) s = s[..z];
            r.Add(new MapPoint
            {
                Used = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(i)), Tag = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(i + 4)),
                Name = Gbk.Decode(s.ToArray()), Map = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(i + 32)),
                X = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(i + 36)), Y = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(i + 38)),
            });
        }
        return r;
    }

    public static byte[] Save(IReadOnlyList<MapPoint> pts)
    {
        var d = new byte[pts.Count * 40];
        for (int k = 0; k < pts.Count; k++)
        {
            var p = pts[k]; int i = k * 40;
            BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(i), p.Used);
            BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(i + 4), p.Tag);
            var n = Gbk.Encode(p.Name); Array.Copy(n, 0, d, i + 8, Math.Min(23, n.Length));
            BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(i + 32), p.Map);
            BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(i + 36), (short)p.X);
            BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(i + 38), (short)p.Y);
        }
        return d;
    }
}
