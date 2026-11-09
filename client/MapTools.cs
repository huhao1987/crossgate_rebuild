using CgPort.Core;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 小地图窗口标题栏右边两个蓝球（这个客户端 exe 的 BMP 209/210）：左 = 自動尋路列表、右 = 全屏大地图。
/// 官方 bob 2026-11-09 实测，界面图都在 exe 资源里（见 PeResources / ExeArt）。
/// </summary>
public static class MapTools
{
    /// <summary>寻路坐标点：游戏目录 data/mappoint.dat；改过就存 user://mappoint.dat 并优先读它。</summary>
    public static List<MapPoint> LoadPoints(string gameRoot)
    {
        var user = ProjectSettings.GlobalizePath("user://mappoint.dat");
        try
        {
            if (File.Exists(user)) return MapPoint.Load(File.ReadAllBytes(user));
            var p = Path.Combine(gameRoot, "data", "mappoint.dat");
            if (GameFiles.Exists(p)) return MapPoint.Load(GameFiles.ReadAllBytes(p));
        }
        catch { }
        return new();
    }
    public static void SavePoints(List<MapPoint> pts)
    {
        try { File.WriteAllBytes(ProjectSettings.GlobalizePath("user://mappoint.dat"), MapPoint.Save(pts)); } catch { }
    }

    private static List<MapLabel>? _labels;
    public static List<MapLabel> Labels => _labels ??= ExeArt.Text("TEXT", 238) is { } t ? MapLabel.Parse(t) : new();
}

/// <summary>
/// 自動尋路列表（exe PNG 208，272×438，贴右上 (688,0)）：当前地图的坐标点每页 8 条（行 y = 79 + 25k，x 22–219），
/// 右边「設定」（BMP 211/212）改这一条；点名字 = 选它当目的地；◀▶ 22763/22764 翻页；
/// 目的座標 東 南 可以直接填，「確認」23216；「執行」23360 开始走；「上一頁」22714 回小地图。
/// </summary>
public partial class AutoPathWindow : CgWindow
{
    public Func<(MapData? Map, int X, int Y, string Name)> Where = () => (null, 0, 0, "");
    public Action<Vector2I>? Go;
    public Action? Back;
    public Action<MapPoint>? Edit;
    public List<MapPoint> Points = new();
    private int _page;
    private Vector2I? _target;
    private LineEdit? _ex, _ey;
    private string _hint = "要进行自动寻路，请先设置或点选目的地坐标。";

    public override void _Ready() { Layer = 17; Build(new Vector2(688, 0), true); Visible = false; }

    public void SetTarget(Vector2I t) { _target = t; _hint = $"目的地：东 {t.X} 南 {t.Y}，按「執行」开始寻路。"; Refresh(); }

    private List<MapPoint> Mine(int map) => Points.Where(p => p.Used == 1 && p.Map == map).ToList();

    public override void Refresh()
    {
        if (!Visible) return;
        Clear(); _ex = _ey = null;
        var (map, _, _, name) = Where();
        ImgTex(ExeArt.Png(208), 0, 0, block: true);
        Btn(22509, 22510, 247, 6, () => Visible = false);
        if (map == null) return;
        Txt(name, 81, 53);
        var list = Mine(map.Id);
        int pages = Math.Max(1, (list.Count + 7) / 8);
        _page = Math.Clamp(_page, 0, pages - 1);
        for (int k = 0; k < 8; k++)
        {
            int i = _page * 8 + k; if (i >= list.Count) break;
            var p = list[i]; float y = 79 + 25 * k;
            Txt(p.Name, 40, y + 6);
            Hit(22, y, 197, 20, _ => SetTarget(new Vector2I(p.X, p.Y)));
            BtnTex(ExeArt.Bmp(211), ExeArt.Bmp(212), 227, y + 1, () => Edit?.Invoke(p));
        }
        Btn(22763, 22763, 129, 285, () => { _page = (_page + pages - 1) % pages; Refresh(); });
        Btn(22764, 22764, 212, 285, () => { _page = (_page + 1) % pages; Refresh(); });
        Txt($"{_page + 1}/{pages}", 176, 291, center: true);
        _ex = Field(_target?.X.ToString() ?? "0", 48, 323);
        _ey = Field(_target?.Y.ToString() ?? "0", 121, 323);
        Btn(23216, 23216, 182, 320, () =>
        {
            if (int.TryParse(_ex?.Text, out var x) && int.TryParse(_ey?.Text, out var y) && map.InBounds(x, y)) SetTarget(new Vector2I(x, y));
            else { _hint = "坐标不对。"; Refresh(); }
        });
        var t = new CgText { Wrap = 30, Text = _hint, Position = new Vector2(8, 362) }; Body.AddChild(t);
        Btn(23360, 23360, 40, 410, () => { if (_target is { } tg) { Go?.Invoke(tg); Visible = false; } else { _hint = "请先设置或点选目的地坐标。"; Refresh(); } });
        Btn(22714, 22714, 150, 410, () => { Visible = false; Back?.Invoke(); });
    }

