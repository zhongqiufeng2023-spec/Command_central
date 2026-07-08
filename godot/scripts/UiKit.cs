using Godot;

/// <summary>
/// 共用 UI 小件:中文字体、操作说明页、场景内暂停菜单。
/// 全部即时绘制(DrawString/DrawRect),与各场景的像素画法一致。
/// </summary>
public static class UiKit
{
	public static Font MakeFont()
	{
		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun" };
		return sf;
	}

	/// <summary>操作说明整页(标题画面与暂停菜单共用)。</summary>
	public static void DrawHelp(CanvasItem c, Font f)
	{
		c.DrawRect(new Rect2(0, 0, 1120, 760), new Color(0.06f, 0.055f, 0.045f, 0.96f), true);
		void T(float x, float y, string s, int size, string col) =>
			c.DrawString(f, new Vector2(x, y), s, HorizontalAlignment.Left, -1, size, new Color(col));

		T(80, 70, "操 作 说 明", 26, "e6c25c");
		T(80, 100, "这游戏的卖点不是打仗,是看不见却要决断——你所有的『知道』都又旧又可疑。", 13, "9aa0a8");

		float y = 150;
		void Sec(string title) { T(80, y, title, 16, "e6c25c"); y += 26; }
		void K(string key, string desc) { T(100, y, key, 13, "ffd9a0"); T(320, y, desc, 13, "c8c2b4"); y += 22; }

		Sec("大地图(行军——粮草与疲惫是两本账)");
		K("WASD / 点击", "行军(官道快,林慢,河找渡滩;夜行慢且倍疲)");
		K("C / R", "扎营入中军帐 / 歇营过夜(黄昏后:养力省粮回心神)");
		K("E(近帅帐)", "谒见周帅:领军令、领粮(小心粮官的『鼠耗』)");
		y += 8;

		Sec("中军帐 · 营区(操纵将军走动;战事照常推进)");
		K("E", "交互:沙盘=推演 · 军令案=读行营文书 · 瞭望台=登台");
		K("F1", "低难度开关:沙盘直接叠加瞭望所见");
		y += 8;

		Sec("沙盘(战斗指挥——你只看得见『送到手上的信息』)");
		K("空格 / + -", "暂停 / 调速(收到要情自动暂停)");
		K("左键 / 右键", "选部 / 令骑传令行军(Shift=疾进;武将会按脾性解读)");
		K("1 2 3 4", "姿态令(经令骑):进攻 / 据守 / 等待 / 游走(风筝)");
		K("5 6 7", "旗鼓(声程内即时·绝对照令·敌亦闻):擂鼓进 / 鸣金退 / 竖旗守");
		K("中键 / Shift+中键", "塘骑侦察 / 遣疑兵虚张声势(诱虏扑空,一战两拨)");
		K("R / B", "探问该部近况 / 军书具报行营(等回执才算送到)");
		K("Ctrl+左键 / Esc", "插信息旗(Ctrl+右键拔) / 回中军帐");
		K("战毕:回车 / P", "班师回大地图 / 复盘对照(认知↔真相,看你错在哪)");

		T(80, 720, "Esc / 回车 · 返回", 14, "e6c25c");
	}
}

/// <summary>
/// 场景内暂停菜单(大地图/中军帐共用):继续 / 操作说明 / 保存并回主菜单 / 退出。
/// 用法:_Input 里先喂给 HandleKey;Open 时场景应停住自己的世界推进。
/// </summary>
public sealed class PauseOverlay
{
	public bool Open;
	private int _sel;
	private bool _help;

	public enum Act { None, SaveToTitle, Quit }

	private static readonly string[] Items = { "继续", "操作说明", "保存并回主菜单", "退出游戏" };

	/// <summary>返回要场景执行的动作;吃掉的按键返回 None 且场景不应再处理。</summary>
	public Act HandleKey(InputEventKey k, out bool consumed)
	{
		consumed = false;
		if (!Open)
		{
			if (k.Keycode == Key.Escape) { Open = true; _sel = 0; _help = false; consumed = true; }
			return Act.None;
		}
		consumed = true;
		if (_help)
		{
			if (k.Keycode is Key.Escape or Key.Enter or Key.KpEnter) _help = false;
			return Act.None;
		}
		switch (k.Keycode)
		{
			case Key.Escape: Open = false; break;
			case Key.W or Key.Up: _sel = (_sel + Items.Length - 1) % Items.Length; break;
			case Key.S or Key.Down: _sel = (_sel + 1) % Items.Length; break;
			case Key.Enter or Key.KpEnter:
				switch (_sel)
				{
					case 0: Open = false; break;
					case 1: _help = true; break;
					case 2: Open = false; return Act.SaveToTitle;
					case 3: return Act.Quit;
				}
				break;
		}
		return Act.None;
	}

	public void Draw(CanvasItem c, Font f)
	{
		if (!Open) return;
		if (_help) { UiKit.DrawHelp(c, f); return; }

		c.DrawRect(new Rect2(0, 0, 1120, 760), new Color(0, 0, 0, 0.62f), true);
		float w = 320, h = 90 + Items.Length * 40;
		var box = new Rect2((1120 - w) / 2, (760 - h) / 2, w, h);
		c.DrawRect(box, new Color(0.10f, 0.09f, 0.07f, 0.97f), true);
		c.DrawRect(box, new Color("d9b34a"), false, 2f);
		c.DrawString(f, box.Position + new Vector2(0, 44), "军 议 暂 歇", HorizontalAlignment.Center, w, 20, new Color("e6c25c"));

		for (int i = 0; i < Items.Length; i++)
		{
			bool on = i == _sel;
			string label = Items[i];
			if (i == 2 && GameState.I is { BattleActive: true }) label = "回主菜单(战中不存档)";
			var pos = box.Position + new Vector2(0, 86 + i * 40);
			if (on) c.DrawString(f, pos + new Vector2(-110, 0), "▶", HorizontalAlignment.Center, w, 15, new Color("e6c25c"));
			c.DrawString(f, pos, label, HorizontalAlignment.Center, w, 15,
				on ? new Color("f2e6c8") : new Color("9a948a"));
		}
	}
}
