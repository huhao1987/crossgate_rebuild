using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>系统子设定（test1）：音樂页 ↑↓ 改音量（0–15）；绘图设定 隐藏画面角色 / NPC 名字；鍵盤、滑鼠页能打开、回上一页。</summary>
public partial class NetSys2 : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_sys2_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var sys = _m.SystemWin;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 120:
                sys.Visible = true; sys.Chosen!(2);
                Check(sys.Page == 2, "系统 → 声音设定 → 音樂页");
                Shot("1_sound");
                int v0 = _m.Cfg.BgmVolume; sys.TestLevel(true, -15);
                Check(_m.Cfg.BgmVolume == 0 && GameAudio.I?.BgmVolume == 0, $"背景音乐 ↓ 到 0（{v0} → {_m.Cfg.BgmVolume}）");
                sys.TestLevel(true, 8);
                Check(Math.Abs(_m.Cfg.BgmVolume - 53) <= 1, $"↑ 8 格 = {_m.Cfg.BgmVolume}%");
                sys.Chosen!(5);
                Check(sys.Page == 5, "绘图设定页");
                _st = 1; _f = 0; break;
            case 1 when _f == 10:
            {
                Shot("2_draw");
                int npcs = _m.VisibleNameTags();
                _m.Cfg.ShowNpcNames = false; sys.DrawChanged!();
                int after = _m.VisibleNameTags();
                Check(after < npcs, $"关掉 NPC 名字：名字标签 {npcs} → {after}");
                _m.Cfg.ShowNpcNames = true; sys.DrawChanged!();
                sys.Chosen!(4); Check(sys.Page == 4, "键盘热键页"); Shot("3_keys");
                sys.Chosen!(3); Check(sys.Page == 3, "鼠标设定页");
                sys.Open(-1); Check(sys.Page == -1, "回上一页");
                return End();
            }
        }
        return _f > 3000;
    }
}
