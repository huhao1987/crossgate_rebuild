using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>研究用：动画 CG_ANIME 动作 CG_ACT（默认 0）八个方向的第一帧，存到 /tmp/dir_编号_方向.png。</summary>
public partial class AnimDirs : SceneTree
{
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var cfg = GameConfig.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000));
        var cache = new TextureCache(new AssetLibrary(cfg.GameRoot, cfg.Sets, cfg.Palette));
        int id = int.Parse(System.Environment.GetEnvironmentVariable("CG_ANIME") ?? "101800");
        int act = int.Parse(System.Environment.GetEnvironmentVariable("CG_ACT") ?? "0");
        var acts = cache.Library.AnimeActions(id);
        GD.Print("ACTS " + string.Join(" ", acts.Keys.Select(k => $"{k.Dir}/{k.Act}")));
        foreach (var ((dir, a), v) in acts)
        {
            if (a != act || v.Frames.Length == 0 || v.Set == null) continue;
            if (v.Set.TryLocal(v.Frames[0], out var e) && v.Set.Decode(e) is { } img)
                Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.ToRgba(cache.Library.Palette)).SavePng($"/tmp/dir_{id}_{dir}.png");
        }
        Quit();
    }
}
