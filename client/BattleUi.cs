using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 战斗界面：敌方在左上、我方在右下（斜向站位，和地图同一套等距方向）。
/// 每回合的事件按顺序播放：行动者冲向目标 → 攻击动作 → 目标受击/闪避 → 伤害数字 → 血条变化 → 倒下 → 回位。
/// 播放期间指令按钮禁用；会话可能已经结算了多个回合（或战斗已结束），事件会排队依次播完再关闭界面。
/// 攻击/受击/倒下的动作号来自配置（act_attack / act_hurt / act_dead），缺失时退回站立动作。
/// </summary>
public partial class BattleUi : CanvasLayer
{
    [Signal] public delegate void CommandChosenEventHandler(int kind);
    /// <summary>带目标的指令（官方界面：点"攻擊"后再点敌人）。</summary>
    [Signal] public delegate void CommandTargetEventHandler(int kind, int target);
    /// <summary>带参数的指令（道具：参数 = 背包格子）。</summary>
    [Signal] public delegate void CommandArgEventHandler(int kind, int target, int arg);
    /// <summary>战斗里"物品"按钮列出的道具：(格子, 道具编号, 数量)。由 Main 提供。</summary>
    public Func<IReadOnlyList<(int Slot, int ItemId, int Count)>>? BagItems;
    public Func<int, (string Name, int Image)>? ItemLook;
    /// <summary>技能：(技能, 第几招, 目标)。</summary>
    [Signal] public delegate void CommandSkillEventHandler(int kind, int target, int arg, int level);
    /// <summary>人物技能、技能栏总数、MP、出战宠物（联网由 NetworkSession 提供；单机暂时没有）。</summary>
    public Func<IReadOnlyList<SkillInfo>>? Skills;
    public Func<int>? SkillSlotsTotal;
    public Func<(int Mp, int MaxMp)>? PlayerMp;
    public Func<PetInfo?>? BattlePet;

    public TextureCache Cache = null!;
    public GameConfig Cfg = null!;
    /// <summary>播放速度倍率（1 = 正常；测试里可以调快）。</summary>
    public float Speed = 1f;

    private Control _root = null!;
    private Node2D _stage = null!;
    private Label _status = null!, _log = null!;
    private sealed class Unit
    {
        public int Id, Side, MaxHp, Hp, Level;
        public AnimSprite Sprite = null!;
        public ProgressBar Bar = null!;
        public Label Tag = null!;
        public Vector2 Home;
        public bool Down;
    }
    private readonly Dictionary<int, Unit> _units = new();
    private readonly List<Button> _buttons = new();
    private readonly Queue<Func<Task>> _queue = new();
    private bool _running;
    private int _playerId;
    private int _gen;   // 每场战斗递增；上一场残留的播放任务发现代号变了就停止
    private readonly List<string> _lines = new();

    /// <summary>正在播放或还有待播放的内容。</summary>
    public bool Busy => _running || _queue.Count > 0;
    public int PlayedEvents { get; private set; }
    public int AttackAnimsShown { get; private set; }

