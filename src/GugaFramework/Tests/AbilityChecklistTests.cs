#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace DemoFactory.Tests;

/// <summary>
/// 技能层 <b>16 条检查清单的本体</b>（单挑场那页 7 节：可调试性 D1~D8 + 功能格 F1~F8）。
/// P3「一份清单三处跑」把这份本体改成<b>由任意宿主递进来</b>的形状：
/// 断言只在这里写一份，H1 假数据 / H2 单挑场 / H3 真 1v1 各出一个注册壳，
/// 壳只回答「哪一个兵、哪一条技能、哪一个数字」，一个字都不带断言。
///
/// <b>本文件是 G1 那一条（换宿主不改断言）的被扫对象</b>：它只认 GugaFramework 的层，
/// 不认得任何原型的世界（钉它的是 <c>AbilityH2LedgerTests.A54</c>，口径见单挑场那页 1 节表 G1 那一行）。
/// 所以这里搜不到原型类型名，也搜不到技能名 / tag 名 / 属性名的字面量——那些全在壳里（<c>Shapes</c> 槽位）。
///
/// 三条口径：
/// 1. <b>写只有一条路</b>：所有注入都走 <see cref="AbilityVerbs"/> 那一份词表（「桥只转发」），
///    清单不许直接调层的写方法——否则「注入的路径」与「玩家的路径」又长成两回事；
/// 2. <b>读只读文本</b>：断言读快照那篇文本与几个数值口，读不回对象（D2「显示即真相」的本意）；
/// 3. <b>不适用要写明为什么不适用，不许静默跳过</b>：宿主用
///    <see cref="IChecklistHost.Supports"/> 声明它接不接某一格，理由必须给出来，
///    <see cref="逐条下落都报得出来"/> 那条会把「声明了却不给理由」判红。
///
/// 与别的测试文件的分工：<c>AbilityH1Tests</c> 钉层的形状（新增列、段序、激活检查表达式），
/// <c>AbilityH2LedgerTests</c> 与 <c>AbilityH3*</c> 是<b>逐条技能的数值台账</b>（每格手算得出来），
/// 这一份是<b>三个宿主共用的断言</b>——同一格在三个世界上都成立，才算「规则只有一份」。
/// </summary>
public abstract class AbilityChecklistTests
{
    /// <summary>清单的 16 个条目号（D 组可调试性、F 组功能格；编号沿用单挑场那页 7 节）。</summary>
    public enum Item
    {
        D1, D2, D3, D4, D5, D6, D7, D8,
        F1, F2, F3, F4, F5, F6, F7, F8,
    }

    /// <summary>
    /// 某一格要不要宿主搭东西：词表里的 <c>Move</c>、分支回跳、带位移目的地的按下。
    /// 宿主接不接由壳声明，<b>不接就说为什么</b>——这一格的存在就是把「静默少一条」变成「当场报不适用」。
    /// </summary>
    public enum Capability
    {
        /// <summary>词表接得住 <see cref="AbilityVerbs.Move"/>。</summary>
        Move,

        /// <summary>能从一份快照文本分叉出另一个世界（D6 往返、D7 分支都要它）。</summary>
        Fork,

        /// <summary>级 2（引擎里起活局、按条喂剧本）——离线清单里没有一个宿主能跑。</summary>
        LiveReplay,
    }

    /// <summary>
    /// 宿主递进来的槽位：每一条技能、每一个 tag、每一个数字叫什么，由壳回答。
    /// 清单本体只按<b>形状</b>要东西（「给我一条有前摇、带消耗、会授予某个 tag 的技能」），
    /// 不许知道它在这个原型里叫什么名字。槽位给全了没有由 <see cref="逐条下落都报得出来"/> 那一格反射判：
    /// 名字不许空、数字不许 0（0 就是这一格压根没摆出来）。
    /// </summary>
    public sealed record Shapes
    {
        // === 两个兵 ===

        public required string Caster { get; init; }

        public required string Target { get; init; }

        // === 五条技能槽：各顶一组形状 ===

        /// <summary>有前摇 + 有消耗 + 有冷却 + 立即结算（F1 的载体；D5、D7 也用它）。</summary>
        public required string Windup { get; init; }

        /// <summary>有前摇且在放中授予一个 tag（F2 的「管不住在放中」、F3/F5/D5 的被打断方）。</summary>
        public required string Channeled { get; init; }

        /// <summary>过了 commit 登记成待决结算、按落地那一格现算（F6 源属性冻结、D2 第一问）。</summary>
        public required string Flying { get; init; }

        /// <summary>一次激活登记两条待决、各走各的到期步（F7 的「互不顶掉」）。</summary>
        public required string TwoPending { get; init; }

        /// <summary>入口被 <see cref="SilenceTag"/> 挡住的那条（F2 的「只管入口」+ 拒绝有名字）。</summary>
        public required string Silenced { get; init; }

        // === 四条效果槽 ===

        /// <summary>持续伤害：时长与周期两列各自存在、可叠层（F4、D2 第三问）。</summary>
        public required string Wound { get; init; }

        /// <summary>带 <c>CancelMatchingTags = ChanneledTag</c> 的打断效果（F3、D5）。</summary>
        public required string Interrupter { get; init; }

        /// <summary>只授予 <see cref="ChanneledTag"/> 的探针效果（F5 用它摆出「效果段那一层」）。</summary>
        public required string TagGrant { get; init; }

        /// <summary>只授予 <see cref="SilenceTag"/> 的探针效果（F2 用它摆出「被挡住」那一格）。</summary>
        public required string SilenceProbe { get; init; }

