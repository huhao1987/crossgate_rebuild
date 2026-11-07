using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>战斗中换宠物：带使魔遇敌 → 宠物 → 收回（M|FF）→ 宠物指令 → 下一回合 宠物 → 叫出树精（M|1）→ 指令2。</summary>
public partial class NetSwap : SceneTree
{
    int _f, _st, _petSet; Main _m = null!; readonly List<string> _r = new(); string _since = "";
    void Check(bool ok, string n) => _r.Add((ok ? "PASS " : "FAIL ") + n);
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_swap_{n}.png");
    string Log() => string.Join("\n", Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(x => x).TakeLast(1).SelectMany(File.ReadLines).Where(l => l.Contains("\ttest1\t") && string.CompareOrdinal(l[..8], _since) >= 0));
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        _since = DateTime.UtcNow.ToString("HH:mm:ss");
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && s.Player.Pets.Count >= 2 && _petSet == 0:
                if (!s.Player.Pets.Any(p => p.Slot == 0 && p.State == PetInfo.Battle)) s.SetPetState(0, PetInfo.Battle);
                s.Say("/dolua gm_heal.lua"); _petSet = _f; break;
            case 0 when _petSet > 0 && _f > _petSet + 60:
                s!.Say("/warp 100 0 370 180"); _st = 1; _f = 0; break;
            case 1 when _f > 60 && !s!.InBattle:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 9000) { Check(false, "遇敌"); return End(); }
                break;
            case 1 when s!.InBattle: _st = 2; _f = 0; break;
            case 2 when _f > 30 && !b.Busy && s!.CommandSlot == 0 && !s.PetTurn:
                b.TestPressPet(); _st = 3; _f = 0; break;
            case 3 when _f == 15:
                Shot("1_window");
                Check(b.TestPressAt(728, 400), "更换宠物窗口有「收回」（宠物在场）");
                _st = 4; _f = 0; break;
            case 4 when _f > 20 && b.PetPhaseShown && s!.PetTurn:
                Check(Log().Contains("Rg M|FF"), "收回 → 服务端收到 Rg M|FF，接着要宠物指令（A|401）");
                b.TestClickSub(0); _st = 5; _f = 0; break;
            case 4 when _f > 600: Check(false, $"收回后宠物指令（PetTurn {s!.PetTurn}）"); return End();
            case 5 when _f == 10:
                b.TestPickTarget(s!.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive).Id); _st = 6; _f = 0; break;
            case 6 when _f > 30 && !s!.InBattle: Check(false, "战斗在叫出树精前就结束了"); return End();
            case 6 when _f > 30 && !b.Busy && s!.CommandSlot == 0 && !s.PetOnField:
                b.TestPressPet(); _st = 7; _f = 0; break;
            case 7 when _f == 15:
                Shot("2_window_nopet");
                Check(!b.TestPressAt(728, 400), "宠物不在场时没有「收回」");
                var tree = s!.Player.Pets.First(p => p.Name == "树精");
                b.TestClickSub(s.Player.Pets.IndexOf(tree)); _st = 8; _f = 0; break;
            case 8 when _f == 60:
                Check(Log().Contains("Rg M|1"), "点树精 → 服务端收到 Rg M|1");
                Check(b.CommandSlot == 1 && b.PanelState.Visible && b.PanelState.Second, $"叫出宠物算指令1，接着是指令2（slot {b.CommandSlot}）");
                Shot("3_after");
                return End();
        }
        return false;
    }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
}
