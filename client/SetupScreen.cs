using CgPort.Core;
using CgPort.Core.Assets;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 首次运行设置：选择游戏目录 → 自动识别资源与服务端 → 写入 user://config.json → 进入游戏。
/// Android 上读取共享存储里的游戏目录需要"所有文件访问"权限（Android 11+），这里提供申请按钮。
/// </summary>
public partial class SetupScreen : CanvasLayer
{
    private LineEdit _path = null!;
    private Label _status = null!;
    private Button _start = null!;
    private AssetDetector.Result? _result;
    private CheckButton _online = null!;
    private OptionButton _ui = null!;
    private CheckButton _fps = null!;
    private HSlider _bgmVol = null!, _seVol = null!;
    private LineEdit _host = null!, _port = null!, _acct = null!, _pwd = null!;
    private Control _netRow = null!;

    private Vector2I _oldScale; private Window.ContentScaleStretchEnum _oldStretch;
    /// <summary>从游戏里打开（系统 → 高级设定）时为 true：多一个「返回游戏」，关掉时恢复游戏的画面缩放。</summary>
    public bool FromGame;

    public override void _Ready()
    {
        // 手机上设置界面单独放大：画布高 400（1080 高的屏幕约 2.7 倍），字和按钮够大，手指点得到
        var root = GetTree().Root;
        _oldScale = root.ContentScaleSize; _oldStretch = root.ContentScaleStretch;
        var ws = DisplayServer.WindowGetSize();
        if (OS.GetName() == "Android" || ws.Y >= 900)
        {
            root.ContentScaleSize = new Vector2I(Math.Max(1, ws.X * 400 / Math.Max(1, ws.Y)), 400);
            root.ContentScaleStretch = Window.ContentScaleStretchEnum.Fractional;
        }
        var bg = new ColorRect { Color = new Color(0.12f, 0.12f, 0.14f), AnchorRight = 1, AnchorBottom = 1 };
        AddChild(bg);
        // 手机模式画布只有 540 高，内容放不下时可以上下滑
        var scroll = new ScrollContainer { AnchorLeft = 0.06f, AnchorRight = 0.94f, AnchorTop = 0.04f, AnchorBottom = 0.97f, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(box);
        var title = new Label { Text = "CG Port · 设置（游戏里：底部「系统」→「高级设定」回到这里）" };
        title.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(title);
        box.AddChild(new Label { Text = "选择游戏目录（包含 bin/ 和服务端目录的那一层），或放着数据包 crossgate.cgpak 的目录（手机推荐：只拷一个文件）。程序只读取，不会修改。", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        var row = new HFlowContainer();
        _path = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 52),
            Text = OS.GetName() == "Android" ? "/storage/emulated/0/CrossGate" : System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "/Downloads/cg_single" };
        row.AddChild(_path);
        var browse = new Button { Text = "浏览…", CustomMinimumSize = new Vector2(120, 52) };
        _path.CustomMinimumSize = new Vector2(500, 52);
        browse.Pressed += Browse;
        row.AddChild(browse);
        box.AddChild(row);

        // 单机 / 联网
        _online = new CheckButton { Text = "联网模式（连接原版服务端 cgmsv）" };
        box.AddChild(_online);
        var net = new HFlowContainer(); net.AddThemeConstantOverride("separation", 8);
        LineEdit Field(string ph, string text, float w, bool secret = false)
        {
            var e = new LineEdit { PlaceholderText = ph, Text = text, CustomMinimumSize = new Vector2(w, 48), Secret = secret };
            net.AddChild(new Label { Text = ph }); net.AddChild(e); return e;
        }
        var old = GameConfig.TryLoadRaw();
        _host = Field("服务器", old?.Host ?? "127.0.0.1", 200);
        _port = Field("端口", (old?.Port ?? 9030).ToString(), 90);
        _acct = Field("账号", old?.Account ?? "test1", 140);
        _pwd = Field("密码", old?.Password ?? "123456", 140, true);
        _netRow = net; net.Visible = false;
        _online.Toggled += on => { net.Visible = on; Detect(); };
        _online.SetPressedNoSignal(old?.Mode == "network"); net.Visible = _online.ButtonPressed;
        if (old != null && !string.IsNullOrEmpty(old.GameRoot) && old.GameRoot != "testdata") _path.Text = old.GameRoot;
        box.AddChild(net);

        // 界面：自动（手机用手机模式）/ 手机模式（界面放大）/ 原版（和官方 960×720 一样）；显示帧数
        var look = new HFlowContainer(); look.AddThemeConstantOverride("separation", 12);
        look.AddChild(new Label { Text = "界面" });
        _ui = new OptionButton { CustomMinimumSize = new Vector2(220, 48) };
        _ui.AddItem("自动（手机用手机模式）"); _ui.AddItem("手机模式（界面放大）"); _ui.AddItem("原版（和电脑版一样）");
        _ui.Selected = (old?.UiMode) switch { "mobile" => 1, "original" => 2, _ => 0 };
        look.AddChild(_ui);
        _fps = new CheckButton { Text = "显示帧数", ButtonPressed = old?.ShowFps ?? false };
        look.AddChild(_fps);
        box.AddChild(look);
        // 音乐 / 音效音量（0 = 关）
        var snd = new HFlowContainer(); snd.AddThemeConstantOverride("separation", 12);
        snd.AddChild(new Label { Text = "音乐" });
        _bgmVol = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = old?.BgmVolume ?? 80, CustomMinimumSize = new Vector2(200, 40) };
        snd.AddChild(_bgmVol);
        snd.AddChild(new Label { Text = "音效" });
        _seVol = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = old?.SeVolume ?? 80, CustomMinimumSize = new Vector2(200, 40) };
        snd.AddChild(_seVol);
        _bgmVol.ValueChanged += _ => GameAudio.I?.SetVolumes((float)_bgmVol.Value / 100f, (float)_seVol.Value / 100f);
        _seVol.ValueChanged += _ => GameAudio.I?.SetVolumes((float)_bgmVol.Value / 100f, (float)_seVol.Value / 100f);
        box.AddChild(snd);
        box.AddChild(new Label { Text = "联网时游戏规则在服务端；本机目录只用来读图档。服务端在 Mac 上就填 127.0.0.1，手机连 Mac 就填 Mac 的局域网 IP（start_server 窗口里会显示）。账号不存在会自动建角色。", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) });

        var btns = new HFlowContainer();
        btns.AddThemeConstantOverride("separation", 12);
        if (OS.GetName() == "Android")
        {
            var perm = new Button { Text = "授予存储权限", CustomMinimumSize = new Vector2(200, 56) };
            perm.Pressed += () => { OS.RequestPermissions(); _status.Text = "已请求权限。如果系统打开了设置页，请为本应用开启“所有文件访问权限”，然后回来点“检测”。"; };
            btns.AddChild(perm);
        }
        var detect = new Button { Text = "检测", CustomMinimumSize = new Vector2(140, 56) };
        detect.Pressed += Detect;
        btns.AddChild(detect);
        _start = new Button { Text = "开始游戏", CustomMinimumSize = new Vector2(160, 56), Disabled = true };
        _start.Pressed += Start;
        btns.AddChild(_start);
        var demo = new Button { Text = "用测试素材试玩", CustomMinimumSize = new Vector2(200, 56) };
        demo.Pressed += () => { Write("{ \"game_root\": \"testdata\" }"); Restart(); };
        btns.AddChild(demo);
        if (FromGame)
        {
            var back = new Button { Text = "返回游戏", CustomMinimumSize = new Vector2(160, 56) };
            back.Pressed += () => { root.ContentScaleSize = _oldScale; root.ContentScaleStretch = _oldStretch; QueueFree(); };
            btns.AddChild(back);
        }
        box.AddChild(btns);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(_status);
    }

    private void Browse()
    {
        var dlg = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, CurrentDir = _path.Text };
        dlg.DirSelected += d => { _path.Text = d; Detect(); };
        AddChild(dlg);
        dlg.PopupCentered(new Vector2I(900, 600));
    }

    /// <summary>选的是目录且里面有 .cgpak 数据包（没有 bin 目录）时，改用数据包作游戏目录。</summary>
    private static string ResolveRoot(string path)
    {
        if (GameFiles.IsPack(path) || !Directory.Exists(path) || Directory.Exists(Path.Combine(path, "bin"))) return path;
        return Directory.EnumerateFiles(path, "*" + GameFiles.Ext).OrderBy(f => f).FirstOrDefault() ?? path;
    }

    private void Detect()
    {
        var root = ResolveRoot(_path.Text.Trim());
        _start.Disabled = true;
        if (!GameFiles.DirectoryExists(root)) { _status.Text = $"找不到目录：{root}\n（Android 上请先授予存储权限）"; return; }
        try
        {
            _result = AssetDetector.Detect(root);
            var lines = new List<string> { $"识别到 {_result.Sets.Count} 组图像资源：" + string.Join("、", _result.Sets.Select(s => s.Name)) };
            lines.Add(_result.ServerDir != null ? $"服务端目录：{_result.ServerDir}" : _online.ButtonPressed ? "没找到服务端目录：联网模式下地图改用客户端 map/0" : "没找到服务端目录（需要 <目录>/data/map），无法单机游戏");
            lines.Add(_result.Palette != null ? "调色板：已找到" : "调色板：没找到（颜色会不对）");
            lines.AddRange(_result.Notes);
            _status.Text = string.Join("\n", lines);
            _start.Disabled = _result.Sets.Count == 0 || (_result.ServerDir == null && !_online.ButtonPressed);
        }
        catch (Exception e) { _status.Text = "检测失败：" + e.Message; }
    }

    private void Start()
    {
        if (_result == null) return;
        var root = ResolveRoot(_path.Text.Trim());
        // 开局地图：有 1000 就用 1000，否则用编号最小的地图
        int start = 1000;
        if (_result.ServerDir != null)
        {
            var maps = MapData.IndexServerMapDir(Path.Combine(root, _result.ServerDir, "data", "map"));
            start = maps.ContainsKey(1000) ? 1000 : maps.Keys.DefaultIfEmpty(0).Min();
        }
        var net = _online.ButtonPressed
            ? new GameConfig.NetSettings(_host.Text.Trim(), int.TryParse(_port.Text, out var pt) ? pt : 9030, _acct.Text.Trim(), _pwd.Text)
            : null;
        Write(GameConfig.BuildJson(root, _result, start, net));
        Restart();
    }

    private void Write(string json)
    {
        // 界面模式、显示帧数这两项 BuildJson 不管，补进去
        if (System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject o)
        {
            o["ui_mode"] = _ui.Selected switch { 1 => "mobile", 2 => "original", _ => "" };
            o["show_fps"] = _fps.ButtonPressed ? 1 : 0;
            o["bgm_volume"] = (int)_bgmVol.Value;
            o["se_volume"] = (int)_seVol.Value;
            json = o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        using var f = global::Godot.FileAccess.Open("user://config.json", global::Godot.FileAccess.ModeFlags.Write);
        f.StoreString(json);
    }

    private void Restart() => GetTree().ReloadCurrentScene();
}
