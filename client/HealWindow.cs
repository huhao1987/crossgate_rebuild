using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 治疗（服务端 BZ 种类 19，Injurydoctor，官方 exe 0x426bb7）。九宫格窗口 7×6 格（448×312）画在 ((W−448)/2,(H−312)/2)，坐标相对窗口左上角：
///   "请选择治疗对象" 黄字 (162,20)；表头 "健康 等级       姓   名        所需花费" (46,40)；
///   每人一行（最多 6 行，行距 21）："%3d   %-16s %7d 金币"（等级、名字、费用）(90, 62+21k)，健康图 22721 + (受伤+24)/25 在 (64, 70+21k)；
///   "全部一起治疗 %7d 金币" (113,202)、"持有金币 %7d 金币" (133,222)；「取  消」(192,255)，提示 "取消治疗。"。
///   滑过一行：有费用 "治疗这个角色的伤。" / 没有 "这个角色没有受伤。"；滑过"全部一起治疗"："治疗所有受伤的角色。" / "没有受伤的角色。"。
///   点一行 → xD … 0 "k"（从 1 数），点全部 → "7"，窗口关掉（音效 79）；取消不发包（音效 57）。
/// </summary>
public partial class HealWindow : CanvasLayer
{
    public TextureCache Cache = null!;
    public Func<long> Gold = () => 0;
    public Action<string>? Submit;   // "1".."7"；"cancel" = 取消
    public DialogPage? Page { get; private set; }
    public string Tip { get; private set; } = "";
    private Control? _win;

    public override void _Ready() { Layer = 20; Visible = false; }

    public void ShowPage(DialogPage? p)
    {
        Page = p?.Kind == "heal" ? p : null;
        foreach (var c in GetChildren()) c.QueueFree();
        _win = null;
        if (Page?.Shop is not { } list) { Visible = false; return; }
        var win = new Control { Position = new Vector2((960 - 448) / 2, (720 - 312) / 2), Size = new Vector2(448, 288) };   // 7×6 格：位置按 448×312 居中，九宫格一格 64×48 → 画出来 448×288
        AddChild(win); _win = win;
        win.AddChild(new Control { Size = win.Size, MouseFilter = Control.MouseFilterEnum.Stop });
        win.AddChild(CgFrame.Tint(win.Size));
        win.AddChild(new CgFrame { Cache = Cache, Size = win.Size, MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        var yellow = ChatLog.Palette[4];
        Label(win, "请选择治疗对象", 162, 20, yellow);
        Label(win, "健康 等级       姓   名        所需花费", 46, 40, Colors.White);
        long all = 0;
        for (int k = 0; k < list.Count && k < 6; k++)
        {
            var e = list[k]; all += e.Price;
            Row(win, 20, 60 + 20 * k, 408, e.Price != 0 ? "治疗这个角色的伤。" : "这个角色没有受伤。", (k + 1).ToString());
            if (Cache.ImageNo(22721 + Math.Clamp((e.Image + 24) / 25, 0, 4)).Tex is { } t)
                win.AddChild(new TextureRect { Texture = t, Position = new Vector2(64, 70 + 21 * k) - t.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            Label(win, $"{e.Count,3}   {Pad(e.Name, 16)} {e.Price,7} 金币", 90, 62 + 21 * k, Colors.White);
        }
        Row(win, 20, 200, 408, all != 0 ? "治疗所有受伤的角色。" : "没有受伤的角色。", "7");
        Label(win, $"全部一起治疗 {all,7} 金币", 113, 202, Colors.White);
        Label(win, $"持有金币 {Gold(),7} 金币", 133, 222, Colors.White);
        var cancel = new Control { Position = new Vector2(186, 251), Size = new Vector2(64, 20), MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = "取消治疗。" };
        cancel.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Cancel(); };
        win.AddChild(cancel);
        Label(win, "取  消", 192, 255, yellow);
        Visible = true;
    }

    private void Row(Control win, float x, float y, float w, string tip, string ans)
    {
        var hit = new Control { Position = new Vector2(x, y), Size = new Vector2(w, 20), MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = tip };
        var box = new GreenBox { Size = hit.Size, Visible = false }; hit.AddChild(box);
        hit.MouseEntered += () => { box.Visible = true; Tip = tip; };
        hit.MouseExited += () => box.Visible = false;
        hit.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Pick(ans); };
        win.AddChild(hit);
    }

    public void Pick(string ans) { GameAudio.Se(79); Visible = false; Page = null; Submit?.Invoke(ans); }
    public void Cancel() { GameAudio.Se(57); Visible = false; Page = null; Submit?.Invoke("cancel"); }

    private static string Pad(string s, int cells) => s + new string(' ', Math.Max(0, cells - s.Sum(CgText.Cells)));

    private static void Label(Control parent, string s, float x, float y, Color c)
    {
        // 官方坐标是字的基准；我们的字画出来比官方同 y 高 6 像素
        parent.AddChild(new CgText { Wrap = 999, FontColor = c, Text = s, Position = new Vector2(x, y + 6) });
    }
}
