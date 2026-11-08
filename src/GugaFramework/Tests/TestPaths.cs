#nullable enable
using System;
using System.IO;
using System.Linq;

namespace DemoFactory.GugaFramework.Guard;

/// <summary>
/// 仓库根定位 —— 一份，五个测试工程共用。
///
/// <para><b>为什么需要它</b>：NUnit 的 <c>AppContext.BaseDirectory</c> 有 84 处站点，
/// 用途都是「从测试的 bin 目录往上找仓库根」，好读 <c>docs/</c> 与 <c>src/</c> 下的源文件做断言
/// （体素体检单读 <c>docs/GugaFramework/技能试验场.md</c>、技能层那条读 <c>Abilities/Stage/</c>、
/// <c>ReplayScriptTests</c> 读 <c>Scripts/</c>）。xUnit v3 的 <c>TestContext</c> 没有
/// <c>TestDirectory</c> 也没有 <c>WorkDirectory</c>（实测 3.2.2 只有 Test / TestOutputHelper /
/// CancellationToken / KeyValueStorage 这些），所以落点给在这里。</para>
///
/// <para><b>为什么按标记文件认而不是数层数</b>：迁移前那 84 处是「数 <c>..</c> 的个数」，
/// 五个工程数出来还不一样（GugaFramework 数 6 级、原型数 3~4 级）。层数是「测试 bin 目录的深度」，
/// 换 TFM（net8.0→net9.0）、换输出路径、还是把测试挪深一层目录，全都要跟着改数一遍 ——
/// 漏一处不报错，只是那条断言读到空串然后判一个看似不相干的红。
/// 认根标记（<c>DemoFactory.sln</c>）就没有这个耦合：往上找，找不到当场抛。</para>
///
/// <para><b>为什么放 Guard/ 这一层</b>：与「重放装配单源」 同一口径（公共那半只许有一份）。各原型只调不重写。
/// 落点在 <c>Guard/</c> 而不在 <c>Tests/</c> 是因为五个测试工程的源码链接都把
/// <c>GugaFramework\Tests\**</c> 排除掉了（那份是公共层自己的用例，不该链进原型进程），
/// 而 <c>Guard/</c> 在通配 <c>GugaFramework\**\*.cs</c> 的收面内 —— 各工程按文件点名排除的是
/// <c>Guard\LogRateWatchdog.cs</c>（那个吃引擎日志接口），本件是纯 C#，链得进也就断得干净。</para>
/// </summary>
public static class TestPaths
{
    /// <summary>根标记：往上找的就是它。选 .sln 是因为它在本仓库根、且不会被任何子工程复制一份。</summary>
    private const string RootMarker = "DemoFactory.sln";

    private static string? _cachedRepoRoot;

    /// <summary>
    /// 仓库根（绝对路径，带尾部分隔符语义由 <see cref="Combine"/> 承担）。
    /// 首次调用时从测试程序集所在目录往上找 <c>DemoFactory.sln</c>，结果缓存。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 往上找到底没摸到根标记。这不是「路径没拼对」的小事 —— 意味着测试被拷到了仓库外
    /// （或 <c>DemoFactory.sln</c> 被改名/删了），当场抛比让下游断言读到空串判出一堆假红强。
    /// </exception>
    public static string RepoRoot
    {
        get
        {
            if (_cachedRepoRoot is not null)
            {
                return _cachedRepoRoot;
            }

            // 起点用 AppContext.BaseDirectory（= 测试程序集 bin 目录），不依赖测试宿主当前工作目录 ——
            // 换 VSTest / Microsoft.Testing Platform / 直接跑 exe，工作目录会变，bin 目录不会。
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, RootMarker)))
                {
                    _cachedRepoRoot = dir.FullName;
                    return _cachedRepoRoot;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                $"从测试目录 {AppContext.BaseDirectory} 往上找不到 {RootMarker}，判不出仓库根。"
                + "这一格钉的是「测试跑在仓库内」这个前提（体素/技能层那几条要读 docs 与 src 的源文件）；"
                + "若测试被拷到仓库外跑，或根标记被改名，这里就该当场红，而不是让下游读到空串判一堆假红。");
        }
    }

    /// <summary>把仓库相对路径拼成绝对路径。</summary>
    public static string FromRoot(params string[] relativeSegments)
        => Path.GetFullPath(Path.Combine(new[] { RepoRoot }.ConcatPath(relativeSegments)));

    /// <summary>测试工程自己的源码目录（<c>bin/Debug/net8.0</c> 上溯到含 csproj 的那一层）。</summary>
    public static string TestsDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (dir.EnumerateFiles("*.csproj").Any())
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                $"从 {AppContext.BaseDirectory} 往上找不到任何 .csproj，判不出测试工程目录。");
        }
    }
}

internal static class PathSegmentExtensions
{
    /// <summary>把相对段接在根后面（<c>Concat</c> + <c>Path.Combine</c> 两步走，省得每处都写 LINQ）。</summary>
    public static string[] ConcatPath(this string[] head, string[] tail)
    {
        var result = new string[head.Length + tail.Length];
        head.CopyTo(result, 0);
        tail.CopyTo(result, head.Length);
        return result;
    }
}
