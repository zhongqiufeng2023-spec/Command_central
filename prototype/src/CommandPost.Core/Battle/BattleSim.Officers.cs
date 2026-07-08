using System;
using System.Collections.Generic;
using System.Linq;

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
    /// <summary>战毕才揭示的真相清单(复盘面板用):传错的令、没送到的书……「原来如此/原来是我害的」。</summary>
    public List<string> Reveals { get; } = new();

    public static string PersonalityCn(Personality p) => p switch
    {
        Personality.Aggressive => "贪功",
        Personality.Cautious => "持重",
        Personality.Cunning => "善诈",
        _ => "稳健"
    };

    private float _lastAskT = -999f;   // 全军请示错峰(免同刻连环弹横幅)

    /// <summary>
    /// 请示求决(PRD §6.4):守势之将见大敌当面而无战令——遣骑来问「战耶守耶」。
    /// 你回令(任意令到)= 照令;迟迟不回 → 按脾性自处:贪功等不住就上,善诈游走,余者固守。
    /// 你可能根本不知道他在等你拍板(请示骑也会死在路上)。
    /// </summary>
    private void UpdateAskForOrders()
    {
        foreach (var u in Units.Where(x => x is { Side: Side.Friend, Allied: false, AiControlled: false } && x.Controllable))
        {
            var foe = NearestUnit(u.Center, Side.Enemy);
            float fd = foe?.Center.DistanceTo(u.Center) ?? float.MaxValue;

            // 威胁散去良久,方可再启一轮请示
            if (u.AskedOnce && !u.AwaitingReply && fd > 180f && Time - u.LastThreatT > 45f)
                u.AskedOnce = false;

            // 触发:守势姿态 + 无令在身(行军中=有令可依,不请示)+ 可观之敌进入目视
            if (!u.AwaitingReply && !u.AskedOnce && u.Stance is BStance.Hold or BStance.Standby
                && u.Path.Count == 0 && u.State != BUnitState.Engaged
                && foe is { AliveCount: >= 60 }
                && fd < 130f * BattleMap.ConcealMult(Map.At(foe.Center))
                && Time - _lastAskT > 30f)
            {
                u.AwaitingReply = true; u.AskedOnce = true; u.AskedT = Time; _lastAskT = Time;
                var spawn = Map.Clamp(u.Center + (HqPos - u.Center).Normalized * 20f);
                int est = Math.Max(10, (int)Math.Round(foe.AliveCount / 10f) * 10);
                var r = new Rider
                {
                    Id = _nextRider++, Kind = RiderKind.Report, Pos = spawn,
                    Phase = RiderPhase.Return, Path = BattlePath.Find(Map, spawn, HqPos),
                    Depart = Time, RoundTrip = false, EstPath = new List<Vec2F>(),
                    DescCn = $"{u.Name}的请示骑",
                    ReportOwn = Snapshot(u),
                    ArriveAlertCn = $"{u.Officer.Name}({u.Name})遣骑请示:敌约{est}骑步当面,我部战耶守耶?——选中该部,1-4 回令"
                };
                r.Sightings[foe.Id] = new EnemySighting(foe.Center, est, fd < 90f ? foe.Type : null, Time);
                r.ExpectedBack = Time + PathLength(spawn, r.Path) / r.Speed + 15f;
                Riders.Add(r);
            }

            // 候令超时 → 自处(真相侧无声进行;批注随下一封军报才回到你眼前)
            if (u.AwaitingReply && Time - u.AskedT > AskPatience(u.Officer))
            {
                u.AwaitingReply = false;
                u.Stance = u.Officer.Personality switch
                {
                    Personality.Aggressive => BStance.Attack,
                    Personality.Cunning => BStance.Skirmish,
                    _ => BStance.Hold
                };
                u.LastQuirkCn = "请示未复,自行斟酌";
            }
        }
    }

    /// <summary>候令的耐性:贪功者等不了半刻,持重者能等到花谢。</summary>
    private static float AskPatience(Commander o) => o.Personality switch
    { Personality.Aggressive => 35f, Personality.Cautious => 95f, _ => 60f };

    /// <summary>去程失真(PRD §6.5):令骑在路上记错话——错姿态,或目的地传偏。
    /// 真相侧只记入 Reveals(复盘才揭示);战中你只能从复命军报里嗅出不对。</summary>
    private void GarbleOrder(Rider r, BattleUnit u)
    {
        if (!_rng.Chance(Difficulty.OrderGarbleChance)) return;
        if (r.OrderStance is { } st)
        {
            BStance[] all = { BStance.Attack, BStance.Hold, BStance.Standby, BStance.Skirmish };
            var wrong = all[(int)(_rng.NextDouble() * all.Length)];
            if (wrong == st) wrong = st == BStance.Hold ? BStance.Attack : BStance.Hold;
            Reveals.Add($"{FormatT(Time)} 你令{u.Name}「{StanceCnOf(st)}」——令骑在路上记成了「{StanceCnOf(wrong)}」");
            r.OrderStance = wrong;
        }
        else if (r.HasMove)
        {
            float ang = (float)(_rng.NextDouble() * Math.PI * 2);
            var off = new Vec2F(MathF.Cos(ang), MathF.Sin(ang)) * (60f + (float)_rng.NextDouble() * 60f);
            Reveals.Add($"{FormatT(Time)} 给{u.Name}的行军目标,令骑传偏了约 {(int)off.Length} 米");
            r.OrderDest = Map.Clamp(r.OrderDest + off);
        }
    }

    /// <summary>令骑送达 → 武将解读执行。返回是否发生走样(真相侧记账,不广播)。</summary>
    private void ApplyOrderWithTemperament(BattleUnit u, Rider r)
    {
        var o = u.Officer;
        float wild = (float)(1.0 - o.Competence);          // 越不济,走样越大
        u.LastQuirkCn = "";
        u.AwaitingReply = false;                           // 得令了(哪怕令是歪的):不再干等请示

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
