using System.Text.RegularExpressions;
using CgPort.Core.Text;

namespace CgPort.Server;

/// <summary>
/// NPC 脚本（服务端 data/npc/*.txt，GBK）。结构（由 9634 个脚本的关键字统计得出）：
///   block 条件 && 条件 …     一条规则；从上往下执行第一条条件成立的规则
///   window "文字",按钮…       对话框，按钮：next / yes / no / ok（可多个）
///   endwindow "文字"         最后一句，说完结束
///   window_yes [条件]        玩家按"是"后的分支（window_no / window_ok 同理），后面跟动作
///   动作：giveitem / killitem / addgold / warp / endevent / globalflag / encount / leavegroup …
/// 关键字大小写不统一，统一按小写处理。暂不支持的条件视为"不成立"，暂不支持的动作跳过（并记录）。
/// </summary>
public sealed class NpcScript
{
    public sealed record Condition(string Kind, int Arg, string Op, long Value);
    public abstract record Step;
    public sealed record WindowStep(string Text, string[] Buttons) : Step;
    public sealed record EndWindowStep(string Text) : Step;
    public sealed record BranchStep(string Button, IReadOnlyList<Condition> Conditions) : Step;
    public sealed record ActionStep(string Name, string Args) : Step;
    public sealed record IfStep(IReadOnlyList<Condition> Conditions) : Step;
    public sealed record ElseStep : Step;
    public sealed record EndIfStep : Step;
    public sealed record Block(IReadOnlyList<Condition> Conditions, IReadOnlyList<Step> Steps);

    public IReadOnlyList<Block> Blocks { get; }
    private NpcScript(List<Block> b) => Blocks = b;

    private static readonly Regex Keyword = new(@"^([A-Za-z_][A-Za-z_0-9]*)\s*(.*)$");
    private static readonly Regex Quoted = new("\"([^\"]*)\"");

    public static NpcScript Parse(string text)
    {
        var blocks = new List<Block>();
        List<Condition>? conds = null; List<Step>? steps = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            int hash = line.IndexOf('#');
            if (hash == 0 || line.StartsWith("//")) continue;
            var m = Keyword.Match(line);
            if (!m.Success) continue;
            var kw = m.Groups[1].Value.ToLowerInvariant();
            var rest = m.Groups[2].Value;
            if (kw == "block")
            {
                if (conds != null) blocks.Add(new Block(conds, steps!));
                conds = ParseConditions(rest); steps = new List<Step>();
                continue;
            }
            if (steps == null) continue;
            switch (kw)
            {
                case "window" or "gawindow":
                {
                    var q = Quoted.Match(rest);
                    var after = q.Success ? rest[(q.Index + q.Length)..] : rest;
                    var buttons = after.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(b => b.ToLowerInvariant()).ToArray();
                    steps.Add(new WindowStep(q.Success ? q.Groups[1].Value : "", buttons));
                    break;
                }
                case "endwindow" or "gaendwindow":
                {
                    var q = Quoted.Match(rest);
                    steps.Add(new EndWindowStep(q.Success ? q.Groups[1].Value : ""));
                    break;
                }
                case "window_yes" or "window_no" or "window_ok" or "window_next" or "window_cancel":
                    steps.Add(new BranchStep(kw[7..], ParseConditions(rest)));
                    break;
                case "if":
                    steps.Add(new IfStep(ParseConditions(rest)));
                    break;
                case "else":
                    steps.Add(new ElseStep());
                    break;
                case "endif":
                    steps.Add(new EndIfStep());
                    break;
                default:
                    steps.Add(new ActionStep(kw, StripComment(rest)));
                    break;
            }
        }
        if (conds != null) blocks.Add(new Block(conds, steps!));
        return new NpcScript(blocks);
    }

    private static string StripComment(string s) { int i = s.IndexOf('#'); return (i >= 0 ? s[..i] : s).Trim(); }

    private static readonly Regex CondRe = new(@"^\s*([A-Za-z_]+?)(?:[_ ]?(\d+))?\s*(>=|<=|==|!=|>|<|=)\s*(-?\d+)\s*$");

    public static List<Condition> ParseConditions(string s)
    {
        var list = new List<Condition>();
        foreach (var part in StripComment(s).Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = CondRe.Match(part);
            if (m.Success)
                list.Add(new Condition(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0,
                    m.Groups[3].Value == "=" ? "==" : m.Groups[3].Value, long.Parse(m.Groups[4].Value)));
            else
                list.Add(new Condition("unsupported:" + part, 0, "==", 0));
        }
        return list;
    }

    public static NpcScript Load(string path) => Parse(Gbk.ReadFile(path));
}

