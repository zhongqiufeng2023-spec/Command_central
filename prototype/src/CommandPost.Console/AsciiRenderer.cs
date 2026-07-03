using CommandPost.Core;

namespace CommandPost.Cli;

/// <summary>把『认知世界』渲染成 ASCII 沙盘 + 面板。绝不渲染真相世界(防穿帮)。</summary>
public static class AsciiRenderer
{
    public static void Render(Simulation sim)
    {
        var truth = sim.Truth;                 // 仅取静态基线:地形、tick、天气、HQ、我方单位名
        var grid = truth.Terrain;
        var belief = sim.Beliefs[Side.Friend];
        int now = truth.Tick;

        if (!Console.IsOutputRedirected) Console.Clear();
        Console.WriteLine($"=== 中军帐 · 遭遇战原型 ===  t{now}  天气:{WeatherCn(grid.Weather)}  难度:{sim.Difficulty.Name}  局势:{StatusCn(sim.Status)}");
        Console.WriteLine("沙盘 = 认知世界(你收到的滞后情报);真相你看不到。");
        Console.WriteLine();

        var ch = new char[grid.Width, grid.Height];
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                ch[x, y] = TerrainCh(grid.At(new Vec2(x, y)));
        Put(ch, truth.FriendHq, 'H');
        Put(ch, truth.EnemyHq, 'X');
        foreach (var g in belief.KnownOf(Side.Friend))
            Put(ch, g.LastKnownPos, (char)('0' + g.UnitId));
        foreach (var g in belief.KnownOf(Side.Enemy))
            Put(ch, g.LastKnownPos, g.AgeAt(now) > 8 ? 'e' : 'E');
        // 战中不显示斥候 / 传令兵的位置:你没有自家侦察兵的实时坐标,派出去只能等回报(复盘才显形)。

        // 列号两行:十位 + 个位(避免 0-9 重复看不清,x=16 = 上行'1'+下行'6')
        Console.Write("    ");
        for (int x = 0; x < grid.Width; x++) Console.Write(x < 10 ? ' ' : (char)('0' + x / 10));
        Console.WriteLine();
        Console.Write("    ");
        for (int x = 0; x < grid.Width; x++) Console.Write((char)('0' + x % 10));
        Console.WriteLine();
        for (int y = 0; y < grid.Height; y++)
        {
            Console.Write(y.ToString().PadLeft(2) + "  ");
            for (int x = 0; x < grid.Width; x++) Console.Write(ch[x, y]);
            Console.WriteLine();
        }
        Console.WriteLine("图例: .平原 ^林 ~河 H我帐 X敌帐 1-3我军(最后已知) E敌军 e敌军幽灵");
        Console.WriteLine($"在途(只知数量,不知位置):斥候 {truth.Scouts.Count(s => s.Side == Side.Friend)} · 传令兵 {truth.Messengers.Count(m => m.Side == Side.Friend)}");
        Console.WriteLine();

        Console.WriteLine("── 我军(认知,可能滞后)──");
        foreach (var g in belief.KnownOf(Side.Friend).OrderBy(g => g.UnitId))
            Console.WriteLine($"  #{g.UnitId} {NameOf(truth, g.UnitId)} @ {g.LastKnownPos} 兵{Str(g.KnownStrength)}  [{Age(g, now)}]");

        Console.WriteLine("── 敌军(认知,最后已知)──");
        var es = belief.KnownOf(Side.Enemy).OrderBy(g => g.UnitId).ToList();
        if (es.Count == 0) Console.WriteLine("  (尚无敌情——派斥候/部队去探)");
        foreach (var g in es)
            Console.WriteLine($"  #{g.UnitId} {TypeCn(g.KnownType)} @ {g.LastKnownPos} 兵{Str(g.KnownStrength)}  [{Age(g, now)}]"
                + (g.AgeAt(now) > 8 ? " 幽灵" : "") + (g.Fidelity < 1 && sim.Difficulty.ShowCredibilityHint ? " 存疑" : ""));
        Console.WriteLine();

        Console.WriteLine("── 军情(最近)──");
        foreach (var line in sim.Log.TakeLast(8)) Console.WriteLine("  " + line);
        Console.WriteLine();
    }

