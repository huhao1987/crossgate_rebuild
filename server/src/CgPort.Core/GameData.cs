namespace CgPort.Core;

// 道具 / 招式 / 技能 / NPC 表。只取已用表间引用确认的列；名称等文本列是 GBK，原样保留字节（以 Latin1 字符串承载），
// 需要显示时再按 GBK 解码。列位置依据见 docs/RESEARCH.md。

/// <summary>itemset.txt：c11 道具编号（覆盖敌人引用的全部道具）、c12 道具图像编号、c14 道具类型。</summary>
public sealed record Item(int Id, int GraphicId, int Type, string RawName)
{
    public string Name => CgPort.Core.Text.Gbk.FromLatin1(RawName).TrimStart('#');
}

/// <summary>tech.txt：c3 招式编号、c5 所属技能编号（100% 能在 skill.txt 找到）、c6 推测为等级。</summary>
public sealed record Tech(int Id, int SkillId, int Level, string RawName);

/// <summary>skill.txt：c1 技能编号。</summary>
public sealed record Skill(int Id, string RawName);

/// <summary>npc.txt：c3 编号、c8 地图、c9/c10 坐标（100% 在地图范围内）、c19 朝向、c20 外观编号、c24 脚本文件名（57% 能在 data/npc 找到）。</summary>
/// RawName：npc.txt 第 2 列（显示名，如"药剂师波洛姆"；第 1 列是类型 Event/Itemshop2…）。空 = 不显示名字。
public sealed record Npc(int Id, int MapId, int X, int Y, int Dir, int AppearanceId, string RawName, string ScriptFile = "", string Type = "")
{
    public string Name => CgPort.Core.Text.Gbk.FromLatin1(RawName).TrimStart('#');
}

public sealed class GameTables
{
    public Dictionary<int, Item> Items { get; } = new();
    public Dictionary<int, Tech> Techs { get; } = new();
    public Dictionary<int, Skill> Skills { get; } = new();
    public Dictionary<int, Npc> Npcs { get; } = new();
    /// <summary>msg.txt：编号 → 文字（GBK 已解码；文件里的 \n 两个字符原样保留，显示时再换行）。</summary>
    public Dictionary<int, string> Messages { get; } = new();
    private readonly Dictionary<int, List<Npc>> _npcByMap = new();

    public IReadOnlyList<Npc> NpcsOnMap(int mapId) => _npcByMap.TryGetValue(mapId, out var l) ? l : Array.Empty<Npc>();

    public static GameTables Load(string dataDir)
    {
        var t = new GameTables();
        foreach (var c in Rows(Path.Combine(dataDir, "itemset.txt")))
            if (I(c, 11) is int id and > 0) t.Items[id] = new Item(id, I(c, 12) ?? 0, I(c, 14) ?? 0, c[0]);
        foreach (var c in Rows(Path.Combine(dataDir, "tech.txt")))
            if (I(c, 3) is int id and > 0) t.Techs[id] = new Tech(id, I(c, 5) ?? 0, I(c, 6) ?? 0, c[0]);
        foreach (var c in Rows(Path.Combine(dataDir, "skill.txt")))
            if (I(c, 1) is int id and > 0) t.Skills[id] = new Skill(id, c[0]);
        foreach (var c in Rows(Path.Combine(dataDir, "npc.txt")))
        {
            if (I(c, 3) is not int id || id <= 0) continue;
            var n = new Npc(id, I(c, 8) ?? 0, I(c, 9) ?? 0, I(c, 10) ?? 0, I(c, 19) ?? 0, I(c, 20) ?? 0, c.Length > 1 ? c[1].Trim() : "", c.Length > 24 ? c[24].Trim() : "", c[0].Trim());
            t.Npcs[id] = n;
            if (!t._npcByMap.TryGetValue(n.MapId, out var l)) t._npcByMap[n.MapId] = l = new();
            l.Add(n);
        }
        var msg = Path.Combine(dataDir, "msg.txt");
        if (GameFiles.Exists(msg))
            foreach (var line in CgPort.Core.Text.Gbk.ReadFile(msg).Split('\n'))
            {
                var c = line.TrimEnd('\r').Split('\t');
                if (c.Length >= 2 && int.TryParse(c[0], out var id)) t.Messages[id] = c[1];
            }
        return t;
    }

    private static IEnumerable<string[]> Rows(string path) =>
        GameFiles.Exists(path) ? TabTable.ReadLines(path).Select(l => l.Split('\t')) : Enumerable.Empty<string[]>();

    private static int? I(string[] c, int i) => i < c.Length && int.TryParse(c[i].Trim(), out int v) ? v : null;
}
