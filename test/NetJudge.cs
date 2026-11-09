using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 鉴定（WN 20，凯蒂夫人 1031 17,12）/ 修理（WN 22，米克尔 1071 7,8）：两把没鉴定的铜剑（GM 把耐久打掉一半）→
/// 鉴定窗口列出 2 把「单手剑？」→ ▲ 只能到 1 → 鉴定 → 确认框 → 是 → 扣 60、剑名字变「铜剑」；修理窗口 → 选 1 把 → 是 → 扣钱。每步拍图。
/// </summary>
public partial class NetJudge : SceneTree
{
    int _f, _st; long _gold0; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_judge_{n}.png");
    bool End() { (_m.Session as NetworkSession)?.Say("/dolua gm_clear.lua"); _st = 99; _f = 0; return false; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    void Talk(NetworkSession s, string name)
    {
        var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == name);
        if (n != null) { _page = null; _m.TalkOrApproach(n); }
    }
    static string Swords(NetworkSession s) => string.Join(",", s.Player.Items.Where(i => i.Slot is 24 or 25).Select(i => i.Name));
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var w = _m.Shop;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_clear.lua", "/giveItem 0 1 1 0", "/giveItem 0 1 1 0", "/dolua gm_dmg.lua", "/addGold 1000", "/warp 1031 0 17 13" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f > 150 && s!.CurrentMap?.MapId == 1031:
                Talk(s, "凯蒂夫人"); _st = 2; _f = 0; break;
            case 2 when _page?.Kind == "judge" && _f > 30:
            {
                var l = w.Page!.Shop!;
                Check(w.Visible && w.Picking && l.Count == 2 && l.All(e => e.Name == "单手剑？" && e.Price == 60), $"鉴定窗口：{string.Join("、", l.Select(e => $"{e.Name} {e.Price}G 格{e.Slot}"))}（剑 {Swords(s!)}）");
                w.Add(0, 1); w.Add(0, 1);
                Check(w.Quantities[0] == 1, $"▲▲ 只到 1：{w.Quantities[0]}");
                Shot("1_judge"); _gold0 = s!.Player.Gold; w.Ok(); _st = 3; _f = 0; break;
            }
            case 2 when _f > 600: Check(false, $"鉴定窗口没出来（{_page?.Kind}）"); return End();
            case 3 when _f == 20:
                Check(w.Confirming, "确认框"); Shot("2_confirm"); w.Yes(); _st = 4; _f = 0; break;
            case 4 when _f == 150:
                Check(s!.Player.Gold == _gold0 - 60 && Swords(s).Contains("铜剑"), $"鉴定：金钱 {_gold0} → {s.Player.Gold}，剑 {Swords(s)}");
                s.Say("/warp 1071 0 7 9"); _st = 5; _f = 0; break;
            case 5 when _f > 150 && s!.CurrentMap?.MapId == 1071:
                Talk(s, "米克尔"); _st = 6; _f = 0; break;
            case 6 when _page?.Kind == "repair" && _f > 30:
            {
                var l = w.Page!.Shop!;
                Check(w.Visible && l.Count >= 1, $"修理窗口：{string.Join("、", l.Select(e => $"{e.Name} {e.Price}G 格{e.Slot}"))}");
                _gold0 = s!.Player.Gold; w.Add(0, 1); Shot("3_repair"); w.Ok(); _st = 7; _f = 0; break;
            }
            case 6 when _f > 600: Check(false, $"修理窗口没出来（{_page?.Kind}）"); return End();
            case 7 when _f == 20:
                Check(w.Confirming, "修理确认框"); Shot("4_confirm"); _page = null; w.Yes(); _st = 8; _f = 0; break;
            case 8 when _f == 150:
                Check(s!.Player.Gold < _gold0, $"修理：金钱 {_gold0} → {s.Player.Gold}");
                Talk(s, "米克尔"); _st = 9; _f = 0; break;
            case 9 when _f == 150:
                Check(_page?.Kind == "repair" && w.Page!.Shop!.Count == 1, $"修好的那把不再列出：{w.Page?.Shop?.Count} 件");
                w.Cancel(); Check(!w.Visible, "取消 → 关掉"); return End();
            case 99 when _f == 90: GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 4000;
    }
}
