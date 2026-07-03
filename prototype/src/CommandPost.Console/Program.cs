using CommandPost.Core;
using CommandPost.Cli;

try { System.Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 重定向时忽略 */ }

// 难度 / 种子
var diff = DifficultySettings.Normal;
if (args.Contains("--easy")) diff = DifficultySettings.Easy;
if (args.Contains("--hard")) diff = DifficultySettings.Hard;
int seed = 12345;
var seedArg = Array.IndexOf(args, "--seed");
if (seedArg >= 0 && seedArg + 1 < args.Length) int.TryParse(args[seedArg + 1], out seed);

var sim = Scenario.MinimalEncounter(diff, seed);

if (args.Contains("--demo")) { RunDemo(sim); return; }

PrintHelp();
AsciiRenderer.Render(sim);

while (true)
{
    if (sim.Status != GameStatus.Ongoing)
        Console.WriteLine($"局终:{sim.Status}。输入 peek 看复盘真相,或 q 退出。");
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;                       // EOF / 非交互
    line = line.Trim();
    if (line.Length == 0) { sim.AdvanceTick(); AsciiRenderer.Render(sim); continue; }

    var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var cmd = p[0].ToLowerInvariant();
    try
    {
        switch (cmd)
        {
            case "q" or "quit": return;
            case "?" or "help": PrintHelp(); break;
            case "n":
                int k = p.Length > 1 ? int.Parse(p[1]) : 1;
                for (int i = 0; i < k && sim.Status == GameStatus.Ongoing; i++) sim.AdvanceTick();
                AsciiRenderer.Render(sim);
                break;
            case "m": sim.IssueIntent(int.Parse(p[1]), Intent.Move(new Vec2(int.Parse(p[2]), int.Parse(p[3])), Tn(p, 4))); break;
            case "a": sim.IssueIntent(int.Parse(p[1]), Intent.Attack(int.Parse(p[2]), Tn(p, 3))); break;
            case "h": sim.IssueIntent(int.Parse(p[1]), Intent.Hold()); break;
            case "r": sim.IssueIntent(int.Parse(p[1]), Intent.Retreat(new Vec2(int.Parse(p[2]), int.Parse(p[3])))); break;
            case "s": sim.RequestStatus(int.Parse(p[1])); break;
            case "c": sim.DispatchScoutFromHq(new Vec2(int.Parse(p[1]), int.Parse(p[2]))); break;
            case "cs": sim.OrderUnitScout(int.Parse(p[1]), new Vec2(int.Parse(p[2]), int.Parse(p[3]))); break;
            case "log": foreach (var l in sim.Log) Console.WriteLine(l); break;
            case "peek": Peek(sim); break;
            case "replay": PlayReplay(sim, p.Length > 1 ? int.Parse(p[1]) : 120); break;
            default: Console.WriteLine("未知命令,输入 ? 看帮助。"); break;
        }
    }
    catch (Exception ex) { Console.WriteLine($"输入有误({ex.Message})。输入 ? 看帮助。"); }
}

static Tone Tn(string[] p, int i)
{
    if (p.Length <= i) return Tone.Normal;
    return p[i].ToLowerInvariant() switch
    {
        "c" => Tone.Cautious, "a" => Tone.Aggressive, "o" => Tone.AllOut, "j" => Tone.UseJudgment, _ => Tone.Normal
    };
}

void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("命令(原型用步进近似『实时可暂停』:命令只是排队,时间靠 n 推进):");
    Console.WriteLine("  n [k]       推进 k 个 tick(默认 1;空回车=推进 1)");
    Console.WriteLine("  m u x y [t] 令部队 u 移动到 (x,y)   t=基调 c/a/o/j");
    Console.WriteLine("  a u e [t]   令部队 u 攻击敌军 #e(按最后已知)");
    Console.WriteLine("  h u         令部队 u 据守");
    Console.WriteLine("  r u x y     令部队 u 后撤到 (x,y)");
    Console.WriteLine("  s u         派传令兵探问部队 u 近况(往返延迟,可能纯沉默)");
    Console.WriteLine("  c x y       中军派斥候去 (x,y) 侦察(视野大、避敌、返回中军)");
    Console.WriteLine("  cs u x y    令部队 u 派斥候去 (x,y)(传令兵先把命令送到)");
    Console.WriteLine("  (部队在等待时会按将领性格自动派斥候,拓展自己的视野)");
    Console.WriteLine("  log 完整军情 | peek 真相快照 | replay [ms] 上帝视角动画复盘 | q 退出");
    Console.WriteLine("  ★命令要花时间送到前线,副将按送达那刻的真实情况解读执行。");
    Console.WriteLine();
}

void Peek(Simulation s)
{
    Console.WriteLine("===== 复盘(真相世界,战中你看不到)=====");
    foreach (var u in s.Truth.Units.OrderBy(u => u.Id))
        Console.WriteLine($"  #{u.Id} {(u.Side == Side.Friend ? "我" : "敌")}{u.Name}({u.TypeCn}) @ {u.Pos} 兵{(int)u.Strength} 士气{(int)u.Morale} 体力{(int)u.Stamina}{(u.Routed ? " 溃逃" : "")}{(u.Strength <= 0 ? " 阵亡" : "")} 令:{u.Order?.ToString() ?? "无"}");
    Console.WriteLine("  --- 真相事件(含副将偏离)---");
    foreach (var l in s.TruthLog.TakeLast(20)) Console.WriteLine("   " + l);
}

void PlayReplay(Simulation s, int ms)
{
    Console.WriteLine($"【复盘】上帝视角逐帧回放,共 {s.Replay.Count} 帧(传令兵 * / 斥候 o 在此显形)……");
    bool redirected = Console.IsOutputRedirected;
    foreach (var f in s.Replay)
    {
        AsciiRenderer.RenderReplayFrame(f, s.Truth.Terrain, s.Truth.FriendHq, s.Truth.EnemyHq);
        if (!redirected && ms > 0) System.Threading.Thread.Sleep(ms);
    }
    Console.WriteLine("(复盘结束)");
}

void RunDemo(Simulation s)
{
    Console.WriteLine("【DEMO】脚本化推进:三路压到敌阵前,观察敌军幽灵如何随延迟情报浮现、交战。");
    AsciiRenderer.Render(s);
    s.IssueIntent(2, Intent.Move(new Vec2(16, 6)));
    s.IssueIntent(1, Intent.Move(new Vec2(16, 3), Tone.Aggressive));
    s.IssueIntent(3, Intent.Move(new Vec2(16, 9), Tone.Cautious));
    for (int t = 0; t < 120 && s.Status == GameStatus.Ongoing; t++)
    {
        s.AdvanceTick();
        if (t % 30 == 29) AsciiRenderer.Render(s);
    }
    AsciiRenderer.Render(s);
    Peek(s);
}
