#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 洞穴清剿的胜负口径与二阶门槛：四只全灭才算成功、撤退 / 超时算失败、
/// 失败后剩余怪留在洞里可再次发起、清剿成功才转高危；
/// 二阶要「第二层 + 清剿 + 嵌入」三项，代价 40 / 10 / 2000。
/// </summary>
public class CaveAndTierGateTests
{
    private const int Seed = 7;

    private const int Sector = UndergroundSoil.CaveSector;
    private const int Layer = UndergroundSoil.NativeCaveLayer;

    /// <summary>挖开洞穴那一格（自上而下）。</summary>
    private static ArenaWorld DigCave(ArenaWorld world)
    {
        world.Soil.BeginDigging(Sector, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.BeginDigging(Sector, Layer, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(Sector, Layer).ShouldBeTrue("洞穴那一格挖开了。");
        return world;
    }

    private static void KillCaveMonsters(ArenaWorld world)
    {
        foreach (var monster in world.Roster.Units
                     .Where(u => u.Role == UnitRole.Monster && !u.LeftTown && u.State != UnitState.Dead)
                     .ToList())
        {
            world.Battle.ApplyDamage(world.Tick, monster, 1000.0);
            monster.State.ShouldBe(UnitState.Dead, "这一击要真的把它打死。");
        }
    }

    // ---- 撤退与超时 ----

    [Fact]
    public void 撤退算失败_伤亡保留_剩余怪留在洞里_准备后可再次发起()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);

        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out var why).ShouldBeTrue(why);
        var monsters = world.Roster.Units.Where(u => u.Role == UnitRole.Monster).ToList();
        monsters.Count.ShouldBe(ArenaTuning.NativeCaveMonsters);

        // 先打死两只，再撤：洞里应当只剩两只。
        world.Battle.ApplyDamage(world.Tick, monsters[0], 1000.0);
        world.Battle.ApplyDamage(world.Tick, monsters[1], 1000.0);

        var fallen = world.Roster.Find("w1")!;
        world.Battle.ApplyDamage(world.Tick, fallen, 1000.0);
        fallen.State.ShouldBe(UnitState.Dead, "撤退前就倒下的那一位，撤退不会把他救回来。");

