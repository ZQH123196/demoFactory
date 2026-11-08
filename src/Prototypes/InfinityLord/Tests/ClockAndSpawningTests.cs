#nullable enable
using System.Linq;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 时间轴 — GameClock 契约的中台级回归（LogicWorld 依赖其暂停/调度/防级联语义）。
/// 事件调度：产出链（行会/巢穴）与经济周期的 Schedule 自续写法。
/// </summary>
public class ClockAndSpawningTests : LogicWorldTestBase
{
    [Fact]
    public void 行会每20步产出英雄_巢穴每30步产出怪物()
    {
        var heroCount0 = Heroes().Count;
        var mobCount0 = Monsters().Count;

        Advance(21);
        Heroes().Count.ShouldBe(heroCount0 + 1, "tick 21 首个英雄落场");

        Advance(30);
        Monsters().Count.ShouldBeGreaterThanOrEqualTo(mobCount0 + 1, "巢穴产出节奏");
    }

    [Fact]
    public void 暂停冻结一切_恢复后继续()
    {
        Advance(21);
        var tick = World.Tick;
        var heroes = Heroes().Count;

        World.SetPaused(true);
        Advance(100);
        World.Tick.ShouldBe(tick, "暂停时时间冻结（Advance 空转）");
        Heroes().Count.ShouldBe(heroes, "暂停时无产出");
        World.Advance(50).ShouldBeFalse("暂停时 Advance 返回 false");

        World.SetPaused(false);
        Advance(1);
        World.Tick.ShouldBe(tick + 1, "恢复后继续推进");
    }

    [Fact]
    public void 快进等价于逐步推进_世界演化确定性()
    {
        var a = new LogicWorld(randomSeed: 42);
        var b = new LogicWorld(randomSeed: 42);
        try
        {
            for (var i = 0; i < 100; i++)
            {
                a.Advance(1);
            }
            b.Advance(100); // 一次推完

            a.DebugSnapshot().ShouldBe(b.DebugSnapshot(), "逐步推进与批量快进的终态完全一致（时间轴语义 — DebugSnapshot 同源）");
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }
}