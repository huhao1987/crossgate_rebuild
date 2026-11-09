using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>天气 EF（zzd）：阿巴尼斯村（4300）effect.txt 全年下雪 强度 2 → 收到 zzd 2 2、画面有雪花；回法兰城停。</summary>
public partial class NetWeather : SceneTree
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
                s.Say("/warp 4300 0 50 50"); _st = 1; _f = 0; break;
            case 1 when _f == 240:
                Check(s!.CurrentMap?.MapId == 4300 && s.Weather == (2, 2), $"进阿巴尼斯村 → zzd {s.Weather} 地图 {s.CurrentMap?.MapId} ({s.X},{s.Y})");
                Check(_m.WeatherFx?.Flags == 2 && _m.WeatherFx.Count > 20, $"画面雪花 {_m.WeatherFx?.Count} 片");
                Root.GetTexture().GetImage().SavePng("res://test/out_weather.png"); _m.WeatherFx!.GetParent<CanvasLayer>().Visible = false; _st = 5; _f = 0; break; case 5 when _f == 5: Root.GetTexture().GetImage().SavePng("res://test/out_weather2.png");
                s.Say("/warp 1000 0 242 100"); _st = 2; _f = 0; break;
            case 2 when _f == 200:
                Check(s!.Weather.Flags == 0 && _m.WeatherFx?.Count == 0, $"回法兰城停 {s.Weather} {_m.WeatherFx?.Count}");
                return End();
        }
        return _f > 3000;
    }
}
