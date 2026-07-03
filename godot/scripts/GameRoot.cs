using Godot;
using CommandPost.Core;
using System.Collections.Generic;
using System.Linq;
using Side = CommandPost.Core.Side;   // 消歧:Godot 也有 Side 枚举

/// <summary>
/// 第一关《黑松岭》· 副将认知视图(实验分支 MVP)。
/// 你看到的不是战场,而是情报拼出来的沙盘:
///   · 本路直辖各队 = 实时可见(就在你身边);
///   · 目视范围内的敌军 = 亲眼所见,实时显示(标「目视」);
///   · 左翼友军 / 视野外敌军 = 只有「最后已知」的影子(斥候·战报,天然滞后失真);
///   · 瞭望台 = 帐附近的实时烟尘印象(只有规模档,不留记忆)。
/// 命令经传令兵送达(有延迟、可能丢失、被副将解读);中军令分阶段而至,收讯自动暂停。
/// R = 上报帅帐 · F = 探问左翼 · Q = 探问选中队 · Tab = 上帝视图(开发对照)。
/// </summary>
public partial class GameRoot : Node2D
{
	private const float CellPx = 34f;
	private const float OriginX = 40f, OriginY = 56f;
	private const double BaseTicksPerSec = 1.0;

	private Simulation _sim = null!;
	private Font _font = null!;
	private int _selectedId = -1;
	private bool _paused = true;
	private bool _godView;                       // 上帝对照(开发用)
	private double _tickAccum;
	private int _speedIdx = 1;
	private static readonly double[] Speeds = { 0.5, 1, 2, 4, 8 };

	// 平滑:每单位「上一 tick 的像素位置」;显示 = lerp(上一格, 当前格, tick进度)
	private readonly Dictionary<int, Vector2> _prevPix = new();
	private readonly Dictionary<int, Vector2> _disp = new();

	// 提醒/横幅
	private int _seenAlerts;
	private string _banner = "";
	private double _bannerAge = 99;

	public override void _Ready()
	{
		_sim = Scenario.FirstBattleBlackPine(DifficultySettings.Normal);   // 全雾开局:敌我都靠各自侦察

		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		_font = sf;

		foreach (var u in _sim.Truth.Units) { _prevPix[u.Id] = ToPixel(u.Pos); _disp[u.Id] = ToPixel(u.Pos); }

		GetWindow().GrabFocus();   // 主动抢键盘焦点(嵌入编辑器时也能收到空格/+-)
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		_bannerAge += delta;

		if (!_paused && !_sim.BattleOver)
		{
			_tickAccum += delta * BaseTicksPerSec * Speeds[_speedIdx];
			int guard = 0;
			while (_tickAccum >= 1.0 && !_sim.BattleOver && guard++ < 50)
			{
				foreach (var u in _sim.Truth.Units) _prevPix[u.Id] = ToPixel(u.Pos);   // 记住 tick 前位置
				_sim.AdvanceTick();
				_tickAccum -= 1.0;
				ConsumeAlerts();
				if (_paused) break;                                                     // 收讯自动暂停:立即停在这一刻
			}
		}

		// 帧间插值:暂停时直接显真实位置;播放时在「上一格↔当前格」之间按进度平滑
		float frac = _paused ? 1f : (float)Mathf.Clamp(_tickAccum, 0, 1);
		foreach (var u in _sim.Truth.Units)
		{
			var cur = ToPixel(u.Pos);
			_disp[u.Id] = (_prevPix.TryGetValue(u.Id, out var pv) ? pv : cur).Lerp(cur, frac);
		}
		QueueRedraw();
	}

	/// <summary>消化新提醒:更新横幅;遇到 Pause 级提醒自动暂停(§时间与节奏:给玩家缓冲)。</summary>
	private void ConsumeAlerts()
	{
		if (_sim.Alerts.Count <= _seenAlerts) return;
		for (int i = _seenAlerts; i < _sim.Alerts.Count; i++)
		{
			var a = _sim.Alerts[i];
			_banner = a.Text;
			_bannerAge = 0;
			if (a.Pause) _paused = true;
		}
		_seenAlerts = _sim.Alerts.Count;
	}

