using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>物品换格（test1）：yi 甲 乙 0 → 服务端只回 vWpn 甲 乙（不补 rU），客户端本地对调；再换回来。</summary>
public partial class NetSwap : SceneTree
{
    int _f, _st, _map0, _a, _b, _id, _id2; Main _m = null!; readonly List<string> _r = new();
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
            {
                var it = s.Player.Items.OrderBy(i => i.Slot).First();
                _a = it.Slot; _id = it.ItemId;
                var o = s.Player.Items.OrderBy(i => i.Slot).Skip(1).First(i => i.ItemId != _id); _b = o.Slot; _id2 = o.ItemId;
                s.MoveItem(_a, _b); _st = 1; _f = 0; break;
            }
            case 1 when _f == 90:
                Check(s!.Player.Items.Any(i => i.Slot == _b && i.ItemId == _id) && s.Player.Items.Any(i => i.Slot == _a && i.ItemId == _id2), $"格 {_a} → {_b}：{string.Join(",", s.Player.Items.OrderBy(i => i.Slot).Take(4).Select(i => $"{i.Slot}:{i.ItemId}:{i.Name}"))} id {_id}/{_id2}");
                s.MoveItem(_b, _a); _st = 2; _f = 0; break;
            case 2 when _f == 90:
                Check(s!.Player.Items.Any(i => i.Slot == _a && i.ItemId == _id), $"换回 {_a}");
                return End();
        }
        return _f > 3000;
    }
}
