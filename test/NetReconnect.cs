using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 断线重连（test1）：进游戏 → 模拟掉线（关 socket）→ 自动重连回到原来的地图 → 再走一步服务端认
/// → 到芙蕾雅遇敌，战斗中掉线 → 战斗画面关掉、重连后回到地图能继续走。
/// </summary>
public partial class NetReconnect : SceneTree
{
    int _f, _st, _drops; (int, int) _pos; int _map; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        if (s != null && _drops == 0) { _drops = -1; s.Disconnected += _ => _drops = Math.Max(1, _drops + 1); }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/warp 1000 0 242 100"); _st = 1; _f = 0; break;
            case 1 when _f == 90:
                _pos = (s!.X, s.Y); _map = s.CurrentMap!.MapId;
                s.TestDrop(); _st = 2; _f = 0; break;
            case 2 when _f == 30:
                Check(!s!.InGame && _drops >= 1, $"掉线被发现（断线 {_drops} 次，InGame {s.InGame}）"); _st = 3; _f = 0; break;
            case 3 when s!.InGame && s.CurrentMap != null && _f > 30 || _f > 2400:
                Check(s.InGame && s.CurrentMap?.MapId == _map && Math.Abs(s.X - _pos.Item1) <= 1 && Math.Abs(s.Y - _pos.Item2) <= 1,
                    $"自动重连回来了：第 {s.Reconnects} 次重连，地图 {s.CurrentMap?.MapId}，位置 ({s.X},{s.Y})（掉线前 {_map} {_pos}），用了 {_f / 60.0:0.0} 秒");
                _pos = (s.X, s.Y); s.Step(2); _st = 4; _f = 0; break;
            case 4 when _f == 60:
                Check((s!.X, s.Y) != _pos, $"重连后还能走：({_pos.Item1},{_pos.Item2}) → ({s.X},{s.Y})");
                s.Say("/warp 100 0 370 180"); _st = 5; _f = 0; break;
            case 5 when _f > 60 && !s!.InBattle:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 6000) { Check(false, "遇敌"); return End(); }
                break;
            case 5 when s!.InBattle && b.Visible: _st = 6; _f = 0; break;
            case 6 when _f == 60:
                s!.TestDrop(); _st = 7; _f = 0; break;
            case 7 when _f == 30:
                Check(!b.Visible, "战斗中掉线：战斗画面关掉"); _st = 8; _f = 0; break;
            case 8 when s!.InGame && s.CurrentMap != null && _f > 60 || _f > 3000:
                Check(s.InGame, $"战斗中掉线后重连回来（第 {s.Reconnects} 次，地图 {s.CurrentMap?.MapId}，战斗中 {s.InBattle}）");
                _st = 9; _f = 0; break;
            case 9 when _f == 240:
                Check(!b.Visible || s!.InBattle, $"画面状态一致：战斗画面 {b.Visible}，服务端战斗 {s!.InBattle}");
                s.Say("/warp 1000 0 242 100");
                return End();
        }
        return _f > 8000;
    }
}
