using CgPort.Core;

namespace CgPort.Server;

/// <summary>一场战斗里的一个敌人实例。</summary>
public sealed record EnemyInstance(int Slot, int EnemyId, int BaseId, int Level, int AnimeId);

/// <summary>一次遇敌的结果。</summary>
public sealed record EncounterResult(int EncountId, int GroupId, IReadOnlyList<EnemyInstance> Enemies);

/// <summary>
/// 遇敌：按坐标找到遇敌区域 → 按权重选敌人组 → 生成敌人列表。
/// 规则说明（推测，等日志/实测核对）：
///  - 组内"绝对出现"的槽位一定出现；其它槽位按权重抽取，直到达到人数（不超过区域的 MaxEnemies）。
///  - 人数在 1..MaxEnemies 之间随机。
///  - 等级在敌人的 [LevelMin, LevelMax] 内均匀随机。
/// </summary>
public sealed class EncounterService
{
    private readonly BattleTables _t;
    public EncounterService(BattleTables tables) => _t = tables;

    public EncounterResult? Roll(int mapId, int x, int y, Random rng)
    {
        var area = _t.EncountAt(mapId, x, y);
        if (area == null || area.Groups.Count == 0) return null;
        int gid = PickWeighted(area.Groups.Select(g => (g.GroupId, g.Weight)).ToList(), rng);
        if (!_t.Groups.TryGetValue(gid, out var group) || group.Slots.Count == 0) return null;

        int want = rng.Next(1, Math.Max(1, area.MaxEnemies) + 1);
        var picked = new List<(int EnemyId, int Slot)>();
        for (int i = 0; i < group.Slots.Count; i++)
            if (group.Slots[i].Absolute) picked.Add((group.Slots[i].EnemyId, i));
        var pool = group.Slots.Select((s, i) => (s, i)).Where(p => !p.s.Absolute).Select(p => (p.s.EnemyId * 100 + p.i, p.s.Weight)).ToList();
        while (picked.Count < want && pool.Count > 0)
        {
            int key = PickWeighted(pool, rng);
            picked.Add((key / 100, key % 100));
        }
        if (picked.Count == 0) picked.Add((group.Slots[0].EnemyId, 0));

        var list = new List<EnemyInstance>();
        foreach (var (eid, slot) in picked.Take(10))
        {
            if (!_t.Enemies.TryGetValue(eid, out var en)) continue;
            int lv = rng.Next(en.LevelMin, en.LevelMax + 1);
            int anime = _t.Bases.TryGetValue(en.BaseId, out var b) ? b.AnimeId : 0;
            list.Add(new EnemyInstance(list.Count, eid, en.BaseId, lv, anime));
        }
        return list.Count == 0 ? null : new EncounterResult(area.Id, gid, list);
    }

    private static int PickWeighted(IReadOnlyList<(int Key, int Weight)> items, Random rng)
    {
        long total = items.Sum(i => (long)Math.Max(0, i.Weight));
        if (total <= 0) return items[rng.Next(items.Count)].Key;
        long r = (long)(rng.NextDouble() * total);
        foreach (var (k, w) in items)
        {
            r -= Math.Max(0, w);
            if (r < 0) return k;
        }
        return items[^1].Key;
    }
}
