namespace CommandPost.Core;

public enum GameStatus { Ongoing, FriendWon, EnemyWon }

/// <summary>
/// 模拟内核(引擎无关)。真相世界 + 双方认知世界 + 传令系统 + 失真管线。
/// 渲染层只读 Beliefs[Side.Friend],绝不读 Truth(防穿帮)。
/// </summary>
public sealed class Simulation
{
    private const double CombatK = 0.06;    // 近战伤害系数
    private const double RangedK = 0.035;   // 放箭伤害系数(隔空、无反击,故低于近战)
    private const double MoveRate = 0.4;    // 全局移动倍率(降速:行军像行军、奔袭像奔袭)

    public TruthWorld Truth { get; }
    public DifficultySettings Difficulty { get; }
    public Dictionary<Side, BeliefWorld> Beliefs { get; } = new();

    /// <summary>逐 tick 真相快照,供战后复盘(上帝视角)动画回放。战中绝不读。</summary>
    public List<ReplayFrame> Replay { get; } = new();

    /// <summary>瞭望台此刻的实时低保真观察(每 tick 重算,不入认知世界);渲染层叠加显示。</summary>
    public List<WatchtowerContact> WatchtowerContacts { get; } = new();

    /// <summary>当前战役的中军任务(可空);每 tick 末评估(§任务制胜负,PRD §8.6 政治的输入)。</summary>
    public Mission? Mission { get; set; }

    /// <summary>给玩家的醒目提示流(中军令/斥候归来/使者未归…);Pause=true 前端自动暂停。</summary>
    public List<Alert> Alerts { get; } = new();

    /// <summary>剧本触发器:到 tick 直接给某单位下真相层命令(袭击队等,绕过传令/解读)。</summary>
    public List<(int Tick, int UnitId, Intent Intent)> ScheduledOrders { get; } = new();

    /// <summary>战后政治评语(战役结束那一 tick 生成一次)。</summary>
    public Appraisal? Appraisal { get; private set; }

    /// <summary>战役是否已结束:任一方覆没,或到达任务的战役窗口(到时敌自退,按目标结算)。</summary>
    public bool BattleOver => Status != GameStatus.Ongoing || (Mission is { } m && Truth.Tick >= m.EndTick);

    /// <summary>本路(玩家直辖)开局总兵力/当前总兵力——政治评价的「代价」输入。</summary>
    public double PlayerStrengthAtStart { get; }
    public double PlayerStrengthNow => Truth.Units.Where(u => u.Side == Side.Friend && u.PlayerLed).Sum(u => u.Strength);

    /// <summary>帅帐↔本路 的上报链路延迟(帅帐在后方)。</summary>
    private const int HqLinkDelay = 12;
    private readonly List<(int ArriveTick, int IntelCount, int SentTick)> _hqReports = new();

    /// <summary>玩家可见的事件流(情报送达、传令、未归提示…)。</summary>
    public List<string> Log { get; } = new();
    /// <summary>真相事件流(副将偏离、丢失等)——仅复盘可见,玩家战中看不到。</summary>
    public List<string> TruthLog { get; } = new();

    private readonly Rng _rng;
    private int _nextMessengerId = 1;
    private int _nextScoutId = 1;
    private readonly List<PendingSignal> _pendingSignals = new();

    public Simulation(TruthWorld truth, DifficultySettings difficulty, Rng rng)
    {
        Truth = truth;
        Difficulty = difficulty;
        _rng = rng;
        Beliefs[Side.Friend] = new BeliefWorld { Owner = Side.Friend };
        Beliefs[Side.Enemy] = new BeliefWorld { Owner = Side.Enemy };
        foreach (var u in truth.Units)
        {
            u.ScoutPool = difficulty.UnitScoutPool;
            var d = truth.HqOf(u.Side.Opponent()) - u.Pos;                  // 初始面向敌方大本营
            if (d.X != 0 || d.Y != 0) u.Facing = SignDir(d);
        }
        PlayerStrengthAtStart = truth.Units.Where(u => u.Side == Side.Friend && u.PlayerLed).Sum(u => u.Strength);
        RecordFrame(); // t0 初始帧
    }

    public GameStatus Status =>
        !Truth.LivingOf(Side.Friend).Any() ? GameStatus.EnemyWon :
        !Truth.LivingOf(Side.Enemy).Any() ? GameStatus.FriendWon :
        GameStatus.Ongoing;

    // ====================================================================
    //  玩家 API
    // ====================================================================

    /// <summary>下达意图:命令由传令兵从中军帐送往该部队(延迟 + 副将解读)。</summary>
    public void IssueIntent(int unitId, Intent intent)
    {
        var u = Truth.UnitById(unitId);
        Truth.Messengers.Add(new Messenger
        {
            Id = _nextMessengerId++,
            Side = Side.Friend,
            Pos = Truth.FriendHq,
            Destination = u?.Pos ?? Truth.FriendHq,
            HomingUnitId = unitId,
            Vision = Difficulty.MessengerVision,
            Command = new CommandPayload { TargetUnitId = unitId, Intent = intent },
            PlayerDispatched = true
        });
        Log.Add($"t{Truth.Tick} 【令旗】向 #{unitId} 发出:{intent}(传令兵出发)");
    }

    /// <summary>主动派传令兵去问某部队近况(往返延迟;可能纯沉默)。</summary>
    public void RequestStatus(int unitId)
    {
        var u = Truth.UnitById(unitId);
        Truth.Messengers.Add(new Messenger
        {
            Id = _nextMessengerId++,
            Side = Side.Friend,
            Pos = Truth.FriendHq,
            Destination = u?.Pos ?? Truth.FriendHq,
            HomingUnitId = unitId,
            Vision = Difficulty.MessengerVision,
            PlayerDispatched = true
        });
        Log.Add($"t{Truth.Tick} 【探问】派传令兵询问 #{unitId} 近况");
    }

