namespace CgPort.Protocol;

/// <summary>
/// 封包日志解析，拿到真实日志后用来统计包头、对照操作：
///  - proto_recv.log：我们自己的 Lua 钩子输出，格式确定：时间\tfd\t角色\t包头\t字段1|字段2|…
///  - packet_*.txt：cgmsv 自带的 logPacket 输出，格式未知，先宽松地找出已知包头所在的位置。
/// </summary>
public static class PacketLog
{
    public sealed record Line(string Source, string Direction, string Header, string[] Fields, string Raw);

    public static IEnumerable<Line> ReadProtoRecv(string path)
    {
        foreach (var raw in File.ReadLines(path, System.Text.Encoding.Latin1))
        {
            if (raw.StartsWith('#')) continue;
            var c = raw.Split('\t');
            if (c.Length < 5) continue;
            yield return new Line("proto_recv", "C2S", c[3], c[4].Length == 0 ? Array.Empty<string>() : c[4].Split('|'), raw);
        }
    }

    public static IEnumerable<Line> ReadCgmsvPacketLog(string path, HeaderRegistry reg)
    {
        foreach (var raw in File.ReadLines(path, System.Text.Encoding.Latin1))
        {
            var tokens = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int hi = Array.FindIndex(tokens, t => reg.IsKnown(t));
            if (hi < 0) continue;
            string dir = raw.Contains("Send", StringComparison.OrdinalIgnoreCase) ? "S2C"
                       : raw.Contains("Recv", StringComparison.OrdinalIgnoreCase) ? "C2S" : "?";
            yield return new Line("cgmsv", dir, tokens[hi], tokens[(hi + 1)..], raw);
        }
    }

    /// <summary>按包头统计出现次数和字段数分布。</summary>
    public static IEnumerable<(string Header, string Direction, int Count, string FieldCounts)> Summarize(IEnumerable<Line> lines) =>
        lines.GroupBy(l => (l.Header, l.Direction))
             .Select(g => (g.Key.Header, g.Key.Direction, g.Count(),
                 string.Join(",", g.GroupBy(l => l.Fields.Length).OrderByDescending(x => x.Count()).Take(3).Select(x => $"{x.Key}字段×{x.Count()}"))))
             .OrderByDescending(x => x.Item3);
}
