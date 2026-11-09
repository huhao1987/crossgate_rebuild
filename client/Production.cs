using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 生产（官方 4.06 客户端 bob 实测 2026-11-07）。技能详情里点制造类招式 → 选择秘笈；点采集类招式 → 直接开始采集。
/// 窗口都在 (0,0)（选择素材贴右在 (688,0)），跟技能窗口同一位置（打开时技能窗口收起）：
///  选择秘笈 23350、製造 23351、選擇素材 23352、結果 23353（制造）/ 23356（采集）、生産 23357。
/// 制造：选配方 → 在选择素材里双击要用的道具（黄框）→「登録」（客户端检查配方的材料够不够：材料登录了 / 材料不足 / 尚未选择材料）
///  →「執行」→ IPy a、"加工中" → 约 10 秒后发 sM → 服务端 JgO → 結果（重試 = 用同样的材料再登录一次）。
/// 采集：IPy 6、生産窗口"伐木中…"→ 约 7 秒后 sM → JgO → 結果（停止）→ 2 秒后自动继续；「停止」或走路就停（IPy 5），按钮变「重試」。
/// 私服客户端多出来的「自動」按钮没做。
/// </summary>
public partial class ProductionUi : Node
{
    public TextureCache Cache = null!;
    public Func<NetworkSession?> Session = () => null;
    public Func<int> PlayerAnime = () => 0;
    public Action? Opened;   // 打开生产窗口时（主程序收起技能窗口）
    public static double CraftSeconds = 10, GatherSeconds = 7, GatherPause = 2;

    public RecipeWindow Recipes { get; private set; } = null!;
    public CraftWindow Craft { get; private set; } = null!;
    public MaterialWindow Materials { get; private set; } = null!;
    public ProduceResultWindow Result { get; private set; } = null!;
    public GatherWindow Gathering { get; private set; } = null!;
    public ItemSkillWindow ItemSkill { get; private set; } = null!;
    public EngraveWindow Engrave { get; private set; } = null!;
    public static double ItemSkillSeconds = 12;
    public int Tech { get; private set; }
    public int TargetSlot { get; private set; } = -1;   // 鉴定 / 修理的道具格
    public string TargetName { get; private set; } = "";

    public SkillInfo? Skill { get; private set; }
    public RecipeInfo? Recipe { get; private set; }
    public bool Working { get; private set; }
    public bool GatherRunning { get; private set; }
    private int _gen;
    private List<int> _lastRegistered = new();

    public override void _Ready()
    {
        AddChild(Recipes = new RecipeWindow { Cache = Cache, Ui = this });
        AddChild(Craft = new CraftWindow { Cache = Cache, Ui = this });
        AddChild(Materials = new MaterialWindow { Cache = Cache, Ui = this });
        AddChild(Result = new ProduceResultWindow { Cache = Cache, Ui = this });
        AddChild(Gathering = new GatherWindow { Cache = Cache, Ui = this });
        AddChild(ItemSkill = new ItemSkillWindow { Cache = Cache, Ui = this });
        AddChild(Engrave = new EngraveWindow { Cache = Cache, Ui = this });
    }

    public void Bind(NetworkSession s)
    {
        s.RecipesChanged += slot => { if (Skill?.Slot == slot && Recipes.Visible) Recipes.Refresh(); };
        s.ProduceDone += OnDone;
        s.StatusChanged += () => { if (Materials.Visible) Materials.Refresh(); if (Craft.Visible) Craft.Refresh(); if (ItemSkill.Visible) ItemSkill.Refresh(); };
    }

    public bool AnyVisible => Recipes.Visible || Craft.Visible || Materials.Visible || Result.Visible || Gathering.Visible || ItemSkill.Visible || Engrave.Visible;

    void HideAll() { Recipes.Visible = Craft.Visible = Materials.Visible = Result.Visible = Gathering.Visible = ItemSkill.Visible = Engrave.Visible = false; }

