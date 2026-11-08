#nullable enable
// audiogen 动词 —— 巨兽竞技场音效的确定性合成器（ticket 17 / 规格 T16）。
// 用法（仓库根）：
//   dotnet run --project src/Tools -- audiogen BeastArena .        # 从零重跑全部 WAV（幂等：内容相同不改写）
//   dotnet run --project src/Tools -- audiogen BeastArena . verify # 重跑并与盘上文件逐字节比对
// 产物：src/Prototypes/BeastArena/Audio/*.wav（单条与三段环境循环）
//       src/Prototypes/BeastArena/Audio/Samples/*.wav（停靠/航行/成长/防守四段固定技术样本）
// 全部声音由本文件合成，不引用任何外部素材；同参数重跑逐字节相同。
//
// 响度布局（回答「警报要压得过环境底声」这一格）：环境循环 -36 LUFS、普通操作反馈 -28、
// 生态关键动作 -25、形态与进阶 -22、警报与胜负 -18。相邻档差 ≥ 3 LU，警报对环境差 18 LU，
// 满足规格「音效」的 ≥6 LU / ≥3 LU（ffmpeg ebur128 实测表见 docs/prototypes/beast-arena/audio-review.md）。
// 单条 true peak 天花板 -3.5 dBTP（估计值，留 0.5 dB 余量给过采样滤波差异），样本混音天花板 -1.2 dBTP。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DemoFactory.Tools.BeastArena;

