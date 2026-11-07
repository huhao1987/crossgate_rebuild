using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 联网名片/邮件（配合工具号 bob = test2：站在 242,101、开着「名」、过一会儿给名片簿第 0 张写信）：
/// 开「名」→ 站到 bob 旁边面向他 → 右上「交换名片」→ 名片簿里有 bob → 收到 bob 的信（左上「郵件」亮）→ 记录里有内容
/// → 给 bob 写信 → 删除 bob 的名片（确认框点"是"）。
/// </summary>
public partial class NetCard : SceneTree
{
    int _f, _st, _try, _bobIdx = -1; (int, int) _face, _want; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_card_{n}.png");
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
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        var h = _m.HudSkin;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90 && s.NpcsOnCurrentMap().Any(n => n.Name == "bob"):
                // 先删掉旧的 bob 名片（上次测试留下的），确保是这次交换来的
                for (int i = 0; i < 60; i++) if (s.Cards[i]?.Name == "bob") s.DeleteCard(i);
                _st = 100; _f = 0; break;
            case 100 when _f == 30:
            {
                var bob = s!.NpcsOnCurrentMap().First(n => n.Name == "bob");
                var (fx, fy) = CgPort.Core.Directions.Deltas[_try % 8];
                _face = (-fx, -fy); _want = (bob.X + fx, bob.Y + fy);
                s.Say($"/warp 1000 0 {_want.Item1} {_want.Item2}"); break;
            }
            case 100 when _f == 90:
                if ((s!.X, s.Y) == _want) { _st = 1; _f = 0; }
                else if (++_try >= 8) { Check(false, "bob 周围没有能站的格子"); return End(); }
                else _f = 0;
                break;
            case 1 when _f == 10:
                if ((s!.Flags & 16) == 0) h.TestClickFlag(3);
                _st = 2; _f = 0; break;
            case 2 when _f == 40:
                Check((s!.Flags & 16) != 0 && h.FlagLit(3), $"「名」开着（开关 {s.Flags}）");
                s.Face(_face.Item1, _face.Item2); _st = 3; _f = 0; break;
            case 3 when _f == 20:
                h.TestPressButton(22587); _st = 4; _f = 0; break;
            case 4 when s!.Cards.Any(c => c?.Name == "bob") || _f > 300:
                _bobIdx = Array.FindIndex(s.Cards.ToArray(), c => c?.Name == "bob");
                Check(_bobIdx >= 0, $"交换名片后名片簿里有 bob（第 {_bobIdx} 张，Lv{(_bobIdx >= 0 ? s.Cards[_bobIdx]!.Level : 0)}，{(_bobIdx >= 0 && s.Cards[_bobIdx]!.Online ? "在线" : "离线")}）");
                _st = 5; _f = 0; break;
            case 5 when _f == 30:
                Check(_m.Chat.Lines.Any(l => l.Contains("交换了名片") || l.Contains("的名片。")), $"聊天栏：{string.Join(" / ", _m.Chat.Lines)}");
                _m.CardBookUi.Toggle(); _st = 6; _f = 0; break;
            case 6 when _f == 20:
                Shot("1_book"); _m.CardBookUi.Visible = false; _st = 7; _f = 0; break;
            case 7 when s!.Mails.Any(m => m.Incoming) || _f > 2400:
                var mail = s.Mails.FirstOrDefault(m => m.Incoming);
                Check(mail != null && mail.Text == "hello hao" && h.MailLit, $"收到 bob 的信：{mail?.Name} {mail?.Date} 「{mail?.Text}」，左上「郵件」亮 {h.MailLit}");
                _st = 8; _f = 0; break;
            case 8 when _f == 20:
                Shot("2_mail_lit");
                _m.MailRecordUi.Open(Math.Max(0, _bobIdx)); _st = 9; _f = 0; break;
            case 9 when _f == 20:
                Shot("3_record"); _m.MailRecordUi.Visible = false;
                _m.MailEditUi.Open(Math.Max(0, _bobIdx)); _m.MailEditUi.Text = "hi bob 你好"; _st = 10; _f = 0; break;
            case 10 when _f == 20:
                Shot("4_edit"); _m.MailEditUi.DoSend(); _st = 11; _f = 0; break;
            case 11 when _f == 60:
                Check(_m.Chat.Lines.Any(l => l.Contains("已将邮件送交bob")), $"写信送出：{string.Join(" / ", _m.Chat.Lines)}");
                _m.MailEditUi.Visible = false;
                _m.CardBookUi.Toggle(); _m.CardBookUi.AskDelete?.Invoke(_bobIdx); _st = 12; _f = 0; break;
            case 12 when _f == 20:
                Check(_m.ConfirmUi.Visible, "删除前弹出确认框");
                Shot("5_confirm"); _m.ConfirmUi.TestYes(); _st = 13; _f = 0; break;
            case 13 when _f == 60:
                Check(s!.Cards.All(c => c?.Name != "bob"), "点「是」后 bob 的名片删掉了");
                return End();
        }
        return _f > 5000;
    }
}
