using System;
using System.Collections.Generic;

namespace CommandPost.Core;

/// <summary>连续世界坐标(米)。战斗B档(逐兵模拟)专用;格子层原型的 Vec2 保持不动。</summary>
public readonly struct Vec2F
{
    public readonly float X, Y;
    public Vec2F(float x, float y) { X = x; Y = y; }
    public static Vec2F operator +(Vec2F a, Vec2F b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2F operator -(Vec2F a, Vec2F b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2F operator *(Vec2F a, float k) => new(a.X * k, a.Y * k);
    public float Length => MathF.Sqrt(X * X + Y * Y);
    public float LengthSq => X * X + Y * Y;
    public Vec2F Normalized { get { float l = Length; return l < 1e-6f ? new Vec2F(1, 0) : new Vec2F(X / l, Y / l); } }
    public float DistanceTo(Vec2F o) => (this - o).Length;
    public static Vec2F Lerp(Vec2F a, Vec2F b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    public float Dot(Vec2F o) => X * o.X + Y * o.Y;
    /// <summary>垂直向量(阵列横向展开用)。</summary>
    public Vec2F Perp => new(-Y, X);
    public override string ToString() => $"({X:0},{Y:0})";
}

/// <summary>战场地貌。草原/官道/黑松林/丘陵/河/渡滩。</summary>
public enum BTerrain { Grass, Road, Forest, Hill, River, Ford }

/// <summary>
/// 战场地形图:方格瓦片(16m)承载地貌,移动/视认在连续坐标上取所在瓦片修正。
/// 可代码手绘(Paint)或 ASCII 载入(. 草 D 路 F 林 H 丘 R 河 f 滩)——
/// 后续接 Tiled / LDtk 的 CSV/IntGrid 导出只是一层 20 行的转换。
/// </summary>
public sealed class BattleMap
{
    public const float TileSize = 16f;
    public int W { get; }
    public int H { get; }
    public float WorldW => W * TileSize;
    public float WorldH => H * TileSize;
    private readonly BTerrain[,] _t;

    public BattleMap(int w, int h, BTerrain fill = BTerrain.Grass)
    {
        W = w; H = h;
        _t = new BTerrain[w, h];
        if (fill != BTerrain.Grass)
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) _t[x, y] = fill;
    }

    public static BattleMap FromAscii(string ascii)
    {
        var lines = ascii.Replace("\r", "").Trim('\n').Split('\n');
        var m = new BattleMap(lines[0].Length, lines.Length);
        for (int y = 0; y < m.H; y++)
            for (int x = 0; x < m.W && x < lines[y].Length; x++)
                m._t[x, y] = lines[y][x] switch
                {
                    'D' => BTerrain.Road, 'F' => BTerrain.Forest, 'H' => BTerrain.Hill,
                    'R' => BTerrain.River, 'f' => BTerrain.Ford, _ => BTerrain.Grass
                };
        return m;
    }

    public void Paint(int x0, int y0, int x1, int y1, BTerrain t)
    {
        for (int x = Math.Max(0, x0); x <= Math.Min(W - 1, x1); x++)
            for (int y = Math.Max(0, y0); y <= Math.Min(H - 1, y1); y++)
                _t[x, y] = t;
    }

    public BTerrain AtTile(int tx, int ty) =>
        _t[Math.Clamp(tx, 0, W - 1), Math.Clamp(ty, 0, H - 1)];
    public BTerrain At(Vec2F p) => AtTile((int)(p.X / TileSize), (int)(p.Y / TileSize));

    public static bool Passable(BTerrain t) => t != BTerrain.River;
    /// <summary>移动速度倍率:路快、林慢、滩涉水最慢、河不可过。</summary>
    public static float SpeedMult(BTerrain t) => t switch
    {
        BTerrain.Road => 1.25f, BTerrain.Forest => 0.6f, BTerrain.Hill => 0.75f,
        BTerrain.Ford => 0.45f, BTerrain.River => 0f, _ => 1f
    };
    /// <summary>视认倍率(按目标所在地形):林中难见。</summary>
    public static float ConcealMult(BTerrain t) => t == BTerrain.Forest ? 0.45f : 1f;

    public Vec2F Clamp(Vec2F p) =>
        new(Math.Clamp(p.X, 2, WorldW - 2), Math.Clamp(p.Y, 2, WorldH - 2));

    /// <summary>目标落在河里时,环搜最近可通行点。</summary>
    public Vec2F NearestPassable(Vec2F p)
    {
        if (Passable(At(p))) return p;
        int cx = (int)(p.X / TileSize), cy = (int)(p.Y / TileSize);
        for (int r = 1; r < Math.Max(W, H); r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    int tx = cx + dx, ty = cy + dy;
                    if (tx < 0 || tx >= W || ty < 0 || ty >= H) continue;
                    if (Passable(_t[tx, ty]))
                        return new Vec2F((tx + 0.5f) * TileSize, (ty + 0.5f) * TileSize);
                }
        return p;
    }
}

/// <summary>瓦片级 A*(8向,代价=步长/速度倍率;河不可过、滩可涉)。部队/令骑共用。</summary>
public static class BattlePath
{
    public static List<Vec2F> Find(BattleMap map, Vec2F from, Vec2F to)
    {
        to = map.NearestPassable(map.Clamp(to));
        int sx = (int)(from.X / BattleMap.TileSize), sy = (int)(from.Y / BattleMap.TileSize);
        int tx = (int)(to.X / BattleMap.TileSize), ty = (int)(to.Y / BattleMap.TileSize);
        sx = Math.Clamp(sx, 0, map.W - 1); sy = Math.Clamp(sy, 0, map.H - 1);
        tx = Math.Clamp(tx, 0, map.W - 1); ty = Math.Clamp(ty, 0, map.H - 1);

        var g = new float[map.W, map.H];
        var came = new (int x, int y)[map.W, map.H];
        var open = new List<(int x, int y)>();
        for (int x = 0; x < map.W; x++) for (int y = 0; y < map.H; y++) g[x, y] = float.MaxValue;
        g[sx, sy] = 0; open.Add((sx, sy));

        static float Heu(int x, int y, int tx, int ty)
        { int dx = Math.Abs(x - tx), dy = Math.Abs(y - ty); return Math.Max(dx, dy) + 0.41f * Math.Min(dx, dy); }

        bool found = false;
        while (open.Count > 0)
        {
            int best = 0; float bf = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                var (ox, oy) = open[i];
                float f = g[ox, oy] + Heu(ox, oy, tx, ty);
                if (f < bf) { bf = f; best = i; }
            }
            var (cx, cy) = open[best];
            open.RemoveAt(best);
            if (cx == tx && cy == ty) { found = true; break; }

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || nx >= map.W || ny < 0 || ny >= map.H) continue;
                    var t = map.AtTile(nx, ny);
                    if (!BattleMap.Passable(t)) continue;
                    float step = (dx != 0 && dy != 0 ? 1.41f : 1f) / BattleMap.SpeedMult(t);
                    float ng = g[cx, cy] + step;
                    if (ng < g[nx, ny])
                    {
                        g[nx, ny] = ng; came[nx, ny] = (cx, cy);
                        if (!open.Contains((nx, ny))) open.Add((nx, ny));
                    }
                }
        }

        var path = new List<Vec2F>();
        if (!found) { path.Add(to); return path; }
        var (px, py) = (tx, ty);
        while (!(px == sx && py == sy))
        {
            path.Add(new Vec2F((px + 0.5f) * BattleMap.TileSize, (py + 0.5f) * BattleMap.TileSize));
            (px, py) = came[px, py];
        }
        path.Reverse();
        // 去共线航点,末点替换为精确目的地
        for (int i = path.Count - 2; i >= 1; i--)
        {
            var a = (path[i] - path[i - 1]).Normalized;
            var b = (path[i + 1] - path[i]).Normalized;
            if (a.Dot(b) > 0.999f) path.RemoveAt(i);
        }
        if (path.Count > 0) path[^1] = to; else path.Add(to);
        return path;
    }
}