public static class AudioGen
{
    private const double CeilingOneShot = -4.0;   // dBTP（估计值，交付判据 -3 实测见 audio-review）
    private const double CeilingSample = -2.0;    // dBTP（估计值，交付判据 -1 实测见 audio-review）

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(string root, string? mode)
    {
        var verify = mode == "verify";
        if (mode is not (null or "verify"))
        {
            Console.Error.WriteLine($"audiogen：不认识的参数 '{mode}'（可用：无参数=生成 / verify=逐字节校验）");
            return 2;
        }

        var clips = BuildClips();
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var report = new StringBuilder();
        report.AppendLine("file\tseconds\tlufs\ttrue_peak_dbtp\tdc\tclip_count\tmax_silence_ms");

        foreach (var clip in clips)
        {
            var x = clip.Render();
            var rel = clip.RelativePath;
            bytes[rel] = AudioSynth.EncodeWav24Mono(x);
            report.AppendLine(MeasureLine(rel, x, clip.Seconds));
        }

        foreach (var sample in Samples(clips))
        {
            var x = sample.Render();
            bytes[sample.RelativePath] = AudioSynth.EncodeWav24Mono(x);
            report.AppendLine(MeasureLine(sample.RelativePath, x, sample.Seconds));
        }

        var written = 0;
        if (verify)
        {
            var bad = 0;
            foreach (var (rel, buf) in bytes)
            {
                var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path) || !ReadOnly(path).AsSpan().SequenceEqual(buf))
                {
                    Console.Error.WriteLine($"verify 不一致：{rel}");
                    bad++;
                }
            }
            Console.WriteLine(bad == 0
                ? $"audiogen verify：{bytes.Count} 个文件逐字节一致"
                : $"audiogen verify：{bad} 个文件与重跑结果不一致（共 {bytes.Count} 个）");
            return bad == 0 ? 0 : 1;
        }

        foreach (var (rel, buf) in bytes)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && ReadOnly(path).AsSpan().SequenceEqual(buf)) continue;
            File.WriteAllBytes(path, buf);
            written++;
        }
        Console.WriteLine($"audiogen：共 {bytes.Count} 个 WAV，本次改写 {written} 个（其余逐字节相同未动）");

        // 测量表落 .cache（临时产物根），供 audio-review.md 汇编；不进版本库。
        var cacheDir = Path.Combine(root, ".cache", "BeastArenaAudio");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(Path.Combine(cacheDir, "measures.tsv"), report.ToString(), new UTF8Encoding(false));
        Console.WriteLine(report.ToString());
        return 0;
    }

    private static byte[] ReadOnly(string path) => File.ReadAllBytes(path);

    private static string MeasureLine(string rel, double[] x, double seconds)
    {
        var lufs = AudioSynth.MeasureLufs(x);
        var tp = AudioSynth.LinearToDb(AudioSynth.TruePeak(x));
        var dc = Math.Abs(AudioSynth.Mean(x));
        var clipped = 0;
        var maxSil = 0;
        var run = 0;
        foreach (var v in x)
        {
            if (Math.Abs(v) >= 0.999) clipped++;
            if (Math.Abs(v) < 1e-3) { run++; if (run > maxSil) maxSil = run; } else run = 0;
        }
        var maxSilMs = maxSil * 1000.0 / AudioSynth.SampleRate;
        return string.Join('\t', rel, seconds.ToString("F2", Inv),
            lufs.ToString("F1", Inv), tp.ToString("F1", Inv),
            dc.ToString("E1", Inv), clipped.ToString(Inv), maxSilMs.ToString("F0", Inv));
    }

    private sealed record Item(string RelativePath, double Seconds, Func<double[]> Render);

    /// <summary>全部声件：单条 16 个 + 环境循环 3 个。RelativePath 以仓库根起算（res:// 同一路径）。</summary>
    private static List<Item> BuildClips()
    {
        var a = "src/Prototypes/BeastArena/Audio/";
        return new List<Item>
        {
            // —— UI ——
            New(a + "ui_click.wav", 0.09, -28, x =>
            {
                AudioSynth.AddTone(x, 0, 0.09, 1400, 1400, 0.8, 0.018);
                AudioSynth.AddNoise(x, Rng("ui_click"), 0, 0.02, 0.25, 0.006, 0.45);
            }),
            New(a + "ui_deny.wav", 0.36, -28, x =>
            {
                AudioSynth.AddTone(x, 0, 0.12, 340, 340, 0.5, 0.06, new[] { 2.0, 4.0 });
                AudioSynth.AddTone(x, 0.16, 0.18, 250, 250, 0.5, 0.08, new[] { 2.0, 4.0 });
            }),
            // —— 建造与交易 ——
            New(a + "build_done.wav", 0.8, -28, x =>
            {
                AudioSynth.AddNoise(x, Rng("build_k1"), 0.0, 0.06, 0.6, 0.015, 0.12);
                AudioSynth.AddNoise(x, Rng("build_k2"), 0.12, 0.06, 0.5, 0.015, 0.12);
                AudioSynth.AddBell(x, 0.24, 0.55, 660, 0.5, 0.14, new[] { 1.0, 1.5, 2.0 });
            }),
            New(a + "trade_done.wav", 0.5, -28, x =>
            {
                AudioSynth.AddBell(x, 0.0, 0.24, 900, 0.55, 0.06, new[] { 2.31, 3.14, 4.21 });
                AudioSynth.AddBell(x, 0.07, 0.3, 1100, 0.4, 0.07, new[] { 2.31, 3.14, 4.21 });
                AudioSynth.AddNoise(x, Rng("trade_tick"), 0, 0.015, 0.2, 0.004, 0.6);
            }),
            // —— 吞食与魔晶 ——
            New(a + "beast_swallow.wav", 1.0, -25, x =>
            {
                AudioSynth.AddTone(x, 0.05, 0.5, 200, 45, 0.8, 0.14);
                AudioSynth.AddNoise(x, Rng("swallow_wet"), 0.0, 0.55, 0.4, 0.18, 0.15);
                AudioSynth.AddTone(x, 0.42, 0.55, 62, 55, 0.5, 0.16);
            }),
            New(a + "crystal_drop.wav", 0.8, -25, x =>
            {
                AudioSynth.AddBell(x, 0.0, 0.35, 1560, 0.5, 0.09, new[] { 1.0, 1.5, 2.0, 3.0 });
                AudioSynth.AddBell(x, 0.04, 0.4, 1975, 0.35, 0.1, new[] { 1.0, 1.5, 2.0 });
                AudioSynth.AddBell(x, 0.08, 0.45, 2637, 0.25, 0.12, new[] { 1.0, 1.5 });
            }),
            // —— 形态与进阶 ——
            New(a + "form_shift.wav", 1.4, -22, x =>
            {
                AudioSynth.AddTone(x, 0, 1.0, 130, 90, 0.5, 0.5, new[] { 2.0, 3.0, 4.0 }, 6.0, 0.6);
                AudioSynth.AddNoise(x, Rng("form_growl"), 0, 0.9, 0.28, 0.45, 0.1);
                AudioSynth.AddBell(x, 0.98, 0.4, 220, 0.5, 0.12, new[] { 1.0, 2.76, 5.4 });
            }),
            New(a + "tier_advance.wav", 2.6, -22, x =>
            {
                // 仪式蓄力（0→1.5 秒渐强持续五度）→ 进阶钟声（1.5 秒起）。
                AudioSynth.AddTone(x, 0.1, 1.4, 110, 110, 0.3, 2.0, new[] { 2.0, 3.0 });
                AudioSynth.AddTone(x, 0.1, 1.4, 165, 165, 0.22, 2.0, new[] { 2.0, 3.0 });
                AudioSynth.AddTone(x, 0.1, 1.4, 220, 220, 0.15, 2.0, new[] { 2.0 });
                AudioSynth.AddBell(x, 1.5, 1.05, 164.8, 0.5, 0.3, new[] { 1.0, 2.76, 5.4, 8.9 });
                AudioSynth.AddBell(x, 1.6, 0.9, 660, 0.2, 0.25, new[] { 1.0, 1.5 });
            }),
            // —— 攻击与受击 ——
            New(a + "attack_shot.wav", 0.28, -28, x =>
            {
                AudioSynth.AddNoise(x, Rng("shot_whoosh"), 0, 0.18, 0.6, 0.06, 0.75);
                AudioSynth.AddTone(x, 0.1, 0.15, 2000, 1200, 0.35, 0.012);
            }),
            New(a + "impact_hit.wav", 0.4, -28, x =>
            {
                AudioSynth.AddTone(x, 0, 0.3, 95, 55, 0.85, 0.07);
                AudioSynth.AddNoise(x, Rng("impact_crack"), 0, 0.05, 0.5, 0.012, 0.25);
            }),
            // —— 警报与胜负 ——
            New(a + "alarm_warning.wav", 1.75, -18, x =>
            {
                // 双音号角三呼：466 / 622 交替，最后一声衰减到文件尾（尾部静音 < 200 毫秒）。
                for (var k = 0; k < 3; k++)
                {
                    var t = k * 0.35 * 2;
                    AudioSynth.AddTone(x, t, 0.28, 466, 466, 0.5, 0.3, new[] { 2.0, 3.0, 4.0 }, 5.0, 0.25);
                    AudioSynth.AddTone(x, t + 0.3, 0.28, 622, 622, 0.5, 0.3, new[] { 2.0, 3.0, 4.0 }, 5.0, 0.25);
                }
            }),
            New(a + "alarm_critical.wav", 1.3, -18, x =>
            {
                for (var k = 0; k < 3; k++)
                    AudioSynth.AddTone(x, k * 0.17, 0.09, 880, 880, 0.5, 0.05, new[] { 2.0, 4.0 });
                AudioSynth.AddTone(x, 0, 1.3, 110, 104, 0.35, 1.2, new[] { 2.0, 3.0 }, 7.0, 0.7);
            }),
            New(a + "result_victory.wav", 2.8, -18, x =>
            {
                double[] f = { 523.25, 659.25, 783.99, 1046.5 };
                for (var k = 0; k < 4; k++)
                    AudioSynth.AddBell(x, k * 0.17, 0.9, f[k], 0.35, 0.18, new[] { 1.0, 2.0, 3.0, 4.5 });
                AudioSynth.AddTone(x, 0.55, 2.2, 261.6, 261.6, 0.28, 1.6, new[] { 2.0, 3.0 });
                AudioSynth.AddTone(x, 0.55, 2.2, 392.0, 392.0, 0.2, 1.6, new[] { 2.0, 3.0 });
                AudioSynth.AddBell(x, 1.4, 1.35, 1046.5, 0.22, 0.3, new[] { 1.0, 2.76 });
            }),
            New(a + "result_defeat.wav", 2.5, -18, x =>
            {
                AudioSynth.AddTone(x, 0.0, 0.7, 392, 392, 0.35, 0.5, new[] { 2.0, 3.0 });
                AudioSynth.AddTone(x, 0.5, 0.7, 311.1, 311.1, 0.35, 0.5, new[] { 2.0, 3.0 });
                AudioSynth.AddTone(x, 1.0, 1.4, 233.1, 233.1, 0.4, 0.9, new[] { 2.0, 3.0 });
                AudioSynth.AddTone(x, 0, 2.4, 58.3, 55, 0.4, 1.4);
                AudioSynth.AddBell(x, 1.8, 0.65, 65.4, 0.5, 0.15, new[] { 1.0, 2.4, 4.2 });
            }),
            // —— 巨兽步伐 ——
            New(a + "beast_step.wav", 0.3, -28, x =>
            {
                AudioSynth.AddTone(x, 0, 0.25, 58, 40, 0.9, 0.06);
                AudioSynth.AddNoise(x, Rng("step_scuff"), 0.02, 0.12, 0.22, 0.04, 0.1);
            }),
            // —— 环境与广播 ——
            New(a + "broadcast_horn.wav", 1.5, -19, x =>
            {
                for (var k = 0; k < 3; k++)
                {
                    var t = k * 0.4;
                    AudioSynth.AddTone(x, t, 0.34, 196, 193, 0.4, 0.28, new[] { 2.0, 3.0, 4.0 });
                    AudioSynth.AddTone(x, t, 0.34, 294, 290, 0.3, 0.28, new[] { 2.0, 3.0 });
                }
            }),
            Loop(a + "amb_dock.wav", 8.0, -36, x =>
            {
                var r = Rng("amb_dock");
                // 水浪：慢 LFO 起伏的低通噪声（LFO 周期取整圈数，循环处严丝合缝）。
                AddLfoNoise(x, r, 0.5, 2.0, 0.25, 0.18);
                AddLfoNoise(x, r, 0.2, 1.6, 0.6, 0.4);
                // 远处木料吱呀与零星锤击（港口低密度）。
                AudioSynth.AddTone(x, 1.2, 0.4, 180, 170, 0.05, 0.18, new[] { 2.0, 3.0 });
                AudioSynth.AddTone(x, 4.5, 0.3, 210, 195, 0.04, 0.15, new[] { 2.0, 3.0 });
                AudioSynth.AddNoise(x, r, 2.8, 0.05, 0.05, 0.015, 0.12);
                AudioSynth.AddNoise(x, r, 6.3, 0.05, 0.045, 0.015, 0.12);
            }),
            Loop(a + "amb_sail.wav", 8.0, -36, x =>
            {
                var r = Rng("amb_sail");
                // 风声：两层不同低进程度的噪声，各自带整圈数 LFO；阵风是 3 秒起的加宽噪声窗。
                AddLfoNoise(x, r, 0.5, 1.6, 0.55, 0.4);
                AddLfoNoise(x, r, 0.25, 3.0, 0.5, 0.75);
                AddLfoNoise(x, r, 0.35, 0.5, 0.3, 0.6, 3.0, 2.5);
            }),
            Loop(a + "amb_defense.wav", 6.0, -36, x =>
            {
                var r = Rng("amb_defense");
                // 紧绷底鼓震音：微失谐低频对拍 + 远处闷雷脉冲 + 零星金属声。
                AudioSynth.AddTone(x, 0, 6.0, 55, 55, 0.3, 8.0);
                AudioSynth.AddTone(x, 0, 6.0, 55.4, 55.4, 0.3, 8.0);
                AddLfoNoise(x, r, 0.75, 1.2, 0.3, 0.12);
                AudioSynth.AddBell(x, 2.0, 0.4, 1200, 0.02, 0.1, new[] { 1.4, 2.6 });
                AudioSynth.AddBell(x, 4.2, 0.4, 1200, 0.018, 0.1, new[] { 1.6, 2.8 });
            }),
        };

        static AudioSynth.Rng Rng(string seed) => new((ulong)StableHash(seed));
        Item New(string rel, double secs, double targetLufs, Action<double[]> paint) => new(rel, secs, () =>
        {
            var x = new double[(int)Math.Round(secs * AudioSynth.SampleRate)];
            paint(x);
            AudioSynth.RemoveDc(x);
            AudioSynth.NormalizeToLufs(x, targetLufs, AudioSynth.DbToLinear(CeilingOneShot));
            return x;
        });
        Item Loop(string rel, double loopSecs, double targetLufs, Action<double[]> paint) => new(rel, loopSecs, () =>
        {
            var raw = new double[(int)Math.Round((loopSecs + 1.0) * AudioSynth.SampleRate)];
            paint(raw);
            var x = AudioSynth.MakeLoop(raw, (int)Math.Round(loopSecs * AudioSynth.SampleRate));
            AudioSynth.RemoveDc(x);
            AudioSynth.NormalizeToLufs(x, targetLufs, AudioSynth.DbToLinear(CeilingOneShot));
            return x;
        });
    }

    /// <summary>带 LFO 的低通噪声层：幅度按 lfoHz 起伏（窗口内整数圈数），窗口两端 30 毫秒淡入淡出防硬边爆音。</summary>
    private static void AddLfoNoise(double[] x, AudioSynth.Rng r, double lpAlpha, double lfoHz,
        double amp, double lfoDepth, double startSec = 0.0, double durSec = -1)
    {
        var start = (int)Math.Round(startSec * AudioSynth.SampleRate);
        var dur = durSec < 0 ? x.Length / (double)AudioSynth.SampleRate - startSec : durSec;
        var len = (int)Math.Round(dur * AudioSynth.SampleRate);
        var cycles = Math.Max(1, (int)Math.Round(lfoHz * dur));
        var f = cycles * 2.0 * Math.PI / len;
        var edge = 0.03 * AudioSynth.SampleRate; // 30 毫秒
        var y = 0.0;
        for (var i = 0; i < len && start + i < x.Length; i++)
        {
            var n = r.Signed();
            y += lpAlpha * (n - y);
            // 0→1→0 的单边余弦 LFO：depth 越大起伏越深。
            var lfo = 1.0 - lfoDepth * 0.5 * (1.0 - Math.Cos(f * i));
            var win = Math.Clamp(Math.Min(i, len - i) / edge, 0.0, 1.0);
            x[start + i] += amp * win * lfo * y;
        }
    }

    /// <summary>四段固定技术样本（规格「音效」：停靠/航行/成长/防守；防守胜利后回落非战斗环境）。
    /// 各源件已带角色响度，样本里按单位增益叠加，警报与环境/命中的相对差即源件响度差。</summary>
    private static List<Item> Samples(List<Item> clips)
    {
        var byName = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var c in clips)
        {
            var name = Path.GetFileNameWithoutExtension(c.RelativePath);
            byName[name] = c.Render();
        }

        var p = "src/Prototypes/BeastArena/Audio/Samples/";
        return new List<Item>
        {
            new(p + "sample_dock.wav", 20.0, () => Mix("sample_dock", byName,
                beds: new (string, long, long)[] { ("amb_dock", 0, 20000) },
                events: new (string, long)[] { ("ui_click", 500), ("build_done", 2000), ("ui_deny", 5000), ("ui_click", 5250),
                    ("trade_done", 7000), ("broadcast_horn", 14000), ("ui_click", 17500) })),
            new(p + "sample_sail.wav", 20.0, () => Mix("sample_sail", byName,
                beds: new (string, long, long)[] { ("amb_sail", 0, 20000) },
                events: new (string, long)[] { ("beast_step", 1000), ("beast_step", 3000), ("beast_swallow", 5500),
                    ("beast_step", 7000), ("crystal_drop", 9000), ("attack_shot", 11000),
                    ("impact_hit", 11160), ("form_shift", 14500) })),
            new(p + "sample_growth.wav", 16.0, () => Mix("sample_growth", byName,
                beds: new (string, long, long)[] { ("amb_dock", 0, 16000) },
                events: new (string, long)[] { ("crystal_drop", 1000), ("trade_done", 2800), ("form_shift", 5000),
                    ("tier_advance", 8000), ("ui_click", 14000) })),
            new(p + "sample_defense.wav", 24.0, () => Mix("sample_defense", byName,
                // 胜利后回落到非战斗环境：amb_defense 铺到 20 秒，19 秒起 amb_dock 交叉接管。
                beds: new (string, long, long)[] { ("amb_defense", 0, 20000), ("amb_dock", 19000, 24000) },
                events: new (string, long)[] { ("alarm_warning", 500), ("attack_shot", 4000), ("impact_hit", 4150),
                    ("attack_shot", 6000), ("impact_hit", 6090), ("alarm_critical", 12000),
                    ("result_victory", 20000) })),
        };
    }

    private static double[] Mix(string name, Dictionary<string, double[]> byName,
        (string Cue, long StartMs, long EndMs)[] beds, (string Cue, long StartMs)[] events)
    {
        var seconds = name switch
        {
            "sample_dock" => 20.0,
            "sample_sail" => 20.0,
            "sample_growth" => 16.0,
            "sample_defense" => 24.0,
            _ => throw new InvalidOperationException($"未知样本 {name}"),
        };
        var mix = new double[(int)Math.Round(seconds * AudioSynth.SampleRate)];
        foreach (var (cue, s, e) in beds)
            AudioSynth.AddLoop(mix, byName[cue], s, e, gain: 1.0, fadeMs: 1000);
        foreach (var (cue, s) in events)
            AudioSynth.AddClip(mix, byName[cue], s);
        // 头尾 10 毫秒总淡化，样本边缘不带截断爆音。
        var edge = AudioSynth.SampleRate / 100;
        var last = mix.Length - 1;
        for (var i = 0; i < edge; i++)
        {
            mix[i] *= i / (double)edge;
            mix[last - i] *= i / (double)edge;
        }
        AudioSynth.RemoveDc(mix);
        var tp = AudioSynth.TruePeak(mix);
        var ceiling = AudioSynth.DbToLinear(CeilingSample);
        if (tp > ceiling)
        {
            var trim = ceiling / tp;
            for (var i = 0; i < mix.Length; i++) mix[i] *= trim;
        }
        return mix;
    }

    /// <summary>种子串的稳定散列（FNV-1a），不依赖运行时字符串哈希。</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in s) h = (h ^ c) * 16777619u;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
