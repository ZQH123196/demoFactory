#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Base;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.FrontEnd;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.Pathfinding;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 前端层的离线用例 —— 菜单栈与设置项的规则，全在这里判，不用起引擎。
///
/// <para><b>为什么这两块值得单独判</b>：菜单栈的 LIFO 与「重复开不叠层」、设置的钳位与越界抛，
/// 是这一层唯一有分支的地方；这些分支静态就能判，钉在纯 C# 这一侧，五条测试线秒级跑。
/// 真正只能出图验的（挡没挡住、字上没上屏、鼠标收没收回来）归 UI 试验场那一格
/// （<c>docs/GugaFramework/前端.md</c>）。</para>
///
/// <para>纯 C#，不引 Godot 命名空间（与本目录其余用例同一档）。</para>
/// </summary>
public sealed class FrontEndTests
{
    [Fact]
    public void 栈是后进先出_从设置退格回暂停屏而不是全关()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause);
        stack.Push(FrontEndScreen.Settings);

        stack.Close_退一层_应回暂停屏();
    }

    [Fact]
    public void 重复开同一屏不叠层()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause).ShouldBeTrue("第一下 Esc 该开出一层暂停屏");
        stack.Push(FrontEndScreen.Pause).ShouldBeFalse("连按两下 Esc 不该叠两层 —— 否则退一格还停在暂停屏，退不掉");
        stack.Depth.ShouldBe(1, "叠层被拒之后栈深应仍是 1");
    }

    [Fact]
    public void 空栈上按退出键不打开任何屏()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.None).ShouldBeFalse("None 不是一屏，不该被推进栈");
        stack.Depth.ShouldBe(0);
        stack.Top.ShouldBe(FrontEndScreen.None);
    }

    [Fact]
    public void 退空归None而不是报错()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause);
        stack.Pop().ShouldBe(FrontEndScreen.None, "退掉最后一层是「菜单全关了」这个正常结局，不是错误");
        stack.Depth.ShouldBe(0);
    }

    [Fact]
    public void 换根只留最上面那一屏()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Title);
        stack.Push(FrontEndScreen.Settings);
        stack.KeepTopOnly();

        stack.Depth.ShouldBe(1, "换根与清空的分别：换根是最上面那层留着，不是一层不留");
        stack.Top.ShouldBe(FrontEndScreen.Settings);
    }

    // === 加了 Result 值之后：栈语义一条都不许变 ===

    [Fact]
    public void 加了结算屏之后_设置叠暂停那条次序不变()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause);
        stack.Push(FrontEndScreen.Settings);

        stack.Close_退一层_应回暂停屏();
    }

    [Fact]
    public void 加了结算屏之后_换根仍只留最上面那一屏()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Result);
        stack.Push(FrontEndScreen.Settings);
        stack.KeepTopOnly();

        stack.Depth.ShouldBe(1);
        stack.Top.ShouldBe(FrontEndScreen.Settings, "KeepTopOnly 留的是最上面那层，不是「除结算屏以外」");
    }

    [Fact]
    public void 开结算屏_栈里只剩它()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause);

        stack.PushExclusive(FrontEndScreen.Result).ShouldBeTrue();

        stack.Depth.ShouldBe(1, "结算屏是暂停屏的替身，不是一层 —— 栈里该只剩它");
        stack.Top.ShouldBe(FrontEndScreen.Result);
        stack.Contains(FrontEndScreen.Pause).ShouldBeFalse("暂停屏被替掉了，不该还留在栈里");
    }

    [Fact]
    public void 开结算屏_退一层就全关_不留下一层能退()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause);
        stack.PushExclusive(FrontEndScreen.Result);

        stack.Pop().ShouldBe(FrontEndScreen.None,
            "结算屏之上没有一层能退 —— 退掉它就回空栈，回暂停屏的话玩家会看到一局已经打完的世界");
    }

    [Fact]
    public void 开结算屏_挡得住重复开_不叠两层()
    {
        var stack = new MenuStack();
        stack.PushExclusive(FrontEndScreen.Result);
        stack.PushExclusive(FrontEndScreen.Result);

        stack.Depth.ShouldBe(1);
    }

    [Fact]
    public void 开None那一档_退回来_栈不变()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause);
        stack.PushExclusive(FrontEndScreen.None).ShouldBeFalse("None 不是一屏");

        stack.Depth.ShouldBe(1, "被拒的那一次不许把原来那层也清掉");
        stack.Top.ShouldBe(FrontEndScreen.Pause);
    }

    [Fact]
    public void 读数里念得出结算屏()
    {
        var stack = new MenuStack();
        stack.Push(FrontEndScreen.Pause);
        stack.PushExclusive(FrontEndScreen.Result);

        stack.Readout().ShouldContain("top=Result");
        stack.Readout().ShouldContain("depth=1");
    }

    // === 冻结口径：真值在基类那一处，闸在装配那一步 ===

    [Fact]
    public void 冻结后推进那一处推进口直接回假()
    {
        // 用一个<b>装齐五步</b>的世界：这一格要断的是「冻住之后推不动」。
        // 拿残壳来断会被缺层守卫先拦住 —— 那是另一条守卫（ticket 01 定的次序：守卫排在暂停与冻结之前），
        // 两者混在一起就分不清是哪一道闸拦的。
        var world = new AssembledWorld();

        world.Advance(1).ShouldBeTrue("对照组：没冻结、层也齐了，这一步推得动");
        var before = world.Tick;

        world.FreezeTime();
        world.TimeFrozen.ShouldBeTrue();

        world.Advance(1).ShouldBeFalse("局结束 = 冻住时间轴");
        world.AdvanceTo(999);   // 回 void：冻结中它连步都不走
        world.Tick.ShouldBe(before, "步号也要断：只断 false 分不清是冻结拦的还是暂停拦的");
    }

    [Fact]
    public void 缺层守卫排在冻结之前_残壳抛缺层那条而不是回假()
    {
        // 这一条断的是次序：装配没齐时推进当场抛「缺哪几层」，
        // 不被冻结那一格悄悄吞成 false —— 后者会把「装配缺层」这条真 bug 盖住。
        var bare = new BareWorld();

        Should.Throw<InvalidOperationException>(() => bare.Advance(1), "层没齐：抛缺层，不回假");

        bare.FreezeTime();
        Should.Throw<InvalidOperationException>(() => bare.Advance(1), "冻着也一样抛缺层 —— 守卫排在冻结之前");
    }

    // === 三处原型各接了什么（读源码判，静态就能判） ===

    [Fact]
    public void 三个原型的标题屏都被填上了()
    {
        foreach (var (proto, _) in Protos())
        {
            ReadAll(proto).ShouldContain("SetTitleEntries", Case.Sensitive, $"{proto} 没有递标题屏条目 —— 那它开出来的就是「（本屏条目由原型递进来）」那一行占位字");
        }
    }

    [Fact]
    public void 暂停屏框架三颗与巨兽递进的三条都在()
    {
        // 暂停屏 = 继续 / 操作说明 / 设置 / 重开一局 / 回标题（外加框架兜底的退出游戏）。
        // 继续 / 设置 / 退出游戏 由框架摆（FrontEndLayer.BuildPauseEntries），
        // 操作说明 / 重开一局 / 回标题 由原型经 SetPauseEntries 递 —— 这里两头各判一遍。
        var layer = File.ReadAllText(TestPaths.FromRoot("src", "GugaFramework", "FrontEnd", "FrontEndLayer.cs"));
        layer.ShouldContain("SetPauseEntries", Case.Sensitive, "框架没有 SetPauseEntries 这一发 —— 原型条目没处递");
        var buildAt = layer.IndexOf("private void BuildPauseEntries", StringComparison.Ordinal);
        buildAt.ShouldBeGreaterThan(0, "FrontEndLayer 里没有 BuildPauseEntries 这一段");
        var pauseBuild = layer[buildAt..];
        pauseBuild.ShouldContain("继续", Case.Sensitive, "框架的「继续」被挪走了");
        pauseBuild.ShouldContain("设置", Case.Sensitive, "框架的「设置」被挪走了");
        pauseBuild.ShouldContain("退出游戏", Case.Sensitive, "框架的「退出游戏」被挪走了");

        var main = Read("BeastArena", "Main.cs");
        var callAt = main.IndexOf("SetPauseEntries(", StringComparison.Ordinal);
        callAt.ShouldBeGreaterThan(0, "巨兽没在暂停屏递条目 —— 暂停屏还是三颗");
        var call = main[callAt..Math.Min(main.Length, callAt + 400)];
        call.ShouldContain("操作说明", Case.Sensitive, "暂停屏缺「操作说明」");
        call.ShouldContain("重开一局", Case.Sensitive, "暂停屏缺「重开一局」");
        call.ShouldContain("回标题", Case.Sensitive, "暂停屏缺「回标题」");
    }

    [Fact]
    public void 三个原型都订阅了局开始与局结束两个事件()
    {
        foreach (var (proto, _) in Protos())
        {
            var src = ReadAll(proto);
            src.ShouldContain("RunStarted", Case.Sensitive, $"{proto} 没有订阅局开始");
            src.ShouldContain("RunEnded", Case.Sensitive, $"{proto} 没有订阅局结束");
            src.ShouldContain("NotifyRunStarted", Case.Sensitive, $"{proto} 没有报局开始");
            src.ShouldContain("NotifyRunEnded", Case.Sensitive, $"{proto} 没有报局结束");
        }
    }

    [Fact]
    public void 三个原型的冻结都走基类那一处推进口()
    {
        foreach (var (proto, _) in Protos())
        {
            ReadAll(proto).ShouldContain("FreezeTime()", Case.Sensitive, $"{proto} 的冻结没有走基类那一处推进口（要翻回「不冻结」改一处就翻不回来了）");
        }
    }

    [Fact]
    public void 无限领主与巨兽都不再自己画结算屏()
    {
        // 结算屏进公共层之前，这两个原型各画了一套（一个居中 Label、一行 HUD 字）。
        // 留着就还是「三处各写一套」。
        var lord = ReadAll("InfinityLord");
        lord.ShouldNotContain("BuildOutcomeBanner", Case.Sensitive, "无限领主那个居中结算 Label 该删了");

        // 只判 HUD 那一件：结算那两个字仍要在巨兽这个原型里出现（结算屏的标题就是它），
        // 判整个原型目录会把「文案还在用」误判成「HUD 还在画」。
        var deck = Read("BeastArena", "DeckView.cs");
        deck.ShouldNotContain("守住了", Case.Sensitive, "结算那一句该由前端层的结算屏画，不留在 HUD 上");
        deck.ShouldNotContain("失守了", Case.Sensitive, "结算那一句该由前端层的结算屏画，不留在 HUD 上");
    }

    [Fact]
    public void 巨兽推进循环里不再读世界的Outcome()
    {
        var main = Read("BeastArena", "Main.cs");
        var loopAt = main.IndexOf("_tickAccumulator +=", StringComparison.Ordinal);
        loopAt.ShouldBeGreaterThan(0, "没找到推进循环那一段");

        // 循环头到循环体结束那一段里读 Outcome = 表现层主动查世界（那是三处里唯一一处倒挂）。
        var guard = main[loopAt..Math.Min(main.Length, loopAt + 260)];
        guard.ShouldNotContain("Outcome", Case.Sensitive, "推进循环的停止条件改成局结束事件写的那一格，不再读世界");
    }

    // === 本组用例的小工具 ===

    /// <summary>三个原型的入口文件（本组要判的接线都在这三个文件里）。</summary>
    private static (string Proto, string File)[] Protos()
        =>
        [
            ("InfinityLord", "Main.Save.cs"),
            ("BattleArray", "Main.cs"),
            ("BeastArena", "Main.cs"),
        ];

    private static string Read(string proto, string file)
        => File.ReadAllText(TestPaths.FromRoot("src", "Prototypes", proto, "Scripts", file));

    private static string ReadAll(string proto)
    {
        var dir = TestPaths.FromRoot("src", "Prototypes", proto);
        return string.Join('\n', Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
    }

    /// <summary>
    /// 一个<b>故意不装齐五步装配</b>的最小世界：只够断「缺层守卫排在冻结之前」那一条次序。
    /// 它推不动是设计，不是缺陷。
    /// </summary>
    private sealed class BareWorld : GameLogicBase
    {
    }

    /// <summary>
    /// 一个<b>装齐五步装配</b>的最小世界 —— 断「冻结之后推不动」要用的那个。
    ///
    /// <para><b>为什么不拿 <see cref="BareWorld"/> 断冻结</b>：残壳推不动有两条完全不同的原因
    /// （缺层、冻结），混在一起断出来的 false 说明不了是哪一道闸拦的。
    /// 场地取 8×8、格长 1.0 米、阻挡表空表 —— 够跑这一步就行，本组不关心绕障。</para>
    /// </summary>
    private sealed class AssembledWorld : GameLogicBase
    {
        public AssembledWorld()
        {
            var grid = new NavGrid(new NavGridSpec
            {
                Space = new SpaceId("frontend-freeze"),
                CellMeters = 1.0,
                CellsX = 8,
                CellsZ = 8,
                OriginX = 0.0,
                OriginZ = 0.0,
                BlockedCells = [],
            });

            Assembly.BuildNavPipeline(new NavPipelineSpec
            {
                Space = grid.Space,
                Grid = grid,
                Budget = new NavSolveBudget { PerRequestExpansions = 512 },
                Recompute = new NavRecomputeTuning { GoalMoveToleranceMeters = 0.25, NotArrivedStepCap = 4 },
                CacheCapacity = 64,
                RecordCapacity = 256,
                TrackedRequestCapacity = 128,
                Move = new NavMoveTuning { StepSeconds = 0.1, LookAheadMeters = 1.0, StepSolveExpansions = 256 },
                Gate = null,
                Separation = null,
                Events = null,
            });

            Assembly.BuildConstruction(new FreePlacementRule(), []);
            Assembly.AttachTerrain(c => new NavCell(c.Col, c.Row));
        }
    }

    [Fact]
    public void 读数按开屏顺序自底向上念()
    {
        var stack = new MenuStack();
        stack.Readout().ShouldContain("depth=0");

        stack.Push(FrontEndScreen.Pause);
        stack.Push(FrontEndScreen.Settings);
        stack.Readout().ShouldContain("Pause > Settings");
        stack.Readout().ShouldContain("depth=2");
    }

    [Fact]
    public void 设置越界当场抛_不静默钳()
    {
        var settings = new FrontEndSettings();
        Should.Throw<ArgumentOutOfRangeException>(() => settings.SetMasterVolume(1.5));
        Should.Throw<ArgumentOutOfRangeException>(() => settings.SetMasterVolume(-0.1));
    }

    [Fact]
    public void 主音量接受边界内的值()
    {
        var settings = new FrontEndSettings();
        settings.SetMasterVolume(0.0);
        settings.MasterVolume.ShouldBe(0.0);
        settings.SetMasterVolume(1.0);
        settings.MasterVolume.ShouldBe(1.0);
    }

    [Fact]
    public void 分辨率两个边长都要大于零()
    {
        var settings = new FrontEndSettings();
        Should.Throw<ArgumentOutOfRangeException>(() => settings.SetResolution(new FrontEndResolution(0, 1080)));
        Should.Throw<ArgumentOutOfRangeException>(() => settings.SetResolution(new FrontEndResolution(1920, -1)));
    }

    [Fact]
    public void 空语言标签当场抛()
    {
        var settings = new FrontEndSettings();
        Should.Throw<ArgumentException>(() => settings.SetLocale("  "));
    }

    [Fact]
    public void 读数用不变文化_小数点是点()
    {
        var settings = new FrontEndSettings();
        settings.SetMasterVolume(0.75);
        // 读数里的数必须是不变文化的点：改了区域设置的机器上不能变逗号，
        // 否则逐字比对就假红。
        settings.Readout().ShouldContain("master=0.75", Case.Sensitive,
            "读数里的小数必须是点（不变文化），改了区域设置的机器上不能变逗号");
        CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator.ShouldBe(".");
    }
}

internal static class MenuStackTestExtensions
{
    /// <summary>从设置屏退一层，应回暂停屏（栈的 LIFO 那一格）。</summary>
    internal static void Close_退一层_应回暂停屏(this MenuStack stack)
    {
        var top = stack.Pop();
        top.ShouldBe(FrontEndScreen.Pause, "从设置退一层应回暂停屏（下面那层留着），不是全关");
        stack.Depth.ShouldBe(1);
    }
}