        // === 三个 tag 名与四条属性名 ===

        /// <summary><see cref="Channeled"/> 在放中授予的那个 tag（F3/F5/D5 都问它）。</summary>
        public required string ChanneledTag { get; init; }

        public required string SilenceTag { get; init; }

        public required string HealthAttribute { get; init; }

        /// <summary>攻击属性名（F6 在路上那一格把它顶掉，验的多半是「抄下来的快照」）。</summary>
        public required string AttackAttribute { get; init; }

        /// <summary><see cref="Windup"/> 那条技能扣掉的资源属性（F1 读它）。</summary>
        public required string CostAttribute { get; init; }

        /// <summary>同段两笔 Override 撞的那条属性，带上下界（F8 读它）。</summary>
        public required string ProbeAttribute { get; init; }

        // === 手算用的数字：台账能复现，红了才知道差在哪一格 ===

        /// <summary>目标满血基值（各条伤害的起算点）。</summary>
        public required double TargetHealth { get; init; }

        /// <summary><see cref="Windup"/> 的前摇几步。</summary>
        public required long WindupPreSteps { get; init; }

        /// <summary><see cref="Windup"/> 过 commit 扣掉的消耗。</summary>
        public required double WindupCost { get; init; }

        /// <summary><see cref="Windup"/> 那一刀的伤害（D7 分支两叉之差就比这个数）。</summary>
        public required double WindupDamage { get; init; }

        /// <summary><see cref="Channeled"/> 的前摇几步（D5 要「取消与前摇走完撞同一步」）。</summary>
        public required long ChanneledPreSteps { get; init; }

        /// <summary><see cref="Flying"/> 过 commit 到落地的延迟步。</summary>
        public required long FlyingDelaySteps { get; init; }

        /// <summary><see cref="Flying"/> 那一发的伤害。</summary>
        public required double FlyingDamage { get; init; }

        /// <summary><see cref="TwoPending"/> 两段的到期延迟（第一段、第二段）。</summary>
        public required long PendingFirstDelay { get; init; }

        public required long PendingSecondDelay { get; init; }

        /// <summary><see cref="TwoPending"/> 两段各打多少。</summary>
        public required double PendingFirstDamage { get; init; }

        public required double PendingSecondDamage { get; init; }

        /// <summary><see cref="Wound"/> 的时长 / 周期 / 一跳伤害（F4 的那两列）。</summary>
        public required long WoundDurationSteps { get; init; }

        public required long WoundPeriodSteps { get; init; }

        public required double WoundDamagePerTick { get; init; }

        /// <summary>
        /// <see cref="ProbeAttribute"/> 的界与两笔 Override 的值（F8）：
        /// 后施加的那笔赢，赢不过界就被抬回 <see cref="ProbeClampedTo"/>。
        /// </summary>
        public required double ProbeFirstMagnitude { get; init; }

        public required double ProbeSecondMagnitude { get; init; }

        public required double ProbeClampedTo { get; init; }
    }

    /// <summary>
    /// 宿主向清单递进来的那一发口：三个世界各自实现一遍，实现里不许有断言。
    /// 写只准走 <see cref="Inject"/>（词表那一份），读只准走文本与几个数值口。
    /// </summary>
    public interface IChecklistHost
    {
        /// <summary>「H1 假数据」/「H2 单挑场」/「H3 真 1v1」——报在每条断言的消息里。</summary>
        string Name { get; }

        Shapes Shapes { get; }

        /// <summary>这一侧接不接某一格；不接就必须在 <see cref="ReasonWithout"/> 给出为什么。</summary>
        bool Supports(Capability capability);

        /// <summary>不接的理由（要说人话，第一次读就懂；空串就是静默跳过，判红）。</summary>
        string ReasonWithout(Capability capability);

        /// <summary>
        /// 某一格清单条目在这一侧跑不跑：<b>返回 null = 跑</b>；返回一句理由 = 这一格在这一侧不适用，
        /// 测试报成「跳过 + 理由」而不是消失（单挑场那页 7 节那句「不适用要写明为什么不适用」的落地）。
        /// 用法：条目号取自测试方法名的前两个字符（<c>D1_…</c> / <c>F4_…</c>），见 <see cref="SetUp"/>。
        /// </summary>
        string? ReasonWithout(Item item);

        /// <summary>同参数重开一场（D1/D4 两跑逐字等靠它）。</summary>
        IChecklistHost Restart();

        /// <summary>推 n 步——时间可控那一格的唯一刻度。</summary>
        void Advance(int steps);

        /// <summary>写路径唯一入口：动词名与参数次序都在 <see cref="AbilityVerbs"/> 那一份表里。</summary>
        bool Inject(string cmd, string args);

        /// <summary>层那半的全量文本（D2 三问读它）。</summary>
        string Snapshot { get; }

        /// <summary>
        /// 逐字比较用的那份文本。宿主自己挑（H1/H2 就用层那份，H3 用重放段格式那份），
        /// 但两跑比的是<b>同一个宿主给的同一个口</b>。
        /// </summary>
        string Frame { get; }

        /// <summary>读一条属性的派生值（S2 出口那份，判定与显示同一份）。</summary>
        double Value(string unit, string attribute);

        /// <summary>读某个单位身上某条 tag 的计数。</summary>
        int TagCount(string unit, string tag);

        /// <summary>读某个单位身上某条效果几层（没有这条效果返回 -1）。</summary>
        int Stacks(string unit, string effect);

        /// <summary>
        /// 行为流里某一类形状留了几条（给了 <paramref name="keep"/> 就只数满足谓词的）。
        /// 形状即名单——没有 kind 字符串，也没有拿文本匹配当第二份实现的口子。
        /// </summary>
        int Count<T>(Func<T, bool>? keep = null) where T : EventPayload;

