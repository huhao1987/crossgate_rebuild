using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 官方界面（主画面 HUD），用原版图档拼出来。图像编号和位置是把官方客户端截图与它实际读取的图像做模板匹配得到的
/// （官方窗口 960×720；右侧元素按右边缘对齐，底栏贴底部）：
///   22570 左上状态面板 (0,0)
///   22581/22583/22585/22587/22589 右上 5 个功能按钮，x = 717/747/777/807/837（相对 960 宽），y = 4；第 6 个 244003 摆摊 (867,7)
///   22561 "小" (808,37)、22591 "動作" (827,37)
///   22560 底栏 640×24，贴底，从右往左平铺
/// 找不到图档（例如占位素材）时整个 HUD 不显示，退回调试文字。
/// </summary>
public partial class Hud : CanvasLayer
{
    public TextureCache Cache = null!;
    public System.Action<int>? ButtonPressed;   // 参数：按钮图像编号
    /// <summary>底部主菜单（官方 22550，鼠标移到画面最下面 24 像素时出现；手机上点底栏出现）：0 地图 1 状态 2 技能 3 物品 4 宠物 5 名片 6 图鉴 7 系统。</summary>
    public System.Action<int>? MenuPressed;
    /// <summary>左上开关 戰 聊 隊 名 易 对应的 FS 位（官方实测：戰 4、聊 8、隊 1、名 16、易 32）。</summary>
    public static readonly int[] FlagBits = { 4, 8, 1, 16, 32 };
    public System.Action<int>? FlagClicked;
    private readonly TextureRect?[] _lit = new TextureRect?[5];
    public int Flags { get; private set; }
    public bool FlagLit(int i) => _lit[i]?.Visible == true;
    public void TestClickFlag(int i) => FlagClicked?.Invoke(FlagBits[i]);
    public void TestPressButton(int id) => ButtonPressed?.Invoke(id);
    public void TestMenu(int i) => MenuPressed?.Invoke(i);
    public void SetFlags(int flags) { Flags = flags; for (int i = 0; i < 5; i++) if (_lit[i] != null) _lit[i]!.Visible = (flags & FlagBits[i]) != 0; }
    private TextureRect? _menu; private GreenBox? _menuBox; private double _menuHold;
    public bool Ok { get; private set; }
    private TextureRect? _skyA, _skyB;
    private int _skyVal = -1; private double _skyAcc;
    /// <summary>现在的 unix 秒（联网时加上服务端校时差），用来画左上角天色。</summary>
    public System.Func<long> Now = () => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly List<Control> _top = new();
    /// <summary>战斗中官方只保留底栏，状态面板和右上按钮隐藏。</summary>
    public void SetBattle(bool on)
    {
        _battle = on;
        foreach (var c in _top) c.Visible = !on && !(c.AnchorLeft == 0 ? _leftCov : _rightCov);
    }
    private bool _leftCov, _rightCov;
    /// <summary>右上角开着窗口（地图、物品）时，官方不会从半透明的窗口底下透出右上角的按钮：把它们藏起来。</summary>
    /// <summary>左边开着窗口（状态、技能、系统、名片簿…都在左上角）时，官方把左上状态面板藏起来。</summary>
    public void SetLeftCovered(bool covered) { _leftCov = covered; if (_battle) return; foreach (var c in _top) if (c.AnchorLeft == 0) c.Visible = !covered; }
    public void SetRightCovered(bool covered) { _rightCov = covered; if (_battle) return; foreach (var c in _top) if (c.AnchorLeft == 1) c.Visible = !covered; }
    private Control? _burst; private CgText? _burstTime;
    /// <summary>卡时（秒；小于 0 不显示）。</summary>
    public void SetBurst(long seconds)
    {
        if (_burst == null || _burstTime == null) return;
        if (seconds < 0) { _burst.Visible = false; return; }
        _burstTime.Text = $"{seconds / 3600:00}:{seconds / 60 % 60:00}";
        if (!_battle && !_leftCov) _burst.Visible = true;
    }
    public string BurstText => _burstTime?.Text ?? "";

