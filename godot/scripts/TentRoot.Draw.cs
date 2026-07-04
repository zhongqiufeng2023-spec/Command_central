using Godot;
using CommandPost.Core;
using System;
using Side = CommandPost.Core.Side;

// 中军帐绘制层:帐内 / 营区 / 瞭望台观察(黑点与烟雾)。全部程序化像素风,无外部素材(将军立绘除外)。
public partial class TentRoot
{
	public override void _Draw()
	{
		switch (_area)
		{
			case Area.Inside: DrawInside(); break;
			case Area.Outside: DrawOutside(); break;
			case Area.Tower: DrawTowerView(); return;   // 观察视野是全屏,不画将军
		}

		// 将军(帐内/营区)
		GeneralSprite.Draw(this, _pos + new Vector2(-22, -84), 44, 88, _dir, _phase, _moving);

		DrawHudCommon();
	}

	// ====================================================================
	//  帐内
	// ====================================================================

	private void DrawInside()
	{
		DrawRect(new Rect2(0, 0, 1120, 760), new Color("1d1a14"), true);

		// 帐幕外沿(暗红)与内壁
		DrawRect(InsideBounds.Grow(34), new Color("4a2620"), true);
		DrawRect(InsideBounds.Grow(14), new Color("6b3a2e"), true);
		// 地面(夯土 + 席)
		DrawChecker(InsideBounds, new Color("6e5b41"), new Color("685640"), 20);
		DrawRect(new Rect2(380, 300, 200, 200), new Color("8a3b30"), true);          // 红毡
		DrawRect(new Rect2(388, 308, 184, 184), new Color("7d352b"), true);

		// 沙盘台:案 + 微缩地形(小小的黑松岭)
		DrawRect(SandTable.Grow(8), new Color("3a2c1c"), true);
		DrawRect(SandTable, new Color("54422a"), true);
		DrawRect(new Rect2(636, 314, 208, 122), new Color("4a5232"), true);          // 微缩草原
		DrawRect(new Rect2(700, 320, 44, 80), new Color("2e4023"), true);            // 微缩林带
		DrawRect(new Rect2(640, 408, 200, 10), new Color("2d4d5e"), true);           // 微缩河
		for (int i = 0; i < 4; i++)
			DrawRect(new Rect2(660 + i * 18, 350, 8, 8), new Color("b03a2e"), true); // 小旗子
		DrawLabel(new Vector2(SandTable.GetCenter().X, 288), "沙 盘", new Color("e6c25c"));

		// 火盆(跳动的火光)
		DrawRect(Brazier, new Color("3a3a3a"), true);
		float f = 4f + 3f * Mathf.Sin((float)_t * 7f);
		DrawCircle(Brazier.GetCenter() + new Vector2(0, -6), 8 + f * 0.5f, new Color(0.95f, 0.55f, 0.2f, 0.8f));
		DrawCircle(Brazier.GetCenter() + new Vector2(0, -10), 4 + f * 0.3f, new Color(1f, 0.8f, 0.4f, 0.9f));

		// 帅旗「酆」与案几
		DrawRect(new Rect2(250, 160, 90, 130), new Color("7d352b"), true);
		DrawString(_font, new Vector2(266, 240), "酆", HorizontalAlignment.Left, -1, 52, new Color("e6c25c"));
		DrawRect(new Rect2(700, 170, 170, 56), new Color("3a2c1c"), true);           // 军令案
		DrawRect(new Rect2(714, 182, 60, 32), new Color("d8d2c4"), true);            // 文书
		DrawRect(new Rect2(790, 180, 8, 36), new Color("c8b088"), true);             // 令箭

		// 帐门(南,掀开的口)
		DrawRect(DoorInside.Grow(6), new Color("2a1a14"), true);
		DrawLabel(new Vector2(DoorInside.GetCenter().X, DoorInside.Position.Y - 8), "▼ 帐外", new Color("9aa0a8"));

		if (NearSandTable)
			DrawHint(SandTable.GetCenter() + new Vector2(0, -100),
				GameState.I.Battle != null ? "E · 入沙盘推演" : "E · 看沙盘(无战事)");
	}

	// ====================================================================
	//  营区
	// ====================================================================

