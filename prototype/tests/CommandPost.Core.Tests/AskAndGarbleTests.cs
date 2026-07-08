using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>请示求决(PRD §6.4)与命令去程失真(PRD §6.5)。</summary>
public class AskAndGarbleTests
{
    private static BattleSim Sim()
        => new(new BattleMap(40, 12), new Rng(9)) { HqPos = new Vec2F(24, 96) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    // ================= 请示求决 =================

    [Fact]
    public void HoldUnit_FacingEnemy_SendsAskRider_AlertOnArrival()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("周石", Personality.Cautious, 0.8), new Vec2F(60, 96), 50);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(155, 96), 70);

        Run(sim, 20f);
        Assert.True(u.AwaitingReply, "守势遇敌该遣骑请示");
        Assert.Contains(sim.Alerts, a => a.Text.Contains("请示") && a.Pause);
        Assert.True(sim.Sandbox.Enemy.Count > 0, "请示骑该把所见之敌捎回沙盘");
    }

    [Fact]
    public void NoReply_AggressiveOfficer_ChargesOnHisOwn()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("秦锐", Personality.Aggressive, 0.6), new Vec2F(60, 96), 120);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(155, 96), 70);

        Run(sim, 50f);                                          // 贪功耐性 35s,到点自作主张
        Assert.False(u.AwaitingReply);
        Assert.Equal(BStance.Attack, u.Stance);
        Assert.Equal("请示未复,自行斟酌", u.LastQuirkCn);
    }

    [Fact]
    public void ReplyInTime_OfficerFollowsYourOrder()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Bow, "弩队", new Commander("王朗", Personality.Steady, 0.85), new Vec2F(60, 96), 50);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(155, 96), 70);

        Run(sim, 8f);                                           // 请示已发
        Assert.True(u.AwaitingReply);
        sim.IssueStance(u.Id, BStance.Skirmish);                // 你回令(令骑 ~5s 送达)
        Run(sim, 62f);                                          // 稳健耐性 60s——回令已至,不该自处
        Assert.Equal(BStance.Skirmish, u.Stance);
        Assert.NotEqual("请示未复,自行斟酌", u.LastQuirkCn);
    }

    // ================= 命令去程失真 =================

    [Fact]
    public void GarbledStanceOrder_ArrivesWrong_AndRevealedPostBattle()
    {
        var sim = Sim();
        sim.Difficulty = new BDifficultyProfile { Cn = "试验", OrderGarbleChance = 1f };   // 必错:验证机制
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("王朗", Personality.Steady, 0.9), new Vec2F(60, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(560, 96), 40);

        sim.IssueStance(u.Id, BStance.Skirmish);
        Run(sim, 20f);
        Assert.NotEqual(BStance.Skirmish, u.Stance);            // 送达的是错令
        Assert.Contains(sim.Reveals, s => s.Contains("记成了"));  // 复盘才揭示
        Assert.DoesNotContain(sim.Sandbox.Feed, s => s.Contains("记成了"));   // 战中不穿帮
    }

    [Fact]
    public void GarbledMoveOrder_DestinationDrifts()
    {
        var sim = Sim();
        sim.Difficulty = new BDifficultyProfile { Cn = "试验", OrderGarbleChance = 1f };
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("王朗", Personality.Steady, 0.9), new Vec2F(60, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(560, 40), 40);

        var ordered = new Vec2F(200, 96);
        sim.IssueMove(u.Id, ordered, run: false);
        Run(sim, 25f);
        var end = u.Path.Count > 0 ? u.Path[^1] : u.Center;
        Assert.True(end.DistanceTo(ordered) > 40f, $"目的地该被传偏(实际终点 {end})");
        Assert.Contains(sim.Reveals, s => s.Contains("传偏"));
    }

    [Fact]
    public void NormalDifficulty_GarbleIsRare()
    {
        var sim = Sim();                                        // 常规档 6%
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("王朗", Personality.Steady, 0.9), new Vec2F(60, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(560, 96), 40);

        sim.IssueStance(u.Id, BStance.Hold);
        Run(sim, 20f);
        Assert.Equal(BStance.Hold, u.Stance);                   // 种子 9 下这单令不该错(基线稳定)
    }
}
