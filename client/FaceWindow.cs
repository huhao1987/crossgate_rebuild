using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 改脸（BZ 36，exe 0x431b81；官方 gm_win 实拍 2026-11-10）：九宫格 7×5 格（448×240），居中。
/// 左上自己的头像（中心 (100,64)），右边三行「脸部」「眼睛」「嘴巴」，各有 ◀ 22762/22763 (178,39+22k)、▶ 22764/22765 (308,39+22k)；
/// 话在头像下面；「是」「否」黄字 (145,211)/(294,211)。
/// 头像 = 底 + 5×眼睛 + 嘴巴（眼睛、嘴巴 0–4 轮转，exe 0x431da7…）；脸部按颜色换底（+25，四种轮转；官方还要查这个形象允许的脸型表，没做）。
/// 是 → xD … 是 新头像图号；否 / 关 → xD … 否。
/// </summary>
public partial class FaceWindow : CgWindow
{
    public Func<int> CurrentFace = () => 0;
    public Action<string, string>? Submit;   // (按钮, 数据)
    public DialogPage? Page { get; private set; }
    public int Face { get; private set; }

    public override void _Ready() { Layer = 20; Visible = false; Build(new Vector2((960 - 448) / 2, 230), false); }   // 官方实拍窗口顶在 230（按申报高度居中，画出来 5 格）

    public void ShowPage(DialogPage? p)
    {
        Page = p?.Kind == "face" ? p : null;
        Visible = Page != null;
        if (Page != null) { Face = CurrentFace(); Refresh(); }
    }

    public override void Refresh()
    {
        Clear();
        if (Page == null) return;
        var size = new Vector2(448, 240);
        Body.AddChild(new Control { Size = size, MouseFilter = Control.MouseFilterEnum.Stop });
        Body.AddChild(CgFrame.Tint(size));
        Body.AddChild(new CgFrame { Cache = Cache, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore, CenterAlpha = 0 });
        if (Cache.ImageNo(Face).Tex is { } t)
            Body.AddChild(new TextureRect { Texture = t, Position = new Vector2(100, 64) - t.GetSize() / 2, MouseFilter = Control.MouseFilterEnum.Ignore });
        string[] names = { "脸部", "眼睛", "嘴巴" };
        for (int k = 0; k < 3; k++)
        {
            int kk = k;
            Btn(22762, 22763, 178, 39 + 22 * k - 10, () => Step(kk, -1));
            Btn(22764, 22765, 308, 39 + 22 * k - 10, () => Step(kk, +1));
            Txt(names[k], 229, 39 + 22 * k - 6 + 6, Colors.White);
        }
        if (Page.Text.Length > 0) Body.AddChild(new CgText { Wrap = 46, Text = Page.Text, Position = new Vector2(20, 120) });
        var yellow = ChatLog.Palette[4];
        if (Page.Buttons.Contains("yes")) { Txt("是", 145, 211, yellow); Hit(135, 205, 30, 20, _ => Yes(), box: false); }
        if (Page.Buttons.Contains("no")) { Txt("否", 294, 211, yellow); Hit(284, 205, 30, 20, _ => No(), box: false); }
    }

    /// <summary>which：0 脸部（颜色 +25 轮转 4 种）、1 眼睛、2 嘴巴（0–4 轮转）。</summary>
    public void Step(int which, int dir)
    {
        int b = Face - Face % 100, col = Face % 100 / 25, eye = Face % 25 / 5, mouth = Face % 5;
        if (which == 0) col = (col + dir + 4) % 4;
        else if (which == 1) eye = (eye + dir + 5) % 5;
        else mouth = (mouth + dir + 5) % 5;
        Face = b + 25 * col + 5 * eye + mouth;
        GameAudio.Se(51);
        Refresh();
    }

    public void Yes() { GameAudio.Se(79); Visible = false; var p = Page; Page = null; if (p != null) Submit?.Invoke("yes", Face.ToString()); }
    public void No() { GameAudio.Se(57); Visible = false; var p = Page; Page = null; if (p != null) Submit?.Invoke("no", ""); }
}
