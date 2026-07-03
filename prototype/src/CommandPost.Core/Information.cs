namespace CommandPost.Core;

/// <summary>一份(可能残缺的)对某单位的观测。</summary>
public sealed class PartialUnitInfo
{
    public int UnitId { get; init; }
    public Side Side { get; init; }
    public UnitType? Type { get; init; }        // null = 兵种不明(残缺)
    public Vec2 Pos { get; init; }
    public double? Strength { get; init; }       // null = 兵力不明(残缺)
}

/// <summary>一份战报(情报)。observed_tick 决定信息年龄;fidelity 表失真。</summary>
public sealed class Report
{
    public int ObservedTick { get; init; }
    public PartialUnitInfo About { get; init; } = default!;
    /// <summary>可信度 1=准确;&lt;1 表示数字被夸大/缩小。</summary>
    public double Fidelity { get; init; } = 1.0;
    public string Kind { get; init; } = "见敌";   // 见敌/接敌/受创/溃败/抵达/近况
}

/// <summary>一道命令(由传令兵送往前线某部队)。</summary>
public sealed class CommandPayload
{
    public int TargetUnitId { get; init; }
    public Intent? Intent { get; init; }
    /// <summary>若非空:命令该部队派斥候到此处(不改动其移动意图)。</summary>
    public Vec2? ScoutTo { get; init; }
}

/// <summary>在途传令兵:可携带战报(回中军帐)或命令(去前线)。会走位、会被拦截。</summary>
public sealed class Messenger
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public Vec2 Pos { get; set; }
    public Vec2 Destination { get; set; }
    /// <summary>命令的目标单位(homing:跟着该单位跑)。报告则为 null。</summary>
    public int? HomingUnitId { get; init; }

    /// <summary>携带的情报包(回中军帐):自身近况 + 该部队所知的敌情,可多份。</summary>
    public List<Report> Reports { get; init; } = new();
    public CommandPayload? Command { get; init; }
    public bool PlayerDispatched { get; init; }  // 玩家主动派出(用于「未归」提示判定)

    public bool Alive { get; set; } = true;
    public bool Arrived { get; set; }
    public bool IsCommand => Command != null;
    /// <summary>移动进度累加器(按 MessengerSpeed 逐 tick 累加,满 1 走一格)。</summary>
    public double Accum { get; set; }
    /// <summary>传令兵自身视野;途中瞥见的敌情(送达时一并并入目的地)。</summary>
    public int Vision { get; init; }
    public Dictionary<int, GhostUnit> Sightings { get; } = new();
}

/// <summary>瞭望台一次实时、低保真的观察(烟尘印象):此刻所见,不入认知世界、不留记忆。</summary>
public readonly record struct WatchtowerContact(Side Side, Vec2 Pos, ScaleHint Scale);

/// <summary>认知世界里的「最后已知」影子单位。</summary>
public sealed class GhostUnit
{
    public int UnitId { get; init; }
    public Side Side { get; init; }
    public Vec2 LastKnownPos { get; set; }
    public double? KnownStrength { get; set; }
    public UnitType? KnownType { get; set; }
    public int ObservedTick { get; set; }
    public double Fidelity { get; set; } = 1.0;
    public bool ConfirmedGone { get; set; }      // 确认歼灭/撤离才移除

    public int AgeAt(int nowTick) => Math.Max(0, nowTick - ObservedTick);
}

/// <summary>某一方的认知世界:只由已送达的战报拼成,必然滞后/残缺/失真。</summary>
public sealed class BeliefWorld
{
    public Side Owner { get; init; }
    public Dictionary<int, GhostUnit> Known { get; } = new();
    /// <summary>待玩家处理 / 最近送达的战报(文书堆)。</summary>
    public List<Report> Inbox { get; } = new();

    public IEnumerable<GhostUnit> KnownOf(Side side) =>
        Known.Values.Where(g => g.Side == side && !g.ConfirmedGone);

    /// <summary>用一份送达的战报更新认知世界。</summary>
    public void Integrate(Report r)
    {
        var info = r.About;
        if (!Known.TryGetValue(info.UnitId, out var g))
        {
            g = new GhostUnit { UnitId = info.UnitId, Side = info.Side };
            Known[info.UnitId] = g;
        }
        // 只用更新的观测覆盖(旧战报不应回退较新的认知)
        if (r.ObservedTick >= g.ObservedTick)
        {
            g.LastKnownPos = info.Pos;
            if (info.Strength is double s) g.KnownStrength = s;
            if (info.Type is UnitType t) g.KnownType = t;
            g.ObservedTick = r.ObservedTick;
            g.Fidelity = r.Fidelity;
        }
        Inbox.Add(r);
    }
}
