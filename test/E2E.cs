using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot.Tests;

/// <summary>
/// 端到端测试（用 testdata 原创占位素材）：
///  1. 开局在野外，关掉遇敌，点击传送点走过去 → 换到小屋地图；
///  2. 从小屋出口走回野外；
///  3. 打开遇敌并放大概率，走一步触发战斗，一直攻击到战斗结束。
/// 运行：godot --path . -s res://test/E2E.cs
/// </summary>
public partial class E2E : SceneTree
{
    private Main _main = null!;
    private int _frame, _stage;
    private readonly List<string> _results = new();
    private readonly List<int> _maps = new();
    private bool _battleStarted; private BattleOutcome? _outcome;
    private int _attackFrame = -1; private bool _shotMid, _checkedOutcome;
    private readonly List<string> _pages = new();
    private Rewards? _rewards;

    public override void _Initialize()
    {
        // 测试用配置：从 (20,20) 出发，先关掉遇敌
        var cfgPath = ProjectSettings.GlobalizePath("user://config.json");
        File.WriteAllText(cfgPath, File.ReadAllText(ProjectSettings.GlobalizePath("res://config.testdata.json"))
            .Replace("\"encounter_scale\": 0.5", "\"encounter_scale\": 0.5, \"encounters\": false")
            .Replace("\"server_dir\"", $"\"game_root\": \"{ProjectSettings.GlobalizePath("res://testdata")}\", \"server_dir\""));
        _main = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate();
        Root.AddChild(_main);
    }

    private void Check(bool ok, string name) { _results.Add((ok ? "PASS " : "FAIL ") + name); }

    public override bool _Process(double delta)
    {
        _frame++;
        var s = _main.Session;
        switch (_stage)
        {
            case 0 when _frame == 5:
                s.MapEntered += (m, x, y) => _maps.Add(m.MapId);
                s.BattleStarted += v => _battleStarted = true;
                s.BattleEnded += o => _outcome = o;
                s.RewardsGained += r => _rewards = r;
                Check(s.CurrentMap!.MapId == 1 && s.X == 20 && s.Y == 20, "开局在野外 (20,20)");
                _main.WalkTo(new Vector2I(20, 19));   // 传送点
                _stage = 1; break;
            case 1 when _frame == 20:
                Root.GetTexture().GetImage().SavePng("res://test/out_e2e_field.png"); _stage = 2; break;
            case 2 when _maps.Contains(2):
                Check(s.CurrentMap!.MapId == 2 && s.X == 6 && s.Y == 9, "踩传送点进入小屋 (6,9)");
                _stage = 3; _frame = 0; break;
            case 3 when _frame == 15:
                Root.GetTexture().GetImage().SavePng("res://test/out_e2e_room.png");
                _main.WalkTo(new Vector2I(6, 10));    // 小屋出口
                _stage = 4; break;
            case 4 when _maps.Count >= 2 && _maps[^1] == 1:
                Check(s.X == 21 && s.Y == 21, "从小屋出口回到野外 (21,21)");
                s.DialogChanged += p => _pages.Add(p?.Buttons.FirstOrDefault() ?? "(end)");
                _main.TalkOrApproach(s.NpcsOnCurrentMap().First(n => n.Id == 1));
                _stage = 40; _frame = 0; break;
            case 40 when s.InDialog && _pages.Count >= 1:
                Root.GetTexture().GetImage().SavePng("res://test/out_e2e_dialog.png");
                s.Answer("next"); s.Answer("yes");
                Check(s.Player.CountOf(7002) == 1 && s.Player.Flags.GetValueOrDefault(1) == 1 && !s.InDialog, "走到 NPC 旁对话，选'是'拿到道具");
                _stage = 41; _frame = 0; break;
            case 41 when _frame == 5:
                // 打开遇敌：直接改会话选项不方便，这里用反射式做法——重新走动直到遇敌，概率放大
                if (s is LocalSession ls)
                {
                    var f = typeof(LocalSession).GetField("_opt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    var opt = (SessionOptions)f.GetValue(ls)!; opt.EncountersEnabled = true; opt.EncounterScale = 10; opt.EncounterGraceSteps = 0;
                    typeof(LocalSession).GetField("_grace", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(ls, 0);
                }
                _main.WalkTo(new Vector2I(16, 25));
                _stage = 5; _frame = 0; break;
            case 5 when _battleStarted:
                Check(s.InBattle, "野外走动触发遇敌");
                _stage = 6; _frame = 0; break;
            case 6 when _frame == 10:
                Root.GetTexture().GetImage().SavePng("res://test/out_e2e_battle.png");
                _stage = 7; _frame = 0; break;
            case 7:
                // 像玩家一样：只有动画播完（按钮可用）时才下指令
                if (s.InBattle && !_main.Battle.Busy) { s.SubmitCommand(new Command(CommandKind.Attack)); _attackFrame = _frame; }
                if (_attackFrame >= 0 && _frame == _attackFrame + 22 && !_shotMid) { _shotMid = true; Root.GetTexture().GetImage().SavePng("res://test/out_e2e_battle_anim.png"); }
                if (_outcome != null && !_checkedOutcome)
                {
                    _checkedOutcome = true;
                    Check(_outcome == BattleOutcome.Win, $"战斗结束：{_outcome}");
                    Check(!s.InBattle, "战斗结束后回到地图");
                    Check(_rewards != null && _rewards.Exp > 0 && s.Player.Exp >= _rewards.Exp, $"战斗奖励：经验 {_rewards?.Exp}，等级 {s.Player.Level}");
                    Check(File.Exists(_main.SavePath), "自动存档");
                    _frame = 0;
                }
                if (_checkedOutcome && !_main.Battle.Visible)
                {
                    Check(_main.Battle.PlayedEvents > 0 && _main.Battle.AttackAnimsShown > 0, $"战斗动画播放：事件 {_main.Battle.PlayedEvents}，攻击动作 {_main.Battle.AttackAnimsShown}");
                    Check(_frame < 900, $"播完后关闭战斗界面（{_frame} 帧）");
                    var acts = _main.Calibration.ActsOf(900000);
                    Check(acts.SequenceEqual(new[] { 0, 1, 2, 3, 4 }), $"测试角色动作号：{string.Join(",", acts)}");
                    _main.Calibration.Toggle();
                    _stage = 9; _frame = 0;
                }
                else if (_frame > 4000) { Check(false, "战斗在限定时间内结束"); _stage = 8; }
                break;
            case 9 when _frame == 10:
                Root.GetTexture().GetImage().SavePng("res://test/out_e2e_calib.png");
                Check(_main.Calibration.Visible, "动作浏览器打开");
                _main.Calibration.Toggle();
                _stage = 8; _frame = 0; break;
            case 8 when _frame == 90:
                Root.GetTexture().GetImage().SavePng("res://test/out_e2e_after.png");
                foreach (var r in _results) GD.Print(r);
                GD.Print($"E2E DONE pass={_results.Count(r => r.StartsWith("PASS"))} fail={_results.Count(r => r.StartsWith("FAIL"))}");
                File.Delete(ProjectSettings.GlobalizePath("user://config.json"));
                return true;
        }
        if (_frame > 6000) { GD.Print("E2E TIMEOUT at stage " + _stage); foreach (var r in _results) GD.Print(r); return true; }
        return false;
    }
}
