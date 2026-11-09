using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Server;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 生产（test1）：学会制药/伐木、当药剂师、给配方 960/961 和材料 → 技能详情点"制药LV1"→ 选择秘笈 → 选 960 → 双击材料、登録 → 執行
/// → 约 10 秒后服务端给成品（生命力回复药（100））→ 結果 → 重試能自动登录；然后到芙蕾雅 (600,300) 伐木 → 得到印度轻木 → 停止。
/// </summary>
public partial class NetProduce : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_prod_{n}.png");
    bool End() { _m.Session.GetType(); (_m.Session as NetworkSession)?.Say("/dolua gm_job0.lua"); GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    SkillInfo? Sk(NetworkSession s, string n) => s.Player.Skills.FirstOrDefault(k => k.Name == n);
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var p = _m.ProdUi;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_clear.lua", "/dolua gm_prod.lua", "/dolua gm_job.lua", "/dolua gm_recipe.lua", "/dolua gm_heal.lua", "/warp 1000 0 242 100" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f == 60:
                s!.Say("/giveItem 12800 10 1 1"); s.Say("/giveItem 12801 2 1 1"); _st = 2; _f = 0; break;
            case 2 when _f > 60 && Sk(s!, "制药") is { Techs.Count: > 0 } && s.Player.Items.Any(i => i.ItemId == 12801):
            {
                var sk = Sk(s, "制药")!;
                Check(sk.Type == 1 && Sk(s, "伐木")?.Type == 4, $"技能种类：制药 {sk.Type}，伐木 {Sk(s, "伐木")?.Type}，招式 {sk.Techs[0].Name}");
                _m.SkillWin.ShowDetail(s.Player.Skills.IndexOf(sk)); _m.SkillWin.TechChosen?.Invoke(sk, 0); _st = 3; _f = 0; break;
            }
            case 2 when _f > 600: Check(false, $"没学会制药或没拿到材料：{string.Join(",", s!.Player.Skills.Select(k => k.Name + k.Techs.Count))}"); return End();
            case 3 when p.Recipes.Visible && p.Skill?.Recipes.Count > 0 || _f > 300:
                Check(p.Recipes.Visible && !_m.SkillWin.Visible && p.Skill!.Recipes.Count >= 2, $"选择秘笈：{string.Join("，", p.Skill?.Recipes.Select(r => $"{r.Id} {r.Name} MP{r.Mp} 能做{r.Ok} 材料 {string.Join("+", r.Materials.Select(m => m.Name + "×" + m.Count))}") ?? new string[0])}");
                Shot("1_recipes"); p.Recipes.TestChoose(0); _st = 4; _f = 0; break;
            case 4 when _f == 20:
                Check(p.Craft.Visible && p.Materials.Visible && p.Recipe?.Id == 960, "选了 960 → 製造 + 選擇素材");
                p.Register(); _st = 5; _f = 0; break;
            case 5 when _f == 10:
                Check(p.Materials.Message == "尚未选择材料。", $"没选就登录：{p.Materials.Message}");
                p.Materials.TestSelect(s!.Player.Items.First(i => i.ItemId == 12800).Slot); p.Register(); _st = 6; _f = 0; break;
            case 6 when _f == 10:
                Check(p.Materials.Message == "材料不足。", $"只选苹果薄荷：{p.Materials.Message}");
                p.Materials.TestSelect(s!.Player.Items.First(i => i.ItemId == 12801).Slot); p.Register(); _st = 7; _f = 0; break;
            case 7 when _f == 10:
                Check(p.Materials.Message == "材料登录了。" && p.Craft.Registered.Count == 2, $"两样都选：{p.Materials.Message}，登录 {string.Join(",", p.Craft.Registered)}");
                Shot("2_registered"); p.Execute(); _st = 8; _f = 0; break;
            case 8 when _f == 30:
                Check(p.Working && p.Materials.Message == "加工中", "執行 → 加工中"); Shot("3_working"); _st = 9; _f = 0; break;
            case 9 when p.Result.Visible || _f > 1200:
                Check(p.Result.Visible && p.LastResult?.Ok == true && s!.Player.Items.Any(i => i.ItemId == 15606), $"做出来了：{p.Result.Text}；背包有生命力回复药（100）{s!.Player.Items.Any(i => i.ItemId == 15606)}");
                _st = 10; _f = 0; break;
            case 10 when _f == 20:
                Shot("4_result"); p.Retry(); _st = 11; _f = 0; break;
            case 11 when _f == 20:
                Check(p.Craft.Visible && p.Materials.Message == "材料不足。", $"重試：材料不够了（柠檬草只剩 {s!.Player.Items.Where(i => i.ItemId == 12801).Sum(i => i.Count)}）→ {p.Materials.Message}");
                p.CancelCraft(); s.Say("/warp 100 0 600 300"); _st = 12; _f = 0; break;
            case 12 when _f == 120:
                p.UseTech(Sk(s!, "伐木")!, 0); _st = 13; _f = 0; break;
            case 13 when _f == 30:
                Check(p.Gathering.Visible && p.GatherRunning, "伐木：生産窗口、伐木中"); Shot("5_gather"); _st = 14; _f = 0; break;
            case 14 when p.Result.Visible && p.LastResult?.Ok == true || _f > 4000:
                Check(p.LastResult?.Type == 4 && p.LastResult.Ok && p.LastResult.Name.Length > 0 && s!.Player.Items.Any(i => i.Name == p.LastResult.Name), $"采集结果：{p.Result.Text}（背包里有了）");
                _st = 15; _f = 0; break;
            case 15 when _f == 10:
                Shot("6_gather_result"); _st = 16; _f = 0; break;
            case 16 when p.Gathering.Visible || _f > 600:
                Check(p.Gathering.Visible && p.GatherRunning, "2 秒后自动继续伐木"); p.StopGather(); _st = 17; _f = 0; break;
            case 17 when _f == 30:
                Check(!p.GatherRunning && p.Gathering.Visible, "停止 → 按钮变重試");
                var log = string.Join("\n", Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(x => x).TakeLast(1).SelectMany(File.ReadLines).Where(l => l.Contains("\ttest1\t")).TakeLast(400));
                Check(log.Contains("sM 5 0 -1 960|") || System.Text.RegularExpressions.Regex.IsMatch(log, @"sM \d+ 0 -1 960\|\d+\|\d+"), "服务端收到 sM 制造");
                Check(System.Text.RegularExpressions.Regex.IsMatch(log, @"IPy 6[\s\S]*sM \d+ 0 -1 *\r?\n[\s\S]*IPy 5"), "采集的 IPy 6 / sM / IPy 5");
                p.CloseAll(); s!.Say("/warp 1000 0 242 100");
                return End();
        }
        return _f > 9000;
    }
}
