using Godot;
using CommandPost.Core;
using System.Collections.Generic;
using System.Linq;
using Side = CommandPost.Core.Side;   // 消歧:Godot 也有 Side 枚举

/// <summary>
/// 黑松岭之战 · 战斗B档(逐兵)实验 —— 本核:状态 / 生命周期 / 镜头 / 输入。
/// 默认「沙盘」视图 = 你的全部世界:己方各部的最后所报位置、敌情旧影、你放的信息旗、在途令骑估计。
/// 下令 = 令骑真实骑行送达;敌情 = 塘骑/军报带回。Tab 切「真实战场」(开发对照:两千余士兵逐个厮杀)。
/// 滚轮缩放 · WASD 平移 · 左键选部 · 右键下令(Shift=疾进)· 中键塘骑 · Ctrl+左键插旗 · R 探问。
/// 绘制层拆到 BattleRoot.Draw.cs(地形/真实/沙盘)与 BattleRoot.Hud.cs(HUD/复盘)。
/// </summary>
public partial class BattleRoot : Node2D
{
	private BattleSim _sim = null!;
	private Font _font = null!;

	private bool _paused = true;
	private bool _realView;                     // false=沙盘(玩家) true=真实(对照)
	private double _accum;
	private int _speedIdx;
	private static readonly double[] Speeds = { 1, 2, 4, 8 };

	private Vector2 _cam;                       // 世界坐标的镜头中心
	private float _zoom = 0.9f;
	private static readonly Vector2 ViewCenter = new(440, 390);

	private int _selectedId = -1;
	private int _seenAlerts;
	private string _banner = "";
	private double _bannerAge = 99;
	private bool _endSung;                      // 胜败尾声只唱一次

