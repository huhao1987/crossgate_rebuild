using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>工具：把 CG_IDS 里每个动画（第一个动作）抽 6 帧拼成一条，存到 /tmp/fx/编号.png，用来对官方截图找特效编号。</summary>
public partial class FxSheet : SceneTree
{
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var cfg = GameConfig.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000));
        var lib = new AssetLibrary(cfg.GameRoot, cfg.Sets, cfg.Palette);
        System.IO.Directory.CreateDirectory("/tmp/fx");
        foreach (var s in (System.Environment.GetEnvironmentVariable("CG_IDS") ?? "110001").Split(','))
        {
            var r = s.Split('-').Select(int.Parse).ToArray();
            for (int id = r[0]; id <= r[^1]; id++)
            {
                var acts = lib.AnimeActions(id);
                if (acts.Count == 0) continue;
                var a = acts.OrderBy(k => k.Key.Act).ThenBy(k => k.Key.Dir).First().Value;
                int n = a.Frames.Length, cols = Math.Min(6, n);
                const int C = 160;
                var strip = Image.CreateEmpty(C * cols, C, false, Image.Format.Rgba8);
                strip.Fill(new Color(0.15f, 0.3f, 0.2f));
                for (int k = 0; k < cols; k++)
                {
                    int f = a.Frames[cols == 1 ? 0 : k * (n - 1) / (cols - 1)];
                    GraphicArchive? set = null; GraphicEntry e = default!;
                    bool ok = a.Set != null && a.Set.TryLocal(f, out e) ? (set = a.Set) != null : lib.Graphics.TryById(f, out set, out e);
                    if (!ok || set == null) continue;
                    var img = set.Decode(e); if (img == null) continue;
                    var px = Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.ToRgba(System.Environment.GetEnvironmentVariable("CG_PAL") is { } pn ? lib.PaletteFor(int.Parse(pn)) : lib.Palette));
                    if (img.Width > C || img.Height > C) { float sc = Math.Min((float)C / img.Width, (float)C / img.Height); px.Resize(Math.Max(1, (int)(img.Width * sc)), Math.Max(1, (int)(img.Height * sc))); }
                    strip.BlendRect(px, new Rect2I(0, 0, px.GetWidth(), px.GetHeight()), new Vector2I(k * C + (C - px.GetWidth()) / 2, (C - px.GetHeight()) / 2));
                }
                strip.SavePng($"/tmp/fx/{id}_{System.Environment.GetEnvironmentVariable("CG_PAL")}.png");
                GD.Print($"FX {id} frames={n} dur={a.DurationMs} acts={acts.Count}");
            }
        }
        Quit();
    }
}