    /// <summary>中军帐直接派斥候去某处侦察(返回中军并直接更新认知世界)。</summary>
    public void DispatchScoutFromHq(Vec2 target)
    {
        int active = Truth.Scouts.Count(s => s.OwnerUnitId is null && s.Side == Side.Friend);
        if (active >= Difficulty.HqScoutPool) { Log.Add($"t{Truth.Tick} (中军斥候已派满 {Difficulty.HqScoutPool})"); return; }
        SpawnScout(Side.Friend, null, Truth.FriendHq, target);
        Log.Add($"t{Truth.Tick} 【斥候】中军派斥候侦察 {target}");
    }

    /// <summary>派传令兵命令某部队派斥候到指定位置(命令本身也有送达延迟)。</summary>
    public void OrderUnitScout(int unitId, Vec2 target)
    {
        var u = Truth.UnitById(unitId);
        Truth.Messengers.Add(new Messenger
        {
            Id = _nextMessengerId++,
            Side = Side.Friend,
            Pos = Truth.FriendHq,
            Destination = u?.Pos ?? Truth.FriendHq,
            HomingUnitId = unitId,
            Vision = Difficulty.MessengerVision,
            Command = new CommandPayload { TargetUnitId = unitId, ScoutTo = target },
            PlayerDispatched = true
        });
        Log.Add($"t{Truth.Tick} 【令旗】令 #{unitId} 派斥候至 {target}(传令兵出发)");
    }

    /// <summary>中军帐发出旗/鼓信号:近即时但需通视/在声程,带宽低,且会被敌方读到(《军争》金鼓旌旗)。</summary>
    public void SignalOrder(SignalKind kind, SignalCode code, int? targetUnitId = null)
    {
        _pendingSignals.Add(new PendingSignal(kind, code, targetUnitId));
        string who = targetUnitId is int id ? $"#{id}" : "全军";
        Log.Add($"t{Truth.Tick} 【{kind.Cn()}】向{who}发出「{code.Cn()}」号令");
    }

    /// <summary>遣使赴帅帐,呈报当前所知敌情(HqLinkDelay 后送达;送达才算「回报」目标)。
    /// 这是你操纵帅帐认知的杠杆——报什么、何时报,决定主帅怎么看你。</summary>
    public void ReportToHq()
    {
        int n = Beliefs[Side.Friend].KnownOf(Side.Enemy).Count();
        _hqReports.Add((Truth.Tick + HqLinkDelay, n, Truth.Tick));
        Log.Add($"t{Truth.Tick} 【上报】遣使赴帅帐呈报(敌情 {n} 条,在途)");
    }

    // ====================================================================
    //  主循环
    // ====================================================================

    public void AdvanceTick()
    {
        Truth.Tick++;
        ApplyScheduledOrders();  // 剧本触发(袭击队等)
        ProcessHqLink();         // 帅帐链路:中军令送达 / 上报送达
        ResolveSignals();
        MoveUnits();
        ResolveRanged();     // 弓/骑射:隔空放箭(先于近战软化敌军)
        ResolveCombat();     // 近战:含骑兵冲锋 + 重步抗线
        UpdateMoraleStamina();
        GenerateReports();
        ObserveFromWatchtower();
        EnemyThink();
        AutonomousScouting();
        MoveScouts();
        MoveMessengers();
        RecordFrame();
        Mission?.Evaluate(this);

        if (BattleOver && Appraisal is null && Mission is not null)
        {
            Appraisal = PoliticalJudge.Judge(this);
            Log.Add($"t{Truth.Tick} 【帅帐】战役毕,评语至:信任 {Appraisal.Trust}——{Appraisal.VerdictCn}");
            Alerts.Add(new Alert(Truth.Tick, "战役毕——帅帐评语已到", true));
        }
    }

    private void ApplyScheduledOrders()
    {
        for (int i = ScheduledOrders.Count - 1; i >= 0; i--)
        {
            var (tick, uid, intent) = ScheduledOrders[i];
            if (Truth.Tick < tick) continue;
            if (Truth.UnitById(uid) is { Alive: true } u) u.Order = intent;
            ScheduledOrders.RemoveAt(i);
        }
    }

    /// <summary>帅帐链路:分阶段中军令按时送达(激活目标);玩家上报延迟送达(置位回报目标)。</summary>
    private void ProcessHqLink()
    {
        if (Mission is not { } m) return;

        foreach (var o in m.Orders.Where(o => !o.Delivered && Truth.Tick >= o.ArriveTick))
        {
            o.Delivered = true;
            foreach (var oid in o.ActivatesObjectives)
                if (m[oid] is { } obj) obj.Active = true;
            if (o.ActivatesObjectives.Contains("C")) m.AidOrderDeliveredTick = Truth.Tick;
            Log.Add($"t{Truth.Tick} 【中军令·{o.TitleCn}】{o.TextCn}");
            Alerts.Add(new Alert(Truth.Tick, $"中军令·{o.TitleCn}:{o.TextCn}", true));
        }

        for (int i = _hqReports.Count - 1; i >= 0; i--)
        {
            var (arrive, n, sent) = _hqReports[i];
            if (Truth.Tick < arrive) continue;
            _hqReports.RemoveAt(i);
            if (n > 0 && m["B"] is { Active: true } b && b.State != ObjectiveState.Done) b.State = ObjectiveState.Done;
            if (m.AidOrderDeliveredTick is int adt && sent >= adt && m["D"] is { Active: true } d) d.State = ObjectiveState.Done;
            Log.Add($"t{Truth.Tick} 【帅帐】收悉尔部所报({n} 条敌情)");
            Alerts.Add(new Alert(Truth.Tick, $"帅帐收悉尔部上报({n} 条敌情)", false));
        }
    }

