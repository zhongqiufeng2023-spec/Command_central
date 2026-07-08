using System;
using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

// 战役任务层(逐兵版):中军令时间线 + 目标清单 + 战后政治评语。
// 铁律(支柱①):战中呈现给玩家的目标进度只用「沙盘所知」判定;
// 用真相判定的目标(破敌/保全)战中一律显示未定,战毕才揭晓。

public enum BObjectiveKind { ScoutEnemy, ReportToHq, DefeatEnemy, PreserveArmy, RelieveAlly, ReportAlly }
public enum BObjectiveState { Pending, Partial, Done, Failed }

/// <summary>一条可判定的行营任务目标(逐兵战役版)。</summary>
public sealed class BObjective
{
    public string Id = "";
    public string Cn = "";
    public BObjectiveKind Kind;
    public BObjectiveState State = BObjectiveState.Pending;
    /// <summary>主目标(参与达成度评级)。</summary>
    public bool Primary;
    /// <summary>未被中军令激活时不评估,UI 显示「令未至」。</summary>
    public bool Active = true;
    /// <summary>ScoutEnemy:需侦明的敌部数。</summary>
    public int RequiredCount = 1;
    /// <summary>PreserveArmy:伤亡上限(占开战兵力比)。</summary>
    public float LossLimit = 0.30f;
}

/// <summary>一道中军令:行营(后方帅帐)在 DispatchT 发出,LinkDelay 秒后送达阵前。</summary>
public sealed class BHqOrder
{
    public float DispatchT;
    public float LinkDelay = 20f;
    public string TitleCn = "";
    public string TextCn = "";
    public string[] Activates = Array.Empty<string>();
    public bool Delivered;
    /// <summary>随令附上行营所知的左翼位置(它的旧认知——发令那刻定格,还带偏差)。</summary>
    public bool SeedAllyPos;
    public bool Snapped;
    public Vec2F AllySnapshot;
    public float ArriveT => DispatchT + LinkDelay;
}

/// <summary>
/// 一场逐兵战役的行营任务:分阶段中军令 + 目标 + 战役窗口(到时敌自退)+ 战毕评语。
/// </summary>
public sealed class BattleMission
{
    public string Title = "";
    public List<BObjective> Objectives = new();
    public List<BHqOrder> Orders = new();
    public Personality HqPersonality = Personality.Steady;
    /// <summary>战役窗口(秒):到时虏骑自退,按目标结算(不必全歼)。</summary>
    public float EndTime = float.MaxValue;

    // —— 军书上行的账目(真相侧,评语用;玩家只经回执得知)——
    /// <summary>已送达行营的军书数。</summary>
    public int ReportsDelivered;
    /// <summary>最详实一封军书所载敌情条数(评「所报翔实」)。</summary>
    public int BestReportIntel;
    /// <summary>破敌令送达前就赢了 = 不俟令而战(政治上是把双刃剑)。</summary>
    public bool WonBeforeWarOrder;
    /// <summary>载有左翼战况的军书送达数(报左翼目标)。</summary>
    public int AllyReportsDelivered;
    /// <summary>本部两队以上抵近左翼战地(评「驰援及时」还是「观望」)。</summary>
    public bool RescueArrived;
    public float RescueT;

    /// <summary>战毕评语(null = 战役未毕)。</summary>
    public Appraisal? Verdict;

    public BObjective? this[string id] => Objectives.FirstOrDefault(o => o.Id == id);
    public bool AllPrimaryDone => Objectives.Where(o => o.Primary).All(o => o.State == BObjectiveState.Done);

    /// <summary>战中评估:只动「沙盘可知」的目标(侦明);真相类目标战毕才判。</summary>
    public void Evaluate(BattleSim sim)
    {
        foreach (var o in Objectives)
        {
            if (!o.Active || o.State is BObjectiveState.Done or BObjectiveState.Failed) continue;
            if (o.Kind == BObjectiveKind.ScoutEnemy)
            {
                // 只数真有对应敌部的标记(幻影敌情 id<0 不算「侦明」——报上去也对不上号)
                int known = sim.Sandbox.Enemy.Keys.Count(k => k >= 0);
                o.State = known >= o.RequiredCount ? BObjectiveState.Done
                        : known > 0 ? BObjectiveState.Partial : BObjectiveState.Pending;
            }
        }
    }

