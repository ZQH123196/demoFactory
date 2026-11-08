#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Pathfinding;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 寻路网格第一期的地基：入参口径、边界即墙、两个版本号分开、跨空间当场抛。
///
/// <para>这些读数原先只存在于原型的算术里（<c>BattleContract.ClampToArena</c> 那种事后夹取），
/// 出了「这个单位为什么没走到那儿」只能靠出图肉眼看。现在「哪里能过」是一张可逐格断言的表。</para>
/// </summary>
public class NavGridTests
{
    private static readonly SpaceId Deck = new("巨兽甲板");
    private static readonly SpaceId Arena = new("竞技场");

    private static NavGridSpec Spec(
        double cell = 1.0,
        int cellsX = 8,
        int cellsZ = 8,
        IReadOnlyList<NavCell>? blocked = null,
        SpaceId? space = null,
        double originX = 0.0,
        double originZ = 0.0)
        => new NavGridSpec
        {
            Space = space ?? Deck,
            CellMeters = cell,
            CellsX = cellsX,
            CellsZ = cellsZ,
            OriginX = originX,
            OriginZ = originZ,
            BlockedCells = blocked ?? Array.Empty<NavCell>(),
        };

    [Fact]
    public void 格长缺省不留_非正与非有限当场抛()
    {
        // NavGridSpec 的格长是 required：漏给是编译期断。这里判的是「给了但不成形状」那半，
        // 以及兜 0 那条病 —— 兜 0 会让净空场恒为 0、什么都能站（「桥只转发」）。
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(Spec(cell: 0)))
            .Message.ShouldContain("格长", Case.Sensitive, "报出来要能指到是哪一格参数不对，不是一句 ArgumentOutOfRange");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(Spec(cell: -1)));
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(Spec(cell: double.NaN)));
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(Spec(cell: double.PositiveInfinity)));
    }

    [Fact]
    public void 格数至少一_零格当场抛()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(Spec(cellsX: 0)))
            .Message.ShouldContain("格数", Case.Sensitive, "边界是网格的定义之一，0 格网格报不出「边界在哪」");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(Spec(cellsZ: 0)));
    }

    [Fact]
    public void 阻挡表必填_漏给当场抛而空表是合法声明()
    {
        // 空表 = 明确声明「这片没有地形阻挡」，是一等状态；漏给才是「没声明」。
        // 把两者分成两种形状，才挡得住「漏了这一类等于墙不存在」。
        Should.Throw<ArgumentNullException>(() => new NavGrid(new NavGridSpec
        {
            Space = Deck,
            CellMeters = 1.0,
            CellsX = 4,
            CellsZ = 4,
            OriginX = 0.0,
            OriginZ = 0.0,
            BlockedCells = null!,
        })).Message.ShouldContain("阻挡表", Case.Sensitive, "报出来要指到漏的是哪一份");

        var empty = new NavGrid(Spec(cellsX: 4, cellsZ: 4, blocked: Array.Empty<NavCell>()));
        empty.CellsX.ShouldBe(4);
        empty.IsBlocked(new NavCell(1, 1)).ShouldBeFalse("空阻挡表 = 明确没有地形阻挡，不是「默认都封」");
    }

    [Fact]
    public void 阻挡表越界与重复当场抛()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(Spec(blocked: new[] { new NavCell(8, 0) })))
            .Message.ShouldContain("越界", Case.Sensitive, "网格外那一圈是虚拟阻挡，替它封堵等于把墙砌在场地上");
        Should.Throw<ArgumentOutOfRangeException>(() => new NavGrid(
            Spec(blocked: new[] { new NavCell(2, 2), new NavCell(2, 2) })))
            .Message.ShouldContain("两次", Case.Sensitive, "同一格封两次会让「这份阻挡表是什么」读不出来");
    }

    [Fact]
    public void 跨空间请求当场抛并带出双方id()
    {
        var grid = new NavGrid(Spec(space: Deck));

        var ex = Should.Throw<InvalidOperationException>(() => grid.RequireSameSpace(Arena));
        ex.Message.ShouldContain("巨兽甲板", Case.Sensitive, "「哪两个空间」是唯一有用的现场 —— 只报「跨空间了」agent 无从下手");
        ex.Message.ShouldContain("竞技场", Case.Sensitive);
        ex.Message.ShouldContain("换算归宿主", Case.Sensitive, "抛的理由要写明：空间不带变换，本层没有做换算需要的东西");

        Should.NotThrow(() => grid.RequireSameSpace(Deck));
    }

    [Fact]
    public void 空间与网格id对不上当场抛()
    {
        var grid = new NavGrid(Spec(space: Deck));
        var ex = Should.Throw<InvalidOperationException>(() => new NavSpace(Arena, grid));
        ex.Message.ShouldContain("巨兽甲板", Case.Sensitive, "装配期就把两个 id 都带出来，比运行期报一句「跨空间」强");
        ex.Message.ShouldContain("竞技场", Case.Sensitive);
    }

    [Fact]
    public void 空间挂着网格并转发两个版本读数()
    {
        var grid = new NavGrid(Spec(space: Deck));
        var space = new NavSpace(Deck, grid);

        space.Grid.ShouldBeSameAs(grid);
        grid.SetDynamicCost(new NavCell(3, 3), 0.2);
        space.DynamicVersion.ShouldBe(1);
        space.StaticVersion.ShouldBe(0);
    }

    [Fact]
    public void 点落格界外当场抛界内给格与局部坐标()
    {
        var grid = new NavGrid(Spec(cell: 0.5, cellsX: 8, cellsZ: 8));

        var local = grid.RequireCellOf(new NavPoint(1.25, 2.75));
        local.Cell.ShouldBe(new NavCell(2, 5), "1.25 米 / 0.5 米一格 = 第 2 列的格内 0.5");
        local.Fx.ShouldBe(0.5, 1e-12);
        local.Fz.ShouldBe(0.5, 1e-12);

        Should.Throw<ArgumentOutOfRangeException>(() => grid.RequireCellOf(new NavPoint(-0.1, 0)))
            .Message.ShouldContain("网格外", Case.Sensitive, "越界是调用方的错，不是一个该被兜住的读数");
        Should.Throw<ArgumentOutOfRangeException>(() => grid.RequireCellOf(new NavPoint(4.0, 0)), "右边界那一线已经在场地之外");
    }

    [Fact]
    public void 边界是网格的定义一部分_贴着边就站不下不需要宿主夹取()
    {
        // 8×8、1 米一格、一格地形阻挡都没有。网格外那一圈是虚拟阻挡，
        // 于是边界附近的净空自动被压住 —— 竞技场矩形那种「不许出去」不再由宿主事后夹。
        var grid = new NavGrid(Spec());

        grid.IsBlocked(new NavCell(0, 0)).ShouldBeFalse("界内第一格不是阻挡；界外那一圈才是");
        grid.IsBlocked(new NavCell(-1, 0)).ShouldBeTrue("网格外那一圈按阻挡算");
        grid.IsBlocked(new NavCell(8, 7)).ShouldBeTrue();

        grid.Clearance.At(0, 0).ShouldBe(1.0, 1e-12, "到界外虚拟阻挡那一格中心的距离正好一格");
        grid.CanStandAt(new NavPoint(0.5, 0.5), 1.0).ShouldBeTrue("格心离虚拟墙一格，1 米半径刚好放得下");
        grid.CanStandAt(new NavPoint(0.05, 0.05), 1.0).ShouldBeFalse("贴边那个点上净空只剩 0.36 米 —— 宿主侧一个夹取都没写");
        grid.CanStandAt(new NavPoint(0.05, 0.05), 0.36).ShouldBeTrue("半径是请求递进来的标量，层里没有单位表");

        grid.Contains(new NavPoint(8.0, 4.0)).ShouldBeFalse("右边界那一线算界外");
        grid.Contains(new NavPoint(7.999, 4.0)).ShouldBeTrue();
    }

    [Fact]
    public void 界外一律站不下_连零半径也站不下()
    {
        var grid = new NavGrid(Spec());
        grid.CanStandAt(new NavPoint(-0.5, 4.0), 0.0).ShouldBeFalse("界外是虚拟阻挡，零半径也不该站得住");
        grid.CanStandAt(new NavPoint(20.0, 20.0), 0.4).ShouldBeFalse();
    }

    [Fact]
    public void 静态版本与动态版本分开_改一个不动另一个()
    {
        var grid = new NavGrid(Spec());
        grid.StaticVersion.ShouldBe(0);
        grid.DynamicVersion.ShouldBe(0);

        grid.SetBlocked(new NavCell(2, 2), true);
        grid.StaticVersion.ShouldBe(1, "地形变了，静态版本 +1");
        grid.DynamicVersion.ShouldBe(0, "动态层一个数都不许动");

        grid.SetDynamicCost(new NavCell(3, 3), 0.2);
        grid.DynamicVersion.ShouldBe(1, "动态层变了，动态版本 +1");
        grid.StaticVersion.ShouldBe(1, "动态层变了，静态版本不许动");

        grid.SetPassCost(new NavCell(4, 4), 2.5);
        grid.StaticVersion.ShouldBe(2, "静态通过成本是静态地形那一档");
        grid.DynamicVersion.ShouldBe(1);
    }

    [Fact]
    public void 值没真变就不动版本号()
    {
        var grid = new NavGrid(Spec(blocked: new[] { new NavCell(2, 2) }));
        var static0 = grid.StaticVersion;

        grid.SetBlocked(new NavCell(2, 2), true);
        grid.StaticVersion.ShouldBe(static0, "同一格封两次不是新地形 —— 动版本号只会让在手路径白白重算");
        grid.SetPassCost(new NavCell(1, 1), 1.0);
        grid.StaticVersion.ShouldBe(static0, "写成 1.0 就是基准 1.0，不是「改成了 1.0」");
        grid.SetDynamicCost(new NavCell(1, 1), 1.0);
        grid.DynamicVersion.ShouldBe(0);
    }

    [Fact]
    public void 通过成本读数_静态与动态合成一个标量()
    {
        var grid = new NavGrid(Spec());

        grid.PassCostAt(new NavCell(1, 1)).ShouldBe(1.0, 1e-12, "1 是基准");
        grid.DynamicCostAt(new NavCell(1, 1)).ShouldBe(1.0, 1e-12, "动态层的缺省是「不减速」—— 那是那层自己的语义缺省");
        grid.EffectiveCostAt(new NavCell(1, 1)).ShouldBe(1.0, 1e-12);

        grid.SetPassCost(new NavCell(1, 1), 2.5);
        grid.SetDynamicCost(new NavCell(1, 1), 0.2);
        grid.EffectiveCostAt(new NavCell(1, 1)).ShouldBe(0.5, 1e-12, "减速是乘性：2.5 × 0.2");

        grid.PassCostAt(new NavCell(-1, 0)).ShouldBe(0.0, "走不过去的东西不报价");
        grid.EffectiveCostAt(new NavCell(-1, 0)).ShouldBe(0.0);
    }

    [Fact]
    public void 写入口与站位读数遇到不成形状的数当场抛()
    {
        var grid = new NavGrid(Spec());

        Should.Throw<ArgumentOutOfRangeException>(() => grid.SetBlocked(new NavCell(8, 0), true))
            .Message.ShouldContain("虚拟阻挡", Case.Sensitive, "界外那一圈不是可以逐格改写的地形");
        Should.Throw<ArgumentOutOfRangeException>(() => grid.SetPassCost(new NavCell(1, 1), 0))
            .Message.ShouldContain("正", Case.Sensitive, "加不动不表阻挡：走不动是 SetBlocked 那一档，成本 0 读起来像免费");
        Should.Throw<ArgumentOutOfRangeException>(() => grid.SetDynamicCost(new NavCell(1, 1), -1));
        Should.Throw<ArgumentOutOfRangeException>(() => grid.CanStandAt(new NavPoint(1, 1), double.NaN))
            .Message.ShouldContain("半径", Case.Sensitive, "半径是请求递进来的标量，兜 0 等于「所有单位都算零半径」");
    }

    [Fact]
    public void 空间名不能是空的()
    {
        Should.Throw<ArgumentException>(() => new SpaceId("  "))
            .Message.ShouldContain("纯空白", Case.Sensitive, "空 id 报出来的跨空间错指向一个不存在的东西");
        Should.Throw<ArgumentException>(() => new SpaceId(null!));
    }
}
