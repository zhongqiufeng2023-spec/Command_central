using Godot;
using CommandPost.Core;
using System.Collections.Generic;
using System.Linq;
using Side = CommandPost.Core.Side;

// BattleRoot 绘制层:地形 + 真实战场(对照)+ 沙盘(玩家世界)。
public partial class BattleRoot
{
	public override void _Draw()
	{
		DrawRect(new Rect2(0, 0, GetViewportRect().Size), new Color(_realView ? "22201a" : "2c2a22"), true);
		DrawTerrain();
		DrawDeployZone();

		if (_realView) DrawRealWorld();
		else DrawSandbox();

		DrawHud();
		DrawFeed();
		DrawBanner();
		DrawEndOverlay();
	}

	private void DrawTerrain()
	{
		var m = _sim.Map;
		float ts = BattleMap.TileSize * _zoom;
		for (int ty = 0; ty < m.H; ty++)
			for (int tx = 0; tx < m.W; tx++)
			{
				var p = ToScreen(new Vec2F(tx * BattleMap.TileSize, ty * BattleMap.TileSize));
				if (p.X < -ts || p.Y < -ts || p.X > 1130 || p.Y > 770) continue;
				var t = m.AtTile(tx, ty);
				Color c = _realView
					? t switch
					{
						BTerrain.Road => new Color("6b5b3e"), BTerrain.Forest => new Color("2e4023"),
						BTerrain.Hill => new Color("575040"), BTerrain.River => new Color("2d4d5e"),
						BTerrain.Ford => new Color("3e6172"), _ => new Color("4a4332")
					}
					: t switch
					{
						BTerrain.Road => new Color("4e4433"), BTerrain.Forest => new Color("39412e"),
						BTerrain.Hill => new Color("454034"), BTerrain.River => new Color("31434d"),
						BTerrain.Ford => new Color("3d5560"), _ => new Color("3a372c")
					};
				DrawRect(new Rect2(p, new Vector2(ts + 1, ts + 1)), c, true);
			}

		// 帅帐
		var hq = ToScreen(_sim.HqPos);
		DrawRect(new Rect2(hq - new Vector2(7, 7), new Vector2(14, 14)), new Color("d9b34a"), true);
		DrawText(hq + new Vector2(0, -14), "帅帐", 12, new Color("e6c25c"), center: true);
	}

	/// <summary>布阵区(战前):西线自家地界淡金渲染 + 东界虚线。</summary>
	private void DrawDeployZone()
	{
		if (!_sim.Deploying) return;
		var tl = ToScreen(new Vec2F(0, 0));
		var br = ToScreen(new Vec2F(_sim.DeployZoneMaxX, _sim.Map.WorldH));
		DrawRect(new Rect2(tl, br - tl), new Color(0.85f, 0.7f, 0.3f, 0.05f), true);
		for (float y = tl.Y; y < br.Y; y += 14)
			DrawLine(new Vector2(br.X, y), new Vector2(br.X, Mathf.Min(y + 7, br.Y)), new Color(0.85f, 0.7f, 0.3f, 0.55f), 2f);
		DrawText(new Vector2(br.X, tl.Y + 90), "布阵区界", 12, new Color("d9b34a"), center: true);
	}

