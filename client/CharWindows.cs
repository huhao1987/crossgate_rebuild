using CgPort.Core;
using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>官方窗口的共用小工具：图片、格子字、命中区。坐标都是相对窗口左上角（960×720 画面）。</summary>
public abstract partial class CgWindow : CanvasLayer
{
    public TextureCache Cache = null!;
    protected Control Win = null!;
    protected Control Body = null!;

    protected void Build(Vector2 pos, bool anchorRight)
    {
        Win = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (anchorRight) { Win.AnchorLeft = Win.AnchorRight = 1; Win.OffsetLeft = pos.X - 960; Win.OffsetTop = pos.Y; }
        else Win.Position = pos;
        AddChild(Win);
        Body = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        Win.AddChild(Body);
    }

    protected void Clear() { foreach (var c in Body.GetChildren()) c.QueueFree(); }

    protected TextureRect? Img(int id, float x, float y, bool block = false)
    {
        if (Cache.ImageNo(id).Tex is not { } t) return null;
        var r = new TextureRect { Texture = t, Position = new Vector2(x, y), MouseFilter = block ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore };
        Body.AddChild(r); return r;
    }

    /// <summary>格子字。right = 右边缘对齐到 x；center = 以 x 为中心。y = 数字的字顶（官方截图量的；数字比原点低 5 像素，汉字低 2 像素）。</summary>
    protected CgText Txt(string s, float x, float y, Color? c = null, bool right = false, bool center = false)
    {
        var t = new CgText { Wrap = 999, FontColor = c ?? Colors.White };
        t.Text = s;
        float w = s.Sum(CgText.Cells) * CgText.CellW;
        t.Position = new Vector2(right ? x - w : center ? x - w / 2 : x, y - 5);
        Body.AddChild(t); return t;
    }

    protected TextureButton? Btn(int img, int pressed, float x, float y, Action onClick)
    {
        if (Cache.ImageNo(img).Tex is not { } t) return null;
        var p = Cache.ImageNo(pressed).Tex ?? t;
        var b = new TextureButton { TextureNormal = t, TextureHover = t, TexturePressed = p, Position = new Vector2(x, y) };
        b.Pressed += onClick; Body.AddChild(b); return b;
    }

    protected Control Hit(float x, float y, float w, float h, Action<InputEventMouseButton> onClick, bool box = true)
    {
        var c = new Control { Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Stop };
        if (box)
        {
            var g = new GreenBox { Size = c.Size, Visible = false }; c.AddChild(g);
            c.MouseEntered += () => g.Visible = true; c.MouseExited += () => g.Visible = false;
        }
        c.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true } mb) onClick(mb); };
        Body.AddChild(c); return c;
    }

    public void Toggle() { Visible = !Visible; if (Visible) Refresh(); }
    public abstract void Refresh();
}

/// <summary>
/// 状态窗口（底部菜单"状态"）：背景 22501 状态 / 22515 明细 / 22525 称号（三个标签就是三张背景），在 (1,1)。
/// 明细里有升级加点：22516"增加点数"在 (148,60)，22517/22518 "+1" 在 (225, 81+23i)，点了发 IHw（官方实测）。
/// 关闭 22509/22510 在 (247,7)。数值位置对照官方截图量的（字顶 / 右对齐边）。
/// </summary>
public partial class StatusPanel : CgWindow
{
    public Func<PlayerState> Player = () => new PlayerState();
    public Action<int>? AddPoint;
    public Func<NetworkSession?> Session = () => null;
    public Action<string, Action>? Ask;   // 确认框（删除称号）
    private int _tab;   // 0 状态 1 明细 2 称号
    private int _titlePage, _titleSel = -1;
    private LineEdit? _freeEdit;

    public override void _Ready()
    {
        Layer = 16;
        Build(new Vector2(1, 1), false);
        Visible = false;
    }

    public void ShowTab(int tab) { _tab = tab; Visible = true; Refresh(); }
    /// <summary>测试用：点第 i 个 "+1"（走按钮同一路径）。</summary>
    public void TestPlus(int i) => Body.GetChildren().OfType<TextureButton>().Where(b => b.Position.X == 225).ElementAtOrDefault(i)?.EmitSignal(BaseButton.SignalName.Pressed);
    public int Tab => _tab;

    private void RowButton(string text, float x, bool on, Action a)
    {
        Body.AddChild(new ColorRect { Color = on ? Color.Color8(250, 210, 120) : Color.Color8(120, 70, 40), Position = new Vector2(x, 377), Size = new Vector2(38, 21), MouseFilter = Control.MouseFilterEnum.Ignore });
        Body.AddChild(new ColorRect { Color = Color.Color8(190, 120, 70), Position = new Vector2(x + 2, 379), Size = new Vector2(34, 17), MouseFilter = Control.MouseFilterEnum.Ignore });
        Txt(text, x + 19, 384, Color.Color8(255, 190, 200), center: true);
        Hit(x, 377, 38, 21, _ => a());
    }

