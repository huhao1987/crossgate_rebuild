using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 交易入口 + 兑换（WN 27 → 28，交换银 1071 30,5）：给 40 个银 → 说话 → 入口「想 交 易」「 没  事 」→ 想交易 →
/// 兑换窗口（银条，每条 20 个银）→ ▲▲▲（第三下加不上）→ 兑换 → 确认框 → 是 → 银少 40、多 2 条银条；再进来点「没事」只关窗口。
/// </summary>
public partial class NetExchange : SceneTree
{
    int _f, _st; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_exch_{n}.png");
    bool End() { (_m.Session as NetworkSession)?.Say("/dolua gm_clear.lua"); _st = 99; _f = 0; return false; }
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
        var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == "交换银");
        if (n != null) { _page = null; _m.TalkOrApproach(n); }
    }
    static int Cnt(NetworkSession s, string name) => s.Player.Items.Where(i => i.Name == name && i.Slot >= 8).Sum(i => Math.Max(1, i.Count));
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var w = _m.Shop; var menu = _m.ShopMenuUi;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_clear.lua", "/giveItem 9602 20", "/giveItem 9602 20", "/warp 1071 0 30 6" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f > 150 && s!.CurrentMap?.MapId == 1071:
                Talk(s); _st = 2; _f = 0; break;
            case 2 when _page?.Kind == "trademenu" && _f > 30:
                Check(menu.Visible && menu.Page?.Options?.Count == 2, $"交易入口：{menu.Page?.NpcName}「{menu.Page?.Text}」");
                Shot("1_menu"); _page = null; menu.Pick(0); _st = 3; _f = 0; break;
            case 2 when _f > 600: Check(false, $"交易入口没出来（{_page?.Kind}）"); return End();
            case 3 when _page?.Kind == "exchange" && _f > 30:
            {
                var l = w.Page!.Shop!;
                Check(w.Visible && w.Exchanging && l.Count >= 1 && w.Page.SourceName == "银", $"兑换窗口：拿 {w.Page.SourceName}（{w.Page.SourceItem}）换 {string.Join("、", l.Select(e => $"{e.Name} 每个 {e.Price}"))}，身上 {Cnt(s!, "银")} 个银");
                w.Add(0, 1); w.Add(0, 1); w.Add(0, 1);
                Check(w.Quantities[0] == 2, $"▲▲▲ 银只够 2 条：{w.Quantities[0]}");
                Shot("2_exch"); w.Ok(); _st = 4; _f = 0; break;
            }
            case 3 when _f > 600: Check(false, $"兑换窗口没出来（{_page?.Kind}）"); return End();
            case 4 when _f == 20:
                Check(w.Confirming, "确认框"); Shot("3_confirm"); w.Yes(); _st = 5; _f = 0; break;
            case 5 when _f == 150:
                Check(Cnt(s!, "银") == 0 && Cnt(s!, "银条") == 2, $"换完：银 {Cnt(s!, "银")}，银条 {Cnt(s!, "银条")}");
                Talk(s!); _st = 6; _f = 0; break;
            case 6 when _page?.Kind == "trademenu" && _f > 30:
                _page = null; menu.Pick(1); _st = 7; _f = 0; break;
            case 7 when _f == 60:
                Check(!menu.Visible && !w.Visible && _page == null, "没事 → 只关窗口"); return End();
            case 99 when _f == 90: GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 4000;
    }
}
