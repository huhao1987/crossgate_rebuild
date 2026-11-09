using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 声音（test1）：法兰城物件层的音乐标记 201 → 放 cgbgm_m1；走到喷水池环境音标记旁边 → 流水声；
/// 到芙蕾雅遇敌 → 战斗音乐 205；攻击 → 动画帧上的音效号被播放；战斗结束 → 回到地图音乐。
/// （云端没有声卡，用的是 Godot 的空驱动，只检查放没放、放的是哪个。）
/// </summary>
public partial class NetAudio : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new(); (int Id, int X, int Y) _mark;
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var a = _m.Audio; var b = _m.Battle;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/dolua gm_heal.lua"); s.Say("/warp 1000 0 242 100"); _st = 1; _f = 0; break;
            case 1 when _f == 120:
                Check(a != null && a.MapBgm == 201 && a.PlayingBgm == 201 && a.BgmPlaying, $"法兰城音乐：标记 {a?.MapBgm}，在放 {a?.PlayingBgm}（{a?.BgmPlaying}）");
                Check(a!.EnvMarks.Count > 0, $"环境音标记 {a.EnvMarks.Count} 个：{string.Join(" ", a.EnvMarks.Select(m => $"{m.Id}@{m.X},{m.Y}"))}");
                if (a.EnvMarks.Count == 0) { _st = 3; _f = 0; break; }
                _mark = a.EnvMarks[0];
                s!.Say($"/warp 1000 0 {_mark.X} {_mark.Y + 2}"); _st = 2; _f = 0; break;
            case 2 when _f == 90:
            {
                var file = SeTable.All.GetValueOrDefault(_mark.Id).File;
                bool has = File.Exists($"/home/claude/wine/client/audio/se/{file}.ogg") || File.Exists($"/home/claude/wine/client/bin/se/{file}.wav");
                Check(a!.EnvPlaying(_mark.Id) == has, $"走到 ({s!.X},{s.Y})，环境音 {_mark.Id}（{file}，客户端{(has ? "有" : "没有")}这个文件）在放：{a.EnvPlaying(_mark.Id)}");
            }
                _st = 3; _f = 0; break;
            case 3 when _f == 10:
                s!.Say("/warp 100 0 370 180"); _st = 4; _f = 0; break;
            case 4 when _f > 60 && !s!.InBattle:
                if (_f % 20 == 0) s.Step(_f / 20 % 2 == 0 ? 2 : 6);
                if (_f > 6000) { Check(false, "遇敌"); return End(); }
                break;
            case 4 when s!.InBattle: _st = 5; _f = 0; break;
            case 5 when _f > 30 && !b.Busy && s!.CommandSlot == 0:
                Check(a!.PlayingBgm == 205 && a.BgmPlaying && a.MapBgm == 210, $"战斗音乐：在放 {a.PlayingBgm}（芙蕾雅的地图音乐标记 {a.MapBgm}）");
                a.PlayedSe.Clear();
                b.TestPressAttack(); _st = 50; _f = 0; break;
            case 50 when _f == 10:
                b.TestPickTarget(s!.CurrentBattle.Units.First(u => u.Side == 1 && u.Alive).Id); _st = 6; _f = 0; break;
            // 宠物也要下指令（宠物在战斗栏时）：每 40 帧点一次宠物第 0 招 + 选敌人，直到动画播出来
            case 6 when s!.PetTurn && !b.Busy && _f % 40 == 11 && b.AttackAnimsShown == 0:
                b.TestClickSub(0); break;
            case 6 when s!.PetTurn && !b.Busy && _f % 40 == 22 && b.AttackAnimsShown == 0:
                { var en = s.CurrentBattle.Units.FirstOrDefault(u => u.Side == 1 && u.Alive); if (en != null) b.TestPickTarget(en.Id); }
                break;
            case 6 when b.AttackAnimsShown > 0 && _f > 200 || _f == 1500:
                Check(a!.PlayedSe.Count > 0, $"（事件 {b.PlayedEvents}，攻击动作 {b.AttackAnimsShown}）攻击动画放了音效：{string.Join(",", a.PlayedSe.Distinct())}（文件 {string.Join(",", a.PlayedSe.Distinct().Select(i => SeTable.All.GetValueOrDefault(i).File))}）");
                _st = 7; _f = 0; break;
            case 7 when !s!.InBattle && !b.Visible || _f > 3000:
                if (s!.InBattle) { if (_f % 200 == 1 && s.CommandSlot == 0 && !b.Busy) { b.TestPressAttack(); var e = s.CurrentBattle.Units.FirstOrDefault(u => u.Side == 1 && u.Alive); if (e != null) b.TestPickTarget(e.Id); } break; }
                _st = 8; _f = 0; break;
            case 7 when s!.InBattle && _f % 100 == 1 && !b.Busy:
                if (s.CommandSlot >= 0) { b.TestPressAttack(); var e = s.CurrentBattle.Units.FirstOrDefault(u => u.Side == 1 && u.Alive); if (e != null) b.TestPickTarget(e.Id); }
                break;
            case 8 when _f == 60:
                Check(a!.PlayingBgm == a.MapBgm && a.BgmPlaying, $"战斗结束回到地图音乐：{a.PlayingBgm}（地图 {s!.CurrentMap?.MapId} 的标记 {a.MapBgm}）");
                s!.Say("/warp 1000 0 242 100");
                return End();
        }
        return _f > 8000;
    }
}
