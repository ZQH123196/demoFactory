#nullable enable
using System;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 大地图那一半的读数：圆盘定尺、罗盘时钟、三城定尺与城的锚点。
/// 纯 C# 逻辑直接调，不起引擎。
///
/// <para><b>定尺是圆盘 + 一张带格号原点的格网</b>：圆心 (200,200)、半径 256 米；
/// 寻路网格 516 × 516、格长 1 米、格号原点 −58 米（覆盖世界 −58..458 米）。</para>
/// </summary>
public class BigMapGridTests
{
    [Fact]
    public void 大地图寻路网格是五百一十六格一米一格_格号原点负五十八()
    {
        var grid = BigMapGrid.Build();

        grid.CellsX.ShouldBe(516, "大地图横向 516 格（圆盘外接方框 512 格，每边再留 2 格余量）。");
        grid.CellsZ.ShouldBe(516, "大地图进深 516 格。");
        grid.CellMeters.ShouldBe(1.0, "格长 1.0 米。");
        grid.ExtentXmeters.ShouldBe(516.0, "516 格 × 1 米 = 516 米见方。");
        grid.OriginX.ShouldBe(-58.0, "格号原点 −58 米（第 (0,0) 格那一角落在空间坐标的哪一点）。");
        grid.OriginZ.ShouldBe(-58.0, "格号原点 −58 米。");
        grid.MinXMeters.ShouldBe(-58.0, "网格在 X 方向的起点是 −58 米。");
        grid.MaxXMeters.ShouldBe(458.0, "网格在 X 方向的终点是 458 米（开区间）。");
        grid.Space.Value.ShouldBe("大地图", "空间名是大地图。");
    }

    [Fact]
    public void 网格整个盖住圆盘_也盖住半径二百五十七那圈出界目标()
    {
        var grid = BigMapGrid.Build();

        // 圆盘外接方框是 −56..456（半径 256 × 2 边长），四个正方向上各取一点。
        foreach (var at in new[]
        {
            new NavPoint(-56.0, 200.0),
            new NavPoint(456.0, 200.0),
            new NavPoint(200.0, -56.0),
            new NavPoint(200.0, 456.0),
        })
        {
            grid.Contains(at).ShouldBeTrue($"圆盘边上的 {at} 必须在寻路网格里，否则兽到不了盘边。");
        }

        // 出界目标是半径 257 那圈（正方向上落在 −57 与 457，比外接方框还各出 1 米）。
        // 网格不越过那一点，目标就落在网格外，求解器判「目标不可达」，兽永远出不了界。
        foreach (var at in new[]
        {
            new NavPoint(-57.0, 200.0),
            new NavPoint(457.0, 200.0),
            new NavPoint(200.0, -57.0),
            new NavPoint(200.0, 457.0),
        })
        {
            grid.Contains(at).ShouldBeTrue($"出界目标 {at} 必须在寻路网格里，否则四个方向出不去。");
        }
    }

    [Fact]
    public void 寻路格网与地形格网只差两格_两套格号对得上()
    {
        var grid = BigMapGrid.Build();

        // 地形那张：格号原点 −56、512 格。寻路那张：格号原点 −58、516 格。
        // 两张现在只差 2 格（寻路每边多两格余量），不是早先那个差 56 米的口径。
        (BigMapTerrain.GridOrigin - (int)grid.OriginX).ShouldBe(2,
            "地形格号 − 寻路格号 = 2（寻路网格每边多两格余量）。");
        (BigMapTerrain.GridCells - grid.CellsX).ShouldBe(-4,
            "寻路网格比地形网格每边多两格：516 − 512 = 4，两边各两格。");

        // 同一米在两张格网上差同一个常数 —— 这是「对得上」那句话的落点。
        var at = BigMapTerrain.GridOrigin + 100;
        var terrainCell = at - BigMapTerrain.GridOrigin;
        var navCell = at - (int)grid.OriginX;
        (navCell - terrainCell).ShouldBe(2, "同一米的两个格号差同一个常数 2。");
    }

