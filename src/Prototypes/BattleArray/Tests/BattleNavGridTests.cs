#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace DemoFactory.Prototypes.BattleArray.Tests;

/// <summary>
/// 工单 08：排兵布阵接寻路层的<b>场地那一半</b> —— 竞技场 ±12 米收成网格边界、
/// 格长定档（1.0 米）与「档与表钉死」 的两边各校验一道、净空保守量的实测数。
///
/// <para><b>这一份只钉场地，不钉走位</b>。走位（追实体 / 原地不动 / 站位冲突三个命令）
/// 与技能层激活检查那一半的读数在 <see cref="BattleNavMoveTests"/>；
/// 「退休手写趋近与拥挤分离」那一格因新旧两条路<b>不逐字相同</b>（float 与 double 的末位差，
/// 逐字读数见 <c>docs/GugaFramework/寻路层.md</c> 7 节）而未落地，处置归用户裁决。</para>
///
/// <para><b>断言强度只比检查点快照</b>（「断言比快照」）。逐字读数都是先跑探针读出来的再写成断言
/// （探针工程 <c>.cache/Pathfinding/Probe08/</c>），不是照着实现推的。</para>
/// </summary>
public class BattleNavGridTests
{
    private const double Cap = 1e-9;

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // === 格长定档（工单 08 验收项之一）===

    [Fact]
    public void 格长定档一_零米_格数正好罩住竞技场()
    {
        // 探针读数：24 格 × 1.0 米 = 24.0 米 = ArenaHalfWidth × 2。
        (BattleNavGrid.Cells * BattleNavGrid.CellMeters).ShouldBe(
            BattleContract.ArenaHalfWidth * 2.0,
            "格数 × 格长必须正好是竞技场边长 —— 少一格是「墙内还有一条能走的空隙」，多一格是「场外还有一格能站」。");

        BattleNavGrid.CellMeters.ShouldBe(1.0, "工单 08 定档：1.0 米（24×24 = 576 格，规模微不足道）");
        BattleNavGrid.Cells.ShouldBe(24, "1.0 米一格罩 24 米见方");
    }

    [Fact]
    public void 格长定档二_子步上限大于最大每步位移_所以每步一刀切完()
    {
        // 探针读数：0.12 / 0.14 / 0.15 / 0.32 米都只切 1 份。
        // 注：兵种卡的移速是 float（`TroopCard.Speed`），升成 double 再乘 0.1 秒会带上 float 残差
        // —— 实测 0.32000000953674324，不是 0.32。这与工单 08 记的「旧路 float / 新路 double 末位差」同源，
        // 这里照实钉住逐字值，不四舍五入成一个好看的数（钉一个舍入后的数就等于把残差藏起来了）。
        var worstStep = BattleNavGrid.MaxStepMeters();
        worstStep.ShouldBe(0.32000000953674324, 1e-12, "骑手 3.2f 米每秒 × 0.1f 秒（「时间基准」），float 残差照实");

        var grid = BattleNavGrid.Build();
        var cap = NavSubstepper.SubstepCapMeters(grid);
        cap.ShouldBe(0.5, "子步上限 = 0.5 × 格长");
        cap.ShouldBeGreaterThan(worstStep, "上限必须大于最大每步位移，否则这一步要切多份、引入额外取整");

        foreach (var kind in BattleContract.Kinds)
        {
            var step = BattleNavGrid.SpeedOf(kind) * BattleContract.StepSeconds;
            NavSubstepper.PlanCount(grid, step).ShouldBe(
                1, $"{kind} 每步 {N(step)} 米应切 1 份 —— 这一场子步是空转（前置账结论二已量过）");
        }
    }

    [Fact]
    public void 格长定档三_最大每步位移不超过一格_否则隧穿那一档失效()
    {
        BattleNavGrid.MaxStepMeters().ShouldBeLessThanOrEqualTo(
            BattleNavGrid.CellMeters, "格长校验第二条：格长 ≥ 最大每步位移（能力前提，不是性能偏好）");
    }

    // === 「档与表钉死」：两边各校验一道 ===

    [Fact]
    public void 规则8_11_宿主侧那一道_三条全过()
    {
        Should.NotThrow(BattleNavGrid.RequirePinnedCellLength, "格长档与场地尺寸、移速表、身体半径表都对得上");
    }

