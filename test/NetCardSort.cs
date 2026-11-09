using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>名片簿「排列」→ 整理名片：三种顺序（登录 / 等级 / 上线）的排法 + 窗口選了「等级顺序」按確定后存进设定、名片簿回来。</summary>
public partial class NetCardSort : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    bool End() { _m.Cfg.CardSort = 0; GameConfig.Patch(("card_sort", 0)); GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
            {
                var cards = new NetworkSession.AddressCard?[60];
                cards[0] = new(0, "甲", "", 5, false, 0) { Seq = 3 };
                cards[1] = new(1, "乙", "", 20, true, 0) { Seq = 1 };
                cards[3] = new(3, "丙", "", 20, false, 0) { Seq = 2 };
                cards[5] = new(5, "丁", "", 1, true, 0) { Seq = 4 };
                string N(int[] o) => string.Join("", o.Take(4).Select(i => cards[i]!.Name));
                var a = CardBookWindow.Order(cards, 0); var b = CardBookWindow.Order(cards, 1); var c = CardBookWindow.Order(cards, 2);
                Check(N(a) == "乙丙甲丁", $"登录顺序：{N(a)}");
                Check(N(b) == "乙丙甲丁", $"等级顺序（同级按登录）：{N(b)}");
                Check(N(c) == "乙丁丙甲", $"上线顺序：{N(c)}");
                Check(a.Skip(4).All(i => cards[i] == null), "空名片排最后");
                _m.HudSkin.TestMenu(5); _st = 1; _f = 0; break;
            }
            case 1 when _f == 30:
                _m.CardBookUi.OpenSort?.Invoke();
                Check(_m.CardSortUi.Visible && !_m.CardBookUi.Visible && _m.CardSortUi.Selected == 0, "点「排列」→ 整理名片（选中登录顺序）");
                Root.GetTexture().GetImage().SavePng("res://test/out_cardsort.png");
                _m.CardSortUi.Pick(1); _m.CardSortUi.Ok(); _st = 2; _f = 0; break;
            case 2 when _f == 20:
                Check(_m.Cfg.CardSort == 1 && _m.CardBookUi.Visible && !_m.CardSortUi.Visible, $"確定 → 存等级顺序（{_m.Cfg.CardSort}）、回名片簿");
                return End();
        }
        return _f > 2000;
    }
}
