using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using CgPort.Server;
using Godot;

namespace CgPort.Godot.Tests;

/// <summary>
/// 联网端到端测试：Godot 客户端（NetworkSession）连本机 Wine 里的原版 cgmsv，
/// 用官方客户端的目录当素材（/home/claude/wine/client），截图供与官方客户端对比。
/// 流程：登录 → 截法兰城 → GM 传送到芙蕾雅 → 来回走到遇敌 → 普攻到战斗结束 → 截图。
/// 运行：godot --path . -s res://test/NetE2E.cs   （环境变量 CG_ACCOUNT 指定账号，默认 test1）
/// </summary>
public partial class NetE2E : SceneTree
{
    private Main _main = null!;
    private int _frame, _stage, _steps;
    private readonly List<string> _results = new();
    private bool _battle; private BattleOutcome? _outcome; private int _turns;
    private DialogPage? _page;
    private long _gold0; private int _items0;
    private bool _usedItem; private int _slotCheckAt = -1; private int _hpBefore, _potBefore, _clicks;

    public override void _Initialize()
    {
        var root = System.Environment.GetEnvironmentVariable("CG_ROOT") ?? "/home/claude/wine/client";
        var acct = System.Environment.GetEnvironmentVariable("CG_ACCOUNT") ?? "test1";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv";
        o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030;
        o["account"] = acct; o["char_select"] = 0; o["password"] = "123456";
        o["player_anime"] = 106002; o["act_stand"] = 0; o["act_walk"] = 1; o["load_save"] = false;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _main = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate();
        Root.AddChild(_main);
    }

    private void Check(bool ok, string name) { _results.Add((ok ? "PASS " : "FAIL ") + name); GD.Print((ok ? "PASS " : "FAIL ") + name); }
    private void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_net_{n}.png");