	// —— 真实战场(对照):两千余士兵逐个画 ——
	private void DrawRealWorld()
	{
		foreach (var u in _sim.Units)
		{
			if (u.AliveCount == 0) continue;
			bool friend = u.Side == Side.Friend;
			bool broken = u.State is BUnitState.Routing or BUnitState.Shattered;
			float r = Mathf.Max(1.3f, 1.05f * _zoom);
			Color body = friend
				? (u.Allied ? new Color(0.88f, 0.58f, 0.22f) : new Color(0.72f, 0.26f, 0.20f))   // 友邻(左翼)橙,本路红
				: new Color(0.32f, 0.45f, 0.62f);
			if (broken) body = new Color(body.R, body.G, body.B, 0.45f + 0.2f * Mathf.Sin((float)_sim.Time * 6f));

			foreach (var s in u.Soldiers)
				DrawCircle(ToScreen(s.Pos), r, body);

			// 队旗:名+存员+士气条+状态
			var c = ToScreen(u.Center);
			DrawText(c + new Vector2(0, -16 - 8 * _zoom), $"{u.Name} {u.AliveCount}", 12,
				u.Allied ? new Color("ffc98a") : friend ? new Color("ffd9a0") : new Color("bcd2ec"), center: true);
			float w = 40;
			DrawRect(new Rect2(c.X - w / 2, c.Y - 12 - 8 * _zoom, w, 3), new Color(0, 0, 0, 0.6f), true);
			DrawRect(new Rect2(c.X - w / 2, c.Y - 12 - 8 * _zoom, w * u.Morale / 100f, 3),
				u.Morale > 45 ? new Color("6fbf5a") : new Color("d0894a"), true);
			if (u.State != BUnitState.Steady)
				DrawText(c + new Vector2(0, -2 - 8 * _zoom), u.StateCn, 11, new Color("e8d9b0"), center: true);
		}

		// 箭矢:短线示弹道
		foreach (var a in _sim.Arrows)
		{
			var p = ToScreen(a.Pos);
			var tail = ToScreen(a.Pos - a.Vel * 0.045f);
			DrawLine(tail, p, new Color(0.92f, 0.88f, 0.7f, 0.85f), Mathf.Max(1f, 0.8f * _zoom));
		}

		// 骑手(真实位置——对照视图才可见)
		foreach (var r2 in _sim.Riders.Where(x => !x.Lost))
			DrawDiamond(ToScreen(r2.Pos), 4f, new Color("5ad0c0"));
	}

