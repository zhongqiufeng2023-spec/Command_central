using Godot;
using CommandPost.Core;
using System.Linq;
using Side = CommandPost.Core.Side;

// BattleRoot HUD 与覆盖层:顶栏 / 军情流水 / 横幅 / 战毕复盘 + 绘制小工具。
public partial class BattleRoot
{
	private void DrawHud()
	{
		string clock = _sim.Over ? "[战毕]" : _sim.Deploying ? "[布阵]" : _paused ? "[暂停]" : $"[▶ {Speeds[_speedIdx]:0.#}x]";
		string mode = _realView ? "真实战场(对照,Tab切回)" : "沙盘·帅帐所知";
		DrawText(new Vector2(16, 22), $"黑松岭之战 · {mode}   {BattleSim.FormatT(_sim.Time)} {clock}", 15, new Color("e8e0d0"));
		DrawText(new Vector2(16, 42),
			_sim.Deploying
				? "布阵中(时间未动):左键选部 · 右键摆位(即时) · 1234 当面定姿态 | 回车=擂鼓开战 | 滚轮缩放 WASD平移 Esc回帐"
				: "空格暂停 ±调速 Tab视图 | 左键选部 右键行军 1进攻 2据守 3等待 4游走 R探问 B军书 | 5鼓 6金 7旗(声程即时·敌亦闻) | 中键塘骑 Shift+中键疑兵 Ctrl+左插旗 Esc回帐",
			11, new Color("9aa0a8"));
		DrawMissionBoard();

		if (!_realView && _selectedId >= 0 && _sim.Sandbox.Own.TryGetValue(_selectedId, out var mk))
		{
			var u = _sim.ById(_selectedId);
			DrawText(new Vector2(880, 70), $"已选:{u?.Name}", 14, new Color("e6c25c"));
			if (u != null)
				DrawText(new Vector2(880, 90), $"武将 {u.Officer.Name}({BattleSim.PersonalityCn(u.Officer.Personality)})", 12,
					u.Officer.Personality == Personality.Steady ? new Color("b8c8a8") : new Color("e0b070"));
			DrawText(new Vector2(880, 108), $"所报兵力 约{mk.Count}", 12, new Color("d8d2c4"));
			DrawText(new Vector2(880, 126), $"所报状态 {mk.StateCn}", 12, new Color("d8d2c4"));
			DrawText(new Vector2(880, 144), $"报于 {(int)(_sim.Time - mk.T)}s 前", 12, new Color("c8bfa8"));
			DrawText(new Vector2(880, 164), "右键=遣令骑传令", 11, new Color("9aa0a8"));
		}
	}

