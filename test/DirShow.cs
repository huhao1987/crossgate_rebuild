using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>研究用：把人物形象 106002 的 8 个方向并排画出来（out_dirs.png）。</summary>
public partial class DirShow : SceneTree
{
    int _f;
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var cfg = GameConfig.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000));
        var cache = new TextureCache(new AssetLibrary(cfg.GameRoot, cfg.Sets, cfg.Palette));
        for (int d = 0; d < 8; d++)
        {
            var n = new Node2D { Position = new Vector2(60 + d * 100, 150) };
            n.AddChild(new AnimSprite { Cache = cache, AnimeId = int.Parse(System.Environment.GetEnvironmentVariable("CG_ANIME") ?? "106002"), Act = 0, FallbackAct = 0, Dir = d });
            var l = new Label { Text = d.ToString(), Position = new Vector2(-4, 10) }; n.AddChild(l);
            Root.AddChild(n);
        }
    }
    public override bool _Process(double d) { if (++_f == 20) { Root.GetTexture().GetImage().SavePng("res://test/out_dirs.png"); return true; } return false; }
}
