using CgPort.Core;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 地图显示：地面只画可见范围；物件、NPC、角色放进 y 排序层。
/// 通行网格用与服务端相同的规则在客户端本地计算（只用于寻路提示，最终以服务端校验为准）。
/// </summary>
public partial class MapView : Node2D
{
    [Signal] public delegate void TileClickedEventHandler(Vector2I cell);
    /// <summary>按住不放（手指或鼠标）时每隔一会儿发一次：手指下面那一格。官方 PC 版按住鼠标人物会一直朝鼠标走。</summary>
    [Signal] public delegate void TileHeldEventHandler(Vector2I cell);
    private bool _held; private Vector2 _heldPos; private double _heldT;

    public MapData Map = null!;
    /// <summary>大图切块地图（GmNM 第 9 段 = 1）：编号 1000+ 不查 GraphicInfo 地图编号，而是 cut 文件里的第 k 块。</summary>
    public CutTable? Cut;
    /// <summary>切块地图上画：图 + 源区域 + 锚点（锚点对准格子中心）。</summary>
    public (Texture2D Tex, Rect2 Src, Vector2 Off)? CutPiece(int id)
    {
        if (Cut == null || !Cut.TryGet(id, out var p) || p.Kind != 0) return null;
        if (Cache.ImageNo(p.Image) is not (ImageTexture tex, var e) || e == null) return null;
        // 源区域是在 640×480 的画布上量的：画布中心 (320,240) = 图的锚点，图左上角在画布 (320+偏移x, 240+偏移y)
        var src = new Rect2(p.Sx - (320 + e.OffsetX), p.Sy - (240 + e.OffsetY), p.W, p.H);
        var clip = src.Intersection(new Rect2(0, 0, tex.GetWidth(), tex.GetHeight()));
        if (clip.Size.X <= 0 || clip.Size.Y <= 0) return null;
        return (tex, clip, new Vector2(-p.Ox, -p.Oy) + (clip.Position - src.Position));
    }
    public Walkability Walk = null!;
    public TextureCache Cache = null!;
    public Node2D Entities = null!;
    public int MissingTiles => _ground?.Missing ?? 0;
    private GroundLayer _ground = null!;
    private Rect2I _lastView;
    private int _lastVersion;

    /// <summary>调色板换了：物件精灵引用的是旧纹理，全部释放后按需重建。</summary>
    private void RespawnAllChunks()
    {
        foreach (var l in _liveChunks.Values) foreach (var n in l) n.QueueFree();
        _liveChunks.Clear();
    }
    // 物件按 16×16 格分区块：只生成镜头附近的区块，远离后释放（最大的地图有 800×800 格）
    private const int Chunk = 16;
    private Node2D _floorObjects = null!;
    /// <summary>调试：物件按 GraphicInfo 标志上色（45 红、1 绿）。环境变量 CG_DEBUG_FLAGS=1。</summary>
    public static bool DebugFlags = System.Environment.GetEnvironmentVariable("CG_DEBUG_FLAGS") == "1";
    private readonly Dictionary<Vector2I, List<(int X, int Y, int MapId)>> _chunkObjects = new();
    private readonly Dictionary<Vector2I, List<Node2D>> _liveChunks = new();
    public int LiveObjectCount => _liveChunks.Values.Sum(l => l.Count);
    public int TotalObjectCount => _chunkObjects.Values.Sum(l => l.Count);

    public void Build(MapData map, Walkability walk, TextureCache cache)
    {
        Map = map; Walk = walk; Cache = cache;
        _ground = new GroundLayer { View = this };
        AddChild(_ground);
        // 台阶类物件（GraphicInfo 标志 = 45，实测法兰城/召唤之间里的台阶全是 45）贴着地面画，永远在人物下面，
        // 否则大台阶按锚点排序会把站在上面的人盖住。标志 1（路灯、小草、传送标记）和 0 照常和人物一起排序。
        _floorObjects = new Node2D { YSortEnabled = true };
        AddChild(_floorObjects);
        Entities = new Node2D { YSortEnabled = true };
        AddChild(Entities);
        IndexObjects(0, 0, map.Width, map.Height);
    }

