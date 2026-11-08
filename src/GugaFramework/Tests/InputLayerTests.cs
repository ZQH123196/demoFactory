#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.InputLayer;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Pathfinding.Stage;
using DemoFactory.Tools;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 输入层用例 —— 读法、缓冲、概念名、推进收编这四格。
///
/// <para><b>这一层能纯 C# 断言，是因为读法分派那一件把引擎那三个读数口做成了构造参数。</b>
/// 用例于是能在没有引擎的进程里喂假的按下/按住进去 —— 「这一发为什么触发了两次」
/// 从此不用起引擎就能判红。</para>
///
/// <para><b>凡是要断「三个原型各自那一段」的，都扫源码</b>（照 <see cref="NavSourceGuardTests"/> 那一套）：
/// 公共层测试进程里链不进原型的 <c>Main.cs</c>（那是吃引擎的 <c>Node</c>），
/// 所以跨原型的事实只能读文件判。每条这样的守卫都配一条「在临时树上先证它抓得住」。</para>
/// </summary>
public class InputLayerTests
{
    private const int HoldFrames = 60;

    /// <summary>
    /// 假引擎源 —— 照引擎真实形状喂：刚按下只亮一帧，按住一直亮。
    /// 「按住不放」在这件里就是 <c>heldFrames</c> 帧的 held。
    /// </summary>
    private sealed class FakeSource
    {
        private readonly int _holdFrames;

        public FakeSource(int holdFrames) => _holdFrames = holdFrames;

        public int Frame { get; private set; }

        public int HeldReads { get; private set; }

        public int JustPressedReads { get; private set; }

        /// <summary>引擎的「刚按下」：一次物理按下只 true 一次，之后进帧就靠引擎自己闩住。</summary>
        public bool JustPressed(string action) => Frame == 0;

        /// <summary>引擎的「按住」：还按着就一直 true。</summary>
        public bool Held(string action)
        {
            HeldReads++;
            return Frame < _holdFrames;
        }

        public float Axis(string action) => Frame < _holdFrames ? 1f : 0f;

        public void NextFrame() => Frame++;

        public InputRead Reader() => new(JustPressed, Held, Axis);
    }

    // === 一、读法：编译期必填，四个旋钮都是「刚按下」 ===

    [Fact]
    public void 四个旋钮的读法都是刚按下_按住不放重复触发就出不来()
    {
        foreach (var knob in InputConcept.TimeKnobConcepts)
        {
            knob.Read.ShouldBe(ReadMode.JustPressed,
                $"{knob.Name} 的读法不是刚按下 —— 按住不放会重复触发暂停 / 单步 / 变速");
        }

        InputConcept.TimeKnobConcepts.Count.ShouldBe(4, "时间旋钮四条：暂停 / 单步 / 提速 / 降速");
    }

    [Fact]
    public void 读法是必填的_构造函数不接没给读法这一种()
    {
        // 漏给读法要编译不过，那不是运行期能断的事。这里断的是它的反面：
        // 「万一有人给读法开了缺省值」——有缺省就等于可以不给。
        var ctors = typeof(InputConcept).GetConstructors();
        ctors.Length.ShouldBe(1, "概念只该有一个构造入口；多一个就多一条能漏给读法的路");
        var parameters = ctors[0].GetParameters();
        parameters.Length.ShouldBe(2);
        parameters[1].Name.ShouldBe("Read");
        parameters[1].HasDefaultValue.ShouldBeFalse("读法不能有缺省值：给了缺省就等于可以不填");
        parameters[1].ParameterType.ShouldBe(typeof(ReadMode));
    }

