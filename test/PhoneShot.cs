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
            o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false; o["ui_mode"] = "mobile";
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
        if (_f == 410)
        {
            // 模拟手指点底部菜单「系统」（画布 1200×540，第 8 格 x 560–640，y 516–540）
            var vp = Root.GetViewport();
            var pos = vp.GetScreenTransform() * new Vector2(600, 530);
            foreach (var pressed in new[] { true, false })
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = pos, GlobalPosition = pos, Device = (int)InputEvent.DeviceIdEmulation });
        }
        if (_f == 420) { var h = Root.GetViewport().GuiGetHoveredControl(); for (Node? n = h; n != null; n = n.GetParent()) GD.Print("HOVER " + n.GetType().Name + " " + (n is Control c ? c.GetGlobalRect() + " " + c.MouseFilter : n is CanvasLayer l ? "layer " + l.Layer : "")); }
        if (_f == 430) GD.Print((_m!.ChatIn.Typing ? "FAIL" : "PASS") + " 点底部菜单不会弹聊天；" + (_m.SystemWin.Visible ? "PASS" : "FAIL") + " 系统窗口打开了");
        if (_f == 440) { Root.GetTexture().GetImage().SavePng("/tmp/phone_sys.png"); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
        return false;
    }
}
