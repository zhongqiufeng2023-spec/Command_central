namespace CommandPost.Core;

/// <summary>
/// 斥候:视野比传令兵大、速度比传令兵慢。被部队或中军帐派往指定位置侦察。
/// 沿途避开敌军(不靠近),发现敌军则记下;到达目标后返回派出者并汇报。
/// 部队派出的 → 汇报给该部队;中军派出的 → 返回中军帐。
/// </summary>
public sealed class Scout
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public Vec2 Pos { get; set; }
    public Vec2 Target { get; init; }
    /// <summary>派出者部队;null = 中军帐派出。</summary>
    public int? OwnerUnitId { get; init; }
    public int Vision { get; init; }

    /// <summary>返回点(每 tick 刷新:部队当前位置 或 中军帐)。</summary>
    public Vec2 Home { get; set; }
    public bool Returning { get; set; }
    public bool Done { get; set; }

    /// <summary>斥候朝向(格方向):随移动更新,渲染画一个方向标。</summary>
    public Vec2 Facing { get; set; } = new Vec2(1, 0);

    /// <summary>沿途所见敌情(真相快照 + 观测时刻)。</summary>
    public Dictionary<int, GhostUnit> Sightings { get; } = new();

    public double Accum { get; set; }
    /// <summary>绕道受阻的耐心:耗尽则放弃目标、提前返回。</summary>
    public int Patience { get; set; } = 12;
}
