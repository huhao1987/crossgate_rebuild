using CgPort.Core.Assets;
using Godot;

namespace CgPort.Godot;

/// <summary>等距换算：地块 64×47，x 向东 = 屏幕右上，y 向南 = 屏幕右下（已用真实数据确认）。</summary>
public static class Iso
{
    public const float HalfW = 32f, HalfH = 24f;
    public static Vector2 ToScreen(float x, float y) => new((x + y) * HalfW, (y - x) * HalfH);
    public static Vector2 ToScreen(Vector2I c) => ToScreen(c.X, c.Y);
    public static Vector2I ToCell(Vector2 p)
    {
        float a = p.X / HalfW, b = p.Y / HalfH;
        return new Vector2I(Mathf.RoundToInt((a - b) * 0.5f), Mathf.RoundToInt((a + b) * 0.5f));
    }

    // 屏幕方向（与动画方向 0..7 对应，偏移量由配置 dir_offset 调整）：上、右上、右、右下、下、左下、左、左上
    private static readonly Vector2[] ScreenDirs =
    {
        new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
    };

    /// <summary>格子位移 → 动画方向。</summary>
    public static int AnimeDirFor(int dx, int dy, int offset)
    {
        var v = ToScreen(dx, dy).Normalized();
        int best = 0; float bd = float.NegativeInfinity;
        for (int i = 0; i < 8; i++) { float d = v.Dot(ScreenDirs[i].Normalized()); if (d > bd) { bd = d; best = i; } }
        return ((best + offset) % 8 + 8) % 8;
    }
}

/// <summary>图像 → 纹理缓存。</summary>
public sealed class TextureCache
{
    private readonly AssetLibrary _lib;
    private readonly Dictionary<int, ImageTexture?> _byId = new();
    public TextureCache(AssetLibrary lib) => _lib = lib;
    public AssetLibrary Library => _lib;

    /// <summary>组内编号（只在只有一组图档时可靠，例如测试素材；正式素材请用 <see cref="ImageNo"/> 或 <see cref="Frame"/>）。</summary>
    public (ImageTexture? Tex, GraphicEntry? Entry) ById(int id)
    {
        if (!_lib.Graphics.TryById(id, out var set, out var e)) return (null, null);
        return (Get(set, e), e);
    }

    /// <summary>全局图像编号（NPC 静态外观、道具图标、地块）= GraphicInfo 里的"地图编号"字段。</summary>
    public (ImageTexture? Tex, GraphicEntry? Entry) ImageNo(int number) => ByMapId(number);

    /// <summary>动画帧：在动画配对的图像组里按组内编号取。</summary>
    public (ImageTexture? Tex, GraphicEntry? Entry) Frame(GraphicArchive? set, int gid)
    {
        if (set == null) return ById(gid);
        if (!set.TryLocal(gid, out var e)) return (null, null);
        return (Get(set, e), e);
    }

    public (ImageTexture? Tex, GraphicEntry? Entry) ByMapId(int mapId)
    {
        // 编号 0 = 没有图（GraphicInfo 里大量图的地图编号字段是 0，按 0 查会拿到一张无关的图：
        // 阿巴尼斯村"告示"等外观 0 的 NPC 就被画成橙色菱形 + 字，官方不画）
        if (mapId <= 0 || !_lib.Graphics.TryByMapId(mapId, out var set, out var e)) return (null, null);
        return (Get(set, e), e);
    }

    private readonly Dictionary<(GraphicArchive, int), ImageTexture?> _tex = new();
    private int _paletteNo = -1;
    /// <summary>当前调色板编号（palet_NN；-1 = 默认）。切换时清空纹理缓存，已显示的精灵下一帧会重新取图。</summary>
    public int PaletteNo
    {
        get => _paletteNo;
        set { if (value == _paletteNo) return; _paletteNo = value; _tex.Clear(); Version++; }
    }
    /// <summary>
    /// 界面窗口的半透明底：官方窗口底是"深蓝 (0,0,128) 点 + 透明点"的棋盘格，官方客户端画出来是均匀的半透明深蓝（实测战斗招式窗口 22702）。
    /// 这里把左右邻居是透明的深蓝点改成 50% 透明，透明点改成 25% 深蓝，看起来就是均匀的一层。实心的深蓝（技能行底条等）不动。
    /// </summary>
    private static void Translucent(byte[] p, int w, int h)
    {
        bool Navy(int i) => p[i + 3] == 255;   // 棋盘格上的任何颜色点（深蓝底、系统窗口的红色徽记都是）
        bool Clear(int x, int y) => x >= 0 && x < w && y >= 0 && y < h && p[(y * w + x) * 4 + 3] == 0;
        var navy = new List<int>(); var holes = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (Navy(i) && Clear(x - 1, y) && Clear(x + 1, y)) navy.Add(i);
            }
        foreach (int i in navy)
        {
            int x = i / 4 % w, y = i / 4 / w;
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                if (Clear(x + dx, y + dy)) holes.Add(((y + dy) * w + x + dx) * 4);
        }
        // 透明度对照官方截图（战斗招式窗口、系统窗口）：底色淡淡一层，背后的画面看得清楚
        foreach (int i in navy) p[i + 3] = 100;
        foreach (int i in holes) { p[i] = 0; p[i + 1] = 0; p[i + 2] = 128; p[i + 3] = 100; }
    }

    /// <summary>每次切换调色板加一；地面层据此重画。</summary>
    public int Version { get; private set; }
    private ImageTexture? Get(GraphicArchive set, GraphicEntry e)
    {
        if (_tex.TryGetValue((set, e.Id), out var t)) return t;
        var img = set.Decode(e);
        if (img == null) t = null;
        else
        {
            var rgba = img.ToRgba(_lib.PaletteFor(_paletteNo));
            if (e.MapId is >= 22000 and < 24000) Translucent(rgba, img.Width, img.Height);
            t = ImageTexture.CreateFromImage(Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, rgba));
        }
        _tex[(set, e.Id)] = t;
        return t;
    }
}

