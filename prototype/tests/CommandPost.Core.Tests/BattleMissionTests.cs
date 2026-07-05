using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>战役任务层(逐兵版):中军令时间线、侦明目标、军书上行+回执、战毕评语、战役窗口。</summary>
public class BattleMissionTests
{
    private static BattleSim Sim(int w = 30, int h = 10)
        => new(new BattleMap(w, h), new Rng(7)) { HqPos = new Vec2F(24, 80) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    /// <summary>标准试验任务:一道令 5s 后送达,激活 A(侦明两部)。</summary>
    private static BattleMission TestMission() => new()
    {
        Title = "试验",
        HqPersonality = Personality.Aggressive,
        Objectives =
        {
            new BObjective { Id = "A", Cn = "侦明", Kind = BObjectiveKind.ScoutEnemy, Primary = true, Active = false, RequiredCount = 2 },
            new BObjective { Id = "B", Cn = "具报", Kind = BObjectiveKind.ReportToHq },
            new BObjective { Id = "C", Cn = "破敌", Kind = BObjectiveKind.DefeatEnemy, Active = false },
            new BObjective { Id = "D", Cn = "保全", Kind = BObjectiveKind.PreserveArmy },
        },
        Orders =
        {
            new BHqOrder { DispatchT = 0, LinkDelay = 5, TitleCn = "第一道令", TextCn = "速侦虏踪。", Activates = new[] { "A" } },
        }
    };

    /// <summary>两军远隔对峙(不接战),挂上试验任务。</summary>
    private static (BattleSim sim, BattleMission m, BattleUnit friend, BattleUnit foe) Standoff()
    {
        var sim = Sim();
        var friend = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(60, 80), 40);
        var foe = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(420, 80), 40);
        var m = TestMission();
        sim.Mission = m;
        return (sim, m, friend, foe);
    }

    [Fact]
    public void HqOrder_DeliversOnTimeline_ActivatesObjective_AndAlerts()
    {
        var (sim, m, _, _) = Standoff();
        Run(sim, 3f);
        Assert.False(m["A"]!.Active);                                  // 令未至,目标未激活
        Run(sim, 4f);
        Assert.True(m["A"]!.Active);                                   // 5s 送达
        Assert.True(m.Orders[0].Delivered);
        Assert.Contains(sim.Alerts, a => a.Text.Contains("中军令") && a.Pause);
    }

    [Fact]
    public void ScoutObjective_JudgedFromSandboxOnly_NotTruth()
    {
        var (sim, m, _, _) = Standoff();
        Run(sim, 6f);                                                  // 令已至,A 激活
        Assert.Equal(BObjectiveState.Pending, m["A"]!.State);          // 真相里有敌,但沙盘空 → 不算侦明(支柱①)
        sim.Sandbox.Enemy[91] = new SandboxEnemyMark { UnitId = 91, Pos = new Vec2F(400, 70), Est = 100, T = sim.Time };
        Run(sim, 1.5f);
        Assert.Equal(BObjectiveState.Partial, m["A"]!.State);
        sim.Sandbox.Enemy[92] = new SandboxEnemyMark { UnitId = 92, Pos = new Vec2F(420, 90), Est = 150, T = sim.Time };
        Run(sim, 1.5f);
        Assert.Equal(BObjectiveState.Done, m["A"]!.State);
    }

    [Fact]
    public void HqReport_RiderRidesOut_ReceiptComesBack_ThenObjectiveDone()
    {
        var (sim, m, _, _) = Standoff();
        sim.Sandbox.Enemy[91] = new SandboxEnemyMark { UnitId = 91, Pos = new Vec2F(400, 70), Est = 100, T = 0 };
        sim.SendHqReport();
        Assert.Contains(sim.Riders, r => r.ToHq);                      // 骑手真实出发
        Assert.Equal(BObjectiveState.Pending, m["B"]!.State);

        Run(sim, 8f);                                                  // 西缘 ~20m,几秒即达
        Assert.Equal(1, m.ReportsDelivered);                           // 行营已收(真相侧)
        Assert.Equal(BObjectiveState.Pending, m["B"]!.State);          // 但回执未到,玩家不知 → 不置 Done

        Run(sim, 20f);                                                 // 批回 12s + 回程
        Assert.Equal(BObjectiveState.Done, m["B"]!.State);
        Assert.Contains(sim.Alerts, a => a.Text.Contains("行营回执"));
        Assert.Equal(1, m.BestReportIntel);                            // 所报敌情条数入账
    }

    [Fact]
    public void Victory_FinishesMission_WithVerdict_AndDefeatObjectiveDone()
    {
        var (sim, m, _, foe) = Standoff();
        Run(sim, 6f);
        foe.Soldiers.Clear();                                          // 敌全灭
        Run(sim, 3f);
        Assert.True(sim.Over);
        Assert.Equal(Side.Friend, sim.Winner);
        Assert.NotNull(m.Verdict);
        Assert.Equal(BObjectiveState.Done, m["C"]!.State);             // 未奉令而胜也算破敌
        Assert.True(m.WonBeforeWarOrder);                              // 破敌令(不存在=未激活)前已胜
        Assert.Equal(BObjectiveState.Done, m["D"]!.State);             // 零伤亡 → 保全
        Assert.True(m.Verdict!.Trust > 50, $"尚功主帅+全胜应受赏,得 {m.Verdict.Trust}");
    }

    [Fact]
    public void BattleWindow_Expires_EnemyWithdraws_SettleByObjectives()
    {
        var (sim, m, _, _) = Standoff();
        m.EndTime = 4f;
        Run(sim, 6f);
        Assert.True(sim.Over);
        Assert.Null(sim.Winner);                                       // 虏自遁,非胜非败
        Assert.NotNull(m.Verdict);
        Assert.Contains(sim.Alerts, a => a.Text.Contains("东遁"));
        Assert.Equal(BObjectiveState.Pending, m["C"]!.State);          // 破敌令未至 → 不判罪(令未至不评)
    }

    [Fact]
    public void BlackPineField_HasMissionAttached()
    {
        var sim = BattleScenario.BlackPineField();
        Assert.NotNull(sim.Mission);
        Assert.Equal(4, sim.Mission!.Objectives.Count);
        Assert.True(sim.Mission.Orders.Count >= 2);                    // 分阶段中军令
    }
}
