using System;
using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>塘骑寻路:必须能离开大营水平线,南渡河北上丘,抵达全图任意可通行处。</summary>
public class ScoutPathTests
{
    private static BattleSim EmptyField()
    {
        var sim = BattleScenario.BlackPineField();
        sim.Units.RemoveAll(u => u.Side == Side.Enemy);   // 清场:专测通行,不受截杀/战况干扰
        return sim;
    }

    private static float ClosestApproach(BattleSim sim, Vec2F target, float seconds)
    {
        float best = float.MaxValue;
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && sim.Riders.Count > 0; i++)
        {
            sim.Tick();
            foreach (var r in sim.Riders) best = MathF.Min(best, r.Pos.DistanceTo(target));
        }
        return best;
    }

    [Fact]
    public void Scout_ReachesTarget_NorthOfHqLine()
    {
        var sim = EmptyField();                            // 大营 y≈272
        var north = new Vec2F(500, 60);                    // 远在北面(林带以西北)
        sim.DispatchScout(north);
        float best = ClosestApproach(sim, north, 120f);
        Assert.True(best < 25f, $"塘骑应能抵达北面目标,最近仅至 {best:0}m");
    }

    [Fact]
    public void Scout_ReachesTarget_SouthAcrossRiver()
    {
        var sim = EmptyField();
        var south = new Vec2F(300, 500);                   // 河南岸(必须走渡滩)
        sim.DispatchScout(south);
        float best = ClosestApproach(sim, south, 180f);
        Assert.True(best < 25f, $"塘骑应能渡滩抵南岸,最近仅至 {best:0}m");
    }

    [Fact]
    public void Path_FromHq_ToNorthEast_ActuallyBends()
    {
        var sim = EmptyField();
        var path = BattlePath.Find(sim.Map, sim.HqPos, new Vec2F(800, 100));
        Assert.NotEmpty(path);
        Assert.True(path[^1].DistanceTo(new Vec2F(800, 100)) < 2f, $"终点应为目标,实为 {path[^1]}");
        Assert.Contains(path, p => MathF.Abs(p.Y - sim.HqPos.Y) > 60f);   // 路线确实离开了大营水平线
    }
}
