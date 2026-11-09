using CgPort.Client;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 名片簿（底部菜单"名片"，官方实测）：背景 23100 在 (0,0)，一页 4 张（每张往下 90），共 15 页 60 张。
/// 每张：头像框 (7,36) 64×72、在线/离线小框 (77,40)、等级（右对齐 x 207）、名字/家族/称号（值从 x 99）；
/// 右边 郵件 23101 (211,42)、記錄 23103 (211,66)、刪除 23105 (211,90)。底下 排列 23107 (20,396)、翻页 22832/22834 (139,396)/(226,396)、页码居中 x 197。
/// 关闭 22509 (246,6)。
/// </summary>
public partial class CardBookWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public Action<int>? WriteMail;     // 点「郵件」
    public Action<int>? ShowRecord;    // 点「記錄」
    public Action<int>? AskDelete;     // 点「刪除」
    private int _page;
    public int Page => _page;

    public override void _Ready() { Layer = 12; Visible = false; Build(Vector2.Zero, false); }

    public override void Refresh()
    {
        Clear();
        var s = Session(); if (s == null) return;
        Img(23100, 0, 0, block: true);
        for (int k = 0; k < 4; k++)
        {
            int idx = _page * 4 + k; float dy = 90 * k;
            Txt("名字", 77, 61 + dy); Txt("家族", 77, 80 + dy); Txt("稱號", 77, 98 + dy);
            var c = s.Cards[idx];
            if (c == null) continue;
            if (c.Portrait > 0 && Cache.ImageNo(c.Portrait).Tex is { } face)
            {
                var sz = face.GetSize();
                Body.AddChild(new TextureRect { Texture = face, Position = new Vector2(7 + (64 - sz.X) / 2, 36 + (72 - sz.Y) / 2), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            Txt(c.Online ? "在线" : "离线", 107, 41 + dy, c.Online ? new Color(1, 1, 0) : Colors.White, center: true);
            Txt(c.Level.ToString(), 207, 46 + dy, right: true);
            Txt(c.Name, 113, 61 + dy);
            Txt(c.Guild, 113, 80 + dy);
            Txt(c.Title, 113, 98 + dy);
            Btn(23101, 23102, 211, 42 + dy, () => WriteMail?.Invoke(idx));
            Btn(23103, 23104, 211, 66 + dy, () => ShowRecord?.Invoke(idx));
            Btn(23105, 23106, 211, 90 + dy, () => AskDelete?.Invoke(idx));
        }
        Btn(23107, 23108, 20, 396, () => { });   // 排列：官方行为还没查
        Btn(22832, 22833, 139, 396, () => { _page = (_page + 14) % 15; Refresh(); });
        Btn(22834, 22835, 226, 396, () => { _page = (_page + 1) % 15; Refresh(); });
        Txt($"{_page + 1}/15", 197, 402, center: true);
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }
}

/// <summary>
/// 编辑邮件（官方实测）：背景 23195 在 (0,0)。上面收件人一页 4 个（选中的青色），翻页 22832/22834 在 (78,148)/(173,148)，页码居中 x 140；
/// 「送出」23196 (36,186)、「上一頁」22714 (156,186)（回名片簿）；下面是写信区。
/// </summary>
public partial class MailEditWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public Action? Back;
    private int _sel, _page;
    private TextEdit _edit = null!;
    public int Selected => _sel;

    public override void _Ready()
    {
        Layer = 13; Visible = false; Build(Vector2.Zero, false);
        _edit = MakeEdit();
        Win.AddChild(_edit);
    }

    /// <summary>写信区（普通 / 宠物邮件共用）：(8,226) 256×196，白字，最多 140 字。</summary>
    public static TextEdit MakeEdit()
    {
        var edit = new TextEdit
        {
            Position = new Vector2(8, 226), Size = new Vector2(256, 196), WrapMode = TextEdit.LineWrappingMode.Boundary,
            ContextMenuEnabled = false, PlaceholderText = "",
        };
        var clear = new StyleBoxEmpty();
        foreach (var n in new[] { "normal", "focus", "read_only" }) edit.AddThemeStyleboxOverride(n, clear);
        edit.AddThemeColorOverride("font_color", Colors.White);
        edit.AddThemeColorOverride("caret_color", Colors.White);
        edit.AddThemeFontSizeOverride("font_size", 15);
        edit.TextChanged += () => { if (edit.Text.Length > 140) { edit.Text = edit.Text[..140]; edit.SetCaretColumn(edit.Text.Length); } };
        return edit;
    }

    public void Open(int cardIndex)
    {
        _sel = cardIndex; _page = Math.Max(0, cardIndex) / 4; _edit.Text = "";
        Visible = true; Refresh(); _edit.GrabFocus();
    }

    public string Text { get => _edit.Text; set => _edit.Text = value; }

    public override void Refresh()
    {
        Clear();
        var s = Session(); if (s == null) return;
        Img(23195, 0, 0, block: true);
        for (int k = 0; k < 4; k++)
        {
            int idx = _page * 4 + k;
            var c = s.Cards[idx];
            if (c == null) continue;
            Txt(c.Name, 140, 42 + 27 * k, idx == _sel ? new Color(0, 1, 1) : Colors.White, center: true);
            Hit(40, 38 + 27 * k, 200, 20, _ => { _sel = idx; Refresh(); });
        }
        Btn(22832, 22833, 78, 148, () => { _page = (_page + 14) % 15; Refresh(); });
        Btn(22834, 22835, 173, 148, () => { _page = (_page + 1) % 15; Refresh(); });
        Txt($"{_page + 1}/15", 140, 154, center: true);
        Btn(23196, 23197, 36, 186, DoSend);
        Btn(22714, 22715, 156, 186, () => { Visible = false; Back?.Invoke(); });
        Btn(22509, 22510, 246, 6, () => Visible = false);
        _edit.MoveToFront();
    }

    public void DoSend()
    {
        var t = _edit.Text.Replace("\n", " ").Trim();
        if (t.Length == 0) return;
        Session()?.SendMail(_sel, t);
        _edit.Text = "";
    }
}

/// <summary>
/// 邮件记录（名片上点「記錄」，有新信时点左上「郵件」也直接开这里；官方 exe 0x446f80 起 + 截图）：背景 23190 在 (0,0)。
/// 上面对方头像 (7,36) 64×72、等级从 x 150、名字中心 (167,72)。一页一封（最新的在第 1 页），共 10 页：
/// 「郵件」23101 中心 (60,140)（回信 → 编辑邮件）、日期从 (130,132)、内容从 (16,182)；
/// 底下 ◀ 22881 中心 (28,410)、页码中心 (76,409)、▶ 22883 中心 (125,410)、「上一頁」22714 中心 (200,410)（回名片簿）。
/// </summary>
public partial class MailRecordWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public Action<int>? Reply;
    public Action? Back;
    private int _card, _page;
    public int Page => _page;
    public const int Pages = 10;
    public override void _Ready() { Layer = 13; Visible = false; Build(Vector2.Zero, false); }
    public void Open(int card) { _card = card; _page = 0; Visible = true; Refresh(); }

    private TextureButton? BtnC(int img, int pressed, float cx, float cy, Action a)
    {
        var sz = Cache.ImageNo(img).Tex?.GetSize() ?? Vector2.Zero;
        return Btn(img, pressed, cx - sz.X / 2, cy - sz.Y / 2, a);
    }

    public override void Refresh()
    {
        Clear();
        var s = Session(); if (s == null) return;
        Img(23190, 0, 0, block: true);
        var c = s.Cards[_card];
        if (c != null)
        {
            if (c.Portrait > 0 && Cache.ImageNo(c.Portrait).Tex is { } face)
            {
                var sz = face.GetSize();
                Body.AddChild(new TextureRect { Texture = face, Position = new Vector2(7 + (64 - sz.X) / 2, 36 + (72 - sz.Y) / 2), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            Txt(c.Level.ToString(), 150, 53);
            Txt(c.Name, 167, 72, center: true);
        }
        var list = s.Mails.Where(m => m.CardIndex == _card).Reverse().ToList();
        if (_page < list.Count)
        {
            var m = list[_page];
            Txt(m.Date, 130, 132, m.Incoming ? Colors.White : new Color(1, 1, 0));
            Body.AddChild(new CgText { Wrap = 30, FontColor = Colors.White, Text = m.Text, Position = new Vector2(16, 178) });
        }
        BtnC(23101, 23102, 60, 140, () => { Visible = false; Reply?.Invoke(_card); });
        BtnC(22881, 22882, 28, 410, () => { _page = (_page + Pages - 1) % Pages; Refresh(); });
        BtnC(22883, 22884, 125, 410, () => { _page = (_page + 1) % Pages; Refresh(); });
        Txt($"{_page + 1}/{Pages}", 76, 409, center: true);
        BtnC(22714, 22715, 200, 410, () => { Visible = false; Back?.Invoke(); });
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }
}

/// <summary>
/// 是/否 确认框（官方删除名片前问"确实要将「hao」从名片列表删除吗？"）：棕色边框 (2,3) 起 257×188，字从 x 51 开始，是 (82,165)、否 (183,165)。
/// </summary>
public partial class ConfirmBox : CanvasLayer
{
    public TextureCache Cache = null!;
    private Action? _yes;
    public override void _Ready() { Layer = 30; Visible = false; }

    public void Ask(string text, Action yes, Vector2? at = null)
    {
        _yes = yes;
        foreach (var c in GetChildren()) c.QueueFree();
        var win = new Control { Position = at ?? new Vector2(2, 3), Size = new Vector2(257, 188) };
        AddChild(win);
        win.AddChild(new Control { Size = win.Size, MouseFilter = Control.MouseFilterEnum.Stop });
        win.AddChild(CgFrame.Tint(win.Size));
        win.AddChild(new CgFrame { Cache = Cache, Size = win.Size, MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        win.AddChild(new CgText { Wrap = 20, FontColor = Colors.White, Text = text, Position = new Vector2(49, 60) });
        Button(win, "是", 80, () => { Visible = false; _yes?.Invoke(); });
        Button(win, "否", 181, () => Visible = false);
        Visible = true;
    }

    private static void Button(Control win, string s, float x, Action a)
    {
        var hit = new Control { Position = new Vector2(x - 12, 152), Size = new Vector2(24, 20), MouseFilter = Control.MouseFilterEnum.Stop };
        hit.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) a(); };
        win.AddChild(hit);
        win.AddChild(new CgText { FontColor = new Color(1, 1, 0), Text = s, Position = new Vector2(x - 8, 155) });
    }

    public void TestYes() { Visible = false; _yes?.Invoke(); }
}
