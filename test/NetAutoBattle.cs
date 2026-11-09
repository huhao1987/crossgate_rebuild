using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>自动战斗：「選項」窗口勾选（原定 / 智能互斥）→ 开「自動」后整场不用手动 → 勾"遇到Lv1魔族时"再遇敌，开场就自动关掉。</summary>
public partial class NetAutoBattle : SceneTree
{
    int _f, _st, _battles; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_auto_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    void Walk(NetworkSession s) { if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6); }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/dolua gm_heal.lua"); s.Say("/warp 100 0 360 170"); _st = 1; _f = 0; break;
            case 1 when _f > 60 && !s!.InBattle: Walk(s); if (_f > 6000) { Check(false, "遇敌"); return End(); } break;
            case 1 when s!.InBattle: _st = 2; _f = 0; break;
            case 2 when _f == 40:
                b.OptionsPressed!(); _st = 3; _f = 0; break;
            case 3 when _f == 15:
                Check(_m.AutoOptsUi.Visible && _m.Auto.Options == AutoBattle.Default, $"選項窗口（选项 0x{_m.Auto.Options:X}）");
                Shot("1_options");
                _m.AutoOptsUi.Flip(0); bool a = _m.Auto.Options == 0x41; _m.AutoOptsUi.Flip(1); Check(a && _m.Auto.Options == 0x42, "原定目标 / 智能选择 只能选一个");
                _m.AutoOptsUi.Visible = false;
                b.SetAuto(true); _st = 4; _f = 0; break;
            case 4 when !s!.InBattle && !b.Visible && _f > 30:
                Check(true, $"开「自動」后这场自己打完了（{_f / 60} 秒）");
                _m.AutoOptsUi.Flip(5); _st = 5; _f = 0; break;
            case 4 when _f > 60 * 120: Check(false, "自动战斗没打完"); return End();
            case 5 when _f > 60 && !s!.InBattle: Walk(s); if (_f > 6000) { Check(false, "第二次遇敌"); return End(); } break;
            case 5 when s!.InBattle: _st = 6; _f = 0; break;
            case 6 when _f == 120:
            {
                bool lv1 = s!.CurrentBattle.Units.Any(u => u.Side == 1 && u.Level == 1);
                Check(lv1 ? !b.AutoOn : b.AutoOn, $"勾了「遇到Lv1魔族时」：敌人有 Lv1 {lv1} → 自动 {b.AutoOn}");
                _m.AutoOptsUi.Flip(5); b.SetAuto(true); _st = 7; _f = 0; break;
            }
            case 7 when !s!.InBattle && !b.Visible && _f > 30:
                b.SetAuto(false); return End();
            case 7 when _f > 60 * 120: Check(false, "第二场没打完"); return End();
        }
        return _f > 60 * 200;
    }
}
