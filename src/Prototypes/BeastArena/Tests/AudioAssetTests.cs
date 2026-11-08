#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.Prototypes.BeastArena.Audio;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 音频资产离线校验（规格「音效」的源文件检查条）。
/// 只读盘上的 WAV、只算数，不起引擎、不调 ffmpeg、不碰任何外部服务（快速测试）。
/// 怕什么：① 有人手改了资产把格式/电平改出契约；② 合成器改出新静音或削波；
/// ③ 警报压不过环境底声（相对响度是混音关系的骨架）。
/// </summary>
public class AudioAssetTests
{
    private const int Rate = 48000;
    private const double Fs = 8388607.0; // 24-bit 满刻度

    private static readonly string AudioDir =
        TestPaths.FromRoot("src", "Prototypes", "BeastArena", "Audio");
    private static readonly string SamplesDir = Path.Combine(AudioDir, "Samples");

    // —— 解码与度量（独立实现，不复用生成器代码：校验方与生成方分离） ——

    private static double[] ReadWav(string path, out int sampleRate, out int bits)
    {
        var b = File.ReadAllBytes(path);
        b.Length.ShouldBeGreaterThan(44, $"{Path.GetFileName(path)}：小于 WAV 头长");
        $"{(char)b[0]}{(char)b[1]}{(char)b[2]}{(char)b[3]}".ShouldBe("RIFF");
        $"{(char)b[8]}{(char)b[9]}{(char)b[10]}{(char)b[11]}".ShouldBe("WAVE");
        BitConverter.ToInt16(b, 20).ShouldBe((short)1, $"{Path.GetFileName(path)}：不是 PCM 编码");
        BitConverter.ToInt16(b, 22).ShouldBe((short)1, "要求单声道");
        sampleRate = BitConverter.ToInt32(b, 24);
        bits = BitConverter.ToInt16(b, 34);
        $"{(char)b[36]}{(char)b[37]}{(char)b[38]}{(char)b[39]}".ShouldBe("data");
        var n = (b.Length - 44) / 3;
        var x = new double[n];
        for (var i = 0; i < n; i++)
        {
            var o = 44 + i * 3;
            var q = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
            if ((q & 0x800000) != 0) q |= unchecked((int)0xFF000000);
            x[i] = q / Fs;
        }
        return x;
    }

