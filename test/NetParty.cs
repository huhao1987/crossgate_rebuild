using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 联网组队（测试自己开第二个连接当 bob = test2：站在 法兰城 242,101、开着「隊」，hao 进队后走两步）：
/// 左上「隊」开关切换 → 面向 bob 点右上「组队」→ 进队 → 队长 bob 走动时自己跟着走（外部脚本看到 /tmp/party_ready 后去点官方画面）→ 再点「组队」离队。
/// </summary>
public partial class NetParty : SceneTree
{
    NetworkSession _b = null!; bool _bWarp; int _bSteps, _bDir = 4; (int, int) _bLast; int _f, _st, _flags0; Main _m = null!; readonly List<string> _r = new();
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_party_{n}.png");
    public override void _Initialize()
    {
        File.Delete("/tmp/party_ready");
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
        if (!_bWarp && _b.InGame && _b.CurrentMap != null)
        {
            _bWarp = true;
            if (_b.InParty) _b.PartyButton();
            _b.Say("/warp 1000 0 242 101"); if ((_b.Flags & 1) == 0) _b.ToggleFlag(1);
        }
        // hao 进队以后 bob（队长）往南走三步（东边那格服务端走不通）
        if (_st == 7 && _f % 40 == 20 && _bSteps < 8 && Math.Abs(_b.X - 242) + _b.Y - 101 < 3)
        {
            if (_bSteps > 0 && (_b.X, _b.Y) == _bLast) _bDir = (_bDir + 1) % 8;   // 上一步没走成就换方向
            _bLast = (_b.X, _b.Y); _bSteps++; _b.Step(_bDir);
        }
        var h = _m.HudSkin;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90 && _bWarp:
                s.Say("/warp 1000 0 242 100"); _st = 1; _f = 0; break;
            case 1 when _f == 60:
                _flags0 = s!.Flags;
                Check(h.FlagLit(2) == ((_flags0 & 1) != 0), $"左上「隊」亮不亮跟开关位一致（开关 {_flags0}）");
                h.TestClickFlag(2); _st = 2; _f = 0; break;
            case 2 when _f == 40:
                Check(s!.Flags == (_flags0 ^ 1) && h.FlagLit(2) == ((s.Flags & 1) != 0), $"点「隊」→ 服务端回新开关 {_flags0} → {s.Flags}，亮字跟着变");
                Shot("1_flags");
                if ((s.Flags & 1) == 0) h.TestClickFlag(2);   // 留着开
                _st = 3; _f = 0; break;
            case 3 when _f == 40:
                s!.Face(0, 1); _st = 4; _f = 0; break;
            case 4 when _f == 20:
                h.TestPressButton(22585); _st = 5; _f = 0; break;
            case 5 when s!.InParty || _f > 180:
                Check(s.InParty && !s.IsPartyLeader && s.Party.Count == 2, $"点「组队」进了 bob 的队：{string.Join("、", s.Party.Select(m => $"{m.Index}:{m.Name} Lv{m.Level} HP{m.Hp}/{m.MaxHp}"))}");
                _st = 6; _f = 0; break;
            case 6 when _f == 30:
                Check(_m.Chat.Lines.Any(l => l.Contains("加入了队伍")), $"聊天栏有系统提示：{string.Join(" / ", _m.Chat.Lines)}");
                Shot("2_joined");
                File.WriteAllText("/tmp/party_ready", "1");
                _st = 7; _f = 0; break;
            case 7 when (s!.X != 242 || s.Y != 100) && _f > 30 || _f > 900:
                _st = 8; _f = 0; break;
            case 8 when _f == 150:
                Check(s!.X != 242 || s.Y != 100, $"队长走了，自己跟着到了 ({s.X},{s.Y})");
                Check(s.Step(2) == CgPort.Server.MoveResult.Blocked, "队员自己不能走");
                Shot("3_follow");
                h.TestPressButton(22585); _st = 9; _f = 0; break;
            case 9 when _f == 60:
                Check(!s!.InParty, "再点「组队」离开了队伍");
                Check(_m.Chat.Lines.Any(l => l.Contains("离开")), $"聊天栏：{string.Join(" / ", _m.Chat.Lines)}");
                GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true;
        }
        return _f > 3000;
    }
}