    [Fact]
    public void 规则8_11_层那一侧_格长非正当场抛不兜0()
    {
        // 层那一侧的口径是「格长为正、为有限数」，与宿主那一侧各管各的形状。
        Should.Throw<ArgumentOutOfRangeException>(
            () => new NavGrid(new NavGridSpec
            {
                Space = BattleNavGrid.Space,
                CellMeters = 0.0,   // 兜 0 会让净空场恒为 0、什么都能站
                CellsX = 4,
                CellsZ = 4,
                OriginX = 0.0,
                OriginZ = 0.0,
                BlockedCells = Array.Empty<NavCell>(),
            }),
            "格长 0 当场抛（「桥只转发」 不许兜默认值）");

        Should.Throw<ArgumentOutOfRangeException>(
            () => new NavGrid(new NavGridSpec
            {
                Space = BattleNavGrid.Space,
                CellMeters = double.NaN,
                CellsX = 4,
                CellsZ = 4,
                OriginX = 0.0,
                OriginZ = 0.0,
                BlockedCells = Array.Empty<NavCell>(),
            }),
            "格长非有限数当场抛");
    }

    [Fact]
    public void 规则8_11_边界那一线算界外_所以半宽十二米那一线站不下()
    {
        var grid = BattleNavGrid.Build();

        // 探针读数：世界 z = 12.0 界内 False、净空 0；z = 11.999 界内 True、净空 0.2936。
        grid.Contains(BattleNavGrid.ToGrid(0.0f, 12.0f)).ShouldBeFalse("边界那一线算界外（口径钉死是为了重放逐字）");
        grid.Contains(BattleNavGrid.ToGrid(0.0f, 12.001f)).ShouldBeFalse("界外一律站不下");
        grid.Contains(BattleNavGrid.ToGrid(0.0f, 11.999f)).ShouldBeTrue("贴内边界那一线仍算界内");
    }

    // === 净空保守量实测（工单 08 验收项之一：量出一个数）===

    [Fact]
    public void 保守量实测_这张网格上的最大保守量是一点二一七米()
    {
        var grid = BattleNavGrid.Build();
        var (worst, worstAt) = WorstConservativeGap(grid);

        // 探针读数（24×24、1 米一格、无地形阻挡，扫 106040 个点）：
        // 最大保守量 1.2171027843833055 米，发生在场地正中附近。
        worst.ShouldBe(1.2171027843833055, 1e-9,
            "工单 08 实测数：这张真实网格上的最大保守量（第一期推的上界是 √2 ≈ 1.4142 格）");
        (worst <= Math.Sqrt(2.0) * BattleNavGrid.CellMeters).ShouldBeTrue(
            $"保守量 {worst} 不许超过第一期推的上界 √2 × 格长 —— 超了说明保守下界那一格算错了");

        // 真值由本用例里一份独立的暴力计算给出（扫遍界外虚拟阻挡那一圈 + 全部已封格），
        // 不复用被测实现那条读法 —— 照第一期那条纪律。
        var truth = BruteForceTruthAt(grid, worstAt);
        (truth >= worst).ShouldBeTrue("保守下界必须 ≤ 真值（它是下界）");
    }

    [Fact]
    public void 保守量实测_布阵最远那格站得下最大身体半径_所以半径进净空咬不到()
    {
        var grid = BattleNavGrid.Build();
        var (x, y) = BattleNavGrid.FarthestDeploymentCellCenter();

        // 探针读数：世界 (0, 11.35)（离墙 0.65 米）净空下界 1.013845853834198。
        // 注：格心那格（z = 11）的下界是 1.2928932188134525，z = 11.35 是布阵最远可达点。
        var clearance = BattleNavGrid.ClearanceAtFarthestDeploymentCell(grid);
        clearance.ShouldBeGreaterThan(BattleNavGrid.MaxBodyRadiusMeters(),
            "布阵最远那格（离墙 0.65 米）的净空下界必须大于骑手 0.75 米 —— "
            + "「半径进净空」这一格在排兵布阵咬不到任何既有位置（前置账结论一）");

        grid.CanStandAt(BattleNavGrid.ToGrid(x, y), BattleNavGrid.MaxBodyRadiusMeters()).ShouldBeTrue(
            "起手就站得下最大半径，否则第一条求解直接 StartBlocked");
    }

    [Fact]
    public void 保守量实测_贴到内边界时骑手站不下_所以这一档碰不到()
    {
        var grid = BattleNavGrid.Build();

        // 探针读数：z = 11.999 处净空下界 0.29359997168742813，骑手 0.75 站不下。
        var nearWall = BattleNavGrid.ToGrid(0.0f, 11.999f);
        grid.Contains(nearWall).ShouldBeTrue("仍在界内");
        grid.CanStandAt(nearWall, BattleNavGrid.MaxBodyRadiusMeters()).ShouldBeFalse(
            "贴到内边界时净空不够 —— 布阵最远只到 11.35，这一档碰不到，但它证明边界那一圈真的在挡人");
    }

