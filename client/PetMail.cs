using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 名片上点「郵件」先弹「選擇郵件」（官方 exe 0x445e80 起，2026-11-09 实测）：背景 23184 在 (0,0) 192×174，
/// 中心 x 95：一般郵件 23185 (y 60)、寵物郵件 23187 (y 96)、上一頁 22714 (y 144)（都是按钮中心）。
/// 只有宠物栏里有带「宠物邮件」技能（招式编号 89xx）的宠物时才弹这个，没有就直接进编辑邮件（实测）。
/// </summary>
public partial class MailTypeWindow : CgWindow
{
    public Action? Normal, Pet, Back;
    public Func<bool> HasMailPet = () => false;
    public override void _Ready() { Layer = 13; Visible = false; Build(Vector2.Zero, false); }

    public override void Refresh()
    {
        Clear();
        Img(23184, 0, 0, block: true);
        BtnC(23185, 23186, 95, 60, () => { Visible = false; Normal?.Invoke(); });
        BtnC(23187, 23188, 95, 96, () => { if (!HasMailPet()) { GameAudio.Se(55); return; } Visible = false; Pet?.Invoke(); });   // 官方：没有能寄的宠物响错误音
        BtnC(22714, 22715, 95, 144, () => { Visible = false; Back?.Invoke(); });
        Btn(22509, 22510, 166, 6, () => Visible = false);
    }

    /// <summary>按钮以 (cx,cy) 为中心（官方 0x476215 的模式 2）。</summary>
    protected TextureButton? BtnC(int img, int pressed, float cx, float cy, Action a)
    {
        var sz = Cache.ImageNo(img).Tex?.GetSize() ?? Vector2.Zero;
        return Btn(img, pressed, cx - sz.X / 2, cy - sz.Y / 2, a);
    }
}

/// <summary>
/// 宠物邮件：选宠物（官方 exe 0x446554 起）：背景 23189 在 (0,0)。按宠物栏 0–4 一行（第 k 栏行底 y = 59 + 28k，空着的栏也占行），
/// 只列有「宠物邮件」技能的；名字（有昵称用昵称）从 x 75、等级右对齐到 x 252（截图量的）。点一行（x 14–260、行底往上 20）= 选它。
/// 「取消」22710 中心 (140,205) → 回「選擇郵件」。
/// </summary>
public partial class PetMailPetsWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public Action<int>? Picked;
    public Action? Back;
    public override void _Ready() { Layer = 13; Visible = false; Build(Vector2.Zero, false); }

    public override void Refresh()
    {
        Clear();
        Img(23189, 0, 0, block: true);
        if (Session() is { } s)
            foreach (var p in s.Player.Pets.Where(p => p.Slot is >= 0 and < 5 && NetworkSession.CanPetMail(p)))
            {
                float y = 59 + 28 * p.Slot;
                Txt(p.DisplayName, 75, y - 13);
                Txt(p.Level.ToString(), 252, y - 13, right: true);
                int slot = p.Slot;
                Hit(14, y - 20, 246, 20, mb => { if (mb.ButtonIndex == MouseButton.Left) { Visible = false; Picked?.Invoke(slot); } });
            }
        var sz = Cache.ImageNo(22710).Tex?.GetSize() ?? Vector2.Zero;
        Btn(22710, 22711, 140 - sz.X / 2, 205 - sz.Y / 2, () => { Visible = false; Back?.Invoke(); });
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }

    public void TestPick(int slot) { Visible = false; Picked?.Invoke(slot); }
}

