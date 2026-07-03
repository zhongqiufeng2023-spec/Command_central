namespace CommandPost.Core;

/// <summary>中军任务目标的种类。原型只实到 ScoutEnemy;其余为后续增量占位(需帅帐 belief / 左翼 NPC)。</summary>
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

    // —— ScoutEnemy 参数 ——
    /// <summary>需侦明的敌军数(认知世界里"较可信"的敌影)。</summary>
    public int RequiredCount { get; init; } = 1;
    /// <summary>情报最大年龄(tick):太旧不算"侦明"。</summary>
    public int MaxAge { get; init; } = 40;
}

/// <summary>
/// 一场战役的中军任务:目标清单 + 双层胜负(硬性 Outcome)。
/// 每 tick 末由 Simulation 评估(§任务制胜负 · PRD §8.6 政治的输入)。
/// </summary>
public sealed class Mission
{
    public string Title { get; init; } = "";
    public List<Objective> Objectives { get; init; } = new();

    /// <summary>硬性胜负(null=进行中)。原型先用歼灭规则占位,后续接"左翼没崩 / 你部没覆没"。</summary>
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
            if (o.State is ObjectiveState.Done or ObjectiveState.Failed) continue;
            switch (o.Kind)
            {
                case ObjectiveKind.ScoutEnemy:
                    int known = belief.KnownOf(Side.Enemy).Count(g => g.AgeAt(now) <= o.MaxAge);
                    o.State = known >= o.RequiredCount ? ObjectiveState.Done
                            : known > 0 ? ObjectiveState.Partial
                            : ObjectiveState.Pending;
                    break;

                // ReportToHq / Support / Preserve:待后续增量(帅帐认知 / 左翼 NPC / 请援)。
            }
        }

        Outcome = sim.Status;   // 占位:后续替换为任务制硬性胜负(左翼没崩 / 你部没覆没)
    }
}

/// <summary>关卡任务工厂。</summary>
public static class Missions
{
    /// <summary>第一关《黑松岭》:A 侦明当面之敌(主);B 回报、C 驰援左翼(主)、D 上报——为后续增量占位。</summary>
    public static Mission BlackPine() => new()
    {
        Title = "黑松岭 · 试探与驰援",
        Objectives =
        {
            new Objective { Id = "A", Cn = "侦明当面之敌(位置 / 规模)", Kind = ObjectiveKind.ScoutEnemy, Primary = true, RequiredCount = 3, MaxAge = 40 },
            new Objective { Id = "B", Cn = "把敌情回报帅帐",             Kind = ObjectiveKind.ReportToHq },
            new Objective { Id = "C", Cn = "驰援左翼",                   Kind = ObjectiveKind.Support, Primary = true },
            new Objective { Id = "D", Cn = "上报左翼战况",               Kind = ObjectiveKind.ReportToHq },
        }
    };
}