        ArenaSetup.Inject(world, "RetreatCave", string.Empty, out _, out var retreatWhy).ShouldBeTrue(retreatWhy);
        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "撤退即收场。");
        fallen.State.ShouldBe(UnitState.Dead, "伤亡保留：死的不复活。");

        world.Soil.IsCleared(Sector, Layer).ShouldBeFalse("撤退不算清剿成功。");
        world.Soil.CaveMonstersAt(Sector, Layer).ShouldBe(2, "剩下两只留在洞里。");
        world.Roster.Units.Count(u => u.Role == UnitRole.Monster
                                     && u.State != UnitState.Dead && !u.LeftTown).ShouldBe(0,
            "留在洞里 = 活着的那几只不在甲板上站着（死的已经成了尸体）。");

        // 准备后可再次发起：只涌出剩下的两只，不重新刷满。
        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out var againWhy).ShouldBeTrue(againWhy);
        world.Roster.Units.Count(u => u.Role == UnitRole.Monster
                                     && u.State != UnitState.Dead && !u.LeftTown).ShouldBe(2,
            "第二次只剩两只活着的原生怪。");

        KillCaveMonsters(world);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Soil.IsCleared(Sector, Layer).ShouldBeTrue("把剩下的两只也打死才算清剿成功。");
    }

    [Fact]
    public void 超时算失败_没有四只全灭就不转高危也不产特殊材料()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);
        world.Soil.AssignMiners(Sector, Layer, 3);

        // 全员不参战：这一场没有任何交火，只能等到时限。
        foreach (var u in world.Roster.Townspeople) u.NonCombatant = true;

        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out var why).ShouldBeTrue(why);
        world.AdvanceTo(world.Battle.EndTick + ArenaTime.StepsPerGameMinute);

        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "时限到即收场（只当保险）。");
        world.Soil.IsCleared(Sector, Layer).ShouldBeFalse("超时不算清剿成功。");
        world.Soil.CaveMonstersAt(Sector, Layer).ShouldBe(ArenaTuning.NativeCaveMonsters,
            "四只都没死，全留在洞里。");

        // 没清剿 → 没有高危产出。
        world.Advance(ArenaTime.StepsPerGameHour * 4);
        world.Soil.MaterialAccumulatedAt(Sector, Layer).ShouldBe(0.0, "失败的那一格不产特殊材料。");
    }

    [Fact]
    public void 清剿成功才转高危生产区_失败不转()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);

        world.Soil.AssignMiners(Sector, Layer, 3);
        world.Advance(ArenaTime.StepsPerGameHour * 4);
        world.Soil.MaterialAccumulatedAt(Sector, Layer).ShouldBe(0.0, "挖开但没清剿：不产特殊材料。");

        ArenaSetup.Inject(world, "ClearCave", $"{Sector}|{Layer}", out _, out _).ShouldBeTrue();
        KillCaveMonsters(world);
        world.Advance(ArenaTime.StepsPerGameMinute);
        world.Soil.IsCleared(Sector, Layer).ShouldBeTrue();

        var before = world.Soil.MaterialAccumulatedAt(Sector, Layer);
        world.Advance(ArenaTime.StepsPerGameHour * 4);
        world.Soil.MaterialAccumulatedAt(Sector, Layer).ShouldBeGreaterThan(before,
            "清剿成功后那一格才产特殊材料。");
    }

    // ---- 二阶门槛与代价 ----

    [Fact]
    public void 二阶门槛三项逐项成立_缺一项都不算()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();

        world.GateMet(BeastTier.Tier2).ShouldBeFalse("开局三项都不到。");

        // ① 只挖到第二层：不够。
        DigCave(world);
        world.GateMet(BeastTier.Tier2).ShouldBeFalse("只挖到第二层，还差清剿与嵌入。");
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var dugWhy).ShouldBeFalse();
        dugWhy.ShouldStartWith($"rejected: {RejectReason.GateNotMet}");
        dugWhy.ShouldContain("清剿", Case.Sensitive, "明细要指出还差洞穴清剿。");

        // ② 加上清剿：还不够。
        world.Soil.MarkCaveCleared(Sector, Layer).ShouldBeTrue();
        world.GateMet(BeastTier.Tier2).ShouldBeFalse("还差嵌入魔晶。");
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var clearedWhy).ShouldBeFalse();
        clearedWhy.ShouldStartWith($"rejected: {RejectReason.GateNotMet}");
        clearedWhy.ShouldContain("嵌入", Case.Sensitive, "明细要指出还差嵌入。");

        // ③ 加上嵌入：三项齐了。
        world.Town.ProduceCrystal(1.0);
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidArgs);
        world.GateMet(BeastTier.Tier2).ShouldBeTrue("第二层 + 清剿 + 嵌入。");
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var okWhy).ShouldBeTrue(okWhy);
    }

    [Fact]
    public void 二阶代价是四十魂石_十份特殊材料_两千金币()
    {
        var cost = ArenaTuning.CostOf(BeastTier.Tier2);
        cost.SoulStone.ShouldBe(40.0, 1e-9);
        cost.SpecialMaterial.ShouldBe(10.0, 1e-9);
        cost.Coin.ShouldBe(2000.0, 1e-9);

        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.Stocked();
        world.Town.ProduceCrystal(1.0);
        DigCave(world);
        world.Soil.MarkCaveCleared(Sector, Layer).ShouldBeTrue();
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidArgs);

        var soul = world.Town.SoulStone;
        var material = world.Town.SpecialMaterial;
        var coin = world.Town.Coin;

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeTrue(why);

        world.Town.SoulStone.ShouldBe(soul - 40.0, 1e-9, "魂石扣 40。");
        world.Town.SpecialMaterial.ShouldBe(material - 10.0, 1e-9, "特殊材料扣 10。");
        world.Town.Coin.ShouldBe(coin - 2000.0, 1e-9, "金币扣 2000。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 可达性_初始两件加三个采矿岗十二游戏小时_覆盖二阶所需的十件()
    {
        ArenaTuning.OpeningSpecialMaterial.ShouldBe(2.0, 1e-9, "开局两件特殊材料。");

        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);
        world.Soil.MarkCaveCleared(Sector, Layer).ShouldBeTrue("洞穴清剿成功。");
        world.Town.ProduceCrystal(1.0);
        world.EmbedCrystal(Sector, Layer).ShouldBe(RejectReason.InvalidArgs, "嵌入后材料产出提速。");

        // 三个采矿岗在嵌入的那一格上干十二游戏小时。
        world.Soil.AssignMiners(Sector, Layer, ArenaTuning.MinePostsPerCell)
            .ShouldBe(ArenaTuning.MinePostsPerCell);
        world.Advance(ArenaTime.StepsPerGameHour * 12);

        var mined = world.Soil.MaterialAccumulatedAt(Sector, Layer);
        mined.ShouldBe(18.0, 1e-9, "3 岗 × 12 游戏小时 ÷ 2 小时一件 = 18 件。");

        var available = ArenaTuning.OpeningSpecialMaterial + Math.Floor(mined);
        available.ShouldBeGreaterThanOrEqualTo(
            ArenaTuning.CostOf(BeastTier.Tier2).SpecialMaterial,
            "初始 2 件 + 十二游戏小时产出覆盖二阶所需的 10 件。");
    }
}