	private void DrawOutside()
	{
		DrawRect(new Rect2(0, 0, 1120, 760), new Color("15130f"), true);
		DrawChecker(OutsideBounds, new Color("5a5238"), new Color("554d35"), 24);    // 营地夯土

		// 栅栏(木桩)
		for (float x = OutsideBounds.Position.X; x <= OutsideBounds.End.X; x += 18)
		{
			DrawRect(new Rect2(x, OutsideBounds.Position.Y - 14, 8, 18), new Color("4a3a26"), true);
			DrawRect(new Rect2(x, OutsideBounds.End.Y - 4, 8, 18), new Color("4a3a26"), true);
		}
		for (float y = OutsideBounds.Position.Y; y <= OutsideBounds.End.Y; y += 18)
		{
			DrawRect(new Rect2(OutsideBounds.Position.X - 14, y, 18, 8), new Color("4a3a26"), true);
			DrawRect(new Rect2(OutsideBounds.End.X - 4, y, 18, 8), new Color("4a3a26"), true);
		}

		// 中军大帐(北,可回)
		DrawTentShape(TentBig, new Color("7d352b"), big: true);
		DrawLabel(new Vector2(TentBig.GetCenter().X, TentBig.End.Y + 16), "▲ 中军帐", new Color("9aa0a8"));

		// 士卒营帐
		foreach (var t in OutsideTents) DrawTentShape(t, new Color("6e5b41"), big: false);

		// 篝火两处
		DrawCampfire(new Vector2(420, 360));
		DrawCampfire(new Vector2(640, 500));

		// 瞭望台:四腿 + 台面 + 梯
		var tb = TowerBase;
		DrawRect(new Rect2(tb.Position.X + 4, tb.Position.Y - 60, 10, tb.Size.Y + 60), new Color("4a3a26"), true);
		DrawRect(new Rect2(tb.End.X - 14, tb.Position.Y - 60, 10, tb.Size.Y + 60), new Color("4a3a26"), true);
		DrawRect(new Rect2(tb.Position.X - 6, tb.Position.Y - 78, tb.Size.X + 12, 26), new Color("5a4632"), true);
		DrawRect(new Rect2(tb.Position.X, tb.Position.Y - 56, tb.Size.X, 8), new Color("4a3a26"), true);
		for (int i = 0; i < 5; i++)                                                    // 梯级
			DrawRect(new Rect2(tb.GetCenter().X - 8, tb.Position.Y - 40 + i * 18, 16, 5), new Color("6b5b3e"), true);
		DrawLabel(new Vector2(tb.GetCenter().X, tb.Position.Y - 90), "瞭望台", new Color("e6c25c"));

		// 辕门(南)
		DrawRect(GateSouth.Grow(8), new Color("3a2c1c"), true);
		DrawLabel(new Vector2(GateSouth.GetCenter().X, GateSouth.Position.Y - 8), "▼ 拔营上路", new Color("9aa0a8"));

		if (NearTower) DrawHint(tb.GetCenter() + new Vector2(0, -110), "E · 登瞭望台");
	}