/// <summary>
/// 按动画编号播放的精灵（角色、NPC、敌人共用）。默认循环播放 Act；
/// PlayOnce 播放一次指定动作，播完回调（Hold=true 时停在最后一帧，用于倒下）。
/// 动画里没有该动作时用 FallbackAct，一次性动作则等一个短暂的默认时长后回调。
/// </summary>
public partial class AnimSprite : Sprite2D
{
    public TextureCache? Cache;
    public int AnimeId;
    public int Dir;
    public int Act;
    public int FallbackAct;
    private double _t;
    private int _onceAct = -1; private bool _hold; private Action? _done; private double _onceT;
    public int ShownAct { get; private set; } = -1;
    public bool HasAct(int act) => Cache != null && AnimeId != 0 && Cache.Library.AnimeActions(AnimeId).ContainsKey((Dir, act));

    public override void _Ready() => Centered = false;

    public void PlayOnce(int act, Action? done = null, bool hold = false)
    {
        _onceAct = act; _hold = hold; _done = done; _onceT = 0; _cueAt = (-1, -1);
    }

    /// <summary>取消一次性动作/停帧，回到循环动作。</summary>
    public void ResetLoop() { _onceAct = -1; _hold = false; _done = null; }

    public override void _Process(double delta)
    {
        if (Cache == null || AnimeId == 0) { FinishIfNoAnim(delta); return; }
        var acts = Cache.Library.AnimeActions(AnimeId);
        if (_onceAct >= 0)
        {
            _onceT += delta * 1000.0;
            if (!acts.TryGetValue((Dir, _onceAct), out var oa) || oa.Frames.Length == 0)
            {
                if (_onceT >= 300) Complete();
            }
            else
            {
                double dur = Math.Max(100.0, oa.DurationMs);
                int fi = Math.Min(oa.Frames.Length - 1, (int)(_onceT / (dur / oa.Frames.Length)));
                Show(oa.Set, oa.Frames[fi]); ShownAct = _onceAct; Cue(oa, _onceAct, fi);
                if (_onceT >= dur) Complete();
                return;
            }
            if (_onceAct >= 0 && _hold && _done == null) return;   // 停帧（动作缺失时保持原画面）
        }
        if (!acts.TryGetValue((Dir, Act), out var a) && !acts.TryGetValue((Dir, FallbackAct), out a) && !acts.TryGetValue((0, FallbackAct), out a)) return;
        if (a.Frames.Length == 0) return;
        _t += delta * 1000.0;
        double per = Math.Max(1.0, (double)a.DurationMs / a.Frames.Length);
        int i = (int)(_t / per) % a.Frames.Length;
        Show(a.Set, a.Frames[i]); ShownAct = Act; Cue(a, Act, i);
    }

    /// <summary>帧上的音效标记（见 AnimeArchive.Action.Sounds）：换到新的一帧时回调一次，参数是原始值（1–9999 音效号，≥10000 命中帧）。</summary>
    public Action<int>? SoundCue;
    /// <summary>一次性：动画走到命中帧（标记 ≥10000）时回调，然后清空。</summary>
    public Action? HitCue;
    private (int Act, int Frame) _cueAt = (-1, -1);
    private void Cue(CgPort.Core.Assets.AnimeArchive.Action a, int act, int frame)
    {
        if ((act, frame) == _cueAt) return;
        _cueAt = (act, frame);
        if (frame >= a.Sounds.Length || a.Sounds[frame] == 0) return;
        int v = a.Sounds[frame];
        SoundCue?.Invoke(v);
        if (v >= CgPort.Core.Assets.AnimeArchive.Action.HitFlag && HitCue is { } h) { HitCue = null; h(); }
    }

