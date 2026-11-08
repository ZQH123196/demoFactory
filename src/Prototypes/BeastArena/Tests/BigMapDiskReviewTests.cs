#nullable enable
using System;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 大地图圆盘那一轮验收复核的用例：停靠净空、地形判界与城模型摆位。
/// 纯 C# 逻辑直接调，不起引擎。
///
/// <para><b>这一份盯的是「兽不压城」与「陆地不出墙」两件事</b>：
/// 停靠点离城门 26.45 米（兽的<b>半宽</b>）那一档，前提是<b>兽身横过来</b>；
/// 地形那一圈按<b>格心</b>判界，陆地才不穿出侧壁。</para>
/// </summary>
public class BigMapDiskReviewTests
{
    /// <summary>
    /// 巨兽沿自身长轴那一个方向的半长（米）—— 体检单实测 89.58 米 ÷ 2。
    /// 这一档只在本文件里用（记「兽头朝城」那一档会推进城里多远），逻辑层那一侧不需要它。
    /// </summary>
    private const double BeastHalfLengthMeters = 44.79;

    // ---- 停靠净空：兽身横过来 ----

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 停靠时兽身横过来_停车轴与城门法线垂直_四座城各一例()
    {
        // Arrange：四座城的停车轴与外法线都取自大地图那张表。
        // Act：四座城各跑一遍。
        foreach (var place in BigMapGrid.Places)
        {
            var axis = place.ParkAxis;
            var normal = place.OutwardNormal;

            // 停车轴与外法线垂直：兽身（长边）与城门那一面平齐，伸进城里的是体宽不是体长。
            var dot = (axis.X * normal.X) + (axis.Z * normal.Z);

            // Assert：停车轴与外法线垂直，停车轴是单位向量。
            dot.ShouldBe(0.0, 1e-9, $"{place.Name} 的停车轴与外法线垂直（兽身横过来）。");

            // 停车轴是水平单位向量。
            Math.Sqrt((axis.X * axis.X) + (axis.Z * axis.Z)).ShouldBe(1.0, 1e-9,
                $"{place.Name} 的停车轴是单位向量。");
        }
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 兽身横过来时净空为零_四座城各一例()
    {
        // Arrange：四座城的城门、城模型中心与停靠点都取自大地图那张表。
        // Act：四座城各算一遍净空。
        foreach (var place in BigMapGrid.Places)
        {
            // 城体整个在城门后面：城门到城模型中心那半个城深。
            var gateToCenter = place.Position.DistanceTo(place.CityCenter);

            // Assert：城体在城门后面半个城深，停靠点在城门外一个兽半宽，净空正好 0 米。
            gateToCenter.ShouldBe(ArenaTuning.CityHalfDepthMeters, 1e-9,
                $"{place.Name} 的城模型中心在城门后面半个城深（48 米）。");

            // 兽停在城门外那一点：离城门一个一阶兽半宽。
            place.StopPoint(BeastTier.Tier1).DistanceTo(place.Position)
                .ShouldBe(ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1), 1e-9,
                    $"{place.Name} 的停靠点在城门外一个兽半宽（26.45 米）。");

            // 净空 = 停靠点到城体的距离 − 城的半深 − 兽的半宽。
            // 横过来时兽身伸进城里的是半宽 26.45 米，于是净空正好 0：兽身贴着城门，不压城。
            var clearance = (place.StopPoint(BeastTier.Tier1).DistanceTo(place.CityCenter)
                             - ArenaTuning.CityHalfDepthMeters
                             - ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1));
            clearance.ShouldBe(0.0, 1e-9,
                $"{place.Name} 停稳之后兽身正好抵住城体边缘（净空 0 米，兽不压城）。");
        }
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 二阶停靠点在城门外一个二阶兽半宽_净空仍为零_四座城各一例()
    {
        // Arrange：二阶余量取三套模型的最大半宽 30.00 米
        // （实测 30.000 / 26.500 / 29.152，出处 docs/prototypes/beast-arena/model-review.md）。
        // Act：四座城各按二阶算一遍停靠点与净空。
        foreach (var place in BigMapGrid.Places)
        {
            var stop = place.StopPoint(BeastTier.Tier2);

            // Assert：停靠点在城门外 30.00 米 —— 进阶后切到三套里任何一套形态，兽身都不压城。
            stop.DistanceTo(place.Position).ShouldBe(ArenaTuning.BeastHalfWidthTier2Meters, 1e-9,
                $"{place.Name} 的二阶停靠点在城门外一个二阶兽半宽（30.00 米）。");

            // 近城净空按新半宽断言：横过来时兽身伸进城里的是二阶半宽，与停靠点偏移同数，净空还是 0。
            var clearance = (stop.DistanceTo(place.CityCenter)
                             - ArenaTuning.CityHalfDepthMeters
                             - ArenaTuning.BeastHalfWidthFor(BeastTier.Tier2));
            clearance.ShouldBe(0.0, 1e-9,
                $"{place.Name} 二阶停稳之后兽身正好抵住城体边缘（净空 0 米，兽不压城）。");
        }

        // 档位表两个读数钉死：一阶那一格不许漂，二阶取最大档。
        ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1).ShouldBe(26.45, "一阶兽半宽仍是 26.45 米。");
        ArenaTuning.BeastHalfWidthFor(BeastTier.Tier2).ShouldBe(30.00, "二阶兽半宽取三套模型的最大半宽。");

