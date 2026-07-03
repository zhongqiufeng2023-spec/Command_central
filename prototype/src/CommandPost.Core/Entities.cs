namespace CommandPost.Core;

/// <summary>副将 / 将领。性格驱动解读(§十);原型只用 Personality + Competence。</summary>
public sealed class Commander
{
    public string Name { get; init; } = "";
    public Personality Personality { get; init; } = Personality.Steady;
    /// <summary>能力 0..1:越高解读越贴近你的本意、执行越稳。</summary>
    public double Competence { get; init; } = 0.7;

    public Commander(string name, Personality personality, double competence = 0.7)
    {
        Name = name;
        Personality = personality;
        Competence = competence;
    }

    public string PersonalityCn => Personality switch
    {
        Personality.Aggressive => "激进",
        Personality.Cautious => "谨慎",
        Personality.Cunning => "善诈",
        _ => "稳健"
    };
}

/// <summary>玩家下达的意图:动词 + 基调 + 目标(§九)。</summary>
public sealed class Intent
{
    public Verb Verb { get; init; }
    public Tone Tone { get; init; } = Tone.Normal;
    public Vec2? TargetPos { get; init; }
    public int? TargetUnitId { get; init; }

    public static Intent Move(Vec2 to, Tone tone = Tone.Normal) => new() { Verb = Verb.Move, TargetPos = to, Tone = tone };
    public static Intent Attack(int unitId, Tone tone = Tone.Normal) => new() { Verb = Verb.Attack, TargetUnitId = unitId, Tone = tone };
    public static Intent Hold(Tone tone = Tone.Normal) => new() { Verb = Verb.Hold, Tone = tone };
    public static Intent Retreat(Vec2 to, Tone tone = Tone.Cautious) => new() { Verb = Verb.Retreat, TargetPos = to, Tone = tone };

    public string VerbCn => Verb switch
    {
        Verb.Move => "移动", Verb.Attack => "攻击", Verb.Hold => "据守", _ => "后撤"
    };
    public string ToneCn => Tone switch
    {
        Tone.Cautious => "谨慎", Tone.Normal => "常规", Tone.Aggressive => "激进",
        Tone.AllOut => "不惜代价", _ => "见机行事"
    };

    public override string ToString()
    {
        string tgt = TargetUnitId is int id ? $"#{id}" : TargetPos?.ToString() ?? "-";
        return $"{VerbCn}({ToneCn})→{tgt}";
    }
}

