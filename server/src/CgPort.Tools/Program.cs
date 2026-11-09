using System.Diagnostics;
using CgPort.Core;
using CgPort.Protocol;
using CgPort.Server;

// 命令：
//   validate <游戏根目录> [服务端目录名]   —— 用真实数据检查地图、通行规则、传送图的一致性
//   packets  <日志文件> [headers.json]    —— 统计封包日志里的包头（拿到日志后用）
if (args.Length == 0) { Console.WriteLine("用法: validate <root> [server_dir] | packets <log> [headers.json]"); return 1; }

switch (args[0])
{
    case "validate":
        if (args.Contains("--no-floor-objects")) Walkability.PassableObjectsMakeFloor = false;
        return Validate(args[1], args.Length > 2 && !args[2].StartsWith("--") ? args[2] : FindServerDir(args[1]));
    case "packets": return Packets(args[1], args.Length > 2 ? args[2] : null);
    case "items": return Items(args[1], args.Length > 2 && !args[2].StartsWith("--") ? args[2] : FindServerDir(args[1]));
    case "fight": return Fight(args[1], args.Length > 2 && !args[2].StartsWith("--") ? args[2] : FindServerDir(args[1]));
    case "diag": return Diag(args[1], args.Length > 2 ? args[2] : FindServerDir(args[1]));
    case "scripts": return Scripts(args[1], args.Length > 2 ? args[2] : FindServerDir(args[1]));
    case "detect":
    {
        var r = CgPort.Core.Assets.AssetDetector.Detect(args[1]);
        foreach (var st in r.Sets) Console.WriteLine($"{st.Name,-8} {st.GraphicInfo,-36} {st.Graphic,-30} {st.AnimeInfo ?? "-",-34} 动作头={st.AnimeHeader}");
        Console.WriteLine($"服务端目录={r.ServerDir}  调色板={r.Palette}  备注={string.Join("; ", r.Notes)}");
        return 0;
    }
    case "playtest": return Playtest(args[1], args.Length > 2 && !args[2].StartsWith("--") ? args[2] : FindServerDir(args[1]));
    case "battle": return Battle(args[1], args.Length > 2 && !args[2].StartsWith("--") ? args[2] : FindServerDir(args[1]));
    case "localtalk": return LocalTalk(args[1], args[2]);
    case "pack": return PackGame(args[1], args[2]);
    case "movie":
        foreach (var e in CgPort.Client.NetworkSession.ParseMovie(args[1], 1, new HashSet<int>())) Console.WriteLine(e);
        return 0;
    case "acts":
    {
        var det = CgPort.Core.Assets.AssetDetector.Detect(args[1]);
        using var lib = new CgPort.Core.Assets.AssetLibrary(args[1], det.Sets, det.Palette);
        foreach (var id in args.Skip(2).Select(int.Parse))
        {
            var acts = lib.AnimeActions(id);
            Console.WriteLine($"动画 {id}：" + string.Join("  ", acts.Values.Where(a => a.Dir == 0).OrderBy(a => a.Act).Select(a => $"{a.Act}:{a.Frames.Length}帧/{a.DurationMs}ms")));
        }
        return 0;
    }
    case "actsheet":
    {
        // actsheet <root> <animeId> <dir> <out.raw>：每个动作一行、最多 12 帧，每格 160×160，RGBA 原始数据（宽 1920）
        var det = CgPort.Core.Assets.AssetDetector.Detect(args[1]);
        using var lib = new CgPort.Core.Assets.AssetLibrary(args[1], det.Sets, det.Palette);
        var acts = lib.AnimeActions(int.Parse(args[2])).Values.Where(a => a.Dir == int.Parse(args[3])).OrderBy(a => a.Act).ToList();
        int W = 1920, C = 160, H = C * acts.Count; var buf = new byte[W * H * 4];
        for (int i = 0; i < buf.Length; i += 4) { buf[i] = 90; buf[i + 1] = 110; buf[i + 2] = 90; buf[i + 3] = 255; }
        for (int r = 0; r < acts.Count; r++)
            for (int f = 0; f < Math.Min(12, acts[r].Frames.Length); f++)
            {
                var set = acts[r].Set; if (set == null || !set.TryLocal(acts[r].Frames[f], out var e)) continue;
                var img = set.Decode(e); if (img == null) continue;
                var rgba = img.ToRgba(lib.Palette);
                int ox = f * C + C / 2 + e.OffsetX, oy = r * C + C - 30 + e.OffsetY;
                for (int y = 0; y < img.Height; y++) for (int x = 0; x < img.Width; x++)
                {
                    int sx = ox + x, sy = oy + y; if (sx < f * C || sx >= f * C + C || sy < r * C || sy >= r * C + C) continue;
                    int si = (y * img.Width + x) * 4; if (rgba[si + 3] == 0) continue;
                    int di = (sy * W + sx) * 4; buf[di] = rgba[si]; buf[di + 1] = rgba[si + 1]; buf[di + 2] = rgba[si + 2];
                }
            }
        File.WriteAllBytes(args[4], buf);
        Console.WriteLine($"{W}x{H} acts=" + string.Join(",", acts.Select(a => a.Act)));
        return 0;
    }
    case "animegrid":
    {
        // animegrid <root> <id...|from-to> <out.raw>：每个动画取第一个动作的中间帧，10 列、每格 192×192；输出 RGBA 原始数据并打印 id 顺序
        var det = CgPort.Core.Assets.AssetDetector.Detect(args[1]);
        using var lib = new CgPort.Core.Assets.AssetLibrary(args[1], det.Sets, det.Palette);
        var ids = new List<int>();
        foreach (var t in args[2].Split(','))
            if (t.Contains('-')) { var r = t.Split('-'); for (int k = int.Parse(r[0]); k <= int.Parse(r[1]); k++) if (lib.AnimeActions(k).Count > 0) ids.Add(k); }
            else ids.Add(int.Parse(t));
        int C = 192, cols = 10, rows = (ids.Count + cols - 1) / cols, W = C * cols, H = C * Math.Max(1, rows);
        var buf = new byte[W * H * 4];
        for (int i = 0; i < buf.Length; i += 4) { buf[i] = 40; buf[i + 1] = 40; buf[i + 2] = 50; buf[i + 3] = 255; }
        for (int n = 0; n < ids.Count; n++)
        {
            var acts = lib.AnimeActions(ids[n]); if (acts.Count == 0) continue;
            var a = acts.Values.OrderBy(x => x.Act).ThenBy(x => x.Dir).First();
            if (a.Frames.Length == 0) continue;
            var set = a.Set; if (set == null || !set.TryLocal(a.Frames[a.Frames.Length / 2], out var e)) continue;
            var img = set.Decode(e); if (img == null) continue;
            var rgba = img.ToRgba(lib.Palette);
            int cx = n % cols * C, cy = n / cols * C, ox = cx + C / 2 + e.OffsetX, oy = cy + C * 3 / 4 + e.OffsetY;
            for (int y = 0; y < img.Height; y++) for (int x = 0; x < img.Width; x++)
            {
                int sx = ox + x, sy = oy + y; if (sx < cx || sx >= cx + C || sy < cy || sy >= cy + C) continue;
                int si = (y * img.Width + x) * 4; if (rgba[si + 3] == 0) continue;
                int di = (sy * W + sx) * 4; buf[di] = rgba[si]; buf[di + 1] = rgba[si + 1]; buf[di + 2] = rgba[si + 2];
            }
        }
        File.WriteAllBytes(args[3], buf);
        Console.WriteLine($"{W}x{H}");
        Console.WriteLine(string.Join(",", ids));
        return 0;
    }
    case "bot": return Bot.Run(args.Length > 1 ? args[1] : "127.0.0.1:9030", args.Length > 2 ? args[2] : "test2", args.Length > 3 ? args[3] : "123456");
    case "net": return Net(args.Length > 1 ? args[1] : "127.0.0.1:9030", args.Length > 2 ? args[2] : "test1", args.Length > 3 ? args[3] : null);
    default: Console.WriteLine($"未知命令 {args[0]}"); return 1;
}

