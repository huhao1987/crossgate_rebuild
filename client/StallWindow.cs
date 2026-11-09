using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 摆摊 / 浏览摊位窗口（官方 4.06 实测，bob + hao，2026-11-10）。572×366 在 (194,165)。
///  背景：物品页 244380（右边带背包 5×4 格 (292+50c, 127+50r)），寵物页 244383（右边空，画自己的 5 只宠物条）。
///  左上标签 物品/寵物 98862–98867（选中的 64×23，在 (30,28) / (95,28)），翻页箭头 243030 / 243033 在 (221,28) / (243,28)。
///  左边：物品页 10 格（2 列 × 5 行），格 243139 在 (29+123c, 52+56r)，名字 (+51,+5) 两行，价钱框 (+51,+34) 67×13 右对齐、数量青字；
///        寵物页 98859（3 张卡）在 (30,52)，一张高 91。
///  右上：金錢框（自己的钱，右对齐 371,31），摊主名字 (413,32)，描述框 (296,46)–(476,112)：描述白字 (300,54)，没写时"未写描述的店铺" + "店铺描述不超过16字符"；
///        人物（摊主）站在 (507,102)。
///  自己摆摊：双击右边背包里的道具 / 宠物条 → 金幣小键盘 244310（在 (212,127)）填售价 → 放到左边；双击左边的撤下。
///        底部「擺攤」98889 (66,335)、「撤攤」98886 (156,335)。
///  浏览别人的：没有底部按钮；双击左边一格 → "确认购买？" → 確定 发 wAo。
/// </summary>
public partial class StallWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public Func<int> PlayerAnime = () => 0;
    public int Tab { get; private set; }          // 0 物品 1 寵物
    private int _petPage;
    public readonly Dictionary<int, int> ItemPrices = new();   // 背包格 → 价
    public readonly Dictionary<int, int> PetPrices = new();    // 宠物栏 → 价
    private readonly List<int> _itemOrder = new(), _petOrder = new();
    public string Desc = "";
    private long _lastClick; private string _lastKey = "";
    // 小键盘
    private int _pricing = -1; private bool _pricingPet; private string _input = "";
    // 买的确认
    private object? _confirm;
    private LineEdit? _descEdit;

    private static readonly Color Ink = new(0.12f, 0.1f, 0.05f), Khaki = new(0.72f, 0.66f, 0.5f);

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(194, 165), false); }

    public bool BrowseMode => Session()?.Browsing != null;

    public void OpenOwn()
    {
        var s = Session(); if (s == null) return;
        if (s.Browsing != null) s.CloseStallBrowse();
        Tab = 0; _petPage = 0; _pricing = -1; _confirm = null; Visible = true; Refresh();
    }
    public void OpenBrowse() { Tab = 0; _petPage = 0; _pricing = -1; _confirm = null; Visible = true; Refresh(); }
    public void Close()
    {
        Visible = false; _pricing = -1; _confirm = null; DropEdit();
        var s = Session(); if (s?.Browsing != null) s.CloseStallBrowse();
    }

    public override void Refresh()
    {
        Clear(); DropEdit();
        var s = Session();
        if (s == null) { Visible = false; return; }
        var view = s.Browsing;
        bool browse = view != null, locked = !browse && s.Stalling;
        Img(Tab == 0 ? 244380 : 244383, 0, 0, block: true);
        Hit(526, 4, 18, 18, _ => Close(), box: false);   // 右上圆钮
        // 标签
        Img(Tab == 0 ? 98864 : 98862, 30, Tab == 0 ? 23 : 28);
        Img(Tab == 1 ? 98867 : 98865, 95, Tab == 1 ? 23 : 28);
        Hit(30, 23, 64, 23, _ => { Tab = 0; _pricing = -1; Refresh(); }, box: false);
        Hit(95, 23, 64, 23, _ => { Tab = 1; _pricing = -1; Refresh(); }, box: false);
        Btn(243030, 243031, 221, 28, () => { if (Tab == 1 && _petPage > 0) { _petPage--; Refresh(); } });
        Btn(243033, 243034, 243, 28, () => { if (Tab == 1 && _petPage < 1) { _petPage++; Refresh(); } });
        // 右上：钱、名字、描述、人物
        Txt(s.Player.Gold.ToString(), 371, 31, right: true);
        Txt(browse ? view!.OwnerName : s.Player.Name, 413, 32, Ink);
        string desc = browse ? view!.Desc : Desc;
        if (desc.Length > 0) Txt(desc, 300, 54);
        else { Txt("未写描述的店铺", 300, 54); Txt("店铺描述不超过16字符", 300, 100); }
        if (!browse && !locked) Hit(296, 46, 180, 66, _ => EditDesc(), box: false);
        int anime = browse ? view!.OwnerAnime : PlayerAnime();
        if (anime > 0) Body.AddChild(new AnimSprite { Cache = Cache, AnimeId = anime, Dir = 5, Act = 0, FallbackAct = 0, Position = new Vector2(507, 102) });

        if (Tab == 0) ItemsTab(s, view, locked); else PetsTab(s, view, locked);

        if (!browse)
        {
            Btn(98889, 98890, 66, 335, () => { if (!s.Stalling) s.StallStart(Desc, ItemPrices, PetPrices); });
            Btn(98886, 98887, 156, 335, () => { if (s.Stalling) s.StallEnd(); });
        }
        if (_pricing >= 0) Keypad();
        if (_confirm != null) Confirm(s);
    }

    // ———————— 物品页 ————————

    private void ItemsTab(NetworkSession s, NetworkSession.StallView? view, bool locked)
    {
        LeftFrame();
        var bag = s.Player.Items.Where(x => x.Slot >= 8 && x.Slot < 28).ToList();
        _itemOrder.RemoveAll(sl => !ItemPrices.ContainsKey(sl) || bag.All(x => x.Slot != sl));
        foreach (var k in ItemPrices.Keys.Where(k => !_itemOrder.Contains(k)).ToList()) { if (bag.Any(x => x.Slot == k)) _itemOrder.Add(k); else ItemPrices.Remove(k); }
        for (int i = 0; i < 10; i++)
        {
            float x = 29 + 123 * (i % 2), y = 52 + 56 * (i / 2);
            Img(243139, x, y);
            PriceBox(x + 51, y + 34);
            if (y + 51 < 330) Sep(x, y + 51);
            ItemStack? it = null; int price = 0, key = -1;
            if (view != null && i < view.Items.Count) { it = view.Items[i].Item; price = view.Items[i].Price; key = it.Slot; }
            else if (view == null && i < _itemOrder.Count) { key = _itemOrder[i]; it = bag.FirstOrDefault(b => b.Slot == key); price = ItemPrices[key]; }
            if (it == null) continue;
            Icon(it.Graphic, it.Count, new Vector2(x, y));
            Body.AddChild(new CgText { Wrap = 11, LineHeight = 15, Position = new Vector2(x + 51, y + 1), Text = it.Name });
            Txt(price.ToString(), x + 102, y + 35, right: true);
            int k2 = key; var it2 = it; int pr = price;
            if (view != null) Hit(x, y, 118, 48, _ => Dbl("v" + k2, () => { _confirm = new NetworkSession.StallItem(pr, it2); Refresh(); }));
            else if (!locked) Hit(x, y, 118, 48, _ => Dbl("l" + k2, () => { ItemPrices.Remove(k2); _itemOrder.Remove(k2); Refresh(); }));
        }
        // 右边：自己的背包
        for (int c = 0; c < 20; c++)
        {
            var p = new Vector2(292 + 50 * (c % 5), 127 + 50 * (c / 5));
            int slot = c + 8;
            if (bag.FirstOrDefault(x => x.Slot == slot) is not { } it) continue;
            if (view == null && ItemPrices.ContainsKey(slot)) continue;   // 已经摆到左边了
            Icon(it.Graphic, it.Count, p);
            if (view == null && !locked) Hit(p.X, p.Y, 48, 48, _ => Dbl("b" + slot, () => { _pricing = slot; _pricingPet = false; _input = ""; Refresh(); }));
        }
    }

    // ———————— 寵物页 ————————

    private void PetsTab(NetworkSession s, NetworkSession.StallView? view, bool locked)
    {
        Img(98859, 30, 52);
        var mine = s.Player.Pets;
        _petOrder.RemoveAll(sl => !PetPrices.ContainsKey(sl) || mine.All(p => p.Slot != sl));
        foreach (var k in PetPrices.Keys.Where(k => !_petOrder.Contains(k)).ToList()) { if (mine.Any(p => p.Slot == k)) _petOrder.Add(k); else PetPrices.Remove(k); }
        for (int i = 0; i < 3; i++)
        {
            int idx = _petPage * 3 + i;
            float cy = 52 + 91 * i;
            PetInfo? pet = null; int price = 0;
            if (view != null && idx < view.Pets.Count) { pet = view.Pets[idx].Pet; price = view.Pets[idx].Price; }
            else if (view == null && idx < _petOrder.Count) { pet = mine.FirstOrDefault(p => p.Slot == _petOrder[idx]); price = PetPrices[_petOrder[idx]]; }
            if (pet == null) continue;
            PetCard(pet, price, 30, cy);
            int sl = pet.Slot; var pet2 = pet; int pr = price;
            if (view != null) Hit(30, cy, 244, 90, _ => Dbl("vp" + sl, () => { _confirm = new NetworkSession.StallPet(pr, pet2); Refresh(); }));
            else if (!locked) Hit(30, cy, 244, 90, _ => Dbl("lp" + sl, () => { PetPrices.Remove(sl); _petOrder.Remove(sl); Refresh(); }));
        }
        // 右边：自己的宠物 5 条
        for (int k = 0; k < 5; k++)
        {
            float y = 122 + 45 * k;
            PetRow(293, y, mine.FirstOrDefault(p => p.Slot == k));
            int slot = k;
            if (view == null && !locked && mine.Any(p => p.Slot == slot) && !PetPrices.ContainsKey(slot))
                Hit(293, y, 229, 43, _ => Dbl("mp" + slot, () => { _pricing = slot; _pricingPet = true; _input = ""; Refresh(); }));
        }
    }

    /// <summary>98859 一张卡上的字（官方截图量的：标签在图里，数值右对齐）。</summary>
    private void PetCard(PetInfo p, int price, float cx, float cy)
    {
        Txt(p.DisplayName, cx + 28, cy + 4, Ink);
        Txt(p.MaxHp.ToString(), cx + 226, cy + 4, Ink, right: true);
        Txt(p.Level.ToString(), cx + 42, cy + 22, Ink, right: true);
        Txt(p.RaceName, cx + 92, cy + 22, Ink);
        Txt(p.MaxMp.ToString(), cx + 226, cy + 22, Ink, right: true);
        Txt(p.Vit.ToString(), cx + 101, cy + 40, Ink, right: true);
        Txt(p.Str.ToString(), cx + 157, cy + 40, Ink, right: true);
        Txt(p.Tgh.ToString(), cx + 226, cy + 40, Ink, right: true);
        Txt(p.SkillSlots.ToString(), cx + 51, cy + 58, Ink, right: true);
        Txt(p.Spd.ToString(), cx + 101, cy + 58, Ink, right: true);
        Txt(p.Mag.ToString(), cx + 157, cy + 58, Ink, right: true);
        Txt(p.Loyalty.ToString(), cx + 226, cy + 58, Ink, right: true);
        Txt(p.Points.ToString(), cx + 55, cy + 76, Ink, right: true);
        Txt(price.ToString(), cx + 208, cy + 76, right: true);
    }

    /// <summary>右边宠物条（暗灰底，等级/生命/魔力 + 4 条细槽，第 2 条蓝 = 生命、第 3 条红 = 魔力）。</summary>
    private void PetRow(float x, float y, PetInfo? p)
    {
        Body.AddChild(new ColorRect { Color = new Color(0.86f, 0.82f, 0.68f), Position = new Vector2(x, y), Size = new Vector2(229, 43), MouseFilter = Control.MouseFilterEnum.Ignore });
        Body.AddChild(new ColorRect { Color = new Color(0.27f, 0.27f, 0.27f), Position = new Vector2(x + 2, y + 2), Size = new Vector2(225, 39), MouseFilter = Control.MouseFilterEnum.Ignore });
        Txt("等级", x + 117, y + 6, Khaki);
        Txt("生命", x + 6, y + 25, Khaki); Txt("魔力", x + 85, y + 25, Khaki);
        for (int b = 0; b < 4; b++)
        {
            Body.AddChild(new ColorRect { Color = new Color(0.4f, 0.37f, 0.33f), Position = new Vector2(x + 171, y + 4 + 9 * b), Size = new Vector2(54, 5), MouseFilter = Control.MouseFilterEnum.Ignore });
            Body.AddChild(new ColorRect { Color = new Color(0.9f, 0.88f, 0.8f), Position = new Vector2(x + 171, y + 4 + 9 * b), Size = new Vector2(5, 5), MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        if (p == null) return;
        Txt(p.DisplayName, x + 4, y + 8);
        Txt(p.Level.ToString(), x + 168, y + 8, right: true);
        Txt($"{p.Hp}/{p.MaxHp}", x + 33, y + 25);
        Txt($"{p.Mp}/{p.MaxMp}", x + 112, y + 25);
        if (p.MaxHp > 0) Body.AddChild(new ColorRect { Color = new Color(0.2f, 0.6f, 0.95f), Position = new Vector2(x + 173, y + 13), Size = new Vector2(30f * p.Hp / p.MaxHp, 4), MouseFilter = Control.MouseFilterEnum.Ignore });
        if (p.MaxMp > 0) Body.AddChild(new ColorRect { Color = new Color(0.9f, 0.1f, 0.1f), Position = new Vector2(x + 173, y + 22), Size = new Vector2(34f * p.Mp / p.MaxMp, 4), MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    // ———————— 小键盘（金幣 244310） ————————

    public long Input => long.TryParse(_input, out var v) ? v : 0;

    private void Keypad()
    {
        const float kx = 212, ky = 127;
        Img(244310, kx, ky, block: true);
        Txt(Input.ToString(), kx + 78, ky + 31, Ink, right: true);
        Txt("售价", kx + 82, ky + 31, Ink);
        int[] grid = { 7, 8, 9, 4, 5, 6, 1, 2, 3 };
        for (int i = 0; i < 10; i++)
        {
            int d = i < 9 ? grid[i] : 0;
            float x = kx + 20 + 24 * (i < 9 ? i % 3 : 0), y = ky + 45 + 20 * (i < 9 ? i / 3 : 3);
            int img = 243170 + 3 * d;
            Btn(img, img + 1, x, y, () => { if (_input.Length < 7) { _input = (_input + d).TrimStart('0'); Refresh(); } });
        }
        Btn(243200, 243201, kx + 92, ky + 65, () => { if (_input.Length > 0) { _input = _input[..^1]; Refresh(); } });   // 更正
        Btn(243206, 243207, kx + 92, ky + 105, () => { _input = ""; Refresh(); });                                         // 清除
        Btn(243210, 243209, kx + 67, ky + 105, () => SetPrice(_pricing, _pricingPet, (int)Input));                         // 確定
        Hit(kx + 101, ky + 6, 16, 16, _ => { _pricing = -1; Refresh(); }, box: false);
    }

    public void SetPrice(int slot, bool pet, int price)
    {
        _pricing = -1;
        if (price > 0)
        {
            if (pet) { PetPrices[slot] = price; if (!_petOrder.Contains(slot)) _petOrder.Add(slot); }
            else if (_itemOrder.Count < 10) { ItemPrices[slot] = price; if (!_itemOrder.Contains(slot)) _itemOrder.Add(slot); }
        }
        Refresh();
    }

    // ———————— 确认购买 ————————

    private void Confirm(NetworkSession s)
    {
        var yellow = new Color(0.45f, 0.3f, 0.1f);
        if (_confirm is NetworkSession.StallItem si)
        {
            const float dx = 198, dy = 116;   // 98856 176×158，跟官方对话框同中心
            Img(98856, dx, dy, block: true);
            Img(243139, dx + 16, dy + 48);
            Icon(si.Item.Graphic, si.Item.Count, new Vector2(dx + 16, dy + 48));
            Body.AddChild(new CgText { Wrap = 11, LineHeight = 15, Position = new Vector2(dx + 68, dy + 50), Text = si.Item.Name, FontColor = Ink });
            Txt(si.Price.ToString(), dx + 150, dy + 84, right: true);
            Btn(98874, 98875, dx + 20, dy + 128, () => { _confirm = null; s.StallBuy(si.Item.Slot); Refresh(); });
            Btn(98877, 98878, dx + 104, dy + 128, () => { _confirm = null; Refresh(); });
        }
        else if (_confirm is NetworkSession.StallPet sp)
        {
            const float dx = 136, dy = 97;    // 98860 300×197（官方位置）
            Img(98860, dx, dy, block: true);
            PetCard(sp.Pet, sp.Price, dx + 12, dy + 51);
            Btn(98874, 98875, dx + 76, dy + 156, () => { _confirm = null; s.StallBuy(sp.Pet.Slot); Refresh(); });
            Btn(98877, 98878, dx + 167, dy + 156, () => { _confirm = null; Refresh(); });
        }
        _ = yellow;
    }

    // ———————— 小工具 ————————

    /// <summary>左边物品区的内框（官方是画出来的线：外深棕、里面一圈亮、再一圈暗）。</summary>
    private void LeftFrame()
    {
        Rect(23, 46, 256, 1, C8(105, 85, 29)); Rect(23, 46, 1, 286, C8(105, 85, 29));
        Rect(23, 331, 256, 1, C8(113, 93, 38)); Rect(278, 46, 1, 286, C8(113, 93, 38));
        Rect(24, 47, 254, 2, C8(239, 232, 211)); Rect(24, 47, 2, 284, C8(239, 232, 211));
        Rect(24, 329, 254, 2, C8(243, 241, 234)); Rect(276, 47, 2, 284, C8(243, 241, 234));
        Rect(26, 49, 250, 1, C8(194, 177, 136)); Rect(26, 49, 1, 280, C8(194, 177, 136));
        Rect(26, 328, 250, 1, C8(160, 143, 94)); Rect(275, 49, 1, 280, C8(160, 143, 94));
    }

    private void Sep(float x, float y) => Rect(x - 1, y, 119, 1, C8(160, 143, 94));

    /// <summary>价钱框 67×13：深蓝边（上左暗、下右亮）+ 蓝灰底 + 右边白色 G。</summary>
    private void PriceBox(float x, float y)
    {
        Rect(x + 1, y, 65, 1, C8(34, 41, 79)); Rect(x, y + 1, 1, 11, C8(34, 41, 79));
        Rect(x + 1, y + 1, 65, 11, C8(79, 85, 121));
        Rect(x + 1, y + 12, 65, 1, C8(193, 198, 224)); Rect(x + 66, y + 1, 1, 11, C8(193, 198, 224));
        Txt("G", x + 64, y + 1, Colors.White, right: true);
    }

    private static Color C8(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b);

    private void Rect(float x, float y, float w, float h, Color c) =>
        Body.AddChild(new ColorRect { Color = c, Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore });

    private void Icon(int image, int count, Vector2 p)
    {
        if (image > 0 && Cache.ImageNo(image).Tex is { } tex)
            Body.AddChild(new TextureRect { Texture = tex, Position = p + (new Vector2(48, 48) - tex.GetSize()) / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
        if (count > 1) Txt(count.ToString(), p.X + 46, p.Y + 36, new Color(0.4f, 1, 1), right: true);
    }

    private void Dbl(string key, Action act)
    {
        long now = (long)Time.GetTicksMsec();
        bool dbl = key == _lastKey && now - _lastClick < 500;
        _lastClick = now; _lastKey = key;
        if (dbl) { _lastKey = ""; act(); }
    }

    private void EditDesc()
    {
        DropEdit();
        _descEdit = new LineEdit { Text = Desc, MaxLength = 16, Position = new Vector2(298, 50), Size = new Vector2(176, 22), PlaceholderText = "未写描述的店铺" };
        _descEdit.TextSubmitted += t => { Desc = t.Trim(); DropEdit(); Refresh(); };
        _descEdit.FocusExited += () => { if (_descEdit != null) { Desc = _descEdit.Text.Trim(); CallDeferred(nameof(Refresh)); } };
        Win.AddChild(_descEdit);
        _descEdit.GrabFocus();
        if (DisplayServer.HasFeature(DisplayServer.Feature.VirtualKeyboard)) DisplayServer.VirtualKeyboardShow(Desc);
    }

    private void DropEdit() { if (_descEdit != null && IsInstanceValid(_descEdit)) _descEdit.QueueFree(); _descEdit = null; }

    // 测试用
    public void TestTab(int t) { Tab = t; Refresh(); }
    public void TestBuy(int i)
    {
        var v = Session()?.Browsing; if (v == null) return;
        if (Tab == 0 && i < v.Items.Count) _confirm = v.Items[i]; else if (Tab == 1 && i < v.Pets.Count) _confirm = v.Pets[i];
        Refresh();
    }
    public void TestConfirm()
    {
        var s = Session(); if (s == null) return;
        if (_confirm is NetworkSession.StallItem si) s.StallBuy(si.Item.Slot); else if (_confirm is NetworkSession.StallPet sp) s.StallBuy(sp.Pet.Slot);
        _confirm = null; Refresh();
    }
    public bool Confirming => _confirm != null;
    public void TestStart() { var s = Session(); if (s != null && !s.Stalling) s.StallStart(Desc, ItemPrices, PetPrices); }
    public bool Pricing => _pricing >= 0;
    public void TestPrice(int bagSlot, int price) { _pricing = bagSlot; _pricingPet = false; SetPrice(bagSlot, false, price); }
}
