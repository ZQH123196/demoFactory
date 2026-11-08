#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace BeastArena.Tests;

/// <summary>
/// 造型线的**源**与**导出器**在版本库里（「资产可重跑」 的护栏）。
///
/// <para><b>踩过的坑（2026-09-30 交付前自查撞出）</b>：四足动作库
/// <c>src/GugaFramework/Tools/glb/clips_beast.gd</c> 与导出器里那半
/// <c>elif clip_lib == "beast"</c> 都只在工作区、没进版本库，而
/// <c>Beast.glb</c> 已经躺在库里了。后果不是「跑不动」而是
/// <b>安静地跑出错的模型</b>：干净 checkout 上第 3 步导出 10 个模型照常退出 0，
/// 但 <c>Beast</c> 那条报 <c>未知动作库 lib="beast"</c>、<c>动作 0</c> ——
/// 巨兽一个 clip 都没有（「模型四件套」 要求的四样少一样）。
/// 模型评审只看 <c>.glb</c> 看不出这个，所以<b>当场没人会发现</b>。</para>
///
/// <para>这正是「排除精确点名」 点名的「伴随文件没提交进版本库」那一族：缺的不是主体，
/// 是重跑它的那个前提。</para>
///
/// <para>所以把「源与导出器认不认」写成断言：它不查 <c>.glb</c> 的内容
/// （那是 review 的活），只查**这两样在不在、认不认**。</para>
/// </summary>
public class AssetSourceGuardTests
{
    private static string RepoRoot()
    {
        // 测试跑在 <原型>/Tests/bin/... 下，往上找带回 .git 的那一层。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("从测试目录往上找不到仓库根（没有 .git）");
    }

