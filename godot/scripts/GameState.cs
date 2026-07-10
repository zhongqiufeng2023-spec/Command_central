using Godot;
using CommandPost.Core;

/// <summary>
/// 跨场景游戏状态(autoload 单例):当前战役、难度选项、大地图进度。
/// 场景流:大地图(Overworld)→ 遭遇/扎营 → 中军帐(Tent)→ 沙盘(Battle)/ 瞭望台。
/// </summary>
public partial class GameState : Node
{
	public static GameState I { get; private set; } = null!;

	/// <summary>当前战役(黑松岭);null = 无战事。</summary>
	public BattleSim? Battle;
	public bool BattleActive => Battle is { Over: false };

	/// <summary>难度预设档(标题画面选;信息丰度旋钮,PRD §11)。</summary>
	public BDifficulty Difficulty = BDifficulty.Normal;

	/// <summary>低难度选项(F1 可随时切):沙盘直接叠加瞭望所见。轻松档默认开。</summary>
	public bool EasySandboxVision;

	/// <summary>本次进帐是否纯扎营(无战事,沙盘空空)。</summary>
	public bool CampOnly = true;

	// —— 大地图进度(200×130 瓦片 ×16px 世界坐标)——
	public Vector2 PartyPos = new(26 * 16, 66 * 16);     // 本部(随军出征起点)
	public Vector2 EnemyPos = new(150 * 16, 64 * 16);    // 当面之敌:黑松岭东缘汛地
	public bool EnemyDefeated;
	private int _battleSeed = 20260704;

	// —— 皇命与行营(无章节剧本:开局即自由,皇命给目标与约束)——
	/// <summary>行营(周崇大军)驻地:谒见、领粮之处。</summary>
	public Vector2 ArmyPos = new(24 * 16, 64 * 16);
	/// <summary>当前皇命限期(行军历小时;<0=无限期)。逾期=催令+信任惩罚。</summary>
	public float MissionDeadline = -1f;
	/// <summary>开局皇命是否已宣读(首次上大地图自动展卷)。</summary>
	public bool OrderRead;

	/// <summary>回大地图时要弹的横幅(如战罢脱离接触)——Overworld._Ready 取走即清。</summary>
	public string PendingBanner = "";

	/// <summary>行军历:出征以来的时辰数(行军才走表;第一日辰时出兵)。</summary>
	public float CampaignHours = 8f;

	/// <summary>军粮 0..100:行军逐时消耗;见底则士卒枵腹入战(军心浮动)。帅帐可领粮。</summary>
	public float Grain = 100f;
	/// <summary>士卒疲惫 0..100:行军积累(夜行倍之),歇营过夜大减;过重则带乏入战。</summary>
	public float Fatigue;

	/// <summary>主帅信任(跨战役累积,0..100 基线 50)——每战的行营裁断向它结转。</summary>
	public int Trust = 50;
	/// <summary>万骨账:出征以来尔部阵亡将士累计——功业是用这个数堆出来的。</summary>
	public int Bones;

	/// <summary>心神(SAN)0..100:败绩/折损/断粮磨蚀,胜利/歇营回补。
	/// 低了不加数值惩罚——加「不可信」:回报误差变大,沙盘会长出幻影敌情(蓝图§4.5)。</summary>
	public float San = 75f;
	/// <summary>上一战的行营裁断(帐内/大地图可回看);null=尚无战绩。</summary>
	public Appraisal? LastVerdict;

	public override void _Ready() => I = this;

	public void StartBattle()
	{
		Battle = BattleScenario.BlackPineField(_battleSeed++);
		Battle.Difficulty = BDifficultyProfile.Of(Difficulty);
		CampOnly = false;

		// —— 行军状态带进战场:疲惫折体力,断粮动军心(大地图的抉择在这里收账)——
		float stam = Fatigue > 80f ? 55f : Fatigue > 55f ? 72f : 100f;
		foreach (var u in Battle.Units)
		{
			if (u.Side != CommandPost.Core.Side.Friend || u.Allied) continue;
			if (stam < 100f) u.Stamina = stam;
			if (Grain <= 0f) u.Morale = 70f;
		}
		if (stam < 100f) Battle.Feed(stam < 60f ? "连夜强行军,人马俱疲——各部带乏入战。" : "连日行军,士卒带乏——体力折损入战。");
		if (Grain <= 0f) Battle.Feed("粮尽!士卒枵腹而战,军心浮动……");

		// SAN→失真:心神耗蚀,你收到的每一份回报都更不可信
		Battle.SanFactor = San >= 60f ? 1f : San >= 35f ? 1.4f : 1.8f;
		if (San < 35f) Battle.Feed("你已多日不得安枕。帐外每一声马嘶,听着都像虏骑。");
		else if (San < 60f) Battle.Feed("心神耗蚀——今日回报的数目,未必可尽信。");
	}

