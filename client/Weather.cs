using Godot;
namespace CgPort.Godot;

/// <summary>
/// 天气（EF = zzd 标志 强度，官方 exe 0x453cc9 / 0x436cd9 / 0x436d9b / 0x436fa3 / 0x4125d1 反汇编）：
///   标志 1 = 雨、2 = 雪、4 = 彩纸（三种可叠加），强度 0–7；标志 0 全部停。
///   标志 0x38（8/16/32）= 飘云（exe 0x436b69 / 0x40e0e2 / 0x40df62）：图 14648（640×956）切 6 块
///     （0 (0,0,300,290) 1 (320,0,320,230) 2 (0,290,260,220) 蓝；3 (0,532,314,278) 4 (316,234,324,300) 5 (320,536,320,376) 白）。
///     普通地图每 500 帧一朵：白块（3–5），速度 4–8，方向 12（右下 45°）；
///     飞船地图（59529 修拉特瓦尔号、38251）飞行中每 10 帧一朵：6 块随机（蓝块淡些），速度 63，方向 14（往下偏右）。
///     速度 s、方向 d 每帧走 方向表 0x495a98[s−1][d] / 256 像素（方向 12 = (45,45)·s，14 = (24,59)·s）。
///     出生点（相对屏幕）：一半 x∈[−宽,−宽/2]、y∈[−高,高]，一半 x∈[−宽,宽]、y∈[−高,−高/2]；钉在地图上随镜头卷动；左上角超过 (宽+320, 高+240) 删掉。
///   雨：每帧生成 强度+1 条，x 在 [0,宽)，竖直下落 12 像素/帧；贴图 1×20、颜色 ARGB 80DDFFFF。
///   雪：强度 &lt;3 时每 (51−25×强度) 毫秒生成 1 片，否则每帧 2×强度−4 片；x 在 [−宽/2, 3宽/2)，
///       一半 vx=rand%2 vy=3+rand%2，一半 vx=1 vy=4+rand%2；贴图两种 4×4 花样（调色板 159/160 色）。
///   彩纸：每帧 强度 片，y 从 −10 开始，vx 同上、vy 4/5 起；每片随机 0–9 的宽高、随机色（调色板 10 色）。
///   粒子超过屏幕底部就删掉。官方主循环按帧算，这里固定 60 帧/秒步进。
/// </summary>
public partial class Weather : Node2D
{
    public Color[] Palette = new Color[256];
    private int _flags, _level;
    private double _acc, _snowMs;
    private readonly RandomNumberGenerator _rng = new();
    private struct P { public int Type, Size, W, H, Color; public float X, Y, Vx, Vy; }
    private readonly List<P> _ps = new();
    /// <summary>云图 14648（Main 设）；镜头左上角的世界坐标（云钉在地图上）；当前地图号（飞船另一套节奏）。</summary>
    public Texture2D? CloudTex;
    public Func<Vector2> Camera = () => Vector2.Zero;
    public Func<int> MapId = () => 0;
    private struct Cloud { public Vector2 World; public int Piece, Speed, Dir; }
    private readonly List<Cloud> _clouds = new();
    private int _cloudFrame;
    private static readonly Rect2[] Pieces = { new(0, 0, 300, 290), new(320, 0, 320, 230), new(0, 290, 260, 220), new(0, 532, 314, 278), new(316, 234, 324, 300), new(320, 536, 320, 376) };
    private static readonly Vector2[] Dirs = { new(45, 45), new(35, 53), new(24, 59), new(12, 62), new(0, 64) };   // 方向 12–16 的第 1 行
    public int CloudCount => _clouds.Count;
    private static readonly int[,] Snow1 = { { 2, 1, 1, 2 }, { 1, 1, 1, 1 }, { 1, 1, 1, 1 }, { 2, 1, 1, 2 } };
    private static readonly int[,] Snow2 = { { 0, 2, 1, 2 }, { 0, 1, 1, 1 }, { 0, 2, 1, 2 }, { 0, 0, 0, 0 } };
    private static readonly int[] Confetti = { 0xea, 0x0c, 0xd8, 0x90, 0xb4, 0xf9, 0x2f, 0xfb, 0xde, 0x10 };
    private static readonly Color Rain = new(0xdd / 255f, 1f, 1f, 0x80 / 255f);

    public int Flags => _flags;
    public int Level => _level;
    public int Count => _ps.Count;

