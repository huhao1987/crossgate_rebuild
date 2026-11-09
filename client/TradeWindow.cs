using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// "请选择对象"小窗（右上「交易」后服务端回了可交易名单时弹出）：棕色边框，黄字标题，名字一行一个，底下「取消」。
/// 位置、大小按官方截图量（960×720 画面里 (320,254) 起 317×187）。
/// </summary>
public partial class TradePicker : CanvasLayer
{
    public TextureCache Cache = null!;
    public Action<int>? Picked;
    private Control _win = null!;

    public override void _Ready() { Layer = 21; Visible = false; }

    public void Show(IReadOnlyList<string> names)
    {
        foreach (var c in GetChildren()) c.QueueFree();
        _win = new Control { Position = new Vector2(320, 254), Size = new Vector2(317, 187) };
        AddChild(_win);
        _win.AddChild(new Control { Size = _win.Size, MouseFilter = Control.MouseFilterEnum.Stop });   // 挡住点击
        _win.AddChild(CgFrame.Tint(_win.Size));
        _win.AddChild(new CgFrame { Cache = Cache, Size = _win.Size, MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        Label(_win, "请选择对象", 158, 22, new Color(1, 1, 0), center: true);
        for (int i = 0; i < names.Count && i < 5; i++)
        {
            int k = i;
            var hit = new Control { Position = new Vector2(70, 50 + 22 * i), Size = new Vector2(180, 20), MouseFilter = Control.MouseFilterEnum.Stop };
            var box = new GreenBox { Size = hit.Size, Visible = false }; hit.AddChild(box);
            hit.MouseEntered += () => box.Visible = true; hit.MouseExited += () => box.Visible = false;
            hit.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Visible = false; Picked?.Invoke(k); } };
            _win.AddChild(hit);
            Label(_win, names[i], 86, 53 + 22 * i, Colors.White);
        }
        var cancel = new Control { Position = new Vector2(128, 158), Size = new Vector2(60, 20), MouseFilter = Control.MouseFilterEnum.Stop };
        cancel.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Visible = false; };
        _win.AddChild(cancel);
        Label(_win, "取  消", 158, 162, new Color(1, 1, 0), center: true);
        Visible = true;
    }

    private static void Label(Control parent, string s, float x, float y, Color c, bool center = false)
    {
        var t = new CgText { Wrap = 999, FontColor = c, Text = s };
        float w = s.Sum(CgText.Cells) * CgText.CellW;
        t.Position = new Vector2(center ? x - w / 2 : x, y - 2);
        parent.AddChild(t);
    }

    public void TestPick(int i) { Visible = false; Picked?.Invoke(i); }
}