    public override void Refresh()
    {
        if (!Visible) return;
        Clear();
        var p = Player(); var s = p.Stats;
        Img(_tab switch { 1 => 22515, 2 => 22980, _ => 22501 }, 0, 0, block: true);   // 称号页是 22980（官方实测；22525 不是）
        Btn(22509, 22510, 247, 7, () => Visible = false);
        // 标签：窗口底部三格（状态 / 明细 / 称号）
        for (int i = 0; i < 3; i++) { int k = i; Hit(6 + i * 89, 408, 84, 24, _ => { _tab = k; Refresh(); }, box: false); }
        Txt(p.Name, 115, 7, center: true);
        var white = Colors.White;
        if (_tab == 0)
        {
            Txt(p.Job, 197, 57, center: true);
            if (Session() is { } ns0) { Txt(ns0.SpecialTitle, 197, 80, center: true); Txt(ns0.FreeTitle, 197, 103, center: true); }   // 特殊稱號 / 玩家稱號
            Txt(p.Level.ToString(), 85, 126, right: true);
            Txt(p.Exp.ToString(), 125, 150, right: true);
            Txt(s.NextExp.ToString(), 125, 172, right: true);
            Txt($"{p.Hp,4}/{p.MaxHp,4}".TrimStart(), 125, 195, right: true);
            Txt($"{p.Mp,4}/{p.MaxMp,4}".TrimStart(), 125, 218, right: true);
            int[] v = { s.Attack, s.Defense, s.Agility, s.Spirit, s.Recovery, s.Charm };
            for (int i = 0; i < v.Length; i++) Txt(v[i].ToString(), 86, 241 + 23 * i, right: true);
            Txt(s.Record.ToString(), 255, 150, right: true);
            Img(22726, 191, 169);   // 种族：官方是图片（22726 人形系 … 22735 邪魔系）
            Txt(s.Endurance.ToString(), 212, 310, right: true);
            Txt(s.Dex.ToString(), 212, 333, right: true);
            Txt(s.Int.ToString(), 212, 356, right: true);
            // 属性条：地 绿、水 蓝、火 红、风 黄；实测 50 = x 185–219（35 像素）、高 8，四条 y = 216/236/256/276
            Color[] col = { new(0, 0.8f, 0), new(0.2f, 0.4f, 1), new(1, 0, 0), new(1, 1, 0) };
            for (int e = 0; e < 4; e++)
                if (s.Element[e] > 0) Body.AddChild(new ColorRect { Color = col[e], Position = new Vector2(185, 216 + 20 * e), Size = new Vector2(70 * Math.Min(100, s.Element[e]) / 100f, 8), MouseFilter = Control.MouseFilterEnum.Ignore });
            // 戰鬥位置 前排 / 後排（官方实测：点另一个 = ih，服务端回 Ov 0 前排 / 1 后排；按钮图档里没有，照截图画）
            if (Session() is { } nsr)
            {
                RowButton("前排", 150, !nsr.BackRow, () => { if (nsr.BackRow) nsr.ToggleBattleRow(); });
                RowButton("後排", 190, nsr.BackRow, () => { if (!nsr.BackRow) nsr.ToggleBattleRow(); });
            }
        }
        else if (_tab == 1)
        {
            Txt(p.Level.ToString(), 79, 42, right: true);
            Txt($"{p.Hp,4}/{p.MaxHp,4}".TrimStart(), 123, 64, right: true);
            Txt($"{p.Mp,4}/{p.MaxMp,4}".TrimStart(), 123, 87, right: true);
            int[] v = { s.Attack, s.Defense, s.Agility, s.Spirit, s.Recovery, s.Charm };
            for (int i = 0; i < v.Length; i++) Txt(v[i].ToString(), 92, 110 + 23 * i, right: true);
            Img(22516, 148, 60);
            Txt(p.StatPoints.ToString(), 252, 64, right: true);
            int[] b = { s.Vit, s.Str, s.Tgh, s.Spd, s.Mag };
            for (int i = 0; i < 5; i++)
            {
                Txt(b[i].ToString(), 219, 84 + 23 * i, right: true);
                int k = i;
                if (p.StatPoints > 0) Btn(22517, 22518, 225, 81 + 23 * i, () => AddPoint?.Invoke(k));
            }
            Txt(s.Endurance.ToString(), 200, 205, right: true);
            Txt(s.Dex.ToString(), 200, 228, right: true);
            Txt(s.Int.ToString(), 200, 251, right: true);
            for (int r = 0; r < 6; r++) Txt($"{s.Resist[r]:+0;-0;+0}", 90, 272 + 23 * r, right: true);
            int[] m = { s.Critical, s.Counter, s.Hit, s.Dodge };
            for (int i = 0; i < 4; i++) Txt($"{m[i]:+0;-0;+0}", 222, 302 + 23 * i, right: true);
        }
        else TitlePage();
        if (_tab != 2 && _freeEdit != null) { _freeEdit.QueueFree(); _freeEdit = null; }
    }

