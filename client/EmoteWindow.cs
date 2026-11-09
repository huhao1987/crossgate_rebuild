using Godot;

namespace CgPort.Godot;

/// <summary>
/// 「動作」窗口（右上"動作"按钮 22591，官方 4.06 实测）：背景 22640（192×360）贴右在 (768,0)，中间是半透明压暗（图里的蓝底和水印不画）。
/// 两列 × 10 行，行距 30；点一个 = MIS x y 动作号（62 进制），旁人收到 NI id|x|y|动作号|朝向，身上播那个动作。
/// 动作号就是动画的动作编号（3 跑 5 攻击 6 魔法 7 投掷 8 受伤 9 防御 10 倒下 11 坐下 12 挥手 13 高兴 14 愤怒 15 悲伤 16 点头 17 石头 18 剪刀 19 布 20 垂钓），
/// 另外 30 = 走路、31 = 站立。当前的动作用棕色框框着（默认站立）。
/// </summary>
public partial class EmoteWindow : CgWindow
{
    public static readonly (string Name, int Code)[] Emotes =
    {
        ("坐下", 11), ("站立", 31), ("挥手", 12), ("走路", 30), ("点头", 16), ("倒下", 10), ("高兴", 13), ("攻击", 5), ("愤怒", 14), ("防御", 9),
        ("悲伤", 15), ("受伤", 8), ("投掷", 7), ("魔法", 6), ("移动中", 3), ("垂钓", 20), ("剪刀", 18), ("石头", 17), ("布", 19),
    };
    public Action<int>? Chosen;
    public int Current = 31;
    private static ImageTexture? _bg;

    public override void _Ready() { Layer = 16; Visible = false; Build(new Vector2(768, 0), true); }

    /// <summary>22640 去掉中间（官方中间是透明压暗，图里画的是蓝底 + 水印）。</summary>
    private ImageTexture? Background()
    {
        if (_bg != null) return _bg;
        if (Cache.ImageNo(22640).Tex is not { } t) return null;
        var img = t.GetImage(); img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = img.GetPixel(x, y);
                bool inside = x >= 12 && x < w - 12 && y >= 34 && y < h - 12;
                bool blue = c.B > c.R && c.B > 0.3f && c.R < 0.45f;
                if (inside || blue) img.SetPixel(x, y, new Color(0, 0, 0, 0));
            }
        return _bg = ImageTexture.CreateFromImage(img);
    }

    public override void Refresh()
    {
        Clear();
        Body.AddChild(new ColorRect { Color = new Color(0, 0, 58 / 255f, 0.55f), Position = new Vector2(6, 30), Size = new Vector2(180, 324), MouseFilter = Control.MouseFilterEnum.Stop });
        if (Background() is { } bg) Body.AddChild(new TextureRect { Texture = bg, MouseFilter = Control.MouseFilterEnum.Ignore });
        Btn(22509, 22510, 167, 7, () => Visible = false);
        for (int i = 0; i < Emotes.Length; i++)
        {
            var (name, code) = Emotes[i];
            bool last = i == Emotes.Length - 1;
            float x = last ? 58 : i % 2 == 0 ? 35 : 116, y = 46 + 30 * (i / 2);
            if (code == Current) Frame(x - 3, y - 3);
            Spread(name, x, y);
            int c = code;
            Hit(x - 3, y - 3, 70, 19, _ => { Current = c; Refresh(); Chosen?.Invoke(c); });
        }
    }

    /// <summary>官方把两个字拉开排（"坐      下"）：2 字间距 40，3 字间距 20。</summary>
    private void Spread(string s, float x, float y)
    {
        float step = s.Length == 2 ? 40 : 20;
        for (int k = 0; k < s.Length; k++) Txt(s[k].ToString(), x + step * k, y);
    }

    private void Frame(float x, float y)
    {
        var c = new Color(0.55f, 0.33f, 0.15f);
        Body.AddChild(new ColorRect { Color = new Color(0.45f, 0.25f, 0.1f, 0.6f), Position = new Vector2(x, y), Size = new Vector2(70, 19), MouseFilter = Control.MouseFilterEnum.Ignore });
        Body.AddChild(new ReferenceRect { BorderColor = c, BorderWidth = 1, EditorOnly = false, Position = new Vector2(x, y), Size = new Vector2(70, 19), MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    public void TestChoose(int code) { Current = code; Refresh(); Chosen?.Invoke(code); }
}
