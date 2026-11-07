using System.Buffers.Binary;

namespace CgPort.Core.Assets;

/// <summary>
/// 自动识别游戏目录里的资源：
///  - 图像组：bin/ 及其子目录下的 GraphicInfo*.bin，配对同后缀的 Graphic*.bin；
///  - 动画组：AnimeInfo*.bin 配对 Anime*.bin，动作头长度（12 或 20）通过试读判断；
///  - 加载顺序：已知文件按 cgmsv.yml 的顺序，其余按名字排在后面；
///  - 服务端目录：游戏目录下含 data/map 的子目录（优先以 _ 开头的）。
/// 不认识的文件不会报错，只是不加载。
/// </summary>
public static class AssetDetector
{
    // cgmsv.yml 的分组顺序（按组名匹配，不区分大小写）
    private static readonly string[] KnownOrder = { "BASE", "EX", "V3", "PUK1", "PUK2", "PUK3", "JOY", "JOY_EX", "PUK4", "PUK7" };

    public sealed record Result(List<AssetSet> Sets, string? ServerDir, string? Palette, List<string> Notes);

    public static Result Detect(string root)
    {
        var notes = new List<string>();
        var bin = Path.Combine(root, "bin");
        var sets = new List<AssetSet>();
        if (!GameFiles.DirectoryExists(bin)) { notes.Add("找不到 bin 目录"); return new Result(sets, FindServerDir(root), null, notes); }

        var files = GameFiles.EnumerateFiles(bin, recursive: true)
            .Where(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        string? Find(string dir, string prefix, string suffix) =>
            files.FirstOrDefault(f => Path.GetDirectoryName(f)!.Replace('\\', '/') == dir
                && Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(f).Substring(prefix.Length).Equals(suffix, StringComparison.OrdinalIgnoreCase));

        var animeInfos = files.Where(f => Path.GetFileName(f).StartsWith("AnimeInfo", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var gi in files.Where(f => Path.GetFileName(f).StartsWith("GraphicInfo", StringComparison.OrdinalIgnoreCase)))
        {
            var dir = Path.GetDirectoryName(gi)!.Replace('\\', '/');
            var tag = Path.GetFileNameWithoutExtension(gi)["GraphicInfo".Length..];   // 例如 "_66"、"Ex_5"、"_PUK2_2"
            var g = Find(dir, "Graphic", tag);
            if (g == null) { notes.Add($"{gi} 没有配对的 Graphic 文件"); continue; }
            // 动画：同目录里组标记相同的 AnimeInfo（版本号可能不同，例如 GraphicInfo_PUK2_2 对 AnimeInfo_PUK2_4）
            string group = GroupKey(tag);
            var ai = animeInfos.FirstOrDefault(a => Path.GetDirectoryName(a)!.Replace('\\', '/') == dir && GroupKey(Path.GetFileNameWithoutExtension(a)["AnimeInfo".Length..]) == group);
            string? an = null; int hdr = 12;
            if (ai != null)
            {
                an = Find(dir, "Anime", Path.GetFileNameWithoutExtension(ai)["AnimeInfo".Length..]);
                if (an != null) hdr = ProbeHeader(Path.Combine(root, ai), Path.Combine(root, an));
                else ai = null;
            }
            sets.Add(new AssetSet(group, g, gi, an, ai, hdr));
        }
        sets = sets.OrderBy(s => Rank(s.GraphicInfo)).ThenBy(s => s.GraphicInfo, StringComparer.OrdinalIgnoreCase).ToList();
        // 官方客户端只加载启动参数（data/argument.ini）里列出的图像文件；目录里多出来的（PUK4/PUK7/v999 等，编号会和基础图档冲突）不能加载
        var allowed = AllowedFromArguments(root);
        if (allowed != null)
        {
            var dropped = sets.Where(st => !allowed.Contains(Path.GetFileNameWithoutExtension(st.GraphicInfo).ToUpperInvariant())).Select(st => st.GraphicInfo).ToList();
            if (dropped.Count > 0) notes.Add("按 argument.ini 不加载：" + string.Join("、", dropped));
            sets = sets.Where(st => allowed.Contains(Path.GetFileNameWithoutExtension(st.GraphicInfo).ToUpperInvariant())).ToList();
        }
        var pal = files.FirstOrDefault(f => f.EndsWith("pal/palet_00.cgp", StringComparison.OrdinalIgnoreCase));
        return new Result(sets, FindServerDir(root), pal, notes);
    }

    /// <summary>
    /// 读 data/argument.ini（登录器传给 cg 主程序的参数，例如 graphicinfobin:66 graphicinfobin_puk2:2 graphicinfobin_joy_ch:1），
    /// 返回允许加载的 GraphicInfo 文件名（大写、不含扩展名）。文件不存在返回 null（全部加载）。
    /// </summary>
    public static HashSet<string>? AllowedFromArguments(string root)
    {
        var p = Path.Combine(root, "data", "argument.ini");
        if (!GameFiles.Exists(p)) return null;
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tok in GameFiles.ReadAllText(p).Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = tok.Split(':');
            if (kv.Length != 2 || !kv[0].StartsWith("graphicinfobin", StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = kv[0]["graphicinfobin".Length..].ToLowerInvariant();
            var name = suffix switch
            {
                "" => $"GraphicInfo_{kv[1]}",
                "ex" => $"GraphicInfoEx_{kv[1]}",
                "v3" => $"GraphicInfoV3_{kv[1]}",
                "_joy_ch" => $"GraphicInfo_Joy_CH{kv[1]}",
                _ when suffix.StartsWith('_') => $"GraphicInfo{suffix}_{kv[1]}",
                _ => null,
            };
            if (name != null) res.Add(name.ToUpperInvariant());
        }
        return res.Count > 0 ? res : null;
    }

    /// <summary>组名：去掉末尾的纯数字版本号。"_PUK2_2"→"PUK2"，"Ex_5"→"EX"，"_66"→"BASE"，"_Joy_125"→"JOY"，"_Joy_CH1"→"JOY_CH1"，"_Joy_EX_110"→"JOY_EX"。</summary>
    private static string GroupKey(string tag)
    {
        var parts = tag.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && int.TryParse(parts[^1], out _)) parts.RemoveAt(parts.Count - 1);
        var key = string.Join("_", parts).ToUpperInvariant();
        return key.Length == 0 ? "BASE" : key;
    }

    private static int Rank(string graphicInfo)
    {
        var key = GroupKey(Path.GetFileNameWithoutExtension(graphicInfo)["GraphicInfo".Length..]);
        int i = Array.IndexOf(KnownOrder, key);
        return i >= 0 ? i : KnownOrder.Length;
    }

    /// <summary>试读前若干个动画：分别按 12 和 20 字节动作头走一遍，看哪种能精确落到下一个动画的起点。</summary>
    public static int ProbeHeader(string infoPath, string animePath)
    {
        var info = GameFiles.ReadAllBytes(infoPath);
        using var f = GameFiles.OpenRead(animePath);
        long size = f.Length;
        int n = Math.Min(info.Length / 12, 200);
        int best = 12, bestScore = -1;
        foreach (int hdr in new[] { 12, 20 })
        {
            int score = 0;
            var buf = new byte[20];
            for (int i = 0; i < n; i++)
            {
                uint off = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(i * 12 + 4));
                int cnt = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(i * 12 + 8));
                long next = i + 1 < info.Length / 12 ? BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan((i + 1) * 12 + 4)) : size;
                long pos = off; bool ok = true;
                for (int k = 0; k < cnt && ok; k++)
                {
                    if (pos + hdr > size) { ok = false; break; }
                    f.Seek(pos, SeekOrigin.Begin);
                    if (f.Read(buf, 0, hdr) < hdr) { ok = false; break; }
                    uint frames = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(8));
                    if (frames > 4096) { ok = false; break; }
                    pos += hdr + frames * 10L;
                }
                if (ok && pos == next) score++;
            }
            if (score > bestScore) { bestScore = score; best = hdr; }
        }
        return best;
    }

    public static string? FindServerDir(string root)
    {
        if (!GameFiles.DirectoryExists(root)) return null;
        var dirs = GameFiles.EnumerateDirectories(root).Select(Path.GetFileName).Where(d => d != null && GameFiles.DirectoryExists(Path.Combine(root, d!, "data", "map"))).ToList();
        return dirs.FirstOrDefault(d => d!.StartsWith('_')) ?? dirs.FirstOrDefault();
    }
}
