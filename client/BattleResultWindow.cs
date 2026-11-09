using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 「戰鬥結果」窗口（官方 4.06 bob 实测）：23380（616×308）在 (172,206)，战斗结束后显示几秒自己关（点 X 也关）。
///  左上 經驗值：4 行（人物 + 宠物），行 y = 60+25i：健康小方块 (47, 行+3)、名字居中 x132、经验右对齐 x302（"+4"）。
///  左下 物品：3 个格子 243139（(19,185) (19,247) (263,213)），名字写在格子旁边的线上。
///  右边 技能經驗值：每个技能一条 23381 在 (332, 54+24i)，名字 x360、经验右对齐 x597（练不了的技能 "--"）。
/// </summary>
public partial class BattleResultWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public static double ShowSeconds = 6;
    private int _gen;
    public override void _Ready() { Layer = 18; Visible = false; Build(new Vector2(172, 206), false); }

    public void Show(NetworkSession.BattleResult r)
    {
        Visible = true; Refresh();
        int g = ++_gen;
        GetTree().CreateTimer(ShowSeconds).Timeout += () => { if (g == _gen) Visible = false; };
    }

    public string Text { get; private set; } = "";

    public override void Refresh()
    {
        Clear();
        var s = Session(); var r = s?.LastResult;
        if (s == null || r == null) return;
        Body.AddChild(new ColorRect { Color = new Color(0, 0, 58 / 255f, 0.55f), Position = new Vector2(4, 4), Size = new Vector2(608, 300), MouseFilter = Control.MouseFilterEnum.Stop });
        if (Cache.ImageNo(23380).Tex is { } bg)
        {
            // 图里是浅蓝底（游戏里是半透明压暗）：底色换透明
            var img = bg.GetImage(); img.Convert(Image.Format.Rgba8);
            var key = img.GetPixel(400, 200);
            for (int y = 0; y < img.GetHeight(); y++) for (int x = 0; x < img.GetWidth(); x++) if (img.GetPixel(x, y).IsEqualApprox(key)) img.SetPixel(x, y, new Color(0, 0, 0, 0));
            Body.AddChild(new TextureRect { Texture = ImageTexture.CreateFromImage(img), MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        Btn(22509, 22510, 594, 9, () => Visible = false);
        var lines = new List<string>();
        for (int i = 0; i < r.Rows.Count && i < 4; i++)
        {
            var row = r.Rows[i]; float y = 60 + 25 * i;
            string name; Color health = new(0, 0.9f, 0);
            if (row.PetSlot < 0) name = s.Player.Name;
            else name = s.Player.Pets.FirstOrDefault(p => p.Slot == row.PetSlot)?.DisplayName ?? "";
            Body.AddChild(new ColorRect { Color = health, Position = new Vector2(47, y + 3), Size = new Vector2(10, 10), MouseFilter = Control.MouseFilterEnum.Ignore });
            Txt(name, 132, y + 6, center: true);
            Txt($"+{row.Exp}", 302, y + 6, right: true);
            if (row.LevelUp) Txt("LV UP", 262, y + 6, new Color(1, 1, 0), right: true);
            lines.Add($"{name}+{row.Exp}{(row.LevelUp ? " 升级" : "")}");
        }
        var cells = new[] { new Vector2(19, 185), new Vector2(19, 247), new Vector2(263, 213) };
        var nameAt = new[] { new Vector2(75, 196), new Vector2(75, 258), new Vector2(75, 222) };
        for (int i = 0; i < 3; i++)
        {
            Img(243139, cells[i].X, cells[i].Y);
            if (i >= r.Items.Count) continue;
            var (id, nm) = r.Items[i];
            var it = s.Player.Items.FirstOrDefault(x => x.ItemId == id);
            if (it != null && it.Graphic > 0 && Cache.ImageNo(it.Graphic).Tex is { } tex)
                Body.AddChild(new TextureRect { Texture = tex, Position = cells[i] + (new Vector2(48, 48) - tex.GetSize()) / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
            Txt(nm, nameAt[i].X, nameAt[i].Y);
            lines.Add("得到 " + nm);
        }
        int k = 0;
        foreach (var sk in s.Player.Skills.OrderBy(x => x.Slot))
        {
            if (k >= 10) break;
            float y = 54 + 24 * k++;
            Img(23381, 332, y);
            Txt(sk.Name, 360, y + 9);
            string v = sk.MaxLevel == 0 && sk.Techs.Count == 0 ? "--" : r.SkillExp.TryGetValue(sk.Slot, out var e) ? $"+{e.Exp}" : "+0";
            Txt(v, 597, y + 9, right: true);
        }
        Text = string.Join(" / ", lines);
    }
}
