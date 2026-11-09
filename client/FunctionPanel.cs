using Godot;

namespace CgPort.Godot;

/// <summary>
/// 右上「功能」（这个服务端 致敬经典 改过的客户端才有，官方 bob 实测）：按钮在 (892,75)，点开下面一列 8 个按钮（(895,107+25i)），
/// 3 页（底下 "0N区" + ◀ ▶ 翻页，循环）。点第 p 页第 i 个 = CUSTOMXB p×8+i+1（实测 佣兵 1、高速 8、第 2 页第 5 个 13）。
/// 服务端回对话框（BZ）或提示。按钮图在官方图档里没有（改版客户端自己画的），这里照截图画：棕底绿字。
/// </summary>
public partial class FunctionPanel : CanvasLayer
{
    public static readonly string[][] Pages =
    {
        new[] { "佣兵", "银行", "传送", "打卡", "称号", "战后", "商店", "高速" },
        new[] { "回复", "鉴定", "维修", "招魂", "血魔", "集合", "归队", "存队" },
        new[] { "声望", "回城", "加点", "转播", "观战", "改名", "佣兵血魔" },
    };
    public Action<int>? Chosen;
    public int Page { get; private set; }
    public bool Open { get; private set; }
    private Control _root = null!;
    private static readonly Color Brown = Color.Color8(170, 130, 71), Cream = Color.Color8(194, 181, 147), Green = new(0.1f, 1f, 0.1f);

    public override void _Ready()
    {
        Layer = 6;
        _root = new Control { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -68, OffsetTop = 75, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        Refresh();
    }

    public void Toggle() { Open = !Open; Refresh(); }
    public void Close() { if (!Open) return; Open = false; Refresh(); }

    private void Refresh()
    {
        foreach (var c in _root.GetChildren()) c.QueueFree();
        Button("功  能", 0, 0, 64, () => Toggle());
        if (!Open) return;
        _root.AddChild(new ColorRect { Color = Cream, Position = new Vector2(1, 24), Size = new Vector2(66, 270), MouseFilter = Control.MouseFilterEnum.Stop });
        var items = Pages[Page];
        for (int i = 0; i < items.Length; i++)
        {
            int code = Page * 8 + i + 1;
            Button(items[i], 5, 30 + 25 * i, 58, () => { Open = false; Refresh(); Chosen?.Invoke(code); });
        }
        Label($"0{Page + 1}区", 34, 234, new Color(0.9f, 0.1f, 0.1f), center: true);
        Arrow("◀", 6, 252, () => { Page = (Page + Pages.Length - 1) % Pages.Length; Refresh(); });
        Arrow("▶", 36, 252, () => { Page = (Page + 1) % Pages.Length; Refresh(); });
    }

    private void Button(string text, float x, float y, float w, Action a)
    {
        var b = new Control { Position = new Vector2(x, y), Size = new Vector2(w, 20), MouseFilter = Control.MouseFilterEnum.Stop };
        b.AddChild(new ColorRect { Color = Color.Color8(92, 93, 91), Size = b.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
        b.AddChild(new ColorRect { Color = Brown, Position = new Vector2(1, 1), Size = b.Size - new Vector2(2, 2), MouseFilter = Control.MouseFilterEnum.Ignore });
        var t = Label(text, w / 2, 4, Green, center: true); b.AddChild(t); t.Position -= new Vector2(0, 0);
        _root.AddChild(b);
        b.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || e is InputEventScreenTouch { Pressed: true }) { a(); b.AcceptEvent(); } };
        t.Reparent(b, false); t.Position = new Vector2(w / 2 - text.Sum(CgText.Cells) * CgText.CellW / 2, 3);
    }

    private void Arrow(string s, float x, float y, Action a)
    {
        var b = new Control { Position = new Vector2(x, y), Size = new Vector2(26, 18), MouseFilter = Control.MouseFilterEnum.Stop };
        b.AddChild(new ColorRect { Color = Color.Color8(150, 150, 150), Size = b.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
        b.AddChild(new CgText { Text = s, FontColor = Color.Color8(80, 210, 255), Position = new Vector2(7, 2) });
        b.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || e is InputEventScreenTouch { Pressed: true }) { a(); b.AcceptEvent(); } };
        _root.AddChild(b);
    }

    private CgText Label(string s, float x, float y, Color c, bool center = false)
    {
        var t = new CgText { Text = s, FontColor = c, Wrap = 999 };
        float w = s.Sum(CgText.Cells) * CgText.CellW;
        t.Position = new Vector2(center ? x - w / 2 : x, y);
        _root.AddChild(t); return t;
    }

    public void TestPage(int p) { Page = p; Open = true; Refresh(); }
}
