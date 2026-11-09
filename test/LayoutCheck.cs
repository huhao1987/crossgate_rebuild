using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 手机模式界面检查（跑的时候 --resolution 2400x1080 等手机尺寸，UI_MODE=mobile）：依次打开各个窗口，
/// 把露在画面外面的控件列出来（不看截图，按位置判断）。最后打一场仗检查战斗界面。
/// </summary>
public partial class LayoutCheck : SceneTree
{
    int _f, _st, _win; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    static readonly (string Name, int Menu)[] Windows = { ("地图", 0), ("状态", 1), ("技能", 2), ("物品", 3), ("宠物", 4), ("名片", 5), ("系统", 7) };

    /// <summary>所有看得见、但有一部分在画面外面的界面控件（只看 CanvasLayer 里的 Control）。</summary>
    List<string> OutOfBounds()
    {
        var view = Root.GetVisibleRect();
        var bad = new List<string>();
        void Walk(Node n)
        {
            if (n is CanvasItem ci && !ci.IsVisibleInTree()) return;
            if (n.Name.ToString().StartsWith("BarTile")) return;
            if (n is Control c && c.Size.X > 1 && c.Size.Y > 1 && c.MouseFilter != Control.MouseFilterEnum.Ignore || n is TextureRect)
            {
                var r = ((Control)n).GetGlobalRect();
                if (r.Size.X > 1 && r.Size.Y > 1 && (r.Position.X < -1 || r.Position.Y < -1 || r.End.X > view.End.X + 1 || r.End.Y > view.End.Y + 1)
                    && !(r.Size.X >= view.Size.X - 2 && r.Size.Y >= view.Size.Y - 2))
                    bad.Add($"{Path(n)} {r.Position.Round()}+{r.Size.Round()}");
            }
            foreach (var ch in n.GetChildren()) Walk(ch);
        }
        foreach (var layer in _m.GetChildren()) if (layer is CanvasLayer cl) Walk(cl);
        return bad;
    }
    void Tap(Vector2 canvas)
    {
        var w = Root.GetScreenTransform() * canvas;
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = w, Pressed = true });
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = w, Pressed = false });
    }
    static string Path(Node n) { var p = new List<string>(); for (var x = n; x != null && p.Count < 3; x = x.GetParent()) p.Add(x.GetType().Name == "Control" || x.GetType().Name == "TextureRect" ? x.Name : x.GetType().Name); return string.Join("<", p); }

    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["ui_mode"] = System.Environment.GetEnvironmentVariable("UI_MODE") ?? "mobile";
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                GD.Print($"窗口 {Root.GetWindow().Size} 画布 {Root.GetVisibleRect().Size} 界面 {_m.UiScale} 倍");
                var hud = OutOfBounds();
                Check(hud.Count == 0, $"地图画面（HUD、聊天）都在画面内{(hud.Count > 0 ? "：\n  " + string.Join("\n  ", hud) : "")}");
                _st = 1; _f = 0; break;
            case 1 when _f == 20:
                _m.HudSkin.MenuPressed?.Invoke(Windows[_win].Menu); _st = 2; _f = 0; break;
            case 2 when _f == 20:
            {
                var bad = OutOfBounds();
                Check(bad.Count == 0, $"「{Windows[_win].Name}」窗口在画面内{(bad.Count > 0 ? "：\n  " + string.Join("\n  ", bad.Take(8)) : "")}");
                _m.HudSkin.MenuPressed?.Invoke(Windows[_win].Menu);   // 再按一次关掉
                if (++_win < Windows.Length) { _st = 1; _f = 0; } else { _st = 30; _f = 0; }
                break;
            }
            // 聊天：触屏点「聊天」→ 输入 → 点「发送」
            case 30 when _f == 10:
            {
                var r = _m.ChatIn.ButtonRect;
                Check(r != null, $"手机模式有「聊天」按钮 {r}");
                if (r == null) { _st = 3; _f = 0; break; }
                Tap(r.Value.GetCenter()); _st = 31; _f = 0; break;
            }
            case 31 when _f == 10:
                Check(_m.ChatIn.Typing, "点「聊天」后进入输入状态（手机上会弹键盘）");
                _m.ChatIn.TestInsert("触屏聊天测试"); Tap(_m.ChatIn.ButtonRect!.Value.GetCenter()); _st = 32; _f = 0; break;
            case 32 when _f == 60:
                Check(!_m.ChatIn.Typing && _m.Chat.Lines.Any(l => l.Contains("触屏聊天测试")), $"点「发送」发出去了：{string.Join(" / ", _m.Chat.Lines.TakeLast(2))}");
                _st = 3; _f = 0; break;
            case 3 when _f == 20:
                s!.Say("/dolua gm_heal.lua"); s.Say("/warp 100 0 370 180"); _st = 4; _f = 0; break;
            case 4 when !s!.InBattle && _f > 60:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 9000) { Check(false, "遇敌"); return End(); }
                break;
            case 4 when s!.InBattle: _st = 5; _f = 0; break;
            case 5 when _f > 30 && !b.Busy && b.PanelState.Visible:
            {
                var bad = OutOfBounds();
                Check(bad.Count == 0, $"战斗指令面板在画面内{(bad.Count > 0 ? "：\n  " + string.Join("\n  ", bad.Take(8)) : "")}");
                b.TestPressSkill(); _st = 6; _f = 0; break;
            }
            case 6 when _f == 20:
            {
                var bad = OutOfBounds();
                Check(bad.Count == 0, $"战斗技能窗口在画面内{(bad.Count > 0 ? "：\n  " + string.Join("\n  ", bad.Take(8)) : "")}");
                return End();
            }
        }
        return _f > 12000;
    }
}
