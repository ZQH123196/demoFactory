#nullable enable
// 音效合成基元（开发期工具，不进游戏）—— audiogen 动词的 DSP 层。
// 一切算术用 double、一切随机来自显式播种的 xorshift，同参数重跑逐字节相同（确定性合成）。
// 采样率与位深钉死 48 kHz / 24-bit PCM（规格「音效」），单声道。

using System;
using System.Collections.Generic;
using System.IO;

namespace DemoFactory.Tools.BeastArena;

public static class AudioSynth
{
    public const int SampleRate = 48000;
    public const int BitsPerSample = 24;
    private const double FullScale = 8388607.0;

    /// <summary>确定性伪随机（xorshift64*）。每个声件自带一个固定种子，重跑逐字节相同。</summary>
    public sealed class Rng
    {
        private ulong _s;

        public Rng(ulong seed)
        {
            if (seed == 0) seed = 0x9E3779B97F4A7C15;
            _s = seed;
        }

        /// <summary>返回 [0, 1)。</summary>
        public double Next()
        {
            _s ^= _s >> 12;
            _s ^= _s << 25;
            _s ^= _s >> 27;
            var v = _s * 0x2545F4914F6CDD1D;
            return (v >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>返回 [-1, 1)。</summary>
        public double Signed() => Next() * 2.0 - 1.0;
    }

    private const double TwoPi = 2.0 * Math.PI;

    /// <summary>正弦单音：频率从 f0 线性滑到 f1，指数衰减包络（tau 秒），可选颤音。</summary>
    public static void AddTone(double[] buf, double startSec, double durSec,
        double f0, double f1, double amp, double tau,
        double[]? partials = null, double tremHz = 0.0, double tremDepth = 0.0)
    {
        var start = (int)Math.Round(startSec * SampleRate);
        var len = (int)Math.Round(durSec * SampleRate);
        var phase = 0.0;
        for (var i = 0; i < len; i++)
        {
            var idx = start + i;
            if (idx >= buf.Length) break;
            var t = i / (double)SampleRate;
            var f = f0 + (f1 - f0) * (durSec > 0 ? t / durSec : 0);
            phase += TwoPi * f / SampleRate;
            var s = Math.Sin(phase);
            if (partials is not null)
            {
                // 加法谐波（锯齿/方波音色）：只叠 partials 指定的分音比，比例固定为 1/k。
                var sum = s;
                for (var k = 0; k < partials.Length; k++)
                {
                    sum += Math.Sin(phase * partials[k]) / (k + 2.0);
                }
                s = sum;
            }
            if (tremHz > 0) s *= 1.0 - tremDepth * 0.5 * (1.0 + Math.Sin(TwoPi * tremHz * t));
            s *= Envelope(i, len, tau);
            buf[idx] += amp * s;
        }
    }

    /// <summary>滤波噪声爆发：白噪过一阶低通，指数衰减包络。</summary>
    public static void AddNoise(double[] buf, Rng rng, double startSec, double durSec,
        double amp, double tau, double lpAlpha)
    {
        var start = (int)Math.Round(startSec * SampleRate);
        var len = (int)Math.Round(durSec * SampleRate);
        var y = 0.0;
        for (var i = 0; i < len; i++)
        {
            var idx = start + i;
            if (idx >= buf.Length) break;
            var x = rng.Signed();
            y += lpAlpha * (x - y); // 一阶低通
            buf[idx] += amp * y * Envelope(i, len, tau);
        }
    }

    /// <summary>钟声：一组固定分音比的指数衰减正弦，分音越高衰减越快。</summary>
    public static void AddBell(double[] buf, double startSec, double durSec,
        double baseFreq, double amp, double tau, double[] ratios)
    {
        for (var k = 0; k < ratios.Length; k++)
        {
            AddTone(buf, startSec, durSec, baseFreq * ratios[k], baseFreq * ratios[k],
                amp / (k + 1.0), tau / (1.0 + 0.6 * k));
        }
    }

    /// <summary>包络：5 毫秒淡入防爆音 + 指数衰减 + 尾部 20 毫秒余弦淡出。</summary>
    private static double Envelope(int i, int len, double tau)
    {
        var t = i / (double)SampleRate;
        var fadeIn = Math.Min(1.0, t / 0.005);
        var decay = tau > 0 ? Math.Exp(-t / tau) : 1.0;
        var remain = (len - i) / (double)SampleRate;
        var fadeOut = remain < 0.02 ? remain / 0.02 : 1.0;
        return fadeIn * decay * Math.Max(0.0, fadeOut);
    }

    /// <summary>去直流：减均值。</summary>
    public static void RemoveDc(double[] x)
    {
        var mean = Mean(x);
        for (var i = 0; i < x.Length; i++) x[i] -= mean;
    }

    public static double Mean(double[] x)
    {
        var sum = 0.0;
        for (var i = 0; i < x.Length; i++) sum += x[i];
        return x.Length > 0 ? sum / x.Length : 0.0;
    }

    /// <summary>样点峰值（绝对值最大）。</summary>
    public static double SamplePeak(double[] x)
    {
        var p = 0.0;
        for (var i = 0; i < x.Length; i++) p = Math.Max(p, Math.Abs(x[i]));
        return p;
    }

    /// <summary>true peak 估计：4 倍 Catmull-Rom 插值过采样后取峰。对含噪信号略低于 ITU 参考滤波，
    /// 因此天花板都留了 0.5 dB 以上余量，最终以 ffmpeg ebur128 实测为准。</summary>
    public static double TruePeak(double[] x)
    {
        if (x.Length < 4) return SamplePeak(x);
        var p = SamplePeak(x);
        for (var i = 1; i < x.Length - 2; i++)
        {
            var x0 = x[i - 1]; var x1 = x[i]; var x2 = x[i + 1]; var x3 = x[i + 2];
            for (var k = 1; k < 4; k++)
            {
                var t = k / 4.0;
                // Catmull-Rom 三次插值
                var v = x1 + 0.5 * t * (x2 - x0 + t * (2.0 * x0 - 5.0 * x1 + 4.0 * x2 - x3
                    + t * (3.0 * (x1 - x2) + x3 - x0)));
                p = Math.Max(p, Math.Abs(v));
            }
        }
        return p;
    }

    /// <summary>BS.1770 K 计权积分响度（LUFS，全缓冲无门限）。单声道按 L 权重 1.0。</summary>
    public static double MeasureLufs(double[] x)
    {
        // 48 kHz 的两节双二阶系数：高频搁架 + RLB 高通（ITU-R BS.1770 附表）。
        // 约定 y = b·x − a1·y1 − a2·y2，a1/a2 用表内原值（RLB 的 a1 是负数，别抄成正号）。
        var y = Biquad(x, 1.5351247, -2.6916959, 1.1985912, -1.6905235, 0.7323998);
        var z = Biquad(y, 1.0, -2.0, 1.0, -1.9900632, 0.9901244);
        var ms = 0.0;
        for (var i = 0; i < z.Length; i++) ms += z[i] * z[i];
        if (x.Length == 0 || ms <= 0) return -200.0;
        return -0.691 + 10.0 * Math.Log10(ms / x.Length);
    }

    private static double[] Biquad(double[] x, double b0, double b1, double b2, double a1, double a2)
    {
        var y = new double[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var x0 = x[i];
            var v = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            y[i] = v;
            x2 = x1; x1 = x0; y2 = y1; y1 = v;
        }
        return y;
    }

    public static double DbToLinear(double db) => Math.Pow(10.0, db / 20.0);
    public static double LinearToDb(double v) => 20.0 * Math.Log10(Math.Max(v, 1e-12));

    /// <summary>先按目标 LUFS 归一，再查 true peak 天花板；超线就整体压低（相对响度差不变）。</summary>
    public static double NormalizeToLufs(double[] x, double targetLufs, double truePeakCeilingLinear)
    {
        var cur = MeasureLufs(x);
        if (cur < -100) throw new InvalidOperationException("声件无能量，无法归一响度");
        var gain = DbToLinear(targetLufs - cur);
        for (var i = 0; i < x.Length; i++) x[i] *= gain;
        var tp = TruePeak(x);
        if (tp > truePeakCeilingLinear)
        {
            var trim = truePeakCeilingLinear / tp;
            for (var i = 0; i < x.Length; i++) x[i] *= trim;
            gain *= trim;
        }
        return gain;
    }

    /// <summary>循环交叉淡化：把 raw 尾部 cf 秒混进头部，让 [cf, raw.Length) 段可无缝循环。
    /// 返回长度为 loopSec 的新缓冲（raw 必须恰为 loop + cf 长）。</summary>
    public static double[] MakeLoop(double[] raw, int loopLen)
    {
        var cf = raw.Length - loopLen;
        if (cf <= 0) throw new InvalidOperationException("MakeLoop 要求 raw 比循环段长（预留交叉淡化区）");
        var outBuf = new double[loopLen];
        Array.Copy(raw, 0, outBuf, 0, loopLen);
        for (var i = 0; i < cf; i++)
        {
            var w = i / (double)(cf - 1); // 0→1：头部越靠后越保留自己
            outBuf[i] = raw[i] * w + raw[loopLen + i] * (1.0 - w);
        }
        return outBuf;
    }

    /// <summary>循环摆放：把 loop 从 startMs 铺到 endMs，头尾各 fadeMs 淡入淡出。</summary>
    public static void AddLoop(double[] mix, double[] loop, long startMs, long endMs, double gain = 1.0, int fadeMs = 60)
    {
        var s = startMs * SampleRate / 1000;
        var e = Math.Min((long)(endMs * SampleRate / 1000), mix.Length);
        var fadeIn = fadeMs * SampleRate / 1000;
        var span = e - s;
        if (span <= 0) return;
        var fadeOutStart = e - fadeIn;
        for (var n = s; n < e; n++)
        {
            if (n < 0) continue;
            var env = 1.0;
            if (n - s < fadeIn) env = (n - s) / (double)fadeIn;
            else if (n >= fadeOutStart) env = (e - n) / (double)fadeIn;
            mix[n] += gain * env * loop[(int)((n - s) % loop.Length)];
        }
    }

    /// <summary>声件摆放（按毫秒偏移），越界截断。</summary>
    public static void AddClip(double[] mix, double[] clip, long startMs, double gain = 1.0)
    {
        var s = startMs * SampleRate / 1000;
        for (var i = 0; i < clip.Length && s + i < mix.Length; i++)
        {
            if (s + i < 0) continue;
            mix[s + i] += gain * clip[i];
        }
    }

    /// <summary>编码 48 kHz / 24-bit 单声道 PCM WAV（RIFF 小端）。返回完整字节，逐字节确定。</summary>
    public static byte[] EncodeWav24Mono(double[] x)
    {
        var dataSize = x.Length * 3;
        using var ms = new MemoryStream(44 + dataSize);
        using var w = new BinaryWriter(ms);
        w.Write(Text("RIFF"));
        w.Write(36 + dataSize);
        w.Write(Text("WAVE"));
        w.Write(Text("fmt "));
        w.Write(16);              // fmt 块长
        w.Write((short)1);        // PCM
        w.Write((short)1);        // 单声道
        w.Write(SampleRate);
        w.Write(SampleRate * 3);  // 字节率
        w.Write((short)3);        // 块对齐
        w.Write((short)BitsPerSample);
        w.Write(Text("data"));
        w.Write(dataSize);
        foreach (var v in x)
        {
            var c = Math.Clamp(v, -0.99999988, 0.99999988);
            var q = (int)Math.Round(c * FullScale);
            q = Math.Clamp(q, -8388607, 8388607);
            w.Write((byte)(q & 0xFF));
            w.Write((byte)((q >> 8) & 0xFF));
            w.Write((byte)((q >> 16) & 0xFF));
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>解析 24-bit 单声道 PCM WAV（供校验用）。返回归一化样本。</summary>
    public static double[] DecodeWav24(string path, out int sampleRate, out int channels, out int bits)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || Tag(bytes, 0) != "RIFF")
            throw new InvalidDataException($"{path}：不是 RIFF");
        if (Tag(bytes, 8) != "WAVE")
            throw new InvalidDataException($"{path}：不是 WAVE");
        sampleRate = BitConverter.ToInt32(bytes, 24);
        channels = BitConverter.ToInt16(bytes, 22);
        bits = BitConverter.ToInt16(bytes, 34);
        if (bits != BitsPerSample || channels != 1)
            throw new InvalidDataException($"{path}：要求 24-bit 单声道，实际 {bits}-bit {channels} 声道");
        var n = (bytes.Length - 44) / 3;
        var x = new double[n];
        for (var i = 0; i < n; i++)
        {
            var o = 44 + i * 3;
            var q = bytes[o] | (bytes[o + 1] << 8);
            var hi = bytes[o + 2];
            q |= hi << 16;
            if ((q & 0x800000) != 0) q |= unchecked((int)0xFF000000); // 24-bit 符号扩展
            x[i] = q / FullScale;
        }
        return x;
    }

    private static byte[] Text(string s) => new[] { (byte)s[0], (byte)s[1], (byte)s[2], (byte)s[3] };

    private static string Tag(byte[] b, int o) => $"{(char)b[o]}{(char)b[o + 1]}{(char)b[o + 2]}{(char)b[o + 3]}";
}
