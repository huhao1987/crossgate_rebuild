using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>研究用：剑士公会会长「我想转职」「我想提升阶级」各走一遍，打印每页对话。</summary>
public partial class NetJobX : SceneTree
{
    int _f, _st, _retry; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_jobx_{n}.png");
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    void TalkTo(NetworkSession s, string name, int x, int y)
    {
        _page = null;
        var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == name && k.X == x && k.Y == y);
        if (n == null) return;
        if (Math.Max(Math.Abs(n.X - s.X), Math.Abs(n.Y - s.Y)) <= 1 && (n.X, n.Y) != (s.X, s.Y)) { s.TalkTo(n.Id); return; }
        _retry++;
        var (dx, dy) = new[] { (0, 1), (1, 1), (-1, 1), (1, 0), (-1, 0) }[_retry % 5];
        s.Say($"/warp 777 0 {x + dx} {y + dy}");
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) { _page = p; GD.Print($"PAGE text=[{p.Text.Replace("\n","/")}] opts=[{string.Join("/", p.Options ?? new List<string>())}] btn={string.Join(",", p.Buttons)}"); } }; }
        var seq = (System.Environment.GetEnvironmentVariable("JOBX") ?? "opt:1").Split(';');
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                GD.Print($"JOB {s.Player.Job}");
                foreach (var c in (System.Environment.GetEnvironmentVariable("JOBX_SAY") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)) s.Say(c);
                s.Say("/warp 777 0 47 1"); _st = 1; _f = 0; break;
            case 1 when _f % 60 == 0 && _page == null:
                TalkTo(s!, "剑士公会会长", 47, 0); if (_f > 600) return true; break;
            case 1 when _page != null:
                _st = 2; _f = 0; break;
            case >= 2 when _f == 40:
            {
                int i = _st - 2;
                Shot($"{i}");
                if (i >= seq.Length || _page == null) { GD.Print($"END JOB {s!.Player.Job}"); return true; }
                var p = _page; _page = null;
                GD.Print($"ANSWER {seq[i]}");
                s!.Answer(seq[i]); _st++; _f = 0; break;
            }
        }
        return _f > 2000;
    }
}
