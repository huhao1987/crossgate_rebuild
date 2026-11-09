using System.Text.Json.Nodes;
using CgPort.Client;
using CgPort.Server;
using CgPort.Core.Assets;
using Godot;
namespace CgPort.Godot.Tests;
/// <summary>
/// 联网图鉴（test1）：清空图鉴和背包 → GM 给一张「迷你蝙蝠的卡片」(14848) → 双击使用 → 服务端问"要把这张卡片注册在图鉴上吗？"→ 确定
/// → 收到 dM（内容）和 IYo（新增）→ 图鉴列表第 4 页有 048 迷你蝙蝠和「新增」→ 详情 → 「新增」没了
/// → gm_albumset 登记 48、142 → 本地没有 142 的内容，客户端发 kza 要，收到 哥布林 → 本地缓存文件里有两条。
/// </summary>
public partial class NetAlbum : SceneTree
{
    int _f, _st; DialogPage? _page; bool _hooked; Main _m = null!; readonly List<string> _r = new(); string _cache = "";
    void Check(bool ok, string n) { _r.Add((ok ? "PASS " : "FAIL ") + n); GD.Print((ok ? "PASS " : "FAIL ") + n); }
    void Shot(string n) => Root.GetTexture().GetImage().SavePng($"res://test/out_album_{n}.png");
    bool End() { GD.Print(string.Join("\n", _r)); File.Delete(ProjectSettings.GlobalizePath("user://config.json")); return true; }
    public override void _Initialize()
    {
        var root = "/home/claude/wine/client";
        var o = JsonNode.Parse(GameConfig.BuildJson(root, AssetDetector.Detect(root), 1000))!.AsObject();
        o["server_dir"] = "../cgmsv"; o["mode"] = "network"; o["host"] = "127.0.0.1"; o["port"] = 9030; o["account"] = "test1"; o["char_select"] = 0; o["password"] = "123456"; o["load_save"] = false;
        o["player_anime"] = 106002;
        File.WriteAllText(ProjectSettings.GlobalizePath("user://config.json"), o.ToJsonString());
        _cache = ProjectSettings.GlobalizePath("user://album_127.0.0.1_9030.txt");
        File.Delete(_cache);
        _m = (Main)GD.Load<PackedScene>("res://client/main.tscn").Instantiate(); Root.AddChild(_m);
    }
    public override bool _Process(double d)
    {
        _f++;
        var s = _m.Session as NetworkSession;
        if (s != null && !_hooked) { _hooked = true; s.DialogChanged += p => { if (p != null) _page = p; }; }
        string dialog = _page?.Text ?? "";
        switch (_st)
        {
            case 0 when s?.InGame == true && s.CurrentMap != null && _f > 90:
                s.Say("/dolua gm_albumclear.lua"); s.Say("/dolua gm_clear.lua"); _st = 1; _f = 0; break;
            case 1 when _f == 60:
                Check(s!.AlbumCount == 0, $"清空后图鉴 0 条（{s.AlbumCount}）");
                s.Say("/giveItem 14848 1 1 1"); _st = 2; _f = 0; break;
            case 2 when s!.Player.Items.Any(i => i.ItemId == 14848) || _f > 300:
            {
                var it = s.Player.Items.FirstOrDefault(i => i.ItemId == 14848);
                Check(it != null, $"拿到 {it?.Name}（格子 {it?.Slot}）");
                if (it == null) return End();
                _page = null;
                s.UseItem(it.Slot); _st = 3; _f = 0; break;
            }
            case 3 when dialog.Contains("图鉴") || _f > 300:
                Check(dialog.Contains("迷你蝙蝠") && dialog.Contains("注册在图鉴上"), $"使用卡片弹出确认：{dialog.Replace("\n", " ")}");
                Shot("1_confirm"); s!.Answer("ok"); _st = 4; _f = 0; break;
            case 4 when s!.AlbumEntries.ContainsKey(48) && s.AlbumNew.Contains(48) || _f > 300:
            {
                s.AlbumEntries.TryGetValue(48, out var e);
                Check(e != null && e.Name == "迷你蝙蝠" && e.Race == 3 && e.Stats.SequenceEqual(new[] { 1, 4, 3, 4, 2 }) && e.Elements.SequenceEqual(new[] { 0, 0, 6, 4 }) && e.Slots == 10 && e.Seal == 1,
                    $"dM 解析：{e?.No} {e?.Name} 图像 {e?.Anime} 种族 {e?.Race} 能力 {string.Join(",", e?.Stats ?? new int[0])} 属性 {string.Join(",", e?.Elements ?? new int[0])} 技能栏 {e?.Slots}");
                Check(s.HasAlbum(48) && s.AlbumNew.Contains(48), "IYo：048 已登记、标为新增");
                Check(!s.Player.Items.Any(i => i.ItemId == 14848), "卡片用掉了");
                _m.AlbumUi.Visible = true; _m.AlbumUi.TestPage(3); _st = 5; _f = 0; break;
            }
            case 5 when _f == 20:
                Shot("2_list"); _m.AlbumUi.OpenDetail(48); _st = 6; _f = 0; break;
            case 6 when _f == 40:
                Shot("3_detail");
                Check(!s!.AlbumNew.Contains(48), "看过详情「新增」去掉");
                s.Say("/dolua gm_albumset.lua"); _st = 7; _f = 0; break;
            case 7 when s!.AlbumEntries.ContainsKey(142) || _f > 300:
            {
                s.AlbumEntries.TryGetValue(142, out var e);
                Check(e?.Name == "哥布林" && e.Race == 0 && s.HasAlbum(142) && s.HasAlbum(48) && s.AlbumCount == 2, $"Ilz 后发 kza 要到 142：{e?.Name} 种族 {e?.Race}，共 {s.AlbumCount} 条");
                _m.AlbumUi.OpenDetail(142); _st = 8; _f = 0; break;
            }
            case 8 when _f == 40:
            {
                Shot("4_goblin");
                var lines = File.Exists(_cache) ? File.ReadAllLines(_cache) : Array.Empty<string>();
                Check(lines.Length == 2 && lines[0].StartsWith("48|") && lines[1].StartsWith("142|"), $"本地缓存：{string.Join(" / ", lines)}");
                _m.AlbumUi.TestPage(9); _st = 9; _f = 0; break;
            }
            case 9 when _f == 20:
                Shot("5_list10"); _m.AlbumUi.Visible = false; s!.Say("/dolua gm_albumclear.lua");
                return End();
        }
        return _f > 5000;
    }
}