    private void MoveUnits()
    {
        foreach (var u in Truth.LivingOf(Side.Friend).Concat(Truth.LivingOf(Side.Enemy)).ToList())
        {
            u.MovedThisTick = false;
            if (ResolveTarget(u) is not Vec2 dest) continue;
            if (dest != u.Pos) u.Facing = SignDir(dest - u.Pos);          // 面向目标(即便贴身不动)
            if (u.Pos == dest) continue;

            double speed = u.BaseSpeed * (0.5 + 0.5 * u.Stamina / 100.0) * MoveRate;
            u.MoveAccumulator += speed;
            int guard = 0;
            while (u.Pos != dest && guard++ < 8)
            {
                var next = StepCell(u.Pos, dest);
                double cost = Truth.Terrain.MoveCost(next);
                if (u.MoveAccumulator < cost) break;
                u.MoveAccumulator -= cost;
                u.Facing = next - u.Pos;                                  // 面向前进方向
                u.Pos = next;
                u.MovedThisTick = true;
                u.Stamina = Math.Max(0, u.Stamina - 0.6);
            }
        }
    }

    private Vec2? ResolveTarget(Unit u) => u.Order?.Verb switch
    {
        Verb.Move => u.Order!.TargetPos,
        Verb.Retreat => u.Order!.TargetPos,
        Verb.Attack => Truth.UnitById(u.Order!.TargetUnitId ?? -1) is { Alive: true } t ? t.Pos : u.Order!.TargetPos,
        _ => null
    };

    private void ResolveCombat()
    {
        var processed = new HashSet<(int, int)>();
        foreach (var f in Truth.LivingOf(Side.Friend).ToList())
        {
            foreach (var e in Truth.LivingOf(Side.Enemy).ToList())
            {
                if (f.Pos.Manhattan(e.Pos) > 1) continue;
                var key = (Math.Min(f.Id, e.Id), Math.Max(f.Id, e.Id));
                if (!processed.Add(key)) continue;
                Fight(f, e);
            }
        }
    }

    private void Fight(Unit a, Unit b)
    {
        double dmgToB = CombatK * a.Strength * Unit.TypeMatchup(a.Type, b.Type)
                        * MoraleFactor(a) * StaminaFactor(a) * ChargeMultOf(a) * _rng.Jitter(0.2) / DefenseOf(b);
        double dmgToA = CombatK * b.Strength * Unit.TypeMatchup(b.Type, a.Type)
                        * MoraleFactor(b) * StaminaFactor(b) * ChargeMultOf(b) * _rng.Jitter(0.2) / DefenseOf(a);

        b.Strength = Math.Max(0, b.Strength - dmgToB);
        a.Strength = Math.Max(0, a.Strength - dmgToA);

        a.Morale -= dmgToA / Math.Max(1, a.MaxStrength) * 100 * 1.5;
        b.Morale -= dmgToB / Math.Max(1, b.MaxStrength) * 100 * 1.5;
        a.Stamina = Math.Max(0, a.Stamina - 2);
        b.Stamina = Math.Max(0, b.Stamina - 2);

        foreach (var u in new[] { a, b })
        {
            if (u.Morale <= 0 && !u.Routed) { u.Routed = true; TruthLog.Add($"t{Truth.Tick} #{u.Id} 士气崩溃,溃逃"); }
            if (u.Strength <= 0) TruthLog.Add($"t{Truth.Tick} #{u.Id} 被歼灭");
        }
    }

    /// <summary>放箭阶段:弓/骑射对射程内(非贴身)最近之敌造成伤害,目标不还手(近战另算)。
    /// 盾抗箭、弓近身即崩 —— 由兵种克制 + 护甲共同体现。</summary>
    private void ResolveRanged()
    {
        foreach (var shooter in Truth.Living.Where(u => u.IsRanged).ToList())
        {
            var target = Truth.LivingOf(shooter.Side.Opponent())
                .Where(e => shooter.Pos.Manhattan(e.Pos) >= 2 && shooter.Pos.DistanceTo(e.Pos) <= shooter.ShootRange)
                .OrderBy(e => shooter.Pos.DistanceTo(e.Pos))
                .FirstOrDefault();
            if (target is null) continue;

            double dmg = RangedK * shooter.Strength * Unit.TypeMatchup(shooter.Type, target.Type)
                         * MoraleFactor(shooter) * StaminaFactor(shooter) * _rng.Jitter(0.2) / DefenseOf(target);
            target.Strength = Math.Max(0, target.Strength - dmg);
            target.Morale -= dmg / Math.Max(1, target.MaxStrength) * 100 * 1.2;
            shooter.Stamina = Math.Max(0, shooter.Stamina - 0.4);
            shooter.Facing = SignDir(target.Pos - shooter.Pos);

            if (target.Morale <= 0 && !target.Routed) { target.Routed = true; TruthLog.Add($"t{Truth.Tick} #{target.Id} 中箭士气崩溃,溃逃"); }
            if (target.Strength <= 0) TruthLog.Add($"t{Truth.Tick} #{target.Id} 被箭雨歼灭");
        }
    }

    /// <summary>受伤时的总防御除数:地形 × 兵种护甲 × 据守抗线加成(重步据阵)。</summary>
    private double DefenseOf(Unit u)
    {
        double d = DefenseMult(u.Pos) * u.ArmArmor;
        bool braced = u.IsHeavyFoot && (u.Order is null || u.Order.Verb == Verb.Hold);
        if (braced) d *= 1.3;                                       // 结阵据守:抗线更硬
        return d;
    }

    /// <summary>骑兵冲击:移动接敌(有冲力)伤害大增,已停下的骑乘近战则较小。</summary>
    private static double ChargeMultOf(Unit u) =>
        !u.IsCavalry ? 1.0 : u.MovedThisTick ? 1.5 : 1.1;

    /// <summary>把一个位移量取成格方向(各分量取符号)。</summary>
    private static Vec2 SignDir(Vec2 d) => new(Math.Sign(d.X), Math.Sign(d.Y));

    private void UpdateMoraleStamina()
    {
        foreach (var u in Truth.Living.ToList())
        {
            bool resting = u.Order is null || u.Order.Verb == Verb.Hold;
            if (resting)
            {
                u.Stamina = Math.Min(100, u.Stamina + 1.0);
                u.Morale = Math.Min(100, u.Morale + 0.3);
            }
            u.Morale = Math.Clamp(u.Morale, 0, 100);
            u.Stamina = Math.Clamp(u.Stamina, 0, 100);
        }
    }