    /// <summary>技能详情里点了某个招式。</summary>
    public void UseTech(SkillInfo sk, int tech)
    {
        var s = Session(); if (s == null) return;
        s.SkillIcon(sk.SkillId);   // 官方：点招式就发 klF（头顶图标）
        Tech = tech;
        if (sk.Type == 1) { OpenRecipes(sk); return; }
        if (sk.Type == 2) { OpenItemSkill(sk, tech); return; }
        if (sk.Type == 3) { StartTreat(sk, tech); return; }
        if (sk.Type == 4) { StartGather(sk); return; }
    }

    // ———— 鉴定 / 修理 / 刻印 ————
    public void OpenItemSkill(SkillInfo sk, int tech)
    {
        StopGather(); HideAll();
        Skill = sk; Tech = tech; Recipe = null; TargetSlot = -1;
        ItemSkill.Message = $"请选择要{sk.Name.Replace("武器", "").Replace("防具", "")}的道具。";
        ItemSkill.Visible = true; ItemSkill.Refresh(); Opened?.Invoke();
    }

    public void ChooseItem(int slot)
    {
        if (Working || Skill == null) return;
        var it = Session()?.Player.Items.FirstOrDefault(i => i.Slot == slot);
        if (it == null) return;
        TargetSlot = TargetSlot == slot ? -1 : slot;
        int mp = ItemMp(it);
        ItemSkill.Message = TargetSlot >= 0 ? $"需消耗{mp}点魔力。" : $"请选择要{Verb}的道具。";
        ItemSkill.Refresh();
    }
    /// <summary>官方：鉴定 / 修理要的魔力 = 道具等级 × 10（实测 1 级铜剑 10；招式表里的 MP 段对这类技能不准）。</summary>
    public static int ItemMp(ItemStack it) => Math.Max(1, it.Level) * 10;
    public string Verb => Skill?.SkillId switch { 219 => "鉴定", 217 or 218 => "修理", _ => Skill?.Name ?? "" };

    public void ExecuteItemSkill()
    {
        var s = Session(); if (s == null || Skill == null || Working) return;
        if (TargetSlot < 0 || s.Player.Items.FirstOrDefault(i => i.Slot == TargetSlot) is not { } it) { ItemSkill.Message = $"请选择要{Verb}的道具。"; ItemSkill.Refresh(); return; }
        int mp = ItemMp(it);
        if (s.Player.Mp < mp) { ItemSkill.Message = "魔力不足。"; ItemSkill.Refresh(); return; }
        TargetName = it.Name;
        if (Skill.SkillId == 220) { ItemSkill.Visible = false; Engrave.Open(); return; }   // 刻印：先开刻印窗口打字
        Working = true; int gen = ++_gen;
        s.SetWork(Skill.SkillId == 219 ? 12 : 13);   // 官方：鉴定 IPy c、修理 IPy d
        ItemSkill.Message = $"{Verb}中 . . ."; ItemSkill.Refresh();
        int slot = TargetSlot, sk = Skill.Slot, tech = Tech;
        GetTree().CreateTimer(ItemSkillSeconds).Timeout += () => { if (gen == _gen && Working) s.UseOnItem(sk, tech, slot); };
    }

    // ———— 急救 ————
    public void StartTreat(SkillInfo sk, int tech)
    {
        StopGather(); HideAll();
        Skill = sk; Tech = tech; Recipe = null;
        Session()?.RequestTreat(sk.Slot, tech);
    }

    // ———— 制造 ————
    public void OpenRecipes(SkillInfo sk)
    {
        StopGather(); HideAll();
        Skill = sk; Recipes.Page = 0;
        Session()?.RequestRecipes(sk.Slot);
        Recipes.Visible = true; Recipes.Refresh(); Opened?.Invoke();
    }

    public void ChooseRecipe(RecipeInfo r)
    {
        Recipe = r; HideAll();
        Craft.Registered.Clear(); Materials.Selected.Clear(); Materials.Message = "请选择材料并登录。";
        Craft.Visible = true; Materials.Visible = true; Craft.Refresh(); Materials.Refresh();
    }

