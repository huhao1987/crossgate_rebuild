using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 联网战斗技能 + 宠物指令（test1：有气功弹等技能、使魔是战斗状态）：
/// 传送到芙蕾雅 → 走到遇敌 → 技能窗口 → 气功弹 → LV1 → 点敌人 → 宠物指令 → 攻击 → 点敌人；每步截图，最后查服务端封包日志。
/// </summary>
public partial class NetSkill : SceneTree
{
    int _f, _st, _petSet; Main _m = null!; readonly List<string> _r = new(); string _since = "";
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_skill_{n}.png");
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        _since = DateTime.UtcNow.AddHours(0).ToString("HH:mm:ss");
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        switch (_st)
        {
            // 等宠物数据到了（IsSy 比进图晚），第一只不是战斗就设成战斗（NetE2E 会把它改回待命）
            case 0 when s?.InGame == true && s.CurrentMap != null && s.Player.Pets.Count > 0 && _petSet == 0:
                // 每次都设一次：刚抓到的宠物 IsSy 状态是 2，但服务端的出战宠物可能没设（NetSeal 抓的迷你蝙蝠）
                s.SetPetState(s.Player.Pets[0].Slot, CgPort.Server.PetInfo.Battle);
                _petSet = _f; break;
            case 0 when s?.InGame == true && _petSet > 0 && _f > _petSet + 60 && s.Player.Pets.Any(p => p.State == 2):
                Check(s.Player.Skills.Count >= 4 && s.Player.Pets.Any(p => p.State == 2), $"登录后有技能 {s.Player.Skills.Count} 个、战斗宠物 {s.Player.Pets.FirstOrDefault(p => p.State == 2)?.Name}");
                s.Say("/dolua gm_heal.lua"); s.Say("/warp 100 0 370 180"); _st = 1; _f = 0; break;
            case 1 when _f > 60 && !s!.InBattle:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 6000) { Check(false, "遇敌"); return End(); }
                break;
            case 1 when s!.InBattle: _st = 2; _f = 0; break;
            case 2 when _f > 30 && !b.Busy && s!.CommandSlot == 0:
                Check(b.PanelState.Visible && s.HasBattlePet, $"带宠物战斗，玩家指令面板（带宠物 {s.HasBattlePet}，单位 {string.Join(",", s.CurrentBattle.Units.Select(u => u.Id + u.Name))}，我 {s.CurrentBattle.PlayerUnitId}，{b.DebugPanel}）");
                b.TestPressSkill(); _st = 3; _f = 0; break;
            case 3 when _f == 20: Shot("1_skills"); b.TestClickSub(_skill); _st = 4; _f = 0; break;   // 默认第 2 个技能 = 气功弹；SKILL_IDX=2 是陨石
            case 4 when _f == 20: Shot("2_techs"); b.TestClickSub(0); _st = 5; _f = 0; break;
            case 5 when _f == 10:
                Check(b.Picking, "选了招式后进入选目标");
                b.TestPickTarget(s!.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive).Id); _st = 6; _f = 0; break;
            case 6 when _f > 10 && b.PetPhaseShown && s!.PetTurn:
                Shot("3_pet"); Check(true, "下完人物指令后显示宠物指令"); b.TestClickSub(0); _st = 7; _f = 0; break;
            case 6 when _f > 600: Shot("3_pet"); Check(false, $"宠物指令面板（PetTurn {s!.PetTurn}，面板 {b.PetPhaseShown}）"); return End();
            case 7 when _f == 10:
                b.TestPickTarget(s!.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive).Id); _st = 8; _f = 0; break;
            case 8 when !_mid && b.SkillsPlayed > 0: _mid = true; _midF = _f; Shot("4_cast"); break;
            case 8 when _mid && _midF > 0 && _f == _midF + 50: Shot("5_fx"); break;
            case 8 when _f == 360:
                Check(b.SkillsPlayed > 0, $"技能动画播了 {b.SkillsPlayed} 组（事件 {b.PlayedEvents}）");
                var log = string.Join("\n", Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(x => x).TakeLast(1).SelectMany(File.ReadLines).Where(l => l.Contains("\ttest1\t") && string.CompareOrdinal(l[..8], _since) >= 0));
                Check(log.Contains($"Rg S|{_skill}|0|"), $"服务端收到技能指令 Rg S|{_skill}|0|目标");
                if (_skill == 2) Check(b.EffectsPlayed > 0, $"陨石播了官方特效动画（{b.EffectsPlayed} 次）");
                Check(log.Contains("Rg W|0|"), "服务端收到宠物指令 Rg W|0|目标");
                Check(log.Contains("SKL|s1C84") || log.Contains("SKL|s190") || log.Contains("CMB"), "服务端回了技能/宠物的动作");
                Shot("4_after"); return End();
        }
        return false;
    }
    bool _mid; int _midF;
    readonly int _skill = int.TryParse(System.Environment.GetEnvironmentVariable("SKILL_IDX"), out var k) ? k : 1;
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
}
