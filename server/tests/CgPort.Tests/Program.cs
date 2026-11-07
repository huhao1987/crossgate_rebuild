using System.Buffers.Binary;
using CgPort.Core;
using CgPort.Protocol;
using CgPort.Server;

// 极简测试框架（环境里拿不到 xunit）：每个 Check 失败都会记录，最后汇总。
int pass = 0, fail = 0;
void Check(bool cond, string name) { if (cond) pass++; else { fail++; Console.WriteLine($"FAIL: {name}"); } }

var dir = Path.Combine(Path.GetTempPath(), "cgport_tests_" + Environment.ProcessId);
Directory.CreateDirectory(Path.Combine(dir, "map"));

// ---------- 合成数据：GraphicInfo（地面 100=可走，101=水；物件 200=2×2 不可走建筑，201=可走装饰） ----------
var gi = Path.Combine(dir, "GraphicInfo_test.bin");
using (var f = File.Create(gi))
{
    void Entry(int gid, int mapId, byte flag, byte east, byte south)
    {
        var e = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(0), gid);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(20), 64);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(24), 47);
        e[28] = east; e[29] = south; e[30] = flag;
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(36), mapId);
        f.Write(e);
    }
    Entry(0, 100, 1, 1, 1); Entry(1, 101, 0, 1, 1); Entry(2, 200, 0, 2, 2); Entry(3, 201, 1, 1, 1); Entry(4, 202, 1, 1, 1);
}
var tiles = new TileInfoTable();
Check(tiles.LoadGraphicInfo(gi) == 5, "GraphicInfo 读取 5 条");

// ---------- 合成两张 10×10 服务端地图 ----------
void WriteServerMap(string path, Func<int, int, ushort> ground, Func<int, int, ushort> obj)
{
    const int W = 10, H = 10;
    var d = new byte[44 + W * H * 4];
    "LS2MAP"u8.CopyTo(d);
    BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(0x28), W);
    BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(0x2A), H);
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(44 + (y * W + x) * 2), ground(x, y));
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(44 + W * H * 2 + (y * W + x) * 2), obj(x, y));
        }
    File.WriteAllBytes(path, d);
}
// 地图 1：第 9 列是水；(4,5) 放一个 2×2 建筑（应占 (4,5),(5,5),(4,4),(5,4)）；(2,2) 放可走装饰
WriteServerMap(Path.Combine(dir, "map", "1_Field"), (x, y) => x == 9 ? (ushort)101 : (ushort)100,
    (x, y) => (x, y) == (4, 5) ? (ushort)200 : (x, y) == (2, 2) ? (ushort)201 : (x, y) == (9, 7) ? (ushort)202 : (ushort)0);
WriteServerMap(Path.Combine(dir, "map", "2_Room"), (x, y) => 100, (x, y) => 0);

var m1 = MapData.Load(Path.Combine(dir, "map", "1_Field"), 1, "Field");
Check(m1.Width == 10 && m1.Height == 10 && m1.Format == "server", "LS2MAP 尺寸与格式");
Check(m1.Ground[m1.Index(9, 0)] == 101 && m1.Objects[m1.Index(4, 5)] == 200, "LS2MAP 大端读取");

var w1 = Walkability.Build(m1, tiles);
Check(!w1.IsWalkable(9, 3), "水不可走");
Check(w1.IsWalkable(9, 7), "水上的桥（可走物件）可走");
Check(w1.IsWalkable(2, 2), "可走装饰不阻挡");
Check(!w1.IsWalkable(4, 5) && !w1.IsWalkable(5, 5) && !w1.IsWalkable(4, 4) && !w1.IsWalkable(5, 4), "建筑占位：东 + 北");
Check(w1.IsWalkable(4, 6) && w1.IsWalkable(3, 5) && w1.IsWalkable(6, 5), "建筑占位不往南/西扩散");
Check(!w1.CanStep(3, 3, 4, 4), "不能走进建筑");
Check(w1.Reachable(0, 0).Count == 100 - 10 - 4 + 1, "连通区域大小（含桥）");

