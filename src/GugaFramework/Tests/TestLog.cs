#nullable enable
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 测试运行期输出 —— 一份，五个测试工程共用。
///
/// <para><b>顶的是什么</b>：NUnit 的 <c>TestContext.Progress.WriteLine</c> / <c>TestContext.Out.WriteLine</c>
/// 各有一份现成实现，迁移时被换成了 <c>Ledger(...)</c>。xUnit v3 的对应物是
/// <c>TestContext.Current.TestOutputHelper</c>（实测 3.2.2 的 <c>TestContext</c> 只有 Current 这一条静态入口，
/// 实例成员是 Test / TestOutputHelper / CancellationToken / KeyValueStorage 这些）——
/// 直接在每处写 <c>TestContext.Current.TestOutputHelper?.WriteLine(...)</c> 会有两个问题：
/// 一是 30 来处重复同一个空值兜底，二是 TestOutputHelper 在测试体外取是 null（v3 文档明说
/// 「Test 不为 null 时它才可用，且实现可能仍返回 null」），拼错地方静默吞掉输出。</para>
///
/// <para><b>为什么落点能这么写</b>：v3 的 TestContext 用 <c>AsyncLocal</c> 存当前上下文，
/// 所以静态方法在测试体内取得到 —— 不必把 helper 逐个注入到构造函数（那要动 71 个 fixture 的签名）。</para>
///
/// <para><b>为什么放公共层</b>：与「重放装配单源」 同一口径（公共那半只许有一份）。
/// 落 <c>Tests/</c> 而非别处，是因为该目录被主 DemoFactory.csproj 显式排除在游戏程序集之外 ——
/// 放别处会把这个文件连同它对 Xunit 的引用一起拖进最终包。各测试工程按文件点名链入
/// （先例：某个原型的测试工程已经单点链过 GugaFramework/Tests/AbilityChecklistTests.cs）。</para>
/// </summary>
public static class TestLog
{
    /// <summary>
    /// 往当前测试的输出里写一行。
    ///
    /// <para>测试体外调用不抛（<c>TestOutputHelper</c> 为 null 时静默跳过）—— 这些调用点
    /// 多数是「顺手把台账念出来」的辅助输出，不该因为取不到输出通道就让用例红掉。
    /// 真要判红的是断言，不是这行字。</para>
    /// </summary>
    public static void Ledger(string line)
        => TestContext.Current.TestOutputHelper?.WriteLine(line);
}
