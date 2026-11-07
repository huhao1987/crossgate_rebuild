using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 联网交易（配合官方客户端：bob 在法兰城、开着「易」；自己站到他面前）：
/// 开「易」→ 站到 (241,101) 面向 bob → 右上「交易」→ "请选择对象"点 bob → 交易窗口 → 双击选第 8 格道具 + 金币 2 →「開啓」
/// → 外部脚本看到 /tmp/trade_ready 让 bob 也开启、确认 → 自己「確認交易」→ 交易完成，道具和金币变了。
/// </summary>
public partial class NetTrade : SceneTree
{
    (int, int) _face, _want; int _try;
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    int _f, _st; long _gold0; int _slotItem; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_trade_{n}.png");
    public override void _Initialize()
    {
        foreach (var f in new[] { "/tmp/trade_ready", "/tmp/trade_confirm" }) File.Delete(f);
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
                _try = 0; _st = 100; _f = 0; break;
            // 依次试 bob 周围 8 格（有的格子不能站，传送不过去），站上去后面向他
            case 100 when _f == 1:
            {
                var bob = s!.NpcsOnCurrentMap().First(n => n.Name == "bob");
                // 先试 bob 面前那格（官方客户端收到交易请求时，对方要站在自己面前才会弹窗）
                var (fx, fy) = CgPort.Core.Directions.Deltas[(((bob.Dir % 8) + 8) % 8 + _try) % 8];
                _face = (-fx, -fy); _want = (bob.X + fx, bob.Y + fy);
                s.Say($"/warp 1000 0 {_want.Item1} {_want.Item2}"); break;
            }
            case 100 when _f == 60:
                if ((s!.X, s.Y) == _want) { _st = 1; _f = 50; }
                else if (++_try >= 8) { Check(false, "bob 周围没有能站的格子"); return End(); }
                else _f = 0;
                break;
            case 1 when _f == 60:
                if ((s!.Flags & 32) == 0) h.TestClickFlag(4);
                _st = 2; _f = 0; break;
            case 2 when _f == 40:
                Check((s!.Flags & 32) != 0 && h.FlagLit(4), $"「易」开着（开关 {s.Flags}）");
                s.Face(_face.Item1, _face.Item2); _st = 3; _f = 0; break;
            case 3 when _f == 20:
                h.TestPressButton(22589); _st = 4; _f = 0; break;
            case 4 when _m.TradePickerUi.Visible || _f > 180:
                Check(_m.TradePickerUi.Visible && s!.TradeCandidates.Contains("bob"), $"点「交易」弹出\"请选择对象\"：{string.Join(",", s!.TradeCandidates)}");
                Shot("1_picker");
                _m.TradePickerUi.TestPick(s.TradeCandidates.ToList().IndexOf("bob")); _st = 5; _f = 0; break;
            case 5 when _f == 40:
                Check(s!.Trade?.Partner == "bob" && _m.TradeUi.Visible, $"交易窗口开了，对象 {s.Trade?.Partner}");
                var it = s.Player.Items.OrderBy(x => x.Slot).FirstOrDefault();
                _slotItem = it?.Slot ?? -1; _gold0 = s.Player.Gold;
                if (it != null) _m.TradeUi.TestPick(it.Slot);
                _m.TradeUi.TestGold(2);
                Shot("2_window");
                _m.TradeUi.DoOpen(); _st = 6; _f = 0; break;
            case 6 when _f == 30:
                Check(s!.Trade?.IOpened == true, $"「開啓」：给 {_slotItem} 格 {s.Player.Items.FirstOrDefault(x => x.Slot == _slotItem)?.Name} + 2G");
                File.WriteAllText("/tmp/trade_ready", "1"); _st = 7; _f = 0; break;
            case 7 when s!.Trade?.TheyOpened == true || _f > 3000:
                Check(s.Trade?.TheyOpened == true && _m.TradeUi.StatusText == "对方交易物品已选取", $"对方开启了，状态栏：{_m.TradeUi.StatusText}");
                _st = 8; _f = 0; break;
            case 8 when _f == 30:
                Shot("3_both_open");
                _m.TradeUi.DoConfirm(); File.WriteAllText("/tmp/trade_confirm", "1"); _st = 9; _f = 0; break;
            case 9 when s!.Trade == null && _f > 30 || _f > 900:
                Check(s!.Trade == null && !_m.TradeUi.Visible, "双方确认后窗口关了");
                _st = 10; _f = 0; break;
            case 10 when _f == 60:
                Check(_m.Chat.Lines.Any(l => l.Contains("交易完成")), $"聊天栏：{string.Join(" / ", _m.Chat.Lines)}");
                Check(s!.Player.Gold == _gold0 - 2, $"金币 {_gold0} → {s.Player.Gold}");
                Check(s.Player.Items.All(x => x.Slot != _slotItem), $"第 {_slotItem} 格的道具给出去了");
                Shot("4_done");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 4000;
    }
}