// ---------- 传送表（含注释、BOM、非法行） ----------
var warpPath = Path.Combine(dir, "warp.txt");
File.WriteAllBytes(warpPath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.ASCII.GetBytes(
    "# comment\r\n1\t1\t8\t8\t2\t1\t1\r\n// another\r\nbad\tline\r\n2\t2\t0\t0\t1\t0\t0\r\n")).ToArray());
var warps = WarpTable.Load(warpPath);
Check(warps.All.Count == 2 && warps.SkippedLines == 1, "传送表解析（BOM/注释/非法行）");
Check(warps.TryGetAt(1, 8, 8, out var wp) && wp.ToMap == 2, "按源坐标查传送");

// ---------- 世界：移动校验 + 传送 ----------
var world = new World(new MapManager(Path.Combine(dir, "map"), tiles), warps);
var c = world.Spawn(1, "t", 1, 7, 7);
Check(world.Step(c, 3) == MoveResult.Warped && c.MapId == 2 && c.X == 1 && c.Y == 1, "踩到传送点后切换地图");
var c2 = world.Spawn(2, "t2", 1, 8, 3);
Check(world.Step(c2, 2) == MoveResult.Blocked && c2.X == 8, "走进水里被挡");
Check(world.StepTo(c2, 5, 3) == MoveResult.InvalidStep, "一次走多格被拒绝");

// ---------- 协议 ----------
foreach (var v in new long[] { 0, 1, 61, 62, 12345, -987654, int.MaxValue })
    Check(Base62.Decode(Base62.Encode(v)) == v, $"Base62 往返 {v}");
Check(Base62.Encode(62) == "10" && Base62.Encode(61) == "Z", "Base62 字母表");
var codec = new PacketCodec();
var text = codec.Build("Mhbt", 100, "hello world|x", -5);
var p = codec.Parse(text);
Check(p.Header == "Mhbt" && p.Fields.Count == 3, "封包拆分");
Check(codec.Int(p, 0) == 100 && codec.Str(p, 1) == "hello world|x" && codec.Int(p, 2) == -5, "封包字段往返");
var reg = new HeaderRegistry();
Check(reg.All.Count == 76 && reg.IsKnown("lDuJ") && reg.IsKnown("SWITMM"), "包头注册表 72+4");
var wire = new PlainLineWire();
Check(wire.Unwrap(wire.Wrap(text)).Single() == text, "直通线路层往返");

// proto_recv.log 解析
var logPath = Path.Combine(dir, "proto_recv.log");
File.WriteAllText(logPath, "# start\n1700000000\t5\t-1\tlDuJ\ta|b|c\n1700000001\t5\t3\tMhbt\t\n1700000002\t5\t3\tlDuJ\tx|y|z\n");
var sum = PacketLog.Summarize(PacketLog.ReadProtoRecv(logPath)).ToList();
Check(sum[0].Header == "lDuJ" && sum[0].Count == 2, "日志统计");


// ---------- 战斗数据：合成 encount / group / enemy / enemybase ----------
string Row(int n, Dictionary<int, string> cols) => string.Join('\t', Enumerable.Range(0, n).Select(i => cols.TryGetValue(i, out var v) ? v : ""));
File.WriteAllText(Path.Combine(dir, "encount.txt"), "# comment\n" +
    Row(34, new() { [0] = "500", [3] = "1", [4] = "0", [5] = "0", [6] = "9", [7] = "9", [9] = "2", [10] = "3", [11] = "10", [14] = "600", [24] = "1" }) + "\n" +
    Row(34, new() { [0] = "501", [3] = "1", [4] = "2", [5] = "2", [6] = "3", [7] = "3", [9] = "2", [10] = "1", [11] = "10", [14] = "601", [24] = "1" }) + "\n");
