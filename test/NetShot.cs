using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>联网登录 → GM 传送到 CG_WARP（"地图 x y"）→ 截图 out_netshot.png。</summary>
public partial class NetShot : SceneTree
{
    int _f, _st; Main _m = null!; readonly Queue<string> _press = new();
    public override void _Initialize()
    {
        var root = System.Environment.GetEnvironmentVariable("CG_ROOT") ?? "/home/claude/wine/client";   // 也可以是 .cgpak 数据包
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = System.Environment.GetEnvironmentVariable("CG_SERVERDIR") ?? "../cgmsv"; o["mode"] = System.Environment.GetEnvironmentVariable("CG_MODE") ?? "network"; o["host"] = "127.0.0.1"; o["port"] = 9030;
        o["account"] = System.Environment.GetEnvironmentVariable("CG_ACCOUNT") ?? "test2"; o["password"] = "123456"; o["load_save"] = false;
        if (System.Environment.GetEnvironmentVariable("CG_MODE") == "local" && System.Environment.GetEnvironmentVariable("CG_WARP") is { } lw)
        { var w = lw.Split(' '); o["start_map"] = int.Parse(w[0]); o["start"] = new JsonArray(int.Parse(w[1]), int.Parse(w[2])); o["encounters"] = false; if (System.Environment.GetEnvironmentVariable("CG_ANIME") is { } an) o["player_anime"] = int.Parse(an); }
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session;
        if (_st == 0 && s is NetworkSession ns0 && ns0.InGame && s.CurrentMap != null && _f > 60) { var w = (System.Environment.GetEnvironmentVariable("CG_WARP") ?? "1530 15 6").Split(' '); ns0.Say($"/warp {w[0]} 0 {w[1]} {w[2]}"); _st = 1; _f = 0; }
        if (_st == 0 && s is LocalSession && s.CurrentMap != null && _f > 30) { _st = 1; _f = 0; }   // 单机：用 CG_WARP 作出生点（见 _Initialize）
        if (_st == 1 && _f == 120 && System.Environment.GetEnvironmentVariable("CG_TALK") is { } tk)
        {
            var t = tk.Split(' ').Select(int.Parse).ToArray();
            var npc = s!.NpcsOnCurrentMap().FirstOrDefault(n => n.X == t[0] && n.Y == t[1]);
            if (npc != null) _m.TalkOrApproach(npc);
            foreach (var a in (System.Environment.GetEnvironmentVariable("CG_PRESS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) _press.Enqueue(a);
            _st = 2; _f = 0;
        }
        if (_st == 2 && _f % 90 == 89) { if (_press.Count > 0) s!.Answer(_press.Dequeue()); else { _st = 1; _f = 149; } }
        if (_st == 1 && _f == 150) { GD.Print($"NETSHOT {s!.CurrentMap?.MapId} ({s.X},{s.Y}) chat [{string.Join(" / ", _m.Chat.Lines)}]"); Root.GetTexture().GetImage().SavePng("res://test/out_netshot.png");
            var me = Root.FindChildren("*", "Sprite2D", true, false).OfType<Node2D>().FirstOrDefault(n => n.GetChildren().OfType<Camera2D>().Any());
            if (me != null)
            {
                GD.Print($"ME pos {me.GlobalPosition} vis {me.Visible} z {me.ZIndex} parent {me.GetParent().Name} idx {me.GetIndex()}/{me.GetParent().GetChildCount()} tex {(me as Sprite2D)?.Texture?.GetSize()} off {(me as Sprite2D)?.Offset} mod {me.Modulate}");
                foreach (var sp in me.GetParent().GetChildren().OfType<Sprite2D>())
                {
                    if (sp == me || sp.Texture == null) continue;
                    var r = new Rect2(sp.GlobalPosition + sp.Offset, sp.Texture.GetSize());
                    if (r.HasPoint(me.GlobalPosition + new Vector2(0, -30)) && sp.GlobalPosition.Y >= me.GlobalPosition.Y) GD.Print($"COVER y {sp.GlobalPosition.Y} rect {r}");
                }
            } File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
        return _f > 3000;
    }
}
