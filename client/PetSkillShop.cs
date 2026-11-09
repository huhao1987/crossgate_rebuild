using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 宠物技能店（服务端 BZ 24 → 25 → 26，Techshop，官方 exe 画 0x427a14 / 0x427fce / 0x42849d）。窗口 576×456 在 (192,132)，坐标相对窗口左上角：
///  共通：标题 NPC 名中心 (288,8)；右边话 5 行 (378, 58+20k)；NPC 站 (468,244)；金币 "%9d" (440,286)；「取消」23076/23077 中心 (464,390)（提示"取消让宠物学习技能。"，不发包）。
///  24 选技能（背景 23090）：页码 "%d/%d" (172,44)、◀▶ (147,52)/(226,52)，一页 8 个，第 k 行 y = 87+32k：
///     名字在 24 字节宽里置中（左 −14）、耗魔 "%4d" 青字 (204,y)、价钱 "%7d" 青字 (256,y)；滑过显示说明 (30,360) 36 字节折行；
///     点：钱不够 → 换"钱不够"那句（音效 55）；没宠物 → 换"没宠物"那句（55）；否则 xD … 0 "序号"、关窗口（音效 79）。
///  25 选宠物（背景 23091）：上面是选的技能（名字 (−14,43)、耗魔 (203,45)、价钱 (256,44)）；
///     第 k 只 y = 134+40k：名字 16 字节宽置中（左 76，y−2）、等级 "%4d" 青字 (266,y)；等级低于要求的整行灰字，点了"宠物等级不足。"（55）；点能学的 → "序号"。
///  26 选栏位（背景 23092）：上面技能 + 宠物名 (40,80)、"等级 %d" (240,82)；宠物有几栏就画几条 23093 (182, 128+25k)，
///     有技能的画名字（24 字节宽置中，左 50）、等级 "%4d" (258)；滑过说明 (30,375)。点一栏 → 确认框（5×5 格 320×260 居中）：
///     "学习『技能』"黄字、"让『宠物』"、"使用『栏位技能 / 空栏位』的位置"、"来学习。"、"持有金额 %8d 金币"、"购买金额 %8d 金币"、"可以吗？"、「是」「否」；是 → "栏位序号"。
/// </summary>
public partial class PetSkillShopWindow : CgWindow
{
    public Func<long> Gold = () => 0;
    public Func<bool> HasPet = () => true;
    public int StandAct;
    public Action<string>? Submit;   // 序号；"cancel"
    public DialogPage? Page { get; private set; }
    public int Message { get; private set; }
    public int PageNo { get; private set; }
    public int ConfirmSlot { get; private set; } = -1;
    private ShopEntry? _skill;      // 24 里选的技能
    private ShopEntry? _pet;        // 25 里选的宠物
    private int _hover = -1;

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(192, 132), false); }

    public void ShowPage(DialogPage? p)
    {
        Page = p?.Kind is "petskill" or "petpick" or "petslot" ? p : null;
        if (Page?.Kind == "petskill") { _skill = null; _pet = null; PageNo = 0; Message = 0; }
        _hover = -1; ConfirmSlot = -1;
        Visible = Page != null;
        if (Visible) Refresh();
    }

    public override void Refresh()
    {
        Clear();
        if (Page?.Shop is not { } list) return;
        if (ConfirmSlot >= 0) { BuildConfirm(list); return; }
        var cyan = ChatLog.Palette[1]; var grey = ChatLog.Palette[7];
        Img(Page.Kind == "petskill" ? 23090 : Page.Kind == "petpick" ? 23091 : 23092, 0, 0, block: true);
        Txt(Page.NpcName, 288, 14, Colors.White, center: true);
        var said = Message == 0 || Page.AltTexts == null ? Page.Text : Page.AltTexts[Message - 1];
        var lines = said.Split('\n').SelectMany(l => l.Length == 0 ? new List<string> { "" } : ShopMenuWindow.Wrap(l, 20).ToList()).Take(5).ToList();
        for (int k = 0; k < lines.Count; k++) Txt(lines[k], 378, 64 + 20 * k, Colors.White);
        if (Page.Face > 0)
        {
            var holder = new Node2D { Position = new Vector2(468, 244) };
            holder.AddChild(new AnimSprite { Cache = Cache, AnimeId = Page.Face, Act = StandAct, FallbackAct = StandAct, Dir = 5 });
            Body.AddChild(holder);
        }
        Txt(Gold().ToString(), 516, 292, Colors.White, right: true);
        if (Page.Kind == "petskill")
        {
            int pages = Math.Max(1, (list.Count + 7) / 8);
            Txt($"{PageNo + 1}/{pages}", 172, 50, Colors.White);
            CBtn(23022, 23026, 147, 52, () => Turn(-1), "切换页数。");
            CBtn(23023, 23027, 226, 52, () => Turn(1), "切换页数。");
            for (int k = 0; k < 8; k++)
            {
                int i = PageNo * 8 + k; if (i >= list.Count) break;
                var e = list[i]; float y = 87 + 32 * k;
                Txt(e.Name, 88, y + 4, Colors.White, center: true);
                Txt($"{e.Image,4}", 204, y + 6, cyan);
                Txt($"{e.Price,7}", 256, y + 6, cyan);
                Row(18, y - 4, 327, 22, i, () => PickSkill(i));
            }
            if (_hover >= 0 && _hover < list.Count) Desc(list[_hover].Desc, 30, 360);
        }
        else
        {
            if (_skill != null) SkillHead(_skill);
            if (Page.Kind == "petpick")
            {
                for (int k = 0; k < list.Count; k++)
                {
                    var e = list[k]; float y = 134 + 40 * k; int kk = k;
                    bool ok = e.Count >= Page.SourceItem;
                    Txt(e.Name, 76 + 68, y + 4, ok ? Colors.White : grey, center: true);
                    Txt($"{e.Count,4}", 266, y + 6, ok ? cyan : grey);
                    var hit = Row(70, y - 4, 230, 22, -1, () => PickPet(kk));
                    hit.TooltipText = ok ? "让该宠物学习此技能。" : "宠物等级不足。";
                }
            }
            else
            {
                if (_pet != null) { Txt(_pet.Name, 40 + 68, 86, Colors.White, center: true); Txt($"等级 {_pet.Count}", 240, 88, Colors.White); }
                int slots = _pet?.Max > 0 ? Math.Min(_pet.Max, list.Count) : list.Count;
                for (int k = 0; k < slots; k++)
                {
                    var e = list[k]; float y = 119 + 25 * k; int kk = k;
                    if (Cache.ImageNo(23093) is (ImageTexture bar, var be) && be != null)
                        Body.AddChild(new TextureRect { Texture = bar, Position = new Vector2(182 + be.OffsetX, y + 9 + be.OffsetY), MouseFilter = Control.MouseFilterEnum.Ignore });
                    if (e.Name.Length > 0)
                    {
                        Txt(e.Name, 50 + 102, y + 6, Colors.White, center: true);
                        Txt($"{e.Image,4}", 258, y + 8, cyan);
                    }
                    var hit = Row(60, y - 2, 244, 20, k, () => PickSlot(kk));
                    hit.TooltipText = "使用此栏来学习技能。";
                }
                if (_hover >= 0 && _hover < list.Count && list[_hover].Name.Length > 0) Desc(list[_hover].Desc, 30, 375);
            }
        }
        CBtn(23076, 23077, 464, 390, Cancel, "取消让宠物学习技能。");
    }

    private void SkillHead(ShopEntry s)
    {
        Txt(s.Name, 88, 49, Colors.White, center: true);
        Txt($"{s.Image,4}", 203, 51, ChatLog.Palette[1]);
        Txt($"{s.Price,7}", 256, 50, ChatLog.Palette[1]);
    }

    private void Desc(string text, float x, float y) =>
        Body.AddChild(new CgText { Wrap = 36, LineHeight = CgText.LineH, Text = text, Position = new Vector2(x, y + 1) });

    private Control Row(float x, float y, float w, float h, int hover, Action a)
    {
        var hit = Hit(x, y, w, h, _ => a(), box: true);
        if (hover >= 0)
        {
            hit.MouseEntered += () => { _hover = hover; Refresh(); };
            hit.MouseExited += () => { if (_hover == hover) { _hover = -1; Refresh(); } };
        }
        return hit;
    }

    public void Turn(int d)
    {
        if (Page?.Shop is not { } list) return;
        int pages = Math.Max(1, (list.Count + 7) / 8);
        GameAudio.Se(51);
        PageNo = ((PageNo + d) % pages + pages) % pages;   // 官方到头了会绕回去（0x427b70）
        Refresh();
    }

    public void PickSkill(int i)
    {
        if (Page?.Shop is not { } list || i < 0 || i >= list.Count) return;
        if (list[i].Price > Gold()) { GameAudio.Se(55); Message = 1; Refresh(); return; }
        if (!HasPet()) { GameAudio.Se(55); Message = 2; Refresh(); return; }
        GameAudio.Se(79); Message = 0;
        _skill = list[i];
        Visible = false; Submit?.Invoke(i.ToString());
    }

    public void PickPet(int k)
    {
        if (Page?.Shop is not { } list || k < 0 || k >= list.Count) return;
        if (list[k].Count < Page.SourceItem) { GameAudio.Se(55); return; }
        GameAudio.Se(79);
        _pet = list[k];
        Visible = false; Submit?.Invoke(list[k].Slot.ToString());
    }

    public void PickSlot(int k) { GameAudio.Se(79); ConfirmSlot = k; Refresh(); }

    public void Yes()
    {
        int k = ConfirmSlot; if (k < 0) return;
        GameAudio.Se(79); ConfirmSlot = -1; Visible = false;
        Submit?.Invoke(k.ToString());
    }
    public void No() { GameAudio.Se(79); ConfirmSlot = -1; Refresh(); }
    public void Cancel() { GameAudio.Se(57); Visible = false; Page = null; ConfirmSlot = -1; Submit?.Invoke("cancel"); }

    private void BuildConfirm(IReadOnlyList<ShopEntry> list)
    {
        // 确认框 5×5 格（320×260）画在 ((W−320)/2,(H−264)/2) = (320,228)，相对本窗口 (128,96)
        var box = new Control { Position = new Vector2(320 - 192, 228 - 132), Size = new Vector2(320, 260), MouseFilter = Control.MouseFilterEnum.Stop };
        Body.AddChild(box);
        box.AddChild(CgFrame.Tint(box.Size));
        box.AddChild(new CgFrame { Cache = Cache, Size = box.Size, MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        void L(string s, float x, float y, Color c) => box.AddChild(new CgText { Wrap = 999, Text = s, FontColor = c, Position = new Vector2(x, y + 1) });
        void C(string s, float y, Color c) { float w = s.Sum(CgText.Cells) * CgText.CellW; L(s, 20 + (32 * CgText.CellW - w) / 2, y, c); }
        var yellow = ChatLog.Palette[4];
        var slot = ConfirmSlot < list.Count ? list[ConfirmSlot] : null;
        C($"学习『{_skill?.Name}』", 20, yellow);
        L($"让『{_pet?.Name}』", 30, 50, Colors.White);
        L($"使用『{(slot == null || slot.Name.Length == 0 ? "空栏位" : slot.Name)}』的位置", 30, 70, Colors.White);
        L("来学习。", 30, 90, Colors.White);
        L($"持有金额 {Gold(),8} 金币", 60, 120, Colors.White);
        L($"购买金额 {_skill?.Price ?? 0,8} 金币", 60, 140, Colors.White);
        C("可以吗？", 170, Colors.White);
        foreach (var (label, x, act) in new (string, float, Action)[] { ("  是  ", 80, Yes), ("  否  ", 186, No) })
        {
            L(label, x, 200, yellow);
            var h = new Control { Position = new Vector2(x - 2, 198), Size = new Vector2(54, 20), MouseFilter = Control.MouseFilterEnum.Stop };
            h.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) act(); };
            box.AddChild(h);
        }
    }

    private void CBtn(int img, int pressed, float cx, float cy, Action a, string tip)
    {
        var sz = Cache.ImageNo(img).Tex?.GetSize() ?? Vector2.Zero;
        var b = Btn(img, pressed, cx - sz.X / 2, cy - sz.Y / 2, a);
        if (b != null) b.TooltipText = tip;
    }
}
