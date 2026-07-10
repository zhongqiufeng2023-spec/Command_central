using System;

namespace CommandPost.Core;

// 遭遇战工厂:在哪儿接的战,就在哪儿的地上打——
// 战场按战略层地形类别(平野/林地/丘陵/泽地)程序化生成,敌军编成按其兵力捏出来。
// 与黑松岭剧设战不同:没有左翼防线、没有分阶段中军令——一场野地里的硬碰硬。
public static partial class BattleScenario
{
    private static readonly string[] HorseArcherLeaders = { "阿贪", "吐迷", "贺遂", "悉万", "厍狄" };
    private static readonly string[] LancerLeaders = { "叱利", "乙旃", "斛律", "库莫", "宇文" };
    private static readonly string[] FootLeaders = { "乌洛", "是云", "拔列", "悉罗", "独孤" };

    /// <summary>
    /// 野地遭遇战:本部(西)对一股虏骑(东,兵力 foeMen 决定编成规模)。
    /// allyMen>0 = 官军友邻已在场中(它先接上的仗,你是撞进来的)——
    /// 友邻不归你辖、其崩溃不判硬败。
    /// </summary>
    public static BattleSim Encounter(BattleGround ground, int seed, int[]? ownCounts = null,
        int foeMen = 420, int allyMen = 0, bool deploy = true)
    {
        var rng = new Rng(seed);
        var map = BuildFieldMap(ground, rng);
        float midY = map.WorldH / 2f;
        var sim = new BattleSim(map, new Rng(seed + 1))
        {
            HqPos = new Vec2F(56, midY),
            AlliedCollapseIsDefeat = false
        };

        // —— 本部六队(建军序列与黑松岭同——同一支军队,损耗跨战延续)——
        var own = new (UnitType type, string name, string officer, Personality per, double cap, Vec2F pos)[]
        {
            (UnitType.Spear,      "枪队",   "王朗",   Personality.Steady,     0.80, new Vec2F(150, midY - 120)),
            (UnitType.MoDao,      "陌刀队", "何武",   Personality.Steady,     0.82, new Vec2F(150, midY - 55)),
            (UnitType.Shield,     "盾队",   "周石",   Personality.Cautious,   0.78, new Vec2F(150, midY + 10)),
            (UnitType.Bow,        "弩队",   "李彀",   Personality.Cautious,   0.75, new Vec2F(110, midY + 70)),
            (UnitType.Cavalry,    "游骑",   "秦锐",   Personality.Aggressive, 0.70, new Vec2F(175, midY + 140)),
            (UnitType.Cataphract, "铁骑",   "呼延豹", Personality.Aggressive, 0.72, new Vec2F(175, midY - 185)),
        };
        for (int i = 0; i < own.Length; i++)
        {
            int cnt = ownCounts != null && i < ownCounts.Length
                ? Math.Clamp(ownCounts[i], 0, OwnFullStrength[i])
                : OwnFullStrength[i];
            if (cnt < 15) continue;                    // 残不成队:缺编
            var o = own[i];
            sim.AddUnit(Side.Friend, o.type, o.name, new Commander(o.officer, o.per, o.cap), o.pos, cnt);
        }

        // —— 虏军编成:按兵力捏队(骑射惯当游哨先动,突骑部众押后)——
        foeMen = Math.Max(120, foeMen);
        int nFoe = Math.Clamp(foeMen / 150, 2, 6);
        for (int i = 0; i < nFoe; i++)
        {
            int men = foeMen / nFoe + rng.Next(-15, 16);
            var (type, pool, cn, per) = (i % 3) switch
            {
                0 => (UnitType.HorseArcher, HorseArcherLeaders, "骑射", Personality.Cunning),
                1 => (UnitType.NomadLancer, LancerLeaders, "突骑", Personality.Aggressive),
                _ => (UnitType.TribalFoot, FootLeaders, "部众", Personality.Steady),
            };
            var pos = new Vec2F(map.WorldW - 130 - rng.Next(0, 70),
                                midY + (i - (nFoe - 1) / 2f) * 85f + rng.Next(-18, 19));
            var u = sim.AddUnit(Side.Enemy, type, cn, new Commander(pool[rng.Next(pool.Length)], per, 0.5 + rng.NextDouble() * 0.15),
                map.Clamp(pos), Math.Max(60, men), ai: true);
            u.HoldUntilT = i < nFoe / 2 ? 60f : 200f;  // 分梯次:游哨先动——斥候来得及往返
            u.ScriptTarget = new Vec2F(280, pos.Y);
        }

        // —— 官军友邻(可选):它在场中缠着虏骑——你入阵时仗已经开了 ——
        if (allyMen >= 60)
        {
            int units = allyMen >= 240 ? 2 : 1;
            for (int i = 0; i < units; i++)
            {
                var pos = new Vec2F(map.WorldW * 0.56f, midY - 200f + i * 70f);
                sim.AddUnit(Side.Friend, UnitType.Cavalry, $"官军巡骑{(units > 1 ? (i == 0 ? "前队" : "后队") : "")}",
                    new Commander(i == 0 ? "郭进" : "曹翰", Personality.Steady, 0.6),
                    map.Clamp(pos), allyMen / units, allied: true);
            }
            sim.Feed("官军巡骑正与虏缠斗于场中——它不归你辖,死活各安天命。");
        }

        sim.Mission = BattleMissions.Encounter(ground, nFoe, allyMen >= 60);
        sim.SpiesAvailable = 0;                        // 遭遇仓促,来不及安插细作
        sim.Feed($"塘骑来报:虏骑一股当面而来,众约{(foeMen + 50) / 100}百——地在{WorldGen.GroundCn(ground)}。");
        if (deploy)
        {
            sim.BeginDeploy(20 * BattleMap.TileSize);
            sim.Feed("布阵:选部右键摆位,Shift+右键面授预令(开战即动),1234 定姿态,回车开战。");
        }
        return sim;
    }

