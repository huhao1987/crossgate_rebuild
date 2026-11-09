using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 宠物表（底部菜单"宠物"，官方 4.06 实测）：背景 22840 在 (0,0)，最多 5 只，每只一行（行距 75）。
/// 每行：名字（居中 x 85）、等级（右对齐 202）、健康绿点 (255,43)、生命/魔法（右对齐 137）、血条/魔条 (147,66)/(147,71)、种族 (147,88)、
/// 状态按钮 (198,62)：22841 战斗 22843 待命 22845 休息 22847 郵件（偶数号是悬停图）。点状态按钮：休息 → 待命 → 战斗 → 休息（官方实测）。
/// 点名字那一行 → 宠物详情。关闭 22509 (246,6)。
/// </summary>
public partial class PetListWindow : CgWindow
{
    public Func<IReadOnlyList<PetInfo>> Pets = () => Array.Empty<PetInfo>();
    public Action<int, int>? SetState;   // 宠物栏、状态
    public Action<int>? OpenDetail;      // 宠物栏
    /// <summary>扔宠物（官方：名字上点右键；手机：按住 0.6 秒，先问一下）。</summary>
    public Action<int>? Drop;
    public Action<int>? DropAsk;

    public override void _Ready() { Layer = 16; Visible = false; Build(Vector2.Zero, false); }

    public static int StateButton(int state) => state switch { PetInfo.Battle => 22841, PetInfo.Standby => 22843, PetInfo.Rest => 22845, _ => 22847 };
    public static int NextState(int state) => state switch { PetInfo.Rest => PetInfo.Standby, PetInfo.Standby => PetInfo.Battle, _ => PetInfo.Rest };

