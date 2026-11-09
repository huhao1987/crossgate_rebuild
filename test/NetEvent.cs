using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>事件格（test1）：法兰城宠物店门口 (171,132) 服务端推 aBS 事件 a → 站上去发 pg → 传送进宠物店。</summary>
public partial class NetEvent : SceneTree
{
    int _f, _st, _map0; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
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
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 120:
                s.Say("/warp 1000 0 170 133"); _st = 1; _f = 0; break;
            case 1 when _f == 90:
                _map0 = s!.CurrentMap!.MapId;
                _m.WalkTo(new Vector2I(171, 132)); _st = 2; _f = 0; break;
            case 2 when s!.CurrentMap?.MapId != _map0 || _f > 400:
                Check(s!.CurrentMap?.MapId != _map0, $"走到宠物店门口 → 事件格 {string.Join(",", s.EventTiles.Select(kv => $"({kv.Key.Item1},{kv.Key.Item2})={kv.Value}"))} → 进了 {s.CurrentMap?.Name} ({s.CurrentMap?.MapId})");
                s.Say("/warp 1000 0 242 100");
                return End();
        }
        return _f > 3000;
    }
}
