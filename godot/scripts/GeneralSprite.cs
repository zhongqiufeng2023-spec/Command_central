using Godot;

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
			var img = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://assets/general.png"));
			KeyOutWhite(img);
			_tex = ImageTexture.CreateFromImage(img);
			return _tex;
		}
	}

	/// <summary>白底抠透明(精灵表是白底黑边像素画)。</summary>
	private static void KeyOutWhite(Image img)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = img.GetPixel(x, y);
				if (c.R > 0.90f && c.G > 0.90f && c.B > 0.90f)
					img.SetPixel(x, y, new Color(0, 0, 0, 0));
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

	/// <summary>画在 topLeft 处,尺寸 w×h;Left 自动镜像。</summary>
	public static void Draw(CanvasItem c, Vector2 topLeft, float w, float h, Dir d, float phase, bool moving)
	{
		var src = Frame(d, phase, moving);
		if (d == Dir.Left)
		{
			c.DrawSetTransform(new Vector2(topLeft.X + w / 2f, topLeft.Y), 0, new Vector2(-1, 1));
			c.DrawTextureRectRegion(Tex, new Rect2(-w / 2f, 0, w, h), src);
			c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
		}
		else
			c.DrawTextureRectRegion(Tex, new Rect2(topLeft, new Vector2(w, h)), src);
	}
}
