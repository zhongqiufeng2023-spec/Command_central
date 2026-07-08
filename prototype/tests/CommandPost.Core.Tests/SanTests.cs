using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>SAN→失真试水(蓝图§4.5):心态崩了,你眼里的战场就更假——
/// 回报误差放大 + 沙盘长出幻影敌情(真相无此人;复盘才揭示)。</summary>
public class SanTests
{
    private static BattleSim Sim()
        => new(new BattleMap(40, 12), new Rng(21)) { HqPos = new Vec2F(40, 96) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    [Fact]
    public void LowSan_GrowsPhantomEnemies_RevealedPostBattle()
    {
        var sim = Sim();
        sim.SanFactor = 1.8f;                                       // 心神耗尽
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(80, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(600, 40), 40);

        Run(sim, 300f);
        Assert.Contains(sim.Sandbox.Enemy.Keys, k => k < 0);        // 幻影敌情(负 id,真相无此部)
        Assert.Contains(sim.Alerts, a => a.Text.Contains("惊报"));
        Assert.Contains(sim.Reveals, s => s.Contains("从未存在"));   // 复盘才拆穿
    }

    [Fact]
    public void ClearMind_NoPhantoms()
    {
        var sim = Sim();                                            // SanFactor = 1(清明)
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(80, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(600, 40), 40);

        Run(sim, 300f);
        Assert.DoesNotContain(sim.Sandbox.Enemy.Keys, k => k < 0);
    }

    [Fact]
    public void Phantoms_DoNotCountToward_ScoutObjective()
    {
        var sim = Sim();
        sim.Mission = new BattleMission
        {
            Title = "试验", EndTime = 999f,
            Objectives = { new BObjective { Id = "A", Cn = "侦明", Kind = BObjectiveKind.ScoutEnemy, Active = true, RequiredCount = 1 } }
        };
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(80, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(600, 40), 40);
        sim.Sandbox.Enemy[-950] = new SandboxEnemyMark { UnitId = -950, Pos = new Vec2F(300, 60), Est = 200, T = 0 };

        Run(sim, 3f);
        Assert.Equal(BObjectiveState.Pending, sim.Mission["A"]!.State);   // 幻影不算侦明
    }
}