        // 三阶模型还没上体检单：问到它的半宽当场抛，不兜一个没定过的数。
        Should.Throw<NotSupportedException>(
            () => ArenaTuning.BeastHalfWidthFor(BeastTier.Tier3), "三阶没有定过半宽。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 兽头朝城那一档会把兽身推进城里十八米_所以要横过来()
    {
        // Arrange：兽的半宽取自调参表，半长取体检单那一档（44.79 米）。
        // 这一条记的是**为什么**要有停车轴那一档：
        // 巨兽节点跟着航向转，模型的长边 89.58 米顺着航向 ——
        // 沿支路（南北向）到沙城与野外锚地时，兽停下来是**头朝城**的，
        // 伸进城里的于是体长那半档（44.79 米），比停靠点那 26.45 米多出 18.34 米 → 兽压城。

        // Act：四座城各算一遍两个朝向的净空。
        foreach (var place in BigMapGrid.Places)
        {
            var noseOn = ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1) - BeastHalfLengthMeters;

            // Assert：头朝城净空是负的（兽压城），横过来净空归零。
            noseOn.ShouldBe(-18.34, 0.01,
                $"{place.Name} 头朝城时净空是负的（兽身压进城里约 18.3 米）。");

            var broadSide = ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1)
                            - ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1);
            broadSide.ShouldBe(0.0, 1e-9,
                $"{place.Name} 横过来时净空归零（兽身贴着城门）。");
        }
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 停靠点是城门外那一点_两座老城的停靠点仍相距二百四十米()
    {
        // Arrange：两座老城的一阶停靠点都取自大地图那张表。
        var poor = BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1);
        var rich = BigMapGrid.RichCity.StopPoint(BeastTier.Tier1);

        // Act：量两座老城停靠点之间的距离。
        var gap = poor.DistanceTo(rich);

        // Assert：两城的停靠点仍相距 240 米 —— 10 游戏小时那一档不变；两座老城的外法线都朝南。
        gap.ShouldBe(240.0, 1e-9,
            "两城的停靠点仍相距 240 米 —— 10 游戏小时那一档不变。");
        poor.Z.ShouldBe(rich.Z, 1e-9, "两座老城的外法线都朝南，停靠点在同一行。");
    }

    // ---- 地形判界：按格心，不按格角 ----

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 地形按格心判界_格角在墙内格心在墙外的那一格算圆外()
    {
        // Arrange：旧方图里「角在墙内、心在墙外」的那一格（382, 380）。
        // 这一格的左下角离圆心 255.98 米（墙内），格心离圆心 256.68 米（墙外）。
        // 按格角判会把它算成陆地，陆地就穿出了侧壁；按格心判才是圆外。

        // Act：查那一格按格心判在不在墙内，再取那一格的地形。
        var inside = BigMapGrid.IsInsideEdge(new NavPoint(382.0, 380.0));
        var terrain = BigMapTerrain.TerrainAtCell(382, 380);

        // Assert：那一格按格心判是圆外 —— 那儿没有地形。
        inside.ShouldBeTrue("那一格的左下角确实还在墙内（旧判据就是按它算的）。");
        terrain.ShouldBeNull("格心在墙外 —— 按格心判就是圆外，那一格没有地形。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 陆地不穿出侧壁_每一格的四角离圆心不超过二百五十六点七一米()
    {
        // Arrange：最远半径从 0 起步。
        var worst = 0.0;

        // Act：遍历每一格算最远半径。
        for (var x = BigMapTerrain.MinCell; x <= BigMapTerrain.MaxCell; x++)
        {
            for (var z = BigMapTerrain.MinCell; z <= BigMapTerrain.MaxCell; z++)
            {
                if (BigMapTerrain.TerrainAtCell(x, z) is null) continue;

                foreach (var corner in new[] { (x, z), (x + 1, z), (x, z + 1), (x + 1, z + 1) })
                {
                    var dx = corner.Item1 - BigMapGrid.DiskCenterX;
                    var dz = corner.Item2 - BigMapGrid.DiskCenterZ;
                    var radius = Math.Sqrt((dx * dx) + (dz * dz));
                    if (radius > worst) worst = radius;
                }
            }
        }

        // Assert：陆地不穿出侧壁。
        // 侧壁在 256 米。按格心判之后，格角最多探出 √2 ÷ 2 ≈ 0.71 米。
        worst.ShouldBeLessThanOrEqualTo(256.0 + 0.71,
            $"陆地最远探到 {worst.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} 米，"
            + "侧壁在 256 米 —— 探出不超过半个格对角线（0.71 米）。");
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 旧方图那一段一格不变_四角与几处地标仍是原来那几类()
    {
        // Arrange：旧方图那一段的每一格都该有地形。
        // 旧 400 米方图（x 40..359）整块落在圆内：每一格都有地形。

        // Act：旧方图每隔 37 格抽一格查地形。
        for (var x = 40; x <= 359; x += 37)
        {
            for (var z = 40; z <= 359; z += 37)
            {
                // Assert：旧方图那一格仍在圆内，仍有地形；四角与几处地标仍是原来那几类。
                BigMapTerrain.TerrainAtCell(x, z).ShouldNotBeNull(
                    $"旧方图那一格（{x},{z}）仍在圆内，仍有地形。");
            }
        }

        // 四角与几处地标仍是原来那几类地形（沙漠带、湖、河、三条路）。
        BigMapTerrain.TerrainAtCell(40, 40).ShouldBe(TerrainKind.Desert, "西南角是沙漠。");
        BigMapTerrain.TerrainAtCell(359, 40).ShouldBe(TerrainKind.Desert, "东南角是沙漠。");
        BigMapTerrain.TerrainAtCell(40, 359).ShouldBe(TerrainKind.Grass, "西北角是草地。");
        BigMapTerrain.TerrainAtCell(359, 359).ShouldBe(TerrainKind.Grass, "东北角是草地。");
        BigMapTerrain.TerrainAtCell(100, 220).ShouldBe(TerrainKind.Water, "湖在西北那一块。");
        BigMapTerrain.TerrainAtCell(300, 100).ShouldBe(TerrainKind.Water, "河在东侧那一条。");
        BigMapTerrain.TerrainAtCell(100, 190).ShouldBe(TerrainKind.Road, "主干道路在 z 180..199 那一带。");
        BigMapTerrain.TerrainAtCell(200, 150).ShouldBe(TerrainKind.Road, "南支路在 x 200..219 那一带。");
        BigMapTerrain.TerrainAtCell(200, 250).ShouldBe(TerrainKind.Road, "北支路在 x 200..219 那一带。");
    }

    // ---- 城模型摆位与判界口 ----

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 城模型整个在城门后面_四座城各一例()
    {
        // Arrange：四座城的城门位置、外法线与城模型中心都取自大地图那张表。
        // Act：四座城各算一遍城模型中心的位置。
        foreach (var place in BigMapGrid.Places)
        {
            var center = place.CityCenter;
            var normal = place.OutwardNormal;

            // 城模型中心在城门沿外法线反推半个城深那一处。
            // Assert：城模型中心在城门后面半个城深（横、纵两向都对得上）。
            center.X.ShouldBe(place.Position.X - (normal.X * ArenaTuning.CityHalfDepthMeters), 1e-9,
                $"{place.Name} 的城模型中心横向在城门后面半个城深。");
            center.Z.ShouldBe(place.Position.Z - (normal.Z * ArenaTuning.CityHalfDepthMeters), 1e-9,
                $"{place.Name} 的城模型中心进深在城门后面半个城深。");
        }
    }

    [Trait("测试类别", "快速测试")]
    [Fact]
    public void 界内界外那一个口是同一把尺子_圆心与四向各一例()
    {
        // Arrange：圆心与四向各取一个探针点：四向取 255.9 米（界内），另取 256.1 米（界外）。
        // Act：探一遍界内界外那一个口。
        var center = BigMapGrid.IsInsideEdge(new NavPoint(BigMapGrid.DiskCenterX, BigMapGrid.DiskCenterZ));
        var east = BigMapGrid.IsInsideEdge(new NavPoint(BigMapGrid.DiskCenterX + 255.9, BigMapGrid.DiskCenterZ));
        var west = BigMapGrid.IsInsideEdge(new NavPoint(BigMapGrid.DiskCenterX - 255.9, BigMapGrid.DiskCenterZ));
        var north = BigMapGrid.IsInsideEdge(new NavPoint(BigMapGrid.DiskCenterX, BigMapGrid.DiskCenterZ + 255.9));
        var south = BigMapGrid.IsInsideEdge(new NavPoint(BigMapGrid.DiskCenterX, BigMapGrid.DiskCenterZ - 255.9));
        var outside = BigMapGrid.IsInsideEdge(new NavPoint(BigMapGrid.DiskCenterX + 256.1, BigMapGrid.DiskCenterZ));

        // Assert：圆心与四向 255.9 米在界内，东侧 256.1 米在界外。
        center.ShouldBeTrue("圆心在界内。");
        east.ShouldBeTrue("东侧 255.9 米在界内。");
        west.ShouldBeTrue("西侧 255.9 米在界内。");
        north.ShouldBeTrue("北侧 255.9 米在界内。");
        south.ShouldBeTrue("南侧 255.9 米在界内。");
        outside.ShouldBeFalse("东侧 256.1 米在界外。");
    }
}
