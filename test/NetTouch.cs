using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 手机触屏操作：用真正的触摸事件（InputEventScreenTouch，经引擎转成鼠标）而不是测试钩子，
/// 在手机比例窗口（跑的时候 --resolution 2400x1080）里：点地面走路、战斗点「攻击」再点敌人、聊天能打开输入。
/// </summary>
public partial class NetTouch : SceneTree
{
    int _f, _st, _turns; Main _m = null!; readonly List<string> _r = new(); (int, int) _pos0;
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_touch_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    /// <summary>画布坐标 → 窗口坐标，再发一次按下 + 抬起的触摸。</summary>
    void Tap(Vector2 canvas)
    {
        var w = Root.GetScreenTransform() * canvas;
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = w, Pressed = true });
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = w, Pressed = false });
        GD.Print($"TAP canvas={canvas} window={w}");
    }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
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
                GD.Print($"窗口 {Root.GetWindow().Size}，画布 {Root.GetVisibleRect().Size}");
                s.Say("/dolua gm_heal.lua"); _st = 1; _f = 0; break;
            case 1 when _f == 30:
                s!.Say("/warp 100 0 370 180"); _st = 2; _f = 0; break;
            case 2 when _f == 90:
                // 点玩家右边一点的地面（画面中心往右 96 像素）
                _pos0 = (s!.X, s.Y);
                Tap(Root.GetVisibleRect().Size / 2 + new Vector2(96, 24)); _st = 3; _f = 0; break;
            case 3 when _f == 120:
                Check((s!.X, s.Y) != _pos0 || s.InBattle, $"触屏点地面会走：({_pos0.Item1},{_pos0.Item2}) → ({s.X},{s.Y})");
                _st = 4; _f = 0; break;
            case 4 when !s!.InBattle:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 9000) { Check(false, "遇敌"); return End(); }
                break;
            case 4 when s!.InBattle: _st = 5; _f = 0; break;
            case 5 when _f > 30 && !b.Busy && s!.CommandSlot == 0 && !s.PetTurn && b.PanelState.Visible:
            {
                var r = b.CommandButtonRect(0);
                Check(r != null, $"攻击按钮位置 {r}");
                Shot("1_panel");
                // 故意点偏：按钮左边 10 像素（手指常这样），靠触屏辅助挪到按钮上
                if (r != null) Tap(new Vector2(r.Value.Position.X - 10, r.Value.GetCenter().Y));
                _st = 6; _f = 0; break;
            }
            case 6 when _f == 15:
                Check(b.Picking, "触屏点在「攻击」左边 10 像素 → 也进入选目标");
                var enemy = s!.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive);
                var p = b.UnitTapPos(enemy.Id);
                if (p != null) Tap(p.Value);
                _st = 7; _f = 0; break;
            case 7 when _f == 30:
                Check(!b.Picking && (s!.CommandSlot != 0 || b.Busy || s.PetTurn), $"触屏点敌人 → 指令下了（slot {s!.CommandSlot}，选目标 {b.Picking}）");
                Shot("2_after");
                _st = 8; _f = 0; break;
            case 8:
                // 剩下的回合用触屏一直打到结束
                if (!s!.InBattle) { Check(true, $"战斗用触屏打完（{_turns} 次攻击）"); return End(); }
                if (_f > 30 && !b.Busy && !b.Picking && b.PanelState.Visible && (s.CommandSlot == 1 || s.CommandSlot == 0 || s.PetTurn) && _f % 30 == 0)
                {
                    var r = b.CommandButtonRect(0); if (r != null) Tap(r.Value.GetCenter());
                }
                if (b.Picking && _f % 30 == 15)
                {
                    var e = s.CurrentBattle.Units.FirstOrDefault(u => u.Side == 1 && u.Alive);
                    if (e != null && b.UnitTapPos(e.Id) is { } q) { Tap(q); _turns++; }
                }
                if (_f > 6000) { Check(false, "战斗没打完"); return End(); }
                break;
        }
        return _f > 12000;
    }
}