    /// <summary>按地形类别程序化生成野战地图(72×40 瓦片 ≈ 1152×640 米,确定性)。</summary>
    private static BattleMap BuildFieldMap(BattleGround g, Rng rng)
    {
        const int W = 72, H = 40;
        var m = new BattleMap(W, H);

        void Blob(BTerrain t, int w, int h, int minX = 2)
        {
            int x = rng.Next(minX, W - w - 2), y = rng.Next(2, H - h - 2);
            m.Paint(x, y, x + w, y + h, t);
        }

        switch (g)
        {
            case BattleGround.Forest:
                // 大林带纵贯中部(锯齿边),林间留谷口
                for (int ty = 0; ty < H; ty++)
                {
                    int wobL = (ty * 13) % 6, wobR = (ty * 7) % 5;
                    m.Paint(28 - wobL / 2, ty, 40 + wobR, ty, BTerrain.Forest);
                }
                m.Paint(31, H / 2 - 3, 36, H / 2 + 1, BTerrain.Grass);
                for (int i = 0; i < 3; i++) Blob(BTerrain.Forest, 4 + rng.Next(4), 3 + rng.Next(3));
                Blob(BTerrain.Hill, 4, 3);
                break;

            case BattleGround.Hills:
                // 岭地:成片丘坡,谷道穿行
                for (int i = 0; i < 7; i++) Blob(BTerrain.Hill, 5 + rng.Next(5), 3 + rng.Next(4));
                for (int i = 0; i < 2; i++) Blob(BTerrain.Forest, 3 + rng.Next(3), 2 + rng.Next(3));
                break;

            case BattleGround.Marsh:
                // 泽国:大片泥泞 + 一条河两处渡——想过去,得走烂地或挤渡口
                for (int i = 0; i < 9; i++) Blob(BTerrain.Marsh, 5 + rng.Next(6), 3 + rng.Next(4));
                {
                    int rx = 34 + rng.Next(8);
                    m.Paint(rx, 0, rx + 1, H - 1, BTerrain.River);
                    int fy1 = 6 + rng.Next(6), fy2 = H - 12 + rng.Next(6);
                    m.Paint(rx, fy1, rx + 1, fy1 + 1, BTerrain.Ford);
                    m.Paint(rx, fy2, rx + 1, fy2 + 1, BTerrain.Ford);
                }
                break;

            default:
                // 平野:开阔地,疏林缓丘点缀——骑兵的天下
                for (int i = 0; i < 3; i++) Blob(BTerrain.Forest, 4 + rng.Next(4), 3 + rng.Next(3));
                for (int i = 0; i < 2; i++) Blob(BTerrain.Hill, 4 + rng.Next(3), 3 + rng.Next(2));
                break;
        }

        // 官道:东西贯中(最后画,泽地里是条堤道;遭遇多半就发生在道上)
        int ry = H / 2 + rng.Next(-2, 3);
        m.Paint(0, ry, W - 1, ry + 1, BTerrain.Road);

        return m;
    }
}
