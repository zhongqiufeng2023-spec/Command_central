namespace CommandPost.Core;

/// <summary>复盘用:某一 tick 的真相世界快照(上帝视角)。战中绝不展示,仅战后回放。</summary>
public sealed class ReplayFrame
{
    public int Tick { get; init; }
    public List<ReplayUnit> Units { get; init; } = new();
    public List<ReplayMover> Messengers { get; init; } = new();
    public List<ReplayMover> Scouts { get; init; } = new();
}

public readonly record struct ReplayUnit(
    int Id, Side Side, UnitType Type, string Name, Vec2 Pos,
    int Strength, int Morale, bool Routed, bool Dead);

public readonly record struct ReplayMover(Side Side, Vec2 Pos, bool IsCommand);
