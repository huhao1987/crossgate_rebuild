using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 留言板（WN 21，555 37,46）：说话 → 留言板 → 输入"测试 a,b|c" → 留言 → 服务端重发、第一条就是自己的（能删）→ 删除 → 没了 → 不再留言只关窗口。
/// </summary>
public partial class NetBoard : SceneTree
{
    int _f, _st, _n0; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    const string Msg = "测试 a,b|c";
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_board_{n}.png");
    bool Quit() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    string Rows() => string.Join(" / ", _m.Board.Page?.Shop?.Where(e => e.Name.Length > 0).Select(e => $"{e.Name}「{e.Desc}」删{e.Count}") ?? Array.Empty<string>());
    int Mine(NetworkSession s) => _m.Board.Page?.Shop?.Count(e => e.Name == s.Player.Name && e.Desc == Msg) ?? 0;
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var w = _m.Board;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/warp 555 0 37 47"); _st = 1; _f = 0; break;
            case 1 when _f > 150 && s!.CurrentMap?.MapId == 555:
            {
                var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == "留言板");
                if (n != null) { _page = null; _m.TalkOrApproach(n); }
                _st = 2; _f = 0; break;
            }
            case 2 when _page?.Kind == "board" && _f > 30:
                _n0 = Mine(s!);
                Check(w.Visible && w.Page!.Shop!.Count == 4, $"留言板：{w.Page?.NpcName} [{string.Join(",", w.Page?.Buttons ?? new List<string>())}] {Rows()}");
                Shot("1_board"); _page = null; w.Input = Msg; w.Write(); _st = 3; _f = 0; break;
            case 2 when _f > 600: Check(false, $"留言板没出来（{_page?.Kind}）"); return Quit();
            case 3 when _f == 120:
                Check(w.Visible && Mine(s!) == _n0 + 1 && w.Page!.Shop!.First(e => e.Desc == Msg).Count != 0, $"留言后：{Rows()}");
                Shot("2_written");
                _n0 = Mine(s!);
                { int k = w.Page!.Shop!.ToList().FindIndex(e => e.Name == s!.Player.Name && e.Desc == Msg); if (k >= 0) w.Delete(k); }
                _st = 4; _f = 0; break;
            case 4 when _f == 120:
                // 本服删了之后重发的是另一页（被删的那格显示"(删除)"）
                Check(w.Visible && (Mine(s!) < _n0 || w.Page!.Shop!.Any(e => e.Desc.Contains("删除"))), $"删除后：{string.Join(" / ", w.Page?.Shop?.Select(e => $"{e.Name}「{e.Desc}」") ?? Array.Empty<string>())}");
                w.Close(); Check(!w.Visible && _m.Board.Page == null, "不再留言 → 关掉"); return Quit();
        }
        return _f > 4000;
    }
}
