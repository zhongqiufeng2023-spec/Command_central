using Godot;
using System;

/// <summary>
/// 程序化音效(无外部素材):启动时合成 16-bit PCM 存成 AudioStreamWav,即点即播。
/// 战鼓 / 号角 / 军报铜哨 / 马蹄 / 竹简翻动 / 胜·败尾声。音量整体收着(-8dB 起)。
/// </summary>
public static class Sfx
{
	private const int Rate = 22050;
	private static readonly Random Noise = new(20260705);

	private static AudioStreamWav? _drum, _horn, _alert, _gallop, _click, _win, _lose;

	public static AudioStreamWav Drum => _drum ??= Make(0.55f, t =>
	{
		float body = MathF.Sin(MathF.Tau * (66f - 18f * t) * t) * MathF.Exp(-t * 7f);
		float slap = Rand() * MathF.Exp(-t * 55f) * 0.55f;
		return (body + slap) * 0.95f;
	});

	public static AudioStreamWav Horn => _horn ??= Make(1.1f, t =>
	{
		float env = MathF.Min(t * 8f, 1f) * MathF.Exp(-MathF.Max(0, t - 0.55f) * 4f);
		float f = 196f * (1f + 0.02f * MathF.Sin(MathF.Tau * 5.2f * t));
		float v = Saw(f * t) * 0.5f + Saw(f * 1.5f * t) * 0.28f + MathF.Sin(MathF.Tau * f * 2f * t) * 0.14f;
		return v * env * 0.6f;
	});

	public static AudioStreamWav Alert => _alert ??= Make(0.5f, t =>
	{
		float n1 = MathF.Sin(MathF.Tau * 880f * t) * MathF.Exp(-t * 10f);
		float n2 = t > 0.16f ? MathF.Sin(MathF.Tau * 1174f * (t - 0.16f)) * MathF.Exp(-(t - 0.16f) * 10f) : 0f;
		return (n1 + n2) * 0.4f;
	});

	private static readonly float[] Hoofs = { 0f, 0.13f, 0.22f, 0.35f };
	public static AudioStreamWav Gallop => _gallop ??= Make(0.5f, t =>
	{
		float v = 0f;
		foreach (float o in Hoofs)
			if (t >= o) v += MathF.Sin(MathF.Tau * 120f * (t - o)) * MathF.Exp(-(t - o) * 38f);
		return v * 0.55f;
	});

	public static AudioStreamWav Click => _click ??= Make(0.05f, t =>
		Rand() * MathF.Exp(-t * 130f) * 0.5f + MathF.Sin(MathF.Tau * 1600f * t) * MathF.Exp(-t * 90f) * 0.3f);

	private static readonly float[] WinNotes = { 262f, 330f, 392f, 523f };
	public static AudioStreamWav Win => _win ??= Make(1.3f, t =>
	{
		float v = 0f;
		for (int i = 0; i < WinNotes.Length; i++)
		{
			float o = i * 0.18f;
			if (t >= o) v += MathF.Sin(MathF.Tau * WinNotes[i] * (t - o)) * MathF.Exp(-(t - o) * 3.4f) * 0.3f;
		}
		return v;
	});

	private static readonly float[] LoseNotes = { 311f, 262f, 208f };
	public static AudioStreamWav Lose => _lose ??= Make(1.5f, t =>
	{
		float v = 0f;
		for (int i = 0; i < LoseNotes.Length; i++)
		{
			float o = i * 0.3f;
			if (t >= o) v += (MathF.Sin(MathF.Tau * LoseNotes[i] * (t - o)) + 0.5f * MathF.Sin(MathF.Tau * LoseNotes[i] * 0.5f * (t - o)))
							 * MathF.Exp(-(t - o) * 2.6f) * 0.26f;
		}
		return v;
	});

	/// <summary>播一响:临时挂一个 AudioStreamPlayer,放完自清。</summary>
	public static void Play(Node host, AudioStreamWav s, float volumeDb = -8f)
	{
		var p = new AudioStreamPlayer { Stream = s, VolumeDb = volumeDb };
		host.AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
	}

	private static float Saw(float x) { x -= MathF.Floor(x); return x * 2f - 1f; }
	private static float Rand() => (float)(Noise.NextDouble() * 2 - 1);

	private static AudioStreamWav Make(float dur, Func<float, float> f)
	{
		int n = (int)(Rate * dur);
		var data = new byte[n * 2];
		for (int i = 0; i < n; i++)
		{
			float t = (float)i / Rate;
			short s = (short)(Math.Clamp(f(t), -1f, 1f) * 30000);
			data[i * 2] = (byte)(s & 0xFF);
			data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
		}
		return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
	}
}