    /// <summary>登录：选中的道具都是这个配方的材料，而且每样材料都够。</summary>
    public void Register()
    {
        var s = Session(); if (s == null || Recipe == null) return;
        var sel = Materials.Selected.Select(sl => s.Player.Items.FirstOrDefault(i => i.Slot == sl)).Where(i => i != null).Select(i => i!).ToList();
        if (sel.Count == 0) { Materials.Message = "尚未选择材料。"; Materials.Refresh(); return; }
        bool ok = Recipe.Materials.All(m => sel.Where(i => i.ItemId == m.ItemId).Sum(i => Math.Max(1, i.Count)) >= m.Count)
               && sel.All(i => Recipe.Materials.Any(m => m.ItemId == i.ItemId));
        if (!ok) { Materials.Message = "材料不足。"; Materials.Refresh(); return; }
        // 按配方材料的顺序排（服务端的 sM 里格子顺序跟配方一致）
        Craft.Registered = Recipe.Materials.Select(m => sel.First(i => i.ItemId == m.ItemId).Slot).ToList();
        _lastRegistered = Craft.Registered.ToList();
        Materials.Message = "材料登录了。"; Materials.Refresh(); Craft.Refresh();
    }

    public void Execute()
    {
        var s = Session(); if (s == null || Recipe == null || Skill == null || Working) return;
        if (Craft.Registered.Count == 0) { Materials.Message = "尚未选择材料。"; Materials.Refresh(); return; }
        if (s.Player.Mp < Recipe.Mp) { Materials.Message = "魔力不足。"; Materials.Refresh(); return; }
        Working = true; int gen = ++_gen;
        s.SetWork(10);
        Materials.Message = "加工中"; Materials.Refresh(); Craft.Refresh();
        var slots = Craft.Registered.ToList(); var rid = Recipe.Id; var skill = Skill.Slot; int tech = Math.Max(0, Recipe.Level - 1);
        GetTree().CreateTimer(CraftSeconds).Timeout += () => { if (gen == _gen && Working) s.Craft(skill, 0, rid, slots); };
        _ = tech;
    }

    public void CancelCraft()
    {
        if (Working) { _gen++; Working = false; Session()?.SetWork(5); }
        HideAll();
    }

    /// <summary>結果的「重試」：回到製造，用同样的材料（还在的话）再登录。</summary>
    public void Retry()
    {
        if (Skill == null) return;
        if (Skill.Type == 4) { StartGather(Skill); return; }
        if (Skill.Type == 2) { int keep = TargetSlot; OpenItemSkill(Skill, Tech); if (keep >= 0) ChooseItem(keep); return; }
        if (Skill.Type == 3) { StartTreat(Skill, Tech); return; }
        if (Recipe == null) return;
        ChooseRecipe(Recipe);
        var s = Session();
        if (s != null && Recipe.Materials.All(m => s.Player.Items.Any(i => i.ItemId == m.ItemId)))
        {
            foreach (var m in Recipe.Materials) Materials.Selected.Add(s.Player.Items.First(i => i.ItemId == m.ItemId).Slot);
            Register();
        }
        else { Materials.Message = "材料不足。"; Materials.Refresh(); }
    }

    // ———— 采集 ————
    public void StartGather(SkillInfo sk)
    {
        var s = Session(); if (s == null) return;
        HideAll(); Skill = sk; Recipe = null;
        GatherRunning = true; Working = true; int gen = ++_gen;
        s.SetWork(6);
        Gathering.Running = true; Gathering.Visible = true; Gathering.Refresh(); Opened?.Invoke();
        GetTree().CreateTimer(GatherSeconds).Timeout += () => { if (gen == _gen && GatherRunning) s.Gather(sk.Slot); };
    }

    public void StopGather()
    {
        if (!GatherRunning) return;
        _gen++; GatherRunning = false; Working = false;
        Session()?.SetWork(5);
        Result.Visible = false;
        Gathering.Running = false; Gathering.Visible = true; Gathering.Refresh();
    }

