namespace CommandPost.Core;

/// <summary>中军任务目标的种类。ScoutEnemy/Support/ReportToHq 已实装;Preserve 留给后续。</summary>
public enum ObjectiveKind { ScoutEnemy, ReportToHq, Support, Preserve }

public enum ObjectiveState { Pending, Partial, Done, Failed }

/// <summary>一条可判定的中军任务目标。</summary>
public sealed class Objective
{
    public string Id { get; init; } = "";
    public string Cn { get; init; } = "";
    public ObjectiveKind Kind { get; init; }
    public ObjectiveState State { get; set; } = ObjectiveState.Pending;
    /// <summary>是否主目标(参与"任务达成度"评级的核心项)。</summary>
    public bool Primary { get; init; }
    /// <summary>是否已被中军令激活(未激活不评估,UI 显示「令未至」)。</summary>
    public bool Active { get; set; } = true;

    // —— ScoutEnemy 参数 ——
    /// <summary>需侦明的敌军数(认知世界里"较可信"的敌影);Support 复用为「需抵达的队数」。</summary>
    public int RequiredCount { get; init; } = 1;
    /// <summary>情报最大年龄(tick):太旧不算"侦明"。</summary>
    public int MaxAge { get; init; } = 40;

    // —— Support(驰援)参数:抵达 TargetPos 半径 Radius 内的本路队数 ≥ RequiredCount ——
    public Vec2? TargetPos { get; init; }
    public int Radius { get; init; } = 4;
}

/// <summary>一道中军令:帅帐在 DispatchTick 发出,LinkDelay 后送达本路中军帐(可激活目标)。</summary>
public sealed class HqOrder
{
    public int DispatchTick { get; init; }
    public string TitleCn { get; init; } = "";
    public string TextCn { get; init; } = "";
    /// <summary>送达时激活的目标 Id(可多个)。</summary>
    public string[] ActivatesObjectives { get; init; } = System.Array.Empty<string>();
    /// <summary>帅帐↔本路 的传令延迟(帅帐在后方,链路比阵内传令更长)。</summary>
    public int LinkDelay { get; init; } = 12;
    public bool Delivered { get; set; }
    public int ArriveTick => DispatchTick + LinkDelay;
}

/// <summary>
/// 一场战役的中军任务:分阶段中军令 + 目标清单 + 战役窗口 + 双层胜负。
/// 每 tick 末由 Simulation 评估(§任务制胜负 · PRD §8.6 政治的输入)。
/// </summary>
public sealed class Mission
{
    public string Title { get; init; } = "";
    public List<Objective> Objectives { get; init; } = new();
    /// <summary>帅帐的分阶段中军令(第一道侦察、第二道驰援……)。</summary>
    public List<HqOrder> Orders { get; init; } = new();
    /// <summary>战役窗口:到此 tick 战役结束(敌自退去),按目标结算——不必打到全歼。</summary>
    public int EndTick { get; init; } = int.MaxValue;
    /// <summary>主帅性格(政治评价权重来源,MVP-light:尚功者重驰援轻伤亡)。</summary>
    public Personality HqPersonality { get; init; } = Personality.Steady;
    /// <summary>驰援令送达时刻(null=未至);D「上报战况」以此为界。</summary>
    public int? AidOrderDeliveredTick { get; set; }

    /// <summary>硬性胜负(歼灭为底);战役窗口的"到时结算"由 Simulation.BattleOver 判。</summary>
    public GameStatus Outcome { get; private set; } = GameStatus.Ongoing;

    public Objective? this[string id] => Objectives.FirstOrDefault(o => o.Id == id);

    /// <summary>主目标是否全部达成(喂"任务达成度"评级)。</summary>
    public bool AllPrimaryDone => Objectives.Where(o => o.Primary).All(o => o.State == ObjectiveState.Done);

    public void Evaluate(Simulation sim)
    {
        var belief = sim.Beliefs[Side.Friend];
        int now = sim.Truth.Tick;

        foreach (var o in Objectives)
        {
            if (!o.Active || o.State is ObjectiveState.Done or ObjectiveState.Failed) continue;
            switch (o.Kind)
            {
                case ObjectiveKind.ScoutEnemy:
                    int known = belief.KnownOf(Side.Enemy).Count(g => g.AgeAt(now) <= o.MaxAge);
                    o.State = known >= o.RequiredCount ? ObjectiveState.Done
                            : known > 0 ? ObjectiveState.Partial
                            : ObjectiveState.Pending;
                    break;

                case ObjectiveKind.Support:
                    // 左翼 = 我方非本路(PlayerLed=false)。全灭 → 失败;本路 RequiredCount 队抵近 → 达成。
                    var wing = sim.Truth.Units.Where(u => u.Side == Side.Friend && !u.PlayerLed).ToList();
                    if (wing.Count > 0 && wing.All(u => !u.Alive)) { o.State = ObjectiveState.Failed; break; }
                    if (o.TargetPos is Vec2 anchor)
                    {
                        int near = sim.Truth.LivingOf(Side.Friend).Count(u => u.PlayerLed && u.Pos.DistanceTo(anchor) <= o.Radius);
                        o.State = near >= o.RequiredCount ? ObjectiveState.Done
                                : near > 0 ? ObjectiveState.Partial
                                : ObjectiveState.Pending;
                    }
                    break;

                // ReportToHq:由 Simulation 在「上报送达帅帐」时置位(见 ReportToHq / ProcessHqLink)。
            }
        }

        Outcome = sim.Status;
    }
}

/// <summary>关卡任务工厂。</summary>
public static class Missions
{
    /// <summary>
    /// 第一关《黑松岭》:开局第一道令 = 侦察(A 侦明 + B 回报);
    /// aidOrderTick 时第二道令 = 驰援左翼(激活 C 驰援 + D 上报战况)。
    /// 战役窗口 = aidOrderTick + 300(到时敌自退,按目标结算)。
    /// </summary>
    public static Mission BlackPine(int aidOrderTick = 300, Vec2? leftWingAnchor = null)
    {
        var anchor = leftWingAnchor ?? new Vec2(9, 2);
        return new Mission
        {
            Title = "黑松岭 · 试探与驰援",
            HqPersonality = Personality.Aggressive,   // 本关主帅:尚功,喜果决(驰援权重高、伤亡看得轻)
            EndTick = aidOrderTick + 300,
            Objectives =
            {
                new Objective { Id = "A", Cn = "侦明当面之敌(三处以上)", Kind = ObjectiveKind.ScoutEnemy, Primary = true, RequiredCount = 3, MaxAge = 40 },
                new Objective { Id = "B", Cn = "敌情回报帅帐(按 R 上报)", Kind = ObjectiveKind.ReportToHq },
                new Objective { Id = "C", Cn = "驰援左翼(两队抵达)",       Kind = ObjectiveKind.Support, Primary = true, Active = false, TargetPos = anchor, Radius = 4, RequiredCount = 2 },
                new Objective { Id = "D", Cn = "上报左翼战况(按 R 上报)",   Kind = ObjectiveKind.ReportToHq, Active = false },
            },
            Orders =
            {
                new HqOrder { DispatchTick = 0, LinkDelay = 2, TitleCn = "第一道令",
                    TextCn = "帅帐谕:虏情未明,速遣斥候,侦得敌踪三处以上,具报帅帐。" },
                new HqOrder { DispatchTick = aidOrderTick, LinkDelay = 12, TitleCn = "第二道令",
                    TextCn = "左翼刘部遇袭甚急!尔部即刻驰援,不得有误。",
                    ActivatesObjectives = new[] { "C", "D" } },
            }
        };
    }
}
