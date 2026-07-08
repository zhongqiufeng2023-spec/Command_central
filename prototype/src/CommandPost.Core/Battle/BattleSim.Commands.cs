using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

// 玩家指令层:一切经沙盘与令骑,没有直控。
public sealed partial class BattleSim
{
    // —— 战前布阵(时间冻结,当面吩咐,即时生效)——

    /// <summary>布阵:把本方某部搬到布阵区内某处(士兵齐齐落位,沙盘即时同步——你亲眼看着他们站好)。</summary>
    public bool DeployMove(int unitId, Vec2F pos)
    {
        if (!Deploying || ById(unitId) is not { Side: Side.Friend, Allied: false } u) return false;
        pos = Map.Clamp(pos);
        if (pos.X > DeployZoneMaxX) pos = new Vec2F(DeployZoneMaxX, pos.Y);
        pos = Map.NearestPassable(pos);
        u.Center = pos;
        for (int i = 0; i < u.Soldiers.Count; i++) u.Soldiers[i].Pos = u.SlotWorld(i);
        if (Sandbox.Own.TryGetValue(unitId, out var mk)) { mk.Pos = pos; mk.T = Time; }
        return true;
    }

    /// <summary>布阵:当面吩咐姿态(不费令骑)。</summary>
    public void DeployStance(int unitId, BStance st)
    {
        if (!Deploying || ById(unitId) is not { Side: Side.Friend, Allied: false } u) return;
        u.Stance = st;
        if (Sandbox.Own.TryGetValue(unitId, out var mk)) mk.StateCn = $"{u.StanceCn}·列阵";
    }

    /// <summary>下令:令骑从帅帐出发,循「沙盘上次所报位置」去找该部(途中按战场旗号校向真实位置)。</summary>
    public void IssueMove(int unitId, Vec2F dest, bool run)
    {
        if (Deploying || ById(unitId) is not { } u || u.Side != Side.Friend || u.Allied || u.AliveCount == 0) return;
        dest = Map.NearestPassable(Map.Clamp(dest));
        var start = Sandbox.Own.TryGetValue(unitId, out var mk) ? mk.Pos : u.Center;
        var r = NewRider(RiderKind.Order, BattlePath.Find(Map, HqPos, start));
        r.TargetUnitId = unitId; r.OrderDest = dest; r.OrderRun = run; r.HasMove = true;
        r.DescCn = $"令骑→{u.Name}";
        Feed($"令骑驰出:令 {u.Name} {(run ? "疾进" : "进")}至 ({(int)dest.X},{(int)dest.Y})");
    }

    /// <summary>下姿态令(进攻/据守/等待):同样由令骑送达,部队此后按姿态自主行事。</summary>
    public void IssueStance(int unitId, BStance stance)
    {
        if (Deploying || ById(unitId) is not { } u || u.Side != Side.Friend || u.Allied || u.AliveCount == 0) return;
        var start = Sandbox.Own.TryGetValue(unitId, out var mk) ? mk.Pos : u.Center;
        var r = NewRider(RiderKind.Order, BattlePath.Find(Map, HqPos, start));
        r.TargetUnitId = unitId; r.OrderStance = stance;
        r.DescCn = $"令骑→{u.Name}";
        Feed($"令骑驰出:令 {u.Name} 转「{StanceCnOf(stance)}」");
    }

    public static string StanceCnOf(BStance s) => s switch
    { BStance.Attack => "进攻", BStance.Standby => "等待", BStance.Skirmish => "游走", _ => "据守" };

    /// <summary>派塘骑侦察某处(到点驻观 3 秒,回来把沿途所见并入沙盘)。</summary>
    public void DispatchScout(Vec2F dest)
    {
        if (Deploying) return;
        dest = Map.NearestPassable(Map.Clamp(dest));
        var r = NewRider(RiderKind.Scout, BattlePath.Find(Map, HqPos, dest));
        r.DestPoint = dest; r.DwellLeft = 3f; r.Speed = 9.5f;
        r.DescCn = "塘骑侦察";
        Feed($"塘骑驰出:侦察 ({(int)dest.X},{(int)dest.Y})");
    }

    /// <summary>探问某部近况(令骑往返,带回该部即时状态)。</summary>
    public void RequestStatus(int unitId)
    {
        if (Deploying || ById(unitId) is not { } u || u.Side != Side.Friend || u.Allied) return;
        var start = Sandbox.Own.TryGetValue(unitId, out var mk) ? mk.Pos : u.Center;
        var r = NewRider(RiderKind.Query, BattlePath.Find(Map, HqPos, start));
        r.TargetUnitId = unitId;
        r.DescCn = $"探问{u.Name}";
        Feed($"令骑驰出:探问 {u.Name} 近况");
    }

    /// <summary>沙盘放置信息旗(纯标注,即时)。</summary>
    public void PlaceFlag(Vec2F p, string? label = null)
        => Sandbox.Flags.Add(new FlagMarker { Pos = Map.Clamp(p), Label = label ?? $"旗{_nextFlag++}" });

    public void RemoveFlagNear(Vec2F p, float radius = 24f)
    {
        var f = Sandbox.Flags.OrderBy(f => f.Pos.DistanceTo(p)).FirstOrDefault();
        if (f != null && f.Pos.DistanceTo(p) <= radius) Sandbox.Flags.Remove(f);
    }

    private Rider NewRider(RiderKind kind, List<Vec2F> path)
    {
        var r = new Rider
        {
            Id = _nextRider++, Kind = kind, Pos = HqPos, Path = path,
            Depart = Time, EstPath = new List<Vec2F>(path)
        };
        float len = PathLength(HqPos, path);
        r.ExpectedBack = Time + (len * 2f) / r.Speed + 18f;
        Riders.Add(r);
        return r;
    }

    private static float PathLength(Vec2F from, List<Vec2F> path)
    {
        float len = 0; var cur = from;
        foreach (var p in path) { len += cur.DistanceTo(p); cur = p; }
        return len;
    }
}