    /// <summary>
    /// 称号页（官方 bob 实测）：背景 22980。玩家稱號：输入框 + 設定 22981 (198,57) → ACwB；
    /// 特別稱號：8 行（x 40 宽 189，行顶 131+30i），点一行 = 选为特殊称号（zGp 序号，青色框）；◀ 22881 (126,99) ▶ 22883 (225,99) 翻页；
    /// 刪除 22985 (35,379) → tj 序号；解除 22983 (160,379) → zGp -1。
    /// </summary>
    private void TitlePage()
    {
        var ns = Session();
        var titles = ns?.Titles ?? Array.Empty<string>();
        int pages = Math.Max(1, (titles.Count + 7) / 8);
        _titlePage = Math.Clamp(_titlePage, 0, pages - 1);
        if (_freeEdit == null)
        {
            _freeEdit = new LineEdit { Position = new Vector2(8, 61), Size = new Vector2(186, 20), Flat = true, MaxLength = 16, Text = ns?.FreeTitle ?? "" };
            _freeEdit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _freeEdit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            _freeEdit.AddThemeFontSizeOverride("font_size", 12);
            Win.AddChild(_freeEdit);
        }
        Btn(22981, 22982, 198, 57, () => { if (_freeEdit != null) ns?.SetFreeTitle(_freeEdit.Text.Trim()); });
        Btn(22881, 22882, 126, 99, () => { _titlePage = (_titlePage + pages - 1) % pages; Refresh(); });
        Btn(22883, 22884, 225, 99, () => { _titlePage = (_titlePage + 1) % pages; Refresh(); });
        Txt($"{_titlePage + 1}/ {pages}", 190, 110, center: true);
        for (int k = 0; k < 8; k++)
        {
            int i = _titlePage * 8 + k; if (i >= titles.Count) break;
            float y = 131 + 30 * k;
            bool on = i == (_titleSel >= 0 ? _titleSel : ns?.TitleIndex ?? -1);
            if (on) Body.AddChild(new CyanBox { Position = new Vector2(40, y), Size = new Vector2(189, 22) });
            Txt(titles[i], 42, y + 11);
            int idx = i;
            Hit(40, y, 189, 22, _ => { _titleSel = idx; ns?.SelectTitle(idx); Refresh(); }, box: false);
        }
        Btn(22985, 22986, 35, 379, () =>
        {
            int sel = _titleSel >= 0 ? _titleSel : ns?.TitleIndex ?? -1;
            if (ns == null || sel < 0 || sel >= titles.Count) return;
            void Del() { ns.DeleteTitle(sel); _titleSel = -1; }
            if (Ask != null) Ask($"确实要删除称号「{titles[sel]}」吗？", Del); else Del();
        });
        Btn(22983, 22984, 160, 379, () => { _titleSel = -1; ns?.SelectTitle(-1); Refresh(); });
    }

    public void TestTitle(int i) { _titleSel = i; Session()?.SelectTitle(i); Refresh(); }
    public void TestClearTitle() { _titleSel = -1; Session()?.SelectTitle(-1); Refresh(); }
    public void TestFreeTitle(string t) { if (_freeEdit != null) _freeEdit.Text = t; Session()?.SetFreeTitle(t); }
}

/// <summary>
/// 物品窗口（底部菜单"物品"）：背景 22540，贴右边在 (689,1)。装备栏 8 格（头/身体/双手/足/两个饰品/水晶），背包 20 格（5×4）。
/// 操作同官方（实测）：双击 = 用 / 穿上（Ak）；单击 = 拿起，再单击另一格 = 放下（yi，装备栏 ↔ 背包就是穿脱）；右键取消。
/// </summary>
public partial class ItemPanel : CgWindow
{
    public Func<PlayerState> Player = () => new PlayerState();
    /// <summary>人物形象（中间那格画站着的自己，官方同）。</summary>
    public Func<int> PlayerAnime = () => 0;
    public Action<int>? Use;
    public Action<int, int>? Move;
    public Action? Wallet, SortBag;
    /// <summary>手上拿着道具时点地图 = 扔掉（官方 DI）：由 Main 调这个取走手上的格子。</summary>
    public int TakeHeld() { int h = _held; Drop(); return h; }
    private int _held = -1;            // 拿在手上的格子
    private TextureRect? _cursor;      // 跟着鼠标的图标
    private long _lastClick; private int _lastSlot = -1;

    // 装备格中心（相对窗口）：0 头 1 身体 2 右手 3 左手 4 足 5 饰品 6 饰品 7 水晶（官方截图量）
    private static readonly Vector2[] EquipPos =
    {
        new(92, 61), new(36, 171), new(36, 118), new(149, 118), new(149, 171), new(36, 61), new(149, 61), new(224, 166),
    };
    private static Vector2 BagPos(int i) => new(32 + 52 * (i % 5), 239 + 52 * (i / 5));

