#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 净空场与保守下界 —— 这一期最要紧的一条性质是「<strong>永不误判能站</strong>」。
///
/// <para>判据用的不是真值而是保守下界，所以它可能保守过头；但它<b>下界不超过真值</b>这件事必须能被逐点验：
/// 用例里带一份<strong>独立的真值计算</strong>（暴力扫全部阻挡格与界外虚拟阻挡），
/// 拿它当下界那条不等式的右边。断言写在真值那一侧，不写在被测实现那一侧。</para>
/// </summary>
public class ClearanceFieldTests
{
    private static readonly SpaceId Deck = new("巨兽甲板");

    private static NavGrid Grid(
        IReadOnlyList<NavCell> blocked,
        int cells = 9,
        double cell = 1.0,
        double originX = 0.0,
        double originZ = 0.0)
        => new(new NavGridSpec
        {
            Space = Deck,
            CellMeters = cell,
            CellsX = cells,
            CellsZ = cells,
            OriginX = originX,
            OriginZ = originZ,
            BlockedCells = blocked,
        });

    /// <summary>
    /// 真值：<c>d(p) = min_o |p − o|</c>，o 取遍全部地形阻挡格与网格外那一圈虚拟阻挡。
    /// 暴力扫，不复用被测实现 —— 复用就没有独立判定这一层了。
    /// </summary>
    private static double TrueClearance(NavGrid grid, NavPoint p)
    {
        var best = double.MaxValue;
        for (var cx = -1; cx <= grid.CellsX; cx++)
        {
            for (var cz = -1; cz <= grid.CellsZ; cz++)
            {
                var inside = cx >= 0 && cx < grid.CellsX && cz >= 0 && cz < grid.CellsZ;
                if (inside && !grid.IsBlocked(new NavCell(cx, cz)))
                {
                    continue;
                }

                var d = p.DistanceTo(grid.CenterOf(new NavCell(cx, cz)));
                if (d < best)
                {
                    best = d;
                }
            }
        }

        return best;
    }

    [Fact]
    public void 净空是精确欧氏距离_不是chamfer()
    {
        // 场地正中立一格阻挡，周围读数应当是可读值。3-4 chamfer 会把对角读成 1.5 或 2，
        // 那这一格就不该叫「距离」，纸面上也没法验。
        var grid = Grid(new[] { new NavCell(4, 4) });
        var root2 = Math.Sqrt(2.0);
        var root5 = Math.Sqrt(5.0);

        grid.Clearance.At(4, 4).ShouldBe(0.0, 1e-12, "阻挡格自己的净空是 0");
        grid.Clearance.At(5, 4).ShouldBe(1.0, 1e-12);
        grid.Clearance.At(5, 5).ShouldBe(root2, 1e-12, "对角是精确的 √2，不是 chamfer 的 1.5");
        grid.Clearance.At(6, 6).ShouldBe(Math.Sqrt(8.0), 1e-12);
        grid.Clearance.At(5, 6).ShouldBe(root5, 1e-12, "跨两格一格的对角是 √5 —— 这一格离界外虚拟阻挡还有 3 米，读数没有被边界压住");
        grid.Clearance.At(0, 0).ShouldBe(1.0, 1e-12, "界外那一圈是虚拟阻挡：贴边那格离它正好一格");
        grid.Clearance.At(-1, 0).ShouldBe(0.0, "界外虚拟阻挡自己的净空是 0");
    }

    [Fact]
    public void 格长缩放净空读数()
    {
        // 同一个形状，格长 0.5 米 ⇒ 距离整体减半。净空场的分辨率就是格长，两处不许各算一套。
        var grid = Grid(new[] { new NavCell(4, 4) }, cell: 0.5);
        grid.Clearance.At(5, 5).ShouldBe(Math.Sqrt(2.0) * 0.5, 1e-12);
        grid.CenterOf(new NavCell(4, 4)).ShouldBe(new NavPoint(2.25, 2.25), "格心 = (下标 + 0.5) × 格长");
    }

