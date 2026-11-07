using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 图鉴（底部菜单"图鉴"，官方 4.06 实测，窗口在 (0,0)）。
/// 列表：背景 22880，69 页 × 15 行（行框顶 35+24i），有登记的行写 "048" (48) 和名字（中心 146），刚登记的行前面贴「新增」22885 (5, 行顶+5)。
///   翻页 ◀ 22881 (60,402) ▶ 22883 (182,402)，页码 "4/69" 中心 134。点有登记的行 → 详情。
/// 详情：背景 22890。上面播放怪物动画；号码 (49,171)、名字（中心 155）；封印图标 (33,191) 22903 可封 / 22904 不可；种族 (192,193)；
///   体力/力量/强度/速度/魔法：星星从 (46, 228+24i) 起，每半颗 8 像素（22901 左半、22902 右半），半颗数 = 值 + 1；
///   地水火风：长条 (187, 240+20j) 高 8，每点 7 像素；技能栏 (197,327)。
///   底部 列表 22891 (12,409)、«10 22893 (98,409)、◀ 22895 (123,409)、"048/1024" 中心 181、▶ 22897 (214,409)、»10 22899 (239,409)。
/// 数据：Ilz 给已登记的号码，dM 给内容（本地没有就发 kza 要）。官方标题栏的绿色"+"是这个私服加的（打开别的窗口），不做。
/// </summary>
public partial class AlbumWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public const int Pages = 69, Rows = 15;
    public int Page { get; private set; }          // 0 起
    public int DetailNo { get; private set; }      // 0 = 列表
    private AnimSprite? _sprite;

    public override void _Ready() { Layer = 16; Visible = false; Build(Vector2.Zero, false); }

    public override void Refresh()
    {
        Clear(); _sprite = null;
        if (DetailNo > 0) Detail(); else List();
        Btn(22509, 22510, 246, 6, () => Visible = false);
    }

    private void List()
    {
        var s = Session();
        Img(22880, 0, 0, block: true);
        for (int i = 0; i < Rows; i++)
        {
            int no = Page * Rows + i + 1;
            float top = 35 + 24 * i;
            if (s == null || !s.HasAlbum(no)) continue;
            if (s.AlbumNew.Contains(no)) Img(22885, 5, top + 5);
            Txt(no.ToString("000"), 48, top + 8);
            if (s.AlbumEntries.TryGetValue(no, out var e)) Txt(e.Name, 146, top + 8, center: true);
            int k = no;
            Hit(44, top, 189, 21, _ => OpenDetail(k));
        }
        Btn(22881, 22882, 60, 402, () => { Page = (Page + Pages - 1) % Pages; Refresh(); });
        Btn(22883, 22884, 182, 402, () => { Page = (Page + 1) % Pages; Refresh(); });
        Txt($"{Page + 1}/{Pages}", 134, 413, center: true);
    }

    private void Detail()
    {
        var s = Session();
        Img(22890, 0, 0, block: true);
        Btn(22891, 22892, 12, 409, () => { Page = (DetailNo - 1) / Rows; DetailNo = 0; Refresh(); });
        Btn(22893, 22894, 98, 409, () => Go(-10));
        Btn(22895, 22896, 123, 409, () => Go(-1));
        Btn(22897, 22898, 214, 409, () => Go(1));
        Btn(22899, 22900, 239, 409, () => Go(10));
        Txt($"{DetailNo:000}/{NetworkSession.AlbumMax}", 181, 414, center: true);
        if (s == null || !s.HasAlbum(DetailNo) || !s.AlbumEntries.TryGetValue(DetailNo, out var e)) return;
        if (e.Anime > 0)
        {
            _sprite = new AnimSprite { Cache = Cache, AnimeId = e.Anime, Dir = 5, Act = 0, FallbackAct = 0, Position = new Vector2(140, 149) };
            Body.AddChild(_sprite);
        }
        Txt(e.No.ToString("000"), 49, 172);
        Txt(e.Name, 155, 172, center: true);
        Img(e.Seal == 1 ? 22903 : 22904, 33, 191);
        Txt(e.Race >= 0 && e.Race < PetInfo.RaceNames.Length ? PetInfo.RaceNames[e.Race] : "", 190, 197);
        for (int i = 0; i < 5; i++)
        {
            int halves = Math.Clamp(e.Stats[i] + 1, 0, 10);
            for (int h = 0; h < halves; h++) Img(h % 2 == 0 ? 22901 : 22902, 46 + 8 * h, 228 + 24 * i);
        }
        var colors = new[] { new Color(0, 1, 0), new Color(0, 1, 1), new Color(1, 0, 0), new Color(1, 1, 0) };
        for (int j = 0; j < 4; j++)
        {
            int w = 7 * Math.Clamp(e.Elements[j], 0, 10);
            if (w > 0) Body.AddChild(new ElementBar { Color = colors[j], Position = new Vector2(187, 240 + 20 * j), Size = new Vector2(w, 8) });
        }
        Txt(e.Slots.ToString(), 197, 329);
    }

    private void Go(int d) { DetailNo = Math.Clamp(DetailNo + d, 1, NetworkSession.AlbumMax); Refresh(); }

    public void OpenDetail(int no)
    {
        DetailNo = no;
        Session()?.AlbumNew.Remove(no);   // 官方：看过详情后「新增」就没了
        Refresh();
    }

    // 测试用
    public void TestPage(int p) { DetailNo = 0; Page = p; Refresh(); }
    public void TestList() { DetailNo = 0; Refresh(); }
}

