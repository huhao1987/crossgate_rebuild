using System.Text;

namespace CgPort.Core.Text;

/// <summary>
/// GBK 解码（服务端数据表、NPC 脚本、地图名都是 GBK）。.NET 默认不带 GBK 编码，
/// 这里用内嵌的对照表（由标准 GBK 编码生成：首字节 0x81–0xFE × 尾字节 0x40–0xFE → UTF-16）。
/// </summary>
public static class Gbk
{
    private static readonly ushort[] Table = LoadTable();

    private static ushort[] LoadTable()
    {
        using var s = typeof(Gbk).Assembly.GetManifestResourceStream("CgPort.Core.Text.gbk_table.bin")
                      ?? throw new InvalidOperationException("缺少 GBK 对照表资源");
        var bytes = new byte[s.Length];
        s.ReadExactly(bytes);
        var t = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, t, 0, bytes.Length);
        return t;
    }

    public static string Decode(ReadOnlySpan<byte> b)
    {
        var sb = new StringBuilder(b.Length);
        for (int i = 0; i < b.Length; i++)
        {
            byte c = b[i];
            if (c < 0x80) { sb.Append((char)c); continue; }
            if (c >= 0x81 && c <= 0xFE && i + 1 < b.Length && b[i + 1] >= 0x40 && b[i + 1] <= 0xFE)
            {
                sb.Append((char)Table[(c - 0x81) * 191 + (b[i + 1] - 0x40)]);
                i++;
                continue;
            }
            sb.Append('�');
        }
        return sb.ToString();
    }

    private static Dictionary<char, ushort>? _rev;

    /// <summary>编码成 GBK（发给服务端的角色名、聊天内容）。表里没有的字符写成 '?'。</summary>
    public static byte[] Encode(string s)
    {
        if (_rev == null)
        {
            var r = new Dictionary<char, ushort>();
            for (int i = 0; i < Table.Length; i++)
            {
                char ch = (char)Table[i];
                if (ch == 0 || ch == '\uFFFD' || r.ContainsKey(ch)) continue;
                r[ch] = (ushort)(((0x81 + i / 191) << 8) | (0x40 + i % 191));
            }
            _rev = r;
        }
        var o = new List<byte>(s.Length * 2);
        foreach (var ch in s)
        {
            if (ch < 0x80) o.Add((byte)ch);
            else if (_rev.TryGetValue(ch, out var g)) { o.Add((byte)(g >> 8)); o.Add((byte)g); }
            else o.Add((byte)'?');
        }
        return o.ToArray();
    }

    /// <summary>数据表按 Latin1 读进来的字符串（每个字符就是一个原始字节）→ 真正的中文。</summary>
    public static string FromLatin1(string raw) => Decode(Encoding.Latin1.GetBytes(raw));

    public static string ReadFile(string path)
    {
        var b = GameFiles.ReadAllBytes(path);
        int start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
        // 带 BOM 的按 UTF-8 读，其余按 GBK
        return start == 3 ? Encoding.UTF8.GetString(b, 3, b.Length - 3) : Decode(b);
    }
}
