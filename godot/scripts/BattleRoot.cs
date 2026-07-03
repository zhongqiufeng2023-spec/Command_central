using Godot;
using CommandPost.Core;
using System.Collections.Generic;
using System.Linq;
using Side = CommandPost.Core.Side;   // 消歧:Godot 也有 Side 枚举

/// <summary>
/// 黑松岭之战 · 战斗B档(逐兵)实验。
/// 默认「沙盘」视图 = 你的全部世界:己方各部的最后所报位置、敌情旧影、你放的信息旗、在途令骑估计。
/// 下令 = 令骑真实骑行送达;敌情 = 塘骑/军报带回。Tab 切「真实战场」(开发对照:两千余士兵逐个厮杀)。
/// 滚轮缩放 · WASD 平移 · 左键选部 · 右键下令(Shift=疾进)· 中键塘骑 · Ctrl+左键插旗 · R 探问。
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

	public override void _Ready()
	{
		_sim = BattleScenario.BlackPineField();

		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		_font = sf;

		_cam = new Vector2(_sim.Map.WorldW / 2f, _sim.Map.WorldH / 2f);
		GetWindow().GrabFocus();
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		_bannerAge += delta;

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
		if (_sim.Alerts.Count <= _seenAlerts) return;
		for (int i = _seenAlerts; i < _sim.Alerts.Count; i++)
		{
			var a = _sim.Alerts[i];
			_banner = a.Text; _bannerAge = 0;
			if (a.Pause) _paused = true;
		}
		_seenAlerts = _sim.Alerts.Count;
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
		switch (k.Keycode)
		{
			case Key.Space: if (!_sim.Over) _paused = !_paused; break;
			case Key.N: if (!_sim.Over) { _sim.Tick(); ConsumeAlerts(); } break;
			case Key.Equal: if (_speedIdx < Speeds.Length - 1) _speedIdx++; break;
			case Key.Minus: if (_speedIdx > 0) _speedIdx--; break;
			case Key.Tab: _realView = !_realView; break;
			case Key.R:
				if (_selectedId >= 0 && !_sim.Over)
				{ _sim.RequestStatus(_selectedId); _banner = "令骑已出:探问该部近况……"; _bannerAge = 0; }
				break;
			case Key.Key1: SendStance(BStance.Attack); break;
			case Key.Key2: SendStance(BStance.Hold); break;
			case Key.Key3: SendStance(BStance.Standby); break;
			case Key.Home: _cam = new Vector2(_sim.Map.WorldW / 2f, _sim.Map.WorldH / 2f); _zoom = 0.9f; break;
			case Key.Escape: _selectedId = -1; break;
		}
		QueueRedraw();
	}

	private void HandleMouse(InputEventMouseButton mb)
	{
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
				_sim.IssueMove(_selectedId, world, run: mb.ShiftPressed);      // 令骑真实出发
				break;

			case MouseButton.Middle when !_sim.Over:
				_sim.DispatchScout(world); break;                              // 塘骑侦察
		}
		QueueRedraw();
	}

	private void SendStance(BStance st)
	{
		if (_selectedId < 0 || _sim.Over) return;
		_sim.IssueStance(_selectedId, st);
		_banner = $"令骑已出:令该部转「{BattleSim.StanceCnOf(st)}」";
		_bannerAge = 0;
	}

	private void ZoomAt(Vector2 screen, float factor)
	{
		var before = ToWorld(screen);
		_zoom = Mathf.Clamp(_zoom * factor, 0.45f, 6f);
		var after = ToWorld(screen);
		_cam += new Vector2(before.X - after.X, before.Y - after.Y);
		QueueRedraw();
	}

	// ====================================================================
	//  绘制
	// ====================================================================

	public override void _Draw()
	{
		DrawRect(new Rect2(0, 0, GetViewportRect().Size), new Color(_realView ? "22201a" : "2c2a22"), true);
		DrawTerrain();

		if (_realView) DrawRealWorld();
		else DrawSandbox();

		DrawHud();
		DrawFeed();
		DrawBanner();
		DrawEndOverlay();
	}

	private void DrawTerrain()
	{
		var m = _sim.Map;
		float ts = BattleMap.TileSize * _zoom;
		for (int ty = 0; ty < m.H; ty++)
			for (int tx = 0; tx < m.W; tx++)
			{
				var p = ToScreen(new Vec2F(tx * BattleMap.TileSize, ty * BattleMap.TileSize));
				if (p.X < -ts || p.Y < -ts || p.X > 1130 || p.Y > 770) continue;
				var t = m.AtTile(tx, ty);
				Color c = _realView
					? t switch
					{
						BTerrain.Road => new Color("6b5b3e"), BTerrain.Forest => new Color("2e4023"),
						BTerrain.Hill => new Color("575040"), BTerrain.River => new Color("2d4d5e"),
						BTerrain.Ford => new Color("3e6172"), _ => new Color("4a4332")
					}
					: t switch
					{
						BTerrain.Road => new Color("4e4433"), BTerrain.Forest => new Color("39412e"),
						BTerrain.Hill => new Color("454034"), BTerrain.River => new Color("31434d"),
						BTerrain.Ford => new Color("3d5560"), _ => new Color("3a372c")
					};
				DrawRect(new Rect2(p, new Vector2(ts + 1, ts + 1)), c, true);
			}

		// 帅帐
		var hq = ToScreen(_sim.HqPos);
		DrawRect(new Rect2(hq - new Vector2(7, 7), new Vector2(14, 14)), new Color("d9b34a"), true);
		DrawText(hq + new Vector2(0, -14), "帅帐", 12, new Color("e6c25c"), center: true);
	}

	// —— 真实战场(对照):两千余士兵逐个画 ——
	private void DrawRealWorld()
	{
		foreach (var u in _sim.Units)
		{
			if (u.AliveCount == 0) continue;
			bool friend = u.Side == Side.Friend;
			bool broken = u.State is BUnitState.Routing or BUnitState.Shattered;
			float r = Mathf.Max(1.3f, 1.05f * _zoom);
			Color body = friend ? new Color(0.72f, 0.26f, 0.20f) : new Color(0.32f, 0.45f, 0.62f);
			if (broken) body = new Color(body.R, body.G, body.B, 0.45f + 0.2f * Mathf.Sin((float)_sim.Time * 6f));

			foreach (var s in u.Soldiers)
				DrawCircle(ToScreen(s.Pos), r, body);

			// 队旗:名+存员+士气条+状态
			var c = ToScreen(u.Center);
			DrawText(c + new Vector2(0, -16 - 8 * _zoom), $"{u.Name} {u.AliveCount}", 12,
				friend ? new Color("ffd9a0") : new Color("bcd2ec"), center: true);
			float w = 40;
			DrawRect(new Rect2(c.X - w / 2, c.Y - 12 - 8 * _zoom, w, 3), new Color(0, 0, 0, 0.6f), true);
			DrawRect(new Rect2(c.X - w / 2, c.Y - 12 - 8 * _zoom, w * u.Morale / 100f, 3),
				u.Morale > 45 ? new Color("6fbf5a") : new Color("d0894a"), true);
			if (u.State != BUnitState.Steady)
				DrawText(c + new Vector2(0, -2 - 8 * _zoom), u.StateCn, 11, new Color("e8d9b0"), center: true);
		}

		// 箭矢:短线示弹道
		foreach (var a in _sim.Arrows)
		{
			var p = ToScreen(a.Pos);
			var tail = ToScreen(a.Pos - a.Vel * 0.045f);
			DrawLine(tail, p, new Color(0.92f, 0.88f, 0.7f, 0.85f), Mathf.Max(1f, 0.8f * _zoom));
		}

		// 骑手(真实位置——对照视图才可见)
		foreach (var r2 in _sim.Riders.Where(x => !x.Lost))
			DrawDiamond(ToScreen(r2.Pos), 4f, new Color("5ad0c0"));
	}

	// —— 沙盘(玩家世界):标记与旧影 ——
	private void DrawSandbox()
	{
		float now = _sim.Time;

		// 瞭望台:帅帐望楼的实时视界(圈内所见即真——低保真但零延迟)
		var live = _sim.WatchtowerVisible().ToList();
		var liveIds = new HashSet<int>(live.Select(u => u.Id));
		DrawArc(ToScreen(_sim.HqPos), _sim.WatchtowerRange * _zoom, 0, Mathf.Tau, 64,
			new Color(0.85f, 0.75f, 0.45f, 0.30f), 1.5f, true);
		DrawText(ToScreen(_sim.HqPos) + new Vector2(0, _sim.WatchtowerRange * _zoom + 12), "瞭望所及", 10,
			new Color(0.85f, 0.75f, 0.45f, 0.5f), center: true);

		// 信息旗
		foreach (var f in _sim.Sandbox.Flags)
		{
			var p = ToScreen(f.Pos);
			DrawLine(p, p + new Vector2(0, -16), new Color("e6c25c"), 2f);
			DrawColoredPolygon(new[] { p + new Vector2(0, -16), p + new Vector2(12, -12), p + new Vector2(0, -8) }, new Color("d9b34a"));
			DrawText(p + new Vector2(0, 12), f.Label, 11, new Color("e6c25c"), center: true);
		}

		// 敌情旧影(菱形;越旧越淡)——望楼正看着的不画旧影,画下面的实见
		foreach (var em in _sim.Sandbox.Enemy.Values)
		{
			if (liveIds.Contains(em.UnitId)) continue;
			var u = _sim.ById(em.UnitId);
			float age = now - em.T;
			float a = Mathf.Clamp(0.95f - age / 120f * 0.6f, 0.3f, 0.95f);
			var p = ToScreen(em.Pos);
			DrawDiamond(p, 11f, new Color(0.42f, 0.58f, 0.78f, a), filled: false);
			string ty = em.Type is UnitType t ? BattleSim.ArmCn(t) : "不明";
			DrawText(p + new Vector2(0, -18), $"虏·{ty} 约{em.Est}", 12, new Color(0.72f, 0.82f, 0.94f, a), center: true);
			DrawText(p + new Vector2(0, 14), $"{(int)age}s前", 10, new Color(0.7f, 0.7f, 0.65f, a), center: true);
		}

		// 己方所报位置(圆token;信息也会旧!)——望楼看得见的画实时位置
		foreach (var mk in _sim.Sandbox.Own.Values)
		{
			var u = _sim.ById(mk.UnitId);
			if (u is null) continue;
			bool watched = liveIds.Contains(mk.UnitId) && u.AliveCount > 0;
			float age = now - mk.T;
			var p = ToScreen(watched ? u.Center : mk.Pos);
			bool sel = mk.UnitId == _selectedId;
			var col = u.AliveCount == 0 ? new Color(0.4f, 0.3f, 0.3f) : new Color(0.70f, 0.24f, 0.18f);
			DrawCircle(p, 10f, col);
			DrawArc(p, 10f, 0, Mathf.Tau, 24, new Color("d9b34a"), sel ? 3f : 1.5f, true);
			DrawText(p + new Vector2(0, -18), $"{u.Name} 约{(watched ? u.AliveCount : mk.Count)}", 12, new Color("ffe0b0"), center: true);
			DrawText(p + new Vector2(0, 14), watched ? $"{u.StanceCn}·{u.StateCn}·望见" : $"{mk.StateCn}·{(int)age}s前", 10,
				watched ? new Color("e8d9a0") : new Color("c8bfa8"), center: true);
		}

		// 望楼实见的敌部(实时亮菱形,只报约数)
		foreach (var u in live.Where(x => x.Side == Side.Enemy))
		{
			var p = ToScreen(u.Center);
			int est = System.Math.Max(10, u.AliveCount / 10 * 10);
			DrawDiamond(p, 11f, new Color(0.50f, 0.68f, 0.90f, 0.95f), filled: false);
			DrawText(p + new Vector2(0, -18), $"虏·{BattleSim.ArmCn(u.Type)} 约{est}", 12, new Color("a8c8ec"), center: true);
			DrawText(p + new Vector2(0, 14), "望见·实时", 10, new Color("d0c8a0"), center: true);
		}

		// 在途骑手(估计位置:按出发时刻+计划路线推算——被截杀了你也不知道)
		foreach (var r in _sim.Riders.Where(x => x.EstPath.Count > 0))
		{
			var est = EstimateRiderPos(r, now);
			if (est is { } ep)
			{
				DrawDiamond(ToScreen(ep), 4.5f, new Color(0.9f, 0.92f, 0.85f, 0.85f));
				DrawText(ToScreen(ep) + new Vector2(0, -10), r.Kind == RiderKind.Scout ? "塘" : "令", 10, new Color("d8e8d0"), center: true);
			}
		}
	}

	/// <summary>沙盘上骑手的「应该到哪了」——纯推算,非真实。</summary>
	private Vec2F? EstimateRiderPos(Rider r, float now)
	{
		float travelled = (now - r.Depart) * r.Speed * 0.85f;   // 平均地形折减估计
		var pts = new List<Vec2F> { _sim.HqPos };
		pts.AddRange(r.EstPath);
		float total = 0;
		for (int i = 1; i < pts.Count; i++) total += pts[i - 1].DistanceTo(pts[i]);
		if (total <= 0) return null;

		float leg = travelled <= total ? travelled
				  : travelled <= total * 2 ? total * 2 - travelled     // 折返
				  : -1;
		if (leg < 0) return null;                                        // 早该回来了(未归则报警)
		float acc = 0;
		for (int i = 1; i < pts.Count; i++)
		{
			float d = pts[i - 1].DistanceTo(pts[i]);
			if (acc + d >= leg)
				return Vec2F.Lerp(pts[i - 1], pts[i], d < 0.001f ? 0 : (leg - acc) / d);
			acc += d;
		}
		return pts[^1];
	}

	// ====================================================================
	//  HUD / 覆盖层
	// ====================================================================

	private void DrawHud()
	{
		string clock = _sim.Over ? "[战毕]" : _paused ? "[暂停]" : $"[▶ {Speeds[_speedIdx]:0.#}x]";
		string mode = _realView ? "真实战场(对照,Tab切回)" : "沙盘·帅帐所知";
		DrawText(new Vector2(16, 22), $"黑松岭之战 · {mode}   {BattleSim.FormatT(_sim.Time)} {clock}", 15, new Color("e8e0d0"));
		DrawText(new Vector2(16, 42),
			"空格暂停 ±调速 Tab视图 滚轮缩放 WASD平移 | 左键选部 右键行军(Shift疾) 1进攻 2据守 3等待 R探问 | 中键塘骑 Ctrl+左键插旗",
			11, new Color("9aa0a8"));

		if (!_realView && _selectedId >= 0 && _sim.Sandbox.Own.TryGetValue(_selectedId, out var mk))
		{
			var u = _sim.ById(_selectedId);
			DrawText(new Vector2(880, 70), $"已选:{u?.Name}", 14, new Color("e6c25c"));
			DrawText(new Vector2(880, 92), $"所报兵力 约{mk.Count}", 12, new Color("d8d2c4"));
			DrawText(new Vector2(880, 110), $"所报状态 {mk.StateCn}", 12, new Color("d8d2c4"));
			DrawText(new Vector2(880, 128), $"报于 {(int)(_sim.Time - mk.T)}s 前", 12, new Color("c8bfa8"));
			DrawText(new Vector2(880, 150), "右键=遣令骑传令", 11, new Color("9aa0a8"));
		}
	}

	private void DrawFeed()
	{
		float y = 760 - 6 * 17 - 10;
		DrawText(new Vector2(16, y - 6), "军情流水:", 12, new Color("e6c25c"));
		foreach (var line in Enumerable.Reverse(_sim.Sandbox.Feed).Take(6).Reverse())
		{
			y += 17;
			DrawText(new Vector2(16, y - 6), line, 12, new Color("b8b2a4"));
		}
	}

	private void DrawBanner()
	{
		if (_banner == "" || (!_paused && _bannerAge > 5)) return;
		float w = 740;
		var rect = new Rect2((1120 - w) / 2, 56, w, 42);
		DrawRect(rect, new Color(0.12f, 0.08f, 0.05f, 0.93f), true);
		DrawRect(rect, new Color("d9b34a"), false, 2f);
		DrawText(rect.Position + new Vector2(14, 27), _banner, 14, new Color("f2e6c8"));
	}

	private void DrawEndOverlay()
	{
		if (!_sim.Over) return;
		DrawRect(new Rect2(0, 0, 1120, 760), new Color(0, 0, 0, 0.55f), true);
		float w = 560, h = 420;
		var box = new Rect2((1120 - w) / 2, (760 - h) / 2, w, h);
		DrawRect(box, new Color(0.10f, 0.09f, 0.07f, 0.97f), true);
		DrawRect(box, new Color("d9b34a"), false, 2f);

		float x = box.Position.X + 30, y = box.Position.Y + 40;
		string title = _sim.Winner == Side.Friend ? "捷!虏骑溃走" : _sim.Winner == Side.Enemy ? "败绩……全军溃散" : "两败俱伤";
		DrawText(new Vector2(x, y), $"战毕 —— {title}", 18, new Color("e6c25c")); y += 34;

		DrawText(new Vector2(x, y), "本路各部(实况复盘):", 13, new Color("d8d2c4")); y += 22;
		foreach (var u in _sim.Units.Where(u => u.Side == Side.Friend))
		{
			DrawText(new Vector2(x, y),
				$"{u.Name}〔{BattleSim.ArmCn(u.Type)}〕 {u.MaxCount}人 → 存{u.AliveCount}  斩获{u.Kills}  {u.StateCn}",
				12, new Color("c8c2b4"));
			y += 20;
		}
		y += 10;
		int eDead = _sim.Units.Where(u => u.Side == Side.Enemy).Sum(u => u.MaxCount - u.AliveCount - u.Fled);
		int eFled = _sim.Units.Where(u => u.Side == Side.Enemy).Sum(u => u.Fled);
		DrawText(new Vector2(x, y), $"虏军:遗尸约{eDead},溃逃出野约{eFled}", 12, new Color("bcd2ec")); y += 30;
		DrawText(new Vector2(x, y), "(政治评语与帅帐问责,待接入战役层)", 11, new Color("9aa0a8"));
	}

	// —— 小工具 ——
	private void DrawText(Vector2 p, string text, int size, Color color, bool center = false)
		=> DrawString(_font, center ? p + new Vector2(-200, 0) : p, text,
			center ? HorizontalAlignment.Center : HorizontalAlignment.Left, center ? 400 : -1, size, color);

	private void DrawDiamond(Vector2 p, float r, Color c, bool filled = true)
	{
		var pts = new[]
		{
			p + new Vector2(0, -r), p + new Vector2(r, 0),
			p + new Vector2(0, r), p + new Vector2(-r, 0)
		};
		if (filled) DrawColoredPolygon(pts, c);
		else DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, c, 1.6f);
	}
}
