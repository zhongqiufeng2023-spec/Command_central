using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>帅帐链路(分阶段中军令 / 上报)、驰援目标判定、战役窗口与战后政治评语。</summary>
public class HqAndPoliticsTests
{
    [Fact]
    public void SecondHqOrder_Arrives_AndActivatesAidObjective()
    {
        var sim = Scenario.FirstBattleBlackPine(aidOrderTick: 20);
        Assert.False(sim.Mission!["C"]!.Active);                    // 驰援目标:令未至不激活

        for (int i = 0; i < 33; i++) sim.AdvanceTick();             // 20 发出 + 12 链路延迟
        Assert.True(sim.Mission["C"]!.Active);
        Assert.True(sim.Mission["D"]!.Active);
        Assert.NotNull(sim.Mission.AidOrderDeliveredTick);
        Assert.Contains(sim.Log, l => l.Contains("第二道令"));
        Assert.Contains(sim.Alerts, a => a.Pause && a.Text.Contains("第二道令"));   // 收令要自动暂停
    }

    [Fact]
    public void Support_Done_WhenTwoPlayerUnitsReachLeftWing()
    {
        var sim = Scenario.FirstBattleBlackPine(aidOrderTick: 5);
        for (int i = 0; i < 18; i++) sim.AdvanceTick();             // 驰援令已送达(5+12)
        var c = sim.Mission!["C"]!;
        Assert.True(c.Active);
        Assert.NotEqual(ObjectiveState.Done, c.State);

        var anchor = c.TargetPos!.Value;                            // 两队本路兵直接放到左翼旗点
        foreach (var u in sim.Truth.LivingOf(Side.Friend).Where(u => u.PlayerLed).Take(2))
            u.Pos = anchor;
        sim.AdvanceTick();
        Assert.Equal(ObjectiveState.Done, c.State);
    }

    [Fact]
    public void ReportToHq_MarksObjectiveB_AfterLinkDelay()
    {
        var sim = Scenario.FirstBattleBlackPine();
        var e = sim.Truth.Units.First(u => u.Side == Side.Enemy);   // 先有一条敌情可报
        sim.Beliefs[Side.Friend].Known[e.Id] = new GhostUnit
        { UnitId = e.Id, Side = Side.Enemy, LastKnownPos = e.Pos, ObservedTick = 0 };

        sim.ReportToHq();
        Assert.NotEqual(ObjectiveState.Done, sim.Mission!["B"]!.State);   // 在途,还不算
        for (int i = 0; i < 13; i++) sim.AdvanceTick();
        Assert.Equal(ObjectiveState.Done, sim.Mission["B"]!.State);       // 送达才算「回报」
    }

    [Fact]
    public void Appraisal_Issued_WhenBattleWindowCloses()
    {
        var sim = Scenario.FirstBattleBlackPine(aidOrderTick: 10);  // 窗口 = 10 + 300
        Assert.Null(sim.Appraisal);
        int guard = 0;
        while (!sim.BattleOver && guard++ < 400) sim.AdvanceTick();

        Assert.True(sim.BattleOver);
        Assert.NotNull(sim.Appraisal);                              // 结束那一 tick 已出评语
        Assert.InRange(sim.Appraisal!.Trust, 0, 100);
        Assert.False(string.IsNullOrEmpty(sim.Appraisal.VerdictCn));
        Assert.NotEmpty(sim.Appraisal.Lines);
    }
}
