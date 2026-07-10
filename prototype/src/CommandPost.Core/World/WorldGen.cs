using System;

namespace CommandPost.Core;

/// <summary>
/// 战略层地貌 id(字节码,与大地图渲染层对齐)。
/// 0-9 沿用大地图既有编码;10 丘陵、11 泽地为热力图新增。
/// </summary>
public enum WTerrain : byte
{
    Plain = 0, Forest = 1, River = 2, Ford = 3, Road = 4,
    Mountain = 5, Camp = 6, FoeCamp = 7, Houses = 8, Miasma = 9,
    Hill = 10, Marsh = 11
}

/// <summary>遭遇战场的地形类别(战略地貌 → 战术地图的桥)。</summary>
public enum BattleGround { Plain, Forest, Hills, Marsh }

/// <summary>
/// 战略层世界地貌生成:确定性值噪声热力图(高度场+湿度场)→ 地形分类。
/// 高度即热力图——渲染层可直接取 Height 做明暗/调试视图;
/// 手绘要素(官道/河渡/营帐/瘴土)由调用方在分类结果上覆盖。
/// </summary>
public static class WorldGen
{
    // —— 确定性散列噪声(与平台/运行次序无关)——
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 974634599);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>单层值噪声:格点散列 + 平滑双线性。</summary>
    public static float ValueNoise(float x, float y, int seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);                       // smoothstep
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed);
        float c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>分形噪声(fBm):多层叠加出山势起伏。</summary>
    public static float Fbm(float x, float y, int seed, int octaves = 4)
    {
        float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += ValueNoise(x * freq, y * freq, seed + i * 7919) * amp;
            norm += amp;
            amp *= 0.5f; freq *= 2f;
        }
        return sum / norm;
    }

    /// <summary>生成一张 [0,1] 归一化热力场(min-max 拉伸,阈值分类才稳)。</summary>
    public static float[,] Field(int seed, int w, int h, float scale = 0.045f, int octaves = 4)
    {
        var f = new float[w, h];
        float min = float.MaxValue, max = float.MinValue;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float v = Fbm(x * scale, y * scale, seed, octaves);
                f[x, y] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        float span = MathF.Max(1e-6f, max - min);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                f[x, y] = (f[x, y] - min) / span;
        return f;
    }

    /// <summary>高度×湿度 → 地貌:高处成山、次高成丘、低湿成泽、湿处成林,余为草原。</summary>
    public static WTerrain Classify(float height, float moisture)
    {
        if (height > 0.78f) return WTerrain.Mountain;
        if (height > 0.64f) return WTerrain.Hill;
        if (height < 0.30f && moisture > 0.62f) return WTerrain.Marsh;
        if (moisture > 0.55f) return WTerrain.Forest;
        return WTerrain.Plain;
    }

    /// <summary>
    /// 生成整张战略地貌(确定性):height 输出高度热力图供渲染层做明暗。
    /// 只产自然地貌;道路/河渡/营帐/瘴土等人文与剧设要素由调用方覆盖。
    /// </summary>
    public static byte[,] Terrain(int seed, int w, int h, out float[,] height)
    {
        height = Field(seed, w, h, 0.038f);
        var moist = Field(seed + 7777, w, h, 0.055f);
        var t = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                t[x, y] = (byte)Classify(height[x, y], moist[x, y]);
        return t;
    }

    /// <summary>战略地貌 → 遭遇战地形类别(在哪儿接战,就在哪儿的地上打)。</summary>
    public static BattleGround GroundOf(byte terrain) => (WTerrain)terrain switch
    {
        WTerrain.Forest => BattleGround.Forest,
        WTerrain.Mountain or WTerrain.Hill => BattleGround.Hills,
        WTerrain.Marsh or WTerrain.Miasma => BattleGround.Marsh,
        _ => BattleGround.Plain
    };

    /// <summary>地形类别名(战报/军书用)。</summary>
    public static string GroundCn(BattleGround g) => g switch
    {
        BattleGround.Forest => "林地", BattleGround.Hills => "丘陵",
        BattleGround.Marsh => "泽地", _ => "平野"
    };
}