    public override void _Ready()
    {
        Layer = 16;
        Build(new Vector2(689, 1), true);
        _cursor = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, ZIndex = 10 };
        AddChild(_cursor);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_cursor is { Visible: true }) _cursor.Position = GetViewport().GetMousePosition() - _cursor.Size / 2;
    }

    public override void Refresh()
    {
        if (!Visible) return;
        Clear();
        var p = Player();
        Img(22540, 0, 0, block: true);
        Btn(22509, 22510, 246, 6, () => { Drop(); Visible = false; });
        Txt(p.Gold.ToString(), 259, 76, right: true);
        Btn(22545, 22546, 194, 93, () => { Drop(); Wallet?.Invoke(); });   // 钱包 → 金幣窗口（扔钱）
        if (SortBag != null)   // 「整理」：这个私服客户端加的按钮（官方 4.06 没有），发 CUSTOMXB 0
        {
            var sort = new SortButton { Position = new Vector2(182, 6), Size = new Vector2(54, 18) };
            sort.Pressed += () => { Drop(); SortBag(); };
            Body.AddChild(sort);
        }
        // 中间那格：站着的自己，面朝下（脚在格子底部往上 10 像素）
        if (PlayerAnime() is int an and > 0)
        {
            var holder = new Node2D { Position = new Vector2(92, 160) };
            holder.AddChild(new AnimSprite { Cache = Cache, AnimeId = an, Act = 0, FallbackAct = 0, Dir = 4 });
            Body.AddChild(holder);
        }
        var bySlot = p.Equipment.Concat(p.Items).Where(i => i.Slot >= 0).ToDictionary(i => i.Slot);
        for (int e = 0; e < 8; e++) Cell(e, EquipPos[e], bySlot.GetValueOrDefault(e));
        for (int i = 0; i < 20; i++) Cell(8 + i, BagPos(i), bySlot.GetValueOrDefault(8 + i));

    }

    private void Cell(int slot, Vector2 c, ItemStack? it)
    {
        if (it != null && it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } t)
        {
            var sz = t.GetSize();
            Body.AddChild(new TextureRect { Texture = t, Position = c - sz / 2, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = slot == _held ? new Color(1, 1, 1, 0.35f) : Colors.White });
            if (it.Count > 1) Txt(it.Count.ToString(), c.X + 22, c.Y + 9, new Color(0.4f, 1, 1), right: true);
        }
        var hit = Hit(c.X - 24, c.Y - 24, 48, 48, mb => Click(slot, it, mb));
        if (it != null) { hit.MouseEntered += () => ShowTip(it); hit.MouseExited += HideTip; }
    }

    private void Click(int slot, ItemStack? it, InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.Right) { Drop(); return; }
        if (mb.ButtonIndex != MouseButton.Left) return;
        long now = (long)Time.GetTicksMsec();
        bool dbl = mb.DoubleClick || (slot == _lastSlot && now - _lastClick < 500);
        _lastClick = now; _lastSlot = slot;
        if (dbl && it != null) { Drop(); Use?.Invoke(slot); return; }
        if (_held < 0) { if (it != null) Pick(slot, it); return; }
        if (_held != slot) Move?.Invoke(_held, slot);
        Drop();
    }

    // ———— 道具说明（官方实测：鼠标停在道具上，物品窗口上半部盖一个 22541 框，在窗口 (0,1)）————
    // 标题 = 道具名，橙色，居中在 x 133、字顶 10；说明从 (10, 37) 起每行 18；$0 白 $1 青 $4 黄（同聊天颜色）；框里放不下的行（后面的介绍文字）不显示。
    private Control? _tip;
    public string TipText { get; private set; } = "";
    private void ShowTip(ItemStack it)
    {
        HideTip();
        if (Cache.ImageNo(22541).Tex is not { } bg) return;
        _tip = new Control { Position = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddChild(new TextureRect { Texture = bg, MouseFilter = Control.MouseFilterEnum.Ignore });
        float tw = it.Name.Sum(CgText.Cells) * CgText.CellW;
        _tip.AddChild(new CgText { Text = it.Name, FontColor = new Color(1, 0.6f, 0), Position = new Vector2(133 - tw / 2, 10 - 2) });
        var lines = DescLines(it.Desc);
        TipText = it.Name + "\n" + string.Join("\n", lines.Select(l => string.Concat(l.Select(seg => seg.Text))));
        for (int l = 0; l < lines.Count && 37 + 18 * l + 14 < 184; l++)
        {
            float x = 10;
            foreach (var (text, color) in lines[l])
            {
                _tip.AddChild(new CgText { Text = text, Wrap = 999, FontColor = color, Position = new Vector2(x, 37 + 18 * l - 2) });
                x += text.Sum(CgText.Cells) * CgText.CellW;
            }
        }
        Win.AddChild(_tip);
    }
    private void HideTip() { _tip?.QueueFree(); _tip = null; }

    /// <summary>服务端道具说明 → 行 → (文字, 颜色) 段。转义：\S 空格、\\n 换行、\\c 逗号；$数字 换颜色。</summary>
    public static List<List<(string Text, Color Color)>> DescLines(string raw)
    {
        var res = new List<List<(string, Color)>>();
        var color = ChatLog.Palette[0];
        foreach (var line in raw.Replace("\\S", " ").Replace("\\\\c", ",").Replace("\\c", ",").Split("\\\\n"))
        {
            var segs = new List<(string, Color)>(); var sb = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '$' && i + 1 < line.Length && char.IsDigit(line[i + 1]))
                {
                    if (sb.Length > 0) { segs.Add((sb.ToString(), color)); sb.Clear(); }
                    int n = line[i + 1] - '0'; color = n < ChatLog.Palette.Length ? ChatLog.Palette[n] : Colors.White; i++;
                    continue;
                }
                sb.Append(line[i]);
            }
            if (sb.Length > 0) segs.Add((sb.ToString(), color));
            res.Add(segs);
        }
        while (res.Count > 0 && res[^1].Count == 0) res.RemoveAt(res.Count - 1);
        return res;
    }

    /// <summary>测试用：显示某格的说明。</summary>
    public void TestHover(int slot) { var p = Player(); if (p.Equipment.Concat(p.Items).FirstOrDefault(i => i.Slot == slot) is { } it) ShowTip(it); }

    private void Pick(int slot, ItemStack it)
    {
        _held = slot;
        if (_cursor != null && Cache.ImageNo(it.Graphic).Tex is { } t) { _cursor.Texture = t; _cursor.Size = t.GetSize(); _cursor.Visible = true; }
        Refresh();
    }

    private void Drop()
    {
        _held = -1;
        if (_cursor != null) _cursor.Visible = false;
        if (Visible) Refresh();
    }

    /// <summary>测试用：模拟点击 / 双击某格。</summary>
    public void TestClick(int slot, bool dbl)
    {
        var p = Player();
        var it = p.Equipment.Concat(p.Items).FirstOrDefault(i => i.Slot == slot);
        if (dbl) { _lastSlot = slot; _lastClick = (long)Time.GetTicksMsec(); }
        Click(slot, it, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
    }
    public int Held => _held;
}

