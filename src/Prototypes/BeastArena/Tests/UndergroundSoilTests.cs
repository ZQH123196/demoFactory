#nullable enable
using System.Linq;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 地下土壤：2 层 × 4 象限共 8 格，自上而下开挖、单处洞穴长在象限 0 的第 2 层、
/// 采矿产出、按层速回长与塌方、清剿后转高危、嵌入魔晶后永久停长并提速材料。
/// </summary>
public class UndergroundSoilTests
{
    private const int Seed = 7;

    private const int CaveSector = UndergroundSoil.CaveSector;
    private const int CaveLayer = UndergroundSoil.NativeCaveLayer;

    /// <summary>把洞穴那一格挖到第二层（自上而下，每层一游戏小时）。</summary>
    private static ArenaWorld DigCave(ArenaWorld world)
    {
        world.Soil.BeginDigging(CaveSector, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.BeginDigging(CaveSector, CaveLayer, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(CaveSector, CaveLayer).ShouldBeTrue("洞穴那一格挖开了。");
        return world;
    }

    /// <summary>把那一格摆成「已清剿」（清剿成功那一格由 <see cref="UndergroundSoil.MarkCaveCleared"/> 写）。</summary>
    private static ArenaWorld ClearedCave(ArenaWorld world)
    {
        DigCave(world);
        world.Soil.MarkCaveCleared(CaveSector, CaveLayer).ShouldBeTrue("清剿成功。");
        return world;
    }

    [Fact]
    public void 地下是两层四象限八格()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        UndergroundSoil.Sectors.ShouldBe(4, "4 个象限。");
        UndergroundSoil.Layers.ShouldBe(2, "2 层。");
        UndergroundSoil.CellCount.ShouldBe(8, "共 8 个土壤格。");
        ArenaTuning.SoilLayerDepthMeters.ShouldBe(3.0, "每层深 3 米。");
        ArenaTuning.SoilSectorSpanMeters.ShouldBe(ArenaTuning.DeckSpanTier1Meters / 2.0, "象限是一阶甲板的四分之一见方。");
        ArenaTuning.SoilSectorSpanMeters.ShouldBe(24.0, "甲板 48 米见方 → 象限 24×24 米（跟着一阶甲板边长走，二阶扩容不带着土壤长）。");

        // 8 格固定输出，一格一段。
        var segments = world.Soil.DebugSegment().Split(" | ");
        segments.Length.ShouldBe(8, "地下段固定 8 格。");
        segments[0].ShouldStartWith("SOIL 0_1 ");
        segments[7].ShouldStartWith("SOIL 3_2 ");
    }

    [Fact]
    public void 开局八格都完好_没有挖开也没有清剿或嵌入()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        for (var sector = 0; sector < 4; sector++)
        {
            for (var layer = 1; layer <= 2; layer++)
            {
                world.Soil.IsDug(sector, layer).ShouldBeFalse($"{sector}_{layer} 开局没挖。");
                world.Soil.IsCleared(sector, layer).ShouldBeFalse($"{sector}_{layer} 开局没清剿。");
                world.Soil.IsEmbedded(sector, layer).ShouldBeFalse($"{sector}_{layer} 开局没嵌入。");
                world.Soil.MinersAt(sector, layer).ShouldBe(0, $"{sector}_{layer} 开局没人。");
                world.Soil.RegrowthProgressAt(sector, layer).ShouldBe(0.0, $"{sector}_{layer} 回长进度从零起。");
            }
        }
    }

    [Fact]
    public void 地下不占任何寻路空间()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var agentsBefore = world.DeckStepper.AgentCount;
        world.Soil.BeginDigging(0, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);

