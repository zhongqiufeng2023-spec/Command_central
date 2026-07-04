using System;

namespace CommandPost.Core;

/// <summary>战斗B档关卡工厂。</summary>
public static class BattleScenario
{
    /// <summary>
    /// 黑松岭之战(逐兵版):大酆前锋一路(6 队 ≈1250 人,西)对草原一路(6 队 ≈1270 骑步,东)。
    /// 地图 60×34 瓦片(≈960×544 米):中部黑松林带、横贯官道、南河两滩、东北/西南丘陵。
    /// 玩家在西侧帅帐,只看沙盘;敌军 AI 驱动。
    /// </summary>
    public static BattleSim BlackPineField(int seed = 20260703)
    {
        var sim = new BattleSim(BuildMap(), new Rng(seed)) { HqPos = new Vec2F(60, 272) };

        // —— 大酆(西,本路)——
        sim.AddUnit(Side.Friend, UnitType.Spear,      "枪队",   new Commander("王朗",   Personality.Steady,     0.80), new Vec2F(150, 190), 240);
        sim.AddUnit(Side.Friend, UnitType.MoDao,      "陌刀队", new Commander("何武",   Personality.Steady,     0.82), new Vec2F(150, 250), 220);
        sim.AddUnit(Side.Friend, UnitType.Shield,     "盾队",   new Commander("周石",   Personality.Cautious,   0.78), new Vec2F(150, 310), 250);
        sim.AddUnit(Side.Friend, UnitType.Bow,        "弩队",   new Commander("李彀",   Personality.Cautious,   0.75), new Vec2F(110, 350), 200);
        sim.AddUnit(Side.Friend, UnitType.Cavalry,    "游骑",   new Commander("秦锐",   Personality.Aggressive, 0.70), new Vec2F(170, 420), 160);
        sim.AddUnit(Side.Friend, UnitType.Cataphract, "铁骑",   new Commander("呼延豹", Personality.Aggressive, 0.72), new Vec2F(170, 120), 180);

        // —— 草原(东,AI;劫掠偏师,兵力略逊——但骑射风筝仍要用脑子解)——
        sim.AddUnit(Side.Enemy, UnitType.HorseArcher, "骑射", new Commander("阿史那", Personality.Cunning,    0.68), new Vec2F(810, 160), 170, ai: true);
        sim.AddUnit(Side.Enemy, UnitType.HorseArcher, "骑射", new Commander("咄陆",   Personality.Cunning,    0.68), new Vec2F(810, 390), 170, ai: true);
        sim.AddUnit(Side.Enemy, UnitType.NomadLancer, "突骑", new Commander("俟斤",   Personality.Aggressive, 0.68), new Vec2F(860, 260), 200, ai: true);
        sim.AddUnit(Side.Enemy, UnitType.TribalFoot,  "部众", new Commander("杂胡",   Personality.Steady,     0.68), new Vec2F(890, 300), 220, ai: true);
        sim.AddUnit(Side.Enemy, UnitType.NomadLancer, "突骑", new Commander("拔野古", Personality.Aggressive, 0.68), new Vec2F(860, 110), 200, ai: true);
        sim.AddUnit(Side.Enemy, UnitType.HorseArcher, "骑射", new Commander("同罗",   Personality.Cunning,    0.68), new Vec2F(890, 440), 150, ai: true);

        sim.Feed("帅帐军情:虏骑现于黑松岭以东,兵力不详。各部就位,听令而动。");
        return sim;
    }

    /// <summary>
    /// 黑松岭地貌(代码手绘,确定性)。想换成手编地图:BattleMap.FromAscii(读入 .txt),
    /// 或后续接 Tiled/LDtk 的 CSV 导出(一层小转换即可)。
    /// </summary>
    private static BattleMap BuildMap()
    {
        var m = new BattleMap(60, 34);

        // 黑松岭:中部纵向林带(参差的锯齿边缘)
        for (int ty = 0; ty <= 24; ty++)
        {
            int wobL = (ty * 13) % 5, wobR = (ty * 7) % 4;
            m.Paint(26 - wobL / 2, ty, 33 + wobR, ty, BTerrain.Forest);
        }
        // 林间空地(伏兵谷口)
        m.Paint(28, 10, 31, 13, BTerrain.Grass);

        // 官道:东西横贯,穿林而过
        m.Paint(0, 16, 59, 17, BTerrain.Road);

        // 南河 + 两处渡滩
        m.Paint(0, 27, 59, 28, BTerrain.River);
        m.Paint(14, 27, 15, 28, BTerrain.Ford);
        m.Paint(44, 27, 45, 28, BTerrain.Ford);
        // 河南岸滩涂草地保留

        // 丘陵:东北高地、西南缓丘
        m.Paint(45, 4, 53, 9, BTerrain.Hill);
        m.Paint(6, 20, 12, 25, BTerrain.Hill);

        return m;
    }
}
