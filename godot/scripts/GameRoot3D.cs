using Godot;
using CommandPost.Core;
using System.Collections.Generic;
using Side = CommandPost.Core.Side;

/// <summary>
/// M2-3D 第一步:把战中沙盘搬进 3D(基本体试水)。复用引擎无关内核 CommandPost.Core,
/// 只换渲染:3D 桌面沙盘 + 相机 + 实时时钟 + 立体棋子。只读认知世界(Beliefs[Friend]),不读真相。
/// 本步「只看不点」——确认 3D 管线/相机/实时;拾取、下令、旗鼓、望楼随后逐层加。
/// </summary>
public partial class GameRoot3D : Node3D
{
	private Simulation _sim = null!;

	// 实时时钟
	private bool _paused = true;
	private double _tickAccum;
	private int _speedIdx = 1;
	private static readonly double[] Speeds = { 0.5, 1, 2, 4 };
	private const double BaseTicksPerSec = 2.5;

	private readonly Dictionary<int, MeshInstance3D> _tokens = new();
	private Label _hud = null!;
	private StandardMaterial3D _matFriend = null!, _matEnemy = null!, _matGhost = null!;

	public override void _Ready()
	{
		_sim = Scenario.MinimalEncounter(DifficultySettings.Normal);
		BuildEnvironment();
		BuildTerrain();
		BuildHqMarkers();

		// 临时 demo 命令:三路压上(本步没有拾取下令,先让战场动起来;下一步换成鼠标下令)
		_sim.IssueIntent(2, Intent.Move(new Vec2(16, 6)));
		_sim.IssueIntent(1, Intent.Move(new Vec2(16, 3), Tone.Aggressive));
		_sim.IssueIntent(3, Intent.Move(new Vec2(16, 9), Tone.Cautious));
	}

	private void BuildEnvironment()
	{
		var grid = _sim.Truth.Terrain;
		float cx = grid.Width / 2f, cz = grid.Height / 2f;

		var cam = new Camera3D();
		AddChild(cam);
		cam.LookAtFromPosition(new Vector3(cx, 20, cz + 18), new Vector3(cx, 0, cz - 1));

		var light = new DirectionalLight3D { RotationDegrees = new Vector3(-55, -40, 0) };
		AddChild(light);

		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color("14110c"),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color("454545")
		};
		AddChild(new WorldEnvironment { Environment = env });

		_matFriend = new StandardMaterial3D { AlbedoColor = new Color("b23b2e") };
		_matEnemy = new StandardMaterial3D { AlbedoColor = new Color("8a8f99") };
		_matGhost = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.55f, 0.55f, 0.62f, 0.45f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};

		var sf = new SystemFont();
		sf.FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun" };
		var layer = new CanvasLayer();
		_hud = new Label { Position = new Vector2(16, 12) };
		_hud.AddThemeFontOverride("font", sf);
		_hud.AddThemeFontSizeOverride("font_size", 16);
		_hud.AddThemeColorOverride("font_color", new Color("e8e0d0"));
		layer.AddChild(_hud);
		AddChild(layer);
	}

	private void BuildTerrain()
	{
		var grid = _sim.Truth.Terrain;
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new BoxMesh { Size = new Vector3(0.96f, 0.1f, 0.96f) }
		};
		mm.InstanceCount = grid.Width * grid.Height;
		int i = 0;
		for (int z = 0; z < grid.Height; z++)
			for (int x = 0; x < grid.Width; x++)
			{
				mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(x, 0, z)));
				mm.SetInstanceColor(i, grid.At(new Vec2(x, z)) switch
				{
					TerrainType.Forest => new Color("3a4d2b"),
					TerrainType.River => new Color("2d4d5e"),
					_ => new Color("4a4332")
				});
				i++;
			}
		AddChild(new MultiMeshInstance3D
		{
			Multimesh = mm,
			MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true }
		});
	}

	private void BuildHqMarkers()
	{
		AddChild(Marker(_sim.Truth.FriendHq, new Color("d9b34a")));
		AddChild(Marker(_sim.Truth.EnemyHq, new Color("8a8a8a")));
	}

	private static MeshInstance3D Marker(Vec2 p, Color c) => new()
	{
		Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.7f, 0.7f) },
		Position = new Vector3(p.X, 0.4f, p.Y),
		MaterialOverride = new StandardMaterial3D { AlbedoColor = c }
	};

	public override void _Process(double delta)
	{
		if (!_paused && _sim.Status == GameStatus.Ongoing)
		{
			_tickAccum += delta * BaseTicksPerSec * Speeds[_speedIdx];
			int guard = 0;
			while (_tickAccum >= 1.0 && _sim.Status == GameStatus.Ongoing && guard++ < 50)
			{
				_sim.AdvanceTick();
				_tickAccum -= 1.0;
			}
		}
		UpdateTokens();
		UpdateHud();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } k) return;
		switch (k.Keycode)
		{
			case Key.Space: _paused = !_paused; break;
			case Key.Equal: if (_speedIdx < Speeds.Length - 1) _speedIdx++; break;
			case Key.Minus: if (_speedIdx > 0) _speedIdx--; break;
			case Key.N: if (_sim.Status == GameStatus.Ongoing) _sim.AdvanceTick(); break;
		}
	}

	private void UpdateTokens()
	{
		var belief = _sim.Beliefs[Side.Friend];
		int now = _sim.Truth.Tick;
		var seen = new HashSet<int>();
		foreach (var g in belief.KnownOf(Side.Friend)) { seen.Add(g.UnitId); Place(g.UnitId, g.LastKnownPos, _matFriend); }
		foreach (var g in belief.KnownOf(Side.Enemy)) { seen.Add(g.UnitId); Place(g.UnitId, g.LastKnownPos, g.AgeAt(now) > 8 ? _matGhost : _matEnemy); }
		foreach (var kv in _tokens) kv.Value.Visible = seen.Contains(kv.Key);
	}

	private void Place(int id, Vec2 pos, StandardMaterial3D mat)
	{
		if (!_tokens.TryGetValue(id, out var node))
		{
			node = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.34f, BottomRadius = 0.34f, Height = 0.8f } };
			AddChild(node);
			_tokens[id] = node;
		}
		node.Visible = true;
		node.MaterialOverride = mat;
		node.Position = new Vector3(pos.X, 0.45f, pos.Y);
	}

	private void UpdateHud()
	{
		string clock = _paused ? "[暂停]" : $"[▶ {Speeds[_speedIdx]:0.#}x]";
		_hud.Text = $"中军帐 · 3D 原型(基本体试水)   t{_sim.Truth.Tick}  {clock}  {StatusCn(_sim.Status)}\n"
				  + "空格 暂停/继续 · -/+ 调速 · N 单步      (本步只看不点:验 3D 管线/相机/实时;拾取·下令·旗鼓·望楼随后)";
	}

	private static string StatusCn(GameStatus s) => s switch
	{
		GameStatus.FriendWon => "我军胜", GameStatus.EnemyWon => "我军败", _ => "进行中"
	};
}
