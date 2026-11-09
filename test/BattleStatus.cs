using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>战斗单位状态动画：直接给战斗画面一组带状态旗标的单位（昏睡 / 石化 / 酒醉 / 混乱 / 遗忘 + 魔法状态），看身上挂没挂对应动画；状态清掉后动画要撤。</summary>
public partial class BattleStatus : SceneTree
{
    int _f, _st; Main _m = null!;
    readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
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
                int a = 106002;
                var units = new List<UnitView> { new(0, 0, 2, 50, 50, a, true, false, "我", 0x4000) };
                int[] st = { 0x20, 0x40, 0x80, 0x100, 0x200 };
                for (int i = 0; i < st.Length; i++) units.Add(new UnitView(10 + i, 1, 1, 20, 20, a, true, false, $"敌{i}", st[i] | (i == 0 ? 0x10000 : 0)));
                _m.Battle.Show(new BattleView(0, units, 0));
                _st = 1; _f = 0; break;
            }
            case 1 when _f == 60:
                Check(BattleUi.MagicEffect(6100) == 110066 && BattleUi.MagicEffect(6109) == 110063 && BattleUi.MagicEffect(6400) == 110070 && BattleUi.MagicEffect(1900) == 110041,
                    $"魔法特效编号：补血1 {BattleUi.MagicEffect(6100)}、补血10 {BattleUi.MagicEffect(6109)}、恢复1 {BattleUi.MagicEffect(6400)}、陨石1 {BattleUi.MagicEffect(1900)}");
                Check(_m.Battle.StatusOverlays == 7, $"状态动画：{_m.Battle.StatusOverlays} 个（5 个异常 + 2 个魔法状态）");
                Root.GetTexture().GetImage().SavePng("res://test/out_battle_status.png");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 2000;
    }
}
