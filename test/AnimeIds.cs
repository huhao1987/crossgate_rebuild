using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
public partial class AnimeIds : SceneTree
{
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var cfg = GameConfig.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000));
        var lib = new AssetLibrary(cfg.GameRoot, cfg.Sets, cfg.Palette);
        var r = (System.Environment.GetEnvironmentVariable("CG_RANGE") ?? "105000-105060").Split('-').Select(int.Parse).ToArray();
        var ok = new List<int>();
        for (int id = r[0]; id <= r[1]; id++) if (lib.AnimeActions(id).Count > 0) ok.Add(id);
        GD.Print("ANIME " + string.Join(",", ok));
        Quit();
    }
}