    private LineEdit Field(string v, float x, float y)
    {
        var e = new LineEdit { Text = v, Position = new Vector2(x, y), Size = new Vector2(44, 18), Flat = true, MaxLength = 4 };
        e.AddThemeColorOverride("font_color", Colors.White);
        e.AddThemeFontSizeOverride("font_size", 12);
        Body.AddChild(e); return e;
    }

    public void TestPage(int d) { _page += d; Refresh(); }
    public Vector2I? Target => _target;
    public int PageCount => Where().Map is { } m ? Math.Max(1, (Mine(m.Id).Count + 7) / 8) : 0;
}

/// <summary>設置地圖座標點（exe PNG 213，272×152，(688,0)）：座標名 / 東 / 南，红钮（BMP 197）= 删掉这一条，「確認」23216 存。</summary>
public partial class PointEditWindow : CgWindow
{
    public MapPoint? Point;
    public Action? Saved;
    private LineEdit _name = null!, _x = null!, _y = null!;
    public override void _Ready() { Layer = 18; Build(new Vector2(688, 0), true); Visible = false; }

    public void Open(MapPoint p) { Point = p; Visible = true; Refresh(); }

    public override void Refresh()
    {
        if (!Visible || Point is not { } p) return;
        Clear();
        ImgTex(ExeArt.Png(213), 0, 0, block: true);
        Btn(22509, 22510, 247, 6, () => Visible = false);
        _name = Edit(p.Name, 78, 48, 180, 11);
        _x = Edit(p.X.ToString(), 98, 83, 54, 4);
        _y = Edit(p.Y.ToString(), 217, 83, 54, 4);
        BtnTex(ExeArt.Bmp(197), ExeArt.Bmp(196), 174, 122, () => { p.Used = 0; Visible = false; Saved?.Invoke(); });
        Btn(23216, 23216, 208, 121, () =>
        {
            if (!int.TryParse(_x.Text, out var x) || !int.TryParse(_y.Text, out var y)) return;
            p.Name = _name.Text.Trim(); p.X = x; p.Y = y; Visible = false; Saved?.Invoke();
        });
    }

    private LineEdit Edit(string v, float x, float y, float w, int max)
    {
        var e = new LineEdit { Text = v, Position = new Vector2(x, y), Size = new Vector2(w, 18), Flat = true, MaxLength = max };
        e.AddThemeColorOverride("font_color", Colors.White);
        e.AddThemeFontSizeOverride("font_size", 12);
        Body.AddChild(e); return e;
    }
}

/// <summary>
/// 全屏大地图（官方 bob 实测）：底图 exe PNG 231（640×480，放大 1.5 倍铺满 960×720），黑菱形 (67,52)–(891,667) 里画地图，
/// 以玩家为中心、每格缩成 1/10.67（东西一格 3 像素、南北 2.25 像素：右键点 (530,330) = (248,84)，玩家 (228,89) 在中心实测），
/// 只画收到过 / 走过的格子；标注取 exe TEXT 238（名字画在点上方 16 像素）。
/// 左上标题 PNG 234 + 地图名；▷/◁ 243033/243030「地图选项」打开半透明选项板（定位点说明 + 7 类显示勾选）；
/// 右键点地图 = 寻路目标（蓝球 BMP 235 + "x,y" + 红点线），出现「开始寻路(Ctrl+Enter)」（小人 BMP 246）。
/// 右上指南针 PNG 245、东 南 坐标；右上角红叉关。
/// </summary>
public partial class BigMapWindow : CanvasLayer
{
    public TextureCache Cache = null!;
    public GameConfig Cfg = null!;
    public Func<(MapData? Map, int X, int Y, string Name)> Where = () => (null, 0, 0, "");
    public Func<int, int, bool> Explored = (_, _) => true;
    public Action<Vector2I>? Go;
    public Vector2I? Target { get; private set; }
    public const float S = 3f / 32f;
    private static readonly Vector2 AreaPos = new(67, 52), AreaSize = new(824, 615);
    private Control _root = null!, _over = null!;
    private SubViewport _vp = null!;
    private MapDraw _draw = null!;
    private bool _options;
    public int LabelCount { get; private set; }

