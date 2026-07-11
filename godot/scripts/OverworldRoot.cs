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
	/// <summary>世界种子:大地图地貌固定成「这一方边野」——地是学得会的,跟骑砍的卡拉迪亚一样。</summary>
	private const int WorldSeed = 20260711;
	private static readonly Vector2 ViewCenter = new(560, 380);
	// 地表:0草 1林 2河 3滩 4官道 5山 6我营 7虏帐 8屋舍 9瘴土 10丘 11泽(WTerrain 对齐)
	private readonly byte[,] _t = new byte[TW, TH];
	/// <summary>高度热力图(WorldGen 输出):地表明暗浮雕用——山脊亮、洼地沉。</summary>
	private float[,] _height = new float[TW, TH];

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

	// —— 事件层:皇命 / 虏帐 / 敌情预警(骑砍式:序章一仗作引子,打完即自由,此后全由玩家探索)——
	private static readonly Vector2 FoeCamp = new(184 * TS, 62 * TS);     // 虏帐
	private bool _letterOpen;                                             // 军书卷轴(皇命/嘉勉)
	private bool _warnedSighting;
	private bool _chaosWhispered;
	private double _engageGrace;                                          // 上图后的接敌保护期(防秒接敌死循环)
	private bool _duskPrompted;                                           // 每晚只提醒一次「歇营还是兼程」
	private bool _grainWarned40, _grainWarned10;
	private double _eventCd = 26;                                         // 行军人味小报冷却
	private readonly Random _evRng = new();

	// —— 边野世界推演(你走,世界才走——骑砍式)——
	private CommandPost.Core.Rng _worldRng = new(20260711);
	private float _respawnRaider = 36f, _respawnPatrol = 48f;             // 补队冷却(行军时辰)
	private bool _warnedRaider;
	/// <summary>在大军(中军纵队)近旁——可谒见、领粮。</summary>
	private bool NearHq => _pos.DistanceTo(GameState.I.ArmyPos) < 120f;

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
		_engageGrace = 4.0;                             // 上图缓几秒再判接敌(战罢/读档都别秒开战)
		_worldRng = new CommandPost.Core.Rng((int)(Time.GetTicksMsec() % 1000003) + 7);

		if (GameState.I.PendingBanner != "")
		{ _banner = GameState.I.PendingBanner; _bannerAge = 0; GameState.I.PendingBanner = ""; }

		// 开局皇命:首次上图自动展卷(此后随时可去行营再看)——这是唯一一次主动递到你手上的纸
		if (!GameState.I.OrderRead)
		{
			GameState.I.OrderRead = true;
			_letterOpen = true;
			Sfx.Play(this, Sfx.Horn, -6f);
		}

		GameState.I.SaveRun();                          // 上大地图即落一笔存档(战毕班师也走这里)
		GetWindow().GrabFocus();
	}

	private void BuildMap()
	{
		// —— 热力图地基:高度×湿度分类出山/丘/林/泽/草(确定性;渲染取高度做浮雕)——
		var gen = CommandPost.Core.WorldGen.Terrain(WorldSeed, TW, TH, out _height);
		for (int x = 0; x < TW; x++)
			for (int y = 0; y < TH; y++)
				_t[x, y] = gen[x, y];

		// —— 世界的骨架(手绘覆盖:剧设与人文要素不交给噪声)——
		for (int x = 0; x < TW; x++)                                         // 北缘群山(锯齿边墙)
			for (int y = 0; y < 6 + (x * 7) % 4; y++) Set(x, y, 5);

		// 黑松岭:中东部大岭(参差的松海——序章战场所在)
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

		// 官道:西起帅帐,东抵虏帐(压过一切地貌——路就是给人走的)
		for (int x = 0; x < TW; x++) { Set(x, 64, 4); Set(x, 65, 4); }

		// 混沌瘴土(东南):连虏骑都绕开的地方——踏入者,心神日蚀
		Paint(168, 106, 197, 127, 9);

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
	private static float SpeedMult(byte t) => t switch
	{ 1 => 0.55f, 3 => 0.45f, 4 => 1.45f, 9 => 0.8f, 10 => 0.7f, 11 => 0.5f, _ => 1f };

	private Vector2 CamOffset() => ViewCenter - _pos;

	public override void _Process(double delta)
	{
		_t0 += delta; _bannerAge += delta;
		if (_engageGrace > 0) _engageGrace -= delta;
		if (_menu.Open || _letterOpen) { QueueRedraw(); return; }
		float dt = (float)delta;

		// —— 皇命时效:限期已过而未接战 → 行营催令(每过一日再催,信任累扣)——
		{
			var gsD = GameState.I;
			if (gsD.MissionDeadline > 0 && gsD.CampaignHours > gsD.MissionDeadline && !gsD.EnemyDefeated)
			{
				gsD.MissionDeadline += 24f;
				gsD.Trust = Math.Max(0, gsD.Trust - 5);
				_banner = "行营催令:三日之期已过,虏踪未破!周帅震怒(主帅信任 -5)。"; _bannerAge = 0;
				Sfx.Play(this, Sfx.Alert, -8f);
			}
		}

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
		float worldHours = 0f;                                            // 本帧世界该走多少表(你走,世界才走)
		if (_moving)
		{
			float hours = dt * 1.2f;                                      // 行军走表(夜行更慢,见下)
			var gs = GameState.I;
			gs.CampaignHours += hours;
			worldHours = hours;
			bool night = NightFactor > 0.6f;
			gs.Grain = Math.Max(0, gs.Grain - hours * 1.1f);              // 人吃马嚼
			gs.Fatigue = Math.Min(100, gs.Fatigue + hours * (night ? 7f : 2.2f));   // 夜行倍疲
			if (gs.Grain <= 0f) gs.San = Math.Max(0, gs.San - hours * 0.9f);        // 枵腹行军,心神俱疲

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
		else if (Input.IsKeyPressed(Key.Space))
		{
			// 空格观望:按兵不动,看世界走(想看官军和虏骑那场野战打完?站着等)
			float hours = dt * 1.8f;
			var gs = GameState.I;
			gs.CampaignHours += hours;
			worldHours = hours;
			gs.Grain = Math.Max(0, gs.Grain - hours * 1.1f);              // 原地也是人吃马嚼
			gs.Fatigue = Math.Max(0, gs.Fatigue - hours * 1.5f);          // 歇脚缓乏
		}
		if (worldHours > 0f) AdvanceWorld(worldHours);

		// —— 瘴土蚀心:立于混沌之地,心神日削(这是地理,不是剧情)——
		if (At(_pos) == 9)
		{
			GameState.I.San = Math.Max(0, GameState.I.San - dt * 1.6f);
			if (!_chaosWhispered)
			{
				_chaosWhispered = true;
				_banner = "风里有低语,像从地底下来——士卒攥紧了刀,没人敢接话。";
				_bannerAge = 0;
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

		// —— 虏帐:虏之腹地(降虏/袭帐等,留给后续内容)——
		if (_pos.DistanceTo(FoeCamp) < 120f)
		{
			_pos += (_pos - FoeCamp).Normalized() * 46f; _moveTarget = null;
			_banner = GameState.I.EnemyDefeated
				? "虏帐残部闭门自守,毡帐间无人出迎。"
				: "虏帐守备森严,游骑四出——轻身近前,讨不得好。";
			_bannerAge = 0;
		}

		// —— 当面之敌:扼守黑松岭东缘;你进抵岭一线(靠近)即出而接敌 ——
		// (_engageGrace:上图后的缓冲——战罢班师/读档,不许落地秒接敌)
		if (!GameState.I.EnemyDefeated)
		{
			var toMe = _pos - _enemy;
			if (toMe.Length() < 520f && !_warnedSighting && _engageGrace <= 0)
			{ _warnedSighting = true; _banner = "塘报:虏骑出汛,正向我逼来!(C=扎营备战,可先布阵)"; _bannerAge = 0; }
			else if (toMe.Length() > 700f) _warnedSighting = false;
			if (toMe.Length() < 340f && _engageGrace <= 0)
				_enemy += toMe.Normalized() * 92f * dt;                       // 出汛接敌
			else if ((_enemy - EnemyPost).Length() > 24f)
				_enemy += (EnemyPost - _enemy).Normalized() * 60f * dt;       // 归汛
			else
				_enemy = EnemyPost + new Vector2(Mathf.Cos((float)_t0 * 0.5f), Mathf.Sin((float)_t0 * 0.7f)) * 14f;

			if (toMe.Length() < 30f && _engageGrace <= 0)
			{
				GameState.I.PartyPos = _pos; GameState.I.EnemyPos = _enemy;
				GameState.I.StartBattle();
				Sfx.Play(this, Sfx.Drum, -4f);
				GameState.Go(this, "res://Tent.tscn");
				return;
			}
		}

		// —— 边野活物与你:劫掠队可避可撞;正打着的战团,你也可以一头撞进去 ——
		if (_engageGrace <= 0)
		{
			var gsW = GameState.I;
			bool anyNear = false;
			for (int i = 0; i < gsW.Parties.Count; i++)
			{
				var p = gsW.Parties[i];
				if (p.State == 3 || !p.Hostile) continue;
				float d = _pos.DistanceTo(p.Pos);
				if (d < 430f)
				{
					anyNear = true;
					if (!_warnedRaider)
					{
						_warnedRaider = true;
						_banner = "塘骑驰报:虏骑一股游弋于近处,烟尘不小——避让还是接战,尔自斟酌。";
						_bannerAge = 0; Sfx.Play(this, Sfx.Alert, -10f);
					}
				}
				if (d < 30f)
				{
					// 它若正与官军酣战——你是提兵撞进战团的,官军入阵为友邻
					int ally = -1;
					foreach (var c in gsW.Clashes) if (c.B == i) { ally = c.A; break; }
					gsW.Clashes.RemoveAll(c => c.A == i || c.B == i);
					if (ally >= 0) gsW.Parties[ally].State = 0;
					p.State = 0;
					SyncState();
					gsW.StartEncounter(i, ally, At((_pos + p.Pos) / 2f));
					Sfx.Play(this, Sfx.Drum, -4f);
					GameState.Go(this, "res://Tent.tscn");
					return;
				}
			}
			if (!anyNear) _warnedRaider = false;
		}

		QueueRedraw();
	}

	public override void _Input(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false } k)
		{
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
					gs.San = Math.Min(100, gs.San + 5f);                  // 一夜安枕,心神稍复
					AdvanceWorld(until);                                  // 你睡下了,边野的日子照过
					_banner = "安营下寨,人马饱歇——明晨卯时拔营。"; _bannerAge = 0;
					Sfx.Play(this, Sfx.Click);
					SyncState(); gs.SaveRun();
				}
				else { _banner = "白日正长,何必歇营?(黄昏后方可歇营过夜)"; _bannerAge = 0; }
			}
			else if (k.Keycode == Key.E && NearHq)
			{
				_letterOpen = true; Sfx.Play(this, Sfx.Click);
				var gs = GameState.I;
				var parts = new System.Collections.Generic.List<string>();

				// 领粮:粮官的「鼠耗」是这支军队的固有摩擦
				if (gs.Grain < 90f)
				{
					int got = 82 + _evRng.Next(19);
					gs.Grain = got;
					parts.Add(got < 92
						? $"粮官王禄拨付军粮,点验短了{100 - got}分——曰:『鼠耗』"
						: "粮官王禄拨付军粮,足额——今儿太阳打西边出来了");
				}
				// 补员:行营拨戍卒填缺(每次谒见补上缺额的六成——余下的,得再跑一趟)
				int replenished = 0;
				for (int i = 0; i < gs.OwnStrength.Length; i++)
				{
					int deficit = CommandPost.Core.BattleScenario.OwnFullStrength[i] - gs.OwnStrength[i];
					if (deficit <= 0) continue;
					int add = Math.Max(deficit > 0 ? 5 : 0, (int)(deficit * 0.6f));
					add = Math.Min(add, deficit);
					gs.OwnStrength[i] += add;
					replenished += add;
				}
				if (replenished > 0)
					parts.Add($"行营拨戍卒 {replenished} 员补入尔部(新卒生疏,聊胜于无)");
				if (parts.Count > 0)
					_pendingBanner = string.Join(";", parts) + "。";
				gs.SaveRun();
			}
		}
		else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && !_menu.Open && !_letterOpen)
			_moveTarget = mb.Position - CamOffset();
	}

	private void SyncState() { GameState.I.PartyPos = _pos; GameState.I.EnemyPos = _enemy; }

	// ====================================================================
	//  边野世界推演:你走,世界才走(骑砍式)——
	//  队伍游弋、官军×虏骑相遇接战、野战按规模限时分晓、散尽的过阵子再开出来。
	// ====================================================================

	private void AdvanceWorld(float hours)
	{
		while (hours > 0f)
		{
			float dt = Mathf.Min(0.5f, hours);                            // 小步推,免得一大步跨山越河
			hours -= dt;
			StepWorld(dt);
		}
	}

	private void StepWorld(float dt)
	{
		var gs = GameState.I;

		// —— 游弋与遁走 ——
		foreach (var p in gs.Parties)
		{
			if (p.State is 1 or 3) continue;
			if (p.State == 2)
			{
				var home = p.Kind == 0 ? GameState.FoeHome : gs.ArmyPos;
				var dHome = home - p.Pos;
				if (dHome.Length() < 60f) { p.Men += p.Men / 5; p.State = 0; p.Target = Vector2.Zero; }  // 归巢整补再出
				else p.Pos += dHome.Normalized() * 150f * dt;                                            // 溃逃快马
				continue;
			}
			// 劫掠队见你近了:咬得动就咬,惹不起绕道(它掂量的是你的兵形——五十抽一,看得见)
			if (p.Hostile)
			{
				float dp = p.Pos.DistanceTo(_pos);
				if (dp < 280f)
					p.Target = gs.OwnTotal < p.Men * 1.5f ? _pos
							 : p.Pos + (p.Pos - _pos).Normalized() * 340f;
			}
			if (p.Target == Vector2.Zero || p.Pos.DistanceTo(p.Target) < 24f)
				p.Target = PickWaypoint(p);
			var dir = p.Target - p.Pos;
			if (dir.Length() < 1f) continue;
			var next = p.Pos + dir.Normalized() * 95f * SpeedMult(At(p.Pos)) * dt;
			next.X = Math.Clamp(next.X, 10, TW * TS - 10); next.Y = Math.Clamp(next.Y, 10, TH * TS - 10);
			if (Passable(At(next))) p.Pos = next;
			else p.Target = PickWaypoint(p);                              // 撞山换道
		}

		// —— 官军×虏骑:相遇即接战(锁住双方,开一场限时野战)——
		for (int i = 0; i < gs.Parties.Count; i++)
			for (int j = 0; j < gs.Parties.Count; j++)
			{
				var a = gs.Parties[i]; var b = gs.Parties[j];
				if (a.Kind != 1 || b.Kind != 0) continue;                 // A=官军 B=虏骑
				if (a.State != 0 || b.State != 0) continue;
				if (a.Pos.DistanceTo(b.Pos) > 30f) continue;
				var ground = CommandPost.Core.WorldGen.GroundOf(At((a.Pos + b.Pos) / 2f));
				gs.Clashes.Add(new GameState.WorldClash
				{ A = i, B = j, Sim = CommandPost.Core.AutoBattle.Start(a.Men, a.Qual, b.Men, b.Qual, ground) });
				a.State = 1; b.State = 1;
				_banner = $"望楼旗语:官军巡骑与虏骑接战于{CommandPost.Core.WorldGen.GroundCn(ground)}——烟尘蔽日!";
				_bannerAge = 0; Sfx.Play(this, Sfx.Alert, -10f);
			}

		// —— 酣战推进(限时:规模越大打得越久;兵力边打边掉——你赶到时它是什么就是什么)——
		for (int k = gs.Clashes.Count - 1; k >= 0; k--)
		{
			var c = gs.Clashes[k];
			var a = gs.Parties[c.A]; var b = gs.Parties[c.B];
			c.Sim.Step(dt, _worldRng);
			a.Men = c.Sim.MenA; b.Men = c.Sim.MenB;
			if (!c.Sim.Over) continue;
			gs.Clashes.RemoveAt(k);
			if (c.Sim.Winner is not { } w)
			{
				a.State = 2; b.State = 2;
				_banner = "野战两败俱伤,双方各自曳兵而走。"; _bannerAge = 0;
				continue;
			}
			var win = w == CommandPost.Core.Side.Friend ? a : b;
			var lose = w == CommandPost.Core.Side.Friend ? b : a;
			win.State = 0; win.Target = Vector2.Zero;
			lose.State = lose.Men < 60 ? 3 : 2;
			_banner = $"野战分晓(打了{(int)(c.Sim.Hours + 0.99f)}个时辰):{win.NameCn}击溃{lose.NameCn}" +
					  (lose.State == 3 ? "——败者就此散尽。" : "——残部曳兵遁走。");
			_bannerAge = 0;
		}

		// —— 边野不空场:散尽的过阵子有新队伍开出来(虏自虏帐,官军自行营)——
		_respawnRaider -= dt;
		if (_respawnRaider <= 0f)
		{
			_respawnRaider = 30f + _evRng.Next(26);
			Revive(0, GameState.FoeHome, 240 + _evRng.Next(180), 0.9f);
		}
		_respawnPatrol -= dt;
		if (_respawnPatrol <= 0f)
		{
			_respawnPatrol = 42f + _evRng.Next(20);
			Revive(1, gs.ArmyPos, 220 + _evRng.Next(140), 1.0f);
		}
	}

	/// <summary>游弋航点:劫掠队扑官道沿线与边镇的富庶处;官军沿官道与渡口巡边。</summary>
	private Vector2 PickWaypoint(GameState.WorldParty p)
	{
		if (p.Kind == 0)
		{
			var picks = new[]
			{
				new Vector2(31 * TS, 104 * TS),                                            // 边镇(有的抢)
				new Vector2((90 + _evRng.Next(70)) * TS, (58 + _evRng.Next(14)) * TS),     // 官道沿线
				new Vector2(95 * TS, 97 * TS),                                             // 南渡滩
				new Vector2((150 + _evRng.Next(30)) * TS, (36 + _evRng.Next(56)) * TS),    // 岭东野地
			};
			return picks[_evRng.Next(picks.Length)];
		}
		var pk = new[]
		{
			new Vector2(30 * TS, 64 * TS), new Vector2(60 * TS, 66 * TS),
			new Vector2(88 * TS, 64 * TS), new Vector2(52 * TS, 80 * TS),
		};
		return pk[_evRng.Next(pk.Length)] + new Vector2(_evRng.Next(-40, 41), _evRng.Next(-40, 41));
	}

	/// <summary>补一支队伍上图:优先复用散尽的空位;同类活队足两支则不补。</summary>
	private void Revive(int kind, Vector2 at, int men, float qual)
	{
		var gs = GameState.I;
		int alive = 0; GameState.WorldParty? slot = null;
		foreach (var p in gs.Parties)
		{
			if (p.Kind != kind) continue;
			if (p.State == 3) slot ??= p; else alive++;
		}
		if (alive >= 2) return;
		if (slot == null)
		{
			if (gs.Parties.Count >= 10) return;
			slot = new GameState.WorldParty { Kind = kind };
			gs.Parties.Add(slot);
		}
		slot.Pos = at + new Vector2(_evRng.Next(-60, 61), _evRng.Next(-60, 61));
		slot.Men = men; slot.Qual = qual; slot.State = 0; slot.Target = Vector2.Zero;
	}

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
					7 => new Color("46404a"), 8 => new Color("6e5b41"), 9 => new Color("39283f"),
					10 => new Color("4d4836"), 11 => new Color("2c4136"),
					_ => new Color("4a5232")
				};
				if (((x + y) & 1) == 0) c = c.Darkened(0.05f);
				// 高度浮雕:热力图打明暗——山脊承光,洼地沉影(地形一眼可读)
				float relief = (_height[x, y] - 0.5f) * 0.22f;
				c = relief >= 0 ? c.Lightened(relief) : c.Darkened(-relief);
				if (_t[x, y] == 9 && ((x * 7 + y * 13) % 11) == 0) c = c.Lightened(0.07f);   // 瘴土磷光斑
				if (_t[x, y] == 11 && ((x * 5 + y * 11) % 13) == 0) c = c.Lightened(0.05f);  // 泽地水洼
				DrawRect(new Rect2(x * TS, y * TS, TS, TS), c, true);
			}

		// 地标(帅帐不再是固定营盘——中军在行军的大队里)
		DrawLabel(new Vector2(19 * TS, 57 * TS), "出师大营(旧盘)", new Color("8a8474"));
		DrawLabel(new Vector2(55 * TS, 57 * TS), "戍垒", new Color("9a948a"));
		DrawLabel(new Vector2(129 * TS, 20 * TS), "黑 松 岭", new Color("9fbf8a"));
		DrawLabel(new Vector2(95 * TS, 58 * TS), "涧水渡", new Color("8ab4c8"));
		DrawLabel(new Vector2(184 * TS, 55 * TS), "虏帐", new Color("bcd2ec"));
		DrawLabel(new Vector2(31 * TS, 97 * TS), "边镇", new Color("c8bfa8"));

		// 当面之敌(扼守岭东):兵形五十抽一——望之知其众寡
		if (!GameState.I.EnemyDefeated)
		{
			bool foeMoving = (_enemy - EnemyPost).Length() > 24f;
			if (foeMoving) DrawDust(_enemy, (_pos.X < _enemy.X) ? 1f : -1f);
			DrawTroops(_enemy, FoeHostMen, new Color(0.09f, 0.09f, 0.12f), _pos.X < _enemy.X ? -1f : 1f, foeMoving);
			DrawLabel(_enemy + new Vector2(0, -26), "当面之敌", new Color("bcd2ec"));
		}

		// 行营(周崇大军驻地):谒见、领粮之处——大军自然是黑压压一片
		{
			var ap = GameState.I.ArmyPos;
			DrawTroops(ap, 2400, new Color(0.34f, 0.14f, 0.11f), 1f, moving: false);
			DrawFlag(ap + new Vector2(12, -10), new Color("e6c25c"));
			DrawFlag(ap + new Vector2(-52, -6), new Color("a8352a"));
			DrawLabel(ap + new Vector2(0, -44), "行营 · 周崇", new Color("e6c25c"));
			if (NearHq)
				DrawLabel(ap + new Vector2(0, 48), "E = 谒见行营", new Color("f2e6c8"));
		}

		// —— 边野活物:虏骑劫掠队 / 官军巡骑(它们各过各的日子)——
		foreach (var p in GameState.I.Parties)
		{
			if (p.State == 3) continue;
			float pfx = p.Target != Vector2.Zero && p.Target.X < p.Pos.X ? -1f : 1f;
			bool pmov = p.State != 1 && p.Target != Vector2.Zero && p.Pos.DistanceTo(p.Target) > 26f;
			if (pmov) DrawDust(p.Pos, pfx);
			DrawTroops(p.Pos, p.Men, p.Hostile ? new Color(0.09f, 0.09f, 0.12f) : new Color(0.30f, 0.13f, 0.10f), pfx, pmov);
			DrawLabel(p.Pos + new Vector2(0, -28), $"{p.NameCn} · 约{Mathf.Max(1, (p.Men + 50) / 100)}百",
				p.Hostile ? new Color("bcd2ec") : new Color("e8c9a0"));
		}
		// 酣战尘团:两军绞在一处——大仗要打上好几个时辰,赶得上就分得了赃(或救得了人)
		foreach (var c in GameState.I.Clashes)
		{
			var mid = (GameState.I.Parties[c.A].Pos + GameState.I.Parties[c.B].Pos) / 2f;
			for (int i = 0; i < 5; i++)
			{
				float ph = ((float)_t0 * 0.9f + i * 0.2f) % 1f;
				float ang = i * 1.26f + (float)_t0 * 1.7f;
				DrawCircle(mid + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (6 + ph * 14), 4f + ph * 7f,
					new Color(0.6f, 0.55f, 0.45f, 0.3f * (1 - ph)));
			}
			DrawLabel(mid + new Vector2(0, -46), $"酣战 · 已{(int)(c.Sim.Hours + 0.99f)}时辰", new Color("d9917a"));
		}

		DrawLabel(new Vector2(182 * TS, 112 * TS), "混沌瘴土", new Color(0.62f, 0.45f, 0.68f, 0.85f));

		// 我方行军纵队(兵形五十抽一 + 牙旗;将军立绘只在中军帐/营区)
		float fx = _faceLeft ? -1f : 1f;
		if (_moving) DrawDust(_pos, fx);
		DrawTroops(_pos, GameState.I.OwnTotal, new Color(0.42f, 0.16f, 0.12f), fx, _moving);
		DrawLine(_pos + new Vector2(2 * fx, -4), _pos + new Vector2(2 * fx, -26), new Color("c8b088"), 2f);
		DrawColoredPolygon(new[] { _pos + new Vector2(2 * fx, -26), _pos + new Vector2(2 * fx + 15 * fx, -21.5f), _pos + new Vector2(2 * fx, -17) }, new Color("d9b34a"));
		DrawLabel(_pos + new Vector2(0, -36), $"本部 · {GameState.I.OwnTotal}", new Color("ffd9a0"));

		if (_moveTarget is { } t2)
			DrawArc(t2, 7f, 0, Mathf.Tau, 16, new Color(1, 1, 1, 0.5f), 1.5f);

		DrawSetTransform(Vector2.Zero, 0, Vector2.One);

		// —— 昼夜:夜行野暗(酉末入夜、卯初天明)——
		if (NightFactor > 0.01f)
			DrawRect(new Rect2(0, 0, 1120, 760), new Color(0.03f, 0.05f, 0.13f, 0.42f * NightFactor), true);

		// —— HUD 层 ——
		DrawString(_font, new Vector2(14, 24),
			$"大酆边野 · {CalendarCn} · 本部 {GameState.I.OwnTotal}/{GameState.OwnFullTotal} · 主帅信任 {GameState.I.Trust}",
			HorizontalAlignment.Left, -1, 16, new Color("e8e0d0"));
		{
			var gs = GameState.I;
			float left = gs.MissionDeadline - gs.CampaignHours;
			string line = gs.EnemyDefeated
				? "虏已破,边野暂靖——尔部自便。(WASD行军 空格观望 C扎营 R歇营 · 行营可领粮补员)"
				: gs.BattlesFought > 0
					? "皇命未竟,虏犹在岭东——何时再战,尔自斟酌。(WASD行军 空格观望 C扎营 R歇营)"
					: $"皇命:侦破黑松岭当面之虏{(gs.MissionDeadline < 0 ? "" : left > 0 ? $"——限期余 {(int)left} 时辰" : "——限期已过!")} | WASD行军 空格观望 C扎营 R歇营";
			DrawString(_font, new Vector2(14, 44), line + " · Esc=菜单", HorizontalAlignment.Left, -1, 12,
				!gs.EnemyDefeated && gs.BattlesFought == 0 && left < 12f && gs.MissionDeadline > 0 ? new Color("d9917a") : new Color("9aa0a8"));
		}

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
			DrawString(_font, new Vector2(318, 66), "心神", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
			DrawRect(new Rect2(354, 56, 96, 9), new Color(0.15f, 0.14f, 0.11f, 0.85f), true);
			DrawRect(new Rect2(354, 56, 96 * gs.San / 100f, 9),
				gs.San >= 60 ? new Color("8fa8a0") : gs.San >= 35 ? new Color("b08ab0") : new Color("9a5a9a"), true);
		}
		DrawMinimap();

		if (_banner != "" && _bannerAge < 4)
			DrawString(_font, new Vector2(360, 90), _banner, HorizontalAlignment.Left, -1, 14, new Color("f2e6c8"));

		DrawLetter();
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
			L("征虏皇命", 20, "e6c25c"); y += 6;
			L("敕曰:虏骑犯我北鄙,现屯黑松岭以东,众寡未详。", 14, "c8c2b4");
			L("命尔部即日进兵,虏情务须侦明,军书具报,相机破之。", 14, "c8c2b4");
			L("限三日。军饷粮草如额拨付,毋掠民,毋纵虏。", 14, "c8c2b4"); y += 8;
			if (GameState.I.MissionDeadline > 0)
				L($"——限期:第 {(int)(GameState.I.MissionDeadline / 24f) + 1} 日前。逾期行营催令,主帅信任日削。", 12, "9aa0a8");
			L("——如何用兵、何时接战、走哪条路,悉听尔便。", 12, "9aa0a8");
		}
		else
		{
			L("周帅嘉勉", 20, "e6c25c"); y += 6;
			L($"「黑松岭之捷,行营已录。今主帅信任 {GameState.I.Trust}。」", 14, "d8d2c4");
			if (GameState.I.LastVerdict is { } v) L($"上战裁断:「{v.VerdictCn}」", 14, "c8c2b4");
			L("「边野之事,尔自斟酌——朝廷的眼睛,始终看着。」", 14, "c8c2b4"); y += 8;
			L("——领粮补员照旧;此后去哪、做什么,是你自己的事了。", 12, "9aa0a8");
		}
		DrawString(_font, new Vector2(x, box.End.Y - 30), "E / 回车 · 领命", HorizontalAlignment.Left, -1, 13, new Color("e6c25c"));
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
					8 => new Color("6e5b41"), 9 => new Color("4a3555"),
					10 => new Color("4d4836"), 11 => new Color("2c4136"),
					_ => new Color("3c412a")
				};
				float rel = (_height[x, y] - 0.5f) * 0.2f;
				c = rel >= 0 ? c.Lightened(rel) : c.Darkened(-rel);
				DrawRect(new Rect2(org.X + x * TS * MS, org.Y + y * TS * MS, TS * MS * 3, TS * MS * 3), c, true);
			}
		DrawRect(new Rect2(org + GameState.I.ArmyPos * MS - new Vector2(2.5f, 2.5f), new Vector2(5, 5)), new Color("d9b34a"), true);
		if (!GameState.I.EnemyDefeated) DrawCircle(org + _enemy * MS, 3f, new Color(0.1f, 0.1f, 0.12f));
		foreach (var p in GameState.I.Parties)
			if (p.State != 3)
				DrawCircle(org + p.Pos * MS, 2.2f, p.Hostile ? new Color(0.12f, 0.12f, 0.16f) : new Color(0.55f, 0.24f, 0.18f));
		foreach (var c in GameState.I.Clashes)
		{
			var mid = (GameState.I.Parties[c.A].Pos + GameState.I.Parties[c.B].Pos) / 2f;
			float pulse = 1.5f + Mathf.Sin((float)_t0 * 6f) * 1.2f;
			DrawCircle(org + mid * MS, 2f + pulse, new Color(0.85f, 0.4f, 0.3f, 0.55f));
		}
		DrawGeneralMark(org + _pos * MS);                                 // 小图上的你=将军本人(大图上是队伍)
	}

	/// <summary>当面之敌的号称兵力(黑松岭一路;望之知势,细数得靠塘骑)。</summary>
	private const int FoeHostMen = 1000;

	/// <summary>
	/// 一支队伍的兵形(五十抽一,封顶三十人):三人一列的行军纵队——
	/// 大图见军势不见全军;人多则纵队拖得长,望一眼便知是股大队还是游哨。
	/// </summary>
	private void DrawTroops(Vector2 pos, int men, Color coat, float fx, bool moving)
	{
		int figs = Math.Clamp((men + 49) / 50, 1, 30);
		for (int i = figs - 1; i >= 0; i--)
		{
			int row = i / 3, col = i % 3;
			float jx = ((i * 37) % 5 - 2) * 0.8f, jy = ((i * 53) % 5 - 2) * 0.9f;
			float bob = moving ? Mathf.Sin((float)_t0 * 7f + i * 1.7f) * 0.9f : 0f;
			var p = pos + new Vector2(-(row * 6.8f + (col - 1) * 1.2f) * fx + jx, (col - 1) * 5.6f + jy + bob);
			DrawCircle(p, 2.4f, coat.Darkened((i * 29 % 4) * 0.05f));                          // 甲身
			DrawCircle(p + new Vector2(0, -2.4f), 1.1f, new Color("c9a684").Darkened((i * 13 % 3) * 0.08f));   // 兜鍪下的脸
		}
	}

	/// <summary>行军扬尘(队尾方向)。</summary>
	private void DrawDust(Vector2 pos, float fx)
	{
		for (int i = 0; i < 3; i++)
		{
			float ph = ((float)_t0 * 0.8f + i * 0.33f) % 1f;
			DrawCircle(pos + new Vector2((-10 - ph * 14) * fx, -1 - ph * 7), 2.5f + ph * 4.5f,
				new Color(0.62f, 0.58f, 0.5f, 0.3f * (1 - ph)));
		}
	}

	/// <summary>小地图上的将军小像(盔缨+金圈):小图见将,大图见军——你既是一杆旗,也是一个人。</summary>
	private void DrawGeneralMark(Vector2 p)
	{
		DrawCircle(p, 4.4f, new Color(0f, 0f, 0f, 0.55f));                                                       // 衬底
		DrawRect(new Rect2(p + new Vector2(-1.6f, -0.5f), new Vector2(3.2f, 3.6f)), new Color("7a2a20"), true);  // 甲身
		DrawCircle(p + new Vector2(0, -1.8f), 1.7f, new Color("d8b090"));                                        // 面
		DrawRect(new Rect2(p + new Vector2(-2.0f, -4.0f), new Vector2(4.0f, 1.4f)), new Color("6b3a28"), true);  // 盔檐
		DrawLine(p + new Vector2(0, -4.0f), p + new Vector2(0, -6.2f), new Color("c4453a"), 1.4f);               // 红缨
		DrawArc(p, 4.4f, 0, Mathf.Tau, 14, new Color("e6c25c"), 1.2f);                                           // 金圈定位
	}

	private void DrawFlag(Vector2 p, Color c)
	{
		DrawLine(p, p + new Vector2(0, -24), new Color("c8b088"), 2f);
		DrawColoredPolygon(new[] { p + new Vector2(0, -24), p + new Vector2(17, -18.5f), p + new Vector2(0, -13) }, c);
	}

	private void DrawLabel(Vector2 p, string s, Color c)
		=> DrawString(_font, p + new Vector2(-70, 0), s, HorizontalAlignment.Center, 140, 13, c);
}
