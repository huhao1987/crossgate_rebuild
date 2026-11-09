using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>右上「对战」「观战」（test1 hao 对着 test3）：对战 → ik x y，对方没开「戰」→ dAgg 0 0（不进战斗）+ 突发战斗失败；观战 → IMv x y → 本区域不允许观战。</summary>
public partial class NetDuel : SceneTree
{
    int _f, _st, _chat0; Main _m = null!; NetworkSession _b = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { try { _b.Dispose(); } catch { } GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        var maps = CgPort.Core.MapData.IndexServerMapDir("/home/claude/wine/cgmsv/data/map");
        _b = new NetworkSession(new NetworkOptions { Host = "127.0.0.1", Port = 9030, CdKey = "test3", Account = "test3", Password = "123456", NewCharName = "nettest3", ResolveMap = f => maps.TryGetValue(f, out var m) ? m.Path : null });
        _b.Start();
    }
    public override bool _Process(double d)
    {
        _f++; _b.Poll();
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _b.InGame && _b.CurrentMap != null && _f > 90:
                if ((_b.Flags & 4) != 0) _b.ToggleFlag(4);   // test3 关「戰」
                s.Say($"/warp {_b.CurrentMap.MapId} 0 {_b.X - 1} {_b.Y}"); _st = 1; _f = 0; break;
            case 1 when _f > 90 && s!.X == _b.X - 1:
                s.Face(1, 0); _chat0 = _m.Chat.Lines.Count;
                _m.HudSkin.TestPressButton(22581); _st = 2; _f = 0; break;
            case 2 when _m.Chat.Lines.TakeLast(3).Any(l => l.Contains("突发战斗失败")) || _f > 200:
                Check(_m.Chat.Lines.TakeLast(3).Any(l => l.Contains("突发战斗失败")) && !s!.InBattle && !_m.Battle.Visible, $"对战 → 对方没开戰：突发战斗失败，没进战斗（InBattle {s!.InBattle}）");
                _chat0 = _m.Chat.Lines.Count;
                _m.HudSkin.TestPressButton(22583); _st = 3; _f = 0; break;
            case 3 when _f == 120:
                // 面前的人没在战斗：服务端回 dAgg 0 0（不进战斗）；在城里有人战斗时回"本区域不允许观战。"（官方 bob 实测）
                Check(!s!.InBattle && !_m.Battle.Visible, $"观战 → 面前没人在战斗：没进战斗（最后几行：{string.Join(" / ", _m.Chat.Lines.TakeLast(2))}）");
                return End();
        }
        return _f > 3000;
    }
}