	// —— 沙盘(玩家世界):标记与旧影 ——
	private void DrawSandbox()
	{
		float now = _sim.Time;

		// 瞭望叠加 = 低难度选项(F1)。默认关:想看敌情,回中军帐亲自登瞭望台(黑点与烟雾)。
		bool easyVision = GameState.I?.EasySandboxVision ?? true;
		var live = easyVision ? _sim.WatchtowerVisible().ToList() : new List<BattleUnit>();
		var liveIds = new HashSet<int>(live.Select(u => u.Id));
		if (easyVision)
		{
			DrawArc(ToScreen(_sim.HqPos), _sim.WatchtowerRange * _zoom, 0, Mathf.Tau, 64,
				new Color(0.85f, 0.75f, 0.45f, 0.30f), 1.5f, true);
			DrawText(ToScreen(_sim.HqPos) + new Vector2(0, _sim.WatchtowerRange * _zoom + 12), "瞭望所及(低难度)", 10,
				new Color(0.85f, 0.75f, 0.45f, 0.5f), center: true);
		}

		// 信息旗
		foreach (var f in _sim.Sandbox.Flags)
		{
			var p = ToScreen(f.Pos);
			DrawLine(p, p + new Vector2(0, -16), new Color("e6c25c"), 2f);
			DrawColoredPolygon(new[] { p + new Vector2(0, -16), p + new Vector2(12, -12), p + new Vector2(0, -8) }, new Color("d9b34a"));
			DrawText(p + new Vector2(0, 12), f.Label, 11, new Color("e6c25c"), center: true);
		}

		// 敌情旧影(菱形;越旧越淡)——望楼正看着的不画旧影,画下面的实见
		foreach (var em in _sim.Sandbox.Enemy.Values)
		{
			if (liveIds.Contains(em.UnitId)) continue;
			var u = _sim.ById(em.UnitId);
			float age = now - em.T;
			float a = Mathf.Clamp(0.95f - age / 120f * 0.6f, 0.3f, 0.95f);
			var p = ToScreen(em.Pos);
			DrawDiamond(p, 11f, new Color(0.42f, 0.58f, 0.78f, a), filled: false);
			string ty = em.Type is UnitType t ? BattleSim.ArmCn(t) : "不明";
			DrawText(p + new Vector2(0, -18), $"虏·{ty} 约{em.Est}", 12, new Color(0.72f, 0.82f, 0.94f, a), center: true);
			DrawText(p + new Vector2(0, 14), $"{(int)age}s前", 10, new Color(0.7f, 0.7f, 0.65f, a), center: true);
		}

		// 己方所报位置(圆token;信息也会旧!)——望楼看得见的画实时位置
		foreach (var mk in _sim.Sandbox.Own.Values)
		{
			var u = _sim.ById(mk.UnitId);
			if (u is null) continue;
			bool watched = liveIds.Contains(mk.UnitId) && u.AliveCount > 0;
			float age = now - mk.T;
			var p = ToScreen(watched ? u.Center : mk.Pos);
			bool sel = mk.UnitId == _selectedId;
			var col = u.AliveCount == 0 ? new Color(0.4f, 0.3f, 0.3f) : new Color(0.70f, 0.24f, 0.18f);
			DrawCircle(p, 10f, col);
			DrawArc(p, 10f, 0, Mathf.Tau, 24, new Color("d9b34a"), sel ? 3f : 1.5f, true);
			DrawText(p + new Vector2(0, -18), $"{u.Name} 约{(watched ? u.AliveCount : mk.Count)}", 12, new Color("ffe0b0"), center: true);
			DrawText(p + new Vector2(0, 14), watched ? $"{u.StanceCn}·{u.StateCn}·望见" : $"{mk.StateCn}·{(int)age}s前", 10,
				watched ? new Color("e8d9a0") : new Color("c8bfa8"), center: true);
		}

		// 左翼友邻(李嵩部):送到手上的战况标记——不归你辖,救不救是你的抉择
		foreach (var am in _sim.Sandbox.Ally.Values)
		{
			float age = now - am.T;
			float a = Mathf.Clamp(0.95f - age / 120f * 0.6f, 0.3f, 0.95f);
			var p = ToScreen(am.Pos);
			var col = new Color(0.92f, 0.62f, 0.28f, a);
			DrawRect(new Rect2(p - new Vector2(9, 9), new Vector2(18, 18)), col, false, 2f);
			string title = am.UnitId < 0
				? "左翼李嵩部(行营所报)"
				: $"左翼·{_sim.ById(am.UnitId)?.Officer.Name}部 约{am.Est}";
			DrawText(p + new Vector2(0, -18), title, 12, new Color(0.95f, 0.78f, 0.5f, a), center: true);
			DrawText(p + new Vector2(0, 14), $"{am.StateCn}·{(int)age}s前", 10, new Color(0.85f, 0.8f, 0.65f, a), center: true);
		}

		// 望楼实见的敌部(实时亮菱形,只报约数)
		foreach (var u in live.Where(x => x.Side == Side.Enemy))
		{
			var p = ToScreen(u.Center);
			int est = System.Math.Max(10, u.AliveCount / 10 * 10);
			DrawDiamond(p, 11f, new Color(0.50f, 0.68f, 0.90f, 0.95f), filled: false);
			DrawText(p + new Vector2(0, -18), $"虏·{BattleSim.ArmCn(u.Type)} 约{est}", 12, new Color("a8c8ec"), center: true);
			DrawText(p + new Vector2(0, 14), "望见·实时", 10, new Color("d0c8a0"), center: true);
		}

		// 在途骑手(估计位置:按出发时刻+计划路线推算——被截杀了你也不知道)
		foreach (var r in _sim.Riders.Where(x => x.EstPath.Count > 0))
		{
			var est = EstimateRiderPos(r, now);
			if (est is { } ep)
			{
				DrawDiamond(ToScreen(ep), 4.5f, new Color(0.9f, 0.92f, 0.85f, 0.85f));
				DrawText(ToScreen(ep) + new Vector2(0, -10), r.Kind == RiderKind.Scout ? "塘" : "令", 10, new Color("d8e8d0"), center: true);
			}
		}
	}

	/// <summary>沙盘上骑手的「应该到哪了」——纯推算,非真实。</summary>
	private Vec2F? EstimateRiderPos(Rider r, float now)
	{
		float travelled = (now - r.Depart) * r.Speed * 0.85f;   // 平均地形折减估计
		var pts = new List<Vec2F> { _sim.HqPos };
		pts.AddRange(r.EstPath);
		float total = 0;
		for (int i = 1; i < pts.Count; i++) total += pts[i - 1].DistanceTo(pts[i]);
		if (total <= 0) return null;

		float leg = travelled <= total ? travelled
				  : travelled <= total * 2 ? total * 2 - travelled     // 折返
				  : -1;
		if (leg < 0) return null;                                        // 早该回来了(未归则报警)
		float acc = 0;
		for (int i = 1; i < pts.Count; i++)
		{
			float d = pts[i - 1].DistanceTo(pts[i]);
			if (acc + d >= leg)
				return Vec2F.Lerp(pts[i - 1], pts[i], d < 0.001f ? 0 : (leg - acc) / d);
			acc += d;
		}
		return pts[^1];
	}
}