        // 挖开地下那一格不该在甲板或大地图的网格上落任何阻挡，也不该在甲板上添单位。
        world.DeckStepper.AgentCount.ShouldBe(agentsBefore, "地下不开寻路网格：挖开那一格不在甲板上添单位。");
        world.Terrain.Missed.ShouldBe(0L, "地下那一摊不进地形接线。");
    }

    [Fact]
    public void 开挖一格要派满三人挖一个游戏小时()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 人不满不开工。
        world.Soil.BeginDigging(0, 1, 2, world.Tick).ShouldBeTrue("派工本身受理。");
        world.Soil.IsDigging(0, 1).ShouldBeFalse("不足 3 人不开工。");

        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(0, 1).ShouldBeFalse("一个游戏小时后还没派满，仍没挖开。");

        world.Soil.BeginDigging(0, 1, 3, world.Tick);
        world.Soil.IsDigging(0, 1).ShouldBeTrue("派满 3 人就开工。");
        world.Advance(ArenaTime.StepsPerGameHour - 1);
        world.Soil.IsDug(0, 1).ShouldBeFalse("差一步没到。");

        world.Advance(1);
        world.Soil.IsDug(0, 1).ShouldBeTrue("满一个游戏小时挖开。");
        world.Soil.IsDigging(0, 1).ShouldBeFalse("挖完这一格就不再挖。");
    }

    [Fact]
    public void 同象限自上而下_上一层没开下一层开不了()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 第 2 层还没开就点它：受理不了。
        world.Soil.BeginDigging(1, 2, 3, world.Tick).ShouldBeFalse("上一层没挖开，挖它等于挖悬空。");

        // 逐层往下：1 → 2，第 1 层挖开之后第 2 层才点得动。
        foreach (var layer in new[] { 1, 2 })
        {
            world.Soil.BeginDigging(1, layer, 3, world.Tick).ShouldBeTrue($"第 {layer} 层该能挖。");
            world.Soil.IsDigging(1, layer).ShouldBeTrue($"第 {layer} 层开工。");
            world.Advance(ArenaTime.StepsPerGameHour);
            world.Soil.IsDug(1, layer).ShouldBeTrue($"第 {layer} 层挖开。");
        }
    }

    [Fact]
    public void 回长按层速_第二层每游戏日两层_第一层每天一层()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Soil.BeginDigging(2, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(2, 1).ShouldBeTrue("第 1 层挖开了。");

        // 第 1 层 1 层/游戏日：到下一个整游戏日之前一直空着，跨过那一刻才长满。
        var boundary = ((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay;
        world.Advance((int)(boundary - world.Tick - 1));
        world.Soil.IsDug(2, 1).ShouldBeTrue("还没到整游戏日，仍是空腔。");
        world.Soil.RegrowthProgressAt(2, 1).ShouldBe(0.0, "回长只挂在整游戏日上。");

        world.Advance(1);
        world.Soil.IsDug(2, 1).ShouldBeFalse("跨过整游戏日那一刻长满。");

        // 第 2 层 2 层/游戏日：同样一个游戏日就长满，而且长满之后同一天里
        // 上面那一层解除悬空也跟着长满（这一条就是两档速率的差别所在）。
        world.Soil.BeginDigging(2, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.BeginDigging(2, 2, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsDug(2, 2).ShouldBeTrue("第 2 层挖开了。");

        var nextBoundary = ((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay;
        world.Advance((int)(nextBoundary - world.Tick));
        world.Soil.IsDug(2, 2).ShouldBeFalse("第 2 层一个游戏日长满（2 层速）。");
        world.Soil.IsDug(2, 1).ShouldBeFalse("第 2 层一满，第 1 层同一天就长满。");

        ArenaTuning.RegrowthLayersPerGameDayDeep.ShouldBe(2.0, "第 2 层每游戏日 2 层。");
        ArenaTuning.RegrowthLayersPerGameDayOuter.ShouldBe(1.0, "第 1 层每游戏日 1 层。");
    }

    [Fact]
    public void 回长填满已挖格引发塌方()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Soil.BeginDigging(1, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.BeginDigging(1, 2, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.AssignMiners(1, 2, 3);

        var before = world.Soil.Collapses.Count;

        // 一个游戏日：自下而上第 2 层先长满（塌），接着第 1 层长满（再塌一次）。
        world.Advance(ArenaTime.StepsPerGameDay);

        var fresh = world.Soil.Collapses.Skip(before).ToList();
        fresh.ShouldNotBeEmpty("回长填满已挖格就该引发塌方。");
        fresh[0].Sector.ShouldBe(1, "塌的是第 1 象限。");
        fresh[0].Layer.ShouldBe(2, "自下而上，最深那一层先塌。");
        fresh[0].MoodDrop.ShouldBe(ArenaTuning.CollapseTownMoodDrop, "全镇情绪 -10。");

        // 两层在同一天依次长满：两格各塌一次，次序自下而上。
        fresh.Select(c => c.Layer).ShouldBe(new[] { 2, 1 }, "自下而上依次塌。");

        world.Soil.IsDug(1, 2).ShouldBeFalse("长满之后那一格不再是空腔。");
        world.Soil.MinersAt(1, 2).ShouldBe(0, "在格人员已经重伤或死亡，岗位得腾出来。");
    }

    [Fact]
    public void 挖开本身不即得货物()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Soil.BeginDigging(0, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);

        world.Soil.IsDug(0, 1).ShouldBeTrue("挖开了。");
        world.Soil.OreAccumulatedAt(0, 1).ShouldBe(0.0, "挖开不产矿石。");
        world.Soil.MaterialAccumulatedAt(0, 1).ShouldBe(0.0, "挖开不产特殊材料。");
    }

    [Fact]
    public void 单处洞穴在象限零的第二层_四只原生怪()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        UndergroundSoil.NativeCaveLayer.ShouldBe(2, "原生洞穴层是第 2 层。");
        UndergroundSoil.CaveSector.ShouldBe(0, "单处洞穴在第一象限。");
        UndergroundSoil.CellCount.ShouldBe(8, "四象限 × 两层。");

        // 四个象限都挖到第 2 层：只有象限 0 那一格是洞穴，其余都不是。
        for (var sector = 0; sector < 4; sector++)
        {
            world.Soil.BeginDigging(sector, 1, 3, world.Tick);
            world.Advance(ArenaTime.StepsPerGameHour);
            world.Soil.BeginDigging(sector, 2, 3, world.Tick);
            world.Advance(ArenaTime.StepsPerGameHour);

            world.Soil.IsDug(sector, 2).ShouldBeTrue($"{sector}_2 挖开了。");
            var expected = UndergroundSoil.IsCaveCell(sector, 2) ? ArenaTuning.NativeCaveMonsters : 0;
            world.Soil.CaveMonstersAt(sector, 2).ShouldBe(expected, $"{sector}_2 洞里那几只。");
        }

        UndergroundSoil.IsCaveCell(0, 2).ShouldBeTrue("象限 0 第 2 层是那处洞穴。");
        UndergroundSoil.IsCaveCell(1, 2).ShouldBeFalse("别的象限没有洞。");
        UndergroundSoil.IsCaveCell(0, 1).ShouldBeFalse("第 1 层没有洞。");
    }

    [Fact]
    public void 其余七格挖开不遇洞()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        for (var sector = 0; sector < 4; sector++)
        {
            for (var layer = 1; layer <= 2; layer++)
            {
                world.Soil.BeginDigging(sector, layer, 3, world.Tick);
                world.Advance(ArenaTime.StepsPerGameHour);
                world.Soil.IsDug(sector, layer).ShouldBeTrue($"{sector}_{layer} 挖开了。");
                if (UndergroundSoil.IsCaveCell(sector, layer)) continue;

                world.Soil.CaveMonstersAt(sector, layer).ShouldBe(0, $"{sector}_{layer} 不是洞穴格。");
            }
        }
    }

    [Fact]
    public void 每个挖开的格开三个采矿岗位_每人在岗每两游戏小时产一矿石()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Soil.BeginDigging(0, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);

        // 没挖开就没有岗位。
        world.Soil.AssignMiners(2, 1, 3).ShouldBe(0, "没挖开的格派不上人。");

        world.Soil.AssignMiners(0, 1, 3).ShouldBe(3, "挖开的格开 3 个岗位。");
        world.Soil.MinersAt(0, 1).ShouldBe(3, "三个矿工在岗。");

        // 一人在岗 2 游戏小时 = 1 矿石；3 人 2 游戏小时 = 3 矿石。
        world.Advance(ArenaTime.StepsPerGameHour * 2);
        world.Soil.OreAccumulatedAt(0, 1).ShouldBe(3.0, 1e-9, "3 人 × 2 游戏小时 = 3 矿石。");
        world.Soil.MaterialAccumulatedAt(0, 1).ShouldBe(0.0, "不是高危区就不产特殊材料。");
    }

    [Fact]
    public void 岗位按人数封顶三个()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Soil.BeginDigging(0, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);

        world.Soil.AssignMiners(0, 1, 9).ShouldBe(3, "一格只有 3 个岗位，派 9 个也只坐得下 3 个。");
        world.Soil.AssignMiners(0, 1, 0).ShouldBe(0, "撤人回零。");
    }

    [Fact]
    public void 高危生产区每人每四游戏小时额外产一特殊材料()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        DigCave(world);
        world.Soil.AssignMiners(CaveSector, CaveLayer, 3);
        world.Soil.MarkCaveCleared(CaveSector, CaveLayer).ShouldBeTrue("清剿成功后转成高危区。");

        world.Advance(ArenaTime.StepsPerGameHour * 4);

        world.Soil.OreAccumulatedAt(CaveSector, CaveLayer).ShouldBe(6.0, 1e-9, "3 人 × 4 游戏小时 ÷ 2 = 6 矿石。");
        world.Soil.MaterialAccumulatedAt(CaveSector, CaveLayer).ShouldBe(3.0, 1e-9,
            "3 人 × 4 游戏小时 ÷ 4 = 3 特殊材料。");
    }

    [Fact]
    public void 下面一层还开着空腔时上面那层不回长()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(world);
        world.Soil.MarkCaveCleared(CaveSector, CaveLayer).ShouldBeTrue("清剿成功。");

        // 嵌入第二层：它永久不回长，于是上面那一层一直悬空，也不该长回去。
        world.Town.ProduceCrystal(1.0);
        world.EmbedCrystal(CaveSector, CaveLayer).ShouldBe(RejectReason.InvalidArgs);

        world.Advance(ArenaTime.StepsPerGameDay * 3);
        world.Soil.IsDug(CaveSector, CaveLayer).ShouldBeTrue("嵌入之后第二层永久不回长。");
        world.Soil.IsDug(CaveSector, 1).ShouldBeTrue("下面还开着，第 1 层也不长。");
        world.Soil.RegrowthProgressAt(CaveSector, 1).ShouldBe(0.0, "第 1 层回长进度没动。");
    }

    [Fact]
    public void 塌方吓跑两成游客_按在镇游客数取人()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        ArenaTuning.CollapseVisitorFleeShare.ShouldBe(0.20, "塌方吓跑在镇游客的 20%。");

        // 每 5 人取 1 人（末尾不足一批按同一比例从头取）。
        world.Soil.VisitorsFledBy(100).ShouldBe(20, "100 人吓跑 20 人。");
        world.Soil.VisitorsFledBy(10).ShouldBe(2, "10 人吓跑 2 人。");
        world.Soil.VisitorsFledBy(4).ShouldBe(0, "不足 5 人时按同一比例取不到一个。");
        world.Soil.VisitorsFledBy(0).ShouldBe(0, "没有游客就没人跑。");
    }

    [Fact]
    public void 已清剿未嵌入的格会回长_回长后洞穴重置成四只()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ClearedCave(world);
        world.Soil.CaveMonstersAt(CaveSector, CaveLayer).ShouldBe(0, "清完洞里是空的。");

        // 未嵌入 → 按层速回长（第 2 层 2 层/游戏日）。
        world.Advance(ArenaTime.StepsPerGameDay);
        world.Soil.IsDug(CaveSector, CaveLayer).ShouldBeFalse("未嵌入的已清剿格会回长。");
        world.Soil.IsCleared(CaveSector, CaveLayer).ShouldBeFalse("长回去就没有清剿状态了。");
        world.Soil.CaveMonstersAt(CaveSector, CaveLayer)
            .ShouldBe(ArenaTuning.NativeCaveMonsters, "洞穴重置，洞里重新住满四只。");

        // 重新挖开：洞里还是四只，且不算清剿成功。
        world.Soil.BeginDigging(CaveSector, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.BeginDigging(CaveSector, CaveLayer, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour);
        world.Soil.IsCleared(CaveSector, CaveLayer).ShouldBeFalse("重挖不算清剿。");
        world.Soil.CaveMonstersAt(CaveSector, CaveLayer).ShouldBe(ArenaTuning.NativeCaveMonsters);
    }

    [Fact]
    public void 嵌入消耗一单位魔晶_永久停止回长_同格不可重复()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        ClearedCave(world);
        world.Town.ProduceCrystal(2.0);

        world.EmbedCrystal(CaveSector, CaveLayer).ShouldBe(RejectReason.InvalidArgs, "嵌进去了。");
        world.Soil.IsEmbedded(CaveSector, CaveLayer).ShouldBeTrue("格上记着已嵌入。");
        world.Town.Crystal.ShouldBe(1.0, 1e-9, "扣掉 1 单位魔晶。");
        world.Growth.Tier2Eligible.ShouldBeTrue("嵌入完成即永久记下二阶资格。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());

        // 同格不可重复：第二发被拒，魔晶不再扣。
        world.EmbedCrystal(CaveSector, CaveLayer).ShouldBe(RejectReason.InvalidPhase);
        world.Town.Crystal.ShouldBe(1.0, 1e-9, "重复嵌入不再扣魔晶。");

        // 永久停止回长：跨过多个游戏日仍是空腔。
        world.Advance(ArenaTime.StepsPerGameDay * 3);
        world.Soil.IsDug(CaveSector, CaveLayer).ShouldBeTrue("嵌入后永久不回长。");
        world.Soil.RegrowthProgressAt(CaveSector, CaveLayer).ShouldBe(0.0, "回长进度一动不动。");
    }

    [Fact]
    public void 嵌入的四道门_停靠_已清剿_第二层_有魔晶()
    {
        // ① 不在停靠期：拒。
        var undocked = ArenaWorldBuilder.New(Seed).Build();
        ClearedCave(undocked);
        undocked.Town.ProduceCrystal(1.0);
        undocked.EmbedBlockerOf(CaveSector, CaveLayer).ShouldBe(SoilEmbedBlocker.NotDocked);
        undocked.EmbedCrystal(CaveSector, CaveLayer).ShouldBe(RejectReason.InvalidPhase);
        undocked.Town.Crystal.ShouldBe(1.0, 1e-9, "被拒不扣。");

        // ② 没清剿：拒。
        var uncleared = ArenaWorldBuilder.New(Seed).Build();
        uncleared.DockedAt(BigMapGrid.PoorCity.Id);
        DigCave(uncleared);
        uncleared.Town.ProduceCrystal(1.0);
        uncleared.EmbedBlockerOf(CaveSector, CaveLayer).ShouldBe(SoilEmbedBlocker.NotCleared);
        uncleared.EmbedCrystal(CaveSector, CaveLayer).ShouldBe(RejectReason.InvalidPhase);

        // ③ 不是第二层：拒。
        uncleared.Soil.IsDug(CaveSector, 1).ShouldBeTrue();
        uncleared.EmbedBlockerOf(CaveSector, 1).ShouldBe(SoilEmbedBlocker.NotCaveLayer);
        uncleared.EmbedCrystal(CaveSector, 1).ShouldBe(RejectReason.InvalidArgs);

        // ④ 仓库里没有魔晶：拒（可读的缺货原因由词表那一侧写）。
        var noCrystal = ArenaWorldBuilder.New(Seed).Build();
        noCrystal.DockedAt(BigMapGrid.PoorCity.Id);
        ClearedCave(noCrystal);
        noCrystal.Town.Crystal.ShouldBe(0.0, "开局没有魔晶。");
        noCrystal.EmbedBlockerOf(CaveSector, CaveLayer).ShouldBe(SoilEmbedBlocker.NoCrystal);
        noCrystal.EmbedCrystal(CaveSector, CaveLayer).ShouldBe(RejectReason.InsufficientMaterial);
        noCrystal.Soil.IsEmbedded(CaveSector, CaveLayer).ShouldBeFalse("没扣成也没嵌上。");
    }

    [Fact]
    public void 嵌入后特殊材料从每岗四小时一件提速到两小时一件()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        ClearedCave(world);
        world.Soil.AssignMiners(CaveSector, CaveLayer, 3);
        world.Town.ProduceCrystal(1.0);
        world.EmbedCrystal(CaveSector, CaveLayer).ShouldBe(RejectReason.InvalidArgs);

        world.Advance(ArenaTime.StepsPerGameHour * 2);

        world.Soil.MaterialAccumulatedAt(CaveSector, CaveLayer).ShouldBe(3.0, 1e-9,
            "3 人 × 2 游戏小时 ÷ 2 = 3 特殊材料（嵌入档）。");
        ArenaTuning.EmbeddedMaterialGameHoursPerMiner.ShouldBe(2.0);
        ArenaTuning.MaterialGameHoursPerMiner.ShouldBe(4.0, "未嵌入那一档仍是 4 小时一件。");
    }

    [Fact]
    public void 土壤与甲板两个空间互不串味()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 地下那一摊推进不影响甲板上任何一格阻挡。
        var church = world.Construction.Find("church")!;
        var cell = DeckBlueprints.ToNavCell(church.Cells[0]);

        world.Advance(1);
        var blockedBefore = world.DeckGrid.IsBlocked(cell);
        blockedBefore.ShouldBeTrue("教堂的阻挡在甲板网格上。");

        world.Soil.BeginDigging(0, 1, 3, world.Tick);
        world.Soil.BeginDigging(1, 1, 3, world.Tick);
        world.Advance(ArenaTime.StepsPerGameHour * 2);

        world.DeckGrid.IsBlocked(cell).ShouldBe(blockedBefore, "挖地下不动甲板阻挡。");
        world.Soil.IsDug(0, 1).ShouldBeTrue("地下照挖。");
    }
}