/// <summary>
/// 对话框的一页。Options 不为空时是"选择列表"窗口（官方 WN 类型 2），点选项用 Answer("opt:序号")（序号从 0 开始）。
/// Kind："talk" 普通对话/列表；"shop" 商店入口（买/卖/离开）；"buy" 买东西列表；"sell" 卖东西列表。
/// 买：Answer("buy:序号:数量[,序号:数量…]")；卖：Answer("sell:格子:数量[,…]")。
/// </summary>
public sealed record DialogPage(string Text, IReadOnlyList<string> Buttons, bool IsLast, IReadOnlyList<string>? Options = null)
{
    public string Kind { get; init; } = "talk";
    public string NpcName { get; init; } = "";
    /// <summary>NPC 头像（全局图像编号），0 = 没有。</summary>
    public int Face { get; init; }
    public IReadOnlyList<ShopEntry>? Shop { get; init; }
}

/// <summary>商店列表里的一项。买：Price 是单价、Max 是一次最多买几个；卖：Slot 是背包格子、Count 是持有数量。</summary>
public sealed record ShopEntry(string Name, int Image, int Price, string Desc, int Count = 0, int Slot = -1, int Max = 1);

/// <summary>脚本运行时需要的玩家/世界接口。</summary>
public interface IScriptHost
{
    PlayerState Player { get; }
    int PlayerX { get; }
    int PlayerY { get; }
    void Warp(int map, int x, int y);
    void StartEncounter(int groupId);
    void Log(string message);
}

/// <summary>一次 NPC 对话的执行器：选规则 → 逐页显示 → 根据按钮走分支并执行动作。</summary>
public sealed class ScriptRun
{
    private readonly NpcScript.Block? _block;
    private readonly IScriptHost _host;
    private readonly Random _rng;
    private int _pc;
    public DialogPage? Current { get; private set; }
    public bool Finished => Current == null;
    public List<string> Unsupported { get; } = new();

    public ScriptRun(NpcScript script, IScriptHost host, Random rng)
    {
        _host = host; _rng = rng;
        _block = script.Blocks.FirstOrDefault(b => b.Conditions.All(Eval));
        _pc = 0;
        Advance(null);
    }

    public bool Eval(NpcScript.Condition c)
    {
        var p = _host.Player;
        long? v = c.Kind switch
        {
            "item" => p.CountOf(c.Arg),
            "itemspace" => p.FreeSlots,
            "gold" => p.Gold,
            "level" or "lv" => p.Level,
            "xpos" => _host.PlayerX,
            "ypos" => _host.PlayerY,
            "endevent" or "globalflag" => p.Flags.TryGetValue(c.Arg, out var f) ? f : 0,
            "group" => 1,                 // 单机：队伍人数 1
            "job" => 0,                   // 职业系统未实现：视为无职业
            "petspace" => 5,
            _ => null,
        };
        if (v == null) { Unsupported.Add(c.Kind); return false; }
        return c.Op switch
        {
            ">=" => v >= c.Value, "<=" => v <= c.Value, ">" => v > c.Value, "<" => v < c.Value,
            "!=" => v != c.Value, _ => v == c.Value,
        };
    }

    /// <summary>玩家按了某个按钮（next/yes/no/ok）。</summary>
    public void Press(string button) => Advance(button.ToLowerInvariant());

    private void Advance(string? pressed)
    {
        Current = null;
        if (_block == null) return;
        var steps = _block.Steps;
        if (pressed != null)
        {
            // next：继续下一步；yes/no/ok：跳到对应分支（条件不成立则找下一个同名分支）
            if (pressed != "next")
            {
                int found = -1;
                for (int i = _pc; i < steps.Count; i++)
                    if (steps[i] is NpcScript.BranchStep b && b.Button == pressed && b.Conditions.All(Eval)) { found = i; break; }
                if (found < 0) return;   // 没有对应分支：对话结束
                _pc = found + 1;
                if (RunActions()) return;
            }
        }
        while (_pc < steps.Count)
        {
            var s = steps[_pc++];
            switch (s)
            {
                case NpcScript.WindowStep w:
                    Current = new DialogPage(w.Text, w.Buttons.Length == 0 ? new[] { "ok" } : w.Buttons, false);
                    return;
                case NpcScript.EndWindowStep e:
                    Current = new DialogPage(e.Text, new[] { "ok" }, true);
                    _pc = steps.Count;
                    return;
                case NpcScript.BranchStep:
                    return;   // 走到别的分支开头：本分支结束
                case NpcScript.ActionStep or NpcScript.IfStep or NpcScript.ElseStep or NpcScript.EndIfStep:
                    _pc--;
                    if (RunActions()) return;
                    break;
            }
        }
    }

