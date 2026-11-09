using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 物品窗口其余功能（test1）：双击药 → "请选择对象"（队伍成员）→ "请选择使用对象"（人/宠物）→ 用掉；
/// 拿起道具点地图 = 扔在地上 → 地上显示 → 点它捡回来；钱包 → 金幣窗口扔 100 → 地上一堆钱（图 27402）→ 捡回来；「整理」。
/// </summary>
public partial class NetItems2 : SceneTree
{
    int _f, _st, _slot, _potions; long _gold; Main _m = null!; readonly List<string> _r = new(); string _since = "";
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_items2_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    int Count(NetworkSession s) => s.Player.Items.Where(i => i.ItemId == 15605).Sum(i => Math.Max(1, i.Count));
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                _since = DateTime.Now.ToString("HH:mm:ss");
                s.Say("/dolua gm_clear.lua"); s.Say("/dolua gm_heal.lua"); s.Say("/warp 1000 0 238 104"); _st = 1; _f = 0; break;
            case 1 when _f == 90:
                s!.Say("/giveItem 15605 3 1 1"); _st = 2; _f = 0; break;
            case 2 when s!.Player.Items.Any(i => i.ItemId == 15605) && _f > 30:
                _slot = s.Player.Items.First(i => i.ItemId == 15605).Slot; _potions = Count(s);
                _m.ItemsUi.Visible = true; _m.ItemsUi.Refresh();
                _m.ItemsUi.TestClick(_slot, true); _st = 3; _f = 0; break;
            case 2 when _f > 600: Check(false, "没拿到药"); return End();
            case 3 when _m.ItemMemberUi.Visible || _f > 300:
                Check(_m.ItemMemberUi.Visible && !_m.ItemsUi.Visible, "双击药 → 服务端要选对象（LY），弹出\"请选择对象\"，物品窗口收起");
                Shot("1_member"); _m.ItemMemberUi.TestPick(0); _st = 4; _f = 0; break;
            case 4 when _m.ItemTargetUi.Visible || _f > 300:
                var t = _m.ItemTargetUi.Shown;
                Check(_m.ItemTargetUi.Visible && t.Count >= 1 && t[0].Self, $"选了自己 → \"请选择使用对象\"：{string.Join("，", t.Select(x => $"{x.Code}:{x.Name} Lv{x.Level} {x.Hp}/{x.MaxHp}"))}");
                Shot("2_target"); _m.ItemTargetUi.TestPick(0); _st = 5; _f = 0; break;
            case 5 when Count(s!) < _potions || _f > 300:
                Check(Count(s) == _potions - 1, $"对自己用了一瓶：{_potions} → {Count(s)}（{_m.Chat.Lines.LastOrDefault()}）");
                _m.ItemsUi.Visible = true; _m.ItemsUi.Refresh();
                _slot = s.Player.Items.First(i => i.ItemId == 15605).Slot;
                _m.ItemsUi.TestClick(_slot, false); _st = 6; _f = 0; break;
            case 6 when _f == 10:
                Check(_m.ItemsUi.Held == _slot, $"拿起第 {_slot} 格");
                _m.TestTileClick(s!.X - 2, s.Y); _st = 7; _f = 0; break;
            case 7 when s!.GroundItems.Any(g => !g.Gold) || _f > 300:
            {
                var g = s.GroundItems.FirstOrDefault(x => !x.Gold);
                Check(g != null && s.Player.Items.All(i => i.Slot != _slot) && _m.GroundShown > 0, $"点地图扔掉：地上 {g?.Name}×{g?.Count} 在 ({g?.X},{g?.Y})，画出来 {_m.GroundShown} 个");
                _st = 8; _f = 0; break;
            }
            case 8 when _f == 20:
            {
                Shot("3_ground");
                var g = s!.GroundItems.First(x => !x.Gold);
                _m.TestTileClick(g.X, g.Y); _st = 9; _f = 0; break;
            }
            case 9 when s!.Player.Items.Any(i => i.ItemId == 15605) && !s.GroundItems.Any(g => !g.Gold) || _f > 600:
                Check(s.Player.Items.Any(i => i.ItemId == 15605) && !s.GroundItems.Any(g => !g.Gold), $"点地上的药捡回来了（{Count(s)} 瓶，站在 {s.X},{s.Y}）");
                _gold = s.Player.Gold;
                _m.ItemsUi.Visible = true; _m.ItemsUi.Refresh(); _m.ItemsUi.Wallet?.Invoke(); _st = 10; _f = 0; break;
            case 10 when _f == 10:
                Check(_m.GoldUi.Visible && !_m.ItemsUi.Visible, "钱包 → 金幣窗口");
                _m.GoldUi.TestInput(100); _st = 11; _f = 0; break;
            case 11 when _f == 10:
                Shot("4_gold"); _m.GoldUi.TestConfirm(); _st = 12; _f = 0; break;
            case 12 when s!.GroundItems.Any(g => g.Gold) && s.Player.Gold == _gold - 100 || _f > 300:
            {
                var g = s.GroundItems.FirstOrDefault(x => x.Gold);
                Check(g != null && g.Count == 100 && s.Player.Gold == _gold - 100 && Main.GoldImage(g.Count) == 27402 && _m.ItemsUi.Visible,
                    $"扔 100 金：身上 {_gold} → {s.Player.Gold}，地上 {g?.Count} 金（图 {Main.GoldImage(g?.Count ?? 0)}），物品窗口回来了 {_m.ItemsUi.Visible}");
                _st = 13; _f = 0; break;
            }
            case 13 when _f == 20:
            {
                Shot("5_gold_ground");
                var g = s!.GroundItems.First(x => x.Gold);
                _m.TestTileClick(g.X, g.Y); _st = 14; _f = 0; break;
            }
            case 14 when s!.Player.Gold == _gold || _f > 600:
                Check(s.Player.Gold == _gold && !s.GroundItems.Any(g => g.Gold), $"钱捡回来了：{s.Player.Gold}");
                _m.ItemsUi.SortBag?.Invoke(); _st = 15; _f = 0; break;
            case 15 when _f == 60:
                var log = string.Join("\n", Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(x => x).TakeLast(1).SelectMany(File.ReadLines).Where(l => l.Contains("\ttest1\t")).TakeLast(300));
                Check(log.Contains("CUSTOMXB 0"), "「整理」发了 CUSTOMXB 0");
                return End();
        }
        return _f > 8000;
    }
}
