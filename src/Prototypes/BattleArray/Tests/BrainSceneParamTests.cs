#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 兵种参数档不漂 —— M5-c 那条「同一棵树、不同 .tscn 参数」的跨语言校验（离线、纯文本，不用起引擎）。
///
/// 为什么要有这条：射程这类数现在有两个住处 —— 逻辑层的兵种卡（<c>UnitCards</c>，进 dotnet test 的那一份）
/// 与引擎侧的场景档（<c>Scenes/Brains/&lt;兵种&gt;.tscn</c>，运行时叶子真读的那一份）。
/// 两边一旦漂开，「离线跑的这局」和「屏幕上那局」就不是同一局，M4 判绿的可复现性当场作废。
/// 运行时宿主也报一次（<c>AI/battle_brain.gd</c> 的「参数冲突」push_error），
/// 但那条要跑起来才看得见 —— 这条测试让漂移在 <c>dotnet test</c> 阶段就红。
/// </summary>
public class BrainSceneParamTests
{
    private static readonly Regex RangeLine = new(@"^range_override\s*=\s*(?<v>-?[0-9.]+)\s*$", RegexOptions.Multiline);
    private static readonly Regex LabelLine = new(@"^brain_label\s*=\s*""(?<v>[^""]*)""\s*$", RegexOptions.Multiline);

    /// <summary>一棵树本体（八个兵种档都 instance 它）—— 结构改了这里先红。</summary>
    [Fact]
    public void 一棵树_选择器下挂着三条支路()
    {
        var text = Read(RepoRoot(), "src", "Prototypes", "BattleArray", "Scenes", "_BattleBrain.tscn");

        text.ShouldContain("beehave_tree.gd", Case.Sensitive, "树本体没了");
        text.ShouldContain("selector_reactive.gd", Case.Sensitive, "根组合件不是反应式选择器（单位 AI 用 SelectorReactive 是定稿口径）");
        text.ShouldContain("BattleStrikeTask.cs");
        text.ShouldContain("BattleCloseInTask.cs");
        text.ShouldContain("BattleHoldTask.cs");
        text.ShouldNotContain("beehave/nodes/decorators", Case.Sensitive, "本契约下不需要装饰件 —— 冒出来就该问一句是谁加的");
    }

    [Fact]
    public void 八个兵种档一件一份_每档的射程与叫法都等于兵种卡()
    {
        var root = RepoRoot();
        var dir = Path.Combine(root, "src", "Prototypes", "BattleArray", "Scenes", "Brains");
        Directory.Exists(dir).ShouldBeTrue($"找不到兵种参数档目录：{dir}");

        var files = Directory.GetFiles(dir, "*.tscn").Select(Path.GetFileNameWithoutExtension).ToList();
        files.OrderBy(n => n).ShouldBe(UnitCards.All.Select(c => c.ModelName).OrderBy(n => n), "参数档与八张兵种卡对不上：新兵种没建档（运行时那个兵会退回纯 C# 规则），或建了档没登记");

        foreach (var card in UnitCards.All)
        {
            var text = Read(dir, card.ModelName + ".tscn");
            text.ShouldContain("_BattleBrain.tscn", Case.Sensitive, $"{card.ModelName} 没 instance 那棵共用的树");

            var range = Number(RangeLine, text, card.ModelName, "range_override");
            range.ShouldBe(card.Stats.Range, 1e-4, $"{card.ModelName}：场景档射程 {range} ≠ 兵种卡 {card.Stats.Range} —— 两处真值漂了，" +
                "离线那局与屏幕上那局不是同一局（改了哪一边？两边一起改，或干脆别覆盖）");

            var label = Text(LabelLine, text, card.ModelName, "brain_label");
            label.ShouldBe($"{RaceName(card.Race)} {card.DisplayName}", $"{card.ModelName}：行为流里报的叫法与 design.md 2.2 节的命名表不一致");
        }
    }

    private static string RaceName(Race race) => race == Race.Human ? "人族" : "兽人";

    private static float Number(Regex line, string text, string model, string field)
    {
        var match = line.Match(text);
        match.Success.ShouldBeTrue($"{model}.tscn 里没写 {field} —— 参数档必须明写自己那一档，别靠默认值");
        return float.Parse(match.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Text(Regex line, string text, string model, string field)
    {
        var match = line.Match(text);
        match.Success.ShouldBeTrue($"{model}.tscn 里没写 {field}");
        return match.Groups["v"].Value;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(parts), System.Text.Encoding.UTF8);

    /// <summary>从测试产物目录往上找到仓库根（认 <c>project.godot</c>）。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull("从测试目录往上找不着 project.godot");
        return dir!.FullName;
    }
}
