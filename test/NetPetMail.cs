using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 宠物邮件（测试自己开第二个连接当 bob = test2；先面对面交换名片；/dolua gm_petmail.lua 给宠物加「宠物邮件」技能）：
/// 名片「郵件」→ 選擇郵件 → 寵物郵件 → 选宠物 → 编辑 + 选道具 → 送出（rMsv）→ 出发效果；
/// 然后 bob 回寄一封宠物邮件 → 到达效果 + 宠物出现 + 聊天提示 → 邮件记录。
/// </summary>
public partial class NetPetMail : SceneTree
{
    NetworkSession _b = null!; bool _bSent;
    int _f, _st, _bob = -1, _pet = -1, _item = -1, _fx0; string _since = ""; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_petmail_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    static string Log(string since) => string.Join("\n", Directory.GetFiles("/home/claude/wine/cgmsv/log", "packet_*.txt").OrderBy(x => x).TakeLast(1)
        .SelectMany(p => CgPort.Core.Text.Gbk.Decode(File.ReadAllBytes(p)).Split('\n')).Where(l => l.Length > 8 && string.CompareOrdinal(l[..8], since) >= 0));
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        var maps = CgPort.Core.MapData.IndexServerMapDir("/home/claude/wine/cgmsv/data/map");
        _b = new NetworkSession(new NetworkOptions { Host = "127.0.0.1", Port = 9030, CdKey = "test2", Account = "test2", Password = "123456", ResolveMap = f => maps.TryGetValue(f, out var m) ? m.Path : null });
        _b.Start();
    }
    public override bool _Process(double d)
    {
        _f++; _b.Poll();
        var s = _m.Session as NetworkSession;
        // bob：hao 的宠物出发以后，回寄一封（第一只宠物 + 背包里第一件道具）
        if (_st == 7 && !_bSent && _f > 120)
        {
            _bSent = true;
            int ci = Array.FindIndex(_b.Cards.ToArray(), c => c?.Name == "hao");
            var bp = _b.Player.Pets.OrderBy(p => p.Slot).FirstOrDefault(p => p.Name.Length > 0);
            var bi = _b.Player.Items.Where(i => i.Slot >= 8).OrderBy(i => i.Slot).FirstOrDefault();
            GD.Print($"BOB 寄宠物邮件：名片 {ci} 宠物 {bp?.Slot} 道具 {bi?.Slot}");
            if (ci >= 0 && bp != null && bi != null) _b.SendPetMail(ci, bp.Slot, bi.Slot, "from bob");
        }
        switch (_st)
        {
            // 准备：bob 进游戏、给宠物加宠物邮件；两人在法兰城 238,101 / 239,101 面对面开「名」交换名片
            case 0 when s?.InGame == true && s.CurrentMap != null && _b.InGame && _b.CurrentMap != null && _f > 90:
                if (_b.InParty) _b.PartyButton();
                foreach (var c in new[] { "/dolua gm_addpet.lua", "/dolua gm_petmail.lua", "/warp 1000 0 239 101" }) _b.Say(c);
                s.Say("/dolua gm_addpet.lua"); s.Say("/dolua gm_petmail.lua"); s.Say("/warp 1000 0 238 101"); _st = 50; _f = 0; break;
            case 50 when _f == 90:
                if ((s!.Flags & 16) == 0) s.ToggleFlag(16);
                if ((_b.Flags & 16) == 0) _b.ToggleFlag(16);
                s.Face(1, 0); _b.Face(-1, 0); _st = 51; _f = 0; break;
            case 51 when _f == 40:
                if (!s!.Cards.Any(c => c?.Name == "bob")) s.ExchangeCard();
                _st = 52; _f = 0; break;
            case 52 when _f == 60:
                if (!_b.Cards.Any(c => c?.Name == "hao")) _b.ExchangeCard();
                _st = 1; _f = 0; break;
            case 1 when _f == 90:
                _bob = Array.FindIndex(s!.Cards.ToArray(), c => c?.Name == "bob");
                var pet = s.Player.Pets.FirstOrDefault(p => p.Slot is >= 0 and < 5 && NetworkSession.CanPetMail(p));
                Check(_bob >= 0 && pet != null, $"名片里有 bob（第 {_bob} 张），有会宠物邮件的宠物 {pet?.DisplayName}（第 {pet?.Slot} 栏，状态 {pet?.State}）");
                if (_bob < 0 || pet == null) return End();
                _pet = pet.Slot;
                if (pet.State == 3) s.SetPetState(_pet, 0);
                _m.CardBookUi.WriteMail?.Invoke(_bob); _st = 2; _f = 0; break;
            case 2 when _f == 20:
                Check(_m.MailTypeUi.Visible && !_m.CardBookUi.Visible, "名片「郵件」先弹「選擇郵件」");
                Shot("1_type"); _m.MailTypeUi.Pet?.Invoke(); _m.MailTypeUi.Visible = false; _st = 3; _f = 0; break;
            case 3 when _f == 20:
                Check(_m.PetMailPetsUi.Visible, "寵物郵件 → 宠物表");
                Shot("2_pets"); _m.PetMailPetsUi.TestPick(_pet); _st = 4; _f = 0; break;
            case 4 when _f == 20:
            {
                Check(_m.PetMailEditUi.Visible && _m.PetMailItemsUi.Visible && _m.PetMailEditUi.PetSlot == _pet, "选宠物 → 编辑宠物邮件 + 選擇傳送物品");
                var bad = s!.Player.Items.FirstOrDefault(i => i.Slot >= 8 && !i.CanPetMail);
                if (bad != null) { _m.PetMailItemsUi.Pick(bad.Slot); Check(_m.PetMailEditUi.ItemSlot == -1, $"不能寄的道具（{bad.Name}）选不上"); }
                var it = s.Player.Items.Where(i => i.Slot >= 8 && i.CanPetMail).OrderBy(i => i.Slot).FirstOrDefault();
                if (it != null) { _item = it.Slot; _m.PetMailItemsUi.Pick(it.Slot); }
                Check(_m.PetMailEditUi.ItemSlot == _item, $"选了道具 {it?.Name}（第 {_item} 格）");
                _m.PetMailEditUi.Text = "pet mail 测试";
                _st = 5; _f = 0; break;
            }
            case 5 when _f == 20:
                Shot("3_edit"); _since = "00:00:00"; _fx0 = _m.PetMailFxCount;
                _m.PetMailEditUi.DoSend(); _st = 6; _f = 0; break;
            case 6 when _f == 25:
                Shot("4_depart"); break;
            case 6 when _f == 120:
            {
                var log = Log(_since);
                var line = log.Split('\n').LastOrDefault(l => l.Contains("\ttest1\trMsv")) ?? "";
                Check(line.Contains($"rMsv {_bob} {_pet} {_item} pet\\Smail\\S测试 0"), $"送出 {line[Math.Max(0, line.IndexOf("rMsv"))..]}");
                Check(_m.Chat.Lines.Any(l => l.Contains("把宠物邮件送给了bob")), $"聊天：{string.Join(" / ", _m.Chat.Lines.TakeLast(3))}");
                Check(_m.PetMailFxCount > _fx0, $"出发效果（lDuJ）放了 {_m.PetMailFxCount - _fx0} 个");
                Check(!s!.Player.Pets.Any(p => p.Slot == _pet && p.Name.Length > 0) && _m.CardBookUi.Visible, "宠物离开宠物栏，回到名片簿");
                _m.CardBookUi.Visible = false;
                GD.Print("WAIT_BOB");
                _fx0 = _m.PetMailFxCount; _st = 7; _f = 0; break;
            }
            case 7 when _m.PetMailFxCount > _fx0 || _f > 60 * 200:
                Check(_m.PetMailFxCount > _fx0, "bob 的宠物邮件到达效果");
                _st = 8; _f = 0; break;
            case 8 when _f == 30:
                Shot("5_arrive"); break;
            case 8 when s!.Mails.Any(m => m.Incoming && m.IsPet) && s.GroundItems.Any(g => g.Pet) && _f > 200:
                Shot("6_arrived");
                var mail = s.Mails.Last(m => m.Incoming && m.IsPet);
                Check(_m.Chat.Lines.Any(l => l.Contains("收到由bob送来的宠物邮件")), $"收到宠物邮件 {mail.PetName} Lv{mail.PetLevel}「{mail.Text}」道具图 {mail.ItemImage}");
                Check(_m.GroundShown > 0, "效果放完宠物站在旁边");
                _m.HudSkin.MailClicked?.Invoke(); _st = 9; _f = 0; break;
            case 8 when _f > 60 * 60:
                Check(false, "没收到宠物邮件"); return End();
            case 9 when _f == 20:
                Check(_m.MailRecordUi.Visible, "左上「郵件」→ 直接开邮件记录");
                Shot("7_record"); return End();
        }
        return _f > 60 * 300;
    }
}
