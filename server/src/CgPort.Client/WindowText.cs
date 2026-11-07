using CgPort.Server;

namespace CgPort.Client;

/// <summary>服务端窗口（BZ）文字 → 对话页。联网和单机共用，保证两边显示一致。</summary>
public static class WindowText
{
    /// <summary>按钮掩码位（62 进制收发，见 PROTOCOL.md）。</summary>
    public static readonly (int Bit, string Name)[] Buttons = { (1, "ok"), (2, "cancel"), (4, "yes"), (8, "no"), (16, "prev"), (32, "next") };

    public static List<string> ButtonNames(int mask) => Buttons.Where(b => (mask & b.Bit) != 0).Select(b => b.Name).ToList();

    /// <summary>
    /// text：已解码的文字，用真正的 '\n' 分行。header：选项前的行数，null = 选择窗口从第一行的数字读
    /// （官方实测：选择窗口文字第一行只有一个数字 N，"这一行在内，选项前共 N 行"，这一行不显示）。
    /// </summary>
    public static DialogPage Build(int type, int mask, string text, int? header = null)
    {
        var lines = text.Split('\n').ToList();
        int h = header ?? 0;
        if (header == null && type == 2 && lines.Count > 0 && int.TryParse(lines[0].Trim(), out var n)) { h = Math.Max(0, n - 1); lines.RemoveAt(0); }
        while (lines.Count > h + 1 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);   // 末尾空行官方不占高度
        var buttons = ButtonNames(mask);
        return type == 2
            ? new DialogPage(string.Join("\n", lines.Take(h)), buttons, false, lines.Skip(h).ToList())
            : new DialogPage(string.Join("\n", lines), buttons.Count > 0 ? buttons : new List<string> { "ok" }, true);
    }
}
