using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>联网：状态窗口（状态/明细）、物品窗口（双击穿装备、拿起放回背包脱下）。test1 先用 GM 拿一把铜剑。</summary>
public partial class NetChar : SceneTree
{
    int _f, _st; Main _m = null!; readonly List<string> _r = new(); int _swordSlot = -1, _free = -1, _vit;
    void Check(bool ok, string n) => _r.Add((ok ? "PASS " : "FAIL ") + n);
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_char_{n}.png");
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
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
                // 先把装备栏的武器脱掉（上次测试可能留着），再给一把铜剑

                s.Say("/dolua gm_clear.lua");   // 背包满了 GM 给不了东西：先清最后 4 格（云端测试服务端的脚本）
                _st = 10; _f = 0; break;
            case 10 when _f == 40: s!.Say("/giveItem 0 1 1 1"); _st = 1; _f = 0; break;
            case 1 when _f == 90:
                Check(s!.Player.Stats.Agility > 0 && s.Player.Job.Length > 0 && s.Player.Stats.NextExp > 0, $"人物属性：{s.Player.Job} Lv{s.Player.Level} 敏捷{s.Player.Stats.Agility} 下一级{s.Player.Stats.NextExp}");
                _m.StatusWin.ShowTab(0); _st = 2; _f = 0; break;
            case 2 when _f == 15: Shot("status"); _m.StatusWin.ShowTab(1); _st = 3; _f = 0; break;
            case 3 when _f == 15:
                Shot("detail"); _m.StatusWin.Toggle();
                _swordSlot = s!.Player.Items.Where(i => i.Name == "铜剑").Select(i => i.Slot).DefaultIfEmpty(-1).Max();
                Check(_swordSlot >= 8, $"背包里有铜剑（格子 {_swordSlot}）");
                if (_swordSlot < 8) { GD.Print(string.Join("\n", _r)); return true; }
                _m.ItemWin.Toggle(); _st = 4; _f = 0; break;
            case 4 when _f == 15:
                Shot("items1");
                _m.ItemWin.TestHover(_swordSlot);
                Check(_m.ItemWin.TipText.StartsWith("铜剑") && _m.ItemWin.TipText.Contains("攻击") && !_m.ItemWin.TipText.Contains("$"), $"道具说明：{_m.ItemWin.TipText.Replace("\n", " / ")}");
                break;
            case 4 when _f == 25:
                Root.GetTexture().GetImage().SavePng("res://test/out_char_tip.png");
                _m.ItemWin.TestClick(_swordSlot, false); _m.ItemWin.TestClick(_swordSlot, true);   // 双击
                _st = 5; _f = 0; break;
            case 5 when _f == 90:
                Check(s!.Player.Equipment.Any(e => e.Slot == 2 && e.Name == "铜剑"), $"双击穿上：武器栏 = {s.Player.Equipment.FirstOrDefault(e => e.Slot == 2)?.Name}");
                Shot("items2");
                _free = Enumerable.Range(8, 20).Where(i => s.Player.Items.All(x => x.Slot != i)).DefaultIfEmpty(-1).First();
                _m.ItemWin.TestClick(2, false);
                Check(_m.ItemWin.Held == 2, "单击装备栏的剑 = 拿起");
                _m.ItemWin.TestClick(_free, false); _st = 6; _f = 0; break;
            case 6 when _f == 90:
                Check(!s!.Player.Equipment.Any(e => e.Slot == 2) && s.Player.Items.Any(i => i.Slot == _free && i.Name == "铜剑"), $"放到背包空格 {_free} = 脱下");
                Shot("items3");
                _m.ItemWin.Toggle();
                s.Say("/dolua gm_points.lua"); _st = 7; _f = 0; break;
            case 7 when _f == 90:
                Check(s!.Player.StatPoints == 3, $"GM 给 3 点 → 收到 kg：{s.Player.StatPoints}");
                _vit = s.Player.Stats.Vit;
                _m.StatusWin.ShowTab(1); _st = 8; _f = 0; break;
            case 8 when _f == 15:
                Shot("detail_points");
                _m.StatusWin.TestPlus(0); _st = 9; _f = 0; break;
            case 9 when _f == 90:
                Check(s!.Player.StatPoints == 2 && s.Player.Stats.Vit == _vit + 1, $"点体力 +1：体力 {_vit} → {s.Player.Stats.Vit}，剩 {s.Player.StatPoints} 点");
                Shot("detail_after");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 4000;
    }
}