    [Fact]
    public void 镜头那十二条_平移缩放按按住_跟随与复位按刚按下()
    {
        InputConcept.CameraConcepts.Count.ShouldBe(12, "镜头十二条：平移 4 / 绕转 2 / 俯仰 2 / 缩放 2 / 跟随 / 复位");

        var held = InputConcept.CameraConcepts
            .Where(c => c.Name.StartsWith("camera_pan", StringComparison.Ordinal)
                        || c.Name.StartsWith("camera_zoom", StringComparison.Ordinal)
                        || c.Name.StartsWith("camera_rotate", StringComparison.Ordinal)
                        || c.Name.StartsWith("camera_pitch", StringComparison.Ordinal))
            .ToList();
        held.Count.ShouldBe(10, "平移 4 + 缩放 2 + 绕转 2 + 俯仰 2 = 十条按「按住」（连续量，每帧都要读）");
        held.ShouldAllBe(c => c.Read == ReadMode.Held, "连续量按「按住」读 —— 按「刚按下」读等于一按就一格");

        foreach (var toggle in new[] { InputConcept.CameraFollowToggle, InputConcept.CameraResetPose })
        {
            toggle.Read.ShouldBe(ReadMode.JustPressed, $"{toggle.Name} 按「刚按下」：按住 Tab 会把跟随反复翻开合上");
        }
    }

    // === 二、按住不放不重复触发（本 ticket 的核心那一格，带一条对照） ===

    [Fact]
    public void 按住不放_四个旋钮各只触发一次()
    {
        var source = new FakeSource(HoldFrames);
        var read = source.Reader();
        var fired = InputConcept.TimeKnobConcepts.ToDictionary(k => k.Name, _ => 0, StringComparer.Ordinal);

        for (var frame = 0; frame < HoldFrames; frame++)
        {
            foreach (var knob in InputConcept.TimeKnobConcepts)
            {
                if (read.Read(knob)) fired[knob.Name]++;
            }

            source.NextFrame();
        }

        foreach (var knob in InputConcept.TimeKnobConcepts)
        {
            fired[knob.Name].ShouldBe(1,
                $"{knob.Name} 在按住 {HoldFrames} 帧里触发了 {fired[knob.Name]} 次 —— "
                + "一次物理按下只该成立一次");
        }
    }

    [Fact]
    public void 对照_同一发若按按住读就会每帧触发一次()
    {
        // 没这一条，上面那条就分不清「读法钉住了」与「源只亮了一帧，压根没得重复」——
        // 它在「什么都没订阅」的世界里也成立，不是判据。这一条是它没释放的对照世界。
        var source = new FakeSource(HoldFrames);
        var read = source.Reader();

        var asHeld = InputConcept.TimeKnobConcepts
            .Select(k => new InputConcept(k.Name, ReadMode.Held))
            .ToList();

        var firedHeld = 0;
        var firedJustPressed = 0;
        for (var frame = 0; frame < HoldFrames; frame++)
        {
            foreach (var knob in asHeld)
            {
                if (read.Read(knob)) firedHeld++;
            }

            foreach (var knob in InputConcept.TimeKnobConcepts)
            {
                if (read.Read(knob)) firedJustPressed++;
            }

            source.NextFrame();
        }

        firedHeld.ShouldBe(HoldFrames * 4,
            "同一次按住，按「按住」读就是每帧一次 —— 这正是「按住不放重复触发」那个形状");
        firedJustPressed.ShouldBe(4, "按「刚按下」读，同一帧形状下每条各一次");
    }

    [Fact]
    public void 按住读法问刚按下当场抛_别把两种口径混着用()
    {
        var read = new FakeSource(1).Reader();
        Should.Throw<InvalidOperationException>(() => read.JustPressed(InputConcept.CameraZoomIn))
            .Message.ShouldContain("读法", Case.Sensitive);
    }

    [Fact]
    public void 模拟量读法问真假当场抛_别把按多强塌成按没按()
    {
        var read = new FakeSource(1).Reader();
        var asAxis = new InputConcept(InputConcept.CameraZoomIn.Name, ReadMode.Axis);
        Should.Throw<InvalidOperationException>(() => read.Read(asAxis));
        Should.Throw<InvalidOperationException>(() => read.ReadAxis(InputConcept.TogglePause))
            .Message.ShouldContain("刚按下", Case.Sensitive);
        read.ReadAxis(InputConcept.CameraZoomIn).ShouldBe(1.0, "按住那一帧的强度就是 1");
    }

    // === 三、输入缓冲：窗长按步数，N 步内有效、N+1 步失效 ===