    /// <summary>战毕结算:揭晓真相类目标 + 生成评语。幂等(重复调用无副作用)。</summary>
    public void Finish(BattleSim sim)
    {
        if (Verdict != null) return;

        foreach (var o in Objectives)
        {
            switch (o.Kind)
            {
                case BObjectiveKind.DefeatEnemy:
                    // 未奉令而胜也算「破敌」——政治账在评语里另算
                    if (sim.Winner == Side.Friend)
                    {
                        if (!o.Active) WonBeforeWarOrder = true;
                        o.State = BObjectiveState.Done;
                    }
                    else if (o.Active) o.State = BObjectiveState.Failed;
                    break;
                case BObjectiveKind.PreserveArmy:
                    o.State = sim.FriendLossFrac <= o.LossLimit ? BObjectiveState.Done : BObjectiveState.Failed;
                    break;
                case BObjectiveKind.ReportToHq:
                    // 送达即算(回执只是玩家的「得知」;行营确已收到)
                    if (ReportsDelivered > 0) o.State = BObjectiveState.Done;
                    else if (o.Active) o.State = BObjectiveState.Failed;
                    break;
                case BObjectiveKind.RelieveAlly:
                    // 左翼保住了没有——真相说了算(战中你只能靠零星情报揪心)
                    o.State = sim.LeftWingCollapsed ? BObjectiveState.Failed : BObjectiveState.Done;
                    break;
                case BObjectiveKind.ReportAlly:
                    if (AllyReportsDelivered > 0) o.State = BObjectiveState.Done;
                    else if (o.Active) o.State = BObjectiveState.Failed;
                    break;
            }
        }
        Verdict = BattlePoliticalJudge.Judge(sim, this);
    }
}

/// <summary>逐兵战役的任务工厂。</summary>
public static class BattleMissions
{
    /// <summary>
    /// 黑松岭(逐兵版):第一道令(侦明+具报)开战即至;第二道令(破敌)约四分钟后压到;
    /// 第三道令(驰援左翼)七分钟后追至——那时你多半已陷在当面之敌里,进退两难。
    /// 窗口 20 分钟,到时虏自遁,纵虏是罪;左翼李嵩若崩,是硬性败。
    /// </summary>
    public static BattleMission BlackPine() => new()
    {
        Title = "黑松岭 · 侦而后战",
        HqPersonality = Personality.Aggressive,     // 周崇尚功:重战果、轻伤亡
        EndTime = 1200f,
        Objectives =
        {
            new BObjective { Id = "A", Cn = "侦明当面之敌(四部以上)", Kind = BObjectiveKind.ScoutEnemy, Primary = true, Active = false, RequiredCount = 4 },
            new BObjective { Id = "B", Cn = "军书具报行营(按 B 发书)", Kind = BObjectiveKind.ReportToHq, Active = false },
            new BObjective { Id = "C", Cn = "破当面之虏",               Kind = BObjectiveKind.DefeatEnemy, Primary = true, Active = false },
            new BObjective { Id = "D", Cn = "保全士马(折损不逾三成)",   Kind = BObjectiveKind.PreserveArmy },
            new BObjective { Id = "E", Cn = "驰援左翼,保李嵩不崩",       Kind = BObjectiveKind.RelieveAlly, Primary = true, Active = false },
            new BObjective { Id = "F", Cn = "左翼战况,具书上闻",         Kind = BObjectiveKind.ReportAlly, Active = false },
        },
        Orders =
        {
            new BHqOrder { DispatchT = 0, LinkDelay = 8, TitleCn = "第一道令",
                TextCn = "行营谕:虏骑犯境,踪迹未明。速遣塘骑,侦得虏踪,具军书以闻。",
                Activates = new[] { "A", "B" } },
            new BHqOrder { DispatchT = 210, LinkDelay = 30, TitleCn = "第二道令",
                TextCn = "行营再谕:朝廷促战,不欲久师。限尔部即行破虏,毋纵其遁!",
                Activates = new[] { "C" } },
            new BHqOrder { DispatchT = 420, LinkDelay = 30, TitleCn = "第三道令",
                TextCn = "行营急谕:左翼李嵩为虏所迫,其势甚急!尔部速分兵驰援,毋得迁延——所示方位,乃行营所知,或有出入,尔自斟酌。",
                Activates = new[] { "E", "F" }, SeedAllyPos = true },
        }
    };
}

