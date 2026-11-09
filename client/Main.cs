using CgPort.Client;
using CgPort.Core;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 入口：读配置 → 加载素材 → 创建会话（目前是单机 LocalSession）→ 显示地图/角色/NPC → 点击行走/对话 → 遇敌进入战斗。
/// 客户端只通过 IGameSession 与服务端交互；以后加联网模式只需要换一个会话实现。
/// 单机模式自动存档到 user://save/slot1.json（换地图、战斗结束、对话结束时）。
/// </summary>
public partial class Main : Node2D
{
    private GameConfig _cfg = null!;
    private AssetLibrary _assets = null!;
    private TextureCache _cache = null!;
    public TextureCache Cache => _cache;
    private TileInfoTable _tiles = null!;
    private IGameSession _session = null!;
    private MapView? _view;
    public MapView? View => _view;
    private AnimSprite _player = null!;
    private Label _playerTag = null!;
    private Camera2D _cam = new();
    private Label _hud = new();
    private ChatLog _chat = null!;
    private PetListWindow _pets = null!;
    private PetDetailWindow _petDetail = null!;
    public PetDetailWindow PetDetail => _petDetail;
    private StatusPanel _status = null!;
    private SystemPanel _system = null!;
    private SkillPanel _skills = null!;
    private MapPanel _mapWin = null!;
    public MapPanel MapWin => _mapWin;
    public SkillPanel SkillWin => _skills;
    private ItemPanel _items = null!;
    public StatusPanel StatusWin => _status;
    public ItemPanel ItemWin => _items;
    public PetListWindow Pets => _pets;
    public Hud HudSkin => _hudSkin;
    public GameConfig Cfg => _cfg;
    /// <summary>测试用：周围对象身上显示的名字标签数。</summary>
    public int VisibleNameTags() => _npcNodes.Count(n => IsInstanceValid(n) && n.GetChildren().OfType<Label>().Any());
    public SystemPanel SystemWin => _system;
    public ChatLog Chat => _chat;
    private TradePicker _tradePicker = null!;
    private CardBookWindow _cardBook = null!;
    private AlbumWindow _album = null!;
    private GameAudio? _audio;
    public GameAudio? Audio => _audio;
    private BankWindow _bank = null!;
    private SkillShopWindow _skillShop = null!;
    public SkillShopWindow SkillShopUi => _skillShop;
    private ProductionUi _prod = null!;
    public ProductionUi ProdUi => _prod;
    private GoldWindow _gold = null!;
    public GoldWindow GoldUi => _gold;
    public ItemPanel ItemsUi => _items;
    private TradePicker _itemMember = null!;
    private ItemTargetWindow _itemTarget = null!;
    public TradePicker ItemMemberUi => _itemMember;
    public ItemTargetWindow ItemTargetUi => _itemTarget;
    public BankWindow BankUi => _bank;
    public AlbumWindow AlbumUi => _album;
    private MailEditWindow _mailEdit = null!;
    private MailRecordWindow _mailRecord = null!;
    private ConfirmBox _confirm = null!;
    public CardBookWindow CardBookUi => _cardBook;
    public MailEditWindow MailEditUi => _mailEdit;
    public MailRecordWindow MailRecordUi => _mailRecord;
    public ConfirmBox ConfirmUi => _confirm;
    private void ClearMail() { if (_session is NetworkSession n) n.UnreadMail = 0; _hudSkin.SetMailLit(false); }
    private TradeWindow _trade = null!;
    public TradePicker TradePickerUi => _tradePicker;
    public TradeWindow TradeUi => _trade;
    private ChatInput _chatIn = null!;
    private Transition? _trans;
    public Transition Trans => _trans!;
    public ChatInput ChatIn => _chatIn;
    private Hud _hudSkin = null!;
    private BattleUi _battleUi = null!;
    private DialogPanel _dialog = null!;
    private ShopPanel _shop = null!;
    public ShopPanel Shop => _shop;
    private BagPanel _bag = null!;
    private Toast _toast = null!;
    private CalibrationPanel _calib = null!;
    public BattleUi Battle => _battleUi;
    public CalibrationPanel Calibration => _calib;
    private readonly Queue<int> _pendingSteps = new();
    private Vector2 _from, _to;
    private double _t = 1;
    private const double StepSeconds = 0.22;
    private AStarGrid2D _astar = new();
    private readonly Dictionary<Vector2I, Npc> _npcAt = new();
    private Npc? _talkTarget;
    private string _talkingTo = "";
    private string _savePath = "";

    public IGameSession Session => _session;
    public Vector2I PlayerCell => new(_session.X, _session.Y);
    public string SavePath => _savePath;

    /// <summary>
    /// 手机模式：整个画面按整数倍放大（1080 高的屏幕 → 2 倍，画布 540 高），界面和文字清楚、按钮够大；
    /// 地图用相机缩小补回来，看到的范围和原版一样（屏幕高度对应原版 720 像素）。原版模式：画布固定 960×720 按比例放大。
    /// </summary>
    public int UiScale { get; private set; } = 1;
    private void ApplyUiScale()
    {
        var root = GetTree().Root;
        var ws = DisplayServer.WindowGetSize();
        if (_cfg.MobileUi && ws.Y > 0)
        {
            UiScale = Math.Max(1, ws.Y / 520);
            root.ContentScaleSize = new Vector2I(Math.Max(1, ws.X / UiScale), Math.Max(1, ws.Y / UiScale));
            root.ContentScaleStretch = Window.ContentScaleStretchEnum.Integer;
            float world = ws.Y / 720f;                 // 原版视野：屏幕高度对应 720 像素地图
            _cam.Zoom = Vector2.One * (_cfg.Zoom * world / UiScale);
        }
        else
        {
            UiScale = 1;
            root.ContentScaleSize = new Vector2I(960, 720);
            root.ContentScaleStretch = Window.ContentScaleStretchEnum.Fractional;
            _cam.Zoom = Vector2.One * _cfg.Zoom;
        }
        GameLog.Write("画面", $"{(_cfg.MobileUi ? "手机" : "原版")}模式 窗口 {ws} 界面 {UiScale} 倍 画布 {root.GetVisibleRect().Size} 地图缩放 {_cam.Zoom.X:0.00}");
    }

