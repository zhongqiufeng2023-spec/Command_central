using System;
using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

// 令骑推进 + 沿途侦察 + 军报回帐 + 情报并入沙盘。
public sealed partial class BattleSim
{
    private void UpdateRiders()
    {
        foreach (var r in Riders)
        {
            if (r.Delivered || r.Lost)
            {
                // 截杀 = 纯沉默:超时才给「未归」提示(硬核档连提示也没有——你自己记着谁没回来)
                if (r.Lost && !r.OverdueAlerted && Time > r.ExpectedBack)
                {
                    r.OverdueAlerted = true;
                    if (Difficulty.OverdueHint) Alerts.Add(new Alert((int)Time, $"{r.DescCn} 迟迟未归……", true));
                }
                continue;
            }

            RecordRiderSightings(r);

            // 截杀风险:身边 40m 有敌部(难度调倍率)
            foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AliveCount > 0))
                if (e.Center.DistanceTo(r.Pos) < 40f && _rng.Chance(0.015 * Dt * Difficulty.InterceptMult)) { r.Lost = true; break; }
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
                if (r is { Kind: RiderKind.Report, ToHq: true })
                { r.Delivered = true; DeliverHqReport(r); continue; }
                switch (r.Kind)
                {
                    case RiderKind.Order when ById(r.TargetUnitId) is { } u && u.AliveCount > 0:
                        ApplyOrderWithTemperament(u, r);       // 武将按脾性解读(第二层迷雾)
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
        // 友邻(左翼)也看在眼里:塘骑路过就能带回李嵩部的战况
        foreach (var a in Units.Where(x => x.Allied && x.AliveCount > 0))
        {
            float dist = a.Center.DistanceTo(r.Pos);
            if (dist > (r.Kind == RiderKind.Scout ? 130f : 100f) * BattleMap.ConcealMult(Map.At(a.Center))) continue;
            int est = Math.Max(10, (int)Math.Round(a.AliveCount / 10f) * 10);
            if (!r.AllySightings.TryGetValue(a.Id, out var old) || Time > old.T)
                r.AllySightings[a.Id] = new AllySighting(a.Center, est, AllyStateCn(a), Time);
        }
    }

    /// <summary>旁观者眼里的友邻战况判语(不是精确内情,是一眼印象)。</summary>
    private static string AllyStateCn(BattleUnit a) => a.State switch
    {
        BUnitState.Engaged => "酣战",
        BUnitState.Wavering => "势危",
        BUnitState.Routing => "将崩",
        BUnitState.Shattered or BUnitState.Destroyed => "已溃",
        _ => "守御"
    };

    private OwnStatus Snapshot(BattleUnit u) =>
        new(u.Id, u.Center, u.AliveCount,
            $"{u.StanceCn}·{u.StateCn}{(u.LastQuirkCn != "" ? $"〔{u.LastQuirkCn}〕" : "")}", Time);

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
        foreach (var (aid, s) in r.AllySightings)
        {
            bool isNew = !Sandbox.Ally.TryGetValue(aid, out var am);
            if (isNew) am = Sandbox.Ally[aid] = new SandboxAllyMark { UnitId = aid };
            if (s.T >= am!.T)
            { am.Pos = s.Pos; am.Est = s.Est; am.StateCn = s.StateCn; am.T = s.T; am.SourceCn = r.DescCn; }
            var au = ById(aid);
            Feed($"左翼所见:{au?.Officer.Name}部约{s.Est}人,{s.StateCn}");
        }
        if (r.ArriveAlertCn is { } cry)
            Alerts.Add(new Alert((int)Time, cry, true));
        if (r.Kind == RiderKind.Scout && r.Sightings.Count == 0 && r.AllySightings.Count == 0)
            Feed("塘骑归:所探之处未见敌踪");
    }

    // —— 军报(部队自发:定期 + 事件)——
    private void AutoReports()
    {
        foreach (var u in Units.Where(x => x.Side == Side.Friend && !x.Allied && x.AliveCount > 0))
        {
            u.ReportClock -= Dt;
            u.EventCooldown -= Dt;

            bool evt = false;
            if (u.State == BUnitState.Engaged && !u.ReportedEngaged) { u.ReportedEngaged = true; evt = true; }
            if (u.State != BUnitState.Engaged) u.ReportedEngaged = false;
            if (u.State is BUnitState.Routing && !u.ReportedRouting) { u.ReportedRouting = true; evt = true; }

            if (u.ReportClock <= 0 || (evt && u.EventCooldown <= 0))
            {
                u.ReportClock = Difficulty.AutoReportPeriod;
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
                // 在左翼战地的本部,军报顺带捎回李嵩部战况
                foreach (var a in Units.Where(x => x.Allied && x.AliveCount > 0))
                {
                    float dist = a.Center.DistanceTo(u.Center);
                    if (dist > 95f * BattleMap.ConcealMult(Map.At(a.Center))) continue;
                    r.AllySightings[a.Id] = new AllySighting(a.Center, Math.Max(10, (int)Math.Round(a.AliveCount / 10f) * 10), AllyStateCn(a), Time);
                }
                Riders.Add(r);
            }
        }
    }
}
