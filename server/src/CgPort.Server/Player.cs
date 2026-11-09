using System.Text.Json;
using CgPort.Core;

namespace CgPort.Server;

/// <summary>
/// 经验表：
///  - explist.txt：每行一个数，第 N 行是升到 N 级所需的**累计**经验（已验证严格递增，共 801 级）；
///  - enemyexplist.txt：第 N 行是 N 级敌人给的经验（90 档，递增）。超过表长的等级取最后一档。
/// </summary>
public sealed class ExpTable
{
    public long[] LevelTotal { get; init; } = Array.Empty<long>();
    public long[] EnemyExp { get; init; } = Array.Empty<long>();

    public static ExpTable Load(string dataDir)
    {
        static long[] Nums(string p) => GameFiles.Exists(p)
            ? TabTable.ReadLines(p).Select(l => long.TryParse(l.Trim(), out var v) ? v : -1).Where(v => v >= 0).ToArray()
            : Array.Empty<long>();
        return new ExpTable { LevelTotal = Nums(Path.Combine(dataDir, "explist.txt")), EnemyExp = Nums(Path.Combine(dataDir, "enemyexplist.txt")) };
    }

    public long ForEnemy(int level) => EnemyExp.Length == 0 ? level * 2L : EnemyExp[Math.Clamp(level, 0, EnemyExp.Length - 1)];
    public long ToReach(int level) => LevelTotal.Length == 0 ? 15L * level * level : LevelTotal[Math.Clamp(level, 0, LevelTotal.Length - 1)];
    public int MaxLevel => LevelTotal.Length == 0 ? 200 : LevelTotal.Length - 1;
}

public sealed class ItemStack
{
    public int ItemId { get; set; }
    public int Count { get; set; }
    /// <summary>联网：服务端给的名字、说明（带 $n 颜色码，\n 换行）、图像编号。</summary>
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int Graphic { get; set; }
    /// <summary>联网：rU 第 6 段（图像后第 2 段）。官方战斗"物品"窗口里第 1 位为 0 的道具盖红叉不能用（实测：药、封印卡 = 1，装备 2/6，材料、金币、魔石、卡片 = 0）。</summary>
    public int Use { get; set; }
    /// <summary>联网：rU 第 8 段（图像后第 4 段）= 道具等级（水龙装备 7、铜剑 1；没鉴定的也有）。</summary>
    public int Level { get; set; }
    /// <summary>联网模式：服务端的格子号（8 起是背包，0–7 是装备栏）。单机模式不用，为 -1。</summary>
    public int Slot { get; set; } = -1;
}

/// <summary>玩家存档数据（单机模式保存到本地 JSON）。</summary>
public sealed class PlayerState
{
    public string Name { get; set; } = "player";
    public int Level { get; set; } = 1;
    public long Exp { get; set; }
    public long Gold { get; set; } = 1000;
    public int Hp { get; set; } = -1;            // -1 = 满血
    public int MaxHp { get; set; }               // 联网模式由服务端给；单机为 0（按等级算）
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public int MapId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Dir { get; set; }
    public List<ItemStack> Items { get; set; } = new();
    /// <summary>事件标记（NPC 脚本的 EndEvent / GlobalFlag）。</summary>
    public Dictionary<int, int> Flags { get; set; } = new();
    /// <summary>装备栏（联网：rU 的 0–7 格）：0 头 1 身体 2 右手(武器) 3 左手 4 足 5/6 饰品 7 水晶。</summary>
    public List<ItemStack> Equipment { get; set; } = new();
    /// <summary>联网：人物属性（CP/aPc 实测各项，见 PROTOCOL.md）。</summary>
    public CharStats Stats { get; set; } = new();
    /// <summary>可分配的升级点数（服务端 kg 包，62 进制）。</summary>
    public int StatPoints { get; set; }
    public string Job { get; set; } = "";
    /// <summary>技能（联网：服务端 CS/cNE 包；按技能栏位置）。</summary>
    public List<SkillInfo> Skills { get; set; } = new();
    /// <summary>技能栏总数（联网：CP2/UU 第 17 项，实测 10）。官方技能窗口显示"剩余技能栏 = 总数 − 已用"。</summary>
    public int SkillSlots { get; set; } = 10;
    /// <summary>宠物（联网：服务端 KP/IsSy、KP2/AU、PT/OBee 包；按宠物栏 0–4）。</summary>
    public List<PetInfo> Pets { get; set; } = new();

