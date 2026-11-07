using Godot;

namespace CgPort.Godot;

/// <summary>
/// 触屏点按辅助：官方界面的按钮是给鼠标做的，在手机上只有两三毫米高，手指常常点在旁边。
/// 手指点下去时（引擎把触摸转成的鼠标事件，设备号 = 模拟），如果正好点中一个可点的控件就不管；
/// 没点中、但离某个小按钮不到 Slop 像素（画布像素，2 倍界面时约 3 毫米），就把这次点按挪到那个按钮中间。
/// 鼠标点击不受影响。
/// </summary>
public partial class TouchAssist : Node
{
    public float Slop = 22;
    private Vector2? _redirect;
    private bool _injecting;

    public override void _Input(InputEvent e)
    {
        if (_injecting || e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb || mb.Device != InputEvent.DeviceIdEmulation) return;
        if (!mb.Pressed)
        {
            if (_redirect is Vector2 r) { Reinject(mb, r); _redirect = null; }
            return;
        }
        _redirect = null;
        var candidates = new List<Control>();
        foreach (var n in GetTree().Root.GetChildren()) Collect(n, candidates);
        var p = mb.Position;
        if (candidates.Any(c => c.GetGlobalRect().HasPoint(p))) return;   // 点中了
        Control? best = null; float bd = Slop;
        foreach (var c in candidates)
        {
            var rect = c.GetGlobalRect();
            float dx = Math.Max(0, Math.Max(rect.Position.X - p.X, p.X - rect.End.X));
            float dy = Math.Max(0, Math.Max(rect.Position.Y - p.Y, p.Y - rect.End.Y));
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < bd) { bd = d; best = c; }
        }
        if (best == null) return;
        var target = best.GetGlobalRect().GetCenter();
        GameLog.Write("触屏辅助", $"{p} 没点中，挪到 {Describe(best)} {target}（差 {bd:0} 像素）");
        _redirect = target;
        Reinject(mb, target);
    }

    private void Reinject(InputEventMouseButton mb, Vector2 pos)
    {
        GetViewport().SetInputAsHandled();
        var e2 = (InputEventMouseButton)mb.Duplicate();
        e2.Position = pos; e2.GlobalPosition = pos;
        var move = new InputEventMouseMotion { Position = pos, GlobalPosition = pos, Device = mb.Device };
        _injecting = true;
        GetViewport().PushInput(move, true);
        GetViewport().PushInput(e2, true);
        _injecting = false;
    }

    /// <summary>看得见、会接收点击、比较小（不超过 200×120）的控件：按钮和我们用 Hit() 做的点击区。</summary>
    private static void Collect(Node n, List<Control> list)
    {
        if (n is CanvasItem ci && !ci.IsVisibleInTree()) return;
        if (n is CanvasLayer cl && !cl.Visible) return;
        if (n is Control c && c.MouseFilter == Control.MouseFilterEnum.Stop)
        {
            var s = c.Size * c.GetGlobalTransform().Scale;
            bool small = s.X <= 200 && s.Y <= 120 && s.X > 2 && s.Y > 2;
            bool clickable = c is BaseButton || c.GetSignalConnectionList(Control.SignalName.GuiInput).Count > 0;
            if (small && clickable && !(c is BaseButton { Disabled: true })) list.Add(c);
        }
        foreach (var ch in n.GetChildren()) Collect(ch, list);
    }

    private static string Describe(Node n) { var p = new List<string>(); for (var x = n; x != null && p.Count < 3; x = x.GetParent()) p.Add(x.Name); return string.Join("<", p); }
}
