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

	/// <summary>低难度选项(F1):沙盘直接叠加瞭望所见。默认关——想看敌情,自己登瞭望台。</summary>
	public bool EasySandboxVision;

	/// <summary>本次进帐是否纯扎营(无战事,沙盘空空)。</summary>
	public bool CampOnly = true;

	// —— 大地图进度(200×130 瓦片 ×16px 世界坐标)——
	public Vector2 PartyPos = new(55 * 16, 66 * 16);     // 前锋营
	public Vector2 EnemyPos = new(150 * 16, 64 * 16);    // 当面之敌:黑松岭东缘汛地
	public bool EnemyDefeated;
	private int _battleSeed = 20260704;

	/// <summary>行军历:出征以来的时辰数(行军才走表;第一日辰时出兵)。</summary>
	public float CampaignHours = 8f;

	/// <summary>主帅信任(跨战役累积,0..100 基线 50)——每战的行营裁断向它结转。</summary>
	public int Trust = 50;
	/// <summary>上一战的行营裁断(帐内/大地图可回看);null=尚无战绩。</summary>
	public Appraisal? LastVerdict;

	public override void _Ready() => I = this;

	public void StartBattle()
	{
		Battle = BattleScenario.BlackPineField(_battleSeed++);
		CampOnly = false;
	}

	/// <summary>战毕班师:胜则虏骑绝迹于野;行营裁断结转主帅信任。</summary>
	public void EndBattleReturn()
	{
		if (Battle is { Over: true, Winner: CommandPost.Core.Side.Friend }) EnemyDefeated = true;
		if (Battle?.Mission?.Verdict is { } v)
		{
			LastVerdict = v;
			Trust = System.Math.Clamp(Trust + (v.Trust - 50), 0, 100);
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
		Battle = null; CampOnly = true; EasySandboxVision = false;
		PartyPos = new Vector2(55 * 16, 66 * 16);
		EnemyPos = new Vector2(150 * 16, 64 * 16);
		EnemyDefeated = false; Trust = 50; LastVerdict = null;
		CampaignHours = 8f;
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
			["hours"] = CampaignHours,
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
		CampaignHours = d.ContainsKey("hours") ? d["hours"].AsSingle() : 8f;
		int vt = d["vtrust"].AsInt32();
		LastVerdict = vt >= 0 ? new Appraisal { Trust = vt, VerdictCn = d["vcn"].AsString() } : null;
		return true;
	}
}