    public override void _Ready()
    {
        var cfg = GameConfig.Load();
        if (cfg == null) { AddChild(new SetupScreen()); return; }   // 没有有效配置：首次运行设置
        _cfg = cfg;
        GameLog.Open(_cfg.GameRoot);
        AddChild(new GameLog());
        Engine.MaxFps = 60;
        AddChild(new TouchAssist());   // 手指点在小按钮旁边也算点中   // 高刷新率手机不必跑满 120，省电
        if (_cfg.ShowFps) AddChild(new FpsOverlay());
        _audio = new GameAudio { Name = "Audio" };
        AddChild(_audio);
        _audio.Init(_cfg.GameRoot, _cfg.BgmVolume / 100f, _cfg.SeVolume / 100f);
        GameLog.Write("配置", $"模式 {_cfg.Mode} 服务器 {_cfg.Host}:{_cfg.Port} 游戏目录 {_cfg.GameRoot}");
        _assets = new AssetLibrary(_cfg.GameRoot, _cfg.Sets, _cfg.Palette);
        _cache = new TextureCache(_assets);
        // 配置里的人物形象在这套图档里不存在（旧默认 100000 在 4.06 图档里没有）就换成 106002（联网时服务端给的默认形象），否则单机人物是隐形的
        if (_assets.AnimeActions(_cfg.PlayerAnime).Count == 0 && _assets.AnimeActions(106002).Count > 0) _cfg.PlayerAnime = 106002;
        _tiles = new TileInfoTable();
        foreach (var s in _cfg.Sets)
        {
            var p = Path.Combine(_cfg.GameRoot, s.GraphicInfo);
            if (GameFiles.Exists(p)) _tiles.LoadGraphicInfo(p);
        }

        _savePath = ProjectSettings.GlobalizePath(_cfg.UsingTestData ? "user://save/testdata.json" : "user://save/slot1.json");
        if (_cfg.Mode == "network")
        {
            // 联网：地图文件优先用服务端目录里的 LS2MAP，没有就用客户端缓存 map/0/<楼层>.dat
            var serverMaps = GameFiles.DirectoryExists(Path.Combine(_cfg.GameRoot, _cfg.ServerDir, "data", "map"))
                ? MapData.IndexServerMapDir(Path.Combine(_cfg.GameRoot, _cfg.ServerDir, "data", "map")) : new();
            var net = new NetworkSession(new NetworkOptions
            {
                Host = _cfg.Host, Port = _cfg.Port, Account = _cfg.Account, Password = _cfg.Password,
                CdKey = _cfg.CdKey.Length > 0 ? _cfg.CdKey : _cfg.Account, ManualCharSelect = _cfg.CharSelect,
                MapCacheDir = ProjectSettings.GlobalizePath($"user://mapcache/{_cfg.Host}_{_cfg.Port}"),
                ResolveMap = f => serverMaps.TryGetValue(f, out var m) ? m.Path
                    : GameFiles.Exists(Path.Combine(_cfg.GameRoot, "map", "0", f + ".dat")) ? Path.Combine(_cfg.GameRoot, "map", "0", f + ".dat") : null,
            });
            var albumCache = new AlbumCache($"{_cfg.Host}_{_cfg.Port}");
            net.AlbumCacheLoad = albumCache.Get; net.AlbumCacheSave = albumCache.Put;
            net.Error += e => { _toast?.Show(e, 6); GameLog.Write("错误", e); };
            net.GameLog = t => GameLog.Write("网络", t);
            net.Disconnected += OnDisconnected;
            net.Status += m => { _toast?.Show(m, 2); GameLog.Write("状态", m); };
            _session = net;
        }
        else
        {
            if (_cfg.StartX == null && GameConfig.LoginPoint(_cfg.GameRoot, _cfg.ServerDir, _cfg.StartMap) is var (lx, ly)) { _cfg.StartX = lx; _cfg.StartY = ly; }
            var local = new LocalSession(_cfg.GameRoot, _cfg.ServerDir, new SessionOptions
            {
                StartMap = _cfg.StartMap, StartX = _cfg.StartX, StartY = _cfg.StartY,
                EncounterScale = _cfg.EncounterScale, EncountersEnabled = _cfg.Encounters,
                GraphicInfoFiles = _cfg.Sets.Select(s => s.GraphicInfo.StartsWith("bin/") ? s.GraphicInfo[4..] : s.GraphicInfo).ToList(),
            });
            if (_cfg.LoadSave && PlayerState.Load(_savePath) is PlayerState saved)
            {
                // 原版重新登录是回到登录点（不是下线的位置）：有登录点就不用存档里的位置
                if (_cfg.StartX != null) saved.MapId = 0;
                local.LoadPlayer(saved);
            }
            _session = local;
        }

        _session.MapEntered += OnMapEntered;
        _session.Moved += OnMoved;
        _session.BattleStarted += v => { _trans?.Enter(); _pendingSteps.Clear(); _talkTarget = null; _battleUi.Show(v); _audio?.BattleStart(); _player.Visible = false; _hudSkin.SetBattle(true); };   // 官方战斗时主界面（状态面板、右上按钮）也隐藏   // 官方战斗时地图上的自己隐藏，只在战斗位置上显示
        _session.TurnResolved += (ev, v) => _battleUi.AppendLog(ev, v);
        _session.BattleEnded += o => { _battleUi.Close(o); _audio?.BattleEnd(); AutoSave(); };
        _func = new FunctionPanel();
        AddChild(_func);
        _func.Chosen = code => (_session as NetworkSession)?.CustomFunction(code);
        _result = new BattleResultWindow { Cache = _cache, Session = () => _session as NetworkSession };
        AddChild(_result);
        if (_session is NetworkSession nsR)
        {
            // 官方：战斗画面退出、回到地图后才出结果窗口
            // 天气（EF）：屏幕层，在所有窗口下面；战斗画面时不画
            var wl = new CanvasLayer { Layer = 1 }; AddChild(wl); MoveChild(wl, 0);
            _weather = new Weather();
            for (int i = 0; i < 256; i++) { uint c = _assets.Palette.Colors[i]; _weather.Palette[i] = new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f); }
            wl.AddChild(_weather);
            nsR.WeatherChanged += (fl, lv) => _weather.Set(fl, lv);
            nsR.MapPatched += (x1, y1, x2, y2) =>
            {
                if (_view == null || _view.Map != nsR.Map) return;
                _view.ApplyPatch(x1, y1, x2, y2);
                var r = new Rect2I(x1, y1, x2 - x1, y2 - y1);
                _walkDirty = _walkDirty is { } d ? d.Merge(r) : r;
            };
            nsR.SoundRequested += id => _audio?.PlaySe(id);
            nsR.BgmRequested += id => _audio?.PlayBgm(id);
            nsR.BattleResultReceived += r => { if (!_battleUi.Visible && !nsR.InBattle) _result.Show(r); else _resultPending = true; };
            nsR.StatusChanged += () => _hudSkin?.SetBurst(nsR.BurstSeconds);
        }
        _session.RewardsGained += OnRewards;
        _session.DialogChanged += p =>
        {
            bool shop = p?.Shop != null;
            _shop.Gold = _session.Player.Gold;
            _shop.ShowPage(shop ? p : null);
            _dialog.ShowPage(_talkingTo, shop ? null : p);
            if (p == null) AutoSave();
        };
        _session.StatusChanged += () =>
        {
            _bag.Refresh(); if (_pets?.Visible == true) _pets.Refresh(); if (_petDetail?.Visible == true) _petDetail.Refresh(); _status?.Refresh(); _items?.Refresh(); _skills?.Refresh();
            // 联网：角色形象以服务端为准
            if (_session is NetworkSession ns && ns.SelfAppearance >= 100000 && _player.AnimeId != ns.SelfAppearance) _player.AnimeId = ns.SelfAppearance;
        };
        _session.ObjectsChanged += SpawnNpcs;
        if (_session is NetworkSession nsg) nsg.GroundChanged += SpawnGround;
        if (_session is NetworkSession nsm) { nsm.ObjectMoved += OnObjectMoved; nsm.ObjectActed += OnObjectActed; nsm.FightingChanged += SpawnNpcs; }

