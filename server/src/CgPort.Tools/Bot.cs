using System.Diagnostics;
using CgPort.Core;

/// <summary>
/// 陪练号：挂一个角色在线，配合你测试组队、交易、名片、邮件。
/// 用法：bot [主机:端口] [账号] [密码]
/// 你在聊天里对它说（站在它旁边说效果最好）：
///   过来   —— 它传送到你旁边并面向你（需要账号有 GM 权限；没有就你自己走到它旁边）
///   组队   —— 它加入你的队伍（你当队长，你走它跟）
///   离队   —— 它离开队伍
///   交易   —— 它向你发起交易
///   名片   —— 它跟你交换名片
///   帮助   —— 它把这些命令说一遍
/// 它自动做的事：
///   开着「隊」「名」「易」三个开关；有人跟它交易就自动「開啓」（什么都不给），对方确认后它也确认；
///   收到邮件就回一封"收到你的信：…"；有人加入它的队伍也行（那它就是队长，站着不动）。
/// </summary>
static class Bot
{
    public static int Run(string hostPort, string account, string password)
    {
        var hp = hostPort.Split(':');
        var s = new CgPort.Client.NetworkSession(new CgPort.Client.NetworkOptions
        {
            Host = hp[0], Port = hp.Length > 1 ? int.Parse(hp[1]) : 9030, CdKey = account, Account = account, Password = password,
            NewCharName = "bob",
        });
        void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");
        string? err = null;
        s.Error += e => { err = e; Log("错误：" + e); };
        s.Status += m => Log(m);

        var jobs = new List<(DateTime At, Action Do)>();
        void Later(double sec, Action a) => jobs.Add((DateTime.Now.AddSeconds(sec), a));
        string? tradeWith = null;
        bool flagsSet = false;

        // 走到 / 传送到某人旁边并面向他
        bool Near(string name, out (int dx, int dy) face)
        {
            face = (0, 0);
            var who = s.NpcsOnCurrentMap().FirstOrDefault(n => n.Name == name);
            if (who == null) return false;
            int dx = who.X - s.X, dy = who.Y - s.Y;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1) { face = (Math.Sign(dx), Math.Sign(dy)); return true; }
            return false;
        }
        void ComeTo(string name, Action? then = null, int attempt = 0)
        {
            if (Near(name, out var f)) { s.Face(f.dx, f.dy); if (then != null) Later(0.8, then); return; }
            var who = s.NpcsOnCurrentMap().FirstOrDefault(n => n.Name == name);
            if (who == null) { s.Say($"{name}，我看不到你，走近一点再叫我。"); return; }
            if (attempt >= 8) { s.Say("传送不过去（可能没有 GM 权限），你走到我旁边再说一次吧。"); return; }
            var (ox, oy) = Directions.Deltas[attempt];
            s.Say($"/warp {s.CurrentMap!.MapId} 0 {who.X + ox} {who.Y + oy}");
            Later(1.5, () => ComeTo(name, then, attempt + 1));
        }

        s.ChatReceived += (text, color) =>
        {
            if (color == 4) return;   // 系统提示
            int c = text.IndexOf(':');
            if (c <= 0) return;
            string who = text[..c].Trim(), msg = text[(c + 1)..].Trim();
            if (who == s.Player.Name) return;
            Log($"{who} 说：{msg}");
            switch (msg)
            {
                case "过来": case "来": ComeTo(who, () => s.Say("来了！")); break;
                case "组队":
                    if (s.InParty) { s.Say("我已经在队伍里了。"); break; }
                    ComeTo(who, () => s.PartyButton()); break;
                case "离队": if (s.InParty) s.PartyButton(); else s.Say("我不在队伍里。"); break;
                case "交易": tradeWith = who; ComeTo(who, () => s.RequestTrade()); break;
                case "名片": ComeTo(who, () => s.ExchangeCard()); break;
                case "帮助": case "help":
                    s.Say("可以对我说：过来、组队、离队、交易、名片。跟我交易我会自动同意，给我写信我会回。"); break;
            }
        };
        s.TradeCandidatesReceived += names =>
        {
            int i = tradeWith == null ? -1 : names.ToList().IndexOf(tradeWith);
            if (i < 0) { s.Say("面前没有能交易的人（对方要开着「易」）。"); return; }
            s.SelectTradeTarget(i); tradeWith = null;
        };
        s.TradeChanged += () =>
        {
            var t = s.Trade;
            if (t == null) return;
            if (!t.IOpened) { Log($"开始和 {t.Partner} 交易"); t.IOpened = true; Later(1.5, () => { if (s.Trade != null) s.OpenTrade(Array.Empty<int>(), 0); }); }
            if (t.TheyConfirmed && !t.IConfirmed) { t.IConfirmed = true; Later(1, () => { if (s.Trade != null) s.ConfirmTrade(); }); }
        };
        s.TradeEnded += done => Log(done ? "交易完成" : "交易中止");
        s.MailReceived += m =>
        {
            Log($"收到 {m.Name} 的信：{m.Text}");
            Later(1.5, () => s.SendMail(m.CardIndex, "收到你的信：" + m.Text));
        };
        s.PartyChanged += () => Log(s.InParty ? $"队伍：{string.Join("、", s.Party.Select(p => p.Name))}" : "不在队伍里");
        s.CardsChanged += () => Log("名片簿：" + string.Join("、", s.Cards.Where(c => c != null).Select(c => c!.Name)));

        s.Start();
        Log($"连接 {hostPort}，账号 {account} …");
        var warp = Environment.GetEnvironmentVariable("BOT_WARP");   // 例 "1000 242 101"；默认不传送，就在登录点
        var sw = Stopwatch.StartNew();
        while (err == null)
        {
            s.Poll();
            if (s.InGame && !flagsSet && sw.ElapsedMilliseconds > 1500)
            {
                flagsSet = true;
                s.SetFlags(1 | 16 | 32);
                Log($"进入游戏：{s.Player.Name} @ {s.CurrentMap?.Name} ({s.X},{s.Y})。已打开「隊」「名」「易」。对它说「帮助」看命令。");
                if (warp != null) { var w = warp.Split(' '); s.Say($"/warp {w[0]} 0 {w[1]} {w[2]}"); }
            }
            var now = DateTime.Now;
            foreach (var j in jobs.Where(j => j.At <= now).ToList()) { jobs.Remove(j); try { j.Do(); } catch (Exception e) { Log("出错：" + e.Message); } }
            Thread.Sleep(20);
        }
        return 1;
    }
}
