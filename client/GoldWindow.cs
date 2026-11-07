using Godot;

namespace CgPort.Godot;

/// <summary>
/// 物品窗口「钱包」→ 金幣窗口（官方 4.06 实测）：背景 22930（200×200）在 (760,0)，盖在物品窗口的位置上（物品窗口先收起，关掉后再打开）。
/// 總額（身上的钱）、金額（要扔的钱）+ 小键盘（同银行的 5–9 / 0–4，18×18，x 17+24i，y 113/137）、更正/全部/清除、確定 / 取消。
/// 確定 = 把钱扔在身边地上（woz），别人可以捡。
/// </summary>
public partial class GoldWindow : CgWindow
{
    public Func<long> Gold = () => 0;
    public Action<long>? Confirm;
    public Action? Closed;
    private string _input = "";
    public long Input => long.TryParse(_input, out var v) ? v : 0;

    public override void _Ready() { Layer = 17; Visible = false; Build(new Vector2(760, 0), true); }

    public void Open() { _input = ""; Visible = true; Refresh(); }

    public override void Refresh()
    {
        Clear();
        Img(22930, 0, 0, block: true);
        Txt(Gold().ToString(), 163, 49, right: true);
        Txt(Input.ToString(), 163, 89, right: true);
        for (int d = 0; d <= 9; d++)
        {
            int k = d;
            Hit(17 + 24 * (d >= 5 ? d - 5 : d), d >= 5 ? 112 : 136, 18, 18, _ => { if (_input.Length < 9) { _input = (_input + k).TrimStart('0'); Refresh(); } });
        }
        Hit(137, 111, 52, 14, _ => { if (_input.Length > 0) { _input = _input[..^1]; Refresh(); } });   // 更正
        Hit(137, 125, 52, 14, _ => { _input = Gold() > 0 ? Gold().ToString() : ""; Refresh(); });         // 全部
        Hit(137, 139, 52, 14, _ => { _input = ""; Refresh(); });                                        // 清除
        Hit(24, 166, 78, 18, _ => { var v = Math.Min(Input, Gold()); Close(); if (v > 0) Confirm?.Invoke(v); });   // 確定
        Hit(107, 166, 78, 18, _ => Close());                                                            // 取消
    }

    private void Close() { Visible = false; Closed?.Invoke(); }

    public void TestInput(long v) { _input = v > 0 ? v.ToString() : ""; Refresh(); }
    public void TestConfirm() { var v = Math.Min(Input, Gold()); Close(); if (v > 0) Confirm?.Invoke(v); }
}
