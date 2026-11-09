using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 治疗（WN 19，贝特里夫医师 1112 11,18）：GM 先把人打成受伤 → 说话 → "请选择治疗对象"列出 人 + 2 只宠物（人有费用）→ 点人 → 扣钱、再说话费用变 0。
/// </summary>
public partial class NetHeal : SceneTree
{
    int _f, _st; long _gold0; int _cost; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_heal_{n}.png");
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    void Talk(NetworkSession s)
    {
        var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == "贝特里夫医师");
        if (n != null) { _page = null; _m.TalkOrApproach(n); }
    }
    string Rows() => string.Join("、", _m.Heal.Page?.Shop?.Select(e => $"Lv{e.Count} {e.Name} 伤{e.Image} {e.Price}G") ?? Array.Empty<string>());
    bool Quit() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var w = _m.Heal;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { $"/dolua gm_hurtname.lua {s.Player.Name} 60", "/addGold 1000", "/warp 1112 0 11 19" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f > 150 && s!.CurrentMap?.MapId == 1112:
                Talk(s); _st = 2; _f = 0; break;
            case 2 when _page?.Kind == "heal" && _f > 30:
            {
                var l = w.Page!.Shop!;
                _cost = l.FirstOrDefault()?.Price ?? 0;
                Check(w.Visible && l.Count >= 1 && l[0].Name == s!.Player.Name && _cost > 0, $"治疗窗口：{Rows()}");
                Shot("1_list"); _gold0 = s!.Player.Gold; w.Pick("1"); _st = 3; _f = 0; break;
            }
            case 2 when _f > 600: Check(false, $"治疗窗口没出来（{_page?.Kind}）"); return Quit();
            case 3 when _f == 150:
                Check(!w.Visible && s!.Player.Gold == _gold0 - _cost, $"点人：金钱 {_gold0} → {s!.Player.Gold}（费用 {_cost}）");
                Talk(s); _st = 4; _f = 0; break;
            case 4 when _f == 150:
                Check(w.Visible && w.Page!.Shop![0].Price == 0, $"治好了再说话：{Rows()}");
                w.Cancel(); Check(!w.Visible, "取消 → 关掉"); return Quit();
        }
        return _f > 4000;
    }
}