	/// <summary>战毕班师:胜则虏骑绝迹于野;行营裁断结转主帅信任;阵亡入万骨账。</summary>
	public void EndBattleReturn()
	{
		if (Battle is { Over: true, Winner: CommandPost.Core.Side.Friend }) EnemyDefeated = true;
		if (Battle?.Mission?.Verdict is { } v)
		{
			LastVerdict = v;
			Trust = System.Math.Clamp(Trust + (v.Trust - 50), 0, 100);
		}
		if (Battle != null)
		{
			foreach (var u in Battle.Units)
				if (u.Side == CommandPost.Core.Side.Friend && !u.Allied)
					Bones += System.Math.Max(0, u.MaxCount - u.AliveCount - u.Fled);
			// 心神结转:胜可回血,败与折损都是磨蚀
			float swing = Battle.Winner == CommandPost.Core.Side.Friend ? 10f
						: Battle.Winner == CommandPost.Core.Side.Enemy ? -15f : -5f;
			San = System.Math.Clamp(San + swing - Battle.FriendLossFrac * 20f, 0f, 100f);
		}

		// 战罢脱离接触:开战时两军标是贴在一起的——虏未灭就得拉开,
		// 否则一回大地图立刻又撞上=回车再战的死循环
		if (!EnemyDefeated)
		{
			EnemyPos = new Vector2(150 * 16, 64 * 16);                   // 虏骑收兵归汛
			var away = PartyPos - EnemyPos;
			var dir = away.Length() > 1f ? away.Normalized() : new Vector2(-1, 0);
			PartyPos += dir * 300f;                                       // 尔部退出接战之地
			PendingBanner = "战罢——两军脱离接触:虏骑退回汛地,尔部收兵移营。";
		}

		Battle = null;
		CampOnly = true;
	}

	public static void Go(Node from, string scene) => from.GetTree().ChangeSceneToFile(scene);

	// ====================================================================
	//  存档(user://save.json):只存大地图进度与元状态;战役不可序列化,战中不存。
	// ====================================================================

	private const string SavePath = "user://save.json";
	public static bool SaveExists => FileAccess.FileExists(SavePath);

	/// <summary>新开一局:回到出征起点(种子随时钟变,回回不同)。</summary>
	public void NewRun()
	{
		Battle = null; CampOnly = true;
		EasySandboxVision = Difficulty == BDifficulty.Easy;   // 轻松档:沙盘代望默认开
		PartyPos = new Vector2(26 * 16, 66 * 16);
		EnemyPos = new Vector2(150 * 16, 64 * 16);
		ArmyPos = new Vector2(24 * 16, 64 * 16);
		MissionDeadline = 8f + 72f;                          // 皇命限三日(行军历自辰时起)
		OrderRead = false;
		EnemyDefeated = false; Trust = 50; LastVerdict = null; Bones = 0;
		CampaignHours = 8f; Grain = 100f; Fatigue = 0f; San = 75f;
		_battleSeed = 20260705 + (int)(Time.GetTicksMsec() % 99991);
	}

	/// <summary>存档(战中静默跳过——战役打完才有得存)。</summary>
	public void SaveRun()
	{
		if (BattleActive) return;
		var d = new Godot.Collections.Dictionary
		{
			["px"] = PartyPos.X, ["py"] = PartyPos.Y,
			["ex"] = EnemyPos.X, ["ey"] = EnemyPos.Y,
			["defeated"] = EnemyDefeated, ["trust"] = Trust,
			["easy"] = EasySandboxVision, ["seed"] = _battleSeed,
			["diff"] = (int)Difficulty,
			["hours"] = CampaignHours, ["grain"] = Grain, ["fatigue"] = Fatigue,
			["bones"] = Bones, ["san"] = San,
			["ax"] = ArmyPos.X, ["ay"] = ArmyPos.Y, ["deadline"] = MissionDeadline, ["orderread"] = OrderRead,
			["vcn"] = LastVerdict?.VerdictCn ?? "", ["vtrust"] = LastVerdict?.Trust ?? -1
		};
		using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		f?.StoreString(Json.Stringify(d));
	}

	public bool LoadRun()
	{
		if (!SaveExists) return false;
		using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		if (f == null) return false;
		var v = Json.ParseString(f.GetAsText());
		if (v.VariantType != Variant.Type.Dictionary) return false;
		var d = v.AsGodotDictionary();
		Battle = null; CampOnly = true;
		PartyPos = new Vector2(d["px"].AsSingle(), d["py"].AsSingle());
		EnemyPos = new Vector2(d["ex"].AsSingle(), d["ey"].AsSingle());
		EnemyDefeated = d["defeated"].AsBool();
		Trust = d["trust"].AsInt32();
		EasySandboxVision = d["easy"].AsBool();
		_battleSeed = d["seed"].AsInt32();
		Difficulty = d.ContainsKey("diff") ? (BDifficulty)d["diff"].AsInt32() : BDifficulty.Normal;
		CampaignHours = d.ContainsKey("hours") ? d["hours"].AsSingle() : 8f;
		Grain = d.ContainsKey("grain") ? d["grain"].AsSingle() : 100f;
		Fatigue = d.ContainsKey("fatigue") ? d["fatigue"].AsSingle() : 0f;
		Bones = d.ContainsKey("bones") ? d["bones"].AsInt32() : 0;
		San = d.ContainsKey("san") ? d["san"].AsSingle() : 75f;
		ArmyPos = d.ContainsKey("ax") ? new Vector2(d["ax"].AsSingle(), d["ay"].AsSingle()) : new Vector2(24 * 16, 64 * 16);
		MissionDeadline = d.ContainsKey("deadline") ? d["deadline"].AsSingle() : -1f;
		OrderRead = !d.ContainsKey("orderread") || d["orderread"].AsBool();
		int vt = d["vtrust"].AsInt32();
		LastVerdict = vt >= 0 ? new Appraisal { Trust = vt, VerdictCn = d["vcn"].AsString() } : null;
		return true;
	}
}
