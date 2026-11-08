#nullable enable
using System;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 命令词表契约 — <c>Scripts/Logic/ReplaySetup.cs</c>（全库唯一那份）的三条约定各有钉子：
///
/// 1. **失败一律 return false，永不抛。** 未知命令、字段数不符、数值解析失败、逻辑层拒绝，
///    四种同款。理由是录制的 ok 字段是权威（<see cref="ReplayRunner.Hooks.Inject"/> 原文
///    「返回实际 ok，与录制时必须一致」）——抛异常会让「录进去的东西」有一类重放不了。
/// 2. **args 一律规范化后再入录**（kind 记枚举名），解析取并集：枚举名 + 界面短名 + 旧名别名。
/// 3. **失败也入录**（未知命令除外——它不是一次写，是客户端发了不存在的动词）。
///
/// 这里不 grep 源码做断言：源码形状会变，而上面三条是行为，行为才是契约。
/// </summary>
public class ReplayVerbTableTests
{
    private static LogicWorld NewWorld() => new(randomSeed: 42);

    // ---------- 1. 失败契约：永不抛 ----------

    [Fact]
    public void 未知命令返false且不抛()
    {
        using var world = NewWorld();
        bool ok = false;
        Should.NotThrow(() => ok = ReplaySetup.Inject(world, "NoSuchVerb", "whatever"));
        ok.ShouldBeFalse();
    }

    [Fact]
    public void 词表里的动词配坏参数返false且不抛()
    {
        using var world = NewWorld();

        foreach (var bad in new[]
                 {
                     ("PlaceBountyFlag", ""),
                     ("PlaceBountyFlag", "lair1"),
                     ("PlaceBountyFlag", "lair1|20|30"),
                     ("PlaceBountyFlag", "lair1|not-a-number"),
                     ("RaiseBountyFlag", "flag1|x"),
                     ("PlaceBuilding", "Tavern|40"),
                     // 下面三行盖的是营建词表迁到公共建造模块那一份之后的三种作者错误：
                     // 方向段读不出 / 工期段读不出 / 载荷段带了竖线（竖线是 args 分隔符、全表一份、
                     // 没有转义档，所以载荷自己再带竖线就永远解不开 —— 整条失败，不截断也不降级）。
                     ("PlaceBuilding", "Tavern|40|-40|x"),
                     ("PlaceBuilding", "Tavern|40|-40|0|-|x"),
                     ("PlaceBuilding", "Tavern|40|-40|0|-|-|Hero|1"),
                     ("PlaceBuilding", "Tavern|not-a-number|-40"),
                     ("DebugSetTreasury", "not-a-number"),
                 })
        {
            bool ok = true;
            Should.NotThrow(() => ok = ReplaySetup.Inject(world, bad.Item1, bad.Item2),
                $"命令 {bad.Item1} 参数 {bad.Item2} 抛了");
            ok.ShouldBeFalse($"命令 {bad.Item1} 参数 {bad.Item2} 不该成功");
        }
    }

    [Fact]
    public void 拼错的kind整条失败且不落任何建筑()
    {
        using var world = NewWorld();
        var before = world.Buildings.Count;

        bool ok = true;
        Should.NotThrow(() => ok = ReplaySetup.Inject(world, "PlaceBuilding", "tavren|40|-40"));
        ok.ShouldBeFalse("拼错的 kind 必须整条失败");
        world.Buildings.Count.ShouldBe(before, "未知 kind 不得降级成某个种类再让逻辑层拒绝（那会把映射错误伪装成『不可放置』）");
    }

    [Fact]
    public void 数字串不被当成枚举序号()
    {
        // Enum.TryParse 吃数字串：BuildingKind 的第 3 项是 Market，
        // 所以 "3|40|-40" 若直接透传，会真建出一座市集 —— 那正是「不许降级」要防的伪装。
        ReplaySetup.TryParseBuildingKind("3", out _).ShouldBeFalse("数字串不在接受集里");
        ReplaySetup.TryParseBuildingKind("-1", out _).ShouldBeFalse();
        ReplaySetup.TryParseBuildingKind("+4", out _).ShouldBeFalse();

        using var world = NewWorld();
        world.DebugSetTreasury(600);
        var before = world.Buildings.Count;

        ReplaySetup.Inject(world, "PlaceBuilding", "3|40|-40").ShouldBeFalse();
        world.Buildings.Count.ShouldBe(before, "数字串不得建出任何建筑");
    }

    // ---------- 2. args 规范化：入录记枚举名，解析吃并集 ----------

