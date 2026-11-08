#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using DemoFactory.Prototypes.BeastArena.Audio;
using DemoFactory.Prototypes.BeastArena.Logic;
using DemoFactory.Prototypes.BeastArena.Show;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 九类事件的表现对照表（T17）：每一类都必须同时有特效轨、音效 cue 与结果标记的三样，
/// 巨兽 clip 那一条只在表里写了名字时才查。怕什么：① 某类漏了一格，画面上「有反应」但三样缺一；
/// ② 轨名 / cue 名 / clip 名与资产、目录、动作库漂开，运行时静默不演；③ 九类被改名或扩成十类。
/// </summary>
public class ShowCueTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("从测试目录往上找不到仓库根（没有 .git）");
    }

    private static string ModelPath(string name)
        => Path.Combine(RepoRoot(), "src", "Prototypes", "BeastArena", "Models", name + ".glb");

    private static string ResPath(string res)
        => Path.Combine(RepoRoot(), res["res://".Length..].Replace('/', Path.DirectorySeparatorChar));

    public static IEnumerable<object[]> Categories()
    {
        foreach (ShowCategory category in Enum.GetValues<ShowCategory>()) yield return [category];
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void 每类事件三样齐备(ShowCategory category)
    {
        var cue = ShowCues.Of(category);

        ShowCues.VfxTracks.ContainsKey(cue.VfxTrack).ShouldBeTrue($"{category} 的特效轨「{cue.VfxTrack}」不在轨表里");
        ResPath(ShowCues.VfxTracks[cue.VfxTrack]).ShouldNotBeNull();
        File.Exists(ResPath(ShowCues.VfxTracks[cue.VfxTrack]))
            .ShouldBeTrue($"{category} 的特效场景不在磁盘上：{ShowCues.VfxTracks[cue.VfxTrack]}");
        AudioCatalog.Get(cue.AudioCue).Category.ShouldNotBe(default(AudioCategory));
        ShowCues.NameOf(category).Length.ShouldBeGreaterThan(0, $"{category} 没有可读的类别名");
    }

    [Fact]
    public void 九类一格不多一格不少()
    {
        Enum.GetValues<ShowCategory>().Length.ShouldBe(9, "规格「演出」只列九类事件；多一类少一类都要先改规格");
    }

    [Fact]
    public void 巨兽clip名都在这件模型的动作表里()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "idle", "walk", "run", "eat", "lie_down", "get_up", "form_shift", "advance",
        };

        foreach (ShowCategory category in Enum.GetValues<ShowCategory>())
        {
            var clip = ShowCues.Of(category).BeastClip;
            if (clip.Length == 0) continue;
            allowed.Contains(clip).ShouldBeTrue($"{category} 的巨兽 clip「{clip}」不在八条动作里");
        }
    }

    [Fact]
    public void 九类引用的模型都在磁盘上()
    {
        string[] models =
        [
            // 六套巨兽 + 两阶底座与浮山
            "Beast", "BeastDesert", "BeastGlutton",
            "BeastTier2", "BeastDesertTier2", "BeastGluttonTier2",
            "LevitationBase", "LevitationBaseTier2", "TownDeck", "TownDeckTier2",
            // 施工与废墟三档
            "ScaffoldSmall", "ScaffoldMedium", "ScaffoldLarge",
            "RuinSmall", "RuinMedium", "RuinLarge",
        ];

        foreach (var model in models)
            File.Exists(ModelPath(model)).ShouldBeTrue($"模型不在磁盘上：{model}.glb（T17 接线会静默不显示）");

        foreach (var spec in DeckBlueprints.Specs)
            File.Exists(ModelPath(spec.Id + "Damaged"))
                .ShouldBeTrue($"建筑「{spec.Name}」没有受损态模型：{spec.Id}Damaged.glb");
    }

    [Fact]
    public void 环境床三条都带环境前缀()
    {
        foreach (var cue in new[] { "amb_dock", "amb_sail", "amb_defense" })
            AudioCatalog.Get(cue).Cue.StartsWith(AudioCatalog.AmbiencePrefix, StringComparison.Ordinal)
                .ShouldBeTrue($"{cue} 不是环境床命名");
    }
}