    [Fact]
    public void 窗内有效窗外出界_窗长按步数算()
    {
        var buffer = new InputBuffer(windowSteps: 2);
        buffer.Record(InputConcept.TogglePause, 100);

        buffer.TryConsume(InputConcept.TogglePause, 100, out var at0).ShouldBeTrue("同一步消费");
        buffer.TryConsume(InputConcept.TogglePause, 100, out _).ShouldBeFalse("消费过就没了（一次按一次消费）");

        buffer.Record(InputConcept.TogglePause, 200);
        buffer.TryConsume(InputConcept.TogglePause, 202, out var at2).ShouldBeTrue("N 步（2 步）内有效");
        at2.ShouldBe(200L, "回传的是按下那一步，不是消费那一步");

        buffer.Record(InputConcept.TogglePause, 300);
        buffer.TryConsume(InputConcept.TogglePause, 303, out _).ShouldBeFalse("N+1 步（3 步）失效");
        buffer.PendingCount.ShouldBe(0, "出窗的那一发当场作废，不留在缓冲里等人来收");
    }

    [Fact]
    public void 缺省窗长两步_构造参数一个数就能改()
    {
        new InputBuffer().WindowSteps.ShouldBe(2, "缺省 2 步（一步 0.1 秒 ⇒ 0.2 秒）");
        new InputBuffer(windowSteps: 5).WindowSteps.ShouldBe(5);
        Should.Throw<ArgumentOutOfRangeException>(() => new InputBuffer(windowSteps: 0));
    }

