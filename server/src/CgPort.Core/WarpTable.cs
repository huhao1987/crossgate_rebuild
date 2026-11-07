namespace CgPort.Core;

/// <summary>传送点：踩到 (FromMap, FromX, FromY) 时传到 (ToMap, ToX, ToY)。</summary>
public readonly record struct Warp(int Id, int FromMap, int FromX, int FromY, int ToMap, int ToX, int ToY);

/// <summary>
/// 服务端 data/warp.txt：Tab 分隔 7 列（编号, 源地图, 源X, 源Y, 目标地图, 目标X, 目标Y），
/// GBK 编码，# 或 // 开头为注释。列含义已用真实数据确认（地图列 100% 能在 data/map 中找到）。
/// </summary>
public sealed class WarpTable
{
    private readonly List<Warp> _all = new();
    private readonly Dictionary<(int, int, int), Warp> _bySource = new();
    public IReadOnlyList<Warp> All => _all;
    public int SkippedLines { get; private set; }

    public static WarpTable Load(string path)
    {
        var t = new WarpTable();
        foreach (var line in TabTable.ReadLines(path))
        {
            var c = line.Split('\t');
            if (c.Length < 7 || !TabTable.TryInts(c, 7, out var v)) { t.SkippedLines++; continue; }
            var w = new Warp(v[0], v[1], v[2], v[3], v[4], v[5], v[6]);
            t._all.Add(w);
            t._bySource.TryAdd((w.FromMap, w.FromX, w.FromY), w);
        }
        return t;
    }

    public bool TryGetAt(int map, int x, int y, out Warp warp) => _bySource.TryGetValue((map, x, y), out warp);
}

/// <summary>服务端 Tab 分隔数据表的通用读取（只处理字节层面的 ASCII 数字列，文本列保留原始字节串）。</summary>
public static class TabTable
{
    public static IEnumerable<string> ReadLines(string path)
    {
        // GBK 文本：数字和 Tab 都是 ASCII，用 Latin1 读取可无损保留字节，数字列可直接解析
        var bytes = GameFiles.ReadAllBytes(path);
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var text = System.Text.Encoding.Latin1.GetString(bytes, start, bytes.Length - start);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var t = line.TrimStart();
            if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("//")) continue;
            yield return line;
        }
    }

    public static bool TryInts(string[] cols, int count, out int[] values)
    {
        values = new int[count];
        for (int i = 0; i < count; i++)
            if (!int.TryParse(cols[i].Trim(), out values[i])) return false;
        return true;
    }
}
