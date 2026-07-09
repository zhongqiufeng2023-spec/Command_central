using System;
using System.Collections.Generic;

namespace CommandPost.Core;

/// <summary>部队状态(全战式):稳阵 → 接战 → 动摇 → 溃走(可能收拢)→ 溃散 / 覆没。</summary>
public enum BUnitState { Steady, Engaged, Wavering, Routing, Shattered, Destroyed }

public enum BOrderKind { Hold, Move }

/// <summary>部队姿态(玩家经令骑设定):没有命令时部队按姿态自主行事——
/// 进攻=视界内自主接敌/挨打就扑;据守=钉在原地打还手;等待=避战自保,敌近则后撤;
/// 游走=风筝战术(远程专用):保持射程带内放箭,敌近就拉开、敌远就跟上,箭尽转退避。</summary>
public enum BStance { Attack, Hold, Standby, Skirmish }

/// <summary>一名士兵:自己的位置与血量。部队兵力 = 存活士兵之和。</summary>
public sealed class Soldier
{
    public int Id;
    public int UnitId;
    public Vec2F Pos;
    public float Hp = 100f;
    public float CoolT;          // 近战出手冷却
    public float ReloadT;        // 远程装填
    public int Ammo;
    public bool Fighting;        // 本刻是否有敌可及(接战中)
}

/// <summary>
/// 一支部队(逐兵容器):中心 + 朝向 + 阵列槽位;士兵向各自槽位跟队。
/// 士气/体力是部队级;伤亡从士兵级向上聚合。
/// </summary>
public sealed class BattleUnit
{
    public int Id;
    public Side Side;
    public UnitType Type;
    public string Name = "";
    public Commander Officer = new("无名", Personality.Steady);

    public List<Soldier> Soldiers = new();
    public int MaxCount;
    public int AliveCount => Soldiers.Count;
    public float LossFrac => 1f - (float)AliveCount / Math.Max(1, MaxCount);

    public Vec2F Center;
    public Vec2F Facing = new(1, 0);
    public List<Vec2F> Path = new();
    public BOrderKind Order = BOrderKind.Hold;
    public bool Running;

    public BUnitState State = BUnitState.Steady;
    public float Morale = 85f, Stamina = 100f;
    public float RecentLoss;         // 近期伤亡(指数衰减)——士气崩的主推手
    public int Kills, Fled;
    public bool Shaken;              // 溃而复聚:士气上限打折
    public float ChargeT;            // 冲锋窗口(骑兵冲入接战后短暂的高伤害)
    public float SpeedNow;           // 中心实际移速(判冲锋)
    public float EngagedT;           // 连续缠斗时长(骑兵判「该凿穿了」)
    public float DisengageT;         // 穿插窗口:>0 时骑队凿穿阵背而出(不恋战、边走边砍)

    public bool AiControlled;        // 敌方:简单单位级 AI
    /// <summary>友邻一路(如左翼李嵩部):同侧但不归你辖——不可下令、不入本路沙盘、自有 AI。</summary>
    public bool Allied;
    /// <summary>驻地锚点:友邻/敌军无战事时守着的位置。</summary>
    public Vec2F Anchor;
    /// <summary>剧本按兵:此刻之前 AI 不动(如左翼之敌先蛰伏、到时才压上)。</summary>
    public float HoldUntilT;
    /// <summary>剧本进军目标:AI 无敌情时朝此推进(到附近或接敌即清除)。</summary>
    public Vec2F? ScriptTarget;
    public float ThinkClock, MoraleClock, ReportClock, EventCooldown, RoutClock, RallyClock;
    public bool ReportedEngaged, ReportedRouting;
    public int VolleyTargetId = -1;  // 远程齐射目标部队

    /// <summary>姿态(玩家部队按此自主行事;敌 AI 走自己的脑子)。</summary>
    public BStance Stance = BStance.Hold;
    /// <summary>武将解读命令时的走样批注(如「贪功压前」)——真相侧不广播,军报/探问才带回。</summary>
    public string LastQuirkCn = "";

    // —— 请示求决(PRD §6.4):敌当面而无战令 → 遣骑请示;不回令则按脾性自处 ——
    public bool AwaitingReply;       // 请示已发,候中军回令
    public bool AskedOnce;           // 本次接触已请示过(威胁散去后重置)
    public float AskedT;

    // —— 布阵预令(面授机宜):开战擂鼓那一刻即照此进军 ——
    public Vec2F? PlannedDest;
    public bool PlannedRun;
    /// <summary>最近一次受威胁(挨箭/接刃)的方位与时刻——姿态反应的依据。</summary>
    public Vec2F LastThreatPos;
    public float LastThreatT = -999f;

    public bool Controllable => State is BUnitState.Steady or BUnitState.Engaged or BUnitState.Wavering && AliveCount > 0;

    /// <summary>直接下令(令骑送达 / 敌 AI / 测试用)。玩家指令必须经令骑,不可直调。</summary>
    public void SetOrderDirect(Vec2F dest, bool run, BattleMap map)
    {
        if (State is BUnitState.Routing or BUnitState.Shattered or BUnitState.Destroyed) return;
        Path = BattlePath.Find(map, Center, dest);
        Running = run;
        Order = BOrderKind.Move;
    }

    /// <summary>横列排面(按存活数开方,宽扁阵)。</summary>
    public int Files => Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(AliveCount * 2.6f)), 2, 44);

    /// <summary>第 i 名士兵的阵位(世界坐标):中心 + 右向×列偏移 + 后向×行偏移。人亡列缩,自然并拢。</summary>
    public Vec2F SlotWorld(int i)
    {
        float sp = BArms.SpacingOf(Type);
        int files = Files;
        int row = i / files, col = i % files;
        float x = (col - (files - 1) * 0.5f) * sp;
        float y = row * sp;
        var right = Facing.Perp;
        var back = Facing * -1f;
        return Center + right * x + back * y;
    }

    public string StateCn => State switch
    {
        BUnitState.Engaged => "接战",
        BUnitState.Wavering => "动摇",
        BUnitState.Routing => "溃走",
        BUnitState.Shattered => "溃散",
        BUnitState.Destroyed => "覆没",
        _ => Path.Count > 0 ? (Running ? "疾进" : "行进") : "待命"
    };

    public string StanceCn => Stance switch
    {
        BStance.Attack => "进攻", BStance.Standby => "等待", BStance.Skirmish => "游走", _ => "据守"
    };
}
