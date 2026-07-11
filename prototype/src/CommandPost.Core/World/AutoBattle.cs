using System;

namespace CommandPost.Core;

/// <summary>
/// 野战快速结算(NPC 对 NPC):不逐兵渲染,但**按时辰打**——
/// 消耗按「接战正面」走(√兵力):规模越大,战线越宽也越久,不会一照面就分胜负。
/// 战略层每推进若干时辰调 Step 一次;一方折损过崩溃线即溃。
/// </summary>
public sealed class AutoBattle
{
    /// <summary>折损至开战兵力的这个比例即溃(与逐兵战 Rout 手感对齐)。</summary>
    public const float BreakFrac = 0.32f;
    /// <summary>基准杀伤系数(每时辰,乘对方 √兵力)。
    /// 调过速:等额三百人野战约十余时辰见分晓,千五百人的大仗要打上一昼夜——
    /// 战略层看得见「酣战正炽」,赶去还来得及撞进战团。</summary>
    public const float KillRate = 1.6f;

    public int StartA { get; private set; }
    public int StartB { get; private set; }
    public int MenA { get; private set; }
    public int MenB { get; private set; }
    public float QualA { get; private set; } = 1f;
    public float QualB { get; private set; } = 1f;
    public BattleGround Ground { get; private set; }
    /// <summary>已打了几个时辰。</summary>
    public float Hours { get; private set; }
    public bool Over { get; private set; }
    /// <summary>胜方(Friend=A方,Enemy=B方;null=两败俱伤)。</summary>
    public Side? Winner { get; private set; }

    private AutoBattle() { }

    public static AutoBattle Start(int menA, float qualA, int menB, float qualB, BattleGround ground) => new()
    {
        StartA = Math.Max(1, menA), StartB = Math.Max(1, menB),
        MenA = Math.Max(1, menA), MenB = Math.Max(1, menB),
        QualA = qualA, QualB = qualB, Ground = ground
    };

    /// <summary>地形消耗倍率:泽地泥泞难杀透,林地各自为战,都拖时间。</summary>
    public static float GroundMult(BattleGround g) => g switch
    {
        BattleGround.Marsh => 0.62f, BattleGround.Forest => 0.8f,
        BattleGround.Hills => 0.88f, _ => 1f
    };

    /// <summary>推进 hours 个时辰的厮杀(可分多次小步调用;rng 供伤亡抖动)。</summary>
    public void Step(float hours, Rng rng)
    {
        if (Over || hours <= 0f) return;
        float mod = GroundMult(Ground);

        // 分小步积分,免得一大步跨过崩溃线太远
        const float slice = 0.25f;
        while (hours > 0f && !Over)
        {
            float dt = MathF.Min(slice, hours);
            hours -= dt;
            Hours += dt;

            float lossA = KillRate * MathF.Sqrt(MenB * QualB) * mod * dt * (float)rng.Jitter(0.18);
            float lossB = KillRate * MathF.Sqrt(MenA * QualA) * mod * dt * (float)rng.Jitter(0.18);
            MenA = Math.Max(0, MenA - Math.Max(1, (int)lossA));
            MenB = Math.Max(0, MenB - Math.Max(1, (int)lossB));

            bool brokeA = MenA <= StartA * BreakFrac || MenA <= 8;
            bool brokeB = MenB <= StartB * BreakFrac || MenB <= 8;
            if (brokeA || brokeB)
            {
                Over = true;
                if (brokeA && brokeB)
                {
                    float fa = MenA / (float)StartA, fb = MenB / (float)StartB;
                    Winner = MathF.Abs(fa - fb) < 0.02f ? null : fa > fb ? Side.Friend : Side.Enemy;
                }
                else Winner = brokeA ? Side.Enemy : Side.Friend;
            }
        }
    }
}