static string FindServerDir(string root) =>
    Directory.EnumerateDirectories(root).Select(Path.GetFileName).FirstOrDefault(d => d!.StartsWith('_') && Directory.Exists(Path.Combine(root, d!, "data", "map")))
    ?? throw new DirectoryNotFoundException("找不到服务端目录（含 data/map 的 _开头目录）");

static int Validate(string root, string serverDir)
{
    var sw = Stopwatch.StartNew();
    var tiles = TileInfoTable.LoadDefault(Path.Combine(root, "bin"));
    var data = Path.Combine(root, serverDir, "data");
    var maps = new MapManager(Path.Combine(data, "map"), tiles);
    var warps = WarpTable.Load(Path.Combine(data, "warp.txt"));
    Console.WriteLine($"地块信息 {tiles.Count} 条；服务端地图 {maps.MapIds.Count} 张；传送 {warps.All.Count} 条（跳过 {warps.SkippedLines} 行）；{sw.ElapsedMilliseconds}ms");

    // 1) 全部地图加载 + 通行统计
    int ok = 0, fail = 0; long tilesTotal = 0, walkTotal = 0, missing = 0;
    foreach (var id in maps.MapIds)
    {
        try { var (m, w) = maps.Get(id); ok++; tilesTotal += m.Width * m.Height; walkTotal += w.CountWalkable(); missing += w.MissingGround; }
        catch (Exception e) { fail++; if (fail <= 5) Console.WriteLine($"  地图 {id} 加载失败: {e.Message}"); }
    }
    Console.WriteLine($"地图加载 成功 {ok} 失败 {fail}；可走格占比 {100.0 * walkTotal / Math.Max(1, tilesTotal):F1}%；地面查不到图像的格子 {missing}（{100.0 * missing / Math.Max(1, tilesTotal):F2}%）");

    // 2) 传送一致性
    int srcIn = 0, dstIn = 0, dstWalk = 0, dstMapOk = 0, n = 0;
    foreach (var w in warps.All)
    {
        n++;
        if (maps.Has(w.FromMap) && maps.Get(w.FromMap).Map.InBounds(w.FromX, w.FromY)) srcIn++;
        if (!maps.Has(w.ToMap)) continue;
        dstMapOk++;
        var (m, wk) = maps.Get(w.ToMap);
        if (m.InBounds(w.ToX, w.ToY)) { dstIn++; if (wk.IsWalkable(w.ToX, w.ToY)) dstWalk++; }
    }
    Console.WriteLine($"传送：源点在地图范围内 {Pct(srcIn, n)}；目标地图存在 {Pct(dstMapOk, n)}；目标点在范围内 {Pct(dstIn, n)}；目标点可走 {Pct(dstWalk, dstIn)}");

    // 3) 连通性：从每个可走的传送目标出发，能走到同一地图上的哪些传送源（出口）
    int checkedDst = 0, reachAnyExit = 0;
    var exitsByMap = warps.All.GroupBy(w => w.FromMap).ToDictionary(g => g.Key, g => g.ToList());
    foreach (var w in warps.All)
    {
        if (!maps.Has(w.ToMap) || !exitsByMap.TryGetValue(w.ToMap, out var exits)) continue;
        var (m, wk) = maps.Get(w.ToMap);
        if (!wk.IsWalkable(w.ToX, w.ToY)) continue;
        checkedDst++;
        var reach = wk.Reachable(w.ToX, w.ToY, 200_000);
        bool any = exits.Any(e => e != w && IsNear(reach, m, e.FromX, e.FromY));
        if (any) reachAnyExit++;
        if (checkedDst >= 3000) break;
    }
    Console.WriteLine($"连通性：抽查 {checkedDst} 个传送落点，能走到同图另一个出口的 {Pct(reachAnyExit, checkedDst)}");

    // 4) 模拟：在一张图上随机走动，确认移动校验与传送逻辑能跑通
    var world = new World(maps, warps);
    var rng = new Random(1);
    var start = warps.All.First(w => maps.Has(w.ToMap) && maps.Get(w.ToMap).Walk.IsWalkable(w.ToX, w.ToY));
    var c = world.Spawn(1, "test", start.ToMap, start.ToX, start.ToY);
    int moved = 0, blocked = 0, warped = 0;
    for (int i = 0; i < 20000; i++)
    {
        var r = world.Step(c, rng.Next(8));
        if (r == MoveResult.Ok) moved++; else if (r == MoveResult.Warped) warped++; else blocked++;
    }
    Console.WriteLine($"随机行走 20000 步：移动 {moved}、被挡 {blocked}、传送 {warped}；最后在地图 {c.MapId} ({c.X},{c.Y})");
    Console.WriteLine($"总耗时 {sw.Elapsed.TotalSeconds:F1}s");
    return fail == 0 ? 0 : 2;
}

// 出口格本身常常是门/楼梯（未必可走），只要能走到它相邻的格子就算能到达
static bool IsNear(HashSet<int> reach, MapData m, int x, int y)
{
    for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (m.InBounds(x + dx, y + dy) && reach.Contains(m.Index(x + dx, y + dy))) return true;
    return false;
}

static string Pct(int a, int b) => b == 0 ? "-" : $"{a}/{b} ({100.0 * a / b:F1}%)";

static int Packets(string log, string? headersJson)
{
    var reg = new HeaderRegistry();
    if (headersJson != null && File.Exists(headersJson)) reg.MergeJson(headersJson);
    var lines = Path.GetFileName(log).StartsWith("proto_recv") ? PacketLog.ReadProtoRecv(log) : PacketLog.ReadCgmsvPacketLog(log, reg);
    foreach (var (h, d, cnt, fc) in PacketLog.Summarize(lines))
        Console.WriteLine($"{h,-8} {d,-4} {cnt,6}  {fc}  {(reg.IsKnown(h) ? reg.Describe(h) : "（不在已知表中）")}");
    return 0;
}


static int Battle(string root, string serverDir)
{
    var data = Path.Combine(root, serverDir, "data");
    var t = BattleTables.Load(data);
    Console.WriteLine($"遇敌区域 {t.Encounts.Count}；敌人组 {t.Groups.Count}；敌人 {t.Enemies.Count}；敌人基础 {t.Bases.Count}");
    // 引用完整性
    int gRef = 0, gOk = 0, eRef = 0, eOk = 0, bRef = 0, bOk = 0;
    foreach (var e in t.Encounts.Values) foreach (var (g, _) in e.Groups) { gRef++; if (t.Groups.ContainsKey(g)) gOk++; }
    foreach (var g in t.Groups.Values) foreach (var s in g.Slots) { eRef++; if (t.Enemies.ContainsKey(s.EnemyId)) eOk++; }
    foreach (var en in t.Enemies.Values) { bRef++; if (t.Bases.ContainsKey(en.BaseId)) bOk++; }
    Console.WriteLine($"引用完整性：遇敌→组 {Pct(gOk, gRef)}；组→敌人 {Pct(eOk, eRef)}；敌人→基础 {Pct(bOk, bRef)}");
    // 区域与地图
    var tiles = TileInfoTable.LoadDefault(Path.Combine(root, "bin"));
    var maps = new MapManager(Path.Combine(data, "map"), tiles);
    int mapsWithEnc = t.Encounts.Values.Select(e => e.MapId).Distinct().Count(maps.Has);
    Console.WriteLine($"有遇敌区域的地图 {mapsWithEnc} 张");
    long walk = 0, covered = 0; var sampleMaps = t.Encounts.Values.Select(e => e.MapId).Distinct().Where(maps.Has).Take(300).ToList();
    foreach (var mid in sampleMaps)
    {
        var (m, w) = maps.Get(mid);
        for (int y = 0; y < m.Height; y++) for (int x = 0; x < m.Width; x++)
            if (w.IsWalkable(x, y)) { walk++; if (t.EncountAt(mid, x, y) != null) covered++; }
    }
    Console.WriteLine($"抽查 {sampleMaps.Count} 张有遇敌的地图：可走格被遇敌区域覆盖 {Pct((int)covered, (int)walk)}");
    // 模拟遇敌
    var svc = new EncounterService(t); var rng = new Random(3); int rolls = 0, ok = 0, enemies = 0, noAnime = 0; var sizes = new int[11];
    foreach (var e in t.Encounts.Values.Take(4000))
    {
        int x = (e.X1 + e.X2) / 2, y = (e.Y1 + e.Y2) / 2; rolls++;
        var r = svc.Roll(e.MapId, x, y, rng);
        if (r == null) continue;
        ok++; enemies += r.Enemies.Count; sizes[Math.Min(10, r.Enemies.Count)]++; noAnime += r.Enemies.Count(z => z.AnimeId == 0);
    }
    Console.WriteLine($"模拟遇敌 {rolls} 次：成功 {Pct(ok, rolls)}；平均 {(double)enemies / Math.Max(1, ok):F2} 只；缺动画编号 {noAnime}；人数分布 " + string.Join(" ", Enumerable.Range(1, 10).Select(i => $"{i}:{sizes[i]}")));
    return 0;
}


