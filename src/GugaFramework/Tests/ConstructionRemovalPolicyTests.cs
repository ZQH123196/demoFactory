#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 归零去向那一个插件槽的契约测试 —— <b>零引擎依赖</b>。
///
/// 守的是一个设计决定：「耐久打到 0 之后那座还在不在册」是玩法决定，所以它是调用方递进来的
/// 一个委托，不是模块写死的一行。没递的那一档必须与递之前逐字相同（五个原型全走没递那条路）；
/// 递了之后两个归零出口（<see cref="ConstructionCore.Damage"/> 扣到 0、
/// <see cref="ConstructionCore.SetHealth"/> 写进 0）必须问同一个政策，
/// 否则「同一种原因在两条路上给两个答案」这件事就发生了。
/// </summary>
public class ConstructionRemovalPolicyTests
{
    [Fact]
    public void 没递政策_归零照旧出册_与递这个槽之前逐字相同()
    {
        var core = Core(10);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        var hit = core.Damage("b1", 100);
        core.Advance(20);

        hit.Ok.ShouldBeTrue("打空了就是打中了，这一发照样回 ok");
        core.Count.ShouldBe(0, "没递政策 = 归零即出册");
        core.OccupiedCount.ShouldBe(0, "出册的那一条链腾占用");
        core.Find("b1").ShouldBeNull();
        core.Events.Where<BuildingCompleted>().ShouldBeEmpty("已经不在册，Completed 永不发");
        core.Events.Where<BuildingRemoved>().Single().Cause.ShouldBe(RemovalCause.Destroyed);
        core.Events.All.Select(e => e.Wire).ShouldBe(new[] { "placed", "damaged", "removed" });
        core.Commit("keep", new Vector2(0.2f, 0.2f)).Ok.ShouldBeTrue("腾干净了才放得进");
    }

    [Fact]
    public void 没递政策_SetHealth写0仍然当场抛_那个出口不因为新槽而松开()
    {
        var core = Core(100);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        Should.Throw<ArgumentOutOfRangeException>(() => core.SetHealth("b1", 0.0));
        Should.Throw<ArgumentOutOfRangeException>(() => core.SetHealth("b1", -5.0));

        core.Find("b1").ShouldNotBeNull("抛出来的那一发不动册");
    }

    [Fact]
    public void 政策答留_那座留在册上耐久真0_这一发回ok且不发Removed()
    {
        var core = Core(10, static (_, _) => false);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        var hit = core.Damage("b1", 100);

        hit.Ok.ShouldBeTrue("耐久确实打到了 0；留不留是政策给的答案，不是这一次没打中");
        core.Count.ShouldBe(1, "答留 = 仍在册");
        core.Find("b1").ShouldNotBeNull("查得到");
        core.Find("b1")!.Health.ShouldBe(0.0, "停在真 0 上");
        core.Events.Where<BuildingRemoved>().ShouldBeEmpty("没出册就没有 Removed");
        core.Events.All.Select(e => e.Wire).ShouldBe(new[] { "placed", "damaged" },
            "致死的那一下照样看得见（表现层的小动作词表挂在 Damaged 上）");
    }

    [Fact]
    public void 政策答留_那座还占着格_同格放不进第二座()
    {
        var core = Core(10, static (_, _) => false);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.Damage("b1", 100);

        core.OccupiedCount.ShouldBe(3, "留在册上就还占着那几格：腾占用是出册那条链的活");
        core.Commit("keep", new Vector2(0.2f, 0.2f)).Ok.ShouldBeFalse("占着的那几格没腾干净");
        core.Count.ShouldBe(1);
    }

    [Fact]
    public void 政策答留_推进照走_留在册上的那一座照样进工期倒数()
    {
        var core = Core(10, static (_, _) => false);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.Damage("b1", 100);
        core.Advance(10);

        core.Find("b1")!.State.ShouldBe(BuildState.Completed, "它还在册上，Advance 就走得到它");
        core.Events.Where<BuildingCompleted>().Single().Subject.ShouldBe("b1");
    }

