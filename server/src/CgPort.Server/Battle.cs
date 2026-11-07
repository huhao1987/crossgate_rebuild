using CgPort.Core;

namespace CgPort.Server;

/// <summary>战斗单位的能力值。真实数值来源（敌人基础表各列 / 角色存档）待核对，目前由 <see cref="IStatProvider"/> 提供。</summary>
public sealed class Stats
{
    public int MaxHp { get; set; }
    public int MaxMp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Agility { get; set; }
    public int Spirit { get; set; }
}

public sealed class Combatant
{
    public int Id { get; init; }
    public int Side { get; init; }               // 0 = 玩家方，1 = 敌方
    public int Level { get; init; }
    public Stats Stats { get; init; } = new();
    public int Hp { get; set; }
    public int Mp { get; set; }
    public bool Guarding { get; set; }
    public bool Escaped { get; set; }
    public int EnemyId { get; init; }            // 敌方单位对应的 enemy.txt 编号
    public int AnimeId { get; init; }
    public IReadOnlyDictionary<string, int[]> Ai { get; init; } = new Dictionary<string, int[]>();
    public bool Alive => Hp > 0 && !Escaped;
}

public enum CommandKind { Attack, Guard, Escape, Wait, Item, Skill, PetSkill, Summon, Weapon, Position }

/// <summary>一回合里一个单位的指令。Arg：道具 = 背包格子；技能 = 技能栏位置；宠物技能 = 宠物技能序号。Level：技能的第几招（从 0 数）。</summary>
public sealed record Command(CommandKind Kind, int TargetId = -1, int Arg = -1, int Level = 0);

/// <summary>TechId：招式编号（联网 SKL 的 s 段，-1 = 不知道）；Magic：魔法类（施法者原地不动）；Heal：回复（Damage 是回复量）。</summary>
/// <summary>Sealed：封印（抓宠）—— null 不是封印，true 抓到了，false 没抓到。</summary>
public sealed record BattleEvent(int Turn, int ActorId, CommandKind Kind, int TargetId, int Damage, bool Missed, bool Killed, bool Escaped, int TechId = -1, bool Magic = false, bool Heal = false, bool? Sealed = null);

public enum BattleOutcome { Ongoing, Win, Lose, Escaped }

/// <summary>伤害、命中、逃跑、出手顺序的公式。默认实现只是占位，等拿到真实日志再按实测校准。</summary>
public interface IBattleFormulas
{
    int Initiative(Combatant c, Random rng);
    bool Hits(Combatant attacker, Combatant defender, Random rng);
    int Damage(Combatant attacker, Combatant defender, Random rng);
    bool EscapeSucceeds(Combatant c, IReadOnlyList<Combatant> enemies, Random rng);
}

/// <summary>占位公式：数值形状合理即可（防御减伤、防御姿态减半、±10% 浮动），不代表原版。</summary>
public sealed class PlaceholderFormulas : IBattleFormulas
{
    public int Initiative(Combatant c, Random rng) => c.Stats.Agility + rng.Next(0, Math.Max(1, c.Stats.Agility / 4 + 1));
    public bool Hits(Combatant a, Combatant d, Random rng) => rng.NextDouble() < Math.Clamp(0.9 + (a.Stats.Agility - d.Stats.Agility) / 400.0, 0.5, 0.99);
    public int Damage(Combatant a, Combatant d, Random rng)
    {
        double baseDmg = Math.Max(1.0, a.Stats.Attack - d.Stats.Defense / 2.0);
        if (d.Guarding) baseDmg /= 2;
        return Math.Max(1, (int)Math.Round(baseDmg * (0.9 + rng.NextDouble() * 0.2)));
    }
    public bool EscapeSucceeds(Combatant c, IReadOnlyList<Combatant> enemies, Random rng) => rng.NextDouble() < 0.6;
}

/// <summary>能力值来源。默认按等级生成占位数值；以后换成读敌人基础表 / 角色数据的实现。</summary>
public interface IStatProvider
{
    Stats ForEnemy(Enemy enemy, EnemyBase? b, int level);
}

public sealed class LevelScaledStats : IStatProvider
{
    public Stats ForEnemy(Enemy enemy, EnemyBase? b, int level) => new()
    {
        MaxHp = 20 + level * 12, MaxMp = 10 + level * 4,
        Attack = 10 + level * 3, Defense = 6 + level * 2, Agility = 8 + level * 2, Spirit = 5 + level * 2,
    };
}

/// <summary>
/// 回合制战斗骨架：收集指令 → 按出手顺序结算 → 判定胜负。
/// 敌人的指令按 enemy.txt 的行为权重（at/gu/es/wa，推测为攻击/防御/逃跑/待机）随机选择。
/// </summary>
public sealed class Battle
{
    public List<Combatant> Units { get; } = new();
    public int Turn { get; private set; }
    public BattleOutcome Outcome { get; private set; } = BattleOutcome.Ongoing;
    public List<BattleEvent> Log { get; } = new();
    private readonly IBattleFormulas _f;
    private readonly Random _rng;