    /// <summary>BS.1770 K 计权积分响度（全缓冲无门限），与 ffmpeg ebur128 同口径的离线近似。</summary>
    private static double Lufs(double[] x)
    {
        double[] Stage(double[] s, double b0, double b1, double b2, double a1, double a2)
        {
            var y = new double[s.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < s.Length; i++)
            {
                var v = b0 * s[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                y[i] = v; x2 = x1; x1 = s[i]; y2 = y1; y1 = v;
            }
            return y;
        }
        var z = Stage(Stage(x, 1.5351247, -2.6916959, 1.1985912, -1.6905235, 0.7323998),
            1.0, -2.0, 1.0, -1.9900632, 0.9901244);
        if (z.Length == 0) return -200;
        var ms = z.Sum(v => v * v) / z.Length;
        return ms <= 0 ? -200 : -0.691 + 10 * Math.Log10(ms);
    }

    /// <summary>true peak 估计：4 倍 Catmull-Rom 过采样取峰（交付判据以 ffmpeg ebur128 实测为准，这里做离线守门）。</summary>
    private static double TruePeakDb(double[] x)
    {
        var p = x.Length == 0 ? 0 : x.Select(Math.Abs).Max();
        for (var i = 1; i + 2 < x.Length; i++)
        {
            var x0 = x[i - 1]; var x1 = x[i]; var x2 = x[i + 1]; var x3 = x[i + 2];
            for (var k = 1; k < 4; k++)
            {
                var t = k / 4.0;
                var v = x1 + 0.5 * t * (x2 - x0 + t * (2 * x0 - 5 * x1 + 4 * x2 - x3
                    + t * (3 * (x1 - x2) + x3 - x0)));
                p = Math.Max(p, Math.Abs(v));
            }
        }
        return 20 * Math.Log10(Math.Max(p, 1e-12));
    }

    private static long MaxSilenceMs(double[] x)
    {
        long run = 0, best = 0;
        foreach (var v in x)
        {
            if (Math.Abs(v) < 1e-3) { run++; best = Math.Max(best, run); } else run = 0;
        }
        return best * 1000 / Rate;
    }

    // —— 用例 ——

    public static IEnumerable<object[]> CatalogWavs() =>
        AudioCatalog.All.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(CatalogWavs))]
    public void 目录里每个声件都是48k的24bitPCM且格式合规(AudioEntry entry)
    {
        // Arrange
        var path = Path.Combine(TestPaths.RepoRoot, AudioCatalog.ToRepoRelative(entry.ResPath));

        // Act
        File.Exists(path).ShouldBeTrue($"{entry.Cue}：资产不在盘上（audiogen 可从零重跑）");
        File.Exists(path + ".import").ShouldBeTrue($"{entry.Cue}：.import 伴随文件没提交（「伴随文件入库」）");
        var x = ReadWav(path, out var rate, out var bits);

        // Assert
        rate.ShouldBe(Rate, $"{entry.Cue}：采样率不是 48 kHz");
        bits.ShouldBe(24, $"{entry.Cue}：位深不是 24 bit");
        var seconds = x.Length / (double)Rate;
        if (entry.Priority == AudioPriority.Ambience)
            seconds.ShouldBeInRange(1.0, 20.0, $"{entry.Cue}：环境循环上限 20 秒");
        else
            seconds.ShouldBeInRange(0.05, 5.0, $"{entry.Cue}：单条上限 5 秒");
    }

    [Theory]
    [MemberData(nameof(CatalogWavs))]
    public void 声件无直流无削波非静音无长静音段(AudioEntry entry)
    {
        var path = Path.Combine(TestPaths.RepoRoot, AudioCatalog.ToRepoRelative(entry.ResPath));
        var x = ReadWav(path, out _, out _);
        var name = Path.GetFileName(path);

        Math.Abs(x.Average()).ShouldBeLessThan(1e-4, $"{name}：有直流偏置");
        x.Select(Math.Abs).Max().ShouldBeLessThan(0.999, $"{name}：样本顶到满刻度（削波）");
        Lufs(x).ShouldBeGreaterThan(-45, $"{name}：几乎无声（非预期静音）");
        TruePeakDb(x).ShouldBeLessThan(-3.0, $"{name}：true peak 越过 -3 dBTP");
        // 尾巴是自然衰减（指数衰减到噪声底），阈值按最长尾巴 + 余量取 400 毫秒；床声允许更长静默段。
        var limit = entry.Priority == AudioPriority.Ambience ? 600 : 400;
        MaxSilenceMs(x).ShouldBeLessThanOrEqualTo(limit, $"{name}：内部出现超长静音段");
    }

    [Theory]
    [InlineData("result_victory")]
    [InlineData("result_defeat")]
    [InlineData("alarm_warning")]
    [InlineData("alarm_critical")]
    public void 警报比环境底声高至少6LU(string alertCue)
    {
        foreach (var amb in new[] { "amb_dock", "amb_sail", "amb_defense" })
        {
            var a = Lufs(ReadWav(Path.Combine(TestPaths.RepoRoot,
                AudioCatalog.ToRepoRelative(AudioCatalog.Get(alertCue).ResPath)), out _, out _));
            var b = Lufs(ReadWav(Path.Combine(TestPaths.RepoRoot,
                AudioCatalog.ToRepoRelative(AudioCatalog.Get(amb).ResPath)), out _, out _));
            (a - b).ShouldBeGreaterThanOrEqualTo(6.0, $"{alertCue} 相对 {amb} 不足 6 LU（规格「音效」）");
        }
    }

    [Theory]
    [InlineData("click")]
    [InlineData("deny")]
    [InlineData("build_done")]
    [InlineData("trade_done")]
    [InlineData("impact_hit")]
    [InlineData("attack_shot")]
    [InlineData("beast_step")]
    public void 普通操作反馈比环境底声高至少3LU(string feedbackCue)
    {
        foreach (var amb in new[] { "amb_dock", "amb_sail", "amb_defense" })
        {
            var a = Lufs(ReadWav(Path.Combine(TestPaths.RepoRoot,
                AudioCatalog.ToRepoRelative(AudioCatalog.Get(feedbackCue).ResPath)), out _, out _));
            var b = Lufs(ReadWav(Path.Combine(TestPaths.RepoRoot,
                AudioCatalog.ToRepoRelative(AudioCatalog.Get(amb).ResPath)), out _, out _));
            (a - b).ShouldBeGreaterThanOrEqualTo(3.0, $"{feedbackCue} 相对 {amb} 不足 3 LU（规格「音效」）");
        }
    }

    public static IEnumerable<object[]> SampleNames() =>
        new[] { "sample_dock", "sample_sail", "sample_growth", "sample_defense" }
            .Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void 四段技术样本15到30秒且混音峰值与底噪合规(string name)
    {
        var path = Path.Combine(SamplesDir, name + ".wav");
        File.Exists(path).ShouldBeTrue($"{name}：技术样本不在盘上");
        File.Exists(path + ".import").ShouldBeTrue($"{name}：.import 伴随文件没提交");
        var x = ReadWav(path, out var rate, out var bits);
        rate.ShouldBe(Rate);
        bits.ShouldBe(24);
        var seconds = x.Length / (double)Rate;
        seconds.ShouldBeInRange(15.0, 30.0, $"{name}：样本时长要求 15~30 秒");
        Math.Abs(x.Average()).ShouldBeLessThan(1e-4, $"{name}：直流偏置");
        x.Select(Math.Abs).Max().ShouldBeLessThan(0.999, $"{name}：削波");
        TruePeakDb(x).ShouldBeLessThan(-1.0, $"{name}：最终混音 true peak 越过 -1 dBTP");
        // 非预期静音：样本内不得有 500 毫秒以上整段死寂（床声一直铺着）。
        MaxSilenceMs(x).ShouldBeLessThanOrEqualTo(500, $"{name}：出现整段死寂");
    }

    [Fact]
    public void 样本目录里只有四段样本()
    {
        Directory.EnumerateFiles(SamplesDir, "*.wav").Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(new[] { "sample_defense", "sample_dock", "sample_growth", "sample_sail" });
    }
}