File.WriteAllText(Path.Combine(dir, "group.txt"),
    Row(35, new() { [0] = "G1", [2] = "600", [3] = "-1", [4] = "-1", [5] = "700", [6] = "701", [15] = "1", [16] = "1", [25] = "1", [26] = "0" }) + "\n" +
    Row(35, new() { [0] = "G2", [2] = "601", [3] = "-1", [4] = "-1", [5] = "701", [15] = "100", [25] = "0" }) + "\n");
File.WriteAllText(Path.Combine(dir, "enemy.txt"),
    Row(48, new() { [0] = "E1", [1] = "at:10;1;1|gu:1|es:1|wa:0;0", [2] = "700", [3] = "800", [4] = "3", [5] = "5", [13] = "18001", [23] = "50", [33] = "620001", [38] = "100" }) + "\n" +
    Row(48, new() { [0] = "E2", [1] = "at:5|gu:1", [2] = "701", [3] = "800", [4] = "7", [5] = "7" }) + "\n" +
    "#E3\tat:1\t702\t800\t1\t1\n");
File.WriteAllText(Path.Combine(dir, "enemybase.txt"),
    Row(46, new() { [0] = "B1", [1] = "800", [15] = "50", [16] = "0", [17] = "50", [18] = "0", [30] = "100123" }) + "\n");
var bt = BattleTables.Load(dir);
Check(bt.Encounts.Count == 2 && bt.Groups.Count == 2 && bt.Enemies.Count == 2 && bt.Bases.Count == 1, "战斗表读取（含被注释的行）");
Check(bt.Enemies[700].Ai["at"].SequenceEqual(new[] { 10, 1, 1 }) && bt.Enemies[700].Drops[0] == (18001, 50) && bt.Enemies[700].Steals[0] == (620001, 100), "敌人 AI/掉落/偷窃解析");
Check(bt.Bases[800].AnimeId == 100123 && bt.Bases[800].Elements.Sum() == 100, "敌人基础表：动画编号、属性");
Check(bt.EncountAt(1, 2, 3)!.Id == 501 && bt.EncountAt(1, 8, 8)!.Id == 500 && bt.EncountAt(2, 0, 0) == null, "按坐标取遇敌区域（小区域优先）");
var enc = new EncounterService(bt); var rng = new Random(7); bool absOk = true, cntOk = true, lvOk = true;
for (int i = 0; i < 500; i++)
{
    var r = enc.Roll(1, 8, 8, rng)!;
    absOk &= r.Enemies.Any(e => e.EnemyId == 700);
    cntOk &= r.Enemies.Count >= 1 && r.Enemies.Count <= 3;
    lvOk &= r.Enemies.All(e => e.EnemyId == 700 ? e.Level is >= 3 and <= 5 : e.Level == 7) && r.Enemies.All(e => e.AnimeId == 100123);
}
Check(absOk, "绝对出现的敌人每次都在"); Check(cntOk, "人数不超过区域上限"); Check(lvOk, "等级在范围内、动画编号正确");
Check(enc.Roll(1, 2, 2, rng)!.Enemies.Single().EnemyId == 701, "小区域只出 1 只指定敌人");

// ---------- 道具 / 招式 / 技能 / NPC ----------
File.WriteAllText(Path.Combine(dir, "itemset.txt"), Row(102, new() { [0] = "I1", [11] = "18001", [12] = "20001", [14] = "26" }) + "\n" + Row(102, new() { [0] = "I2", [11] = "620001", [12] = "20002", [14] = "21" }) + "\n");
File.WriteAllText(Path.Combine(dir, "skill.txt"), Row(19, new() { [0] = "S1", [1] = "1" }) + "\n");
File.WriteAllText(Path.Combine(dir, "tech.txt"), Row(15, new() { [0] = "T1", [3] = "9001", [5] = "1", [6] = "1" }) + "\n");
File.WriteAllText(Path.Combine(dir, "npc.txt"), Row(25, new() { [0] = "N1", [3] = "1", [8] = "1", [9] = "3", [10] = "4", [19] = "4", [20] = "100500" }) + "\n");
var gt = GameTables.Load(dir);
Check(gt.Items.Count == 2 && gt.Items[18001].GraphicId == 20001 && gt.Items[18001].Type == 26, "道具表");
Check(gt.Techs[9001].SkillId == 1 && gt.Skills.ContainsKey(1), "招式→技能");
Check(gt.NpcsOnMap(1).Single() is { X: 3, Y: 4, Dir: 4, AppearanceId: 100500 }, "NPC 位置/朝向/外观");
Check(bt.Enemies[700].Drops.All(d => gt.Items.ContainsKey(d.ItemId)) && bt.Enemies[700].Steals.All(d => gt.Items.ContainsKey(d.ItemId)), "敌人掉落/偷窃引用道具表");