    public Battle(IBattleFormulas formulas, Random rng) { _f = formulas; _rng = rng; }

    public IEnumerable<Combatant> Side(int s) => Units.Where(u => u.Side == s);
    public Combatant? Get(int id) => Units.FirstOrDefault(u => u.Id == id);

    /// <summary>根据遇敌结果加入敌方单位。</summary>
    public void AddEnemies(EncounterResult enc, BattleTables t, IStatProvider stats)
    {
        int nextId = Units.Count == 0 ? 0 : Units.Max(u => u.Id) + 1;
        foreach (var e in enc.Enemies)
        {
            var en = t.Enemies[e.EnemyId];
            t.Bases.TryGetValue(en.BaseId, out var b);
            var st = stats.ForEnemy(en, b, e.Level);
            Units.Add(new Combatant { Id = nextId++, Side = 1, Level = e.Level, Stats = st, Hp = st.MaxHp, Mp = st.MaxMp, EnemyId = e.EnemyId, AnimeId = e.AnimeId, Ai = en.Ai });
        }
    }

    /// <summary>敌人按行为权重选指令；目标随机选一个活着的玩家方单位。</summary>
    public Command ChooseEnemyCommand(Combatant c)
    {
        int W(string k) => c.Ai.TryGetValue(k, out var v) && v.Length > 0 ? Math.Max(0, v[0]) : 0;
        var options = new[] { (CommandKind.Attack, W("at")), (CommandKind.Guard, W("gu")), (CommandKind.Escape, W("es")), (CommandKind.Wait, W("wa")) };
        int total = options.Sum(o => o.Item2);
        var kind = CommandKind.Attack;
        if (total > 0)
        {
            int r = _rng.Next(total);
            foreach (var (k, w) in options) { if (r < w) { kind = k; break; } r -= w; }
        }
        var targets = Side(0).Where(u => u.Alive).ToList();
        return new Command(kind, targets.Count > 0 ? targets[_rng.Next(targets.Count)].Id : -1);
    }

    /// <summary>结算一回合。playerCommands：玩家方单位编号 → 指令（缺省为攻击随机敌人）。</summary>
    public BattleOutcome RunTurn(IReadOnlyDictionary<int, Command> playerCommands)
    {
        if (Outcome != BattleOutcome.Ongoing) return Outcome;
        Turn++;
        var cmds = new Dictionary<int, Command>();
        foreach (var u in Units.Where(u => u.Alive))
        {
            u.Guarding = false;
            if (u.Side == 1) cmds[u.Id] = ChooseEnemyCommand(u);
            else cmds[u.Id] = playerCommands.TryGetValue(u.Id, out var pc) ? pc : new Command(CommandKind.Attack, -1);
        }
        foreach (var (id, c) in cmds) if (c.Kind == CommandKind.Guard) Get(id)!.Guarding = true;   // 防御在回合开始时生效

        var order = cmds.Keys.Select(id => Get(id)!).OrderByDescending(u => _f.Initiative(u, _rng)).ToList();
        foreach (var actor in order)
        {
            if (!actor.Alive) continue;
            var c = cmds[actor.Id];
            switch (c.Kind)
            {
                case CommandKind.Attack:
                    var target = Get(c.TargetId);
                    if (target == null || !target.Alive || target.Side == actor.Side)
                    {
                        var pool = Units.Where(u => u.Alive && u.Side != actor.Side).ToList();
                        if (pool.Count == 0) break;
                        target = pool[_rng.Next(pool.Count)];
                    }
                    if (!_f.Hits(actor, target, _rng)) { Log.Add(new(Turn, actor.Id, c.Kind, target.Id, 0, true, false, false)); break; }
                    int dmg = _f.Damage(actor, target, _rng);
                    target.Hp = Math.Max(0, target.Hp - dmg);
                    Log.Add(new(Turn, actor.Id, c.Kind, target.Id, dmg, false, target.Hp == 0, false));
                    break;
                case CommandKind.Escape:
                    bool ok = _f.EscapeSucceeds(actor, Units.Where(u => u.Alive && u.Side != actor.Side).ToList(), _rng);
                    if (ok) actor.Escaped = true;
                    Log.Add(new(Turn, actor.Id, c.Kind, -1, 0, !ok, false, ok));
                    break;
                default:
                    Log.Add(new(Turn, actor.Id, c.Kind, -1, 0, false, false, false));
                    break;
            }
            if (Decide() != BattleOutcome.Ongoing) break;
        }
        return Outcome = Decide();
    }

    private BattleOutcome Decide()
    {
        bool playersLeft = Side(0).Any(u => u.Alive), enemiesLeft = Side(1).Any(u => u.Alive);
        if (!enemiesLeft) return BattleOutcome.Win;   // 敌人全灭或全部逃走都算战斗结束（胜利）
        if (!playersLeft) return Side(0).Any(u => u.Escaped) ? BattleOutcome.Escaped : BattleOutcome.Lose;
        return BattleOutcome.Ongoing;
    }
}
