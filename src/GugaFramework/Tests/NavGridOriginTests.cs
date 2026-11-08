#nullable enable
using System;
using DemoFactory.GugaFramework.Pathfinding;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 网格原点：网格第 (0, 0) 格那一角落在空间坐标的哪一点。
///
/// <para><b>三条口径</b>：① 零基网格递 0 就够，行为一个字不变；
/// ② 格索引仍从 0 起算 —— 原点挪的是整张网格的位置，不是格号；
/// ③ 原点不是空间→世界的变换（ADR 0009 那一条没动）：空间仍然只有名字，换算仍然归宿主。</para>
///
/// <para><b>为什么值得单列一份用例</b>：原点错了的症状是「路算得出来，兽走不到」——
/// 读数全绿，错的是场地本身。这类错只有拿「同一个点在两张不同原点的网格上」对读才看得出来。</para>
/// </summary>
public class NavGridOriginTests
{
    private static readonly SpaceId Field = new("原点场地");

    private static NavGrid Grid(
        double originX = 0.0,
        double originZ = 0.0,
        int cells = 8,
        double cell = 1.0,
        NavCell[]? blocked = null)
        => new(new NavGridSpec
        {
            Space = Field,
            CellMeters = cell,
            CellsX = cells,
            CellsZ = cells,
            OriginX = originX,
            OriginZ = originZ,
            BlockedCells = blocked ?? Array.Empty<NavCell>(),
        });

    [Fact]
    public void 格心跟着原点走_零基网格照旧从半米起()
    {
        var zero = Grid();
        zero.CenterOf(new NavCell(0, 0)).ShouldBe(new NavPoint(0.5, 0.5),
            "零基网格第 (0,0) 格的中心在 (0.5, 0.5)。");

        var moved = Grid(originX: -58.0, originZ: -58.0);
        moved.CenterOf(new NavCell(0, 0)).ShouldBe(new NavPoint(-57.5, -57.5),
            "原点 −58 米时第 (0,0) 格的中心落在 (−57.5, −57.5)。");
        moved.CenterOf(new NavCell(58, 58)).ShouldBe(new NavPoint(0.5, 0.5),
            "格索引不变：第 (58,58) 格正对着世界原点那一米。");
    }

    [Fact]
    public void 界按原点算_不按格号算()
    {
        var grid = Grid(originX: -58.0, originZ: -58.0, cells: 516);

        grid.Contains(new NavPoint(-58.0, 200.0)).ShouldBeTrue("左边界那一线算界内。");
        grid.Contains(new NavPoint(-58.1, 200.0)).ShouldBeFalse("左边界之外算界外。");
        grid.Contains(new NavPoint(457.9, 200.0)).ShouldBeTrue("右边最后一格算界内。");
        grid.Contains(new NavPoint(458.0, 200.0)).ShouldBeFalse("右边界那一线算界外。");
    }

    [Fact]
    public void 落格先减原点_格内局部坐标照旧零到一()
    {
        var grid = Grid(originX: -58.0, originZ: -58.0);

        grid.Clearance.TryLocal(new NavPoint(-57.5, -57.5), out var cx, out var cz, out var fx, out var fz)
            .ShouldBeTrue("点在网格内。");
        cx.ShouldBe(0, "第 (0,0) 格。");
        cz.ShouldBe(0, "第 (0,0) 格。");
        fx.ShouldBe(0.5, 1e-9, "格内横向位置照旧是 0..1。");
        fz.ShouldBe(0.5, 1e-9, "格内进深位置照旧是 0..1。");
    }

    [Fact]
    public void 原点非有限数当场抛_不兜零()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Grid(originX: double.NaN))
            .Message.ShouldContain("原点", Case.Sensitive, "报出来要指到是原点那一列不对");
        Should.Throw<ArgumentOutOfRangeException>(() => Grid(originZ: double.PositiveInfinity));
    }

    [Fact]
    public void 界外报错带着世界跨度_读数能照着改()
    {
        var grid = Grid(originX: -58.0, originZ: -58.0, cells: 4);
        Should.Throw<ArgumentOutOfRangeException>(() => grid.RequireCellOf(new NavPoint(100.0, 100.0)))
            .Message.ShouldContain("-58", Case.Sensitive, "报错要带出网格在空间里占的那一段");
    }

    [Fact]
    public void 净空是格与格之间的距离_整张网格挪位置读数不变()
    {
        var zero = Grid();
        var moved = Grid(originX: -58.0, originZ: -58.0);

        zero.Clearance.At(0, 0).ShouldBe(moved.Clearance.At(0, 0),
            "净空与整张网格摆在哪儿无关。");
        moved.Clearance.ConservativeLowerBoundAt(new NavPoint(-57.5, -57.5))
            .ShouldBe(zero.Clearance.ConservativeLowerBoundAt(new NavPoint(0.5, 0.5)), 1e-9,
            "同一个格心位置（挪过原点之后）读到的保守下界一样。");
    }

    [Fact]
    public void 直线段判据按世界米走_带原点的网格上不许穿格()
    {
        // 这一条盯的是「拉直」那一处的 DDA：格边界是**世界米**（原点 + 格号 × 格长），
        // 少了原点那一项，DDA 会在零基网格上碰巧算对、在带原点的网格上整段错位 ——
        // 而错位的方式是「直线段判成走得通」，症状是折线穿墙，读数上却看不出哪一步错了。
        var blocked = Grid(originX: -58.0, originZ: -58.0, cells: 128, blocked: new[] { new NavCell(66, 66) });
        var clear = Grid(originX: -58.0, originZ: -58.0, cells: 128);

        NavPathStraightener.SegmentWalkable(blocked, new NavPoint(3.5, 8.5), new NavPoint(13.5, 8.5), 0.0)
            .ShouldBeFalse("中间那一格是阻挡，直线段走不通 —— 走通了就是折线穿墙。");
        NavPathStraightener.SegmentWalkable(clear, new NavPoint(3.5, 8.5), new NavPoint(13.5, 8.5), 0.0)
            .ShouldBeTrue("没有阻挡时同一段走得通。");
    }
}
