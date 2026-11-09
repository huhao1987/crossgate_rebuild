using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>宠物邮件效果（喂假的 lDuJ）：四种效果的出发 / 到达各放一次，到达的宠物效果放完后出现在地上。</summary>
public partial class NetPetMailFx : SceneTree
{
    int _f, _st, _k; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_petmailfx_{n}.png");
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
    static string B62(int v) { const string d = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"; var s = ""; do { s = d[v % 62] + s; v /= 62; } while (v > 0); return s; }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                _st = 1; _f = 0; break;
            case 1 when _f == 1:
            {
                int x = s!.X + 1, y = s.Y, kind = _k;
                string id = "zz" + _k;
                s.TestFeed($"lDuJ {id} {B62(106058)} {B62(x)} {B62(y)} 0 1 {kind} 3|{id}|{x}|{y}|4|106058|20|0|小朱{kind}|\\\\z\\\\z|0|0|0||0|0|0");
                break;
            }
            case 1 when _f == 20 || _f == 70 || _f == 130:
                Shot($"{_k}_{_f}");
                if (_f == 20 && _k != 1) Check(!GroundVisible($"小朱{_k}") || _k == 3, $"种类 {_k}：效果放的时候宠物先不出现");
                break;
            case 1 when _f == 300:
                Check(GroundVisible($"小朱{_k}"), $"种类 {_k}：效果放完宠物出现");
                s!.TestFeed($"uSB zz{_k}");
                if (++_k < 4) { _f = 0; break; }
                // 出发
                s.TestFeed($"lDuJ yy0 {B62(106058)} {B62(s.X + 1)} {B62(s.Y)} 0 0 0 3|yy0|3|3|4|106058|20|0|小朱|\\\\z\\\\z|0|0|0||0|0|0");
                _st = 2; _f = 0; break;
            case 2 when _f == 90:
                Shot("depart"); Check(!s!.GroundItems.Any(g => g.Id == "yy0"), "出发不建对象"); return End();
        }
        return _f > 3000;
    }

    bool GroundVisible(string name)
    {
        bool found = false;
        void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is Label t && t.Text == name && t.IsVisibleInTree()) found = true; Walk(c); } }
        Walk(_m);
        return found;
    }
}