    // 迁移前是 NUnit 的 [Test] + [Values("tavern", "Tavern", "TAVERN")] 参数化方法。
    // xUnit 里 [Fact] 配内联 [InlineData] 是非法组合（CS0592：特性对方法无效），
    // 对应写法是 [Theory] + 每个值一条独立 [InlineData]。
    [Theory]
    [InlineData("tavern")]
    [InlineData("Tavern")]
    [InlineData("TAVERN")]
    public void kind取并集_三种写法都落成同一座且规范化成枚举名(string token)
    {
        using var world = NewWorld();
        world.DebugSetTreasury(600);

        var ok = ReplaySetup.Inject(world, "PlaceBuilding", $"{token}|40|-40", out var canonical);
        ok.ShouldBeTrue($"写法 {token} 应当被接受");
        canonical.ShouldBe("Tavern|40|-40|0|-|-|Hero/1/0", $"写法 {token} 入录的应是枚举名 + 营建词表那一份的补齐段（方向恒 0 / 建筑 id 不记 / "
            + "工期缺省=蓝图 0 / 载荷=阵营/等级/产出周期）");
    }

    [Fact]
    public void 游侠公会旧名规范化成枚举名()
    {
        using var world = NewWorld();
        world.DebugSetTreasury(600);

        var ok = ReplaySetup.Inject(world, "PlaceBuilding", "guild_ranger|-45|38", out var canonical);
        ok.ShouldBeTrue();
        canonical.ShouldBe("GuildHall|-45|38|0|-|-|Hero/1/20", "旧名是入口方言，Journal 里记的必须是枚举名（+ 营建词表的补齐段）");
    }

    [Fact]
    public void 两段与单段args的规范化结果原样透传()
    {
        using var world = NewWorld();
        world.DebugSetTreasury(600);

        ReplaySetup.Inject(world, "PlaceBountyFlag", "lair1|20", out var bountyArgs).ShouldBeTrue();
        bountyArgs.ShouldBe("lair1|20", "两段 args 本就规范，原样透传");

        ReplaySetup.Inject(world, "DebugSetTreasury", "600", out var treasuryArgs).ShouldBeTrue();
        treasuryArgs.ShouldBe("600");
    }

    // ---------- 3. 录制纪律：失败也入录，未知命令不入录 ----------

    [Fact]
    public void 逻辑层拒绝也入录且ok为假()
    {
        using var rec = ReplaySetup.ForSeed(42);
        rec.Inject("DebugSetTreasury", ReplaySetup.Args(600));

        // 贴着行会放市集 → 过近拦截
        var ok = rec.Inject("PlaceBuilding", ReplaySetup.Args(BuildingKind.Market, new System.Numerics.Vector2(5, 5)));
        ok.ShouldBeFalse();
        rec.Journal.Count.ShouldBe(2, "被拒的那一笔也要入录 —— 拒绝是可观察行为");
        rec.Journal[1].Ok.ShouldBeFalse();
        rec.Journal[1].Args.ShouldBe("Market|5|5|0|-|-|Hero/1/0", "入录的应是规范化后的 args（营建词表那一份的补齐段也照录）");
    }

    [Fact]
    public void 未知命令不入录()
    {
        using var rec = ReplaySetup.ForSeed(42);
        rec.Inject("NoSuchVerb", "whatever");
        rec.Journal.ShouldBeEmpty("客户端发了不存在的动词不是一次写，不该进 Journal");
    }

    [Fact]
    public void 未开录时只跑不记()
    {
        var world = new LogicWorld(randomSeed: 42);
        using var rec = ReplaySetup.Recorder(world, 42);
        rec.EndSession();

        var ok = rec.Inject("DebugSetTreasury", ReplaySetup.Args(600));
        ok.ShouldBeTrue();
        rec.Journal.ShouldBeEmpty("未开录时写路径照跑，只是不入录");
        world.Treasury.ShouldBe(600, "未开录不等于不执行");
    }

    // ---------- 合成输入：账上可读，重放端跳过（2026-10-05 用户裁决） ----------

    [Fact]
    public void 合成点击入录且点位定点化()
    {
        using var rec = ReplaySetup.ForSeed(42);
        rec.Inject("SyntheticInputClick", "900|600|left|1");

        rec.Journal.Count.ShouldBe(1, "合成点击是一次写 —— 玩家这一手要能在账上读到");
        rec.Journal[0].Cmd.ShouldBe("SyntheticInputClick");
        rec.Journal[0].Args.ShouldBe("900|600|left|1");
        rec.Journal[0].Ok.ShouldBeTrue();
    }

