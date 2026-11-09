using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 「動作」窗口（test1 hao + test3 nettest3 在旁边看）：右上「動作」→ 窗口 → 点坐下 → test3 收到 NI …|11 → 点挥手 → 12；
/// test3 做"高兴"（MIS 13）→ hao 这边 nettest3 的精灵播动作 13。
/// </summary>
public partial class NetEmote : SceneTree
{
    int _f, _st; Main _m = null!; NetworkSession _b = null!; readonly List<string> _r = new();
    readonly List<(int Id, int Act)> _seen = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_emote_{n}.png");
    bool End() { try { _b.Dispose(); } catch { } GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        var maps = CgPort.Core.MapData.IndexServerMapDir("/home/claude/wine/cgmsv/data/map");
        _b = new NetworkSession(new NetworkOptions { Host = "127.0.0.1", Port = 9030, CdKey = "test3", Account = "test3", Password = "123456", NewCharName = "nettest3", ResolveMap = f => maps.TryGetValue(f, out var m) ? m.Path : null });
        _b.ObjectActed += (id, a) => _seen.Add((id, a));
        _b.Start();
    }
    public override bool _Process(double d)
    {
        _f++; _b.Poll();
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _b.InGame && _b.CurrentMap != null && _f > 90:
                s.Say($"/warp {_b.CurrentMap.MapId} 0 {_b.X - 1} {_b.Y}"); _st = 1; _f = 0; break;
            case 1 when _f > 90 && s!.X == _b.X - 1:
                _m.HudSkin.TestPressButton(22591);
                Check(_m.EmoteUi?.Visible == true, "右上「動作」→ 动作窗口");
                Shot("1_window");
                _m.EmoteUi!.TestChoose(11); _st = 2; _f = 0; break;
            case 1 when _f > 600: Check(false, $"没站到 test3 旁边：({s!.X},{s.Y}) vs ({_b.X},{_b.Y})"); return End();
            case 2 when _seen.Any(x => x.Act == 11) || _f > 200:
                Check(_seen.Any(x => x.Act == 11), $"坐下 → 旁人收到动作 11（{string.Join(",", _seen.Select(x => x.Act))}）");
                Shot("2_sit");
                _m.EmoteUi!.TestChoose(12); _st = 3; _f = 0; break;
            case 3 when _seen.Any(x => x.Act == 12) || _f > 200:
                Check(_seen.Any(x => x.Act == 12), "挥手 → 12");
                _b.Emote(13); _st = 4; _f = 0; break;
            case 4 when _f == 15:
            {
                var who = s!.NpcsOnCurrentMap().FirstOrDefault(n => n.Name == _b.Player.Name);
                var shown = who != null ? _m.SpriteOf(who.Id) : null;
                Check(shown?.ShownAct == 13, $"test3 做高兴 → hao 这边它的精灵播动作 {shown?.ShownAct}");
                Shot("3_other");
                _m.EmoteUi!.TestChoose(31);
                return End();
            }
        }
        return _f > 5000;
    }
}