    /// <summary>执行从当前位置开始的连续动作（含 if/else/endif），遇到对话框/分支停下。返回是否已切换地图等需要结束的情况。</summary>
    private bool RunActions()
    {
        var steps = _block!.Steps;
        while (_pc < steps.Count)
        {
            switch (steps[_pc])
            {
                case NpcScript.ActionStep a:
                    _pc++;
                    if (Do(a.Name, a.Args)) { _pc = steps.Count; return true; }
                    break;
                case NpcScript.IfStep i:
                    _pc++;
                    if (!i.Conditions.All(Eval)) SkipToElseOrEndIf(steps, stopAtElse: true);
                    break;
                case NpcScript.ElseStep:
                    _pc++;
                    SkipToElseOrEndIf(steps, stopAtElse: false);
                    break;
                case NpcScript.EndIfStep:
                    _pc++;
                    break;
                default:
                    return false;
            }
        }
        return false;
    }

    /// <summary>从当前位置向后跳过嵌套块：stopAtElse=true 时停在同层 else 之后，否则停在同层 endif 之后。</summary>
    private void SkipToElseOrEndIf(IReadOnlyList<NpcScript.Step> steps, bool stopAtElse)
    {
        int depth = 0;
        while (_pc < steps.Count)
        {
            var st = steps[_pc++];
            if (st is NpcScript.IfStep) depth++;
            else if (st is NpcScript.EndIfStep) { if (depth == 0) return; depth--; }
            else if (st is NpcScript.ElseStep && depth == 0 && stopAtElse) return;
            else if (st is NpcScript.BranchStep or NpcScript.WindowStep or NpcScript.EndWindowStep && depth == 0)
            {
                // 在 if 块里的对话框：if 不成立时也一并跳过（只跳过同层内部的）
                continue;
            }
        }
    }

    private static int[] Ints(string s) => Regex.Matches(s, @"-?\d+").Select(m => int.Parse(m.Value)).ToArray();

    private bool Do(string name, string args)
    {
        var p = _host.Player; var n = Ints(args);
        switch (name)
        {
            case "giveitem" or "giveleak1item":
                if (args.Contains('%'))
                {
                    // (道具%权重,道具%权重,…)：按权重随机给一个
                    var opts = Regex.Matches(args, @"(\d+)%(\d+)").Select(m => (Id: int.Parse(m.Groups[1].Value), W: int.Parse(m.Groups[2].Value))).ToList();
                    int total = opts.Sum(o => o.W), r = _rng.Next(Math.Max(1, total));
                    foreach (var o in opts) { if (r < o.W) { p.AddItem(o.Id); break; } r -= o.W; }
                }
                else if (n.Length > 0) p.AddItem(n[0], n.Length > 1 ? Math.Max(1, n[1]) : 1);
                return false;
            case "killitem":
                if (n.Length > 0) p.RemoveItem(n[0], n.Length > 1 ? Math.Max(1, n[1]) : 1);
                return false;
            case "addgold":
                if (n.Length > 0) p.Gold = Math.Max(0, p.Gold + n[0]);
                return false;
            case "endevent" or "globalflag":
                if (n.Length >= 2) p.Flags[n[0]] = n[1];
                return false;
            case "warp":
                if (n.Length >= 4) { _host.Warp(n[1], n[2], n[3]); return true; }
                return false;
            case "encount":
                if (n.Length > 0) { _host.StartEncounter(n[0]); return true; }
                return false;
            case "leavegroup":
                return false;   // 单机没有组队
            case "killitemall":
                if (n.Length > 0) p.RemoveItem(n[0], p.CountOf(n[0]));
                return false;
            case "message" or "systemmessage":
                _host.Log("系统消息");   // 内容由客户端以后显示，这里只记录
                return false;
            case "endblock":
                _pc = int.MaxValue / 2; return false;
            case "play_se" or "setlp" or "setdur" or "setobject" or "addstampcount" or "localcount1" or "localcount2":
                return false;   // 音效/耐久/显示类动作：单机暂时忽略
            default:
                Unsupported.Add(name);
                _host.Log($"暂不支持的脚本动作：{name}");
                return false;
        }
    }
}
