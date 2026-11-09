using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Server;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 鉴定 / 修理 / 急救（test1）：学会技能、拿一把没鉴定的铜剑（"单手剑？"）→ 鉴定 → 名字变铜剑；修理铜剑（没受损 → 失败）；
/// 自己受伤 30 → 急救 → 选自己 → 回复生命力。
/// </summary>
public partial class NetProduce2 : SceneTree
{
    int _f, _st, _hp0, _tries; Main _m = null!; NetworkSession _b = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_prod2_{n}.png");
    bool End() { try { _b.Dispose(); } catch { } GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        var maps = CgPort.Core.MapData.IndexServerMapDir("/home/claude/wine/cgmsv/data/map");
        _b = new NetworkSession(new NetworkOptions { Host = "127.0.0.1", Port = 9030, CdKey = "test3", Account = "test3", Password = "123456", NewCharName = "nettest3", ResolveMap = f => maps.TryGetValue(f, out var m) ? m.Path : null });
        _b.Start();
    }
    SkillInfo? Sk(NetworkSession s, int id) => s.Player.Skills.FirstOrDefault(k => k.SkillId == id);
    int _sw = -1;
    ItemStack? Sword(NetworkSession s) => _sw >= 0 ? s.Player.Items.FirstOrDefault(i => i.Slot == _sw) : s.Player.Items.FirstOrDefault(i => i.ItemId == 0 && i.Name.EndsWith("？"));
    public override bool _Process(double d)
    {
        _f++; _b.Poll();
        var s = _m.Session as NetworkSession; var p = _m.ProdUi;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_clear.lua", "/dolua gm_prod.lua", "/dolua gm_heal.lua", "/giveItem 0 1 1 0" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f > 60 && Sk(s!, 219) is { Techs.Count: > 0 } && Sword(s!) != null && s!.Player.Skills.Count > 0:
            {
                var sk = Sk(s!, 219)!; var sw = Sword(s!)!; _sw = sw.Slot;
                Check(sk.Type == 2 && Sk(s!, 223)?.Type == 3, $"技能种类：鉴定 {sk.Type}、急救 {Sk(s!, 223)?.Type}；没鉴定的剑叫「{sw.Name}」");
                _m.SkillWin.ShowDetail(s!.Player.Skills.IndexOf(sk)); _m.SkillWin.TechChosen?.Invoke(sk, 0);
                _st = 2; _f = 0; break;
            }
            case 1 when _f > 600: Check(false, $"准备失败：{string.Join(",", s!.Player.Skills.Select(k => k.Name))} 剑 {Sword(s!)?.Name}"); return End();
            case 2 when _f == 20:
                Check(p.ItemSkill.Visible && p.ItemSkill.Message == "请选择要鉴定的道具。", $"選擇物品窗口：{p.ItemSkill.Message}");
                p.ChooseItem(Sword(s!)!.Slot);
                Check(p.TargetSlot == Sword(s!)!.Slot && p.ItemSkill.Message.StartsWith("需消耗10点"), $"双击剑：{p.ItemSkill.Message}");
                Shot("1_select"); p.ExecuteItemSkill(); _st = 3; _f = 0; break;
            case 3 when p.Result.Visible || _f > 1200:
                Check(p.Result.Visible && p.LastResult?.Type == 2 && (p.LastResult.Ok ? Sword(s!)?.Name == "铜剑" : p.Result.Text.Contains("失败")), $"鉴定结果（会随机失败）：{p.Result.Text}；剑现在叫「{Sword(s!)?.Name}」");
                Shot("2_appraised");
                _m.SkillWin.TechChosen?.Invoke(Sk(s!, 217)!, 0); _st = 4; _f = 0; break;
            case 4 when _f == 20:
                Check(p.ItemSkill.Visible && p.ItemSkill.Message == "请选择要修理的道具。", $"修理：{p.ItemSkill.Message}");
                p.ChooseItem(Sword(s!)!.Slot); p.ExecuteItemSkill(); _st = 5; _f = 0; break;
            case 5 when p.Result.Visible || _f > 1200:
                Check(p.Result.Visible && p.LastResult?.Type == 2, $"修理结果（没受损）：{p.Result.Text}");
                // 顾客 test3 站到旁边、受伤、加入 hao 的队伍（急救对自己没用：官方单人时 ND 是空的）
                if (s!.InParty) s.PartyButton();
                if ((s.Flags & 1) == 0) s.ToggleFlag(1);   // 开「隊」才能让别人加入
                s.Say($"/warp {_b.CurrentMap!.MapId} 0 {_b.X - 1} {_b.Y}"); _st = 51; _f = 0; break;
            case 51 when _b.X == s!.X + 1 && _b.Y == s.Y && _f > 60 || _f > 400:
                s!.Say($"/dolua gm_hurtname.lua {_b.Player.Name} 30"); _b.Face(-1, 0); _b.PartyButton(); _st = 52; _f = 0; break;
            case 52 when s!.InParty && _f > 30 || _f > 300:
                Check(s!.InParty, $"test3 加入队伍：{string.Join("、", s.ItemMembers())}");
                _st = 6; _f = 0; break;
            case 6 when _f == 20:
                _hp0 = _b.Player.Hp;
                p.CloseAll();
                _m.SkillWin.TechChosen?.Invoke(Sk(s, 223)!, 0); _st = 7; _f = 0; break;
            case 7 when _m.ItemMemberUi.Visible || _f > 300:
                Check(_m.ItemMemberUi.Visible, "急救 → 请选择对象（队员）");
                if (!_m.ItemMemberUi.Visible) return End();
                _m.ItemMemberUi.Visible = false; s!.SelectItemMember(Math.Max(0, s.ItemMembers().ToList().IndexOf(_b.Player.Name))); _st = 8; _f = 0; break;
            case 8 when _m.ItemTargetUi.Visible || _f > 300:
                Check(_m.ItemTargetUi.Visible && _m.ItemTargetUi.Shown.Count > 0, $"请选择使用对象：{string.Join("、", _m.ItemTargetUi.Shown.Select(t => $"{t.Name} 受伤{t.Injury} {t.Hp}/{t.MaxHp}"))}");
                if (!_m.ItemTargetUi.Visible) return End();
                Shot("3_treat_target");
                _m.ItemTargetUi.TestPick(0); _st = 9; _f = 0; break;
            case 9 when (p.Result.Visible && p.LastResult?.Type == 3 && p.LastResult.Ok == false) && _tries < 4 && _f > 20:
                // 急救会随机失败（服务端 "接受hao的急救，但是却失败了。"），再试
                _tries++; Check(p.Result.Text.Contains("失败"), $"第 {_tries} 次急救失败：{p.Result.Text}");
                p.Retry(); _st = 7; _f = 0; break;
            case 9 when p.Result.Visible && p.LastResult?.Type == 3 && (p.LastResult.Ok || _tries >= 4) || _f > 300:
                Check(p.LastResult?.Type == 3 && p.LastResult.Ok && _b.Player.Hp > _hp0 && p.Result.Text.Contains("nettest3") && p.Result.Text.Contains("回复了"), $"急救结果：{p.Result.Text}（test3 HP {_hp0} → {_b.Player.Hp}）");
                Shot("4_treated"); p.CloseAll(); s!.Say("/dolua gm_heal.lua"); if (s.InParty) s.PartyButton();
                _b.Dispose();
                return End();
        }
        return _f > 5000;
    }
}