    public override void _Ready()
    {
        Layer = 30; Visible = false;
        _root = new Control { Size = new Vector2(960, 720), MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_root);
        _vp = new SubViewport { Size = (Vector2I)AreaSize, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        _draw = new MapDraw { Owner2 = this };
        _vp.AddChild(_draw);
        AddChild(_vp);
    }

    public void Open() { Visible = true; Refresh(); }
    public void Toggle() { if (Visible) Visible = false; else Open(); }

    public void Refresh()
    {
        foreach (var c in _root.GetChildren()) c.QueueFree();
        var (map, px, py, name) = Where();
        if (ExeArt.Png(231) is { } bg) _root.AddChild(new TextureRect { Texture = bg, Size = new Vector2(960, 720), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore });
        else _root.AddChild(new ColorRect { Color = new Color(0.85f, 0.78f, 0.62f), Size = new Vector2(960, 720), MouseFilter = Control.MouseFilterEnum.Ignore });
        if (map == null) return;
        // 地图（只在打开 / 换目标时画一次）
        _draw.Map = map; _draw.Center = new Vector2I(px, py);
        _draw.QueueRedraw();
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        var mapRect = new TextureRect { Texture = _vp.GetTexture(), Position = AreaPos, Size = AreaSize, MouseFilter = Control.MouseFilterEnum.Stop, Material = new ShaderMaterial { Shader = Diamond } };
        mapRect.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb) PickAt(mb.Position);
            else if (e is InputEventScreenTouch { Pressed: true, DoubleTap: true } st) PickAt(st.Position);
        };
        _root.AddChild(mapRect);
        _over = new Control { Position = AreaPos, Size = AreaSize, MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        _root.AddChild(_over);
        // 标注
        LabelCount = 0;
        foreach (var l in MapTools.Labels)
        {
            if (l.Map != map.Id || (Cfg.MapLabelMask & (1 << l.Kind)) == 0) continue;
            var p = ToArea(l.X, l.Y, px, py);
            if (p.X < 0 || p.Y < 0 || p.X > AreaSize.X || p.Y > AreaSize.Y || !InDiamond(p) || !Explored(l.X, l.Y)) continue;   // 官方：没去过的地方不标
            if (l.Kind is >= 1 and <= 3 or 5 && l.Image > 0 && l.Image != 18 && Cache.ImageNo(l.Image).Tex is { } icon)
                _over.AddChild(new TextureRect { Texture = icon, Position = p - icon.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            var color = l.Kind == 6 && l.Image is >= 0 and < 10 ? ChatLog.Palette[Math.Min(l.Image, ChatLog.Palette.Length - 1)] : Colors.White;
            var off = l.Pos switch { 1 => new Vector2(0, 4), 2 => new Vector2(-50, -8), 3 => new Vector2(50, -8), _ => new Vector2(0, -20) };
            var tag = Small(l.Name, color); tag.Position = p + off + new Vector2(-80, 0);
            _over.AddChild(tag); LabelCount++;
        }
        // 自己（红球）、目标（蓝球 + 坐标 + 红点线）
        var me = ToArea(px, py, px, py);
        if (Target is { } t)
        {
            var tp = ToArea(t.X, t.Y, px, py);
            _over.AddChild(new DotLine { From = me, To = tp });
            if (ExeArt.Bmp(235) is { } blue) _over.AddChild(new TextureRect { Texture = blue, Position = tp - new Vector2(7, 7), MouseFilter = Control.MouseFilterEnum.Ignore });
            var lab = Small($"{t.X},{t.Y}", new Color(1f, 1f, 0.3f)); lab.Position = tp + new Vector2(-80 + 30, 6); _over.AddChild(lab);
        }
        if (ExeArt.Bmp(233) is { } red) _over.AddChild(new TextureRect { Texture = red, Position = me - new Vector2(7, 7), MouseFilter = Control.MouseFilterEnum.Ignore });
        // 标题、选项、开始寻路
        if (ExeArt.Png(234) is { } title) _root.AddChild(new TextureRect { Texture = title, Position = new Vector2(12, 11), MouseFilter = Control.MouseFilterEnum.Ignore });
        AddText(name, 104, 22, Colors.White, center: true);
        AddButton(_options ? 243030 : 243033, _options ? 243032 : 243035, 11, 51, () => { _options = !_options; Refresh(); });
        AddText("地图选项", 34, 55, Colors.White);
        if (Target != null)
        {
            if (ExeArt.Bmp(246) is { } run) { var b = new TextureButton { TextureNormal = run, TextureHover = ExeArt.Bmp(247) ?? run, Position = new Vector2(11, 73) }; b.Pressed += StartWalk; _root.AddChild(b); }
            AddText("开始寻路(Ctrl+Enter)", 34, 77, Colors.White);
        }
        if (_options) BuildOptions();
        // 指南针、坐标、关闭
        if (ExeArt.Png(245) is { } comp) _root.AddChild(new TextureRect { Texture = comp, Position = new Vector2(795, 60), MouseFilter = Control.MouseFilterEnum.Ignore });
        AddText("东", 781, 128, new Color(0.2f, 0.9f, 0.2f)); AddText(px.ToString(), 822, 128, Colors.White, right: true);
        AddText("南", 832, 128, new Color(0.2f, 0.9f, 0.2f)); AddText(py.ToString(), 873, 128, Colors.White, right: true);
        AddButton(22509, 22510, 935, 6, () => Visible = false);
    }

    private void BuildOptions()
    {
        _root.AddChild(new OptionsBg { Position = new Vector2(37, 52), Size = new Vector2(220, 268) });
        var green = new Color(0.2f, 1f, 0.2f);
        AddText("定位点：", 44, 59, green);
        if (ExeArt.Bmp(233) is { } r) _root.AddChild(new TextureRect { Texture = r, Position = new Vector2(44, 80), MouseFilter = Control.MouseFilterEnum.Ignore });
        AddText("你现在的位置", 62, 79, Colors.White);
        if (ExeArt.Bmp(235) is { } b) _root.AddChild(new TextureRect { Texture = b, Position = new Vector2(44, 100), MouseFilter = Control.MouseFilterEnum.Ignore });
        AddText("右键点选的寻路坐标", 62, 99, Colors.White);
        AddText("显示选项：", 44, 119, green);
        string[] names = { "小的地点或居所", "伐木地点", "挖掘地点", "狩猎地点", "村镇大地点", "宠物Lv1坐标", "特定NPC" };
        for (int k = 0; k < 7; k++)
        {
            int kk = k; float y = 139 + 20 * k;
            _root.AddChild(new CheckMark { Position = new Vector2(47, y + 5), Size = new Vector2(8, 8), On = (Cfg.MapLabelMask & (1 << k)) != 0 });
            AddText(names[k], 62, y, Colors.White);
            var hit = new Control { Position = new Vector2(44, y), Size = new Vector2(200, 18), MouseFilter = Control.MouseFilterEnum.Stop };
            hit.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                Cfg.MapLabelMask ^= 1 << kk; GameConfig.Patch(("map_labels", Cfg.MapLabelMask)); Refresh();
            };
            _root.AddChild(hit);
        }
    }

    /// <summary>大地图上的字比人物头上的名字小（官方 5 个字约 55 像素宽），白字黑影。</summary>
    private static Label Small(string text, Color c)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(160, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", 11);
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_shadow_color", Colors.Black);
        l.AddThemeConstantOverride("shadow_offset_x", 1); l.AddThemeConstantOverride("shadow_offset_y", 1);
        l.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        return l;
    }

    private Vector2 ToArea(int x, int y, int px, int py) => AreaSize / 2 + (Iso.ToScreen(x, y) - Iso.ToScreen(px, py)) * S;
    private static bool InDiamond(Vector2 p) => MathF.Abs(p.X / AreaSize.X - 0.5f) + MathF.Abs(p.Y / AreaSize.Y - 0.5f) <= 0.5f;

    public void PickAt(Vector2 local)
    {
        var (map, px, py, _) = Where();
        if (map == null || !InDiamond(local)) return;
        var c = Iso.ToCell(Iso.ToScreen(px, py) + (local - AreaSize / 2) / S);
        if (!map.InBounds(c.X, c.Y)) return;
        Target = c; Refresh();
    }
    public void SetTarget(Vector2I? t) { Target = t; if (Visible) Refresh(); }

    private void StartWalk() { if (Target is { } t) { Visible = false; Go?.Invoke(t); } }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter, CtrlPressed: true }) { StartWalk(); GetViewport().SetInputAsHandled(); }
        else if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { Visible = false; GetViewport().SetInputAsHandled(); }
    }

    private void AddText(string s, float x, float y, Color c, bool right = false, bool center = false)
    {
        var t = new CgText { Wrap = 999, FontColor = c, Text = s };
        float w = s.Sum(CgText.Cells) * CgText.CellW;
        t.Position = new Vector2(right ? x - w : center ? x - w / 2 : x, y - 5);
        _root.AddChild(t);
    }
    private void AddButton(int img, int hover, float x, float y, Action click)
    {
        if (Cache.ImageNo(img).Tex is not { } t) return;
        var b = new TextureButton { TextureNormal = t, TextureHover = Cache.ImageNo(hover).Tex ?? t, TexturePressed = Cache.ImageNo(img + 1).Tex ?? t, Position = new Vector2(x, y) };
        b.Pressed += click; _root.AddChild(b);
    }

    private static Shader? _diamond;
    private static Shader Diamond => _diamond ??= new Shader { Code = "shader_type canvas_item; void fragment(){ if (abs(UV.x-0.5)+abs(UV.y-0.5) > 0.5) discard; COLOR = texture(TEXTURE, UV); }" };

    /// <summary>画地图：地面 + 物件（锚点格走过才画），整体缩放 S、玩家在中心。</summary>
    private partial class MapDraw : Node2D
    {
        public BigMapWindow Owner2 = null!;
        public MapData? Map;
        public Vector2I Center;
        public override void _Ready() => TextureFilter = TextureFilterEnum.Linear;
        public override void _Draw()
        {
            if (Map is not { } m) return;
            var cache = Owner2.Cache;
            var origin = AreaSize / 2 - Iso.ToScreen(Center.X, Center.Y) * S;
            DrawSetTransform(origin, 0, new Vector2(S, S));
            int r = (int)(AreaSize.X / 2 / S / Iso.HalfW) + 2;
            var objs = new List<(float Key, int X, int Y, int Id)>();
            for (int sy = -r; sy <= r; sy++)          // sy = y − x（屏幕纵向）
                for (int sx = -r; sx <= r; sx++)      // sx = x + y（屏幕横向）
                {
                    if (((sx + sy) & 1) != 0) continue;
                    int x = Center.X + (sx - sy) / 2, y = Center.Y + (sx + sy) / 2;
                    if (!m.InBounds(x, y) || !Owner2.Explored(x, y)) continue;
                    int i = m.Index(x, y);
                    int g = m.Ground[i];
                    if (g != 0 && cache.ByMapId(g) is ({ } tex, { } e)) DrawTexture(tex, Iso.ToScreen(x, y) + new Vector2(e.OffsetX, e.OffsetY));
                    int o = m.Objects[i];
                    if (o > 999) objs.Add((Iso.ToScreen(x, y).Y, x, y, o));
                }
            foreach (var (_, x, y, id) in objs.OrderBy(o => o.Key))
                if (cache.ByMapId(id) is ({ } tex, { } e)) DrawTexture(tex, Iso.ToScreen(x, y) + new Vector2(e.OffsetX, e.OffsetY));
        }
    }

    private partial class DotLine : Control
    {
        public Vector2 From, To;
        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
        public override void _Draw()
        {
            float len = From.DistanceTo(To); if (len < 1) return;
            var d = (To - From) / len;
            for (float t = 0; t < len; t += 6) DrawRect(new Rect2(From + d * t - Vector2.One, new Vector2(2, 2)), new Color(1, 0.15f, 0.1f));
        }
    }
    private partial class OptionsBg : Control
    {
        public override void _Ready() => MouseFilter = MouseFilterEnum.Stop;
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.25f, 0.24f, 0.22f, 0.7f));
            DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color(0.93f, 0.9f, 0.8f), false, 2);
        }
    }
    private partial class CheckMark : Control
    {
        public bool On;
        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, new Vector2(8, 8)), new Color(0.82f, 0.85f, 0.85f));
            if (!On) return;
            var red = new Color(0.95f, 0.1f, 0.1f);
            DrawLine(new Vector2(-1, 4), new Vector2(2, 7), red, 2); DrawLine(new Vector2(2, 7), new Vector2(10, -3), red, 2);
        }
    }
}