    public const int BagSize = 20;      // 背包格数（原版是 20 格，堆叠规则待确认）
    public const int StackLimit = 99;

    public int CountOf(int itemId) => Items.Where(i => i.ItemId == itemId).Sum(i => i.Count);
    public int FreeSlots => BagSize - Items.Count;

    /// <summary>加道具：同种先叠加到已有格子（每格最多 StackLimit），再占新格子。返回实际加入的数量。</summary>
    public int AddItem(int itemId, int count = 1)
    {
        int left = count;
        foreach (var s in Items.Where(i => i.ItemId == itemId))
        {
            int add = Math.Min(left, StackLimit - s.Count);
            s.Count += add; left -= add;
            if (left == 0) return count;
        }
        while (left > 0 && Items.Count < BagSize)
        {
            int add = Math.Min(left, StackLimit);
            Items.Add(new ItemStack { ItemId = itemId, Count = add }); left -= add;
        }
        return count - left;
    }

    /// <summary>移除道具，返回实际移除的数量。</summary>
    public int RemoveItem(int itemId, int count = 1)
    {
        int left = count;
        for (int i = Items.Count - 1; i >= 0 && left > 0; i--)
        {
            if (Items[i].ItemId != itemId) continue;
            int take = Math.Min(left, Items[i].Count);
            Items[i].Count -= take; left -= take;
            if (Items[i].Count == 0) Items.RemoveAt(i);
        }
        return count - left;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
    public static PlayerState? Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(path)) : null;
}

/// <summary>一场战斗的奖励。</summary>
public sealed record Rewards(long Exp, int LevelsGained, int NewLevel, IReadOnlyList<(int ItemId, int Count)> Items, IReadOnlyList<int> ItemsLostBagFull);

/// <summary>成长与掉落规则（数值规则是推测，等日志/实测校准）。</summary>
public sealed class Progression
{
    private readonly ExpTable _exp;
    private readonly BattleTables _battle;
    public Progression(ExpTable exp, BattleTables battle) { _exp = exp; _battle = battle; }

    /// <summary>
    /// 战斗胜利后结算：经验 = 每个被击倒敌人按其等级查 enemyexplist；
    /// 掉落 = 每个被击倒敌人的每个掉落项按掉落率（视为百分比）独立判定。
    /// </summary>
    public Rewards Grant(PlayerState p, IEnumerable<Combatant> defeatedEnemies, Random rng)
    {
        long exp = 0; var items = new List<(int, int)>(); var lost = new List<int>();
        foreach (var e in defeatedEnemies)
        {
            exp += _exp.ForEnemy(e.Level);
            if (!_battle.Enemies.TryGetValue(e.EnemyId, out var en)) continue;
            foreach (var (itemId, prob) in en.Drops)
                if (rng.Next(100) < Math.Clamp(prob, 0, 100))
                {
                    if (p.AddItem(itemId) == 1) items.Add((itemId, 1)); else lost.Add(itemId);
                }
        }
        int before = p.Level;
        p.Exp += exp;
        while (p.Level < _exp.MaxLevel && p.Exp >= _exp.ToReach(p.Level + 1)) p.Level++;
        return new Rewards(exp, p.Level - before, p.Level, items, lost);
    }
}


/// <summary>一招（技能的某一级，或宠物的一个技能）。</summary>
public sealed class TechInfo
{
    public int Index { get; set; }          // 在技能里的序号（下指令时发这个，从 0 数）
    public int TechId { get; set; }
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int Mp { get; set; }
}

