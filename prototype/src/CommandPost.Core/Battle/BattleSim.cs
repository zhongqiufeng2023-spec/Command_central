using System;
using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

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

/// <summary>
/// 战斗B档:全战式逐兵真实层 + Radio Commander 式沙盘指挥层。
/// 真实层:每兵一单位(血量/阵位/近战/箭矢/士气/溃逃),连续坐标,10Hz 固定步。
/// 指挥层:玩家只看沙盘;下令=令骑真实骑行送达;敌情=斥候/军报带回的旧影。
/// </summary>
public sealed class BattleSim
{
    public const float Dt = 0.1f;

    public BattleMap Map { get; }
    public List<BattleUnit> Units { get; } = new();
    public List<Arrow> Arrows { get; } = new();
    public List<Rider> Riders { get; } = new();
    public SandboxState Sandbox { get; } = new();
    public List<Alert> Alerts { get; } = new();
    public Vec2F HqPos { get; set; }
    public float Time { get; private set; }
    public bool Over { get; private set; }
    public Side? Winner { get; private set; }

    private readonly Rng _rng;
    private int _nextUnit = 1, _nextSoldier = 1, _nextRider = 1, _nextFlag = 1;
    private readonly Dictionary<int, BattleUnit> _byId = new();
    private readonly Dictionary<(int, int), List<Soldier>> _hash = new();
    /// <summary>敌方共享记忆:我方部队「最后所见」(敌 AI 的对称迷雾,简化版)。</summary>
    private readonly Dictionary<int, (Vec2F pos, float t)> _enemyKnown = new();
    private float _endClock, _spotClock;
    private const float HashCell = 3f;

    public BattleSim(BattleMap map, Rng rng) { Map = map; _rng = rng; }

    public BattleUnit? ById(int id) => _byId.TryGetValue(id, out var u) ? u : null;

    public BattleUnit AddUnit(Side side, UnitType type, string name, Commander officer, Vec2F center, int count, bool ai = false)
    {
        var u = new BattleUnit
        {
            Id = _nextUnit++, Side = side, Type = type, Name = name, Officer = officer,
            Center = Map.Clamp(center), MaxCount = count, AiControlled = ai,
            Facing = side == Side.Friend ? new Vec2F(1, 0) : new Vec2F(-1, 0),
            ReportClock = 20f + (_nextUnit % 5) * 4f      // 错峰:别全军同刻发军报
        };
        for (int i = 0; i < count; i++)
            u.Soldiers.Add(new Soldier
            {
                Id = _nextSoldier++, UnitId = u.Id, Pos = u.SlotWorld(i),
                Ammo = BArms.AmmoOf(type),
                ReloadT = 0.5f + (float)_rng.NextDouble() * 2f,   // 开战已上弦:首轮齐射快
                CoolT = (float)_rng.NextDouble()
            });
        Units.Add(u); _byId[u.Id] = u;
        if (side == Side.Friend)
            Sandbox.Own[u.Id] = new SandboxOwnMark { UnitId = u.Id, Pos = u.Center, Count = count, StateCn = "就位", T = 0 };
        return u;
    }

    private float RandReload(UnitType t)
    { var (lo, hi) = BArms.ReloadOf(t); return lo + (float)_rng.NextDouble() * (hi - lo); }

    // ====================================================================
    //  玩家 API —— 全部经沙盘与令骑,没有直控
    // ====================================================================

    /// <summary>下令:令骑从帅帐出发,循「沙盘上次所报位置」去找该部(途中按战场旗号校向真实位置)。</summary>
    public void IssueMove(int unitId, Vec2F dest, bool run)
    {
        if (ById(unitId) is not { } u || u.Side != Side.Friend || u.AliveCount == 0) return;
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
        if (ById(unitId) is not { } u || u.Side != Side.Friend || u.AliveCount == 0) return;
        var start = Sandbox.Own.TryGetValue(unitId, out var mk) ? mk.Pos : u.Center;
        var r = NewRider(RiderKind.Order, BattlePath.Find(Map, HqPos, start));
        r.TargetUnitId = unitId; r.OrderStance = stance;
        r.DescCn = $"令骑→{u.Name}";
        Feed($"令骑驰出:令 {u.Name} 转「{StanceCnOf(stance)}」");
    }

    public static string StanceCnOf(BStance s) => s switch
    { BStance.Attack => "进攻", BStance.Standby => "等待", _ => "据守" };

