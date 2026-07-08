using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>战后复盘(PRD §10):逐秒录真相+认知双帧;战毕对照回放与关键分叉分析。</summary>
public class ReplayTests
{
    private static BattleSim Sim()
        => new(new BattleMap(30, 10), new Rng(3)) { HqPos = new Vec2F(24, 80) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    [Fact]
    public void Frames_RecordedOncePerSecond_WithTruthAndBelief()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(60, 80), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(420, 80), 30);
        sim.Sandbox.Enemy[99] = new SandboxEnemyMark { UnitId = 99, Pos = new Vec2F(300, 40), Est = 100, T = 0 };

        Run(sim, 10f);
        Assert.InRange(sim.Replay.Frames.Count, 9, 11);                 // ~1 帧/秒
        var f = sim.Replay.Frames[^1];
        Assert.Contains(f.Units, x => x.Id == u.Id && x.Alive == 30);   // 真相侧
        Assert.Contains(f.BeliefEnemy, m => m.Id == 99);                // 认知侧
        Assert.Contains(f.BeliefOwn, m => m.Id == u.Id);
    }

    [Fact]
    public void Frames_AreSnapshots_NotLiveReferences()
    {
        var sim = Sim();
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(60, 80), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(420, 80), 30);
        var mark = new SandboxEnemyMark { UnitId = 99, Pos = new Vec2F(300, 40), Est = 100, T = 0 };
        sim.Sandbox.Enemy[99] = mark;

        Run(sim, 3f);
        mark.Pos = new Vec2F(999, 99);                                  // 事后改沙盘
        var recorded = sim.Replay.Frames[0].BeliefEnemy.First(m => m.Id == 99);
        Assert.True(recorded.Pos.DistanceTo(new Vec2F(300, 40)) < 1f); // 录的是当时的样子
    }

    [Fact]
    public void Analyze_FindsBeliefTruthDivergence()
    {
        var sim = Sim();
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(40, 130), 30);
        var foe = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(430, 40), 40);
        // 你的沙盘一直以为敌在旧地——真相里它早跑远了
        sim.Sandbox.Enemy[foe.Id] = new SandboxEnemyMark { UnitId = foe.Id, Pos = new Vec2F(430, 40), Est = 40, T = 0 };
        foe.SetOrderDirect(new Vec2F(100, 40), run: true, sim.Map);

        Run(sim, 60f);
        var moments = sim.Replay.Analyze(id => "部众");
        Assert.NotEmpty(moments);
        Assert.Contains("实际已移", moments[0].Cn);
        Assert.True(moments[0].BeliefAt.DistanceTo(moments[0].TruthAt) > 130f);
    }

    [Fact]
    public void FinalFrame_RecordedAtBattleEnd()
    {
        var sim = Sim();
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(60, 80), 30);
        var foe = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(420, 80), 30);
        Run(sim, 5f);
        foe.Soldiers.Clear();
        Run(sim, 3f);
        Assert.True(sim.Over);
        Assert.True(sim.Replay.Duration >= sim.Time - 1.6f, $"战毕收尾帧应已录(胶卷至 {sim.Replay.Duration:0.0}s,战至 {sim.Time:0.0}s)");
    }
}
