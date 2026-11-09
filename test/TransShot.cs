using Godot;
namespace CgPort.Godot.Tests;
/// <summary>过场效果截图：默认 / 仿石器各拍「拿走」和「放进来」。</summary>
public partial class TransShot : SceneTree
{
    int _f; Main _m = null!;
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = System.Text.Json.Nodes.JsonNode.Parse(GameConfig.BuildJson(root, CgPort.Core.Assets.AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "local"; o["encounters"] = false; o["load_save"] = false; o["start"] = new System.Text.Json.Nodes.JsonArray(242, 100);
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var t = _m.Trans;
        // 两种样式各拍：拿走第 20 帧、拿走快结束、放进来第 30 帧（暂停后手动步进，好对照官方）
        if (_f == 60 || _f == 140)
        {
            t.StyleOf = () => _f < 100 ? 0 : 1; t.Paused = true; t.Enter(); t.Advance(20);
        }
        int b = _f < 100 ? 60 : 140, st = _f < 100 ? 0 : 1;
        if (_f == b + 3) { Root.GetTexture().GetImage().SavePng($"res://test/out_trans{st}_out20.png"); GD.Print($"样式{st} {t.PhaseName} 帧{t.Frame}"); }
        if (_f == b + 5) { while (t.Running && t.PhaseName == "Out") t.Advance(1); GD.Print($"样式{st} 拿走结束后：{t.PhaseName}"); t.Advance(30); }
        if (_f == b + 8) { Root.GetTexture().GetImage().SavePng($"res://test/out_trans{st}_in30.png"); GD.Print($"样式{st} {t.PhaseName} 帧{t.Frame}"); }
        if (_f == b + 10) { int n = 0; while (t.Running && n < 2000) { t.Advance(1); n++; } GD.Print($"样式{st} 放进来再走 {n} 帧结束，Running={t.Running}"); }
        if (_f == 200) { File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
        return false;
    }
}
