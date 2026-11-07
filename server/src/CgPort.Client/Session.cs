using CgPort.Core;
using CgPort.Server;

namespace CgPort.Client;

/// <summary>客户端看到的地图信息。MapFile 是可以直接用 <see cref="MapData.Load"/> 读取的地图文件路径。</summary>
public sealed record MapInfo(int MapId, string Name, string MapFile);

/// <summary>战斗单位的显示信息（客户端只需要这些，不需要服务端内部的能力值）。</summary>
public sealed record UnitView(int Id, int Side, int Level, int Hp, int MaxHp, int AnimeId, bool Alive, bool Escaped, string Name = "");

public sealed record BattleView(int Turn, IReadOnlyList<UnitView> Units, int PlayerUnitId);

/// <summary>
/// 客户端与服务端之间的会话接口。
///  - 单机：<see cref="LocalSession"/> 在进程内直接调用服务端代码；
///  - 联网：以后实现 NetworkSession，内部走 cgmsv 协议（拿到封包日志后实现）。
/// 客户端只依赖这个接口，两种模式的上层代码完全相同。
/// </summary>
public interface IGameSession : IDisposable
{
    event Action<MapInfo, int, int>? MapEntered;                          // 进入地图（含传送），参数：地图、x、y
    event Action<int, int, int>? Moved;                                   // 位置确认：x、y、方向
    event Action<BattleView>? BattleStarted;
    event Action<IReadOnlyList<BattleEvent>, BattleView>? TurnResolved;
    event Action<BattleOutcome>? BattleEnded;
    event Action<Rewards>? RewardsGained;                                 // 战斗奖励（经验、升级、掉落）
    event Action<DialogPage?>? DialogChanged;                             // NPC 对话：新的一页；null = 对话结束
    event Action? StatusChanged;                                          // 等级/经验/金钱/背包有变化
    /// <summary>周围对象（NPC/其他玩家）有变化。单机模式里 NPC 固定，不会触发。</summary>
    event Action? ObjectsChanged { add { } remove { } }
    /// <summary>聊天栏消息：文字、颜色编号（0 白、1 青…，同服务端 TK 包）。路人 NPC 说的话也走这里。</summary>
    event Action<string, int>? ChatReceived { add { } remove { } }

    MapInfo? CurrentMap { get; }
    PlayerState Player { get; }
    bool InDialog { get; }
    int X { get; }
    int Y { get; }
    bool InBattle { get; }

    void Start();
    /// <summary>请求往某个方向走一格（0–7，见 <see cref="Directions"/>）。服务端校验后通过 Moved / MapEntered 回报。</summary>
    MoveResult Step(int dir);
    /// <summary>战斗中提交本回合指令。</summary>
    void SubmitCommand(Command command);
    IReadOnlyList<Npc> NpcsOnCurrentMap();
    /// <summary>和 NPC 对话（需要站在 2 格以内）。返回是否开始了对话。</summary>
    bool TalkTo(int npcId);
    /// <summary>在对话框里按按钮：next / yes / no / ok。</summary>
    void Answer(string button);
    /// <summary>道具信息（名称、图像编号），客户端显示背包用。</summary>
    Item? ItemInfo(int itemId);
    void Save(string path);
    /// <summary>每帧调用：联网会话在这里处理收到的封包；单机会话什么都不做。</summary>
    void Poll() { }
}

public sealed class SessionOptions
{
    public int StartMap { get; set; } = 1000;
    public int? StartX { get; set; }
    public int? StartY { get; set; }
    public int PlayerLevel { get; set; } = 10;
    /// <summary>每走一格的遇敌概率 = 遇敌区域的遇敌率参数 / 100 × 这个系数（遇敌率参数的真实含义待核对）。</summary>
    public double EncounterScale { get; set; } = 0.5;
    /// <summary>战斗结束后至少走多少步才会再次遇敌。</summary>
    public int EncounterGraceSteps { get; set; } = 5;
    public bool EncountersEnabled { get; set; } = true;
    public int Seed { get; set; } = Environment.TickCount;
    /// <summary>bin 目录下要加载的 GraphicInfo 文件（相对路径）；为空时用默认列表（按 cgmsv.yml 顺序）。</summary>
    public IReadOnlyList<string>? GraphicInfoFiles { get; set; }
}

/// <summary>单机会话：在客户端进程里直接运行服务端逻辑。</summary>
public sealed class LocalSession : IGameSession
{
    public event Action<MapInfo, int, int>? MapEntered;
    public event Action<int, int, int>? Moved;
    public event Action<BattleView>? BattleStarted;
    public event Action<IReadOnlyList<BattleEvent>, BattleView>? TurnResolved;
    public event Action<BattleOutcome>? BattleEnded;
    public event Action<Rewards>? RewardsGained;
    public event Action<DialogPage?>? DialogChanged;
    public event Action? StatusChanged;