// ---------- 战斗骨架 ----------
var battle = new Battle(new PlaceholderFormulas(), new Random(11));
var hero = new Combatant { Id = 0, Side = 0, Level = 10, Stats = new Stats { MaxHp = 300, Attack = 60, Defense = 30, Agility = 40 }, Hp = 300 };
battle.Units.Add(hero);
var encR = enc.Roll(1, 8, 8, new Random(5))!;
battle.AddEnemies(encR, bt, new LevelScaledStats());
Check(battle.Side(1).Count() == encR.Enemies.Count && battle.Side(1).All(u => u.Hp > 0 && u.AnimeId == 100123), "按遇敌结果生成敌方单位");

while (battle.RunTurn(new Dictionary<int, Command>()) == BattleOutcome.Ongoing && battle.Turn < 100) { }
Check(battle.Outcome == BattleOutcome.Win, "强力角色能打赢");
Check(battle.Log.Count > 0 && battle.Log.All(e => e.Damage >= 0), "战斗日志");
Check(battle.Log.Where(e => e.Killed).Select(e => e.TargetId).Distinct().Count() <= battle.Side(1).Count(), "击杀记录不重复超额");
// 防御减伤：同样的攻击，防御时伤害更低（占位公式下应约为一半）
var pf = new PlaceholderFormulas(); var a = new Combatant { Stats = new Stats { Attack = 100, Defense = 0 } };
var d1 = new Combatant { Stats = new Stats { Defense = 40 } }; var d2 = new Combatant { Stats = new Stats { Defense = 40 }, Guarding = true };
var rr = new Random(1); double s1 = 0, s2 = 0; for (int i = 0; i < 200; i++) { s1 += pf.Damage(a, d1, rr); s2 += pf.Damage(a, d2, rr); }
Check(s2 < s1 * 0.6, "防御姿态减伤");
// 敌人只会逃跑时：战斗以敌人全部逃走结束
var b2 = new Battle(new PlaceholderFormulas(), new Random(2));
b2.Units.Add(new Combatant { Id = 0, Side = 0, Stats = new Stats { MaxHp = 50, Attack = 1, Defense = 999, Agility = 1 }, Hp = 50 });
b2.Units.Add(new Combatant { Id = 1, Side = 1, Stats = new Stats { MaxHp = 9999, Agility = 50 }, Hp = 9999, Ai = BattleTables.ParseAi("at:0|gu:0|es:1|wa:0") });
while (b2.RunTurn(new Dictionary<int, Command> { [0] = new Command(CommandKind.Guard) }) == BattleOutcome.Ongoing && b2.Turn < 50) { }
Check(b2.Outcome == BattleOutcome.Win && b2.Get(1)!.Escaped, "敌人逃走后战斗结束");