    [Fact]
    public void 两城相距二百四十米_野外锚地在两城之间()
    {
        var poor = BigMapGrid.PoorCity.Position;
        var rich = BigMapGrid.RichCity.Position;

        poor.DistanceTo(rich).ShouldBe(240.0, 1e-9, "两城航程 240 米。");
        poor.X.ShouldBe(90.0, "贫城横向 90 米。");
        poor.Z.ShouldBe(210.0, "贫城进深 210 米。");
        rich.X.ShouldBe(330.0, "富城横向 330 米。");
        rich.Z.ShouldBe(210.0, "富城与贫城同在 z=210 一行。");
        BigMapGrid.PoorCity.InwardRings.ShouldBe(0, "贫城向心环数 0。");
        BigMapGrid.PoorCity.PriceFactor.ShouldBe(0.8, "贫城物价系数 0.8。");
        BigMapGrid.PoorCity.VisitorPoolCap.ShouldBe(80, "贫城游客池上限 80。");
        BigMapGrid.RichCity.InwardRings.ShouldBe(1, "富城向心环数 1。");
        BigMapGrid.RichCity.PriceFactor.ShouldBe(1.2, "富城物价系数 1.2。");
        BigMapGrid.RichCity.VisitorPoolCap.ShouldBe(150, "富城游客池上限 150。");

        var wild = BigMapGrid.WildAnchor.Position;
        wild.X.ShouldBe(210.0, "野外锚地横向 210 米。");
        wild.Z.ShouldBe(290.0, "野外锚地进深 290 米。");
    }

    [Fact]
    public void 世界是圆盘_圆心两百半径二百五十六_外侧即出界()
    {
        BigMapGrid.DiskCenterX.ShouldBe(200.0, "圆心横向 200 米。");
        BigMapGrid.DiskCenterZ.ShouldBe(200.0, "圆心进深 200 米。");
        BigMapGrid.LandRadiusMeters.ShouldBe(256.0, "陆地半径 256 米。");

        BigMapGrid.IsInsideEdge(new NavPoint(200.0, 200.0)).ShouldBeTrue("圆心在界内。");
        BigMapGrid.IsInsideEdge(new NavPoint(455.9, 200.0)).ShouldBeTrue("半径 255.9 米那一段算界内。");
        BigMapGrid.IsOutOfBounds(new NavPoint(456.1, 200.0)).ShouldBeTrue("半径 256.1 米落在星空出界区。");
        BigMapGrid.IsOutOfBounds(new NavPoint(0.0, 0.0)).ShouldBeTrue("西南角离圆心 282.8 米，出界。");
    }

    [Fact]
    public void 界内外只差半米_东南西北四个方向各一例()
    {
        // 判据是「离圆心超过 256 米」，严格大于：255.9 在界内、256.1 在界外，四个方向各一例。
        BigMapGrid.IsInsideEdge(new NavPoint(200.0 + 255.9, 200.0)).ShouldBeTrue("东侧 255.9 米在界内。");
        BigMapGrid.IsOutOfBounds(new NavPoint(200.0 + 256.1, 200.0)).ShouldBeTrue("东侧 256.1 米出界。");

        BigMapGrid.IsInsideEdge(new NavPoint(200.0 - 255.9, 200.0)).ShouldBeTrue("西侧 255.9 米在界内。");
        BigMapGrid.IsOutOfBounds(new NavPoint(200.0 - 256.1, 200.0)).ShouldBeTrue("西侧 256.1 米出界。");

        BigMapGrid.IsInsideEdge(new NavPoint(200.0, 200.0 + 255.9)).ShouldBeTrue("北侧 255.9 米在界内。");
        BigMapGrid.IsOutOfBounds(new NavPoint(200.0, 200.0 + 256.1)).ShouldBeTrue("北侧 256.1 米出界。");

        BigMapGrid.IsInsideEdge(new NavPoint(200.0, 200.0 - 255.9)).ShouldBeTrue("南侧 255.9 米在界内。");
        BigMapGrid.IsOutOfBounds(new NavPoint(200.0, 200.0 - 256.1)).ShouldBeTrue("南侧 256.1 米出界。");
    }

