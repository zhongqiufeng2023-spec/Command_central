using Godot;
using System;

/// <summary>
/// 骑砍式像素大地图:大酆边镇一隅。
/// 你的仪仗(将军旗队)在野外行进(WASD/方向键,或点击行军);虏骑游队在黑松岭以东游弋,
/// 靠近即遭遇 → 入中军帐备战;C = 就地扎营(无战事也可进帐练习)。
/// 战略层完整版(任务/补给/多队)见蓝图;此为骑砍式实时大地图的第一块可玩切片。
/// </summary>
public partial class OverworldRoot : Node2D
{
	private const int TW = 70, TH = 46;          // 瓦片数
	private const float TS = 16f;                // 瓦片像素
	// 地表:0草 1林 2河 3滩 4官道 5山 6我营 7虏帐
	private readonly byte[,] _t = new byte[TW, TH];

	private Vector2 _pos;                        // 我方仪仗位置(像素)
	private Vector2? _moveTarget;                // 点击行军目标
	private GeneralSprite.Dir _dir = GeneralSprite.Dir.Right;
	private float _phase;
	private bool _moving;

	private Vector2 _enemy;
	private float _enemyPhase;
	private double _t0;
	private string _banner = ""; private double _bannerAge = 99;
	private Font _font = null!;

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.Nearest;
		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		_font = sf;

