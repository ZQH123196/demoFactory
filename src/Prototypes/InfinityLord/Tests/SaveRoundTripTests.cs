#nullable enable
using System;
using System.Linq;
using DemoFactory.GugaFramework.Persistence;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace DemoFactory.Prototypes.InfinityLord.Tests;

/// <summary>
/// 无限领主存档那一半的判据 —— 往返、续走、待触发事件表、寻路登记表、随机流。
///
/// <para><b>主力那一格是往返</b>：<c>WriteState → ReadState → WriteState</c> 两串逐字相等。
/// 它跑不绿只有两种病——有派生数据被存进去了（多写了一份能重算的东西），
/// 或漏了字段（少读了一份）。一个测试顶两件事。</para>
/// </summary>
public class SaveRoundTripTests
{
    private const int Seed = 42;

    [Fact]
    public void 往返两串逐字相等()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(200);

        var first = Write(world);
        var second = Write(Load(world, 200));
        if (first != second)
            throw new InvalidOperationException(
                "WriteState → ReadState → WriteState 两串必须逐字相等：" + FirstDifference(first, second));
    }

    [Fact]
    public void 往返守住三次读档()
    {
        // 一次往返绿了不代表第二次也绿：漏掉的字段常常是「只在第二次才显形」的那一类
        // （例：产出链 id 进过表，读档后表里多了一条，下一读再写就多一条）。
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(300);
        var text = Write(world);
        var twice = Write(Load(world, 300));
        Write(LoadFrom(twice)).ShouldBe(text, "连读两次档之后两串仍要逐字相等");
    }

    [Fact]
    public void 读档后接着走两百步不报错()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(200);
        var loaded = Load(world, 200);

        // 「接着玩」这一格的底线：读档之后世界照常演化 200 步不抛，且步号对得上。
        loaded.Advance(200).ShouldBeTrue();
        loaded.Tick.ShouldBe(400);
        loaded.Units.Count.ShouldBeGreaterThan(0, "读档之后单位还要在场上");
        loaded.Buildings.Count.ShouldBe(7, "场地建筑全集随存档回来");
    }

    [Fact]
    public void 读档后待触发事件照常触发()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(100);
        var loaded = Load(world, 100);
        var before = loaded.Treasury;

        // 不重建待触发事件表的后果：脉搏回来了，但这批结算永远不发生，而且不报错。
        loaded.Advance(120); // 跨过两轮经济周期
        loaded.Treasury.ShouldBeGreaterThan(before,
            "经济周期那一格必须照常触发（它挂的是闭包委托，只能按名字重排）");
    }

    [Fact]
    public void 待触发事件表重建逐字相同()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(200);
        var loaded = Load(world, 200);

        // 不重建的后果：脉搏回来了，但这批结算永远不发生，而且不报错。
        loaded.PendingEvents().ShouldBe(world.PendingEvents(),
            "待触发事件表要按同一份名字、同一份次序重排");
    }

    [Fact]
    public void 待触发事件表里有经济周期与波次()
    {
        var world = new LogicWorld(randomSeed: Seed);
        var names = world.PendingEvents().Select(e => e.Id).ToList();
        names.ShouldContain("经济周期", "经济周期是自续的那一条，不重排它金库永远不再涨");
        names.ShouldContain("BOSS降临");
        names.ShouldContain(name => name.StartsWith("波次#", StringComparison.Ordinal));
    }

    [Fact]
    public void 寻路登记表按原次序重建()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(200);
        var before = world.Nav.Roster().Select(r => r.Id).ToList();
        var loaded = Load(world, 200);

        // 登记次序 = 步进次序。次序错了就是重放分叉，而症状是「读档之后逐步走的路线不一样」。
        loaded.Nav.Roster().Select(r => r.Id).ShouldBe(before, "寻路登记表按原登记次序重建");
    }

    [Fact]
    public void 位置进了存档()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(120);
        var text = Write(world);

        // 位置属权威档：存。只存命令是重算不出位置的——重算要的是上一份解的输入。
        text.ShouldContain("navRoster", Case.Sensitive);
        var loaded = Load(world, 120);
        loaded.Nav.Roster().ShouldBe(world.Nav.Roster(), "位置与朝向逐字还原");
    }

    [Fact]
    public void 随机流读档后接着走()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(200);
        var loaded = Load(world, 200);
        var draws = loaded.RandomDraws;

        world.Advance(80);
        loaded.Advance(80);

        // 不续随机流的话，下一波小队的落点会分岔，而症状要跑到几十步之后才显形 ——
        // 那时读的人已经不知道该看哪一列了。已抽次数续上了，两边的消耗就同步。
        loaded.RandomDraws.ShouldBeGreaterThan(draws, "读档之后随机流要接着抽");
        loaded.RandomDraws.ShouldBe(world.RandomDraws, "同一份已抽次数在两个世界里同步推进");
    }

    [Fact]
    public void 步号两边都在()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(200);
        var loaded = Load(world, 200);
        loaded.Tick.ShouldBe(world.Tick, "时钟步号恢复");
        loaded.Nav.Stepper.StepIndex.ShouldBe(world.Nav.Stepper.StepIndex, "寻路步号与时钟步号是两个独立计数器，两边都要恢复");
    }


    [Fact]
    public void 空世界守卫()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(30);
        var state = Write(world);

        var cursor = new SaveCursor(state);
        cursor.EnterObject();
        Should.Throw<InvalidOperationException>(() => world.ReadState(new SaveReader(cursor)))
            .Message.ShouldContain("空世界", Case.Sensitive, "往已经种过世界的实例上灌，种子那七座会原地叠加");
    }

    [Fact]
    public void 跟系统共享随机的世界存不回来()
    {
        var world = new LogicWorld();
        world.Advance(10);
        Should.Throw<InvalidOperationException>(() => Write(world))
            .Message.ShouldContain("固定种子", Case.Sensitive, "Random 的内部状态存不进去，那一种世界要当场拒");
    }

    [Fact]
    public void 种子对不上当场报错()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(10);
        var loaded = LogicWorld.EmptyForLoad(Seed + 1);
        Should.Throw<InvalidOperationException>(() => loaded.RestoreRandomStream(Seed, 0))
            .Message.ShouldContain("种子", Case.Sensitive, "两个种子续不出同一条随机流");
    }

    [Fact]
    public void 一局的存档远在上限之内()
    {
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(2000);
        long bytes = System.Text.Encoding.UTF8.GetByteCount(Write(world));
        bytes.ShouldBeLessThan(SaveFile.MaxBytes, "10MB 是开发期报警线：一局的存档应当远在它之内");
    }

    [Fact]
    public void 落盘读回来两串相等()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(200);

        var info = store.Write(1, LogicWorld.WorldName, world, world.Tick);
        info.Exists.ShouldBeTrue();
        info.World.ShouldBe(LogicWorld.WorldName);
        info.Tick.ShouldBe(world.Tick);

        var loaded = LogicWorld.EmptyForLoad(Seed);
        var header = store.Read(1, LogicWorld.WorldName, loaded);
        header.Tick.ShouldBe(world.Tick, "文件头那一列给人和工具念，状态段那一份是 ReadState 消费的那一份");
        Write(loaded).ShouldBe(Write(world), "落盘那一圈不能改字");
    }

    [Fact]
    public void 原型名不符当场报错()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        var world = new LogicWorld(randomSeed: Seed);
        world.Advance(50);
        store.Write(1, LogicWorld.WorldName, world, world.Tick);

        Should.Throw<SaveWorldMismatchException>(() =>
                store.Read(1, "BeastArena", LogicWorld.EmptyForLoad(Seed)))
            .Message.ShouldContain("BeastArena", Case.Sensitive, "读档先比对原型名，不对当场报错");
    }

    // === 样本 ===

    /// <summary>
    /// 两串的第一处不同 —— 断言里只该带这一小段。
    ///
    /// <para>整串两万多字贴进判红消息，读的人要在里面找第二个字开始不同的位置；
    /// 而「第一处不同在哪」就是那一条症状的全部线索。</para>
    /// </summary>
    private static string FirstDifference(string expected, string actual)
    {
        var n = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < n; i++)
        {
            if (expected[i] == actual[i]) continue;
            var from = Math.Max(0, i - 60);
            var len = Math.Min(140, expected.Length - from);
            return $"第一处不同在第 {i.ToString(System.Globalization.CultureInfo.InvariantCulture)} 个字符：\n"
                   + $"应：…{expected.Substring(from, len)}…\n"
                   + $"实：…{actual.Substring(from, Math.Min(140, actual.Length - from))}…";
        }

        return $"两串前缀相同，长度 {expected.Length} 对 {actual.Length}";
    }


    private static string Write(LogicWorld world)
    {
        var w = new SaveWriter();
        world.WriteState(w);
        return w.Result;
    }

    private static LogicWorld Load(LogicWorld source, int steps) => LoadFrom(Write(source));

    private static LogicWorld LoadFrom(string state)
    {
        var loaded = LogicWorld.EmptyForLoad(Seed);
        var cursor = new SaveCursor(state);
        cursor.EnterObject();
        loaded.ReadState(new SaveReader(cursor));
        cursor.LeaveObject();
        return loaded;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "demofactory-ilord-save-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Path)) System.IO.Directory.Delete(Path, recursive: true);
            }
            catch (System.IO.IOException)
            {
                // 临时目录删不掉不是本轮的判据。
            }
        }
    }
}