using System.Linq;
using Xunit;
using CommandPost.Core;

namespace CommandPost.Core.Tests;

/// <summary>武将解读层(第二层迷雾,蓝图 §十):同一道令,不同脾性的武将执行不同;
/// 走样批注只随军报/探问带回——你下的令和他做的事之间,隔着一层人。</summary>
public class OfficerTemperamentTests
{
    private static BattleSim Sim()
        => new(new BattleMap(40, 12), new Rng(5)) { HqPos = new Vec2F(24, 96) };

    private static void Run(BattleSim sim, float seconds)
    {
        int n = (int)(seconds / BattleSim.Dt);
        for (int i = 0; i < n && !sim.Over; i++) sim.Tick();
    }

    /// <summary>下移动令并等令骑送达,返回送达后的部队路径终点。</summary>
    private static Vec2F DeliverMove(BattleSim sim, BattleUnit u, Vec2F dest)
    {
        sim.IssueMove(u.Id, dest, run: false);
        Run(sim, 25f);                                  // 令骑抵达(几十米,几秒);再走一小段
        var path = u.Path;
        return path.Count > 0 ? path[^1] : u.Center;
    }

    [Fact]
    public void SteadyOfficer_ExecutesOrderAsGiven()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("王朗", Personality.Steady, 0.85), new Vec2F(60, 96), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(560, 96), 30, ai: true);

        var ordered = new Vec2F(200, 96);
        var end = DeliverMove(sim, u, ordered);
        Assert.True(end.DistanceTo(ordered) < 20f, $"稳健武将应照令执行,实际终点 {end}");
        Assert.Equal("", u.LastQuirkCn);
    }

    [Fact]
    public void AggressiveOfficer_OvershootsTowardEnemy()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("秦锐", Personality.Aggressive, 0.55), new Vec2F(60, 96), 30);
        var foe = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(430, 96), 30, ai: true);

        var ordered = new Vec2F(280, 96);               // 敌前 150m 停住——他不会停的
        var end = DeliverMove(sim, u, ordered);
        Assert.True(end.DistanceTo(foe.Center) < ordered.DistanceTo(foe.Center) - 15f,
            $"贪功武将该压前:令点距敌 {ordered.DistanceTo(foe.Center):0},实际终点距敌 {end.DistanceTo(foe.Center):0}");
        Assert.Equal("贪功压前", u.LastQuirkCn);
    }

    [Fact]
    public void CautiousOfficer_StopsShortOfEnemy()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Shield, "盾队", new Commander("周石", Personality.Cautious, 0.55), new Vec2F(60, 96), 30);
        var foe = sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(430, 96), 30, ai: true);

        var ordered = new Vec2F(370, 96);               // 你把他画到敌人嘴边(60m)
        var end = DeliverMove(sim, u, ordered);
        Assert.True(end.DistanceTo(foe.Center) > ordered.DistanceTo(foe.Center) + 10f,
            $"持重武将该止步敌前:令点距敌 {ordered.DistanceTo(foe.Center):0},实际终点距敌 {end.DistanceTo(foe.Center):0}");
        Assert.Equal("止步敌前", u.LastQuirkCn);
    }

    [Fact]
    public void Quirk_ComesBackWithOrderRiderReport()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("秦锐", Personality.Aggressive, 0.55), new Vec2F(60, 96), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(430, 96), 30, ai: true);

        sim.IssueMove(u.Id, new Vec2F(280, 96), run: false);
        Run(sim, 60f);                                  // 令骑送达 + 回程复命
        Assert.Contains("贪功压前", sim.Sandbox.Own[u.Id].StateCn);   // 走样随复命回到你的沙盘
    }

    [Fact]
    public void DeployOrders_FaceToFace_NoReinterpretation()
    {
        var sim = Sim();
        var u = sim.AddUnit(Side.Friend, UnitType.Spear, "枪队", new Commander("秦锐", Personality.Aggressive, 0.4), new Vec2F(60, 96), 30);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot, "部众", new Commander("敌", Personality.Steady), new Vec2F(430, 96), 30, ai: true);
        sim.BeginDeploy(300f);

        Assert.True(sim.DeployMove(u.Id, new Vec2F(200, 96)));
        Assert.True(u.Center.DistanceTo(new Vec2F(200, 96)) < 1f);     // 当面吩咐:摆哪是哪
        sim.DeployStance(u.Id, BStance.Standby);
        Assert.Equal(BStance.Standby, u.Stance);                       // 面对面,没有解读的余地
        Assert.Equal("", u.LastQuirkCn);
    }
}
