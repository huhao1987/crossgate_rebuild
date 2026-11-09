using Godot;

namespace CgPort.Godot;

/// <summary>
/// 进出战斗的过场（官方 exe 0x473e67 按编号分派；系统 → 绘图设定 →「过场动画样式」0x1870748 选哪一套）。
/// 两段：先把旧画面"拿走"（变黑），再把新画面"放进来"。官方每帧走一步，这里按 60 帧/秒步进。
///   默认（编号 13 拿走 / 14 放进来，0x4734c5）：每一横行单独左右移动，偶数行往右、奇数行往左。
///     拿走：第 f 帧时离中线 d 行的行满足 H/16 − d/8 ≤ f 就每帧加速 2（上下边缘先动、中线最后），中线那行移出屏幕结束。
///     放进来：偶数行从左边 −W、奇数行从右边 +W 进来，d/8 ≤ f 的行开始动（中线先动），初速 60、每帧减 2（减到 0 又回 60），过了 0 就停在 0；第 0 行到位结束。
///   仿石器（编号 8 拿走 / 9 放进来，0x472dcc）：画面切成 8×8 块，每帧随机挑一块开始掉（速度每帧 +1）。
///     拿走：块从原位掉到 H+20 以下消失；放进来：块从 y = −60 掉到原位停住。64 块都结束为止。
/// 每帧先清成黑色，再画各行 / 各块（静止的块先画，正在掉的盖在上面）。旧画面是开始时抓的图，新画面直接用当前屏幕。
/// </summary>
public partial class Transition : CanvasLayer
{
    /// <summary>0 默认，1 仿石器。</summary>
    public int Style;
    /// <summary>开始时从设定里读样式。</summary>
    public Func<int>? StyleOf;
    private TextureRect _cover = null!;
    private ShaderMaterial _mat = null!;
    private enum Phase { None, Out, In }
    private Phase _phase = Phase.None;
    private double _acc;
    private int _w, _h, _frame;
    public bool Paused;
    public bool Running => _phase != Phase.None;
    public string PhaseName => _phase.ToString();
    public int Frame => _frame;

    // 默认：每行
    private float[] _x = Array.Empty<float>(), _spd = Array.Empty<float>();
    // 仿石器：64 块
    private readonly float[] _by = new float[64], _bv = new float[64], _bt = new float[64];
    private readonly int[] _bs = new int[64];
    private int _started, _done;
    private readonly Random _rng = new();
    private Image _data = null!;
    private ImageTexture? _dataTex;

    private const string Code = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform sampler2D data : filter_nearest;
uniform vec2 size;
uniform int style;        // 0 每行左右移动，1 8×8 块
uniform bool live;        // true = 新画面（取屏幕），false = 旧画面（取 TEXTURE）
void fragment() {
    vec2 p = floor(UV * size);
    vec4 c = vec4(0.0, 0.0, 0.0, 1.0);
    vec2 q = vec2(-1.0);
    if (style == 0) {
        float off = texelFetch(data, ivec2(int(p.y), 0), 0).r;
        float sx = p.x - off;
        if (sx >= 0.0 && sx < size.x) q = vec2(sx, p.y);
    } else {
        vec2 b = floor(size / 8.0);
        int bx = int(p.x / b.x);
        if (bx < 8) {
            float best = 0.0;
            for (int by = 0; by < 8; by++) {
                vec2 d = texelFetch(data, ivec2(bx, by), 0).rg;   // r = 现在的 y，g = 状态（0 不画、1 在掉、2 停着）
                if (d.g < 0.5 || p.y < d.r || p.y >= d.r + b.y) continue;
                bool falling = d.g < 1.5;
                if (falling || best < 0.5) { q = vec2(p.x, float(by) * b.y + (p.y - d.r)); best = falling ? 1.0 : 2.0; }
            }
        }
    }
    if (q.x >= 0.0) {
        vec2 uv = (q + 0.5) / size;
        c = live ? textureLod(screen_tex, uv, 0.0) : textureLod(TEXTURE, uv, 0.0);
    }
    COLOR = c;
}";