    [Fact]
    public void 合成动作按下与抬起各记一笔()
    {
        using var rec = ReplaySetup.ForSeed(42);
        rec.Inject("SyntheticInputAction", "place_market|1");
        rec.Inject("SyntheticInputAction", "place_market|0");

        rec.Journal.Count.ShouldBe(2, "按下与抬起成对 —— 漏一个账上就出现半个按键");
        rec.Journal[0].Args.ShouldBe("place_market|1");
        rec.Journal[1].Args.ShouldBe("place_market|0");
    }

    [Fact]
    public void 合成输入重放端不碰世界()
    {
        using var world = NewWorld();
        var before = world.ReplaySnapshot();
        var buildings = world.Buildings.Count;
        var treasury = world.Treasury;

        // 重放端消费这两条时一律跳过：无引擎、无光标，玩家路径在重放世界里不存在。
        ReplaySetup.Inject(world, "SyntheticInputClick", "900|600|left|1")
            .ShouldBeTrue("跳过也要返 true —— 账上这笔与录制端对齐");
        ReplaySetup.Inject(world, "SyntheticInputAction", "place_market|1")
            .ShouldBeTrue();

        world.Buildings.Count.ShouldBe(buildings, "合成输入那一层不许凭空造出建筑");
        world.Treasury.ShouldBe(treasury, "也不许凭空花钱");
        world.ReplaySnapshot().ShouldBe(before, "跳过 = 状态逐字不变（真变化由紧随的逻辑写承担）");
    }

    [Fact]
    public void 合成输入坏参数判否且空段也算坏()
    {
        using var world = NewWorld();
        var action = "SyntheticInputAction";
        var click = "SyntheticInputClick";

        ReplaySetup.Inject(world, action, "place_market").ShouldBeFalse("少一段");
        ReplaySetup.Inject(world, click, "900|600|left").ShouldBeFalse("少一段");
        ReplaySetup.Inject(world, click, "a|b|left|1").ShouldBeFalse("坐标解析不出");
        // 这条是实测踩到的："|" 切出两个空串，段数正好等于动作动词要的 2 段 ——
        // 只查段数会把空参数当合法入账。
        ReplaySetup.Inject(world, action, "|").ShouldBeFalse("段数够但两段全空，不算合法");
        ReplaySetup.Inject(world, click, "|left|1").ShouldBeFalse("坐标段为空");
    }

    [Fact]
    public void 词表含合成输入那两条()
    {
        ReplaySetup.IsVerb("SyntheticInputAction").ShouldBeTrue();
        ReplaySetup.IsVerb("SyntheticInputClick").ShouldBeTrue();
    }

    [Fact]
    public void 开录清空上一段并抓起点()
    {
        using var rec = ReplaySetup.ForSeed(42);
        rec.Inject("PlaceBountyFlag", ReplaySetup.Args("lair1", 20));
        rec.Mark("t-old");
        rec.Journal.ShouldNotBeEmpty();

        rec.BeginSession();
        rec.Journal.ShouldBeEmpty("开新段要清空上一段");
        rec.Checkpoints.ShouldBeEmpty();
        rec.StartTick.ShouldBe(rec.World.Tick);
        rec.StartSnapshot.ShouldNotBeEmpty();
    }

    // ---------- 4. 词表本身的形状 ----------

    [Fact]
    public void 词表七条名字互不重复()
    {
        // 条数**不写死**（原先写 7）：加一条动词是本原型最常做的改动之一，
        // 钉一个数就变成「每加一条都要改这里一次」，而这一条要证的是「名字不重复、不空」。
        ReplaySetup.VerbNames.ShouldNotBeEmpty();
        ReplaySetup.VerbNames.ShouldBeUnique();
        ReplaySetup.VerbNames.ShouldAllBe(name => !string.IsNullOrWhiteSpace(name));
        ReplaySetup.IsVerb("PlaceUnit").ShouldBeTrue("排布口进词表（位置真值搬进移动解之后它也是一次写）");
    }

    [Fact]
    public void 词表每一条都接得住坏参数且不抛()
    {
        using var world = NewWorld();
        foreach (var verb in ReplaySetup.VerbNames)
        {
            bool ok = true;
            Should.NotThrow(() => ok = ReplaySetup.Inject(world, verb, "|"), $"动词 {verb} 抛了");
            ok.ShouldBeFalse($"动词 {verb} 收到坏 args 不该成功");
        }
    }
}
