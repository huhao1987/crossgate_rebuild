using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>研究用：动画 CG_ANIME 方向 CG_DIR 的每个动作，取若干帧拼一行，存 /tmp/acts_编号.png（每行一个动作）。</summary>
public partial class AnimActs : SceneTree
{
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var cfg = GameConfig.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000));
        var cache = new TextureCache(new AssetLibrary(cfg.GameRoot, cfg.Sets, cfg.Palette));
        int id = int.Parse(System.Environment.GetEnvironmentVariable("CG_ANIME") ?? "101800");
        int dir = int.Parse(System.Environment.GetEnvironmentVariable("CG_DIR") ?? "5");
        var acts = cache.Library.AnimeActions(id).Where(k => k.Key.Dir == dir).OrderBy(k => k.Key.Act).ToList();
        int cw = 140, ch = 130, cols = 8;
        var outImg = Image.CreateEmpty(cw * cols, ch * acts.Count, false, Image.Format.Rgba8);
        outImg.Fill(new Color(0.5f, 0.5f, 0.5f));
        for (int r = 0; r < acts.Count; r++)
        {
            var a = acts[r].Value;
            for (int c = 0; c < cols && a.Frames.Length > 0; c++)
            {
                int fi = c * a.Frames.Length / cols;
                if (a.Set == null || !a.Set.TryLocal(a.Frames[fi], out var e) || a.Set.Decode(e) is not { } img) continue;
                var im = Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.ToRgba(cache.Library.Palette));
                int x = c * cw + 70 + e.OffsetX, y = r * ch + 110 + e.OffsetY;
                outImg.BlendRect(im, new Rect2I(0, 0, img.Width, img.Height), new Vector2I(Math.Clamp(x, 0, outImg.GetWidth() - 1), Math.Clamp(y, 0, outImg.GetHeight() - 1)));
            }
            GD.Print($"ROW {r} act {a.Act} frames {a.Frames.Length} ms {a.DurationMs} sounds {string.Join(",", a.Sounds.Select((v, i) => (v, i)).Where(t => t.v != 0).Select(t => $"{t.i}:{t.v}"))}");
        }
        outImg.SavePng($"/tmp/acts_{id}.png");
        Quit();
    }
}
