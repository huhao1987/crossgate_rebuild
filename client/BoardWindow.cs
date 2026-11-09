using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 留言板（服务端 BZ 种类 21，Dengon，官方 exe 0x4210d9 读、0x42b2eb 画）。窗口 576×456 在 ((W−576)/2,(H−456)/2)，背景 23224（600×438，跟 23050 一样往左上偏 12,3）。
/// 坐标相对窗口左上角：标题（服务端第 1 段）中心 (302,8)；4 条留言，第 k 条基准 y = 72+76k：
///   头像（第 1 段图像编号）(44,y)、名字中心 (160,y−28)、时间 "%04d/%02d/%02d %02d:%02d" 灰字 (249,y−28)、内容 50 字节折行 (90,y)、
///   能删的（第 3 段非 0）右边「删除」23218/23219 中心 (552,y−20)。
/// 输入框 (16,389)；「留言」23216/23217 (552,394)、「不再留言」23214/23215 (552,420)；有上一页（掩码 0x10）23225/23226 (190,354)、有下一页（0x20）23227/23228 (390,354)。
/// 发包：删除第 i 条 → xD … 40(62 进制 "E") "i"；留言 → xD … 1 转义后的文字（空的话提示"请输入文字。"、音效 55）；上一页 0x10、下一页 0x20 数据空；不再留言只关窗口（音效 57）。
/// 窗口不关，服务端会重发整个留言板。
/// </summary>
public partial class BoardWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public DialogPage? Page { get; private set; }
    private LineEdit? _input;
    private string _keep = "";

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(192, 132), false); }

    public void ShowPage(DialogPage? p)
    {
        if (_input != null) _keep = _input.Text;
        Page = p?.Kind == "board" ? p : null;
        Visible = Page != null;
        if (Visible) Refresh(); else _keep = "";
    }

    public override void Refresh()
    {
        Clear();
        if (Page?.Shop is not { } list) return;
        Img(23224, -12, -3, block: true);
        Txt(Page.NpcName, 302, 14, Colors.White, center: true);
        for (int k = 0; k < list.Count && k < 4; k++)
        {
            var e = list[k]; float y = 72 + 76 * k; int kk = k;
            if (e.Image > 0 && Cache.ImageNo(e.Image) is (ImageTexture face, var fe) && fe != null)
                Body.AddChild(new TextureRect { Texture = face, Position = new Vector2(44 + fe.OffsetX, y + fe.OffsetY), MouseFilter = Control.MouseFilterEnum.Ignore });
            if (e.Name.Length > 0) Txt(e.Name, 160, y - 22, Colors.White, center: true);
            if (e.Price > 0)
            {
                var t = DateTimeOffset.FromUnixTimeSeconds(e.Price).ToLocalTime();
                Txt($"{t.Year:0000}/{t.Month:00}/{t.Day:00} {t.Hour:00}:{t.Minute:00}", 249, y - 22, ChatLog.Palette[7]);
            }
            if (e.Desc.Length > 0)
                Body.AddChild(new CgText { Wrap = 50, LineHeight = CgText.LineH, Text = e.Desc, Position = new Vector2(90, y + 1) });
            if (e.Count != 0) CBtn(23218, 23219, 552, y - 20, () => Delete(kk), "删除此留言。");
        }
        _input = new LineEdit
        {
            Position = new Vector2(16, 380), Size = new Vector2(500, 22), MaxLength = 50, Text = _keep, Flat = true,
            PlaceholderText = "", CaretBlink = true,
        };
        _input.AddThemeColorOverride("font_color", Colors.White);
        _input.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _input.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _input.TextSubmitted += _ => Write();
        Body.AddChild(_input);
        CBtn(23216, 23217, 552, 394, Write, "进行留言。");
        CBtn(23214, 23215, 552, 420, Close, "不再留言。");
        if (Page.Buttons.Contains("prev")) CBtn(23225, 23226, 190, 354, () => Turn(0x10), "转到前一页。");
        if (Page.Buttons.Contains("next")) CBtn(23227, 23228, 390, 354, () => Turn(0x20), "转到后一页。");
    }

    public string Input { get => _input?.Text ?? ""; set { if (_input != null) _input.Text = value; } }

    public void Write()
    {
        var text = Input.Trim();
        if (text.Length == 0) { GameAudio.Se(55); return; }
        GameAudio.Se(79);
        Input = ""; _keep = "";
        Session()?.WindowSend(1, NetworkSession.WindowEscape(text));
    }
    public void Delete(int k) { GameAudio.Se(51); Session()?.WindowSend(0x40, k.ToString()); }
    public void Turn(int bits) { GameAudio.Se(79); Session()?.WindowSend(bits, ""); }
    public void Close() { GameAudio.Se(57); Visible = false; Page = null; _keep = ""; Session()?.WindowClose(); }

    private void CBtn(int img, int pressed, float cx, float cy, Action a, string tip)
    {
        var sz = Cache.ImageNo(img).Tex?.GetSize() ?? Vector2.Zero;
        var b = Btn(img, pressed, cx - sz.X / 2, cy - sz.Y / 2, a);
        if (b != null) b.TooltipText = tip;
    }
}
