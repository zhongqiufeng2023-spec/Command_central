using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>旗鼓通道(蓝图§九):近即时、只及声程、绝对照令(不过武将解读)、敌亦可闻。</summary>
public class SignalTests
{
    private static BattleSim Sim()
        => new(new BattleMap(50, 12), new Rng(13)) { HqPos = new Vec2F(40, 96) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    [Fact]
    public void Signal_InstantWithinRange_SilentBeyond()
    {
        var sim = Sim();
        var near = sim.AddUnit(Side.Friend, UnitType.Spear, "近部", new Commander("甲", Personality.Steady, 0.9), new Vec2F(150, 96), 30);
        var far = sim.AddUnit(Side.Friend, UnitType.Spear, "远部", new Commander("乙", Personality.Steady, 0.9), new Vec2F(700, 96), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(760, 40), 30);
        Run(sim, 1f);

        int heard = sim.SoundSignal(BStance.Attack);
        Assert.Equal(1, heard);
        Assert.Equal(BStance.Attack, near.Stance);       // 声程内(110m):即时照令,无令骑
        Assert.Equal(BStance.Hold, far.Stance);          // 声程外(660m):什么也没听见
        Assert.Empty(sim.Riders.Where(r => r.Kind == RiderKind.Order));
    }

    [Fact]
    public void Signal_AbsoluteObedience_NoTemperamentTwist()
    {
        var sim = Sim();
        // 贪功武将 + 敌在眼前:若走令骑,他多半抗令喊杀;旗鼓=耳只听金鼓,必须照令
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("秦锐", Personality.Aggressive, 0.3), new Vec2F(150, 96), 40);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(240, 96), 70);
        Run(sim, 3f);
        u.LastThreatT = sim.Time;                        // 敌在眼前(受威胁中)

        sim.SoundSignal(BStance.Standby);
        Assert.Equal(BStance.Standby, u.Stance);         // 绝对照令,贪功也得听
        Assert.Equal("", u.LastQuirkCn);
    }

    [Fact]
    public void Signal_ClearsAwaitingReply()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("周石", Personality.Cautious, 0.8), new Vec2F(150, 96), 50);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(250, 96), 70);
        Run(sim, 10f);
        Assert.True(u.AwaitingReply);                    // 已遣骑请示

        sim.SoundSignal(BStance.Hold);                   // 旗鼓也是回令
        Assert.False(u.AwaitingReply);
    }
}