/// <summary>
/// 编辑宠物邮件（官方实测）：背景 23200 在 (0,0)。收件人头像 (20,48) 64×72 格子里居中、名字中心 (166,65)；
/// 右边道具框中心 (216,120)，道具名中心 (160,154)；「送出」23196 (36,186)、「上一頁」22714 (156,186)（回宠物表）；下面写信。
/// 同时右上开「選擇傳送物品」（PetMailItemWindow）。送出 = rMsv 名片 宠物栏 道具格 内容 0。
/// </summary>
public partial class PetMailEditWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public Action? Back, Sent;
    public int Card { get; private set; } = -1;
    public int PetSlot { get; private set; } = -1;
    public int ItemSlot = -1;
    private TextEdit _edit = null!;

    public override void _Ready()
    {
        Layer = 13; Visible = false; Build(Vector2.Zero, false);
        _edit = MailEditWindow.MakeEdit();
        Win.AddChild(_edit);
    }

    public string Text { get => _edit.Text; set => _edit.Text = value; }

    public void Open(int card, int petSlot)
    {
        Card = card; PetSlot = petSlot; ItemSlot = -1; _edit.Text = "";
        Visible = true; Refresh(); _edit.GrabFocus();
    }

    public override void Refresh()
    {
        Clear();
        var s = Session(); if (s == null) return;
        Img(23200, 0, 0, block: true);
        if (Card is >= 0 and < 60 && s.Cards[Card] is { } c)
        {
            if (c.Portrait > 0 && Cache.ImageNo(c.Portrait).Tex is { } face)
            {
                var sz = face.GetSize();
                Body.AddChild(new TextureRect { Texture = face, Position = new Vector2(20 + (64 - sz.X) / 2, 48 + (72 - sz.Y) / 2), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            Txt(c.Name, 166, 65, center: true);
        }
        var it = s.Player.Items.FirstOrDefault(i => i.Slot == ItemSlot);
        if (it != null && Cache.ImageNo(it.Graphic).Tex is { } t)
        {
            var sz = t.GetSize();
            Body.AddChild(new TextureRect { Texture = t, Position = new Vector2(216, 120) - sz / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            Txt(it.Name, 160, 154, center: true);
        }
        Btn(23196, 23197, 36, 186, DoSend);
        Btn(22714, 22715, 156, 186, () => { Visible = false; Back?.Invoke(); });
        Btn(22509, 22510, 246, 6, () => { Visible = false; Back?.Invoke(); });
        _edit.MoveToFront();
    }

    public void DoSend()
    {
        var t = _edit.Text.Replace("\n", " ").Trim();
        if (t.Length == 0 || Card < 0 || PetSlot < 0) return;
        Session()?.SendPetMail(Card, PetSlot, ItemSlot, t);
        GameAudio.Se(59);
        _edit.Text = ""; Visible = false; Sent?.Invoke();
    }
}

/// <summary>
/// 選擇傳送物品（官方 exe 0x446bb3 起）：背景 23205 (272×252) 贴右上 (688,0)。背包 20 格 5×4，第 k 格左上 (8+52(k%5), 36+53(k/5)) 47×47，
/// 道具图在格子中心；数量（可堆叠的，1 也画）。不能寄的（rU 标志第 1 位为 0）盖红叉 23040。
/// 双击能寄的道具 = 选中（青框），再双击同一格取消；不能寄的响错误音。
/// </summary>
public partial class PetMailItemWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public Func<int> Selected = () => -1;
    public Action<int>? Select;
    public override void _Ready() { Layer = 13; Visible = false; Build(new Vector2(688, 0), true); }

    public override void Refresh()
    {
        Clear();
        var s = Session(); if (s == null) return;
        Img(23205, 0, 0, block: true);
        var bag = s.Player.Items.Where(i => i.Slot >= 8).ToDictionary(i => i.Slot);
        for (int k = 0; k < 20; k++)
        {
            int slot = 8 + k;
            var c = new Vector2(8 + 52 * (k % 5), 36 + 53 * (k / 5));
            bag.TryGetValue(slot, out var it);
            if (it != null && Cache.ImageNo(it.Graphic).Tex is { } t)
            {
                var sz = t.GetSize();
                Body.AddChild(new TextureRect { Texture = t, Position = c + new Vector2(24, 24) - sz / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
                if (it.Stackable) Txt(it.Count.ToString(), c.X + 46, c.Y + 36, new Color(0.4f, 1, 1), right: true);
                if (!it.CanPetMail && Cache.ImageNo(23040).Tex is { } x)
                    Body.AddChild(new TextureRect { Texture = x, Position = c + new Vector2(24, 24) - x.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            if (slot == Selected()) Body.AddChild(new ReferenceRect { Position = c, Size = new Vector2(47, 47), BorderColor = new Color(0, 1, 1), BorderWidth = 1, EditorOnly = false, MouseFilter = Control.MouseFilterEnum.Ignore });
            Hit(c.X, c.Y, 47, 47, mb => { if (mb.ButtonIndex == MouseButton.Left && mb.DoubleClick) Pick(slot); });
        }
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }

    public void Pick(int slot)
    {
        var it = Session()?.Player.Items.FirstOrDefault(i => i.Slot == slot);
        if (it == null || !it.CanPetMail) { GameAudio.Se(55); return; }
        GameAudio.Se(79);
        Select?.Invoke(Selected() == slot ? -1 : slot);
        Refresh();
    }
}

/// <summary>
/// 宠物邮件的宠物出发 / 到达效果（lDuJ，官方 exe 0x40d3d2 建效果、0x40d2c2 每帧；按 60 帧/秒步进）。
/// 效果种类 = 宠物的 PetMailEffect（服务端给 0–3），出发码 2×种类、到达码 2×种类+1：
///   0（码 0/1）：三个分身绕中心转，每帧转 6°；出发先停 1 秒，然后半径每帧 +4（地图单位，一格 64），超过 800 结束；到达从 800 每帧 −4，到 0 结束。
///   1（码 2/3）：前 100 毫秒不画，然后往上跳（每帧 8 像素），200 毫秒后往下落，400 毫秒结束。
///   2（码 4/5）：闪烁，每 30 帧切一次显示/隐藏，显示过 3 次结束。
///   3：出发（码 6）往远处走开（离开玩家 13 格或 8 秒后结束；这里直接向右下走开）；到达（码 7）立刻结束。
/// 到达的效果结束后才显示那只宠物（对象由 lDuJ 的对象串建，见 NetworkSession.OnPetMailEffect）。
/// </summary>
public partial class PetMailFxNode : Node2D
{
    public TextureCache Cache = null!;
    public int Anime, Dir, Code, StandAct;
    public Vector2 Cell;
    public Action? Done;
    private readonly List<AnimSprite> _sprites = new();
    private double _acc, _ms;
    private int _frames, _shown;
    private float _angle, _radius, _height, _dirY = -1;
    private bool _visible;
    public bool Finished { get; private set; }

    public override void _Ready()
    {
        int n = Code <= 1 ? 3 : 1;
        for (int i = 0; i < n; i++)
        {
            var s = new AnimSprite { Cache = Cache, AnimeId = Anime, Dir = Dir, Act = StandAct, FallbackAct = StandAct, Visible = false };
            AddChild(s); _sprites.Add(s);
        }
        Position = Iso.ToScreen(Cell.X, Cell.Y);
        if (Code == 1) _radius = 800;
        if (Code == 7) Finish();
        else Place();
    }

    public override void _Process(double delta)
    {
        if (Finished) return;
        _acc += delta;
        while (_acc >= 1 / 60.0 && !Finished) { _acc -= 1 / 60.0; _ms += 1000 / 60.0; Step(); }
        if (!Finished) Place();
    }

    private void Step()
    {
        _frames++;
        switch (Code)
        {
            case 0:
                if (_ms > 1000) { _angle += 6; _radius += 4; }
                if (_radius > 800) Finish();
                break;
            case 1:
                _angle += 6; _radius -= 4;
                if (_radius <= 0) Finish();
                break;
            case 2: case 3:
                if (_ms >= 200) _dirY = 1;
                _visible = _ms > 100;
                if (_visible) _height += _dirY * 8;
                if (_ms >= 400) Finish();
                break;
            case 4: case 5:
                if (_frames % 30 == 0) { _visible = !_visible; if (!_visible) _shown++; }
                if (_shown >= 3) Finish();
                break;
            case 6:
                _visible = true;
                Cell += new Vector2(0.06f, 0.06f);
                if (_ms > 8000) Finish();
                break;
        }
    }

    private void Place()
    {
        switch (Code)
        {
            case 0: case 1:
                for (int i = 0; i < _sprites.Count; i++)
                {
                    float a = Mathf.DegToRad(_angle + 120 * i);
                    float dx = Mathf.Cos(a) * _radius / 64f, dy = Mathf.Sin(a) * _radius / 64f;
                    _sprites[i].Position = Iso.ToScreen(dx, dy);
                    _sprites[i].Visible = true;
                }
                break;
            case 2: case 3:
                _sprites[0].Position = new Vector2(0, Math.Min(0, _height)); _sprites[0].Visible = _visible; break;
            case 4: case 5:
                _sprites[0].Position = Vector2.Zero; _sprites[0].Visible = _visible; break;
            case 6:
                Position = Iso.ToScreen(Cell.X, Cell.Y); _sprites[0].Visible = true; break;
        }
    }

    private void Finish()
    {
        if (Finished) return;
        Finished = true;
        foreach (var s in _sprites) s.Visible = false;
        Done?.Invoke();
        QueueFree();
    }
}
