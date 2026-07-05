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
}
