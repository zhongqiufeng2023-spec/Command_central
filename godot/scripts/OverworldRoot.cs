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
			float speed = 130f * SpeedMult(At(_pos));
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
		}

		// —— 当面之敌:扼守黑松岭东缘;你进抵岭一线(靠近)即出而接敌 ——
		if (!GameState.I.EnemyDefeated)
		{
			var toMe = _pos - _enemy;
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
				GameState.Go(this, "res://Tent.tscn");
				return;
			}
		}

		QueueRedraw();
	}

	public override void _Input(InputEvent e)
	{
		if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
			_moveTarget = mb.Position - CamOffset();
		else if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == Key.C)
		{
			GameState.I.PartyPos = _pos; GameState.I.EnemyPos = _enemy;
			GameState.I.CampOnly = true; GameState.I.Battle = null;
			GameState.Go(this, "res://Tent.tscn");
		}
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

		DrawSetTransform(Vector2.Zero, 0, Vector2.One);

		// —— HUD 层 ——
		DrawString(_font, new Vector2(14, 24), "大酆边野 · 行军", HorizontalAlignment.Left, -1, 16, new Color("e8e0d0"));
		DrawString(_font, new Vector2(14, 44),
			GameState.I.EnemyDefeated
				? "虏骑已绝迹于野。C=扎营入帐 · WASD/点击=行军"
				: "军令:进抵黑松岭一线,试探当面之敌 | WASD/点击=行军 · C=扎营入帐(先扎营再推进,可用瞭望台)",
			HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
		DrawMinimap();

		if (_banner != "" && _bannerAge < 4)
			DrawString(_font, new Vector2(360, 90), _banner, HorizontalAlignment.Left, -1, 14, new Color("f2e6c8"));
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
