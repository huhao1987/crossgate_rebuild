using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>联网聊天输入：打字（绿色显示）→ 回车 → 服务端转发回来出现在聊天栏。</summary>
public partial class NetChat : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) => _r.Add((ok ? "PASS " : "FAIL ") + n);
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
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                _m.ChatIn.TestType("你好abc 123"); _st = 1; _f = 0; break;
            case 1 when _f == 10:
                Check(_m.ChatIn.Typing && _m.ChatIn.Shown == "你好abc 123", $"打字中，左下角显示：{_m.ChatIn.Shown}");
                Root.GetTexture().GetImage().SavePng("res://test/out_chat_typing.png");
                _m.ChatIn.TestEnter(); _st = 2; _f = 0; break;
            case 2 when _f == 60:
                var lines = _m.Chat.Lines;
                Check(!_m.ChatIn.Typing && _m.ChatIn.Shown == "", "回车后输入清空");
                Check(lines.Any(l => l.Contains("你好abc 123")), $"服务端转发回来，聊天栏：{string.Join(" / ", lines)}");
                Root.GetTexture().GetImage().SavePng("res://test/out_chat_sent.png");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 3000;
    }
}
