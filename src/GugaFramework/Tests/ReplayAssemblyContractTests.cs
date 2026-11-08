#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 重放装配的<b>形状契约</b> —— 收编前那三份拷贝各答一套，这一份替所有原型答（「重放装配单源」）。
///
/// <para>它防的是漂，不是业务：收口前有一份的 Journal 存原始参数、另两份存规范参数，
/// 三分之二成立而且没有任何测试钉住；检查点守卫三份三种答案。这里把每一条口径钉成一条断言，
/// 换原型、换词表都不用改它。</para>
///
/// <para>用假世界而不是真原型世界：这里要验的是<b>装配那一层的行为</b>，
/// 某个原型的词表对不对是它自己那份测试的事（两边同形念 `til=` 那条纪律同源）。</para>
/// </summary>
public class ReplayAssemblyContractTests
{
    /// <summary>假世界：一个计数器 + 一本可拒绝的账，够装配那一层要的四个成员。</summary>
    private sealed class FakeWorld : IReplayWorld
    {
        public long Tick { get; private set; }

        public bool IsPaused { get; private set; }

        /// <summary>这一发是否被逻辑层拒收。</summary>
        public bool Reject { get; set; }

        public bool Paused
        {
            get => IsPaused;
            set => IsPaused = value;
        }

        public int Advances { get; private set; }

        public bool Advance(int steps = 1)
        {
            if (IsPaused || steps < 1)
            {
                return false;
            }

            Tick += steps;
            Advances++;
            return true;
        }

        public string ReplaySnapshot() => $"tick={Tick.ToString(CultureInfo.InvariantCulture)} reject={Reject}";
    }

    /// <summary>
    /// 假词表：<b>带别名集</b>（界面短名 / 枚举名 / 旧名都认，但规范化只出枚举名那一种）
    /// —— 这一格就是另两份有、那一份没有的那一格。
    /// </summary>
    private static ReplayInject InjectorFor(FakeWorld world, out List<string> reasons)
    {
        reasons = [];
        return (string cmd, string args, out string canonical, out string reason) =>
        {
            canonical = args;
            reason = string.Empty;
            switch (cmd)
            {
                case "Place":
                    if (!ReplayArgs.Split(args, 2, out var two))
                    {
                        reason = "参数要 2 段";
                        return false;
                    }

                    canonical = ReplayArgs.Join("Tavern", two[0]); // 别名折成唯一那个 token
                    return !world.Reject;

                case "Bare":
                    return !world.Reject; // 无别名集那一格的形状：原样即规范

                default:
                    reason = $"命令「{cmd}」不在词表里";
                    return false;
            }
        };
    }

    private static ReplayAssembly Assembly(FakeWorld world, bool recording)
    {
        var inject = InjectorFor(world, out _);
        return new ReplayAssembly(world, 42, ["Place", "Bare"], inject,
            target =>
            {
                while (world.Tick < target && !world.IsPaused)
                {
                    world.Advance((int)Math.Min(250, target - world.Tick));
                }
            },
            recording: recording);
    }

    // ---------- 写路径 ----------