	private void DrawTentShape(Rect2 r, Color c, bool big)
	{
		var top = new Vector2(r.GetCenter().X, r.Position.Y - (big ? 34 : 20));
		DrawColoredPolygon(new[] { top, new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.End.Y) }, c);
		DrawColoredPolygon(new[] { top, new Vector2(r.GetCenter().X - (big ? 30 : 18), r.End.Y),
			new Vector2(r.GetCenter().X + (big ? 30 : 18), r.End.Y) }, c.Darkened(0.25f));
		if (big)
		{
			DrawLine(top, top + new Vector2(0, -18), new Color("c8b088"), 2f);
			DrawColoredPolygon(new[] { top + new Vector2(0, -18), top + new Vector2(14, -13), top + new Vector2(0, -8) }, new Color("d9b34a"));
		}
	}

	private void DrawCampfire(Vector2 p)
	{
		DrawCircle(p, 9f, new Color("3a3a3a"));
		float f = 3f + 2.5f * Mathf.Sin((float)_t * 6f + p.X);
		DrawCircle(p + new Vector2(0, -4), 5 + f * 0.5f, new Color(0.95f, 0.55f, 0.2f, 0.85f));
		float ph = ((float)_t * 0.4f + p.Y * 0.01f) % 1f;
		DrawCircle(p + new Vector2(3, -14 - ph * 26), 4 + ph * 7, new Color(0.7f, 0.68f, 0.62f, 0.25f * (1 - ph)));
	}

	// ====================================================================
	//  瞭望台观察:黑点(队伍多寡)与烟雾(扬尘远近)——不给名字、不给数目、不给兵种
	// ====================================================================

	private void DrawTowerView()
	{
		DrawRect(new Rect2(0, 0, 1120, 760), new Color("11100c"), true);
		var center = new Vector2(560, 390);

		if (GameState.I.Battle is { } b)
		{
			const float S = 0.55f;                                       // 米 → 像素
			var tower = b.HqPos;

			// 地形剪影(辨方位用,极暗)
			for (int ty = 0; ty < b.Map.H; ty++)
				for (int tx = 0; tx < b.Map.W; tx++)
				{
					var t = b.Map.AtTile(tx, ty);
					if (t == BTerrain.Grass) continue;
					var wp = new Vec2F((tx + 0.5f) * BattleMap.TileSize, (ty + 0.5f) * BattleMap.TileSize);
					var sp = center + new Vector2(wp.X - tower.X, wp.Y - tower.Y) * S;
					Color c = t switch
					{
						BTerrain.Forest => new Color(0.16f, 0.20f, 0.13f),
						BTerrain.River or BTerrain.Ford => new Color(0.13f, 0.19f, 0.22f),
						BTerrain.Hill => new Color(0.20f, 0.19f, 0.16f),
						_ => new Color(0.16f, 0.15f, 0.12f)
					};
					DrawRect(new Rect2(sp, new Vector2(BattleMap.TileSize * S + 1, BattleMap.TileSize * S + 1)), c, true);
				}

			// 自家帐位
			DrawRect(new Rect2(center - new Vector2(4, 4), new Vector2(8, 8)), new Color("d9b34a"), true);

			foreach (var u in b.Units)
			{
				if (u.AliveCount == 0) continue;
				var rel = new Vector2(u.Center.X - tower.X, u.Center.Y - tower.Y);
				float dist = rel.Length();
				var sp = center + rel * S;
				float wobble = 3f + dist * 0.035f;                       // 越远越测不准
				float fade = Mathf.Clamp(1.25f - dist / 750f, 0.25f, 1f);
				bool friend = u.Side == Side.Friend;

				// 黑点群:点数 ≈ 人数/25(你只能估出「一小撮」还是「黑压压一片」)
				int dots = Math.Clamp(u.AliveCount / 25, 1, 12);
				for (int i = 0; i < dots; i++)
				{
					var off = new Vector2(
						(H(u.Id * 31 + i) - 0.5f) * 2f * (6f + wobble),
						(H(u.Id * 57 + i) - 0.5f) * 2f * (5f + wobble * 0.7f));
					var col = friend ? new Color(0.28f, 0.12f, 0.10f, 0.85f * fade)
									 : new Color(0.05f, 0.05f, 0.06f, 0.9f * fade);
					DrawCircle(sp + off, friend ? 2.2f : 2.6f, col);
				}

				// 敌队扬尘/烟:柱数与浓度 ≈ 规模;远则淡(远近的第二信号)
				if (!friend)
				{
					int plumes = Math.Clamp(u.AliveCount / 60, 1, 4);
					for (int i = 0; i < plumes; i++)
					{
						float ph = ((float)_t * 0.25f + H(u.Id * 13 + i)) % 1f;
						var pp = sp + new Vector2((H(u.Id * 7 + i) - 0.5f) * 22f, -6f - ph * 34f);
						DrawCircle(pp, 5f + ph * 12f, new Color(0.62f, 0.58f, 0.50f, 0.28f * (1 - ph) * fade));
					}
				}
			}

			DrawString(_font, new Vector2(16, 26), $"瞭望台 · 亲见  {BattleSim.FormatT(b.Time)}", HorizontalAlignment.Left, -1, 16, new Color("e8e0d0"));
			DrawString(_font, new Vector2(16, 48),
				"黑点=队伍(点多则众) · 扬尘=虏骑(烟浓则近且众) · 记下方位,回沙盘插旗推演 | E/Esc 下台",
				HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
		}
		else
		{
			// 无战事:望大地图方向——虏骑游队的扬尘(或四野无烟)
			DrawString(_font, new Vector2(16, 26), "瞭望台 · 四野", HorizontalAlignment.Left, -1, 16, new Color("e8e0d0"));
			if (!GameState.I.EnemyDefeated)
			{
				var rel = (GameState.I.EnemyPos - GameState.I.PartyPos) * 0.9f;
				var sp = center + rel;
				for (int i = 0; i < 3; i++)
				{
					float ph = ((float)_t * 0.22f + i * 0.31f) % 1f;
					DrawCircle(sp + new Vector2((i - 1) * 10f, -ph * 40f), 6f + ph * 14f,
						new Color(0.62f, 0.58f, 0.50f, 0.30f * (1 - ph)));
				}
				DrawCircle(sp, 3f, new Color(0.05f, 0.05f, 0.06f, 0.9f));
				DrawString(_font, new Vector2(16, 48), "远处有扬尘——虏骑未去。E/Esc 下台", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
			}
			else
				DrawString(_font, new Vector2(16, 48), "四野无烟尘,边野暂安。E/Esc 下台", HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
			DrawRect(new Rect2(center - new Vector2(4, 4), new Vector2(8, 8)), new Color("d9b34a"), true);
		}

		DrawBannerLine();
	}

	private static float H(int n)
	{
		float v = Mathf.Sin(n * 12.9898f) * 43758.547f;
		return v - Mathf.Floor(v);
	}

	// ====================================================================
	//  HUD
	// ====================================================================

	private void DrawHudCommon()
	{
		string place = _area == Area.Inside ? "中军帐" : "营区";
		string war = GameState.I.BattleActive ? $"战况:{BattleSim.FormatT(GameState.I.Battle!.Time)} 进行中"
				   : GameState.I.Battle is { Over: true } ? "战况:已分胜负(沙盘内班师)" : "无战事";
		DrawString(_font, new Vector2(16, 26), $"{place} · {war}", HorizontalAlignment.Left, -1, 16, new Color("e8e0d0"));
		DrawString(_font, new Vector2(16, 46),
			"WASD/方向键 行走 · E 交互 · F1 低难度(沙盘瞭望叠加) · Esc 拔营",
			HorizontalAlignment.Left, -1, 12, new Color("9aa0a8"));
		DrawBannerLine();
	}

	private void DrawBannerLine()
	{
		if (_banner == "" || _bannerAge > 6) return;
		float w = 780;
		var rect = new Rect2((1120 - w) / 2, 66, w, 40);
		DrawRect(rect, new Color(0.12f, 0.08f, 0.05f, 0.93f), true);
		DrawRect(rect, new Color("d9b34a"), false, 2f);
		DrawString(_font, rect.Position + new Vector2(14, 26), _banner, HorizontalAlignment.Left, (int)w - 28, 13, new Color("f2e6c8"));
	}

	private void DrawHint(Vector2 p, string text)
	{
		var rect = new Rect2(p.X - 90, p.Y - 16, 180, 26);
		DrawRect(rect, new Color(0.1f, 0.09f, 0.06f, 0.9f), true);
		DrawRect(rect, new Color("d9b34a"), false, 1.5f);
		DrawString(_font, new Vector2(p.X - 82, p.Y + 3), text, HorizontalAlignment.Left, 168, 12, new Color("f2e6c8"));
	}

	private void DrawChecker(Rect2 r, Color a, Color b, int cell)
	{
		DrawRect(r, a, true);
		for (int x = 0; x < (int)(r.Size.X / cell) + 1; x++)
			for (int y = 0; y < (int)(r.Size.Y / cell) + 1; y++)
				if (((x + y) & 1) == 0)
					DrawRect(new Rect2(r.Position.X + x * cell, r.Position.Y + y * cell,
						Mathf.Min(cell, r.End.X - (r.Position.X + x * cell)),
						Mathf.Min(cell, r.End.Y - (r.Position.Y + y * cell))), b, true);
	}

	private void DrawLabel(Vector2 p, string s, Color c)
		=> DrawString(_font, p + new Vector2(-70, 0), s, HorizontalAlignment.Center, 140, 13, c);
}