/// <summary>
/// 系统窗口（底部菜单"系统"，官方实测）：背景 22500 在 (1,1)，关闭 (167,7)；7 项字顶 64+27i，左 47。
/// 登出 = 发 CharLogout 回设置界面；聊天设定 = 聊天栏显示行数 2→4→6；其余还没做。
/// </summary>
public partial class SystemPanel : CgWindow
{
    public static readonly string[] Items = { "登      出", "聊 天 设 定", "声 音 设 定", "鼠 标 设 定", "键 盘 热 键", "绘 图 设 定", "战 斗 录 像", "高 级 设 定" };
    public Action<int>? Chosen;
    /// <summary>当前页：-1 菜单，2 音樂（22961），3 滑鼠（22963），4 鍵盤（22964），5 繪圖設定（22965）。</summary>
    public int Page { get; private set; } = -1;
    public GameConfig Cfg = null!;
    public Action? DrawChanged;   // 绘图设定改了（主程序重画周围对象）
    public Action<string>? Toast;

    public override void _Ready()
    {
        Layer = 16;
        Build(new Vector2(1, 1), false);
        Visible = false;
        VisibilityChanged += () => { if (Visible) { Page = -1; Refresh(); } };
    }

    public void Open(int page) { Page = page; Refresh(); }

    public override void Refresh()
    {
        if (!Visible) return;
        Clear();
        var yellow = new Color(1, 1, 0);
        switch (Page)
        {
            case 2:   // 音樂：背景音乐 / 音效 0–15（官方默认 8），↑↓ 调
                Img(22961, 0, 0, block: true);
                Txt("背景音乐音量", 14, 48, yellow);
                Level(73, () => Cfg.BgmVolume, v => Cfg.BgmVolume = v);
                Txt("音效音量", 14, 98, yellow);
                Level(122, () => Cfg.SeVolume, v => Cfg.SeVolume = v);
                break;
            case 3:   // 滑鼠：设定物品的移动（手机上是点按，固定）
                Img(22963, 0, 0, block: true);
                Txt("设定物品的移动", 16, 48, yellow);
                Txt("现在", 16, 73);
                Txt("鼠标左键", 38, 100);
                Txt("<变更>", 126, 124);
                Hit(124, 120, 50, 18, _ => Toast?.Invoke("手机上用点按，不能改"));
                break;
            case 4:   // 鍵盤：热键一览
                Img(22964, 0, 0, block: true);
                string[] keys = { "冒险者日志(F9)", "人物名称显示(F11)", "血条显示(Ctrl+F11)", "截取屏幕(F12)", "窗口大小切换(Ctrl+F12)", "表情输入框开关(TAB)", "清除屏幕聊天(HOME)", "老板键(F10)" };
                for (int i = 0; i < keys.Length; i++) Txt(keys[i], 16, 50 + 26 * i, yellow);
                break;
            case 5:   // 繪圖設定
                Img(22965, 0, 0, block: true);
                Option(0, "隐藏画面角色", Cfg.HidePlayers ? "隐藏" : "不隐藏", () => { Cfg.HidePlayers = !Cfg.HidePlayers; GameConfig.Patch(("hide_players", Cfg.HidePlayers ? 1 : 0)); DrawChanged?.Invoke(); });
                Option(1, "显示NPC名字", Cfg.ShowNpcNames ? "开启" : "关闭", () => { Cfg.ShowNpcNames = !Cfg.ShowNpcNames; GameConfig.Patch(("npc_names", Cfg.ShowNpcNames ? 1 : 0)); DrawChanged?.Invoke(); });
                Option(2, "战斗背景修正", "开启", () => Toast?.Invoke("这项还没做"));
                Option(3, "过场动画样式", "默认", () => Toast?.Invoke("这项还没做"));
                break;
            default:
                Page = -1;
                Img(22500, 0, 0, block: true);
                Btn(22509, 22510, 167, 7, () => Visible = false);
                for (int i = 0; i < Items.Length; i++)
                {
                    int k = i;
                    Txt(Items[i], 47, 64 + 27 * i + 3);
                    Hit(42, 60 + 27 * i, 100, 19, _ => Chosen?.Invoke(k));
                }
                return;
        }
        Btn(22509, 22510, 167, 7, () => Visible = false);
        Txt("回上一页", 64, 270);
        Hit(60, 266, 72, 18, _ => { Page = -1; Refresh(); });
    }

