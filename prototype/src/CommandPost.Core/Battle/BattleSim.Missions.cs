using System;
using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

// 任务层接入:中军令按时间线送达 / 军书上行(真实骑手驰往行营,可被截杀) /
// 行营回执(玩家「得知已收悉」的唯一途径) / 战役窗口(到时虏自遁,按目标结算)。
public sealed partial class BattleSim
{
    public BattleMission? Mission { get; set; }

    /// <summary>开战我方总兵力(伤亡比分母;Units 不移除,MaxCount 恒定)。</summary>
    public int FriendStartStrength => Units.Where(u => u.Side == Side.Friend).Sum(u => u.MaxCount);
    public int FriendAliveStrength => Units.Where(u => u.Side == Side.Friend).Sum(u => u.AliveCount);
    public float FriendLossFrac => 1f - (float)FriendAliveStrength / Math.Max(1, FriendStartStrength);

    private float _missionClock;
    private readonly List<float> _receiptDue = new();
    private const float HqExitX = 4f;             // 西缘出图点(行营在图外更西)
    private const float HqHandling = 12f;         // 行营览书、批回的工夫

    private void UpdateMission()
    {
        if (Mission is not { } m || Over) return;

        // —— 中军令到点送达:激活目标 + 自动暂停横幅 ——
        foreach (var o in m.Orders)
        {
            if (o.Delivered || Time < o.ArriveT) continue;
            o.Delivered = true;
            foreach (var id in o.Activates)
                if (m[id] is { } obj) obj.Active = true;
            Alerts.Add(new Alert((int)Time, $"中军令·{o.TitleCn}:{o.TextCn}", true));
            Feed($"中军令至:{o.TitleCn}");
        }

        // —— 行营回执:军书真送达后,回骑批回才算「玩家得知」——
        for (int i = _receiptDue.Count - 1; i >= 0; i--)
            if (Time >= _receiptDue[i])
            {
                _receiptDue.RemoveAt(i);
                foreach (var obj in m.Objectives.Where(o => o.Kind == BObjectiveKind.ReportToHq && o.Active))
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
        float len = PathLength(HqPos, r.Path);
        r.ExpectedBack = Time + (len * 2f) / r.Speed + HqHandling + 20f;
        Riders.Add(r);
        Feed($"军书发出:具报敌情 {r.Sightings.Count} 条,骑手驰往行营");
    }

    /// <summary>军书骑抵西缘出图 = 送达行营:真相侧记账,排一封回执骑返程。</summary>
    private void DeliverHqReport(Rider r)
    {
        if (Mission is not { } m) return;
        m.ReportsDelivered++;
        m.BestReportIntel = Math.Max(m.BestReportIntel, r.Sightings.Count);
        float rideBack = r.Pos.DistanceTo(HqPos) / r.Speed;
        _receiptDue.Add(Time + HqHandling + rideBack);
    }
}
