using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 中军帐 · 像素场景 —— 本核:区域状态机 / 将军行走 / 交互 / 战役后台推进。
/// 你操纵将军(WASD/方向键)在帐内走动:
///   靠近沙盘按 E → 进入沙盘推演(战斗指挥);
///   走出帐门 → 营区;营区东侧瞭望台,按 E 登台 → 亲眼观察(黑点与烟雾,只有方位/远近/多寡);
///   营区南门 → 拔营上路(战事未决不可走)。
/// 战役在你走动时照常推进——去瞭望台的每一步都是时间。F1 = 低难度(沙盘直接显示瞭望所见)。
/// 绘制层见 TentRoot.Draw.cs。
/// </summary>
public partial class TentRoot : Node2D
{
	private enum Area { Inside, Outside, Tower }
	private Area _area = Area.Inside;

	private Vector2 _pos = new(430, 430);
	private GeneralSprite.Dir _dir = GeneralSprite.Dir.Down;
	private float _phase;
	private bool _moving;

	private Font _font = null!;
	private double _t;                                  // 场景钟(火光/烟雾动画)
	private double _acc;                                // 战役后台推进累加器
	private int _seenAlerts;
	private string _banner = ""; private double _bannerAge = 99;
	private readonly PauseOverlay _menu = new();        // Esc 暂停菜单(开着时战役也停)

	// —— 帐内布置(障碍/交互区)——
	private static readonly Rect2 InsideBounds = new(220, 150, 680, 470);
	private static readonly Rect2 SandTable = new(620, 300, 240, 150);     // 沙盘台(障碍+交互)
	private static readonly Rect2 Brazier = new(300, 240, 40, 40);         // 火盆
	private static readonly Rect2 DoorInside = new(520, 596, 90, 26);      // 帐门(南)

	// —— 营区布置 ——
	private static readonly Rect2 OutsideBounds = new(70, 130, 980, 560);
	private static readonly Rect2 TentBig = new(460, 130, 200, 110);       // 中军帐(北,回帐)
	private static readonly Rect2 TowerBase = new(860, 430, 56, 70);       // 瞭望台基座
	private static readonly Rect2 GateSouth = new(520, 664, 90, 26);       // 辕门(拔营)
	private static readonly Rect2[] OutsideTents =
	{ new(150, 220, 110, 80), new(150, 420, 110, 80), new(320, 540, 110, 80), new(700, 560, 110, 80) };

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.Nearest;
		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		_font = sf;

