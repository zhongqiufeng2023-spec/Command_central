using System.Linq;
using CommandPost.Core;
using Xunit;

namespace CommandPost.Core.Tests;

// 战略层地基:热力图地形生成 / NPC 野战限时结算 / 按地形生成的遭遇战场。
public class WorldGenTests
{
    [Fact]
    public void Terrain_SameSeed_SameMap()
    {
        var a = WorldGen.Terrain(20260711, 120, 80, out _);
        var b = WorldGen.Terrain(20260711, 120, 80, out _);
        for (int x = 0; x < 120; x++)
            for (int y = 0; y < 80; y++)
                Assert.Equal(a[x, y], b[x, y]);
    }

    [Fact]
    public void Terrain_DifferentSeed_Differs()
    {
        var a = WorldGen.Terrain(1, 120, 80, out _);
        var b = WorldGen.Terrain(2, 120, 80, out _);
        int diff = 0;
        for (int x = 0; x < 120; x++)
            for (int y = 0; y < 80; y++)
                if (a[x, y] != b[x, y]) diff++;
        Assert.True(diff > 500, $"两种子地图几乎一样(差异 {diff} 格)");
    }

    [Fact]
    public void Terrain_HasVariety_AndSaneDistribution()
    {
        var t = WorldGen.Terrain(20260711, 200, 130, out var height);
        var count = new int[12];
        for (int x = 0; x < 200; x++)
            for (int y = 0; y < 130; y++)
            {
                count[t[x, y]]++;
                Assert.InRange(height[x, y], 0f, 1f);
            }
        int total = 200 * 130;
        Assert.True(count[(int)WTerrain.Plain] > total * 0.10, "草原太少");
        Assert.True(count[(int)WTerrain.Forest] > 0, "无林");
        Assert.True(count[(int)WTerrain.Hill] > 0, "无丘");
        Assert.True(count[(int)WTerrain.Mountain] > 0, "无山");
        Assert.True(count[(int)WTerrain.Mountain] < total * 0.35, "山占比失控");
    }

    [Theory]
    [InlineData(0.90f, 0.5f, WTerrain.Mountain)]
    [InlineData(0.70f, 0.5f, WTerrain.Hill)]
    [InlineData(0.20f, 0.80f, WTerrain.Marsh)]
    [InlineData(0.45f, 0.70f, WTerrain.Forest)]
    [InlineData(0.45f, 0.30f, WTerrain.Plain)]
    public void Classify_ByHeightAndMoisture(float h, float m, WTerrain expect)
        => Assert.Equal(expect, WorldGen.Classify(h, m));

    [Fact]
    public void GroundOf_MapsStrategicToTactical()
    {
        Assert.Equal(BattleGround.Forest, WorldGen.GroundOf((byte)WTerrain.Forest));
        Assert.Equal(BattleGround.Hills, WorldGen.GroundOf((byte)WTerrain.Hill));
        Assert.Equal(BattleGround.Hills, WorldGen.GroundOf((byte)WTerrain.Mountain));
        Assert.Equal(BattleGround.Marsh, WorldGen.GroundOf((byte)WTerrain.Marsh));
        Assert.Equal(BattleGround.Marsh, WorldGen.GroundOf((byte)WTerrain.Miasma));
        Assert.Equal(BattleGround.Plain, WorldGen.GroundOf((byte)WTerrain.Road));
        Assert.Equal(BattleGround.Plain, WorldGen.GroundOf((byte)WTerrain.Plain));
    }
}

public class AutoBattleTests
{
    private static float RunToEnd(AutoBattle ab, int seed)
    {
        var rng = new Rng(seed);
        int guard = 0;
        while (!ab.Over && guard++ < 400) ab.Step(0.5f, rng);
        Assert.True(ab.Over, "野战迟迟不结束");
        return ab.Hours;
    }

    [Fact]
    public void Deterministic_SameInputs_SameOutcome()
    {
        var a = AutoBattle.Start(400, 1f, 400, 1f, BattleGround.Plain);
        var b = AutoBattle.Start(400, 1f, 400, 1f, BattleGround.Plain);
        RunToEnd(a, 7); RunToEnd(b, 7);
        Assert.Equal(a.MenA, b.MenA);
        Assert.Equal(a.MenB, b.MenB);
        Assert.Equal(a.Winner, b.Winner);
        Assert.Equal(a.Hours, b.Hours);
    }

    [Fact]
    public void BiggerBattle_LastsLonger()
    {
        var small = AutoBattle.Start(300, 1f, 300, 1f, BattleGround.Plain);
        var big = AutoBattle.Start(1500, 1f, 1500, 1f, BattleGround.Plain);
        float hs = RunToEnd(small, 11), hb = RunToEnd(big, 11);
        Assert.True(hb > hs, $"大战({hb:0.0}时辰)竟不比小仗({hs:0.0}时辰)久");
        Assert.True(hs >= 1f, $"小仗也不该一照面就完({hs:0.0}时辰)");
    }