    public override void _Ready()
    {
        Layer = 10;
        _root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_root);
        _root.GuiInput += OnRootInput;
        _grid = new BattleGrid { Cache = Cache, MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 };
        _root.AddChild(_grid);
        _stage = new Node2D { YSortEnabled = true };
        _root.AddChild(_stage);
        _status = new Label { Position = new Vector2(24, 16) };
        _status.AddThemeFontSizeOverride("font_size", 20);
        _root.AddChild(_status);
        _log = new Label { AnchorTop = 1, AnchorBottom = 1, OffsetLeft = 24, OffsetTop = -170, OffsetRight = 700, OffsetBottom = -12, AutowrapMode = TextServer.AutowrapMode.Word, VerticalAlignment = VerticalAlignment.Bottom };
        _root.AddChild(_log);
        if (BuildOfficialPanel()) { Visible = false; return; }
        var bar = new HBoxContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -380, OffsetTop = -76, OffsetRight = -16, OffsetBottom = -16, Alignment = BoxContainer.AlignmentMode.End };
        _root.AddChild(bar);
        foreach (var (text, kind) in new[] { ("攻击", CommandKind.Attack), ("防御", CommandKind.Guard), ("逃跑", CommandKind.Escape) })
        {
            var b = new Button { Text = text, CustomMinimumSize = new Vector2(110, 56) };
            b.Pressed += () => { if (!Busy) EmitSignal(SignalName.CommandChosen, (int)kind); };
            bar.AddChild(b); _buttons.Add(b);
        }
        Visible = false;
    }

    private Vector2 Size => GetViewport().GetVisibleRect().Size;

    private static readonly Vector2[] Slots =
    {
        new(480, 370), new(540, 320), new(420, 420), new(600, 270), new(360, 430),
        new(420, 327), new(480, 277), new(360, 377), new(540, 227), new(300, 417),
        new(158, 188), new(218, 138), new(98, 238), new(278, 88), new(60, 288),
        new(210, 240), new(270, 190), new(150, 290), new(330, 140), new(90, 340),
    };
    /// <summary>单机模式敌人依次占的位置（先前排中间，再两侧，再后排）。</summary>
    private static readonly int[] SlotOrder = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    private Vector2 SlotPos(int slot) => Size / 2 + (Slots[Math.Clamp(slot, 0, 19)] - new Vector2(320, 240));

    private BattleGrid _grid = null!;
    /// <summary>这场战斗的底图（联网 = EN 第 2 段）。</summary>
    public Func<int>? Backdrop;
    public void Show(BattleView v)
    {
        _grid.Backdrop = Backdrop?.Invoke() ?? 0; _grid.Fix = Cfg.BattleBgFix; _grid.QueueRedraw();
        foreach (var c in _stage.GetChildren()) c.QueueFree();
        _gen++; _units.Clear(); _queue.Clear(); _lines.Clear(); _running = false;
        PlayedEvents = 0; AttackAnimsShown = 0; _turnStart = -1; _picking = false; _slot = 0; _hasPet = BattlePet?.Invoke() != null;
        Recording = false; if (_recBtn != null && ExeArt.Bmp(175) is { } rb) _recBtn.TextureNormal = _recBtn.TextureHover = rb;
        _playerId = v.PlayerUnitId;
        // 站位：官方 20 个位置的屏幕坐标（640×480 画面，来自 CGAssistant 的位置表），居中放到当前画面上。
        // 联网模式单位 Id 就是位置号（0–9 我方、10–19 敌方）；单机模式按顺序映射。
        var enemies = v.Units.Where(u => u.Side == 1).ToList();
        for (int i = 0; i < enemies.Count; i++)
            AddUnit(enemies[i], SlotPos(enemies[i].Id >= 10 ? enemies[i].Id : 10 + SlotOrder[i % 10]), Iso.AnimeDirFor(0, 1, Cfg.DirOffset));
        var me = v.Units.First(u => u.Id == v.PlayerUnitId);
        AddUnit(me with { AnimeId = me.AnimeId != 0 ? me.AnimeId : Cfg.PlayerAnime }, SlotPos(me.Id is >= 0 and < 10 ? me.Id : 0), Iso.AnimeDirFor(0, -1, Cfg.DirOffset));
        // 我方其他单位（宠物、队友）：联网时位置号就是 Id（实测带宠物时人物在 5、宠物在 0）
        foreach (var u in v.Units.Where(u => u.Side == 0 && u.Id != v.PlayerUnitId && u.Id is >= 0 and < 10))
            AddUnit(u, SlotPos(u.Id), Iso.AnimeDirFor(0, -1, Cfg.DirOffset));
        Say("遭遇敌人！");
        UpdateStatus(v.Turn);
        Visible = true;
        SetButtons();
    }

    private void AddUnit(UnitView u, Vector2 pos, int dir)
    {
        // 原版战斗画面是 640×480，这里放大显示；血条和名字反向缩放保持原大小
        const float scale = 1f;   // 官方战斗不放大，与地图同比例
        var s = new AnimSprite { Cache = Cache, AnimeId = u.AnimeId, Dir = dir, Act = Cfg.ActStand, FallbackAct = 0, Position = pos, Scale = Vector2.One * scale, SoundCue = f => GameAudio.I?.Cue(f) };
        if (u.AnimeId == 0) s.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-16, -40), new Vector2(16, -40), new Vector2(16, 0), new Vector2(-16, 0) }, Color = u.Side == 0 ? Colors.SkyBlue : Colors.IndianRed });
        // 官方：名字在头顶（我方青色、敌方白色）；只有我方脚下有一条小血条，敌人不显示血量
        var bar = new ProgressBar { MaxValue = Math.Max(1, u.MaxHp), Value = u.Hp, ShowPercentage = false, Size = new Vector2(32, 4), Position = new Vector2(-16, 4), Visible = u.Side == 0 };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.1f), BorderColor = new Color(0.75f, 0.75f, 0.75f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(0.85f, 0.15f, 0.15f) });
        var name = u.Name.Length > 0 ? u.Name : (u.Side == 0 ? "你" : $"Lv{u.Level}");
        var tag = NameTag.Make(name, u.Side == 0 ? NameTag.PlayerColor : NameTag.NpcColor, -92);
        s.AddChild(bar); s.AddChild(tag);
        _stage.AddChild(s);
        _units[u.Id] = new Unit { Id = u.Id, Side = u.Side, MaxHp = u.MaxHp, Hp = u.Hp, Level = u.Level, Sprite = s, Bar = bar, Tag = tag, Home = pos, Down = !u.Alive };
    }

    private void UpdateStatus(int turn)
    {
        if (_units.TryGetValue(_playerId, out var me)) _status.Text = $"第 {turn + 1} 回合    HP {me.Hp}/{me.MaxHp}";
    }

    private void SetButtons()
    {
        foreach (var b in _buttons) b.Disabled = Busy;
        if (_panel != null)
        {
            if (Busy) { _picking = false; _slot = 0; }
            ApplySlot();
            _msg!.Text = _picking ? "请选择目标。" : "";
            if (!Busy && _turnStart < 0) _turnStart = (long)Time.GetTicksMsec();
            if (Busy) _turnStart = -1;
        }
    }

    // ———————— 官方指令面板（图档与位置取自官方截图模板匹配；坐标相对 960 宽画面的右边缘） ————————
    private Control? _panel; private Label? _msg; private bool _picking; private long _turnStart = 0;
    private TextureRect? _d1, _d2;
    // 图像：普通、悬停、不能用（灰）。指令2 只能攻击/防御/逃跑（官方实测：技能/物品/宠物/武器/位置变灰）
    private static readonly (int Img, int Hover, int Gray, int X, int Y, CommandKind? Kind, bool Slot2)[] PanelButtons =
    {
        (22652, 22651, 22653, 700, 29, CommandKind.Attack, true), (22654, 22655, 22653, 700, 55, CommandKind.Guard, true),
        (22657, 22658, 22659, 796, 16, CommandKind.Skill, false), (22660, 22661, 22662, 796, 37, CommandKind.Item, false), (22663, 22664, 22665, 796, 58, CommandKind.Summon, false),
        (22666, 22667, 22668, 875, 16, CommandKind.Weapon, false), (22669, 22670, 22671, 875, 37, CommandKind.Position, false), (22672, 22673, 22674, 875, 58, CommandKind.Escape, true),
    };
    private readonly List<(TextureButton Btn, bool Slot2)> _cmdButtons = new();

    private bool BuildOfficialPanel()
    {
        var bg = Cache.ImageNo(22676).Tex;
        if (bg == null) return false;
        _panel = new Control { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -292, OffsetRight = 0, OffsetTop = 0, OffsetBottom = 140, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_panel);
        Vector2 P(int x, int y) => new(x - 668, y);
        _panelBg = new TextureRect { Texture = bg, Position = P(668, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddChild(_panelBg);
        foreach (var (img, hover, gray, x, y, kind, slot2) in PanelButtons)
        {
            var t = Cache.ImageNo(img).Tex; var th = Cache.ImageNo(hover).Tex ?? t;
            if (t == null) continue;
            var b = new TextureButton { TextureNormal = t, TextureHover = th, TexturePressed = th, TextureDisabled = Cache.ImageNo(gray).Tex ?? t, Position = P(x, y) };
            var k = kind;
            b.Pressed += () => OnPanelButton(k);
            _panel.AddChild(b);
            _cmdButtons.Add((b, slot2));
        }
        _sub = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddChild(_sub);
        // 提示条（"请选择目标。"）
        if (Cache.ImageNo(22705).Tex is ImageTexture bar)
        {
            _msgBar = new TextureRect { Texture = bar, Position = P(689, 112), MouseFilter = Control.MouseFilterEnum.Ignore };
            _panel.AddChild(_msgBar);
            _msg = NameTag.Make("", Colors.White, 0); _msg.Position = P(689 + 18, 112 + 12); _msg.HorizontalAlignment = HorizontalAlignment.Left;
            _panel.AddChild(_msg);
        }
        else { _msg = new Label(); _panel.AddChild(_msg); }
        // 回合倒计时：画面中间的大数字（22160–22169 蓝色，最后 10 秒用 22170–22179 红色），两位数字中心在 (480,350)
        _d1 = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore }; _d2 = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_d1); _root.AddChild(_d2);
        _status.Visible = false; _log.Visible = false;
        BuildExtraBar(P);
        return true;
    }

    // ———— 指令面板下面的一条（这个私服客户端加的）：「本局錄像」BMP 175/176、「自動」173/174、「選項」252/253（exe 资源，官方截图量的位置） ————
    /// <summary>录像回放用：不显示指令面板和倒计时。</summary>
    public bool ReadOnly;
    /// <summary>这一场要存录像（点「本局錄像」切换；官方：点了变灰，打完存 video\video_年月日_序号.dat）。</summary>
    public bool Recording { get; private set; }
    public Action<bool>? RecordToggled;
    /// <summary>自动战斗开关、「選項」按钮。</summary>
    public bool AutoOn { get; private set; }
    public Action<bool>? AutoToggled;
    public Action? OptionsPressed;
    private TextureButton? _recBtn, _autoBtn;
    private void BuildExtraBar(Func<int, int, Vector2> P)
    {
        var bar = new Panel { Position = P(767, 76), Size = new Vector2(189, 24), MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.1f, 0.9f), CornerRadiusTopLeft = 10, CornerRadiusBottomLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomRight = 10, BorderColor = new Color(0.55f, 0.55f, 0.55f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        _panel!.AddChild(bar);
        _recBtn = ExeBtn(175, P(780, 79), () => SetRecording(!Recording));
        _autoBtn = ExeBtn(174, P(861, 79), () => SetAuto(!AutoOn));
        ExeBtn(252, P(906, 79), () => OptionsPressed?.Invoke(), 253);
    }
    private TextureButton? ExeBtn(int id, Vector2 pos, Action a, int hover = -1)
    {
        if (ExeArt.Bmp(id) is not { } t) return null;
        var b = new TextureButton { TextureNormal = t, TextureHover = hover > 0 ? ExeArt.Bmp(hover) ?? t : t, Position = pos };
        b.Pressed += () => { GameAudio.Se(51); a(); };
        _panel!.AddChild(b); return b;
    }
    public void SetRecording(bool on)
    {
        Recording = on;
        if (_recBtn != null && ExeArt.Bmp(on ? 176 : 175) is { } t) _recBtn.TextureNormal = _recBtn.TextureHover = t;
        RecordToggled?.Invoke(on);
    }
    /// <summary>自动战斗替玩家下指令：走和手动一样的信号，面板照常往下一个指令位走。</summary>
    public void AutoAct(Command c)
    {
        if (c.Kind is CommandKind.Skill or CommandKind.PetSkill) EmitSignal(SignalName.CommandSkill, (int)c.Kind, c.TargetId, c.Arg, c.Level);
        else if (c.Kind == CommandKind.Item) EmitSignal(SignalName.CommandArg, (int)c.Kind, c.TargetId, c.Arg);
        else if (c.TargetId >= 0) EmitSignal(SignalName.CommandTarget, (int)c.Kind, c.TargetId);
        else EmitSignal(SignalName.CommandChosen, (int)c.Kind);
        CloseSub(); AfterCommand();
    }
    public void SetAuto(bool on)
    {
        AutoOn = on;
        if (_autoBtn != null && ExeArt.Bmp(on ? 173 : 174) is { } t) _autoBtn.TextureNormal = _autoBtn.TextureHover = t;
        AutoToggled?.Invoke(on);
    }

    private void OnPanelButton(CommandKind? kind)
    {
        if (Busy || kind == null) { GameLog.Write("战斗", $"按钮 {kind} 被忽略：动画还在播（队列 {_queue.Count}）"); return; }
        GameLog.Write("战斗", $"按钮 {kind}");
        _itemMenu?.QueueFree(); _itemMenu = null;
        if (kind == CommandKind.Attack) { CloseSub(); ShowBar(true); _pickKind = CommandKind.Attack; _pickSide = 1; _picking = true; _msg!.Text = "请选择目标。"; return; }
        if (kind == CommandKind.Item) { ShowItemMenu(); return; }
        if (kind == CommandKind.Skill) { ShowSkillWindow(); return; }
        if (kind == CommandKind.Summon) { ShowPetSwap(); return; }
        if (kind == CommandKind.Weapon) { ShowWeapons(); return; }
        EmitSignal(SignalName.CommandChosen, (int)kind); AfterCommand();
    }

    // ———— 指令1 / 指令2：没带宠物时人物每回合两次行动，官方面板背景 22676（指令1）→ 22677（指令2），两次都下完面板收起 ————
    private TextureRect? _panelBg; private int _slot;
    /// <summary>每回合要下几次指令（联网原版服务端 = 2；单机 = 1）。</summary>
    public int CommandsPerTurn { get; set; } = 2;
    /// <summary>现在是本回合第几个指令（0 = 指令1，1 = 指令2）。</summary>
    public int CommandSlot => _slot;
    /// <summary>服务端说正在等第几个指令（NetworkSession.CommandRequested）；petTurn = 现在要宠物的指令，hasPet = 本场带着宠物。</summary>
    public void SetCommandSlot(int slot, bool petTurn = false, bool hasPet = false)
    {
        if (slot < 0) return;
        _slot = slot; _hasPet = hasPet || petTurn;
        ApplySlot();
    }
    private bool _hasPet;
    /// <summary>第二个指令是宠物的（带宠物出战时）。</summary>
    private bool PetPhase => _hasPet && _slot >= 1;
    private void AfterCommand() { _slot++; _picking = false; ApplySlot(); }
    private void ApplySlot()
    {
        CloseSub();
        if (_panelBg != null)
        {
            // 背景：没带宠物 22676 指令1 / 22677 指令2；带宠物 22650 玩家指令 → 22680 宠物指令（位置不同：685,4）
            int bg = PetPhase ? 22680 : _hasPet ? 22650 : _slot >= 1 ? 22677 : 22676;
            if (Cache.ImageNo(bg).Tex is { } t) _panelBg.Texture = t;
            _panelBg.Position = new Vector2((PetPhase ? 685 : 668) - 668, 4);
        }
        foreach (var (b, slot2) in _cmdButtons) { b.Visible = !PetPhase; b.Disabled = _slot >= 1 && !slot2; }
        if (_panel != null) _panel.Visible = !ReadOnly && !Busy && Visible && _slot < CommandsPerTurn;
        if (_msg != null && !_picking) _msg.Text = "";
        ShowBar(!PetPhase || _picking);
        if (PetPhase && !_picking && _panel != null && _panel.Visible) ShowPetTechs();
    }

    // ———————————————————— 技能窗口（官方截图量的位置，960 宽画面坐标） ————————————————————
    // 技能列表 22700 在 (688,111)：魔法 MP/最大MP 右对齐到 x=940、字原点 y=123；"剩余技能栏" 值在 (835,144)；
    //   每个技能一条 22703 在 (695, 164+24i)，名字原点 (719, 行+3)，等级右对齐 906，占用栏数右对齐 942；取消 22681/22682 在 (789,412)。
    // 招式列表 22701 在 (688,111)（宠物指令时在 (688,86)，头上是 22680）：MP 同上（窗口 +12），第 i 招名字原点 (723, 窗口+30+24i)，MP 消耗右对齐 945；
    //   技能表 22683/22684 在 (789,447) 回到技能列表。官方客户端这两个招式窗口的边框图在我们的图档里缺（Joy_EX），所以用 22701 补。
    private Control? _sub; private TextureRect? _msgBar;
    private void ShowBar(bool v) { if (_msgBar != null) _msgBar.Visible = v; if (_msg != null) _msg.Visible = v; }
    private void CloseSub() { if (_sub == null) return; foreach (var c in _sub.GetChildren()) c.QueueFree(); }
    private static Vector2 Q(float x, float y) => new(x - 668, y);
    private TextureRect? Img(int id, float x, float y)
    {
        if (Cache.ImageNo(id).Tex is not { } t) return null;
        var r = new TextureRect { Texture = t, Position = Q(x, y), MouseFilter = Control.MouseFilterEnum.Ignore };
        _sub!.AddChild(r); return r;
    }
    private void Text(string text, float x, float y, Color? c = null, bool right = false)
    {
        var t = new CgText { Wrap = 999, FontColor = c ?? Colors.White };
        t.Text = text;
        float w = text.Sum(CgText.Cells) * CgText.CellW;
        t.Position = Q(right ? x - w : x, y);
        _sub!.AddChild(t);
    }
    private Control Hit(float x, float y, float w, float h, Action onClick)
    {
        var c = new Control { Position = Q(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Stop };
        var box = new GreenBox { Size = c.Size, Visible = false }; c.AddChild(box);
        c.MouseEntered += () => box.Visible = true; c.MouseExited += () => box.Visible = false;
        c.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { onClick(); _panel?.AcceptEvent(); } };
        _sub!.AddChild(c); return c;
    }
    private void TexButton(int img, int hover, float x, float y, Action onClick)
    {
        if (Cache.ImageNo(img).Tex is not { } t) return;
        var b = new TextureButton { TextureNormal = t, TextureHover = Cache.ImageNo(hover).Tex ?? t, TexturePressed = Cache.ImageNo(hover).Tex ?? t, Position = Q(x, y) };
        b.Pressed += onClick; _sub!.AddChild(b);
    }
    /// <summary>官方格式（实测 "22/1340" "153/ 153"）：MP 3 位、最大 MP 4 位，左对齐在 x=878。</summary>
    private static string MpText((int Mp, int MaxMp) m) => $"{m.Mp,3}/{m.MaxMp,4}".TrimStart();
    private static float MpX((int Mp, int MaxMp) m) => 878 + ($"{m.Mp,3}".Length - $"{m.Mp,3}".TrimStart().Length) * CgText.CellW;

    private void ShowSkillWindow()
    {
        CloseSub(); _picking = false; ShowBar(false);
        var skills = Skills?.Invoke() ?? Array.Empty<SkillInfo>();
        Img(22700, 688, 111);
        var pm = PlayerMp?.Invoke() ?? (0, 0);
        Text(MpText(pm), MpX(pm), 123 - 5);
        int total = SkillSlotsTotal?.Invoke() ?? 10;
        Text($"{total - skills.Sum(k => k.SlotsUsed)}/{total}", 835, 144);
        for (int i = 0; i < skills.Count && i < 10; i++)
        {
            var sk = skills[i]; float y = 164 + 24 * i;
            Img(22703, 695, y);
            Text(sk.Name, 719, y + 3);
            Text(sk.Level.ToString(), 906.5f, y + 3, right: true);
            Text(sk.SlotsUsed.ToString(), 942.5f, y + 3, right: true);
            Hit(695, y, 256, 20, () => ShowTechWindow(sk));
        }
        TexButton(22681, 22682, 789, 412, () => { CloseSub(); ShowBar(true); });
    }

    /// <summary>战斗里出战/收回宠物（指令1 才能用）。</summary>
    public Func<IReadOnlyList<PetInfo>>? AllPets;
    /// <summary>现在场上的宠物（没有 = null）。</summary>
    public Func<PetInfo?>? PetOnField;

    /// <summary>
    /// "更换宠物"窗口（官方实测）：22720 在 (688,111)，每页 3 只、行距 86；名字居中 x787、字顶 146；健康方块 22721 在 (927,148)；
    /// 等级左 750、HP 右对齐 893、字顶 174；MP 右对齐 827、字顶 197。点一只 = 叫出（M|栏位），收回 22690/91 (728,400) = M|FF，取消 22681/82。
    /// 有宠物在场时才有"收回"，取消在 (852,401)；否则只有取消，在 (790,401)。
    /// </summary>
    private void ShowPetSwap()
    {
        CloseSub(); _picking = false; ShowBar(false);
        Img(22720, 688, 111);
        var pets = AllPets?.Invoke() ?? Array.Empty<PetInfo>();
        var outPet = PetOnField?.Invoke();
        for (int i = 0; i < pets.Count && i < 3; i++)
        {
            var p = pets[i]; float dy = 86 * i;
            Img(22721, 927, 148 + dy);
            float nw = p.DisplayName.Sum(CgText.Cells) * CgText.CellW;
            Text(p.DisplayName, 787 - nw / 2, 146 + dy - 5, p.State == PetInfo.Battle ? new Color(1, 1, 0) : null);   // 出战中的那只名字黄色（官方实测）
            // 右边的小血条/魔条（实测）：HP 红 (902–927, 177–179)，MP 黄 (902–911, 181–183)；长度按比例，满 = 26 像素
            if (p.MaxHp > 0) _sub!.AddChild(new ColorRect { Color = new Color(1, 0, 0), Position = Q(902, 177 + dy), Size = new Vector2(26f * Math.Clamp(p.Hp, 0, p.MaxHp) / p.MaxHp, 3), MouseFilter = Control.MouseFilterEnum.Ignore });
            if (p.MaxMp > 0) _sub!.AddChild(new ColorRect { Color = new Color(1, 1, 0), Position = Q(902, 181 + dy), Size = new Vector2(26f * Math.Clamp(p.Mp, 0, p.MaxMp) / p.MaxMp, 3), MouseFilter = Control.MouseFilterEnum.Ignore });
            Text(p.Level.ToString(), 750, 174 + dy - 5);
            var hp = $"{p.Hp,3}/{p.MaxHp,4}".TrimStart(); Text(hp, 893 - hp.Sum(CgText.Cells) * CgText.CellW, 174 + dy - 5);
            var mp = $"{p.Mp,3}/{p.MaxMp,4}".TrimStart(); Text(mp, 827 - mp.Sum(CgText.Cells) * CgText.CellW, 197 + dy - 5);
            int slot = p.Slot;
            if (outPet == null || outPet.Slot != slot) Hit(697, 140 + dy, 254, 80, () => { EmitSignal(SignalName.CommandArg, (int)CommandKind.Summon, -1, slot); AfterCommand(); });
        }
        if (outPet != null)
        {
            TexButton(22690, 22691, 728, 400, () => { EmitSignal(SignalName.CommandArg, (int)CommandKind.Summon, -1, -1); AfterCommand(); });
            TexButton(22681, 22682, 852, 401, () => { CloseSub(); ShowBar(true); });
        }
        else TexButton(22681, 22682, 790, 401, () => { CloseSub(); ShowBar(true); });
    }
    /// <summary>测试用。</summary>
    public void TestPressPet() => OnPanelButton(CommandKind.Summon);
    public void TestPressWeapon() => OnPanelButton(CommandKind.Weapon);
    public void TestPressPosition() => OnPanelButton(CommandKind.Position);
    /// <summary>测试用：武器窗口里点第 i 格（背包第 i 格）。</summary>
    public void TestClickWeaponCell(int i)
    {
        var p = Q(696 + 52 * (i % 5), 145 + 53 * (i / 5));
        var hit = _sub?.GetChildren().OfType<Control>().FirstOrDefault(c => c.MouseFilter == Control.MouseFilterEnum.Stop && c is not TextureButton && c.Position == p);
        hit?.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left });
    }

    /// <summary>背包道具（含说明，用来判断是不是武器）。</summary>
    public Func<IReadOnlyList<ItemStack>>? BagStacks;
    private static readonly string[] WeaponKinds = { "剑", "斧", "枪", "杖", "弓", "刀", "镖", "拳", "爪", "盾" };   // 盾也能换（官方实测：水龙之盾没打叉）
    /// <summary>说明里"种类 xx"是武器类（服务端道具说明的 $0种类 段）。</summary>
    public static bool IsWeapon(ItemStack it)
    {
        int k = it.Desc.IndexOf("种类");
        if (k < 0) return false;
        var kind = it.Desc[(k + 2)..].Replace("\\S", "").TrimStart();
        int end = kind.IndexOfAny(new[] { '\\', '$', ' ' });
        if (end > 0) kind = kind[..end];
        return WeaponKinds.Any(kind.Contains);
    }

    /// <summary>
    /// "更换武器"窗口（官方实测）：22736 在 (688,111)，背包 20 格 5×4，格子 (696+52列, 145+53行) 48×48；
    /// 不是武器的盖红叉 22738。点武器 = Q|格子；解除武装 22740/41 (738,369) = Q|FF；取消 22681/82 (849,370)。
    /// </summary>
    private void ShowWeapons()
    {
        CloseSub(); _picking = false; ShowBar(false);
        Img(22736, 688, 111);
        var bag = BagStacks?.Invoke() ?? Array.Empty<ItemStack>();
        for (int i = 0; i < 20; i++)
        {
            float x = 696 + 52 * (i % 5), y = 145 + 53 * (i / 5);
            var it = bag.FirstOrDefault(b => b.Slot == 8 + i);
            if (it == null) continue;
            if (it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } t)
            {
                var sz = t.GetSize();
                _sub!.AddChild(new TextureRect { Texture = t, Position = Q(x + 24 - sz.X / 2, y + 24 - sz.Y / 2), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            if (!IsWeapon(it)) { Img(22738, x, y); continue; }
            int slot = it.Slot;
            Hit(x, y, 48, 48, () => { EmitSignal(SignalName.CommandArg, (int)CommandKind.Weapon, -1, slot); AfterCommand(); });
        }
        TexButton(22740, 22741, 738, 369, () => { EmitSignal(SignalName.CommandArg, (int)CommandKind.Weapon, -1, -1); AfterCommand(); });
        TexButton(22681, 22682, 849, 370, () => { CloseSub(); ShowBar(true); });
    }
    /// <summary>测试用：按子窗口里位置在 (x,y) 的图片按钮（收回、取消…）。</summary>
    public bool TestPressAt(float x, float y)
    {
        var b = _sub?.GetChildren().OfType<TextureButton>().FirstOrDefault(t => t.Position == Q(x, y));
        b?.EmitSignal(BaseButton.SignalName.Pressed); return b != null;
    }

    private void ShowTechWindow(SkillInfo sk)
    {
        CloseSub();
        var mp = PlayerMp?.Invoke() ?? (0, 0);
        TechList(sk.Techs, 111, mp, t => { _pickKind = CommandKind.Skill; _pickArg = sk.Slot; _pickLevel = t.Index; });
        TexButton(22683, 22684, 789, 447, ShowSkillWindow);
    }

    private void ShowPetTechs()
    {
        CloseSub();
        var pet = BattlePet?.Invoke();
        if (pet == null) return;
        Text(MpText((pet.Mp, pet.MaxMp)), MpX((pet.Mp, pet.MaxMp)) - 77, 61 - 5);   // 22680 面板里的"魔法"框（实测字顶 61、左 803）
        TechList(pet.Techs, 86, (pet.Mp, pet.MaxMp), t => { _pickKind = CommandKind.PetSkill; _pickArg = t.Index; _pickLevel = 0; });
        TexButton(22681, 22682, 789, 424, () => { });
    }

    /// <summary>招式列表：点一招 → 选目标（MP 不够的变灰、点不了）。</summary>
    /// <summary>
    /// 招式列表（官方实测）：人物 = 22702 在 (688,111)，只画已有的招式，每行 22704 在 (703, 139+22i)，名字字顶 行+4、x 725，MP 右对齐 944；
    /// 宠物 = 22745 在 (688,86)，固定 9 行 22746 在 (699, 115+22i)，名字字顶 行+5、x 722，MP 右对齐 928。表头 MP 字顶 人物 121 / 宠物 99、左 880。
    /// 点一招 → 选目标（MP 不够的变灰、点不了）。
    /// </summary>
    private void TechList(IReadOnlyList<TechInfo> techs, float top, (int Mp, int MaxMp) mp, Action<TechInfo> choose)
    {
        bool pet = top < 100;
        Img(pet ? 22745 : 22702, 688, top);
        Text(MpText(mp), MpX(mp) + 2, (pet ? 99 : 121) - 5);
        int rows = pet ? 9 : Math.Min(techs.Count, 9);
        for (int i = 0; i < rows; i++)
        {
            float ry = pet ? 115 + 22 * i : 139 + 22 * i, rx = pet ? 699 : 703;
            Img(pet ? 22746 : 22704, rx, ry);
            if (i >= techs.Count) continue;
            var t = techs[i];
            bool ok = t.Mp <= mp.Mp;
            var col = ok ? Colors.White : new Color(0.55f, 0.55f, 0.55f);
            Text(t.Name, pet ? 722 : 725, ry + (pet ? 5 : 4) - 2, col);
            Text(t.Mp.ToString(), pet ? 928 : 944, ry + (pet ? 7 : 6) - 5, col, right: true);
            if (ok) Hit(rx, ry, pet ? 244 : 240, 20, () =>
            {
                choose(t);
                CloseSub();
                // 目标：任意单位（补血等对我方，攻击对敌方；官方也是招式选完再点人）
                _pickSide = -1; _picking = true; ShowBar(true); _msg!.Text = "请选择目标。";
            });
        }
    }
    private int _pickLevel;

    private CommandKind _pickKind = CommandKind.Attack; private int _pickSide = 1, _pickArg = -1;
    private Control? _itemMenu;

    /// <summary>
    /// "物品"窗口（官方 4.06 实测）：22737 在 (688,111)，背包 20 格 5×4，格子 (696+52列, 145+53行) 48×48（同更换武器）；
    /// 战斗里不能用的（rU 用途段第 1 位是 0：装备、材料、金币、魔石、卡片…）盖红叉 22738。鼠标停在道具上，下面出说明框 22541（688,277）。
    /// 双击能用的 → 窗口关掉、"请选择目标。" → 点人 = Rg I|格|目标（封印卡对敌人）。取消 22681/82 (788,369)。
    /// </summary>
    private void ShowItemMenu()
    {
        CloseSub(); _picking = false; ShowBar(false);
        Img(22737, 688, 111);
        var bag = BagStacks?.Invoke() ?? Array.Empty<ItemStack>();
        for (int i = 0; i < 20; i++)
        {
            float x = 696 + 52 * (i % 5), y = 145 + 53 * (i / 5);
            var it = bag.FirstOrDefault(b => b.Slot == 8 + i);
            if (it == null) continue;
            if (it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } t)
            {
                var sz = t.GetSize();
                _sub!.AddChild(new TextureRect { Texture = t, Position = Q(x + 24 - sz.X / 2, y + 24 - sz.Y / 2), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            if (it.Count > 1) Text(it.Count.ToString(), x + 46, y + 31, new Color(0.4f, 1, 1), right: true);
            bool ok = (it.Use & 1) != 0;
            if (!ok) Img(22738, x, y);
            var item = it;
            var h = Hit(x, y, 48, 48, () => { if (ok) ItemClicked(item); });
            h.MouseEntered += () => ItemTip(item); h.MouseExited += () => { _itemTip?.QueueFree(); _itemTip = null; };
        }
        TexButton(22681, 22682, 788, 369, () => { CloseSub(); ShowBar(true); });
    }

    private Control? _itemTip; private long _itemClick; private int _itemClickSlot = -1;
    private void ItemClicked(ItemStack it)
    {
        long now = (long)Time.GetTicksMsec();
        bool dbl = _itemClickSlot == it.Slot && now - _itemClick < 500;
        _itemClick = now; _itemClickSlot = it.Slot;
        if (dbl) { _itemClickSlot = -1; _itemTip = null; CloseSub(); PickItem(it.Slot, it.Name.Contains("封印卡")); }
    }

    /// <summary>道具说明框（同物品窗口：22541，标题橙色居中、说明按颜色码）。</summary>
    private void ItemTip(ItemStack it)
    {
        _itemTip?.QueueFree();
        if (Cache.ImageNo(22541).Tex is not { } bg) return;
        var tip = new Control { Position = Q(688, 277), MouseFilter = Control.MouseFilterEnum.Ignore };
        tip.AddChild(new TextureRect { Texture = bg, MouseFilter = Control.MouseFilterEnum.Ignore });
        float tw = it.Name.Sum(CgText.Cells) * CgText.CellW;
        tip.AddChild(new CgText { Text = it.Name, FontColor = Colors.White, Position = new Vector2(133 - tw / 2, 10 - 2) });
        var lines = ItemPanel.DescLines(it.Desc);
        for (int l = 0; l < lines.Count && 37 + 18 * l + 14 < 184; l++)
        {
            float x = 10;
            foreach (var (text, color) in lines[l])
            {
                tip.AddChild(new CgText { Text = text, Wrap = 999, FontColor = color, Position = new Vector2(x, 37 + 18 * l - 2) });
                x += text.Sum(CgText.Cells) * CgText.CellW;
            }
        }
        _sub!.AddChild(tip); _itemTip = tip;
    }

    /// <summary>选了一个道具：封印卡（抓宠）对敌人用，其他对自己人用。</summary>
    private void PickItem(int slot, bool seal)
    {
        _itemMenu?.QueueFree(); _itemMenu = null; _itemTip = null; CloseSub();
        GameLog.Write("战斗", $"道具 第{slot}格{(seal ? "（封印卡）" : "")}");
        _pickKind = CommandKind.Item; _pickSide = seal ? 1 : 0; _pickArg = slot; _picking = true; ShowBar(true); _msg!.Text = "请选择目标。";
    }
    /// <summary>测试用：物品窗口里某格是不是盖了红叉（不能用）。</summary>
    public bool ItemWindowOpen => _sub?.GetChildren().OfType<TextureRect>().Any(t => t.Texture == Cache.ImageNo(22737).Tex) == true;
    public void TestPressItem() => OnPanelButton(CommandKind.Item);
    public void TestDoubleClickItem(ItemStack it) { ItemClicked(it); ItemClicked(it); }
    public int ItemWindowCrosses => _sub?.GetChildren().OfType<TextureRect>().Count(t => t.Texture == Cache.ImageNo(22738).Tex) ?? 0;
    /// <summary>测试用：点「物品」、选第 slot 格道具（和点菜单一样的路径）。</summary>
    public void TestPickItem(int slot)
    {
        OnPanelButton(CommandKind.Item);
        var item = (BagItems?.Invoke() ?? Array.Empty<(int Slot, int ItemId, int Count)>()).FirstOrDefault(x => x.Slot == slot);
        string name = ItemLook?.Invoke(item.ItemId).Name ?? "";
        PickItem(slot, name.Contains("封印卡"));
    }

    /// <summary>测试用：模拟"点攻击按钮 → 在屏幕某点点一下"（走和真实点击一样的代码路径）。</summary>
    /// <summary>测试用：面板是否显示、背景是不是指令2（22677）。</summary>
    public (bool Visible, bool Second) PanelState => (_panel?.Visible ?? false, _panelBg?.Texture != null && _panelBg.Texture == Cache.ImageNo(22677).Tex);
    public void TestPressAttack() => OnPanelButton(CommandKind.Attack);
    public void TestPressGuard() => OnPanelButton(CommandKind.Guard);
    /// <summary>测试用：指令按钮在画面（画布坐标）里的矩形；第 i 个 = PanelButtons 顺序（0 攻击）。</summary>
    public Rect2? CommandButtonRect(int i) => i < _cmdButtons.Count ? _cmdButtons[i].Btn.GetGlobalRect() : null;
    /// <summary>测试用：某个单位在画面上"该点的位置"（精灵脚下往上 40）。</summary>
    public Vector2? UnitTapPos(int id) => _units.TryGetValue(id, out var u) ? u.Sprite.GetGlobalTransformWithCanvas().Origin + new Vector2(0, -40) : null;
    /// <summary>测试用：点"技能"、点第 i 个技能、点第 i 招（走和真实点击一样的路径）、选目标。</summary>
    public void TestPressSkill() => OnPanelButton(CommandKind.Skill);
    public void TestClickSub(int index)
    {
        var hits = _sub!.GetChildren().OfType<Control>().Where(c => c.MouseFilter == Control.MouseFilterEnum.Stop && c is not TextureButton).ToList();
        if (index < hits.Count) hits[index].EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left });
    }
    public bool Picking => _picking;
    public void TestPickTarget(int unitId)
    {
        if (!_picking || !_units.TryGetValue(unitId, out var u)) return;
        _picking = false; ShowBar(false);
        if (_pickKind == CommandKind.Attack) EmitSignal(SignalName.CommandTarget, (int)CommandKind.Attack, u.Id);
        else if (_pickKind is CommandKind.Skill or CommandKind.PetSkill) EmitSignal(SignalName.CommandSkill, (int)_pickKind, u.Id, _pickArg, _pickLevel);
        else EmitSignal(SignalName.CommandArg, (int)_pickKind, u.Id, _pickArg);
        AfterCommand();
    }
    public string DebugPanel => $"hasPet={_hasPet} slot={_slot} petPhase={PetPhase} pet={BattlePet?.Invoke()?.Name}";
    public bool PetPhaseShown => PetPhase && (_panel?.Visible ?? false);
    public IEnumerable<(int Id, int Side, Vector2 Pos, bool Down)> UnitsOnScreen => _units.Values.Select(u => (u.Id, u.Side, u.Sprite.Position, u.Down));

    /// <summary>测试用：直接用道具（跳过菜单和点选）。</summary>
    public void UseItemOn(int slot, int target) { EmitSignal(SignalName.CommandArg, (int)CommandKind.Item, target, slot); AfterCommand(); }

    public override void _Process(double delta)
    {
        if (_panel == null || !Visible) return;
        bool show = !Busy && _turnStart > 0 && !ReadOnly;
        _d1!.Visible = _d2!.Visible = show;
        if (!show) return;
        int left = Math.Max(0, 99 - (int)(((long)Time.GetTicksMsec() - _turnStart) / 1000));   // 官方本服配置 99 秒（原版 30 秒）
        int baseNo = left <= 10 ? 22170 : 22160;
        _d1.Texture = Cache.ImageNo(baseNo + left / 10).Tex; _d2.Texture = Cache.ImageNo(baseNo + left % 10).Tex;
        var c = Size / 2;
        _d1.Position = c + new Vector2(-32, -42); _d2.Position = c + new Vector2(0, -42);
    }

    /// <summary>
    /// 选目标。点击事件要从 _root 的 GuiInput 拿：_root 是铺满全屏、MouseFilter=Stop 的 Control，
    /// 点击会被 GUI 吃掉，到不了 _UnhandledInput（之前就是因此在战斗里点敌人没反应）。
    /// </summary>
    private void OnRootInput(InputEvent e)
    {
        if (!_picking || !Visible || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        // 选目标：点到离鼠标最近的敌人（以精灵脚下往上 40 像素为中心）
        Unit? best = null; float bd = 70;
        foreach (var u in _units.Values)
        {
            if ((_pickSide >= 0 && u.Side != _pickSide) || u.Down) continue;
            float d = (u.Sprite.Position + new Vector2(0, -40)).DistanceTo(mb.Position);
            if (d < bd) { bd = d; best = u; }
        }
        if (best == null) { GameLog.Write("战斗", $"选目标：{mb.Position} 附近 70 像素内没有目标"); return; }
        GameLog.Write("战斗", $"选目标：{best.Id}（距离 {bd:0}）");
        _picking = false; _msg!.Text = "";
        if (_pickKind == CommandKind.Attack) EmitSignal(SignalName.CommandTarget, (int)CommandKind.Attack, best.Id);
        else if (_pickKind is CommandKind.Skill or CommandKind.PetSkill) EmitSignal(SignalName.CommandSkill, (int)_pickKind, best.Id, _pickArg, _pickLevel);
        else EmitSignal(SignalName.CommandArg, (int)_pickKind, best.Id, _pickArg);
        AfterCommand();
        _root.AcceptEvent();
    }

    public int PlayerId => _playerId;
    private string Name(int id) => id == _playerId ? "你" : $"敌人{id}";

    private void Say(string line)
    {
        _lines.Add(line);
        while (_lines.Count > 6) _lines.RemoveAt(0);
        _log.Text = string.Join("\n", _lines);
    }

    /// <summary>会话结算了一个回合：把事件排进播放队列。</summary>
    public void AppendLog(IEnumerable<BattleEvent> events, BattleView v)
    {
        // 同一个人连续放的同一招（连击的每一下、范围魔法的每个目标）合成一组一起播
        var list = events.ToList();
        for (int i = 0; i < list.Count;)
        {
            int j = i + 1;
            if (list[i].Kind is CommandKind.Skill or CommandKind.PetSkill)
                while (j < list.Count && list[j].Kind == list[i].Kind && list[j].ActorId == list[i].ActorId && list[j].TechId == list[i].TechId && list[j].Magic == list[i].Magic) j++;
            var group = list.GetRange(i, j - i);
            Enqueue(() => group.Count == 1 && group[0].Kind is not (CommandKind.Skill or CommandKind.PetSkill) ? PlayEvent(group[0]) : PlaySkill(group));
            i = j;
        }
        Enqueue(() => { Sync(v); return Task.CompletedTask; });
    }

    /// <summary>战斗画面真正关掉（播完最后的动画）时：出战斗过场从这里开始。</summary>
    public event Action? Closed;

    /// <summary>断线：立刻关掉战斗画面（不等动画）。</summary>
    public void Abort()
    {
        _gen++; _queue.Clear(); _running = false; _picking = false;
        if (Visible) { Visible = false; Closed?.Invoke(); }
    }

    public void Close(BattleOutcome o)
    {
        int gen = _gen;
        Enqueue(async () =>
        {
            Say(o switch { BattleOutcome.Win => "战斗胜利！", BattleOutcome.Lose => "战斗失败……", _ => "脱离了战斗" });
            await Wait(1.0);
            if (gen == _gen) { Visible = false; Closed?.Invoke(); }
        });
    }

    private void Enqueue(Func<Task> step)
    {
        _turnStart = -1;
        _queue.Enqueue(step);
        SetButtons();
        if (!_running) _ = Pump();
    }

    private async Task Pump()
    {
        _running = true; SetButtons();
        int gen = _gen;
        while (_queue.Count > 0 && gen == _gen)
        {
            var step = _queue.Dequeue();
            try { await step(); }
            catch (Exception ex) { GD.PushError($"战斗播放出错：{ex}"); }
        }
        if (gen != _gen) return;
        _running = false; SetButtons();
    }

    /// <summary>回合结束后用服务端的状态校正血量（以服务端为准）。</summary>
    private void Sync(BattleView v)
    {
        foreach (var u in v.Units)
            if (_units.TryGetValue(u.Id, out var x))
            {
                x.Hp = u.Hp; x.Bar.Value = u.Hp;
                if (!u.Alive && !x.Down) { x.Down = true; x.Sprite.Modulate = new Color(1, 1, 1, 0.5f); }
                if (u.Escaped) x.Sprite.Visible = false;
            }
        UpdateStatus(v.Turn);
    }

    private async Task PlayEvent(BattleEvent e)
    {
        PlayedEvents++;
        if (!_units.TryGetValue(e.ActorId, out var a)) return;
        switch (e.Kind)
        {
            case CommandKind.Attack when _units.TryGetValue(e.TargetId, out var t):
                await PlayAttack(a, t, e);
                break;
            case CommandKind.Guard:
                Say($"{Name(e.ActorId)} 防御");
                Float(a, "防御", Colors.LightSkyBlue);
                if (a.Sprite.HasAct(Cfg.ActGuard)) { await PlayOnce(a.Sprite, Cfg.ActGuard, hold: true); await Wait(0.25); a.Sprite.Act = Cfg.ActStand; }
                else await Wait(0.45);
                break;
            case CommandKind.Escape:
                Say(e.Escaped ? $"{Name(e.ActorId)} 逃跑成功" : $"{Name(e.ActorId)} 逃跑失败");
                if (e.Escaped)
                {
                    var away = a.Home + (a.Side == 0 ? new Vector2(260, 160) : new Vector2(-260, -160));
                    a.Sprite.Act = RunAct(a.Sprite);
                    a.Sprite.Dir = a.Side == 0 ? Iso.AnimeDirFor(0, 1, Cfg.DirOffset) : Iso.AnimeDirFor(0, -1, Cfg.DirOffset);
                    await TweenTo(a.Sprite, "position", away, 0.5);
                    a.Sprite.Visible = false;
                }
                else { Float(a, "失败", Colors.Gray); await Wait(0.45); }
                break;
            case CommandKind.Item when e.Sealed != null && _units.TryGetValue(e.TargetId, out var st):
                await PlaySeal(a, st, e.Sealed.Value);
                break;
            case CommandKind.Item when e.Heal && _units.TryGetValue(e.TargetId, out var ht):
                Say($"{Name(e.ActorId)} 使用道具，{Name(e.TargetId)} 回复 {e.Damage} 点");
                await Hit(a, ht, e, heal: true);
                break;
            default:
                await Wait(0.15);
                break;
        }
    }

    public int SealsShown { get; private set; }

    /// <summary>
    /// 封印（抓宠）：使用者原地播攻击动作甩出卡片，目标身上出效果；抓到了 → 目标缩小淡出（被收走），没抓到 → 飘"失败"。
    /// （官方有专门的卡片飞行和封印动画，编号还没对上，先用这个。）
    /// </summary>
    private async Task PlaySeal(Unit a, Unit t, bool ok)
    {
        SealsShown++;
        await PlayOnce(a.Sprite, a.Sprite.HasAct(Cfg.ActThrow) ? Cfg.ActThrow : Cfg.ActAttack);   // 扔卡片 = 投掷动作 7
        a.Sprite.Act = Cfg.ActStand;
        Effect(t, ok ? new Color(1f, 0.95f, 0.4f) : new Color(0.7f, 0.7f, 0.7f));
        if (ok)
        {
            Say($"{Name(a.Id)} 封印了 {t.Sprite.Name}！");
            Float(t, "封印成功", new Color(1f, 0.9f, 0.3f));
            t.Down = true;
            var tw = t.Sprite.CreateTween().SetSpeedScale(Speed).SetParallel();
            tw.TweenProperty(t.Sprite, "scale", t.Sprite.Scale * 0.2f, 0.6);
            tw.TweenProperty(t.Sprite, "modulate:a", 0.0, 0.6);
            await Wait(0.7);
            t.Sprite.Visible = false; t.Bar.Visible = false; t.Tag.Visible = false;
        }
        else
        {
            Say($"{Name(a.Id)} 封印失败");
            Float(t, "封印失败", Colors.LightGray);
            await Wait(0.6);
        }
    }

    /// <summary>技能/魔法的名字：自己和宠物会的技能里找，找不到就叫"技能"。</summary>
    private string TechName(int techId)
    {
        // 招式编号 = 技能编号×100 + 第几级（服务端 tech.txt：气功弹LV1=400 属技能 4，陨石LV1=1900 属技能 19）
        foreach (var sk in Skills?.Invoke() ?? Array.Empty<SkillInfo>())
            if (sk.SkillId == techId / 100)
                foreach (var t in sk.Techs) if (t.Index == techId % 100) return t.Name;
        foreach (var p in AllPets?.Invoke() ?? Array.Empty<PetInfo>())
            foreach (var t in p.Techs) if (t.TechId == techId) return t.Name;
        return techId == 0 ? "连击" : "技能";
    }

    public int SkillsPlayed { get; private set; }
    public int HealsShown { get; private set; }

    /// <summary>
    /// 播一组技能事件。物理技能（连击、乾坤一掷等）：冲过去，每一下都播攻击动作和伤害，打完再跑回来。
    /// 魔法（陨石、补血等）：站在原地播施法动作，所有目标同时出效果和数字；补血是绿字、血量往上涨。
    /// </summary>
    private async Task PlaySkill(List<BattleEvent> group)
    {
        PlayedEvents += group.Count; SkillsPlayed++;
        var e0 = group[0];
        if (!_units.TryGetValue(e0.ActorId, out var a)) return;
        string name = TechName(e0.TechId);
        if (!e0.Magic)
        {
            if (!_units.TryGetValue(e0.TargetId, out var t0)) return;
            var dir = a.Sprite.Dir;
            var to = a.Home + (t0.Home - a.Home) * 0.72f;
            a.Sprite.Act = RunAct(a.Sprite); a.Sprite.ZIndex = 1;
            Float(a, name, Colors.Yellow);
            await TweenTo(a.Sprite, "position", to, 0.28);
            int total = 0;
            foreach (var e in group)
            {
                if (!_units.TryGetValue(e.TargetId, out var t)) continue;
                if (a.Sprite.HasAct(Cfg.ActAttack)) AttackAnimsShown++;
                var (sh, sd) = PlayAttack(a.Sprite, Cfg.ActAttack);
                await sh;
                if (e.Missed) { Float(t, "Miss", Colors.LightGray); await sd; continue; }
                total += e.Damage;
                await Task.WhenAll(Hit(a, t, e, heal: false), sd);
            }
            Say($"{Name(a.Id)} 使用 {name}，" + (group.Count > 1 ? $"{group.Count} 下共 " : "") + $"{total} 点伤害" + (group.Any(e => e.Killed) ? "，击倒！" : ""));
            a.Sprite.Act = RunAct(a.Sprite);
            await TweenTo(a.Sprite, "position", a.Home, 0.24);
            a.Sprite.Act = Cfg.ActStand; a.Sprite.Dir = dir; a.Sprite.ZIndex = 0;
            return;
        }
        // 魔法：原地施法
        Float(a, name, Colors.Yellow);
        await PlayOnce(a.Sprite, a.Sprite.HasAct(Cfg.ActMagic) ? Cfg.ActMagic : Cfg.ActAttack);
        a.Sprite.Act = Cfg.ActStand;
        var hits = new List<Task>();
        foreach (var e in group)
            if (_units.TryGetValue(e.TargetId, out var t))
            {
                if (e.Missed) { Float(t, "Miss", Colors.LightGray); continue; }
                hits.Add(Hit(a, t, e, e.Heal));
            }
        await Task.WhenAll(hits);
        var targets = string.Join("、", group.Select(e => Name(e.TargetId)).Distinct());
        Say(e0.Heal ? $"{Name(a.Id)} 使用 {name}，{targets} 回复 {group.Sum(e => e.Damage)} 点"
                    : $"{Name(a.Id)} 使用 {name}，对 {targets} 造成 {group.Sum(e => e.Damage)} 点伤害" + (group.Any(e => e.Killed) ? "，击倒！" : ""));
    }

    /// <summary>一下命中：目标身上出效果、飘数字、血条变化；heal = 回血（绿字，不播受伤）。</summary>
    private async Task Hit(Unit a, Unit t, BattleEvent e, bool heal)
    {
        if (MagicEffect(e.TechId) is int fx) PlayEffectAnime(t, fx);
        else Effect(t, e.Magic ? (heal ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.6f, 0.2f)) : Colors.White);
        if (heal)
        {
            HealsShown++;
            t.Hp = Math.Min(t.MaxHp > 0 ? t.MaxHp : int.MaxValue, t.Hp + e.Damage);
            Float(t, e.Damage.ToString(), new Color(0.35f, 1f, 0.45f));
            var hb = t.Bar.CreateTween().SetSpeedScale(Speed);
            hb.TweenProperty(t.Bar, "value", (double)t.Hp, 0.3);
            if (t.Id == _playerId) _status.Text = $"HP {t.Hp}/{t.MaxHp}";
            await Wait(0.45);
            return;
        }
        t.Hp = Math.Max(0, t.Hp - e.Damage);
        Float(t, e.Damage.ToString(), t.Side == 0 ? new Color(1f, 0.55f, 0.4f) : Colors.White);
        Flash(t.Sprite);
        var hurt = PlayOnce(t.Sprite, Cfg.ActHurt);
        var knock = (t.Home - a.Home).Normalized() * 14;
        var tw = t.Sprite.CreateTween().SetSpeedScale(Speed);
        tw.TweenProperty(t.Sprite, "position", t.Home + knock, 0.08);
        tw.TweenProperty(t.Sprite, "position", t.Home, 0.14);
        var barTw = t.Bar.CreateTween().SetSpeedScale(Speed);
        barTw.TweenProperty(t.Bar, "value", (double)t.Hp, 0.3);
        await hurt;
        if (e.Killed && !t.Down)
        {
            t.Down = true;
            await PlayOnce(t.Sprite, Cfg.ActDead, hold: true);
            var fade = t.Sprite.CreateTween().SetSpeedScale(Speed);
            fade.TweenProperty(t.Sprite, "modulate", new Color(1, 1, 1, t.Side == 0 ? 0.6f : 0.35f), 0.3);
        }
        if (t.Id == _playerId) _status.Text = $"HP {t.Hp}/{t.MaxHp}";
    }

    /// <summary>
    /// 官方客户端（cg4.06cn.exe 0x49a838）里的攻击魔法特效表：每种属性一行 4 个动画，按魔法等级分档
    /// （0x487a78：LV1–3 第 0 档、4–6 第 1 档、7–9 第 2 档、10 第 3 档，越高越大）。
    /// 行：0 地（陨石）、1 水（冰柱）、2 火、3 风（龙卷）。
    /// </summary>
    private static readonly int[,] MagicFx =
    {
        { 110041, 110038, 110035, 110032 },
        { 110029, 110026, 110023, 110020 },
        { 110149, 110146, 110143, 110140 },
        { 110047, 110046, 110045, 110044 },
    };
    private static readonly byte[] LevelBucket = { 0, 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 3, 3 };

    /// <summary>
    /// 招式编号 → 特效动画。服务端 tech.txt：陨石/冰冻/火焰/风刃 = 技能 19–22，强力 23–26，超强 27–30，
    /// 招式编号 = 技能×100 + 等级−1（宠物版从 16 开始：1916 = 宠物陨石 LV1）。
    /// </summary>
    public static int? MagicEffect(int techId)
    {
        int skill = techId / 100, n = techId % 100;
        if (skill < 19 || skill > 30) return null;
        int lv = n >= 16 ? n - 15 : n + 1;
        return MagicFx[(skill - 19) % 4, LevelBucket[Math.Clamp(lv, 0, LevelBucket.Length - 1)]];
    }

    public int EffectsPlayed { get; private set; }

    private void PlayEffectAnime(Unit t, int anime)
    {
        var acts = Cache.Library.AnimeActions(anime);
        if (acts.Count == 0) { Effect(t, Colors.Orange); return; }
        EffectsPlayed++;
        var a = acts.Keys.OrderBy(k => k.Act).ThenBy(k => k.Dir).First();
        var fx = new AnimSprite { Cache = Cache, AnimeId = anime, Dir = a.Dir, Act = a.Act, FallbackAct = a.Act, Position = t.Sprite.Position, Scale = t.Sprite.Scale, ZIndex = 9, SoundCue = f => GameAudio.I?.Cue(f) };
        _stage.AddChild(fx);
        fx.PlayOnce(a.Act, () => fx.QueueFree());
        GetTree().CreateTimer(3.0).Timeout += () => { if (IsInstanceValid(fx)) fx.QueueFree(); };
    }

    /// <summary>
    /// 命中效果。官方每个技能有自己的特效动画（还没对上编号），先用一个扩散的光圈代替。
    /// </summary>
    private void Effect(Unit t, Color c)
    {
        var ring = new EffectRing { Position = t.Sprite.Position + new Vector2(0, -40), Tint = c, ZIndex = 9 };
        _root.AddChild(ring);
        var tw = ring.CreateTween().SetSpeedScale(Speed).SetParallel();
        tw.TweenProperty(ring, "scale", new Vector2(2.2f, 1.4f), 0.4).From(new Vector2(0.3f, 0.2f));
        tw.TweenProperty(ring, "modulate:a", 0.0, 0.4).SetDelay(0.1);
        tw.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    private async Task PlayAttack(Unit a, Unit t, BattleEvent e)
    {
        var dir = a.Sprite.Dir;
        // 冲到目标前方（离目标约 55% 的距离），途中播放行走动作
        var to = a.Home + (t.Home - a.Home) * 0.72f;
        a.Sprite.Act = RunAct(a.Sprite);
        a.Sprite.ZIndex = 1;
        await TweenTo(a.Sprite, "position", to, 0.28);
        a.Sprite.Act = Cfg.ActStand;
        if (a.Sprite.HasAct(Cfg.ActAttack)) AttackAnimsShown++;
        var (atkHit, atkDone) = PlayAttack(a.Sprite, Cfg.ActAttack);
        await atkHit;   // 官方：伤害在攻击动画的"命中帧"出现，不是等整个动作做完
        if (e.Missed)
        {
            Say($"{Name(a.Id)} 攻击 {Name(t.Id)}，没打中");
            Float(t, "Miss", Colors.LightGray);
            var side = new Vector2(0, -18);
            await TweenTo(t.Sprite, "position", t.Home + side, 0.1);
            await TweenTo(t.Sprite, "position", t.Home, 0.12);
        }
        else
        {
            Say($"{Name(a.Id)} 攻击 {Name(t.Id)}，造成 {e.Damage} 点伤害" + (e.Killed ? "，击倒！" : ""));
            t.Hp = Math.Max(0, t.Hp - e.Damage);
            Float(t, e.Damage.ToString(), t.Side == 0 ? new Color(1f, 0.55f, 0.4f) : Colors.White);
            Flash(t.Sprite);
            var hurt = PlayOnce(t.Sprite, Cfg.ActHurt);
            var knock = (t.Home - a.Home).Normalized() * 14;
            var tw = t.Sprite.CreateTween().SetSpeedScale(Speed);
            tw.TweenProperty(t.Sprite, "position", t.Home + knock, 0.08);
            tw.TweenProperty(t.Sprite, "position", t.Home, 0.14);
            var barTw = t.Bar.CreateTween().SetSpeedScale(Speed);
            barTw.TweenProperty(t.Bar, "value", (double)t.Hp, 0.3);
            await hurt;
            if (e.Killed)
            {
                t.Down = true;
                await PlayOnce(t.Sprite, Cfg.ActDead, hold: true);
                var fade = t.Sprite.CreateTween().SetSpeedScale(Speed);
                fade.TweenProperty(t.Sprite, "modulate", new Color(1, 1, 1, t.Side == 0 ? 0.6f : 0.35f), 0.3);
            }
            if (t.Id == _playerId) _status.Text = $"HP {t.Hp}/{t.MaxHp}";
        }
        await atkDone;
        a.Sprite.Act = RunAct(a.Sprite);
        await TweenTo(a.Sprite, "position", a.Home, 0.24);
        a.Sprite.Act = Cfg.ActStand; a.Sprite.Dir = dir; a.Sprite.ZIndex = 0;
    }

    // —— 小工具 ——

    private Task Wait(double seconds)
    {
        var tcs = new TaskCompletionSource();
        GetTree().CreateTimer(seconds / Math.Max(0.01f, Speed)).Timeout += () => tcs.TrySetResult();
        return tcs.Task;
    }

    private Task TweenTo(Node n, string prop, Variant value, double seconds)
    {
        var tcs = new TaskCompletionSource();
        var tw = n.CreateTween().SetSpeedScale(Speed);
        tw.TweenProperty(n, prop, value, seconds);
        tw.Finished += () => tcs.TrySetResult();
        return tcs.Task;
    }

    /// <summary>战斗里跑过去/跑回来用"跑"动作 3（没有就用走路）。</summary>
    private int RunAct(AnimSprite s) => s.HasAct(Cfg.ActRun) ? Cfg.ActRun : Cfg.ActWalk;

    /// <summary>播放攻击动作：Hit 在动画的命中帧（帧标记 ≥10000）完成，没有命中帧就在动作结束时完成；Done 在动作结束时完成。</summary>
    private (Task Hit, Task Done) PlayAttack(AnimSprite s, int act)
    {
        var hit = new TaskCompletionSource();
        s.HitCue = () => hit.TrySetResult();
        var done = PlayOnce(s, act);
        async Task Finish() { await done; hit.TrySetResult(); s.HitCue = null; }
        return (hit.Task, Finish());
    }

    private Task PlayOnce(AnimSprite s, int act, bool hold = false)
    {
        if (act < 0) return Wait(0.2);
        var tcs = new TaskCompletionSource();
        s.PlayOnce(act, () => tcs.TrySetResult(), hold);
        // 动画速度不随 Speed 变化，测试加速时用定时器兜底
        if (Speed > 1.5f) GetTree().CreateTimer(1.5 / Speed).Timeout += () => tcs.TrySetResult();
        return tcs.Task;
    }

    private void Flash(AnimSprite s)
    {
        var tw = s.CreateTween().SetSpeedScale(Speed);
        tw.TweenProperty(s, "modulate", new Color(2.2f, 1.4f, 1.4f), 0.05);
        tw.TweenProperty(s, "modulate", Colors.White, 0.18);
    }

    private void Float(Unit u, string text, Color color)
    {
        var l = new Label { Text = text, Position = u.Sprite.Position + new Vector2(-30, -130), Size = new Vector2(60, 30), HorizontalAlignment = HorizontalAlignment.Center, ZIndex = 10 };
        l.AddThemeFontSizeOverride("font_size", 24);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 6);
        _root.AddChild(l);
        var tw = l.CreateTween().SetSpeedScale(Speed).SetParallel();
        tw.TweenProperty(l, "position:y", l.Position.Y - 40, 0.7);
        tw.TweenProperty(l, "modulate:a", 0.0, 0.7).SetDelay(0.3);
        tw.Chain().TweenCallback(Callable.From(l.QueueFree));
    }
}

/// <summary>技能命中时的临时光圈特效。</summary>
public partial class EffectRing : Node2D
{
    public Color Tint = Colors.White;
    public override void _Draw()
    {
        DrawArc(Vector2.Zero, 30, 0, Mathf.Tau, 32, Tint, 5);
        DrawCircle(Vector2.Zero, 18, new Color(Tint, 0.35f));
    }
}

/// <summary>
/// 战斗时叠在地图上的那层（官方 exe 0x407e8f 起，战斗底图 = EN 第 2 段）：
///   底图在 22130–22149 或 22970–22979（22977 除外）时：只把底图的四个角贴到画面四角
///     （底图里半透明棋盘点那片不画；左上 125×95 取 (0,0)、右上 256×192 取 (386,0)、左下 146×111 取 (0,369)、右下 146×111 取 (494,369)），
///     然后「战斗背景修正」开着就整屏盖一层半透明暗棕（实测 背景×0.72 + (19,11,5)），
///     再从 (−124,−52) 起每 128×48 贴一块菱形格（隔行错开 64）：修正开 = PNG 243（只有边线），关 = BMP 221（边线 + 棋盘点的棕色底）。
///   其它底图：整张画在画面中间（这个服务端没见到）。
/// </summary>
public partial class BattleGrid : Control
{
    public TextureCache? Cache;
    public int Backdrop;
    public bool Fix = true;
    private static ImageTexture? _line;

    public static bool Tiled(int bg) => bg == 0 || (bg is >= 22130 and <= 22149) || (bg is >= 22970 and <= 22979 && bg != 22977);

    public override void _Draw()
    {
        var sz = Size; float w = sz.X, h = sz.Y;
        var tex = Backdrop > 0 ? Solid(Backdrop) : null;
        if (!Tiled(Backdrop))
        {
            if (tex != null) DrawTexture(tex, (sz - tex.GetSize()) / 2);
            return;
        }
        if (tex != null)
        {
            void Corner(float dx, float dy, float sx, float sy, float cw, float ch) => DrawTextureRectRegion(tex, new Rect2(dx, dy, cw, ch), new Rect2(sx, sy, cw, ch));
            Corner(0, 0, 0, 0, 125, 95);
            Corner(w - 256, 0, 386, 0, 256, 192);
            Corner(0, h - 111, 0, 369, 146, 111);
            Corner(w - 146, h - 111, 494, 369, 146, 111);
        }
        if (Fix) DrawRect(new Rect2(Vector2.Zero, sz), new Color(68 / 255f, 39 / 255f, 18 / 255f, 0.28f));
        var tile = Fix ? Line() : ExeArt.Bmp(221);
        if (tile == null) return;
        int toggle = 0;
        for (float y = -52, x = -124; y <= h;)
        {
            DrawTexture(tile, new Vector2(x, y));
            x += 128;
            if (x > w) { y += 48; toggle = 1 - toggle; x = 4 - toggle * 64; }
        }
    }

    /// <summary>底图里的棋盘点（半透明的那片菱形）官方画出来是看不见的（实测角上透出地图、没有网点）：只留实心的像素。</summary>
    private int _solidId = -1; private ImageTexture? _solid;
    private ImageTexture? Solid(int id)
    {
        if (id == _solidId) return _solid;
        _solidId = id; _solid = null;
        if (Cache?.ImageNo(id).Tex is not { } t) return null;
        var im = t.GetImage(); im.Convert(Image.Format.Rgba8);
        for (int y = 0; y < im.GetHeight(); y++)
            for (int x = 0; x < im.GetWidth(); x++)
                if (im.GetPixel(x, y).A < 0.99f) im.SetPixel(x, y, new Color(0, 0, 0, 0));
        return _solid = ImageTexture.CreateFromImage(im);
    }

    /// <summary>PNG 243（黑色当透明）。</summary>
    private static ImageTexture? Line()
    {
        if (_line != null || ExeArt.Png(243) is not { } t) return _line;
        var im = t.GetImage(); im.Convert(Image.Format.Rgba8);
        for (int y = 0; y < im.GetHeight(); y++)
            for (int x = 0; x < im.GetWidth(); x++)
                if (im.GetPixel(x, y) is { R: 0, G: 0, B: 0 }) im.SetPixel(x, y, new Color(0, 0, 0, 0));
        return _line = ImageTexture.CreateFromImage(im);
    }
}