    [Fact]
    public void 净空保守下界_永不误判能站_四个兵种半径各扫一遍()
    {
        var grid = BattleNavGrid.Build();
        var radii = new[] { 0.40, 0.45, 0.55, 0.75 };
        var checkedPoints = 0;
        var standHits = 0;

        // 只扫单位真会站的那一块（|x| ≤ 6、|z| ≤ 11），0.05 米一步。
        for (var x = -6.0; x <= 6.0; x += 0.05)
        {
            for (var z = -11.0; z <= 11.0; z += 0.05)
            {
                var p = BattleNavGrid.ToGrid((float)x, (float)z);
                if (!grid.Contains(p)) continue;
                checkedPoints++;

                foreach (var radius in radii)
                {
                    if (!grid.CanStandAt(p, radius)) continue;
                    standHits++;

                    // 「下界 ≥ 半径 ⇒ 真值一定 ≥ 半径」：这一条是保守下界存在的全部理由。
                    (BruteForceTruthAt(grid, p) >= radius).ShouldBeTrue($"下界说 {N(radius)} 米半径站得下，真值就必须够 —— 保守过头可以，误判能站不行");
                }
            }
        }

        checkedPoints.ShouldBeGreaterThan(100000, $"扫点太少（{checkedPoints}），这一条会假绿");
        standHits.ShouldBeGreaterThan(400000, $"半径判读次数太少（{standHits}），覆盖面没打开");
    }

    // === 坐标换算（ADR 0009）===

    [Fact]
    public void 坐标换算_世界原点映射到网格正中那一格()
    {
        // 竞技场以中线为 0（±12 米），网格原点在角上 ⇒ 换算就是各加 12。
        var center = BattleNavGrid.ToGrid(0.0f, 0.0f);
        center.X.ShouldBe(12.0, Cap, "世界 X=0 → 网格 X=12（格心在下标+0.5 处）");
        center.Z.ShouldBe(12.0, Cap, "世界 Y=0 → 网格 Y=12");

        var back = BattleNavGrid.ToWorld(center);
        back.X.ShouldBe(0.0f, "换算过去再换算回来要回到原处");
        back.Y.ShouldBe(0.0f, "换算过去再换算回来要回到原处");
    }

    [Fact]
    public void 坐标换算_布阵最远点落在界内那一侧()
    {
        var (x, y) = BattleNavGrid.FarthestDeploymentCellCenter();
        var grid = BattleNavGrid.Build();
        grid.Contains(BattleNavGrid.ToGrid(x, y)).ShouldBeTrue(
            $"布阵最远点 ({N(x)}, {N(y)}) 必须落在网格界内，否则起手就界外");
    }

    // === 内部：两份独立算读数的工具 ===

    /// <summary>逐点扫出最大保守量（真值 − 下界）及其位置。</summary>
    private static (double Worst, NavPoint At) WorstConservativeGap(NavGrid grid)
    {
        var worst = 0.0;
        var at = new NavPoint(0, 0);
        for (var x = -6.0; x <= 6.0; x += 0.05)
        {
            for (var z = -11.0; z <= 11.0; z += 0.05)
            {
                var p = BattleNavGrid.ToGrid((float)x, (float)z);
                if (!grid.Contains(p)) continue;
                var gap = BruteForceTruthAt(grid, p) - grid.Clearance.ConservativeLowerBoundAt(p);
                if (gap <= worst) continue;
                worst = gap;
                at = p;
            }
        }

        return (worst, at);
    }

    /// <summary>
    /// 真值 = 到最近阻挡格中心的距离（阻挡格 = 已封格 + 网格外那一圈虚拟阻挡）。
    /// <b>不复用被测实现</b>：净空场那份表与保守下界那一格都不参与这条计算。
    /// </summary>
    private static double BruteForceTruthAt(NavGrid grid, NavPoint p)
    {
        var best = double.PositiveInfinity;
        for (var cx = -1; cx <= grid.CellsX; cx++)
        {
            for (var cz = -1; cz <= grid.CellsZ; cz++)
            {
                var cell = new NavCell(cx, cz);
                var inside = cx >= 0 && cx < grid.CellsX && cz >= 0 && cz < grid.CellsZ;
                if (inside && !grid.IsBlocked(cell)) continue;
                var d = p.DistanceTo(grid.CenterOf(cell));
                if (d < best) best = d;
            }
        }

        return best;
    }
}