    public World World { get; }
    public PlayerState Player { get; private set; } = new();
    public bool InDialog => _dialog is { Finished: false } || _wm != null;
    public event Action<string, int>? ChatReceived;
    private (WindowmanScript Script, string Label, string Npc)? _wm;
    private readonly Dictionary<string, WindowmanScript?> _wmScripts = new();

    /// <summary>msg.txt 的文字，文件里的 \n（两个字符）换成换行。</summary>
    private string MsgText(int id) => GameTables.Messages.TryGetValue(id, out var t) ? t.Replace("\\n", "\n") : "";

    private DialogPage WindowmanPage()
    {
        var (sc, label, name) = _wm!.Value;
        var p = sc.Get(label)!;
        var (type, mask) = WindowmanScript.Shape(p);
        return WindowText.Build(type, mask, MsgText(p.MsgId)) with { NpcName = name, IsLast = false };
    }

    private WindowmanScript? LoadWindowman(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (_wmScripts.TryGetValue(file, out var s)) return s;
        var p = Path.Combine(_npcDir, file);
        if (!GameFiles.Exists(p)) p += ".txt";
        s = GameFiles.Exists(p) ? WindowmanScript.Load(p) : null;
        _wmScripts[file] = s;
        return s;
    }
    public ExpTable Exp { get; }
    private readonly Progression _progression;
    private readonly string _npcDir;
    private ScriptRun? _dialog;
    private readonly Dictionary<string, NpcScript?> _scripts = new();
    public List<string> ScriptLog { get; } = new();
    public BattleTables BattleTables { get; }
    public GameTables GameTables { get; }
    public MapInfo? CurrentMap { get; private set; }
    public int X => _me.X;
    public int Y => _me.Y;
    public bool InBattle => _battle != null;

    private readonly SessionOptions _opt;
    private readonly EncounterService _encounters;
    private readonly Random _rng;
    private Character _me = null!;
    private Battle? _battle;
    private int _playerUnitId;
    private int _grace;
    private readonly string _mapDir;

    public LocalSession(string gameRoot, string serverDir, SessionOptions? options = null)
    {
        _opt = options ?? new SessionOptions();
        _rng = new Random(_opt.Seed);
        var data = Path.Combine(gameRoot, serverDir, "data");
        _mapDir = Path.Combine(data, "map");
        TileInfoTable tiles;
        if (_opt.GraphicInfoFiles is { Count: > 0 } files)
        {
            tiles = new TileInfoTable();
            foreach (var f in files) { var p = Path.Combine(gameRoot, "bin", f); if (GameFiles.Exists(p)) tiles.LoadGraphicInfo(p); }
        }
        else tiles = TileInfoTable.LoadDefault(Path.Combine(gameRoot, "bin"));
        World = new World(new MapManager(_mapDir, tiles), WarpTable.Load(Path.Combine(data, "warp.txt")));
        BattleTables = GameFiles.Exists(Path.Combine(data, "encount.txt")) ? BattleTables.Load(data) : new BattleTables();
        GameTables = GameTables.Load(data);
        _encounters = new EncounterService(BattleTables);
        Exp = ExpTable.Load(data);
        _progression = new Progression(Exp, BattleTables);
        _npcDir = Path.Combine(data, "npc");
    }

    /// <summary>用于测试或自定义：直接给出已加载的组件。</summary>
    public LocalSession(World world, BattleTables battle, GameTables game, string mapDir, SessionOptions? options = null)
    {
        _opt = options ?? new SessionOptions();
        _rng = new Random(_opt.Seed);
        World = world; BattleTables = battle; GameTables = game; _mapDir = mapDir;
        _encounters = new EncounterService(BattleTables);
        var data = Path.GetDirectoryName(mapDir)!;
        Exp = ExpTable.Load(data);
        _progression = new Progression(Exp, BattleTables);
        _npcDir = Path.Combine(data, "npc");
    }

    /// <summary>读档：在 Start() 之前调用。</summary>
    public void LoadPlayer(PlayerState state) => Player = state;

    public void Start()
    {
        int map = World.Maps.Has(_opt.StartMap) ? _opt.StartMap : World.Maps.MapIds.Min();
        var (x, y) = _opt.StartX is int sx && _opt.StartY is int sy ? (sx, sy) : FindStart(map);
        if (Player.MapId != 0 && World.Maps.Has(Player.MapId)) { map = Player.MapId; x = Player.X; y = Player.Y; }   // 有存档时从存档位置开始
        else Player.Level = Math.Max(Player.Level, _opt.PlayerLevel);
        _me = World.Spawn(1, Player.Name, map, x, y);
        SyncPlayer();
        EnterMap();
    }