static int Items(string root, string serverDir)
{
    var data = Path.Combine(root, serverDir, "data");
    var g = GameTables.Load(data); var b = BattleTables.Load(data);
    Console.WriteLine($"道具 {g.Items.Count}；招式 {g.Techs.Count}；技能 {g.Skills.Count}；NPC {g.Npcs.Count}");
    int dr = 0, drOk = 0, st = 0, stOk = 0;
    foreach (var e in b.Enemies.Values)
    {
        foreach (var d in e.Drops) { dr++; if (g.Items.ContainsKey(d.ItemId)) drOk++; }
        foreach (var d in e.Steals) { st++; if (g.Items.ContainsKey(d.ItemId)) stOk++; }
    }
    int tk = g.Techs.Values.Count(t => g.Skills.ContainsKey(t.SkillId));
    Console.WriteLine($"引用完整性：敌人掉落→道具 {Pct(drOk, dr)}；偷窃→道具 {Pct(stOk, st)}；招式→技能 {Pct(tk, g.Techs.Count)}");
    var tiles = TileInfoTable.LoadDefault(Path.Combine(root, "bin"));
    var maps = new MapManager(Path.Combine(data, "map"), tiles);
    int inMap = 0, inBounds = 0, onWalk = 0, nearWalk = 0;
    foreach (var n in g.Npcs.Values)
    {
        if (!maps.Has(n.MapId)) continue; inMap++;
        var (m, w) = maps.Get(n.MapId);
        if (!m.InBounds(n.X, n.Y)) continue; inBounds++;
        if (w.IsWalkable(n.X, n.Y)) onWalk++;
        bool near = false;
        for (int dy = -1; dy <= 1 && !near; dy++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dy != 0) && w.IsWalkable(n.X + dx, n.Y + dy)) { near = true; break; }
        if (near) nearWalk++;
    }
    Console.WriteLine($"NPC：地图存在 {Pct(inMap, g.Npcs.Count)}；坐标在范围内 {Pct(inBounds, inMap)}；站在可走格上 {Pct(onWalk, inBounds)}；旁边有可走格（玩家能走过去对话） {Pct(nearWalk, inBounds)}");
    Console.WriteLine("道具类型分布（前 8）：" + string.Join(" ", g.Items.Values.GroupBy(i => i.Type).OrderByDescending(x => x.Count()).Take(8).Select(x => $"{x.Key}:{x.Count()}")));
    return 0;
}

static int Fight(string root, string serverDir)
{
    var data = Path.Combine(root, serverDir, "data");
    var t = BattleTables.Load(data); var svc = new EncounterService(t); var rng = new Random(9);
    int fights = 0, win = 0, lose = 0, esc = 0, unfinished = 0; long turns = 0;
    foreach (var e in t.Encounts.Values.Take(2000))
    {
        var r = svc.Roll(e.MapId, (e.X1 + e.X2) / 2, (e.Y1 + e.Y2) / 2, rng);
        if (r == null) continue;
        int lv = (int)Math.Round(r.Enemies.Average(x => x.Level));
        var b = new Battle(new PlaceholderFormulas(), rng);
        for (int i = 0; i < 2; i++)   // 两个与敌人平均等级相同的占位角色
        {
            var s = new LevelScaledStats().ForEnemy(t.Enemies[r.Enemies[0].EnemyId], null, lv);
            s.MaxHp *= 3; s.Attack = s.Attack * 3 / 2;
            b.Units.Add(new Combatant { Id = 1000 + i, Side = 0, Level = lv, Stats = s, Hp = s.MaxHp });
        }
        b.AddEnemies(r, t, new LevelScaledStats());
        while (b.RunTurn(new Dictionary<int, Command>()) == BattleOutcome.Ongoing && b.Turn < 200) { }
        fights++; turns += b.Turn;
        switch (b.Outcome) { case BattleOutcome.Win: win++; break; case BattleOutcome.Lose: lose++; break; case BattleOutcome.Escaped: esc++; break; default: unfinished++; break; }
    }
    Console.WriteLine($"模拟战斗 {fights} 场（占位数值，只检验流程）：胜 {win}、负 {lose}、我方逃跑 {esc}、未结束 {unfinished}；平均 {(double)turns / Math.Max(1, fights):F1} 回合");
    return unfinished == 0 ? 0 : 3;
}