    public override void _Ready()
    {
        Layer = 5;
        var root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        var panel = Img(22570);
        if (panel == null) { Ok = false; return; }
        Ok = true;
        // 底栏：640 宽，贴底，从右往左铺满
        for (int k = 0; k < 4; k++)
        {
            var bar = Img(22560);
            if (bar == null) break;
            bar.AnchorLeft = bar.AnchorRight = 1; bar.AnchorTop = bar.AnchorBottom = 1;
            bar.OffsetLeft = -640 * (k + 1); bar.OffsetTop = -24; bar.OffsetRight = bar.OffsetLeft + 640; bar.OffsetBottom = 0;
            bar.Name = "BarTile" + k;   // 平铺用，超出画面左边是正常的
            root.AddChild(bar);
        }
        // 底部主菜单：640×24，贴左下；8 个按钮各 80 宽。悬停时官方画 2px 绿框
        _menu = Img(22550);
        if (_menu != null)
        {
            _menu.AnchorTop = _menu.AnchorBottom = 1; _menu.OffsetLeft = 0; _menu.OffsetRight = 640; _menu.OffsetTop = -24; _menu.OffsetBottom = 0;
            _menu.MouseFilter = Control.MouseFilterEnum.Stop; _menu.Visible = false;
            _menuBox = new GreenBox { Size = new Vector2(80, 24), Visible = false };
            _menu.AddChild(_menuBox);
            _menu.GuiInput += e =>
            {
                if (e is InputEventMouseMotion mm) { _menuBox.Visible = true; _menuBox.Position = new Vector2((int)(mm.Position.X / 80) * 80, 0); }
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) { MenuPressed?.Invoke(Mathf.Clamp((int)(mb.Position.X / 80), 0, 7)); _menuHold = 0; }
            };
            _menu.MouseExited += () => _menuBox.Visible = false;
            // 手机：点底栏（菜单没出来时）→ 出现 5 秒。这一层要放在菜单下面，
            // 以前放在上面，把菜单的点击全挡住了（手机实测：点菜单没反应，触屏辅助又把点按挪到「聊天」按钮上）
            var tapZone = new Control { AnchorTop = 1, AnchorBottom = 1, AnchorRight = 1, OffsetTop = -24, MouseFilter = Control.MouseFilterEnum.Pass };
            tapZone.GuiInput += e => { if (e is InputEventScreenTouch { Pressed: true } || e is InputEventMouseButton { Pressed: true }) _menuHold = 5; };
            root.AddChild(tapZone);
            root.AddChild(_menu);
        }
        // 面板左边的圆盘是天色（exe 0x414868）：天色值 0–191（CgClock.Sky），序号 = 值/48、r = 值%48，
        // 天色图 22596+序号 画在 (27−r, 24)、下一张 22596+(序号+1)%4 画在 (75−r, 24)（图中心，图 48×40），面板盖在上面只露出圆盘那块
        var sky = new Control { Size = new Vector2(100, 48), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _skyA = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _skyB = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        sky.AddChild(_skyA); sky.AddChild(_skyB);
        root.AddChild(sky); _top.Add(sky);
        UpdateSky();
        panel.Position = Vector2.Zero; root.AddChild(panel); _top.Add(panel);
        // 左上 "Burst: hh:mm"（服务端 MTg 给秒数，官方实测在状态面板下面 (1,65) 一条暗底）
        _burst = new Control { Position = new Vector2(1, 65), Size = new Vector2(98, 22), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _burst.AddChild(new ColorRect { Color = new Color(0.12f, 0.11f, 0.06f, 0.75f), Size = _burst.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
        _burst.AddChild(new CgText { Text = "Burst:", FontColor = new Color(1f, 0.45f, 0.45f), Position = new Vector2(3, 3) });
        _burstTime = new CgText { Text = "", FontColor = Colors.White, Position = new Vector2(54, 3) };
        _burst.AddChild(_burstTime);
        root.AddChild(_burst); _top.Add(_burst);
        // 左上 5 个开关 戰 聊 隊 名 易：开着时盖上亮字 22574–22578（官方：亮=开）。点一下切换（服务端 FS 开关位）。
        for (int i = 0; i < 5; i++)
        {
            int bit = FlagBits[i];
            var lit = Img(22574 + i);
            if (lit != null) { lit.Position = new Vector2(59 + 17 * i, 6); lit.Visible = false; panel.AddChild(lit); _lit[i] = lit; }
            var hit = new Control { Position = new Vector2(57 + 17 * i, 3), Size = new Vector2(16, 16), MouseFilter = Control.MouseFilterEnum.Stop };
            hit.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || e is InputEventScreenTouch { Pressed: true }) FlagClicked?.Invoke(bit); };
            panel.AddChild(hit);
        }
        // 第 6 个 244003 = 摆摊 Ctrl+B（20×20，在 (867,7)，官方 4.06 实测）
        int[] btns = { 22581, 22583, 22585, 22587, 22589, 244003 };
        int[] xs = { 717, 747, 777, 807, 837, 867 };
        for (int i = 0; i < btns.Length; i++)
        {
            var t = Img(btns[i]);
            if (t == null) continue;
            AnchorRight(t, xs[i] - 960, btns[i] == 244003 ? 7 : 4); t.MouseFilter = Control.MouseFilterEnum.Stop;
            int id = btns[i];
            t.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ButtonPressed?.Invoke(id); };
            root.AddChild(t); _top.Add(t);
        }
        if (Img(22561) is TextureRect small) { AnchorRight(small, 808 - 960, 37); root.AddChild(small); _top.Add(small); }
        if (Img(22591) is TextureRect act)
        {
            AnchorRight(act, 827 - 960, 37); root.AddChild(act); _top.Add(act);
            act.MouseFilter = Control.MouseFilterEnum.Stop;   // 「動作」→ 动作窗口（官方 Ctrl+F）
            act.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || e is InputEventScreenTouch { Pressed: true }) ButtonPressed?.Invoke(22591); };
        }
        SetFlags(Flags);
        // 左上「郵件」：有新邮件时亮（22571 盖在 (81,21)），点一下打开名片簿
        _mailLit = Img(22571);
        if (_mailLit != null) { _mailLit.Position = new Vector2(81, 21); _mailLit.Visible = false; panel.AddChild(_mailLit); }
        var mailHit = new Control { Position = new Vector2(79, 19), Size = new Vector2(64, 14), MouseFilter = Control.MouseFilterEnum.Stop };
        mailHit.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || e is InputEventScreenTouch { Pressed: true }) MailClicked?.Invoke(); };
        panel.AddChild(mailHit);
    }
    private TextureRect? _mailLit;
    public System.Action? MailClicked;
    public bool MailLit => _mailLit?.Visible == true;
    public void SetMailLit(bool on) { if (_mailLit != null) _mailLit.Visible = on; }

    public override void _Process(double delta)
    {
        if ((_skyAcc += delta) >= 1) { _skyAcc = 0; UpdateSky(); }
        if (_menu == null) return;
        var vp = GetViewport();
        float h = vp.GetVisibleRect().Size.Y;
        bool hover = DisplayServer.GetName() != "headless" && !OS.HasFeature("mobile") && vp.GetMousePosition().Y >= h - 24;
        if (_menuHold > 0) _menuHold -= delta;
        _menu.Visible = Ok && (hover || _menuHold > 0 || PinMenu) && !_battle;
    }
    private bool _battle;
    /// <summary>测试用：让菜单出现几秒（等同手机上点底栏）。</summary>
    public void ShowMenu(double seconds = 5) => _menuHold = seconds;
    /// <summary>手机模式：底部菜单一直显示（官方要鼠标移到最下面才出来，手机没有悬停）。</summary>
    public bool PinMenu;
    public bool MenuShown => _menu?.Visible ?? false;

    private static void AnchorRight(TextureRect c, float offsetFromRight, float y)
    {
        var sz = c.Texture.GetSize();
        c.AnchorLeft = c.AnchorRight = 1;
        c.OffsetLeft = offsetFromRight; c.OffsetTop = y;
        c.OffsetRight = offsetFromRight + sz.X; c.OffsetBottom = y + sz.Y;
    }

    private TextureRect? Img(int number)
    {
        var (tex, _) = Cache.ImageNo(number);
        if (tex == null) return null;
        return new TextureRect { Texture = tex, Size = tex.GetSize(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Keep, MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
    }

    /// <summary>天色值一变就换图、挪位置。</summary>
    private void UpdateSky()
    {
        if (_skyA == null || _skyB == null) return;
        int v = CgPort.Core.CgClock.Sky(Now());
        if (v == _skyVal) return;
        _skyVal = v;
        int idx = v / 48, r = v % 48;
        void Put(TextureRect t, int img, float cx)
        {
            var (tex, e) = Cache.ImageNo(img);
            t.Texture = tex;
            if (tex != null) { t.Size = tex.GetSize(); t.Position = new Vector2(cx + (e?.OffsetX ?? -24), 24 + (e?.OffsetY ?? -20)); }
        }
        Put(_skyA, 22596 + idx, 27 - r);
        Put(_skyB, 22596 + (idx + 1) % 4, 75 - r);
    }
    public int SkyValue => _skyVal;
}

/// <summary>
/// 官方窗口边框：9 块图拼成（22620–22628 棕色，22630–22638 灰色）：左上、上、右上、左、中、右、左下、下、右下。
/// 边和中间部分平铺。素材里没有这些图时退回纯色底。
/// </summary>
public partial class CgFrame : Control
{
    /// <summary>
    /// 官方窗口的底：把后面的画面压暗并偏蓝（实测 ≈ 背景×0.45 + (0,0,32)，即深蓝 (0,0,58) 以 55% 不透明度叠上去）。
    /// </summary>
    public static ColorRect Tint(Vector2 size) => new()
    {
        Color = new Color(0, 0, 58 / 255f, 0.55f), Position = new Vector2(4, 4), Size = size - new Vector2(8, 8),
        MouseFilter = MouseFilterEnum.Ignore,
    };
    public TextureCache Cache = null!;
    public int BaseNumber = 22620;
    /// <summary>中间那块图是否画（官方对话框中间是半透明压暗，不画中间块）。</summary>
    public float CenterAlpha = 1;
    private ImageTexture?[] _t = new ImageTexture?[9];

    public override void _Ready()
    {
        for (int i = 0; i < 9; i++) _t[i] = Cache.ImageNo(BaseNumber + i).Tex;
        MouseFilter = MouseFilterEnum.Stop;
        // 官方窗口的底色（深蓝，棋盘格 = 半透明）不画：压暗由 Tint 负责（实测 背景×0.45 + 蓝 32）。
        // 以前只换掉和中间块中心同色的像素，块边上一列纯深蓝没换掉，拼起来每 64 像素一条竖线。
        if (_t.All(t => t != null))
            for (int i = 0; i < 9; i++)
            {
                var img = _t[i]!.GetImage(); img.Convert(Image.Format.Rgba8);
                for (int y = 0; y < img.GetHeight(); y++)
                    for (int x = 0; x < img.GetWidth(); x++)
                    {
                        var c = img.GetPixel(x, y);
                        if (c.A > 0 && c.B > 0.3f && c.R < 0.15f && c.G < 0.15f) img.SetPixel(x, y, new Color(0, 0, 0, 0));
                    }
                _t[i] = ImageTexture.CreateFromImage(img);
            }
    }

    public override void _Draw()
    {
        var s = Size;
        if (_t.Any(t => t == null)) { DrawRect(new Rect2(Vector2.Zero, s), new Color(0.1f, 0.1f, 0.3f, 0.9f)); return; }
        var tl = _t[0]!.GetSize(); var br = _t[8]!.GetSize();
        var mid = new Rect2(tl, s - tl - br);
        // 中间块：CenterAlpha = 0 时不画（官方对话框中间只是半透明压暗；以前照画会在每 64 像素的块边上留下竖线）
        if (CenterAlpha > 0) Tile(_t[4]!, mid);
        Tile(_t[1]!, new Rect2(tl.X, 0, s.X - tl.X - br.X, tl.Y));
        Tile(_t[7]!, new Rect2(tl.X, s.Y - br.Y, s.X - tl.X - br.X, br.Y));
        Tile(_t[3]!, new Rect2(0, tl.Y, tl.X, s.Y - tl.Y - br.Y));
        Tile(_t[5]!, new Rect2(s.X - br.X, tl.Y, br.X, s.Y - tl.Y - br.Y));
        DrawTexture(_t[0]!, Vector2.Zero);
        DrawTexture(_t[2]!, new Vector2(s.X - _t[2]!.GetSize().X, 0));
        DrawTexture(_t[6]!, new Vector2(0, s.Y - _t[6]!.GetSize().Y));
        DrawTexture(_t[8]!, s - br);
    }

    private void Tile(ImageTexture t, Rect2 r)
    {
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;
        var ts = t.GetSize();
        for (float y = r.Position.Y; y < r.End.Y; y += ts.Y)
            for (float x = r.Position.X; x < r.End.X; x += ts.X)
            {
                var w = Math.Min(ts.X, r.End.X - x); var h = Math.Min(ts.Y, r.End.Y - y);
                DrawTextureRectRegion(t, new Rect2(x, y, w, h), new Rect2(0, 0, w, h));
            }
    }
}
