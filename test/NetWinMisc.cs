using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 本服 NPC 不会弹、只能用 GM 脚本（gm_win.lua 给自己弹任意窗口）测的窗口：
/// 交出物品入口 WN 30（「 交出物品 」「  取  消  」，取消只关）、埋起物品 WN 29（背包样子，旗标不对的格子点了不发包）、输入窗口 WN 11（下面有输入框）。
/// </summary>
public partial class NetWinMisc : SceneTree
{
    int _f, _st; Main _m = null!; CgPort.Server.DialogPage? _page; bool _hook;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_winmisc_{n}.png");
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
        if (s != null && !_hook) { _hook = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                _page = null; s.Say("/dolua gm_win.lua 30 0 14003|交换银|x|请把银子交给我。"); _st = 1; _f = 0; break;
            case 1 when _page?.Kind == "givemenu" && _f > 20:
                Check(_m.ShopMenuUi.Visible && _m.ShopMenuUi.Page?.Text == "请把银子交给我。", $"交出物品入口：{_m.ShopMenuUi.Page?.NpcName}「{_m.ShopMenuUi.Page?.Text}」");
                Shot("1_give"); _m.ShopMenuUi.Pick(1); _st = 2; _f = 0; break;
            case 1 when _f > 300: Check(false, $"交出物品入口没出来（{_page?.Kind}）"); _st = 2; _f = 0; break;
            case 2 when _f == 30:
                Check(!_m.ShopMenuUi.Visible, "取消 → 关掉");
                _page = null; s!.Say("/dolua gm_win.lua 29 0 8"); _st = 3; _f = 0; break;
            case 3 when _page?.Kind == "bury" && _f > 20:
            {
                Check(_m.Bury.Visible && _m.Bury.Page?.SourceItem == 8, $"埋起物品：不能选的格 {_m.Bury.Page?.SourceItem}");
                Shot("2_bury"); _m.Bury.Pick(8);
                Check(_m.Bury.Visible, "点不能选的那格 → 不发包、窗口还在");
                _m.Bury.Close(); _page = null; s!.Say("/dolua gm_win.lua 11 3 请输入名字："); _st = 4; _f = 0; break;
            }
            case 3 when _f > 300: Check(false, $"埋起物品没出来（{_page?.Kind}）"); _st = 4; _f = 0; break;
            case 4 when _page != null && _f > 20:
                Check(_m.DialogUi.Visible && _m.DialogUi.InputBox != null, $"输入窗口：「{_page.Text.Trim()}」 输入框 {_m.DialogUi.InputBox != null} 按钮 [{string.Join(",", _page.Buttons)}]");
                Shot("3_input"); _m.DialogUi.InputBox!.Text = "abc"; _m.DialogUi.EmitSignal(DialogPanel.SignalName.ButtonPressed, "cancel");
                _page = null; s!.Say("/dolua gm_win.lua 35 3 请输入家族名称及会馆名称"); _st = 5; _f = 0; break;
            // 成立家族那种两个输入框的窗口（BZ 35）
            case 5 when _page != null && _f > 20:
                Check(_m.DialogUi.Visible && _m.DialogUi.InputBox != null && _m.DialogUi.InputBox2 != null && _page.Input2, $"两个输入框的窗口（35）：框 {_m.DialogUi.InputBox != null}/{_m.DialogUi.InputBox2 != null}");
                Shot("4_input2");
                _m.DialogUi.InputBox!.Text = "家名"; _m.DialogUi.EmitSignal(DialogPanel.SignalName.ButtonPressed, "ok");
                Check(_m.DialogUi.Visible, "第二个框空着点确定 → 不发、窗口还在");
                _m.DialogUi.EmitSignal(DialogPanel.SignalName.ButtonPressed, "cancel");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
            case 5 when _f > 300: Check(false, "两个输入框的窗口没出来"); GD.Print(string.Join("\n", _r)); return true;
            case 4 when _f > 300: Check(false, "输入窗口没出来"); GD.Print(string.Join("\n", _r)); return true;
        }
        return _f > 3000;
    }
}
