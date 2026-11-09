using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>战斗录像：遇敌 → 战斗里点「本局錄像」→ 打完存档 + 聊天"战斗录制成功" → 系统 → 战斗录像 → 列表 → 播放 → 播完回地图。</summary>
public partial class NetVideo : SceneTree
{
    int _f, _st, _n0, _lastSlot = -9; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_video_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
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
        var s = _m.Session as NetworkSession; var b = _m.Battle;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                _n0 = VideoStore.List().Count;
                s.Say("/dolua gm_heal.lua"); s.Say("/warp 100 0 370 180"); _st = 1; _f = 0; break;
            case 1 when _f > 60 && !s!.InBattle:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 6000) { Check(false, "遇敌"); return End(); }
                break;
            case 1 when s!.InBattle: _st = 2; _f = 0; break;
            case 2 when _f == 60:
                Check(b.Visible, "进战斗");
                b.SetRecording(true); _st = 3; _f = 0; break;
            case 3 when _f == 10:
                Shot("1_battle"); _st = 4; _f = 0; break;
            case 4:
                // 自己打：每次要指令就普攻第一个活着的敌人
                if (s!.InBattle && !b.Busy && s.CommandSlot >= 0 && s.CommandSlot != _lastSlot)
                {
                    _lastSlot = s.CommandSlot;
                    var e = s.CurrentBattle.Units.FirstOrDefault(u => u.Side == 1 && u.Alive);
                    if (e != null) s.SubmitCommand(new Command(CommandKind.Attack, e.Id));
                }
                if (b.Busy) _lastSlot = -9;
                if (!s.InBattle && !b.Visible && _f > 60) { _st = 5; _f = 0; }
                if (_f > 60 * 120) { Check(false, "战斗没打完"); return End(); }
                break;
            case 5 when _f == 60:
            {
                var list = VideoStore.List();
                Check(list.Count == _n0 + 1, $"存了录像（{_n0} → {list.Count}，{list.LastOrDefault()?.File} {list.LastOrDefault()?.Map} {list.LastOrDefault()?.Recorder}）");
                Check(_m.Chat.Lines.Any(l => l.Contains("战斗录制成功")), $"聊天：{_m.Chat.Lines.LastOrDefault(l => l.Contains("战斗"))}");
                _m.SystemWin.Visible = true; _m.SystemWin.Chosen!(6); _st = 6; _f = 0; break;
            }
            case 6 when _f == 20:
                Check(_m.VideoListUi.Visible && _m.VideoListUi.Count == _n0 + 1, $"战斗录像列表 {_m.VideoListUi.Count} 个");
                Shot("2_list");
                _m.VideoListUi.TestPlay(_m.VideoListUi.Count - 1); _st = 7; _f = 0; break;
            case 7 when _f == 90:
                Check(_m.Replaying && _m.ReplayUi.Visible, "播放录像（只读战斗画面）");
                Shot("3_replay"); break;
            case 7 when _f > 90 && !_m.Replaying:
                Check(!_m.ReplayUi.Visible && !s!.InBattle, $"播完回到地图（{_f / 60} 秒）");
                return End();
            case 7 when _f > 60 * 120:
                Check(false, "录像没播完"); return End();
        }
        return _f > 60 * 200;
    }
}
