using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>战斗中换武器（Rg Q|格子|）、对调位置（Rg P）。test1 背包里要有一把剑（NetChar 留下的铜剑，没有就 GM 给）。</summary>
public partial class NetWeapon : SceneTree
{
    int _f, _st, _sword = -1; Main _m = null!; readonly List<string> _r = new(); string _since = "";
    void Check(bool ok, string n) => _r.Add((ok ? "PASS " : "FAIL ") + n);
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
    void PetAttackIfAsked(NetworkSession s, BattleUi b)
    {
        if (s.PetTurn && b.PetPhaseShown && !b.Picking) { b.TestClickSub(0); b.TestPickTarget(s.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive).Id); }
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        if (_f % 300 == 0) GD.Print($"STATE st={_st} f={_f} inGame={s?.InGame} battle={s?.InBattle} slot={s?.CommandSlot} pet={s?.PetTurn} busy={b?.Busy} turn={(s?.InBattle == true ? s.CurrentBattle.Turn : -1)}");
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                if (!s.Player.Items.Any(i => BattleUi.IsWeapon(i))) { s.Say("/dolua gm_clear.lua"); s.Say("/giveItem 0 1 1 1"); }
                s.Say("/dolua gm_heal.lua"); _st = 10; _f = 0; break;
            case 10 when _f == 60: s!.Say("/warp 100 0 370 180"); _st = 1; _f = 0; break;
            case 1 when _f > 60 && !s!.InBattle:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 9000) { Check(false, "遇敌"); return End(); }
                break;
            case 1 when s!.InBattle: _st = 2; _f = 0; break;
            case 2 when _f > 30 && !b.Busy && s!.CommandSlot == 0 && !s.PetTurn:
                _sword = s.Player.Items.Where(BattleUi.IsWeapon).Select(i => i.Slot).DefaultIfEmpty(-1).First();
                Check(_sword >= 8, $"背包里有武器（格子 {_sword}）");
                if (_sword < 8) return End();
                b.TestPressWeapon(); _st = 3; _f = 0; break;
            case 3 when _f == 15:
                Root.GetTexture().GetImage().SavePng("res://test/out_weapon.png");
                b.TestClickWeaponCell(_sword - 8); _st = 4; _f = 0; break;
            case 4 when _f == 40:
                Check(Log().Contains($"Rg Q|{_sword:X}|"), $"点武器 → 服务端收到 Rg Q|{_sword:X}|");
                _st = 5; _f = 0; break;
            case 5:
                PetAttackIfAsked(s!, b);
                // 指令2（没带宠物时）：普通攻击
                if (_f > 30 && !b.Busy && s!.CommandSlot == 1 && !s.PetTurn && !b.Picking && b.PanelState.Visible) { b.TestPressAttack(); b.TestPickTarget(s.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive).Id); }
                if (!s!.InBattle) { Check(false, "战斗在试位置前结束了"); return End(); }
                if (_f > 30 && !b.Busy && s.CommandSlot == 0 && !s.PetTurn && s.CurrentBattle.Turn >= 1) { b.TestPressPosition(); _st = 6; _f = 0; }
                break;
            case 6 when _f == 40:
                Check(Log().Contains("Rg P"), "位置 → 服务端收到 Rg P");
                return End();
        }
        return false;
    }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
}
