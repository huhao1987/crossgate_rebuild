using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 埋起物品（服务端 BZ 种类 29，exe 读 0x422b16、画 0x42a578）：背景 23358 贴在画面右上角 (W−272, 0)，就是背包的样子；
/// 格子 8–27 五列，第 k 格左上 (8+52·列, 36+53·行)，道具图中心 (+24,+24)，数量 "%4d" (+14,+31)。
/// 道具旗标（rU 第 10 段）位 1 = 能埋：点了 → xD … 0 "格子"、关窗口；不能埋 / 是服务端指定不能选的那格（数据第 1 段）→ "这个物品无法埋起来。"（音效 55）。
/// 这台服务端没有会弹出这个窗口的 NPC，没实测过。
/// </summary>
public partial class BuryWindow : CgWindow
{
    public Func<IReadOnlyList<ItemStack>> Items = () => Array.Empty<ItemStack>();
    public Action<string>? Submit;
    public DialogPage? Page { get; private set; }

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(960 - 272, 0), false); }

    public void ShowPage(DialogPage? p)
    {
        Page = p?.Kind == "bury" ? p : null;
        Visible = Page != null;
        if (Visible) Refresh();
    }

    public override void Refresh()
    {
        Clear();
        if (Page == null) return;
        Img(23358, 0, 0, block: true);
        Btn(22509, 22510, 252, 6, Close);   // 官方这个窗口没有关闭钮（不知道怎么退出），移植加一个 ×
        var items = Items();
        for (int slot = 8; slot < 28; slot++)
        {
            int k = slot - 8; float x = 8 + 52 * (k % 5), y = 36 + 53 * (k / 5); int sl = slot;
            var it = items.FirstOrDefault(i => i.Slot == slot);
            if (it != null)
            {
                if (Cache.ImageNo(it.Graphic).Tex is { } t)
                    Body.AddChild(new TextureRect { Texture = t, Position = new Vector2(x + 24, y + 24) - t.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
                if (it.Stackable && it.Count > 0) Txt($"{it.Count,4}", x + 14, y + 37, ChatLog.Palette[1]);
            }
            bool ok = it != null && (it.Flags & 1) != 0 && slot != Page.SourceItem;
            var hit = Hit(x, y, 47, 47, _ => Pick(sl), box: true);
            hit.TooltipText = ok ? "埋起此物品。" : "这个物品无法埋起来。";
        }
    }

    public void Close() { GameAudio.Se(57); Visible = false; Page = null; Submit?.Invoke("cancel"); }

    public void Pick(int slot)
    {
        var it = Items().FirstOrDefault(i => i.Slot == slot);
        if (Page == null || it == null || (it.Flags & 1) == 0 || slot == Page.SourceItem) { GameAudio.Se(55); return; }
        GameAudio.Se(79); Visible = false; Page = null;
        Submit?.Invoke(slot.ToString());
    }
}
