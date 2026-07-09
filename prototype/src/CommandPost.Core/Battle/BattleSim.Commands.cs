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

    /// <summary>布阵·预令(面授机宜):开战擂鼓那一刻,该部即刻照此进军——
    /// 当面吩咐,不走令骑、不经解读——这是你信息最全、控制最强的唯一时刻(PRD §6.0)。</summary>
    public bool DeployPlanMove(int unitId, Vec2F dest, bool run)
    {
        if (!Deploying || ById(unitId) is not { Side: Side.Friend, Allied: false } u) return false;
        u.PlannedDest = Map.NearestPassable(Map.Clamp(dest));
        u.PlannedRun = run;
        return true;
    }

    /// <summary>战前遣细作(只在布阵时;开战后就混不进去了):扮作虏中杂胡,混入敌军最大一部。
    /// 战中他会周期递出密报(兵力确数+位置+动向);每递一次冒暴露之险——
    /// 一旦事败=纯沉默,你只会觉得他久无书信(难度可给提示)。</summary>
    public bool PlantSpy()
    {
        if (!Deploying || SpiesAvailable <= 0) return false;
        var target = Units.Where(u => u.Side == Side.Enemy && u.AliveCount > 0)
                          .OrderByDescending(u => u.AliveCount).FirstOrDefault();
        if (target == null) return false;
        SpiesAvailable--;
        Spies.Add(new BSpy { UnitId = target.Id, NextT = 60f + (float)_rng.NextDouble() * 60f });
        Feed("细作已遣:扮作虏中杂胡混入敌营——能不能递出书信,看他的造化。");
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

    /// <summary>
    /// 佯动·设疑兵(蓝图§十二,简化版):遣数十老弱赍旗鼓往某处虚张声势——
    /// 敌军望见/闻声,把它当一支真部队记进认知,循声而来却扑空。
    /// 第一次从「被迷雾困」变「用迷雾打人」。替身走得慢、路上也会被截杀;
    /// 敌迫近(50m)即识破遁散,或 90s 后自行收场。一战两拨。
    /// </summary>
    public void DispatchDecoy(Vec2F dest)
    {
        if (Deploying || Over || DecoysLeft <= 0) return;
        DecoysLeft--;
        dest = Map.NearestPassable(Map.Clamp(dest));
        var r = NewRider(RiderKind.Decoy, BattlePath.Find(Map, HqPos, dest));
        r.DestPoint = dest; r.Speed = 6.5f;                  // 老弱替身拖着大鼓,快不了
        r.DescCn = "疑兵队";
        Feed($"疑兵队出:数十老弱赍旗鼓,往 ({(int)dest.X},{(int)dest.Y}) 虚张声势(余 {DecoysLeft} 拨)");
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

    /// <summary>旗鼓声程(米):以帅帐为圆心;林中阻声打折。</summary>
    public const float SignalRange = 280f;

    /// <summary>
    /// 旗鼓通道(蓝图§九,《孙子·军争》「言不相闻,故为金鼓」):中军擂鼓/鸣金/竖旗——
    /// 近即时、带宽低(只传姿态)、**绝对照令**(耳只听金鼓,不过武将解读);
    /// 只及声程(林中打折),声程边缘会**误读**(听岔的入 Reveals,复盘才知);
    /// 且**敌军也听得见**:声程内虏骑当即知晓左近我军方位——军令泄露是旗鼓的价钱。
    /// 返回闻令部数(测试用;战中不呈现——鼓声出帐,谁听见了你看不见)。
    /// </summary>
    public int SoundSignal(BStance stance)
    {
        if (Deploying || Over) return 0;
        int heard = 0;
        foreach (var u in Units.Where(x => x is { Side: Side.Friend, Allied: false, AiControlled: false } && x.Controllable))
        {
            float r = SignalRange * (Map.At(u.Center) == BTerrain.Forest ? 0.6f : 1f);
            float d = u.Center.DistanceTo(HqPos);
            if (d > r) continue;
            heard++;
            var eff = stance;
            if (d > r * 0.75f && _rng.Chance(0.25))
            {
                BStance[] all = { BStance.Attack, BStance.Hold, BStance.Standby };
                eff = all[(int)(_rng.NextDouble() * all.Length)];
                if (eff != stance)
                    Reveals.Add($"{FormatT(Time)} {u.Name}在声程边缘听岔了:「{SignalCn(stance)}」听成「{StanceCnOf(eff)}」");
            }
            u.Stance = eff;                    // 旗鼓=绝对照令(戚继光:耳只听金鼓,眼只看旗帜)
            u.AwaitingReply = false;
            u.LastQuirkCn = "";
        }
        // 军令泄露:声程内的敌部也听见了——当即知晓其左近我军方位(免延迟)
        foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0 && x.Center.DistanceTo(HqPos) < SignalRange))
            foreach (var f in Units.Where(x => x.Side == Side.Friend && x.AliveCount > 0 && x.Center.DistanceTo(e.Center) < 160f))
                _enemyKnown[f.Id] = (f.Center, Time);
        Feed($"中军{SignalCn(stance)}——鼓角出帐,声程内各部当即照令(虏骑亦闻)");
        return heard;
    }

    public static string SignalCn(BStance s) => s switch
    { BStance.Attack => "擂鼓(进)", BStance.Standby => "鸣金(退)", _ => "竖旗(守)" };

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