    private void FinishIfNoAnim(double delta)
    {
        if (_onceAct < 0 || _done == null) return;
        _onceT += delta * 1000.0;
        if (_onceT >= 300) Complete();
    }

    private void Complete()
    {
        var d = _done; _done = null;
        if (!_hold) _onceAct = -1;
        d?.Invoke();
    }

    private void Show(GraphicArchive? set, int gid)
    {
        var (tex, e) = Cache!.Frame(set, gid);
        if (tex == null || e == null) return;
        Texture = tex;
        Offset = new Vector2(e.OffsetX, e.OffsetY);
    }
}

/// <summary>
/// 头顶名字：官方客户端是 12 像素点阵字、黑色投影、字间距较宽；玩家青色、NPC 白色，水平居中在角色上方。
/// </summary>
public static class NameTag
{
    public static readonly Color PlayerColor = new(0f, 1f, 1f), NpcColor = Colors.White;
    private static FontVariation? _font;
    /// <summary>默认字体加宽字距（官方每个字之间约空 3 像素），关掉抗锯齿更接近点阵字。</summary>
    private static FontVariation Font => _font ??= CgFont.Make();

    public static Label Make(string text, Color color, float yOffset = -92)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(160, 16), Position = new Vector2(-80, yOffset), ZIndex = 50 };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeFontOverride("font", Font);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_shadow_color", Colors.Black);
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
        l.AddThemeConstantOverride("shadow_outline_size", 0);
        l.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        return l;
    }
}

/// <summary>
/// 官方客户端的字：12 像素点阵宋体、无抗锯齿、字距宽。这里用系统字体关掉抗锯齿 + 加字距来接近。
/// </summary>
public static class CgFont
{
    public static FontVariation Make(int spacing = 3)
    {
        var sys = new SystemFont { FontNames = new[] { "SimSun", "NSimSun", "Noto Sans CJK SC", "Noto Sans CJK", "WenQuanYi Zen Hei" }, Antialiasing = TextServer.FontAntialiasing.None, Hinting = TextServer.Hinting.Normal, SubpixelPositioning = TextServer.SubpixelPositioning.Disabled };
        return new FontVariation { BaseFont = sys, SpacingGlyph = spacing, SpacingSpace = 6 };   // 官方半角空格比较宽（实测约 8–15 像素，和位置有关，等换成点阵字再精确对）
    }
}

/// <summary>
/// 官方对话框文字排版：等宽格子。官方客户端按 GBK 字节排字：半角 1 格、全角 2 格，
/// 960 宽画面里每格 8.5 像素（实测：盖兹对话"喔喔!"全角间距 17 像素）；每行最多 47 格，超了换行
/// （实测：46 格后的"启"被挤到下一行）；行距 19 像素。服务端的文字靠全角空格对齐，所以必须照格子摆，
/// 用普通字体的自然宽度会对不齐。逐字画，字体是什么都不影响位置。
/// </summary>
public partial class CgText : Control
{
    public const float CellW = 8.5f;
    public const int LineH = 19, WrapCells = 46;   // 官方实测：一行最多 46 字节（"掉"在 45、"落"到 47 就换行；"开"正好 46 不换）
    private string _text = "";
    public string Text { get => _text; set { _text = value; _lines = Layout(value, Wrap); QueueRedraw(); } }
    public Color FontColor = Colors.White;
    private int _wrap = WrapCells;
    /// <summary>每行几格（改了马上重排；以前是字段，初始化里写在 Text 后面就不生效）。</summary>
    public int Wrap { get => _wrap; set { _wrap = value; _lines = Layout(_text, value); QueueRedraw(); } }
    public int LineHeight = LineH;
    private List<string> _lines = new();
    private static Font? _font;
    public int LineCount => _lines.Count;

    public CgText() { MouseFilter = MouseFilterEnum.Ignore; }

    public static int Cells(char c) => c < 0x80 ? 1 : 2;
    /// <summary>画 "%字母" 聊天表情（聊天栏才开）。</summary>
    public bool Emoji;
    public static int? EmojiIndex(char c) => c is >= 'A' and <= 'Z' ? c - 'A' : c is >= 'a' and <= 'z' ? c - 'a' + 26 : null;

