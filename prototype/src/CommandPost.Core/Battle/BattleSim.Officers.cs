using System;

namespace CommandPost.Core;

// 武将解读层(第二层迷雾,蓝图 §十 / PRD §6.2):
// 令骑送达的命令,武将按「送达那刻的实情 + 自己的脾性」解读执行——
//   贪功(Aggressive):敌在近前就压上去打,收着的令也敢自作主张;
//   持重(Cautious) :不肯把阵脚送到敌人嘴边,人马乏了不肯疾驰;
//   善诈(Cunning)  :远路不走直线,爱绕侧翼;
//   稳健(Steady)   :照令执行,分毫不差——用熟了他,迷雾就小(养成的意义)。
// 走样「批注」只随军报/探问回来才被你看见:你下的令和他做的事之间,隔着一层人。
// 战前布阵(当面吩咐)不走这层——面对面说话,没有解读的余地。
public sealed partial class BattleSim
{
    public static string PersonalityCn(Personality p) => p switch
    {
        Personality.Aggressive => "贪功",
        Personality.Cautious => "持重",
        Personality.Cunning => "善诈",
        _ => "稳健"
    };

    /// <summary>令骑送达 → 武将解读执行。返回是否发生走样(真相侧记账,不广播)。</summary>
    private void ApplyOrderWithTemperament(BattleUnit u, Rider r)
    {
        var o = u.Officer;
        float wild = (float)(1.0 - o.Competence);          // 越不济,走样越大
        u.LastQuirkCn = "";

        // —— 姿态令的解读 ——
        if (r.OrderStance is { } st)
        {
            var eff = st;
            bool foeAtHand = Time - u.LastThreatT < 12f;
            if (o.Personality == Personality.Aggressive && st is BStance.Standby or BStance.Hold
                && foeAtHand && _rng.Chance(0.35 + wild * 0.40))
            { eff = BStance.Attack; u.LastQuirkCn = "敌在眼前,抗令喊杀"; }
            else if (o.Personality == Personality.Cautious && st == BStance.Attack
                && u.LossFrac > 0.35f && _rng.Chance(0.30 + wild * 0.40))
            { eff = BStance.Hold; u.LastQuirkCn = "伤亡已重,畏缩据守"; }
            u.Stance = eff;
        }

        // —— 移动令的解读 ——
        if (r.HasMove)
        {
            var dest = r.OrderDest;
            bool run = r.OrderRun;
            switch (o.Personality)
            {
                case Personality.Aggressive:
                {
                    // 目的地附近有敌 → 不停在你画的点上,直接压到敌跟前
                    var foe = NearestUnit(dest, u.Side == Side.Friend ? Side.Enemy : Side.Friend);
                    if (foe != null && foe.Center.DistanceTo(dest) < 220f)
                    {
                        float over = 30f + wild * 55f;
                        dest = Map.Clamp(dest + (foe.Center - dest).Normalized * over);
                        run = true;
                        if (u.LastQuirkCn == "") u.LastQuirkCn = "贪功压前";
                    }
                    break;
                }
                case Personality.Cautious:
                {
                    // 你把他往敌人嘴边画 → 止步于稍后;人马乏了,疾进令当常行军走
                    var foe = NearestUnit(dest, u.Side == Side.Friend ? Side.Enemy : Side.Friend);
                    if (foe != null && foe.Center.DistanceTo(dest) < 100f)
                    {
                        dest = Map.Clamp(dest + (dest - foe.Center).Normalized * (25f + wild * 45f));
                        if (u.LastQuirkCn == "") u.LastQuirkCn = "止步敌前";
                    }
                    if (run && u.Stamina < 55f)
                    { run = false; if (u.LastQuirkCn == "") u.LastQuirkCn = "惜力缓行"; }
                    break;
                }
                case Personality.Cunning:
                {
                    // 远路不走直线:偏出一肩侧翼,再折向目标
                    if (dest.DistanceTo(u.Center) > 140f && _rng.Chance(0.5))
                    {
                        var dir = (dest - u.Center).Normalized;
                        float side = _rng.Chance(0.5) ? 1f : -1f;
                        dest = Map.Clamp(dest + dir.Perp * (side * (40f + wild * 45f)));
                        if (u.LastQuirkCn == "") u.LastQuirkCn = "绕行侧翼";
                    }
                    break;
                }
                // Steady:照令执行
            }
            u.SetOrderDirect(dest, run, Map);
        }
    }
}
