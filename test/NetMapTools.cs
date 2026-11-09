using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>地图工具（test1）：小地图两个蓝球 → 全屏大地图（标注、右键选目标、开始寻路）、自動尋路列表（法兰城坐标点、設定）。</summary>
public partial class NetMapTools : SceneTree
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
                s.Say("/warp 1000 0 228 89"); _st = 1; _f = 0; break;
            case 1 when _f == 120:
                _m.MapWin.Visible = true; _m.MapWin.Refresh(); _st = 2; _f = 0; break;
            case 2 when _f == 20:
                Root.GetTexture().GetImage().SavePng("res://test/out_minimap.png");
                _m.MapWin.OpenBigMap!(); _st = 3; _f = 0; break;
            case 3 when _f == 30:
                Check(ExeArt.Ok && _m.BigMap.Visible && _m.BigMap.LabelCount >= 2, $"大地图：exe 资源 {ExeArt.Ok}，标注 {_m.BigMap.LabelCount} 个");
                Root.GetTexture().GetImage().SavePng("res://test/out_bigmap.png");
                _m.BigMap.PickAt(new Vector2(527 - 67, 301 - 52)); _st = 4; _f = 0; break;
            case 4 when _f == 20:
                Check(_m.BigMap.Target is { } t && Math.Abs(t.X - 248) <= 1 && Math.Abs(t.Y - 84) <= 1, $"右键 (527,301) → 目标 {_m.BigMap.Target}（官方 248,84）");
                Root.GetTexture().GetImage().SavePng("res://test/out_bigmap2.png");
                _m.BigMap.Visible = false; _m.MapWin.OpenAutoPath!(); _st = 5; _f = 0; break;
            case 5 when _f == 20:
                Check(_m.AutoPath.Visible && _m.AutoPath.PageCount >= 3, $"自動尋路列表 {_m.AutoPath.PageCount} 页");
                Root.GetTexture().GetImage().SavePng("res://test/out_autopath.png");
                _m.AutoPath.SetTarget(new Vector2I(232, 89)); _m.AutoPath.Go!(new Vector2I(232, 89)); _st = 6; _f = 0; break;
            case 6 when _f == 200:
                Check(s!.X == 232 && s.Y == 89, $"执行寻路走到 ({s.X},{s.Y})");
                return End();
        }
        return _f > 3000;
    }
}
