#nullable enable
using System;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;
using DemoFactory.Prototypes.Template.Logic;
using Shouldly;
using Xunit;

namespace _Template.Tests;

/// <summary>
/// 三层接线样板的自检 —— 样板里那几条形状本身跑得通，不是一段没人执行过的注释。
///
/// 本文件只碰 <c>LayerWiring</c> 与公共层，不引用任何原型代码，所以复制到新原型后原样有效。
/// 新原型接上真世界之后把它删掉也行 —— 那时第一个真断言就接上了。
/// </summary>
public class LayerWiringTests
{
    [Fact]
    public void 场地注册口_格数格长与静态阻挡表照递进来的样子()
    {
        var blocked = new[] { new NavCell(0, 0) };
        var grid = LayerWiring.BuildSite(new SpaceId("样板场地"), 2.0, 3, 4, 0.0, 0.0, blocked);

        grid.CellsX.ShouldBe(3);
        grid.CellsZ.ShouldBe(4);
        grid.CellMeters.ShouldBe(2.0);
        grid.ExtentXmeters.ShouldBe(6.0, "格数 × 格长 = 场地边长（「档与表钉死」那一份数）");
        grid.IsBlocked(new NavCell(0, 0)).ShouldBeTrue("静态阻挡表里的那一格要封着");
        grid.IsBlocked(new NavCell(1, 1)).ShouldBeFalse("空表之外的地形照常能过");
    }

    [Fact]
    public void 坐标换算_两份成对_来回一趟回到原点()
    {
        var local = LayerWiring.ToGrid(7.0, -3.0, originX: -20.0, originZ: -30.0);
        var (x, z) = LayerWiring.ToWorld(local, originX: -20.0, originZ: -30.0);

        x.ShouldBe(7.0);
        z.ShouldBe(-3.0);
    }

    [Fact]
    public void 每步先落地形再推逻辑步_推几步时钟走几步_暂停时空转()
    {
        var clock = new GameClock();
        var grid = LayerWiring.BuildSite(new SpaceId("样板场地"), 1.0, 2, 2, 0.0, 0.0, Array.Empty<NavCell>());
        var terrain = new NavTerrainLink(grid, c => new NavCell(c.Col, c.Row));

        LayerWiring.AdvanceSteps(clock, terrain, construction: null, steps: 3);
        clock.Tick.ShouldBe(3);

        clock.Pause();
        LayerWiring.AdvanceSteps(clock, terrain, construction: null, steps: 5);
        clock.Tick.ShouldBe(3, "暂停中连地形都不读，步号不动");
    }

    [Fact]
    public void 五步装配_三件都能建出来_推步按次序落地形()
    {
        var clock = new GameClock();
        var assembly = new WorldAssembly(clock);
        var grid = LayerWiring.BuildSite(new SpaceId("样板场地"), 1.0, 4, 4, 0.0, 0.0, Array.Empty<NavCell>());

        var (stepper, construction, terrain) = LayerWiring.Assemble(
            assembly,
            new SpaceId("样板场地"),
            grid,
            new NavMoveTuning { StepSeconds = 0.1, LookAheadMeters = 2.0, StepSolveExpansions = 2000 },
            new FreePlacementRule(positionStep: 0.01),
            Array.Empty<Blueprint>(),
            c => new NavCell(c.Col, c.Row));

        stepper.ShouldNotBeNull();
        construction.ShouldNotBeNull();
        terrain.ShouldNotBeNull();

        // 第 ⑤ 步：推进归装配，不在本类里另抄一条。缺层守卫与推进次序都在那边。
        assembly.Advance(3).ShouldBeTrue();
        clock.Tick.ShouldBe(3, "推进归 WorldAssembly，样板不自己排次序");
    }

    [Fact]
    public void 五步装配_少一层当场抛_不静默建半套()
    {
        var clock = new GameClock();
        var assembly = new WorldAssembly(clock);

        // 只走完 ③（建造模块），没建走位流水线也没接地形接线。
        assembly.BuildConstruction(new FreePlacementRule(positionStep: 0.01), Array.Empty<Blueprint>());

        var threw = Should.Throw<InvalidOperationException>(() => assembly.Advance(1));
        threw.Message.ShouldContain("走位流水线");
        threw.Message.ShouldContain("地形接线");
        clock.Tick.ShouldBe(0, "缺层时一步都不该走");
    }

    [Fact]
    public void 动词并集_每一条都出自某一层那份表_且不重复()
    {
        LayerWiring.VerbNames.ShouldNotBeEmpty();
        LayerWiring.VerbNames.ShouldBeUnique();

        foreach (var verb in LayerWiring.VerbNames)
        {
            var known = Array.IndexOf(AbilityVerbs.Names, verb) >= 0
                        || Array.IndexOf(ConstructionVerbs.Names, verb) >= 0;
            known.ShouldBeTrue($"「{verb}」不在任何一层的名单里：并集里的名字只能从层那份表取");
        }

        LayerWiring.IsConstructionVerb(ConstructionVerbs.Place).ShouldBeTrue();
        LayerWiring.IsConstructionVerb(AbilityVerbs.Cast).ShouldBeFalse();
    }
}
