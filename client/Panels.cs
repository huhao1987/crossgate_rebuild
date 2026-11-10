using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 对话框，照官方窗口做：棕色边框（图档 22620–22628），中间半透明压暗（能看到后面的地图），
/// 12 像素白字带投影、行距约 19 像素；选择列表的选项鼠标移上去有绿色框，按钮是黄色文字。
/// </summary>
public partial class DialogPanel : CanvasLayer
{
    [Signal] public delegate void ButtonPressedEventHandler(string button);
    public TextureCache? Cache;
    private Control _win = null!;
    private VBoxContainer _lines = null!;
    private HBoxContainer _buttons = null!;
    private const int LineH = 19;
    /// <summary>输入窗口的输入框（不是输入窗口时为 null）。</summary>
    public LineEdit? InputBox { get; private set; }
    public LineEdit? InputBox2 { get; private set; }

    public override void _Ready()
    {
        Layer = 20;
        _win = new Control { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f };
        AddChild(_win);
        Visible = false;
    }

    private static string Label(string b) => b switch { "next" => "下一页", "prev" => "上一页", "yes" => "是", "no" => "否", "ok" => "确 定", "cancel" => "取 消", _ => b };

    private static Label Text(string t, Color c)
    {
        var l = new Label { Text = t, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_shadow_color", Colors.Black);
        l.AddThemeConstantOverride("shadow_offset_x", 1); l.AddThemeConstantOverride("shadow_offset_y", 1);
        l.AddThemeConstantOverride("line_spacing", LineH - 16);
        l.AddThemeFontOverride("font", CgFont.Make());
        return l;
    }

    /// <summary>
    /// 布局照官方截图量（960×720 画面）：窗口 448×240、水平居中、比画面中心高 12 像素；
    /// 文字原点 (14,18)，等宽格子排（CgText）；按钮黄色格子字，n 个按钮的中心在宽度的 (i+1)/(n+1) 处，字顶在 209。
    /// 文字多了窗口往下长。
    /// </summary>
    public void ShowPage(string npcName, DialogPage? page)
    {
        if (page == null) { Visible = false; return; }
        foreach (var c in _win.GetChildren()) c.QueueFree();
        var text = page.Text.Replace("\\n", "\n");
        var body = new CgText { Position = new Vector2(14, 18) };
        body.Text = text;
        int textLines = text.Length == 0 ? 0 : body.LineCount;
        int optLines = page.Options?.Count ?? 0;
        float w = 448, h = Math.Max(240, 18 + (textLines + optLines + (page.Input ? 1 : 0)) * LineH + 52);
        _win.OffsetLeft = -w / 2; _win.OffsetRight = w / 2; _win.OffsetTop = -120 - 12; _win.OffsetBottom = h - 120 - 12;
        _win.AddChild(new Control { Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Stop });   // 挡住点击，不让点到地图
        _win.AddChild(CgFrame.Tint(new Vector2(w, h)));
        if (Cache != null) _win.AddChild(new CgFrame { Cache = Cache, Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        float y = 18;
        if (textLines > 0) { _win.AddChild(body); y += textLines * LineH; }
        // 输入窗口（BZ 11）：文字下面一行输入框
        InputBox = null; InputBox2 = null;
        if (page.Input2)
        {
            LineEdit Box(float by)
            {
                var sz = new Vector2(24 * CgText.CellW + 8, 20);
                _win.AddChild(new ColorRect { Position = new Vector2(120, by), Size = sz, Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Ignore });
                var b = new LineEdit { Position = new Vector2(120, by), Size = sz, MaxLength = 12, Flat = true };
                b.AddThemeColorOverride("font_color", Colors.White);
                var clear = new StyleBoxEmpty();
                b.AddThemeStyleboxOverride("normal", clear); b.AddThemeStyleboxOverride("focus", clear);
                _win.AddChild(b);
                return b;
            }
            InputBox = Box(102); InputBox2 = Box(164);
            InputBox.CallDeferred(Control.MethodName.GrabFocus);
        }
        if (page.Input && !page.Input2)
        {
            InputBox = new LineEdit { Position = new Vector2(14, y), Size = new Vector2(w - 28, 20), MaxLength = 60, Flat = true };
            InputBox.AddThemeColorOverride("font_color", Colors.White);
            InputBox.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.35f) });
            InputBox.AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.35f) });
            _win.AddChild(InputBox); y += LineH + 4;
            InputBox.CallDeferred(Control.MethodName.GrabFocus);
        }
        for (int i = 0; i < optLines; i++)
        {
            int idx = i;
            // 官方实测：悬停框纯绿 2px，x 13 … w-15，从字原点上 3px 开始、高 17
            var row = new Control { Position = new Vector2(13, y - 3), Size = new Vector2(w - 28, 17), MouseFilter = Control.MouseFilterEnum.Stop };
            var box = new GreenBox { Size = row.Size, Visible = false };
            row.AddChild(box);
            row.AddChild(new CgText { Text = page.Options![i], Position = new Vector2(1, 3) });
            row.MouseEntered += () => box.Visible = true; row.MouseExited += () => box.Visible = false;
            row.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) EmitSignal(SignalName.ButtonPressed, "opt:" + idx); };
            _win.AddChild(row); y += LineH;
        }
        int n = page.Buttons.Count;
        for (int i = 0; i < n; i++)
        {
            // 按钮是黄色格子字（不是图片）："确 定""取 消"中间有半角空格，"上一页""下一页"没有；
            // 第 i 个按钮的字以 w·(i+1)/(n+1) 为中心，字原点 y = 窗口底 - 33（240 高时 207，字顶 209）；悬停绿框比字宽 2px、高 18
            var name = page.Buttons[i];
            var label = Label(name);
            float tw = label.Sum(CgText.Cells) * CgText.CellW;
            float tx = w * (i + 1) / (n + 1) - tw / 2, ty = h - 33;
            var btn = new Control { Position = new Vector2(tx - 2.5f, ty - 3), Size = new Vector2(tw + 2, 18), MouseFilter = Control.MouseFilterEnum.Stop };
            var box = new GreenBox { Size = btn.Size, Visible = false };
            btn.AddChild(box);
            btn.AddChild(new CgText { Text = label, FontColor = new Color(1, 1, 0), Position = new Vector2(2.5f, 3) });
            btn.MouseEntered += () => box.Visible = true; btn.MouseExited += () => box.Visible = false;
            btn.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) EmitSignal(SignalName.ButtonPressed, name); };
            _win.AddChild(btn);
        }
        Visible = true;
    }
}

