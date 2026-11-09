using Godot;

namespace CgPort.Godot;

/// <summary>
/// 我的口頭禪（系统 → 聊天设定 → 常用句 <设定>，官方实测）：背景 22950 / 22951 / 22952（第一 / 二 / 三页，272×438 在 (0,0)），
/// 每页 8 条：第一页 F1–F8、第二页 Ctrl+F1–F8、第三页 Shift+F1–F8。第 k 条输入框（蓝条）x 18–256、y 89+42k 高 22；
/// 页签 第一頁 (5–90)、第二頁 (95–175)、第三頁 (180–265)，y 40–62；「確定」22712 中心 (137,417) 存起来。
/// 按对应热键 = 直接说这句话（手机上没有 F 键，点每条左边的标签也能说）。存在 user://phrases.txt（一行一条，共 24 行）。
/// </summary>
public partial class PhraseWindow : CgWindow
{
    public const int Count = 24;
    public static string[] Load()
    {
        var r = new string[Count];
        try
        {
            if (global::Godot.FileAccess.FileExists("user://phrases.txt"))
            {
                var ls = global::Godot.FileAccess.GetFileAsString("user://phrases.txt").Split('\n');
                for (int i = 0; i < Count && i < ls.Length; i++) r[i] = ls[i].TrimEnd('\r');
            }
        }
        catch { }
        for (int i = 0; i < Count; i++) r[i] ??= "";
        return r;
    }
    public static void Save(string[] p)
    {
        try { using var f = global::Godot.FileAccess.Open("user://phrases.txt", global::Godot.FileAccess.ModeFlags.Write); f.StoreString(string.Join("\n", p)); } catch { }
    }

    public string[] Phrases = Load();
    public Action<string>? Say;
    private int _page;
    private readonly LineEdit[] _edits = new LineEdit[8];
    public int Page => _page;

    public override void _Ready()
    {
        Layer = 17; Visible = false; Build(Vector2.Zero, false);
        for (int k = 0; k < 8; k++)
        {
            var e = new LineEdit { Position = new Vector2(18, 89 + 42 * k), Size = new Vector2(238, 22), Flat = true, MaxLength = 60, ContextMenuEnabled = false };
            e.AddThemeColorOverride("font_color", Colors.White);
            e.AddThemeFontSizeOverride("font_size", 13);
            e.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); e.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            int kk = k;
            e.TextChanged += t => Phrases[_page * 8 + kk] = t;
            Win.AddChild(e); _edits[k] = e;
        }
    }

    public override void Refresh()
    {
        Clear();
        Img(22950 + _page, 0, 0, block: true);
        Hit(5, 40, 85, 22, _ => Show(0), box: false);
        Hit(95, 40, 80, 22, _ => Show(1), box: false);
        Hit(180, 40, 85, 22, _ => Show(2), box: false);
        for (int k = 0; k < 8; k++)
        {
            int i = _page * 8 + k;
            Hit(8, 70 + 42 * k, 82, 16, _ => { if (Phrases[i].Length > 0) Say?.Invoke(Phrases[i]); });   // 点标签说出来（手机用）
            _edits[k].Text = Phrases[i];
            _edits[k].MoveToFront();
        }
        var ok = Cache.ImageNo(22712).Tex?.GetSize() ?? new Vector2(80, 20);
        Btn(22712, 22713, 137 - ok.X / 2, 417 - ok.Y / 2, () => { Save(Phrases); GameAudio.Se(51); Visible = false; });   // 確定（中心 (137,417)，截图量的）
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }

    private void Show(int page) { _page = page; GameAudio.Se(51); Refresh(); }
    public void Open() { _page = 0; Visible = true; Refresh(); }

    /// <summary>热键：F1–F8 第一页、Ctrl 第二页、Shift 第三页。返回 true = 这一条有内容、已经说了。</summary>
    public bool Hotkey(Key key, bool ctrl, bool shift)
    {
        if (key < Key.F1 || key > Key.F8) return false;
        int i = (shift ? 16 : ctrl ? 8 : 0) + (int)(key - Key.F1);
        if (Phrases[i].Length == 0) return false;
        Say?.Invoke(Phrases[i]);
        return true;
    }
}
