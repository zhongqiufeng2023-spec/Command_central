using System;

namespace CommandPost.Core;

/// <summary>战斗B档关卡工厂。</summary>
public static class BattleScenario
{
    /// <summary>原内容整体下移的行数(北面扩出的左翼战地)。</summary>
    private const int NorthRows = 16;
    private const float NorthShift = NorthRows * BattleMap.TileSize;   // 256m

    /// <summary>
    /// 黑松岭之战(逐兵版):大酆前锋一路(6 队 ≈1250 人,西)对草原一路(6 队 ≈1010 骑步,远东);
    /// 北面高地另有左翼友军李嵩一路(≈600,NPC)——左翼之敌(≈940)自东北压上,
    /// 李嵩告急、行营促援,你陷在当面之敌里进退两难(关卡文档 §4-6)。
    /// 地图 96×50 瓦片(≈1536×800 米)——战场够大,斥候来得及往返:
    /// 敌军分梯次进兵(骑射游哨先动、突骑部众后继),不再一开场就压到家门口。
    /// 玩家在西侧帅帐,只看沙盘;敌军与友邻均 AI 驱动。
    /// </summary>
    public static BattleSim BlackPineField(int seed = 20260703, bool deploy = true)
    {
        var sim = new BattleSim(BuildMap(), new Rng(seed)) { HqPos = new Vec2F(60, 272 + NorthShift) };

        // —— 大酆(西,本路)——
        sim.AddUnit(Side.Friend, UnitType.Spear,      "枪队",   new Commander("王朗",   Personality.Steady,     0.80), S(150, 190), 240);
        sim.AddUnit(Side.Friend, UnitType.MoDao,      "陌刀队", new Commander("何武",   Personality.Steady,     0.82), S(150, 250), 220);
        sim.AddUnit(Side.Friend, UnitType.Shield,     "盾队",   new Commander("周石",   Personality.Cautious,   0.78), S(150, 310), 250);
        sim.AddUnit(Side.Friend, UnitType.Bow,        "弩队",   new Commander("李彀",   Personality.Cautious,   0.75), S(110, 350), 200);
        sim.AddUnit(Side.Friend, UnitType.Cavalry,    "游骑",   new Commander("秦锐",   Personality.Aggressive, 0.70), S(170, 420), 160);
        sim.AddUnit(Side.Friend, UnitType.Cataphract, "铁骑",   new Commander("呼延豹", Personality.Aggressive, 0.72), S(170, 120), 180);

        // —— 草原(远东,AI;远来劫掠的疲师:分梯次进兵——游哨先动,主力后继)——
        foreach (var (type, name, cn, per, cap, pos, cnt, holdT) in new[]
        {
            (UnitType.HorseArcher, "阿史那", "骑射", Personality.Cunning,    0.55, S(1350, 160), 150, 180f),
            (UnitType.HorseArcher, "咄陆",   "骑射", Personality.Cunning,    0.55, S(1350, 390), 150, 180f),
            (UnitType.HorseArcher, "同罗",   "骑射", Personality.Cunning,    0.55, S(1400, 400), 130, 180f),
            (UnitType.NomadLancer, "俟斤",   "突骑", Personality.Aggressive, 0.55, S(1280, 260), 190, 300f),
            (UnitType.NomadLancer, "拔野古", "突骑", Personality.Aggressive, 0.55, S(1280, 110), 180, 300f),
            (UnitType.TribalFoot,  "杂胡",   "部众", Personality.Steady,     0.55, S(1210, 300), 200, 300f),
        })
        {
            var u = sim.AddUnit(Side.Enemy, type, cn, new Commander(name, per, cap), pos, cnt, ai: true);
            u.HoldUntilT = holdT;
            // 各有将令:压向大酆正面防线(不被北面左翼战况吸走;近了或接敌才转常态)
            u.ScriptTarget = new Vec2F(280, pos.Y);
        }

        // —— 左翼友军:李嵩一路(北面高地,NPC——不归你辖,不进你沙盘;它的死活要靠情报)——
        sim.AddUnit(Side.Friend, UnitType.Spear,  "李嵩枪队", new Commander("李嵩", Personality.Steady,   0.64), new Vec2F(250, 85),  230, allied: true);
        sim.AddUnit(Side.Friend, UnitType.Shield, "李嵩盾队", new Commander("赵衡", Personality.Cautious, 0.58), new Vec2F(230, 118), 220, allied: true);
        sim.AddUnit(Side.Friend, UnitType.Bow,    "李嵩弩队", new Commander("孙集", Personality.Steady,   0.52), new Vec2F(212, 72),  150, allied: true);

        // —— 左翼之敌:另一路虏军(东北蛰伏,五分钟后压上——正压着李嵩打的那一路)——
        foreach (var (type, name, cn, per, cap, pos, cnt) in new[]
        {
            (UnitType.NomadLancer, "贺鲁", "突骑", Personality.Aggressive, 0.62, new Vec2F(920, 70),  200),
            (UnitType.NomadLancer, "车鼻", "突骑", Personality.Aggressive, 0.60, new Vec2F(942, 112), 180),
            (UnitType.TribalFoot,  "思结", "部众", Personality.Steady,     0.58, new Vec2F(968, 88),  330),
            (UnitType.HorseArcher, "仆固", "骑射", Personality.Cunning,    0.60, new Vec2F(920, 132), 160),
        })
        {
            var u = sim.AddUnit(Side.Enemy, type, cn, new Commander(name, per, cap), pos, cnt, ai: true);
            u.HoldUntilT = 330f;                       // 蛰伏:开战五分半后才动
            u.ScriptTarget = new Vec2F(250, 90);       // 目标:李嵩的高地
        }

        sim.Mission = BattleMissions.BlackPine();
        sim.SpiesAvailable = 1;                        // 战前可遣一名细作混入敌军
        sim.Feed("帅帐军情:虏骑现于黑松岭以东,踪迹未明——地远,塘骑来回要些工夫,早派早知道。");
        sim.Feed("左翼:李嵩一路屯北面高地,与尔部互为犄角——北径可通其营。");
        if (deploy)
        {
            sim.BeginDeploy(22 * BattleMap.TileSize);   // 布阵区:西线自家地界(黑松林以西)
            sim.Feed("布阵:选部右键摆位,Shift+右键面授预令(开战即动),1234 定姿态,K 遣细作,回车开战。");
        }
        return sim;
    }

