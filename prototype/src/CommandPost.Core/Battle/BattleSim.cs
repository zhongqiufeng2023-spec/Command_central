using System;
using System.Collections.Generic;
using System.Linq;

namespace CommandPost.Core;

/// <summary>
/// 战斗B档:全战式逐兵真实层 + Radio Commander 式沙盘指挥层。
/// 真实层:每兵一单位(血量/阵位/近战/箭矢/士气/溃逃),连续坐标,10Hz 固定步。
/// 指挥层:玩家只看沙盘;下令=令骑真实骑行送达;敌情=斥候/军报带回的旧影。
///
/// 类按职责拆成多个 partial 文件:
///   BattleSim.cs          — 本核:状态 / 建军 / 主循环 / 生死判定 / 共享工具
///   BattleSim.Commands.cs — 玩家指令(令骑 / 塘骑 / 插旗)
///   BattleSim.Ai.cs       — 敌方对称迷雾 + 敌 AI + 我方姿态自主 + 风筝步
///   BattleSim.Riders.cs   — 令骑推进 / 沿途侦察 / 军报 / 情报并入沙盘
///   BattleSim.Units.cs    — 部队级(行军 / 接战 / 士气 / 溃逃 / 齐射选敌)
///   BattleSim.Soldiers.cs — 士兵级(跟队 / 近战 / 放箭 / 安全位移)+ 箭矢结算
/// 信息载体(Arrow/Rider/Sandbox 等)见 BattleInfo.cs。
/// </summary>
public sealed partial class BattleSim
{
    public const float Dt = 0.1f;

    public BattleMap Map { get; }
    public List<BattleUnit> Units { get; } = new();
    public List<Arrow> Arrows { get; } = new();
    public List<Rider> Riders { get; } = new();
    public SandboxState Sandbox { get; } = new();
    public List<Alert> Alerts { get; } = new();
    public Vec2F HqPos { get; set; }
    public float Time { get; private set; }
    public bool Over { get; private set; }
    public Side? Winner { get; private set; }

    /// <summary>瞭望台:帅帐望楼的实时视界半径(低保真、只在范围内、不留记忆)。</summary>
    public float WatchtowerRange { get; set; } = 180f;

    private readonly Rng _rng;
    private int _nextUnit = 1, _nextSoldier = 1, _nextRider = 1, _nextFlag = 1;
    private readonly Dictionary<int, BattleUnit> _byId = new();
    private readonly Dictionary<(int, int), List<Soldier>> _hash = new();
    /// <summary>敌方共享记忆:我方部队「最后所见」快照(非实时——敌 AI 也吃情报滞后,与玩家对称)。</summary>
    private readonly Dictionary<int, (Vec2F pos, float seenT)> _enemyKnown = new();
    /// <summary>发现→全军知晓之间的传讯延迟队列(readyT 到点才并入共享记忆)。</summary>
    private readonly Dictionary<int, (Vec2F pos, float readyT)> _enemyPending = new();
    private float _endClock, _spotClock;
    private const float HashCell = 3f;
    private const float EnemyReactLag = 5f;    // 敌方发现你→全军协同的反应延迟(对标玩家令骑往返)
    private const float EnemyRefresh = 7f;     // 已知目标的快照刷新间隔(看得见也不实时跟,会扑「最后所见」)
    private const float EnemyForget = 45f;     // 失去接触后遗忘
    private const float EnemyCloseVision = 60f; // 近身实时反应半径(免贴脸还站桩)

    public BattleSim(BattleMap map, Rng rng) { Map = map; _rng = rng; }

    public BattleUnit? ById(int id) => _byId.TryGetValue(id, out var u) ? u : null;

    public BattleUnit AddUnit(Side side, UnitType type, string name, Commander officer, Vec2F center, int count, bool ai = false)
    {
        var u = new BattleUnit
        {
            Id = _nextUnit++, Side = side, Type = type, Name = name, Officer = officer,
            Center = Map.Clamp(center), MaxCount = count, AiControlled = ai,
            Facing = side == Side.Friend ? new Vec2F(1, 0) : new Vec2F(-1, 0),
            ReportClock = 20f + (_nextUnit % 5) * 4f      // 错峰:别全军同刻发军报
        };
        for (int i = 0; i < count; i++)
            u.Soldiers.Add(new Soldier
            {
                Id = _nextSoldier++, UnitId = u.Id, Pos = u.SlotWorld(i),
                Ammo = BArms.AmmoOf(type),
                ReloadT = 0.5f + (float)_rng.NextDouble() * 2f,   // 开战已上弦:首轮齐射快
                CoolT = (float)_rng.NextDouble()
            });
        Units.Add(u); _byId[u.Id] = u;
        if (side == Side.Friend)
            Sandbox.Own[u.Id] = new SandboxOwnMark { UnitId = u.Id, Pos = u.Center, Count = count, StateCn = "就位", T = 0 };
        return u;
    }

