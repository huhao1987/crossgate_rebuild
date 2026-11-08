using CgPort.Client;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 技能导师的学习 / 忘记窗口（官方 4.06 客户端 bob 实测 2026-11-09），576×432 在 (192,132)：
///  学习（BZ 17）背景 23301：左上技能名（中心 175, 55），说明 (16,122) 起行距 21，"使用技能栏栏位数：N 栏" (37,340)、"学习费用：N 金币" (37,380)；
///   右边：对白泡泡里 NPC 的话 (110+268, 78)，NPC 站在 (468,240)，金幣（身上的钱）右对齐 518, 293，「学 习」「不 学」黄字中心 463、y 348 / 384。
///  忘记（BZ 18）背景 23300："现在技能栏使用量的总计：用/总" (29,50)，"请选择要忘记的技能"黄字中心 175, 95；
///   每个技能一条 22703（同技能列表）在 (40, 124+24i)：名字 +30、等级右对齐 +212、栏位右对齐 +247；
///   右边对白、NPC，"※※ 注意 ※※"、"技能只要一忘记，就无法复原，请注意。"、「算了」。
/// 标题栏中间是 NPC 名字；右上 X 关闭。
/// </summary>
public partial class SkillShopWindow : CgWindow
{
    public Func<NetworkSession?> Session = () => null;
    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2(192, 132), false); }

    public override void Refresh()
    {
        Clear();
        var s = Session(); var pg = s?.SkillShop;
        if (s == null || pg == null) { Visible = false; return; }
        Visible = true;
        var yellow = new Color(1, 1, 0);
        Img(pg.Type == 17 ? 23301 : 23300, 0, 0, block: true);
        Btn(22509, 22510, 550, 6, () => s.SkillShopClose());
        Txt(pg.Npc, 288, 15, center: true);
        // 右边：NPC 的话、NPC 本人
        var talk = new CgText { Wrap = 22, Position = new Vector2(378, 73), Text = pg.Text };
        Body.AddChild(talk);
        if (pg.Face > 0 && Cache.ImageNo(pg.Face) is (ImageTexture tex, var e) && e != null)
            Body.AddChild(new TextureRect { Texture = tex, Position = new Vector2(468 + e.OffsetX, 240 + e.OffsetY), MouseFilter = Control.MouseFilterEnum.Ignore });
        if (pg.Type == 17)
        {
            Txt(pg.SkillName, 175, 55, center: true);
            Body.AddChild(new CgText { Wrap = 38, LineHeight = 21, Position = new Vector2(16, 116), Text = pg.Desc });
            Txt("使用技能栏栏位数：", 37, 340); Txt(pg.Slots.ToString(), 222, 340, right: true); Txt("栏", 233, 340);
            Txt("学习费用：", 37, 380); Txt(pg.Cost.ToString(), 190, 380, right: true); Txt("金币", 205, 380);
            Txt(s.Player.Gold.ToString(), 518, 293, right: true);
            Choice("学  习", 463, 348, () => s.SkillShopChoose(1));
            Choice("不  学", 463, 384, () => s.SkillShopClose());
        }
        else
        {
            int used = s.Player.Skills.Sum(k => k.SlotsUsed);
            Txt($"现在技能栏使用量的总计：{used}/{s.Player.SkillSlots}", 29, 50);
            Txt("请选择要忘记的技能", 175, 95, yellow, center: true);
            for (int i = 0; i < pg.Known.Count && i < 10; i++)
            {
                var (name, lv, slots) = pg.Known[i]; float y = 124 + 24 * i; int sel = i + 1;
                Img(22703, 40, y);
                Txt(name, 70, y + 6);
                Txt(lv.ToString(), 252, y + 5, right: true);
                Txt(slots.ToString(), 287, y + 5, right: true);
                Hit(40, y, 256, 20, _ => s.SkillShopChoose(sel));
            }
            Txt("※※    注意    ※※", 463, 290, yellow, center: true);
            Body.AddChild(new CgText { Wrap = 22, LineHeight = 21, Position = new Vector2(370, 313), Text = "技能只要一忘记，就无法复原，请注意。" });
            Choice("算  了", 463, 388, () => s.SkillShopClose());
        }
    }

    private void Choice(string text, float cx, float y, Action a)
    {
        float w = text.Sum(CgText.Cells) * CgText.CellW;
        Hit(cx - w / 2 - 3, y - 4, w + 6, 18, _ => a());
        Txt(text, cx, y, Colors.White, center: true);   // 官方是白字
    }

    public void TestChoose(int sel) => Session()?.SkillShopChoose(sel);
}
