using Godot;
using CommandPost.Core;
using System.Collections.Generic;
using System.Linq;
using Side = CommandPost.Core.Side;

// 战后复盘(PRD §10):战毕按 P 进入——上帝视角逐秒回放整场真相,
// 叠加「当时你沙盘上以为的」旧影(虚线连到实际位置 = 认知与真相的裂缝),
// 战中隐形的令骑/塘骑在此显形(含被截杀的),关键分叉点可一键跳转。
public partial class BattleRoot
{
	private bool _replayMode;
	private float _replayT;
	private bool _replayPlay;
	private List<BKeyMoment>? _keyMoments;
	private const float ReplaySpeed = 8f;

	private void EnterReplay()
	{
		_replayMode = true;
		_replayT = 0;
		_replayPlay = true;
		_keyMoments ??= _sim.Replay.Analyze(id => _sim.ById(id)?.Name ?? "敌部");
		_cam = new Vector2(_sim.Map.WorldW / 2f, _sim.Map.WorldH / 2f);
	}

	private void TickReplay(double delta)
	{
		if (!_replayMode || !_replayPlay) return;
		_replayT += (float)delta * ReplaySpeed;
		if (_replayT >= _sim.Replay.Duration) { _replayT = _sim.Replay.Duration; _replayPlay = false; }
	}

	private void HandleReplayKey(InputEventKey k)
	{
		switch (k.Keycode)
		{
			case Key.Escape or Key.P: _replayMode = false; break;
			case Key.Space: _replayPlay = !_replayPlay; break;
			case Key.Left: _replayT = Mathf.Max(0, _replayT - 15f); break;
			case Key.Right: _replayT = Mathf.Min(_sim.Replay.Duration, _replayT + 15f); break;
			case Key.Home: _cam = new Vector2(_sim.Map.WorldW / 2f, _sim.Map.WorldH / 2f); break;
			case Key.Key1 or Key.Key2 or Key.Key3:
			{
				int i = (int)k.Keycode - (int)Key.Key1;
				if (_keyMoments != null && i < _keyMoments.Count)
				{
					var km = _keyMoments[i];
					_replayT = Mathf.Max(0, km.T - 5f);
					_cam = new Vector2(km.TruthAt.X, km.TruthAt.Y);
					_replayPlay = true;
				}
				break;
			}
		}
	}

	/// <summary>复盘模式的鼠标:滚轮缩放 + 点时间轴跳转。返回 true = 已消费。</summary>
	private bool HandleReplayMouse(InputEventMouseButton mb)
	{
		if (!_replayMode) return false;
		switch (mb.ButtonIndex)
		{
			case MouseButton.WheelUp: ZoomAt(mb.Position, 1.15f); break;
			case MouseButton.WheelDown: ZoomAt(mb.Position, 1f / 1.15f); break;
			case MouseButton.Left:
				var bar = new Rect2(60, 716, 1000, 30);
				if (bar.HasPoint(mb.Position) && _sim.Replay.Duration > 0)
				{
					_replayT = Mathf.Clamp((mb.Position.X - 60f) / 1000f, 0f, 1f) * _sim.Replay.Duration;
					_replayPlay = false;
				}
				break;
		}
		return true;
	}

