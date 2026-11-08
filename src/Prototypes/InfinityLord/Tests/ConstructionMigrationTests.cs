#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 营建迁到公共建造模块之后（issue 05）新钉的钉子 —— 每条只盖一件迁前盖不到的事。
///
/// 迁前那批功能断言（BuildingPlacementTests / EconomyConservationTests / DeterminismSentinelTests
/// 等）证明的是「行为没变」；这一组证明的是「接的是模块那一侧、不是又抄了一份」：
/// 册内座数、落点量化、拒绝原因带回、血量真值在哪、出册收口、产出链的触发点、载荷往返、
/// 快照段的模块列、动词表的「名单外当场抛」。
/// </summary>
public class ConstructionMigrationTests : LogicWorldTestBase
{
    /// <summary>远离全部种子建筑的落点（最小间距外，迁前那批测试同款取法）。</summary>
    private static readonly Vector2 Far = new(-50, -50);

    [Fact]
    public void 场地建筑与玩家营建同在模块册_册内零座是开局那一句的历史账()
    {
        // 盖：2026-10-02 场地建筑进册（RegisterFixture）之后「玩家营建那几座进册、种子那几座不进」
        // 这条判据退役了 —— 现在册里开局就有种子那 7 座，判据换成「两类都进册、坐标与血量归模块」。
        // 迁前那句「开局册内零座」断言的是旧结构（种子不在册内），留着它就是把旧结构钉死。
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Construction.Count.ShouldBe(7, "开局册内 7 座 = 种子那 7 座");
        World.Construction.Ids.ShouldBe(new[]
        {
            "palace", "guild", "market", "tavern", "apothecary", "lair1", "lair2",
        });
        World.Buildings.Count.ShouldBe(7, "句柄表与册同数");
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Construction.Count.ShouldBe(8, "玩家营建那一座也进册");
        World.Construction.OccupiedCount.ShouldBe(8, "占用与册内座数同数（自由放置口径）");
        World.Construction.Ids.ShouldContain("tavern2");    }

    [Fact]
    public void 落点量化到百分之一步长_模块那一行是真值()
    {
        // 盖：落点只有一份真值（D9/D29）。迁前落点不量化，迁后由自由放置规则量化到 0.01；
        // 原型侧那个 Building.Position 必须读模块那一行，不是自己再存一份调用方给的坐标。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, new Vector2(40.1234f, -37.8765f)).ShouldBeTrue();

        var building = World.Buildings.Single(b => b.Id == "tavern2");
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        building.Position.ShouldBe(new Vector2(40.12f, -37.88f), "原型侧读到的就是量化后的落点");
        World.Construction.Find("tavern2")!.Position.ShouldBe(building.Position, "两侧读的是同一个数（原型那一份是转发，不是副本）");    }

    [Fact]
    public void 拒绝不扣款_三种拒绝原因都读得出来()
    {
        // 盖：经济排序契约（spec 3 节第 6 条）+ 拒绝原因可读。金库不足与「巢穴不可放置」由
        // 原型自己判（模块不认识钱，封闭的 PlacementReason 里没有经济那一档，D3）。
        // 金库排布分两段做：先造「差一块」那一档，再把金库排回满的验另外两档 ——
        // 一次只验一个原因，不然头一个拒绝就把后面的挡住了。
        World.DebugSetTreasury(LogicWorld.TavernCost - 1);
        var poor = World.TryPlaceBuilding(BuildingKind.Tavern, Far);
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        poor.Ok.ShouldBeFalse();
        poor.Reason.ShouldContain("金库不足");
        World.Treasury.ShouldBe(LogicWorld.TavernCost - 1, "金库不足：分文不动");
        World.DebugSetTreasury(150);
        var lair = World.TryPlaceBuilding(BuildingKind.MonsterLair, Far);
        // 玩家↔场地的间距由模块判（场地建筑进册并占位）—— 拒绝现在带回模块那一档原因。
        var nearSeed = World.TryPlaceBuilding(BuildingKind.Tavern, new Vector2(-10, -8));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        lair.Ok.ShouldBeFalse();
        lair.Reason.ShouldContain("不可由玩家放置");
        nearSeed.Ok.ShouldBeFalse();
        nearSeed.Reason.ShouldContain("TooClose", Case.Sensitive, "拒绝来自模块那一档（不再是原型自写的那句「过近：与种子建筑间距 < 12」）");
        nearSeed.CoreReason.ShouldBe(PlacementReason.TooClose, "拒绝原因原样带回，幽灵（只读裁决流）与点下去读到的是同一句话");
        World.Treasury.ShouldBe(150, "两次拒绝金库一文没动");
        World.Buildings.Any(b => b.Id.StartsWith("tavern2", StringComparison.Ordinal)).ShouldBeFalse("拒绝不入册");
        Log.BuildingPlaced.ShouldBeEmpty("拒绝不发事件");    }

