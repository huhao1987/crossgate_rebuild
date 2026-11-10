using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 商店买/卖列表（服务端 WN 类型 6 = 买、7 = 卖）。
/// 每行：道具图标、名字、单价/数量；点一行选中，"＋/－"改数量，"确定"一次提交全部选中的。
/// 外观沿用官方对话框（9 宫格边框 + 半透明底 + 黄色文字按钮）；具体排版待对照官方截图再校。
/// </summary>
public partial class ShopPanel : CanvasLayer
{
    /// <summary>提交：buy:序号:数量,… / sell:格子:数量,… / cancel。</summary>
    [Signal] public delegate void SubmitEventHandler(string answer);
    public TextureCache? Cache;
    private Control _win = null!;
    private DialogPage? _page;
    private int[] _qty = System.Array.Empty<int>();
    private Label _total = null!;
    private readonly List<Label> _qtyLabels = new();
    private const int RowH = 34, W = 448;

    public override void _Ready()
    {
        Layer = 20;
        _win = new Control { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f };
        AddChild(_win);
        Visible = false;
    }

    public long Gold { get; set; }

    private static Label Text(string t, Color c, int size = 12)
    {
        var l = new Label { Text = t, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_shadow_color", Colors.Black);
        l.AddThemeConstantOverride("shadow_offset_x", 1); l.AddThemeConstantOverride("shadow_offset_y", 1);
        l.AddThemeFontOverride("font", CgFont.Make());
        return l;
    }

    private static Button TextButton(string t, Action a)
    {
        var b = new Button { Text = t, Flat = true, CustomMinimumSize = new Vector2(24, 20) };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.1f));
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.Pressed += a;
        return b;
    }

    public void ShowPage(DialogPage? page)
    {
        if (page == null || page.Shop == null) { Visible = false; _page = null; return; }
        _page = page;
        foreach (var c in _win.GetChildren()) c.QueueFree();
        _qtyLabels.Clear();
        var list = page.Shop;
        _qty = new int[list.Count];
        bool buy = page.Kind == "buy";
        int rows = Math.Min(list.Count, 8);
        float h = 30 + 2 * 19 + rows * RowH + 60;
        _win.OffsetLeft = -W / 2f; _win.OffsetRight = W / 2f; _win.OffsetTop = -h / 2; _win.OffsetBottom = h / 2;
        _win.AddChild(new Control { Size = new Vector2(W, h), MouseFilter = Control.MouseFilterEnum.Stop });
        _win.AddChild(CgFrame.Tint(new Vector2(W, h)));
        if (Cache != null) _win.AddChild(new CgFrame { Cache = Cache, Size = new Vector2(W, h), MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });

        var title = Text(page.NpcName, new Color(1f, 0.9f, 0.1f)); title.Position = new Vector2(16, 12); _win.AddChild(title);
        var say = Text(page.Text, Colors.White); say.Position = new Vector2(16, 31); _win.AddChild(say);

        var scroll = new ScrollContainer { Position = new Vector2(10, 56), Size = new Vector2(W - 20, rows * RowH), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(vb); _win.AddChild(scroll);
        for (int i = 0; i < list.Count; i++)
        {
            int idx = i; var e = list[i];
            var row = new Control { CustomMinimumSize = new Vector2(W - 34, RowH), MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = e.Desc };
            var box = new ReferenceRect { BorderColor = new Color(0.1f, 0.8f, 0.1f), BorderWidth = 1, EditorOnly = false, Size = row.CustomMinimumSize, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddChild(box);
            row.MouseEntered += () => box.Visible = true; row.MouseExited += () => box.Visible = false;
            if (Cache?.ImageNo(e.Image) is (ImageTexture tex, _))
            {
                var icon = new TextureRect { Texture = tex, Position = new Vector2(2, 1), Size = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(icon);
            }
            var name = Text(e.Name + (buy ? "" : $"  ×{e.Count}"), Colors.White); name.Position = new Vector2(40, 9); row.AddChild(name);
            var price = Text($"{e.Price} G", new Color(1f, 0.9f, 0.1f)); price.Position = new Vector2(250, 9); row.AddChild(price);
            var minus = TextButton("－", () => Change(idx, -1)); minus.Position = new Vector2(320, 6); row.AddChild(minus);
            var q = Text("0", Colors.White); q.Position = new Vector2(350, 9); row.AddChild(q); _qtyLabels.Add(q);
            var plus = TextButton("＋", () => Change(idx, +1)); plus.Position = new Vector2(372, 6); row.AddChild(plus);
            row.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && mb.Position.X < 310) Change(idx, +1); };
            vb.AddChild(row);
        }
        _total = Text("", Colors.White); _total.Position = new Vector2(16, h - 54); _win.AddChild(_total);
        var bx = new HBoxContainer { Position = new Vector2(0, h - 30), Size = new Vector2(W, 20), Alignment = BoxContainer.AlignmentMode.Center };
        bx.AddThemeConstantOverride("separation", 40);
        bx.AddChild(TextButton("确定", Confirm));
        bx.AddChild(TextButton("取消", () => EmitSignal(SignalName.Submit, "cancel")));
        _win.AddChild(bx);
        UpdateTotal();
        Visible = true;
    }

    private void Change(int i, int d)
    {
        if (_page?.Shop == null) return;
        var e = _page.Shop[i];
        int max = _page.Kind == "buy" ? e.Max : Math.Min(e.Count, e.Max);   // 卖：服务端给的可卖数量（0 = 这家店不收）
        int was = _qty[i];
        _qty[i] = Math.Clamp(_qty[i] + d, 0, max);
        _qtyLabels[i].Text = _qty[i].ToString();
        UpdateTotal();
        // 官方提示（实测）：到上限再按▲ "无法再增加。"，到 0 再按▼ "无法再减少。"
        if (_qty[i] == was) { Hint = d > 0 ? "无法再增加。" : "无法再减少。"; _total.Text = Hint; }
    }

    public string Hint { get; private set; } = "";
    /// <summary>测试用：按某一件的 ▲/▼。</summary>
    public void TestChange(int i, int d) => Change(i, d);

    private long Total() => _page?.Shop == null ? 0 : _page.Shop.Select((e, i) => (long)e.Price * _qty[i]).Sum();

    private void UpdateTotal()
    {
        if (_page == null) return;
        _total.Text = _page.Kind == "buy" ? $"合计 {Total()} G    持有 {Gold} G" : $"合计 {Total()} G";
        _total.AddThemeColorOverride("font_color", _page.Kind == "buy" && Total() > Gold ? new Color(1, 0.3f, 0.3f) : Colors.White);
    }

    private void Confirm()
    {
        if (_page?.Shop == null) return;
        var sel = _page.Shop.Select((e, i) => (e, i)).Where(x => _qty[x.i] > 0).ToList();
        if (sel.Count == 0) { EmitSignal(SignalName.Submit, "cancel"); return; }
        EmitSignal(SignalName.Submit, _page.Kind == "buy"
            ? "buy:" + string.Join(",", sel.Select(x => $"{x.i}:{_qty[x.i]}"))
            : "sell:" + string.Join(",", sel.Select(x => $"{x.e.Slot}:{_qty[x.i]}")));
    }
}