/// <summary>
/// 宠物窗口（底部菜单"宠物"打开）：列出宠物的名字、等级、HP、MP，以及 战斗/待命/休息/散步 四个状态。
/// 点状态按钮 → NetworkSession.SetPetState（KS 包，官方协议实测）。官方这个窗口的图档在现有客户端图档里缺，布局是自己排的。
/// </summary>
public partial class PetPanel : CanvasLayer
{
    public TextureCache? Cache;
    public Func<IReadOnlyList<PetInfo>> Pets = () => Array.Empty<PetInfo>();
    public Action<int, int>? SetState;   // 宠物栏、状态
    private Control _win = null!;
    private static readonly (int State, string Label)[] States = { (PetInfo.Battle, "战斗"), (PetInfo.Standby, "待命"), (PetInfo.Rest, "休息"), (PetInfo.Walk, "散步") };

    public override void _Ready()
    {
        Layer = 15;
        _win = new Control { Position = new Vector2(8, 70) };
        AddChild(_win);
        Visible = false;
    }
    public void Toggle() { Visible = !Visible; if (Visible) Refresh(); }
    public void Refresh()
    {
        if (!Visible) return;
        foreach (var c in _win.GetChildren()) c.QueueFree();
        var pets = Pets();
        const float W = 300, RowH = 58;
        float h = 34 + Math.Max(1, pets.Count) * RowH + 8;
        _win.AddChild(new Control { Size = new Vector2(W, h), MouseFilter = Control.MouseFilterEnum.Stop });
        _win.AddChild(CgFrame.Tint(new Vector2(W, h)));
        if (Cache != null) _win.AddChild(new CgFrame { Cache = Cache, Size = new Vector2(W, h), MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        _win.AddChild(new CgText { Text = "宠物", Position = new Vector2(14, 12), FontColor = new Color(1, 1, 0) });
        var close = new Control { Position = new Vector2(W - 44, 8), Size = new Vector2(34, 20), MouseFilter = Control.MouseFilterEnum.Stop };
        close.AddChild(new CgText { Text = "关闭", Position = new Vector2(0, 3), FontColor = new Color(1, 1, 0) });
        close.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Visible = false; };
        _win.AddChild(close);
        if (pets.Count == 0) { _win.AddChild(new CgText { Text = "没有宠物", Position = new Vector2(14, 40) }); return; }
        for (int i = 0; i < pets.Count; i++)
        {
            var p = pets[i]; float y = 34 + i * RowH;
            _win.AddChild(new CgText { Text = $"{p.DisplayName}", Position = new Vector2(14, y) });
            _win.AddChild(new CgText { Text = $"Lv{p.Level}", Position = new Vector2(150, y) });
            _win.AddChild(new CgText { Text = $"HP {p.Hp,4}/{p.MaxHp,4}  MP {p.Mp,4}/{p.MaxMp,4}", Position = new Vector2(14, y + 17), FontColor = new Color(0.85f, 0.85f, 0.85f) });
            for (int k = 0; k < States.Length; k++)
            {
                var (st, label) = States[k];
                int slot = p.Slot;
                var b = new Control { Position = new Vector2(14 + k * 52, y + 35), Size = new Vector2(46, 18), MouseFilter = Control.MouseFilterEnum.Stop };
                bool on = p.State == st;
                if (on) b.AddChild(new GreenBox { Size = b.Size });
                b.AddChild(new CgText { Text = label, Position = new Vector2(6, 3), FontColor = on ? new Color(0, 1, 0) : new Color(1, 1, 0) });
                b.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !on) SetState?.Invoke(slot, st); };
                _win.AddChild(b);
            }
        }
    }
}