    /// <summary>音量一行："现在 N ↑ ↓"（官方 0–15，存成 0–100）。</summary>
    private void Level(float y, Func<int> get, Action<int> set)
    {
        int lv = (int)Math.Round(get() * 15 / 100.0);
        Txt("现在", 14, y); Txt(lv.ToString(), 59, y);
        void Change(int d)
        {
            int n = Math.Clamp(lv + d, 0, 15);
            set((int)Math.Round(n * 100 / 15.0));
            GameAudio.I?.SetVolumes(Cfg.BgmVolume / 100f, Cfg.SeVolume / 100f);
            GameConfig.Patch(("bgm_volume", Cfg.BgmVolume), ("se_volume", Cfg.SeVolume));
            Refresh();
        }
        Txt("↑", 129, y); Hit(124, y - 4, 18, 18, _ => Change(1));
        Txt("↓", 153, y); Hit(148, y - 4, 18, 18, _ => Change(-1));
    }

    private void Option(int i, string label, string value, Action change)
    {
        float y = 48 + 50 * i;
        Txt(label, 16, y, new Color(1, 1, 0));
        Txt("现在", 16, y + 25); Txt(value, 67, y + 25);
        Txt("<变更>", 126, y + 25);
        Hit(124, y + 21, 50, 18, _ => { change(); Refresh(); });
    }

    public void TestLevel(bool bgm, int delta)
    {
        if (bgm) Cfg.BgmVolume = Math.Clamp((int)Math.Round((Math.Round(Cfg.BgmVolume * 15 / 100.0) + delta) * 100 / 15.0), 0, 100);
        else Cfg.SeVolume = Math.Clamp((int)Math.Round((Math.Round(Cfg.SeVolume * 15 / 100.0) + delta) * 100 / 15.0), 0, 100);
        GameAudio.I?.SetVolumes(Cfg.BgmVolume / 100f, Cfg.SeVolume / 100f);
        Refresh();
    }
}

/// <summary>
/// 技能窗口（底部菜单"技能"，官方实测，窗口都在 (1,1)，下面坐标是窗口内的）：
///  列表页 22750：职业居中 x147、字顶 49；剩余技能栏右对齐 162；每个技能一条 22703 在 (6, 107+28i)，名字 x36，等级右对齐 218，占用栏数右对齐 252。
///  详情页 22760：技能名居中 x89；等级 x220；经验值 x66；下一级右对齐 239；招式行 22704 在 (17, 80+22i)，名字 x39，MP 右对齐 245；
///  底部 列表 22830/31 在 (25,420)、◀ 22832/33 (128,420)、▶ 22834/35 (211,420)、页码"2/4"居中 x183。
///  官方等级显示"1/ 8"（第二个数是这个职业能练到的最高等级），这个上限的来源还没弄清，暂时只显示当前等级。
/// </summary>
public partial class SkillPanel : CgWindow
{
    public Func<PlayerState> Player = () => new PlayerState();
    private int _detail = -1;   // -1 = 列表页；否则 = 第几个技能
    public Action<SkillInfo, int>? TechChosen;

    public override void _Ready()
    {
        Layer = 16;
        Build(new Vector2(1, 1), false);
        Visible = false;
    }

    public void ShowDetail(int i) { _detail = i; Visible = true; Refresh(); }
    /// <summary>拖技能换位置：两个技能栏号。</summary>
    public Action<int, int>? Swap;
    public int Detail => _detail;