/// <summary>兵种战斗参数表(逐兵层)。克制沿用 Unit.TypeMatchup;数值实验档,待调。</summary>
public static class BArms
{
    /// <summary>行走速度 m/s(奔跑 ×1.5)。</summary>
    public static float SpeedOf(UnitType t) => t switch
    {
        UnitType.HorseArcher => 7f,
        UnitType.Cavalry or UnitType.NomadLancer => 6.5f,
        UnitType.Cataphract => 5f,
        UnitType.TribalFoot => 1.8f,
        _ => 1.6f
    };

    /// <summary>护甲除数:受伤 = 伤害 / 护甲。</summary>
    public static float ArmorOf(UnitType t) => t switch
    {
        UnitType.Cataphract => 1.7f, UnitType.Shield => 1.6f, UnitType.MoDao => 1.3f,
        UnitType.Spear => 1.25f, UnitType.Cavalry or UnitType.NomadLancer => 1.0f,
        UnitType.TribalFoot => 0.95f, UnitType.HorseArcher => 0.9f, UnitType.Bow => 0.75f, _ => 1f
    };

    public static bool Ranged(UnitType t) => t is UnitType.Bow or UnitType.HorseArcher;
    /// <summary>射程(米)。弩(Bow)远而慢装,骑射近而快装。</summary>
    public static float RangeOf(UnitType t) => t switch { UnitType.Bow => 160f, UnitType.HorseArcher => 120f, _ => 0f };
    public static float RangedDamage(UnitType t) => t == UnitType.Bow ? 58f : 42f;
    public static (float min, float max) ReloadOf(UnitType t) => t == UnitType.Bow ? (8f, 11f) : (5f, 7.5f);
    public static int AmmoOf(UnitType t) => t == UnitType.Bow ? 20 : 25;

    public static bool IsCav(UnitType t) => t is UnitType.Cavalry or UnitType.NomadLancer or UnitType.Cataphract or UnitType.HorseArcher;
    /// <summary>近战出手基础伤害(未计克制/护甲/冲锋)。</summary>
    public const float MeleeBase = 34f;
    /// <summary>武器可及(米)。</summary>
    public static float ReachOf(UnitType t) => IsCav(t) ? 2.0f : 1.6f;
    /// <summary>阵列间距(米)。</summary>
    public static float SpacingOf(UnitType t) => IsCav(t) ? 2.4f : 1.15f;
}
