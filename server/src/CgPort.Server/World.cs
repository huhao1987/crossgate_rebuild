using CgPort.Core;

namespace CgPort.Server;

/// <summary>按需加载并缓存地图及其通行网格。</summary>
public sealed class MapManager
{
    private readonly Dictionary<int, (string Path, string Name)> _index;
    private readonly TileInfoTable _tiles;
    private readonly Dictionary<int, (MapData Map, Walkability Walk)> _cache = new();

    public MapManager(string serverMapDir, TileInfoTable tiles)
    {
        _index = MapData.IndexServerMapDir(serverMapDir);
        _tiles = tiles;
    }

    public IReadOnlyCollection<int> MapIds => _index.Keys;
    public bool Has(int id) => _index.ContainsKey(id);
    public string NameOf(int id) => _index.TryGetValue(id, out var e) ? e.Name : "";

    public (MapData Map, Walkability Walk) Get(int id)
    {
        if (_cache.TryGetValue(id, out var c)) return c;
        if (!_index.TryGetValue(id, out var e)) throw new KeyNotFoundException($"地图 {id} 不存在");
        var map = MapData.Load(e.Path, id, e.Name);
        c = (map, Walkability.Build(map, _tiles));
        _cache[id] = c;
        return c;
    }
}

/// <summary>角色在世界中的位置。</summary>
public sealed class Character
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public int MapId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Dir { get; set; }
}

public enum MoveResult { Ok, Warped, Blocked, InvalidStep }

/// <summary>
/// 世界模型 MVP：逐格移动校验 + 传送。客户端发来的移动请求先经过这里，
/// 不合法的一步直接拒绝（防止穿墙/瞬移），踩到传送点时切换地图。
/// </summary>
public sealed class World
{
    public MapManager Maps { get; }
    public WarpTable Warps { get; }
    private readonly Dictionary<int, Character> _chars = new();

    public World(MapManager maps, WarpTable warps) { Maps = maps; Warps = warps; }

    public Character Spawn(int id, string name, int mapId, int x, int y)
    {
        var c = new Character { Id = id, Name = name, MapId = mapId, X = x, Y = y };
        _chars[id] = c;
        return c;
    }

    public MoveResult Step(Character c, int dir)
    {
        if (dir < 0 || dir > 7) return MoveResult.InvalidStep;
        var (dx, dy) = Directions.Deltas[dir];
        return StepTo(c, c.X + dx, c.Y + dy, dir);
    }

    public MoveResult StepTo(Character c, int nx, int ny, int dir = -1)
    {
        var (_, walk) = Maps.Get(c.MapId);
        if (Math.Abs(nx - c.X) > 1 || Math.Abs(ny - c.Y) > 1) return MoveResult.InvalidStep;
        if (!walk.CanStep(c.X, c.Y, nx, ny) && !Warps.TryGetAt(c.MapId, nx, ny, out _)) return MoveResult.Blocked;
        c.X = nx; c.Y = ny;
        if (dir >= 0) c.Dir = dir;
        if (Warps.TryGetAt(c.MapId, nx, ny, out var w) && Maps.Has(w.ToMap))
        {
            c.MapId = w.ToMap; c.X = w.ToX; c.Y = w.ToY;
            return MoveResult.Warped;
        }
        return MoveResult.Ok;
    }
}