    [Fact]
    public void StrongerSide_Wins()
    {
        var ab = AutoBattle.Start(600, 1f, 250, 1f, BattleGround.Plain);
        RunToEnd(ab, 23);
        Assert.Equal(Side.Friend, ab.Winner);
        Assert.True(ab.MenA > 300, "胜方伤亡离谱");
    }

    [Fact]
    public void Marsh_SlowsTheKilling()
    {
        var plain = AutoBattle.Start(400, 1f, 400, 1f, BattleGround.Plain);
        var marsh = AutoBattle.Start(400, 1f, 400, 1f, BattleGround.Marsh);
        float hp = RunToEnd(plain, 31), hm = RunToEnd(marsh, 31);
        Assert.True(hm > hp, $"泽地({hm:0.0})该比平野({hp:0.0})打得久");
    }
}

public class EncounterScenarioTests
{
    private static int CountTiles(BattleMap m, BTerrain t)
    {
        int n = 0;
        for (int x = 0; x < m.W; x++)
            for (int y = 0; y < m.H; y++)
                if (m.AtTile(x, y) == t) n++;
        return n;
    }

    [Theory]
    [InlineData(BattleGround.Plain)]
    [InlineData(BattleGround.Forest)]
    [InlineData(BattleGround.Hills)]
    [InlineData(BattleGround.Marsh)]
    public void Encounter_Builds_OnEveryGround(BattleGround g)
    {
        var sim = BattleScenario.Encounter(g, 42, foeMen: 450, deploy: false);
        Assert.Equal(6, sim.Units.Count(u => u.Side == Side.Friend && !u.Allied));
        Assert.Equal(3, sim.Units.Count(u => u.Side == Side.Enemy));
        Assert.NotNull(sim.Mission);
        Assert.False(sim.AlliedCollapseIsDefeat);
        Assert.Contains(sim.Mission!.Objectives, o => o.Kind == BObjectiveKind.DefeatEnemy && o.Primary);
    }

    [Fact]
    public void Encounter_TerrainMatchesGround()
    {
        Assert.True(CountTiles(BattleScenario.Encounter(BattleGround.Forest, 5, deploy: false).Map, BTerrain.Forest) > 200, "林地遭遇战没长林子");
        Assert.True(CountTiles(BattleScenario.Encounter(BattleGround.Hills, 5, deploy: false).Map, BTerrain.Hill) > 40, "丘陵遭遇战没起丘");
        Assert.True(CountTiles(BattleScenario.Encounter(BattleGround.Marsh, 5, deploy: false).Map, BTerrain.Marsh) > 50, "泽地遭遇战没铺泥泞");
        var plain = BattleScenario.Encounter(BattleGround.Plain, 5, deploy: false).Map;
        Assert.True(CountTiles(plain, BTerrain.Grass) > plain.W * plain.H / 2, "平野遭遇战不够开阔");
    }

    [Fact]
    public void Encounter_SameSeed_SameForces()
    {
        var a = BattleScenario.Encounter(BattleGround.Hills, 99, foeMen: 500, deploy: false);
        var b = BattleScenario.Encounter(BattleGround.Hills, 99, foeMen: 500, deploy: false);
        Assert.Equal(a.Units.Count, b.Units.Count);
        for (int i = 0; i < a.Units.Count; i++)
        {
            Assert.Equal(a.Units[i].MaxCount, b.Units[i].MaxCount);
            Assert.Equal(a.Units[i].Officer.Name, b.Units[i].Officer.Name);
        }
    }

    [Fact]
    public void Encounter_WithAlly_AlliedUnitsPresent_NoHardDefeatRule()
    {
        var sim = BattleScenario.Encounter(BattleGround.Plain, 7, foeMen: 400, allyMen: 260, deploy: false);
        Assert.Equal(2, sim.Units.Count(u => u.Allied));
        Assert.False(sim.AlliedCollapseIsDefeat);

        // 友邻全灭也不判硬败(把友邻士卒直接清空,跑几拍看胜负判定)
        foreach (var u in sim.Units)
            if (u.Allied) u.Soldiers.Clear();
        for (int i = 0; i < 40; i++) sim.Tick();
        Assert.False(sim.Over && sim.Winner == Side.Enemy && sim.Time < 5f, "友邻覆灭不该立即判硬性败");
    }

    [Fact]
    public void Encounter_RunsWithoutCrash()
    {
        var sim = BattleScenario.Encounter(BattleGround.Marsh, 13, foeMen: 480, allyMen: 200, deploy: true);
        sim.FinishDeploy();
        for (int i = 0; i < 1200 && !sim.Over; i++) sim.Tick();   // 两分钟战况
        Assert.True(sim.Time > 60f);
    }

    [Fact]
    public void Encounter_ReducedOwnCounts_OmitsBrokenUnits()
    {
        var counts = new[] { 240, 10, 250, 0, 160, 14 };          // 陌刀/弩/铁骑 残不成队
        var sim = BattleScenario.Encounter(BattleGround.Plain, 3, ownCounts: counts, deploy: false);
        Assert.Equal(3, sim.Units.Count(u => u.Side == Side.Friend && !u.Allied));
    }
}