    // ====================================================================
    //  情报生成(失真管线)
    // ====================================================================

    private void GenerateReports()
    {
        foreach (var observer in Truth.Living.ToList())
        {
            // 1) 更新本部队的局部敌情:它此刻看得见的敌军,记进自己的小认知(真相快照 + 观测时刻)
            double range = observer.Vision * Truth.Terrain.ScoutMultiplier(observer.Pos);
            foreach (var foe in Truth.LivingOf(observer.Side.Opponent()).ToList())
            {
                if (observer.Pos.DistanceTo(foe.Pos) > range) continue;
                observer.Knowledge[foe.Id] = new GhostUnit
                {
                    UnitId = foe.Id, Side = foe.Side, LastKnownPos = foe.Pos,
                    KnownStrength = foe.Strength, KnownType = foe.Type, ObservedTick = Truth.Tick
                };
            }

            // 2) 周期性派传令兵,打包带回「自身近况 + 它所知的全部敌情」(各自带各自的观测时刻)
            if (_rng.Chance(0.15))
                SpawnBundle(observer.Pos, observer.Side, PackKnowledge(observer));
        }
    }

    /// <summary>瞭望台(高处观察):中军帐近处的实时、低保真直接观察。每 tick 重算成「烟尘印象」叠加层,
    /// 不写入认知世界(那是延迟情报的领域);只给位置 + 规模档,不给精确兵力/兵种;随天气压缩、只覆盖近场。</summary>
    private void ObserveFromWatchtower()
    {
        WatchtowerContacts.Clear();
        var tower = Truth.FriendHq;
        foreach (var u in Truth.Living)
        {
            double range = Difficulty.WatchtowerRange * Truth.Terrain.ScoutMultiplier(u.Pos);
            if (tower.DistanceTo(u.Pos) <= range)
                WatchtowerContacts.Add(new WatchtowerContact(u.Side, u.Pos, ScaleOf(u.Strength)));
        }
    }

    private static ScaleHint ScaleOf(double strength) =>
        strength >= 120 ? ScaleHint.Large : strength >= 70 ? ScaleHint.Medium : ScaleHint.Small;

    /// <summary>把一支部队此刻所知的情报打成一包:自身近况 + 它记得的敌情(可能已滞后)。</summary>
    private List<Report> PackKnowledge(Unit u)
    {
        var bundle = new List<Report> { BuildReport(u, isSelf: true, "近况") };
        foreach (var g in u.Knowledge.Values)
            bundle.Add(BuildReportFromGhost(g, "敌情"));
        return bundle;
    }

    private Report BuildReport(Unit target, bool isSelf, string kind)
    {
        var (strength, type, fidelity) = Distort(target.Strength, target.Type, isSelf);
        return new Report
        {
            ObservedTick = Truth.Tick,
            Fidelity = fidelity,
            Kind = kind,
            About = new PartialUnitInfo
            {
                UnitId = target.Id, Side = target.Side, Pos = target.Pos, Strength = strength, Type = type
            }
        };
    }

    /// <summary>把部队记得的敌情(它当时所见的真相快照)转成一份(可能失真的)战报,沿用它的观测时刻。</summary>
    private Report BuildReportFromGhost(GhostUnit g, string kind)
    {
        var (strength, type, fidelity) = Distort(g.KnownStrength ?? 0, g.KnownType ?? UnitType.Spear, isSelf: false);
        return new Report
        {
            ObservedTick = g.ObservedTick,   // 关键:沿用观测时刻 → 自带信息年龄
            Fidelity = fidelity,
            Kind = kind,
            About = new PartialUnitInfo
            {
                UnitId = g.UnitId, Side = g.Side, Pos = g.LastKnownPos, Strength = strength, Type = type
            }
        };
    }

    /// <summary>失真管线:自身情报准确;敌情按难度掷出残缺(null)/失真(数字偏移)。</summary>
    private (double? strength, UnitType? type, double fidelity) Distort(double truthStrength, UnitType truthType, bool isSelf)
    {
        if (isSelf) return (truthStrength, truthType, 1.0);
        double? strength = truthStrength;
        UnitType? type = truthType;
        double fidelity = 1.0;
        if (_rng.Chance(Difficulty.PartialChance)) strength = null;
        else if (_rng.Chance(Difficulty.DistortionChance))
        {
            double f = _rng.NextDouble() * 1.2 + 0.5;       // 0.5..1.7
            strength = Math.Round(truthStrength * f);
            fidelity = 0.5;
        }
        if (_rng.Chance(Difficulty.PartialChance * 0.5)) type = null;
        return (strength, type, fidelity);
    }

    private void SpawnBundle(Vec2 from, Side side, List<Report> reports)
    {
        if (reports.Count == 0) return;
        Truth.Messengers.Add(new Messenger
        {
            Id = _nextMessengerId++,
            Side = side,
            Pos = from,
            Destination = Truth.HqOf(side),
            Vision = Difficulty.MessengerVision,
            Reports = reports
        });
    }

    // ====================================================================
    //  传令兵推进 / 送达 / 拦截
    // ====================================================================

    private void MoveMessengers()
    {
        // 遍历快照:Deliver 在送达「探问」时会当场派出回程传令兵,会改动 Truth.Messengers。
        var current = Truth.Messengers.ToList();
        Truth.Messengers.Clear();
        foreach (var m in current)
        {
            if (!m.Alive) continue;

            if (m.HomingUnitId is int hid)
            {
                var tu = Truth.UnitById(hid);
                if (tu is { Alive: true }) m.Destination = tu.Pos;
                else if (m.IsCommand) { TruthLog.Add($"t{Truth.Tick} 命令目标 #{hid} 已不在,作废"); continue; }
            }

            m.Accum += Difficulty.MessengerSpeed;
            while (m.Accum >= 1 && m.Pos != m.Destination)
            {
                m.Pos = StepCell(m.Pos, m.Destination);
                m.Accum -= 1;
                if (m.Vision > 0) RecordSightings(m.Sightings, m.Pos, m.Vision, m.Side);  // 途中瞥见敌军
                double intercept = Difficulty.InterceptChancePerTick * Truth.Terrain.InterceptMultiplier(m.Pos);
                if (_rng.Chance(intercept)) { m.Alive = false; OnMessengerLost(m); break; }
            }
            if (!m.Alive) continue;

            if (m.Pos == m.Destination) { Deliver(m); continue; } // 送达;Deliver 内派出的回程会 Add 进(已清空的)列表并保留
            Truth.Messengers.Add(m);                              // 幸存者放回
        }
    }