/// <summary>官方的悬停框：纯绿 (0,255,0)、2 像素宽，画在控件边界内侧。</summary>
public partial class GreenBox : Control
{
    public GreenBox() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        var c = new Color(0, 1, 0); float W = Size.X, H = Size.Y;
        DrawRect(new Rect2(0, 0, W, 2), c); DrawRect(new Rect2(0, H - 2, W, 2), c);
        DrawRect(new Rect2(0, 0, 2, H), c); DrawRect(new Rect2(W - 2, 0, 2, H), c);
    }
}

/// <summary>背包：20 格，显示道具图标（道具表第 12 列的图像编号）和数量。</summary>
public partial class BagPanel : CanvasLayer
{
    public TextureCache Cache = null!;
    public IGameSession Session = null!;
    private GridContainer _grid = null!;
    private Label _info = null!;

    public override void _Ready()
    {
        Layer = 15;
        var panel = new Panel { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -360, OffsetRight = -12, OffsetTop = 60, OffsetBottom = 470 };
        AddChild(panel);
        panel.AddChild(new Label { Text = "背包", Position = new Vector2(14, 8) });
        _grid = new GridContainer { Columns = 5, Position = new Vector2(14, 36) };
        panel.AddChild(_grid);
        _info = new Label { Position = new Vector2(14, 360), Size = new Vector2(320, 40), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_info);
        Visible = false;
    }

    public void Toggle() { Visible = !Visible; if (Visible) Refresh(); }

    public void Refresh()
    {
        if (!Visible) return;
        foreach (var c in _grid.GetChildren()) c.QueueFree();
        var items = Session.Player.Items;
        // 联网模式道具带服务端格子号（8 起是背包）：按格子摆；单机模式按顺序摆
        bool bySlot = items.Count > 0 && items.All(x => x.Slot >= 8);
        for (int i = 0; i < PlayerState.BagSize; i++)
        {
            var slot = new Button { CustomMinimumSize = new Vector2(60, 60), ClipText = true };
            var st = bySlot ? items.FirstOrDefault(x => x.Slot - 8 == i) : (i < items.Count ? items[i] : null);
            if (st != null)
            {
                var info = Session.ItemInfo(st.ItemId);
                if (info != null && Cache.ImageNo(info.GraphicId) is (ImageTexture tex, _)) { slot.Icon = tex; slot.ExpandIcon = true; }
                slot.Text = st.Count > 1 ? st.Count.ToString() : "";
                var desc = $"{info?.Name ?? "道具"} #{st.ItemId} ×{st.Count}";
                slot.TooltipText = desc;
                slot.Pressed += () => _info.Text = desc;
            }
            _grid.AddChild(slot);
        }
        _info.Text = $"金钱 {Session.Player.Gold}    空位 {Session.Player.FreeSlots}";
    }
}