	// —— 坐标 ——
	private static Vector2 ToPixel(Vec2 c) => new(OriginX + c.X * CellPx + CellPx / 2f, OriginY + c.Y * CellPx + CellPx / 2f);
	private Vector2 DispPos(Unit u) => _disp.TryGetValue(u.Id, out var d) ? d : ToPixel(u.Pos);

	// 显示朝向:移动中用像素位移方向(最顺滑),静止则用内核 Facing
	private Vector2 FacingPixel(Unit u)
	{
		if (_prevPix.TryGetValue(u.Id, out var pv))
		{
			var mv = DispPos(u) - pv;
			if (mv.LengthSquared() > 0.5f) return mv.Normalized();
		}
		var f = new Vector2(u.Facing.X, u.Facing.Y);
		return f.LengthSquared() > 0 ? f.Normalized() : new Vector2(1, 0);
	}

	private Vec2 ToCell(Vector2 world)
	{
		var g = _sim.Truth.Terrain;
		int x = Mathf.Clamp((int)Mathf.Round((world.X - OriginX - CellPx / 2f) / CellPx), 0, g.Width - 1);
		int y = Mathf.Clamp((int)Mathf.Round((world.Y - OriginY - CellPx / 2f) / CellPx), 0, g.Height - 1);
		return new Vec2(x, y);
	}

