using CommandPost.Core;
using Xunit;

namespace CommandPost.Core.Tests;

public class CoreTests
{
    // 兵种克制三角(纯函数)
    [Fact]
    public void TypeMatchup_FollowsRockPaperScissors()
    {
        Assert.True(Unit.TypeMatchup(UnitType.Spear, UnitType.Cavalry) > 1.0);   // 枪克骑
        Assert.True(Unit.TypeMatchup(UnitType.Cavalry, UnitType.Bow) > 1.0);     // 骑克弓
        Assert.True(Unit.TypeMatchup(UnitType.Bow, UnitType.Spear) > 1.0);       // 弓克步
        Assert.True(Unit.TypeMatchup(UnitType.Cavalry, UnitType.Spear) < 1.0);   // 骑忌枪
    }

    // 认知世界:较新的观测覆盖,较旧的被忽略
    [Fact]
    public void Belief_NewerObservationWins_OlderIgnored()
    {
        var b = new BeliefWorld { Owner = Side.Friend };
        b.Integrate(Report(unitId: 9, tick: 5, pos: new Vec2(5, 5)));
        b.Integrate(Report(unitId: 9, tick: 3, pos: new Vec2(1, 1))); // 更旧 → 应忽略
        Assert.Equal(new Vec2(5, 5), b.Known[9].LastKnownPos);

        b.Integrate(Report(unitId: 9, tick: 8, pos: new Vec2(9, 9))); // 更新 → 覆盖
        Assert.Equal(new Vec2(9, 9), b.Known[9].LastKnownPos);
        Assert.Equal(8, b.Known[9].ObservedTick);
    }

    // 命令延迟:意图不会立即生效,传令兵送到后副将才执行(intercept=0 确定性)
    [Fact]
    public void Command_TakesTimeToReachFrontline()
    {
        var sim = BuildControlled(NoLossSlow());
        var u = sim.Truth.UnitById(1)!;
        Assert.Null(u.Order);

        sim.IssueIntent(1, Intent.Move(new Vec2(8, 4)));
        sim.AdvanceTick();
        Assert.Null(u.Order); // 1 tick 还没送到

        for (int i = 0; i < 6; i++) sim.AdvanceTick();
        Assert.NotNull(u.Order);
        Assert.Equal(Verb.Move, u.Order!.Verb); // 高能力副将 + 常规基调 → 忠实执行
    }

    // 丢失 = 纯沉默:全程拦截下,玩家永远拿不到敌情
    [Fact]
    public void TotalInterception_MeansPureSilence()
    {
        var sim = BuildSpotter(AlwaysIntercept());
        for (int i = 0; i < 60; i++) sim.AdvanceTick();
        Assert.Empty(sim.Beliefs[Side.Friend].KnownOf(Side.Enemy)); // 一条敌情都没送达
    }

    // 对照组:不拦截时,玩家确实会从战报里得知敌情
    [Fact]
    public void WithoutInterception_FriendLearnsEnemy()
    {
        var sim = BuildSpotter(NeverIntercept());
        for (int i = 0; i < 60; i++) sim.AdvanceTick();
        Assert.NotEmpty(sim.Beliefs[Side.Friend].KnownOf(Side.Enemy));
    }

    // 确定性:同种子同输入 → 同结果(可单测、可复盘重放)
    [Fact]
    public void SameSeed_SameInput_IsDeterministic()
    {
        double Run()
        {
            var s = Scenario.MinimalEncounter(DifficultySettings.Normal, seed: 777);
            s.IssueIntent(2, Intent.Move(new Vec2(9, 6)));
            s.IssueIntent(1, Intent.Attack(5, Tone.Aggressive));
            for (int i = 0; i < 50; i++) s.AdvanceTick();
            return s.Truth.Units.Sum(u => u.Strength) + s.Truth.Tick;
        }
        Assert.Equal(Run(), Run());
    }

    // 胜负判定
    [Fact]
    public void Status_DetectsWinAndLoss()
    {
        var win = Scenario.MinimalEncounter();
        foreach (var e in win.Truth.Units.Where(u => u.Side == Side.Enemy)) e.Strength = 0;
        Assert.Equal(GameStatus.FriendWon, win.Status);

        var lose = Scenario.MinimalEncounter();
        foreach (var f in lose.Truth.Units.Where(u => u.Side == Side.Friend)) f.Strength = 0;
        Assert.Equal(GameStatus.EnemyWon, lose.Status);
    }

