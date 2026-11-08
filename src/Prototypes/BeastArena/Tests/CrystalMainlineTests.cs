#nullable enable
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 主线端到端：吞食矿石 → 消化累计 → 甲板落魔晶 → 搬运入库 → 已清剿第二层嵌入 → 二阶门槛满足。
///
/// <para><b>魔晶只经过镇账那一本</b>（<see cref="TownAccounts.Crystal"/>）：产出、搬入、扣减
/// 读的是同一个数，守恒等式每一步都平。谁再另立一本「嵌入用的魔晶账」，这条用例会先红 ——
/// 上一版正是两本账各记一半，测试直写 adapter 才绿、真跑却嵌不进去。</para>
/// </summary>
public class CrystalMainlineTests
{
    private const int Seed = 7;
    private const int Sector = UndergroundSoil.CaveSector;
    private const int Layer = UndergroundSoil.NativeCaveLayer;

    [Fact]
    public void 吞食矿石到二阶门槛_魔晶只经过镇账那一本()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // ---- ① 吞食两块矿石：各 60 点消化，累计 120 点 → 产 1 单位魔晶 ----
        var first = world.Ecology.Find("ore_poor_1")!;
        var second = world.Ecology.Find("ore_poor_2")!;

        PlaceAt(world, first.Position);
        world.Voyage.SetSatiation(10.0);
        world.EatTarget(first.Id).ShouldBe(RejectReason.InvalidArgs);
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);

        world.Ecology.DigestionProgress.ShouldBe(ArenaTuning.OreDigestionGain, 1e-9, "一块矿石 60 点。");
        world.Ecology.CrystalsProduced.ShouldBe(0, "还不满 100，不产魔晶。");
        world.Town.Crystal.ShouldBe(0.0, 1e-9, "产出之前镇账里没有魔晶。");

        PlaceAt(world, second.Position);
        world.EatTarget(second.Id).ShouldBe(RejectReason.InvalidArgs);
        world.Advance(ArenaTime.StepsPerGameMinute * ArenaTuning.EatGameMinutes);

        world.Ecology.DigestionProgress.ShouldBe(2 * ArenaTuning.OreDigestionGain, 1e-9, "120 点。");
        world.Ecology.CrystalsProduced.ShouldBe(1, "满 100 产 1 单位，溢出保留。");
        (world.CrystalOnDeck + world.CrystalCarrying).ShouldBe(1,
            "产出的 1 单位落在甲板上（刚被拿起时算搬运中）。");
        world.Town.Crystal.ShouldBe(0.0, 1e-9, "落甲板还不是入库。");

        // ---- ② 搬运入库：镇账那一本涨到 1，甲板清空 ----
        for (var i = 0; i < 2400 && world.Town.Crystal < 1.0; i++) world.Advance(1);

        world.Town.Crystal.ShouldBe(1.0, 1e-9, "工人或帮工把甲板那一件搬进镇政府仓库。");
        world.CrystalWarehouse.ShouldBe(1);
        world.CrystalOnDeck.ShouldBe(0, "甲板上那一件已入库。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());

        // 把饱食度摆满：后面挖两层 + 清剿的几游戏小时里不再自动觅食，
        // 主线只走这一颗魔晶（否则中途又吞一棵树，账上加第二颗，用例的叙事就糊了）。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);

        // ---- ③ 停靠 → 挖到第二层 → 真打一场清剿 ----
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);

        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out var clearWhy).ShouldBeTrue(clearWhy);
        foreach (var monster in world.Roster.Units
                     .Where(u => u.Role == UnitRole.Monster && !u.LeftTown && u.State != UnitState.Dead)
                     .ToList())
        {
            world.Battle.ApplyDamage(world.Tick, monster, 1000.0);
        }

        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Soil.IsCleared(Sector, Layer).ShouldBeTrue("四只全灭才算清剿成功。");

        // ---- ④ 在已清剿的第二层嵌入：唯一那本账扣 1 ----
        world.EmbedBlockerOf(Sector, Layer).ShouldBe(SoilEmbedBlocker.None);
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidArgs);
        world.Soil.IsEmbedded(Sector, Layer).ShouldBeTrue("嵌进去了。");
        world.Town.Crystal.ShouldBe(0.0, 1e-9, "嵌入从镇账扣掉 1 单位。");
        world.Town.SpentCrystal.ShouldBe(1.0, 1e-9, "扣减记在消耗那一列。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());

        // ---- ⑤ 二阶门槛满足 ----
        world.GateMet(BeastTier.Tier2).ShouldBeTrue("第二层 + 清剿 + 嵌入三项齐了。");
    }

    /// <summary>挖开洞穴那一格（自上而下）。</summary>
    private static void DigCave(ArenaWorld world)
    {
        world.Soil.BeginDigging(Sector, 1, ArenaTuning.DigWorkerThreshold, world.Tick).ShouldBeTrue();
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.BeginDigging(Sector, Layer, ArenaTuning.DigWorkerThreshold, world.Tick).ShouldBeTrue();
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(Sector, Layer).ShouldBeTrue("洞穴那一格挖开了。");
    }

    private static void PlaceAt(ArenaWorld world, NavPoint at)
        => world.MapStepper.ApplyMove(BeastVoyage.BeastId, at, 0.0);
}
