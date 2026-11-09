using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 交易宠物（界面 = test1 hao，test3 nettest3 在 (242,100)）：
///  A. test3 把它的宠物放上来 → hao 的交易窗口「寵物」页看到（RwK/Yq）→ 双方确认 → hao 多一只。
///  B. hao 在自己的「寵物」页双击选那只 → 開啓（aiAb  栏|0 0）→ 确认 → 宠物到 test3。
///  C. test3 再把它还给 hao（收尾，保持测试号原样）。
/// </summary>
public partial class NetTradePet : SceneTree
{
    int _f, _st, _phase; Main _m = null!; NetworkSession _b = null!; readonly List<string> _r = new();
    string _petName = ""; int _petsA0;
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_tradepet_{n}.png");
    bool End() { try { _b.Dispose(); } catch { } GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
        var maps = CgPort.Core.MapData.IndexServerMapDir("/home/claude/wine/cgmsv/data/map");
        _b = new NetworkSession(new NetworkOptions { Host = "127.0.0.1", Port = 9030, CdKey = "test3", Account = "test3", Password = "123456", NewCharName = "nettest3", ResolveMap = f => maps.TryGetValue(f, out var m) ? m.Path : null });
        _b.Start();
    }
    /// <summary>开一次交易：hao 发起；giverIsB = test3 给宠物 slotB，否则 hao 给 slotA。</summary>
    void StartTrade() { var s = (NetworkSession)_m.Session; s.Face(0, -1); s.RequestTrade(); }
    public override bool _Process(double d)
    {
        _f++; _b.Poll();
        var s = _m.Session as NetworkSession;
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _b.InGame && _b.CurrentMap != null && _f > 120:
                if ((s.Flags & 32) == 0) s.ToggleFlag(32);
                if ((_b.Flags & 32) == 0) _b.ToggleFlag(32);
                s.Say($"/warp {_b.CurrentMap.MapId} 0 {_b.X} {_b.Y + 1}"); _st = 1; _f = 0; break;
            case 1 when _f > 90 && s!.X == _b.X && s.Y == _b.Y + 1:
                _phase = 0;
                if (_b.Player.Pets.Count == 0) { Check(false, "test3 没有宠物可给"); return End(); }
                _petName = _b.Player.Pets[0].Name; _petsA0 = s.Player.Pets.Count;
                StartTrade(); _st = 2; _f = 0; break;
            case 1 when _f > 600: Check(false, $"没站好：({s!.X},{s.Y}) test3 ({_b.X},{_b.Y})"); return End();
            case 2 when s!.TradeCandidates.Count > 0 && _f > 40 || _f > 200:
                if (s.TradeCandidates.Count == 0) { Check(false, "面前没有交易对象"); return End(); }
                _m.TradePickerUi.Visible = false; s.SelectTradeTarget(0); _st = 3; _f = 0; break;
            case 3 when _b.Trade != null && _m.TradeUi.Visible && _f > 20:
                // 给的一方放上宠物、另一方空着
                if (_phase == 1) { _m.TradeUi.TestTab(true, 1); _m.TradeUi.TestPickPet(s!.Player.Pets.First(p => p.Name == _petName).Slot); Shot("b_pick"); _m.TradeUi.DoOpen(); _b.OpenTrade(Array.Empty<int>(), 0); }
                else { _b.OpenTrade(Array.Empty<int>(), 0, new[] { _b.Player.Pets.First(p => p.Name == _petName).Slot }); _m.TradeUi.DoOpen(); }
                _st = 4; _f = 0; break;
            case 3 when _f > 300: Check(false, "交易窗口没开"); return End();
            case 4 when _f == 60:
                if (_phase == 0)
                {
                    _m.TradeUi.TestTab(false, 1);
                    var tp = s!.Trade?.TheirPets.Values.FirstOrDefault();
                    Check(tp != null && tp.Name == _petName && tp.Level > 0, $"对方放上来的宠物：{tp?.Name} Lv{tp?.Level} 招式 {string.Join("、", tp?.Techs.Select(x => x.Name) ?? Array.Empty<string>())}");
                    Shot("a_their");
                }
                else if (_phase == 1) Check(_b.Trade?.TheirPets.Values.Any(p => p.Name == _petName) == true, $"test3 看到 hao 放的宠物：{string.Join(",", _b.Trade?.TheirPets.Values.Select(p => p.Name) ?? Array.Empty<string>())}");
                s!.ConfirmTrade(); _b.ConfirmTrade(); _st = 5; _f = 0; break;
            case 5 when s!.Trade == null && _b.Trade == null && _f > 60 || _f > 400:
                if (_phase == 0) Check(s!.Player.Pets.Any(p => p.Name == _petName), $"交易完成：hao 拿到 {_petName}（宠物 {_petsA0} → {s.Player.Pets.Count}）");
                else if (_phase == 1) Check(_b.Player.Pets.Any(p => p.Name == _petName) && !s!.Player.Pets.Any(p => p.Name == _petName), $"hao 把 {_petName} 交给 test3");
                else { Check(s!.Player.Pets.Any(p => p.Name == _petName), "test3 还给 hao（收尾）"); return End(); }
                _phase++; StartTrade(); _st = 2; _f = 0; break;
        }
        return _f > 5000;
    }
}