/// <summary>屏幕中央短暂显示的提示（战斗奖励、系统消息）。</summary>
/// <summary>
/// 聊天栏（左下角）：官方客户端"聊天设定 → 显示行数"默认 2 行，新消息从下往上顶。
/// 实测（官方 960×720 画面）：最新一行字形顶部 y=654，上一行 636（行距 18），字形左边 x=11；与对话框同一套 8.5px/字节 的格子字。
/// 颜色编号来自服务端 TK 包：0 白、1 青（实测 0,255,255）；其余按官方调色板顺序。
/// </summary>
public partial class ChatLog : CanvasLayer
{
    public static readonly Color[] Palette =
    {
        new(1, 1, 1), new(0, 1, 1), new(1, 0, 1), new(0, 0, 1), new(1, 1, 0),
        new(0, 1, 0), new(1, 0, 0), new(0.63f, 0.63f, 0.63f), new(0, 0.5f, 1), new(0.5f, 1, 0.5f),
    };
    public int MaxLines = 2;
    public const int Line = 18, LastTop = 652, Left = 8;
    private readonly List<(string Text, Color Color)> _lines = new();
    private Control _root = null!;

    public override void _Ready()
    {
        Layer = 5;
        _root = new Control { AnchorTop = 1, AnchorBottom = 1, OffsetTop = -720, OffsetBottom = 0, OffsetRight = 960, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
    }

    public void Add(string text, int color)
    {
        var c = color >= 0 && color < Palette.Length ? Palette[color] : Colors.White;
        foreach (var l in CgText.Layout(text, 110)) _lines.Add((l, c));
        while (_lines.Count > MaxLines) _lines.RemoveAt(0);
        foreach (var ch in _root.GetChildren()) ch.QueueFree();
        for (int i = 0; i < _lines.Count; i++)
        {
            var t = new CgText { Wrap = 999, Emoji = true, FontColor = _lines[i].Color, Position = new Vector2(Left, LastTop - (_lines.Count - 1 - i) * Line) };
            t.Text = _lines[i].Text;
            _root.AddChild(t);
        }
    }

    public IReadOnlyList<string> Lines => _lines.Select(l => l.Text).ToList();
}

/// <summary>
/// 聊天输入（官方实测）：没有输入框，直接打字，绿色 (0,255,0) 格子字出现在左下角底栏上方（字顶 y=681、x=10），回车发送、Esc 清空。
/// 用一个看不见文字的 LineEdit 接键盘和输入法（中文输入要靠它），自己用 CgText 画出来。手机上点左下角弹出键盘。
/// </summary>
public partial class ChatInput : CanvasLayer
{
    public Action<string>? Send;
    private LineEdit _edit = null!;
    private CgText _shown = null!;
    private ColorRect _caret = null!;
    private double _blink;

