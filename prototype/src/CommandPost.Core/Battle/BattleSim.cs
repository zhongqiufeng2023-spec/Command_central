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

    /// <summary>难度参数向量(信息丰度旋钮,PRD §11)。</summary>
    public BDifficultyProfile Difficulty { get; set; } = BDifficultyProfile.Of(BDifficulty.Normal);

    /// <summary>复盘胶卷:逐秒录真相帧+认知帧;战毕对照回放(上帝视角只在战后)。</summary>
    public BattleReplay Replay { get; } = new();
    private float _replayClock = 1f;   // 首帧在开战第一秒落下

    /// <summary>SAN→失真系数(蓝图§4.5 试水):1=清明;越高,回报误差越大、沙盘越可能长出幻影敌情。
    /// 心态崩了,你眼里的战场就更假——SAN 与迷雾母题双向绑定。</summary>
    public float SanFactor { get; set; } = 1f;
    private float _sanClock;
    private int _nextPhantom = -901;

    /// <summary>已张开的疑兵(佯动):虚设旌旗金鼓,污染敌方认知。</summary>
    public List<BDecoy> Decoys { get; } = new();
    /// <summary>疑兵队余量(老弱替身有限,一战两拨)。</summary>
    public int DecoysLeft { get; set; } = 2;
    private int _nextDecoy = -501;

    /// <summary>战前可遣的细作名额(间谍:混入敌军递密报)。</summary>
    public int SpiesAvailable { get; set; }
    /// <summary>已混入敌营的细作。</summary>
    public List<BSpy> Spies { get; } = new();
    /// <summary>细作每次递书的暴露概率(测试可置 0/1)。</summary>
    public float SpyCatchChance { get; set; } = 0.10f;

    /// <summary>战前布阵:时间冻结,本方各部可当面吩咐(不费令骑)。FinishDeploy 后开战。</summary>
    public bool Deploying { get; private set; }
    /// <summary>布阵区东界(世界米):开战前只能摆在自家地界。</summary>
    public float DeployZoneMaxX { get; private set; }

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

    public BattleUnit AddUnit(Side side, UnitType type, string name, Commander officer, Vec2F center, int count, bool ai = false, bool allied = false)
    {
        var u = new BattleUnit
        {
            Id = _nextUnit++, Side = side, Type = type, Name = name, Officer = officer,
            Center = Map.Clamp(center), MaxCount = count, AiControlled = ai || allied, Allied = allied,
            Anchor = Map.Clamp(center),
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
        if (side == Side.Friend && !allied)
            Sandbox.Own[u.Id] = new SandboxOwnMark { UnitId = u.Id, Pos = u.Center, Count = count, StateCn = "就位", T = 0 };
        return u;
    }

    private float RandReload(UnitType t)
    { var (lo, hi) = BArms.ReloadOf(t); return lo + (float)_rng.NextDouble() * (hi - lo); }

    // ====================================================================
    //  主循环(固定步 0.1s)
    // ====================================================================

    public void BeginDeploy(float zoneMaxX) { Deploying = true; DeployZoneMaxX = zoneMaxX; }

    public void FinishDeploy()
    {
        if (!Deploying) return;
        Deploying = false;
        int planned = 0;
        foreach (var u in Units)
            if (u is { Side: Side.Friend, Allied: false, PlannedDest: { } pd })
            { u.SetOrderDirect(pd, u.PlannedRun, Map); u.PlannedDest = null; planned++; }
        Feed(planned > 0 ? $"各部就位,战鼓起!{planned} 部依预令而动。" : "各部就位,战鼓起!");
    }

    public void Tick()
    {
        if (Over || Deploying) return;
        Time += Dt;
        UpdateEnemyKnowledge();
        UpdateEnemyAi();
        UpdateAlliedAi();    // 友邻一路(左翼)自己会打——你救不救是另一回事
        UpdateStances();     // 我方各部按姿态自主行事(进攻扑敌/等待避战)
        UpdateAskForOrders();// 请示求决:守势之将见敌请示,不回令则自处
        UpdateRiders();
        UpdateMission();
        UpdateSanPhantoms(); // SAN 低:沙盘自己长出不存在的敌人
        UpdateSpies();       // 敌营细作的密报(与暴露)
        AutoReports();
        UpdateUnits();
        RebuildHash();
        UpdateSoldiers();
        UpdateArrows();
        RemoveDead();
        CheckBattleEnd();
        RecordReplayFrame();   // 放在胜负判定后:战毕那一刻的收尾帧也录上
    }

    /// <summary>SAN 试水(蓝图§4.5):SanFactor ≥ 1.3 时,每隔一阵可能凭空长出一条「幻影敌情」——
    /// 惊慌的塘骑、熬红的眼、只存在于你沙盘上的虏骑。真相无此人;复盘才揭示。</summary>
    private void UpdateSanPhantoms()
    {
        if (SanFactor < 1.3f) return;
        _sanClock += Dt;
        if (_sanClock < 20f) return;
        _sanClock = 0;
        if (!_rng.Chance(0.28 * (SanFactor - 1.2f))) return;

        float ang = (float)(_rng.NextDouble() * Math.PI) - MathF.PI / 2f;    // 东半面某向
        float dist = 180f + (float)_rng.NextDouble() * 220f;
        var pos = Map.Clamp(HqPos + new Vec2F(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist * 0.8f));
        int est = (10 + (int)(_rng.NextDouble() * 25)) * 10;
        int id = _nextPhantom--;
        Sandbox.Enemy[id] = new SandboxEnemyMark { UnitId = id, Pos = pos, Est = est, Type = null, T = Time };
        Alerts.Add(new Alert((int)Time, $"塘骑惊报:又见虏骑!约{est}众,不知何部……", true));
        Reveals.Add($"{FormatT(Time)} 那支「约{est}众」的虏骑从未存在——心神耗尽时,沙盘也会骗你");
    }

    /// <summary>细作(间谍):混在敌部里,周期递出密报——身在营中,数的是确数,还知道动向。
    /// 每递一次冒暴露之险:事败=纯沉默(难度档可在久无书信后给一句提示);
    /// 所在虏部若溃散,细作趁乱脱身归来。</summary>
    private void UpdateSpies()
    {
        foreach (var sp in Spies)
        {
            if (sp.Burned)
            {
                if (!sp.HintGiven && Difficulty.OverdueHint && Time > sp.BurnedT + 240f)
                { sp.HintGiven = true; Alerts.Add(new Alert((int)Time, "敌营的细作久无书信……但愿只是不便动笔。", false)); }
                continue;
            }
            var u = ById(sp.UnitId);
            if (u is null || u.AliveCount == 0 || u.State is BUnitState.Shattered or BUnitState.Destroyed)
            {
                sp.Burned = true; sp.BurnedT = float.MaxValue; sp.HintGiven = true;
                Alerts.Add(new Alert((int)Time, "细作趁乱脱身归来:所在虏部已溃,再无可报。", false));
                continue;
            }
            if (Time < sp.NextT) continue;
            sp.NextT = Time + 75f + (float)_rng.NextDouble() * 60f;

            if (_rng.Chance(SpyCatchChance))
            {
                sp.Burned = true; sp.BurnedT = Time;
                Reveals.Add($"{FormatT(Time)} 细作递书时事败,没于敌营——此后的杳无音讯,不是他偷懒");
                continue;
            }

            sp.Reports++;
            var mk = Sandbox.Enemy.TryGetValue(u.Id, out var em) ? em
                   : Sandbox.Enemy[u.Id] = new SandboxEnemyMark { UnitId = u.Id };
            mk.Pos = u.Center; mk.Est = u.AliveCount; mk.Type = u.Type; mk.T = Time - 15f;   // 递书出营耗一刻
            string intent = u.Path.Count > 0 ? $"正开往 ({(int)u.Path[^1].X},{(int)u.Path[^1].Y})" : "屯驻未动";
            Alerts.Add(new Alert((int)Time,
                $"细作密报:{ArmCn(u.Type)}部实有 {u.AliveCount} 骑步,现在 ({(int)u.Center.X},{(int)u.Center.Y}),{intent}。", true));
        }
    }

    /// <summary>逐秒录一帧(战毕再补一帧收尾)。内存量级:20 分钟 ≈ 1200 帧,忽略不计。</summary>
    private void RecordReplayFrame()
    {
        _replayClock += Dt;
        if (_replayClock < 1f && !Over) return;
        _replayClock = 0;
        var f = new BReplayFrame { T = Time };
        foreach (var u in Units)
            f.Units.Add(new BReplayUnit(u.Id, u.Side, u.Allied, u.Center, u.AliveCount, u.State));
        foreach (var r in Riders)
            f.Riders.Add(new BReplayRider(r.Pos, r.Kind, r.Lost));
        foreach (var (id, m) in Sandbox.Enemy) f.BeliefEnemy.Add(new BReplayMark(id, m.Pos, m.Est, m.T));
        foreach (var (id, m) in Sandbox.Own) f.BeliefOwn.Add(new BReplayMark(id, m.Pos, m.Count, m.T));
        foreach (var (id, m) in Sandbox.Ally) f.BeliefAlly.Add(new BReplayMark(id, m.Pos, m.Est, m.T));
        Replay.Frames.Add(f);
    }

    /// <summary>友邻崩溃是否判硬性败:剧本战(左翼是防线一环)=真;
    /// 野战里顺路来援的官军=假——那是你自己选的仗,友军垮了仗还在打。</summary>
    public bool AlliedCollapseIsDefeat = true;

    /// <summary>左翼(友邻一路)是否已崩:折损逾六成五,或全员失序——一支残队独存不算「左翼尚在」。</summary>
    public bool LeftWingCollapsed
    {
        get
        {
            int start = 0, alive = 0; bool any = false, standing = false;
            foreach (var u in Units)
            {
                if (!u.Allied) continue;
                any = true; start += u.MaxCount; alive += u.AliveCount;
                if (u.Controllable) standing = true;
            }
            return any && (alive < start * 0.35f || !standing);
        }
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

        // 硬性败之一:左翼(友邻一路)崩溃——指挥中枢侧翼洞开,全线不可守(关卡文档 §6)
        if (AlliedCollapseIsDefeat && LeftWingCollapsed)
        {
            Over = true; Winner = Side.Enemy;
            Alerts.Add(new Alert((int)Time, "左翼崩溃!虏骑自北而下,全线动摇——败局已定。", true));
            Mission?.Finish(this);
            return;
        }

        bool friendStands = Units.Any(u => u.Side == Side.Friend && !u.Allied && u.Controllable);
        bool enemyStands = Units.Any(u => u.Side == Side.Enemy && u.Controllable);
        if (friendStands && enemyStands) return;
        Over = true;
        Winner = friendStands ? Side.Friend : enemyStands ? Side.Enemy : null;
        Alerts.Add(new Alert((int)Time, Winner == Side.Friend ? "虏骑溃矣!战场是我们的。"
                                       : Winner == Side.Enemy ? "全军溃散……败局已定。" : "两败俱伤,战场沉寂。", true));
        Mission?.Finish(this);
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