    private void OnMessengerLost(Messenger m)
    {
        TruthLog.Add($"t{Truth.Tick} 传令兵#{m.Id} 在 {m.Pos} 被拦截/阵亡(载荷丢失)");
        // 丢失 = 纯沉默;仅在低难度对玩家主动派出者给「未归」提示。
        if (m.Side == Side.Friend && m.PlayerDispatched && Difficulty.NotifyLostActiveMessenger)
        {
            Log.Add($"t{Truth.Tick} ⚠ 派往 #{m.HomingUnitId} 的传令兵迟迟未归……");
            Alerts.Add(new Alert(Truth.Tick, $"派往 #{m.HomingUnitId} 的传令兵迟迟未归……", true));
        }
    }

    private void Deliver(Messenger m)
    {
        if (m.Reports.Count > 0)
        {
            var belief = Beliefs[m.Side];
            int freshEnemy = 0;
            foreach (var r in m.Reports)
            {
                bool newer = !belief.Known.TryGetValue(r.About.UnitId, out var g) || r.ObservedTick > g.ObservedTick;
                belief.Integrate(r);
                if (newer && r.About.Side == Side.Enemy) freshEnemy++;
                if (m.Side == Side.Friend && newer) Log.Add(FormatReport(r)); // 只报真正更新的情报,免刷屏
            }
            freshEnemy += FlushSightingsToBelief(m.Sightings, m.Side);         // 传令兵途中所见 → 中军认知
            if (m.Side == Side.Friend && freshEnemy > 0)
                Alerts.Add(new Alert(Truth.Tick, $"新敌情战报送达({freshEnemy} 条)", false));
        }
        else if (m.Command is CommandPayload c)
        {
            var u = Truth.UnitById(c.TargetUnitId);
            if (u is { Alive: true })
            {
                if (c.ScoutTo is Vec2 sp)
                {
                    DispatchScout(u.Side, u, sp);                              // 命令该部队派斥候
                    if (m.Side == Side.Friend) Log.Add($"t{Truth.Tick} 【送达】#{u.Id} 收到命令,派出斥候 → {sp}");
                }
                else if (c.Intent is Intent intent)
                {
                    u.Order = Interpret(u, intent);
                    if (m.Side == Side.Friend) Log.Add($"t{Truth.Tick} 【送达】#{u.Id} 收到命令并开始执行");
                }
                foreach (var kv in m.Sightings) u.Knowledge[kv.Key] = kv.Value; // 传令兵途中所见 → 交给该部队
            }
        }
        else if (m.HomingUnitId is int qid) // 探问到达:回程带该部队完整局部情报包(自身近况 + 它所知敌情)
        {
            var u = Truth.UnitById(qid);
            if (u is { Alive: true })
                Truth.Messengers.Add(new Messenger
                {
                    Id = _nextMessengerId++,
                    Side = m.Side,
                    Pos = u.Pos,
                    Destination = Truth.HqOf(m.Side),
                    Vision = Difficulty.MessengerVision,
                    Reports = PackKnowledge(u),
                    PlayerDispatched = m.PlayerDispatched
                });
        }
    }

    private string FormatReport(Report r)
    {
        var a = r.About;
        string who = a.Side == Side.Enemy ? "敌" : "我";
        string str = a.Strength is double v ? ((int)v).ToString() : "?";
        string ty = a.Type?.ToString() is string t ? t : "?";
        string doubt = (r.Fidelity < 1 && Difficulty.ShowCredibilityHint) ? " (存疑)" : "";
        return $"t{Truth.Tick} 【战报·{r.Kind}】{who}#{a.UnitId}({ty}) @ {a.Pos} 兵{str}{doubt} [观测t{r.ObservedTick}]";
    }

    // ====================================================================
    //  副将解读(第二层迷雾,§九/§十)
    // ====================================================================

    private Intent Interpret(Unit unit, Intent intent)
    {
        var cmdr = unit.Commander;
        Verb verb = intent.Verb;
        Tone tone = intent.Tone;
        Vec2? tpos = intent.TargetPos;
        int? tid = intent.TargetUnitId;

        // 能力不足 → 误读基调
        if (_rng.Chance(1 - cmdr.Competence)) tone = ShiftTone(tone);

        // 情况已变:要打的敌军不在了 → 按性格临机改判
        if (verb == Verb.Attack && tid is int aid)
        {
            var tgt = Truth.UnitById(aid);
            if (tgt is null || !tgt.Alive)
                (verb, tpos, tid) = Improvise(unit);
            else if (unit.Pos.DistanceTo(tgt.Pos) > 6 && cmdr.Personality == Personality.Cautious && tone == Tone.Cautious)
                verb = Verb.Hold;
        }

        // 性格 vs 基调摩擦
        if (cmdr.Personality == Personality.Aggressive && tone == Tone.Cautious) tone = Tone.Normal;
        if (cmdr.Personality == Personality.Cautious && tone == Tone.AllOut) tone = Tone.Aggressive;

        var executed = new Intent { Verb = verb, Tone = tone, TargetPos = tpos, TargetUnitId = tid };
        if (!SameIntent(intent, executed))
            TruthLog.Add($"t{Truth.Tick} 副将{cmdr.Name}({cmdr.PersonalityCn}) 将「{intent}」执行为「{executed}」");
        return executed;
    }