    [Fact]
    public void 玩家与玩家的间距由模块判_原因原样带回()
    {
        // 盖：几何判定归模块（D4/D12），且它那一档封闭原因要能被原型读出来做「不扣款」的信号。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();

        var tooClose = World.TryPlaceBuilding(BuildingKind.Tavern, Far + new Vector2(8, 0));
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        tooClose.Ok.ShouldBeFalse();
        tooClose.CoreReason.ShouldBe(PlacementReason.TooClose, "8 < 半径和 6+6=12 ⇒ 模块判 TooClose（原样带回，不折成别的档）");
        World.Treasury.ShouldBe(600 - LogicWorld.TavernCost, "只有头一座扣了款");    }

    [Fact]
    public void 玩家建筑掉血走模块_血量真值在模块那一行()
    {
        // 盖：D2 —— 册内那几座的血量账归模块，原型那份 Health 只是转发。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();
        var building = World.Buildings.Single(b => b.Id == "tavern2");

        World.DebugDamageBuilding("tavern2", 100).ShouldBeTrue();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        building.Health.ShouldBe(300, "原型侧读到的就是模块那一份");
        World.Construction.Find("tavern2")!.Health.ShouldBe(300);
        World.ReplaySnapshot().ShouldContain("hp=300/400", Case.Sensitive, "快照那一列印的是血量（「断言比快照」）");    }

    [Fact]
    public void 册内建筑被打空_模块出册_原型事件与句柄同一次收口()
    {
        // 盖：摧毁是去向不是状态（D16）—— 册里不留「已摧毁」的占位，句柄与事件同一次调用里收口。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();
        var seeded = World.Construction.Count; // 7 座场地建筑 + 刚落的那一座

        World.DebugDamageBuilding("tavern2", 400).ShouldBeTrue();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Construction.Count.ShouldBe(seeded - 1, "册里少了一座（场地建筑那几座不动）");
        World.Buildings.Any(b => b.Id == "tavern2").ShouldBeFalse("句柄出列");
        Log.Destroyed.Select(b => b.Id).ShouldBe(new[] { "tavern2" }, Case.Sensitive, "表现层清模型靠这条事件（只发一次）");    }

    [Fact]
    public void 册内建筑直接写血量当场抛_绕不过模块留第二份真值()
    {
        // 盖：血量那一份只有模块能写（D2）。原型的排布口是 DebugHealBuilding / DebugDamageBuilding，
        // 建筑对象上那个 setter 只对不在册的那几座（种子与终幕）有效。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();
        var building = World.Buildings.Single(b => b.Id == "tavern2");

        Should.Throw<InvalidOperationException>(() => building.Health = 999);
        building.Health.ShouldBe(400, "抛异常那发一个字都没改");
    }

    [Fact]
    public void 行会落建的产出链由Completed事件起()
    {
        // 盖：产出链不再从落建入口里发，改成订阅模块的 Completed（工期全 0 ⇒ 与 Placed 在同一次
        // Commit 里同步连发，行为等价 D16~D17）。迁前那一行在 PlaceBuilding 体内，已经删掉。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.GuildHall, Far).ShouldBeTrue();
        World.Buildings.Single(b => b.Id == "guildhall1").SpawnIntervalTicks.ShouldBe(20);

        World.Advance(25); // 产出周期 20 步，落场在脉搏步

        Heroes().Any(h => h.HomeGuildId == "guildhall1").ShouldBeTrue("新行会起产出链（建筑即招募，迁后由 Completed 事件起的那一条）");
    }

