using Godot;

namespace CgPort.Godot;

/// <summary>
/// 进出战斗的过场（对照官方客户端截图仿的，时长凭目测）：
///  - 进战斗：先抓一张当前地图画面盖在最上面，中间开一个椭圆洞往外扩，洞里是战斗画面；洞外的旧画面每行往两边拉伸，越靠中线拉得越厉害，边缘按 8 像素一格取整（官方是一格一格的马赛克边）。
///  - 出战斗：先黑屏 0.3 秒，再用同样的椭圆把地图"开"出来。
/// </summary>
public partial class Transition : CanvasLayer
{
    private TextureRect _cover = null!;
    private ShaderMaterial _mat = null!;
    private double _t = -1, _hold;
    public float Duration = 0.8f;
    public bool Running => _t >= 0;

    private const string Code = @"
shader_type canvas_item;
uniform float progress;      // 0 → 1：洞从无到盖满全屏
uniform vec2 size;           // 画面像素大小
void fragment() {
    vec2 p = UV * size;
    vec2 c = size * 0.5;
    // 8 像素一格的马赛克取整
    vec2 q = floor(p / 8.0) * 8.0 + 4.0;
    vec2 d = (q - c) / c;                 // -1..1
    float r = progress * 1.5;             // 1.5 → 椭圆刚好盖住四角
    float e = length(vec2(d.x, d.y * 1.6)); // 横向扁的椭圆（官方的洞是横着长的）
    if (e < r) discard;
    // 洞外：每行往中线方向拉伸（越靠近洞，横向拉得越多）
    float pull = clamp(1.0 - (e - r) * 2.0, 0.0, 1.0) * progress;
    float sx = c.x + (UV.x * size.x - c.x) * (1.0 - 0.6 * pull);
    COLOR = texture(TEXTURE, vec2(sx / size.x, UV.y));
}";

    public override void _Ready()
    {
        Layer = 45;
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _cover = new TextureRect { AnchorRight = 1, AnchorBottom = 1, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        AddChild(_cover);
    }

    /// <summary>进战斗：把这一刻的画面当成"旧画面"，然后开洞。</summary>
    public void Enter()
    {
        var img = GetViewport().GetTexture().GetImage();
        _cover.Texture = ImageTexture.CreateFromImage(img);
        Start(0);
    }

    /// <summary>出战斗：黑屏一会儿再开洞。</summary>
    public void Exit()
    {
        var sz = GetViewport().GetVisibleRect().Size;
        var img = Image.CreateEmpty(Mathf.Max(1, (int)sz.X), Mathf.Max(1, (int)sz.Y), false, Image.Format.Rgb8);
        img.Fill(Colors.Black);
        _cover.Texture = ImageTexture.CreateFromImage(img);
        Start(0.3);
    }

    /// <summary>测试用：停在某个进度（不再自动走）。</summary>
    public void Freeze(float p) { _t = -1; _cover.Visible = true; Apply(p); }

    private void Start(double hold)
    {
        _t = 0; _hold = hold;
        _cover.Visible = true;
        Apply(0);
    }

    private void Apply(float p)
    {
        _mat.SetShaderParameter("progress", p);
        _mat.SetShaderParameter("size", GetViewport().GetVisibleRect().Size);
    }

    public override void _Process(double delta)
    {
        if (_t < 0) return;
        if (_hold > 0) { _hold -= delta; return; }
        _t += delta;
        float p = (float)Mathf.Clamp(_t / Duration, 0, 1);
        Apply(p * p);   // 先慢后快
        if (p >= 1) { _t = -1; _cover.Visible = false; }
    }
}