    public override void _Ready()
    {
        Layer = 6;
        var root = new Control { AnchorTop = 1, AnchorBottom = 1, OffsetTop = -720, OffsetBottom = 0, OffsetRight = 960, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        _edit = new LineEdit { Position = new Vector2(6, 674), Size = new Vector2(620, 20), Flat = true, CaretBlink = false, ContextMenuEnabled = false, MaxLength = 120 };
        if (ShowButton)
        {
            // 手机：输入行不接触摸、获得焦点也不自己弹键盘，只有点「聊天」按钮才弹（以前点底部菜单会误弹键盘）
            _edit.MouseFilter = Control.MouseFilterEnum.Ignore;
            _edit.VirtualKeyboardEnabled = false;
        }
        var clear = new Color(0, 0, 0, 0);
        _edit.AddThemeColorOverride("font_color", clear); _edit.AddThemeColorOverride("caret_color", clear);
        _edit.AddThemeColorOverride("selection_color", clear); _edit.AddThemeColorOverride("font_selected_color", clear);
        _edit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _edit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _edit.TextSubmitted += t =>
        {
            if (_emojiPanel != null) { _emojiPanel.QueueFree(); _emojiPanel = null; }
            if (t.Trim().Length > 0) Send?.Invoke(t.Trim());
            _edit.Clear(); _edit.ReleaseFocus(); Redraw();
            DisplayServer.VirtualKeyboardHide();
            if (_button != null) _button.Text = "聊天";
        };
        _edit.TextChanged += _ => Redraw();
        root.AddChild(_edit);
        _shown = new CgText { Wrap = 999, FontColor = new Color(0, 1, 0), Position = new Vector2(10, 681 - 5) };
        root.AddChild(_shown);
        _caret = new ColorRect { Color = new Color(0, 1, 0), Size = new Vector2(2, 12), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_caret);
        _root = root;
        if (ShowButton)
        {
            // 手机模式：输入行右边一个看得见的「聊天」按钮，点了弹出手机键盘（官方是直接用键盘打字，手机上没有键盘）
            // 放在画面右下角（底栏上面），离左下的底部菜单远远的
            var btn = new Button { Text = "聊天", FocusMode = Control.FocusModeEnum.None,
                AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -84, OffsetRight = -8, OffsetTop = -66, OffsetBottom = -30 };
            btn.AddThemeFontSizeOverride("font_size", 16);
            btn.Pressed += () => { if (_edit.HasFocus()) Submit(); else Start(); };
            var full = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(full);
            full.AddChild(btn);
            _button = btn;
            // 手机上打 %字母 不方便：「表情」按钮弹出 36 个表情（exe PNG 103），点一个就把 %字母 插进输入行（官方没有这个面板）
            var emo = new Button { Text = "表情", FocusMode = Control.FocusModeEnum.None,
                AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -164, OffsetRight = -90, OffsetTop = -66, OffsetBottom = -30 };
            emo.AddThemeFontSizeOverride("font_size", 16);
            emo.Pressed += ToggleEmoji;
            full.AddChild(emo);
            _emojiParent = full;
        }
    }

    private Control? _emojiParent, _emojiPanel;
    public bool EmojiOpen => _emojiPanel != null;
    /// <summary>表情面板：10 列 × 4 行，每格 36×36（18×18 的图放大 2 倍），在「聊天」按钮上面。</summary>
    public void ToggleEmoji()
    {
        if (_emojiPanel != null) { _emojiPanel.QueueFree(); _emojiPanel = null; return; }
        if (_emojiParent == null || ExeArt.Png(103) is not { } sheet) return;
        int n = sheet.GetWidth() / 18 * (sheet.GetHeight() / 18);
        n = Math.Min(n, 52);
        int rows = (n + 9) / 10;
        var panel = new Panel { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -8 - 10 * 38 - 8, OffsetRight = -8, OffsetTop = -72 - rows * 38 - 8, OffsetBottom = -72 };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0.2f, 0.85f), BorderColor = new Color(0.85f, 0.7f, 0.4f), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
        for (int i = 0; i < n; i++)
        {
            var at = new AtlasTexture { Atlas = sheet, Region = new Rect2(18 * (i % 10), 18 * (i / 10), 18, 18) };
            var b = new TextureButton { TextureNormal = at, IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered, Position = new Vector2(8 + 38 * (i % 10), 8 + 38 * (i / 10)), Size = new Vector2(36, 36), FocusMode = Control.FocusModeEnum.None };
            char code = i < 26 ? (char)('A' + i) : (char)('a' + i - 26);
            b.Pressed += () => InsertEmoji(code);
            panel.AddChild(b);
        }
        _emojiParent.AddChild(panel);
        _emojiPanel = panel;
    }
    public void InsertEmoji(char code)
    {
        if (!_edit.HasFocus()) _edit.GrabFocus();
        _edit.InsertTextAtCaret("%" + code);
        Redraw();
        if (_button != null) _button.Text = "发送";
    }
    /// <summary>手机模式显示「聊天」按钮。</summary>
    public bool ShowButton;
    private Control _root = null!;
    private Button? _button;

    public void Start()
    {
        _edit.GrabFocus();
        DisplayServer.VirtualKeyboardShow(_edit.Text, new Rect2(_edit.GlobalPosition, _edit.Size), DisplayServer.VirtualKeyboardType.Default, 120);
        if (_button != null) _button.Text = "发送";
    }

    private void Submit() => _edit.EmitSignal(LineEdit.SignalName.TextSubmitted, _edit.Text);

    public bool Typing => _edit.HasFocus();

