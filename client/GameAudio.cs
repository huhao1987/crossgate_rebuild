using CgPort.Core;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 背景音乐和音效（官方 4.06 的做法）：
/// - 背景音乐：bin/bgm/bgm.cf 一行一首 "序 号 文件 音量 循环 ?"（号 200–243）。地图物件层里 200–249 的格子就是这张图的音乐号（不画出来）；
///   战斗用 205（cgbgm_b0），打完回到地图的音乐。图上没有音乐标记就接着放原来的。
/// - 环境音：物件层 250 起 = 环境音 (值 − 249)（1 流水 … 26），离主角近才听得到，按距离变小，循环播放。
/// - 音效：动画帧上的标记（见 AnimeArchive.Action.Sounds），号查 <see cref="SeTable"/>。
/// 文件：先找声音包 audio/bgm|se/小写名.ogg（手机：数据包旁边的 audio 文件夹，用 Mac 上转好的 ogg，小很多），没有再读 bin/ 里的 wav。缺文件就不响。
/// </summary>
public partial class GameAudio : Node
{
    public static GameAudio? I { get; private set; }
    private string _root = "", _audioDir = "";
    private readonly Dictionary<int, (string File, int Volume, bool Loop)> _bgmTable = new();
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private AudioStreamPlayer _bgm = null!;
    private readonly List<AudioStreamPlayer> _se = new();
    private readonly Dictionary<int, AudioStreamPlayer> _env = new();
    public int MapBgm { get; private set; } = -1;
    public int PlayingBgm { get; private set; } = -1;
    public float BgmVolume = 0.8f, SeVolume = 0.8f;   // 0–1（设定里调）
    public readonly List<int> PlayedSe = new();       // 测试用：最近播放过的音效号

    public void Init(string gameRoot, float bgmVol, float seVol)
    {
        I = this;
        _root = gameRoot; BgmVolume = bgmVol; SeVolume = seVol;
        var baseDir = gameRoot.EndsWith(GameFiles.Ext, StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(gameRoot)! : gameRoot;
        _audioDir = Path.Combine(baseDir, "audio");
        _bgm = new AudioStreamPlayer { Name = "Bgm" };
        AddChild(_bgm);
        for (int i = 0; i < 8; i++) { var p = new AudioStreamPlayer(); _se.Add(p); AddChild(p); }
        LoadBgmTable();
        GameLog.Write("声音", $"声音包 {(Directory.Exists(_audioDir) ? _audioDir : "（没有，用 bin 里的 wav）")}，音乐 {_bgmTable.Count} 首");
    }

    private void LoadBgmTable()
    {
        string? cf = File.Exists(Path.Combine(_audioDir, "bgm", "bgm.cf")) ? Path.Combine(_audioDir, "bgm", "bgm.cf")
                   : GameFiles.Exists(Path.Combine(_root, "bin", "bgm", "bgm.cf")) ? Path.Combine(_root, "bin", "bgm", "bgm.cf") : null;
        if (cf == null) return;
        var bytes = File.Exists(cf) ? File.ReadAllBytes(cf) : GameFiles.ReadAllBytes(cf);
        foreach (var line in System.Text.Encoding.Latin1.GetString(bytes).Split('\n'))
        {
            var f = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 5 || !int.TryParse(f[1], out var id) || !f[2].Contains('.')) continue;
            var file = f[2].Replace("\\\\", "/").Replace('\\', '/');
            file = Path.GetFileNameWithoutExtension(file);
            _bgmTable[id] = (file, int.TryParse(f[3], out var v) ? v : 90, f[4] != "0");
        }
    }

    // ———————— 背景音乐 ————————

    /// <summary>进图：物件层里第一个 200–249 的值就是音乐号；环境音标记记下来。</summary>
    public void OnMap(MapData map)
    {
        int bgm = -1;
        _envMarks.Clear();
        for (int i = 0; i < map.Objects.Length; i++)
        {
            int v = map.Objects[i];
            if (v is >= 200 and < 250 && bgm < 0) bgm = v;
            else if (v is >= 250 and < 300) _envMarks.Add((v - 249, i % map.Width, i / map.Width));
        }
        foreach (var p in _env.Values) p.QueueFree();
        _env.Clear();
        if (bgm >= 0) MapBgm = bgm;
        if (!_inBattle) PlayBgm(MapBgm);
    }

    private bool _inBattle;
    public void BattleStart() { _inBattle = true; PlayBgm(205); }
    public void BattleEnd() { _inBattle = false; PlayBgm(MapBgm); }

    public void PlayBgm(int id)
    {
        if (id == PlayingBgm && _bgm.Playing) return;
        PlayingBgm = id;
        _bgm.Stop();
        if (id < 0 || !_bgmTable.TryGetValue(id, out var e)) return;
        var s = Load("bgm", e.File, e.Loop);
        if (s == null) return;
        _bgm.Stream = s;
        _bgm.VolumeDb = Db(BgmVolume * e.Volume / 100f);
        _bgm.Play();
    }

    public void SetVolumes(float bgm, float se)
    {
        BgmVolume = bgm; SeVolume = se;
        if (PlayingBgm >= 0 && _bgmTable.TryGetValue(PlayingBgm, out var e)) _bgm.VolumeDb = Db(BgmVolume * e.Volume / 100f);
        if (BgmVolume <= 0) _bgm.Stop(); else if (!_bgm.Playing) { int id = PlayingBgm; PlayingBgm = -1; PlayBgm(id); }
    }

