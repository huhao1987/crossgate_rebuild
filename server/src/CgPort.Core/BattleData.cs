namespace CgPort.Core;

// 服务端战斗相关数据表。列位置由真实数据的表间引用关系推断（见 docs/RESEARCH.md），
// 标注"推测"的字段语义还需要拿运行中的游戏核对。以 # 或 // 开头的行是注释（含被注释掉的数据行）。

/// <summary>encount.txt 一行：地图上的一个遇敌区域。</summary>
public sealed record Encount(
    int Id, int MapId, int X1, int Y1, int X2, int Y2,
    int Kind,        // c9：1–3，推测为遇敌类型
    int MaxEnemies,  // c10：1–10
    int Rate,        // c11：推测为遇敌率/步数参数（常见 10、1、100）
    int NextId, int NextProb,                       // c12, c13：连锁遇敌
    IReadOnlyList<(int GroupId, int Weight)> Groups) // c14–c23 敌人组编号，c24–c33 相对权重
{
    public bool Contains(int x, int y) => x >= X1 && x <= X2 && y >= Y1 && y <= Y2;
    public long Area => (long)(X2 - X1 + 1) * (Y2 - Y1 + 1);
}

/// <summary>group.txt 一行：一个敌人组。</summary>
public sealed record EnemyGroup(
    int Id, int AppearByItem, int NotAppearByItem,                       // c2, c3, c4
    IReadOnlyList<(int EnemyId, int Weight, bool Absolute)> Slots);       // c5–c14, c15–c24, c25–c34

/// <summary>enemy.txt 一行：一种敌人（引用 enemybase）。</summary>
public sealed record Enemy(
    int Id, int BaseId, int LevelMin, int LevelMax,                       // c2, c3, c4, c5
    IReadOnlyDictionary<string, int[]> Ai,                                // c1：如 at/gu/es/wa（推测：攻击/防御/逃跑/待机权重）
    IReadOnlyList<(int ItemId, int Prob)> Drops,                          // c13–c22 道具编号（100% 能在 itemset 找到）/ c23–c32 掉落率
    IReadOnlyList<(int ItemId, int Prob)> Steals);                        // c33–c37 道具编号（100% 能在 itemset 找到）/ c38–c42 概率（推测为可偷窃道具）

/// <summary>enemybase.txt 一行：敌人基础属性。</summary>
public sealed record EnemyBase(
    int Id, int AnimeId,                 // c1, c30（取值范围与动画编号一致）
    int[] Elements,                      // c15–c18：推测为地水火风（合计多为 100 或 200）
    int[] Resists,                       // c19–c24：推测为 6 种异常状态抗性
    int[] Raw);                          // 全部数字列原样保留，便于以后补充语义

public sealed class BattleTables
{
    public Dictionary<int, Encount> Encounts { get; } = new();
    public Dictionary<int, EnemyGroup> Groups { get; } = new();
    public Dictionary<int, Enemy> Enemies { get; } = new();
    public Dictionary<int, EnemyBase> Bases { get; } = new();
    private readonly Dictionary<int, List<Encount>> _byMap = new();

    public IReadOnlyList<Encount> EncountsOnMap(int mapId) => _byMap.TryGetValue(mapId, out var l) ? l : Array.Empty<Encount>();

    /// <summary>某个坐标适用的遇敌区域：包含该点的区域里面积最小的（推测；ZORDER 语义未确认）。</summary>
    public Encount? EncountAt(int mapId, int x, int y)
    {
        Encount? best = null;
        foreach (var e in EncountsOnMap(mapId))
            if (e.Contains(x, y) && (best == null || e.Area < best.Area)) best = e;
        return best;
    }

    public static BattleTables Load(string dataDir)
    {
        var t = new BattleTables();
        foreach (var c in Rows(Path.Combine(dataDir, "encount.txt"), 34))
        {
            var groups = new List<(int, int)>();
            for (int k = 0; k < 10; k++) { int g = I(c, 14 + k); if (g > 0) groups.Add((g, Math.Max(0, I(c, 24 + k)))); }
            int x1 = I(c, 4), y1 = I(c, 5), x2 = I(c, 6), y2 = I(c, 7);
            var e = new Encount(I(c, 0), I(c, 3), Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2),
                I(c, 9), I(c, 10), I(c, 11), I(c, 12), I(c, 13), groups);
            if (e.Id <= 0) continue;
            t.Encounts[e.Id] = e;
            if (!t._byMap.TryGetValue(e.MapId, out var l)) t._byMap[e.MapId] = l = new();
            l.Add(e);
        }
        foreach (var c in Rows(Path.Combine(dataDir, "group.txt"), 35))
        {
            var slots = new List<(int, int, bool)>();
            for (int k = 0; k < 10; k++) { int id = I(c, 5 + k); if (id > 0) slots.Add((id, Math.Max(0, I(c, 15 + k)), I(c, 25 + k) == 1)); }
            var g = new EnemyGroup(I(c, 2), I(c, 3), I(c, 4), slots);
            if (g.Id > 0) t.Groups[g.Id] = g;
        }
        foreach (var c in Rows(Path.Combine(dataDir, "enemy.txt"), 48))
        {
            var drops = new List<(int, int)>();
            for (int k = 0; k < 10; k++) { int id = I(c, 13 + k); if (id > 0) drops.Add((id, I(c, 23 + k))); }
            var steals = new List<(int, int)>();
            for (int k = 0; k < 5; k++) { int id = I(c, 33 + k); if (id > 0) steals.Add((id, I(c, 38 + k))); }
            var en = new Enemy(I(c, 2), I(c, 3), I(c, 4), Math.Max(I(c, 4), I(c, 5)), ParseAi(c.Length > 1 ? c[1] : ""), drops, steals);
            if (en.Id > 0) t.Enemies[en.Id] = en;
        }
        foreach (var c in Rows(Path.Combine(dataDir, "enemybase.txt"), 46))
        {
            var raw = Enumerable.Range(0, c.Length).Select(i => I(c, i)).ToArray();
            var b = new EnemyBase(I(c, 1), I(c, 30), Enumerable.Range(15, 4).Select(i => I(c, i)).ToArray(),
                Enumerable.Range(19, 6).Select(i => I(c, i)).ToArray(), raw);
            if (b.Id > 0) t.Bases[b.Id] = b;
        }
        return t;
    }

    private static IEnumerable<string[]> Rows(string path, int minCols)
    {
        foreach (var line in TabTable.ReadLines(path))
        {
            var c = line.Split('\t');
            if (c.Length >= Math.Min(minCols, 8)) yield return c;
        }
    }

    private static int I(string[] c, int i) => i < c.Length && int.TryParse(c[i].Trim(), out int v) ? v : 0;

    /// <summary>"at:10;1;1|gu:1|es:1|wa:0;0;0" → { at:[10,1,1], gu:[1], ... }</summary>
    public static Dictionary<string, int[]> ParseAi(string s)
    {
        var d = new Dictionary<string, int[]>();
        foreach (var part in s.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            int k = part.IndexOf(':');
            if (k <= 0) continue;
            d[part[..k]] = part[(k + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out int v) ? v : 0).ToArray();
        }
        return d;
    }
}