/// <summary>人物技能：cNE 每个 11 段（栏位|技能编号|名字|等级|经验|?|升级经验|占用栏数|?|?|顺序），招式来自 Rspn。</summary>
public sealed class SkillInfo
{
    public int Slot { get; set; }
    public int SkillId { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Exp { get; set; }
    public int NextExp { get; set; }
    public int SlotsUsed { get; set; } = 1;
    /// <summary>技能种类（cNE 每个技能第 9 段，官方实测）：0 战斗、1 制造（铸剑/制药/料理…）、2 对道具（鉴定/修理/刻印）、3 对人（急救/治疗）、4 采集（伐木/狩猎/挖掘）。</summary>
    public int Type { get; set; }
    /// <summary>当前职业能练到的最高等级（cNE 第 6 段，实测：游民 连击 6、制药 0；药剂师 制药 6）。0 = 这个职业用不了（没有招式）。</summary>
    public int MaxLevel { get; set; } = 10;
    public List<TechInfo> Techs { get; set; } = new();
    /// <summary>制造类技能的配方（xmgQ）。</summary>
    public List<RecipeInfo> Recipes { get; set; } = new();
}

/// <summary>配方（xmgQ 每条 22 段：编号|等级|图像|名字|说明|MP|?(1/0，意思没弄清)|5×(材料编号|名字|数量)，空的编号 -1）。</summary>
public sealed class RecipeInfo
{
    public int Id { get; set; }
    public int Level { get; set; }
    public int Image { get; set; }
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int Mp { get; set; }
    public bool Ok { get; set; }
    public List<(int ItemId, string Name, int Count)> Materials { get; set; } = new();
}

/// <summary>宠物：IsSy 的各项（实测）+ OBee 的技能。State：0 休息、1 待命、2 战斗、3 散步（KS/LKQy 实测）。</summary>
public sealed class PetInfo
{
    public int Slot { get; set; }
    public int Anime { get; set; }
    public int Level { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public long Exp { get; set; }
    public long NextExp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Agility { get; set; }
    public int Spirit { get; set; }
    public int Recovery { get; set; }
    public int Loyalty { get; set; }
    public string Name { get; set; } = "";
    public string Nick { get; set; } = "";
    public int State { get; set; }
    public List<TechInfo> Techs { get; set; } = new();
    public string DisplayName => Nick.Length > 0 ? Nick : Name;
    /// <summary>种族（IsSy 第 2 项，和封印卡种族同编号）：0 人形 1 龙 2 不死 3 飞行 4 昆虫 5 植物 6 野兽 7 特殊 8 金属。</summary>
    public int Race { get; set; } = -1;
    public static readonly string[] RaceNames = { "人形系", "龙系", "不死系", "飞行系", "昆虫系", "植物系", "野兽系", "特殊系", "金属系", "邪魔系" };
    public string RaceName => Race >= 0 && Race < RaceNames.Length ? RaceNames[Race] : "";
    /// <summary>地水火风（0–100，IsSy 16–19）。</summary>
    public int[] Element { get; set; } = new int[4];
    public int SkillSlots { get; set; }
    /// <summary>可分配点数（sA 宠物栏 点数）。</summary>
    public int Points { get; set; }
    // AU：1 体力 2 力量 3 强度 4 速度 5 魔法 6 必杀 7 反击 8 命中 9 闪躲 10–15 抗毒/昏睡/石化/醉/混乱/遗忘 18 负重上限
    public int Vit { get; set; }
    public int Str { get; set; }
    public int Tgh { get; set; }
    public int Spd { get; set; }
    public int Mag { get; set; }
    public int Critical { get; set; }
    public int Counter { get; set; }
    public int Hit { get; set; }
    public int Dodge { get; set; }
    public int[] Resist { get; set; } = new int[6];
    public int LoadMax { get; set; }
    /// <summary>宠物装备（iKe）：0 项圈 1 防具 2 饰品 3 饰品 4 水晶（格子含义待核对）。</summary>
    public List<ItemStack> Equipment { get; set; } = new();

    public const int Rest = 0, Standby = 1, Battle = 2, Walk = 3;
}


/// <summary>
/// 人物属性（aPc 各项，官方状态窗口对照实测）：5 体力 6 力量 7 强度 8 速度 9 魔法 10 耐力 11 灵巧 12 智力 13 经验 14 下一级
/// 16 攻击 17 防御 18 敏捷 19 精神 20 回复 21 魅力 23–26 地水火风；UU：1 必杀 2 反击 3 命中 4 闪躲 5–10 抗毒/抗昏睡/抗石化/抗醉/抗混乱/抗遗忘 11 战绩。
/// </summary>
public sealed class CharStats
{
    public int Vit, Str, Tgh, Spd, Mag, Endurance, Dex, Int;
    public long NextExp;
    public int Attack, Defense, Agility, Spirit, Recovery, Charm;
    public int[] Element = new int[4];
    public int Critical, Counter, Hit, Dodge;
    public int[] Resist = new int[6];
    public int Record;
}