    /// <summary>主画面没被别的界面吃掉的按键：可打印字符就开始输入（官方直接打字就进聊天）。</summary>
    public bool TryStart(InputEventKey k)
    {
        if (!k.Pressed || _edit.HasFocus() || k.CtrlPressed || k.AltPressed || k.MetaPressed) return false;
        if (k.Keycode == Key.Enter) { _edit.GrabFocus(); return true; }
        if (k.Unicode < 32) return false;
        _edit.GrabFocus();
        _edit.InsertTextAtCaret(char.ConvertFromUtf32((int)k.Unicode));
        return true;
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape } && _edit.HasFocus()) { _edit.Clear(); _edit.ReleaseFocus(); Redraw(); GetViewport().SetInputAsHandled(); }
        // 点输入行弹键盘：只在原版模式（手机模式只用「聊天」按钮，输入行紧贴底部菜单，手指一点就误触）
        if (!ShowButton && e is InputEventScreenTouch { Pressed: true } t && !_edit.HasFocus())
        {
            var p = GetViewport().GetScreenTransform().AffineInverse() * t.Position;
            var line = new Rect2(_root.GlobalPosition + new Vector2(0, 664), new Vector2(626, 32));
            if (line.HasPoint(p)) Start();
        }
    }

    private void Redraw()
    {
        var text = _edit.Text;
        _shown.Text = text;
        _caret.Position = new Vector2(10 + text.Sum(CgText.Cells) * CgText.CellW + 1, 680);
    }

    public override void _Process(double delta)
    {
        if (!_edit.HasFocus()) { _caret.Visible = false; return; }
        _blink += delta;
        _caret.Visible = (int)(_blink * 2) % 2 == 0;
    }

    /// <summary>测试用：模拟输入一段文字并回车。</summary>
    public void TestType(string text) { _edit.GrabFocus(); _edit.InsertTextAtCaret(text); Redraw(); }
    public void TestEnter() => _edit.EmitSignal(LineEdit.SignalName.TextSubmitted, _edit.Text);
    public string Shown => _shown.Text;
    public Rect2? ButtonRect => _button?.GetGlobalRect();
    public void TestInsert(string text) { _edit.InsertTextAtCaret(text); Redraw(); }
}

public partial class Toast : CanvasLayer
{
    private Label _label = null!;
    private double _left;
    public override void _Ready()
    {
        Layer = 25;
        _label = new Label { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.3f, OffsetLeft = -400, OffsetRight = 400, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _label.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_label);
    }
    public void Show(string text, double seconds = 3) { _label.Text = text; _left = seconds; _label.Visible = true; }
    public override void _Process(double d) { if (_left > 0 && (_left -= d) <= 0) _label.Visible = false; }
}

/// <summary>
/// 动作浏览器（校准工具）：预览任意动画编号的每个动作和方向，把看到的动作号设为站立/行走/攻击/受击/倒下，
/// 以及调整方向偏移（让角色朝向和走路方向一致）。结果写回 user://config.json，下次启动生效（本次立即生效）。
/// </summary>
public partial class CalibrationPanel : CanvasLayer
{
    public TextureCache Cache = null!;
    public GameConfig Cfg = null!;
    public Action<string>? Notify;
    private AnimSprite _preview = null!;
    private SpinBox _anime = null!;
    private Label _info = null!;
    private int _act, _dir;

