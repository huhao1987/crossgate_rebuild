namespace CgPort.Core;

/// <summary>GraphicInfo 中与地图有关的字段（服务端只关心通行和占格，不需要像素）。</summary>
public readonly record struct TileInfo(int GraphicId, int MapId, byte Flag, byte East, byte South, int Width, int Height, int OffsetX, int OffsetY)
{
    /// <summary>已用传送目的地验证：标志 1 = 可通行（地面、物件通用）。</summary>
    public bool Passable => Flag == 1;
}

/// <summary>
/// 按 map_id 索引的地块信息表。可加载多组 GraphicInfo，后加载的覆盖先加载的（与客户端一致）。
/// GraphicInfo 每条 40 字节（小端）：id, offset, length, ox, oy, w, h, east(u8), south(u8), flag(u8), 5 字节保留, map_id。
/// </summary>
public sealed class TileInfoTable
{
    public const int EntrySize = 40;
    private readonly Dictionary<int, TileInfo> _byMapId = new();

    public int Count => _byMapId.Count;

    public int LoadGraphicInfo(string path)
    {
        var data = GameFiles.ReadAllBytes(path);
        int n = data.Length / EntrySize, added = 0;
        var span = data.AsSpan();
        for (int i = 0; i < n; i++)
        {
            var e = span.Slice(i * EntrySize, EntrySize);
            int mapId = BitConverter.ToInt32(e[36..40]);
            if (mapId == 0) continue;
            _byMapId[mapId] = new TileInfo(
                GraphicId: BitConverter.ToInt32(e[0..4]),
                MapId: mapId,
                Flag: e[30], East: e[28], South: e[29],
                Width: BitConverter.ToInt32(e[20..24]), Height: BitConverter.ToInt32(e[24..28]),
                OffsetX: BitConverter.ToInt32(e[12..16]), OffsetY: BitConverter.ToInt32(e[16..20]));
            added++;
        }
        return added;
    }

    public bool TryGet(int mapId, out TileInfo info) => _byMapId.TryGetValue(mapId, out info);

    /// <summary>客户端 bin 目录下按 cgmsv.yml 顺序的默认 GraphicInfo 列表（存在才加载）。</summary>
    public static readonly string[] DefaultGraphicInfoFiles =
    {
        "GraphicInfo_66.bin", "GraphicInfoEx_5.bin", "GraphicInfoV3_19.bin",
        "Puk1/GraphicInfo_PUK1_1.bin", "Puk2/GraphicInfo_PUK2_2.bin", "Puk3/GraphicInfo_PUK3_1.bin",
        "GraphicInfo_Joy_125.bin", "GraphicInfo_Joy_EX_110.bin", "Puk4/GraphicInfo_PUK4_1.bin", "Puk7/GraphicInfo_PUK7_1.bin",
    };

    public static TileInfoTable LoadDefault(string binDir)
    {
        var t = new TileInfoTable();
        foreach (var rel in DefaultGraphicInfoFiles)
        {
            var p = Path.Combine(binDir, rel);
            if (GameFiles.Exists(p)) t.LoadGraphicInfo(p);
        }
        return t;
    }
}
