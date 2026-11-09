using CgPort.Client;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 家族兽窗口（服务端 BZ 种类 32，官方 exe 0x431605 画、0x422730 读内容；用 GM 脚本发 32 号窗口给官方客户端实测）：
/// 背景 exe PNG 217（272×413）在 (0,0)，关闭 22509 (246,6)。
///   （下面按钮坐标都是按钮中心）
///   名字：白字 (82,41)；族长（GI 职称号 0）点名字框 (78,40)–(230,57) 可以改，「更名」按钮 22807/22808 在 (208,74)
///         （不能改时只画灰的 22808）→ xD 按钮 0 "N|名字"。
///   说明：一页 10 行，字在 (22, 87+24k)；◀ 22762/22763 (204,336)、▶ 22764/22765 (234,336) 每次翻 10 行。
///   喂食：把背包道具放进左下格子 (38,338)–(84,384)（要有职称权限位 8，没有就提示"很抱歉，你无权给家族兽喂食。"），
///         道具图画在格子中间，出现「喂」按钮 23216/23217 (120,378) → xD 按钮 3 "I|背包格"；双击格子取回。
///   注：官方 4.06cn 把道具放进格子的那行代码写坏了（0x4318c6 把选中格子设成 −1），实际上放不进去；这里按本意做成能放。
/// </summary>
public partial class GuildBeastWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    /// <summary>手上拿着的背包格（物品窗口里点起来的道具），取走后物品窗口就不拿着了；没有返回 -1。</summary>
    public Func<int> TakeHeld = () => -1;
    public Action<string>? Notice;
    private NetworkSession.GuildBeast? _beast;
    private int _start, _feed = -1;
    private bool _editing;
    private LineEdit? _name;
    private long _lastClick;

    public NetworkSession.GuildBeast? Beast => _beast;
    public int Start => _start;
    public int FeedSlot => _feed;
    public bool Editing => _editing;

    public override void _Ready() { Layer = 13; Visible = false; Build(Vector2.Zero, false); }

    public void Show(NetworkSession.GuildBeast? b)
    {
        if (b == null) { Visible = false; return; }
        bool same = Visible && _beast?.Name == b.Name;
        _beast = b; if (!same) { _start = 0; _feed = -1; }
        _editing = false;
        Visible = true; Refresh();
    }

    public void Close() { Visible = false; _editing = false; Session()?.GuildBeastClose(); }

    public override void Refresh()
    {
        if (!Visible || _beast is not { } b) return;
        Clear(); _name = null;
        var s = Session();
        // 官方先在 (2,29)–(270,407) 压暗一层（实测背后画面 ×0.43），再贴 PNG 217（黑色当透明）
        Body.AddChild(new ColorRect { Position = new Vector2(2, 29), Size = new Vector2(268, 378), Color = new Color(0, 0, 0, 0.57f), MouseFilter = Control.MouseFilterEnum.Stop });
        ImgTex(Background(), 0, 0);
        Btn(22509, 22510, 246, 6, Close);
        bool master = s?.GuildTitleId == 0;
        if (_editing)
        {
            _name = new LineEdit { Text = b.Name, Position = new Vector2(80, 41), Size = new Vector2(150, 20), Flat = true, MaxLength = 16, ContextMenuEnabled = false };
            _name.AddThemeColorOverride("font_color", Colors.White);
            _name.AddThemeFontSizeOverride("font_size", 13);
            _name.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _name.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            _name.TextSubmitted += _ => Rename();
            Body.AddChild(_name); _name.CallDeferred(Control.MethodName.GrabFocus);
            var rb = CBtn(22807, 22808, 208, 74, Rename); if (rb != null) rb.TooltipText = "给家族兽重新命名。";
        }
        else
        {
            Txt(b.Name, 82, 47, Colors.White);   // 官方画在 (82,41)，字比我们的低 6 像素（截图量）
            if (master) { var h = Hit(78, 40, 152, 17, _ => { _editing = true; GameAudio.Se(51); Refresh(); }, box: false); h.TooltipText = "点击可编辑家族兽名字。"; }
            if (Cache.ImageNo(22808).Tex is { } dis) ImgTex(dis, 208 - dis.GetSize().X / 2, 74 - dis.GetSize().Y / 2);
        }
        for (int i = _start; i < Math.Min(b.Lines.Count, _start + 10); i++) Txt(b.Lines[i], 22, 93 + 24 * (i - _start), Colors.White);
        var prev = CBtn(22762, 22763, 204, 336, () => Page(-1)); if (prev != null) prev.TooltipText = "向前翻页。";
        var next = CBtn(22764, 22765, 234, 336, () => Page(1)); if (next != null) next.TooltipText = "向后翻页。";
        var slot = Hit(38, 338, 46, 46, Slot, box: false);
        var it = _feed >= 8 ? s?.Player.Items.FirstOrDefault(i => i.Slot == _feed) : null;
        if (_feed >= 8 && it == null) _feed = -1;
        if (it != null)
        {
            slot.TooltipText = "双击可取回此物品。";
            if (Cache.ImageNo(it.Graphic).Tex is { } t) Body.AddChild(new TextureRect { Texture = t, Position = new Vector2(61, 361) - t.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            if (it.Stackable) Txt(it.Count.ToString(), 82, 369, new Color(0.4f, 1, 1), right: true);
            var fb = CBtn(23216, 23217, 120, 378, Feed); if (fb != null) fb.TooltipText = "喂给家族兽这个物品。";
        }
        else slot.TooltipText = "可以将喂养的物品拖曳到此处。";
    }

    public void Page(int d)
    {
        int n = _beast?.Lines.Count ?? 0;
        if (d < 0 && _start >= 10) _start -= 10;
        else if (d > 0 && n > 10 && _start + 10 < n) _start += 10;
        else return;
        GameAudio.Se(51); Refresh();
    }

    private void Slot(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex != MouseButton.Left) return;
        long now = (long)Time.GetTicksMsec();
        bool dbl = mb.DoubleClick || now - _lastClick < 400; _lastClick = now;
        int held = TakeHeld();
        if (held >= 8) { PutItem(held); return; }
        if (dbl && _feed >= 0) { _feed = -1; GameAudio.Se(51); Refresh(); }
    }

    /// <summary>把背包格放进喂食格（要有权限位 8）。</summary>
    public void PutItem(int bagSlot)
    {
        var s = Session();
        if (s == null) return;
        if ((s.GuildCommission & 8) == 0) { Notice?.Invoke("很抱歉，你无权给家族兽喂食。"); return; }
        _feed = bagSlot; GameAudio.Se(79); Refresh();
    }

    public void Feed()
    {
        if (_feed < 8) return;
        GameAudio.Se(51);
        Session()?.GuildBeastFeed(_feed);
        _feed = -1;
    }

    public void Rename()
    {
        var n = (_name?.Text ?? "").Trim();
        if (n.Length == 0) { GameAudio.Se(55); return; }
        GameAudio.Se(51);
        Session()?.GuildBeastRename(n);
        _editing = false; Refresh();
    }

    /// <summary>官方这个窗口的按钮坐标是按钮中心（0x476215）。</summary>
    private TextureButton? CBtn(int img, int pressed, float cx, float cy, Action onClick)
    {
        var sz = Cache.ImageNo(img).Tex?.GetSize() ?? Vector2.Zero;
        return Btn(img, pressed, cx - sz.X / 2, cy - sz.Y / 2, onClick);
    }

    private static ImageTexture? _bg;
    private static ImageTexture? Background()
    {
        if (_bg != null || ExeArt.Png(217) is not { } t) return _bg;
        var im = t.GetImage(); im.Convert(Image.Format.Rgba8);
        for (int y = 0; y < im.GetHeight(); y++)
            for (int x = 0; x < im.GetWidth(); x++)
                if (im.GetPixel(x, y) is { R: 0, G: 0, B: 0 }) im.SetPixel(x, y, new Color(0, 0, 0, 0));
        return _bg = ImageTexture.CreateFromImage(im);
    }

    public void TestEdit(string name) { _editing = true; Refresh(); if (_name != null) _name.Text = name; }
}
