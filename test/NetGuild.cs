using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 家族：hao 是「浩之家」族长（服务端建好的）。登录收 GI → 家族名 / 职称号 0；名片上 bob 的家族栏；
/// GM 脚本发家族兽窗口（BZ 32）→ 名字 + 说明（超长行折行）、翻页；放道具 →「喂」发 xD … 3 I|格；改名发 xD … 0 N|名字。
/// </summary>
public partial class NetGuild : SceneTree
{
    int _f, _st, _slot = -1; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_guild_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    static string LastSent()
    {
        var f = Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(File.GetLastWriteTime).Last();
        var lines = CgPort.Core.Text.Gbk.Decode(File.ReadAllBytes(f)).Split('\n');
        return lines.LastOrDefault(l => l.Contains("[Recv]") && l.Contains("\ttest1\txD ")) ?? "";
    }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var w = _m.GuildBeastUi;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 120:
            {
                Check(s.GuildName == "浩之家" && s.GuildRoom == "浩之馆" && s.GuildTitleId == 0 && s.GuildId == 10001, $"GI：{s.GuildId} {s.GuildName} / {s.GuildRoom} 职称 {s.GuildTitleId} 权限 {s.GuildCommission}");
                var bob = s.Cards.FirstOrDefault(c => c?.Name == "bob");
                if (bob != null) Check(bob.Guild == "浩之家", $"名片 bob 的家族：「{bob.Guild}」"); else GD.Print("（名片簿里没有 bob，跳过家族栏检查）");
                s.Say("/dolua gm_beastwin.lua"); _st = 1; _f = 0; break;
            }
            case 0 when _f > 900: Check(false, "没进游戏"); return End();
            case 1 when w.Visible || _f > 300:
                Check(w.Visible && w.Beast?.Name == "小兽", $"家族兽窗口：{w.Beast?.Name}");
                Check(w.Beast?.Lines.Count == 13 && w.Beast.Lines[2] == "性格：开朗活泼爱玩耍的好孩子" && w.Beast.Lines[3] == "呀", $"说明 {w.Beast?.Lines.Count} 行（第 3 行折行）：{string.Join("/", w.Beast?.Lines.Take(4) ?? Array.Empty<string>())}");
                _st = 2; _f = 0; break;
            case 2 when _f == 20:
                Shot("1_window");
                w.Page(1); Check(w.Start == 10, $"▶ 翻到第 {w.Start + 1} 行起"); Shot("2_page2");
                w.Page(1); Check(w.Start == 10, "最后一页再按 ▶ 不动");
                w.Page(-1); Check(w.Start == 0, "◀ 回第一页");
                _slot = s!.Player.Items.Where(i => i.Slot >= 8).Select(i => i.Slot).DefaultIfEmpty(-1).First();
                w.PutItem(_slot); _st = 3; _f = 0; break;
            case 3 when _f == 20:
                Check(_slot >= 8 && w.FeedSlot == _slot, $"道具放进喂食格：格 {_slot}"); Shot("3_item");
                w.Feed(); _st = 4; _f = 0; break;
            case 4 when _f == 60:
                Check(LastSent().EndsWith($" 3 I|{_slot}") || LastSent().TrimEnd().EndsWith($" 3 I|{_slot}"), $"喂：{LastSent().Split('\t').Last().Trim()}");
                s!.Say("/dolua gm_beastwin.lua"); _st = 5; _f = 0; break;
            case 5 when _f == 60:
                w.TestEdit("大兽"); Check(w.Editing, "族长点名字框 → 可以改名"); Shot("4_edit");
                w.Rename(); _st = 6; _f = 0; break;
            case 6 when _f == 60:
                Check(LastSent().TrimEnd().EndsWith(" 0 N|大兽"), $"更名：{LastSent().Split('\t').Last().Trim()}");
                w.Close(); Check(!w.Visible, "× 关闭"); return End();
        }
        return _f > 3000;
    }
}