    [Fact]
    public void 保守下界在凸墙角上与真值相等()
    {
        // 已知凸墙：场地正中立一格阻挡。取 (5, 5) —— 紧贴阻挡格东北角的格点。
        // 四格取的是本格 + fx ≤ 0.5 取上一格 / fz ≤ 0.5 取上一格，于是四格 = (5,5)(4,5)(5,4)(4,4)，
        // 算出来正好 = √2 / 2 = 真值：界是紧的，一厘米都没多让。
        var grid = Grid(new[] { new NavCell(4, 4) });
        var p = new NavPoint(5.0, 5.0);

        var bound = grid.Clearance.ConservativeLowerBoundAt(p);
        var truth = TrueClearance(grid, p);

        bound.ShouldBe(Math.Sqrt(0.5), 1e-12, "凸墙角上界是紧的：下界 = √2/2");
        bound.ShouldBe(truth, 1e-12, "下界与真值在这个点上相等");
        TestLog.Ledger($"凸墙角 p={p}：下界={bound.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}，真值={truth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    [Fact]
    public void 下界可能比真值保守_偏出去几厘米()
    {
        var grid = Grid(new[] { new NavCell(4, 4) });
        var p = new NavPoint(5.0, 5.2);

        var bound = grid.Clearance.ConservativeLowerBoundAt(p);
        var truth = TrueClearance(grid, p);

        bound.ShouldBeLessThan(truth, "下界绝不许超过真值 —— 超过就是误判能站，单位会插进墙里");
        TestLog.Ledger(
            $"偏心 p={p}：下界={bound.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}，"
            + $"真值={truth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}，"
            + $"保守掉={(truth - bound).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)} 米");
    }

    [Fact]
    public void 凸墙逐点验_下界不误判能站()
    {
        // 凸墙取一个 L 形：(4,4)(4,5)(5,4) 三格，(4,4) 是凸进开阔地的那只墙角。
        // 在整片场地上按 0.25 米步长逐点扫，每点扫一串半径。
        // 判据只有一条，且写在下界那一侧：<c>CanStandAt 说能站 ⇒ 真值一定 ≥ 那个半径</c>。
        var grid = Grid(new[] { new NavCell(4, 4), new NavCell(4, 5), new NavCell(5, 4) });
        var radii = new[] { 0.0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
        var checkedPoints = 0;
        var stoodTimes = 0;
        var worstGap = 0.0;
        var worstAt = new NavPoint(0, 0);

        for (var x = 0.0; x < 9.0; x += 0.25)
        {
            for (var z = 0.0; z < 9.0; z += 0.25)
            {
                var p = new NavPoint(Math.Round(x, 2), Math.Round(z, 2));
                var truth = TrueClearance(grid, p);
                var bound = grid.Clearance.ConservativeLowerBoundAt(p);
                checkedPoints++;

                bound.ShouldBeLessThanOrEqualTo(truth + 1e-9, $"p={p} 的下界超过了真值 —— 那就是误判能站");

                foreach (var r in radii)
                {
                    if (!grid.CanStandAt(p, r))
                    {
                        continue;
                    }

                    stoodTimes++;
                    truth.ShouldBeGreaterThanOrEqualTo(
                        r - 1e-9,
                        $"p={p} 半径 {r} 米被判为站得下，而真值只有 {truth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} 米 —— 这条判据是「不误判能站」的全部依据");

                    var gap = truth - bound;
                    if (gap > worstGap)
                    {
                        worstGap = gap;
                        worstAt = p;
                    }
                }
            }
        }

        TestLog.Ledger(
            $"逐点验：{checkedPoints} 个点 × {radii.Length} 个半径，站得下 {stoodTimes} 次；"
            + $"最保守处 {worstAt} 差 {worstGap.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)} 米");

        // 保守量有上限，这个上限是推出来的不是估出来的：设 t = |p − 所在格心| ≤ √2/2（一格方框里的点到中心的最大距离），
        // 则 真值 ≥ 净空(本格) − t，于是 下界 ≥ 净空(本格) − t ≥ 真值 − 2t ≥ 真值 − √2。
        // 两条合起来就是 0 ≤ 真值 − 下界 ≤ √2 格 —— 上界保证不误判能站，下界保证不至于保守到没法用。
        worstGap.ShouldBeGreaterThanOrEqualTo(0.0, "下界超过真值 = 误判能站，这是这条判据唯一不许犯的错");
        worstGap.ShouldBeLessThanOrEqualTo(
            Math.Sqrt(2.0) + 1e-9,
            "保守量超过 √2 格 —— 推导给的上界是 √2 格，破了说明四格取法走偏了");
        stoodTimes.ShouldBeGreaterThan(0, "全判站不下 = 这条判据不可用（连格心都站不了）");
    }

    [Fact]
    public void 半径逐个递增时站位单调收缩()
    {
        // 判据随半径单调：半径变大只会站得更少。反过来漂就说明四格取法里混进了状态。
        var grid = Grid(new[] { new NavCell(4, 4) });
        var p = new NavPoint(4.5, 6.5);   // 阻挡格正南两格那格的格心，真值 2.0 米
        var stood = new List<bool>();
        for (var r = 0.0; r <= 3.0; r += 0.25)
        {
            stood.Add(grid.CanStandAt(p, r));
        }

        for (var i = 1; i < stood.Count; i++)
        {
            stood[i].ShouldBeLessThanOrEqualTo(stood[i - 1], $"半径 {i * 0.25} 米比 {((i - 1) * 0.25).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} 米站得更下 —— 判据不单调");
        }

        // 格心处下界与真值相等：0 到 2.0 米都站得下，2.0 之后站不下。
        stood.Take(9).ShouldAllBe(b => b, "格心的净空是 2.0 米，2.0 米以内的半径都该站得下");
        stood.Skip(9).ShouldAllBe(b => !b, "过了 2.0 米就站不下 —— 下界在格心处与真值相等，不该再多让");
    }

    [Fact]
    public void 点在网格外下界取零不抛()
    {
        // 避让可能把单位推出边界几厘米，游戏循环里不该因为一次推挤崩掉。
        var grid = Grid(Array.Empty<NavCell>());

        grid.Clearance.ConservativeLowerBoundAt(new NavPoint(-0.5, 4.0)).ShouldBe(0.0);
        grid.Clearance.ConservativeLowerBoundAt(new NavPoint(20.0, 20.0)).ShouldBe(0.0);
        grid.Clearance.ConservativeLowerBoundAt(new NavPoint(-1e9, 4.0)).ShouldBe(0.0);
        Should.NotThrow(() => grid.CanStandAt(new NavPoint(-0.5, 4.0), 0.4));
        grid.Contains(new NavPoint(-0.5, 4.0)).ShouldBeFalse("要判界外用 Contains，不靠下界那一格");
    }

    [Fact]
    public void 地形一改净空场跟着重算()
    {
        var grid = Grid(Array.Empty<NavCell>());
        grid.Clearance.At(4, 4).ShouldBe(5.0, 1e-12, "9×9 空场地：中央那格离界外虚拟阻挡 5 格");

        grid.SetBlocked(new NavCell(4, 4), true);

        grid.Clearance.At(4, 4).ShouldBe(0.0, 1e-12, "新封的那格净空归零");
        grid.Clearance.At(5, 4).ShouldBe(1.0, 1e-12, "邻格跟着压到一格 —— 净空场是地形的一部分，不是一张建好就不动的表");
        grid.StaticVersion.ShouldBe(1);
    }

    [Fact]
    public void 同一份地形两次算净空逐字相等()
    {
        // 确定性靠结构：同一份阻挡表算两次，一个数都不许差（重放要落到逐字那一档）。
        var blocked = new[] { new NavCell(2, 3), new NavCell(4, 6), new NavCell(7, 1) };
        var a = Grid(blocked);
        var b = Grid(blocked);

        for (var cx = 0; cx < a.CellsX; cx++)
        {
            for (var cz = 0; cz < a.CellsZ; cz++)
            {
                a.Clearance.At(cx, cz).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    .ShouldBe(
                        b.Clearance.At(cx, cz).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        $"({cx},{cz}) 两张表的读数分叉了");
            }
        }
    }
}
