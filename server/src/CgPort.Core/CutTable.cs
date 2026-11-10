using System.Buffers.Binary;
using System.Text;
using CgPort.Core.Assets;

namespace CgPort.Core;

/// <summary>
/// 大图切块地图（官方 exe 0x437163：进图封包 GmNM 第 9 段 = 1 时读 bin/puk2/cutdat/地图号.cut，找不到提示"未找到cut数据，请更新客户端。"）。
/// 文件："CUT " + u16 头长(12) + u16 地面块数 + u16 物件块数 + u16 动画块数，然后每块 18 字节：
///   u32 图像（全局编号）、u16 种类（0 = 从大图切一块，1 = 动画）、u16 源 x、u16 源 y、u16 宽、u16 高、u16 锚点 x、u16 锚点 y。
/// 地图里的地面 / 物件编号 1000+k 就是第 k 块（地面块在前，物件、动画块接着排，1100 = 第 100 块）。
/// 例：修拉特瓦尔号 59529 = 鲸鱼飞船 301114（640×480）切成 10×10 块 64×48、锚点 (32,24)；甲板物件从 301239 切；5 个动画（113058–113062）。
/// </summary>
public sealed class CutTable
{
    public readonly record struct Piece(int Image, int Kind, int Sx, int Sy, int W, int H, int Ox, int Oy);
    public const int Base = 1000;
    public IReadOnlyList<Piece> Pieces { get; }
    public int GroundCount { get; }

    private CutTable(List<Piece> p, int ground) { Pieces = p; GroundCount = ground; }

    public bool TryGet(int id, out Piece p)
    {
        int k = id - Base;
        if (k >= 0 && k < Pieces.Count) { p = Pieces[k]; return true; }
        p = default; return false;
    }

    public static CutTable? Load(string gameRoot, int mapId)
    {
        foreach (var dir in new[] { "bin/puk2/cutdat", "bin/Puk2/cutdat", "bin/PUK2/cutdat" })
        {
            var path = Path.Combine(gameRoot, dir, mapId + ".cut");
            if (!GameFiles.Exists(path)) continue;
            var d = GameFiles.ReadAllBytes(path);
            if (d.Length < 12 || Encoding.ASCII.GetString(d, 0, 4) != "CUT ") return null;
            int hdr = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(4));
            int ground = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(6));
            var list = new List<Piece>();
            for (int o = hdr; o + 18 <= d.Length; o += 18)
            {
                var s = d.AsSpan(o);
                list.Add(new Piece((int)BinaryPrimitives.ReadUInt32LittleEndian(s), BinaryPrimitives.ReadUInt16LittleEndian(s[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(s[6..]), BinaryPrimitives.ReadUInt16LittleEndian(s[8..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(s[10..]), BinaryPrimitives.ReadUInt16LittleEndian(s[12..]),
                    BinaryPrimitives.ReadInt16LittleEndian(s[14..]), BinaryPrimitives.ReadInt16LittleEndian(s[16..])));   // 锚点有符号（动画块 65515 = −21）
            }
            return new CutTable(list, ground);
        }
        return null;
    }
}
