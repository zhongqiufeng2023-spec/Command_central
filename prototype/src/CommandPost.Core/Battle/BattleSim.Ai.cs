using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

// AI 与自主行为层:敌方对称迷雾 + 敌方 AI + 我方姿态自主 + 风筝步。
public sealed partial class BattleSim
{
    // —— 敌方对称迷雾:发现你 → 隔 EnemyReactLag 全军才知晓,存的是「最后所见」快照(非实时制导),
    //    失接触 EnemyForget 后遗忘。敌方和你一样吃情报滞后,不再「你一露头全军神反应」。——
    private void UpdateEnemyKnowledge()
    {
        _spotClock += Dt;
        if (_spotClock < 1f) return;
        _spotClock = 0;

        foreach (var f in Units.Where(x => x.Side == Side.Friend && x.AliveCount > 0))
        {
            float conceal = BattleMap.ConcealMult(Map.At(f.Center));
            bool seen = Units.Any(e => e.Side == Side.Enemy && e.AliveCount > 0
                                    && e.Center.DistanceTo(f.Center) <= 110f * conceal);
            if (!seen) continue;

            if (_enemyKnown.TryGetValue(f.Id, out var k))
            {
                if (Time - k.seenT >= EnemyRefresh) _enemyKnown[f.Id] = (f.Center, Time);   // 半实时刷新快照
            }
            else if (!_enemyPending.ContainsKey(f.Id))
                _enemyPending[f.Id] = (f.Center, Time + EnemyReactLag);                     // 发现:先入延迟队列
        }

        foreach (var kv in _enemyPending.Where(kv => Time >= kv.Value.readyT).ToList())
        { _enemyKnown[kv.Key] = (kv.Value.pos, Time); _enemyPending.Remove(kv.Key); }        // 到点 → 全军知晓

        foreach (var k in _enemyKnown.Where(kv => Time - kv.Value.seenT > EnemyForget).Select(kv => kv.Key).ToList())
            _enemyKnown.Remove(k);
    }

    private void UpdateEnemyAi()
    {
        foreach (var e in Units.Where(x => x.Side == Side.Enemy && x.AiControlled && x.Controllable))
        {
            e.ThinkClock -= Dt;
            if (e.ThinkClock > 0) continue;
            e.ThinkClock = 4f;

            // 近身实时反应(免贴脸站桩);否则用「延迟共享 + 最后所见快照」情报
            var close = NearestUnit(e.Center, Side.Friend);
            Vec2F? goal = close != null && close.Center.DistanceTo(e.Center) <= EnemyCloseVision ? close.Center : null;
            if (goal is null)
            {
                float kb = float.MaxValue;
                foreach (var kv in _enemyKnown.Values)
                {
                    float d = kv.pos.DistanceTo(e.Center);
                    if (d < kb) { kb = d; goal = kv.pos; }
                }
            }

            if (goal is { } kn)
            {
                float dist = kn.DistanceTo(e.Center);
                if (BArms.Ranged(e.Type) && e.Soldiers.Any(s => s.Ammo > 0))
                    SkirmishStep(e, kn, dist);                              // 骑射风筝:射程带内游走放箭
                else if (dist > 14f)
                    e.SetOrderDirect(kn, dist < 120f && BArms.IsCav(e.Type), Map);   // 箭尽/近战:压上肉搏
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
                    // 骑兵穿插:冲锋窗口(2.5s)一过就凿穿阵背而出,拉开再回身重冲——骑兵久缠必被步阵吞掉
                    if (BArms.IsCav(u.Type) && u.State == BUnitState.Engaged && u.EngagedT > 3f && u.DisengageT <= 0f)
                    {
                        u.DisengageT = 5f;
                        u.SetOrderDirect(Map.Clamp(u.Center + u.Facing * 80f), run: true, Map);
                        break;
                    }
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
                case BStance.Skirmish:
                {
                    // 游走(风筝):远程且有矢 → 射程带内保持距离放箭
                    if (BArms.Ranged(u.Type) && u.Soldiers.Any(s => s.Ammo > 0))
                    {
                        var foe = NearestUnit(u.Center, Side.Enemy);
                        if (foe != null) SkirmishStep(u, foe.Center, foe.Center.DistanceTo(u.Center));
                        break;
                    }
                    goto case BStance.Attack;                               // 箭尽:拔刀冲上去(游骑的第二把武器)
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

    /// <summary>风筝一步:敌进我退(拉回射程带),敌远我跟(贴到射程),带内站定放箭。敌我共用。</summary>
    private void SkirmishStep(BattleUnit u, Vec2F foePos, float dist)
    {
        float range = BArms.RangeOf(u.Type);
        if (dist < range * 0.45f)
            u.SetOrderDirect(Map.Clamp(u.Center + (u.Center - foePos).Normalized * (range * 0.7f - dist + 20f)), run: true, Map);
        else if (dist > range * 0.9f)
            u.SetOrderDirect(foePos, run: BArms.IsCav(u.Type), Map);
        else if (u.Path.Count > 0) { u.Path.Clear(); u.Order = BOrderKind.Hold; }
    }
}