    [Fact]
    public void 政策答出册_与没递政策的那一档逐字相同()
    {
        var core = Core(10, static (_, _) => true);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.Damage("b1", 100);
        core.Advance(20);

        core.Count.ShouldBe(0);
        core.OccupiedCount.ShouldBe(0);
        core.Events.Where<BuildingCompleted>().ShouldBeEmpty();
        core.Events.Where<BuildingRemoved>().Single().Cause.ShouldBe(RemovalCause.Destroyed);
        core.Commit("keep", new Vector2(0.2f, 0.2f)).Ok.ShouldBeTrue();
    }

    [Fact]
    public void SetHealth写到0_问同一个政策_答留就留在册上()
    {
        var core = Core(10, static (_, _) => false);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        var set = core.SetHealth("b1", 0.0);

        set.Ok.ShouldBeTrue();
        core.Count.ShouldBe(1, "答留 = 仍在册");
        core.Find("b1")!.Health.ShouldBe(0.0, "停在真 0 上");
        core.Find("b1")!.MaxHealth.ShouldBe(100.0, "0 抬不高任何上限");
        core.Events.Where<BuildingRemoved>().ShouldBeEmpty();
        core.Events.All.Select(e => e.Wire).ShouldBe(new[] { "placed", "health_set" });
    }

    [Fact]
    public void SetHealth写到0_问同一个政策_答出册走同一条Detach链()
    {
        var core = Core(10, static (_, _) => true);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        core.SetHealth("b1", 0.0);

        core.Count.ShouldBe(0);
        core.OccupiedCount.ShouldBe(0);
        core.Events.Where<BuildingRemoved>().Single().Cause.ShouldBe(RemovalCause.Destroyed);
        core.Commit("keep", new Vector2(0.2f, 0.2f)).Ok.ShouldBeTrue("腾干净了才放得进");
    }

    [Fact]
    public void 递了政策_负数仍然当场抛_0是一个状态负数不是()
    {
        var core = Core(100, static (_, _) => false);
        core.Commit("keep", new Vector2(0.2f, 0.2f));

        Should.Throw<ArgumentOutOfRangeException>(() => core.SetHealth("b1", -5.0));

        core.Find("b1").ShouldNotBeNull("抛出来的那一发不动册");
        core.Find("b1")!.Health.ShouldBe(100.0);
    }

    [Fact]
    public void 两个归零出口问到的是同一座建筑与同一条去向()
    {
        var asked = new List<(string Id, RemovalCause Cause, double Health)>();
        var core = Core(10, (building, cause) =>
        {
            asked.Add((building.Id, cause, building.Health));
            return false;
        });

        core.Commit("keep", new Vector2(0.2f, 0.2f));
        core.Commit("keep", new Vector2(4.2f, 0.2f));
        core.Damage("b1", 100);
        core.SetHealth("b2", 0.0);

        asked.Count.ShouldBe(2, "两个出口各问一次");
        asked[0].Id.ShouldBe("b1");
        asked[1].Id.ShouldBe("b2");
        asked[0].Cause.ShouldBe(RemovalCause.Destroyed);
        asked[1].Cause.ShouldBe(RemovalCause.Destroyed);
        asked[0].Health.ShouldBe(0.0, "政策看到的是打空之后那座：耐久已经是 0");
        asked[1].Health.ShouldBe(0.0);
        core.Count.ShouldBe(2, "两座都留在册上");
    }

    // ---- 工具 -------------------------------------------------------------------

    private static CellFootprint Footprint() => CellFootprint.FromCells(new[]
    {
        new GridCell(0, 0), new GridCell(1, 0), new GridCell(0, 1),
    });

    private static ConstructionCore Core(long buildDurationTicks,
        Func<Building, RemovalCause, bool>? removePolicy = null)
        => new(new GridPlacementRule(1, -2, -2, 9, 9),
            new[] { new Blueprint("keep", 100, Footprint(), buildDurationTicks) },
            removePolicy: removePolicy);
}
