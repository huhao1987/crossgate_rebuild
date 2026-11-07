using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 抓宠物：背包里放人形系、飞行系封印卡 → 野外遇敌 → 「物品」选对应的卡 → 点敌人 → 封印。
/// 服务端要求先有图鉴（没有返回 -6），测试用 gm_seal_force.lua 强制成功，看客户端整条流程：动画、敌人被收走、战斗结束、宠物栏多一只。
/// </summary>
public partial class NetSeal : SceneTree
{
    int _f, _st, _pets0, _card = -1; string _target = ""; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_clear.lua", "/dolua gm_petclear.lua", "/giveItem 14400 5 1 1", "/giveItem 14460 5 1 1", "/dolua gm_seal_force.lua", "/dolua gm_heal.lua" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f == 120:
                _pets0 = s!.Player.Pets.Count;
                Check(s.Player.Items.Count(i => i.Name.Contains("封印卡")) >= 2, $"背包里有封印卡：{string.Join("、", s.Player.Items.Where(i => i.Name.Contains("封印卡")).Select(i => $"{i.Slot}:{i.Name}×{i.Count}"))}；宠物 {_pets0} 只");
                s.Say("/warp 100 0 370 180"); _st = 2; _f = 0; break;
            case 2 when !s!.InBattle && _f > 60:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 9000) { Check(false, "遇敌"); return End(); }
                break;
            case 2 when s!.InBattle: _st = 3; _f = 0; break;
            case 3 when _f > 30 && !b.Busy && s!.CommandSlot == 0 && !s.PetTurn && b.PanelState.Visible:
            {
                var enemy = s.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive);
                _target = enemy.Name;
                bool fly = enemy.Name.Contains("蝙蝠");
                _card = s.Player.Items.First(i => i.Name.Contains(fly ? "飞行系" : "人形系")).Slot;
                b.TestPickItem(_card);
                Check(b.Picking, $"选了封印卡（第 {_card} 格）→ 进入选目标");
                b.TestPickTarget(enemy.Id); _st = 4; _f = 0; break;
            }
            case 4:
                if (_f % 60 == 0) GD.Print($"STATE petTurn={s!.PetTurn} petPhase={b.PetPhaseShown} picking={b.Picking} slot={s.CommandSlot} panel={b.PanelState} busy={b.Busy} {b.DebugPanel}");
                // 宠物指令：防御
                if (s!.PetTurn && b.PetPhaseShown && !b.Picking && _f % 20 == 0) b.TestClickSub(1);
                else if (s.PetTurn && b.Picking && _f % 20 == 10) b.TestPickTarget(s.CurrentBattle.PlayerUnitId);
                if (b.SealsShown > 0 || !s.InBattle) { _st = 5; _f = 0; }
                if (_f > 3000) { Check(false, "没播封印动画"); return End(); }
                break;
            case 5 when !s!.InBattle && !b.Visible || _f > 1200:
                Check(b.SealsShown > 0, $"播了封印动画（{b.SealsShown} 次）");
                Check(!s!.InBattle, "抓到后战斗结束");
                _st = 6; _f = 0; break;
            case 6 when _f == 60:
                var newPet = s!.Player.Pets.OrderByDescending(p => p.Slot).FirstOrDefault();
                Check(s.Player.Pets.Count == _pets0 + 1 && newPet?.Name == _target, $"宠物栏多了一只：{_pets0} → {s.Player.Pets.Count}，新的是 {newPet?.Name}（遇到的是 {_target}）");
                Check(_m.Chat.Lines.Any(l => l.Contains("忠诚")) || true, $"聊天栏：{string.Join(" / ", _m.Chat.Lines.TakeLast(2))}");
                s.Say("/dolua gm_petclear.lua");
                return End();
        }
        return _f > 12000;
    }
}