    /// <summary>派塘骑侦察某处(到点驻观 3 秒,回来把沿途所见并入沙盘)。</summary>
    public void DispatchScout(Vec2F dest)
    {
        dest = Map.NearestPassable(Map.Clamp(dest));
        var r = NewRider(RiderKind.Scout, BattlePath.Find(Map, HqPos, dest));
        r.DestPoint = dest; r.DwellLeft = 3f; r.Speed = 9.5f;
        r.DescCn = "塘骑侦察";
        Feed($"塘骑驰出:侦察 ({(int)dest.X},{(int)dest.Y})");
    }

    /// <summary>探问某部近况(令骑往返,带回该部即时状态)。</summary>
    public void RequestStatus(int unitId)
    {
        if (ById(unitId) is not { } u || u.Side != Side.Friend) return;
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

    // ====================================================================
    //  主循环(固定步 0.1s)
    // ====================================================================

    public void Tick()
    {
        if (Over) return;
        Time += Dt;
        UpdateEnemyKnowledge();
        UpdateEnemyAi();
        UpdateStances();     // 我方各部按姿态自主行事(进攻扑敌/等待避战)
        UpdateRiders();
        AutoReports();
        UpdateUnits();
        RebuildHash();
        UpdateSoldiers();
        UpdateArrows();
        RemoveDead();
        CheckBattleEnd();
    }

    // —— 敌方对称迷雾(简化):敌单位 110m 视界(林中打折)看见我方即记入共享记忆,40s 后遗忘 ——
    private void UpdateEnemyKnowledge()
    {
        _spotClock += Dt;
        if (_spotClock < 1f) return;
        _spotClock = 0;
        foreach (var f in Units.Where(x => x.Side == Side.Friend && x.AliveCount > 0))
        {
            float conceal = BattleMap.ConcealMult(Map.At(f.Center));
            foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0))
                if (e.Center.DistanceTo(f.Center) <= 110f * conceal)
                { _enemyKnown[f.Id] = (f.Center, Time); break; }
        }
        foreach (var k in _enemyKnown.Where(kv => Time - kv.Value.t > 40f).Select(kv => kv.Key).ToList())
            _enemyKnown.Remove(k);
    }

    private void UpdateEnemyAi()
    {
        foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AiControlled && x.Controllable))
        {
            e.ThinkClock -= Dt;
            if (e.ThinkClock > 0) continue;
            e.ThinkClock = 2.5f;

            (Vec2F pos, float t)? known = null;
            float kb = float.MaxValue;
            foreach (var kv in _enemyKnown.Values)
            {
                float d = kv.pos.DistanceTo(e.Center);
                if (d < kb) { kb = d; known = kv; }
            }
            if (known is { } kn)
            {
                float dist = kn.pos.DistanceTo(e.Center);
                if (BArms.Ranged(e.Type) && dist < 70f)
                {
                    var away = (e.Center - kn.pos).Normalized;              // 骑射放风筝:拉开到百米
                    e.SetOrderDirect(Map.Clamp(e.Center + away * 60f), true, Map);
                }
                else if (dist > 14f)
                    e.SetOrderDirect(kn.pos, dist < 120f && BArms.IsCav(e.Type), Map);
            }
            else if (e.State == BUnitState.Steady && e.Path.Count == 0)
                e.SetOrderDirect(Map.Clamp(e.Center + new Vec2F(-90f, 0)), false, Map);   // 无敌情:徐进压上
        }
    }

    /// <summary>我方各部的姿态自主行为(每 2s 想一次)——没有命令也不再站着挨打。</summary>
    private void UpdateStances()
    {
        foreach (var u in Units.Where(x => x.Side == Side.Friend && !x.AiControlled && x.Controllable))
        {
            u.ThinkClock -= Dt;
            if (u.ThinkClock > 0) continue;
            u.ThinkClock = 2f;
            bool threatened = Time - u.LastThreatT < 8f;

            switch (u.Stance)
            {
                case BStance.Attack:
                {
                    // 视界内寻敌(林中难见);看不见但刚挨打 → 朝威胁来向扑
                    BattleUnit? tgt = null; float bd = float.MaxValue;
                    foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0))
                    {
                        float vis = 130f * BattleMap.ConcealMult(Map.At(e.Center));
                        float d = e.Center.DistanceTo(u.Center);
                        if (d <= vis && d < bd) { bd = d; tgt = e; }
                    }
                    if (tgt != null && bd > 10f)
                        u.SetOrderDirect(tgt.Center, run: BArms.IsCav(u.Type) || bd < 60f, Map);
                    else if (tgt == null && threatened)
                        u.SetOrderDirect(u.LastThreatPos, run: true, Map);
                    break;
                }
                case BStance.Standby:
                {
                    // 避战自保:敌近或挨打 → 反向拉开 70m
                    Vec2F? threat = threatened ? u.LastThreatPos : null;
                    var near = NearestUnit(u.Center, Side.Enemy);
                    if (near != null && near.Center.DistanceTo(u.Center) < 55f) threat = near.Center;
                    if (threat is { } tp)
                        u.SetOrderDirect(Map.Clamp(u.Center + (u.Center - tp).Normalized * 70f), run: true, Map);
                    break;
                }
                // Hold(据守):钉在原地——近战自动还手,弩手自动齐射
            }
        }
    }

    // ====================================================================
    //  令骑
    // ====================================================================

    private void UpdateRiders()
    {
        foreach (var r in Riders)
        {
            if (r.Delivered || r.Lost)
            {
                // 截杀 = 纯沉默:超时才给「未归」提示
                if (r.Lost && !r.OverdueAlerted && Time > r.ExpectedBack)
                { r.OverdueAlerted = true; Alerts.Add(new Alert((int)Time, $"{r.DescCn} 迟迟未归……", true)); }
                continue;
            }

            RecordRiderSightings(r);

            // 截杀风险:身边 40m 有敌部
            foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0))
                if (e.Center.DistanceTo(r.Pos) < 40f && _rng.Chance(0.015 * Dt)) { r.Lost = true; break; }
            if (r.Lost) continue;

            // 送令/探问:目标部队在动,途中校向真实位置(战场上循旗而行)
            if (r.Phase == RiderPhase.Outbound && r.Kind is RiderKind.Order or RiderKind.Query
                && ById(r.TargetUnitId) is { AliveCount: > 0 } tu
                && (r.Path.Count == 0 || r.Path[^1].DistanceTo(tu.Center) > 25f))
                r.Path = BattlePath.Find(Map, r.Pos, tu.Center);

            if (r.Phase == RiderPhase.Dwell)
            {
                r.DwellLeft -= Dt;
                if (r.DwellLeft <= 0) { r.Phase = RiderPhase.Return; r.Path = BattlePath.Find(Map, r.Pos, HqPos); }
                continue;
            }

            MoveAlongPath(r);
            if (r.Path.Count > 0) continue;

            if (r.Phase == RiderPhase.Outbound)
            {
                switch (r.Kind)
                {
                    case RiderKind.Order when ById(r.TargetUnitId) is { } u && u.AliveCount > 0:
                        if (r.OrderStance is { } st) u.Stance = st;
                        if (r.HasMove) u.SetOrderDirect(r.OrderDest, r.OrderRun, Map);
                        r.ReportOwn = Snapshot(u);
                        break;
                    case RiderKind.Query when ById(r.TargetUnitId) is { } q && q.AliveCount > 0:
                        r.ReportOwn = Snapshot(q);
                        break;
                    case RiderKind.Scout:
                        r.Phase = RiderPhase.Dwell;
                        continue;
                }
                r.Phase = RiderPhase.Return;
                r.Path = BattlePath.Find(Map, r.Pos, HqPos);
            }
            else if (r.Phase == RiderPhase.Return)
            {
                r.Delivered = true;
                MergeRiderIntel(r);
            }
        }
        Riders.RemoveAll(r => r.Delivered || (r.Lost && r.OverdueAlerted));
    }

    private void MoveAlongPath(Rider r)
    {
        float budget = r.Speed * MathF.Max(0.45f, BattleMap.SpeedMult(Map.At(r.Pos))) * Dt;
        while (budget > 0 && r.Path.Count > 0)
        {
            var next = r.Path[0];
            float d = r.Pos.DistanceTo(next);
            if (d <= budget) { r.Pos = next; r.Path.RemoveAt(0); budget -= d; }
            else { r.Pos += (next - r.Pos).Normalized * budget; budget = 0; }
        }
    }

    /// <summary>骑手沿途视界 100m:所见敌部记为带误差的估计(远则糊,驻观的塘骑最准)。</summary>
    private void RecordRiderSightings(Rider r)
    {
        foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0))
        {
            float dist = e.Center.DistanceTo(r.Pos);
            float range = (r.Kind == RiderKind.Scout ? 130f : 100f) * BattleMap.ConcealMult(Map.At(e.Center));
            if (dist > range) continue;
            float err = Math.Clamp(dist / (r.Phase == RiderPhase.Dwell ? 500f : 300f), 0.05f, 0.35f);
            int est = Math.Max(10, (int)Math.Round(e.AliveCount * (1 + ((float)_rng.NextDouble() * 2 - 1) * err) / 10f) * 10);
            UnitType? type = dist < 90f || r.Phase == RiderPhase.Dwell ? e.Type : null;
            if (!r.Sightings.TryGetValue(e.Id, out var old) || Time > old.T)
                r.Sightings[e.Id] = new EnemySighting(e.Center, est, type, Time);
        }
    }

    private OwnStatus Snapshot(BattleUnit u) => new(u.Id, u.Center, u.AliveCount, $"{u.StanceCn}·{u.StateCn}", Time);

    // —— 瞭望台:帅帐望楼的实时所见(低保真、只在范围内、不留记忆)——
    public float WatchtowerRange { get; set; } = 180f;
    public IEnumerable<BattleUnit> WatchtowerVisible() =>
        Units.Where(u => u.AliveCount > 0 &&
            u.Center.DistanceTo(HqPos) <= WatchtowerRange * BattleMap.ConcealMult(Map.At(u.Center)));

    /// <summary>骑手回帐:所携情报并入沙盘(信息年龄 = 采集时刻,不是送达时刻)。</summary>
    private void MergeRiderIntel(Rider r)
    {
        if (r.ReportOwn is { } own)
        {
            var mk = Sandbox.Own.TryGetValue(own.UnitId, out var m) ? m : Sandbox.Own[own.UnitId] = new SandboxOwnMark { UnitId = own.UnitId };
            if (own.T >= mk.T)
            { mk.Pos = own.Pos; mk.Count = own.Count; mk.StateCn = own.StateCn; mk.T = own.T; }
            Feed($"军报:{ById(own.UnitId)?.Name} 现约{own.Count}人,{own.StateCn}");
        }
        foreach (var (eid, s) in r.Sightings)
        {
            bool isNew = !Sandbox.Enemy.TryGetValue(eid, out var em);
            if (isNew) em = Sandbox.Enemy[eid] = new SandboxEnemyMark { UnitId = eid };
            if (s.T >= em!.T)
            { em.Pos = s.Pos; em.Est = s.Est; em.Type = s.Type ?? em.Type; em.T = s.T; }
            string ty = em.Type is UnitType t ? ArmCn(t) : "不明";
            if (isNew)
                Alerts.Add(new Alert((int)Time, $"侦得敌军:{ty}约{em.Est}骑步,于 ({(int)em.Pos.X},{(int)em.Pos.Y})", true));
            else
                Feed($"敌情更新:{ty}约{em.Est} @({(int)em.Pos.X},{(int)em.Pos.Y})");
        }
        if (r.Kind == RiderKind.Scout && r.Sightings.Count == 0)
            Feed("塘骑归:所探之处未见敌踪");
    }

    // ====================================================================
    //  军报(部队自发:定期 + 事件)
    // ====================================================================

    private void AutoReports()
    {
        foreach (var u in Units.Where(x => x.Side == Side.Friend && x.AliveCount > 0))
        {
            u.ReportClock -= Dt;
            u.EventCooldown -= Dt;

            bool evt = false;
            if (u.State == BUnitState.Engaged && !u.ReportedEngaged) { u.ReportedEngaged = true; evt = true; }
            if (u.State != BUnitState.Engaged) u.ReportedEngaged = false;
            if (u.State is BUnitState.Routing && !u.ReportedRouting) { u.ReportedRouting = true; evt = true; }

            if (u.ReportClock <= 0 || (evt && u.EventCooldown <= 0))
            {
                u.ReportClock = 40f;
                u.EventCooldown = 15f;
                var r = new Rider
                {
                    Id = _nextRider++, Kind = RiderKind.Report, Pos = u.Center,
                    Phase = RiderPhase.Return, Path = BattlePath.Find(Map, u.Center, HqPos),
                    Depart = Time, ReportOwn = Snapshot(u), DescCn = $"{u.Name}的军报",
                    EstPath = new List<Vec2F>(), RoundTrip = false
                };
                r.ExpectedBack = Time + PathLength(u.Center, r.Path) / r.Speed + 15f;
                // 该部当下所见敌情随报捎回
                foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0))
                {
                    float dist = e.Center.DistanceTo(u.Center);
                    if (dist > 95f * BattleMap.ConcealMult(Map.At(e.Center))) continue;
                    float err = Math.Clamp(dist / 300f, 0.05f, 0.3f);
                    int est = Math.Max(10, (int)Math.Round(e.AliveCount * (1 + ((float)_rng.NextDouble() * 2 - 1) * err) / 10f) * 10);
                    r.Sightings[e.Id] = new EnemySighting(e.Center, est, dist < 80f ? e.Type : null, Time);
                }
                Riders.Add(r);
            }
        }
    }

    // ====================================================================
    //  部队级:行军 / 接战判定 / 士气 / 溃逃与收拢
    // ====================================================================

    private void UpdateUnits()
    {
        foreach (var u in Units)
        {
            if (u.State == BUnitState.Destroyed) continue;
            if (u.AliveCount == 0)
            {
                u.State = BUnitState.Destroyed;
                if (u.Side == Side.Friend) Alerts.Add(new Alert((int)Time, $"{u.Name} 覆没!", true));
                continue;
            }

            u.RecentLoss *= MathF.Exp(-Dt / 5f);
            u.ChargeT = MathF.Max(0, u.ChargeT - Dt);

            var nearestFoe = NearestUnit(u.Center, u.Side == Side.Friend ? Side.Enemy : Side.Friend);
            float foeDist = nearestFoe?.Center.DistanceTo(u.Center) ?? float.MaxValue;

            if (u.State is BUnitState.Routing or BUnitState.Shattered)
            {
                UpdateRout(u, foeDist);
                continue;
            }

            // 接战判定:有兵正在挥刀,或敌部已经撞进阵里(推进不提前冻结——前排咬上才算接战)
            bool engaged = foeDist < 5f || u.Soldiers.Any(s => s.Fighting);
            if (engaged && u.State != BUnitState.Engaged && BArms.IsCav(u.Type) && u.SpeedNow > 3.5f)
                u.ChargeT = 2.5f;                                        // 冲锋窗口:带着冲量撞进去
            u.State = engaged ? BUnitState.Engaged : (u.Morale < 25f ? BUnitState.Wavering : BUnitState.Steady);

            // 行军(接战则钉住,由士兵层厮杀)
            if (!engaged && u.Path.Count > 0)
            {
                float speed = BArms.SpeedOf(u.Type) * BattleMap.SpeedMult(Map.At(u.Center))
                            * (u.Running ? 1.5f : 1f) * (0.8f + 0.2f * u.Stamina / 100f);
                var before = u.Center;
                float budget = speed * Dt;
                while (budget > 0 && u.Path.Count > 0)
                {
                    var next = u.Path[0];
                    float d = u.Center.DistanceTo(next);
                    if (d <= budget) { u.Center = next; u.Path.RemoveAt(0); budget -= d; }
                    else { u.Center += (next - u.Center).Normalized * budget; budget = 0; }
                }
                var moved = u.Center - before;
                u.SpeedNow = moved.Length / Dt;
                if (moved.LengthSq > 0.0001f) u.Facing = moved.Normalized;
                if (u.Path.Count == 0) { u.Order = BOrderKind.Hold; u.Running = false; }
            }
            else if (engaged)
            {
                u.SpeedNow = 0;
                // 阵心随人堆漂移,面向最近之敌
                var sum = new Vec2F(0, 0);
                foreach (var s in u.Soldiers) sum += s.Pos;
                u.Center = sum * (1f / u.AliveCount);
                if (nearestFoe != null)
                {
                    u.Facing = (nearestFoe.Center - u.Center).Normalized;
                    u.LastThreatPos = nearestFoe.Center; u.LastThreatT = Time;   // 接刃即知威胁所在
                }
            }
            else u.SpeedNow = 0;

            // 体力
            if (engaged) u.Stamina -= 2f * Dt;
            else if (u.Path.Count > 0) u.Stamina -= (u.Running ? 1.2f : 0.4f) * Dt;
            else u.Stamina += 1.2f * Dt;
            u.Stamina = Math.Clamp(u.Stamina, 0, 100);

            // 士气(0.5s 一算,平滑逼近)
            u.MoraleClock += Dt;
            if (u.MoraleClock >= 0.5f)
            {
                u.MoraleClock = 0;
                UpdateMorale(u, nearestFoe, foeDist);
            }

            // 远程:每 0.5s 选齐射目标(有克制空间且不误伤缠斗中的自己人)
            if (BArms.Ranged(u.Type)) PickVolleyTarget(u);
        }
    }

    private void UpdateMorale(BattleUnit u, BattleUnit? foe, float foeDist)
    {
        int nearRoutFriends = Units.Count(x => x.Side == u.Side && x != u && x.State is BUnitState.Routing or BUnitState.Shattered
                                            && x.AliveCount > 0 && x.Center.DistanceTo(u.Center) < 80f);
        int nearRoutEnemies = Units.Count(x => x.Side != u.Side && x.State is BUnitState.Routing or BUnitState.Shattered
                                            && x.AliveCount > 0 && x.Center.DistanceTo(u.Center) < 80f);
        // 侧后受敌:贴身敌部相对朝向
        bool rear = false; int attackers = 0;
        foreach (var e in Units.Where(x => x.Side != u.Side && x.AliveCount > 0 && x.Center.DistanceTo(u.Center) < 18f))
        {
            attackers++;
            if ((e.Center - u.Center).Normalized.Dot(u.Facing) < -0.35f) rear = true;
        }

        float m = 55f + (float)u.Officer.Competence * 25f
                - u.LossFrac * 55f
                - u.RecentLoss * 3.5f
                - (attackers >= 2 ? 12f : 0)
                - (rear ? 15f : 0)
                - nearRoutFriends * 8f
                + nearRoutEnemies * 6f
                - (u.Stamina < 25f ? 8f : 0)
                - (u.Shaken ? 12f : 0);
        u.Morale += (Math.Clamp(m, 0, 100) - u.Morale) * 0.3f;

        if (u.Morale < 10f && (u.State == BUnitState.Engaged || u.RecentLoss > 2f))
        {
            u.State = BUnitState.Routing;
            u.Path.Clear(); u.Order = BOrderKind.Hold; u.RoutClock = 0; u.RallyClock = 0;
            u.Kills += 0; // 溃走瞬间无事,伤亡照旧算
        }
    }

    private void UpdateRout(BattleUnit u, float foeDist)
    {
        u.RoutClock += Dt;
        // 阵心随人堆
        var sum = new Vec2F(0, 0);
        foreach (var s in u.Soldiers) sum += s.Pos;
        u.Center = sum * (1f / Math.Max(1, u.AliveCount));

        if (u.State == BUnitState.Routing && u.AliveCount < u.MaxCount * 0.25f)
            u.State = BUnitState.Shattered;                            // 折损过半再溃 → 彻底散了

        if (u.State == BUnitState.Routing)
        {
            u.RallyClock += Dt;
            if (u.RallyClock >= 3f)
            {
                u.RallyClock = 0;
                if (foeDist > 90f) u.Morale += 7f;
                if (u.Morale >= 30f)
                {
                    u.State = BUnitState.Steady; u.Shaken = true;      // 收拢归建,但已胆寒
                    u.ReportedRouting = false;
                    if (u.Side == Side.Friend) { /* 军报会带回,不在此穿帮 */ }
                }
            }
        }
    }

    private void PickVolleyTarget(BattleUnit u)
    {
        u.VolleyTargetId = -1;
        if (u.State == BUnitState.Engaged) return;                     // 缠斗中无暇齐射
        float range = BArms.RangeOf(u.Type);
        BattleUnit? best = null; float bd = float.MaxValue;
        foreach (var e in Units.Where(x => x.Side != u.Side && x.AliveCount > 0))
        {
            float d = e.Center.DistanceTo(u.Center);
            if (d > range || d >= bd) continue;
            // 不朝与自己人绞在一起的敌部抛射(免屠自己人;流矢仍可能误伤)
            bool mixed = e.State == BUnitState.Engaged &&
                Units.Any(f => f.Side == u.Side && f.AliveCount > 0 && f.Center.DistanceTo(e.Center) < 14f);
            if (mixed) continue;
            best = e; bd = d;
        }
        if (best != null) u.VolleyTargetId = best.Id;
    }

    private BattleUnit? NearestUnit(Vec2F from, Side side)
    {
        BattleUnit? best = null; float bd = float.MaxValue;
        foreach (var x in Units)
        {
            if (x.Side != side || x.AliveCount == 0) continue;
            float d = x.Center.DistanceTo(from);
            if (d < bd) { bd = d; best = x; }
        }
        return best;
    }

    // ====================================================================
    //  士兵级:跟队 / 分离 / 近战 / 放箭 / 奔逃
    // ====================================================================

    private void RebuildHash()
    {
        _hash.Clear();
        foreach (var u in Units)
            foreach (var s in u.Soldiers)
            {
                var key = ((int)(s.Pos.X / HashCell), (int)(s.Pos.Y / HashCell));
                if (!_hash.TryGetValue(key, out var list)) _hash[key] = list = new List<Soldier>();
                list.Add(s);
            }
    }

    private void ForNeighbors(Vec2F p, float radius, Action<Soldier> act)
    {
        int cx = (int)(p.X / HashCell), cy = (int)(p.Y / HashCell);
        int r = (int)(radius / HashCell) + 1;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (_hash.TryGetValue((cx + dx, cy + dy), out var list))
                    foreach (var s in list) act(s);
    }

    private void UpdateSoldiers()
    {
        foreach (var u in Units)
        {
            if (u.AliveCount == 0) continue;
            bool routing = u.State is BUnitState.Routing or BUnitState.Shattered;
            float stamF = 0.8f + 0.2f * u.Stamina / 100f;
            float reach = BArms.ReachOf(u.Type);
            float dmgStam = 0.75f + 0.25f * u.Stamina / 100f;
            // 接触区:敌部 45m 内时,士兵获得「扑向近敌」的自主性(阵线由此真实咬合)
            var nearFoeUnit = NearestUnit(u.Center, u.Side == Side.Friend ? Side.Enemy : Side.Friend);
            bool contactZone = nearFoeUnit != null && nearFoeUnit.Center.DistanceTo(u.Center) < 45f;
            float seekR = contactZone ? 8f : reach + 0.6f;

            for (int i = 0; i < u.Soldiers.Count; i++)
            {
                var s = u.Soldiers[i];
                s.CoolT -= Dt;
                s.Fighting = false;

                if (routing)
                {
                    float edgeX = u.Side == Side.Friend ? 4f : Map.WorldW - 4f;
                    var dir = (new Vec2F(edgeX, s.Pos.Y) - s.Pos).Normalized;
                    s.Pos += dir * BArms.SpeedOf(u.Type) * 1.5f * BattleMap.SpeedMult(Map.At(s.Pos)) * Dt;
                    continue;                                          // 溃兵只顾逃命(仍可被截杀)
                }

                // 近战:先在「可扑」半径内寻敌——可及则挥刀,稍远则直接扑上去(阵线咬合)
                Soldier? target = null; float bd = seekR;
                ForNeighbors(s.Pos, seekR, o =>
                {
                    if (o.UnitId == s.UnitId) return;
                    var ou = _byId[o.UnitId];
                    if (ou.Side == u.Side) return;
                    float d = o.Pos.DistanceTo(s.Pos);
                    if (d < bd) { bd = d; target = o; }
                });

                if (target is { } foe)
                {
                    if (bd <= reach)
                    {
                        s.Fighting = true;
                        if (s.CoolT <= 0)
                        {
                            s.CoolT = 1.4f + (float)_rng.NextDouble() * 0.5f;
                            var fu = _byId[foe.UnitId];
                            float charge = u.ChargeT > 0 && BArms.IsCav(u.Type) ? 1.7f : 1f;
                            float dmg = BArms.MeleeBase * (float)Unit.TypeMatchup(u.Type, fu.Type) / BArms.ArmorOf(fu.Type)
                                      * charge * dmgStam * (0.75f + (float)_rng.NextDouble() * 0.5f);
                            foe.Hp -= dmg;
                            if (foe.Hp <= 0) { u.Kills++; fu.RecentLoss += 1f; }
                        }
                    }
                    else
                        s.Pos += (foe.Pos - s.Pos).Normalized
                               * BArms.SpeedOf(u.Type) * 1.4f * BattleMap.SpeedMult(Map.At(s.Pos)) * stamF * Dt;
                    continue;
                }

                // 远程:有齐射目标且不在近战中 → 装填、放箭
                if (BArms.Ranged(u.Type) && u.VolleyTargetId >= 0 && s.Ammo > 0)
                {
                    s.ReloadT -= Dt;
                    if (s.ReloadT <= 0 && ById(u.VolleyTargetId) is { AliveCount: > 0 } tu)
                    {
                        s.ReloadT = RandReload(u.Type);
                        s.Ammo--;
                        var victim = tu.Soldiers[_rng.Next(tu.Soldiers.Count)];
                        float dist = victim.Pos.DistanceTo(s.Pos);
                        float scatter = 2.5f + dist * 0.05f;
                        var aim = victim.Pos + new Vec2F(((float)_rng.NextDouble() * 2 - 1) * scatter,
                                                         ((float)_rng.NextDouble() * 2 - 1) * scatter);
                        Arrows.Add(new Arrow
                        {
                            Pos = s.Pos, Origin = s.Pos, Vel = (aim - s.Pos).Normalized * 38f,
                            TravelLeft = aim.DistanceTo(s.Pos),
                            RawDamage = BArms.RangedDamage(u.Type) * (0.8f + (float)_rng.NextDouble() * 0.4f),
                            Shooter = u.Type, Side = u.Side
                        });
                    }
                }

                // 跟队:向自己的阵位靠拢
                var slot = u.SlotWorld(i);
                float sd = s.Pos.DistanceTo(slot);
                if (sd > 0.25f)
                {
                    float speed = BArms.SpeedOf(u.Type) * BattleMap.SpeedMult(Map.At(s.Pos))
                                * ((u.Running || sd > 8f) ? 1.5f : 1f) * stamF;
                    s.Pos += (slot - s.Pos).Normalized * MathF.Min(speed * Dt, sd);
                }

                // 拥挤分离(轻推)
                var push = new Vec2F(0, 0);
                ForNeighbors(s.Pos, 0.8f, o =>
                {
                    if (o == s) return;
                    var d = s.Pos - o.Pos;
                    float l = d.Length;
                    if (l < 0.8f && l > 0.001f) push += d * (1f / l) * (0.8f - l);
                });
                s.Pos += push * (2.2f * Dt);
            }
        }
    }

    private void UpdateArrows()
    {
        for (int i = Arrows.Count - 1; i >= 0; i--)
        {
            var a = Arrows[i];
            a.Pos += a.Vel * Dt;
            a.TravelLeft -= 38f * Dt;
            if (a.TravelLeft > 0) continue;

            // 落点结算:近旁任何士兵(含友军——流矢不认人)
            Soldier? hit = null; float bd = 1.1f;
            ForNeighbors(a.Pos, 1.1f, o =>
            {
                float d = o.Pos.DistanceTo(a.Pos);
                if (d < bd) { bd = d; hit = o; }
            });
            if (hit is { } victim)
            {
                var vu = _byId[victim.UnitId];
                float dmg = a.RawDamage * (float)Unit.TypeMatchup(a.Shooter, vu.Type) / BArms.ArmorOf(vu.Type);
                victim.Hp -= dmg;
                if (vu.Side != a.Side) { vu.LastThreatPos = a.Origin; vu.LastThreatT = Time; }   // 挨箭知来向
                if (victim.Hp <= 0)
                {
                    vu.RecentLoss += 1f;
                    if (vu.Side != a.Side)
                        foreach (var su in Units) { if (su.Side == a.Side && su.Type == a.Shooter) { su.Kills++; break; } }
                }
            }
            Arrows.RemoveAt(i);
        }
    }

    private void RemoveDead()
    {
        foreach (var u in Units)
        {
            for (int i = u.Soldiers.Count - 1; i >= 0; i--)
            {
                var s = u.Soldiers[i];
                bool fledOff = s.Pos.X < 6f || s.Pos.X > Map.WorldW - 6f;
                if (s.Hp <= 0) u.Soldiers.RemoveAt(i);
                else if (fledOff && u.State is BUnitState.Routing or BUnitState.Shattered)
                { u.Fled++; u.Soldiers.RemoveAt(i); }
            }
        }
    }

    private void CheckBattleEnd()
    {
        _endClock += Dt;
        if (_endClock < 1.5f) return;
        _endClock = 0;
        if (!Units.Any(u => u.Side == Side.Friend) || !Units.Any(u => u.Side == Side.Enemy)) return;   // 没有两军就没有胜负
        bool friendStands = Units.Any(u => u.Side == Side.Friend && u.Controllable);
        bool enemyStands = Units.Any(u => u.Side == Side.Enemy && u.Controllable);
        if (friendStands && enemyStands) return;
        Over = true;
        Winner = friendStands ? Side.Friend : enemyStands ? Side.Enemy : null;
        Alerts.Add(new Alert((int)Time, Winner == Side.Friend ? "虏骑溃矣!战场是我们的。"
                                       : Winner == Side.Enemy ? "全军溃散……败局已定。" : "两败俱伤,战场沉寂。", true));
    }

    // ====================================================================
    //  小工具
    // ====================================================================

    public void Feed(string text) => Sandbox.Feed.Add($"[{FormatT(Time)}] {text}");
    public static string FormatT(float t) => $"{(int)(t / 60):00}:{(int)(t % 60):00}";

    public static string ArmCn(UnitType t) => t switch
    {
        UnitType.Spear => "枪", UnitType.Bow => "弩", UnitType.Cavalry => "轻骑", UnitType.Shield => "盾",
        UnitType.MoDao => "陌刀", UnitType.Cataphract => "具装", UnitType.HorseArcher => "骑射",
        UnitType.NomadLancer => "突骑", UnitType.TribalFoot => "部众", _ => "?"
    };
}
