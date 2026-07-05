using System.Collections.Generic;

namespace CommandPost.Core;

// 战斗B档的信息载体(DTO):箭矢、令骑、沿途所见、沙盘标记。
// 与 BattleSim 逻辑解耦——这些只是「数据」,不含行为。

/// <summary>一支飞行中的箭/弩矢:真实弹道,落点结算命中(可能误伤)。</summary>
public sealed class Arrow
{
    public Vec2F Pos, Vel;
    public Vec2F Origin;         // 射出点(被射中者由此知道威胁来向)
    public float TravelLeft;
    public float RawDamage;      // 未计克制/护甲
    public UnitType Shooter;
    public Side Side;
}

public enum RiderKind { Order, Report, Scout, Query }
public enum RiderPhase { Outbound, Dwell, Return }

/// <summary>令骑/塘骑:真实世界里骑行的信使——送令、回报、侦察、探问。会被截杀(纯沉默)。</summary>
public sealed class Rider
{
    public int Id;
    public RiderKind Kind;
    public Vec2F Pos;
    public float Speed = 8.5f;
    public RiderPhase Phase = RiderPhase.Outbound;
    public List<Vec2F> Path = new();
    public int TargetUnitId = -1;
    public Vec2F DestPoint;
    public Vec2F OrderDest; public bool OrderRun; public bool HasMove;
    public BStance? OrderStance;             // 携带的姿态令(可与移动令同乘一骑)
    public float DwellLeft;
    public Dictionary<int, EnemySighting> Sightings = new();
    public OwnStatus? ReportOwn;                  // 携带的我部近况
    public bool Lost, Delivered, OverdueAlerted;
    /// <summary>军书上行骑手:目的地是行营(西缘出图),不回帐。</summary>
    public bool ToHq;
    public float Depart, ExpectedBack;
    /// <summary>沙盘估算行程用:出发时的计划路线(非真实位置——被截杀了你也不知道)。</summary>
    public List<Vec2F> EstPath = new();
    public bool RoundTrip = true;
    public string DescCn = "";
}

public readonly record struct EnemySighting(Vec2F Pos, int Est, UnitType? Type, float T);
public readonly record struct OwnStatus(int UnitId, Vec2F Pos, int Count, string StateCn, float T);

public sealed class SandboxOwnMark { public int UnitId; public Vec2F Pos; public int Count; public string StateCn = "就位"; public float T; }
public sealed class SandboxEnemyMark { public int UnitId; public Vec2F Pos; public int Est; public UnitType? Type; public float T; }
public sealed class FlagMarker { public Vec2F Pos; public string Label = ""; }

/// <summary>玩家的沙盘:只装「送到手上的信息」——绝不引用真实世界对象(双世界铁律)。</summary>
public sealed class SandboxState
{
    public Dictionary<int, SandboxOwnMark> Own { get; } = new();
    public Dictionary<int, SandboxEnemyMark> Enemy { get; } = new();
    public List<FlagMarker> Flags { get; } = new();
    public List<string> Feed { get; } = new();
}
