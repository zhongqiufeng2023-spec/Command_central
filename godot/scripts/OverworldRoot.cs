using Godot;
using System;

/// <summary>
/// 骑砍式战略大地图(200×130 瓦片 ≈ 3200×2080 px,镜头跟随)。
/// 布局按第一关文档:帅帐(周崇)在你身后(西)→ 你的前锋营 → 官道东进 → 涧水渡口 →
/// 黑松岭大岭(中东) —— 当面之敌(敌军一路副将)扼守岭东,候你接敌;远东为虏帐。
/// WASD/点击=行军 · C=扎营入帐 · 靠近敌军即遭遇 → 入中军帐备战。
/// 你在大地图上是「一队人马」仪仗标记;将军本人的立绘只在中军帐/营区。
/// </summary>
public partial class OverworldRoot : Node2D
{
	private const int TW = 200, TH = 130;
	private const float TS = 16f;
	private static readonly Vector2 ViewCenter = new(560, 380);
	// 地表:0草 1林 2河 3滩 4官道 5山 6我营 7虏帐 8屋舍
	private readonly byte[,] _t = new byte[TW, TH];

	private Vector2 _pos;
	private Vector2? _moveTarget;
	private bool _faceLeft;
	private bool _moving;

	private Vector2 _enemy;
	private static readonly Vector2 EnemyPost = new(150 * TS, 64 * TS);   // 敌军汛地:黑松岭东缘
	private double _t0;
	private string _banner = ""; private double _bannerAge = 99;
	private string _pendingBanner = "";                                   // 合上卷轴后再弹(免被盖住)
	private Font _font = null!;
	private readonly PauseOverlay _menu = new();

	// —— 事件层:谒见周帅 / 虏帐章末 / 敌情预警 ——
	private static readonly Vector2 HqCamp = new(19 * TS, 64 * TS);       // 帅帐(周崇)
	private static readonly Vector2 FoeCamp = new(184 * TS, 62 * TS);     // 虏帐
	private bool _letterOpen;                                             // 谒见军令卷轴
	private bool _ending;                                                 // 章末结算
	private bool _warnedSighting;
	private bool _duskPrompted;                                           // 每晚只提醒一次「歇营还是兼程」
	private bool _grainWarned40, _grainWarned10;
	private double _eventCd = 26;                                         // 行军人味小报冷却
	private readonly Random _evRng = new();
	private bool NearHq => _pos.DistanceTo(HqCamp) < 100f;

	// 行军路上的人味小报(荒诞表层——Radio Commander 味)
	private static readonly string[] MarchFlavor =
	{
		"塘骑来报:岭上火光十数处!……细看,是牧人烧荒。虚惊。",
		"前哨拿住一个『虏谍』,搜出干粮三块——是邻村货郎,放了。",
		"军中传言:虏骑有三万之众。传到第五营,变成了八万。",
		"辎重营小校来报:骡子啃了半面认旗,请示是否记过。",
		"斥候王二狗回报:『前面……前面全是树。』——黑松岭,确实全是树。",
		"伙夫头儿抱怨:再赶路,腌菜坛子要颠碎第三个了。",
		"夜里有士卒说梦话喊『杀』,惊动半营人拔刀站了一刻钟。",
	};

	private static readonly string[] Shichen = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
	private static float DayHour => GameState.I.CampaignHours % 24f;
	/// <summary>入夜程度 0..1(酉末起、卯初散)。</summary>
	private static float NightFactor
	{
		get
		{
			float h = DayHour;
			return h < 4f ? 1f : h < 6f ? (6f - h) / 2f : h < 18f ? 0f : h < 21f ? (h - 18f) / 3f : 1f;
		}
	}
	private static string CalendarCn =>
		$"第{(int)(GameState.I.CampaignHours / 24f) + 1}日 {Shichen[((int)DayHour + 1) / 2 % 12]}时";

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.Nearest;
		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		_font = sf;