	// —— 输入(用 _Input 最早拿到事件,避免被任何东西提前吃掉)——
	public override void _Input(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false } k) HandleKey(k);
		else if (e is InputEventMouseButton { Pressed: true } mb) HandleClick(mb);
	}

	private void HandleKey(InputEventKey k)
	{
		switch (k.Keycode)
		{
			case Key.Space: if (!_sim.BattleOver) _paused = !_paused; break;
			case Key.N:
				if (!_sim.BattleOver)
				{
					foreach (var u in _sim.Truth.Units) _prevPix[u.Id] = ToPixel(u.Pos);
					_sim.AdvanceTick();
					ConsumeAlerts();
				}
				break;
			case Key.Equal: if (_speedIdx < Speeds.Length - 1) _speedIdx++; break;
			case Key.Minus: if (_speedIdx > 0) _speedIdx--; break;
			case Key.Tab: _godView = !_godView; break;
			case Key.R:                                            // 上报帅帐(B/D 目标;也是政治的杠杆)
				if (!_sim.BattleOver) { _sim.ReportToHq(); _banner = "使者已遣出,赴帅帐上报……"; _bannerAge = 0; }
				break;
			case Key.F:                                            // 探问左翼近况(传令兵往返)
				if (!_sim.BattleOver &&
					_sim.Truth.Units.FirstOrDefault(u => u.Side == Side.Friend && !u.PlayerLed && u.Alive) is { } wing)
				{ _sim.RequestStatus(wing.Id); _banner = $"已派人探问左翼({wing.Name})……"; _bannerAge = 0; }
				break;
			case Key.Q:                                            // 探问选中队近况
				if (!_sim.BattleOver && _selectedId >= 0) { _sim.RequestStatus(_selectedId); _banner = "已派人探问该队近况……"; _bannerAge = 0; }
				break;
			case Key.Escape: _selectedId = -1; break;
		}
		QueueRedraw();
	}

	private void HandleClick(InputEventMouseButton mb)
	{
		var world = GetGlobalMousePosition();
		if (_sim.BattleOver && mb.ButtonIndex != MouseButton.Left) return;

		switch (mb.ButtonIndex)
		{
			case MouseButton.Left:
				var pick = _sim.Truth.LivingOf(Side.Friend).Where(u => u.PlayerLed)
					.OrderBy(u => DispPos(u).DistanceTo(world)).FirstOrDefault();
				_selectedId = (pick != null && DispPos(pick).DistanceTo(world) < CellPx) ? pick.Id : -1;
				break;

			case MouseButton.Right when _selectedId >= 0:
				// 副将下令:经传令兵送达(延迟/丢失/解读)。点「目视敌」或「敌影」=攻击;点空地=移动。
				var seenEnemy = _sim.Truth.LivingOf(Side.Enemy)
					.FirstOrDefault(en => Seen(en.Pos) && DispPos(en).DistanceTo(world) < CellPx);
				if (seenEnemy != null) { _sim.IssueIntent(_selectedId, Intent.Attack(seenEnemy.Id)); break; }

				var ghost = _sim.Beliefs[Side.Friend].KnownOf(Side.Enemy)
					.FirstOrDefault(g => ToPixel(g.LastKnownPos).DistanceTo(world) < CellPx);
				_sim.IssueIntent(_selectedId, ghost != null ? Intent.Attack(ghost.UnitId) : Intent.Move(ToCell(world)));
				break;

			case MouseButton.Middle:
				_sim.DispatchScoutFromHq(ToCell(world));   // 中军帐派斥候探雾
				break;
		}
		QueueRedraw();
	}

	// —— 视野:某点是否被你「亲眼」看见(本路各队 / 你的斥候 / 帐上瞭望)——左翼不算(它的见闻走战报)
	private bool Seen(Vec2 pos)
	{
		if (_godView) return true;
		var g = _sim.Truth.Terrain;
		foreach (var u in _sim.Truth.LivingOf(Side.Friend).Where(u => u.PlayerLed))
			if (pos.DistanceTo(u.Pos) <= u.Vision * g.ScoutMultiplier(u.Pos)) return true;
		foreach (var s in _sim.Truth.Scouts.Where(s => s.Side == Side.Friend))
			if (pos.DistanceTo(s.Pos) <= s.Vision * g.ScoutMultiplier(s.Pos)) return true;
		return pos.DistanceTo(_sim.Truth.FriendHq) <= _sim.Difficulty.WatchtowerRange * g.ScoutMultiplier(pos);
	}

	// ====================================================================
	//  绘制
	// ====================================================================

	public override void _Draw()
	{
		var g = _sim.Truth.Terrain;
		var field = new Rect2(OriginX, OriginY, g.Width * CellPx, g.Height * CellPx);

		DrawRect(field, new Color("4a4332"), true);
		for (int y = 0; y < g.Height; y++)
			for (int x = 0; x < g.Width; x++)
			{
				var t = g.At(new Vec2(x, y));
				if (t == TerrainType.Plain) continue;
				DrawRect(new Rect2(OriginX + x * CellPx, OriginY + y * CellPx, CellPx, CellPx),
					t == TerrainType.Forest ? new Color("3a4d2b") : new Color("2d4d5e"), true);
			}

		// 迷雾:暗雾铺满 + 视野亮圈(本路各队 / 斥候 / 帐)。认知视图雾更重。
		DrawRect(field, new Color(0.04f, 0.05f, 0.07f, _godView ? 0.25f : 0.52f), true);
		void Reveal(Vector2 c, float rCells) => DrawCircle(c, rCells * CellPx, new Color(0.82f, 0.78f, 0.58f, 0.10f));
		foreach (var u in _sim.Truth.LivingOf(Side.Friend).Where(u => u.PlayerLed))
			Reveal(DispPos(u), (float)(u.Vision * g.ScoutMultiplier(u.Pos)));
		foreach (var s in _sim.Truth.Scouts.Where(s => s.Side == Side.Friend))
			Reveal(ToPixel(s.Pos), (float)(s.Vision * g.ScoutMultiplier(s.Pos)));
		Reveal(ToPixel(_sim.Truth.FriendHq), (float)(_sim.Difficulty.WatchtowerRange * g.ScoutMultiplier(_sim.Truth.FriendHq)));

		DrawHq(_sim.Truth.FriendHq, new Color("d9b34a"), "帐");
		if (_godView) DrawHq(_sim.Truth.EnemyHq, new Color("8a8a8a"), "敌营");

		DrawAidAnchor();
		DrawWatchtowerContacts();

		// 敌军:上帝=全画;认知=只画「目视」中的(其余只有下面的敌影)
		foreach (var u in _sim.Truth.Units.Where(u => u.Side == Side.Enemy && u.Strength > 0))
		{
			bool seen = Seen(u.Pos);
			if (_godView) DrawUnit(u, dim: !seen);
			else if (seen) DrawUnit(u, dim: false, tag: "目视");
		}

		DrawEnemyGhosts();

		// 我军:本路 = 实时;左翼 = 目视才画真身,否则画战报影子
		foreach (var u in _sim.Truth.Units.Where(u => u.Side == Side.Friend && u.Strength > 0))
		{
			if (u.PlayerLed || _godView || Seen(u.Pos)) DrawUnit(u, dim: false);
		}
		DrawWingGhosts();

		DrawScoutsAndMessengers();
		DrawHud();
		DrawMissionPanel();
		DrawIntelPanel();
		DrawSelectedPanel();
		DrawLogTail();
		DrawBanner();
		DrawEndOverlay();
	}

	private void DrawUnit(Unit u, bool dim, string? tag = null)
	{
		var p = DispPos(u);
		bool friend = u.Side == Side.Friend;
		float a = dim ? 0.4f : 1f;
		Color fill = u.Routed ? new Color(0.5f, 0.42f, 0.42f, a)
			: friend ? (u.PlayerLed ? new Color(0.70f, 0.23f, 0.18f, a) : new Color(0.62f, 0.38f, 0.20f, a))
			: new Color(0.25f, 0.44f, 0.63f, a);
		DrawCircle(p, CellPx * 0.30f, fill);
		DrawArc(p, CellPx * 0.30f, 0, Mathf.Tau, 24,
			friend ? new Color(0.90f, 0.76f, 0.36f, a) : new Color(0.62f, 0.75f, 0.88f, a), 2f, true);

		// 朝向:箭头鼻子
		var dir = FacingPixel(u);
		Color nose = friend ? new Color(0.98f, 0.86f, 0.46f, a) : new Color(0.72f, 0.83f, 0.96f, a);
		DrawColoredPolygon(new[]
		{
			p + dir * CellPx * 0.46f,
			p + dir.Rotated(Mathf.Pi * 0.5f) * CellPx * 0.17f,
			p + dir.Rotated(-Mathf.Pi * 0.5f) * CellPx * 0.17f
		}, nose);

		if (u.Id == _selectedId)
		{
			DrawArc(p, CellPx * 0.44f, 0, Mathf.Tau, 32, new Color("ffe08a"), 3f, true);
			if (u.IsRanged) DrawArc(p, u.ShootRange * CellPx, 0, Mathf.Tau, 48, new Color(0.98f, 0.85f, 0.45f, 0.35f), 1.5f, true);
		}

		if (OrderTargetPixel(u) is Vector2 tp && tp != p)
			DrawLine(p, p + (tp - p).Normalized() * CellPx * 0.5f, new Color(1, 1, 1, 0.3f * a), 2f);

		string title = $"{u.Name}·{ArmCn(u.Type)}" + (tag != null ? $"〔{tag}〕" : "");
		DrawLabel(p, title, -1.35f, friend ? new Color("fdf6e3") : new Color(0.81f, 0.88f, 0.94f, a));
		DrawLabel(p, $"兵{(int)u.Strength}", 0.95f, new Color(0.99f, 0.96f, 0.89f, a));
		if (!dim) DrawBars(p, u);
	}

	private Vector2? OrderTargetPixel(Unit u)
	{
		if (u.Order is null) return null;
		if (u.Order.TargetUnitId is int tid && _sim.Truth.UnitById(tid) is { Alive: true } t) return DispPos(t);
		if (u.Order.TargetPos is Vec2 tp) return ToPixel(tp);
		return null;
	}

	// 敌影:认知世界里的「最后已知」(空心菱形 + 约数 + 信息年龄;越旧越淡)
	private void DrawEnemyGhosts()
	{
		int now = _sim.Truth.Tick;
		foreach (var gh in _sim.Beliefs[Side.Friend].KnownOf(Side.Enemy))
		{
			var p = ToPixel(gh.LastKnownPos);
			int age = gh.AgeAt(now);
			float a = Mathf.Clamp(0.9f - age / 90f * 0.55f, 0.3f, 0.9f);
			float r = CellPx * 0.34f;
			DrawPolyline(new[]
			{
				p + new Vector2(0, -r), p + new Vector2(r, 0),
				p + new Vector2(0, r), p + new Vector2(-r, 0), p + new Vector2(0, -r)
			}, new Color(0.86f, 0.62f, 0.26f, a), 1.6f);

			string ty = gh.KnownType is UnitType t ? ArmCn(t) : "?";
			string str = gh.KnownStrength is double v ? $"约{(int)v}" : "?";
			DrawLabel(p, $"{ty}·{str}", -1.3f, new Color(0.93f, 0.73f, 0.36f, a));
			DrawLabel(p, $"{age}t前", 0.95f, new Color(0.85f, 0.68f, 0.38f, a * 0.9f));
		}
	}

	// 左翼影子:目视外的友军他路,只剩战报里的样子(方框 + 约数 + 年龄)
	private void DrawWingGhosts()
	{
		if (_godView) return;
		int now = _sim.Truth.Tick;
		foreach (var gh in _sim.Beliefs[Side.Friend].KnownOf(Side.Friend))
		{
			var truthUnit = _sim.Truth.UnitById(gh.UnitId);
			if (truthUnit is null || truthUnit.PlayerLed) continue;          // 只画他路
			if (truthUnit.Strength > 0 && Seen(truthUnit.Pos)) continue;      // 目视中已画真身
			var p = ToPixel(gh.LastKnownPos);
			int age = gh.AgeAt(now);
			float a = Mathf.Clamp(0.85f - age / 120f * 0.4f, 0.35f, 0.85f);
			float r = CellPx * 0.30f;
			DrawRect(new Rect2(p.X - r, p.Y - r, r * 2, r * 2), new Color(0.75f, 0.55f, 0.30f, a), false, 1.6f);
			string str = gh.KnownStrength is double v ? $"约{(int)v}" : "?";
			DrawLabel(p, $"{truthUnit.Name}", -1.3f, new Color(0.9f, 0.78f, 0.55f, a));
			DrawLabel(p, $"{str}·{age}t前", 0.95f, new Color(0.85f, 0.72f, 0.48f, a * 0.9f));
		}
	}

	// 瞭望台烟尘:帐附近敌情的实时低保真印象(只有规模档;仅认知视图)
	private void DrawWatchtowerContacts()
	{
		if (_godView) return;
		foreach (var c in _sim.WatchtowerContacts.Where(c => c.Side == Side.Enemy))
		{
			var p = ToPixel(c.Pos);
			float r = CellPx * 0.22f;
			DrawColoredPolygon(new[]
			{
				p + new Vector2(0, -r), p + new Vector2(r, r), p + new Vector2(-r, r)
			}, new Color(0.72f, 0.66f, 0.52f, 0.55f));
			DrawLabel(p, $"烟尘·{c.Scale.Cn()}", 1.1f, new Color(0.8f, 0.75f, 0.6f, 0.8f));
		}
	}

	// 驰援旗点(第二道令激活后)
	private void DrawAidAnchor()
	{
		if (_sim.Mission?["C"] is not { Active: true, TargetPos: Vec2 anchor }) return;
		var p = ToPixel(anchor);
		DrawLine(p + new Vector2(0, 10), p + new Vector2(0, -14), new Color("e6c25c"), 2f);
		DrawColoredPolygon(new[]
		{
			p + new Vector2(0, -14), p + new Vector2(14, -9), p + new Vector2(0, -4)
		}, new Color("d9b34a"));
		DrawLabel(p, "驰援点", 1.3f, new Color("e6c25c"));
	}

	private void DrawScoutsAndMessengers()
	{
		// 斥候(带朝向标)
		foreach (var s in _sim.Truth.Scouts.Where(s => s.Side == Side.Friend))
		{
			var sp = ToPixel(s.Pos);
			DrawCircle(sp, CellPx * 0.16f, new Color("5ad0c0"));
			var sdir = new Vector2(s.Facing.X, s.Facing.Y);
			if (sdir.LengthSquared() > 0)
				DrawLine(sp, sp + sdir.Normalized() * CellPx * 0.30f, new Color("baf0e6"), 2f);
			DrawLabel(sp, s.Returning ? "斥·归" : "斥", -0.9f, new Color("baf0e6"));
		}
		// 传令兵(你自己的信使网:令=白点,报=黄点)
		foreach (var m in _sim.Truth.Messengers.Where(m => m.Side == Side.Friend && m.Alive))
		{
			var mp = ToPixel(m.Pos);
			DrawCircle(mp, CellPx * 0.10f, m.IsCommand ? new Color(0.95f, 0.95f, 0.9f, 0.9f) : new Color(0.92f, 0.82f, 0.5f, 0.9f));
		}
	}

	private void DrawBars(Vector2 p, Unit u)
	{
		float w = CellPx * 0.7f, x = p.X - w / 2, y = p.Y + CellPx * 0.34f;
		DrawRect(new Rect2(x, y, w, 3), new Color(0, 0, 0, 0.5f), true);
		DrawRect(new Rect2(x, y, w * (float)(u.Morale / 100.0), 3), new Color("6fbf5a"), true);
		DrawRect(new Rect2(x, y + 4, w, 3), new Color(0, 0, 0, 0.5f), true);
		DrawRect(new Rect2(x, y + 4, w * (float)(u.Stamina / 100.0), 3), new Color("5a9fd0"), true);
	}

	private void DrawHq(Vec2 c, Color color, string label)
	{
		var p = ToPixel(c);
		DrawRect(new Rect2(p.X - CellPx * 0.3f, p.Y - CellPx * 0.3f, CellPx * 0.6f, CellPx * 0.6f), color, true);
		DrawLabel(p, label, 0.1f, new Color("2c2013"));
	}

	private void DrawLabel(Vector2 p, string text, float row, Color? color = null)
		=> DrawString(_font, new Vector2(p.X - CellPx * 0.9f, p.Y + row * 13 + 4), text,
			HorizontalAlignment.Center, CellPx * 1.8f, 12, color ?? new Color("fdf6e3"));

	// ====================================================================
	//  HUD / 面板
	// ====================================================================

	private void DrawHud()
	{
		string clock = _sim.BattleOver ? "[战役毕]" : _paused ? "[暂停]" : $"[▶ {Speeds[_speedIdx]:0.#}x]";
		string mode = _godView ? "上帝对照(Tab切回)" : "副将认知视图";
		DrawString(_font, new Vector2(OriginX, 26),
			$"黑松岭 · {mode}   t{_sim.Truth.Tick}  {clock}",
			HorizontalAlignment.Left, -1, 14, new Color("e8e0d0"));
		DrawString(_font, new Vector2(OriginX, 44),
			"空格暂停 · -/+调速 · N单步 · Tab上帝 | 左键选队 · 右键令(传令兵送达) · 中键斥候 | R上报帅帐 · F探左翼 · Q探选中队",
			HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
	}

	private float PanelX => OriginX + _sim.Truth.Terrain.Width * CellPx + 16;

	private void DrawMissionPanel()
	{
		float x = PanelX, y = OriginY;
		var m = _sim.Mission;
		if (m is null) return;
		DrawString(_font, new Vector2(x, y), $"中军任务 · {m.Title}", HorizontalAlignment.Left, 240, 14, new Color("e6c25c"));
		y += 24;
		foreach (var o in m.Objectives)
		{
			string icon = !o.Active ? "▢" : o.State switch
			{
				ObjectiveState.Done => "●",
				ObjectiveState.Partial => "◐",
				ObjectiveState.Failed => "✕",
				_ => "○"
			};
			Color c = !o.Active ? new Color("7a7f87")
				: o.State == ObjectiveState.Done ? new Color("8fbf6a")
				: o.State == ObjectiveState.Failed ? new Color("d06a5a")
				: new Color("d8d2c4");
			string text = $"{icon} {o.Cn}" + (!o.Active ? "(令未至)" : "") + (o.Primary ? " ★" : "");
			DrawString(_font, new Vector2(x, y), text, HorizontalAlignment.Left, 240, 12, c);
			y += 19;
		}
	}

	private void DrawIntelPanel()
	{
		float x = PanelX, y = OriginY + 118;
		var ghosts = _sim.Beliefs[Side.Friend].KnownOf(Side.Enemy)
			.OrderBy(gh => gh.AgeAt(_sim.Truth.Tick)).ToList();
		DrawString(_font, new Vector2(x, y), $"敌情·斥候判断 ({ghosts.Count})", HorizontalAlignment.Left, -1, 14, new Color("e6c25c"));
		y += 22;
		if (ghosts.Count == 0)
		{
			DrawString(_font, new Vector2(x, y), "(未有回报 — 中键派斥候)", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
			return;
		}
		foreach (var gh in ghosts.Take(8))
		{
			string ty = gh.KnownType is UnitType t ? ArmCn(t) : "兵种不明";
			string str = gh.KnownStrength is double v ? $"约{(int)v}" : "兵力不明";
			string doubt = gh.Fidelity < 0.5 ? " ⚠" : "";
			DrawString(_font, new Vector2(x, y),
				$"敌#{gh.UnitId} {ty}·{str} @{gh.LastKnownPos} {gh.AgeAt(_sim.Truth.Tick)}t前{doubt}",
				HorizontalAlignment.Left, 250, 12, new Color("d6b96a"));
			y += 18;
		}
	}

	private void DrawSelectedPanel()
	{
		float x = PanelX, y = OriginY + 306;
		DrawString(_font, new Vector2(x, y), "选中队", HorizontalAlignment.Left, -1, 14, new Color("e6c25c"));
		y += 22;
		if (_sim.Truth.UnitById(_selectedId) is { } s && s.Strength > 0)
		{
			string[] lines =
			{
				$"{s.Name}·{ArmCn(s.Type)}  武将{s.Commander.Name}({s.Commander.PersonalityCn})",
				$"特性 {ArmTrait(s)}",
				$"兵力 {(int)s.Strength}/{(int)s.MaxStrength}  士气{(int)s.Morale} 体力{(int)s.Stamina}",
				$"当前令 {(s.Order?.ToString() ?? "—")}",
				s.Routed ? "⚠ 已溃逃" : "",
			};
			foreach (var line in lines)
			{
				if (line == "") continue;
				DrawString(_font, new Vector2(x, y), line, HorizontalAlignment.Left, 250, 12, new Color("d8d2c4"));
				y += 18;
			}
		}
		else
			DrawString(_font, new Vector2(x, y), "(左键点本路一队)", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));

		// 图例
		y = OriginY + 420;
		string[] legend =
		{
			"深红=本路 · 棕=左翼他路 · 蓝=目视之敌",
			"菱形=敌影(最后已知) · 方框=左翼影",
			"▲烟尘=瞭望台印象 · 白点令/黄点报",
			"鼻尖=朝向 · 绿条士气 蓝条体力",
		};
		foreach (var l in legend)
		{
			DrawString(_font, new Vector2(x, y), l, HorizontalAlignment.Left, 250, 11, new Color("9aa0a8"));
			y += 16;
		}
	}

	private void DrawLogTail()
	{
		float x = OriginX, y = OriginY + _sim.Truth.Terrain.Height * CellPx + 20;
		DrawString(_font, new Vector2(x, y), "军报流水:", HorizontalAlignment.Left, -1, 12, new Color("e6c25c"));
		y += 18;
		foreach (var line in Enumerable.Reverse(_sim.Log).Take(8).Reverse())
		{
			DrawString(_font, new Vector2(x, y), line, HorizontalAlignment.Left, 820, 12, new Color("b8b2a4"));
			y += 17;
		}
	}

	private void DrawBanner()
	{
		if (_banner == "" || (!_paused && _bannerAge > 5)) return;
		var vp = GetViewportRect().Size;
		float w = Mathf.Min(720, vp.X - 80);
		var rect = new Rect2((vp.X - w) / 2, 60, w, 44);
		DrawRect(rect, new Color(0.12f, 0.08f, 0.05f, 0.92f), true);
		DrawRect(rect, new Color("d9b34a"), false, 2f);
		DrawString(_font, new Vector2(rect.Position.X + 14, rect.Position.Y + 28), _banner,
			HorizontalAlignment.Left, w - 28, 14, new Color("f2e6c8"));
	}

	private void DrawEndOverlay()
	{
		if (!_sim.BattleOver) return;
		var vp = GetViewportRect().Size;
		DrawRect(new Rect2(0, 0, vp.X, vp.Y), new Color(0, 0, 0, 0.55f), true);

		float w = 520, h = 380;
		var box = new Rect2((vp.X - w) / 2, (vp.Y - h) / 2, w, h);
		DrawRect(box, new Color(0.10f, 0.09f, 0.07f, 0.97f), true);
		DrawRect(box, new Color("d9b34a"), false, 2f);

		float x = box.Position.X + 26, y = box.Position.Y + 36;
		DrawString(_font, new Vector2(x, y), $"战役毕 —— {StatusCn(_sim.Status)}", HorizontalAlignment.Left, w - 52, 18, new Color("e6c25c"));
		y += 30;

		if (_sim.Mission is { } m)
		{
			foreach (var o in m.Objectives)
			{
				string icon = !o.Active ? "▢" : o.State switch
				{
					ObjectiveState.Done => "●", ObjectiveState.Partial => "◐",
					ObjectiveState.Failed => "✕", _ => "○"
				};
				DrawString(_font, new Vector2(x, y), $"{icon} {o.Cn}", HorizontalAlignment.Left, w - 52, 13, new Color("d8d2c4"));
				y += 20;
			}
		}
		y += 8;

		if (_sim.Appraisal is { } ap)
		{
			DrawString(_font, new Vector2(x, y), "—— 帅帐评语(主帅隔雾看你)——", HorizontalAlignment.Left, w - 52, 13, new Color("e6c25c"));
			y += 22;
			foreach (var line in ap.Lines)
			{
				DrawString(_font, new Vector2(x, y), line, HorizontalAlignment.Left, w - 52, 12, new Color("c8c2b4"));
				y += 18;
			}
			y += 6;
			// 信任条
			float bw = w - 52;
			DrawRect(new Rect2(x, y, bw, 10), new Color(0, 0, 0, 0.6f), true);
			DrawRect(new Rect2(x, y, bw * ap.Trust / 100f, 10), new Color("d9b34a"), true);
			y += 26;
			DrawString(_font, new Vector2(x, y), $"主帅信任 {ap.Trust}/100 —— {ap.VerdictCn}", HorizontalAlignment.Left, w - 52, 15, new Color("f2e6c8"));
		}
	}

	private static string ArmTrait(Unit u) =>
		u.IsRanged ? $"放箭·射程{u.ShootRange}(近身即崩)"
		: u.IsCavalry ? "骑兵·冲锋接敌"
		: u.IsHeavyFoot ? $"重步·据守抗线(护甲{u.ArmArmor:0.0})"
		: "散兵";

	private static string ArmCn(UnitType t) => t switch
	{
		UnitType.Spear => "枪", UnitType.Bow => "弓", UnitType.Cavalry => "轻骑", UnitType.Shield => "盾",
		UnitType.MoDao => "陌刀", UnitType.Cataphract => "具装", UnitType.HorseArcher => "骑射",
		UnitType.NomadLancer => "突骑", UnitType.TribalFoot => "部落", _ => "?"
	};

	private static string StatusCn(GameStatus s) => s switch
	{
		GameStatus.FriendWon => "我军胜", GameStatus.EnemyWon => "我军败", _ => "到时结算(敌自退去)"
	};
}
