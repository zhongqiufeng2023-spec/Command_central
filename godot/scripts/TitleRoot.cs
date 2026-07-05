using Godot;
using System;

/// <summary>
/// 标题画面:夜营剪影 + 主菜单(出征 / 继续行军 / 操作说明 / 退出)。
/// 全部即时绘制;↑↓/W S 选择,回车确认,鼠标可点。
/// </summary>
public partial class TitleRoot : Node2D
{
	private Font _font = null!;
	private double _t;
	private int _sel;
	private bool _help;
	private string _hint = ""; private double _hintAge = 99;

	private static readonly string[] Items = { "出征 · 黑松岭", "继续行军(读档)", "操作说明", "退出" };
	private const float MenuX = 560, MenuY0 = 470, MenuDy = 46;

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.Nearest;
		_font = UiKit.MakeFont();
		GetWindow().GrabFocus();
	}

	public override void _Process(double delta) { _t += delta; _hintAge += delta; QueueRedraw(); }

	public override void _Input(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false } k)
		{
			if (_help)
			{
				if (k.Keycode is Key.Escape or Key.Enter or Key.KpEnter) _help = false;
				return;
			}
			switch (k.Keycode)
			{
				case Key.W or Key.Up: _sel = (_sel + Items.Length - 1) % Items.Length; break;
				case Key.S or Key.Down: _sel = (_sel + 1) % Items.Length; break;
				case Key.Enter or Key.KpEnter: Activate(_sel); break;
				case Key.Escape: GetTree().Quit(); break;
			}
		}
		else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
		{
			if (_help) { _help = false; return; }
			int hit = HitItem(mb.Position);
			if (hit >= 0) Activate(hit);
		}
		else if (e is InputEventMouseMotion mm && !_help)
		{
			int hov = HitItem(mm.Position);
			if (hov >= 0) _sel = hov;
		}
	}

	private static int HitItem(Vector2 p)
	{
		for (int i = 0; i < Items.Length; i++)
			if (new Rect2(MenuX - 160, MenuY0 + i * MenuDy - 26, 320, 38).HasPoint(p)) return i;
		return -1;
	}

	private void Activate(int i)
	{
		switch (i)
		{
			case 0:
				GameState.I.NewRun();
				GameState.Go(this, "res://Overworld.tscn");
				break;
			case 1:
				if (GameState.I.LoadRun()) GameState.Go(this, "res://Overworld.tscn");
				else { _hint = "尚无存档——先出征,行军中的进度会自动记下。"; _hintAge = 0; }
				break;
			case 2: _help = true; break;
			case 3: GetTree().Quit(); break;
		}
	}

	public override void _Draw()
	{
		// —— 夜空(分层色带 + 星 + 低月)——
		DrawRect(new Rect2(0, 0, 1120, 300), new Color("12141f"), true);
		DrawRect(new Rect2(0, 300, 1120, 160), new Color("171826"), true);
		DrawRect(new Rect2(0, 460, 1120, 120), new Color("1d1a24"), true);
		var rng = new Random(7);
		for (int i = 0; i < 90; i++)
		{
			float x = rng.Next(0, 1120), y = rng.Next(0, 380);
			float tw = 0.35f + 0.3f * MathF.Sin((float)_t * (1.2f + i % 5 * 0.4f) + i);
			DrawRect(new Rect2(x, y, 2, 2), new Color(0.85f, 0.85f, 0.9f, Math.Max(0.08f, tw)), true);
		}
		DrawCircle(new Vector2(920, 130), 46, new Color("d8d2c0"));
		DrawCircle(new Vector2(905, 118), 40, new Color("12141f"));       // 月牙

		// —— 黑松岭远山剪影 ——
		var ridge = new Vector2[24];
		for (int i = 0; i < 24; i++)
		{
			float x = i * 1120f / 23f;
			float h = 430 + 46 * MathF.Sin(i * 0.9f) + 24 * MathF.Sin(i * 2.3f + 1.7f);
			ridge[i] = new Vector2(x, h);
		}
		var poly = new Vector2[26];
		ridge.CopyTo(poly, 0);
		poly[24] = new Vector2(1120, 580); poly[25] = new Vector2(0, 580);
		DrawColoredPolygon(poly, new Color("0d1410"));

		// —— 地面 ——
		DrawRect(new Rect2(0, 560, 1120, 200), new Color("191710"), true);

		// —— 中军帐剪影(左)+ 帐内暖光 + 牙旗 ——
		var tentBase = new Vector2(230, 600);
		DrawColoredPolygon(new[]
		{
			tentBase + new Vector2(-150, 0), tentBase + new Vector2(-95, -105),
			tentBase + new Vector2(0, -128), tentBase + new Vector2(95, -105), tentBase + new Vector2(150, 0)
		}, new Color("221c14"));
		float glow = 0.55f + 0.1f * MathF.Sin((float)_t * 2.3f);
		DrawColoredPolygon(new[]
		{ tentBase + new Vector2(-24, 0), tentBase + new Vector2(0, -44), tentBase + new Vector2(24, 0) },
			new Color(0.95f, 0.65f, 0.25f, glow));
		DrawLine(tentBase + new Vector2(0, -128), tentBase + new Vector2(0, -190), new Color("6e5b41"), 3f);
		float wave = MathF.Sin((float)_t * 2.6f) * 6f;
		DrawColoredPolygon(new[]
		{ tentBase + new Vector2(0, -190), tentBase + new Vector2(52, -178 + wave), tentBase + new Vector2(0, -164) },
			new Color("a8352a"));

		// 营火两簇
		for (int i = 0; i < 2; i++)
		{
			var fp = new Vector2(520 + i * 260, 640 + i * 30);
			float f = 0.5f + 0.35f * MathF.Sin((float)_t * (3f + i) + i * 2);
			DrawCircle(fp, 7 + 2 * f, new Color(0.95f, 0.55f, 0.18f, 0.75f));
			DrawCircle(fp + new Vector2(0, -6 - 4 * f), 3.5f, new Color(0.99f, 0.8f, 0.35f, 0.8f));
		}

		// —— 题字 ——
		DrawString(_font, new Vector2(0, 205), "中 军 帐", HorizontalAlignment.Center, 1120, 64, new Color("e6c25c"));
		DrawString(_font, new Vector2(0, 250), "The Command Tent", HorizontalAlignment.Center, 1120, 15, new Color("8a8474"));
		DrawString(_font, new Vector2(0, 296), "—— 一将功成万骨枯 ——", HorizontalAlignment.Center, 1120, 18, new Color("b8b2a4"));
		DrawString(_font, new Vector2(0, 330), "你永远没有上帝视角:所有的『知道』都又旧又可疑,而你必须决断。",
			HorizontalAlignment.Center, 1120, 13, new Color("6f6a5e"));

		// —— 菜单 ——
		for (int i = 0; i < Items.Length; i++)
		{
			bool on = i == _sel;
			bool dim = i == 1 && !GameState.SaveExists;
			var y = MenuY0 + i * MenuDy;
			if (on)
			{
				DrawColoredPolygon(new[]
				{ new Vector2(MenuX - 150, y - 12), new Vector2(MenuX - 132, y - 5), new Vector2(MenuX - 150, y + 2) },
					new Color("e6c25c"));
			}
			DrawString(_font, new Vector2(MenuX - 200, y), Items[i], HorizontalAlignment.Center, 400, 19,
				dim ? new Color("55524a") : on ? new Color("f2e6c8") : new Color("9a948a"));
		}

		if (_hint != "" && _hintAge < 4)
			DrawString(_font, new Vector2(0, MenuY0 + Items.Length * MenuDy + 18), _hint,
				HorizontalAlignment.Center, 1120, 13, new Color("d9b34a"));

		DrawString(_font, new Vector2(0, 738), "↑↓ 选择 · 回车 确认        像素实验版 · Godot 4.7 Mono",
			HorizontalAlignment.Center, 1120, 11, new Color("55524a"));

		if (_help) UiKit.DrawHelp(this, _font);
	}
}
