using System;

namespace CommandPost.Core;

// 士兵级更新:跟队 / 分离 / 近战 / 放箭 / 奔逃 / 安全位移,以及箭矢飞行与命中结算。
public sealed partial class BattleSim
{
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
            if (u.DisengageT > 0) seekR = reach;                        // 穿插:边走边砍可及之敌,绝不停下追人

            for (int i = 0; i < u.Soldiers.Count; i++)
            {
                var s = u.Soldiers[i];
                s.CoolT -= Dt;
                s.Fighting = false;

                if (routing)
                {
                    float edgeX = u.Side == Side.Friend ? 4f : Map.WorldW - 4f;
                    MoveSoldierTo(s, new Vec2F(edgeX, s.Pos.Y), BArms.SpeedOf(u.Type) * 1.5f);
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
                                      * charge * dmgStam * (0.75f + (float)_rng.NextDouble() * 0.5f)
                                      * BattleMap.DefenseMult(Map.At(foe.Pos));   // 丘/林:守方减伤
                            foe.Hp -= dmg;
                            if (foe.Hp <= 0) { u.Kills++; fu.RecentLoss += 1f; }
                        }
                    }
                    else
                        MoveSoldierTo(s, foe.Pos, BArms.SpeedOf(u.Type) * 1.4f * stamF);
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
                        float scatter = (2.5f + dist * 0.05f) * (BArms.IsCav(u.Type) ? 1.35f : 1f);   // 马上放箭散
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
                    MoveSoldierTo(s, slot, BArms.SpeedOf(u.Type) * ((u.Running || sd > 8f) ? 1.5f : 1f) * stamF);

                // 拥挤分离(轻推;不许把人挤进河里)
                var push = new Vec2F(0, 0);
                ForNeighbors(s.Pos, 0.8f, o =>
                {
                    if (o == s) return;
                    var d = s.Pos - o.Pos;
                    float l = d.Length;
                    if (l < 0.8f && l > 0.001f) push += d * (1f / l) * (0.8f - l);
                });
                var pushed = s.Pos + push * (2.2f * Dt);
                if (BattleMap.Passable(Map.At(pushed))) s.Pos = pushed;
            }
        }
    }

    /// <summary>士兵安全位移:绝不踏入不可通行地形(河)。直走被挡就沿岸滑动;
    /// 已陷河中(异常兜底)则径直自救上岸——根治「士兵冻死在河里、敌军围着打不完」。</summary>
    private void MoveSoldierTo(Soldier s, Vec2F target, float speedBase)
    {
        if (!BattleMap.Passable(Map.At(s.Pos)))
        {
            var shore = Map.NearestPassable(s.Pos);                     // 自救:河中不吃地形减速
            var d0 = shore - s.Pos;
            float l0 = d0.Length;
            if (l0 > 0.01f) s.Pos += d0 * (MathF.Min(speedBase * Dt, l0) / l0);
            return;
        }

        var d = target - s.Pos;
        float dist = d.Length;
        if (dist < 0.01f) return;
        float step = MathF.Min(speedBase * BattleMap.SpeedMult(Map.At(s.Pos)) * Dt, dist);
        var next = s.Pos + d * (step / dist);
        if (BattleMap.Passable(Map.At(next))) { s.Pos = next; return; }

        var slideX = new Vec2F(next.X, s.Pos.Y);                        // 沿岸滑动(保留一个轴的进度)
        if (BattleMap.Passable(Map.At(slideX))) { s.Pos = slideX; return; }
        var slideY = new Vec2F(s.Pos.X, next.Y);
        if (BattleMap.Passable(Map.At(slideY))) s.Pos = slideY;
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
                float dmg = ArrowDamage(a, vu, Map);
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

    /// <summary>一支箭对某部士兵的实际伤害:克制 × 护甲 × 盾墙迎箭 × 地形减伤(丘/林)。</summary>
    public static float ArrowDamage(Arrow a, BattleUnit victim, BattleMap map)
    {
        float block = 1f;
        if (victim.Type == UnitType.Shield && victim.Side != a.Side)
        {
            var toOrigin = (a.Origin - victim.Center).Normalized;
            if (toOrigin.Dot(victim.Facing) > 0.25f) block = 0.35f;   // 盾墙正对箭雨:挡下大半
        }
        return a.RawDamage * (float)Unit.TypeMatchup(a.Shooter, victim.Type) / BArms.ArmorOf(victim.Type)
             * block * BattleMap.DefenseMult(map.At(a.Pos));
    }
}
