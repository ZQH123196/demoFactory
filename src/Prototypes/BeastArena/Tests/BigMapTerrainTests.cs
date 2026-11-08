#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 大地图地形表（用户 2026-10-19：四种地形、平面填色；两座老城在草地，沙城在沙漠）。
/// 纯 C# 逻辑直接调，不起引擎。
///
/// <para><b>格即米</b>：格长 1 米，格号就是米号，地形边界那一组数全按米写。
/// 格网是 512 × 512、格号原点 −56（圆盘跨世界 −56..456 米）。</para>
/// </summary>
public class BigMapTerrainTests
{
    [Fact]
    public void 按格心判圆内_每一格都有地形_圆外回null()
    {
        // 判据是格心（格号 + 0.5 米）：格心离圆心不超过 256 米即算陆地。
        // 旧口径取格子左下角（格号本身），边缘上两种口径会判到不同的格 ——
        // 角在墙内、格心在墙外的那些格：旧口径算陆地，新口径算星空；
        // 角在墙外、格心在墙内的那些格：旧口径算星空，新口径算陆地。
        // 两种口径同一格判得不一样的，差的是「角到格心那一段」，最多 √2 ÷ 2 ≈ 0.71 米。
        var missing = 0;
        var outside = 0;
        var flipped = 0;
        var flippedGap = 0.0;
        var overhang = 0.0;
        for (var z = BigMapTerrain.MinCell; z <= BigMapTerrain.MaxCell; z++)
        {
            for (var x = BigMapTerrain.MinCell; x <= BigMapTerrain.MaxCell; x++)
            {
                var centre = new NavPoint(x + 0.5, z + 0.5);
                var byCentre = BigMapGrid.IsInsideEdge(centre);
                var byCorner = BigMapGrid.IsInsideEdge(new NavPoint(x, z));
                if (byCentre != byCorner)
                {
                    flipped++;
                    flippedGap = Math.Max(flippedGap, Math.Abs(Distance(centre) - Distance(new NavPoint(x, z))));
                }

                if (!byCentre)
                {
                    outside++;
                    if (BigMapTerrain.TerrainAtCell(x, z) is not null) missing++;
                    continue;
                }

                if (BigMapTerrain.TerrainAtCell(x, z) is null) missing++;

                // 陆地探出墙外多少：这一格最远那个角离圆心多远。
                foreach (var (cornerX, cornerZ) in new[] { (x, z), (x + 1, z), (x, z + 1), (x + 1, z + 1) })
                {
                    overhang = Math.Max(overhang, Distance(new NavPoint(cornerX, cornerZ)) - BigMapGrid.LandRadiusMeters);
                }
            }
        }

        missing.ShouldBe(0, "格心在圆内的每一格都必有地形，格心在圆外的每一格都回 null。");
        outside.ShouldBeGreaterThan(0, "格网里确实有圆外那一圈（圆盘跨 −56..456，格网是 −56..455）。");

        // 边缘上最多差 0.71 米（角到格心那一段）。
        flippedGap.ShouldBeLessThanOrEqualTo(Math.Sqrt(2.0) / 2.0 + 1e-9,
            "两种口径判得不一样的那些格，差的只是角到格心那一段，最多 √2 ÷ 2 ≈ 0.71 米。");

        // 按格心判之后，陆地最多探出墙外 0.71 米（不足一米，认了）。
        overhang.ShouldBeLessThanOrEqualTo(Math.Sqrt(2.0) / 2.0 + 1e-9,
            "陆地探出圆盘侧壁最多 √2 ÷ 2 ≈ 0.71 米。");
        flipped.ShouldBeGreaterThan(0, "两种口径确实在边缘上判出了不同的格（不是同一件事换写法）。");

        // 圆外：格网之内但圆盘之外的那几个角。
        BigMapTerrain.TerrainAtCell(0, 0).ShouldBeNull("西南角在格网里但在圆盘外，没有地形。");
        BigMapTerrain.TerrainAtCell(455, 455).ShouldBeNull("东北角在格网里但在圆盘外，没有地形。");

        // 格网之外：负格号那一头与末格那一头都回 null。
        BigMapTerrain.TerrainAtCell(-57, 200).ShouldBeNull("−57 号格在格网之外。");
        BigMapTerrain.TerrainAtCell(456, 200).ShouldBeNull("456 号格在格网之外。");
    }