        /// <summary>某一类形状在行为流里出现过没有（同样可带谓词）。</summary>
        bool HasEvent<T>(Func<T, bool>? keep = null) where T : EventPayload;

        /// <summary>
        /// 从一份快照文本分叉出<b>另一个</b>可以继续推的世界（D6 往返、D7 分支）。
        /// 这一侧不支持就抛 <see cref="NotSupportedException"/>，清单会先问 <see cref="Supports"/> 再调。
        /// </summary>
        IChecklistHost Fork(string snapshot);
    }

    /// <summary>由壳提供：这个宿主的世界（H1/H2/H3 各一份，写在各自的文件里）。</summary>
    protected abstract IChecklistHost NewHost();

    private IChecklistHost _host = null!;

    /// <summary>
    /// 迁移前是 NUnit 的 <c>[SetUp]</c>。xUnit 没有这个属性，正统形态是「构造函数即 setup」——
    /// xUnit 本来就是每个测试方法新建一个实例，与这里「每格一份全新宿主」的原意天然吻合。
    ///
    /// <para>注意：<b>「这一格不适用就跳过」不能放在构造函数里</b>。xUnit v3 的
    /// <c>Assert.Skip</c> 靠抛 <c>SkipException</c> 控制流；从构造函数抛出时，运行器把它
    /// 记成 <b>失败</b>而不是<b>跳过</b>（症状：明明写了跳过理由，跑出来却是红的一条）。
    /// 所以 setup 留在构造函数，跳过判定挪进 <see cref="SkipIfUnsupported"/>，
    /// 由每个用例在方法体第一行调一次 —— 那才是 xUnit 认的跳过位置。</para>
    /// </summary>
    protected AbilityChecklistTests()
    {
        _host = NewHost();
    }

    /// <summary>
    /// 「这一格跑不了就跳过并写明理由，不许静默消失」——每个用例在方法体第一行调一次。
    /// 条目号就写在方法名开头（D1_… / F4_…）。
    /// 取的是 <b>方法名</b>：xUnit v3 的 <c>TestContext.Test</c>（<c>Xunit.Sdk.ITest</c>）没有
    /// <c>DisplayName</c>，DisplayName 挂在 TestCase 上且会带类名/参数后缀；方法名是这一格要的那一串。
    /// </summary>
    protected void SkipIfUnsupported()
    {
        var name = TestContext.Current.TestMethod.MethodName;
        if (name.Length >= 2 && (name[0] == 'D' || name[0] == 'F')
            && Enum.TryParse<Item>(name[..2], out var item))
        {
            var reason = _host.ReasonWithout(item);
            if (reason is not null)
            {
                Assert.Skip($"{name[..2]} 在 {_host.Name} 这一侧不适用：{reason}");
            }
        }
    }

    // ============================ D 组：可调试性八条 ============================

    /// <summary>
    /// <b>D1 时间可控</b>：同一份剧本（第几步发哪一发都写在下面这一处）跑两遍，每一步的读数<b>逐字</b>全等。
    /// 这一格是「时间可控」那一格：逻辑时钟不靠渲染帧率，两跑才有得比。
    /// </summary>
    [Fact]
    public void D1_同一份剧本两跑_每格读数逐字全等()
    {
        SkipIfUnsupported();
        var first = RunScript(_host);
        var second = RunScript(NewHost());
        second.Count.ShouldBe(20, "确实跑了 20 格（不然是空跑）");
        second.ShouldBe(first, $"{_host.Name} 同一份剧本两跑不逐字等：\n{FirstDiff(first, second)}");
    }

