using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>研究用：手机分辨率下截图。SHOT=setup 首次设置界面；SHOT=game 联网进游戏（手机模式）后截图，再打开系统窗口截一张。</summary>
public partial class PhoneShot : SceneTree
{
    int _f; string _mode = System.Environment.GetEnvironmentVariable("SHOT") ?? "setup"; Main? _m;
    public override void _Initialize()
    {
        var cfg = ProjectSettings.GlobalizePath("user://config.json");
        if (File.Exists(cfg)) File.Delete(cfg);
        if (_mode == "game")
        {
            var root = "/home/claude/wine/client";
            var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
            o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false; o["ui_mode"] = "mobile";
            File.WriteAllText(cfg, o.ToJsonString());
        }
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate();
        Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        if (_mode == "setup") { if (_f == 30) { Root.GetTexture().GetImage().SavePng("/tmp/phone_setup.png"); return true; } return false; }
        if (_f == 400) Root.GetTexture().GetImage().SavePng("/tmp/phone_game.png");
        // 模拟手指（ScreenTouch，跟真手机一样由引擎转成鼠标）依次点底部菜单 8 项（画布 1200×540，每项 80 宽，y 516–540；点在偏上的 520，最容易误触的位置）
        if (_f >= 410 && _f < 410 + 8 * 20 && (_f - 410) % 20 == 0)
        {
            int k = (_f - 410) / 20;
            var pos = Root.GetViewport().GetScreenTransform() * new Vector2(80 * k + 40, 518);
            Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = pos });
            Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = false, Position = pos });
        }
        if (_f >= 410 && _f < 410 + 8 * 20 && (_f - 410) % 20 == 10)
        {
            int k = (_f - 410) / 20;
            GD.Print($"{(_m!.ChatIn.Typing ? "FAIL" : "PASS")} 点菜单第 {k + 1} 项：不弹聊天");
            if (k == 7) GD.Print((_m.SystemWin.Visible ? "PASS" : "FAIL") + " 点「系统」打开了系统窗口");
        }
        if (_f == 575) Root.GetTexture().GetImage().SavePng("/tmp/phone_sys.png");
        if (_f == 580) { File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
        return false;
    }
}