		if (GameState.I.BattleActive)
		{ _banner = "军情紧急!沙盘推演在帐内,亲自观望登瞭望台。"; _bannerAge = 0; }
		GetWindow().GrabFocus();
	}

	public override void _Process(double delta)
	{
		_t += delta; _bannerAge += delta;
		if (_menu.Open) { QueueRedraw(); return; }      // 军议暂歇:连外面的仗都停一停

		// 战役后台推进:你在帐里走动,外面照样打(1x 实时)
		if (GameState.I.BattleActive)
		{
			_acc += delta * 10.0;
			while (_acc >= 1.0)
			{
				GameState.I.Battle!.Tick();
				_acc -= 1.0;
			}
			var alerts = GameState.I.Battle!.Alerts;
			if (alerts.Count > _seenAlerts)
			{
				_banner = alerts[^1].Text; _bannerAge = 0;
				_seenAlerts = alerts.Count;
			}
		}

		if (_area != Area.Tower) UpdateWalk((float)delta);
		QueueRedraw();
	}

	private void UpdateWalk(float dt)
	{
		var dir = Vector2.Zero;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;

		_moving = dir != Vector2.Zero;
		if (!_moving) return;

		var next = _pos + dir.Normalized() * 200f * dt;
		var bounds = _area == Area.Inside ? InsideBounds : OutsideBounds;
		next.X = Math.Clamp(next.X, bounds.Position.X + 12, bounds.End.X - 12);
		next.Y = Math.Clamp(next.Y, bounds.Position.Y + 20, bounds.End.Y - 4);

		if (!Blocked(next)) _pos = next;
		else
		{
			var sx = new Vector2(next.X, _pos.Y);
			var sy = new Vector2(_pos.X, next.Y);
			if (!Blocked(sx)) _pos = sx;
			else if (!Blocked(sy)) _pos = sy;
		}

		_phase = (_phase + dt * 1.9f) % 1f;
		_dir = Mathf.Abs(dir.X) >= Mathf.Abs(dir.Y)
			? (dir.X >= 0 ? GeneralSprite.Dir.Right : GeneralSprite.Dir.Left)
			: (dir.Y >= 0 ? GeneralSprite.Dir.Down : GeneralSprite.Dir.Up);

		// 门与区域切换(踩进区域即切)
		if (_area == Area.Inside && DoorInside.HasPoint(_pos))
		{ _area = Area.Outside; _pos = new Vector2(560, 260); }
		else if (_area == Area.Outside && TentBig.HasPoint(_pos + new Vector2(0, -8)) && _pos.Y < 250)
		{ _area = Area.Inside; _pos = new Vector2(560, 570); }
		else if (_area == Area.Outside && GateSouth.HasPoint(_pos))
			TryLeaveCamp();
	}

	private bool Blocked(Vector2 p) => _area switch
	{
		Area.Inside => SandTable.Grow(6).HasPoint(p) || Brazier.Grow(4).HasPoint(p),
		Area.Outside => TowerBase.Grow(4).HasPoint(p) || Blocks(OutsideTents, p),
		_ => false
	};
	private static bool Blocks(Rect2[] rs, Vector2 p)
	{ foreach (var r in rs) if (r.Grow(2).HasPoint(p)) return true; return false; }

	// —— 交互 ——
	private bool NearSandTable => _area == Area.Inside && SandTable.Grow(46).HasPoint(_pos);
	private bool NearTower => _area == Area.Outside && TowerBase.Grow(40).HasPoint(_pos);

	public override void _Input(InputEvent e)
	{
		if (e is not InputEventKey { Pressed: true, Echo: false } k) return;

		// 台上 Esc = 下台;其余 Esc 交给暂停菜单
		if (_area == Area.Tower && !_menu.Open && k.Keycode == Key.Escape) { _area = Area.Outside; return; }
		switch (_menu.HandleKey(k, out bool consumed))
		{
			case PauseOverlay.Act.SaveToTitle:
				GameState.I.SaveRun(); GameState.Go(this, "res://Title.tscn"); return;
			case PauseOverlay.Act.Quit:
				GameState.I.SaveRun(); GetTree().Quit(); return;
		}
		if (consumed) return;

		switch (k.Keycode)
		{
			case Key.E:
				if (_area == Area.Tower) { _area = Area.Outside; }
				else if (NearSandTable)
				{
					if (GameState.I.Battle != null) GameState.Go(this, "res://Battle.tscn");
					else { _banner = "并无战事,沙盘空空。(去大地图寻虏骑;拔营走南辕门)"; _bannerAge = 0; }
				}
				else if (NearTower) _area = Area.Tower;
				break;
			case Key.F1:
				GameState.I.EasySandboxVision = !GameState.I.EasySandboxVision;
				_banner = $"低难度·沙盘瞭望叠加:{(GameState.I.EasySandboxVision ? "开" : "关(要看敌情,亲自登台)")}";
				_bannerAge = 0;
				break;
		}
	}

	private void TryLeaveCamp()
	{
		if (GameState.I.BattleActive)
		{
			_banner = "战事未决,不可拔营!(沙盘推演,或战毕再走)"; _bannerAge = 0;
			_pos += new Vector2(0, -18);
			return;
		}
		GameState.I.Battle = null;
		GameState.Go(this, "res://Overworld.tscn");
	}
}
