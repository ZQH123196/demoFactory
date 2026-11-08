#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Tools;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 寻路层的源码守卫 —— 「寻路单入口」「寻路只有寻路层一个入口」能不能判红，而不是只当约定。
///
/// <para>两条用例各管一半：一条扫真实 <c>src/</c> 判绿（今天只有豁免名单里那几处），
/// 一条在 <c>.cache/</c> 下当场造一棵违规树扫它判红 —— <b>先证它抓得住</b>。
/// 判据只会越写越钝，一个从不判红的守卫和没有守卫长得一模一样，而那是最贵的失败方式。</para>
///
/// <para>临时违规样本是运行时在 <c>.cache/</c> 下造的、用完删掉的，<b>不提交</b>：
/// 仓库里留一份会被 SDK 通配编译的违规 <c>.cs</c>，会把测试工程编挂（症状离病根很远）。</para>
/// </summary>
public class NavSourceGuardTests
{
    [Fact]
    public void 扫真实源码树_没有豁免盖不住的命中()
    {
        var src = Path.Combine(TestPaths.RepoRoot, "src");
        var hits = NavSourceGuard.Scan(src);

        // 扫到几个要看得见：「扫到 0 个所以全过」和「扫到 2000 个都没问题」长得一样。
        var fileCount = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Count(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        TestLog.Ledger($"扫到 {fileCount} 个 .cs，命中 {hits.Count} 处（豁免 {hits.Count(h => h.ExemptionWhy is not null)} 处）");
        foreach (var hit in hits)
        {
            TestLog.Ledger("  " + NavSourceGuard.Describe(hit));
        }

        fileCount.ShouldBeGreaterThan(150, $"扫到的文件太少（实测 218）—— 根目录或排除面写错了，下面那条会假绿");
        var unmatched = hits.Where(h => h.ExemptionWhy is null).ToList();
        unmatched.ShouldBeEmpty(
            "寻路层之外自己搞了一套移动 / 网格。「寻路单入口」 说寻路只有寻路层一个入口。"
            + "要么收编（工单 08），要么把这一处挪进寻路层；确实该留着就加一条豁免并写明它让哪一步变便宜：\n"
            + string.Join("\n", unmatched.Select(NavSourceGuard.Describe)));
    }

    [Fact]
    public void 三条判据都在真实源码上被激活过()
    {
        // 防止「判据写了但从没匹配过任何东西」这种假绿：豁免名单里每条都注明它撞的是哪条判据，
        // 这里数一遍，三条都得真的在扫中出现过。
        var src = Path.Combine(TestPaths.RepoRoot, "src");
        var rules = NavSourceGuard.Scan(src)
            .GroupBy(h => h.Rule)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        TestLog.Ledger(string.Join(" / ", rules.Select(kv => $"{kv.Key}:{kv.Value}")));

        rules.ContainsKey(NavSourceGuard.GridOutsideRule).ShouldBeFalse(
            "守卫 ① 今天在真实源码上 0 命中（三个原型都还没接寻路层）—— 这一条要等工单 08 才有真值");
        rules.ContainsKey(NavSourceGuard.EngineNavRule).ShouldBeFalse("引擎导航在原型里 0 命中");

        // 守卫 ③ 的命中数**跟着豁免名单走**，不写死个数：写死一个数的话，每摘一条都要改这里一次，
        // 而「改一个断言让它继续绿」正是这条断言要防的那种动作。
        // 名单清空之后这一条要反过来读：**命中数必须是 0**（有一处手写移动就判红），
        // 而「判据还抓得住东西」由下面那两条临时违规树的用例证明（它们每次跑都在造违规样本）。
        var ownNextStep = rules.TryGetValue(NavSourceGuard.OwnNextStepRule, out var hits) ? hits : 0;
        if (NavSourceGuard.Exemptions.Count == 0)
        {
            ownNextStep.ShouldBe(
                0,
                $"豁免名单空表，而真实源码上守卫 ③ 命中了 {ownNextStep} 处 —— 名单空表示"
                + "「寻路层之外不许再有自己的下一站」，有命中就是真违规（没有可以挂账的地方了）");
        }
        else
        {
            ownNextStep.ShouldBe(
                NavSourceGuard.Exemptions.Count,
                $"真实源码上守卫 ③ 的命中数（{ownNextStep}）"
                + $"应当正好等于豁免名单条数（{NavSourceGuard.Exemptions.Count}）："
                + "有命中没豁免 = 真违规，有豁免没命中 = 挂账（「账写目的」），两头都判红");
        }
    }

    [Fact]
    public void 每条豁免都对得上现存的写法_退休了就该摘掉()
    {
        // 「账写目的」 的一半靠这个断言兜住：豁免是对现存的违规开的一张 ticket，
        // 那一处退休了那张 ticket 就该作废 —— 挂着的 ticket 是「答不上来目的」的另一种写法。
        var src = Path.Combine(TestPaths.RepoRoot, "src");
        var hits = NavSourceGuard.Scan(src);

        var stale = NavSourceGuard.Exemptions
            .Where(e => !hits.Any(h =>
                string.Equals(h.RelativePath, e.RelativePath, StringComparison.Ordinal) &&
                string.Equals(h.Symbol, e.Symbol, StringComparison.Ordinal)))
            .Select(e => $"{e.RelativePath} / {e.Symbol}")
            .ToList();

        stale.ShouldBeEmpty(
            "这几条豁免对不上任何现存的命中了 —— 那一处已经退休或改名，摘掉它（「账写目的」：答不上来目的的当场销掉）：\n"
            + string.Join("\n", stale));

        foreach (var exemption in NavSourceGuard.Exemptions)
        {
            exemption.Why.Length.ShouldBeGreaterThan(
                20,
                $"{exemption.RelativePath} / {exemption.Symbol} 的理由太短 —— 「账写目的」 要的是「它让 agent 调试的哪一步变便宜」");
        }
    }

    /// <summary>
    /// 先证它抓得住：在 <c>.cache/</c> 下当场造一棵违规树，三条判据各埋一处，扫它判红。
    ///
    /// <para>为什么不提交一份违规样本文件：<c>GugaFramework.Tests.csproj</c> 用
    /// <c>..\**\*.cs</c> 通配加排除名单编源码，落在仓库里的一个违规 <c>.cs</c> 会被编进测试工程 ——
    /// 症状是「测试工程编不过」，离「守卫判据对不对」隔着一整层。</para>
    /// </summary>
    [Fact]
    public void 临时违规树证它抓得住_三条判据各判红一处()
    {
        var root = Artifacts.Ensure(TestPaths.RepoRoot, "Pathfinding", "GuardProbe");
        var file = Path.Combine(root, "Prototypes", "Probe", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, new[]
        {
            "#nullable enable",
            "namespace Probe;",
            "public sealed class Probe",
            "{",
            "public sealed class NavGrid { }",
            "    private readonly NavGrid _grid;",
            "    public Probe() => _grid = new NavGrid(new NavGridSpec());",
            "    private void MoveToward(double tx, double tz) { }",
            "    public void Go() { var len = 3.0; var step = 1.0; this.X -= this.X / len * step; }",
            "    public object Ask() => NavigationServer.GetSingleton();",
            "}",
        });

        try
        {
            var hits = NavSourceGuard.Scan(root);
            foreach (var hit in hits)
            {
                TestLog.Ledger("  " + NavSourceGuard.Describe(hit));
            }

            var gridHits = hits.Where(h => h.Rule == NavSourceGuard.GridOutsideRule).ToList();
            gridHits.Count.ShouldBe(1,
                "守卫 ① 认「声明」：样本里唯一该判红的是 public sealed class NavGrid { } 那一行，"
                + "而宿主正当的 new NavGrid(new NavGridSpec()) 递网格那两行不判红 —— "
                + "收窄到 0 命中（判据失灵）与连正当的递网格一起判红，是两种不同的坏");
            gridHits[0].Line.ShouldBe(5, "判红的应该是样本里声明那一张网格的那一行，不是宿主递网格的那一行");
            hits.Where(h => h.Rule == NavSourceGuard.EngineNavRule).ShouldNotBeEmpty(
                "守卫 ② 没抓到 NavigationServer");
            hits.Where(h => h.Rule == NavSourceGuard.OwnNextStepRule).Count().ShouldBeGreaterThanOrEqualTo(
                2,
                "守卫 ③ 两个信号（方法名 MoveToward、内联那行归一化写法）至少要各抓到一处");

            NavSourceGuard.Unmatched(root).ShouldNotBeEmpty(
                "样本在临时树里、不在豁免名单的路径上，所以「没被豁免盖住」那一份必须非空");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 临时树里的层自身与测试行不判红()
    {
        // 守卫的另外两个范围边界：层自己那一行不算「第二张网格」，用例行也不算。
        var root = Artifacts.Ensure(TestPaths.RepoRoot, "Pathfinding", "GuardScopeProbe");
        var layer = Path.Combine(root, "GugaFramework", "Pathfinding", "Grid.cs");
        var tests = Path.Combine(root, "GugaFramework", "Tests", "GridProbeTests.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(layer)!);
        Directory.CreateDirectory(Path.GetDirectoryName(tests)!);
        File.WriteAllLines(layer, new[] { "public sealed class Grid { private NavGrid _g = new NavGrid(new NavGridSpec()); }" });
        File.WriteAllLines(tests, new[] { "public class GridProbeTests { void F() { var g = new NavGrid(new NavGridSpec()); var x = g.StaticVersion; } }" });

        try
        {
            var hits = NavSourceGuard.Scan(root);
            hits.Where(h => h.Rule == NavSourceGuard.GridOutsideRule).ShouldBeEmpty(
                "本层自己的源码与它自己的用例行不算「第二张网格」—— 用例必须按名字指认它断言的对象");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 扫不存在的根当场抛_不许扫零个文件判一个全过()
    {
        Should.Throw<ArgumentException>(() => NavSourceGuard.Scan(Path.Combine(TestPaths.RepoRoot, "没有这一层")))
            .Message.ShouldContain("不存在", Case.Sensitive, "扫 0 个文件会判出一个「全过」，而它和真全过长得一模一样");
    }
}
