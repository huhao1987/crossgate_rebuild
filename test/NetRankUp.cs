using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 提升阶级（见习剑士 → 剑士）成功路径：GM 补条件（等级 20、晋级试验旗标 90–93、钱；声望之前已设 6000），
/// 剑士公会会长「我想提升阶级」→ 手续费 5000 的是 / 不是 → 是 → 职业变剑士、钱少 5000、「提升了阶级」提示、称号换成剑士。测完还原。
/// </summary>
public partial class NetRankUp : SceneTree
{
    int _f, _st, _retry; long _gold; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new(), _chat = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_rankup_{n}.png");
    // 收尾：先还原（等服务端处理完再退出，不然还原指令会丢，下一个测试拿到 Lv20 的剑士）
    bool End() { (_m.Session as NetworkSession)?.Say("/dolua gm_jobx_undo.lua"); _st = 98; _f = 0; return false; }
    bool Quit() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    void TalkTo(NetworkSession s)
    {
        var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == "剑士公会会长");
        if (n == null) return;
        if (Math.Max(Math.Abs(n.X - s.X), Math.Abs(n.Y - s.Y)) <= 1 && (n.X, n.Y) != (s.X, s.Y)) { s.TalkTo(n.Id); return; }
        var (dx, dy) = new[] { (0, 1), (1, 1), (-1, 1), (1, 0), (-1, 0) }[++_retry % 5];
        s.Say($"/warp 777 0 {n.X + dx} {n.Y + dy}");
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; s.ChatReceived += (t, _) => _chat.Add(t); }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_jobx_undo.lua", "/dolua gm_fame.lua", "/dolua gm_lv20.lua", "/dolua gm_trial.lua", "/dolua gm_givegold.lua" }) s.Say(c);
                s.Say("/warp 777 0 47 1"); _st = 1; _f = 0; break;
            case 1 when _f == 120:
                Check(s!.Player.Job == "见习剑士", $"开始是：{s.Player.Job}"); _gold = s.Player.Gold; _page = null; _st = 2; _f = 0; break;
            case 2 when _page == null && _f % 60 == 1: TalkTo(s!); if (_f > 900) { Check(false, "跟会长说不上话"); return End(); } break;
            case 2 when _page != null:
                _page = null; s!.Answer("opt:2"); _st = 3; _f = 0; break;
            case 3 when _page != null && _f > 30:
                Check(_page.Text.Contains("手续费总共是") && _page.Text.Contains("5000") && _page.Options?.Count == 2 && _page.Options[0] == "是", $"手续费确认：{_page.Text.Replace("\n", "/")} [{string.Join("/", _page.Options ?? new List<string>())}]");
                Shot("1_fee"); _page = null; _gold = s!.Player.Gold; s.Answer("opt:0"); _st = 4; _f = 0; break;
            case 3 when _f > 300: Check(false, $"没出手续费确认（{_page?.Text.Replace("\n", "/")}）"); return End();
            case 4 when _f == 120:
                Check(s!.Player.Job == "剑士", $"职业：{s.Player.Job}");
                Check(_gold - s.Player.Gold == 5000, $"手续费：{_gold} → {s.Player.Gold}");
                Check(_chat.Any(t => t.Contains("提升了阶级")), $"提示：{string.Join(" / ", _chat.Where(t => t.Contains("阶级") || t.Contains("称号")))}");
                Check(s.Titles.Contains("剑士"), $"称号：{string.Join(",", s.Titles)}");
                Shot("2_done"); s.Say("/dolua gm_jobx_undo.lua"); _st = 5; _f = 0; break;
            case 5 when _f == 90:
                Check(s!.Player.Job == "见习剑士" && s.Player.Level == 2, $"还原：{s.Player.Job} Lv{s.Player.Level}"); return Quit();
            case 98 when _f == 120: return Quit();
        }
        return _f > 3000;
    }
}
