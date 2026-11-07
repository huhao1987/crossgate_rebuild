using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 银行（保险箱）窗口（官方 4.06 实测，跟银行员说话后服务端发 BZ 类型 23）：背景 23340（560×364）在 (200,148)，跟交易窗口同一个位置。
/// 左边是保险箱，右边是自己。上面四个标签 物品/寵物/存錢/提錢，选中哪个就把对应的整块图 23341–23344 贴在 (4,58)（图里带着选中的标签）。
///  物品：两边各 5×4 格（格子左上 13+52c / 291+52c, 94+52r），双击自己的道具存进去，双击保险箱的取出来。
///  寵物：两边各 5 行（横线 y = 140+22k），名字在线上方居中（147 / 425），双击存/取。
///  存錢：左 存款；右 金幣、存入 + 小键盘（5–9 / 0–4，18×18，x 333+24i，y 199/223）、更正/全部/清除、確定 22712 (380,266)。
///  提錢：镜像（左边 −278）：左 存款、提出 + 小键盘、確定 (102,266)；右 金幣。
/// 底部：手续费、持有金额；最下面一排页码 1–9（243170+3×数字，当前页用 +1 的暗色）在 (12+28k, 337)。关闭 22509 (538,6)，关窗口不发包。
/// </summary>
public partial class BankWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public int Tab { get; private set; }   // 0 物品 1 寵物 2 存錢 3 提錢
    private string _input = "";
    private long _lastClick; private string _lastKey = "";

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(200, 148), false); }

    public void Open() { _input = ""; Tab = 0; Visible = true; Refresh(); }
    public long Input => long.TryParse(_input, out var v) ? v : 0;

    public override void Refresh()
    {
        Clear();
        var s = Session();
        var b = s?.Bank;
        if (s == null || b == null) { Visible = false; return; }
        Img(23340, 0, 0, block: true);
        Img(23341 + Tab, 4, 58);
        Txt(s.Player.Name, 432, 12, center: true);
        // 计数：保险箱（个/只/G）、自己（个/只/G）
        var bag = s.Player.Items.Where(x => x.Slot >= 8 && x.Slot < 28).ToList();
        Counter($"{b.Items.Count(x => x != null)} 个", 50); Counter($"{b.Pets.Count(x => x != null)} 只", 140); Counter($"{b.Gold} G", 230);
        Counter($"{bag.Count} 个", 329); Counter($"{s.Player.Pets.Count} 只", 419); Counter($"{s.Player.Gold} G", 509);
        for (int i = 0; i < 4; i++) { int k = i; Hit(new[] { 8f, 148, 284, 422 }[i], 62, 128, 20, _ => { Tab = k; _input = ""; Refresh(); }, box: false); }

        switch (Tab)
        {
            case 0:
                for (int c = 0; c < 20; c++)
                {
                    var p = new Vector2(13 + 52 * (c % 5), 94 + 52 * (c / 5));
                    if (b.Items[c] is { } it) Icon(it.Image, it.Count, p);
                    int k = c; Hit(p.X, p.Y, 48, 48, _ => Dbl("b" + k, () => s.BankWithdraw(k)));
                }
                for (int c = 0; c < 20; c++)
                {
                    var p = new Vector2(291 + 52 * (c % 5), 94 + 52 * (c / 5));
                    int slot = c + 8;
                    if (bag.FirstOrDefault(x => x.Slot == slot) is { } it) Icon(it.Graphic, it.Count, p);
                    Hit(p.X, p.Y, 48, 48, _ => Dbl("m" + slot, () => s.BankDeposit(slot)));
                }
                break;
            case 1:
                for (int k = 0; k < 5; k++)
                {
                    float line = 140 + 22 * k;
                    if (b.Pets[k] is { } bp) Txt(bp.OrigName.Length > 0 ? bp.OrigName : bp.Name, 147, line - 12, center: true);
                    int kk = k; Hit(64, line - 20, 166, 20, _ => Dbl("bp" + kk, () => s.BankWithdrawPet(kk)));
                    var mp = s.Player.Pets.FirstOrDefault(x => x.Slot == k);
                    if (mp != null) Txt(mp.Name, 425, line - 12, center: true);
                    Hit(342, line - 20, 166, 20, _ => Dbl("mp" + kk, () => s.BankDepositPet(kk)));
                }
                break;
            case 2:
                Txt(b.Gold.ToString(), 200, 155, right: true);
                Txt(s.Player.Gold.ToString(), 476, 123, right: true);
                Txt(Input.ToString(), 476, 163, right: true);
                Keypad(0, s.Player.Gold);
                Btn(22712, 22713, 380, 266, () => { s.BankGold(true, Math.Min(Input, s.Player.Gold)); _input = ""; });
                break;
            case 3:
                Txt(b.Gold.ToString(), 200, 123, right: true);
                Txt(Input.ToString(), 200, 163, right: true);
                Txt(s.Player.Gold.ToString(), 476, 155, right: true);
                Keypad(-278, b.Gold);
                Btn(22712, 22713, 102, 266, () => { s.BankGold(false, Math.Min(Input, b.Gold)); _input = ""; });
                break;
        }
        // 底部两条
        Txt("手续费", 62, 311); Txt("0", 186, 311, right: true); Txt("金币", 194, 311);
        Txt("持有金额", 331, 311); Txt(s.Player.Gold.ToString(), 471, 311, right: true); Txt("金币", 481, 311);
        // 页码
        for (int pg = 1; pg <= 9; pg++)
        {
            int p = pg;
            int img = 243170 + 3 * pg;
            if (pg == b.Page) Img(img + 1, 12 + 28 * (pg - 1), 337);
            else Btn(img, img + 2, 12 + 28 * (pg - 1), 337, () => { s.BankPage(p); Refresh(); });
        }
        Btn(22509, 22510, 538, 6, () => s.CloseBank());
    }

    private void Counter(string s, float cx) => Txt(s, cx, 41, center: true);

    private void Icon(int image, int count, Vector2 p)
    {
        if (image > 0 && Cache.ImageNo(image).Tex is { } tex)
            Body.AddChild(new TextureRect { Texture = tex, Position = p + (new Vector2(48, 48) - tex.GetSize()) / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
        if (count > 1) Txt(count.ToString(), p.X + 46, p.Y + 34, new Color(0.4f, 1, 1), right: true);
    }

    /// <summary>官方是双击才存/取。</summary>
    private void Dbl(string key, Func<bool> act)
    {
        long now = (long)Time.GetTicksMsec();
        bool dbl = key == _lastKey && now - _lastClick < 500;
        _lastClick = now; _lastKey = key;
        if (dbl) { _lastKey = ""; act(); }
    }

    private void Keypad(float dx, long all)
    {
        for (int d = 0; d <= 9; d++)
        {
            int k = d;
            float x = 333 + 24 * (d >= 5 ? d - 5 : d) + dx, y = d >= 5 ? 199 : 223;
            Hit(x, y, 18, 18, _ => { if (_input.Length < 9) { _input = (_input + k).TrimStart('0'); Refresh(); } });
        }
        Hit(453 + dx, 197, 52, 14, _ => { if (_input.Length > 0) { _input = _input[..^1]; Refresh(); } });   // 更正
        Hit(453 + dx, 213, 52, 14, _ => { _input = all > 0 ? all.ToString() : ""; Refresh(); });              // 全部
        Hit(453 + dx, 229, 52, 14, _ => { _input = ""; Refresh(); });                                        // 清除
    }

    // 测试用
    public void TestTab(int t) { Tab = t; _input = ""; Refresh(); }
    public void TestInput(long v) { _input = v > 0 ? v.ToString() : ""; Refresh(); }
}