    private (Verb, Vec2?, int?) Improvise(Unit unit)
    {
        var ghosts = Beliefs[unit.Side].KnownOf(unit.Side.Opponent()).ToList();
        if (ghosts.Count == 0) return (Verb.Hold, null, null);
        var nearest = ghosts.OrderBy(g => unit.Pos.DistanceTo(g.LastKnownPos)).First();
        return unit.Commander.Personality switch
        {
            Personality.Cautious => (Verb.Hold, null, null),
            Personality.Aggressive => (Verb.Attack, nearest.LastKnownPos, nearest.UnitId),
            _ => (Verb.Move, nearest.LastKnownPos, null)
        };
    }

    private Tone ShiftTone(Tone t)
    {
        int i = (int)t + (_rng.Chance(0.5) ? 1 : -1);
        i = Math.Clamp(i, 0, (int)Tone.UseJudgment);
        return (Tone)i;
    }

    private static bool SameIntent(Intent a, Intent b) =>
        a.Verb == b.Verb && a.Tone == b.Tone && a.TargetPos.Equals(b.TargetPos) && a.TargetUnitId == b.TargetUnitId;

    // ====================================================================
    //  敌方 Utility AI(对称迷雾:基于敌方自己的认知世界,§十五)
    // ====================================================================

    private void EnemyThink()
    {
        if (Truth.Tick % 3 != 0) return;
        var belief = Beliefs[Side.Enemy];
        var ghosts = belief.KnownOf(Side.Friend).ToList();

        foreach (var e in Truth.LivingOf(Side.Enemy).Where(u => !u.AiExempt).ToList())
        {
            var (atkW, riskW, holdW) = Weights(e.Commander.Personality);
            Intent best = Intent.Hold();
            double bestU = holdW - 0.1;

            foreach (var g in ghosts)
            {
                double dist = e.Pos.DistanceTo(g.LastKnownPos);
                double gs = g.KnownStrength ?? e.Strength;
                double power = e.Strength / (gs + 1);
                double u = atkW * power - riskW * (gs / (e.Strength + 1)) - dist * 0.05;
                if (u > bestU) { bestU = u; best = Intent.Move(g.LastKnownPos); } // 进军到「最后已知」位置(可能你已不在那)
            }

            // 决策质量:有概率走神
            if (_rng.Chance(1 - Difficulty.EnemyDecisionQuality))
                best = ghosts.Count > 0 ? Intent.Move(ghosts[_rng.Next(ghosts.Count)].LastKnownPos) : Intent.Hold();

            e.Order = best;
        }
    }

    private static (double atk, double risk, double hold) Weights(Personality p) => p switch
    {
        Personality.Aggressive => (1.2, 0.2, 0.1),
        Personality.Cautious => (0.5, 1.0, 0.6),
        Personality.Cunning => (0.9, 0.4, 0.2),
        _ => (0.8, 0.6, 0.3)
    };

    // ====================================================================
    //  旗鼓信号(低带宽、近即时、需通视/在声程、会被敌方读到 · §军争)
    // ====================================================================

    private void ResolveSignals()
    {
        if (_pendingSignals.Count == 0) return;
        var origin = Truth.FriendHq;
        foreach (var sig in _pendingSignals)
        {
            var addressed = (sig.TargetUnitId is int tid
                ? Truth.LivingOf(Side.Friend).Where(u => u.Id == tid)
                : Truth.LivingOf(Side.Friend)).ToList();

            foreach (var u in addressed)
            {
                if (!Perceives(sig.Kind, origin, u.Pos))
                {
                    TruthLog.Add($"t{Truth.Tick} #{u.Id} 未接到{sig.Kind.Cn()}「{sig.Code.Cn()}」(超出{(sig.Kind == SignalKind.Flag ? "视程" : "声程")})");
                    continue;                                          // 看不见/听不到 → 令不达(纯沉默)
                }
                var code = MaybeMisread(sig.Kind, origin, u, sig.Code);
                u.Order = SignalToIntent(code);
                if (code != sig.Code)
                    TruthLog.Add($"t{Truth.Tick} #{u.Id} 能见度差,把{sig.Kind.Cn()}「{sig.Code.Cn()}」误读为「{code.Cn()}」");

                // 泄密:收令而动的部队动作显眼,敌在其旗/鼓可及范围内即窥得其位置(强度不明)
                foreach (var e in Truth.LivingOf(Side.Enemy))
                {
                    if (!Perceives(sig.Kind, u.Pos, e.Pos)) continue;
                    MergeGhost(Beliefs[Side.Enemy], new GhostUnit
                    {
                        UnitId = u.Id, Side = Side.Friend, LastKnownPos = u.Pos,
                        KnownStrength = null, KnownType = u.Type, ObservedTick = Truth.Tick, Fidelity = 0.7
                    }, friendLog: false);
                    TruthLog.Add($"t{Truth.Tick} 敌#{e.Id} 因我{sig.Kind.Cn()}号令,窥得我#{u.Id} @ {u.Pos}");
                }
            }
        }
        _pendingSignals.Clear();
    }

    /// <summary>能否收到信号:旌旗按视程(地形/天气打折)、金鼓按声程(地形阻尼)。</summary>
    private bool Perceives(SignalKind kind, Vec2 origin, Vec2 observer) =>
        origin.DistanceTo(observer) <= EffectiveRange(kind, observer);

    private double EffectiveRange(SignalKind kind, Vec2 at) => kind == SignalKind.Flag
        ? Difficulty.FlagRange * Truth.Terrain.ScoutMultiplier(at)
        : Difficulty.DrumRange * Truth.Terrain.SoundMultiplier(at);

    /// <summary>能见度差(靠近可及边缘)→ 有概率把信号误读成相邻词。</summary>
    private SignalCode MaybeMisread(SignalKind kind, Vec2 origin, Unit u, SignalCode code)
    {
        double eff = EffectiveRange(kind, u.Pos);
        double clarity = eff <= 0 ? 0 : 1 - origin.DistanceTo(u.Pos) / eff;
        return _rng.Chance(Math.Clamp((1 - clarity) * 0.5, 0, 0.5)) ? ShiftSignal(code) : code;
    }

