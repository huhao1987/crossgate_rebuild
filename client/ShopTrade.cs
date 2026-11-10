using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 商店买 / 卖窗口（服务端 BZ 种类 6 / 7，官方 exe 买 0x42394f、卖 0x424480，格子 0x4768dc，数量钮 0x476748）。
/// 背景 23055（576×432）画在 ((W−576)/2, (H−456)/2)，下面坐标都相对窗口左上角，按钮坐标是中心：
///   标题 NPC 名字中心 (288,8)；右边对话框里的话（买：平时 / 钱不够 / 放不下 三句换着显示）(378, 48+21k) 最多 5 行；NPC 站 (468,244)。
///   ◀ 23022/23026 (147,52)、▶ 23023/23027 (226,52)，中间页码 "%d/%d" (173,44)，一页 10 件（两列，先排左列 5 件）。
///   格子：第 c 列第 r 行 cx = 20+176c、cy = 127+68r，道具图中心 (cx+24, cy−11)；数量 "%2d" (cx+64, cy−34)、价钱 "%8d" (cx+54, cy−7)；
///     ▲ 23020/23024 (cx+108, cy−27)、▼ 23021/23025 (cx+142, cy−27)（提示 增加/减少此物品数量。到头了 无法再增加/减少。）。
///   金币 "%9d" (440,285)、总额 (440,337)（买：钱不够时红字）。
///   买：「买入」23073（没选东西时 23074 灰）中心 (421,391)、「取消」23076 (516,391)；不能装备的画红叉 "×" (cx+37, cy+2)、价钱灰。
///       一次最多 min(120, 剩下的空格 × 一格能放几个)；点买入 → 钱不够换"钱不够"那句、放不下换"放不下"那句（音效 55），否则开确认框。
///   卖：「卖出」23070（没选 23072 灰）、「取消」23076；「全賣」BMP 171/172 中心 (272,40)；数量按组（"%4d/组" (cx+1, cy+5)），
///       卖不了的（第 9 段 0）价钱红字、▲加不上去；一次最多 持有数 / 一组几个 组。持有金额 + 卖出金额超过 10,000,000 不让卖。
///   确认框（9 宫格 640×408 居中）：已选的 "%-22s × %-3d" 两列 (20+300c, 26+20r)，"持有金额 … 购买 / 卖出金额 …" (120,251)，
///       "购买 / 卖出以上物品，可以吗？" (215,295)，「  是  」(143,335) /「  否  」(313,335)。是 → xD … 0 数据；否 → 回到列表。
/// </summary>
public partial class ShopTradeWindow : CgWindow
{
    public Func<long> Gold = () => 0;
    public int StandAct;
    public Func<int> FreeSlots = () => 20;
    public Action<string>? Submit;   // buy:序号:数量,… / sell:格子:组数,…
    private DialogPage? _page;
    private int[] _qty = Array.Empty<int>();
    private int _pageNo, _msg, _hover = -1;
    private bool _confirm, _all;
    private Control? _confirmBox;
    public bool Buying => _page?.Kind == "buy";
    /// <summary>鉴定（WN 20，exe 0x42700c）/ 修理（WN 22，exe 0x429b7d）：跟卖窗口同一个样子，每件只能选 0 / 1（▲ 上限 1），价钱黄字（色 4），
    /// 按钮 鉴定 23083/23085（没选 23084）、修理 23080/23082（没选 23081）；确认框只列名字 "%-22s"；是 → xD … 0 "格子|格子|…"。</summary>
    public bool Picking => _page?.Kind is "judge" or "repair";
    /// <summary>兑换（WN 28，exe 0x428ff6）：背景 23330，跟买窗口同样子；金币那两格换成 "持有的拿来换的道具数 个" / "要交出几个 个"（"个" 黄字 (530,…)）；
    /// 每件 "%6d" 每个要几个 (x+40,y+4)（没选灰字）+ "个" (x+110)；按钮 兑换 23331/23332（没选 23333）。
    /// 官方 exe 这里 ▲ 带了"要检查能不能卖"的旗标，而兑换的那一栏从来没填，所以官方客户端点 ▲ 永远"无法再增加"——移植照原意让它能加。</summary>
    public bool Exchanging => _page?.Kind == "exchange";
    public Func<int, int> CountOf = _ => 0;   // 背包里某道具编号一共几个
    private bool Judge => _page?.Kind == "judge";
    public DialogPage? Page => _page;
    public int PageNo => _pageNo;
    public int Message => _msg;
    public bool Confirming => _confirm;
    public IReadOnlyList<int> Quantities => _qty;

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(192, 132), false); }

    public void ShowPage(DialogPage? p)
    {
        if (p?.Shop == null || p.Kind is not ("buy" or "sell" or "judge" or "repair" or "exchange")) { _page = null; Visible = false; return; }
        _page = p; _qty = new int[p.Shop.Count]; _pageNo = 0; _msg = 0; _confirm = false; _all = false; _hover = -1;
        Visible = true; Refresh();
    }

    private int Pages => Math.Max(1, (_page!.Shop!.Count + 9) / 10);
    private long Total() => _page?.Shop is { } l ? l.Select((e, i) => (long)e.Price * (Buying || Picking || Exchanging ? _qty[i] : _qty[i] / Math.Max(1, e.Stack))).Sum() : 0;
    private int Selected => _qty.Count(q => q > 0);
    /// <summary>买：除了第 skip 件，其它已选的要占几格（每件 ⌈数量 / 一格几个⌉）。</summary>
    private int SlotsUsed(int skip = -1) => _page!.Shop!.Select((e, i) => i == skip || _qty[i] == 0 ? 0 : (_qty[i] + Math.Max(1, e.Max) - 1) / Math.Max(1, e.Max)).Sum();

    public override void Refresh()
    {
        Clear();
        if (_page?.Shop is not { } list) return;
        // 确认时官方把商店窗口关掉、原地换成 640×408 的确认框（0x424074：状态 1 重建 [0x1870444]），列表不画
        if (_confirm) { BuildConfirm(); return; }
        Img(Exchanging ? 23330 : 23055, 0, 0, block: true);
        Txt(_page.NpcName, 288, 14, Colors.White, center: true);
        // 对话框：鼠标停在道具上显示道具说明，否则显示这一句
        var said = _hover >= 0 && _hover < list.Count ? list[_hover].Name + "\n" + list[_hover].Desc
            : _msg == 0 || _page.AltTexts == null ? _page.Text : _page.AltTexts[_msg - 1];
        // 话按 20 字节折行（exe 0x40f581 用的 [0x119e9f4]，商店入口读完就设成 0x14）；鉴定 / 修理的话是客户端自己的（开头带空行，官方照样空一行）
        var lines = said.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 || (Picking && _hover < 0)).SelectMany(l => l.Length == 0 ? new List<string> { "" } : ShopMenuWindow.Wrap(l, 20)).Take(_hover >= 0 ? 8 : 5).ToList();
        for (int k = 0; k < lines.Count; k++) Txt(lines[k], 378, 54 + 21 * k, Colors.White);
        NpcFigure.Add(Body, Cache, new Vector2(468, 244), _page.Face, StandAct, 5);
        Txt($"{_pageNo + 1}/{Pages}", 189, 50, Colors.White, center: true);
        CBtn(23022, 23026, 147, 52, () => Turn(-1), "切换页数。");
        CBtn(23023, 23027, 226, 52, () => Turn(1), "切换页数。");
        if (!Buying && !Picking && !Exchanging && ExeArt.Bmp(_all ? 171 : 172) is { } allTex)
        {
            var b = BtnTex(allTex, allTex, 272 - allTex.GetWidth() / 2f, 40 - allTex.GetHeight() / 2f, ToggleAll);
            if (b != null) b.TooltipText = _all ? "取消全选。" : "全部选择。";
        }
        for (int k = 0; k < 10; k++)
        {
            int i = _pageNo * 10 + k; if (i >= list.Count) break;
            var e = list[i];
            float cx = 20 + 176 * (k / 5), cy = 127 + 68 * (k % 5);
            if (Cache.ImageNo(e.Image).Tex is { } t) Body.AddChild(new TextureRect { Texture = t, Position = new Vector2(cx + 24, cy - 11) - t.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            if (Buying && !Exchanging && e.Flag == 0) Txt("×", cx + 37, cy + 8, new Color(1, 0.2f, 0.2f));
            if (!Buying && !Picking && !Exchanging && e.Stack > 1) Txt($"{e.Stack}/组", cx + 1, cy + 11, new Color(0.5f, 1, 0.5f));
            int shown = Buying || Picking || Exchanging ? _qty[i] : _qty[i] / Math.Max(1, e.Stack);
            Txt(shown.ToString(), 80 + cx, cy - 28, Colors.White, right: true);
            var priceColor = Picking ? ChatLog.Palette[4] : Buying ? (e.Flag == 0 ? new Color(0.6f, 0.6f, 0.6f) : Colors.White) : (e.Flag == 0 ? new Color(1, 0.25f, 0.25f) : Colors.White);
            if (Exchanging)
            {
                Txt(e.Price.ToString(), cx + 119, cy - 1, _qty[i] == 0 ? ChatLog.Palette[7] : Colors.White, right: true);
                Txt("个", cx + 138, cy - 1, ChatLog.Palette[4]);
            }
            else Txt(e.Price.ToString(), cx + 126, cy - 1, priceColor, right: true);
            int ii = i;
            var hit = Hit(cx, cy - 35, 47, 47, _ => { }, box: false);
            hit.MouseEntered += () => { _hover = ii; Refresh(); };
            hit.MouseExited += () => { if (_hover == ii) { _hover = -1; Refresh(); } };
            CBtn(23020, 23024, cx + 108, cy - 27, () => Add(ii, +1), "增加此物品数量。");
            CBtn(23021, 23025, cx + 142, cy - 27, () => Add(ii, -1), "减少此物品数量。");
        }
        long total = Total();
        long have = Exchanging ? CountOf(_page.SourceItem) : Gold();
        Txt(have.ToString(), 512, 291, Colors.White, right: true);
        Txt(total.ToString(), 512, 343, (Buying || Picking || Exchanging) && have < total ? new Color(1, 0.25f, 0.25f) : Colors.White, right: true);
        bool any = Selected > 0;
        if (Exchanging) { Txt("个", 530, 291, ChatLog.Palette[4]); Txt("个", 530, 343, ChatLog.Palette[4]); }
        if (Exchanging)
        {
            if (any) CBtn(23331, 23332, 421, 391, Ok, "兑换指定物品。"); else CImg(23333, 421, 391);
            CBtn(23076, 23077, 516, 391, Cancel, "取消交易。");
        }
        else if (Picking)
        {
            int b0 = Judge ? 23083 : 23080;
            if (any) CBtn(b0, b0 + 2, 421, 391, Ok, Judge ? "鉴定指定物品。" : "修理指定物品。"); else CImg(b0 + 1, 421, 391);
            CBtn(23076, 23077, 516, 391, Cancel, Judge ? "取消鉴定。" : "取消修理。");
        }
        else if (Buying)
        {
            if (any) CBtn(23073, 23075, 421, 391, Ok, "购买指定物品。"); else CImg(23074, 421, 391);
            CBtn(23076, 23077, 516, 391, Cancel, "不买了。");
        }
        else
        {
            if (any) CBtn(23070, 23071, 421, 391, Ok, "卖出指定物品。"); else CImg(23072, 421, 391);
            CBtn(23076, 23077, 516, 391, Cancel, "不卖了。");
        }
        Btn(22509, 22510, 551, 6, Cancel);
    }

    public void Turn(int d)
    {
        int n = Math.Clamp(_pageNo + d, 0, Pages - 1);
        GameAudio.Se(51);
        if (n != _pageNo) { _pageNo = n; Refresh(); }
    }

    /// <summary>▲ / ▼（官方 0x476748）：买一次 1 个，卖一次一组；到头了音效 55。</summary>
    public void Add(int i, int dir)
    {
        if (_page?.Shop is not { } list || i < 0 || i >= list.Count) return;
        var e = list[i];
        int step = Buying || Picking || Exchanging ? 1 : Math.Max(1, e.Stack);
        if (dir > 0)
        {
            int max = Picking ? 1 : Exchanging ? (int)((CountOf(_page.SourceItem) - (Total() - (long)e.Price * _qty[i])) / Math.Max(1, e.Price)) : Buying ? Math.Min(120, Math.Max(0, FreeSlots() - SlotsUsed(i)) * Math.Max(1, e.Max)) : e.Flag == 0 ? 0 : e.Count / step;
            if ((_qty[i] / step) >= max) { GameAudio.Se(55); if (Buying && e.Flag != 0) _msg = 2; Refresh(); return; }
            _qty[i] += step;
        }
        else
        {
            if (_qty[i] < step) { GameAudio.Se(55); return; }
            _qty[i] -= step;
            if (_msg == 2) _msg = 0;
        }
        GameAudio.Se(51); Refresh();
    }

    /// <summary>卖：「全賣」——能卖的全部按整组选上 / 再按一次全部取消（官方 0x424a50）。</summary>
    public void ToggleAll()
    {
        if (_page?.Shop is not { } list || Buying || Picking || Exchanging) return;
        _all = !_all;
        for (int i = 0; i < list.Count; i++) { var e = list[i]; int st = Math.Max(1, e.Stack); _qty[i] = _all && e.Flag != 0 ? e.Count / st * st : 0; }
        GameAudio.Se(51); Refresh();
    }

    public void Ok()
    {
        if (Selected == 0) return;
        long total = Total();
        if (Exchanging)
        {
            if (CountOf(_page!.SourceItem) < total) { _msg = 1; GameAudio.Se(55); Refresh(); return; }
            if (SlotsUsed() > FreeSlots()) { _msg = 2; GameAudio.Se(55); Refresh(); return; }
        }
        else if (Buying || Picking)
        {
            if (Gold() < total) { _msg = 1; GameAudio.Se(55); Refresh(); return; }
            if (Buying && SlotsUsed() > FreeSlots()) { _msg = 2; GameAudio.Se(55); Refresh(); return; }
        }
        else if (Gold() + total > 10_000_000) { GameAudio.Se(55); return; }
        GameAudio.Se(78);
        _confirm = true; Refresh();
    }

    public void Cancel() { GameAudio.Se(57); Visible = false; _page = null; Submit?.Invoke("cancel"); }

    /// <summary>确认框「是」：买发 序号:数量，卖发 格子:组数（官方 "%d|%d" 用 | 连起来）。</summary>
    public void Yes()
    {
        if (_page?.Shop is not { } list) return;
        GameAudio.Se(79);
        string ans = Exchanging ? "swap:" + string.Join(",", list.Select((e, i) => (e, i)).Where(x => _qty[x.i] > 0).Select(x => $"{x.i}:{_qty[x.i]}"))
            : Picking ? "pick:" + string.Join(",", list.Select((e, i) => (e, i)).Where(x => _qty[x.i] > 0).Select(x => x.e.Slot.ToString()))
            : Buying
            ? "buy:" + string.Join(",", list.Select((e, i) => (e, i)).Where(x => _qty[x.i] > 0).Select(x => $"{x.i}:{_qty[x.i]}"))
            : "sell:" + string.Join(",", list.Select((e, i) => (e, i)).Where(x => _qty[x.i] > 0).Select(x => $"{x.e.Slot}:{_qty[x.i] / Math.Max(1, x.e.Stack)}"));
        Visible = false; _page = null; _confirm = false;
        Submit?.Invoke(ans);
    }
    public void No() { GameAudio.Se(79); _confirm = false; Refresh(); }

    private void BuildConfirm()
    {
        if (_page?.Shop is not { } list) return;
        // 确认框画在屏幕正中 640×408（相对本窗口 (192,132) 的偏移是 (−32,24)）
        // 窗口 10×8 格：位置按 640×408 居中算（exe 0x4240a8），但九宫格一格 64×48，画出来只有 640×384（官方实拍 2026-11-10 量的）
        var box = new Control { Position = new Vector2(160 - 192, 156 - 132), Size = new Vector2(640, 384), MouseFilter = Control.MouseFilterEnum.Stop };
        Body.AddChild(box); _confirmBox = box;
        box.AddChild(new ColorRect { Position = new Vector2(4, 4), Size = box.Size - new Vector2(8, 8), Color = new Color(0, 0, 58 / 255f, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore });
        box.AddChild(new CgFrame { Cache = Cache, Size = box.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
        int n = 0;
        foreach (var (e, i) in list.Select((e, i) => (e, i)))
        {
            if (_qty[i] == 0) continue;
            int count = Buying ? _qty[i] : _qty[i] / Math.Max(1, e.Stack);
            string name = Buying || Picking || Exchanging ? e.Name : $"{e.Name}({e.Stack}个)";
            var t = new CgText { Wrap = 999, Text = Picking ? name : $"{name}{new string(' ', Math.Max(0, 22 - name.Sum(CgText.Cells)))} × {count}", FontColor = Colors.White, Position = new Vector2(20 + 300 * (n % 2), 32 + 20 * (n / 2)) };
            box.AddChild(t); n++;
        }
        if (Exchanging)
            box.AddChild(new CgText { Wrap = 999, Text = $"{_page.SourceName} {CountOf(_page.SourceItem),8} 个    交换个数 {Total(),8} 个", FontColor = Colors.White, Position = new Vector2(120, 257) });
        else box.AddChild(new CgText { Wrap = 999, Text = $"持有金额 {Gold(),9} 金币    {(Picking ? (Judge ? "鉴定" : "修理") : Buying ? "购买" : "卖出")}金额 {Total(),9} 金币", FontColor = Colors.White, Position = new Vector2(120, 257) });
        box.AddChild(new CgText { Text = $"{(Exchanging ? "交易" : Picking ? (Judge ? "鉴定" : "修理") : Buying ? "购买" : "卖出")}以上物品，可以吗？", FontColor = Colors.White, Position = new Vector2(215, 301) });
        // 官方实拍（2026-11-10）：是 字左缘 204、否 418（相对确认框）
        foreach (var (label, x, act) in new (string, float, Action)[] { ("  是  ", 187, Yes), ("  否  ", 401, No) })
        {
            box.AddChild(new CgText { Text = label, FontColor = new Color(1, 1, 0.4f), Position = new Vector2(x, 341) });
            var h = new Control { Position = new Vector2(x - 4, 334), Size = new Vector2(64, 22), MouseFilter = Control.MouseFilterEnum.Stop };
            h.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) act(); };
            box.AddChild(h);
        }
    }

    private TextureButton? CBtn(int img, int pressed, float cx, float cy, Action a, string tip)
    {
        var sz = Cache.ImageNo(img).Tex?.GetSize() ?? Vector2.Zero;
        var b = Btn(img, pressed, cx - sz.X / 2, cy - sz.Y / 2, a);
        if (b != null) b.TooltipText = tip;
        return b;
    }
    private void CImg(int img, float cx, float cy)
    {
        if (Cache.ImageNo(img).Tex is { } t) ImgTex(t, cx - t.GetWidth() / 2f, cy - t.GetHeight() / 2f);
    }

    // 测试用
    public void TestSetQty(int i, int q) { if (i >= 0 && i < _qty.Length) { _qty[i] = q; Refresh(); } }
}