	/// <summary>军令牌:行营任务目标一览。战中只按「你所知」显示——真相类目标战毕才揭晓。</summary>
	private void DrawMissionBoard()
	{
		if (_sim.Mission is not { } m || _realView) return;
		float x = 880, y = 190;
		DrawText(new Vector2(x, y), $"军令牌 · {m.Title}", 13, new Color("e6c25c")); y += 8;
		foreach (var o in m.Objectives)
		{
			y += 19;
			string mark; Color c;
			if (!o.Active) { mark = "◇"; c = new Color("6f6a5e"); }
			else switch (o.State)
			{
				case BObjectiveState.Done: mark = "✓"; c = new Color("8fc97a"); break;
				case BObjectiveState.Failed: mark = "✗"; c = new Color("d97a6a"); break;
				case BObjectiveState.Partial: mark = "◐"; c = new Color("e6c25c"); break;
				default:
					mark = "・";
					c = o.Kind is BObjectiveKind.DefeatEnemy or BObjectiveKind.PreserveArmy or BObjectiveKind.RelieveAlly
						? new Color("9aa0a8") : new Color("d8d2c4");   // 真相类:战毕方知
					break;
			}
			DrawText(new Vector2(x, y), $"{mark} {o.Cn}{(o.Active ? "" : "〔令未至〕")}", 12, c);
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
		float w = 620, h = 640;
		var box = new Rect2((1120 - w) / 2, (760 - h) / 2, w, h);
		DrawRect(box, new Color(0.10f, 0.09f, 0.07f, 0.97f), true);
		DrawRect(box, new Color("d9b34a"), false, 2f);

		float x = box.Position.X + 30, y = box.Position.Y + 38;
		bool leftCollapsed = _sim.LeftWingCollapsed;
		string title = _sim.Winner == Side.Friend ? "捷!虏骑溃走"
					 : _sim.Winner == Side.Enemy ? (leftCollapsed ? "败绩……左翼崩覆" : "败绩……全军溃散")
					 : "战罢——虏骑遁去";
		DrawText(new Vector2(x, y), $"战毕 —— {title}", 18, new Color("e6c25c")); y += 32;

		DrawText(new Vector2(x, y), "本路各部(实况复盘):", 13, new Color("d8d2c4")); y += 21;
		foreach (var u in _sim.Units.Where(u => u.Side == Side.Friend && !u.Allied))
		{
			DrawText(new Vector2(x, y),
				$"{u.Name}〔{BattleSim.ArmCn(u.Type)}〕 {u.MaxCount}人 → 存{u.AliveCount}  斩获{u.Kills}  {u.StateCn}",
				12, new Color("c8c2b4"));
			y += 19;
		}
		if (_sim.Units.Any(u => u.Allied))
		{
			y += 4;
			int aStart = _sim.Units.Where(u => u.Allied).Sum(u => u.MaxCount);
			int aAlive = _sim.Units.Where(u => u.Allied).Sum(u => u.AliveCount);
			DrawText(new Vector2(x, y),
				$"左翼李嵩一路:{aStart}人 → 存{aAlive}  {(leftCollapsed ? "崩覆" : "得全")}",
				12, leftCollapsed ? new Color("d9917a") : new Color("e8b878"));
			y += 19;
		}
		y += 8;
		int eDead = _sim.Units.Where(u => u.Side == Side.Enemy).Sum(u => u.MaxCount - u.AliveCount - u.Fled);
		int eFled = _sim.Units.Where(u => u.Side == Side.Enemy).Sum(u => u.Fled);
		DrawText(new Vector2(x, y), $"虏军:遗尸约{eDead},溃逃出野约{eFled}", 12, new Color("bcd2ec")); y += 26;

		if (_sim.Mission is { Verdict: { } v } m)
		{
			DrawText(new Vector2(x, y), "—— 行营裁断(周帅隔着他的雾看你)——", 13, new Color("e6c25c")); y += 21;
			foreach (var line in v.Lines)
			{ DrawText(new Vector2(x + 8, y), line, 12, line.Contains("(-") ? new Color("d9917a") : new Color("a8c99a")); y += 18; }
			y += 8;
			// 信任条
			var barBg = new Rect2(x, y, 340, 12);
			DrawRect(barBg, new Color(0.2f, 0.18f, 0.15f), true);
			DrawRect(new Rect2(x, y, 340 * v.Trust / 100f, 12),
				v.Trust >= 55 ? new Color("8fc97a") : v.Trust >= 35 ? new Color("e6c25c") : new Color("d97a6a"), true);
			DrawRect(barBg, new Color("6f6a5e"), false, 1f);
			DrawText(new Vector2(x + 352, y + 11), $"{v.Trust}/100", 12, new Color("d8d2c4"));
			y += 26;
			DrawText(new Vector2(x, y), $"裁断:「{v.VerdictCn}」", 14, new Color("f2e6c8")); y += 26;
		}
		DrawText(new Vector2(x, y), "回车 · 班师回营(返回大地图)      P · 复盘对照(认知 ↔ 真相)", 13, new Color("e6c25c"));
	}

	// —— 绘制小工具 ——
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