// 用真实数据模拟一个玩家：在单机会话里四处走、穿过传送点、遇敌、战斗；并检查动画/图像引用。
static int Playtest(string root, string serverDir)
{
    var sw = Stopwatch.StartNew();
    var bin = Path.Combine(root, "bin");
    // ---- 1. 动画与图像引用检查（只看编号，不解码像素） ----
    var gids = new HashSet<int>();
    foreach (var rel in TileInfoTable.DefaultGraphicInfoFiles)
    {
        var p = Path.Combine(bin, rel);
        if (!File.Exists(p)) continue;
        var d = File.ReadAllBytes(p);
        for (int i = 0; i + 40 <= d.Length; i += 40) gids.Add(BitConverter.ToInt32(d, i));
    }
    var animeSets = new (string Info, string Anime, int Hdr)[]
    {
        ("AnimeInfo_4.bin", "Anime_4.bin", 12), ("AnimeInfoEx_1.Bin", "AnimeEx_1.Bin", 12), ("AnimeInfoV3_8.bin", "AnimeV3_8.bin", 20),
        ("Puk1/AnimeInfo_PUK1_1.Bin", "Puk1/Anime_PUK1_1.bin", 20), ("Puk2/AnimeInfo_PUK2_4.bin", "Puk2/Anime_PUK2_4.bin", 20),
        ("Puk3/AnimeInfo_PUK3_2.bin", "Puk3/Anime_PUK3_2.bin", 20), ("AnimeInfo_Joy_91.bin", "Anime_Joy_91.bin", 20),
        ("Puk4/AnimeInfo_PUK4_1.bin", "Puk4/Anime_PUK4_1.bin", 20), ("Puk7/AnimeInfo_PUK7_1.bin", "Puk7/Anime_PUK7_1.bin", 20),
    };
    var animes = animeSets.Where(a => File.Exists(Path.Combine(bin, a.Info)) && File.Exists(Path.Combine(bin, a.Anime)))
        .Select(a => new CgPort.Core.Assets.AnimeArchive(Path.Combine(bin, a.Info), Path.Combine(bin, a.Anime), a.Hdr)).ToList();
    IReadOnlyDictionary<(int Dir, int Act), CgPort.Core.Assets.AnimeArchive.Action>? Acts(int id)
    {
        for (int i = animes.Count - 1; i >= 0; i--) if (animes[i].Has(id)) return animes[i].Actions(id);
        return null;
    }
    Console.WriteLine($"图像编号 {gids.Count}；动画库 {animes.Count} 组");
    var pa = Acts(100000);
    if (pa != null)
    {
        var byAct = pa.Keys.GroupBy(k => k.Act).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}方向");
        Console.WriteLine($"角色动画 100000：{pa.Count} 个动作块；按动作号统计 " + string.Join(" ", byAct));
        int frames = pa.Values.Sum(a => a.Frames.Length), framesOk = pa.Values.Sum(a => a.Frames.Count(gids.Contains));
        Console.WriteLine($"  帧引用的图像存在 {Pct(framesOk, frames)}");
    }
    var data = Path.Combine(root, serverDir, "data");
    var gt = GameTables.Load(data); var bt = BattleTables.Load(data);
    int npcAnime = 0, npcGfx = 0, npcNone = 0;
    foreach (var n in gt.Npcs.Values) { if (Acts(n.AppearanceId) != null) npcAnime++; else if (gids.Contains(n.AppearanceId)) npcGfx++; else npcNone++; }
    Console.WriteLine($"NPC 外观 {gt.Npcs.Count} 个：是动画编号 {npcAnime}、是静态图像编号 {npcGfx}、都找不到 {npcNone}");
    int eb = 0, ebOk = 0, ebFrames = 0, ebFramesOk = 0;
    foreach (var b in bt.Bases.Values)
    {
        eb++; var a = Acts(b.AnimeId);
        if (a == null) continue; ebOk++;
        foreach (var act in a.Values) { ebFrames += act.Frames.Length; ebFramesOk += act.Frames.Count(gids.Contains); }
    }
    Console.WriteLine($"敌人外观：动画存在 {Pct(ebOk, eb)}；这些动画的帧引用图像存在 {Pct(ebFramesOk, ebFrames)}");

    // ---- 2. 单机会话模拟 ----
    var sess = new CgPort.Client.LocalSession(root, serverDir, new CgPort.Client.SessionOptions { StartMap = 1000, Seed = 42, EncounterScale = 0.5 });
    var tiles = TileInfoTable.LoadDefault(bin);
    var rng = new Random(42);
    var visited = new HashSet<int>(); int warps = 0, encounters = 0, wins = 0, losses = 0, steps = 0, rejected = 0, battleTurns = 0;
    MapData? map = null; Walkability? walk = null; long missingOnVisited = 0, tilesOnVisited = 0;
    sess.MapEntered += (m, x, y) =>
    {
        map = MapData.Load(m.MapFile, m.MapId); walk = Walkability.Build(map, tiles);
        if (visited.Add(m.MapId)) { missingOnVisited += walk.MissingGround; tilesOnVisited += map.Width * map.Height; }
    };
    sess.BattleStarted += v => encounters++;
    sess.BattleEnded += o => { if (o == BattleOutcome.Win) wins++; else if (o == BattleOutcome.Lose) losses++; };
    sess.Start();
    Console.WriteLine($"开局：地图 {sess.CurrentMap!.MapId}（{sess.CurrentMap.Name}）({sess.X},{sess.Y})，可走={walk!.IsWalkable(sess.X, sess.Y)}");
    var warpTable = WarpTable.Load(Path.Combine(data, "warp.txt"));
    for (int trip = 0; trip < 400 && steps < 30000; trip++)
    {
        // 70% 去本图的某个传送点（出行），30% 随机走走
        int mapId = sess.CurrentMap!.MapId;
        var exits = warpTable.All.Where(w => w.FromMap == mapId).ToList();
        (int X, int Y)? target = null;
        var reach = walk!.Reachable(sess.X, sess.Y, 200_000);
        if (exits.Count > 0 && rng.NextDouble() < 0.7)
        {
            var reachableExits = exits.Where(e => NeighborIn(reach, map!, e.FromX, e.FromY)).ToList();
            if (reachableExits.Count > 0) { var e = reachableExits[rng.Next(reachableExits.Count)]; target = (e.FromX, e.FromY); }
        }
        if (target == null && reach.Count > 1) { var cell = reach.ElementAt(rng.Next(reach.Count)); target = (cell % map!.Width, cell / map.Width); }
        if (target == null) break;
        var path = BfsPath(walk!, map!, (sess.X, sess.Y), target.Value);
        int startMap = mapId;
        foreach (var dir in path)
        {
            while (sess.InBattle) { sess.SubmitCommand(new Command(CommandKind.Attack)); battleTurns++; if (battleTurns > 1_000_000) break; }
            var r = sess.Step(dir); steps++;
            if (r == MoveResult.Warped) { warps++; break; }
            if (r != MoveResult.Ok) { rejected++; break; }
            if (sess.CurrentMap!.MapId != startMap) break;
        }
        while (sess.InBattle) { sess.SubmitCommand(new Command(CommandKind.Attack)); battleTurns++; }
    }
    Console.WriteLine($"模拟：走了 {steps} 步，服务端拒绝 {rejected} 步，传送 {warps} 次，到过 {visited.Count} 张地图，遇敌 {encounters} 次（胜 {wins}、负 {losses}），战斗回合 {battleTurns}");
    Console.WriteLine($"到过的地图里地面找不到图像的格子 {Pct((int)missingOnVisited, (int)tilesOnVisited)}");
    Console.WriteLine($"耗时 {sw.Elapsed.TotalSeconds:F1}s");
    foreach (var a in animes) a.Dispose();
    return 0;
}

static bool NeighborIn(HashSet<int> reach, MapData m, int x, int y)
{
    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        if ((dx != 0 || dy != 0) && m.InBounds(x + dx, y + dy) && reach.Contains(m.Index(x + dx, y + dy))) return true;
    return false;
}

// 8 方向 BFS 寻路；终点允许是不可走格（传送点常在门口），返回方向序列
static List<int> BfsPath(Walkability w, MapData m, (int X, int Y) from, (int X, int Y) to)
{
    var prev = new Dictionary<int, (int Cell, int Dir)>(); var q = new Queue<int>();
    int s = m.Index(from.X, from.Y), t = m.Index(to.X, to.Y);
    q.Enqueue(s); prev[s] = (-1, -1);
    while (q.Count > 0)
    {
        int c = q.Dequeue(); if (c == t) break;
        int x = c % m.Width, y = c / m.Width;
        for (int d = 0; d < 8; d++)
        {
            var (dx, dy) = Directions.Deltas[d]; int nx = x + dx, ny = y + dy;
            if (!m.InBounds(nx, ny)) continue;
            int n = m.Index(nx, ny);
            if (prev.ContainsKey(n)) continue;
            bool ok = n == t ? (Math.Abs(dx) + Math.Abs(dy) == 1 || w.IsWalkable(x + dx, y) || w.IsWalkable(x, y + dy)) : w.CanStep(x, y, nx, ny);
            if (!ok) continue;
            prev[n] = (c, d); q.Enqueue(n);
        }
    }
    var dirs = new List<int>();
    if (!prev.ContainsKey(t)) return dirs;
    for (int c = t; prev[c].Cell != -1; c = prev[c].Cell) dirs.Add(prev[c].Dir);
    dirs.Reverse();
    return dirs;
}


