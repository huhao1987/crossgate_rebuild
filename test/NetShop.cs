using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 商店（官方专用窗口）：医院药剂师 → 入口（想买 / 想卖 / 不必了）→ 买窗口（两列 10 格、▲▼、金币 / 总额）→ 买 2 瓶 → 确认框 → 是 → 钱少 150、药多 2；
/// 再进卖窗口：卖不了的（这个服务端全是 0）▲加不上、全賣也选不上。每步拍图对照官方。
/// </summary>
public partial class NetShop : SceneTree
{
    int _f, _st; long _gold0; int _items0; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_shop_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
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
        var doc = s.NpcsOnCurrentMap().FirstOrDefault(n => n.X == 16 && n.Y == 35);
        if (doc != null) { _page = null; _m.TalkOrApproach(doc); }
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var w = _m.Shop; var menu = _m.ShopMenuUi;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/dolua gm_clear.lua"); s.Say("/addGold 1000"); s.Say("/warp 1112 0 16 36"); _st = 1; _f = 0; break;
            case 1 when _f > 90 && s!.CurrentMap?.MapId == 1112:
                Talk(s); _st = 2; _f = 0; break;
            case 2 when _page?.Kind == "shop" && _f > 30:
                Check(menu.Visible && menu.Page?.Options?.Count == 3, $"入口窗口：{menu.Page?.NpcName}「{menu.Page?.Text}」 [{string.Join("/", menu.Page?.Options ?? new List<string>())}]");
                Shot("1_menu"); _page = null; menu.Pick(0); _st = 3; _f = 0; break;
            case 2 when _f > 600: Check(false, "入口窗口没出来"); return End();
            case 3 when _page?.Kind == "buy" && _f > 30:
                Check(w.Visible && w.Buying && w.Page!.Shop!.Count > 0, $"买窗口：{string.Join("、", w.Page!.Shop!.Select(e => $"{e.Name} {e.Price}G"))}");
                _gold0 = s!.Player.Gold; _items0 = s.Player.Items.Sum(i => i.Count);
                w.Add(0, 1); w.Add(0, 1); w.Add(1, 1); w.Add(1, -1);
                Check(w.Quantities[0] == 2 && w.Quantities[1] == 0, $"▲▲ ▲▼：{string.Join(",", w.Quantities)}");
                Shot("2_buy"); w.Ok(); _st = 4; _f = 0; break;
            case 3 when _f > 600: Check(false, "买窗口没出来"); return End();
            case 4 when _f == 20:
                Check(w.Confirming, "确认框"); Shot("3_confirm"); w.Yes(); _st = 5; _f = 0; break;
            case 5 when _f == 120:
                Check(s!.Player.Gold == _gold0 - 150 && s.Player.Items.Sum(i => i.Count) == _items0 + 2, $"买了 2 瓶：金钱 {_gold0} → {s.Player.Gold}，道具 {_items0} → {s.Player.Items.Sum(i => i.Count)}");
                Talk(s); _st = 6; _f = 0; break;
            case 6 when _page?.Kind == "shop" && _f > 30:
                _page = null; menu.Pick(1); _st = 7; _f = 0; break;
            case 6 when _f > 600: Check(false, "第二次入口没出来"); return End();
            case 7 when _page?.Kind == "sell" && _f > 30:
            {
                var list = w.Page!.Shop!;
                Check(w.Visible && !w.Buying && list.Count > 0, $"卖窗口：{list.Count} 件，{string.Join("、", list.Take(4).Select(e => $"{e.Name}×{e.Count} {e.Price}G {e.Stack}/组 可卖{e.Flag}"))}");
                w.Add(0, 1); w.ToggleAll();
                Check(w.Quantities.All(q => q == 0) || list.Any(e => e.Flag != 0), $"卖不了的选不上：{string.Join(",", w.Quantities.Take(6))}");
                Shot("4_sell"); w.Cancel();
                Check(!w.Visible, "取消 → 关掉"); return End();
            }
            case 7 when _f > 600: Check(false, $"卖窗口没出来（{_page?.Kind}）"); return End();
        }
        return _f > 3000;
    }
}