		BuildMap();
		_pos = GameState.I.PartyPos;
		_enemy = GameState.I.EnemyPos;
		GameState.I.SaveRun();                          // 上大地图即落一笔存档(战毕班师也走这里)
		GetWindow().GrabFocus();
	}

	private void BuildMap()
	{
		for (int x = 0; x < TW; x++)
			for (int y = 0; y < TH; y++)
				_t[x, y] = (byte)(y < 6 + (x * 7) % 4 ? 5 : 0);              // 北缘群山(锯齿)

		// 黑松岭:中东部大岭(参差的松海)
		for (int y = 22; y <= 100; y++)
		{
			int wobL = (y * 13) % 9, wobR = (y * 7) % 7;
			for (int x = 112 - wobL; x <= 146 + wobR; x++) Set(x, y, 1);
		}
		Paint(120, 60, 140, 70, 0);                                          // 岭中谷口(官道穿岭)
		for (int x = 120; x <= 140; x++) { Set(x, 64, 4); Set(x, 65, 4); }

		// 涧水:北山出,南流入野(在前锋营与黑松岭之间)
		for (int y = 8; y < TH; y++)
		{
			int rx = 92 + (int)(6 * Math.Sin(y * 0.12));
			Set(rx, y, 2); Set(rx + 1, y, 2); Set(rx + 2, y, 2);
		}
		Paint(90, 63, 97, 66, 3);                                            // 官道渡滩
		Paint(88, 96, 95, 99, 3);                                            // 南渡滩

		// 官道:西起帅帐,东抵虏帐
		for (int x = 0; x < TW; x++) { Set(x, 64, 4); Set(x, 65, 4); }

		// 零星小林与丘
		Paint(30, 30, 40, 38, 1); Paint(58, 88, 68, 96, 1); Paint(160, 30, 172, 40, 1);
		Paint(44, 14, 58, 20, 5); Paint(150, 96, 166, 104, 5);

		// 帅帐(周崇,你身后)· 前锋营(你)· 虏帐(远东)· 边镇(西南)
		Paint(16, 60, 22, 68, 6);
		Paint(52, 60, 58, 68, 6);
		Paint(180, 58, 188, 66, 7);
		Paint(26, 100, 36, 108, 8);
	}
	private void Set(int x, int y, byte v) { if (x >= 0 && x < TW && y >= 0 && y < TH) _t[x, y] = v; }
	private void Paint(int x0, int y0, int x1, int y1, byte v)
	{ for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) Set(x, y, v); }

	private byte At(Vector2 p)
	{
		int x = Math.Clamp((int)(p.X / TS), 0, TW - 1), y = Math.Clamp((int)(p.Y / TS), 0, TH - 1);
		return _t[x, y];
	}
	private static bool Passable(byte t) => t != 2 && t != 5;
	private static float SpeedMult(byte t) => t switch { 1 => 0.55f, 3 => 0.45f, 4 => 1.45f, _ => 1f };

	private Vector2 CamOffset() => ViewCenter - _pos;

	public override void _Process(double delta)
	{
		_t0 += delta; _bannerAge += delta;
		if (_menu.Open || _letterOpen || _ending) { QueueRedraw(); return; }
		float dt = (float)delta;

		var dir = Vector2.Zero;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
		if (dir != Vector2.Zero) _moveTarget = null;
		else if (_moveTarget is { } tgt)
		{
			var d = tgt - _pos;
			if (d.Length() < 5f) _moveTarget = null; else dir = d.Normalized();
		}

		_moving = dir != Vector2.Zero;
		if (_moving)
		{
			float hours = dt * 1.2f;                                      // 行军走表(夜行更慢,见下)
			var gs = GameState.I;
			gs.CampaignHours += hours;
			bool night = NightFactor > 0.6f;
			gs.Grain = Math.Max(0, gs.Grain - hours * 1.1f);              // 人吃马嚼
			gs.Fatigue = Math.Min(100, gs.Fatigue + hours * (night ? 7f : 2.2f));   // 夜行倍疲

			float speed = 130f * SpeedMult(At(_pos)) * (night ? 0.75f : 1f);
			var next = _pos + dir.Normalized() * speed * dt;
			next.X = Math.Clamp(next.X, 10, TW * TS - 10); next.Y = Math.Clamp(next.Y, 10, TH * TS - 10);
			if (Passable(At(next))) _pos = next;
			else
			{
				var sx = new Vector2(next.X, _pos.Y);
				var sy = new Vector2(_pos.X, next.Y);
				if (Passable(At(sx))) _pos = sx;
				else if (Passable(At(sy))) _pos = sy;
			}
			if (Mathf.Abs(dir.X) > 0.01f) _faceLeft = dir.X < 0;

			// 行军人味小报(冷却随机)
			_eventCd -= delta;
			if (_eventCd <= 0)
			{
				_eventCd = 20 + _evRng.Next(22);
				_banner = MarchFlavor[_evRng.Next(MarchFlavor.Length)]; _bannerAge = 0;
			}
		}

		// —— 昼夜与后勤的提点 ——
		{
			var gs = GameState.I;
			float h = DayHour;
			if (h >= 17f && h < 21f && !_duskPrompted)
			{ _duskPrompted = true; _banner = "天色向晚——R=歇营过夜(养力省粮),或趁夜兼程(慢且倍疲)。"; _bannerAge = 0; }
			if (h >= 6f && h < 16f) _duskPrompted = false;
			if (gs.Grain < 40f && !_grainWarned40)
			{ _grainWarned40 = true; _banner = "粮官来报:军粮不足四成——该回帅帐领粮了。"; _bannerAge = 0; }
			if (gs.Grain < 10f && !_grainWarned10)
			{ _grainWarned10 = true; _banner = "粮将尽!再拖下去,士卒要枵腹而战了。"; _bannerAge = 0; }
			if (gs.Grain >= 40f) _grainWarned40 = false;
			if (gs.Grain >= 10f) _grainWarned10 = false;
		}

		// —— 事件:虏帐(先破当面之虏方可近前;破敌后抵达=章末)——
		if (_pos.DistanceTo(FoeCamp) < 120f)
		{
			if (GameState.I.EnemyDefeated)
			{
				if (!_ending) Sfx.Play(this, Sfx.Horn, -5f);
				_ending = true; _moveTarget = null;
			}
			else
			{
				_pos += (_pos - FoeCamp).Normalized() * 46f; _moveTarget = null;
				_banner = "虏帐守备森严,游骑四出——先破当面之虏,再图斯地。"; _bannerAge = 0;
			}
		}

		// —— 当面之敌:扼守黑松岭东缘;你进抵岭一线(靠近)即出而接敌 ——
		if (!GameState.I.EnemyDefeated)
		{
			var toMe = _pos - _enemy;
			if (toMe.Length() < 520f && !_warnedSighting)
			{ _warnedSighting = true; _banner = "塘报:虏骑出汛,正向我逼来!(C=扎营备战,可先布阵)"; _bannerAge = 0; }
			else if (toMe.Length() > 700f) _warnedSighting = false;
			if (toMe.Length() < 340f)
				_enemy += toMe.Normalized() * 92f * dt;                       // 出汛接敌
			else if ((_enemy - EnemyPost).Length() > 24f)
				_enemy += (EnemyPost - _enemy).Normalized() * 60f * dt;       // 归汛
			else
				_enemy = EnemyPost + new Vector2(Mathf.Cos((float)_t0 * 0.5f), Mathf.Sin((float)_t0 * 0.7f)) * 14f;

			if (toMe.Length() < 30f)
			{
				GameState.I.PartyPos = _pos; GameState.I.EnemyPos = _enemy;
				GameState.I.StartBattle();
				Sfx.Play(this, Sfx.Drum, -4f);
				GameState.Go(this, "res://Tent.tscn");
				return;
			}
		}

		QueueRedraw();
	}

	public override void _Input(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false } k)
		{
			if (_ending)
			{
				if (k.Keycode is Key.Enter or Key.KpEnter)
				{ SyncState(); GameState.I.SaveRun(); GameState.Go(this, "res://Title.tscn"); }
				return;
			}
			if (_letterOpen)
			{
				if (k.Keycode is Key.E or Key.Escape or Key.Enter or Key.KpEnter)
				{
					_letterOpen = false;
					if (_pendingBanner != "") { _banner = _pendingBanner; _bannerAge = 0; _pendingBanner = ""; }
				}
				return;
			}
			switch (_menu.HandleKey(k, out bool consumed))
			{
				case PauseOverlay.Act.SaveToTitle:
					SyncState(); GameState.I.SaveRun(); GameState.Go(this, "res://Title.tscn"); return;
				case PauseOverlay.Act.Quit:
					SyncState(); GameState.I.SaveRun(); GetTree().Quit(); return;
			}
			if (consumed) return;
			if (k.Keycode == Key.C)
			{
				SyncState();
				GameState.I.CampOnly = true; GameState.I.Battle = null;
				GameState.Go(this, "res://Tent.tscn");
			}
			else if (k.Keycode == Key.R)
			{
				// 歇营过夜:养力省粮——「扎营 vs 夜行」抉择的另一半
				float h = DayHour;
				var gs = GameState.I;
				if (h >= 17f || h < 5f)
				{
					float until = h < 5f ? 6f - h : 24f - h + 6f;
					gs.CampaignHours += until;
					gs.Grain = Math.Max(0, gs.Grain - until * 0.4f);
					gs.Fatigue = Math.Max(0, gs.Fatigue - 48f);
					_banner = "安营下寨,人马饱歇——明晨卯时拔营。"; _bannerAge = 0;
					Sfx.Play(this, Sfx.Click);
					SyncState(); gs.SaveRun();
				}
				else { _banner = "白日正长,何必歇营?(黄昏后方可歇营过夜)"; _bannerAge = 0; }
			}
			else if (k.Keycode == Key.E && NearHq)
			{
				_letterOpen = true; Sfx.Play(this, Sfx.Click);
				// 顺道领粮:粮官的「鼠耗」是这支军队的固有摩擦
				var gs = GameState.I;
				if (gs.Grain < 90f)
				{
					int got = 82 + _evRng.Next(19);
					gs.Grain = got;
					_pendingBanner = got < 92
						? $"粮官王禄拨付军粮,点验短了{100 - got}分——曰:『鼠耗』。"
						: "粮官王禄拨付军粮,足额——今儿太阳打西边出来了。";
				}
			}
		}
		else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && !_menu.Open && !_letterOpen && !_ending)
			_moveTarget = mb.Position - CamOffset();
	}

	private void SyncState() { GameState.I.PartyPos = _pos; GameState.I.EnemyPos = _enemy; }

	public override void _Draw()
	{
		DrawRect(new Rect2(0, 0, 1120, 760), new Color("15130f"), true);
		var cam = CamOffset();
		DrawSetTransform(cam, 0, Vector2.One);

		// —— 世界层(只画视口内瓦片)——
		int x0 = Math.Clamp((int)(-cam.X / TS) - 1, 0, TW - 1);
		int y0 = Math.Clamp((int)(-cam.Y / TS) - 1, 0, TH - 1);
		int x1 = Math.Clamp(x0 + (int)(1120 / TS) + 3, 0, TW);
		int y1 = Math.Clamp(y0 + (int)(760 / TS) + 3, 0, TH);
		for (int x = x0; x < x1; x++)
			for (int y = y0; y < y1; y++)
			{
				Color c = _t[x, y] switch
				{
					1 => new Color("2e4023"), 2 => new Color("2d4d5e"), 3 => new Color("3e6172"),
					4 => new Color("6b5b3e"), 5 => new Color("55504a"), 6 => new Color("5a4632"),
					7 => new Color("46404a"), 8 => new Color("6e5b41"), _ => new Color("4a5232")
				};
				if (((x + y) & 1) == 0) c = c.Darkened(0.05f);
				DrawRect(new Rect2(x * TS, y * TS, TS, TS), c, true);
			}

		// 地标
		DrawLabel(new Vector2(19 * TS, 57 * TS), "帅帐 · 周崇", new Color("e6c25c"));
		DrawFlag(new Vector2(19 * TS, 60 * TS), new Color("e6c25c"));
		DrawLabel(new Vector2(55 * TS, 57 * TS), "前锋营(本部)", new Color("ffd9a0"));
		DrawFlag(new Vector2(55 * TS, 60 * TS), new Color("d9b34a"));
		DrawLabel(new Vector2(129 * TS, 20 * TS), "黑 松 岭", new Color("9fbf8a"));
		DrawLabel(new Vector2(95 * TS, 58 * TS), "涧水渡", new Color("8ab4c8"));
		DrawLabel(new Vector2(184 * TS, 55 * TS), "虏帐", new Color("bcd2ec"));
		DrawLabel(new Vector2(31 * TS, 97 * TS), "边镇", new Color("c8bfa8"));

		// 当面之敌(扼守岭东)
		if (!GameState.I.EnemyDefeated)
		{
			DrawCircle(_enemy, 8f, new Color(0.08f, 0.08f, 0.1f));
			DrawCircle(_enemy + new Vector2(7, -3), 5f, new Color(0.08f, 0.08f, 0.1f));
			DrawCircle(_enemy + new Vector2(-7, 2), 5f, new Color(0.08f, 0.08f, 0.1f));
			for (int i = 0; i < 3; i++)
			{
				float ph = ((float)_t0 * 0.7f + i * 0.33f) % 1f;
				DrawCircle(_enemy + new Vector2(-9 - ph * 15, -3 - ph * 9), 3f + ph * 6f,
					new Color(0.6f, 0.56f, 0.48f, 0.35f * (1 - ph)));
			}
			DrawLabel(_enemy + new Vector2(0, -22), "当面之敌", new Color("bcd2ec"));
		}

		// 我方仪仗(一队人马 + 牙旗;将军立绘在中军帐/营区)
		float fx = _faceLeft ? -1f : 1f;
		if (_moving)
			for (int i = 0; i < 3; i++)
			{
				float ph = ((float)_t0 * 0.8f + i * 0.33f) % 1f;
				DrawCircle(_pos + new Vector2((-10 - ph * 14) * fx, -1 - ph * 7), 2.5f + ph * 4.5f,
					new Color(0.62f, 0.58f, 0.5f, 0.3f * (1 - ph)));
			}
		var umber = new Color(0.42f, 0.16f, 0.12f);
		DrawCircle(_pos + new Vector2(-7 * fx, 3), 5f, umber.Darkened(0.15f));
		DrawCircle(_pos + new Vector2(2 * fx, -1), 6f, umber);
		DrawCircle(_pos + new Vector2(10 * fx, 3), 4.5f, umber.Darkened(0.1f));
		DrawLine(_pos + new Vector2(2 * fx, -4), _pos + new Vector2(2 * fx, -26), new Color("c8b088"), 2f);
		DrawColoredPolygon(new[] { _pos + new Vector2(2 * fx, -26), _pos + new Vector2(2 * fx + 15 * fx, -21.5f), _pos + new Vector2(2 * fx, -17) }, new Color("d9b34a"));
		DrawLabel(_pos + new Vector2(0, -34), "本部", new Color("ffd9a0"));

		if (_moveTarget is { } t2)
			DrawArc(t2, 7f, 0, Mathf.Tau, 16, new Color(1, 1, 1, 0.5f), 1.5f);

		if (NearHq)
			DrawLabel(HqCamp + new Vector2(0, 40), "E=谒见周帅", new Color("f2e6c8"));

		DrawSetTransform(Vector2.Zero, 0, Vector2.One);

		// —— 昼夜:夜行野暗(酉末入夜、卯初天明)——
		if (NightFactor > 0.01f)
			DrawRect(new Rect2(0, 0, 1120, 760), new Color(0.03f, 0.05f, 0.13f, 0.42f * NightFactor), true);

		// —— HUD 层 ——
		DrawString(_font, new Vector2(14, 24), $"大酆边野 · {CalendarCn} · 主帅信任 {GameState.I.Trust}", HorizontalAlignment.Left, -1, 16, new Color("e8e0d0"));
		DrawString(_font, new Vector2(14, 44),
			GameState.I.EnemyDefeated
				? "虏骑已绝迹于野。C=扎营入帐 · R=歇营过夜 · WASD/点击=行军 · Esc=菜单"
				: "军令:进抵黑松岭一线,试探当面之敌 | WASD/点击=行军 · C=扎营入帐 · R=歇营过夜 · Esc=菜单",
			HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));

		// 后勤条:粮草与疲惫(行军的两本账)
		{
			var gs = GameState.I;
			DrawString(_font, new Vector2(14, 66), "粮草", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
			DrawRect(new Rect2(50, 56, 96, 9), new Color(0.15f, 0.14f, 0.11f, 0.85f), true);
			DrawRect(new Rect2(50, 56, 96 * gs.Grain / 100f, 9),
				gs.Grain > 35 ? new Color("8fa86a") : new Color("d0894a"), true);
			DrawString(_font, new Vector2(166, 66), "疲惫", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
			DrawRect(new Rect2(202, 56, 96, 9), new Color(0.15f, 0.14f, 0.11f, 0.85f), true);
			DrawRect(new Rect2(202, 56, 96 * gs.Fatigue / 100f, 9),
				gs.Fatigue < 55 ? new Color("8a8474") : gs.Fatigue < 80 ? new Color("d0a84a") : new Color("c46a4a"), true);
		}
		DrawMinimap();

		if (_banner != "" && _bannerAge < 4)
			DrawString(_font, new Vector2(360, 90), _banner, HorizontalAlignment.Left, -1, 14, new Color("f2e6c8"));

		DrawLetter();
		DrawEnding();
		_menu.Draw(this, _font);
	}

	/// <summary>谒见周帅:中军令卷轴(破敌前=军令;破敌后=嘉勉与新命)。</summary>
	private void DrawLetter()
	{
		if (!_letterOpen) return;
		DrawRect(new Rect2(0, 0, 1120, 760), new Color(0, 0, 0, 0.6f), true);
		float w = 640, h = 420;
		var box = new Rect2((1120 - w) / 2, (760 - h) / 2, w, h);
		DrawRect(box, new Color(0.16f, 0.13f, 0.09f, 0.98f), true);
		DrawRect(box, new Color("d9b34a"), false, 2f);
		float x = box.Position.X + 40, y = box.Position.Y + 48;

		void L(string s, int size, string col) { DrawString(_font, new Vector2(x, y), s, HorizontalAlignment.Left, (int)w - 80, size, new Color(col)); y += size + 10; }

		if (!GameState.I.EnemyDefeated)
		{
			L("征虏中军令", 20, "e6c25c"); y += 6;
			L("周崇谕前锋总兵官:", 14, "d8d2c4");
			L("虏骑犯我北鄙,现屯黑松岭以东,众寡未详。", 14, "c8c2b4");
			L("命尔部即日东进,进抵岭一线;虏情务须侦明,军书具报,", 14, "c8c2b4");
			L("相机破之。朝廷候捷,毋纵毋怠。", 14, "c8c2b4"); y += 8;
			L("——行军中扎营(C)可入中军帐;战起时先布阵再擂鼓。", 12, "9aa0a8");
		}
		else
		{
			L("周帅嘉勉", 20, "e6c25c"); y += 6;
			L($"「黑松岭之捷,行营已录。今主帅信任 {GameState.I.Trust}。」", 14, "d8d2c4");
			if (GameState.I.LastVerdict is { } v) L($"上战裁断:「{v.VerdictCn}」", 14, "c8c2b4");
			L("「虏酋新败,巢帐空虚——尔部乘胜东捣虏帐,毕其功于一役!」", 14, "c8c2b4"); y += 8;
			L("——东进至虏帐,即结此章。", 12, "9aa0a8");
		}
		DrawString(_font, new Vector2(x, box.End.Y - 30), "E / 回车 · 领命", HorizontalAlignment.Left, -1, 13, new Color("e6c25c"));
	}

	/// <summary>章末:兵抵虏帐,第一章完。</summary>
	private void DrawEnding()
	{
		if (!_ending) return;
		DrawRect(new Rect2(0, 0, 1120, 760), new Color(0, 0, 0, 0.78f), true);
		float w = 560, h = 360;
		var box = new Rect2((1120 - w) / 2, (760 - h) / 2, w, h);
		DrawRect(box, new Color(0.10f, 0.09f, 0.07f, 0.98f), true);
		DrawRect(box, new Color("d9b34a"), false, 2f);
		float y = box.Position.Y + 64;
		DrawString(_font, new Vector2(0, y), "第一章 · 黑松岭 —— 完", HorizontalAlignment.Center, 1120, 26, new Color("e6c25c")); y += 44;
		DrawString(_font, new Vector2(0, y), "虏帐已空,残部远遁漠北。边野暂靖。", HorizontalAlignment.Center, 1120, 14, new Color("c8c2b4")); y += 30;
		DrawString(_font, new Vector2(0, y), $"用时 {CalendarCn} · 主帅信任 {GameState.I.Trust}", HorizontalAlignment.Center, 1120, 14, new Color("d8d2c4")); y += 26;
		if (GameState.I.LastVerdict is { } v)
			DrawString(_font, new Vector2(0, y), $"行营终评:「{v.VerdictCn}」", HorizontalAlignment.Center, 1120, 14, new Color("f2e6c8"));
		y += 34;
		DrawString(_font, new Vector2(0, y), $"此章,尔部埋骨边野者 {GameState.I.Bones} 人。", HorizontalAlignment.Center, 1120, 14, new Color("c8a8a0")); y += 26;
		DrawString(_font, new Vector2(0, y), "一将功成万骨枯。", HorizontalAlignment.Center, 1120, 13, new Color("8a8474")); y += 36;
		DrawString(_font, new Vector2(0, y), "回车 · 回主菜单", HorizontalAlignment.Center, 1120, 14, new Color("e6c25c"));
	}

	private void DrawMinimap()
	{
		const float MS = 0.055f;                                          // 3200×2080 → 176×114
		var org = new Vector2(1120 - TW * TS * MS - 14, 14);
		DrawRect(new Rect2(org - new Vector2(3, 3), new Vector2(TW * TS * MS + 6, TH * TS * MS + 6)), new Color(0, 0, 0, 0.55f), true);
		for (int x = 0; x < TW; x += 3)
			for (int y = 0; y < TH; y += 3)
			{
				Color c = _t[x, y] switch
				{
					1 => new Color("2e4023"), 2 or 3 => new Color("2d4d5e"), 4 => new Color("6b5b3e"),
					5 => new Color("55504a"), 6 => new Color("d9b34a"), 7 => new Color("8ab"),
					8 => new Color("6e5b41"), _ => new Color("3c412a")
				};
				DrawRect(new Rect2(org.X + x * TS * MS, org.Y + y * TS * MS, TS * MS * 3, TS * MS * 3), c, true);
			}
		DrawCircle(org + _pos * MS, 3f, new Color("ffd9a0"));
		if (!GameState.I.EnemyDefeated) DrawCircle(org + _enemy * MS, 3f, new Color(0.1f, 0.1f, 0.12f));
	}

	private void DrawFlag(Vector2 p, Color c)
	{
		DrawLine(p, p + new Vector2(0, -24), new Color("c8b088"), 2f);
		DrawColoredPolygon(new[] { p + new Vector2(0, -24), p + new Vector2(17, -18.5f), p + new Vector2(0, -13) }, c);
	}

	private void DrawLabel(Vector2 p, string s, Color c)
		=> DrawString(_font, p + new Vector2(-70, 0), s, HorizontalAlignment.Center, 140, 13, c);
}
