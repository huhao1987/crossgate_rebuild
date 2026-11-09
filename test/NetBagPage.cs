using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>背包翻页（私服「背包1–4」，SWITM）：开放全部页（gm_vipbag）→ 点背包2 → 整页 rU、高亮换到 2 → 点背包1 换回。</summary>
public partial class NetBagPage : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_bag_{n}.png");
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
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/dolua gm_vipbag.lua"); _m.ItemsUi.Toggle(); _st = 1; _f = 0; break;
            case 1 when _f == 40:
                Shot("1_page1"); Check(s!.BagPage == 0, "开始在背包1");
                _m.ItemsUi.TestBagTab(1); _st = 2; _f = 0; break;
            case 2 when s!.BagPage == 1 || _f > 300:
                Check(s.BagPage == 1, $"点背包2 → 服务端整页 rU，现在第 {s.BagPage + 1} 页（{s.Player.Items.Count(i => i.Slot >= 8)} 件）");
                _st = 3; _f = 0; break;
            case 3 when _f == 20:
                Shot("2_page2"); _m.ItemsUi.TestBagTab(0); _st = 4; _f = 0; break;
            case 4 when s!.BagPage == 0 || _f > 300:
                Check(s.BagPage == 0, $"点背包1 换回（{s.Player.Items.Count(i => i.Slot >= 8)} 件）");
                return End();
        }
        return _f > 3000;
    }
}
