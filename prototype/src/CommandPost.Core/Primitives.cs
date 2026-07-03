namespace CommandPost.Core;

/// <summary>整数网格坐标。</summary>
public readonly record struct Vec2(int X, int Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public int Manhattan(Vec2 o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y);
    public double DistanceTo(Vec2 o)
    {
        double dx = X - o.X, dy = Y - o.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
    public override string ToString() => $"({X},{Y})";
}

public enum Side { Friend, Enemy }

/// <summary>兵种(跨阵营)。大酆:枪槊/弓弩/轻骑/刀盾/陌刀/具装;草原:骑射/突骑/部落步众。</summary>
public enum UnitType { Spear, Bow, Cavalry, Shield, MoDao, Cataphract, HorseArcher, NomadLancer, TribalFoot }

/// <summary>阵营(与 Side 正交:一场里 Friend=大酆、Enemy=草原/冥府)。</summary>
public enum Faction { Dafeng, Steppe, Netherworld }

/// <summary>玩家下达的命令动词。</summary>
public enum Verb { Move, Attack, Hold, Retreat }

/// <summary>执行基调——给副将解读留空间(§九)。</summary>
public enum Tone { Cautious, Normal, Aggressive, AllOut, UseJudgment }

/// <summary>将领性格——驱动副将解读、敌方 AI 效用权重(§十、§十五)。</summary>
public enum Personality { Aggressive, Cautious, Cunning, Steady }

public enum TerrainType { Plain, Forest, River }

/// <summary>天气由章节/难度设定,局内不变(§十七)。</summary>
public enum Weather { Clear, Fog, Night }

/// <summary>规模档:瞭望台高处观察只能估个大概(烟尘大小),不识精确兵力。</summary>
public enum ScaleHint { Unknown, Small, Medium, Large }

public static class SideExtensions
{
    public static Side Opponent(this Side s) => s == Side.Friend ? Side.Enemy : Side.Friend;
}

public static class ScaleHintExtensions
{
    public static string Cn(this ScaleHint s) => s switch
    {
        ScaleHint.Small => "小", ScaleHint.Medium => "中", ScaleHint.Large => "大", _ => "?"
    };
}

/// <summary>确定性随机源(可种子化,便于单测与复盘重放)。</summary>
public sealed class Rng
{
    private readonly Random _r;
    public int Seed { get; }
    public Rng(int seed) { Seed = seed; _r = new Random(seed); }
    public double NextDouble() => _r.NextDouble();
    public int Next(int maxExclusive) => _r.Next(maxExclusive);
    public int Next(int minInclusive, int maxExclusive) => _r.Next(minInclusive, maxExclusive);
    /// <summary>以概率 p 返回 true。</summary>
    public bool Chance(double p) => _r.NextDouble() < p;
    /// <summary>在 [1-spread, 1+spread] 间的乘性抖动。</summary>
    public double Jitter(double spread) => 1.0 + (_r.NextDouble() * 2 - 1) * spread;
}

/// <summary>
/// 难度旋钮(§十三):信息为主 + 少量数值。预设档一键设定整个向量。
/// </summary>
public sealed class DifficultySettings
{
    public string Name { get; init; } = "普通";

    // —— 信息向量(你方) ——
    /// <summary>传令兵每 tick 行进格数(越慢 → 情报/命令延迟越大)。</summary>
    public double MessengerSpeed { get; init; } = 2.0;
    /// <summary>传令兵每 tick 被拦截的基础概率(丢失=纯沉默)。</summary>
    public double InterceptChancePerTick { get; init; } = 0.01;
    /// <summary>战报残缺(数量/兵种不明)的概率。</summary>
    public double PartialChance { get; init; } = 0.20;
    /// <summary>战报失真(数字夸大/缩小)的概率。</summary>
    public double DistortionChance { get; init; } = 0.15;
    /// <summary>是否显示战报可信度提示(高难度关闭 → 只能交叉比对)。</summary>
    public bool ShowCredibilityHint { get; init; } = true;
    /// <summary>主动派出的传令兵丢失时,是否提示「未归」(低难度提示;高难度纯沉默)。</summary>
    public bool NotifyLostActiveMessenger { get; init; } = true;
    /// <summary>传令兵自身视野(较小);途中瞥见敌军会顺带带回。</summary>
    public int MessengerVision { get; init; } = 2;
    /// <summary>斥候视野(比传令兵大)。</summary>
    public int ScoutVision { get; init; } = 6;
    /// <summary>斥候速度(比传令兵慢、比步兵快)。</summary>
    public double ScoutSpeed { get; init; } = 1.5;
    /// <summary>每支部队可同时在外的斥候数。</summary>
    public int UnitScoutPool { get; init; } = 2;
    /// <summary>中军帐可同时在外的斥候数。</summary>
    public int HqScoutPool { get; init; } = 3;

    // —— 旗鼓信号(§军争 金鼓旌旗) ——
    /// <summary>旌旗信号基础视程(从中军帐起算,按观察者地形/天气打折)。</summary>
    public int FlagRange { get; init; } = 7;
    /// <summary>金鼓信号基础声程(按地形阻尼)。</summary>
    public int DrumRange { get; init; } = 9;
    /// <summary>瞭望台(高处观察)视野半径:实时但低保真,随天气压缩。看得见烟尘 > 够得着号令。</summary>
    public int WatchtowerRange { get; init; } = 11;

    // —— 敌方向量 ——
    /// <summary>敌方决策质量(0..1,越高越接近最优)。</summary>
    public double EnemyDecisionQuality { get; init; } = 0.6;

    public static DifficultySettings Easy => new()
    {
        Name = "轻松", MessengerSpeed = 3.0, InterceptChancePerTick = 0.003,
        PartialChance = 0.08, DistortionChance = 0.05, ShowCredibilityHint = true,
        NotifyLostActiveMessenger = true,
        MessengerVision = 3, ScoutVision = 7, ScoutSpeed = 1.8, UnitScoutPool = 3, HqScoutPool = 4,
        FlagRange = 9, DrumRange = 11, WatchtowerRange = 13,
        EnemyDecisionQuality = 0.4
    };

    public static DifficultySettings Normal => new();

    public static DifficultySettings Hard => new()
    {
        Name = "硬核", MessengerSpeed = 1.5, InterceptChancePerTick = 0.02,
        PartialChance = 0.30, DistortionChance = 0.25, ShowCredibilityHint = false,
        NotifyLostActiveMessenger = false,
        MessengerVision = 1, ScoutVision = 5, ScoutSpeed = 1.3, UnitScoutPool = 1, HqScoutPool = 2,
        FlagRange = 5, DrumRange = 7, WatchtowerRange = 9,
        EnemyDecisionQuality = 0.85
    };
}

/// <summary>地形网格 + 天气;影响移动、侦察、传令(§十七)。</summary>
public sealed class TerrainGrid
{
    public int Width { get; }
    public int Height { get; }
    public Weather Weather { get; init; } = Weather.Clear;
    private readonly TerrainType[,] _cells;

    public TerrainGrid(int width, int height, TerrainType fill = TerrainType.Plain)
    {
        Width = width;
        Height = height;
        _cells = new TerrainType[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _cells[x, y] = fill;
    }

    public bool InBounds(Vec2 p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;
    public TerrainType At(Vec2 p) => InBounds(p) ? _cells[p.X, p.Y] : TerrainType.Plain;
    public void Set(Vec2 p, TerrainType t) { if (InBounds(p)) _cells[p.X, p.Y] = t; }

    /// <summary>移动成本倍率(林地慢、河流更慢)。</summary>
    public double MoveCost(Vec2 p) => At(p) switch
    {
        TerrainType.Forest => 2.0,
        TerrainType.River => 3.0,
        _ => 1.0
    };

    /// <summary>侦察半径乘子:雾/夜大幅压缩;林地略减(§十七)。</summary>
    public double ScoutMultiplier(Vec2 observerPos) =>
        Weather switch
        {
            Weather.Fog => 0.5,
            Weather.Night => 0.45,
            _ => At(observerPos) == TerrainType.Forest ? 0.7 : 1.0
        };

    /// <summary>传令兵被拦截概率乘子:夜/雾/林地更容易出事。</summary>
    public double InterceptMultiplier(Vec2 p) =>
        (Weather switch { Weather.Night => 1.8, Weather.Fog => 1.4, _ => 1.0 })
        * (At(p) == TerrainType.Forest ? 1.5 : 1.0);

    /// <summary>声音传播倍率(金鼓信号用):林地阻尼;夜略减。</summary>
    public double SoundMultiplier(Vec2 p) =>
        (At(p) == TerrainType.Forest ? 0.6 : 1.0) * (Weather == Weather.Night ? 0.9 : 1.0);
}
