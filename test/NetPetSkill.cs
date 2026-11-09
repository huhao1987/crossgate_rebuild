using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 宠物技能店（WN 24 → 25 → 26，宠物技能店的佩蒂 1154 30,20）：选「什么都不做」(50G) → 选第一只宠物 → 选第一个空栏 → 确认框 → 是 →
/// 扣 50、宠物多了这招。测完 gm_petreset 还原宠物。
/// </summary>
public partial class NetPetSkill : SceneTree
{
    int _f, _st; long _gold0; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_petskill_{n}.png");
    bool End() { (_m.Session as NetworkSession)?.Say("/dolua gm_petreset.lua"); _st = 99; _f = 0; return false; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    string Techs(NetworkSession s) => string.Join(",", s.Player.Pets.OrderBy(p => p.Slot).FirstOrDefault()?.Techs.Select(t => t.Name) ?? Array.Empty<string>());
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var w = _m.PetShop;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_petreset.lua", "/addGold 1000", "/warp 1154 0 31 20" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f > 180 && s!.CurrentMap?.MapId == 1154:
            {
                var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == "宠物技能店的佩蒂");
                if (n != null) { _page = null; _m.TalkOrApproach(n); }
                _st = 2; _f = 0; break;
            }
            case 2 when _page?.Kind == "petskill" && _f > 30:
            {
                var l = w.Page!.Shop!;
                Check(w.Visible && l.Count >= 4, $"选技能：{string.Join("、", l.Select(e => $"{e.Name} 魔{e.Image} {e.Price}G"))}");
                Shot("1_skills"); _gold0 = s!.Player.Gold;
                int i = l.ToList().FindIndex(e => e.Name == "什么都不做");
                _page = null; w.PickSkill(i); _st = 3; _f = 0; break;
            }
            case 2 when _f > 600: Check(false, $"宠物技能店没出来（{_page?.Kind}）"); return End();
            case 3 when _page?.Kind == "petpick" && _f > 30:
                Check(w.Visible && w.Page!.Shop!.Count >= 1, $"选宠物（要求等级 {w.Page!.SourceItem}）：{string.Join("、", w.Page.Shop!.Select(e => $"{e.Name} Lv{e.Count} {e.Max}栏"))}");
                Shot("2_pets"); _page = null; w.PickPet(0); _st = 4; _f = 0; break;
            case 3 when _f > 600: Check(false, $"选宠物没出来（{_page?.Kind}）"); return End();
            case 4 when _page?.Kind == "petslot" && _f > 30:
            {
                var l = w.Page!.Shop!;
                int empty = l.ToList().FindIndex(e => e.Name.Length == 0);
                Check(w.Visible && empty >= 0, $"选栏位：{string.Join("、", l.Select(e => e.Name.Length == 0 ? "空" : e.Name))}；学之前 {Techs(s!)}");
                Shot("3_slots"); w.PickSlot(empty); _st = 5; _f = 0; break;
            }
            case 4 when _f > 600: Check(false, $"选栏位没出来（{_page?.Kind}）"); return End();
            case 5 when _f == 20:
                Check(w.ConfirmSlot >= 0, "确认框"); Shot("4_confirm"); w.Yes(); _st = 6; _f = 0; break;
            case 6 when _f == 150:
                Check(s!.Player.Gold == _gold0 - 50 && Techs(s).Contains("什么都不做"), $"学会：金钱 {_gold0} → {s.Player.Gold}，技能 {Techs(s)}");
                return End();
            case 99 when _f == 90: GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 4000;
    }
}
