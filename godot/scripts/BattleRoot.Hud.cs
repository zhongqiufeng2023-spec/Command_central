using Godot;
using CommandPost.Core;
using System.Linq;
using Side = CommandPost.Core.Side;

// BattleRoot HUD 与覆盖层:顶栏 / 军情流水 / 横幅 / 战毕复盘 + 绘制小工具。
public partial class BattleRoot
{
	private void DrawHud()
	{
		string clock = _sim.Over ? "[战毕]" : _paused ? "[暂停]" : $"[▶ {Speeds[_speedIdx]:0.#}x]";
		string mode = _realView ? "真实战场(对照,Tab切回)" : "沙盘·帅帐所知";
		DrawText(new Vector2(16, 22), $"黑松岭之战 · {mode}   {BattleSim.FormatT(_sim.Time)} {clock}", 15, new Color("e8e0d0"));
		DrawText(new Vector2(16, 42),
			"空格暂停 ±调速 Tab视图 滚轮缩放 WASD平移 | 左键选部 右键行军(Shift疾) 1进攻 2据守 3等待 4游走 R探问 | 中键塘骑 Ctrl+左键插旗",
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