/// <summary>
/// 商店入口（服务端 BZ 种类 5，官方 exe 0x41efee 读、0x4236a9 画）：背景 23050（292×260），画在 ((W−256)/2, (H−312)/2)（图本身往左上偏 12,3）。
///   标题 = NPC 名字，中心 x 145、y 8；NPC 站在 (43,134)；对话框里的话按 26 字节折行，(97, 44+21k)，最多 5 行；
///   选项「 想  买 」「 想  卖 」「不 必 了」在 (170, 157+24k)，第 i 项在 掩码 & i 非 0 时才有；
///   点想买 / 想卖 → xD … 0 "1"/"2"（音效 79）；点不必了 / × → 关掉（音效 57），不发包。
/// （下面的坐标是相对 (352,204) 的官方坐标；窗口节点放在 (340,201)，所以统一 +12,+3。）
/// </summary>
public partial class ShopMenuWindow : CgWindow
{
    public Action<int>? Choose;   // 0 想买 / 1 想卖 / 2 不必了
    public int StandAct;
    private DialogPage? _page;
    public DialogPage? Page => _page;
    private const float Ox = 12, Oy = 3;

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(340, 201), false); }

    public void ShowPage(DialogPage? p)
    {
        _page = p?.Kind is "shop" or "skillmenu" or "trademenu" or "givemenu" ? p : null;
        Visible = _page != null;
        if (Visible) Refresh();
    }

    public override void Refresh()
    {
        Clear();
        if (_page is not { } p) return;
        Img(23050, 0, 0, block: true);
        Txt(p.NpcName, 145 + Ox, 8 + Oy + 6, Colors.White, center: true);
        NpcFigure.Add(Body, Cache, new Vector2(43 + Ox, 134 + Oy), p.Face, StandAct, 3);
        var lines = Wrap(p.Text.Replace("\n", ""), 20).Take(5).ToList();   // 官方一行 20 字节（[0x119e9f4]=0x14；实拍交换银"有银的人我可以帮你弄／成银条喔!"）
        for (int k = 0; k < lines.Count; k++) Txt(lines[k], 97 + Ox, 44 + 21 * k + Oy + 6, Colors.White);
        // 同一个背景的三种入口：商店（BZ 5，0x423681）、技能导师（BZ 16，0x425e94：字 (150,157+24k)，点的范围 x 140–260）、
        // 交易入口（BZ 27，0x428d82：两项 (170,177+24k)）
        var (labels, x0, y0) = p.Kind switch
        {
            "skillmenu" => (new[] { " 想学习技能 ", " 想忘记技能 ", "  还是算了 " }, 150f, 157f),
            "trademenu" => (new[] { "想 交 易", " 没  事 " }, 170f, 177f),
            "givemenu" => (new[] { " 交出物品 ", "  取  消  " }, 170f, 177f),   // BZ 30，exe 0x42a7d0
            _ => (new[] { " 想  买 ", " 想  卖 ", "不 必 了" }, 170f, 157f),
        };
        var opts = p.Options ?? new List<string>();
        for (int i = 0; i < labels.Length; i++)
        {
            if (i >= opts.Count || opts[i].Length == 0) continue;
            int ii = i; float y = y0 + 24 * i + Oy;
            Txt(labels[i], x0 + Ox, y + 6, Colors.White);
            float w = labels[i].Sum(CgText.Cells) * CgText.CellW;
            if (p.Kind == "skillmenu") Hit(140 + Ox, y - 3, 120, 23, _ => Pick(ii), box: false);
            else Hit(x0 + Ox - 2, y, w + 4, 20, _ => Pick(ii), box: false);
        }
        Btn(22509, 22510, 280, 8, () => Pick(labels.Length - 1));
    }

    public void Pick(int i)
    {
        // 最后一项（不必了 / 还是算了）音效 57；交易入口两项都是 79（exe 0x428eb4）
        GameAudio.Se(_page?.Kind is not ("trademenu" or "givemenu") && i == 2 ? 57 : 79);
        Visible = false;
        Choose?.Invoke(i);
    }

    /// <summary>按 GBK 字节宽度折行（官方 0x40f581：26 字节一行）。</summary>
    public static IEnumerable<string> Wrap(string s, int width)
    {
        var sb = new System.Text.StringBuilder(); int n = 0;
        foreach (var ch in s)
        {
            int w = ch < 0x80 ? 1 : 2;
            if (n + w > width) { yield return sb.ToString(); sb.Clear(); n = 0; }
            sb.Append(ch); n += w;
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