    [Fact]
    public void 出界目标与送回点在同一条射线上()
    {
        // 富城那一侧：出界目标是半径 257（一定在圆外），送回点是半径 255.5（一定在圆内）。
        var from = new NavPoint(330.0, 210.0);
        var outbound = BigMapGrid.RadialPoint(from, BigMapGrid.OutboundTargetRadiusMeters);
        var back = BigMapGrid.RadialPoint(from, BigMapGrid.SendBackRadiusMeters);

        Radius(outbound).ShouldBe(BigMapGrid.OutboundTargetRadiusMeters, 1e-9,
            "出界目标落在半径 257 米那一点（兽在目标前停，也要保证越过 256 那一线）。");
        Radius(back).ShouldBe(BigMapGrid.SendBackRadiusMeters, 1e-9,
            "送回点落在同一方向、半径 255.5 米那一点。");

        BigMapGrid.IsOutOfBounds(outbound).ShouldBeTrue("出界目标在圆盘外。");
        BigMapGrid.IsInsideEdge(back).ShouldBeTrue("送回点在圆盘内。");

        // 两点严格同一条射线：兽是被「推回来」的，不是被横移回来的。
        var cross = ((outbound.X - BigMapGrid.DiskCenterX) * (back.Z - BigMapGrid.DiskCenterZ))
                    - ((outbound.Z - BigMapGrid.DiskCenterZ) * (back.X - BigMapGrid.DiskCenterX));
        cross.ShouldBe(0.0, 1e-9, "出界目标与送回点在同一条射线上。");
    }

    [Fact]
    public void 径向点四个方向各一例_都从圆心那条射线出去()
    {
        // 正东、正西、正北、正南：起点正好在半径 256 米那一圈上，方向是整数，答案逐字可读。
        BigMapGrid.RadialPoint(new NavPoint(456.0, 200.0), 257.0)
            .ShouldBe(new NavPoint(457.0, 200.0), "正东那一侧朝东出去。");
        BigMapGrid.RadialPoint(new NavPoint(-56.0, 200.0), 257.0)
            .ShouldBe(new NavPoint(-57.0, 200.0), "正西那一侧朝西出去。");
        BigMapGrid.RadialPoint(new NavPoint(200.0, 456.0), 257.0)
            .ShouldBe(new NavPoint(200.0, 457.0), "正北那一侧朝北出去。");
        BigMapGrid.RadialPoint(new NavPoint(200.0, -56.0), 257.0)
            .ShouldBe(new NavPoint(200.0, -57.0), "正南那一侧朝南出去。");

        // 正落在圆心上时方向无从取：按固定的那一条（+X）走，重放要逐字可复现。
        BigMapGrid.RadialPoint(new NavPoint(200.0, 200.0), 257.0)
            .ShouldBe(new NavPoint(457.0, 200.0), "圆心上方向取 +X 那一档。");
    }

