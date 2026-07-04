using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>战斗B档(逐兵):建制、箭矢、近战、溃逃、令骑送令、军报入沙盘。</summary>
public class BattleSimTests
{
    private static BattleSim Sim(int w = 30, int h = 10)
        => new(new BattleMap(w, h), new Rng(7)) { HqPos = new Vec2F(24, 80) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    [Fact]
    public void AddUnit_SpawnsEverySoldier_AsIndividual()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("甲", Personality.Steady), new Vec2F(100, 80), 120);
        Assert.Equal(120, u.AliveCount);                              // 每兵一单位
        Assert.Equal(120, u.Soldiers.Select(s => s.Id).Distinct().Count());
        Assert.All(u.Soldiers, s => Assert.True(s.Hp > 0));           // 各有血量
    }

    [Fact]
    public void Crossbows_LooseRealArrows_ThatKill()
    {
        var sim = Sim();
        var bow = sim.AddUnit(Side.Friend, UnitType.Bow, "弩队", new Commander("甲", Personality.Steady), new Vec2F(60, 80), 40);
        var foe = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(190, 80), 40);
        Run(sim, 3f);
        Assert.NotEmpty(sim.Arrows);                                  // 真的有箭在飞
        Run(sim, 30f);
        Assert.True(foe.AliveCount < 40, $"敌应有中箭者,现存 {foe.AliveCount}");
        Assert.True(bow.Soldiers.Any(s => s.Ammo < 20), "有人放过箭(耗了矢)");
    }

    [Fact]
    public void Melee_TwoUnitsClash_BothBleed()
    {
        var sim = Sim();
        var a = sim.AddUnit(Side.Friend, UnitType.Spear, "枪", new Commander("甲", Personality.Steady), new Vec2F(80, 80), 60);
        var b = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "众", new Commander("乙", Personality.Steady), new Vec2F(150, 80), 60);
        a.SetOrderDirect(new Vec2F(150, 80), run: true, sim.Map);
        b.SetOrderDirect(new Vec2F(80, 80), run: true, sim.Map);
        Run(sim, 60f);
        Assert.True(a.AliveCount < 60 && b.AliveCount < 60, $"交锋应见伤亡:{a.AliveCount} vs {b.AliveCount}");
        Assert.True(a.Kills > 0 || b.Kills > 0);
    }

    [Fact]
    public void Outnumbered_Unit_BreaksAndRouts()
    {
        var sim = Sim();
        var weak = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "孤众", new Commander("乙", Personality.Steady, 0.4), new Vec2F(160, 80), 40);
        var s1 = sim.AddUnit(Side.Friend, UnitType.Spear, "枪一", new Commander("甲", Personality.Steady), new Vec2F(90, 60), 200);
        var s2 = sim.AddUnit(Side.Friend, UnitType.MoDao, "刀二", new Commander("丙", Personality.Steady), new Vec2F(90, 100), 200);
        s1.SetOrderDirect(new Vec2F(160, 78), true, sim.Map);
        s2.SetOrderDirect(new Vec2F(160, 82), true, sim.Map);
        Run(sim, 90f);
        bool broken = weak.State is BUnitState.Routing or BUnitState.Shattered or BUnitState.Destroyed
                      || weak.LossFrac > 0.5f;
        Assert.True(broken, $"寡不敌众应崩:状态{weak.State} 存{weak.AliveCount}/40 士气{weak.Morale:0}");
    }

    [Fact]
    public void OrderRider_PhysicallyDelivers_ThenUnitMoves()
    {
        var sim = Sim(40, 10);
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(400, 80), 50);
        var start = u.Center;

        sim.IssueMove(u.Id, new Vec2F(200, 80), run: false);
        Assert.NotEmpty(sim.Riders);                                   // 令骑真实出发
        Run(sim, 5f);
        Assert.Empty(u.Path);                                          // 令未到,部队未动

        Run(sim, 90f);                                                 // 令骑 ~380m 路程
        Assert.True(u.Center.DistanceTo(start) > 30f, $"送达后应开拔,仅移 {u.Center.DistanceTo(start):0}m");
    }

    [Fact]
    public void AttackStance_ChargesNearbyEnemy_WithoutPlayerOrder()
    {
        var sim = Sim();
        var a = sim.AddUnit(Side.Friend, UnitType.Spear, "枪", new Commander("甲", Personality.Steady), new Vec2F(80, 80), 60);
        a.Stance = BStance.Attack;                                     // 姿态=进攻:视界内自主接敌
        var e = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "众", new Commander("乙", Personality.Steady), new Vec2F(135, 80), 60);
        Run(sim, 45f);
        Assert.True(a.Kills > 0 || e.AliveCount < 60, $"进攻姿态应自主接敌:斩获{a.Kills} 敌存{e.AliveCount}");
    }

    [Fact]
    public void HoldStance_StaysPut_UnderNoContact()
    {
        var sim = Sim();
        var a = sim.AddUnit(Side.Friend, UnitType.Shield, "盾", new Commander("甲", Personality.Steady), new Vec2F(80, 80), 60);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "众", new Commander("乙", Personality.Steady), new Vec2F(400, 80), 60);
        Run(sim, 20f);                                                 // 默认据守:远敌不动如山
        Assert.True(a.Center.DistanceTo(new Vec2F(80, 80)) < 10f);
    }

    [Fact]
    public void SkirmishStance_KitesAndKeepsShooting()
    {
        var sim = Sim(40, 10);
        var bow = sim.AddUnit(Side.Friend, UnitType.Bow, "弩", new Commander("甲", Personality.Steady), new Vec2F(300, 80), 40);
        bow.Stance = BStance.Skirmish;                                 // 游走:风筝
        var foe = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "众", new Commander("乙", Personality.Steady), new Vec2F(380, 80), 60);
        foe.SetOrderDirect(new Vec2F(60, 80), run: true, sim.Map);     // 直扑弩队

        float minDist = float.MaxValue;
        for (int i = 0; i < 600 && !sim.Over; i++)
        {
            sim.Tick();
            minDist = System.MathF.Min(minDist, bow.Center.DistanceTo(foe.Center));
        }
        Assert.True(minDist > 25f, $"游走应保持距离,最近却到 {minDist:0}m");
        Assert.True(foe.AliveCount < 60, "游走途中应持续放箭杀伤");
    }

    [Fact]
    public void ShieldWall_FacingArchers_BlocksMostArrowDamage()
    {
        var sim = Sim();
        var sh = sim.AddUnit(Side.Enemy, UnitType.Shield, "盾", new Commander("乙", Personality.Steady), new Vec2F(150, 80), 10);
        var arrow = new Arrow { Origin = new Vec2F(60, 80), RawDamage = 50f, Shooter = UnitType.Bow, Side = Side.Friend };

        sh.Facing = new Vec2F(-1, 0);                                  // 盾墙迎着弓手
        float front = BattleSim.ArrowDamage(arrow, sh);
        sh.Facing = new Vec2F(1, 0);                                   // 背对
        float back = BattleSim.ArrowDamage(arrow, sh);
        Assert.True(front < back * 0.5f, $"迎盾应大幅减伤:迎面{front:0.0} vs 背对{back:0.0}");
    }

    [Fact]
    public void AutoReport_UpdatesSandbox_WithAgedInfo()
    {
        var sim = Sim(40, 10);
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("甲", Personality.Steady), new Vec2F(300, 80), 60);
        Assert.Equal(0f, sim.Sandbox.Own[u.Id].T);                     // 开局基线
        Run(sim, 90f);                                                 // 首报 ~20-40s 发出 + 骑行送达
        Assert.True(sim.Sandbox.Own[u.Id].T > 0f, "定期军报应已更新沙盘");
        Assert.True(sim.Sandbox.Feed.Count > 0);
    }
}
