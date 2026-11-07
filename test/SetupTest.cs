using CgPort.Core.Assets;
using Godot;

namespace CgPort.Godot.Tests;

/// <summary>没有配置时进入首次设置界面；自动识别能找到占位素材的资源组和服务端目录。</summary>
public partial class SetupTest : SceneTree
{
    private int _frame;
    public override void _Initialize()
    {
        var cfg = ProjectSettings.GlobalizePath("user://config.json");
        if (File.Exists(cfg)) File.Delete(cfg);
        Root.AddChild(GD.Load<PackedScene>("res://client/main.tscn").Instantiate());
    }
    public override bool _Process(double delta)
    {
        if (++_frame < 5) return false;
        bool setup = Root.GetChild(0).GetChildren().Any(c => c is SetupScreen);
        GD.Print((setup ? "PASS" : "FAIL") + " 没有配置时显示首次设置界面");
        var r = AssetDetector.Detect(ProjectSettings.GlobalizePath("res://testdata"));
        bool ok = r.Sets.Count == 1 && r.Sets[0].Name == "TEST" && r.Sets[0].AnimeHeader == 12 && r.ServerDir == "_server" && r.Palette != null;
        GD.Print((ok ? "PASS" : "FAIL") + $" 自动识别占位素材：{r.Sets.Count} 组，服务端 {r.ServerDir}");
        var json = GameConfig.BuildJson("/x", r, 1);
        var back = GameConfig.Parse(json);
        GD.Print((back.Sets.Count == 1 && back.ServerDir == "_server" && back.StartMap == 1 ? "PASS" : "FAIL") + " 生成的配置能被读回");
        return true;
    }
}
