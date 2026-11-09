using Godot;

namespace CgPort.Godot;

/// <summary>
/// 官方的提示栏：鼠标停在按钮 / 道具上时，说明文字（"切换页数。""购买指定物品。"…）画在画面左下 (8, H−20)，
/// 最上层、白字，每帧重画，没有悬停就空（exe 0x40ef83 写进 0x12d06a8、0x44e582 画、画完清空）。
/// 移植这边各窗口照旧把说明放在 TooltipText 里，这里每帧取鼠标下面的控件（及其上层）的 TooltipText 画出来；
/// Godot 自带的浮动提示框关掉（project.godot gui/timers/tooltip_delay_sec 设得很大）。
/// </summary>
public partial class HintBar : CanvasLayer
{
    private CgText _text = null!;
    public string Current { get; private set; } = "";

    public override void _Ready()
    {
        Layer = 100;
        _text = new CgText { Wrap = 999, Position = new Vector2(8, 701) };
        AddChild(_text);
    }

    public override void _Process(double delta)
    {
        string tip = "";
        for (Node? n = GetViewport().GuiGetHoveredControl(); n != null; n = n.GetParent())
            if (n is Control c && !string.IsNullOrEmpty(c.TooltipText)) { tip = c.TooltipText; break; }
        if (tip != Current) { Current = tip; _text.Text = tip; }
    }
}
