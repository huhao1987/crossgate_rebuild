using System.Text.Json;
using System.Text.Json.Nodes;
using CgPort.Client;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 战斗录像（这个私服客户端加的功能，官方实测）：战斗里点「本局錄像」，打完把这一场存成 video\video_年月日_序号.dat，
/// 聊天栏 "战斗录制成功（video\video_261109_000.dat）。"；系统 → 战斗录像 打开「战斗录像列表」看 / 播 / 删。
/// 官方的 .dat 里是缩略图 JPEG + 一张底图 + 加密的战斗封包；这里存成自己的格式：
/// user://video/video_年月日_序号.dat（JSON：时间、地图名、录制者、每行 [毫秒, 封包明文]）+ 同名 .png 缩略图（120×90）。
/// </summary>
public static class VideoStore
{
    public const string Dir = "user://video";
    public sealed record Entry(string File, DateTime Time, string Map, string Recorder, ImageTexture? Thumb);

    public static string Save(NetworkSession.BattleRecording r, Image? thumb)
    {
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        string day = r.Time.ToString("yyMMdd");
        int n = 0;
        while (global::Godot.FileAccess.FileExists($"{Dir}/video_{day}_{n:000}.dat")) n++;
        string name = $"video_{day}_{n:000}.dat";
        var o = new JsonObject
        {
            ["time"] = r.Time.ToString("yyyy/MM/dd HH:mm"), ["map"] = r.MapName, ["recorder"] = r.Recorder,
            ["lines"] = new JsonArray(r.Lines.Select(l => (JsonNode)new JsonArray(l.Ms, l.Line)).ToArray()),
        };
        using (var f = global::Godot.FileAccess.Open($"{Dir}/{name}", global::Godot.FileAccess.ModeFlags.Write)) f?.StoreString(o.ToJsonString());
        if (thumb != null) { thumb.Resize(120, 90, Image.Interpolation.Bilinear); thumb.SavePng($"{Dir}/{name[..^4]}.png"); }
        return name;
    }

    public static List<Entry> List()
    {
        var r = new List<Entry>();
        if (!DirAccess.DirExistsAbsolute(Dir)) return r;
        foreach (var f in DirAccess.GetFilesAt(Dir).Where(f => f.EndsWith(".dat")).OrderBy(f => f))
        {
            try
            {
                var o = JsonNode.Parse(global::Godot.FileAccess.GetFileAsString($"{Dir}/{f}"))!.AsObject();
                DateTime.TryParse(o["time"]?.GetValue<string>(), out var t);
                ImageTexture? th = null;
                var png = $"{Dir}/{f[..^4]}.png";
                if (global::Godot.FileAccess.FileExists(png)) { var im = Image.LoadFromFile(ProjectSettings.GlobalizePath(png)); if (im != null) th = ImageTexture.CreateFromImage(im); }
                r.Add(new Entry(f, t, o["map"]?.GetValue<string>() ?? "", o["recorder"]?.GetValue<string>() ?? "", th));
            }
            catch { }
        }
        return r;
    }

    public static NetworkSession.BattleRecording? Load(string file)
    {
        try
        {
            var o = JsonNode.Parse(global::Godot.FileAccess.GetFileAsString($"{Dir}/{file}"))!.AsObject();
            DateTime.TryParse(o["time"]?.GetValue<string>(), out var t);
            var lines = o["lines"]!.AsArray().Select(a => (a![0]!.GetValue<int>(), a[1]!.GetValue<string>())).ToList();
            return new NetworkSession.BattleRecording(t, o["map"]?.GetValue<string>() ?? "", o["recorder"]?.GetValue<string>() ?? "", lines);
        }
        catch { return null; }
    }

    public static void Delete(string file)
    {
        DirAccess.RemoveAbsolute($"{Dir}/{file}");
        var png = $"{Dir}/{file[..^4]}.png";
        if (global::Godot.FileAccess.FileExists(png)) DirAccess.RemoveAbsolute(png);
    }
}