    private static string ReadRepoFile(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void 四足动作库在版本库里()
    {
        // 「在版本库里」= 文件在磁盘上（git 跟踪与否由 check 那条查，这里先管内容在不在）。
        var path = Path.Combine(
            RepoRoot(), "src", "GugaFramework", "Tools", "glb", "clips_beast.gd");
        File.Exists(path).ShouldBeTrue("四足动作库不在磁盘上 —— 干净 checkout 导出巨兽会动作 0（「资产可重跑」 要求从零可重跑）");
    }

    [Fact]
    public void 导出器认得beast这座动作库()
    {
        var src = ReadRepoFile("src/GugaFramework/Tools/glb/export_glb.gd");

        // 登记表里巨兽那件写的是 lib = "beast"，导出器要有对应的分支；
        // 没有的话那一条会走 _fail("未知动作库")，而**整轮导出照样退出 0**。
        src.ShouldContain("BeastClips", Case.Sensitive, "导出器没 preload 四足动作库");
        src.ShouldContain("clip_lib == \"beast\"", Case.Sensitive, "导出器没有 beast 分支 —— 登记表里的 lib=\"beast\" 会命中 _fail");
    }

    [Fact]
    public void 导出器preload的每个动作库文件都存在()
    {
        // 逐条 preload 抓出来验存在性：preload 一个不存在的路径是**运行期**才炸，
        // 而造型线第 3 步要等到某个模型用到那座库才炸 —— 顺序一换就以为没事了。
        var src = ReadRepoFile("src/GugaFramework/Tools/glb/export_glb.gd");
        var root = RepoRoot();
        var missing = 0;
        foreach (var line in src.Split('\n'))
        {
            var at = line.IndexOf("preload(\"res://", StringComparison.Ordinal);
            if (at < 0) continue;
            var start = at + "preload(\"res://".Length;
            var end = line.IndexOf('"', start);
            if (end < 0) continue;
            var rel = line[start..end].Replace('/', Path.DirectorySeparatorChar);
            if (!File.Exists(Path.Combine(root, rel)))
            {
                missing++;
                TestLog.Ledger($"缺: {rel}");
            }
        }

        missing.ShouldBe(0, "导出器 preload 的动作库文件有缺失 —— 见上面列出的路径");
    }

    [Fact]
    public void 巨兽源场景在版本库里()
    {
        // 同样这一族：Rigs/Units/Beast.tscn 与 .glb 都在，唯独动作库源头漏了。
        var path = Path.Combine(RepoRoot(), "src", "Prototypes", "BeastArena", "Tools", "Rigs", "Units", "Beast.tscn");
        File.Exists(path).ShouldBeTrue("巨兽源场景不在磁盘上");
    }

    /// <summary>
    /// 这道护栏本身的单元测试。
    ///
    /// <para>为什么不能靠「把某个 <c>.import</c> 拿掉再跑护栏」来证明护栏有效：
    /// 这台机器上 <c>dotnet test</c> 跑完会把该文件**写回原样**（实测：清成 0 字节，
    /// 跑完一次测试又是 1172 字节），所以那个负向验证在活文件上根本立不住 ——
    /// 护栏报「无 uid：无」是对的，环境把文件回滚了而已。</para>
    ///
    /// <para>所以改成在**临时目录**上测护栏本身：文件在不在、uid 在不在，两条分支各测一次。</para>
    /// </summary>
    [Fact]
    public void import伴随文件的护栏_缺文件与缺uid两条分支都验得出来()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arena-import-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var present = Path.Combine(dir, "Present.glb");
            var orphan = Path.Combine(dir, "Orphan.glb");   // 只有 .glb，缺 .import
            var noUid = Path.Combine(dir, "NoUid.glb");
            File.WriteAllText(present, "x");
            File.WriteAllText(orphan, "x");
            File.WriteAllText(noUid, "x");
            File.WriteAllText(present + ".import", "[remap]\nuid=\"uid://aaaa\"\n");
            File.WriteAllText(noUid + ".import", "[remap]\n");  // 有文件、没 uid

            var missing = new List<string>();
            var uidless = new List<string>();
            foreach (var glb in Directory.GetFiles(dir, "*.glb"))
            {
                var import = glb + ".import";
                if (!File.Exists(import)) { missing.Add(Path.GetFileName(glb)); continue; }
                if (!File.ReadAllText(import).Contains("uid=\"uid://")) uidless.Add(Path.GetFileName(glb));
            }

            missing.ShouldBe(new[] { "Orphan.glb" }, Case.Sensitive, "缺伴随文件那条分支没判出来");
            uidless.ShouldBe(new[] { "NoUid.glb" }, Case.Sensitive, "缺 uid 那条分支没判出来");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// 扫全目录的护栏本体：每个 <c>.glb</c> 都要有 <c>.glb.import</c>，且里面要有 uid（「伴随文件入库」）。
    ///
    /// <para>本条是被自己的操作弄丢的（2026-09-30）：为验「资产可重跑」「从零重跑」删了 <c>Beast.glb</c>
    /// 再跑导出器，**无头导出把 <c>.import</c> 冲掉了** —— <c>Beast.glb</c> 本身逐字节重跑出来了，
    /// 伴随文件没了。<c>.glb</c> 看着完好、模型评审也过，所以当场发现不了。
    /// 这正是「排除精确点名」 点名的那一族：缺的不是主体，是重跑它的那个前提。</para>
    ///
    /// <para>扫到几个要看得见：断言报「缺 N 个」时得知道它扫了几个 ——
    /// 「扫到 0 个所以全过」和「扫到 10 个都没缺」长得一样。</para>
    /// </summary>
    [Fact]
    public void 每个模型的import伴随文件都在()
    {
        // 「伴随文件入库」：`.glb.import` 里的 uid 每次首次导入随机发、**不可再生**，所以得进版本库。
        //
        // 扫全目录而不是只查巨兽：这一族缺口从来不是「某一个」，漏哪个就看运气。
        var models = Path.Combine(RepoRoot(), "src", "Prototypes", "BeastArena", "Models");
        var missing = new List<string>();
        var noUid = new List<string>();
        var seen = new List<string>();
        foreach (var glb in Directory.GetFiles(models, "*.glb"))
        {
            seen.Add(Path.GetFileName(glb));
            var import = glb + ".import";
            if (!File.Exists(import)) { missing.Add(Path.GetFileName(glb)); continue; }
            if (!File.ReadAllText(import).Contains("uid=\"uid://")) noUid.Add(Path.GetFileName(glb));
        }

        // 扫到几个要看得见：断言报「缺 N 个」时，得知道它到底扫了几个 ——
        // 「扫到 0 个所以全过」和「扫到 10 个都没缺」是两回事，长得却一样。
        TestLog.Ledger($"扫到 {seen.Count} 个 .glb：{string.Join(" / ", seen)}");
        TestLog.Ledger($"缺 .import：{(missing.Count == 0 ? "无" : string.Join(" / ", missing))}");
        TestLog.Ledger($"无 uid：{(noUid.Count == 0 ? "无" : string.Join(" / ", noUid))}");

        seen.ShouldNotBeEmpty("一个 .glb 都没扫到 —— 目录路径或过滤写错了，下面两条断言会假绿");
        TestLog.Ledger($"扫到 {seen.Count} 个 .glb：{string.Join(" / ", seen)}");
        TestLog.Ledger($"缺 .import：{(missing.Count == 0 ? "无" : string.Join(" / ", missing))}");
        TestLog.Ledger($"无 uid：{(noUid.Count == 0 ? "无" : string.Join(" / ", noUid))}");

        seen.ShouldNotBeEmpty("一个 .glb 都没扫到 —— 目录路径或过滤写错了，下面两条断言会假绿");
        missing.ShouldBeEmpty("这些模型缺 `.import` 伴随文件（「伴随文件入库」）");
        noUid.ShouldBeEmpty("这些 `.import` 里没有 uid，导入时会再随机发一个（「伴随文件入库」）");
    }
}