// ---------- 素材解码（用原创占位素材 testdata，与 Python 参考实现对照） ----------
var td = "/home/claude/cgport/testdata";
if (Directory.Exists(td))
{
    using var al = new CgPort.Core.Assets.AssetLibrary(td, new[] { new CgPort.Core.Assets.AssetSet("TEST", "bin/Graphic_test.bin", "bin/GraphicInfo_test.bin", "bin/Anime_test.bin", "bin/AnimeInfo_test.bin", 12) });
    var first = new List<long>();
    for (int gid = 0; gid < 4; gid++)
    {
        al.Graphics.TryById(gid, out var set, out var ge);
        var img = set.Decode(ge)!; long h = 0;
        foreach (var b in img.Pixels) h = (h * 31 + b) % 1000000007;
        first.Add(h);
    }
    Check(first.SequenceEqual(new long[] { 807024865, 270799548, 597459212, 794930247 }), "C# 解码与 Python 参考实现一致");
    Check(al.Graphics.TryByMapId(100, out _, out var g100) && g100.Width == 64 && g100.Flag == 1, "按地图编号取地砖");
    var acts = al.AnimeActions(900000);
    Check(acts.Count == 40 && acts[(2, 1)].Frames.SequenceEqual(new[] { 29, 30, 31, 32 }) && acts[(2, 4)].Frames.Length == 1, "动画动作与帧");
    al.Graphics.TryById(17, out var s17, out var e17);
    var rgba = s17.Decode(e17)!.ToRgba(al.Palette);
    Check(rgba.Length == e17.Width * e17.Height * 4 && rgba.Where((b, i) => i % 4 == 3).Any(a => a == 255) && rgba.Where((b, i) => i % 4 == 3).Any(a => a == 0), "转 RGBA（含透明像素）");
}

// ---------- 单机会话：走路 → 传送 → 遇敌 → 战斗 ----------
{
    var opts = new CgPort.Client.SessionOptions { StartMap = 1, StartX = 6, StartY = 6, Seed = 3, EncounterScale = 10, EncounterGraceSteps = 0 };
    var sess = new CgPort.Client.LocalSession(new World(new MapManager(Path.Combine(dir, "map"), tiles), warps), bt, gt, Path.Combine(dir, "map"), opts);
    var entered = new List<int>(); int moves = 0; CgPort.Client.BattleView? started = null; BattleOutcome? ended = null; int turnEvents = 0;
    sess.MapEntered += (m, x, y) => entered.Add(m.MapId);
    sess.Moved += (x, y, d) => moves++;
    sess.BattleStarted += v => started = v;
    sess.TurnResolved += (ev, v) => turnEvents += ev.Count;
    sess.BattleEnded += o => ended = o;
    sess.Start();
    Check(entered.SequenceEqual(new[] { 1 }) && sess.X == 6 && sess.Y == 6 && File.Exists(sess.CurrentMap!.MapFile), "会话开始：进入初始地图");
    Check(sess.NpcsOnCurrentMap().Count == 1, "当前地图 NPC");
    // 遇敌概率放大后，第一步就会进入战斗
    sess.Step(4);
    Check(started != null && sess.InBattle && started.Units.Count(u => u.Side == 1) >= 1, "走在遇敌区域里触发战斗");
    Check(sess.Step(4) == MoveResult.Blocked, "战斗中不能移动");
    for (int i = 0; i < 100 && sess.InBattle; i++) sess.SubmitCommand(new Command(CommandKind.Attack));
    Check(ended == BattleOutcome.Win && !sess.InBattle && turnEvents > 0, "提交指令直到战斗结束");
    // 关闭遇敌，走到 (8,8) 的传送点
    var sess2 = new CgPort.Client.LocalSession(new World(new MapManager(Path.Combine(dir, "map"), tiles), warps), bt, gt, Path.Combine(dir, "map"),
        new CgPort.Client.SessionOptions { StartMap = 1, StartX = 7, StartY = 7, EncountersEnabled = false });
    var e2 = new List<int>(); sess2.MapEntered += (m, x, y) => e2.Add(m.MapId);
    sess2.Start();
    Check(sess2.Step(3) == MoveResult.Warped && e2.SequenceEqual(new[] { 1, 2 }) && sess2.CurrentMap!.MapId == 2, "会话中传送换图");
}

// ---------- GBK ----------
Check(CgPort.Core.Text.Gbk.Decode(new byte[] { 0xb2, 0xe2, 0xca, 0xd4, (byte)'A' }) == "测试A", "GBK 解码");