		BuildMap();
		_pos = GameState.I.PartyPos;
		_enemy = GameState.I.EnemyPos;
		GetWindow().GrabFocus();
	}

	private void BuildMap()
	{
		// 草原打底 + 北缘群山
		for (int x = 0; x < TW; x++)
			for (int y = 0; y < TH; y++)
				_t[x, y] = (byte)(y < 3 ? 5 : 0);
		// 黑松岭:中部大林带(锯齿)
		for (int y = 4; y <= 30; y++)
		{
			int wob = (y * 13) % 5;
			for (int x = 30 - wob / 2; x <= 39 + (y * 7) % 4; x++) Set(x, y, 1);
		}
		// 河:南部横贯 + 两滩
		for (int x = 0; x < TW; x++) { Set(x, 36, 2); Set(x, 37, 2); }
		Set(16, 36, 3); Set(16, 37, 3); Set(17, 36, 3); Set(17, 37, 3);
		Set(50, 36, 3); Set(50, 37, 3); Set(51, 36, 3); Set(51, 37, 3);
		// 官道:东西向,穿林
		for (int x = 0; x < TW; x++) { Set(x, 25, 4); Set(x, 26, 4); }
		// 零星小林与丘
		Paint(8, 8, 12, 11, 1); Paint(52, 10, 58, 14, 1); Paint(12, 30, 15, 33, 1);
		// 我营(西)与虏帐(东)
		Paint(9, 24, 12, 27, 6); Paint(58, 16, 61, 19, 7);
	}
	private void Set(int x, int y, byte v) { if (x >= 0 && x < TW && y >= 0 && y < TH) _t[x, y] = v; }
	private void Paint(int x0, int y0, int x1, int y1, byte v)
	{ for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) Set(x, y, v); }

	private byte At(Vector2 p)
	{
		int x = Math.Clamp((int)(p.X / TS), 0, TW - 1), y = Math.Clamp((int)(p.Y / TS), 0, TH - 1);
		return _t[x, y];
	}
	private static bool Passable(byte t) => t != 2 && t != 5;               // 河与山不可行(滩可涉)
	private static float SpeedMult(byte t) => t switch { 1 => 0.55f, 3 => 0.45f, 4 => 1.45f, _ => 1f };

	public override void _Process(double delta)
	{
		_t0 += delta; _bannerAge += delta;
		float dt = (float)delta;

		// —— 我方行进:键盘优先,否则点击目标 ——
		var dir = Vector2.Zero;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
		if (dir != Vector2.Zero) _moveTarget = null;
		else if (_moveTarget is { } tgt)
		{
			var d = tgt - _pos;
			if (d.Length() < 4f) _moveTarget = null; else dir = d.Normalized();
		}

		_moving = dir != Vector2.Zero;
		if (_moving)
		{
			float speed = 95f * SpeedMult(At(_pos));
			var next = _pos + dir.Normalized() * speed * dt;
			next.X = Math.Clamp(next.X, 8, TW * TS - 8); next.Y = Math.Clamp(next.Y, 8, TH * TS - 8);
			if (Passable(At(next))) _pos = next;
			else
			{
				var slideX = new Vector2(next.X, _pos.Y);
				var slideY = new Vector2(_pos.X, next.Y);
				if (Passable(At(slideX))) _pos = slideX;
				else if (Passable(At(slideY))) _pos = slideY;
			}
			_phase = (_phase + dt * 1.6f) % 1f;
			_dir = Mathf.Abs(dir.X) >= Mathf.Abs(dir.Y)
				? (dir.X >= 0 ? GeneralSprite.Dir.Right : GeneralSprite.Dir.Left)
				: (dir.Y >= 0 ? GeneralSprite.Dir.Down : GeneralSprite.Dir.Up);
		}

		// —— 虏骑游队:黑松岭以东游弋;见我则追 ——
		if (!GameState.I.EnemyDefeated)
		{
			_enemyPhase += dt;
			var toMe = _pos - _enemy;
			Vector2 edir;
			if (toMe.Length() < 210f) edir = toMe.Normalized() * 0.92f;                     // 追击
			else edir = new Vector2(Mathf.Cos(_enemyPhase * 0.35f), Mathf.Sin(_enemyPhase * 0.5f)) * 0.5f;   // 游弋
			var enext = _enemy + edir * 78f * dt;
			if (Passable(At(enext))) _enemy = enext;

			if (toMe.Length() < 26f)                                                        // 遭遇!
			{
				GameState.I.PartyPos = _pos; GameState.I.EnemyPos = _enemy;
				GameState.I.StartBattle();
				GameState.Go(this, "res://Tent.tscn");
				return;
			}
		}

		QueueRedraw();
	}

	public override void _Input(InputEvent e)
	{
		if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
			_moveTarget = mb.Position;
		else if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == Key.C)
		{
			GameState.I.PartyPos = _pos; GameState.I.EnemyPos = _enemy;
			GameState.I.CampOnly = true; GameState.I.Battle = null;
			GameState.Go(this, "res://Tent.tscn");
		}
	}

	public override void _Draw()
	{
		// 地表
		for (int x = 0; x < TW; x++)
			for (int y = 0; y < TH; y++)
			{
				Color c = _t[x, y] switch
				{
					1 => new Color("2e4023"), 2 => new Color("2d4d5e"), 3 => new Color("3e6172"),
					4 => new Color("6b5b3e"), 5 => new Color("55504a"), 6 => new Color("5a4632"),
					7 => new Color("46404a"), _ => new Color("4a5232")
				};
				// 轻微棋盘抖动,像素质感
				if (((x + y) & 1) == 0) c = c.Darkened(0.05f);
				DrawRect(new Rect2(x * TS, y * TS, TS, TS), c, true);
			}

		// 地标
		DrawLabel(new Vector2(10.5f * TS, 23f * TS), "大酆·前锋营", new Color("ffd9a0"));
		DrawLabel(new Vector2(34f * TS, 6f * TS), "黑 松 岭", new Color("9fbf8a"));
		DrawLabel(new Vector2(59.5f * TS, 15f * TS), "虏帐", new Color("bcd2ec"));
		DrawLabel(new Vector2(33f * TS, 38.5f * TS), "涧水", new Color("8ab4c8"));

		// 我营旗
		DrawFlag(new Vector2(10.5f * TS, 25.5f * TS), new Color("d9b34a"));

		// 虏骑游队(黑骑影 + 扬尘)
		if (!GameState.I.EnemyDefeated)
		{
			DrawCircle(_enemy, 7f, new Color(0.08f, 0.08f, 0.1f));
			DrawCircle(_enemy + new Vector2(6, -3), 4.5f, new Color(0.08f, 0.08f, 0.1f));
			for (int i = 0; i < 3; i++)
			{
				float ph = ((float)_t0 * 0.7f + i * 0.33f) % 1f;
				DrawCircle(_enemy + new Vector2(-8 - ph * 14, -2 - ph * 8), 3f + ph * 5f,
					new Color(0.6f, 0.56f, 0.48f, 0.35f * (1 - ph)));
			}
			DrawLabel(_enemy + new Vector2(0, -18), "虏骑", new Color("bcd2ec"));
		}

		// 我方仪仗:将军立绘 + 牙旗
		var top = _pos + new Vector2(-14, -52);
		GeneralSprite.Draw(this, top, 28, 56, _dir, _phase, _moving);
		DrawLine(_pos + new Vector2(12, -50), _pos + new Vector2(12, -30), new Color("c8b088"), 2f);
		DrawColoredPolygon(new[] { _pos + new Vector2(12, -50), _pos + new Vector2(26, -46), _pos + new Vector2(12, -41) }, new Color("b03a2e"));

		if (_moveTarget is { } t2)
			DrawArc(t2, 6f, 0, Mathf.Tau, 16, new Color(1, 1, 1, 0.5f), 1.5f);

		// HUD
		DrawString(_font, new Vector2(14, 24), "大酆边野 · 行军", HorizontalAlignment.Left, -1, 16, new Color("e8e0d0"));
		DrawString(_font, new Vector2(14, 44),
			GameState.I.EnemyDefeated
				? "虏骑已绝迹于野。C=扎营入帐 · WASD/点击=行军"
				: "WASD/点击=行军 · C=扎营入帐 | 虏骑游弋于黑松岭以东——迎上去,或诱其来攻",
			HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));

		if (_banner != "" && _bannerAge < 4)
			DrawString(_font, new Vector2(360, 80), _banner, HorizontalAlignment.Left, -1, 14, new Color("f2e6c8"));
	}

	private void DrawFlag(Vector2 p, Color c)
	{
		DrawLine(p, p + new Vector2(0, -22), new Color("c8b088"), 2f);
		DrawColoredPolygon(new[] { p + new Vector2(0, -22), p + new Vector2(16, -17), p + new Vector2(0, -12) }, c);
	}

	private void DrawLabel(Vector2 p, string s, Color c)
		=> DrawString(_font, p + new Vector2(-60, 0), s, HorizontalAlignment.Center, 120, 12, c);
}
