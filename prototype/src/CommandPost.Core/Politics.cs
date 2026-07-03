namespace CommandPost.Core;

/// <summary>
/// 战后政治评语(PRD §8.6 MVP-light):主帅隔着他的雾与政治看你——
/// 评的是「帅帐所知的结果 × 听令 × 代价 × 主帅性格」,不是你的真实功劳。
/// 赢未必受赏,输未必受罚。
/// </summary>
public sealed class Appraisal
{
    /// <summary>主帅信任 0..100(基线 50)。</summary>
    public int Trust { get; init; }
    public string VerdictCn { get; init; } = "";
    /// <summary>逐条评语(含加减分)。</summary>
    public List<string> Lines { get; init; } = new();
}

public static class PoliticalJudge
{
    public static Appraisal Judge(Simulation sim)
    {
        var m = sim.Mission!;
        var lines = new List<string>();
        double trust = 50;

        // 主帅性格权重:尚功者(激进)重驰援轻伤亡;持重者(谨慎)反之。
        double aidW = 1.0, lossW = 1.0;
        if (m.HqPersonality == Personality.Aggressive) { aidW = 1.4; lossW = 0.6; }
        if (m.HqPersonality == Personality.Cautious) { aidW = 0.7; lossW = 1.6; }

        void Add(double d, string cn) { trust += d; lines.Add($"{cn}({(d >= 0 ? "+" : "")}{d:0})"); }

        if (m["A"]?.State == ObjectiveState.Done) Add(8, "侦报翔实,帅帐嘉之");
        else Add(-6, "虏情不明,帅帐以为怠慢");

        if (m["B"]?.State == ObjectiveState.Done) Add(5, "军报通达");
        else Add(-4, "久无回报,帅帐疑尔观望");

        if (m["C"] is { Active: true } c)
        {
            if (c.State == ObjectiveState.Done) Add(15 * aidW, "驰援及时,左翼得全");
            else if (c.State == ObjectiveState.Failed) Add(-18 * aidW, "坐视左翼覆没,帅帐震怒");
            else Add(-10 * aidW, "奉令迟疑,驰援未至");
        }
        if (m["D"] is { Active: true, State: ObjectiveState.Done }) Add(4, "战况上达,帅帐悉之");

        double lossFrac = 1 - sim.PlayerStrengthNow / System.Math.Max(1, sim.PlayerStrengthAtStart);
        if (lossFrac < 0.15) Add(5 * lossW, "所部完好");
        else if (lossFrac > 0.5) Add(-8 * lossW, "损兵折将");

        if (sim.Status == GameStatus.FriendWon) Add(10, "阵斩虏首,虏骑遁去");
        if (sim.Status == GameStatus.EnemyWon) Add(-20, "丧师之责,无可推诿");

        int t = (int)System.Math.Clamp(trust, 0, 100);
        string verdict = t >= 75 ? "记功一转,迁赏有望"
                       : t >= 55 ? "尚可,勉之"
                       : t >= 35 ? "留用察看"
                       : "夺职问罪,槛送京师";
        return new Appraisal { Trust = t, VerdictCn = verdict, Lines = lines };
    }
}