    [Fact]
    public void 城门是城的逻辑点_兽停在城门外一个兽半宽()
    {
        var poor = BigMapGrid.PoorCity;

        // 城门 = 城的逻辑点（贫城在 (90,210)），城体整个在它后面。
        poor.Position.ShouldBe(new NavPoint(90.0, 210.0), "贫城城门在 (90,210)。");
        poor.OutwardNormal.ShouldBe(new NavPoint(0.0, -1.0), "贫城的路在它南面，外法线朝南。");

        // 停靠点 = 城门沿外法线外推一个兽半宽（一阶实测 26.45 米）。
        poor.StopPoint(BeastTier.Tier1).X.ShouldBe(90.0, 1e-9, "停靠点与城门同在一条横线上。");
        poor.StopPoint(BeastTier.Tier1).Z.ShouldBe(210.0 - ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1), 1e-9,
            "停靠点在城门南边一个兽半宽（26.45 米）。");
        ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1).ShouldBe(26.45, "一阶兽半宽 26.45 米 = 实测 52.9 ÷ 2。");

        // 城模型中心 = 城门沿外法线反推半个城深（实测 48 米）。
        poor.CityCenter.X.ShouldBe(90.0, 1e-9, "城模型中心与城门同在一条横线上。");
        poor.CityCenter.Z.ShouldBe(210.0 + ArenaTuning.CityHalfDepthMeters, 1e-9,
            "城模型中心在城门北边半个城深（48 米）。");
        ArenaTuning.CityHalfDepthMeters.ShouldBe(48.0, "城半深 48 米 = 实测 96 ÷ 2。");

        // 沙城的外法线朝北（南支路在它北面）。
        BigMapGrid.DesertCity.OutwardNormal.ShouldBe(new NavPoint(0.0, 1.0), "沙城的路在它北面，外法线朝北。");
        BigMapGrid.DesertCity.StopPoint(BeastTier.Tier1).Z.ShouldBe(
            90.0 + ArenaTuning.BeastHalfWidthFor(BeastTier.Tier1), 1e-9,
            "沙城的停靠点在城门北边一个兽半宽。");

        // 野外锚地的外法线朝南（北支路在它南面）。
        BigMapGrid.WildAnchor.OutwardNormal.ShouldBe(new NavPoint(0.0, -1.0), "野外锚地的路在它南面，外法线朝南。");
    }

    [Fact]
    public void 贫城与富城的停靠点仍相距二百四十米()
    {
        var poor = BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1);
        var rich = BigMapGrid.RichCity.StopPoint(BeastTier.Tier1);

        poor.DistanceTo(rich).ShouldBe(240.0, 1e-9,
            "两城的停靠点仍相距 240 米 —— 10 游戏小时那一档（240 ÷ 0.4 = 600 现实秒）不变。");
        poor.X.ShouldBe(90.0, 1e-9, "贫城停靠点横向 90 米。");
        rich.X.ShouldBe(330.0, 1e-9, "富城停靠点横向 330 米。");
        poor.Z.ShouldBe(rich.Z, 1e-9, "两座老城的外法线都朝南，停靠点在同一行。");
    }

    [Fact]
    public void 罗盘时钟开局第六点_随游戏分钟连续推进()
    {
        ArenaTime.CompassAt(0).ShouldBe("Day 1 · 06:00", "开局是 Day 1 六点整。");

        // 一个游戏小时 = 600 步 = 60 个游戏分钟。
        ArenaTime.CompassAt(ArenaTime.StepsPerGameMinute).ShouldBe("Day 1 · 06:01", "走满一个游戏分钟钟面走 1 分。");
        ArenaTime.CompassAt(ArenaTime.StepsPerGameHour).ShouldBe("Day 1 · 07:00", "走满一个游戏小时钟面走 1 小时。");

        // 一个游戏日 = 14400 步 = 24 小时。
        ArenaTime.CompassAt(ArenaTime.StepsPerGameDay).ShouldBe("Day 2 · 06:00", "走满一个游戏日进到第二天同一时刻。");

        // 跨过午夜那一刻：第六点往后 18 小时是第二天的零点。
        var toMidnight = ArenaTime.StepsPerGameHour * 18;
        ArenaTime.CompassAt(toMidnight).ShouldBe("Day 2 · 00:00", "第六点往后 18 小时是零点。");
    }

    [Fact]
    public void 时间换算三档自洽()
    {
        ArenaTime.StepsPerGameMinute.ShouldBe(10, "一个逻辑步 0.1 秒，一个游戏分钟 10 步。");
        ArenaTime.StepsPerGameHour.ShouldBe(600, "一个游戏小时 600 步。");
        ArenaTime.StepsPerGameDay.ShouldBe(14400, "一个游戏日 14400 步。");
        ArenaTime.GameMinutesPerDay.ShouldBe(1440, "一天 1440 个游戏分钟。");
        ArenaTime.SecondsPerStep.ShouldBe(0.1, "一个逻辑步 0.1 现实秒。");
    }

    /// <summary>一点离圆心的距离（米）—— 判界与径向点都用这一把尺子。</summary>
    private static double Radius(NavPoint at)
        => Math.Sqrt(((at.X - BigMapGrid.DiskCenterX) * (at.X - BigMapGrid.DiskCenterX))
                     + ((at.Z - BigMapGrid.DiskCenterZ) * (at.Z - BigMapGrid.DiskCenterZ)));
}