    [Fact]
    public void 负格号读得到_圆内那一段是草地()
    {
        // 格号原点是 −56：负格号不是「读不到」，是照样按米读。
        BigMapTerrain.TerrainAtCell(-50, 200).ShouldBe(TerrainKind.Grass,
            "−50 号格在圆盘内（离圆心 250 米），读得到，是草地。");
        BigMapTerrain.TerrainAtCell(-56, 200).ShouldBe(TerrainKind.Grass,
            "−56 号格是格网最西那一格，格心离圆心 255.5 米，算界内，是草地。");

        // 界上那一格按格心算：格心在半径 256 米内即算陆地。
        BigMapTerrain.TerrainAtCell(-56, 201).ShouldBe(TerrainKind.Grass,
            "−56 号格往北一格：格心离圆心 255.50 米，在 256 米内，算陆地，是草地。"
            + "旧口径取左下角（离圆心 256.002 米）会判成星空，新口径按格心判，边缘上差的就是这一格。");
        BigMapTerrain.TerrainAtCell(-56, 216).ShouldBeNull(
            "−56 号格再往北 15 格：格心离圆心 256.03 米，出了圆盘，回 null。");
    }

    [Fact]
    public void 旧方图那四个角还是草与沙漠()
    {
        // 旧 400 米方图 [40,360] 的四个角：一格不变。
        BigMapTerrain.TerrainAtCell(40, 40).ShouldBe(TerrainKind.Desert, "西南角在沙漠带里。");
        BigMapTerrain.TerrainAtCell(359, 40).ShouldBe(TerrainKind.Desert, "东南角在沙漠带里。");
        BigMapTerrain.TerrainAtCell(40, 359).ShouldBe(TerrainKind.Grass, "西北角是草地。");
        BigMapTerrain.TerrainAtCell(359, 359).ShouldBe(TerrainKind.Grass, "东北角是草地。");
    }

    [Fact]
    public void 旧方图那一段四种地形一格不变()
    {
        // 期望表在测试里另算一遍（不读 BigMapTerrain 的内部表）——
        // 免得「实现改了、期望也跟着改」那种自说自话。
        var wrong = new List<string>();
        for (var z = 40; z <= 359; z++)
        {
            for (var x = 40; x <= 359; x++)
            {
                var expected = ExpectedKind(x, z);
                var actual = BigMapTerrain.TerrainAtCell(x, z);
                if (actual == expected) continue;
                wrong.Add($"({x},{z}) 期望 {expected} 实际 {(actual is null ? "null" : actual.Value.ToString())}");
            }
        }

        wrong.Count.ShouldBe(0,
            "旧方图 [40,360] 那一段四种地形一格不变。第一几处不符：" + string.Join("；", wrong.GetRange(0, Math.Min(5, wrong.Count))));
    }

    [Fact]
    public void 圆盘新扩出的环带一律草地()
    {
        // 旧方图之外、圆盘之内的那一圈：这一轮不打磨地图，一律默认草地。
        // 圆外那一圈（读作 null）不在这条判据里 —— 那一圈是星空，不是陆地。
        var notGrass = 0;
        var checkedCells = 0;
        for (var z = BigMapTerrain.MinCell; z <= BigMapTerrain.MaxCell; z++)
        {
            for (var x = BigMapTerrain.MinCell; x <= BigMapTerrain.MaxCell; x++)
            {
                if (x >= 40 && x <= 359 && z >= 40 && z <= 359) continue;
                if (BigMapTerrain.TerrainAtCell(x, z) is null) continue;
                checkedCells++;
                if (BigMapTerrain.TerrainAtCell(x, z) is not TerrainKind.Grass) notGrass++;
            }
        }

        checkedCells.ShouldBeGreaterThan(0, "新扩出的环带确实有格（圆盘跨 −56..456，格网是 −56..455）。");
        notGrass.ShouldBe(0, "新扩出的环带是默认草地，不铺细节。");
    }

    [Fact]
    public void 四种地都摆到了地图上()
    {
        var seen = new HashSet<TerrainKind>();
        for (var z = BigMapTerrain.MinCell; z <= BigMapTerrain.MaxCell; z++)
        {
            for (var x = BigMapTerrain.MinCell; x <= BigMapTerrain.MaxCell; x++)
            {
                var kind = BigMapTerrain.TerrainAtCell(x, z);
                if (kind is not null) seen.Add(kind.Value);
            }
        }

        seen.Count.ShouldBe(4, "草地 / 沙漠 / 道路 / 水四种都在图上。");
    }

    [Fact]
    public void TerrainAtStopPoints()
    {
        BigMapTerrain.TerrainAt(BigMapGrid.PoorCity.Position).ShouldBe(TerrainKind.Grass, "贫城城门在草地上。");
        BigMapTerrain.TerrainAt(BigMapGrid.RichCity.Position).ShouldBe(TerrainKind.Grass, "富城城门在草地上。");
        BigMapTerrain.TerrainAt(BigMapGrid.DesertCity.Position).ShouldBe(TerrainKind.Desert, "沙城城门在沙漠里。");
        BigMapTerrain.TerrainAt(BigMapGrid.WildAnchor.Position).ShouldBe(TerrainKind.Grass, "野外锚地在草地上。");
    }

