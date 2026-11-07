using System.Buffers.Binary;
using System.Text;

namespace CgPort.Core;

/// <summary>
/// 游戏文件读取入口：普通目录，或一个 .cgpak 数据包（手机上只放一个大文件，不用拷上万个小文件）。
/// 路径写法不变：游戏目录设成数据包本身（如 /sdcard/CrossGate/crossgate.cgpak），
/// 之后 Path.Combine(游戏目录, "bin", "Graphic_66.bin") 这样的路径会自动从包里读。包里不压缩，大文件可以直接随机读。
///
/// .cgpak 格式（小端）：
///   0  8  "CGPAK1\0\0"
///   8  8  索引偏移 u64
///  16  4  文件数 u32
///  …      文件内容依次存放
///  索引：每项 u16 路径字节数 + UTF-8 路径（相对、用 /）+ u64 偏移 + u64 长度
/// 包里查文件不区分大小写（原版文件名大小写不统一，如 AnimeEx_1.Bin）。
/// </summary>
public static class GameFiles
{
    public const string Ext = ".cgpak";
    public static readonly byte[] Magic = "CGPAK1\0\0"u8.ToArray();

    private sealed record Entry(string Name, long Offset, long Length);

    private sealed class Pack
    {
        public required string FilePath;
        public readonly Dictionary<string, Entry> Files = new(StringComparer.Ordinal);
        public readonly HashSet<string> Dirs = new(StringComparer.Ordinal) { "" };
    }

    private static readonly Dictionary<string, Pack?> _packs = new(StringComparer.Ordinal);
    private static readonly object _lock = new();

    private static string Key(string rel) => rel.Replace('\\', '/').Trim('/').ToLowerInvariant();