    private static float Db(float lin) => lin <= 0.001f ? -80 : Mathf.LinearToDb(lin);

    // ———————— 音效 ————————

    /// <summary>动画帧标记：1–9999 直接是音效号；≥10000 是命中帧，减 10000 后不为 0 才有声音。</summary>
    public void Cue(int flag)
    {
        int id = flag >= CgPort.Core.Assets.AnimeArchive.Action.HitFlag ? flag - CgPort.Core.Assets.AnimeArchive.Action.HitFlag : flag;
        if (id > 0) PlaySe(id);
    }

    public void PlaySe(int id, float vol = 1)
    {
        PlayedSe.Add(id); if (PlayedSe.Count > 50) PlayedSe.RemoveAt(0);
        if (SeVolume <= 0 || !SeTable.All.TryGetValue(id, out var e)) return;
        var s = Load("se", e.File, false);
        if (s == null) return;
        var p = _se.FirstOrDefault(x => !x.Playing) ?? _se[0];
        p.Stream = s; p.VolumeDb = Db(SeVolume * vol * e.Volume / 100f); p.Play();
    }

    // ———————— 环境音 ————————

    private readonly List<(int Id, int X, int Y)> _envMarks = new();
    public IReadOnlyList<(int Id, int X, int Y)> EnvMarks => _envMarks;
    public bool EnvPlaying(int id) => _env.TryGetValue(id, out var p) && p.Playing;
    public bool BgmPlaying => _bgm.Playing && _bgm.Stream != null;
    public const int EnvRange = 10;   // 格；超过就听不到（官方的距离没测，先按 10 格）

    /// <summary>主角每走一步调一次：每种环境音取最近的标记，按距离定音量。</summary>
    public void UpdateEnv(int px, int py)
    {
        if (_envMarks.Count == 0) return;
        var near = _envMarks.GroupBy(m => m.Id).Select(g => (Id: g.Key, D: g.Min(m => MathF.Sqrt((m.X - px) * (m.X - px) + (m.Y - py) * (m.Y - py))))).ToList();
        foreach (var (id, d) in near)
        {
            float vol = Math.Clamp(1 - d / EnvRange, 0, 1);
            if (!_env.TryGetValue(id, out var p))
            {
                if (vol <= 0 || !SeTable.All.TryGetValue(id, out var e) || Load("se", e.File, true) is not { } s) continue;
                p = new AudioStreamPlayer { Stream = s };
                AddChild(p); _env[id] = p; p.Play();
            }
            p.VolumeDb = Db(SeVolume * vol);
            if (vol <= 0 && p.Playing) p.Stop(); else if (vol > 0 && !p.Playing) p.Play();
        }
    }

    // ———————— 读文件 ————————

    private AudioStream? Load(string kind, string name, bool loop)
    {
        string key = $"{kind}/{name.ToLowerInvariant()}/{loop}";
        if (_streams.TryGetValue(key, out var s)) return s;
        s = null;
        try
        {
            var ogg = Path.Combine(_audioDir, kind, name.ToLowerInvariant() + ".ogg");
            if (File.Exists(ogg))
            {
                var o = AudioStreamOggVorbis.LoadFromFile(ogg);
                if (o != null) { o.Loop = loop; s = o; }
            }
            else
            {
                var dir = Path.Combine(_root, "bin", kind);
                var wav = GameFiles.Exists(Path.Combine(dir, name + ".wav")) ? Path.Combine(dir, name + ".wav")
                        : GameFiles.EnumerateFiles(dir).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (wav != null) s = Wav(GameFiles.ReadAllBytes(wav), loop);
            }
        }
        catch (Exception e) { GameLog.Write("声音", $"{kind}/{name} 读不了：{e.Message}"); }
        _streams[key] = s;
        return s;
    }

    /// <summary>官方的 wav 是 PCM（8 位无符号或 16 位），22050/44100 Hz。</summary>
    public static AudioStreamWav? Wav(byte[] d, bool loop)
    {
        if (d.Length < 44 || d[0] != 'R' || d[8] != 'W') return null;
        int pos = 12, ch = 1, rate = 22050, bits = 8; byte[]? data = null;
        while (pos + 8 <= d.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
            int len = BitConverter.ToInt32(d, pos + 4);
            if (len < 0 || pos + 8 + len > d.Length) len = d.Length - pos - 8;
            if (id == "fmt ") { ch = BitConverter.ToInt16(d, pos + 10); rate = BitConverter.ToInt32(d, pos + 12); bits = BitConverter.ToInt16(d, pos + 22); }
            else if (id == "data") { data = d.AsSpan(pos + 8, len).ToArray(); break; }
            pos += 8 + len + (len & 1);
        }
        if (data == null) return null;
        if (bits == 8) for (int i = 0; i < data.Length; i++) data[i] = (byte)(data[i] - 128);   // Godot 的 8 位是有符号
        var w = new AudioStreamWav { Data = data, Format = bits == 16 ? AudioStreamWav.FormatEnum.Format16Bits : AudioStreamWav.FormatEnum.Format8Bits, MixRate = rate, Stereo = ch == 2 };
        if (loop) { w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = 0; w.LoopEnd = data.Length / (ch * bits / 8); }
        return w;
    }
}
