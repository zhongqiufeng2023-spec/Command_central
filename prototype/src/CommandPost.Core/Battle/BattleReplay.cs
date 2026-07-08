using System;
using System.Collections.Generic;

namespace CommandPost.Core;

// 战后复盘(PRD §10):战中逐秒录「真相帧 + 认知帧」,战毕对照回放——
// 上帝视角只在战后给;「原来如此」和「原来是我害的」都在这里兑现。

/// <summary>一支部队在某秒的真相快照。</summary>
public readonly record struct BReplayUnit(int Id, Side Side, bool Allied, Vec2F Pos, int Alive, BUnitState State);

/// <summary>骑手在某秒的真相快照(战中你看不到他们;复盘才显形——含被截杀的)。</summary>
public readonly record struct BReplayRider(Vec2F Pos, RiderKind Kind, bool Lost);

/// <summary>沙盘标记在某秒的样子(认知侧):Id 对应部队;T=情报采集时刻。</summary>
public readonly record struct BReplayMark(int Id, Vec2F Pos, int Est, float T);

/// <summary>一帧 = 同一秒的「战场真相」与「你沙盘上以为的」。</summary>
public sealed class BReplayFrame
{
    public float T;
    public List<BReplayUnit> Units = new();
    public List<BReplayRider> Riders = new();
    public List<BReplayMark> BeliefEnemy = new();
    public List<BReplayMark> BeliefOwn = new();
    public List<BReplayMark> BeliefAlly = new();
}

/// <summary>复盘的「关键分叉点」:你以为的和实际的差得最远的时刻。</summary>
public sealed class BKeyMoment
{
    public float T;
    public string Cn = "";
    public Vec2F BeliefAt, TruthAt;
}

/// <summary>整场战役的回放胶卷。</summary>
public sealed class BattleReplay
{
    public List<BReplayFrame> Frames = new();
    public float Duration => Frames.Count > 0 ? Frames[^1].T : 0f;

    public BReplayFrame? At(float t)
    {
        if (Frames.Count == 0) return null;
        int idx = Math.Clamp((int)t, 0, Frames.Count - 1);       // 帧距 1s
        // 帧距未必严格 1s(战毕补帧),就近微调
        while (idx > 0 && Frames[idx].T > t) idx--;
        while (idx < Frames.Count - 1 && Frames[idx + 1].T <= t) idx++;
        return Frames[idx];
    }

    /// <summary>
    /// 找认知与真相的关键分叉:沙盘敌情标记与该部实际位置差得离谱的时刻。
    /// 每部只记它差得最远的一刻,按偏差取前几名。
    /// </summary>
    public List<BKeyMoment> Analyze(Func<int, string> nameOf, int top = 3)
    {
        var best = new Dictionary<int, BKeyMoment>();
        var bestD = new Dictionary<int, float>();
        foreach (var f in Frames)
        {
            foreach (var mark in f.BeliefEnemy)
            {
                foreach (var u in f.Units)
                {
                    if (u.Id != mark.Id || u.Alive == 0) continue;
                    float d = mark.Pos.DistanceTo(u.Pos);
                    float age = f.T - mark.T;
                    if (d < 130f || age < 15f) continue;                 // 差得不远/情报还新,不算分叉
                    if (!bestD.TryGetValue(mark.Id, out var bd) || d > bd)
                    {
                        bestD[mark.Id] = d;
                        best[mark.Id] = new BKeyMoment
                        {
                            T = f.T, BeliefAt = mark.Pos, TruthAt = u.Pos,
                            Cn = $"{BattleSim.FormatT(f.T)} 沙盘上「{nameOf(mark.Id)}」的标记已旧 {(int)age}s——" +
                                 $"你以为它在原地,实际已移 {(int)d} 米"
                        };
                    }
                }
            }
        }
        var list = new List<BKeyMoment>(best.Values);
        list.Sort((a, b) =>
        {
            float da = a.BeliefAt.DistanceTo(a.TruthAt), db = b.BeliefAt.DistanceTo(b.TruthAt);
            return db.CompareTo(da);
        });
        if (list.Count > top) list.RemoveRange(top, list.Count - top);
        list.Sort((a, b) => a.T.CompareTo(b.T));
        return list;
    }
}