    /// <summary>走路、进战斗、换图：采集停下，制造中断。</summary>
    public void Interrupt()
    {
        if (GatherRunning) StopGather();
        else if (Working && Skill?.Type == 1) { _gen++; Working = false; Session()?.SetWork(5); Materials.Message = "请选择材料并登录。"; Materials.Refresh(); }
        else if (Working && Skill?.Type == 2) { _gen++; Working = false; Session()?.SetWork(5); ItemSkill.Message = $"请选择要{Verb}的道具。"; ItemSkill.Refresh(); }
    }

    public void CloseAll()
    {
        if (AnyVisible) Session()?.ClearSkillIcon();
        if (GatherRunning) { _gen++; GatherRunning = false; Working = false; Session()?.SetWork(5); }
        else if (Working) CancelCraft();
        HideAll();
    }

    private void OnDone(NetworkSession.ProduceResult r)
    {
        var s = Session(); if (s == null || Skill == null) return;
        LastResult = r;
        if (r.Type == 4)
        {
            if (!GatherRunning) return;
            int gen = _gen;
            s.SetWork(5);
            Gathering.Visible = false;
            Result.Show(Skill, r, gather: true, Recipe);
            GetTree().CreateTimer(GatherPause).Timeout += () =>
            {
                if (gen != _gen || !GatherRunning) return;
                Result.Visible = false;
                StartGather(Skill);
            };
            return;
        }
        if (r.Type is 2 or 3)
        {
            if (r.Type == 2) { Working = false; _gen++; s.SetWork(5); }
            HideAll();
            Result.Show(Skill, r, gather: false, Recipe);
            return;
        }
        Working = false; _gen++;
        s.SetWork(5);
        Craft.Visible = false; Materials.Visible = false;
        Result.Show(Skill, r, gather: false, Recipe);
    }
    /// <summary>刻印窗口按「刻印」：马上发 sM（官方不发 IPy、不等）。</summary>
    public void DoEngrave(string text)
    {
        var s = Session(); if (s == null || Skill == null || Working || TargetSlot < 0) return;
        text = text.Trim();
        if (text.Length == 0) return;
        Working = true; _gen++;
        Engrave.Visible = false;
        s.UseOnItem(Skill.Slot, Tech, TargetSlot, text);
    }
    public NetworkSession.ProduceResult? LastResult { get; private set; }
    public string TreatName = "";
    /// <summary>鉴定后道具名字会变（"单手剑？" → "铜剑"），官方结果里显示新名字。</summary>
    public string TargetNameNow => Session()?.Player.Items.FirstOrDefault(i => i.Slot == TargetSlot)?.Name ?? TargetName;
    public int TargetImage => Session()?.Player.Items.FirstOrDefault(i => i.Slot == TargetSlot)?.Graphic ?? 0;
}

/// <summary>选择秘笈（23350）：技能名、等级、魔法（现在/最大）；8 行配方（等级 N 名字 MP）；下面说明、◀ 页 ▶、取消。</summary>
public partial class RecipeWindow : CgWindow
{
    public ProductionUi Ui = null!;
    public int Page;
    private int _hover = -1;
    public override void _Ready() { Layer = 16; Visible = false; Build(Vector2.Zero, false); }