/// <summary>
/// 战后政治评语(逐兵版):行营隔着他的雾与政治看你——
/// 评「行营所知的结果 × 听令 × 代价 × 主帅性格」,不评你的真实辛苦。赢未必受赏。
/// </summary>
public static class BattlePoliticalJudge
{
    public static Appraisal Judge(BattleSim sim, BattleMission m)
    {
        var lines = new List<string>();
        double trust = 50;

        double warW = 1.0, lossW = 1.0;
        if (m.HqPersonality == Personality.Aggressive) { warW = 1.4; lossW = 0.6; }
        if (m.HqPersonality == Personality.Cautious) { warW = 0.7; lossW = 1.6; }

        void Add(double d, string cn) { trust += d; lines.Add($"{cn}({(d >= 0 ? "+" : "")}{d:0})"); }

        if (m["A"]?.State == BObjectiveState.Done) Add(8, "虏情侦得明白,行营嘉之");
        else if (m["A"] is { Active: true }) Add(-6, "虏情不明,行营以为怠慢");

        if (m["B"]?.State == BObjectiveState.Done)
        {
            Add(5, "军书通达");
            if (m.BestReportIntel >= 4) Add(3, "所报翔实,与诸路印证无误");
        }
        else if (m["B"] is { Active: true }) Add(-5, "自开战无一书上闻,行营疑尔观望");

        if (m["C"] is { } c)
        {
            if (c.State == BObjectiveState.Done)
            {
                if (m.WonBeforeWarOrder)
                {
                    if (m.HqPersonality == Personality.Aggressive) Add(18, "不俟令而破敌,周帅壮之");
                    else Add(6, "轻进侥胜,非持重之道");
                }
                else Add(15 * warW, "奉令破虏,阵斩有数");
            }
            else if (c.State == BObjectiveState.Failed)
            {
                if (sim.Winner == Side.Enemy) Add(-20 * warW, "丧师之责,无可推诿");
                else Add(-8 * warW, "纵虏自遁,行营不悦");
            }
        }

        if (m["D"] is { } d2)
        {
            if (d2.State == BObjectiveState.Done) Add(6 * lossW, "士马保全");
            else Add(-8 * lossW, "折损逾三成,枯骨盈野");
        }

        if (m["E"] is { } e5)
        {
            if (e5.State == BObjectiveState.Done)
            {
                if (m.RescueArrived) Add(14, "提兵北援,左翼获全——李嵩具表称谢");
                else if (e5.Active) Add(-6, "左翼幸全,然尔部未至,行营记曰:观望");
            }
            else if (e5.State == BObjectiveState.Failed) Add(-22, "坐视左翼崩覆,罪无可逭");
        }
        if (m["F"]?.State == BObjectiveState.Done) Add(4, "左翼战况上闻,行营得以调度");

        if (sim.Winner == Side.Friend) Add(10, "捷书驰奏京师");

        int t = (int)Math.Clamp(trust, 0, 100);
        string verdict = t >= 75 ? "记功一转,迁赏有望"
                       : t >= 55 ? "尚可,勉之"
                       : t >= 35 ? "留用察看"
                       : "夺职问罪,槛送京师";
        return new Appraisal { Trust = t, VerdictCn = verdict, Lines = lines };
    }
}