    [Fact]
    public void 未知动词不是一次写_不入录但照样返false()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: true);

        a.Inject("NoSuchVerb", "whatever").ShouldBeFalse();
        a.Journal.ShouldBeEmpty("客户端发了一个不存在的动词，它没走成任何写 —— 不是一次写");
    }

    [Fact]
    public void 已知动词被拒_入录且ok为假_拒绝是可观察行为()
    {
        var world = new FakeWorld { Reject = true };
        using var a = Assembly(world, recording: true);

        a.Inject("Place", "lair1|20").ShouldBeFalse();
        a.Journal.Count.ShouldBe(1);
        a.Journal[0].Ok.ShouldBeFalse();
    }

    [Fact]
    public void Journal记的是规范化后的args_不是调用方原样传进来的串()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: true);

        a.Inject("Place", "lair1|20");
        a.Journal[0].Args.ShouldBe("Tavern|lair1", "同一逻辑事实（建一间酒馆）在 Journal 里只有一种 token");
    }

    [Fact]
    public void 未开录时写路径照跑_只是不入录()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: false);

        a.Inject("Bare", "").ShouldBeTrue();
        a.Journal.ShouldBeEmpty();
        world.Tick.ShouldBe(0, "「未开录」不等于「未执行」");
    }

    [Fact]
    public void 失败原因给当次调用看_不进Journal()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: true);

        a.Inject("Place", "只有一段", out var reason);
        reason.ShouldNotBeEmpty();
        a.Journal[0].Args.ShouldBe("只有一段", "解析失败时规范化没走到，原样入录；原因不进 Journal（脚本只需要「成没成」）");
    }

    // ---------- 检查点 ----------

    [Fact]
    public void 检查点未开录不入记_没有重放方的检查点是查不了的账()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: false);
        a.Mark("t0");
        a.Checkpoints.ShouldBeEmpty();
    }

    [Fact]
    public void 命令与检查点共用一个发生序号_同一步内先命令后检查点才还原得出来()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: true);

        a.Inject("Bare", "");
        a.Mark("first");
        a.Inject("Bare", "");
        a.Mark("second");

        a.Journal.Select(e => e.Seq).ShouldBe(new[] { 0, 2 });
        a.Checkpoints.Select(c => c.Seq).ShouldBe(new[] { 1, 3 });
    }

    // ---------- 三钩子 ----------

    [Fact]
    public void 三钩子里的Inject绕过入录_重放端不产生新账()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: true);
        var hooks = a.Hooks();

        hooks.Inject("Place", "lair1|20").ShouldBeTrue();
        a.Journal.ShouldBeEmpty("重放端走的是词表本身；录制只发生在 assembly.Inject 那一个口上");

        hooks.AdvanceTo(10);
        world.Tick.ShouldBe(10);
        hooks.Snapshot().ShouldBe(world.ReplaySnapshot());
    }

    [Fact]
    public void 开录抓起点_重放端据此对齐()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: false);
        world.Advance(37);

        a.BeginSession();
        a.StartTick.ShouldBe(37);
        a.StartSnapshot.ShouldBe("tick=37 reject=False");
    }

    [Fact]
    public void 开新段清空上一段()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: true);
        a.Inject("Bare", "");
        a.Mark("old");
        a.Journal.ShouldNotBeEmpty();

        a.BeginSession();
        a.Journal.ShouldBeEmpty();
        a.Checkpoints.ShouldBeEmpty();
    }

    // ---------- 脚本导出 ----------

    [Fact]
    public void 脚本导出走公共形状_种子与起点一起带出去()
    {
        var world = new FakeWorld();
        using var a = Assembly(world, recording: true);
        a.Inject("Place", "lair1|20");
        a.Mark("m1");

        var script = a.ToScript();
        script.Seed.ShouldBe(42);
        script.Entries.Count.ShouldBe(1);
        script.Checkpoints.Count.ShouldBe(1);
        script.StartTick.ShouldBe(0);
        script.StartSnapshot.ShouldNotBeNull("开录即抓，第 0 步也是一步");
    }

    // ---------- 交接 ----------

    [Fact]
    public void Dispose不清账本_调用方已经把List的引用递进脚本了()
    {
        var world = new FakeWorld();
        var a = Assembly(world, recording: true);
        a.Inject("Place", "lair1|20");
        a.Mark("m1");

        var script = a.ToScript(); // 脚本持有的是 Journal / Checkpoints 这两个 List 本身
        a.Dispose();

        script.Entries.Count.ShouldBe(1, "using 一收就把交出去的账本清空 = 假绿");
        script.Checkpoints.Count.ShouldBe(1);
    }

    [Fact]
    public void Dispose把世界的交还回调叫一次_装配不拥有世界的生命周期()
    {
        var world = new FakeWorld();
        var disposed = 0;
        var a = new ReplayAssembly(world, 42, ["Bare"],
            (string cmd, string args, out string canonical, out string reason) =>
            {
                canonical = args;
                reason = string.Empty;
                return true;
            },
            _ => { }, onDispose: () => disposed++);

        a.Dispose();
        a.Dispose(); // 幂等
        disposed.ShouldBe(1);
        a.IsRecording.ShouldBeFalse();
    }
}
