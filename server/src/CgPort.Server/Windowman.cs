using CgPort.Core.Text;

namespace CgPort.Server;

/// <summary>
/// Windowman 类 NPC 的窗口脚本（data/npc/WM_*.txt 等，npc.txt 第 24 列）。
/// 格式（对照 cgmsv 实测：见习封印师鲁德 WM_shibata_2010、路标 WM_ijk_2026）：
///   标签:                         一页的开始，从 PREV_00 开始（没有就从第一个标签）
///   Window 消息号,按钮,按钮…       文字取 msg.txt；# 开头是注释
/// 按钮：SELECT[n]_xxx = 选项 n（有选项的页是选择窗口，类型 2），NEXT_xx / PREV_xx / OK / CANCEL / YES / NO_x …
///   按钮名本身就是要跳去的标签名；没有这个标签就关闭窗口（OK、CANCEL 一般如此）。
/// 实测服务端：Window 60050,SELECT[1]_100,SELECT[2]_100,CANCEL → "BZ 2 2"；Window 60052,OK,PREV_00 → "BZ 0 h"（1+16）。
/// </summary>
public sealed class WindowmanScript
{
    public sealed record Page(int MsgId, IReadOnlyList<string> Tokens);
    private readonly Dictionary<string, Page> _pages = new(StringComparer.Ordinal);
    public string Start { get; private set; } = "";

    public static WindowmanScript Parse(string text)
    {
        var w = new WindowmanScript();
        string label = "", first = "";   // 有的脚本没有标签，直接一行 Window（WM_aoki_2058）
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("Window", StringComparison.OrdinalIgnoreCase) && line.Length > 6 && char.IsWhiteSpace(line[6]))
            {
                var parts = line[7..].Split(',').Select(t => t.Trim()).ToList();
                // 行内注释：按钮后面跟 # 的去掉
                for (int i = 0; i < parts.Count; i++) { int h = parts[i].IndexOf('#'); if (h >= 0) parts[i] = parts[i][..h].Trim(); }
                if (int.TryParse(parts[0], out var id) && !w._pages.ContainsKey(label))
                {
                    if (w._pages.Count == 0) first = label;
                    w._pages[label] = new Page(id, parts.Skip(1).Where(t => t.Length > 0).ToList());
                }
                continue;
            }
            int colon = line.IndexOf(':');
            if (colon > 0 && !line[..colon].Contains(' '))
            {
                label = line[..colon];
            }
        }
        w.Start = w._pages.ContainsKey("PREV_00") ? "PREV_00" : first;
        return w;
    }

    public static WindowmanScript Load(string path) => Parse(Gbk.ReadFile(path));

    public Page? Get(string label) => _pages.TryGetValue(label, out var p) ? p : null;

    /// <summary>按钮名 → 种类（ok/cancel/yes/no/prev/next/select）。</summary>
    public static string Kind(string token)
    {
        var t = token.ToUpperInvariant();
        if (t.StartsWith("SELECT")) return "select";
        if (t.StartsWith("NEXT")) return "next";
        if (t.StartsWith("PREV")) return "prev";
        if (t.StartsWith("CANCEL")) return "cancel";
        if (t.StartsWith("YES")) return "yes";
        if (t.StartsWith("NO")) return "no";
        if (t.StartsWith("OK")) return "ok";
        return "";
    }

    /// <summary>窗口类型（有选项 = 2 选择窗口，否则 0）和按钮掩码（1 确定 2 取消 4 是 8 否 16 上一页 32 下一页），与服务端一致。</summary>
    public static (int Type, int Mask) Shape(Page p)
    {
        int mask = 0; bool select = false;
        foreach (var k in p.Tokens.Select(Kind))
            switch (k)
            {
                case "select": select = true; break;
                case "ok": mask |= 1; break;
                case "cancel": mask |= 2; break;
                case "yes": mask |= 4; break;
                case "no": mask |= 8; break;
                case "prev": mask |= 16; break;
                case "next": mask |= 32; break;
            }
        return (select ? 2 : 0, mask);
    }

    /// <summary>按下按钮（"next" 等）或选第 n 个选项（"opt:0" 起），返回下一页的标签；null = 关闭。</summary>
    public string? Press(Page p, string button)
    {
        string? tok;
        if (button.StartsWith("opt:") && int.TryParse(button[4..], out var n))
        {
            // 选项 n（界面从 0 数）对应 SELECT[n+1]；按钮里没有编号的就按出现顺序
            tok = p.Tokens.FirstOrDefault(t => t.StartsWith($"SELECT[{n + 1}]", StringComparison.OrdinalIgnoreCase))
                  ?? p.Tokens.Where(t => Kind(t) == "select").ElementAtOrDefault(n);
        }
        else tok = p.Tokens.FirstOrDefault(t => Kind(t) == button);
        return tok != null && _pages.ContainsKey(tok) ? tok : null;
    }
}