/// <summary>
/// 战斗录像列表（官方实测，画面中间 (193,133) 574×430，橙边框）：标题 BMP 191「战斗录像列表」；一页 6 个（2 列 × 3 行），
/// 第 k 个框 (213+270c, 182+110r) 258×98：缩略图 (220+270c, 187+110r) 120×90，右边「时 间 / 地图名 / 录制者」标签 (347, +4/+37/+69)。
/// 底下（画面坐标）：播放 BMP 192/193 (222,517)、删 BMP 195/196 (272,517)、上一頁 23225 (384,517)、"第 N 页" (448,517)、下一頁 23227 (516,517)、取消 23214 (660,517)。
/// 点一个框 = 选中（绿框）。
/// </summary>
public partial class VideoListWindow : CanvasLayer
{
    public TextureCache Cache = null!;
    public Action<NetworkSession.BattleRecording>? Play;
    private Control _root = null!;
    private List<VideoStore.Entry> _list = new();
    private int _page, _sel = -1;
    public int Count => _list.Count;
    public int Selected => _sel;

    public override void _Ready() { Layer = 25; Visible = false; }

    public void Open() { _list = VideoStore.List(); _page = 0; _sel = _list.Count > 0 ? 0 : -1; Visible = true; Refresh(); }

    public void Refresh()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        _root = new Control { Position = new Vector2(193, 133), Size = new Vector2(574, 430) };
        AddChild(_root);
        _root.AddChild(new Control { Size = _root.Size, MouseFilter = Control.MouseFilterEnum.Stop });
        _root.AddChild(CgFrame.Tint(_root.Size));
        _root.AddChild(new CgFrame { Cache = Cache, Size = _root.Size, MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        if (ExeArt.Bmp(191) is { } title) _root.AddChild(new TextureRect { Texture = title, Position = new Vector2(287 - title.GetWidth() / 2f, 8), MouseFilter = Control.MouseFilterEnum.Ignore });
        for (int k = 0; k < 6; k++)
        {
            int i = _page * 6 + k, c = k % 2, r = k / 2;
            var box = new Vector2(20 + 270 * c, 49 + 110 * r);
            bool sel = i == _sel;
            _root.AddChild(new ReferenceRect { Position = box, Size = new Vector2(258, 98), BorderColor = sel ? new Color(0, 1, 0) : new Color(0.75f, 0.55f, 0.3f), BorderWidth = 1, EditorOnly = false, MouseFilter = Control.MouseFilterEnum.Ignore });
            _root.AddChild(new ColorRect { Position = box + new Vector2(7, 5), Size = new Vector2(120, 90), Color = new Color(0, 0, 0.13f), MouseFilter = Control.MouseFilterEnum.Ignore });
            string[] tags = { "时 间", "地图名", "录制者" };
            for (int t = 0; t < 3; t++) Tag(box + new Vector2(134, 4 + 33 * t), tags[t]);
            if (i < _list.Count)
            {
                var e = _list[i];
                if (e.Thumb != null) _root.AddChild(new TextureRect { Texture = e.Thumb, Position = box + new Vector2(7, 5), Size = new Vector2(120, 90), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore });
                string[] vals = { e.Time.ToString("MM/dd HH:mm"), e.Map, e.Recorder };
                for (int t = 0; t < 3; t++) _root.AddChild(new CgText { Text = vals[t], FontColor = Colors.White, Position = box + new Vector2(134, 19 + 33 * t), Wrap = 14 });
                var hit = new Control { Position = box, Size = new Vector2(258, 98), MouseFilter = Control.MouseFilterEnum.Stop };
                int ii = i;
                hit.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) { _sel = ii; if (mb.DoubleClick) DoPlay(); else Refresh(); } };
                _root.AddChild(hit);
            }
        }
        // 底下按钮（官方截图模板匹配的位置）：播放 BMP 192/193 (29,384)、删 BMP 195/196 (79,384)、上一頁 23225 (191,384)、页码、下一頁 23227 (323,384)、取消 23214 (467,384)
        BmpBtn(192, 193, new Vector2(29, 384), DoPlay);
        BmpBtn(195, 196, new Vector2(79, 384), () => { if (_sel >= 0 && _sel < _list.Count) { VideoStore.Delete(_list[_sel].File); _list = VideoStore.List(); _sel = Math.Min(_sel, _list.Count - 1); Refresh(); } });
        Btn(23225, 23226, new Vector2(191, 384), () => { if (_page > 0) { _page--; Refresh(); } });
        _root.AddChild(new ColorRect { Position = new Vector2(253, 386), Size = new Vector2(52, 17), Color = new Color(0.05f, 0.05f, 0.9f), MouseFilter = Control.MouseFilterEnum.Ignore });
        _root.AddChild(new CgText { Text = $"第 {_page + 1} 页", FontColor = Colors.White, Position = new Vector2(258, 388), Wrap = 20 });
        Btn(23227, 23228, new Vector2(323, 384), () => { if ((_page + 1) * 6 < _list.Count) { _page++; Refresh(); } });
        Btn(23214, 23215, new Vector2(467, 384), () => Visible = false);
    }

    private void DoPlay()
    {
        if (_sel < 0 || _sel >= _list.Count || VideoStore.Load(_list[_sel].File) is not { } rec) return;
        Visible = false; Play?.Invoke(rec);
    }
    public void TestPlay(int i) { _sel = i; DoPlay(); }

    private void Tag(Vector2 p, string text)
    {
        var tag = new Polygon2D { Polygon = new[] { new Vector2(4, 0), new Vector2(66, 0), new Vector2(66, 13), new Vector2(0, 13), new Vector2(0, 4) }, Color = new Color(0.94f, 0.68f, 0.42f), Position = p };
        _root.AddChild(tag);
        _root.AddChild(new CgText { Text = text, FontColor = new Color(0.1f, 1f, 0.1f), Position = p + new Vector2(14, -3), Wrap = 20 });
    }
    private void Btn(int img, int down, Vector2 p, Action a)
    {
        if (Cache.ImageNo(img).Tex is not { } t) return;
        var b = new TextureButton { TextureNormal = t, TextureHover = t, TexturePressed = Cache.ImageNo(down).Tex ?? t, Position = p };
        b.Pressed += a; _root.AddChild(b);
    }
    private void BmpBtn(int img, int hover, Vector2 p, Action a)
    {
        if (ExeArt.Bmp(img) is not { } t) return;
        var b = new TextureButton { TextureNormal = t, TextureHover = ExeArt.Bmp(hover) ?? t, Position = p };
        b.Pressed += a; _root.AddChild(b);
    }
}

