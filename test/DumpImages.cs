using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>研究用：把图像编号 CG_RANGE（如 "22640-22760"）逐个存成 PNG 到 CG_OUT 目录（文件名 = 编号_宽x高.png）。</summary>
public partial class DumpImages : SceneTree
{
    public override void _Initialize()
    {
        var root = System.Environment.GetEnvironmentVariable("CG_ROOT") ?? "/home/claude/wine/client";
        var cfg = GameConfig.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000));
        var cache = new TextureCache(new AssetLibrary(cfg.GameRoot, cfg.Sets, cfg.Palette));
        var r = (System.Environment.GetEnvironmentVariable("CG_RANGE") ?? "22640-22760").Split('-').Select(int.Parse).ToArray();
        var outDir = System.Environment.GetEnvironmentVariable("CG_OUT") ?? "/tmp/dump";
        System.IO.Directory.CreateDirectory(outDir);
        int n = 0;
        // CG_FILTER="70-90x16-26"：只要宽 70–90、高 16–26 的
        int[]? filter = System.Environment.GetEnvironmentVariable("CG_FILTER") is { } fs ? fs.Split('x').SelectMany(p => p.Split('-')).Select(int.Parse).ToArray() : null;
        for (int id = r[0]; id <= r[1]; id++)
        {
            if (!cache.Library.Graphics.TryByMapId(id, out _, out var e0)) continue;
            if (filter != null && (e0.Width < filter[0] || e0.Width > filter[1] || e0.Height < filter[2] || e0.Height > filter[3])) continue;
            var (t, e) = cache.ImageNo(id);
            if (t == null || e == null) continue;
            var im = t.GetImage(); im.SavePng($"{outDir}/{id}_{im.GetWidth()}x{im.GetHeight()}.png"); n++;
        }
        GD.Print($"DUMPED {n}");
        Quit();
    }
}
