using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Server;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 职业 / 技能导师 / 称号（test1，地图 777 技能与职业）：游民 + 剑士推荐信 → 剑士公会会长「我想就职」→ 见习剑士；
/// 制药在剑士职业下最高 0 级（技能详情显示 "--"）；导师「连续攻击」→ 想忘记 → 忘记挖掘 → 是；导师「诸刃」→ 想学习 → 学习 → 学会诸刃；
/// 称号：选第 1 个 → 状态窗口特殊称号显示；解除；玩家称号 abc。
/// </summary>
public partial class NetJob : SceneTree
{
    int _f, _st; string _forget = "挖掘"; Main _m = null!; readonly List<string> _r = new(); DialogPage? _page; bool _hook;
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_job_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    void TalkTo(NetworkSession s, string name, int x, int y)
    {
        _page = null;
        var n = s.NpcsOnCurrentMap().FirstOrDefault(k => k.Name == name && k.X == x && k.Y == y);
        if (n == null) return;
        if (Math.Max(Math.Abs(n.X - s.X), Math.Abs(n.Y - s.Y)) <= 1 && (n.X, n.Y) != (s.X, s.Y)) { s.TalkTo(n.Id); return; }
        // 旁边的格子被人（官方客户端 bob）站了：换个格子再试
        _retry++;
        var (dx, dy) = new[] { (0, 1), (1, 1), (-1, 1), (1, 0), (-1, 0) }[_retry % 5];
        s.Say($"/warp 777 0 {x + dx} {y + dy}");
    }
    int _retry;
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) { _page = p; GD.Print($"PAGE kind={p.Kind} text=[{p.Text.Replace("\n","/")}] opts={p.Options?.Count} btn={string.Join(",", p.Buttons)}"); } }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_job0.lua", "/dolua gm_skills.lua", "/dolua gm_prod.lua", "/giveItem 18101 1 1 1", "/warp 777 0 47 1" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f == 150:
                Check(s!.Player.Job == "游民", $"先当游民：{s.Player.Job}");
                TalkTo(s, "剑士公会会长", 47, 0); _st = 2; _f = 0; break;
            case 2 when _page != null || _f > 300:
                Check(_page?.Options?.Contains("我想就职") == true, $"剑士公会会长：{_page?.Text.Replace("\n", " ").Trim()} [{string.Join("/", _page?.Options ?? new string[0])}]");
                Shot("1_guild"); s!.Answer("opt:0"); _page = null; _st = 3; _f = 0; break;
            case 3 when s!.Player.Job != "游民" && _f > 30 || _f > 300:
                Check(s.Player.Job == "见习剑士", $"就职：{s.Player.Job}（{_page?.Text.Replace("\n", " ").Trim()}）");
                s.Answer("ok"); _st = 4; _f = 0; break;
            case 4 when _f == 30:
            {
                GD.Print("SKILLS " + string.Join(",", s!.Player.Skills.Select(x => x.Name)));
                var k = s!.Player.Skills.FirstOrDefault(x => x.Name == "制药");
                Check(k != null && k.MaxLevel < (s.Player.Skills.FirstOrDefault(x => x.Name == "气功弹")?.MaxLevel ?? -1), $"等级上限：制药 {k?.Level}/{k?.MaxLevel}，气功弹 {(s.Player.Skills.FirstOrDefault(x => x.Name == "气功弹")?.MaxLevel ?? -1)}");
                _m.SkillWin.ShowDetail(s.Player.Skills.IndexOf(k!)); _st = 5; _f = 0; break;
            }
            case 5 when _f == 20:
                Shot("2_skill_max"); _m.SkillWin.Visible = false;
                s!.Say("/warp 777 0 53 1"); _page = null; _st = 6; _f = 0; break;
            case 6 when _f % 90 == 0:
                TalkTo(s!, "连续攻击", 53, 0); if (_f > 600) { _st = 7; _f = 0; } break;
            case 6 when _page != null: _st = 7; _f = 0; break;
            case 7 when _page != null || _f > 300:
                Check(_page?.Kind == "skillmenu" && _page.Options?.Count == 3, $"技能导师入口：{_page?.Text.Trim()} [{string.Join("/", _page?.Options ?? new string[0])}]");
                Shot("3_menu"); s!.Answer("opt:1"); _st = 8; _f = 0; break;
            case 8 when s!.SkillShop != null || _f > 300:
            {
                var pg = s.SkillShop;
                _forget = pg?.Known.Any(k => k.Name == "诸刃") == true ? "诸刃" : "挖掘";   // 上次测试学会的诸刃先忘掉，再学一次
                int idx = pg?.Known.ToList().FindIndex(k => k.Name == _forget) ?? -1;
                Check(pg?.Type == 18 && idx >= 0 && _m.SkillShopUi.Visible, $"忘记窗口：{string.Join("、", pg?.Known.Select(k => k.Name) ?? new string[0])}");
                _st = 9; _f = 0; break;
            }
            case 9 when _f == 20:
                Shot("4_forget"); _page = null; _m.SkillShopUi.TestChoose(s!.SkillShop!.Known.ToList().FindIndex(k => k.Name == _forget) + 1); _st = 10; _f = 0; break;
            case 10 when _page != null || _f > 300:
                Check(_page?.Buttons.Contains("yes") == true, $"确认：{_page?.Text.Trim()}"); _page = null; s!.Answer("yes"); _st = 11; _f = 0; break;
            case 11 when s!.Player.Skills.All(k => k.Name != _forget) && _page?.Text.Contains("删除了") == true || _f > 300:
                Check(s.Player.Skills.All(k => k.Name != _forget), $"{_forget}忘掉了（{_page?.Text.Trim()}）");
                if (_page != null) s.Answer("ok");
                _st = 12; _f = 0; break;
            case 12 when _f == 30:
                s!.Say("/warp 777 0 53 3"); _page = null; _st = 13; _f = 0; break;
            case 13 when _f % 90 == 0:
                TalkTo(s!, "诸刃", 53, 2); if (_f > 600) { _st = 14; _f = 0; } break;
            case 13 when _page != null: _st = 14; _f = 0; break;
            case 14 when _page != null || _f > 300:
                s!.Answer("opt:0"); _st = 15; _f = 0; break;
            case 15 when s!.SkillShop?.Type == 17 || _f > 300:
                Check(s.SkillShop?.Type == 17 && s.SkillShop.SkillName == "诸刃", $"学习窗口：{s.SkillShop?.SkillName} 栏位 {s.SkillShop?.Slots} 费用 {s.SkillShop?.Cost}");
                _st = 16; _f = 0; break;
            case 16 when _f == 20:
                Shot("5_learn"); _page = null; _m.SkillShopUi.TestChoose(1); _st = 17; _f = 0; break;
            case 17 when s!.Player.Skills.Any(k => k.Name == "诸刃") && _page != null || _f > 300:
                Check(s.Player.Skills.Any(k => k.Name == "诸刃"), $"学会诸刃（{_page?.Text.Trim()}）");
                if (_page != null) s.Answer("ok");
                _st = 18; _f = 0; break;
            case 18 when _f == 30:
                Check(s!.Titles.Count > 0, $"称号列表：{string.Join("、", s.Titles)}");
                _m.StatusWin.ShowTab(2); _m.StatusWin.TestTitle(0); _st = 19; _f = 0; break;
            case 19 when s!.TitleIndex == 0 && _f > 20 || _f == 300:
                Shot("6_titles");
                GD.Print($"T19 f={_f}");
                Check(s!.TitleIndex == 0 && s.SpecialTitle == s.Titles[0], $"选称号：{s.TitleIndex} {s.SpecialTitle}");
                _m.StatusWin.TestFreeTitle("abc"); _st = 20; _f = 0; break;
            case 20 when s!.FreeTitle == "abc" || _f == 300:
                Check(s!.FreeTitle == "abc", $"玩家称号：{s.FreeTitle}");
                _m.StatusWin.ShowTab(0); _st = 21; _f = 0; break;
            case 21 when _f == 20:
                Shot("7_status"); _m.StatusWin.ShowTab(2); _m.StatusWin.TestClearTitle(); _st = 22; _f = 0; break;
            case 22 when s!.TitleIndex == -1 || _f == 300:
                Check(s!.TitleIndex == -1, $"解除称号：{s.TitleIndex}");
                _m.StatusWin.TestFreeTitle(""); _m.StatusWin.Visible = false;
                s.Say("/warp 1000 0 242 100");
                return End();
        }
        return _f > 9000;
    }
}