    private float RandReload(UnitType t)
    { var (lo, hi) = BArms.ReloadOf(t); return lo + (float)_rng.NextDouble() * (hi - lo); }

    // ====================================================================
    //  主循环(固定步 0.1s)
    // ====================================================================

    public void Tick()
    {
        if (Over) return;
        Time += Dt;
        UpdateEnemyKnowledge();
        UpdateEnemyAi();
        UpdateStances();     // 我方各部按姿态自主行事(进攻扑敌/等待避战)
        UpdateRiders();
        AutoReports();
        UpdateUnits();
        RebuildHash();
        UpdateSoldiers();
        UpdateArrows();
        RemoveDead();
        CheckBattleEnd();
    }

    // —— 瞭望台:帅帐望楼此刻所见(不写入沙盘、不留记忆;渲染层直接读)——
    public IEnumerable<BattleUnit> WatchtowerVisible() =>
        Units.Where(u => u.AliveCount > 0 &&
            u.Center.DistanceTo(HqPos) <= WatchtowerRange * BattleMap.ConcealMult(Map.At(u.Center)));

    // ====================================================================
    //  生死清理 / 胜负判定
    // ====================================================================

    private void RemoveDead()
    {
        foreach (var u in Units)
        {
            for (int i = u.Soldiers.Count - 1; i >= 0; i--)
            {
                var s = u.Soldiers[i];
                bool fledOff = s.Pos.X < 6f || s.Pos.X > Map.WorldW - 6f;
                if (s.Hp <= 0) u.Soldiers.RemoveAt(i);
                else if (fledOff && u.State is BUnitState.Routing or BUnitState.Shattered)
                { u.Fled++; u.Soldiers.RemoveAt(i); }
            }
        }
    }

    private void CheckBattleEnd()
    {
        _endClock += Dt;
        if (_endClock < 1.5f) return;
        _endClock = 0;
        if (!Units.Any(u => u.Side == Side.Friend) || !Units.Any(u => u.Side == Side.Enemy)) return;   // 没有两军就没有胜负
        bool friendStands = Units.Any(u => u.Side == Side.Friend && u.Controllable);
        bool enemyStands = Units.Any(u => u.Side == Side.Enemy && u.Controllable);
        if (friendStands && enemyStands) return;
        Over = true;
        Winner = friendStands ? Side.Friend : enemyStands ? Side.Enemy : null;
        Alerts.Add(new Alert((int)Time, Winner == Side.Friend ? "虏骑溃矣!战场是我们的。"
                                       : Winner == Side.Enemy ? "全军溃散……败局已定。" : "两败俱伤,战场沉寂。", true));
    }

    // ====================================================================
    //  共享工具(供各 partial 文件复用)
    // ====================================================================

    private BattleUnit? NearestUnit(Vec2F from, Side side)
    {
        BattleUnit? best = null; float bd = float.MaxValue;
        foreach (var x in Units)
        {
            if (x.Side != side || x.AliveCount == 0) continue;
            float d = x.Center.DistanceTo(from);
            if (d < bd) { bd = d; best = x; }
        }
        return best;
    }

    private void RebuildHash()
    {
        _hash.Clear();
        foreach (var u in Units)
            foreach (var s in u.Soldiers)
            {
                var key = ((int)(s.Pos.X / HashCell), (int)(s.Pos.Y / HashCell));
                if (!_hash.TryGetValue(key, out var list)) _hash[key] = list = new List<Soldier>();
                list.Add(s);
            }
    }

    private void ForNeighbors(Vec2F p, float radius, Action<Soldier> act)
    {
        int cx = (int)(p.X / HashCell), cy = (int)(p.Y / HashCell);
        int r = (int)(radius / HashCell) + 1;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (_hash.TryGetValue((cx + dx, cy + dy), out var list))
                    foreach (var s in list) act(s);
    }

    public void Feed(string text) => Sandbox.Feed.Add($"[{FormatT(Time)}] {text}");
    public static string FormatT(float t) => $"{(int)(t / 60):00}:{(int)(t % 60):00}";

    public static string ArmCn(UnitType t) => t switch
    {
        UnitType.Spear => "枪", UnitType.Bow => "弩", UnitType.Cavalry => "轻骑", UnitType.Shield => "盾",
        UnitType.MoDao => "陌刀", UnitType.Cataphract => "具装", UnitType.HorseArcher => "骑射",
        UnitType.NomadLancer => "突骑", UnitType.TribalFoot => "部众", _ => "?"
    };
}