    public override void _Ready()
    {
        Layer = 45;
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _cover = new TextureRect { AnchorRight = 1, AnchorBottom = 1, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        AddChild(_cover);
    }

    /// <summary>进战斗：抓这一刻的画面（地图）当旧画面。</summary>
    public void Enter() => Begin();
    /// <summary>出战斗：抓这一刻的画面（战斗）当旧画面。</summary>
    public void Exit() => Begin();

    private void Begin()
    {
        if (StyleOf != null) Style = StyleOf();
        var img = GetViewport().GetTexture().GetImage();
        _cover.Texture = ImageTexture.CreateFromImage(img);
        var sz = GetViewport().GetVisibleRect().Size;
        _w = Mathf.Max(8, (int)sz.X); _h = Mathf.Max(8, (int)sz.Y);
        _mat.SetShaderParameter("size", new Vector2(_w, _h));
        _mat.SetShaderParameter("style", Style);
        StartPhase(Phase.Out);
        _cover.Visible = true;
    }

    private void StartPhase(Phase ph)
    {
        _phase = ph; _frame = 0; _acc = 0;
        _mat.SetShaderParameter("live", ph == Phase.In);
        if (Style == 0)
        {
            _x = new float[_h]; _spd = new float[_h];
            for (int i = 0; i < _h; i++) _x[i] = ph == Phase.In ? (i % 2 == 0 ? -_w : _w) : 0;
            _data = Image.CreateEmpty(_h, 1, false, Image.Format.Rf);
        }
        else
        {
            int bh = _h / 8;
            for (int k = 0; k < 64; k++)
            {
                int row = k / 8;
                _bt[k] = row * bh; _bv[k] = 0;
                _by[k] = ph == Phase.In ? -60 : row * bh;
                _bs[k] = ph == Phase.In ? 0 : 2;
            }
            _started = _done = 0;
            _data = Image.CreateEmpty(8, 8, false, Image.Format.Rgf);
        }
        Upload();
    }

    /// <summary>走一帧；返回 true = 这一段结束了。</summary>
    private bool Step()
    {
        _frame++;
        return Style == 0 ? StepRows() : StepBlocks();
    }

    private bool StepRows()
    {
        int mid = _h / 2;
        if (_phase == Phase.Out)
        {
            for (int i = 0; i < _h; i++)
            {
                int dir = i % 2 == 0 ? 1 : -1;
                _x[i] += dir * _spd[i];
                int d = Math.Abs(i - mid);
                if (_h / 16 - d / 8 <= _frame) _spd[i] += 2;
            }
            return Math.Abs(_x[mid]) > _w;
        }
        for (int i = 0; i < _h; i++)
        {
            int dir = i % 2 == 0 ? 1 : -1;
            if ((dir == 1 && _x[i] < 0) || (dir == -1 && _x[i] > 0))
            {
                _x[i] += dir * _spd[i];
                if (Math.Abs(i - mid) / 8 <= _frame) _spd[i] = _spd[i] == 0 ? 60 : _spd[i] - 2;
            }
            else _x[i] = 0;
        }
        return _x[0] >= 0;
    }

    private bool StepBlocks()
    {
        bool outPh = _phase == Phase.Out;
        if (_started < 64)
        {
            int k;
            do k = _rng.Next(64); while (outPh ? _bs[k] != 2 : _bs[k] != 0);
            _bs[k] = 1; _started++;
        }
        for (int k = 0; k < 64; k++)
        {
            if (_bs[k] != 1) continue;
            _bv[k]++; _by[k] += _bv[k];
            if (outPh && _by[k] >= _h + 20) { _bs[k] = 0; _done++; }
            else if (!outPh && _by[k] >= _bt[k]) { _by[k] = _bt[k]; _bs[k] = 2; _done++; }
        }
        return _done >= 64;
    }

    private void Upload()
    {
        if (Style == 0) for (int i = 0; i < _h; i++) _data.SetPixel(i, 0, new Color(_x[i], 0, 0));
        else for (int k = 0; k < 64; k++) _data.SetPixel(k % 8, k / 8, new Color(_by[k], _bs[k], 0));
        if (_dataTex == null || _dataTex.GetSize() != new Vector2(_data.GetWidth(), _data.GetHeight())) _dataTex = ImageTexture.CreateFromImage(_data);
        else _dataTex.Update(_data);
        _mat.SetShaderParameter("data", _dataTex);
    }

    /// <summary>测试用：直接走 n 帧（不管 Paused）。</summary>
    public void Advance(int n) { for (int i = 0; i < n && Running; i++) Tick(); }

    private void Tick()
    {
        if (!Step()) { Upload(); return; }
        if (_phase == Phase.Out) StartPhase(Phase.In);
        else { _phase = Phase.None; _cover.Visible = false; }
    }

    public override void _Process(double delta)
    {
        if (_phase == Phase.None || Paused) return;
        _acc += delta;
        int n = 0;
        while (_acc >= 1 / 60.0 && n < 4) { _acc -= 1 / 60.0; n++; Tick(); if (!Running) break; }
    }
}
