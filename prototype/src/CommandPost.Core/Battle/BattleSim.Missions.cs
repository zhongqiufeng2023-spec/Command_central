using System;
using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

// 任务层接入:中军令按时间线送达 / 军书上行(真实骑手驰往行营,可被截杀) /
// 行营回执(玩家「得知已收悉」的唯一途径) / 战役窗口(到时虏自遁,按目标结算)。
public sealed partial class BattleSim
{
    public BattleMission? Mission { get; set; }

    /// <summary>开战本路总兵力(伤亡比分母;不含友邻左翼——「保全士马」只算你直辖的)。</summary>
    public int FriendStartStrength => Units.Where(u => u.Side == Side.Friend && !u.Allied).Sum(u => u.MaxCount);
    public int FriendAliveStrength => Units.Where(u => u.Side == Side.Friend && !u.Allied).Sum(u => u.AliveCount);
    public float FriendLossFrac => 1f - (float)FriendAliveStrength / Math.Max(1, FriendStartStrength);

    private float _missionClock;
    private readonly List<(float t, bool ally)> _receiptDue = new();
    private bool _allyDistressSent;
    private const float HqExitX = 4f;             // 西缘出图点(行营在图外更西)
    private const float HqHandling = 12f;         // 行营览书、批回的工夫

    private void UpdateMission()
    {
        if (Mission is not { } m || Over) return;

        // —— 中军令到点送达:激活目标 + 自动暂停横幅 ——
        foreach (var o in m.Orders)
        {
            // 行营发令那一刻,把它「所知的左翼位置」定格——这是行营的旧认知,可能已偏
            if (o.SeedAllyPos && !o.Snapped && Time >= o.DispatchT)
            {
                var allies = Units.Where(u => u.Allied && u.AliveCount > 0).ToList();
                if (allies.Count > 0)
                {
                    var c = new Vec2F(allies.Average(a => a.Center.X), allies.Average(a => a.Center.Y));
                    o.AllySnapshot = Map.Clamp(c + new Vec2F(70f, 45f));   // 行营隔着两重雾看左翼:位置带偏
                }
                o.Snapped = true;
            }
            if (o.Delivered || Time < o.ArriveT) continue;
            o.Delivered = true;
            foreach (var id in o.Activates)
                if (m[id] is { } obj) obj.Active = true;
            if (o is { SeedAllyPos: true, Snapped: true })
            {
                Sandbox.Ally[-1] = new SandboxAllyMark
                { UnitId = -1, Pos = o.AllySnapshot, Est = 0, StateCn = "吃紧(行营所报)", T = o.DispatchT, SourceCn = "行营军令" };
            }
            Alerts.Add(new Alert((int)Time, $"中军令·{o.TitleCn}:{o.TextCn}", true));
            Feed($"中军令至:{o.TitleCn}");
        }

        // —— 左翼告急骑:李嵩一接敌就遣骑来告(真实骑行,途中可被截杀=你根本不知道左翼已经打起来了)——
        if (!_allyDistressSent && Units.Any(u => u.Allied && u.State == BUnitState.Engaged))
        {
            _allyDistressSent = true;
            var lead = Units.Where(u => u.Allied && u.AliveCount > 0)
                            .OrderByDescending(u => u.AliveCount).FirstOrDefault();
            if (lead != null)
            {
                // 从阵后上马(朝帅帐一侧先脱离接触)——不然一出阵就撞进敌刀丛里
                var spawn = Map.Clamp(lead.Center + (HqPos - lead.Center).Normalized * 30f);
                var r = new Rider
                {
                    Id = _nextRider++, Kind = RiderKind.Report, Pos = spawn,
                    Phase = RiderPhase.Return, Path = BattlePath.Find(Map, spawn, HqPos),
                    Depart = Time, RoundTrip = false, EstPath = new List<Vec2F>(),
                    DescCn = $"{lead.Officer.Name}的告急骑",
                    ArriveAlertCn = $"左翼告急!{lead.Officer.Name}遣骑驰报:虏骑大至,我部苦战,乞速援——"
                };
                foreach (var a in Units.Where(x => x.Allied && x.AliveCount > 0))
                    r.AllySightings[a.Id] = new AllySighting(a.Center, Math.Max(10, (int)Math.Round(a.AliveCount / 10f) * 10), AllyStateCn(a), Time);
                foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0 && x.Center.DistanceTo(lead.Center) < 220f))
                    r.Sightings[e.Id] = new EnemySighting(e.Center, Math.Max(10, (int)Math.Round(e.AliveCount * 1.3f / 10f) * 10), null, Time);  // 惊慌之下,人数报得虚高
                r.ExpectedBack = Time + PathLength(spawn, r.Path) / r.Speed + 15f;
                Riders.Add(r);
            }
        }

        // —— 驰援判定:两队以上本部兵马抵近左翼战地 = 与李嵩合势 ——
        if (!m.RescueArrived && m.Objectives.Any(o => o.Kind == BObjectiveKind.RelieveAlly && o.Active))
        {
            var allies = Units.Where(u => u.Allied && u.AliveCount > 0).ToList();
            if (allies.Count > 0)
            {
                var c = new Vec2F(allies.Average(a => a.Center.X), allies.Average(a => a.Center.Y));
                int near = Units.Count(u => u is { Side: Side.Friend, Allied: false } && u.Controllable && u.Center.DistanceTo(c) < 150f);
                if (near >= 2)
                {
                    m.RescueArrived = true; m.RescueT = Time;
                    Alerts.Add(new Alert((int)Time, "尔部已抵左翼战地,与李嵩合势迎击!", false));
                }
            }
        }

        // —— 行营回执:军书真送达后,回骑批回才算「玩家得知」——
        for (int i = _receiptDue.Count - 1; i >= 0; i--)
            if (Time >= _receiptDue[i].t)
            {
                bool hadAlly = _receiptDue[i].ally;
                _receiptDue.RemoveAt(i);
                foreach (var obj in m.Objectives.Where(o => o.Active &&
                             (o.Kind == BObjectiveKind.ReportToHq || (o.Kind == BObjectiveKind.ReportAlly && hadAlly))))
                    obj.State = BObjectiveState.Done;
                Alerts.Add(new Alert((int)Time, "行营回执:军书收悉,尔部相机行事。", false));
            }

        // —— 周期评估(1s;只动沙盘可知的目标)——
        _missionClock += Dt;
        if (_missionClock >= 1f) { _missionClock = 0; m.Evaluate(this); }

        // —— 战役窗口:虏骑自退,按目标结算 ——
        if (Time >= m.EndTime)
        {
            Over = true; Winner = null;
            Alerts.Add(new Alert((int)Time, "虏骑不欲久战,拔营东遁——战罢,按目标结算。", true));
            m.Finish(this);
        }
    }

    /// <summary>军书上行:骑手真实驰往行营(西缘出图)。所报 = 此刻沙盘所信;
    /// 途中可被截杀 = 纯沉默——回执迟迟不至,就该疑心书没送到,补发一封。</summary>
    public void SendHqReport()
    {
        if (Deploying) return;
        var exit = new Vec2F(HqExitX, HqPos.Y);
        var r = new Rider
        {
            Id = _nextRider++, Kind = RiderKind.Report, ToHq = true,
            Pos = HqPos, Path = BattlePath.Find(Map, HqPos, exit),
            Depart = Time, RoundTrip = false, DescCn = "赍书驰行营的骑手",
            EstPath = new List<Vec2F>()
        };
        r.Sightings = Sandbox.Enemy.ToDictionary(kv => kv.Key,
            kv => new EnemySighting(kv.Value.Pos, kv.Value.Est, kv.Value.Type, kv.Value.T));
        r.AllySightings = Sandbox.Ally.Where(kv => kv.Key >= 0).ToDictionary(kv => kv.Key,
            kv => new AllySighting(kv.Value.Pos, kv.Value.Est, kv.Value.StateCn, kv.Value.T));
        float len = PathLength(HqPos, r.Path);
        r.ExpectedBack = Time + (len * 2f) / r.Speed + HqHandling + 20f;
        Riders.Add(r);
        Feed($"军书发出:具报敌情 {r.Sightings.Count} 条{(r.AllySightings.Count > 0 ? "、左翼战况" : "")},骑手驰往行营");
    }

    /// <summary>军书骑抵西缘出图 = 送达行营:真相侧记账,排一封回执骑返程。</summary>
    private void DeliverHqReport(Rider r)
    {
        if (Mission is not { } m) return;
        m.ReportsDelivered++;
        m.BestReportIntel = Math.Max(m.BestReportIntel, r.Sightings.Count);
        bool hasAlly = r.AllySightings.Count > 0;
        if (hasAlly) m.AllyReportsDelivered++;
        float rideBack = r.Pos.DistanceTo(HqPos) / r.Speed;
        _receiptDue.Add((Time + HqHandling + rideBack, hasAlly));
    }
}