    private SignalCode ShiftSignal(SignalCode c)
    {
        int i = Math.Clamp((int)c + (_rng.Chance(0.5) ? 1 : -1), 0, (int)SignalCode.Rally);
        return (SignalCode)i;
    }

    private Intent SignalToIntent(SignalCode code) => code switch
    {
        SignalCode.Advance => Intent.Move(Truth.EnemyHq, Tone.Aggressive),
        SignalCode.Halt => Intent.Hold(),
        SignalCode.Retreat => Intent.Retreat(Truth.FriendHq),
        _ => Intent.Move(Truth.FriendHq)                         // Rally:集结到中军帐
    };

    // ====================================================================
    //  斥候(视野大、速度中、避敌绕道、到点回报)
    // ====================================================================

    private void SpawnScout(Side side, Unit? owner, Vec2 from, Vec2 target)
    {
        Truth.Scouts.Add(new Scout
        {
            Id = _nextScoutId++,
            Side = side,
            Pos = from,
            Target = target,
            OwnerUnitId = owner?.Id,
            Vision = Difficulty.ScoutVision,
            Home = owner?.Pos ?? Truth.HqOf(side)
        });
    }

    /// <summary>部队派斥候(受该部队斥候上限约束)。</summary>
    private void DispatchScout(Side side, Unit owner, Vec2 target)
    {
        if (Truth.Scouts.Count(s => s.OwnerUnitId == owner.Id) >= owner.ScoutPool) return;
        SpawnScout(side, owner, owner.Pos, target);
    }

    /// <summary>等待中的部队按将领性格自发派斥候,拓展自己的视野(§十二 等待是玩法)。</summary>
    private void AutonomousScouting()
    {
        foreach (var u in Truth.Living.ToList())
        {
            bool idle = u.Order is null || u.Order.Verb == Verb.Hold;            // 「在等待时」
            bool inCombat = Truth.LivingOf(u.Side.Opponent()).Any(e => e.Pos.Manhattan(u.Pos) <= 1);
            if (!idle || inCombat) continue;
            if (Truth.Scouts.Count(s => s.OwnerUnitId == u.Id) >= u.ScoutPool) continue;
            var (chance, reach, lateral) = ScoutStyle(u.Commander.Personality);
            if (!_rng.Chance(chance)) continue;
            SpawnScout(u.Side, u, u.Pos, ScoutTargetFor(u, reach, lateral));
        }
    }

    private static (double chance, int reach, int lateral) ScoutStyle(Personality p) => p switch
    {
        Personality.Aggressive => (0.20, 8, 0),   // 频繁、向前探得深
        Personality.Cautious   => (0.30, 4, 3),   // 最勤、近距、顾侧翼
        Personality.Cunning    => (0.18, 6, 4),   // 偏侧翼 / 迂回
        _                      => (0.15, 6, 1)     // 稳健:适中
    };

    private Vec2 ScoutTargetFor(Unit u, int reach, int lateral)
    {
        var dir = Truth.HqOf(u.Side.Opponent()) - u.Pos;                         // 朝敌方大本营方向
        int sx = Math.Sign(dir.X), sy = Math.Sign(dir.Y);
        int off = lateral == 0 ? 0 : _rng.Next(-lateral, lateral + 1);
        return new Vec2(
            Math.Clamp(u.Pos.X + sx * reach, 0, Truth.Terrain.Width - 1),
            Math.Clamp(u.Pos.Y + sy * reach + off, 0, Truth.Terrain.Height - 1));
    }

    private void MoveScouts()
    {
        var current = Truth.Scouts.ToList();
        Truth.Scouts.Clear();
        foreach (var s in current)
        {
            if (s.Done) continue;
            // 刷新返回点:部队派出的随部队走;部队已亡则回中军帐
            s.Home = s.OwnerUnitId is int oid && Truth.UnitById(oid) is { Alive: true } ow ? ow.Pos : Truth.HqOf(s.Side);
            Vec2 goal = s.Returning ? s.Home : s.Target;

            s.Accum += Difficulty.ScoutSpeed;
            int guard = 0;
            while (s.Accum >= 1 && s.Pos != goal && guard++ < 8)
            {
                var step = ChooseScoutStep(s, goal);
                if (step is not Vec2 nx) { s.Patience--; break; }                // 被敌封住,绕不过
                s.Facing = nx - s.Pos;
                s.Pos = nx;
                s.Accum -= 1;
                RecordSightings(s.Sightings, s.Pos, s.Vision, s.Side);
            }
            RecordSightings(s.Sightings, s.Pos, s.Vision, s.Side);

            if (!s.Returning)
            {
                if (s.Pos == s.Target || s.Patience <= 0) s.Returning = true;    // 到达目标 或 绕不过 → 回报
            }
            else if (s.Pos == s.Home)
            {
                DeliverScout(s);
                continue;                                                         // Done,不放回列表
            }
            Truth.Scouts.Add(s);
        }
    }

