using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>第一刀:建军(队/武将/六兵种 + 草原不对称阵营)、全雾开局、任务(侦察目标)判定。</summary>
public class FirstBattleTests
{
    [Fact]
    public void BlackPine_Builds_Asymmetric_Factions_WithCaps()
    {
        var sim = Scenario.FirstBattleBlackPine();
        var friend = sim.Truth.Units.Where(u => u.Side == Side.Friend).ToList();
        var enemy = sim.Truth.Units.Where(u => u.Side == Side.Enemy).ToList();

        Assert.InRange(friend.Count, 1, 8);                       // 一路 ≤8 队
        Assert.All(friend, u => Assert.Equal(Faction.Dafeng, u.Faction));
        Assert.All(enemy, u => Assert.Equal(Faction.Steppe, u.Faction));
        Assert.All(sim.Truth.Units, u => Assert.True(u.MaxStrength <= 250)); // 每队 ≤250 人
        Assert.All(sim.Truth.Units, u => Assert.NotEqual("", u.Commander.Name)); // 每队配武将

        // 草原兵种与我们不同:有骑射,且我方没有骑射
        Assert.Contains(enemy, u => u.Type == UnitType.HorseArcher);
        Assert.DoesNotContain(friend, u => u.Type == UnitType.HorseArcher);
    }

    [Fact]
    public void FogOfWar_AtStart_KnowsOwnNotEnemy()
    {
        var sim = Scenario.FirstBattleBlackPine();
        var belief = sim.Beliefs[Side.Friend];

        Assert.Empty(belief.KnownOf(Side.Enemy));                              // 敌全雾
        int ownUnits = sim.Truth.Units.Count(u => u.Side == Side.Friend);
        Assert.Equal(ownUnits, belief.KnownOf(Side.Friend).Count());          // 己方位置已知(免费基线)
    }

    [Fact]
    public void SteppeArms_HaveDistinctAttributes()
    {
        // 枪槊克突骑;盾挡骑射;骑射机动最高
        Assert.True(Unit.TypeMatchup(UnitType.Spear, UnitType.NomadLancer) > 1.0);
        Assert.True(Unit.TypeMatchup(UnitType.HorseArcher, UnitType.Shield) < 1.0);

        var horseArcher = new Unit { Type = UnitType.HorseArcher };
        var cavalry = new Unit { Type = UnitType.Cavalry };
        var foot = new Unit { Type = UnitType.Spear };
        Assert.True(horseArcher.BaseSpeed > cavalry.BaseSpeed);
        Assert.True(cavalry.BaseSpeed > foot.BaseSpeed);
    }

    [Fact]
    public void Mission_ScoutObjective_CompletesWhenEnemyKnown()
    {
        var sim = Scenario.FirstBattleBlackPine();
        var a = sim.Mission!["A"]!;
        Assert.Equal(ObjectiveState.Pending, a.State);

        // 模拟斥候已侦明:把 3 个敌军新鲜情报塞进认知世界,再评估
        var belief = sim.Beliefs[Side.Friend];
        foreach (var e in sim.Truth.Units.Where(u => u.Side == Side.Enemy).Take(3))
            belief.Known[e.Id] = new GhostUnit
            {
                UnitId = e.Id, Side = Side.Enemy, LastKnownPos = e.Pos,
                KnownStrength = e.Strength, KnownType = e.Type, ObservedTick = sim.Truth.Tick
            };

        sim.Mission.Evaluate(sim);
        Assert.Equal(ObjectiveState.Done, a.State);
    }
}
