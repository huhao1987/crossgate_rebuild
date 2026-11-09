using CgPort.Client;
using CgPort.Server;
using Godot;

namespace CgPort.Godot;

/// <summary>
/// 自动战斗（这个私服客户端加的「自動」「選項」，官方 exe 0x405700 起；选项位 0x499604，默认 0x42）：
///   位 0 原定目标(位置)优先「优先攻击某位置的目标。」/ 位 1 智能选择「随机选取合适的目标。」（两个只能选一个）；
///   我方生命低于 50%(带宠战斗)：位 2 恢复类魔法补血「使用魔法恢复目标生命值。」、位 3 去道具补血「通过嗑药给目标恢复生命。」、位 4 宠物护卫「让宠物护卫该目标。」；
///   停止自动战斗：位 5 遇到 Lv1 魔族时、位 6 战斗为 BOSS 战时（EN 战斗种类 4–6）。
/// 每次要指令（面板出来）就替玩家 / 宠物下指令。"原定目标"= 最近一次手动点的敌人位置，倒了就随便挑一个。
/// </summary>
public sealed class AutoBattle
{
    public const int Default = 0x42;
    public int Options = Default;
    public bool On;
    public int LastTarget = -1;
    private readonly Random _rng = new();
    private int _decidedTurn = -1, _decidedSlot = -1;
    private bool _checkedStart;

    public void BattleStarted() { _decidedTurn = _decidedSlot = -1; _checkedStart = false; }

    /// <summary>开战时检查停止条件：返回 true = 要关掉自动战斗。</summary>
    public bool ShouldStop(NetworkSession s)
    {
        if (_checkedStart) return false;
        _checkedStart = true;
        if ((Options & 0x40) != 0 && s.BattleType is >= 4 and <= 6) return true;
        if ((Options & 0x20) != 0 && s.CurrentBattle.Units.Any(u => u.Side == 1 && u.Alive && u.Level == 1)) return true;
        return false;
    }

    /// <summary>这一次该下什么指令（每回合每个指令位只下一次）；null = 已经下过了。</summary>
    public Command? Decide(NetworkSession s, int turn, int slot, bool petTurn)
    {
        if (turn == _decidedTurn && slot == _decidedSlot) return null;
        _decidedTurn = turn; _decidedSlot = slot;
        var v = s.CurrentBattle;
        var enemies = v.Units.Where(u => u.Side == 1 && u.Alive).ToList();
        var hurt = v.Units.Where(u => u.Side == 0 && u.Alive && u.MaxHp > 0 && u.Hp * 2 < u.MaxHp).OrderBy(u => (double)u.Hp / u.MaxHp).FirstOrDefault();
        if (hurt != null)
        {
            if (!petTurn)
            {
                if ((Options & 4) != 0 && HealSkill(s.Player) is { } heal) return new Command(CommandKind.Skill, hurt.Id, heal.Slot, heal.Tech);
                if ((Options & 8) != 0 && s.Player.Items.FirstOrDefault(i => i.Slot >= 8 && (i.Use & 1) != 0 && i.Name.Contains("药")) is { } med) return new Command(CommandKind.Item, hurt.Id, med.Slot);
            }
            else if ((Options & 0x10) != 0 && s.Player.Pets.FirstOrDefault(p => p.State == PetInfo.Battle)?.Techs.FirstOrDefault(t => t.Name.Contains("护卫")) is { } guard)
                return new Command(CommandKind.PetSkill, hurt.Id, guard.Index);
        }
        if (enemies.Count == 0) return new Command(CommandKind.Guard);
        var target = (Options & 1) != 0 && enemies.FirstOrDefault(e => e.Id == LastTarget) is { } kept ? kept : enemies[_rng.Next(enemies.Count)];
        return petTurn ? new Command(CommandKind.PetSkill, target.Id, 0) : new Command(CommandKind.Attack, target.Id);
    }

