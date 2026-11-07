using System.Text;

namespace CgPort.Protocol;

/// <summary>
/// cgmsv 封包（文本层）：<c>包头 字段1 字段2 …</c>，空格分隔。
/// 依据：服务端 Lua 框架里的 Protocol.Send 实现——数字字段用 62 进制，字符串字段经 nrproto 转义。
/// 线上是否还有一层加密/校验尚未确认，由 <see cref="IWireLayer"/> 承担，默认直通。
/// </summary>
public sealed record Packet(string Header, IReadOnlyList<string> Fields)
{
    public override string ToString() => Fields.Count == 0 ? Header : Header + " " + string.Join(' ', Fields);
}

/// <summary>62 进制数字。字母表顺序是推测（与同系列协议一致），拿到真实封包后核对。</summary>
public static class Base62
{
    public static string Alphabet { get; set; } = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Encode(long v)
    {
        if (v == 0) return Alphabet[0].ToString();
        bool neg = v < 0; ulong u = neg ? (ulong)(-v) : (ulong)v;
        var sb = new StringBuilder();
        while (u > 0) { sb.Insert(0, Alphabet[(int)(u % 62)]); u /= 62; }
        return neg ? "-" + sb : sb.ToString();
    }

    public static long Decode(string s)
    {
        if (string.IsNullOrEmpty(s)) throw new FormatException("空字符串");
        bool neg = s[0] == '-'; long v = 0;
        for (int i = neg ? 1 : 0; i < s.Length; i++)
        {
            int d = Alphabet.IndexOf(s[i]);
            if (d < 0) throw new FormatException($"非法 62 进制字符 '{s[i]}'");
            v = checked(v * 62 + d);
        }
        return neg ? -v : v;
    }
}

/// <summary>字符串字段转义。cgmsv 的 nrprotoEscapeString 具体规则待确认，这里先实现常见写法并允许替换。</summary>
public interface IFieldEscaper
{
    string Escape(string s);
    string Unescape(string s);
}

/// <summary>推测的转义：反斜杠转义空格、反斜杠、换行、竖线、逗号。拿到日志后按实际规则修正。</summary>
public sealed class BackslashEscaper : IFieldEscaper
{
    private static readonly (char Raw, char Code)[] Map = { ('\\', '\\'), (' ', 'S'), ('\n', 'n'), ('|', 'z'), (',', 'c') };

    public string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            int i = Array.FindIndex(Map, m => m.Raw == ch);
            if (i >= 0) sb.Append('\\').Append(Map[i].Code); else sb.Append(ch);
        }
        return sb.ToString();
    }

    public string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int k = 0; k < s.Length; k++)
        {
            if (s[k] == '\\' && k + 1 < s.Length)
            {
                int i = Array.FindIndex(Map, m => m.Code == s[k + 1]);
                if (i >= 0) { sb.Append(Map[i].Raw); k++; continue; }
            }
            sb.Append(s[k]);
        }
        return sb.ToString();
    }
}

/// <summary>线路层（加密/校验/分包），未确认前用直通实现。</summary>
public interface IWireLayer
{
    byte[] Wrap(string text);
    IEnumerable<string> Unwrap(byte[] data);
}

/// <summary>直通：每个封包一行，以换行结尾。</summary>
public sealed class PlainLineWire : IWireLayer
{
    public Encoding Encoding { get; init; } = Encoding.Latin1;
    public byte[] Wrap(string text) => Encoding.GetBytes(text + "\n");
    public IEnumerable<string> Unwrap(byte[] data) =>
        Encoding.GetString(data).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'));
}

/// <summary>封包文本编解码。</summary>
public sealed class PacketCodec
{
    public IFieldEscaper Escaper { get; init; } = new BackslashEscaper();

    public Packet Parse(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("空封包");
        return new Packet(parts[0], parts[1..]);
    }

    public string Build(string header, params object[] fields)
    {
        var sb = new StringBuilder(header);
        foreach (var f in fields)
        {
            sb.Append(' ');
            sb.Append(f switch
            {
                int i => Base62.Encode(i),
                long l => Base62.Encode(l),
                string s => Escaper.Escape(s),
                _ => throw new ArgumentException($"不支持的字段类型 {f?.GetType().Name}"),
            });
        }
        return sb.ToString();
    }

    public long Int(Packet p, int i) => Base62.Decode(p.Fields[i]);
    public string Str(Packet p, int i) => Escaper.Unescape(p.Fields[i]);
}
