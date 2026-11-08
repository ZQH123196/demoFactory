#nullable enable
using System.Linq;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片2/3 — 悬赏全链：挂赏 → 勇气承接 → 击杀 → 结算发赏 → 退款/释放兜底。
/// （原为 MCP 实测「挂赏 $25 → hero 承接 → 巢穴 300 血磨到 0」的完整消耗战链路，现确定性断言）
/// </summary>
public class BountyFlowTests : LogicWorldTestBase
{
    [Fact]
    public void 挂赏扣款_旗子入池()
    {
        var before = World.Treasury;
        World.PlaceBountyFlag("lair2", 25).ShouldBeTrue();
        World.Treasury.ShouldBe(before - 25);
        var flag = World.Flags.Single(f => f.TargetId == "lair2" && !f.Settled);
        flag.Gold.ShouldBe(25);
        Log.Bounties.Any(b => b.Kind == BountyEventKind.Placed && b.TargetId == "lair2").ShouldBeTrue();
    }

    [Fact]
    public void 挂赏失败三态_金额非法_金库不足_目标无效()
    {
        World.PlaceBountyFlag("lair1", 0).ShouldBeFalse("金额非法");
        World.PlaceBountyFlag("lair1", 999).ShouldBeFalse("金库不足（150 上限）");
        World.PlaceBountyFlag("guild", 10).ShouldBeFalse("玩家方建筑不可挂赏");
        World.PlaceBountyFlag("nope", 10).ShouldBeFalse("目标不存在");
        World.Treasury.ShouldBe(150, "失败路径零扣款");
    }

    [Fact]
    public void 无人承接_目标被顺手击杀_悬赏原路退款()
    {
        World.PlaceBountyFlag("lair2", 25).ShouldBeTrue();
        // 不派承接：直接拆目标（DebugDamageBuilding 与 EngageBuilding 同一收口）
        World.DebugDamageBuilding("lair2", 300).ShouldBeTrue();

        Advance(1); // SettleFlags 下一步结算
        Log.Bounties.Any(b => b.Kind == BountyEventKind.Refunded && b.Gold == 25).ShouldBeTrue("无人承接 → 赏金退回金库");
        World.Treasury.ShouldBe(150, "退款后金库复原（150−25+25）");
        Log.Destroyed.Single().Id.ShouldBe("lair2", "摧毁事件已发");
    }

    [Fact]
    public void 巢穴摧毁_延迟后远端重生_产出链恢复()
    {
        World.Buildings.Count(b => b.Kind == BuildingKind.MonsterLair).ShouldBe(2);

        World.DebugDamageBuilding("lair2", 300).ShouldBeTrue();
        World.Buildings.Count(b => b.Kind == BuildingKind.MonsterLair).ShouldBe(1, "摧毁即移除");
        Log.Destroyed.Any(b => b.Id == "lair2").ShouldBeTrue();

        Advance(LogicWorld.LairRespawnDelayTicks);
        World.Buildings.Count(b => b.Kind == BuildingKind.MonsterLair).ShouldBe(2, "延迟后重生");
        var lair3 = Log.Respawned.Single();
        lair3.Id.ShouldBe("lair3", "重生序号从 lair3 起（不与种子巢穴冲突）");
        lair3.Health.ShouldBe(LogicWorld.LairRespawnHealth);
        lair3.Position.ShouldBe(new System.Numerics.Vector2(65, -25), "旧址距玩家建筑 ≥35 → 原地重建（威胁原点延续）");
        lair3.SpawnIntervalTicks.ShouldBe(LogicWorld.LairSpawnIntervalTicks, "产出节奏恢复");
    }

    [Fact]
    public void 重生选址_被围死的旧址外推到安全距离之外()
    {
        // 排布：把一座玩家建筑贴到 lair2 旧址旁（< MinDistance=35）→ 选址必须外推
        var oldPos = new System.Numerics.Vector2(65, -25);
        var nearby = new Building
        {
            Id = "outpost",
            Kind = BuildingKind.Market,
            Owner = Faction.Hero,
            Position = new System.Numerics.Vector2(64, -24), // 距旧址 ~1.4
            Health = 600,
            SpawnIntervalTicks = 0,
        };
        var result = LogicWorld.PickLairRespawnPosition(oldPos, [.. World.Buildings, nearby]);

        var distanceFromOutpost = System.Numerics.Vector2.Distance(result, nearby.Position);
        distanceFromOutpost.ShouldBeGreaterThanOrEqualTo(LogicWorld.LairRespawnMinDistance, $"外推点距玩家建筑 ≥ {LogicWorld.LairRespawnMinDistance}（远端语义）");
        // 外推方向 = 旧址 − 玩家建筑质心，验证方向一致（点积为正）
        var centroid = World.Buildings
            .Where(b => b.Owner == Faction.Hero)
            .Aggregate(System.Numerics.Vector2.Zero, (acc, b) => acc + b.Position)
            / World.Buildings.Count(b => b.Owner == Faction.Hero);
        var dir = System.Numerics.Vector2.Normalize(oldPos - centroid);
        var offset = result - oldPos;
        System.Numerics.Vector2.Dot(dir, offset).ShouldBeGreaterThan(0, "沿远离玩家建筑质心的方向外推");
    }

    [Fact]
    public void 承接者阵亡_旗子释放回待接池()
    {
        Advance(25); // 首英雄已落场（tick 21）
        World.PlaceBountyFlag("lair2", 25).ShouldBeTrue();
        var hero = Heroes().Single();
        var flag = World.Flags.Single(f => !f.Settled);
        flag.ClaimedByHeroId = hero.Id; // 排布：直接判给英雄（承接语义归决策链测试，这里验证释放链）
        hero.ClaimedFlagId = flag.Id;

        hero.Health = 0; // 承接者阵亡
        Advance(1);

        var released = Log.Bounties.Where(b => b.Kind == BountyEventKind.Released && b.FlagId == flag.Id).ToList();
        released.Count.ShouldBe(1, "释放事件恰好一次");
        released[0].HeroId.ShouldBe(hero.Id);
        flag.Settled.ShouldBeFalse("旗子未结算");
        flag.ClaimedByHeroId.ShouldBeNull("回待接池（其他英雄可接手 — 消耗战涌现的结构基础）");
    }
}
