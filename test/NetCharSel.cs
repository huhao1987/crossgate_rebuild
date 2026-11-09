using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 选角色 / 创建 / 删除（账号 test4，界面打开 char_select）：空账号 → 選擇角色挑第 2 个形象、颜色 2 → 創造角色填名字、点数、属性 → 「可以吗？」是
/// → 列表出现（形象 105039、头像 = 202900+50+…）→ 删除（问两次）→ 列表空 → 再建一个 → 登入 → 进游戏。
/// </summary>
public partial class NetCharSel : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_charsel_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test4"; o["password"] = "123456"; o["load_save"] = false;
        o["char_select"] = 1;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    NetworkSession S => (NetworkSession)_m.Session;
    public override bool _Process(double d)
    {
        _f++;
        var cs = _m.CharSelectUi;
        switch (_st)
        {
            case 0 when cs != null && cs.Visible && _f > 30:
                Check(true, $"登录后显示选角色：{string.Join(" / ", S.CharSlots.Select(c => c?.Name ?? "（空）"))}");
                // 账号里已有角色就先删掉（上次测试留下的）
                if (S.CharSlots.Any(c => c != null)) { int p = S.CharSlots.ToList().FindIndex(c => c != null); S.DeleteChar(p); _st = 0; _f = -60; break; }
                Shot("1_empty");
                cs.TestPick(1, 2); _st = 1; _f = 0; break;
            case 0 when _f > 900: Check(false, "没出现选角色界面"); return End();
            case 1 when _f == 20:
                Shot("2_pick");
                cs!.TestCreatePage(0); cs.TestFill("selq", new[] { 15, 0, 0, 15, 0 }, new[] { 0, 0, 10, 0 }, 2, 1);
                Check(cs.Body == 105039, $"形象 = 105027 + 6×2 = {cs.Body}，头像 {cs.Face}");
                _st = 2; _f = 0; break;
            case 2 when _f == 20:
                Shot("3_create");
                cs!.TryCreate(); cs.TestConfirm(); _st = 3; _f = 0; break;
            case 3 when _f % 60 == 0 && _f < 300: GD.Print($"DBG slots {string.Join(",", S.CharSlots.Select(c => c?.Name ?? "-"))}"); break;
            case 3 when S.CharSlots[0]?.Name == "selq" || _f > 300:
            {
                var c = S.CharSlots[0];
                Check(c?.Name == "selq" && c.Anime == 105039 && c.Face == 202961 && c.Stats[0] == 15, $"创建好了：{c?.Name} 形象 {c?.Anime} 头像 {c?.Face} 点数 {string.Join(",", c?.Stats ?? new int[0])} 属性 {string.Join(",", c?.Elements ?? new int[0])}");
                _st = 4; _f = 0; break;
            }
            case 4 when _f == 20:
                Shot("4_list");
                cs!.AskDelete(0); cs.TestConfirm(); cs.TestConfirm(); _st = 5; _f = 0; break;
            case 5 when S.CharSlots[0] == null && _f > 30 || _f > 300:
                Check(S.CharSlots[0] == null, "删除（问两次）→ 列表空了");
                cs!.TestPick(0, 0); cs.TestCreatePage(1); cs.TestFill("selr", new[] { 10, 5, 5, 5, 5 }, new[] { 5, 5, 0, 0 }, 1, 1);
                cs.TryCreate(); cs.TestConfirm(); _st = 6; _f = 0; break;
            case 6 when S.CharSlots[1]?.Name == "selr" || _f > 300:
                Check(S.CharSlots[1]?.Name == "selr", $"右页再建一个：{S.CharSlots[1]?.Name}");
                S.EnterChar(1); cs!.Visible = false; _st = 7; _f = 0; break;
            case 7 when S.InGame && S.CurrentMap != null && _f > 60 || _f > 600:
                Check(S.InGame && S.Player.Name == "selr", $"登入 → 进游戏：{S.Player.Name} @ {S.CurrentMap?.Name}");
                Check(S.Player.Stats.Element[0] == 50 && S.Player.Stats.Element[1] == 50, $"属性（第一次登入后才生效）：地水火风 {string.Join(",", S.Player.Stats.Element)}");
                return End();
        }
        return _f > 5000;
    }
}
