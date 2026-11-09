using System.Text.Json;
using System.Text.Json.Nodes;
using CgPort.Core.Assets;
using CgPort.Core;

namespace CgPort.Godot;

/// <summary>
/// 客户端配置（config.json）。查找顺序：user://config.json → res://config.json → res://config.testdata.json。
/// game_root 不存在时退回工程内的 testdata（原创占位素材），保证工程能直接跑起来。
/// </summary>
public sealed class GameConfig
{
    public string GameRoot = "";
    public string ServerDir = "_server";
    public List<AssetSet> Sets = new();
    public string Palette = "bin/pal/palet_00.cgp";
    public int StartMap = 1000;
    public int? StartX, StartY;
    public int PlayerAnime;
    public int ActStand, ActWalk = 1, DirOffset;
    /// <summary>
    /// 战斗动作号（2026-11-07 改正，acts_v=3）：0 站立 1 走路 2 起跑 3 跑 4 停步 5 攻击 6 施法 7 投掷 8 受伤 9 防御 10 倒下
    /// 11 坐 12–16 表情 17–19 猜拳。依据：动画帧上的"命中"标记（≥10000）只出现在 5/6/7（Anime_4 全部 11608/11600/6168 组），
    /// 和 CGTool 的动作表一致；以前把 2 当攻击、3 当受伤、5 当施法、8 当防御是看图猜错的。-1 表示不播放专门动作。
    /// </summary>
    public int ActAttack = 5, ActHurt = 8, ActDead = 10, ActMagic = 6, ActGuard = 9, ActRun = 3, ActBeforeRun = 2, ActThrow = 7;
    public double EncounterScale = 0.5;
    public bool Encounters = true;
    public float Zoom = 1f;
    public bool UsingTestData;
    public bool LoadSave = true;
    /// <summary>"local" = 单机（进程内服务端）；"network" = 连 cgmsv 服务端。</summary>
    public string Mode = "local";
    /// <summary>随时间变化的地图用哪个调色板（-1 = 按游戏时间自动；规律仍在实测中，暂时固定 0）。</summary>
    public int TimePalette = -1;
    public string Host = "127.0.0.1";
    public int Port = 9030;
    /// <summary>画面角落显示帧数（默认关；帧数统计总是写进日志）。</summary>
    public bool ShowFps;
    /// <summary>音乐、音效音量 0–100（bgm_volume / se_volume，默认 80）。</summary>
    public int BgmVolume = 80, SeVolume = 80;
    /// <summary>系统 → 绘图设定（官方 4.06）：隐藏画面角色（别的玩家不画）、显示 NPC 名字。</summary>
    public bool HidePlayers, ShowNpcNames = true;
    /// <summary>大地图「显示选项」7 类（官方默认：地点、村镇、NPC 打勾），位 0–6 = 标注类别 0–6。</summary>
    public int MapLabelMask = 0b1010001;
    /// <summary>联网：登录后显示选角色界面（char_select，默认开；自动测试关掉 = 自动进第一个角色）。</summary>
    public bool CharSelect = true;

