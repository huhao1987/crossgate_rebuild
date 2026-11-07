using CgPort.Client;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 用道具（药等）时的"请选择使用对象"窗口（官方 4.06 实测）：棕框 510×235 在 (225,229)。
/// 表头 健康/等级/姓名/生命力/魔力，每行一个对象（自己、自己的宠物；行距 22），自己的名字绿色。
/// 底下「取消」「上一页」（回到"请选择对象"选队伍里的人）。点一行 = 对它用。
/// 前一步"请选择对象"跟交易的选人窗口是同一个样子，直接用 <see cref="TradePicker"/>。
/// </summary>
public partial class ItemTargetWindow : CanvasLayer
{
    public TextureCache Cache = null!;
    public Action<int>? Picked;   // 对象编号（0 人，宠物 = 栏位 + 1）
    public Action? Back;
    public IReadOnlyList<NetworkSession.ItemTarget> Shown { get; private set; } = Array.Empty<NetworkSession.ItemTarget>();

    public override void _Ready() { Layer = 21; Visible = false; }

    public void Show(IReadOnlyList<NetworkSession.ItemTarget> list)
    {
        Shown = list;
        foreach (var c in GetChildren()) c.QueueFree();
        var win = new Control { Position = new Vector2(225, 229), Size = new Vector2(510, 235) };
        AddChild(win);
        win.AddChild(new Control { Size = win.Size, MouseFilter = Control.MouseFilterEnum.Stop });
        win.AddChild(CgFrame.Tint(win.Size));
        win.AddChild(new CgFrame { Cache = Cache, Size = win.Size, MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        var yellow = new Color(1, 1, 0);
        Label(win, "请选择使用对象", 255, 22, yellow, center: true);
        Label(win, "健康", 37, 59, Colors.White); Label(win, "等级", 85, 59, Colors.White); Label(win, "姓名", 130, 59, Colors.White);
        Label(win, "生命力", 292, 59, Colors.White); Label(win, "魔力", 406, 59, Colors.White);
        for (int i = 0; i < list.Count && i < 6; i++)
        {
            var t = list[i]; float y = 79 + 22 * i;
            int code = t.Code;
            var hit = new Control { Position = new Vector2(38, y - 4), Size = new Vector2(452, 20), MouseFilter = Control.MouseFilterEnum.Stop };
            var box = new GreenBox { Size = hit.Size, Visible = false }; hit.AddChild(box);
            hit.MouseEntered += () => box.Visible = true; hit.MouseExited += () => box.Visible = false;
            hit.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Visible = false; Picked?.Invoke(code); } };
            win.AddChild(hit);
            win.AddChild(new ColorRect { Color = t.Hp > 0 ? new Color(0, 0.9f, 0) : new Color(0.9f, 0, 0), Position = new Vector2(49, y + 1), Size = new Vector2(10, 10), MouseFilter = Control.MouseFilterEnum.Ignore });
            Label(win, t.Level.ToString(), 109, y, Colors.White, right: true);
            Label(win, t.Name, 129, y, t.Self ? new Color(0, 1, 0) : Colors.White);
            Label(win, $"{t.Hp}/{t.MaxHp,4}", 348, y, Colors.White, right: true);
            Label(win, $"{t.Mp}/{t.MaxMp,4}", 460, y, Colors.White, right: true);
        }
        Button(win, "取  消", 172, () => Visible = false);
        Button(win, "上一页", 335, () => { Visible = false; Back?.Invoke(); });
        Visible = true;
    }

    private void Button(Control win, string s, float cx, Action a)
    {
        var hit = new Control { Position = new Vector2(cx - 34, 202), Size = new Vector2(68, 20), MouseFilter = Control.MouseFilterEnum.Stop };
        hit.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) a(); };
        win.AddChild(hit);
        Label(win, s, cx, 206, new Color(1, 1, 0), center: true);
    }

    private static void Label(Control parent, string s, float x, float y, Color c, bool center = false, bool right = false)
    {
        var t = new CgText { Wrap = 999, FontColor = c, Text = s };
        float w = s.Sum(CgText.Cells) * CgText.CellW;
        t.Position = new Vector2(center ? x - w / 2 : right ? x - w : x, y - 2);
        parent.AddChild(t);
    }

    public void TestPick(int code) { Visible = false; Picked?.Invoke(code); }
}