        _player = new AnimSprite { Cache = _cache, AnimeId = _cfg.PlayerAnime, Act = _cfg.ActStand, FallbackAct = _cfg.ActStand, Dir = 4 };
        _player.AddChild(_cam);
        _playerTag = NameTag.Make(_session.Player.Name, NameTag.PlayerColor);
        _player.AddChild(_playerTag);
        ApplyUiScale();
        GetTree().Root.SizeChanged += ApplyUiScale;

        var ui = new CanvasLayer();
        AddChild(ui);
        _hud.Position = new Vector2(8, 8);
        ui.AddChild(_hud);
        _hudSkin = new Hud { Cache = _cache, PinMenu = _cfg.MobileUi };
        AddChild(_hudSkin);
        // 右上按钮（官方提示）：22581 对战 Ctrl+J、22583 观战 Ctrl+K、22585 组队 Ctrl+L、22587 交换名片、22589 交易
        _hudSkin.ButtonPressed += id =>
        {
            if (_session is not NetworkSession ns) return;
            if (id == 22581) ns.Duel();
            else if (id == 22583) ns.Watch();
            else if (id == 22585) ns.PartyButton();
            else if (id == 22587) ns.ExchangeCard();
            else if (id == 22589) ns.RequestTrade();
            else if (id == 244003) ToggleStall();
            else if (id == 22591) ToggleEmote();
        };
        // 名片簿 / 写信 / 邮件记录 / 删除确认
        _cardBook = new CardBookWindow { Cache = _cache, Session = () => _session as NetworkSession };
        _mailEdit = new MailEditWindow { Cache = _cache, Session = () => _session as NetworkSession };
        _mailRecord = new MailRecordWindow { Cache = _cache, Session = () => _session as NetworkSession };
        _confirm = new ConfirmBox { Cache = _cache };
        AddChild(_cardBook); AddChild(_mailEdit); AddChild(_mailRecord); AddChild(_confirm);
        _cardBook.WriteMail = i => { _cardBook.Visible = false; _mailEdit.Open(i); };
        _mailEdit.Back = () => _cardBook.Toggle();
        _cardBook.ShowRecord = i => _mailRecord.Open(i);
        _cardBook.AskDelete = i =>
        {
            if (_session is NetworkSession nd && nd.Cards[i] is { } card)
                _confirm.Ask($"确实要将「{card.Name}」从名片列表删除吗？", () => nd.DeleteCard(i));
        };
        if (_session is NetworkSession nsC)
        {
            nsC.CardsChanged += () => { if (_cardBook.Visible) _cardBook.Refresh(); if (_mailEdit.Visible) _mailEdit.Refresh(); };
            nsC.MailReceived += m => { _hudSkin.SetMailLit(true); if (_mailRecord.Visible) _mailRecord.Refresh(); };
            _hudSkin.MailClicked = () => { _cardBook.Visible = true; _cardBook.Refresh(); ClearMail(); };
        }
        _album = new AlbumWindow { Cache = _cache, Session = () => _session as NetworkSession };
        AddChild(_album);
        if (_session is NetworkSession nsA) nsA.AlbumChanged += () => { if (_album.Visible) _album.Refresh(); };
        // 用道具选对象：先选队伍里的人（同交易的选人小窗），再选人/宠物
        _itemMember = new TradePicker { Cache = _cache };
        _itemTarget = new ItemTargetWindow { Cache = _cache };
        AddChild(_itemMember); AddChild(_itemTarget);
        if (_session is NetworkSession nsI)
        {
            nsI.ItemMemberRequested += names => { _items.Visible = false; _itemMember.Show(names); };   // 官方：物品窗口这时关掉
            _itemMember.Picked += i => nsI.SelectItemMember(i);
            nsI.ItemTargetRequested += list => _itemTarget.Show(list);
            _itemTarget.Picked += c => { if (nsI.Treating && _prod != null) _prod.TreatName = _itemTarget.Shown.FirstOrDefault(t => t.Code == c)?.Name ?? ""; nsI.UseItemOn(c); };
            _itemTarget.Back += () => _itemMember.Show(nsI.ItemMembers());
        }
        _prod = new ProductionUi { Cache = _cache, Session = () => _session as NetworkSession, PlayerAnime = () => _player.AnimeId };
        AddChild(_prod);
        if (_session is NetworkSession nsP)
        {
            _prod.Bind(nsP);
            _prod.Opened = () => { if (_skills != null) _skills.Visible = false; };
            nsP.BattleStarted += _ => _prod.CloseAll();
        }
        _skillShop = new SkillShopWindow { Cache = _cache, Session = () => _session as NetworkSession };
        AddChild(_skillShop);
        if (_session is NetworkSession nsS) nsS.SkillShopChanged += () => _skillShop.Refresh();
        _bank = new BankWindow { Cache = _cache, Session = () => _session as NetworkSession };
        AddChild(_bank);
        // 摆摊 / 浏览摊位
        _stall = new StallWindow { Cache = _cache, Session = () => _session as NetworkSession, PlayerAnime = () => _player.AnimeId };
        AddChild(_stall);
        if (_session is NetworkSession nsSt)
        {
            nsSt.StallChanged += () =>
            {
                if (nsSt.Browsing != null && !_stall.Visible) _stall.OpenBrowse();
                else if (_stall.Visible) _stall.Refresh();
                SpawnNpcs();
            };
            nsSt.StatusChanged += () => { if (_stall.Visible && !_stall.Pricing) _stall.Refresh(); };
        }
        if (_session is NetworkSession nsB)
        {
            nsB.BankChanged += () => { if (nsB.Bank == null) _bank.Visible = false; else if (!_bank.Visible) _bank.Open(); else _bank.Refresh(); };
            nsB.StatusChanged += () => { if (_bank.Visible) _bank.Refresh(); };
        }
        _tradePicker = new TradePicker { Cache = _cache };
        AddChild(_tradePicker);
        _trade = new TradeWindow { Cache = _cache, Session = () => _session as NetworkSession };
        AddChild(_trade);
        if (_session is NetworkSession nsT)
        {
            nsT.TradeCandidatesReceived += names => { if (names.Count > 0) _tradePicker.Show(names); };
            _tradePicker.Picked += i => nsT.SelectTradeTarget(i);
            nsT.TradeChanged += () => { if (nsT.Trade == null) _trade.Visible = false; else if (!_trade.Visible) _trade.Open(); else _trade.Refresh(); };
            nsT.StatusChanged += () => { if (_trade.Visible) _trade.Refresh(); };
        }
        if (_session is NetworkSession nsF)
        {
            nsF.FlagsChanged += () => _hudSkin.SetFlags(nsF.Flags);
            _hudSkin.SetFlags(nsF.Flags);
            _hudSkin.FlagClicked += bit => nsF.ToggleFlag(bit);
        }
        var bagBtn = new Button { Text = "背包", AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -110, OffsetRight = -12, OffsetTop = 8, OffsetBottom = 52 };
        bagBtn.Pressed += () => _bag.Toggle();
        ui.AddChild(bagBtn);
        var calBtn = new Button { Text = "动作", AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -110, OffsetRight = -12, OffsetTop = 58, OffsetBottom = 102 };
        calBtn.Pressed += () => _calib.Toggle();
        ui.AddChild(calBtn);
        CallDeferred(nameof(ApplySkinLayout), bagBtn, calBtn);