/// <summary>
/// 录像回放：离线的 NetworkSession 照原来的时间间隔把封包喂进去（不连服务端），第二个 BattleUi（只读，不显示指令面板）照常播放。
/// 播完（或点画面右上 × 关掉）回到地图。
/// </summary>
public partial class VideoPlayer : Node
{
    public NetworkSession.BattleRecording Rec = null!;
    public BattleUi Ui = null!;
    public Action? Finished;
    private NetworkSession _s = null!;
    private int _i;
    private double _ms;
    private bool _ended;
    public bool Done => _ended;

    public override void _Ready()
    {
        _s = new NetworkSession(new NetworkOptions());
        _s.SetReplayName(Rec.Recorder);
        _s.BattleStarted += v => { Ui.Backdrop = () => _s.BattleBackdrop; Ui.Show(v); };
        _s.TurnResolved += (ev, v) => Ui.AppendLog(ev, v);
        _s.BattleEnded += o => { Ui.Close(o); _ended = true; };
        Ui.ReadOnly = true;
    }

    public override void _Process(double delta)
    {
        _ms += delta * 1000;
        while (_i < Rec.Lines.Count && (Rec.Lines[_i].Ms <= _ms || Ui.Speed > 1)) _s.TestFeed(Rec.Lines[_i++].Line);
        if ((_ended || _i >= Rec.Lines.Count) && !Ui.Visible) { Finished?.Invoke(); QueueFree(); }
    }

    public void Stop() { Ui.Visible = false; _i = Rec.Lines.Count; _ended = true; }
}
