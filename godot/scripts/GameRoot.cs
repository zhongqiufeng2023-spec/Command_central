using Godot;
using CommandPost.Core;
using System.Collections.Generic;
using System.Linq;
using Side = CommandPost.Core.Side;   // 消歧:Godot 也有 Side 枚举

/// <summary>
/// 真实世界 · 直控开发视图(god view)。
/// 直接读 Truth、直接指挥真实部队、看敌我真实动向与每单位属性。
/// 连续渲染:地形色块(无格线)+ 帧间按 tick 进度插值 → 平滑移动(内核仍是格步进)。
/// 迷雾:铺暗雾 + 视野亮圈(你的单位/斥候/帐);敌仍暴露,但视野外标「雾中」→ 用来判断视野范围。
/// </summary>
public partial class GameRoot : Node2D
{
	private const float CellPx = 34f;
	private const float OriginX = 40f, OriginY = 56f;

	private Simulation _sim = null!;
	private Font _font = null!;
	private int _selectedId = -1;

	private bool _paused = true;
	private double _tickAccum;
	private int _speedIdx = 1;
	private static readonly double[] Speeds = { 0.5, 1, 2, 4 };
	private const double BaseTicksPerSec = 1.0;   // 慢下来、从容(移动太快的元凶之一)

	// 平滑:每单位「上一 tick 的像素位置」;显示 = lerp(上一格, 当前格, tick进度)
	private readonly Dictionary<int, Vector2> _prevPix = new();
	private readonly Dictionary<int, Vector2> _disp = new();