    [Fact]
    public void 服务建筑不产单位_不排产出链()
    {
        // 盖：Completed 对每座都发，但产出链只属于产单位的那几座 —— 排一条 0 步的链不是「马上产出」
        // （GameClock.Schedule(0) 当场抛）。迁前只有行会与巢穴起链，迁后门槛落在 StartSpawnChain。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();

        Should.NotThrow(() => World.Advance(30), "落一座不产单位的服务建筑之后照常推进");
    }

    [Fact]
    public void 载荷段编解码往返_载荷真的落在建筑上()
    {
        // 盖：D31 —— 载荷是原型自己的数据（阵营/等级/产出周期），模块存而不读、原样归还；
        // 编解码留在原型适配器，形状是「阵营/等级/产出周期」（段内斜线，竖线留给 args 分隔符）。
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        BuildingPayload.DefaultFor(BuildingKind.GuildWarrior).Encode().ShouldBe("Hero/1/20");
        BuildingPayload.DefaultFor(BuildingKind.Market).Encode().ShouldBe("Hero/1/0");
        BuildingPayload.TryDecode("Monster/2/30", out var back).ShouldBeTrue();
        back.ShouldBe(new BuildingPayload(Faction.Monster, 2, 30));
        BuildingPayload.TryDecode("-", out var fallback, BuildingPayload.DefaultFor(BuildingKind.Tavern)).ShouldBeTrue("整段没给（-）= 照种类给缺省，不是「等级 0」");
        fallback.ShouldBe(BuildingPayload.DefaultFor(BuildingKind.Tavern));
        BuildingPayload.TryDecode("Hero|1|0", out _).ShouldBeFalse("载荷段带竖线解不出（竖线是 args 分隔符）");
        BuildingPayload.TryDecode("0/1/0", out _).ShouldBeFalse("数字串不当阵营用（不降级）");    }

    [Fact]
    public void 参数里的载荷段真的落到那座建筑上()
    {
        // 盖：重放端照参数重建的那座建筑，载荷与录制端逐字相同 —— 载荷段不是摆设。
        World.DebugSetTreasury(600);
        var ok = ReplaySetup.Inject(world: World, cmd: "PlaceBuilding",
            args: "GuildWarrior|40|-40|0|-|0|Hero/2/20");

        ok.ShouldBeTrue();
        var guild = World.Buildings.Single(b => b.Id == "guildwarrior1");
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        guild.Level.ShouldBe(2, "载荷里的等级原样归还（模块不读，原型自己印）");
        guild.Owner.ShouldBe(Faction.Hero);
        guild.SpawnIntervalTicks.ShouldBe(20);    }

    [Fact]
    public void 快照含模块摘要与状态列_段首身份不变()
    {
        // 盖：D32 —— 模块段嵌进本原型的快照：头部带模块摘要，建筑段带状态/在建剩余步/暂停/方向，
        // 原型自己那两列（Owner / lv）在同一段里。段首两个 token 恒为「B + 建筑 id」
        // —— L2 语义档的身份判据靠它（「断言三层」）。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();

        var replay = World.ReplaySnapshot();
        var debug = World.DebugSnapshot();
        var segment = replay.Split(" | ").First(s => s.Contains("B tavern2 "));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        replay.ShouldContain("construction kind=Radius buildings=8 occupied=8", Case.Sensitive, "模块摘要折进头部段（7 座场地 + 1 座玩家营建）");
        debug.ShouldContain("construction kind=Radius buildings=8 occupied=8", Case.Sensitive, "两个快照同一份模块段");
        segment.Split(' ').Take(2).ShouldBe(new[] { "B", "tavern2" }, Case.Sensitive, "段首身份 = 类型 + Id");
        segment.ShouldContain("Hero lv=1", Case.Sensitive, "原型那两列在同一段里");
        segment.ShouldContain("state=completed", Case.Sensitive, "模块的状态列");
        segment.ShouldContain("left=0", Case.Sensitive);
        segment.ShouldContain("paused=0", Case.Sensitive);
        segment.ShouldContain("dir=0", Case.Sensitive);
        var debugLines = debug.Split('\n');
        debugLines.ShouldContain(_l => _l.Contains("B guild "), "种子建筑也有一行（场地建筑进册那一档）");
        debugLines.ShouldContain(_l => _l.Contains(
                "B guild GuildHall Hero lv=1 pos=0,0 hp=1000/1000 state=completed left=0 paused=0 dir=0"),
            "场地建筑那一行与玩家营建同形：坐标 / 血量上限 / 状态列都从模块那一行搬过来");    }