    public override void Refresh()
    {
        if (!Visible) return;
        Clear();
        var p = Player(); var skills = p.Skills;
        if (_detail >= skills.Count) _detail = -1;
        if (_detail < 0)
        {
            Img(22750, 0, 0, block: true);
            Btn(22509, 22510, 246, 6, () => Visible = false);
            Txt(p.Job, 147, 52, center: true);
            Txt((p.SkillSlots - skills.Sum(k => k.SlotsUsed)).ToString(), 162, 77, right: true);
            for (int i = 0; i < skills.Count && i < 10; i++)
            {
                var k = skills[i]; float y = 107 + 28 * i; int idx = i;
                Img(22703, 6, y);
                Txt(k.Name, 36, y + 6);
                Txt(k.Level.ToString(), 218, y + 5, right: true);
                Txt(k.SlotsUsed.ToString(), 252, y + 5, right: true);
                // 官方：按住一个技能拖到另一个上 = 换位置（Noeb a b）；只点一下 = 详情
                var h = Hit(6, y, 256, 20, _ => { });
                var kk = k;
                h.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb) return;
                    int to = idx + (int)MathF.Floor(mb.Position.Y / 28f);
                    if (to != idx && to >= 0 && to < skills.Count && Swap != null) Swap(kk.Slot, skills[to].Slot);
                    else { _detail = idx; Refresh(); }
                };
            }
        }
        else
        {
            var k = skills[_detail];
            Img(22760, 0, 0, block: true);
            Btn(22509, 22510, 246, 6, () => Visible = false);
            Txt(k.Name, 90, 42, center: true);
            Txt($"{k.Level}/{k.MaxLevel,2}", 220, 42);   // 官方"1/ 0"：后面是这个职业能练到的最高等级
            Txt(k.Exp.ToString(), 66, 66);
            Txt(k.Level >= k.MaxLevel ? "--" : k.NextExp.ToString(), 240, 66, right: true);   // 练到顶（或职业用不了）显示 --
            for (int i = 0; i < k.Techs.Count && i < 10; i++)
            {
                var t = k.Techs[i]; float y = 80 + 22 * i;
                Img(22704, 17, y);
                Txt(t.Name, 37, y + 8);
                Txt(t.Mp.ToString(), 247, y + 9, right: true);
                int ti = i; var kk = k;
                if (k.Type is 1 or 4) Hit(17, y, 240, 20, _ => TechChosen?.Invoke(kk, ti));   // 生产技能：点招式 = 开始（官方）
            }
            Btn(22830, 22831, 25, 420, () => { _detail = -1; Refresh(); });
            Btn(22832, 22833, 128, 420, () => { _detail = (_detail + skills.Count - 1) % skills.Count; Refresh(); });
            Btn(22834, 22835, 211, 420, () => { _detail = (_detail + 1) % skills.Count; Refresh(); });
            Txt($"{_detail + 1}/{skills.Count}", 183, 426, center: true);
        }
    }
}

/// <summary>
/// 地图窗口（底部菜单"地图"，官方实测）：背景 22535 贴右在 (713,1)，关闭 (222,6)；地名居中 x122、字顶 40；
/// "东 242  南 100"：东 字左 66、数值左 91，南 字左 136、数值左 161，字顶 190。
/// 中间菱形：以玩家为中心的地形缩影（等距投影，北在左上、东在右上，每格 2 像素），左角 (29,117)、宽 201、高 ~100；
/// 没走过的地方是黑的（官方同：客户端记录探索过的范围），玩家位置画一个闪烁的白点。
/// 每格颜色 = 物件图（有的话）或地面图的平均色。
/// </summary>
public partial class MapPanel : CgWindow
{
    public Func<(MapData? Map, int X, int Y)> Where = () => (null, 0, 0);
    public Func<string> MapName0 = () => "";
    private string MapName(MapData m) { var n = MapName0(); return (n.Length > 0 ? n : m.Name).Split('|')[0]; }
    private ImageTexture? _mini; private int _miniMap = -1, _miniVer = -1;
    private readonly Dictionary<int, bool[]> _explored = new();
    private readonly Dictionary<int, Color> _tileColor = new();
    private int _ver;
    private TextureRect? _dot; private double _blink;
    public const int Radius = 16;   // 走过时周围多少格算"探索过"（≈ 一屏；官方是"客户端下载过地图数据的范围"，凭截图估的）
    private const float DiaLeft = 24, DiaMidY = 118, DiaW = 212, DiaH = 112;   // 黑色菱形盖满框里的浅色底（官方同）

    public override void _Ready()
    {
        Layer = 16;
        Build(new Vector2(713, 1), true);
        Visible = false;
    }

    /// <summary>主程序每次走动 / 进图都调用：标记探索范围。</summary>
    public void Visit(MapData map, int px, int py)
    {
        if (!_explored.TryGetValue(map.Id, out var e)) _explored[map.Id] = e = new bool[map.Width * map.Height];
        bool changed = false;
        for (int y = Math.Max(0, py - Radius); y <= Math.Min(map.Height - 1, py + Radius); y++)
            for (int x = Math.Max(0, px - Radius); x <= Math.Min(map.Width - 1, px + Radius); x++)
                if (!e[y * map.Width + x]) { e[y * map.Width + x] = true; changed = true; }
        if (changed) _ver++;
        if (Visible) Refresh();   // 小地图以玩家为中心，走一步就要重画
    }