	private void DrawReplay()
	{
		var f = _sim.Replay.At(_replayT);
		if (f == null)
		{
			DrawText(new Vector2(16, 22), "复盘:无胶卷(战役未开打)。Esc 返回。", 14, new Color("e8e0d0"));
			return;
		}

		// —— 认知层:当时沙盘以为的敌位(空心菱形),虚线连向实际位置 = 裂缝可视化 ——
		foreach (var m in f.BeliefEnemy)
		{
			var bp = ToScreen(m.Pos);
			DrawDiamond(bp, 10f, new Color(0.5f, 0.68f, 0.9f, 0.55f), filled: false);
			foreach (var u in f.Units)
			{
				if (u.Id != m.Id || u.Alive == 0) continue;
				var tp = ToScreen(u.Pos);
				if (bp.DistanceTo(tp) > 14f)
					DrawDashedLine(bp, tp, new Color(0.9f, 0.78f, 0.42f, 0.55f), 1.4f, 7f);
				break;
			}
		}

		// —— 真相层:各部色团(面积≈存员),名与数全揭(上帝视角只在战后)——
		foreach (var u in f.Units)
		{
			if (u.Alive == 0) continue;
			var p = ToScreen(u.Pos);
			float r = Mathf.Clamp(Mathf.Sqrt(u.Alive) * 0.9f, 2.5f, 16f) * _zoom;
			Color c = u.Side == Side.Friend
				? (u.Allied ? new Color(0.88f, 0.58f, 0.22f) : new Color(0.72f, 0.26f, 0.20f))
				: new Color(0.32f, 0.45f, 0.62f);
			if (u.State is BUnitState.Routing or BUnitState.Shattered) c = new Color(c.R, c.G, c.B, 0.45f);
			DrawCircle(p, r, c);
			DrawText(p + new Vector2(0, -r - 7), $"{_sim.ById(u.Id)?.Name} {u.Alive}", 11,
				new Color(0.92f, 0.90f, 0.82f, 0.85f), center: true);
			if (u.State is BUnitState.Routing or BUnitState.Shattered)
				DrawText(p + new Vector2(0, r + 12), u.State == BUnitState.Routing ? "溃走" : "溃散", 10,
					new Color(0.9f, 0.6f, 0.5f, 0.8f), center: true);
		}

		// —— 骑手显形(战中你看不到他们;被截杀的画红×——「原来那封军书死在这里」)——
		foreach (var r2 in f.Riders)
		{
			var p = ToScreen(r2.Pos);
			if (r2.Lost)
			{
				var red = new Color(0.85f, 0.3f, 0.25f, 0.9f);
				DrawLine(p + new Vector2(-4, -4), p + new Vector2(4, 4), red, 2f);
				DrawLine(p + new Vector2(-4, 4), p + new Vector2(4, -4), red, 2f);
			}
			else
				DrawDiamond(p, 4f, r2.Kind == RiderKind.Scout ? new Color("7ad0a0") : new Color("5ad0c0"));
		}

		DrawReplayHud();
	}

	private void DrawReplayHud()
	{
		DrawText(new Vector2(16, 22),
			$"复盘 · 上帝视角(认知 ↔ 真相)   {BattleSim.FormatT(_replayT)} / {BattleSim.FormatT(_sim.Replay.Duration)}   {(_replayPlay ? $"[▶{ReplaySpeed:0}x]" : "[▮▮]")}",
			15, new Color("e6c25c"));
		DrawText(new Vector2(16, 42),
			"空格 播/停 · ←→ ±15秒 · 点时间轴跳转 · 1-3 跳关键分叉 · 滚轮缩放 WASD平移 · Esc 返回结算",
			11, new Color("9aa0a8"));

		// 关键分叉点(自动标注:你以为 vs 实际)
		if (_keyMoments is { Count: > 0 })
		{
			DrawText(new Vector2(620, 66), "—— 关键分叉(裂缝最大的时刻)——", 12, new Color("e6c25c"));
			for (int i = 0; i < _keyMoments.Count; i++)
				DrawText(new Vector2(620, 86 + i * 18), $"{i + 1}. {_keyMoments[i].Cn}", 10, new Color("c8c2b4"));
		}

		// 时间轴
		var bar = new Rect2(60, 722, 1000, 12);
		DrawRect(bar, new Color(0.13f, 0.12f, 0.10f, 0.92f), true);
		float dur = Mathf.Max(1f, _sim.Replay.Duration);
		DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * (_replayT / dur), bar.Size.Y)), new Color("b08a35"), true);
		DrawRect(bar, new Color("6f6a5e"), false, 1f);
		if (_keyMoments != null)
			foreach (var km in _keyMoments)
				DrawDiamond(new Vector2(bar.Position.X + bar.Size.X * (km.T / dur), bar.Position.Y + 6), 5f, new Color("e6c25c"));
	}
}