    public override void Refresh()
    {
        Clear();
        Img(22840, 0, 0, block: true);
        var pets = Pets();
        for (int k = 0; k < 5; k++)
        {
            var p = pets.FirstOrDefault(x => x.Slot == k);
            if (p == null) continue;
            float dy = 75 * k;
            Txt(p.DisplayName, 85, 41 + dy, center: true);
            Txt(p.Level.ToString(), 202, 41 + dy, right: true);
            Body.AddChild(new ColorRect { Color = new Color(0, 0.9f, 0), Position = new Vector2(255, 43 + dy), Size = new Vector2(8, 8), MouseFilter = Control.MouseFilterEnum.Ignore });
            Txt($"{p.Hp,3}/{p.MaxHp,4}".TrimStart(), 137, 63 + dy, right: true);
            Txt($"{p.Mp,3}/{p.MaxMp,4}".TrimStart(), 137, 86 + dy, right: true);
            Bar(147, 66 + dy, p.MaxHp > 0 ? (float)p.Hp / p.MaxHp : 0, new Color(0.95f, 0.1f, 0.1f));
            Bar(147, 71 + dy, p.MaxMp > 0 ? (float)p.Mp / p.MaxMp : 0, new Color(0.95f, 0.85f, 0.1f));
            Txt(p.RaceName, 147, 88 + dy, new Color(0.4f, 1, 1));
            int slot = p.Slot, st = p.State;
            if (st == PetInfo.Walk) Img(StateButton(st), 198, 62 + dy);   // 郵件（散步）状态只能在详情里用「展示」改
            else Btn(StateButton(st), StateButton(st) + 1, 198, 62 + dy, () => SetState?.Invoke(slot, NextState(st)));
            var h = Hit(10, 36 + dy, 150, 24, mb => { if (mb.ButtonIndex == MouseButton.Right) Drop?.Invoke(slot); else if (mb.ButtonIndex == MouseButton.Left) _pressAt = Time.GetTicksMsec(); });
            h.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) return;
                if (_pressAt == 0) return;
                bool longPress = Time.GetTicksMsec() - _pressAt > 600; _pressAt = 0;
                if (longPress) DropAsk?.Invoke(slot); else OpenDetail?.Invoke(slot);
            };
        }
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }

    private void Bar(float x, float y, float frac, Color c)
    {
        Body.AddChild(new ColorRect { Color = new Color(0, 0, 0.4f), Position = new Vector2(x, y), Size = new Vector2(42, 3), MouseFilter = Control.MouseFilterEnum.Ignore });
        Body.AddChild(new ColorRect { Color = c, Position = new Vector2(x, y), Size = new Vector2(42 * Math.Clamp(frac, 0, 1), 3), MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    private ulong _pressAt;
    public void TestClickState(int slot) { var p = Pets().FirstOrDefault(x => x.Slot == slot); if (p != null) SetState?.Invoke(slot, NextState(p.State)); }
}

/// <summary>
/// 宠物详情（官方 4.06 实测，窗口在 (0,0)）：底部四个标签 状態/明細/技能/装備（4.06 自己画的一条浅色栏，图档里没有，照截图画）。
///  状態 22800：等级、经验、下一级、生命、魔力、攻击、防御、敏捷、精神、回复、忠诚度、技能栏；右边宠物形象、健康、种族、地水火风；更名 22807、展示、列表 22830。
///  明細 22810：基本值（增加点数、体力力量强度速度魔法，有点数时出 +1）、抵抗值 6 项、修正值 4 项、忠诚度、技能栏。
///  技能 22824：10 行 22761，招式名、MP。
///  装備 22824：项圈、饰品×2、防具、水晶 5 格，名字、负重。
/// 翻页 22832/22834 在宠物之间切换，「列表」回宠物表。
/// </summary>
public partial class PetDetailWindow : CgWindow
{
    public Func<IReadOnlyList<PetInfo>> Pets = () => Array.Empty<PetInfo>();
    public Action<int, int>? AddPoint;     // 宠物栏、属性 0–4
    public Action<int, string>? Rename;    // 宠物栏、新名字
    public Action<int, int>? SetState;     // 宠物栏、状态（展示 = 郵件/散步）
    public Action? BackToList;
    public int Slot { get; private set; }
    public int Tab { get; private set; }
    private LineEdit? _rename;

    public override void _Ready() { Layer = 17; Visible = false; Build(Vector2.Zero, false); }

    public void Open(int slot, int tab = 0) { Slot = slot; Tab = tab; _rename = null; Visible = true; Refresh(); }

    public override void Refresh()
    {
        Clear();
        _rename = null;
        var pets = Pets();
        var p = pets.FirstOrDefault(x => x.Slot == Slot);
        if (p == null) { Visible = false; return; }
        if (_renaming) { DrawRename(p); return; }
        Img(Tab switch { 0 => 22800, 1 => 22810, _ => 22824 }, 0, 0, block: true);
        Txt(p.DisplayName, 165, 12, center: true);
        switch (Tab)
        {
            case 0: DrawStatus(p); break;
            case 1: DrawDetail(p); break;
            case 2: DrawTechs(p); break;
            case 3: DrawEquip(p); break;
        }
        // 翻页（在已有的宠物之间）、页码
        float py = Tab == 0 ? 378 : 383;
        Btn(22832, 22833, Tab == 0 ? 145 : 146, py, () => Flip(-1));
        Btn(22834, 22835, Tab == 0 ? 215 : 211, py, () => Flip(1));
        Txt($"{Slot + 1}/5", 194, py + 6, center: true);
        Btn(22830, 22831, 30, Tab == 0 ? 379 : 383, () => { Visible = false; BackToList?.Invoke(); });
        TabBar();
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }

    private void Flip(int d)
    {
        var slots = Pets().Select(x => x.Slot).OrderBy(x => x).ToList();
        if (slots.Count == 0) return;
        int i = slots.IndexOf(Slot); if (i < 0) i = 0;
        Slot = slots[(i + d + slots.Count) % slots.Count];
        Refresh();
    }

    // —— 底部标签栏：浅色底 (217,207,173)，4 个深色圆角按钮 60×21，x 4/72/139/208，y 413（截图量的）——
    private static readonly string[] TabNames = { "状 態", "明 細", "技 能", "装 備" };
    private void TabBar()
    {
        Body.AddChild(new ColorRect { Color = Color.Color8(217, 207, 173), Position = new Vector2(1, 410), Size = new Vector2(270, 27), MouseFilter = Control.MouseFilterEnum.Ignore });
        int[] xs = { 4, 72, 139, 208 };
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            var b = new TabButton { Position = new Vector2(xs[i], 413), Size = new Vector2(60, 21), Selected = i == Tab };
            Body.AddChild(b);
            Txt(TabNames[i], xs[i] + 30, 419, i == Tab ? new Color(0.4f, 1, 1) : Color.Color8(238, 237, 235), center: true);
            Hit(xs[i], 413, 60, 21, _ => { Tab = k; Refresh(); });
        }
    }

    private void DrawStatus(PetInfo p)
    {
        Txt(p.Level.ToString(), 84, 47, right: true);
        Txt(p.Exp.ToString(), 123, 70, right: true);
        Txt(p.NextExp.ToString(), 123, 93, right: true);
        Txt($"{p.Hp,3}/{p.MaxHp,4}".TrimStart(), 122, 116, right: true);
        Txt($"{p.Mp,3}/{p.MaxMp,4}".TrimStart(), 123, 139, right: true);
        int[] v = { p.Attack, p.Defense, p.Agility, p.Spirit, p.Recovery };
        for (int i = 0; i < 5; i++) Txt(v[i].ToString(), 84, 162 + 23 * i, right: true);
        Txt(p.Loyalty.ToString(), 83, 277, right: true);
        Txt(p.SkillSlots.ToString(), 83, 300, right: true);
        // 宠物形象（站着，朝左下）
        if (p.Anime > 0)
        {
            var s = new AnimSprite { Cache = Cache, AnimeId = p.Anime, Dir = 5, Act = 0, FallbackAct = 0, Position = new Vector2(196, 168) };
            Body.AddChild(s);
        }
        Body.AddChild(new ColorRect { Color = new Color(0, 0.9f, 0), Position = new Vector2(178, 216), Size = new Vector2(8, 8), MouseFilter = Control.MouseFilterEnum.Ignore });
        Txt(p.RaceName, 193, 238);
        Color[] ec = { new(0, 0.8f, 0), new(0, 0.5f, 1), new(0.95f, 0, 0), new(0.95f, 0.85f, 0) };
        for (int i = 0; i < 4; i++)
            if (p.Element[i] > 0)
                Body.AddChild(new ColorRect { Color = ec[i], Position = new Vector2(187, 287 + 20 * i), Size = new Vector2(66 * Math.Min(100, p.Element[i]) / 100f, 9), MouseFilter = Control.MouseFilterEnum.Ignore });
        Btn(22807, 22808, 30, 327, () => { _renaming = true; Refresh(); });
        // 展示：4.06 的按钮图档里没有，画一个；点了 = 宠物郵件/散步状态（LKQy 3），再点回待命
        bool shown = p.State == PetInfo.Walk;
        Body.AddChild(new TabButton { Position = new Vector2(30, 353), Size = new Vector2(80, 20), Light = true, Selected = shown });
        Txt("展 示", 70, 359, shown ? new Color(0.4f, 1, 1) : Color.Color8(40, 40, 40), center: true);
        int slot = p.Slot;
        Hit(30, 353, 80, 20, _ => SetState?.Invoke(slot, shown ? PetInfo.Standby : PetInfo.Walk));
    }

    private void DrawDetail(PetInfo p)
    {
        Txt(p.Level.ToString(), 87, 39, right: true);
        Txt($"{p.Hp,3}/{p.MaxHp,4}".TrimStart(), 121, 62, right: true);
        Txt($"{p.Mp,3}/{p.MaxMp,4}".TrimStart(), 121, 85, right: true);
        int[] v = { p.Attack, p.Defense, p.Agility, p.Spirit, p.Recovery };
        for (int i = 0; i < 5; i++) Txt(v[i].ToString(), 87, 108 + 23 * i, right: true);
        for (int i = 0; i < 6; i++) Txt($"{(p.Resist[i] >= 0 ? "+" : "")}{p.Resist[i]}", 87, 246 + 23 * i, right: true);
        // 基本值
        Txt(p.Points.ToString(), 237, 63, right: true);
        int[] b = { p.Vit, p.Str, p.Tgh, p.Spd, p.Mag };
        int slot = p.Slot;
        for (int i = 0; i < 5; i++)
        {
            Txt(b[i].ToString(), 216, 86 + 23 * i, right: true);
            int k = i;
            if (p.Points > 0) Btn(22812, 22813, 224, 81 + 23 * i, () => AddPoint?.Invoke(slot, k));
        }
        Txt(p.Loyalty.ToString(), 207, 207, right: true);
        Txt(p.SkillSlots.ToString(), 207, 230, right: true);
        int[] m = { p.Critical, p.Counter, p.Hit, p.Dodge };
        for (int i = 0; i < 4; i++) Txt($"{(m[i] >= 0 ? "+" : "")}{m[i]}", 218, 276 + 23 * i, right: true);
    }

    private void DrawTechs(PetInfo p)
    {
        int rows = Math.Max(1, Math.Min(10, p.SkillSlots > 0 ? p.SkillSlots : 10));
        for (int i = 0; i < 10; i++)
        {
            Img(22761, 10, 36 + 25 * i);
            var t = p.Techs.FirstOrDefault(x => x.Index == i);
            if (t == null) continue;
            Txt(t.Name, 40, 42 + 25 * i);
            Txt(t.Mp.ToString(), 252, 42 + 25 * i, right: true);
            // 拖一招到另一行 = 换位置（HJQp 宠物栏 a b）
            int from = i, slot = p.Slot;
            var h = Hit(10, 36 + 25 * i, 250, 22, _ => { }, box: false);
            h.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb) return;
                int to = from + (int)MathF.Floor(mb.Position.Y / 25f);
                if (to != from && to >= 0 && to < 10) SwapTechs?.Invoke(slot, from, to);
            };
        }
    }
    public Action<int, int, int>? SwapTechs;

    private static readonly (string Label, float X, float Y)[] EquipBoxes =
        { ("项圈", 29, 53), ("饰品", 29, 126), ("防具", 29, 200), ("饰品", 197, 126), ("水晶", 197, 200) };
    private void DrawEquip(PetInfo p)
    {
        for (int i = 0; i < EquipBoxes.Length; i++)
        {
            var (label, x, y) = EquipBoxes[i];
            Body.AddChild(new TabButton { Position = new Vector2(x, y), Size = new Vector2(46, 46), Light = true });
            var it = p.Equipment.FirstOrDefault(e => e.Slot == i);
            if (it != null && it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } tex)
                Body.AddChild(new TextureRect { Texture = tex, Position = new Vector2(x + (46 - tex.GetWidth()) / 2f, y + (46 - tex.GetHeight()) / 2f), MouseFilter = Control.MouseFilterEnum.Ignore });
            else Txt(label, x + 23, y + 17, Color.Color8(60, 40, 20), center: true);
        }
        if (p.Anime > 0) Body.AddChild(new AnimSprite { Cache = Cache, AnimeId = p.Anime, Dir = 5, Act = 0, FallbackAct = 0, Position = new Vector2(140, 210) });
        Txt(p.DisplayName, 12, 304);
        Txt("负重", 12, 323);
        Txt($"0/{p.LoadMax}", 52, 323, new Color(0.4f, 1, 1));
    }

    // —— 更改名字：22809 在 (0,0)；原名 (82,60)，新名字输入框 (75,93) 185×18，确定 22712 (40,160)，取消 22716 (160,160) ——
    private bool _renaming;
    private void DrawRename(PetInfo p)
    {
        Img(22809, 0, 0, block: true);
        Txt(p.DisplayName, 82, 60);
        _rename = new LineEdit { Position = new Vector2(75, 93), Size = new Vector2(185, 18), MaxLength = 16, Flat = true };
        _rename.AddThemeColorOverride("font_color", Colors.White);
        _rename.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _rename.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _rename.AddThemeFontSizeOverride("font_size", 13);
        _rename.TextSubmitted += _ => ConfirmRename();
        Body.AddChild(_rename);
        Btn(22712, 22713, 40, 160, ConfirmRename);
        Btn(22716, 22717, 160, 160, () => { _renaming = false; Refresh(); });
        Btn(22509, 22510, 246, 6, () => { _renaming = false; Refresh(); });
        _rename.GrabFocus();
        DisplayServer.VirtualKeyboardShow("", null, DisplayServer.VirtualKeyboardType.Default, 16);
    }
    private void ConfirmRename()
    {
        var name = _rename?.Text.Trim() ?? "";
        DisplayServer.VirtualKeyboardHide();
        if (name.Length > 0) Rename?.Invoke(Slot, name);
        _renaming = false; Refresh();
    }

    // 测试用
    public void TestTab(int t) { Tab = t; Refresh(); }
    public void TestRename(string name) { _renaming = true; Refresh(); if (_rename != null) _rename.Text = name; ConfirmRename(); }
    public void TestAddPoint(int which) => AddPoint?.Invoke(Slot, which);
    public bool Renaming => _renaming;
}

/// <summary>4.06 宠物详情底部的圆角标签按钮 / 浅色按钮（图档里没有，照截图颜色画）。</summary>
public partial class TabButton : Control
{
    public bool Selected, Light;
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        var bg = Light ? (Selected ? Color.Color8(120, 120, 120) : Color.Color8(205, 200, 190)) : (Selected ? Color.Color8(70, 54, 14) : Color.Color8(112, 88, 24));
        var sb = new StyleBoxFlat { BgColor = bg, CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5, BorderColor = Color.Color8(60, 45, 20), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 };
        DrawStyleBox(sb, new Rect2(Vector2.Zero, Size));
    }
}
