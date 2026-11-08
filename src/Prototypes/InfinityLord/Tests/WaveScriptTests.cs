#nullable enable
using System;
using System.Linq;
using System.Numerics;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 切片 10「终局」第 2 步 — 剧本波次表（：数值与强度由 AI 自定，不再确认）。
///
/// 设计口径：
/// - 威胁曲线 = **剧本波次表**（Majesty 2 本尊即剧本脚本）：固定时间点刷强化怪小队，参数表驱动，
///   确定性可测可调可断言 — 巢穴涓流（按 LairSpawnIntervalTicks 一只地精）保留不变，波次是叠在上面的加压层。
/// - 波次从**剧本入侵点**落场，不依赖巢穴存活：清剿巢穴不等于关掉剧本压力
///   （否则外环威胁可被一个内环动作永久消除，曲线就没了斜率）。
/// - 每一波的数值必须严格上台阶 — 「成长失去了目标」的反面修正。
/// 验证 seam：LogicWorld.WaveTable（静态表）+ WaveStarted 事件 + Units 快照。
/// </summary>
public class WaveScriptTests : LogicWorldTestBase
{
    // === 切 1：表本身（时间点 + 数值台阶） ===

    [Fact]
    public void 波次表_三个固定时间点且数值逐波上台阶()
    {
        var table = LogicWorld.WaveTable;

        table.Select(w => w.AtTick).ShouldBe(new[] { 600, 1200, 1800 }, false, "第 1/2/3 波分别在 1:00 / 2:00 / 3:00 落场（按修正后的移速重新定档，波间仍等距、波间留建设窗口）");

        table.Select(w => w.No).ShouldBe(new[] { 1, 2, 3 }, false, "波次编号连续");

        for (var i = 1; i < table.Length; i++)
        {
            // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
            // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
            // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

            table[i].Health.ShouldBeGreaterThan(table[i - 1].Health, $"第{i + 1}波血量高于上一波");
            table[i].Attack.ShouldBeGreaterThan(table[i - 1].Attack, $"第{i + 1}波攻击高于上一波");
            // 这一处是 xUnit v3 **原生**的 Assert.Multiple(Action[])：单个 lambda 就是其中
            // 一个 Action，块内多条断言照旧聚合判红——语义与迁移前 NUnit 的
            // Assert.Multiple(() => { ... }) 一致，所以不必拆成顺序执行。
            Assert.Multiple(() =>
            {
                Threat(table[i]).ShouldBeGreaterThan(Threat(table[i - 1]), $"第{i + 1}波威胁值上台阶");
                table[i].Name.ShouldNotBe(table[i - 1].Name, "每波换一种怪（可读的加压）");
            });        }
    }

    /// <summary>威胁值与勇气公式分母同式（LogicWorld.EstimateThreat 的单位口径：攻击×2 + 血量/5）。</summary>
    private static float Threat(WaveRow row) => row.Attack * 2 + row.Health / 5f;

    // === 切 2：到点落场，数值与表一致 ===

    [Fact]
    public void 第1波到点_强化地精小队按表落场()
    {
        FortifyPalace(); // 零干预下宫殿会在 1 分钟前被推平（判负）— 本测试只看波次层
        Advance(599);
        Monsters().Count(m => m.Name == "强化地精").ShouldBe(0, "第 1 波前不提前到场");

        Advance(2); // 第 600 步调度到期 → 第 601 步落场

        var squad = Monsters().Where(m => m.Name == "强化地精").ToList();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        squad.Count.ShouldBe(3, "第 1 波 = 强化地精 ×3");
        squad.All(m => m.MaxHealth == 60).ShouldBeTrue("血量按表");
        squad.All(m => m.Attack == 9).ShouldBeTrue("攻击按表");
        squad.All(m => m.Side == Faction.Monster).ShouldBeTrue("阵营是怪物方");    }

    [Fact]
    public void 三波依次到点_每波各自小队落场()
    {
        FortifyPalace(); // 波次层单独观察：排除「宫殿提前被毁 → 判负」的干扰

        Advance(3005);

        // 断言读的是**播报流**（落场那一刻的数），不是 3005 步之后的存活数：
        // 移动换成一份移动解之后怪物真的进得了城，先落场的那两波会被英雄清掉 ——
        // 实测第 1 波在第 1500 步前后就已全歼。存活数答的是「还活着几只」，
        // 而这一节要证的是「表上三波各自按时、按量落了场」。
        Log.Waves.Select(w => w.No).ShouldBe(new[] { 1, 2, 3 }, false, "每波播报一次（表现层挂提示）");
        Log.Waves.Select(w => w.Count).ShouldBe(
            LogicWorld.WaveTable.Select(w => w.Count), false, "每波上报的只数照表");
        Log.Waves.Select(w => w.Name).ShouldBe(LogicWorld.WaveTable.Select(w => w.Name), false, "每波换一种怪");
    }

    [Fact]
    public void 波次播报带波号与数值行()
    {
        FortifyPalace();
        Advance(901);

        var wave = Log.Waves.Single();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        wave.No.ShouldBe(1);
        wave.Name.ShouldBe("强化地精");
        wave.Count.ShouldBe(3);    }

    // === 切 3：剧本压力不依赖巢穴（清剿巢穴关不掉曲线） ===

    [Fact]
    public void 巢穴全毁_波次照样到点压境()
    {
        FortifyPalace();
        foreach (var lair in World.Buildings.Where(b => b.Kind == BuildingKind.MonsterLair).ToList())
        {
            World.DebugDamageBuilding(lair.Id, 9999).ShouldBeTrue("排布前提：巢穴全部摧毁");
        }

        Advance(601);

        Monsters().Count(m => m.Name == "强化地精").ShouldBe(3, "波次从剧本入侵点落场 — 清剿巢穴只关掉涓流，关不掉外环加压曲线");
    }

    // === 切 4：波次怪走同一套袭城 AI（谁近打谁 — 零改动） ===

    [Fact]
    public void 波次怪落场后向玩家建筑推进()
    {
        FortifyPalace();
        Advance(601);
        var squad = Monsters().Where(m => m.Name == "强化地精").ToList();
        var playerBuildings = World.Buildings.Where(b => b.Owner == Faction.Hero).ToList();
        var distAtSpawn = squad.Min(m => playerBuildings.Min(b => Vector2.Distance(m.Position, b.Position)));

        Advance(60);

        var squadAlive = squad.Where(m => m.Health > 0).ToList();
        squadAlive.ShouldNotBeEmpty("排布前提：60 步（6 秒）后小队仍有存活者");
        var distLater = squadAlive.Min(m => playerBuildings.Min(b => Vector2.Distance(m.Position, b.Position)));

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        distAtSpawn.ShouldBeGreaterThan(60f, "落场点在剧本入侵点（远离玩家建筑）");
        distLater.ShouldBeLessThan(distAtSpawn, "波次怪照旧袭城 — 与涓流怪同一套 AI，零改动");    }

    // === 切 5：涓流与波次共存（曲线是叠加层，不是替换） ===

    [Fact]
    public void 巢穴涓流地精不受波次表影响()
    {
        Advance(LogicWorld.LairSpawnIntervalTicks + 1); // 两座种子巢穴各出 1 只（涓流按 LairSpawnIntervalTicks）

        var trickle = Monsters().First();
        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。

        trickle.Name.ShouldBe("地精", "巢穴涓流仍是基础地精（节奏见 LairSpawnIntervalTicks，未被波次改写）");
        trickle.MaxHealth.ShouldBe(30);
        trickle.Attack.ShouldBe(5);    }
}