static int Diag(string root, string serverDir)
{
    var bin = Path.Combine(root, "bin"); var data = Path.Combine(root, serverDir, "data");
    var a = new CgPort.Core.Assets.AnimeArchive(Path.Combine(bin, "AnimeInfo_4.bin"), Path.Combine(bin, "Anime_4.bin"), 12);
    Console.WriteLine("角色动画 100000，方向 0 的各动作：动作号=帧数/时长ms");
    Console.WriteLine("  " + string.Join("  ", a.Actions(100000).Where(k => k.Key.Dir == 0).OrderBy(k => k.Key.Act).Select(k => $"{k.Key.Act}={k.Value.Frames.Length}/{k.Value.DurationMs}")));
    var bt = BattleTables.Load(data); var warps = WarpTable.Load(Path.Combine(data, "warp.txt"));
    var adj = warps.All.GroupBy(w => w.FromMap).ToDictionary(g => g.Key, g => g.Select(w => w.ToMap).Distinct().ToList());
    var seen = new HashSet<int> { 1000 }; var q = new Queue<(int, int)>(); q.Enqueue((1000, 0)); var withEnc = new List<(int Map, int Depth)>();
    while (q.Count > 0) { var (m, d) = q.Dequeue(); if (bt.EncountsOnMap(m).Count > 0) withEnc.Add((m, d)); if (d >= 3 || !adj.TryGetValue(m, out var ns)) continue; foreach (var n in ns) if (seen.Add(n)) q.Enqueue((n, d + 1)); }
    Console.WriteLine($"法兰城 3 次传送内可达地图 {seen.Count} 张，其中有遇敌区域的 {withEnc.Count} 张：" + string.Join(" ", withEnc.Take(12).Select(x => $"{x.Map}(距{x.Depth})")));
    Console.WriteLine($"法兰城(1000)本身的遇敌区域 {bt.EncountsOnMap(1000).Count} 个");
    Console.WriteLine("遇敌率参数(c11)分布：" + string.Join(" ", bt.Encounts.Values.GroupBy(e => e.Rate).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key}:{g.Count()}")));
    var target = withEnc.FirstOrDefault(x => x.Depth > 0).Map;
    if (target == 0) target = bt.Encounts.Values.GroupBy(e => e.MapId).OrderByDescending(g => g.Count()).First().Key;
    var sess = new CgPort.Client.LocalSession(root, serverDir, new CgPort.Client.SessionOptions { StartMap = target, Seed = 1 });
    int enc = 0, steps = 0, inArea = 0, turns = 0; sess.BattleStarted += v => enc++;
    sess.Start(); var rng = new Random(1);
    for (int i = 0; i < 4000; i++)
    {
        while (sess.InBattle && turns < 200000) { sess.SubmitCommand(new Command(CommandKind.Attack)); turns++; }
        var r = sess.Step(rng.Next(8));
        if (r == MoveResult.Ok) { steps++; if (bt.EncountAt(sess.CurrentMap!.MapId, sess.X, sess.Y) != null) inArea++; }
    }
    Console.WriteLine($"在地图 {target}（{sess.CurrentMap!.Name}）随机走：成功 {steps} 步，其中 {inArea} 步在遇敌区域内，遇敌 {enc} 次，战斗回合 {turns}");
    return 0;
}


// 用全部真实 NPC 脚本检验脚本引擎：只输出统计数字，不输出任何对话内容
static int Scripts(string root, string serverDir)
{
    var data = Path.Combine(root, serverDir, "data"); var npcDir = Path.Combine(data, "npc");
    int files = 0, failed = 0, blocks = 0, windows = 0, branches = 0, actions = 0;
    var unsupportedCond = new Dictionary<string, int>(); var actionNames = new Dictionary<string, int>();
    var parsed = new Dictionary<string, NpcScript>();
    foreach (var f in Directory.EnumerateFiles(npcDir))
    {
        files++;
        try
        {
            var sc = NpcScript.Load(f); parsed[Path.GetFileName(f)] = sc;
            foreach (var b in sc.Blocks)
            {
                blocks++;
                foreach (var c in b.Conditions.Where(c => c.Kind.StartsWith("unsupported:"))) { var k = "（格式无法解析）"; unsupportedCond[k] = unsupportedCond.GetValueOrDefault(k) + 1; }
                foreach (var st in b.Steps)
                    switch (st)
                    {
                        case NpcScript.WindowStep or NpcScript.EndWindowStep: windows++; break;
                        case NpcScript.BranchStep: branches++; break;
                        case NpcScript.ActionStep a: actions++; actionNames[a.Name] = actionNames.GetValueOrDefault(a.Name) + 1; break;
                    }
            }
        }
        catch { failed++; }
    }
    Console.WriteLine($"脚本文件 {files}，解析失败 {failed}；规则 {blocks}，对话框 {windows}，分支 {branches}，动作 {actions}");
    var known = new HashSet<string> { "giveitem", "giveleak1item", "killitem", "addgold", "endevent", "globalflag", "warp", "encount", "leavegroup", "killitemall", "message", "systemmessage", "endblock", "play_se", "setlp", "setdur", "setobject", "addstampcount", "localcount1", "localcount2" };
    long knownCount = actionNames.Where(kv => known.Contains(kv.Key)).Sum(kv => kv.Value);
    Console.WriteLine($"动作中已支持的占 {Pct((int)knownCount, actions)}；未支持的前 12 种：" + string.Join(" ", actionNames.Where(kv => !known.Contains(kv.Key)).OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key}:{kv.Value}")));
    // 条件种类覆盖（按真实脚本里出现的次数）
    var condKinds = new Dictionary<string, int>();
    foreach (var sc in parsed.Values) foreach (var b in sc.Blocks)
        foreach (var c in b.Conditions.Concat(b.Steps.OfType<NpcScript.BranchStep>().SelectMany(x => x.Conditions)))
            condKinds[c.Kind.StartsWith("unsupported:") ? "（无法解析）" : c.Kind] = condKinds.GetValueOrDefault(c.Kind.StartsWith("unsupported:") ? "（无法解析）" : c.Kind) + 1;
    var supported = new HashSet<string> { "item", "itemspace", "gold", "level", "lv", "xpos", "ypos", "endevent", "globalflag", "group", "job", "petspace" };
    int totalC = condKinds.Values.Sum(), okC = condKinds.Where(kv => supported.Contains(kv.Key)).Sum(kv => kv.Value);
    Console.WriteLine($"条件 {totalC} 处，已支持的占 {Pct(okC, totalC)}；未支持的前 12 种：" + string.Join(" ", condKinds.Where(kv => !supported.Contains(kv.Key)).OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key}:{kv.Value}")));

    // 对每个有脚本的 NPC 模拟"新角色上去对话"：能否弹出第一页；一路按第一个按钮最多 30 页
    var gt = GameTables.Load(data);
    int npcWithScript = 0, scriptFound = 0, firstPage = 0, completed = 0, stuck = 0; long pages = 0;
    foreach (var n in gt.Npcs.Values)
    {
        if (string.IsNullOrWhiteSpace(n.ScriptFile)) continue;
        npcWithScript++;
        var key = parsed.ContainsKey(n.ScriptFile) ? n.ScriptFile : parsed.ContainsKey(n.ScriptFile + ".txt") ? n.ScriptFile + ".txt" : null;
        if (key == null) continue;
        scriptFound++;
        var host = new SimHost { X = n.X, Y = n.Y + 1 };
        var run = new ScriptRun(parsed[key], host, new Random(n.Id));
        if (run.Current == null) continue;
        firstPage++;
        int k = 0;
        while (run.Current != null && !run.Current.IsLast && k < 30) { run.Press(run.Current.Buttons.FirstOrDefault() ?? "ok"); pages++; k++; }
        if (k >= 30) stuck++; else completed++;
    }
    Console.WriteLine($"有脚本名的 NPC {npcWithScript}，脚本文件存在 {Pct(scriptFound, npcWithScript)}；新角色对话能弹出第一页 {Pct(firstPage, scriptFound)}；一路按第一个按钮能正常结束 {Pct(completed, firstPage)}（超过 30 页 {stuck}），共翻页 {pages}");
    return 0;
}

// 联网冒烟测试：NetworkSession 连原版 cgmsv → 登录 → 走几步 → GM 传送到野外 → 走到遇敌 → 普攻打完一场
static int Net(string hostPort, string account, string? mapDir)
{
    var hp = hostPort.Split(':');
    var maps = mapDir != null ? MapData.IndexServerMapDir(mapDir) : new();
    var log = new List<string>();
    var s = new CgPort.Client.NetworkSession(new CgPort.Client.NetworkOptions
    {
        Host = hp[0], Port = int.Parse(hp[1]), CdKey = account, Account = account, Password = "123456",
        NewCharName = "net" + account, ResolveMap = f => maps.TryGetValue(f, out var m) ? m.Path : null,
        Trace = l => { lock (log) log.Add(l); },
    });
    int fails = 0;
    void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    string? err = null; s.Error += e => err = e;
    int maps0 = 0; s.MapEntered += (m, x, y) => maps0++;
    bool battle = false; CgPort.Server.BattleOutcome? outcome = null; int turns = 0;
    s.BattleStarted += v => battle = true;
    s.TurnResolved += (ev, v) => { turns++; Console.WriteLine($"  回合 {turns}: " + string.Join("; ", ev.Select(e => $"{e.ActorId}->{e.TargetId} 伤害{e.Damage}{(e.Killed ? " 击倒" : "")}"))); };
    s.BattleEnded += o => outcome = o;
    bool Pump(Func<bool> until, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { s.Poll(); if (until() || err != null) return until(); Thread.Sleep(20); }
        return until();
    }
    s.Start();
    Check(Pump(() => s.InGame && s.CurrentMap != null, 10000), $"登录并进入游戏：{s.Player.Name} @ {s.CurrentMap?.MapId} ({s.X},{s.Y}) {err}");
    Pump(() => false, 800);
    Check(s.CurrentMap?.Name.Length > 0, $"地图名：{s.CurrentMap?.Name}");
    Check(s.CurrentMap?.MapId != 1000 || s.NpcsOnCurrentMap().Count > 0, $"周围对象：{s.NpcsOnCurrentMap().Count} 个，例如 {string.Join("、", s.NpcsOnCurrentMap().Take(3).Select(n => n.Name))}");
    // TALK_AT=地图:x:y → 传送到那个 NPC 旁边对话，打印服务端回的原始行（研究窗口格式用），然后退出
    if (Environment.GetEnvironmentVariable("TALK_AT") is { } ta)
    {
        var t = ta.Split(':').Select(int.Parse).ToArray();
        foreach (var cmd in (Environment.GetEnvironmentVariable("TALK_SAY") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)) { s.Say(cmd); Pump(() => false, 600); }
        if (int.TryParse(Environment.GetEnvironmentVariable("RAW_DELAY"), out var rd)) Pump(() => false, rd);
        foreach (var raw in (Environment.GetEnvironmentVariable("RAW_SEND") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.StartsWith("WAIT ")) { Pump(() => false, int.Parse(raw[5..])); continue; }
            if (raw.StartsWith("FRONTOF "))   // 站到某人面前并面向他（交易/组队要对着人）
            {
                Pump(() => s.NpcsOnCurrentMap().Any(n => n.Name == raw[8..]), 6000);
                var who = s.NpcsOnCurrentMap().FirstOrDefault(n => n.Name == raw[8..]);
                if (who == null) { Console.WriteLine("找不到 " + raw[8..]); continue; }
                var (fx, fy) = Directions.Deltas[((who.Dir % 8) + 8) % 8];
                s.Say($"/warp {s.CurrentMap!.MapId} 0 {who.X + fx} {who.Y + fy}"); Pump(() => false, 2000);
                s.Face(-fx, -fy); Pump(() => false, 600);
                Console.WriteLine($"站到 {who.Name}({who.X},{who.Y} 朝{who.Dir}) 面前 ({s.X},{s.Y})");
                continue;
            }
            if (raw.StartsWith("SAY ")) { s.Say(raw[4..]); Pump(() => false, 1500); continue; }
            if (raw.StartsWith("TALK "))   // 和附近叫这个名字的 NPC 说话
            {
                var who = s.NpcsOnCurrentMap().FirstOrDefault(n => n.Name == raw[5..]);
                Console.WriteLine(who == null ? "找不到 " + raw[5..] : $"对话 {who.Name}({who.X},{who.Y})");
                if (who != null) { s.TalkTo(who.Id); Pump(() => false, 2000); }
                continue;
            }
            if (raw.StartsWith("STEP ")) { foreach (var c in raw[5..]) { s.Step(c - '0'); Pump(() => false, 500); } Console.WriteLine($"走到 {s.CurrentMap?.MapId} ({s.X},{s.Y})"); continue; }
            if (raw.StartsWith("ANSB ")) { var q = raw[5..].Split(' ', 2); s.AnswerWith(q[0], q.Length > 1 ? q[1] : ""); Pump(() => false, 1500); continue; }
            if (raw.StartsWith("ANS ")) { s.Answer(raw[4..]); Pump(() => false, 1500); continue; }
            if (raw.StartsWith("FACE ")) { var d = raw[5..].Split(' ').Select(int.Parse).ToArray(); s.Face(d[0], d[1]); Pump(() => false, 600); continue; }
            s.SendRaw(raw); Pump(() => false, 1200);
        }
        // HOLD_MS=毫秒 → 在线待着收包（两个号配合研究组队/交易用），把收到的全部记到 talk_trace_<账号>.txt
        if (int.TryParse(Environment.GetEnvironmentVariable("HOLD_MS"), out var hold))
        {
            Pump(() => false, hold);
            lock (log) File.WriteAllLines($"talk_trace_{account}.txt", log);
            Console.WriteLine($"{s.Player.Name} @ ({s.X},{s.Y}) 收发 {log.Count} 行；队伍 {(s.InParty ? (s.IsPartyLeader ? "队长" : "队员") : "无")}：" +
                string.Join("、", s.Party.Select(m => $"{m.Index}:{m.Name} Lv{m.Level} HP{m.Hp}/{m.MaxHp} MP{m.Mp}/{m.MaxMp} #{m.ObjId}")) + $"；开关 {s.Flags}");
            return 0;
        }
        // BATTLE_RAW="Rg S|1|0|A;Rg W|0|A" → 来回走到遇敌，服务端每要一次指令（LYIm A|）就按顺序发一条；用完后发 Rg N
        if (Environment.GetEnvironmentVariable("BATTLE_RAW") is { } br)
        {
            var cmds = new Queue<string>(br.Split(';', StringSplitOptions.RemoveEmptyEntries));
            for (int i = 0; i < 400 && !s.InBattle; i++) { s.Step(i % 2 == 0 ? 2 : 6); Pump(() => s.InBattle, 350); }
            int seen = 0; var until = DateTime.Now.AddSeconds(90);
            while (s.InBattle && DateTime.Now < until)
            {
                Pump(() => false, 300);
                int asks; lock (log) asks = log.Count(l => l.StartsWith("S LYIm A|"));
                // 每个 A| 之后要两条指令（指令1、指令2）
                while (seen < asks) { seen++; for (int k = 0; k < 2; k++) { var c = cmds.Count > 0 ? cmds.Dequeue() : "Rg N"; s.SendRaw(c); Pump(() => false, 400); } }
            }
            lock (log) File.WriteAllLines("talk_trace.txt", log);
            Console.WriteLine(s.InBattle ? "战斗超时" : "战斗结束");
            return 0;
        }
        if (t[0] == 0)   // TALK_AT=0:0:0 → 只执行 TALK_SAY，然后按 TALK_PRESS
        {
            foreach (var a in (Environment.GetEnvironmentVariable("TALK_PRESS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) { s.Answer(a); Pump(() => false, 1500); }
            lock (log) File.WriteAllLines("talk_trace.txt", log);
            lock (log) foreach (var l in log.SkipWhile(l => !l.StartsWith("C uSr"))) Console.WriteLine(l.Length > 500 ? l[..500] : l);
            if (Environment.GetEnvironmentVariable("DUMP_STATE") != null)
            {
                Console.WriteLine($"技能栏 {s.Player.SkillSlots}，MP {s.Player.Mp}/{s.Player.MaxMp}");
                var st = s.Player.Stats;
                Console.WriteLine($"  {s.Player.Name} {s.Player.Job} Lv{s.Player.Level} 经验{s.Player.Exp}/{st.NextExp} HP{s.Player.Hp}/{s.Player.MaxHp} 体{st.Vit} 力{st.Str} 强{st.Tgh} 速{st.Spd} 魔{st.Mag} 耐{st.Endurance} 灵{st.Dex} 智{st.Int} 攻{st.Attack} 防{st.Defense} 敏{st.Agility} 精{st.Spirit} 回{st.Recovery} 魅{st.Charm} 地水火风 {string.Join(",", st.Element)} 必杀{st.Critical} 反击{st.Counter} 命中{st.Hit} 闪躲{st.Dodge} 战绩{st.Record} 点数{s.Player.StatPoints}");
                foreach (var e in s.Player.Equipment) Console.WriteLine($"  装备[{e.Slot}] {e.Name} 图{e.Graphic}");
                foreach (var k in s.Player.Skills) Console.WriteLine($"  技能[{k.Slot}] {k.Name} Lv{k.Level} 经验{k.Exp}/{k.NextExp} 占{k.SlotsUsed}：" + string.Join("、", k.Techs.Select(x => $"{x.Index}:{x.Name}({x.Mp}MP,#{x.TechId})")));
                foreach (var p in s.Player.Pets) Console.WriteLine($"  宠物[{p.Slot}] {p.DisplayName} Lv{p.Level} HP{p.Hp}/{p.MaxHp} MP{p.Mp}/{p.MaxMp} 攻{p.Attack} 防{p.Defense} 敏{p.Agility} 状态{p.State} 形象{p.Anime} {p.RaceName} 经验{p.Exp}/{p.NextExp} 精{p.Spirit} 回{p.Recovery} 忠{p.Loyalty} 技能栏{p.SkillSlots} 地水火风{string.Join(",", p.Element)} 体力强速魔{p.Vit},{p.Str},{p.Tgh},{p.Spd},{p.Mag} 点数{p.Points} 负重{p.LoadMax} 昵称[{p.Nick}]：" + string.Join("、", p.Techs.Select(x => $"{x.Index}:{x.Name}")));
            }
            return 0;
        }
        foreach (var (ax, ay) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1) })
        {
            s.Say($"/warp {t[0]} 0 {t[1] + ax} {t[2] + ay}");
            Pump(() => s.CurrentMap?.MapId == t[0] && s.X == t[1] + ax && s.Y == t[2] + ay, 2000);
            if (s.X != t[1] + ax || s.Y != t[2] + ay) continue;
            Pump(() => false, 500);
            var npc = s.NpcsOnCurrentMap().FirstOrDefault(n => n.X == t[1] && n.Y == t[2]);
            Console.WriteLine($"站在 ({s.X},{s.Y})，目标 {npc?.Name ?? "无"}；周围 {string.Join("、", s.NpcsOnCurrentMap().Select(n => $"{n.Name}({n.X},{n.Y})"))}");
            if (npc == null) break;
            int mark; lock (log) mark = log.Count;
            s.TalkTo(npc.Id);
            Pump(() => false, 2000);
            var answers = (Environment.GetEnvironmentVariable("TALK_PRESS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var a in answers) { s.Answer(a); Pump(() => false, 1500); }
            lock (log) foreach (var l in log.Skip(mark)) Console.WriteLine(l.Length > 600 ? l[..600] + "…" : l);
            break;
        }
        lock (log) File.WriteAllLines("talk_trace.txt", log);
        return 0;
    }
    // 和 NPC 对话：传送到 NPC 旁边，转身面向它
    foreach (var n in s.NpcsOnCurrentMap().OrderBy(n => Math.Abs(n.X - s.X) + Math.Abs(n.Y - s.Y)).Take(int.TryParse(Environment.GetEnvironmentVariable("TALK_N"), out var tn) ? tn : 3))
    {
        Console.WriteLine($"  对象 {n.Id} {n.Name} ({n.X},{n.Y}) 外观 {n.AppearanceId}");
        CgPort.Server.DialogPage? page = null; s.DialogChanged += p => { if (p != null) page = p; };
        int fl = s.CurrentMap!.MapId;
        foreach (var (ax, ay) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1) })
        {
            s.Say($"/warp {fl} 0 {n.X + ax} {n.Y + ay}");
            Pump(() => s.X == n.X + ax && s.Y == n.Y + ay, 1500);
            if (s.X != n.X + ax || s.Y != n.Y + ay) continue;
            Pump(() => false, 300);
            s.TalkTo(n.Id);
            if (Pump(() => page != null, 2500)) break;
        }
        Console.WriteLine($"    → {(page == null ? "无窗口" : page.Text.Replace("\n", " / ") + " [" + string.Join(",", page.Buttons) + "]")}");
        if (page != null) { s.Answer("cancel"); Pump(() => false, 300); }
    }
    // GM 传送到芙蕾雅野外（测试账号已加进服务端 Lua 管理员列表）
    s.Say("/warp 100 0 370 180");
    Check(Pump(() => s.CurrentMap?.MapId == 100, 5000), $"传送到野外：{s.CurrentMap?.MapId} ({s.X},{s.Y})");
    Pump(() => false, 500);
    int sx = s.X, steps = 0; int corrected = 0; s.Moved += (x, y, d) => { };
    while (!s.InBattle && steps < 400 && err == null)
    {
        s.Step(steps % 2 == 0 ? 2 : 6);   // 东、西来回
        steps++;
        Pump(() => s.InBattle, 300);
    }
    Check(s.InBattle || battle, $"走 {steps} 步后遇敌");
    Pump(() => battle, 3000);
    Check(battle, "收到战斗单位列表");
    var sw2 = Stopwatch.StartNew();
    while (outcome == null && sw2.Elapsed.TotalSeconds < 120 && err == null)
    {
        if (s.InBattle) s.SubmitCommand(new Command(CommandKind.Attack));
        Pump(() => outcome != null, 1500);
    }
    Check(outcome != null, $"战斗结束：{outcome}，{turns} 回合");
    Check(err == null, "没有连接错误 " + err);
    File.WriteAllLines("net_trace.txt", log);
    s.Dispose();
    Console.WriteLine($"NET DONE fail={fails}");
    return fails == 0 ? 0 : 1;
}


// 打包手机数据：pack <游戏目录> <输出.cgpak>
// 只装游戏真正读的文件：argument.ini 允许的图像/动画组 + 调色板 + 地图格子用的 GraphicInfo + 服务端 data（不含工具；客户端 exe 只为了里面的界面图资源）。
static int PackGame(string root, string output)
{
    var det = CgPort.Core.Assets.AssetDetector.Detect(root);
    var files = new List<string>();
    void Add(string? rel) { if (rel != null && File.Exists(Path.Combine(root, rel)) && !files.Contains(rel, StringComparer.OrdinalIgnoreCase)) files.Add(rel.Replace('\\', '/')); }
    Add("data/argument.ini");
    foreach (var st in det.Sets) { Add(st.GraphicInfo); Add(st.Graphic); Add(st.AnimeInfo); Add(st.Anime); }
    foreach (var gi in CgPort.Core.TileInfoTable.DefaultGraphicInfoFiles) Add("bin/" + gi);
    // 这个客户端的新界面图 / 大地图标注在 exe 资源里（PeResources），寻路坐标点在 data/mappoint.dat
    foreach (var exe in Directory.EnumerateFiles(root, "*.exe"))
        if (CgPort.Core.Assets.PeResources.Load(exe) is { } rr && rr.Get("10", 208) != null) { Add(Path.GetFileName(exe)); break; }
    Add("data/mappoint.dat");
    var pal = Path.Combine(root, "bin", "pal");
    if (Directory.Exists(pal)) foreach (var f in Directory.EnumerateFiles(pal, "*", SearchOption.AllDirectories)) Add(Path.GetRelativePath(root, f));
    if (det.ServerDir != null)
    {
        var data = Path.Combine(root, det.ServerDir, "data");
        var skipExt = new[] { ".exe", ".dll", ".mdb", ".db3", ".rar", ".zip", ".7z", ".bak" };
        var skipDir = new[] { "bin", "Favorite", "err_map" };
        foreach (var f in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(data, f).Replace('\\', '/');
            if (skipExt.Any(e => rel.EndsWith(e, StringComparison.OrdinalIgnoreCase)) || skipDir.Any(d => rel.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase))) continue;
            Add(Path.GetRelativePath(root, f));
        }
    }
    long total = files.Sum(f => new FileInfo(Path.Combine(root, f)).Length);
    Console.WriteLine($"图像组 {det.Sets.Count}：{string.Join("、", det.Sets.Select(x => x.Name))}；服务端目录：{det.ServerDir ?? "（没有，只能联网）"}");
    Console.WriteLine($"共 {files.Count} 个文件，{total / 1048576.0:F0} MB → {output}");
    var sw = System.Diagnostics.Stopwatch.StartNew(); long last = 0;
    CgPort.Core.GameFiles.Build(root, files, output, (done, all) =>
    {
        if (sw.ElapsedMilliseconds - last < 5000) return; last = sw.ElapsedMilliseconds;
        Console.WriteLine($"  {done * 100 / Math.Max(1, all)}%  {done / 1048576} / {all / 1048576} MB");
    });
    Console.WriteLine($"完成，用时 {sw.Elapsed.TotalSeconds:F0} 秒。校验中…");
    // 校验：从包里识别出的资源和原目录一致；抽查文件内容逐字节相同
    var det2 = CgPort.Core.Assets.AssetDetector.Detect(output);
    bool ok = det2.Sets.Select(x => x.Name + x.Graphic + x.AnimeHeader).SequenceEqual(det.Sets.Select(x => x.Name + x.Graphic + x.AnimeHeader)) && det2.ServerDir == det.ServerDir;
    var rng = new Random(1);
    foreach (var f in files.OrderBy(_ => rng.Next()).Take(40).Append(files[^1]))
    {
        var a = File.ReadAllBytes(Path.Combine(root, f)); var b = CgPort.Core.GameFiles.ReadAllBytes(Path.Combine(output, f));
        if (!a.AsSpan().SequenceEqual(b)) { ok = false; Console.WriteLine("  内容不一致：" + f); }
    }
    Console.WriteLine(ok ? "校验通过" : "校验失败");
    return ok ? 0 : 1;
}

// 单机对话自测：localtalk <游戏根目录> <服务端目录>，按真实数据检查路人/告示牌/导游 NPC
static int LocalTalk(string root, string serverDir)
{
    int fail = 0;
    void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fail++; }
    var s = new CgPort.Client.LocalSession(root, serverDir, new CgPort.Client.SessionOptions { StartMap = 1000, StartX = 84, StartY = 64, EncountersEnabled = false, Seed = 1 });
    var chats = new List<string>(); DialogPage? page = null; int pages = 0;
    s.ChatReceived += (t, c) => chats.Add(t);
    s.DialogChanged += p => { page = p; pages++; };
    s.Start();
    CgPort.Core.Npc Find(string name) => s.NpcsOnCurrentMap().First(n => n.Name == name);
    // 路人：聊天栏一句话，不弹窗口
    var town = Find("马葛道尔");
    for (int i = 0; i < 6; i++) s.TalkTo(town.Id);
    Console.WriteLine("  " + string.Join(" / ", chats.Distinct()));
    Check(chats.Count == 6 && chats.All(c => c.StartsWith("马葛道尔: ")) && chats.Distinct().Count() == 2 && pages == 0 && !s.InDialog, "路人：6 次对话 = 6 句聊天（两句随机），不弹窗口");
    Check(chats.Contains("马葛道尔: 如果想作战斗系的职业,就到公会去看看吧!"), "路人：文字与服务端一致");
    // 导游：见习封印师鲁德 → 选择窗口
    s.Dispose();
    s = new CgPort.Client.LocalSession(root, serverDir, new CgPort.Client.SessionOptions { StartMap = 1000, StartX = 93, StartY = 73, EncountersEnabled = false, Seed = 1 });
    s.DialogChanged += p => { page = p; pages++; };
    s.Start();
    Check(s.TalkTo(Find("见习封印师鲁德").Id) && page != null, "导游：弹出窗口");
    Console.WriteLine($"  [{page?.Text}] 选项: {string.Join(" | ", page?.Options ?? new List<string>())} 按钮: {string.Join(",", page?.Buttons ?? new List<string>())}");
    Check(page!.Options?.Count == 2 && page.Options[1] == "什么是封印卡?" && page.Text.Split('\n').Length == 3 && page.Buttons.SequenceEqual(new[] { "cancel" }), "导游：选择窗口（正文 3 行，两个选项，取消）——与服务端 BZ 2 2 一致");
    s.Answer("opt:0");
    Console.WriteLine($"  [{page?.Text}] 按钮: {string.Join(",", page?.Buttons ?? new List<string>())}");
    Check(page != null && page.Text.Contains("宠物资料卡是写有宠物档案") && page.Buttons.SequenceEqual(new[] { "next" }), "导游：选 1 → 下一页（服务端 BZ 0 w）");
    s.Answer("next");
    Check(page != null && page.Text.Contains("得到宠物资料卡的方法") && page.Buttons.SequenceEqual(new[] { "ok", "prev" }), "导游：下一页 → 确定/上一页（服务端 BZ 0 h）");
    s.Answer("prev");
    Check(page?.Options?.Count == 2, "导游：上一页 → 回到选择窗口");
    s.Answer("cancel");
    Check(page == null && !s.InDialog, "导游：取消 → 关闭");
    // 路标
    s.Dispose();
    s = new CgPort.Client.LocalSession(root, serverDir, new CgPort.Client.SessionOptions { StartMap = 1000, StartX = 90, StartY = 68, EncountersEnabled = false, Seed = 1 });
    s.DialogChanged += p => { page = p; };
    s.Start(); s.TalkTo(Find("路标").Id);
    Check(page != null && page.Text.Contains("北") && page.Text.Contains("大圣堂方面") && page.Buttons.SequenceEqual(new[] { "ok" }), "路标：方向说明 + 确定");
    s.Answer("ok"); Check(page == null, "路标：确定 → 关闭");
    // 告示牌：没有回应
    s.Dispose();
    s = new CgPort.Client.LocalSession(root, serverDir, new CgPort.Client.SessionOptions { StartMap = 1000, StartX = 156, StartY = 37, EncountersEnabled = false, Seed = 1 });
    page = null; pages = 0; s.DialogChanged += p => { page = p; pages++; };
    s.Start(); s.TalkTo(Find("大圣堂").Id);
    Check(pages == 0 && !s.InDialog, "告示牌：对话无回应（与服务端一致）");
    // 全部 Windowman：脚本能读、起始页存在、每页的 msg 都有、每页至少一个按钮或选项
    var tables = s.GameTables; int total = 0, ok = 0; var bad = new List<string>();
    var npcDir = Path.Combine(root, serverDir, "data", "npc");
    foreach (var n in tables.Npcs.Values.Where(n => n.Type == "Windowman"))
    {
        total++;
        var f = Path.Combine(npcDir, n.ScriptFile); if (!CgPort.Core.GameFiles.Exists(f)) f += ".txt";
        if (!CgPort.Core.GameFiles.Exists(f)) { bad.Add($"{n.Name}@{n.MapId}: 缺文件 {n.ScriptFile}"); continue; }
        var wm = CgPort.Server.WindowmanScript.Load(f);
        var seen = new HashSet<string>(); var q = new Queue<string>(); q.Enqueue(wm.Start); bool good = wm.Get(wm.Start) != null; if (!good) bad.Add($"{n.Name}@{n.MapId} {n.ScriptFile}: 没有起始页 [{wm.Start}]");
        while (good && q.Count > 0)
        {
            var l = q.Dequeue(); if (!seen.Add(l)) continue;
            var pg = wm.Get(l)!;
            if (!tables.Messages.ContainsKey(pg.MsgId) || pg.Tokens.Count == 0) { good = false; bad.Add($"{n.Name}@{n.MapId} {n.ScriptFile} {l}: msg {pg.MsgId} {(tables.Messages.ContainsKey(pg.MsgId) ? "" : "不存在")} 按钮 {pg.Tokens.Count}"); break; }
            foreach (var t in pg.Tokens) if (wm.Get(t) != null) q.Enqueue(t);
        }
        if (good) ok++;
    }
    Console.WriteLine($"  Windowman 全部 {total} 个，{ok} 个完整");
    foreach (var b in bad.Take(15)) Console.WriteLine("    " + b);
    Console.WriteLine(fail == 0 ? "ALL PASS" : $"{fail} FAIL");
    return fail == 0 ? 0 : 1;
}


sealed class SimHost : IScriptHost
{
    public PlayerState Player { get; } = new() { Level = 10, Gold = 1000 };
    public int X, Y;
    public int PlayerX => X;
    public int PlayerY => Y;
    public void Warp(int map, int x, int y) { }
    public void StartEncounter(int groupId) { }
    public void Log(string message) { }
}


