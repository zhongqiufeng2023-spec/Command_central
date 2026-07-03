namespace CommandPost.Core;

/// <summary>关卡/场景工厂。原型只有一场最小遭遇战。</summary>
public static class Scenario
{
    /// <summary>
    /// 最小遭遇战:每方 3 支部队、一张 20×12 小地图(平原 + 一片林 + 一段河)。
    /// 玩家初始知道「我方出发位置与兵力」(免费基线 §3.2),敌情全靠侦察。
    /// </summary>
    public static Simulation MinimalEncounter(DifficultySettings? difficulty = null, int seed = 12345)
    {
        var terrain = new TerrainGrid(20, 12, TerrainType.Plain) { Weather = Weather.Clear };
        for (int y = 2; y <= 6; y++) terrain.Set(new Vec2(9, y), TerrainType.Forest);   // 中部林带
        for (int x = 8; x <= 12; x++) terrain.Set(new Vec2(x, 9), TerrainType.River);    // 南侧河

        var truth = new TruthWorld
        {
            Terrain = terrain,
            FriendHq = new Vec2(0, 6),
            EnemyHq = new Vec2(19, 6)
        };

        // —— 我方(左)——
        truth.Units.Add(new Unit { Id = 1, Side = Side.Friend, Type = UnitType.Spear, Name = "左翼",
            Commander = new Commander("赵铁", Personality.Aggressive, 0.65), Pos = new Vec2(2, 2), Strength = 100, MaxStrength = 100 });
        truth.Units.Add(new Unit { Id = 2, Side = Side.Friend, Type = UnitType.Shield, Name = "中军",
            Commander = new Commander("钱稳", Personality.Steady, 0.85), Pos = new Vec2(2, 6), Strength = 120, MaxStrength = 120 });
        truth.Units.Add(new Unit { Id = 3, Side = Side.Friend, Type = UnitType.Cavalry, Name = "右翼",
            Commander = new Commander("孙锐", Personality.Cautious, 0.75), Pos = new Vec2(2, 10), Strength = 80, MaxStrength = 80 });

        // —— 敌方(右)——
        truth.Units.Add(new Unit { Id = 4, Side = Side.Enemy, Type = UnitType.Cavalry, Name = "敌骑",
            Commander = new Commander("胡狼", Personality.Aggressive, 0.7), Pos = new Vec2(17, 3), Strength = 90, MaxStrength = 90 });
        truth.Units.Add(new Unit { Id = 5, Side = Side.Enemy, Type = UnitType.Bow, Name = "敌弩",
            Commander = new Commander("乌目", Personality.Cunning, 0.7), Pos = new Vec2(17, 6), Strength = 80, MaxStrength = 80 });
        truth.Units.Add(new Unit { Id = 6, Side = Side.Enemy, Type = UnitType.Spear, Name = "敌枪",
            Commander = new Commander("石头", Personality.Steady, 0.7), Pos = new Vec2(17, 9), Strength = 110, MaxStrength = 110 });

        var sim = new Simulation(truth, difficulty ?? DifficultySettings.Normal, new Rng(seed));

        // 各方都「知道自己出发时的部队」(免费基线),观测于 t0。
        SeedOwnBelief(sim, Side.Friend);
        SeedOwnBelief(sim, Side.Enemy);
        return sim;
    }

