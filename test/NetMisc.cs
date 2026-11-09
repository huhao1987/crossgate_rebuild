using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>零碎功能（test1）：扔宠物再捡回来、状态窗口 戰鬥位置 前排↔后排、拖技能换位置、宠物招式换位置（都换回去）、左上 Burst、右上「功能」第 1 页。</summary>
public partial class NetMisc : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    string _pet = ""; int _petSlot; bool _row0; string _sk0 = "", _sk1 = ""; string _t0 = "";
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_misc_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && s.Player.Pets.Count > 0 && _f > 120:
                s.Say("/warp 1000 0 236 113"); _st = 1; _f = 0; break;
            case 1 when _f == 90:
            {
                var p = s!.Player.Pets.OrderByDescending(x => x.Slot).First();
                _pet = p.Name; _petSlot = p.Slot;
                s.Face(0, 1); s.DropPet(p.Slot); _st = 2; _f = 0; break;
            }
            case 2 when s!.GroundItems.Any(g => g.Pet) || _f > 200:
            {
                var g = s.GroundItems.FirstOrDefault(x => x.Pet);
                Check(g != null && g.Name == _pet && s.Player.Pets.All(p => p.Slot != _petSlot), $"扔宠物：地上 {g?.Name} ({g?.X},{g?.Y})，宠物栏 {_petSlot} 空了");
                Shot("1_dropped");
                if (g == null) return End();
                s.PickUp(g.X, g.Y); _st = 3; _f = 0; break;
            }
            case 3 when s!.Player.Pets.Any(p => p.Name == _pet) && !s.GroundItems.Any(g => g.Pet) || _f > 200:
                Check(s!.Player.Pets.Any(p => p.Name == _pet), $"捡回来了：{_pet}");
                _row0 = s.BackRow; s.ToggleBattleRow(); _st = 4; _f = 0; break;
            case 4 when s!.BackRow != _row0 || _f > 120:
                Check(s!.BackRow != _row0, $"戰鬥位置：{(_row0 ? "後排" : "前排")} → {(s.BackRow ? "後排" : "前排")}");
                _m.StatusWin.Visible = true; _m.StatusWin.Refresh(); _st = 41; _f = 0; break;
            case 41 when _f == 20:
                Shot("2_row"); _m.StatusWin.Visible = false;
                s!.ToggleBattleRow();
                _sk0 = s.Player.Skills[0].Name; _sk1 = s.Player.Skills[1].Name;
                s.SwapSkills(s.Player.Skills[0].Slot, s.Player.Skills[1].Slot); _st = 5; _f = 0; break;
            case 5 when s!.Player.Skills[0].Name == _sk1 || _f > 120:
                Check(s!.Player.Skills[0].Name == _sk1 && s.Player.Skills[1].Name == _sk0, $"拖技能换位置：{_sk0},{_sk1} → {s.Player.Skills[0].Name},{s.Player.Skills[1].Name}");
                s.SwapSkills(s.Player.Skills[0].Slot, s.Player.Skills[1].Slot);
                var pt5 = s.Player.Pets.First(p => p.Techs.Count >= 2);
                _petSlot = pt5.Slot; _t0 = pt5.Techs.First(t => t.Index == 0).Name;
                s.SwapPetTechs(pt5.Slot, 0, 1); _st = 6; _f = 0; break;
            case 6 when s!.Player.Pets.First(p => p.Slot == _petSlot).Techs.FirstOrDefault(t => t.Index == 1)?.Name == _t0 || _f > 120:
            {
                var pt = s!.Player.Pets.First(p => p.Slot == _petSlot);
                Check(pt.Techs.FirstOrDefault(t => t.Index == 1)?.Name == _t0, $"宠物招式换位置：第 1 招 {_t0} 到了第 2 行（现在 {string.Join(",", pt.Techs.OrderBy(t => t.Index).Select(t => t.Name))}）");
                s.SwapPetTechs(pt.Slot, 0, 1);
                Check(_m.HudSkin.BurstText.Length == 5, $"左上 Burst {_m.HudSkin.BurstText}");
                _m.FuncUi.TestPage(0);
                Check(_m.FuncUi.Open, "右上「功能」打开第 1 页");
                _st = 7; _f = 0; break;
            }
            case 7 when _f == 20: Shot("3_func"); _m.FuncUi.Close(); _st = 8; _f = 0; break;
            case 8 when _f == 30: return End();
        }
        return _f > 3000;
    }
}