    [Fact]
    public void 按坐标读地形_格心与边界都算那一格()
    {
        BigMapTerrain.TerrainAt(new NavPoint(40.5, 190.5)).ShouldBe(TerrainKind.Road, "主干道路那一条带的格心是道路。");
        BigMapTerrain.TerrainAt(new NavPoint(210.5, 140.5)).ShouldBe(TerrainKind.Road, "南支那一条带的格心是道路。");
        BigMapTerrain.TerrainAt(new NavPoint(210.5, 240.5)).ShouldBe(TerrainKind.Road, "北支那一条带的格心是道路。");
        BigMapTerrain.TerrainAt(new NavPoint(120.5, 240.5)).ShouldBe(TerrainKind.Water, "湖泊格心是水。");
        BigMapTerrain.TerrainAt(new NavPoint(300.5, 100.5)).ShouldBe(TerrainKind.Water, "河流格心是水。");
        BigMapTerrain.TerrainAt(new NavPoint(210.0, 90.0)).ShouldBe(TerrainKind.Desert, "沙城那一格是沙漠。");

        // 格的边界算里面那一格：x=100.0 是第 100 格的西界，仍归第 100 格。
        BigMapTerrain.TerrainAt(new NavPoint(100.0, 240.0)).ShouldBe(TerrainKind.Water, "湖泊西界算湖格。");

        // 圆外回 null（格网之内但圆盘之外的那一角）。
        BigMapTerrain.TerrainAt(new NavPoint(0.5, 0.5)).ShouldBeNull("西南角在圆盘外，没有地形。");
        BigMapTerrain.TerrainAt(new NavPoint(-100.5, 200.0)).ShouldBeNull("格网之外也没有地形。");

        // 旧方图那一段现在仍在圆内：39.5 与 400.5 都读得到（草）。
        BigMapTerrain.TerrainAt(new NavPoint(39.5, 200.0)).ShouldBe(TerrainKind.Grass,
            "39.5 米那一格在圆盘内（离圆心 160.5 米），是草地。");
        BigMapTerrain.TerrainAt(new NavPoint(400.5, 200.0)).ShouldBe(TerrainKind.Grass,
            "400.5 米那一格在圆盘内（离圆心 200.5 米），是草地。");
    }

    [Fact]
    public void RoadGraphReachesAllStops()
    {
        BigMapTerrain.RoadGraphReaches(
            BigMapGrid.PoorCity.Position,
            BigMapGrid.RichCity.Position,
            BigMapGrid.WildAnchor.Position,
            BigMapGrid.DesertCity.Position).ShouldBeTrue("四个停靠点都该挨着道路，道路格还该连成一片。");

        // 「挨着」按米判：到最近道路格矩形边缘（不取格心）的距离 ≤ 10 米。
        BigMapTerrain.DistanceToNearestRoadCell(BigMapGrid.PoorCity.Position.X, BigMapGrid.PoorCity.Position.Z)
            .ShouldBe(10.0, 1e-9, "贫城离主干道路北缘 10 米。");
        BigMapTerrain.DistanceToNearestRoadCell(BigMapGrid.RichCity.Position.X, BigMapGrid.RichCity.Position.Z)
            .ShouldBe(10.0, 1e-9, "富城离主干道路北缘 10 米。");
        BigMapTerrain.DistanceToNearestRoadCell(BigMapGrid.WildAnchor.Position.X, BigMapGrid.WildAnchor.Position.Z)
            .ShouldBe(10.0, 1e-9, "野外锚地离北支北缘 10 米。");
        BigMapTerrain.DistanceToNearestRoadCell(BigMapGrid.DesertCity.Position.X, BigMapGrid.DesertCity.Position.Z)
            .ShouldBe(10.0, 1e-9, "沙城离南支南缘 10 米。");

        // 反面对照：湖心离路 40.5 米，挨不着。
        BigMapTerrain.DistanceToNearestRoadCell(120.5, 240.5).ShouldBe(40.5, 1e-9, "湖心离主干道路 40.5 米。");
        BigMapTerrain.RoadGraphReaches(new NavPoint(120.5, 240.5)).ShouldBeFalse("湖心四周没有道路。");
    }

    [Fact]
    public void 水面不挨着城格_道路不修进水里()
    {
        // 城格的四邻不许是水（城不该泡在水里）。
        foreach (var spot in new[]
                 {
                     BigMapGrid.PoorCity.Position, BigMapGrid.RichCity.Position,
                     BigMapGrid.WildAnchor.Position, BigMapGrid.DesertCity.Position,
                 })
        {
            var x = (int)Math.Floor(spot.X);
            var z = (int)Math.Floor(spot.Z);
            foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (nx < BigMapTerrain.MinCell || nx > BigMapTerrain.MaxCell) continue;
                if (nz < BigMapTerrain.MinCell || nz > BigMapTerrain.MaxCell) continue;
                BigMapTerrain.TerrainAtCell(nx, nz).ShouldNotBe(TerrainKind.Water,
                    $"停靠点 ({x},{z}) 的四邻不该是水。");
            }
        }