	public override void _Ready()
	{
		_sim = Scenario.FirstBattleBlackPine(DifficultySettings.Normal);

		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		_font = sf;

		foreach (var u in _sim.Truth.Units) { _prevPix[u.Id] = ToPixel(u.Pos); _disp[u.Id] = ToPixel(u.Pos); }

		// dev:让草原 AI 立刻"知道"你的位置,便于观察敌方动向。
		foreach (var u in _sim.Truth.LivingOf(Side.Friend))
			_sim.Beliefs[Side.Enemy].Known[u.Id] = new GhostUnit
			{
				UnitId = u.Id, Side = Side.Friend, LastKnownPos = u.Pos,
				KnownStrength = u.Strength, KnownType = u.Type, ObservedTick = 0
			};

		GetWindow().GrabFocus();   // 主动抢键盘焦点(嵌入编辑器时也能收到空格/+-)
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (!_paused && _sim.Status == GameStatus.Ongoing)
		{
			_tickAccum += delta * BaseTicksPerSec * Speeds[_speedIdx];
			int guard = 0;
			while (_tickAccum >= 1.0 && _sim.Status == GameStatus.Ongoing && guard++ < 50)
			{
				foreach (var u in _sim.Truth.Units) _prevPix[u.Id] = ToPixel(u.Pos);   // 记住 tick 前位置
				_sim.AdvanceTick();
				_tickAccum -= 1.0;
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
			case Key.Space: _paused = !_paused; break;
			case Key.N:
				if (_sim.Status == GameStatus.Ongoing)
				{ foreach (var u in _sim.Truth.Units) _prevPix[u.Id] = ToPixel(u.Pos); _sim.AdvanceTick(); }
				break;
			case Key.Equal: if (_speedIdx < Speeds.Length - 1) _speedIdx++; break;
			case Key.Minus: if (_speedIdx > 0) _speedIdx--; break;
			case Key.Escape: _selectedId = -1; break;
		}
		QueueRedraw();
	}

	private void HandleClick(InputEventMouseButton mb)
	{
		var world = GetGlobalMousePosition();
		if (_sim.Status != GameStatus.Ongoing && mb.ButtonIndex != MouseButton.Left) return;

		switch (mb.ButtonIndex)
		{
			case MouseButton.Left:
				var pick = _sim.Truth.LivingOf(Side.Friend).OrderBy(u => DispPos(u).DistanceTo(world)).FirstOrDefault();
				_selectedId = (pick != null && DispPos(pick).DistanceTo(world) < CellPx) ? pick.Id : -1;
				break;

			case MouseButton.Right when _selectedId >= 0:
				var u2 = _sim.Truth.UnitById(_selectedId);
				if (u2 is { Alive: true })
				{
					// 直控:点敌=攻击,否则=移动。直接写 Order,无传令延迟。
					var enemy = _sim.Truth.LivingOf(Side.Enemy).FirstOrDefault(en => DispPos(en).DistanceTo(world) < CellPx);
					u2.Order = enemy != null ? Intent.Attack(enemy.Id) : Intent.Move(ToCell(world));
				}
				break;

			case MouseButton.Middle:
				_sim.DispatchScoutFromHq(ToCell(world));   // 中军直接派斥候去探(即时)
				break;
		}
		QueueRedraw();
	}

	// —— 视野:某点是否在你的视野内(单位 / 斥候 / 帐上瞭望)——
	private bool Seen(Vec2 pos)
	{
		var g = _sim.Truth.Terrain;
		foreach (var u in _sim.Truth.LivingOf(Side.Friend))
			if (pos.DistanceTo(u.Pos) <= u.Vision * g.ScoutMultiplier(u.Pos)) return true;
		foreach (var s in _sim.Truth.Scouts.Where(s => s.Side == Side.Friend))
			if (pos.DistanceTo(s.Pos) <= s.Vision * g.ScoutMultiplier(s.Pos)) return true;
		return pos.DistanceTo(_sim.Truth.FriendHq) <= _sim.Difficulty.WatchtowerRange * g.ScoutMultiplier(pos);
	}

	// —— 绘制 ——
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

		// 迷雾:暗雾铺满 + 视野亮圈(单位 / 斥候 / 帐)
		DrawRect(field, new Color(0.04f, 0.05f, 0.07f, 0.44f), true);
		void Reveal(Vector2 c, float rCells) => DrawCircle(c, rCells * CellPx, new Color(0.82f, 0.78f, 0.58f, 0.11f));
		foreach (var u in _sim.Truth.LivingOf(Side.Friend)) Reveal(DispPos(u), (float)(u.Vision * g.ScoutMultiplier(u.Pos)));
		foreach (var s in _sim.Truth.Scouts.Where(s => s.Side == Side.Friend)) Reveal(ToPixel(s.Pos), (float)(s.Vision * g.ScoutMultiplier(s.Pos)));
		Reveal(ToPixel(_sim.Truth.FriendHq), (float)(_sim.Difficulty.WatchtowerRange * g.ScoutMultiplier(_sim.Truth.FriendHq)));

		DrawHq(_sim.Truth.FriendHq, new Color("d9b34a"), "帐");
		DrawHq(_sim.Truth.EnemyHq, new Color("8a8a8a"), "敌营");

		// 斥候(带朝向标)
		foreach (var s in _sim.Truth.Scouts.Where(s => s.Side == Side.Friend))
		{
			var sp = ToPixel(s.Pos);
			DrawCircle(sp, CellPx * 0.16f, new Color("5ad0c0"));
			var sdir = new Vector2(s.Facing.X, s.Facing.Y);
			if (sdir.LengthSquared() > 0)
				DrawLine(sp, sp + sdir.Normalized() * CellPx * 0.30f, new Color("baf0e6"), 2f);
			DrawLabel(sp, "斥", -0.9f, new Color("baf0e6"));
		}

		// 敌军(真相:始终画;视野外标「雾中」并变淡)
		foreach (var u in _sim.Truth.Units.Where(u => u.Side == Side.Enemy && u.Strength > 0))
			DrawUnit(u, seenOverride: Seen(u.Pos));
		// 情报判断:斥候回报的「敌军最后已知位置」(空心菱形,可能已滞后/失真 → 与真相错位就是情报的代价)
		DrawBeliefGhosts();
		// 我军
		foreach (var u in _sim.Truth.Units.Where(u => u.Side == Side.Friend && u.Strength > 0))
			DrawUnit(u, seenOverride: true);

		DrawHud();
		DrawInspector();
	}

