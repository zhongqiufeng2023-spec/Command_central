using Godot;
using System;

/// <summary>
/// 将军像素立绘(assets/general.png,8列×4行帧表,单元 128×256):
///   行0 = 正面行走 6 帧 · 行1 = 侧面奔跑 6 帧 + 正面立姿 2 · 行2 = 背面行走 6 帧 + 持械侧姿 2 · 行3 = 正面踏步 6 帧 + 持械 2。
/// 运行时 Image.LoadFromFile 加载(绕过导入管线,命令行直跑也能用)+ 白底转透明。
/// 左向 = 右向镜像(DrawSetTransform 翻转)。调用方需把节点 TextureFilter 设为 Nearest 保像素感。
/// </summary>
public static class GeneralSprite
{
	public const int CellW = 128, CellH = 256;
	private static ImageTexture? _tex;

	public static Texture2D Tex
	{
		get
		{
			if (_tex != null) return _tex;
			// 三级回退:①用户自制素材(assets/,将来编辑器里做的放这) ②仓库自带 src/将军.png ③程序化替身(永不崩)
			string projDir = ProjectSettings.GlobalizePath("res://");
			var candidates = new[]
			{
				System.IO.Path.Combine(projDir, "assets", "general.png"),
				System.IO.Path.Combine(projDir, "assets", "将军.png"),
				System.IO.Path.GetFullPath(System.IO.Path.Combine(projDir, "..", "src", "将军.png")),
			};
			foreach (var p in candidates)
			{
				if (!System.IO.File.Exists(p)) continue;
				var img = Image.LoadFromFile(p);
				if (img == null) continue;
				KeyOutWhite(img);
				_tex = ImageTexture.CreateFromImage(img);
				return _tex;
			}
			_tex = ImageTexture.CreateFromImage(MakeFallbackSheet());
			return _tex;
		}
	}

	/// <summary>找不到立绘时的程序化替身帧表(暗红披风小人):游戏照跑,等用户放上自己的素材。</summary>
	private static Image MakeFallbackSheet()
	{
		var img = Image.CreateEmpty(CellW * 8, CellH * 4, false, Image.Format.Rgba8);
		var armor = new Color(0.45f, 0.17f, 0.13f);
		var trim = new Color(0.85f, 0.70f, 0.30f);
		for (int cy = 0; cy < 4; cy++)
			for (int cx = 0; cx < 8; cx++)
			{
				int ox = cx * CellW, oy = cy * CellH;
				int bob = cx % 2 == 0 ? 0 : 6;                       // 走路上下颠一颠
				for (int y = 60 + bob; y < 236; y++)
					for (int x = 34; x < 94; x++)
						img.SetPixel(ox + x, oy + y, armor);
				for (int y = 24 + bob; y < 60 + bob; y++)             // 头
					for (int x = 46; x < 82; x++)
						img.SetPixel(ox + x, oy + y, new Color(0.80f, 0.62f, 0.48f));
				for (int y = 96 + bob; y < 104 + bob; y++)            // 束带
					for (int x = 34; x < 94; x++)
						img.SetPixel(ox + x, oy + y, trim);
			}
		return img;
	}

	/// <summary>白底抠透明 + 白边羽化:低饱和的亮像素按「接近白的程度」渐隐并压暗,
	/// 抗锯齿留下的灰白毛边一并处理;皮肤/金甲/红披风饱和度高,不受影响。</summary>
	private static void KeyOutWhite(Image img)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = img.GetPixel(x, y);
				float maxc = Math.Max(c.R, Math.Max(c.G, c.B));
				float minc = Math.Min(c.R, Math.Min(c.G, c.B));
				float sat = maxc <= 0.001f ? 0f : (maxc - minc) / maxc;
				if (sat < 0.28f && minc > 0.62f)
				{
					float a = Mathf.Clamp((0.80f - minc) / 0.18f, 0f, 1f);   // ≥0.80 全透;0.62~0.80 渐隐
					img.SetPixel(x, y, new Color(c.R * 0.82f, c.G * 0.82f, c.B * 0.82f, c.A * a));
				}
			}
	}

	public enum Dir { Down, Up, Right, Left }

	/// <summary>取一帧源区。phase ∈ [0,1) 走一个循环;moving=false 取立姿。</summary>
	public static Rect2 Frame(Dir d, float phase, bool moving)
	{
		int f = (int)(phase * 6) % 6;
		return d switch
		{
			Dir.Down => moving ? Cell(f, 0) : Cell(6, 1),          // 正面:行0;立姿用行1第7格
			Dir.Up => moving ? Cell(f, 2) : Cell(0, 2),            // 背面:行2
			_ => moving ? Cell(f, 1) : Cell(0, 1),                 // 侧面:行1奔跑
		};
	}
	private static Rect2 Cell(int cx, int cy) => new(cx * CellW, cy * CellH, CellW, CellH);

	/// <summary>画在 topLeft 处,尺寸 w×h。精灵表侧向帧原生朝左 → Right 镜像、Left 原样。</summary>
	public static void Draw(CanvasItem c, Vector2 topLeft, float w, float h, Dir d, float phase, bool moving)
	{
		var src = Frame(d, phase, moving);
		if (d == Dir.Right)
		{
			c.DrawSetTransform(new Vector2(topLeft.X + w / 2f, topLeft.Y), 0, new Vector2(-1, 1));
			c.DrawTextureRectRegion(Tex, new Rect2(-w / 2f, 0, w, h), src);
			c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
		}
		else
			c.DrawTextureRectRegion(Tex, new Rect2(topLeft, new Vector2(w, h)), src);
	}
}
