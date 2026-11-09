using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>系统窗口：底部菜单"系统"打开 → 截图 → 登出（服务端收到 WA）→ 回到设置界面。</summary>
public partial class NetSys : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new(); string _since = "";
    void Check(bool ok, string n) => _r.Add((ok ? "PASS " : "FAIL ") + n);
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        _since = DateTime.UtcNow.ToString("HH:mm:ss");
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                _m.HudSkin.MenuPressed!(2); _st = 10; _f = 0; break;
            case 10 when _f == 15:
                Check(_m.SkillWin.Visible && _m.SkillWin.Detail == -1, "技能列表打开");
                Root.GetTexture().GetImage().SavePng("res://test/out_skills.png");
                _m.SkillWin.ShowDetail(1); _st = 11; _f = 0; break;
            case 11 when _f == 15:
                Root.GetTexture().GetImage().SavePng("res://test/out_skill_detail.png");
                _m.SkillWin.Toggle(); _m.MapWin.ShowAll = System.Environment.GetEnvironmentVariable("CG_MAPALL") != null; if (System.Environment.GetEnvironmentVariable("CG_MAPK") is { } mk) _m.MapWin.K = float.Parse(mk); _m.HudSkin.MenuPressed!(0); _st = 12; _f = 0; break;
            case 12 when _f == 20:
                Check(_m.MapWin.Visible, "地图窗口打开");
                Root.GetTexture().GetImage().SavePng("res://test/out_map.png");
                _m.MapWin.Toggle(); _m.HudSkin.MenuPressed!(7); _st = 1; _f = 0; break;
            case 1 when _f == 15:
                Check(_m.SystemWin.Visible, "系统窗口打开");
                Root.GetTexture().GetImage().SavePng("res://test/out_system.png");
                _m.SystemWin.Chosen!(0); _st = 2; _f = 0; break;
            case 2 when _f == 60:
                var log = string.Join("\n", Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(x => x).TakeLast(1).SelectMany(File.ReadLines).Where(l => l.Contains("\ttest1\t") && string.CompareOrdinal(l[..8], _since) >= 0));
                Check(log.Contains("[Recv]") && log.Split('\n').Any(l => l.Contains("[Recv]") && l.TrimEnd().EndsWith("WA")), "服务端收到登出 WA");
                Check(!s!.InGame && _m.GetChildren().OfType<SetupScreen>().Any(), "回到设置界面");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 3000;
    }
}
