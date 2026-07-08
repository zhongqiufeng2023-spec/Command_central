using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>左翼战线(关卡文档 §4-6):李嵩一路 NPC、告急骑、驰援令与行营旧认知、左翼崩溃=硬性败。</summary>
public class LeftWingTests
{
    private static BattleSim Sim(int w = 30, int h = 10)
        => new(new BattleMap(w, h), new Rng(11)) { HqPos = new Vec2F(24, 80) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    private static BattleMission BareMission() => new() { Title = "试验", EndTime = 999f };

    [Fact]
    public void BlackPine_Scenario_HasLeftWing()
    {
        var sim = BattleScenario.BlackPineField();
        var allies = sim.Units.Where(u => u.Allied).ToList();
        Assert.Equal(3, allies.Count);                                     // 李嵩一路三队
        Assert.All(allies, a => Assert.Equal(Side.Friend, a.Side));
        Assert.All(allies, a => Assert.False(sim.Sandbox.Own.ContainsKey(a.Id)));   // 不进你的沙盘

        var leftFoes = sim.Units.Where(u => u.Side == Side.Enemy && u.HoldUntilT > 0).ToList();
        Assert.Equal(4, leftFoes.Count);                                   // 左翼之敌四队蛰伏
        Assert.All(leftFoes, e => Assert.NotNull(e.ScriptTarget));

        Assert.Contains(sim.Mission!.Objectives, o => o.Kind == BObjectiveKind.RelieveAlly && o.Primary && !o.Active);
        Assert.Contains(sim.Mission.Orders, o => o.SeedAllyPos);           // 驰援令随附行营所知位置
    }

    [Fact]
    public void PlayerCannotCommand_AlliedUnits()
    {
        var sim = Sim();
        var ally = sim.AddUnit(Side.Friend, UnitType.Spear, "李嵩枪队", new Commander("李嵩", Personality.Steady), new Vec2F(60, 80), 30, allied: true);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(420, 80), 30);

        sim.IssueMove(ally.Id, new Vec2F(200, 80), false);
        sim.IssueStance(ally.Id, BStance.Attack);
        sim.RequestStatus(ally.Id);
        Assert.Empty(sim.Riders);                                          // 友邻不受你的令

        sim.BeginDeploy(400);
        Assert.False(sim.DeployMove(ally.Id, new Vec2F(100, 80)));         // 布阵也摆不动他
    }

    [Fact]
    public void AlliedCollapse_IsHardDefeat()
    {
        var sim = Sim();
        sim.Mission = BareMission();
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(40, 40), 40);      // 本部远在他处
        var ally = sim.AddUnit(Side.Friend, UnitType.Spear, "李嵩枪队", new Commander("李嵩", Personality.Steady, 0.4), new Vec2F(300, 80), 18, allied: true);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady, 0.8), new Vec2F(315, 80), 160, ai: true);

        Run(sim, 120f);                                                    // 18 打 160,必崩
        Assert.True(sim.Over, $"左翼该崩了(李嵩 {ally.AliveCount} 人,状态 {ally.State})");
        Assert.Equal(Side.Enemy, sim.Winner);
        Assert.Contains(sim.Alerts, a => a.Text.Contains("左翼崩溃"));
        Assert.Equal(BObjectiveState.Failed,
            sim.Mission.Objectives.FirstOrDefault(o => o.Kind == BObjectiveKind.RelieveAlly)?.State ?? BObjectiveState.Failed);
    }

    [Fact]
    public void DistressRider_BringsAllyIntel_AndCryForHelp()
    {
        var sim = Sim();
        sim.Mission = BareMission();
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(30, 120), 60);
        var ally = sim.AddUnit(Side.Friend, UnitType.Spear, "李嵩枪队", new Commander("李嵩", Personality.Steady, 0.9), new Vec2F(60, 40), 100, allied: true);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady, 0.9), new Vec2F(74, 40), 100, ai: true);

        Run(sim, 30f);                                                     // 势均力敌缠斗 → 告急骑出发并抵帐(~50m)
        Assert.True(sim.Sandbox.Ally.Count > 0, "告急骑该把左翼战况带回沙盘");
        Assert.Contains(sim.Alerts, a => a.Text.Contains("左翼告急") && a.Pause);
        Assert.Contains(sim.Sandbox.Ally.Values, m => m.UnitId == ally.Id);
    }

    [Fact]
    public void RelieveOrder_SeedsHqStaleAllyPosition()
    {
        var sim = Sim();
        var m = BareMission();
        m.Objectives.Add(new BObjective { Id = "E", Cn = "驰援", Kind = BObjectiveKind.RelieveAlly, Active = false });
        m.Orders.Add(new BHqOrder { DispatchT = 1, LinkDelay = 2, TitleCn = "驰援令", TextCn = "速援左翼!", Activates = new[] { "E" }, SeedAllyPos = true });
        sim.Mission = m;
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(40, 120), 40);
        var ally = sim.AddUnit(Side.Friend, UnitType.Spear, "李嵩枪队", new Commander("李嵩", Personality.Steady), new Vec2F(100, 40), 40, allied: true);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(460, 140), 40, ai: true);

        Run(sim, 5f);
        Assert.True(m["E"]!.Active);
        Assert.True(sim.Sandbox.Ally.TryGetValue(-1, out var mark), "驰援令该在沙盘落下行营所报的左翼位置");
        Assert.True(mark!.Pos.DistanceTo(ally.Center) > 30f, "行营给的位置是旧认知,应带偏差");
        Assert.Contains("行营", mark.SourceCn);
    }

    [Fact]
    public void RescueArrival_TrackedForVerdict()
    {
        var sim = Sim();
        var m = BareMission();
        m.Objectives.Add(new BObjective { Id = "E", Cn = "驰援", Kind = BObjectiveKind.RelieveAlly, Active = true });
        sim.Mission = m;
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(80, 70), 40);
        sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("丙", Personality.Steady), new Vec2F(90, 90), 40);
        sim.AddUnit(Side.Friend, UnitType.Spear, "李嵩枪队", new Commander("李嵩", Personality.Steady), new Vec2F(100, 80), 40, allied: true);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(460, 140), 40, ai: true);

        Run(sim, 3f);
        Assert.True(m.RescueArrived, "两队本部兵马抵近左翼 = 合势");
        Assert.Contains(sim.Alerts, a => a.Text.Contains("合势"));
    }

    [Fact]
    public void AllyReport_DeliveredToHq_MarksReportAllyDone()
    {
        var sim = Sim();
        var m = BareMission();
        m.Objectives.Add(new BObjective { Id = "F", Cn = "报左翼", Kind = BObjectiveKind.ReportAlly, Active = true });
        sim.Mission = m;
        sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("甲", Personality.Steady), new Vec2F(60, 80), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("乙", Personality.Steady), new Vec2F(460, 140), 40, ai: true);

        sim.Sandbox.Ally[7] = new SandboxAllyMark { UnitId = 7, Pos = new Vec2F(100, 40), Est = 200, StateCn = "酣战", T = 0 };
        sim.SendHqReport();
        Run(sim, 30f);                                                     // 送达 + 回执
        Assert.Equal(1, m.AllyReportsDelivered);
        Assert.Equal(BObjectiveState.Done, m["F"]!.State);
    }
}