	public override void _Ready()
	{
		// 战役来自 GameState(大地图遭遇时创建);单独 F6 跑本场景时兜底自建
		_sim = GameState.I?.Battle ?? BattleScenario.BlackPineField();

		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		_font = sf;

		_cam = new Vector2(_sim.Map.WorldW / 2f, _sim.Map.WorldH / 2f);
		_endSung = _sim.Over;                   // 中途回帐再进来,别重唱
		_seenAlerts = _sim.Alerts.Count;        // 帐内已听过的横幅不再叮
		GetWindow().GrabFocus();
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		_bannerAge += delta;
		TickReplay(delta);

		// 镜头平移
		float pan = 420f / _zoom * (float)delta;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) _cam.Y -= pan;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) _cam.Y += pan;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) _cam.X -= pan;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) _cam.X += pan;

		if (!_paused && !_sim.Over)
		{
			_accum += delta * 10.0 * Speeds[_speedIdx];      // 10 内核步/秒 × 倍速
			int guard = 0;
			while (_accum >= 1.0 && !_sim.Over && guard++ < 60)
			{
				_sim.Tick();
				_accum -= 1.0;
				ConsumeAlerts();
				if (_paused) break;
			}
		}
		QueueRedraw();
	}

	private void ConsumeAlerts()
	{
		if (_sim.Alerts.Count > _seenAlerts)
		{
			bool chime = false;
			for (int i = _seenAlerts; i < _sim.Alerts.Count; i++)
			{
				var a = _sim.Alerts[i];
				_banner = a.Text; _bannerAge = 0;
				if (a.Pause) { _paused = true; chime = true; }
			}
			_seenAlerts = _sim.Alerts.Count;
			if (chime) Sfx.Play(this, Sfx.Alert);
		}
		if (_sim.Over && !_endSung)
		{
			_endSung = true;
			Sfx.Play(this, _sim.Winner == Side.Friend ? Sfx.Win : _sim.Winner == Side.Enemy ? Sfx.Lose : Sfx.Horn, -6f);
		}
	}

	// —— 坐标 ——
	private Vector2 ToScreen(Vec2F w) => new((w.X - _cam.X) * _zoom + ViewCenter.X, (w.Y - _cam.Y) * _zoom + ViewCenter.Y);
	private Vec2F ToWorld(Vector2 s) => new((s.X - ViewCenter.X) / _zoom + _cam.X, (s.Y - ViewCenter.Y) / _zoom + _cam.Y);

	// —— 输入 ——
	public override void _Input(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false } k) HandleKey(k);
		else if (e is InputEventMouseButton { Pressed: true } mb) HandleMouse(mb);
	}

	private void HandleKey(InputEventKey k)
	{
		if (_replayMode) { HandleReplayKey(k); QueueRedraw(); return; }
		switch (k.Keycode)
		{
			case Key.P when _sim.Over:
				EnterReplay();
				Sfx.Play(this, Sfx.Click);
				break;
			case Key.Space: if (!_sim.Over && !_sim.Deploying) _paused = !_paused; break;
			case Key.N: if (!_sim.Over && !_sim.Deploying) { _sim.Tick(); ConsumeAlerts(); } break;
			case Key.Equal: if (_speedIdx < Speeds.Length - 1) _speedIdx++; break;
			case Key.Minus: if (_speedIdx > 0) _speedIdx--; break;
			case Key.Tab: _realView = !_realView; break;
			case Key.R:
				if (_selectedId >= 0 && !_sim.Over)
				{ _sim.RequestStatus(_selectedId); _banner = "令骑已出:探问该部近况……"; _bannerAge = 0; }
				break;
			case Key.B:
				if (!_sim.Over && !_sim.Deploying && _sim.Mission != null)
				{
					_sim.SendHqReport();
					_banner = $"军书发出:具报敌情 {_sim.Sandbox.Enemy.Count} 条——回执未至前,别当它送到了";
					_bannerAge = 0;
					Sfx.Play(this, Sfx.Gallop);
				}
				break;
			case Key.Key1: SendStance(BStance.Attack); break;
			case Key.Key2: SendStance(BStance.Hold); break;
			case Key.Key3: SendStance(BStance.Standby); break;
			case Key.Key4: SendStance(BStance.Skirmish); break;
			case Key.Key5: SoundSignal(BStance.Attack, Sfx.Drum); break;
			case Key.Key6: SoundSignal(BStance.Standby, Sfx.Horn); break;
			case Key.Key7: SoundSignal(BStance.Hold, Sfx.Alert); break;
			case Key.F1:
				if (GameState.I != null)
				{
					GameState.I.EasySandboxVision = !GameState.I.EasySandboxVision;
					_banner = $"低难度·沙盘瞭望叠加:{(GameState.I.EasySandboxVision ? "开" : "关(要看敌情,回帐登瞭望台)")}";
					_bannerAge = 0;
				}
				break;
			case Key.Home: _cam = new Vector2(_sim.Map.WorldW / 2f, _sim.Map.WorldH / 2f); _zoom = 0.9f; break;
			case Key.Enter or Key.KpEnter when _sim.Deploying:
				_sim.FinishDeploy();
				_paused = false;
				_banner = "战鼓起!此后一切军令须经令骑送达。";
				_bannerAge = 0;
				Sfx.Play(this, Sfx.Drum, -4f);
				break;
			case Key.Enter or Key.KpEnter when _sim.Over:
				GameState.I?.EndBattleReturn();
				GameState.Go(this, "res://Overworld.tscn");
				break;
			case Key.Escape:
				if (GameState.I != null) GameState.Go(this, "res://Tent.tscn");   // 回中军帐(战役照常推进)
				else _selectedId = -1;
				break;
		}
		QueueRedraw();
	}

	private void HandleMouse(InputEventMouseButton mb)
	{
		if (HandleReplayMouse(mb)) { QueueRedraw(); return; }
		var world = ToWorld(mb.Position);

		switch (mb.ButtonIndex)
		{
			case MouseButton.WheelUp:
				ZoomAt(mb.Position, 1.15f); return;
			case MouseButton.WheelDown:
				ZoomAt(mb.Position, 1f / 1.15f); return;

			case MouseButton.Left when mb.CtrlPressed:
				_sim.PlaceFlag(world); break;                                  // 插信息旗

			case MouseButton.Left:
				// 选部:按沙盘上的「所报位置」找(玩家世界里部队就在那)
				int pick = -1; float bd = 26f / _zoom;
				foreach (var mk in _sim.Sandbox.Own.Values)
				{
					var u = _sim.ById(mk.UnitId);
					if (u is null || u.AliveCount == 0) continue;
					float d = (float)ToScreen(mk.Pos).DistanceTo(mb.Position) / _zoom;
					if (d < bd) { bd = d; pick = mk.UnitId; }
				}
				_selectedId = pick;
				break;

			case MouseButton.Right when mb.CtrlPressed:
				_sim.RemoveFlagNear(world); break;

			case MouseButton.Right when _selectedId >= 0 && !_sim.Over:
				if (_sim.Deploying) { _sim.DeployMove(_selectedId, world); Sfx.Play(this, Sfx.Click); }
				else { _sim.IssueMove(_selectedId, world, run: mb.ShiftPressed); Sfx.Play(this, Sfx.Gallop); }
				break;

			case MouseButton.Middle when !_sim.Over:
				if (_sim.Deploying) { _banner = "布阵中——开战后方可遣塘骑。"; _bannerAge = 0; }
				else { _sim.DispatchScout(world); Sfx.Play(this, Sfx.Gallop); }
				break;
		}
		QueueRedraw();
	}

	/// <summary>旗鼓:近处即时、绝对照令、敌亦可闻(声程圈见沙盘)。</summary>
	private void SoundSignal(BStance st, AudioStreamWav sfx)
	{
		if (_sim.Over || _sim.Deploying) return;
		_sim.SoundSignal(st);
		_banner = $"中军{BattleSim.SignalCn(st)}——声程内各部即刻照令;虏骑也听见了。";
		_bannerAge = 0;
		Sfx.Play(this, sfx, -3f);
		QueueRedraw();
	}

	private void SendStance(BStance st)
	{
		if (_selectedId < 0 || _sim.Over) return;
		if (_sim.Deploying)
		{
			_sim.DeployStance(_selectedId, st);
			_banner = $"布阵:当面吩咐该部「{BattleSim.StanceCnOf(st)}」";
			_bannerAge = 0;
			Sfx.Play(this, Sfx.Click);
			return;
		}
		_sim.IssueStance(_selectedId, st);
		_banner = $"令骑已出:令该部转「{BattleSim.StanceCnOf(st)}」";
		_bannerAge = 0;
		Sfx.Play(this, Sfx.Gallop);
	}

	private void ZoomAt(Vector2 screen, float factor)
	{
		var before = ToWorld(screen);
		_zoom = Mathf.Clamp(_zoom * factor, 0.45f, 6f);
		var after = ToWorld(screen);
		_cam += new Vector2(before.X - after.X, before.Y - after.Y);
		QueueRedraw();
	}
}
