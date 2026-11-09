using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 联网摆摊（界面 = test1 hao，另开一个会话 test3 nettest3 当顾客/摊主，都在法兰城 1000）：
///  A. hao 站到 (242,101)（test3 在 (242,100)）→ 右上摆摊按钮开窗口 → 给一瓶药定价 7 → 擺攤 → Tmrx 0，
///     test3 看到 NI …|200|0|1（头上挂牌）→ test3 浏览（ElVN）看到这瓶药 7 G → 买（wAo 格）→ Yx 0，hao 少一瓶、多 7 金币 → hao 撤攤。
///  B. test3 把买到的药摆出来卖 9 → hao 点 test3（界面上 = 浏览）→ 窗口进浏览模式 → 双击 → 确认购买 → 药回来、少 9 金币。
/// </summary>
public partial class NetStall : SceneTree
{
    int _f, _st; Main _m = null!; NetworkSession _b = null!; readonly List<string> _r = new();
    long _gold0; int _cnt0, _slot; int _haoId;
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_stall_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); try { if (_b.Stalling) _b.StallEnd(); _b.Dispose(); } catch { } File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    int Potions(NetworkSession s) => s.Player.Items.Where(i => i.ItemId == 15605).Sum(i => i.Count);
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        var maps = MapData.IndexServerMapDir("/home/claude/wine/cgmsv/data/map");
        _b = new NetworkSession(new NetworkOptions { Host = "127.0.0.1", Port = 9030, CdKey = "test3", Account = "test3", Password = "123456", NewCharName = "nettest3", ResolveMap = f => maps.TryGetValue(f, out var m) ? m.Path : null });
        _b.Start();
    }
    public override bool _Process(double d)
    {
        _f++;
        _b.Poll();
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _b.InGame && _f > 90:
                s.Say("/giveItem 15605 3 1 1"); s.Say("/dolua gm_givegold.lua nettest3 50"); s.Say($"/warp 1000 0 {_b.X} {_b.Y + 1}"); _st = 1; _f = 0; break;
            case 1 when _f > 120 && Potions(s!) > 0 && s!.Y == _b.Y + 1:
            {
                _haoId = 0;
                _cnt0 = Potions(s); _gold0 = s.Player.Gold;
                _slot = s.Player.Items.First(i => i.ItemId == 15605).Slot;
                _m.ToggleStall();
                Check(_m.StallUi.Visible && !_m.StallUi.BrowseMode, "右上摆摊按钮 → 自己的摆摊窗口");
                _m.StallUi.TestPrice(_slot, 7);
                Check(_m.StallUi.ItemPrices.TryGetValue(_slot, out var pr) && pr == 7, $"背包格 {_slot} 定价 7");
                _st = 2; _f = 0; break;
            }
            case 1 when _f > 600: Check(false, $"准备失败：药 {Potions(s!)} 位置 ({s!.X},{s.Y}) test3 ({_b.X},{_b.Y})"); return End();
            case 2 when _f == 20:
                Shot("1_setup");
                _m.StallUi.TestStart(); _st = 3; _f = 0; break;
            case 3 when s!.Stalling && _b.Stalls.Count > 0 || _f > 300:
                Check(s.Stalling, "擺攤 → Tmrx 0");
                _haoId = _b.Objects.FirstOrDefault(o => o.X == s.X && o.Y == s.Y)?.Id ?? 0;
                Check(_haoId != 0 && _b.IsStall(_haoId), $"旁人看到摆摊（NI …|200|0|1）：{_b.Stalls.Count} 个摊");
                _b.StallBrowse(_haoId); _st = 4; _f = 0; break;
            case 4 when _b.Browsing != null || _f > 300:
            {
                var v = _b.Browsing;
                Check(v != null && v.Items.Count == 1 && v.Items[0].Price == 7 && v.Items[0].Item.ItemId == 15605, $"浏览：{v?.Items.Count} 件，{string.Join("、", v?.Items.Select(x => $"{x.Item.Name}×{x.Item.Count} {x.Price}G") ?? Array.Empty<string>())}，描述「{v?.Desc}」");
                if (v == null || v.Items.Count == 0) return End();
                _b.StallBuy(v.Items[0].Item.Slot); _st = 5; _f = 0; break;
            }
            case 5 when Potions(s!) < _cnt0 && s!.Player.Gold == _gold0 + 7 || _f > 300:
                Check(Potions(s!) < _cnt0 && s!.Player.Gold == _gold0 + 7, $"被买走：药 {_cnt0} → {Potions(s!)}，金币 {_gold0} → {s!.Player.Gold}");
                Check(Potions(_b) > 0 && _b.Browsing!.Items.Count == 0, $"顾客买到（Yx 0），摊上那格清掉");
                _b.CloseStallBrowse();
                Shot("2_sold");
                s.StallEnd(); _st = 6; _f = 0; break;
            case 6 when !s!.Stalling && !_b.IsStall(_haoId) || _f > 300:
                Check(!s!.Stalling && !_b.IsStall(_haoId), "撤攤 → KXhH 0，旁人那边牌子没了");
                _m.StallUi.Close();
                _cnt0 = Potions(s); _gold0 = s.Player.Gold;
                var mine = _b.Player.Items.First(i => i.ItemId == 15605);
                _b.StallStart("便宜药水", new Dictionary<int, int> { [mine.Slot] = 9 }, new Dictionary<int, int>());
                _st = 7; _f = 0; break;
            case 7 when _b.Stalling && s!.Stalls.Count > 0 && _f > 30 || _f > 300:
            {
                var npc = s!.NpcsOnCurrentMap().FirstOrDefault(n => s.IsStall(n.Id));
                Check(_b.Stalling && npc != null, $"test3 摆摊，hao 看到：{npc?.Name}");
                if (npc == null) return End();
                _m.TalkOrApproach(npc); _st = 8; _f = 0; break;
            }
            case 8 when _m.StallUi.Visible && _m.StallUi.BrowseMode || _f > 300:
                Check(_m.StallUi.Visible && _m.StallUi.BrowseMode && s!.Browsing!.Items.Count == 1 && s.Browsing.Desc == "便宜药水", $"点摆摊的人 → 浏览窗口（{s!.Browsing?.Items.Count} 件，「{s.Browsing?.Desc}」）");
                _st = 9; _f = 0; break;
            case 9 when _f == 20:
                Shot("3_browse");
                _m.StallUi.TestBuy(0); _st = 10; _f = 0; break;
            case 10 when _f == 20:
                Check(_m.StallUi.Confirming, "确认购买对话框");
                Shot("4_confirm");
                _m.StallUi.TestConfirm(); _st = 11; _f = 0; break;
            case 11 when Potions(s!) > _cnt0 && s!.Player.Gold == _gold0 - 9 || _f > 300:
                Check(Potions(s!) > _cnt0 && s!.Player.Gold == _gold0 - 9, $"买到：药 {_cnt0} → {Potions(s!)}，金币 {_gold0} → {s!.Player.Gold}");
                _st = 12; _f = 0; break;
            case 12 when _f == 20:
                Shot("5_bought");
                _m.StallUi.Close(); _b.StallEnd(); _st = 13; _f = 0; break;
            case 13 when !_b.Stalling || _f > 200:
                Check(!_b.Stalling && !_m.StallUi.Visible, "收尾：test3 收摊、窗口关掉");
                return End();
        }
        return _f > 5000;
    }
}
