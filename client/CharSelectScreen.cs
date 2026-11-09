using CgPort.Client;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 选角色 / 选形象 / 创建角色（官方 4.06 实测，三个画面都是 640×480，这里放大 1.5 倍铺满 960×720）。
///  角色選擇 22030：两页（左 = 位置 0，右 = 位置 1），头像 (65,63)、名字居中 212、等级/体力力量强度速度魔力 右对齐 257（y 134、157+23i）、
///   地水火风条 (194, 271+21i) 宽 100、上線次數 (257,359)；按钮 登入 22031 (54,374) / 刪除 22032 (190,376)，空页 創造 22036 (187,378)；右页 +290；上一頁 22033 (247,434)。
///   删除：先"警告…一经删除将无法恢复"确定，再"真的要删除吗？"（官方要按住 Ctrl 再点「是」，手机上没有 Ctrl，问两次）→ Godf。
///  選擇角色 22200：14 个形象（exe 里的表：动画 105002+25k，头像 202800+100k），7 列 × 2 行，脚下在 (77+81c, 276+107r)，选中红框；
///   旋轉角色 ◀▶ (82/187, 71)、選擇顏色 ◀▶ (82/187, 112)（颜色 = 动画 +6、头像 +25）；登入 (120,415)、上一頁 (386,419)。
///  創造角色 22005：名字 (130,92)；头像 (55,164) 眼睛 / 嘴巴 ◀▶ (208/241, 173/200)（头像 = 底 + 25×颜色 + 5×眼 + 嘴）；
///   基本狀態剩余（大数字 22010–22019）(255,259)，体力…魔力 ◀▶ (208/241, 291+28i)、数值 (150, 291+28i)，共 30 点；
///   屬性剩余 (530,106)，地水火风 ◀▶ (536/567, 152+41i)、条 (412, 156+41i)，共 10 点，最多两种而且要相邻；登入 (357,406)–(437,456) → "可以吗？" → aSEl；上一頁 (467,406)（两个字都画在背景里）。属性在第一次登入后才生效（列表里先是 0）。
/// </summary>
public partial class CharSelectScreen : CanvasLayer
{
    public TextureCache Cache = null!;
    public NetworkSession Session = null!;
    public Action? Back;
    private Control _root = null!;
    private ConfirmBox _confirm = null!;
    public int Page { get; private set; }   // 0 选角色 1 选形象 2 创建
    private int _pos, _pick, _color, _dir = 4, _eye, _mouth;
    private readonly int[] _stats = new int[5], _elem = new int[4];
    private LineEdit? _name;
    public string Hint { get; private set; } = "";

    /// <summary>两套形象（exe 0x483200 的表，每条 6 个数：动画、?、?、头像、小头像、?）：右上「Change」（这个私服客户端加的）在两套之间切换（官方实测）。
    /// 第 2 套 = 106002 起（hao / bob 就是这一套），头像 200000 起；颜色、眼睛、嘴巴规则同第 1 套（实测创建 "aSEl 1 settwo 106002 200010 …"：眼睛 +2 = 头像 +10）。</summary>
    public static readonly int[][] AnimeSets =
    {
        new[] { 105002, 105027, 105052, 105077, 105102, 105127, 105152, 105252, 105277, 105302, 105327, 105352, 105377, 105402 },
        new[] { 106002, 106027, 106052, 106077, 106102, 106127, 106152, 106252, 106277, 106302, 106327, 106352, 106377, 106402 },
    };
    public static readonly int[][] FaceSets =
    {
        new[] { 202800, 202900, 203000, 203100, 203200, 203300, 203400, 203500, 203600, 203700, 203800, 203900, 204000, 204100 },
        new[] { 200000, 200200, 200800, 201000, 201200, 201400, 201600, 200400, 200600, 201800, 202000, 202200, 202400, 202600 },
    };
    private int _set;
    public int Set => _set;
    private int[] Anime => AnimeSets[_set];
    private int[] FaceBase => FaceSets[_set];
    private static readonly Color Ink = new(0.15f, 0.1f, 0.05f);