    private (int, int) FindStart(int map)
    {
        // 优先用一个传送落点（一定可走）；没有就找地图中心附近的可走格
        var w = World.Warps.All.FirstOrDefault(v => v.ToMap == map && World.Maps.Get(map).Walk.IsWalkable(v.ToX, v.ToY));
        if (w != default) return (w.ToX, w.ToY);
        var (m, walk) = World.Maps.Get(map);
        for (int r = 0; r < Math.Max(m.Width, m.Height); r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (walk.IsWalkable(m.Width / 2 + dx, m.Height / 2 + dy)) return (m.Width / 2 + dx, m.Height / 2 + dy);
        return (0, 0);
    }

    private void EnterMap()
    {
        var path = MapData.IndexServerMapDir(_mapDir)[_me.MapId].Path;
        CurrentMap = new MapInfo(_me.MapId, World.Maps.NameOf(_me.MapId), path);
        SyncPlayer();
        MapEntered?.Invoke(CurrentMap, _me.X, _me.Y);
    }

    public MoveResult Step(int dir)
    {
        if (InBattle || InDialog) return MoveResult.Blocked;
        var r = World.Step(_me, dir);
        if (r == MoveResult.Warped) { _grace = _opt.EncounterGraceSteps; EnterMap(); return r; }
        if (r != MoveResult.Ok) return r;
        SyncPlayer();
        Moved?.Invoke(_me.X, _me.Y, _me.Dir);
        MaybeEncounter();
        return r;
    }

    private void MaybeEncounter()
    {
        if (!_opt.EncountersEnabled) return;
        if (_grace > 0) { _grace--; return; }
        var area = BattleTables.EncountAt(_me.MapId, _me.X, _me.Y);
        if (area == null) return;
        double p = Math.Clamp(area.Rate, 1, 100) / 100.0 * _opt.EncounterScale;
        if (_rng.NextDouble() >= p) return;
        var enc = _encounters.Roll(_me.MapId, _me.X, _me.Y, _rng);
        if (enc == null) return;
        BeginBattle(enc);
    }

    private void BeginBattle(EncounterResult enc)
    {
        _battle = new Battle(new PlaceholderFormulas(), _rng);
        var st = new LevelScaledStats().ForEnemy(BattleTables.Enemies[enc.Enemies[0].EnemyId], null, Player.Level);
        st.MaxHp *= 3; st.Attack = st.Attack * 3 / 2;   // 占位：让玩家比同级敌人强一些
        _playerUnitId = 0;
        _battle.Units.Add(new Combatant { Id = _playerUnitId, Side = 0, Level = Player.Level, Stats = st, Hp = st.MaxHp, Mp = st.MaxMp });
        _battle.AddEnemies(enc, BattleTables, new LevelScaledStats());
        BattleStarted?.Invoke(View());
    }

    public void SubmitCommand(Command command)
    {
        if (_battle == null) return;
        int before = _battle.Log.Count;
        var outcome = _battle.RunTurn(new Dictionary<int, Command> { [_playerUnitId] = command });
        TurnResolved?.Invoke(_battle.Log.Skip(before).ToList(), View());
        if (outcome != BattleOutcome.Ongoing)
        {
            var defeated = _battle.Side(1).Where(u => u.Hp == 0 && !u.Escaped).ToList();
            _battle = null;
            _grace = _opt.EncounterGraceSteps;
            BattleEnded?.Invoke(outcome);
            if (outcome == BattleOutcome.Win)
            {
                var r = _progression.Grant(Player, defeated, _rng);
                RewardsGained?.Invoke(r);
                StatusChanged?.Invoke();
            }
        }
    }

    private BattleView View() => new(_battle!.Turn,
        _battle.Units.Select(u => new UnitView(u.Id, u.Side, u.Level, u.Hp, u.Stats.MaxHp, u.AnimeId, u.Alive, u.Escaped)).ToList(), _playerUnitId);

    public IReadOnlyList<Npc> NpcsOnCurrentMap() => CurrentMap == null ? Array.Empty<Npc>() : GameTables.NpcsOnMap(CurrentMap.MapId);

    private void SyncPlayer() { Player.MapId = _me.MapId; Player.X = _me.X; Player.Y = _me.Y; Player.Dir = _me.Dir; }

    public Item? ItemInfo(int itemId) => GameTables.Items.TryGetValue(itemId, out var it) ? it : null;

    public void Save(string path) { SyncPlayer(); Player.Save(path); }

    public bool TalkTo(int npcId)
    {
        if (InBattle || InDialog || !GameTables.Npcs.TryGetValue(npcId, out var npc) || CurrentMap == null || npc.MapId != CurrentMap.MapId) return false;
        if (Math.Max(Math.Abs(npc.X - _me.X), Math.Abs(npc.Y - _me.Y)) > 2) return false;
        switch (npc.Type)
        {
            case "TownPeople":
                // 路人：不弹窗口，从第 24 列的几个 msg 编号里随机挑一句，作为聊天消息"名字: 话"（实测服务端发 TK 包，颜色 0）
                var ids = npc.ScriptFile.Split('|').Select(x => int.TryParse(x.Trim(), out var v) ? v : 0).Where(GameTables.Messages.ContainsKey).ToList();
                if (ids.Count > 0) ChatReceived?.Invoke($"{npc.Name}: {MsgText(ids[_rng.Next(ids.Count)]).Replace('\n', ' ').Trim()}", 0);
                return true;
            case "Billboard":
                return true;   // 告示牌：实测服务端对"对话"不回任何东西
            case "Windowman":
                var wm = LoadWindowman(npc.ScriptFile);
                if (wm == null || wm.Get(wm.Start) == null) break;
                _wm = (wm, wm.Start, npc.Name);
                DialogChanged?.Invoke(WindowmanPage());
                return true;
        }
        var script = LoadScript(npc.ScriptFile);
        if (script == null) { DialogChanged?.Invoke(new DialogPage("……", new[] { "ok" }, true)); _dialog = null; return true; }
        _dialog = new ScriptRun(script, new Host(this), _rng);
        if (_dialog.Current == null) { DialogChanged?.Invoke(new DialogPage("……", new[] { "ok" }, true)); _dialog = null; return true; }
        DialogChanged?.Invoke(_dialog.Current);
        return true;
    }

    public void Answer(string button)
    {
        if (_wm is var (wmScript, label, _) && wmScript.Get(label) is { } wp)
        {
            var nextLabel = wmScript.Press(wp, button);
            if (nextLabel == null) { _wm = null; DialogChanged?.Invoke(null); }
            else { _wm = _wm.Value with { Label = nextLabel }; DialogChanged?.Invoke(WindowmanPage()); }
            return;
        }
        if (_dialog == null) { DialogChanged?.Invoke(null); return; }
        var last = _dialog.Current?.IsLast ?? true;
        if (last) { _dialog = null; DialogChanged?.Invoke(null); StatusChanged?.Invoke(); return; }
        _dialog.Press(button);
        var next = _dialog.Current;
        if (next == null) { _dialog = null; DialogChanged?.Invoke(null); StatusChanged?.Invoke(); }
        else DialogChanged?.Invoke(next);
        if (_pendingWarp is var (m, x, y)) { _pendingWarp = null; _dialog = null; DialogChanged?.Invoke(null); _me.MapId = m; _me.X = x; _me.Y = y; EnterMap(); }
        if (_pendingEncounter is int g) { _pendingEncounter = null; _dialog = null; DialogChanged?.Invoke(null); StartScriptedEncounter(g); }
    }

    private (int, int, int)? _pendingWarp;
    private int? _pendingEncounter;

    private void StartScriptedEncounter(int groupId)
    {
        if (!BattleTables.Groups.TryGetValue(groupId, out var g) || g.Slots.Count == 0) return;
        var list = g.Slots.Where(s => BattleTables.Enemies.ContainsKey(s.EnemyId)).Take(10).Select((s, i) =>
        {
            var en = BattleTables.Enemies[s.EnemyId];
            return new EnemyInstance(i, s.EnemyId, en.BaseId, _rng.Next(en.LevelMin, en.LevelMax + 1), BattleTables.Bases.TryGetValue(en.BaseId, out var b) ? b.AnimeId : 0);
        }).ToList();
        if (list.Count > 0) BeginBattle(new EncounterResult(0, groupId, list));
    }

    private NpcScript? LoadScript(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (_scripts.TryGetValue(file, out var s)) return s;
        var p = Path.Combine(_npcDir, file);
        if (!GameFiles.Exists(p)) p += ".txt";
        s = GameFiles.Exists(p) ? NpcScript.Load(p) : null;
        _scripts[file] = s;
        return s;
    }

    private sealed class Host : IScriptHost
    {
        private readonly LocalSession _s;
        public Host(LocalSession s) => _s = s;
        public PlayerState Player => _s.Player;
        public int PlayerX => _s._me.X;
        public int PlayerY => _s._me.Y;
        public void Warp(int map, int x, int y) { if (_s.World.Maps.Has(map)) _s._pendingWarp = (map, x, y); }
        public void StartEncounter(int groupId) => _s._pendingEncounter = groupId;
        public void Log(string m) => _s.ScriptLog.Add(m);
    }

    public void Dispose() { }
}
