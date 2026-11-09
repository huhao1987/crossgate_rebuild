using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>地图同步：没有服务端地图目录、本地也没缓存的 402 莎莲娜 → 建空图，jmb 校验不符发 UUN，aBS 补上周围 27×27；走几步继续补；存进缓存。</summary>
public partial class NetMapSync : SceneTree
{
    int _f, _st, _map0, _n0; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../nope"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
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
                foreach (var f in Directory.GetFiles(ProjectSettings.GlobalizePath("user://mapcache"), "402.dat", SearchOption.AllDirectories)) File.Delete(f);
                s.Say("/warp 402 0 100 100"); _st = 1; _f = 0; break;
            case 1 when _f == 240:
            {
                var m = _m.View!.Map;
                _n0 = m.Ground.Count(v => v != 0);
                Check(s!.CurrentMap?.MapId == 402 && m == s.Map && _n0 >= 400, $"402 空图 {m.Width}x{m.Height} 收到地面 {_n0} 格 物件 {m.Objects.Count(v => v > 999)}");
                Root.GetTexture().GetImage().SavePng("res://test/out_mapsync.png");
                var tgt = Enumerable.Range(0, 27 * 27).Select(i => new Vector2I(s.X - 13 + i % 27, s.Y - 13 + i / 27)).Where(c => _m.View!.Walk.IsWalkable(c.X, c.Y)).OrderByDescending(c => Math.Max(Math.Abs(c.X - s.X), Math.Abs(c.Y - s.Y))).FirstOrDefault();
                GD.Print($"walk to {tgt}"); _m.WalkTo(tgt); _st = 2; _f = 0; break;
            }
            case 2 when _f == 400:
            {
                var m = _m.View!.Map; int n1 = m.Ground.Count(v => v != 0);
                Check(n1 > _n0, $"走远后地面 {_n0} → {n1}（{s!.X},{s.Y}）");
                Root.GetTexture().GetImage().SavePng("res://test/out_mapsync2.png");
                s.Say("/warp 1000 0 242 100"); _st = 3; _f = 0; break;
            }
            case 3 when _f == 150:
                Check(Directory.GetFiles(ProjectSettings.GlobalizePath("user://mapcache"), "402.dat", SearchOption.AllDirectories).Length == 1, "换图后 402 写进缓存");
                return End();
        }
        return _f > 3000;
    }
}
