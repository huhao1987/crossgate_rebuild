using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>宠物表 + 宠物详情：状态按钮循环、详情各页、种族、加点（kjSK）、改名（ycX）。</summary>
public partial class NetPetDetail : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new(); int _spd0; string _nick0 = "";
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_pet_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    PetInfo? P(NetworkSession s, int slot) => s.Player.Pets.FirstOrDefault(p => p.Slot == slot);
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
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90 && s.Player.Pets.Count >= 2:
                s.Say("/dolua gm_petclear.lua"); s.SetPetState(0, PetInfo.Battle); s.Say("/dolua gm_petpoints.lua"); _st = 1; _f = 0; break;
            case 1 when _f == 90:
                _m.HudSkin.MenuPressed!(4); _st = 2; _f = 0; break;
            case 2 when _f == 20:
                Check(_m.Pets.Visible, $"宠物表打开：{string.Join("、", s!.Player.Pets.Select(p => $"{p.Slot}:{p.DisplayName} Lv{p.Level} {p.RaceName} 状态{p.State}"))}");
                Check(P(s, 0)?.RaceName == "飞行系" && P(s, 1)?.RaceName == "植物系", "种族：使魔 飞行系、树精 植物系（官方一样）");
                Check(P(s, 0)?.Level == 1, $"等级用对字段（使魔 Lv{P(s, 0)?.Level}，官方 1）");
                Shot("1_list");
                _m.Pets.TestClickState(1); _st = 3; _f = 0; break;   // 树精 待命 → 战斗
            case 3 when _f == 60:
                Check(P(s!, 1)?.State == PetInfo.Battle && P(s, 0)?.State == PetInfo.Standby, $"点树精的状态按钮：待命 → 战斗，使魔自动变待命（{P(s, 0)?.State},{P(s, 1)?.State}）");
                s.SetPetState(0, PetInfo.Battle);
                _m.Pets.OpenDetail!(0); _st = 4; _f = 0; break;
            case 4 when _f == 30:
                Check(_m.PetDetail.Visible && !_m.Pets.Visible, "点使魔 → 详情（宠物表关掉）");
                Shot("2_status");
                _m.PetDetail.TestTab(1); _st = 5; _f = 0; break;
            case 5 when _f == 20:
                _spd0 = P(s!, 0)!.Spd;
                Check(P(s, 0)!.Points == 3, $"增加点数 3（sA）：{P(s, 0)!.Points}");
                Shot("3_detail");
                _m.PetDetail.TestAddPoint(3); _st = 6; _f = 0; break;   // 速度 +1
            case 6 when _f == 60:
                Check(P(s!, 0)!.Spd == _spd0 + 1 && P(s, 0)!.Points == 2, $"速度 +1：{_spd0} → {P(s, 0)!.Spd}，剩 {P(s, 0)!.Points} 点");
                _nick0 = P(s, 0)!.Nick;
                _m.PetDetail.TestTab(0); _m.PetDetail.TestRename("小飞"); _st = 7; _f = 0; break;
            case 7 when _f == 60:
                Check(P(s!, 0)!.Nick == "小飞" && P(s, 0)!.DisplayName == "小飞", $"改名：{P(s, 0)!.DisplayName}");
                _m.PetDetail.TestTab(2); _st = 8; _f = 0; break;
            case 8 when _f == 20:
                Shot("4_techs"); _m.PetDetail.TestTab(3); _st = 9; _f = 0; break;
            case 9 when _f == 20:
                Shot("5_equip");
                _m.PetDetail.TestRename(_nick0.Length > 0 ? _nick0 : "使魔");   // 改回去
                _st = 10; _f = 0; break;
            case 10 when _f == 60:
                return End();
        }
        return _f > 4000;
    }
}