    // 物件编号 ≤ 999 不是图：0–10 是通行标记，其余（如 200–263 编辑器标记"凤"字橙块）官方都不画（exe 0x4343ff 起）
    private void IndexObjects(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int mid = Map.Objects[Map.Index(x, y)];
                if (mid <= 999) continue;
                var key = new Vector2I(x / Chunk, y / Chunk);
                if (!_chunkObjects.TryGetValue(key, out var l)) _chunkObjects[key] = l = new();
                l.Add((x, y, mid));
            }
    }

    /// <summary>服务端补了一块地图（aBS）：重建涉及的物件区块、重画地面。</summary>
    public void ApplyPatch(int x1, int y1, int x2, int y2)
    {
        x1 = Math.Max(0, x1); y1 = Math.Max(0, y1); x2 = Math.Min(Map.Width, x2); y2 = Math.Min(Map.Height, y2);
        if (x2 <= x1 || y2 <= y1) return;
        int cx0 = x1 / Chunk, cy0 = y1 / Chunk, cx1 = (x2 - 1) / Chunk, cy1 = (y2 - 1) / Chunk;
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                var k = new Vector2I(cx, cy);
                _chunkObjects.Remove(k);
                IndexObjects(cx * Chunk, cy * Chunk, Math.Min(Map.Width, (cx + 1) * Chunk), Math.Min(Map.Height, (cy + 1) * Chunk));
                if (_liveChunks.Remove(k, out var nodes)) foreach (var n in nodes) n.QueueFree();
            }
        _lastView = default;   // 下一帧重新生成可见区块、重画地面
    }

    private void SpawnChunk(Vector2I key)
    {
        var nodes = new List<Node2D>();
        if (_chunkObjects.TryGetValue(key, out var list))
            foreach (var (x, y, mid) in list)
            {
                if (Cut != null && Cut.TryGet(mid, out var cp))
                {
                    if (cp.Kind == 1)
                    {
                        // 动画块：在锚点画这个动画
                        var acts = Cache.Library.AnimeActions(cp.Image);
                        if (acts.Count == 0) continue;
                        var ak = acts.Keys.OrderBy(q => q.Act).ThenBy(q => q.Dir).First();
                        var an = new AnimSprite { Cache = Cache, AnimeId = cp.Image, Dir = ak.Dir, Act = ak.Act, FallbackAct = ak.Act, Position = Iso.ToScreen(x, y) + new Vector2(cp.Ox, cp.Oy) };   // 动画块：锚点字段是相对格子的偏移（有符号，exe 0x433fda 加上去）
                        Entities.AddChild(an); nodes.Add(an);
                    }
                    else if (CutPiece(mid) is var (ctex, csrc, coff))
                    {
                        var cs = new Sprite2D { Texture = ctex, Centered = false, RegionEnabled = true, RegionRect = csrc, Position = Iso.ToScreen(x, y), Offset = coff };
                        Entities.AddChild(cs); nodes.Add(cs);
                    }
                    continue;
                }
                var (tex, e) = Cache.ByMapId(mid);
                if (tex == null || e == null) continue;
                // 锚点就是占位区最靠前的格子（物件从锚点往东、往北占格），直接用它排序
                var sp = new Sprite2D { Texture = tex, Centered = false, Position = Iso.ToScreen(x, y), Offset = new Vector2(e.OffsetX, e.OffsetY) };
                if (DebugFlags) sp.Modulate = e.Flag == 45 ? new Color(1, 0.3f, 0.3f) : e.Flag == 1 ? new Color(0.3f, 1, 0.3f) : Colors.White;
                (e.Flag == 45 ? _floorObjects : Entities).AddChild(sp);
                nodes.Add(sp);
            }
        _liveChunks[key] = nodes;
    }

    /// <summary>保证可见格子范围（再往外扩一圈区块，大物件可能伸进画面）内的区块已生成，释放远处的。</summary>
    private void UpdateChunks(Rect2I cells)
    {
        int cx0 = Math.Max(0, cells.Position.X / Chunk - 1), cy0 = Math.Max(0, cells.Position.Y / Chunk - 1);
        int cx1 = Math.Min((Map.Width - 1) / Chunk, cells.End.X / Chunk + 1), cy1 = Math.Min((Map.Height - 1) / Chunk, cells.End.Y / Chunk + 1);
        var want = new HashSet<Vector2I>();
        for (int cy = cy0; cy <= cy1; cy++) for (int cx = cx0; cx <= cx1; cx++) want.Add(new Vector2I(cx, cy));
        foreach (var k in want) if (!_liveChunks.ContainsKey(k)) SpawnChunk(k);
        foreach (var k in _liveChunks.Keys.ToList())
        {
            // 留一圈缓冲，避免来回走动时反复生成
            if (k.X >= cx0 - 1 && k.X <= cx1 + 1 && k.Y >= cy0 - 1 && k.Y <= cy1 + 1) continue;
            foreach (var n in _liveChunks[k]) n.QueueFree();
            _liveChunks.Remove(k);
        }
    }

    public override void _Process(double delta)
    {
        ProcessHeld(delta);
        var cam = GetViewport().GetCamera2D();
        if (cam == null || Map == null) return;
        var size = GetViewportRect().Size / cam.Zoom;
        var r = new Rect2(cam.GetScreenCenterPosition() - size / 2, size);
        var cs = new[] { Iso.ToCell(r.Position), Iso.ToCell(r.Position + new Vector2(r.Size.X, 0)), Iso.ToCell(r.Position + new Vector2(0, r.Size.Y)), Iso.ToCell(r.End) };
        var mn = new Vector2I(cs.Min(c => c.X), cs.Min(c => c.Y)) - new Vector2I(2, 2);
        var mx = new Vector2I(cs.Max(c => c.X), cs.Max(c => c.Y)) + new Vector2I(3, 3);
        var view = new Rect2I(mn, mx - mn);
        if (view != _lastView || Cache.Version != _lastVersion)
        {
            if (Cache.Version != _lastVersion) RespawnAllChunks();
            _lastView = view; _lastVersion = Cache.Version; _ground.Visible = view; _ground.QueueRedraw(); UpdateChunks(view);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // 触摸会被 Godot 转成鼠标事件（emulate_mouse_from_touch，默认开启），所以这里只处理鼠标左键，避免一次点按触发两次
        Vector2? pos = e switch
        {
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb => mb.Position,
            _ => null,
        };
        if (pos is Vector2 p)
        {
            _held = true; _heldPos = p; _heldT = 0;
            EmitSignal(SignalName.TileClicked, CellAt(p));
        }
    }

    private Vector2I CellAt(Vector2 p) => Iso.ToCell(GetCanvasTransform().AffineInverse() * p - GlobalPosition);

    /// <summary>松开（在哪儿松开都算）结束"按住走"；按住时跟踪手指位置。</summary>
    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) _held = false;
        else if (e is InputEventMouseMotion mm && _held) _heldPos = mm.Position;
    }

    private void ProcessHeld(double delta)
    {
        if (!_held) return;
        if (!Input.IsMouseButtonPressed(MouseButton.Left)) { _held = false; return; }
        if ((_heldT += delta) < 0.25) return;
        _heldT = 0;
        EmitSignal(SignalName.TileHeld, CellAt(_heldPos));
    }

    private partial class GroundLayer : Node2D
    {
        public MapView View = null!;
        public new Rect2I Visible;
        public int Missing;

        public override void _Draw()
        {
            var m = View.Map;
            int x0 = Math.Clamp(Visible.Position.X, 0, m.Width), y0 = Math.Clamp(Visible.Position.Y, 0, m.Height);
            int x1 = Math.Clamp(Visible.End.X, 0, m.Width), y1 = Math.Clamp(Visible.End.Y, 0, m.Height);
            Missing = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int mid = m.Ground[m.Index(x, y)];
                    if (mid == 0) continue;
                    if (View.Cut != null)
                    {
                        // 切块地图只认 cut 表里的编号；服务端在第 0 行放的标记格（16、地图号、3、41993、239）官方地图文件里没有，不画
                        if (View.CutPiece(mid) is var (ctex, csrc, coff)) DrawTextureRectRegion(ctex, new Rect2(Iso.ToScreen(x, y) + coff, csrc.Size), csrc);
                        continue;
                    }
                    var (tex, e) = View.Cache.ByMapId(mid);
                    if (tex == null || e == null) { Missing++; continue; }
                    DrawTexture(tex, Iso.ToScreen(x, y) + new Vector2(e.OffsetX, e.OffsetY));
                }
        }
    }
}
