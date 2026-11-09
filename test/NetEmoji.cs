using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>聊天表情：说 "hi %A%B%c %z" → 聊天栏画 3 个表情，%z（第 52 个，图里只有 36 个）原样显示（官方 bob 实测同）。</summary>
public partial class NetEmoji : SceneTree
{
    int _f, _st, _map0; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
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
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 120:
                s.Say("hi %A%B%c %z"); _st = 1; _f = 0; break;
            case 1 when _f == 60:
                Check(_m.Chat.Lines.Any(l => l.Contains("%A%B%c")), $"聊天栏 [{string.Join(" / ", _m.Chat.Lines)}]");
                Root.GetTexture().GetImage().SavePng("res://test/out_emoji.png");
                return End();
        }
        return _f > 3000;
    }
}