    /// <summary>改 user://config.json 里的几项（游戏里的设定窗口用，不用重启）。</summary>
    public static void Patch(params (string Key, int Value)[] kv)
    {
        try
        {
            if (!global::Godot.FileAccess.FileExists("user://config.json")) return;
            if (System.Text.Json.Nodes.JsonNode.Parse(global::Godot.FileAccess.GetFileAsString("user://config.json")) is not System.Text.Json.Nodes.JsonObject o) return;
            foreach (var (k, v) in kv) o[k] = v;
            using var f = global::Godot.FileAccess.Open("user://config.json", global::Godot.FileAccess.ModeFlags.Write);
            f.StoreString(o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch { }
    }
    /// <summary>界面模式："mobile" 手机（界面整数倍放大）、"original" 原版（和官方 960×720 一样）、空 = 自动（安卓手机用 mobile）。</summary>
    public string UiMode = "";
    public bool MobileUi => UiMode == "mobile" || (UiMode == "" && global::Godot.OS.GetName() == "Android");
    public string Account = "", Password = "", CdKey = "";

    /// <summary>读取有效配置；没有（或游戏目录不存在）时返回 null，由调用方显示首次运行设置界面。</summary>
    public static GameConfig? Load()
    {
        foreach (var p in new[] { "user://config.json", "res://config.json" })
            if (global::Godot.FileAccess.FileExists(p))
            {
                var c = Parse(global::Godot.FileAccess.GetFileAsString(p));
                if (c.GameRoot == "testdata") return TestData();
                if (GameFiles.DirectoryExists(c.GameRoot)) return c;
                global::Godot.GD.PushWarning($"游戏目录不存在：{c.GameRoot}");
            }
        return null;
    }

    /// <summary>
    /// 单机出生点：用服务端 data/loginPoints.yml 里该地图的登录点（原版"法兰城登陆点"等），
    /// 优先 242,100（法兰城传送石旁，官方回城点）。找不到返回 null（由服务端代码自己找可走格）。
    /// </summary>
    public static (int X, int Y)? LoginPoint(string root, string serverDir, int map)
    {
        try
        {
            var f = Path.Combine(root, serverDir, "data", "loginPoints.yml");
            if (!GameFiles.Exists(f)) return null;
            var pts = new List<(int, int)>(); bool init = false;
            foreach (var line in GameFiles.ReadLines(f))
            {
                if (line.StartsWith("init")) init = true; else if (line.Length > 0 && !char.IsWhiteSpace(line[0])) init = false;
                var m = System.Text.RegularExpressions.Regex.Match(line, @"\[\s*\d+\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\]");
                if (!init && m.Success && int.Parse(m.Groups[1].Value) == map) pts.Add((int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));
            }
            if (pts.Count == 0) return null;
            return pts.Contains((242, 100)) ? (242, 100) : pts[0];
        }
        catch { return null; }
    }

    /// <summary>读 user://config.json，不做任何检查（设置界面用来预填上次的值）。</summary>
    public static GameConfig? TryLoadRaw()
    {
        try { return global::Godot.FileAccess.FileExists("user://config.json") ? Parse(global::Godot.FileAccess.GetFileAsString("user://config.json")) : null; }
        catch { return null; }
    }

    public static GameConfig TestData()
    {
        var t = Parse(global::Godot.FileAccess.GetFileAsString("res://config.testdata.json"));
        t.GameRoot = global::Godot.ProjectSettings.GlobalizePath("res://testdata");
        t.UsingTestData = true;
        return t;
    }

    /// <summary>按自动识别结果生成配置 JSON（写到 user://config.json）。</summary>
    public static string BuildJson(string root, CgPort.Core.Assets.AssetDetector.Result r, int startMap, NetSettings? net = null)
    {
        var o = new JsonObject
        {
            ["game_root"] = root,
            ["server_dir"] = r.ServerDir ?? "",
            ["palette"] = r.Palette ?? "bin/pal/palet_00.cgp",
            ["start_map"] = startMap,
            ["player_anime"] = 106002,   // 4.06 图档里没有 100000
            ["act_stand"] = 0, ["act_walk"] = 1, ["act_attack"] = 5, ["act_hurt"] = 8, ["act_dead"] = 10, ["acts_v"] = 3, ["dir_offset"] = 0,
            ["encounter_scale"] = 0.5, ["encounters"] = true, ["zoom"] = 1.0,
        };
        if (net != null)
        {
            o["mode"] = "network"; o["host"] = net.Host; o["port"] = net.Port; o["account"] = net.Account; o["password"] = net.Password;
            o["player_anime"] = 106002;   // 进游戏后换成服务端给的形象
        }
        var sets = new JsonArray();
        foreach (var s in r.Sets)
        {
            var so = new JsonObject { ["name"] = s.Name, ["graphic"] = s.Graphic, ["graphic_info"] = s.GraphicInfo, ["anime_header"] = s.AnimeHeader };
            if (s.Anime != null) { so["anime"] = s.Anime; so["anime_info"] = s.AnimeInfo; }
            sets.Add(so);
        }
        o["sets"] = sets;
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public sealed record NetSettings(string Host, int Port, string Account, string Password);

    public static GameConfig Parse(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        var c = new GameConfig
        {
            GameRoot = Str(o, "game_root", ""),
            ServerDir = Str(o, "server_dir", "_server"),
            Palette = Str(o, "palette", "bin/pal/palet_00.cgp"),
            StartMap = Int(o, "start_map", 1000),
            PlayerAnime = Int(o, "player_anime", 0),
            ActStand = Int(o, "act_stand", 0),
            ActWalk = Int(o, "act_walk", 1),
            DirOffset = Int(o, "dir_offset", 0),
            // acts_v < 3 的旧配置里攻击/受伤/施法/防御号是错的，一律用新的默认值
            ActAttack = V3(o) ? Int(o, "act_attack", 5) : 5, ActHurt = V3(o) ? Int(o, "act_hurt", 8) : 8, ActDead = o["acts_v"] == null ? 10 : Int(o, "act_dead", 10),
            ActMagic = V3(o) ? Int(o, "act_magic", 6) : 6, ActGuard = V3(o) ? Int(o, "act_guard", 9) : 9,
            EncounterScale = o["encounter_scale"]?.GetValue<double>() ?? 0.5,
            Encounters = o["encounters"]?.GetValue<bool>() ?? true,
            Zoom = (float)(o["zoom"]?.GetValue<double>() ?? 1.0),
            LoadSave = o["load_save"]?.GetValue<bool>() ?? true,
            Mode = Str(o, "mode", "local"), TimePalette = Int(o, "time_palette", -1), Host = Str(o, "host", "127.0.0.1"), Port = Int(o, "port", 9030), ShowFps = Int(o, "show_fps", 0) != 0, BgmVolume = Int(o, "bgm_volume", 80), SeVolume = Int(o, "se_volume", 80), HidePlayers = Int(o, "hide_players", 0) != 0, CharSelect = Int(o, "char_select", 1) != 0, ShowNpcNames = Int(o, "npc_names", 1) != 0, MapLabelMask = Int(o, "map_labels", 0b1010001), UiMode = Str(o, "ui_mode", ""),
            Account = Str(o, "account", ""), Password = Str(o, "password", ""), CdKey = Str(o, "cdkey", ""),
        };
        if (o["start"] is JsonArray st && st.Count == 2) { c.StartX = st[0]!.GetValue<int>(); c.StartY = st[1]!.GetValue<int>(); }
        if (o["sets"] is JsonArray sets)
            foreach (var s in sets.OfType<JsonObject>())
                c.Sets.Add(new AssetSet(Str(s, "name", ""), Str(s, "graphic", ""), Str(s, "graphic_info", ""),
                    s["anime"]?.GetValue<string>(), s["anime_info"]?.GetValue<string>(), Int(s, "anime_header", 12)));
        return c;
    }

    private static string Str(JsonObject o, string k, string d) => o[k]?.GetValue<string>() ?? d;
    private static bool V3(System.Text.Json.Nodes.JsonObject o) => o["acts_v"] is { } v && v.GetValue<int>() >= 3;
    private static int Int(JsonObject o, string k, int d) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : d;
}