	private void DrawUnit(Unit u, bool seenOverride)
	{
		var p = DispPos(u);
		bool friend = u.Side == Side.Friend;
		float a = seenOverride ? 1f : 0.4f;
		Color fill = u.Routed ? new Color(0.5f, 0.42f, 0.42f, a) : friend ? new Color(0.70f, 0.23f, 0.18f, a) : new Color(0.25f, 0.44f, 0.63f, a);
		DrawCircle(p, CellPx * 0.30f, fill);
		DrawArc(p, CellPx * 0.30f, 0, Mathf.Tau, 24, friend ? new Color(0.90f, 0.76f, 0.36f, a) : new Color(0.62f, 0.75f, 0.88f, a), 2f, true);

		// 朝向:一个小箭头鼻子(移动时随实际位移方向,静止时随内核朝向)
		var dir = FacingPixel(u);
		Color nose = friend ? new Color(0.98f, 0.86f, 0.46f, a) : new Color(0.72f, 0.83f, 0.96f, a);
		DrawColoredPolygon(new[]
		{
			p + dir * CellPx * 0.46f,
			p + dir.Rotated(Mathf.Pi * 0.5f) * CellPx * 0.17f,
			p + dir.Rotated(-Mathf.Pi * 0.5f) * CellPx * 0.17f
		}, nose);

		if (u.Id == _selectedId) DrawArc(p, CellPx * 0.44f, 0, Mathf.Tau, 32, new Color("ffe08a"), 3f, true);
		// 选中的远程兵:画出放箭射程圈
		if (u.Id == _selectedId && u.IsRanged)
			DrawArc(p, u.ShootRange * CellPx, 0, Mathf.Tau, 48, new Color(0.98f, 0.85f, 0.45f, 0.35f), 1.5f, true);

		if (OrderTargetPixel(u) is Vector2 tp && tp != p)
			DrawLine(p, p + (tp - p).Normalized() * CellPx * 0.5f, new Color(1, 1, 1, 0.3f * a), 2f);

		DrawLabel(p, $"{u.Name}·{ArmCn(u.Type)}", -1.35f, friend ? new Color("fdf6e3") : new Color(0.81f, 0.88f, 0.94f, a));
		DrawLabel(p, seenOverride ? $"兵{(int)u.Strength}" : "雾中", 0.95f, seenOverride ? new Color("fdf6e3") : new Color("d6a44a"));
		if (seenOverride) DrawBars(p, u);
	}

	private Vector2? OrderTargetPixel(Unit u)
	{
		if (u.Order is null) return null;
		if (u.Order.TargetUnitId is int tid && _sim.Truth.UnitById(tid) is { Alive: true } t) return DispPos(t);
		if (u.Order.TargetPos is Vec2 tp) return ToPixel(tp);
		return null;
	}

