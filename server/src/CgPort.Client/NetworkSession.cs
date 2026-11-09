using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using CgPort.Core;
using CgPort.Core.Text;
using CgPort.Server;

namespace CgPort.Client;

/// <summary>联网参数。</summary>
public sealed class NetworkOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9030;
    /// <summary>ClientLogin 的第一个字段。官方客户端发一个 32 位哈希；cgmsv 用它当账号主键。</summary>
    public string CdKey { get; set; } = "";
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>账号下没有角色时自动创建的角色名。</summary>
    public string NewCharName { get; set; } = "移植测试";
    /// <summary>楼层编号 → 本地地图文件（服务端 LS2MAP 或客户端缓存 MAP），找不到返回 null。</summary>
    public Func<int, string?> ResolveMap { get; set; } = _ => null;
    /// <summary>每个收发的封包都回调一次（调试/抓包用）："C xxx" / "S xxx"。</summary>
    public Action<string>? Trace { get; set; }
}

/// <summary>
/// 联网会话：用 cgmsv 文本协议连原版服务端（协议见 docs/PROTOCOL.md）。
/// 网络线程只负责收行，所有解析和事件都在调用 <see cref="Poll"/> 的线程（游戏主线程）里做。
/// 目前支持：登录/建角色/进游戏、进图与换图、行走（带令牌）、位置校正、周围对象、战斗（普攻/防御/逃跑）。
/// NPC 对话、道具使用等还没接。
/// </summary>
public sealed class NetworkSession : IGameSession
{
    public event Action<MapInfo, int, int>? MapEntered;
    public event Action<int, int, int>? Moved;
    public event Action<BattleView>? BattleStarted;
    public event Action<IReadOnlyList<BattleEvent>, BattleView>? TurnResolved;
    public event Action<BattleOutcome>? BattleEnded;
    public event Action<Rewards>? RewardsGained;
    public event Action<DialogPage?>? DialogChanged;
    public event Action? StatusChanged;
    public event Action? ObjectsChanged;
    /// <summary>周围某个对象（其他玩家、会走的 NPC）走了一步：参数是更新后的对象。</summary>
    public event Action<Npc>? ObjectMoved;
    /// <summary>别人做了个动作（坐下、挥手…）：对象编号、动作号。</summary>
    public event Action<int, int>? ObjectActed;
    /// <summary>「動作」窗口：MIS x y 动作号（官方实测 "MIS 3N 1O b" = 坐下 11）。</summary>
    public void Emote(int code) => Send("MIS", B62(X), B62(Y), B62(code));
    /// <summary>登录状态变化（显示给玩家）。</summary>
    public event Action<string>? Status;
    public event Action<string, int>? ChatReceived;
    /// <summary>出错（连接失败、登录失败等）。</summary>
    public event Action<string>? Error;

    public MapInfo? CurrentMap { get; private set; }
    public PlayerState Player { get; } = new();
    public bool InDialog => _window != null;
    public int X { get; private set; }
    public int Y { get; private set; }
    public bool InBattle { get; private set; }
    public bool InGame { get; private set; }
    public string SelfId { get; private set; } = "";

    private readonly NetworkOptions _opt;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private Thread? _reader;
    private readonly ConcurrentQueue<string> _inbox = new();
    private volatile bool _closed;
    private string _token = "0";
    private int _floor;
    private bool _createdChar;
    private readonly Dictionary<string, Npc> _objects = new();          // 周围对象（NPC/其他玩家），键是对象 id（62 进制）
    private readonly Dictionary<int, Item> _items = new();               // 道具编号 → 名称/图像（从 I 包收集）

    // 战斗
    private List<UnitView> _units = new();
    private int _turn;
    private bool _battleShown;
    private Command? _pending;
    private int _answeredThisTurn;
    private int _lastTurnAnswered = -1;

    public NetworkSession(NetworkOptions opt) { _opt = opt; }

    public IReadOnlyCollection<Npc> Objects => _objects.Values;

    // ———————————————————— 连接与收发 ————————————————————

    /// <summary>连接断开（不是自己 Dispose 的）。参数是原因。界面收到后自动重连（见 Main）。</summary>
    public event Action<string>? Disconnected;
    public Action<string>? GameLog;
    public bool Connected { get; private set; }
    public int Reconnects { get; private set; }
    private string? _lastError;

    /// <summary>
    /// 断线重连：关掉旧连接，清掉跟这次连接有关的状态（周围对象、战斗、窗口、交易、银行、队伍），重新走登录流程
    /// （ClientLogin → 角色列表 → 进角色 → 服务端发进图 → 界面重建地图）。账号被占用（服务端还没发现旧连接断了）就由界面过几秒再试。
    /// </summary>
    public void Reconnect()
    {
        try { _tcp?.Close(); } catch { }
        while (_inbox.TryDequeue(out _)) { }
        _lastError = null; Reconnects++;
        InGame = false; InBattle = false; _units = new(); _battleShown = false; _pending = null; _lastTurnAnswered = -1;
        _objects.Clear(); _ground.Clear(); _window = null; Trade = null; Bank = null; _party.Clear();
        Start();
    }

    /// <summary>测试用：模拟网络掉线（直接关 socket，不标记 _closed）。</summary>
    public void TestDrop() { try { _tcp?.Client.Shutdown(SocketShutdown.Both); _tcp?.Close(); } catch { } }

    public void Start()
    {
        try
        {
            _tcp = new TcpClient { NoDelay = true };
            _tcp.Connect(_opt.Host, _opt.Port);
            _stream = _tcp.GetStream();
        }
        catch (Exception e) { _lastError = e.Message; Error?.Invoke($"连接服务器失败：{e.Message}"); Disconnected?.Invoke(e.Message); return; }
        Connected = true;
        var stream = _stream;
        _reader = new Thread(() => ReadLoop(stream)) { IsBackground = true, Name = "cg-net" };
        _reader.Start();
        Status?.Invoke("正在登录…");
        Send("JFVf", _opt.CdKey, _opt.Password, _opt.Account, "");   // 字段顺序：cdkey 密码 账号（实测）
    }

