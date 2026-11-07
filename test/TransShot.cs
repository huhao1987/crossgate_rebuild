using Godot;
namespace CgPort.Godot.Tests;
/// <summary>过场效果截图：进战斗（洞往外扩）和出战斗（黑屏后开洞）各拍 4 张。</summary>
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
        // 进战斗：拍地图当"旧画面"，然后把战斗区域换成一块纯色（看得出洞），停在 0.1/0.3/0.5/0.7
        if (_f == 60) { _m.Trans.Enter(); _m.Trans.Freeze(0.05f); }
        float[] ps = { 0.1f, 0.3f, 0.5f, 0.7f };
        for (int i = 0; i < 4; i++)
        {
            if (_f == 62 + i * 6) _m.Trans.Freeze(ps[i]);
            if (_f == 65 + i * 6) Root.GetTexture().GetImage().SavePng($"res://test/out_trans_in_{i}.png");
        }
        if (_f == 100) { File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
        return false;
    }
}