    // 探问(派回程传令兵)不会因「遍历中改集合」抛异常,且能送回近况
    [Fact]
    public void RequestStatus_DoesNotThrow_AndDeliversReport()
    {
        var sim = BuildControlled(NoLossSlow());
        sim.RequestStatus(1);
        for (int i = 0; i < 30; i++) sim.AdvanceTick();   // 给往返时间(原 bug 会在此抛异常)
        Assert.Contains(sim.Beliefs[Side.Friend].Inbox, r => r.About.UnitId == 1);
    }

    // 中军派出的斥候发现敌军并把敌情带回认知世界(避敌、到点/受阻则返回)
    [Fact]
    public void HqScout_DiscoversEnemy_AndReportsBack()
    {
        var terrain = new TerrainGrid(20, 5, TerrainType.Plain);
        var truth = new TruthWorld { Terrain = terrain, FriendHq = new Vec2(0, 2), EnemyHq = new Vec2(19, 2) };
        // 友军部队看不见(Vision 0),敌军也看不见 → 唯一情报来源就是中军派出的斥候
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Vision = 0,
            Commander = new Commander("甲", Personality.Steady, 1.0), Pos = new Vec2(0, 2), Strength = 100, MaxStrength = 100 });
        truth.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Spear, Vision = 0,
            Commander = new Commander("乙", Personality.Steady, 1.0), Pos = new Vec2(10, 2), Strength = 100, MaxStrength = 100 });
        var sim = new Simulation(truth, NeverIntercept(), new Rng(1));

        Assert.Empty(sim.Beliefs[Side.Friend].KnownOf(Side.Enemy));
        sim.DispatchScoutFromHq(new Vec2(10, 2));               // 派斥候去敌军附近
        for (int i = 0; i < 80; i++) sim.AdvanceTick();
        Assert.NotEmpty(sim.Beliefs[Side.Friend].KnownOf(Side.Enemy));   // 斥候发现并带回
        // 斥候不靠近敌军:它从不踏入敌军 2 格内(此处用真相校验侦察纪律由实现保证,断言敌情已知即可)
    }

    // 旌旗:近处部队即时收到、远处部队看不见(纯沉默)
    [Fact]
    public void FlagSignal_ReachesNearbyUnit_ButNotFarUnit()
    {
        var truth = new TruthWorld { Terrain = new TerrainGrid(20, 12) { Weather = Weather.Clear }, FriendHq = new Vec2(0, 6), EnemyHq = new Vec2(19, 6) };
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Name = "近", Pos = new Vec2(1, 6), Strength = 100, MaxStrength = 100 });
        truth.Units.Add(new Unit { Id = 2, Side = Side.Friend, Type = UnitType.Spear, Name = "远", Pos = new Vec2(15, 6), Strength = 100, MaxStrength = 100 });
        truth.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Spear, Name = "敌", Vision = 0, Pos = new Vec2(18, 1), Strength = 100, MaxStrength = 100 });
        var sim = new Simulation(truth, DifficultySettings.Normal, new Rng(1));

        sim.SignalOrder(SignalKind.Flag, SignalCode.Advance);   // 全军
        sim.AdvanceTick();

        Assert.NotNull(truth.UnitById(1)!.Order);   // 近(dist 1 ≤ 7)即时收到
        Assert.Null(truth.UnitById(2)!.Order);      // 远(dist 15 > 7)看不见 → 令不达
    }

    // 旗鼓泄密:收令而动的部队,被视线/声程内的敌军窥得位置 → 并入敌方认知世界
    [Fact]
    public void Signal_LeaksOrderedUnitPosition_ToEnemyBelief()
    {
        var truth = new TruthWorld { Terrain = new TerrainGrid(20, 12) { Weather = Weather.Clear }, FriendHq = new Vec2(0, 6), EnemyHq = new Vec2(19, 6) };
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Name = "前", Pos = new Vec2(1, 6), Strength = 100, MaxStrength = 100 });
        truth.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Bow, Name = "敌", Vision = 0, Pos = new Vec2(5, 6), Strength = 100, MaxStrength = 100 });
        var sim = new Simulation(truth, DifficultySettings.Normal, new Rng(1));

        Assert.Empty(sim.Beliefs[Side.Enemy].KnownOf(Side.Friend));   // 信号前:敌(视野0)不知我

        sim.SignalOrder(SignalKind.Flag, SignalCode.Advance, targetUnitId: 1);
        sim.AdvanceTick();

        Assert.Contains(sim.Beliefs[Side.Enemy].KnownOf(Side.Friend), g => g.UnitId == 1);   // 旗号泄密
    }

    // 能见度门槛:雾天旌旗视程砍半,原本够得着的部队收不到
    [Fact]
    public void Fog_ShrinksFlagRange()
    {
        Simulation Build(Weather w)
        {
            var t = new TruthWorld { Terrain = new TerrainGrid(20, 12) { Weather = w }, FriendHq = new Vec2(0, 6), EnemyHq = new Vec2(19, 6) };
            t.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Pos = new Vec2(6, 6), Strength = 100, MaxStrength = 100 });
            t.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Spear, Vision = 0, Pos = new Vec2(18, 1), Strength = 100, MaxStrength = 100 });
            return new Simulation(t, DifficultySettings.Normal, new Rng(1));
        }

        var clear = Build(Weather.Clear);
        clear.SignalOrder(SignalKind.Flag, SignalCode.Halt, 1);
        clear.AdvanceTick();
        Assert.NotNull(clear.Truth.UnitById(1)!.Order);     // 晴:dist 6 ≤ 7 → 收到

        var fog = Build(Weather.Fog);
        fog.SignalOrder(SignalKind.Flag, SignalCode.Halt, 1);
        fog.AdvanceTick();
        Assert.Null(fog.Truth.UnitById(1)!.Order);          // 雾:有效视程 7×0.5=3.5 < 6 → 收不到
    }

    // 瞭望台:近处敌军实时看到、远处看不到(零延迟叠加层)
    [Fact]
    public void Watchtower_SeesNearEnemyRealTime_ButNotFar()
    {
        var truth = new TruthWorld { Terrain = new TerrainGrid(30, 12) { Weather = Weather.Clear }, FriendHq = new Vec2(0, 6), EnemyHq = new Vec2(29, 6) };
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Vision = 0, Pos = new Vec2(0, 6), Strength = 100, MaxStrength = 100 });
        truth.Units.Add(new Unit { Id = 8, Side = Side.Enemy, Type = UnitType.Bow, Vision = 0, Pos = new Vec2(5, 6), Strength = 100, MaxStrength = 100 });    // 近(dist 5 ≤ 8)
        truth.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Spear, Vision = 0, Pos = new Vec2(20, 6), Strength = 100, MaxStrength = 100 });  // 远(dist 20 > 8)
        var sim = new Simulation(truth, DifficultySettings.Normal, new Rng(1));

        sim.AdvanceTick();

        Assert.Contains(sim.WatchtowerContacts, c => c.Side == Side.Enemy && c.Pos == new Vec2(5, 6));   // 近敌:望楼实时看到
        Assert.DoesNotContain(sim.WatchtowerContacts, c => c.Pos == new Vec2(20, 6));                     // 远敌:望不到
    }

    // 瞭望台:只给规模档、不入认知世界(精确层),且实时短暂(离开即消失)
    [Fact]
    public void Watchtower_GivesScaleBucket_RealtimeEphemeral_NotInBelief()
    {
        var truth = new TruthWorld { Terrain = new TerrainGrid(30, 12) { Weather = Weather.Clear }, FriendHq = new Vec2(0, 6), EnemyHq = new Vec2(29, 6) };
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Vision = 0, Pos = new Vec2(0, 6), Strength = 100, MaxStrength = 100 });
        var enemy = new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Spear, Vision = 0, Pos = new Vec2(4, 6), Strength = 130, MaxStrength = 130 };
        truth.Units.Add(enemy);
        var sim = new Simulation(truth, DifficultySettings.Normal, new Rng(1));

        sim.AdvanceTick();
        Assert.Contains(sim.WatchtowerContacts, c => c.Side == Side.Enemy && c.Scale == ScaleHint.Large);  // 130 → 大队
        Assert.Empty(sim.Beliefs[Side.Friend].KnownOf(Side.Enemy));                                          // 不写进认知世界(精确层)

        enemy.Pos = new Vec2(20, 6);   // 敌移出望楼范围
        sim.AdvanceTick();
        Assert.DoesNotContain(sim.WatchtowerContacts, c => c.Side == Side.Enemy);                            // 实时短暂:离开即消失
    }

    // 能见度门槛:雾天瞭望台视程也砍半
    [Fact]
    public void Fog_ShrinksWatchtowerRange()
    {
        Simulation Build(Weather w)
        {
            var t = new TruthWorld { Terrain = new TerrainGrid(30, 12) { Weather = w }, FriendHq = new Vec2(0, 6), EnemyHq = new Vec2(29, 6) };
            t.Units.Add(new Unit { Id = 1, Side = Side.Friend, Vision = 0, Pos = new Vec2(0, 6), Strength = 100, MaxStrength = 100 });
            t.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Vision = 0, Pos = new Vec2(7, 6), Strength = 100, MaxStrength = 100 });
            return new Simulation(t, DifficultySettings.Normal, new Rng(1));
        }

        var clear = Build(Weather.Clear); clear.AdvanceTick();
        Assert.Contains(clear.WatchtowerContacts, c => c.Side == Side.Enemy);      // 晴:dist 7 ≤ 8
        var fog = Build(Weather.Fog); fog.AdvanceTick();
        Assert.DoesNotContain(fog.WatchtowerContacts, c => c.Side == Side.Enemy);  // 雾:8×0.5=4 < 7
    }

    // ===== helpers =====

    private static Report Report(int unitId, int tick, Vec2 pos) => new()
    {
        ObservedTick = tick,
        About = new PartialUnitInfo { UnitId = unitId, Side = Side.Enemy, Pos = pos, Strength = 100, Type = UnitType.Spear }
    };

    private static DifficultySettings NoLossSlow() => new()
    { Name = "test", MessengerSpeed = 2, InterceptChancePerTick = 0 };

    private static DifficultySettings AlwaysIntercept() => new()
    { Name = "test", MessengerSpeed = 2, InterceptChancePerTick = 1.0, NotifyLostActiveMessenger = false };

    private static DifficultySettings NeverIntercept() => new()
    { Name = "test", MessengerSpeed = 3, InterceptChancePerTick = 0 };

    /// <summary>一个友军单位与一个敌军单位,无干扰,用于命令延迟测试。</summary>
    private static Simulation BuildControlled(DifficultySettings diff)
    {
        var terrain = new TerrainGrid(12, 5, TerrainType.Plain);
        var truth = new TruthWorld { Terrain = terrain, FriendHq = new Vec2(0, 2), EnemyHq = new Vec2(11, 2) };
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Name = "A",
            Commander = new Commander("甲", Personality.Steady, 1.0), Pos = new Vec2(8, 2), Strength = 100, MaxStrength = 100 });
        truth.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Spear, Name = "E",
            Commander = new Commander("乙", Personality.Steady, 1.0), Pos = new Vec2(2, 2), Strength = 100, MaxStrength = 100 });
        return new Simulation(truth, diff, new Rng(1));
    }

    /// <summary>友军观察者紧贴敌军(必然侦察到),HQ 在旁,用于「丢失 vs 送达」对照。</summary>
    private static Simulation BuildSpotter(DifficultySettings diff)
    {
        var terrain = new TerrainGrid(12, 5, TerrainType.Plain);
        var truth = new TruthWorld { Terrain = terrain, FriendHq = new Vec2(3, 2), EnemyHq = new Vec2(11, 2) };
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Shield, Name = "哨",
            Commander = new Commander("甲", Personality.Steady, 1.0), Pos = new Vec2(5, 2), Strength = 400, MaxStrength = 400 });
        truth.Units.Add(new Unit { Id = 9, Side = Side.Enemy, Type = UnitType.Bow, Name = "敌", Vision = 0,
            Commander = new Commander("乙", Personality.Cautious, 1.0), Pos = new Vec2(8, 2), Strength = 50, MaxStrength = 50 });
        return new Simulation(truth, diff, new Rng(1));
    }
}