        // 道路格本身不是水（道路遇水绕行，不穿水）。
        for (var z = BigMapTerrain.MinCell; z <= BigMapTerrain.MaxCell; z++)
        {
            for (var x = BigMapTerrain.MinCell; x <= BigMapTerrain.MaxCell; x++)
            {
                if (BigMapTerrain.TerrainAtCell(x, z) is TerrainKind.Road)
                {
                    BigMapTerrain.TerrainAtCell(x, z).ShouldNotBe(TerrainKind.Water);
                }
            }
        }
    }

    [Fact]
    public void 沙城登记在册且追加在末尾()
    {
        BigMapGrid.PlaceById("desert_city").ShouldNotBeNull("沙城是大地图上的停靠点。");
        BigMapGrid.Places.Count.ShouldBe(4, "贫城 / 野外锚地 / 富城 / 沙城共四个点位。");
        BigMapGrid.Places[3].Id.ShouldBe("desert_city", "新点位追加在末尾：空艇到访数按序号派生，插中间会让重放分叉。");

        BigMapGrid.DesertCity.Position.X.ShouldBe(210.0, "沙城横在沙漠带中线上。");
        BigMapGrid.DesertCity.Position.Z.ShouldBe(90.0, "沙城纵在南侧沙漠带里。");
        BigMapGrid.DesertCity.InwardRings.ShouldBe(0, "沙城贴世界南缘，向心环数 0。");
        BigMapGrid.DesertCity.PriceFactor.ShouldBe(0.9, "沙城物价系数 0.9（第一版草案）。");
        BigMapGrid.DesertCity.VisitorPoolCap.ShouldBe(100, "沙城游客池上限 100（第一版草案）。");
    }

    [Fact]
    public void 沙城能停靠_游客池按城档_城门落在沙漠里()
    {
        var world = ArenaWorldBuilder.New().Build();
        ArenaSetup.Inject(world, "Dock", "desert_city", out _, out var why).ShouldBeTrue(why);
        for (var i = 0; i < 6000 && !world.IsDocked; i++) world.Advance(1);

        world.IsDocked.ShouldBeTrue("沙城是停靠点，能停。");
        world.DockedCityId.ShouldBe("desert_city", "停的是沙城。");
        world.Visitors.Cap.ShouldBe(100, "沙城游客池上限 100（第一版草案）。");

        // 兽停在城门外那一点（门外就是路），城本身在沙漠里。
        NavArrive.Reached(world.Voyage.Position, BigMapGrid.DesertCity.StopPoint(BeastTier.Tier1), ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"应当停在沙城门外，实际停在 {world.Voyage.Position}");
        BigMapTerrain.TerrainAt(BigMapGrid.DesertCity.Position).ShouldBe(TerrainKind.Desert,
            "沙城城门落在沙漠格里，地形读数是 desert。");
    }

    [Fact]
    public void 地形名读数四种加界外()
    {
        BigMapTerrain.NameOf(TerrainKind.Grass).ShouldBe("grass");
        BigMapTerrain.NameOf(TerrainKind.Desert).ShouldBe("desert");
        BigMapTerrain.NameOf(TerrainKind.Road).ShouldBe("road");
        BigMapTerrain.NameOf(TerrainKind.Water).ShouldBe("water");
        BigMapTerrain.NameOf(null).ShouldBe("-");
    }

    /// <summary>
    /// 一点离圆心的距离（米）—— 测试自己算一遍，不读实现那一处半径判据。
    /// </summary>
    private static double Distance(NavPoint point)
    {
        var dx = point.X - BigMapGrid.DiskCenterX;
        var dz = point.Z - BigMapGrid.DiskCenterZ;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>
    /// 期望的地形（测试自己算一遍，不读实现那张表）——
    /// 铺色次序：草 → 沙漠 → 水 → 道路。
    /// </summary>
    private static TerrainKind ExpectedKind(int x, int z)
    {
        var kind = TerrainKind.Grass;
        if (x >= 40 && x <= 359 && z >= 40 && z <= 159) kind = TerrainKind.Desert;
        if (x >= 100 && x <= 159 && z >= 220 && z <= 259) kind = TerrainKind.Water;
        if (x >= 280 && x <= 319 && z >= 40 && z <= 179) kind = TerrainKind.Water;
        if (x >= 40 && x <= 359 && z >= 180 && z <= 199) kind = TerrainKind.Road;
        if (x >= 200 && x <= 219 && z >= 100 && z <= 179) kind = TerrainKind.Road;
        if (x >= 200 && x <= 219 && z >= 200 && z <= 279) kind = TerrainKind.Road;
        return kind;
    }
}
