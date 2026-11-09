using Godot;
namespace CgPort.Godot;

/// <summary>
/// 天气（EF = zzd 标志 强度，官方 exe 0x453cc9 / 0x436cd9 / 0x436d9b / 0x436fa3 / 0x4125d1 反汇编）：
///   标志 1 = 雨、2 = 雪、4 = 彩纸（三种可叠加），强度 0–7；标志 0 全部停。0x38 位只设一个变量，没找到画的地方，先不做。
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
        if (flags == 0) _ps.Clear();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_flags == 0 && _ps.Count == 0) return;
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
        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            var p = _ps[i]; p.X += p.Vx; p.Y += p.Vy;
            if (p.Y >= h) _ps.RemoveAt(i); else _ps[i] = p;
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
