using System;
using System.Linq;

namespace CommandPost.Core;

// 部队级更新:行军 / 接战判定 / 士气 / 溃逃与收拢 / 齐射选敌。
public sealed partial class BattleSim
{
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
            u.EngagedT = engaged ? u.EngagedT + Dt : 0f;
            u.DisengageT = MathF.Max(0, u.DisengageT - Dt);

            // 行军(接战则钉住,由士兵层厮杀;穿插中的骑队例外——凿穿阵背而出)
            if ((!engaged || u.DisengageT > 0) && u.Path.Count > 0)
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
            else
            {
                u.SpeedNow = 0;
                if (Time - u.LastThreatT < 6f)                                   // 静立受矢:转身迎盾
                    u.Facing = (u.LastThreatPos - u.Center).Normalized;
            }

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
}