    /// <summary>补血魔法：名字带"补血"/"恢复"的战斗技能，挑 MP 够用的最高一招。</summary>
    private static (int Slot, int Tech)? HealSkill(PlayerState p)
    {
        foreach (var sk in p.Skills.Where(k => k.Name.Contains("补血") || k.Name.Contains("恢复")))
        {
            var t = sk.Techs.Where(x => x.Mp <= p.Mp).OrderByDescending(x => x.Index).FirstOrDefault();
            if (t != null) return (sk.Slot, t.Index);
        }
        return null;
    }
}

/// <summary>
/// 自动战斗「選項」窗口（官方实测，贴右在 (705,113) 246×232）：三段标题（绿字）+ 7 个勾选项，行距 20。
/// 标题：攻击目标选项 (y 133) / 我方生命低于50%(带宠战斗) (193) / 停止自动战斗 (273)；勾选框 x 721、字 x 737（画面坐标）。
/// </summary>
public partial class AutoBattleOptions : CanvasLayer
{
    public AutoBattle Auto = null!;
    public Action? Changed;
    private Control _root = null!;
    private static readonly (int Bit, string Text, float Y)[] Items =
    {
        (0, "原定目标(位置)优先", 153), (1, "智能选择", 173),
        (2, "恢复类魔法补血", 213), (3, "去道具补血", 233), (4, "宠物护卫", 253),
        (5, "遇到Lv1魔族时", 293), (6, "战斗为BOSS战时", 313),
    };

    public override void _Ready() { Layer = 12; Visible = false; }
    public void Toggle() { Visible = !Visible; if (Visible) Refresh(); }

    public void Refresh()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        var full = new Control { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -960, MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(960, 720) };
        AddChild(full);
        _root = new Control { Position = new Vector2(705, 113), Size = new Vector2(246, 232), MouseFilter = Control.MouseFilterEnum.Stop };
        full.AddChild(_root);
        _root.AddChild(new Bg { Size = _root.Size });
        var green = new Color(0.3f, 0.95f, 0.3f);
        Head("攻击目标选项", 133, green); Head("我方生命低于50%(带宠战斗)", 193, green); Head("停止自动战斗", 273, green);
        foreach (var (bit, text, y) in Items)
        {
            bool on = (Auto.Options & (1 << bit)) != 0;
            _root.AddChild(new Check { On = on, Position = new Vector2(721 - 705, y - 113 + 4) });
            _root.AddChild(new CgText { Text = text, FontColor = Colors.White, Position = new Vector2(737 - 705, y - 113), Wrap = 30 });
            var hit = new Control { Position = new Vector2(10, y - 113), Size = new Vector2(226, 18), MouseFilter = Control.MouseFilterEnum.Stop };
            int b = bit;
            hit.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Flip(b); };
            _root.AddChild(hit);
        }
    }

    /// <summary>点一项：位 0 / 1 互斥（官方 0x405bc2），其余开关（0x405c67 异或）。</summary>
    public void Flip(int bit)
    {
        if (bit == 0) Auto.Options = (Auto.Options & ~2) | 1;
        else if (bit == 1) Auto.Options = (Auto.Options & ~1) | 2;
        else Auto.Options ^= 1 << bit;
        GameAudio.Se(59);
        Changed?.Invoke(); Refresh();
    }

    private void Head(string t, float y, Color c) => _root.AddChild(new CgText { Text = t, FontColor = c, Position = new Vector2(717 - 705, y - 113), Wrap = 40 });

    private partial class Bg : Control
    {
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.12f, 0.12f, 0.16f, 0.85f));
            DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color(0.85f, 0.6f, 0.3f), false, 2);
        }
    }
    private partial class Check : Control
    {
        public bool On;
        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, new Vector2(9, 9)), new Color(0.82f, 0.85f, 0.85f));
            if (!On) return;
            var red = new Color(0.95f, 0.1f, 0.1f);
            DrawLine(new Vector2(-1, 4), new Vector2(3, 8), red, 2); DrawLine(new Vector2(3, 8), new Vector2(11, -3), red, 2);
        }
    }
}