	// 情报判断的敌军残影:斥候回报后中军「以为」敌军在哪(空心菱形 + 约数)。
	// 与真相实心圈错位 = 情报滞后;这正是「看不见却要决断」的可视化。
	private void DrawBeliefGhosts()
	{
		foreach (var gh in _sim.Beliefs[Side.Friend].KnownOf(Side.Enemy))
		{
			var p = ToPixel(gh.LastKnownPos);
			float r = CellPx * 0.34f;
			DrawPolyline(new[]
			{
				p + new Vector2(0, -r), p + new Vector2(r, 0),
				p + new Vector2(0, r), p + new Vector2(-r, 0), p + new Vector2(0, -r)
			}, new Color(0.86f, 0.62f, 0.26f, 0.62f), 1.6f);
			string est = gh.KnownStrength is double v ? $"判~{(int)v}" : "判?";
			DrawLabel(p, est, 1.5f, new Color(0.93f, 0.73f, 0.36f, 0.9f));
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
		=> DrawString(_font, new Vector2(p.X - CellPx * 0.7f, p.Y + row * 13 + 4), text,
			HorizontalAlignment.Center, CellPx * 1.4f, 12, color ?? new Color("fdf6e3"));

	private void DrawHud()
	{
		string clock = _paused ? "[暂停]" : $"[▶ {Speeds[_speedIdx]:0.#}x]";
		DrawString(_font, new Vector2(OriginX, 30),
			$"真实世界·直控   t{_sim.Truth.Tick}  {clock}  局势:{StatusCn(_sim.Status)}    "
		  + "空格 暂停 · -/+ 调速 · N 单步    左键=选我军 · 右键=移动/攻击 · 中键=派斥候(回报见右栏敌情)",
			HorizontalAlignment.Left, -1, 14, new Color("e8e0d0"));
	}

	private void DrawInspector()
	{
		var g = _sim.Truth.Terrain;
		float x = OriginX + g.Width * CellPx + 16, y = OriginY;
		DrawString(_font, new Vector2(x, y), "单位属性(真相)", HorizontalAlignment.Left, -1, 14, new Color("e6c25c"));
		y += 26;

		if (_sim.Truth.UnitById(_selectedId) is { } s && s.Strength > 0)
		{
			string[] lines =
			{
				$"{s.Name} · {ArmCn(s.Type)} · {(s.Side == Side.Friend ? "大酆" : "草原")}",
				$"武将 {s.Commander.Name}({s.Commander.PersonalityCn})",
				$"特性 {ArmTrait(s)}",
				$"兵力 {(int)s.Strength} / {(int)s.MaxStrength}",
				$"士气 {(int)s.Morale}    体力 {(int)s.Stamina}",
				$"速度 {s.BaseSpeed:0.0}    视野 {s.Vision}    位置 {s.Pos}",
				$"当前令 {(s.Order?.ToString() ?? "—")}",
				s.Routed ? "⚠ 已溃逃" : "",
			};
			foreach (var line in lines)
			{
				if (line == "") continue;
				DrawString(_font, new Vector2(x, y), line, HorizontalAlignment.Left, 260, 13, new Color("d8d2c4"));
				y += 21;
			}
		}
		else
			DrawString(_font, new Vector2(x, y), "(左键点一个单位查看)", HorizontalAlignment.Left, -1, 13, new Color("9aa0a8"));

		// —— 敌情·斥候判断:中军现在「以为」的敌军(可能滞后/失真)——
		y += 14;
		var ghosts = _sim.Beliefs[Side.Friend].KnownOf(Side.Enemy)
			.OrderBy(gh => gh.AgeAt(_sim.Truth.Tick)).ToList();
		DrawString(_font, new Vector2(x, y), $"敌情·斥候判断 ({ghosts.Count})", HorizontalAlignment.Left, -1, 14, new Color("e6c25c"));
		y += 24;
		if (ghosts.Count == 0)
		{
			DrawString(_font, new Vector2(x, y), "(未有回报 — 中键派斥候探雾)", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
			y += 20;
		}
		else foreach (var gh in ghosts.Take(9))
		{
			string ty = gh.KnownType is UnitType t ? ArmCn(t) : "兵种不明";
			string str = gh.KnownStrength is double v ? $"约{(int)v}" : "兵力不明";
			string doubt = gh.Fidelity < 0.5 ? " ⚠存疑" : "";
			DrawString(_font, new Vector2(x, y),
				$"敌#{gh.UnitId} {ty}·{str} @{gh.LastKnownPos} · {gh.AgeAt(_sim.Truth.Tick)}t前{doubt}",
				HorizontalAlignment.Left, 270, 12, new Color("d6b96a"));
			y += 19;
		}

		y += 12;
		DrawString(_font, new Vector2(x, y), "红=大酆 · 蓝=草原 · 鼻尖=朝向", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8")); y += 18;
		DrawString(_font, new Vector2(x, y), "亮圈=视野 · 敌「雾中」=视野外", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8")); y += 18;
		DrawString(_font, new Vector2(x, y), "空心菱形「判~」=情报判断位置", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8")); y += 18;
		DrawString(_font, new Vector2(x, y), "绿条=士气 蓝条=体力", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
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
		GameStatus.FriendWon => "我军胜", GameStatus.EnemyWon => "我军败", _ => "进行中"
	};
}