    /// <summary>选下一步:朝目标推进,但不踏入「能看见的敌军」的 2 格内(不靠近 → 自然绕道)。</summary>
    private Vec2? ChooseScoutStep(Scout s, Vec2 goal)
    {
        const int avoid = 2;
        var seen = Truth.LivingOf(s.Side.Opponent()).Where(e => s.Pos.DistanceTo(e.Pos) <= s.Vision).ToList();
        var cands = new List<Vec2> { StepCell(s.Pos, goal) };
        foreach (var d in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1) })
            cands.Add(s.Pos + d);

        Vec2? best = null;
        double bestDist = double.MaxValue;
        foreach (var c in cands)
        {
            if (c == s.Pos || !Truth.Terrain.InBounds(c)) continue;
            if (seen.Any(e => c.DistanceTo(e.Pos) < avoid)) continue;            // 不靠近敌军
            double d2 = c.DistanceTo(goal);
            if (d2 < bestDist) { bestDist = d2; best = c; }
        }
        return best;
    }

    private void DeliverScout(Scout s)
    {
        if (s.OwnerUnitId is int oid)
        {
            var owner = Truth.UnitById(oid);
            if (owner is { Alive: true })
            {
                foreach (var kv in s.Sightings) owner.Knowledge[kv.Key] = kv.Value;     // 汇报给该部队
                if (s.Side == Side.Friend && s.Sightings.Count > 0)                      // 空手而归不刷屏
                    Log.Add($"t{Truth.Tick} #{oid} 的斥候归来,带回敌情 {s.Sightings.Count} 条(将经传令兵转呈中军)");
            }
        }
        else
        {
            int fresh = FlushSightingsToBelief(s.Sightings, s.Side);                     // 中军斥候 → 直接更新认知世界
            if (s.Side == Side.Friend)
            {
                Log.Add($"t{Truth.Tick} 中军斥候归来,敌情 {s.Sightings.Count} 条");
                if (fresh > 0) Alerts.Add(new Alert(Truth.Tick, $"斥候归来:侦获敌情 {fresh} 条", true));
            }
        }
    }

    /// <summary>把某点 vision 范围内的敌军记入字典(真相快照 + 当前观测时刻)。</summary>
    private void RecordSightings(Dictionary<int, GhostUnit> into, Vec2 pos, int vision, Side observerSide)
    {
        double range = vision * Truth.Terrain.ScoutMultiplier(pos);
        if (range <= 0) return;
        foreach (var foe in Truth.LivingOf(observerSide.Opponent()))
        {
            double dist = pos.DistanceTo(foe.Pos);
            if (dist > range) continue;
            into[foe.Id] = new GhostUnit
            {
                UnitId = foe.Id, Side = foe.Side, LastKnownPos = foe.Pos,
                KnownStrength = foe.Strength, KnownType = foe.Type, ObservedTick = Truth.Tick,
                Fidelity = Math.Clamp(1 - dist / range, 0, 1)   // 观测清晰度:越近越清(判读误差的依据)
            };
        }
    }

    private int FlushSightingsToBelief(Dictionary<int, GhostUnit> sightings, Side side)
    {
        int fresh = 0;
        foreach (var g in sightings.Values)
            if (MergeGhost(Beliefs[side], JudgeSighting(g), side == Side.Friend)) fresh++;
        return fresh;
    }

    /// <summary>斥候/传令兵把「亲眼所见的真相」判读成一份估计:越远越糊 —— 兵力报个约数(带误差)、
    /// 兵种可能认不清。同一次侦察里每个目标各按各自观测距离判读 → 近的准、远的糊(§情报失真)。</summary>
    private GhostUnit JudgeSighting(GhostUnit obs)
    {
        double closeness = Math.Clamp(obs.Fidelity, 0, 1);          // RecordSightings 存的观测清晰度
        double? est = obs.KnownStrength;
        UnitType? type = obs.KnownType;

        if (obs.KnownStrength is double truth)
        {
            double err = (1 - closeness) * 0.6;                     // 最远处 ±60%
            double f = 1 + (_rng.NextDouble() * 2 - 1) * err;
            est = Math.Max(10, Math.Round(truth * f / 10.0) * 10.0);// 取整到十:斥候报个约数
        }
        if (_rng.Chance((1 - closeness) * 0.5)) type = null;        // 远处认不清兵种

        return new GhostUnit
        {
            UnitId = obs.UnitId, Side = obs.Side, LastKnownPos = obs.LastKnownPos,
            KnownStrength = est, KnownType = type, ObservedTick = obs.ObservedTick,
            Fidelity = Math.Round(closeness, 2)
        };
    }

    private bool MergeGhost(BeliefWorld b, GhostUnit g, bool friendLog)
    {
        bool newer = !b.Known.TryGetValue(g.UnitId, out var ex) || g.ObservedTick > ex.ObservedTick;
        if (!newer) return false;
        b.Known[g.UnitId] = new GhostUnit
        {
            UnitId = g.UnitId, Side = g.Side, LastKnownPos = g.LastKnownPos,
            KnownStrength = g.KnownStrength, KnownType = g.KnownType, ObservedTick = g.ObservedTick, Fidelity = g.Fidelity
        };
        if (friendLog && g.Side == Side.Enemy)
            Log.Add($"t{Truth.Tick} 【侦获】敌#{g.UnitId} @ {g.LastKnownPos} [观测t{g.ObservedTick}]");
        return true;
    }

    /// <summary>把当前真相世界存为一帧(供复盘动画)。</summary>
    private void RecordFrame()
    {
        var f = new ReplayFrame { Tick = Truth.Tick };
        foreach (var u in Truth.Units)
            f.Units.Add(new ReplayUnit(u.Id, u.Side, u.Type, u.Name, u.Pos,
                (int)u.Strength, (int)u.Morale, u.Routed, u.Strength <= 0));
        foreach (var m in Truth.Messengers)
            f.Messengers.Add(new ReplayMover(m.Side, m.Pos, m.IsCommand));
        foreach (var s in Truth.Scouts)
            f.Scouts.Add(new ReplayMover(s.Side, s.Pos, false));
        Replay.Add(f);
    }

    // ====================================================================
    //  小工具
    // ====================================================================

    private static Vec2 StepCell(Vec2 from, Vec2 to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (dx == 0 && dy == 0) return from;
        if (Math.Abs(dx) >= Math.Abs(dy)) return from + new Vec2(Math.Sign(dx), 0);
        return from + new Vec2(0, Math.Sign(dy));
    }

    private static double MoraleFactor(Unit u) => 0.6 + 0.4 * u.Morale / 100.0;
    private static double StaminaFactor(Unit u) => 0.6 + 0.4 * u.Stamina / 100.0;
    private double DefenseMult(Vec2 p) => Truth.Terrain.At(p) switch
    {
        TerrainType.Forest => 1.25,
        TerrainType.River => 1.15,
        _ => 1.0
    };
}