        _battleUi = new BattleUi { Cache = _cache, Cfg = _cfg };
        AddChild(_battleUi);
        _battleUi.CommandsPerTurn = _session is NetworkSession ? 2 : 1;
        _battleUi.Closed += () => _trans?.Exit();
        if (_session is NetworkSession nsb) nsb.CommandRequested += slot => _battleUi.SetCommandSlot(slot, nsb.PetTurn, nsb.HasBattlePet);
        _battleUi.CommandSkill += (k, t, a, lv) => _session.SubmitCommand(new Command((CommandKind)k, t, a, lv));
        _battleUi.Skills = () => _session.Player.Skills;
        _battleUi.SkillSlotsTotal = () => _session.Player.SkillSlots;
        _battleUi.PlayerMp = () => (_session.Player.Mp, _session.Player.MaxMp);
        _battleUi.AllPets = () => _session.Player.Pets;
        _battleUi.BagStacks = () => _session.Player.Items;
        _battleUi.PetOnField = () => _session is NetworkSession { PetOnField: true } ? _session.Player.Pets.FirstOrDefault(p => p.State == PetInfo.Battle) : null;
        _battleUi.BattlePet = () => _session is NetworkSession ? _session.Player.Pets.FirstOrDefault(p => p.State == PetInfo.Battle) : null;
        _battleUi.CommandChosen += k => _session.SubmitCommand(new Command((CommandKind)k));
        _battleUi.CommandTarget += (k, t) => _session.SubmitCommand(new Command((CommandKind)k, t));
        _battleUi.CommandArg += (k, t, a) => _session.SubmitCommand(new Command((CommandKind)k, t, a));
        _battleUi.BagItems = () => _session.Player.Items.Select(i => (i.Slot, i.ItemId, i.Count)).ToList();
        _battleUi.ItemLook = id => _session.ItemInfo(id) is { } it ? (it.Name, it.GraphicId) : ("道具", 0);
        _dialog = new DialogPanel { Cache = _cache };
        AddChild(_dialog);
        _dialog.ButtonPressed += b => _session.Answer(b);
        _shop = new ShopPanel { Cache = _cache };
        AddChild(_shop);
        _shop.Submit += a => _session.Answer(a);
        _bag = new BagPanel { Cache = _cache, Session = _session };
        AddChild(_bag);
        _pets = new PetListWindow { Cache = _cache, Pets = () => _session.Player.Pets, SetState = (slot, st) => (_session as NetworkSession)?.SetPetState(slot, st) };
        AddChild(_pets);
        _petDetail = new PetDetailWindow
        {
            Cache = _cache, Pets = () => _session.Player.Pets,
            SetState = (slot, st) => (_session as NetworkSession)?.SetPetState(slot, st),
            AddPoint = (slot, k) => (_session as NetworkSession)?.AddPetPoint(slot, k),
            Rename = (slot, n) => (_session as NetworkSession)?.RenamePet(slot, n),
            BackToList = () => { _pets.Visible = true;
        _petDetail.SwapTechs = (pet, a, b) => (_session as NetworkSession)?.SwapPetTechs(pet, a, b); _pets.Refresh(); },
        };
        AddChild(_petDetail);
        _pets.OpenDetail = slot => { _pets.Visible = false; _petDetail.Open(slot); };
        _pets.Drop = slot => (_session as NetworkSession)?.DropPet(slot);
        _pets.DropAsk = slot => { var p = _session.Player.Pets.FirstOrDefault(x => x.Slot == slot); if (p != null) _confirm.Ask($"要把「{p.DisplayName}」放在地上吗？", () => (_session as NetworkSession)?.DropPet(slot)); };
        _status = new StatusPanel { Cache = _cache, Player = () => _session.Player, AddPoint = k => (_session as NetworkSession)?.AddStatPoint(k), Session = () => _session as NetworkSession, Ask = (t, yes) => _confirm?.Ask(t, yes) };
        AddChild(_status);
        _mapWin = new MapPanel { Cache = _cache, Where = () => (_view?.Map, _session.X, _session.Y), MapName0 = () => _session.CurrentMap?.Name ?? "" };
        AddChild(_mapWin);
        _skills = new SkillPanel { Cache = _cache, Player = () => _session.Player };
        _skills.TechChosen = (sk, t) => _prod?.UseTech(sk, t);
        _skills.Swap = (a, b) => (_session as NetworkSession)?.SwapSkills(a, b);
        AddChild(_skills);
        _system = new SystemPanel { Cache = _cache, Cfg = _cfg, Toast = t => _toast.Show(t, 2), DrawChanged = SpawnNpcs };
        _system.Chosen += k =>
        {
            switch (k)
            {
                case 0: _system.Visible = false; (_session as NetworkSession)?.Logout(); AddChild(new SetupScreen { Layer = 50 }); break;
                case 1: _chat.MaxLines = _chat.MaxLines >= 6 ? 2 : _chat.MaxLines + 2; _toast.Show($"聊天显示 {_chat.MaxLines} 行", 2); break;
                case 2 or 3 or 4 or 5: _system.Open(k); break;   // 音樂 / 滑鼠 / 鍵盤 / 繪圖設定
                case 7: _system.Visible = false; AddChild(new SetupScreen { Layer = 50, FromGame = true }); break;   // 高级设定：界面模式、显示帧数、服务器（官方这一项的内容还没做）
                default: _toast.Show("这项设定还没做", 2); break;
            }
        };
        AddChild(_system);
        _items = new ItemPanel { Cache = _cache, Player = () => _session.Player, PlayerAnime = () => _player.AnimeId, Use = sl => (_session as NetworkSession)?.UseItem(sl), Move = (a, b) => (_session as NetworkSession)?.MoveItem(a, b) };
        AddChild(_gold = new GoldWindow { Cache = _cache, Gold = () => _session.Player.Gold });
        if (_session is NetworkSession nsw)
        {
            _items.Wallet = () => { _items.Visible = false; _gold.Open(); };
            _items.SortBag = nsw.SortBag;
            _gold.Confirm = v => nsw.DropGold(v);
            _gold.Closed = () => _items.Toggle();
        }
        AddChild(_items);
        _hudSkin.MenuPressed += i =>
        {
            // 左边的窗口（状态/技能/宠物/名片/图鉴/系统）都在左上角同一个位置，右边的（地图/物品）都贴右上：
            // 打开一个就把同一边的其它窗口关掉，不叠在一起（手机上叠着根本看不清）
            bool right = i is 0 or 3;
            var leftWins = new CanvasLayer?[] { _status, _skills, _pets, _petDetail, _cardBook, _mailEdit, _mailRecord, _album, _system };
            var rightWins = new CanvasLayer?[] { _mapWin, _items, _gold };
            var mine = i switch { 0 => _mapWin, 1 => _status, 2 => _skills, 3 => (CanvasLayer)_items, 4 => _pets, 5 => _cardBook, 6 => _album, 7 => _system, _ => null };
            foreach (var w in right ? rightWins : leftWins) if (w != null && w != mine && !(i == 4 && w == _petDetail)) w.Visible = false;
            if (!right) _prod?.CloseAll();
            switch (i)
            {
                case 0: _mapWin.Toggle(); break;
                case 1: _status.Toggle(); break;
                case 2: _skills.Toggle(); break;
                case 3: if (_session is NetworkSession) _items.Toggle(); else _bag.Toggle(); break;
                case 4: if (_petDetail.Visible) _petDetail.Visible = false; else _pets.Toggle(); break;
                case 5: if (_session is NetworkSession) { _cardBook.Toggle(); ClearMail(); } else _toast.Show("单机模式没有名片", 2); break;
                case 6: if (_session is NetworkSession) _album.Toggle(); else _toast.Show("单机模式还没有图鉴", 2); break;
                case 7: _system.Toggle(); break;
                default: _toast.Show("这个窗口还没做", 2); break;
            }
        };
        _toast = new Toast();
        AddChild(_toast);
        _trans = new Transition();
        AddChild(_trans);
        _chat = new ChatLog();
        AddChild(_chat);
        _chatIn = new ChatInput { ShowButton = _cfg.MobileUi, Send = t => { if (_session is NetworkSession ns) ns.Say(t); else _chat.Add($"{_session.Player.Name}: {t}", 5); } };
        AddChild(_chatIn);
        _session.ChatReceived += (t, c) => _chat.Add(t, c);
        _calib = new CalibrationPanel { Cache = _cache, Cfg = _cfg, Notify = m => _toast.Show(m, 2) };
        AddChild(_calib);

