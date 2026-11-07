using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>联网宠物窗口：底部菜单 → 宠物 → 使魔设为待命 → 再设回战斗；每步看服务端回的宠物状态。</summary>
public partial class NetPets : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    PetInfo? Pet0 => (_m.Session as NetworkSession)?.Player.Pets.FirstOrDefault(p => p.Slot == 0);
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                _m.HudSkin.ShowMenu(); _st = 1; _f = 0; break;
            case 1 when _f == 10:
                Root.GetTexture().GetImage().SavePng("res://test/out_menu.png");
                _m.HudSkin.MenuPressed!(4); _st = 2; _f = 0; break;
            case 2 when _f == 10:
                Check(_m.Pets.Visible && Pet0 != null, $"宠物窗口打开，使魔状态 {Pet0?.State}");
                Root.GetTexture().GetImage().SavePng("res://test/out_pets.png");
                s!.SetPetState(0, PetInfo.Standby); _st = 3; _f = 0; break;
            case 3 when _f == 90:
                Check(Pet0?.State == PetInfo.Standby, $"设为待命 → 服务端回状态 {Pet0?.State}");
                s!.SetPetState(0, PetInfo.Battle); _st = 4; _f = 0; break;
            case 4 when _f == 90:
                Check(Pet0?.State == PetInfo.Battle && s!.Player.Pets.Count(p => p.State == PetInfo.Battle) == 1, $"设回战斗 → 服务端回状态 {Pet0?.State}，只有一只战斗");
                Root.GetTexture().GetImage().SavePng("res://test/out_pets2.png");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 4000;
    }
}