    /// <summary>按 \n 分行，再按格数自动换行（全角字不拆开）。</summary>
    public static List<string> Layout(string text, int wrap)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var sb = new System.Text.StringBuilder(); int n = 0;
            foreach (var ch in raw)
            {
                int w = Cells(ch);
                if (n + w > wrap) { lines.Add(sb.ToString()); sb.Clear(); n = 0; }
                sb.Append(ch); n += w;
            }
            lines.Add(sb.ToString());
        }
        return lines;
    }

    public override void _Draw()
    {
        _font ??= CgFont.Make(0);
        for (int l = 0; l < _lines.Count; l++)
        {
            float x = 0, y = l * LineHeight + 13;
            var line = _lines[l];
            for (int k = 0; k < line.Length; k++)
            {
                char ch = line[k];
                // 聊天表情（exe 0x411539）："%" + 字母 → 第几个表情（A–Z = 0–25，a–z = 26–51，超过张数原样显示），
                // 图是 exe PNG 103（18×18 一格、10 列），按字高缩小画在这两个字节的位置
                if (Emoji && ch == '%' && k + 1 < line.Length && EmojiIndex(line[k + 1]) is int ei && ExeArt.Png(103) is { } sheet && ei < sheet.GetWidth() / 18 * (sheet.GetHeight() / 18))
                {
                    DrawTextureRectRegion(sheet, new Rect2(x + 1, y - 12, 15, 15), new Rect2(ei % (sheet.GetWidth() / 18) * 18, ei / (sheet.GetWidth() / 18) * 18, 18, 18));
                    x += 2 * CellW; k++; continue;
                }
                if (ch != ' ' && ch != '　')
                {
                    var s = ch.ToString();
                    float gw = _font.GetStringSize(s, HorizontalAlignment.Left, -1, 12).X, cw = Cells(ch) * CellW;
                    float gx = x + Math.Max(0, (cw - gw) / 2);   // 字形在格子里居中（半角标点在官方也是居中的）
                    DrawString(_font, new Vector2(gx + 1, y + 1), s, HorizontalAlignment.Left, -1, 12, Colors.Black);
                    DrawString(_font, new Vector2(gx, y), s, HorizontalAlignment.Left, -1, 12, FontColor);
                }
                x += Cells(ch) * CellW;
            }
        }
    }
}

/// <summary>官方客户端 exe 里的界面图 / 文本（见 PeResources）。游戏目录里找带 RCDATA 208 的 exe；没有就全部返回 null（界面退化）。</summary>
public static class ExeArt
{
    private static CgPort.Core.Assets.PeResources? _res;
    private static string? _root;
    private static readonly Dictionary<string, ImageTexture?> _tex = new();

    public static void Init(string gameRoot)
    {
        if (_root == gameRoot) return;
        _root = gameRoot; _res = null; _tex.Clear();
        try
        {
            foreach (var f in CgPort.Core.GameFiles.EnumerateFiles(gameRoot).Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).OrderByDescending(f => f.Contains("cg", StringComparison.OrdinalIgnoreCase)))
                if (CgPort.Core.Assets.PeResources.Load(f) is { } r && r.Get("10", 208) != null) { _res = r; break; }
        }
        catch { }
    }
    public static bool Ok => _res != null;

    /// <summary>RCDATA 里的 PNG。</summary>
    public static ImageTexture? Png(int id) => Get("p" + id, () =>
    {
        if (_res?.Get("10", id) is not { } b) return null;
        var im = new Image(); return im.LoadPngFromBuffer(b) == Error.Ok ? im : null;
    });

    /// <summary>BITMAP 资源，黑色 (0,0,0) 当透明（官方实测）。</summary>
    public static ImageTexture? Bmp(int id) => Get("b" + id, () =>
        _res?.DecodeBmp(id) is var (w, h, rgba) ? Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba) : null);

    public static string? Text(string type, int id) => _res?.Get(type, id) is { } b ? CgPort.Core.Text.Gbk.Decode(b) : null;

    private static ImageTexture? Get(string key, Func<Image?> make)
    {
        if (_tex.TryGetValue(key, out var t)) return t;
        var im = make();
        return _tex[key] = im == null ? null : ImageTexture.CreateFromImage(im);
    }
}

/// <summary>
/// 商店 / 技能店这类窗口里站着的 NPC（官方 0x44bbaf）：头像段的编号 ≥ 100000 是动画（站立动作、指定方向），
/// 小于的是一张图（14089 这种，按图本身的偏移贴在落脚点）。官方实拍：药剂师波洛姆（14089）站在窗口里的地板上。
/// </summary>
public static class NpcFigure
{
    public static void Add(Control parent, TextureCache cache, Vector2 at, int face, int standAct, int dir)
    {
        if (face <= 0) return;
        if (face >= 100000)
        {
            var holder = new Node2D { Position = at };
            holder.AddChild(new AnimSprite { Cache = cache, AnimeId = face, Act = standAct, FallbackAct = standAct, Dir = dir });
            parent.AddChild(holder);
        }
        else if (cache.ImageNo(face) is (ImageTexture tex, var e) && e != null)
            parent.AddChild(new TextureRect { Texture = tex, Position = at + new Vector2(e.OffsetX, e.OffsetY), MouseFilter = Control.MouseFilterEnum.Ignore });
    }
}