        if (_session is NetworkSession nsc && _cfg.CharSelect)
        {
            _charSel = new CharSelectScreen { Cache = _cache, Session = nsc, Visible = false };
            _charSel.Back = () => { nsc.Logout(); _charSel.Visible = false; AddChild(new SetupScreen { Layer = 50 }); };
            AddChild(_charSel);
        }
        _session.Start();
    }
    private CharSelectScreen? _charSel;
    public CharSelectScreen? CharSelectUi => _charSel;

    private void AutoSave()
    {
        try { _session.Save(_savePath); } catch (Exception e) { GD.PushWarning($"存档失败：{e.Message}"); }
    }

    private void OnRewards(Rewards r)
    {
        var parts = new List<string> { $"获得经验 {r.Exp}" };
        if (r.LevelsGained > 0) parts.Add($"升到 Lv{r.NewLevel}！");
        foreach (var (id, n) in r.Items) parts.Add($"获得 {_session.ItemInfo(id)?.Name ?? $"道具#{id}"}×{n}");
        if (r.ItemsLostBagFull.Count > 0) parts.Add($"背包满了，{r.ItemsLostBagFull.Count} 件道具没拿到");
        _toast.Show(string.Join("\n", parts));
        _bag.Refresh();
    }

    private void OnMapEntered(MapInfo info, int x, int y)
    {
        if (_reconnectTry > 0) { _toast?.Show("重新连上了", 2); GameLog.Write("网络", "重连成功"); }
        _reconnectTry = 0;
        _pendingSteps.Clear();
        _talkTarget = null;
        _player.GetParent()?.RemoveChild(_player);
        _view?.QueueFree();
        _npcNodes.Clear();
        MapData map;
        if (_session is NetworkSession nmap && nmap.Map != null) map = nmap.Map;   // 联网：会话里那张（缓存/空图 + 服务端 aBS 补）
        else
        {
            if (string.IsNullOrEmpty(info.MapFile) || !GameFiles.Exists(info.MapFile)) { _toast.Show($"本地没有地图 {info.MapId} 的文件", 5); return; }
            map = MapData.Load(info.MapFile, info.MapId, info.Name);
        }
        _walkDirty = null;
        // 调色板：地图指定的优先（室内多为 0），否则随游戏时间变化
        _mapPalette = map.Palette;
        _cache.PaletteNo = CurrentPalette();
        _view = new MapView();
        _view.Build(map, Walkability.Build(map, _tiles), _cache);
        _audio?.OnMap(map);
        _audio?.UpdateEnv(x, y);
        AddChild(_view);
        MoveChild(_view, 0);
        _view.TileClicked += OnTileClicked;
        _view.TileHeld += cell => { if (!_session.InDialog && !_session.InBattle && !_battleUi.Visible && _talkTarget == null && cell != _walkTarget) WalkTo(cell); };
        SpawnNpcs();
        _groundNodes.Clear(); SpawnGround();
        _view.Entities.AddChild(_player);
        _player.Position = _from = _to = Iso.ToScreen(x, y);
        _t = 1;
        _mapWin?.Visit(map, x, y);
        _cam.MakeCurrent();
        _astar = new AStarGrid2D { Region = new Rect2I(0, 0, map.Width, map.Height), DiagonalMode = AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles };
        _astar.Update();
        for (int yy = 0; yy < map.Height; yy++)
            for (int xx = 0; xx < map.Width; xx++)
                _astar.SetPointSolid(new Vector2I(xx, yy), !_view.Walk.IsWalkable(xx, yy));
        AutoSave();
    }

    private void ApplySkinLayout(Button bag, Button cal)
    {
        if (!_hudSkin.Ok) return;
        bag.Visible = false;                         // 用官方右上角的按钮
        cal.Visible = false;                         // 校准工具藏起来，F2 打开（官方界面没有这个按钮）
        _hud.Visible = false;                        // 调试信息默认隐藏，F3 显示
        _hud.Position = new Vector2(8, 70);
    }

    private readonly List<Node2D> _npcNodes = new();

    /// <summary>（重新）生成当前地图上的 NPC/其他玩家精灵。联网模式下对象列表会随封包更新。</summary>
    // ———— 地上的道具 / 钱 ————
    private Vector2I? _pickTarget;
    private readonly List<Node2D> _groundNodes = new();
    /// <summary>钱按金额换图（官方 exe 0x453ade 起）：≥1000000 27409、≥500000 27408、≥100000 27407、≥50000 27406、≥10000 27405、
    /// ≥5000 27404、≥1000 27403、≥100 27402、≥50 27401、其余 27400（一枚金币）。</summary>
    public static int GoldImage(long amount) => amount switch
    {
        >= 1000000 => 27409, >= 500000 => 27408, >= 100000 => 27407, >= 50000 => 27406, >= 10000 => 27405,
        >= 5000 => 27404, >= 1000 => 27403, >= 100 => 27402, >= 50 => 27401, _ => 27400,
    };
    public int GroundShown => _groundNodes.Count;

    private void SpawnGround()
    {
        foreach (var n in _groundNodes) if (IsInstanceValid(n)) n.QueueFree();
        _groundNodes.Clear();
        if (_view == null || _session is not NetworkSession ns) return;
        foreach (var g in ns.GroundItems)
        {
            if (g.Pet)   // 扔在地上的宠物：画它的动画、头上名字
            {
                var ps = new AnimSprite { Cache = _cache, AnimeId = g.Image, Dir = ((g.Dir % 8) + 8) % 8, Act = _cfg.ActStand, FallbackAct = _cfg.ActStand, Position = Iso.ToScreen(g.X, g.Y) };
                ps.AddChild(NameTag.Make(g.Name, NameTag.NpcColor, -92));
                _view.Entities.AddChild(ps); _groundNodes.Add(ps);
                continue;
            }
            int img = g.Gold ? GoldImage(g.Count) : g.Image;
            if (_cache.ImageNo(img) is not (ImageTexture tex, var e) || e == null) continue;
            var node = new Sprite2D { Texture = tex, Centered = false, Position = Iso.ToScreen(g.X, g.Y), Offset = new Vector2(e.OffsetX, e.OffsetY) };
            _view.Entities.AddChild(node);
            _groundNodes.Add(node);
        }
    }

    private void SpawnNpcs()
    {
        if (_view == null) return;
        foreach (var n in _npcNodes) if (IsInstanceValid(n)) n.QueueFree();
        _npcNodes.Clear();
        _npcAt.Clear();
        _npcById.Clear();
        foreach (var n in _session.NpcsOnCurrentMap())
        {
            _npcAt[new Vector2I(n.X, n.Y)] = n;
            Node2D? node = null;
            // NPC 外观约 30% 是动画编号、70% 是静态图像编号（用真实数据统计），两种都要支持
            // 外观编号 ≥100000 是动画编号，否则是全局图像编号（GraphicInfo 的"地图编号"字段）
            if (n.AppearanceId >= 100000 && _assets.AnimeActions(n.AppearanceId).Count > 0)
                node = new AnimSprite { Cache = _cache, AnimeId = n.AppearanceId, Dir = ((n.Dir % 8) + 8) % 8, Act = _cfg.ActStand, FallbackAct = _cfg.ActStand, Position = Iso.ToScreen(n.X, n.Y) };
            else if (_cache.ImageNo(n.AppearanceId) is (ImageTexture tex, var e) && e != null)
                node = new Sprite2D { Texture = tex, Centered = false, Position = Iso.ToScreen(n.X, n.Y), Offset = new Vector2(e.OffsetX, e.OffsetY) };
            if (node == null) continue;
            // 静态图像的名字放在图像上方，动画角色用固定高度
            float top = node is Sprite2D { Texture: not null } sp && node is not AnimSprite ? sp.Offset.Y - 14 : -92;
            bool isPlayer = _session is NetworkSession nsp && nsp.IsPlayerObj(n.Id);
            if (isPlayer && _cfg.HidePlayers) node.Visible = false;   // 系统 → 绘图设定 → 隐藏画面角色
            if (!string.IsNullOrEmpty(n.Name) && (isPlayer || _cfg.ShowNpcNames)) node.AddChild(NameTag.Make(n.Name, NameTag.NpcColor, top));
            // 战斗中：头上 Battle 标志（官方是动画，素材里没找到；用对战按钮的剑 22581 + 橙字 Battle 代替）
            if (_session is NetworkSession nsf && nsf.IsFighting(n.Id))
            {
                if (_cache.ImageNo(22581).Tex is { } sw) node.AddChild(new Sprite2D { Texture = sw, Centered = false, Position = new Vector2(-14, top - 30), ZIndex = 50 });
                node.AddChild(NameTag.Make("Battle", new Color(1f, 0.45f, 0.1f), top + 12));
            }
            // 摆摊中：头上"店铺 贩售中"牌子 98895（名字上方），名字下面橙字"玩家店铺"（官方实测）
            if (_session is NetworkSession nss && nss.IsStall(n.Id))
            {
                if (_cache.ImageNo(98895).Tex is { } sign) node.AddChild(new Sprite2D { Texture = sign, Centered = false, Position = new Vector2(-24, top - 51), ZIndex = 50 });
                node.AddChild(NameTag.Make("玩家店铺", new Color(1f, 0.6f, 0.1f), top + 18));
            }
            _view.Entities.AddChild(node);
            _npcNodes.Add(node);
            _npcById[n.Id] = (node, n);
        }
    }

    private readonly Dictionary<int, (Node2D Node, Npc Npc)> _npcById = new();
    private StallWindow _stall = null!;
    private EmoteWindow? _emote;
    private BattleResultWindow _result = null!;
    private bool _resultPending;
    private FunctionPanel _func = null!;
    private Weather? _weather;
    private Rect2I? _walkDirty; private double _walkT;
    public Weather? WeatherFx => _weather;
    public FunctionPanel FuncUi => _func;
    public BattleResultWindow ResultUi => _result;
    public EmoteWindow? EmoteUi => _emote;
    public AnimSprite? SpriteOf(int id) => _npcById.TryGetValue(id, out var e) ? e.Node as AnimSprite : null;

    /// <summary>「動作」按钮 / Ctrl+F：动作窗口（贴右，跟地图、物品同一侧）。</summary>
    public void ToggleEmote()
    {
        if (_session is not NetworkSession ns || _session.InBattle) return;
        if (_emote == null)
        {
            _emote = new EmoteWindow { Cache = _cache };
            AddChild(_emote);
            _emote.Chosen = code => { ns.Emote(code); PlayEmote(_player, code); };
        }
        _emote.Toggle();
    }

    /// <summary>
    /// 播动作（自己点的、或别人 NI 来的）：30 走路 / 3 跑 一直播；31 站立；10 倒下、11 坐下 停在最后一帧；其他播一次回站立。走路时恢复。
    /// </summary>
    private void PlayEmote(AnimSprite a, int code)
    {
        a.ResetLoop();
        switch (code)
        {
            case 31: a.Act = _cfg.ActStand; break;
            case 30: a.Act = _cfg.ActWalk; break;
            case 3: a.Act = 3; break;
            case 10 or 11: a.PlayOnce(code, hold: true); break;
            default: if (code is > 0 and <= 20) a.PlayOnce(code); break;
        }
    }

    private void OnObjectActed(int id, int code)
    {
        if (_npcById.TryGetValue(id, out var e) && IsInstanceValid(e.Node) && e.Node is AnimSprite a) PlayEmote(a, code);
    }
    public StallWindow StallUi => _stall;

    /// <summary>右上第 6 个按钮 / Ctrl+B：打开自己的摆摊窗口（再按关掉）。</summary>
    public void ToggleStall()
    {
        if (_session is not NetworkSession || _session.InBattle) return;
        if (_stall.Visible) _stall.Close(); else _stall.OpenOwn();
    }

    /// <summary>别的玩家/NPC 走了一步：只挪这一个，播走路动作，走完换回站立。</summary>
    private void OnObjectMoved(Npc n)
    {
        if (!_npcById.TryGetValue(n.Id, out var e) || !IsInstanceValid(e.Node)) return;
        var old = e.Npc;
        if (_npcAt.TryGetValue(new Vector2I(old.X, old.Y), out var at) && at.Id == n.Id) _npcAt.Remove(new Vector2I(old.X, old.Y));
        _npcAt[new Vector2I(n.X, n.Y)] = n;
        _npcById[n.Id] = (e.Node, n);
        var to = Iso.ToScreen(n.X, n.Y);
        if (e.Node is AnimSprite a)
        {
            a.ResetLoop();
            a.Dir = ((n.Dir % 8) + 8) % 8;
            a.Act = _cfg.ActWalk;
        }
        var tw = e.Node.CreateTween();
        tw.TweenProperty(e.Node, "position", to, StepSeconds);
        tw.TweenCallback(Callable.From(() =>
        {
            if (e.Node is AnimSprite s && IsInstanceValid(s) && s.Position == Iso.ToScreen(_npcById[n.Id].Npc.X, _npcById[n.Id].Npc.Y)) s.Act = _cfg.ActStand;
        }));
    }

    private void OnMoved(int x, int y, int dir)
    {
        _audio?.UpdateEnv(x, y);
        _from = _player.Position;
        _to = Iso.ToScreen(x, y);
        _t = 0;
        // 跟着队长走时位置由服务端/会话推过来，朝向按位移算
        var dv = _to - _from;
        if (dv.LengthSquared() > 1) { var c = Iso.ToCell(_to) - Iso.ToCell(_from); if (c != Vector2I.Zero) _player.Dir = Iso.AnimeDirFor(Math.Sign(c.X), Math.Sign(c.Y), _cfg.DirOffset); }
        if (_view?.Map != null) _mapWin?.Visit(_view.Map, x, y);
    }

    /// <summary>测试用：模拟点地图上的一格。</summary>
    public void TestTileClick(int x, int y) => OnTileClicked(new Vector2I(x, y));

    private void OnTileClicked(Vector2I cell)
    {
        if (_session.InDialog || _session.InBattle || _battleUi.Visible) return;
        if (_session is NetworkSession nsd)
        {
            // 手上拿着物品窗口里的道具时点地图 = 扔掉（官方 DI，服务端放在身边空地上）
            if (_items.Visible && _items.Held >= 8) { nsd.DropItem(_items.TakeHeld()); return; }
            // 点地上的道具/钱：在旁边就捡，远就先走过去（走到旁边自动捡）
            if (nsd.GroundItems.FirstOrDefault(g => g.X == cell.X && g.Y == cell.Y) is { } gi)
            {
                _talkTarget = null;
                if (!nsd.PickUp(gi.X, gi.Y)) { _pickTarget = cell; WalkTo(cell); }
                return;
            }
        }
        _pickTarget = null;
        // 点到 NPC（或其旁边一格）：近就直接对话，远就先走过去
        var npc = _npcAt.TryGetValue(cell, out var hit) ? hit : null;
        if (npc != null) { TalkOrApproach(npc); return; }
        _talkTarget = null;
        WalkTo(cell);
    }

    public void TalkOrApproach(Npc npc)
    {
        if (Math.Max(Math.Abs(npc.X - _session.X), Math.Abs(npc.Y - _session.Y)) <= 1) { BeginTalk(npc); return; }
        // 找 NPC 周围离玩家最近的可走格
        Vector2I? best = null; int bestD = int.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var c = new Vector2I(npc.X + dx, npc.Y + dy);
                if ((dx == 0 && dy == 0) || _view == null || !_view.Walk.IsWalkable(c.X, c.Y)) continue;
                int d = Math.Abs(c.X - _session.X) + Math.Abs(c.Y - _session.Y);
                if (d < bestD) { bestD = d; best = c; }
            }
        if (best == null) return;
        _talkTarget = npc;
        WalkTo(best.Value, keepTalkTarget: true);
    }

    private void BeginTalk(Npc npc)
    {
        _talkTarget = null;
        // 摆摊的人：官方右键（手机：点）他 = 浏览摊位（ElVN）
        if (_session is NetworkSession nsb && nsb.IsStall(npc.Id)) { nsb.StallBrowse(npc.Id); return; }
        _talkingTo = npc.Name;
        _session.TalkTo(npc.Id);
    }

    /// <summary>点击目标：本地算路径，然后一格一格请求服务端（服务端校验每一步）。</summary>
    private Vector2I _walkTarget = new(-1, -1);
    public void WalkTo(Vector2I target, bool keepTalkTarget = false)
    {
        if (_session.InBattle || _session.InDialog || _view == null || !_view.Map.InBounds(target.X, target.Y)) return;
        if (!keepTalkTarget) _talkTarget = null;
        _player.ResetLoop();   // 坐下 / 倒下（動作）走路就恢复
        _prod?.Interrupt();   // 官方：走路就停止采集 / 制造
        _walkTarget = target;
        _pendingSteps.Clear();
        // 传送点所在格可能本身不可走，允许把它作为终点
        bool wasSolid = _astar.IsPointSolid(target);
        if (wasSolid) _astar.SetPointSolid(target, false);
        var path = _astar.GetIdPath(new Vector2I(_session.X, _session.Y), target);
        if (wasSolid) _astar.SetPointSolid(target, true);
        // 走不到（点在物件、墙里面）：改走到离它最近、走得到的格子，不要没反应
        if (path.Count == 0 && _view.Walk != null)
        {
            Vector2I? near = null; int bd = int.MaxValue;
            for (int r = 1; r <= 3 && near == null; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        var c = new Vector2I(target.X + dx, target.Y + dy);
                        if (!_view.Map.InBounds(c.X, c.Y) || !_view.Walk.IsWalkable(c.X, c.Y)) continue;
                        var p2 = _astar.GetIdPath(new Vector2I(_session.X, _session.Y), c);
                        if (p2.Count > 0 && p2.Count < bd) { bd = p2.Count; near = c; path = p2; }
                    }
        }
        for (int i = 1; i < path.Count; i++)
        {
            var d = path[i] - path[i - 1];
            int dir = Array.IndexOf(Directions.Deltas, (d.X, d.Y));
            if (dir >= 0) _pendingSteps.Enqueue(dir);
        }
    }

    public override void _Process(double delta)
    {
        UpdateCovered();
        if (_weather != null) _weather.Visible = !_battleUi.Visible;
        if (_walkDirty is { } wd && _view != null && (_walkT += delta) > 0.3)
        {
            // 服务端补了地图：重算通行（物件会占邻格，整张重算），寻路网格只更新补过的范围（外扩 4 格）
            _walkT = 0; _walkDirty = null;
            var wm = _view.Map;
            _view.Walk = Walkability.Build(wm, _tiles);
            var rr = wd.Grow(4).Intersection(new Rect2I(0, 0, wm.Width, wm.Height));
            for (int yy = rr.Position.Y; yy < rr.End.Y; yy++)
                for (int xx = rr.Position.X; xx < rr.End.X; xx++)
                    _astar.SetPointSolid(new Vector2I(xx, yy), !_view.Walk.IsWalkable(xx, yy));
        }
        if ((_palCheck += delta) > 5 && _cache != null && _session?.CurrentMap != null)
        {
            _palCheck = 0;
            int pal = CurrentPalette();
            if (pal != _cache.PaletteNo) _cache.PaletteNo = pal;
        }
        if (_session == null) return;
        _session.Poll();
        if (_playerTag != null && _playerTag.Text != _session.Player.Name) _playerTag.Text = _session.Player.Name;
        if (_t < 1)
        {
            _t = Math.Min(1, _t + delta / StepSeconds);
            _player.Position = _from.Lerp(_to, (float)_t);
        }
        bool moving = _t < 1;
        if (_pickTarget is Vector2I pt && _session is NetworkSession nsp && Math.Max(Math.Abs(pt.X - nsp.X), Math.Abs(pt.Y - nsp.Y)) <= 1 && !moving)
        {
            _pickTarget = null; _pendingSteps.Clear(); nsp.PickUp(pt.X, pt.Y);
        }
        if (!moving && _pendingSteps.Count > 0 && !_session.InBattle && !_session.InDialog && !_battleUi.Visible)
        {
            int dir = _pendingSteps.Dequeue();
            var (dx, dy) = Directions.Deltas[dir];
            _player.Dir = Iso.AnimeDirFor(dx, dy, _cfg.DirOffset);
            var r = _session.Step(dir);
            if (r == MoveResult.Blocked || r == MoveResult.InvalidStep) { _pendingSteps.Clear(); _talkTarget = null; }
            moving = r == MoveResult.Ok;
        }
        if (!moving && _pendingSteps.Count == 0 && _talkTarget != null && !_session.InBattle) BeginTalk(_talkTarget);
        _player.Act = moving ? _cfg.ActWalk : _cfg.ActStand;
        if (!_player.Visible && !_battleUi.Visible) { _player.Visible = true; _hudSkin.SetBattle(false); }
        if (_resultPending && !_battleUi.Visible && _session is NetworkSession nsr2 && nsr2.LastResult != null) { _resultPending = false; _result.Show(nsr2.LastResult); }
        var m = _session.CurrentMap; var p = _session.Player;
        _hud.Text = $"{(_cfg.UsingTestData ? "[测试素材] " : "")}{m?.Name} (#{m?.MapId})  ({_session.X},{_session.Y})    Lv{p.Level}  经验 {p.Exp}  金钱 {p.Gold}    FPS {Engine.GetFramesPerSecond()}\n" +
                    "点地面行走 · 点 NPC 对话 · 野外会遇敌 · 滚轮缩放 · 「动作」校准动作号";
    }

    private bool _rightCovered, _leftCovered;
    private void UpdateCovered()
    {
        bool c = (_mapWin?.Visible ?? false) || (_items?.Visible ?? false) || (_gold?.Visible ?? false) || (_emote?.Visible ?? false) || (_prod?.ItemSkill.Visible ?? false) || (_prod?.Materials.Visible ?? false);
        if (c != _rightCovered) { _rightCovered = c; _hudSkin?.SetRightCovered(c); }
        if (_func != null) _func.Visible = !c && !_session.InBattle && _session is NetworkSession;
        bool l = (_status?.Visible ?? false) || (_skills?.Visible ?? false) || (_system?.Visible ?? false)
              || (_pets?.Visible ?? false) || (_petDetail?.Visible ?? false)
              || (_cardBook?.Visible ?? false) || (_album?.Visible ?? false) || (_prod?.AnyVisible ?? false) || (_mailEdit?.Visible ?? false) || (_mailRecord?.Visible ?? false);
        if (l != _leftCovered) { _leftCovered = l; _hudSkin?.SetLeftCovered(l); }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_session == null) return;
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) _cam.Zoom = (_cam.Zoom * 1.1f).Clamp(Vector2.One * 0.3f, Vector2.One * 4f);
            else if (mb.ButtonIndex == MouseButton.WheelDown) _cam.Zoom = (_cam.Zoom / 1.1f).Clamp(Vector2.One * 0.3f, Vector2.One * 4f);
        }
        else if (e is InputEventMagnifyGesture mg) _cam.Zoom = (_cam.Zoom * mg.Factor).Clamp(Vector2.One * 0.3f, Vector2.One * 4f);
        else if (e is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.L } && _session is NetworkSession nsl) nsl.PartyButton();   // 官方 Ctrl+L 组队
        else if (e is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.Semicolon } && _session is NetworkSession nsc) nsc.ExchangeCard();   // 官方 Ctrl+; 交换名片
        else if (e is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.Apostrophe } && _session is NetworkSession nst) nst.RequestTrade();   // 官方 Ctrl+' 交易
        else if (e is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.J } && _session is NetworkSession nsj) nsj.Duel();    // 官方 Ctrl+J 对战
        else if (e is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.K } && _session is NetworkSession nsk) nsk.Watch();   // 官方 Ctrl+K 观战
        else if (e is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.B }) ToggleStall();   // 官方 Ctrl+B 摆摊
        else if (e is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.F }) ToggleEmote();   // 官方 Ctrl+F 情绪动作
        else if (e is InputEventKey ck && _chatIn != null && _chatIn.TryStart(ck)) GetViewport().SetInputAsHandled();   // 直接打字 = 聊天（官方同）
        else if (e is InputEventKey { Pressed: true, Keycode: Key.F2 }) _calib?.Toggle();
        else if (e is InputEventKey { Pressed: true, Keycode: Key.F10 }) AddChild(new SetupScreen { Layer = 50 });   // 重新设置（切换单机/联网、改服务器）
        else if (e is InputEventKey { Pressed: true, Keycode: Key.F3 }) _hud.Visible = !_hud.Visible;
    }

    private int _mapPalette = 0;
    private double _palCheck;
    /// <summary>当前应使用的调色板：地图指定的（室内）优先，否则按游戏内时间（配置 time_palette 可固定）。</summary>
    // ———————— 断线重连 ————————
    // 手机上切到后台、换 Wi-Fi/流量、锁屏都会断。断了就自动重连：1、2、4、8、15、15 秒后各试一次，都不行就问要不要再试。
    // 重连成功后服务端照常发进图，地图、背包、宠物都会重新来一遍。
    private int _reconnectTry;
    private SceneTreeTimer? _reconnectTimer;
    private static readonly int[] ReconnectDelays = { 1, 2, 4, 8, 15, 15 };
    public bool Reconnecting => _reconnectTimer != null;

    private void OnDisconnected(string why)
    {
        if (_session is not NetworkSession ns) return;
        _battleUi?.Abort(); _audio?.BattleEnd();
        _pendingSteps.Clear(); _talkTarget = null;
        if (_reconnectTry >= ReconnectDelays.Length)
        {
            _reconnectTry = 0; _reconnectTimer = null;
            GameLog.Write("网络", "重连失败，问玩家");
            _confirm?.Ask($"与服务器的连接中断了（{why}）。\n要重新连接吗？", () => OnDisconnected(why));
            return;
        }
        int delay = ReconnectDelays[_reconnectTry++];
        _toast?.Show($"连接中断，{delay} 秒后重连（第 {_reconnectTry} 次）…", delay + 1);
        GameLog.Write("网络", $"断线：{why}；{delay} 秒后第 {_reconnectTry} 次重连");
        var t = _reconnectTimer = GetTree().CreateTimer(delay);
        t.Timeout += () => { if (_reconnectTimer != t) return; _reconnectTimer = null; ns.Reconnect(); };
    }

    public override void _Notification(int what)
    {
        // 从后台回来：连接已经断了但还没轮到重连，就马上连
        if (what == NotificationApplicationResumed && _session is NetworkSession ns && !ns.Connected && _reconnectTimer != null)
        {
            _reconnectTimer = null; GameLog.Write("网络", "回到前台，马上重连"); ns.Reconnect();
        }
    }

    private int CurrentPalette()
    {
        if (_mapPalette >= 0) return _mapPalette;
        if (_cfg.TimePalette >= 0) return _cfg.TimePalette;
        long off = _session is NetworkSession ns ? ns.ServerTimeOffset : 0;
        return CgClock.Palette(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + off);
    }

    public override void _ExitTree() { if (_session != null) AutoSave(); _session?.Dispose(); _assets?.Dispose(); }
}