// ---------- 背包、经验、存档 ----------
{
    var ps = new PlayerState();
    Check(ps.AddItem(1, 150) == 150 && ps.Items.Count == 2 && ps.CountOf(1) == 150, "道具堆叠（每格 99）");
    Check(ps.RemoveItem(1, 100) == 100 && ps.CountOf(1) == 50 && ps.Items.Count == 1, "移除道具");
    for (int i = 0; i < 30; i++) ps.AddItem(1000 + i);
    Check(ps.Items.Count == PlayerState.BagSize && ps.AddItem(9999) == 0, "背包满了放不进");
    File.WriteAllText(Path.Combine(dir, "explist.txt"), "0\n0\n15\n65\n175\n400\n");
    File.WriteAllText(Path.Combine(dir, "enemyexplist.txt"), "0\n2\n4\n6\n8\n10\n12\n14\n");
    var et = ExpTable.Load(dir);
    Check(et.ToReach(3) == 65 && et.ForEnemy(4) == 8 && et.ForEnemy(500) == 14 && et.MaxLevel == 5, "经验表");
    var prog = new Progression(et, bt);
    var p2 = new PlayerState { Level = 1 };
    var dropper = new Combatant { Side = 1, Level = 7, EnemyId = 700, Hp = 0 };     // 700 号敌人：掉落 18001，掉落率 50
    int got = 0; var rr2 = new Random(4);
    for (int i = 0; i < 400; i++) { var r = prog.Grant(p2, new[] { dropper }, rr2); got += r.Items.Count; }
    Check(p2.Level == 5 && p2.Exp == 400 * 14, "累计经验升级（到表顶为止）");
    Check(got > 0 && p2.CountOf(18001) == got, "掉落进背包");
    var savePath = Path.Combine(dir, "save", "slot1.json");
    p2.Flags[42] = 1; p2.Save(savePath);
    var p3 = PlayerState.Load(savePath)!;
    Check(p3.Level == 5 && p3.Exp == p2.Exp && p3.CountOf(18001) == got && p3.Flags[42] == 1, "存档读档");
}

// ---------- NPC 脚本（自编测试脚本，验证语法与执行） ----------
{
    var script = NpcScript.Parse(
        "block Item 500 >= 1\n" +
        "endwindow \"already have\"\n" +
        "\n" +
        "block EndEvent 7 == 1\n" +
        "window \"page1\",next\n" +
        "window \"want it?\",yes,no\n" +
        "window_no\n" +
        "window_yes ItemSpace < 1\n" +
        "endwindow \"bag full\"\n" +
        "window_yes\n" +
        "giveItem 500\n" +
        "AddGold -100\n" +
        "endevent 8 = 1\n" +
        "\n" +
        "block\n" +
        "endwindow \"default\"\n");
    Check(script.Blocks.Count == 3, "脚本分块");
    var host = new TestHost();
    var run = new ScriptRun(script, host, new Random(1));
    Check(run.Current!.Text == "default" && run.Current.IsLast, "条件都不满足时走默认规则");
    host.Player.Flags[7] = 1;
    run = new ScriptRun(script, host, new Random(1));
    Check(run.Current!.Text == "page1" && run.Current.Buttons.SequenceEqual(new[] { "next" }), "事件标记满足：第一页");
    run.Press("next");
    Check(run.Current!.Text == "want it?" && run.Current.Buttons.SequenceEqual(new[] { "yes", "no" }), "第二页：是/否");
    run.Press("yes");
    Check(host.Player.CountOf(500) == 1 && host.Player.Gold == 900 && host.Player.Flags[8] == 1 && run.Finished, "按'是'：给道具、扣钱、设标记");
    run = new ScriptRun(script, host, new Random(1));
    Check(run.Current!.Text == "already have", "已持有道具时走第一条规则");
    var sw2 = NpcScript.Parse("block\nwindow \"go?\",yes,no\nwindow_yes\nwarp 0,2,5,5\n");
    var h2 = new TestHost(); var r2 = new ScriptRun(sw2, h2, new Random(1)); r2.Press("yes");
    Check(h2.Warped == (2, 5, 5), "脚本传送");
    var s3 = NpcScript.Parse("block\nwindow \"pick\",ok\nwindow_ok\ngiveitem (601%1,602%0)\n");
    var h3 = new TestHost(); var r3 = new ScriptRun(s3, h3, new Random(1)); r3.Press("ok");
    Check(h3.Player.CountOf(601) == 1 && h3.Player.CountOf(602) == 0, "按权重随机给道具");
    var s5 = NpcScript.Parse("block\nwindow \"q\",ok\nwindow_ok\nif Item 700 >= 1\ngiveitem 701\nelse\ngiveitem 702\nendif\naddgold 5\n");
    var h5 = new TestHost(); var r5 = new ScriptRun(s5, h5, new Random(1)); r5.Press("ok");
    Check(h5.Player.CountOf(702) == 1 && h5.Player.CountOf(701) == 0 && h5.Player.Gold == 1005, "if/else：条件不成立走 else，endif 后继续");
    var h6 = new TestHost(); h6.Player.AddItem(700); var r6 = new ScriptRun(s5, h6, new Random(1)); r6.Press("ok");
    Check(h6.Player.CountOf(701) == 1 && h6.Player.CountOf(702) == 0, "if/else：条件成立走 if");
    var s4 = NpcScript.Parse("block Fame >= 10\nendwindow \"x\"\nblock\nendwindow \"y\"\n");
    Check(new ScriptRun(s4, new TestHost(), new Random(1)).Current!.Text == "y", "暂不支持的条件视为不成立");
}