    [Fact]
    public void 名单外的动词当场抛_不拿false表达没这一格()
    {
        // 盖：D34 口径 2 —— 本原型这一侧只接 PlaceBuilding，其余动词名打过来当场抛并写明理由。
        var sink = new InfinityLordConstructionSink(World);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        sink.SupportedVerbs.ShouldBe(new[] { ConstructionVerbs.Place });
        Should.Throw<NotSupportedException>(
            () => ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Remove, "tavern2"));
        Should.Throw<NotSupportedException>(
            () => ConstructionVerbs.Dispatch(sink, ConstructionVerbs.Check, "Tavern|0|0|0"));    }

    [Fact]
    public void 建筑行仍按Id序数排序_不依赖任何遍历顺序()
    {
        // 盖：确定性红线 —— 快照段序照 Id 序数排，与模块册内的登记序无关（两处排序各排各的）。
        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();
        World.PlaceBuilding(BuildingKind.Market, new Vector2(-40, 50)).ShouldBeTrue();

        var ids = World.ReplaySnapshot().Split(" | ")
            .Where(s => s.StartsWith("B ", StringComparison.Ordinal))
            .Select(s => s.Split(' ')[1])
            .ToList();

        ids.ShouldBe(ids.OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void 建成事件按模块Completed发_建模型跟的是它不是Placed()
    {
        // 盖：表现层建模型跟的那条事件。Placed 是「已落座、在建」，Completed 是「建成了」——
        // 工期接上之后那几秒不该先摆出一座成品楼。本轮工期全 0 ⇒ 两条在同一次 Commit 里连发，
        // 所以这里能验的是「建成事件确实发了、且先于任何产出」与「推进不会补发第二条」。
        var completed = new System.Collections.Generic.List<string>();
        World.BuildingCompleted += b => completed.Add(b.Id);

        World.DebugSetTreasury(600);
        World.PlaceBuilding(BuildingKind.Tavern, Far).ShouldBeTrue();
        World.Advance(30);

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        completed.ShouldBe(new[] { "tavern2" }, Case.Sensitive, "建成那一条发一次（推进不补发）");
        World.Construction.Find("tavern2")!.State.ShouldBe(BuildState.Completed, "建成那一刻模块那一行的状态已经是 completed（表现层摆成品楼就是跟着这个读）");    }

    [Fact]
    public void 蓝图带res路径的模型引用_幽灵造型的出处只有这一处()
    {
        // 盖：D29 的模型引用字段 —— 营建幽灵用真模型文件（「真模型」），路径按 res:// 引（「按 res:// 引用」
        // 禁按 uid），由模块的蓝图表给出、模块自己不读。表现层因此不必另抄一份 kind→模型表，
        // 蓝图改了引用幽灵就跟着换，不存在「两份表对不上」那一种漂。
        foreach (var kind in new[]
                 {
                     BuildingKind.Market, BuildingKind.Tavern, BuildingKind.Apothecary,
                     BuildingKind.GuildHall, BuildingKind.GuildWarrior,
                 })
        {
            var path = World.Construction.Blueprints[LogicWorld.BlueprintIdOf(kind)].ModelPath;
            path.ShouldStartWith("res://", Case.Sensitive);
            path.ShouldEndWith(".glb", Case.Sensitive, $"{kind} 的模型引用是 res:// 下的 .glb");
        }
    }

    // ---- 场地建筑进册（2026-10-02）---------------------------------------------------

    /// <summary>
    /// 幽灵与落建同源的那半条钉子 —— issue 05 记的那条「⚠️ 幽灵绿但点击被拒」的机器证明。
    ///
    /// <para>做法是把<b>同一个点</b>问两遍：模块的裁决流（<c>Construction.Check</c>，幽灵只读它）
    /// 与落建入口（<c>TryPlaceBuilding</c>，点击走它），两个读数必须永远一致。
    /// 迁前那半年里这两者在种子那一带是不一致的 —— 幽灵读不到册外的场地建筑，
    /// 于是绿着放，点下去被那个入口自己那段距离判定拒掉。</para>
    ///
    /// <para>取的点覆盖了这份种子布局里所有有代表性的几类：贴行会、贴宫殿、
    /// 行会↔宫殿那道 <b>8 米窄缝的中点</b>、坊市↔药店那道 <b>11.31 米缝的中点</b>、以及一片空地。
    /// 前两类与两条缝是「该拒」的那一侧（半径和 6+6=12，缝比 12 窄 ⇒ 缝里任何一点都过不了），
    /// 空地是「该成」的那一侧（成与不成两档都盖，才不是只钉了拒绝这一半）。</para>
    /// </summary>
    [Fact]
    public void 幽灵与落建同源_模块裁决与落建入口在同样几个点永远一致()
    {
        World.DebugSetTreasury(1_000_000);

        var cases = new (string What, Vector2 At, bool Expected)[]
        {
            ("贴行会（0,0）正东 11 米", new Vector2(11, 0), false),
            ("贴宫殿（-8,0）正北 11 米", new Vector2(-8, 11), false),
            ("行会↔宫殿 8 米窄缝的中点", new Vector2(-4, 0), false),
            ("坊市（-18,8）↔药店（-10,16）11.31 米缝的中点", new Vector2(-14, 12), false),
            ("空地（40,40）", new Vector2(40, 40), true),
        };

        foreach (var (what, at, expected) in cases)
        {
            var core = World.Construction.Check(LogicWorld.BlueprintIdOf(BuildingKind.Tavern), at, 0);
            var door = World.TryPlaceBuilding(BuildingKind.Tavern, at);

            // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
            // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
            // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

            core.Ok.ShouldBe(expected, $"模块裁决（{what}）");
            door.Ok.ShouldBe(core.Ok, $"落建入口与模块裁决必须一致（{what}）—— 迁前正是在这一带两者不一致，幽灵绿而点下去被拒");
            door.CoreReason.ShouldBe(core.Ok ? PlacementReason.None : core.Reason, $"拒绝原因同一档（{what}）");        }
    }

    [Fact]
    public void 场地建筑的血量与坐标真值归模块_原型侧读的是转发()
    {
        // 盖：场地建筑进册之后与玩家营建同形 —— 血量不再留在 LogicWorld 那一侧。
        // 快照里那行也从「hp=8000 pos=(-8,0)」变成模块那套列（「断言比快照」 只比检查点快照，所以钉它）。
        var palace = World.Buildings.Single(b => b.Id == "palace");
        var row = World.Construction.Find("palace")!;

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        palace.Health.ShouldBe(LogicWorld.PalaceHealth, "原型侧读到的就是模块那一份");
        palace.Position.ShouldBe(row.Position, "坐标同理（量化后那一点是唯一的）");
        row.MaxHealth.ShouldBe(LogicWorld.PalaceHealth, "血量上限是蓝图数据（D17）");
        World.ReplaySnapshot().ShouldContain($"hp={LogicWorld.PalaceHealth}/{LogicWorld.PalaceHealth}", Case.Sensitive, "场地建筑那一行按模块格式印");    }

    [Fact]
    public void 场地建筑在册内直接写血量当场抛_血量账只有一条路径()
    {
        // 盖：D2 对场地建筑同样成立 —— 旧的 `palace.Health = …` 那条排布口随进册一起关掉。
        var palace = World.Buildings.Single(b => b.Id == "palace");

        Should.Throw<InvalidOperationException>(() => palace.Health = 999);
        palace.Health.ShouldBe(LogicWorld.PalaceHealth, "抛异常那发一个字都没改");
    }

    [Fact]
    public void 堡垒化抬血量_终局脚本那一发逐字是208000且上限一并抬()
    {
        // 盖：终局脚本第 0 步 `HealBuilding palace|200000` 之后起点快照写的是 hp=208000（8000+200000），
        // 且**上限一并抬到 208000** —— 不抬的话之后任何一次 Repair 都会把它钳回 8000（语义坑）。
        World.DebugHealBuilding("palace", 200_000).ShouldBeTrue();
        var row = World.Construction.Find("palace")!;

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        World.Buildings.Single(b => b.Id == "palace").Health.ShouldBe(208_000, "8000 + 200000 = 208000，与迁前逐字同一个数");
        row.MaxHealth.ShouldBe(208_000, "血量上限被合法抬高（max(现上限, 目标值)）");
        World.ReplaySnapshot().ShouldContain("hp=208000/208000", Case.Sensitive, "快照两列一起落定");    }

    [Fact]
    public void 抬过上限之后Repair只增不减_不把血量反向扣回上限()
    {
        // 盖：D19「Repair 钳到血量上限」在上限被 SetHealth 抬高之后照样成立，只是钳的是新上限。
        // 只抬当前血量不抬上限的话，下面这两发会把 207000 钳回 8000 —— 那不是修血，是扣血，
        // 而且回 ok=true（「修上了」），读数上完全看不出来。
        World.DebugHealBuilding("palace", 200_000);
        World.DebugDamageBuilding("palace", 1_000).ShouldBeTrue();
        var before = World.Buildings.Single(b => b.Id == "palace").Health;
        before.ShouldBe(207_000);

        var repaired = World.Construction.Repair("palace", 10_000_000);
        var after = World.Buildings.Single(b => b.Id == "palace").Health;

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        repaired.Ok.ShouldBeTrue("没修满不算失败：钳到上限是这一发的语义");
        ((double)after).ShouldBe(World.Construction.Find("palace")!.MaxHealth, "钳在抬高后的新上限");
        after.ShouldBe(208_000);
        after.ShouldBeGreaterThan(before, "只增不减（207000 没被扣回 8000）");    }

    [Fact]
    public void 血量绝对值写口的作者错误与未知id_非正数抛未知id回读数()
    {
        // 盖：两类失败的分界（D7）。血量 0 与「归零」是同一件事而归零要走出册那条链，
        // 从这里兜 0 等于给它造第二个出口 —— 所以当场抛而不是回读数。
        var core = World.Construction;

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        Should.Throw<ArgumentOutOfRangeException>(() => core.SetHealth("palace", -1), "负数当场抛");
        Should.Throw<ArgumentOutOfRangeException>(() => core.SetHealth("palace", 0), "零当场抛（归零走 Damage）");

        var ghost = core.SetHealth("没有这座", 100);
        ghost.Ok.ShouldBeFalse("未知 id 回读数不抛");
        ghost.Reason.ShouldBe(PlacementReason.UnknownId);

        core.SetHealth("palace", LogicWorld.PalaceHealth).Ok.ShouldBeTrue("满血照样回 ok —— 它不是经济动作，不继承 Repair 那一档 InvalidState（那是给原型的「不扣款」信号）");    }

    [Fact]
    public void 同种子跑两遍_场地建筑进册与血量写口逐字全等()
    {
        // 盖：确定性红线在这条新路径上重新成立一遍 —— 场地建筑登记在 SeedWorld 里逐字复现，
        // 血量绝对值写口不引入任何随机来源。同种子 + 同一串操作 ⇒ 快照逐字相等。
        var snapshots = new List<string>();
        for (var run = 0; run < 2; run++)
        {
            using var world = new LogicWorld(randomSeed: 42);
            world.DebugSetTreasury(600);
            world.DebugHealBuilding("palace", 200_000);
            world.PlaceBuilding(BuildingKind.GuildHall, Far);
            world.Advance(120);
            world.DebugHealBuilding("guild", 37);
            snapshots.Add(world.ReplaySnapshot());
            world.Dispose();
        }

        snapshots[1].ShouldBe(snapshots[0], $"同种子两跑逐字全等；第一处分歧在第 {FirstDiff(snapshots[0], snapshots[1])} 字符");
    }

    private static int FirstDiff(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            if (a[i] != b[i]) return i;
        }

        return n;
    }
}
