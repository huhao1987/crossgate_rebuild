using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// WN 42（TechshopEX 李道特 59801 121,121，教人形宠物"座骑Ⅰ"）：官方分派表 42 和 24 共用同一个处理函数，移植把 42 当 24 显示。
/// 只看窗口出来、内容对，截图，不真学。
/// </summary>
public partial class NetPetSkillEx : SceneTree
{
    int _f, _st; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
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
        var s = _m.Session as NetworkSession; var w = _m.PetShop;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/warp 59801 0 121 122"); _st = 1; _f = 0; break;
            case 1 when _f > 180 && s!.CurrentMap?.MapId == 59801:
            {
                var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == "李道特");
                Check(n != null, "找到李道特");
                if (n != null) { _page = null; _m.TalkOrApproach(n); }
                _st = 2; _f = 0; break;
            }
            case 2 when _page?.Kind == "petskill" && _f > 30:
            {
                var l = w.Page!.Shop!;
                Check(w.Visible && l.Any(e => e.Name == "座骑Ⅰ" && e.Price == 2000), $"WN 42 按 24 显示：{string.Join("、", l.Select(e => $"{e.Name} {e.Price}G"))}（{w.Page.NpcName}）");
                Root.GetTexture().GetImage().SavePng("res://test/out_petskillex.png");
                w.Cancel(); _st = 99; _f = 0; break;
            }
            case 2 when _f > 600: Check(false, $"窗口没出来（{_page?.Kind}）"); _st = 99; _f = 0; break;
            case 99 when _f == 60: GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 4000;
    }
}