    public override void _Ready()
    {
        Layer = 45;
        AddChild(new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Stop });
        _root = new Control { Scale = new Vector2(1.5f, 1.5f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _confirm = new ConfirmBox { Cache = Cache };
        AddChild(_confirm); _confirm.Layer = 50;
        Session.CharListReceived += _ => { Page = 0; Visible = true; Refresh(); };
        Session.CharCreateFailed += m => { Hint = "创建失败：" + m; Refresh(); };
        Refresh();
    }

    public void Refresh()
    {
        foreach (var c in _root.GetChildren()) c.QueueFree();
        _name = null;
        switch (Page)
        {
            case 0: DrawList(); break;
            case 1: DrawPick(); break;
            default: DrawCreate(); break;
        }
    }

    // ———— 角色選擇 ————
    private void DrawList()
    {
        Img(22030, 0, 0);
        for (int pos = 0; pos < 2; pos++)
        {
            float dx = 290 * pos; int p = pos;
            var c = Session.CharSlots.ElementAtOrDefault(pos);
            if (c == null) { Btn(22036, 22037, 187 + dx, 378, () => { _pos = p; _pick = 0; _color = 0; Page = 1; Refresh(); }); continue; }
            if (c.Face > 0) Img(c.Face, 65 + dx, 63);
            Txt(c.Name, 212 + dx, 76, Ink, center: true);
            Txt(c.Level.ToString(), 257 + dx, 131, Ink, right: true);
            for (int i = 0; i < 5; i++) Txt(c.Stats[i].ToString(), 257 + dx, 154 + 23 * i, Ink, right: true);
            Color[] col = { new(0, 0.85f, 0), new(0.2f, 0.4f, 1), new(1, 0, 0), new(1, 1, 0) };
            for (int e = 0; e < 4; e++)
                if (c.Elements[e] > 0) _root.AddChild(new ColorRect { Color = col[e], Position = new Vector2(194 + dx, 271 + 21.3f * e), Size = new Vector2(c.Elements[e], 7), MouseFilter = Control.MouseFilterEnum.Ignore });
            Txt(c.Logins.ToString(), 257 + dx, 356, Ink, right: true);
            Btn(22031, 22034, 54 + dx, 374, () => { Visible = false; Session.EnterChar(p); });
            Btn(22032, 22035, 190 + dx, 376, () => AskDelete(p));
        }
        Btn(22033, 22033, 247, 434, () => Back?.Invoke());
        if (Hint.Length > 0) Txt(Hint, 320, 466, Colors.White, center: true);
    }

    public void AskDelete(int pos)
    {
        var c = Session.CharSlots.ElementAtOrDefault(pos); if (c == null) return;
        _confirm.Ask("*** 警告 ***\n所选择的人物一经删除将无法恢复，你确定要删除吗？", () =>
            _confirm.Ask($"真的要删除「{c.Name}」吗？", () => Session.DeleteChar(pos), new Vector2(352, 266)), new Vector2(352, 266));
    }

    // ———— 選擇角色 ————
    private void DrawPick()
    {
        Img(22200, 0, 0);
        for (int i = 0; i < Anime.Length; i++)
        {
            int c = i % 7, r = i / 7; var foot = new Vector2(77 + 81 * c, 276 + 107 * r);
            int k = i;
            _root.AddChild(new AnimSprite { Cache = Cache, AnimeId = Anime[i] + (i == _pick ? 6 * _color : 0), Dir = i == _pick ? _dir : 4, Act = 0, FallbackAct = 0, Position = foot });
            if (i == _pick) _root.AddChild(new ReferenceRect { BorderColor = new Color(1, 0, 0), BorderWidth = 2, EditorOnly = false, Position = foot - new Vector2(29, 98), Size = new Vector2(58, 105), MouseFilter = Control.MouseFilterEnum.Ignore });
            Hit(foot.X - 29, foot.Y - 98, 58, 105, () => { _pick = k; _color = 0; Refresh(); });
        }
        Hit(82, 60, 30, 24, () => { _dir = (_dir + 7) % 8; Refresh(); });
        Hit(187, 60, 30, 24, () => { _dir = (_dir + 1) % 8; Refresh(); });
        Hit(82, 101, 30, 24, () => { _color = (_color + 3) % 4; Refresh(); });
        Hit(187, 101, 30, 24, () => { _color = (_color + 1) % 4; Refresh(); });
        Hit(120, 415, 88, 52, () => { _eye = _mouth = 0; Array.Clear(_stats); Array.Clear(_elem); Hint = ""; Page = 2; Refresh(); });
        Hit(386, 419, 144, 52, () => { Page = 0; Refresh(); });
        // 右上「Change」：换另一套形象（图档里没找到这张按钮图，照官方的样子画：浅青灰底板 + 深青斜体字）
        var chg = new Panel { Position = new Vector2(533, 9), Size = new Vector2(94, 28), MouseFilter = Control.MouseFilterEnum.Stop };
        chg.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.78f, 0.82f, 0.74f), BorderColor = new Color(0.35f, 0.3f, 0.22f), BorderWidthBottom = 2, BorderWidthRight = 2, BorderWidthLeft = 1, BorderWidthTop = 1, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 });
        var lbl = new Label { Text = "Change", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Size = chg.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        lbl.AddThemeFontSizeOverride("font_size", 19); lbl.AddThemeColorOverride("font_color", new Color(0.1f, 0.33f, 0.36f));
        lbl.AddThemeConstantOverride("outline_size", 3); lbl.AddThemeColorOverride("font_outline_color", new Color(0.95f, 0.85f, 0.55f));
        chg.AddChild(lbl);
        chg.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ToggleSet(); };
        _root.AddChild(chg);
    }

    /// <summary>「Change」：换一套形象，选中的位置和颜色清零。</summary>
    public void ToggleSet() { _set ^= 1; _pick = 0; _color = 0; Refresh(); }

    // ———— 創造角色 ————
    public int Face => FaceBase[_pick] + 25 * _color + 5 * _eye + _mouth;
    public int Body => Anime[_pick] + 6 * _color;

    private void DrawCreate()
    {
        Img(22005, 0, 0);
        _name = new LineEdit { Position = new Vector2(126, 82), Size = new Vector2(130, 20), MaxLength = 16, Text = _nameText, PlaceholderText = "名字" };
        _name.AddThemeFontSizeOverride("font_size", 12);
        _name.TextChanged += t => _nameText = t;
        _root.AddChild(_name);
        Img(Face, 55, 164);
        Arrows(173, () => { _eye = (_eye + 4) % 5; }, () => { _eye = (_eye + 1) % 5; });
        Arrows(200, () => { _mouth = (_mouth + 4) % 5; }, () => { _mouth = (_mouth + 1) % 5; });
        BigNum(30 - _stats.Sum(), 255, 259);
        for (int i = 0; i < 5; i++)
        {
            int k = i;
            BigNum(_stats[i], 150, 291 + 28 * i, left: true);
            Arrows(291 + 28 * i, () => { if (_stats[k] > 0) _stats[k]--; }, () => { if (_stats.Sum() < 30 && _stats[k] < 15) _stats[k]++; });
        }
        BigNum(10 - _elem.Sum(), 530, 106);
        Color[] col = { new(0, 0.9f, 0), new(0.2f, 0.85f, 1), new(1, 0.1f, 0.1f), new(1, 1, 0) };
        for (int e = 0; e < 4; e++)
        {
            int k = e; float y = 152 + 41 * e;
            if (_elem[e] > 0) _root.AddChild(new ColorRect { Color = col[e], Position = new Vector2(412, y + 4), Size = new Vector2(11.2f * _elem[e], 14), MouseFilter = Control.MouseFilterEnum.Ignore });
            Btn(22008, 22008, 536, y, () => { if (_elem[k] > 0) _elem[k]--; Refresh(); });
            Btn(22009, 22009, 567, y, () => { if (CanAddElement(k)) _elem[k]++; Refresh(); });
        }
        Hit(357, 406, 80, 50, TryCreate);                              // 背景里就画着「登入」「上一頁」
        Hit(467, 406, 120, 50, () => { Page = 1; Refresh(); });
        Txt(Hint.Length > 0 ? Hint : "进行名字、面部及人物属性设置。", 8, 465, Colors.White);
    }

    private string _nameText = "";

    /// <summary>属性最多两种，而且要相邻（地-水-火-风-地），总共 10 点。</summary>
    public bool CanAddElement(int k)
    {
        if (_elem.Sum() >= 10) return false;
        for (int o = 0; o < 4; o++)
            if (o != k && _elem[o] > 0 && (o + 2) % 4 == k) return false;   // 对面的属性
        int others = Enumerable.Range(0, 4).Count(o => o != k && _elem[o] > 0);
        return others < 2 || _elem[k] > 0;
    }

    public void TryCreate()
    {
        var name = _nameText.Trim();
        if (name.Length == 0) { Hint = "请输入名字。"; Refresh(); return; }
        if (_stats.Sum() < 30) { Hint = "基本状态的点数还没分配完。"; Refresh(); return; }
        if (_elem.Sum() < 10) { Hint = "属性的点数还没分配完。"; Refresh(); return; }
        _confirm.Ask("可以吗？", () => { Hint = ""; Session.CreateChar(_pos, name, Body, Face, _stats.ToArray(), _elem.ToArray()); Page = 0; }, new Vector2(352, 266));
    }

    // ———— 小工具 ————
    private void Arrows(float y, Action left, Action right)
    {
        Btn(22008, 22008, 208, y, () => { left(); Refresh(); });
        Btn(22009, 22009, 241, y, () => { right(); Refresh(); });
    }

    /// <summary>大数字 22010–22019（0–9）。left：从 x 往右排，否则右对齐到 x+16。</summary>
    private void BigNum(int v, float x, float y, bool left = false)
    {
        var s = v.ToString();
        var texs = s.Select(ch => Cache.ImageNo(22010 + (ch - '0')).Tex).ToList();
        float w = texs.Sum(t => t?.GetSize().X ?? 12);
        float cx = left ? x : x + 16 - w;
        foreach (var t in texs) { if (t == null) continue; _root.AddChild(new TextureRect { Texture = t, Position = new Vector2(cx, y), MouseFilter = Control.MouseFilterEnum.Ignore }); cx += t.GetSize().X; }
    }

    private void Img(int id, float x, float y)
    {
        if (Cache.ImageNo(id).Tex is { } t) _root.AddChild(new TextureRect { Texture = t, Position = new Vector2(x, y), MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    private void Btn(int img, int hover, float x, float y, Action a)
    {
        if (Cache.ImageNo(img).Tex is not { } t) { Hit(x, y, 80, 40, a); return; }
        var b = new TextureButton { TextureNormal = t, TextureHover = Cache.ImageNo(hover).Tex ?? t, TexturePressed = Cache.ImageNo(hover).Tex ?? t, Position = new Vector2(x, y) };
        b.Pressed += a; _root.AddChild(b);
    }

    private void Hit(float x, float y, float w, float h, Action a)
    {
        var c = new Control { Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Stop };
        c.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || e is InputEventScreenTouch { Pressed: true }) a(); };
        _root.AddChild(c);
    }

    private void Txt(string s, float x, float y, Color c, bool right = false, bool center = false)
    {
        var t = new CgText { Text = s, FontColor = c, Wrap = 999 };
        float w = s.Sum(CgText.Cells) * CgText.CellW;
        t.Position = new Vector2(right ? x - w : center ? x - w / 2 : x, y);
        _root.AddChild(t);
    }

    // 测试用
    public void TestPick(int k, int color) { _pick = k; _color = color; Page = 1; Refresh(); }
    public void TestCreatePage(int pos) { _pos = pos; Page = 2; Refresh(); }
    public void TestFill(string name, int[] stats, int[] elem, int eye = 0, int mouth = 0) { _eye = eye; _mouth = mouth; _nameText = name; stats.CopyTo(_stats, 0); elem.CopyTo(_elem, 0); Refresh(); }
    public void TestConfirm() => _confirm.TestYes();
}
