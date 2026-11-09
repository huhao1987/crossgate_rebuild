using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>聊天设定：行数 +1、颜色 绿→红、范围 5→4 → 说话包带颜色 6 范围 4；我的口頭禪 F1 说出第一条；绘图设定 隐藏 4 档。</summary>
public partial class NetChatCfg : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_chatcfg_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    static string Log() => string.Join("\n", Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(x => x).TakeLast(1).SelectMany(p => CgPort.Core.Text.Gbk.Decode(File.ReadAllBytes(p)).Split('\n')).Where(l => l.Contains("\ttest1\tuSr")).TakeLast(5));
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
        var sys = _m.SystemWin;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                sys.Visible = true; sys.Open(1); _st = 1; _f = 0; break;
            case 1 when _f == 20:
                Shot("1_page");
                sys.TestChat("lines+"); sys.TestChat("color"); sys.TestChat("range-");
                _st = 2; _f = 0; break;
            case 2 when _f == 20:
                Shot("2_changed");
                Check(_m.Chat.MaxLines == 3, $"显示行数 2 → {_m.Chat.MaxLines}");
                s!.Say("cfg test"); _st = 3; _f = 0; break;
            case 3 when _f == 60:
            {
                var log = Log();
                Check(log.Contains("P|cfg\\Stest 6 4 1"), $"说话包带颜色 6 范围 4：{log.Split('\n').LastOrDefault()}");
                sys.Visible = false;
                _m.PhrasesUi.Open(); _m.PhrasesUi.Phrases[0] = "phrase one"; _m.PhrasesUi.Refresh(); _st = 4; _f = 0; break;
            }
            case 4 when _f == 20:
                Shot("3_phrases");
                _m.PhrasesUi.Visible = false;
                Check(_m.PhrasesUi.Hotkey(Key.F1, false, false), "F1 = 第一条"); _st = 5; _f = 0; break;
            case 5 when _f == 60:
                Check(Log().Contains("P|phrase\\Sone 6 4 1"), "F1 说出了「phrase one」");
                sys.Visible = true; sys.Open(5); _st = 6; _f = 0; break;
            case 6 when _f == 20:
                Shot("4_draw");
                // 恢复默认，免得影响别的测试
                s!.ChatColor = 5; s.ChatRange = 5;
                return End();
        }
        return _f > 3000;
    }
}
