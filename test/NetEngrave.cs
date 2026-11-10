using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>刻印（220）：技能 → 選擇物品双击剑 →「刻印」→ 刻印窗口打字 →「刻印」→ sM 栏 招式 格 文字 → 剑的刻字 = 文字、结果"的刻印成功了。"。测完把技能还原。</summary>
public partial class NetEngrave : SceneTree
{
    int _f, _st, _sw = -1; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_engrave_{n}.png");
    // 收尾：先把技能还原，等服务端处理完再退出（马上退出的话还原指令会丢）
    bool End() { (_m.Session as NetworkSession)?.Say("/dolua gm_kakuin_undo.lua"); _st = 98; _f = 0; return false; }
    bool Quit() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    SkillInfo? Sk(NetworkSession s) => s.Player.Skills.FirstOrDefault(k => k.SkillId == 220);
    ItemStack? Sword(NetworkSession s) => s.Player.Items.FirstOrDefault(i => _sw >= 0 ? i.Slot == _sw : i.Slot >= 8 && i.Name == "铜剑");
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession; var p = _m.ProdUi;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                foreach (var c in new[] { "/dolua gm_kakuin.lua", "/dolua gm_heal.lua", "/giveItem 0 1 1 1" }) s.Say(c);
                _st = 1; _f = 0; break;
            case 1 when _f > 90 && Sk(s!) is { Techs.Count: > 0 } && Sword(s!) != null:
            {
                var sk = Sk(s!)!; _sw = Sword(s!)!.Slot;
                _m.SkillWin.ShowDetail(s!.Player.Skills.IndexOf(sk)); _m.SkillWin.TechChosen?.Invoke(sk, 0);
                _st = 2; _f = 0; break;
            }
            case 1 when _f > 600: Check(false, $"准备失败：技能 {string.Join(",", s!.Player.Skills.Select(k => k.Name))}，剑 {Sword(s!)?.Name}"); return End();
            case 2 when _f == 20:
                Check(p.ItemSkill.Visible && p.ItemSkill.Message == "请选择要刻印的道具。", $"選擇物品：{p.ItemSkill.Message}");
                p.ChooseItem(_sw); p.ExecuteItemSkill(); _st = 3; _f = 0; break;
            case 3 when _f == 20:
                Check(p.Engrave.Visible && !p.ItemSkill.Visible, "按「刻印」→ 刻印窗口");
                p.Engrave.Text = "haoabc"; Shot("1_window"); p.DoEngrave(p.Engrave.Text); _st = 4; _f = 0; break;
            case 4 when p.Result.Visible || _f > 600:
                Check(p.Result.Visible && p.Result.Text.Contains("的刻印成功了"), $"结果：{p.Result.Text}");
                Check(Sword(s!)?.Engraved == "haoabc", $"剑上刻的字：「{Sword(s!)?.Engraved}」");
                Shot("2_result"); p.CloseAll(); return End();
        }
        if (_st == 98 && _f == 120) return Quit();
        return _f > 3000;
    }
}
