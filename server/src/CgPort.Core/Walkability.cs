namespace CgPort.Core;

/// <summary>
/// 通行网格。规则（均已用真实数据验证，见 docs/RESEARCH.md）：
///  1. 地面图像标志 = 1 才可走；地面查不到图像的格子视为不可走。
///  2. 物件标志 != 1 时阻挡其占位区：从锚点往东（+x）East 格、往北（-y）South 格。
/// </summary>
public sealed class Walkability
{
    public int Width { get; }
    public int Height { get; }
    private readonly bool[] _walk;
    public int MissingGround { get; }

    private Walkability(int w, int h, bool[] walk, int missing) { Width = w; Height = h; _walk = walk; MissingGround = missing; }

    public bool IsWalkable(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _walk[y * Width + x];

    public int CountWalkable() => _walk.Count(b => b);

    /// <summary>可走物件（如桥、木板路）是否把它占的格子变成可走。默认开启，依据见 docs/RESEARCH.md。</summary>
    public static bool PassableObjectsMakeFloor { get; set; } = true;

    public static Walkability Build(MapData map, TileInfoTable tiles)
    {
        int w = map.Width, h = map.Height, missing = 0;
        var walk = new bool[w * h];
        for (int i = 0; i < walk.Length; i++)
        {
            if (tiles.TryGet(map.Ground[i], out var g)) walk[i] = g.Passable;
            else if (map.Ground[i] != 0) missing++;
        }
        if (PassableObjectsMakeFloor)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int pm = map.Objects[y * w + x];
                    if (pm == 0 || !tiles.TryGet(pm, out var po) || !po.Passable) continue;
                    for (int dy = 0; dy < Math.Max(1, (int)po.South); dy++)
                        for (int dx = 0; dx < Math.Max(1, (int)po.East); dx++)
                        {
                            int cx = x + dx, cy = y - dy;
                            if (cx >= 0 && cy >= 0 && cx < w && cy < h) walk[cy * w + cx] = true;
                        }
                }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int mid = map.Objects[y * w + x];
                if (mid == 0 || !tiles.TryGet(mid, out var o) || o.Passable) continue;
                int ex = Math.Max(1, (int)o.East), sy = Math.Max(1, (int)o.South);
                for (int dy = 0; dy < sy; dy++)
                    for (int dx = 0; dx < ex; dx++)
                    {
                        int cx = x + dx, cy = y - dy;
                        if (cx >= 0 && cy >= 0 && cx < w && cy < h) walk[cy * w + cx] = false;
                    }
            }
        return new Walkability(w, h, walk, missing);
    }

    /// <summary>从起点出发，按 8 方向（斜走要求两侧不全堵）可达的格子数，用于连通性检查。</summary>
    public HashSet<int> Reachable(int sx, int sy, int limit = int.MaxValue)
    {
        var seen = new HashSet<int>();
        if (!IsWalkable(sx, sy)) return seen;
        var q = new Queue<(int, int)>();
        q.Enqueue((sx, sy)); seen.Add(sy * Width + sx);
        while (q.Count > 0 && seen.Count < limit)
        {
            var (x, y) = q.Dequeue();
            foreach (var (dx, dy) in Directions.Deltas)
            {
                int nx = x + dx, ny = y + dy;
                if (!CanStep(x, y, nx, ny)) continue;
                if (seen.Add(ny * Width + nx)) q.Enqueue((nx, ny));
            }
        }
        return seen;
    }

    public bool CanStep(int x, int y, int nx, int ny)
    {
        int dx = nx - x, dy = ny - y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0)) return false;
        if (!IsWalkable(nx, ny)) return false;
        if (dx != 0 && dy != 0 && !IsWalkable(x + dx, y) && !IsWalkable(x, y + dy)) return false;
        return true;
    }
}

/// <summary>地图格子空间的 8 个方向（x 东、y 南）。编号与客户端动画方向的对应关系待确认。</summary>
public static class Directions
{
    public static readonly (int dx, int dy)[] Deltas =
    {
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1),
    };
}