/// <summary>
/// 交易窗口（官方 23230，560×364，在 (200,148)）。照官方客户端实测：
///  左边是对方（名字、个/只/G 计数、物品/宠物/金币三个标签：23231/23233/23234），右边是自己（23241/23243/23244）。
///  自己的物品页显示自己背包 20 格，双击选上（黄框），「開啓」(23264) 亮出去；对方开启后状态栏从红字"选取交易物品中"变白字"对方交易物品已选取"。
///  「確認交易」(23270) 发确认，「取消」(23272) 中止。自己的金币页：金币余额、交易金额、0–9 小键盘、更正/全部/消除。
/// </summary>
public partial class TradeWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    private int _theirTab, _myTab;
    private readonly SortedSet<int> _picked = new();
    private readonly SortedSet<int> _pickedPets = new();
    public IReadOnlyCollection<int> PickedPets => _pickedPets;
    private string _goldInput = "";
    private long _lastClick; private int _lastSlot = -1;

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(200, 148), false); }

    public void Open() { _picked.Clear(); _pickedPets.Clear(); _goldInput = ""; _theirTab = 0; _myTab = 0; Visible = true; Refresh(); }

    public IReadOnlyCollection<int> Picked => _picked;
    public int GoldInput => int.TryParse(_goldInput, out var g) ? g : 0;
    public string StatusText { get; private set; } = "";

    public override void Refresh()
    {
        Clear();
        var s = Session();
        var t = s?.Trade;
        if (t == null) { Visible = false; return; }
        Img(23230, 0, 0, block: true);
        Txt(t.Partner, 130, 12, center: true);
        Txt(s!.Player.Name, 432, 12, center: true);
        // 计数：个（道具件数）、只（宠物）、G
        Counter(t.TheirItems.Count, "个", 50); Counter(t.TheirPets.Count, "只", 140); Counter(t.TheirGold, "G", 230);
        Counter(_picked.Count, "个", 329); Counter(_pickedPets.Count, "只", 419); Counter(GoldInput, "G", 509);
        // 对方
        switch (_theirTab)
        {
            case 0:
                Img(23231, 4, 58);
                foreach (var it in t.TheirItems) if (it.Slot is >= 0 and < 20) Icon(it, SlotPos(4, it.Slot));
                break;
            case 1:   // 寵物：5 行（横线 y = 140+22k，名字在线上方居中，同银行），对方放上来的宠物（名字 + 等级）
                Img(23233, 4, 58);
                int row = 0;
                foreach (var tp in t.TheirPets.Values) { if (row >= 5) break; Txt($"{tp.DisplayName} Lv{tp.Level}", 147, 128 + 22 * row++, center: true); }
                break;
            case 2: Img(23234, 4, 58); Txt(t.TheirGold.ToString(), 248, 102, right: true); break;
        }
        for (int i = 0; i < 3; i++) { int k = i; Hit(8 + 88 * i, 60, 84, 20, _ => { _theirTab = k; Refresh(); }, box: false); }
        // 自己
        switch (_myTab)
        {
            case 0:
                Img(23241, 283, 58);
                var bag = s.Player.Items.Where(x => x.Slot >= 8 && x.Slot < 28).ToList();
                foreach (var it in bag)
                {
                    int cell = it.Slot - 8;
                    var p = SlotPos(283, cell);
                    Icon(it, p);
                    if (_picked.Contains(it.Slot)) Body.AddChild(new YellowBox { Position = p, Size = new Vector2(48, 48) });
                }
                if (!t.IOpened)
                    for (int c = 0; c < 20; c++) { int slot = c + 8; var p = SlotPos(283, c); Hit(p.X, p.Y, 48, 48, _ => ClickSlot(slot)); }
                break;
            case 1:   // 自己的宠物：双击选上（黄字）/ 取消
                Img(23243, 283, 58);
                for (int k = 0; k < 5; k++)
                {
                    var mp = s.Player.Pets.FirstOrDefault(x => x.Slot == k);
                    float line = 140 + 22 * k;
                    if (mp == null) continue;
                    Txt($"{mp.DisplayName} Lv{mp.Level}", 425, line - 12, _pickedPets.Contains(k) ? new Color(1, 1, 0) : Colors.White, center: true);
                    int kk = k;
                    if (!t.IOpened) Hit(342, line - 20, 166, 20, _ => ClickPet(kk));
                }
                break;
            case 2:
                Img(23244, 283, 58);
                Txt(s.Player.Gold.ToString(), 493, 125, right: true);
                Txt(GoldInput.ToString(), 493, 163, right: true);
                if (!t.IOpened) Keypad();
                break;
        }
        for (int i = 0; i < 3; i++) { int k = i; Hit(287 + 88 * i, 60, 84, 20, _ => { _myTab = k; Refresh(); }, box: false); }
        // 状态栏
        StatusText = t.TheyOpened ? "对方交易物品已选取" : "选取交易物品中";
        Txt(StatusText, 140, 312, t.TheyOpened ? Colors.White : new Color(1, 0.2f, 0.2f), center: true);
        // 按钮
        if (t.IOpened) Img(23265, 376, 305);
        else Btn(23264, 23265, 376, 305, () => DoOpen());
        if (t.IConfirmed) Img(23271, 103, 334);
        else Btn(23270, 23271, 103, 334, () => DoConfirm());
        Btn(23272, 23273, 333, 334, () => s.CancelTrade());
        Btn(22509, 22510, 538, 6, () => s.CancelTrade());   // 右上角 X（官方窗口共用的关闭钮）
    }

    private void Counter(int n, string unit, float cx) => Txt($"{n} {unit}", cx, 41, center: true);

    /// <summary>格子左上角（相对窗口）：5 列 × 4 行，间距 52.5 × 52（官方截图量）。</summary>
    private static Vector2 SlotPos(float left, int cell) => new(left + 8 + MathF.Round(52.5f * (cell % 5)), 95 + 52 * (cell / 5));

    private void Icon(ItemStack it, Vector2 p)
    {
        if (it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } tex)
        {
            var sz = tex.GetSize();
            Body.AddChild(new TextureRect { Texture = tex, Position = p + (new Vector2(48, 48) - sz) / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        if (it.Count > 1) Txt(it.Count.ToString(), p.X + 46, p.Y + 34, new Color(0.4f, 1, 1), right: true);
    }

    private void ClickSlot(int slot)
    {
        long now = (long)Time.GetTicksMsec();
        bool dbl = slot == _lastSlot && now - _lastClick < 500;
        _lastClick = now; _lastSlot = slot;
        if (!dbl) return;
        if (Session()?.Player.Items.Any(x => x.Slot == slot) != true) return;
        if (!_picked.Remove(slot)) _picked.Add(slot);
        Refresh();
    }

    private long _petClick; private int _petLast = -1;
    private void ClickPet(int slot)
    {
        long now = (long)Time.GetTicksMsec();
        bool dbl = slot == _petLast && now - _petClick < 500;
        _petClick = now; _petLast = slot;
        if (!dbl) return;
        if (!_pickedPets.Remove(slot)) _pickedPets.Add(slot);
        Refresh();
    }

    private void Keypad()
    {
        for (int d = 0; d <= 9; d++)
        {
            int k = d;
            float x = 333 + 24 * (d % 5) + 283 - 283, y = d >= 5 ? 197 : 221;
            Hit(x + 0, y, 20, 20, _ => { if (_goldInput.Length < 9) { _goldInput = (_goldInput + k).TrimStart('0'); Refresh(); } });
        }
        Hit(454, 198, 52, 12, _ => { if (_goldInput.Length > 0) { _goldInput = _goldInput[..^1]; Refresh(); } });   // 更正：删一位
        Hit(454, 212, 52, 12, _ => { _goldInput = (Session()?.Player.Gold ?? 0).ToString(); Refresh(); });         // 全部
        Hit(454, 228, 52, 12, _ => { _goldInput = ""; Refresh(); });                                              // 消除
    }

    public void DoOpen()
    {
        var s = Session(); if (s?.Trade == null) return;
        int gold = (int)Math.Min(GoldInput, s.Player.Gold);
        s.OpenTrade(_picked, gold, _pickedPets);
    }
    public void DoConfirm() => Session()?.ConfirmTrade();

    // 测试用
    public void TestPickPet(int slot) { if (!_pickedPets.Remove(slot)) _pickedPets.Add(slot); Refresh(); }
    public void TestPick(int slot) { if (!_picked.Remove(slot)) _picked.Add(slot); Refresh(); }
    public void TestGold(int g) { _goldInput = g.ToString(); Refresh(); }
    public void TestTab(bool mine, int tab) { if (mine) _myTab = tab; else _theirTab = tab; Refresh(); }
}

/// <summary>选中道具的黄框（官方交易窗口里双击选上的道具）。</summary>
public partial class YellowBox : Control
{
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color(1, 0.9f, 0.1f), false, 2);
}