    /// <summary>
    /// <b>D1 的另一半</b>：读快照本身不许推进世界——暂停中读数一个字都不动。
    /// 「暂停冻的是自己走，不是被喂着走」，级 2 那一轮的边界同一条（已删的验证报告）。
    /// </summary>
    [Fact]
    public void D1_不推进时读数一个字都不动()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Flying}|{s.Target}");
        _host.Advance(2);
        var paused = _host.Snapshot;
        for (var i = 0; i < 5; i++)
        {
            _host.Snapshot.ShouldBe(paused, $"{_host.Name}：读第 {i + 1} 次就把世界推走了（读快照不许有副作用）");
        }
    }

    /// <summary>
    /// <b>D2 状态可查（显示即真相）</b>：三件事<b>只读快照那篇文本</b>答得出——
    /// ① 伤害在路上（待决结算那一行）、② 为什么没放（拒绝原因直接念出来）、③ 谁身上挂着持续效果。
    /// 断言读的是文本，不是回查对象：AI 拿一发读取口就要能判，不许靠手感。
    /// </summary>
    [Fact]
    public void D2_三问只读快照文本答得出()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;

        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Flying}|{s.Target}");
        _host.Advance(1);
        var flying = _host.Snapshot;
        flying.ShouldContain("p;", Case.Sensitive, $"{_host.Name}：①「伤害在路上」读不出（待决结算那一行没进快照）\n{flying}");

        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Caster}|{s.SilenceProbe}");
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Silenced}|{s.Target}");
        _host.Snapshot.ShouldContain("BlockedByTag", Case.Sensitive, $"{_host.Name}：②「为什么没放」念不出名字（入口被 tag 挡住要有拒绝原因）");

        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Target}|{s.Wound}");
        _host.Snapshot.ShouldContain(s.Wound, Case.Sensitive, $"{_host.Name}：③「谁挂着那条持续效果」读不出（效果行里没有 {s.Wound}）");
    }

    /// <summary>
    /// <b>D3 行为可注入</b>：词表那九条动词逐条都改得动真值、且改完读得出来。
    /// <c>Move</c> 那一发单独处理：这一侧接就验「走到位」，不接就验<b>当场抛</b>——
    /// 两种都不许静默（这正是 P3 收词表要防的那类缺口）。
    /// </summary>
    [Fact]
    public void D3_词表逐条动词都改得动真值()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;

        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Target}|{s.Wound}").ShouldBeTrue("施加效果没打成");
        _host.Stacks(s.Target, s.Wound).ShouldBeGreaterThanOrEqualTo(1, "挂上了却读不出层数");
        _host.Inject(AbilityVerbs.RemoveEffect, $"{s.Target}|{s.Wound}|1").ShouldBeTrue();
        _host.Stacks(s.Target, s.Wound).ShouldBeLessThanOrEqualTo(0, "撤了还在，就是没撤成");

        _host.Inject(AbilityVerbs.SetBase, $"{s.Target}|{s.HealthAttribute}|40").ShouldBeTrue();
        _host.Value(s.Target, s.HealthAttribute).ShouldBe(40, "SetBase 拧了基值却读不回");

        _host.Inject(AbilityVerbs.SetAttribute,
            $"{s.Target}|{s.ProbeAttribute}|{s.ProbeFirstMagnitude}|Override").ShouldBeTrue();
        _host.Value(s.Target, s.ProbeAttribute).ShouldNotBe(1, "挂修饰符没改得动派生值");

        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Windup}|{s.Target}").ShouldBeTrue("按下没走 S4 入口");
        _host.Advance((int)s.WindupPreSteps);
        _host.Inject(AbilityVerbs.ClearCooldown, $"{s.Caster}|{s.Windup}").ShouldBeTrue();
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Windup}|{s.Target}").ShouldBeTrue("清了冷却却还放不出第二发");
        _host.Inject(AbilityVerbs.Cancel, $"{s.Caster}|{s.Windup}").ShouldBeTrue("取消动词没受理");

        if (_host.Supports(Capability.Move))
        {
            _host.Inject(AbilityVerbs.Move, $"{s.Target}|1").ShouldBeTrue($"{_host.Name}：接得住 Move 却这一发没打成");
        }
        else
        {
            Should.Throw<NotSupportedException>(() => _host.Inject(AbilityVerbs.Move, $"{s.Target}|1"), $"{_host.Name}：词表名单里没有 Move，就必须<b>当场抛</b>，不许接了再空转。" +
                $"这一格的理由：{_host.ReasonWithout(Capability.Move)}");
        }
    }

    /// <summary>
    /// <b>D4 级 1 重放</b>：检查点序列两跑逐字等（比的是读数，事件流不逐字——「断言比快照」 确认过强度）；
    /// 每格都带步号，能与行为流「第几步」那一栏直接互查（「时间基准」 只认逻辑时钟）。
    /// </summary>
    [Fact]
    public void D4_检查点序列两跑逐字等且每格带步号()
    {
        SkipIfUnsupported();
        var frames = RunScript(_host);
        var again = RunScript(NewHost());
        again.ShouldBe(frames, $"{_host.Name}：检查点序列两跑不逐字等：\n{FirstDiff(frames, again)}");
        frames[7].ShouldContain("tick=", Case.Sensitive, $"每格都要念得出步号（拿它跟行为流第几行对上，「时间基准」 只认逻辑时钟），实到：{frames[7]}");
        (TickOf(frames[^1]) - TickOf(frames[0])).ShouldBe(19, $"{_host.Name}：二十格就是二十步——步号得跟着 Advance 走（读数里的步号与世界脱钩，" +
            "下一位读者就会拿错刻度）");
    }

    /// <summary>
    /// <b>D5 段序竞争有名字</b>：取消请求与「前摇走完」撞在同一步时，赢的是取消（S3 先跑取消段）。
    /// 对照那一半（同一格不发取消 → 就该 commit）一起钉着，否则分不清是段序对还是压根没触发。
    /// 反证（把两段调过来）不在常驻测试里做，埋法与结果在已删的验证报告。
    /// </summary>
    [Fact]
    public void D5_取消与前摇走完撞同一步_取消赢()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Channeled}|{s.Target}");
        _host.Advance((int)s.ChanneledPreSteps - 1);
        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Caster}|{s.Interrupter}");
        _host.Advance(1);   // 这一步既是前摇走完那一格，也是取消兑现那一格

        var notCommitted = _host.Count<AbilityInterrupted>(i => !i.Committed);
        notCommitted.ShouldBe(1, $"{_host.Name}：撞同一步时该由取消段赢，且留一条 AbilityInterrupted 形状写明没提交" +
            $"（旧文本的 committed=0，实到 {notCommitted} 条）\n{_host.Snapshot}");
        _host.Count<AbilityCommitted>(c => c.AbilityId == s.Channeled).ShouldBe(0, "取消赢了就不该过 commit 点——AbilityCommitted 形状那一行念出了技能名，就是段序排到了超时之后");
        _host.TagCount(s.Caster, s.ChanneledTag).ShouldBe(0, "被打断退出后，「在放中」那一层的 tag 该随技能一起退");

        var control = NewHost();
        var b = control.Shapes;
        control.Inject(AbilityVerbs.Cast, $"{b.Caster}|{b.Channeled}|{b.Target}");
        control.Advance((int)b.ChanneledPreSteps);
        control.Count<AbilityInterrupted>(i => !i.Committed).ShouldBe(0, "同一格不发取消就该走到 commit（对照那一半没站着，这条断言才是两段撞出来的）");
        control.Count<AbilityCommitted>(c => c.AbilityId == b.Channeled).ShouldBe(1, "对照那一半要真过 commit 点：AbilityCommitted 形状念得出技能名");
    }

    /// <summary>
    /// <b>D6 往返测试（状态可回跳）</b>：快照 → 重建 → 再快照，逐字相等。
    /// 不等就是公理 A2（状态即数据）破了，而差在哪一行指得出来——派生行不进重建、靠 S2 现算，
    /// 所以这一条顺带把 S2 的确定性也钉住。
    /// </summary>
    [Fact]
    public void D6_快照往返_重建再存逐字相等()
    {
        SkipIfUnsupported();
        Require(Capability.Fork, "D6");
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Flying}|{s.Target}");
        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Target}|{s.Wound}");
        _host.Advance(3);
        var saved = _host.Snapshot;

        var rebuilt = _host.Fork(saved);
        rebuilt.Snapshot.ShouldBe(saved, $"{_host.Name}：往返不等，差在哪一行：\n{FirstDiff(saved, rebuilt.Snapshot)}");
    }

    /// <summary>
    /// <b>D7 分支对照</b>：同一检查点分两叉，一支按下、一支不按，两叉之差正好等于那一刀的手算数。
    /// 起跳成本是 O(1) 而不是重跑 k 步——「改一笔输入看看会怎样」这条通路便宜不便宜就看它。
    /// </summary>
    [Fact]
    public void D7_从检查点分叉_改一笔输入只差那一笔()
    {
        SkipIfUnsupported();
        Require(Capability.Fork, "D7");
        var s = _host.Shapes;
        _host.Advance(2);
        var checkpoint = _host.Snapshot;

        var casted = _host.Fork(checkpoint);
        casted.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Windup}|{s.Target}");
        casted.Advance((int)s.WindupPreSteps + 1);
        var idle = _host.Fork(checkpoint);
        idle.Advance((int)s.WindupPreSteps + 1);

        var dropped = idle.Value(s.Target, s.HealthAttribute) - casted.Value(s.Target, s.HealthAttribute);
        idle.Value(s.Target, s.HealthAttribute).ShouldBe(s.TargetHealth, $"{_host.Name}：不按的那一叉本该一分没掉（世界自己在动就要写明扣掉的是哪一笔）");
        ((double)casted.Value(s.Target, s.HealthAttribute)).ShouldBe(s.TargetHealth - s.WindupDamage, 0.001, $"按下的那一叉该掉 {s.WindupDamage}，实到掉 {s.TargetHealth - casted.Value(s.Target, s.HealthAttribute)}");
        ((double)dropped).ShouldBe(s.WindupDamage, 0.001, "两叉之差就是那一笔——起跳是 O(1)（不是重跑前 k 步）才敢这么说");
    }

    /// <summary>
    /// <b>D8 级 2 引擎重放</b>：同一份剧本在级 1（离线）与级 2（引擎里起活局、按条喂）逐格指纹全等。
    /// 这一格<b>离线清单里没有一个宿主能跑</b>：它要吃真引擎与桥，数字在 `docs/debug 指南.md` 与已删的验证报告。
    /// 所以三处都按「不适用 + 理由」报出来，而不是悄悄少一条——这正是它写在清单里的价值。
    /// </summary>
    [Fact]
    public void D8_级2引擎重放_离线三处都报不适用()
    {
        SkipIfUnsupported();
        _host.Supports(Capability.LiveReplay).ShouldBeFalse("级 2 要起活局：dotnet test 这一层跑不了它。若哪天离线跑得了，这条要改口而不是删");
        _host.ReasonWithout(Capability.LiveReplay).ShouldNotBeEmpty("D8 的不适用理由必须写出来（不许静默跳过）");
        _host.ReasonWithout(Capability.LiveReplay).Length.ShouldBeGreaterThanOrEqualTo(12, $"理由要让人第一次读就懂为什么，实到：{_host.ReasonWithout(Capability.LiveReplay)}");
    }

    // ============================ F 组：功能格八条 ============================

    /// <summary>
    /// <b>F1 commit 点前后取消的返还差</b>：前摇中取消 → 消耗一分不扣、冷却也不给；
    /// 过了 commit 被打断 → 消耗照扣、冷却照留（清单页「被打断不返还冷却」那一格）。
    /// </summary>
    [Fact]
    public void F1_commit_前取消不扣消耗_commit_后照扣()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        var startCost = _host.Value(s.Caster, s.CostAttribute);
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Windup}|{s.Target}");
        _host.Advance(1);   // 还在前摇里
        _host.Value(s.Caster, s.CostAttribute).ShouldBe(startCost, $"{_host.Name}：没过 commit 点就不该扣消耗");

        _host.Inject(AbilityVerbs.Cancel, $"{s.Caster}|{s.Windup}");
        _host.Advance(1);
        _host.Value(s.Caster, s.CostAttribute).ShouldBe(startCost, "前摇中取消：消耗一分不扣");
        _host.Snapshot.ShouldContain("Interrupted", Case.Sensitive, "取消要有名字（拒绝原因读得出来）");
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Windup}|{s.Target}").ShouldBeTrue("前摇中取消也不给冷却：马上再按该放得出去");

        var after = NewHost();
        var b = after.Shapes;
        var baseCost = after.Value(b.Caster, b.CostAttribute);
        after.Inject(AbilityVerbs.Cast, $"{b.Caster}|{b.Windup}|{b.Target}");
        after.Advance((int)b.WindupPreSteps);   // 过 commit 那一格
        after.Value(b.Caster, b.CostAttribute).ShouldBe(baseCost - b.WindupCost, $"{_host.Name}：过了 commit 就该扣掉那一笔消耗（实到扣 {baseCost - after.Value(b.Caster, b.CostAttribute)}，" +
            $"应扣 {b.WindupCost}）");
        after.Inject(AbilityVerbs.Cancel, $"{b.Caster}|{b.Windup}");
        after.Value(b.Caster, b.CostAttribute).ShouldBe(baseCost - b.WindupCost, "已提交的消耗不返还");
        after.Inject(AbilityVerbs.Cast, $"{b.Caster}|{b.Windup}|{b.Target}").ShouldBeFalse("过了 commit 就该留冷却：马上再按要被拒");
        after.Snapshot.ShouldContain("Cooldown", Case.Sensitive, "而拒绝要有名字");
    }

    /// <summary>
    /// <b>F2 BlockedTags 只管入口</b>：在放中的能力对中途新增的 tag 无感（前摇照走完、结算照样落地）；
    /// 而入口那一发问得到 tag——被挡住放不下，且「为什么没放」念得出名字。
    /// 打断另有其口（F3 的 <c>CancelMatchingTags</c>），三件事不是一件事。
    /// </summary>
    [Fact]
    public void F2_入口被挡有名字_管不住在放中的那一条()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Channeled}|{s.Target}");
        _host.Advance(1);
        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Caster}|{s.SilenceProbe}");
        _host.Advance((int)s.ChanneledPreSteps);
        _host.Count<AbilityCommitted>(c => c.AbilityId == s.Channeled).ShouldBe(1, $"{_host.Name}：中途新挂的 tag 不该影响在放中的能力——前摇照走完、commit 照过（形状 AbilityCommitted）" +
            $"（打断另有其口，见 F3）");

        var probe = NewHost();
        var p = probe.Shapes;
        probe.Inject(AbilityVerbs.ApplyEffect, $"{p.Caster}|{p.SilenceProbe}");
        probe.Inject(AbilityVerbs.Cast, $"{p.Caster}|{p.Silenced}|{p.Target}").ShouldBeFalse("入口被挡住就该放不下");
        probe.Snapshot.ShouldContain("BlockedByTag", Case.Sensitive, "而拒绝必须有名字");
    }

    /// <summary>
    /// <b>F3 显式打断</b>：带 <c>CancelMatchingTags</c> 的效果命中「在放的那条」，行为流留一行并写明
    /// 提交没提交（<c>committed=0/1</c>）。而<b>身上那条效果不随技能撤销</b>——
    /// 取消技能 ≠ 取消已登记的结算（N-9 的归属口径，N-22 那条缺口就长在这一格）。
    /// </summary>
    [Fact]
    public void F3_显式打断在放的能力_留痕且分清已登记的()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Channeled}|{s.Target}");
        _host.Advance(1);
        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Caster}|{s.Interrupter}");
        _host.Advance(1);

        _host.Count<AbilityInterrupted>(i => !i.Committed).ShouldBe(1, $"{_host.Name}：显式打断要留一条 AbilityInterrupted 形状并写明还没提交（Committed=false，旧文本的 committed=0；Cost 与冷却都不消耗）");
        _host.TagCount(s.Caster, s.ChanneledTag).ShouldBe(0, "被打断之后那条「在放中」的 tag 该随技能退出");
        _host.Stacks(s.Caster, s.Interrupter).ShouldBeGreaterThanOrEqualTo(1, "打断方那条效果照在——取消技能 ≠ 取消已施加的效果（N-9 的归属口径）");
    }

    /// <summary>
    /// <b>F4 Duration ≠ period</b>：时长与周期是两列，改一列另一列不动。
    /// 这条看的是数字：一跳掉一跳的血，而到期步只由时长那一列决定，与跳了几次无关。
    /// </summary>
    [Fact]
    public void F4_时长与周期两列各自存在()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        s.WoundDurationSteps.ShouldBeGreaterThan(s.WoundPeriodSteps * 2, "这一格的摆法本身要成立：时长得比周期大出一倍以上，不然分不清两列");

        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Target}|{s.Wound}");
        var health = _host.Value(s.Target, s.HealthAttribute);
        _host.Advance((int)s.WoundPeriodSteps);
        _host.Value(s.Target, s.HealthAttribute).ShouldBe(health - s.WoundDamagePerTick, $"{_host.Name}：周期那一跳到点就该掉血");
        _host.Advance((int)s.WoundPeriodSteps);
        _host.Value(s.Target, s.HealthAttribute).ShouldBe(health - 2 * s.WoundDamagePerTick, "两跳就是两跳的量——周期在按自己的列走");
        _host.Stacks(s.Target, s.Wound).ShouldBeGreaterThanOrEqualTo(1, "两跳之后这条效果还在：时长那一列比周期长，两列各走各的（这就是「Duration ≠ period」的正证）");
        _host.Snapshot.ShouldContain(s.Wound, Case.Sensitive, "效果行还在快照里念得出来");
    }

    /// <summary>
    /// <b>F5 tag 两段汇总</b>：能力段（在放中授予）与效果段（持续效果授予）各计一层，
    /// S2 一处收口成同一张计数表——「同一条 tag 两层」要读得出来，不能各算各的。
    /// </summary>
    [Fact]
    public void F5_tag_能力段与效果段汇总到一张计数表()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.ApplyEffect, $"{s.Caster}|{s.TagGrant}");
        _host.TagCount(s.Caster, s.ChanneledTag).ShouldBe(1, $"{_host.Name}：效果段授予的那一层读不出（{s.ChanneledTag}）");

        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Channeled}|{s.Target}");
        _host.Advance(1);
        _host.TagCount(s.Caster, s.ChanneledTag).ShouldBe(2, "两段各计一层、合在<b>同一张计数表</b>上——S2 一处收口，不是各算各的");
        _host.Snapshot.ShouldContain($"t;{s.ChanneledTag};2", Case.Sensitive, $"计数行要念得出这个 2（显示即真相），实到：{Lines(_host.Snapshot, "t;")}");
    }

    /// <summary>
    /// <b>F6 Calc 走注册函数</b>：伤害由注册口那一份纯函数算，输入与输出同框念出；
    /// 而链上的<b>源属性是 commit 那一刻抄下的快照</b>——所以在「在路上」那一格把攻击顶成别的数，
    /// 不许改变已在路上的那一发（目标属性相反，按落地那一格现读，与 <c>A43</c> 同一口径）。
    /// 载体那条技能得<b>真的读源属性</b>，否则这一格就退化成「数对得上」——壳里那行注释要说清它读的是哪一条。
    /// </summary>
    [Fact]
    public void F6_Calc走注册函数_源属性是快照不是活值()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Flying}|{s.Target}");
        _host.Advance(1);
        _host.Snapshot.ShouldContain("p;", Case.Sensitive, "过了 commit 就该登记成待决结算");

        var before = _host.Value(s.Target, s.HealthAttribute);
        _host.Inject(AbilityVerbs.SetAttribute, $"{s.Caster}|{s.AttackAttribute}|1|Override");
        _host.Advance((int)s.FlyingDelaySteps);
        _host.Value(s.Target, s.HealthAttribute).ShouldBe(before - s.FlyingDamage, $"{_host.Name}：在路上把攻击顶成 1 之后，那一发仍该按 commit 抄下来的攻击算（应掉 {s.FlyingDamage}，" +
            $"实掉 {before - _host.Value(s.Target, s.HealthAttribute)}）");
    }

    /// <summary>
    /// <b>F7 Pending 登记即入快照</b>：同一次激活登记多条待决，各占一条、错峰各自落地、互不顶掉；
    /// 取消技能也不撤已经登记的结算（N-9）。漏了这一格，级 2 在落地那一步当场对不齐。
    /// </summary>
    [Fact]
    public void F7_同一次激活两条待决互不顶掉_取消也不撤()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.TwoPending}|{s.Target}");
        _host.Advance(1);
        var rows = Lines(_host.Snapshot, "p;");
        rows.Count.ShouldBe(2, $"{_host.Name}：一次激活登记两条待决，快照里就该念得出两条（实到 {rows.Count} 条）\n{_host.Snapshot}");

        _host.Inject(AbilityVerbs.Cancel, $"{s.Caster}|{s.TwoPending}");
        var health = _host.Value(s.Target, s.HealthAttribute);
        _host.Advance((int)s.PendingFirstDelay);
        _host.Value(s.Target, s.HealthAttribute).ShouldBe(health - s.PendingFirstDamage, "第一段照结——取消技能不等于取消已登记的结算");
        _host.Advance((int)(s.PendingSecondDelay - s.PendingFirstDelay));
        _host.Value(s.Target, s.HealthAttribute).ShouldBe(health - s.PendingFirstDamage - s.PendingSecondDamage, "第二段错峰落地");
    }

    /// <summary>
    /// <b>F8 modifier 排序键</b>：同一步两条 Override 的结果由 <c>(AppliedAtTick, Seq)</c> 定，
    /// 不由字典顺序定；聚合完之后才套属性的界（已删的设计档那句先后）。
    /// </summary>
    [Fact]
    public void F8_同段两笔修饰符_排序键定唯一结果_界在最后套()
    {
        SkipIfUnsupported();
        var s = _host.Shapes;
        _host.Inject(AbilityVerbs.SetAttribute, $"{s.Target}|{s.ProbeAttribute}|{s.ProbeFirstMagnitude}|Override");
        _host.Inject(AbilityVerbs.SetAttribute, $"{s.Target}|{s.ProbeAttribute}|{s.ProbeSecondMagnitude}|Override");
        _host.Value(s.Target, s.ProbeAttribute).ShouldBe(s.ProbeClampedTo, $"{_host.Name}：两笔 Override 该由排序键定出唯一结果、再由属性的界抬回 {s.ProbeClampedTo}，" +
            $"实到 {_host.Value(s.Target, s.ProbeAttribute)}");

        var again = NewHost();
        var b = again.Shapes;
        again.Inject(AbilityVerbs.SetAttribute, $"{b.Target}|{b.ProbeAttribute}|{b.ProbeFirstMagnitude}|Override");
        again.Inject(AbilityVerbs.SetAttribute, $"{b.Target}|{b.ProbeAttribute}|{b.ProbeSecondMagnitude}|Override");
        again.Snapshot.ShouldBe(_host.Snapshot, $"{_host.Name}：同一串注入两跑读数逐字等（排序键不是字典顺序）");
    }

    // ============================ 逐条下落与工具 ============================

    /// <summary>
    /// <b>逐条下落都报得出来</b>（单挑场那页 7 节那句「不适用要写明为什么不适用，不许静默跳过」的机器口径）：
    /// 宿主声明不接的每一格，都必须给出一句够长的理由；理由为空 = 这条断言当场红。
    /// 16 条各自跑没跑，看 <c>dotnet test</c> 的通过/跳过计数——跳过的每一条都带着这句理由。
    /// </summary>
    [Fact]
    public void 逐条下落都报得出来()
    {
        SkipIfUnsupported();
        foreach (var capability in new[] { Capability.Move, Capability.Fork, Capability.LiveReplay })
        {
            if (_host.Supports(capability)) continue;
            var reason = _host.ReasonWithout(capability);
            reason.ShouldNotBeNull(); reason.ShouldNotBeEmpty();
            reason.Length.ShouldBeGreaterThanOrEqualTo(12, $"{_host.Name} 的 {capability} 理由太短，第一次读不懂为什么：{reason}");
        }

        // 每一条「不适用」都得是一句读得懂的话；空串与「不适用」三个字都不算
        var skipped = new List<string>();
        foreach (Item item in Enum.GetValues<Item>())
        {
            var reason = _host.ReasonWithout(item);
            if (reason is null) continue;
            reason.Length.ShouldBeGreaterThanOrEqualTo(12, $"{_host.Name} 说 {item} 不适用，理由却读不出为什么：「{reason}」");
            skipped.Add(item.ToString());
        }

        TestLog.Ledger($"清单下落｜{_host.Name}：跑 {16 - skipped.Count} 条" +
            $"，不适用 {skipped.Count} 条 {(skipped.Count == 0 ? string.Empty : "(" + string.Join(",", skipped) + ")")}");

        Enum.GetValues<Item>().Length.ShouldBe(16, "清单就该 16 条（D1~D8 + F1~F8），多一条少一条都要先改这一句、再改单挑场那页 7 节");

        // 壳递进来的槽位一格都不许留空：留空就是「这一格其实没跑」，比不适用更坏
        foreach (var property in typeof(Shapes).GetProperties())
        {
            var value = property.GetValue(_host.Shapes);
            var text = value as string;
            var number = value is double or long ? Convert.ToDouble(value) : (double?)null;
            value.ShouldNotBeNull($"{_host.Name} 的槽位 {property.Name} 没给");
            if (text is not null)
            {
                text.ShouldNotBeEmpty($"{_host.Name} 的槽位 {property.Name} 给了空名字");
            }

            if (number is not null)
            {
                // number 是 double?，Shouldly 的 ShouldBeGreaterThan<T> 约束在 T? 上解不出来
                // （CS0313：double? 不满足 IComparable<double?>）。在 null 守卫之内取 .Value。
                number.Value.ShouldBeGreaterThan(0, $"{_host.Name} 的槽位 {property.Name} 给了 {number}——手算台账的数字不许是 0 或负（0 就是这一格压根没摆）");
            }
        }
    }

    /// <summary>
    /// 那份「同一串剧本」：<b>第几步发哪一发只写在清单这一处</b>，三个宿主喂的是同一段。
    /// 槽位从各自壳递进来的名字拼参数串，所以这一段里没有任何一个技能名。
    /// </summary>
    private static List<string> RunScript(IChecklistHost host)
    {
        var s = host.Shapes;
        var frames = new List<string>();
        for (var i = 1; i <= 20; i++)
        {
            if (i == 2)
            {
                host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Flying}|{s.Target}");
            }

            if (i == 4)
            {
                host.Inject(AbilityVerbs.ApplyEffect, $"{s.Target}|{s.TagGrant}");
            }

            if (i == 6)
            {
                host.Inject(AbilityVerbs.SetAttribute, $"{s.Target}|{s.ProbeAttribute}|0.5|Override");
            }

            if (i == 8)
            {
                host.Inject(AbilityVerbs.Cast, $"{s.Caster}|{s.Windup}|{s.Target}");
            }

            host.Advance(1);
            frames.Add(host.Frame);
        }

        return frames;
    }

    /// <summary>这一侧不接某一格：把理由交给跳过消息，让它<b>报出来</b>而不是消失。</summary>
    private void Require(Capability capability, string itemKey)
    {
        if (_host.Supports(capability)) return;
        Assert.Skip($"{itemKey} 在 {_host.Name} 这一侧不适用：{_host.ReasonWithout(capability)}");
    }

    /// <summary>从一格读数里取出步号（三个宿主的读数都以 <c>tick=N</c> 起头，见单挑场那页 5 节）。</summary>
    private static long TickOf(string frame)
    {
        var at = frame.IndexOf("tick=", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"读数里念不出步号：{frame}");
        var start = at + 5;
        var end = start;
        while (end < frame.Length && char.IsDigit(frame[end])) end++;
        end.ShouldBeGreaterThan(start, $"tick= 后面不是一串数字：{frame}");
        return long.Parse(frame[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<string> Lines(string snapshot, string prefix)
        => snapshot.Split('\n').Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    /// <summary>差在哪一行要一眼看得见（三个宿主共用这一份 diff，别各抄一个）。</summary>
    private static string FirstDiff(string a, string b)
    {
        var la = a.Split('\n');
        var lb = b.Split('\n');
        for (var i = 0; i < Math.Min(la.Length, lb.Length); i++)
        {
            if (la[i] != lb[i])
            {
                return $"第 {i + 1} 行\n  甲: {la[i]}\n  乙: {lb[i]}";
            }
        }

        return la.Length == lb.Length ? "(无差异)" : $"行数 {la.Length} vs {lb.Length}";
    }

    private static string FirstDiff(List<string> a, List<string> b)
    {
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            if (a[i] == b[i]) continue;
            var at = 0;
            while (at < Math.Min(a[i].Length, b[i].Length) && a[i][at] == b[i][at]) at++;
            return $"第 {i + 1} 格 @字符 {at}\n  甲: {a[i]}\n  乙: {b[i]}";
        }

        return a.Count == b.Count ? "(无差异)" : $"格数 {a.Count} vs {b.Count}";
    }
}