/// <summary>真相世界中的一支部队(玩家永不可直接看见,只见认知世界的影子)。</summary>
public sealed class Unit
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public Faction Faction { get; init; } = Faction.Dafeng;
    public UnitType Type { get; init; }
    public string Name { get; init; } = "";
    public Commander Commander { get; init; } = new("无名", Personality.Steady);

    public Vec2 Pos { get; set; }
    public double Strength { get; set; }
    public double MaxStrength { get; init; }
    public double Morale { get; set; } = 100;   // 0..100,崩则溃逃
    public double Stamina { get; set; } = 100;   // 0..100,竭则战力打折

    /// <summary>副将当前正在执行的命令(已被解读过的,可能偏离玩家本意)。</summary>
    public Intent? Order { get; set; }
    public bool Routed { get; set; }

    /// <summary>移动进度累积器(按地形成本逐 tick 累加)。</summary>
    public double MoveAccumulator { get; set; }

    /// <summary>朝向(格方向,如 (1,0)):随移动/接敌更新,渲染画一个箭头鼻子。暂不参与战斗,预留侧翼/背刺。</summary>
    public Vec2 Facing { get; set; } = new Vec2(1, 0);
    /// <summary>本 tick 是否发生了移动(供骑兵冲锋判定:接敌时有冲力则伤害更高)。</summary>
    public bool MovedThisTick { get; set; }

    /// <summary>本部队视野半径(雾/夜/林会压缩)。</summary>
    public int Vision { get; init; } = 4;
    /// <summary>本部队可同时在外的斥候上限(由难度初始化)。</summary>
    public int ScoutPool { get; set; } = 2;

    /// <summary>本部队亲眼所见的敌情(它自己的小认知,真相快照 + 观测时刻);传令时随近况一并送回。</summary>
    public Dictionary<int, GhostUnit> Knowledge { get; } = new();

    public bool Alive => Strength > 0 && !Routed;

    public string TypeCn => Type switch
    {
        UnitType.Spear => "枪", UnitType.Bow => "弓", UnitType.Cavalry => "骑", UnitType.Shield => "盾",
        UnitType.MoDao => "陌刀", UnitType.Cataphract => "具装", UnitType.HorseArcher => "骑射",
        UnitType.NomadLancer => "突骑", UnitType.TribalFoot => "部落", _ => "盾"
    };

    /// <summary>兵种克制系数:attacker 对 defender 的伤害乘子(枪克骑/骑克弓/弓克步/盾抗远程)。</summary>
    public static double TypeMatchup(UnitType attacker, UnitType defender) => (attacker, defender) switch
    {
        (UnitType.Spear, UnitType.Cavalry) => 1.5,
        (UnitType.Cavalry, UnitType.Bow) => 1.5,
        (UnitType.Cavalry, UnitType.Spear) => 0.7,
        (UnitType.Bow, UnitType.Spear) => 1.3,
        (UnitType.Bow, UnitType.Shield) => 0.6,
        (UnitType.Shield, UnitType.Bow) => 1.2,
        // —— 大酆 长杆重步 克 各种骑(枪槊/陌刀) ——
        (UnitType.Spear, UnitType.Cataphract) => 1.4,
        (UnitType.Spear, UnitType.NomadLancer) => 1.5,
        (UnitType.Spear, UnitType.HorseArcher) => 1.4,
        (UnitType.MoDao, UnitType.Cavalry) => 1.5,
        (UnitType.MoDao, UnitType.Cataphract) => 1.6,
        (UnitType.MoDao, UnitType.NomadLancer) => 1.6,
        (UnitType.MoDao, UnitType.HorseArcher) => 1.4,
        // —— 大酆 骑/具装 克 远程·散兵 ——
        (UnitType.Cavalry, UnitType.HorseArcher) => 1.4,
        (UnitType.Cavalry, UnitType.TribalFoot) => 1.4,
        (UnitType.Cataphract, UnitType.Bow) => 1.6,
        (UnitType.Cataphract, UnitType.HorseArcher) => 1.3,
        (UnitType.Cataphract, UnitType.TribalFoot) => 1.6,
        (UnitType.Cataphract, UnitType.Spear) => 0.7,
        (UnitType.Cataphract, UnitType.MoDao) => 0.6,
        (UnitType.Shield, UnitType.HorseArcher) => 1.2,
        // —— 草原:骑射克慢步·被盾克;突骑克远程·被长杆克 ——
        (UnitType.HorseArcher, UnitType.Spear) => 1.25,
        (UnitType.HorseArcher, UnitType.MoDao) => 1.2,
        (UnitType.HorseArcher, UnitType.Shield) => 0.6,
        (UnitType.HorseArcher, UnitType.TribalFoot) => 1.2,
        (UnitType.HorseArcher, UnitType.Cataphract) => 0.7,
        (UnitType.NomadLancer, UnitType.Bow) => 1.5,
        (UnitType.NomadLancer, UnitType.HorseArcher) => 1.1,
        (UnitType.NomadLancer, UnitType.TribalFoot) => 1.4,
        (UnitType.NomadLancer, UnitType.Spear) => 0.7,
        (UnitType.NomadLancer, UnitType.MoDao) => 0.6,
        _ => 1.0
    };

    /// <summary>基础移动速度(格/tick,未计地形与体力)。</summary>
    public double BaseSpeed => Type switch
    {
        UnitType.HorseArcher => 2.2,                          // 骑射最快(放风筝)
        UnitType.Cavalry or UnitType.NomadLancer => 2.0,
        UnitType.Cataphract => 1.6,                           // 具装重、稍慢
        _ => 1.0
    };

    // ——— 兵种战斗剖面:弓射箭 · 骑冲锋 · 步抗线 ———

    /// <summary>远程兵种:可在 ShootRange 内隔空放箭(不接近即造成伤害),但近战脆。</summary>
    public bool IsRanged => Type is UnitType.Bow or UnitType.HorseArcher;

    /// <summary>放箭射程(格);近战兵为 0。</summary>
    public int ShootRange => Type switch { UnitType.Bow => 4, UnitType.HorseArcher => 3, _ => 0 };

    /// <summary>是否重步(枪/盾/陌刀):据守抗线时额外获得防御。</summary>
    public bool IsHeavyFoot => Type is UnitType.Spear or UnitType.Shield or UnitType.MoDao;

    /// <summary>是否骑兵(轻骑/突骑/具装):移动接敌可得冲锋加成。</summary>
    public bool IsCavalry => Type is UnitType.Cavalry or UnitType.NomadLancer or UnitType.Cataphract;

    /// <summary>兵种基础护甲(抗线):受伤时除以它 —— &gt;1 抗打(重步/具装),&lt;1 脆皮(弓/骑射)。</summary>
    public double ArmArmor => Type switch
    {
        UnitType.Shield => 1.6,          // 刀盾:抗线王
        UnitType.Cataphract => 1.5,      // 具装:铁罐头
        UnitType.MoDao => 1.3,
        UnitType.Spear => 1.25,
        UnitType.TribalFoot => 1.0,
        UnitType.Cavalry or UnitType.NomadLancer => 0.95,
        UnitType.HorseArcher => 0.85,
        UnitType.Bow => 0.7,             // 弓:近身即崩
        _ => 1.0
    };
}