    public override bool _Process(double delta)
    {
        _frame++;
        var s = _main.Session as NetworkSession;
        switch (_stage)
        {
            case 0 when s != null:
                s.BattleStarted += v => _battle = true;
                s.TurnResolved += (e, v) => _turns++;
                s.BattleEnded += oc => _outcome = oc;
                _stage = 1; break;
            case 1 when s!.InGame && s.CurrentMap != null && _frame > 60:
                Check(true, $"[{s.Player.Name}] [{string.Join(",", s.NpcsOnCurrentMap().Select(n => n.Name))}] 登录进入 {s.CurrentMap.MapId} {s.CurrentMap.Name} ({s.X},{s.Y})，周围 {s.NpcsOnCurrentMap().Count} 个对象");
                _stage = 2; _frame = 0; break;
            case 2 when _frame == 90:
                // 这个测试走"不带宠物"（人物两次行动）的流程；带宠物的在 NetSkill 里测（宠物数据登录后稍晚才到，所以在这里改）
                foreach (var pet in s!.Player.Pets.Where(p => p.State == CgPort.Server.PetInfo.Battle)) s.SetPetState(pet.Slot, CgPort.Server.PetInfo.Standby);
                s.Say("/dolua gm_clear.lua");   // 背包满了买不进东西：先清最后 4 格（云端测试服务端的脚本）
                Shot("town");
                s!.DialogChanged += p => { if (p != null) _page = p; };
                s.Say("/1");   // 服务端 Lua 的自动遇敌菜单：一个 WN 选择列表窗口
                _stage = 20; _frame = 0; break;
            case 20 when _page != null && _frame > 30:
                Check(_page.Options?.Count > 0, $"收到服务端窗口：{_page.Options?.Count} 个选项，按钮 {string.Join(",", _page.Buttons)}");
                Shot("dialog");
                s!.Answer("cancel");
                _stage = 30; _frame = 0; break;
            case 20 when _frame > 300:
                Check(false, "没收到服务端窗口"); _stage = 30; _frame = 0; break;
            case 30 when _frame == 30:   // 点 NPC：走到旁边、转身面向它 → 服务端发对话窗口
                _page = null;
                var npc = s!.NpcsOnCurrentMap().FirstOrDefault(n => n.Name.StartsWith("罗吉"));
                if (npc == null) { Check(false, "找不到 NPC 罗吉"); _stage = 21; _frame = 0; break; }
                _main.TalkOrApproach(npc); _stage = 31; _frame = 0; break;
            case 31 when _page != null && _frame > 20:
                Check(true, $"点 NPC 对话：{_page.Text.Replace("\n", " ").Trim()} [{string.Join(",", _page.Buttons)}]，站在 ({s!.X},{s.Y})");
                Shot("npc_talk"); s.Answer("ok"); _stage = 40; _frame = 0; break;
            case 31 when _frame > 600:
                Check(false, $"点 NPC 后 10 秒没收到对话窗口，站在 ({s!.X},{s.Y})"); _stage = 40; _frame = 0; break;
            // 商店：GM 给钱 → 传送到医院药剂师旁 → 对话 → 选"买" → 在商店面板里买 1 瓶药
            case 40 when _frame == 20: s!.Say("/addGold 1000"); s.Say("/giveItem 15605 2"); break;
            case 40 when _frame == 50: s!.Say("/warp 1112 0 16 36"); break;
            case 40 when _frame > 80 && s!.CurrentMap?.MapId == 1112 && s.X == 16 && s.Y == 36:
                _page = null; _gold0 = s.Player.Gold; _items0 = s.Player.Items.Sum(i => i.Count);
                var doc = s.NpcsOnCurrentMap().FirstOrDefault(n => n.X == 16 && n.Y == 35);
                if (doc == null) { Check(false, "医院里找不到药剂师"); _stage = 21; _frame = 0; break; }
                _main.TalkOrApproach(doc); _stage = 41; _frame = 0; break;
            case 40 when _frame > 400: Check(false, "传送到医院失败"); _stage = 21; _frame = 0; break;
            case 41 when _page?.Kind == "shop" && _frame > 20:
                Shot("shop_menu"); var menu = _page; _page = null; s!.Answer("opt:0"); _stage = 42; _frame = 0; break;
            case 42 when _page?.Kind == "buy" && _frame > 30:
                Check(_main.Shop.Visible && _page.Shop!.Count > 0, $"商店买东西列表：{string.Join("、", _page.Shop!.Select(e => $"{e.Name} {e.Price}G"))}");
                Shot("shop_buy");
                _main.Shop.Add(0, 1); _main.Shop.Ok(); _main.Shop.Yes();
                _stage = 43; _frame = 0; break;
            case 43 when _frame == 90:
                Check(s!.Player.Gold == _gold0 - 75 && (s.Player.Items.Sum(i => i.Count) == _items0 + 1 || s.Player.Items.Count >= 20), $"买到 1 件（背包 {s.Player.Items.Count}/20 格）：金钱 {_gold0} → {s.Player.Gold}，道具 {_items0} → {s.Player.Items.Sum(i => i.Count)}");
                s.Answer("opt:2"); _stage = 21; _frame = 0; break;
            case 41 or 42 when _frame > 300: Check(false, $"商店窗口没出来（{_page?.Kind}）"); _stage = 21; _frame = 0; break;
            case 21 when _frame == 30:
                s!.Say("/dolua gm_heal.lua");   // 测试角色血太少会一回合就倒下，药还没生效（云端测试服务端的脚本）
                s.Say("/warp 100 0 370 180");
                _stage = 3; _frame = 0; break;
            case 3 when s!.CurrentMap?.MapId == 100 && _frame > 60:
                Check(true, $"传送到 {s.CurrentMap.Name} ({s.X},{s.Y})");
                Shot("field");
                _stage = 4; _frame = 0; break;
            case 4:
                if (_battle) { Check(true, $"走 {_steps} 步后遇敌"); _stage = 5; _frame = 0; break; }
                if (_frame % 14 == 0) { s!.Step(_steps % 2 == 0 ? 2 : 6); _steps++; }
                if (_steps > 400) { Check(false, "400 步内没遇敌"); _stage = 9; }
                break;
            case 5 when _frame == 40:
                Shot("battle");
                _stage = 6; _frame = 0; break;
            case 6:
                if (s!.InBattle && !_main.Battle.Busy && _frame % 30 == 0)
                {
                    // 第一回合：用一瓶生命力回复药给自己（测"物品"指令）；之后普攻
                    var pot = s.Player.Items.FirstOrDefault(i => i.ItemId == 15605);
                    if (!_usedItem && pot != null && _main.Battle.Visible)
                    {
                        _usedItem = true; _hpBefore = s.Player.Hp; _potBefore = s.Player.Items.Where(i => i.ItemId == 15605).Sum(i => i.Count);
                        // 官方路径：「物品」→ 物品窗口（22737，不能用的盖红叉）→ 双击药 → "请选择目标" → 点自己
                        _main.Battle.TestPressItem();
                        Check(_main.Battle.ItemWindowOpen, $"战斗物品窗口打开，红叉 {_main.Battle.ItemWindowCrosses} 个（背包 {s.Player.Items.Count} 件，能用 {s.Player.Items.Count(i => (i.Use & 1) != 0)} 件）");
                        Shot("battle_items");
                        _main.Battle.TestDoubleClickItem(pot);
                        _main.Battle.TestPickTarget(_main.Battle.PlayerId);
                        _slotCheckAt = _frame + 10;
                    }
                    else if (_frame % 60 == 0)
                    {
                        // 真实点击路径：按"攻击"→ 往敌人身上发一次真的鼠标按下/抬起（经 GUI 分发）
                        var foe = _main.Battle.UnitsOnScreen.FirstOrDefault(u => u.Side == 1 && !u.Down);
                        if (foe.Side == 1)
                        {
                            _main.Battle.TestPressAttack();
                            var at = foe.Pos + new Vector2(0, -40);
                            Input.ParseInputEvent(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at });
                            Input.ParseInputEvent(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at });
                            _clicks++;
                        }
                    }
                }
                if (_frame == _slotCheckAt)
                {
                    var ps = _main.Battle.PanelState;
                    Check(ps.Visible && ps.Second && _main.Battle.CommandSlot == 1, $"下完指令1后面板换成指令2（显示 {ps.Visible}，指令2 {ps.Second}，slot {_main.Battle.CommandSlot}，{_main.Battle.DebugPanel}，宠物 {string.Join(",", s!.Player.Pets.Select(p => p.Name + p.State))}）");
                    Shot("battle_cmd2");
                }
                if (_frame == 70) Shot("battle_anim");
                if (_outcome != null && !_main.Battle.Visible)
                {
                    int potAfter = s.Player.Items.Where(i => i.ItemId == 15605).Sum(i => i.Count);
                    Check(_usedItem && potAfter == _potBefore - 1, $"战斗中用药：药 {_potBefore} → {potAfter}，HP 用前 {_hpBefore}");
                    Check(_turns >= 1, $"战斗结束：{_outcome}，{_turns} 回合（点敌人 {_clicks} 次）"); _stage = 7; _frame = 0;
                }
                if (_frame > 60 * 120) { Check(false, "战斗 2 分钟内没结束"); _stage = 9; }
                break;
            case 7 when _frame == 30:
            {
                var ns = (NetworkSession)_main.Session;
                Check(_outcome == CgPort.Server.BattleOutcome.Lose || _main.ResultUi.Visible && ns.LastResult != null, $"战斗结果窗口：{_main.ResultUi.Text}（{_outcome}）");
                Check(_main.HudSkin.BurstText.Length == 5, $"左上 Burst {_main.HudSkin.BurstText}（{ns.BurstSeconds} 秒）");
                Shot("after"); _stage = 9; break;
            }
            case 9:
                GD.Print($"NETE2E DONE pass={_results.Count(r => r.StartsWith("PASS"))} fail={_results.Count(r => r.StartsWith("FAIL"))}");
                File.Delete(ProjectSettings.GlobalizePath("user://config.json"));
                return true;
        }
        if (_frame > 60 * 60 && _stage < 4) { GD.Print("NETE2E TIMEOUT stage " + _stage); return true; }
        return false;
    }
}