/// <summary>图鉴详情里的属性长条：圆角（第一行和最后一行两端各缩 1 像素），官方纯色。</summary>
public partial class ElementBar : Control
{
    public Color Color;
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        DrawRect(new Rect2(1, 0, w - 2, 1), Color);
        DrawRect(new Rect2(0, 1, w, h - 2), Color);
        DrawRect(new Rect2(1, h - 1, w - 2, 1), Color);
    }
}

/// <summary>
/// 图鉴内容的本地缓存（官方客户端是 data/album.dat）：一个服务器一个文件 user://album_主机_端口.txt，
/// 每行 号码|图像|封印|?|种族|体力|力量|强度|速度|魔法|地|水|火|风|技能栏|名字。
/// </summary>
public sealed class AlbumCache
{
    private readonly string _path;
    private readonly Dictionary<int, NetworkSession.AlbumEntry> _d = new();

    public AlbumCache(string key)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) key = key.Replace(ch, '_');
        _path = ProjectSettings.GlobalizePath($"user://album_{key.Replace(':', '_')}.txt");
        try
        {
            if (File.Exists(_path))
                foreach (var line in File.ReadAllLines(_path))
                {
                    var f = line.Split('|');
                    if (f.Length < 16) continue;
                    var n = f.Take(15).Select(x => int.TryParse(x, out var v) ? v : 0).ToArray();
                    _d[n[0]] = new NetworkSession.AlbumEntry(n[0], n[1], n[2], n[3], n[4], n[5..10], n[10..14], n[14], string.Join("|", f.Skip(15)));
                }
        }
        catch (Exception e) { GameLog.Write("图鉴缓存", e.Message); }
    }

    public NetworkSession.AlbumEntry? Get(int no) => _d.TryGetValue(no, out var e) ? e : null;

    public void Put(NetworkSession.AlbumEntry e)
    {
        if (_d.TryGetValue(e.No, out var old) && Line(old) == Line(e)) return;
        _d[e.No] = e;
        try { File.WriteAllLines(_path, _d.Values.OrderBy(x => x.No).Select(Line)); }
        catch (Exception ex) { GameLog.Write("图鉴缓存", ex.Message); }
    }

    private static string Line(NetworkSession.AlbumEntry e) =>
        string.Join("|", new[] { e.No, e.Anime, e.Seal, e.Unknown, e.Race }.Concat(e.Stats).Concat(e.Elements).Append(e.Slots)) + "|" + e.Name;
}
