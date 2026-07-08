namespace CommandPost.Core;

/// <summary>难度预设档(PRD §11:一键设定整个参数向量,不做自定义滑块)。</summary>
public enum BDifficulty { Easy, Normal, Hardcore }

/// <summary>
/// 难度参数向量——「难度 = 信息丰度」(支柱④):主要调信息,不灌数值。
/// 轻松:军报勤、骑手稳、沙盘代望(兼当新手教学);硬核:报疏、路险、未归纯沉默。
/// </summary>
public sealed class BDifficultyProfile
{
    public BDifficulty Level;
    public string Cn = "";
    /// <summary>骑手遇袭概率倍率(令骑/塘骑/军书——双向迷雾的物流风险)。</summary>
    public float InterceptMult = 1f;
    /// <summary>骑手逾期未归是否提醒。硬核 = 纯沉默:谁没回来,你自己记着。</summary>
    public bool OverdueHint = true;
    /// <summary>部队自发军报周期(秒)——报得勤,战场就「透」一分。</summary>
    public float AutoReportPeriod = 40f;
    /// <summary>沙盘直接叠加瞭望所见(免登台)——轻松档默认开。</summary>
    public bool SandboxWatchOverlay;
    /// <summary>令骑记错命令的概率(去程失真,PRD §6.5)——错姿态/偏目的地,复盘才揭示。</summary>
    public float OrderGarbleChance = 0.06f;

    public static BDifficultyProfile Of(BDifficulty d) => d switch
    {
        BDifficulty.Easy => new BDifficultyProfile
        { Level = d, Cn = "轻松", InterceptMult = 0.5f, OverdueHint = true, AutoReportPeriod = 28f, SandboxWatchOverlay = true, OrderGarbleChance = 0.02f },
        BDifficulty.Hardcore => new BDifficultyProfile
        { Level = d, Cn = "硬核", InterceptMult = 1.6f, OverdueHint = false, AutoReportPeriod = 55f, SandboxWatchOverlay = false, OrderGarbleChance = 0.12f },
        _ => new BDifficultyProfile
        { Level = BDifficulty.Normal, Cn = "常规", InterceptMult = 1f, OverdueHint = true, AutoReportPeriod = 40f, SandboxWatchOverlay = false, OrderGarbleChance = 0.06f },
    };

    public static string DescCn(BDifficulty d) => d switch
    {
        BDifficulty.Easy => "轻松:军报频、骑手稳、沙盘代望——初掌兵符者宜之",
        BDifficulty.Hardcore => "硬核:军报疏、路多截杀、未归无提醒——纯粹的雾",
        _ => "常规:如实的战场——情报又旧又缺,但尚讲道理",
    };
}