    public void Set(int flags, int level)
    {
        _flags = flags; _level = Math.Clamp(level, 0, 7);
        if (flags == 0) { _ps.Clear(); _clouds.Clear(); }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_flags == 0 && _ps.Count == 0 && _clouds.Count == 0) return;
        _acc += delta;
        int steps = 0;
        while (_acc >= 1 / 60.0 && steps < 4) { _acc -= 1 / 60.0; Step(1000 / 60.0); steps++; }
        if (steps >= 4) _acc = 0;
        QueueRedraw();
    }

    private int R(int n) => (int)(_rng.Randi() % (uint)n);

    private void Step(double ms)
    {
        var size = GetViewportRect().Size;
        int w = (int)size.X, h = (int)size.Y;
        if ((_flags & 1) != 0)
            for (int i = 0; i < _level + 1; i++) _ps.Add(new P { Type = 1, X = R(Math.Max(1, w)), Y = 0, Vx = 0, Vy = 12 });
        if ((_flags & 2) != 0)
        {
            if (_level < 3) { _snowMs += ms; if (_snowMs >= 51 - 25 * _level) { _snowMs = 0; _ps.Add(Flake(2, w, 0)); } }
            else for (int i = 0; i < 2 * _level - 4; i++) _ps.Add(Flake(2, w, -10));
        }
        if ((_flags & 4) != 0)
            for (int i = 0; i < _level; i++) { var p = Flake(3, w, -10); p.Vy += 1; p.W = R(10); p.H = R(10); p.Color = Confetti[R(10)]; _ps.Add(p); }
        StepClouds(w, h);
        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            var p = _ps[i]; p.X += p.Vx; p.Y += p.Vy;
            if (p.Y >= h) _ps.RemoveAt(i); else _ps[i] = p;
        }
    }

    private void StepClouds(int w, int h)
    {
        var cam = Camera();
        if ((_flags & 0x38) != 0 && CloudTex != null)
        {
            _cloudFrame++;
            bool ship = MapId() is 59529 or 38251;
            if (ship ? _cloudFrame % 10 == 0 : _cloudFrame % 500 == 0)
            {
                int piece = ship ? R(6) : 3 + R(3), speed = ship ? 63 : 4 + R(5), dir = ship ? 14 : 12;
                Vector2 at = R(2) == 0 ? new(-R(w / 2 + 1) - w / 2, R(2 * h + 1) - h) : new(R(2 * w + 1) - w, -R(h / 2 + 1) - h / 2);
                _clouds.Add(new Cloud { World = cam + at, Piece = piece, Speed = speed, Dir = dir });
            }
        }
        for (int i = _clouds.Count - 1; i >= 0; i--)
        {
            var c = _clouds[i];
            c.World += Dirs[c.Dir - 12] * c.Speed / 256f;
            var scr = c.World - cam;
            if (scr.X > w + 320 || scr.Y > h + 240) _clouds.RemoveAt(i); else _clouds[i] = c;
        }
    }

    private P Flake(int type, int w, float y)
    {
        var p = new P { Type = type, X = R(Math.Max(1, 2 * w)) - w / 2, Y = y, Size = R(2) };
        if (R(2) == 0) { p.Vx = R(2); p.Vy = 3 + R(2); } else { p.Vx = 1; p.Vy = 4 + R(2); }
        return p;
    }

    public override void _Draw()
    {
        if (CloudTex != null)
        {
            var cam = Camera();
            foreach (var c in _clouds)
            {
                var src = Pieces[c.Piece];
                // 官方画法参数：白块 0x32、飞船上的蓝块 0x01（更淡）
                DrawTextureRectRegion(CloudTex, new Rect2(c.World - cam, src.Size), src, new Color(1, 1, 1, c.Piece >= 3 ? 0.85f : 0.55f));
            }
        }
        foreach (var p in _ps)
        {
            switch (p.Type)
            {
                case 1: DrawRect(new Rect2(p.X, p.Y - 10, 1, 20), Rain); break;
                case 2:
                    var pat = p.Size == 0 ? Snow1 : Snow2;
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                            if (pat[y, x] > 0) DrawRect(new Rect2(p.X + x - 2, p.Y + y - 2, 1, 1), Palette[pat[y, x] == 1 ? 159 : 160]);
                    break;
                case 3: if (p.W > 0 && p.H > 0) DrawRect(new Rect2(p.X - p.W / 2, p.Y - 10 - p.H / 2, p.W, p.H), Palette[p.Color]); break;
            }
        }
    }
}
