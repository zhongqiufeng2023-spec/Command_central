using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>佯动·疑兵(蓝图§十二 简化版):污染敌方认知——第一次「用迷雾打人」。</summary>
public class DecoyTests
{
    private static BattleSim Sim(int w = 40, int h = 25)
        => new(new BattleMap(w, h), new Rng(17)) { HqPos = new Vec2F(40, 60) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    [Fact]
    public void Decoy_RidesOut_PlantsAtDestination_LimitedStock()
    {
        var sim = Sim();
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(60, 60), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(620, 380), 30);

        sim.DispatchDecoy(new Vec2F(200, 120));
        Assert.Equal(1, sim.DecoysLeft);
        Assert.Contains(sim.Riders, r => r.Kind == RiderKind.Decoy);

        Run(sim, 40f);                                       // ~170m / 6.5m/s ≈ 26s
        Assert.Single(sim.Decoys);
        Assert.Contains(sim.Sandbox.Feed, s => s.Contains("疑兵已张"));

        sim.DispatchDecoy(new Vec2F(300, 120));
        sim.DispatchDecoy(new Vec2F(300, 160));              // 第三拨:没人了
        Assert.Equal(0, sim.DecoysLeft);
        Assert.Equal(1, sim.Riders.Count(r => r.Kind == RiderKind.Decoy));
    }

    [Fact]
    public void Decoy_LuresEnemyAwayFromRealTarget()
    {
        var sim = Sim();
        // 真身藏在西南远处(敌看不见);疑兵张在敌北面——敌该循鼓声北上扑空
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(50, 380), 30);
        var e = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady, 0.7), new Vec2F(300, 260), 60, ai: true);

        sim.DispatchDecoy(new Vec2F(300, 120));
        Run(sim, 100f);
        Assert.True(e.Center.Y < 215f, $"敌该被疑兵诱向北(实在 {e.Center})");
    }

    [Fact]
    public void Decoy_ExpiresAndVanishesFromEnemyMind()
    {
        var sim = Sim();
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(50, 380), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(620, 380), 30);

        sim.DispatchDecoy(new Vec2F(150, 100));
        Run(sim, 130f);                                      // 抵达 ~18s + 90s 寿命
        Assert.Empty(sim.Decoys);
        Assert.Contains(sim.Sandbox.Feed, s => s.Contains("疑兵收场") || s.Contains("疑兵遁归"));
    }
}
