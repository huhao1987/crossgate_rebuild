using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Server;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 联网银行（test1）：清背包、GM 给一张卡片 → 传送到法兰银行 (1121) → 跟「银行员工」说话 → 银行窗口（BZ 类型 23）
/// → 存道具 → 取回 → 存 10 金币 → 提 10 金币 → 翻到第 2 页 → 宠物页存一只再取回 → 关窗口。
/// </summary>
public partial class NetBank : SceneTree
{
    int _f, _st; long _gold0, _bank0; int _petSlot = -1; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_bank_{n}.png");
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
        var b = s?.Bank;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/dolua gm_clear.lua"); s.Say("/giveItem 14848 1 1 1"); s.Say("/warp 1121 0 11 9"); _st = 1; _f = 0; break;
            case 1 when s!.CurrentMap?.MapId == 1121 && s.NpcsOnCurrentMap().Any(n => n.Name == "银行员工") && s.Player.Items.Any(i => i.ItemId == 14848) && _f > 30:
                _m.TalkOrApproach(s.NpcsOnCurrentMap().First(n => n.Name == "银行员工")); _st = 2; _f = 0; break;
            case 1 when _f > 600:
                Check(false, $"没到银行：地图 {s!.CurrentMap?.MapId} 卡片 {s.Player.Items.Any(i => i.ItemId == 14848)}"); return End();
            case 2 when b != null && _m.BankUi.Visible || _f > 400:
                Check(b != null && _m.BankUi.Visible, $"跟银行员工说话 → 银行窗口（第 {b?.Page} 页，{b?.Items.Count(x => x != null)} 件，{b?.Pets.Count(x => x != null)} 只，存款 {b?.Gold}）");
                if (b == null) return End();
                _st = 3; _f = 0; break;
            case 3 when _f == 20:
            {
                Shot("1_items");
                var it = s!.Player.Items.First(i => i.ItemId == 14848);
                Check(s.BankDeposit(it.Slot), $"存：背包格 {it.Slot} {it.Name}");
                _st = 4; _f = 0; break;
            }
            case 4 when b!.Items.Any(x => x?.Name == "迷你蝙蝠的卡片") && !s!.Player.Items.Any(i => i.ItemId == 14848) || _f > 300:
            {
                var k = Array.FindIndex(b.Items, x => x?.Name == "迷你蝙蝠的卡片");
                Check(k >= 0 && !s!.Player.Items.Any(i => i.ItemId == 14848), $"存进保险箱第 {k} 格，背包里没了");
                _m.BankUi.Refresh(); _st = 5; _f = 0; break;
            }
            case 5 when _f == 20:
                Shot("2_deposited");
                Check(s!.BankWithdraw(Array.FindIndex(b!.Items, x => x?.Name == "迷你蝙蝠的卡片")), "取");
                _st = 6; _f = 0; break;
            case 6 when s!.Player.Items.Any(i => i.ItemId == 14848) && b!.Items.All(x => x?.Name != "迷你蝙蝠的卡片") || _f > 300:
                Check(s.Player.Items.Any(i => i.ItemId == 14848), $"取回背包（格 {s.Player.Items.FirstOrDefault(i => i.ItemId == 14848)?.Slot}）");
                _gold0 = s.Player.Gold; _bank0 = b!.Gold;
                _m.BankUi.TestTab(2); _m.BankUi.TestInput(10); _st = 7; _f = 0; break;
            case 7 when _f == 20:
                Shot("3_deposit_gold");
                s!.BankGold(true, 10); _st = 8; _f = 0; break;
            case 8 when b!.Gold == _bank0 + 10 && s!.Player.Gold == _gold0 - 10 || _f > 300:
                Check(b.Gold == _bank0 + 10 && s!.Player.Gold == _gold0 - 10, $"存 10 金币：存款 {_bank0} → {b.Gold}，身上 {_gold0} → {s!.Player.Gold}");
                _m.BankUi.TestTab(3); _m.BankUi.TestInput(10); _st = 9; _f = 0; break;
            case 9 when _f == 20:
                Shot("4_withdraw_gold");
                s!.BankGold(false, 10); _st = 10; _f = 0; break;
            case 10 when b!.Gold == _bank0 && s!.Player.Gold == _gold0 || _f > 300:
                Check(b.Gold == _bank0 && s!.Player.Gold == _gold0, $"提 10 金币：存款 {b.Gold}，身上 {s!.Player.Gold}");
                _m.BankUi.TestTab(0); s.BankPage(2); _m.BankUi.Refresh(); _st = 11; _f = 0; break;
            case 11 when _f == 40:
                Check(b!.Page == 2, $"翻到第 2 页（{b.Items.Count(x => x != null)} 件）");
                Shot("5_page2");
                s!.BankPage(1); _m.BankUi.TestTab(1); _st = 12; _f = 0; break;
            case 12 when _f == 40:
            {
                Shot("6_pets");
                var pet = s!.Player.Pets.Where(p => p.State != PetInfo.Battle).OrderByDescending(p => p.Slot).FirstOrDefault();
                if (pet == null) { Check(true, "没有可存的宠物，跳过"); _st = 15; _f = 0; break; }
                _petSlot = pet.Slot;
                Check(s.BankDepositPet(pet.Slot), $"存宠物 {pet.Name}（栏 {pet.Slot}）");
                _st = 13; _f = 0; break;
            }
            case 13 when b!.Pets.Any(x => x != null) && s!.Player.Pets.All(p => p.Slot != _petSlot) || _f > 300:
            {
                var k = Array.FindIndex(b.Pets, x => x != null);
                Check(k >= 0 && s!.Player.Pets.All(p => p.Slot != _petSlot), $"宠物进了保险箱第 {k} 只：{(k >= 0 ? b.Pets[k]!.OrigName : "")}");
                _m.BankUi.Refresh(); _st = 14; _f = 0; break;
            }
            case 14 when _f == 20:
                Shot("7_pet_deposited");
                s!.BankWithdrawPet(Array.FindIndex(b!.Pets, x => x != null)); _st = 15; _f = 0; break;
            case 15 when (_petSlot < 0 || s!.Player.Pets.Any(p => p.Slot == _petSlot)) && _f > 30 || _f > 300:
                if (_petSlot >= 0) Check(s!.Player.Pets.Any(p => p.Slot == _petSlot), "宠物取回来了");
                s!.CloseBank(); _st = 16; _f = 0; break;
            case 16 when _f == 10:
                Check(!_m.BankUi.Visible, "关窗口");
                s!.Say("/warp 1000 0 242 100");
                return End();
        }
        return _f > 5000;
    }
}