    public override void Refresh()
    {
        Clear();
        var s = Ui.Session(); var sk = Ui.Skill;
        if (s == null || sk == null) return;
        Img(23350, 0, 0, block: true);
        Btn(22509, 22510, 318, 6, () => Ui.CloseAll());
        Txt(sk.Name, 104, 43, center: true);
        Txt(sk.Level.ToString(), 73, 65);
        Txt($"{s.Player.Mp}/{s.Player.MaxMp}", 321, 65, right: true);
        var list = sk.Recipes;
        int pages = Math.Max(1, (list.Count + 7) / 8);
        Page = Math.Clamp(Page, 0, pages - 1);
        for (int k = 0; k < 8; k++)
        {
            int i = Page * 8 + k; if (i >= list.Count) break;
            var r = list[i]; float y = 89 + 23 * k;
            var c = i == _hover ? new Color(1, 0.2f, 0.2f) : Colors.White;   // 第 7 段（Ok）的意思没弄清：官方 961 是 0 也照样白字
            Txt($"等级 {r.Level}", 15, y + 7, c);
            Txt(r.Name, 72, y + 7, c);
            Txt(r.Mp.ToString(), 315, y + 8, c, right: true);
            int idx = i;
            var h = Hit(12, y, 302, 20, _ => Ui.ChooseRecipe(r));
            h.MouseEntered += () => { _hover = idx; Refresh(); };
        }
        if (_hover >= 0 && _hover < list.Count) Txt(list[_hover].Desc, 40, 305);
        Btn(22832, 22833, 46, 345, () => { Page = (Page + pages - 1) % pages; Refresh(); });
        Btn(22834, 22835, 140, 345, () => { Page = (Page + 1) % pages; Refresh(); });
        Txt($"{Page + 1}/ {pages}", 109, 355, center: true);
        Btn(22710, 22711, 210, 346, () => Ui.CloseAll());
    }

    public void TestChoose(int i) { var sk = Ui.Skill; if (sk != null && i < sk.Recipes.Count) Ui.ChooseRecipe(sk.Recipes[i]); }
}

/// <summary>製造（23351）：配方名、MP；材料 5 行（名字、数量）；登录了的材料放在右下 3×2 格；中间站着自己；執行 / 取消。</summary>
public partial class CraftWindow : CgWindow
{
    public ProductionUi Ui = null!;
    public List<int> Registered = new();   // 背包格
    public override void _Ready() { Layer = 16; Visible = false; Build(Vector2.Zero, false); }

    public override void Refresh()
    {
        Clear();
        var s = Ui.Session(); var r = Ui.Recipe;
        if (s == null || r == null) return;
        Img(23351, 0, 0, block: true);
        Btn(22509, 22510, 318, 6, () => Ui.CancelCraft());
        Txt(r.Name, 138, 45, center: true);
        Txt(r.Mp.ToString(), 310, 46, right: true);
        for (int m = 0; m < r.Materials.Count && m < 5; m++)
        {
            Txt(r.Materials[m].Name, 20, 71 + 24 * m);
            Txt(r.Materials[m].Count.ToString(), 305, 72 + 24 * m, right: true);
        }
        for (int k = 0; k < Registered.Count && k < 5; k++)
        {
            var it = s.Player.Items.FirstOrDefault(i => i.Slot == Registered[k]);
            if (it == null) continue;
            var p = new Vector2(176 + 53 * (k % 3), 212 + 53 * (k / 3));
            if (it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } tex)
                Body.AddChild(new TextureRect { Texture = tex, Position = p + (new Vector2(46, 46) - tex.GetSize()) / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            if (it.Count > 1) Txt(it.Count.ToString(), p.X + 44, p.Y + 33, new Color(0.4f, 1, 1), right: true);
        }
        if (Ui.PlayerAnime() is int an and > 0)
        {
            var holder = new Node2D { Position = new Vector2(91, 292) };
            holder.AddChild(new AnimSprite { Cache = Cache, AnimeId = an, Act = 0, FallbackAct = 0, Dir = 4 });
            Body.AddChild(holder);
        }
        if (Ui.Working) Img(23362, 60, 348);
        else Btn(23361, 23360, 60, 348, () => Ui.Execute());
        Btn(22710, 22711, 180, 348, () => Ui.CancelCraft());
    }
}

/// <summary>選擇素材（23352，贴右 (688,0)）：背包 20 格（双击选上 = 黄框），提示一行，「登録」。</summary>
public partial class MaterialWindow : CgWindow
{
    public ProductionUi Ui = null!;
    public readonly SortedSet<int> Selected = new();
    public string Message = "";
    private long _lastClick; private int _lastSlot = -1;
    public override void _Ready() { Layer = 16; Visible = false; Build(new Vector2(688, 0), true); }

