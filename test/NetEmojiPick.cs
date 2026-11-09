using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>手机模式的表情面板：点「表情」→ 36 个表情 → 点第 2 个插入 %B → 发送 → 聊天栏显示表情。</summary>
public partial class NetEmojiPick : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false; o["ui_mode"] = "mobile";
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
                _m.ChatIn.ToggleEmoji(); _st = 1; _f = 0; break;
            case 1 when _f == 15:
                Check(_m.ChatIn.EmojiOpen, "点「表情」→ 表情面板");
                Root.GetTexture().GetImage().SavePng("res://test/out_emoji_pick.png");
                _m.ChatIn.InsertEmoji('B'); _m.ChatIn.Send?.Invoke("hi %B"); _m.ChatIn.ToggleEmoji(); _st = 2; _f = 0; break;
            case 2 when _f == 60:
                Check(_m.Chat.Lines.Any(l => l.Contains("%B")), $"聊天栏：{_m.Chat.Lines.LastOrDefault()}");
                return End();
        }
        return _f > 2000;
    }
}
