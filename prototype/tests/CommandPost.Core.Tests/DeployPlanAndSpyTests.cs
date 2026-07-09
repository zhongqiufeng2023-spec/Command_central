using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>布阵预令(战前面授机宜,开战即动)与细作(敌营间谍密报)。</summary>
public class DeployPlanAndSpyTests
{
    private static BattleSim Sim()
        => new(new BattleMap(50, 12), new Rng(23)) { HqPos = new Vec2F(40, 96) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    // ================= 布阵预令 =================

    [Fact]
    public void PlannedMove_ExecutesAtDrumbeat_NoRiderNoTwist()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("秦锐", Personality.Aggressive, 0.4), new Vec2F(80, 96), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(700, 96), 30);
        sim.BeginDeploy(300f);

        var dest = new Vec2F(300, 60);
        Assert.True(sim.DeployPlanMove(u.Id, dest, run: false));
        Assert.NotNull(u.PlannedDest);
        Assert.Empty(sim.Riders);                                      // 面授机宜,不费令骑

        sim.FinishDeploy();
        Assert.True(u.Path.Count > 0, "擂鼓即动");
        Assert.True(u.Path[^1].DistanceTo(dest) < 20f, $"贪功武将也照预令走(终点 {u.Path[^1]})");   // 当面吩咐不走样
        Assert.Contains(sim.Sandbox.Feed, s => s.Contains("依预令而动"));

        Run(sim, 10f);
        Assert.True(u.Center.DistanceTo(new Vec2F(80, 96)) > 15f, "开战后确实开拔了");
    }

    [Fact]
    public void PlannedMove_OnlyDuringDeploy_OnlyOwnUnits()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("甲", Personality.Steady), new Vec2F(80, 96), 30);
        var ally = sim.AddUnit(Side.Friend, UnitType.Spear, "李嵩枪队", new Commander("李嵩", Personality.Steady), new Vec2F(120, 40), 30, allied: true);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(700, 96), 30);

        Assert.False(sim.DeployPlanMove(u.Id, new Vec2F(300, 60), false));   // 未布阵:授不了
        sim.BeginDeploy(300f);
        Assert.False(sim.DeployPlanMove(ally.Id, new Vec2F(300, 60), false)); // 友邻不归你辖
    }

    // ================= 细作 =================

    [Fact]
    public void Spy_PlantsIntoLargestEnemy_ReportsExactNumbers()
    {
        var sim = Sim();
        sim.SpiesAvailable = 1;
        sim.SpyCatchChance = 0f;                                       // 验证递书管线:不掷暴露
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(80, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "小部", new Commander("乙", Personality.Steady), new Vec2F(600, 60), 60);
        var big = sim.AddUnit(Side.Enemy, UnitType.NomadLancer, "大部", new Commander("丙", Personality.Steady), new Vec2F(650, 120), 150);

        sim.BeginDeploy(300f);
        Assert.True(sim.PlantSpy());
        Assert.Equal(0, sim.SpiesAvailable);
        Assert.False(sim.PlantSpy());                                  // 一战一名
        Assert.Equal(big.Id, sim.Spies[0].UnitId);                     // 混进最大一部

        sim.FinishDeploy();
        Run(sim, 150f);                                                // 首报 60~120s
        Assert.True(sim.Spies[0].Reports > 0, "细作该递出过密报");
        Assert.Contains(sim.Alerts, a => a.Text.Contains("细作密报") && a.Pause);
        Assert.True(sim.Sandbox.Enemy.ContainsKey(big.Id));
        Assert.Equal(big.AliveCount, sim.Sandbox.Enemy[big.Id].Est);   // 身在营中,数的是确数
    }

    [Fact]
    public void Spy_Caught_GoesSilent_RevealedPostBattle()
    {
        var sim = Sim();
        sim.SpiesAvailable = 1;
        sim.SpyCatchChance = 1f;                                       // 必被抓:验证纯沉默
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(80, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(650, 120), 100);

        sim.BeginDeploy(300f);
        sim.PlantSpy();
        sim.FinishDeploy();
        Run(sim, 150f);
        Assert.True(sim.Spies[0].Burned);
        Assert.DoesNotContain(sim.Alerts, a => a.Text.Contains("细作密报"));   // 一封书信也没递出来
        Assert.Contains(sim.Reveals, s => s.Contains("事败"));                 // 复盘才知道他死了
    }

    [Fact]
    public void Spy_EscapesWhenHostUnitShatters()
    {
        var sim = Sim();
        sim.SpiesAvailable = 1;
        sim.SpyCatchChance = 0f;
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(80, 96), 40);
        var host = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(650, 120), 100);
        sim.BeginDeploy(300f);
        sim.PlantSpy();
        sim.FinishDeploy();

        Run(sim, 5f);
        host.Soldiers.Clear();                                         // 所在虏部覆没
        Run(sim, 5f);
        Assert.True(sim.Spies[0].Burned);
        Assert.Contains(sim.Alerts, a => a.Text.Contains("脱身归来"));
    }
}