    private Color TileColor(int img)
    {
        if (_tileColor.TryGetValue(img, out var c)) return c;
        c = Colors.Black;
        if (img > 0 && Cache.ImageNo(img).Tex is { } t)
        {
            var im = t.GetImage(); float r = 0, g = 0, b = 0; int n = 0;
            for (int y = 0; y < im.GetHeight(); y += 2)
                for (int x = 0; x < im.GetWidth(); x += 2)
                {
                    var p = im.GetPixel(x, y);
                    if (p.A < 0.5f) continue;
                    r += p.R; g += p.G; b += p.B; n++;
                }
            if (n > 0) c = new Color(r / n, g / n, b / n);
        }
        _tileColor[img] = c;
        return c;
    }

    /// <summary>每格多少像素：官方实测往南走 4 格，小地图整体移动 (−8,−4) 像素 → 2。</summary>
    public float K = 2f;
    public bool ShowAll;   // 研究用：不管探索过没有，全画
    private ImageTexture BuildMini(MapData map, int cx, int cy)
    {
        int w = (int)DiaW + 1, h = (int)DiaH + 1;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var e = _explored.GetValueOrDefault(map.Id);
        float k = K;
        for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                // 菱形外透明
                float ux = (px - w / 2f) / (w / 2f), uy = (py - h / 2f) / (h / 2f);
                if (MathF.Abs(ux) + MathF.Abs(uy) > 1) { img.SetPixel(px, py, new Color(0, 0, 0, 0)); continue; }
                // 反投影（玩家在中心）：sx = (dx + dy)·k，sy = (dy − dx)·k/2
                float sx = (px - w / 2f) / k, sy = (py - h / 2f) * 2 / k;
                int tx = cx + (int)MathF.Round((sx - sy) / 2), ty = cy + (int)MathF.Round((sx + sy) / 2);
                if (tx < 0 || ty < 0 || tx >= map.Width || ty >= map.Height) { img.SetPixel(px, py, Colors.Black); continue; }
                int i = ty * map.Width + tx;
                if (!ShowAll && (e == null || !e[i])) { img.SetPixel(px, py, Colors.Black); continue; }
                img.SetPixel(px, py, TileColor(map.Ground[i]));   // 官方只画地面（建筑不画）
            }
        return ImageTexture.CreateFromImage(img);
    }

    private void MoveDot(MapData map, int x, int y) { if (_dot != null) _dot.Position = new Vector2(DiaLeft + DiaW / 2 - 1, DiaMidY - 1); }

    public override void Refresh()
    {
        if (!Visible) return;
        Clear(); _dot = null;
        var (map, px, py) = Where();
        Img(22535, 0, 0, block: true);
        Btn(22509, 22510, 222, 6, () => Visible = false);
        if (map == null) return;
        Txt(MapName(map), 122, 43, center: true);
        Txt("东", 66, 193, Colors.Black); Txt(px.ToString(), 91, 193);
        Txt("南", 136, 193, Colors.Black); Txt(py.ToString(), 161, 193);
        _mini = BuildMini(map, px, py);
        Body.AddChild(new TextureRect { Texture = _mini, Position = new Vector2(DiaLeft, DiaMidY - (int)(DiaH / 2)), MouseFilter = Control.MouseFilterEnum.Ignore });
        _dot = new TextureRect { Texture = ImageTexture.CreateFromImage(Dot()), MouseFilter = Control.MouseFilterEnum.Ignore };
        Body.AddChild(_dot);
        MoveDot(map, px, py);
    }

    private static Image Dot() { var i = Image.CreateEmpty(3, 3, false, Image.Format.Rgba8); i.Fill(Colors.White); return i; }

    public override void _Process(double delta)
    {
        if (_dot == null || !Visible) return;
        _blink += delta; _dot.Visible = (int)(_blink * 3) % 2 == 0;
    }
}

/// <summary>物品窗口标题旁的「整理」按钮（私服客户端的图档里才有，这里照截图画：土黄底、灰边、绿字带黑影）。</summary>
public partial class SortButton : Control
{
    public event Action? Pressed;
    private bool _down, _hover;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var t = new CgText { Wrap = 999, FontColor = new Color(0.1f, 1f, 0.1f), Text = "整  理" };
        float w = "整  理".Sum(CgText.Cells) * CgText.CellW;
        t.Position = new Vector2((Size.X - w) / 2, 1);
        AddChild(t);
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; _down = false; QueueRedraw(); };
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        if (mb.Pressed) { _down = true; QueueRedraw(); }
        else if (_down) { _down = false; QueueRedraw(); Pressed?.Invoke(); }
    }
    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(0.35f, 0.35f, 0.38f));
        DrawRect(r.Grow(-2), _down ? new Color(0.62f, 0.5f, 0.3f) : _hover ? new Color(0.82f, 0.68f, 0.44f) : new Color(0.75f, 0.6f, 0.38f));
    }
}

/// <summary>称号列表里选中那一行的青色框（官方 2px）。</summary>
public partial class CyanBox : Control
{
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color(0, 1, 1), false, 2);
}