    [Fact]
    public void 丢输入路径_那一发记进缓冲后在窗口里被消费()
    {
        // 丢输入的那两条真实路径（失焦、暂停态的菜单）形状一样：
        // 有一帧看见了刚按下，但那一帧没人消费它。于是记进缓冲，下一帧源已经灭了。
        var source = new FakeSource(1);   // 只在第 0 帧亮
        var read = source.Reader();
        var buffer = new InputBuffer(windowSteps: 2);
        var sourceTick = 40L;

        read.Notice(InputConcept.TogglePause, buffer, sourceTick).ShouldBeTrue("第 0 帧看见了一发刚按下");

        source.NextFrame();
        source.JustPressed("input_toggle_pause").ShouldBeFalse("源已经灭了 —— 这一发只能从缓冲里补");

        read.ReadBuffered(InputConcept.TogglePause, buffer, sourceTick + 1, out var firedAt).ShouldBeTrue(
            "窗口内补回来了");
        firedAt.ShouldBe(sourceTick, "补的是触发时刻，不是消费时刻");
        buffer.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void 缓冲补不回出窗的那一发_丢得太久就当没按过()
    {
        var source = new FakeSource(1);
        var read = source.Reader();
        var buffer = new InputBuffer(windowSteps: 2);
        read.Notice(InputConcept.StepOnce, buffer, 0);

        source.NextFrame();
        read.ReadBuffered(InputConcept.StepOnce, buffer, 3, out _).ShouldBeFalse(
            "过了 3 步（> 2）还不消费，这一发作废 —— 不然「玩家早就想按，现在才生效」变成常态");
    }

    [Fact]
    public void 反向用例_参数依赖步号的命令当帧消费不走缓冲窗口过了就作废()
    {
        // 无限领主「读到悬赏金额当前值」那种：命令内容要读当前步的世界真值。
        // 把它延到窗口里再执行，读到的就是另一个步号的数 —— 那不是补触发时刻，是改命令内容。
        var source = new FakeSource(1);
        var read = source.Reader();
        var buffer = new InputBuffer(windowSteps: 2);
        var tick = 10L;

        read.ReadSameFrameOnly(InputConcept.TogglePause).ShouldBeTrue("当帧成立");
        buffer.PendingCount.ShouldBe(0, "这一条一个字都不许进缓冲");

        source.NextFrame();
        read.ReadSameFrameOnly(InputConcept.TogglePause).ShouldBeFalse("下一帧就不再成立 —— 窗口对它无效");

        // 对照：同样那一发交给缓冲那条路，它是会被补回来的（所以这条约束是必要的，不是保守）。
        var source2 = new FakeSource(1);
        var read2 = source2.Reader();
        var buffer2 = new InputBuffer(windowSteps: 2);
        read2.Notice(InputConcept.TogglePause, buffer2, tick);
        source2.NextFrame();
        read2.ReadBuffered(InputConcept.TogglePause, buffer2, tick + 1, out _).ShouldBeTrue(
            "同一发走缓冲那条路会被补 —— 两条路的差别是这一格要守住的东西");
    }

    [Fact]
    public void 缓冲只放刚按下那一档_按住与模拟量记不进去()
    {
        foreach (var concept in new[] { InputConcept.CameraZoomIn, InputConcept.CameraPanLeft })
        {
            InputBuffer.Bufferable(concept).ShouldBeFalse($"{concept.Name} 按「按住」读 —— 不存在「作废」，补它是画蛇添足");

            var buffer = new InputBuffer();
            Should.Throw<ArgumentException>(() => buffer.Record(concept, 0))
                .Message.ShouldContain("不进缓冲", Case.Sensitive);
        }

        InputBuffer.Bufferable(InputConcept.TogglePause).ShouldBeTrue();
    }

    [Fact]
    public void 狂按两下只占一格_新的那发顶掉旧的()
    {
        var buffer = new InputBuffer(windowSteps: 3);
        buffer.Record(InputConcept.SpeedUp, 10);
        buffer.Record(InputConcept.SpeedUp, 11);
        buffer.PendingCount.ShouldBe(1, "同一个概念不占两格 —— 否则「狂点两下」会堆出两条同概念的记录");
        buffer.TryConsume(InputConcept.SpeedUp, 11, out var at).ShouldBeTrue();
        at.ShouldBe(11L, "消费的是后按的那一发");
    }

    [Fact]
    public void 清理出窗的记录_每帧调一次()
    {
        var buffer = new InputBuffer(windowSteps: 2);
        buffer.Record(InputConcept.TogglePause, 0);
        buffer.Record(InputConcept.SpeedUp, 0);
        buffer.ExpirePast(1).ShouldBe(0, "还在窗内");
        buffer.ExpirePast(3).ShouldBe(2, "两步之外全清");
        buffer.PendingCount.ShouldBe(0);
    }

    // === 四、时间旋钮：既不进 Journal 也不进快照（ADR 0015 的直接断言） ===

    [Fact]
    public void 时间旋钮既不进快照也不进动作账_三个动作逐个按过一遍()
    {
        var stage = new NavStage(NavStageSample.Spec());
        stage.Advance(4);

        var snapshotBefore = stage.DebugSnapshot();
        var ledgerBefore = stage.EventTailLines(200);

        // 三个旋钮各触发一次。源在第 0 帧亮着 —— 三个是三个不同的键，同一帧各按一下是正常打法。
        var source = new FakeSource(1);
        var read = source.Reader();

        read.Read(InputConcept.TogglePause).ShouldBeTrue();
        if (stage.Paused) stage.Resume(); else stage.Pause();

        read.Read(InputConcept.SpeedUp).ShouldBeTrue();
        stage.Speed = 2.0;

        // 暂停与变速是纯表现层状态：读数口照常动，快照与动作账一个字都不许变。
        stage.Paused.ShouldBeTrue("暂停确实被按下去了（这条断言断的不是「有没有生效」）");
        stage.Speed.ShouldBe(2.0, "变速确实生效了");
        stage.DebugSnapshot().ShouldBe(snapshotBefore, "时间旋钮是表现层状态，进快照会在没分叉的地方报分叉");
        stage.EventTailLines(200).ShouldBe(ledgerBefore, "暂停与变速一条都不许进动作账（ADR 0015）");

        // 单步不进动作账；但它本来就该把世界推进一步，那一步改的是步号不是账。
        var tickBefore = stage.Tick;
        read.Read(InputConcept.StepOnce).ShouldBeTrue();
        stage.AdvanceStep();
        stage.Tick.ShouldBe(tickBefore + 1, "单步就该推进一步");
        stage.EventTailLines(200).ShouldBe(ledgerBefore, "单步不进动作账 —— 它是一次推进，不是一次写");

        foreach (var knob in InputConcept.TimeKnobConcepts)
        {
            snapshotBefore.Contains(knob.Name, StringComparison.Ordinal).ShouldBeFalse(
                $"{knob.Name} 不许是快照里的一格");
        }
    }

    [Fact]
    public void 四个旋钮名不在任何一条公共层动词表里_它们不是写路径()
    {
        var verbNames = AbilityVerbs.Names
            .Concat(ConstructionVerbs.Names)
            .Concat(NavVerbs.Names)
            .ToArray();
        verbNames.Length.ShouldBeGreaterThan(5, "三张表至少要读到东西，空表会判出一个看似全过的绿");

        foreach (var knob in InputConcept.TimeKnobConcepts)
        {
            verbNames.Contains(knob.Name, StringComparer.Ordinal).ShouldBeFalse(
                $"{knob.Name} 出现在动词表里 —— 补进词表就是把它变成写路径，顶 ADR 0015 的牛");
        }
    }

    // === 五、三个原型的暂停共用一个概念名（扫源码） ===

    [Fact]
    public void 三个原型的暂停都走input_toggle_pause_绑的键各自不同()
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InfinityLord"] = "Key.Escape",
            ["BattleArray"] = "Key.P",
            ["BeastArena"] = "Key.Space",
        };