    public static Vector2 Cell(int c) => new(10 + MathF.Round(51.5f * (c % 5)), 35 + 52 * (c / 5));

    public override void Refresh()
    {
        Clear();
        var s = Ui.Session(); if (s == null) return;
        Img(23352, 0, 0, block: true);
        Btn(22509, 22510, 246, 6, () => Ui.CancelCraft());
        for (int c = 0; c < 20; c++)
        {
            int slot = c + 8; var p = Cell(c);
            var it = s.Player.Items.FirstOrDefault(i => i.Slot == slot);
            if (it != null)
            {
                if (it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } tex)
                    Body.AddChild(new TextureRect { Texture = tex, Position = p + (new Vector2(46, 46) - tex.GetSize()) / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
                if (it.Count > 1) Txt(it.Count.ToString(), p.X + 44, p.Y + 33, new Color(0.4f, 1, 1), right: true);
                if (Selected.Contains(slot)) Body.AddChild(new YellowBox { Position = p - Vector2.One, Size = new Vector2(48, 48) });
            }
            if (!Ui.Working) Hit(p.X, p.Y, 46, 46, _ => Click(slot));
        }
        Txt(Message, 136, 289, center: true);
        if (Ui.Working) Img(23365, 100, 347);
        else Btn(23363, 23364, 100, 347, () => Ui.Register());
    }

    private void Click(int slot)
    {
        long now = (long)Time.GetTicksMsec();
        bool dbl = slot == _lastSlot && now - _lastClick < 500;
        _lastClick = now; _lastSlot = slot;
        if (!dbl) return;
        if (Ui.Session()?.Player.Items.Any(i => i.Slot == slot) != true) return;
        if (!Selected.Remove(slot)) Selected.Add(slot);
        Refresh();
    }

    public void TestSelect(int slot) { if (!Selected.Remove(slot)) Selected.Add(slot); Refresh(); }
}

/// <summary>結果（制造 23353 / 采集 23356）：技能名、等级、经验值、下一级；成品名；"的合成成功了。"/"发现了。"；成品图；失去的材料、技能经验；重試 / 停止。</summary>
public partial class ProduceResultWindow : CgWindow
{
    public ProductionUi Ui = null!;
    public string Text { get; private set; } = "";
    private SkillInfo? _sk; private NetworkSession.ProduceResult? _r; private bool _gather; private RecipeInfo? _recipe;
    public override void _Ready() { Layer = 16; Visible = false; Build(Vector2.Zero, false); }

    public void Show(SkillInfo sk, NetworkSession.ProduceResult r, bool gather, RecipeInfo? recipe)
    {
        _sk = sk; _r = r; _gather = gather; _recipe = recipe;
        Visible = true; Refresh();
    }