    /// <summary>复盘:渲染某一 tick 的上帝视角(真相)。传令兵 * / 斥候 o 在此显形。</summary>
    public static void RenderReplayFrame(ReplayFrame f, TerrainGrid grid, Vec2 fHq, Vec2 eHq)
    {
        if (!Console.IsOutputRedirected) Console.Clear();
        Console.WriteLine($"=== 复盘 · 上帝视角(战中你看不到)===  t{f.Tick}");
        Console.WriteLine();

        var ch = new char[grid.Width, grid.Height];
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                ch[x, y] = TerrainCh(grid.At(new Vec2(x, y)));
        Put(ch, fHq, 'H');
        Put(ch, eHq, 'X');
        foreach (var m in f.Messengers) Put(ch, m.Pos, m.Side == Side.Friend ? '*' : '+');   // 传令兵显形
        foreach (var s in f.Scouts) Put(ch, s.Pos, s.Side == Side.Friend ? 'o' : 'x');        // 斥候显形
        foreach (var u in f.Units) if (!u.Dead) Put(ch, u.Pos, (char)('0' + u.Id));           // 部队在最上层

        Console.Write("    ");
        for (int x = 0; x < grid.Width; x++) Console.Write(x < 10 ? ' ' : (char)('0' + x / 10));
        Console.WriteLine();
        Console.Write("    ");
        for (int x = 0; x < grid.Width; x++) Console.Write((char)('0' + x % 10));
        Console.WriteLine();
        for (int y = 0; y < grid.Height; y++)
        {
            Console.Write(y.ToString().PadLeft(2) + "  ");
            for (int x = 0; x < grid.Width; x++) Console.Write(ch[x, y]);
            Console.WriteLine();
        }
        Console.WriteLine("图例: 1-3我军 4-6敌军 *我传令 +敌传令 o我斥候 x敌斥候 H我帐 X敌帐");
        Console.WriteLine();
        foreach (var u in f.Units.OrderBy(u => u.Id))
        {
            string st = u.Dead ? "阵亡" : u.Routed ? "溃逃" : $"兵{u.Strength} 士气{u.Morale}";
            Console.WriteLine($"  #{u.Id} {(u.Side == Side.Friend ? "我" : "敌")}{u.Name}({TypeCn(u.Type)}) @ {u.Pos} {st}");
        }
    }

    private static void Put(char[,] g, Vec2 p, char c)
    {
        if (p.X >= 0 && p.X < g.GetLength(0) && p.Y >= 0 && p.Y < g.GetLength(1)) g[p.X, p.Y] = c;
    }

    private static char TerrainCh(TerrainType t) => t switch
    {
        TerrainType.Forest => '^',
        TerrainType.River => '~',
        _ => '.'
    };

    private static string NameOf(TruthWorld t, int id) => t.UnitById(id)?.Name ?? "?";
    private static string Str(double? s) => s is double v ? ((int)v).ToString() : "?";
    private static string TypeCn(UnitType? t) => t switch
    {
        UnitType.Spear => "枪", UnitType.Bow => "弓", UnitType.Cavalry => "骑", UnitType.Shield => "盾", _ => "?"
    };
    private static string Age(GhostUnit g, int now) => g.AgeAt(now) == 0 ? "刚刚" : $"{g.AgeAt(now)}t前";
    private static string WeatherCn(Weather w) => w switch { Weather.Fog => "雾", Weather.Night => "夜", _ => "晴" };
    private static string StatusCn(GameStatus s) => s switch
    {
        GameStatus.FriendWon => "★我军胜★", GameStatus.EnemyWon => "☠我军败☠", _ => "进行中"
    };
}
