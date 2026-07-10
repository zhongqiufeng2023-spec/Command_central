using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>沙盒兵力延续:伤亡跨战带入(损耗的部队开战就是缺员的)。</summary>
public class PersistenceTests
{
    [Fact]
    public void Scenario_AppliesCarriedStrength()
    {
        var counts = new[] { 120, 220, 50, 200, 16, 180 };
        var sim = BattleScenario.BlackPineField(deploy: false, ownCounts: counts);
        var own = sim.Units.Where(u => u is { Side: Side.Friend, Allied: false }).ToList();

        Assert.Equal(120, own.First(u => u.Name == "枪队").AliveCount);   // 带伤上阵
        Assert.Equal(50, own.First(u => u.Name == "盾队").AliveCount);
        Assert.Equal(16, own.First(u => u.Name == "游骑").AliveCount);
        Assert.Equal(220, own.First(u => u.Name == "陌刀队").AliveCount); // 未损的照旧
    }

    [Fact]
    public void Scenario_SkipsGuttedUnits_ClampsOverfull()
    {
        var counts = new[] { 240, 8, 250, 200, 160, 9999 };
        var sim = BattleScenario.BlackPineField(deploy: false, ownCounts: counts);
        var own = sim.Units.Where(u => u is { Side: Side.Friend, Allied: false }).ToList();

        Assert.DoesNotContain(own, u => u.Name == "陌刀队");              // 残不成队,暂缺编
        Assert.Equal(5, own.Count);
        Assert.Equal(180, own.First(u => u.Name == "铁骑").AliveCount);   // 不许超编
    }

    [Fact]
    public void Scenario_DefaultIsFullStrength()
    {
        var sim = BattleScenario.BlackPineField(deploy: false);
        int total = sim.Units.Where(u => u is { Side: Side.Friend, Allied: false }).Sum(u => u.AliveCount);
        Assert.Equal(BattleScenario.OwnFullStrength.Sum(), total);
    }
}