    private void ReadLoop(NetworkStream stream)
    {
        var buf = new byte[65536];
        var line = new List<byte>(4096);
        try
        {
            while (!_closed)
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] == (byte)'\n') { _inbox.Enqueue(Gbk.Decode(line.ToArray())); line.Clear(); }
                    else if (buf[i] != (byte)'\r') line.Add(buf[i]);
                }
            }
        }
        catch (Exception) when (_closed || stream != _stream) { return; }
        catch (Exception e) { _inbox.Enqueue("\0ERR " + e.Message); }
        if (stream == _stream) _inbox.Enqueue("\0CLOSED");   // 重连后旧线程的结束不算
    }

    /// <summary>研究用：原样发一个包（包头 + 空格分隔的字段）。</summary>
    public void SendRaw(string line) { var p = line.Split(' '); Send(p[0], p.Skip(1).ToArray()); }

    private void Send(string header, params string[] fields)
    {
        var text = fields.Length == 0 ? header : header + " " + string.Join(' ', fields);
        _opt.Trace?.Invoke("C " + text);
        var bytes = Gbk.Encode(text + "\n");
        try { _stream?.Write(bytes, 0, bytes.Length); }
        catch (Exception e) { Error?.Invoke($"发送失败：{e.Message}"); }
    }

    /// <summary>在主线程调用：处理收到的封包。</summary>
    public void Poll()
    {
        while (_inbox.TryDequeue(out var line))
        {
            if (line.StartsWith('\0'))
            {
                if (line.StartsWith("\0ERR")) { _lastError = line[5..]; continue; }
                if (_closed) continue;
                bool wasIn = InGame;
                InGame = false; Connected = false;
                GameLog?.Invoke($"连接断开（{_lastError ?? "服务器关闭了连接"}），之前{(wasIn ? "在游戏里" : "还没进游戏")}");
                Disconnected?.Invoke(_lastError ?? "服务器断开了连接");
                continue;
            }
            _opt.Trace?.Invoke("S " + line);
            try { Handle(line); }
            catch (Exception e) { _opt.Trace?.Invoke($"! 解析失败 {e.Message}: {line}"); }
        }
    }

    // ———————————————————— 编码小工具 ————————————————————

    private static string B62(long v) => CgPort.Protocol.Base62.Encode(v);
    private static int D62(string s) => (int)CgPort.Protocol.Base62.Decode(s);
    private static int Hex(string s) => int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    private static string Unesc(string s) => s.Replace("\\S", " ").Replace("\\\\c", ",").Replace("\\c", ",").Replace("\\\\n", "\n").Replace("\\n", "\n");

    // ———————————————————— 封包处理 ————————————————————

    private void Handle(string line)
    {
        int sp = line.IndexOf(' ');
        string head = sp < 0 ? line : line[..sp];
        string rest = sp < 0 ? "" : line[(sp + 1)..];
        var f = rest.Split(' ');
        switch (head)
        {
            case "whV":   // ClientLogin 结果
                if (f[0] == "0") { Status?.Invoke("获取角色列表…"); Send("Gp"); }
                else Error?.Invoke("登录失败：" + rest);
                break;
            case "Daj":   // CharList
                OnCharList(f);
                break;
            case "DZ":    // CreateNewChar
                if (f[0] == "successful") Send("Gp"); else Error?.Invoke("创建角色失败：" + rest);
                break;
            case "hPUd":  // CharLogin
                if (f[0] == "successful") { InGame = true; Status?.Invoke("已进入游戏"); }
                else Error?.Invoke("进入游戏失败：" + Unesc(rest));
                break;
            case "PudO":  // LI：自己的对象 id、服务端 unix 时间（62 进制）
                SelfId = f[0];
                if (f.Length > 1) ServerTimeOffset = D62Safe(f[1]) - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                break;
            case "GmNM":  // CC：进图
                if (Bank != null) CloseBank();   // 换图（传送）银行窗口就关
                _stalls.Clear(); if (Browsing != null) { Browsing = null; StallChanged?.Invoke(); }
                OnMapChange(f);
                break;
            case "TeD":   // MN：地图名
                if (CurrentMap != null) CurrentMap = CurrentMap with { Name = Unesc(rest.Trim()).Split("\\\\z")[0] };
                break;
            case "QBt":   // XYD：服务端校正位置
                X = D62(f[0]); Y = D62(f[1]);
                Moved?.Invoke(X, Y, f.Length > 2 && int.TryParse(f[2], out var d) ? d : 0);
                break;
            case "uSB":   // CD：对象没了（捡走 / 走出视野）
                foreach (var id in rest.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (_ground.Remove(id)) GroundChanged?.Invoke();
                    else if (_objects.Remove(id)) { if (_stalls.Remove(D62Safe(id))) StallChanged?.Invoke(); ObjectsChanged?.Invoke(); }
                }
                break;
            case "nsY":   // C：周围对象，多个对象用 Tab 分隔
                foreach (var obj in rest.Split('\t')) OnObject(obj.Trim());
                ObjectsChanged?.Invoke();
                break;
            case "Zd":    // STK：系统提示（"bob加入了你的队伍。"），官方在聊天栏用黄字显示
                ChatReceived?.Invoke(Unesc(rest.Trim()), 4);
                break;
            case "SkBD":  // FS：开关（左上 戰聊隊名易）
                Flags = D62Safe(rest.Trim());
                FlagsChanged?.Invoke();
                break;
            case "JJQj":  // CN：队伍成员
                OnPartyMember(f);
                break;
            case "TNT":   // PP：队员状态
                OnPartyParam(rest.Trim());
                break;
            case "bNY":   // PR：组队请求结果 请求(1 加入/0 离开) 结果(1 成功)
                if (f.Length >= 2 && f[0] == "0" && f[1] == "1") { _party.Clear(); PartyChanged?.Invoke(); }
                break;
            case "gnxd":  // AB：名片簿全部 60 张（每张 7 段：有效|名字|称号|?|等级|在线|头像）
                OnCards(rest.Trim());
                break;
            case "vI":    // ABI：更新一张名片 "序号 1|名字|称号|?|等级|在线|头像|…"
                if (f.Length >= 2 && int.TryParse(f[0], out var ci)) { SetCard(ci, string.Join(' ', f.Skip(1)).Split('|'), 0); CardsChanged?.Invoke(); }
                break;
            case "Mhbt":  // MSG：收到邮件 "名片序号 日期|内容|-1 0"
                OnMail(f);
                break;
            case "Cxo":   // TRPL：可交易的对象名单 "hao|bob|"
                TradeCandidates = rest.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Unesc).ToList();
                if (TradeCandidates.Count > 0) Send("OU", "0");   // 官方弹出"请选择对象"时先发一个 OU 0，清掉服务端可能残留的交易状态
                TradeCandidatesReceived?.Invoke(TradeCandidates);
                break;
            case "ms":    // TRS：有人要跟我交易 "bob a"（第二段是对方等级，62 进制：bob Lv10 = a）
                Trade = new TradeState { Partner = Unesc(f[0]) };
                TradeChanged?.Invoke();
                break;
            case "nUbJ":  // TRLI：对方放上来的道具（格式同 rU，第一段是序号）
                if (Trade != null && ParseItem(rest.Trim().Split('|'), 0) is { } ti) { Trade.TheirItems.RemoveAll(x => x.Slot == ti.Slot); Trade.TheirItems.Add(ti); TradeChanged?.Invoke(); }
                break;
            case "hJq":   // TRLG：对方放上来的金币
                if (Trade != null) { Trade.TheirGold = int.TryParse(f[0], out var tg) ? tg : 0; TradeChanged?.Invoke(); }
                break;
            case "RwK":   // TRLP：对方放上来的宠物
                if (Trade != null && f.Length >= 2 && int.TryParse(f[0], out var tpi))
                {
                    var a = string.Join(' ', f.Skip(1)).Split('|');
                    var tp = new PetInfo { Slot = tpi, Name = Unesc(a[0]), Level = Int(a, 1), Race = Int(a, 2), MaxHp = Int(a, 3), Hp = Int(a, 3), MaxMp = Int(a, 4), Mp = Int(a, 4),
                        Vit = Int(a, 5), Str = Int(a, 6), Tgh = Int(a, 7), Spd = Int(a, 8), Mag = Int(a, 9), Loyalty = Int(a, 10), SkillSlots = Int(a, 11), Anime = Int(a, 13),
                        Attack = Int(a, 14), Defense = Int(a, 15), Agility = Int(a, 16), Spirit = Int(a, 17), Recovery = Int(a, 18) };
                    for (int e = 0; e < 4; e++) tp.Element[e] = Int(a, 19 + e);
                    if (a.Length > 12 && a[12].Length > 0 && a[12] != a[0]) tp.Nick = tp.Name;
                    Trade.TheirPets[tpi] = tp; TradeChanged?.Invoke();
                }
                break;
            case "Yq":    // TRLPS：对方那只宠物的招式名
                if (Trade != null && f.Length >= 2 && int.TryParse(f[0], out var tpk) && Trade.TheirPets.TryGetValue(tpk, out var tpp))
                {
                    tpp.Techs = string.Join(' ', f.Skip(1)).Split('|').Where(x => x.Trim().Length > 0).Select((n, k) => new TechInfo { Index = k, Name = Unesc(n) }).ToList();
                    TradeChanged?.Invoke();
                }
                break;
            case "xEg":   // TRCL：对方撤回了某件（道具 / 宠物）
                if (Trade != null) { Trade.TheirItems.Clear(); Trade.TheirPets.Clear(); Trade.TheirGold = 0; Trade.TheyOpened = false; TradeChanged?.Invoke(); }
                break;
            case "NdM":   // TROP：对方"开启"了（选好了）
                if (Trade != null) { Trade.TheyOpened = true; TradeChanged?.Invoke(); }
                break;
            case "We":    // TROC：0 中止、1 对方确认、2 完成
                if (f[0] == "1") { if (Trade != null) { Trade.TheyConfirmed = true; TradeChanged?.Invoke(); } }
                else { Trade = null; TradeChanged?.Invoke(); TradeEnded?.Invoke(f[0] == "2"); }
                break;
            case "Ov":    // POS：战斗位置 0 前排 1 后排
                BackRow = f[0] == "1"; StatusChanged?.Invoke();
                break;
            case "Gn":    // RS：战斗结果（经验、掉落、技能经验）
                OnBattleResult(rest.Trim());
                break;
            case "MTg":   // BT：左上 Burst 时间（秒，62 进制）、是否在走
                BurstSeconds = D62Safe(f[0]); BurstRunning = f.Length > 1 && f[1] == "1"; BurstAt = DateTime.UtcNow;
                StatusChanged?.Invoke();
                break;
            case "Tmrx":  // 摆摊结果：0 成功
                OnStallStarted(f[0]);
                break;
            case "KXhH":  // 收摊结果：0 成功
                if (f[0] == "0") { Stalling = false; StallChanged?.Invoke(); } else ChatReceived?.Invoke("收摊失败", 4);
                break;
            case "ft":    // 浏览摊位
                OnStallBrowse(f);
                break;
            case "Yx":    // 买摊位上的东西：0 成功
                OnStallBought(f[0]);
                break;
            case "NI":    // CA：对象动作/移动
                OnAction(rest.Trim());
                break;
            case "aPc":   // CP：人物状态
                OnCharParam(rest.Trim());
                break;
            case "rU":    // I：道具
                OnItems(rest.Trim());
                break;
            case "dAgg":  // EN：进入战斗（"dAgg 0 0" = 对战/突袭失败，官方随后 "Zd 突发战斗失败"）
                if (f.Length > 0 && f[0] == "0") break;
                InBattle = true; _battleShown = false; _turn = 0; _units.Clear(); _down.Clear(); _lastTurnAnswered = -1;
                break;
            case "LYIm":  // B：战斗
                OnBattle(rest.Trim());
                break;
            case "BZ":    // WN：服务端弹出窗口（NPC 对话、选项列表、GM 命令菜单）
                OnWindow(f);
                break;
            case "zdr":   // TK：聊天/系统消息。对象号 P|文字 颜色 字号（实测："3qR P|马葛道尔:\S公会好像… 0 0"，系统消息对象号 -1）
                OnChat(f);
                break;
            case "cNE":   // CS：人物技能（每个 11 段，见 OnSkills）
                OnSkills(rest.Trim()); StatusChanged?.Invoke();
                break;
            case "Rspn":  // CT：某个技能的各招（技能栏 名字|说明|MP|?|招式编号|?|…）
                OnTechs(f); StatusChanged?.Invoke();
                break;
            case "IsSy":  // KP：宠物属性（宠物栏 掩码|值…，同 CP 的掩码规则）
                OnPet(f); StatusChanged?.Invoke();
                break;
            case "OBee":  // PT：宠物技能（宠物栏 序号|招式编号|名字|说明|MP|?|?|?|…）
                OnPetTechs(f); StatusChanged?.Invoke();
                break;
            case "UU":    // CP2：1–4 必杀/反击/命中/闪躲 5–10 六种抗性 11 战绩 17 技能栏总数（实测 10）
                {
                    var u = rest.Trim().Split('|');
                    if (u.Length > 17 && u[0] == "1")
                    {
                        if (int.TryParse(u[17], out var sl) && sl > 0) Player.SkillSlots = sl;
                        var st = Player.Stats;
                        st.Critical = Int(u, 1); st.Counter = Int(u, 2); st.Hit = Int(u, 3); st.Dodge = Int(u, 4);
                        for (int r = 0; r < 6; r++) st.Resist[r] = Int(u, 5 + r);
                        st.Record = Int(u, 11);
                    }
                    StatusChanged?.Invoke();
                }
                break;
            case "AU":    // KP2：宠物明细（体力力量强度速度魔法、修正、抗性、负重）
                OnPetParam(f);
                break;
            case "sA":    // PLVUP：宠物可分配点数 "宠物栏 点数"
                if (f.Length >= 2 && int.TryParse(f[0], out var ps) && Player.Pets.FirstOrDefault(p => p.Slot == ps) is { } pp) { pp.Points = int.TryParse(f[1], out var pn) ? pn : 0; StatusChanged?.Invoke(); }
                break;
            case "Ilz":   // 图鉴：已登记的号码，32 个 32 位整数（图鉴号 N 在第 N-1 位，实测）
                OnAlbumBits(rest);
                break;
            case "dM":    // ALI：某个图鉴号的内容（客户端发 kza 要，或刚登记时服务端主动发）
                OnAlbumInfo(f);
                break;
            case "IYo":   // ALO：刚登记的号码（列表里显示「新增」）
                if (f.Length > 0 && f[0].Length > 0 && D62(f[0]) is var an and > 0 and <= AlbumMax) { _album[an] = true; AlbumNew.Add(an); AlbumChanged?.Invoke(); }   // 登记后服务端不再发 Ilz（实测），这里自己记上
                break;
            case "LY":    // GFLI：用道具要先选队伍里的人
                ItemMemberRequested?.Invoke(ItemMembers());
                break;
            case "ysVc":  // 急救：服务端同意（iET 之后），客户端弹"请选择对象"（队伍成员）
                _treating = true;
                ItemMemberRequested?.Invoke(ItemMembers());
                break;
            case "ND":    // 急救：选了人之后的对象表（名字|等级|受伤|生命|最大生命|魔力|最大魔力）×N
                OnTreatTargets(rest.Trim());
                break;
            case "nFV":   // GPDI：选了人之后，选具体对象（人 / 宠物）
                ItemTargetRequested?.Invoke(ItemTargets());
                break;
            case "xmgQ":  // IR：制造技能的配方（客户端 fS 技能栏 要）
                OnRecipes(f);
                break;
            case "JgO":   // TU：生产结果
                OnProduce(f);
                break;
            case "iKe":   // 宠物装备
                OnPetEquip(f);
                break;
            case "kg":    // LVUP：可分配点数（62 进制，实测 登录 "h" = 17，加一点后 "g" = 16）
                Player.StatPoints = D62Safe(rest.Trim()); StatusChanged?.Invoke();
                break;
            case "Sc":    // TITLE：称号列表，每个 3 段：名字|?|?（实测 "见习剑士|2|111|呢喃的歌声|1|7|"）
            {
                var tp = rest.Split('|'); var list = new List<string>();
                for (int i = 0; i + 2 < tp.Length; i += 3) if (tp[i].Length > 0) list.Add(Unesc(tp[i]));
                Titles = list; StatusChanged?.Invoke();
                break;
            }
            case "jv":    // CJ：职业名（"1 游民"）
                { var j = rest.Trim(); int sp2 = j.IndexOf(' '); Player.Job = Unesc(sp2 >= 0 ? j[(sp2 + 1)..] : j); StatusChanged?.Invoke(); }
                break;
            case "Or":    // 服务端发来的 Echo（少见）
                break;
        }
    }

    private void OnCharList(string[] f)
    {
        if (f.Length < 2 || f[0] != "0")
        {
            // 重连时服务端可能还没发现旧连接断了：断开，让界面过几秒再试
            if (Reconnects > 0) { _lastError = "账号还在服务器上（旧连接还没断干净）"; try { _tcp?.Close(); } catch { } return; }
            Error?.Invoke("账号被占用（可能已在别处登录）"); return;
        }
        var list = string.Join(' ', f.Skip(1)).Trim();
        var chars = list.Split('|', StringSplitOptions.RemoveEmptyEntries);
        // 每个角色占两段："名字" 和 "位置\\z头像\\z等级\\z…\\z注册号\\z职业\\z…\\z形象\\z-1"
        for (int i = 0; i + 1 < chars.Length; i += 2)
        {
            var name = chars[i]; var parts = chars[i + 1].Split("\\\\z");
            if (parts.Length < 15) continue;
            Player.Name = Unesc(name);
            Player.Level = int.TryParse(parts[2], out var lv) ? lv : 1;
            Status?.Invoke($"进入角色 {Player.Name}…");
            Send("emRV", B62(long.Parse(parts[14])), parts[0], B62(long.Parse(parts[1])));   // 角色列表里注册号是十进制，CharLogin 要 62 进制（实测）
            return;
        }
        if (_createdChar) { Error?.Invoke("创建角色后仍然没有角色"); return; }
        _createdChar = true;
        Status?.Invoke("账号下没有角色，自动创建…");
        // 形象 106002、头像 241400（游民）；点数 体0 力0 强0 速15 魔15，属性 地0 水0 火5 风5（与官方客户端创建界面的默认分配无关，仅测试用）
        Send("aSEl", "0", _opt.NewCharName, B62(106002), B62(241400), "0", "0", "0", "f", "f", "0", "0", "5", "5");
    }

    private void OnMapChange(string[] f)
    {
        _floor = D62(f[1]);
        X = D62(f[4]); Y = D62(f[5]);
        _token = f[^1].Length > 0 ? f[^1] : f[^2];
        _objects.Clear(); _ground.Clear();
        var file = _opt.ResolveMap(_floor) ?? "";
        CurrentMap = new MapInfo(_floor, "", file);
        Player.MapId = _floor; Player.X = X; Player.Y = Y;
        MapEntered?.Invoke(CurrentMap, X, Y);
    }

    // ———— 地上的东西（官方实测 2026-11-07）————
    // nsY 里 7 段的是地上的道具 "对象|x|y|图像|?|名字|数量"，4 段的是钱 "对象|x|y|金额"（钱少是几枚金币，多了是钱袋）。
    // uSB 对象 = 这个对象没了（被捡走、走出视野）。
    public sealed record GroundItem(string Id, int X, int Y, int Image, string Name, int Count, bool Gold) { public bool Pet { get; init; } public int Dir { get; init; } }
    /// <summary>
    /// 扔宠物（DP = FvWm x y 宠物栏，官方实测：宠物窗口里在宠物名字上点右键）→ 服务端 IsSy 栏 0（栏空了）+ nsY 3|id|x|y|朝向|形象|…|名字
    /// （类型 3 = 地上的宠物，放在面前一格）+ "放下树精 (法兰城:242,101)"。捡：面向它 fIM x y 方向 → IsSy + uSB + "捡到了树精。"
    /// </summary>
    public void DropPet(int petSlot) => Send("FvWm", B62(X), B62(Y), B62(petSlot));
    private readonly Dictionary<string, GroundItem> _ground = new();
    public IReadOnlyCollection<GroundItem> GroundItems => _ground.Values;
    public event Action? GroundChanged;

    /// <summary>扔道具 DI：QpfE x y 0 背包格（服务端放在身边空地上）。</summary>
    public void DropItem(int slot) => Send("QpfE", B62(X), B62(Y), "0", B62(slot));
    /// <summary>扔钱 DG：woz x y 金额（62 进制）。</summary>
    public void DropGold(long amount) { if (amount > 0) Send("woz", B62(X), B62(Y), B62(amount)); }
    /// <summary>捡 PI：先面向那一格，再发 fIM x y 方向（方向号同走路 0–7）；站在上面就是方向 8（实测面向后发方向号能捡到）。</summary>
    public bool PickUp(int x, int y)
    {
        int dx = x - X, dy = y - Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return false;
        int dir = dx == 0 && dy == 0 ? 8 : Array.IndexOf(CgPort.Core.Directions.Deltas, (dx, dy));
        if (dir < 8) Face(dx, dy);
        Send("fIM", B62(X), B62(Y), dir.ToString());
        return true;
    }
    /// <summary>这个服务端（致敬经典）的「整理」背包按钮：CUSTOMXB 0（官方 4.06 没有）。</summary>
    public void SortBag() => Send("CUSTOMXB", "0");
    /// <summary>右上「功能」第 code 项（1 佣兵 2 银行 … 8 高速 … 23 佣兵血魔），这个服务端的扩展。</summary>
    public void CustomFunction(int code) => Send("CUSTOMXB", code.ToString());

    private void OnObject(string s)
    {
        var p = s.Split('|');
        if (p.Length == 7 && int.TryParse(p[1], out var ix) && int.TryParse(p[2], out var iy))
        {
            _ground[p[0]] = new GroundItem(p[0], ix, iy, int.TryParse(p[3], out var im) ? im : 0, Unesc(p[5]), int.TryParse(p[6], out var cn) ? cn : 1, false);
            GroundChanged?.Invoke(); return;
        }
        if (p.Length == 4 && int.TryParse(p[1], out var gx) && int.TryParse(p[2], out var gy))
        {
            _ground[p[0]] = new GroundItem(p[0], gx, gy, 0, "金币", int.TryParse(p[3], out var amt) ? amt : 0, true);
            GroundChanged?.Invoke(); return;
        }
        if (p.Length < 9) return;
        int type = int.TryParse(p[0], out var t) ? t : 0;
        if (type == 3 && int.TryParse(p[2], out var px) && int.TryParse(p[3], out var py))   // 地上的宠物
        {
            _ground[p[1]] = new GroundItem(p[1], px, py, int.TryParse(p[5], out var pa) ? pa : 0, Unesc(p[8]), 1, false) { Pet = true, Dir = int.TryParse(p[4], out var pd) ? pd : 0 };
            GroundChanged?.Invoke(); return;
        }
        var id = p[1];
        if (id == SelfId)
        {
            X = int.Parse(p[2]); Y = int.Parse(p[3]);
            if (int.TryParse(p[5], out var me) && me != SelfAppearance) { SelfAppearance = me; StatusChanged?.Invoke(); }
            return;
        }
        int.TryParse(p[2], out var x); int.TryParse(p[3], out var y); int.TryParse(p[4], out var dir); int.TryParse(p[5], out var img);
        _objects[id] = new Npc(D62Safe(id), _floor, x, y, dir, img, Encoding.Latin1.GetString(Gbk.Encode(Unesc(p[8]))));   // Npc.RawName 约定是按 Latin1 存的原始 GBK 字节
        if (type == 1) _playerObjs.Add(D62Safe(id)); else _playerObjs.Remove(D62Safe(id));
    }

    private static int D62Safe(string s) { try { return D62(s); } catch { return 0; } }
    /// <summary>nsY 第 1 段类型 1 = 玩家（实测：hao、bob 是 1，NPC 是 29/48/50/80…）。</summary>
    private readonly HashSet<int> _playerObjs = new();
    public bool IsPlayerObj(int id) => _playerObjs.Contains(id);

    private void OnAction(string s)
    {
        // 一行可能有多段（逗号分隔）："8we|242|100|45|7|1,8we|242|100|51|7"
        foreach (var seg in s.Split(','))
        {
            var p = seg.Split('|');
            if (p.Length < 5) continue;
            if (p[0] == SelfId) continue;   // 自己的位置以本地预测 + QBt 校正为准
            {
                int fid = D62Safe(p[0]);
                if (p[3] == "40" ? _fighting.Add(fid) : _fighting.Remove(fid)) FightingChanged?.Invoke();
            }
            if (p[3] == "200")   // 摆摊：id|x|y|200|0|1 开张、…|0 收摊（实测）
            {
                int sid = D62Safe(p[0]);
                bool on = p.Length > 5 && p[5] == "1";
                if (on ? _stalls.Add(sid) : _stalls.Remove(sid)) StallChanged?.Invoke();
                continue;
            }
            if (_objects.TryGetValue(p[0], out var n) && int.TryParse(p[1], out var x) && int.TryParse(p[2], out var y))
            {
                if ((x != n.X || y != n.Y) && FollowTarget == p[0]) Follow(n.X, n.Y);
                var moved = x != n.X || y != n.Y;
                _objects[p[0]] = n with { X = x, Y = y, Dir = int.TryParse(p[4], out var d) ? d : n.Dir };
                if (moved) ObjectMoved?.Invoke(_objects[p[0]]);
                // 动作（官方 動作 窗口发 MIS，旁人收到 NI id|x|y|动作号|朝向）：3–20 动画动作、30 走路、31 站立
                else if (int.TryParse(p[3], out var act) && (act is >= 3 and <= 20 || act is 30 or 31)) ObjectActed?.Invoke(n.Id, act);
            }
        }
    }

    // ———————————————————— 战斗结果 / 卡时（官方 bob 实测 2026-11-09）————————————————————
    // Gn -2|升级|经验,宠物栏|升级|经验,…(3 只),道具号\z名字|道具号\z名字|道具号\z名字|,技能0,…技能19
    //   数字是 62 进制；技能段 "升级|经验" 或空（= +0）。窗口「戰鬥結果」23380 几秒后自己关。
    // MTg 秒 0/1：左上 "Burst: hh:mm"（新号 W4 = 3600 = 01:00；bob 4UE = 18888 秒 = 05:14）。
    public sealed record ResultRow(int PetSlot, bool LevelUp, long Exp);
    public sealed record BattleResult(List<ResultRow> Rows, List<(int Id, string Name)> Items, Dictionary<int, (bool LevelUp, long Exp)> SkillExp);
    public BattleResult? LastResult { get; private set; }
    public event Action<BattleResult>? BattleResultReceived;
    public long BurstSeconds { get; private set; } = -1;
    /// <summary>平时设的战斗位置（状态窗口 戰鬥位置）：Ov 1 = 后排。</summary>
    public bool BackRow { get; private set; }
    /// <summary>POS = ih（不带参数也行，官方发 "ih "）：前排 ↔ 后排对调，服务端回 Ov。</summary>
    public void ToggleBattleRow() => Send("ih", "");
    /// <summary>SKSW = Noeb a b：技能列表里两个技能换位置（拖动，官方实测服务端回两条 cNE，改的是第 11 段顺序）。</summary>
    public void SwapSkills(int a, int b) => Send("Noeb", a.ToString(), b.ToString());
    /// <summary>PSSW = HJQp 宠物栏 a b：宠物的两招换位置（服务端回整条 OBee）。</summary>
    public void SwapPetTechs(int pet, int a, int b) => Send("HJQp", pet.ToString(), a.ToString(), b.ToString());
    public bool BurstRunning { get; private set; }
    public DateTime BurstAt { get; private set; }

    private void OnBattleResult(string s)
    {
        var seg = s.Split(',');
        var rows = new List<ResultRow>(); var items = new List<(int, string)>(); var sk = new Dictionary<int, (bool, long)>();
        for (int i = 0; i < seg.Length; i++)
        {
            var p = seg[i].Split('|');
            if (i <= 3)
            {
                if (p.Length >= 3 && p[0].Length > 0) rows.Add(new ResultRow(p[0] == "-2" ? -1 : D62Safe(p[0]), p[1] == "1", D62Safe(p[2])));
            }
            else if (i == 4)
            {
                foreach (var it in p)
                {
                    var q = it.Split("\\z");
                    if (q.Length >= 2 && int.TryParse(q[0], out var id)) items.Add((id, Unesc(q[1])));
                }
            }
            else if (p.Length >= 2) sk[i - 5] = (p[0] == "1", D62Safe(p[1]));
        }
        LastResult = new BattleResult(rows, items, sk);
        BattleResultReceived?.Invoke(LastResult);
    }

    // ———————————————————— 摆摊 ————————————————————
    // 实测（官方 4.06 bob + 工具 hao，2026-11-09/10），服务端 cgmsv.cf stallflg=1、stallmaps=1000,1500：
    //   开张 ylGN 描述 描述 价1\\z…价20\\z宠价1\\z…宠价5\\z（背包 20 格按顺序 + 5 只宠物，0 = 不卖）→ Tmrx 0；
    //     旁人收到 NI id|x|y|200|0|1（头上挂"店铺 贩售中"牌子），收摊 JMh → KXhH 0，旁人 NI …|200|0|0。
    //   浏览：右键（手机：点）旁边摆摊的人 → ElVN 对象id → ft 0 对象id 描述 描述 物品段 宠物段：
    //     物品段 = 个数|（价|rU 的 14 段）×N；宠物段 = 只数|（价|宠物栏|IsSy 24 段|?|?|?|AU 19 段|可分配点数|招式数|（编号|名字）×招式数）×N
    //   买：wAo 格（道具 = 卖家背包格 8 起，宠物 = 宠物栏 0–4）→ Yx 0；成功后官方本地把那格清掉，不重新浏览。
    //   错误提示（exe 里的顺序；编号对应没逐个试过）：开张 1 最大摊位数已满 2 两次摆摊时间间隔过短 3 已经有人在此摆摊 其他 摆摊失败；
    //   浏览 1 没有可以浏览的摊位 2 已经收摊 3 正在忙 4 没有找到老板；买 1 没有找到老板 2 摊位已经变更 3 物品空间已满 4 宠物空间已满 5 钱不够（实测：没钱的号买 → Yx 5）6 已经售出 7 老板的钱包已满 其他 购买失败。
    public sealed record StallItem(int Price, ItemStack Item);
    public sealed record StallPet(int Price, PetInfo Pet);
    public sealed class StallView
    {
        public int OwnerId; public string OwnerName = ""; public int OwnerAnime; public string Desc = "";
        public List<StallItem> Items = new(); public List<StallPet> Pets = new();
    }
    private readonly HashSet<int> _stalls = new();
    public bool IsStall(int objId) => _stalls.Contains(objId);
    public IReadOnlyCollection<int> Stalls => _stalls;
    public bool Stalling { get; private set; }
    public StallView? Browsing { get; private set; }
    public event Action? StallChanged;
    private int _browseTarget;

    private static readonly string[] StallStartErr = { "", "这个子星最大摊位数已满", "两次摆摊时间间隔过短", "已经有人在此摆摊" };
    private static readonly string[] StallBrowseErr = { "", "没有可以浏览的摊位", "浏览的摊位已经收摊", "浏览的摊位正在忙，请稍后再试", "没有找到老板" };
    private static readonly string[] StallBuyErr = { "", "没有找到老板", "老板的摊位已经变更", "您的物品空间已满", "您的宠物空间已满", "您的钱不够", "要买的东西已经售出", "老板的钱包已满" };
    private static string Pick(string[] t, string code, string other) => int.TryParse(code, out var n) && n > 0 && n < t.Length ? t[n] : other;

    /// <summary>开张：itemPrices 键是背包格（8–27），petPrices 键是宠物栏（0–4），价钱 0 = 不卖。</summary>
    public void StallStart(string desc, IReadOnlyDictionary<int, int> itemPrices, IReadOnlyDictionary<int, int> petPrices)
    {
        if (string.IsNullOrWhiteSpace(desc)) desc = "未写描述的店铺";
        var d = desc.Replace("\\", "\\\\").Replace(" ", "\\S");
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++) sb.Append(itemPrices.TryGetValue(i + 8, out var v) ? v : 0).Append("\\\\z");
        for (int i = 0; i < 5; i++) sb.Append(petPrices.TryGetValue(i, out var v) ? v : 0).Append("\\\\z");
        Send("ylGN", d, d, sb.ToString());
    }
    public void StallEnd() => Send("JMh");

    private void OnStallStarted(string code)
    {
        if (code == "0") { Stalling = true; StallChanged?.Invoke(); }
        else ChatReceived?.Invoke(Pick(StallStartErr, code, "摆摊失败"), 4);
    }

    /// <summary>浏览旁边的摊位（官方：右键点摆摊的人，先转过去面对他）。</summary>
    public void StallBrowse(int objId)
    {
        if (_objects.Values.FirstOrDefault(o => o.Id == objId) is { } o && Math.Max(Math.Abs(o.X - X), Math.Abs(o.Y - Y)) <= 1) Face(o.X - X, o.Y - Y);
        _browseTarget = objId;
        Send("ElVN", B62(objId));
    }

    private void OnStallBrowse(string[] f)
    {
        if (f.Length < 1 || f[0] != "0") { ChatReceived?.Invoke(Pick(StallBrowseErr, f.Length > 0 ? f[0] : "", "没有可以浏览的摊位"), 4); return; }
        var v = new StallView { OwnerId = f.Length > 1 ? D62Safe(f[1]) : _browseTarget, Desc = f.Length > 2 ? Unesc(f[2]) : "" };
        if (_objects.Values.FirstOrDefault(o => o.Id == v.OwnerId) is { } o) { v.OwnerName = o.Name; v.OwnerAnime = o.AppearanceId; }
        if (f.Length > 4)
        {
            var t = f[4].Split('|');
            int n = int.TryParse(t[0], out var nn) ? nn : 0, i = 1;
            for (int k = 0; k < n && i + 13 < t.Length; k++, i += 15)
                if (ParseItem(t, i + 1) is { } it) v.Items.Add(new StallItem(int.TryParse(t[i], out var pr) ? pr : 0, it));
        }
        if (f.Length > 5)
        {
            var t = f[5].Split('|');
            int n = int.TryParse(t[0], out var nn) ? nn : 0, i = 1;
            for (int k = 0; k < n && i + 49 < t.Length; k++)
            {
                var pet = new PetInfo { Slot = int.TryParse(t[i + 1], out var sl) ? sl : 0 };
                ApplyPet(pet, t[(i + 2)..(i + 26)]);
                ApplyPetParam(pet, new[] { "1" }.Concat(t[(i + 29)..(i + 48)]).ToArray());
                pet.Points = int.TryParse(t[i + 48], out var pts) ? pts : 0;
                int tc = int.TryParse(t[i + 49], out var tcn) ? tcn : 0;
                for (int j = 0; j < tc && i + 51 + 2 * j < t.Length; j++)
                    pet.Techs.Add(new TechInfo { Name = Unesc(t[i + 51 + 2 * j]) });
                v.Pets.Add(new StallPet(int.TryParse(t[i], out var pr) ? pr : 0, pet));
                i += 50 + 2 * tc;
            }
        }
        Browsing = v;
        StallChanged?.Invoke();
    }

    private int _buying = -1;
    /// <summary>买：道具给卖家的背包格（8 起），宠物给宠物栏（0–4）。</summary>
    public void StallBuy(int slot) { _buying = slot; Send("wAo", B62(slot)); }
    public void CloseStallBrowse() { Browsing = null; StallChanged?.Invoke(); }

    private void OnStallBought(string code)
    {
        if (code == "0" && Browsing != null)
        {
            if (_buying >= 8) Browsing.Items.RemoveAll(x => x.Item.Slot == _buying);
            else Browsing.Pets.RemoveAll(x => x.Pet.Slot == _buying);
        }
        else if (code != "0") ChatReceived?.Invoke(Pick(StallBuyErr, code, "购买失败"), 4);
        _buying = -1;
        StallChanged?.Invoke();
    }

    // ———————————————————— 名片 / 邮件 ————————————————————
    // 实测（官方客户端 bob + 工具 hao），双方都开「名」（FS 位 16）：
    //   面向对方点右上「交换名片」→ AAB "vcAz 自己x 自己y" → 两边 "Zd 和hao交换了名片。"、"vI 序号 1|名字|称号|?|等级|在线|头像|…"、"TFCu 8"（音效）。
    //   登录时 AB "gnxd" 给全部 60 张（每张 7 段）；对方上下线时收 vI（在线 1/0）。
    //   删名片：官方先问"确实要将「hao」从名片列表删除吗？"，是 → DAB "FPkR 序号" → 服务端重发 gnxd。
    //   写信：名片上点「郵件」→ 编辑邮件 → 「送出」→ MSG "CV 名片序号 内容 0"（空格写成 \S）→ "Zd 已将邮件送交hao。"
    //   收信：MSG "Mhbt 名片序号 日期|内容|-1 0"（对方不在线时，等他上线再发；日期例 "2026/11/\S4\S\S9:02"）。

    public sealed record AddressCard(int Index, string Name, string Title, int Level, bool Online, int Portrait);
    public sealed record Mail(int CardIndex, string Name, string Date, string Text, bool Incoming);
    private readonly AddressCard?[] _cards = new AddressCard?[60];
    public IReadOnlyList<AddressCard?> Cards => _cards;
    public List<Mail> Mails { get; } = new();
    public int UnreadMail { get; set; }
    public event Action? CardsChanged;
    public event Action<Mail>? MailReceived;

    private void OnCards(string s)
    {
        var p = s.Split('|');
        for (int i = 0; i < 60 && i * 7 < p.Length; i++) SetCard(i, p, i * 7);
        CardsChanged?.Invoke();
    }

    private void SetCard(int idx, string[] p, int o)
    {
        if (idx < 0 || idx >= 60) return;
        if (o + 6 >= p.Length || p[o] != "1" || p[o + 1].Length == 0) { _cards[idx] = null; return; }
        int I(int k) => int.TryParse(p[o + k], out var v) ? v : 0;
        _cards[idx] = new AddressCard(idx, Unesc(p[o + 1]), Unesc(p[o + 2]), I(4), I(5) != 0, I(6));
    }

    private void OnMail(string[] f)
    {
        if (f.Length < 2 || !int.TryParse(f[0], out var ci)) return;
        var p = f[1].Split('|');
        var m = new Mail(ci, ci is >= 0 and < 60 ? _cards[ci]?.Name ?? "" : "", Unesc(p[0]), p.Length > 1 ? Unesc(p[1]) : "", true);
        Mails.Add(m); UnreadMail++;
        MailReceived?.Invoke(m);
    }

    // ———————————————————— 图鉴 ————————————————————

    /// <summary>
    /// 一条图鉴（dM，实测 "dM M qkW 1 0 3 1 4 3 4 2 0 0 6 4 a  迷你蝙蝠"）：
    /// 号码 图像 封印 ? 种族 体力 力量 强度 速度 魔法 地 水 火 风 技能栏 (空) 名字，都是 62 进制。
    /// 星数（半颗为单位）= 值 + 1；属性每点 7 像素长条；封印 1 = 可封印图标 22903，0 = 不可 22904。
    /// </summary>
    public sealed record AlbumEntry(int No, int Anime, int Seal, int Unknown, int Race, int[] Stats, int[] Elements, int Slots, string Name);

    public const int AlbumMax = 1024;
    private readonly bool[] _album = new bool[AlbumMax + 1];
    private readonly HashSet<int> _albumAsked = new();
    public readonly Dictionary<int, AlbumEntry> AlbumEntries = new();
    public readonly HashSet<int> AlbumNew = new();
    public event Action? AlbumChanged;
    /// <summary>本地缓存（官方是 data/album.dat）：图鉴内容是服务端定的静态数据，存下来下次不用再要。</summary>
    public Func<int, AlbumEntry?>? AlbumCacheLoad;
    public Action<AlbumEntry>? AlbumCacheSave;

    public bool HasAlbum(int no) => no is > 0 and <= AlbumMax && _album[no];
    public int AlbumCount => _album.Count(b => b);

    private void OnAlbumBits(string rest)
    {
        var f = rest.Split(' ');
        var vals = (f.Length > 1 ? f[1] : "").Split('|');
        Array.Clear(_album);
        for (int i = 0; i < vals.Length && i < 32; i++)
        {
            if (!long.TryParse(vals[i], out var v)) continue;
            uint u = unchecked((uint)v);
            for (int b = 0; b < 32; b++)
                if ((u & (1u << b)) != 0) { int no = i * 32 + b + 1; if (no <= AlbumMax) _album[no] = true; }
        }
        for (int no = 1; no <= AlbumMax; no++)
            if (_album[no] && !AlbumEntries.ContainsKey(no))
            {
                if (AlbumCacheLoad?.Invoke(no) is { } e) AlbumEntries[no] = e;
                else if (_albumAsked.Add(no)) Send("kza", B62(no));   // 官方：有登记但本地没有内容就发 kza 号码，服务端回 dM
            }
        AlbumChanged?.Invoke();
    }

    private void OnAlbumInfo(string[] f)
    {
        if (f.Length < 15) return;
        int I(int i) => f[i].Length == 0 ? 0 : D62(f[i]);
        var e = new AlbumEntry(I(0), I(1), I(2), I(3), I(4),
            new[] { I(5), I(6), I(7), I(8), I(9) }, new[] { I(10), I(11), I(12), I(13) }, I(14),
            Unesc(f.Skip(15).LastOrDefault(x => x.Length > 0) ?? ""));
        AlbumEntries[e.No] = e;
        AlbumCacheSave?.Invoke(e);
        AlbumChanged?.Invoke();
    }

    // ———————————————————— 生产（官方客户端 bob 实测 2026-11-07）————————————————————
    // 制造：技能详情点招式 → 客户端 fS 栏 → 服务端 xmgQ 栏 配方… → 选配方、登录材料、执行 → IPy a（加工中，约 10 秒）
    //       → sM 栏 招式序号 -1 配方|格|格… → 服务端 rU（扣材料、给成品）+ cNE（技能经验）+ JgO 成功 种类 经验|…|图像|失去的材料… → IPy 5。
    // 采集：点招式 → IPy 6（开始，约 7 秒）→ sM 栏 0 -1 （最后一段空）→ JgO 1/0 4 经验|0|0|0|0|0|图像|名字|0 → IPy 5，2 秒后自动 IPy 6 继续。
    public event Action<int>? RecipesChanged;
    /// <summary>生产结果：成功、种类（1 制造 4 采集）、原始段。</summary>
    public event Action<ProduceResult>? ProduceDone;
    public sealed record ProduceResult(bool Ok, int Type, int Exp, int Image, string Name, IReadOnlyList<string> Lost, int Heal = 0);
    public void RequestRecipes(int skillSlot) => Send("fS", skillSlot.ToString());
    /// <summary>工作状态 IPy：5 平常、6 采集中、10 制造中（62 进制 "a"）。</summary>
    public void SetWork(int state) => Send("IPy", B62(state));
    public void Gather(int skillSlot, int tech = 0) => Send("sM", skillSlot.ToString(), tech.ToString(), "-1", "");
    /// <summary>
    /// 鉴定 / 修理 / 刻印（技能种类 2，官方 bob 实测 2026-11-09）：点招式 → klF x y 头顶图标 → 選擇物品窗口（23354）双击道具、按「鑑定」
    /// → IPy c（鉴定中，修理 d）→ 约 12 秒后 sM 栏 招式 格(62 进制) 空 → rU（鉴定后名字、说明变了）+ cNE + JgO 1/0 2 经验|0|0|0|0|0|0 → IPy 5。
    /// 没受损的东西去修："Zd 此道具并未受损。" + JgO 0。
    /// </summary>
    public void UseOnItem(int skillSlot, int tech, int bagSlot) => Send("sM", skillSlot.ToString(), tech.ToString(), B62(bagSlot), "");
    /// <summary>
    /// 急救（技能种类 3）：点招式 → iET 栏 → 服务端 ysVc（空）→ 客户端"请选择对象"（队伍成员）→ McVa 序号 → 服务端 ND 对象表
    /// → "请选择使用对象" → sM 栏 招式 对象（0 人，宠物 = 栏位+1）空，马上就有结果：对方 aPc + "Zd 接受bob的急救，回复了30点的生命力。"，自己 JgO 1 3 经验|…|回复量。
    /// </summary>
    public void RequestTreat(int skillSlot, int tech) { _treatSkill = skillSlot; _treatTech = tech; Send("iET", skillSlot.ToString()); }
    private bool _treating; private int _treatSkill, _treatTech;
    public bool Treating => _treating;
    /// <summary>头顶的技能图标 klF x y 号（官方 exe：技能 200–216 制造 10、215 11、修理 217/218 13、鉴定 219 12、急救 223/224 14、伐木 225 5、狩猎 226 7、挖掘 227 6；0 = 去掉）。</summary>
    public void SkillIcon(int skillId)
    {
        int code = skillId switch
        {
            215 => 11, >= 200 and <= 216 => 10, 217 or 218 => 13, 219 => 12, 220 or 221 or 222 or 255 => 10,
            223 or 224 => 14, 225 or 251 => 5, 226 or 253 => 7, 227 or 250 => 6, 2000 => 9, _ => 0,
        };
        Send("klF", B62(X), B62(Y), B62(code));
    }
    public void ClearSkillIcon() => Send("klF", B62(X), B62(Y), "0");

    private void OnTreatTargets(string s)
    {
        var p = s.Split('|');
        var list = new List<ItemTarget>();
        for (int i = 0, k = 0; i + 6 < p.Length; i += 7, k++)
        {
            if (p[i].Length == 0) continue;
            list.Add(new ItemTarget(k, Unesc(p[i]), Int(p, i + 1), Int(p, i + 3), Int(p, i + 4), Int(p, i + 5), Int(p, i + 6), Unesc(p[i]) == Player.Name) { Injury = Int(p, i + 2) });
        }
        ItemTargetRequested?.Invoke(list);
    }

    public void Craft(int skillSlot, int tech, int recipeId, IEnumerable<int> bagSlots) =>
        Send("sM", skillSlot.ToString(), tech.ToString(), "-1", string.Join("|", new[] { recipeId }.Concat(bagSlots)));

    private void OnRecipes(string[] f)
    {
        if (f.Length < 1 || !int.TryParse(f[0], out var slot)) return;
        var a = string.Join(' ', f.Skip(1)).Split('|');
        var list = new List<RecipeInfo>();
        for (int i = 0; i + 21 < a.Length; i += 22)
        {
            if (!int.TryParse(a[i], out var id) || id < 0) continue;
            var r = new RecipeInfo { Id = id, Level = Int(a, i + 1), Image = Int(a, i + 2), Name = Unesc(a[i + 3]), Desc = Unesc(a[i + 4]), Mp = Int(a, i + 5), Ok = a[i + 6] == "1" };
            for (int m = 0; m < 5; m++)
                if (int.TryParse(a[i + 7 + 3 * m], out var mid) && mid >= 0) r.Materials.Add((mid, Unesc(a[i + 8 + 3 * m]), Int(a, i + 9 + 3 * m)));
            list.Add(r);
        }
        var sk = Player.Skills.FirstOrDefault(x => x.Slot == slot);
        if (sk != null) sk.Recipes = list;
        RecipesChanged?.Invoke(slot);
    }

    private void OnProduce(string[] f)
    {
        if (f.Length < 2) return;
        bool ok = f[0] == "1"; int type = int.TryParse(f[1], out var t) ? t : 0;
        var p = string.Join(' ', f.Skip(2)).Split('|');
        int exp = Int(p, 0);
        ProduceResult r;
        if (type == 4 || p.Length < 9)
        {
            // 采集：…|图像|名字|0；失败 图像 -1、没有名字
            int ni = Array.FindIndex(p, 1, x => x.Length > 0 && !long.TryParse(x, out _));
            r = new ProduceResult(ok, type, exp, ni > 0 ? Int(p, ni - 1) : -1, ni > 0 ? Unesc(p[ni]) : "", Array.Empty<string>());
        }
        else r = new ProduceResult(ok, type, exp, Int(p, 7), "", p.Skip(8).Where(x => x.Trim().Length > 0).Select(Unesc).ToList());
        if (type == 3) r = r with { Heal = Int(p, 6) };   // 急救：JgO 1 3 经验|0|0|0|0|0|回复量
        ProduceDone?.Invoke(r);
    }

    // ———————————————————— 称号（官方 bob 实测）————————————————————
    // 状态窗口「稱號」页：点一行 = zGp 序号（ST，选为特殊称号）；解除 = zGp -1；删除 = tj 序号（DT）；
    // 玩家称号输入框 + 設定 = ACwB 文字（FT）。服务端回 aPc（第 28 项选中序号、第 30 项玩家称号）和 Sc。
    public IReadOnlyList<string> Titles { get; private set; } = Array.Empty<string>();
    public int TitleIndex { get; private set; } = -1;
    public string FreeTitle { get; private set; } = "";
    public string SpecialTitle => TitleIndex >= 0 && TitleIndex < Titles.Count ? Titles[TitleIndex] : "";
    public void SelectTitle(int i) => Send("zGp", i.ToString());
    public void DeleteTitle(int i) => Send("tj", i.ToString());
    public void SetFreeTitle(string text) => Send("ACwB", text.Replace("\\", "\\\\").Replace(" ", "\\S"));

    // ———————————————————— 技能导师（官方客户端 bob 实测 2026-11-09）————————————————————
    // 右键对着导师 → BZ 16 入口（DialogPage kind skillmenu）→ 想学习 xD 0 1 → BZ 17 "头像|名|话|技能名|说明|栏位|费用|"
    //   → 学习 xD 0 1 → 服务端 BZ 0 结果（"这个技能已经学过了"/"技能栏位不足"/学会）；不学 = 关窗口。
    // 想忘记 xD 0 2 → BZ 18 "头像|名|话|(名字|等级|栏位)×n|" → 点一行 xD 0 序号(1 起) → BZ 0 是/否确认 → 是 → cNE 清掉那一栏 + "失去了…的技能"。
    public sealed record SkillShopPage(int Type, int Face, string Npc, string Text, string SkillName, string Desc, int Slots, int Cost,
        IReadOnlyList<(string Name, int Level, int Slots)> Known);
    public SkillShopPage? SkillShop { get; private set; }
    public event Action? SkillShopChanged;

    private void OnSkillShop(int type, string[] q)
    {
        string T(int i) => i < q.Length ? Unesc(q[i]).Replace("\\n", "\n") .Trim('\n') : "";
        int N(int i) => i < q.Length && int.TryParse(q[i], out var v) ? v : 0;
        var known = new List<(string, int, int)>();
        if (type == 18) for (int i = 3; i + 2 < q.Length; i += 3) if (q[i].Length > 0) known.Add((T(i), N(i + 1), N(i + 2)));
        SkillShop = type == 17
            ? new SkillShopPage(17, N(0), T(1), T(2), T(3), T(4), N(5), N(6), known)
            : new SkillShopPage(18, N(0), T(1), T(2), "", "", 0, 0, known);
        DialogChanged?.Invoke(null);
        SkillShopChanged?.Invoke();
    }

    /// <summary>学习（type 17）：sel 1。忘记（type 18）：sel = 第几个（1 起）。</summary>
    public void SkillShopChoose(int sel)
    {
        if (_window == null || SkillShop == null) return;
        var w = _window; _window = null; SkillShop = null; SkillShopChanged?.Invoke();
        Send("xD", B62(X), B62(Y), w.Seq, w.ObjId, "0", sel.ToString());
    }
    public void SkillShopClose() { _window = null; SkillShop = null; SkillShopChanged?.Invoke(); }

    // ———————————————————— 银行 ————————————————————

    /// <summary>保险箱里的一件道具（每页 20 格）：图像|数量|?|名字|?|说明。</summary>
    public sealed record BankItem(int Slot, int Image, int Count, string Name, string Desc);
    /// <summary>保险箱里的一只宠物（每页 5 只，每只 35 段）：0 名字(昵称) 1 等级 2 种族? 3 生命 4 最大生命 … 12 原名 13 图像 … 25/26 技能名…，列表上显示原名（官方实测）。</summary>
    public sealed record BankPet(int Slot, string Name, string OrigName, int Anime, string[] Raw);
    public sealed class BankState
    {
        public int Page = 1;
        public readonly BankItem?[] Items = new BankItem?[20];
        public readonly BankPet?[] Pets = new BankPet?[5];
        public long Gold;
        internal string Seq = "", ObjId = "";
    }
    /// <summary>银行窗口开着时不为 null（BZ 类型 23 = "n"）。关窗口不发包（官方实测）。</summary>
    public BankState? Bank { get; private set; }
    public event Action? BankChanged;

    /// <summary>
    /// BZ n 0 序号 对象 数据。数据第一段是种类（官方实测 2026-11-07）：
    ///  A|整页：20 件 × 6 段 + 5 只宠物 × 35 段 + 存款；I|格|6 段：一件变了（空 = 取走）；P|格|35 段：一只宠物；G|存款。
    /// 格子号都是当前页里的（0–19 / 0–4），翻页发 rh 页号，服务端回整页 A。
    /// </summary>
    private void OnBank(string[] f)
    {
        var b = Bank ?? new BankState();
        b.Seq = f[2]; b.ObjId = f[3];
        var q = string.Join(' ', f.Skip(4)).Split('|');
        string S(int i) => i < q.Length ? q[i] : "";
        int N(int i) => int.TryParse(S(i), out var v) ? v : 0;
        BankItem? Item(int slot, int i) => S(i).Length == 0 ? null : new BankItem(slot, N(i), N(i + 1), Unesc(S(i + 3)), S(i + 5));
        BankPet? Pet(int slot, int i) => S(i).Length == 0 && S(i + 12).Length == 0 ? null
            : new BankPet(slot, Unesc(S(i)), Unesc(S(i + 12)), N(i + 13), Enumerable.Range(i, 35).Select(S).ToArray());
        switch (S(0).Trim())
        {
            case "A":
                for (int k = 0; k < 20; k++) b.Items[k] = Item(k, 1 + 6 * k);
                for (int k = 0; k < 5; k++) b.Pets[k] = Pet(k, 121 + 35 * k);
                b.Gold = long.TryParse(S(296).Trim(), out var g) ? g : 0;
                break;
            case "I": if (N(1) is var si and >= 0 and < 20) b.Items[si] = Item(si, 2); break;
            case "P": if (N(1) is var sp and >= 0 and < 5) b.Pets[sp] = Pet(sp, 2); break;
            case "G": b.Gold = long.TryParse(S(1).Trim(), out var g2) ? g2 : b.Gold; break;
        }
        Bank = b;
        BankChanged?.Invoke();
    }

    private void BankSend(string data) { if (Bank is { } b) Send("xD", B62(X), B62(Y), b.Seq, b.ObjId, "0", data); }
    public void BankPage(int page) { if (Bank is { } b && page is >= 1 and <= 9) { b.Page = page; Send("rh", page.ToString()); } }
    /// <summary>存道具：背包格 → 当前页第一个空格（官方：I\\z背包格\\z100+格\\z数量\\z）。</summary>
    public bool BankDeposit(int bagSlot)
    {
        if (Bank is not { } b || Player.Items.FirstOrDefault(i => i.Slot == bagSlot) is not { } it) return false;
        int k = Array.FindIndex(b.Items, x => x == null);
        if (k < 0) return false;
        BankSend($"I\\\\z{bagSlot}\\\\z{100 + k}\\\\z{Math.Max(1, it.Count)}\\\\z");
        return true;
    }
    /// <summary>取道具：当前页某格 → 背包第一个空格（8–27）。</summary>
    public bool BankWithdraw(int k)
    {
        if (Bank is not { } b || k is < 0 or >= 20 || b.Items[k] is not { } it) return false;
        int bag = Enumerable.Range(8, 20).FirstOrDefault(sl => Player.Items.All(i => i.Slot != sl), -1);
        if (bag < 0) return false;
        BankSend($"I\\\\z{100 + k}\\\\z{bag}\\\\z{Math.Max(1, it.Count)}\\\\z");
        return true;
    }
    public bool BankDepositPet(int petSlot)
    {
        if (Bank is not { } b || Player.Pets.All(p => p.Slot != petSlot)) return false;
        int k = Array.FindIndex(b.Pets, x => x == null);
        if (k < 0) return false;
        BankSend($"P\\\\z{petSlot}\\\\z{100 + k}\\\\z");
        return true;
    }
    public bool BankWithdrawPet(int k)
    {
        if (Bank is not { } b || k is < 0 or >= 5 || b.Pets[k] == null) return false;
        int slot = Enumerable.Range(0, 5).FirstOrDefault(sl => Player.Pets.All(p => p.Slot != sl), -1);
        if (slot < 0) return false;
        BankSend($"P\\\\z{100 + k}\\\\z{slot}\\\\z");
        return true;
    }
    /// <summary>存钱 G\\z金额\\z、提钱 D\\z金额\\z（官方实测），服务端回 G|存款 和 aPc（身上的钱）。</summary>
    public void BankGold(bool deposit, long amount) { if (amount > 0) BankSend($"{(deposit ? "G" : "D")}\\\\z{amount}\\\\z"); }
    public void CloseBank() { if (Bank != null) { Bank = null; BankChanged?.Invoke(); } }

    /// <summary>右上「交换名片」（Ctrl+;）：跟面前的人交换。</summary>
    public void ExchangeCard() => Send("vcAz", B62(X), B62(Y));
    public void DeleteCard(int index) => Send("FPkR", index.ToString());
    public void SendMail(int cardIndex, string text)
    {
        if (cardIndex < 0 || cardIndex >= 60 || _cards[cardIndex] == null || text.Length == 0) return;
        Send("CV", cardIndex.ToString(), text.Replace("\\", "\\\\").Replace(" ", "\\S"), "0");
        Mails.Add(new Mail(cardIndex, _cards[cardIndex]!.Name, DateTime.Now.ToString("yyyy/MM/dd HH:mm"), text, false));
    }

    // ———————————————————— 交易 ————————————————————
    // 实测（官方客户端 bob 发起，工具 hao 配合），双方都要开着「易」（FS 位 32）：
    //   面向对方点右上「交易」→ TRPL "ixH" → 服务端回 "Cxo 名字|…"（没人："Zd 没有可交易的对象！"）
    //   → 官方弹"请选择对象"，点名字 → TRS "QeuC 序号" → 自己直接开交易窗口；对方收 "ms bob a"（也开窗口）。
    //   选好东西点「開啓」→ TROP "aiAb 格|0 宠物 金币"（例 "aiAb 8|0  5"：第 8 格 + 5G；没东西 "aiAb   0"）
    //     → 对方收 nUbJ（每件道具，格式同 rU 第一段为序号）、hJq 金币、NdM（已开启）。
    //   「確認交易」→ TROC "OU 1" → 对方收 "We 1"；双方都确认 → 道具/金币对调（rU、aPc），两边 "Zd 交易完成。" + "We 2"。
    //   「取消」→ "OU 0" → 两边 "Zd 交易中止。" + "We 0"。
    //   多件道具的写法还没抓到（官方一次只选上了一件），按 "格|0|格|0" 推测。

    public sealed class TradeState
    {
        public string Partner = "";
        public List<ItemStack> TheirItems = new();
        public int TheirGold;
        /// <summary>对方放上来的宠物（RwK 序号 名字|等级|种族|最大生命|最大魔力|体|力|强|速|魔|忠诚|技能栏|原名|形象|攻|防|敏|精|回|地|水|火|风，实测）+ Yq 序号 招式名…</summary>
        public SortedDictionary<int, PetInfo> TheirPets = new();
        public bool TheyOpened, TheyConfirmed, IOpened, IConfirmed;
    }
    public TradeState? Trade { get; private set; }
    public IReadOnlyList<string> TradeCandidates { get; private set; } = Array.Empty<string>();
    public event Action<IReadOnlyList<string>>? TradeCandidatesReceived;
    public event Action? TradeChanged;
    /// <summary>交易结束：true = 完成，false = 中止。</summary>
    public event Action<bool>? TradeEnded;

    /// <summary>右上「交易」按钮（Ctrl+'）：问服务端面前有谁能交易。</summary>
    public void RequestTrade() => Send("ixH");
    /// <summary>右上「对战」（Ctrl+J，官方实测 "ik x y"）：跟面前的玩家 PK；对方没开「戰」→ "dAgg 0 0" + "Zd 突发战斗失败"。</summary>
    public void Duel() => Send("ik", B62(X), B62(Y));
    /// <summary>右上「观战」（Ctrl+K，"IMv x y"）：看面前正在战斗的人（头上有 Battle 标志，NI 动作 40）。不让看时服务端回 "本区域不允许观战。"</summary>
    public void Watch() => Send("IMv", B62(X), B62(Y));
    private readonly HashSet<int> _fighting = new();
    /// <summary>这个对象正在战斗（NI 动作 40，官方头上显示 Battle）。</summary>
    public bool IsFighting(int id) => _fighting.Contains(id);
    public event Action? FightingChanged;
    /// <summary>"请选择对象"里点了第 i 个。</summary>
    public void SelectTradeTarget(int i)
    {
        if (i < 0 || i >= TradeCandidates.Count) return;
        Send("QeuC", i.ToString());
        Trade = new TradeState { Partner = TradeCandidates[i] };
        TradeChanged?.Invoke();
    }
    /// <summary>
    /// 「開啓」：亮出自己要给的东西。官方 exe 的格式（%d|%d 用 | 连起来）：道具 = "背包格|序号|背包格|序号…"，宠物 = "宠物栏|序号…"，序号从 0 数（实测 "aiAb  1|0 0" 给出 1 号栏宠物）。
    /// </summary>
    public void OpenTrade(IEnumerable<int> slots, int gold, IEnumerable<int>? pets = null)
    {
        if (Trade == null) return;
        Send("aiAb", string.Join("|", slots.Select((x, k) => $"{x}|{k}")), string.Join("|", (pets ?? Array.Empty<int>()).Select((x, k) => $"{x}|{k}")), gold.ToString());
        Trade.IOpened = true; TradeChanged?.Invoke();
    }
    public void ConfirmTrade() { if (Trade == null) return; Send("OU", "1"); Trade.IConfirmed = true; TradeChanged?.Invoke(); }
    public void CancelTrade() { if (Trade == null) return; Send("OU", "0"); }

    // ———————————————————— 组队 ————————————————————
    // 实测（官方客户端 bob + 工具 hao）：
    //   加入：面向对方，发 PR = "zn 自己x 自己y 1"；对方没开「隊」开关 → "Zd 没办法加入队伍。" + "bNY 1 0"。
    //   成功：两边都收 JJQj 0 队长、JJQj 1 新队员（"1|对象号十进制l等级|最大HP|HP|MP|名字|"）、Zd 提示、TFCu 0（音效）、TNT 全队状态、bNY 1 1。
    //   离开：发 "zn x y 0" → 自己收 Zd、JJQj 1 0|、bNY 0 1；其他人收 Zd "bob\S离开了队伍。"、JJQj 1 0|。
    //   队员不单独发位置：队长走一步，服务端把队员挪到前一个人刚离开的格子（队员只收到地图窗口 jmb 更新）。

    public sealed record PartyMember(int Index, string ObjId, int Level, string Name, int Hp, int MaxHp, int Mp, int MaxMp);
    private readonly SortedDictionary<int, PartyMember> _party = new();
    public IReadOnlyList<PartyMember> Party => _party.Values.ToList();
    public bool InParty => _party.Count >= 2 && _party.Values.Any(m => m.ObjId == SelfId);
    public bool IsPartyLeader => InParty && _party.TryGetValue(0, out var l) && l.ObjId == SelfId;
    public event Action? PartyChanged;
    /// <summary>FS 开关位（左上面板）：1 隊（允许别人加入）、4 戰、8 聊、16 名（名片）、32 易（交易）。官方实测。</summary>
    public int Flags { get; private set; }
    public event Action? FlagsChanged;
    public void SetFlags(int flags) => Send("dcMv", B62(flags));
    public void ToggleFlag(int bit) => SetFlags(Flags ^ bit);

    /// <summary>要求加入面前的人的队伍（官方右上「组队」按钮，Ctrl+L）；已在队里就是离开。</summary>
    public void PartyButton() => Send("zn", B62(X), B62(Y), InParty ? "0" : "1");

    /// <summary>自己在队里排在谁后面（队长走 → 自己跟着走到他刚离开的格子）。</summary>
    private string? FollowTarget
    {
        get
        {
            if (!InParty || IsPartyLeader) return null;
            var me = _party.Values.FirstOrDefault(m => m.ObjId == SelfId);
            if (me == null) return null;
            return _party.Values.Where(m => m.Index < me.Index).OrderByDescending(m => m.Index).FirstOrDefault()?.ObjId;
        }
    }

    private void Follow(int x, int y)
    {
        if (InBattle) return;
        int dx = Math.Sign(x - X), dy = Math.Sign(y - Y);
        int dir = Array.IndexOf(Directions.Deltas, (dx, dy));
        X = x; Y = y; Player.X = X; Player.Y = Y;
        Moved?.Invoke(X, Y, Math.Max(0, dir));
    }

    private void OnPartyMember(string[] f)
    {
        if (f.Length < 2 || !int.TryParse(f[0], out var idx)) return;
        var p = string.Join(' ', f.Skip(1)).Split('|');
        if (p.Length < 6 || p[0] != "1") _party.Remove(idx);
        else
        {
            var idLv = p[1].Split('l');
            string obj = long.TryParse(idLv[0], out var id) ? B62(id) : idLv[0];
            int lv = idLv.Length > 1 && int.TryParse(idLv[1], out var l) ? l : 0;
            int I(int i) => int.TryParse(p[i], out var v) ? v : 0;
            _party[idx] = new PartyMember(idx, obj, lv, Unesc(p[5]), I(3), I(2), I(4), _party.TryGetValue(idx, out var old) && old.ObjId == obj ? old.MaxMp : 0);
        }
        PartyChanged?.Invoke();
    }

    private void OnPartyParam(string s)
    {
        var p = s.Split('|');
        for (int i = 0; i + 4 < p.Length; i += 5)
        {
            var m = _party.Values.FirstOrDefault(x => x.ObjId == p[i]);
            if (m == null) continue;
            int I(int k, int def) => int.TryParse(p[i + k], out var v) ? v : def;
            _party[m.Index] = m with { Hp = I(1, m.Hp), MaxHp = I(2, m.MaxHp), Mp = I(3, m.Mp), MaxMp = I(4, m.MaxMp) };
        }
        PartyChanged?.Invoke();
    }

    /// <summary>
    /// CP（aPc）人物状态。第一个字段是位掩码（62 进制，可能带负号 = 32 位有符号）：
    /// "1" = 完整（后面依次是第 1…31 项）；否则每个置位的第 i 位后面跟一个值（按位从低到高）。
    /// 实测各项：1 HP、2 最大HP、3 MP、4 最大MP、5–9 体力/力量/强度/速度/魔法、15 等级、27 金钱、29 名字
    /// （用 GM 脚本改值对照得出；其余项含义待核对）。
    /// </summary>
    private readonly string[] _cp = new string[32];
    private void OnCharParam(string s)
    {
        var p = s.Split('|');
        if (p.Length < 2) return;
        if (p[0] == "1") { for (int i = 1; i < 32 && i < p.Length; i++) _cp[i] = p[i]; }
        else
        {
            uint mask = (uint)D62Safe(p[0]); int k = 1;
            for (int bit = 1; bit < 32 && k < p.Length; bit++) if ((mask & (1u << bit)) != 0) _cp[bit] = p[k++];
        }
        int I(int i, int def) => int.TryParse(_cp[i], out var v) ? v : def;
        Player.Hp = I(1, Player.Hp); Player.MaxHp = I(2, Player.MaxHp);
        Player.Mp = I(3, Player.Mp); Player.MaxMp = I(4, Player.MaxMp);
        Player.Level = I(15, Player.Level);
        Player.Gold = I(27, (int)Player.Gold);
        Player.Exp = I(13, (int)Player.Exp);
        var st = Player.Stats;
        st.Vit = I(5, st.Vit); st.Str = I(6, st.Str); st.Tgh = I(7, st.Tgh); st.Spd = I(8, st.Spd); st.Mag = I(9, st.Mag);
        st.Endurance = I(10, st.Endurance); st.Dex = I(11, st.Dex); st.Int = I(12, st.Int);
        st.NextExp = I(14, (int)st.NextExp);
        TitleIndex = I(28, TitleIndex);                       // 选中的特殊称号（Sc 列表的序号，-1 没选；官方实测）
        if (_cp[30] != null) FreeTitle = Unesc(_cp[30]);       // 玩家称号（自己写的）
        st.Attack = I(16, st.Attack); st.Defense = I(17, st.Defense); st.Agility = I(18, st.Agility);
        st.Spirit = I(19, st.Spirit); st.Recovery = I(20, st.Recovery); st.Charm = I(21, st.Charm);
        for (int e = 0; e < 4; e++) st.Element[e] = I(23 + e, st.Element[e]);
        if (!string.IsNullOrEmpty(_cp[29])) Player.Name = Unesc(_cp[29]);
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// I（rU）道具：每格 14 段：格子|名字|?|说明|图像|?|?|?|?|?|道具编号|?|数量|（空）
    /// 格子 0–7 是装备栏（7 = 水晶），8 起是背包；名字为空 = 这格清空。数量 0 = 不可堆叠的单件。
    /// </summary>
    private readonly SortedDictionary<int, ItemStack> _slots = new();
    /// <summary>一个道具（rU / nUbJ 共用格式）：格|名字|?|说明|图|…|道具编号(第10段)|?|数量(第12段)|…</summary>
    private ItemStack? ParseItem(string[] p, int i)
    {
        if (i + 12 >= p.Length || !int.TryParse(p[i], out var slot)) return null;
        if (p[i + 1].Length == 0 || !int.TryParse(p[i + 4], out var gfx) || !int.TryParse(p[i + 10], out var itemId)) return null;
        int count = int.TryParse(p[i + 12], out var c) && c > 0 ? c : 1;
        return new ItemStack { ItemId = itemId, Count = count, Slot = slot, Name = Unesc(p[i + 1]), Desc = p[i + 3], Graphic = gfx, Use = int.TryParse(p[i + 6], out var u) ? u : 0, Level = int.TryParse(p[i + 8], out var lv) ? lv : 0 };
    }

    private void OnItems(string s)
    {
        var p = s.Split('|');
        for (int i = 0; i + 12 < p.Length; i += 14)
        {
            if (!int.TryParse(p[i], out var slot)) continue;
            if (ParseItem(p, i) is not { } it) { _slots.Remove(slot); continue; }
            _slots[slot] = it;
            _items[it.ItemId] = new Item(it.ItemId, it.Graphic, 0, System.Text.Encoding.Latin1.GetString(CgPort.Core.Text.Gbk.Encode(it.Name)));
            _names[it.ItemId] = it.Name;
        }
        Player.Items = _slots.Values.Where(x => x.Slot >= 8).ToList();
        Player.Equipment = _slots.Values.Where(x => x.Slot is >= 0 and < 8).ToList();
        StatusChanged?.Invoke();
    }
    private readonly Dictionary<int, string> _names = new();
    public string? ItemName(int id) => _names.TryGetValue(id, out var n) ? n : null;

    // ———————————————————— 战斗 ————————————————————

    private void OnBattle(string s)
    {
        if (s.StartsWith("C|")) OnUnits(s[2..]);
        else if (s.StartsWith("A|")) OnCommandRequest(s[2..]);
        else if (s.StartsWith("M|")) _lastMovie = s[2..];
    }
    private string _lastMovie = "";
    private readonly HashSet<int> _down = new();

    private void OnUnits(string body)
    {
        var f = body.Split('|');
        // 第 1 段是效果标志；之后每个单位 12 段：位置|名字|形象|?|等级|HP|最大HP|MP|最大MP|状态|?|?（数字 16 进制，同 CGAssistant 的解析）
        var units = new List<UnitView>();
        for (int i = 1; i + 11 < f.Length; i += 12)
        {
            int pos = Hex(f[i]);
            if (pos < 0 || pos > 19 || f[i].Length == 0) break;
            int hp = Hex(f[i + 5]);
            units.Add(new UnitView(pos, pos >= 10 ? 1 : 0, Hex(f[i + 4]), hp, Hex(f[i + 6]), Hex(f[i + 2]), hp > 0, false, Unesc(f[i + 1])));
        }
        if (units.Count == 0)
        {
            // "C|20" 这类：单位列表为空 → 战斗结束。先把最后一个回合的动作播出来，再按"敌人是否全倒"判断胜负
            if (InBattle)
            {
                _turn++;
                var last = EventsFrom(_units, _units);
                _units = _units.Select(u => _down.Contains(u.Id) ? u with { Hp = 0, Alive = false } : u).ToList();
                TurnResolved?.Invoke(last, View());
                bool enemiesDown = _units.Where(u => u.Side == 1).All(u => !u.Alive);
                bool meDown = _units.Where(u => u.Side == 0).All(u => !u.Alive);
                EndBattle(enemiesDown ? BattleOutcome.Win : meDown ? BattleOutcome.Lose : BattleOutcome.Escaped);
            }
            return;
        }
        var before = _units; _units = units;
        if (!_battleShown) { _battleShown = true; BattleStarted?.Invoke(View()); return; }
        _turn++;
        TurnResolved?.Invoke(EventsFrom(before, units), View());
        if (units.Where(u => u.Side == 1).All(u => !u.Alive)) EndBattle(BattleOutcome.Win);
        else if (units.Where(u => u.Side == 0).All(u => !u.Alive)) EndBattle(BattleOutcome.Lose);
    }

    /// <summary>从动作脚本（M|…）里取出"谁打了谁、多少伤害"；解析不出来时按血量变化补。</summary>
    /// <summary>解析战斗动作脚本里的 SKL 段（格式见里面的注释）。down：倒下的单位会加进去。</summary>
    public static List<BattleEvent> ParseMovie(string movie, int turn, HashSet<int> down)
    {
        var ev = new List<BattleEvent>();
        int _turn = turn; var _down = down;
        var tok = movie.Split('|');
        // SKL 段（官方实测，2026-11-04）：SKL|s招式|f效果|i..|l..|p..| 之后三种写法：
        //   普攻    a0|a行动者|d目标|标志|伤害|…              （例 a0|aA|d5|1|23）
        //   物理技  w0|a行动者|d目标|f标志|伤害|（a…|d…|f…|伤害 每一下重复）  （连击 w0|a5|dA|f1|3|a5|dA|f1|3）
        //   魔法    0|a施法者|目标|f标志|d数值|（目标|f|d 每个目标重复）     （陨石 0|a5|A|f1|d41；补血 0|a5|5|f0|d3E）
        // 标志位 0x20 = 目标倒下。目标和施法者同一边 = 回复。
        // ITM 段（官方实测，2026-11-07）：
        //   用药   ITM|e0|r使用者|0|目标|d回复量|       （例 ITM|e0|r5|0|5|d48）
        //   封印卡 ITM|e6|a使用者|d目标|结果|           （结果 1 = 抓到，敌人被收走；0 = 没抓到）
        int i = 0;
        while (i < tok.Length)
        {
            if (tok[i] == "ITM" && i + 1 < tok.Length)
            {
                var kind = tok[i + 1];
                if (kind == "e0" && i + 5 < tok.Length && tok[i + 2].StartsWith('r') && tok[i + 5].StartsWith('d'))
                {
                    int user = Hex(tok[i + 2][1..]), tg = Hex(tok[i + 4]), amt = Hex(tok[i + 5][1..]);
                    ev.Add(new BattleEvent(_turn, user, CommandKind.Item, tg, amt, false, false, false, Heal: true));
                    i += 6; continue;
                }
                if (kind == "e6" && i + 4 < tok.Length && tok[i + 2].StartsWith('a') && tok[i + 3].StartsWith('d'))
                {
                    int user = Hex(tok[i + 2][1..]), tg = Hex(tok[i + 3][1..]);
                    bool ok = tok[i + 4] == "1";
                    if (ok) _down.Add(tg);
                    ev.Add(new BattleEvent(_turn, user, CommandKind.Item, tg, 0, !ok, ok, false, Sealed: ok));
                    i += 5; continue;
                }
                i += 2; continue;
            }
            if (tok[i] != "SKL") { i++; continue; }
            int j = i + 1;
            int tech = -1;
            while (j < tok.Length && tok[j] != "SKL" && tok[j] != "ACT" && tok[j] != "END" && tok[j] != "CHT" && !(tok[j].Length > 0 && !char.IsLetter(tok[j][0]) && tok[j] != "FF"))
            {
                if (tok[j].StartsWith('s') && tok[j].Length > 1) tech = Hex(tok[j][1..]);
                if (tok[j].StartsWith('p')) { j++; break; }
                j++;
            }
            int end = j; while (end < tok.Length && tok[end] != "FF" && tok[end] != "SKL" && tok[end] != "ACT" && tok[end] != "END") end++;
            var body = tok[j..end];
            i = end + 1;
            if (body.Length == 0) continue;
            int Num(string t) => Hex(t.TrimStart('a', 'd', 'f', 'w'));
            if (body[0] == "0" && body.Length >= 5)
            {
                // 魔法：a施法者，然后 (目标, f标志, d数值) 重复
                int actor = Num(body[1]);
                for (int k = 2; k + 2 < body.Length + 1 && k + 2 <= body.Length - 1; k += 3)
                {
                    int tg = Num(body[k]); long fl = Num(body[k + 1]); int val = Num(body[k + 2]);
                    bool heal = (actor < 10) == (tg < 10);
                    bool killed = (fl & 0x20) != 0; if (killed) _down.Add(tg);
                    ev.Add(new BattleEvent(_turn, actor, CommandKind.Skill, tg, val, val == 0 && !heal, killed, false, tech, true, heal));
                }
            }
            else
            {
                // 普攻（a0 开头）/ 物理技（w0 开头）：跳过开头一段，然后 (a行动者, d目标, 标志, 伤害) 重复
                for (int k = 1; k + 3 < body.Length + 1 && k + 3 <= body.Length; k += 4)
                {
                    if (!body[k].StartsWith('a') || !body[k + 1].StartsWith('d')) break;
                    int actor = Num(body[k]), tg = Num(body[k + 1]); long fl = Num(body[k + 2]); int dmg = Num(body[k + 3]);
                    bool killed = (fl & 0x20) != 0; if (killed) _down.Add(tg);
                    ev.Add(new BattleEvent(_turn, actor, tech is 7300 or -1 ? CommandKind.Attack : CommandKind.Skill, tg, dmg, dmg == 0, killed, false, tech));
                }
            }
        }
        return ev;
    }

    private List<BattleEvent> EventsFrom(List<UnitView> before, List<UnitView> after)
    {
        var ev = new List<BattleEvent>();
        ev.AddRange(ParseMovie(_lastMovie, _turn, _down));
        if (ev.Count == 0)
            foreach (var u in after)
            {
                var b = before.FirstOrDefault(x => x.Id == u.Id);
                if (b == null || b.Hp <= u.Hp) continue;
                var attacker = after.FirstOrDefault(x => x.Side != u.Side && x.Alive)?.Id ?? -1;
                ev.Add(new BattleEvent(_turn, attacker, CommandKind.Attack, u.Id, b.Hp - u.Hp, false, b.Alive && !u.Alive, false));
            }
        _lastMovie = "";
        return ev;
    }

    /// <summary>
    /// A|<标志>|<回合>|：服务端要指令。没带宠物时每回合要两次（人物有两个行动：官方面板先显示"指令1"再"指令2"），
    /// 带宠物时第二次是宠物的。CommandRequested 的参数是这是本回合第几次（0/1），界面据此换面板。
    /// </summary>
    private void OnCommandRequest(string body)
    {
        var f = body.Split('|');
        int turn = f.Length > 1 && int.TryParse(f[1], out var t) ? t : 0;
        if (turn != _lastTurnAnswered) { _lastTurnAnswered = turn; _answeredThisTurn = 0; }
        // 标志（16 进制，实测）：没带宠物 8000 → 8001（人物两次行动）；带宠物 400（人物）→ 420（宠物，0x20）→ 421（都下完）
        int flags = Hex(f[0]);
        // 宠物站在人物前一排（位置 − 5，实测人物 5、宠物 0）。服务端在宠物全设为待命后仍可能发 400/420（"战宠"字段没清），
        // 这时战场上并没有宠物，服务端会自己跳过宠物指令——客户端也不能显示宠物面板（实测）。
        // 用过「位置」（Rg P）前后排对调后，人物 0、宠物 5（实测）：宠物位置 = 人物位置 ± 5
        int me = PlayerUnitId();
        bool petHere = _units.Any(u => u.Side == 0 && u.Id == PetPos(me) && u.Alive);
        // 低位（实测）：0x20 = 人物下完、等宠物；0x01 = 人物用"宠物"指令（叫出/收回）下完、等宠物（401）；两个都有（421）= 都下完了
        int low = flags & 0x21;
        HasBattlePet = (flags & 0x400) != 0 && petHere;
        PetTurn = HasBattlePet && (low == 0x20 || low == 0x01);
        if (HasBattlePet && low == 0x21) return;
        if ((flags & 0x20) != 0 && !petHere) return;
        _awaiting = true;
        CommandSlot = PetTurn ? 1 : _answeredThisTurn;
        if (_pending != null) { var c = _pending; _pending = null; SendCommand(c); }
        else CommandRequested?.Invoke(CommandSlot);
    }
    /// <summary>现在要的是宠物的指令（A| 标志 0x20）。</summary>
    public bool PetTurn { get; private set; }
    /// <summary>战场上我方有宠物（站在人物前一排，位置 = 人物 − 5）。</summary>
    public bool PetOnField { get { int me = PlayerUnitId(); return InBattle && _units.Any(u => u.Side == 0 && u.Id == PetPos(me) && u.Alive); } }
    /// <summary>自己宠物的站位：和人物同一列、另一排（人物 5 → 宠物 0；对调后人物 0 → 宠物 5）。</summary>
    private static int PetPos(int me) => me >= 5 ? me - 5 : me + 5;
    /// <summary>本场战斗带着宠物（A| 标志 0x400）：人物只下一次指令，第二次是宠物的。</summary>
    public bool HasBattlePet { get; private set; }
    /// <summary>服务端正在等第几个指令（0 = 指令1，1 = 指令2）；-1 = 没在等。</summary>
    public int CommandSlot { get; private set; } = -1;
    /// <summary>服务端时间 − 本机时间（秒），游戏内昼夜用。</summary>
    public long ServerTimeOffset { get; private set; }
    /// <summary>自己角色的形象编号（服务端 C 包里自己那一条的第 6 段，例如 106002）；0 = 还不知道。</summary>
    public int SelfAppearance { get; private set; }
    public event Action<int>? CommandRequested;
    private bool _awaiting;
    private Command? _lastCommand;

    private void SendCommand(Command c)
    {
        _awaiting = false; _answeredThisTurn++; _lastCommand = c; CommandSlot = -1;
        switch (c.Kind)
        {
            case CommandKind.Attack:
                int target = c.TargetId >= 0 ? c.TargetId : _units.FirstOrDefault(u => u.Side == 1 && u.Alive)?.Id ?? 10;
                Send("Rg", "H|" + target.ToString("X"));
                break;
            case CommandKind.Guard: Send("Rg", "G"); break;
            case CommandKind.Escape: Send("Rg", "E"); break;
            case CommandKind.Item:    // I|格子|目标（16 进制，同 CGAssistant BattleUseItem）
                Send("Rg", $"I|{c.Arg:X}|{(c.TargetId >= 0 ? c.TargetId : PlayerUnitId()):X}"); break;
            case CommandKind.Skill:   // S|技能栏|第几招|目标（官方实测：气功弹 LV1 打 A 号位 = "S|1|0|A"）
                Send("Rg", $"S|{c.Arg:X}|{c.Level:X}|{Math.Max(0, c.TargetId):X}"); break;
            case CommandKind.Weapon:     // 换武器：Q|格子|，卸下 Q|FF|（官方实测 "Rg Q|15|"、"Rg Q|FF|"）
                Send("Rg", c.Arg < 0 ? "Q|FF|" : $"Q|{c.Arg:X}|"); break;
            case CommandKind.Position:   // 前后排对调（官方实测 "Rg P"，没有窗口，直接生效）
                Send("Rg", "P"); break;
            case CommandKind.Summon:     // 宠物：M|宠物栏 = 叫出，M|FF = 收回（官方实测 "Rg M|1"、"Rg M|FF"）
                Send("Rg", c.Arg < 0 ? "M|FF" : $"M|{c.Arg:X}"); break;
            case CommandKind.PetSkill:   // W|宠物技能序号|目标（官方实测：使魔"攻击"打 A 号位 = "W|0|A"）
                Send("Rg", $"W|{c.Arg:X}|{Math.Max(0, c.TargetId):X}"); break;
            default: Send("Rg", "N"); break;
        }
    }

    public void SubmitCommand(Command command)
    {
        if (!InBattle) return;
        if (_awaiting) SendCommand(command); else _pending = command;
    }

    private void EndBattle(BattleOutcome o)
    {
        if (!InBattle) return;
        InBattle = false; _pending = null; _lastCommand = null;
        BattleEnded?.Invoke(o);
    }

    private BattleView View() => new(_turn, _units.ToList(), PlayerUnitId());
    /// <summary>战斗里"我"是哪个位置：按名字找（带宠物时宠物在 0 号位、人物在 5 号位，不能取第一个我方单位）。</summary>
    private int PlayerUnitId() =>
        (_units.FirstOrDefault(u => u.Side == 0 && u.Name == Player.Name) ?? _units.Where(u => u.Side == 0).OrderByDescending(u => u.Id >= 5).FirstOrDefault())?.Id ?? 0;
    /// <summary>当前战斗的单位（测试和界面用）。</summary>
    public BattleView CurrentBattle => View();

    // ———————————————————— 行走与其它 ————————————————————

    private static readonly char[] DirLetters = { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h' };   // 与 Directions.Deltas 顺序一致

    public MoveResult Step(int dir)
    {
        if (!InGame || InBattle || dir < 0 || dir > 7) return MoveResult.InvalidStep;
        if (InParty && !IsPartyLeader) return MoveResult.Blocked;   // 队员跟着队长走，自己不能走（要先离队）
        Send("zA", B62(X), B62(Y), DirLetters[dir].ToString(), _token);
        var (dx, dy) = Directions.Deltas[dir];
        X += dx; Y += dy; Player.X = X; Player.Y = Y;   // 本地预测；服务端不同意会发 QBt 校正
        Moved?.Invoke(X, Y, dir);
        return MoveResult.Ok;
    }

    /// <summary>登出（CharLogout = WA）：通知服务端后断开。</summary>
    public void Logout() { if (InGame) Send("WA"); InGame = false; try { _stream?.Close(); } catch { } }

    /// <summary>说话（聊天框）。空格按协议转义成 \S。</summary>
    public void Say(string text) => Send("uSr", B62(X), B62(Y), "P|" + text.Replace(" ", "\\S"), "5", "5", "1");   // 官方实测 "uSr x y P|hello123 5 5 1"：颜色 5（绿，聊天设定默认）

    public IReadOnlyList<Npc> NpcsOnCurrentMap() => _objects.Values.ToList();
    /// <summary>
    /// 和 NPC 说话：原版是"转身面向相邻的 NPC"就触发（CGAssistant 的脚本也是用 TurnTo 跟 NPC 对话）。
    /// 发的是只转向的走路包：zA x y <大写方向字母> 令牌。服务端随后发 BZ 窗口。
    /// </summary>
    /// <summary>原地转向（dx,dy 各 −1..1）：只转向的走路包。组队、交易这些都是对"面前的人"做的。</summary>
    public void Face(int dx, int dy)
    {
        int dir = Array.IndexOf(Directions.Deltas, (Math.Sign(dx), Math.Sign(dy)));
        if (dir < 0 || !InGame) return;
        Send("zA", B62(X), B62(Y), char.ToUpperInvariant(DirLetters[dir]).ToString(), _token);
    }

    public bool TalkTo(int npcId)
    {
        var n = _objects.Values.FirstOrDefault(o => o.Id == npcId);
        if (n == null || !InGame || InBattle) return false;
        int dx = Math.Sign(n.X - X), dy = Math.Sign(n.Y - Y);
        if (Math.Max(Math.Abs(n.X - X), Math.Abs(n.Y - Y)) > 1 || (dx == 0 && dy == 0)) return false;
        int dir = Array.IndexOf(Directions.Deltas, (dx, dy));
        Send("zA", B62(X), B62(Y), char.ToUpperInvariant(DirLetters[dir]).ToString(), _token);
        return true;
    }

    // ———————————————————— 窗口（WN） ————————————————————
    // BZ <类型> <按钮位> <序号> <窗口对象id62> <内容>；内容 = "<表头行数>|<各行用 \\n 分隔>"（类型 2 = 选择列表）。
    // 回复：xD <x62> <y62> <序号> <窗口对象id> <按钮值> <选中行>（实测：点第 4 个可选行 → "... 0 4"）。
    // ———————————————————— 技能 / 宠物 ————————————————————

    private static int Int(string[] a, int i) => i < a.Length && int.TryParse(a[i], out var v) ? v : 0;

    /// <summary>cNE：每个技能 11 段 栏位|技能编号|名字|等级|经验|?|升级经验|占用栏数|?|?|顺序。名字为空 = 这一栏没有技能。
    /// 登录时发全部 20 栏，学会/升级时只发变化的那一栏（实测）。</summary>
    private void OnSkills(string body)
    {
        var a = body.Split('|');
        for (int i = 0; i + 10 < a.Length; i += 11)
        {
            if (!int.TryParse(a[i], out var slot)) continue;
            var old = Player.Skills.FirstOrDefault(x => x.Slot == slot);
            var name = Unesc(a[i + 2]);
            if (name.Length == 0) { if (old != null) Player.Skills.Remove(old); continue; }
            var sk = old ?? new SkillInfo { Slot = slot };
            sk.SkillId = Int(a, i + 1); sk.Name = name; sk.Level = Int(a, i + 3); sk.Exp = Int(a, i + 4); sk.NextExp = Int(a, i + 6);
            sk.SlotsUsed = Math.Max(1, Int(a, i + 7)); sk.Type = Int(a, i + 8); sk.MaxLevel = Int(a, i + 5); sk.Order = Int(a, i + 10);
            if (old == null) Player.Skills.Add(sk);
        }
        Player.Skills.Sort((x, y) => x.Order != y.Order ? x.Order.CompareTo(y.Order) : x.Slot.CompareTo(y.Slot));
    }

    /// <summary>
    /// Rspn 技能栏 名字|说明|MP|?|?|?|…（每招 6 段，例：气功弹LV1|以赤手空拳来攻击敌人|2|1|4213|1）。
    /// 第 5 段（4213、补血 4189）不是招式编号；真正的招式编号 = 技能编号×100 + 序号（战斗动画 SKL|s 里用的就是它）。
    /// </summary>
    private void OnTechs(string[] f)
    {
        if (f.Length < 2 || !int.TryParse(f[0], out var slot)) return;
        var a = string.Join(' ', f.Skip(1)).Split('|');
        var list = new List<TechInfo>();
        for (int i = 0; i + 5 < a.Length; i += 6)
            if (a[i].Length > 0) list.Add(new TechInfo { Index = list.Count, Name = Unesc(a[i]), Desc = Unesc(a[i + 1]), Mp = Int(a, i + 2), TechId = Int(a, i + 4) /* 见上：不是招式编号 */ });
        var sk = Player.Skills.FirstOrDefault(x => x.Slot == slot);
        if (sk == null) { sk = new SkillInfo { Slot = slot }; Player.Skills.Add(sk); }
        sk.Techs = list;
    }

    /// <summary>IsSy 宠物栏 掩码|值…：掩码 1 = 全部（第 1…25 项依次），否则每个置位的第 i 位跟一个值。
    /// 实测各项：1 形象 2 等级 3 HP 4 最大HP 5 MP 6 最大MP 7 经验 8 升级经验 10 攻击 11 防御 12 敏捷 13 精神 14 回复 15 忠诚 22 名字 23 昵称 24 状态。
    /// 掩码 0 = 这一栏的宠物没了。</summary>
    private void OnPet(string[] f)
    {
        if (f.Length < 2 || !int.TryParse(f[0], out var slot)) return;
        var a = string.Join(' ', f.Skip(1)).Split('|');
        long mask = (uint)D62Safe(a[0]);
        var pet = Player.Pets.FirstOrDefault(p => p.Slot == slot);
        if (mask == 0) { if (pet != null) Player.Pets.Remove(pet); return; }
        if (pet == null) { pet = new PetInfo { Slot = slot }; Player.Pets.Add(pet); Player.Pets.Sort((x, y) => x.Slot.CompareTo(y.Slot)); }
        ApplyPet(pet, a);
    }

    /// <summary>IsSy 的掩码|值…填进一只宠物（摆摊浏览 ft 里也是这个格式）。</summary>
    private static void ApplyPet(PetInfo pet, string[] a)
    {
        long mask = (uint)D62Safe(a[0]);
        var vals = new Dictionary<int, string>();
        if (mask == 1) for (int i = 1; i < a.Length; i++) vals[i] = a[i];
        else { int k = 1; for (int bit = 1; bit < 32; bit++) if (((mask >> bit) & 1) != 0 && k < a.Length) vals[bit] = a[k++]; }
        foreach (var (i, v) in vals)
        {
            long.TryParse(v, out var n);
            switch (i)
            {
                case 1: pet.Anime = (int)n; break;
                case 2: pet.Race = (int)n; break;     // 官方详情"种族"（以前误当成等级）
                case 9: pet.Level = (int)n; break;
                case 3: pet.Hp = (int)n; break;
                case 4: pet.MaxHp = (int)n; break;
                case 5: pet.Mp = (int)n; break;
                case 6: pet.MaxMp = (int)n; break;
                case 7: pet.Exp = n; break;
                case 8: pet.NextExp = n; break;
                case 10: pet.Attack = (int)n; break;
                case 11: pet.Defense = (int)n; break;
                case 12: pet.Agility = (int)n; break;
                case 13: pet.Spirit = (int)n; break;
                case 14: pet.Recovery = (int)n; break;
                case 15: pet.Loyalty = (int)n; break;
                case 16: case 17: case 18: case 19: pet.Element[i - 16] = (int)n; break;
                case 20: pet.SkillSlots = (int)n; break;
                case 22: pet.Name = Unesc(v); break;
                case 23: pet.Nick = Unesc(v); break;
                case 24: pet.State = (int)n; break;
            }
        }
    }

    /// <summary>AU 宠物栏 掩码|值…（同 IsSy 的掩码规则；官方宠物"明细"对照实测，见 PetInfo）。</summary>
    private void OnPetParam(string[] f)
    {
        if (f.Length < 2 || !int.TryParse(f[0], out var slot)) return;
        var pet = Player.Pets.FirstOrDefault(p => p.Slot == slot);
        if (pet == null) return;
        ApplyPetParam(pet, string.Join(' ', f.Skip(1)).Split('|'));
        StatusChanged?.Invoke();
    }

    private static void ApplyPetParam(PetInfo pet, string[] a)
    {
        long mask = (uint)D62Safe(a[0]);
        var vals = new Dictionary<int, string>();
        if (mask == 1) for (int i = 1; i < a.Length; i++) vals[i] = a[i];
        else { int k = 1; for (int bit = 1; bit < 32; bit++) if (((mask >> bit) & 1) != 0 && k < a.Length) vals[bit] = a[k++]; }
        foreach (var (i, v) in vals)
        {
            int.TryParse(v, out var n);
            switch (i)
            {
                case 1: pet.Vit = n; break; case 2: pet.Str = n; break; case 3: pet.Tgh = n; break;
                case 4: pet.Spd = n; break; case 5: pet.Mag = n; break;
                case 6: pet.Critical = n; break; case 7: pet.Counter = n; break; case 8: pet.Hit = n; break; case 9: pet.Dodge = n; break;
                case >= 10 and <= 15: pet.Resist[i - 10] = n; break;
                case 18: pet.LoadMax = n; break;
            }
        }
    }

    /// <summary>iKe 宠物栏 装备格|道具（格式同 rU）。</summary>
    private void OnPetEquip(string[] f)
    {
        if (f.Length < 2 || !int.TryParse(f[0], out var slot)) return;
        var pet = Player.Pets.FirstOrDefault(p => p.Slot == slot);
        if (pet == null) return;
        var p2 = string.Join(' ', f.Skip(1)).Split('|');
        if (!int.TryParse(p2[0], out var es)) return;
        pet.Equipment.RemoveAll(x => x.Slot == es);
        if (ParseItem(p2, 0) is { } it) pet.Equipment.Add(it);
    }

    /// <summary>宠物加点（PLVUP = kjSK 宠物栏 属性）：0 体力 1 力量 2 强度 3 速度 4 魔法。服务端回 IsSy/AU 部分更新 + sA 剩余点数。官方实测。</summary>
    public void AddPetPoint(int petSlot, int which) => Send("kjSK", petSlot.ToString(), which.ToString());
    /// <summary>宠物改名（KN = ycX 宠物栏 新名字）：服务端回 IsSy 只改第 23 项（昵称）。官方实测。</summary>
    public void RenamePet(int petSlot, string name) => Send("ycX", petSlot.ToString(), name.Replace(" ", "\\S"));

    /// <summary>OBee 宠物栏 每招 8 段：序号|招式编号|名字|说明|MP|?|?|?（例：0|7300|攻击|…|0|1|1141|0|1|7400|防御|…）。</summary>
    private void OnPetTechs(string[] f)
    {
        if (f.Length < 2 || !int.TryParse(f[0], out var slot)) return;
        var a = string.Join(' ', f.Skip(1)).Split('|');
        var list = new List<TechInfo>();
        for (int i = 0; i + 7 < a.Length; i += 8)
            if (a[i + 2].Length > 0) list.Add(new TechInfo { Index = Int(a, i), TechId = Int(a, i + 1), Name = Unesc(a[i + 2]), Desc = Unesc(a[i + 3]), Mp = Int(a, i + 4) });
        var pet = Player.Pets.FirstOrDefault(p => p.Slot == slot);
        if (pet == null) { pet = new PetInfo { Slot = slot }; Player.Pets.Add(pet); }
        pet.Techs = list;
    }

    /// <summary>
    /// 设宠物状态（KS = LKQy，实测）：一次发 5 个宠物栏的状态，0 休息 1 待命 2 战斗 3 散步；只发 2 个时服务端改了状态但不设"战宠"，宠物不会出战。
    /// 设某只为战斗时，原来战斗的那只改成待命（同服务端 Char.SetPetDepartureState 的做法）。
    /// </summary>
    public void SetPetState(int slot, int state)
    {
        var states = new int[5];
        foreach (var p in Player.Pets) if (p.Slot is >= 0 and < 5) states[p.Slot] = p.State;
        if (state == PetInfo.Battle) for (int i = 0; i < 5; i++) if (states[i] == PetInfo.Battle) states[i] = PetInfo.Standby;
        if (slot is >= 0 and < 5) states[slot] = state;
        Send("LKQy", states.Select(x => x.ToString()).ToArray());
    }

    // ———————————————————— 道具 / 加点 ————————————————————

    /// <summary>双击道具（ID 包 = Ak，官方实测 "Ak 3U 1C f 0"：坐标、格子 62 进制、0）：背包里的装备 = 穿上，药 = 用，装备栏里的 = 换手。</summary>
    public void UseItem(int slot) { _usingSlot = slot; _treating = false; Send("Ak", B62(X), B62(Y), B62(slot), "0"); }

    // ———— 用道具选对象（官方实测 2026-11-07）————
    // 双击药 → Ak → 服务端 LY（GFLI）→ 客户端弹"请选择对象"（队伍成员，单人就是自己）→ 点了发 mjCv 序号（GPDI）
    // → 服务端 nFV → 客户端弹"请选择使用对象"（那个人 + 他的宠物：健康/等级/姓名/生命力/魔力）→ 点了发 iVfo x y 格子 对象（0 人，宠物 = 栏位+1）。
    private int _usingSlot = -1, _usingMember;
    public sealed record ItemTarget(int Code, string Name, int Level, int Hp, int MaxHp, int Mp, int MaxMp, bool Self) { public int Injury { get; init; } }
    /// <summary>LY：要选队伍里的人（名字列表）。</summary>
    public event Action<IReadOnlyList<string>>? ItemMemberRequested;
    /// <summary>nFV：要选具体对象。</summary>
    public event Action<IReadOnlyList<ItemTarget>>? ItemTargetRequested;
    public IReadOnlyList<string> ItemMembers() => InParty ? _party.Values.Select(m => m.Name).ToList() : new List<string> { Player.Name };
    public void SelectItemMember(int index) { _usingMember = index; Send(_treating ? "McVa" : "mjCv", index.ToString()); }
    public void UseItemOn(int target)
    {
        if (_treating) { _treating = false; Send("sM", _treatSkill.ToString(), _treatTech.ToString(), target.ToString(), ""); return; }
        if (_usingSlot >= 0) Send("iVfo", B62(X), B62(Y), B62(_usingSlot), target.ToString());
    }
    public void CancelTreat() => _treating = false;
    /// <summary>选中的队员是自己：自己 + 自己的宠物；别人：只有他（他的宠物要 nFV 里的数据，格式还没抓到）。</summary>
    public List<ItemTarget> ItemTargets()
    {
        var list = new List<ItemTarget>();
        var members = InParty ? _party.Values.ToList() : null;
        var m = members != null && _usingMember < members.Count ? members[_usingMember] : null;
        if (m == null || m.ObjId == SelfId)
        {
            list.Add(new ItemTarget(0, Player.Name, Player.Level, Player.Hp, Player.MaxHp, Player.Mp, Player.MaxMp, true));
            foreach (var p in Player.Pets.OrderBy(p => p.Slot))
                list.Add(new ItemTarget(p.Slot + 1, p.DisplayName, p.Level, p.Hp, p.MaxHp, p.Mp, p.MaxMp, false));
        }
        else list.Add(new ItemTarget(0, m.Name, m.Level, m.Hp, m.MaxHp, m.Mp, m.MaxMp, false));
        return list;
    }

    /// <summary>移动道具（MI = yi，官方实测 "yi 3 f 0"：从格子、到格子、数量 0 = 整格）。装备栏 ↔ 背包就是穿脱。</summary>
    public void MoveItem(int from, int to, int count = 0) => Send("yi", B62(from), B62(to), count.ToString());

    /// <summary>升级加点（LVUP = IHw，官方实测 "IHw 0" 加体力）：0 体力 1 力量 2 强度 3 速度 4 魔法。服务端回 aPc + kg（剩余点数）。</summary>
    public void AddStatPoint(int which) => Send("IHw", which.ToString());

    private void OnChat(string[] f)
    {
        if (f.Length < 2) return;
        // 文字里的空格已转成 \S，所以按空格切开后：[0] 对象号 [1] P|文字 [2] 颜色 [3] 字号
        var t = f[1];
        int bar = t.IndexOf('|');
        if (bar >= 0) t = t[(bar + 1)..];
        int color = f.Length > 2 && int.TryParse(f[2], out var c) ? c : 0;
        ChatReceived?.Invoke(Unesc(t), color);
    }

    private sealed record Window(int Type, int ButtonMask, string Seq, string ObjId);
    private Window? _window;
    private static readonly (int Bit, string Name)[] WindowButtons = WindowText.Buttons;

    private void OnWindow(string[] f)
    {
        if (f.Length < 5) return;
        int type = D62Safe(f[0]), mask = D62Safe(f[1]);   // 两个都是 62 进制：是/否窗口的按钮掩码是 "c"（= 12 = 是 4 + 否 8）
        if (type == 23) { OnBank(f); return; }   // 银行（保险箱）窗口，见 OnBank
        _window = new Window(type, mask, f[2], f[3]);
        if (type is 17 or 18) { OnSkillShop(type, string.Join(' ', f.Skip(4)).Split('|')); return; }
        var data = string.Join(' ', f.Skip(4));
        int bar = data.IndexOf('|');
        int? header = null;
        if (bar >= 0 && int.TryParse(data[..bar].Replace("\\S", "").Trim(), out var h)) { header = h; data = data[(bar + 1)..]; }
        DialogPage page;
        if (type == 16)
        {
            // 技能导师（Skillmaster）入口：头像|NPC名|话|，选项固定（官方实测）：想学习技能 / 想忘记技能 / 还是算了 → xD … 0 1/2/3
            var q = string.Join(' ', f.Skip(4)).Split('|');
            string T(int i) => i < q.Length ? Unesc(q[i]).Replace("\\n", "\n").Replace("\n", "\n").Trim('\n') : "";
            page = new DialogPage(T(2), new List<string>(), false, new List<string> { "想学习技能", "想忘记技能", "还是算了" })
            { Kind = "skillmenu", NpcName = T(1), Face = int.TryParse(q[0], out var fc) ? fc : 0 };
            DialogChanged?.Invoke(page);
            return;
        }
        if (type is 5 or 6 or 7)
        {
            // 商店：头像|NPC名|话|…（全部用 | 分隔，不带"标题行数"前缀）
            var q = string.Join(' ', f.Skip(4)).Split('|');
            string T(int i) => i < q.Length ? Unesc(q[i]).Replace("\\n", "\n").Trim('\n') : "";
            int N(int i) => i < q.Length && int.TryParse(q[i], out var v) ? v : 0;
            var common = new { Face = N(0), Npc = T(1), Text = T(2) };
            if (type == 5)
                page = new DialogPage(common.Text, new List<string>(), false, new List<string> { "买", "卖", "离开" }) { Kind = "shop" };
            else if (type == 6)
            {
                // 买：…|钱不够的话|背包满的话|然后每件 6 段：名字|图像|单价|说明|?|一次最多几个
                var list = new List<ShopEntry>();
                for (int i = 5; i + 5 < q.Length; i += 6) list.Add(new ShopEntry(T(i), N(i + 1), N(i + 2), T(i + 3), 0, -1, Math.Max(1, N(i + 5))));
                page = new DialogPage(common.Text, new List<string> { "cancel" }, false) { Kind = "buy", Shop = list };
            }
            else
            {
                // 卖：…|然后每件 10 段：名字|数量|图像|价格|格子|?|?|说明|可卖数量|堆叠上限
                // 第 9 段 = 可卖数量（官方客户端实测 2026-11-04：本服药剂师对每件都发 0，官方客户端点▲提示"无法再增加"、卖不了）。
                var list = new List<ShopEntry>();
                for (int i = 3; i + 9 < q.Length; i += 10) list.Add(new ShopEntry(T(i), N(i + 2), N(i + 3), T(i + 7), Math.Max(1, N(i + 1)), N(i + 4), N(i + 8)));
                page = new DialogPage(common.Text, new List<string> { "cancel" }, false) { Kind = "sell", Shop = list };
            }
            page = page with { NpcName = common.Npc, Face = common.Face };
        }
        else
        {
            page = WindowText.Build(type, mask, string.Join("\n", data.Split("\\\\n").Select(Unesc)), header);
        }
        DialogChanged?.Invoke(page);
    }

    public void Answer(string button)
    {
        if (_window == null) return;
        var w = _window; _window = null;
        string sel = "", btn = "0";
        if (button.StartsWith("opt:"))
        {
            // 选项序号：界面从 0 数，协议从 1 数（实测：选第一项发 1；发 0 服务端不理）。商店入口的"离开"直接关掉。
            int n = int.Parse(button[4..]) + 1;
            if (w.Type == 5 && n == 3) { DialogChanged?.Invoke(null); return; }
            if (w.Type == 16 && n == 3) { DialogChanged?.Invoke(null); return; }   // 还是算了
            sel = n.ToString();
        }
        else if (button.StartsWith("buy:") || button.StartsWith("sell:"))
        {
            // 买：序号\\z数量\\z…（每项后面都带分隔符，同 CGAssistant）；卖：格子\\z数量，项之间用 \\z 连接
            var items = button[(button.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':')).ToList();
            sel = button.StartsWith("buy:")
                ? string.Concat(items.Select(x => $"{x[0]}\\\\z{x[1]}\\\\z"))
                : string.Join("\\\\z", items.Select(x => $"{x[0]}\\\\z{x[1]}"));
        }
        else if (button.StartsWith("data:")) sel = button[5..];   // 原样数据（买卖列表等），字段内的 \\z 已由调用方写好
        else btn = B62(WindowButtons.FirstOrDefault(b => b.Name == button).Bit);   // 按钮值也是 62 进制：下一页 32 发 "w"（官方抓包），发 "32" 服务端不理
        Send("xD", B62(X), B62(Y), w.Seq, w.ObjId, btn, sel);
        DialogChanged?.Invoke(null);   // 服务端如果还有下一页，会再发 BZ
    }
    public Item? ItemInfo(int itemId) => _items.TryGetValue(itemId, out var i) ? i : null;
    public void Save(string path) { }   // 联网模式存档在服务端

    public void Dispose()
    {
        _closed = true;
        try { _tcp?.Close(); } catch { }
    }
}