        foreach (var pair in bindings)
        {
            var path = PrototypeMain(pair.Key);
            var text = File.ReadAllText(path);

            // 断的是「走那一个概念常量」而不是「文件里出现过那个字符串」：
            // 三处都写 InputConcept.TogglePause，动作名于是同一个，由构造保证，不靠三份字面量对齐。
            UsesPublicPauseConcept(text).ShouldBeTrue($"{pair.Key} 的暂停没走公共概念常量 InputConcept.TogglePause");
            text.Contains(pair.Value, StringComparison.Ordinal).ShouldBeTrue(
                $"{pair.Key} 的 input_toggle_pause 绑的不是 {pair.Value} —— 键位是原型的内容决定");
        }
    }

    /// <summary>这一份 Main 的暂停是不是走公共概念常量，且没留着任何旧概念名。</summary>
    private static bool UsesPublicPauseConcept(string text)
        => text.Contains("InputConcept.TogglePause", StringComparison.Ordinal)
           && !text.Contains("battle_pause", StringComparison.Ordinal)
           && !text.Contains("arena_toggle_pause", StringComparison.Ordinal)
           && !text.Contains("ui_accept", StringComparison.Ordinal);

    // === 六、玩家按键与调试桥走同一个方法，不各写一份（ADR 0014 收的那一条） ===

    /// <summary>
    /// 巨兽竞技场的单步，<b>玩家按键与调试桥走的是同一个方法</b>。
    ///
    /// <para>这一格断的是「同一件事两处代码」那一族：收编前玩家侧自己写
    /// <c>_world.Advance(1)</c> 加 <c>_deck.Sync(...)</c>，桥上的 <c>StepDebug</c> 写逐字同形的两行，
    /// 两份各自会漂。收编的形状是<b>两条触发、汇进一个方法</b>，不是把其中一份删掉。</para>
    ///
    /// <para>另两个时间旋钮不在这一格：变速玩家侧调的就是桥上 <c>SetSpeed</c>（本来就是一条路），
    /// 暂停桥上没有对应动词（那是「只有一处」，不是「两处」）。</para>
    /// </summary>
    [Fact]
    public void 巨兽的单步_玩家按键调的是桥上那个方法_不自己再抄一份()
    {
        var text = File.ReadAllText(PrototypeMain("BeastArena"));

        text.ShouldContain("StepDebug();", Case.Sensitive,
            "玩家侧单步那一档没有调 StepDebug —— 收编的形状是两条触发汇进一个方法");

        // 玩家侧那一档的方法体里不许再出现自己推进世界的那两行。
        // 判法：取 PollTimeKnobs 的方法体，里面不该再有 Advance(1) / _deck.Sync。
        var body = Between(text, "private void PollTimeKnobs()", "public string StepDebug");
        body.ShouldNotBeEmpty(
            "没截到 PollTimeKnobs 的方法体 —— 两个标记里有一个对不上，取出来是空串，下面几条就成了空过");
        body.ShouldNotContain("_world.Advance(", Case.Sensitive,
            "PollTimeKnobs 里自己推进了世界 —— 同一件事两处代码，先漂的必然是这一份");
        body.ShouldNotContain("_deck.Sync(", Case.Sensitive,
            "PollTimeKnobs 里自己同步了画面 —— 同上");
        body.ShouldContain("InputConcept.StepOnce", Case.Sensitive, "单步那一档本身还在，只是触发汇进桥上了");
    }

    /// <summary>取两个标记之间那一段（标记本身不含在结果里）。找不到起始标记就返回空串。</summary>
    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        if (from < 0) return string.Empty;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? string.Empty : text[from..to];
    }

    [Fact]
    public void 暂停守卫抓得住_临时树里塞一个旧概念名照样判红()
    {
        var root = Artifacts.Ensure(TestPaths.RepoRoot, "Input", "GuardProbe");
        var path = Path.Combine(root, "Prototypes", "Probe", "Scripts", "Main.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "#nullable enable\nnamespace DemoFactory.Prototypes.Probe;\n"
            + "public partial class Main { private const string ActionPause = \"battle_pause\"; }\n");

        try
        {
            UsesPublicPauseConcept(File.ReadAllText(path)).ShouldBeFalse(
                "守卫抓不住自己造的样本 —— 上面那条「三个原型共用概念名」就只是三行巧合，没有判据");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // === 六、推进到目标步：三处循环收编成一份（扫源码） ===

    [Fact]
    public void 三处推进循环归零_推进到目标步只剩基类那一份()
    {
        var root = Path.Combine(TestPaths.RepoRoot, "src", "Prototypes");
        var loops = ScanPrototypeSources(root)
            .Select(hit => Describe(hit))
            .ToList();

        loops.ShouldBeEmpty(
            "推进到目标步的循环只剩基类那一份了。手写的推进循环再出现一次，"
            + "逐字同形就会漂成第二种写法（暂停守不守、推的是不是装配那三件都分叉）：\n"
            + string.Join("\n", loops));
    }

    [Fact]
    public void 推进循环守卫抓得住_临时树里塞一个while目标步照样判红()
    {
        var root = Artifacts.Ensure(TestPaths.RepoRoot, "Input", "AdvanceGuardProbe");
        var path = Path.Combine(root, "Prototypes", "Probe", "Scripts", "Logic", "ProbeSetup.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "#nullable enable\nnamespace Probe;\npublic static class ProbeSetup\n{\n"
            + "    public static void AdvanceTo(ProbeWorld world, long targetTick)\n    {\n"
            + "        while (world.Tick < targetTick) { world.Advance(1); }\n    }\n}\n");

        try
        {
            FindAdvanceLoops(path).ShouldNotBeEmpty("守卫抓不住自己造的样本 —— 收编那条就没人守了");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 三个世界的推进钩子都转发给基类那一份()
    {
        foreach (var setup in new[]
                 {
                     Path.Combine(TestPaths.RepoRoot, "src", "Prototypes", "InfinityLord", "Scripts", "Logic", "ReplaySetup.cs"),
                     Path.Combine(TestPaths.RepoRoot, "src", "Prototypes", "BattleArray", "Scripts", "Logic", "DuelRiftSetup.cs"),
                     Path.Combine(TestPaths.RepoRoot, "src", "Prototypes", "BeastArena", "Scripts", "Logic", "ArenaSetup.cs"),
                 })
        {
            File.Exists(setup).ShouldBeTrue($"找不到 {Path.GetFileName(setup)}");
            var text = File.ReadAllText(setup);
            text.ShouldContain("world.AdvanceTo(", Case.Sensitive,
                $"{Path.GetFileName(setup)} 的 AdvanceTo 没转发给世界那份");
        }
    }

    private static string PrototypeMain(string prototype)
        => Path.Combine(TestPaths.RepoRoot, "src", "Prototypes", prototype, "Scripts", "Main.cs");

    /// <summary>手写推进循环的两种形状：<c>while (... &lt; targetTick ...)</c> 与调试直达口写的循环体。</summary>
    private static IEnumerable<(string File, int Line)> FindAdvanceLoops(string path)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isTargetLoop = line.Contains("while", StringComparison.Ordinal)
                               && line.Contains("targetTick", StringComparison.Ordinal);
            if (isTargetLoop) yield return (path, i + 1);
        }
    }

    private static IEnumerable<(string File, int Line)> ScanPrototypeSources(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(FindAdvanceLoops);

    private static string Describe((string File, int Line) hit)
        => $"{hit.File}:{hit.Line}";
}