// ---------- 会话里的对话与奖励 ----------
{
    Directory.CreateDirectory(Path.Combine(dir, "npc"));
    File.WriteAllText(Path.Combine(dir, "npc", "guide.txt"), "block\nwindow \"hello\",yes,no\nwindow_yes\ngiveitem 18001,3\n");
    File.WriteAllText(Path.Combine(dir, "npc.txt"), Row(25, new() { [0] = "N1", [3] = "1", [8] = "1", [9] = "3", [10] = "4", [19] = "4", [20] = "100500", [24] = "guide.txt" }) + "\n");
    var gt2 = GameTables.Load(dir);
    var sess3 = new CgPort.Client.LocalSession(new World(new MapManager(Path.Combine(dir, "map"), tiles), warps), bt, gt2, Path.Combine(dir, "map"),
        new CgPort.Client.SessionOptions { StartMap = 1, StartX = 3, StartY = 6, EncountersEnabled = false });
    DialogPage? page = null; int closed = 0;
    sess3.DialogChanged += d => { if (d == null) closed++; else page = d; };
    sess3.Start();
    Check(sess3.TalkTo(1) && page!.Text == "hello" && sess3.InDialog, "走近 NPC 开始对话");
    Check(sess3.Step(0) == MoveResult.Blocked, "对话中不能移动");
    sess3.Answer("yes");
    Check(sess3.Player.CountOf(18001) == 3 && !sess3.InDialog && closed == 1, "对话分支给道具后结束");
    Check(sess3.ItemInfo(18001)?.GraphicId == 20001, "道具信息查询");
    var far = new CgPort.Client.LocalSession(new World(new MapManager(Path.Combine(dir, "map"), tiles), warps), bt, gt2, Path.Combine(dir, "map"),
        new CgPort.Client.SessionOptions { StartMap = 1, StartX = 8, StartY = 8, EncountersEnabled = false });
    far.Start();
    Check(!far.TalkTo(1), "离太远不能对话");
}

Directory.Delete(dir, true);
Console.WriteLine($"通过 {pass}，失败 {fail}");
return fail == 0 ? 0 : 1;


sealed class TestHost : IScriptHost
{
    public PlayerState Player { get; } = new();
    public int PlayerX => 0;
    public int PlayerY => 0;
    public (int, int, int)? Warped;
    public void Warp(int map, int x, int y) => Warped = (map, x, y);
    public void StartEncounter(int groupId) { }
    public void Log(string message) { }
}