    /// <summary>原坐标平移:旧图内容整体下移 256m(北面让给左翼战地)。</summary>
    private static Vec2F S(float x, float y) => new(x, y + NorthShift);

    /// <summary>
    /// 黑松岭地貌(代码手绘,确定性)。北面 16 行 = 左翼战地;东半 = 开阔的接敌纵深。
    /// 想换成手编地图:BattleMap.FromAscii(读入 .txt),或后续接 Tiled/LDtk/TileMap 的导出。
    /// </summary>
    private static BattleMap BuildMap()
    {
        var m = new BattleMap(96, 34 + NorthRows);
        int n = NorthRows;

        // ===== 北面:左翼战地 =====
        m.Paint(12, 3, 17, 8, BTerrain.Hill);          // 李嵩的高地(居高守御)
        m.Paint(32, 2, 38, 6, BTerrain.Forest);        // 北林:左翼之敌来路上的遮蔽
        m.Paint(52, 2, 58, 7, BTerrain.Forest);        // 东北疏林

        // ===== 西半:原黑松岭战场(整体下移 n 行)=====

        // 黑松岭:纵向林带(参差的锯齿边缘)——战场西段的分界岭
        for (int ty = 0; ty <= 24; ty++)
        {
            int wobL = (ty * 13) % 5, wobR = (ty * 7) % 4;
            m.Paint(26 - wobL / 2, ty + n, 33 + wobR, ty + n, BTerrain.Forest);
        }
        // 林间空地(伏兵谷口)
        m.Paint(28, 10 + n, 31, 13 + n, BTerrain.Grass);

        // 官道:东西横贯,穿林而过
        m.Paint(0, 16 + n, 95, 17 + n, BTerrain.Road);

        // 南河 + 三处渡滩(东段多一处)
        m.Paint(0, 27 + n, 95, 28 + n, BTerrain.River);
        m.Paint(14, 27 + n, 15, 28 + n, BTerrain.Ford);
        m.Paint(44, 27 + n, 45, 28 + n, BTerrain.Ford);
        m.Paint(74, 27 + n, 75, 28 + n, BTerrain.Ford);

        // 丘陵:东北高地、西南缓丘
        m.Paint(45, 4 + n, 53, 9 + n, BTerrain.Hill);
        m.Paint(6, 20 + n, 12, 25 + n, BTerrain.Hill);

        // 西缘矮丘:玩家阵前的防守锚点(居高临下,受击减伤 ×0.85——摆盾枪在此,稳)
        m.Paint(8, 8 + n, 12, 12 + n, BTerrain.Hill);

        // 疏林数丛:两翼遮蔽与伏兵位
        m.Paint(17, 3 + n, 20, 5 + n, BTerrain.Forest);     // 北翼小林
        m.Paint(19, 20 + n, 22, 22 + n, BTerrain.Forest);   // 官道南小林
        m.Paint(40, 20 + n, 43, 23 + n, BTerrain.Forest);   // 东南疏林

        // ===== 东半:接敌纵深(斥候的舞台) =====
        m.Paint(62, 6 + n, 70, 12 + n, BTerrain.Hill);      // 东岭高地(敌骑来路的瞭望死角)
        m.Paint(58, 20 + n, 63, 24 + n, BTerrain.Forest);   // 官道南东林
        m.Paint(76, 2 + n, 84, 8 + n, BTerrain.Forest);     // 东北松林(虏营屏障)
        m.Paint(84, 20 + n, 90, 25 + n, BTerrain.Hill);     // 东南缓丘

        // 北径:纵贯南北接上官道(最后画,免被林丘盖掉)——驰援左翼走这条快
        m.Paint(20, 3, 21, 16 + n, BTerrain.Road);

        return m;
    }
}