    /// <summary>
    /// 第一关《黑松岭》:玩家 = 大酆前锋一路副将(6 队,每队 ≤250,各配武将、单一主兵种),
    /// 对阵草原部落一路(不对称:骑射为主 + 突骑 + 部众)。全雾开局(只知己方),敌情全靠斥候。
    /// v2:加左翼友军(他路,不可指挥)+ 草原袭击队(剧本:aidOrderTick-40 扑向左翼)+
    /// 分阶段中军令(t0 侦察令;aidOrderTick 驰援令)。aidOrderTick 默认 300(实验压缩;设计值 1200)。
    /// </summary>
    public static Simulation FirstBattleBlackPine(DifficultySettings? difficulty = null, int seed = 20250629, int aidOrderTick = 300)
    {
        var terrain = new TerrainGrid(24, 14, TerrainType.Plain) { Weather = Weather.Clear };
        for (int y = 4; y <= 9; y++) terrain.Set(new Vec2(12, y), TerrainType.Forest);   // 黑松岭:中部林带
        for (int x = 11; x <= 14; x++) terrain.Set(new Vec2(x, 11), TerrainType.River);

        var truth = new TruthWorld
        {
            Terrain = terrain,
            FriendHq = new Vec2(1, 7),    // 你的本路中军帐(在你阵线之后)
            EnemyHq = new Vec2(23, 7)
        };

        int id = 1;
        void Feng(UnitType t, string name, string cmdr, Personality p, double comp, Vec2 pos, double str) =>
            truth.Units.Add(new Unit { Id = id++, Side = Side.Friend, Faction = Faction.Dafeng, Type = t, Name = name,
                Commander = new Commander(cmdr, p, comp), Pos = pos, Strength = str, MaxStrength = str });
        // 大酆 · 你这一路(6 队)
        Feng(UnitType.Spear,      "枪队",   "王朗",   Personality.Steady,     0.80, new Vec2(3, 3), 240);
        Feng(UnitType.MoDao,      "陌刀队", "何武",   Personality.Steady,     0.82, new Vec2(3, 5), 220);
        Feng(UnitType.Shield,     "盾队",   "周石",   Personality.Cautious,   0.78, new Vec2(3, 7), 250);
        Feng(UnitType.Bow,        "弩队",   "李彀",   Personality.Cautious,   0.75, new Vec2(2, 9), 200);
        Feng(UnitType.Cavalry,    "游骑",   "秦锐",   Personality.Aggressive, 0.70, new Vec2(4, 11), 160);
        Feng(UnitType.Cataphract, "铁骑",   "呼延豹", Personality.Aggressive, 0.72, new Vec2(4, 1), 180);

        // 左翼友军(他路,刘都尉部):不可指挥、据阵自守;它的存亡就是第二道令的题眼。
        var wingAnchor = new Vec2(9, 2);
        void Wing(UnitType t, string name, string cmdr, Vec2 pos, double str) =>
            truth.Units.Add(new Unit { Id = id++, Side = Side.Friend, Faction = Faction.Dafeng, Type = t, Name = name,
                Commander = new Commander(cmdr, Personality.Steady, 0.75), Pos = pos, Strength = str, MaxStrength = str,
                PlayerLed = false, Order = Intent.Hold() });
        Wing(UnitType.Spear,  "左翼枪", "刘整", new Vec2(9, 2),  220);
        Wing(UnitType.Shield, "左翼盾", "陈苍", new Vec2(10, 3), 200);

        void Hu(UnitType t, string name, string cmdr, Personality p, Vec2 pos, double str) =>
            truth.Units.Add(new Unit { Id = id++, Side = Side.Enemy, Faction = Faction.Steppe, Type = t, Name = name,
                Commander = new Commander(cmdr, p, 0.68), Pos = pos, Strength = str, MaxStrength = str });
        // 草原部落 · 当面之敌(不对称:骑射为主)
        Hu(UnitType.HorseArcher, "骑射", "阿史那", Personality.Cunning,    new Vec2(21, 4), 200);
        Hu(UnitType.HorseArcher, "骑射", "咄陆",   Personality.Cunning,    new Vec2(21, 10), 200);
        Hu(UnitType.NomadLancer, "突骑", "俟斤",   Personality.Aggressive, new Vec2(22, 7), 220);
        Hu(UnitType.TribalFoot,  "部众", "杂胡",   Personality.Steady,     new Vec2(23, 7), 240);

        // 草原袭击队(剧本接管,AiExempt):按时辰扑向左翼——催生第二道「驰援」令。
        int raider1 = id, raider2 = id + 1;
        Hu(UnitType.NomadLancer, "突骑", "拔野古", Personality.Aggressive, new Vec2(21, 1), 230);
        Hu(UnitType.HorseArcher, "骑射", "同罗",   Personality.Cunning,    new Vec2(22, 2), 180);
        foreach (var rid in new[] { raider1, raider2 }) truth.UnitById(rid)!.AiExempt = true;

        var sim = new Simulation(truth, difficulty ?? DifficultySettings.Normal, new Rng(seed));
        SeedOwnBelief(sim, Side.Friend);
        SeedOwnBelief(sim, Side.Enemy);
        sim.Mission = Missions.BlackPine(aidOrderTick, wingAnchor);
        sim.ScheduledOrders.Add((aidOrderTick - 40, raider1, Intent.Move(wingAnchor, Tone.Aggressive)));
        sim.ScheduledOrders.Add((aidOrderTick - 40, raider2, Intent.Move(new Vec2(10, 3), Tone.Aggressive)));
        return sim;
    }

    private static void SeedOwnBelief(Simulation sim, Side side)
    {
        var belief = sim.Beliefs[side];
        foreach (var u in sim.Truth.Units.Where(u => u.Side == side))
        {
            belief.Known[u.Id] = new GhostUnit
            {
                UnitId = u.Id,
                Side = side,
                LastKnownPos = u.Pos,
                KnownStrength = u.Strength,
                KnownType = u.Type,
                ObservedTick = 0
            };
        }
    }
}
