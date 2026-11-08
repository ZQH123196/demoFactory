#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// ticket 07 的键鼠覆盖检查：<b>词表里每个业务动词都得有一条界面入口</b>。
///
/// <para><b>比对的是两份真表</b>：<see cref="ArenaSetup.VerbNames"/>（业务动词）与
/// <see cref="UiEntryPoints.All"/>（界面入口表）。缺一条、多一条、重复一条、或者把主线动词
/// 偷偷标成「世界自动触发」都判红；再回读入口表指名的源文件，确认那个控件名真的在代码里 ——
/// 表不是一张没人核对的愿望清单。</para>
/// </summary>
public class UiEntryCoverageTests
{
    /// <summary>合成输入那两条不是业务动词，不进覆盖比对。</summary>
    private static readonly HashSet<string> Synthetic =
        new(SyntheticVerbs.Names, StringComparer.Ordinal);

    private static List<string> BusinessVerbs()
        => ArenaSetup.VerbNames.Where(v => !Synthetic.Contains(v)).ToList();

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 词表里每个业务动词都有界面入口标记()
    {
        var business = BusinessVerbs();
        var registered = UiEntryPoints.All.Select(e => e.Verb).ToList();

        var missing = business.Where(v => !UiEntryPoints.HasEntry(v)).ToList();
        missing.ShouldBeEmpty(
            $"有 {missing.Count} 个业务动词没有界面入口：{string.Join("、", missing)}。"
            + "补界面或（只限世界自动触发的那一条）在 UiEntryPoints 标 Automatic 并写清理由。");

        var stale = registered.Where(v => !business.Contains(v)).ToList();
        stale.ShouldBeEmpty(
            $"界面入口表里有 {stale.Count} 条不在词表里：{string.Join("、", stale)}。动词删了，入口表要跟着删。");

        var duplicates = registered.GroupBy(v => v, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        duplicates.ShouldBeEmpty($"入口表里这些动词登记了不止一次：{string.Join("、", duplicates)}。");

        registered.Count.ShouldBe(business.Count,
            "入口表与业务动词表要一一对应：每条动词一条入口，不多不少。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 自动化入口只留给出界转向那一条()
    {
        var automatic = UiEntryPoints.All
            .Where(e => e.Kind == UiEntryKind.Automatic)
            .Select(e => e.Verb)
            .ToList();

        automatic.ShouldBe(new[] { "SteerOutOfBounds" }, Case.Sensitive,
            "Automatic 只留给「世界自己触发、玩家按不到」的出界转向；"
            + "别的动词标成 Automatic 就等于绕过了「必须点得到」这一条。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 每条入口的控件名都能在对应源文件里找到()
    {
        var scripts = TestPaths.FromRoot("src", "Prototypes", "BeastArena", "Scripts");
        var broken = new List<string>();

        foreach (var entry in UiEntryPoints.All)
        {
            var path = Path.Combine(scripts, entry.Source.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                broken.Add($"{entry.Verb}：源文件 {entry.Source} 不存在");
                continue;
            }

            var source = File.ReadAllText(path);
            if (!source.Contains(entry.Control, StringComparison.Ordinal))
            {
                broken.Add($"{entry.Verb}：{entry.Source} 里找不到控件名「{entry.Control}」");
            }
        }

        broken.ShouldBeEmpty(
            "入口表与代码对不上（表里写着能点，代码里没有那个控件）：" + string.Join(" ‖ ", broken));
    }
}
