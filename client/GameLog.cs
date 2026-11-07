using Godot;

namespace CgPort.Godot;

/// <summary>
/// 运行日志：写到游戏目录旁边的 logs 文件夹（手机上是 /storage/emulated/0/CrossGate/logs/），电脑上写到 user://logs/。
/// 记录：启动信息（机型、屏幕、缩放）、每次点击/触摸点在哪里、点中了什么、报错、每 10 秒的帧数统计。
/// 画面上不显示任何东西；以后玩的时候出问题，把这个文件夹发回来就能看到当时的情况。只保留最近 10 个文件。
/// </summary>
public partial class GameLog : Node
{
    private static StreamWriter? _w;
    private static readonly object _lock = new();
    public static string? FilePath { get; private set; }

    // 帧数统计
    private double _acc; private int _frames; private double _worst; private double _sinceLog;

    public static void Open(string gameRoot)
    {
        try
        {
            string dir;
            if (OS.GetName() == "Android" && gameRoot.Length > 0)
            {
                var baseDir = gameRoot.EndsWith(".cgpak", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(gameRoot)! : gameRoot;
                dir = Path.Combine(baseDir, "logs");
            }
            else dir = ProjectSettings.GlobalizePath("user://logs");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "cgport_*.log").OrderByDescending(x => x).Skip(9)) try { File.Delete(old); } catch { }
            FilePath = Path.Combine(dir, $"cgport_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _w = new StreamWriter(FilePath, false) { AutoFlush = true };
            Write("启动", $"{OS.GetName()} {OS.GetModelName()} 屏幕 {DisplayServer.ScreenGetSize()} 窗口 {DisplayServer.WindowGetSize()} " +
                         $"DPI {DisplayServer.ScreenGetDpi()} 刷新率 {DisplayServer.ScreenGetRefreshRate():0} 安全区 {DisplayServer.GetDisplaySafeArea()}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("崩溃", e.ExceptionObject?.ToString() ?? "");
            TaskScheduler.UnobservedTaskException += (_, e) => Write("异常", e.Exception.ToString());
        }
        catch (Exception e) { GD.PushWarning("日志打不开：" + e.Message); }
    }

    public static void Write(string kind, string text)
    {
        if (_w == null) return;
        lock (_lock) { try { _w.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{kind}] {text}"); } catch { } }
    }

    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; }

    public override void _Process(double delta)
    {
        _acc += delta; _frames++; _sinceLog += delta;
        if (delta > _worst) _worst = delta;
        if (_sinceLog >= 10)
        {
            Write("帧数", $"平均 {_frames / _acc:0.0} fps，最慢一帧 {_worst * 1000:0} ms，内存 {OS.GetStaticMemoryUsage() / 1048576} MB");
            _acc = 0; _frames = 0; _worst = 0; _sinceLog = 0;
        }
    }

    /// <summary>每次点击/触摸：位置、当时鼠标下面是哪个界面控件（没有就是点在地图上）。</summary>
    public override void _Input(InputEvent e)
    {
        if (e is InputEventScreenTouch { Pressed: true } t)
            Write("触摸", $"窗口坐标 {t.Position} 画布坐标 {GetViewport().GetScreenTransform().AffineInverse() * t.Position}");
        else if (e is InputEventMouseButton { Pressed: true } m)
        {
            var hover = GetViewport().GuiGetHoveredControl();
            Write("点击", $"画布坐标 {m.Position} 按钮 {m.ButtonIndex} 控件 {(hover == null ? "（地图）" : Describe(hover))}");
        }
    }

    private static string Describe(Control c)
    {
        var parts = new List<string>();
        for (Node? n = c; n != null && parts.Count < 4; n = n.GetParent())
            parts.Add(n is TextureButton or Button ? $"{n.GetType().Name}" : n.Name.ToString());
        return string.Join("<", parts);
    }
}

/// <summary>画面右下角的帧数（只有配置 show_fps = 1 时才加）。</summary>
public partial class FpsOverlay : CanvasLayer
{
    private Label _l = null!;
    public override void _Ready()
    {
        Layer = 100;
        _l = new Label { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -90, OffsetTop = -22, MouseFilter = Control.MouseFilterEnum.Ignore };
        _l.AddThemeColorOverride("font_color", new Color(0.4f, 1, 0.4f));
        AddChild(_l);
    }
    public override void _Process(double delta) => _l.Text = $"{Engine.GetFramesPerSecond()} fps";
}