    /// <summary>路径在某个数据包里时，返回包和包内相对路径（小写键）。</summary>
    private static bool Resolve(string path, out Pack pack, out string key)
    {
        pack = null!; key = "";
        int i = path.IndexOf(Ext, StringComparison.OrdinalIgnoreCase);
        while (i >= 0)
        {
            int end = i + Ext.Length;
            if (end == path.Length || path[end] is '/' or '\\')
            {
                var file = path[..end];
                var p = Load(file);
                if (p != null) { pack = p; key = end == path.Length ? "" : Key(path[(end + 1)..]); return true; }
            }
            i = path.IndexOf(Ext, end, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static Pack? Load(string file)
    {
        lock (_lock)
        {
            if (_packs.TryGetValue(file, out var p)) return p;
            p = File.Exists(file) ? Read(file) : null;
            _packs[file] = p;
            return p;
        }
    }

    private static Pack Read(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> head = stackalloc byte[20];
        fs.ReadExactly(head);
        if (!head[..8].SequenceEqual(Magic)) throw new InvalidDataException($"{file} 不是 .cgpak 数据包");
        long indexOff = BinaryPrimitives.ReadInt64LittleEndian(head[8..]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(head[16..]);
        fs.Position = indexOff;
        var idx = new byte[fs.Length - indexOff];
        fs.ReadExactly(idx);
        var pack = new Pack { FilePath = file };
        int o = 0;
        for (int n = 0; n < count; n++)
        {
            int len = BinaryPrimitives.ReadUInt16LittleEndian(idx.AsSpan(o)); o += 2;
            var name = Encoding.UTF8.GetString(idx, o, len); o += len;
            long off = BinaryPrimitives.ReadInt64LittleEndian(idx.AsSpan(o)); o += 8;
            long size = BinaryPrimitives.ReadInt64LittleEndian(idx.AsSpan(o)); o += 8;
            var k = Key(name);
            pack.Files[k] = new Entry(name, off, size);
            for (int s = k.LastIndexOf('/'); s > 0; s = k.LastIndexOf('/', s - 1)) if (!pack.Dirs.Add(k[..s])) break;
        }
        return pack;
    }

    public static bool IsPack(string path) => path.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    public static bool Exists(string path) => Resolve(path, out var p, out var k) ? p.Files.ContainsKey(k) : File.Exists(path);

    public static bool DirectoryExists(string path) => Resolve(path, out var p, out var k) ? p.Dirs.Contains(k) : Directory.Exists(path);

    public static Stream OpenRead(string path)
    {
        if (!Resolve(path, out var p, out var k)) return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
        if (!p.Files.TryGetValue(k, out var e)) throw new FileNotFoundException("数据包里没有这个文件", path);
        return new SubStream(new FileStream(p.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess), e.Offset, e.Length);
    }

    public static byte[] ReadAllBytes(string path)
    {
        if (!Resolve(path, out _, out _)) return File.ReadAllBytes(path);
        using var s = OpenRead(path);
        var b = new byte[s.Length];
        s.ReadExactly(b);
        return b;
    }

    public static string ReadAllText(string path) => Resolve(path, out _, out _) ? Encoding.UTF8.GetString(ReadAllBytes(path)) : File.ReadAllText(path);

    public static IEnumerable<string> ReadLines(string path) =>
        Resolve(path, out _, out _) ? ReadAllText(path).Split('\n').Select(l => l.TrimEnd('\r')) : File.ReadLines(path);

    /// <summary>列出目录下的文件（返回完整路径，和 Directory.EnumerateFiles 一样）。包里的文件名保留原来的大小写。</summary>
    public static IEnumerable<string> EnumerateFiles(string dir, bool recursive = false)
    {
        if (!Resolve(dir, out var p, out var k)) return Directory.EnumerateFiles(dir, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        var prefix = k.Length == 0 ? "" : k + "/";
        var root = dir.TrimEnd('/', '\\');
        return p.Files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal) && (recursive || f.Key.IndexOf('/', prefix.Length) < 0))
            .Select(f => root + "/" + f.Value.Name[prefix.Length..]).ToList();
    }

    public static IEnumerable<string> EnumerateDirectories(string dir)
    {
        if (!Resolve(dir, out var p, out var k)) return Directory.EnumerateDirectories(dir);
        var prefix = k.Length == 0 ? "" : k + "/";
        var root = dir.TrimEnd('/', '\\');
        // 目录名保留大小写：从任一文件的原名里取
        return p.Dirs.Where(d => d.Length > prefix.Length && d.StartsWith(prefix, StringComparison.Ordinal) && d.IndexOf('/', prefix.Length) < 0)
            .Select(d => { var any = p.Files.First(f => f.Key.StartsWith(d + "/", StringComparison.Ordinal)).Value.Name; return root + "/" + any[prefix.Length..d.Length]; })
            .ToList();
    }

    /// <summary>包内一段数据的只读、可定位流。</summary>
    private sealed class SubStream : Stream
    {
        private readonly FileStream _f; private readonly long _start, _len; private long _pos;
        public SubStream(FileStream f, long start, long len) { _f = f; _start = start; _len = len; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _len;
        public override long Position { get => _pos; set => _pos = Math.Clamp(value, 0, _len); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int n = (int)Math.Min(buffer.Length, _len - _pos);
            if (n <= 0) return 0;
            _f.Position = _start + _pos;
            n = _f.Read(buffer[..n]);
            _pos += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) =>
            Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _pos + offset, _ => _len + offset };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _f.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>
    /// 打包：把 root 下的 files（相对路径）写成一个 .cgpak。progress 回报 (已写字节, 总字节)。
    /// </summary>
    public static void Build(string root, IReadOnlyList<string> files, string output, Action<long, long>? progress = null)
    {
        long total = files.Sum(f => new FileInfo(Path.Combine(root, f)).Length), done = 0;
        var entries = new List<(string Name, long Off, long Len)>();
        using (var o = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            o.Write(Magic); o.Write(new byte[12]);
            var buf = new byte[1 << 20];
            foreach (var f in files)
            {
                long off = o.Position;
                using (var i = File.OpenRead(Path.Combine(root, f)))
                {
                    int n;
                    while ((n = i.Read(buf)) > 0) { o.Write(buf, 0, n); done += n; progress?.Invoke(done, total); }
                }
                entries.Add((f.Replace('\\', '/'), off, o.Position - off));
            }
            long indexOff = o.Position;
            var bw = new BinaryWriter(o, Encoding.UTF8, leaveOpen: true);
            foreach (var (name, off, len) in entries)
            {
                var b = Encoding.UTF8.GetBytes(name);
                bw.Write((ushort)b.Length); bw.Write(b); bw.Write(off); bw.Write(len);
            }
            bw.Flush();
            o.Position = 8; bw.Write(indexOff); bw.Write(entries.Count); bw.Flush();
        }
        lock (_lock) _packs.Remove(output);
    }
}