    public override void Refresh()
    {
        Clear();
        if (_sk == null || _r == null) return;
        var r = _r;
        Img(_gather ? 23356 : 23353, 0, 0, block: true);
        Btn(22509, 22510, 246, 6, () => Ui.CloseAll());
        Txt(_sk.Name, 82, 43, center: true);
        Txt(_sk.Level.ToString(), 250, 45, right: true);
        Txt(_sk.Exp.ToString(), 60, 67);
        Txt(_sk.NextExp.ToString(), 202, 67);
        var lines = new List<string>();
        string name = _gather ? r.Name : r.Type == 2 ? Ui.TargetNameNow : r.Type == 3 ? Ui.TreatName : _recipe?.Name ?? "";
        string msg;
        if (_gather) msg = r.Ok ? "发现了。" : "什么也没发现。";
        else if (r.Type == 2) msg = $"的{Ui.Verb}{(r.Ok ? "成功了。" : "失败了。")}";
        else if (r.Type == 3) msg = $"{_sk.Name}{(r.Ok ? "成功了。" : "失败了。")}";
        else msg = r.Ok ? "的合成成功了。" : "的合成失败了。";
        if (name.Length > 0) Txt(name, _gather ? 140 : 134, 96, center: true);
        Txt(msg, 10, 120);
        int img = r.Type == 2 ? Ui.TargetImage : r.Image;
        if (r.Type == 3 && r.Ok && r.Heal > 0) lines.Add($"回复了{r.Heal}点生命力。");
        if ((r.Ok || r.Type == 2) && img > 0 && Cache.ImageNo(img).Tex is { } tex)
            Body.AddChild(new TextureRect { Texture = tex, Position = new Vector2(193, 184) - tex.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
        foreach (var l in r.Lost) lines.Add($"{l} 失去了！");
        if (r.Exp > 0) lines.Add($"技能经验值 +{r.Exp}");
        for (int i = 0; i < lines.Count; i++) Txt(lines[i], 10, 241 + 18 * i);
        Text = string.Join(" / ", new[] { name + msg }.Concat(lines));
        if (Ui.PlayerAnime() is int an and > 0)
        {
            var holder = new Node2D { Position = new Vector2(82, 216) };
            holder.AddChild(new AnimSprite { Cache = Cache, AnimeId = an, Act = r.Ok ? 13 : 0, FallbackAct = 0, Dir = 4 });
            Body.AddChild(holder);
        }
        if (_gather) Btn(23395, 23396, 95, 409, () => Ui.StopGather());
        else Btn(23400, 23401, 95, 409, () => Ui.Retry());
    }
}

/// <summary>生産（23357）：采集时显示自己在干活、"伐木中…"、停止（停下后变重試）。</summary>
public partial class GatherWindow : CgWindow
{
    public ProductionUi Ui = null!;
    public bool Running;
    public override void _Ready() { Layer = 16; Visible = false; Build(Vector2.Zero, false); }

    public override void Refresh()
    {
        Clear();
        var sk = Ui.Skill; if (sk == null) return;
        Img(23357, 0, 0, block: true);
        Btn(22509, 22510, 246, 6, () => Ui.CloseAll());
        if (Ui.PlayerAnime() is int an and > 0)
        {
            var holder = new Node2D { Position = new Vector2(135, 138) };
            holder.AddChild(new AnimSprite { Cache = Cache, AnimeId = an, Act = Running ? 6 : 0, FallbackAct = 0, Dir = 4 });
            Body.AddChild(holder);
        }
        if (Running) { Txt($"{sk.Name}中 . . .", 145, 186, center: true); Btn(23395, 23396, 95, 218, () => Ui.StopGather()); }
        else Btn(23400, 23401, 95, 218, () => Ui.StartGather(sk));
    }
}

/// <summary>
/// 刻印（23355，272×104 贴右在 (688,0)，官方实测）：選擇物品里选好道具按「刻印」后出现。蓝色输入条 (13,39) 152×18 打要刻的字，
/// 「刻印」23372 在 (165,71) → sM 栏 招式 格 文字。关掉 = 不刻。
/// </summary>
public partial class EngraveWindow : CgWindow
{
    public ProductionUi Ui = null!;
    private LineEdit _edit = null!;
    public string Text { get => _edit.Text; set => _edit.Text = value; }
    public override void _Ready()
    {
        Layer = 16; Visible = false; Build(new Vector2(688, 0), true);
        _edit = new LineEdit { Position = new Vector2(16, 39), Size = new Vector2(148, 18), Flat = true, MaxLength = 16, ContextMenuEnabled = false };
        _edit.AddThemeColorOverride("font_color", Colors.White);
        _edit.AddThemeFontSizeOverride("font_size", 13);
        _edit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _edit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _edit.TextSubmitted += t => Ui.DoEngrave(t);
        Win.AddChild(_edit);
    }
    public void Open() { _edit.Text = ""; Visible = true; Refresh(); _edit.GrabFocus(); }
    public override void Refresh()
    {
        Clear();
        Img(23355, 0, 0, block: true);
        Btn(22509, 22510, 246, 6, () => Ui.CloseAll());
        Btn(23372, 23373, 165, 71, () => Ui.DoEngrave(_edit.Text));
        _edit.MoveToFront();
    }
}

/// <summary>
/// 選擇物品（23354，272×440 贴右在 (688,0)，官方 bob 实测）：鉴定 / 修理 / 刻印用。背包 20 格（同選擇素材的格子），
/// 双击选一件（黄框），下面一行提示（"请选择要鉴定的道具。" → "需消耗10点魔力。" → "鉴定中 . . ."），自己站在 (74,404)，
/// 按钮 修理 23366 / 鑑定 23369 / 刻印 23372（+1 按下、+2 灰）在 (160,398)。鼠标停在道具上，下半部显示道具名（蓝底）。
/// </summary>
public partial class ItemSkillWindow : CgWindow
{
    public ProductionUi Ui = null!;
    public string Message = "";
    private long _lastClick; private int _lastSlot = -1;
    private Control? _tip;
    public override void _Ready() { Layer = 16; Visible = false; Build(new Vector2(688, 0), true); }

    public override void Refresh()
    {
        Clear(); _tip = null;
        var s = Ui.Session(); if (s == null || Ui.Skill == null) return;
        Img(23354, 0, 0, block: true);
        Btn(22509, 22510, 246, 6, () => Ui.CloseAll());
        for (int c = 0; c < 20; c++)
        {
            int slot = c + 8; var p = MaterialWindow.Cell(c);
            var it = s.Player.Items.FirstOrDefault(i => i.Slot == slot);
            if (it == null) continue;
            if (it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } tex)
                Body.AddChild(new TextureRect { Texture = tex, Position = p + (new Vector2(46, 46) - tex.GetSize()) / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            if (it.Count > 1) Txt(it.Count.ToString(), p.X + 44, p.Y + 33, new Color(0.4f, 1, 1), right: true);
            if (Ui.TargetSlot == slot) Body.AddChild(new YellowBox { Position = p - Vector2.One, Size = new Vector2(48, 48) });
            if (Ui.Working) continue;
            var h = Hit(p.X, p.Y, 46, 46, _ => Click(slot));
            var item = it;
            h.MouseEntered += () => ShowName(item.Name); h.MouseExited += () => { _tip?.QueueFree(); _tip = null; };
        }
        Txt(Message, 8, 270);
        if (Ui.PlayerAnime() is int an and > 0)
        {
            var holder = new Node2D { Position = new Vector2(74, 404) };
            holder.AddChild(new AnimSprite { Cache = Cache, AnimeId = an, Act = Ui.Working ? 6 : 0, FallbackAct = 0, Dir = 4 });
            Body.AddChild(holder);
        }
        int btn = Ui.Skill.SkillId switch { 219 => 23369, 217 or 218 => 23366, _ => 23372 };
        if (Ui.Working) Img(btn + 2, 160, 398);
        else Btn(btn, btn + 1, 160, 398, () => Ui.ExecuteItemSkill());
    }

    private void ShowName(string name)
    {
        _tip?.QueueFree();
        _tip = new Control { Position = new Vector2(4, 247), MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddChild(new ColorRect { Color = new Color(0, 0, 0.55f), Size = new Vector2(264, 178), MouseFilter = Control.MouseFilterEnum.Ignore });
        _tip.AddChild(new ColorRect { Color = new Color(0.85f, 0.75f, 0.55f), Position = new Vector2(0, 21), Size = new Vector2(264, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        float w = name.Sum(CgText.Cells) * CgText.CellW;
        _tip.AddChild(new CgText { Text = name, Position = new Vector2(132 - w / 2, 3) });
        Body.AddChild(_tip);
    }

    private void Click(int slot)
    {
        long now = (long)Time.GetTicksMsec();
        bool dbl = slot == _lastSlot && now - _lastClick < 500;
        _lastClick = now; _lastSlot = slot;
        if (dbl) Ui.ChooseItem(slot);
    }
}