    public override void _Ready()
    {
        Layer = 18;
        var panel = new Panel { AnchorLeft = 0, AnchorRight = 0, OffsetLeft = 12, OffsetRight = 452, OffsetTop = 56, OffsetBottom = 700 };
        AddChild(panel);
        var box = new VBoxContainer { Position = new Vector2(14, 10), Size = new Vector2(412, 520) };
        panel.AddChild(box);
        box.AddChild(new Label { Text = "动作浏览器（校准）" });
        var row0 = new HBoxContainer(); box.AddChild(row0);
        row0.AddChild(new Label { Text = "动画编号" });
        _anime = new SpinBox { MinValue = 0, MaxValue = 9_999_999, Value = Cfg.PlayerAnime, CustomMinimumSize = new Vector2(160, 0) };
        _anime.ValueChanged += v => { _preview.AnimeId = (int)v; _preview.ResetLoop(); Refresh(); };
        row0.AddChild(_anime);
        var stage = new Control { CustomMinimumSize = new Vector2(412, 180) };
        box.AddChild(stage);
        stage.AddChild(new ColorRect { Color = new Color(0.25f, 0.3f, 0.25f), Size = new Vector2(412, 180) });
        _preview = new AnimSprite { Cache = Cache, AnimeId = Cfg.PlayerAnime, Position = new Vector2(206, 150), Scale = Vector2.One * 1.5f };
        stage.AddChild(_preview);
        box.AddChild(Row(("动作 −", () => Step(ref _act, -1, 40)), ("动作 +", () => Step(ref _act, 1, 40)), ("方向 −", () => Step(ref _dir, -1, 8)), ("方向 +", () => Step(ref _dir, 1, 8))));
        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(412, 70) };
        box.AddChild(_info);
        box.AddChild(new Label { Text = "把当前动作设为：" });
        box.AddChild(Row(("站立", () => Set("act_stand", _act)), ("行走", () => Set("act_walk", _act)), ("攻击", () => Set("act_attack", _act))));
        box.AddChild(Row(("受击", () => Set("act_hurt", _act)), ("倒下", () => Set("act_dead", _act)), ("施法", () => Set("act_magic", _act))));
        box.AddChild(Row(("防御", () => Set("act_guard", _act)), ("关闭", () => Visible = false)));
        box.AddChild(new Label { Text = "方向偏移（人物走路朝向不对时调）：", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(Row(("偏移 −1", () => Set("dir_offset", (Cfg.DirOffset + 7) % 8)), ("偏移 +1", () => Set("dir_offset", (Cfg.DirOffset + 1) % 8))));
        Visible = false;
        Refresh();
    }

    private HBoxContainer Row(params (string Text, Action Act)[] buttons)
    {
        var r = new HBoxContainer();
        foreach (var (t, a) in buttons)
        {
            var b = new Button { Text = t, CustomMinimumSize = new Vector2(96, 44) };
            b.Pressed += a; r.AddChild(b);
        }
        return r;
    }

    private void Step(ref int v, int d, int mod) { v = ((v + d) % mod + mod) % mod; _preview.ResetLoop(); Refresh(); }

    public void Toggle() { Visible = !Visible; if (Visible) Refresh(); }

    /// <summary>当前动画所有存在的 (方向, 动作)。</summary>
    public List<int> ActsOf(int anime) => Cache.Library.AnimeActions(anime).Keys.Select(k => k.Act).Distinct().OrderBy(a => a).ToList();

    private void Refresh()
    {
        if (_preview == null) return;
        _preview.Dir = _dir; _preview.Act = _act; _preview.FallbackAct = _act;
        var acts = Cache.Library.AnimeActions(_preview.AnimeId);
        string cur = acts.TryGetValue((_dir, _act), out var a) ? $"{a.Frames.Length} 帧 / {a.DurationMs} 毫秒" : "（该方向没有这个动作）";
        if (!acts.ContainsKey((_dir, _act))) _preview.Texture = null;
        _info.Text = $"动作 {_act}  方向 {_dir}  {cur}\n此动画的动作号：{string.Join(",", ActsOf(_preview.AnimeId))}\n" +
                     $"当前设置：站立{Cfg.ActStand} 行走{Cfg.ActWalk} 攻击{Cfg.ActAttack} 受击{Cfg.ActHurt} 倒下{Cfg.ActDead} 施法{Cfg.ActMagic} 防御{Cfg.ActGuard} 偏移{Cfg.DirOffset}";
    }

    private void Set(string key, int value)
    {
        switch (key)
        {
            case "act_stand": Cfg.ActStand = value; break;
            case "act_walk": Cfg.ActWalk = value; break;
            case "act_attack": Cfg.ActAttack = value; break;
            case "act_hurt": Cfg.ActHurt = value; break;
            case "act_dead": Cfg.ActDead = value; break;
            case "act_magic": Cfg.ActMagic = value; break;
            case "act_guard": Cfg.ActGuard = value; break;
            case "dir_offset": Cfg.DirOffset = value; break;
        }
        const string path = "user://config.json";
        string msg = $"{key} = {value}";
        if (global::Godot.FileAccess.FileExists(path))
        {
            var o = System.Text.Json.Nodes.JsonNode.Parse(global::Godot.FileAccess.GetFileAsString(path))!.AsObject();
            o[key] = value; o["acts_v"] = 3;
            using var f = global::Godot.FileAccess.Open(path, global::Godot.FileAccess.ModeFlags.Write);
            f.StoreString(o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            msg += "（已保存）";
        }
        else msg += "（本次有效；没有 user://config.json，未保存）";
        Notify?.Invoke(msg);
        Refresh();
    }
}
